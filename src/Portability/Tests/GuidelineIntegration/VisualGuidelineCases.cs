using System.Reflection;
using System.Windows;
using System.Windows.Media;

internal static class VisualGuidelineCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null);

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        var results = new List<Result>();
        Check("visual-guidelines-captured-classic-border-order", () => Compare(
            [0, 416, 1, 415, 2, 416], [0, 70, 1, 69, 2, 70], Matrix.Identity, new DpiScale(1, 1), false));
        Check("visual-guidelines-fractional-frozen-order", () => Compare(
            [0.25, 16.75, 1.25, 15.75, 2.25, 16.75], [0.25, 18.75, 1.25, 17.75, 2.25, 18.75],
            Matrix.Identity, new DpiScale(1.5, 1.25), true));
        Check("visual-guidelines-reflected-order", () => Compare(
            [0.25, 16.75, 1.25, 15.75, 2.25, 16.75], [0.25, 18.75, 1.25, 17.75, 2.25, 18.75],
            new Matrix(-1, 0, 0, -1, 24, 22), new DpiScale(1, 1), false));
#if !STOCK
        Check("visual-guidelines-NaN-rejects", () => Reject(new GuidelineVisual([1, double.NaN, 0], [0, 2], Matrix.Identity)));
        Check("drawing-guidelines-static-unsorted-rejects", () => Reject(DrawingSet([2, 1], [0, 2], false)));
        Check("drawing-guidelines-static-NaN-rejects", () => Reject(DrawingSet([0, double.NaN], [0, 2], false)));
        Check("drawing-guidelines-dynamic-odd-pair-rejects", () => Reject(DrawingSet([0], [0, 1], true)));
        Check("drawing-guidelines-dynamic-unsorted-rejects", () => Reject(DrawingSet([2, 0, 1, 0], [0, 1], true)));
        Check("drawing-guidelines-dynamic-NaN-rejects", () => Reject(DrawingSet([0, double.NaN], [0, 1], true)));
#endif
        return results;

        void Check(string name, Action action)
        {
            try { action(); results.Add(new(name, true)); }
            catch (Exception error) { results.Add(new(name, false, error.ToString())); }
        }

        void Compare(double[] x, double[] y, Matrix transform, DpiScale dpi, bool freeze)
        {
            var visual = new GuidelineVisual(x, y, transform);
            if (freeze) { visual.X.Freeze(); visual.Y.Freeze(); }
            byte[] actual = render(visual, dpi, 40, 32);
            var ordered = new GuidelineVisual(x.Order().ToArray(), y.Order().ToArray(), transform);
            byte[] expected = render(ordered, dpi, 40, 32);
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Visual guideline order changed the complete raster.");
            if (!actual.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0))
                throw new InvalidOperationException("Both visual guideline controls rendered empty.");
            if (!visual.X.SequenceEqual(x) || !visual.Y.SequenceEqual(y))
                throw new InvalidOperationException("Rendering reordered the caller-owned visual guideline collections.");
        }

#if !STOCK
        void Reject(Visual visual)
        {
            try { render(visual, new DpiScale(1, 1), 40, 32); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("Malformed guideline data was accepted.");
        }
#endif
    }

#if !STOCK
    private static DrawingVisual DrawingSet(double[] x, double[] y, bool dynamic)
    {
        var guidelines = dynamic ? (GuidelineSet)Activator.CreateInstance(typeof(GuidelineSet),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<double>(), Array.Empty<double>(), true], null)! : new GuidelineSet(x, y);
        if (dynamic) { guidelines.GuidelinesX = new DoubleCollection(x); guidelines.GuidelinesY = new DoubleCollection(y); }
        var visual = new DrawingVisual();
        using var drawing = visual.RenderOpen();
        drawing.PushGuidelineSet(guidelines);
        drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0.25, 16.5, 18.5));
        drawing.Pop();
        return visual;
    }
#endif

    private sealed class GuidelineVisual : DrawingVisual
    {
        internal DoubleCollection X { get; }
        internal DoubleCollection Y { get; }

        internal GuidelineVisual(double[] x, double[] y, Matrix transform)
        {
            X = new DoubleCollection(x); Y = new DoubleCollection(y);
            VisualXSnappingGuidelines = X; VisualYSnappingGuidelines = Y;
            Transform = new MatrixTransform(transform);
            using var drawing = RenderOpen();
            drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0.25, 16.5, 18.5));
            drawing.DrawLine(new Pen(Brushes.Blue, 1), new Point(1.25, 1.25), new Point(15.75, 17.75));
        }
    }
}
