// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Composition;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal static partial class SkiaGeometry
    {
        internal static Rect GetRenderBounds(Geometry geometry, Pen pen, Matrix world, double tolerance, ToleranceType type)
        {
            if (geometry.IsObviouslyEmpty()) return Rect.Empty;
            try
            {
                using (SKPath fill = Create(geometry, false))
                using (SKPath stroke = pen == null ? null : Create(geometry, true))
                using (SKPath extent = Create(geometry, false, includeHollows: true))
                    return RenderBounds(fill, stroke, extent, pen, world, tolerance, type);
            }
            catch (ArithmeticException) { return Rect.Empty; }
        }

        internal static unsafe Rect GetRenderBounds(Pen pen, Matrix world, Point* points, byte* types, uint pointCount,
            uint segmentCount, Matrix geometry, double tolerance, ToleranceType type)
        {
            if (points == null || types == null || pointCount == 0 || segmentCount == 0)
                throw new ArgumentException("Portable polygon bounds require nonempty point and segment arrays.");
            try
            {
                using (var path = new SKPath())
                {
                    path.MoveTo(ToPoint(points[0]));
                    uint index = 1;
                    for (uint segment = 0; segment < segmentCount; segment++)
                    {
                        byte flags = types[segment];
                        bool cubic = (flags & (byte)MILCoreSegFlags.SegTypeMask) == (byte)MILCoreSegFlags.SegTypeBezier;
                        bool line = (flags & (byte)MILCoreSegFlags.SegTypeMask) == (byte)MILCoreSegFlags.SegTypeLine;
                        if (!line && !cubic || pointCount - index < (cubic ? 3 : 1))
                            throw new ArgumentException("Portable polygon bounds received invalid segment types or point counts.");
                        if (pen != null && (flags & (byte)MILCoreSegFlags.SegIsAGap) != 0)
                            throw new NotSupportedException("Portable rendering does not support unstroked segments in a stroked path.");
                        if (cubic) { path.CubicTo(ToPoint(points[index]), ToPoint(points[index + 1]), ToPoint(points[index + 2])); index += 3; }
                        else path.LineTo(ToPoint(points[index++]));
                    }
                    if (index != pointCount)
                        throw new ArgumentException("Portable polygon bounds received unused source points.");
                    if ((types[0] & (byte)MILCoreSegFlags.SegClosed) != 0) path.Close();
                    ApplyTransform(path, geometry);
                    return RenderBounds(path, pen == null ? null : path, path, pen, world, tolerance, type);
                }
            }
            catch (ArithmeticException) { return Rect.Empty; }
        }

        private static Rect RenderBounds(SKPath fill, SKPath stroke, SKPath extent, Pen pen, Matrix world,
            double tolerance, ToleranceType type)
        {
            Rect result;
            using (var transformed = new SKPath(fill))
            {
                ApplyTransform(transformed, world);
                result = PathBounds(transformed);
            }
            if (pen != null && double.IsNaN(pen.Thickness)) return Rect.Empty;
            if (pen != null)
            {
                using (SKPaint paint = CreateStroke(pen))
                using (var outline = new SKPath())
                {
                    double absolute = StrokeTolerance(extent, pen, world, tolerance, type);
                    double effective = absolute == 0 ? 0.25 : Math.Max(absolute, 1e-6);
                    double halfWidth = paint.StrokeWidth / 2;
                    double determinant = Math.Abs(world.M11 * world.M22 - world.M12 * world.M21);
                    double threshold = effective * 0.004;
                    if (halfWidth == 0 || halfWidth * halfWidth * determinant < threshold * threshold)
                    {
                        // MIL's skipped-pen fallback uses the geometry-space point without the world transform.
                        if (result.IsEmpty) result = new Rect(0, 0, 0, 0);
                        if (!stroke.IsEmpty) result.Union(StrokeStart(stroke, Matrix.Identity));
                        return result;
                    }
                    double scale = double.Hypot(double.Hypot(world.M11, world.M12), double.Hypot(world.M21, world.M22));
                    // Sub-float requests can make Skia's offset solver discard curves instead of refining them.
                    if (effective < CoordinatePrecision(stroke, paint.StrokeWidth) * scale && HasNonconstantCurve(stroke))
                        throw new NotSupportedException("Portable curved stroke bounds cannot satisfy the requested tolerance below raster coordinate precision.");
                    double resolution = Math.Max(1, 0.25 * scale / effective);
                    if (!double.IsFinite(resolution) || resolution > float.MaxValue)
                        throw new NotSupportedException("Portable geometry bounds cannot satisfy the requested stroke tolerance at raster precision.");
                    paint.GetFillPath(stroke, outline, (float)resolution);
                    ApplyTransform(outline, world);
                    Rect strokeBounds = PathBounds(outline);
                    result.Union(strokeBounds.IsEmpty && !stroke.IsEmpty ? StrokeStart(stroke, world) : strokeBounds);
                    result.Union(CollapsedContourBounds(stroke, (float)halfWidth, pen.StartLineCap, world));
                }
            }
            // MIL returns an origin point when a nonempty path contains only hollow figures.
            return result.IsEmpty ? new Rect(0, 0, 0, 0) : result;
        }

        private static double StrokeTolerance(SKPath extent, Pen pen, Matrix world, double tolerance, ToleranceType type)
        {
            Rect loose = PathBounds(extent);
            double factor = pen.LineJoin == PenLineJoin.Miter ? Math.Max(1, pen.MiterLimit) * Math.Sqrt(2) :
                pen.StartLineCap == PenLineCap.Square || pen.EndLineCap == PenLineCap.Square || pen.DashCap == PenLineCap.Square ? Math.Sqrt(2) : 1;
            double padding = Math.Abs(pen.Thickness) * 0.5 * factor;
            if (!double.IsFinite(padding)) throw new ArithmeticException("Portable stroke bounds overflowed raster coordinates.");
            // MIL's loose-bounds shortcut treats a zero-area rectangle as empty here.
            if (!loose.IsEmpty && loose.Width > 0 && loose.Height > 0) { loose.Inflate(padding, padding); loose.Transform(world); }
            var bounds = new SKRect(Scalar(loose.Left), Scalar(loose.Top), Scalar(loose.Right), Scalar(loose.Bottom));
            return AbsoluteTolerance(bounds, tolerance, type);
        }

        private static Rect PathBounds(SKPath path)
        {
            if (path.IsEmpty) return Rect.Empty;
            SKRect bounds = path.TightBounds;
            if (!Finite(bounds)) throw new ArithmeticException("Portable geometry bounds overflowed raster coordinates.");
            return new Rect(bounds.Left, bounds.Top, (double)bounds.Right - bounds.Left, (double)bounds.Bottom - bounds.Top);
        }

        private static double CoordinatePrecision(SKPath path, float width)
        {
            SKRect bounds = path.Bounds;
            float magnitude = Math.Max(width, Math.Max(Math.Max(Math.Abs(bounds.Left), Math.Abs(bounds.Top)),
                Math.Max(Math.Abs(bounds.Right), Math.Abs(bounds.Bottom))));
            float next = MathF.BitIncrement(magnitude);
            return float.IsFinite(next) ? (double)next - magnitude : (double)magnitude - MathF.BitDecrement(magnitude);
        }

        private static bool HasNonconstantCurve(SKPath path)
        {
            using (SKPath.RawIterator iterator = path.CreateRawIterator())
            {
                var points = new SKPoint[4];
                SKPathVerb verb;
                while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                {
                    int count = verb == SKPathVerb.Cubic ? 3 : verb == SKPathVerb.Quad || verb == SKPathVerb.Conic ? 2 : 0;
                    for (int index = 1; index <= count; index++)
                        if (points[index] != points[0]) return true;
                }
                return false;
            }
        }

        private static Rect StrokeStart(SKPath path, Matrix world)
        {
            SKPoint start = path.GetPoint(0);
            Point point = world.Transform(new Point(start.X, start.Y));
            return new Rect(Scalar(point.X), Scalar(point.Y), 0, 0);
        }

        private static SKPath CollapsedStroke(SKPoint point, float halfWidth, PenLineCap cap)
        {
            var path = new SKPath();
            // MIL gives a coincident segment a horizontal tangent, including flat-cap normal bounds.
            if (cap == PenLineCap.Round) path.AddCircle(point.X, point.Y, halfWidth);
            else if (cap == PenLineCap.Square) path.AddRect(new SKRect(point.X - halfWidth, point.Y - halfWidth, point.X + halfWidth, point.Y + halfWidth));
            else { path.MoveTo(point.X, point.Y - halfWidth); path.LineTo(point.X, point.Y + halfWidth); }
            return path;
        }

        private static Rect CollapsedContourBounds(SKPath path, float halfWidth, PenLineCap cap, Matrix world)
        {
            Rect result = Rect.Empty;
            using (SKPath.RawIterator iterator = path.CreateRawIterator())
            {
                var points = new SKPoint[4];
                SKPoint start = default;
                bool hasSegment = false, collapsed = true, closed = false;
                SKPathVerb verb;
                while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                {
                    if (verb == SKPathVerb.Move)
                    {
                        IncludeContour();
                        start = points[0]; hasSegment = false; collapsed = true; closed = false;
                    }
                    else if (verb == SKPathVerb.Close) closed = true;
                    else
                    {
                        hasSegment = true;
                        int count = verb == SKPathVerb.Cubic ? 3 : verb == SKPathVerb.Quad || verb == SKPathVerb.Conic ? 2 : 1;
                        for (int index = 1; index <= count; index++) collapsed &= points[index] == start;
                    }
                }
                IncludeContour();

                void IncludeContour()
                {
                    if (!hasSegment || !collapsed) return;
                    using (SKPath contour = CollapsedStroke(start, halfWidth, closed ? PenLineCap.Round : cap))
                    {
                        ApplyTransform(contour, world);
                        result.Union(PathBounds(contour));
                    }
                }
            }
            return result;
        }

        private static void ApplyTransform(SKPath path, Matrix transform)
        {
            if (transform.IsIdentity) return;
            var matrix = new SKMatrix(Scalar(transform.M11), Scalar(transform.M21), Scalar(transform.OffsetX),
                Scalar(transform.M12), Scalar(transform.M22), Scalar(transform.OffsetY), 0, 0, 1);
            path.Transform(matrix);
            if (!Finite(path.Bounds)) throw new ArithmeticException("Portable geometry transform overflowed raster coordinates.");
        }
    }
}
#endif
