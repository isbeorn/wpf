using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class SystemMetricChecks
{
    private static readonly string[] Names = ["HorizontalScrollBarButtonWidth", "HorizontalScrollBarHeight", "VerticalScrollBarWidth", "VerticalScrollBarButtonHeight"];
    private static Type MetricsType => typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings.PortableScrollBarMetrics", throwOnError: true)!;

    internal static object CreateFixture() => Create([13.25, 19.5, 23.75, 29.125]);

    internal static string Verify(bool alternate)
    {
        double[] expected = alternate ? [13.25, 19.5, 23.75, 29.125] : [17, 17, 17, 17];
        Func<double>[] getters = [() => SystemParameters.HorizontalScrollBarButtonWidth, () => SystemParameters.HorizontalScrollBarHeight,
            () => SystemParameters.VerticalScrollBarWidth, () => SystemParameters.VerticalScrollBarButtonHeight];
        Func<ResourceKey>[] keys = [() => SystemParameters.HorizontalScrollBarButtonWidthKey, () => SystemParameters.HorizontalScrollBarHeightKey,
            () => SystemParameters.VerticalScrollBarWidthKey, () => SystemParameters.VerticalScrollBarButtonHeightKey];
        var core = typeof(SolidColorBrush).Assembly;
        var provider = core.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider", throwOnError: true)!;
        object profile = provider.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        object metrics = profile.GetType().GetProperty("ScrollBarMetrics", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(profile)!;
        for (int index = 0; index < Names.Length; index++)
        {
            Require(Read(metrics, index) == expected[index] && getters[index]() == expected[index], Names[index] + " did not preserve its profile DIP value.");
            ResourceKey key = keys[index]();
            var element = new Border();
            element.SetResourceReference(FrameworkElement.WidthProperty, key);
            Require(element.Width == expected[index], Names[index] + " did not resolve through its original DynamicResource key.");
            element.Resources[key] = expected[index] + 2.25;
            Require(element.Width == expected[index] + 2.25, Names[index] + " did not honor a local resource override.");
            element.Resources.Remove(key);
            Require(element.Width == expected[index] && ReferenceEquals(key, keys[index]()), Names[index] + " did not restore the system value and stable resource-key identity.");
        }
        VerifySize(keys[2](), keys[1](), expected[2], expected[1]);
        VerifySize(keys[0](), keys[3](), expected[0], expected[3]);

        string[] parameters = ["horizontalScrollBarButtonWidthDip", "horizontalScrollBarHeightDip", "verticalScrollBarWidthDip", "verticalScrollBarButtonHeightDip"];
        for (int index = 0; index < Names.Length; index++)
        {
            foreach (double invalid in new[] { 0.0, -double.Epsilon, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                double[] dimensions = [17, 17, 17, 17];
                dimensions[index] = invalid;
                bool rejected = false;
                try { Create(dimensions); }
                catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException range && range.ParamName == parameters[index]) { rejected = true; }
                Require(rejected, Names[index] + " accepted an invalid dimension or reported the wrong parameter.");
            }
            foreach (double valid in new[] { double.Epsilon, 0.25, 17.0, double.MaxValue })
            {
                double[] dimensions = [17, 17, 17, 17];
                dimensions[index] = valid;
                Require(Read(Create(dimensions), index) == valid, Names[index] + " rejected or rounded a finite positive boundary.");
            }
        }

        object replacement = profile.GetType().GetMethod("CreateInitialLightProfile", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [false, null, 1.0, 500, false, 4.0, 4.0, Create([31, 37, 41, 43]), null, 400, 4.0, 4.0])!;
        bool immutable = false;
        try { provider.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [replacement]); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { immutable = true; }
        Require(immutable && ReferenceEquals(profile, provider.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)), "Consumed metric profile was replaced.");
        for (int index = 0; index < Names.Length; index++) Require(getters[index]() == expected[index], Names[index] + " changed after rejected profile replacement.");
        return "Four fixed-profile DIP metrics preserve asymmetric values, original dynamic resource keys, local override/removal, positive finite boundaries and startup immutability.";
    }

    private static object Create(double[] dimensions) => Activator.CreateInstance(MetricsType, BindingFlags.NonPublic | BindingFlags.Instance, null,
        dimensions.Select(value => (object)value).ToArray(), null)!;

    private static double Read(object metrics, int index) => (double)MetricsType.GetProperty(Names[index] + "Dip", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(metrics)!;

    private static void VerifySize(ResourceKey width, ResourceKey height, double expectedWidth, double expectedHeight)
    {
        var element = new Border();
        element.SetResourceReference(FrameworkElement.WidthProperty, width);
        element.SetResourceReference(FrameworkElement.HeightProperty, height);
        element.Measure(new Size(500, 500));
        element.Arrange(new Rect(0, 0, expectedWidth, expectedHeight));
        Require(element.ActualWidth == expectedWidth && element.ActualHeight == expectedHeight, "Paired system metric resources did not lay out in DIPs.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
