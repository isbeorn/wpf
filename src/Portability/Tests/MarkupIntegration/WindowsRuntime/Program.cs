using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Portability.MarkupIntegration.Fixture;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var checks = new List<object>();
        var failed = false;
        void Check(string name, Func<string> action)
        {
            try { checks.Add(new { name, status = "pass", detail = action() }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        _ = new Application();
        ProbeView? view = null;
        Check("real-baml-view-construction-and-generated-event-wiring", () =>
        {
            view = new ProbeView();
            Require(view.ConnectedButton is not null && view.ConnectedCard is not null, "Named BAML elements were not connected.");
            view.ConnectedButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.InitializeComponent();
            view.ConnectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(view.ActionCount == 2, "Generated event hookup or repeated InitializeComponent guard failed.");
            return "Constructed real compiled view; generated named fields connected; event executed twice without duplicate hookup.";
        });
        Check("dictionary-template-and-local-type-two-pass-runtime", () =>
        {
            Require(view is not null, "View construction failed.");
            Require(view!.ConnectedCard.GetType() == typeof(ProbeCard), "Local control type did not resolve from pass2 BAML.");
            var template = view.ConnectedTemplateTarget.ContentTemplate;
            var content = (TextBlock)template.LoadContent();
            content.DataContext = new ProbeModel { Label = "Runtime template value" };
            content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Require(content.Text == "Runtime template value", "Compiled dictionary template or binding did not materialize.");
            Require(view.Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush, "Merged compiled dictionary did not load.");
            return "Resolved local control, merged dictionary and compiled DataTemplate; bound template content materialized.";
        });
        Check("pack-resource-spaces-unicode-and-source-case", () =>
        {
            var info = Application.GetResourceStream(new Uri("/Portability.MarkupIntegration.Fixture;component/assets/casesensitive%20%ce%a9.txt", UriKind.Relative));
            using var reader = new StreamReader(info.Stream);
            Require(reader.ReadToEnd().Contains("Actual pack resource", StringComparison.Ordinal), "Compiled pack resource contents did not round-trip.");
            return "Loaded actual embedded resource from a case-sensitive source path containing spaces and Unicode.";
        });
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "real-wpf-markup-windows-runtime-reference", os = RuntimeInformation.OSDescription, scope = "Current fork compiler output loaded by stock Windows WPF runtime. Linux real-framework runtime construction remains pending.", framework = typeof(Application).Assembly.FullName, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }
}
