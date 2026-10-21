using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Portable.NativeHost;

internal static class PortableEntryDiagnosticsCases
{
    internal static int Run()
    {
        bool expected = Environment.GetCommandLineArgs().Contains("--expect-enabled", StringComparer.Ordinal);
        var checks = new List<object>();
        var observations = new List<object>();
        var originalError = Console.Error;
        using var captured = new StringWriter(CultureInfo.InvariantCulture);
        using var watchdog = new Timer(_ => Environment.Exit(3), null, TimeSpan.FromSeconds(35), Timeout.InfiniteTimeSpan);
        Window? window = null;
        X11Fixture? native = null;
        int exit = 1;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                native = new X11Fixture("little");
                NativeWindowHost.InitializeForCurrentThread();
            }
            Console.SetError(captured);
            Configure();
            var target = new TextBlock { Name = "PortableDiagnosticsTarget" };
            var model = new DiagnosticModel();
            target.SetBinding(TextBlock.TextProperty, new Binding("UnavailableProperty") { Source = model });
            Drain();
            Require(BindingOperations.GetBindingExpression(target, TextBlock.TextProperty)?.Status == BindingStatus.PathError,
                "The real binding did not encounter the missing property.");
            string bindingText = captured.ToString();
            observations.Add(new { stage = "missing-binding", status = BindingOperations.GetBindingExpression(target, TextBlock.TextProperty)!.Status.ToString(), text = bindingText });
            Require(expected ? bindingText.Contains("UnavailableProperty", StringComparison.Ordinal) &&
                bindingText.Contains(nameof(DiagnosticModel), StringComparison.Ordinal) &&
                bindingText.Contains("PortableDiagnosticsTarget", StringComparison.Ordinal) &&
                bindingText.Contains("target property is 'Text'", StringComparison.Ordinal) : bindingText.Length == 0,
                "Startup configuration did not produce the expected binding path/source/target/property diagnostic or default silence.");
            checks.Add(new { name = "real-invalid-binding-diagnostic", status = "pass" });

            target.SetBinding(TextBlock.TextProperty, new Binding(nameof(DiagnosticModel.Value)) { Source = model });
            Drain();
            Require(target.Text == "binding restored" && BindingOperations.GetBindingExpression(target, TextBlock.TextProperty)?.Status == BindingStatus.Active,
                "A valid binding did not recover after the failed path.");
            checks.Add(new { name = "valid-binding-recovery", status = "pass" });

            Configure();
            captured.GetStringBuilder().Clear();
            target.SetBinding(TextBlock.TextProperty, new Binding("UnavailablePropertyAgain") { Source = model });
            Drain();
            string repeatedText = captured.ToString();
            int messages = repeatedText.Split('\n').Count(line => line.Contains("UnavailablePropertyAgain", StringComparison.Ordinal) && line.Contains("target property is 'Text'", StringComparison.Ordinal));
            Require(messages == (expected ? 1 : 0), "Repeated startup configuration duplicated or lost the real binding diagnostic.");
            observations.Add(new { stage = "repeated-configuration", messages, text = repeatedText });
            target.SetBinding(TextBlock.TextProperty, new Binding(nameof(DiagnosticModel.Value)) { Source = model });
            Drain();
            Require(target.Text == "binding restored", "Repeated diagnostic setup prevented valid binding restoration.");
            checks.Add(new { name = "repeat-setup-single-output", status = "pass" });

            window = new Window { Title = "Portable entry diagnostics", Width = 160, Height = 80, ShowActivated = false, ShowInTaskbar = false, Content = target };
            window.Show();
            Wait(() => target.IsLoaded && window.IsLoaded);
            captured.GetStringBuilder().Clear();
            const string missingKey = "PortableDiagnosticsMissingBrush";
            Require(target.TryFindResource(missingKey) is null, "The missing resource unexpectedly exists.");
            string resourceText = captured.ToString();
            Require(expected ? resourceText.Contains("Resource not found", StringComparison.Ordinal) && resourceText.Contains(missingKey, StringComparison.Ordinal) : resourceText.Length == 0,
                "The genuine loaded resource lookup did not produce the expected key diagnostic or default silence.");
            observations.Add(new { stage = "loaded-resource", target.IsLoaded, resourceKey = missingKey, text = resourceText });
            var resource = new object();
            target.Resources.Add(missingKey, resource);
            Require(ReferenceEquals(target.FindResource(missingKey), resource), "Resource lookup did not recover after adding the missing key.");
            target.Resources.Remove(missingKey);
            Require(target.TryFindResource(missingKey) is null, "Resource removal did not restore the missing-key state.");
            target.Resources.Add(missingKey, resource);
            Require(ReferenceEquals(target.FindResource(missingKey), resource), "Resource restoration failed.");
            checks.Add(new { name = "loaded-resource-key-and-recovery", status = "pass" });

            window.Close();
            Drain();
            Require(PresentationSource.FromVisual(target) is null && !window.IsVisible, "The diagnostic window retained its presentation source after Close.");
            checks.Add(new { name = "normal-close-source-cleanup", status = "pass" });
            exit = 0;
        }
        catch (Exception exception)
        {
            checks.Add(new { name = "diagnostics-boundary", status = "fail", detail = exception.ToString() });
        }
        finally
        {
            window?.Close();
            Console.SetError(originalError);
            native?.Dispose();
        }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "nina-portable-entry-diagnostics", expectedEnabled = expected, checks, observations,
            entrySource = typeof(NINA.PortableEntry).Assembly.Location, framework = typeof(Window).Assembly.Location }, new JsonSerializerOptions { WriteIndented = true }));
        return exit;
    }

    private static void Configure()
    {
        // The retained old entry has no setup method, allowing a causal missing-output run.
        typeof(NINA.PortableEntry).GetMethod("ConfigureDiagnostics", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Wait(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            Require(watch.Elapsed < TimeSpan.FromSeconds(5), "The genuine diagnostic window did not finish loading.");
            Drain();
            Thread.Sleep(10);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class DiagnosticModel
    {
        public string Value => "binding restored";
    }
}
