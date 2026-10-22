using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portability.SystemSettings.GenericFixture;

internal static partial class Program
{
    private static int RunClassicDictionaryDiagnostic()
    {
        var checks = new List<object>();
        var materialized = new List<string>();
        var phase = "classic-assembly-identity";
        var failed = false;
        try
        {
            var assembly = Assembly.Load("PresentationFramework.Classic");
            Require(assembly.GetName().FullName == "PresentationFramework.Classic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35", "Classic identity changed.");
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Classic escaped Default ALC.");
            checks.Add(new { name = phase, status = "pass", detail = assembly.Location });
            phase = "full-original-classic-dictionary";
            var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/PresentationFramework.Classic;component/themes/classic.xaml", UriKind.Relative));
            Require(dictionary.Contains(typeof(UserControl)) && dictionary.Contains(typeof(DocumentViewer)), "Original control styles are absent.");
            checks.Add(new { name = phase, status = "pass", detail = $"Original compiled dictionary has {dictionary.Count} entries; it was not merged into application resources." });
            foreach (var key in dictionary.Keys.Cast<object>())
            {
                phase = "deferred-resource:" + key;
                Require(dictionary[key] is not null, "Original deferred resource resolved to null.");
                materialized.Add(key.ToString()!);
            }
            checks.Add(new { name = "complete-deferred-resource-materialization", status = "pass", detail = $"Materialized all {materialized.Count} original entries without exclusions." });
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = phase, status = "fail", detail = exception.ToString() });
        }
        WriteThemeDiagnostic("original-classic-dictionary", checks, materialized, null);
        return failed ? 1 : 0;
    }

    private static int RunDefaultThemeLayoutDiagnostic()
    {
        var checks = new List<object>();
        object? observations = null;
        var failed = false;
        var phase = "custom-generic-fallback-and-explicit-style-precedence";
        try
        {
            VerifyGenericStylePrecedence();
            checks.Add(new { name = phase, status = "pass", detail = "Original custom generic BAML fallback survived explicit local style assignment and removal." });
            phase = "ordinary-usercontrol-default-template-and-content-layout";
            var content = new Grid { Width = 80, Height = 30 };
            var control = new UserControl { Content = content };
            control.Measure(new Size(320, 200));
            control.Arrange(new Rect(0, 0, 320, 200));
            control.UpdateLayout();
            var parent = VisualTreeHelper.GetParent(content);
            observations = new { templatePresent = control.Template is not null, visualChildren = VisualTreeHelper.GetChildrenCount(control),
                desiredWidth = control.DesiredSize.Width, desiredHeight = control.DesiredSize.Height,
                contentWidth = content.ActualWidth, contentHeight = content.ActualHeight,
                content.IsMeasureValid, content.IsArrangeValid, parentType = parent?.GetType().FullName };
            Require(control.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue, "Test supplied a local template.");
            Require(control.Template is not null && VisualTreeHelper.GetChildrenCount(control) > 0 && parent is ContentPresenter
                && content.IsMeasureValid && content.IsArrangeValid && content.ActualWidth == 80 && content.ActualHeight == 30,
                "Ordinary UserControl default template did not connect and lay out original content: " + JsonSerializer.Serialize(observations));
            checks.Add(new { name = "ordinary-usercontrol-default-template-and-content-layout", status = "pass", detail = "Default style connected real content through ContentPresenter with positive measured/arranged size." });
            phase = "ordinary-button-default-template-and-layout";
            var button = new Button { Content = "Classic", Width = 120, Height = 32 };
            VerifyOrdinaryControlLayout(button);
            checks.Add(new { name = phase, status = "pass", detail = "Ordinary Button resolved its real theme template and completed positive layout." });
            phase = "ordinary-textbox-default-template-contenthost-and-layout";
            var textBox = new TextBox { Text = "Classic", Width = 160, Height = 32 };
            VerifyOrdinaryControlLayout(textBox);
            Require(textBox.Template.FindName("PART_ContentHost", textBox) is ScrollViewer, "Original TextBox template did not connect its ScrollViewer content host.");
            checks.Add(new { name = phase, status = "pass", detail = "Ordinary TextBox resolved its real template, connected PART_ContentHost and completed positive layout." });
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = phase, status = "fail", detail = exception.ToString() });
        }
        WriteThemeDiagnostic("ordinary-default-theme-layout", checks, [], observations);
        return failed ? 1 : 0;
    }

    private static void VerifyGenericStylePrecedence()
    {
        var control = new GenericControl();
        control.BeginInit();
        control.EndInit();
        Require(control.Width == 71 && control.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(0, 128, 128), "Original custom generic fallback changed.");
        var style = new Style(typeof(GenericControl));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 89.0));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Red));
        control.Style = style;
        Require(control.Width == 89 && ReferenceEquals(control.Background, Brushes.Red), "Explicit style did not override generic theme setters.");
        control.ClearValue(FrameworkElement.StyleProperty);
        Require(control.Width == 71 && control.Background is SolidColorBrush restored && restored.Color == Color.FromRgb(0, 128, 128), "Removing explicit style did not restore generic fallback.");
    }

    private static void VerifyOrdinaryControlLayout(Control control)
    {
        control.BeginInit();
        control.EndInit();
        control.Measure(new Size(320, 200));
        control.Arrange(new Rect(0, 0, 320, 200));
        control.UpdateLayout();
        Require(control.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue, "Test supplied a local control template.");
        Require(control.Template is not null && VisualTreeHelper.GetChildrenCount(control) > 0
            && control.IsMeasureValid && control.IsArrangeValid && control.ActualWidth > 0 && control.ActualHeight > 0,
            control.GetType().Name + " did not resolve and lay out its ordinary default template: " + JsonSerializer.Serialize(new
            {
                templatePresent = control.Template is not null, visualChildren = VisualTreeHelper.GetChildrenCount(control),
                control.IsMeasureValid, control.IsArrangeValid, control.ActualWidth, control.ActualHeight,
                desiredWidth = control.DesiredSize.Width, desiredHeight = control.DesiredSize.Height
            }));
    }

    private static void WriteThemeDiagnostic(string mode, List<object> checks, List<string> materialized, object? observations)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "system-settings-classic-theme-diagnostic", mode, os = RuntimeInformation.OSDescription,
            scope = "Genuine source-built Classic dictionary and ordinary resource lookup. Direct dictionary loading does not select the default theme; native Windows theme parity is not claimed.",
            checks, materializedResources = materialized, observations,
            assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name is "PresentationFramework.Classic" or "PresentationUI" or "PresentationFramework" or "PresentationCore")
                .Select(assembly => new { identity = assembly.FullName, path = assembly.Location,
                    loadContext = AssemblyLoadContext.GetLoadContext(assembly)?.Name,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
