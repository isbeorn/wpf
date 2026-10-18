using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Input;
using MS.Internal.Portability.Settings;
using MS.Internal.Portability.Windowing;

bool mouseVanish = args.Contains("--mouse-vanish");
bool rejectCursor = args.Contains("--reject-cursor");
bool overrideCursor = args.Contains("--override-cursor");
string? cultureName = args.FirstOrDefault(value => value.StartsWith("--culture=", StringComparison.Ordinal))?.Substring("--culture=".Length);
if (cultureName != null) System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(pointerSettings: new PortablePointerSettings(4, 6, 500), mouseVanish: mouseVanish));
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        if (args.Contains("--close-during-hide"))
        {
            foreach (bool beforeApply in new[] { false, true })
            foreach (int compensationFailure in new[] { 0, 1, 2 })
            {
                var closingHost = new ControlsHost { NotifyBeforeVisibilityApply = beforeApply };
                using var closingSource = new PortablePresentationSource(_ => { });
                closingSource.AttachHost(closingHost);
                var closingProvider = (PortableInputProvider)closingSource.GetInputProvider(typeof(MouseDevice));
                closingHost.VisibilityChanged = visible =>
                {
                    if (visible) return;
                    closingSource.Dispose();
                    closingHost.AllowVisibility = compensationFailure != 1;
                    closingHost.ThrowOnVisibility = compensationFailure == 2;
                };
                bool accepted = false;
                bool failed = false;
                try { accepted = closingProvider.SetCursorVisibility(false); }
                catch (InvalidOperationException) { failed = true; }
                if (accepted || failed != (compensationFailure != 0) || !closingSource.IsDisposed || closingProvider.IsCursorHidden ||
                    closingHost.VisibilityCalls != 2 || closingHost.Visible != (compensationFailure == 0))
                    throw new InvalidOperationException($"Close during hide: before={beforeApply}, compensation={compensationFailure}, accepted={accepted}, failed={failed}, visible={closingHost.Visible}, calls={closingHost.VisibilityCalls}.");
                closingHost.AllowVisibility = true;
                closingHost.ThrowOnVisibility = false;
                closingHost.SetCursorVisibility(true);
                Console.WriteLine($"PASS close during hide before={beforeApply} compensation={compensationFailure}");
            }
            Dispatcher.CurrentDispatcher.InvokeShutdown();
            return;
        }
        var host = new ControlsHost { AllowVisibility = !rejectCursor };
        using var source = new PortablePresentationSource(size => { });
        host.Source = source;
        source.AttachHost(host);
        var selectorModel = new TextModel { Value = "first" };
        var selector = new ItemSelector();
        selector.Items.Add("first");
        selector.Items.Add("second");
        selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding(nameof(TextModel.Value)) { Source = selectorModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        selector.SelectedIndex = 1;
        if (selectorModel.Value != "second") throw new InvalidOperationException("Selector dependency-property binding did not preserve source updates.");
        var model = new TextModel();
        var box = new TextBox { Width = 200, Height = 40 };
        var content = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        content.SetValue(Control.BackgroundProperty, Brushes.White);
        box.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = content };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(TextModel.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var root = new AdornerDecorator { Child = box };
        source.RootVisual = root;
        root.Measure(new Size(200, 40));
        root.Arrange(new Rect(0, 0, 200, 40));
        root.UpdateLayout();
        if (!ReferenceEquals(Keyboard.Focus(box), box)) throw new InvalidOperationException("TextBox did not acquire keyboard focus.");
        root.UpdateLayout();
        var caret = FindVisual(root, "CaretElement") ?? throw new InvalidOperationException("The actual TextBox caret did not attach to the AdornerDecorator.");
        if (!ReferenceEquals(PresentationSource.FromVisual(caret), source)) throw new InvalidOperationException("The managed caret did not retain the actual presentation source.");
        if (args.Contains("--unicode-commits"))
        {
            UnicodeCommitChecks.Run(host,source,box,root,model,args.Single(value => value.StartsWith("--font=",StringComparison.Ordinal))[7..]);
            Dispatcher.CurrentDispatcher.InvokeShutdown();
            return;
        }
        host.Report(new RawMouseInputReport(InputMode.Foreground, 1, source, RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 10, 10, 0, IntPtr.Zero));
        if (!box.IsMouseOver) throw new InvalidOperationException("Actual mouse hit testing did not place the pointer over the TextBox.");
        if (overrideCursor) Mouse.OverrideCursor = Cursors.Cross;
        host.Report(new RawTextInputReport(source, InputMode.Foreground, 1, false, false, false, '\u03A9'));
        if (box.Text != "\u03A9" || model.Value != "\u03A9") throw new InvalidOperationException("Actual TextBox or bound model did not receive committed text.");
        bool hidden = host.Cursors.Any(cursor => ReferenceEquals(cursor, Cursors.None));
        if (hidden != (mouseVanish && !rejectCursor)) throw new InvalidOperationException("Cursor hiding did not respect profile and actual host success.");
        if (mouseVanish && rejectCursor)
        {
            host.AllowVisibility = true;
            host.Report(new RawTextInputReport(source, InputMode.Foreground, 2, false, false, false, '\u03B2'));
            if (box.Text != "\u03A9\u03B2" || model.Value != box.Text) throw new InvalidOperationException("Committed text after rejected cursor hiding did not reach the TextBox and bound model.");
            hidden = host.Cursors.Count > 0 && ReferenceEquals(host.Cursors.Last(), Cursors.None);
            if (!hidden) throw new InvalidOperationException("Rejected cursor hiding prevented a later accepted hide before mouse movement.");
        }
        if (hidden && overrideCursor)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (!ReferenceEquals(host.Cursors.Last(), Cursors.None)) throw new InvalidOperationException("Changing OverrideCursor exposed the cursor while typing kept it hidden.");
        }
        var provider = (PortableInputProvider)source.GetInputProvider(typeof(MouseDevice));
        if (hidden)
        {
            host.AllowVisibility = false;
            host.Report(new RawMouseInputReport(InputMode.Foreground, 2, source, RawMouseActions.AbsoluteMove, 11, 10, 0, IntPtr.Zero));
            if (host.Visible || !provider.IsCursorHidden) throw new InvalidOperationException("Rejected show request falsely cleared native or provider visibility ownership.");
            host.AllowVisibility = true;
            host.ThrowOnVisibility = true;
            bool threw = false;
            try { host.Report(new RawMouseInputReport(InputMode.Foreground, 3, source, RawMouseActions.AbsoluteMove, 12, 10, 0, IntPtr.Zero)); }
            catch (InvalidOperationException) { threw = true; }
            finally { host.ThrowOnVisibility = false; }
            if (!threw || host.Visible || !provider.IsCursorHidden) throw new InvalidOperationException("Throwing show request falsely cleared visibility ownership.");
        }
        host.Report(new RawMouseInputReport(InputMode.Foreground, 4, source, RawMouseActions.AbsoluteMove, 13, 10, 0, IntPtr.Zero));
        if (hidden && ReferenceEquals(host.Cursors.Last(), Cursors.None)) throw new InvalidOperationException("Actual mouse movement did not restore the host cursor.");
        if (overrideCursor && !ReferenceEquals(host.Cursors.Last(), hidden ? Cursors.Wait : Cursors.Cross)) throw new InvalidOperationException("Mouse movement did not restore the latest actual override shape.");
        box.Select(0, 1);
        if (box.SelectionStart != 0 || box.SelectionLength != 1) throw new InvalidOperationException("Actual TextBox selection could not include committed text.");
        box.Select(box.Text.Length, 0);
        if (box.CaretIndex != box.Text.Length) throw new InvalidOperationException("Actual TextBox caret did not move to the text end.");
        root.UpdateLayout();
        if (!ReferenceEquals(PresentationSource.FromVisual(caret), source)) throw new InvalidOperationException("Selection movement detached the managed caret source.");
        if (mouseVanish)
        {
            Commit('c');
            if (host.Visible || !provider.IsCursorHidden) throw new InvalidOperationException("Typing did not hide the cursor before changing sources.");
            using (var otherSource = new PortablePresentationSource(_ => { }))
            {
                var otherHost = new ControlsHost { Source = otherSource };
                otherSource.AttachHost(otherHost);
                otherSource.RootVisual = new Border { Width = 200, Height = 40, Background = Brushes.White };
                ((UIElement)otherSource.RootVisual).Measure(new Size(200, 40));
                ((UIElement)otherSource.RootVisual).Arrange(new Rect(0, 0, 200, 40));
                otherHost.Report(new RawMouseInputReport(InputMode.Foreground, 5, otherSource, RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 10, 10, 0, IntPtr.Zero));
                if (!host.Visible || provider.IsCursorHidden) throw new InvalidOperationException("Real active-source change retained the old host's hidden cursor.");
            }
            Activate();
            Commit('d');
            host.Report(new RawMouseInputReport(InputMode.Foreground, 6, source, RawMouseActions.Deactivate, 0, 0, 0, IntPtr.Zero));
            if (!host.Visible || provider.IsCursorHidden) throw new InvalidOperationException("Mouse deactivation retained cursor visibility ownership.");
            foreach (bool throwOnRestore in new[] { false, true })
            {
                Activate();
                Commit('f');
                host.AllowVisibility = throwOnRestore;
                host.ThrowOnVisibility = throwOnRestore;
                bool rejected = false;
                try { host.Report(new RawMouseInputReport(InputMode.Foreground, 6, source, RawMouseActions.Deactivate, 0, 0, 0, IntPtr.Zero)); }
                catch (InvalidOperationException) { rejected = true; }
                finally { host.AllowVisibility = true; host.ThrowOnVisibility = false; }
                if (!rejected || host.Visible || !provider.IsCursorHidden || Mouse.PrimaryDevice.ActiveSource != null)
                    throw new InvalidOperationException($"Failed visibility cleanup: throw={throwOnRestore}, rejected={rejected}, visible={host.Visible}, hidden={provider.IsCursorHidden}, active={Mouse.PrimaryDevice.ActiveSource != null}.");
                provider.NotifyDeactivate();
                if (!host.Visible || provider.IsCursorHidden) throw new InvalidOperationException("Visibility cleanup could not retry after native restoration failed.");
            }
            Activate();
            Commit('e');
            if (host.Visible || !provider.IsCursorHidden) throw new InvalidOperationException("Typing did not hide the cursor before source close.");
        }
        source.Dispose();
        if (!host.Visible || provider.IsCursorHidden) throw new InvalidOperationException("Source close retained a hidden native cursor.");
        Mouse.OverrideCursor = null;
        if (PresentationSource.FromVisual(caret) != null || Keyboard.FocusedElement != null) throw new InvalidOperationException("Source disposal retained the caret source or keyboard focus.");
        if (mouseVanish)
        {
            foreach (bool throwOnRestore in new[] { false, true })
            {
                var failingHost = new ControlsHost();
                var failingSource = new PortablePresentationSource(_ => { });
                failingSource.AttachHost(failingHost);
                var failingProvider = (PortableInputProvider)failingSource.GetInputProvider(typeof(MouseDevice));
                if (!failingProvider.SetCursorVisibility(false)) throw new InvalidOperationException("Close-failure fixture could not hide its native cursor.");
                failingHost.AllowVisibility = throwOnRestore;
                failingHost.ThrowOnVisibility = throwOnRestore;
                bool rejected = false;
                try { failingSource.Dispose(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected || !failingSource.IsDisposed || failingHost.Visible) throw new InvalidOperationException("Close concealed failed native visibility cleanup or retained the source.");
                int calls = failingHost.VisibilityCalls;
                bool disposed = false;
                try { failingProvider.SetCursorVisibility(true); }
                catch (ObjectDisposedException) { disposed = true; }
                if (!disposed || failingHost.VisibilityCalls != calls) throw new InvalidOperationException("Disposed visibility provider called a dead source host.");
                failingHost.AllowVisibility = true;
                failingHost.ThrowOnVisibility = false;
                failingHost.SetCursorVisibility(true);
            }
        }
        if (OperatingSystem.IsLinux() && File.ReadLines("/proc/self/maps").Any(line => line.Contains("user32.dll", StringComparison.OrdinalIgnoreCase) || line.Contains("PresentationNative_cor3", StringComparison.OrdinalIgnoreCase) || line.Contains("wpfgfx_cor3", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Windows native modules were mapped by actual TextBox input or binding.");
        Console.WriteLine($"PASS actual source-attached TextBox caret, selection, BMP two-way binding, Selector binding and cursor profile vanish={mouseVanish} reject={rejectCursor}");
        Dispatcher.CurrentDispatcher.InvokeShutdown();

        void Activate() => host.Report(new RawMouseInputReport(InputMode.Foreground, 7, source, RawMouseActions.Activate | RawMouseActions.AbsoluteMove, 10, 10, 0, IntPtr.Zero));
        void Commit(char character)
        {
            host.Report(new RawTextInputReport(source, InputMode.Foreground, 8, false, false, false, character));
            if (model.Value != box.Text) throw new InvalidOperationException("Visibility lifecycle input stopped updating the actual bound model.");
        }
    }
    catch (Exception exception) { failure = exception; }
});
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start(); thread.Join();
if (failure != null) Console.WriteLine(failure);
return failure == null ? 0 : 1;

static Visual? FindVisual(Visual visual, string name)
{
    if (visual.GetType().Name == name) return visual;
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        if (VisualTreeHelper.GetChild(visual, index) is Visual child && FindVisual(child, name) is Visual match) return match;
    return null;
}

sealed class TextModel { public string Value { get; set; } = ""; }
sealed class ItemSelector : System.Windows.Controls.Primitives.Selector { }
sealed class ControlsHost : IPortablePresentationHost, IPortableInputHost
{
    public PortablePresentationSource? Source { get; set; }
    public Size ClientSize => new(200, 40);
    public DpiScale DpiScale => new(1, 1);
    public event Action? MetricsChanged { add { } remove { } }
    public event Action? Exposed { add { } remove { } }
    public event Action? Closed { add { } remove { } }
    public event Action<InputReport>? RawInput;
    public void Report(InputReport report) => RawInput?.Invoke(report);
    public bool AcquireFocus(bool checkOnly) => true;
    public bool CaptureMouse() => true;
    public void ReleaseMouseCapture() { }
    public bool AllowCursor { get; set; } = true;
    public bool AllowVisibility { get; set; } = true;
    public bool ThrowOnVisibility { get; set; }
    public bool Visible { get; private set; } = true;
    public int VisibilityCalls { get; private set; }
    public bool NotifyBeforeVisibilityApply { get; set; }
    public Action<bool>? VisibilityChanged { get; set; }
    private Cursor _requestedCursor = System.Windows.Input.Cursors.Arrow;
    public List<Cursor> Cursors { get; } = new();
    public bool SetCursor(Cursor cursor) { if (!AllowCursor) return false; _requestedCursor = cursor; Cursors.Add(Visible ? cursor : System.Windows.Input.Cursors.None); return true; }
    public bool SetCursorVisibility(bool visible)
    {
        VisibilityCalls++;
        if (ThrowOnVisibility) throw new InvalidOperationException("Native cursor visibility request failed.");
        if (!AllowVisibility) return false;
        if (NotifyBeforeVisibilityApply) VisibilityChanged?.Invoke(visible);
        Visible = visible;
        Cursors.Add(visible ? _requestedCursor : System.Windows.Input.Cursors.None);
        if (!NotifyBeforeVisibilityApply) VisibilityChanged?.Invoke(visible);
        return true;
    }
    public KeyStates GetKeyStates(Key key) => KeyStates.None;
    public PortablePointerSnapshot GetPointerState() => new(new Point(0, 0), MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released);
    public Point ClientToScreen(Point point) => point;
    public Point ScreenToClient(Point point) => point;
    public PresentationSource? SourceFromScreenPoint(Point point) => Source;
    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride) { }
}
