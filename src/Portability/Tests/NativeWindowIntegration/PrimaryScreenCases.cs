extern alias WpfFork;

using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.X11;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;

internal static class PrimaryScreenCases
{
    internal static int Run(string[] args)
    {
        var checks = new List<Check>();
        object? observation = null;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The observed primary-screen probe requires its owned Linux Xvfb server.");
            using var native = new X11Fixture("little");
            NativeWindowHost.InitializeForCurrentThread(AppBuilder.Configure<Avalonia.Application>());
            var screen = ExternalWindowBridge.ReadPrimaryScreen() ?? throw new InvalidOperationException("The owned single-screen fixture did not supply observed screen metrics.");
            var root = native.RootSize;
            if (screen.Bounds.X != 0 || screen.Bounds.Y != 0 || screen.Bounds.Width != root.Width || screen.Bounds.Height != root.Height)
                throw new InvalidOperationException("This single-screen fixture's observed bounds do not match independent X11 root geometry.");
            double expectedScale = double.Parse(args.Single(), CultureInfo.InvariantCulture);
            if (screen.Scaling != expectedScale) throw new InvalidOperationException("The native backend did not observe the explicitly controlled process scale.");
            double width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
            if (width != root.Width / screen.Scaling || height != root.Height / screen.Scaling)
                throw new InvalidOperationException("The registered dimensions differ from actual native pixels divided once by observed screen scaling.");
            checks.Add(new("observed-primary-bounds-and-registered-dip-pair", "pass", "Native pixels divided once by observed scaling match both registered axes."));
            var box = new ComboBox();
            if (box.MaxDropDownHeight != height / 3 || box.IsDropDownOpen)
                throw new InvalidOperationException("Real ComboBox metadata did not capture one third of observed primary height while closed.");
            checks.Add(new("observed-primary-genuine-combobox-metadata", "pass", "Closed ComboBox metadata uses one third of registered height."));
            observation = new { source = screen.GetType().FullName, selection = screen.IsPrimary ? "marked-primary" : "sole-observed-screen", screen.DisplayName, screen.IsPrimary,
                handle = screen.TryGetPlatformHandle()?.Handle.ToInt64(), descriptor = screen.TryGetPlatformHandle()?.HandleDescriptor,
                pixelBounds = new { screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height }, nativeRootPixels = new { root.Width, root.Height },
                scale = screen.Scaling, widthDip = width, heightDip = height, box.MaxDropDownHeight };
        }
        catch (Exception error) { checks.Add(new("native-primary-screen", "fail", error.ToString())); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "native-primary-screen-startup-snapshot", scope = "Owned single-screen Xvfb observation, no GNOME monitor or live-update qualification.", observation, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count == 2 && checks.All(check => check.status == "pass") ? 0 : 1;
    }
}
