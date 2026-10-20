using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

internal static partial class Program
{
    private static readonly object evidenceLock = new();
    private static readonly List<object> checks = [], observations = [], loads = [], dependencyFailures = [];
    private static readonly List<string> resources = [];
    private static string phase = "startup", state = "", result = "";
    private static bool failed;
    private static string? error;
    private static Application? app;
    private static NINA.Profile.ProfileService? profile;
    private static NINA.WPF.Base.Interfaces.ViewModel.IFramingAssistantVM? vm;
    private static Window? window;
    private static readonly string[] required = ["coherent-source-adapted-consumer", "released-whole-runtime-default-ALC",
        "guarded-owned-paths-before-native-or-profile-init", "original-in-memory-preference-provider",
        "original-offline-catalog-initializer-and-nonempty-queries", "original-34-dictionaries",
        "real-public-17-argument-constructor", "original-view-and-overlay-BAML", "constructor-cache-and-hips-readiness",
        "real-static-load-and-annotator", "original-imageview-static-zoom-pair", "field-size-and-rotation-pairs",
        "mosaic-counts-and-bound-overlap-pairs", "managed-drag-forward-and-reverse", "real-dynamic-zoom-boundaries",
        "owner-and-background-public-setcoordinates", "real-layout-overlay-and-raster", "public-disposal-and-source-detach", "real-vm-cache-save-and-thumbnails", "real-vm-cache-source-reload", "cache-disabled-fov-miss-recovery"];

