extern alias WpfFork;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Media.Imaging;
using WpfFork::System.Windows.Threading;
using Point = WpfFork::System.Windows.Point;
using Rect = WpfFork::System.Windows.Rect;
using Visual = WpfFork::System.Windows.Media.Visual;
using Application = Avalonia.Application;

var checks = new ConcurrentQueue<Check>();
bool completed = false;
Dispatcher? dispatcher = null;
string settings = args.FirstOrDefault(value => value.StartsWith("--settings=", StringComparison.Ordinal))?[11..] ?? "little";
string? fontPath = args.FirstOrDefault(value => value.StartsWith("--font=", StringComparison.Ordinal))?[7..];
string? settingsOrigin = null;
string? settingsPayloadPath = args.FirstOrDefault(value => value.StartsWith("--settings-payload=", StringComparison.Ordinal))?[19..];
object? resolvedSettings = null;
bool settingsOnly = args.Contains("--settings-only", StringComparer.Ordinal);
bool wmOnly = args.Contains("--wm-only", StringComparer.Ordinal);
bool traceHostMetrics = args.Contains("--trace-host-metrics", StringComparer.Ordinal);
var windowManagerTrace = new ConcurrentQueue<object>();
var guidelineCapture = new ConcurrentQueue<object>();
var frameTransitions = new ConcurrentQueue<object>();
var clickThresholds = new ConcurrentQueue<object>();
using var watchdog = new Timer(_ =>
{
    checks.Enqueue(new("watchdog", "fail", "The genuine native Window fixture did not finish within 40 seconds."));
    WriteResults();
    Environment.Exit(2);
}, null, TimeSpan.FromSeconds(40), Timeout.InfiniteTimeSpan);
try
{
    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture requires a native Linux X11 server.");
    byte[]? settingsPayload = settingsPayloadPath is null ? null : File.ReadAllBytes(settingsPayloadPath);
    if (settings == "captured-gnome")
        Require(settingsPayload is { Length: 2044 } && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(settingsPayload)) == "B8D5495AD53FA16EC0E96CBB5C9D8502E4DE59DFDB2BED357307FE9C6C412F57",
            "The captured GNOME payload is not the verified unchanged 2044-byte observation.");
    using var native = new X11Fixture(settings, settingsPayload);
    settingsOrigin = native.SettingsOrigin;
    bool negative = settings is not ("little" or "big" or "captured-gnome" or "missing-distance-little" or "missing-distance-big" or "published-little" or "published-big" or "published-min" or "published-max" or "name-valid-little" or "name-valid-big");
    try { NativeWindowHost.InitializeForCurrentThread(AppBuilder.Configure<Application>()); }
    catch (Exception exception) when (negative && exception.Message.Contains("XSETTINGS", StringComparison.Ordinal) &&
        (exception is PlatformNotSupportedException || exception is InvalidDataException || exception is InvalidOperationException))
    {
        checks.Enqueue(new("native-settings-negative-" + settings, "pass", exception.Message));
        completed = true;
    }
    if (!completed)
    {
        Require(!negative, "Invalid native settings were accepted.");
        Run("native-settings-resolution-and-provenance", VerifyPointerPreferences);
        if (settingsOnly) { completed = true; }
        else
        {
        dispatcher = Dispatcher.CurrentDispatcher;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            try
            {
                if (args.Contains("--click-thresholds-only", StringComparer.Ordinal))
                {
                    Run("resolved-settings-controlled-report-click-thresholds", () => VerifyClickThresholds(native));
                    completed = true;
                }
                else Exercise(native);
            }
            catch (Exception exception) { checks.Enqueue(new("unhandled-native-window-fixture", "fail", exception.ToString())); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
        }));
        Dispatcher.Run();
        }
    }
}
catch (Exception exception) { checks.Enqueue(new("native-window-initialization-or-loop", "fail", exception.ToString())); }
checks.Enqueue(new("all-checks-completed", completed ? "pass" : "fail", completed ? "All requested checks returned." : "The fixture stopped before all checks completed."));
WriteResults();
return checks.All(check => check.status == "pass") ? 0 : 1;

void VerifyPointerPreferences()
{
    bool fallback = settings is "captured-gnome" or "missing-distance-little" or "missing-distance-big" or "name-valid-little" or "name-valid-big";
    int distance = settings == "published-min" ? 1 : settings == "published-max" ? int.MaxValue : settings.StartsWith("published", StringComparison.Ordinal) ? 11 : 5;
    int time = settings == "published-min" ? 1 : fallback || settings == "published-max" ? 400 : settings.StartsWith("published", StringComparison.Ordinal) ? 725 : 450;
    var property = typeof(NativeWindowHost).GetProperty("PointerPreferences", BindingFlags.Static | BindingFlags.NonPublic)!;
    object resolved = property.GetValue(null)!;
    object Read(string name) => resolved.GetType().GetProperty(name)!.GetValue(resolved)!;
    Require((int)Read("Distance") == distance && (int)Read("Time") == time, "Startup changed a published preference or failed to apply the approved absent-distance fallback.");
    Require((string)Read("Origin") == settingsOrigin && Read("DistanceSource").ToString() == (fallback ? "DeclaredFallback" : "XSettings") && Read("TimeSource").ToString() == "XSettings",
        "Per-field preference provenance or actual selection/property origin was discarded.");
    var core = typeof(Mouse).Assembly;
    object profile = core.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider")!.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    object pointer = profile.GetType().GetProperty("PointerSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profile)!;
    object Pointer(string name) => pointer.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pointer)!;
    Require((int)Pointer("DoubleClickWidth") == distance && (int)Pointer("DoubleClickHeight") == distance && (int)Pointer("DoubleClickTimeMilliseconds") == time,
        "Resolved settings did not reach the existing immutable WPF pointer snapshot in physical pixels/milliseconds.");
    resolvedSettings = new { distance, time, origin = Read("Origin"), distanceSource = Read("DistanceSource").ToString(), timeSource = Read("TimeSource").ToString() };
}

