using System.Resources;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var checks = new List<object>();
        var failed = false;
        FlowDocumentScrollViewer? viewer = null;
        void Check(string name, Func<object> action)
        {
            try { checks.Add(new { name, status = "pass", detail = action() }); }
            catch (Exception error) { failed = true; checks.Add(new { name, status = "fail", detail = (object)error.ToString() }); }
        }

        Check("public-flow-document-scroll-viewer-construction", () =>
        {
            viewer = new FlowDocumentScrollViewer { Width = 320, Height = 180 };
            Require(viewer.Document is null, "Probe requires the original initial empty-document state.");
            return viewer.GetType().FullName!;
        });
        Check("original-assembly-identity-and-all-compiled-pages", () =>
        {
            var assembly = typeof(PresentationUIStyleResources).Assembly;
            var identity = assembly.GetName();
            Require(identity.Name == "PresentationUI" && identity.Version == new Version(10, 0, 0, 0) &&
                Convert.ToHexString(identity.GetPublicKeyToken()!) == "31BF3856AD364E35", "Original PresentationUI identity changed.");
            using var stream = assembly.GetManifestResourceStream("PresentationUI.g.resources")!;
            using var reader = new ResourceReader(stream);
            var pages = reader.Cast<System.Collections.DictionaryEntry>().Select(item => (string)item.Key).Order().ToArray();
            Require(pages.Length == 13 && pages.All(page => page.EndsWith(".baml", StringComparison.Ordinal)), "Original compiled page closure differs.");
            Require(assembly.GetManifestResourceNames().Contains("MS.Internal.Documents.Resources.resources") &&
                assembly.GetManifestResourceNames().Contains("FxResources.PresentationUI.SR.resources"), "Original document/string resources missing.");
            return new { identity = assembly.FullName, assembly.Location, pages };
        });
        Check("all-original-theme-dictionaries", () =>
        {
            string[] themes = ["Aero.NormalColor", "Aero2.NormalColor", "AeroLite.NormalColor", "Classic", "Generic",
                "Luna.Homestead", "Luna.Metallic", "Luna.NormalColor", "MediaCenterTheme"];
            var counts = new List<object>();
            foreach (var theme in themes)
            {
                var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri(
                    "/PresentationUI;component/themes/" + theme.ToLowerInvariant() + ".xaml", UriKind.Relative));
                Require(dictionary.Count > 0, "Original theme dictionary is empty: " + theme);
                counts.Add(new { theme, dictionary.Count });
            }
            return counts;
        });
        Check("genuine-component-key-default-template-and-child-layout", () =>
        {
            Require(viewer is not null, "Public viewer construction failed.");
            var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/PresentationUI;component/themes/generic.xaml", UriKind.Relative));
            var key = new ComponentResourceKey(typeof(PresentationUIStyleResources), "PUIFlowDocumentScrollViewer");
            Require(dictionary[key] is Style style && style.TargetType == typeof(FlowDocumentScrollViewer), "Original Generic BAML component-key style missing.");
            viewer!.BeginInit();
            viewer.EndInit();
            viewer.Measure(new Size(320, 180));
            viewer.Arrange(new Rect(0, 0, 320, 180));
            viewer.UpdateLayout();
            Require(viewer.Template is not null, "Original default theme template did not resolve.");
            var scroll = viewer.Template!.FindName("PART_ContentHost", viewer) as ScrollViewer;
            Require(scroll?.Content is FrameworkElement, "Original template did not create its scroll host and child.");
            var child = (FrameworkElement)scroll!.Content;
            Require(child.GetType().FullName == "MS.Internal.Documents.FlowDocumentView", "Original child type differs.");
            Require(child.ActualWidth == 320 && child.ActualHeight == 180 && scroll.ViewportWidth == 320 && scroll.ViewportHeight == 180,
                "Original child initial layout or scroll viewport differs.");
            return new { componentKey = key.ToString(), child = child.GetType().FullName, child.ActualWidth,
                child.ActualHeight, scroll.ViewportWidth, scroll.ViewportHeight, visualChildren = VisualTreeHelper.GetChildrenCount(viewer) };
        });
        Check("no-native-backend-or-unreached-windows-contract-loaded", () =>
        {
            string[] contracts = ["DirectWriteForwarder", "ReachFramework", "System.Printing", "System.Windows.Forms",
                "System.Windows.Forms.Primitives", "System.Drawing.Common", "System.DirectoryServices",
                "Wpf.Portable.NativeHost", "Avalonia.Base", "Avalonia.X11"];
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => contracts.Contains(assembly.GetName().Name)).Select(assembly => assembly.FullName).ToArray();
            Require(loaded.Length == 0, "An unreached Windows contract was loaded: " + string.Join("; ", loaded));
            return "No window, native backend or document-text formatter was created.";
        });
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "original-source-presentation-ui", checks,
            scope = "Original assembly, BAML, default template and initial empty-document child layout. Non-null document text formatting and Windows document services remain separate." },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
