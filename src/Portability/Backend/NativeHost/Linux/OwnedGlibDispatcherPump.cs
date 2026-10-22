extern alias WpfFork;

using System.Runtime.InteropServices;
using IDispatcherPump = WpfFork::MS.Internal.Portability.IDispatcherPump;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class OwnedGlibDispatcherPump : IDispatcherPump
{
    private readonly X11Connection _connection;
    private readonly Thread _owner = Thread.CurrentThread;
    private readonly OwnedGlib.FdCallback _fdCallback;
    private readonly nint _fdSource;
    private bool _disposed;
    private int _depth;

    internal OwnedGlibDispatcherPump(X11Connection connection)
    {
        _connection = connection;
        connection.AttachPump();
        _fdCallback = OnReadable;
        try
        {
            _fdSource = OwnedGlib.g_unix_fd_source_new(connection.ConnectionNumber, 1 | 8 | 16);
            if (_fdSource == 0) throw new InvalidOperationException("Unable to create the X11 event source.");
            OwnedGlib.g_source_set_callback(_fdSource, Marshal.GetFunctionPointerForDelegate(_fdCallback), 0, 0);
            if (OwnedGlib.g_source_attach(_fdSource, connection.MainContext) == 0)
                throw new InvalidOperationException("Unable to attach the X11 event source.");
        }
        catch
        {
            if (_fdSource != 0) { OwnedGlib.g_source_destroy(_fdSource); OwnedGlib.g_source_unref(_fdSource); }
            connection.DetachPump();
            throw;
        }
    }

    public bool HasPendingInput
    {
        get
        {
            Verify();
            return _connection.HasPendingInput || OwnedGlib.g_main_context_pending(_connection.MainContext) != 0;
        }
    }
    public void Wake() { if (!Volatile.Read(ref _disposed)) _connection.Wake(); }
    public void WaitAndDispatch(int timeoutMilliseconds)
    {
        Verify();
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, Timeout.Infinite);
        _connection.ThrowPendingException();
        var pendingWake = _connection.ConsumeWake();
        nint timeoutSource = 0;
        _depth++;
        _connection.EnterFrame();
        try
        {
            if (_connection.HasPendingInput)
            {
                _connection.DispatchBatch();
                pendingWake = true;
            }
            if (timeoutMilliseconds > 0 && !pendingWake)
            {
                timeoutSource = OwnedGlib.g_timeout_source_new((uint)timeoutMilliseconds);
                if (timeoutSource == 0) throw new InvalidOperationException("Unable to create a bounded native wait.");
                OwnedGlib.g_source_set_callback(timeoutSource, OwnedGlib.TimeoutPointer, 0, 0);
                if (OwnedGlib.g_source_attach(timeoutSource, _connection.MainContext) == 0)
                    throw new InvalidOperationException("Unable to attach a bounded native wait.");
            }
            OwnedGlib.g_main_context_iteration(_connection.MainContext, timeoutMilliseconds != 0 && !pendingWake ? 1 : 0);
            _connection.ThrowPendingException();
            if (!pendingWake) _connection.DispatchBatch();
        }
        finally
        {
            if (timeoutSource != 0) { OwnedGlib.g_source_destroy(timeoutSource); OwnedGlib.g_source_unref(timeoutSource); }
            _connection.ExitFrame();
            _depth--;
        }
        _connection.ThrowPendingException();
    }
    private int OnReadable(int fd, uint condition, nint data)
    {
        if ((condition & (8 | 16)) != 0)
        {
            _connection.CaptureCallbackException(new IOException("The X11 connection became unavailable."));
            return 0;
        }
        try { _connection.QueueNativeEvents(32); }
        catch (Exception exception) { _connection.CaptureCallbackException(exception); }
        return 1;
    }
    public void Dispose()
    {
        if (_owner != Thread.CurrentThread) throw new InvalidOperationException("Dispose the native pump on its owner thread.");
        if (_disposed) return;
        _connection.VerifyOwner();
        if (_depth != 0) throw new InvalidOperationException("Unwind all native pump frames before disposing the pump.");
        _disposed = true;
        OwnedGlib.g_source_destroy(_fdSource);
        OwnedGlib.g_source_unref(_fdSource);
        _connection.DetachPump();
    }
    private void Verify() { _connection.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
}

internal static class OwnedGlib
{
    private const string Library = "libglib-2.0.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FdCallback(int fd, uint condition, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceCallback(nint data);
    private static readonly SourceCallback TimeoutCallback = _ => 0;
    internal static readonly nint TimeoutPointer = Marshal.GetFunctionPointerForDelegate(TimeoutCallback);
    [DllImport(Library)] internal static extern nint g_main_context_new();
    [DllImport(Library)] internal static extern void g_main_context_unref(nint context);
    [DllImport(Library)] internal static extern void g_main_context_push_thread_default(nint context);
    [DllImport(Library)] internal static extern void g_main_context_pop_thread_default(nint context);
    [DllImport(Library)] internal static extern int g_main_context_pending(nint context);
    [DllImport(Library)] internal static extern int g_main_context_iteration(nint context, int mayBlock);
    [DllImport(Library)] internal static extern void g_main_context_wakeup(nint context);
    [DllImport(Library)] internal static extern nint g_unix_fd_source_new(int fd, uint condition);
    [DllImport(Library)] internal static extern nint g_timeout_source_new(uint interval);
    [DllImport(Library)] internal static extern void g_source_set_callback(nint source, nint callback, nint data, nint notify);
    [DllImport(Library)] internal static extern uint g_source_attach(nint source, nint context);
    [DllImport(Library)] internal static extern void g_source_destroy(nint source);
    [DllImport(Library)] internal static extern void g_source_unref(nint source);
}
