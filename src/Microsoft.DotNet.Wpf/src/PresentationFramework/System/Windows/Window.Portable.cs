// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shell;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;

namespace System.Windows
{
    public partial class Window
    {
        private readonly bool _portableFrameworkOwned;

        internal Window(bool inRbw, bool portableFrameworkOwned)
        {
            _inTrustedSubWindow = inRbw;
            _portableFrameworkOwned = portableFrameworkOwned;
            if (portableFrameworkOwned) InheritanceBehavior = InheritanceBehavior.SkipToThemeNext;
            Initialize();
        }

        private IPortableWindowPeer _portablePeer;
        private PortablePresentationSource _portableSource;
        private bool _portableUpdatingMetrics;
        private bool _portableModal;
        private Rect _portableRestoreBounds = Rect.Empty;
        private Size _portableNormalFrameSize;
        private WindowState? _portableStateRequest;
        private PortableBoundsChanges _portablePendingBounds;
        private PortableBoundsChanges _portableInitialSizeBounds;
        private PortableBoundsChanges _portableBoundsAwaitingAck;
        private Size _portableRequestedClientPixels;
        private Point _portableRequestedDesktopPosition;
        private DpiScale _portableRequestedBoundsDpi;
        private bool _portableHasBeenShown;
        private bool _portableApplyingBounds;
        private bool _portableMetricsDuringBoundsApply;
        private WindowState _portableLastMetricsState;
        private bool _portablePendingLocationNotification;
        private bool _portableUseRestoreDimensions;
        private bool _portableLayingOut;

        [Flags]
        private enum PortableBoundsChanges { None = 0, Width = 1, Height = 2, Left = 4, Top = 8 }

        private static PlatformNotSupportedException PortableUnsupported(string feature) =>
            new PlatformNotSupportedException(feature + " is not supported by the initial portable Window implementation.");

        private Size PortableFrameSize => _portablePeer == null ? new Size() : new Size(
            Math.Max(0, _portablePeer.OuterSize.Width - _portablePeer.ClientSize.Width),
            Math.Max(0, _portablePeer.OuterSize.Height - _portablePeer.ClientSize.Height));

        private static readonly Lazy<Style> s_portableDefaultStyle = new Lazy<Style>(CreatePortableWindowStyle);
        internal static Style PortableDefaultStyle => s_portableDefaultStyle.Value;

