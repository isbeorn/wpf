// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal sealed class PortableRenderTarget : IPortableRenderTarget, IDisposable
    {
        private SKSurface _surface;
        private bool _disposed;

        public Visual RootVisual { get; }

        internal PortableRenderTarget(Visual rootVisual, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(rootVisual);
            rootVisual.Dispatcher.VerifyAccess();
            RootVisual = rootVisual;
            _surface = CreateSurface(width, height);
            try
            {
                MediaContext.From(rootVisual.Dispatcher).RegisterPortableRenderTarget(this);
            }
            catch
            {
                _surface.Dispose();
                throw;
            }
        }

        public void Render()
        {
            VerifyAccess();
            SKCanvas canvas = _surface.Canvas;
            int saved = canvas.Save();
            try
            {
                canvas.Clear(SKColors.Transparent);
                var walker = new SkiaDrawingContextWalker(canvas);
                WalkVisual(RootVisual, walker);
                canvas.Flush();
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        internal void Resize(int width, int height)
        {
            VerifyAccess();
            SKSurface replacement = CreateSurface(width, height);
            _surface.Dispose();
            _surface = replacement;
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
            MediaContext.From(RootVisual.Dispatcher).UnregisterPortableRenderTarget(this);
            _disposed = true;
            _surface.Dispose();
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

        private static void WalkVisual(Visual visual, SkiaDrawingContextWalker walker)
        {
            int saved = walker.Canvas.Save();
            try
            {
                if (visual.VisualEffect != null || visual.VisualBitmapEffect != null)
                    throw new NotSupportedException("Portable rendering does not support visual effects.");
                if (visual.VisualOpacityMask != null)
                    throw new NotSupportedException("Portable rendering does not support a visual opacity mask.");

                walker.Canvas.Translate((float)visual.VisualOffset.X, (float)visual.VisualOffset.Y);
                if (visual.VisualTransform != null)
                    walker.PushTransform(visual.VisualTransform);
                if (visual.VisualClip != null)
                    walker.PushClip(visual.VisualClip);
                if (visual.VisualOpacity != 1)
                    walker.PushOpacity(visual.VisualOpacity);

                if (visual is DrawingVisual drawingVisual)
                    drawingVisual.WalkContent(walker);
                else if (visual.GetType() != typeof(ContainerVisual))
                    throw new NotSupportedException($"Portable rendering does not support visual content of type {visual.GetType().FullName}.");

                int children = visual.InternalVisualChildrenCount;
                for (int index = 0; index < children; index++)
                    WalkVisual(visual.InternalGetVisualChild(index), walker);
            }
            finally
            {
                walker.Canvas.RestoreToCount(saved);
            }
        }
    }
}
#endif
