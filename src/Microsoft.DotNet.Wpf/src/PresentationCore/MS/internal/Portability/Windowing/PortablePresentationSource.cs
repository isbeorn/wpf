// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MS.Internal.Portability.Rendering;
using MS.Internal.Portability.Input;

namespace MS.Internal.Portability.Windowing
{
    internal sealed class PortablePresentationSource : PresentationSource, IDisposable
    {
        private readonly Action<Size> _layoutRoot;
        private IPortablePresentationHost _host;
        private PortableCompositionTarget _compositionTarget;
        private PortableRenderTarget _rasterTarget;
        private PortableInputProvider _inputProvider;
        private Visual _publishedRoot;
        private HostMetrics _metrics;
        private bool _isDisposed;
        private bool _isDisposing;
        private bool _layoutRequested;
        private long _rootChangeGeneration;

        internal PortablePresentationSource(Action<Size> layoutRoot)
        {
            ArgumentNullException.ThrowIfNull(layoutRoot);
            _layoutRoot = layoutRoot;
        }

        internal void AttachHost(IPortablePresentationHost host)
        {
            VerifyAccess();
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentNullException.ThrowIfNull(host);
            if (Dispatcher.HasShutdownStarted)
                throw new InvalidOperationException("The dispatcher is shutting down.");
            if (_host != null) throw new InvalidOperationException("A presentation host is already attached.");

            HostMetrics metrics = HostMetrics.Read(host);
            var compositionTarget = new PortableCompositionTarget(this, metrics.DpiScale);
            _host = host;
            _compositionTarget = compositionTarget;
            _metrics = metrics;
            bool added = false;
            try
            {
                host.MetricsChanged += OnMetricsChanged;
                host.Exposed += OnExposed;
                host.Closed += OnClosed;
                host.RawInput += OnRawInput;
                AddSource();
                added = true;
                Dispatcher.ShutdownStarted += OnShutdown;
            }
            catch
            {
                host.MetricsChanged -= OnMetricsChanged;
                host.Exposed -= OnExposed;
                host.Closed -= OnClosed;
                host.RawInput -= OnRawInput;
                if (added) RemoveSource();
                compositionTarget.DisposeDirect();
                _host = null;
                _compositionTarget = null;
                throw;
            }
        }

        public override Visual RootVisual
        {
            get
            {
                VerifyAccess();
                return _compositionTarget?.RootVisual;
            }
            set
            {
                VerifyAttached();
                Visual oldRoot = _compositionTarget.RootVisual;
                if (ReferenceEquals(oldRoot, value)) return;

                PortableCompositionTarget compositionTarget = _compositionTarget;
                long previousGeneration = _rootChangeGeneration;
                compositionTarget.SetRootVisualDirect(value);
                long generation = ++_rootChangeGeneration;
                PortableRenderTarget replacement = null;
                try
                {
                    if (value != null) VisualTreeHelper.SetRootDpi(value, _metrics.DpiScale);
                    if (!IsCurrentRootChange(generation, compositionTarget)) return;
                    replacement = CreateRasterTarget(value, _metrics);
                }
                catch
                {
                    replacement?.Dispose();
                    if (IsCurrentRootChange(generation, compositionTarget))
                    {
                        if (oldRoot == null || (!oldRoot.IsRootElement && VisualTreeHelper.GetParent(oldRoot) == null))
                        {
                            compositionTarget.SetRootVisualDirect(oldRoot);
                            _rootChangeGeneration = previousGeneration;
                        }
                        else
                        {
                            compositionTarget.SetRootVisualDirect(null);
                            _rasterTarget?.Dispose();
                            _rasterTarget = null;
                            Visual abandonedPublishedRoot = IsRootSourceOf(_publishedRoot) ? _publishedRoot : null;
                            _publishedRoot = null;
                            if (abandonedPublishedRoot != null) RootChanged(abandonedPublishedRoot, null);
                        }
                    }
                    throw;
                }

                if (!IsCurrentRootChange(generation, compositionTarget))
                {
                    replacement?.Dispose();
                    return;
                }

                _rasterTarget?.Dispose();
                _rasterTarget = replacement;
                RequestLayout();
                Visual oldPublishedRoot = IsRootSourceOf(_publishedRoot) ? _publishedRoot : null;
                _publishedRoot = value;
                RootChanged(oldPublishedRoot, value);
            }
        }

