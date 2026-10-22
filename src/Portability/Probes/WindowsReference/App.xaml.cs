using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace WindowsReference;

public partial class App : Application
{
    internal sealed record CheckResult(string Name, string Status, string Detail);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var checks = new List<CheckResult>();
        if (e.Args is ["--case", string caseName])
        {
            try
            {
                ReferenceCases.Run(caseName, checks);
            }
            catch (Exception exception)
            {
                checks.Add(new(caseName, "fail", exception.ToString()));
            }
        }
        else if (e.Args.Length == 0)
        {
            foreach (string childCase in ReferenceCases.Names)
            {
                try
                {
                    await RunChild(childCase, checks).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    checks.Add(new(childCase + ".launch", "fail", exception.ToString()));
                }
            }
        }
        else
        {
            checks.Add(new("arguments", "fail", "Expected no arguments or --case <name>."));
        }

        string[] frameworkNames = ["WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml"];
        var assemblies = frameworkNames.Select(name => Assembly.Load(name)).Select(assembly => new
        {
            name = assembly.GetName().Name,
            version = assembly.GetName().Version?.ToString(),
            publicKeyToken = Convert.ToHexString(assembly.GetName().GetPublicKeyToken() ?? []),
            fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion,
            path = assembly.Location
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "WindowsReference",
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            scope = "Windows stock WPF 10.0.12 reference fixture; not this fork build or a whole NINA baseline",
            runtime = RuntimeInformation.FrameworkDescription,
            assemblies,
            checks = checks.Select(check => new { name = check.Name, status = check.Status, detail = check.Detail })
        }));
        int exitCode = checks.Any(check => check.Status != "pass") ? 1 : 0;
        Environment.ExitCode = exitCode;
        if (!Dispatcher.HasShutdownStarted)
        {
            Shutdown(exitCode);
        }
    }

    private static async Task RunChild(string name, List<CheckResult> checks)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--case");
        start.ArgumentList.Add(name);
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().ConfigureAwait(false);
            checks.Add(new(name + ".watchdog", "fail", "Child exceeded 20 seconds and was terminated."));
            return;
        }

        string stdout = await output.ConfigureAwait(false);
        string stderr = await error.ConfigureAwait(false);
        try
        {
            using JsonDocument result = JsonDocument.Parse(stdout);
            JsonElement results = result.RootElement.GetProperty("checks");
            if (results.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Child returned no checks.");
            }
            foreach (JsonElement check in results.EnumerateArray())
            {
                checks.Add(new(check.GetProperty("name").GetString()!,
                    check.GetProperty("status").GetString()!, check.GetProperty("detail").GetString()!));
            }
            if (child.ExitCode != 0 || stderr.Length != 0)
            {
                checks.Add(new(name + ".process", "fail", $"Exit {child.ExitCode}; stderr: {stderr}"));
            }
        }
        catch (Exception exception)
        {
            checks.Add(new(name + ".result", "fail", $"Exit {child.ExitCode}: {exception.Message}; stdout: {stdout}; stderr: {stderr}"));
        }
    }
}
