extern alias WpfFork;

using System.Runtime.ExceptionServices;
using Wpf.Portable.NativeHost.Linux;
using Wpf.Portable.NativeHost.Linux.Accessibility;
using WpfFork::MS.Internal.Portability.Input;
using WpfFork::MS.Internal.Portability.Windowing;
using WpfFork::System.Windows.Input;
using WpfPoint = WpfFork::System.Windows.Point;
using WpfSize = WpfFork::System.Windows.Size;
using WpfRect = WpfFork::System.Windows.Rect;
using WpfDpi = WpfFork::System.Windows.DpiScale;
using WpfSource = WpfFork::System.Windows.PresentationSource;
using WpfState = WpfFork::System.Windows.WindowState;
using WpfStyle = WpfFork::System.Windows.WindowStyle;
using WpfKey = WpfFork::System.Windows.Input.Key;
using WpfCursor = WpfFork::System.Windows.Input.Cursor;
using KeyStates = WpfFork::System.Windows.Input.KeyStates;

namespace Wpf.Portable.NativeHost;

internal sealed class NativeWindowFactory(X11Connection connection, X11Screens screens, PortalFileDialogs dialogs, AtSpiServer automation) : IPortableWindowFactory, IDisposable
{
    private readonly Dictionary<X11Window, NativeWindowPeer> _windows = new();