void VerifyClickThresholds(X11Fixture native)
{
    Require(settings is "captured-gnome" or "missing-distance-little" or "missing-distance-big", "Threshold cases require the resolved 5-pixel/400ms profile.");
    var core = typeof(Mouse).Assembly;
    Type reportType = core.GetType("System.Windows.Input.RawMouseInputReport")!;
    Type actionsType = core.GetType("System.Windows.Input.RawMouseActions")!;
    object mode = Enum.Parse(core.GetType("System.Windows.Input.InputMode")!, "Foreground");
    int ordinal = 0;
    try
    {
        foreach (int dpi in new[] { 96, 192 })
        {
            native.SetDpi(dpi); Drain();
            var window = new Window { Title = "Resolved pointer thresholds " + dpi, Width = 180, Height = 120,
                ShowActivated = false, Content = new Border { Background = Brushes.Red } };
            try
            {
                window.Show();
                Wait(() => PresentationSource.FromVisual(window) != null && VisualTreeHelper.GetDpi(window).DpiScaleX == dpi / 96d);
                var xid = native.FindWindow(window.Title);
                Wait(() => native.IsMapped(xid));
                native.Click(xid, 10, 10); Drain();
                var source = PresentationSource.FromVisual(window)!;
                object provider = typeof(PresentationSource).GetMethod("GetInputProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, [typeof(MouseDevice)])!;
                var report = provider.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(method => method.Name == "ReportInput" && method.GetParameters().Length == 1);
                var counts = new List<int>();
                window.PreviewMouseDown += (_, e) => counts.Add(e.ClickCount);
                foreach (var (dx, dy, delay, expected) in new[]
                {
                    (4, 0, 1, 2), (5, 0, 1, 1), (-4, 0, 1, 2), (-5, 0, 1, 1),
                    (0, 4, 1, 2), (0, 5, 1, 1), (0, -4, 1, 2), (0, -5, 1, 1),
                    (0, 0, 399, 2), (0, 0, 400, 1)
                })
                {
                    counts.Clear();
                    int first = unchecked(Environment.TickCount + 10000 + ordinal++ * 2000);
                    Click(first, 80, 80);
                    Click(unchecked(first + delay), 80 + dx, 80 + dy);
                    Require(counts.SequenceEqual(new[] { 1, expected }), $"Physical delta ({dx},{dy}), timestamp delta {delay}ms at {dpi} DPI produced [{string.Join(',', counts)}].");
                    clickThresholds.Enqueue(new { dpi, physicalDeltaX = dx, physicalDeltaY = dy, timestampDeltaMilliseconds = delay, clickCount = counts[1], delivery = "controlled raw reports through actual source provider, not OS timing" });
                }
                void Click(int timestamp, int x, int y)
                {
                    Send("AbsoluteMove, Button1Press");
                    Send("Button1Release");
                    void Send(string actions) => report.Invoke(provider, [Activator.CreateInstance(reportType,
                        [mode, timestamp, source, Enum.Parse(actionsType, actions), x, y, 0, IntPtr.Zero])!]);
                }
            }
            finally { window.Close(); }
        }
    }
    finally { native.SetDpi(96); Drain(); }
}

void Exercise(X11Fixture native)
{
    Require(fontPath is not null && File.Exists(fontPath), "Pass --font=<verified pinned DejaVuSans.ttf>.");
    var family = new FontFamily(new Uri(Path.GetFullPath(fontPath!)), "DejaVu Sans");
    Run("genuine-window-independent-native-frame", () =>
    {
        var surfaceWindow = new Window { Title = "Genuine WPF surface fixture", Width = 180, Height = 100,
            ShowActivated = false, Background = Brushes.Red };
        int closed = 0;
        surfaceWindow.Closed += (_, _) => closed++;
        try
        {
            surfaceWindow.Show();
            var surfaceXid = native.FindWindow(surfaceWindow.Title);
            Wait(() => native.IsMapped(surfaceXid) && native.Pixel(surfaceXid, 5, 5) == 0x00ff0000);
            Require(surfaceXid != IntPtr.Zero && native.Pixel(surfaceXid, 5, 5) == 0x00ff0000,
                "The genuine Window frame did not reach native X11 pixels.");
            Run("native-frame-transition-overlap-and-buffer-rejection", () =>
            {
                var source = PresentationSource.FromVisual(surfaceWindow)!;
                object peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
                var present = peer.GetType().GetMethod("Present")!.CreateDelegate<PresentFrame>(peer);
                var targetSize = native.Size(surfaceXid);
                byte[] small = BgraFrame(20, 20, 0, 255, 0);
                present(small, 20, 20, 80);
                frameTransitions.Enqueue(new { frameWidth = 20, frameHeight = 20, targetSize.Width, targetSize.Height, uncovered = "clear" });
                Require(native.Pixel(surfaceXid, 5, 5) == 0x0000ff00 && native.Pixel(surfaceXid, 40, 40) == 0,
                    "A smaller valid previous frame did not clear the newly exposed native viewport.");
                byte[] large = BgraFrame(400, 300, 255, 0, 0);
                present(large, 400, 300, 1600);
                frameTransitions.Enqueue(new { frameWidth = 400, frameHeight = 300, targetSize.Width, targetSize.Height, uncovered = "clip" });
                var originalClient = native.Size(surfaceXid);
                Require(native.Pixel(surfaceXid, 5, 5) == 0x000000ff &&
                    native.Pixel(surfaceXid, originalClient.Width - 5, originalClient.Height - 5) == 0x000000ff,
                    "A larger valid previous frame did not clip to the smaller native viewport.");
                bool narrowRejected = false, shortRejected = false;
                try { present(small, 20, 20, 79); } catch (ArgumentException) { narrowRejected = true; }
                try { present(small.AsSpan(0, small.Length - 1), 20, 20, 80); } catch (ArgumentException) { shortRejected = true; }
                Require(narrowRejected && shortRejected, "Malformed frame stride or short borrowed buffer was accepted.");
                surfaceWindow.Width = 220; surfaceWindow.Height = 140;
                Wait(() => native.Size(surfaceXid).Width > originalClient.Width &&
                    native.Pixel(surfaceXid, 5, 5) == 0x00ff0000 && surfaceWindow.ActualWidth == 220);
                surfaceWindow.Width = 180; surfaceWindow.Height = 100;
                Wait(() => native.Size(surfaceXid) == originalClient &&
                    native.Pixel(surfaceXid, 5, 5) == 0x00ff0000 && surfaceWindow.ActualWidth == 180);
            });
            surfaceWindow.Background = Brushes.Blue;
            Wait(() => native.Pixel(surfaceXid, 5, 5) == 0x000000ff);
            Require(native.Pixel(surfaceXid, 5, 5) == 0x000000ff, "The retained Window frame did not update native pixels.");
        }
        finally { surfaceWindow.Close(); }
        Require(closed == 1 && PresentationSource.FromVisual(surfaceWindow) is null,
            "The native surface Window did not acknowledge close and detach once.");
    });
    Run("native-writeable-bitmap-precision-retained-mutation-and-lifetime", () =>
    {
        for (int lifetime = 0; lifetime < 2; lifetime++)
        {
            var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Rgb48, null);
            var imageWindow = new Window { Title = "Genuine WPF scientific image " + lifetime,
                Width = 180, Height = 100, ShowActivated = false, Content = new ImageScene(bitmap) };
            try
            {
                WriteSamples(65535, 257, 17);
                imageWindow.Show(); var imageXid = native.FindWindow(imageWindow.Title);
                Wait(() => native.IsMapped(imageXid) && native.Pixel(imageXid, 10, 10) == 0x00ff0100);
                WriteSamples(17, 257, 65535);
                Wait(() => native.Pixel(imageXid, 10, 10) == 0x000001ff);
                imageWindow.Width = 240; imageWindow.Height = 160;
                Wait(() => imageWindow.ActualWidth == 240 && native.Pixel(imageXid, 10, 10) == 0x000001ff);
                native.Expose(imageXid); Drain();
                Require(native.Pixel(imageXid, 10, 10) == 0x000001ff, "Native expose lost the retained scientific image.");
                WriteSamples(65535, 257, 17);
                Wait(() => native.Pixel(imageXid, 10, 10) == 0x00ff0100);
            }
            finally { imageWindow.Close(); }
            Require(PresentationSource.FromVisual(imageWindow) is null, "Repeated image Window lifetime retained its source.");
            void WriteSamples(ushort red, ushort green, ushort blue)
            {
                ushort[] samples = [red, green, blue];
                bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), samples, 6, 0);
                Array.Clear(samples);
                ushort[] copied = new ushort[3]; bitmap.CopyPixels(copied, 6, 0);
                Require(copied.SequenceEqual(new ushort[] { red, green, blue }), "Native presentation discarded 16-bit source precision or retained caller storage.");
            }
        }
    });
    if (wmOnly)
    {
        ExerciseWindowManager(native);
        completed = true;
        return;
    }
    Run("native-explicit-bounds-stale-cache-server-equal-and-unchanged-request", () =>
    {
        var sizeWindow = new Window { Title = "Genuine WPF explicit size request", Width = 180, Height = 100,
            ShowActivated = false, Background = Brushes.Red };
        try
        {
            sizeWindow.Show(); var xid = native.FindWindow(sizeWindow.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000); Drain();
            var source = PresentationSource.FromVisual(sizeWindow)!;
            object peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var setSize = peer.GetType().GetMethod("SetClientSize")!.CreateDelegate<Action<WpfFork::System.Windows.Size>>(peer);
            var oldPublished = (WpfFork::System.Windows.Size)peer.GetType().GetProperty("ClientSize")!.GetValue(peer)!;
            native.Resize(xid, 230, 150);
            Require(native.Size(xid) == (230u, 150u) && (WpfFork::System.Windows.Size)peer.GetType().GetProperty("ClientSize")!.GetValue(peer)! == oldPublished,
                "The server-already-equal fixture pumped or failed to change the real native viewport.");
            setSize(new WpfFork::System.Windows.Size(230, 150));
            Wait(() => sizeWindow.ActualWidth == 230 && sizeWindow.ActualHeight == 150 && native.Pixel(xid, 225, 145) == 0x00ff0000);
            setSize(new WpfFork::System.Windows.Size(230, 150)); Drain();
            Require(native.Size(xid) == (230u, 150u) && sizeWindow.ActualWidth == 230 && sizeWindow.ActualHeight == 150 &&
                native.Pixel(xid, 225, 145) == 0x00ff0000, "An unchanged explicit size request lost actual metrics or pixels.");
            native.Resize(xid, 270, 170);
            Require(native.Size(xid) == (270u, 170u), "The stale-cache fixture did not change actual native geometry.");
            setSize(new WpfFork::System.Windows.Size(230, 150));
            Wait(() => native.Size(xid) == (230u, 150u) && sizeWindow.ActualWidth == 230 && sizeWindow.ActualHeight == 150 &&
                native.Pixel(xid, 225, 145) == 0x00ff0000);
            var setPosition = peer.GetType().GetMethod("SetDesktopPosition")!.CreateDelegate<Action<Point>>(peer);
            var oldPosition = (Point)peer.GetType().GetProperty("DesktopPosition")!.GetValue(peer)!;
            native.Move(xid, 71, 83);
            Require(native.ClientOrigin(xid) == (71, 83) && (Point)peer.GetType().GetProperty("DesktopPosition")!.GetValue(peer)! == oldPosition,
                "The server-already-equal position fixture pumped or failed to move the native window.");
            setPosition(new Point(71, 83));
            Wait(() => sizeWindow.Left == 71 && sizeWindow.Top == 83);
            setPosition(new Point(71, 83)); Drain();
            Require(native.ClientOrigin(xid) == (71, 83) && sizeWindow.Left == 71 && sizeWindow.Top == 83,
                "An unchanged explicit position request lost native metrics.");
            native.Move(xid, 123, 135); setPosition(new Point(71, 83));
            Wait(() => native.ClientOrigin(xid) == (71, 83) && sizeWindow.Left == 71 && sizeWindow.Top == 83);
        }
        finally { sizeWindow.Close(); }
    });
    Run("native-zero-subpixel-diagnostics-minimum-pixel-and-recovery", () =>
    {
        foreach (var invalid in new (double Width, double Height)[] { (0, 100), (180, 0), (0, 0), (0.5, 100), (180, 0.5), (0.5, 0.5) })
        {
            var shown = new Window { Title = "Genuine WPF invalid shown extent " + invalid, Width = 180, Height = 100,
                ShowActivated = false, Background = Brushes.Red };
            try
            {
                shown.Show(); var xid = native.FindWindow(shown.Title);
                Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000);
                ExpectUnsupported(() => { shown.Width = invalid.Width; shown.Height = invalid.Height; });
                shown.Width = 180; shown.Height = 100;
                Wait(() => native.Size(xid) == (180u, 100u) && native.Pixel(xid, 175, 95) == 0x00ff0000); Drain();
                Require(shown.ActualWidth == 180 && shown.ActualHeight == 100, "The shown Window did not recover supported dimensions.");
            }
            finally { shown.Close(); }
            var initial = new Window { Title = "Genuine WPF invalid initial extent " + invalid, Width = invalid.Width,
                Height = invalid.Height, ShowActivated = false, Background = Brushes.Red };
            try { ExpectUnsupported(initial.Show); }
            finally { initial.Close(); }
            var unrelated = new Window { Title = "Genuine WPF frame after rejected extent " + invalid, Width = 180, Height = 100,
                ShowActivated = false, Background = Brushes.Blue };
            try
            {
                unrelated.Show(); var xid = native.FindWindow(unrelated.Title);
                Wait(() => native.IsMapped(xid) && native.Pixel(xid, 175, 95) == 0x000000ff);
            }
            finally { unrelated.Close(); }
        }
        var minimum = new Window { Title = "Genuine WPF minimum native pixel", Width = 1, Height = 1,
            ShowActivated = false, Background = Brushes.Red };
        try
        {
            minimum.Show(); var xid = native.FindWindow(minimum.Title);
            Wait(() => native.Size(xid) == (1u, 1u) && native.Pixel(xid, 0, 0) == 0x00ff0000);
            minimum.Width = 180; minimum.Height = 100;
            Wait(() => native.Size(xid) == (180u, 100u) && native.Pixel(xid, 175, 95) == 0x00ff0000);
            minimum.Width = 1; minimum.Height = 1;
            Wait(() => native.Size(xid) == (1u, 1u) && native.Pixel(xid, 0, 0) == 0x00ff0000);
        }
        finally { minimum.Close(); }
        static void ExpectUnsupported(Action action)
        {
            try { action(); }
            catch (PlatformNotSupportedException exception) when (exception.Message.Contains("at least one native client pixel", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("A zero/subpixel native extent did not receive the named managed profile diagnostic.");
        }
    });
    Run("native-minimum-pixel-dpi-roundtrips-and-unrelated-frame", () =>
    {
        try
        {
            foreach (var size in new (double Width, double Height)[] { (1, 1), (1, 120), (180, 1) })
            foreach (int startDpi in new[] { 96, 144 })
            {
                native.SetDpi(startDpi); Drain();
                var minimum = new Window { Title = "Genuine WPF minimum DPI " + size + startDpi, Width = size.Width, Height = size.Height,
                    ShowActivated = false, Background = Brushes.Red };
                try
                {
                    minimum.Show(); var xid = native.FindWindow(minimum.Title);
                    Wait(() => native.IsMapped(xid) && native.Pixel(xid, 0, 0) == 0x00ff0000);
                    for (int cycle = 0; cycle < 2; cycle++)
                    foreach (int dpi in new[] { startDpi == 96 ? 144 : 96, startDpi })
                    {
                        native.SetDpi(dpi);
                        Wait(() => VisualTreeHelper.GetDpi(minimum).DpiScaleX == dpi / 96d &&
                            native.Size(xid).Width >= 1 && native.Size(xid).Height >= 1 && native.Pixel(xid, 0, 0) == 0x00ff0000);
                        Drain();
                        var actual = native.Size(xid);
                        Require((size.Width != 1 || actual.Width == 1) && (size.Height != 1 || actual.Height == 1),
                            "A minimum-pixel DPI transition changed the actual native minimum extent.");
                        frameTransitions.Enqueue(new { kind = "minimum-pixel-dpi", requestedWidth = size.Width, requestedHeight = size.Height,
                            startDpi, dpi, cycle, physicalWidth = actual.Width, physicalHeight = actual.Height,
                            minimum.ActualWidth, minimum.ActualHeight });
                    }
                }
                finally { minimum.Close(); }
                var next = new Window { Title = "Genuine WPF independent DPI frame " + size + startDpi, Width = 180, Height = 100,
                    ShowActivated = false, Background = Brushes.Blue };
                try
                {
                    next.Show(); var xid = native.FindWindow(next.Title);
                    Wait(() => native.IsMapped(xid) && native.Pixel(xid, 10, 10) == 0x000000ff);
                }
                finally { next.Close(); }
            }
        }
        finally { native.SetDpi(96); Drain(); }
    });
    var button = new Button { OverridesDefaultStyle = true, Content = "Go", Width = 180, Height = 60, Background = Brushes.Green, FontFamily = family, Template = ButtonTemplate() };
    var stack = new StackPanel(); stack.Children.Add(button);
    var window = new Window { Title = "Genuine WPF native fixture", Width = 360, Height = 240, Content = stack,
        Background = Brushes.Red, FontFamily = family };
    int sourceCount = 0, rendered = 0, clicked = 0, pressed = 0, released = 0, keyDown = 0, keyUp = 0;
    IntPtr xid = IntPtr.Zero;
    window.SourceInitialized += (_, _) => sourceCount++;
    window.ContentRendered += (_, _) => rendered++;
    button.Click += (_, _) => { Require(dispatcher!.CheckAccess(), "Button.Click lost WPF owner affinity."); clicked++; };
    button.PreviewMouseLeftButtonDown += (_, _) => pressed++;
    button.PreviewMouseLeftButtonUp += (_, _) => released++;
    Run("genuine-window-source-layout-frame", () =>
    {
        window.Show();
        xid = native.FindWindow(window.Title);
        Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 220) == 0x00ff0000);
        Require(sourceCount == 1 && PresentationSource.FromVisual(window) is { IsDisposed: false } &&
            window.ActualWidth > 0 && button.ActualHeight == 60 && rendered > 0, "Genuine Window/source/layout did not attach.");
        Require(window.GetType().Assembly.GetName().Name == "PresentationFramework" &&
            button.GetType().Assembly == window.GetType().Assembly, "Fixture did not use genuine WPF Window and Button.");
    });
    Run("native-window-pixels-and-retained-invalidation", () =>
    {
        Require(xid != IntPtr.Zero, "No real X11 window matches the genuine WPF title.");
        Require(native.Pixel(xid, 5, 220) == 0x00ff0000, "The native window did not contain the genuine WPF red background.");
        button.Background = Brushes.Blue;
        Wait(() => native.Pixel(xid, 100, 20) == 0x000000ff);
        Require(native.Pixel(xid, 100, 20) == 0x000000ff, "WPF retained invalidation did not reach native framebuffer pixels.");
        native.Expose(xid); Wait(() => native.Pixel(xid, 100, 20) == 0x000000ff);
        Require(native.Pixel(xid, 100, 20) == 0x000000ff, "Native expose did not repaint the retained WPF content.");
    });
    Run("native-programmatic-capture-and-disable-release", () =>
    {
        Require(button.CaptureMouse() && ReferenceEquals(Mouse.Captured, button),
            "Genuine WPF programmatic capture did not obtain an actual native grab.");
        window.IsEnabled = false;
        Require(Mouse.Captured is null && Keyboard.FocusedElement is null && Mouse.LeftButton == MouseButtonState.Released,
            "Native disable retained WPF capture, focus or pressed mouse state.");
        window.IsEnabled = true;
        Require(button.CaptureMouse() && ReferenceEquals(Mouse.Captured, button), "Reenabled native capture did not recover.");
        button.ReleaseMouseCapture();
        Require(Mouse.Captured is null, "Native capture release retained WPF capture.");
    });
    Run("native-real-cover-retained-update-and-uncover", () =>
    {
        using (native.Cover(xid))
        {
            Wait(() => native.ScreenPixel(xid, 100, 20) == 0);
            button.Background = Brushes.Lime; Drain();
            Require(native.ScreenPixel(xid, 100, 20) == 0, "The covering native window did not occlude the retained mutation.");
        }
        Wait(() => native.Pixel(xid, 100, 20) == 0x0000ff00 && native.ScreenPixel(xid, 100, 20) == 0x0000ff00);
        button.Background = Brushes.Blue;
        Wait(() => native.Pixel(xid, 100, 20) == 0x000000ff);
    });
    Run("native-pointer-button-press-release-click", () =>
    {
        native.Focus(xid);
        native.Click(xid, 100, 30);
        Wait(() => clicked == 1 && released == 1);
        Require(pressed == 1 && Mouse.LeftButton == MouseButtonState.Released && !button.IsMouseCaptured,
            "Native mouse press/release did not complete genuine Button.Click and capture release.");
    });
    Run("native-cursor-visibility-retains-latest-shape", () =>
    {
        object peer = PresentationSource.FromVisual(window)!.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(PresentationSource.FromVisual(window))!;
        var setShape = peer.GetType().GetMethod("SetCursor")!.CreateDelegate<Func<Cursor, bool>>(peer);
        var setVisibility = peer.GetType().GetMethod("SetCursorVisibility")!.CreateDelegate<Func<bool, bool>>(peer);
        Require(setShape(Cursors.IBeam), "Native IBeam cursor was rejected.");
        Drain();
        Wait(() => native.CursorImage().Visible);
        ulong beam = native.CursorImage().Fingerprint;
        Require(setShape(Cursors.Hand), "Native Hand cursor was rejected.");
        Wait(() => native.CursorImage().Visible && native.CursorImage().Fingerprint != beam);
        ulong hand = native.CursorImage().Fingerprint;
        Require(setVisibility(false), "Native cursor hiding was rejected.");
        Wait(() => !native.CursorImage().Visible);
        Require(setShape(Cursors.IBeam), "A valid native cursor shape was rejected while hidden.");
        Drain(); Require(!native.CursorImage().Visible, "A shape change made the hidden native cursor visible.");
        Require(setVisibility(true), "Native cursor restoration was rejected.");
        Wait(() => native.CursorImage().Visible);
        Require(native.CursorImage().Fingerprint == beam && beam != hand, "Native cursor restoration lost the latest IBeam shape.");
    });
    Run("native-hide-show-preserves-source-and-deactivates-input", () =>
    {
        var source = PresentationSource.FromVisual(window);
        window.Hide(); Wait(() => !native.IsMapped(xid));
        Require(!window.IsVisible && PresentationSource.FromVisual(window) == source && Mouse.Captured is null && Keyboard.FocusedElement is null,
            "Native Hide lost the source or retained active input/capture.");
        window.Show(); Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 220) == 0x00ff0000);
        Require(sourceCount == 1 && PresentationSource.FromVisual(window) == source,
            "Native Show created a replacement source after Hide.");
    });
    Run("native-keyboard-reactivation-after-disable-enable-activate", () =>
    {
        native.Focus(xid); Drain();
        window.IsEnabled = false; Drain();
        window.IsEnabled = true; Drain();
        Require(window.Activate(), "The already native-focused Window did not reactivate.");
        try { native.Key('b', true); Wait(() => Keyboard.IsKeyDown(Key.B)); }
        finally { native.Key('b', false); Drain(); }
        Require(Keyboard.IsKeyUp(Key.B), "Native reactivation retained a released key.");
    });
    Run("native-key-textbox-committed-text", () =>
    {
        var text = new TextBox { OverridesDefaultStyle = true, Width = 220, Height = 60, FontFamily = family, FontSize = 18, Background = Brushes.White, Template = TextTemplate() };
        text.PreviewKeyDown += (_, _) => keyDown++;
        text.PreviewKeyUp += (_, _) => keyUp++;
        stack.Children.Add(text);
        Wait(() => text.ActualHeight == 60);
        native.Focus(xid);
        native.Click(xid, 120, 85);
        Wait(() => text.IsKeyboardFocused);
        native.Type('a'); native.Type('b');
        Wait(() => text.Text == "ab" && keyUp >= 2);
        Require(keyDown >= 2 && Keyboard.IsKeyUp(Key.A) && Keyboard.IsKeyUp(Key.B),
            "Native committed text or real key release state did not reach TextBox.");
    });
    Run("native-resize-expose-uniform-dpi", () =>
    {
        try
        {
            window.Width = 420; window.Height = 280;
            Wait(() => native.HasSize(xid, 420, 280) && window.ActualWidth == 420 && window.ActualHeight == 280 &&
                native.Pixel(xid, 5, 260) == 0x00ff0000);
            var dpi = VisualTreeHelper.GetDpi(window);
            Require(dpi.DpiScaleX > 0 && dpi.DpiScaleX == dpi.DpiScaleY, "The native X11 uniform DPI was not propagated to genuine WPF.");
            Require(native.Pixel(xid, 5, 260) == 0x00ff0000, "Resized native framebuffer did not receive the WPF background.");
        }
        finally { CaptureGuidelines(window); }
    });
    Run("native-owner-modal-wpf-nested-frame", () =>
    {
        var modalButton = new Button { OverridesDefaultStyle = true, Content = "Close", Width = 180, Height = 60, Background = Brushes.Green, FontFamily = family, Template = ButtonTemplate() };
        var modal = new Window { Title = "Genuine WPF native dialog", Owner = window, Width = 260, Height = 160,
            Content = modalButton, FontFamily = family, Background = Brushes.Red };
        bool inside = false, buttonClick = false;
        modalButton.Click += (_, _) => { buttonClick = true; modal.DialogResult = true; };
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Normal, (_, _) =>
        {
            var modalXid = native.FindWindow(modal.Title);
            if (modalXid == IntPtr.Zero || modalButton.ActualWidth == 0) return;
            inside = true;
            Require(ReferenceEquals(modal.Owner, window) && window.OwnedWindows.Count == 1, "Actual Window owner was not retained inside the modal frame.");
            native.Focus(modalXid); native.Click(modalXid, 120, 50);
        }, dispatcher!);
        try { Require(modal.ShowDialog() == true && inside && buttonClick, "Real native Button did not close the synchronous WPF modal frame."); }
        finally { timer.Stop(); if (modal.IsVisible) modal.Close(); }
        Require(window.IsVisible && window.IsEnabled && window.OwnedWindows.Count == 0, "Modal exit did not restore the owner.");
    });
    Run("native-close-cancel-accepted-single-ack", () =>
    {
        bool cancel = true; int closing = 0, closed = 0;
        window.Closing += (_, args) => { closing++; args.Cancel = cancel; };
        window.Closed += (_, _) => closed++;
        native.RequestClose(xid); Wait(() => closing == 1);
        Require(window.IsVisible && closed == 0, "Canceled native close disposed the genuine Window.");
        cancel = false; native.RequestClose(xid); Wait(() => closed == 1);
        Require(closing == 2 && !window.IsVisible && PresentationSource.FromVisual(window) is null,
            "Accepted native close did not detach the source exactly once.");
        window.Close(); Require(closed == 1, "Repeated Window.Close duplicated the Closed acknowledgement.");
    });
    Run("native-close-from-lost-capture-during-hide-and-disable", () =>
    {
        foreach (bool hide in new[] { true, false })
        {
            var capturedWindow = new Window { Title = "Genuine WPF reentrant capture close " + hide,
                Width = 180, Height = 100, ShowActivated = false, Background = Brushes.Red };
            int closed = 0;
            capturedWindow.Closed += (_, _) => closed++;
            try
            {
                capturedWindow.Show(); var capturedXid = native.FindWindow(capturedWindow.Title);
                Wait(() => native.IsMapped(capturedXid));
                Require(capturedWindow.CaptureMouse(), "Reentrant close fixture did not acquire actual native capture.");
                capturedWindow.LostMouseCapture += (_, _) => capturedWindow.Close();
                if (hide) capturedWindow.Hide(); else capturedWindow.IsEnabled = false;
                Require(closed == 1 && PresentationSource.FromVisual(capturedWindow) is null && Mouse.Captured is null,
                    "Closing from LostMouseCapture did not finish Window/source cleanup exactly once.");
            }
            finally { capturedWindow.Close(); }
        }
    });
    Run("native-five-button-transitions-and-held-activation-snapshots", () => NativeInputChecks.FiveButtons(native, Wait, Drain));
    Run("native-unexpected-unmap-capture-loss-recovery-and-disposal", () => NativeInputChecks.UnexpectedCaptureLossAndDisposal(native, Wait, Drain));
    if (window.IsVisible) window.Close();
    completed = true;
}

