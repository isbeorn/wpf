extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

internal static class WindowPresentationCases
{
    internal static void ExerciseEmptySizes(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain,
        int requestedDpi, Action<object> observe)
    {
        double scale = requestedDpi / 96d;
        run("native-empty-transparent-SizeToContent-show-hide-grow-remove-reopen", () =>
        {
            var items = new ItemsControl { ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel))) };
            var window = new Window { Title = "Genuine WPF empty notification host", WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Content = new Grid { Children = { items } } };
            int initialized = 0, closed = 0;
            window.SourceInitialized += (_, _) => initialized++;
            window.Closed += (_, _) => closed++;
            try
            {
                window.Show(); window.Hide();
                var xid = native.FindWindow(window.Title);
                Require(xid != IntPtr.Zero, "Synchronous empty Show/Hide did not retain its native Window.");
                wait(() => !native.IsMapped(xid) && native.Size(xid) == (1u, 1u));
                var source = PresentationSource.FromVisual(window)!;
                Require(Equal(VisualTreeHelper.GetDpi(window).DpiScaleX, scale), "The empty Window did not use the controlled DPI.");
                items.Items.Add(new Border { Width = 80, Height = 48, Background = Brushes.Red });
                window.Show();
                wait(() => native.IsMapped(xid) && native.Size(xid) == ((uint)(80 * scale), (uint)(48 * scale)) && native.Pixel(xid, 4, 4, true) == 0xffff0000);
                items.Items.Clear();
                wait(() => native.Size(xid) == (1u, 1u) && native.Pixel(xid, 0, 0, true) == 0);
                window.Hide(); wait(() => !native.IsMapped(xid)); window.Show();
                wait(() => native.IsMapped(xid) && native.Size(xid) == (1u, 1u));
                items.Items.Add(new Border { Width = 56, Height = 32, Background = Brushes.Green });
                wait(() => native.Size(xid) == ((uint)(56 * scale), (uint)(32 * scale)) && native.Pixel(xid, 4, 4, true) == 0xff008000);
                items.Items.Clear(); wait(() => native.Size(xid) == (1u, 1u)); drain();
                Require(initialized == 1 && ReferenceEquals(source, PresentationSource.FromVisual(window)), "Empty/grown hide-show replaced the original source.");
                var physical = native.Size(xid);
                observe(new { kind = "empty-notification-host", requestedDpi, nativeWidth = physical.Width, nativeHeight = physical.Height, window.ActualWidth, window.ActualHeight, initialized });
            }
            finally { window.Close(); drain(); }
            Require(closed == 1 && PresentationSource.FromVisual(window) is null && native.FindWindow(window.Title) == IntPtr.Zero,
                "The empty notification host did not close and detach once.");
        });
        run("native-zero-subpixel-thresholds-both-axes-initial-shown-and-recovery", () =>
        {
            foreach (double pixels in new[] { 0, 0.5, 1, 1.5, 2 })
            foreach (Size extent in new[] { new Size(pixels / scale, 40), new Size(40, pixels / scale), new Size(pixels / scale, pixels / scale) })
            foreach (bool initial in new[] { true, false })
            {
                var window = new Window { Title = "Genuine WPF minimum extent " + pixels + extent + initial,
                    WindowStyle = WindowStyle.None, Width = initial ? extent.Width : 80, Height = initial ? extent.Height : 60,
                    ShowActivated = false, Background = Brushes.Red };
                try
                {
                    window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
                    if (!initial) { window.Width = extent.Width; window.Height = extent.Height; }
                    var expected = ((uint)Math.Max(1, Math.Floor(extent.Width * scale)), (uint)Math.Max(1, Math.Floor(extent.Height * scale)));
                    wait(() => native.Size(xid) == expected && native.Pixel(xid, 0, 0) == 0xff0000);
                    drain();
                    Require(Equal(window.ActualWidth * scale, expected.Item1) && Equal(window.ActualHeight * scale, expected.Item2),
                        "Published Window layout differs from the actual minimum native client geometry.");
                    window.Width = 80; window.Height = 60;
                    wait(() => native.Size(xid) == ((uint)(80 * scale), (uint)(60 * scale)) && native.Pixel(xid, 4, 4) == 0xff0000);
                    window.Width = extent.Width; window.Height = extent.Height;
                    wait(() => native.Size(xid) == expected && native.Pixel(xid, 0, 0) == 0xff0000);
                    var physical = native.Size(xid);
                    observe(new { kind = "minimum-native-extent", requestedDpi, pixels, initial, requested = extent, nativeWidth = physical.Width, nativeHeight = physical.Height });
                }
                finally { window.Close(); drain(); }
            }
            var recovery = new Window { Title = "Genuine WPF frame after minimum extents", Width = 80, Height = 60,
                WindowStyle = WindowStyle.None, ShowActivated = false, Background = Brushes.Blue };
            try
            {
                recovery.Show(); var xid = native.FindWindow(recovery.Title);
                wait(() => native.IsMapped(xid) && native.Pixel(xid, 4, 4) == 0xff);
            }
            finally { recovery.Close(); drain(); }
        });
        run("native-finite-maximum-pixel-sentinel-and-invalid-input-preservation", () =>
        {
            var window = new Window { Title = "Genuine WPF finite maximum constraints", Width = 80, Height = 60,
                WindowStyle = WindowStyle.None, ShowActivated = false, Background = Brushes.Red };
            try
            {
                window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
                object peer = OwnedNativeAccess.Peer(PresentationSource.FromVisual(window)!);
                var setSize = peer.GetType().GetMethod("SetClientSize")!.CreateDelegate<Action<Size>>(peer);
                var setConstraints = peer.GetType().GetMethod("SetClientSizeConstraints")!.CreateDelegate<Action<Size, Size>>(peer);
                OwnedNativeAccess.Call(OwnedNativeAccess.Connection, "Synchronize");
                var hintsBeforeRejects = ((IEnumerable<IntPtr>)OwnedNativeAccess.Call(native, "Property", xid, "WM_NORMAL_HINTS", 41)!).ToArray();
                Require(hintsBeforeRejects.Length >= 9, "The original WM_NORMAL_HINTS property is missing its size fields.");
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, double.MaxValue, 65536 / scale })
                {
                    Reject(() => setSize(new Size(invalid, 60)));
                    Reject(() => setSize(new Size(80, invalid)));
                }
                Reject(() => window.Width = -1); Reject(() => window.Height = -1);
                foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, -1, double.MaxValue, 65536 / scale })
                {
                    Reject(() => setConstraints(new Size(), new Size(invalid, double.PositiveInfinity)));
                    Reject(() => setConstraints(new Size(), new Size(double.PositiveInfinity, invalid)));
                }
                OwnedNativeAccess.Call(OwnedNativeAccess.Connection, "Synchronize");
                Require(((IEnumerable<IntPtr>)OwnedNativeAccess.Call(native, "Property", xid, "WM_NORMAL_HINTS", 41)!).SequenceEqual(hintsBeforeRejects),
                    "A rejected native size or maximum changed the actual WM_NORMAL_HINTS sequence.");
                Require(native.Size(xid) == ((uint)(80 * scale), (uint)(60 * scale)), "A rejected native size or maximum changed client geometry.");
                foreach (Size maximum in new[] { new Size(0, double.PositiveInfinity), new Size(double.PositiveInfinity, 0),
                    new Size(0.5 / scale, double.PositiveInfinity), new Size(double.PositiveInfinity, 0.5 / scale),
                    new Size(2 / scale, 2 / scale), new Size(double.PositiveInfinity, double.PositiveInfinity) })
                {
                    window.MaxWidth = maximum.Width; window.MaxHeight = maximum.Height;
                    bool finite = !double.IsPositiveInfinity(maximum.Width) || !double.IsPositiveInfinity(maximum.Height);
                    long width = double.IsPositiveInfinity(maximum.Width) ? 65535 : Math.Max(1, (long)Math.Floor(maximum.Width * scale));
                    long height = double.IsPositiveInfinity(maximum.Height) ? 65535 : Math.Max(1, (long)Math.Floor(maximum.Height * scale));
                    wait(() => MaximumMatches(finite, width, height));
                    observe(new { kind = "finite-native-maximum", requestedDpi, maximum = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture), finite, width, height });
                }
                wait(() =>
                {
                    var physical = native.Size(xid);
                    var client = (Size)OwnedNativeAccess.Get(peer, "ClientSize");
                    return Equal(window.Width * scale, physical.Width) && Equal(window.Height * scale, physical.Height)
                        && Equal(window.ActualWidth * scale, physical.Width) && Equal(window.ActualHeight * scale, physical.Height)
                        && Equal(client.Width * scale, physical.Width) && Equal(client.Height * scale, physical.Height);
                });
                window.Width = 80; window.Height = 60;
                wait(() => native.Size(xid) == ((uint)(80 * scale), (uint)(60 * scale)) && native.Pixel(xid, 4, 4) == 0xff0000);

                bool MaximumMatches(bool finite, long width, long height)
                {
                    var hints = (IEnumerable<IntPtr>)OwnedNativeAccess.Call(native, "Property", xid, "WM_NORMAL_HINTS", 41)!;
                    long[] values = hints.Select(value => value.ToInt64()).ToArray();
                    Require(values.Length >= 9, "The actual WM_NORMAL_HINTS property is missing its size fields.");
                    bool hasMaximum = (values[0] & (1 << 5)) != 0;
                    return hasMaximum == finite && (!finite || values[7] == width && values[8] == height);
                }
            }
            finally { window.Close(); drain(); }

            static void Reject(Action body)
            {
                try { body(); }
                catch (Exception error) when (error is ArgumentException or OverflowException or PlatformNotSupportedException) { return; }
                throw new InvalidOperationException("An invalid native size or maximum was accepted.");
            }
        });
    }

    internal static void ExerciseFrameBounds(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain,
        int requestedDpi, Action<object> observe)
    {
        double scale = requestedDpi / 96d;
        run("native-late-frame-add-remove-retains-outer-size-and-user-resize", () =>
        {
            Require(native.HasWindowManager, "Late frame sizing requires a real window manager.");
            var window = new Window { Title = "Genuine WPF late frame sizing", Left = 120, Top = 80,
                Width = 320, Height = 240, WindowStyle = WindowStyle.None, ShowActivated = false, Background = Brushes.Red };
            IntPtr xid = IntPtr.Zero;
            Size expected = new(320, 240);
            try
            {
                window.Show(); xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && HasOuter(window, xid, expected) && SizeAcknowledged(window));
                var source = PresentationSource.FromVisual(window);
                Observe("zero-frame-size-acknowledged", window, xid, expected);
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                wait(() => HasFrame(xid, true));
                try { wait(() => HasOuter(window, xid, expected)); }
                finally { Observe("late-frame-added", window, xid, expected); }
                foreach (Size next in new[] { new Size(360, 272), new Size(300, 224) })
                {
                    var frame = native.FrameExtents(xid)!.Value;
                    native.Resize(xid, checked((uint)(next.Width * scale - frame.Left - frame.Right)),
                        checked((uint)(next.Height * scale - frame.Top - frame.Bottom)));
                    wait(() => HasOuter(window, xid, next));
                    expected = next;
                    Observe("later-native-user-size", window, xid, expected);
                }
                window.WindowStyle = WindowStyle.None;
                wait(() => HasFrame(xid, false));
                try { wait(() => HasOuter(window, xid, expected)); }
                finally { Observe("late-frame-removed", window, xid, expected); }
                window.Hide(); wait(() => !native.IsMapped(xid)); window.Show();
                wait(() => native.IsMapped(xid) && HasOuter(window, xid, expected));
                Require(ReferenceEquals(source, PresentationSource.FromVisual(window)), "Frame changes or hide/show replaced the native presentation source.");
            }
            finally { window.Close(); drain(); }
            Require(PresentationSource.FromVisual(window) is null && !native.IsMapped(xid), "Late frame sizing retained a closed source or native window.");
        });
        run("native-framed-outer-size-save-recreate-three-lifetimes", () =>
        {
            Require(native.HasWindowManager, "Framed lifetime sizing requires a real window manager.");
            Size persisted = new(320, 240);
            for (int lifetime = 0; lifetime < 3; lifetime++)
            {
                Size requested = persisted;
                var window = new Window { Title = "Genuine WPF framed lifetime " + lifetime, Left = 120, Top = 80,
                    Width = requested.Width, Height = requested.Height, ShowActivated = false, Background = Brushes.Blue };
                IntPtr xid = IntPtr.Zero;
                try
                {
                    window.Show(); xid = native.FindWindow(window.Title);
                    wait(() => native.IsMapped(xid) && HasFrame(xid, true));
                    try { wait(() => HasOuter(window, xid, requested)); }
                    finally { Observe("framed-lifetime-" + lifetime, window, xid, requested); }
                    persisted = new Size(window.ActualWidth, window.ActualHeight);
                }
                finally { window.Close(); drain(); }
                Require(PresentationSource.FromVisual(window) is null && !native.IsMapped(xid), "A saved lifetime retained its old source or native window.");
                Require(persisted == new Size(320, 240), "Saving actual outer dimensions accumulated a native frame across lifetimes.");
            }
        });

        bool HasFrame(IntPtr xid, bool decorated)
        {
            var frame = native.FrameExtents(xid);
            return frame is { } value && (value.Left + value.Right + value.Top + value.Bottom > 0) == decorated;
        }
        static bool SizeAcknowledged(Window window) =>
            (Convert.ToInt32(OwnedNativeAccess.Get(window, "_portablePendingBounds")) & 3) == 0
            && (Convert.ToInt32(OwnedNativeAccess.Get(window, "_portableBoundsAwaitingAck")) & 3) == 0;
        bool HasOuter(Window window, IntPtr xid, Size expected)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var size = native.Size(xid);
            var frame = native.FrameExtents(xid) ?? default;
            return Equal(dpi.DpiScaleX, scale) && Equal(dpi.DpiScaleY, scale)
                && Equal(window.Width, expected.Width) && Equal(window.Height, expected.Height)
                && Equal(window.ActualWidth, expected.Width) && Equal(window.ActualHeight, expected.Height)
                && Equal(window.RestoreBounds.Width, expected.Width) && Equal(window.RestoreBounds.Height, expected.Height)
                && Equal(size.Width + frame.Left + frame.Right, expected.Width * scale)
                && Equal(size.Height + frame.Top + frame.Bottom, expected.Height * scale);
        }
        void Observe(string stage, Window window, IntPtr xid, Size expected)
        {
            var size = native.Size(xid);
            var frame = native.FrameExtents(xid) ?? default;
            var origin = native.ClientOrigin(xid);
            var dpi = VisualTreeHelper.GetDpi(window);
            observe(new { kind = "normal-frame-outer-size", stage, requestedDpi, xid = xid.ToInt64(),
                expectedWidth = expected.Width, expectedHeight = expected.Height,
                window.Width, window.Height, window.ActualWidth, window.ActualHeight,
                restoreWidth = window.RestoreBounds.Width, restoreHeight = window.RestoreBounds.Height,
                clientWidth = size.Width, clientHeight = size.Height, clientX = origin.X, clientY = origin.Y,
                frame.Left, frame.Right, frame.Top, frame.Bottom, dpiX = dpi.DpiScaleX, dpiY = dpi.DpiScaleY,
                pending = OwnedNativeAccess.Get(window, "_portablePendingBounds").ToString(),
                awaitingAck = OwnedNativeAccess.Get(window, "_portableBoundsAwaitingAck").ToString() });
        }
    }

    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain)
    {
        run("native-Window-startup-centering-and-hide-show", () =>
        {
            var screensType = typeof(Wpf.Portable.NativeHost.NativeWindowHost).Assembly.GetType("Wpf.Portable.NativeHost.Linux.X11Screens")!;
            using var screens = (IDisposable)Activator.CreateInstance(screensType, OwnedNativeAccess.Members, null, [OwnedNativeAccess.Connection], null)!;
            var screen = OwnedNativeAccess.Call(screens, "ReadPrimary") ?? throw new InvalidOperationException("The owned startup placement fixture requires its observed single screen.");
            var observed = OwnedNativeAccess.Get(screen, "WorkingArea");
            var area = new Rect((int)OwnedNativeAccess.Get(observed, "X"), (int)OwnedNativeAccess.Get(observed, "Y"),
                (int)OwnedNativeAccess.Get(observed, "Width"), (int)OwnedNativeAccess.Get(observed, "Height"));
            var owner = new Window { Title = "Genuine WPF startup placement owner", Left = 100, Top = 90, Width = 400, Height = 280,
                ShowActivated = false };
            try
            {
                owner.Show(); var ownerXid = native.FindWindow(owner.Title);
                wait(() => native.IsMapped(ownerXid)); drain();
                foreach (bool owned in new[] { false, true })
                foreach (WindowStartupLocation location in new[] { WindowStartupLocation.CenterScreen, WindowStartupLocation.CenterOwner })
                {
                    var window = new Window { Title = "Genuine WPF startup placement " + owned + location, Left = 20, Top = 30,
                        Width = 180, Height = 120, ShowActivated = false, Owner = owned ? owner : null, WindowStartupLocation = location };
                    int initialized = 0; window.SourceInitialized += (_, _) => initialized++;
                    try
                    {
                        window.Show(); var xid = native.FindWindow(window.Title);
                        wait(() => native.IsMapped(xid) && Centered());
                        var source = PresentationSource.FromVisual(window);
                        window.Hide(); wait(() => !native.IsMapped(xid));
                        window.Left = 120; window.Top = 140; window.Show();
                        var dpi = VisualTreeHelper.GetDpi(window);
                        wait(() => native.IsMapped(xid) && Math.Abs(Bounds(xid).X - 120 * dpi.DpiScaleX) <= 1 && Math.Abs(Bounds(xid).Y - 140 * dpi.DpiScaleY) <= 1);
                        Require(initialized == 1 && native.FindWindow(window.Title) == xid && ReferenceEquals(source, PresentationSource.FromVisual(window)),
                            "Startup placement changed native identity or repeated centering through hide/show.");

                        bool Centered()
                        {
                            Rect actual = Bounds(xid), target = location == WindowStartupLocation.CenterOwner && owned ? Bounds(ownerXid) : area;
                            var dpi = VisualTreeHelper.GetDpi(window);
                            double x = location == WindowStartupLocation.CenterOwner && !owned ? 20 * dpi.DpiScaleX : target.X + (target.Width - actual.Width) / 2;
                            double y = location == WindowStartupLocation.CenterOwner && !owned ? 30 * dpi.DpiScaleY : target.Y + (target.Height - actual.Height) / 2;
                            if (location == WindowStartupLocation.CenterOwner && owned)
                            {
                                x = Math.Max(area.Left, Math.Min(x, area.Right - actual.Width));
                                y = Math.Max(area.Top, Math.Min(y, area.Bottom - actual.Height));
                            }
                            return Math.Abs(actual.X - x) <= 1 && Math.Abs(actual.Y - y) <= 1;
                        }
                    }
                    finally { window.Close(); drain(); }
                }
            }
            finally { owner.Close(); drain(); }

            Rect Bounds(IntPtr xid)
            {
                var origin = native.ClientOrigin(xid); var size = native.Size(xid);
                var frame = native.FrameExtents(xid) ?? (Left: 0, Right: 0, Top: 0, Bottom: 0);
                return new Rect(origin.X - frame.Left, origin.Y - frame.Top, size.Width + frame.Left + frame.Right, size.Height + frame.Top + frame.Bottom);
            }
        });
        run("native-mapping-lifetime-show-hide-show-close", () =>
        {
            Require(!native.IsMapped(IntPtr.Zero), "A zero native window was mapped.");
            var window = new Window { Title = "Genuine WPF mapping lifetime", Width = 180, Height = 120,
                ShowActivated = false, Background = Brushes.Red };
            int initialized = 0, closed = 0;
            window.SourceInitialized += (_, _) => initialized++;
            window.Closed += (_, _) => closed++;
            try
            {
                window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid));
                var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("Show did not attach a genuine native source.");
                Require(initialized == 1 && closed == 0, "Show did not create one genuine native source.");
                window.Hide(); wait(() => !native.IsMapped(xid));
                Require(!window.IsVisible && native.FindWindow(window.Title) == xid &&
                    ReferenceEquals(source, PresentationSource.FromVisual(window)), "Hide replaced or detached the native source.");
                window.Show(); wait(() => native.IsMapped(xid));
                Require(native.FindWindow(window.Title) == xid && initialized == 1 && closed == 0 &&
                    ReferenceEquals(source, PresentationSource.FromVisual(window)), "Show after Hide replaced the native identity.");
                window.Close();
                wait(() => closed == 1 && source.IsDisposed && PresentationSource.FromVisual(window) is null && !native.IsMapped(xid));
                Require(native.FindWindow(window.Title) == IntPtr.Zero && !native.IsMapped(IntPtr.Zero),
                    "Close retained a discoverable or zero native window.");
            }
            finally { window.Close(); drain(); }
            Require(closed == 1 && PresentationSource.FromVisual(window) is null, "The mapping Window did not close and detach once.");
        });
        run("native-Window-style-paired-live-decorations", () =>
        {
            var window = new Window { Title = "Genuine WPF Window style", Width = 180, Height = 120,
                ShowActivated = false, Background = Brushes.Red };
            int initialized = 0, closed = 0;
            window.SourceInitialized += (_, _) => initialized++;
            window.Closed += (_, _) => closed++;
            try
            {
                Require(window.WindowStyle == WindowStyle.SingleBorderWindow, "The ordinary Window style default changed.");
                window.WindowStyle = WindowStyle.None;
                window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && native.Decorations(xid) == 0 && native.Pixel(xid, 8, 8) == 0xff0000);
                if (native.HasWindowManager) wait(() => HasAppliedFrame(window, xid, false));
                var source = PresentationSource.FromVisual(window)!;
                IntPtr focus = native.FocusedWindow;
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                wait(() => native.Decorations(xid) != 0 && native.Pixel(xid, 8, 8) == 0xff0000);
                if (native.HasWindowManager) wait(() => HasAppliedFrame(window, xid, true));
                window.WindowStyle = WindowStyle.None;
                wait(() => native.Decorations(xid) == 0 && native.Pixel(xid, 8, 8) == 0xff0000);
                if (native.HasWindowManager) wait(() => HasAppliedFrame(window, xid, false));
                Require(ReferenceEquals(source, PresentationSource.FromVisual(window)) && initialized == 1 && closed == 0 &&
                    native.FocusedWindow == focus, "Style changes replaced the native/source lifetime or changed focus.");
                window.Hide(); drain(); window.Show(); drain();
                Require(native.FindWindow(window.Title) == xid && native.Decorations(xid) == 0 && initialized == 1,
                    "Hide/show lost the decoration setting or native identity.");
            }
            finally { window.Close(); drain(); }
            Require(closed == 1 && PresentationSource.FromVisual(window) is null, "The style Window did not close and detach once.");

            bool HasAppliedFrame(Window current, IntPtr xid, bool decorated)
            {
                if (native.FrameExtents(xid) is not { } frame) return false;
                if ((frame.Left + frame.Right + frame.Top + frame.Bottom > 0) != decorated) return false;
                var size = native.Size(xid);
                var origin = native.ClientOrigin(xid);
                var dpi = VisualTreeHelper.GetDpi(current);
                return Equal(current.ActualWidth * dpi.DpiScaleX, size.Width + frame.Left + frame.Right) &&
                    Equal(current.ActualHeight * dpi.DpiScaleY, size.Height + frame.Top + frame.Bottom) &&
                    Equal(current.Left * dpi.DpiScaleX, origin.X - frame.Left) &&
                    Equal(current.Top * dpi.DpiScaleY, origin.Y - frame.Top);
            }
        });
        if (native.HasWindowManager) run("native-Window-Topmost-paired-request-and-stacking", () =>
        {
            var window = new Window { Title = "Genuine WPF Window topmost", Width = 180, Height = 120,
                ShowActivated = false, Background = Brushes.Red };
            var other = new Window { Title = "Genuine WPF Window ordinary stacking", Width = 180, Height = 120,
                ShowActivated = false, Background = Brushes.Blue };
            try
            {
                window.Topmost = true;
                window.Show(); var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && native.IsTopmost(xid));
                var source = PresentationSource.FromVisual(window)!;
                other.Show(); var otherXid = native.FindWindow(other.Title);
                wait(() => native.IsMapped(otherXid));
                if (native.HasWindowManager)
                    wait(() => IsAbove(native, xid, otherXid));
                IntPtr focus = native.FocusedWindow;
                window.Topmost = false; wait(() => !native.IsTopmost(xid));
                if (native.HasWindowManager)
                {
                    other.Topmost = true; wait(() => native.IsTopmost(otherXid) && IsAbove(native, otherXid, xid));
                    other.Topmost = false; wait(() => !native.IsTopmost(otherXid));
                }
                window.Topmost = true; wait(() => native.IsTopmost(xid));
                window.Topmost = false; wait(() => !native.IsTopmost(xid));
                Require(native.FocusedWindow == focus && ReferenceEquals(source, PresentationSource.FromVisual(window)),
                    "Topmost changes activated or reconstructed the Window.");
            }
            finally { other.Close(); window.Close(); drain(); }
        });
        run("native-transparent-Window-ARGB-paint-resize-owner-lifetime", () =>
        {
            for (int lifetime = 0; lifetime < 2; lifetime++)
            {
                var owner = new Window { Title = "Genuine WPF alpha owner " + lifetime, Width = 220, Height = 140, Background = Brushes.White };
                var window = new Window { Title = "Genuine WPF alpha Window " + lifetime, Width = 160, Height = 100,
                    ShowActivated = false, ShowInTaskbar = false };
                int initialized = 0, closed = 0;
                window.SourceInitialized += (_, _) => initialized++;
                window.Closed += (_, _) => closed++;
                try
                {
                    owner.Show(); var ownerXid = native.FindWindow(owner.Title);
                    wait(() => native.IsMapped(ownerXid)); native.Focus(ownerXid); drain();
                    window.Owner = owner;
                    window.WindowStyle = WindowStyle.None;
                    window.AllowsTransparency = true;
                    window.Background = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
                    window.Show(); var xid = native.FindWindow(window.Title);
                    wait(() => native.IsMapped(xid) && native.Pixel(xid, 8, 8, true) == 0x80800000);
                    var source = PresentationSource.FromVisual(window)!;
                    Require(native.PopupAttributes(xid).Depth == 32 && !native.PopupAttributes(xid).OverrideRedirect &&
                        native.Decorations(xid) == 0 && native.SkipsTaskbar(xid) && native.TransientOwner(xid) == ownerXid &&
                        native.FocusedWindow == ownerXid && !window.IsActive, "The transparent Window lost ARGB/native Window/owner/taskbar/nonactivation semantics.");
                    window.Background = new SolidColorBrush(Color.FromArgb(64, 0, 0, 255));
                    wait(() => native.Pixel(xid, 8, 8, true) == 0x40000040);
                    window.Background = Brushes.Transparent;
                    wait(() => native.Pixel(xid, 8, 8, true) == 0);
                    window.Background = Brushes.Green;
                    wait(() => native.Pixel(xid, 8, 8, true) == 0xff008000);
                    window.Background = new SolidColorBrush(Color.FromArgb(64, 0, 0, 255));
                    wait(() => native.Pixel(xid, 8, 8, true) == 0x40000040);
                    native.Expose(xid); drain();
                    Require(native.Pixel(xid, 8, 8, true) == 0x40000040, "Expose lost actual native Window alpha.");
                    window.Width += 48; window.Height += 32;
                    wait(() => window.ActualWidth == 208 && window.ActualHeight == 132 && native.Pixel(xid, 190, 115, true) == 0x40000040);
                    window.Width -= 48; window.Height -= 32;
                    wait(() => window.ActualWidth == 160 && window.ActualHeight == 100 && native.Pixel(xid, 145, 85, true) == 0x40000040);
                    Require(owner.CaptureMouse() && ReferenceEquals(Mouse.Captured, owner) && !native.CanAcquirePointerGrab(),
                        "A transparent child prevented actual owner capture.");
                    window.Topmost = true; drain(); window.Topmost = false; drain();
                    Require(ReferenceEquals(Mouse.Captured, owner) && native.FocusedWindow == ownerXid,
                        "Changing transparent Window stacking stole owner capture or focus.");
                    owner.ReleaseMouseCapture(); drain();
                    Require(native.CanAcquirePointerGrab(), "Owner capture did not release after transparent Window presentation.");
                    window.Hide(); wait(() => !native.IsMapped(xid));
                    window.Show(); wait(() => native.IsMapped(xid) && native.Pixel(xid, 8, 8, true) == 0x40000040);
                    Require(initialized == 1 && ReferenceEquals(source, PresentationSource.FromVisual(window)) && native.FocusedWindow == ownerXid,
                        "Transparent hide/show changed source identity or owner focus.");
                    owner.Close(); drain();
                    Require(closed == 1 && source.IsDisposed && PresentationSource.FromVisual(window) is null &&
                        native.FindWindow(window.Title) == IntPtr.Zero && owner.OwnedWindows.Count == 0, "Owner close retained the transparent Window/source.");
                }
                finally { window.Close(); owner.Close(); drain(); }
                Require(closed == 1 && PresentationSource.FromVisual(window) is null, "Repeated transparent Window lifetime did not close once.");
            }
        });
        run("Window-transparency-and-style-public-validation", () =>
        {
            var window = new Window { Title = "Genuine WPF alpha validation", Width = 140, Height = 90, ShowActivated = false };
            try
            {
                window.AllowsTransparency = true;
                Expect<InvalidOperationException>(() => window.Show());
                Require(PresentationSource.FromVisual(window) is null, "Invalid transparency/style created a native source.");
                window.WindowStyle = WindowStyle.None;
                window.Show(); drain();
                var source = PresentationSource.FromVisual(window)!;
                Expect<InvalidOperationException>(() => window.WindowStyle = WindowStyle.SingleBorderWindow);
                Expect<InvalidOperationException>(() => window.AllowsTransparency = false);
                window.Hide(); drain();
                Expect<InvalidOperationException>(() => window.AllowsTransparency = false);
                Require(window.WindowStyle == WindowStyle.None && window.AllowsTransparency && ReferenceEquals(source, PresentationSource.FromVisual(window)),
                    "Rejected changes altered public values or the source.");
            }
            finally { window.Close(); drain(); }
            var unsupported = new Window();
            try { Expect<PlatformNotSupportedException>(() => unsupported.WindowStyle = WindowStyle.ToolWindow); }
            finally { unsupported.Close(); }
        });
    }

    private static bool IsAbove(X11Fixture native, IntPtr higher, IntPtr lower)
    {
        var values = native.ClientStacking.ToList();
        int high = values.IndexOf(higher), low = values.IndexOf(lower);
        return high >= 0 && low >= 0 && high > low;
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool Equal(double first, double second) => Math.Abs(first - second) < 0.000001;
}
