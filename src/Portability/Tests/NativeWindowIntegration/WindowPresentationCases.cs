extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

internal static class WindowPresentationCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain)
    {
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
