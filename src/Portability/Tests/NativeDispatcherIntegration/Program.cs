extern alias WpfFork;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using DependencyObject = WpfFork::System.Windows.DependencyObject;
using DependencyProperty = WpfFork::System.Windows.DependencyProperty;
using PropertyMetadata = WpfFork::System.Windows.PropertyMetadata;
using Dispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using DispatcherFrame = WpfFork::System.Windows.Threading.DispatcherFrame;
using DispatcherPriority = WpfFork::System.Windows.Threading.DispatcherPriority;
using DispatcherTimer = WpfFork::System.Windows.Threading.DispatcherTimer;
using DispatcherOperationStatus = WpfFork::System.Windows.Threading.DispatcherOperationStatus;
using DispatcherSynchronizationContext = WpfFork::System.Windows.Threading.DispatcherSynchronizationContext;

if (args.FirstOrDefault() == "--late-host-profile-only") return LateHostProfile();

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
    using var fixtureServer = new X11Fixture("little");
    NativeWindowHost.InitializeForCurrentThread();
    var fontPath = args.Single(value => value.StartsWith("--font=", StringComparison.Ordinal))[7..];
    Require(Path.IsPathFullyQualified(fontPath) && File.Exists(fontPath), "Pass --font=<absolute pinned DejaVuSans.ttf>.");
    var font = new FontFamily(new Uri(fontPath), "DejaVu Sans");
    var dispatcher = Dispatcher.CurrentDispatcher;
    var owner = Environment.CurrentManagedThreadId;
    var fixture = new Fixture();
    var input = new TextBox { OverridesDefaultStyle = true, Height = 60, Template = TextTemplate(), FontFamily = font };
    var status = new TextBlock { Text = "The actual WPF dispatcher owns this native event pump." };
    window = new Window
    {
        Title = "Real WPF dispatcher native integration",
        Width = 560,
        Height = 170,
        FontFamily = font,
        Content = new StackPanel { Margin = new Thickness(16), Children = { status, input } }
    };
    var keyDown = 0;
    var keyUp = 0;
    var textEvents = 0;
    var activated = 0;
    var deactivated = 0;
    var callbackOwner = true;
    Action? keyDownHook = null;
    Action? keyUpHook = null;
    window.Loaded += (_, _) => opened = true;
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
    window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) =>
    {
        keyDown++;
        callbackOwner &= dispatcher.CheckAccess() && Environment.CurrentManagedThreadId == owner;
        fixture.SetValue(Fixture.ValueProperty, (int)fixture.GetValue(Fixture.ValueProperty) + 1);
        keyDownHook?.Invoke();
    }), true);
    window.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler((_, _) => { keyUp++; keyUpHook?.Invoke(); }), true);
    window.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, _) => textEvents++), true);
    window.Show();
    var xid = OwnedNativeAccess.Handle(PresentationSource.FromVisual(window)!);
    Require(xid != IntPtr.Zero, "The owned host did not create an actual X11 window.");
    using var nativeInput = new X11Input();
    PumpUntil(() => input.ActualHeight == 60 && window.IsLoaded);
    window.Activate();
    nativeInput.Focus(xid);
    fixtureServer.Click(xid, 100, 60);
    PumpUntil(() => nativeInput.CurrentFocus() == xid && input.IsKeyboardFocused);

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
        var backend = typeof(NativeDispatcherHost).Assembly;
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name!.StartsWith("Avalonia", StringComparison.Ordinal)), "An Avalonia assembly entered the owned host runtime.");
        return new { assembly = typeof(Dispatcher).Assembly.FullName, path = typeof(Dispatcher).Assembly.Location, moduleVersionId = typeof(Dispatcher).Assembly.ManifestModule.ModuleVersionId, owner, backend = backend.FullName, backendPath = backend.Location };

    });

    Run("same-thread-send-is-inline-without-native-pumping", () =>
    {
        var native = OwnedNativeAccess.Pump;
        var downBefore = keyDown;
        var upBefore = keyUp;
        var queuedRan = false;
        var queued = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => queuedRan = true));
        nativeInput.PressAndRelease();
        Require((bool)OwnedNativeAccess.Get(native, "HasPendingInput"), "The Send case has no pending native input to detect accidental pumping.");
        var previousContext = SynchronizationContext.Current;
        var callbackThread = 0;
        var value = dispatcher.Invoke(() =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            Require(!queuedRan && keyDown == downBefore && keyUp == upBefore, "Send pumped queued WPF or native callbacks before its inline delegate.");
            return 42;
        }, DispatcherPriority.Send);
        Require(value == 42 && callbackThread == owner && ReferenceEquals(SynchronizationContext.Current, previousContext), "Same-thread Send did not return synchronously on its owner thread or restore its context.");
        Require(!queuedRan && queued.Status == DispatcherOperationStatus.Pending && keyDown == downBefore && keyUp == upBefore && (bool)OwnedNativeAccess.Get(native, "HasPendingInput"), "Same-thread Send pumped WPF or native work before returning.");
        PumpUntil(() => queuedRan && keyDown > downBefore && keyUp > upBefore);
        return new { value, callbackThread, queuedStatusAfterExplicitPump = queued.Status.ToString(), nativeDeliveryAfterExplicitPump = true };
    });

    Run("disable-processing-blocks-native-frame-and-wait-pumping", () =>
    {
        var native = OwnedNativeAccess.Pump;
        var downBefore = keyDown;
        var upBefore = keyUp;
        var queuedRan = false;
        var blockedFrame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { queuedRan = true; blockedFrame.Continue = false; }));
        nativeInput.PressAndRelease();
        Require((bool)OwnedNativeAccess.Get(native, "HasPendingInput"), "The disabled-processing case has no pending native input.");
        var frameRejected = false;
        var waitAnyIndex = -1;
        var waitAllResult = -1;
        using (dispatcher.DisableProcessing())
        {
            try { Dispatcher.PushFrame(blockedFrame); }
            catch (InvalidOperationException) { frameRejected = true; }
            Require(frameRejected && !queuedRan, "DisableProcessing allowed an actual WPF frame to pump native or managed work.");
            using var first = new ManualResetEvent(false);
            using var second = new ManualResetEvent(false);
            var context = new DispatcherSynchronizationContext(dispatcher);
            IntPtr[] handles = [first.SafeWaitHandle.DangerousGetHandle(), second.SafeWaitHandle.DangerousGetHandle()];
            Require(context.Wait(handles, false, 0) == WaitHandle.WaitTimeout, "Disabled WaitAny ignored unsignaled handles.");
            var signal = Task.Run(() => { Thread.Sleep(40); second.Set(); });
            waitAnyIndex = context.Wait(handles, false, 2000);
            signal.GetAwaiter().GetResult();
            Require(waitAnyIndex == 1, "Disabled WaitAny did not observe the worker-signaled second handle.");
            Require(context.Wait(handles, true, 0) == WaitHandle.WaitTimeout, "Disabled WaitAll ignored the unsignaled first handle.");
            first.Set();
            waitAllResult = context.Wait(handles, true, 0);
            Require(waitAllResult == 0, "Disabled WaitAll did not observe both signaled handles.");
            first.Reset(); second.Reset();
            Require(context.Wait(handles, false, 0) == WaitHandle.WaitTimeout, "Disabled WaitAny ignored reset handles.");
            Require(!queuedRan && keyDown == downBefore && keyUp == upBefore && (bool)OwnedNativeAccess.Get(native, "HasPendingInput"), "A disabled wait dispatched queued WPF or native input callbacks.");
        }
        PumpUntil(() => queuedRan && keyDown > downBefore && keyUp > upBefore);
        return new { frameRejected, waitAnyIndex, waitAllResult, processingResumed = queuedRan, nativeDeliveryAfterReenable = true };
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
        var nativePending = OwnedNativeAccess.HasPendingInput;
        Require(nativePending, "The actual X11 queue failed to report the injected pending native input.");
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { nativeBeforeBackground = keyDown > downBefore; order.Add("background"); }));
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => order.Add("input")));
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => order.Add("normal")));
        try { PumpUntil(() => order.Count == 3 && keyDown > downBefore && keyUp > upBefore && textEvents > textBefore); }
        catch (Exception exception) { throw new InvalidOperationException($"Native delivery diagnostics: down={keyDown - downBefore}, up={keyUp - upBefore}, text={textEvents - textBefore}, order={string.Join(',', order)}, focused={input.IsKeyboardFocused}, active={window.IsActive}, nativeFocus={nativeInput.CurrentFocus()}, xid={xid}.", exception); }
        Require(order.SequenceEqual(["normal", "input", "background"]) && nativeBeforeBackground, "Native input or the actual WPF priority queue was bypassed.");
        Require(callbackOwner && input.Text?.Contains('a') == true && (int)fixture.GetValue(Fixture.ValueProperty) > 7, "The native callback did not update the actual WPF dependency object on its owner thread.");
        return new { nativePending, keyPresses = keyDown - downBefore, keyReleases = keyUp - upBefore, textEvents = textEvents - textBefore, callbackOwner, nativeBeforeBackground, order, delivery = "OS-injected XTest events through the actual X server; real WPF InputManager; not physical keyboard input" };
    });

    Run("native-focus-loss-and-regain", () =>
    {
        var lostBefore = deactivated;
        var gainedBefore = activated;
        var otherInput = new TextBox { OverridesDefaultStyle = true, Template = TextTemplate(), FontFamily = font };
        var other = new Window { Title = "Native focus destination", Left = 600, Top = 240, Width = 260, Height = 100, WindowStartupLocation = WindowStartupLocation.Manual, Content = otherInput };
        var otherKeys = 0;
        other.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => otherKeys++), true);
        try
        {
            other.Show();
            var otherHandle = OwnedNativeAccess.Handle(PresentationSource.FromVisual(other)!);
            nativeInput.Focus(otherHandle);
            PumpUntil(() => otherInput.ActualWidth > 0 && other.IsLoaded);
            fixtureServer.Click(otherHandle, 100, 50);
            PumpUntil(() => otherInput.IsKeyboardFocused);
            Require(nativeInput.CurrentFocus() == otherHandle, "The X server did not move input focus to the other native window.");
            var keysBefore = keyDown;
            nativeInput.PressAndRelease();
            PumpUntil(() => otherKeys > 0);
            Require(keyDown == keysBefore, "The original host received a key event targeted at the other focused native window.");
            nativeInput.Focus(xid);
            fixtureServer.Click(xid, 100, 60);
            PumpUntil(() => input.IsKeyboardFocused);
            Require(nativeInput.CurrentFocus() == xid, "The X server did not return input focus to the host.");
            nativeInput.PressAndRelease();
            PumpUntil(() => keyDown > keysBefore);
            return new { lost = true, regained = true, otherKeys, deactivatedCallbacks = deactivated - lostBefore, activatedCallbacks = activated - gainedBefore, focus = "Actual X server input focus and key routing between two native windows; WM activation recorded separately." };
        }
        finally
        {
            other.Close();
            nativeInput.Focus(xid);
            fixtureServer.Click(xid, 100, 60);
            PumpUntil(() => input.IsKeyboardFocused);
        }
    });

    Run("repeated-native-pending-probes-preserve-delivery", () =>
    {
        var native = OwnedNativeAccess.Pump;
        var probes = 0;
        for (var i = 0; i < 3; i++)
        {
            var beforeDown = keyDown;
            var beforeUp = keyUp;
            var background = false;
            nativeInput.PressAndRelease();
            var deadline = Stopwatch.StartNew();
            while (!(bool)OwnedNativeAccess.Get(native, "HasPendingInput") && deadline.ElapsedMilliseconds < 200) Thread.Sleep(1);
            for (var probe = 0; probe < 8; probe++)
            {
                Require((bool)OwnedNativeAccess.Get(native, "HasPendingInput"), "A repeated native queue probe lost pending Xlib input before dispatch.");
                probes++;
            }
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => background = true));
            PumpUntil(() => keyDown > beforeDown && keyUp > beforeUp && background);
        }
        return new { probes, rounds = 3, delivery = "XPending may move socket data into the actual Xlib event queue; native delivery must still complete." };
    });

    Run("bounded-native-and-posted-callback-fairness", () =>
    {
        var observations = new List<object>();
        foreach (int budget in new[] { 1, 32 })
        {
            PumpFor(TimeSpan.FromMilliseconds(20));
            int posts = 0;
            int nativeStart = keyDown + keyUp;
            OwnedNativeAccess.Post(() => posts++);
            for (int turn = 0; turn < 4 && (posts == 0 || keyDown + keyUp == nativeStart); turn++)
            {
                for (int pair = 0; pair < 16; pair++) nativeInput.PressAndRelease();
                DispatchTurn("native-pressure");
            }
            Require(posts == 1 && keyDown + keyUp > nativeStart,
                $"Posted work starved under actual XTest backlog with dispatch budget {budget}.");
            PumpFor(TimeSpan.FromMilliseconds(20));
            int nativeBefore = keyDown + keyUp, postsBefore = posts;
            bool replenish = true;
            Action? posted = null;
            posted = () => { posts++; if (replenish) OwnedNativeAccess.Post(posted!); };
            OwnedNativeAccess.Post(posted);
            nativeInput.PressAndRelease();
            try
            {
                for (int turn = 0; turn < 4 && (keyDown + keyUp == nativeBefore || posts == postsBefore); turn++)
                    DispatchTurn("posted-pressure");
                Require(keyDown + keyUp > nativeBefore && posts > postsBefore,
                    $"Native input starved under self-posting work with dispatch budget {budget}.");
            }
            finally { replenish = false; PumpFor(TimeSpan.FromMilliseconds(20)); }
            observations.Add(new { budget, nativeTransitions = keyDown + keyUp - nativeStart, posts,
                actualXTestBacklog = true, bothQueuesProgressed = true });

            void DispatchTurn(string pressure)
            {
                int nativeBeforeTurn = keyDown + keyUp, postsBeforeTurn = posts;
                int dispatched = (int)OwnedNativeAccess.Call(OwnedNativeAccess.Connection, "DispatchBatch", budget)!;
                Require(dispatched > 0 && dispatched <= budget && keyDown + keyUp - nativeBeforeTurn + posts - postsBeforeTurn <= budget,
                    $"The combined native/callback dispatch cap changed: {pressure}, budget={budget}, dispatched={dispatched}.");
            }
        }
        return observations;
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
        var child = new Window { Title = "Owned native modal", Width = 280, Height = 100, Owner = window, FontFamily = font };
        var childOpened = false;
        var childClosed = false;
        var ownerDuringModal = false;
        child.Loaded += (_, _) =>
        {
            childOpened = true;
            ownerDuringModal = child.Owner == window && window.OwnedWindows.Count == 1;
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => child.DialogResult = true));
        };
        child.Closed += (_, _) => childClosed = true;
        var modal = child.ShowDialog();
        Require(modal == true && childOpened && childClosed && ownerDuringModal, $"Owned modal diagnostics: result={modal}, opened={childOpened}, closed={childClosed}, ownerDuringModal={ownerDuringModal}.");
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
        var clock = Stopwatch.StartNew();
        var target = OwnedNativeAccess.Get(PresentationSource.FromVisual(window)!, "_rasterTarget");
        var renderedEvent = target.GetType().GetEvent("FrameRendered", OwnedNativeAccess.Members)!;
        Action rendered = () => renderFrames++;
        renderedEvent.GetAddMethod(true)!.Invoke(target, [rendered]);
        var renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(12) };
        renderTimer.Tick += (_, _) => status.Text = $"Retained native frame {renderFrames}; background runs {backgroundRuns}; tick {clock.ElapsedTicks}";
        void Background()
        {
            backgroundRuns++;
            var received = keyDown - downBefore;
            if (received > 0 && nativeAtFirstBackground < 0) nativeAtFirstBackground = received;
            nativeAtLastBackground = received;
            if (clock.ElapsedMilliseconds < 500)
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Background));
        }
        renderTimer.Start();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Background));
        var producer = Task.Run(() =>
        {
            using var injected = new X11Input();
            for (var i = 0; i < 25; i++) { injected.PressAndRelease(); Thread.Sleep(15); }
        });
        try { PumpUntil(() => producer.IsCompleted && clock.ElapsedMilliseconds >= 550); producer.GetAwaiter().GetResult(); }
        finally { renderTimer.Stop(); renderedEvent.GetRemoveMethod(true)!.Invoke(target, [rendered]); }
        Require(keyDown - downBefore >= 20 && renderFrames >= 2 && backgroundRuns > 1 && nativeAtFirstBackground >= 1 && nativeAtLastBackground > nativeAtFirstBackground,
            $"Progress diagnostics: nativeKeys={keyDown - downBefore}, renders={renderFrames}, background={backgroundRuns}, first={nativeAtFirstBackground}, last={nativeAtLastBackground}.");
        return new { nativeKeyPresses = keyDown - downBefore, renderFrames, backgroundRuns, nativeAtFirstBackground, nativeAtLastBackground, elapsedMilliseconds = clock.ElapsedMilliseconds };
    });

    Run("direct-managed-input-exception-propagation", () =>
    {
        var observed = false;
        var expected = new NativeCallbackTestException();
        keyDownHook = () => { keyDownHook = null; throw expected; };
        nativeInput.PressAndRelease();
        try { PumpUntil(() => false); }
        catch (NativeCallbackTestException error) when (ReferenceEquals(error, expected)) { observed = true; }
        finally { keyDownHook = null; }
        var recovered = false;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => recovered = true));
        var beforeDown = keyDown; var beforeUp = keyUp;
        nativeInput.PressAndRelease();
        PumpUntil(() => recovered && keyDown > beforeDown && keyUp > beforeUp);
        Require(observed && dispatcher.CheckAccess(), "The direct managed input callback exception was swallowed or escaped the owner thread.");
        return new { observed, exactExceptionIdentity = true, recovered, nativeInputRecovered = true, delivery = "Direct managed dispatch from the source input provider into the WPF frame; this does not exercise a GLib C callback." };
    });

    Run("glib-signal-callback-exception-propagation", () => VerifyGlibException(false, "GLIB_SIGNAL_CALLBACK"));
    Run("glib-background-callback-exception-propagation", () => VerifyGlibException(true, "GLIB_BACKGROUND_CALLBACK"));

    object VerifyGlibException(bool delayed, string marker)
    {
        var expected = new NativeCallbackTestException(marker);
        var observed = false;
        var capturedAcrossBoundary = false;
        using var callback = new NativeBoundaryCallback(OwnedNativeAccess.Connection, expected, delayed);
        try { PumpUntil(() => false); }
        catch (NativeCallbackTestException exception) when (ReferenceEquals(exception, expected))
        {
            observed = true;
            capturedAcrossBoundary = exception.StackTrace?.Contains("InvokeFromGlib", StringComparison.Ordinal) == true &&
                exception.StackTrace.Contains("ThrowPendingException", StringComparison.Ordinal);
        }
        var nativeRecovered = false;
        var wpfRecovered = false;
        var beforeDown = keyDown;
        var beforeUp = keyUp;
        OwnedNativeAccess.Post(() => nativeRecovered = true);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => wpfRecovered = true));
        nativeInput.PressAndRelease();
        PumpUntil(() => nativeRecovered && wpfRecovered && keyDown > beforeDown && keyUp > beforeUp);
        Require(observed && capturedAcrossBoundary && callback.ReturnedToNative && dispatcher.CheckAccess(), "The GLib callback did not return to C before the original exception reached the WPF frame.");
        return new { observed, exactExceptionIdentity = true, firstExceptionPreserved = true, capturedAcrossBoundary,
            callback.ReturnedToNative, nativeRecovered, wpfRecovered, nativeInputRecovered = true,
            callback = delayed ? "owned-context timeout source" : "owned-context idle source",
            scope = "Actual fixture GLib C callback using production exception capture/throw boundary; upstream signal/background callbacks no longer exist." };
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
    scope = "Actual WPF WindowsBase Dispatcher, Window, TextBox and InputManager with owned X11/GLib event pump; XTest OS-injected input",
    unproven = "Physical keyboard qualification, GNOME/XWayland desktop behavior and native IME",
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

