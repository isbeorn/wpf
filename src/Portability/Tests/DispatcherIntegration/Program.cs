extern alias WpfFork;

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DependencyObject = WpfFork::System.Windows.DependencyObject;
using DependencyProperty = WpfFork::System.Windows.DependencyProperty;
using PropertyMetadata = WpfFork::System.Windows.PropertyMetadata;
using Dispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using DispatcherFrame = WpfFork::System.Windows.Threading.DispatcherFrame;
using DispatcherPriority = WpfFork::System.Windows.Threading.DispatcherPriority;
using DispatcherOperationStatus = WpfFork::System.Windows.Threading.DispatcherOperationStatus;
using DispatcherTimer = WpfFork::System.Windows.Threading.DispatcherTimer;
using DispatcherSynchronizationContext = WpfFork::System.Windows.Threading.DispatcherSynchronizationContext;
using PresentationTraceSources = WpfFork::System.Diagnostics.PresentationTraceSources;

var checks = new List<object>();
var failed = false;
if (args.Contains("--weak-event-memory-enabled"))
    AppContext.SetSwitch("Switch.MS.Internal.EnableWeakEventMemoryImprovements", true);
else if (args.Contains("--weak-event-memory-legacy"))
    AppContext.SetSwitch("Switch.MS.Internal.EnableWeakEventMemoryImprovements", false);
checks.Add(new
{
    name = "fork-assembly-identity",
    status = "pass",
    detail = $"{typeof(Dispatcher).Assembly.FullName}; location={typeof(Dispatcher).Assembly.Location}; MVID={typeof(Dispatcher).Assembly.ManifestModule.ModuleVersionId}"
});
Run("weak-event-public-contended-notification", WeakEventWaitCases.PublicNotification);
Run("weak-event-contended-read-and-write", WeakEventWaitCases.ReadAndWrite);
Run("weak-event-recursion-and-context-failure-restoration", WeakEventWaitCases.RecursionAndFailure);
if (args.Contains("--weak-event-waits-only"))
{
    WriteResults();
    return failed ? 1 : 0;
}
Run("dependency-object-and-affinity", () =>
{
    var value = new Fixture();
    value.SetValue(Fixture.ValueProperty, 42);
    Require((int)value.GetValue(Fixture.ValueProperty) == 42, "Dependency property did not retain its value.");
    Exception? affinityFailure = null;
    var foreign = new Thread(() =>
    {
        try { value.SetValue(Fixture.ValueProperty, 43); }
        catch (Exception exception) { affinityFailure = exception; }
    });
    foreign.Start();
    Require(foreign.Join(2000), "Affinity thread did not finish.");
    Require(affinityFailure is InvalidOperationException, "Foreign mutation did not reject thread access.");
});

Run("managed-trace-refresh", () =>
{
    PresentationTraceSources.Refresh();
    var source = PresentationTraceSources.DependencyPropertySource;
    Require(source.Name == "System.Windows.DependencyProperty", "Managed trace source identity changed.");
    var value = new Fixture();
    value.SetValue(Fixture.ValueProperty, 51);
    Require((int)value.GetValue(Fixture.ValueProperty) == 51, "Property operation failed after trace refresh.");
});

Run("pump-registration-failures-and-recovery", () =>
{
    Expect<ArgumentNullException>(() => PumpConfiguration.Register(null));
    PumpConfiguration.Register(() => null);
    Expect<InvalidOperationException>(() => PumpConfiguration.Register(() => null));
    Expect<InvalidOperationException>(() => _ = Dispatcher.CurrentDispatcher);
    Require(Dispatcher.FromThread(Thread.CurrentThread) == null, "Null factory published a partial Dispatcher.");

    PumpConfiguration.Register(() => throw new ProbeException("factory failure"));
    Expect<ProbeException>(() => _ = Dispatcher.CurrentDispatcher);
    Require(Dispatcher.FromThread(Thread.CurrentThread) == null, "Throwing factory published a partial Dispatcher.");

    PumpConfiguration.Register(() => { _ = Dispatcher.CurrentDispatcher; return null; });
    Expect<InvalidOperationException>(() => _ = Dispatcher.CurrentDispatcher);
    Require(Dispatcher.FromThread(Thread.CurrentThread) == null, "Recursive factory published a partial Dispatcher.");

    var dispatcher = Dispatcher.CurrentDispatcher;
    Expect<InvalidOperationException>(() => PumpConfiguration.Register(() => null));
    var ran = false;
    dispatcher.BeginInvoke(new Action(() => ran = true));
    Drain();
    Require(ran, "Managed default did not recover after failed factory consumption.");
});

