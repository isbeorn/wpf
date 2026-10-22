extern alias WpfFork;

using System.Diagnostics;
using System.Collections.Concurrent;
using Tmds.DBus.Protocol;
using ICommand = System.Windows.Input.ICommand;
using System.Text.Json;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Data;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;


internal static class AtSpiChecks
{
    private const string Accessible = "org.a11y.atspi.Accessible";
    private const string Component = "org.a11y.atspi.Component";
    private static readonly List<object> Observations = new();

    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait,
        Action drain, Func<ControlTemplate> template)
    {
        Require(Environment.GetEnvironmentVariable("WPF_ATSPI_PRIVATE_BUS") == "1", "AT-SPI tests require the explicitly owned private session bus.");
        string address = JsonDocument.Parse(Execute("busctl", "--user", "--json=short", "call", "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress")).RootElement.GetProperty("data")[0].GetString()!;
        Require(address.StartsWith("unix:", StringComparison.Ordinal), "Private accessibility bus did not provide a Unix address.");
        var protocol = new Protocol(address);
        var command = new BoundCommand();
        Button? button = null;
        Window Create(string title)
        {
            button = new Button { Content = "Acquire frame", Width = 120, Height = 40, Margin = new Thickness(17),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                OverridesDefaultStyle = true, Template = template(), Background = Brushes.Green };
            button.SetBinding(Button.CommandProperty, new Binding(nameof(CommandModel.Command)) { Source = new CommandModel(command) });
            return new Window { Title = title, Width = 280, Height = 180, Left = 40, Top = 50,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, Content = button };
        }
        T External<T>(Func<T> action)
        {
            var task = Task.Run(action);
            wait(() => task.IsCompleted);
            return task.GetAwaiter().GetResult();
        }
        void ExternalAction(Action action) => External(() => { action(); return true; });
        (Node node, List<Node> children) ReadChildrenBeforeLayout(string windowTitle)
        {
            var query = Task.Run(() => { var node = protocol.FindWindow(windowTitle); return (node, children: protocol.Children(node)); });
            var timer = Stopwatch.StartNew();
            // Service the existing native dispatcher queue before permitting WPF's first layout.
            while (!query.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(2.5))
            {
                OwnedNativeAccess.Call(OwnedNativeAccess.Pump, "WaitAndDispatch", 0);
                Thread.Sleep(1);
            }
            Require(query.IsCompleted, "The external pre-layout query did not finish.");
            return query.GetAwaiter().GetResult();
        }
        string title = "WPF AT-SPI Window";
        var window = Create(title);
        Node? root = null;
        Node? child = null;
        try
        {
            window.Show(); drain();
            run("atspi-external-window-button-tree-and-identity", () =>
            {
                root = External(() => protocol.FindWindow(title));
                Require(External(() => protocol.Role(root)) == 23, "Window did not expose the AT-SPI frame role.");
                child = External(() => protocol.Children(root).Single(node => protocol.Name(node) == "Acquire frame"));
                Require(External(() => protocol.Role(child)) == 43, "Button did not expose the AT-SPI push button role.");
                Require(!External(() => protocol.Interfaces(root)).Contains("org.a11y.atspi.Action"), "Window invented an Invoke action.");
                Require(!External(() => protocol.Interfaces(child)).Any(value => value is "org.a11y.atspi.Text" or "org.a11y.atspi.EditableText" or "org.a11y.atspi.Value"), "The initial Button profile invented text/value patterns.");
                window.Title = title + " renamed"; button!.Content = "Capture again";
                Require(External(() => protocol.Name(root)) == title + " renamed" && External(() => protocol.Name(child)) == "Capture again", "Protocol names did not follow genuine peer state.");
                window.Title = title; button.Content = "Acquire frame";
                Require(External(() => protocol.Children(root)).Contains(child), "Repeated tree queries replaced the Button object identity.");
                Require(External(() => protocol.Children(new Node(child.Bus, "/org/a11y/atspi/accessible/root"))).Contains(root), "The Window was not a child of the application root.");
            });
            if (root is null || child is null) return;
            run("atspi-external-real-bound-command-invoke-disable-enable", () =>
            {
                Require(External(() => protocol.Call(child, "org.a11y.atspi.Action", "GetName", "i", "0")).GetProperty("data")[0].GetString() == "click", "Actual Button did not offer Invoke.");
                ExternalAction(() => protocol.Call(child, "org.a11y.atspi.Action", "DoAction", "i", "0"));
                wait(() => command.Count == 1);
                command.SetEnabled(false); drain();
                Require(!button!.IsEnabled && !External(() => protocol.State(child, 8)), "CanExecute=false did not disable the real Button and protocol state.");
                try { ExternalAction(() => protocol.Call(child, "org.a11y.atspi.Action", "DoAction", "i", "0")); }
                catch (InvalidOperationException exception) when (!exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) { Observations.Add(new { disabledInvokeError = exception.Message }); }
                drain(); Require(command.Count == 1, "A disabled command executed via AT-SPI.");
                command.SetEnabled(true); drain();
                Require(button.IsEnabled && External(() => protocol.State(child, 8)), "CanExecute=true did not restore enabled state.");
                ExternalAction(() => protocol.Call(child, "org.a11y.atspi.Action", "DoAction", "i", "0"));
                wait(() => command.Count == 2);
            });
            run("atspi-external-focus-forwarding", () =>
            {
                window.Activate(); drain();
                Keyboard.ClearFocus(); drain();
                Require(!External(() => protocol.State(child, 12)), "Cleared WPF focus remained focused on the wire.");
                using var observer = External(() => new FocusObserver(address, child.Path));
                drain();
                ExternalAction(() => protocol.Call(child, Component, "GrabFocus"));
                wait(() => ReferenceEquals(Keyboard.FocusedElement, button));
                wait(() => observer.Signals.Any(signal => signal is ("focused", 1)));
                Require(External(() => protocol.State(child, 12)), "AT-SPI did not report genuine WPF keyboard focus.");
                Keyboard.ClearFocus(); drain();
                Require(!External(() => protocol.State(child, 12)), "Focus loss was not reflected on the wire.");
                ExternalAction(() => protocol.Call(child, Component, "GrabFocus"));
                wait(() => observer.Signals.Count(signal => signal is ("focused", 1)) >= 2);
                Require(observer.Signals.Select(signal => signal.state).SequenceEqual(new[] { 1, 0, 1 }), "The external focus observer did not receive gain/loss/gain in order.");
                Observations.Add(new { focusSignals = observer.Signals.Select(signal => new { signal.kind, signal.state }).ToArray(), source = "separate real accessibility-bus connection registered as an event listener" });
            });
            run("atspi-external-physical-bounds-dpi-and-movement", () =>
            {
                foreach (int dpi in new[] { 96, 192, 96 })
                {
                    native.SetDpi(dpi); drain();
                    var xid = native.FindWindow(title);
                    var retainedOrigin = native.ClientOrigin(xid);
                    var retainedSize = native.Size(xid);
                    int[] retainedWire = External(() => protocol.Extents(root));
                    var retainedPeer = UIElementAutomationPeer.CreatePeerForElement(window)!.GetBoundingRectangle();
                    Require(retainedWire.Zip(new[] { retainedOrigin.X, retainedOrigin.Y, retainedSize.Width, retainedSize.Height }).All(pair => Math.Abs(pair.First - pair.Second) <= 1) &&
                        Math.Abs(retainedPeer.Width - retainedSize.Width) <= 1 && Math.Abs(retainedPeer.Height - retainedSize.Height) <= 1,
                        "The current native extent, actual peer and wire disagree before explicit declared resizing.");
                    Observations.Add(new { phase = "external-DPI-before-explicit-resize", dpi, clientWidth = retainedSize.Width, clientHeight = retainedSize.Height,
                        currentLogicalWidth = window.Width, currentLogicalHeight = window.Height, peer = retainedPeer.ToString(), retainedWire,
                        policy = "Global XSETTINGS DPI changes retain physical native extent; explicit public Window dimensions request resizing." });
                    window.Width = 280; window.Height = 180; drain();
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var before = native.ClientOrigin(xid);
                        window.Left += direction * 23; window.Top += direction * 11; drain();
                        var origin = native.ClientOrigin(xid);
                        var size = native.Size(xid);
                        Observations.Add(new { phase = "native-dpi-size-precondition", dpi, direction, originX = origin.X, originY = origin.Y, beforeX = before.X, beforeY = before.Y, clientWidth = size.Width, clientHeight = size.Height, window.Width, window.Height, window.ActualWidth, window.ActualHeight });
                        Require(origin.X - before.X == direction * 23 * dpi / 96 && origin.Y - before.Y == direction * 11 * dpi / 96,
                            "The declared movement did not reach the actual X11 client in both axes.");
                        Require(size.Width == 280 * dpi / 96 && size.Height == 180 * dpi / 96, "The actual X11 client did not follow declared DPI dimensions.");
                        foreach (var (node, element, role) in new[] { (root, (UIElement)window, "Window"), (child, (UIElement)button!, "Button") })
                        {
                            var expected = UIElementAutomationPeer.CreatePeerForElement(element)!.GetBoundingRectangle();
                            int[] actual = External(() => protocol.Extents(node));
                            int[] nativeExpected = role == "Window" ? [origin.X, origin.Y, size.Width, size.Height] :
                                [origin.X + 17 * dpi / 96, origin.Y + 17 * dpi / 96, 120 * dpi / 96, 40 * dpi / 96];
                            Require(double.IsFinite(expected.X) && double.IsFinite(expected.Y) && expected.Width > 0 && expected.Height > 0 &&
                                Math.Abs(actual[0] - expected.X) <= 1 && Math.Abs(actual[1] - expected.Y) <= 1 &&
                                Math.Abs(actual[2] - expected.Width) <= 1 && Math.Abs(actual[3] - expected.Height) <= 1 &&
                                actual.Zip(nativeExpected).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                                $"{role} DPI {dpi}: wire [{string.Join(',', actual)}], peer {expected}, native/declared [{string.Join(',', nativeExpected)}].");
                            Observations.Add(new { dpi, direction, role, peer = expected.ToString(), actual, nativeExpected });
                        }
                    }
                }
            });
            run("atspi-external-initial-tree-before-first-layout", () =>
            {
                var initialButton = new Button { Content = "Initially declared Button", Width = 120, Height = 40,
                    OverridesDefaultStyle = true, Template = template() };
                var initial = new Window { Title = "WPF AT-SPI before first layout", Width = 280, Height = 180,
                    ShowActivated = false, Content = initialButton };
                try
                {
                    initial.Show();
                    var first = ReadChildrenBeforeLayout("WPF AT-SPI before first layout");
                    Require(!initial.IsLoaded && first.children.Count == 0, "The fixture did not observe the actual pre-layout empty tree.");
                    drain();
                    wait(() => initial.IsLoaded && initialButton.IsLoaded && initialButton.ActualWidth == 120);
                    Require(ReferenceEquals(PresentationSource.FromVisual(initialButton), PresentationSource.FromVisual(initial)), "The genuine declared Button was not attached after first layout.");
                    string[] names = External(() => protocol.Children(first.node).Select(protocol.Name).ToArray());
                    Observations.Add(new { preLayoutChildren = first.children.Count, loaded = initial.IsLoaded, postLayoutNames = names });
                    Require(names.Contains("Initially declared Button"), "The native server cached the initial empty tree after genuine WPF layout/Loaded.");
                }
                finally { initial.Close(); }
            });
            run("atspi-external-close-before-first-loaded", () =>
            {
                var closing = new Window { Title = "WPF AT-SPI close before Loaded", Width = 160, Height = 100,
                    ShowActivated = false, Content = new Button { Content = "Never loaded", OverridesDefaultStyle = true, Template = template() } };
                try
                {
                    closing.Show();
                    var first = ReadChildrenBeforeLayout("WPF AT-SPI close before Loaded");
                    Require(!closing.IsLoaded && first.children.Count == 0, "The close case was not observed before first Loaded.");
                    closing.Close(); drain();
                    Require(!External(() => protocol.Windows()).Contains(first.node), "Close-before-Loaded left or resurrected a native root.");
                    bool rejected = false;
                    try { External(() => protocol.Name(first.node)); }
                    catch (InvalidOperationException exception) when (!exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
                    Require(rejected, "The closed pre-layout root remained callable after the Loaded queue drained.");
                }
                finally { closing.Close(); }
            });
            run("atspi-external-hide-show-close-reopen-cleanup", () =>
            {
                window.Hide(); drain();
                Require(!External(() => protocol.State(child, 25)), "Hidden window child remains showing.");
                window.Show(); drain();
                Require(External(() => protocol.State(child, 25)), "Shown window child did not become showing.");
                Require(External(() => protocol.FindWindow(title)) == root && External(() => protocol.Children(root)).Contains(child), "Hide/Show changed accessible identity.");
                window.Close(); drain();
                Require(!External(() => protocol.Windows()).Contains(root), "Closed Window registration survived native cleanup.");
                bool rejected = false;
                try { External(() => protocol.Name(child)); } catch (InvalidOperationException exception) when (!exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) { rejected = true; Observations.Add(new { detachedObjectError = exception.Message }); }
                Require(rejected, "Closed Button object remains callable.");
                title = "WPF AT-SPI Reopened"; window = Create(title); window.Show(); drain();
                Node reopened = External(() => protocol.FindWindow(title));
                Require(reopened != root && External(() => protocol.Children(reopened)).All(node => node != child), "Reopened Window reused detached protocol objects.");
                Node reopenedChild = External(() => protocol.Children(reopened).Single(node => protocol.Name(node) == "Acquire frame"));
                ExternalAction(() => protocol.Call(reopenedChild, "org.a11y.atspi.Action", "DoAction", "i", "0"));
                wait(() => command.Count == 3);
                window.Close(); drain();
                Require(!External(() => protocol.Windows()).Contains(reopened), "Reopened Window registration leaked after Close.");
            });
            run("atspi-external-detached-child-rejects-stale-action-without-parent-query", () =>
            {
                const string detachedTitle = "WPF AT-SPI detached child";
                var live = Create(detachedTitle);
                try
                {
                    live.Show(); drain();
                    Node liveRoot = External(() => protocol.FindWindow(detachedTitle));
                    Node stale = External(() => protocol.Children(liveRoot).Single(node => protocol.Name(node) == "Acquire frame"));
                    int before = command.Count;
                    var detachedButton = (Button)live.Content;
                    live.Content = null; drain();
                    bool rejected = false;
                    try { External(() => protocol.Call(stale, "org.a11y.atspi.Action", "DoAction", "i", "0")); }
                    catch (InvalidOperationException exception) when (!exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    { rejected = true; Observations.Add(new { detachedChildError = exception.Message }); }
                    Require(rejected && command.Count == before, "A detached real Button remained invokable on its stale path without refreshing its parent.");
                    live.Content = detachedButton; drain();
                    Node reattached = External(() => protocol.Children(liveRoot).Single(node => protocol.Name(node) == "Acquire frame"));
                    Require(reattached != stale, "Reattached control reused its invalidated stale path.");
                    External(() => protocol.Call(reattached, "org.a11y.atspi.Action", "DoAction", "i", "0"));
                    wait(() => command.Count == before + 1);
                }
                finally { live.Close(); drain(); }
            });
        }
        finally
        {
            window.Close();
            string? output = Environment.GetEnvironmentVariable("WPF_ATSPI_OBSERVATIONS");
            if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(new { protocol.Trace, Observations }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    internal static void ExerciseDisconnect(Action<string, Action> run, Action<Func<bool>> wait, Action drain, Func<ControlTemplate> template)
    {
        Require(Environment.GetEnvironmentVariable("WPF_ATSPI_PRIVATE_BUS") == "1", "Disconnect checks require the explicitly owned private bus.");
        string address = JsonDocument.Parse(Execute("busctl", "--user", "--json=short", "call", "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress")).RootElement.GetProperty("data")[0].GetString()!;
        var protocol = new Protocol(address);
        const string title = "WPF AT-SPI disconnected bus cleanup";
        var window = new Window { Title = title, Width = 220, Height = 120, ShowActivated = false,
            Content = new Button { Content = "Disconnected cleanup", OverridesDefaultStyle = true, Template = template() } };
        try
        {
            window.Show(); drain();
            var query = Task.Run(() => protocol.Children(protocol.FindWindow(title)));
            wait(() => query.IsCompleted);
            Require(query.GetAwaiter().GetResult().Count == 1, "The real peer was not registered before disconnect.");
            run("atspi-owned-bus-disconnect-before-Closed-dispatch-window-cleanup", () =>
            {
                int pid = JsonDocument.Parse(Execute("busctl", "--address=" + address, "--json=short", "call", "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID", "s", "org.freedesktop.DBus")).RootElement.GetProperty("data")[0].GetInt32();
                Require(pid.ToString() == Environment.GetEnvironmentVariable("WPF_ATSPI_OWNED_BUS_PID") &&
                    File.ReadAllText($"/proc/{pid}/stat").Split(' ')[21] == Environment.GetEnvironmentVariable("WPF_ATSPI_OWNED_BUS_START"), "The disconnect PID does not match the runner's owned accessibility daemon identity.");
                using var bus = Process.GetProcessById(pid);
                Execute("kill", "-TERM", pid.ToString());
                Require(bus.WaitForExit(2000), "The owned accessibility daemon did not exit before shutdown.");
                // Deliberately close before draining the queued native Closed callback.
                window.Close();
                drain();
                Require(PresentationSource.FromVisual(window) is null, "The disconnected-bus Window did not release its actual presentation source.");
            });
        }
        finally { window.Close(); drain(); }
    }
    internal static void ExerciseText(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait,
        Action drain, string fontPath, bool includeRichText = false)
    {
        Require(Environment.GetEnvironmentVariable("WPF_ATSPI_PRIVATE_BUS") == "1", "Text AT-SPI checks require the explicitly owned private bus.");
        Require(File.Exists(fontPath), "Pass the existing pinned DejaVuSans font.");
        string address = JsonDocument.Parse(Execute("busctl", "--user", "--json=short", "call", "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress")).RootElement.GetProperty("data")[0].GetString()!;
        Require(address.StartsWith("unix:", StringComparison.Ordinal), "Private accessibility bus did not provide a Unix address.");
        var protocol = new Protocol(address);
        var model = new TextModel();
        var box = new TextBox { Width = 220, Height = 42, Margin = new Thickness(12),
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans") };
        WpfFork::System.Windows.Automation.AutomationProperties.SetName(box, "Sequence annotation");
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(TextModel.Value)) { Source = model,
            Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var window = new Window { Title = "WPF AT-SPI genuine TextBox", Width = 280, Height = 150,
            Left = 40, Top = 50, Content = box, ShowActivated = false };
        string phase = "genuine-editor-preconditions";
        bool preconditions = false, exported = false;
        T External<T>(Func<T> action)
        {
            var task = Task.Run(action);
            wait(() => task.IsCompleted);
            return task.GetAwaiter().GetResult();
        }
        Node? editor = null;
        try
        {
            window.Show(); drain(); window.Activate(); box.Focus(); drain();
            run("atspi-text-genuine-Classic-editor-binding-layout-focus", () =>
            {
                var xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && box.IsLoaded && box.ActualWidth > 0 && box.ActualHeight > 0 && ReferenceEquals(Keyboard.FocusedElement, box));
                Require(box.Template is not null && box.Template.FindName("PART_ContentHost", box) is not null,
                    "The genuine Classic TextBox content host did not construct.");
                Require(box.Text == "abcdef" && model.Value == box.Text && BindingOperations.IsDataBound(box, TextBox.TextProperty),
                    "The actual editor text or retained two-way binding differs.");
                Require(PresentationSource.FromVisual(box) == PresentationSource.FromVisual(window), "The editor is not attached to its actual native Window.");
                Observations.Add(new { phase, actualText = box.Text, box.ActualWidth, box.ActualHeight,
                    template = box.Template!.GetType().AssemblyQualifiedName, actualFocus = ReferenceEquals(Keyboard.FocusedElement, box) });
                preconditions = true;
            });
            if (!preconditions) return;
            phase = "external-editor-Text-interface-and-Entry-role";
            run("atspi-text-external-genuine-editor-Text-interface-and-Entry-role", () =>
            {
                string editorWindowTitle = window.Title;
                Node root = External(() => protocol.FindWindow(editorWindowTitle));
                editor = External(() => protocol.Children(root).Single(node => protocol.Name(node) == "Sequence annotation"));
                string[] interfaces = External(() => protocol.Interfaces(editor));
                uint role = External(() => protocol.Role(editor));
                Observations.Add(new { phase, editor, interfaces, role, expectedRole = 79, actualText = box.Text });
                Require(interfaces.Contains("org.a11y.atspi.Text") && role == 79,
                    $"Actual WPF TextBox must export Text and Entry; role={role}, interfaces=[{string.Join(',', interfaces)}].");
                exported = true;
            });
            if (!exported || editor is null) return;
            using var observer = External(() => new TextObserver(address, editor.Path));
            phase = "external-collapsed-caret-forward-backward";
            run("atspi-text-external-collapsed-caret-forward-backward-and-signals", () =>
            {
                foreach (int position in new[] { 3, 5, 1 })
                {
                    box.CaretIndex = position; drain();
                    Require(box.SelectionLength == 0 && box.CaretIndex == position, "Actual editor did not establish the collapsed caret.");
                    Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == position,
                        "Wire caret does not follow the actual WPF caret.");
                    wait(() => observer.Signals.Any(signal => signal.member == "TextCaretMoved" && signal.detail1 == position));
                }
            });
            phase = "external-selection-grow-shrink-clear";
            run("atspi-text-external-selection-grow-shrink-clear-and-signals", () =>
            {
                foreach (var (start, length) in new[] { (2, 2), (1, 4), (2, 1) })
                {
                    box.Select(start, length); drain();
                    Require(box.SelectionStart == start && box.SelectionLength == length, "Actual editor selection differs.");
                    Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetNSelections")).GetProperty("data")[0].GetInt32() == 1,
                        "Wire selection count does not describe the actual selection.");
                    var range = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetSelection", "i", "0")).GetProperty("data");
                    Require(range[0].GetInt32() == start && range[1].GetInt32() == start + length, "Wire selection endpoints differ.");
                    Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == start + length,
                        "Wire caret differs from the forward programmatic selection's active end.");
                    Observations.Add(new { phase, start, length, expectedActiveEnd = start + length,
                        publicCaretIndexAsSelectionStart = box.CaretIndex });
                }
                box.CaretIndex = 0; drain();
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetNSelections")).GetProperty("data")[0].GetInt32() == 0,
                    "Clearing the actual selection left a wire selection.");
                wait(() => observer.Signals.Any(signal => signal.member == "TextSelectionChanged"));
                Observations.Add(new { phase, signals = observer.Signals.Select(signal => new { signal.member, signal.detail, signal.detail1, signal.detail2 }).ToArray(), box.SelectionStart, box.SelectionLength,
                    publicCaretIndexAsSelectionStart = box.CaretIndex });
            });
            phase = "external-Unicode-scalar-range";
            run("atspi-text-external-Unicode-scalar-count-and-complete-range", () =>
            {
                model.Value = "A\U0001F600e\u0301Z"; drain();
                Require(box.Text == model.Value && box.Text.Length == 6 && BindingOperations.IsDataBound(box, TextBox.TextProperty),
                    "The genuine editor did not retain the source update and binding.");
                Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CharacterCount")) == 5,
                    "AT-SPI CharacterCount counted UTF-16 code units.");
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetText", "ii", "1", "2")).GetProperty("data")[0].GetString() == "\U0001F600",
                    "The AT-SPI range split the supplementary character.");
                box.CaretIndex = 3; drain();
                Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == 2,
                    "The actual UTF-16 caret after the emoji did not become codepoint offset 2.");
                box.Select(1, 2); drain();
                var selection = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetSelection", "i", "0")).GetProperty("data");
                Require(selection[0].GetInt32() == 1 && selection[1].GetInt32() == 2 &&
                    External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == 2,
                    "The selected complete emoji did not retain scalar endpoints and the real moving end.");
            });
            run("atspi-text-external-caret-and-selection-setters-reach-real-Unicode-editor", () =>
            {
                foreach (var (scalar, utf16) in new[] { (0, 0), (5, 6), (2, 3) })
                {
                    Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "SetCaretOffset", "i", scalar.ToString())).GetProperty("data")[0].GetBoolean(), "Wire caret navigation was rejected.");
                    drain();
                    Require(box.SelectionLength == 0 && box.CaretIndex == utf16, "Wire scalar caret navigation did not reach the actual UTF-16 editor position.");
                }
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "AddSelection", "ii", "1", "2")).GetProperty("data")[0].GetBoolean(), "Wire selection creation was rejected.");
                Require(box.SelectionStart == 1 && box.SelectionLength == 2, "Wire selection split the actual supplementary character.");
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "SetSelection", "iii", "0", "0", "1")).GetProperty("data")[0].GetBoolean(), "Wire selection update was rejected.");
                Require(box.SelectionStart == 0 && box.SelectionLength == 1, "Wire selection update did not reach the actual editor.");
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "RemoveSelection", "i", "0")).GetProperty("data")[0].GetBoolean(), "Wire selection removal was rejected.");
                Require(box.SelectionLength == 0 && box.CaretIndex == 1, "Wire selection removal did not retain the actual moving caret.");
            });
            phase = "native-selection-active-end-both-directions";
            run("atspi-text-native-forward-reverse-selection-active-end", () =>
            {
                model.Value = "abcdef"; drain(); box.CaretIndex = 4; drain();
                native.Focus(native.FindWindow(window.Title)); box.Focus(); drain();
                try
                {
                    native.Key('\uffe1', true); native.Type('\uff53');
                    wait(() => box.SelectionStart == 4 && box.SelectionLength == 1);
                    Require(box.CaretIndex == 4 && External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == 5,
                        "Forward native selection used SelectionStart instead of its actual moving end.");
                    native.Type('\uff51'); wait(() => box.SelectionLength == 0 && box.CaretIndex == 4);
                    native.Type('\uff51'); native.Type('\uff51');
                    wait(() => box.SelectionStart == 2 && box.SelectionLength == 2);
                    Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CaretOffset")) == 2,
                        "Reverse native selection did not move the wire caret to its actual leading end.");
                }
                finally { native.Key('\uffe1', false); }
                wait(() => Keyboard.IsKeyUp(Key.LeftShift));
            });
            run("atspi-text-external-character-range-at-document-end-is-empty", () =>
            {
                model.Value = "one two"; drain();
                var boundary = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetStringAtOffset", "iu", "7", "0")).GetProperty("data");
                Require(boundary[0].GetString() == "" && boundary[1].GetInt32() == 7 && boundary[2].GetInt32() == 7,
                    "Character granularity at CharacterCount repeated the last character instead of an empty end range.");
            });
            run("atspi-text-external-word-range-before-document-start-is-empty", () =>
            {
                var boundary = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetTextBeforeOffset", "iu", "0", "1")).GetProperty("data");
                Require(boundary[0].GetString() == "" && boundary[1].GetInt32() == 0 && boundary[2].GetInt32() == 0,
                    "Word granularity before document start repeated the first word instead of an empty start range.");
            });
            run("atspi-text-external-word-range-after-document-end-is-empty", () =>
            {
                var boundary = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetTextAfterOffset", "iu", "7", "1")).GetProperty("data");
                Require(boundary[0].GetString() == "" && boundary[1].GetInt32() == 7 && boundary[2].GetInt32() == 7,
                    "Word granularity after document end repeated the final word instead of an empty end range.");
            });
            run("atspi-text-external-paired-character-word-and-empty-boundaries", () =>
            {
                foreach (var (method, offset, unit, text, start, end) in new[]
                {
                    ("GetStringAtOffset", 0, 0, "o", 0, 1), ("GetStringAtOffset", 7, 0, "", 7, 7),
                    ("GetTextAtOffset", 0, 0, "o", 0, 1), ("GetTextAtOffset", 7, 0, "", 7, 7),
                    ("GetTextBeforeOffset", 0, 0, "", 0, 0), ("GetTextBeforeOffset", 7, 0, "o", 6, 7),
                    ("GetTextAfterOffset", 0, 0, "n", 1, 2), ("GetTextAfterOffset", 6, 0, "", 7, 7),
                    ("GetTextAfterOffset", 7, 0, "", 7, 7), ("GetStringAtOffset", 7, 1, "", 7, 7),
                    ("GetTextBeforeOffset", 7, 1, "two", 4, 7)
                })
                {
                    var boundary = External(() => protocol.Call(editor, "org.a11y.atspi.Text", method, "iu", offset.ToString(), unit.ToString())).GetProperty("data");
                    Require(boundary[0].GetString() == text && boundary[1].GetInt32() == start && boundary[2].GetInt32() == end,
                        $"{method}({offset},{unit}) did not return its expected boundary range.");
                }
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetText", "ii", "7", "7")).GetProperty("data")[0].GetString() == "", "An empty end range returned text.");
                model.Value = ""; drain();
                Require(External(() => protocol.IntProperty(editor, "org.a11y.atspi.Text", "CharacterCount")) == 0, "The empty source has a nonzero wire count.");
                foreach (string method in new[] { "GetStringAtOffset", "GetTextAtOffset", "GetTextBeforeOffset", "GetTextAfterOffset" })
                {
                    var empty = External(() => protocol.Call(editor, "org.a11y.atspi.Text", method, "iu", "0", "0")).GetProperty("data");
                    Require(empty[0].GetString() == "" && empty[1].GetInt32() == 0 && empty[2].GetInt32() == 0, "An empty editor invented a range.");
                }
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetText", "ii", "0", "-1")).GetProperty("data")[0].GetString() == "", "Empty whole-document text differs.");
                wait(() => observer.Signals.Any(signal => signal.member == "TextChanged" && signal.detail == "insert") &&
                    observer.Signals.Any(signal => signal.member == "TextChanged" && signal.detail == "delete"));
            });
            run("atspi-text-explicit-legacy-end-boundaries-reject-with-modern-start-forms-working", () =>
            {
                model.Value = "one two"; drain();
                foreach (string boundary in new[] { "2", "6" })
                {
                    bool rejected = false;
                    try { External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetTextAtOffset", "iu", "0", boundary)); }
                    catch (InvalidOperationException exception) when (exception.Message.Contains("legacy AT-SPI boundary form", StringComparison.Ordinal))
                    { rejected = true; Observations.Add(new { unsupportedLegacyBoundary = boundary, protocolError = exception.Message }); }
                    Require(rejected, "An unsupported legacy END boundary returned a fabricated START range.");
                }
                var modern = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetStringAtOffset", "iu", "0", "1")).GetProperty("data");
                var start = External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetTextAtOffset", "iu", "0", "1")).GetProperty("data");
                Require(modern[0].GetString()!.Length > 0 && modern[0].GetString() == start[0].GetString() &&
                    modern[1].GetInt32() == 0 && start[1].GetInt32() == 0 && modern[2].GetInt32() == start[2].GetInt32(),
                    "Rejecting unsupported END forms broke the genuine modern/start word range.");
            });
            phase = "external-edit-readonly-and-source-recovery";
            run("atspi-text-external-edit-binding-readonly-disabled-recovery", () =>
            {
                Require(External(() => protocol.Interfaces(editor)).Contains("org.a11y.atspi.EditableText"), "The actual writable WPF Value provider was not exported.");
                External(() => protocol.Call(editor, "org.a11y.atspi.EditableText", "SetTextContents", "s", "wire")); drain();
                Require(box.Text == "wire" && model.Value == "wire" && BindingOperations.IsDataBound(box, TextBox.TextProperty), "Wire editing changed a shadow value or removed the binding.");
                box.IsReadOnly = true;
                Require(!External(() => protocol.Call(editor, "org.a11y.atspi.EditableText", "SetTextContents", "s", "blocked")).GetProperty("data")[0].GetBoolean(), "Read-only editing was reported successful.");
                Require(box.Text == "wire" && model.Value == "wire", "Read-only wire editing changed actual data.");
                box.IsReadOnly = false; box.IsEnabled = false;
                Require(!External(() => protocol.Call(editor, "org.a11y.atspi.EditableText", "SetTextContents", "s", "blocked")).GetProperty("data")[0].GetBoolean(), "Disabled editing was reported successful.");
                box.IsEnabled = true; model.Value = "source-again"; drain();
                Require(External(() => protocol.Call(editor, "org.a11y.atspi.Text", "GetText", "ii", "0", "-1")).GetProperty("data")[0].GetString() == "source-again", "Wire text stopped following the real source binding.");
            });
            if (includeRichText)
            {
            phase = "external-real-RichTextBox-document-provider";
            run("atspi-text-external-real-RichTextBox-document-and-selection", () =>
            {
                var rich = new RichTextBox { Width = 230, Height = 90, FontFamily = box.FontFamily,
                    Document = new WpfFork::System.Windows.Documents.FlowDocument(new WpfFork::System.Windows.Documents.Paragraph(new WpfFork::System.Windows.Documents.Run("abc\U0001F600Z"))) };
                WpfFork::System.Windows.Automation.AutomationProperties.SetName(rich, "Sequence notes document");
                var richWindow = new Window { Title = "WPF AT-SPI actual RichTextBox", Width = 290, Height = 160, Content = rich };
                try
                {
                    richWindow.Show(); drain();
                    wait(() => rich.IsLoaded && rich.ActualWidth > 0 && rich.ActualHeight > 0);
                    string richWindowTitle = richWindow.Title;
                    Node root = External(() => protocol.FindWindow(richWindowTitle));
                    Node document = External(() => protocol.Children(root).Single(node => protocol.Name(node) == "Sequence notes document"));
                    var peer = UIElementAutomationPeer.CreatePeerForElement(rich)!;
                    var provider = (System.Windows.Automation.Provider.ITextProvider)peer.GetPattern(PatternInterface.Text)!;
                    string actualDocument = provider.DocumentRange.GetText(-1);
                    Require(peer.GetPattern(PatternInterface.Value) is null && External(() => protocol.Role(document)) == 82,
                        "The actual RichTextBox was replaced by an entry/value facade.");
                    Require(External(() => protocol.Interfaces(document)).Contains("org.a11y.atspi.Text") &&
                        External(() => protocol.Call(document, "org.a11y.atspi.Text", "GetText", "ii", "0", "-1")).GetProperty("data")[0].GetString() == actualDocument,
                        "Wire document text differs from the actual RichTextBox Text provider.");
                    rich.CaretPosition = rich.Document.ContentStart; drain();
                    Require(External(() => protocol.IntProperty(document, "org.a11y.atspi.Text", "CaretOffset")) == 0,
                        "RichTextBox document start did not expose a collapsed zero caret.");
                    rich.SelectAll(); drain();
                    var selected = External(() => protocol.Call(document, "org.a11y.atspi.Text", "GetSelection", "i", "0")).GetProperty("data");
                    int count = actualDocument.EnumerateRunes().Count();
                    Require(selected[0].GetInt32() == 0 && selected[1].GetInt32() == count,
                        "RichTextBox SelectAll did not expose its complete real document selection.");
                }
                finally { richWindow.Close(); drain(); }
            });
            }
        }
        finally
        {
            window.Close(); drain();
            string? output = Environment.GetEnvironmentVariable("WPF_ATSPI_OBSERVATIONS");
            if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(new { protocol.Trace, Observations,
                finalPhase = phase, exported, futureChecksReached = exported, richTextSelected = includeRichText, richTextQualification = "pending native PTS / managed bottomless work",
                futureChecks = new[] { "collapsed-caret-forward-backward", "selection-grow-shrink-clear", "Unicode-scalar-range",
                    "native-selection-active-end-both-directions", "external-edit-readonly-and-source-recovery", "external-real-RichTextBox-document-provider" },
                windowDetached = PresentationSource.FromVisual(window) is null }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class TextModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value = "abcdef";
        public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class TextObserver : IDisposable
    {
        private readonly DBusConnection _connection;
        private readonly IDisposable _watch;
        internal ConcurrentQueue<(string member, string detail, int detail1, int detail2)> Signals { get; } = new();
        internal TextObserver(string address, string path)
        {
            _connection = new DBusConnection(new DBusConnectionOptions(address) { AutoConnect = false });
            try
            {
                _connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
                _watch = _connection.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Interface = "org.a11y.atspi.Event.Object", Path = path },
                    static (message, _) => { var reader = message.GetBodyReader(); return (member: message.MemberAsString!, detail: reader.ReadString(), detail1: reader.ReadInt32(), detail2: reader.ReadInt32()); },
                    notification => { if (notification.HasValue) Signals.Enqueue(notification.Value); }, emitOnCapturedContext: false).AsTask().GetAwaiter().GetResult();
                foreach (string kind in new[] { "object:text-caret-moved", "object:text-selection-changed", "object:text-changed" })
                {
                    using var writer = _connection.GetMessageWriter();
                    writer.WriteMethodCallHeader(destination: "org.a11y.atspi.Registry", path: "/org/a11y/atspi/registry", @interface: "org.a11y.atspi.Registry", member: "RegisterEvent", signature: "sass");
                    writer.WriteString(kind); writer.WriteArray(Array.Empty<string>()); writer.WriteString("");
                    _connection.CallMethodAsync(writer.CreateMessage()).GetAwaiter().GetResult();
                }
            }
            catch { _watch?.Dispose(); _connection.Dispose(); throw; }
        }
        public void Dispose() { _watch.Dispose(); _connection.Dispose(); }
    }

    private sealed class FocusObserver : IDisposable
    {
        private readonly DBusConnection _connection;
        private readonly IDisposable _watch;
        internal ConcurrentQueue<(string kind, int state)> Signals { get; } = new();
        internal FocusObserver(string address, string path)
        {
            _connection = new DBusConnection(new DBusConnectionOptions(address) { AutoConnect = false });
            try
            {
                _connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
                _watch = _connection.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Interface = "org.a11y.atspi.Event.Object", Member = "StateChanged", Path = path },
                    static (message, _) => { var reader = message.GetBodyReader(); return (kind: reader.ReadString(), state: reader.ReadInt32()); },
                    notification => { if (notification.HasValue) Signals.Enqueue(notification.Value); }, emitOnCapturedContext: false).AsTask().GetAwaiter().GetResult();
                using var writer = _connection.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: "org.a11y.atspi.Registry", path: "/org/a11y/atspi/registry", @interface: "org.a11y.atspi.Registry", member: "RegisterEvent", signature: "sass");
                writer.WriteString("object:state-changed:focused"); writer.WriteArray(Array.Empty<string>()); writer.WriteString("");
                _connection.CallMethodAsync(writer.CreateMessage()).GetAwaiter().GetResult();
            }
            catch { _watch?.Dispose(); _connection.Dispose(); throw; }
        }
        public void Dispose() { _watch.Dispose(); _connection.Dispose(); }
    }
    private sealed record Node(string Bus, string Path);
    private sealed class Protocol(string address)
    {
        internal List<object> Trace { get; } = new();
        internal JsonElement Call(Node node, string iface, string method, params string[] arguments)
        {
            string raw = Execute("busctl", ["--address=" + address, "--json=short", "--", "call", node.Bus, node.Path, iface, method, .. arguments]);
            Trace.Add(new { node, iface, method, raw });
            return JsonDocument.Parse(raw).RootElement.Clone();
        }
        internal List<Node> Children(Node node) => Call(node, Accessible, "GetChildren").GetProperty("data")[0].EnumerateArray()
            .Select(pair => new Node(pair[0].GetString()!, pair[1].GetString()!)).ToList();
        internal List<Node> Windows() => Children(new Node("org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root"))
            .SelectMany(Children).ToList();
        internal Node FindWindow(string name)
        {
            var timer = Stopwatch.StartNew();
            do { var found = Windows().FirstOrDefault(node => Name(node) == name); if (found is not null) return found; Thread.Sleep(40); }
            while (timer.Elapsed < TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("External AT-SPI tree did not contain actual WPF Window '" + name + "'.");
        }
        internal string Name(Node node)
        {
            string raw = Execute("busctl", "--address=" + address, "--json=short", "get-property", node.Bus, node.Path, Accessible, "Name");
            Trace.Add(new { node, property = "Name", raw });
            return JsonDocument.Parse(raw).RootElement.GetProperty("data").GetString()!;
        }
        internal string[] Interfaces(Node node) => Call(node, Accessible, "GetInterfaces").GetProperty("data")[0].EnumerateArray().Select(value => value.GetString()!).ToArray();
        internal int IntProperty(Node node, string iface, string property)
        {
            string raw = Execute("busctl", "--address=" + address, "--json=short", "get-property", node.Bus, node.Path, iface, property);
            Trace.Add(new { node, iface, property, raw });
            return JsonDocument.Parse(raw).RootElement.GetProperty("data").GetInt32();
        }
        internal uint Role(Node node) => Call(node, Accessible, "GetRole").GetProperty("data")[0].GetUInt32();
        internal bool State(Node node, int bit) => (Call(node, Accessible, "GetState").GetProperty("data")[0][bit / 32].GetUInt32() & (1u << (bit % 32))) != 0;
        internal int[] Extents(Node node) => Call(node, Component, "GetExtents", "u", "0").GetProperty("data")[0].EnumerateArray().Select(value => value.GetInt32()).ToArray();
    }
    private static string Execute(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(2500)) { process.Kill(true); throw new InvalidOperationException(executable + " protocol timeout."); }
        string result = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException(executable + ": " + error.GetAwaiter().GetResult());
        return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record CommandModel(ICommand Command);
    private sealed class BoundCommand : ICommand
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _enabled = true;
        internal int Count { get; private set; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => _enabled;
        public void Execute(object? parameter) { Require(_enabled && Environment.CurrentManagedThreadId == _thread, "Command executed disabled or outside its WPF dispatcher."); Count++; }
        internal void SetEnabled(bool enabled) { _enabled = enabled; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
