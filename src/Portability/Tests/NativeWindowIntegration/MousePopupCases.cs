extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Controls.Primitives;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

internal static class MousePopupCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, string fontPath, int dpi)
    {
        run("natural-default-ToolTip-hover-close-reopen", () => NaturalToolTip(native, wait, drain, fontPath, dpi));
        foreach (var placement in new[] { PlacementMode.Mouse, PlacementMode.MousePoint })
            run("native-" + placement + "-placement-motion-cursor-and-edges", () => Placement(native, wait, drain, placement, dpi));
    }

    private static void NaturalToolTip(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath, int dpi)
    {
        var target = new Border { Background = Brushes.Green };
        var window = new Window { Title = "Natural default mouse ToolTip", Left = 100, Top = 100, Width = 260, Height = 160, Content = target };
        var tooltip = new ToolTip { Content = new Border { Width = 60, Height = 24, Background = Brushes.Red },
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
        ToolTipService.SetToolTip(target, tooltip);
        ToolTipService.SetInitialShowDelay(target, 50);
        ToolTipService.SetBetweenShowDelay(target, 0);
        ToolTipService.SetShowDuration(target, 10000);
        int opened = 0, closed = 0;
        tooltip.Opened += (_, _) => opened++;
        tooltip.Closed += (_, _) => closed++;
        PresentationSource? previous = null;
        try
        {
            native.MovePointer(2, 2);
            window.Show(); drain();
            IntPtr owner = native.FindWindow(window.Title);
            Require(VisualTreeHelper.GetDpi(window).DpiScaleX == dpi / 96d, "Natural tooltip DPI differs from the requested native setting.");
            Require(tooltip.Placement == PlacementMode.Mouse, "The genuine tooltip default placement changed.");
            native.Focus(owner);
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                native.MovePointer(owner, 70, 55);
                wait(() => opened == cycle && PresentationSource.FromVisual(tooltip) is { IsDisposed: false });
                drain();
                var source = PresentationSource.FromVisual(tooltip)!;
                var xid = OwnedNativeAccess.Handle(source);
                Require(tooltip.IsOpen && tooltip.Template is not null && tooltip.IsMeasureValid && tooltip.IsArrangeValid &&
                    source != previous && native.IsMapped(xid) && Mouse.Captured is null && native.CanAcquirePointerGrab(),
                    "Natural hover did not map a fresh genuine ToolTip without capturing the pointer.");
                var cursor = native.CursorPlacementBounds();
                var origin = native.ClientOrigin(xid);
                Require(origin.X == cursor.X && origin.Y == cursor.Bottom + 2,
                    "The natural default ToolTip did not clear the actual native cursor hotspot and occupied bottom.");
                native.MovePointer(2, 2);
                wait(() => closed == cycle && source.IsDisposed);
                drain();
                Require(!tooltip.IsOpen && !native.RootChildren.Contains(xid) && Mouse.Captured is null && native.CanAcquirePointerGrab(),
                    "Native mouse departure did not close the ToolTip and release its source/window state.");
                PopupCases.Observations.Add(new { kind = "natural-mouse-tooltip", cycle, dpi, opened, closed,
                    popupX = origin.X, popupY = origin.Y, pointerX = cursor.X, pointerY = cursor.Y,
                    cursorRight = cursor.Right, cursorBottom = cursor.Bottom, source.IsDisposed });
                previous = source;
            }
        }
        finally { ToolTipService.SetToolTip(target, null); tooltip.IsOpen = false; window.Close(); drain(); }
    }

    private static void Placement(X11Fixture native, Action<Func<bool>> wait, Action drain, PlacementMode placement, int dpi)
    {
        var window = new Window { Title = "Native mouse Popup " + placement, Left = 100, Top = 100, Width = 300, Height = 200,
            Background = Brushes.Green };
        var popup = new Popup { PlacementTarget = window, Placement = placement, AllowsTransparency = true,
            StaysOpen = true, Child = new Border { Width = 50, Height = 30, Background = Brushes.Red } };
        try
        {
            window.Show(); drain();
            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            Require(scale == dpi / 96d, "Mouse Popup DPI differs from the requested native setting.");
            IntPtr owner = native.FindWindow(window.Title);
            native.Focus(owner);
            var cursorFingerprints = new HashSet<ulong>();
            foreach (var cursorShape in new[] { Cursors.Arrow, Cursors.IBeam, Cursors.Cross })
            {
                window.Cursor = cursorShape;
                native.MovePointer(owner, 80, 60); drain();
                var cursorImage = native.CursorImage();
                Require(cursorImage.Visible && cursorFingerprints.Add(cursorImage.Fingerprint),
                    "The requested cursor shape did not produce a distinct visible native cursor image.");
                PopupCases.Observations.Add(new { kind = "native-mouse-popup-cursor-precondition", placement = placement.ToString(),
                    shape = cursorShape.ToString(), cursorImage.Fingerprint });
                foreach (var (x, y) in new[] { (80, 60), (110, 80), (50, 40), (80, 60) })
                {
                    native.MovePointer(owner, x, y); drain();
                    OpenAndClose("motion-" + cursorShape, true);
                }
            }
            var screen = native.ScreenSize;
            foreach (var (x, y) in new[] { (2, 2), (screen.Width - 2, 2), (screen.Width - 2, screen.Height - 2), (2, screen.Height - 2) })
            {
                native.MovePointer(x, y); drain();
                OpenAndClose("screen-edge", false);
            }

            void OpenAndClose(string scenario, bool exact)
            {
                var cursor = native.CursorPlacementBounds();
                popup.IsOpen = true;
                wait(() => PresentationSource.FromVisual(popup.Child) is { IsDisposed: false }); drain();
                var source = PresentationSource.FromVisual(popup.Child)!;
                IntPtr xid = OwnedNativeAccess.Handle(source);
                var origin = native.ClientOrigin(xid);
                var size = native.Size(xid);
                var screen = native.ScreenSize;
                Require(native.IsMapped(xid) && Mouse.Captured is null && native.CanAcquirePointerGrab() && native.FocusedWindow == owner,
                    "Mouse placement did not preserve mapped native source, pointer freedom and owner focus.");
                Require(origin.X >= 0 && origin.Y >= 0 && origin.X + size.Width <= screen.Width && origin.Y + size.Height <= screen.Height,
                    "Mouse placement did not fit the actual native screen bounds.");
                if (exact)
                    Require(origin.X == cursor.X && origin.Y == (placement == PlacementMode.MousePoint ? cursor.Y : cursor.Bottom + 2),
                        "Mouse placement lost the actual physical pointer/cursor anchor or applied DPI scaling twice.");
                Require(Math.Abs(size.Width - 50 * scale) <= 1 && Math.Abs(size.Height - 30 * scale) <= 1,
                    "Mouse placement changed the Popup content's single DPI conversion.");
                popup.IsOpen = false;
                wait(() => source.IsDisposed); drain();
                Require(!native.RootChildren.Contains(xid) && Mouse.Captured is null && native.CanAcquirePointerGrab(),
                    "Closing a mouse Popup retained a native window, source or pointer capture.");
                PopupCases.Observations.Add(new { kind = "native-mouse-popup-placement", placement = placement.ToString(), dpi, scenario,
                    popupX = origin.X, popupY = origin.Y, size.Width, size.Height, pointerX = cursor.X, pointerY = cursor.Y,
                    cursorRight = cursor.Right, cursorBottom = cursor.Bottom, source.IsDisposed });
            }
        }
        finally { popup.IsOpen = false; window.Close(); drain(); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
