using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

internal static class GioNative
{
    private const string Gio = "libgio-2.0.so.0", Glib = "libglib-2.0.so.0", Object = "libgobject-2.0.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Ready(nint source, nint result, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Destroy(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Signal(nint connection, nint sender, nint path, nint iface, nint signal, nint parameters, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Method(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Closed(nint connection, int vanished, nint error, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct VTable { internal nint Method, GetProperty, SetProperty, Pad0, Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7; }
    [StructLayout(LayoutKind.Sequential)] private struct Error { internal uint Domain; internal int Code; internal nint Message; }
    internal static Exception TakeError(nint error)
    {
        if (error == 0) return new IOException("The native D-Bus operation failed without an error.");
        try { return new IOException(Marshal.PtrToStringUTF8(Marshal.PtrToStructure<Error>(error).Message)); }
        finally { g_error_free(error); }
    }
    internal static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? throw new InvalidDataException("A native UTF-8 string was null.");
    [DllImport(Gio)] internal static extern nint g_dbus_address_get_for_bus_sync(int bus, nint cancel, out nint error);
    [DllImport(Gio)] internal static extern nint g_dbus_connection_new_for_address_sync([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int flags, nint observer, nint cancel, out nint error);
    [DllImport(Gio)] internal static extern void g_dbus_connection_set_exit_on_close(nint connection, int value);
    [DllImport(Gio)] internal static extern nint g_dbus_connection_get_unique_name(nint connection);
    [DllImport(Gio)] internal static extern int g_dbus_connection_is_closed(nint connection);
    [DllImport(Gio)] internal static extern int g_dbus_connection_close_sync(nint connection, nint cancel, out nint error);
    [DllImport(Gio)] internal static extern void g_dbus_connection_call(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, int flags, int timeout, nint cancel, Ready callback, nint data);
    [DllImport(Gio)] internal static extern nint g_dbus_connection_call_finish(nint connection, nint result, out nint error);
    [DllImport(Gio)] internal static extern uint g_dbus_connection_signal_subscribe(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string? sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path, nint arg0, int flags, Signal callback, nint data, Destroy destroy);
    [DllImport(Gio)] internal static extern void g_dbus_connection_signal_unsubscribe(nint connection, uint id);
    [DllImport(Gio)] internal static extern uint g_dbus_connection_register_object(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint info, ref VTable table, nint data, Destroy destroy, out nint error);
    [DllImport(Gio)] internal static extern int g_dbus_connection_unregister_object(nint connection, uint id);
    [DllImport(Gio)] internal static extern nint g_dbus_node_info_new_for_xml([MarshalAs(UnmanagedType.LPUTF8Str)] string xml, out nint error);
    [DllImport(Gio)] internal static extern nint g_dbus_node_info_lookup_interface(nint node, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Gio)] internal static extern void g_dbus_node_info_unref(nint node);
    [DllImport(Gio)] internal static extern void g_dbus_method_invocation_return_value(nint invocation, nint value);
    [DllImport(Gio)] internal static extern void g_dbus_method_invocation_return_dbus_error(nint invocation, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(Gio)] internal static extern int g_dbus_connection_emit_signal(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string? destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, nint parameters, out nint error);
    [DllImport(Gio)] internal static extern nint g_cancellable_new();
    [DllImport(Gio)] internal static extern void g_cancellable_cancel(nint cancel);
    [DllImport(Object)] internal static extern nint g_object_ref(nint value);
    [DllImport(Object)] internal static extern void g_object_unref(nint value);
    [DllImport(Object)] internal static extern nuint g_signal_connect_data(nint instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, Closed callback, nint data, nint destroy, int flags);
    [DllImport(Object)] internal static extern void g_signal_handler_disconnect(nint instance, nuint id);
    [DllImport(Glib)] internal static extern void g_error_free(nint error);
    [DllImport(Glib)] internal static extern void g_free(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_ref(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_ref_sink(nint value);
    [DllImport(Glib)] internal static extern void g_variant_unref(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_get_type_string(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)] string signature);
    [DllImport(Glib)] internal static extern void g_variant_type_free(nint type);
    [DllImport(Glib)] internal static extern nint g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Glib)] internal static extern nint g_variant_new_object_path([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Glib)] internal static extern nint g_variant_new_boolean(int value);
    [DllImport(Glib)] internal static extern nint g_variant_new_uint32(uint value);
    [DllImport(Glib)] internal static extern nint g_variant_new_int32(int value);
    [DllImport(Glib)] internal static extern nint g_variant_new_int16(short value);
    [DllImport(Glib)] internal static extern nint g_variant_new_double(double value);
    [DllImport(Glib)] internal static extern nint g_variant_new_byte(byte value);
    [DllImport(Glib)] internal static extern nint g_variant_new_variant(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_new_dict_entry(nint key, nint value);
    [DllImport(Glib)] internal static extern nint g_variant_new_tuple(nint[] children, nuint count);
    [DllImport(Glib)] internal static extern nint g_variant_new_array(nint childType, nint[] children, nuint count);
    [DllImport(Glib)] internal static extern nuint g_variant_n_children(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_get_child_value(nint value, nuint index);
    [DllImport(Glib)] internal static extern nint g_variant_get_variant(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_get_string(nint value, out nuint length);
    [DllImport(Glib)] internal static extern uint g_variant_get_uint32(nint value);
    [DllImport(Glib)] internal static extern int g_variant_get_int32(nint value);
    [DllImport(Glib)] internal static extern int g_variant_get_boolean(nint value);
    [DllImport(Glib)] internal static extern double g_variant_get_double(nint value);
    [DllImport(Glib)] internal static extern nint g_variant_lookup_value(nint dictionary, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, nint expectedType);
}
