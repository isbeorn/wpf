extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Data;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Shell;

internal static class ChromeCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, int requestedDpi = 96, Action<object>? trace = null)
    {
        Require(native.HasWindowManager, "Custom chrome qualification requires the real private window manager.");
        var viewport = native.RootSize; double requestedScale = requestedDpi / 96.0;
        trace?.Invoke(new { stage = "chrome-observed-viewport", requestedDpi, width = viewport.Width, height = viewport.Height });
        Require(viewport.Width >= 1024 * requestedScale && viewport.Height >= 768 * requestedScale,
            "Chrome gesture qualification requires at least 1024x768 logical DIPs of actual native root geometry.");
        run("public-WindowChrome-declared-defaults-and-validation", () =>
        {
            var chrome = new WindowChrome();
            Require(chrome.CaptionHeight == 23 && chrome.ResizeBorderThickness == new Thickness(8) &&
                chrome.GlassFrameThickness == new Thickness(0) && chrome.CornerRadius == new CornerRadius(0), "Declared constructor defaults differ.");
            Require(SystemParameters.WindowNonClientFrameThickness == new Thickness(8, 31, 8, 8), "Normal nonclient geometry was replaced by the zero glass policy.");
            Reject(() => chrome.GlassFrameThickness = new Thickness(1));
            Reject(() => chrome.NonClientFrameEdges = NonClientFrameEdges.Left);
            Require(chrome.GlassFrameThickness == new Thickness(0) && chrome.NonClientFrameEdges == NonClientFrameEdges.None, "Unsupported frame input committed before rejection.");
            chrome.CaptionHeight = 30; chrome.CornerRadius = new CornerRadius(1);
            Require(chrome.CaptionHeight == 30 && chrome.CornerRadius == new CornerRadius(1), "Caller geometry was lost.");
        });
        run("public-WindowChrome-attach-replace-remove-and-reshow", () =>
        {
            var window = NewWindow("Genuine WPF chrome lifecycle");
            int initialized = 0; window.SourceInitialized += (_, _) => initialized++;
            try
            {
                WindowChrome.SetWindowChrome(window, NewChrome()); window.Show();
                var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid) && native.Decorations(xid) == 0);
                var source = PresentationSource.FromVisual(window); drain(); VerifyDpi(window, xid);
                var origin = native.ClientOrigin(xid);
                Drag(window, new Point(100, 43), 18, 12); drain();
                Require(native.ClientOrigin(xid) == origin, "The original 30-DIP caption treated client content at y43 as caption.");
                var replacement = new WindowChrome { CaptionHeight = 40 };
                WindowChrome.SetWindowChrome(window, replacement); drain();
                Drag(window, new Point(130, 43), 18, 12);
                wait(() => native.ClientOrigin(xid) == (origin.X + 18, origin.Y + 12));
                replacement.CaptionHeight = 30; drain(); origin = native.ClientOrigin(xid);
                Drag(window, new Point(160, 43), 18, 12); drain();
                Require(native.ClientOrigin(xid) == origin, "Restoring the 30-DIP caption retained the replaced hit band.");
                Require(native.FindWindow(window.Title) == xid && ReferenceEquals(source, PresentationSource.FromVisual(window)), "Replacement changed source lifetime.");
                WindowChrome.SetWindowChrome(window, null!); wait(() => native.Decorations(xid) != 0);
                WindowChrome.SetWindowChrome(window, NewChrome()); wait(() => native.Decorations(xid) == 0);
                window.Hide(); drain(); window.Show(); wait(() => native.IsMapped(xid));
                Require(initialized == 1 && native.Decorations(xid) == 0, "Hide/show replaced source or lost chrome.");
                window.WindowStyle = WindowStyle.None; WindowChrome.SetWindowChrome(window, null!); drain();
                Require(native.Decorations(xid) == 0, "Removal restored an obsolete requested style.");
                window.WindowStyle = WindowStyle.SingleBorderWindow; wait(() => native.Decorations(xid) != 0);
            }
            finally { window.Close(); drain(); }
        });
        foreach (bool custom in new[] { false, true }) run("public-ResizeMode-paired-functions-programmatic-bounds-state-" + custom, () =>
        {
            var window = NewWindow("Genuine WPF resize modes " + custom);
            window.MinWidth = 180; window.MinHeight = 120; window.MaxWidth = 500; window.MaxHeight = 350;
            try
            {
                window.ResizeMode = ResizeMode.NoResize;
                if (custom) WindowChrome.SetWindowChrome(window, NewChrome());
                window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid)); VerifyDpi(window, xid);
                foreach (var mode in new[] { ResizeMode.NoResize, ResizeMode.CanMinimize, ResizeMode.CanResize, ResizeMode.CanMinimize, ResizeMode.NoResize, ResizeMode.CanResize })
                {
                    window.ResizeMode = mode;
                    long controls = custom ? 0 : mode == ResizeMode.CanResize ? 126 : mode == ResizeMode.CanMinimize ? 58 : 26;
                    wait(() => native.MotifFunctions(xid) == 62 && native.Decorations(xid) == controls);
                    Require(window.ResizeMode == mode, "Native controls changed the requested ResizeMode.");
                    window.Width = window.Width == 320 ? 340 : 320; window.Height = window.Height == 220 ? 240 : 220;
                    wait(() =>
                    {
                        var size = native.Size(xid); var frame = native.FrameExtents(xid) ?? default; var dpi = VisualTreeHelper.GetDpi(window);
                        return Near(window.ActualWidth, window.Width) && Near(window.ActualHeight, window.Height) &&
                            size.Width + frame.Left + frame.Right == (int)Math.Round(window.Width * dpi.DpiScaleX) &&
                            size.Height + frame.Top + frame.Bottom == (int)Math.Round(window.Height * dpi.DpiScaleY);
                    });
                    Require(window.MinWidth == 180 && window.MinHeight == 120 && window.MaxWidth == 500 && window.MaxHeight == 350, "ResizeMode changed public constraints.");
                }
                foreach (var mode in new[] { ResizeMode.NoResize, ResizeMode.CanMinimize, ResizeMode.CanResize })
                {
                    window.ResizeMode = mode; var restoreOrigin = native.ClientOrigin(xid);
                    string pendingTransition = "Minimized";
                    try
                    {
                        window.WindowState = WindowState.Minimized; wait(() => window.WindowState == WindowState.Minimized && native.State(xid) == "Minimized" && !native.IsMapped(xid));
                        pendingTransition = "Normal-after-Minimized";
                        window.WindowState = WindowState.Normal; wait(() => native.IsMapped(xid) && window.WindowState == WindowState.Normal && native.State(xid) == "Normal" && native.ClientOrigin(xid) == restoreOrigin);
                        pendingTransition = "Maximized";
                        window.WindowState = WindowState.Maximized; wait(() => window.WindowState == WindowState.Maximized && native.State(xid) == "Maximized");
                        pendingTransition = "Normal-after-Maximized";
                        window.WindowState = WindowState.Normal; wait(() => window.WindowState == WindowState.Normal && native.State(xid) == "Normal" && native.ClientOrigin(xid) == restoreOrigin);
                    }
                    catch (Exception failure)
                    {
                        try
                        {
                            object? WindowField(string name) => typeof(Window).GetField(name, OwnedNativeAccess.Members)!.GetValue(window);
                            var peer = OwnedNativeAccess.Peer(PresentationSource.FromVisual(window)!);
                            var peerDesktop = (Point)OwnedNativeAccess.Get(peer, "_desktopPosition");
                            var peerClient = (Point)OwnedNativeAccess.Get(peer, "_clientScreenOrigin");
                            var requestedDesktop = (Point)WindowField("_portableRequestedDesktopPosition")!;
                            var cached = new
                            {
                                peerDesktopX = peerDesktop.X, peerDesktopY = peerDesktop.Y,
                                peerClientX = peerClient.X, peerClientY = peerClient.Y,
                                peerMetricsState = OwnedNativeAccess.Get(peer, "_metricsState").ToString(),
                                peerRequestedState = OwnedNativeAccess.Get(peer, "_requestedState").ToString(),
                                peerMetricsRefreshQueued = (bool)OwnedNativeAccess.Get(peer, "_metricsRefreshQueued"),
                                peerStateChangedPending = (bool)OwnedNativeAccess.Get(peer, "_stateChangedPending"),
                                peerStateRequestPending = (bool)OwnedNativeAccess.Get(peer, "_stateRequestPending"),
                                windowPendingBounds = WindowField("_portablePendingBounds")!.ToString(),
                                windowBoundsAwaitingAck = WindowField("_portableBoundsAwaitingAck")!.ToString(),
                                requestedDesktopX = requestedDesktop.X, requestedDesktopY = requestedDesktop.Y,
                                actualLeft = (double)WindowField("_actualLeft")!, actualTop = (double)WindowField("_actualTop")!,
                                windowLastMetricsState = WindowField("_portableLastMetricsState")!.ToString(),
                                windowUpdatingMetrics = (bool)WindowField("_portableUpdatingMetrics")!,
                                windowApplyingBounds = (bool)WindowField("_portableApplyingBounds")!,
                                windowStateRequest = WindowField("_portableStateRequest")?.ToString()
                            };
                            var client = native.ClientOrigin(xid); var frame = native.FrameExtents(xid) ?? default;
                            var size = native.Size(xid); var dpi = VisualTreeHelper.GetDpi(window);
                            trace?.Invoke(new { stage = "resize-mode-failure-only", custom, mode = mode.ToString(), pendingTransition, cached,
                                expectedClientX = restoreOrigin.X, expectedClientY = restoreOrigin.Y,
                                clientX = client.X, clientY = client.Y, clientWidth = size.Width, clientHeight = size.Height,
                                frameLeft = frame.Left, frameTop = frame.Top, frameRight = frame.Right, frameBottom = frame.Bottom,
                                publicState = window.WindowState.ToString(), nativeState = native.State(xid), mapped = native.IsMapped(xid),
                                publicLeft = window.Left, publicTop = window.Top, publicWidth = window.Width, publicHeight = window.Height,
                                actualWidth = window.ActualWidth, actualHeight = window.ActualHeight,
                                restoreBounds = window.RestoreBounds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                scaleX = dpi.DpiScaleX, scaleY = dpi.DpiScaleY, error = failure.ToString() });
                        }
                        catch (Exception diagnosticError)
                        {
                            trace?.Invoke(new { stage = "resize-mode-failure-snapshot-error", custom, mode = mode.ToString(), pendingTransition,
                                error = diagnosticError.ToString() });
                        }
                        throw;
                    }
                }
                window.ResizeMode = ResizeMode.CanResize;
                Reject(() => window.ResizeMode = ResizeMode.CanResizeWithGrip);
                Require(window.ResizeMode == ResizeMode.CanResize, "Unsupported grip mode committed before rejection.");
            }
            finally { window.Close(); drain(); }
        });
        foreach (var direction in new[] { ResizeGripDirection.TopLeft, ResizeGripDirection.Top, ResizeGripDirection.TopRight,
            ResizeGripDirection.Right, ResizeGripDirection.BottomRight, ResizeGripDirection.Bottom, ResizeGripDirection.BottomLeft, ResizeGripDirection.Left })
            run("native-WindowChrome-eight-edge-grow-shrink-" + direction, () =>
            {
                var window = NewWindow("Genuine WPF chrome resize " + direction);
                try
                {
                    WindowChrome.SetWindowChrome(window, NewChrome()); window.Show(); var xid = native.FindWindow(window.Title);
                    wait(() => native.IsMapped(xid)); drain(); VerifyDpi(window, xid);
                    var before = native.Size(xid);
                    bool left = direction is ResizeGripDirection.Left or ResizeGripDirection.TopLeft or ResizeGripDirection.BottomLeft;
                    bool right = direction is ResizeGripDirection.Right or ResizeGripDirection.TopRight or ResizeGripDirection.BottomRight;
                    bool top = direction is ResizeGripDirection.Top or ResizeGripDirection.TopLeft or ResizeGripDirection.TopRight;
                    bool bottom = direction is ResizeGripDirection.Bottom or ResizeGripDirection.BottomLeft or ResizeGripDirection.BottomRight;
                    int dx = left ? -32 : right ? 32 : 0, dy = top ? -32 : bottom ? 32 : 0;
                    Point Grip() => new(left ? 6 : right ? window.ActualWidth - 6 : window.ActualWidth / 2,
                        top ? 6 : bottom ? window.ActualHeight - 6 : window.ActualHeight / 2);
                    Drag(window, Grip(), dx, dy);
                    wait(() => native.Size(xid).Width == before.Width + Math.Abs(dx) && native.Size(xid).Height == before.Height + Math.Abs(dy));
                    Drag(window, Grip(), -dx, -dy);
                    wait(() => native.Size(xid) == before);
                    Require(native.CanAcquirePointerGrab(), "Resize retained the native pointer grab.");
                }
                finally { native.Button(1, false); window.Close(); drain(); }
            });
        run("native-WindowChrome-nested-client-caption-doubleclick-and-explicit-grip", () =>
        {
            var window = NewWindow("Genuine WPF chrome client routing");
            int clicks = 0, commands = 0;
            var button = new Button { Width = 70, Height = 24, Template = ButtonTemplate(), Background = Brushes.Blue };
            button.Click += (_, _) => clicks++;
            var captionCommand = new RoutedCommand("CaptionMaximizeRestore", typeof(ChromeCases));
            window.CommandBindings.Add(new CommandBinding(captionCommand, (_, _) =>
            { commands++; window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            var client = new Border { Child = button, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            WindowChrome.SetIsHitTestVisibleInChrome(client, true);
            var grip = new Border { Width = 20, Height = 20, Background = Brushes.Green, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 25, 25) };
            WindowChrome.SetResizeGripDirection(grip, ResizeGripDirection.BottomRight);
            var content = new Grid(); content.Children.Add(client); content.Children.Add(grip); window.Content = content;
            button.SetBinding(Button.CommandProperty, new Binding(nameof(CaptionCommandSource.Command)) { Source = new CaptionCommandSource(captionCommand) });
            try
            {
                WindowChrome.SetWindowChrome(window, NewChrome()); window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && button.ActualWidth > 0); drain(); VerifyDpi(window, xid);
                Require(button.IsEnabled && ReferenceEquals(button.Command, captionCommand) &&
                    button.GetBindingExpression(Button.CommandProperty)?.Status == BindingStatus.Active && captionCommand.CanExecute(null, button),
                    "The parented caption Button did not establish an enabled genuine command binding before input.");
                var origin = native.ClientOrigin(xid); Click(window, button.TranslatePoint(new Point(35, 12), window));
                wait(() => clicks == 1 && commands == 1 && window.WindowState == WindowState.Maximized && native.State(xid) == "Maximized");
                Click(window, button.TranslatePoint(new Point(35, 12), window));
                wait(() => clicks == 2 && commands == 2 && window.WindowState == WindowState.Normal && native.State(xid) == "Normal" && native.ClientOrigin(xid) == origin);
                Drag(window, new Point(100, 22), 38, 24); wait(() => native.ClientOrigin(xid) == (origin.X + 38, origin.Y + 24));
                var size = native.Size(xid); Drag(window, grip.TranslatePoint(new Point(10, 10), window), 30, 20);
                wait(() => native.Size(xid) == (size.Width + 30, size.Height + 20));
                drain(); Click(window, new Point(100, 22)); Click(window, new Point(100, 22));
                wait(() => window.WindowState == WindowState.Maximized && native.State(xid) == "Maximized");
                drain(); Click(window, new Point(130, 22)); Click(window, new Point(130, 22));
                wait(() => window.WindowState == WindowState.Normal && native.State(xid) == "Normal");
                foreach (var mode in new[] { ResizeMode.NoResize, ResizeMode.CanMinimize })
                {
                    window.ResizeMode = mode; drain(); size = native.Size(xid);
                    Drag(window, new Point(window.ActualWidth - 6, window.ActualHeight - 6), 25, 25); drain();
                    Require(native.Size(xid) == size, mode + " accepted a custom-edge gesture.");
                    int captionX = mode == ResizeMode.NoResize ? 160 : 190;
                    Click(window, new Point(captionX, 22)); Click(window, new Point(captionX, 22)); drain();
                    Require(window.WindowState == WindowState.Normal && native.State(xid) == "Normal", mode + " accepted caption maximize.");
                }
                window.ResizeMode = ResizeMode.CanResize; drain(); size = native.Size(xid);
                Drag(window, new Point(window.ActualWidth - 6, window.ActualHeight - 6), 25, 25);
                wait(() => native.Size(xid) == (size.Width + 25, size.Height + 25));
                Click(window, new Point(220, 22)); Click(window, new Point(220, 22));
                wait(() => window.WindowState == WindowState.Maximized && native.State(xid) == "Maximized");
                Click(window, new Point(160, 22)); Click(window, new Point(160, 22));
                wait(() => window.WindowState == WindowState.Normal && native.State(xid) == "Normal");
            }
            finally { native.Button(1, false); window.Close(); drain(); }
        });
        foreach (string variant in new[] { "remove", "replace", "mutate", "recapture" })
            run("native-WindowChrome-capture-handoff-reentrancy-" + variant, () =>
            {
                var window = NewWindow("Genuine WPF chrome capture reentrancy " + variant); window.WindowStyle = WindowStyle.None;
                var first = new Border { Width = 70, Height = 60, Background = Brushes.Blue, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                var second = new Border { Width = 70, Height = 60, Background = Brushes.Green, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                var content = new Grid(); content.Children.Add(first); content.Children.Add(second); window.Content = content;
                var chrome = NewChrome(); bool lost = false;
                MouseEventHandler? handler = null;
                handler = (_, _) =>
                {
                    first.LostMouseCapture -= handler;
                    lost = true;
                    if (variant == "remove") WindowChrome.SetWindowChrome(window, null!);
                    else if (variant == "replace") WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0) });
                    else if (variant == "mutate") { chrome.CaptionHeight = 0; chrome.ResizeBorderThickness = new Thickness(0); }
                    else Require(Mouse.Capture(second), "Application capture renewal failed.");
                };
                try
                {
                    WindowChrome.SetWindowChrome(window, chrome); window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid)); VerifyDpi(window, xid); drain();
                    Require(Mouse.Capture(first) && ReferenceEquals(Mouse.Captured, first) && !native.CanAcquirePointerGrab(), "The persistent application capture was not native and logical.");
                    first.LostMouseCapture += handler;
                    int pressX = variant switch { "remove" => 100, "replace" => 130, "mutate" => 160, _ => 190 };
                    var origin = native.ClientOrigin(xid); var point = Screen(window, new Point(pressX, 22));
                    native.MovePointer(point.X, point.Y); native.Button(1, true); drain();
                    Require(lost, "The real held caption press did not release the initial capture.");
                    native.MovePointer(point.X + 30, point.Y + 20); drain(); native.Button(1, false); drain();
                    Require(native.ClientOrigin(xid) == origin, "A stale caption decision started WM movement after the application callback.");
                    if (variant == "recapture")
                    {
                        Require(ReferenceEquals(Mouse.Captured, second) && !native.CanAcquirePointerGrab(), "The renewed application capture was not retained in WPF and the native server.");
                        Mouse.Capture(null); drain(); Require(Mouse.Captured == null && native.CanAcquirePointerGrab(), "Renewed capture did not remain releasable by its application owner.");
                    }
                    else Require(Mouse.Captured == null && native.CanAcquirePointerGrab(), "An aborted chrome operation retained logical or native capture.");
                }
                finally { first.LostMouseCapture -= handler; native.Button(1, false); Mouse.Capture(null); window.Close(); drain(); }
            });
        run("native-WindowChrome-resize-constraints", () =>
        {
            var window = NewWindow("Genuine WPF chrome constraints"); window.MinWidth = 260; window.MinHeight = 160; window.MaxWidth = 380; window.MaxHeight = 280;
            try
            {
                WindowChrome.SetWindowChrome(window, NewChrome()); window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid)); VerifyDpi(window, xid); drain();
                Drag(window, new Point(window.ActualWidth - 6, window.ActualHeight - 6), 500, 500);
                wait(() => Near(window.ActualWidth, 380) && Near(window.ActualHeight, 280));
                Drag(window, new Point(window.ActualWidth - 6, window.ActualHeight - 6), -500, -500);
                wait(() => Near(window.ActualWidth, 260) && Near(window.ActualHeight, 160));
            }
            finally { native.Button(1, false); window.Close(); drain(); }
        });
        run("native-WindowChrome-move-release-race-hide-close-and-window-ownership", () =>
        {
            var first = NewWindow("Genuine WPF first chrome owner"); var second = NewWindow("Genuine WPF second chrome owner"); second.Left = 500;
            try
            {
                WindowChrome.SetWindowChrome(first, NewChrome()); WindowChrome.SetWindowChrome(second, NewChrome()); first.Show(); second.Show();
                var firstXid = native.FindWindow(first.Title); var secondXid = native.FindWindow(second.Title); wait(() => native.IsMapped(firstXid) && native.IsMapped(secondXid)); drain(); VerifyDpi(first, firstXid); VerifyDpi(second, secondXid);
                Drag(first, new Point(100, 22), 20, 20);
                var before = native.ClientOrigin(secondXid); var start = Screen(second, new Point(130, 22));
                native.MovePointer(start.X, start.Y); native.Button(1, true); drain(); first.Hide(); drain();
                native.MovePointer(start.X + 28, start.Y + 22); drain(); native.Button(1, false);
                wait(() => native.ClientOrigin(secondXid) == (before.X + 28, before.Y + 22));
                Require(native.CanAcquirePointerGrab(), "Another window's Hide canceled ownership incorrectly or retained the grab.");
                start = Screen(second, new Point(160, 22)); native.MovePointer(start.X, start.Y); native.Button(1, true); native.Button(1, false); drain();
                Require(native.CanAcquirePointerGrab(), "Fast button release left the WM handoff stuck.");
                start = Screen(second, new Point(190, 22)); native.MovePointer(start.X, start.Y);
                native.Button(1, true); drain(); second.Hide(); drain(); native.MovePointer(10, 10); native.Button(1, false); drain();
                Require(!native.IsMapped(secondXid) && native.CanAcquirePointerGrab(), "Hide did not cancel an active native gesture.");
                second.Show(); wait(() => native.IsMapped(secondXid)); start = Screen(second, new Point(220, 22));
                native.MovePointer(start.X, start.Y); native.Button(1, true); drain(); second.Close(); drain(); native.Button(1, false); drain();
                Require(PresentationSource.FromVisual(second) == null && native.CanAcquirePointerGrab(), "Close retained a source or native gesture grab.");
            }
            finally { native.Button(1, false); second.Close(); first.Close(); drain(); }
        });

        void VerifyDpi(Window window, IntPtr xid)
        {
            double expected = requestedDpi / 96.0; var dpi = VisualTreeHelper.GetDpi(window);
            var initialSize = native.Size(xid);
            trace?.Invoke(new { stage = "chrome-observed-dpi", title = window.Title, requestedDpi,
                scaleX = dpi.DpiScaleX, scaleY = dpi.DpiScaleY, actualWidth = window.ActualWidth, actualHeight = window.ActualHeight,
                clientWidth = initialSize.Width, clientHeight = initialSize.Height });
            Require(Near(dpi.DpiScaleX, expected) && Near(dpi.DpiScaleY, expected), "The live Window DPI does not match the explicitly requested Chrome test scale.");
            wait(() =>
            {
                var size = native.Size(xid); var frame = native.FrameExtents(xid) ?? default;
                return size.Width + frame.Left + frame.Right == (int)Math.Round(window.ActualWidth * expected) &&
                    size.Height + frame.Top + frame.Bottom == (int)Math.Round(window.ActualHeight * expected);
            });
        }
        void Click(Window window, Point point)
        {
            var p = Screen(window, point); native.MovePointer(p.X, p.Y); native.Button(1, true); drain(); native.Button(1, false); drain();
        }
        void Drag(Window window, Point point, int dx, int dy)
        {
            var p = Screen(window, point); native.MovePointer(p.X, p.Y); native.Button(1, true); drain();
            native.MovePointer(p.X + dx, p.Y + dy); drain(); native.Button(1, false); drain();
        }
    }
    internal static void StateDenial(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, Action<object>? trace = null)
    {
        run("native-WM-denied-state-background-input-repaint-and-supersession", () =>
        {
            var window = NewWindow("Genuine WPF denied native maximize");
            var content = new Border { Background = Brushes.Red };
            window.Content = content; int presses = 0; content.MouseDown += (_, _) => presses++;
            object? nativeWindow = null; System.Reflection.EventInfo? metricsEvent = null; Action? onMetrics = null;
            try
            {
                window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && native.Pixel(xid, 10, 70) == 0x00ff0000);
                native.RestrictWindowFunctions(xid); drain();
                Require(native.MotifFunctions(xid) == 36, "The independent fault injection did not restrict native resize.");
                nativeWindow = OwnedNativeAccess.Window(PresentationSource.FromVisual(window)!);
                metricsEvent = nativeWindow.GetType().GetEvent("MetricsChanged", OwnedNativeAccess.Members)!;
                int metricsSamples = 0;
                void Snapshot(string stage) => trace?.Invoke(new { stage, requested = window.WindowState.ToString(), observed = native.State(xid), mapped = native.IsMapped(xid), functions = native.MotifFunctions(xid) });
                onMetrics = () => { if (metricsSamples++ < 8) Snapshot("denied-Max-native-metrics"); };
                metricsEvent.AddEventHandler(nativeWindow, onMetrics);
                window.WindowState = WindowState.Maximized;
                Snapshot("denied-Max-setter-before-background-wait");
                bool background = false;
                _ = window.Dispatcher.BeginInvoke(WpfFork::System.Windows.Threading.DispatcherPriority.Background, new Action(() => background = true));
                drain();
                metricsEvent.RemoveEventHandler(nativeWindow, onMetrics); onMetrics = null;
                Snapshot("denied-Max-background-completed");
                Require(background && native.State(xid) == "Normal" && window.WindowState == WindowState.Maximized,
                    "Denied state did not leave the dispatcher live with genuine unresolved native intent.");
                content.Background = Brushes.Blue;
                var point = Screen(window, new Point(80, 100));
                native.MovePointer(point.X, point.Y); native.Button(1, true); drain(); native.Button(1, false); drain();
                wait(() => presses == 1 && native.Pixel(xid, 10, 70) == 0x000000ff);
                native.Expose(xid); drain();
                Require(native.Pixel(xid, 10, 70) == 0x000000ff, "Denied state blocked retained expose repaint.");
                window.WindowState = WindowState.Normal;
                wait(() => native.State(xid) == "Normal" && window.WindowState == WindowState.Normal);
                window.Hide(); drain(); window.Show(); wait(() => native.IsMapped(xid) && native.State(xid) == "Normal");
                window.Close();
                Require(PresentationSource.FromVisual(window) is null, "Denied state retained its native source after recovery and close.");
            }
            finally
            {
                if (onMetrics != null) metricsEvent!.RemoveEventHandler(nativeWindow, onMetrics);
                native.Button(1, false); window.Close(); drain();
            }
        });
    }
    private sealed class CaptionCommandSource(RoutedCommand command) { public RoutedCommand Command { get; } = command; }
    private static Window NewWindow(string title) => new() { Title = title, Width = 320, Height = 220, Left = 100, Top = 100,
        Background = Brushes.DarkRed, ShowActivated = false };
    private static WindowChrome NewChrome() => new() { CaptionHeight = 30, CornerRadius = new CornerRadius(1), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false };
    private static (int X, int Y) Screen(Window window, Point point) { var p = window.PointToScreen(point); return (checked((int)Math.Round(p.X)), checked((int)Math.Round(p.Y))); }
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .01;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (PlatformNotSupportedException) { return; } throw new InvalidOperationException("Unsupported operation was accepted."); }
    private static ControlTemplate ButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter))); return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
