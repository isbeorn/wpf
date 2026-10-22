using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class SystemFontChecks
{
    internal static readonly string[] Roles = ["Status", "Icon", "Caption", "SmallCaption", "Menu"];
    private static readonly (string Suffix, DependencyProperty Property, object Alternate)[] Properties =
    [
        ("FontFamily", TextBlock.FontFamilyProperty, new FontFamily("serif")),
        ("FontSize", TextBlock.FontSizeProperty, 23.5),
        ("FontStyle", TextBlock.FontStyleProperty, FontStyles.Italic),
        ("FontWeight", TextBlock.FontWeightProperty, FontWeights.Bold),
        ("FontTextDecorations", TextBlock.TextDecorationsProperty, TextDecorations.Underline)
    ];

    internal static string VerifyRole(string role)
    {
        foreach (var (suffix, property, alternate) in Properties)
        {
            var keyProperty = typeof(SystemFonts).GetProperty(role + suffix + "Key", BindingFlags.Public | BindingFlags.Static)!;
            var key = (ResourceKey)keyProperty.GetValue(null)!;
            var text = new TextBlock();
            text.SetResourceReference(property, key);
            object expected = typeof(SystemFonts).GetProperty("Message" + suffix)!.GetValue(null)!;
            object actual = text.GetValue(property);
            Require(Equals(actual, expected), role + suffix + " resource did not resolve the declared Message policy.");
            Require(Equals(typeof(SystemFonts).GetProperty(role + suffix)!.GetValue(null), expected), role + suffix + " getter differs from its resource.");
            text.Resources[key] = alternate;
            Require(Equals(text.GetValue(property), alternate), role + suffix + " ignored a local resource override.");
            text.Resources.Remove(key);
            Require(Equals(text.GetValue(property), expected) && ReferenceEquals(key, keyProperty.GetValue(null)), role + suffix + " did not restore its stable original resource key.");
            if (suffix == "FontTextDecorations")
                Require(actual is TextDecorationCollection { IsFrozen: true, Count: 0 } && ReferenceEquals(actual, SystemFonts.MessageFontTextDecorations), role + " decorations did not share the frozen empty Message collection.");
            if (suffix == "FontFamily") Require(ReferenceEquals(actual, SystemFonts.MessageFontFamily), role + " family did not share the cached Message family.");
        }
        Require(SystemFonts.MessageFontFamily.Source == "Global User Interface" && SystemFonts.MessageFontSize == 11 &&
            SystemFonts.MessageFontStyle == FontStyles.Normal && SystemFonts.MessageFontWeight == FontWeights.Normal,
            "The existing declared Message font policy changed.");
        VerifyNoWindowsModules();
        return role + " shares all five declared Message values through original getters and keys, including override/removal and frozen decoration identity.";
    }

    internal static string VerifyToolTip()
    {
        var tip = new ToolTip { Content = "Portable status font" };
        tip.BeginInit(); tip.EndInit();
        tip.Measure(new Size(320, 160));
        tip.Arrange(new Rect(0, 0, tip.DesiredSize.Width, tip.DesiredSize.Height));
        Require(tip.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue && tip.Template != null &&
            VisualTreeHelper.GetChildrenCount(tip) > 0 && tip.IsMeasureValid && tip.IsArrangeValid && tip.ActualWidth > 0 && tip.ActualHeight > 0,
            "The genuine Classic ToolTip template did not complete layout.");
        Require(ReferenceEquals(tip.FontFamily, SystemFonts.StatusFontFamily) && tip.FontSize == SystemFonts.StatusFontSize &&
            tip.FontStyle == SystemFonts.StatusFontStyle && tip.FontWeight == SystemFonts.StatusFontWeight,
            "The genuine Classic ToolTip did not consume Status font resources.");
        Require(!tip.HasDropShadow, "The declared portable Classic ToolTip unexpectedly has a drop shadow.");
        VerifyNoWindowsModules();
        return "Original Classic ToolTip completed construction, template lookup and Measure/Arrange with declared Status font resources.";
    }

    internal static string VerifyDropShadow()
    {
        Require(!SystemParameters.DropShadow, "The declared portable Classic no-shadow policy changed.");
        ResourceKey key = SystemParameters.DropShadowKey;
        var element = new Border();
        element.SetResourceReference(FrameworkElement.TagProperty, key);
        Require(element.Tag is false, "The original DropShadow key did not resolve the fixed no-shadow policy.");
        element.Resources[key] = true;
        Require(element.Tag is true, "A local shadow resource override was ignored.");
        element.Resources.Remove(key);
        Require(element.Tag is false && ReferenceEquals(key, SystemParameters.DropShadowKey), "Removing the local override did not restore the original key and policy.");
        VerifyNoWindowsModules();
        return "Declared no-shadow getter and original resource key preserve local override/removal without native preference discovery.";
    }

    private static void VerifyNoWindowsModules()
    {
        if (OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        string[] forbidden = ["user32.dll", "gdi32.dll", "uxtheme.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "dwrite.dll"];
        Require(!process.Modules.Cast<ProcessModule>().Any(module => forbidden.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase)),
            "Portable font resources loaded a Windows native module.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
