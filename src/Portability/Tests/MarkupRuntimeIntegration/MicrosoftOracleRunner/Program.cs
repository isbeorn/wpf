using System.CodeDom.Compiler;
using System.Collections;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FixtureControls;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var checks = new List<object>();
        var failed = false;
        var oracleVerified = false;
        object? view = null;
        using var origin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MicrosoftCompilerOracle.json")));
        void Check(string name, Func<string> action)
        {
            try { checks.Add(new { name, status = "pass", detail = action() }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
        Check("unchanged-microsoft-compiled-oracle", () =>
        {
            var detail = VerifyOracle(origin.RootElement);
            oracleVerified = true;
            return detail;
        });
        Check("actual-source-framework-identities", SourceAssemblies);
        if (oracleVerified)
        {
            Check("microsoft-compiled-usercontrol-and-connectors", () =>
            {
                view = ConstructView();
                return VerifyNames(view);
            });
        }
        else
            checks.Add(new { name = "microsoft-compiled-usercontrol-and-connectors", status = "blocked", detail = "Frozen Microsoft-produced binary verification failed." });
        if (view is not null)
            Check("microsoft-compiled-dictionary-and-control-template", () => VerifyDictionary(view));
        else
            checks.Add(new { name = "microsoft-compiled-dictionary-and-control-template", status = "blocked", detail = "Unchanged Microsoft-produced BAML view failed construction." });
        Check("portable-microsoft-baml-path-avoids-directwrite-interop", () =>
        {
            Require(AppDomain.CurrentDomain.GetAssemblies().All(assembly => assembly.GetName().Name != "DirectWriteForwarder"), "Portable Microsoft BAML execution loaded the Windows-only DirectWrite compiler contract.");
            return "Unchanged Microsoft-produced BAML execution did not load DirectWriteForwarder.";
        });
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "microsoft-produced-baml-source-framework-runtime", os = RuntimeInformation.OSDescription, scope = "Frozen unchanged SDK10 Microsoft-produced companion assembly loaded by the current-source app-local WPF runtime. The candidate is a binary reference and is never recompiled against the port.", origin = origin.RootElement, checks, candidateAssemblyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "FixtureControls") }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static string VerifyOracle(JsonElement origin)
    {
        var candidatePath = Path.Combine(AppContext.BaseDirectory, origin.GetProperty("fixtureAssembly").GetString()!);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidatePath)));
        Require(hash == origin.GetProperty("fixtureSha256").GetString(), "Microsoft-produced assembly bytes changed.");
        var assembly = Assembly.Load("FixtureControls");
        Require(assembly.Location == candidatePath, "Frozen Microsoft-produced assembly loaded from an unexpected location.");
        var type = assembly.GetType("FixtureControls.FixtureView", throwOnError: true)!;
        var generated = type.GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>() ?? throw new InvalidDataException("Frozen component has no compiler origin attribute.");
        Require(generated.Tool == "PresentationBuildTasks" && generated.Version == "10.0.12.0", "Frozen component does not identify the Microsoft SDK compiler.");
        Require(assembly.GetManifestResourceNames().Contains("FixtureControls.g.resources"), "Frozen compiled resources missing.");
        var resources = new List<string>();
        using var stream = assembly.GetManifestResourceStream("FixtureControls.g.resources")!;
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            Require(entry.Key is "fixtureview.baml" or "fixtureresources.baml", $"Unexpected Microsoft oracle resource: {entry.Key}");
            using var data = (Stream)entry.Value!;
            using var copy = new MemoryStream();
            data.CopyTo(copy);
            var bytes = copy.ToArray();
            Require(System.Text.Encoding.Unicode.GetString(bytes).Contains("MSBAML", StringComparison.Ordinal), $"Microsoft oracle resource lacks a genuine BAML header: {entry.Key}");
            resources.Add($"{entry.Key}: {bytes.Length} bytes, SHA256={Convert.ToHexString(SHA256.HashData(bytes))}");
        }
        Require(resources.Count == 2, "Expected both unchanged Microsoft-produced BAML resources.");
        return $"{assembly.FullName}: {assembly.Location}; SHA256={hash}; generated by {generated.Tool} {generated.Version}; {string.Join("; ", resources.Order())}.";
    }

    private static string SourceAssemblies() => string.Join("; ", new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework" }.Select(name =>
    {
        var assembly = Assembly.Load(name);
        var build = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Require(assembly.GetName().Version == new Version(10, 0, 0, 0) && build is not null && build.Contains("-portable.", StringComparison.Ordinal), $"Wrong framework build: {assembly.FullName}");
        Require(Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), $"Framework is not app-local: {assembly.Location}");
        return $"{assembly.FullName}; build={build}: {RuntimeAssemblyEvidence.Describe(assembly)}";
    })) + "; " + RuntimeAssemblyEvidence.ExtensionsAndHost();

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Keep WPF types out of the entry point so runtime loading failures can be reported as JSON.")]
    private static object ConstructView() => new FixtureView();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyNames(object instance)
    {
        var view = (FixtureView)instance;
        foreach (var name in new[] { "Metric", "Editor", "Increment", "Preview" })
        {
            var named = view.FindName(name);
            var field = typeof(FixtureView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
            Require(named is not null && ReferenceEquals(named, field), $"Microsoft-generated field was not connected: {name}");
        }
        view.InitializeComponent();
        return "Constructed unchanged Microsoft BAML; all four Microsoft-generated fields match the runtime namescope; repeated InitializeComponent succeeds.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyDictionary(object instance)
    {
        var view = (FixtureView)instance;
        var dictionary = new ResourceDictionary { Source = new Uri("/FixtureControls;component/fixtureresources.xaml", UriKind.Relative) };
        view.Resources.MergedDictionaries.Add(dictionary);
        Require(dictionary["FixtureAccent"] is System.Windows.Media.SolidColorBrush brush && brush.Color.ToString() == "#FF1177BB", "Microsoft-produced dictionary resource did not resolve.");
        var metric = (MetricControl)view.FindName("Metric");
        metric.Style = (Style)dictionary[typeof(MetricControl)];
        metric.Value = 9;
        Require(metric.ApplyTemplate(), "Microsoft-compiled control template did not instantiate.");
        var text = (TextBlock)metric.Template.FindName("PART_Value", metric);
        text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Require(text.Text == "9", "Microsoft-compiled template's templated-parent binding did not execute.");
        return "Loaded unchanged Microsoft-compiled component dictionary and control template; templated-parent binding reports Value=9.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
