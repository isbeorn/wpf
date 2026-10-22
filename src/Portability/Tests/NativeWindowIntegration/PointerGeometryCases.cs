extern alias WpfFork;

using System.Reflection;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

internal static class PointerGeometryCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, int requestedDpi, Action<object> observe)
    {
        foreach (bool captured in new[] { false, true })
            run("native-pointer-source-move-" + (captured ? "captured" : "uncaptured") + "-both-directions",
                () => StationaryPointer(native, wait, drain, captured, requestedDpi, observe));
        run("native-pointer-inactive-source-move-noninterference", () => InactiveSource(native, wait, drain));
        foreach (string transition in new[] { "close", "hide", "disable" })
            run("native-pointer-source-move-reentry-" + transition, () => Reentry(native, wait, drain, transition));
    }

    private static void StationaryPointer(X11Fixture native, Action<Func<bool>> wait, Action drain, bool captured, int requestedDpi, Action<object> observe)
    {
        var window = Create("Stationary native pointer " + captured);
        int down = 0, up = 0;
        window.PreviewMouseDown += (_, _) => down++;
        window.PreviewMouseUp += (_, _) => up++;
        try
        {
            var xid = ShowAndPoint(window, native, wait, drain);
            var dpi = VisualTreeHelper.GetDpi(window);
            observe(new { kind = "native-pointer-geometry-dpi", captured, requestedDpi,
                observedDpiX = 96 * dpi.DpiScaleX, observedDpiY = 96 * dpi.DpiScaleY });
            Require(Math.Abs(dpi.DpiScaleX - requestedDpi / 96d) < 0.0001 &&
                Math.Abs(dpi.DpiScaleY - requestedDpi / 96d) < 0.0001,
                $"The genuine pointer window DPI does not match requested {requestedDpi}: {96 * dpi.DpiScaleX},{96 * dpi.DpiScaleY}.");
            var source = PresentationSource.FromVisual(window)!;
            IntPtr focus = native.FocusedWindow;
            if (captured)
            {
                native.Button(1, true); wait(() => Mouse.LeftButton == MouseButtonState.Pressed && down == 1);
                Require(window.CaptureMouse() && Mouse.Captured == window, "The stationary-pointer source could not acquire genuine capture.");
            }
            var physical = native.PointerPosition;
            foreach (var delta in new[] { new Point(20, 15), new Point(-40, -30), new Point(20, 15) })
            {
                MoveAndDrain(window, xid, delta, native, wait, drain);
                Require(native.PointerPosition == physical, "A source move moved the physical native pointer.");
                AssertPosition(window, xid, physical, native);
                Require(ReferenceEquals(source, PresentationSource.FromVisual(window)) && native.FocusedWindow == focus,
                    "A source move changed native focus or presentation identity.");
                Require(Mouse.Captured == (captured ? window : null) &&
                    Mouse.LeftButton == (captured ? MouseButtonState.Pressed : MouseButtonState.Released),
                    "A geometry-only input reconciliation changed capture or button state.");
                Require(down == (captured ? 1 : 0) && up == 0, "A geometry-only reconciliation manufactured a button transition.");
            }
            if (captured)
            {
                native.Button(1, false); wait(() => up == 1 && Mouse.LeftButton == MouseButtonState.Released);
                Mouse.Capture(null); wait(() => Mouse.Captured is null && native.CanAcquirePointerGrab());
            }
        }
        finally { native.Button(1, false); Mouse.Capture(null); window.Close(); drain(); }
    }

    private static void InactiveSource(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var active = Create("Native pointer geometry active source");
        var inactive = Create("Native pointer geometry unrelated source");
        inactive.Left = 700; inactive.Top = 400; inactive.Width = 160; inactive.Height = 100; inactive.ShowActivated = false;
        EventInfo? rawInput = null; object? peer = null; Delegate? observer = null; int unrelatedMoves = 0;
        try
        {
            var activeXid = ShowAndPoint(active, native, wait, drain);
            inactive.Show(); var inactiveXid = native.FindWindow(inactive.Title);
            wait(() => native.IsMapped(inactiveXid)); drain();
            native.MovePointer(activeXid, 120, 70);
            wait(() => active.IsMouseOver); drain();
            Require(active.CaptureMouse(), "The active source could not capture for inactive-window isolation.");
            var inactiveSource = PresentationSource.FromVisual(inactive)!;
            peer = inactiveSource.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inactiveSource)!;
            rawInput = peer.GetType().GetEvent("RawInput")!;
            Action<object> observe = report =>
            {
                var type = report.GetType();
                if (type.Name == "RawMouseInputReport" && type.GetProperty("Actions")!.GetValue(report)!.ToString()!.Contains("AbsoluteMove", StringComparison.Ordinal))
                    unrelatedMoves++;
            };
            observer = Delegate.CreateDelegate(rawInput.EventHandlerType!, observe.Target, observe.Method);
            rawInput.AddEventHandler(peer, observer);
            var source = PresentationSource.FromVisual(active);
            var physical = native.PointerPosition;
            var focus = native.FocusedWindow;
            foreach (var delta in new[] { new Point(15, 10), new Point(-30, -20), new Point(15, 10) })
            {
                MoveAndDrain(inactive, inactiveXid, delta, native, wait, drain);
                Require(native.PointerPosition == physical && Mouse.Captured == active && native.FocusedWindow == focus &&
                    ReferenceEquals(source, PresentationSource.FromVisual(active)), "Moving an unrelated source changed pointer ownership, focus or identity.");
                AssertPosition(active, activeXid, physical, native);
                Require(unrelatedMoves == 0, "An inactive source geometry change fabricated a mouse move report.");
            }
        }
        finally
        {
            if (rawInput is not null) rawInput.RemoveEventHandler(peer, observer);
            Mouse.Capture(null); inactive.Close(); active.Close(); drain();
        }
    }

    private static void Reentry(X11Fixture native, Action<Func<bool>> wait, Action drain, string transition)
    {
        var window = Create("Native pointer geometry reentry " + transition);
        int callbacks = 0, closed = 0, movesAfterClose = 0;
        bool armed = false;
        window.Closed += (_, _) => closed++;
        window.LocationChanged += (_, _) => { if (closed != 0) movesAfterClose++; };
        window.PreviewMouseMove += (_, _) =>
        {
            if (!armed) return;
            armed = false;
            callbacks++;
            if (transition == "close") window.Close();
            else if (transition == "hide") window.Hide();
            else window.IsEnabled = false;
        };
        try
        {
            var xid = ShowAndPoint(window, native, wait, drain);
            var source = PresentationSource.FromVisual(window)!;
            native.Button(1, true); wait(() => Mouse.LeftButton == MouseButtonState.Pressed);
            Require(window.CaptureMouse(), "The reentry source could not acquire genuine capture.");
            var physical = native.PointerPosition;
            armed = true;
            window.Left += 20; window.Top += 15;
            wait(() => callbacks == 1); drain();
            Require(native.PointerPosition == physical && Mouse.Captured is null && movesAfterClose == 0,
                "Geometry callback reentry retained capture, moved the pointer or notified position after close.");
            if (transition == "close")
                Require(closed == 1 && source.IsDisposed && PresentationSource.FromVisual(window) is null && !native.IsMapped(xid),
                    "Close during pointer reconciliation retained its native/source lifetime.");
            else
            {
                Require(ReferenceEquals(source, PresentationSource.FromVisual(window)) && closed == 0,
                    "Hide/disable during pointer reconciliation replaced the source lifetime.");
                if (transition == "hide") Require(!window.IsVisible && !native.IsMapped(xid), "Reentry did not hide the native source.");
                else Require(!window.IsEnabled && native.IsMapped(xid), "Reentry did not disable the live native source.");
            }
            native.Button(1, false); wait(native.CanAcquirePointerGrab);
        }
        finally { armed = false; native.Button(1, false); Mouse.Capture(null); window.Close(); drain(); }
    }

    private static Window Create(string title) => new()
    {
        Title = title, WindowStyle = WindowStyle.None, Width = 260, Height = 160, Left = 100, Top = 80,
        ShowActivated = true, Content = new Border { Background = Brushes.DarkBlue }
    };

    private static IntPtr ShowAndPoint(Window window, X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        window.Show(); var xid = native.FindWindow(window.Title);
        wait(() => native.IsMapped(xid) && window.IsActive && native.FocusedWindow == xid); drain();
        native.MovePointer(xid, 120, 70);
        wait(() => window.IsMouseOver); drain();
        AssertPosition(window, xid, native.PointerPosition, native);
        return xid;
    }

    private static void MoveAndDrain(Window window, IntPtr xid, Point delta, X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var previous = native.ClientOrigin(xid);
        var scale = VisualTreeHelper.GetDpi(window);
        var frame = native.FrameExtents(xid) ?? (Left: 0, Right: 0, Top: 0, Bottom: 0);
        var requested = new Point(window.Left + delta.X, window.Top + delta.Y);
        window.Left = requested.X; window.Top = requested.Y;
        var expected = (X: (int)Math.Round(requested.X * scale.DpiScaleX, MidpointRounding.AwayFromZero) + frame.Left,
            Y: (int)Math.Round(requested.Y * scale.DpiScaleY, MidpointRounding.AwayFromZero) + frame.Top);
        try { wait(() => native.ClientOrigin(xid) == expected); }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Native source move mismatch: previous={previous}, requested={requested}, dpi={scale.DpiScaleX},{scale.DpiScaleY}, frame={frame}, expected={expected}, actual={native.ClientOrigin(xid)}.", exception);
        }
        drain();
        wait(() => Same(window.PointToScreen(new Point()), new Point(expected.X, expected.Y))); drain();
    }

    private static void AssertPosition(Window window, IntPtr xid, (int X, int Y) physical, X11Fixture native)
    {
        var origin = native.ClientOrigin(xid);
        var scale = VisualTreeHelper.GetDpi(window);
        var local = Mouse.GetPosition(window);
        var expected = new Point((physical.X - origin.X) / scale.DpiScaleX, (physical.Y - origin.Y) / scale.DpiScaleY);
        Require(Same(local, expected) && Same(window.PointToScreen(local), new Point(physical.X, physical.Y)),
            $"Stationary pointer drifted after source move: native={physical}, origin={origin}, local={local}, expected={expected}, screen={window.PointToScreen(local)}.");
    }

    private static bool Same(Point left, Point right) => Math.Abs(left.X - right.X) < .01 && Math.Abs(left.Y - right.Y) < .01;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
