using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PackageConsumer;

namespace CompleteViewWindowsReference;

internal static class Program
{
    private const string FrozenMicrosoftSha256 = "60D2A40A5FEBA9D901821A95EE8EDA5448A3F777C12C1A42C44B90B212F1ED48";

    [STAThread]
    private static int Main(string[] args)
    {
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var checks = new ConcurrentQueue<Check>();
        var desktop = new WindowsDesktop(Wait);
        bool interactive = args.Contains("--interactive", StringComparer.Ordinal);
        bool completed = false;
        JsonElement? fixtureOrigin = null;
        using var watchdog = new Timer(_ =>
        {
            checks.Enqueue(new("watchdog", "fail", "The stock Windows workflow exceeded 60 seconds."));
            WriteResults();
            Environment.Exit(2);
        }, null, interactive ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            Check("stock-runtime-and-input-origin", VerifyInputs);
            if (checks.All(check => check.status == "pass"))
            {
                CompleteViewWorkflow.Run(desktop, interactive, Check, Wait);
                completed = true;
            }
        }
        catch (Exception exception) { checks.Enqueue(new("stock-workflow-unhandled", "fail", exception.ToString())); }
        finally
        {
            if (Application.Current is { } application)
                foreach (Window window in application.Windows.Cast<Window>().ToArray()) Check("stock-window-cleanup", window.Close);
        }
        Check("all-checks-completed", () =>
        {
            Require(completed, "The shared workflow stopped early.");
            string[] expected = CompleteViewWorkflow.RequiredChecks(interactive).Append("stock-runtime-and-input-origin").ToArray();
            Require(checks.Count == expected.Length && new HashSet<string>(checks.Select(check => check.name), StringComparer.Ordinal).SetEquals(expected),
                "The stock result omitted, duplicated or added a required check.");
        });
        WriteResults();
        return checks.All(check => check.status == "pass") ? 0 : 1;

        void Check(string name, Action action)
        {
            try { action(); checks.Enqueue(new(name, "pass", "Observed using the shared workflow and stock Windows WPF.")); }
            catch (Exception exception) { checks.Enqueue(new(name, "fail", exception.ToString())); }
        }

        void VerifyInputs()
        {
            Require(OperatingSystem.IsWindows(), "The reference requires Windows.");
            foreach (var assembly in new[] { typeof(Window).Assembly, typeof(Visual).Assembly, typeof(DependencyObject).Assembly })
            {
                Require(Path.GetDirectoryName(assembly.Location)!.EndsWith("Microsoft.WindowsDesktop.App" + Path.DirectorySeparatorChar + "10.0.12", StringComparison.OrdinalIgnoreCase),
                    "Stock WindowsDesktop 10.0.12 was not loaded: " + assembly.Location);
                Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "A framework assembly used another load context.");
            }
            var metadata = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(item => item.Key, item => item.Value!);
            foreach (string source in new[] { "CompleteViewWorkflow.cs", "WorkflowResources.xaml", "WindowsDesktop.cs", "Program.cs", "app.manifest" })
                Require(Hash(Path.Combine(AppContext.BaseDirectory, "provenance", source)) == metadata[source], "A copied source differs from the compiled input: " + source);
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "FixtureControls.dll");
            string fixtureHash = Hash(fixturePath);
            Require(fixtureHash == metadata["FixtureInputSha256"], "FixtureControls differs from the exact compiler input reference.");
            Require(Hash(Path.Combine(AppContext.BaseDirectory, "CompleteViewOrigin.json")) == metadata["FixtureOriginSha256"], "The compiler-origin manifest differs from the exact build input.");
            using var origin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CompleteViewOrigin.json")));
            fixtureOrigin = origin.RootElement.Clone();
            Require(origin.RootElement.GetProperty("origin").GetString() == metadata["FixtureOrigin"], "The selected compiler origin differs from its manifest.");
            Require(origin.RootElement.GetProperty("fixtureSha256").GetString() == fixtureHash, "The per-origin fixture hash differs.");
            if (metadata["FixtureOrigin"] == "Microsoft") Require(fixtureHash == FrozenMicrosoftSha256, "The frozen Microsoft fixture does not match the trusted original hash.");
        }

        void WriteResults()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic &&
                (assembly.GetName().Name is "App" or "FixtureControls" or "WindowsBase" or "PresentationCore" or "PresentationFramework" or "System.Xaml"));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                probe = "complete-view-stock-windows-reference", os = RuntimeInformation.OSDescription,
                launchMode = args.Contains("--ordinary-apphost", StringComparer.Ordinal) ? "ordinary-apphost" : "unspecified",
                renderMode = RenderOptions.ProcessRenderMode.ToString(),
                scope = "Stock WindowsDesktop 10.0.12; OS-injected SendInput on owned foreground windows; synthetic owned-window WM_DPICHANGED reference, not physical monitor transitions",
                compiledInputs = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(item => item.Key, item => item.Value),
                assemblies = assemblies.Select(assembly => new { identity = assembly.FullName, path = assembly.Location,
                    sha256 = Hash(assembly.Location), defaultLoadContext = AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default }),
                fixtureOrigin,
                pixelReadCount = desktop.PixelReadCount, pixels = desktop.Pixels.ToArray(), dpi = desktop.DpiObservations.ToArray(),
                focus = desktop.FocusObservations.ToArray(), typing = desktop.TypingObservations.ToArray(),
                clientSizes = desktop.ClientSizeObservations.ToArray(), checks
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Wait(Func<bool> ready, Func<string>? detail)
    {
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => { if (ready() || elapsed.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { if (!ready()) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Require(ready(), "The Windows observable result did not arrive: " + detail?.Invoke());
    }

    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed record Check(string name, string status, string detail);
}
