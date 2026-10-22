using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

internal static class X11Native
{
    private const string Library = "libX11.so.6";
    internal const int GenericEvent = 35;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ErrorHandler(nint display, ref XErrorEvent error);
    [DllImport(Library)] internal static extern int XInitThreads();
    [DllImport(Library)] internal static extern nint XOpenDisplay(nint name);
    [DllImport(Library)] internal static extern int XCloseDisplay(nint display);
    [DllImport(Library)] internal static extern nuint XDefaultRootWindow(nint display);
    [DllImport(Library)] internal static extern int XConnectionNumber(nint display);
    [DllImport(Library)] internal static extern int XDefaultScreen(nint display);
    [DllImport(Library)] internal static extern int XDefaultDepth(nint display, int screen);
    [DllImport(Library)] internal static extern nint XDefaultVisual(nint display, int screen);
    [DllImport(Library)] internal static extern nuint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(Library)] internal static extern int XEventsQueued(nint display, int mode);
    [DllImport(Library)] internal static extern int XNextEvent(nint display, out XEvent nativeEvent);
    [DllImport(Library)] internal static extern int XGetEventData(nint display, ref XGenericEventCookie cookie);
    [DllImport(Library)] internal static extern void XFreeEventData(nint display, ref XGenericEventCookie cookie);
    [DllImport(Library)] internal static extern int XFlush(nint display);
    [DllImport(Library)] internal static extern int XSync(nint display, int discard);
    [DllImport(Library)] internal static extern nuint XNextRequest(nint display);
    [DllImport(Library)] internal static extern nint XSetErrorHandler(ErrorHandler handler);
    [DllImport(Library)] internal static extern int XGetWindowAttributes(nint display, nuint window, out XWindowAttributes attributes);
    [DllImport(Library)] internal static extern int XQueryExtension(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport(Library)] internal static extern int XFree(nint data);
}
