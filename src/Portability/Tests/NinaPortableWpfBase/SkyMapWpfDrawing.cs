/*
 * Derived from NINA.WPF.Base/SkySurvey/SkyMapRasterRenderer.cs.
 * Copyright 2016 - 2026 Stefan Berg and the N.I.N.A. contributors.
 * This Source Code Form is subject to the Mozilla Public License, v. 2.0.
 * https://mozilla.org/MPL/2.0/
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.WPF.Base.SkySurvey {
    internal sealed class SkyMapWpfDrawing {
        private static readonly Pen BoundaryPen = Pen(128, 240, 230, 140, 0.5);
        private static readonly Pen GridPen = Pen(127, 70, 130, 180, 1);
        private static readonly Pen EquatorPen = Pen(127, 70, 130, 180, 3);
        private static readonly Pen ConstellationPen = Pen(128, 0, 255, 0, 1);
        private static readonly Pen TelescopePen = Pen(128, 255, 255, 0, 2);
        private static readonly Pen HorizonPen = Pen(200, 255, 165, 0, 2);
        private static readonly Pen HatchPen = Pen(255, 58, 42, 22, 1);
        private static readonly SolidColorBrush StarBrush = Brush(200, 255, 255, 255);
        private static readonly SolidColorBrush DsoFill = Brush(10, 255, 255, 255);
        private static readonly SolidColorBrush MaskBackground = Brush(255, 8, 8, 8);
        private static readonly SolidColorBrush ConstellationBrush = Brush(128, 255, 255, 153);
        private static readonly SolidColorBrush StarLabelBrush = Brush(128, 255, 215, 0);
        private static readonly Typeface RegularFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface BoldFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface ItalicFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
        private static readonly (Pen Pen, Brush Brush) Galaxy = DsoStyle(Colors.BurlyWood, 128);
        private static readonly (Pen Pen, Brush Brush) PlanetaryNebula = DsoStyle(Colors.Cyan, 128);
        private static readonly (Pen Pen, Brush Brush) Nebula = DsoStyle(Colors.Violet, 128);
        private static readonly (Pen Pen, Brush Brush) GlobularCluster = DsoStyle(Colors.Yellow, 128);
        private static readonly (Pen Pen, Brush Brush) DefaultDso = DsoStyle(Colors.White, 127);
        private readonly int width;
        private readonly int height;
        private readonly RenderTargetBitmap target;
        private readonly RenderTargetBitmap preview;
        private readonly byte[] pixels;
        private readonly byte[] previewPixels;
        private readonly Dictionary<(BitmapSource Image, bool Flip), (BitmapSource Image, LinkedListNode<(BitmapSource, bool)> Node)> snapshots = [];
        private readonly LinkedList<(BitmapSource, bool)> history = [];

        internal SkyMapWpfDrawing(int width, int height, bool software) {
            this.width = width;
            this.height = height;
            target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            if (software) {
                preview = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width * 0.5)), Math.Max(1, (int)Math.Ceiling(height * 0.5)), 96, 96, PixelFormats.Pbgra32);
                previewPixels = new byte[checked(preview.PixelWidth * preview.PixelHeight * 4)];
            }
            pixels = new byte[checked(width * height * 4)];
        }

        internal void Render(WriteableBitmap destination, SkyMapScene scene, IReadOnlyList<SkyMapImagePlacement> images, Point? telescope, bool software, SkyMapRenderQuality quality) {
            bool reduced = software && quality == SkyMapRenderQuality.InteractionPreview;
            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen()) {
                if (reduced) drawing.PushTransform(new ScaleTransform(0.5, 0.5));
                DrawFrame(drawing, scene, images, telescope, reduced ? 0.5 : 1);
                if (reduced) drawing.Pop();
            }
            target.Clear();
            if (reduced) {
                preview.Clear();
                preview.Render(visual);
                preview.CopyPixels(previewPixels, checked(preview.PixelWidth * 4), 0);
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                    int sourceX = (int)(((long)x * 2 + 1) * preview.PixelWidth / (width * 2L));
                    int sourceY = (int)(((long)y * 2 + 1) * preview.PixelHeight / (height * 2L));
                    Buffer.BlockCopy(previewPixels, (sourceY * preview.PixelWidth + sourceX) * 4, pixels, (y * width + x) * 4, 4);
                }
            } else {
                target.Render(visual);
                target.CopyPixels(pixels, checked(width * 4), 0);
            }
            destination.WritePixels(new Int32Rect(0, 0, width, height), pixels, checked(width * 4), 0);
        }

        internal void Clear() { snapshots.Clear(); history.Clear(); }

        private BitmapSource Snapshot(BitmapSource source, bool flip) {
            var key = (source, flip);
            if (snapshots.TryGetValue(key, out var existing)) {
                history.Remove(existing.Node);
                history.AddFirst(existing.Node);
                return existing.Image;
            }
            BitmapSource image = source;
            if (!source.IsFrozen) {
                int stride = checked((source.PixelWidth * source.Format.BitsPerPixel + 7) / 8);
                var data = new byte[checked(stride * source.PixelHeight)];
                source.CopyPixels(data, stride, 0);
                image = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, source.Format, source.Palette, data, stride);
                image.Freeze();
            }
            snapshots.Add(key, (image, history.AddFirst(key)));
            if (snapshots.Count > 64) { snapshots.Remove(history.Last.Value); history.RemoveLast(); }
            return image;
        }

        private void DrawFrame(DrawingContext drawing, SkyMapScene scene, IReadOnlyList<SkyMapImagePlacement> images, Point? telescope, double scale) {
            foreach (SkyMapImagePlacement image in images) {
                double rotation = image.Rotation * Math.PI / 180;
                double cosine = Math.Cos(rotation), sine = Math.Sin(rotation);
                double halfWidth = image.Width / 2, halfHeight = image.Height / 2;
                double transformedHalfWidth = Math.Abs(cosine) * halfWidth + Math.Abs(sine) * halfHeight;
                double transformedHalfHeight = Math.Abs(sine) * halfWidth + Math.Abs(cosine) * halfHeight;
                if (image.Center.X + transformedHalfWidth <= 0 || image.Center.X - transformedHalfWidth >= width || image.Center.Y + transformedHalfHeight <= 0 || image.Center.Y - transformedHalfHeight >= height) continue;
                double horizontalScale = image.FlipHorizontally ? -1 : 1;
                var matrix = new Matrix(horizontalScale * cosine, horizontalScale * sine, -sine, cosine, 0, 0);
                matrix.OffsetX = image.Center.X - image.Center.X * matrix.M11 - image.Center.Y * matrix.M21;
                matrix.OffsetY = image.Center.Y - image.Center.X * matrix.M12 - image.Center.Y * matrix.M22;
                drawing.PushTransform(new MatrixTransform(matrix));
                drawing.DrawImage(image.IsDecodedCacheImage ? image.Image : Snapshot(image.Image, image.FlipHorizontally), new Rect(image.Center.X - halfWidth, image.Center.Y - halfHeight, image.Width, image.Height));
                drawing.Pop();
            }
            DrawPaths(drawing, scene.ConstellationBoundaries, BoundaryPen, null);
            DrawPaths(drawing, scene.GridLines, GridPen, EquatorPen);
            DrawLines(drawing, scene.ConstellationLines, ConstellationPen);
            foreach (SkyMapStar star in scene.Stars) drawing.DrawEllipse(StarBrush, null, star.Center, star.Radius, star.Radius);
            foreach (SkyMapDeepSkyObject dso in scene.DeepSkyObjects) {
                var (pen, brush) = dso.Type switch { "GALXY" or "GALCL" => Galaxy, "PLNNB" => PlanetaryNebula, "BRTNB" or "CL+NB" => Nebula, "GLOCL" => GlobularCluster, _ => DefaultDso };
                drawing.PushTransform(new RotateTransform(dso.PositionAngle, dso.Center.X, dso.Center.Y));
                drawing.DrawEllipse(DsoFill, pen, dso.Center, dso.RadiusX, dso.RadiusY);
                drawing.Pop();
                if (!string.IsNullOrWhiteSpace(dso.Name)) {
                    FormattedText text = Text(dso.Name, 10, FontStyles.Normal, FontWeights.Normal, brush);
                    drawing.DrawText(text, new Point(dso.Center.X - text.Width / 2, dso.Center.Y + dso.RadiusY + 5));
                }
            }
            foreach (SkyMapLabel label in scene.Labels) if (label.Kind != SkyMapLabelKind.CardinalDirection) DrawLabel(drawing, label, scale);
            if (telescope is Point p) {
                var pen = TelescopePen;
                drawing.DrawEllipse(null, pen, p, 15, 15);
                drawing.DrawLine(pen, new Point(p.X, p.Y - 15), new Point(p.X, p.Y - 5));
                drawing.DrawLine(pen, new Point(p.X, p.Y + 5), new Point(p.X, p.Y + 15));
                drawing.DrawLine(pen, new Point(p.X - 15, p.Y), new Point(p.X - 5, p.Y));
                drawing.DrawLine(pen, new Point(p.X + 5, p.Y), new Point(p.X + 15, p.Y));
            }
            foreach (SkyMapPath path in scene.HorizonMaskAreas) {
                if (path.Points.Count < 3) continue;
                drawing.PushClip(Geometry(path, true, scale));
                drawing.DrawRectangle(MaskBackground, null, new Rect(0, 0, Math.Ceiling(width * scale) / scale, Math.Ceiling(height * scale) / scale));
                // GDI's calibrated eight-pixel hatch stays in device coordinates during the half-size preview.
                drawing.PushTransform(new ScaleTransform(1 / scale, 1 / scale));
                for (int offset = -(int)Math.Ceiling(height * scale / 8) * 8; offset <= Math.Ceiling(width * scale); offset += 8)
                    drawing.DrawLine(HatchPen, new Point(offset - 0.5, -0.5), new Point(offset + Math.Ceiling(height * scale) + 0.5, Math.Ceiling(height * scale) + 0.5));
                drawing.Pop();
                drawing.Pop();
            }
            DrawLines(drawing, scene.HorizonLines, HorizonPen);
            foreach (SkyMapLabel label in scene.Labels) if (label.Kind == SkyMapLabelKind.CardinalDirection) DrawLabel(drawing, label, scale);
        }

        private void DrawLabel(DrawingContext drawing, SkyMapLabel label, double scale) {
            var (size, style, weight, brush) = label.Kind switch {
                SkyMapLabelKind.CardinalDirection => (16d, FontStyles.Normal, FontWeights.Bold, Brushes.Red),
                SkyMapLabelKind.Constellation => (11d, FontStyles.Normal, FontWeights.Bold, ConstellationBrush),
                SkyMapLabelKind.Star => (8d, FontStyles.Italic, FontWeights.Normal, StarLabelBrush),
                _ => (7d, FontStyles.Italic, FontWeights.Normal, Brushes.SteelBlue)
            };
            FormattedText text = Text(label.Text, size, style, weight, brush);
            double x = label.Position.X, y = label.Position.Y;
            if (label.Kind == SkyMapLabelKind.CardinalDirection) { x -= text.Width / 2; y -= text.Height / 2; }
            else if (label.Kind is SkyMapLabelKind.Constellation or SkyMapLabelKind.Star) x -= text.Width / 2;
            if (label.Kind is SkyMapLabelKind.CardinalDirection or SkyMapLabelKind.Grid) {
                x = Math.Clamp(x, 0, Math.Max(0, Math.Ceiling(width * scale) / scale - text.Width));
                y = Math.Clamp(y, 0, Math.Max(0, Math.Ceiling(height * scale) / scale - text.Height));
            }
            drawing.DrawText(text, new Point(x, y));
        }

        private static FormattedText Text(string text, double points, FontStyle style, FontWeight weight, Brush brush) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, weight == FontWeights.Bold ? BoldFont : style == FontStyles.Italic ? ItalicFont : RegularFont, points * 96 / 72, brush, 1);
        private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); return brush; }
        private static Pen Pen(byte a, byte r, byte g, byte b, double width) { var pen = new Pen(Brush(a, r, g, b), width); pen.Freeze(); return pen; }
        private static (Pen, Brush) DsoStyle(Color color, byte alpha) => (Pen(alpha, color.R, color.G, color.B, 1), Brush(255, color.R, color.G, color.B));
        private static void DrawLines(DrawingContext drawing, IReadOnlyList<SkyMapLine> lines, Pen pen) { foreach (SkyMapLine line in lines) drawing.DrawLine(pen, line.Start, line.End); }
        private static void DrawPaths(DrawingContext drawing, IReadOnlyList<SkyMapPath> paths, Pen pen, Pen thick) { foreach (SkyMapPath path in paths) if (path.Points.Count >= 2) drawing.DrawGeometry(null, path.StrokeThickness > pen.Thickness && thick != null ? thick : pen, Geometry(path, path.Closed)); }
        private static StreamGeometry Geometry(SkyMapPath path, bool closed, double maskScale = 0) {
            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            // The original mask selects SmoothingMode.None, including device rounding at odd preview edges.
            Point RasterPoint(Point point) => maskScale == 0 ? point : new Point(Math.Round(point.X * maskScale, MidpointRounding.AwayFromZero) / maskScale, Math.Round(point.Y * maskScale, MidpointRounding.AwayFromZero) / maskScale);
            using (StreamGeometryContext context = geometry.Open()) { context.BeginFigure(RasterPoint(path.Points[0]), closed, closed); for (int i = 1; i < path.Points.Count; i++) context.LineTo(RasterPoint(path.Points[i]), true, false); }
            return geometry;
        }
    }
}
