using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

internal static class X11ClipboardNative
{
    private const string Library = "libX11.so.6";
    internal const nint PropertyChangeMask = 1 << 22;
    internal const nint StructureNotifyMask = 1 << 17;
    [DllImport(Library)] internal static extern nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [DllImport(Library)] internal static extern int XDestroyWindow(nint display, nuint window);
    [DllImport(Library)] internal static extern int XSelectInput(nint display, nuint window, nint mask);
    [DllImport(Library)] internal static extern nuint XGetSelectionOwner(nint display, nuint selection);
    [DllImport(Library)] internal static extern int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);
    [DllImport(Library)] internal static extern int XConvertSelection(nint display, nuint selection, nuint target, nuint property, nuint requestor, nuint time);
    [DllImport(Library)] internal static extern int XDeleteProperty(nint display, nuint window, nuint property);
    [DllImport(Library)] internal static extern int XChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, byte[] data, int count);
    [DllImport(Library, EntryPoint = "XChangeProperty")] internal static extern int XChangePropertyLongs(nint display, nuint window, nuint property, nuint type, int format, int mode, nuint[] data, int count);
    [DllImport(Library)] internal static extern int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint requestedType, out nuint actualType, out int format, out nuint count, out nuint remaining, out nint data);
    [DllImport(Library)] internal static extern int XSendEvent(nint display, nuint window, int propagate, nint mask, ref XEvent nativeEvent);
}
