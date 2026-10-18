using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class ApplicationRunCases
{
    private static readonly string[] Scenarios =
    [
        "startup-exit-code",
        "window-last-close",
        "main-window-close",
        "explicit-shutdown",
        "shutdown-before-run",
        "affinity-validation",
        "exit-exception-cleanup"
    ];

    internal static IEnumerable<ApplicationRunResult> RunAll()
    {
        foreach (string scenario in Scenarios)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--application-run");
            start.ArgumentList.Add(scenario);
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                yield return new("application-run-" + scenario, "fail", "Application child exceeded 15 seconds.");
                continue;
            }
            yield return new("application-run-" + scenario, process.ExitCode == 0 ? "pass" : "fail",
                output.GetAwaiter().GetResult().Trim() + error.GetAwaiter().GetResult().Trim());
        }
    }

    internal static int RunChild(string scenario, Action? initialize = null)
    {
        int result = 1;
        var thread = new Thread(() =>
        {
            var events = new List<string>();
            try
            {
                initialize?.Invoke();
                Execute(scenario, events);
                Console.WriteLine(JsonSerializer.Serialize(new { scenario, status = "pass", assembly = typeof(Application).Assembly.Location, events }));
                result = 0;
            }
            catch (Exception exception)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { scenario, status = "fail", assembly = typeof(Application).Assembly.Location, events, detail = exception.ToString() }));
            }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(12))) return 1;
        return result;
    }

    private static void Execute(string scenario, List<string> events)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int owner = Environment.CurrentManagedThreadId;
        int startupCount = 0, exitCount = 0;
        app.Startup += (_, _) =>
        {
            Require(Environment.CurrentManagedThreadId == owner && app.Dispatcher.CheckAccess(), "Startup lost dispatcher ownership.");
            startupCount++;
            events.Add("startup");
        };
        app.Exit += (_, _) => { exitCount++; events.Add("exit"); };
        app.Dispatcher.ShutdownStarted += (_, _) => events.Add("dispatcher-started");
        app.Dispatcher.ShutdownFinished += (_, _) => events.Add("dispatcher-finished");
        int code;
        switch (scenario)
        {
            case "startup-exit-code":
                app.Startup += (_, args) =>
                {
                    Require(args.Args.Contains(scenario), "Startup arguments did not reach the application.");
                    Expect<InvalidOperationException>(() => new Application());
                    Expect<InvalidOperationException>(() => app.Run());
                    app.Shutdown(17);
                    app.Shutdown(99);
                };
                app.Exit += (_, args) =>
                {
                    Require(args.ApplicationExitCode == 17, "Repeated Shutdown replaced the first exit code.");
                    args.ApplicationExitCode = 31;
                };
                code = app.Run();
                Require(code == 31 && Environment.ExitCode == 31, "Exit override did not reach Run and Environment.ExitCode.");
                break;
            case "window-last-close":
            {
                app.ShutdownMode = ShutdownMode.OnLastWindowClose;
                Window main = NewWindow(), other = NewWindow();
                int closed = 0;
                bool cancelLastClose = true;
                main.Closed += (_, _) => closed++;
                other.Closing += (_, args) => args.Cancel = cancelLastClose;
                other.Closed += (_, _) => closed++;
                app.Startup += (_, _) =>
                {
                    Require(app.MainWindow == main && app.Windows.Count == 2, "Run(window) did not retain the constructed main window and collection.");
                    other.Show();
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        Require(main.IsVisible, "Run(window) did not show its window after Startup.");
                        main.Close();
                        Require(exitCount == 0 && app.Windows.Count == 1 && !app.Dispatcher.HasShutdownStarted,
                            "Closing one of two windows stopped OnLastWindowClose.");
                        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            other.Close();
                            Require(other.IsVisible && exitCount == 0 && !app.Dispatcher.HasShutdownStarted,
                                "Canceled last-window close stopped the application.");
                            cancelLastClose = false;
                            other.Close();
                        }));
                    }));
                };
                code = app.Run(main);
                Require(code == 0 && closed == 2, "Closing the final window did not complete Application.Run.");
                break;
            }
            case "main-window-close":
            {
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                Window main = NewWindow(), other = NewWindow();
                bool cancel = true;
                int closed = 0;
                main.Closing += (_, args) => args.Cancel = cancel;
                main.Closed += (_, _) => closed++;
                other.Closed += (_, _) => closed++;
                app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    other.Show();
                    other.Close();
                    Require(app.Windows.Count == 1 && exitCount == 0 && !app.Dispatcher.HasShutdownStarted,
                        "Closing a secondary window stopped OnMainWindowClose.");
                    Window survivor = NewWindow();
                    survivor.Closing += (_, args) => args.Cancel = true;
                    survivor.Closed += (_, _) => closed++;
                    survivor.Show();
                    main.Close();
                    Require(main.IsVisible && exitCount == 0, "Canceled main-window close stopped the application.");
                    cancel = false;
                    main.Close();
                }));
                code = app.Run(main);
                Require(code == 0 && closed == 3, "Main-window close did not force shutdown of the surviving canceling window.");
                break;
            }
            case "explicit-shutdown":
            {
                Window main = NewWindow();
                int forcedClosed = 0;
                app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    main.Close();
                    Require(exitCount == 0 && app.Windows.Count == 0 && !app.Dispatcher.HasShutdownStarted,
                        "OnExplicitShutdown exited when its last window closed.");
                    Window forced = NewWindow();
                    forced.Closing += (_, args) => args.Cancel = true;
                    forced.Closed += (_, _) => forcedClosed++;
                    forced.Show();
                    app.Shutdown(23);
                }));
                code = app.Run(main);
                Require(code == 23 && forcedClosed == 1, "Explicit shutdown failed to propagate its exit code or force window cleanup.");
                break;
            }
            case "shutdown-before-run":
                app.Shutdown(9);
                code = app.Run();
                Require(code == 9 && startupCount == 0, "Shutdown before Run did not suppress Startup and preserve its exit code.");
                break;
            case "affinity-validation":
            {
                Exception? runFailure = null, shutdownFailure = null;
                Window? foreignWindow = null;
                using var ready = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                var foreign = new Thread(() =>
                {
                    try { app.Run(); } catch (Exception exception) { runFailure = exception; }
                    try { app.Shutdown(); } catch (Exception exception) { shutdownFailure = exception; }
                    foreignWindow = NewWindow();
                    ready.Set();
                    release.Wait();
                    foreignWindow.Close();
                }) { IsBackground = true };
                if (OperatingSystem.IsWindows()) foreign.SetApartmentState(ApartmentState.STA);
                foreign.Start();
                Require(ready.Wait(2000), "Foreign window construction did not complete.");
                try
                {
                    Require(runFailure is InvalidOperationException && shutdownFailure is InvalidOperationException,
                        "Foreign-thread Application.Run or Shutdown bypassed affinity.");
                    Expect<ArgumentException>(() => app.Run(foreignWindow!));
                }
                finally { release.Set(); }
                Require(foreign.Join(2000), "Foreign window cleanup did not complete.");
                app.Startup += (_, _) => app.Shutdown();
                code = app.Run();
                Require(code == 0, "Rejected Run calls prevented a later valid Run.");
                break;
            }
            case "exit-exception-cleanup":
                app.Startup += (_, _) => app.Shutdown(27);
                app.Exit += (_, _) => throw new ExitMarkerException();
                Expect<ExitMarkerException>(() => app.Run());
                Require(Environment.ExitCode == 27, "Exit exception lost the requested exit code.");
                break;
            default:
                throw new ArgumentException("Unknown application scenario: " + scenario);
        }
        Require(startupCount == (scenario == "shutdown-before-run" ? 0 : 1) && exitCount == 1,
            "Startup or Exit did not occur exactly once.");
        Require(Application.Current == null && app.MainWindow == null && app.Dispatcher.HasShutdownStarted,
            "Application shutdown did not clear ownership and start dispatcher shutdown.");
        bool exceptionalExit = scenario == "exit-exception-cleanup";
        Require(app.Dispatcher.HasShutdownFinished == !exceptionalExit &&
            events.IndexOf("exit") < events.IndexOf("dispatcher-started") &&
            events[^1] == (exceptionalExit ? "dispatcher-started" : "dispatcher-finished"),
            "Application Exit and dispatcher shutdown order changed.");
        Expect<InvalidOperationException>(() => app.Run());
        Expect<InvalidOperationException>(() => new Application());
    }

    private static Window NewWindow() => new() { Width = 160, Height = 120, Content = new Border() };
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class ExitMarkerException : Exception;
}

internal sealed record ApplicationRunResult(string name, string status, string detail);
