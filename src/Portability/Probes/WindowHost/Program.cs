using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Portability.WindowHost;

internal static class Program
{
    internal static readonly ProbeReport Report = new();
    internal static bool Headless;
    internal static bool Smoke;
    internal static bool RequireNativeInput;
    internal static int DurationSeconds = 3;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--headless": Headless = true; break;
                    case "--smoke": Smoke = true; break;
                    case "--require-native-input": RequireNativeInput = true; break;
                    case "--duration-seconds" when i + 1 < args.Length:
                        DurationSeconds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                        if (DurationSeconds is < 1 or > 120)
                            throw new ArgumentException("Duration must be between 1 and 120 seconds.");
                        break;
                    default: throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                }
            }
            if (Headless && RequireNativeInput)
                throw new ArgumentException("--require-native-input cannot be used with --headless.");

            using var watchdog = Smoke ? new Timer(_ =>
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    probe = "open-source-window-host",
                    os = RuntimeInformation.OSDescription,
                    scope = Headless ? "avalonia-headless-no-native-evidence" : "avalonia-native-host-only",
                    checks = new[] { new { name = "smoke-watchdog", status = "fail", detail = "The host did not exit within the smoke duration plus 15 seconds." } }
                }));
                Environment.Exit(1);
            }, null, TimeSpan.FromSeconds(DurationSeconds + 15), Timeout.InfiniteTimeSpan) : null;

            var builder = AppBuilder.Configure<ProbeApplication>();
            if (Headless)
                builder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
            else
                builder.UsePlatformDetect();
            builder.StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
        }
        catch (Exception exception)
        {
            Report.Check("host-error", false, exception.ToString());
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "open-source-window-host",
            os = RuntimeInformation.OSDescription,
            scope = Headless ? "avalonia-headless-no-native-evidence" : "avalonia-native-host-only",
            wpfCompatibility = "unproven: no WPF assemblies, Dispatcher contract, HWND contract or NINA views are implemented",
            checks = Report.Checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        return Report.Failed ? 1 : 0;
    }
}