Run("registered-pump-thread-isolation-and-disposal", () =>
{
    var owner = Environment.CurrentManagedThreadId;
    var calls = 0;
    var pump = PumpConfiguration.CreateManagedPump();
    PumpConfiguration.Register(() =>
    {
        Require(Environment.CurrentManagedThreadId == owner, "Factory ran on another thread.");
        calls++;
        return pump;
    });

    Exception? foreignFailure = null;
    var foreign = new Thread(() =>
    {
        try { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        catch (Exception exception) { foreignFailure = exception; }
    }) { IsBackground = true };
    foreign.Start();
    Require(foreign.Join(2000) && foreignFailure == null && calls == 0, "Another thread consumed the registered factory.");
    var dispatcher = Dispatcher.CurrentDispatcher;
    Require(calls == 1 && ReferenceEquals(dispatcher, Dispatcher.CurrentDispatcher), "Factory was not consumed exactly once.");
    var ran = false;
    dispatcher.BeginInvoke(new Action(() => ran = true));
    Drain();
    Require(ran, "Registered real pump failed to process operations.");
    dispatcher.InvokeShutdown();
    Expect<ObjectDisposedException>(() => PumpConfiguration.Wake(pump));
});

Run("priority-fifo-and-inactive-promotion", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var order = new List<string>();
    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => order.Add("background")));
    dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => order.Add("normal-1")));
    dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => order.Add("normal-2")));
    var inactive = dispatcher.BeginInvoke(DispatcherPriority.Inactive, new Action(() => order.Add("promoted")));
    Drain();
    Require(order.SequenceEqual(["normal-1", "normal-2", "background"]), "Priority or FIFO order changed.");
    Require(inactive.Status == DispatcherOperationStatus.Pending, "Inactive operation executed.");
    inactive.Priority = DispatcherPriority.Normal;
    inactive.Priority = DispatcherPriority.Inactive;
    Drain();
    Require(inactive.Status == DispatcherOperationStatus.Pending, "Demoted operation executed.");
    inactive.Priority = DispatcherPriority.Normal;
    Drain();
    Require(order.Last() == "promoted" && inactive.Status == DispatcherOperationStatus.Completed, "Inactive promotion failed.");
});

Run("nested-frame-and-inline-send", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var order = new List<string>();
    dispatcher.BeginInvoke(new Action(() =>
    {
        order.Add("outer");
        var nested = new DispatcherFrame();
        dispatcher.BeginInvoke(new Action(() => { order.Add("nested"); nested.Continue = false; }));
        Dispatcher.PushFrame(nested);
        order.Add("resumed");
    }));
    dispatcher.Invoke(() => order.Add("send"), DispatcherPriority.Send);
    Require(order.SequenceEqual(["send"]), "Same-thread Send pumped pending operations.");
    Drain();
    Require(order.SequenceEqual(["send", "outer", "nested", "resumed"]), "Nested frame order changed.");
});

Run("disable-processing-and-waits", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var ran = false;
    dispatcher.BeginInvoke(new Action(() => ran = true));
    using (dispatcher.DisableProcessing())
    {
        Expect<InvalidOperationException>(() => Dispatcher.PushFrame(new DispatcherFrame()));
        Require(!ran, "DisableProcessing dispatched queued work.");
        using var signal = new ManualResetEvent(false);
        var context = new DispatcherSynchronizationContext(dispatcher);
        var handle = signal.SafeWaitHandle.DangerousGetHandle();
        Require(context.Wait([handle], false, 0) == WaitHandle.WaitTimeout, "Disabled wait did not time out.");
        signal.Set();
        Require(context.Wait([handle], false, 0) == 0, "Disabled wait did not observe signal.");
        using var secondSignal = new ManualResetEvent(false);
        IntPtr[] disabledHandles = [handle, secondSignal.SafeWaitHandle.DangerousGetHandle()];
        Require(context.Wait(disabledHandles, true, 0) == WaitHandle.WaitTimeout, "Disabled WaitAll ignored an unsignaled handle.");
        secondSignal.Set();
        Require(context.Wait(disabledHandles, true, 0) == 0, "Disabled WaitAll did not observe both signals.");
        Require(!ran, "Disabled wait pumped queued work.");
    }
    Drain();
    Require(ran, "Reenabling processing did not dispatch queued work.");
    using var first = new ManualResetEvent(true);
    using var second = new ManualResetEvent(false);
    var enabled = new DispatcherSynchronizationContext(dispatcher);
    IntPtr[] handles = [first.SafeWaitHandle.DangerousGetHandle(), second.SafeWaitHandle.DangerousGetHandle()];
    Require(enabled.Wait(handles, false, 0) == 0, "Enabled WaitAny did not observe signal.");
    first.Reset();
    Require(enabled.Wait(handles, false, 0) == WaitHandle.WaitTimeout, "Enabled WaitAny did not time out.");
    first.Set();
    Require(enabled.Wait(handles, true, 0) == WaitHandle.WaitTimeout, "WaitAll ignored an unsignaled handle.");
    second.Set();
    Require(enabled.Wait(handles, true, 0) == 0, "WaitAll did not observe both signals.");
});

