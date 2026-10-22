using System.Runtime.InteropServices;
using System.Text;
using static Wpf.Portable.NativeHost.Linux.X11WindowNative;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class X11Window : IDisposable
{
    private readonly bool _popup;
    private readonly X11Framebuffer _framebuffer;
    private readonly X11Screens _screens;
    private bool _disposed, _mapped, _shown, _enabled = true, _activateOnMap = true;
    private X11WindowState _initialState;
    private X11WindowDecorations _decorations = X11WindowDecorations.Full;
    private nuint _resizeControls = 4 | 32 | 64;
    public X11Connection Connection { get; }
    public nuint Handle { get; private set; }
    public nint Visual { get; }
    public int Depth { get; }
    public nuint Colormap { get; private set; }
    public bool Transparent { get; }
    public bool IsComposited { get { Verify(); return XGetSelectionOwner(Connection.Display, Connection.GetAtom("_NET_WM_CM_S" + XDefaultScreen(Connection.Display))) != 0; } }
    public double Scale { get { Verify(); return _screens.ReadDpi() / 96.0; } }
    public bool IsVisible => !_disposed && _mapped;
    public event Action? Exposed;
    public event Action? MetricsChanged;
    public event Action? CloseRequested;
    public event Action? Closed;
    public event Action<bool>? FocusChanged;
    public event Action<XEvent>? RawEvent;

    public X11Window(X11Connection connection, X11WindowOptions options)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        connection.VerifyOwner();
        if (nint.Size != 8 || !BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("The owned X11 window requires Linux x64 LP64 little-endian ABI.");
        ValidateSize(options.Width, options.Height);
        if (options.Owner is { } owner)
        {
            owner.Verify();
            if (owner.Connection != connection) throw new ArgumentException("An X11 owner must use the same connection.", nameof(options));
        }
        if (options.Popup && options.Splash) throw new ArgumentException("A native window cannot be both a popup and a splash.", nameof(options));
        _popup = options.Popup;
        Transparent = options.Transparent;
        int screen = XDefaultScreen(connection.Display);
        Visual = XDefaultVisual(connection.Display, screen);
        Depth = XDefaultDepth(connection.Display, screen);
        if (Transparent)
        {
            if (XMatchVisualInfo(connection.Display, screen, 32, 4, out var info) == 0)
                throw new PlatformNotSupportedException("Transparent windows require an X11 TrueColor ARGB32 visual.");
            var formatPointer = XRenderFindVisualFormat(connection.Display, info.Visual);
            if (formatPointer == 0) throw new PlatformNotSupportedException("XRender cannot describe the ARGB32 visual.");
            var format = Marshal.PtrToStructure<RenderFormat>(formatPointer);
            if (info.RedMask != 0xff0000 || info.GreenMask != 0xff00 || info.BlueMask != 0xff ||
                format.Type != 1 || format.Depth != 32 || format.Direct.Alpha != 24 || format.Direct.AlphaMask != 255)
                throw new PlatformNotSupportedException("The native visual does not expose premultiplied BGRA with eight-bit alpha.");
            Visual = info.Visual;
            Depth = 32;
        }
        _screens = new X11Screens(connection);
        _screens.Changed += OnScreenChanged;
        _framebuffer = new X11Framebuffer(this);
        try
        {
            Colormap = XCreateColormap(connection.Display, connection.RootWindow, Visual, 0);
            if (Colormap == 0) throw new InvalidOperationException("XCreateColormap failed.");
            var attributes = new SetWindowAttributes
            {
                BackgroundPixel = 0, BorderPixel = 0, Colormap = Colormap,
                OverrideRedirect = _popup ? 1 : 0,
                EventMask = (nint)((1L << 0) | (1L << 1) | (1L << 2) | (1L << 3) | (1L << 4) |
                    (1L << 5) | (1L << 6) | (1L << 15) | (1L << 17) | (1L << 21) | (1L << 22))
            };
            Handle = XCreateWindow(connection.Display, connection.RootWindow, options.X, options.Y,
                (uint)options.Width, (uint)options.Height, 0, Depth, 1, Visual,
                (1u << 1) | (1u << 3) | (1u << 9) | (1u << 11) | (1u << 13), ref attributes);
            if (Handle == 0) throw new InvalidOperationException("XCreateWindow failed.");
            connection.RegisterWindow(Handle, HandleEvent);
            XSetWMProtocols(connection.Display, Handle,
                [connection.GetAtom("WM_DELETE_WINDOW"), connection.GetAtom("WM_TAKE_FOCUS")], 2);
            var normal = new SizeHints { Flags = (nint)((1 << 4) | (1 << 9)), MinWidth = 1, MinHeight = 1, WinGravity = 1 };
            XSetWMNormalHints(connection.Display, Handle, ref normal);
            SetAtoms("_NET_WM_WINDOW_TYPE", [_popup ? connection.GetAtom("_NET_WM_WINDOW_TYPE_POPUP_MENU") : connection.GetAtom(options.Splash ? "_NET_WM_WINDOW_TYPE_SPLASH" : "_NET_WM_WINDOW_TYPE_NORMAL")]);
            SetOwner(options.Owner);
            UpdateWmHints();
            connection.Synchronize();
        }
        catch
        {
            if (Handle != 0) { connection.UnregisterWindow(Handle); XDestroyWindow(connection.Display, Handle); Handle = 0; }
            if (Colormap != 0) { XFreeColormap(connection.Display, Colormap); Colormap = 0; }
            _framebuffer.Dispose();
            _screens.Dispose();
            _disposed = true;
            throw;
        }
    }

    public X11WindowMetrics ReadMetrics()
    {
        Verify();
        if (XGetWindowAttributes(Connection.Display, Handle, out var attributes) == 0 || attributes.Width <= 0 || attributes.Height <= 0)
            throw new InvalidOperationException("Cannot query positive X11 client geometry.");
        if (XTranslateCoordinates(Connection.Display, Handle, Connection.RootWindow, 0, 0, out int x, out int y, out _) == 0)
            throw new InvalidOperationException("Cannot query the X11 client screen origin.");
        var values = ReadLongProperty(Connection, Handle, "_NET_FRAME_EXTENTS", 6, 4);
        var extents = default(X11FrameExtents);
        if (values.Length != 0)
        {
            if (values.Length != 4 || values.Any(value => value > int.MaxValue))
                throw new InvalidOperationException("The X11 frame extents are malformed.");
            extents = new((int)values[0], (int)values[2], (int)values[1], (int)values[3]);
        }
        return new(attributes.Width, attributes.Height, x, y, extents, ReadState());
    }

    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    { Verify(); _framebuffer.Present(pixels, width, height, stride); }
    private SizeHints ReadNormalHints()
    {
        if (XGetWMNormalHints(Connection.Display, Handle, out var hints, out _) == 0)
            throw new InvalidOperationException("The owned X11 window has no readable WM_NORMAL_HINTS.");
        return hints;
    }
    public void Move(int x, int y)
    {
        Verify();
        var hints = ReadNormalHints();
        hints.Flags |= 1 << 2;
        hints.X = x; hints.Y = y;
        XSetWMNormalHints(Connection.Display, Handle, ref hints);
        XMoveWindow(Connection.Display, Handle, x, y);
        Connection.Flush();
    }
    public void Resize(int width, int height)
    { Verify(); ValidateSize(width, height); XResizeWindow(Connection.Display, Handle, (uint)width, (uint)height); Connection.Flush(); }
    public void Show(bool activate = true)
    {
        Verify();
        _activateOnMap = activate && !_popup && _enabled;
        UpdateWmHints();
        if (_activateOnMap) XDeleteProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_USER_TIME"));
        else XChangeLongProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_USER_TIME"), 6, 32, 0, [0], 1);
        bool remainIconic = _initialState == X11WindowState.Minimized && ReadState() == X11WindowState.Minimized;
        _shown = true;
        if (!remainIconic)
        {
            XMapWindow(Connection.Display, Handle);
            // A queued withdrawal can erase pre-map state properties before this remap.
            if (!_popup && _initialState == X11WindowState.Maximized)
                SendRoot("_NET_WM_STATE", 1, (nint)Connection.GetAtom("_NET_WM_STATE_MAXIMIZED_HORZ"),
                    (nint)Connection.GetAtom("_NET_WM_STATE_MAXIMIZED_VERT"), 1);
            else if (!_popup && _initialState == X11WindowState.FullScreen)
                SendRoot("_NET_WM_STATE", 1, (nint)Connection.GetAtom("_NET_WM_STATE_FULLSCREEN"), 0, 1);
            if (_initialState == X11WindowState.Minimized && !_popup) XIconifyWindow(Connection.Display, Handle, XDefaultScreen(Connection.Display));
            else if (_activateOnMap) SendRoot("_NET_ACTIVE_WINDOW", 1, 0);
        }
        _mapped = !remainIconic;
        Connection.Flush();
    }
    public bool Activate()
    {
        Verify();
        if (!_enabled || !_mapped || _popup) return false;
        _activateOnMap = true;
        XDeleteProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_USER_TIME"));
        UpdateWmHints();
        SendRoot("_NET_ACTIVE_WINDOW", 1, 0);
        Connection.Flush();
        return true;
    }
    public void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight)
    {
        Verify();
        if (minWidth < 0 || minHeight < 0 || maxWidth < 0 || maxHeight < 0 ||
            minWidth > 65535 || minHeight > 65535 || maxWidth > 65535 || maxHeight > 65535 ||
            (maxWidth != 0 && Math.Max(1, minWidth) > maxWidth) || (maxHeight != 0 && Math.Max(1, minHeight) > maxHeight))
            throw new ArgumentOutOfRangeException(nameof(minWidth), "X11 size constraints are inconsistent or outside native bounds.");
        var previous = ReadNormalHints();
        var hints = new SizeHints { Flags = (nint)((1 << 4) | (1 << 9)) | (previous.Flags & ((1 << 0) | (1 << 2))),
            X = previous.X, Y = previous.Y,
            MinWidth = Math.Max(1, minWidth), MinHeight = Math.Max(1, minHeight), WinGravity = 1 };
        if (maxWidth != 0 || maxHeight != 0)
        {
            hints.Flags |= 1 << 5;
            hints.MaxWidth = maxWidth == 0 ? 65535 : maxWidth;
            hints.MaxHeight = maxHeight == 0 ? 65535 : maxHeight;
        }
        XSetWMNormalHints(Connection.Display, Handle, ref hints);
        Connection.Flush();
    }
    public void Hide() { Verify(); XUnmapWindow(Connection.Display, Handle); _shown = false; _mapped = false; Connection.Flush(); }
    public void SetTitle(string title)
    {
        Verify(); ArgumentNullException.ThrowIfNull(title);
        if (title.Contains('\0')) throw new ArgumentException("An X11 title cannot contain a NUL character.", nameof(title));
        byte[] bytes = Encoding.UTF8.GetBytes(title);
        XChangeProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_NAME"), Connection.GetAtom("UTF8_STRING"), 8, 0, bytes, bytes.Length);
        XStoreName(Connection.Display, Handle, [.. bytes, 0]);
        Connection.Flush();
    }
    internal int IconCapacity
    {
        get
        {
            Verify();
            // ChangeProperty has six 32-bit header units; BIG-REQUESTS adds one.
            long capacity = Math.Max((long)XMaxRequestSize(Connection.Display) - 6, (long)XExtendedMaxRequestSize(Connection.Display) - 7);
            if (capacity < 2) throw new InvalidOperationException("The X server cannot accept a window icon property.");
            return checked((int)Math.Min(int.MaxValue, capacity));
        }
    }
    public void SetIcon(nuint[]? values)
    {
        Verify();
        if (values is null) XDeleteProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_ICON"));
        else
        {
            if (values.Length == 0 || values.Length > IconCapacity)
                throw new ArgumentException("The complete window icon exceeds the X server's negotiated request capacity.", nameof(values));
            for (int offset = 0; offset < values.Length;)
            {
                if (values.Length - offset < 2 || values[offset] is 0 or > int.MaxValue || values[offset + 1] is 0 or > int.MaxValue)
                    throw new ArgumentException("The window icon contains invalid bitmap dimensions.", nameof(values));
                long pixels = checked((long)values[offset] * (long)values[offset + 1]);
                offset += 2;
                if (pixels > values.Length - offset) throw new ArgumentException("The window icon bitmap pixels are incomplete.", nameof(values));
                offset += checked((int)pixels);
            }
            XChangeLongProperty(Connection.Display, Handle, Connection.GetAtom("_NET_WM_ICON"), 6, 32, 0, values, values.Length);
        }
        Connection.Flush();
    }
    public void SetOwner(X11Window? owner)
    {
        Verify();
        if (owner is null) XDeleteProperty(Connection.Display, Handle, Connection.GetAtom("WM_TRANSIENT_FOR"));
        else
        {
            owner.Verify();
            if (owner == this || owner.Connection != Connection) throw new ArgumentException("An X11 owner must be another live window on the same connection.", nameof(owner));
            XSetTransientForHint(Connection.Display, Handle, owner.Handle);
        }
        Connection.Flush();
    }
    public void SetDecorations(X11WindowDecorations decorations)
    {
        Verify();
        if (!Enum.IsDefined(decorations)) throw new ArgumentOutOfRangeException(nameof(decorations));
        _decorations = decorations;
        UpdateMotifHints();
    }
    public void SetResizeControls(bool resize, bool minimize, bool maximize)
    {
        Verify();
        _resizeControls = (resize ? 4u : 0) | (minimize ? 32u : 0) | (maximize ? 64u : 0);
        UpdateMotifHints();
    }
    private void UpdateMotifHints()
    {
        nuint decorations = _decorations switch
        {
            X11WindowDecorations.None => 0,
            X11WindowDecorations.Border => 2,
            _ => 2 | 8 | 16 | _resizeControls
        };
        XChangeLongProperty(Connection.Display, Handle, Connection.GetAtom("_MOTIF_WM_HINTS"), Connection.GetAtom("_MOTIF_WM_HINTS"),
            // Openbox couples maximize permission to resize. Keep programmatic states available;
            // ResizeMode controls decorations and owned Chrome gestures, not desktop actions.
            32, 0, [3, 62, decorations, 0, 0], 5);
        Connection.Flush();
    }
    public bool SupportsMoveResize
    {
        get { Verify(); return ReadLongProperty(Connection, Connection.RootWindow, "_NET_SUPPORTED", 4, 4096).Contains(Connection.GetAtom("_NET_WM_MOVERESIZE")); }
    }
    public void BeginMoveResize(int direction, int physicalScreenX, int physicalScreenY)
    {
        Verify();
        if (direction is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(direction));
        if (_popup || !IsVisible || !_enabled) throw new InvalidOperationException("Interactive WM operations require a visible enabled normal window.");
        if (!SupportsMoveResize) throw new PlatformNotSupportedException("The window manager does not advertise interactive move/resize support.");
        SendRoot("_NET_WM_MOVERESIZE", physicalScreenX, physicalScreenY, direction, 1, 1);
        Connection.Flush();
    }
    public void CancelMoveResize()
    {
        Verify();
        SendRoot("_NET_WM_MOVERESIZE", 0, 0, 11, 1, 1);
        Connection.Flush();
    }
    public void SetTopmost(bool value) { Verify(); ChangeStateAtom("_NET_WM_STATE_ABOVE", value); Connection.Flush(); }
    public void SetShowInTaskbar(bool value) { Verify(); ChangeStateAtom("_NET_WM_STATE_SKIP_TASKBAR", !value); Connection.Flush(); }
    public void SetEnabled(bool value) { Verify(); _enabled = value; UpdateWmHints(); Connection.Flush(); }
    public void SetHitTestable(bool value)
    {
        Verify();
        if (XFixesQueryExtension(Connection.Display, out _, out _) == 0)
            throw new PlatformNotSupportedException("Native popup input regions require XFixes.");
        nuint region = 0;
        try
        {
            if (!value) region = XFixesCreateRegion(Connection.Display, [], 0);
            if (!value && region == 0) throw new InvalidOperationException("XFixesCreateRegion failed.");
            XFixesSetWindowShapeRegion(Connection.Display, Handle, 2, 0, 0, region);
            Connection.Synchronize();
        }
        finally { if (region != 0) XFixesDestroyRegion(Connection.Display, region); }
    }
    public bool AcquireFocus(bool checkOnly)
    {
        Verify();
        if (!checkOnly && !_enabled) return false;
        XGetInputFocus(Connection.Display, out nuint focus, out _);
        if (focus == Handle || checkOnly) return focus == Handle;
        if (!_enabled || XGetWindowAttributes(Connection.Display, Handle, out var attributes) == 0 || attributes.MapState != 2) return false;
        XSetInputFocus(Connection.Display, Handle, 2, 0);
        Connection.Synchronize();
        XGetInputFocus(Connection.Display, out focus, out _);
        return focus == Handle;
    }
    internal static IReadOnlyList<X11Window> GetStackingOrder(X11Connection connection, IReadOnlyList<X11Window> windows)
    {
        connection.VerifyOwner();
        var clients = new Dictionary<nuint, X11Window>();
        foreach (var window in windows)
        {
            if (window.Connection != connection) throw new ArgumentException("Stacking candidates must share the native connection.", nameof(windows));
            if (!window.IsVisible || !IsViewable(connection, window.Handle)) continue;
            nuint ancestor = window.Handle;
            var visited = new HashSet<nuint>();
            while (ancestor != 0 && visited.Add(ancestor))
            {
                if (!TryQueryTree(connection, ancestor, out nuint parent, out _)) break;
                if (parent == connection.RootWindow) { clients.Add(ancestor, window); break; }
                ancestor = parent;
            }
        }
        if (!TryQueryTree(connection, connection.RootWindow, out _, out nuint[] children))
            throw new InvalidOperationException("Cannot query the native root window stacking order.");
        var ordered = new List<X11Window>();
        for (int i = children.Length - 1; i >= 0; i--)
            if (clients.TryGetValue(children[i], out var window)) ordered.Add(window);
        return ordered;
    }
    private static bool IsViewable(X11Connection connection, nuint window)
    {
        int status = 0;
        WindowAttributes attributes = default;
        return connection.TryForeignRequest(() => status = XGetWindowAttributes(connection.Display, window, out attributes))
            && status != 0 && attributes.MapState == 2;
    }
    private static bool TryQueryTree(X11Connection connection, nuint window, out nuint parent, out nuint[] children)
    {
        int status = 0;
        nuint nativeParent = 0;
        nint data = 0;
        uint count = 0;
        try
        {
            bool success = connection.TryForeignRequest(() => status = XQueryTree(connection.Display, window, out _, out nativeParent, out data, out count));
            parent = nativeParent;
            if (!success || status == 0) { children = []; return false; }
            if (count != 0 && data == 0) throw new InvalidOperationException("The native window tree returned no child storage.");
            children = new nuint[checked((int)count)];
            for (int i = 0; i < children.Length; i++) children[i] = (nuint)Marshal.ReadIntPtr(data, checked(i * nint.Size));
            return true;
        }
        finally { if (data != 0) XFree(data); }
    }
    internal void RaiseWithoutActivation()
    {
        Verify();
        if (!IsVisible || !IsViewable(Connection, Handle)) return;
        var changes = new WindowChanges { StackMode = 0 }; // Above, without changing geometry or focus.
        if (XReconfigureWMWindow(Connection.Display, Handle, XDefaultScreen(Connection.Display), 1 << 6, ref changes) == 0)
            throw new InvalidOperationException("Cannot request a nonactivating native window raise.");
        Connection.Flush();
    }
    public void RequestState(X11WindowState state)
    {
        Verify();
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (_popup && state != X11WindowState.Normal) throw new InvalidOperationException("An X11 popup has no managed window state.");
        _initialState = state;
        if (!_shown)
        {
            UpdateWmHints();
            ChangeStateAtom("_NET_WM_STATE_MAXIMIZED_HORZ", state == X11WindowState.Maximized);
            ChangeStateAtom("_NET_WM_STATE_MAXIMIZED_VERT", state == X11WindowState.Maximized);
            ChangeStateAtom("_NET_WM_STATE_FULLSCREEN", state == X11WindowState.FullScreen);
        }
        else if (state == X11WindowState.Minimized) XIconifyWindow(Connection.Display, Handle, XDefaultScreen(Connection.Display));
        else
        {
            XMapWindow(Connection.Display, Handle);
            SendRoot("_NET_WM_STATE", state == X11WindowState.Maximized ? 1 : 0,
                (nint)Connection.GetAtom("_NET_WM_STATE_MAXIMIZED_HORZ"), (nint)Connection.GetAtom("_NET_WM_STATE_MAXIMIZED_VERT"), 1);
            SendRoot("_NET_WM_STATE", state == X11WindowState.FullScreen ? 1 : 0,
                (nint)Connection.GetAtom("_NET_WM_STATE_FULLSCREEN"), 0, 1);
        }
        Connection.Flush();
    }
    private X11WindowState ReadState()
    {
        var atoms = ReadLongProperty(Connection, Handle, "_NET_WM_STATE", 4);
        bool Has(string name) => atoms.Contains(Connection.GetAtom(name));
        if (Has("_NET_WM_STATE_HIDDEN")) return X11WindowState.Minimized;
        var wmState = ReadLongProperty(Connection, Handle, "WM_STATE", Connection.GetAtom("WM_STATE"), 2);
        if (wmState.Length > 0 && wmState[0] == 3) return X11WindowState.Minimized;
        if (Has("_NET_WM_STATE_FULLSCREEN")) return X11WindowState.FullScreen;
        return Has("_NET_WM_STATE_MAXIMIZED_HORZ") && Has("_NET_WM_STATE_MAXIMIZED_VERT") ? X11WindowState.Maximized : X11WindowState.Normal;
    }
    private void ChangeStateAtom(string name, bool enabled)
    {
        nuint atom = Connection.GetAtom(name);
        if (_shown) SendRoot("_NET_WM_STATE", enabled ? 1 : 0, (nint)atom, 0, 1);
        else
        {
            var atoms = ReadLongProperty(Connection, Handle, "_NET_WM_STATE", 4).Where(item => item != atom).ToList();
            if (enabled) atoms.Add(atom);
            SetAtoms("_NET_WM_STATE", atoms.ToArray());
        }
    }
    private void SetAtoms(string name, nuint[] atoms) => XChangeLongProperty(Connection.Display, Handle,
        Connection.GetAtom(name), 4, 32, 0, atoms, atoms.Length);
    private void UpdateWmHints()
    {
        var hints = new WmHints { Flags = 1 | 2, Input = _enabled && !_popup ? 1 : 0,
            InitialState = _initialState == X11WindowState.Minimized ? 3 : 1 };
        XSetWMHints(Connection.Display, Handle, ref hints);
    }
    private void SendRoot(string name, nint a, nint b, nint c = 0, nint d = 0, nint e = 0)
    {
        var message = new ClientMessage { Type = 33, Display = Connection.Display, Window = Handle,
            MessageType = Connection.GetAtom(name), Format = 32, Data0 = a, Data1 = b, Data2 = c, Data3 = d, Data4 = e };
        if (XSendEvent(Connection.Display, Connection.RootWindow, 0, (nint)((1L << 19) | (1L << 20)), ref message) == 0)
            throw new InvalidOperationException("Cannot send X11 window manager request " + name + ".");
    }
    private void OnScreenChanged() { if (!_disposed) MetricsChanged?.Invoke(); }
    private void HandleEvent(XEvent value)
    {
        if (_disposed) return;
        if (value.Type != 17) RawEvent?.Invoke(value);
        if (_disposed) return;
        switch (value.Type)
        {
            case 12:
                _framebuffer.Repaint();
                if (!_disposed) Exposed?.Invoke();
                break;
            case 17:
                DisposeCore(false);
                return;
            case 18:
                _mapped = false;
                MetricsChanged?.Invoke();
                break;
            case 19:
                _mapped = true;
                MetricsChanged?.Invoke();
                break;
            case 22:
            case 28:
                MetricsChanged?.Invoke();
                break;
            case 9:
            case 10:
                if (value.Focus.Mode is 0 or 3 && value.Focus.Detail != 2) FocusChanged?.Invoke(value.Type == 9);
                break;
            case 33:
                if (value.ClientMessage.MessageType == Connection.GetAtom("WM_PROTOCOLS") && value.ClientMessage.Format == 32)
                {
                    if ((nuint)value.ClientMessage.Data0 == Connection.GetAtom("WM_DELETE_WINDOW")) CloseRequested?.Invoke();
                    else if ((nuint)value.ClientMessage.Data0 == Connection.GetAtom("WM_TAKE_FOCUS") && _enabled && _activateOnMap && !_popup
                        && Connection.TryGetWindowAttributes(Handle, out var attributes) && attributes.MapState == 2)
                    {
                        XSetInputFocus(Connection.Display, Handle, 2, (nuint)value.ClientMessage.Data1);
                        Connection.Flush();
                    }
                }
                break;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Connection.VerifyOwner();
        DisposeCore(true);
    }
    private void DisposeCore(bool destroy)
    {
        _disposed = true;
        _mapped = false;
        _shown = false;
        nuint window = Handle;
        Handle = 0;
        Connection.UnregisterWindow(window);
        _framebuffer.Dispose();
        _screens.Dispose();
        if (destroy) XDestroyWindow(Connection.Display, window);
        if (Colormap != 0) { XFreeColormap(Connection.Display, Colormap); Colormap = 0; }
        Connection.Flush();
        Closed?.Invoke();
    }
    private void Verify() { Connection.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private static void ValidateSize(int width, int height)
    {
        if (width is < 1 or > 65535 || height is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(width), "X11 client dimensions must be between one and 65535 pixels.");
    }
}
