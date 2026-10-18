using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal;
using MS.Internal.Portability.Input;
using MS.Internal.Portability.Settings;
using MS.Internal.Portability.Windowing;

PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(pointerSettings: new PortablePointerSettings(4, 6, 500)));
var results = new List<object>();
Exception? failure = null;
var thread = new Thread(() =>
{
    try { Exercise(); }
    catch (Exception exception) { failure = exception; }
});
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
string[] windowsNativeMappings = OperatingSystem.IsLinux() ? File.ReadLines("/proc/self/maps")
    .Where(line => line.Contains("PresentationNative_cor3", StringComparison.OrdinalIgnoreCase) || line.Contains("wpfgfx_cor3", StringComparison.OrdinalIgnoreCase) || line.Contains("user32.dll", StringComparison.OrdinalIgnoreCase)).ToArray() : [];
if (windowsNativeMappings.Length != 0) failure ??= new InvalidOperationException("Windows native modules were mapped by the portable input fixture.");
Console.WriteLine(JsonSerializer.Serialize(new { fixture = "real-wpf-input-report-pipeline", core = typeof(InputManager).Assembly.Location,
    windowsBase = typeof(Dispatcher).Assembly.Location, windowsNativeMappings, results, failure = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
return failure == null ? 0 : 1;

void Exercise()
{
    Run("dispatcher-owned-input-manager", () => Require(ReferenceEquals(InputManager.Current, InputManager.Current), "InputManager was not dispatcher-owned."));
    Run("culture-input-language-and-event-subscription", () =>
    {
        var languages = InputLanguageManager.Current;
        InputLanguageEventHandler handler = (_, _) => { };
        languages.InputLanguageChanged += handler;
        languages.InputLanguageChanged -= handler;
        Require(languages.CurrentInputLanguage.Equals(System.Globalization.CultureInfo.CurrentCulture), "Input language did not use the actual dispatcher culture.");
        var otherCulture = System.Globalization.CultureInfo.CurrentCulture.Name == "en-US" ? new System.Globalization.CultureInfo("de-DE") : new System.Globalization.CultureInfo("en-US");
        Expect<PlatformNotSupportedException>(() => languages.CurrentInputLanguage = otherCulture);
    });
    Run("custom-input-language-source-retains-real-event-lifetime", () =>
    {
        var manager = InputLanguageManager.Current;
        var original = manager.Source;
        var custom = new LanguageSource();
        InputLanguageEventHandler handler = (_, _) => { };
        try
        {
            manager.RegisterInputLanguageSource(custom);
            manager.InputLanguageChanged += handler;
            manager.InputLanguageChanged -= handler;
            Require(custom.Initializations == 1 && custom.Uninitializations == 1, "Actual custom language-list enumeration did not preserve subscription lifetime.");
        }
        finally { manager.RegisterInputLanguageSource(original); }
    });
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    host.Source = source;
    var root = new InputRoot();
    source.RootVisual = root;
    root.Measure(new Size(100, 40));
    root.Arrange(new Rect(0, 0, 100, 40));
    root.UpdateLayout();
    var provider = (PortableInputProvider)source.GetInputProvider(typeof(KeyboardDevice));
    Run("single-provider-real-interfaces", () =>
    {
        Require(ReferenceEquals(provider, source.GetInputProvider(typeof(MouseDevice))), "Keyboard and mouse use different providers.");
        Require(!provider.SetCursorVisibility(false) && !provider.IsCursorHidden, "Host without optional native visibility capability claimed hidden cursor success.");
    });
    Run("accessibility-boundary-does-not-fabricate-native-peers", () =>
    {
        Require(!System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.AutomationFocusChanged), "An inaccessible backend claimed an automation listener.");
        var peer = new System.Windows.Automation.Peers.UIElementAutomationPeer(root.First);
        Expect<PlatformNotSupportedException>(() => peer.ProviderFromPeer(peer));
    });
    Run("missing-host-input-capabilities-fail-explicitly", () =>
    {
        using var renderingSource = new PortablePresentationSource(_ => { });
        renderingSource.AttachHost(new RenderingHost());
        var renderingProvider = (PortableInputProvider)renderingSource.GetInputProvider(typeof(MouseDevice));
        Require(!renderingProvider.AcquireFocus(false) && !renderingProvider.CaptureMouse() && !renderingProvider.SetCursor(Cursors.Arrow), "Rendering-only host claimed native input capabilities.");
        Expect<PlatformNotSupportedException>(() => renderingProvider.ClientToScreen(new Point(1, 2)));
    });
    Run("focus-acquire-check-failure-and-two-elements", () =>
    {
        Require(!provider.AcquireFocus(true) && host.FocusRequests == 0, "checkOnly acquired native focus.");
        host.AllowFocus = false;
        Require(!ReferenceEquals(Keyboard.Focus(root.First), root.First), "Failed native acquisition reported focus.");
        host.AllowFocus = true;
        Require(ReferenceEquals(Keyboard.Focus(root.First), root.First), "First element did not acquire actual focus.");
        Require(provider.AcquireFocus(true), "Focused host did not answer checkOnly.");
        Require(ReferenceEquals(Keyboard.Focus(root.Second), root.Second), "Second element did not acquire focus.");
        Keyboard.Focus(root.First);
    });
    Run("public-uielement-focus-keeps-windows-touch-keyboard-inapplicable", () =>
    {
        Require(root.First.Focus() && ReferenceEquals(Keyboard.FocusedElement, root.First), "Public UIElement.Focus did not retain real keyboard focus.");
        MS.Internal.Interop.TipTsfHelper.Hide(root.First);
    });
    Run("programmatic-capture-before-mouse-input-can-be-lost", () =>
    {
        Require(Mouse.Capture(root.First), "Programmatic native capture failed before first mouse input.");
        host.Report(new RawMouseInputReport(InputMode.Foreground, 1, source, RawMouseActions.CancelCapture, 20, 12, 0, IntPtr.Zero));
        Require(Mouse.Captured == null, "Native capture loss before first mouse input left WPF capture stuck.");
    });
    Run("capture-snapshot-query-failure-releases-grab-and-recovers", () =>
    {
        MouseReport(RawMouseActions.Deactivate, 20, 12);
        int releases = host.ReleaseCalls;
        host.ThrowOnPointerQuery = true;
        Expect<InvalidOperationException>(() => Mouse.Capture(root.First));
        host.ThrowOnPointerQuery = false;
        Require(host.ReleaseCalls == releases + 1 && !host.HasNativeCapture && Mouse.Captured == null, "Failed pointer query leaked native or WPF capture.");
        Require(Mouse.Capture(root.First) && ReferenceEquals(Mouse.Captured, root.First), "Capture could not recover after a failed native pointer query.");
        Mouse.Capture(null);
    });
    Run("capture-report-handler-failure-releases-grab-and-recovers", () =>
    {
        MouseReport(RawMouseActions.Deactivate, 20, 12);
        int releases = host.ReleaseCalls;
        MouseEventHandler fail = (_, _) => throw new InvalidOperationException("Mouse move handler failed.");
        root.First.MouseMove += fail;
        try { Expect<InvalidOperationException>(() => Mouse.Capture(root.First)); }
        finally { root.First.MouseMove -= fail; }
        Require(host.ReleaseCalls == releases + 1 && !host.HasNativeCapture && Mouse.Captured == null && Mouse.DirectlyOver == null, "Report handler failure leaked capture or active hit state.");
        Require(Mouse.Capture(root.First) && ReferenceEquals(Mouse.Captured, root.First), "Capture could not recover after report handler failure.");
        Mouse.Capture(null);
    });
    Run("failed-explicit-release-remains-owned-and-retries", () =>
    {
        Require(Mouse.Capture(root.First) && host.HasNativeCapture, "Fixture did not own native capture.");
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        int releases = host.ReleaseCalls;
        host.ThrowOnRelease = true;
        Expect<InvalidOperationException>(() => Mouse.Capture(null));
        Require(host.HasNativeCapture && ReferenceEquals(Mouse.Captured, root.First) && Mouse.LeftButton == MouseButtonState.Pressed, "Failed native release was reported as successful ownership or physical button loss.");
        host.ThrowOnRelease = false;
        Require(Mouse.Capture(null), "Retried release failed.");
        Require(host.ReleaseCalls == releases + 2 && !host.HasNativeCapture && Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Pressed, "Retried release did not release both capture owners or changed a physically held button.");
        MouseReport(RawMouseActions.Button1Release, 20, 12);
    });
    Run("native-capture-loss-during-throwing-release-is-authoritative", () =>
    {
        Require(Mouse.Capture(root.First) && host.HasNativeCapture, "Synchronous loss fixture did not capture.");
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        int releases = host.ReleaseCalls;
        host.BeforeRelease = () => MouseReport(RawMouseActions.CancelCapture, 20, 12);
        host.ThrowOnRelease = true;
        try { Expect<InvalidOperationException>(() => Mouse.Capture(null)); }
        finally { host.BeforeRelease = null; host.ThrowOnRelease = false; }
        Require(host.ReleaseCalls == releases + 1 && !host.HasNativeCapture && Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Pressed, "Actual native loss recursed, retained capture or changed physical button state.");
        MouseReport(RawMouseActions.Button1Release, 20, 12);
    });
    foreach (bool throwAfterLoss in new[] { false, true })
    Run("capture-reacquired-during-native-release-survives-" + (throwAfterLoss ? "failure" : "success"), () =>
    {
        Require(Mouse.Capture(root.First) && host.HasNativeCapture, "Initial native capture failed.");
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        int losses = 0;
        MouseEventHandler recapture = (_, _) =>
        {
            losses++;
            Require(Mouse.Capture(root.Second), "LostMouseCapture could not reacquire native capture.");
        };
        root.First.LostMouseCapture += recapture;
        host.NotifyAfterNativeRelease = true;
        host.BeforeRelease = () => MouseReport(RawMouseActions.CancelCapture, 20, 12);
        host.ThrowOnRelease = throwAfterLoss;
        try
        {
            if (throwAfterLoss) Expect<InvalidOperationException>(() => Mouse.Capture(null));
            else Require(Mouse.Capture(null), "Initial release did not complete.");
            Require(losses == 1 && ReferenceEquals(Mouse.Captured, root.Second) && host.HasNativeCapture && Mouse.LeftButton == MouseButtonState.Pressed,
                "Outer native release discarded newer capture or changed physical button state.");
        }
        finally
        {
            root.First.LostMouseCapture -= recapture;
            host.BeforeRelease = null;
            host.NotifyAfterNativeRelease = false;
            host.ThrowOnRelease = false;
        }
        int releases = host.ReleaseCalls;
        Require(Mouse.Capture(null) && Mouse.Captured == null && !host.HasNativeCapture && host.ReleaseCalls == releases + 1,
            "Newer native capture could not be released after the outer release returned.");
        MouseReport(RawMouseActions.Button1Release, 20, 12);
    });
    Run("key-preview-bubble-repeat-modifier-and-up", () =>
    {
        var route = new List<string>();
        var repeats = new List<bool>();
        root.PreviewKeyDown += (_, e) => route.Add("root-preview");
        root.First.PreviewKeyDown += (_, e) => { route.Add("child-preview"); repeats.Add(e.IsRepeat); Require(e.IsDown, "State was updated after routed handlers."); };
        root.First.KeyDown += (_, e) => { route.Add("child-bubble"); Require((e.Key != Key.A && e.Key != Key.LeftCtrl) || e.KeyboardDevice.Modifiers == ModifierKeys.Control, "Modifier state is stale."); };
        root.KeyDown += (_, _) => route.Add("root-bubble");
        SendKey(RawKeyboardActions.KeyDown, Key.LeftCtrl);
        SendKey(RawKeyboardActions.KeyDown, Key.A);
        SendKey(RawKeyboardActions.KeyDown, Key.A);
        Require(route.Skip(4).Take(4).SequenceEqual(new[] { "root-preview", "child-preview", "child-bubble", "root-bubble" }), "Preview/bubble route is wrong.");
        Require(repeats.SequenceEqual(new[] { false, false, true }), "Repeat state is wrong.");
        SendKey(RawKeyboardActions.KeyUp, Key.A);
        SendKey(RawKeyboardActions.KeyUp, Key.LeftCtrl);
        Require(Keyboard.Modifiers == ModifierKeys.None && Keyboard.IsKeyUp(Key.A), "Release did not update key state.");
    });
    Run("toggle-repeat-and-deactivation-preserves-toggle", () =>
    {
        SendKey(RawKeyboardActions.KeyDown, Key.CapsLock);
        SendKey(RawKeyboardActions.KeyDown, Key.CapsLock);
        Require(Keyboard.IsKeyToggled(Key.CapsLock), "Repeated CapsLock toggled twice.");
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        Require(Keyboard.IsKeyUp(Key.CapsLock) && Keyboard.IsKeyToggled(Key.CapsLock) && Keyboard.FocusedElement == null, "Deactivate left pressed state or dropped toggle state.");
        host.KeySnapshot[Key.CapsLock] = KeyStates.Toggled;
        SendKey(RawKeyboardActions.Activate, Key.None);
        Keyboard.Focus(root.First);
        SendKey(RawKeyboardActions.KeyDown, Key.CapsLock);
        SendKey(RawKeyboardActions.KeyUp, Key.CapsLock);
        Require(!Keyboard.IsKeyToggled(Key.CapsLock), "Second transition did not toggle off.");
        host.KeySnapshot.Clear();
    });
    Run("committed-bmp-text-real-composition-route", () =>
    {
        string text = "";
        TextCompositionManager.AddTextInputHandler(root.First, (_, e) => text += e.Text);
        host.Report(new RawTextInputReport(source, InputMode.Foreground, 50, false, false, false, '\u03A9'));
        Require(text == "\u03A9", "Committed BMP character did not use TextCompositionManager.");
        Expect<PlatformNotSupportedException>(() => host.Report(new RawTextInputReport(source, InputMode.Foreground, 51, false, false, false, '\uD83D')));
    });
    Run("activation-reconciles-native-key-snapshot", () =>
    {
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        host.KeySnapshot[Key.RightShift] = KeyStates.Down;
        host.KeySnapshot[Key.CapsLock] = KeyStates.Toggled;
        SendKey(RawKeyboardActions.Activate, Key.None);
        Require(Keyboard.Modifiers == ModifierKeys.Shift && Keyboard.IsKeyToggled(Key.CapsLock), "Activation did not reconcile preheld modifier and toggle state.");
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        Require(Keyboard.Modifiers == ModifierKeys.None && Keyboard.IsKeyToggled(Key.CapsLock), "Deactivation did not clear only pressed snapshot state.");
        host.KeySnapshot.Clear();
        SendKey(RawKeyboardActions.Activate, Key.None);
        Require(Keyboard.Modifiers == ModifierKeys.None && !Keyboard.IsKeyToggled(Key.CapsLock), "Reactivation retained obsolete native state.");
        Keyboard.Focus(root.First);
    });
    Run("deactivation-with-missing-release-resets-repeat", () =>
    {
        var repeats = new List<bool>();
        root.First.KeyDown += (_, e) => { if (e.Key == Key.F3) repeats.Add(e.IsRepeat); };
        SendKey(RawKeyboardActions.KeyDown, Key.F3);
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        SendKey(RawKeyboardActions.Activate, Key.None);
        Keyboard.Focus(root.First);
        SendKey(RawKeyboardActions.KeyDown, Key.F3);
        SendKey(RawKeyboardActions.KeyDown, Key.F3);
        SendKey(RawKeyboardActions.KeyUp, Key.F3);
        SendKey(RawKeyboardActions.KeyDown, Key.F3);
        SendKey(RawKeyboardActions.KeyUp, Key.F3);
        Require(repeats.SequenceEqual(new[] { false, false, true, false }), "Fresh, held and released presses after native deactivation have incorrect repeat state.");
    });
    Run("physical-coordinate-roundtrip-asymmetric-dpi", () =>
    {
        Point physical = new(20, 12);
        Point screen = PointUtil.ClientToScreen(physical, source);
        Require(screen == new Point(320, 212) && PointUtil.ScreenToClient(screen, source) == physical, "Nonzero screen origin did not roundtrip.");
        Require(PointUtil.ClientToRoot(physical, source) == new Point(10, 8), "Asymmetric DPI mixed physical and logical coordinates.");
    });
    Run("screen-hit-source-validation", () =>
    {
        using var unattached = new PortablePresentationSource(_ => { });
        host.OverrideHitSource = true;
        host.HitSource = unattached;
        Expect<InvalidOperationException>(() => provider.SourceFromScreenPoint(new Point(320, 212)));
        unattached.Dispose();
        Expect<InvalidOperationException>(() => provider.SourceFromScreenPoint(new Point(320, 212)));
        PortablePresentationSource? foreign = null;
        var otherThread = new Thread(() => foreign = new PortablePresentationSource(_ => { }));
        otherThread.Start(); otherThread.Join();
        host.HitSource = foreign;
        Expect<InvalidOperationException>(() => provider.SourceFromScreenPoint(new Point(320, 212)));
        host.HitSource = null;
        Require(provider.SourceFromScreenPoint(new Point(320, 212)) == null, "No native source was fabricated as a hit.");
        host.OverrideHitSource = false;
    });
    Run("real-mouse-enter-leave-and-relative-position-both-directions", () =>
    {
        int firstEnter = 0, firstLeave = 0, secondEnter = 0, secondLeave = 0;
        root.First.MouseEnter += (_, _) => firstEnter++;
        root.First.MouseLeave += (_, _) => firstLeave++;
        root.Second.MouseEnter += (_, _) => secondEnter++;
        root.Second.MouseLeave += (_, _) => secondLeave++;
        MouseReport(RawMouseActions.AbsoluteMove, 120, 12);
        Require(ReferenceEquals(Mouse.DirectlyOver, root.Second) && firstLeave == 1 && secondEnter == 1, "Real hit testing did not leave first and enter second.");
        Require(Mouse.GetPosition(root.Second) == new Point(10, 8), "Mouse position did not combine child offset and asymmetric DPI.");
        MouseReport(RawMouseActions.AbsoluteMove, 20, 12);
        Require(ReferenceEquals(Mouse.DirectlyOver, root.First) && firstEnter == 1 && secondLeave == 1, "Real hit testing did not leave second and reenter first.");
    });
    Run("activation-reconciles-preheld-buttons-without-fake-transitions", () =>
    {
        MouseReport(RawMouseActions.Deactivate, 20, 12);
        int transitions = 0;
        root.First.MouseDown += (_, _) => transitions++;
        root.First.MouseUp += (_, _) => transitions++;
        host.PointerSnapshot = new PortablePointerSnapshot(new Point(20, 12), MouseButtonState.Pressed, MouseButtonState.Pressed,
            MouseButtonState.Pressed, MouseButtonState.Pressed, MouseButtonState.Pressed);
        MouseReport(RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 20, 12);
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
            Require(ButtonState(button) == MouseButtonState.Pressed, "Activation missed preheld native button " + button);
        MouseReport(RawMouseActions.Deactivate, 20, 12);
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
            Require(ButtonState(button) == MouseButtonState.Released, "Deactivation left native button pressed " + button);
        host.PointerSnapshot = new PortablePointerSnapshot(new Point(20, 12), MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released);
        MouseReport(RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 20, 12);
        Require(transitions == 0, "State reconciliation fabricated button transition events.");
    });
    Run("mouse-hit-test-button-state-and-up", () =>
    {
        var events = new List<string>();
        root.PreviewMouseDown += (_, _) => events.Add("root-preview");
        root.First.MouseDown += (_, e) => { events.Add("child-down"); if (e.ChangedButton == MouseButton.Left) Require(e.LeftButton == MouseButtonState.Pressed, "Button state is stale."); };
        root.MouseDown += (_, _) => events.Add("root-bubble");
        MouseReport(RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 20, 12);
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        MouseReport(RawMouseActions.Button1Release, 20, 12);
        Require(events.SequenceEqual(new[] { "root-preview", "child-down", "root-bubble" }) && Mouse.LeftButton == MouseButtonState.Released, "Mouse route or release failed.");
    });
    Run("all-five-buttons-press-and-release", () =>
    {
        var buttons = new[]
        {
            (RawMouseActions.Button1Press, RawMouseActions.Button1Release, MouseButton.Left),
            (RawMouseActions.Button2Press, RawMouseActions.Button2Release, MouseButton.Right),
            (RawMouseActions.Button3Press, RawMouseActions.Button3Release, MouseButton.Middle),
            (RawMouseActions.Button4Press, RawMouseActions.Button4Release, MouseButton.XButton1),
            (RawMouseActions.Button5Press, RawMouseActions.Button5Release, MouseButton.XButton2)
        };
        MouseButton? down = null, up = null;
        root.First.MouseDown += (_, e) => down = e.ChangedButton;
        root.First.MouseUp += (_, e) => up = e.ChangedButton;
        foreach (var (press, release, button) in buttons)
        {
            MouseReport(press, 20, 12);
            Require(down == button && ButtonState(button) == MouseButtonState.Pressed, "Pressed state or event maps to the wrong mouse button: " + button);
            MouseReport(release, 20, 12);
            Require(up == button && ButtonState(button) == MouseButtonState.Released, "Released state or event maps to the wrong mouse button: " + button);
        }
    });
    Run("real-keybinding-command-dispatch", () =>
    {
        int executed = 0;
        var command = new RoutedCommand();
        root.First.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++));
        root.First.InputBindings.Add(new KeyBinding(command, Key.F2, ModifierKeys.None));
        SendKey(RawKeyboardActions.KeyDown, Key.F2);
        SendKey(RawKeyboardActions.KeyUp, Key.F2);
        Require(executed == 1, "Raw keyboard report did not execute the actual routed KeyBinding command.");
    });
    Run("capture-failure-release-and-unexpected-loss", () =>
    {
        int gained = 0, lost = 0;
        root.First.GotMouseCapture += (_, _) => gained++;
        root.First.LostMouseCapture += (_, _) => lost++;
        host.AllowCapture = false;
        Require(!Mouse.Capture(root.First), "Failed host capture claimed success.");
        host.AllowCapture = true;
        Require(Mouse.Capture(root.First) && ReferenceEquals(Mouse.Captured, root.First), "Capture failed.");
        Mouse.Capture(null);
        Require(Mouse.Captured == null && lost == 1, "Release did not route lost capture.");
        Require(Mouse.Capture(root.First), "Recapture failed.");
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        MouseReport(RawMouseActions.CancelCapture, 20, 12);
        Require(Mouse.Captured == null && !host.HasNativeCapture && Mouse.LeftButton == MouseButtonState.Pressed && gained == 2 && lost == 2, "Unexpected capture loss changed physical button state or retained capture.");
        MouseReport(RawMouseActions.Button1Release, 20, 12);
        Require(Mouse.LeftButton == MouseButtonState.Released, "Actual release after capture loss did not release the button.");
    });
    Run("deactivation-without-release-clears-both-devices", () =>
    {
        Keyboard.Focus(root.First);
        SendKey(RawKeyboardActions.KeyDown, Key.LeftShift);
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        MouseReport(RawMouseActions.Deactivate, 20, 12);
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        Require(Mouse.LeftButton == MouseButtonState.Released && Keyboard.Modifiers == ModifierKeys.None, "Deactivate left stale pressed state.");
    });
    Run("inactive-source-reports-do-not-reach-other-focus", () =>
    {
        Keyboard.Focus(root.First);
        SendKey(RawKeyboardActions.KeyDown, Key.F4);
        var otherHost = new ContractHost();
        using var otherSource = new PortablePresentationSource(_ => { });
        otherHost.Source = otherSource;
        otherSource.AttachHost(otherHost);
        var otherRoot = new InputRoot();
        otherSource.RootVisual = otherRoot;
        otherRoot.Measure(new Size(100, 40)); otherRoot.Arrange(new Rect(0, 0, 100, 40));
        Keyboard.Focus(otherRoot.First);
        int keyEvents = 0, textEvents = 0;
        var repeats = new List<bool>();
        otherRoot.First.KeyDown += (_, e) => { if (e.Key == Key.A) keyEvents++; else if (e.Key == Key.F4) repeats.Add(e.IsRepeat); };
        TextCompositionManager.AddTextInputHandler(otherRoot.First, (_, _) => textEvents++);
        otherHost.Report(new RawKeyboardInputReport(otherSource, InputMode.Foreground, 99, RawKeyboardActions.KeyDown, 0, false, false, KeyInterop.VirtualKeyFromKey(Key.F4), IntPtr.Zero));
        otherHost.Report(new RawKeyboardInputReport(otherSource, InputMode.Foreground, 99, RawKeyboardActions.KeyDown, 0, false, false, KeyInterop.VirtualKeyFromKey(Key.F4), IntPtr.Zero));
        otherHost.Report(new RawKeyboardInputReport(otherSource, InputMode.Foreground, 99, RawKeyboardActions.KeyUp, 0, false, false, KeyInterop.VirtualKeyFromKey(Key.F4), IntPtr.Zero));
        Require(repeats.SequenceEqual(new[] { false, true }), "Source activation retained the previous source's repeat state.");
        SendKey(RawKeyboardActions.KeyDown, Key.A);
        host.Report(new RawTextInputReport(source, InputMode.Foreground, 100, false, false, false, 'x'));
        SendKey(RawKeyboardActions.Deactivate, Key.None);
        Require(keyEvents == 0 && textEvents == 0 && ReferenceEquals(Keyboard.FocusedElement, otherRoot.First), "An inactive source changed the active source's focus or input.");
    });
    Run("host-release-failure-still-deactivates-and-unregisters", () =>
    {
        var failingHost = new ContractHost();
        using var failingSource = new PortablePresentationSource(_ => { });
        failingHost.Source = failingSource;
        failingSource.AttachHost(failingHost);
        var failingRoot = new InputRoot();
        failingSource.RootVisual = failingRoot;
        failingRoot.Measure(new Size(100, 40)); failingRoot.Arrange(new Rect(0, 0, 100, 40));
        Keyboard.Focus(failingRoot.First);
        failingHost.Report(new RawKeyboardInputReport(failingSource, InputMode.Foreground, 100, RawKeyboardActions.KeyDown, 0, false, false, KeyInterop.VirtualKeyFromKey(Key.LeftShift), IntPtr.Zero));
        failingHost.Report(new RawMouseInputReport(InputMode.Foreground, 100, failingSource, RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 20, 12, 0, IntPtr.Zero));
        Require(Mouse.Capture(failingRoot.First), "Failure fixture could not capture.");
        failingHost.ThrowOnRelease = true;
        Expect<InvalidOperationException>(() => failingSource.Dispose());
        Require(failingSource.IsDisposed && Keyboard.FocusedElement == null && Keyboard.Modifiers == ModifierKeys.None && Mouse.Captured == null, "Release failure prevented device and source cleanup.");
    });
    Run("native-deactivation-release-failure-still-clears-managed-state", () =>
    {
        var failingHost = new ContractHost();
        using var failingSource = new PortablePresentationSource(_ => { });
        failingHost.Source = failingSource;
        failingSource.AttachHost(failingHost);
        var failingRoot = new InputRoot();
        failingSource.RootVisual = failingRoot;
        failingRoot.Measure(new Size(100, 40)); failingRoot.Arrange(new Rect(0, 0, 100, 40));
        Require(Mouse.Capture(failingRoot.First) && failingHost.HasNativeCapture, "Native deactivation fixture did not capture.");
        failingHost.ThrowOnRelease = true;
        Expect<InvalidOperationException>(() => failingHost.Report(new RawMouseInputReport(InputMode.Foreground, 101, failingSource, RawMouseActions.Deactivate, 20, 12, 0, IntPtr.Zero)));
        Require(Mouse.Captured == null && Mouse.DirectlyOver == null && failingHost.HasNativeCapture, "Native deactivation failed to clear WPF state or falsely claimed native release.");
        failingHost.ThrowOnRelease = false;
        failingSource.Dispose();
        Require(failingSource.IsDisposed && !failingHost.HasNativeCapture && failingHost.ReleaseCalls == 2, "Source disposal did not retry failed native ownership cleanup.");
    });
    Run("capture-query-and-release-failures-retain-native-cleanup-for-disposal", () =>
    {
        var failingHost = new ContractHost { ThrowOnPointerQuery = true, ThrowOnRelease = true };
        using var failingSource = new PortablePresentationSource(_ => { });
        failingHost.Source = failingSource;
        failingSource.AttachHost(failingHost);
        var failingRoot = new InputRoot();
        failingSource.RootVisual = failingRoot;
        failingRoot.Measure(new Size(100, 40)); failingRoot.Arrange(new Rect(0, 0, 100, 40));
        Expect<InvalidOperationException>(() => Mouse.Capture(failingRoot.First));
        Require(failingHost.HasNativeCapture && Mouse.Captured == null, "Failed native capture cleanup was hidden or reported as WPF capture.");
        failingHost.ThrowOnPointerQuery = false;
        failingHost.ThrowOnRelease = false;
        failingSource.Dispose();
        Require(!failingHost.HasNativeCapture && failingHost.ReleaseCalls == 2 && failingSource.IsDisposed, "Disposal forgot native capture after both pointer query and release failed.");
    });
    Run("closed-source-during-native-focus-request-cannot-gain-focus", () =>
    {
        var closingHost = new ContractHost();
        using var closingSource = new PortablePresentationSource(_ => { });
        closingHost.Source = closingSource;
        closingSource.AttachHost(closingHost);
        var closingRoot = new InputRoot();
        closingSource.RootVisual = closingRoot;
        closingRoot.Measure(new Size(100, 40)); closingRoot.Arrange(new Rect(0, 0, 100, 40));
        closingHost.AfterAcquireFocus = () => closingSource.Dispose();
        var focused = Keyboard.Focus(closingRoot.First);
        Require(closingSource.IsDisposed && !ReferenceEquals(focused, closingRoot.First) && !ReferenceEquals(Keyboard.FocusedElement, closingRoot.First), "A source closed during native acquisition gained keyboard focus.");
    });
    Run("source-disposal-during-capture-activation-cannot-leave-capture", () =>
    {
        var closingHost = new ContractHost();
        using var closingSource = new PortablePresentationSource(_ => { });
        closingHost.Source = closingSource;
        closingSource.AttachHost(closingHost);
        var closingRoot = new InputRoot();
        closingSource.RootVisual = closingRoot;
        closingRoot.Measure(new Size(100, 40)); closingRoot.Arrange(new Rect(0, 0, 100, 40));
        closingRoot.First.MouseMove += (_, _) => closingSource.Dispose();
        Require(!Mouse.Capture(closingRoot.First), "A source disposed during real capture activation reported successful capture.");
        Require(closingSource.IsDisposed && Mouse.Captured == null && !closingHost.HasNativeCapture && closingHost.ReleaseCalls == 1, "Closing during capture activation left native or WPF capture.");
    });
    Run("wrong-source-thread-and-provider-disposal", () =>
    {
        using var other = new PortablePresentationSource(_ => { });
        Expect<ArgumentException>(() => host.Report(new RawKeyboardInputReport(other, InputMode.Foreground, 100, RawKeyboardActions.Activate, 0, false, false, 0, IntPtr.Zero)));
        Exception? wrongThread = null;
        var otherThread = new Thread(() => { try { provider.AcquireFocus(true); } catch (Exception exception) { wrongThread = exception; } });
        otherThread.Start(); otherThread.Join();
        Require(wrongThread is InvalidOperationException, "Foreign thread queried input provider.");
        Keyboard.Focus(root.First);
        SendKey(RawKeyboardActions.KeyDown, Key.LeftShift);
        MouseReport(RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 20, 12);
        MouseReport(RawMouseActions.Button1Press, 20, 12);
        Require(Mouse.Capture(root.First), "Final active capture did not succeed.");
        bool cleanupReportCompleted = false;
        host.BeforeRelease = () =>
        {
            MouseReport(RawMouseActions.CancelCapture, 20, 12);
            cleanupReportCompleted = Mouse.Captured == null;
        };
        source.Dispose();
        host.BeforeRelease = null;
        Require(cleanupReportCompleted && !host.HasNativeCapture, "Actual native capture-loss callback was not delivered while source disposal was in progress.");
        Require(Keyboard.FocusedElement == null && Keyboard.Modifiers == ModifierKeys.None && Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Released, "Disposal did not deactivate the real devices.");
        Require(!InputManager.Current.InputProviders.Cast<IInputProvider>().Contains(provider), "Disposed provider remained registered.");
        Expect<ObjectDisposedException>(() => provider.AcquireFocus(false));
        Expect<ObjectDisposedException>(() => provider.ReportInput(new RawKeyboardInputReport(source, InputMode.Foreground, 101, RawKeyboardActions.Activate, 0, false, false, 0, IntPtr.Zero)));
    });
    Dispatcher.CurrentDispatcher.InvokeShutdown();

    void SendKey(RawKeyboardActions actions, Key key) => host.Report(new RawKeyboardInputReport(source, InputMode.Foreground, Environment.TickCount, actions, 0, false, false, KeyInterop.VirtualKeyFromKey(key), IntPtr.Zero));
    void MouseReport(RawMouseActions actions, int x, int y) => host.Report(new RawMouseInputReport(InputMode.Foreground, Environment.TickCount, source, actions, x, y, 0, IntPtr.Zero));
}
void Run(string name, Action action)
{
    try { action(); results.Add(new { name, passed = true }); }
    catch (Exception exception) { results.Add(new { name, passed = false, error = exception.ToString() }); throw; }
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static MouseButtonState ButtonState(MouseButton button) => button switch
{
    MouseButton.Left => Mouse.LeftButton,
    MouseButton.Right => Mouse.RightButton,
    MouseButton.Middle => Mouse.MiddleButton,
    MouseButton.XButton1 => Mouse.XButton1,
    MouseButton.XButton2 => Mouse.XButton2,
    _ => throw new ArgumentOutOfRangeException(nameof(button))
};

sealed class InputRoot : UIElement
{
    public UIElement First { get; } = new InputLeaf();
    public UIElement Second { get; } = new InputLeaf();
    public InputRoot() { AddVisualChild(First); AddVisualChild(Second); }
    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index == 0 ? First : index == 1 ? Second : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureCore(Size availableSize) { First.Measure(new Size(50, 40)); Second.Measure(new Size(50, 40)); return new Size(100, 40); }
    protected override void ArrangeCore(Rect finalRect) { First.Arrange(new Rect(0, 0, 50, 40)); Second.Arrange(new Rect(50, 0, 50, 40)); }
}
sealed class LanguageSource : IInputLanguageSource
{
    public int Initializations { get; private set; }
    public int Uninitializations { get; private set; }
    public void Initialize() => Initializations++;
    public void Uninitialize() => Uninitializations++;
    public System.Globalization.CultureInfo CurrentInputLanguage { get; set; } = new("en-US");
    public System.Collections.IEnumerable InputLanguageList => new[] { new System.Globalization.CultureInfo("en-US"), new System.Globalization.CultureInfo("de-DE") };
}
sealed class InputLeaf : UIElement
{
    public InputLeaf() => Focusable = true;
    protected override Size MeasureCore(Size availableSize) => availableSize;
    protected override void OnRender(DrawingContext context) => context.DrawRectangle(Brushes.Red, null, new Rect(RenderSize));
}
sealed class ContractHost : IPortablePresentationHost, IPortableInputHost
{
    public PortablePresentationSource? Source { get; set; }
    public Size ClientSize => new(100, 40);
    public DpiScale DpiScale => new(2, 1.5);
    public bool AllowFocus { get; set; } = true;
    public bool AllowCapture { get; set; } = true;
    public bool Focused { get; private set; }
    public int FocusRequests { get; private set; }
    public event Action? MetricsChanged { add { } remove { } }
    public event Action? Exposed { add { } remove { } }
    public event Action? Closed { add { } remove { } }
    public event Action<InputReport>? RawInput;
    public void Report(InputReport report) { if (report is RawMouseInputReport mouse && (mouse.Actions & RawMouseActions.CancelCapture) != 0) HasNativeCapture = false; RawInput?.Invoke(report); }
    public Action? AfterAcquireFocus { get; set; }
    public bool AcquireFocus(bool checkOnly) { if (checkOnly) return Focused; FocusRequests++; Focused = AllowFocus; AfterAcquireFocus?.Invoke(); return Focused; }
    public bool HasNativeCapture { get; private set; }
    public bool CaptureMouse() { if (!AllowCapture) return false; HasNativeCapture = true; return true; }
    public bool ThrowOnRelease { get; set; }
    public Action? BeforeRelease { get; set; }
    public bool NotifyAfterNativeRelease { get; set; }
    public int ReleaseCalls { get; private set; }
    public void ReleaseMouseCapture()
    {
        ReleaseCalls++;
        if (NotifyAfterNativeRelease) HasNativeCapture = false;
        BeforeRelease?.Invoke();
        if (ThrowOnRelease) throw new InvalidOperationException("Native capture release failed.");
        if (!NotifyAfterNativeRelease) HasNativeCapture = false;
    }
    public bool SetCursor(Cursor cursor) => true;
    public Dictionary<Key, KeyStates> KeySnapshot { get; } = new();
    public KeyStates GetKeyStates(Key key) => KeySnapshot.GetValueOrDefault(key);
    public PortablePointerSnapshot PointerSnapshot { get; set; } = new(new Point(20, 12), MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released);
    public bool ThrowOnPointerQuery { get; set; }
    public PortablePointerSnapshot GetPointerState() => ThrowOnPointerQuery ? throw new InvalidOperationException("Native pointer query failed.") : PointerSnapshot;
    public Point ClientToScreen(Point point) => new(point.X + 300, point.Y + 200);
    public Point ScreenToClient(Point point) => new(point.X - 300, point.Y - 200);
    public bool OverrideHitSource { get; set; }
    public PresentationSource? HitSource { get; set; }
    public PresentationSource? SourceFromScreenPoint(Point point) => OverrideHitSource ? HitSource : point.X >= 300 && point.Y >= 200 && point.X < 500 && point.Y < 260 ? Source : null;
    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride) { }
}
sealed class RenderingHost : IPortablePresentationHost
{
    public Size ClientSize => new(100, 40);
    public DpiScale DpiScale => new(1, 1);
    public event Action? MetricsChanged { add { } remove { } }
    public event Action? Exposed { add { } remove { } }
    public event Action? Closed { add { } remove { } }
    public event Action<InputReport>? RawInput { add { } remove { } }
    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride) { }
}
