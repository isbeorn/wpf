extern alias WpfFork;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using Avalonia.X11;
using WpfFork::MS.Internal.Portability.Input;
using WpfFork::MS.Internal.Portability.Windowing;
using WpfFork::System.Windows.Input;
using WpfPoint = WpfFork::System.Windows.Point;
using WpfSize = WpfFork::System.Windows.Size;
using WpfDpi = WpfFork::System.Windows.DpiScale;
using WpfSource = WpfFork::System.Windows.PresentationSource;
using WpfState = WpfFork::System.Windows.WindowState;
using WpfKey = WpfFork::System.Windows.Input.Key;
using WpfCursor = WpfFork::System.Windows.Input.Cursor;
using KeyStates = WpfFork::System.Windows.Input.KeyStates;
using AvaState = Avalonia.Controls.WindowState;

namespace Wpf.Portable.NativeHost;

internal sealed class NativeWindowFactory(IWindowingPlatform platform) : IPortableWindowFactory
{
    private readonly Dictionary<IWindowImpl, NativeWindowPeer> _windows = new();

    public IPortableWindowPeer CreateWindow(WpfSource source, PortableWindowOptions options)
    {
        source.VerifyAccess();
        IWindowImpl implementation = platform.CreateWindow();
        try
        {
            var peer = new NativeWindowPeer(implementation, source, _windows, options);
            _windows.Add(implementation, peer);
            return peer;
        }
        catch { implementation.Dispose(); throw; }
    }
}

internal sealed class NativeWindowPeer : IPortableWindowPeer, IPortableInputHost, IPortableFileDialogHost
{
    private readonly IWindowImpl _window;
    private readonly WpfSource _source;
    private readonly Dictionary<IWindowImpl, NativeWindowPeer> _windows;
    private readonly ExternalWindowBridge _native;
    private readonly NativeAutomationRoot _automation;
    private readonly IFramebufferRenderTarget _framebuffer;
    private IReadOnlyDictionary<Avalonia.Input.Key, int> _keySnapshot = new Dictionary<Avalonia.Input.Key, int>();
    private bool _disposed, _closing, _closed, _isActive, _mouseActive, _keyboardActive, _captured;
    private bool _constructing = true;
    private WpfState _metricsState;
    private WpfState _requestedState;
    private bool _metricsRefreshQueued;
    private bool _stateChangedPending;
    private bool _stateRequestPending;
    private StandardCursorType _cursorShape = StandardCursorType.Arrow;
    private bool _cursorVisible = true;
    private WpfSize _clientSize, _outerSize;
    private WpfPoint _desktopPosition;
    private WpfPoint _clientScreenOrigin;
    private WpfDpi _dpiScale;
    private CancellationTokenSource? _fileDialogCancellation;

