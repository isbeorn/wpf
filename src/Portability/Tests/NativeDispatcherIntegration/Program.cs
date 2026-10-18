extern alias WpfFork;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Wpf.Portable.NativeHost;
using DependencyObject = WpfFork::System.Windows.DependencyObject;
using DependencyProperty = WpfFork::System.Windows.DependencyProperty;
using PropertyMetadata = WpfFork::System.Windows.PropertyMetadata;
using Dispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using DispatcherFrame = WpfFork::System.Windows.Threading.DispatcherFrame;
using DispatcherPriority = WpfFork::System.Windows.Threading.DispatcherPriority;
using DispatcherTimer = WpfFork::System.Windows.Threading.DispatcherTimer;
using DispatcherOperationStatus = WpfFork::System.Windows.Threading.DispatcherOperationStatus;

var checks = new List<object>();
var failed = false;
var completed = false;
Window? window = null;
var expectedClose = false;
var opened = false;
var closed = false;
var interrupted = false;
using var watchdog = new Timer(_ =>
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "real-wpf-native-dispatcher-integration",
        os = RuntimeInformation.OSDescription,
        scope = "Linux X11/GLib with actual fork WindowsBase Dispatcher",
        checks = new[] { new { name = "watchdog", status = "fail", detail = "Native dispatcher integration exceeded 30 seconds." } }
    }));
    Environment.Exit(1);
}, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