    [STAThread]
    private static int Main(string[] args)
    {
        result = Environment.GetEnvironmentVariable("NINA_FRAMING_RESULT") ?? "";
        if (!Path.IsPathFullyQualified(result)) { Console.Error.WriteLine("Owned absolute result path required; not initialized."); return 2; }
        string preparation = Path.GetFullPath(Environment.GetEnvironmentVariable("NINA_FRAMING_PREPARATION")!);
        string evidenceRoot = Path.Combine(preparation, "isolated-runs") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(result).StartsWith(evidenceRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(result)))
        { Console.Error.WriteLine("Result path is outside existing owned run evidence; not initialized."); return 2; }
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => { lock (evidenceLock) loads.Add(new { phase,
            identity = e.LoadedAssembly.FullName, path = e.LoadedAssembly.Location,
            context = AssemblyLoadContext.GetLoadContext(e.LoadedAssembly)?.Name }); };
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException
                || e.Exception is PlatformNotSupportedException && (e.Exception.StackTrace?.Contains("System.Drawing", StringComparison.Ordinal) ?? false))
                lock (evidenceLock) dependencyFailures.Add(new { phase, error = e.Exception.ToString() });
        };
        IDisposable? native = null;
        using var watchdog = new Timer(_ => { failed = true; error = "90-second watchdog in " + phase; try { WriteResult(); } finally { Environment.Exit(3); } }, null, 90000, Timeout.Infinite);
        try
        {
            VerifyPrivateLinux(args);
            Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS")), "Startup hooks forbidden.");

            Check(required[0], VerifyClosure);
            Check(required[1], VerifyRuntime);
            Check(required[2], ConfigureState);
            Check(required[3], ConfigurePreferences);
            Check(required[4], InitializeCatalog);
            native = InitializeNative();
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.DispatcherUnhandledException += (_, e) => { e.Handled = true; CaptureFirstGeometryFault(e.Exception); Fail(e.Exception); app.Shutdown(1); };
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { RunFraming(); }
                catch (Exception e) { Fail(e); }
                finally
                {
                    try { window?.Close(); vm?.Dispose(); vm = null; profile?.Release(); }
                    catch (Exception e) { Fail(e); }
                    app.Shutdown(failed ? 1 : 0);
                }
            }));
            app.Run();
        }
        catch (Exception e) { Fail(e); }
        finally { native?.Dispose(); }
        WriteResult();
        return failed ? 1 : 0;
    }

    private static void VerifyClosure() => VerifyReleasedInputs();
    private static void VerifyRuntime() => VerifyReleasedRuntime();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureState()
    {
        string allowed = Path.GetFullPath(Environment.GetEnvironmentVariable("NINA_FRAMING_ALLOWED_ROOT")!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        state = Path.GetFullPath(Environment.GetEnvironmentVariable("NINA_FRAMING_STATE")!);
        Require(state.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(state), "Fresh owned state required.");
        Directory.CreateDirectory(state);
        string catalogRoot = Owned(Path.Combine(state, "catalog")) + Path.DirectorySeparatorChar;
        Environment.SetEnvironmentVariable("localappdata", catalogRoot, EnvironmentVariableTarget.Process);
        string exactDatabase = Owned(Path.Combine(state, "NINA", "NINA.sqlite"));
        Require(!exactDatabase.Contains('%'), "Default DB expansion failed.");
        Owned(exactDatabase);
        Directory.CreateDirectory(Path.GetDirectoryName(exactDatabase)!);
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = Owned(Path.Combine(state, "NINA"));
        NINA.Profile.ProfileService.PROFILEFOLDER = Owned(Path.Combine(NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, "Profiles"));
        foreach (string path in new[] { NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, NINA.Profile.ProfileService.PROFILEFOLDER,
            Owned(Path.Combine(state, "cache")), Owned(Path.Combine(NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, "Logs")) }) Directory.CreateDirectory(path);
        observations.Add(new { stage = "paths-before-initialization", state, catalogRoot, exactDatabase,
            temp = NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, profiles = NINA.Profile.ProfileService.PROFILEFOLDER,
            logs = Owned(Path.Combine(NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, "Logs")), cache = Owned(Path.Combine(state, "cache")),
            databasePath = "Already-adapted original Astrometry Linux default uses CoreUtil.APPLICATIONTEMPPATH/NINA.sqlite.", environmentScope = "child-process-only" });
    }

    private static void ConfigurePreferences()
    {
        Type type = typeof(NINA.View.FramingAssistantView).Assembly.GetType("NINA.Properties.Settings", true)!;
        var settings = (ApplicationSettingsBase)type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        Type providerType = typeof(StartupHook).GetNestedType("InMemorySettingsProvider", BindingFlags.NonPublic)!;
        var provider = (SettingsProvider)Activator.CreateInstance(providerType, true)!;
        provider.Initialize("StartupBenchmark", new());
        settings.Providers.Clear(); settings.Providers.Add(provider);
        foreach (SettingsProperty property in settings.Properties) property.Provider = provider;
        settings.Reload(); settings["UpdateSettings"] = false;
        settings["DatabaseLocation"] = Owned(Path.Combine(NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH, "NINA.sqlite"));
        Require(settings.Properties.Cast<SettingsProperty>().All(p => ReferenceEquals(p.Provider, provider)), "Nonisolated user settings provider.");
    }

    private static void InitializeCatalog()
    {
        using var context = new NINA.Astrometry.DatabaseInteraction().GetContext();
        context.Database.Initialize(false);
        var counts = new Dictionary<string, long>();
        foreach (string table in new[] { "dsodetail", "constellationstar", "constellation", "constellationboundaries", "hipsskymaps" })
        {
            long count = context.Database.SqlQuery<long>("SELECT COUNT(*) FROM " + table).Single();
            Require(count > 0, "Original initialized catalog is empty: " + table);
            counts[table] = count;
        }
        observations.Add(new { stage = "original-offline-catalog", counts,
            userVersion = context.Database.SqlQuery<int>("PRAGMA user_version").Single(), initializerDisabled = false });
    }

    private static string Owned(string path)
    {
        string full = Path.GetFullPath(path);
        Require(full.StartsWith(state.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "State path escapes owned namespace: " + full);
        return full;
    }
    private static void Check(string name, Action action)
    {
        phase = name; action();
        lock (evidenceLock) Require(dependencyFailures.Count == 0, "Reached native dependency failure; stop before further scenarios.");
        checks.Add(new { name, status = "pass" });
    }
    private static void Fail(Exception e) { if (failed) return; failed = true; error = e.ToString(); lock (evidenceLock) checks.Add(new { name = phase, status = "fail", error }); }
    private static void Require(bool condition, string message) { if (failed) throw new DiagnosticAbortException(); if (!condition) throw new InvalidDataException(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void WriteResult()
    {
        lock (evidenceLock)
        {
            string[] passed = checks.Select(x => JsonSerializer.SerializeToElement(x)).Where(x => x.GetProperty("status").GetString() == "pass").Select(x => x.GetProperty("name").GetString()!).ToArray();
            string[] missing = required.Except(passed).ToArray();
            if (missing.Length != 0) failed = true;
            string json = JsonSerializer.Serialize(new { probe = "original-NINA-framing-and-mosaic", ninaRevision = "4742afae0a09c79be772f23fcf163fb5852d2775",
                failed, error, phase, checks, expectedCheckCount = required.Length, unreachedOrFailedChecks = missing, resources, observations, loads, dependencyFailures,
                scope = "Private Linux Xvfb with the released coherent source-adapted consumer and whole runtime family. Original VM/annotator/catalog/XAML, deterministic public survey and nighttime services. Managed commands and routed Button.Click only, no native input or hardware/network.",
                backgroundLifetimeGap = "Original constructor Task.Run and resize async work have no public completion/cancellation contract; readiness predicates precede disposal, process isolation bounds remaining lifetime.",
                os = RuntimeInformation.OSDescription }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(result, json); Console.WriteLine(json);
        }
    }
    private static string DesktopName()
    {
        char[] buffer = new char[256];
        Require(GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, buffer, buffer.Length * 2, out _), "Cannot verify private desktop.");
        return new string(buffer, 0, Array.IndexOf(buffer, '\0'));
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, [Out] char[] buffer, int length, out int needed);
}
