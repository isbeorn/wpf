extern alias WpfFork;

using System.Reflection;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Threading;

internal static class MessageBoxNativeCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, int requestedDpi, Action<object> observe)
    {
        void VerifyDpi(Window window, string role)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            observe(new { kind = "native-MessageBox-dpi", role, requestedDpi,
                observedDpiX = 96 * dpi.DpiScaleX, observedDpiY = 96 * dpi.DpiScaleY });
            Require(Math.Abs(dpi.DpiScaleX - requestedDpi / 96d) < 0.0001 &&
                Math.Abs(dpi.DpiScaleY - requestedDpi / 96d) < 0.0001,
                $"The genuine MessageBox {role} DPI does not match requested {requestedDpi}: {96 * dpi.DpiScaleX},{96 * dpi.DpiScaleY}.");
        }
        run("native-MessageBox-owner-only-default-keyboard", () =>
        {
            var owner = new Window { Title = "MessageBox owner", Width = 200, Height = 120, Content = new TextBox { Text = "focus" } };
            var sibling = new Window { Title = "MessageBox sibling", Width = 180, Height = 100 };
            try
            {
                sibling.Show(); owner.Show(); drain(); owner.Activate();
                VerifyDpi(owner, "owner");
                var text = (TextBox)owner.Content; text.Focus();
                native.MovePointer(native.FindWindow(owner.Title), 20, 30); drain();
                Require(Mouse.Capture(text), "The real owner could not acquire native mouse capture.");
                Require(Show(native, "MessageBox native default", dialog =>
                {
                    VerifyDpi(dialog, "dialog");
                    Require(!Enabled(owner) && Enabled(sibling) && owner.IsEnabled && !WpfFork::System.Windows.Interop.ComponentDispatcher.IsThreadModal,
                        "MessageBox did not preserve owner-only native disable/no-PushModal.");
                    Require(Mouse.Captured == null && native.CanAcquirePointerGrab(), "MessageBox retained the owner's managed/native capture.");
                    native.Focus(native.FindWindow(dialog.Title)); Key(native, 0xff0d);
                }, caption => MessageBox.Show(owner, "real native message", caption, MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No)) == MessageBoxResult.No,
                    "Actual Enter did not choose the No default.");
                Require(Enabled(owner) && Enabled(sibling) && Keyboard.FocusedElement == text, "Owner enablement/focus did not recover.");
                owner.IsEnabled = false;
                Require(Show(native, "MessageBox native disabled owner", dialog => Click(native, dialog, "OK"), caption => MessageBox.Show(owner, "disabled owner", caption)) == MessageBoxResult.OK
                    && !owner.IsEnabled && !Enabled(owner) && Enabled(sibling), "MessageBox enabled an initially disabled owner.");
            }
            finally { Mouse.Capture(null); sibling.Close(); owner.Close(); drain(); }
        });
        run("native-MessageBox-Escape-and-YesNo-close", () =>
        {
            Require(Show(native, "MessageBox native cancel", dialog => { native.Focus(native.FindWindow(dialog.Title)); Key(native, 0xff1b); },
                caption => MessageBox.Show("message", caption, MessageBoxButton.YesNoCancel)) == MessageBoxResult.Cancel, "Actual Escape did not cancel.");
            Require(Show(native, "MessageBox native no-close", dialog =>
            {
                native.RequestClose(native.FindWindow(dialog.Title));
                drain(); Require(dialog.IsVisible, "YesNo accepted native WM_DELETE_WINDOW.");
                Click(native, dialog, "No");
            }, caption => MessageBox.Show("message", caption, MessageBoxButton.YesNo)) == MessageBoxResult.No, "YesNo did not recover from rejected native close.");
        });
        run("native-MessageBox-Tab-forward-reverse-and-Space", () =>
        {
            foreach (bool reverse in new[] { false, true })
            {
                MessageBoxResult target = reverse ? MessageBoxResult.OK : MessageBoxResult.Cancel;
                Require(Show(native, "MessageBox native tab " + reverse, dialog =>
                {
                    native.Focus(native.FindWindow(dialog.Title));
                    if (reverse) native.Key((char)0xffe1, true);
                    try { Key(native, 0xff09); }
                    finally { if (reverse) native.Key((char)0xffe1, false); }
                    wait(() => Keyboard.FocusedElement is Button button && button.Content.ToString()!.Replace("_", "") == target.ToString());
                    Key(native, 0x20);
                }, caption => MessageBox.Show("message", caption, MessageBoxButton.OKCancel, MessageBoxImage.None,
                    reverse ? MessageBoxResult.Cancel : MessageBoxResult.OK)) == target, "Native Tab/Space chose the wrong actual button.");
            }
        });
        run("native-MessageBox-all-button-results", () =>
        {
            (MessageBoxButton Set, MessageBoxResult[] Results)[] cases =
            [
                (MessageBoxButton.OK, [MessageBoxResult.OK]), (MessageBoxButton.OKCancel, [MessageBoxResult.OK, MessageBoxResult.Cancel]),
                (MessageBoxButton.AbortRetryIgnore, [MessageBoxResult.Abort, MessageBoxResult.Retry, MessageBoxResult.Ignore]),
                (MessageBoxButton.YesNoCancel, [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel]),
                (MessageBoxButton.YesNo, [MessageBoxResult.Yes, MessageBoxResult.No]), (MessageBoxButton.RetryCancel, [MessageBoxResult.Retry, MessageBoxResult.Cancel]),
                (MessageBoxButton.CancelTryContinue, [MessageBoxResult.Cancel, MessageBoxResult.TryAgain, MessageBoxResult.Continue])
            ];
            foreach (var item in cases)
            foreach (var result in item.Results)
                Require(Show(native, "MessageBox native result " + result, dialog => Click(native, dialog, result == MessageBoxResult.TryAgain ? "Try Again" : result.ToString()),
                    caption => MessageBox.Show("Unicode \u03a9 \U0001f600", caption, item.Set)) == result, "Native button returned an incorrect result: " + result);
        });
        run("native-MessageBox-blocked-UI-repeat-and-concurrent-offhost", () =>
        {
            var owner = new Window { Title = "MessageBox blocked owner", Width = 180, Height = 100 };
            var sibling = new Window { Title = "MessageBox blocked sibling", Width = 180, Height = 100 };
            try
            {
                owner.Show(); sibling.Show(); drain();
                object ownerPeer = Peer(owner), siblingPeer = Peer(sibling);
                Action observe = () => Require(PeerEnabled(ownerPeer) && PeerEnabled(siblingPeer), "Offhost MessageBox disabled a window on the blocked original UI thread.");
                for (int iteration = 0; iteration < 3; iteration++) Offhost(native, drain, 1, iteration, observe);
                Offhost(native, drain, 2, 3, observe);
            }
            finally { sibling.Close(); owner.Close(); drain(); }
        });
        run("native-MessageBox-seven-set-default-Escape-and-native-close", () =>
        {
            (MessageBoxButton Set, MessageBoxResult Last, MessageBoxResult? Cancel)[] cases =
            [
                (MessageBoxButton.OK, MessageBoxResult.OK, MessageBoxResult.OK),
                (MessageBoxButton.OKCancel, MessageBoxResult.Cancel, MessageBoxResult.Cancel),
                (MessageBoxButton.AbortRetryIgnore, MessageBoxResult.Ignore, null),
                (MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel, MessageBoxResult.Cancel),
                (MessageBoxButton.YesNo, MessageBoxResult.No, null),
                (MessageBoxButton.RetryCancel, MessageBoxResult.Cancel, MessageBoxResult.Cancel),
                (MessageBoxButton.CancelTryContinue, MessageBoxResult.Continue, MessageBoxResult.Cancel)
            ];
            foreach (var item in cases)
            {
                string label = item.Last.ToString();
                Require(Show(native, "MessageBox default " + item.Set, dialog => { native.Focus(native.FindWindow(dialog.Title)); Key(native, 0xff0d); },
                    caption => MessageBox.Show("message", caption, item.Set, MessageBoxImage.None, item.Last)) == item.Last, "Native Enter used the wrong declared default for " + item.Set);
                foreach (bool close in new[] { false, true })
                    Require(Show(native, "MessageBox close " + item.Set + close, dialog =>
                    {
                        IntPtr handle = native.FindWindow(dialog.Title);
                        if (close) native.RequestClose(handle);
                        else { native.Focus(handle); Key(native, 0xff1b); }
                        if (item.Cancel == null) { drain(); Require(dialog.IsVisible, "A set without Cancel accepted Escape/native close."); Click(native, dialog, label); }
                    }, caption => MessageBox.Show("message", caption, item.Set)) == (item.Cancel ?? item.Last), "Native cancellation returned the wrong result for " + item.Set);
            }
            Require(Show(native, "MessageBox absent valid default", dialog => { native.Focus(native.FindWindow(dialog.Title)); Key(native, 0xff0d); },
                caption => MessageBox.Show("message", caption, MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.Cancel)) == MessageBoxResult.Yes,
                "An absent valid default did not fall back to the first button for actual Enter.");
        });
        run("native-MessageBox-offhost-original-exception-and-session-recovery", () =>
        {
            Type host = typeof(Wpf.Portable.NativeHost.NativeWindowHost).Assembly.GetType("Wpf.Portable.NativeHost.NativeMessageBoxHost", true)!;
            object executor = host.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var expected = new InvalidOperationException("MessageBox offhost marker");
            object? failedSession = null;
            try
            {
                host.GetMethod("Execute")!.Invoke(executor, [new Func<MessageBoxResult>(() =>
                {
                    failedSession = typeof(Wpf.Portable.NativeHost.NativeDispatcherHost).GetProperty("Session", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    new Window { Title = "MessageBox exceptional native session", Width = 120, Height = 80 }.Show();
                    throw expected;
                })]);
                throw new InvalidOperationException("Offhost executor swallowed the original exception.");
            }
            catch (TargetInvocationException exception) { Require(ReferenceEquals(exception.InnerException, expected), "Offhost executor replaced the original exception object."); }
            ClosedSession(failedSession!);
            object? successfulSession = null;
            var successful = (MessageBoxResult)host.GetMethod("Execute")!.Invoke(executor, [new Func<MessageBoxResult>(() =>
            {
                successfulSession = typeof(Wpf.Portable.NativeHost.NativeDispatcherHost).GetProperty("Session", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                new Window { Title = "MessageBox successful native session", Width = 120, Height = 80 }.Show();
                return MessageBoxResult.Yes;
            })])!;
            Require(successful == MessageBoxResult.Yes, "Offhost executor changed its synchronous result.");
            ClosedSession(successfulSession!);
            Offhost(native, drain, 1, 4);
        });
        run("native-MessageBox-access-key-active-deactivated-and-closed-scopes", () =>
        {
            int firstClicks = 0, secondClicks = 0;
            var firstButton = new Button { Content = "first", IsDefault = true };
            var secondButton = new Button { Content = "second", IsDefault = true };
            firstButton.Click += (_, _) => firstClicks++;
            secondButton.Click += (_, _) => secondClicks++;
            var first = new Window { Title = "MessageBox scope first", Left = 20, Top = 20, Width = 150, Height = 90, Content = firstButton };
            var second = new Window { Title = "MessageBox scope second", Left = 200, Top = 20, Width = 150, Height = 90, Content = secondButton };
            var other = new Window { Title = "MessageBox scope empty", Left = 400, Top = 20, Width = 150, Height = 90 };
            try
            {
                first.Show(); second.Show(); other.Show(); drain();
                native.Focus(native.FindWindow(first.Title)); wait(() => first.IsActive); Key(native, 0xff0d); wait(() => firstClicks == 1);
                Require(secondClicks == 0, "Native Enter invoked the inactive default scope.");
                native.Focus(native.FindWindow(second.Title)); wait(() => second.IsActive); Key(native, 0xff0d); wait(() => secondClicks == 1);
                Require(firstClicks == 1, "Switching native focus invoked the previous default scope.");
                native.Focus(native.FindWindow(other.Title)); wait(() => other.IsActive); Key(native, 0xff0d); drain();
                Require(firstClicks == 1 && secondClicks == 1, "Deactivated scope retained an active default.");
            }
            finally { other.Close(); second.Close(); first.Close(); drain(); }
            Require(!AccessKeyManager.ProcessKey(null, "\r", false) && firstClicks == 1 && secondClicks == 1, "Closed/no-active source invoked a stale default.");
        });
    }

    private static void ClosedSession(object session)
    {
        object connection = session.GetType().GetProperty("Connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        Require((bool)session.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!
            && (bool)connection.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!,
            "Offhost completion did not dispose its actual native session and connection.");
    }

    private static void Offhost(X11Fixture native, Action drain, int count, int iteration, Action? observeBlockedOwner = null)
    {
        bool dispatched = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => dispatched = true));
        string[] captions = Enumerable.Range(0, count).Select(index => $"MessageBox blocked UI {iteration}-{index}").ToArray();
        Exception? controllerFailure = null;
        var controller = new Thread(() =>
        {
            try
            {
                var handles = new List<IntPtr>();
                foreach (string caption in captions)
                {
                    IntPtr window = IntPtr.Zero;
                    var limit = System.Diagnostics.Stopwatch.StartNew();
                    while (limit.Elapsed < TimeSpan.FromSeconds(8))
                    {
                        window = native.FindWindow(caption);
                        if (window != IntPtr.Zero && native.IsMapped(window)) break;
                        Thread.Sleep(10);
                    }
                    Require(window != IntPtr.Zero && native.IsMapped(window), "The external native controller never observed the offhost MessageBox.");
                    Require(!dispatched, "The blocked original UI dispatcher ran its Background callback.");
                    Require(native.TransientOwner(window) == IntPtr.Zero, "Offhost MessageBox adopted an owner from another UI thread.");
                    observeBlockedOwner?.Invoke();
                    handles.Add(window);
                }
                foreach (IntPtr window in handles) { native.Focus(window); Key(native, 0xff0d); }
            }
            catch (Exception exception)
            {
                controllerFailure = exception;
                foreach (string caption in captions)
                {
                    IntPtr window = native.FindWindow(caption);
                    if (window != IntPtr.Zero) native.RequestClose(window);
                }
            }
        }) { IsBackground = true, Name = "MessageBox native controller" };
        controller.Start();
        var calls = captions.Select(caption => Task.Factory.StartNew(() => MessageBox.Show("worker \u03a9 \U0001f600", caption, MessageBoxButton.OK),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        MessageBoxResult[] results = [];
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? callFailure = null;
        try { results = Task.WhenAll(calls).Result; }
        catch (Exception exception) { callFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
        finally { Require(controller.Join(TimeSpan.FromSeconds(10)), "The external native controller did not finish."); }
        callFailure?.Throw();
        if (controllerFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(controllerFailure).Throw();
        Require(results.All(result => result == MessageBoxResult.OK) && !dispatched, "Offhost calls returned incorrect results or pumped the blocked UI.");
        drain(); Require(dispatched, "The original UI dispatcher was unusable after offhost completion.");
        foreach (string caption in captions) Require(native.FindWindow(caption) == IntPtr.Zero, "Offhost MessageBox native window survived synchronous completion.");
    }

    private static MessageBoxResult Show(X11Fixture native, string caption, Action<Window> action, Func<string, MessageBoxResult> show)
    {
        bool acted = false;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var dialog = PresentationSource.CurrentSources.Cast<PresentationSource>().Where(source => source.Dispatcher == Dispatcher.CurrentDispatcher)
                .Select(source => source.RootVisual).OfType<Window>().SingleOrDefault(window => window.Title == caption);
            if (dialog is null || !native.IsMapped(native.FindWindow(caption)) || !Buttons(dialog).Any(button => button.ActualWidth > 0)) return;
            timer.Stop(); acted = true; action(dialog);
        };
        timer.Start();
        try { var result = show(caption); Require(acted, "The native controller never observed the actual managed MessageBox."); return result; }
        finally { timer.Stop(); }
    }
    private static void Click(X11Fixture native, Window dialog, string label)
    {
        Button button = Buttons(dialog).Single(value => value.Content.ToString()!.Replace("_", "") == label);
        Point location = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), dialog);
        var dpi = VisualTreeHelper.GetDpi(dialog);
        native.Click(native.FindWindow(dialog.Title), checked((int)(location.X * dpi.DpiScaleX)), checked((int)(location.Y * dpi.DpiScaleY)));
    }
    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Buttons(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static bool Enabled(Window window)
        => PeerEnabled(Peer(window));
    private static object Peer(Window window)
    {
        var source = PresentationSource.FromVisual(window)!;
        return source.GetType().GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
    }
    private static bool PeerEnabled(object peer) => (bool)peer.GetType().GetProperty("IsEnabled")!.GetValue(peer)!;
    private static void Key(X11Fixture native, int symbol) { native.Key((char)symbol, true); native.Key((char)symbol, false); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
