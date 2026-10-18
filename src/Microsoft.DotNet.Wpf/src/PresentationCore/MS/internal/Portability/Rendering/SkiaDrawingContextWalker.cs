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

        internal SkiaDrawingContextWalker(SKCanvas canvas, SKColorFilter linearSamplingPrecision)
        {
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
            if (!_guidelines.IsEmpty)
            {
                using var path = new SKPath();
                path.AddRoundRect(ToRect(rectangle), (float)Math.Min(Math.Abs(radiusX), rectangle.Width / 2), (float)Math.Min(Math.Abs(radiusY), rectangle.Height / 2));
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
            using (SKPath fill = SkiaGeometry.Create(geometry, false))
            {
                if (brush != null)
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
        internal void SetVisualGuidelines(Visual visual) => _guidelines = PortableGuidelineFrame.Create(
            visual.VisualXSnappingGuidelines, visual.VisualYSnappingGuidelines, false, Canvas.TotalMatrix);

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
            SKFilterMode filter = _bitmapScalingMode switch
            {
                BitmapScalingMode.Linear => SKFilterMode.Linear,
                BitmapScalingMode.NearestNeighbor => SKFilterMode.Nearest,
                BitmapScalingMode.HighQuality => throw Unsupported("HighQuality/Fant bitmap sampling, which requires image prefiltering"),
                _ => throw Unsupported($"bitmap scaling mode {_bitmapScalingMode}")
            };
            using (SKBitmap pixels = bitmap.PortableBuffer.CreateDisplayBitmap())
            {
                SKMatrix sampling = new SKMatrix(
                    (float)(rectangle.Width / pixels.Width), 0, (float)rectangle.Left,
                    0, (float)(rectangle.Height / pixels.Height), (float)rectangle.Top, 0, 0, 1);
                if (!_guidelines.IsEmpty) sampling = SKMatrix.Concat(Canvas.TotalMatrix, sampling);
                using SKShader shader = pixels.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    new SKSamplingOptions(filter), sampling);
                using SKShader precise = filter == SKFilterMode.Linear ? shader.WithColorFilter(_linearSamplingPrecision) : null;
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
            int saved = Canvas.Save();
            try { Canvas.ResetMatrix(); Canvas.DrawPath(snapped, fill); }
            finally { Canvas.RestoreToCount(saved); }
        }

        private static SKPaint CreateBrush(Brush brush)
        {
            if (!(brush is SolidColorBrush solid)) throw Unsupported($"brush type {brush.GetType().FullName}");
            Color color = solid.Color;
            return new SKPaint { IsAntialias = true, Color = new SKColor(color.R, color.G, color.B, Alpha(color.A / 255d * solid.Opacity)) };
        }

        private static SKPaint CreatePen(Pen pen)
        {
            if (pen.StartLineCap != pen.EndLineCap || pen.StartLineCap == PenLineCap.Triangle)
                throw Unsupported("different line end caps or triangle caps");
            if (pen.DashStyle != null && pen.DashStyle.Dashes.Count != 0)
                throw Unsupported("dashed pens");
            SKPaint paint = CreateBrush(pen.Brush);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)Math.Abs(pen.Thickness);
            paint.StrokeMiter = (float)pen.MiterLimit;
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
