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
using System.Windows.Media;
using System.Windows.Threading;

internal static partial class Program
{
    private static readonly List<object> checks = [];
    private static readonly List<object> loads = [];
    private static readonly List<object> nativeFailures = [];
    private static readonly List<object> observations = [];
    private static readonly List<string> resources = [];
    private static string phase = "startup";
    private static NINA.Profile.ProfileService? profile;
    private static X11Fixture? native;
    private static bool Stock => Environment.GetEnvironmentVariable("NINA_PORTABLE_VIEWS_STOCK") == "1";

    [STAThread]
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) => loads.Add(new { phase, identity = args.LoadedAssembly.FullName,
            path = args.LoadedAssembly.Location, context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly)?.Name });
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is DllNotFoundException or MarshalDirectiveException or BadImageFormatException)
                nativeFailures.Add(new { phase, exception = args.Exception.ToString() });
        };
        using var watchdog = new Timer(_ => { WriteResult(true, "90-second watchdog during " + phase); Environment.Exit(3); }, null, 90000, Timeout.Infinite);
        bool failed = false;
        string? error = null;
        try
        {
            Require(!Stock && OperatingSystem.IsLinux(), "This copied-stage source diagnostic is Linux-only.");
            Require(Environment.GetEnvironmentVariable("NINA_NATIVE_IMAGING_EXECUTION_AUTHORIZED") == "reviewed-whole-grid-popup-stage", "Native execution is held until root releases the coherent Grid+Popup family.");
            Require(NativeFormat is "Gray16" or "Rgb48", "Explicit separate native format process required.");
            Check("unchanged-managed-closure", VerifyClosure);
            phase = "explicit-native-bootstrap-before-WPF-resources";
            InitializeNative();
            Check("explicit-runtime-identity", VerifyRuntime);
            Check("isolated-original-profile-paths", ConfigureState);
            RunOriginalNativeImaging();
        }
        catch (Exception exception)
        {
            failed = true;
            error = exception.ToString();
            checks.Add(new { name = phase, status = "fail", detail = error });
        }
        finally
        {
            try { profile?.Release(); }
            catch (Exception exception) { failed = true; error += exception.ToString(); }
            native?.Dispose();
        }
        WriteResult(failed, error);
        return failed ? 1 : 0;
    }

    private static void WriteResult(bool failed, string? error)
    {
        var value = JsonSerializer.Serialize(new
        {
            probe = "original-nina-imagecontrolvm-native-imaging", ninaRevision = "4742afae0a09c79be772f23fcf163fb5852d2775",
            mode = "Linux-native-original-ImageControlVM-" + NativeFormat, expectedCheckCount = NativeRequiredChecks(NativeFormat).Length, expectedChecks = NativeRequiredChecks(NativeFormat), unreachedOrFailedChecks = NativeRequiredChecks(NativeFormat).Except(NativeCompleted).ToArray(),
            failed, error, phase, checks, loadedResourceDictionaries = resources, observations, assemblyLoads = loads, nativeFailures,
            scope = "Original ImageControlVM constructor with real ProfileService and four original mediators; original ImageControlView/ImageView BAML and full33 original dictionaries. Deterministic24MP public Image setter, genuine bound command and toggle binding checks. Actual private-Xvfb Window.Show and native input. No acquisition, PrepareImage, star processing, private setters or replacement templates.",
            os = RuntimeInformation.OSDescription,
            ninaAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name!.StartsWith("NINA", StringComparison.Ordinal))
                .Select(assembly => new { identity = assembly.FullName, path = assembly.Location,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Environment.GetEnvironmentVariable("NINA_IMAGE_VM_RESULT_PATH")!, value);
        Console.WriteLine(value);
    }

    private static void Check(string name, Action action)
    {
        phase = name;
        action();
        checks.Add(new { name, status = "pass" }); NativeCompleted.Add(name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializeNative()
    {
        Require(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")), "Private Xvfb DISPLAY is required.");
        native = new X11Fixture("little");
        Wpf.Portable.NativeHost.NativeWindowHost.InitializeForCurrentThread();
        observations.Add(new { stage = "native-bootstrap", display = Environment.GetEnvironmentVariable("DISPLAY"),
            settingsOrigin = native.SettingsOrigin, nativeShow = false, input = false });
    }

    private static void VerifyClosure()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Closure.json")));
        Require(manifest.RootElement.GetProperty("revision").GetString() == "4742afae0a09c79be772f23fcf163fb5852d2775", "NINA revision differs.");
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, file.GetProperty("outputPath").GetString()!));
            Require(path.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal), "Closure path escapes output.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.GetProperty("sha256").GetString(), "Original bytes differ: " + path);
            Require(AssemblyName.GetAssemblyName(path).FullName == file.GetProperty("identity").GetString(), "Original identity differs: " + path);
        }
    }

    private static void VerifyRuntime()
    {
        foreach (var name in new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework", "System.Windows.Extensions" })
        {
            var assembly = Assembly.Load(name);
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Multiple WPF type universes.");
            Require(Stock ? assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal)
                : Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Unexpected WPF runtime location.");
            observations.Add(new { runtime = assembly.FullName, path = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) });
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureState()
    {
        var state = Environment.GetEnvironmentVariable("NINA_PORTABLE_VIEWS_STATE")!;
        Require(Path.IsPathFullyQualified(state), "Isolated state path required.");
        Directory.CreateDirectory(state);
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = state;
        NINA.Profile.ProfileService.PROFILEFOLDER = Path.Combine(state, "Profiles");
    }

    private static void Settle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
}