void CaptureGuidelines(Visual root)
{
    Visit(root);
    void Visit(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var origin = new Point();
        var x = new Point(1, 0); var y = new Point(0, 1);
        if (!ReferenceEquals(root, visual))
        {
            var transform = visual.TransformToAncestor(root);
            origin = transform.Transform(origin); x = transform.Transform(x); y = transform.Transform(y);
        }
        var matrix = new[] { x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y };
        string owner = visual.GetType().FullName ?? visual.GetType().Name;
        var snapX = VisualTreeHelper.GetXSnappingGuidelines(visual)?.ToArray() ?? Array.Empty<double>();
        var snapY = VisualTreeHelper.GetYSnappingGuidelines(visual)?.ToArray() ?? Array.Empty<double>();
        if (snapX.Length != 0 || snapY.Length != 0)
            guidelineCapture.Enqueue(new { kind = "visual-snapping", owner, dpi = new[] { dpi.DpiScaleX, dpi.DpiScaleY }, matrix, x = snapX, y = snapY });
        if (VisualTreeHelper.GetDrawing(visual) is { } drawing) VisitDrawing(drawing, owner, matrix, dpi);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) Visit(child);
    }
    void VisitDrawing(Drawing drawing, string owner, double[] matrix, DpiScale dpi)
    {
        if (drawing is not DrawingGroup group) return;
        if (group.GuidelineSet is { } guidelines)
            guidelineCapture.Enqueue(new { kind = "drawing-guidelines", owner, dpi = new[] { dpi.DpiScaleX, dpi.DpiScaleY },
                matrix, drawingTransform = group.Transform?.Value.ToString(CultureInfo.InvariantCulture),
                x = guidelines.GuidelinesX?.ToArray(), y = guidelines.GuidelinesY?.ToArray(),
                isDynamic = typeof(GuidelineSet).GetProperty("IsDynamic", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(guidelines) });
        foreach (Drawing child in group.Children) VisitDrawing(child, owner, matrix, dpi);
    }
}

