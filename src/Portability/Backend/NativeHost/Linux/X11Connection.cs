using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class X11Connection : IDisposable
{
    private readonly Thread _owner = Thread.CurrentThread;
    private readonly object _lifetime = new();
    private readonly Dictionary<string, nuint> _atoms = new(StringComparer.Ordinal);
    private readonly Dictionary<nuint, List<Action<XEvent>>> _windows = new();
    private readonly Dictionary<int, List<Action<XEvent>>> _eventTypes = new();
    private readonly Dictionary<int, Action<XEvent>> _extensions = new();
    private readonly Queue<XEvent> _events = new();
    private readonly ConcurrentQueue<Action> _callbacks = new();
    private readonly X11Errors.Sink _errors = new();
    private ExceptionDispatchInfo? _callbackException;
    private bool _disposed, _pumpAttached, _preferCallback;
    private int _frameDepth, _callbackDepth, _wakePending;

    internal X11Connection()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The owned X11 backend currently supports Linux x64.");
        X11Errors.Initialize();
        Display = X11Native.XOpenDisplay(0);
        if (Display == 0) throw new InvalidOperationException("Unable to open the X11 display.");
        X11Errors.Register(Display, _errors);
        try
        {
            RootWindow = X11Native.XDefaultRootWindow(Display);
            ConnectionNumber = X11Native.XConnectionNumber(Display);
            MainContext = OwnedGlib.g_main_context_new();
            if (MainContext == 0) throw new InvalidOperationException("Unable to create a private GLib context.");
            // GIO operations started on the owner thread bind their completions to this context.
            OwnedGlib.g_main_context_push_thread_default(MainContext);
        }
        catch
        {
            if (MainContext != 0) OwnedGlib.g_main_context_unref(MainContext);
            X11Native.XCloseDisplay(Display);
            X11Errors.Unregister(Display);
            throw;
        }
    }

    internal nint Display { get; }
    internal nuint RootWindow { get; }
    internal int ConnectionNumber { get; }
    internal nint MainContext { get; }
    internal bool HasPendingInput
    {
        get
        {
            VerifyOwner();
            ThrowPendingException();
            return !_callbacks.IsEmpty || _events.Count != 0 || X11Native.XEventsQueued(Display, 1) != 0;
        }
    }

    internal nuint GetAtom(string name)
    {
        VerifyOwner();
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_atoms.TryGetValue(name, out var atom)) _atoms.Add(name, atom = X11Native.XInternAtom(Display, name, 0));
        return atom;
    }
    internal void RegisterWindow(nuint window, Action<XEvent> handler) { VerifyOwner(); AddHandler(_windows, window, handler); }
    internal void UnregisterWindow(nuint window) { VerifyOwner(); if (_windows.Remove(window, out var handlers)) handlers.Clear(); }
    internal void UnregisterWindow(nuint window, Action<XEvent> handler) { VerifyOwner(); RemoveHandler(_windows, window, handler); }
    internal void RegisterEvent(int type, Action<XEvent> handler) { VerifyOwner(); AddHandler(_eventTypes, type, handler); }
    internal void UnregisterEvent(int type, Action<XEvent> handler) { VerifyOwner(); RemoveHandler(_eventTypes, type, handler); }
    private static void AddHandler<TKey>(Dictionary<TKey, List<Action<XEvent>>> table, TKey key, Action<XEvent> handler) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!table.TryGetValue(key, out var handlers)) table.Add(key, handlers = new());
        handlers.Add(handler);
    }
    private static void RemoveHandler<TKey>(Dictionary<TKey, List<Action<XEvent>>> table, TKey key, Action<XEvent> handler) where TKey : notnull
    {
        if (!table.TryGetValue(key, out var handlers)) return;
        handlers.Remove(handler);
        if (handlers.Count == 0) table.Remove(key);
    }
    private static void InvokeSnapshot(List<Action<XEvent>>? handlers, Action<XEvent>[]? snapshot, XEvent nativeEvent)
    {
        if (handlers == null || snapshot == null) return;
        foreach (var handler in snapshot)
            if (handlers.Contains(handler)) handler(nativeEvent);
    }
    internal void RegisterExtension(int opcode, Action<XEvent> handler) { VerifyOwner(); ArgumentNullException.ThrowIfNull(handler); _extensions.Add(opcode, handler); }
    internal void UnregisterExtension(int opcode) { VerifyOwner(); _extensions.Remove(opcode); }
    internal void Flush() { VerifyOwner(); X11Native.XFlush(Display); ThrowPendingException(); }
    internal void Synchronize() { VerifyOwner(); X11Native.XSync(Display, 0); ThrowPendingException(); }

    // Foreign selection requestors can disappear between ownership checks and property writes.
    internal bool TryForeignRequest(Action nativeRequests)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(nativeRequests);
        Synchronize();
        var first = X11Native.XNextRequest(Display);
        nativeRequests();
        var end = X11Native.XNextRequest(Display);
        X11Native.XSync(Display, 0);
        var succeeded = true;
        var count = _errors.Errors.Count;
        for (var i = 0; i < count && _errors.Errors.TryDequeue(out var error); i++)
        {
            if (error.ErrorCode == 3 && unchecked(error.Serial - first) < unchecked(end - first)) succeeded = false;
            else _errors.Errors.Enqueue(error);
        }
        ThrowPendingException();
        return succeeded;
    }

    // Queries for foreign or recently destroyed windows never share the primary display's error queue.
    internal bool TryGetWindowAttributes(nuint window, out XWindowAttributes attributes)
    {
        VerifyOwner();
        attributes = default;
        var query = X11Native.XOpenDisplay(0);
        if (query == 0) return false;
        var errors = new X11Errors.Sink { FirstSerial = X11Native.XNextRequest(query), LastSerial = X11Native.XNextRequest(query) };
        X11Errors.Register(query, errors);
        try
        {
            var result = X11Native.XGetWindowAttributes(query, window, out attributes);
            X11Native.XSync(query, 0);
            return result != 0 && errors.Errors.IsEmpty;
        }
        finally
        {
            X11Native.XCloseDisplay(query);
            X11Errors.Unregister(query);
        }
    }

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _callbacks.Enqueue(callback);
            WakeUnderLock();
        }
    }
    internal void Wake()
    {
        lock (_lifetime)
        {
            if (!_disposed) WakeUnderLock();
        }
    }
    private void WakeUnderLock()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0) OwnedGlib.g_main_context_wakeup(MainContext);
    }
    internal bool ConsumeWake() => Interlocked.Exchange(ref _wakePending, 0) != 0;

    internal int DispatchBatch(int maximum = 32)
    {
        VerifyOwner();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        ThrowPendingException();
        QueueNativeEvents(maximum);
        var dispatched = 0;
        while (dispatched < maximum)
        {
            if ((_preferCallback || _events.Count == 0) && _callbacks.TryDequeue(out var callback))
            {
                _preferCallback = false;
                _callbackDepth++;
                try { callback(); }
                finally { _callbackDepth--; }
            }
            else if (_events.TryDequeue(out var nativeEvent))
            {
                _preferCallback = true;
                try
                {
                    _eventTypes.TryGetValue(nativeEvent.Type, out var typed);
                    _windows.TryGetValue(nativeEvent.Any.Window, out var windows);
                    var typedSnapshot = typed?.ToArray();
                    var windowSnapshot = windows?.ToArray();
                    _extensions.TryGetValue(nativeEvent.GenericCookie.Extension, out var extension);
                    _callbackDepth++;
                    try
                    {
                        InvokeSnapshot(typed, typedSnapshot, nativeEvent);
                        if (nativeEvent.Type == X11Native.GenericEvent)
                        {
                            if (extension != null && _extensions.GetValueOrDefault(nativeEvent.GenericCookie.Extension) == extension) extension(nativeEvent);
                        }
                        else InvokeSnapshot(windows, windowSnapshot, nativeEvent);
                    }
                    finally { _callbackDepth--; }
                }
                finally { ReleaseCookie(ref nativeEvent); }
            }

            else break;
            dispatched++;
        }
        ThrowPendingException();
        return dispatched;
    }

    internal void QueueNativeEvents(int maximum)
    {
        VerifyOwner();
        for (var count = 0; count < maximum && _events.Count < maximum && X11Native.XEventsQueued(Display, 1) > 0; count++)
        {
            X11Native.XNextEvent(Display, out var nativeEvent);
            // Xlib invalidates unclaimed cookie data at the next XNextEvent, including nested dispatch.
            if (nativeEvent.Type == X11Native.GenericEvent && X11Native.XGetEventData(Display, ref nativeEvent.GenericCookie) == 0)
                nativeEvent.GenericCookie.Data = 0;
            try { _events.Enqueue(nativeEvent); }
            catch { ReleaseCookie(ref nativeEvent); throw; }
        }
    }
    private void ReleaseCookie(ref XEvent nativeEvent)
    {
        if (nativeEvent.Type == X11Native.GenericEvent && nativeEvent.GenericCookie.Data != 0)
            X11Native.XFreeEventData(Display, ref nativeEvent.GenericCookie);
    }

    // Native GIO/source callbacks capture here and return to C; only the owner pump rethrows.
    internal void CaptureCallbackException(Exception exception)
        => Interlocked.CompareExchange(ref _callbackException, ExceptionDispatchInfo.Capture(exception), null);
    internal void ThrowPendingException()
    {
        Interlocked.Exchange(ref _callbackException, null)?.Throw();
        if (_errors.Errors.TryDequeue(out var error))
            throw new InvalidOperationException($"X11 error {error.ErrorCode} on request {error.RequestCode}.{error.MinorCode}, resource 0x{error.ResourceId:x}, serial {error.Serial}.");
    }
    internal void AttachPump() { VerifyOwner(); if (_pumpAttached) throw new InvalidOperationException("This X11 connection already has a dispatcher pump."); _pumpAttached = true; }
    internal void DetachPump() { VerifyOwner(); _pumpAttached = false; }
    internal void EnterFrame() { VerifyOwner(); _frameDepth++; }
    internal void ExitFrame() => _frameDepth--;
    internal void VerifyOwner()
    {
        if (_owner != Thread.CurrentThread) throw new InvalidOperationException("Use the X11 connection on its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_owner != Thread.CurrentThread) throw new InvalidOperationException("Dispose the X11 connection on its owner thread.");
        if (_disposed) return;
        if (_frameDepth != 0 || _callbackDepth != 0 || _pumpAttached)
            throw new InvalidOperationException("Dispose the pump after unwinding native frames before disposing its X11 connection.");
        lock (_lifetime) _disposed = true;
        while (_events.TryDequeue(out var nativeEvent)) ReleaseCookie(ref nativeEvent);
        _callbacks.Clear();
        _windows.Clear();
        _eventTypes.Clear();
        _extensions.Clear();
        OwnedGlib.g_main_context_pop_thread_default(MainContext);
        OwnedGlib.g_main_context_unref(MainContext);
        X11Native.XCloseDisplay(Display);
        X11Errors.Unregister(Display);
    }
}

