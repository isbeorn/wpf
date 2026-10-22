// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal sealed class PortableRenderTarget : IPortableRenderTarget, IDisposable
    {
        private SKSurface _surface;
        private readonly SKColorFilter _linearSamplingPrecision;
        private DpiScale _dpiScale;
        private bool _disposed;
        private readonly HashSet<Visual> _brushSources = new();
        private readonly HashSet<Visual> _activeVisuals = new();
        private HashSet<Visual> _frameSources;

        public Visual RootVisual { get; }
        internal event Action FrameRendered;

        internal PortableRenderTarget(Visual rootVisual, int width, int height)
            : this(rootVisual, width, height, new DpiScale(1, 1))
        {
        }

        internal PortableRenderTarget(Visual rootVisual, int width, int height, DpiScale dpiScale)
        {
            ArgumentNullException.ThrowIfNull(rootVisual);
            rootVisual.Dispatcher.VerifyAccess();
            ValidateDpi(dpiScale);
            RootVisual = rootVisual;
            _surface = CreateSurface(width, height);
            _dpiScale = dpiScale;
            try
            {
                // Skia 3.119.4's color-filter shader uses raster stages instead of its 4-bit legacy sampler.
                _linearSamplingPrecision = SKColorFilter.CreateColorMatrix(new float[]
                {
                    1, 0, 0, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 0, 1, 0
                }) ?? throw new InvalidOperationException("The portable linear sampling precision filter could not be allocated.");
                MediaContext.From(rootVisual.Dispatcher).RegisterPortableRenderTarget(this);
            }
            catch
            {
                _surface.Dispose();
                _linearSamplingPrecision?.Dispose();
                throw;
            }
        }

        public void Render()
        {
            VerifyAccess();
            if (_frameSources != null) throw new InvalidOperationException("A portable render target cannot render recursively.");
            _frameSources = new HashSet<Visual>();
            SKCanvas canvas = _surface.Canvas;
            int saved = canvas.Save();
            try
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)_dpiScale.DpiScaleX, (float)_dpiScale.DpiScaleY);
                var walker = new SkiaDrawingContextWalker(canvas, _linearSamplingPrecision, this);
                WalkVisual(RootVisual, walker);
                canvas.Flush();
                foreach (Visual obsolete in new List<Visual>(_brushSources))
                    if (!_frameSources.Contains(obsolete))
                    {
                        _brushSources.Remove(obsolete);
                        MediaContext.From(RootVisual.Dispatcher).UnregisterPortableBrushSource(obsolete);
                    }
            }
            finally
            {
                canvas.RestoreToCount(saved);
                // Keep both old and newly encountered sources after failure so fixing either can retry the frame.
                _frameSources = null;
            }
            // Handlers may dispose or resize the target, so raster state must not be used after notification.
            FrameRendered?.Invoke();
        }

        internal void Resize(int width, int height) => Resize(width, height, _dpiScale);

        internal void Resize(int width, int height, DpiScale dpiScale)
        {
            VerifyAccess();
            ValidateDpi(dpiScale);
            SKSurface replacement = CreateSurface(width, height);
            _surface.Dispose();
            _surface = replacement;
            _dpiScale = dpiScale;
            Invalidate();
        }

        internal void Invalidate()
        {
            VerifyAccess();
            MediaContext.From(RootVisual.Dispatcher).PostRender();
        }

        internal SKBitmap Snapshot()
        {
            VerifyAccess();
            using (SKImage image = _surface.Snapshot())
                return SKBitmap.FromImage(image);
        }

        public void Dispose()
        {
            RootVisual.Dispatcher.VerifyAccess();
            if (_disposed)
                return;
            FrameRendered = null;
            MediaContext.From(RootVisual.Dispatcher).UnregisterPortableRenderTarget(this);
            foreach (Visual source in _brushSources)
                MediaContext.From(RootVisual.Dispatcher).UnregisterPortableBrushSource(source);
            _brushSources.Clear();
            _disposed = true;
            _surface.Dispose();
            _linearSamplingPrecision.Dispose();
        }

        private void VerifyAccess()
        {
            RootVisual.Dispatcher.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static SKSurface CreateSurface(int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            return SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("The portable CPU raster surface could not be allocated.");
        }

        private static void ValidateDpi(DpiScale dpiScale)
        {
            if (!double.IsFinite(dpiScale.DpiScaleX) || !double.IsFinite(dpiScale.DpiScaleY) ||
                !float.IsFinite((float)dpiScale.DpiScaleX) || !float.IsFinite((float)dpiScale.DpiScaleY) ||
                (float)dpiScale.DpiScaleX <= 0 || (float)dpiScale.DpiScaleY <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scales must be finite positive values representable by the raster backend.");
        }

        internal void RegisterBrushSource(Visual visual)
        {
            VerifyAccess();
            visual.Dispatcher.VerifyAccess();
            if (visual.Dispatcher != RootVisual.Dispatcher)
                throw new InvalidOperationException("VisualBrush content must belong to the render target's dispatcher.");
            _frameSources.Add(visual);
            if (_brushSources.Add(visual))
                MediaContext.From(RootVisual.Dispatcher).RegisterPortableBrushSource(visual);
        }

        internal void WalkVisual(Visual visual, SkiaDrawingContextWalker walker)
        {
            if (visual.VisualOpacity == 0)
                return;
            if (!_activeVisuals.Add(visual))
                throw new NotSupportedException("Portable rendering does not support recursive VisualBrush graphs.");
            var guidelines = walker.CaptureGuidelines();
            BitmapScalingMode bitmapScalingMode = walker.PushBitmapScalingMode(visual.VisualBitmapScalingMode);
            int saved = walker.Canvas.Save();
            try
            {
                Effect effect = visual.VisualEffect;
                if ((effect != null && !(effect is BlurEffect)) || visual.VisualBitmapEffect != null)
                    throw new NotSupportedException("Portable rendering does not support visual effects.");
                if (visual.VisualOpacityMask != null)
                    throw new NotSupportedException("Portable rendering does not support a visual opacity mask.");

                walker.Canvas.Translate((float)visual.VisualOffset.X, (float)visual.VisualOffset.Y);
                if (visual.VisualTransform != null)
                    walker.PushTransform(visual.VisualTransform);
                if (visual.VisualClip != null)
                    walker.PushClip(visual.VisualClip);
                if (effect is BlurEffect blur)
                    walker.PushBlur(blur);
                if (visual.VisualOpacity != 1)
                    walker.PushOpacity(visual.VisualOpacity);
                walker.SetVisualGuidelines(visual);

                if (visual is DrawingVisual drawingVisual)
                    drawingVisual.WalkContent(walker);
                else if (visual is UIElement element)
                    element.WalkContent(walker);
                else if (!(visual is ContainerVisual) || visual is HostVisual)
                    throw new NotSupportedException($"Portable rendering does not support visual content of type {visual.GetType().FullName}.");

                int children = visual.InternalVisualChildrenCount;
                for (int index = 0; index < children; index++)
                    WalkVisual(visual.InternalGetVisualChild(index), walker);
            }
            finally
            {
                walker.Canvas.RestoreToCount(saved);
                walker.RestoreGuidelines(guidelines);
                walker.RestoreBitmapScalingMode(bitmapScalingMode);
                _activeVisuals.Remove(visual);
            }
        }
    }
}
#endif
