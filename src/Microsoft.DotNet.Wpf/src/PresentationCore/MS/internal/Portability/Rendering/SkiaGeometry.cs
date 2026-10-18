// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal static class SkiaGeometry
    {
        internal static SKPath Create(Geometry geometry, bool stroke)
        {
            var result = new SKPath();
            try
            {
                Append(result, geometry, stroke);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static void Append(SKPath destination, Geometry geometry, bool stroke)
        {
            using (var path = new SKPath())
            {
                switch (geometry)
                {
                    case RectangleGeometry rectangle:
                        if (!rectangle.Rect.IsEmpty)
                            path.AddRoundRect(SkiaDrawingContextWalker.ToRect(rectangle.Rect), (float)Math.Abs(rectangle.RadiusX), (float)Math.Abs(rectangle.RadiusY));
                        break;
                    case EllipseGeometry ellipse:
                        path.AddOval(new SKRect((float)(ellipse.Center.X - Math.Abs(ellipse.RadiusX)), (float)(ellipse.Center.Y - Math.Abs(ellipse.RadiusY)),
                            (float)(ellipse.Center.X + Math.Abs(ellipse.RadiusX)), (float)(ellipse.Center.Y + Math.Abs(ellipse.RadiusY))));
                        break;
                    case LineGeometry line:
                        path.MoveTo(SkiaDrawingContextWalker.ToPoint(line.StartPoint));
                        path.LineTo(SkiaDrawingContextWalker.ToPoint(line.EndPoint));
                        break;
                    case GeometryGroup group:
                        foreach (Geometry child in group.Children) Append(path, child, stroke);
                        path.FillType = group.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                        break;
                    case PathGeometry pathGeometry:
                        AppendFigures(path, pathGeometry, stroke);
                        break;
                    case StreamGeometry stream:
                        // Parsing WPF's serialized commands stays managed and preserves segment flags.
                        AppendFigures(path, stream.GetAsPathGeometry(), stroke);
                        break;
                    default:
                        throw new NotSupportedException($"Portable rendering does not support geometry type {geometry.GetType().FullName}.");
                }
                if (geometry.Transform != null && !geometry.Transform.Value.IsIdentity)
                    path.Transform(SkiaDrawingContextWalker.ToMatrix(geometry.Transform.Value));
                destination.FillType = path.FillType;
                destination.AddPath(path);
            }
        }

        private static void AppendFigures(SKPath path, PathGeometry geometry, bool stroke)
        {
            path.FillType = geometry.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            foreach (PathFigure figure in geometry.Figures)
            {
                if (!stroke && !figure.IsFilled) continue;
                path.MoveTo(SkiaDrawingContextWalker.ToPoint(figure.StartPoint));
                foreach (PathSegment segment in figure.Segments)
                {
                    if (stroke && !segment.IsStroked)
                        throw new NotSupportedException("Portable rendering does not support unstroked segments in a stroked path.");
                    switch (segment)
                    {
                        case LineSegment line:
                            path.LineTo(SkiaDrawingContextWalker.ToPoint(line.Point)); break;
                        case BezierSegment cubic:
                            path.CubicTo(SkiaDrawingContextWalker.ToPoint(cubic.Point1), SkiaDrawingContextWalker.ToPoint(cubic.Point2), SkiaDrawingContextWalker.ToPoint(cubic.Point3)); break;
                        case QuadraticBezierSegment quadratic:
                            path.QuadTo(SkiaDrawingContextWalker.ToPoint(quadratic.Point1), SkiaDrawingContextWalker.ToPoint(quadratic.Point2)); break;
                        case PolyLineSegment lines:
                            foreach (Point point in lines.Points) path.LineTo(SkiaDrawingContextWalker.ToPoint(point)); break;
                        case PolyBezierSegment cubics:
                            for (int index = 0; index + 2 < cubics.Points.Count; index += 3)
                                path.CubicTo(SkiaDrawingContextWalker.ToPoint(cubics.Points[index]), SkiaDrawingContextWalker.ToPoint(cubics.Points[index + 1]), SkiaDrawingContextWalker.ToPoint(cubics.Points[index + 2]));
                            break;
                        case PolyQuadraticBezierSegment quadratics:
                            for (int index = 0; index + 1 < quadratics.Points.Count; index += 2)
                                path.QuadTo(SkiaDrawingContextWalker.ToPoint(quadratics.Points[index]), SkiaDrawingContextWalker.ToPoint(quadratics.Points[index + 1]));
                            break;
                        case ArcSegment arc:
                            path.ArcTo((float)arc.Size.Width, (float)arc.Size.Height, (float)arc.RotationAngle,
                                arc.IsLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
                                arc.SweepDirection == SweepDirection.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                                (float)arc.Point.X, (float)arc.Point.Y);
                            break;
                        default:
                            throw new NotSupportedException($"Portable rendering does not support path segment {segment.GetType().FullName}.");
                    }
                }
                if (figure.IsClosed) path.Close();
            }
        }
    }
}
#endif