internal static class X11Errors
{
    internal sealed class Sink
    {
        internal nuint FirstSerial, LastSerial = nuint.MaxValue;
        internal readonly ConcurrentQueue<XErrorEvent> Errors = new();
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, Sink> Displays = new();
    private static readonly X11Native.ErrorHandler Handler = Handle;
    private static X11Native.ErrorHandler? _previous;
    private static bool _initialized;
    internal static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            if (X11Native.XInitThreads() == 0) throw new InvalidOperationException("Unable to initialize Xlib threading.");
            var previous = X11Native.XSetErrorHandler(Handler);
            if (previous != 0) _previous = Marshal.GetDelegateForFunctionPointer<X11Native.ErrorHandler>(previous);
            _initialized = true;
        }
    }
    internal static void Register(nint display, Sink sink) { lock (Gate) Displays.Add(display, sink); }
    internal static void Unregister(nint display) { lock (Gate) Displays.Remove(display); }
    private static int Handle(nint display, ref XErrorEvent error)
    {
        try
        {
            Sink? sink;
            lock (Gate) Displays.TryGetValue(display, out sink);
            if (sink != null && error.Serial >= sink.FirstSerial && error.Serial <= sink.LastSerial)
            {
                sink.Errors.Enqueue(error);
                return 0;
            }
            return _previous?.Invoke(display, ref error) ?? 0;
        }
        catch { return 0; }
    }
}