    public IPortableWindowPeer CreateWindow(WpfSource source, PortableWindowOptions options)
    {
        source.VerifyAccess();
        var window = new X11Window(connection, new X11WindowOptions { Transparent = options.AllowsTransparency });
        try
        {
            var peer = new NativeWindowPeer(window, source, _windows, options, dialogs, automation);
            _windows.Add(window, peer);
            return peer;
        }
        catch { window.Dispose(); throw; }
    }
    public IPortablePopupPeer CreatePopup(WpfSource source, WpfSource ownerSource, bool transparent)
    {
        source.VerifyAccess();
        var owner = _windows.Values.SingleOrDefault(peer => peer.Source == ownerSource)
            ?? throw new InvalidOperationException("The Popup placement source has no live native peer.");
        var window = owner.CreateNativePopup(transparent);
        try
        {
            var peer = new NativeWindowPeer(window, source, _windows,
                new PortableWindowOptions { Owner = owner, IsEnabled = true, ShowInTaskbar = false }, dialogs, automation, owner);
            _windows.Add(window, peer);
            return peer;
        }
        catch { window.Dispose(); throw; }
    }
    public WpfRect GetPopupScreenBounds(WpfSource owner, WpfRect targetBounds, WpfPoint point, bool useWorkArea)
    {
        owner.VerifyAccess();
        if (!_windows.Values.Any(peer => peer.Source == owner)) throw new InvalidOperationException("The Popup owner source is detached.");
        var target = new X11PixelRect(checked((int)Math.Floor(targetBounds.X)), checked((int)Math.Floor(targetBounds.Y)),
            checked((int)Math.Ceiling(targetBounds.Width)), checked((int)Math.Ceiling(targetBounds.Height)));
        var screen = screens.ReadForRect(target);
        var area = screen.WorkingArea;
        var bounds = useWorkArea && area.Contains(checked((int)point.X), checked((int)point.Y)) ? area : screen.Bounds;
        return new WpfRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
    public void Dispose()
    {
        connection.VerifyOwner();
        ExceptionDispatchInfo? failure = null;
        foreach (var pair in _windows.ToArray())
        {
            if (!_windows.ContainsKey(pair.Key)) continue;
            var peer = pair.Value;
            try { peer.Close(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { peer.Dispose(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        failure?.Throw();
    }
}

internal sealed class NativeWindowPeer : IPortableWindowPeer, IPortablePopupPeer, IPortableInputHost, IPortableFileDialogHost
{
    private readonly NativeWindowPeer? _popupOwner;
    private NativeWindowPeer? _keyboardRecipient;
    internal bool PopupComposited => _window.IsComposited;
    internal WpfSource Source => _source;
    public event Action? OwnerDeactivated;
    public event Action? OwnerClosing;
    private readonly X11Window _window;
    private readonly X11Input _input;
    private readonly PortalFileDialogs _dialogs;
    private readonly AtSpiServer _automationServer;
    private IDisposable? _automation;
    private readonly WpfSource _source;
    private readonly Dictionary<X11Window, NativeWindowPeer> _windows;
    private IReadOnlyDictionary<WpfKey, KeyStates> _keySnapshot = new Dictionary<WpfKey, KeyStates>();
    private bool _disposed, _closing, _closed, _isActive, _mouseActive, _keyboardActive, _captured;
    private bool _constructing = true;
    private WpfState _metricsState, _requestedState;
    private bool _metricsRefreshQueued, _stateChangedPending, _stateRequestPending;
    private WpfSize _clientSize, _outerSize;
    private WpfPoint _desktopPosition, _clientScreenOrigin;
    private WpfDpi _dpiScale;
    private CancellationTokenSource? _fileDialogCancellation;

    internal NativeWindowPeer(X11Window window, WpfSource source,
        Dictionary<X11Window, NativeWindowPeer> windows, PortableWindowOptions options, PortalFileDialogs dialogs, AtSpiServer automation, NativeWindowPeer? popupOwner = null)
    {
        _popupOwner = popupOwner;
        _window = window;
        _source = source;
        _windows = windows;
        _dialogs = dialogs;
        _automationServer = automation;
        _input = new X11Input(window);
        try
        {
            window.CloseRequested += OnCloseRequested;
            window.Closed += OnNativeClosed;
            window.Exposed += OnExposed;
            window.MetricsChanged += OnNativeMetricsChanged;
            window.FocusChanged += OnFocus;
            _input.KeyInput += OnKeyInput;
            _input.TextInput += OnTextInput;
            _input.PointerInput += OnPointerInput;
            _input.CaptureLost += OnCaptureLost;
            if (_popupOwner is null)
            {
                SetTitle(options.Title);
                SetWindowStyle(options.WindowStyle);
                SetTopmost(options.Topmost);
            }
            if (options.DesktopPosition is { } position) SetDesktopPosition(position);
            SetOwner(options.Owner);
            SetShowInTaskbar(options.ShowInTaskbar);
            SetEnabled(options.IsEnabled);
            if (_popupOwner is null) SetWindowState(options.WindowState);
            if (options.ClientSize is { } size) SetClientSize(size);
            RefreshMetrics();
            _constructing = false;
        }
        catch { _input.Dispose(); throw; }
    }
    private void OnCloseRequested() { if (!_closing && !_closed) CloseRequested?.Invoke(); }
    private void OnExposed() { if (CanNotify) Exposed?.Invoke(); }
    private void OnNativeMetricsChanged()
    {
        if (CanNotify && IsVisible) _stateChangedPending = true;
        QueueMetricsRefresh();
    }

    private bool CanNotify => !_constructing && !_disposed && !_closed && !_source.IsDisposed;
    public WpfSize ClientSize => _clientSize;
    public WpfSize OuterSize => _outerSize;
    public WpfDpi DpiScale => _dpiScale;
    public WpfPoint DesktopPosition => _desktopPosition;
    public bool IsVisible { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsActive => _isActive;
    public WpfState WindowState => _metricsState;
    public event Action? MetricsChanged;
    public event Action? Exposed;
    public event Action? Closed;
    public event Action<InputReport>? RawInput;
    public event Action? CloseRequested;
    public event Action<bool>? ActivationChanged;
    public event Action<WpfState>? StateChanged;

    public async Task<PortableFileDialogResult> ShowFileDialogAsync(PortableFileDialogOptions options, CancellationToken cancellationToken)
    {
        Verify();
        if (_closed || !IsVisible) throw new InvalidOperationException("A file chooser requires a live visible native owner.");
        if (_fileDialogCancellation is not null) throw new InvalidOperationException("This native owner already has a pending file chooser.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _fileDialogCancellation = cancellation;
        try
        {
            var result = await _dialogs.ShowAsync(_window.Handle, options, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            Verify();
            return result;
        }
        finally { _fileDialogCancellation = null; }
    }

    public void Present(ReadOnlySpan<byte> premultipliedBgra, int pixelWidth, int pixelHeight, int stride)
    {
        Verify();
        if (_closed || _source.IsDisposed || _source.RootVisual is null)
            throw new InvalidOperationException("A detached or closed native peer cannot accept a frame.");
        _window.Present(premultipliedBgra, pixelWidth, pixelHeight, stride);
    }

    public void SetTitle(string title) { Verify(); _window.SetTitle(title); }
    public void SetShowInTaskbar(bool show) { Verify(); _window.SetShowInTaskbar(show); }
    public void SetWindowStyle(WpfStyle style)
    {
        Verify();
        _window.SetDecorations(style switch
        {
            WpfStyle.None => X11WindowDecorations.None,
            WpfStyle.SingleBorderWindow => X11WindowDecorations.Full,
            _ => throw new PlatformNotSupportedException("The native Window style is unsupported.")
        });
        QueueMetricsRefresh();
    }
    public void SetTopmost(bool topmost) { Verify(); _window.SetTopmost(topmost); }
    public void SetDesktopPosition(WpfPoint position)
    {
        Verify();
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        _window.Move(checked((int)Math.Round(position.X)), checked((int)Math.Round(position.Y)));
        QueueMetricsRefresh();
    }
    public void SetClientSize(WpfSize size)
    {
        Verify();
        _window.Resize(ToPixels(size.Width), ToPixels(size.Height));
        QueueMetricsRefresh();
    }
    public void SetClientSizeConstraints(WpfSize minimum, WpfSize maximum)
    { Verify(); _window.SetMinMaxSize(ToMinimumPixels(minimum.Width), ToMinimumPixels(minimum.Height), ToMaximumPixels(maximum.Width), ToMaximumPixels(maximum.Height)); }
    public void SetOwner(IPortableWindowPeer owner)
    {
        Verify();
        if (owner is not null && owner is not NativeWindowPeer) throw new ArgumentException("The owner uses a different native backend.", nameof(owner));
        _window.SetOwner((owner as NativeWindowPeer)?._window);
    }
    public void SetEnabled(bool enabled)
    {
        Verify();
        bool deactivate = CanNotify && IsEnabled && !enabled;
        if (IsEnabled != enabled)
        {
            _input.ResetComposition();
            var keyboardOwner = KeyboardOwner;
            if (keyboardOwner != this && keyboardOwner.CanNotify && keyboardOwner._keyboardRecipient == this)
                keyboardOwner._input.ResetComposition();
        }
        if (!enabled && _captured) ReleaseMouseCapture();
        _window.SetEnabled(enabled);
        IsEnabled = enabled;
        if (deactivate)
        {
            ReportMouse(RawMouseActions.CancelCapture);
            if (!CanNotify || IsEnabled) return;
            if (_mouseActive) ReportMouse(RawMouseActions.Deactivate);
            if (!CanNotify || IsEnabled) return;
            if (_keyboardActive) ReportKey(RawKeyboardActions.Deactivate, Environment.TickCount, 0);
            _mouseActive = false;
            _keyboardActive = false;
        }
    }
    public void SetWindowState(WpfState state)
    {
        Verify();
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        _stateRequestPending = _stateRequestPending || state != _metricsState;
        _requestedState = state;
        if (IsVisible) ApplyWindowState();
    }
    private void ApplyWindowState() => _window.RequestState(_requestedState switch
    { WpfState.Normal => X11WindowState.Normal, WpfState.Minimized => X11WindowState.Minimized, WpfState.Maximized => X11WindowState.Maximized,
      _ => throw new ArgumentOutOfRangeException(nameof(_requestedState)) });
    public void Show(bool activate)
    {
        Verify();
        if (!IsVisible) _input.ResetComposition();
        if (_popupOwner is null) _automation ??= _automationServer.Attach(_source, _window);
        if (_popupOwner == null) ApplyWindowState();
        _window.Show(activate);
        IsVisible = true;
        if (activate && _requestedState != WpfState.Minimized) Activate();
    }
    public void Hide()
    {
        Verify();
        if (IsVisible) _input.ResetComposition();
        if (!_stateRequestPending) _requestedState = ToWpf(_window.ReadMetrics().State);
        _stateRequestPending |= _requestedState != WpfState.Normal;
        IsVisible = false;
        ClearPopupKeyboardRecipient();
        if (_captured) { ReleaseMouseCapture(); ReportMouse(RawMouseActions.CancelCapture); }
        if (!CanNotify || IsVisible) return;
        _window.Hide();
        if (_mouseActive) ReportMouse(RawMouseActions.Deactivate);
        if (!CanNotify || IsVisible) return;
        if (_keyboardActive) ReportKey(RawKeyboardActions.Deactivate, Environment.TickCount, 0);
        _mouseActive = false;
        _keyboardActive = false;
    }
    public bool Activate()
    {
        Verify();
        _window.Activate();
        bool wasKeyboardActive = _keyboardActive;
        bool acquired = AcquireFocus(false);
        if (acquired && !wasKeyboardActive && CanNotify)
            ReportKey(RawKeyboardActions.Activate, Environment.TickCount, 0);
        return acquired;
    }
    public bool AcquireFocus(bool checkOnly)
    {
        Verify();
        if (!IsEnabled || !IsVisible || _closed) return false;
        var keyboardOwner = KeyboardOwner;
        bool result = _popupOwner == null ? _window.AcquireFocus(checkOnly) : keyboardOwner._window.AcquireFocus(true);
        if (result && !checkOnly)
        {
            var previous = keyboardOwner._keyboardRecipient ?? keyboardOwner;
            if (previous != this) previous._keyboardActive = false;
            keyboardOwner._keyboardRecipient = this == keyboardOwner ? null : this;
            _keySnapshot = keyboardOwner._input.ReadKeyboardState();
            _keyboardActive = true;
        }
        return result;
    }
    public KeyStates GetKeyStates(WpfKey key)
    {
        Verify();
        return _keySnapshot.GetValueOrDefault(key);
    }
    public PortablePointerSnapshot GetPointerState()
    {
        Verify();
        return _input.ReadPointerState();
    }
    public bool CaptureMouse()
    {
        Verify();
        _captured = IsEnabled && IsVisible && _input.CaptureMouse();
        if (!CanNotify || !IsEnabled || !IsVisible)
        {
            if (_captured) _input.ReleaseMouseCapture();
            _captured = false;
            return false;
        }
        if (_captured)
        {
            foreach (var peer in _windows.Values) peer._captured = ReferenceEquals(peer, this);
            MarkMouseActive();
        }
        return _captured;
    }
    public void ReleaseMouseCapture()
    {
        Verify();
        bool released = _captured;
        _input.ReleaseMouseCapture();
        _captured = false;
        if (!released) return;
        _ = _source.Dispatcher.BeginInvoke(WpfFork::System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            if (!CanNotify || Mouse.PrimaryDevice.ActiveSource != _source || _windows.Values.Any(peer => peer._captured)) return;
            var position = _input.ReadPointerState().PhysicalClientPosition;
            if (SourceFromScreenPoint(ClientToScreen(new WpfPoint(position.X, position.Y))) != null) return;
            _mouseActive = false;
            ReportMouse(RawMouseActions.Deactivate);
        }));
    }
    public bool SetCursor(WpfCursor cursor)
    {
        Verify();
        return !_closed && !_closing && _input.SetCursor(cursor);
    }
    public bool SetCursorVisibility(bool visible)
    {
        Verify();
        return _closed || _closing ? visible : _input.SetCursorVisibility(visible);
    }
    public WpfPoint ClientToScreen(WpfPoint point)
    {
        Verify();
        return new(point.X + _clientScreenOrigin.X, point.Y + _clientScreenOrigin.Y);
    }
    public WpfPoint ScreenToClient(WpfPoint point)
    {
        Verify();
        return new(point.X - _clientScreenOrigin.X, point.Y - _clientScreenOrigin.Y);
    }
    public WpfSource? SourceFromScreenPoint(WpfPoint point)
    {
        Verify();
        var inputOwner = CanNotify && _window.Handle != 0 ? this :
            _windows.Values.FirstOrDefault(peer => peer.CanNotify && peer._window.Handle != 0);
        if (inputOwner is null) return null;
        var native = inputOwner._input.WindowAt(checked((int)point.X), checked((int)point.Y));
        if (native == 0) return null;
        return _windows.Values.FirstOrDefault(peer => peer.CanNotify && peer._window.Handle == native)?._source;
    }

    public void Close()
    {
        Verify();
        if (_closed || _closing) return;
        _closing = true;
        var popupFailure = CloseOwnedPopups();
        try
        {
            ClearPopupKeyboardRecipient();
            _automation?.Dispose();
            _automation = null;
            _window.Dispose();
            AcknowledgeClose();
        }
        finally { _closing = false; popupFailure?.Throw(); }
    }
    private void OnNativeClosed() { if (!_closing) AcknowledgeClose(); }
    private void AcknowledgeClose()
    {
        if (_closed) return;
        _closed = true; IsVisible = false; _isActive = false;
        _fileDialogCancellation?.Cancel();
        Closed?.Invoke();
    }
    public void Dispose()
    {
        if (_disposed) return;
        Verify();
        var failure = CloseOwnedPopups();
        void Clean(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        Clean(ClearPopupKeyboardRecipient);
        Clean(() => _fileDialogCancellation?.Cancel());
        _window.Exposed -= OnExposed;
        _window.CloseRequested -= OnCloseRequested;
        _window.MetricsChanged -= OnNativeMetricsChanged;
        _window.Closed -= OnNativeClosed;
        _window.FocusChanged -= OnFocus;
        _input.KeyInput -= OnKeyInput;
        _input.TextInput -= OnTextInput;
        _input.PointerInput -= OnPointerInput;
        _input.CaptureLost -= OnCaptureLost;
        if (_automation is not null) Clean(_automation.Dispose);
        _automation = null;
        Clean(_input.Dispose);
        _windows.Remove(_window);
        _disposed = true;
        if (!_closed) Clean(_window.Dispose);
        failure?.Throw();
    }

    private void OnFocus(bool active)
    {
        if (!CanNotify || _isActive == active) return;
        if (active && (!IsVisible || !IsEnabled)) return;
        _isActive = active;
        if (active)
        {
            var recipient = KeyboardRecipient;
            recipient._keySnapshot = _input.ReadKeyboardState();
            recipient._keyboardActive = true;
            recipient.ReportKey(RawKeyboardActions.Activate, Environment.TickCount, 0);
        }
        else
        {
            ClearPopupKeyboardRecipient();
            foreach (var popup in _windows.Values.Where(peer => peer._popupOwner == this).ToArray())
                _ = _source.Dispatcher.BeginInvoke(WpfFork::System.Windows.Threading.DispatcherPriority.Send,
                    new Action(() => { if (popup.CanNotify) popup.OwnerDeactivated?.Invoke(); }));
            if (_captured) { ReleaseMouseCapture(); ReportMouse(RawMouseActions.CancelCapture); }
            if (!CanNotify) return;
            if (_keyboardActive) ReportKey(RawKeyboardActions.Deactivate, Environment.TickCount, 0);
            if (!CanNotify) return;
            if (_mouseActive) ReportMouse(RawMouseActions.Deactivate);
            _keyboardActive = false; _mouseActive = false;
        }
        if (CanNotify && _isActive == active) ActivationChanged?.Invoke(active);
    }
    private void OnCaptureLost()
    {
        if (!CanNotify || !_captured) return;
        _captured = false;
        ReportMouse(RawMouseActions.CancelCapture);
    }
    private void RefreshMetrics()
    {
        var metrics = _window.ReadMetrics();
        double scale = _window.Scale;
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("The native X11 DPI scale is invalid.");
        _clientSize = new WpfSize(metrics.ClientWidth / scale, metrics.ClientHeight / scale);
        _outerSize = new WpfSize((metrics.ClientWidth + metrics.FrameExtents.Left + metrics.FrameExtents.Right) / scale,
            (metrics.ClientHeight + metrics.FrameExtents.Top + metrics.FrameExtents.Bottom) / scale);
        _desktopPosition = new WpfPoint(metrics.ClientX - metrics.FrameExtents.Left, metrics.ClientY - metrics.FrameExtents.Top);
        _clientScreenOrigin = new WpfPoint(metrics.ClientX, metrics.ClientY);
        _dpiScale = new WpfDpi(scale, scale);
        if (_constructing || IsVisible) _metricsState = ToWpf(metrics.State);
    }
    private void QueueMetricsRefresh()
    {
        if (!CanNotify || _metricsRefreshQueued) return;
        _metricsRefreshQueued = true;
        _source.Dispatcher.BeginInvoke(WpfFork::System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            _metricsRefreshQueued = false;
            if (CanNotify)
            {
                bool stateChanged = _stateChangedPending;
                _stateChangedPending = false;
                var previousState = _metricsState;
                RefreshMetrics();
                if (stateChanged && IsVisible)
                {
                    if (_stateRequestPending && _metricsState == _requestedState) _stateRequestPending = false;
                    if (!_stateRequestPending) _requestedState = _metricsState;
                    if (previousState != _metricsState) StateChanged?.Invoke(_metricsState);
                }
                if (!CanNotify) return;
                MetricsChanged?.Invoke();
                if (CanNotify && IsVisible && _stateRequestPending) ApplyWindowState();
            }
        }));
    }
    private void OnKeyInput(X11KeyInput key)
    {
        if (!CanNotify || !IsVisible || !IsEnabled) return;
        var recipient = KeyboardRecipient;
        if (!recipient._keyboardActive)
        {
            recipient._keySnapshot = KeyboardOwner._input.ReadKeyboardState();
            recipient._keyboardActive = true;
            recipient.ReportKey(RawKeyboardActions.Activate, key.Timestamp, 0);
        }
        if (!recipient.CanNotify || !recipient.IsVisible || !recipient.IsEnabled) return;
        recipient.RawInput?.Invoke(new RawKeyboardInputReport(recipient._source, InputMode.Foreground, key.Timestamp,
            key.IsDown ? RawKeyboardActions.KeyDown : RawKeyboardActions.KeyUp,
            0, false, key.IsAltDown, key.VirtualKey, IntPtr.Zero));
    }
    private void OnTextInput(int timestamp, string text)
    {
        if (!CanNotify || !IsVisible || !IsEnabled) return;
        var recipient = KeyboardRecipient;
        recipient.RawInput?.Invoke(new PortableTextInputReport(recipient._source, InputMode.Foreground, timestamp, text));
    }
    private void OnPointerInput(X11PointerInput pointer)
    {
        if (!CanNotify || !IsVisible || !IsEnabled) return;
        if (pointer.IsLeave)
        {
            if (!_captured && _mouseActive) { ReportMouse(RawMouseActions.Deactivate, pointer.Timestamp); _mouseActive = false; }
            return;
        }
        var action = pointer.Actions | RawMouseActions.AbsoluteMove;
        if (!_mouseActive) action |= RawMouseActions.Activate;
        MarkMouseActive();
        RawInput?.Invoke(new RawMouseInputReport(InputMode.Foreground, pointer.Timestamp, _source,
            action, pointer.X, pointer.Y, pointer.Wheel, IntPtr.Zero));
    }
    internal X11Window CreateNativePopup(bool transparent)
    {
        Verify();
        return new X11Window(_window.Connection, new X11WindowOptions
        { Popup = true, Transparent = transparent, Owner = _window, X = checked((int)_clientScreenOrigin.X), Y = checked((int)_clientScreenOrigin.Y) });
    }
    void IPortablePopupPeer.Show() => Show(false);
    public void SetHitTestable(bool value)
    {
        Verify();
        if (_popupOwner is null) throw new InvalidOperationException("Only a native Popup has a hit-testable popup surface.");
        _window.SetHitTestable(value);
    }
    private NativeWindowPeer KeyboardOwner => _popupOwner?.KeyboardOwner ?? this;
    private NativeWindowPeer KeyboardRecipient => KeyboardOwner._keyboardRecipient is { CanNotify: true, IsVisible: true } recipient ? recipient : KeyboardOwner;
    private void ClearPopupKeyboardRecipient()
    {
        var owner = KeyboardOwner;
        var recipient = owner._keyboardRecipient;
        if (recipient == null || (this != owner && recipient != this)) return;
        if (this != owner && owner.CanNotify) owner._input.ResetComposition();
        owner._keyboardRecipient = null;
        if (recipient._keyboardActive && recipient.CanNotify)
            recipient.ReportKey(RawKeyboardActions.Deactivate, Environment.TickCount, 0);
        recipient._keyboardActive = false;
    }
    private ExceptionDispatchInfo? CloseOwnedPopups()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var popup in _windows.Values.Where(peer => peer._popupOwner == this).ToArray())
        {
            try { if (popup.CanNotify) popup.OwnerClosing?.Invoke(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { if (popup.CanNotify) popup.Close(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        return failure;
    }

    private void MarkMouseActive()
    {
        foreach (var peer in _windows.Values) peer._mouseActive = ReferenceEquals(peer, this);
    }
    private void ReportKey(RawKeyboardActions actions, int timestamp, int virtualKey) =>
        RawInput?.Invoke(new RawKeyboardInputReport(_source, InputMode.Foreground, timestamp, actions, 0, false, false, virtualKey, IntPtr.Zero));
    private void ReportMouse(RawMouseActions actions, int? timestamp = null) =>
        RawInput?.Invoke(new RawMouseInputReport(InputMode.Foreground, timestamp ?? Environment.TickCount, _source, actions, 0, 0, 0, IntPtr.Zero));
    private void Verify() { _source.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private static WpfState ToWpf(X11WindowState state) => state switch
    { X11WindowState.Normal => WpfState.Normal, X11WindowState.Minimized => WpfState.Minimized, X11WindowState.Maximized => WpfState.Maximized,
      _ => throw new PlatformNotSupportedException("The native window state is unsupported.") };
    private int ToPixels(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value * _window.Scale < 1)
            throw new PlatformNotSupportedException("The owned X11 Window requires at least one native client pixel on each axis.");
        return checked((int)(value * _window.Scale));
    }
    private int ToMinimumPixels(double value) => Math.Max(1, checked((int)(value * _window.Scale)));
    private int ToMaximumPixels(double value) => double.IsPositiveInfinity(value) ? 0 : checked((int)(value * _window.Scale));
}