try
{
    NativeDispatcherHost.InitializeForCurrentThread(AppBuilder.Configure<ProbeApplication>());
    var dispatcher = Dispatcher.CurrentDispatcher;
    var owner = Environment.CurrentManagedThreadId;
    var fixture = new Fixture();
    var input = new TextBox { PlaceholderText = "Native X11 input probe" };
    var status = new TextBlock { Text = "The actual WPF dispatcher owns this native event pump." };
    window = new Window
    {
        Title = "Real WPF dispatcher native integration",
        Width = 560,
        Height = 170,
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { status, input } }
    };
    var keyDown = 0;
    var keyUp = 0;
    var textEvents = 0;
    var activated = 0;
    var deactivated = 0;
    var callbackOwner = true;
    Action? keyDownHook = null;
    Action? keyUpHook = null;
    window.Opened += (_, _) => opened = true;
    window.Closed += (_, _) =>
    {
        closed = true;
        if (!expectedClose)
        {
            interrupted = true;
            failed = true;
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }
    };
    window.Activated += (_, _) => activated++;
    window.Deactivated += (_, _) => deactivated++;
    window.AddHandler(InputElement.KeyDownEvent, (_, _) =>
    {
        keyDown++;
        callbackOwner &= dispatcher.CheckAccess() && Environment.CurrentManagedThreadId == owner;
        fixture.SetValue(Fixture.ValueProperty, (int)fixture.GetValue(Fixture.ValueProperty) + 1);
        keyDownHook?.Invoke();
    }, RoutingStrategies.Tunnel);
    window.AddHandler(InputElement.KeyUpEvent, (_, _) => { keyUp++; keyUpHook?.Invoke(); }, RoutingStrategies.Tunnel);
    window.AddHandler(InputElement.TextInputEvent, (_, _) => textEvents++, RoutingStrategies.Tunnel);
    window.Show();
    var nativeHandle = window.TryGetPlatformHandle();
    Require(nativeHandle is { Handle: not 0, HandleDescriptor: "XID" }, "The host did not create an actual X11 window.");
    var xid = nativeHandle!.Handle;
    using var nativeInput = new X11Input();
    window.Activate();
    nativeInput.Focus(xid);
    input.Focus();
    PumpUntil(() => nativeInput.CurrentFocus() == xid);

    Run("actual-fork-identity-and-thread-ownership", () =>
    {
        fixture.SetValue(Fixture.ValueProperty, 7);
        Exception? foreignFailure = null;
        var foreign = new Thread(() =>
        {
            try { fixture.SetValue(Fixture.ValueProperty, 8); }
            catch (Exception exception) { foreignFailure = exception; }
        });
        foreign.Start();
        Require(foreign.Join(2000) && foreignFailure is InvalidOperationException, "Actual WPF dependency-object affinity was not enforced.");
        Require((int)fixture.GetValue(Fixture.ValueProperty) == 7 && ReferenceEquals(fixture.Dispatcher, dispatcher), "The fixture does not use the actual WPF dispatcher.");
        Require(typeof(Dispatcher).Assembly.GetName().Name == "WindowsBase", "The dispatcher assembly identity changed.");
        Require(typeof(Dispatcher).Assembly.GetName().GetPublicKeyToken()!.SequenceEqual(typeof(NativeDispatcherHost).Assembly.GetName().GetPublicKeyToken()!), "The backend friend assembly has the wrong signing identity.");
        var x11Assembly = typeof(Avalonia.X11.ExternalDispatcherBridge).Assembly;
        var x11Version = x11Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Require(x11Version.Contains("portable-native-input.2d79ba55fae1b1e71c69f93a668080c9029e6a31", StringComparison.Ordinal) && Convert.ToHexString(x11Assembly.GetName().GetPublicKeyToken()!) == "C8D484A7012F9A8B", "The pinned source X11 adaptation or original signing identity was not loaded.");
        return new { assembly = typeof(Dispatcher).Assembly.FullName, path = typeof(Dispatcher).Assembly.Location, moduleVersionId = typeof(Dispatcher).Assembly.ManifestModule.ModuleVersionId, owner, x11Assembly = x11Assembly.FullName, x11Version, x11Path = x11Assembly.Location };
    });

    Run("worker-invoke-and-coalesced-wake", () =>
    {
        var invokedThread = 0;
        var task = Task.Run(() => dispatcher.Invoke(() => invokedThread = Environment.CurrentManagedThreadId));
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        var received = 0;
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => received++));
        });
        PumpUntil(() => producer.IsCompleted && received == 100);
        Require(invokedThread == owner, "A worker invocation ran outside the native owner thread.");
        return new { invokedThread, received };
    });

    Run("native-idle-frame-wakes-from-worker", () =>
    {
        var frame = new DispatcherFrame();
        var invokedThread = 0;
        var clock = Stopwatch.StartNew();
        var worker = Task.Run(() =>
        {
            Thread.Sleep(60);
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { invokedThread = Environment.CurrentManagedThreadId; frame.Continue = false; }));
        });
        Dispatcher.PushFrame(frame);
        worker.GetAwaiter().GetResult();
        Require(invokedThread == owner, "An idle native frame did not wake for its worker operation.");
        return new { invokedThread, elapsedMilliseconds = clock.ElapsedMilliseconds, wpfPollingTimer = false };
    });

    Run("native-input-query-delivery-and-wpf-priorities", () =>
    {
        var downBefore = keyDown;
        var upBefore = keyUp;
        var textBefore = textEvents;
        var order = new List<string>();
        var nativeBeforeBackground = false;
        nativeInput.PressAndRelease();
        var nativePending = ((Avalonia.Threading.IControlledDispatcherImpl)Avalonia.Threading.Dispatcher.UIThread.PlatformImpl).HasPendingInput;
        Require(nativePending, "The actual X11 queue failed to report the injected pending native input.");
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { nativeBeforeBackground = keyDown > downBefore; order.Add("background"); }));
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => order.Add("input")));
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => order.Add("normal")));
        try { PumpUntil(() => order.Count == 3 && keyDown > downBefore && keyUp > upBefore && textEvents > textBefore); }
        catch (Exception exception) { throw new InvalidOperationException($"Native delivery diagnostics: down={keyDown - downBefore}, up={keyUp - upBefore}, text={textEvents - textBefore}, order={string.Join(',', order)}, focused={input.IsFocused}, active={window.IsActive}, nativeFocus={nativeInput.CurrentFocus()}, xid={xid}.", exception); }
        Require(order.SequenceEqual(["normal", "input", "background"]) && nativeBeforeBackground, "Native input or the actual WPF priority queue was bypassed.");
        Require(callbackOwner && input.Text?.Contains('a') == true && (int)fixture.GetValue(Fixture.ValueProperty) > 7, "The native callback did not update the actual WPF dependency object on its owner thread.");
        return new { nativePending, keyPresses = keyDown - downBefore, keyReleases = keyUp - upBefore, textEvents = textEvents - textBefore, callbackOwner, nativeBeforeBackground, order, delivery = "OS-injected XTest events through the actual X server; not physical keyboard input or WPF InputManager" };
    });

    Run("native-focus-loss-and-regain", () =>
    {
        var lostBefore = deactivated;
        var gainedBefore = activated;
        var otherInput = new TextBox();
        var other = new Window { Title = "Native focus destination", Width = 260, Height = 100, Content = otherInput };
        var otherKeys = 0;
        other.AddHandler(InputElement.KeyDownEvent, (_, _) => otherKeys++, RoutingStrategies.Tunnel);
        try
        {
            other.Show();
            var otherHandle = other.TryGetPlatformHandle()!.Handle;
            nativeInput.Focus(otherHandle);
            otherInput.Focus();
            PumpFor(TimeSpan.FromMilliseconds(30));
            Require(nativeInput.CurrentFocus() == otherHandle, "The X server did not move input focus to the other native window.");
            var keysBefore = keyDown;
            nativeInput.PressAndRelease();
            PumpUntil(() => otherKeys > 0);
            Require(keyDown == keysBefore, "The original host received a key event targeted at the other focused native window.");
            nativeInput.Focus(xid);
            input.Focus();
            Require(nativeInput.CurrentFocus() == xid, "The X server did not return input focus to the host.");
            nativeInput.PressAndRelease();
            PumpUntil(() => keyDown > keysBefore);
            return new { lost = true, regained = true, otherKeys, deactivatedCallbacks = deactivated - lostBefore, activatedCallbacks = activated - gainedBefore, focus = "Actual X server input focus and key routing between two native windows; WM activation recorded separately." };
        }
        finally
        {
            other.Close();
            nativeInput.Focus(xid);
            input.Focus();
        }
    });

    Run("repeated-native-pending-probes-preserve-delivery", () =>
    {
        var native = (Avalonia.Threading.IControlledDispatcherImpl)Avalonia.Threading.Dispatcher.UIThread.PlatformImpl;
        var probes = 0;
        for (var i = 0; i < 3; i++)
        {
            var beforeDown = keyDown;
            var beforeUp = keyUp;
            var background = false;
            nativeInput.PressAndRelease();
            var deadline = Stopwatch.StartNew();
            while (!native.HasPendingInput && deadline.ElapsedMilliseconds < 200) Thread.Sleep(1);
            for (var probe = 0; probe < 8; probe++)
            {
                Require(native.HasPendingInput, "A repeated native queue probe lost pending Xlib input before dispatch.");
                probes++;
            }
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => background = true));
            PumpUntil(() => keyDown > beforeDown && keyUp > beforeUp && background);
        }
        return new { probes, rounds = 3, delivery = "XPending may move socket data into the actual Xlib event queue; native delivery must still complete." };
    });

    Run("nested-wpf-frame-from-native-key-callback", () =>
    {
        var completedNested = false;
        var releasedInsideNested = false;
        keyDownHook = () =>
        {
            keyDownHook = null;
            var frame = new DispatcherFrame();
            keyUpHook = () => releasedInsideNested = true;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); keyUpHook = null; }
            completedNested = true;
        };
        nativeInput.PressAndRelease();
        try { PumpUntil(() => completedNested); }
        finally { keyDownHook = null; keyUpHook = null; }
        Require(releasedInsideNested, "The native key release was starved while a native callback pushed a WPF frame.");
        return new { completedNested, releasedInsideNested };
    });

    Run("actual-wpf-cancellation-and-timer-stop", () =>
    {
        var executed = false;
        using var cancellation = new CancellationTokenSource();
        var operation = dispatcher.InvokeAsync(() => executed = true, DispatcherPriority.Background, cancellation.Token);
        cancellation.Cancel();
        PumpUntil(() => operation.Status == DispatcherOperationStatus.Aborted);
        Require(!executed && operation.Task.IsCanceled, "A canceled actual WPF operation executed.");
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { Require(dispatcher.CheckAccess(), "A WPF timer ran outside the owner thread."); ticks++; };
        timer.Start();
        try { PumpUntil(() => ticks > 0); }
        finally { timer.Stop(); }
        var stoppedAt = ticks;
        PumpFor(TimeSpan.FromMilliseconds(70));
        Require(ticks == stoppedAt, "The stopped WPF timer continued ticking through the native pump.");
        return new { canceledStatus = operation.Status.ToString(), ticks, stoppedAt };
    });

    Run("owned-native-modal-with-wpf-pump", () =>
    {
        var child = new Window { Title = "Owned native modal", Width = 280, Height = 100 };
        var childOpened = false;
        var childClosed = false;
        child.Opened += (_, _) => childOpened = true;
        child.Closed += (_, _) => childClosed = true;
        var modal = child.ShowDialog<bool>(window);
        var ownerDuringModal = child.Owner == window;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => child.Close(true)));
        PumpUntil(() => modal.IsCompleted);
        Require(modal.GetAwaiter().GetResult() && childOpened && childClosed && ownerDuringModal, $"Owned modal diagnostics: result={modal.GetAwaiter().GetResult()}, opened={childOpened}, closed={childClosed}, ownerDuringModal={ownerDuringModal}.");
        nativeInput.Focus(xid);
        input.Focus();
        PumpUntil(() => nativeInput.CurrentFocus() == xid);
        return new { childOpened, childClosed, ownerDuringModal, ownerAfterClose = child.Owner == window };
    });

    Run("background-progress-during-native-input-and-rendering", () =>
    {
        var downBefore = keyDown;
        var renderFrames = 0;
        var backgroundRuns = 0;
        var nativeAtFirstBackground = -1;
        var nativeAtLastBackground = -1;
        var rendering = true;
        var clock = Stopwatch.StartNew();
        void Render(TimeSpan _)
        {
            renderFrames++;
            status.Text = $"Native render callback {renderFrames}; WPF background runs {backgroundRuns}";
            if (rendering) window.RequestAnimationFrame(Render);
        }
        void Background()
        {
            backgroundRuns++;
            var received = keyDown - downBefore;
            if (received > 0 && nativeAtFirstBackground < 0) nativeAtFirstBackground = received;
            nativeAtLastBackground = received;
            if (clock.ElapsedMilliseconds < 500)
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Background));
        }
        window.RequestAnimationFrame(Render);
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Background));
        var producer = Task.Run(() =>
        {
            using var injected = new X11Input();
            for (var i = 0; i < 25; i++) { injected.PressAndRelease(); Thread.Sleep(15); }
        });
        try { PumpUntil(() => producer.IsCompleted && clock.ElapsedMilliseconds >= 550); producer.GetAwaiter().GetResult(); }
        finally { rendering = false; }
        Require(keyDown - downBefore >= 20 && renderFrames >= 2 && backgroundRuns > 1 && nativeAtFirstBackground >= 1 && nativeAtLastBackground > nativeAtFirstBackground,
            $"Progress diagnostics: nativeKeys={keyDown - downBefore}, renders={renderFrames}, background={backgroundRuns}, first={nativeAtFirstBackground}, last={nativeAtLastBackground}.");
        return new { nativeKeyPresses = keyDown - downBefore, renderFrames, backgroundRuns, nativeAtFirstBackground, nativeAtLastBackground, elapsedMilliseconds = clock.ElapsedMilliseconds };
    });

    Run("direct-managed-input-exception-propagation", () =>
    {
        var observed = false;
        keyDownHook = () => { keyDownHook = null; throw new NativeCallbackTestException(); };
        nativeInput.PressAndRelease();
        try { PumpUntil(() => false); }
        catch (NativeCallbackTestException) { observed = true; }
        finally { keyDownHook = null; }
        var recovered = false;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => recovered = true));
        PumpUntil(() => recovered);
        Require(observed && dispatcher.CheckAccess(), "The direct managed input callback exception was swallowed or escaped the owner thread.");
        return new { observed, recovered, delivery = "Direct managed dispatch from the source input provider into the WPF frame; this does not exercise a GLib C callback." };
    });

    Run("glib-signal-callback-exception-propagation", () =>
        VerifyGlibException(Avalonia.Threading.DispatcherPriority.Normal, "GLIB_SIGNAL_CALLBACK", "CheckSignaled"));

    Run("glib-background-callback-exception-propagation", () =>
        VerifyGlibException(Avalonia.Threading.DispatcherPriority.Background, "GLIB_BACKGROUND_CALLBACK", "RequestBackgroundProcessing"));

    object VerifyGlibException(Avalonia.Threading.DispatcherPriority priority, string marker, string nativeCallback)
    {
        var observed = false;
        var capturedAcrossBoundary = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => throw new NativeCallbackTestException(marker), priority);
        try { PumpUntil(() => false); }
        catch (NativeCallbackTestException exception) when (exception.Message == marker)
        {
            observed = true;
            capturedAcrossBoundary = exception.StackTrace?.Contains(nativeCallback, StringComparison.Ordinal) == true &&
                exception.StackTrace.Contains("NativeCallbackExceptions.ThrowPending", StringComparison.Ordinal);
        }
        var avaloniaRecovered = false;
        var wpfRecovered = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => avaloniaRecovered = true, Avalonia.Threading.DispatcherPriority.Normal);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => wpfRecovered = true));
        PumpUntil(() => avaloniaRecovered && wpfRecovered);
        Require(observed && capturedAcrossBoundary && dispatcher.CheckAccess(), "The GLib callback exception did not return safely through the native boundary to the WPF frame.");
        return new { observed, capturedAcrossBoundary, avaloniaRecovered, wpfRecovered, callback = nativeCallback };
    }

    Run("native-close-and-actual-wpf-shutdown", () =>
    {
        var shutdownEvents = new List<string>();
        dispatcher.ShutdownStarted += (_, _) => shutdownEvents.Add("started");
        dispatcher.ShutdownFinished += (_, _) => shutdownEvents.Add("finished");
        var pending = dispatcher.BeginInvoke(DispatcherPriority.Inactive, new Action(() => throw new InvalidOperationException("Inactive work executed.")));
        expectedClose = true;
        window.Close();
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        Dispatcher.Run();
        Require(opened && closed && dispatcher.HasShutdownFinished && pending.Status == DispatcherOperationStatus.Aborted && shutdownEvents.SequenceEqual(["started", "finished"]), "Native close or actual WPF shutdown failed.");
        return new { opened, closed, dispatcher.HasShutdownFinished, pendingStatus = pending.Status.ToString(), shutdownEvents };
    });
    completed = true;
}
catch (Exception exception)
{
    failed = true;
    checks.Add(new { name = "host-error", status = "fail", detail = exception.ToString() });
}
finally
{
    if (window is { IsVisible: true })
    {
        expectedClose = true;
        window.Close();
    }
    var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
    if (dispatcher is not null && !dispatcher.HasShutdownFinished)
    {
        try { dispatcher.InvokeShutdown(); }
        catch (Exception exception) { failed = true; checks.Add(new { name = "cleanup-error", status = "fail", detail = exception.ToString() }); }
    }
}
completed &= !interrupted;
failed |= !completed;
checks.Add(new { name = "all-checks-completed", status = completed ? "pass" : "fail", detail = "The synchronous owner-thread test sequence must finish before a successful exit." });
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "real-wpf-native-dispatcher-integration",
    os = RuntimeInformation.OSDescription,
    display = Environment.GetEnvironmentVariable("DISPLAY"),
    scope = "Actual fork WindowsBase Dispatcher and DependencyObject with pinned Avalonia 12.1.4 X11/GLib native event pump; XTest OS-injected input",
    unproven = "WPF Window, InputManager, routed input events, controls, native capture, physical keyboard qualification and full GTK integration",
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void Run(string name, Func<object> test)
{
    try { checks.Add(new { name, status = "pass", detail = test() }); }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}

