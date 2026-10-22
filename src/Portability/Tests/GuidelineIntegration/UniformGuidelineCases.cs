using System.Windows;
using System.Windows.Media;

internal static class UniformGuidelineCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null);

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        var results = new List<Result>();
        var dpi = new DpiScale(1, 1);
        var translated = new Matrix(1, 0, 0, 1, 2.5, 2.5);
        Check("uniform-rounded-original-transparent-border", () => Compare([0, 35], [0, 35], translated, dpi, transparent: true, deviceOffset: new Vector(0.5, 0.5)));
        Check("uniform-rounded-visible-border", () => Compare([0, 35], [0, 35], translated, dpi, deviceOffset: new Vector(0.5, 0.5)));
        Check("uniform-rounded-negative-offset", () => Compare([0, 35], [0, 35], new Matrix(1, 0, 0, 1, 2.25, 2.25), dpi, deviceOffset: new Vector(-0.25, -0.25)));
        Check("uniform-rounded-negative-origin", () => Compare([0, 35], [0, 35], new Matrix(1, 0, 0, 1, -2.25, -2.25), dpi, deviceOffset: new Vector(0.25, 0.25)));
        Check("uniform-rounded-empty-X-duplicate-Y", () => Compare([], [0, 0, 35, 35], translated, dpi));
        Check("uniform-rounded-duplicate-X-empty-Y", () => Compare([0, 0, 35, 35], [], translated, dpi));
        Check("uniform-rounded-reflected-both-axes", () => Compare([0, 35], [0, 35], new Matrix(-1, 0, 0, -1, 40.5, 40.5), dpi));
        Check("uniform-rounded-double-DPI", () => Compare([0, 35], [0, 35], translated, new DpiScale(2, 2)));
        Check("uniform-rounded-fractional-DPI-even-span", () => Compare([0, 36], [0, 36], translated, new DpiScale(1.5, 1.5)));
        Check("uniform-rounded-widened-stroke", () => Compare([0, 35], [0, 35], translated, dpi, stroke: true));
        Check("uniform-ellipse-native-conic", () => Compare([0, 35], [0, 35], translated, dpi, ellipse: true));
#if !STOCK
        Check("nonuniform-conic-X-remains-explicit", () => Reject([0, 35.25], [0, 35], dpi));
        Check("nonuniform-conic-Y-remains-explicit", () => Reject([0, 35], [0, 35.25], dpi));
        Check("nonuniform-conic-interior-offset-remains-explicit", () => Reject([0, 7.25, 35], [0, 35], dpi));
        Check("nonuniform-conic-near-offset-remains-explicit", () => Reject([0, 35.0009765625], [0, 35], dpi));
        Check("nonuniform-conic-fractional-DPI-odd-span-remains-explicit", () => Reject([0, 35], [0, 35], new DpiScale(1.5, 1.5)));
#endif
        return results;

        void Check(string name, Action action)
        {
            try { action(); results.Add(new(name, true)); }
            catch (Exception error) { results.Add(new(name, false, error.ToString())); }
        }

        void Compare(double[] x, double[] y, Matrix transform, DpiScale scale, bool transparent = false,
            Vector? deviceOffset = null, bool stroke = false, bool ellipse = false)
        {
            byte[] actual = render(new ConicVisual(x, y, transform, transparent, stroke, ellipse), scale, 96, 96);
            byte[] single = render(new ConicVisual(x.Take(1).ToArray(), y.Take(1).ToArray(), transform, transparent, stroke, ellipse), scale, 96, 96);
            if (!actual.SequenceEqual(single)) throw new InvalidOperationException("Uniform multiple guidelines changed the complete single-guide raster.");
            if (deviceOffset is Vector offset)
            {
                Matrix device = transform;
                device.Translate(offset.X, offset.Y);
                byte[] expected = render(new ConicVisual([], [], device, transparent, stroke, ellipse), scale, 96, 96);
                if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Uniform guidelines differed from explicit device translation.");
            }
            bool colored = actual.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0);
            if (colored == transparent) throw new InvalidOperationException("The visible/transparent rounded control had incorrect coverage.");
        }

#if !STOCK
        void Reject(double[] x, double[] y, DpiScale scale)
        {
            try { render(new ConicVisual(x, y, translated, false, false, true), scale, 96, 96); }
            catch (NotSupportedException error) when (error.Message.Contains("rational conic", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("A nonuniform rational-conic frame was silently accepted.");
        }
#endif
    }

    private sealed class ConicVisual : DrawingVisual
    {
        internal ConicVisual(double[] x, double[] y, Matrix transform, bool transparent, bool stroke, bool ellipse)
        {
            VisualXSnappingGuidelines = new DoubleCollection(x); VisualYSnappingGuidelines = new DoubleCollection(y);
            Transform = new MatrixTransform(transform);
            using var drawing = RenderOpen();
            Brush fill = transparent ? Brushes.Transparent : Brushes.Red;
            Pen? pen = stroke ? new Pen(Brushes.Blue, 2) : null;
            if (ellipse) drawing.DrawEllipse(fill, pen, new Point(17.5, 17.5), 17.5, 17.5);
            else drawing.DrawRoundedRectangle(fill, pen, new Rect(0, 0, 35, 35), 1, 1);
        }
    }
}