void ExerciseWindowManager(X11Fixture native)
{
    Require(native.HasWindowManager, "The native Window manager fixture requires a real EWMH window manager.");
    foreach (string notification in new[] { "state", "dpi" })
        Run("native-wm-direct-" + notification + "-close-and-unrelated-frame", () =>
        {
            var window = new Window { Title = "Genuine WPF direct " + notification + " close", Width = 300, Height = 180,
                Background = Brushes.Red };
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            try
            {
                window.Show(); var xid = native.FindWindow(window.Title);
                Wait(() => native.IsMapped(xid) && native.Pixel(xid, 10, 10) == 0x00ff0000);
                if (notification == "state")
                {
                    window.StateChanged += (_, _) => { if (window.WindowState == WindowState.Maximized) window.Close(); };
                    native.RequestMaximized(xid, true);
                }
                else
                {
                    window.DpiChanged += (_, _) => window.Close();
                    native.SetDpi(144);
                }
                Wait(() => closed); Drain();
                var next = new Window { Title = "Genuine WPF unrelated frame after " + notification, Width = 300, Height = 180,
                    Background = Brushes.Blue };
                try
                {
                    next.Show(); var nextXid = native.FindWindow(next.Title);
                    Wait(() => native.IsMapped(nextXid) && native.Pixel(nextXid, 10, 10) == 0x000000ff);
                }
                finally { next.Close(); }
            }
            finally
            {
                window.Close();
                if (notification == "dpi") { native.SetDpi(96); Drain(); }
            }
        });
    Run("native-wm-real-state-and-geometry-notifications", () =>
    {
        var window = new Window { Title = "Genuine WPF WM state fixture", Width = 320, Height = 200,
            Background = Brushes.Red };
        IntPtr xid = IntPtr.Zero;
        void Trace(string kind) => windowManagerTrace.Enqueue(new { kind, requested = window.WindowState.ToString(),
            actualNative = xid == IntPtr.Zero ? "uncreated" : native.State(xid), window.ActualWidth, window.ActualHeight });
        window.StateChanged += (_, _) => Trace("StateChanged");
        window.SizeChanged += (_, _) => Trace("SizeChanged");
        try
        {
            window.Show(); xid = native.FindWindow(window.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000 && window.ActualWidth > 0);
            var normalClient = native.Size(xid);
            native.RequestMaximized(xid, true);
            Wait(() => native.State(xid) == "Maximized" && window.WindowState == WindowState.Maximized && native.Size(xid).Width > normalClient.Width);
            Drain(); Trace("maximized-observed");
            window.Hide(); Wait(() => !native.IsMapped(xid)); Drain(); Trace("maximized-hidden-observed");
            Require(window.WindowState == WindowState.Maximized, "Native withdrawal changed the public maximized Window state.");
            window.Show(); Wait(() => native.IsMapped(xid) && native.State(xid) == "Maximized" && window.WindowState == WindowState.Maximized);
            Trace("maximized-after-hide-show-observed");
            window.Hide(); window.Show(); Drain();
            Require(native.State(xid) == "Maximized" && window.WindowState == WindowState.Maximized,
                "An immediate Hide/Show lost the observed maximized native state.");
            native.RequestMaximized(xid, false);
            Wait(() => native.State(xid) == "Normal" && window.WindowState == WindowState.Normal && native.Size(xid) == normalClient);
            native.Minimize(xid);
            Wait(() => native.State(xid) == "Minimized" && window.WindowState == WindowState.Minimized && !native.IsMapped(xid));
            Drain(); window.Hide(); Drain(); Trace("minimized-hidden-observed");
            Require(window.WindowState == WindowState.Minimized, "Native withdrawal changed the public minimized Window state.");
            window.Show();
            Wait(() => native.State(xid) == "Minimized" && window.WindowState == WindowState.Minimized && !native.IsMapped(xid));
            window.WindowState = WindowState.Normal;
            Wait(() => native.State(xid) == "Normal" && native.IsMapped(xid));
            window.WindowState = WindowState.Minimized;
            Wait(() => native.State(xid) == "Minimized" && !native.IsMapped(xid));
            window.WindowState = WindowState.Normal;
            Wait(() => native.State(xid) == "Normal" && native.IsMapped(xid));
            Drain(); Trace("normal-after-minimize-observed");
        }
        finally { window.Close(); }
    });
    Run("native-wm-rapid-state-intent-and-close-before-deferred-metrics", () =>
    {
        var window = new Window { Title = "Genuine WPF rapid state fixture", Width = 320, Height = 200, Background = Brushes.Red };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000);
            var original = native.Size(xid);
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Normal;
            Drain();
            Wait(() => native.State(xid) == "Normal" && window.WindowState == WindowState.Normal && native.Size(xid) == original);
            bool closeQueued = false;
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState != WindowState.Maximized) return;
                closeQueued = true;
                dispatcher!.BeginInvoke(DispatcherPriority.Send, new Action(window.Close));
            };
            window.WindowState = WindowState.Maximized;
            Wait(() => closeQueued && PresentationSource.FromVisual(window) is null);
            Drain();
        }
        finally { window.Close(); }
    });
    Run("native-wm-initial-minimized-and-maximized", () =>
    {
        foreach (var state in new[] { WindowState.Minimized, WindowState.Maximized })
        {
            var window = new Window { Title = "Genuine WPF initial state " + state, Width = 300, Height = 180,
                Background = Brushes.Red, WindowState = state };
            try
            {
                window.Show(); IntPtr xid = native.FindWindow(window.Title);
                Wait(() => native.State(xid) == state.ToString() &&
                    native.IsMapped(xid) == (state == WindowState.Maximized));
                window.WindowState = WindowState.Normal;
                try { Wait(() => native.State(xid) == "Normal" && native.IsMapped(xid)); }
                finally { windowManagerTrace.Enqueue(new { kind = "initial-state-restore-result", initial = state.ToString(),
                    requested = window.WindowState.ToString(), actualNative = native.State(xid), mapped = native.IsMapped(xid), window.ActualWidth, window.ActualHeight }); }
            }
            finally { window.Close(); }
        }
    });
    Run("native-wm-explicit-restore-position-equals-maximized-origin", () =>
    {
        var window = new Window { Title = "Genuine WPF restore position intent", Width = 320, Height = 200,
            Left = 180, Top = 140, Background = Brushes.Red };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000); Drain();
            object peer = PresentationSource.FromVisual(window)!.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(PresentationSource.FromVisual(window))!;
            Point Position() => (Point)peer.GetType().GetProperty("DesktopPosition")!.GetValue(peer)!;
            var originalOrigin = native.ClientOrigin(xid); var originalPosition = Position();
            var frameOffset = new Point(originalOrigin.X - originalPosition.X, originalOrigin.Y - originalPosition.Y);
            window.WindowState = WindowState.Maximized;
            Wait(() => native.State(xid) == "Maximized" && window.WindowState == WindowState.Maximized); Drain();
            var target = Position();
            window.Left = target.X; window.Top = target.Y; Drain();
            window.WindowState = WindowState.Normal;
            Wait(() => native.State(xid) == "Normal" && Position() == target &&
                native.ClientOrigin(xid) == ((int)(target.X + frameOffset.X), (int)(target.Y + frameOffset.Y)));
            Drain();
            Require(window.Left == target.X && window.Top == target.Y && Position() == target,
                "A newly pending restore position was lost because it matched a transient maximized origin.");
        }
        finally { window.Close(); }
    });
    Run("native-wm-maximize-restore-pending-size", () =>
    {
        var window = new Window { Title = "Genuine WPF WM maximize fixture", Width = 320, Height = 200,
            Background = Brushes.Red, ShowActivated = false };
        try
        {
            window.Show();
            IntPtr xid = native.FindWindow(window.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000 && window.ActualWidth > 0);
            var normal = window.RestoreBounds;
            var normalClient = native.Size(xid);
            window.WindowState = WindowState.Maximized;
            Wait(() => native.State(xid) == "Maximized" && native.Size(xid).Width > normalClient.Width && window.ActualWidth > normal.Width);
            Require(window.RestoreBounds == normal, "Native maximize geometry overwrote the genuine Window normal restore bounds.");
            var maximizedClient = native.Size(xid);
            window.Width = normal.Width + 80; window.Height = normal.Height + 40;
            Drain();
            Require(native.Size(xid) == maximizedClient, "A pending normal-size edit resized the visible maximized native window.");
            Require(window.RestoreBounds.Width == normal.Width + 80 && window.RestoreBounds.Height == normal.Height + 40,
                "The genuine Window did not retain pending restore dimensions.");
            window.WindowState = WindowState.Normal;
            Wait(() => native.State(xid) == "Normal" && Math.Abs(window.ActualWidth - normal.Width - 80) < 1 &&
                Math.Abs(window.ActualHeight - normal.Height - 40) < 1);
        }
        finally { window.Close(); }
    });
    Run("native-wm-minimize-restore-pending-size", () =>
    {
        var window = new Window { Title = "Genuine WPF WM minimize fixture", Width = 320, Height = 200,
            Background = Brushes.Red, ShowActivated = false };
        try
        {
            window.Show(); IntPtr xid = native.FindWindow(window.Title);
            Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000 && window.ActualWidth > 0);
            var normal = window.RestoreBounds;
            var normalClient = native.Size(xid);
            window.WindowState = WindowState.Minimized;
            Wait(() => native.State(xid) == "Minimized" && !native.IsMapped(xid));
            window.Width = normal.Width + 60; window.Height = normal.Height + 30;
            Drain();
            Require(native.Size(xid) == normalClient, "A pending normal-size edit resized the minimized native window.");
            window.WindowState = WindowState.Normal;
            try
            {
                Wait(() => native.State(xid) == "Normal" && native.IsMapped(xid) && Math.Abs(window.ActualWidth - normal.Width - 60) < 1 &&
                    Math.Abs(window.ActualHeight - normal.Height - 30) < 1);
            }
            finally
            {
                windowManagerTrace.Enqueue(new { kind = "minimize-restore-result", requested = window.WindowState.ToString(),
                    actualNative = native.State(xid), mapped = native.IsMapped(xid), window.ActualWidth, window.ActualHeight,
                    restore = window.RestoreBounds.ToString(CultureInfo.InvariantCulture), expectedWidth = normal.Width + 60, expectedHeight = normal.Height + 30 });
            }
        }
        finally { window.Close(); }
    });
    Run("native-wm-below-minimum-and-above-maximum-restore-requests", () =>
    {
        foreach (var state in new[] { WindowState.Minimized, WindowState.Maximized })
        foreach (bool belowMinimum in new[] { true, false })
        {
            var window = new Window { Title = "Genuine WPF constrained restore " + state + belowMinimum,
                Width = 320, Height = 200, MinWidth = 200, MinHeight = 120, MaxWidth = 500, MaxHeight = 400, Background = Brushes.Red };
            IntPtr xid = IntPtr.Zero;
            void TraceConstraint(string kind) => windowManagerTrace.Enqueue(new { kind, nonNormalState = state.ToString(), belowMinimum,
                requested = window.WindowState.ToString(), timestamp = Stopwatch.GetTimestamp(),
                window.Width, window.Height, window.ActualWidth, window.ActualHeight,
                restore = window.RestoreBounds.ToString(CultureInfo.InvariantCulture) });
            window.StateChanged += (_, _) => TraceConstraint("constrained-state");
            window.SizeChanged += (_, _) => TraceConstraint("constrained-size");
            Action? stopMetricsTrace = null;
            try
            {
                window.Show(); xid = native.FindWindow(window.Title);
                Wait(() => native.IsMapped(xid) && native.Pixel(xid, 5, 5) == 0x00ff0000);
                window.WindowState = state;
                Wait(() => native.State(xid) == state.ToString() && window.WindowState == state);
                Drain(); var nonNormalClient = native.Size(xid);
                double requestedWidth = belowMinimum ? 100 : 800, requestedHeight = belowMinimum ? 80 : 650;
                window.Width = requestedWidth; window.Height = requestedHeight; Drain();
                Require(native.Size(xid) == nonNormalClient && window.RestoreBounds.Width == requestedWidth &&
                    window.RestoreBounds.Height == requestedHeight, "A constrained pending restore request was clamped early or resized the non-Normal native viewport.");
                TraceConstraint("constrained-before-normal-request");
                if (traceHostMetrics && state == WindowState.Maximized && !belowMinimum)
                    stopMetricsTrace = TraceNativeMetrics(window);
                window.WindowState = WindowState.Normal;
                try
                {
                    Wait(() => native.State(xid) == "Normal" && native.IsMapped(xid) &&
                        window.ActualWidth == (belowMinimum ? 200 : 500) && window.ActualHeight == (belowMinimum ? 120 : 400));
                    Drain();
                    var client = native.Size(xid);
                    Wait(() => native.Pixel(xid, client.Width - 5, client.Height - 5) == 0x00ff0000);
                    native.Expose(xid); Drain();
                    Require(native.Pixel(xid, client.Width - 5, client.Height - 5) == 0x00ff0000,
                        "The settled native viewport edge did not retain a complete WPF frame after resize/expose.");
                }
                finally
                {
                    windowManagerTrace.Enqueue(new { kind = "constrained-restore-result", nonNormalState = state.ToString(), belowMinimum,
                        requested = window.WindowState.ToString(), actualNative = native.State(xid), mapped = native.IsMapped(xid),
                        nativeClientWidth = native.Size(xid).Width, nativeClientHeight = native.Size(xid).Height,
                        window.ActualWidth, window.ActualHeight, restore = window.RestoreBounds.ToString(CultureInfo.InvariantCulture) });
                }
            }
            finally { stopMetricsTrace?.Invoke(); window.Close(); }
        }
    });
}

