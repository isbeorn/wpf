using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class X11Fixture : IDisposable
{
    private readonly IntPtr _display;
    private readonly IntPtr _root;
    private readonly IntPtr _settingsOwner;
    internal string SettingsOrigin => "_XSETTINGS_S" + XDefaultScreen(_display) + "/owner=0x" +
        _settingsOwner.ToInt64().ToString("x") + "/_XSETTINGS_SETTINGS";

    internal X11Fixture(string settings, byte[]? settingsPayload = null)
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) throw new InvalidOperationException("XOpenDisplay failed for the native fixture.");
        _root = XDefaultRootWindow(_display);
        var selection = XInternAtom(_display, "_XSETTINGS_S" + XDefaultScreen(_display), false);
        if (XGetSelectionOwner(_display, selection) != IntPtr.Zero)
            throw new InvalidOperationException("The native fixture refuses to replace an existing XSETTINGS selection owner. Use a dedicated Xvfb server.");
        if (settings == "absent") return;
        _settingsOwner = XCreateSimpleWindow(_display, _root, 0, 0, 1, 1, 0, IntPtr.Zero, IntPtr.Zero);
        byte[] data = settingsPayload ?? Settings(settings);
        var property = XInternAtom(_display, "_XSETTINGS_SETTINGS", false);
        if (settings != "absent-property") XChangeProperty(_display, _settingsOwner, property, property, 8, 0, data, data.Length);
        XSetSelectionOwner(_display, selection, _settingsOwner, IntPtr.Zero);
        XSync(_display, false);
    }

    internal IntPtr FindWindow(string title)
    {
        XGrabServer(_display);
        try { return FindWindow(_root, title, 0); }
        finally
        {
            XUngrabServer(_display);
            XSync(_display, false);
        }
    }
    internal (int Width, int Height) ScreenSize => Size(_root);

    private IntPtr FindWindow(IntPtr parent, string title, int depth)
    {
        if (depth > 8) return IntPtr.Zero;
        if (XQueryTree(_display, parent, out _, out _, out var children, out var count) == 0)
            throw new InvalidOperationException("XQueryTree failed.");
        try
        {
            if (count > 4096) throw new InvalidOperationException("The native fixture window tree is too large.");
            for (int i = 0; i < count; i++)
            {
                var child = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                if (XFetchName(_display, child, out var name) != 0)
                {
                    try { if (Marshal.PtrToStringUTF8(name) == title) return child; }
                    finally { XFree(name); }
                }
                var nested = FindWindow(child, title, depth + 1);
                if (nested != IntPtr.Zero) return nested;
            }
        }
        finally { if (children != IntPtr.Zero) XFree(children); }
        return IntPtr.Zero;
    }

    internal void Focus(IntPtr window)
    {
        XSetInputFocus(_display, window, 2, IntPtr.Zero);
        XSync(_display, false);
        XGetInputFocus(_display, out var focused, out _);
        if (focused != window) throw new InvalidOperationException("The X server did not assign focus to the requested genuine WPF window.");
    }

    internal void Minimize(IntPtr window)
    {
        if (XIconifyWindow(_display, window, XDefaultScreen(_display)) == 0)
            throw new InvalidOperationException("The native fixture could not request external minimization.");
        XSync(_display, false);
    }

    internal void Key(char key, bool pressed)
    {
        byte code = XKeysymToKeycode(_display, (IntPtr)key);
        if (code == 0 || !XTestFakeKeyEvent(_display, code, pressed, IntPtr.Zero))
            throw new InvalidOperationException("XTest key transition failed.");
        XSync(_display, false);
    }

    internal KeyboardMapLease RemapSpareKey(params uint[] symbols) => new(this, symbols);

    internal static string? NativeEnvironmentVariable(string name) => Marshal.PtrToStringUTF8(getenv(name));
    internal static void SetNativeEnvironmentVariable(string name, string? value)
    {
        int result = value == null ? unsetenv(name) : setenv(name, value, 1);
        if (result != 0) throw new InvalidOperationException("Cannot configure the owned native fixture environment: " + name);
    }

    internal sealed class KeyboardMapLease : IDisposable
    {
        private readonly X11Fixture _owner;
        private readonly byte _code;
        private readonly IntPtr[] _original;
        private bool _disposed;

        internal KeyboardMapLease(X11Fixture owner, uint[] symbols)
        {
            _owner = owner;
            XDisplayKeycodes(owner._display, out int minimum, out int maximum);
            if (minimum < 8 || maximum > 255 || minimum > maximum) throw new InvalidOperationException("Invalid native keycode range.");
            _code = checked((byte)maximum);
            var pressed = new byte[32];
            XQueryKeymap(owner._display, pressed);
            if ((pressed[_code >> 3] & (1 << (_code & 7))) != 0)
                throw new InvalidOperationException("The spare native keycode is already physically pressed.");
            var mapping = XGetKeyboardMapping(owner._display, _code, 1, out int width);
            if (mapping == IntPtr.Zero || width < 1 || width > 256) throw new InvalidOperationException("Cannot snapshot the spare native key mapping.");
            try
            {
                _original = new IntPtr[width];
                Marshal.Copy(mapping, _original, 0, width);
            }
            finally { XFree(mapping); }
            Remap(symbols);
        }

        internal void Remap(params uint[] symbols)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (symbols.Length is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(symbols));
            var mapping = symbols.Select(value => checked((IntPtr)(long)value)).ToArray();
            XChangeKeyboardMapping(_owner._display, _code, mapping.Length, mapping, 1);
            XSync(_owner._display, false);
        }

        internal void Key(bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!XTestFakeKeyEvent(_owner._display, _code, pressed, IntPtr.Zero))
                throw new InvalidOperationException("The remapped native key transition failed.");
            XSync(_owner._display, false);
        }

        internal void Tap()
        {
            try { Key(true); }
            finally { Key(false); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Key(false);
            XChangeKeyboardMapping(_owner._display, _code, _original.Length, _original, 1);
            XSync(_owner._display, false);
            _disposed = true;
        }
    }

    internal void Button(uint button, bool pressed)
    {
        if (!XTestFakeButtonEvent(_display, button, pressed, IntPtr.Zero))
            throw new InvalidOperationException("XTest button transition failed.");
        XSync(_display, false);
    }

    internal void MovePointer(int rootX, int rootY)
    {
        if (!XTestFakeMotionEvent(_display, -1, rootX, rootY, IntPtr.Zero))
            throw new InvalidOperationException("XTest pointer motion failed.");
        XSync(_display, false);
    }

    internal void MovePointer(IntPtr window, int clientX, int clientY)
    {
        if (XTranslateCoordinates(_display, window, _root, clientX, clientY, out int x, out int y, out _) == 0)
            throw new InvalidOperationException("XTranslateCoordinates failed.");
        MovePointer(x, y);
    }

    internal void Unmap(IntPtr window) { XUnmapWindow(_display, window); XSync(_display, false); }

    internal void Resize(IntPtr window, uint width, uint height)
    {
        XResizeWindow(_display, window, width, height);
        XSync(_display, false);
    }

    [DllImport("libX11.so.6")]
    private static extern int XResizeWindow(IntPtr display, IntPtr window, uint width, uint height);

    internal void Move(IntPtr window, int x, int y) { XMoveWindow(_display, window, x, y); XSync(_display, false); }
    internal (int X, int Y) ClientOrigin(IntPtr window)
    {
        if (XTranslateCoordinates(_display, window, _root, 0, 0, out int x, out int y, out _) == 0)
            throw new InvalidOperationException("Cannot query actual native client origin.");
        return (x, y);
    }
    [DllImport("libX11.so.6")]
    private static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);

    internal void SetDesktopWorkArea(int x, int y, int width, int height)
        => SetDesktopWorkAreas([(uint)x, (uint)y, (uint)width, (uint)height], 0);

    internal void SetDesktopWorkAreas(uint[] values, uint? currentDesktop)
    {
        var bytes = new byte[values.Length * IntPtr.Size];
        for (int index = 0; index < values.Length; index++)
            BitConverter.GetBytes((long)values[index]).CopyTo(bytes, index * IntPtr.Size);
        XChangeProperty(_display, _root, XInternAtom(_display, "_NET_WORKAREA", false),
            (IntPtr)6, 32, 0, bytes, values.Length);
        IntPtr desktop = XInternAtom(_display, "_NET_CURRENT_DESKTOP", false);
        if (currentDesktop is { } selected)
            XChangeProperty(_display, _root, desktop, (IntPtr)6, 32, 0, BitConverter.GetBytes((long)selected), 1);
        else
            XDeleteProperty(_display, _root, desktop);
        XSync(_display, false);
    }

    internal void SetDpi(int dpi)
    {
        byte[] data = Encoding.ASCII.GetBytes("Xft.dpi:\t" + dpi + "\n");
        XChangeProperty(_display, _root, XInternAtom(_display, "RESOURCE_MANAGER", false),
            (IntPtr)31, 8, 0, data, data.Length);
        XSync(_display, false);
    }

    internal IDisposable Cover(IntPtr window)
    {
        var size = Size(window);
        if (XTranslateCoordinates(_display, window, _root, 0, 0, out int x, out int y, out _) == 0)
            throw new InvalidOperationException("Cannot locate the native window for occlusion.");
        var cover = XCreateSimpleWindow(_display, _root, x, y, size.Width, size.Height, 0, IntPtr.Zero, IntPtr.Zero);
        if (cover == IntPtr.Zero) throw new InvalidOperationException("Cannot create the native covering window.");
        XMapRaised(_display, cover); XSync(_display, false);
        return new CoverLease(this, cover);
    }

    internal IDisposable CreateFocusSentinel(out IntPtr window)
    {
        window = XCreateSimpleWindow(_display, _root, 0, 0, 1, 1, 0, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("Cannot create the owned focus sentinel.");
        try { XMapRaised(_display, window); Focus(window); }
        catch { XDestroyWindow(_display, window); XSync(_display, false); throw; }
        return new CoverLease(this, window);
    }
    internal uint ScreenPixel(IntPtr window, int clientX, int clientY)
    {
        if (XTranslateCoordinates(_display, window, _root, clientX, clientY, out int x, out int y, out _) == 0)
            throw new InvalidOperationException("Cannot locate the native screen pixel.");
        return Pixel(_root, x, y);
    }

    private sealed class CoverLease(X11Fixture owner, IntPtr window) : IDisposable
    {
        private IntPtr _window = window;
        public void Dispose()
        {
            if (_window == IntPtr.Zero) return;
            XDestroyWindow(owner._display, _window); XSync(owner._display, false);
            _window = IntPtr.Zero;
        }
    }

    internal IDisposable HoldPointerGrab()
    {
        int result = XGrabPointer(_display, _root, false, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        XSync(_display, false);
        if (result != 0) throw new InvalidOperationException("The independent fixture connection could not hold native pointer capture.");
        return new PointerGrabLease(this);
    }

    private sealed class PointerGrabLease(X11Fixture owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            XUngrabPointer(owner._display, IntPtr.Zero);
            XSync(owner._display, false);
        }
    }
    internal bool CanAcquirePointerGrab()
    {
        int result = XGrabPointer(_display, _root, false, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result == 0) XUngrabPointer(_display, IntPtr.Zero);
        XSync(_display, false);
        return result == 0;
    }

    internal (bool Visible, ulong Fingerprint) CursorImage()
    {
        IntPtr memory = XFixesGetCursorImage(_display);
        if (memory == IntPtr.Zero) throw new InvalidOperationException("XFixesGetCursorImage returned no native image.");
        try
        {
            var image = Marshal.PtrToStructure<NativeCursorImage>(memory);
            int count = checked(image.Width * image.Height);
            if (count <= 0 || count > 1048576 || image.Pixels == IntPtr.Zero)
                throw new InvalidOperationException("The native cursor image layout is invalid.");
            bool visible = false; ulong fingerprint = 14695981039346656037;
            for (int index = 0; index < count; index++)
            {
                ulong pixel = unchecked((ulong)Marshal.ReadIntPtr(image.Pixels, checked(index * IntPtr.Size)).ToInt64());
                visible |= (pixel & 0xff000000) != 0;
                fingerprint = unchecked((fingerprint ^ pixel) * 1099511628211);
            }
            return (visible, fingerprint);
        }
        finally { XFree(memory); }
    }

    internal void Click(IntPtr window, int clientX, int clientY)
    {
        if (XTranslateCoordinates(_display, window, _root, clientX, clientY, out var x, out var y, out _) == 0)
            throw new InvalidOperationException("XTranslateCoordinates failed.");
        if (!XTestFakeMotionEvent(_display, -1, x, y, IntPtr.Zero) ||
            !XTestFakeButtonEvent(_display, 1, true, IntPtr.Zero) ||
            !XTestFakeButtonEvent(_display, 1, false, IntPtr.Zero))
            throw new InvalidOperationException("XTest mouse press/release injection failed.");
        XSync(_display, false);
    }

    internal void Type(char character)
    {
        uint code = XKeysymToKeycode(_display, (IntPtr)character);
        if (code == 0) throw new InvalidOperationException("The test character has no native X11 keycode.");
        bool down = XTestFakeKeyEvent(_display, code, true, IntPtr.Zero);
        bool up = XTestFakeKeyEvent(_display, code, false, IntPtr.Zero);
        XSync(_display, false);
        if (!down || !up) throw new InvalidOperationException("XTest keyboard press/release injection failed.");
    }

    internal unsafe uint Pixel(IntPtr window, int x, int y, bool preserveAlpha = false)
    {
        XSync(_display, false);
        nuint serial = XNextRequest(_display);
        NativeError? failed = null;
        IntPtr prior = IntPtr.Zero;
        NativeErrorHandler handler = (IntPtr display, ref NativeError error) =>
        {
            if (display == _display && error.Serial == serial && error.RequestCode == 73 && error.ErrorCode == 8)
            {
                failed = error;
                return 0;
            }
            fixed (NativeError* pointer = &error)
                return ((delegate* unmanaged[Cdecl]<IntPtr, NativeError*, int>)prior)(display, pointer);
        };
        prior = XSetErrorHandler(handler);
        IntPtr image = IntPtr.Zero;
        try
        {
            if (prior == IntPtr.Zero) throw new InvalidOperationException("Native pixel observation requires the existing X11 error handler.");
            image = XGetImage(_display, window, x, y, 1, 1, unchecked((nuint)~0UL), 2);
            XSync(_display, false);
            if (failed is { } error)
            {
                int observed = XGetWindowAttributes(_display, window, out var geometry);
                throw new InvalidOperationException($"Native XGetImage BadMatch: window=0x{window.ToInt64():x}, pixel=({x},{y}), serial={error.Serial}, " +
                    $"attributes={observed}, geometry=({geometry.X},{geometry.Y},{geometry.Width},{geometry.Height}), mapState={geometry.MapState}.");
            }
            if (image == IntPtr.Zero) throw new InvalidOperationException("Native XGetImage returned no surface pixels.");
            return (uint)XGetPixel(image, 0, 0).ToUInt64() & (preserveAlpha ? uint.MaxValue : 0x00ffffff);
        }
        finally
        {
            if (image != IntPtr.Zero) XDestroyImage(image);
            XSetErrorHandler(prior);
            GC.KeepAlive(handler);
        }
    }

    internal (int Depth, bool OverrideRedirect) PopupAttributes(IntPtr window)
    {
        if (XGetWindowAttributes(_display, window, out var attributes) == 0) throw new InvalidOperationException("Popup has no native attributes.");
        return (attributes.Depth, attributes.OverrideRedirect != 0);
    }
    internal (int X, int Y) PointerPosition
    {
        get
        {
            if (XQueryPointer(_display, _root, out _, out _, out int x, out int y, out _, out _, out _) == 0)
                throw new InvalidOperationException("Cannot query the private native pointer position.");
            return (x, y);
        }
    }
    internal IntPtr StackingAncestor(IntPtr window)
    {
        var visited = new HashSet<IntPtr>();
        while (window != IntPtr.Zero && visited.Add(window))
        {
            if (XQueryTree(_display, window, out _, out var parent, out var children, out _) == 0)
                throw new InvalidOperationException("Cannot query the native stacking parent.");
            if (children != IntPtr.Zero) XFree(children);
            if (parent == _root) return window;
            window = parent;
        }
        throw new InvalidOperationException("The native client has no distinct root stacking ancestor.");
    }
    internal IReadOnlyList<IntPtr> RootChildren
    {
        get
        {
            if (XQueryTree(_display, _root, out _, out _, out var children, out int count) == 0) throw new InvalidOperationException("Cannot query owned native root children.");
            try
            {
                if (count < 0 || count > 4096) throw new InvalidOperationException("Unexpected native root child count.");
                var result = new IntPtr[count];
                for (int i = 0; i < count; i++) result[i] = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                return result;
            }
            finally { if (children != IntPtr.Zero) XFree(children); }
        }
    }

    internal (uint[] Window, uint[] Root) Pixels(IntPtr window, IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count == 0) throw new ArgumentException("Native pixel sampling requires points.", nameof(points));
        int left = points.Min(point => point.X), top = points.Min(point => point.Y);
        int right = points.Max(point => point.X), bottom = points.Max(point => point.Y);
        uint width = checked((uint)(right - left + 1)), height = checked((uint)(bottom - top + 1));
        if (XTranslateCoordinates(_display, window, _root, 0, 0, out int originX, out int originY, out _) == 0)
            throw new InvalidOperationException("Cannot locate the native sampling region.");
        int rootX = checked(originX + left), rootY = checked(originY + top);
        IntPtr windowImage = IntPtr.Zero, rootImage = IntPtr.Zero;
        try
        {
            XSync(_display, false);
            windowImage = XGetImage(_display, window, left, top, width, height, unchecked((nuint)~0UL), 2);
            if (windowImage == IntPtr.Zero) throw new InvalidOperationException("Native XGetImage returned no window sampling region.");
            rootImage = XGetImage(_display, _root, rootX, rootY, width, height, unchecked((nuint)~0UL), 2);
            if (rootImage == IntPtr.Zero) throw new InvalidOperationException("Native XGetImage returned no root sampling region.");
            var windowPixels = new uint[points.Count]; var rootPixels = new uint[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                int x = points[index].X - left, y = points[index].Y - top;
                windowPixels[index] = (uint)XGetPixel(windowImage, x, y).ToUInt64() & 0x00ffffff;
                rootPixels[index] = (uint)XGetPixel(rootImage, x, y).ToUInt64() & 0x00ffffff;
            }
            return (windowPixels, rootPixels);
        }
        finally
        {
            try { if (rootImage != IntPtr.Zero) XDestroyImage(rootImage); }
            finally { if (windowImage != IntPtr.Zero) XDestroyImage(windowImage); }
        }
    }
    internal unsafe bool IsMapped(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        XSync(_display, false);
        nuint serial = XNextRequest(_display);
        bool absent = false;
        IntPtr prior = IntPtr.Zero;
        NativeErrorHandler handler = (IntPtr display, ref NativeError error) =>
        {
            if (display == _display && error.Serial == serial && error.ResourceId == unchecked((nuint)window.ToInt64()) &&
                error.RequestCode == 3 && error.ErrorCode == 3)
            {
                absent = true;
                return 0;
            }
            fixed (NativeError* pointer = &error)
                return ((delegate* unmanaged[Cdecl]<IntPtr, NativeError*, int>)prior)(display, pointer);
        };
        prior = XSetErrorHandler(handler);
        try
        {
            if (prior == IntPtr.Zero) throw new InvalidOperationException("Native mapping observation requires the existing X11 error handler.");
            int observed = XGetWindowAttributes(_display, window, out var attributes);
            XSync(_display, false);
            return !absent && observed != 0 && attributes.MapState == 2;
        }
        finally
        {
            XSetErrorHandler(prior);
            GC.KeepAlive(handler);
        }
    }
    internal (int Width, int Height) RootSize => Size(_root);
    internal bool HasSize(IntPtr window, int width, int height) =>
        XGetWindowAttributes(_display, window, out var attributes) != 0 && attributes.Width == width && attributes.Height == height;
    internal (int Width, int Height) Size(IntPtr window)
        => SizeOnConnection(_display, window);

    internal static (int Width, int Height) SizeOnConnection(IntPtr display, IntPtr window)
    {
        if (XGetWindowAttributes(display, window, out var attributes) == 0)
            throw new InvalidOperationException("The native window has no geometry.");
        return (attributes.Width, attributes.Height);
    }
    internal string State(IntPtr window)
    {
        var atoms = Property(window, "_NET_WM_STATE", 4);
        if (atoms.Contains(XInternAtom(_display, "_NET_WM_STATE_HIDDEN", false))) return "Minimized";
        return atoms.Contains(XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_HORZ", false)) &&
            atoms.Contains(XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_VERT", false)) ? "Maximized" : "Normal";
    }
    internal IReadOnlyList<IntPtr> SplashWindows => RootChildren.Where(window => Property(window, "_NET_WM_WINDOW_TYPE", 4).Contains(XInternAtom(_display, "_NET_WM_WINDOW_TYPE_SPLASH", false))).ToArray();
    internal bool IsAbove(IntPtr window) => Property(window, "_NET_WM_STATE", 4).Contains(XInternAtom(_display, "_NET_WM_STATE_ABOVE", false));
    internal bool HasZeroUserTime(IntPtr window)
    {
        var values = Property(window, "_NET_WM_USER_TIME", 6);
        return values.Count == 1 && values[0] == IntPtr.Zero;
    }
    internal (int X, int Y, int Width, int Height) Bounds(IntPtr window)
    {
        if (XGetWindowAttributes(_display, window, out var attributes) == 0) throw new InvalidOperationException("Native window has no attributes.");
        return (attributes.X, attributes.Y, attributes.Width, attributes.Height);
    }
    internal bool SkipsTaskbar(IntPtr window) => Property(window, "_NET_WM_STATE", 4).Contains(XInternAtom(_display, "_NET_WM_STATE_SKIP_TASKBAR", false));
    internal bool IsTopmost(IntPtr window) => Property(window, "_NET_WM_STATE", 4).Contains(XInternAtom(_display, "_NET_WM_STATE_ABOVE", false));
    internal void RestrictWindowFunctions(IntPtr window)
    {
        var property = XInternAtom(_display, "_MOTIF_WM_HINTS", false);
        var values = Property(window, "_MOTIF_WM_HINTS", checked((int)property));
        if (values.Count != 5) throw new InvalidDataException("The native Motif hint is incomplete.");
        values[0] = checked((IntPtr)(values[0].ToInt64() | 1)); values[1] = (IntPtr)36;
        var bytes = new byte[values.Count * IntPtr.Size];
        for (int i = 0; i < values.Count; i++) BitConverter.GetBytes(values[i].ToInt64()).CopyTo(bytes, i * IntPtr.Size);
        XChangeProperty(_display, window, property, property, 32, 0, bytes, values.Count);
        XSync(_display, false);
    }
    internal long MotifFunctions(IntPtr window) => Property(window, "_MOTIF_WM_HINTS", checked((int)XInternAtom(_display, "_MOTIF_WM_HINTS", false))).ElementAt(1).ToInt64();
    internal long Decorations(IntPtr window) => Property(window, "_MOTIF_WM_HINTS", checked((int)XInternAtom(_display, "_MOTIF_WM_HINTS", false))).ElementAt(2).ToInt64();
    internal (int Left, int Right, int Top, int Bottom)? FrameExtents(IntPtr window)
    {
        var values = Property(window, "_NET_FRAME_EXTENTS", 6);
        if (values.Count == 0) return null;
        if (values.Count != 4 || values.Any(value => value.ToInt64() < 0)) throw new InvalidDataException("Malformed native frame extents.");
        return (checked((int)values[0]), checked((int)values[1]), checked((int)values[2]), checked((int)values[3]));
    }
    internal IReadOnlyList<IntPtr> ClientStacking => Property(_root, "_NET_CLIENT_LIST_STACKING", 33);
    internal IntPtr TransientOwner(IntPtr window) => Property(window, "WM_TRANSIENT_FOR", 33).SingleOrDefault();
    internal IntPtr FocusedWindow { get { XGetInputFocus(_display, out var window, out _); return window; } }
    internal void ObserveTaskbarRequests()
    {
        XSelectInput(_display, _root, (IntPtr)(1 << 19));
        XSync(_display, false);
    }
    internal IReadOnlyList<int> TakeTaskbarRequests(IntPtr window)
    {
        var requests = new List<int>();
        IntPtr state = XInternAtom(_display, "_NET_WM_STATE", false);
        IntPtr skip = XInternAtom(_display, "_NET_WM_STATE_SKIP_TASKBAR", false);
        while (XCheckTypedEvent(_display, 33, out var message) != 0)
        {
            var client = message.Client;
            if (client.Window != window || client.MessageType != state ||
                client.Data1 != skip && client.Data2 != skip) continue;
            if (client.Format != 32 || client.Data0 != IntPtr.Zero && client.Data0 != (IntPtr)1)
                throw new InvalidDataException("Malformed native taskbar state request.");
            requests.Add(checked((int)client.Data0));
        }
        return requests;
    }
    internal bool HasWindowManager => Property(_root, "_NET_SUPPORTING_WM_CHECK", 33).Count == 1;
    internal (long Standard, long Extended) IconRequestLimits => ((long)XMaxRequestSize(_display), (long)XExtendedMaxRequestSize(_display));
    [DllImport("libX11.so.6")] private static extern IntPtr XMaxRequestSize(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XExtendedMaxRequestSize(IntPtr display);
    internal uint[]? WindowIcon(IntPtr window)
    {
        var property = XInternAtom(_display, "_NET_WM_ICON", true);
        if (property == IntPtr.Zero) return null;
        if (XGetWindowProperty(_display, window, property, IntPtr.Zero, IntPtr.Zero, false, (IntPtr)6,
                out var type, out var format, out var count, out var remaining, out var data) != 0)
            throw new InvalidOperationException("Cannot measure the native window icon property.");
        try
        {
            if (type == IntPtr.Zero) return null;
            if (type != (IntPtr)6 || format != 32 || count != IntPtr.Zero || remaining.ToInt64() < 0 || remaining.ToInt64() % 4 != 0)
                throw new InvalidDataException("The native window icon is not a CARDINAL32 property.");
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
        int length = checked((int)(remaining.ToInt64() / 4));
        if (XGetWindowProperty(_display, window, property, IntPtr.Zero, (IntPtr)length, false, (IntPtr)6,
                out type, out format, out count, out remaining, out data) != 0)
            throw new InvalidOperationException("Cannot read the complete native window icon property.");
        try
        {
            if (type != (IntPtr)6 || format != 32 || count.ToInt64() != length || remaining != IntPtr.Zero || (length != 0 && data == IntPtr.Zero))
                throw new InvalidDataException("The native window icon changed or was truncated during readback.");
            var result = new uint[length];
            for (int index = 0; index < length; index++) result[index] = unchecked((uint)Marshal.ReadIntPtr(data, checked(index * IntPtr.Size)).ToInt64());
            return result;
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }
    private List<IntPtr> Property(IntPtr window, string name, int expectedType)
    {
        var property = XInternAtom(_display, name, true);
        if (property == IntPtr.Zero) return new();
        if (XGetWindowProperty(_display, window, property, IntPtr.Zero, (IntPtr)128, false, (IntPtr)expectedType,
                out var type, out var format, out var count, out var remaining, out var data) != 0)
            throw new InvalidOperationException("Cannot read native property " + name);
        try
        {
            if (type == IntPtr.Zero) return new();
            if (type != (IntPtr)expectedType || format != 32 || count.ToInt64() > 128 || remaining != IntPtr.Zero)
                throw new InvalidDataException("Malformed native property " + name);
            var result = new List<IntPtr>();
            for (int i = 0; i < count.ToInt64(); i++) result.Add(Marshal.ReadIntPtr(data, i * IntPtr.Size));
            return result;
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    internal void Expose(IntPtr window)
    { XClearArea(_display, window, 0, 0, 0, 0, true); XSync(_display, false); }

    internal void RequestClose(IntPtr window)
    {
        var message = new XEvent { Client = new ClientMessage { Type = 33, SendEvent = 1, Display = _display,
            Window = window, MessageType = XInternAtom(_display, "WM_PROTOCOLS", false), Format = 32,
            Data0 = XInternAtom(_display, "WM_DELETE_WINDOW", false) } };
        if (XSendEvent(_display, window, false, IntPtr.Zero, ref message) == 0)
            throw new InvalidOperationException("The native WM_DELETE_WINDOW request failed.");
        XSync(_display, false);
    }
    internal void RequestMaximized(IntPtr window, bool maximized)
    {
        var message = new XEvent { Client = new ClientMessage { Type = 33, SendEvent = 1, Display = _display,
            Window = window, MessageType = XInternAtom(_display, "_NET_WM_STATE", false), Format = 32,
            Data0 = maximized ? (IntPtr)1 : IntPtr.Zero,
            Data1 = XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_VERT", false),
            Data2 = XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_HORZ", false), Data3 = (IntPtr)2 } };
        if (XSendEvent(_display, _root, false, (IntPtr)((1 << 19) | (1 << 20)), ref message) == 0)
            throw new InvalidOperationException("The native EWMH maximize/restore request failed.");
        XSync(_display, false);
    }

    public void Dispose()
    {
        if (_settingsOwner != IntPtr.Zero) XDestroyWindow(_display, _settingsOwner);
        XCloseDisplay(_display);
    }

    private static byte[] Settings(string variant)
    {
        string nameVariant = variant.EndsWith("-little", StringComparison.Ordinal) ? variant[..^7] : variant.EndsWith("-big", StringComparison.Ordinal) ? variant[..^4] : variant;
        string[] extraNames = nameVariant switch
        {
            "name-illegal-distance" => ["Net/DoubleClickDistanc?"],
            "name-unknown-illegal" => ["Other/invalid?"],
            "name-nonascii" => ["Other/\u00ff"],
            "name-nul" => ["Other/\0"],
            "name-empty" => [""],
            "name-leading-slash" => ["/Other"],
            "name-trailing-slash" => ["Other/"],
            "name-empty-segment" => ["Other//Name"],
            "name-digit-start" => ["0Other"],
            "name-digit-segment" => ["Other/0Name"],
            "name-valid" => ["Other/valid_name0", "_111/_background", "_background"],
            _ => []
        };
        bool missingDistance = variant.StartsWith("missing-distance", StringComparison.Ordinal) || variant == "both-absent" || extraNames.Length != 0;
        bool missingTime = variant is "missing-time" or "both-absent";
        bool little = variant != "big" && !variant.EndsWith("-big", StringComparison.Ordinal);
        var bytes = new List<byte> { little ? (byte)0 : (byte)1, 0, 0, 0 };
        Add32(1); Add32((uint)((missingDistance ? 0 : 1) + (missingTime ? 0 : 1) + extraNames.Length + (variant is "duplicate" or "distance-duplicate" or "missing-distance-duplicate" ? 1 : 0)));
        if (!missingTime) Entry("Net/DoubleClickTime", variant == "published-min" ? 1u : missingDistance || variant == "published-max" ? 400u : variant.StartsWith("published", StringComparison.Ordinal) ? 725u : 450u,
            variant is "value-type" or "missing-distance-value-type" ? (byte)1 : (byte)0);
        if (!missingDistance) Entry("Net/DoubleClickDistance", variant == "distance-range" ? 0u : variant == "published-min" ? 1u : variant == "published-max" ? int.MaxValue : variant.StartsWith("published", StringComparison.Ordinal) ? 11u : 5u,
            variant == "distance-value-type" ? (byte)1 : (byte)0);
        foreach (string name in extraNames) Entry(name, 11);
        if (variant == "duplicate") Entry("Net/DoubleClickTime", 400);
        if (variant == "distance-duplicate") Entry("Net/DoubleClickDistance", 6);
        if (variant == "missing-distance-duplicate") Entry("Net/DoubleClickTime", 400);
        if (variant is "trailing" or "missing-distance-trailing") bytes.Add(0);
        var result = bytes.ToArray();
        if (variant is "header" or "missing-distance-header") result[0] = 2;
        if (variant is "count" or "missing-distance-count") BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 5000);
        if (variant == "missing-distance-under-count") BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 0);
        if (variant is "truncated" or "missing-distance-truncated") Array.Resize(ref result, result.Length - 1);
        if (variant is "type" or "missing-distance-type") result[12] = 9;
        if (variant is "range" or "missing-distance-range") BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), 0);
        return result;

        void Add32(uint value)
        { Span<byte> valueBytes = stackalloc byte[4]; if (little) BinaryPrimitives.WriteUInt32LittleEndian(valueBytes, value); else BinaryPrimitives.WriteUInt32BigEndian(valueBytes, value); bytes.AddRange(valueBytes.ToArray()); }
        void Entry(string name, uint value, byte type = 0)
        {
            bytes.Add(type); bytes.Add(0);
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            Span<byte> length = stackalloc byte[2];
            if (little) BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)nameBytes.Length); else BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)nameBytes.Length);
            bytes.AddRange(length.ToArray()); bytes.AddRange(nameBytes);
            while (bytes.Count % 4 != 0) bytes.Add(0);
            Add32(1);
            if (type == 1)
            {
                byte[] text = Encoding.ASCII.GetBytes(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add32((uint)text.Length); bytes.AddRange(text);
                while (bytes.Count % 4 != 0) bytes.Add(0);
            }
            else Add32(value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCursorImage
    { public short X, Y; public ushort Width, Height, XHot, YHot; public nuint Serial; public IntPtr Pixels; public nuint Atom; public IntPtr Name; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeError
    { public int Type; public IntPtr Display; public nuint ResourceId, Serial; public byte ErrorCode, RequestCode, MinorCode; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeErrorHandler(IntPtr display, ref NativeError error);
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientMessage
    { public int Type; public nuint Serial; public int SendEvent; public IntPtr Display, Window, MessageType; public int Format; public IntPtr Data0, Data1, Data2, Data3, Data4; }
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent { [FieldOffset(0)] public ClientMessage Client; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowAttributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public IntPtr Visual, Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public IntPtr ColorMap;
        public int MapInstalled, MapState;
        public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public IntPtr Screen;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr mask);
    [DllImport("libX11.so.6")] private static extern int XCheckTypedEvent(IntPtr display, int type, out XEvent message);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, int width, int height, int border, IntPtr borderColor, IntPtr background);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XUnmapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XMapRaised(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XGrabPointer(IntPtr display, IntPtr window, bool ownerEvents, uint mask, int pointerMode, int keyboardMode, IntPtr confine, IntPtr cursor, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XUngrabPointer(IntPtr display, IntPtr time);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyExisting);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, byte[] data, int count);
    [DllImport("libX11.so.6")] private static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
    [DllImport("libX11.so.6")] private static extern int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern nuint XNextRequest(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(NativeErrorHandler handler);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int x, out int y, out uint mask);
    [DllImport("libX11.so.6")] private static extern int XGrabServer(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XUngrabServer(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out int count);
    [DllImport("libX11.so.6")] private static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")] private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revert, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XGetInputFocus(IntPtr display, out IntPtr window, out int revert);
    [DllImport("libX11.so.6")] private static extern int XIconifyWindow(IntPtr display, IntPtr window, int screen);
    [DllImport("libXfixes.so.3")] private static extern IntPtr XFixesGetCursorImage(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out WindowAttributes attributes);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        IntPtr offset, IntPtr length, bool delete, IntPtr requestedType, out IntPtr type, out int format,
        out IntPtr count, out IntPtr remaining, out IntPtr data);
    [DllImport("libX11.so.6")] private static extern int XTranslateCoordinates(IntPtr display, IntPtr source, IntPtr destination, int x, int y, out int resultX, out int resultY, out IntPtr child);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr symbol);
    [DllImport("libc.so.6")] private static extern IntPtr getenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libc.so.6")] private static extern int setenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);
    [DllImport("libc.so.6")] private static extern int unsetenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libX11.so.6")] private static extern int XDisplayKeycodes(IntPtr display, out int minimum, out int maximum);
    [DllImport("libX11.so.6")] private static extern int XQueryKeymap(IntPtr display, [Out] byte[] keys);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetKeyboardMapping(IntPtr display, byte first, int count, out int width);
    [DllImport("libX11.so.6")] private static extern int XChangeKeyboardMapping(IntPtr display, int first, int width, IntPtr[] symbols, int count);
    [DllImport("libXtst.so.6")] private static extern bool XTestFakeKeyEvent(IntPtr display, uint code, bool press, IntPtr delay);
    [DllImport("libXtst.so.6")] private static extern bool XTestFakeButtonEvent(IntPtr display, uint button, bool press, IntPtr delay);
    [DllImport("libXtst.so.6")] private static extern bool XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, IntPtr delay);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetImage(IntPtr display, IntPtr window, int x, int y, uint width, uint height, nuint planes, int format);
    [DllImport("libX11.so.6")] private static extern nuint XGetPixel(IntPtr image, int x, int y);
    [DllImport("libX11.so.6")] private static extern int XDestroyImage(IntPtr image);
    [DllImport("libX11.so.6")] private static extern int XClearArea(IntPtr display, IntPtr window, int x, int y, uint width, uint height, bool expose);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, ref XEvent message);
}
