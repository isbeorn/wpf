extern alias WpfFork;

using System.Globalization;
using System.Text.Json;
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
            double expectedScale = double.Parse(args.Single(), CultureInfo.InvariantCulture);
            native.SetDpi(checked((int)(expectedScale * 96)));
            NativeWindowHost.InitializeForCurrentThread();
            var screensType = typeof(NativeWindowHost).Assembly.GetType("Wpf.Portable.NativeHost.Linux.X11Screens")!;
            using var screens = (IDisposable)Activator.CreateInstance(screensType, OwnedNativeAccess.Members, null, [OwnedNativeAccess.Connection], null)!;
            var screen = OwnedNativeAccess.Call(screens, "ReadPrimary") ?? throw new InvalidOperationException("The owned single-screen fixture did not supply observed screen metrics.");
            var bounds = OwnedNativeAccess.Get(screen, "Bounds");
            int x = (int)OwnedNativeAccess.Get(bounds, "X"), y = (int)OwnedNativeAccess.Get(bounds, "Y");
            int pixelWidth = (int)OwnedNativeAccess.Get(bounds, "Width"), pixelHeight = (int)OwnedNativeAccess.Get(bounds, "Height");
            double scale = (double)OwnedNativeAccess.Get(screen, "Dpi") / 96d;
            bool isPrimary = (bool)OwnedNativeAccess.Get(screen, "IsPrimary");
            var root = native.RootSize;
            if (x != 0 || y != 0 || pixelWidth != root.Width || pixelHeight != root.Height)
                throw new InvalidOperationException("This single-screen fixture's observed bounds do not match independent X11 root geometry.");
            if (scale != expectedScale) throw new InvalidOperationException("The native backend did not observe the explicitly controlled process scale.");
            double width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
            if (width != root.Width / scale || height != root.Height / scale)
                throw new InvalidOperationException("The registered dimensions differ from actual native pixels divided once by observed screen scaling.");
            checks.Add(new("observed-primary-bounds-and-registered-dip-pair", "pass", "Native pixels divided once by observed scaling match both registered axes."));
            var box = new ComboBox();
            if (box.MaxDropDownHeight != height / 3 || box.IsDropDownOpen)
                throw new InvalidOperationException("Real ComboBox metadata did not capture one third of observed primary height while closed.");
            checks.Add(new("observed-primary-genuine-combobox-metadata", "pass", "Closed ComboBox metadata uses one third of registered height."));
            observation = new { source = screen.GetType().FullName, selection = isPrimary ? "marked-primary" : "sole-observed-screen",
                name = OwnedNativeAccess.Get(screen, "Name"), isPrimary,
                id = OwnedNativeAccess.Get(screen, "Id").ToString(),
                pixelBounds = new { x, y, width = pixelWidth, height = pixelHeight }, nativeRootPixels = new { root.Width, root.Height },
                scale, widthDip = width, heightDip = height, box.MaxDropDownHeight };

        }
        catch (Exception error) { checks.Add(new("native-primary-screen", "fail", error.ToString())); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "native-primary-screen-startup-snapshot", scope = "Owned single-screen Xvfb observation, no GNOME monitor or live-update qualification.", observation, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count == 2 && checks.All(check => check.status == "pass") ? 0 : 1;
    }
}
