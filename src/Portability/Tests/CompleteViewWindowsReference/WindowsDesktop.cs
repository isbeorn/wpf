using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PackageConsumer;

namespace CompleteViewWindowsReference;

internal sealed class WindowsDesktop(Action<Func<bool>, Func<string>?> wait) : ICompleteViewDesktop
{
    internal ConcurrentQueue<object> Pixels { get; } = new();
    internal ConcurrentQueue<object> DpiObservations { get; } = new();
    internal ConcurrentQueue<object> FocusObservations { get; } = new();
    internal ConcurrentQueue<object> TypingObservations { get; } = new();
    internal ConcurrentQueue<object> ClientSizeObservations { get; } = new();
    private readonly Dictionary<(Window, FrameworkElement, double, double), (int X, int Y, uint Rgb)> _lastPixels = new();
    private int? _pendingDpi;
    internal long PixelReadCount { get; private set; }

    public bool IsMapped(Window window)
    {
        window.VerifyAccess();
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        bool mapped = hwnd != IntPtr.Zero && Native.IsWindowVisible(OwnHandle(window)) && !Native.IsIconic(hwnd);
        if (mapped && _pendingDpi is { } dpi)
        {
            _pendingDpi = null;
            ApplyDpi(window, dpi, "first-mapped-owned-window");
        }
        return mapped;
    }

    public (int Width, int Height) ClientSize(Window window)
    {
        IntPtr hwnd = OwnHandle(window);
        Program.Require(Native.GetClientRect(hwnd, out var client), "Cannot read the owned native client size.");
        var size = (Width: client.Right - client.Left, Height: client.Bottom - client.Top);
        ClientSizeObservations.Enqueue(new { window = window.Title, width = size.Width, height = size.Height,
            dpi = VisualTreeHelper.GetDpi(window).DpiScaleX });
        return size;
    }

