using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

internal static class PrimaryScreenChecks
{
    private static Type MetricsType => typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings.PortablePrimaryScreenMetrics", true)!;
    internal static object CreateFixture() => Create(1537.25, 947.75);

    internal static string Verify(bool supplied)
    {
        if (!supplied)
        {
            Missing(() => SystemParameters.PrimaryScreenWidth);
            Missing(() => SystemParameters.PrimaryScreenHeight);
            return "Both missing primary-screen axes report the named immutable-profile prerequisite without native lookup.";
        }
        const double width = 1537.25, height = 947.75;
        if (SystemParameters.PrimaryScreenWidth != width || SystemParameters.PrimaryScreenHeight != height)
            throw new InvalidOperationException("The primary-screen snapshot swapped axes, rounded or converted DIPs twice.");
        var element = new Border();
        ResourceKey x = SystemParameters.PrimaryScreenWidthKey, y = SystemParameters.PrimaryScreenHeightKey;
        element.SetResourceReference(FrameworkElement.WidthProperty, x);
        element.SetResourceReference(FrameworkElement.HeightProperty, y);
        Exact(width, height);
        element.Resources[x] = 701.5; Exact(701.5, height);
        element.Resources.Remove(x); Exact(width, height);
        element.Resources[y] = 809.25; Exact(width, 809.25);
        element.Resources.Remove(y); Exact(width, height);
        element.Measure(new Size(2000, 2000)); element.Arrange(new Rect(0, 0, width, height));
        if (element.ActualWidth != width || element.ActualHeight != height || !ReferenceEquals(x, SystemParameters.PrimaryScreenWidthKey) || !ReferenceEquals(y, SystemParameters.PrimaryScreenHeightKey))
            throw new InvalidOperationException("Primary-screen resource key identity or DIP layout changed.");
        for (int axis = 0; axis < 2; axis++)
        {
            foreach (double value in new[] { 0, -double.Epsilon, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                bool rejected = false;
                try { Create(axis == 0 ? value : width, axis == 1 ? value : height); }
                catch (TargetInvocationException error) when (error.InnerException is ArgumentOutOfRangeException range && range.ParamName == (axis == 0 ? "widthDip" : "heightDip")) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("An invalid primary-screen axis was accepted or misidentified.");
            }
            foreach (double value in new[] { double.Epsilon, 0.25, double.MaxValue })
            {
                object metric = Create(axis == 0 ? value : width, axis == 1 ? value : height);
                if ((double)MetricsType.GetProperty(axis == 0 ? "WidthDip" : "HeightDip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(metric)! != value)
                    throw new InvalidOperationException("A finite positive primary-screen boundary was rounded or rejected.");
            }
        }
        var box = new ComboBox { Width = 180, Height = 32 };
        box.Items.Add("Declared test screen"); box.SelectedIndex = 0;
        box.BeginInit(); box.EndInit(); box.ApplyTemplate();
        box.Measure(new Size(300, 200)); box.Arrange(new Rect(0, 0, 180, 32));
        if (box.Template == null || box.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue || box.MaxDropDownHeight != height / 3 || box.IsDropDownOpen ||
            !box.IsMeasureValid || !box.IsArrangeValid || box.ActualWidth != 180 || box.ActualHeight != 32 || VisualTreeHelper.GetChildrenCount(box) == 0)
            throw new InvalidOperationException("The genuine closed Classic ComboBox did not use its primary-height metadata.");
        var shadow = box.Template.FindName("Shdw", box) as FrameworkElement ?? throw new InvalidOperationException("The original Classic dropdown template part is absent.");
        if (shadow.MaxHeight != height / 3) throw new InvalidOperationException("The original dropdown template binding lost the metadata default.");
        box.MaxDropDownHeight = 173.125;
        if (shadow.MaxHeight != 173.125) throw new InvalidOperationException("The original dropdown template binding ignored a local height.");
        box.ClearValue(ComboBox.MaxDropDownHeightProperty);
        if (box.MaxDropDownHeight != height / 3 || shadow.MaxHeight != height / 3 || box.IsDropDownOpen)
            throw new InvalidOperationException("Clearing a local dropdown height did not restore metadata while closed.");
        Type profile = typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings.PortableSystemSettings", true)!;
        object replacement = profile.GetMethod("CreateInitialLightProfile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false, null, 1.0, 500, false, 4.0, 4.0, null, Create(701, 503)])!;
        Type provider = typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider", true)!;
        bool frozen = false;
        try { provider.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [replacement]); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { frozen = true; }
        if (!frozen || SystemParameters.PrimaryScreenWidth != width || SystemParameters.PrimaryScreenHeight != height)
            throw new InvalidOperationException("Consumed primary-screen metrics were replaced.");
        return "Declared asymmetric fractional DIP pair, independent resource override/removal, boundaries and freeze, plus genuine closed Classic ComboBox metadata/template binding.";

        void Exact(double expectedWidth, double expectedHeight)
        {
            if (element.Width != expectedWidth || element.Height != expectedHeight) throw new InvalidOperationException("A primary-screen resource override changed the wrong axis or was not reversible.");
        }
    }

    internal static int RunUnavailable()
    {
        bool passed = false; string detail;
        try
        {
            Missing(() => SystemParameters.PrimaryScreenWidth); Missing(() => SystemParameters.PrimaryScreenHeight);
            try { _ = new ComboBox(); throw new InvalidOperationException("ComboBox invented missing screen dimensions."); }
            catch (TypeInitializationException error) when (error.InnerException is PlatformNotSupportedException unsupported && unsupported.Message.Contains("primary-screen", StringComparison.Ordinal)) { }
            detail = "Both getters and real ComboBox initialization fail with an explicit native-screen prerequisite in a separate process.";
            passed = true;
        }
        catch (Exception error) { detail = error.ToString(); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "primary-screen-unavailable", checks = new[] { new { name = "missing-screen-and-combobox-prerequisite", status = passed ? "pass" : "fail", detail } } }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    internal static string VerifyRepeatPolicy()
    {
        var button = new RepeatButton { Width = 120, Height = 32, Content = "Repeat" };
        if (button.Delay != 500 || button.Interval != 33)
            throw new InvalidOperationException("Real RepeatButton metadata lost the fixed portable 500/33 millisecond defaults.");
        button.BeginInit(); button.EndInit(); button.ApplyTemplate();
        button.Measure(new Size(200, 100)); button.Arrange(new Rect(0, 0, 120, 32));
        if (button.Template == null || button.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue ||
            VisualTreeHelper.GetChildrenCount(button) == 0 || !button.IsMeasureValid || !button.IsArrangeValid || button.ActualWidth != 120 || button.ActualHeight != 32)
            throw new InvalidOperationException("The actual Classic RepeatButton template did not construct and lay out.");
        button.Delay = 0; button.Interval = 1;
        if (button.Delay != 0 || button.Interval != 1) throw new InvalidOperationException("Local repeat timing overrides were ignored.");
        button.ClearValue(RepeatButton.DelayProperty); button.ClearValue(RepeatButton.IntervalProperty);
        if (button.Delay != 500 || button.Interval != 33) throw new InvalidOperationException("Removing local repeat timing did not restore the metadata defaults.");
        if (SystemParameters.KeyboardDelay != 1 || SystemParameters.KeyboardSpeed != 31)
            throw new InvalidOperationException("The declared portable keyboard repeat policy changed.");
        var element = new Border();
        ResourceKey delay = SystemParameters.KeyboardDelayKey, speed = SystemParameters.KeyboardSpeedKey;
        VerifyKey(delay, 1, 3); VerifyKey(speed, 31, 0);
        if (!ReferenceEquals(delay, SystemParameters.KeyboardDelayKey) || !ReferenceEquals(speed, SystemParameters.KeyboardSpeedKey) || button.Delay != 500 || button.Interval != 33)
            throw new InvalidOperationException("Original repeat keys or independent RepeatButton metadata changed.");
        return "Real Classic RepeatButton construction/template/layout, fixed 500/33 ms metadata, reversible local timing and original keyboard delay/speed resource override/removal.";

        void VerifyKey(ResourceKey key, int expected, int replacement)
        {
            element.SetResourceReference(FrameworkElement.TagProperty, key);
            if (!Equals(element.Tag, expected)) throw new InvalidOperationException("An original keyboard repeat key resolved incorrectly.");
            element.Resources[key] = replacement;
            if (!Equals(element.Tag, replacement) || SystemParameters.KeyboardDelay != 1 || SystemParameters.KeyboardSpeed != 31)
                throw new InvalidOperationException("A local repeat resource override changed the independent system policy.");
            element.Resources.Remove(key);
            if (!Equals(element.Tag, expected)) throw new InvalidOperationException("Removing a repeat resource override failed.");
        }
    }

    internal static string VerifyAnimation()
    {
        if (SystemParameters.ComboBoxAnimation || SystemParameters.ComboBoxPopupAnimation != PopupAnimation.None)
            throw new InvalidOperationException("The fixed portable Classic ComboBox animation policy changed.");
        ResourceKey animation = SystemParameters.ComboBoxAnimationKey, popup = SystemParameters.ComboBoxPopupAnimationKey;
        var element = new Border();
        element.SetResourceReference(FrameworkElement.TagProperty, animation);
        if (element.Tag is not false) throw new InvalidOperationException("The original animation key did not resolve false.");
        element.Resources[animation] = true;
        if (element.Tag is not true || SystemParameters.ComboBoxAnimation || SystemParameters.ComboBoxPopupAnimation != PopupAnimation.None)
            throw new InvalidOperationException("Local animation resource precedence changed the independent system policy.");
        element.Resources.Remove(animation);
        if (element.Tag is not false || !ReferenceEquals(animation, SystemParameters.ComboBoxAnimationKey)) throw new InvalidOperationException("Animation resource removal failed.");
        element.SetResourceReference(FrameworkElement.TagProperty, popup);
        if (!Equals(element.Tag, PopupAnimation.None)) throw new InvalidOperationException("The original popup animation key did not resolve None.");
        element.Resources[popup] = PopupAnimation.Fade;
        if (!Equals(element.Tag, PopupAnimation.Fade) || SystemParameters.ComboBoxPopupAnimation != PopupAnimation.None) throw new InvalidOperationException("Popup animation resource override failed.");
        element.Resources.Remove(popup);
        if (!Equals(element.Tag, PopupAnimation.None) || !ReferenceEquals(popup, SystemParameters.ComboBoxPopupAnimationKey)) throw new InvalidOperationException("Popup animation resource removal failed.");
        return "Fixed no-animation policy preserves both original keys and reversible resource overrides independently of getters.";
    }

    private static string CurrentProfileName()
    {
        Type provider = typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider", true)!;
        object profile = provider.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return (string)profile.GetType().GetProperty("ProfileName", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profile)!;
    }

    private static object Create(double width, double height) => Activator.CreateInstance(MetricsType, BindingFlags.Instance | BindingFlags.NonPublic, null, [width, height], null)!;
    private static void Missing(Func<double> read)
    {
        try { _ = read(); }
        catch (PlatformNotSupportedException error) when (error.Message.Contains(CurrentProfileName(), StringComparison.Ordinal) && error.Message.Contains("primary-screen", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Missing primary-screen metrics did not report the named profile prerequisite.");
    }
    internal static int RunConstructor()
    {
        bool passed = false;
        string detail;
        try
        {
            var box = new ComboBox();
            if (!double.IsFinite(box.MaxDropDownHeight) || box.MaxDropDownHeight <= 0 || box.IsDropDownOpen)
                throw new InvalidOperationException("Closed genuine ComboBox has no positive default dropdown height.");
            detail = "Constructed actual ComboBox with default MaxDropDownHeight=" + box.MaxDropDownHeight;
            passed = true;
        }
        catch (Exception error) { detail = error.ToString(); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "actual-combobox-primary-screen-constructor", os = RuntimeInformation.OSDescription,
            checks = new[] { new { name = "actual-combobox-constructor", status = passed ? "pass" : "fail", detail } } }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
