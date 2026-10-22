extern alias WpfFork;

using System.Windows.Automation.Provider;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

namespace Wpf.Portable.NativeHost.Linux.Accessibility;

internal sealed class AtSpiNode : IDisposable
{
    private readonly AtSpiServer _server;
    internal readonly AtSpiServer.WindowRegistration Window;
    private AutomationPeer? _peer;
    private readonly IInputElement? _owner;
    private IDisposable? _registration;
    private AtSpiText? _text;
    private List<AtSpiNode> _children = new();
    private bool _disposed, _focused;
    internal bool WasRequested { get; private set; }
    internal string Path { get; }
    internal AtSpiNode? Parent { get; set; }

    internal AtSpiNode(AtSpiServer server, AtSpiServer.WindowRegistration window, AutomationPeer? peer, AtSpiNode? parent)
    {
        _server = server; Window = window; _peer = peer; Parent = parent; Path = server.AllocatePath(); server.AddNode(this);
        _owner = peer switch { UIElementAutomationPeer element => element.Owner, ContentElementAutomationPeer content => content.Owner, _ => null };
        if (_owner is not null) PresentationSource.AddSourceChangedHandler(_owner, SourceChanged);
    }
    internal AutomationPeer Peer
    {
        get
        {
            Verify(); WasRequested = true;
            return _peer ??= UIElementAutomationPeer.CreatePeerForElement(Window.Owner)
                ?? throw new InvalidOperationException("The actual WPF Window did not create an automation peer.");
        }
    }
    internal bool IsAvailable => !_disposed && !Window.Disposed && !Window.Source.IsDisposed
        && (_owner is not DependencyObject owner || ReferenceEquals(PresentationSource.FromDependencyObject(owner), Window.Source));
    private void Verify()
    {
        _server.Connection.VerifyOwner();
        if (!_disposed && !IsAvailable) Dispose();
        ObjectDisposedException.ThrowIf(_disposed || Window.Disposed || Window.Source.IsDisposed, this);
    }
    private void SourceChanged(object sender, SourceChangedEventArgs args)
    {
        if (!ReferenceEquals(args.NewSource, Window.Source)) Dispose();
    }
    private AtSpiText? Text
    {
        get
        {
            var peer = Peer;
            return _text ??= peer is UIElementAutomationPeer element && element.Owner is WpfFork::System.Windows.Controls.Primitives.TextBoxBase editor
                && peer.GetPattern(PatternInterface.Text) is ITextProvider provider ? new AtSpiText(this, editor, peer, provider) : null;
        }
    }
    private string[] Interfaces => new[] { AtSpiProtocol.Accessible, AtSpiProtocol.Component }
        .Concat(Peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider ? [AtSpiProtocol.Action] : Array.Empty<string>())
        .Concat(Text is not null ? [AtSpiProtocol.Text] : Array.Empty<string>())
        .Concat(Text?.CanSetValue == true ? [AtSpiProtocol.EditableText] : Array.Empty<string>()).ToArray();
    internal void Register()
    {
        if (_registration is not null || _server.Bus is null || _disposed) return;
        // A Window has fixed interfaces; registering it before Show must not request a peer.
        var interfaces = _peer is null ? new[] { AtSpiProtocol.Accessible, AtSpiProtocol.Component } : Interfaces;
        _registration = _server.Bus.RegisterObject(Path, AtSpiProtocol.Xml(interfaces), Handle);
    }
    internal void Unregister() { _registration?.Dispose(); _registration = null; }
    private Task<GioVariant> Handle(string iface, string method, GioVariant args)
    {
        Verify();
        return Task.FromResult(iface switch
        {
            AtSpiProtocol.Properties => AtSpiServer.Properties(method, args, Properties),
            AtSpiProtocol.Accessible => Accessible(method, args),
            AtSpiProtocol.Component => Component(method, args),
            AtSpiProtocol.Action => Action(method, args),
            AtSpiProtocol.Text => (Text ?? throw new NotSupportedException("This real WPF peer has no text provider.")).Call(method, args),
            AtSpiProtocol.EditableText => (Text ?? throw new NotSupportedException("This real WPF peer has no editor.")).Edit(method, args),
            _ => throw new NotSupportedException("Unknown AT-SPI interface.")
        });
    }
    internal IReadOnlyList<AtSpiNode> RefreshChildren()
    {
        var next = Peer.GetChildren()?.Select(peer => Window.Wrap(peer, this)).ToList() ?? new();
        var previous = _children; _children = next;
        foreach (var child in next) child.Parent = this;
        foreach (var removed in previous.Except(next).ToArray())
        {
            _server.EmitChildren(Path, "remove", previous.IndexOf(removed), _server.Reference(removed.Path)); removed.Dispose();
        }
        foreach (var added in next.Except(previous))
        {
            _server.CacheAdded(added); _server.EmitChildren(Path, "add", next.IndexOf(added), _server.Reference(added.Path));
        }
        return next;
    }
    internal void MaterializeDescendants() { foreach (var child in RefreshChildren()) child.MaterializeDescendants(); }
    private string Name => Peer.GetName() ?? "";
    private string Description => Peer.GetHelpText() ?? "";
    private string ParentPath => Parent?.Path ?? AtSpiProtocol.Root;
    private int Index => Parent is null ? _server.WindowIndex(Window) : Parent.RefreshChildren().ToList().IndexOf(this);
    private uint Role => Peer.GetAutomationControlType() switch
    {
        AutomationControlType.Window => 23, AutomationControlType.Button => 43, AutomationControlType.Edit => 79,
        AutomationControlType.Document => 82, AutomationControlType.Text => 29, AutomationControlType.CheckBox => 7,
        AutomationControlType.ComboBox => 11, AutomationControlType.Tab => 38, AutomationControlType.TabItem => 37,
        AutomationControlType.List => 31, AutomationControlType.ListItem => 32, AutomationControlType.ScrollBar => 48,
        AutomationControlType.Hyperlink => 88, AutomationControlType.Menu => 33, AutomationControlType.MenuItem => 35,
        AutomationControlType.Tree => 65, AutomationControlType.TreeItem => 91, AutomationControlType.Group => 39,
        _ => 67
    };
    private string RoleName => Role switch { 23 => "frame", 43 => "push button", 79 => "entry", 82 => "document", 29 => "label", _ => Peer.GetLocalizedControlType() ?? "unknown" };
    private GioVariant State()
    {
        ulong mask = 0;
        void Set(int bit, bool yes) { if (yes) mask |= 1UL << bit; }
        Set(7, Text?.CanSetValue == true && Text.IsReadOnly == false); Set(8, Peer.IsEnabled()); Set(11, Peer.IsKeyboardFocusable());
        Set(12, Peer.HasKeyboardFocus()); Set(24, Peer.IsEnabled()); Set(25, !Peer.IsOffscreen() && Window.Owner.IsVisible);
        Set(30, !Peer.IsOffscreen() && Window.Owner.IsVisible); Set(43, Text?.IsReadOnly == true);
        Set(26, Text?.IsMultiline == false); Set(17, Text?.IsMultiline == true); Set(38, Text is not null);
        Set(1, Parent is null && Window.Owner.IsActive);
        return AtSpiValues.Array("u", [GioVariant.UInt32((uint)mask), GioVariant.UInt32((uint)(mask >> 32))]);
    }
    private Dictionary<string, GioVariant> Properties(string iface) => iface switch
    {
        AtSpiProtocol.Accessible => new()
        {
            ["version"] = GioVariant.UInt32(1), ["Name"] = GioVariant.String(Name), ["Description"] = GioVariant.String(Description),
            ["Parent"] = _server.Reference(ParentPath), ["ChildCount"] = GioVariant.Int32(RefreshChildren().Count),
            ["Locale"] = GioVariant.String(System.Globalization.CultureInfo.CurrentUICulture.Name), ["AccessibleId"] = GioVariant.String(Peer.GetAutomationId() ?? "")
        },
        AtSpiProtocol.Component => new() { ["version"] = GioVariant.UInt32(1) },
        AtSpiProtocol.Action when Peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider => new() { ["version"] = GioVariant.UInt32(1), ["NActions"] = GioVariant.Int32(1) },
        AtSpiProtocol.Text when Text is not null => new() { ["version"] = GioVariant.UInt32(1), ["CharacterCount"] = GioVariant.Int32(Text.CharacterCount), ["CaretOffset"] = GioVariant.Int32(Text.Caret) },
        AtSpiProtocol.EditableText when Text?.CanSetValue == true => new() { ["version"] = GioVariant.UInt32(1) },
        _ => throw new NotSupportedException("This actual peer does not supply the requested properties interface.")
    };
    private GioVariant Accessible(string method, GioVariant args) => method switch
    {
        "GetChildren" => AtSpiValues.Tuple(AtSpiValues.Array("(so)", RefreshChildren().Select(node => _server.Reference(node.Path)))),
        "GetChildAtIndex" => AtSpiValues.Tuple(_server.Reference(AtSpiValues.Int(args, 0) is int i && i >= 0 && i < RefreshChildren().Count ? _children[i].Path : AtSpiProtocol.Null)),
        "GetIndexInParent" => AtSpiValues.Tuple(GioVariant.Int32(Index)), "GetRole" => AtSpiValues.Tuple(GioVariant.UInt32(Role)),
        "GetRoleName" or "GetLocalizedRoleName" => AtSpiValues.Tuple(GioVariant.String(RoleName)), "GetState" => AtSpiValues.Tuple(State()),
        "GetAttributes" => AtSpiValues.Tuple(GioVariant.Array("{ss}")), "GetRelationSet" => AtSpiValues.Tuple(Relations()),
        "GetApplication" => AtSpiValues.Tuple(_server.Reference(AtSpiProtocol.Root)), "GetInterfaces" => AtSpiValues.Tuple(AtSpiValues.Strings(Interfaces)),
        _ => throw new NotSupportedException("Unknown accessible method.")
    };
    private GioVariant Relations()
    {
        var label = Peer.GetLabeledBy();
        return label is null ? GioVariant.Array("(ua(so))") : AtSpiValues.Array("(ua(so))",
            [AtSpiValues.Tuple(GioVariant.UInt32(2), AtSpiValues.Array("(so)", [_server.Reference(Window.Wrap(label, Parent ?? this).Path)]))]);
    }
    private Rect Bounds(uint coordinates)
    {
        Rect bounds = Peer.GetBoundingRectangle(); if (bounds.IsEmpty) return Rect.Empty;
        Point origin = Origin(coordinates); bounds.Offset(-origin.X, -origin.Y); return bounds;
    }
    internal Point Origin(uint coordinates) => coordinates switch
    {
        0 => new Point(),
        1 => Window.Owner.PointToScreen(new Point()),
        2 => Parent is null ? Window.Owner.PointToScreen(new Point()) : Parent.Peer.GetBoundingRectangle().TopLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(coordinates))
    };
    internal static GioVariant RectValues(Rect rect, bool structure)
    {
        var values = rect.IsEmpty ? new int[4] : new[] { checked((int)Math.Floor(rect.X)), checked((int)Math.Floor(rect.Y)), checked((int)Math.Ceiling(rect.Width)), checked((int)Math.Ceiling(rect.Height)) };
        var tuple = AtSpiValues.Tuple(values.Select(GioVariant.Int32).ToArray()); return structure ? AtSpiValues.Tuple(tuple) : tuple;
    }
    private GioVariant Component(string method, GioVariant args)
    {
        switch (method)
        {
            case "GetExtents": return RectValues(Bounds(AtSpiValues.UInt(args, 0)), true);
            case "GetPosition": { var rect = Bounds(AtSpiValues.UInt(args, 0)); return AtSpiValues.Tuple(GioVariant.Int32(rect.IsEmpty ? 0 : (int)rect.X), GioVariant.Int32(rect.IsEmpty ? 0 : (int)rect.Y)); }
            case "GetSize": { var rect = Bounds(0); return AtSpiValues.Tuple(GioVariant.Int32(rect.IsEmpty ? 0 : (int)rect.Width), GioVariant.Int32(rect.IsEmpty ? 0 : (int)rect.Height)); }
            case "GetLayer": return AtSpiValues.Tuple(GioVariant.UInt32(Parent is null ? 7u : 3u));
            case "GetAlpha": return AtSpiValues.Tuple(GioVariant.Double(1));
            case "GrabFocus": Peer.SetFocus(); return AtSpiValues.Tuple(GioVariant.Bool(Peer.HasKeyboardFocus()));
            case "Contains":
            case "GetAccessibleAtPoint":
            {
                Point point = new(AtSpiValues.Int(args, 0), AtSpiValues.Int(args, 1)); Point origin = Origin(AtSpiValues.UInt(args, 2)); point.Offset(origin.X, origin.Y);
                bool contains = Peer.GetBoundingRectangle().Contains(point);
                if (method == "Contains") return AtSpiValues.Tuple(GioVariant.Bool(contains));
                if (!contains) return AtSpiValues.Tuple(_server.Reference(AtSpiProtocol.Null));
                var element = Window.Owner.InputHitTest(Window.Owner.PointFromScreen(point)) as UIElement;
                while (element is not null)
                {
                    if (UIElementAutomationPeer.CreatePeerForElement(element) is { } hit) return AtSpiValues.Tuple(_server.Reference(Window.Wrap(hit, this).Path));
                    element = VisualTreeHelper.GetParent(element) as UIElement;
                }
                return AtSpiValues.Tuple(_server.Reference(Path));
            }
            case "SetPosition": case "SetSize": case "SetExtents": return AtSpiValues.Tuple(GioVariant.Bool(false));
            default: throw new NotSupportedException("Unknown component method.");
        }
    }
    private GioVariant Action(string method, GioVariant args)
    {
        if (Peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new NotSupportedException("This actual peer does not support Invoke.");
        if (method == "GetNActions") return AtSpiValues.Tuple(GioVariant.Int32(1));
        if (AtSpiValues.Int(args, 0) != 0) throw new ArgumentOutOfRangeException(nameof(args));
        return method switch
        {
            "GetName" or "GetLocalizedName" => AtSpiValues.Tuple(GioVariant.String("click")),
            "GetDescription" => AtSpiValues.Tuple(GioVariant.String(Description)), "GetKeyBinding" => AtSpiValues.Tuple(GioVariant.String(Peer.GetAccessKey() ?? "")),
            "DoAction" => Invoke(invoke), _ => throw new NotSupportedException("Unknown action method.")
        };
    }
    private static GioVariant Invoke(IInvokeProvider invoke) { invoke.Invoke(); return AtSpiValues.Tuple(GioVariant.Bool(true)); }
    internal void NotifyFocus()
    {
        if (_disposed || !WasRequested) return;
        if (!IsAvailable) { Dispose(); return; }
        bool focused = Peer.HasKeyboardFocus(); if (_focused == focused) return; _focused = focused;
        Notify("StateChanged", "focused", focused ? 1 : 0, 0, GioVariant.Int32(0));
    }
    internal void Notify(string member, string detail, int first, int second, GioVariant value)
        => _server.Emit(Path, member, AtSpiValues.Tuple(GioVariant.String(detail), GioVariant.Int32(first), GioVariant.Int32(second), AtSpiServer.BoxTake(value), GioVariant.Dictionary(new Dictionary<string, GioVariant>())));
    internal GioVariant CacheItem() => AtSpiValues.Tuple(_server.Reference(Path), _server.Reference(AtSpiProtocol.Root), _server.Reference(ParentPath),
        GioVariant.Int32(Index), GioVariant.Int32(RefreshChildren().Count), AtSpiValues.Strings(Interfaces), GioVariant.String(Name), GioVariant.UInt32(Role), GioVariant.String(Description), State());
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_owner is not null) PresentationSource.RemoveSourceChangedHandler(_owner, SourceChanged);
        try
        {
            foreach (var child in _children.ToArray()) child.Dispose();
            if (Parent is { _disposed: false } parent)
            {
                parent._peer?.ResetChildrenCache();
                int index = parent._children.IndexOf(this);
                if (index >= 0) { parent._children.RemoveAt(index); _server.EmitChildren(parent.Path, "remove", index, _server.Reference(Path)); }
            }
            _server.CacheRemoved(this);
        }
        finally
        {
            _children.Clear();
            _text?.Dispose(); _text = null;
            Unregister(); _server.RemoveNode(this);
            if (_peer is not null) Window.Forget(_peer);
        }
    }
}