Action TraceNativeMetrics(Window window)
{
    var source = PresentationSource.FromVisual(window)!;
    object peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    object implementation = peer.GetType().GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer)!;
    object bridge = peer.GetType().GetField("_native", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer)!;
    object platform = bridge.GetType().GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
    var display = (IntPtr)platform.GetType().GetProperty("Display")!.GetValue(platform)!;
    var handle = (IntPtr)bridge.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
    Action callback = () =>
    {
        if (source.IsDisposed) return;
        object cached = implementation.GetType().GetProperty("ClientSize")!.GetValue(implementation)!;
        double cachedWidth = (double)cached.GetType().GetProperty("Width")!.GetValue(cached)!;
        double cachedHeight = (double)cached.GetType().GetProperty("Height")!.GetValue(cached)!;
        double scaling = (double)implementation.GetType().GetProperty("RenderScaling")!.GetValue(implementation)!;
        var server = X11Fixture.SizeOnConnection(display, handle);
        object publishedClient = peer.GetType().GetProperty("ClientSize")!.GetValue(peer)!;
        object publishedOuter = peer.GetType().GetProperty("OuterSize")!.GetValue(peer)!;
        object publishedState = peer.GetType().GetProperty("WindowState")!.GetValue(peer)!;
        windowManagerTrace.Enqueue(new { kind = "host-metrics-same-connection", windowState = window.WindowState.ToString(),
            cachedWidth, cachedHeight, scaling, serverWidth = server.Width, serverHeight = server.Height,
            publishedClient = publishedClient.ToString(), publishedOuter = publishedOuter.ToString(), publishedState = publishedState.ToString(),
            pendingBounds = window.GetType().GetField("_portablePendingBounds", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window)?.ToString(),
            awaitingBounds = window.GetType().GetField("_portableBoundsAwaitingAck", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window)?.ToString(),
            window.ActualWidth, window.ActualHeight, restore = window.RestoreBounds.ToString(CultureInfo.InvariantCulture) });
    };
    var metricsEvent = peer.GetType().GetEvent("MetricsChanged")!;
    metricsEvent.AddEventHandler(peer, callback);
    return () => metricsEvent.RemoveEventHandler(peer, callback);
}

