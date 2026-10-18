using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Portability.MarkupIntegration.Fixture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var checks = new List<object>();
        var interopLoads = new List<object>();
        var activeCheck = "startup";
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
        {
            if (args.LoadedAssembly.GetName().Name == "DirectWriteForwarder")
            {
                lock (interopLoads)
                    interopLoads.Add(new { check = activeCheck, identity = args.LoadedAssembly.FullName, path = args.LoadedAssembly.Location, stack = Environment.StackTrace });
            }
        };
        var failed = false;
        object? view = null;
        void Check(string name, Func<string> action)
        {
            activeCheck = name;
            try { checks.Add(new { name, status = "pass", detail = action() }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
        if (args.Length != 0)
        {
            Check("isolated-managed-type-trigger", () => ProbeTypeTrigger(args.Single()));
            Console.WriteLine(JsonSerializer.Serialize(new { probe = "portable-wpf-interop-load-trigger", os = RuntimeInformation.OSDescription, trigger = args, checks, interopLoads }, new JsonSerializerOptions { WriteIndented = true }));
            return failed ? 1 : 0;
        }
        Check("actual-portable-framework-assembly-identities", PortableAssemblies);
        Check("baml-optional-namespace-assembly", NamespaceResolutionChecks.MissingNamespaceCandidate);
        Check("baml-loads-available-namespace-contributor", NamespaceResolutionChecks.AvailableNamespaceContributor);
        Check("baml-explicit-assembly-and-dependency-errors", NamespaceResolutionChecks.RequiredAssemblyFailures);
        Check("compiled-attached-validation-template", NamespaceResolutionChecks.CompiledValidationTemplate);
        Check("portable-message-font-profile-and-real-formatting", VerifyMessageFont);
        Check("text-element-inherits-portable-message-font", VerifyTextElement);
        Check("real-baml-usercontrol-construction", () =>
        {
            view = ConstructView();
            return "Constructed the exact compiled markup fixture using the current fork's actual WPF framework implementations.";
        });
        if (view is not null)
        {
            Check("generated-component-fields-and-events", () => VerifyEvents(view));
            Check("merged-dictionary-template-and-pack-resource", () => VerifyResources(view));
        }
        else
        {
            checks.Add(new { name = "generated-component-fields-and-events", status = "blocked", detail = "Real BAML view construction failed." });
            checks.Add(new { name = "merged-dictionary-template-and-pack-resource", status = "blocked", detail = "Real BAML view construction failed." });
        }
        Check("portable-markup-avoids-directwrite-interop", () =>
        {
            Require(AppDomain.CurrentDomain.GetAssemblies().All(assembly => assembly.GetName().Name != "DirectWriteForwarder"), "Portable markup execution loaded the Windows-only DirectWrite compiler contract.");
            return "Actual message-font, TextElement and compiled BAML execution did not load DirectWriteForwarder.";
        });
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "real-portable-wpf-baml-runtime",
            os = RuntimeInformation.OSDescription,
            scope = "Exact markup integration fixture compiled and executed through current-source app-local WPF assemblies with no WindowsDesktop framework requirement. A failed construction check records the first actual runtime boundary and is not compatibility success.",
            checks,
            interopLoads,
            loadedWindowsContracts = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name is "DirectWriteForwarder" or "System.Printing" or "ReachFramework" or "PresentationUI" or "System.Private.Windows.Core")
                .Select(assembly => new { identity = assembly.FullName, path = assembly.Location }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ProbeTypeTrigger(string mode)
    {
        var core = Assembly.Load("PresentationCore");
        if (mode == "--probe-font-cache-dpi")
        {
            var utility = core.GetType("MS.Internal.FontCache.Util", throwOnError: true)!;
            var value = utility.GetProperty("PixelsPerDip", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            Require(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) == 1.0, "Portable font cache DPI changed.");
            return $"Invoked actual FontCache.Util.PixelsPerDip: {value}.";
        }
        var name = mode switch
        {
            "--probe-generic-run-constructor" => "MS.Internal.TextFormatting.GenericTextRunProperties",
            "--probe-generic-paragraph-constructor" => "MS.Internal.TextFormatting.GenericTextParagraphProperties",
            "--probe-span-vector-constructor" => "MS.Internal.SpanVector",
            "--probe-formatted-constructor" => "System.Windows.Media.FormattedText",
            "--probe-typography-constructor" => "System.Windows.Media.TextFormatting.TextRunTypographyProperties",
            _ => throw new ArgumentException("Unknown isolated type trigger.")
        };
        var type = core.GetType(name, throwOnError: true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length).First();
        RuntimeHelpers.PrepareMethod(constructor.MethodHandle);
        return $"Prepared actual constructor IL without executing FormattedText or a view: {constructor.DeclaringType!.FullName}.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string PortableAssemblies()
    {
        var names = new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework" };
        return string.Join("; ", names.Select(name =>
        {
            var assembly = Assembly.Load(name);
            var identity = assembly.GetName();
            Require(identity.Version == new Version(10, 0, 0, 0), $"Unexpected portable assembly identity: {identity}");
            var buildVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Require(buildVersion is not null && buildVersion.Contains("-portable.", StringComparison.Ordinal), $"Assembly does not identify the source-built portable product: {identity}");
            Require(Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), $"Framework assembly did not load app-locally: {assembly.Location}");
            return $"{identity.FullName}; build={buildVersion}: {RuntimeAssemblyEvidence.Describe(assembly)}";
        })) + "; " + RuntimeAssemblyEvidence.ExtensionsAndHost();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyMessageFont()
    {
        Require(SystemFonts.MessageFontFamily.Source == "Global User Interface" && SystemFonts.MessageFontSize == 11.0, "Initial portable UI font profile changed.");
        Require(SystemFonts.MessageFontStyle == FontStyles.Normal && SystemFonts.MessageFontWeight == FontWeights.Normal, "Initial portable UI font style changed.");
        Require(SystemFonts.MessageFontTextDecorations.Count == 0 && SystemFonts.MessageFontTextDecorations.IsFrozen, "Portable UI font decorations must be empty and frozen.");
        var typeface = new System.Windows.Media.Typeface(SystemFonts.MessageFontFamily, SystemFonts.MessageFontStyle, SystemFonts.MessageFontWeight, FontStretches.Normal);
        Require(typeface.TryGetGlyphTypeface(out var physical), "Portable UI font did not resolve to a physical installed face.");
        Require(physical.FontUri.IsFile && File.Exists(physical.FontUri.LocalPath), "Portable UI face does not identify an installed physical font file.");
        var text = new System.Windows.Media.FormattedText("Portable UI", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, SystemFonts.MessageFontSize, System.Windows.Media.Brushes.Black, 1);
        Require(text.Width > 0 && text.Height > 0, "Actual portable formatter did not format the UI font.");
        return $"Initial portable product profile: 11 DIPs, Normal, no decorations; physical face={physical.FontUri}; formatted width={text.Width}, height={text.Height}. OS desktop font/theme preference parity is not claimed.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyTextElement()
    {
        var run = new System.Windows.Documents.Run("Portable UI");
        Require(run.FontFamily.Source == SystemFonts.MessageFontFamily.Source && run.FontSize == SystemFonts.MessageFontSize, "Actual TextElement did not inherit the portable message font profile.");
        Require(run.FontStyle == SystemFonts.MessageFontStyle && run.FontWeight == SystemFonts.MessageFontWeight, "Actual TextElement font style differs from the portable profile.");
        return $"Constructed actual Run/TextElement with family={run.FontFamily.Source}, size={run.FontSize}, style={run.FontStyle} and weight={run.FontWeight}.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Keep WPF types out of the entry point so runtime loading failures can be reported as JSON.")]
    private static object ConstructView() => new ProbeView();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyEvents(object instance)
    {
        var view = (ProbeView)instance;
        Require(view.ConnectedButton is not null && view.ConnectedCard is not null, "Generated fields did not connect.");
        view.ConnectedButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.InitializeComponent();
        view.ConnectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(view.ActionCount == 2, "Generated event wiring or repeated-initialization guard failed.");
        return "Generated fields, event hookup and idempotent InitializeComponent executed through the real framework.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyResources(object instance)
    {
        var view = (ProbeView)instance;
        Require(view.ConnectedCard.GetType() == typeof(ProbeCard), "Local-type BAML did not resolve.");
        var content = (TextBlock)view.ConnectedTemplateTarget.ContentTemplate.LoadContent();
        content.DataContext = new ProbeModel { Label = "Runtime template value" };
        content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Require(content.Text == "Runtime template value", "Compiled template binding did not materialize.");
        Require(view.Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush, "Merged compiled dictionary did not load.");
        return "Resolved local types, merged dictionary and bound compiled template. " + VerifyPackResources();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyPackResources()
    {
        const string component = "/Portability.MarkupIntegration.Fixture;component/";
        const string text = "assets/casesensitive%20%ce%a9.txt";
        byte[]? expectedText = null;
        var variants = new[]
        {
            component + text,
            "pack://application:,,," + component + text,
            component + "Assets/CaseSensitive%20%CE%A9.TXT",
            component + text + "?mime-probe=1",
            component + text + "#mime-probe"
        };
        foreach (var location in variants)
        {
            var resource = Application.GetResourceStream(new Uri(location, UriKind.RelativeOrAbsolute));
            Require(resource is not null && resource.ContentType == "text/plain", $"TXT pack resource MIME differs: {location}");
            using var stream = resource!.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var bytes = copy.ToArray();
            expectedText ??= bytes;
            Require(bytes.SequenceEqual(expectedText) && System.Text.Encoding.UTF8.GetString(bytes).Contains("Actual pack resource", StringComparison.Ordinal), $"TXT pack resource bytes differ: {location}");
        }
        byte[] expectedOpaque = [0x00, 0xff, 0x10, 0x50, 0x4b, 0x03, 0x04, 0x00];
        foreach (var name in new[] { "assets/opaque%20payload%20%ce%a9.fixtureopaque", "assets/noextension%20%ce%a9" })
        {
            var resource = Application.GetResourceStream(new Uri(component + name, UriKind.Relative));
            Require(resource is not null && resource.ContentType == "application/octet-stream", $"Opaque resource must not infer a format: {name}");
            using var stream = resource!.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Require(copy.ToArray().SequenceEqual(expectedOpaque), $"Opaque resource bytes changed: {name}");
        }
        var baml = Application.GetResourceStream(new Uri(component + "views/orbit%20view%20%ce%a9.xaml", UriKind.Relative));
        Require(baml is not null && baml.ContentType == "application/baml+xml", "Known XAML-to-BAML resource MIME changed.");
        baml!.Stream.Dispose();
        return "Verified Unicode TXT resource MIME/bytes through relative and absolute pack URIs, case, query and fragment variants; unknown and extensionless resources remain exact opaque bytes; compiled BAML MIME is preserved.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
