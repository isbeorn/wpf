extern alias WpfFork;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Platform;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using AutomationPeer = Avalonia.Automation.Peers.AutomationPeer;
using AutomationControlType = Avalonia.Automation.Peers.AutomationControlType;
using WpfPeer = WpfFork::System.Windows.Automation.Peers.AutomationPeer;
using WpfWindow = WpfFork::System.Windows.Window;
using WpfSource = WpfFork::System.Windows.PresentationSource;
using WpfInvoke = System.Windows.Automation.Provider.IInvokeProvider;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace Wpf.Portable.NativeHost;

// The single Control is only the existing X11 server's protocol attachment, not a rendered tree.
internal sealed class NativeAutomationRoot : Control, IDisposable
{
    private readonly WpfSource _source;
    private readonly IWindowImpl _window;
    private readonly Dictionary<WpfPeer, PeerFacade> _peers = new(ReferenceEqualityComparer.Instance);
    private readonly RootPeer _peer;
    private WpfWindow? _owner;
    private bool _disposed;

    internal NativeAutomationRoot(WpfSource source, IWindowImpl window)
    {
        _source = source;
        _window = window;
        _peer = new RootPeer(this);
        Focusable = false;
        IsHitTestVisible = false;
    }

    internal void Attach()
    {
        _source.VerifyAccess();
        if (_disposed || _owner is not null) return;
        _owner = _source.RootVisual as WpfWindow;
        if (_owner is null) return;
        _owner.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocusChanged), true);
        _owner.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocusChanged), true);
        if (_owner.IsLoaded) RefreshInitialChildren();
        else _owner.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnInitialLoaded), true);
    }

    private void OnInitialLoaded(object sender, RoutedEventArgs e) => RefreshInitialChildren();

    private void RefreshInitialChildren()
    {
        if (_owner is null) return;
        _owner.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnInitialLoaded));
        if (_disposed || _source.IsDisposed) return;
        // An early reader can cache an empty WPF peer and native node before the first template/layout.
        // Do not create a WPF peer when the optional accessibility service has not requested one.
        UIElementAutomationPeer.FromElement(_owner)?.ResetChildrenCache();
        _peer.NotifyInitialChildrenReady();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => _peer;

    private WpfPeer? WindowPeer
    {
        get
        {
            _source.VerifyAccess();
            if (_disposed || _source.IsDisposed || _owner is null) return null;
            var peer = UIElementAutomationPeer.CreatePeerForElement(_owner);
            if (peer is not null) _peers.TryAdd(peer, _peer);
            return peer;
        }
    }

    private PeerFacade Wrap(WpfPeer peer, AutomationPeer? parent = null)
    {
        if (peer is UIElementAutomationPeer element && ReferenceEquals(element.Owner, _owner)) return _peer;
        if (!_peers.TryGetValue(peer, out var facade))
            _peers.Add(peer, facade = new PeerFacade(this, peer, parent ?? _peer));
        if (parent is not null) facade.UpdateParent(parent);
        return facade;
    }

    private void OnFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => _peer.NotifyFocusChanged();

    public void Dispose()
    {
        _source.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        if (_owner is not null)
        {
            _owner.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnInitialLoaded));
            _owner.RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocusChanged));
            _owner.RemoveHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocusChanged));
        }
        _owner = null;
        _peer.ClearFocusSubscribers();
        _peers.Clear();
    }

    private class PeerFacade(NativeAutomationRoot root, WpfPeer? peer, AutomationPeer? parent) : AutomationPeer, IInvokeProvider
    {
        protected NativeAutomationRoot Root { get; } = root;
        private AutomationPeer? _parent = parent;
        protected virtual WpfPeer? Peer
        {
            get { Root._source.VerifyAccess(); return Root._disposed ? null : peer; }
        }
        protected override string? GetNameCore() => Peer?.GetName();
        protected override string GetClassNameCore() => Peer?.GetClassName() ?? string.Empty;
        protected override string? GetAutomationIdCore() => Peer?.GetAutomationId();
        protected override string? GetAcceleratorKeyCore() => Peer?.GetAcceleratorKey();
        protected override string? GetAccessKeyCore() => Peer?.GetAccessKey();
        protected override AutomationControlType GetAutomationControlTypeCore() => Peer?.GetAutomationControlType() switch
        {
            WpfFork::System.Windows.Automation.Peers.AutomationControlType.Window => AutomationControlType.Window,
            WpfFork::System.Windows.Automation.Peers.AutomationControlType.Button => AutomationControlType.Button,
            _ => AutomationControlType.Custom
        };
        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => Peer?.GetChildren()?
            .Select(child => (AutomationPeer)Root.Wrap(child, this)).ToArray() ?? Array.Empty<AutomationPeer>();
        internal void UpdateParent(AutomationPeer value) => _parent = value;
        protected override AutomationPeer? GetParentCore() => _parent;
        protected override AutomationPeer? GetLabeledByCore() => Peer?.GetLabeledBy() is { } label ? Root.Wrap(label) : null;
        protected override bool TrySetParent(AutomationPeer? value) { _parent = value; return true; }
        protected override bool IsEnabledCore() => Peer?.IsEnabled() == true;
        protected override bool IsKeyboardFocusableCore() => Peer?.IsKeyboardFocusable() == true;
        protected override bool HasKeyboardFocusCore() => Peer?.HasKeyboardFocus() == true;
        protected override bool IsControlElementCore() => Peer?.IsControlElement() == true;
        protected override bool IsContentElementCore() => Peer?.IsContentElement() == true;
        protected override bool IsOffscreenCore() => Root._owner?.IsVisible != true || Peer?.IsOffscreen() != false;
        protected override Rect GetBoundingRectangleCore()
        {
            var bounds = Peer?.GetBoundingRectangle();
            if (bounds is null || bounds.Value.IsEmpty) return default;
            // WPF peers report physical screen coordinates. The AT-SPI root adds this native
            // client origin and scale again, so its facade contract is client DIPs.
            var origin = Root._window.PointToScreen(default);
            double scale = Root._window.RenderScaling;
            return new Rect((bounds.Value.X - origin.X) / scale, (bounds.Value.Y - origin.Y) / scale,
                bounds.Value.Width / scale, bounds.Value.Height / scale);
        }
        protected override void SetFocusCore() => (Peer ?? throw new InvalidOperationException("The WPF accessible window is detached.")).SetFocus();
        protected override void BringIntoViewCore()
        {
            if (Peer is UIElementAutomationPeer element && element.Owner is FrameworkElement owner) owner.BringIntoView();
        }
        protected override bool ShowContextMenuCore() => false;
        protected override object? GetProviderCore(Type providerType) => providerType == typeof(IInvokeProvider) &&
            Peer?.GetPattern(PatternInterface.Invoke) is WpfInvoke ? this : null;
        void IInvokeProvider.Invoke() => (Peer?.GetPattern(PatternInterface.Invoke) as WpfInvoke
            ?? throw new InvalidOperationException("This WPF peer does not expose Invoke.")).Invoke();
    }

    private sealed class RootPeer(NativeAutomationRoot root) : PeerFacade(root, null, null), IRootProvider
    {
        protected override WpfPeer? Peer => Root.WindowPeer;
        public ITopLevelImpl PlatformImpl => Root._window;
        public event EventHandler? FocusChanged;
        internal void NotifyFocusChanged() => FocusChanged?.Invoke(this, EventArgs.Empty);
        internal void NotifyInitialChildrenReady() => RaiseChildrenChangedEvent();
        internal void ClearFocusSubscribers() => FocusChanged = null;
        public AutomationPeer? GetFocus()
        {
            Root._source.VerifyAccess();
            return !Root._disposed && Keyboard.FocusedElement is UIElement element &&
                ReferenceEquals(WpfSource.FromVisual(element), Root._source) &&
                UIElementAutomationPeer.CreatePeerForElement(element) is { } focused ? Root.Wrap(focused) : null;
        }
        public AutomationPeer? GetPeerFromPoint(Point point)
        {
            Root._source.VerifyAccess();
            if (Root._disposed || Root._owner is null) return null;
            var element = Root._owner.InputHitTest(new WpfFork::System.Windows.Point(point.X, point.Y)) as UIElement;
            while (element is not null)
            {
                if (UIElementAutomationPeer.CreatePeerForElement(element) is { } hit) return Root.Wrap(hit);
                element = VisualTreeHelper.GetParent(element) as UIElement;
            }
            return null;
        }
        protected override object? GetProviderCore(Type providerType) => providerType == typeof(IRootProvider) ? this : base.GetProviderCore(providerType);
    }
}
