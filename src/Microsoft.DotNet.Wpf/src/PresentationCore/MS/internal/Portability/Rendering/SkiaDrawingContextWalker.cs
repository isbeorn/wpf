// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal sealed class SkiaDrawingContextWalker : DrawingContextWalker
    {
        internal SKCanvas Canvas { get; }
        private BitmapScalingMode _bitmapScalingMode = BitmapScalingMode.Linear;
        private readonly SKColorFilter _linearSamplingPrecision;
        private PortableGuidelineFrame _guidelines = PortableGuidelineFrame.Empty;
        private readonly System.Collections.Generic.Stack<PortableGuidelineFrame> _guidelineStates = new();

        private readonly PortableRenderTarget _target;

        internal SkiaDrawingContextWalker(SKCanvas canvas, SKColorFilter linearSamplingPrecision)
            : this(canvas, linearSamplingPrecision, null) { }

        internal SkiaDrawingContextWalker(SKCanvas canvas, SKColorFilter linearSamplingPrecision, PortableRenderTarget target)
        {
            _target = target;
            Canvas = canvas;
            _linearSamplingPrecision = linearSamplingPrecision;
        }

        internal override BitmapScalingMode PushBitmapScalingMode(BitmapScalingMode mode)
        {
            BitmapScalingMode previous = _bitmapScalingMode;
            if (mode != BitmapScalingMode.Unspecified) _bitmapScalingMode = mode;
            return previous;
        }

        internal override void RestoreBitmapScalingMode(BitmapScalingMode previous) => _bitmapScalingMode = previous;

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
            if (pen == null || pen.Brush == null || pen.Thickness == 0) return;
            if (!_guidelines.IsEmpty)
            {
                using var path = new SKPath();
                path.MoveTo(ToPoint(point0)); path.LineTo(ToPoint(point1));
                using (SKPaint paint = CreatePen(pen)) DrawSnappedPath(path, paint);
                return;
            }
            using (SKPaint paint = CreatePen(pen))
                Canvas.DrawLine(ToPoint(point0), ToPoint(point1), paint);
        }

        public override void DrawRectangle(Brush brush, Pen pen, Rect rectangle)
        {
            if (rectangle.IsEmpty) return;
            if (brush is VisualBrush visualBrush)
            {
                using var fill = new SKPath();
                fill.AddRect(ToRect(rectangle));
                DrawVisualBrush(visualBrush, fill, rectangle);
                brush = null;
            }
            if (!_guidelines.IsEmpty)
            {
                using var path = new SKPath(); path.AddRect(ToRect(rectangle));
                Paint(brush, pen, paint => DrawSnappedPath(path, paint));
                return;
            }
            Paint(brush, pen, paint => Canvas.DrawRect(ToRect(rectangle), paint));
        }

        public override void DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY)
        {
            if (rectangle.IsEmpty) return;
            if (brush is VisualBrush visualBrush)
            {
                using var fill = !_guidelines.IsSimple
                    ? SkiaGeometry.Create(new RectangleGeometry(rectangle, radiusX, radiusY).GetAsPathGeometry(), false)
                    : new SKPath();
                if (_guidelines.IsSimple)
                    fill.AddRoundRect(ToRect(rectangle), (float)Math.Min(Math.Abs(radiusX), rectangle.Width / 2), (float)Math.Min(Math.Abs(radiusY), rectangle.Height / 2));
                DrawVisualBrush(visualBrush, fill, rectangle);
                brush = null;
            }
            if (!_guidelines.IsSimple && brush != null)
            {
                using (SKPath fill = SkiaGeometry.Create(new RectangleGeometry(rectangle, radiusX, radiusY).GetAsPathGeometry(), false))
                using (SKPaint paint = CreateBrush(brush)) DrawSnappedPath(fill, paint);
                brush = null;
            }
            bool dashedRectangle = (radiusX == 0 || radiusY == 0) && pen?.DashStyle?.Dashes?.Count > 0;
            if (!_guidelines.IsEmpty || dashedRectangle)
            {
                using var path = new SKPath();
                // Native zero-radius figures start at top-left; Skia AddRoundRect starts at bottom-left.
                if (dashedRectangle) path.AddRect(ToRect(rectangle));
                else path.AddRoundRect(ToRect(rectangle), (float)Math.Min(Math.Abs(radiusX), rectangle.Width / 2), (float)Math.Min(Math.Abs(radiusY), rectangle.Height / 2));
                Paint(brush, pen, paint => DrawSnappedPath(path, paint));
                return;
            }
            Paint(brush, pen, paint => Canvas.DrawRoundRect(ToRect(rectangle),
                (float)Math.Min(Math.Abs(radiusX), rectangle.Width / 2), (float)Math.Min(Math.Abs(radiusY), rectangle.Height / 2), paint));
        }

        public override void DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY)
        {
            var rectangle = new SKRect((float)(center.X - Math.Abs(radiusX)), (float)(center.Y - Math.Abs(radiusY)),
                (float)(center.X + Math.Abs(radiusX)), (float)(center.Y + Math.Abs(radiusY)));
            if (brush is VisualBrush visualBrush)
            {
                using var fill = new SKPath(); fill.AddOval(rectangle);
                DrawVisualBrush(visualBrush, fill, new Rect(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height));
                brush = null;
            }
            if (!_guidelines.IsEmpty)
            {
                using var path = new SKPath(); path.AddOval(rectangle);
                Paint(brush, pen, paint => DrawSnappedPath(path, paint));
                return;
            }
            Paint(brush, pen, paint => Canvas.DrawOval(rectangle, paint));
        }

        public override void DrawGeometry(Brush brush, Pen pen, Geometry geometry)
        {
            if (geometry == null || (brush == null && (pen == null || pen.Brush == null))) return;
            Geometry fillGeometry = brush != null && !_guidelines.IsSimple && geometry is RectangleGeometry rectangle
                ? rectangle.GetAsPathGeometry() : geometry;
            using (SKPath fill = SkiaGeometry.Create(fillGeometry, false))
            {
                if (brush is VisualBrush visualBrush)
                    DrawVisualBrush(visualBrush, fill, geometry.Bounds);
                else if (brush != null)
                    using (SKPaint paint = CreateBrush(brush)) DrawSnappedPath(fill, paint);
            }
            if (pen != null && pen.Brush != null && pen.Thickness != 0)
                using (SKPath stroke = SkiaGeometry.CreateStrokePath(geometry, pen))
                using (SKPaint paint = CreatePen(pen)) DrawSnappedPath(stroke, paint);
        }

        public override void DrawGlyphRun(Brush foregroundBrush, GlyphRun glyphRun)
        {
            if (glyphRun == null || foregroundBrush == null) return;
            if (glyphRun.IsSideways)
                throw Unsupported("sideways glyph runs");
            using (var font = new SKFont(glyphRun.GlyphTypeface.PortableFontFace.Typeface, (float)glyphRun.FontRenderingEmSize))
            using (SKPaint paint = CreateBrush(foregroundBrush))
            {
                ushort[] glyphs = glyphRun.GlyphIndices.ToArray();
                var positions = new SKPoint[glyphs.Length];
                double advance = 0;
                for (int index = 0; index < glyphs.Length; index++)
                {
                    Point offset = glyphRun.GlyphOffsets == null || glyphRun.GlyphOffsets.Count == 0 ? default : glyphRun.GlyphOffsets[index];
                    positions[index] = new SKPoint((float)glyphRun.GlyphTypeface.PortableFontFace.GetGlyphOrigin(glyphRun, glyphs[index], advance, offset), (float)-offset.Y);
                    advance += glyphRun.AdvanceWidths[index];
                }
                using (var builder = new SKTextBlobBuilder())
                {
                    SKPositionedRunBuffer buffer = builder.AllocatePositionedRun(font, glyphs.Length);
                    buffer.SetGlyphs(glyphs);
                    buffer.SetPositions(positions);
                    using (SKTextBlob blob = builder.Build())
                    {
                        SKMatrix matrix = Canvas.TotalMatrix;
                        float baseline = matrix.MapPoint(ToPoint(glyphRun.BaselineOrigin)).Y;
                        matrix.TransY += _guidelines.OffsetY(baseline);
                        int saved = Canvas.Save();
                        try
                        {
                            Canvas.SetMatrix(matrix);
                            Canvas.DrawText(blob, (float)glyphRun.BaselineOrigin.X, (float)glyphRun.BaselineOrigin.Y, paint);
                        }
                        finally { Canvas.RestoreToCount(saved); }
                    }
                }
                GC.KeepAlive(glyphRun);
            }
        }

        public override void DrawDrawing(Drawing drawing) => drawing?.WalkCurrentValue(this);

        public override void PushClip(Geometry clipGeometry)
        {
            _guidelineStates.Push(_guidelines);
            Canvas.Save();
            if (clipGeometry != null)
                using (SKPath clip = SkiaGeometry.Create(clipGeometry, false)) Canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        }

        public override void PushTransform(Transform transform)
        {
            _guidelineStates.Push(_guidelines);
            Canvas.Save();
            if (transform != null)
            {
                SKMatrix matrix = ToMatrix(transform.Value);
                Canvas.Concat(in matrix);
            }
        }

        public override void PushOpacity(double opacity)
        {
            _guidelineStates.Push(_guidelines);
            using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, Alpha(opacity)) })
                Canvas.SaveLayer(paint);
        }

        public override void Pop()
        {
            Canvas.Restore();
            _guidelines = _guidelineStates.Pop();
        }

        internal (int Count, PortableGuidelineFrame Frame) CaptureGuidelines() => (_guidelineStates.Count, _guidelines);
        internal void RestoreGuidelines((int Count, PortableGuidelineFrame Frame) state)
        {
            while (_guidelineStates.Count > state.Count) _guidelineStates.Pop();
            _guidelines = state.Frame;
        }
        internal void SetVisualGuidelines(Visual visual) => _guidelines = PortableGuidelineFrame.CreateVisual(
            visual.VisualXSnappingGuidelines, visual.VisualYSnappingGuidelines, Canvas.TotalMatrix);

        public override void DrawLine(Pen pen, Point point0, AnimationClock point0Animations, Point point1, AnimationClock point1Animations)
        {
            RejectAnimation(point0Animations, point1Animations);
            DrawLine(pen, point0, point1);
        }
        public override void DrawRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations)
        {
            RejectAnimation(rectangleAnimations);
            DrawRectangle(brush, pen, rectangle);
        }
        public override void DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations,
            double radiusX, AnimationClock radiusXAnimations, double radiusY, AnimationClock radiusYAnimations)
        {
            RejectAnimation(rectangleAnimations, radiusXAnimations, radiusYAnimations);
            DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        public override void DrawEllipse(Brush brush, Pen pen, Point center, AnimationClock centerAnimations,
            double radiusX, AnimationClock radiusXAnimations, double radiusY, AnimationClock radiusYAnimations)
        {
            RejectAnimation(centerAnimations, radiusXAnimations, radiusYAnimations);
            DrawEllipse(brush, pen, center, radiusX, radiusY);
        }
        public override void PushOpacity(double opacity, AnimationClock opacityAnimations)
        {
            RejectAnimation(opacityAnimations);
            PushOpacity(opacity);
        }
        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
            if (imageSource == null || rectangle.IsEmpty) return;
            if (!(imageSource is System.Windows.Media.Imaging.BitmapSource bitmap) || bitmap.PortableBuffer == null)
                throw Unsupported("image sources without portable bitmap storage");
            SKSamplingOptions options = _bitmapScalingMode switch
            {
                BitmapScalingMode.Linear => new SKSamplingOptions(SKFilterMode.Linear),
                BitmapScalingMode.NearestNeighbor => new SKSamplingOptions(SKFilterMode.Nearest),
                BitmapScalingMode.HighQuality => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                _ => throw Unsupported($"bitmap scaling mode {_bitmapScalingMode}")
            };
            using (SKBitmap pixels = bitmap.PortableBuffer.CreateDisplayBitmap())
            {
                bool highQuality = _bitmapScalingMode == BitmapScalingMode.HighQuality;
                // Only this private display copy is frozen. Skia can share it without copying the source pixels again.
                if (highQuality) pixels.SetImmutable();
                using SKImage image = highQuality
                    ? SKImage.FromBitmap(pixels) ?? throw new InvalidOperationException("The portable high-quality display image could not be created.")
                    : null;
                SKMatrix sampling = new SKMatrix(
                    (float)(rectangle.Width / pixels.Width), 0, (float)rectangle.Left,
                    0, (float)(rectangle.Height / pixels.Height), (float)rectangle.Top, 0, 0, 1);
                if (!_guidelines.IsEmpty) sampling = SKMatrix.Concat(Canvas.TotalMatrix, sampling);
                using SKShader shader = highQuality
                    ? image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, options, sampling)
                    : pixels.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, options, sampling);
                using SKShader precise = options.Filter == SKFilterMode.Linear ? shader.WithColorFilter(_linearSamplingPrecision) : null;
                using var paint = new SKPaint { Shader = precise ?? shader, IsAntialias = true };
                using var path = new SKPath();
                path.AddRect(ToRect(rectangle));
                DrawSnappedPath(path, paint);
            }
            GC.KeepAlive(imageSource);
        }
        public override void DrawImage(ImageSource imageSource, Rect rectangle, AnimationClock rectangleAnimations)
        {
            RejectAnimation(rectangleAnimations);
            DrawImage(imageSource, rectangle);
        }
        public override void DrawVideo(MediaPlayer player, Rect rectangle) => throw Unsupported("video");
        public override void DrawVideo(MediaPlayer player, Rect rectangle, AnimationClock rectangleAnimations) => throw Unsupported("video");
        public override void PushOpacityMask(Brush opacityMask) => throw Unsupported("an opacity mask");
        public override void PushGuidelineSet(GuidelineSet guidelines)
        {
            PortableGuidelineFrame frame = guidelines == null ? _guidelines : PortableGuidelineFrame.Create(
                guidelines.GuidelinesX, guidelines.GuidelinesY, guidelines.IsDynamic, Canvas.TotalMatrix);
            _guidelineStates.Push(_guidelines);
            Canvas.Save();
            _guidelines = frame;
        }
        internal override void PushGuidelineY1(double coordinate) => PushGuidelineY2(coordinate, 0);
        internal override void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            PortableGuidelineFrame frame = PortableGuidelineFrame.Create(null,
                new DoubleCollection { leadingCoordinate, offsetToDrivenCoordinate }, true, Canvas.TotalMatrix);
            _guidelineStates.Push(_guidelines);
            Canvas.Save();
            _guidelines = frame;
        }
        [Obsolete(MS.Internal.Media.VisualTreeUtils.BitmapEffectObsoleteMessage)]
        public override void PushEffect(BitmapEffect effect, BitmapEffectInput effectInput) => throw Unsupported("bitmap effects");

        private void Paint(Brush brush, Pen pen, Action<SKPaint> draw)
        {
            if (brush != null)
                using (SKPaint fill = CreateBrush(brush)) draw(fill);
            if (pen != null && pen.Brush != null && pen.Thickness != 0)
                using (SKPaint stroke = CreatePen(pen)) draw(stroke);
        }

        private void DrawSnappedPath(SKPath path, SKPaint paint)
        {
            if (_guidelines.IsEmpty) { Canvas.DrawPath(path, paint); return; }
            using var outline = new SKPath();
            if (paint.Style == SKPaintStyle.Stroke && !paint.GetFillPath(path, outline)) return;
            using SKPath snapped = _guidelines.SnapPath(paint.Style == SKPaintStyle.Stroke ? outline : path, Canvas.TotalMatrix);
            using SKPaint fill = paint.Clone();
            fill.Style = SKPaintStyle.Fill;
            fill.PathEffect = null;
            int saved = Canvas.Save();
            try { Canvas.ResetMatrix(); Canvas.DrawPath(snapped, fill); }
            finally { Canvas.RestoreToCount(saved); }
        }

        private void DrawVisualBrush(VisualBrush brush, SKPath shape, Rect bounds)
        {
            if (brush.TileMode != TileMode.None) throw Unsupported($"VisualBrush tile mode {brush.TileMode}");
            Visual source = brush.Visual;
            if (source == null || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            if (_target == null) throw Unsupported("VisualBrush content without a portable render target");
            _target.RegisterBrushSource(source);
            Rect content = source.CalculateSubgraphBoundsOuterSpace();
            Rect viewbox = AbsoluteRectangle(brush.Viewbox, brush.ViewboxUnits, content);
            Rect viewport = AbsoluteRectangle(brush.Viewport, brush.ViewportUnits, bounds);
            if (!UsableRectangle(viewbox) || !UsableRectangle(viewport)) return;

            // These operations follow CTileBrushUtils::CalculateViewboxToViewportMapping.
            double sx = brush.Stretch == Stretch.None ? 1 : viewport.Width / viewbox.Width;
            double sy = brush.Stretch == Stretch.None ? 1 : viewport.Height / viewbox.Height;
            if (brush.Stretch == Stretch.Uniform) sx = sy = Math.Min(sx, sy);
            if (brush.Stretch == Stretch.UniformToFill) sx = sy = Math.Max(sx, sy);
            double ax = brush.AlignmentX == AlignmentX.Left ? 0 : brush.AlignmentX == AlignmentX.Right ? 1 : 0.5;
            double ay = brush.AlignmentY == AlignmentY.Top ? 0 : brush.AlignmentY == AlignmentY.Bottom ? 1 : 0.5;
            var mapping = new Matrix(sx, 0, 0, sy,
                viewport.X + ax * viewport.Width - (viewbox.X + ax * viewbox.Width) * sx,
                viewport.Y + ay * viewport.Height - (viewbox.Y + ay * viewbox.Height) * sy);
            // CBrushTypeUtils applies the normalized bounds transform before the absolute transform.
            var brushTransform = new Matrix(1 / bounds.Width, 0, 0, 1 / bounds.Height, -bounds.X / bounds.Width, -bounds.Y / bounds.Height);
            brushTransform.Append(brush.RelativeTransform.Value);
            brushTransform.Append(new Matrix(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y));
            brushTransform.Append(brush.Transform.Value);
            RequireFinite(mapping); RequireFinite(brushTransform);
            if (!mapping.HasInverse || !brushTransform.HasInverse) return;

            int saved = Canvas.Save();
            var guidelines = CaptureGuidelines();
            BitmapScalingMode mode = _bitmapScalingMode;
            try
            {
                if (_guidelines.IsEmpty) Canvas.ClipPath(shape, SKClipOperation.Intersect, true);
                else
                {
                    SKMatrix original = Canvas.TotalMatrix;
                    using SKPath snapped = _guidelines.SnapPath(shape, original);
                    Canvas.ResetMatrix();
                    Canvas.ClipPath(snapped, SKClipOperation.Intersect, true);
                    Canvas.SetMatrix(original);
                }
                SKMatrix transform = ToMatrix(brushTransform);
                Canvas.Concat(in transform);
                Canvas.ClipRect(ToRect(viewport), SKClipOperation.Intersect, true);
                transform = ToMatrix(mapping);
                Canvas.Concat(in transform);
                if (brush.Opacity != 1) PushOpacity(brush.Opacity);
                _target.WalkVisual(source, this);
            }
            finally
            {
                Canvas.RestoreToCount(saved);
                RestoreGuidelines(guidelines);
                _bitmapScalingMode = mode;
            }
        }

        private static Rect AbsoluteRectangle(Rect rectangle, BrushMappingMode units, Rect bounds)
        {
            if (rectangle.IsEmpty || units == BrushMappingMode.Absolute) return rectangle;
            if (bounds.IsEmpty) return Rect.Empty;
            return new Rect(bounds.X + rectangle.X * bounds.Width, bounds.Y + rectangle.Y * bounds.Height,
                rectangle.Width * bounds.Width, rectangle.Height * bounds.Height);
        }

        private static bool UsableRectangle(Rect value) => !value.IsEmpty && value.Width > 0 && value.Height > 0 &&
            double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Right) && double.IsFinite(value.Bottom);

        private static void RequireFinite(Matrix matrix)
        {
            SKMatrix value = ToMatrix(matrix);
            if (!float.IsFinite(value.ScaleX) || !float.IsFinite(value.ScaleY) || !float.IsFinite(value.SkewX) ||
                !float.IsFinite(value.SkewY) || !float.IsFinite(value.TransX) || !float.IsFinite(value.TransY))
                throw Unsupported("non-finite VisualBrush transforms");
        }

        private static SKPaint CreateBrush(Brush brush)
        {
            if (brush is ImageBrush { ImageSource: null })
                return new SKPaint { IsAntialias = true, Color = SKColors.Transparent };
            if (!(brush is SolidColorBrush solid)) throw Unsupported($"brush type {brush.GetType().FullName}");
            Color color = solid.Color;
            return new SKPaint { IsAntialias = true, Color = new SKColor(color.R, color.G, color.B, Alpha(color.A / 255d * solid.Opacity)) };
        }

        private static SKPaint CreatePen(Pen pen)
        {
            if (pen.StartLineCap != pen.EndLineCap || pen.StartLineCap == PenLineCap.Triangle)
                throw Unsupported("different line end caps or triangle caps");
            using SKPathEffect dashes = PortableDashPattern.Create(pen);
            SKPaint paint = CreateBrush(pen.Brush);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)Math.Abs(pen.Thickness);
            paint.StrokeMiter = (float)pen.MiterLimit;
            paint.PathEffect = dashes;
            paint.StrokeCap = pen.StartLineCap == PenLineCap.Round ? SKStrokeCap.Round :
                pen.StartLineCap == PenLineCap.Square ? SKStrokeCap.Square : SKStrokeCap.Butt;
            paint.StrokeJoin = pen.LineJoin == PenLineJoin.Round ? SKStrokeJoin.Round :
                pen.LineJoin == PenLineJoin.Bevel ? SKStrokeJoin.Bevel : SKStrokeJoin.Miter;
            return paint;
        }

        private static void RejectAnimation(params AnimationClock[] clocks)
        {
            if (clocks.Any(clock => clock != null)) throw Unsupported("animation clocks");
        }
        private static byte Alpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        private static NotSupportedException Unsupported(string operation) => new NotSupportedException($"Portable rendering does not support {operation}.");
        internal static SKPoint ToPoint(Point point) => new SKPoint((float)point.X, (float)point.Y);
        internal static SKRect ToRect(Rect rect) => new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
        internal static SKMatrix ToMatrix(Matrix value) => new SKMatrix((float)value.M11, (float)value.M21, (float)value.OffsetX,
            (float)value.M12, (float)value.M22, (float)value.OffsetY, 0, 0, 1);
    }
}
#endif
