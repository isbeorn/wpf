using System.Runtime.InteropServices;

internal sealed class NativeBoundaryCallback : IDisposable
{
    private readonly object _connection;
    private readonly Exception _expected;
    private readonly SourceCallback _callback;
    private readonly IntPtr _source;
    internal bool ReturnedToNative { get; private set; }

    internal NativeBoundaryCallback(object connection, Exception expected, bool delayed)
    {
        _connection = connection;
        _expected = expected;
        _callback = InvokeFromGlib;
        _source = delayed ? g_timeout_source_new(1) : g_idle_source_new();
        if (_source == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate the native callback source.");
        try
        {
            g_source_set_callback(_source, _callback, IntPtr.Zero, IntPtr.Zero);
            if (g_source_attach(_source, (IntPtr)OwnedNativeAccess.Get(connection, "MainContext")) == 0)
                throw new InvalidOperationException("Cannot attach the callback to the actual owned context.");
        }
        catch { g_source_unref(_source); throw; }
    }

    private int InvokeFromGlib(IntPtr data)
    {
        try { throw _expected; }
        catch (Exception error)
        {
            OwnedNativeAccess.Call(_connection, "CaptureCallbackException", error);
            OwnedNativeAccess.Call(_connection, "CaptureCallbackException", new InvalidOperationException("A later callback must not replace the first exception."));
        }
        ReturnedToNative = true;
        return 0;
    }

    public void Dispose()
    {
        g_source_destroy(_source);
        g_source_unref(_source);
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceCallback(IntPtr data);
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_idle_source_new();
    [DllImport("libglib-2.0.so.0")] private static extern IntPtr g_timeout_source_new(uint interval);
    [DllImport("libglib-2.0.so.0")] private static extern void g_source_set_callback(IntPtr source, SourceCallback callback, IntPtr data, IntPtr notify);
    [DllImport("libglib-2.0.so.0")] private static extern uint g_source_attach(IntPtr source, IntPtr context);
    [DllImport("libglib-2.0.so.0")] private static extern void g_source_destroy(IntPtr source);
    [DllImport("libglib-2.0.so.0")] private static extern void g_source_unref(IntPtr source);
}
