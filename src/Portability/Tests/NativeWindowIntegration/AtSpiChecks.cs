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
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
                Observations.Add(new { focusSignals = observer.Signals.Select(signal => new { signal.kind, signal.state }).ToArray(), source = "separate real accessibility-bus connection registered as an event listener" });
            });
            run("atspi-external-physical-bounds-dpi-and-movement", () =>
            {
                foreach (int dpi in new[] { 96, 192, 96 })
                {
                    native.SetDpi(dpi); drain();
                    var xid = native.FindWindow(title);
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var before = native.ClientOrigin(xid);
                        window.Left += direction * 23; window.Top += direction * 11; drain();
                        var origin = native.ClientOrigin(xid);
                        var size = native.Size(xid);
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
        }
        finally
        {
            window.Close();
            string? output = Environment.GetEnvironmentVariable("WPF_ATSPI_OBSERVATIONS");
            if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(new { protocol.Trace, Observations }, new JsonSerializerOptions { WriteIndented = true }));
        }
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
            string raw = Execute("busctl", ["--address=" + address, "--json=short", "call", node.Bus, node.Path, iface, method, .. arguments]);
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