    public void Focus(Window window)
    {
        IntPtr hwnd = OwnHandle(window);
        Program.Require(Native.IsWindowVisible(hwnd) && Native.IsWindowEnabled(hwnd), "The owned target is hidden or disabled; no input was injected.");
        bool activated = window.Activate();
        bool requested = Native.SetForegroundWindow(hwnd);
        IntPtr foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint foregroundProcess);
        FocusObservations.Enqueue(new { window = window.Title, target = hwnd.ToInt64(), activated, requested,
            foreground = foreground.ToInt64(), foregroundProcess, ownProcess = Environment.ProcessId });
        wait(() => Native.GetForegroundWindow() == hwnd, () => $"The owned stock reference window could not acquire foreground (target={hwnd}, foreground={Native.GetForegroundWindow()}, activation={activated}, request={requested}); no input was injected.");
    }

    public void Click(Window window, FrameworkElement element)
    {
        IntPtr hwnd = OwnHandle(window);
        if (Native.IsWindowEnabled(hwnd)) Focus(window);
        VerifyForeground(hwnd, allowOwnedModal: true);
        Point point = ScreenPoint(window, element, element.ActualWidth / 2, element.ActualHeight / 2);
        var physical = new Native.Point(checked((int)Math.Round(point.X)), checked((int)Math.Round(point.Y)));
        IntPtr hit = Native.ChildWindowFromPointEx(Native.GetDesktopWindow(), physical, 1);
        Program.Require(IsOwnWindow(hit) && (hit == hwnd || Native.IsChild(hwnd, hit)),
            "The click point is covered by another native window; no input was injected.");
        int width = Native.GetSystemMetrics(78), height = Native.GetSystemMetrics(79);
        int left = Native.GetSystemMetrics(76), top = Native.GetSystemMetrics(77);
        Program.Require(width > 0 && height > 0 && point.X >= left && point.Y >= top && point.X < left + width && point.Y < top + height,
            "The click point lies outside the virtual desktop.");
        int x = checked((int)((point.X - left) * 65536 / width + 65536d / (width * 2)));
        int y = checked((int)((point.Y - top) * 65536 / height + 65536d / (height * 2)));
        bool swapped = Native.GetSystemMetrics(23) != 0;
        Send(hwnd, true, [Mouse(x, y, 0x0001 | 0x8000 | 0x4000 | 0x2000), Mouse(0, 0, swapped ? 8u : 2u), Mouse(0, 0, swapped ? 16u : 4u)]);
    }

    public void ReplaceText(Window window, TextBox editor, string text)
    {
        IntPtr hwnd = OwnHandle(window);
        Focus(window);
        if (!editor.IsKeyboardFocused) Click(window, editor);
        wait(() => editor.IsKeyboardFocused, () => "The native click did not focus the target TextBox.");
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c })
            Program.Require((Native.GetAsyncKeyState(key) & 0x8000) == 0, "A modifier is already held; no typing was injected.");
        Send(hwnd, false, [Key(0x11, false), Key(0x41, false), Key(0x41, true), Key(0x11, true)]);
        wait(() => editor.SelectionLength == editor.Text.Length, () => "Native Ctrl+A did not select the existing text.");
        foreach (char character in text)
        {
            Program.Require(!char.IsSurrogate(character), "The stock reference typing profile accepts BMP text only.");
            short mapping = Native.VkKeyScanEx(character, Native.GetKeyboardLayout(0));
            TypingObservations.Enqueue(new { character = character.ToString(), layout = Native.GetKeyboardLayout(0).ToInt64(), mapping,
                shift = Native.GetAsyncKeyState(0x10), control = Native.GetAsyncKeyState(0x11), alt = Native.GetAsyncKeyState(0x12),
                queuedShift = Native.GetKeyState(0x10), queuedControl = Native.GetKeyState(0x11),
                capsLock = Native.GetKeyState(0x14), numLock = Native.GetKeyState(0x90), scrollLock = Native.GetKeyState(0x91),
                wpfModifiers = System.Windows.Input.Keyboard.Modifiers.ToString() });
            if (mapping == -1)
            {
                Send(hwnd, false, [Unicode(character, false), Unicode(character, true)]);
                continue;
            }
            int modifiers = (mapping >> 8) & 7;
            var inputs = new List<Native.Input>();
            foreach (var modifier in new[] { (Mask: 1, Key: 0x10), (Mask: 2, Key: 0x11), (Mask: 4, Key: 0x12) })
                if ((modifiers & modifier.Mask) != 0) inputs.Add(Key(modifier.Key, false));
            inputs.Add(Key(mapping & 0xff, false)); inputs.Add(Key(mapping & 0xff, true));
            foreach (var modifier in new[] { (Mask: 4, Key: 0x12), (Mask: 2, Key: 0x11), (Mask: 1, Key: 0x10) })
                if ((modifiers & modifier.Mask) != 0) inputs.Add(Key(modifier.Key, true));
            Send(hwnd, false, inputs.ToArray());
        }
    }

    public uint Pixel(Window window, FrameworkElement element, double x, double y)
    {
        IntPtr hwnd = OwnHandle(window);
        Point point = ScreenPoint(window, element, x, y);
        var screen = new Native.Point(checked((int)Math.Round(point.X)), checked((int)Math.Round(point.Y)));
        IntPtr hit = Native.ChildWindowFromPointEx(Native.GetDesktopWindow(), screen, 1);
        Program.Require(IsOwnWindow(hit) && (hit == hwnd || Native.IsChild(hwnd, hit)),
            "The requested native pixel is covered by another window.");
        var client = new Native.Point(checked((int)Math.Round(point.X)), checked((int)Math.Round(point.Y)));
        Program.Require(Native.ScreenToClient(hwnd, ref client) && Native.GetClientRect(hwnd, out var bounds) &&
            client.X >= 0 && client.Y >= 0 && client.X < bounds.Right && client.Y < bounds.Bottom, "The requested pixel lies outside the owned client.");
        IntPtr dc = Native.GetDC(hwnd);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed for the owned window.");
        uint color;
        try { color = Native.GetPixel(dc, client.X, client.Y); }
        finally { Native.ReleaseDC(hwnd, dc); }
        Program.Require(color != uint.MaxValue, "Native GetPixel could not read the owned client surface.");
        uint rgb = (color & 0xff) << 16 | (color & 0xff00) | (color >> 16 & 0xff);
        PixelReadCount++;
        var key = (window, element, x, y);
        var observation = (client.X, client.Y, rgb);
        if (!_lastPixels.TryGetValue(key, out var previous) || previous != observation)
        {
            _lastPixels[key] = observation;
            Pixels.Enqueue(new { window = window.Title, element = element.Name, x, y, clientX = client.X, clientY = client.Y, rgb });
        }
        return rgb;
    }

    public void SetDpi(int dpi)
    {
        Program.Require(dpi > 0 && dpi <= short.MaxValue, "The synthetic DPI message requires a positive signed 16-bit DPI value.");
        Program.Require(Native.AreDpiAwarenessContextsEqual(Native.GetThreadDpiAwarenessContext(), new IntPtr(-4)),
            "The reference requires its own PerMonitorV2 manifest; no desktop DPI settings were changed.");
        _pendingDpi = null;
        var windows = Application.Current.Windows.Cast<Window>().Where(IsMapped).ToArray();
        if (windows.Length == 0)
        {
            _pendingDpi = dpi;
            DpiObservations.Enqueue(new { kind = "deferred-synthetic-owned-window-request", requestedDpi = dpi,
                applicationPoint = "next-first-mapped-owned-window", physicalMonitorTransition = false });
        }
        foreach (Window window in windows)
            ApplyDpi(window, dpi, "existing-mapped-owned-window");
    }

    private void ApplyDpi(Window window, int dpi, string applicationPoint)
    {
        IntPtr hwnd = OwnHandle(window);
        var old = VisualTreeHelper.GetDpi(window);
        Program.Require(Native.GetWindowRect(hwnd, out var bounds), "Cannot read the owned window's native rectangle.");
        uint monitorDpi = Native.GetDpiForWindow(hwnd);
        double ratio = dpi / old.PixelsPerInchX;
        var suggested = new Native.Rect { Left = bounds.Left, Top = bounds.Top,
            Right = checked(bounds.Left + (int)Math.Round((bounds.Right - bounds.Left) * ratio)),
            Bottom = checked(bounds.Top + (int)Math.Round((bounds.Bottom - bounds.Top) * ratio)) };
        Native.SendMessage(hwnd, 0x02e0, new IntPtr((long)((uint)dpi | (uint)dpi << 16)), ref suggested);
        wait(() => VisualTreeHelper.GetDpi(window).PixelsPerInchX == dpi && VisualTreeHelper.GetDpi(window).PixelsPerInchY == dpi,
            () => "Stock WPF did not accept the owned-window synthetic WM_DPICHANGED message.");
        Program.Require(Native.GetClientRect(hwnd, out var client), "Cannot read the owned client's post-message dimensions.");
        DpiObservations.Enqueue(new { window = window.Title, kind = "synthetic-owned-window-WM_DPICHANGED",
            applicationPoint,
            oldDpiX = old.PixelsPerInchX, oldDpiY = old.PixelsPerInchY, requestedDpi = dpi,
            actualWpfDpiX = VisualTreeHelper.GetDpi(window).PixelsPerInchX,
            monitorDpiBefore = monitorDpi, monitorDpiAfter = Native.GetDpiForWindow(hwnd),
            clientPixelWidth = client.Right - client.Left, clientPixelHeight = client.Bottom - client.Top,
            logicalOuterWidth = window.ActualWidth, logicalOuterHeight = window.ActualHeight,
            physicalMonitorTransition = false });
    }

    private static Point ScreenPoint(Window window, FrameworkElement element, double x, double y)
    {
        Program.Require(ReferenceEquals(Window.GetWindow(element), window), "The element belongs to another window.");
        return element.PointToScreen(new Point(x, y));
    }

    private static IntPtr OwnHandle(Window window)
    {
        window.VerifyAccess();
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        Program.Require(IsOwnWindow(hwnd), "The target is not a live native window owned by this process.");
        return hwnd;
    }

    private static bool IsOwnWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && Native.IsWindow(hwnd) &&
        Native.GetWindowThreadProcessId(hwnd, out uint process) != 0 && process == Environment.ProcessId;

    private static void VerifyForeground(IntPtr target, bool allowOwnedModal)
    {
        IntPtr foreground = Native.GetForegroundWindow();
        Program.Require(IsOwnWindow(target) && IsOwnWindow(foreground), "The foreground window is not owned by the reference; no input was injected.");
        Program.Require(foreground == target || (allowOwnedModal && Native.GetWindow(foreground, 4) == target),
            "The foreground window is neither the target nor its owned modal; no input was injected.");
    }

    private static void Send(IntPtr target, bool allowOwnedModal, Native.Input[] inputs)
    {
        VerifyForeground(target, allowOwnedModal);
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            VerifyForeground(target, allowOwnedModal);
            var release = inputs.Take((int)sent).Where(input => input.Type == 1 && (input.Data.Key.Flags & 2) == 0).Select(input =>
            { input.Data.Key.Flags |= 2; return input; }).ToList();
            if (inputs.Take((int)sent).Any(input => input.Type == 0 && (input.Data.Mouse.Flags & 2) != 0)) release.Add(Mouse(0, 0, 4));
            if (inputs.Take((int)sent).Any(input => input.Type == 0 && (input.Data.Mouse.Flags & 8) != 0)) release.Add(Mouse(0, 0, 16));
            if (release.Count > 0 && Native.SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf<Native.Input>()) != release.Count)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Partial input delivery also failed to release its owned keys/buttons.");
            throw new Win32Exception(error, $"SendInput accepted {sent}/{inputs.Length} owned-window events.");
        }
    }

    private static Native.Input Mouse(int x, int y, uint flags) => new() { Type = 0,
        Data = new Native.InputUnion { Mouse = new Native.MouseInput { X = x, Y = y, Flags = flags } } };
    private static Native.Input Key(int virtualKey, bool up)
    {
        uint scan = Native.MapVirtualKey((uint)virtualKey, 0);
        return new Native.Input { Type = 1, Data = new Native.InputUnion { Key = new Native.KeyboardInput {
            VirtualKey = (ushort)virtualKey, Scan = (ushort)scan, Flags = (scan > 0 ? 8u : 0u) | (up ? 2u : 0u) } } };
    }
    private static Native.Input Unicode(char character, bool up) => new() { Type = 1,
        Data = new Native.InputUnion { Key = new Native.KeyboardInput { Scan = character, Flags = 4u | (up ? 2u : 0u) } } };

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point(int x, int y) { public int X = x, Y = y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Key; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] internal static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint map);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern short VkKeyScanEx(char character, IntPtr layout);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] internal static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, ref Rect rect);
    }
}