void Drain()
{
    bool ready = false;
    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => ready = true, dispatcher!);
    try { Wait(() => ready); }
    finally { timer.Stop(); }
}

void Run(string name, Action action)
{
    try { action(); checks.Enqueue(new(name, "pass", "Observed through genuine WPF objects and native X11 callbacks/pixels.")); }
    catch (Exception exception) { checks.Enqueue(new(name, "fail", exception.ToString())); }
}
void Wait(Func<bool> ready)
{
    var deadline = Stopwatch.StartNew();
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
    { if (ready() || deadline.Elapsed > TimeSpan.FromSeconds(3)) frame.Continue = false; }, dispatcher!);
    try { if (!ready()) Dispatcher.PushFrame(frame); }
    finally { timer.Stop(); }
    Require(ready(), "The native/WPF observable result did not arrive within 3 seconds.");
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static byte[] BgraFrame(int width, int height, byte blue, byte green, byte red)
{
    byte[] frame = new byte[checked(width * height * 4)];
    for (int i = 0; i < frame.Length; i += 4)
    { frame[i] = blue; frame[i + 1] = green; frame[i + 2] = red; frame[i + 3] = 255; }
    return frame;
}
static ControlTemplate ButtonTemplate()
{
    var border = new FrameworkElementFactory(typeof(Border));
    border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
    border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
    return new ControlTemplate(typeof(Button)) { VisualTree = border };
}
static ControlTemplate TextTemplate()
{
    var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
    host.SetValue(FrameworkElement.OverridesDefaultStyleProperty, true);
    host.SetValue(Control.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
    var presenter = new FrameworkElementFactory(typeof(ScrollContentPresenter), "PART_ScrollContentPresenter");
    presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
    host.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ScrollViewer)) { VisualTree = presenter });
    return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
}
void WriteResults() => Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = wmOnly ? "genuine-wpf-native-window-manager" : "genuine-wpf-native-window", os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    scope = wmOnly ? "native virtual X11 server with workspace Openbox; genuine Window state, geometry and retained frame; physical desktop unqualified" :
        "native virtual X11 server; OS-injected XTest input; genuine Window, retained frame and InputManager; physical desktop unqualified",
    display = Environment.GetEnvironmentVariable("DISPLAY"), culture = CultureInfo.CurrentCulture.Name, settingsOrigin,
    windowManagerTrace,
    guidelineCapture,
    frameTransitions,
    clickThresholds,
    assemblies = new[] { typeof(Window).Assembly, typeof(Visual).Assembly, typeof(Dispatcher).Assembly, typeof(NativeWindowHost).Assembly }
        .Select(assembly => new { name = assembly.GetName().Name, version = assembly.GetName().Version?.ToString(),
            publicKeyToken = Convert.ToHexString(assembly.GetName().GetPublicKeyToken() ?? Array.Empty<byte>()), path = assembly.Location }),
    settings, resolvedSettings,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
internal sealed record Check(string name, string status, string detail);
internal delegate void PresentFrame(ReadOnlySpan<byte> pixels, int width, int height, int stride);
internal sealed class ImageScene(BitmapSource bitmap) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawImage(bitmap, new Rect(RenderSize));
}
