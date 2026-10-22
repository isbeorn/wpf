using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Input;
using MS.Internal.Portability.Windowing;

internal static class AutomationCases
{
    internal static void Run(string reportPath)
    {
        string[] nativeBaseline = NativeMappings();
        var checks = new List<object>();
        bool failed = false;
        Check("button-managed-peer-identity-name-role", () =>
        {
            var button = new Button { Content = "Run" };
            var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
            Require(peer is ButtonAutomationPeer && ReferenceEquals(peer, UIElementAutomationPeer.FromElement(button)) &&
                ReferenceEquals(peer, UIElementAutomationPeer.CreatePeerForElement(button)), "Real control peer identity was not retained.");
            Require(peer.GetName() == "Run" && peer.GetAutomationControlType() == AutomationControlType.Button, "Button name/role changed.");
            AutomationProperties.SetName(button, "Execute exposure");
            Require(peer.GetName() == "Execute exposure", "Attached name did not reach the actual peer.");
            Require(peer.GetPattern(PatternInterface.Toggle) == null, "Button invented Toggle support.");
        });
        Check("managed-schema-identifiers-and-reserved-values", () =>
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(new Button())!;
            Require(!string.IsNullOrEmpty(peer.GetLocalizedControlType()) && ControlType.Button.Id > 0 && ValuePatternIdentifiers.Pattern.Id > 0,
                "Managed control/pattern identifiers did not initialize.");
            Require(AutomationElementIdentifiers.NameProperty.Id > 0 && TextPatternIdentifiers.Pattern.Id > 0,
                "Managed property/text schema metadata did not initialize.");
            Require(ReferenceEquals(AutomationElementIdentifiers.NotSupported, AutomationElementIdentifiers.NotSupported) &&
                ReferenceEquals(TextPatternIdentifiers.MixedAttributeValue, TextPatternIdentifiers.MixedAttributeValue) &&
                !ReferenceEquals(AutomationElementIdentifiers.NotSupported, TextPatternIdentifiers.MixedAttributeValue), "Reserved schema values lost their distinct stable identity.");
            Require(!AutomationInteropProvider.ClientsAreListening, "Unavailable native export claimed clients.");
        });
        Check("textbox-managed-peer-name-role-and-value", () =>
        {
            var box = new TextBox { Text = "before" };
            AutomationProperties.SetName(box, "Exposure");
            var peer = UIElementAutomationPeer.CreatePeerForElement(box)!;
            Require(peer is TextBoxAutomationPeer && peer.GetName() == "Exposure" && peer.GetAutomationControlType() == AutomationControlType.Edit,
                "TextBox peer name/role changed.");
            Require(((IValueProvider)peer.GetPattern(PatternInterface.Value)).Value == "before", "Value did not read the actual editor.");
        });
        Check("checkbox-managed-peer-name-role-and-toggle-binding", () =>
        {
            var model = new PeerModel();
            var toggle = new CheckBox { Content = "Calibrate", IsThreeState = true };
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PeerModel.Checked)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var peer = UIElementAutomationPeer.CreatePeerForElement(toggle)!;
            Require(peer is CheckBoxAutomationPeer && peer.GetName() == "Calibrate" && peer.GetAutomationControlType() == AutomationControlType.CheckBox,
                "CheckBox peer name/role changed.");
            var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            provider.Toggle(); Require(toggle.IsChecked == true && model.Checked == true && provider.ToggleState == ToggleState.On, "Toggle did not update the real binding.");
            provider.Toggle(); Require(toggle.IsChecked == null && model.Checked == null && provider.ToggleState == ToggleState.Indeterminate, "Three-state Toggle did not reach Indeterminate.");
            provider.Toggle(); Require(toggle.IsChecked == false && model.Checked == false && provider.ToggleState == ToggleState.Off, "Toggle did not return to Off.");
            toggle.IsEnabled = false; Expect<ElementNotEnabledException>(provider.Toggle);
            Require(!peer.IsEnabled() && toggle.IsChecked == false, "Disabled toggle mutated state.");
            toggle.IsEnabled = true; provider.Toggle(); Require(peer.IsEnabled() && toggle.IsChecked == true, "Re-enabled toggle did not work.");
        });
        Check("unshown-window-managed-peer-title-role-and-hidden-children", () =>
        {
            var button = new Button { Content = "Child" };
            var window = new Window { Title = "Managed peer window", Content = button };
            try
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(window)!;
                window.ApplyTemplate(); window.Measure(new Size(240, 120)); window.Arrange(new Rect(0, 0, 240, 120));
                Require(peer is WindowAutomationPeer && peer.GetName() == window.Title && peer.GetAutomationControlType() == AutomationControlType.Window,
                    "Window peer did not describe its actual title and role.");
                Require(peer.GetChildren() == null || peer.GetChildren().Count == 0, "Unshown Window unexpectedly exposed unconstructed visual children.");
                window.Title = "Updated managed title"; Require(peer.GetName() == window.Title, "Window title change left a stale peer name.");
                var label = new Label { Content = "Labeled window" };
                var labelPeer = UIElementAutomationPeer.CreatePeerForElement(label)!;
                Require(labelPeer is LabelAutomationPeer && labelPeer.GetName() == "Labeled window", "Actual Label peer did not describe its content.");
                AutomationProperties.SetLabeledBy(window, label);
                Require(peer.GetName() == "Labeled window", "Window label did not override title.");
                window.Title = "Title behind label"; Require(peer.GetName() == "Labeled window", "Window title change displaced its label.");
                AutomationProperties.SetName(window, "Named window"); Require(peer.GetName() == "Named window", "Explicit Window name did not override label.");
                window.ClearValue(AutomationProperties.NameProperty);
                Require(peer.GetName() == "Labeled window", "Clearing explicit Window name did not restore label.");
                window.ClearValue(AutomationProperties.LabeledByProperty);
                Require(peer.GetName() == window.Title, "Clearing Window label did not restore title.");
            }
            finally { window.Close(); }
        });
        Check("invoke-command-canexecute-disabled-and-reenabled", () =>
        {
            var command = new PeerCommand();
            var button = new Button { Content = "Invoke", Command = command };
            var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
            var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invoke.Invoke(); Drain(); Require(command.Executions == 1, "Invoke did not run the real command exactly once.");
            command.Enabled = false; command.Notify(); Drain();
            Require(!button.IsEnabled && !peer.IsEnabled(), "CanExecute did not disable the real control/peer.");
            Expect<ElementNotEnabledException>(invoke.Invoke); Drain(); Require(command.Executions == 1, "Disabled Invoke executed.");
            command.Enabled = true; command.Notify(); Drain(); invoke.Invoke(); Drain(); Require(command.Executions == 2, "Re-enabled Invoke did not execute.");
        });
        Check("value-retains-binding-readonly-disabled-and-reenabled", () =>
        {
            var model = new PeerModel { Value = "original" };
            var box = new TextBox();
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(PeerModel.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var provider = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(box)!.GetPattern(PatternInterface.Value);
            provider.SetValue("updated"); Drain();
            Require(box.Text == "updated" && model.Value == "updated" && BindingOperations.IsDataBound(box, TextBox.TextProperty), "Value edit lost the actual two-way binding.");
            model.Value = "from source"; Drain(); Require(box.Text == "from source" && provider.Value == "from source", "Retained Value binding no longer followed its source.");
            provider.SetValue("updated"); Drain();
            box.IsReadOnly = true; Require(provider.IsReadOnly, "Read-only state was stale."); Expect<ElementNotEnabledException>(() => provider.SetValue("blocked"));
            Require(box.Text == "updated" && model.Value == "updated", "Read-only edit mutated text/model.");
            box.IsReadOnly = false; box.IsEnabled = false; Expect<ElementNotEnabledException>(() => provider.SetValue("blocked"));
            box.IsEnabled = true; provider.SetValue("again"); Drain(); Require(box.Text == "again" && model.Value == "again" && !provider.IsReadOnly, "Re-enabled Value edit failed.");
            Expect<ArgumentNullException>(() => provider.SetValue(null!));
        });
        Check("source-attached-peer-bounds-focus-transforms-dpi-and-detach", () =>
        {
            var host = new PeerHost();
            using var source = new PortablePresentationSource(_ => { }); source.AttachHost(host); host.Source = source;
            var button = new Button { Width = 50, Height = 30, Template = EmptyTemplate(typeof(Button)), RenderTransform = new SkewTransform(15, 0) };
            var root = new Canvas { Width = 180, Height = 100 };
            Canvas.SetLeft(button, 20); Canvas.SetTop(button, 12); root.Children.Add(button); source.RootVisual = root;
            root.Measure(new Size(180, 100)); root.Arrange(new Rect(0, 0, 180, 100)); root.UpdateLayout();
            var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
            Equal(peer.GetBoundingRectangle(), ExpectedBounds(button));
            peer.SetFocus(); Require(peer.HasKeyboardFocus() && ReferenceEquals(Keyboard.FocusedElement, button), "Peer focus bypassed the actual WPF focus path.");
            host.Origin = new Point(-180, 75); Equal(peer.GetBoundingRectangle(), ExpectedBounds(button));
            host.Scale = new DpiScale(2, 1.5); host.NotifyMetrics(); root.UpdateLayout(); Equal(peer.GetBoundingRectangle(), ExpectedBounds(button));
            Rect bounds = peer.GetBoundingRectangle(); Point click = peer.GetClickablePoint();
            Require(Math.Abs(click.X - (bounds.Left + bounds.Width / 2)) < 0.001 && Math.Abs(click.Y - (bounds.Top + bounds.Height / 2)) < 0.001, "Clickable point did not follow the source.");
            source.RootVisual = null; Require(peer.GetBoundingRectangle().IsEmpty && double.IsNaN(peer.GetClickablePoint().X), "Detached peer fabricated screen coordinates.");
            source.Dispose(); Require(peer.GetBoundingRectangle().IsEmpty && !peer.HasKeyboardFocus(), "Disposed source left screen bounds/focus.");
        });
        Check("managed-peers-do-not-claim-native-automation-export", () =>
        {
            var peer = new UIElementAutomationPeer(new UIElement());
            foreach (AutomationEvents value in Enum.GetValues<AutomationEvents>()) Require(!AutomationPeer.ListenerExists(value), "Unavailable backend claimed a listener.");
            peer.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
            var method = typeof(AutomationPeer).GetMethod("ProviderFromPeer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            try { method.Invoke(peer, [peer]); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is PlatformNotSupportedException) { return; }
            throw new InvalidOperationException("Unavailable native provider export did not produce a diagnostic.");
        });
        string[] nativeAfter = NativeMappings();
        string[] forbidden = OperatingSystem.IsWindows() ? nativeAfter.Where(path => !path.Contains("user32.dll", StringComparison.OrdinalIgnoreCase)).ToArray() : nativeAfter;
        Check("peer-queries-and-actions-do-not-load-windows-automation", () => Require(forbidden.Length == 0, "Windows native modules loaded: " + string.Join(";", forbidden)));
        var report = new { fixture = "genuine-managed-wpf-automation-peers", core = typeof(AutomationPeer).Assembly.Location, framework = typeof(Button).Assembly.Location, checks, nativeBaseline, nativeAfter, forbidden };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json); Console.WriteLine(json);
        if (failed) throw new InvalidOperationException("Managed automation peer checks failed.");

        void Check(string name, Action action)
        {
            try { action(); checks.Add(new { name, status = "pass" }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
    }

    private static string[] NativeMappings() => OperatingSystem.IsLinux() ? File.ReadLines("/proc/self/maps").Where(IsForbidden).ToArray() :
        System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Select(m => m.FileName).Where(IsForbidden).ToArray();
    private static bool IsForbidden(string path) => path.Contains("UIAutomationCore", StringComparison.OrdinalIgnoreCase) || path.Contains("user32.dll", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("PresentationNative_cor3", StringComparison.OrdinalIgnoreCase) || path.Contains("wpfgfx_cor3", StringComparison.OrdinalIgnoreCase);
    private static ControlTemplate EmptyTemplate(Type type) => new(type) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
    private static Rect ExpectedBounds(UIElement element)
    {
        Point[] p = [element.PointToScreen(new Point()), element.PointToScreen(new Point(element.RenderSize.Width, 0)),
            element.PointToScreen(new Point(0, element.RenderSize.Height)), element.PointToScreen(new Point(element.RenderSize.Width, element.RenderSize.Height))];
        return new Rect(p.Min(x => x.X), p.Min(x => x.Y), p.Max(x => x.X) - p.Min(x => x.X), p.Max(x => x.Y) - p.Min(x => x.Y));
    }
    private static void Equal(Rect actual, Rect expected)
    {
        Require(!actual.IsEmpty && actual.Width > 0 && actual.Height > 0 && Math.Abs(actual.X - expected.X) < 0.001 && Math.Abs(actual.Y - expected.Y) < 0.001 &&
            Math.Abs(actual.Width - expected.Width) < 0.001 && Math.Abs(actual.Height - expected.Height) < 0.001, $"Bounds {actual} differ from actual visual screen bounds {expected}.");
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}

internal sealed class PeerModel : INotifyPropertyChanged
{
    private string _value = "";
    public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
    public bool? Checked { get; set; } = false;
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal sealed class PeerCommand : ICommand
{
    public bool Enabled { get; set; } = true;
    public int Executions { get; private set; }
    public bool CanExecute(object? parameter) => Enabled;
    public void Execute(object? parameter) => Executions++;
    public event EventHandler? CanExecuteChanged;
    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
internal sealed class PeerHost : IPortablePresentationHost, IPortableInputHost
{
    public PortablePresentationSource? Source { get; set; }
    public Point Origin { get; set; } = new(300, 200);
    public DpiScale Scale { get; set; } = new(1, 1);
    public Size ClientSize => new(180, 100);
    public DpiScale DpiScale => Scale;
    public event Action? MetricsChanged;
    public void NotifyMetrics() => MetricsChanged?.Invoke();
    public event Action? Exposed { add { } remove { } }
    public event Action? Closed { add { } remove { } }
    public event Action<InputReport>? RawInput { add { } remove { } }
    public bool AcquireFocus(bool checkOnly) => true;
    public bool CaptureMouse() => true;
    public void ReleaseMouseCapture() { }
    public bool SetCursor(Cursor cursor) => true;
    public KeyStates GetKeyStates(Key key) => KeyStates.None;
    public PortablePointerSnapshot GetPointerState() => new(new Point(), MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released);
    public Point ClientToScreen(Point p) => new(p.X + Origin.X, p.Y + Origin.Y);
    public Point ScreenToClient(Point p) => new(p.X - Origin.X, p.Y - Origin.Y);
    public PresentationSource? SourceFromScreenPoint(Point p) => Source;
    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride) { }
}
