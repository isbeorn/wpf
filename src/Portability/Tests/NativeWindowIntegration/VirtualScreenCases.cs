extern alias WpfFork;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;

internal static class VirtualScreenCases
{
    internal static int Run(string[] args)
    {
        var checks = new List<Check>();
        object? observation = null;
        var unionObservations = new List<object>();
        bool completed = false;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Virtual-screen observation requires an owned Linux Xvfb server.");
            if (args.Length != 1) throw new ArgumentException("Pass the expected scale.");
            double scale = double.Parse(args[0], CultureInfo.InvariantCulture);
            using var native = new X11Fixture("little");
            native.SetDpi(checked((int)(scale * 96)));
            var root = native.RootSize;
            native.SetDesktopWorkArea(37, 53, root.Width - 107, root.Height - 151);
            NativeWindowHost.InitializeForCurrentThread();
            var expected = new Rect(0, 0, root.Width / scale, root.Height / scale);
            var expectedWorkArea = new Rect(37 / scale, 53 / scale, (root.Width - 107) / scale, (root.Height - 151) / scale);
            Check("virtual-screen-independent-full-bounds-and-workarea", () =>
                Require(SystemParameters.WorkArea == expectedWorkArea && expectedWorkArea != expected,
                    "The controlled reserved work area must differ from the independently observed full desktop bounds."));
            foreach (var metric in new (string Name, Func<double> Read, ResourceKey Key, double Expected)[]
            {
                ("width", () => SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenWidthKey, expected.Width),
                ("height", () => SystemParameters.VirtualScreenHeight, SystemParameters.VirtualScreenHeightKey, expected.Height),
                ("left", () => SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeftKey, expected.Left),
                ("top", () => SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTopKey, expected.Top)
            })
            {
                Check("virtual-screen-" + metric.Name + "-getter-and-resource", () =>
                {
                    Require(metric.Read() == metric.Expected, "The virtual-screen getter differs from independent native pixels divided once by observed scale.");
                    var consumer = new Border();
                    consumer.SetResourceReference(FrameworkElement.TagProperty, metric.Key);
                    Require(consumer.Tag is double value && value == metric.Expected, "The system resource key differs from the public virtual-screen getter.");
                    double replacement = metric.Expected + 7.5;
                    consumer.Resources[metric.Key] = replacement;
                    Require(consumer.Tag is double overridden && overridden == replacement, "The virtual-screen resource ignored a local override.");
                    consumer.Resources.Remove(metric.Key);
                    Require(consumer.Tag is double restored && restored == metric.Expected, "Removing the virtual-screen resource override did not restore native bounds.");
                });
            }
            Check("virtual-screen-background-same-snapshot", () =>
                Require(Task.Run(Read).GetAwaiter().GetResult() == expected,
                    "Background reads must observe the process-wide virtual-screen snapshot without registering a UI backend on that thread."));
            Check("virtual-screen-signed-gapped-union-vectors", () => VerifyUnionVectors(unionObservations));
            native.SetDesktopWorkArea(0, 0, root.Width, root.Height);
            native.SetDpi(scale == 1 ? 192 : 96);
            Check("virtual-screen-startup-snapshot", () =>
                Require(Read() == expected, "Virtual-screen dimensions and origins did not retain their registered startup snapshot after native settings changed."));
            observation = new { scale, fullDesktopPixels = new { root.Width, root.Height }, expectedDip = expected,
                reservedWorkAreaDip = expectedWorkArea, snapshotAfterNativeDpiAndWorkAreaChange = true };
            completed = true;
        }
        catch (Exception error) { checks.Add(new("virtual-screen-initialization", "fail", error.ToString())); }
        checks.Add(new("all-checks-completed", completed ? "pass" : "fail", completed ? "All requested checks returned." : "The virtual-screen fixture stopped early."));
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "native-virtual-screen-startup-snapshot",
            scope = "Private single-screen Xvfb plus declared signed/gapped monitor vectors; no actual multi-monitor, NINA restore or physical desktop qualification.",
            observation, unionObservations, checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count == 9 && checks.All(check => check.status == "pass") ? 0 : 1;

        void Check(string name, Action action)
        {
            try { action(); checks.Add(new(name, "pass", "Public getter and system resource behavior match independent native bounds.")); }
            catch (Exception error) { checks.Add(new(name, "fail", error.ToString())); }
        }
    }

    private static void VerifyUnionVectors(List<object> observations)
    {
        var cases = new (double Dpi, (int X, int Y, int Width, int Height)[] Monitors, Rect Expected)[]
        {
            (96, [(10, 20, 100, 80)], new Rect(10, 20, 100, 80)),
            (96, [(-800, -600, 800, 600), (0, 0, 1920, 1080)], new Rect(-800, -600, 2720, 1680)),
            (120, [(-1200, 120, 800, 600), (300, -240, 900, 800)], new Rect(-960, -192, 1920, 768)),
            (120, [(300, -240, 900, 800), (-1200, 120, 800, 600)], new Rect(-960, -192, 1920, 768)),
            (192, [(-1900, -900, 800, 600), (-900, -300, 640, 480)], new Rect(-950, -450, 820, 540)),
            (144, [(17, 29, 600, 400), (400, 200, 500, 300)], new Rect(17 / 1.5, 29 / 1.5, 883 / 1.5, 471 / 1.5))
        };
        var assembly = typeof(NativeWindowHost).Assembly;
        var rectangleType = assembly.GetType("Wpf.Portable.NativeHost.Linux.X11PixelRect", true)!;
        var screenType = assembly.GetType("Wpf.Portable.NativeHost.Linux.X11Screen", true)!;
        var union = typeof(NativeWindowHost).GetMethod("GetVirtualScreenBounds", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var vector in cases)
        {
            var monitors = Array.CreateInstance(screenType, vector.Monitors.Length);
            for (int i = 0; i < vector.Monitors.Length; i++)
            {
                var bounds = vector.Monitors[i];
                object full = Activator.CreateInstance(rectangleType, OwnedNativeAccess.Members, null,
                    [bounds.X, bounds.Y, bounds.Width, bounds.Height], null)!;
                object work = Activator.CreateInstance(rectangleType, OwnedNativeAccess.Members, null,
                    [bounds.X + 3, bounds.Y + 5, bounds.Width - 10, bounds.Height - 13], null)!;
                object monitor = Activator.CreateInstance(screenType, OwnedNativeAccess.Members, null,
                    [(nuint)(i + 1), "declared-monitor-" + i, i == 0, full, work, vector.Dpi], null)!;
                monitors.SetValue(monitor, i);
            }
            var actual = (Rect)union.Invoke(null, [monitors])!;
            Require(actual == vector.Expected, "The native startup union lost signed origins, gaps, order independence or one-time fractional DPI conversion.");
            observations.Add(new { kind = "declared-monitor-union", vector.Dpi, monitorCount = monitors.Length,
                expected = vector.Expected, actual });
        }
    }

    private static Rect Read() => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
