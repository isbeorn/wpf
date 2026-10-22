// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;

namespace MS.Internal.Portability.Windowing
{
    internal sealed class PortablePopupHost : IDisposable
    {
        private readonly PortablePresentationSource _source;
        private IPortablePopupPeer _peer;
        private UIElement _root;
        private readonly Action<Size> _resized;
        private readonly Action _ownerDeactivated;
        private readonly Action _ownerClosing;
        private bool _layout;
        private bool _disposed;
        private Size _desired = Size.Empty;

        internal PortablePopupHost(PresentationSource owner, bool transparent, Action<Size> resized, Action ownerDeactivated, Action ownerClosing)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _resized = resized;
            _ownerDeactivated = ownerDeactivated;
            _ownerClosing = ownerClosing;
            _source = new PortablePresentationSource(LayoutRoot);
            try
            {
                _peer = PortableWindowPlatform.Factory.CreatePopup(_source, owner, transparent);
                _peer.OwnerDeactivated += OnOwnerDeactivated;
                _peer.OwnerClosing += OnOwnerClosing;
                _source.AttachHost(_peer);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool IsAlive => !_disposed && !_source.IsDisposed;
        internal PresentationSource Source => _source;
        internal IPortablePopupPeer Peer => _peer;

        internal void SetRoot(Visual visual)
        {
            UIElement previous = _root;
            if (previous != null)
            {
                previous.LayoutUpdated -= OnLayoutUpdated;
                UIElement.PropagateSuspendLayout(previous);
            }
            _root = null;
            try { _source.RootVisual = visual; }
            catch
            {
                if (!_disposed && ReferenceEquals(_source.RootVisual, previous))
                {
                    _root = previous;
                    if (previous != null)
                    {
                        previous.LayoutUpdated += OnLayoutUpdated;
                        UIElement.PropagateResumeLayout(null, previous);
                    }
                }
                throw;
            }
            if (_disposed)
            {
                if (visual != null) UIElement.PropagateSuspendLayout(visual);
                return;
            }
            _root = visual as UIElement;
            if (_root != null) _root.LayoutUpdated += OnLayoutUpdated;
            if (visual != null) UIElement.PropagateResumeLayout(null, visual);
            if (!_disposed && ReferenceEquals(_source.RootVisual, visual)) LayoutRoot(_peer.ClientSize);
        }
        private void LayoutRoot(Size size)
        {
            if (_disposed || _root == null || _layout) return;
            _layout = true;
            try
            {
                UIElement root = _root;
                root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (_disposed || !ReferenceEquals(_root, root)) return;
                root.Arrange(new Rect(root.DesiredSize));
                if (_disposed || !ReferenceEquals(_root, root)) return;
                PublishDesiredSize();
            }
            finally { _layout = false; }
        }

        private void OnLayoutUpdated(object sender, EventArgs args)
        {
            if (!_layout && !_disposed && _root is { IsMeasureValid: true, IsArrangeValid: true })
                PublishDesiredSize();
        }

        private void PublishDesiredSize()
        {
            Size desired = _root.DesiredSize;
            if (desired == _desired) return;
            _desired = desired;
            DpiScale dpi = _peer.DpiScale;
            // X11 requires nonzero storage even for a logically empty PopupRoot.
            _peer.SetClientSize(new Size(Math.Max(desired.Width, 1 / dpi.DpiScaleX), Math.Max(desired.Height, 1 / dpi.DpiScaleY)));
            if (!_disposed) _resized(desired);
        }

        private void OnOwnerDeactivated() { if (!_disposed) _ownerDeactivated(); }
        private void OnOwnerClosing() { if (!_disposed) _ownerClosing(); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IPortablePopupPeer peer = _peer;
            _peer = null;
            UIElement root = _root;
            if (root != null) root.LayoutUpdated -= OnLayoutUpdated;
            _root = null;
            if (peer != null)
            {
                peer.OwnerDeactivated -= OnOwnerDeactivated;
                peer.OwnerClosing -= OnOwnerClosing;
            }
            try
            {
                if (root != null) UIElement.PropagateSuspendLayout(root);
            }
            finally
            {
                try { _source.Dispose(); }
                finally { peer?.Dispose(); }
            }
        }
    }
}
#endif
