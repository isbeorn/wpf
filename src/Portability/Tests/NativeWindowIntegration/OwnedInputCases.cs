extern alias WpfFork;

using System.Text;
using System.Reflection;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Controls.Primitives;
using WpfFork::System.Windows.Data;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Threading;

internal static class OwnedInputCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, FontFamily font)
    {
        run("native-XKB-remap-shift-and-key-pairs", () => Mapping(native, wait, drain, font));
        run("native-XKB-keypad-NumLock-both-directions", () => Keypad(native, wait, drain, font));
        run("native-XKB-compose-whole-Unicode-commits", () => Compose(native, wait, drain, font));
        run("native-horizontal-wheel-preserves-focus-and-capture", () => HorizontalWheel(native, wait, drain, font));
        foreach (string transition in new[] { "prefix-disable-enable", "disabled-prefix-enable", "key-hide-show" })
            run("native-compose-lifecycle-" + transition, () => CompositionLifecycle(native, wait, drain, font, transition));
        foreach (string transition in new[] { "hide-show", "disable-enable", "disabled-text", "public-close" })
            run("native-popup-peer-key-lifecycle-" + transition, () => PopupKeyboardLifecycle(native, wait, drain, font, transition));
        run("native-input-two-UI-thread-window-lifetimes", () => IndependentThreads(native, wait, font));
    }

    private static void IndependentThreads(X11Fixture native, Action<Func<bool>> wait, FontFamily font)
    {
        var ready = new[] { new TaskCompletionSource<Dispatcher>(), new TaskCompletionSource<Dispatcher>() };
        var dispatchers = new Dispatcher?[2];
        var fixtures = new Fixture?[2];
        var owners = new int[2];
        var clicks = new int[2];
        var keys = new int[2];
        var callbacksOnOwner = new[] { true, true };
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        string fontSource = font.Source;
        using var overlap = new Barrier(2);
        var threads = Enumerable.Range(0, 2).Select(index => new Thread(() =>
        {
            try
            {
                Wpf.Portable.NativeHost.NativeWindowHost.InitializeForCurrentThread();
                owners[index] = Environment.CurrentManagedThreadId;
                var dispatcher = Dispatcher.CurrentDispatcher;
                ready[index].SetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception error) { failures.Enqueue(error); ready[index].TrySetException(error); }
        }) { IsBackground = true, Name = "Owned X11 input UI " + index }).ToArray();
        try
        {
            foreach (var thread in threads) thread.Start();
            wait(() => ready.All(item => item.Task.IsCompleted));
            for (int index = 0; index < 2; index++) dispatchers[index] = ready[index].Task.GetAwaiter().GetResult();
            for (int round = 0; round < 4; round++)
            {
                var opening = Enumerable.Range(0, 2).Select(index => dispatchers[index]!.InvokeAsync(() =>
                {
                    Require(overlap.SignalAndWait(2000), "The independent UI threads did not overlap their first Window creation.");
                    var fixture = Create($"Owned input thread {index} round {round}", new FontFamily(fontSource));
                    fixtures[index] = fixture;
                    fixture.Window.Left = 20 + index * 420; fixture.Window.Top = 200;
                    fixture.Box.PreviewMouseDown += (_, _) => { callbacksOnOwner[index] &= Environment.CurrentManagedThreadId == owners[index]; clicks[index]++; };
                    fixture.Box.PreviewKeyDown += (_, _) => { callbacksOnOwner[index] &= Environment.CurrentManagedThreadId == owners[index]; keys[index]++; };
                    fixture.Window.Show();
                }).Task).ToArray();
                wait(() => opening.All(task => task.IsCompleted));
                foreach (var task in opening) task.GetAwaiter().GetResult();
                for (int index = 0; index < 2; index++)
                {
                    int target = index;
                    var xid = native.FindWindow($"Owned input thread {index} round {round}");
                    wait(() => native.IsMapped(xid));
                    native.Focus(xid); native.Click(xid, 180, 60);
                    wait(() => dispatchers[target]!.Invoke(() => fixtures[target]!.Box.IsKeyboardFocused));
                    native.Type('a');
                    wait(() => dispatchers[target]!.Invoke(() => fixtures[target]!.Box.Text == "a" && fixtures[target]!.Model.Value == "a"));
                    Require(dispatchers[target]!.Invoke(() => clicks[target] == round + 1 && keys[target] == round + 1 && callbacksOnOwner[target]),
                        "Native input crossed UI-thread routes or duplicated callbacks after window recreation.");
                }
                Require(dispatchers[1]!.Invoke(() => fixtures[1]!.Window.CaptureMouse()) && !native.CanAcquirePointerGrab(),
                    "The independent connection did not own a real native grab before its last Window closed.");
                var closing = Enumerable.Range(0, 2).Select(index => dispatchers[index]!.InvokeAsync(() =>
                {
                    Require(overlap.SignalAndWait(2000), "The independent UI threads did not overlap their last Window closure.");
                    var fixture = fixtures[index]!;
                    fixture.Window.Close();
                    Require(PresentationSource.FromVisual(fixture.Window) == null, "A last Window retained its WPF source on an independent connection.");
                    fixtures[index] = null;
                }).Task).ToArray();
                wait(() => closing.All(task => task.IsCompleted));
                foreach (var task in closing) task.GetAwaiter().GetResult();
                Require(native.CanAcquirePointerGrab() && Enumerable.Range(0, 2).All(index => native.FindWindow($"Owned input thread {index} round {round}") == IntPtr.Zero),
                    "Last Window cleanup retained a native window or pointer grab on an independent connection.");
            }
            Require(owners[0] != owners[1] && failures.IsEmpty, "The two native UI connections did not retain independent live owner threads.");
        }
        finally
        {
            for (int index = 0; index < 2; index++)
            {
                int target = index;
                var dispatcher = dispatchers[index] ?? (ready[index].Task.IsCompletedSuccessfully ? ready[index].Task.Result : null);
                if (dispatcher is { HasShutdownStarted: false })
                    _ = dispatcher.InvokeAsync(() => { fixtures[target]?.Window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); });
            }
            wait(() => threads.All(thread => !thread.IsAlive));
            foreach (var thread in threads) Require(thread.Join(100), "An independent native UI thread survived its cleanup.");
        }
        if (failures.TryPeek(out var error)) throw new InvalidOperationException("An independent native UI thread failed.", error);
    }

    private static void PopupKeyboardLifecycle(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font, string transition)
    {
        var owner = Create("Owned popup keyboard " + transition, font);
        var box = new TextBox { OverridesDefaultStyle = true, Width = 220, Height = 60, FontFamily = font,
            Template = new ControlTemplate(typeof(TextBox)) { VisualTree = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost") } };
        var popup = new Popup { PlacementTarget = owner.Box, Placement = PlacementMode.Bottom, StaysOpen = true, Child = box };
        var texts = new List<string>();
        box.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, args) => texts.Add(args.Text)), true);
        try
        {
            Show(owner, native, wait);
            var ownerXid = native.FindWindow(owner.Window.Title);
            popup.IsOpen = true;
            wait(() => PresentationSource.FromVisual(box) != null && box.ActualHeight == 60);
            box.Focus(); wait(() => box.IsKeyboardFocused);
            Require(native.FocusedWindow == ownerXid, "Popup keyboard routing unexpectedly moved native focus away from its owner.");
            object peer = PresentationSource.FromVisual(box)!.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(PresentationSource.FromVisual(box))!;
            var hide = peer.GetType().GetMethod("Hide")!.CreateDelegate<Action>(peer);
            var show = peer.GetType().GetMethod("Show", new[] { typeof(bool) })!.CreateDelegate<Action<bool>>(peer);
            var enable = peer.GetType().GetMethod("SetEnabled")!.CreateDelegate<Action<bool>>(peer);
            bool changed = transition == "disabled-text";
            KeyEventHandler handler = (_, args) =>
            {
                if (args.Key != Key.A || changed) return;
                changed = true;
                if (transition == "public-close") { popup.IsOpen = false; owner.Box.Focus(); return; }
                if (transition == "hide-show") { hide(); show(false); }
                else { enable(false); enable(true); }
                box.Focus();
            };
            box.PreviewKeyDown += handler;
            try
            {
                if (transition == "disabled-text") enable(false);
                native.Type('a'); drain();
            }
            finally { box.PreviewKeyDown -= handler; }
            Require(changed && box.Text == "" && owner.Box.Text == "" && texts.Count == 0 && owner.Texts.Count == 0,
                "Owner-delivered native text crossed an ineligible Popup peer transition: phase=" + transition + ", popup=" + box.Text + ", owner=" + owner.Box.Text + ", routed=" + string.Join("|", texts));
            if (transition == "public-close")
            {
                Require(!popup.IsOpen && PresentationSource.FromVisual(box) == null, "Public Popup close retained its genuine source.");
                native.Click(ownerXid, 180, 60); wait(() => owner.Box.IsKeyboardFocused);
                native.Type('b'); wait(() => owner.Box.Text == "b" && owner.Model.Value == "b");
                return;
            }
            enable(true); box.Focus(); drain();
            native.Type('b'); wait(() => box.Text == "b");
            Require(texts.SequenceEqual(new[] { "b" }), "A Popup keyboard transition suppressed a subsequent eligible native commit.");
        }
        finally { popup.IsOpen = false; owner.Window.Close(); }
    }

    private static void HorizontalWheel(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font)
    {
        var fixture = Create("Owned native horizontal wheel", font);
        var vertical = new List<int>();
        var horizontal = new List<int>();
        EventInfo? raw = null;
        object? peer = null;
        Delegate? observer = null;
        fixture.Window.PreviewMouseWheel += (_, args) => vertical.Add(args.Delta);
        try
        {
            Show(fixture, native, wait);
            var source = PresentationSource.FromVisual(fixture.Window)!;
            peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            raw = peer.GetType().GetEvent("RawInput")!;
            Action<object> observe = report =>
            {
                var type = report.GetType();
                if (type.Name != "RawMouseInputReport") return;
                object Read(string name) => type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(report)!;
                if (Read("Actions").ToString()!.Contains("HorizontalWheelRotate", StringComparison.Ordinal)) horizontal.Add((int)Read("Wheel"));
            };
            observer = Delegate.CreateDelegate(raw.EventHandlerType!, observe.Target, observe.Method);
            raw.AddEventHandler(peer, observer);
            object focused = Keyboard.FocusedElement;
            Require(fixture.Window.CaptureMouse(), "Horizontal-wheel fixture could not obtain a real native grab.");
            foreach (uint button in new uint[] { 6, 7 })
            {
                try { native.Button(button, true); }
                finally { native.Button(button, false); }
                drain();
                Require(vertical.Count == 0 && ReferenceEquals(Keyboard.FocusedElement, focused) && ReferenceEquals(Mouse.Captured, fixture.Window) && !native.CanAcquirePointerGrab(),
                    "Horizontal native wheel input became a vertical event or changed keyboard focus/native capture.");
            }
            Require(horizontal.SequenceEqual(new[] { -120, 120 }), "Native horizontal wheel presses lost their raw direction or duplicated a release report: " + string.Join(",", horizontal));
            foreach (uint button in new uint[] { 4, 5 })
            {
                try { native.Button(button, true); }
                finally { native.Button(button, false); }
                drain();
            }
            Require(vertical.SequenceEqual(new[] { 120, -120 }), "The independent vertical-wheel positive control did not deliver both directions.");
            fixture.Window.ReleaseMouseCapture(); drain();
            Require(Mouse.Captured == null && native.CanAcquirePointerGrab(), "Wheel verification retained capture after explicit release.");
        }
        finally
        {
            if (observer != null) raw!.RemoveEventHandler(peer, observer);
            fixture.Window.Close();
        }
    }

    private static void CompositionLifecycle(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font, string transition)
    {
        var fixture = Create("Owned compose lifecycle " + transition, font);
        try
        {
            Show(fixture, native, wait);
            if (transition == "key-hide-show")
            {
                bool changed = false;
                KeyEventHandler handler = (_, args) =>
                {
                    if (args.Key != Key.A || changed) return;
                    changed = true; fixture.Window.Hide(); fixture.Window.Show(); fixture.Box.Focus();
                };
                fixture.Box.PreviewKeyDown += handler;
                try { native.Type('a'); drain(); }
                finally { fixture.Box.PreviewKeyDown -= handler; }
                Require(changed && fixture.Box.Text == "" && fixture.Texts.Count == 0,
                    "A key callback's Hide/Show allowed its stale paired native text commit: actual=" + fixture.Box.Text);
                var xid = native.FindWindow(fixture.Window.Title);
                native.Focus(xid); drain(); native.Click(xid, 180, 60); wait(() => fixture.Box.IsKeyboardFocused);
                native.Type('b'); wait(() => fixture.Box.Text == "b" && fixture.Model.Value == "b");
                return;
            }
            native.Type('x'); wait(() => fixture.Box.Text == "x");
            fixture.Box.SelectAll(); fixture.Texts.Clear();
            using var key = native.RemapSpareKey(0xfe51);
            drain();
            if (transition == "disabled-prefix-enable") fixture.Window.IsEnabled = false;
            key.Tap(); drain();
            if (transition == "prefix-disable-enable") fixture.Window.IsEnabled = false;
            fixture.Window.IsEnabled = true;
            fixture.Window.Activate(); fixture.Box.Focus(); drain();
            fixture.Box.SelectAll(); fixture.Texts.Clear();
            native.Type('e'); wait(() => fixture.Texts.Count != 0);
            Require(fixture.Box.Text == "e" && fixture.Model.Value == "e" && fixture.Texts.SequenceEqual(new[] { "e" }),
                "A compose prefix survived an enable transition: phase=" + transition + ", actual=" + fixture.Box.Text + ", routed=" + string.Join("|", fixture.Texts));
        }
        finally { fixture.Window.Close(); }
    }

    private static void Keypad(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font)
    {
        var fixture = Create("Owned native keypad", font);
        var target = Create("Owned keypad snapshot target", font);
        var other = target.Window;
        other.Left = 600; other.Top = 400;
        bool changedLock = false;
        int homeDown = 0, homeUp = 0, numberDown = 0, numberUp = 0, otherNumberDown = 0;
        string phase = "pairs";
        var trace = new List<string>();
        fixture.Box.PreviewKeyDown += (_, args) => trace.Add($"{phase}:fixture-down/{args.Key}/repeat={args.IsRepeat}/time={args.Timestamp}");
        fixture.Box.PreviewKeyUp += (_, args) => trace.Add($"{phase}:fixture-up/{args.Key}/time={args.Timestamp}");
        other.PreviewKeyDown += (_, args) =>
        {
            trace.Add($"{phase}:other-down/{args.Key}/repeat={args.IsRepeat}/time={args.Timestamp}");
            if (args.Key == Key.NumPad7 && !args.IsRepeat) otherNumberDown++;
        };
        fixture.Box.PreviewKeyDown += (_, args) => { if (args.IsRepeat) return; if (args.Key == Key.Home) homeDown++; if (args.Key == Key.NumPad7) numberDown++; };
        fixture.Box.PreviewKeyUp += (_, args) => { if (args.Key == Key.Home) homeUp++; if (args.Key == Key.NumPad7) numberUp++; };
        try
        {
            Show(fixture, native, wait);
            native.Type('a'); native.Type('b'); wait(() => fixture.Box.Text == "ab");
            SetLock(false);
            native.Type('\uffb7'); wait(() => homeDown == 1 && homeUp == 1 && Keyboard.IsKeyUp(Key.Home));
            Require(fixture.Box.Text == "ab" && fixture.Box.CaretIndex == 0, "NumLock-off keypad Home did not navigate without inserting text.");
            fixture.Box.CaretIndex = fixture.Box.Text.Length;
            SetLock(true);
            native.Type('\uffb7'); drain();
            Require(fixture.Box.Text == "ab7" && fixture.Model.Value == "ab7" && numberDown == 1 && numberUp == 1 && Keyboard.IsKeyUp(Key.NumPad7),
                $"NumLock-on keypad7 changed the key identity or navigated before inserting text: actual={fixture.Box.Text}, numberDown={numberDown}, numberUp={numberUp}, homeDown={homeDown}, homeUp={homeUp}.");
            SetLock(false);
            native.Type('\uffb7'); wait(() => homeDown == 2 && homeUp == 2);
            Require(fixture.Box.Text == "ab7" && fixture.Box.CaretIndex == 0, "Returning NumLock to off retained the numeric keypad behavior.");
            SetLock(true); fixture.Box.CaretIndex = fixture.Box.Text.Length;
            native.Key('\uffb7', true); wait(() => Keyboard.IsKeyDown(Key.NumPad7));
            SetLock(false);
            native.Key('\uffb7', false); wait(() => Keyboard.IsKeyUp(Key.NumPad7) && numberDown == 2 && numberUp == 2);
            Require(homeDown == 2 && homeUp == 2, "NumLock changing while keypad7 was held changed its paired release identity.");
            native.Key('\uffb7', true); wait(() => Keyboard.IsKeyDown(Key.Home));
            SetLock(true);
            native.Key('\uffb7', false); wait(() => Keyboard.IsKeyUp(Key.Home) && homeDown == 3 && homeUp == 3);
            Require(numberDown == 2 && numberUp == 2, "The reverse NumLock change manufactured a numeric keypad release.");

            other.Show(); var otherXid = native.FindWindow(other.Title); wait(() => native.IsMapped(otherXid));
            phase = "other-focus"; native.Focus(otherXid); drain(); native.Click(otherXid, 180, 60);
            wait(() => target.Box.IsKeyboardFocused && !fixture.Box.IsKeyboardFocused);
            trace.Add("other-focus:fixtureFocused=" + fixture.Box.IsKeyboardFocused + "/native=" + native.FocusedWindow);
            phase = "other-press"; native.Key('\uffb7', true); wait(() => otherNumberDown == 1); drain();
            Require(numberDown == 2, "The held keypad press reached the previous editor before activation. " + string.Join(" | ", trace));
            trace.Add("other-press:count=" + numberDown);
            var xid = native.FindWindow(fixture.Window.Title);
            phase = "return-focus"; native.Focus(xid); drain(); phase = "return-click"; native.Click(xid, 180, 60); wait(() => fixture.Box.IsKeyboardFocused && Keyboard.IsKeyDown(Key.NumPad7));
            Require(numberDown == 2, "A held keypad activation snapshot manufactured a key-down transition. " + string.Join(" | ", trace));
            native.Key('\uffb7', false); wait(() => Keyboard.IsKeyUp(Key.NumPad7) && numberUp == 3);
            Require(numberDown == 2, "A snapshot-only keypad press manufactured a paired down event on release.");

            void SetLock(bool enabled)
            {
                if (Keyboard.IsKeyToggled(Key.NumLock) == enabled) return;
                native.Type('\uff7f'); changedLock = !changedLock;
                wait(() => Keyboard.IsKeyToggled(Key.NumLock) == enabled && Keyboard.IsKeyUp(Key.NumLock));
            }
        }
        finally
        {
            native.Key('\uffb7', false);
            if (changedLock) native.Type('\uff7f');
            other.Close();
            fixture.Window.Close();
        }
    }

    private static void Mapping(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font)
    {
        var fixture = Create("Owned XKB mapping", font);
        var other = new Window { Title = "Owned native lock snapshot target", Width = 120, Height = 80, ShowActivated = false };
        int qDown = 0, qUp = 0, aDown = 0, aUp = 0;
        int capsTransitions = 0;
        bool changedCaps = false;
        fixture.Box.PreviewKeyDown += (_, args) => { if (args.Key == Key.Q) qDown++; if (args.Key == Key.A) aDown++; };
        fixture.Box.PreviewKeyUp += (_, args) => { if (args.Key == Key.Q) qUp++; if (args.Key == Key.A) aUp++; };
        fixture.Box.PreviewKeyDown += (_, args) => { if (args.Key == Key.CapsLock) capsTransitions++; };
        try
        {
            Show(fixture, native, wait);
            using var key = native.RemapSpareKey('q', 'Q');
            drain();
            key.Tap();
            wait(() => fixture.Box.Text == "q" && qDown == 1 && qUp == 1 && Keyboard.IsKeyUp(Key.Q));
            key.Remap('a', 'A');
            drain();
            native.Key('\uffe1', true);
            wait(() => Keyboard.IsKeyDown(Key.LeftShift));
            key.Tap();
            wait(() => fixture.Box.Text == "qA" && aDown == 1 && aUp == 1 && Keyboard.IsKeyUp(Key.A));
            native.Key('\uffe1', false);
            wait(() => Keyboard.IsKeyUp(Key.LeftShift));
            Require(fixture.Model.Value == "qA" && fixture.Texts.SequenceEqual(new[] { "q", "A" }),
                "Native XKB remapping or Shift translation lost/duplicated a key pair, text commit or bound value.");
            bool initialCaps = Keyboard.IsKeyToggled(Key.CapsLock);
            other.Show(); var otherXid = native.FindWindow(other.Title); wait(() => native.IsMapped(otherXid));
            var xid = native.FindWindow(fixture.Window.Title);
            for (int direction = 0; direction < 2; direction++)
            {
                native.Focus(otherXid); drain();
                native.Key('\uffe5', true); native.Key('\uffe5', false);
                changedCaps = !changedCaps;
                drain(); native.Focus(xid); fixture.Box.Focus();
                bool expected = direction == 0 ? !initialCaps : initialCaps;
                wait(() => fixture.Box.IsKeyboardFocused && Keyboard.IsKeyToggled(Key.CapsLock) == expected && Keyboard.IsKeyUp(Key.CapsLock));
                Require(capsTransitions == 0, "A native lock-state activation snapshot manufactured a key transition.");
            }
        }
        finally
        {
            native.Key('\uffe1', false);
            if (changedCaps) { native.Key('\uffe5', true); native.Key('\uffe5', false); }
            other.Close(); fixture.Window.Close();
        }
    }

    private static void Compose(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font)
    {
        string directory = Path.Combine(Path.GetTempPath(), "wpf-owned-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Compose");
        string? original = Environment.GetEnvironmentVariable("XCOMPOSEFILE");
        string? originalNative = X11Fixture.NativeEnvironmentVariable("XCOMPOSEFILE");
        File.WriteAllText(path, "<Multi_key> <a> <e> : \"x\u03a9e\u0301\U0001F600\"\n<dead_acute> <e> : \"\u00e9\"\n", new UTF8Encoding(false, true));
        Environment.SetEnvironmentVariable("XCOMPOSEFILE", path);
        X11Fixture.SetNativeEnvironmentVariable("XCOMPOSEFILE", path);
        var fixture = Create("Owned XKB whole commits", font);
        var other = new Window { Title = "Owned compose focus target", Left = 600, Top = 400, Width = 120, Height = 80 };
        try
        {
            Show(fixture, native, wait);
            native.Type('a'); wait(() => fixture.Box.Text == "a");
            fixture.Box.SelectAll();
            fixture.Box.IsUndoEnabled = false; fixture.Box.IsUndoEnabled = true;
            fixture.Texts.Clear();
            using var key = native.RemapSpareKey(0xff20); // Multi_key does not require a physical keyboard binding.
            drain(); key.Tap(); drain();
            native.Type('a'); drain();
            Require(fixture.Box.Text == "a" && fixture.Texts.Count == 0, "An incomplete native compose sequence edited its selected text.");
            native.Type('e');
            const string whole = "x\u03a9e\u0301\U0001F600";
            wait(() => fixture.Texts.Count != 0);
            Require(fixture.Box.Text == whole && fixture.Model.Value == whole && fixture.Texts.SequenceEqual(new[] { whole }),
                "A native multi-scalar compose commit was split, duplicated or changed: actual=" + fixture.Box.Text + ", model=" + fixture.Model.Value +
                ", routed=" + string.Join("|", fixture.Texts) + ", nativeCompose=" + X11Fixture.NativeEnvironmentVariable("XCOMPOSEFILE"));
            fixture.Box.Undo(); drain();
            Require(fixture.Box.Text == "a" && fixture.Model.Value == "a" && !fixture.Box.CanUndo, "One undo did not restore the selected text before the native compose commit.");
            fixture.Box.Redo(); drain();
            Require(fixture.Box.Text == whole && fixture.Model.Value == whole && !fixture.Box.CanRedo, "One redo did not restore the entire native compose commit.");

            ReplaceByNative(0xfe51, () => native.Type('e'), "\u00e9");
            ReplaceByNative(0x0101f600, () => { }, "\U0001F600");
            foreach (uint invalid in new uint[] { 0x0100d800, 0x01110000 })
            {
                key.Remap(invalid); drain(); fixture.Texts.Clear(); key.Tap(); drain();
                Require(fixture.Box.Text == "\U0001F600" && fixture.Model.Value == "\U0001F600" && fixture.Texts.Count == 0,
                    "An invalid Unicode keysym reached the editor or emitted a partial native text commit.");
            }

            key.Remap(0xff20); drain(); key.Tap(); drain();
            other.Show(); var otherXid = native.FindWindow(other.Title); wait(() => native.IsMapped(otherXid));
            native.Focus(otherXid); drain();
            var fixtureXid = native.FindWindow(fixture.Window.Title);
            native.Focus(fixtureXid); drain();
            native.Click(fixtureXid, 180, 60); wait(() => fixture.Box.IsKeyboardFocused);
            fixture.Box.SelectAll(); fixture.Texts.Clear();
            native.Type('a');
            try { wait(() => fixture.Box.Text == "a" && fixture.Model.Value == "a"); }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Native focus-return compose state: actual=" + fixture.Box.Text + ", routed=" + string.Join("|", fixture.Texts) +
                    ", keyboardFocused=" + fixture.Box.IsKeyboardFocused + ", nativeFocus=" + native.FocusedWindow, exception);
            }
            Require(fixture.Texts.SequenceEqual(new[] { "a" }), "Native focus loss retained an incomplete compose sequence.");

            key.Tap(); drain();
            native.Key('\uff1b', true); native.Key('\uff1b', false); drain();
            fixture.Box.SelectAll(); fixture.Texts.Clear();
            native.Type('e'); wait(() => fixture.Box.Text == "e" && fixture.Model.Value == "e");
            Require(fixture.Texts.SequenceEqual(new[] { "e" }), "Escape did not cancel native compose before ordinary text resumed.");

            void ReplaceByNative(uint symbol, Action suffix, string expected)
            {
                fixture.Box.SelectAll(); fixture.Texts.Clear();
                key.Remap(symbol); drain(); key.Tap(); drain(); suffix();
                wait(() => fixture.Box.Text == expected && fixture.Model.Value == expected);
                Require(fixture.Texts.SequenceEqual(new[] { expected }), "Native dead-key or supplementary text was not one exact whole commit.");
            }
        }
        finally
        {
            other.Close(); fixture.Window.Close();
            X11Fixture.SetNativeEnvironmentVariable("XCOMPOSEFILE", originalNative);
            Environment.SetEnvironmentVariable("XCOMPOSEFILE", original);
            File.Delete(path); Directory.Delete(directory);
        }
    }

    private static Fixture Create(string title, FontFamily font)
    {
        var box = new TextBox { OverridesDefaultStyle = true, FontFamily = font, Width = 300, Height = 60,
            Template = new ControlTemplate(typeof(TextBox)) { VisualTree = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost") } };
        var model = new Model();
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(Model.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var result = new Fixture(new Window { Title = title, Width = 360, Height = 120, Content = box, FontFamily = font }, box, model, new());
        box.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, args) => result.Texts.Add(args.Text)), true);
        return result;
    }

    private static void Show(Fixture fixture, X11Fixture native, Action<Func<bool>> wait)
    {
        fixture.Window.Show(); var xid = native.FindWindow(fixture.Window.Title);
        wait(() => native.IsMapped(xid) && fixture.Box.ActualHeight == 60);
        native.Focus(xid); native.Click(xid, 180, 60); wait(() => fixture.Box.IsKeyboardFocused);
    }

    private sealed class Model { public string Value { get; set; } = ""; }
    private sealed record Fixture(Window Window, TextBox Box, Model Model, List<string> Texts);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
