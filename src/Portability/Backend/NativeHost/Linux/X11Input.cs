extern alias WpfFork;

using System.Runtime.InteropServices;
using System.Text;
using WpfFork::MS.Internal.Portability.Input;
using WpfFork::System.Windows.Input;
using WpfPoint = WpfFork::System.Windows.Point;

namespace Wpf.Portable.NativeHost.Linux;

internal readonly record struct X11KeyInput(int Timestamp, int VirtualKey, bool IsDown, bool IsAltDown);
internal readonly record struct X11PointerInput(int Timestamp, int X, int Y, int Wheel, RawMouseActions Actions, bool IsLeave);

internal sealed class X11Input : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly X11Window _window;
    private readonly nuint _handle;
    private readonly PointerRouter _pointer;
    private readonly X11Cursor _cursor;
    // A queued release keeps its press identity even if NumLock or the layout changes first.
    private readonly Dictionary<byte, int> _pressedVirtualKeys = new();
    private nint _compose;
    private int _generation;
    private bool _disposed;

    internal X11Input(X11Window window)
    {
        _window = window;
        _handle = window.Handle;
        window.Connection.VerifyOwner();
        _cursor = new(window);
        CreateComposeState();
        try { _pointer = PointerRouter.Attach(this); }
        catch { X11InputNative.xkb_compose_state_unref(_compose); _compose = 0; _cursor.Dispose(); throw; }
        window.RawEvent += OnEvent;
        window.Closed += Dispose;
    }

    internal event Action<X11KeyInput>? KeyInput;
    internal event Action<int, string>? TextInput;
    internal event Action<X11PointerInput>? PointerInput;
    internal event Action? CaptureLost;

    private void Verify()
    {
        _window.Connection.VerifyOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void CreateComposeState()
    {
        var context = X11InputNative.xkb_context_new(0);
        if (context == 0) throw new InvalidOperationException("Cannot create the native keyboard compose context.");
        nint table = 0;
        try
        {
            var locale = Environment.GetEnvironmentVariable("LC_ALL");
            if (string.IsNullOrEmpty(locale)) locale = Environment.GetEnvironmentVariable("LC_CTYPE");
            if (string.IsNullOrEmpty(locale)) locale = Environment.GetEnvironmentVariable("LANG");
            table = X11InputNative.xkb_compose_table_new_from_locale(context, string.IsNullOrEmpty(locale) ? "C.UTF-8" : locale, 0);
            if (table == 0 || (_compose = X11InputNative.xkb_compose_state_new(table, 0)) == 0)
                throw new InvalidOperationException("Cannot create the native keyboard compose state.");
        }
        finally
        {
            if (table != 0) X11InputNative.xkb_compose_table_unref(table);
            X11InputNative.xkb_context_unref(context);
        }
    }

    private void OnEvent(XEvent nativeEvent)
    {
        if (_disposed) return;
        switch (nativeEvent.Type)
        {
            case 2 or 3:
                if (_window.IsVisible) OnKey(nativeEvent.Key);
                break;
            case 7 or 8:
                // Core crossings also describe grab loss when XI2 pointer delivery is selected.
                _pointer.Crossing(this, nativeEvent.Crossing.Serial, nativeEvent.Crossing.Mode, _pointer.Device(this));
                break;
            case 10 when nativeEvent.Focus.Mode is 0 or 3 && nativeEvent.Focus.Detail != 2:
                _pressedVirtualKeys.Clear();
                ResetCompose();
                break;
            case 18:
                _pressedVirtualKeys.Clear();
                ResetCompose();
                _pointer.Unmapped(this);
                break;
        }
    }

    private void OnKey(XKeyEvent key)
    {
        var display = _window.Connection.Display;
        var code = checked((byte)key.Keycode);
        var symbol = X11InputNative.XkbKeycodeToKeysym(display, code, 0, 0);
        bool translated = X11InputNative.XkbLookupKeySym(display, code, key.State, out uint consumed, out nuint textSymbol) != 0;
        int virtualKey = VirtualKey(translated && IsKeypad(symbol) ? textSymbol : symbol);
        uint altMask = AltMask();
        bool down = key.Type == 2;
        if (down)
        {
            if (_pressedVirtualKeys.TryGetValue(code, out int pressed)) virtualKey = pressed;
            else _pressedVirtualKeys.Add(code, virtualKey);
        }
        else if (_pressedVirtualKeys.Remove(code, out int pressed)) virtualKey = pressed;
        int generation = _generation;
        KeyInput?.Invoke(new(unchecked((int)(uint)key.Time), virtualKey, down, (key.State & altMask) != 0));
        if (!down || _disposed || generation != _generation || !_window.IsVisible) return;
        if (!translated) return;
        symbol = textSymbol;
        if ((key.State & ~consumed & (4u | altMask)) != 0) return;
        if ((symbol & 0xff000000u) == 0x01000000u)
        {
            nuint scalar = symbol & 0x00ffffffu;
            if (scalar > 0x10ffff || scalar is >= 0xd800 and <= 0xdfff) return;
        }
        X11InputNative.xkb_compose_state_feed(_compose, checked((uint)symbol));
        int status = X11InputNative.xkb_compose_state_get_status(_compose);
        if (status == 1) return; // A dead key or Multi_key sequence is still incomplete.
        string text;
        if (status == 2)
        {
            int length = X11InputNative.xkb_compose_state_get_utf8(_compose, null, 0);
            if (length < 0 || length > 1024 * 1024) throw new InvalidOperationException("The native compose commit has an invalid length.");
            var bytes = new byte[length + 1];
            int written = X11InputNative.xkb_compose_state_get_utf8(_compose, bytes, (nuint)bytes.Length);
            if (written != length) throw new InvalidOperationException("The native compose commit changed while being read.");
            text = Utf8.GetString(bytes, 0, length);
            X11InputNative.xkb_compose_state_reset(_compose);
        }
        else if (status == 3)
        {
            X11InputNative.xkb_compose_state_reset(_compose);
            return;
        }
        else
        {
            var bytes = new byte[8];
            int length = X11InputNative.xkb_keysym_to_utf8(checked((uint)symbol), bytes, (nuint)bytes.Length);
            if (length <= 1) return;
            if (length > bytes.Length || bytes[length - 1] != 0) throw new InvalidOperationException("The native keysym text is malformed.");
            text = Utf8.GetString(bytes, 0, length - 1);
        }
        foreach (var rune in text.EnumerateRunes()) if (Rune.IsControl(rune)) return;
        if (text.Length != 0 && !_disposed && generation == _generation)
            TextInput?.Invoke(unchecked((int)(uint)key.Time), text);
    }

    private uint AltMask()
    {
        var map = X11InputNative.XGetModifierMapping(_window.Connection.Display);
        if (map == 0) throw new InvalidOperationException("Cannot query the native modifier map.");
        try
        {
            var modifiers = Marshal.PtrToStructure<XModifierKeymap>(map);
            if (modifiers.KeysPerModifier < 0 || modifiers.KeysPerModifier > 256 || (modifiers.KeysPerModifier != 0 && modifiers.Keys == 0))
                throw new InvalidOperationException("The native modifier map is malformed.");
            uint result = 0;
            for (int modifier = 0; modifier < 8; modifier++)
                for (int index = 0; index < modifiers.KeysPerModifier; index++)
                {
                    byte code = Marshal.ReadByte(modifiers.Keys, modifier * modifiers.KeysPerModifier + index);
                    nuint symbol = code == 0 ? 0 : X11InputNative.XkbKeycodeToKeysym(_window.Connection.Display, code, 0, 0);
                    if (symbol is 0xffe9 or 0xffea) result |= 1u << modifier;
                }
            return result;
        }
        finally { X11InputNative.XFreeModifiermap(map); }
    }

    internal IReadOnlyDictionary<Key, KeyStates> ReadKeyboardState()
    {
        Verify();
        var map = new byte[32];
        if (X11InputNative.XQueryKeymap(_window.Connection.Display, map) == 0) throw new InvalidOperationException("XQueryKeymap failed.");
        var states = new Dictionary<Key, KeyStates>();
        uint? lookupState = null;
        for (int code = 8; code < 256; code++)
        {
            if ((map[code >> 3] & (1 << (code & 7))) == 0) continue;
            if (!_pressedVirtualKeys.TryGetValue((byte)code, out int vk))
            {
                nuint symbol = X11InputNative.XkbKeycodeToKeysym(_window.Connection.Display, (byte)code, 0, 0);
                if (IsKeypad(symbol))
                {
                    lookupState ??= ReadKeyboardLookupState();
                    if (X11InputNative.XkbLookupKeySym(_window.Connection.Display, (byte)code, lookupState.Value, out _, out nuint effective) != 0)
                        symbol = effective;
                }
                _pressedVirtualKeys.Add((byte)code, vk = VirtualKey(symbol));
            }
            var key = KeyInterop.KeyFromVirtualKey(vk);
            if (key != Key.None) states[key] = KeyStates.Down;
        }
        ReadLock("Caps Lock", Key.CapsLock);
        ReadLock("Num Lock", Key.NumLock);
        ReadLock("Scroll Lock", Key.Scroll);
        return states;

        void ReadLock(string name, Key key)
        {
            if (X11InputNative.XkbGetNamedIndicator(_window.Connection.Display, _window.Connection.GetAtom(name), out _, out int state, 0, out _) == 0)
                throw new PlatformNotSupportedException("XKB does not expose the lock indicator '" + name + "'.");
            if (state != 0) states[key] = states.GetValueOrDefault(key) | KeyStates.Toggled;
        }
    }

    private uint ReadKeyboardLookupState()
    {
        XIButtonState buttons = default;
        try
        {
            if (Xi2Native.XIQueryPointer(_window.Connection.Display, _pointer.Device(this), _handle, out _, out _, out _, out _, out _, out _,
                    out buttons, out var modifiers, out var group) == 0)
                throw new InvalidOperationException("Cannot query the paired keyboard's native modifier state.");
            if ((modifiers.Effective & ~255) != 0 || group.Effective is < 0 or > 3)
                throw new InvalidOperationException("The native keyboard lookup state is malformed.");
            return (uint)modifiers.Effective | ((uint)group.Effective << 13);
        }
        finally { if (buttons.Mask != 0) X11Native.XFree(buttons.Mask); }
    }

    internal PortablePointerSnapshot ReadPointerState()
    {
        Verify();
        XIButtonState buttons = default;
        try
        {
            if (Xi2Native.XIQueryPointer(_window.Connection.Display, _pointer.Device(this), _window.Handle, out _, out _, out _, out _,
                    out double x, out double y, out buttons, out _, out _) == 0)
                throw new InvalidOperationException("Cannot query the native master pointer on this screen.");
            if (buttons.MaskLength < 0 || buttons.MaskLength > 4096 || (buttons.MaskLength != 0 && buttons.Mask == 0))
                throw new InvalidOperationException("XIQueryPointer returned a malformed button bitmap.");
            return new(new WpfPoint(x, y), State(1), State(3), State(2), State(8), State(9));

            MouseButtonState State(int button) => (button >> 3) < buttons.MaskLength &&
                (Marshal.ReadByte(buttons.Mask, button >> 3) & (1 << (button & 7))) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released;
        }
        finally { if (buttons.Mask != 0) X11Native.XFree(buttons.Mask); }
    }

    internal bool CaptureMouse() { Verify(); return _window.IsVisible && _pointer.Capture(this); }
    internal void ReleaseMouseCapture() { _window.Connection.VerifyOwner(); if (!_disposed) _pointer.Release(this); }
    internal bool SetCursor(Cursor cursor) { Verify(); return _cursor.SetCursor(cursor); }
    internal bool SetCursorVisibility(bool visible) { Verify(); return _cursor.SetVisibility(visible); }

    internal nuint WindowAt(int physicalScreenX, int physicalScreenY)
    {
        Verify();
        var connection = _window.Connection;
        nuint parent = connection.RootWindow;
        var visited = new HashSet<nuint>();
        while (visited.Add(parent) && visited.Count <= 256)
        {
            if (_pointer.HasWindow(parent)) return parent;
            nuint child = 0;
            int translated = 0;
            // Foreign WM frames can disappear between queries. Only BadWindow is an expected race.
            if (!connection.TryForeignRequest(() => translated = X11InputNative.XTranslateCoordinates(connection.Display,
                    connection.RootWindow, parent, physicalScreenX, physicalScreenY, out _, out _, out child)) || translated == 0 || child == 0) return 0;
            parent = child;
        }
        return 0;
    }

    private void ResetCompose()
    {
        _generation++;
        if (_compose != 0) X11InputNative.xkb_compose_state_reset(_compose);
    }

    internal void ResetComposition() { Verify(); ResetCompose(); }

    public void Dispose()
    {
        if (_disposed) return;
        _window.Connection.VerifyOwner();
        _disposed = true;
        _generation++;
        _window.RawEvent -= OnEvent;
        _window.Closed -= Dispose;
        _pointer.Detach(this);
        _cursor.Dispose();
        X11InputNative.xkb_compose_state_unref(_compose);
        _compose = 0;
        KeyInput = null;
        TextInput = null;
        PointerInput = null;
        CaptureLost = null;
    }

    private static int VirtualKey(nuint symbol)
    {
        if (symbol is >= 0x61 and <= 0x7a) return (int)symbol - 0x20;
        if (symbol is >= 0x41 and <= 0x5a or >= 0x30 and <= 0x39) return (int)symbol;
        if (symbol is >= 0xffbe and <= 0xffd5) return 0x70 + (int)(symbol - 0xffbe);
        if (symbol is >= 0xffb0 and <= 0xffb9) return 0x60 + (int)(symbol - 0xffb0);
        return symbol switch
        {
            0xff08 => 0x08, 0xff09 or 0xfe20 => 0x09, 0xff0b => 0x0c, 0xff0d or 0xff8d => 0x0d,
            0xff13 => 0x13, 0xff14 => 0x91, 0xff1b => 0x1b, 0x20 => 0x20,
            0xff50 or 0xff95 => 0x24, 0xff51 or 0xff96 => 0x25, 0xff52 or 0xff97 => 0x26,
            0xff53 or 0xff98 => 0x27, 0xff54 or 0xff99 => 0x28, 0xff55 or 0xff9a => 0x21,
            0xff56 or 0xff9b => 0x22, 0xff57 or 0xff9c => 0x23, 0xff58 or 0xff9d => 0x0c,
            0xff60 => 0x29, 0xff61 => 0x2c, 0xff63 or 0xff9e => 0x2d, 0xffff or 0xff9f => 0x2e,
            0xff67 => 0x5d, 0xff6a => 0x2f, 0xff7f => 0x90,
            0xffaa => 0x6a, 0xffab => 0x6b, 0xffac => 0x6c, 0xffad => 0x6d, 0xffae => 0x6e, 0xffaf => 0x6f,
            0xffe1 => 0xa0, 0xffe2 => 0xa1, 0xffe3 => 0xa2, 0xffe4 => 0xa3,
            0xffe5 or 0xffe6 => 0x14, 0xffe9 => 0xa4, 0xffea or 0xfe03 => 0xa5, 0xffeb => 0x5b, 0xffec => 0x5c,
            0x3b or 0x3a => 0xba, 0x3d or 0x2b => 0xbb, 0x2c or 0x3c => 0xbc, 0x2d or 0x5f => 0xbd,
            0x2e or 0x3e => 0xbe, 0x2f or 0x3f => 0xbf, 0x60 or 0x7e => 0xc0, 0x5b or 0x7b => 0xdb,
            0x5c or 0x7c => 0xdc, 0x5d or 0x7d => 0xdd, 0x27 or 0x22 => 0xde,
            0x1008ff11 => 0xae, 0x1008ff12 => 0xad, 0x1008ff13 => 0xaf,
            0x1008ff14 => 0xb3, 0x1008ff15 => 0xb2, 0x1008ff16 => 0xb1, 0x1008ff17 => 0xb0,
            _ => 0
        };
    }

    private static bool IsKeypad(nuint symbol) => symbol is >= 0xff80 and <= 0xffbd;

    private sealed class PointerRouter
    {
        [ThreadStatic] private static Dictionary<X11Connection, PointerRouter>? s_routers;
        private static Dictionary<X11Connection, PointerRouter> Routers => s_routers ??= new();
        private readonly X11Connection _connection;
        private readonly int _opcode;
        private readonly int _keyboardEvent;
        private readonly Dictionary<nuint, (X11Input Input, int Device)> _windows = new();
        private X11Input? _captureOwner;
        private nuint _captureSerial;

        private PointerRouter(X11Connection connection)
        {
            _connection = connection;
            if (X11Native.XQueryExtension(connection.Display, "XInputExtension", out _opcode, out _, out _) == 0)
                throw new PlatformNotSupportedException("The owned input backend requires XInput2.");
            int major = 2, minor = 0;
            if (Xi2Native.XIQueryVersion(connection.Display, ref major, ref minor) != 0 || major < 2)
                throw new PlatformNotSupportedException("The X server does not support XInput2.");
            if (X11InputNative.XkbSetDetectableAutoRepeat(connection.Display, 1, out int supported) == 0 || supported == 0)
                throw new PlatformNotSupportedException("The X server does not support detectable keyboard repeat.");
            major = 1; minor = 0;
            if (X11InputNative.XkbQueryExtension(connection.Display, out _, out _keyboardEvent, out _, ref major, ref minor) == 0 ||
                X11InputNative.XkbSelectEvents(connection.Display, 0x100, 2, 2) == 0)
                throw new PlatformNotSupportedException("The X server does not support XKB keyboard mapping notifications.");
            connection.RegisterExtension(_opcode, OnExtension);
            connection.RegisterEvent(34, OnMapping);
            connection.RegisterEvent(_keyboardEvent, OnMapping);
        }

        private void OnMapping(XEvent nativeEvent)
        {
            if (nativeEvent.Type == 34) X11InputNative.XRefreshKeyboardMapping(ref nativeEvent);
            else
            {
                var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref nativeEvent, 1));
                if (MemoryMarshal.Read<XkbEventHeader>(bytes).KeyboardEventType != 1) return;
                if (X11InputNative.XkbRefreshKeyboardMapping(ref nativeEvent) != 0)
                    throw new InvalidOperationException("Cannot refresh the native XKB keyboard mapping.");
            }
            foreach (var entry in _windows.Values.ToArray())
                if (!entry.Input._disposed) entry.Input.ResetCompose();
        }

        internal static unsafe PointerRouter Attach(X11Input input)
        {
            var connection = input._window.Connection;
            if (!Routers.TryGetValue(connection, out var router)) Routers.Add(connection, router = new(connection));
            bool added = false;
            try
            {
                if (Xi2Native.XIGetClientPointer(connection.Display, input._window.Handle, out int device) == 0 || device <= 1)
                    throw new PlatformNotSupportedException("The X server does not expose a client master pointer.");
                uint bits = PointerMask;
                var mask = new XIEventMask { DeviceId = device, MaskLength = sizeof(uint), Mask = (nint)(&bits) };
                if (Xi2Native.XISelectEvents(connection.Display, input._window.Handle, ref mask, 1) != 0)
                    throw new InvalidOperationException("Cannot select native master pointer events.");
                router._windows.Add(input._handle, (input, device));
                added = true;
                connection.Flush();
                return router;
            }
            catch
            {
                if (added) router._windows.Remove(input._handle);
                if (router._windows.Count == 0) router.Unregister();
                throw;
            }
        }

        private const uint PointerMask = (1u << Xi2Native.ButtonPress) | (1u << Xi2Native.ButtonRelease) | (1u << Xi2Native.Motion) | (1u << Xi2Native.Enter) | (1u << Xi2Native.Leave);
        internal int Device(X11Input input) => _windows[input._handle].Device;
        internal bool HasWindow(nuint window) => _windows.ContainsKey(window);

        internal unsafe bool Capture(X11Input input)
        {
            if (_captureOwner == input) return true;
            int device = Device(input);
            if (_captureOwner != null && Device(_captureOwner) != device)
                throw new PlatformNotSupportedException("Pointer capture cannot transfer between different master devices.");
            uint bits = PointerMask;
            var mask = new XIEventMask { DeviceId = device, MaskLength = sizeof(uint), Mask = (nint)(&bits) };
            nuint serial = X11Native.XNextRequest(_connection.Display);
            if (Xi2Native.XIGrabDevice(_connection.Display, device, input._handle, 0, 0, 1, 1, 0, ref mask) != 0) return false;
            _captureSerial = serial;
            _captureOwner = input;
            _connection.Flush();
            // WPF publishes its new capture before routing loss from the previous element.
            return !input._disposed && _captureOwner == input;
        }

        internal void Release(X11Input input)
        {
            if (_captureOwner != input) return;
            _captureSerial = X11Native.XNextRequest(_connection.Display);
            _captureOwner = null;
            Xi2Native.XIUngrabDevice(_connection.Display, Device(input), 0);
            _connection.Flush();
        }

        internal bool Crossing(X11Input input, nuint serial, int mode, int device)
        {
            if (mode is not (1 or 2)) return true;
            // Xlib serials are unsigned long. Signed subtraction orders them across native rollover.
            if (unchecked((nint)(serial - _captureSerial)) < 0 ||
                (serial == _captureSerial && _captureOwner != null && _captureOwner != input)) return false;
            if (mode == 2 && _captureOwner == input && Device(input) == device)
            {
                _captureOwner = null;
                input.CaptureLost?.Invoke();
            }
            return true;
        }

        internal void Unmapped(X11Input input)
        {
            if (_captureOwner != input) return;
            _captureOwner = null;
            input.CaptureLost?.Invoke();
        }

        private void OnExtension(XEvent nativeEvent)
        {
            var cookie = nativeEvent.GenericCookie;
            if (cookie.Data == 0) return;
            if (cookie.EventType is Xi2Native.Enter or Xi2Native.Leave)
            {
                var crossing = Marshal.PtrToStructure<XIEnterLeaveEvent>(cookie.Data);
                if (!_windows.TryGetValue(crossing.Event, out var target) || target.Input._disposed || target.Device != crossing.DeviceId) return;
                var input = target.Input;
                if (!Crossing(input, crossing.Serial, crossing.Mode, crossing.DeviceId)) return;
                if (input._disposed || !input._window.IsVisible) return;
                if (crossing.Detail is not (0 or 1 or 3 or 4) || AnyButtonDown(crossing.Buttons)) return;
                input.PointerInput?.Invoke(new(unchecked((int)(uint)crossing.Time), Coordinate(crossing.EventX), Coordinate(crossing.EventY), 0,
                    RawMouseActions.AbsoluteMove, cookie.EventType == Xi2Native.Leave));
                return;
            }
            if (cookie.EventType is not (Xi2Native.Motion or Xi2Native.ButtonPress or Xi2Native.ButtonRelease)) return;
            var pointer = Marshal.PtrToStructure<XIDeviceEvent>(cookie.Data);
            if (!_windows.TryGetValue(pointer.Event, out var recipient) || recipient.Input._disposed || !recipient.Input._window.IsVisible || recipient.Device != pointer.DeviceId) return;
            bool down = cookie.EventType == Xi2Native.ButtonPress;
            int wheel = 0;
            RawMouseActions actions = RawMouseActions.AbsoluteMove;
            if (cookie.EventType != Xi2Native.Motion)
            {
                actions |= pointer.Detail switch
                {
                    1 => down ? RawMouseActions.Button1Press : RawMouseActions.Button1Release,
                    2 => down ? RawMouseActions.Button3Press : RawMouseActions.Button3Release,
                    3 => down ? RawMouseActions.Button2Press : RawMouseActions.Button2Release,
                    4 or 5 when down => RawMouseActions.VerticalWheelRotate,
                    6 or 7 when down => RawMouseActions.HorizontalWheelRotate,
                    8 => down ? RawMouseActions.Button4Press : RawMouseActions.Button4Release,
                    9 => down ? RawMouseActions.Button5Press : RawMouseActions.Button5Release,
                    _ => RawMouseActions.None
                };
                if (pointer.Detail is 4 or 5 or 6 or 7)
                {
                    if (!down) return;
                    wheel = pointer.Detail is 4 or 7 ? 120 : -120;
                }
                else if (actions == RawMouseActions.AbsoluteMove) return;
            }
            recipient.Input.PointerInput?.Invoke(new(unchecked((int)(uint)pointer.Time), Coordinate(pointer.EventX), Coordinate(pointer.EventY), wheel, actions, false));
        }

        private static int Coordinate(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

        private static bool AnyButtonDown(XIButtonState buttons)
        {
            if (buttons.MaskLength < 0 || buttons.MaskLength > 4096 || (buttons.MaskLength != 0 && buttons.Mask == 0))
                throw new InvalidOperationException("The native crossing button bitmap is malformed.");
            for (int index = 0; index < buttons.MaskLength; index++)
                if (Marshal.ReadByte(buttons.Mask, index) != 0) return true;
            return false;
        }

        internal void Detach(X11Input input)
        {
            Release(input);
            _windows.Remove(input._handle);
            if (_windows.Count != 0) return;
            Unregister();
        }

        private void Unregister()
        {
            _connection.UnregisterExtension(_opcode);
            _connection.UnregisterEvent(34, OnMapping);
            _connection.UnregisterEvent(_keyboardEvent, OnMapping);
            X11InputNative.XkbSelectEvents(_connection.Display, 0x100, 2, 0);
            Routers.Remove(_connection);
        }
    }
}