        public override bool IsDisposed => _isDisposed;

        protected override CompositionTarget GetCompositionTargetCore()
        {
            VerifyAccess();
            return _compositionTarget;
        }

        public void Dispose()
        {
            VerifyAccess();
            if (_isDisposed || _isDisposing) return;
            _isDisposing = true;
            ++_rootChangeGeneration;
            try
            {
                try
                {
                    _inputProvider?.Dispose();
                }
                finally
                {
                    _inputProvider = null;
                    DisposeCore();
                }
            }
            finally
            {
                _isDisposing = false;
            }
        }

        private void DisposeCore()
        {
            _isDisposed = true;
            Dispatcher.ShutdownStarted -= OnShutdown;
            if (_host != null)
            {
                _host.MetricsChanged -= OnMetricsChanged;
                _host.Exposed -= OnExposed;
                _host.Closed -= OnClosed;
                _host.RawInput -= OnRawInput;
            }
            Visual oldRoot = IsRootSourceOf(_publishedRoot) ? _publishedRoot : null;
            _publishedRoot = null;
            _rasterTarget?.Dispose();
            _rasterTarget = null;
            _compositionTarget?.DisposeDirect();
            _compositionTarget = null;
            if (_host != null)
            {
                _host = null;
                RemoveSource();
            }
            try
            {
                RootChanged(oldRoot, null);
            }
            finally
            {
                ClearContentRenderedListeners();
            }
            GC.SuppressFinalize(this);
        }

        internal override IInputProvider GetInputProvider(Type inputDevice)
        {
            if (_isDisposed || _host == null) return null;
            return inputDevice == typeof(MouseDevice) || inputDevice == typeof(KeyboardDevice)
                ? EnsureInputProvider() : null;
        }

        private PortableInputProvider EnsureInputProvider() =>
            _inputProvider ??= new PortableInputProvider(this, _host as IPortableInputHost);

        private PortableRenderTarget CreateRasterTarget(Visual root, HostMetrics metrics)
        {
            if (root == null || metrics.PixelWidth == 0 || metrics.PixelHeight == 0) return null;
            var target = new PortableRenderTarget(root, metrics.PixelWidth, metrics.PixelHeight, metrics.DpiScale);
            target.FrameRendered += OnFrameRendered;
            return target;
        }

        private void OnMetricsChanged()
        {
            VerifyAccess();
            if (_isDisposed || _isDisposing) return;
            VerifyAttached();
            HostMetrics metrics = HostMetrics.Read(_host);
            if (metrics.PixelWidth == 0 || metrics.PixelHeight == 0)
            {
                _rasterTarget?.Dispose();
                _rasterTarget = null;
            }
            else if (_rasterTarget == null)
            {
                _rasterTarget = CreateRasterTarget(RootVisual, metrics);
            }
            else
            {
                _rasterTarget.Resize(metrics.PixelWidth, metrics.PixelHeight, metrics.DpiScale);
            }
            _metrics = metrics;
            _compositionTarget.DpiScale = metrics.DpiScale;
            if (RootVisual != null) VisualTreeHelper.SetRootDpi(RootVisual, metrics.DpiScale);
            RequestLayout();
        }

        private void OnExposed()
        {
            VerifyAccess();
            if (_isDisposed || _isDisposing) return;
            VerifyAttached();
            _rasterTarget?.Invalidate();
        }

        private void OnClosed() => Dispose();

        private void OnShutdown(object sender, EventArgs args) => Dispose();

        private void OnRawInput(InputReport report)
        {
            VerifyAccess();
            if (_isDisposed || (_isDisposing && _inputProvider == null)) return;
            if (_host == null) throw new InvalidOperationException("No presentation host is attached.");
            ArgumentNullException.ThrowIfNull(report);
            if (!ReferenceEquals(report.InputSource, this))
                throw new ArgumentException("The input report belongs to a different presentation source.", nameof(report));
            EnsureInputProvider().ReportInput(report);
        }