static ControlTemplate TextTemplate()
{
    var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
    return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
}

static int LateHostProfile()
{
    object? detail = null;
    Exception? failure = null;
    using var watchdog = new Timer(_ => Environment.Exit(9), null, 15000, Timeout.Infinite);
    try
    {
        using var native = new X11Fixture("little");
        Require(Dispatcher.FromThread(Thread.CurrentThread) is null, "The late-host probe already has a Dispatcher.");
        bool highContrast = SystemParameters.HighContrast;
        var provider = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider", true)!;
        object profile = provider.GetProperty("Current", OwnedNativeAccess.Members)!.GetValue(null)!;
        VerifyRejection();
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try { VerifyRejection(); }
            catch (Exception error) { workerFailure = error; }
        }) { IsBackground = true };
        worker.Start();
        Require(worker.Join(5000), "The second rejected native host did not finish cleanup.");
        if (workerFailure is not null) throw new InvalidOperationException("The second late native initialization did not preserve rejection and cleanup.", workerFailure);
        Require(ReferenceEquals(profile, provider.GetProperty("Current", OwnedNativeAccess.Members)!.GetValue(null)) &&
            SystemParameters.HighContrast == highContrast, "Rejected native initialization replaced the consumed settings snapshot.");
        detail = new { rejections = 2, profile = OwnedNativeAccess.Get(profile, "ProfileName"), settingsUnchanged = true, nativeConnectionsDisposed = true };
    }
    catch (Exception error) { failure = error; }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "late-first-native-host-settings-registration",
        scope = "Fresh process, genuine public WPF system-setting consumption and actual owned native startup/cleanup",
        checks = new[] { new { name = "late-first-host-rejects-consumed-settings-and-cleans-up", status = failure is null ? "pass" : "fail", detail = failure is null ? detail : failure.ToString() } }
    }, new JsonSerializerOptions { WriteIndented = true }));
    return failure is null ? 0 : 1;

    static void VerifyRejection()
    {
        Exception? rejected = null;
        try { NativeWindowHost.InitializeForCurrentThread(); }
        catch (Exception error) { rejected = error; }
        finally
        {
            var current = Dispatcher.FromThread(Thread.CurrentThread);
            if (current is not null && !current.HasShutdownFinished) current.InvokeShutdown();
        }
        Require(rejected is InvalidOperationException && rejected.Message.Contains("already been consumed", StringComparison.Ordinal),
            "Late first native initialization did not preserve the consumed-profile rejection: " + rejected);
        var session = typeof(NativeDispatcherHost).GetField("s_session", OwnedNativeAccess.Members)!.GetValue(null)!;
        Require((bool)OwnedNativeAccess.Get(OwnedNativeAccess.Get(session, "Connection"), "_disposed"),
            "Rejected native initialization retained its X11 connection.");
        Require(Dispatcher.FromThread(Thread.CurrentThread)!.HasShutdownFinished, "Rejected native initialization retained its WPF dispatcher.");
    }
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
