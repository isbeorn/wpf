extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using System.Reflection;

internal static class NativeInputChecks
{
    internal static void FiveButtons(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        foreach (var (button, nativeButton) in new[]
        {
            (MouseButton.Left, 1u), (MouseButton.Right, 3u), (MouseButton.Middle, 2u),
            (MouseButton.XButton1, 8u), (MouseButton.XButton2, 9u)
        })
        {
            var window = CreateWindow("Genuine WPF five-button " + button);
            int down = 0, up = 0;
            window.PreviewMouseDown += (_, args) => { if (args.ChangedButton == button) down++; };
            window.PreviewMouseUp += (_, args) => { if (args.ChangedButton == button) up++; };
            try
            {
                native.MovePointer(1020, 760);
                window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid)); drain();
                native.Button(nativeButton, true);
                Require(window.CaptureMouse(), button + " activation snapshot did not acquire a native grab.");
                Require(State(button) == MouseButtonState.Pressed && down == 0,
                    button + " native held activation snapshot was lost or manufactured a button-down event.");
                native.Button(nativeButton, false);
                wait(() => State(button) == MouseButtonState.Released && up == 1);
                window.ReleaseMouseCapture(); drain();
                native.MovePointer(xid, 20, 20);
                native.Button(nativeButton, true);
                wait(() => State(button) == MouseButtonState.Pressed && down == 1);
                native.Button(nativeButton, false);
                wait(() => State(button) == MouseButtonState.Released && up == 2);
            }
            finally { native.Button(nativeButton, false); window.Close(); }
        }
    }

    internal static void UnexpectedCaptureLossAndDisposal(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var first = CreateWindow("Genuine WPF native unmap capture loss");
        var second = CreateWindow("Genuine WPF held-button capture recovery");
        int lost = 0, recoveryDown = 0;
        first.LostMouseCapture += (_, _) => lost++;
        second.PreviewMouseDown += (_, _) => recoveryDown++;
        try
        {
            native.MovePointer(1020, 760);
            first.Show(); var firstXid = native.FindWindow(first.Title);
            wait(() => native.IsMapped(firstXid)); drain();
            native.Button(1, true);
            Require(first.CaptureMouse() && Mouse.LeftButton == MouseButtonState.Pressed,
                "The native capture-loss fixture did not snapshot the held physical button.");
            Require(!native.CanAcquirePointerGrab(), "The WPF capture did not own an actual X11 server grab.");
            native.Unmap(firstXid);
            wait(() => Mouse.Captured is null && lost == 1);
            second.Show(); var secondXid = native.FindWindow(second.Title);
            wait(() => native.IsMapped(secondXid));
            Require(second.CaptureMouse() && Mouse.LeftButton == MouseButtonState.Pressed && recoveryDown == 0,
                "Unexpected native grab loss released the physical button or recovery manufactured a press.");
            native.Button(1, false);
            wait(() => Mouse.LeftButton == MouseButtonState.Released);
            Require(!native.CanAcquirePointerGrab(), "The recovered source did not retain its native grab.");
            native.MovePointer(secondXid, 20, 20); drain();
            var source = PresentationSource.FromVisual(second)!;
            object provider = source.GetType().GetField("_inputProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            Require((bool)provider.GetType().GetMethod("SetCursorVisibility", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(provider, new object[] { false })!, "The actual source provider rejected hiding the native cursor.");
            wait(() => !native.CursorImage().Visible);
            second.Close();
            Require(Mouse.Captured is null && PresentationSource.FromVisual(second) is null,
                "Window/source disposal retained WPF capture or its source.");
            wait(() => native.CanAcquirePointerGrab() && native.CursorImage().Visible);
            first.Background = Brushes.Blue;
            drain();
        }
        finally { native.Button(1, false); second.Close(); first.Close(); }
    }

    private static Window CreateWindow(string title) => new()
    { Title = title, Width = 180, Height = 100, ShowActivated = false, Background = Brushes.Red };

    private static MouseButtonState State(MouseButton button) => button switch
    {
        MouseButton.Left => Mouse.LeftButton, MouseButton.Right => Mouse.RightButton,
        MouseButton.Middle => Mouse.MiddleButton, MouseButton.XButton1 => Mouse.XButton1,
        MouseButton.XButton2 => Mouse.XButton2, _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
