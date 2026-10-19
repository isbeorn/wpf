using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

// Xlib/XKB and libxkbcommon public C ABI on Linux LP64.
internal static class X11InputNative
{
    [DllImport("libX11.so.6")] internal static extern int XUngrabPointer(nint display, nuint time);
    [DllImport("libX11.so.6")] internal static extern int XQueryKeymap(nint display, [Out] byte[] keys);
    [DllImport("libX11.so.6")] internal static extern int XkbLookupKeySym(nint display, byte keycode, uint state, out uint consumed, out nuint symbol);
    [DllImport("libX11.so.6")] internal static extern nuint XkbKeycodeToKeysym(nint display, byte keycode, int group, int level);
    [DllImport("libX11.so.6")] internal static extern int XkbSetDetectableAutoRepeat(nint display, int detectable, out int supported);
    [DllImport("libX11.so.6")] internal static extern int XkbQueryExtension(nint display, out int opcode, out int firstEvent, out int firstError, ref int major, ref int minor);
    [DllImport("libX11.so.6")] internal static extern int XkbSelectEvents(nint display, uint device, nuint change, nuint values);
    [DllImport("libX11.so.6")] internal static extern int XkbRefreshKeyboardMapping(ref XEvent nativeEvent);
    [DllImport("libX11.so.6")] internal static extern int XRefreshKeyboardMapping(ref XEvent nativeEvent);
    [DllImport("libX11.so.6")] internal static extern int XkbGetNamedIndicator(nint display, nuint name, out int index, out int state, nint map, out int real);
    [DllImport("libX11.so.6")] internal static extern nint XGetModifierMapping(nint display);
    [DllImport("libX11.so.6")] internal static extern int XFreeModifiermap(nint map);
    [DllImport("libX11.so.6")] internal static extern int XTranslateCoordinates(nint display, nuint source, nuint target, int x, int y, out int translatedX, out int translatedY, out nuint child);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_context_new(int flags);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_context_unref(nint context);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_compose_table_new_from_locale(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string locale, int flags);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_compose_table_unref(nint table);
    [DllImport("libxkbcommon.so.0")] internal static extern nint xkb_compose_state_new(nint table, int flags);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_compose_state_unref(nint state);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_compose_state_feed(nint state, uint symbol);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_compose_state_get_status(nint state);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_compose_state_get_utf8(nint state, [Out] byte[]? buffer, nuint size);
    [DllImport("libxkbcommon.so.0")] internal static extern void xkb_compose_state_reset(nint state);
    [DllImport("libxkbcommon.so.0")] internal static extern int xkb_keysym_to_utf8(uint symbol, [Out] byte[] buffer, nuint size);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XModifierKeymap { internal int KeysPerModifier; internal nint Keys; }

[StructLayout(LayoutKind.Sequential)]
internal struct XkbEventHeader
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Time; internal int KeyboardEventType, Device;
}
