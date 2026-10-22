extern alias WpfFork;

using System.Runtime.CompilerServices;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Input;

namespace Wpf.Portable.NativeHost.Linux.Accessibility;

internal sealed class AtSpiServer : IDisposable
{
    private readonly X11Connection _connection;
    private readonly List<WindowRegistration> _windows = new();
    private readonly Dictionary<string, AtSpiNode> _nodes = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private GioBusConnection? _session, _bus;
    private IDisposable? _application, _cache;
    private (string Bus, string Path) _desktop = ("", AtSpiProtocol.Null);
    private int _nextNode, _applicationId;
    private volatile bool _disposed;
    internal Exception? ConnectionFailure { get; private set; }

    internal AtSpiServer(X11Connection connection)
    {
        _connection = connection;
        connection.VerifyOwner();
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            ConnectionFailure = new InvalidOperationException("No existing session bus was supplied for optional accessibility.");
            return;
        }
        _ = ConnectAsync();
    }

    internal IDisposable Attach(PresentationSource source, X11Window window)
    {
        Verify(); source.VerifyAccess();
        if (source.RootVisual is not Window owner) throw new ArgumentException("AT-SPI requires an actual WPF Window root.", nameof(source));
        var registration = new WindowRegistration(this, source, owner);
        _windows.Add(registration);
        if (_bus is not null)
        {
            registration.Root.Register();
            EmitChildren(AtSpiProtocol.Root, "add", _windows.Count - 1, Reference(registration.Root.Path));
        }
        return registration;
    }

    private async Task ConnectAsync()
    {
        try
        {
            _session = GioBusConnection.ConnectSession(_connection);
            Task<GioVariant> addressCall;
            using (var empty = GioVariant.Tuple()) addressCall = _session.CallAsync("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress", empty, "(s)", _stop.Token);
            string address;
            using (var addressReply = await addressCall.ConfigureAwait(false)) address = AtSpiValues.String(addressReply, 0);
            await Owner();
            if (_disposed) return;
            _bus = GioBusConnection.ConnectAddress(_connection, address);
            _bus.Closed += OnBusClosed;
            _application = _bus.RegisterObject(AtSpiProtocol.Root, AtSpiProtocol.Xml([AtSpiProtocol.Accessible, AtSpiProtocol.Application]), HandleApplication);
            _cache = _bus.RegisterObject(AtSpiProtocol.CachePath, AtSpiProtocol.Xml([AtSpiProtocol.Cache]), HandleCache);
            foreach (var window in _windows) window.Root.Register();
            Task<GioVariant> embedCall;
            using (var request = AtSpiValues.Tuple(Reference(AtSpiProtocol.Root)))
                embedCall = _bus.CallAsync(AtSpiProtocol.Registry, AtSpiProtocol.Root, "org.a11y.atspi.Socket", "Embed", request, "((so))", _stop.Token);
            string desktopBus, desktopPath;
            using (var embedded = await embedCall.ConfigureAwait(false))
            using (var reference = embedded.Child(0))
            { desktopBus = AtSpiValues.String(reference, 0); desktopPath = AtSpiValues.String(reference, 1); }
            await Owner();
            if (_disposed) return;
            _desktop = (desktopBus, desktopPath);
            for (int index = 0; index < _windows.Count; index++) EmitChildren(AtSpiProtocol.Root, "add", index, Reference(_windows[index].Root.Path));
        }
        catch (Exception error)
        {
            await Owner();
            if (!_disposed) { ConnectionFailure = error; CloseBus(); }
        }
    }

    private void OnBusClosed(Exception error) { if (!_disposed) { ConnectionFailure = error; CloseBus(); } }
    private void CloseBus()
    {
        foreach (var node in _nodes.Values) node.Unregister();
        _cache?.Dispose(); _cache = null;
        _application?.Dispose(); _application = null;
        if (_bus is not null) { _bus.Closed -= OnBusClosed; _bus.Dispose(); _bus = null; }
        _session?.Dispose(); _session = null;
    }

    internal X11Connection Connection => _connection;
    internal GioBusConnection? Bus => _bus;
    internal GioVariant Reference(string path) => AtSpiValues.Reference(path == AtSpiProtocol.Null ? "" : _bus?.UniqueName ?? "", path);
    internal string AllocatePath() => "/org/a11y/atspi/accessible/n" + (++_nextNode).ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal void AddNode(AtSpiNode node) => _nodes.Add(node.Path, node);
    internal void RemoveNode(AtSpiNode node) => _nodes.Remove(node.Path);
    internal int WindowIndex(WindowRegistration window) => _windows.IndexOf(window);
    internal void Verify() { _connection.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }

    private Task<GioVariant> HandleApplication(string iface, string method, GioVariant args)
    {
        Verify();
        if (iface == AtSpiProtocol.Properties)
            return Task.FromResult(Properties(method, args, ApplicationProperties, (name, property, value) =>
            {
                if (name != AtSpiProtocol.Application || property != "Id") throw new NotSupportedException("This application property is read-only.");
                _applicationId = value.ReadInt32();
            }));
        if (iface == AtSpiProtocol.Application)
            return Task.FromResult(method switch
            {
                "GetLocale" => AtSpiValues.Tuple(GioVariant.String(System.Globalization.CultureInfo.CurrentUICulture.Name)),
                "GetApplicationBusAddress" => AtSpiValues.Tuple(GioVariant.String("")),
                _ => throw new NotSupportedException("Unknown AT-SPI application method.")
            });
        if (iface != AtSpiProtocol.Accessible) throw new NotSupportedException("Unknown AT-SPI application interface.");
        return Task.FromResult(method switch
        {
            "GetChildren" => AtSpiValues.Tuple(AtSpiValues.Array("(so)", _windows.Select(w => Reference(w.Root.Path)))),
            "GetChildAtIndex" => AtSpiValues.Tuple(Reference(AtSpiValues.Int(args, 0) is int i && i >= 0 && i < _windows.Count ? _windows[i].Root.Path : AtSpiProtocol.Null)),
            "GetIndexInParent" => AtSpiValues.Tuple(GioVariant.Int32(-1)),
            "GetRole" => AtSpiValues.Tuple(GioVariant.UInt32(75)),
            "GetRoleName" or "GetLocalizedRoleName" => AtSpiValues.Tuple(GioVariant.String("application")),
            "GetState" => AtSpiValues.Tuple(AtSpiValues.Array("u", [GioVariant.UInt32(0), GioVariant.UInt32(0)])),
            "GetAttributes" => AtSpiValues.Tuple(GioVariant.Array("{ss}")),
            "GetRelationSet" => AtSpiValues.Tuple(GioVariant.Array("(ua(so))")),
            "GetApplication" => AtSpiValues.Tuple(Reference(AtSpiProtocol.Root)),
            "GetInterfaces" => AtSpiValues.Tuple(AtSpiValues.Strings([AtSpiProtocol.Accessible, AtSpiProtocol.Application])),
            _ => throw new NotSupportedException("Unknown AT-SPI accessible application method.")
        });
    }

    private Dictionary<string, GioVariant> ApplicationProperties(string iface) => iface switch
    {
        AtSpiProtocol.Accessible => new()
        {
            ["version"] = GioVariant.UInt32(1), ["Name"] = GioVariant.String(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "WPF application"),
            ["Description"] = GioVariant.String(""), ["Parent"] = AtSpiValues.Reference(_desktop.Bus, _desktop.Path),
            ["ChildCount"] = GioVariant.Int32(_windows.Count), ["Locale"] = GioVariant.String(System.Globalization.CultureInfo.CurrentUICulture.Name), ["AccessibleId"] = GioVariant.String("")
        },
        AtSpiProtocol.Application => new()
        {
            ["ToolkitName"] = GioVariant.String("WPF"), ["ToolkitVersion"] = GioVariant.String(typeof(Window).Assembly.GetName().Version?.ToString() ?? ""),
            ["Version"] = GioVariant.String(typeof(Window).Assembly.GetName().Version?.ToString() ?? ""), ["AtspiVersion"] = GioVariant.String("2.1"),
            ["InterfaceVersion"] = GioVariant.UInt32(1), ["Id"] = GioVariant.Int32(_applicationId)
        },
        _ => throw new NotSupportedException("Unknown application properties interface.")
    };

    internal static GioVariant Properties(string method, GioVariant args, Func<string, Dictionary<string, GioVariant>> get,
        Action<string, string, GioVariant>? set = null)
    {
        string iface = AtSpiValues.String(args, 0);
        if (method == "Set")
        {
            using var boxed = args.Child(2); using var value = boxed.Unbox();
            (set ?? throw new NotSupportedException("These properties are read-only."))(iface, AtSpiValues.String(args, 1), value);
            return GioVariant.Tuple();
        }
        var values = get(iface);
        if (method == "GetAll") return AtSpiValues.Tuple(AtSpiValues.Properties(values));
        try
        {
            if (method != "Get" || !values.TryGetValue(AtSpiValues.String(args, 1), out var value)) throw new NotSupportedException("Unknown D-Bus property.");
            return AtSpiValues.Tuple(GioVariant.Box(value));
        }
        finally { foreach (var value in values.Values) value.Dispose(); }
    }

    private Task<GioVariant> HandleCache(string iface, string method, GioVariant args)
    {
        Verify();
        if (iface == AtSpiProtocol.Properties) return Task.FromResult(Properties(method, args, name => name == AtSpiProtocol.Cache
            ? new() { ["version"] = GioVariant.UInt32(1) } : throw new NotSupportedException("Unknown cache properties.")));
        if (iface != AtSpiProtocol.Cache || method != "GetItems") throw new NotSupportedException("Unknown AT-SPI cache method.");
        foreach (var window in _windows.ToArray()) window.Root.MaterializeDescendants();
        return Task.FromResult(AtSpiValues.Tuple(AtSpiValues.Array("((so)(so)(so)iiassusau)",
            _nodes.Values.ToArray().Select(node => node.CacheItem()).Prepend(ApplicationCacheItem()))));
    }

    private GioVariant ApplicationCacheItem() => AtSpiValues.Tuple(Reference(AtSpiProtocol.Root), Reference(AtSpiProtocol.Root), AtSpiValues.Reference(_desktop.Bus, _desktop.Path),
        GioVariant.Int32(-1), GioVariant.Int32(_windows.Count), AtSpiValues.Strings([AtSpiProtocol.Accessible, AtSpiProtocol.Application]),
        GioVariant.String(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "WPF application"), GioVariant.UInt32(75), GioVariant.String(""),
        AtSpiValues.Array("u", [GioVariant.UInt32(0), GioVariant.UInt32(0)]));

    internal void CacheAdded(AtSpiNode node)
    {
        using var body = AtSpiValues.Tuple(node.CacheItem());
        Send(AtSpiProtocol.CachePath, AtSpiProtocol.Cache, "AddAccessible", body);
    }
    internal void CacheRemoved(AtSpiNode node)
    {
        using var body = AtSpiValues.Tuple(Reference(node.Path));
        Send(AtSpiProtocol.CachePath, AtSpiProtocol.Cache, "RemoveAccessible", body);
    }

    internal void Emit(string path, string member, GioVariant body)
    {
        using (body) Send(path, AtSpiProtocol.EventObject, member, body);
    }
    private void Send(string path, string iface, string member, GioVariant body)
    {
        var bus = _bus;
        if (_disposed || bus is null) return;
        if (bus.IsClosed) { OnBusClosed(new IOException("The accessibility bus closed before its queued notification.")); return; }
        try { bus.EmitSignal(null, path, iface, member, body); }
        catch (IOException error) when (bus.IsClosed) { OnBusClosed(error); }
    }
    internal void EmitChildren(string path, string operation, int index, GioVariant reference)
        => Emit(path, "ChildrenChanged", AtSpiValues.Tuple(GioVariant.String(operation), GioVariant.Int32(index), GioVariant.Int32(0), BoxTake(reference), GioVariant.Dictionary(new Dictionary<string, GioVariant>())));
    internal static GioVariant BoxTake(GioVariant value) { using (value) return GioVariant.Box(value); }

    public void Dispose()
    {
        _connection.VerifyOwner();
        if (_disposed) return;
        _disposed = true; _stop.Cancel();
        try { foreach (var window in _windows.ToArray()) window.Dispose(); }
        finally { CloseBus(); _stop.Dispose(); }
    }

    private OwnerAwaitable Owner() => new(this);
    private readonly struct OwnerAwaitable(AtSpiServer server) : INotifyCompletion
    {
        public OwnerAwaitable GetAwaiter() => this;
        public bool IsCompleted => server._disposed;
        public void OnCompleted(Action continuation)
        {
            try { server._connection.Post(continuation); }
            catch (ObjectDisposedException) when (server._disposed) { continuation(); }
        }
        public void GetResult() { if (!server._disposed) server._connection.VerifyOwner(); }
    }

    internal sealed class WindowRegistration : IDisposable
    {
        private readonly AtSpiServer _server;
        internal readonly PresentationSource Source;
        internal readonly Window Owner;
        internal readonly AtSpiNode Root;
        private readonly Dictionary<AutomationPeer, AtSpiNode> _peers = new(ReferenceEqualityComparer.Instance);
        internal bool Disposed { get; private set; }
        internal WindowRegistration(AtSpiServer server, PresentationSource source, Window owner)
        {
            _server = server; Source = source; Owner = owner;
            Root = new AtSpiNode(server, this, null, null);
            owner.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(Loaded), true);
            owner.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FocusChanged), true);
            owner.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FocusChanged), true);
        }
        internal AtSpiNode Wrap(AutomationPeer peer, AtSpiNode parent)
        {
            if (peer is UIElementAutomationPeer element && ReferenceEquals(element.Owner, Owner)) return Root;
            if (!_peers.TryGetValue(peer, out var node)) { node = new AtSpiNode(_server, this, peer, parent); _peers.Add(peer, node); node.Register(); }
            return node;
        }
        internal void Forget(AutomationPeer peer) => _peers.Remove(peer);
        private void Loaded(object sender, RoutedEventArgs args)
        {
            if (Disposed || Source.IsDisposed) return;
            UIElementAutomationPeer.FromElement(Owner)?.ResetChildrenCache();
            if (Root.WasRequested)
            {
                Root.RefreshChildren(); _server.CacheAdded(Root);
            }
        }
        private void FocusChanged(object sender, KeyboardFocusChangedEventArgs args)
        {
            foreach (var node in _peers.Values.Append(Root).ToArray()) if (node.WasRequested) node.NotifyFocus();
        }
        public void Dispose()
        {
            _server._connection.VerifyOwner(); if (Disposed) return; Disposed = true;
            Owner.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(Loaded));
            Owner.RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FocusChanged));
            Owner.RemoveHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(FocusChanged));
            foreach (var node in _peers.Values.ToArray()) node.Dispose(); Root.Dispose(); _peers.Clear();
            int index = _server._windows.IndexOf(this); _server._windows.Remove(this);
            _server.EmitChildren(AtSpiProtocol.Root, "remove", index, _server.Reference(Root.Path));
        }
    }
}
