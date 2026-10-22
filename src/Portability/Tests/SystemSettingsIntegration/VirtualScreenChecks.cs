using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static int RunVirtualScreenSettings(bool unavailable)
    {
        var checks = new List<object>();
        bool failed = false;
        var declared = new Rect(-1600.5, -300.25, 3840.75, 1500.5);
        object? profile = null;
        if (!unavailable)
        {
            profile = CreateProfile(false, virtualScreenBoundsDip: declared);
            Register(profile);
        }
        foreach (var metric in new (string Name, Func<double> Read, ResourceKey Key, double Expected)[]
        {
            ("left", () => SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeftKey, declared.Left),
            ("top", () => SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTopKey, declared.Top),
            ("width", () => SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenWidthKey, declared.Width),
            ("height", () => SystemParameters.VirtualScreenHeight, SystemParameters.VirtualScreenHeightKey, declared.Height)
        })
        {
            CheckVirtual("virtual-screen-" + metric.Name + (unavailable ? "-unavailable" : "-signed-fractional-resource"), () =>
            {
                if (unavailable)
                {
                    try { _ = metric.Read(); }
                    catch (PlatformNotSupportedException error) when (error.Message.Contains("virtual-screen", StringComparison.Ordinal) &&
                        error.Message.Contains("portable-light-v1", StringComparison.Ordinal)) { return; }
                    throw new InvalidOperationException("Missing virtual-screen settings did not report the named profile prerequisite.");
                }
                Require(metric.Read() == metric.Expected, "The process profile lost a signed or fractional virtual-screen value.");
                var consumer = new Border();
                consumer.SetResourceReference(FrameworkElement.TagProperty, metric.Key);
                Require(Equals(consumer.Tag, metric.Expected), "The original virtual-screen resource key differs from its getter.");
                consumer.Resources[metric.Key] = metric.Expected + 3.25;
                Require(Equals(consumer.Tag, metric.Expected + 3.25), "A local virtual-screen resource override was ignored.");
                consumer.Resources.Remove(metric.Key);
                Require(Equals(consumer.Tag, metric.Expected), "Removing the local virtual-screen resource did not restore the profile value.");
            });
        }
        if (!unavailable)
        {
            CheckVirtual("virtual-screen-background-process-snapshot", () =>
                Require(Task.Run(ReadVirtualScreen).GetAwaiter().GetResult() == declared,
                    "A background thread did not read the same process-wide virtual-screen rectangle."));
            CheckVirtual("virtual-screen-invalid-bounds", () =>
            {
                foreach (Rect bounds in new[] { Rect.Empty, new Rect(0, 0, 0, 1), new Rect(0, 0, 1, 0),
                    new Rect(double.NaN, 0, 1, 1), new Rect(0, double.PositiveInfinity, 1, 1),
                    new Rect(0, 0, double.PositiveInfinity, 1), new Rect(0, 0, 1, double.NaN),
                    new Rect(double.MaxValue, 0, double.MaxValue, 1) })
                    ExpectArgumentRange(() => CreateProfile(false, virtualScreenBoundsDip: bounds), "virtualScreenBoundsDip");
            });
            CheckVirtual("virtual-screen-constructor-and-value-snapshot", () =>
            {
                object constructed = profile!.GetType().GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(
                    ["virtual-constructor", GetProperty(profile, "Colors"), false, false, null, 1.0, 500, false, 4.0, 4.0, null, null, 400, 4.0, 4.0, declared]);
                Require(Equals(GetProperty(constructed, "VirtualScreenBoundsDip"), declared), "The constructor and profile factory disagree on virtual bounds.");
                foreach (Rect bounds in new[] { new Rect(17.25, 23.5, 701.75, 503.25), new Rect(-23.5, 17.25, 1.5, 2.75) })
                    Require(Equals(GetProperty(CreateProfile(false, virtualScreenBoundsDip: bounds), "VirtualScreenBoundsDip"), bounds),
                        "A valid positive or negative fractional origin was rejected or rounded.");
                Rect copy = (Rect)GetProperty(profile, "VirtualScreenBoundsDip");
                copy.Offset(10, -20);
                Require(ReadVirtualScreen() == declared, "Changing a returned rectangle mutated the immutable process profile.");
            });
            CheckVirtual("virtual-screen-late-replacement-rejected", () =>
            {
                bool rejected = false;
                try { Register(CreateProfile(false, virtualScreenBoundsDip: new Rect(17, 23, 701, 503))); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
                Require(rejected && ReadVirtualScreen() == declared, "A consumed virtual-screen startup profile was replaced.");
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "virtual-screen-process-settings", unavailable,
            scope = "Declared process-wide profile and public resources, no native topology qualification.", declared, checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;

        void CheckVirtual(string name, Action action)
        {
            try { action(); checks.Add(new { name, status = "pass", detail = "Observed through the process profile and public SystemParameters/resource values." }); }
            catch (Exception error) { failed = true; checks.Add(new { name, status = "fail", detail = error.ToString() }); }
        }
    }

    private static Rect ReadVirtualScreen() => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
}
