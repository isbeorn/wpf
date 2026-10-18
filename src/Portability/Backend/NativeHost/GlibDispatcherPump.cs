extern alias WpfFork;

using System.Runtime.InteropServices;
using Avalonia.Controls.Platform;
using Avalonia.Threading;
using IDispatcherPump = WpfFork::MS.Internal.Portability.IDispatcherPump;

namespace Wpf.Portable.NativeHost;

internal sealed class GlibDispatcherPump : IDispatcherPump
{
    private readonly IControlledDispatcherImpl _nativeDispatcher;
    private readonly ManagedDispatcherImpl.IManagedDispatcherInputProvider _input;
    private readonly NativeCallbackExceptions _exceptions;
    private readonly Thread _owner = Thread.CurrentThread;
    private readonly IntPtr _context;
    private int _wakePending;
    private int _iterationDepth;
    private int _disposed;

    internal GlibDispatcherPump(IControlledDispatcherImpl nativeDispatcher, ManagedDispatcherImpl.IManagedDispatcherInputProvider input, NativeCallbackExceptions exceptions)
    {
        _nativeDispatcher = nativeDispatcher;
        _input = input;
        _exceptions = exceptions;
        _context = Glib.g_main_context_ref(Glib.g_main_context_default());
        if (Glib.g_main_context_acquire(_context) == 0)
        {
            Glib.g_main_context_unref(_context);
            throw new InvalidOperationException("The default GLib context is owned by another thread.");
        }
    }

    public bool HasPendingInput
    {
        get
        {
            VerifyOwner();
            _exceptions.ThrowPending();
            // GLib's CanQueryPendingInput is false because its ready sources include more than X11 input.
            // Include those sources conservatively instead of silently reporting no native input.
            return _input.HasInput || _nativeDispatcher.HasPendingInput || Glib.g_main_context_pending(_context) != 0;
        }
    }

    public void Wake()
    {
        if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _wakePending, 1) == 0)
            Glib.g_main_context_wakeup(_context);
    }

    public void WaitAndDispatch(int timeoutMilliseconds)
    {
        VerifyOwner();
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, Timeout.Infinite);
        _exceptions.ThrowPending();
        var pendingWake = Interlocked.Exchange(ref _wakePending, 0) != 0;
        IntPtr timeoutSource = IntPtr.Zero;
        _iterationDepth++;
        try
        {
            if (_input.HasInput)
            {
                _input.DispatchNextInputEvent();
                pendingWake = true;
            }
            if (timeoutMilliseconds > 0 && !pendingWake)
            {
                timeoutSource = Glib.g_timeout_source_new((uint)timeoutMilliseconds);
                Glib.g_source_set_callback(timeoutSource, Glib.TimeoutCallback, IntPtr.Zero, IntPtr.Zero);
                if (Glib.g_source_attach(timeoutSource, _context) == 0)
                    throw new InvalidOperationException("Unable to attach the bounded native-wait timeout.");
            }
            Glib.g_main_context_iteration(_context, timeoutMilliseconds != 0 && !pendingWake ? 1 : 0);
        }
        finally
        {
            if (timeoutSource != IntPtr.Zero)
            {
                Glib.g_source_destroy(timeoutSource);
                Glib.g_source_unref(timeoutSource);
            }
            _iterationDepth--;
        }
        _exceptions.ThrowPending();
    }

    public void Dispose()
    {
        if (_owner != Thread.CurrentThread)
            throw new InvalidOperationException("Dispose the native pump on its owner thread.");
        if (_iterationDepth != 0)
            throw new InvalidOperationException("Unwind all native pump frames before disposing the native pump.");
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Glib.g_main_context_release(_context);
        Glib.g_main_context_unref(_context);
    }

    private void VerifyOwner()
    {
        if (_owner != Thread.CurrentThread)
            throw new InvalidOperationException("Dispatch native events on the pump's owner thread.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}

internal static class Glib
{
    private const string Library = "libglib-2.0.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SourceCallback(IntPtr data);
    internal static readonly SourceCallback TimeoutCallback = _ => 0;

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr g_main_context_default();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr g_main_context_ref(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_main_context_unref(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int g_main_context_acquire(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_main_context_release(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int g_main_context_pending(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int g_main_context_iteration(IntPtr context, int mayBlock);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_main_context_wakeup(IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr g_timeout_source_new(uint interval);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_source_set_callback(IntPtr source, SourceCallback callback, IntPtr data, IntPtr notify);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint g_source_attach(IntPtr source, IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_source_destroy(IntPtr source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_source_unref(IntPtr source);
}
