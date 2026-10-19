using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

internal static partial class X11WindowNative
{
    internal const string Xlib = "libX11.so.6";
    [DllImport(Xlib)] internal static extern nint XMaxRequestSize(nint display);
    [DllImport(Xlib)] internal static extern nint XExtendedMaxRequestSize(nint display);
    [StructLayout(LayoutKind.Sequential)]
    internal struct VisualInfo
    {
        public nint Visual;
        public nuint VisualId;
        public int Screen, Depth, Class;
        public nuint RedMask, GreenMask, BlueMask;
        public int ColormapSize, BitsPerRgb;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SetWindowAttributes
    {
        public nuint BackgroundPixmap, BackgroundPixel, BorderPixmap, BorderPixel;
        public int BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public nint EventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public nuint Colormap, Cursor;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowAttributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public nint Visual;
        public nuint Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public nuint Colormap;
        public int MapInstalled, MapState;
        public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    internal struct ClientMessage
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public nuint Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public nint Display;
        [FieldOffset(32)] public nuint Window;
        [FieldOffset(40)] public nuint MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public nint Data0;
        [FieldOffset(64)] public nint Data1;
        [FieldOffset(72)] public nint Data2;
        [FieldOffset(80)] public nint Data3;
        [FieldOffset(88)] public nint Data4;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WmHints
    {
        public nint Flags;
        public int Input, InitialState;
        public nuint IconPixmap, IconWindow;
        public int IconX, IconY;
        public nuint IconMask, WindowGroup;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SizeHints
    {
        public nint Flags;
        public int X, Y, Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight, WidthInc, HeightInc;
        public int MinAspectX, MinAspectY, MaxAspectX, MaxAspectY, BaseWidth, BaseHeight, WinGravity;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RenderDirectFormat
    { public short Red, RedMask, Green, GreenMask, Blue, BlueMask, Alpha, AlphaMask; }
    [StructLayout(LayoutKind.Sequential)] internal struct RenderFormat
    { public nuint Id; public int Type, Depth; public RenderDirectFormat Direct; public nuint Colormap; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle
    { public short X, Y; public ushort Width, Height; }

    [DllImport(Xlib)] internal static extern int XDefaultScreen(nint display);
    [DllImport(Xlib)] internal static extern nint XDefaultVisual(nint display, int screen);
    [DllImport(Xlib)] internal static extern int XDefaultDepth(nint display, int screen);
    [DllImport(Xlib)] internal static extern int XMatchVisualInfo(nint display, int screen, int depth, int visualClass, out VisualInfo info);
    [DllImport(Xlib)] internal static extern nuint XCreateColormap(nint display, nuint window, nint visual, int alloc);
    [DllImport(Xlib)] internal static extern int XFreeColormap(nint display, nuint colormap);
    [DllImport(Xlib)] internal static extern nuint XCreateWindow(nint display, nuint parent, int x, int y, uint width, uint height,
        uint borderWidth, int depth, uint windowClass, nint visual, nuint valueMask, ref SetWindowAttributes attributes);
    [DllImport(Xlib)] internal static extern int XDestroyWindow(nint display, nuint window);
    [DllImport(Xlib)] internal static extern int XMapWindow(nint display, nuint window);
    [DllImport(Xlib)] internal static extern int XMapRaised(nint display, nuint window);
    [DllImport(Xlib)] internal static extern int XUnmapWindow(nint display, nuint window);
    [DllImport(Xlib)] internal static extern int XMoveWindow(nint display, nuint window, int x, int y);
    [DllImport(Xlib)] internal static extern int XResizeWindow(nint display, nuint window, uint width, uint height);
    [DllImport(Xlib)] internal static extern int XSetWMProtocols(nint display, nuint window, nuint[] protocols, int count);
    [DllImport(Xlib)] internal static extern int XSetWMHints(nint display, nuint window, ref WmHints hints);
    [DllImport(Xlib)] internal static extern int XSetWMNormalHints(nint display, nuint window, ref SizeHints hints);
    [DllImport(Xlib)] internal static extern int XSetTransientForHint(nint display, nuint window, nuint owner);
    [DllImport(Xlib)] internal static extern int XStoreName(nint display, nuint window, byte[] title);
    [DllImport(Xlib)] internal static extern int XGetWindowAttributes(nint display, nuint window, out WindowAttributes attributes);
    [DllImport(Xlib)] internal static extern int XTranslateCoordinates(nint display, nuint source, nuint destination,
        int x, int y, out int destinationX, out int destinationY, out nuint child);
    [DllImport(Xlib)] internal static extern int XGetWindowProperty(nint display, nuint window, nuint property,
        nint offset, nint length, int delete, nuint requestedType, out nuint actualType,
        out int actualFormat, out nuint count, out nuint bytesAfter, out nint data);
    [DllImport(Xlib)] internal static extern int XChangeProperty(nint display, nuint window, nuint property,
        nuint type, int format, int mode, byte[] data, int count);
    [DllImport(Xlib, EntryPoint = "XChangeProperty")] internal static extern int XChangeLongProperty(nint display, nuint window,
        nuint property, nuint type, int format, int mode, nuint[] data, int count);
    [DllImport(Xlib)] internal static extern int XDeleteProperty(nint display, nuint window, nuint property);
    [DllImport(Xlib)] internal static extern int XSendEvent(nint display, nuint window, int propagate, nint eventMask, ref ClientMessage message);
    [DllImport(Xlib)] internal static extern int XIconifyWindow(nint display, nuint window, int screen);
    [DllImport(Xlib)] internal static extern int XGetInputFocus(nint display, out nuint focus, out int revert);
    [DllImport(Xlib)] internal static extern int XSetInputFocus(nint display, nuint focus, int revert, nuint time);
    [DllImport(Xlib)] internal static extern int XFree(nint data);
    [DllImport(Xlib)] internal static extern nuint XGetSelectionOwner(nint display, nuint selection);
    [DllImport("libXrender.so.1")] internal static extern nint XRenderFindVisualFormat(nint display, nint visual);
    [DllImport("libXfixes.so.3")] internal static extern int XFixesQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport("libXfixes.so.3")] internal static extern nuint XFixesCreateRegion(nint display, Rectangle[] rectangles, int count);
    [DllImport("libXfixes.so.3")] internal static extern void XFixesDestroyRegion(nint display, nuint region);
    [DllImport("libXfixes.so.3")] internal static extern void XFixesSetWindowShapeRegion(nint display, nuint window, int kind, int x, int y, nuint region);

    internal static nuint[] ReadLongProperty(X11Connection connection, nuint window, string name, nuint expectedType, int limit = 128)
    {
        if (XGetWindowProperty(connection.Display, window, connection.GetAtom(name), 0, limit, 0, expectedType,
            out nuint type, out int format, out nuint count, out nuint remaining, out nint data) != 0)
            throw new InvalidOperationException("Cannot query X11 property " + name + ".");
        try
        {
            if (type == 0) return [];
            if (type != expectedType || format != 32 || count > (nuint)limit || remaining != 0 || (count != 0 && data == 0))
                throw new InvalidOperationException("X11 property " + name + " has an invalid type, format or size.");
            var values = new nuint[checked((int)count)];
            for (int i = 0; i < values.Length; i++) values[i] = (nuint)Marshal.ReadIntPtr(data, checked(i * nint.Size));
            return values;
        }
        finally { if (data != 0) XFree(data); }
    }
    internal static byte[] ReadByteProperty(X11Connection connection, nuint window, string name, nuint expectedType, int limit)
    {
        nuint type = 0, count = 0, remaining = 0;
        int format = 0, status = 0;
        nint data = 0;
        bool succeeded = connection.TryForeignRequest(() => status = XGetWindowProperty(connection.Display, window,
            connection.GetAtom(name), 0, (limit + 3) / 4, 0, expectedType, out type, out format, out count, out remaining, out data));
        if (!succeeded || status != 0)
        {
            if (data != 0) XFree(data);
            throw new InvalidOperationException("Cannot query X11 property " + name + ".");
        }
        try
        {
            if (type == 0) return [];
            if (type != expectedType || format != 8 || count > (nuint)limit || remaining != 0 || (count != 0 && data == 0))
                throw new InvalidOperationException("X11 property " + name + " has an invalid type, format or size.");
            var bytes = new byte[checked((int)count)];
            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { if (data != 0) XFree(data); }
    }
}
