using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
#if NINA_PORTABLE_ENTRY_DIAGNOSTICS
        if (Environment.GetCommandLineArgs().Contains("--portable-entry-diagnostics", StringComparer.Ordinal))
            return PortableEntryDiagnosticsCases.Run();
#endif

        var checks = new List<object>();
        var loads = new List<object>();
        var nativeFailures = new List<object>();
        var phase = "startup";
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
        {
            lock (loads)
                loads.Add(new { phase, identity = args.LoadedAssembly.FullName, path = args.LoadedAssembly.Location,
                    loadContext = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly)?.Name,
                    stack = args.LoadedAssembly.GetName().Name is "DirectWriteForwarder" or "System.Private.Windows.Core" or "System.Windows.Forms" ? Environment.StackTrace : null });
        };
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is DllNotFoundException or MarshalDirectiveException or BadImageFormatException)
                lock (nativeFailures)
                    nativeFailures.Add(new { phase, exception = args.Exception.ToString() });
        };
        var failed = false;
        var binaryVerified = false;
        var viewAttempted = false;
        var viewConstructed = false;
        var layoutVerified = false;
        object? layoutObservations = null;
        using var watchdog = new Timer(_ => Environment.Exit(3), null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Closure.json")));
        var resources = new List<string>();
        try
        {
            phase = "unchanged-managed-closure";
            VerifyClosure(manifest.RootElement);
            binaryVerified = true;
            checks.Add(new { name = phase, status = "pass", detail = "Every prepared managed assembly/satellite has its pinned baseline bytes; no producer project or original application startup is invoked." });
            phase = IsStockReference ? "stock-windows-runtime" : "qualified-package-runtime";
            checks.Add(new { name = phase, status = "pass", detail = VerifyRuntime() });
            phase = "artifact-state-before-nina-initialization";
            ConfigureState();
            checks.Add(new { name = phase, status = "pass", detail = "Original public CoreUtil.APPLICATIONTEMPPATH and ProfileService.PROFILEFOLDER redirected before resource/profile initialization." });
            phase = "six-upstream-resource-dictionaries";
            var application = LoadResources(resources);
            checks.Add(new { name = phase, status = "pass", detail = string.Join("; ", resources) });
            phase = "unchanged-direct-view-initialize-component";
            viewAttempted = true;
            var view = ConstructView();
            viewConstructed = true;
            checks.Add(new { name = phase, status = "pass", detail = "Constructed actual NINA.View.Equipment.Guider.DirectGuiderDetailView through its unchanged Microsoft-generated InitializeComponent." });
            phase = "real-view-layout-and-validation";
            var detail = LayoutAndValidate(view, out layoutObservations);
            checks.Add(new { name = phase, status = "pass", detail });
            layoutVerified = true;
            GC.KeepAlive(application);
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = phase, status = "fail", detail = exception.ToString() });
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "nina-unchanged-binary-portable-direct-view-diagnostic",
            os = RuntimeInformation.OSDescription,
            hostMode = IsStockReference ? "stock-WindowsDesktop-reference" : "package-only-portable-diagnostic",
            scope = "Phase3 diagnostic: portable mode uses immutable Phase1 final-2 packages; the explicitly separate stock mode compares WindowsDesktop layout. Original NINA application startup, NUnit hardware bootstrap, native display and full workflow are not invoked. Known final-2 animation/word-selection defects are outside these checks; final qualification requires refreshed packages after Phase2 repairs.",
            compatibility = new { sourceRebuild = "not-performed", unchangedBinary = binaryVerified ? "verified" : "failed",
                originalMicrosoftBamlView = viewConstructed ? "pass" : viewAttempted ? "fail" : "blocked",
                layout = layoutVerified ? "pass" : viewConstructed ? "fail" : "blocked" },
            origin = manifest.RootElement,
            checks,
            loadedResourceDictionaries = resources,
            layoutObservations,
            assemblyLoads = loads,
            nativeFailures,
            linuxWindowsModuleMappings = OperatingSystem.IsLinux()
                ? File.ReadAllLines("/proc/self/maps").Where(line => line.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase) || line.Contains("DirectWriteForwarder", StringComparison.OrdinalIgnoreCase) || line.Contains("user32", StringComparison.OrdinalIgnoreCase)).ToArray()
                : [],
            ninaAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name!.StartsWith("NINA", StringComparison.Ordinal))
                .Select(assembly => new { identity = assembly.FullName, path = assembly.Location, loadContext = AssemblyLoadContext.GetLoadContext(assembly)?.Name,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static void VerifyClosure(JsonElement manifest)
    {
        Require(manifest.GetProperty("revision").GetString() == "4742afae0a09c79be772f23fcf163fb5852d2775", "Unexpected NINA revision.");
        foreach (var file in manifest.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, file.GetProperty("outputPath").GetString()!));
            Require(path.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal), "Closure path escapes output.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.GetProperty("sha256").GetString(), $"Unchanged binary hash differs: {path}");
            Require(AssemblyName.GetAssemblyName(path).FullName == file.GetProperty("identity").GetString(), $"Unchanged binary identity differs: {path}");
        }
        var configName = Assembly.GetEntryAssembly()!.GetName().Name + ".runtimeconfig.json";
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, configName)));
        var options = config.RootElement.GetProperty("runtimeOptions");
        var frameworks = options.TryGetProperty("framework", out var single) ? new[] { single.GetProperty("name").GetString() }
            : options.GetProperty("frameworks").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
        Require(IsStockReference ? frameworks.Contains("Microsoft.WindowsDesktop.App") : frameworks.SequenceEqual(new[] { "Microsoft.NETCore.App" }), "Host framework configuration differs from its explicit mode.");
    }

    private static string VerifyRuntime()
    {
        var result = new List<string>();
        foreach (var name in new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework", "System.Windows.Extensions" })
        {
            var assembly = Assembly.Load(name);
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Runtime escaped Default ALC.");
            Require(IsStockReference ? assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal)
                : Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Runtime did not load from its explicit host mode.");
            result.Add($"{assembly.FullName}: {assembly.Location}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))}");
        }
        return string.Join("; ", result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureState()
    {
        var state = Environment.GetEnvironmentVariable("NINA_PORTABLE_VIEWS_STATE") ?? throw new InvalidOperationException("Runner must provide an artifact state directory.");
        Require(Path.IsPathFullyQualified(state), "State directory must be absolute.");
        Directory.CreateDirectory(state);
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = state;
        NINA.Profile.ProfileService.PROFILEFOLDER = Path.Combine(state, "Profiles");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Keep framework types out of Main so initial type-loading failures can be captured as JSON.")]
    private static object LoadResources(List<string> loaded)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var source in new[]
        {
            "Resources/StaticResources/ProfileService.xaml", "Resources/StaticResources/Brushes.xaml",
            "Resources/Styles/TextBlock.xaml", "Resources/Styles/TextBox.xaml",
            "Resources/Styles/CheckBox.xaml", "Resources/Styles/Tooltip.xaml"
        })
        {
            var uri = "/NINA.WPF.Base;component/" + source;
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) });
            loaded.Add(uri);
        }
        Require(app.Resources["ProfileService"].GetType() == typeof(NINA.Profile.ProfileService), "Original profile resource type differs.");
        return app;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Keep NINA types out of Main so unchanged-assembly loading failures can be captured as JSON.")]
    private static object ConstructView()
    {
        var type = typeof(NINA.View.Equipment.Guider.DirectGuiderDetailView);
        var assembly = type.Assembly;
        Require(assembly.GetName().Version == new Version(3, 3, 0, 1068) && assembly.GetName().GetPublicKeyToken()!.Length == 0, "NINA identity differs.");
        Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "NINA escaped Default ALC.");
        var generated = type.GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>();
        Require(generated?.Tool == "PresentationBuildTasks" && generated.Version == "10.0.12.0", "NINA component was not produced by its original Microsoft compiler.");
        return new NINA.View.Equipment.Guider.DirectGuiderDetailView();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string LayoutAndValidate(object value, out object observations)
    {
        var view = (UserControl)value;
        view.Measure(new Size(800, 450));
        view.Arrange(new Rect(0, 0, 800, 450));
        view.UpdateLayout();
        var minimum = Descendants(view).OfType<TextBox>().Single(textBox => BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path?.Path == "ActiveProfile.GuiderSettings.MountDitherMinimumPixels");
        var binding = BindingOperations.GetBinding(minimum, TextBox.TextProperty)!;
        Require(binding.ValidationRules.Count(rule => rule is NINA.Core.Utility.ValidationRules.DoubleRangeRule) == 1, "Original minimum-dither validation rule differs.");
        Require(double.IsFinite(view.DesiredSize.Width) && double.IsFinite(view.DesiredSize.Height), "Actual view layout is non-finite.");
        var content = view.Content as FrameworkElement;
        var contentCompletedLayout = content is not null && content.IsMeasureValid && content.IsArrangeValid && content.ActualWidth > 0 && content.ActualHeight > 0;
        observations = new
        {
            viewVisibility = view.Visibility.ToString(), view.IsMeasureValid, view.IsArrangeValid,
            desiredWidth = view.DesiredSize.Width, desiredHeight = view.DesiredSize.Height,
            view.ActualWidth, view.ActualHeight, templatePresent = view.Template is not null,
            visualChildren = System.Windows.Media.VisualTreeHelper.GetChildrenCount(view),
            contentType = content?.GetType().FullName, contentVisibility = content?.Visibility.ToString(),
            contentMeasureValid = content?.IsMeasureValid, contentArrangeValid = content?.IsArrangeValid,
            contentWidth = content?.ActualWidth, contentHeight = content?.ActualHeight,
            contentVisualParent = content is null ? null : System.Windows.Media.VisualTreeHelper.GetParent(content)?.GetType().FullName,
            columns = content is Grid grid ? grid.ColumnDefinitions.Select(column => new { width = column.Width.ToString(), actual = column.ActualWidth }).ToArray() : [],
            rows = content is Grid rows ? rows.RowDefinitions.Select(row => new { height = row.Height.ToString(), actual = row.ActualHeight }).ToArray() : []
        };
        Require(contentCompletedLayout,
            "Original view content did not complete positive visual layout: " + JsonSerializer.Serialize(observations));
        return $"Actual UserControl measured/arranged without Show; desired={view.DesiredSize}; original TextBox binding retains the real DoubleRangeRule. No profile is loaded and no equipment is connected.";
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root))
            if (item is DependencyObject child)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException(message);
    }

    private static bool IsStockReference => Environment.GetEnvironmentVariable("NINA_PORTABLE_VIEWS_STOCK") == "1";
}