if (OperatingSystem.IsWindows())
{
    Run("windows-sta-disabled-waits", () =>
    {
        Require(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "STA fixture was not initialized as STA.");
        var dispatcher = Dispatcher.CurrentDispatcher;
        var context = new DispatcherSynchronizationContext(dispatcher);
        using var first = new ManualResetEvent(false);
        using var second = new ManualResetEvent(false);
        IntPtr[] handles = [first.SafeWaitHandle.DangerousGetHandle(), second.SafeWaitHandle.DangerousGetHandle()];
        Require(context.Wait(handles, false, 0) == WaitHandle.WaitTimeout, "Enabled STA WaitAny did not time out.");
        first.Set();
        second.Set();
        Expect<NotSupportedException>(() => context.Wait(handles, true, 0));

        var ran = false;
        dispatcher.BeginInvoke(new Action(() => ran = true));
        using (dispatcher.DisableProcessing())
        {
            Require(context.Wait(handles, true, 0) == 0, "Disabled STA WaitAll did not observe both signals.");
            second.Reset();
            Require(context.Wait(handles, true, 0) == WaitHandle.WaitTimeout, "Disabled STA WaitAll ignored an unsignaled handle.");
            Require(context.Wait(handles, false, 0) == 0, "Disabled STA WaitAny did not observe its first signal.");
            first.Reset();
            Require(context.Wait(handles, false, 0) == WaitHandle.WaitTimeout, "Disabled STA WaitAny did not time out.");
            Require(!ran, "Disabled STA waits pumped queued work.");
        }

        first.Set();
        second.Set();
        Expect<NotSupportedException>(() => context.Wait(handles, true, 0));
        Drain();
        Require(ran, "STA processing did not resume after the disabled scope.");
    }, sta: true);
}
else
{
    checks.Add(new { name = "windows-sta-disabled-waits", status = "not-applicable", detail = "Windows STA behavior is not claimed on this platform." });
}

Run("cancellation-and-exceptions", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    using var cancellation = new CancellationTokenSource();
    var ran = false;
    var canceled = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Normal, cancellation.Token);
    cancellation.Cancel();
    Drain();
    Require(!ran && canceled.Status == DispatcherOperationStatus.Aborted && canceled.Task.IsCanceled, "Pending cancellation failed.");
    var alreadyCanceled = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Normal, cancellation.Token);
    Drain();
    Require(!ran && alreadyCanceled.Status == DispatcherOperationStatus.Aborted, "Already-canceled enqueue executed.");
    var failure = dispatcher.InvokeAsync(new Action(() => throw new ProbeException("expected")));
    Drain();
    Expect<ProbeException>(() => failure.Task.GetAwaiter().GetResult());
    var handled = false;
    dispatcher.UnhandledException += (_, args) => { handled = args.Exception is ProbeException; args.Handled = true; };
    dispatcher.BeginInvoke(new Action(() => throw new ProbeException("handled")));
    Drain();
    Require(handled, "BeginInvoke exception did not reach the dispatcher handler.");
});

Run("timer-start-stop-restart", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var ticks = 0;
    var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(5) };
    timer.Tick += (_, _) => { ticks++; timer.Stop(); };
    timer.Start();
    PumpUntil(() => ticks == 1);
    Drain();
    Require(!timer.IsEnabled && ticks == 1, "Stopped timer kept running.");
    timer.Start();
    PumpUntil(() => ticks == 2);
    Require(!timer.IsEnabled, "Restarted timer did not stop.");
});

Run("cross-thread-wake-and-invoke", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var frame = new DispatcherFrame();
    var observedThread = 0;
    Exception? workerFailure = null;
    var worker = new Thread(() =>
    {
        try
        {
            Thread.Sleep(20);
            dispatcher.Invoke(() => observedThread = Environment.CurrentManagedThreadId);
            dispatcher.BeginInvoke(new Action(() => frame.Continue = false));
        }
        catch (Exception exception)
        {
            workerFailure = exception;
            try { frame.Continue = false; }
            catch (Exception wakeFailure) { workerFailure = new AggregateException(exception, wakeFailure); }
        }
    }) { IsBackground = true };
    worker.Start();
    Dispatcher.PushFrame(frame);
    Require(worker.Join(2000), "Cross-thread Invoke did not finish.");
    Require(workerFailure == null, workerFailure?.ToString() ?? "Unexpected worker failure.");
    Require(observedThread == Environment.CurrentManagedThreadId, "Cross-thread callback used the wrong thread.");
});