        private void OnFrameRendered()
        {
            if (_isDisposed || _rasterTarget == null || _host == null) return;
            PortableRenderTarget renderedTarget = _rasterTarget;
            using (var frame = renderedTarget.Snapshot())
            {
                _host.Present(frame.GetPixelSpan(), frame.Width, frame.Height, frame.RowBytes);
            }
            if (!_isDisposed && ReferenceEquals(_rasterTarget, renderedTarget)) FireContentRendered(this);
        }

        private void RequestLayout()
        {
            if (_layoutRequested || RootVisual == null) return;
            _layoutRequested = true;
            MediaContext.From(Dispatcher).BeginInvokeOnRender(_ =>
            {
                _layoutRequested = false;
                if (!_isDisposed && RootVisual != null) _layoutRoot(_metrics.ClientSize);
                return null;
            }, this);
        }

        private void VerifyAttached()
        {
            VerifyAccess();
            ObjectDisposedException.ThrowIf(_isDisposed || _isDisposing, this);
            if (_host == null) throw new InvalidOperationException("No presentation host is attached.");
        }

        private bool IsCurrentRootChange(long generation, PortableCompositionTarget target) =>
            !_isDisposed && !_isDisposing && generation == _rootChangeGeneration && ReferenceEquals(target, _compositionTarget);

        private readonly struct HostMetrics
        {
            private HostMetrics(Size clientSize, DpiScale dpiScale, int pixelWidth, int pixelHeight)
            {
                ClientSize = clientSize;
                DpiScale = dpiScale;
                PixelWidth = pixelWidth;
                PixelHeight = pixelHeight;
            }

            internal Size ClientSize { get; }
            internal DpiScale DpiScale { get; }
            internal int PixelWidth { get; }
            internal int PixelHeight { get; }

            internal static HostMetrics Read(IPortablePresentationHost host)
            {
                Size size = host.ClientSize;
                DpiScale dpi = host.DpiScale;
                if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width < 0 || size.Height < 0)
                    throw new ArgumentOutOfRangeException(nameof(host), "The client size must be finite and nonnegative.");
                if (!double.IsFinite(dpi.DpiScaleX) || !double.IsFinite(dpi.DpiScaleY) || dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0)
                    throw new ArgumentOutOfRangeException(nameof(host), "The DPI scale must be finite and positive.");
                double width = Math.Ceiling(size.Width * dpi.DpiScaleX);
                double height = Math.Ceiling(size.Height * dpi.DpiScaleY);
                if (width > int.MaxValue || height > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(host), "The physical client size is too large.");
                return new HostMetrics(size, dpi, (int)width, (int)height);
            }
        }

        private sealed class PortableCompositionTarget : CompositionTarget
        {
            private readonly PortablePresentationSource _source;
            internal PortableCompositionTarget(PortablePresentationSource source, DpiScale dpiScale)
            {
                _source = source;
                DpiScale = dpiScale;
            }
            public override Visual RootVisual
            {
                get => base.RootVisual;
                set => _source.RootVisual = value;
            }
            internal void SetRootVisualDirect(Visual visual) => base.RootVisual = visual;
            public override void Dispose() => _source.Dispose();
            internal void DisposeDirect() => base.Dispose();
            internal DpiScale DpiScale { get; set; }
            public override Matrix TransformToDevice
            {
                get
                {
                    VerifyAPIReadOnly();
                    return new Matrix(DpiScale.DpiScaleX, 0, 0, DpiScale.DpiScaleY, 0, 0);
                }
            }
            public override Matrix TransformFromDevice
            {
                get
                {
                    VerifyAPIReadOnly();
                    return new Matrix(1 / DpiScale.DpiScaleX, 0, 0, 1 / DpiScale.DpiScaleY, 0, 0);
                }
            }
        }
    }
}
#endif
