using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Input.TextInput;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.FreeDesktop;
using Avalonia.Threading;
using static Avalonia.X11.XLib;

namespace Avalonia.X11;

[PrivateApi]
public sealed class ExternalWindowBridge : IDisposable
{
    private readonly AvaloniaX11Platform _platform;
    private readonly X11Window _window;
    private readonly IntPtr _handle;
    private bool _captured;
    private bool _disposed;
    private static Observer? s_observer;

    public ExternalWindowBridge(IWindowImpl window) : this(window, null) { }

    public ExternalWindowBridge(IWindowImpl window, Control? automationRoot)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window = window as X11Window ?? throw new ArgumentException("The native peer requires an X11 platform window.", nameof(window));
        _platform = AvaloniaLocator.Current.GetRequiredService<AvaloniaX11Platform>();
        _handle = window.Handle?.Handle ?? throw new InvalidOperationException("The X11 window has no native handle.");
        window.SetInputRoot(new InputRoot(automationRoot ?? new InputElement()));
        s_observer ??= new Observer(_platform);
        s_observer.Windows.Add(_handle, this);
    }

    public event Action<bool>? FocusChanged;
    public event Action? CaptureLost;

    public async Task<OpenFilePickerResult> OpenFilePickerAsync(FilePickerOpenOptions options, string? directory, CancellationToken cancellationToken)
    {
        var provider = await CreatePortalProviderAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(directory))).ConfigureAwait(true);
        return await provider.OpenFilePickerWithResultAsync(options).ConfigureAwait(true);
    }

    public async Task<SaveFilePickerResult> SaveFilePickerAsync(FilePickerSaveOptions options, string? directory, CancellationToken cancellationToken)
    {
        var provider = await CreatePortalProviderAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(directory))).ConfigureAwait(true);
        return await provider.SaveFilePickerWithResultAsync(options).ConfigureAwait(true);
    }

    private async Task<IStorageProvider> CreatePortalProviderAsync(CancellationToken cancellationToken)
    {
        Verify();
        cancellationToken.ThrowIfCancellationRequested();
        var provider = await PortablePortalStorageProvider.TryCreateAsync(() =>
        {
            Verify();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IPortalParentLease?>(new TrivialPortalParentLease($"x11:{_handle:X}"));
        }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Verify();
        return provider ?? throw new PlatformNotSupportedException("The desktop file chooser portal is unavailable; GTK fallback is not enabled.");
    }

    public void RequestClientSize(Size clientSize)
    {
        Verify();
        _window.RequestClientSizeFromExternalHost(clientSize);
    }

    public (PixelSize ClientSize, PixelPoint ClientOrigin, Thickness FrameExtents, WindowState State) ReadMetrics()
    {
        Verify();
        var attributes = new XWindowAttributes();
        if (XGetWindowAttributes(_platform.Display, _handle, ref attributes) == 0 || attributes.width <= 0 || attributes.height <= 0)
            throw new InvalidOperationException("Cannot query valid native X11 client dimensions.");
        if (!XTranslateCoordinates(_platform.Display, _handle, _platform.Info.RootWindow, 0, 0,
                out int rootX, out int rootY, out _))
            throw new InvalidOperationException("Cannot query the native X11 client screen origin.");
        var state = ReadWindowState();
        var property = _platform.Info.Atoms._NET_FRAME_EXTENTS;
        if (XGetWindowProperty(_platform.Display, _handle, property, IntPtr.Zero, (IntPtr)4, false,
                (IntPtr)Atom.XA_CARDINAL, out var type, out var format, out var count, out var remaining, out var data) != 0)
            throw new InvalidOperationException("Cannot query native X11 frame extents.");
        try
        {
            var extents = default(Thickness);
            if (type != IntPtr.Zero)
            {
                if (type != (IntPtr)Atom.XA_CARDINAL || format != 32 || count.ToInt64() != 4 || remaining != IntPtr.Zero || data == IntPtr.Zero)
                    throw new InvalidOperationException("The native X11 frame extents are malformed.");
                long[] values = new long[4];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = Marshal.ReadIntPtr(data, index * IntPtr.Size).ToInt64();
                    if (values[index] < 0 || values[index] > int.MaxValue)
                        throw new InvalidOperationException("The native X11 frame extent is outside the supported range.");
                }
                extents = new Thickness(values[0], values[2], values[1], values[3]);
            }
            return (new PixelSize(attributes.width, attributes.height), new PixelPoint(rootX, rootY), extents, state);
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    public bool AcquireFocus(bool checkOnly)
    {
        Verify();
        XGetInputFocus(_platform.Display, out var focused, out _);
        if (focused == _handle || checkOnly) return focused == _handle;
        var attributes = new XWindowAttributes();
        if (XGetWindowAttributes(_platform.Display, _handle, ref attributes) == 0 || attributes.map_state != MapState.IsViewable)
            return false;
        XSetInputFocus(_platform.Display, _handle, RevertTo.Parent, IntPtr.Zero);
        XSync(_platform.Display, false);
        XGetInputFocus(_platform.Display, out focused, out _);
        return focused == _handle;
    }

    public bool CaptureMouse()
    {
        Verify();
        if (_captured) return true;
        var result = XGrabPointer(_platform.Display, _handle, false,
            EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.PointerMotionMask |
            EventMask.EnterWindowMask | EventMask.LeaveWindowMask,
            GrabMode.GrabModeAsync, GrabMode.GrabModeAsync, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        XFlush(_platform.Display);
        return _captured = result == GrabResult.GrabSuccess;
    }

    public void ReleaseMouseCapture()
    {
        Verify();
        if (!_captured) return;
        XUngrabPointer(_platform.Display, IntPtr.Zero);
        XFlush(_platform.Display);
        _captured = false;
    }

    public IWindowImpl? WindowAt(PixelPoint screenPoint)
    {
        Verify();
        var root = _platform.Info.RootWindow;
        if (!XTranslateCoordinates(_platform.Display, root, root, screenPoint.X, screenPoint.Y,
                out _, out _, out var child)) return null;
        while (child != IntPtr.Zero)
        {
            if (_platform.Windows.TryGetValue(child, out var info) && info.Window is { } window) return window;
            if (!XTranslateCoordinates(_platform.Display, root, child, screenPoint.X, screenPoint.Y,
                    out _, out _, out child)) return null;
        }
        return null;
    }

    public WindowState ReadWindowState()
    {
        Verify();
        var atoms = _platform.Info.Atoms;
        if (XGetWindowProperty(_platform.Display, _handle, atoms._NET_WM_STATE, IntPtr.Zero,
                (IntPtr)128, false, (IntPtr)Atom.XA_ATOM, out var type, out var format,
                out var count, out var remaining, out var data) != 0)
            throw new InvalidOperationException("Cannot query the native X11 window state.");
        try
        {
            if (type == IntPtr.Zero) return WindowState.Normal;
            if (type != (IntPtr)Atom.XA_ATOM || format != 32 || count.ToInt64() > 128 || remaining != IntPtr.Zero)
                throw new InvalidOperationException("The native X11 window state property is malformed.");
            bool horizontal = false, vertical = false, minimized = false, fullScreen = false;
            for (int i = 0; i < count.ToInt64(); i++)
            {
                var atom = Marshal.ReadIntPtr(data, i * IntPtr.Size);
                horizontal |= atom == atoms._NET_WM_STATE_MAXIMIZED_HORZ;
                vertical |= atom == atoms._NET_WM_STATE_MAXIMIZED_VERT;
                minimized |= atom == atoms._NET_WM_STATE_HIDDEN;
                fullScreen |= atom == atoms._NET_WM_STATE_FULLSCREEN;
            }
            return minimized ? WindowState.Minimized : fullScreen ? WindowState.FullScreen :
                horizontal && vertical ? WindowState.Maximized : WindowState.Normal;
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    public IReadOnlyDictionary<Key, int> ReadKeyboardState()
    {
        Verify();
        var map = new byte[32];
        if (XQueryKeymap(_platform.Display, map) == 0)
            throw new InvalidOperationException("XQueryKeymap failed.");
        var states = new Dictionary<Key, int>();
        for (var code = 8; code < 256; code++)
        {
            if (!XkbLookupKeySym(_platform.Display, code, 0, out _, out var symbol)) continue;
            var key = X11KeyTransform.KeyFromX11Key((X11Key)symbol);
            if (key != Key.None && (map[code >> 3] & (1 << (code & 7))) != 0)
                states[key] = 1;
        }
        ReadLock("Caps Lock", Key.CapsLock);
        ReadLock("Num Lock", Key.NumLock);
        ReadLock("Scroll Lock", Key.Scroll);
        return states;

        void ReadLock(string name, Key key)
        {
            var atom = XInternAtom(_platform.Display, name, true);
            if (atom == IntPtr.Zero || XkbGetNamedIndicator(_platform.Display, atom, out _, out var state,
                    IntPtr.Zero, out _) == 0)
                throw new PlatformNotSupportedException("XKB does not expose the lock indicator '" + name + "'.");
            if (state != 0) states[key] = states.GetValueOrDefault(key) | 2;
        }
    }

    public unsafe (Point Position, RawInputModifiers Buttons) ReadPointerState()
    {
        Verify();
        if (_platform.XI2 is null || XIGetClientPointer(_platform.Display, _handle, out var device) == 0)
            throw new PlatformNotSupportedException("Native five-button pointer snapshots require XInput2 client-pointer queries.");
        XIButtonState buttons = default;
        try
        {
            if (XIQueryPointer(_platform.Display, device, _handle, out _, out _, out _, out _,
                    out var x, out var y, out buttons, out _, out _) == 0)
                throw new InvalidOperationException("XIQueryPointer could not query the native pointer on this screen.");
            if (buttons.MaskLen < 0 || buttons.MaskLen > 4096 || (buttons.MaskLen > 0 && buttons.Mask == null))
                throw new InvalidOperationException("XIQueryPointer returned an invalid native button bitmap.");
            RawInputModifiers modifiers = 0;
            Add(1, RawInputModifiers.LeftMouseButton); Add(3, RawInputModifiers.RightMouseButton);
            Add(2, RawInputModifiers.MiddleMouseButton); Add(8, RawInputModifiers.XButton1MouseButton);
            Add(9, RawInputModifiers.XButton2MouseButton);
            return (new Point(x, y), modifiers);

            void Add(int button, RawInputModifiers flag)
            {
                if ((button >> 3) < buttons.MaskLen && (buttons.Mask[button >> 3] & (1 << (button & 7))) != 0)
                    modifiers |= flag;
            }
        }
        finally { if (buttons.Mask != null) XFree(buttons.Mask); }
    }

    public static (byte[] Data, string Origin) ReadPointerSettingsProperty()
    {
        Dispatcher.UIThread.VerifyAccess();
        var platform = AvaloniaLocator.Current.GetRequiredService<AvaloniaX11Platform>();
        var selection = XInternAtom(platform.Display, "_XSETTINGS_S" + XDefaultScreen(platform.Display), true);
        var owner = selection == IntPtr.Zero ? IntPtr.Zero : XGetSelectionOwner(platform.Display, selection);
        if (owner == IntPtr.Zero)
            throw new PlatformNotSupportedException("No native XSETTINGS owner supplies pointer double-click preferences.");
        var property = XInternAtom(platform.Display, "_XSETTINGS_SETTINGS", true);
        if (property == IntPtr.Zero || XGetWindowProperty(platform.Display, owner, property, IntPtr.Zero,
                (IntPtr)(65536 / 4), false, property, out var actualType, out var format, out var items,
                out var remaining, out var data) != 0)
            throw new PlatformNotSupportedException("Cannot read the native XSETTINGS pointer preferences.");
        try
        {
            if (actualType != property || format != 8 || remaining != IntPtr.Zero || items.ToInt64() > 65536)
                throw new InvalidOperationException("The native XSETTINGS property has an invalid type, format or size.");
            var bytes = new byte[checked((int)items.ToInt64())];
            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            return (bytes, "_XSETTINGS_S" + XDefaultScreen(platform.Display) + "/owner=0x" + owner.ToInt64().ToString("x") + "/_XSETTINGS_SETTINGS");
        }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }

    public static void VerifyFramebufferUpload()
    {
        var platform = AvaloniaLocator.Current.GetRequiredService<AvaloniaX11Platform>();
        XSync(platform.DeferredDisplay, false);
        if (XError.LastError.error_code != 0)
        {
            var error = XError.LastError;
            XError.ThrowLastError("Native framebuffer synchronization observed X11 request " + error.request_code +
                "/" + error.minor_code + " on resource 0x" + error.resourceid.ToInt64().ToString("x"));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Verify();
        ReleaseMouseCapture();
        _disposed = true;
        s_observer!.Windows.Remove(_handle);
        if (s_observer.Windows.Count == 0)
        {
            if (ReferenceEquals(_platform.DispatcherImpl.EventDispatcher.EventHook, s_observer))
                _platform.DispatcherImpl.EventDispatcher.EventHook = s_observer.Previous;
            s_observer = null;
        }
    }

    private void Verify()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class InputRoot(InputElement root) : IInputRoot
    {
        public IFocusManager? FocusManager => null;
        public IInputElement? PointerOverElement { get; set; }
        public IInputElement? CursorElement { get; set; }
        public ITextInputMethodImpl? InputMethod => null;
        public InputElement RootElement { get; } = root;
        public InputElement FocusRoot => RootElement;
        public void PointerOverInvalidated() { }
    }

    private sealed class Observer : X11EventDispatcher.IEventHook
    {
        public Dictionary<IntPtr, ExternalWindowBridge> Windows { get; } = new();
        public X11EventDispatcher.IEventHook? Previous { get; }

        public Observer(AvaloniaX11Platform platform)
        {
            Previous = platform.DispatcherImpl.EventDispatcher.EventHook;
            platform.DispatcherImpl.EventDispatcher.EventHook = this;
        }

        public bool TryHandleEvent(in XEvent evt)
        {
            if (Windows.TryGetValue(evt.AnyEvent.window, out var peer))
            {
                if (evt.type == XEventName.FocusIn && evt.FocusChangeEvent.mode == (int)NotifyMode.NotifyNormal)
                    peer.FocusChanged?.Invoke(true);
                else if (evt.type == XEventName.FocusOut && evt.FocusChangeEvent.mode == (int)NotifyMode.NotifyNormal)
                    peer.FocusChanged?.Invoke(false);
                if (peer._captured && (evt.type == XEventName.UnmapNotify || evt.type == XEventName.DestroyNotify ||
                    ((evt.type == XEventName.EnterNotify || evt.type == XEventName.LeaveNotify) &&
                     evt.CrossingEvent.mode == NotifyMode.NotifyUngrab)))
                {
                    peer._captured = false;
                    peer.CaptureLost?.Invoke();
                }
            }
            return Previous?.TryHandleEvent(in evt) == true;
        }
    }

    [DllImport("libX11.so.6")]
    private static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revert);
    [DllImport("libX11.so.6")]
    private static extern int XQueryKeymap(IntPtr display, [Out] byte[] keys);
    [DllImport("libX11.so.6")]
    private static extern int XkbGetNamedIndicator(IntPtr display, IntPtr name, out int index, out int state, IntPtr map, out int real);
    [DllImport("libXi.so.6")]
    private static extern int XIGetClientPointer(IntPtr display, IntPtr window, out int device);
    [DllImport("libXi.so.6")]
    private static extern unsafe int XIQueryPointer(IntPtr display, int device, IntPtr window, out IntPtr root,
        out IntPtr child, out double rootX, out double rootY, out double clientX, out double clientY,
        out XIButtonState buttons, out XIModifierState modifiers, out XIModifierState group);
}

internal unsafe partial class X11Window
{
    // Pinned Resize request body, without its cached-equality early return or force=true cache mutation.
    internal void RequestClientSizeFromExternalHost(Size clientSize)
    {
        bool needImmediatePopupResize = clientSize != ClientSize;
        var pixelSize = ToPixelSize(clientSize);
        if (pixelSize.Width < 1 || pixelSize.Height < 1)
            throw new PlatformNotSupportedException("The initial portable X11 Window profile requires at least one native client pixel on each axis; zero or subpixel client extents are unsupported.");
        UpdateSizeHints(pixelSize);
        XConfigureResizeWindow(_x11.Display, _handle, pixelSize);
        if (_useRenderWindow && !_useCompositorDrivenRenderWindowResize)
            XConfigureResizeWindow(_x11.Display, _renderHandle, pixelSize);
        XFlush(_x11.Display);
        if (!_wasMappedAtLeastOnce || (_overrideRedirect && needImmediatePopupResize))
        {
            _realSize = pixelSize;
            Resized?.Invoke(ClientSize, WindowResizeReason.Layout);
        }
    }
}
