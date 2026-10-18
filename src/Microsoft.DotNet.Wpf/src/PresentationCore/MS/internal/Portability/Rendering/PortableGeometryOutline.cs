// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal static class PortableGeometryOutline
    {
        internal static PathGeometry Create(Geometry geometry, double tolerance, ToleranceType type)
        {
            try
            {
                using (SKPath input = SkiaGeometry.Create(geometry, false, closeFillContours: true))
                using (SKPath bounds = SkiaGeometry.Create(geometry, false, includeHollows: true))
                {
                    if (input.IsEmpty) return new PathGeometry();
                    double absolute = SkiaGeometry.AbsoluteTolerance(bounds.TightBounds, tolerance, type);
                    if (double.IsNaN(absolute)) return new PathGeometry();
                    using (SKPath polynomial = FlattenConics(input, absolute))
                    using (SKPath simplified = polynomial.Simplify())
                    {
                        if (simplified == null)
                            throw new InvalidOperationException("Portable geometry outline simplification failed.");
                        if (input.FillType == SKPathFillType.Winding)
                        {
                            using (SKPath winding = simplified.ToWinding())
                            {
                                if (winding == null)
                                    throw new InvalidOperationException("Portable geometry outline winding normalization failed.");
                                return ToGeometry(winding, FillRule.Nonzero);
                            }
                        }
                        return ToGeometry(simplified, FillRule.EvenOdd);
                    }
                }
            }
            catch (ArithmeticException) { return new PathGeometry(); }
        }

        private static SKPath FlattenConics(SKPath input, double tolerance)
        {
            var result = new SKPath { FillType = input.FillType };
            try
            {
                using (SKPath.RawIterator iterator = input.CreateRawIterator())
                {
                    var points = new SKPoint[4];
                    SKPathVerb verb;
                    int segments = 0;
                    while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                    {
                        switch (verb)
                        {
                            case SKPathVerb.Move: result.MoveTo(points[0]); break;
                            case SKPathVerb.Line: result.LineTo(points[1]); break;
                            case SKPathVerb.Quad: result.QuadTo(points[1], points[2]); break;
                            case SKPathVerb.Cubic: result.CubicTo(points[1], points[2], points[3]); break;
                            case SKPathVerb.Conic:
                                float weight = iterator.ConicWeight();
                                if (!float.IsFinite(weight) || weight <= 0)
                                    throw new NotSupportedException("Portable geometry outlines require positive finite conic weights.");
                                if (weight == 1)
                                    result.QuadTo(points[1], points[2]);
                                else
                                    AddConic(result, new HomogeneousPoint(points[0], 1), new HomogeneousPoint(points[1], weight),
                                        new HomogeneousPoint(points[2], 1), tolerance, 0, ref segments);
                                break;
                            case SKPathVerb.Close: result.Close(); break;
                            default: throw new NotSupportedException($"Portable geometry outlines do not support path verb {verb}.");
                        }
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static void AddConic(SKPath path, HomogeneousPoint p0, HomogeneousPoint p1, HomogeneousPoint p2,
            double tolerance, int depth, ref int segments)
        {
            Point start = p0.Point, control = p1.Point, end = p2.Point;
            SKPoint quantizedStart = Quantize(start), quantizedEnd = Quantize(end);
            double rounding = Math.Max(Distance(start, quantizedStart), Distance(end, quantizedEnd));
            if (rounding > tolerance)
                throw PrecisionFailure();
            // Positive rational weights keep the curve inside its control hull.
            if (DistanceToSegment(control, start, end) + rounding <= tolerance)
            {
                if (++segments > 65536) throw PrecisionFailure();
                path.LineTo(quantizedEnd);
                return;
            }
            if (depth >= 24) throw PrecisionFailure();
            HomogeneousPoint left = HomogeneousPoint.Midpoint(p0, p1);
            HomogeneousPoint right = HomogeneousPoint.Midpoint(p1, p2);
            HomogeneousPoint middle = HomogeneousPoint.Midpoint(left, right);
            AddConic(path, p0, left, middle, tolerance, depth + 1, ref segments);
            AddConic(path, middle, right, p2, tolerance, depth + 1, ref segments);
        }

        private static PathGeometry ToGeometry(SKPath path, FillRule fillRule)
        {
            var geometry = new PathGeometry { FillRule = fillRule };
            using (SKPath.RawIterator iterator = path.CreateRawIterator())
            {
                var points = new SKPoint[4];
                PathFigure figure = null;
                SKPathVerb verb;
                while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                {
                    switch (verb)
                    {
                        case SKPathVerb.Move:
                            figure = new PathFigure { StartPoint = ToPoint(points[0]), IsFilled = true };
                            geometry.Figures.Add(figure);
                            break;
                        case SKPathVerb.Line: figure.Segments.Add(new LineSegment(ToPoint(points[1]), true)); break;
                        case SKPathVerb.Quad: figure.Segments.Add(new QuadraticBezierSegment(ToPoint(points[1]), ToPoint(points[2]), true)); break;
                        case SKPathVerb.Cubic: figure.Segments.Add(new BezierSegment(ToPoint(points[1]), ToPoint(points[2]), ToPoint(points[3]), true)); break;
                        case SKPathVerb.Close: figure.IsClosed = true; break;
                        default: throw new NotSupportedException($"Portable outlined path conversion does not support path verb {verb}.");
                    }
                }
            }
            for (int index = geometry.Figures.Count - 1; index >= 0; index--)
                if (!HasArea(geometry.Figures[index])) geometry.Figures.RemoveAt(index);
            return geometry;
        }

        private static bool HasArea(PathFigure figure)
        {
            Point current = figure.StartPoint;
            double area = 0;
            foreach (PathSegment segment in figure.Segments)
            {
                if (segment is LineSegment line)
                { area += SegmentArea(current, line.Point, default, default, 1); current = line.Point; }
                else if (segment is QuadraticBezierSegment quad)
                { area += SegmentArea(current, quad.Point1, quad.Point2, default, 2); current = quad.Point2; }
                else if (segment is BezierSegment cubic)
                { area += SegmentArea(current, cubic.Point1, cubic.Point2, cubic.Point3, 3); current = cubic.Point3; }
            }
            area += SegmentArea(current, figure.StartPoint, default, default, 1);
            return area != 0;
        }

        private static double SegmentArea(Point p0, Point p1, Point p2, Point p3, int degree)
        {
            Span<Vector> power = stackalloc Vector[4];
            Vector a = new Vector(p0.X, p0.Y), b = new Vector(p1.X, p1.Y), c = new Vector(p2.X, p2.Y), d = new Vector(p3.X, p3.Y);
            power[0] = a;
            if (degree == 1) power[1] = b - a;
            else if (degree == 2) { power[1] = 2 * (b - a); power[2] = a - 2 * b + c; }
            else { power[1] = 3 * (b - a); power[2] = 3 * (a - 2 * b + c); power[3] = -a + 3 * b - 3 * c + d; }
            double area = 0;
            for (int i = 0; i <= degree; i++)
                for (int j = 1; j <= degree; j++)
                    area += j * (power[i].X * power[j].Y - power[i].Y * power[j].X) / (i + j);
            return area;
        }

        private static SKPoint Quantize(Point point)
        {
            var result = new SKPoint((float)point.X, (float)point.Y);
            if (!float.IsFinite(result.X) || !float.IsFinite(result.Y)) throw PrecisionFailure();
            return result;
        }
        private static Point ToPoint(SKPoint point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                throw new InvalidOperationException("Portable geometry simplification produced non-finite coordinates.");
            return new Point(point.X, point.Y);
        }
        private static double Distance(Point point, SKPoint other) => double.Hypot(point.X - other.X, point.Y - other.Y);
        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            double x = end.X - start.X, y = end.Y - start.Y;
            double length = x * x + y * y;
            double t = length == 0 ? 0 : Math.Clamp(((point.X - start.X) * x + (point.Y - start.Y) * y) / length, 0, 1);
            return double.Hypot(point.X - start.X - t * x, point.Y - start.Y - t * y);
        }
        private static NotSupportedException PrecisionFailure() => new NotSupportedException(
            "Portable conic outline conversion cannot satisfy the requested tolerance within raster precision and subdivision limits.");

        private readonly struct HomogeneousPoint
        {
            private readonly double _x, _y, _weight;
            internal HomogeneousPoint(SKPoint point, double weight) { _x = point.X * weight; _y = point.Y * weight; _weight = weight; }
            private HomogeneousPoint(double x, double y, double weight) { _x = x; _y = y; _weight = weight; }
            internal Point Point => new Point(_x / _weight, _y / _weight);
            internal static HomogeneousPoint Midpoint(HomogeneousPoint first, HomogeneousPoint second) =>
                new HomogeneousPoint((first._x + second._x) / 2, (first._y + second._y) / 2, (first._weight + second._weight) / 2);
        }
    }
}
#endif