internal sealed class ProbeApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("The desktop lifetime was not installed.");

        var input = new TextBox { PlaceholderText = "Type here to verify native input delivery" };
        var window = new Window
        {
            Title = "Open-source portability host probe",
            Width = 520,
            Height = 160,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Avalonia host feasibility only. WPF semantics remain unproven." },
                    input
                }
            }
        };
        desktop.MainWindow = window;
        var opened = false;
        var closed = false;
        var checksCompleted = false;
        var nativeKeys = 0;
        var nativeText = 0;
        window.Closed += (_, _) => closed = true;
        window.KeyDown += (_, _) => nativeKeys++;
        window.AddHandler(InputElement.TextInputEvent, (_, e) => nativeText += e.Text?.Length ?? 0,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        window.Opened += async (_, _) =>
        {
            opened = true;
            try
            {
                var handle = window.TryGetPlatformHandle();
                if (!Program.Headless)
                    Program.Report.Check("native-window-handle", handle is { Handle: not 0 },
                        new { descriptor = handle?.HandleDescriptor, handle = handle?.Handle.ToInt64() });
                await RunChecks(window).ConfigureAwait(true);
                window.Activate();
                var focused = input.Focus();
                Program.Report.Check("control-focus", focused && input.IsFocused,
                    new { focused, input.IsFocused, window.IsActive, native = !Program.Headless });

                if (Program.Headless)
                {
                    window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
                    window.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
                    window.KeyTextInput("a");
                    Program.Report.Check("headless-input-delivery", nativeKeys > 0 && nativeText > 0 && input.Text == "a",
                        new { keyEvents = nativeKeys, textEvents = nativeText, input.Text, synthetic = true });
                }
                checksCompleted = true;

                if (Program.Smoke)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Program.DurationSeconds)).ConfigureAwait(true);
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                Program.Report.Check("runtime-error", false, exception.ToString());
                window.Close();
            }
        };
        desktop.Exit += (_, _) =>
        {
            Program.Report.Check("host-check-sequence-completed", checksCompleted,
                checksCompleted ? "All host checks reached completion before shutdown." : "The window closed before all host checks completed.");
            Program.Report.Check("window-create-close", opened && closed,
                new { opened, closed, native = !Program.Headless });
            if (!Program.Headless)
            {
                if (nativeKeys > 0 && nativeText > 0)
                    Program.Report.Check("native-input-delivery", true, new { keyEvents = nativeKeys, textEvents = nativeText });
                else if (Program.RequireNativeInput)
                    Program.Report.Check("native-input-delivery", false, "No native key and text input observed before shutdown.");
                else
                    Program.Report.Unproven("native-input-delivery", "No native key and text input observed. Run interactively or use --require-native-input with an external native input source.");
            }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunChecks(Window window)
    {
        var report = Program.Report;
        var dispatcher = Dispatcher.UIThread;
        var uiThread = Environment.CurrentManagedThreadId;
        var assembly = typeof(Window).Assembly;
        var identity = assembly.FullName;
        var mvid = assembly.ManifestModule.ModuleVersionId;
        report.Check("ui-thread-ownership", dispatcher.CheckAccess(), new { uiThread });
        var worker = await Task.Run(() =>
        {
            var thread = Environment.CurrentManagedThreadId;
            var denied = false;
            try { window.VerifyAccess(); }
            catch (InvalidOperationException) { denied = true; }
            var invokedThread = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
            return new { thread, denied, invokedThread, access = dispatcher.CheckAccess() };
        }).ConfigureAwait(true);
        report.Check("worker-invoke-and-access-guard", worker.denied && !worker.access && worker.invokedThread == uiThread,
            worker);
        var asyncThread = await Task.Run(async () => await dispatcher.InvokeAsync(() => Environment.CurrentManagedThreadId)).ConfigureAwait(true);
        report.Check("worker-invoke-async", asyncThread == uiThread, new { asyncThread, uiThread });

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var executed = false;
            var canceled = false;
            try { await dispatcher.InvokeAsync(() => executed = true, DispatcherPriority.Normal, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            report.Check("dispatcher-operation-cancellation", canceled && !executed, new { canceled, executed });
        }

        var ticks = 0;
        var timerThread = 0;
        var firstTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            ticks++;
            timerThread = Environment.CurrentManagedThreadId;
            firstTick.TrySetResult();
        };
        timer.Start();
        await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        timer.Stop();
        var stoppedAt = ticks;
        await Task.Delay(100).ConfigureAwait(true);
        report.Check("dispatcher-timer-start-stop", ticks > 0 && timerThread == uiThread && ticks == stoppedAt,
            new { ticks, timerThread, uiThread, stoppedAt });

        var child = new Window { Title = "Owned modal probe", Width = 280, Height = 90, Content = new TextBlock { Text = "Closing this owned modal automatically." } };
        var childOpened = false;
        var childClosed = false;
        var pumpedWhileModal = false;
        child.Opened += (_, _) =>
        {
            childOpened = true;
            dispatcher.Post(() => { pumpedWhileModal = true; child.Close(true); });
        };
        child.Closed += (_, _) => childClosed = true;
        var result = await child.ShowDialog<bool>(window).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        report.Check("owner-async-modal-event-loop", childOpened && childClosed && pumpedWhileModal && result && child.Owner == window,
            new { childOpened, childClosed, pumpedWhileModal, result, ownerRetained = child.Owner == window });

        if (dispatcher.SupportsRunLoops)
        {
            var frame = new DispatcherFrame();
            var callbackRan = false;
            dispatcher.Post(() => { callbackRan = true; frame.Continue = false; });
            dispatcher.PushFrame(frame);
            report.Check("single-nested-dispatcher-frame", callbackRan, "One posted callback exited one Avalonia frame. WPF reentrancy, priorities, COM pumping and synchronous ShowDialog compatibility remain unproven.");
        }
        else
            report.Unproven("single-nested-dispatcher-frame", "This backend does not support dispatcher run loops.");

        report.Check("assembly-identity-unchanged", ReferenceEquals(assembly, typeof(Window).Assembly) && identity == assembly.FullName && mvid == assembly.ManifestModule.ModuleVersionId,
            new { name = identity, moduleVersionId = mvid, path = assembly.Location, dispatcherAssembly = typeof(Dispatcher).Assembly.FullName, windowType = typeof(Window).FullName });
        report.Check("ui-thread-after-await", dispatcher.CheckAccess() && Environment.CurrentManagedThreadId == uiThread,
            new { uiThread, currentThread = Environment.CurrentManagedThreadId });
    }
}

internal sealed class ProbeReport
{
    public List<object> Checks { get; } = [];
    public bool Failed { get; private set; }

    public void Check(string name, bool success, object detail)
    {
        Failed |= !success;
        Checks.Add(new { name, status = success ? "pass" : "fail", detail });
    }

    public void Unproven(string name, object detail) => Checks.Add(new { name, status = "unproven", detail });
}
