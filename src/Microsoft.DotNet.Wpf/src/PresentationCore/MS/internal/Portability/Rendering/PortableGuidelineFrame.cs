// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal sealed class PortableGuidelineFrame
    {
        internal static readonly PortableGuidelineFrame Empty = new PortableGuidelineFrame([], [], [], []);
        private readonly float[] _x, _offsetX, _y, _offsetY;
        private PortableGuidelineFrame(float[] x, float[] offsetX, float[] y, float[] offsetY)
        { _x = x; _offsetX = offsetX; _y = y; _offsetY = offsetY; }
        internal bool IsEmpty => _x.Length == 0 && _y.Length == 0;
        internal bool IsSimple => _x.Length <= 1 && _y.Length <= 1;
        internal float OffsetX(float value) => FindOffset(value, _x, _offsetX);
        internal float OffsetY(float value) => FindOffset(value, _y, _offsetY);

        internal static PortableGuidelineFrame Create(DoubleCollection x, DoubleCollection y, bool dynamic, SKMatrix matrix)
        {
            // CSnappingFrame::PushFrame replaces the parent frame even when this transform cannot snap.
            if (matrix.SkewX != 0 || matrix.SkewY != 0 || matrix.Persp0 != 0 || matrix.Persp1 != 0 || matrix.Persp2 != 1)
                return Empty;
            if ((x == null || x.Count == 0) && (y == null || y.Count == 0)) return Empty;
            (float[] coordinatesX, float[] offsetsX) = Build(x, dynamic, matrix.ScaleX, matrix.TransX);
            (float[] coordinatesY, float[] offsetsY) = Build(y, dynamic, matrix.ScaleY, matrix.TransY);
            return new PortableGuidelineFrame(coordinatesX, offsetsX, coordinatesY, offsetsY);
        }

        private static (float[], float[]) Build(DoubleCollection values, bool dynamic, float scale, float translation)
        {
            int count = values == null ? 0 : values.Count;
            if (dynamic && count % 2 != 0) throw new ArgumentException("Dynamic guidelines require leading-coordinate and shift pairs.");
            int step = dynamic ? 2 : 1;
            count /= step;
            var coordinates = new float[count];
            var offsets = new float[count];
            float previous = float.NegativeInfinity;
            for (int index = 0; index < count; index++)
            {
                float leading = (float)values[index * step];
                float shift = dynamic ? (float)values[index * step + 1] : 0;
                float local = leading + shift;
                if (float.IsNaN(local) || local < previous)
                    throw new ArgumentException("Guidelines must be ordered by their driven coordinate and cannot contain NaN.");
                previous = local;
                float deviceLeading = leading * scale + translation;
                float deviceShift = shift * scale;
                int destination = scale < 0 ? count - index - 1 : index;
                coordinates[destination] = deviceLeading + deviceShift;
                // Stable StoreRangeDynamic rounds the gap separately from its leading coordinate.
                offsets[destination] = RoundedOffset(deviceLeading) + (dynamic ? RoundedOffset(deviceShift) : 0);
            }
            return (coordinates, offsets);
        }

        private static float RoundedOffset(float value)
        {
            if (!(MathF.Abs(value) < 8388608f)) return 0;
            float offset = MathF.Floor(value + 0.5f) - value;
            return offset <= -0.5f ? offset + 1 : offset;
        }

        private static float FindOffset(float value, float[] coordinates, float[] offsets)
        {
            if (coordinates.Length == 0) return 0;
            int lower = 0, upper = coordinates.Length - 1;
            if (value > coordinates[upper]) return offsets[upper];
            if (value <= coordinates[0]) return offsets[0];
            while (upper - lower > 1)
            {
                int middle = (lower + upper) / 2;
                if (value > coordinates[middle]) lower = middle;
                else upper = middle;
            }
            return offsets[coordinates[upper] - value < value - coordinates[lower] ? upper : lower];
        }

        internal SKPath SnapPath(SKPath path, SKMatrix matrix)
        {
            if (IsSimple)
            {
                var transformed = new SKPath(path);
                transformed.Transform(matrix);
                transformed.Transform(SKMatrix.CreateTranslation(OffsetX(0), OffsetY(0)));
                return transformed;
            }
            var result = new SKPath { FillType = path.FillType };
            try
            {
                using (SKPath.RawIterator iterator = path.CreateRawIterator())
                {
                    var points = new SKPoint[4];
                    SKPathVerb verb;
                    while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                    {
                        if (verb == SKPathVerb.Quad)
                        {
                            // PathFigureData::SetQuadraticBezier elevates the unsnapped local curve first.
                            SKPoint start = points[0], control = points[1], end = points[2];
                            points[1] = Elevate(start, control);
                            points[2] = Elevate(end, control);
                            points[3] = end;
                            verb = SKPathVerb.Cubic;
                        }
                        for (int index = 0; index < points.Length; index++)
                        {
                            SKPoint device = matrix.MapPoint(points[index]);
                            points[index] = new SKPoint(device.X + OffsetX(device.X), device.Y + OffsetY(device.Y));
                        }
                        switch (verb)
                        {
                            case SKPathVerb.Move: result.MoveTo(points[0]); break;
                            case SKPathVerb.Line: result.LineTo(points[1]); break;
                            case SKPathVerb.Cubic: result.CubicTo(points[1], points[2], points[3]); break;
                            case SKPathVerb.Close: result.Close(); break;
                            case SKPathVerb.Conic:
                                throw new NotSupportedException("Multiple portable guidelines over rational conic curves require native cubic-shape calibration.");
                        }
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static SKPoint Elevate(SKPoint endpoint, SKPoint control) => new SKPoint(
            (float)((1d / 3d) * endpoint.X + (2d / 3d) * control.X),
            (float)((1d / 3d) * endpoint.Y + (2d / 3d) * control.Y));
    }
}
#endif