    internal NativeWindowPeer(IWindowImpl window, WpfSource source,
        Dictionary<IWindowImpl, NativeWindowPeer> windows, PortableWindowOptions options)
    {
        _window = window;
        _source = source;
        _windows = windows;
        _automation = new NativeAutomationRoot(source, window);
        _native = new ExternalWindowBridge(window, _automation);
        try
        {
            _framebuffer = window.Surfaces.OfType<IFramebufferPlatformSurface>().FirstOrDefault()?.CreateFramebufferRenderTarget()
                ?? throw new PlatformNotSupportedException("The native X11 peer has no CPU framebuffer surface.");
            window.Closing = _ => { if (!_closing && !_closed) CloseRequested?.Invoke(); return true; };
            window.Closed = OnNativeClosed;
            window.Paint = _ => { if (CanNotify) Exposed?.Invoke(); };
            window.Resized = (_, _) => QueueMetricsRefresh();
            window.ScalingChanged = _ => QueueMetricsRefresh();
            window.PositionChanged = _ => QueueMetricsRefresh();
            window.WindowStateChanged = _ =>
            {
                if (!CanNotify || !IsVisible) return;
                _stateChangedPending = true;
                QueueMetricsRefresh();
            };
            window.Input = OnInput;
            _native.FocusChanged += OnFocus;
            _native.CaptureLost += OnCaptureLost;
            SetTitle(options.Title);
            if (options.DesktopPosition is { } position) SetDesktopPosition(position);
            SetOwner(options.Owner);
            SetShowInTaskbar(options.ShowInTaskbar);
            SetEnabled(options.IsEnabled);
            SetWindowState(options.WindowState);
            if (options.ClientSize is { } size) SetClientSize(size);
            RefreshMetrics();
            _constructing = false;
        }
        catch { _framebuffer?.Dispose(); _native.Dispose(); _automation.Dispose(); throw; }
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
            var filters = options.Filters.Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Patterns }).ToArray();
            FilePickerFileType? selected = filters.Length == 0 ? null : filters[options.FilterIndex - 1];
            IReadOnlyList<IStorageFile> files;
            FilePickerFileType? selectedType;
            if (options.Save)
            {
                var result = await _native.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = options.Title, SuggestedFileName = options.FileName,
                    FileTypeChoices = filters, SuggestedFileType = selected
                }, options.Directory, cancellation.Token).ConfigureAwait(true);
                files = result.File is null ? [] : [result.File];
                selectedType = result.SelectedFileType;
            }
            else
            {
                var result = await _native.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = options.Title, SuggestedFileName = options.FileName,
                    FileTypeFilter = filters, SuggestedFileType = selected, AllowMultiple = options.AllowMultiple
                }, options.Directory, cancellation.Token).ConfigureAwait(true);
                files = result.Files;
                selectedType = result.SelectedFileType;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            Verify();
            int? filterIndex = null;
            if (selectedType is not null)
            {
                int index = Array.IndexOf(filters, selectedType);
                if (index < 0) throw new InvalidDataException("The desktop file chooser returned an unknown filter.");
                filterIndex = index + 1;
            }
            return new PortableFileDialogResult(files.Count != 0, Array.AsReadOnly(files.Select(file => file.Path.LocalPath).ToArray()), filterIndex);
        }
        finally { _fileDialogCancellation = null; }
    }

    public unsafe void Present(ReadOnlySpan<byte> premultipliedBgra, int pixelWidth, int pixelHeight, int stride)
    {
        Verify();
        if (_closed || _source.IsDisposed || _source.RootVisual is null)
            throw new InvalidOperationException("A detached or closed native peer cannot accept a frame.");
        int rowBytes = checked(pixelWidth * 4);
        if (pixelWidth <= 0 || pixelHeight <= 0 || stride < rowBytes ||
            premultipliedBgra.Length < checked(stride * (pixelHeight - 1) + rowBytes))
            throw new ArgumentException("The premultiplied BGRA frame dimensions or stride are invalid.");
        var scene = new IRenderTarget.RenderTargetSceneInfo(new PixelSize(pixelWidth, pixelHeight),
            DpiScale.DpiScaleX, new Size(ClientSize.Width, ClientSize.Height), CompositionTransparencyLevel.None);
        using (var target = _framebuffer.Lock(scene, out _))
        {
            int targetRowBytes = checked(target.Size.Width * 4);
            if (target.Size.Width <= 0 || target.Size.Height <= 0 || target.RowBytes < targetRowBytes || target.Format != PixelFormat.Bgra8888 ||
                target.AlphaFormat != AlphaFormat.Premul || target.Address == IntPtr.Zero)
                throw new InvalidOperationException("The native framebuffer dimensions, layout or pixel format are invalid.");
            int copyBytes = checked(Math.Min(pixelWidth, target.Size.Width) * 4);
            int copyRows = Math.Min(pixelHeight, target.Size.Height);
            // Configure events can follow a viewport resize. The source still owns a valid frame for its last metrics.
            for (int row = 0; row < target.Size.Height; row++)
            {
                var destination = new Span<byte>((byte*)target.Address + checked(row * target.RowBytes), targetRowBytes);
                destination.Clear();
                if (row < copyRows) premultipliedBgra.Slice(checked(row * stride), copyBytes).CopyTo(destination);
            }
        }
        ExternalWindowBridge.VerifyFramebufferUpload();
    }

    public void SetTitle(string title) { Verify(); _window.SetTitle(title); }
    public void SetShowInTaskbar(bool show) { Verify(); _window.ShowTaskbarIcon(show); }
    public void SetDesktopPosition(WpfPoint position)
    {
        Verify();
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        _window.Move(new PixelPoint(checked((int)Math.Round(position.X)), checked((int)Math.Round(position.Y))));
        QueueMetricsRefresh();
    }
    public void SetClientSize(WpfSize size)
    {
        Verify();
        _native.RequestClientSize(new Size(size.Width, size.Height));
        QueueMetricsRefresh();
    }
    public void SetClientSizeConstraints(WpfSize minimum, WpfSize maximum)
    { Verify(); _window.SetMinMaxSize(new Size(minimum.Width, minimum.Height), new Size(maximum.Width, maximum.Height)); }
    public void SetOwner(IPortableWindowPeer owner)
    {
        Verify();
        if (owner is not null && owner is not NativeWindowPeer) throw new ArgumentException("The owner uses a different native backend.", nameof(owner));
        _window.SetParent((owner as NativeWindowPeer)?._window);
    }
    public void SetEnabled(bool enabled)
    {
        Verify();
        bool deactivate = CanNotify && IsEnabled && !enabled;
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
    private void ApplyWindowState() => _window.WindowState = _requestedState switch
    { WpfState.Normal => AvaState.Normal, WpfState.Minimized => AvaState.Minimized, WpfState.Maximized => AvaState.Maximized,
      _ => throw new ArgumentOutOfRangeException(nameof(_requestedState)) };
    public void Show(bool activate)
    {
        Verify();
        _automation.Attach();
        _window.Show(activate, false);
        IsVisible = true;
        ApplyWindowState();
        if (activate && _requestedState != WpfState.Minimized) Activate();
    }
    public void Hide()
    {
        Verify();
        if (!_stateRequestPending) _requestedState = ToWpf(_native.ReadWindowState());
        _stateRequestPending |= _requestedState != WpfState.Normal;
        IsVisible = false;
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
        bool result = _native.AcquireFocus(checkOnly);
        if (result && !checkOnly)
        {
            _keySnapshot = _native.ReadKeyboardState();
            _keyboardActive = true;
        }
        return result;
    }
    public KeyStates GetKeyStates(WpfKey key)
    {
        Verify();
        return TryAvaloniaKey(key, out var nativeKey) ? (KeyStates)_keySnapshot.GetValueOrDefault(nativeKey) : KeyStates.None;
    }
    public PortablePointerSnapshot GetPointerState()
    {
        Verify();
        var (position, buttons) = _native.ReadPointerState();
        return new PortablePointerSnapshot(new WpfPoint(position.X, position.Y),
            State(RawInputModifiers.LeftMouseButton), State(RawInputModifiers.RightMouseButton),
            State(RawInputModifiers.MiddleMouseButton), State(RawInputModifiers.XButton1MouseButton),
            State(RawInputModifiers.XButton2MouseButton));
        MouseButtonState State(RawInputModifiers button) => (buttons & button) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released;
    }
    public bool CaptureMouse()
    {
        Verify();
        _captured = IsEnabled && IsVisible && _native.CaptureMouse();
        if (_captured) _mouseActive = true;
        return _captured;
    }
    public void ReleaseMouseCapture() { Verify(); _native.ReleaseMouseCapture(); _captured = false; }
    public bool SetCursor(WpfCursor cursor)
    {
        Verify();
        if (_closed || _closing) return false;
        StandardCursorType? type = cursor?.CursorType.ToString() switch
        {
            null or "Arrow" => StandardCursorType.Arrow, "None" => StandardCursorType.None,
            "Hand" => StandardCursorType.Hand, "IBeam" => StandardCursorType.Ibeam,
            "Cross" => StandardCursorType.Cross, "Wait" => StandardCursorType.Wait,
            "SizeAll" => StandardCursorType.SizeAll, "SizeNS" => StandardCursorType.SizeNorthSouth,
            "SizeWE" => StandardCursorType.SizeWestEast, "SizeNWSE" => StandardCursorType.TopLeftCorner,
            "SizeNESW" => StandardCursorType.TopRightCorner, "No" => StandardCursorType.No, _ => null
        };
        if (type is null) return false;
        _cursorShape = type.Value;
        ApplyCursor();
        return true;
    }
    public bool SetCursorVisibility(bool visible)
    {
        Verify();
        if (_closed || _closing) return visible;
        _cursorVisible = visible;
        ApplyCursor();
        return true;
    }
    private void ApplyCursor() => _window.SetCursor(AvaloniaLocator.Current.GetRequiredService<ICursorFactory>()
        .GetCursor(_cursorVisible ? _cursorShape : StandardCursorType.None));
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
        var native = _native.WindowAt(new PixelPoint(checked((int)point.X), checked((int)point.Y)));
        return native is not null && _windows.TryGetValue(native, out var peer) && !peer._source.IsDisposed ? peer._source : null;
    }

    public void Close()
    {
        Verify();
        if (_closed || _closing) return;
        _closing = true;
        try { _window.Dispose(); }
        finally { _closing = false; }
        AcknowledgeClose();
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
        _fileDialogCancellation?.Cancel();
        _window.Input = null; _window.Paint = null; _window.Closing = null;
        _window.Resized = null; _window.ScalingChanged = null; _window.PositionChanged = null;
        _window.WindowStateChanged = null; _window.Closed = null;
        _native.Dispose();
        _framebuffer.Dispose();
        _windows.Remove(_window);
        _disposed = true;
        try { if (!_closed) _window.Dispose(); }
        finally { _automation.Dispose(); }
    }

    private void OnFocus(bool active)
    {
        if (!CanNotify || _isActive == active) return;
        if (active && (!IsVisible || !IsEnabled)) return;
        _isActive = active;
        if (active)
        {
            _keySnapshot = _native.ReadKeyboardState();
            _keyboardActive = true;
            ReportKey(RawKeyboardActions.Activate, Environment.TickCount, 0);
        }
        else
        {
            if (_captured) { ReleaseMouseCapture(); ReportMouse(RawMouseActions.CancelCapture); }
            if (!CanNotify) return;
            if (_keyboardActive) ReportKey(RawKeyboardActions.Deactivate, Environment.TickCount, 0);
            if (!CanNotify) return;
            if (_mouseActive) ReportMouse(RawMouseActions.Deactivate);
            _keyboardActive = false; _mouseActive = false;
        }
        ActivationChanged?.Invoke(active);
    }
    private void OnCaptureLost()
    {
        if (!CanNotify || !_captured) return;
        _captured = false;
        ReportMouse(RawMouseActions.CancelCapture);
    }
    private void RefreshMetrics()
    {
        var metrics = _native.ReadMetrics();
        double scale = _window.RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("The native X11 DPI scale is invalid.");
        _clientSize = new WpfSize(metrics.ClientSize.Width / scale, metrics.ClientSize.Height / scale);
        _outerSize = new WpfSize((metrics.ClientSize.Width + metrics.FrameExtents.Left + metrics.FrameExtents.Right) / scale,
            (metrics.ClientSize.Height + metrics.FrameExtents.Top + metrics.FrameExtents.Bottom) / scale);
        _desktopPosition = new WpfPoint(metrics.ClientOrigin.X - metrics.FrameExtents.Left, metrics.ClientOrigin.Y - metrics.FrameExtents.Top);
        _clientScreenOrigin = new WpfPoint(metrics.ClientOrigin.X, metrics.ClientOrigin.Y);
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
                // Upstream state, scaling and Configure handlers still use the XID after their callbacks return.
                RefreshMetrics();
                if (stateChanged && IsVisible)
                {
                    if (_stateRequestPending && _metricsState == _requestedState) _stateRequestPending = false;
                    if (!_stateRequestPending) _requestedState = _metricsState;
                    StateChanged?.Invoke(_metricsState);
                }
                if (!CanNotify) return;
                MetricsChanged?.Invoke();
                if (CanNotify && IsVisible && _stateRequestPending) ApplyWindowState();
            }
        }));
    }
    private void OnInput(RawInputEventArgs args)
    {
        if (!CanNotify || !IsVisible || !IsEnabled || _source.IsDisposed) return;
        int timestamp = unchecked((int)args.Timestamp);
        switch (args)
        {
            case RawKeyEventArgs key:
                if (!Enum.TryParse<WpfKey>(key.Key.ToString(), out var mapped) || KeyInterop.VirtualKeyFromKey(mapped) == 0)
                    throw new PlatformNotSupportedException("The native logical key '" + key.Key + "' has no WPF key mapping.");
                if (!_keyboardActive)
                {
                    _keySnapshot = _native.ReadKeyboardState();
                    _keyboardActive = true;
                    ReportKey(RawKeyboardActions.Activate, timestamp, 0);
                }
                RawInput?.Invoke(new RawKeyboardInputReport(_source, InputMode.Foreground, timestamp,
                    key.Type == RawKeyEventType.KeyDown ? RawKeyboardActions.KeyDown : RawKeyboardActions.KeyUp,
                    0, false, (key.Modifiers & RawInputModifiers.Alt) != 0, KeyInterop.VirtualKeyFromKey(mapped), IntPtr.Zero));
                break;
            case RawTextInputEventArgs text:
                RawInput?.Invoke(new PortableTextInputReport(_source, InputMode.Foreground, timestamp, text.Text));
                break;
            case RawPointerEventArgs pointer:
                if (pointer.Type == RawPointerEventType.CancelCapture) { OnCaptureLost(); break; }
                if (pointer.Type == RawPointerEventType.LeaveWindow)
                {
                    if (!_captured && _mouseActive) { ReportMouse(RawMouseActions.Deactivate, timestamp); _mouseActive = false; }
                    break;
                }
                var action = pointer.Type switch
                {
                    RawPointerEventType.Move => RawMouseActions.None,
                    RawPointerEventType.LeftButtonDown => RawMouseActions.Button1Press,
                    RawPointerEventType.LeftButtonUp => RawMouseActions.Button1Release,
                    RawPointerEventType.RightButtonDown => RawMouseActions.Button2Press,
                    RawPointerEventType.RightButtonUp => RawMouseActions.Button2Release,
                    RawPointerEventType.MiddleButtonDown => RawMouseActions.Button3Press,
                    RawPointerEventType.MiddleButtonUp => RawMouseActions.Button3Release,
                    RawPointerEventType.XButton1Down => RawMouseActions.Button4Press,
                    RawPointerEventType.XButton1Up => RawMouseActions.Button4Release,
                    RawPointerEventType.XButton2Down => RawMouseActions.Button5Press,
                    RawPointerEventType.XButton2Up => RawMouseActions.Button5Release,
                    RawPointerEventType.Wheel => RawMouseActions.VerticalWheelRotate,
                    _ => throw new PlatformNotSupportedException("Native pointer action '" + pointer.Type + "' is outside the initial mouse contract.")
                };
                int wheel = 0;
                if (pointer is RawMouseWheelEventArgs scroll)
                {
                    if (scroll.Delta.X != 0)
                        throw new PlatformNotSupportedException("Portable WPF does not route native horizontal wheel input.");
                    wheel = checked((int)Math.Round(scroll.Delta.Y * 120));
                }
                if (!_mouseActive) action |= RawMouseActions.Activate;
                _mouseActive = true;
                RawInput?.Invoke(new RawMouseInputReport(InputMode.Foreground, timestamp, _source,
                    action | RawMouseActions.AbsoluteMove,
                    checked((int)Math.Round(pointer.Position.X * _window.RenderScaling)),
                    checked((int)Math.Round(pointer.Position.Y * _window.RenderScaling)), wheel, IntPtr.Zero));
                break;
            default: throw new PlatformNotSupportedException("The native raw input report '" + args.GetType().Name + "' is outside the initial mouse/keyboard/text contract.");
        }
    }
    private void ReportKey(RawKeyboardActions actions, int timestamp, int virtualKey) =>
        RawInput?.Invoke(new RawKeyboardInputReport(_source, InputMode.Foreground, timestamp, actions, 0, false, false, virtualKey, IntPtr.Zero));
    private void ReportMouse(RawMouseActions actions, int? timestamp = null) =>
        RawInput?.Invoke(new RawMouseInputReport(InputMode.Foreground, timestamp ?? Environment.TickCount, _source, actions, 0, 0, 0, IntPtr.Zero));
    private void Verify() { _source.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private static WpfState ToWpf(AvaState state) => state switch
    { AvaState.Normal => WpfState.Normal, AvaState.Minimized => WpfState.Minimized, AvaState.Maximized => WpfState.Maximized,
      _ => throw new PlatformNotSupportedException("The native window state is unsupported.") };
    private static bool TryAvaloniaKey(WpfKey key, out Avalonia.Input.Key nativeKey) => Enum.TryParse(key.ToString(), out nativeKey);
}
