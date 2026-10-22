using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static int RunHoverSettings(string[] args)
    {
        var checks = new List<object>();
        bool failed = false;
        void Check(string name, Func<string> body)
        {
            try { checks.Add(new { name, status = "pass", detail = body() }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
        string variant = args.Length == 0 ? "default" : args.Length == 1 ? args[0] : "invalid";
        int time = variant switch { "default" => 400, "--custom" => 137, "--zero" => 0, "--max" => int.MaxValue, _ => -1 };
        double width = variant == "default" ? 4.0 : 2.5;
        double height = variant == "default" ? 4.0 : 7.5;
        if (time < 0)
            Check("arguments", () => throw new ArgumentException("Use --hover-settings-only with no argument or one of --custom, --zero or --max."));
        else
        {
            Check("hover-startup-profile", () =>
            {
                if (variant != "default")
                    Register(CreateProfile(false, hoverTime: time, hoverWidth: width, hoverHeight: height));
                Require((string)GetProperty(CurrentProfile(), "ProfileName") == "portable-light-v1", "Hover profile identity changed.");
                return "Declared startup profile consumed; desktop preferences were not queried.";
            });
            Check("hover-public-getters-and-resource-keys", () => VerifyHoverSettings(time, width, height));
            Check("hover-validation-and-immutability", VerifyHoverSettingsValidation);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "hover-settings-integration", variant, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static string VerifyHoverSettings(int time, double width, double height)
    {
        Require(SystemParameters.MouseHoverTime == TimeSpan.FromMilliseconds(time), "Public hover time did not preserve the startup interval.");
        Require(SystemParameters.MouseHoverWidth == width && SystemParameters.MouseHoverHeight == height, "Public hover dimensions did not preserve independent DIPs.");
        object profile = CurrentProfile();
        Require((int)GetProperty(profile, "MouseHoverTimeMilliseconds") == time
            && (double)GetProperty(profile, "MouseHoverWidthDip") == width
            && (double)GetProperty(profile, "MouseHoverHeightDip") == height, "Hover startup values were changed or the axes were exchanged.");

        Func<ResourceKey>[] keys = [() => SystemParameters.MouseHoverTimeKey, () => SystemParameters.MouseHoverWidthKey, () => SystemParameters.MouseHoverHeightKey];
        object[] expected = [TimeSpan.FromMilliseconds(time), width, height];
        object[] overrides = [TimeSpan.FromMilliseconds(time == 0 ? 1 : 0), width + 1.25, height + 2.5];
        for (int index = 0; index < keys.Length; index++)
        {
            ResourceKey key = keys[index]();
            var element = new Border();
            element.SetResourceReference(FrameworkElement.TagProperty, key);
            Require(Equals(element.Tag, expected[index]), "Hover resource did not resolve its original system key: " + index);
            element.Resources[key] = overrides[index];
            Require(Equals(element.Tag, overrides[index]), "Hover resource ignored a local override: " + index);
            element.Resources.Remove(key);
            Require(Equals(element.Tag, expected[index]) && ReferenceEquals(key, keys[index]()), "Hover resource removal or key identity changed: " + index);
        }
        var dimensions = new Border();
        dimensions.SetResourceReference(FrameworkElement.WidthProperty, SystemParameters.MouseHoverWidthKey);
        dimensions.SetResourceReference(FrameworkElement.HeightProperty, SystemParameters.MouseHoverHeightKey);
        dimensions.Measure(new Size(100, 100));
        dimensions.Arrange(new Rect(0, 0, width, height));
        Require(dimensions.ActualWidth == width && dimensions.ActualHeight == height, "Hover rectangle resources did not lay out in DIPs.");
        return $"Actual public getters and original resource keys retain {time} ms and {width}/{height} DIPs, local override/removal and paired layout.";
    }

    private static string VerifyHoverSettingsValidation()
    {
        foreach (int value in new[] { 0, 1, 400, int.MaxValue })
            Require((int)GetProperty(CreateProfile(false, hoverTime: value), "MouseHoverTimeMilliseconds") == value, "Valid hover time was rejected or changed.");
        foreach (int value in new[] { -1, int.MinValue })
            ExpectArgumentRange(() => CreateProfile(false, hoverTime: value), "mouseHoverTimeMilliseconds");
        foreach (double value in new[] { 0.0, -double.Epsilon, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            ExpectArgumentRange(() => CreateProfile(false, hoverWidth: value), "mouseHoverWidthDip");
            ExpectArgumentRange(() => CreateProfile(false, hoverHeight: value), "mouseHoverHeightDip");
        }
        foreach (double value in new[] { double.Epsilon, 0.25, 4.0, double.MaxValue })
        {
            object wide = CreateProfile(false, hoverWidth: value);
            object tall = CreateProfile(false, hoverHeight: value);
            Require((double)GetProperty(wide, "MouseHoverWidthDip") == value && (double)GetProperty(wide, "MouseHoverHeightDip") == 4.0,
                "Valid hover width was rounded or changed the other axis.");
            Require((double)GetProperty(tall, "MouseHoverHeightDip") == value && (double)GetProperty(tall, "MouseHoverWidthDip") == 4.0,
                "Valid hover height was rounded or changed the other axis.");
        }
        object current = CurrentProfile();
        Type type = current.GetType();
        object defaults = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke(
            ["hover-defaults", GetProperty(current, "Colors"), false, false, null, 1.0, 500, false, 4.0, 4.0, null, null, Type.Missing, Type.Missing, Type.Missing, Type.Missing]);
        Require((int)GetProperty(defaults, "MouseHoverTimeMilliseconds") == 400
            && (double)GetProperty(defaults, "MouseHoverWidthDip") == 4.0
            && (double)GetProperty(defaults, "MouseHoverHeightDip") == 4.0, "Constructor optional hover defaults changed.");
        object factoryDefaults = type.GetMethod("CreateInitialLightProfile", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [false, null, 1.0, 500, false, 4.0, 4.0, null, null, Type.Missing, Type.Missing, Type.Missing, Type.Missing])!;
        Require((int)GetProperty(factoryDefaults, "MouseHoverTimeMilliseconds") == 400
            && (double)GetProperty(factoryDefaults, "MouseHoverWidthDip") == 4.0
            && (double)GetProperty(factoryDefaults, "MouseHoverHeightDip") == 4.0, "Factory optional hover defaults changed.");
        var before = (SystemParameters.MouseHoverTime, SystemParameters.MouseHoverWidth, SystemParameters.MouseHoverHeight);
        bool rejected = false;
        try { Register(CreateProfile(false, hoverTime: 731, hoverWidth: 9.25, hoverHeight: 11.5)); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
        Require(rejected && ReferenceEquals(current, CurrentProfile())
            && before == (SystemParameters.MouseHoverTime, SystemParameters.MouseHoverWidth, SystemParameters.MouseHoverHeight), "Consumed hover profile was replaced or public values changed.");
        return "Nonnegative integer time boundaries, both positive finite DIP axes, constructor/factory defaults and consumed-profile rejection retain their contracts.";
    }
}
