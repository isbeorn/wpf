using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;

internal static class MessageBoxCases
{
    internal static void Exercise(ManagedFactory factory, Action<string, Action> run)
    {
        run("messagebox-public-validation", () =>
        {
            Throws<InvalidEnumArgumentException>(() => MessageBox.Show("text", "caption", (MessageBoxButton)(-1)));
            Throws<InvalidEnumArgumentException>(() => MessageBox.Show("text", "caption", MessageBoxButton.OK, (MessageBoxImage)1));
            Throws<InvalidEnumArgumentException>(() => MessageBox.Show("text", "caption", MessageBoxButton.OK, MessageBoxImage.None, (MessageBoxResult)8));
            Throws<InvalidEnumArgumentException>(() => MessageBox.Show("text", "caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, (MessageBoxOptions)1));
            Throws<ArgumentNullException>(() => MessageBox.Show((Window)null!, "text"));
            Throws<PlatformNotSupportedException>(() => MessageBox.Show("text", "caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.ServiceNotification));
            Throws<PlatformNotSupportedException>(() => MessageBox.Show("text", "caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.DefaultDesktopOnly));
            Require(Throws<InvalidEnumArgumentException>(() => MessageBox.Show("text", "caption", (MessageBoxButton)(-1), (MessageBoxImage)1,
                (MessageBoxResult)8, (MessageBoxOptions)1)).ParamName == "button", "Enum validation order changed.");
            Throws<PlatformNotSupportedException>(() => MessageBox.ShowCore(new IntPtr(123), "text", "caption", MessageBoxButton.OK,
                MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None));
            var owner = new Window(); owner.Show();
            try { Throws<ArgumentException>(() => MessageBox.Show(owner, "text", "caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.ServiceNotification)); }
            finally { owner.Close(); }
        });
        run("messagebox-six-public-window-overloads", () =>
        {
            var owner = new Window { Title = "MessageBox overload owner" }; owner.Show();
            Func<string, MessageBoxResult>[] calls =
            [
                _ => MessageBox.Show(owner, "text"),
                caption => MessageBox.Show(owner, "text", caption),
                caption => MessageBox.Show(owner, "text", caption, MessageBoxButton.OK),
                caption => MessageBox.Show(owner, "text", caption, MessageBoxButton.OK, MessageBoxImage.Warning),
                caption => MessageBox.Show(owner, "text", caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK),
                caption => MessageBox.Show(owner, "text", caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK, MessageBoxOptions.RightAlign)
            ];
            try
            {
                for (int index = 0; index < calls.Length; index++)
                    Require(Show(index == 0 ? "" : "overload-" + index, dialog => { Require(dialog.Owner == owner, "Window overload lost its managed owner."); Invoke(dialog, "OK"); }, calls[index]) == MessageBoxResult.OK,
                        "Public Window overload did not complete: " + index);
            }
            finally { owner.Close(); }
        });
        run("messagebox-owner-only-modality-and-focus", () =>
        {
            var text = new TextBox { Text = "owner focus" };
            var owner = new Window { Content = text }; owner.Show(); var ownerPeer = factory.Last;
            var sibling = new Window(); sibling.Show(); var siblingPeer = factory.Last;
            owner.Activate(); text.Focus();
            try
            {
                var result = Show("owner-only", dialog =>
                {
                    Require(!ownerPeer.IsEnabled && siblingPeer.IsEnabled && owner.IsEnabled, "MessageBox disabled the sibling or changed managed IsEnabled.");
                    Require(!System.Windows.Interop.ComponentDispatcher.IsThreadModal, "MessageBox pushed ComponentDispatcher modal state.");
                    Require(ReferenceEquals(dialog.Owner, owner) && owner.OwnedWindows.Count == 1, "MessageBox owner was not retained.");
                    Invoke(dialog, "No");
                }, caption => MessageBox.Show(owner, "message", caption, MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No));
                Require(result == MessageBoxResult.No && ownerPeer.IsEnabled && siblingPeer.IsEnabled, "MessageBox did not restore owner enablement.");
                owner.IsEnabled = false;
                Require(Show("disabled-owner", dialog => Invoke(dialog, "OK"), caption => MessageBox.Show(owner, "message", caption)) == MessageBoxResult.OK
                    && !ownerPeer.IsEnabled && !owner.IsEnabled, "MessageBox enabled an initially disabled owner.");
            }
            finally { sibling.Close(); owner.Close(); }
        });
        run("messagebox-button-results-and-default-fallback", () =>
        {
            (MessageBoxButton Set, MessageBoxResult[] Results)[] cases =
            [
                (MessageBoxButton.OK, [MessageBoxResult.OK]),
                (MessageBoxButton.OKCancel, [MessageBoxResult.OK, MessageBoxResult.Cancel]),
                (MessageBoxButton.AbortRetryIgnore, [MessageBoxResult.Abort, MessageBoxResult.Retry, MessageBoxResult.Ignore]),
                (MessageBoxButton.YesNoCancel, [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel]),
                (MessageBoxButton.YesNo, [MessageBoxResult.Yes, MessageBoxResult.No]),
                (MessageBoxButton.RetryCancel, [MessageBoxResult.Retry, MessageBoxResult.Cancel]),
                (MessageBoxButton.CancelTryContinue, [MessageBoxResult.Cancel, MessageBoxResult.TryAgain, MessageBoxResult.Continue])
            ];
            foreach (var item in cases)
            foreach (var result in item.Results)
            {
                string label = result == MessageBoxResult.TryAgain ? "Try Again" : result.ToString();
                Require(Show("result-" + item.Set + "-" + result, dialog =>
                {
                    Require(Buttons(dialog).Single(button => button.IsDefault).Content.ToString()!.Replace("_", "") == label, "Default result selected the wrong button.");
                    Invoke(dialog, label);
                }, caption => MessageBox.Show("message", caption, item.Set, MessageBoxImage.None, result)) == result, "Actual Button invocation returned the wrong MessageBoxResult.");
            }
            Require(Show("absent-default", dialog =>
            {
                Require(Buttons(dialog).Single(button => button.IsDefault).Content.ToString() == "_Yes", "Absent valid default did not select first button.");
                Invoke(dialog, "Yes");
            }, caption => MessageBox.Show("message", caption, MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.Cancel)) == MessageBoxResult.Yes, "Absent default fallback failed.");
        });
        run("messagebox-close-resource-and-application-isolation", () =>
        {
            var app = Application.Current!;
            int windows = app.Windows.Count; Window? main = app.MainWindow;
            var poison = new PoisonConverter();
            var broken = new Style(typeof(Button));
            broken.Setters.Add(new Setter(FrameworkElement.TagProperty, new System.Windows.Data.Binding { Source = "broken application resource", Converter = poison }));
            broken.Setters.Add(new Setter(Control.TemplateProperty, null));
            app.Resources[typeof(Button)] = broken;
            var brokenWindow = new Style(typeof(Window)); brokenWindow.Setters.Add(new Setter(Control.TemplateProperty, null));
            app.Resources[typeof(Window)] = brokenWindow;
            app.Resources[SystemColors.WindowTextBrushKey] = "not a brush";
            var previous = app.ShutdownMode; app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            try
            {
                Require(Show("isolated", dialog =>
                {
                    Require(app.Windows.Count == windows && app.MainWindow == main, "Private MessageBox registered with Application or became MainWindow.");
                    Require(Buttons(dialog).All(button => button.Template != null && button.Tag == null), "MessageBox Button template/Tag: " + string.Join(", ", Buttons(dialog).Select(button => $"template={button.Template != null},Tag={button.Tag ?? "null"}")));
                    Require(dialog.Template != null && Buttons(dialog).All(button => button.FontFamily.Source == dialog.FontFamily.Source && button.FontSize == dialog.FontSize), "Dialog template or message-font inheritance was lost.");
                    Require(poison.Calls == 0, "Private MessageBox evaluated an application binding converter.");
                    Require(dialog.TryFindResource(SystemColors.WindowTextBrushKey) is Brush, "MessageBox inherited the broken application resource.");
                    dialog.Close();
                }, caption => MessageBox.Show("message", caption)) == MessageBoxResult.OK, "OK close returned a non-OK result.");
                Require(!app.Dispatcher.HasShutdownStarted && app.Windows.Count == windows && app.MainWindow == main, "Private MessageBox changed Application lifetime.");
                Require(Show("yes-no-close", dialog => { dialog.Close(); Require(dialog.IsVisible, "YesNo ordinary close was accepted."); Invoke(dialog, "No"); },
                    caption => MessageBox.Show("message", caption, MessageBoxButton.YesNo)) == MessageBoxResult.No, "YesNo selection failed after rejected close.");
            }
            finally { app.ShutdownMode = previous; app.Resources.Remove(typeof(Button)); app.Resources.Remove(typeof(Window)); app.Resources.Remove(SystemColors.WindowTextBrushKey); }
        });
        run("messagebox-nested-owner-close-and-option-layout", () =>
        {
            var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
            try
            {
                Require(Show("outer", outer =>
                {
                    var outerPeer = factory.Last;
                    foreach (MessageBoxOptions options in new[] { MessageBoxOptions.None, MessageBoxOptions.RtlReading, MessageBoxOptions.RightAlign, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign })
                    {
                        Require(Show("inner-" + options, inner =>
                        {
                            Require(!ownerPeer.IsEnabled && !outerPeer.IsEnabled, "Nested MessageBox left an owner enabled.");
                            var text = Descendants<TextBlock>(inner).Single(value => value.Text == "Unicode \u03a9 \U0001f600");
                            Require(text.FlowDirection == (options.HasFlag(MessageBoxOptions.RtlReading) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)
                                && text.TextAlignment == (options.HasFlag(MessageBoxOptions.RightAlign) ? TextAlignment.Right : TextAlignment.Left), "RTL and RightAlign were not applied independently: " + options);
                            Invoke(inner, "Cancel");
                        }, caption => MessageBox.Show(outer, "Unicode \u03a9 \U0001f600", caption, MessageBoxButton.OKCancel,
                            MessageBoxImage.Information, MessageBoxResult.Cancel, options)) == MessageBoxResult.Cancel, "Nested cancellation failed.");
                    }
                    Require(!ownerPeer.IsEnabled && outerPeer.IsEnabled, "Nested close restored the wrong owner.");
                    Invoke(outer, "OK");
                }, caption => MessageBox.Show(owner, "outer", caption)) == MessageBoxResult.OK && ownerPeer.IsEnabled, "Outer completion did not restore its owner.");
                Require(Show("owner-close", _ => owner.Close(), caption => MessageBox.Show(owner, "message", caption, MessageBoxButton.YesNo)) == MessageBoxResult.No
                    && ownerPeer.Disposed, "Owner close did not terminate and dispose the owned MessageBox.");
            }
            finally { owner.Close(); }
        });
        run("messagebox-body-show-and-cleanup-failure-identity", () =>
        {
            var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
            int sources = PresentationSource.CurrentSources.Cast<PresentationSource>().Count();
            try
            {
                factory.FailNextShow = true;
                Throws<MarkerException>(() => MessageBox.Show(owner, "message", "MessageBox failing show"));
                Require(factory.Last.Disposed && ownerPeer.IsEnabled && PresentationSource.CurrentSources.Cast<PresentationSource>().Count() == sources,
                    "Failed show leaked a peer/source or disabled its owner.");
                var primary = new MarkerException(); var cleanup = new MarkerException();
                ownerPeer.BeforeSetEnabled = enabled => { if (enabled) throw cleanup; };
                try
                {
                    Show("body-failure", _ => throw primary, caption => MessageBox.Show(owner, "message", caption));
                    throw new InvalidOperationException("Expected original dispatcher exception.");
                }
                catch (MarkerException actual) { Require(ReferenceEquals(actual, primary), "Cleanup replaced the original exception object."); }
                finally { ownerPeer.BeforeSetEnabled = null; ownerPeer.SetEnabled(true); }
                Require(factory.Last.Disposed && PresentationSource.CurrentSources.Cast<PresentationSource>().Count() == sources, "Failed body leaked a dialog source/peer.");
                Require(Show("after-failure", dialog => Invoke(dialog, "OK"), caption => MessageBox.Show(owner, "message", caption)) == MessageBoxResult.OK
                    && ownerPeer.IsEnabled, "MessageBox was unusable after failure.");
            }
            finally { factory.FailNextShow = false; ownerPeer.BeforeSetEnabled = null; owner.Close(); }
        });
        run("messagebox-capture-callout-nested-same-owner-restoration", () =>
        {
            var owner = new Window(); owner.Show(); var peer = factory.Last;
            bool nested = false;
            peer.ReleasingCapture = () =>
            {
                peer.ReleasingCapture = null;
                Require(Show("capture-inner", dialog => Invoke(dialog, "OK"), caption => MessageBox.Show(owner, "nested", caption)) == MessageBoxResult.OK,
                    "Capture callout nested MessageBox failed.");
                nested = true;
            };
            try
            {
                Require(Show("capture-outer", dialog => Invoke(dialog, "OK"), caption => MessageBox.Show(owner, "outer", caption)) == MessageBoxResult.OK
                    && nested && peer.IsEnabled, "Capture callout nesting left its owner disabled.");
            }
            finally { peer.ReleasingCapture = null; peer.SetEnabled(true); owner.Close(); }
        });
        run("messagebox-capture-callout-owner-close-hide-disable-and-throw", () =>
        {
            foreach (string action in new[] { "close", "hide", "disable", "throw" })
            {
                var owner = new Window(); owner.Show(); var peer = factory.Last;
                var survivor = new Window(); survivor.Show(); var survivorPeer = factory.Last;
                int creates = factory.CreateCount;
                Window? closedDialog = null;
                var marker = new MarkerException();
                peer.ReleasingCapture = () =>
                {
                    if (action == "close") { closedDialog = owner.OwnedWindows.Cast<Window>().Single(); owner.Close(); }
                    else if (action == "hide") owner.Hide();
                    else if (action == "disable") owner.IsEnabled = false;
                    else throw marker;
                };
                try
                {
                    if (action == "throw")
                    {
                        Require(ReferenceEquals(Throws<MarkerException>(() => MessageBox.Show(owner, "message", "MessageBox capture failure")), marker), "Capture failure identity changed.");
                        Require(peer.IsEnabled && owner.OwnedWindows.Count == 0, "Capture failure left its owner disabled or retained an owned dialog.");
                    }
                    else
                    {
                        MessageBoxResult result = Show("capture-" + action, dialog => Invoke(dialog, "OK"), caption => MessageBox.Show(owner, "message", caption));
                        Require(result == (action == "close" ? MessageBoxResult.No : MessageBoxResult.OK), $"Capture callout {action} returned the wrong terminal result: {result}.");
                        if (action == "close")
                        {
                            Require(peer.Disposed && owner.OwnedWindows.Count == 0 && closedDialog != null && PresentationSource.FromVisual(closedDialog) == null,
                                "Capture-close leaked its owner or created a dialog source.");
                            Throws<InvalidOperationException>(() => closedDialog!.Show());
                            Require(factory.CreateCount == creates, "Capture-close resurrected or created a native dialog peer.");
                        }
                        if (action == "hide") Require(!peer.IsVisible && peer.IsEnabled, "Capture-hide did not preserve hidden/enabled owner state.");
                        if (action == "disable") Require(!owner.IsEnabled && !peer.IsEnabled, "Capture-disable was undone by cleanup.");
                    }
                    Require(survivorPeer.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Capture cleanup disabled its sibling or leaked modal scope.");
                }
                finally { peer.ReleasingCapture = null; survivor.Close(); owner.Close(); }
            }
        });
        run("messagebox-existing-foreign-dispatcher-source-isolation", () =>
        {
            using var ready = new ManualResetEventSlim();
            Dispatcher? foreignDispatcher = null;
            Window? foreignWindow = null;
            Exception? foreignFailure = null;
            var foreign = new Thread(() =>
            {
                try
                {
                    PortableWindowPlatform.RegisterFactory(new ManagedFactory());
                    foreignDispatcher = Dispatcher.CurrentDispatcher;
                    foreignWindow = new Window { Title = "MessageBox foreign dispatcher", Width = 180, Height = 100 };
                    foreignWindow.Show();
                    ready.Set();
                    Dispatcher.Run();
                }
                catch (Exception exception)
                {
                    foreignFailure = exception;
                    try { foreignWindow?.Close(); }
                    catch (Exception cleanup) { foreignFailure = new AggregateException(exception, cleanup); }
                    ready.Set();
                }
            }) { IsBackground = true };
            if (OperatingSystem.IsWindows()) foreign.SetApartmentState(ApartmentState.STA);
            foreign.Start();
            try
            {
                Require(ready.Wait(TimeSpan.FromSeconds(5)) && foreignFailure == null, "Foreign dispatcher source could not be established: " + foreignFailure);
                Require(Show("foreign-source", dialog => Invoke(dialog, "OK"), caption => MessageBox.Show("message", caption)) == MessageBoxResult.OK,
                    "A foreign live source prevented the local MessageBox result.");
            }
            finally
            {
                if (foreign.IsAlive) foreignDispatcher?.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try { foreignWindow?.Close(); }
                    finally { foreignDispatcher.InvokeShutdown(); }
                }));
                Require(foreign.Join(TimeSpan.FromSeconds(5)), "Foreign dispatcher did not finish cleanup.");
            }
            Require(foreignFailure == null, "Foreign dispatcher failed during cleanup: " + foreignFailure);
        });
    }

    private static MessageBoxResult Show(string name, Action<Window> action, Func<string, MessageBoxResult> show)
    {
        string caption = name.Length == 0 ? string.Empty : "MessageBox lifecycle " + name;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            Window? dialog = PresentationSource.CurrentSources.Cast<PresentationSource>().Where(source => source.Dispatcher == Dispatcher.CurrentDispatcher)
                .Select(source => source.RootVisual).OfType<Window>().SingleOrDefault(window => window.Title == caption);
            if (dialog == null) return;
            timer.Stop();
            action(dialog);
        };
        timer.Start();
        try { return show(caption); }
        finally { timer.Stop(); }
    }

    private static IEnumerable<Button> Buttons(DependencyObject root)
        => Descendants<Button>(root);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Invoke(Window dialog, string label)
    {
        Button button = Buttons(dialog).Single(value => value.Content.ToString()!.Replace("_", "") == label);
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
    }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class PoisonConverter : System.Windows.Data.IValueConverter
    {
        internal int Calls;
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            Calls++;
            throw new InvalidOperationException("The application resource converter must never execute for MessageBox.");
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
