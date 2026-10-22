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
        object? workAreaObservation = null;
        object? propertyObservation = null;
        string? expectedRejection = null;
        bool initializing = false;
        int expectedChecks = 5;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The observed primary-screen probe requires its owned Linux Xvfb server.");
            using var native = new X11Fixture("little");
            if (args.Length is < 1 or > 2) throw new ArgumentException("Pass scale and optional work-area profile.");
            double expectedScale = double.Parse(args[0], CultureInfo.InvariantCulture);
            string scenario = args.Length == 2 ? args[1] : "absent";
            native.SetDpi(checked((int)(expectedScale * 96)));
            var root = native.RootSize;
            var expectedPixels = scenario == "reserved"
                ? new Int32Rect(37, 53, root.Width - 107, root.Height - 151)
                : new Int32Rect(0, 0, root.Width, root.Height);
            if (scenario == "reserved")
                native.SetDesktopWorkArea(expectedPixels.X, expectedPixels.Y, expectedPixels.Width, expectedPixels.Height);
            else if (scenario != "absent")
            {
                uint[] first = [70, 27, checked((uint)(root.Width - 70)), checked((uint)(root.Height - 27))];
                uint[] second = [17, 31, checked((uint)(root.Width - 97)), checked((uint)(root.Height - 131))];
                uint[] values;
                uint? currentDesktop = null;
                int selected = 0;
                switch (scenario)
                {
                    case "noindex-one": values = first; break;
                    case "noindex-identical": values = [.. first, .. first]; break;
                    case "noindex-ambiguous":
                        values = [.. first, .. second];
                        expectedRejection = "The EWMH desktop work area is ambiguous without a current desktop.";
                        break;
                    case "index-zero": values = [.. first, .. second]; currentDesktop = 0; break;
                    case "index-one": values = [.. first, .. second]; currentDesktop = 1; selected = 4; break;
                    case "invalid-index":
                        values = [.. first, .. second]; currentDesktop = 2;
                        expectedRejection = "The EWMH desktop work area is malformed.";
                        break;
                    case "partial":
                        values = [.. first, 17, 31, 400];
                        expectedRejection = "The EWMH desktop work area is malformed.";
                        break;
                    case "invalid-width":
                    case "invalid-height":
                    case "oversized-width":
                    case "oversized-height":
                        values = [.. first]; currentDesktop = 0;
                        values[scenario.EndsWith("width", StringComparison.Ordinal) ? 2 : 3] = scenario.StartsWith("oversized", StringComparison.Ordinal) ? (uint)int.MaxValue + 1 : 0;
                        expectedRejection = "The EWMH desktop work area is outside supported pixel bounds.";
                        break;
                    default: throw new ArgumentException("Unknown work-area profile.");
                }
                native.SetDesktopWorkAreas(values, currentDesktop);
                propertyObservation = new { scenario, workAreaCardinals = values, currentDesktop, expectedRejection };
                if (expectedRejection is not null) expectedChecks = 1;
                else expectedPixels = new Int32Rect((int)values[selected], (int)values[selected + 1], (int)values[selected + 2], (int)values[selected + 3]);
            }
            initializing = true;
            NativeWindowHost.InitializeForCurrentThread();
            initializing = false;
            if (expectedRejection is not null) throw new InvalidOperationException("An invalid or ambiguous native work-area profile was accepted.");
            var screensType = typeof(NativeWindowHost).Assembly.GetType("Wpf.Portable.NativeHost.Linux.X11Screens")!;
            using var screens = (IDisposable)Activator.CreateInstance(screensType, OwnedNativeAccess.Members, null, [OwnedNativeAccess.Connection], null)!;
            var screen = OwnedNativeAccess.Call(screens, "ReadPrimary") ?? throw new InvalidOperationException("The owned single-screen fixture did not supply observed screen metrics.");
            var bounds = OwnedNativeAccess.Get(screen, "Bounds");
            int x = (int)OwnedNativeAccess.Get(bounds, "X"), y = (int)OwnedNativeAccess.Get(bounds, "Y");
            int pixelWidth = (int)OwnedNativeAccess.Get(bounds, "Width"), pixelHeight = (int)OwnedNativeAccess.Get(bounds, "Height");
            double scale = (double)OwnedNativeAccess.Get(screen, "Dpi") / 96d;
            bool isPrimary = (bool)OwnedNativeAccess.Get(screen, "IsPrimary");
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
            observation = new { scenario, source = screen.GetType().FullName, selection = isPrimary ? "marked-primary" : "sole-observed-screen",
                name = OwnedNativeAccess.Get(screen, "Name"), isPrimary,
                id = OwnedNativeAccess.Get(screen, "Id").ToString(),
                pixelBounds = new { x, y, width = pixelWidth, height = pixelHeight }, nativeRootPixels = new { root.Width, root.Height },
                scale, widthDip = width, heightDip = height, box.MaxDropDownHeight, expectedWorkAreaPixels = expectedPixels };

            var expectedDip = new Rect(expectedPixels.X / expectedScale, expectedPixels.Y / expectedScale,
                expectedPixels.Width / expectedScale, expectedPixels.Height / expectedScale);
            Rect workArea = SystemParameters.WorkArea;
            if (workArea != expectedDip)
                throw new InvalidOperationException("Public WorkArea differs from controlled native work-area pixels divided once by observed scaling.");
            checks.Add(new("observed-primary-workarea-dip", "pass", "All four public rectangle coordinates match the independent X11 work-area profile and scale."));
            object rectangle = typeof(SystemParameters).GetProperty("WorkAreaInternal", OwnedNativeAccess.Members)!.GetValue(null)!;
            int left = (int)OwnedNativeAccess.Get(rectangle, "left"), top = (int)OwnedNativeAccess.Get(rectangle, "top");
            int right = (int)OwnedNativeAccess.Get(rectangle, "right"), bottom = (int)OwnedNativeAccess.Get(rectangle, "bottom");
            if (left != expectedPixels.X || top != expectedPixels.Y ||
                right != expectedPixels.X + expectedPixels.Width || bottom != expectedPixels.Y + expectedPixels.Height)
                throw new InvalidOperationException("Internal WorkAreaInternal must retain exact native pixel edges without DIP conversion.");
            checks.Add(new("observed-primary-workarea-internal-pixels", "pass", "Both origins and both far edges retain the native pixel rectangle."));
            workAreaObservation = new { dip = workArea, pixels = new { left, top, right, bottom } };
            native.SetDesktopWorkArea(scenario == "reserved" ? 0 : 37, scenario == "reserved" ? 0 : 53,
                scenario == "reserved" ? root.Width : root.Width - 107, scenario == "reserved" ? root.Height : root.Height - 151);
            native.SetDpi(expectedScale == 1 ? 192 : 96);
            if (SystemParameters.WorkArea != expectedDip || SystemParameters.PrimaryScreenWidth != width || SystemParameters.PrimaryScreenHeight != height)
                throw new InvalidOperationException("Registered primary metrics must retain the existing startup snapshot after native properties change.");
            checks.Add(new("observed-primary-workarea-startup-snapshot", "pass", "Work area and dimensions retain the same registered startup snapshot."));
        }
        catch (InvalidOperationException error) when (initializing && expectedRejection is not null && error.Message == expectedRejection)
        { checks.Add(new("observed-primary-workarea-rejection", "pass", error.Message)); }
        catch (Exception error) { checks.Add(new("native-primary-screen", "fail", error.ToString())); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "native-primary-screen-startup-snapshot", scope = "Owned single-screen Xvfb observation, no GNOME monitor or live-update qualification.", observation, workAreaObservation, propertyObservation, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count == expectedChecks && checks.All(check => check.status == "pass") ? 0 : 1;
    }
}