Run("queued-shutdown-and-abort", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var started = false;
    var finished = false;
    dispatcher.ShutdownStarted += (_, _) => started = true;
    dispatcher.ShutdownFinished += (_, _) => finished = true;
    var pending = dispatcher.BeginInvoke(DispatcherPriority.Inactive, new Action(() => throw new InvalidOperationException("Inactive operation ran.")));
    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    Dispatcher.Run();
    Require(started && finished && dispatcher.HasShutdownStarted && dispatcher.HasShutdownFinished, "Shutdown lifecycle was incomplete.");
    Require(pending.Status == DispatcherOperationStatus.Aborted, "Shutdown did not abort pending work.");
    var rejected = dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Post-shutdown operation ran.")));
    Require(rejected.Status == DispatcherOperationStatus.Aborted, "Post-shutdown enqueue was accepted.");
});

Run("immediate-shutdown", () =>
{
    var dispatcher = Dispatcher.CurrentDispatcher;
    var events = new List<string>();
    dispatcher.ShutdownStarted += (_, _) => events.Add("started");
    dispatcher.ShutdownFinished += (_, _) => events.Add("finished");
    dispatcher.InvokeShutdown();
    Require(events.SequenceEqual(["started", "finished"]) && dispatcher.HasShutdownFinished, "Immediate shutdown lifecycle changed.");
});

WriteResults();
return failed ? 1 : 0;

void WriteResults() => Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "dispatcher-integration",
    os = RuntimeInformation.OSDescription,
    scope = "fork WindowsBase dependency properties and Dispatcher with managed portable pump; native ETW disabled; no native window/input host",
    weakEventMemoryImprovements = AppContext.TryGetSwitch("Switch.MS.Internal.EnableWeakEventMemoryImprovements", out bool enabled) ? enabled : (bool?)null,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));

void Run(string name, Action test, bool sta = false)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { test(); }
        catch (Exception exception) { failure = exception; }
        finally
        {
            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            if (dispatcher != null && !dispatcher.HasShutdownFinished)
            {
                try { dispatcher.InvokeShutdown(); }
                catch (Exception exception) { failure ??= exception; }
            }
        }
    }) { IsBackground = true, Name = name };
    if (sta && OperatingSystem.IsWindows())
    {
        thread.SetApartmentState(ApartmentState.STA);
    }
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(10)))
    {
        failure = new TimeoutException("Dispatcher test exceeded its 10-second watchdog.");
    }
    failed |= failure != null;
    checks.Add(new { name, status = failure == null ? "pass" : "fail", detail = failure?.ToString() ?? "Observable behavior passed." });
}

static void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}

static void PumpUntil(Func<bool> condition)
{
    var frame = new DispatcherFrame();
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
    poll.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) { poll.Stop(); frame.Continue = false; } };
    poll.Start();
    Dispatcher.PushFrame(frame);
    Require(condition(), "Condition did not become true before its deadline.");
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}

internal sealed class Fixture : DependencyObject
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(int), typeof(Fixture), new PropertyMetadata(0));
}

internal sealed class ProbeException(string message) : Exception(message);

// The backend registration contract is internal. Reflection keeps this fixture from
// adding a production friend assembly or exposing configuration APIs just for tests.
internal static class PumpConfiguration
{
    private static readonly Assembly Kernel = typeof(Dispatcher).Assembly;
    private static readonly Type PumpType = Kernel.GetType("MS.Internal.Portability.IDispatcherPump", throwOnError: true)!;
    private static readonly MethodInfo RegisterMethod = Kernel.GetType("MS.Internal.Portability.DispatcherPlatform", throwOnError: true)!
        .GetMethod("RegisterPumpFactory", BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static void Register(Func<object?>? factory)
    {
        Delegate? typedFactory = factory == null ? null : Expression.Lambda(typeof(Func<>).MakeGenericType(PumpType),
            Expression.Convert(Expression.Invoke(Expression.Constant(factory)), PumpType)).Compile();
        Invoke(RegisterMethod, null, [typedFactory]);
    }

    internal static object CreateManagedPump() => Activator.CreateInstance(
        Kernel.GetType("MS.Internal.Portability.ManagedDispatcherPump", throwOnError: true)!, nonPublic: true)!;

    internal static void Wake(object pump) => Invoke(pump.GetType().GetMethod("Wake")!, pump, []);

    private static void Invoke(MethodInfo method, object? target, object?[] arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
