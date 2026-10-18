// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal static partial class SkiaGeometry
    {
        internal static Rect GetBounds(Geometry geometry)
        {
            try
            {
                using (SKPath path = Create(geometry, false, includeHollows: true))
                {
                    if (path.IsEmpty) return Rect.Empty;
                    SKRect bounds = path.TightBounds;
                    if (!Finite(bounds)) return Rect.Empty;
                    return new Rect(bounds.Left, bounds.Top, (double)bounds.Right - bounds.Left, (double)bounds.Bottom - bounds.Top);
                }
            }
            catch (ArithmeticException) { return Rect.Empty; }
        }

        internal static double AbsoluteTolerance(SKRect bounds, double tolerance, ToleranceType type)
        {
            double extent = Math.Max((double)bounds.Right - bounds.Left, (double)bounds.Bottom - bounds.Top);
            return type == ToleranceType.Relative
                ? Math.Max(double.IsNaN(tolerance) ? 0 : tolerance, 1e-12) * extent
                : Math.Max(double.IsNaN(tolerance) ? 0 : tolerance, extent * 1e-12);
        }

        internal static bool Contains(Geometry geometry, Pen pen, Point point, double tolerance, ToleranceType type)
        {
            try { return ContainsFinite(geometry, pen, point, tolerance, type); }
            catch (ArithmeticException) { return false; }
        }

        private static bool ContainsFinite(Geometry geometry, Pen pen, Point point, double tolerance, ToleranceType type)
        {
            if (!float.IsFinite((float)point.X) || !float.IsFinite((float)point.Y))
                return false;
            if (pen != null && (pen.Thickness == 0 || double.IsNaN(pen.Thickness)))
                return false;
            using (SKPath path = pen == null ? Create(geometry, false, closeFillContours: true) : CreateStrokePath(geometry, pen))
            using (SKPath boundsPath = Create(geometry, false, includeHollows: true))
            {
                if (path.IsEmpty)
                    return false;
                SKRect bounds = boundsPath.TightBounds;
                if (!Finite(bounds))
                    return false;
                double absolute = AbsoluteTolerance(bounds, tolerance, type);
                if (double.IsNaN(absolute))
                    return false;
                if (pen == null)
                    return ContainsOrNear(path, point, absolute);
                using (SKPaint stroke = CreateStroke(pen))
                using (var outline = new SKPath())
                {
                    if (!stroke.GetFillPath(path, outline))
                        return false;
                    return ContainsOrNear(outline, point, absolute);
                }
            }
        }

        private static bool ContainsOrNear(SKPath path, Point point, double tolerance)
        {
            if (path.IsEmpty || !Finite(path.Bounds))
                return false;
            if (path.Contains((float)point.X, (float)point.Y) || double.IsPositiveInfinity(tolerance))
                return true;
            // MIL also hits near a fill contour's start, including a collapsed point.
            using (SKPath.RawIterator iterator = path.CreateRawIterator())
            {
                var points = new SKPoint[4];
                SKPathVerb verb;
                while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                    if (verb == SKPathVerb.Move && double.Hypot(points[0].X - point.X, points[0].Y - point.Y) <= tolerance)
                        return true;
            }
            if (tolerance <= 0)
                return false;
            using (var edge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Min(2 * tolerance, float.MaxValue),
                StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
            using (var boundary = new SKPath())
                return edge.GetFillPath(path, boundary) && boundary.Contains((float)point.X, (float)point.Y);
        }

        private static SKPaint CreateStroke(Pen pen, bool allowDashes = false)
        {
            if (pen.StartLineCap != pen.EndLineCap || pen.StartLineCap == PenLineCap.Triangle)
                throw new NotSupportedException("Portable geometry containment does not support different line end caps or triangle caps.");
            if (!allowDashes && pen.DashStyle != null && pen.DashStyle.Dashes.Count != 0)
                throw new NotSupportedException("Portable geometry containment does not support dashed pens.");
            if (!float.IsFinite((float)pen.Thickness))
                throw new NotSupportedException("Portable geometry containment requires a finite stroke width representable by the raster backend.");
            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Math.Abs(pen.Thickness),
                StrokeMiter = Scalar(pen.MiterLimit),
                StrokeCap = pen.StartLineCap == PenLineCap.Round ? SKStrokeCap.Round :
                    pen.StartLineCap == PenLineCap.Square ? SKStrokeCap.Square : SKStrokeCap.Butt,
                StrokeJoin = pen.LineJoin == PenLineJoin.Round ? SKStrokeJoin.Round :
                    pen.LineJoin == PenLineJoin.Bevel ? SKStrokeJoin.Bevel : SKStrokeJoin.Miter
            };
        }

        internal static SKPath Create(Geometry geometry, bool stroke, bool closeFillContours = false, bool includeHollows = false)
        {
            var result = new SKPath();
            try
            {
                Append(result, geometry, stroke, closeFillContours, includeHollows);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static void Append(SKPath destination, Geometry geometry, bool stroke, bool closeFillContours, bool includeHollows)
        {
            using (var path = new SKPath())
            {
                switch (geometry)
                {
                    case RectangleGeometry rectangle:
                        if (!rectangle.Rect.IsEmpty && (rectangle.Rect.Width == 0 || rectangle.Rect.Height == 0))
                        {
                            path.MoveTo(ToPoint(rectangle.Rect.TopLeft));
                            path.LineTo(ToPoint(rectangle.Rect.BottomRight));
                            path.Close();
                        }
                        else if (!rectangle.Rect.IsEmpty)
                            path.AddRoundRect(ToRect(rectangle.Rect), Scalar(Math.Min(Math.Abs(rectangle.RadiusX), rectangle.Rect.Width / 2)),
                                Scalar(Math.Min(Math.Abs(rectangle.RadiusY), rectangle.Rect.Height / 2)));
                        break;
                    case EllipseGeometry ellipse:
                        path.AddOval(new SKRect(Scalar(ellipse.Center.X - Math.Abs(ellipse.RadiusX)), Scalar(ellipse.Center.Y - Math.Abs(ellipse.RadiusY)),
                            Scalar(ellipse.Center.X + Math.Abs(ellipse.RadiusX)), Scalar(ellipse.Center.Y + Math.Abs(ellipse.RadiusY))));
                        break;
                    case LineGeometry line:
                        path.MoveTo(ToPoint(line.StartPoint));
                        path.LineTo(ToPoint(line.EndPoint));
                        break;
                    case GeometryGroup group:
                        foreach (Geometry child in group.Children) Append(path, child, stroke, closeFillContours, includeHollows);
                        path.FillType = group.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                        break;
                    case PathGeometry pathGeometry:
                        AppendFigures(path, pathGeometry, stroke, closeFillContours, includeHollows);
                        break;
                    case StreamGeometry stream:
                        // Parsing WPF's serialized commands stays managed and preserves segment flags.
                        AppendFigures(path, stream.GetAsPathGeometry(), stroke, closeFillContours, includeHollows);
                        break;
                    default:
                        throw new NotSupportedException($"Portable rendering does not support geometry type {geometry.GetType().FullName}.");
                }
                if (geometry.Transform != null && !geometry.Transform.Value.IsIdentity)
                {
                    Matrix transform = geometry.Transform.Value;
                    var matrix = new SKMatrix(Scalar(transform.M11), Scalar(transform.M21), Scalar(transform.OffsetX),
                        Scalar(transform.M12), Scalar(transform.M22), Scalar(transform.OffsetY), 0, 0, 1);
                    path.Transform(matrix);
                }
                destination.FillType = path.FillType;
                destination.AddPath(path);
            }
        }

        private static float Scalar(double value)
        {
            float scalar = (float)value;
            if (!float.IsFinite(scalar))
                throw new ArithmeticException("Portable geometry requires finite coordinates representable by the raster backend.");
            return scalar;
        }

        private static bool Finite(SKRect rect) => float.IsFinite(rect.Left) && float.IsFinite(rect.Top) &&
            float.IsFinite(rect.Right) && float.IsFinite(rect.Bottom);

        private static SKPoint ToPoint(Point point) => new SKPoint(Scalar(point.X), Scalar(point.Y));
        private static SKRect ToRect(Rect rect) => new SKRect(Scalar(rect.Left), Scalar(rect.Top), Scalar(rect.Right), Scalar(rect.Bottom));

        internal static SKPath CreateStrokePath(Geometry geometry, Pen pen)
        {
            if ((pen.StartLineCap != pen.DashCap || pen.EndLineCap != pen.DashCap) && HasGaps(geometry))
                throw new NotSupportedException("Portable mixed-segment strokes require matching outer and gap caps.");
            if (geometry is RectangleGeometry rectangle && (rectangle.RadiusX == 0 || rectangle.RadiusY == 0) &&
                pen.DashStyle != null && pen.DashStyle.Dashes.Count != 0)
                geometry = rectangle.GetAsPathGeometry();
            return Create(geometry, true);
        }

        private static bool HasGaps(Geometry geometry)
        {
            if (geometry is GeometryGroup group)
            {
                foreach (Geometry child in group.Children)
                    if (HasGaps(child)) return true;
            }
            PathGeometry path = geometry as PathGeometry;
            if (geometry is StreamGeometry stream) path = stream.GetAsPathGeometry();
            if (path != null)
                foreach (PathFigure figure in path.Figures)
                    foreach (PathSegment segment in figure.Segments)
                        if (!segment.IsStroked && SegmentEnd(segment).HasValue) return true;
            return false;
        }

        private static void AppendFigures(SKPath path, PathGeometry geometry, bool stroke, bool closeFillContours, bool includeHollows)
        {
            path.FillType = geometry.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            foreach (PathFigure figure in geometry.Figures)
            {
                if (!stroke && !includeHollows && !figure.IsFilled) continue;
                int gap = -1;
                if (stroke)
                    for (int index = 0; index < figure.Segments.Count; index++)
                        if (!figure.Segments[index].IsStroked && SegmentEnd(figure.Segments[index]).HasValue)
                        { gap = index; break; }

                // Start a closed mixed figure after a gap so its closing edge still joins its first run.
                bool mixed = gap >= 0;
                if (!figure.IsClosed) gap = -1;
                Point? nextStart = gap < 0 ? figure.StartPoint : SegmentEnd(figure.Segments[gap]).Value;
                if (!mixed) { path.MoveTo(ToPoint(nextStart.Value)); nextStart = null; }
                for (int offset = 0; offset < figure.Segments.Count; offset++)
                {
                    int index = gap < 0 ? offset : (gap + 1 + offset) % figure.Segments.Count;
                    if (gap >= 0 && index == 0)
                    {
                        if (nextStart.HasValue) path.MoveTo(ToPoint(nextStart.Value));
                        path.LineTo(ToPoint(figure.StartPoint));
                        nextStart = null;
                    }
                    PathSegment segment = figure.Segments[index];
                    if (stroke && !segment.IsStroked)
                    {
                        Point? end = SegmentEnd(segment);
                        if (end.HasValue) nextStart = end;
                    }
                    else if (SegmentEnd(segment).HasValue)
                    {
                        if (nextStart.HasValue) path.MoveTo(ToPoint(nextStart.Value));
                        AppendSegment(path, segment);
                        nextStart = null;
                    }
                }
                if (!mixed && (figure.IsClosed || closeFillContours)) path.Close();
            }
        }

        private static Point? SegmentEnd(PathSegment segment)
        {
            switch (segment)
            {
                case LineSegment line: return line.Point;
                case BezierSegment cubic: return cubic.Point3;
                case QuadraticBezierSegment quadratic: return quadratic.Point2;
                case ArcSegment arc: return arc.Point;
                case PolyLineSegment lines: return lines.Points.Count == 0 ? null : lines.Points[lines.Points.Count - 1];
                case PolyBezierSegment cubics:
                    int cubicCount = cubics.Points.Count / 3 * 3;
                    return cubicCount == 0 ? null : cubics.Points[cubicCount - 1];
                case PolyQuadraticBezierSegment quadratics:
                    int quadraticCount = quadratics.Points.Count / 2 * 2;
                    return quadraticCount == 0 ? null : quadratics.Points[quadraticCount - 1];
                default: throw new NotSupportedException($"Portable rendering does not support path segment {segment.GetType().FullName}.");
            }
        }

        private static void AppendSegment(SKPath path, PathSegment segment)
        {
            switch (segment)
            {
                case LineSegment line:
                    path.LineTo(ToPoint(line.Point)); break;
                case BezierSegment cubic:
                    path.CubicTo(ToPoint(cubic.Point1), ToPoint(cubic.Point2), ToPoint(cubic.Point3)); break;
                case QuadraticBezierSegment quadratic:
                    path.QuadTo(ToPoint(quadratic.Point1), ToPoint(quadratic.Point2)); break;
                case PolyLineSegment lines:
                    foreach (Point point in lines.Points) path.LineTo(ToPoint(point)); break;
                case PolyBezierSegment cubics:
                    for (int index = 0; index + 2 < cubics.Points.Count; index += 3)
                        path.CubicTo(ToPoint(cubics.Points[index]), ToPoint(cubics.Points[index + 1]), ToPoint(cubics.Points[index + 2]));
                    break;
                case PolyQuadraticBezierSegment quadratics:
                    for (int index = 0; index + 1 < quadratics.Points.Count; index += 2)
                        path.QuadTo(ToPoint(quadratics.Points[index]), ToPoint(quadratics.Points[index + 1]));
                    break;
                case ArcSegment arc:
                    path.ArcTo(Scalar(arc.Size.Width), Scalar(arc.Size.Height), Scalar(arc.RotationAngle),
                        arc.IsLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
                        arc.SweepDirection == SweepDirection.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                        Scalar(arc.Point.X), Scalar(arc.Point.Y));
                    break;
                default:
                    throw new NotSupportedException($"Portable rendering does not support path segment {segment.GetType().FullName}.");
            }
        }
    }
}
#endif