static void PumpUntil(Func<bool> condition)
{
    var frame = new DispatcherFrame();
    var clock = Stopwatch.StartNew();
    var poll = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(5) };
    poll.Tick += (_, _) => { if (condition() || clock.ElapsedMilliseconds >= 3000) frame.Continue = false; };
    poll.Start();
    try { Dispatcher.PushFrame(frame); }
    finally { poll.Stop(); }
    Require(condition(), "The native/WPF condition did not complete within three seconds.");
}

static void PumpFor(TimeSpan duration)
{
    var clock = Stopwatch.StartNew();
    PumpUntil(() => clock.Elapsed >= duration);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class ProbeApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

internal sealed class Fixture : DependencyObject
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(int), typeof(Fixture), new PropertyMetadata(0));
}

internal sealed class NativeCallbackTestException(string marker = "DIRECT_INPUT_CALLBACK") : Exception(marker);

internal sealed class X11Input : IDisposable
{
    private readonly IntPtr _display;
    private readonly uint _key;

    internal X11Input()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) throw new InvalidOperationException("XOpenDisplay failed for the test input source.");
        try
        {
            if (XTestQueryExtension(_display, out _, out _, out _, out _) == 0)
                throw new InvalidOperationException("The X server does not expose the XTest extension.");
            _key = XKeysymToKeycode(_display, 0x61);
            if (_key == 0) throw new InvalidOperationException("The X server has no keycode for the test 'a' key.");
        }
        catch { XCloseDisplay(_display); throw; }
    }

    internal void Focus(IntPtr window) { XSetInputFocus(_display, window, 2, 0); XSync(_display, 0); }
    internal IntPtr CurrentFocus() { XGetInputFocus(_display, out var focus, out _); return focus; }
    internal void PressAndRelease()
    {
        var pressed = XTestFakeKeyEvent(_display, _key, 1, 0);
        var released = XTestFakeKeyEvent(_display, _key, 0, 0);
        if (pressed == 0 || released == 0)
            throw new InvalidOperationException("XTest rejected the native key press/release request.");
        XSync(_display, 0);
    }
    public void Dispose() => XCloseDisplay(_display);

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr displayName);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSetInputFocus(IntPtr display, IntPtr focus, int revertTo, nuint time);
    [DllImport("libX11.so.6")] private static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, int discard);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
    [DllImport("libXtst.so.6")] private static extern int XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, nuint delay);
}
