using System.CodeDom.Compiler;
using System.Configuration;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static partial class Program
{
    private static readonly List<object> checks = [], observations = [], loads = [], dependencyFailures = [];
    private static readonly List<string> resources = [];
    private static string phase = "startup", mode = "", state = "", result = "";
    private static bool failed;
    private static string? error;
    private static NINA.Profile.ProfileService? profile;
    private static bool Stock
    {
        get
        {
#if NINA_STOCK_REFERENCE
            return true;
#else
            return false;
#endif
        }
    }
    private static Application? app;

    [STAThread]
    private static int Main(string[] args)
    {
        mode = Environment.GetEnvironmentVariable("NINA_RICHTEXT_CASE") ?? "unapproved";
        result = Environment.GetEnvironmentVariable("NINA_RICHTEXT_RESULT")!;
        Require(!string.IsNullOrWhiteSpace(result) && Path.IsPathFullyQualified(result), "Absolute owned result path required.");
        using var watchdog = new Timer(_ => { failed = true; error = "35-second watchdog during " + phase; WriteResult(); Environment.Exit(3); }, null, 35000, Timeout.Infinite);
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) => loads.Add(new { phase, identity = args.LoadedAssembly.FullName,
            path = args.LoadedAssembly.Location, context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly)?.Name });
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is DllNotFoundException or FileNotFoundException or BadImageFormatException or MarshalDirectiveException)
                dependencyFailures.Add(new { phase, exception = args.Exception.ToString() });
        };
        IDisposable? native = null;
        try
        {
            Require(!Stock || (args.Length == 1 && args[0] == "--private-noninput-desktop"), "Stock child requires the reviewed private-desktop launcher.");
            Require(mode == "caret-profile", "Explicit reviewed control workflow required.");
            Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS")), "Automatic startup hooks are forbidden; only the isolated provider type is reused.");
            Check("unchanged-160-original-closure", VerifyClosure);
            Check("single-runtime-type-universe", VerifyRuntime);
            Check("guarded-profile-and-log-paths", ConfigureState);
            Check("original-in-memory-provider-before-preference-reads", ConfigurePreferences);
            if (!Stock) native = InitializeNative();
            ConfigureEditorProfile();
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Check("original-33-resource-dictionaries", () =>
            {
                var sources = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ResourceSources.json")))!;
                Require(sources.Length == 33, "Original dictionary count differs.");
                foreach (var source in sources)
                {
                    if (source == "/NINA.WPF.Base;component/Resources/Styles/SplitButton.xaml")
                        MergeOriginal("/NINA.WPF.Base;component/Resources/Styles/Hyperlink.xaml");
                    if (source == "/NINA;component/Resources/Styles/Markdown.xaml")
                    {
                        Require(app.Resources["ApplicationHyperlinkStyle"] is Style, "Original Hyperlink must resolve before Markdown Source loading.");
                        observations.Add(new { stage = "pre-Markdown-prerequisite", originalHyperlinkStylePresent = true, loadedSources = resources.ToArray() });
                    }
                    MergeOriginal(source);
                }
                profile = (NINA.Profile.ProfileService)app.Resources["ProfileService"];

                void MergeOriginal(string source)
                {
                    observations.Add(new { stage = "original-resource-Source-load-begin", source, loadIndex = resources.Count });
                    var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                    app!.Resources.MergedDictionaries.Add(dictionary);
                    resources.Add(source);
                }
            });
            Check("original-consumer-Hyperlink-dictionary", () =>
            {
                const string hyperlink = "/NINA.WPF.Base;component/Resources/Styles/Hyperlink.xaml";
                var original = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ResourceSources.json")))!;
                Require(resources.Count == 34 && app.Resources.MergedDictionaries.Count == 34 && resources.Where(source => source != hyperlink).SequenceEqual(original), "Original33 relative order or completed34 closure differs.");
                Require(resources.IndexOf(hyperlink) + 1 == resources.IndexOf("/NINA.WPF.Base;component/Resources/Styles/SplitButton.xaml"), "Original App Hyperlink-before-SplitButton order missing.");
                var originalHyperlink = (Style)app.Resources["ApplicationHyperlinkStyle"];
                var originalMarkdown = (Style)app.Resources["MarkdownStyle"];
                var nestedHyperlink = (Style)originalMarkdown.Resources[typeof(System.Windows.Documents.Hyperlink)];
                Require(ReferenceEquals(nestedHyperlink.BasedOn, originalHyperlink), "Original Markdown Hyperlink BasedOn did not capture the original application style.");
                observations.Add(new { stage = "original-Markdown-Hyperlink-prerequisite", totalDictionaries = resources.Count, actualBasedOnIdentity = true, loadOrder = resources.ToArray() });
            });
            Check("actual-isolated-profile", () =>
            {
                Require(!Directory.EnumerateFileSystemEntries(NINA.Profile.ProfileService.PROFILEFOLDER).Any(), "Profile state is not fresh.");
                Require(profile!.TryLoad(string.Empty) && profile.Profiles.Count == 1, "Original isolated profile failed.");
            });
            app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try { RunEditorControls(native); }
                catch (Exception exception) { RecordFailure(exception); }
                finally
                {
                    profile?.Release();
                    if (!app.Dispatcher.HasShutdownStarted) app.Shutdown();
                }
            }));
            app.Run();
        }
        catch (Exception exception) { RecordFailure(exception); }
        finally { profile?.Release(); native?.Dispose(); }
        WriteResult();
        return failed ? 1 : 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IDisposable InitializeNative()
    {
#if NINA_STOCK_REFERENCE
        throw new InvalidOperationException("Stock native bootstrap must not run.");
#else
        Require(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")), "Owned Xvfb DISPLAY required.");
        var fixture = new X11Fixture("little");
        Wpf.Portable.NativeHost.NativeWindowHost.InitializeForCurrentThread();
        observations.Add(new { stage = "native-bootstrap-before-WPF", display = Environment.GetEnvironmentVariable("DISPLAY"), fixture.SettingsOrigin });
        return fixture;
#endif
    }

    private static void ConfigureState()
    {
        state = Path.GetFullPath(Environment.GetEnvironmentVariable("NINA_RICHTEXT_STATE")!);
        string allowed = Path.GetFullPath(Environment.GetEnvironmentVariable("NINA_RICHTEXT_ALLOWED_ROOT")!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Require(state.StartsWith(allowed, Stock ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "State escapes owned root.");
        Require(result.StartsWith(allowed, Stock ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Result escapes owned root.");
        Require(!Directory.Exists(state), "State reuse is forbidden.");
        Directory.CreateDirectory(state);
        File.WriteAllText(Path.Combine(state, "owner.json"), "{\"ninaRevision\":\"4742afae0a09c79be772f23fcf163fb5852d2775\"}");
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = state;
        NINA.Profile.ProfileService.PROFILEFOLDER = Path.Combine(state, "Profiles");
        Directory.CreateDirectory(NINA.Profile.ProfileService.PROFILEFOLDER);
        observations.Add(new { stage = "owned-state", state, profiles = NINA.Profile.ProfileService.PROFILEFOLDER, logs = Path.Combine(state, "Logs") });
    }

    private static void ConfigurePreferences()
    {
        var settingsType = typeof(NINA.ViewModel.ProfileSelectVM).Assembly.GetType("NINA.Properties.Settings", true)!;
        var settings = (ApplicationSettingsBase)settingsType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var providerType = typeof(StartupHook).GetNestedType("InMemorySettingsProvider", BindingFlags.NonPublic)!;
        var provider = (SettingsProvider)Activator.CreateInstance(providerType, true)!;
        provider.Initialize("StartupBenchmark", new System.Collections.Specialized.NameValueCollection());
        settings.Providers.Clear(); settings.Providers.Add(provider);
        foreach (SettingsProperty property in settings.Properties) property.Provider = provider;
        settings.Reload();
        settings["UpdateSettings"] = false;
        settings["UseSavedProfileSelection"] = false;
        settings["DatabaseLocation"] = Path.Combine(state, "unused-NINA.sqlite");
        Require(settings.Providers.Count == 1 && settings.Properties.Cast<SettingsProperty>().All(property => ReferenceEquals(property.Provider, provider)), "Unexpected user-settings provider.");
        settings["ApplicationFontFamily"] = new System.Windows.Media.FontFamily(new Uri(AppContext.BaseDirectory), "./Fonts/#DejaVu Sans");
        observations.Add(new { stage = "preference-isolation", provider = providerType.FullName, beforeFirstPreferencePropertyRead = true,
            startupHookInitializeInvoked = false, checkboxToggle = false, preferenceFilePersistence = "OPEN; provider stores no files" });
    }

    private static void VerifyClosure()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Closure.json")));
        Require(manifest.RootElement.GetProperty("revision").GetString() == "4742afae0a09c79be772f23fcf163fb5852d2775", "NINA revision differs.");
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray(); Require(files.Length == 160, "Original closure count differs.");
        foreach (var file in files)
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, file.GetProperty("outputPath").GetString()!));
            Require(path.StartsWith(AppContext.BaseDirectory, Stock ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Closure path escapes output.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.GetProperty("sha256").GetString(), "Original bytes differ: " + path);
            Require(AssemblyName.GetAssemblyName(path).FullName == file.GetProperty("identity").GetString(), "Original identity differs.");
        }
    }

    private static void VerifyRuntime()
    {
        foreach (string name in new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework", "System.Windows.Extensions" })
        {
            var assembly = Assembly.Load(name);
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Multiple framework type universes.");
            Require(Stock ? assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal) : Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Unexpected framework origin.");
            observations.Add(new { runtime = assembly.FullName, path = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) });
        }
    }

    private static void ObserveBaml(Type type, string key)
    {
        var generated = type.GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>();
        Require(generated?.Tool == "PresentationBuildTasks" && generated?.Version == "10.0.12.0", "Original BAML compiler differs.");
        using var stream = type.Assembly.GetManifestResourceStream("NINA.g.resources")!;
        using var reader = new ResourceReader(stream);
        foreach (System.Collections.DictionaryEntry entry in reader)
            if ((string)entry.Key == key)
            {
                using var bytes = new MemoryStream(); ((Stream)entry.Value!).CopyTo(bytes);
                observations.Add(new { stage = "original-BAML", key, length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())), compiler = generated!.Tool, generated.Version }); return;
            }
        throw new InvalidDataException("Original BAML absent.");
    }

    private static void Check(string name, Action action) { phase = name; action(); checks.Add(new { name, status = "pass" }); }
    private static void RecordFailure(Exception exception) { failed = true; error = exception.ToString(); checks.Add(new { name = phase, status = "fail", error }); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Settle() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void WriteResult()
    {
        var required = EditorRequiredChecks;
        var passed = checks.Select(item => JsonSerializer.SerializeToElement(item)).Where(item => item.GetProperty("status").GetString() == "pass").Select(item => item.GetProperty("name").GetString()).ToHashSet();
        var missing = required.Where(name => !passed.Contains(name)).ToArray();
        if (missing.Length != 0 && !failed) { failed = true; error = "Incomplete workflow: " + string.Join(", ", missing); }
        var json = JsonSerializer.Serialize(new { probe = "original-NINA-richtext-control", mode, failed, error, phase,
            ninaRevision = "4742afae0a09c79be772f23fcf163fb5852d2775", framework = Stock ? "stock-WindowsDesktop10.0.12" : "root-release-pending-portable-control-diagnostic",
            checks, expectedCheckCount = required.Length, unreachedOrFailedChecks = missing, loadedResourceDictionaries = resources, observations, assemblyLoads = loads, dependencyFailures,
            nativeInput = false, preferenceFilePersistence = "OPEN", fullApplicationStartup = false, packageQualification = false,
            fullPluginsViewLoaded = false, catalogInvoked = false, externalPluginQualification = false,
            claim = "standalone actual MdXaml/AvalonEdit controls plus separately detached original PluginsView BAML" }, new JsonSerializerOptions { WriteIndented = true });
        json = json.Replace("unchanged-160-original-closure", "declared-160-member-runtime-closure", StringComparison.Ordinal); File.WriteAllText(result, json); Console.WriteLine(json);
    }
}

