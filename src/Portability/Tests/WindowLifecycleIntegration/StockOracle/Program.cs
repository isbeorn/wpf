using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--unanchored-context-menu")
            return UnanchoredContextMenuReference.Run();
        if (args.Length == 1 && args[0] == "--empty-sizing")
            return RunEmptySizing();
        if (args.Length == 2 && args[0] == "--application-run")
            return ApplicationRunCases.RunChild(args[1]);
        var checks = new List<object>();
        bool failed = false;
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool increase in new[] { true, false })
        {
            Window? window = null;
            var snapshots = new List<object>();
            try
            {
                window = new Window { Title = "WPF stock restore oracle", Left = 160, Top = 140, Width = 400, Height = 300, ShowActivated = false };
                window.Show(); Drain(); snapshots.Add(Capture("normal", window));
                window.WindowState = state; Drain(); snapshots.Add(Capture("state", window));
                Rect stateBounds = NativeBounds(window);
                double width = increase ? 520 : 280, height = increase ? 380 : 220;
                double left = increase ? 200 : 120, top = increase ? 170 : 100;
                window.Width = width; window.Height = height; window.Left = left; window.Top = top;
                Drain(); snapshots.Add(Capture("edited", window));
                Assert(NativeBounds(window) == stateBounds, "Editing restore bounds changed the displayed state bounds.");
                Rect restore = window.RestoreBounds;
                Assert(Near(restore.Width, width) && Near(restore.Height, height) && Near(restore.Left, left) && Near(restore.Top, top), "RestoreBounds did not retain edits on all axes.");
                window.WindowState = WindowState.Normal; Drain(); snapshots.Add(Capture("restored", window));
                Assert(Near(window.Width, width) && Near(window.Height, height) && Near(window.Left, left) && Near(window.Top, top), "Normal state did not apply edited restore bounds.");
                checks.Add(new { name = state + (increase ? "-increase" : "-decrease"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true;
                checks.Add(new { name = state + (increase ? "-increase" : "-decrease"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { window?.Close(); }
        }
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool large in new[] { false, true })
        {
            var constrained = new Window { Title = "WPF restore constraint oracle", Width = 400, Height = 300,
                MinWidth = 200, MinHeight = 160, MaxWidth = 600, MaxHeight = 500, ShowActivated = false };
            var snapshots = new List<object>();
            try
            {
                constrained.Show(); Drain(); constrained.WindowState = state; Drain();
                constrained.Width = large ? 800 : 100; constrained.Height = large ? 650 : 80;
                snapshots.Add(Capture("edited-constrained-state", constrained));
                constrained.WindowState = WindowState.Normal; Drain(); snapshots.Add(Capture("restored-constrained", constrained));
                Assert(Near(constrained.Width, large ? 600 : 200) && Near(constrained.Height, large ? 500 : 160), "Restored size did not respect min/max constraints.");
                checks.Add(new { name = state + (large ? "-above-maximum" : "-below-minimum"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true; checks.Add(new { name = state + (large ? "-above-maximum" : "-below-minimum"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { constrained.Close(); }
        }
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        {
            var external = new Window { Title = "WPF external state visibility oracle", Width = 400, Height = 300 };
            var snapshots = new List<object>();
            try
            {
                external.Show(); Drain();
                IntPtr handle = new WindowInteropHelper(external).Handle;
                ShowWindow(handle, state == WindowState.Maximized ? 3 : 6); Drain();
                snapshots.Add(Capture("external-state", external));
                Assert(external.WindowState == state, "WPF did not observe the external native state.");
                Assert(Near(external.Width, 400) && Near(external.Height, 300), "Externally observed state did not expose restore dimensions.");
                external.Hide(); Drain(); snapshots.Add(Capture("hidden", external));
                external.Show(); Drain(); snapshots.Add(Capture("reshown", external));
                Assert(external.WindowState == state && (state == WindowState.Maximized ? IsZoomed(handle) : IsIconic(handle)),
                    "Hide/Show did not preserve an externally selected native window state.");
                checks.Add(new { name = "external-" + state + "-hide-show", status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true;
                checks.Add(new { name = "external-" + state + "-hide-show", status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { external.Close(); }
        }
        foreach (SizeToContent mode in Enum.GetValues<SizeToContent>())
        foreach (bool constrained in new[] { false, true })
        {
            var element = new SizingElement();
            var sizing = new Window { Title = "WPF SizeToContent oracle", Width = 280, Height = 220, Content = element, ShowActivated = false };
            var snapshots = new List<object>();
            try
            {
                if (constrained) { sizing.MinWidth = 200; sizing.MinHeight = 160; sizing.MaxWidth = 300; sizing.MaxHeight = 240; }
                sizing.Show(); Drain();
                Size frame = FrameSize(sizing);
                sizing.SizeToContent = mode;
                foreach (Size desired in new[] { new Size(160, 120), new Size(400, 300), new Size(140, 100) })
                {
                    element.SetDesired(desired); sizing.UpdateLayout(); Drain();
                    Rect displayed = NativeBounds(sizing);
                    double width = mode is SizeToContent.Width or SizeToContent.WidthAndHeight ? desired.Width + frame.Width : 280;
                    double height = mode is SizeToContent.Height or SizeToContent.WidthAndHeight ? desired.Height + frame.Height : 220;
                    if (constrained) { width = Math.Clamp(width, 200, 300); height = Math.Clamp(height, 160, 240); }
                    snapshots.Add(new { stage = "desired-" + desired, observed = Capture("layout", sizing), element.ActualWidth, element.ActualHeight });
                    Assert(Near(displayed.Width, width) && Near(displayed.Height, height), "SizeToContent axes or constraints did not follow content layout.");
                }
                Rect manual = NativeBounds(sizing);
                sizing.SizeToContent = SizeToContent.Manual;
                element.SetDesired(new Size(260, 180)); sizing.UpdateLayout(); Drain();
                Assert(NativeBounds(sizing) == manual, "Returning to Manual did not preserve current size.");
                sizing.Width = constrained ? 250 : 360; sizing.Height = constrained ? 200 : 280; sizing.UpdateLayout(); Drain();
                Assert(Near(NativeBounds(sizing).Width, constrained ? 250 : 360) && Near(NativeBounds(sizing).Height, constrained ? 200 : 280), "Manual dimensions did not resume control.");
                checks.Add(new { name = "size-to-content-" + mode + (constrained ? "-constrained" : "-free"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true; checks.Add(new { name = "size-to-content-" + mode + (constrained ? "-constrained" : "-free"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { sizing.Close(); }
        }
        var contentProbe = new Window { Title = "WPF content notification oracle", Width = 320, Height = 240, ShowActivated = false };
        try
        {
            int notifications = 0;
            contentProbe.ContentRendered += (_, _) => notifications++;
            contentProbe.Show(); Drain();
            Assert(notifications == 0, "Stock bare Window unexpectedly raised ContentRendered.");
            contentProbe.Content = new FrameworkElement(); Drain();
            Assert(notifications == 1, "Stock Window did not raise ContentRendered after content assignment.");
            checks.Add(new { name = "bare-window-content-notification", status = "pass", detail = "No ContentRendered with null Content, one notification after assigning content." });
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = "bare-window-content-notification", status = "fail", detail = exception.ToString() });
        }
        finally { contentProbe.Close(); }
        foreach (ApplicationRunResult result in ApplicationRunCases.RunAll())
        {
            checks.Add(result);
            failed |= result.status == "fail";
        }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "stock-window-restore-oracle", os = RuntimeInformation.OSDescription,
            scope = "Microsoft WindowsDesktop WPF native state, restore bounds and process-isolated public Application.Run", assembly = typeof(Window).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }
    private static int RunEmptySizing()
    {
        var checks = new List<object>();
        string? failure = null;
        IntPtr foregroundBefore = GetForegroundWindow();
        PresentationSource? priorSource = null;
        try
        {
            for (int lifetime = 0; lifetime < 2; lifetime++)
            {
                EmptyCase("empty-auto-lifetime-" + lifetime, (window, content, events, snapshots) =>
                {
                    snapshots.Add(CaptureEmpty("before-show", window, events));
                    window.Show(); window.Hide();
                    snapshots.Add(CaptureEmpty("synchronous-empty-show-hide", window, events));
                    var source = PresentationSource.FromVisual(window);
                    Assert(source != null && !ReferenceEquals(priorSource, source), "Fresh lifetime did not create a distinct source.");
                    priorSource = source;
                    content.Children.Add(new System.Windows.Controls.Border { Width = 48, Height = 32, Background = System.Windows.Media.Brushes.Red });
                    window.Show(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("add-content-show-grow", window, events));
                    Assert(content.ActualWidth > 0 && content.ActualHeight > 0, "Positive content did not receive layout.");
                    content.Children.Clear(); window.UpdateLayout(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("clear-visible-shrink", window, events));
                    window.Hide(); window.Show(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("empty-hide-show", window, events));
                    Assert(ReferenceEquals(source, PresentationSource.FromVisual(window)) && events.SourceInitialized == 1, "Hide/Show replaced the empty source.");
                    content.Children.Add(new System.Windows.Controls.Border { Width = 64, Height = 24 });
                    window.UpdateLayout(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("add-again-grow", window, events));
                    content.Children.Clear(); window.UpdateLayout(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("remove-again-shrink", window, events));
                    EmptyDrain(); snapshots.Add(CaptureEmpty("second-idle-observation", window, events));
                });
            }
            foreach (Size requested in new[] { new Size(0, 32), new Size(48, 0), new Size(0, 0), new Size(0.5, 32), new Size(48, 0.5), new Size(0.5, 0.5), new Size(1, 1) })
            {
                EmptyCase("manual-" + requested, (window, content, events, snapshots) =>
                {
                    window.SizeToContent = SizeToContent.Manual;
                    window.Width = requested.Width; window.Height = requested.Height;
                    snapshots.Add(CaptureEmpty("manual-request-before-show", window, events));
                    window.Show(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("manual-initial", window, events));
                    window.Width = 48; window.Height = 32; EmptyDrain();
                    snapshots.Add(CaptureEmpty("manual-grow", window, events));
                    window.Width = requested.Width; window.Height = requested.Height; EmptyDrain();
                    snapshots.Add(CaptureEmpty("manual-return", window, events));
                    window.Hide(); window.Show(); EmptyDrain();
                    snapshots.Add(CaptureEmpty("manual-hide-show", window, events));
                });
            }
            EmptyCase("finite-zero-subpixel-and-unbounded-constraints", (window, content, events, snapshots) =>
            {
                content.Children.Add(new System.Windows.Controls.Border { Width = 48, Height = 32 });
                window.MaxWidth = 0; window.MaxHeight = 0;
                window.Show(); EmptyDrain(); snapshots.Add(CaptureEmpty("finite-zero-maxima", window, events));
                window.MaxWidth = double.PositiveInfinity; window.MaxHeight = double.PositiveInfinity;
                window.UpdateLayout(); EmptyDrain(); snapshots.Add(CaptureEmpty("unbounded-maxima-grow", window, events));
                window.MaxWidth = 0.5; window.MaxHeight = 0.5;
                window.UpdateLayout(); EmptyDrain(); snapshots.Add(CaptureEmpty("finite-subpixel-maxima-shrink", window, events));
                window.MinWidth = 2; window.MinHeight = 3;
                window.UpdateLayout(); EmptyDrain(); snapshots.Add(CaptureEmpty("positive-minima-over-maxima", window, events));
                window.ClearValue(Window.MinWidthProperty); window.ClearValue(Window.MinHeightProperty);
                window.UpdateLayout(); EmptyDrain(); snapshots.Add(CaptureEmpty("clear-minima-shrink", window, events));
                window.ClearValue(Window.MaxWidthProperty); window.ClearValue(Window.MaxHeightProperty);
                window.UpdateLayout(); EmptyDrain(); snapshots.Add(CaptureEmpty("clear-maxima-grow", window, events));
                content.Children.Clear(); window.UpdateLayout(); EmptyDrain();
                snapshots.Add(CaptureEmpty("clear-content-return-empty", window, events));
            });
        }
        catch (Exception exception) { failure = exception.ToString(); }
        IntPtr foregroundAfter = GetForegroundWindow();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "stock-empty-window-sizing", mode = "--empty-sizing", os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription, assembly = typeof(Window).Assembly.Location,
            assemblyVersion = typeof(Window).Assembly.GetName().Version?.ToString(),
            physicalKeyboard = false, nativeInput = false, defaultOracleExecuted = false,
            foregroundBefore = foregroundBefore.ToString("X"), foregroundAfter = foregroundAfter.ToString("X"),
            foregroundUnchanged = foregroundBefore == foregroundAfter, failed = failure != null, error = failure, checks
        }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        return failure == null ? 0 : 1;

        void EmptyCase(string name, Action<Window, System.Windows.Controls.Grid, EmptyEvents, List<object>> action)
        {
            var window = new Window { Title = "WPF stock empty sizing " + name, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000,
                SizeToContent = SizeToContent.WidthAndHeight };
            var content = new System.Windows.Controls.Grid(); window.Content = content;
            var events = new EmptyEvents(window);
            var snapshots = new List<object>();
            Exception? error = null;
            bool sourceDetached = false, nativeDestroyed = false;
            try { action(window, content, events, snapshots); Assert(!window.IsActive, "A nonactivating calibration window became active."); }
            catch (Exception exception) { error = exception; }
            finally
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                try
                {
                    window.Close(); EmptyDrain();
                    sourceDetached = PresentationSource.FromVisual(window) == null;
                    nativeDestroyed = handle == IntPtr.Zero || !IsWindow(handle);
                    Assert(events.Closed == 1 && sourceDetached && nativeDestroyed && !window.IsVisible, "Window/source/native cleanup did not complete once.");
                }
                catch (Exception exception) { error = error == null ? exception : new AggregateException(error, exception); }
                finally { events.Detach(); }
            }
            checks.Add(new { name, status = error == null ? "pass" : "fail", detail = error?.ToString(), snapshots,
                cleanup = new { events.Closed, sourceDetached, nativeDestroyed, counts = events.Counts() } });
            if (error != null) throw new InvalidOperationException("Empty sizing case failed: " + name, error);
        }
    }

    private static object CaptureEmpty(string stage, Window window, EmptyEvents events)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        object? native = null;
        if (handle != IntPtr.Zero)
        {
            if (!GetClientRect(handle, out RECT client) || !GetWindowRect(handle, out RECT outer)) throw new Win32Exception();
            native = new { handle = handle.ToString("X"), client = new { client.Left, client.Top, client.Right, client.Bottom,
                width = client.Right - client.Left, height = client.Bottom - client.Top },
                outer = new { outer.Left, outer.Top, outer.Right, outer.Bottom, width = outer.Right - outer.Left, height = outer.Bottom - outer.Top },
                visible = IsWindowVisible(handle) };
        }
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var source = PresentationSource.FromVisual(window);
        return new { stage, window.Width, window.Height, window.ActualWidth, window.ActualHeight,
            desired = new { window.DesiredSize.Width, window.DesiredSize.Height },
            render = new { window.RenderSize.Width, window.RenderSize.Height },
            window.MinWidth, window.MinHeight, window.MaxWidth, window.MaxHeight,
            sizeToContent = window.SizeToContent.ToString(), window.IsVisible, window.IsActive,
            restoreIsEmpty = window.RestoreBounds.IsEmpty, restore = RectData(window.RestoreBounds),
            dpi = new { dpi.DpiScaleX, dpi.DpiScaleY, dpi.PixelsPerInchX, dpi.PixelsPerInchY },
            sourceIdentity = source == null ? (int?)null : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source),
            native, counts = events.Counts() };
    }

    private static void EmptyDrain()
    {
        var frame = new DispatcherFrame();
        bool timedOut = false;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var idle = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        var deadline = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Send,
            (_, _) => { timedOut = true; frame.Continue = false; }, dispatcher);
        try { deadline.Start(); Dispatcher.PushFrame(frame); }
        finally { deadline.Stop(); if (idle.Status == DispatcherOperationStatus.Pending) idle.Abort(); }
        Assert(!timedOut, "Empty sizing dispatcher did not reach idle within three seconds.");
    }

    private sealed class EmptyEvents
    {
        internal int SourceInitialized, SizeChanged, LayoutUpdated, Loaded, Unloaded, ContentRendered, Closed, NativeSize, NativeWindowPosition;
        private HwndSource? source;
        internal EmptyEvents(Window window)
        {
            window.SourceInitialized += (_, _) => { SourceInitialized++; source = (HwndSource?)PresentationSource.FromVisual(window); source?.AddHook(Observe); };
            window.SizeChanged += (_, _) => SizeChanged++;
            window.LayoutUpdated += (_, _) => LayoutUpdated++;
            window.Loaded += (_, _) => Loaded++;
            window.Unloaded += (_, _) => Unloaded++;
            window.ContentRendered += (_, _) => ContentRendered++;
            window.Closed += (_, _) => Closed++;
        }
        private IntPtr Observe(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0005) NativeSize++;
            if (message == 0x0047) NativeWindowPosition++;
            return IntPtr.Zero;
        }
        internal object Counts() => new { SourceInitialized, SizeChanged, LayoutUpdated, Loaded, Unloaded, ContentRendered, Closed, NativeSize, NativeWindowPosition };
        internal void Detach() { if (source is { IsDisposed: false }) source.RemoveHook(Observe); }
    }
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    private static bool Near(double left, double right) => Math.Abs(left - right) < 2;
    private static void Assert(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static object Capture(string stage, Window window) => new { stage, state = window.WindowState.ToString(), window.Left, window.Top, window.Width, window.Height, restore = RectData(window.RestoreBounds), displayed = RectData(NativeBounds(window)) };
    private static object RectData(Rect rect) => new { rect.X, rect.Y, rect.Width, rect.Height };
    private static Rect NativeBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out RECT rect)) throw new Win32Exception();
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    private static Size FrameSize(Window window)
    {
        if (!GetClientRect(new WindowInteropHelper(window).Handle, out RECT client)) throw new Win32Exception();
        Rect outer = NativeBounds(window);
        return new Size(outer.Width - (client.Right - client.Left), outer.Height - (client.Bottom - client.Top));
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { internal int Left, Top, Right, Bottom; }
}

internal sealed class SizingElement : FrameworkElement
{
    private Size _desired = new(160, 120);
    internal void SetDesired(Size value) { _desired = value; InvalidateMeasure(); }
    protected override Size MeasureOverride(Size constraint) => _desired;
}