        private static Style CreatePortableWindowStyle()
        {
            // Used only when the exact Window theme key has no style.
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            var decorator = new FrameworkElementFactory(typeof(AdornerDecorator));
            decorator.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            border.AppendChild(decorator);
            var template = new ControlTemplate(typeof(Window)) { VisualTree = border };
            var style = new Style(typeof(Window));
            style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension(SystemColors.WindowTextBrushKey)));
            style.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension(SystemColors.WindowBrushKey)));
            style.Setters.Add(new Setter(TemplateProperty, template));
            style.Seal();
            return style;
        }

        private WindowStyle PortableChromeWindowStyle => WindowChrome.GetWindowChrome(this) == null ? WindowStyle : WindowStyle.None;

        internal void PortableUpdateChrome()
        {
            if (!_disposed) _portablePeer?.SetWindowStyle(PortableChromeWindowStyle);
        }

        internal bool PortableBeginChromeMoveResize(ResizeGripDirection direction, Point physicalScreenPosition)
        {
            VerifyContextAndObjectState();
            if (_portablePeer == null || !_portablePeer.IsVisible || !_portablePeer.IsEnabled) return false;
            return _portablePeer.BeginMoveResize(direction, physicalScreenPosition);
        }

        internal void PortableCancelChromeOperation()
        {
            if (!_disposed) _portablePeer?.CancelMoveResize();
        }

        private void PortableCreateSource()
        {
            VerifyContextAndObjectState();
            VerifyCanShow();
            VerifyNotClosing();
            if (_portablePeer != null) return;
            if (ResizeMode == ResizeMode.CanResizeWithGrip) throw PortableUnsupported(nameof(ResizeMode) + ".CanResizeWithGrip");
            if (WindowStyle is not WindowStyle.None and not WindowStyle.SingleBorderWindow) throw PortableUnsupported(nameof(WindowStyle));
            VerifyConsistencyWithAllowsTransparency();
            if (TaskbarItemInfo != null) throw PortableUnsupported(nameof(TaskbarItemInfo));

            var source = new PortablePresentationSource(PortableLayoutRoot);
            IPortableWindowPeer peer = null;
            try
            {
                peer = PortableWindowPlatform.Factory.CreateWindow(source, new PortableWindowOptions
                {
                    Title = Title,
                    ShowInTaskbar = ShowInTaskbar,
                    WindowStyle = PortableChromeWindowStyle,
                    ResizeMode = ResizeMode,
                    AllowsTransparency = AllowsTransparency,
                    Topmost = Topmost,
                    Owner = _ownerWindow?._portablePeer,
                    IsEnabled = IsEnabled,
                    WindowState = WindowState
                }) ?? throw new InvalidOperationException("The portable window factory returned null.");
                _portablePeer = peer;
                _portableSource = source;
                peer.CloseRequested += PortableOnCloseRequested;
                peer.Closed += PortableOnClosed;
                peer.ActivationChanged += HandleActivate;
                peer.StateChanged += PortableOnStateChanged;
                peer.MetricsChanged += PortableOnMetricsChanged;
                IsEnabledChanged += PortableOnIsEnabledChanged;
                source.AttachHost(peer);
                if (_icon != null) peer.SetIcon(_icon);
                PortableInitializeBounds();
                PortableUpdateBounds();
                SetIWindowService();
                source.RootVisual = this;
                if (_disposed || _portablePeer == null) return;
                PortableLayoutRoot(_portablePeer.ClientSize);
                if (_disposed || _portablePeer == null) return;
                LayoutUpdated += PortableOnLayoutUpdated;
                OnSourceInitialized(EventArgs.Empty);
            }
            catch
            {
                if (_portablePeer != null) PortableReleasePeer();
                else
                {
                    source.Dispose();
                    peer?.Dispose();
                }
                throw;
            }
        }

        private Size PortableReadLayoutSize()
        {
            Size size = _portablePeer.OuterSize;
            PortableBoundsChanges intent = !_portableHasBeenShown ? _portableInitialSizeBounds :
                _portablePeer.WindowState == WindowState.Normal && _portableStateRequest == null ? _portablePendingBounds : PortableBoundsChanges.None;
            if ((intent & PortableBoundsChanges.Width) != 0) size.Width = _portableRestoreBounds.Width;
            if ((intent & PortableBoundsChanges.Height) != 0) size.Height = _portableRestoreBounds.Height;
            return size;
        }

        private void PortableLayoutRoot(Size clientSize)
        {
            if (_disposed || _portablePeer == null || _portableSource == null || _portableLayingOut) return;
            _portableLayingOut = true;
            try
            {
                Size outer = PortableReadLayoutSize();
                Size constraint = new Size(
                    SizeToContent == SizeToContent.Width || SizeToContent == SizeToContent.WidthAndHeight ? double.PositiveInfinity : outer.Width,
                    SizeToContent == SizeToContent.Height || SizeToContent == SizeToContent.WidthAndHeight ? double.PositiveInfinity : outer.Height);
                Measure(constraint);
                if (_disposed || _portablePeer == null) return;
                if (SizeToContent != SizeToContent.Manual)
                {
                    Rect restore = _portableRestoreBounds;
                    PortableBoundsChanges axes = PortableBoundsChanges.None;
                    if (double.IsPositiveInfinity(constraint.Width))
                    {
                        axes |= PortableBoundsChanges.Width;
                        if (PortableContentSizeNeedsRequest(PortableBoundsChanges.Width, restore.Width, DesiredSize.Width, _portableNormalFrameSize.Width, _portablePeer.DpiScale.DpiScaleX))
                        {
                            PortableQueueBounds(PortableBoundsChanges.Width);
                            restore.Width = DesiredSize.Width;
                        }
                    }
                    if (double.IsPositiveInfinity(constraint.Height))
                    {
                        axes |= PortableBoundsChanges.Height;
                        if (PortableContentSizeNeedsRequest(PortableBoundsChanges.Height, restore.Height, DesiredSize.Height, _portableNormalFrameSize.Height, _portablePeer.DpiScale.DpiScaleY))
                        {
                            PortableQueueBounds(PortableBoundsChanges.Height);
                            restore.Height = DesiredSize.Height;
                        }
                    }
                    _portableRestoreBounds = restore;
                    PortableClampRestoreBounds(axes & _portablePendingBounds);
                    if (!_portableHasBeenShown || (_portablePeer.WindowState == WindowState.Normal && _portableStateRequest == null))
                        PortableApplyBounds();
                    if (_disposed || _portablePeer == null) return;
                    outer = PortableReadLayoutSize();
                }
                Arrange(new Rect(outer));
            }
            finally { _portableLayingOut = false; }
        }

        private void PortableOnLayoutUpdated(object sender, EventArgs args)
        {
            if (_portableHasBeenShown && SizeToContent != SizeToContent.Manual && _portablePeer != null)
                PortableLayoutRoot(_portablePeer.ClientSize);
        }

        private bool PortableContentSizeNeedsRequest(PortableBoundsChanges axis, double current, double desired, double frame, double dpi)
        {
            if ((_portablePendingBounds & axis) != 0) return current != desired;
            return !PortablePixelRequestMatches(Math.Max(0, current - frame) * dpi, Math.Max(0, desired - frame) * dpi);
        }

        private Rect PortableReadBounds()
        {
            DpiScale dpi = _portablePeer.DpiScale;
            Size size = _portablePeer.OuterSize;
            Point position = _portablePeer.DesktopPosition;
            if (!double.IsFinite(dpi.DpiScaleX) || !double.IsFinite(dpi.DpiScaleY) ||
                dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0 ||
                !double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
                !double.IsFinite(size.Width) || !double.IsFinite(size.Height))
                throw new InvalidOperationException("The portable window peer reported invalid bounds or DPI.");
            return new Rect(position.X / dpi.DpiScaleX, position.Y / dpi.DpiScaleY, size.Width, size.Height);
        }

        private Point PortableToDesktop(Point logical)
        {
            DpiScale dpi = _portablePeer.DpiScale;
            double x = logical.X * dpi.DpiScaleX;
            double y = logical.Y * dpi.DpiScaleY;
            ValidateTopLeft(x);
            ValidateTopLeft(y);
            return new Point(MS.Internal.DoubleUtil.DoubleToInt(x), MS.Internal.DoubleUtil.DoubleToInt(y));
        }

        private void PortableApplyStartupLocation()
        {
            if (WindowState != WindowState.Normal || WindowStartupLocation == WindowStartupLocation.Manual) return;
            IPortableWindowPeer owner = _ownerWindow?._portablePeer;
            if (WindowStartupLocation == WindowStartupLocation.CenterOwner && owner == null) return;
            Rect area = PortableWindowPlatform.Factory.GetWindowStartupWorkArea(_portableSource, _ownerWindow?._portableSource);
            DpiScale dpi = _portablePeer.DpiScale;
            Rect restore = _portableRestoreBounds;
            Size size = new Size(restore.Width * dpi.DpiScaleX, restore.Height * dpi.DpiScaleY);
            double x = area.X + (area.Width - size.Width) / 2;
            double y = area.Y + (area.Height - size.Height) / 2;
            if (WindowStartupLocation == WindowStartupLocation.CenterOwner && _ownerWindow.WindowState == WindowState.Normal)
            {
                x = owner.DesktopPosition.X + (owner.OuterSize.Width * owner.DpiScale.DpiScaleX - size.Width) / 2;
                y = owner.DesktopPosition.Y + (owner.OuterSize.Height * owner.DpiScale.DpiScaleY - size.Height) / 2;
                x = Math.Max(area.Left, Math.Min(x, area.Right - size.Width));
                y = Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height));
            }
            restore.Location = new Point(x / dpi.DpiScaleX, y / dpi.DpiScaleY);
            _portableRestoreBounds = restore;
            bool updating = _updateHwndLocation;
            _updateHwndLocation = false;
            try
            {
                SetCurrentValue(LeftProperty, restore.X);
                if (_disposed || _portablePeer == null) return;
                SetCurrentValue(TopProperty, restore.Y);
            }
            finally { _updateHwndLocation = updating; }
            if (_disposed || _portablePeer == null) return;
            PortableQueueBounds(PortableBoundsChanges.Left | PortableBoundsChanges.Top);
            PortableApplyBounds();
        }

        private void PortableInitializeBounds()
        {
            Rect actual = PortableReadBounds();
            _portableNormalFrameSize = PortableFrameSize;
            _portablePendingBounds = PortableBoundsChanges.None;
            _portableBoundsAwaitingAck = PortableBoundsChanges.None;
            if (!double.IsNaN(Width)) _portablePendingBounds |= PortableBoundsChanges.Width;
            if (!double.IsNaN(Height)) _portablePendingBounds |= PortableBoundsChanges.Height;
            if (!double.IsNaN(Left)) _portablePendingBounds |= PortableBoundsChanges.Left;
            if (!double.IsNaN(Top)) _portablePendingBounds |= PortableBoundsChanges.Top;
            _portableInitialSizeBounds = _portablePendingBounds & (PortableBoundsChanges.Width | PortableBoundsChanges.Height);
            _portableRestoreBounds = new Rect(double.IsNaN(Left) ? actual.X : Left, double.IsNaN(Top) ? actual.Y : Top,
                double.IsNaN(Width) ? actual.Width : Width, double.IsNaN(Height) ? actual.Height : Height);
            _portableStateRequest = WindowState == _portablePeer.WindowState ? null : WindowState;
            PortablePublishLocation(actual.TopLeft, false);
        }

        private void PortableUpdateBounds(PortableBoundsChanges changes = PortableBoundsChanges.None)
        {
            ValidateLengthForHeightWidth(Width);
            ValidateLengthForHeightWidth(Height);
            ValidateLengthForHeightWidth(MinWidth);
            ValidateLengthForHeightWidth(MinHeight);
            ValidateLengthForHeightWidth(MaxWidth);
            ValidateLengthForHeightWidth(MaxHeight);
            if (_portablePeer == null || _portableUpdatingMetrics) return;
            Rect restore = _portableRestoreBounds;
            double width = (changes & PortableBoundsChanges.Width) != 0 && !double.IsNaN(Width) ? Width : restore.Width;
            double height = (changes & PortableBoundsChanges.Height) != 0 && !double.IsNaN(Height) ? Height : restore.Height;
            if (width != restore.Width) PortableQueueBounds(PortableBoundsChanges.Width);
            if (height != restore.Height) PortableQueueBounds(PortableBoundsChanges.Height);
            restore.Size = new Size(width, height);
            _portableRestoreBounds = restore;
            if ((_portablePeer.WindowState == WindowState.Normal && _portableStateRequest == null) ||
                (!_portableHasBeenShown && WindowState == WindowState.Normal))
                PortableClampRestoreBounds();
            PortableUpdateConstraints();
            if (!_portableHasBeenShown || (_portablePeer.WindowState == WindowState.Normal &&
                _portableStateRequest == null))
                PortableApplyBounds();
        }

        private void PortableClampRestoreBounds(PortableBoundsChanges axes = PortableBoundsChanges.Width | PortableBoundsChanges.Height)
        {
            Rect restore = _portableRestoreBounds;
            double width = (axes & PortableBoundsChanges.Width) != 0 ? Math.Max(MinWidth, Math.Min(restore.Width, Math.Max(MinWidth, MaxWidth))) : restore.Width;
            double height = (axes & PortableBoundsChanges.Height) != 0 ? Math.Max(MinHeight, Math.Min(restore.Height, Math.Max(MinHeight, MaxHeight))) : restore.Height;
            if (width != restore.Width) PortableQueueBounds(PortableBoundsChanges.Width);
            if (height != restore.Height) PortableQueueBounds(PortableBoundsChanges.Height);
            restore.Size = new Size(width, height);
            _portableRestoreBounds = restore;
        }

        private void PortableQueueBounds(PortableBoundsChanges changes)
        {
            _portablePendingBounds |= changes;
            _portableBoundsAwaitingAck &= ~changes;
            if (_portableHasBeenShown) _portableInitialSizeBounds &= ~changes;
            else _portableInitialSizeBounds |= changes & (PortableBoundsChanges.Width | PortableBoundsChanges.Height);
        }

        private static bool PortablePixelRequestMatches(double actual, double requested)
        {
            return MS.Internal.DoubleUtil.AreClose(actual, Math.Floor(requested)) ||
                MS.Internal.DoubleUtil.AreClose(actual, Math.Ceiling(requested)) ||
                (requested == 0 && MS.Internal.DoubleUtil.AreClose(actual, 1));
        }

        private void PortableAcknowledgeBounds()
        {
            DpiScale dpi = _portablePeer.DpiScale;
            Size client = _portablePeer.ClientSize;
            Point position = _portablePeer.DesktopPosition;
            PortableBoundsChanges acknowledged = PortableBoundsChanges.None;
            if (PortablePixelRequestMatches(client.Width * dpi.DpiScaleX, _portableRequestedClientPixels.Width)) acknowledged |= PortableBoundsChanges.Width;
            if (PortablePixelRequestMatches(client.Height * dpi.DpiScaleY, _portableRequestedClientPixels.Height)) acknowledged |= PortableBoundsChanges.Height;
            if (_isVisible && MS.Internal.DoubleUtil.AreClose(position.X, _portableRequestedDesktopPosition.X)) acknowledged |= PortableBoundsChanges.Left;
            if (_isVisible && MS.Internal.DoubleUtil.AreClose(position.Y, _portableRequestedDesktopPosition.Y)) acknowledged |= PortableBoundsChanges.Top;
            acknowledged &= _portableBoundsAwaitingAck;
            _portablePendingBounds &= ~acknowledged;
            _portableBoundsAwaitingAck &= ~acknowledged;
            _portableInitialSizeBounds &= ~acknowledged;
        }

        private void PortableUpdateConstraints()
        {
            Size frame = _portableNormalFrameSize;
            _portablePeer.SetClientSizeConstraints(
                new Size(Math.Max(0, MinWidth - frame.Width), Math.Max(0, MinHeight - frame.Height)),
                new Size(Math.Max(0, Math.Max(MinWidth, MaxWidth) - frame.Width), Math.Max(0, Math.Max(MinHeight, MaxHeight) - frame.Height)));
        }

        private void PortableUpdatePosition(PortableBoundsChanges change, double value)
        {
            ValidateTopLeft(value);
            if (_portablePeer == null || _portableUpdatingMetrics || double.IsNaN(value)) return;
            Rect restore = _portableRestoreBounds;
            if (change == PortableBoundsChanges.Left) restore.X = value;
            else restore.Y = value;
            _portableRestoreBounds = restore;
            PortableQueueBounds(change);
            if (!_portableHasBeenShown || (_portablePeer.WindowState == WindowState.Normal &&
                _portableStateRequest == null))
                PortableApplyBounds();
        }

        private void PortableApplyBounds()
        {
            PortableBoundsChanges changes = _portablePendingBounds & ~_portableBoundsAwaitingAck;
            if (changes == PortableBoundsChanges.None) return;
            Rect target = _portableRestoreBounds;
            _portableApplyingBounds = true;
            _portableMetricsDuringBoundsApply = false;
            try
            {
                if ((changes & (PortableBoundsChanges.Left | PortableBoundsChanges.Top)) != 0)
                {
                    Point position = PortableToDesktop(target.TopLeft);
                    _portableRequestedDesktopPosition = position;
                    _portableRequestedBoundsDpi = _portablePeer.DpiScale;
                    _portableBoundsAwaitingAck |= _portablePendingBounds & (PortableBoundsChanges.Left | PortableBoundsChanges.Top);
                    _portablePeer.SetDesktopPosition(position);
                }
                if ((changes & (PortableBoundsChanges.Width | PortableBoundsChanges.Height)) != 0)
                {
                    Size frame = _portableNormalFrameSize;
                    Size client = new Size(Math.Max(0, target.Width - frame.Width), Math.Max(0, target.Height - frame.Height));
                    DpiScale dpi = _portablePeer.DpiScale;
                    _portableRequestedClientPixels = new Size(client.Width * dpi.DpiScaleX, client.Height * dpi.DpiScaleY);
                    _portableRequestedBoundsDpi = dpi;
                    _portableBoundsAwaitingAck |= _portablePendingBounds & (PortableBoundsChanges.Width | PortableBoundsChanges.Height);
                    _portablePeer.SetClientSize(client);
                }
            }
            finally { _portableApplyingBounds = false; }
            if (_portableMetricsDuringBoundsApply) PortableOnMetricsChanged();
        }

        private void PortablePublishLocation(Point logical, bool notify)
        {
            bool changed = !MS.Internal.DoubleUtil.AreClose(_actualLeft, logical.X) ||
                !MS.Internal.DoubleUtil.AreClose(_actualTop, logical.Y);
            _actualLeft = logical.X;
            _actualTop = logical.Y;
            if (!notify)
            {
                bool updating = _updateHwndLocation;
                _updateHwndLocation = false;
                try { CoerceValue(LeftProperty); CoerceValue(TopProperty); }
                finally { _updateHwndLocation = updating; }
            }
            else if (changed && _portablePeer.WindowState == WindowState.Normal)
            {
                _portablePendingLocationNotification = true;
                if (WindowState == WindowState.Normal)
                {
                    _portablePendingLocationNotification = false;
                    WmMoveChangedHelper();
                }
            }
        }

        private void PortableOnMetricsChanged()
        {
            VerifyAccess();
            if (_disposed || _portablePeer == null || _portableUpdatingMetrics) return;
            if (_portableApplyingBounds) { _portableMetricsDuringBoundsApply = true; return; }
            Rect actual = PortableReadBounds();
            _portableLastMetricsState = _portablePeer.WindowState;
            if (_portablePeer.WindowState == WindowState.Normal &&
                (_portableStateRequest == null || _portableStateRequest == WindowState.Normal))
            {
                Size frame = PortableFrameSize;
                if (frame != _portableNormalFrameSize)
                {
                    DpiScale dpi = _portablePeer.DpiScale;
                    Size client = _portablePeer.ClientSize;
                    PortableBoundsChanges changes = PortableBoundsChanges.None;
                    if (frame.Width != _portableNormalFrameSize.Width &&
                        ((_portablePendingBounds & PortableBoundsChanges.Width) != 0 ||
                        ((SizeToContent == SizeToContent.Manual || SizeToContent == SizeToContent.Height) &&
                        PortablePixelRequestMatches(client.Width * dpi.DpiScaleX,
                            Math.Max(0, _portableRestoreBounds.Width - _portableNormalFrameSize.Width) * dpi.DpiScaleX))))
                        changes |= PortableBoundsChanges.Width;
                    if (frame.Height != _portableNormalFrameSize.Height &&
                        ((_portablePendingBounds & PortableBoundsChanges.Height) != 0 ||
                        ((SizeToContent == SizeToContent.Manual || SizeToContent == SizeToContent.Width) &&
                        PortablePixelRequestMatches(client.Height * dpi.DpiScaleY,
                            Math.Max(0, _portableRestoreBounds.Height - _portableNormalFrameSize.Height) * dpi.DpiScaleY))))
                        changes |= PortableBoundsChanges.Height;
                    PortableQueueBounds(changes);
                    _portableNormalFrameSize = frame;
                    InvalidateMeasure();
                    PortableUpdateConstraints();
                }
                if (_portableBoundsAwaitingAck != PortableBoundsChanges.None && !_portableRequestedBoundsDpi.Equals(_portablePeer.DpiScale))
                    _portableBoundsAwaitingAck = PortableBoundsChanges.None;
                if ((_portablePendingBounds & (PortableBoundsChanges.Width | PortableBoundsChanges.Height)) != 0)
                    PortableClampRestoreBounds(_portablePendingBounds);
                if (_portableHasBeenShown) PortableAcknowledgeBounds();
                Rect restore = _portableRestoreBounds;
                if ((_portablePendingBounds & PortableBoundsChanges.Width) == 0) restore.Width = actual.Width;
                if ((_portablePendingBounds & PortableBoundsChanges.Height) == 0) restore.Height = actual.Height;
                if ((_isVisible || !_portableHasBeenShown) && (_portablePendingBounds & PortableBoundsChanges.Left) == 0) restore.X = actual.X;
                if ((_isVisible || !_portableHasBeenShown) && (_portablePendingBounds & PortableBoundsChanges.Top) == 0) restore.Y = actual.Y;
                _portableRestoreBounds = restore;
                if (_portableHasBeenShown)
                {
                    _portableStateRequest = null;
                    if ((_portablePendingBounds & ~_portableBoundsAwaitingAck) != PortableBoundsChanges.None)
                    {
                        PortableApplyBounds();
                        if (_disposed || _portablePeer == null) return;
                        actual = PortableReadBounds();
                    }
                }
            }
            else if (_portableStateRequest == _portablePeer.WindowState)
                _portableStateRequest = null;
            _portableUpdatingMetrics = true;
            try
            {
                bool restoreDimensions = _portableUseRestoreDimensions && _portablePeer.WindowState != WindowState.Normal;
                PortableBoundsChanges dimensionIntent = _portablePendingBounds;
                if (_portablePeer.WindowState != WindowState.Normal) dimensionIntent &= ~_portableInitialSizeBounds;
                SetValue(WidthProperty, restoreDimensions || (dimensionIntent & PortableBoundsChanges.Width) != 0 ? _portableRestoreBounds.Width : actual.Width);
                if (_disposed || _portablePeer == null) return;
                SetValue(HeightProperty, restoreDimensions || (dimensionIntent & PortableBoundsChanges.Height) != 0 ? _portableRestoreBounds.Height : actual.Height);
            }
            finally { _portableUpdatingMetrics = false; }
            if (!_disposed && _portablePeer != null && (_isVisible || !_portableHasBeenShown)) PortablePublishLocation(actual.TopLeft, true);
        }

        private void PortableRequestState(WindowState state)
        {
            if (_portableUpdatingMetrics) return;
            if (_portablePeer == null) { _previousWindowState = state; return; }
            _portableUseRestoreDimensions = false;
            _portableStateRequest = state;
            _portablePeer.SetWindowState(state);
        }

        private void PortableOnStateChanged(WindowState state)
        {
            VerifyAccess();
            if (_disposed || _previousWindowState == state) return;
            _portableUseRestoreDimensions = state != WindowState.Normal && WindowState != state;
            if (state == WindowState.Normal && _portableLastMetricsState != WindowState.Normal)
                _portableStateRequest = WindowState.Normal;
            bool notifyLocation = false;
            _portableUpdatingMetrics = true;
            try
            {
                if (_portableUseRestoreDimensions) UpdateDimensionsToRestoreBounds();
                SetValue(WindowStateProperty, state);
                _previousWindowState = state;
                if (state == WindowState.Normal && _portableLastMetricsState == WindowState.Normal && _portablePendingLocationNotification)
                {
                    _portablePendingLocationNotification = false;
                    notifyLocation = true;
                }
            }
            finally { _portableUpdatingMetrics = false; }
            if (notifyLocation) WmMoveChangedHelper();
            OnStateChanged(EventArgs.Empty);
        }

        private void PortableOnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!_portableModal) _portablePeer?.SetEnabled((bool)args.NewValue);
        }

        private object PortableShowHelper(bool visible)
        {
            if (_disposed || _isVisible == visible) return null;
            _isClosing = false;
            if (visible)
            {
                if (Application.IsShuttingDown) return null;
                PortableCreateSource();
                if (_disposed || _portablePeer == null) return null;
                PortableLayoutRoot(_portablePeer.ClientSize);
                if (_disposed || _portablePeer == null) return null;
                if (Visibility != Visibility.Visible)
                {
                    PortableEndDialog();
                    return null;
                }
                if (!_portableHasBeenShown) PortableApplyStartupLocation();
                if (_disposed || _portablePeer == null) return null;
                if (Visibility != Visibility.Visible)
                {
                    PortableEndDialog();
                    return null;
                }
                SetShowKeyboardCueState();
                _isVisible = true;
                _portableHasBeenShown = true;
                try { _portablePeer.Show(ShowActivated); }
                catch
                {
                    _isVisible = false;
                    try { _portablePeer?.Hide(); }
                    catch { }
                    UpdateVisibilityProperty(Visibility.Collapsed);
                    throw;
                }
            }
            else
            {
                _isVisible = false;
                ClearShowKeyboardCueState();
                PortableEndDialog();
                _portablePeer?.Hide();
            }
            return null;
        }

        private void PortableClose(bool shutdown, bool ignoreCancel)
        {
            VerifyNotClosing();
            if (_disposed) return;
            _appShuttingDown = shutdown;
            _ignoreCancel = ignoreCancel;
            _isClosing = true;
            var args = new CancelEventArgs();
            try { OnClosing(args); }
            catch
            {
                try { PortableFinishClose(); }
                catch { }
                throw;
            }
            if (!ShouldCloseWindow(args.Cancel))
            {
                _isClosing = false;
                _dialogResult = null;
                return;
            }
            PortableFinishClose();
        }

        private void PortableFinishClose()
        {
            PortableEndDialog();
            _isVisible = false;
            if (_portableSource != null) _portableSource.RootVisual = null;
            try
            {
                if (_portablePeer == null) PortableOnClosed();
                else
                {
                    _portablePeer.Close();
                    if (!_disposed)
                        throw new InvalidOperationException("The portable window peer did not acknowledge Close synchronously.");
                }
            }
            catch
            {
                if (!_disposed)
                {
                    // Preserve the failed close while still releasing the managed source and owned peer.
                    try { InternalDispose(); }
                    catch { }
                }
                throw;
            }
        }

        private void PortableOnCloseRequested()
        {
            VerifyAccess();
            InternalClose(false, false);
        }

        private void PortableOnClosed()
        {
            VerifyAccess();
            if (_disposed) return;
            PortableEndDialog();
            _isVisible = false;
            InternalDispose();
            OnClosed(EventArgs.Empty);
        }

        private void PortableReleasePeer()
        {
            IPortableWindowPeer peer = _portablePeer;
            _portablePeer = null;
            PortablePresentationSource source = _portableSource;
            _portableSource = null;
            IsEnabledChanged -= PortableOnIsEnabledChanged;
            LayoutUpdated -= PortableOnLayoutUpdated;
            if (peer != null)
            {
                peer.CloseRequested -= PortableOnCloseRequested;
                peer.Closed -= PortableOnClosed;
                peer.ActivationChanged -= HandleActivate;
                peer.StateChanged -= PortableOnStateChanged;
                peer.MetricsChanged -= PortableOnMetricsChanged;
            }
            try { source?.Dispose(); }
            finally { peer?.Dispose(); }
        }

        internal bool? PortableShowMessageBox() => PortableShowDialog(messageBox: true);

        private bool? PortableShowDialog(bool messageBox = false)
        {
            VerifyApiSupported();
            VerifyContextAndObjectState();
            VerifyCanShow();
            VerifyNotClosing();
            VerifyConsistencyWithAllowsTransparency();
            if (_isVisible) throw new InvalidOperationException(SR.ShowDialogOnVisible);
            if (_showingAsDialog) throw new InvalidOperationException(SR.ShowDialogOnModal);
            EnsureDialogCommand();
            var disabled = new List<KeyValuePair<Window, bool>>();
            Window previousActive = null;
            IInputElement previousFocus = messageBox ? Keyboard.FocusedElement : null;
            bool previousOwnerModal = messageBox && _ownerWindow != null && _ownerWindow._portableModal;
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source.Dispatcher == Dispatcher && source.RootVisual is Window window && window != this && !window._disposed && window._portablePeer != null)
                {
                    if (window._portablePeer.IsActive) previousActive = window;
                    if ((!messageBox || ReferenceEquals(window, _ownerWindow)) && window._portablePeer.IsVisible && window._portablePeer.IsEnabled)
                        disabled.Add(new KeyValuePair<Window, bool>(window, true));
                }
            }
            bool pushed = false;
            bool bodyFailed = false;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            try
            {
                foreach (var entry in disabled)
                {
                    Window window = entry.Key;
                    IPortableWindowPeer peer = window._portablePeer;
                    if (window._disposed || peer == null) continue;
                    window._portableModal = true;
                    peer.ReleaseMouseCapture();
                    if (window._disposed || !ReferenceEquals(peer, window._portablePeer))
                    {
                        window._portableModal = previousOwnerModal;
                        continue;
                    }
                    peer.SetEnabled(false);
                }
                if (messageBox && _disposed) return false;
                _showingAsDialog = true;
                Show();
                if (_showingAsDialog && _isVisible && !_disposed)
                {
                    if (!messageBox)
                    {
                        pushed = true;
                        ComponentDispatcher.PushModal();
                    }
                    if (_showingAsDialog && _isVisible && !_disposed)
                    {
                        _dispatcherFrame = new DispatcherFrame();
                        Dispatcher.PushFrame(_dispatcherFrame);
                    }
                }
                return _dialogResult ?? false;
            }
            catch (Exception exception)
            {
                bodyFailed = true;
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                try { if (!_disposed && _isVisible) Hide(); }
                catch { }
                throw;
            }
            finally
            {
                PortableEndDialog();
                try { if (pushed) ComponentDispatcher.PopModal(); }
                catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
                foreach (var entry in disabled)
                {
                    bool restore = entry.Key._portableModal;
                    entry.Key._portableModal = previousOwnerModal;
                    if (restore && !entry.Key._disposed && entry.Key._portablePeer != null)
                    {
                        try { entry.Key._portablePeer.SetEnabled(entry.Value && entry.Key.IsEnabled); }
                        catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
                    }
                }
                if (previousActive != null && !previousActive._disposed && previousActive._portablePeer?.IsVisible == true && previousActive._portablePeer.IsEnabled)
                {
                    try
                    {
                        previousActive._portablePeer.Activate();
                        if (messageBox && previousFocus is DependencyObject element && Window.GetWindow(element) == previousActive)
                            Keyboard.Focus(previousFocus);
                    }
                    catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
                }
                if (!bodyFailed) failure?.Throw();
            }
        }

        private void PortableEndDialog()
        {
            if (!_showingAsDialog) return;
            _showingAsDialog = false;
            _dialogResult ??= false;
            if (_dispatcherFrame != null)
            {
                _dispatcherFrame.Continue = false;
                _dispatcherFrame = null;
            }
        }

        internal IPortableWindowPeer PortableBeginFileDialog()
        {
            VerifyContextAndObjectState();
            IPortableWindowPeer peer = _portablePeer;
            if (_disposed || !_isVisible || peer == null || !peer.IsVisible)
                throw new InvalidOperationException("A portable file dialog requires a live visible native owner.");
            if (_portableModal || !peer.IsEnabled)
                throw new InvalidOperationException("The native file dialog owner is already disabled or modal.");
            _portableModal = true;
            try
            {
                peer.ReleaseMouseCapture();
                if (!PortableOwnsDialogPeer(peer)) throw new OperationCanceledException("The file dialog owner closed while entering modal state.");
                peer.SetEnabled(false);
                if (!PortableOwnsDialogPeer(peer)) throw new OperationCanceledException("The file dialog owner closed while entering modal state.");
                return peer;
            }
            catch
            {
                PortableEndFileDialog(peer, false);
                throw;
            }
        }

        internal bool PortableOwnsDialogPeer(IPortableWindowPeer peer) =>
            !_disposed && _isVisible && ReferenceEquals(_portablePeer, peer) && peer.IsVisible;

        internal void PortableEndFileDialog(IPortableWindowPeer peer, bool activate)
        {
            VerifyAccess();
            bool restore = _portableModal;
            _portableModal = false;
            if (!restore || _disposed || !ReferenceEquals(_portablePeer, peer)) return;
            peer.SetEnabled(IsEnabled);
            if (PortableOwnsDialogPeer(peer) && activate && peer.IsEnabled) peer.Activate();
        }
    }
}
#endif
