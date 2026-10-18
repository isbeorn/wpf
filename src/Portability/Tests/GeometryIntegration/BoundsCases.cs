using System.Windows;
using System.Windows.Media;
using System.Text.Json;
using System.IO;

internal static class BoundsCases
{
    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        var precisionReference = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "BoundsPrecisionReference.json")))!;
        foreach (var sample in PrecisionSamples())
        {
            double[] expected = precisionReference[sample.Name];
            float magnitude = (float)Math.Max(Math.Max(Math.Abs(expected[0]), Math.Abs(expected[1])), Math.Max(Math.Abs(expected[2]), Math.Abs(expected[3])));
            double tolerance = Math.Max(1e-6, (double)MathF.BitIncrement(magnitude) - magnitude);
            check("stock-precision-" + sample.Name, () =>
            {
#if PORTABLE_GEOMETRY_TESTS
                if (!sample.IsRepresentable) return Unsupported(() => sample.Bounds(), "precision");
#endif
                return Equal(sample.Bounds(), new Rect(expected[0], expected[1], expected[2], expected[3]), tolerance);
            }, true);
        }
        var pen = new Pen(Brushes.Red, 4);
        var transformed = new RectangleGeometry(new Rect(0, 0, 10, 10))
            { Transform = new MatrixTransform(2, 0, 0, 3, 40, 20) };
        check("render-bounds-geometry-transform-before-pen", () => Equal(transformed.GetRenderBounds(pen), new Rect(38, 18, 24, 34)), true);
        var path = PathGeometry.CreateFromGeometry(transformed);
        check("render-bounds-path-geometry-transform-before-pen", () => Equal(path.GetRenderBounds(pen), new Rect(38, 18, 24, 34)), true);
        check("retained-bounds-world-transform-after-pen", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, pen, path),
            new MatrixTransform(3, 0, 0, 2, 7, 11)), new Rect(121, 47, 72, 68)), true);
        check("retained-bounds-transformed-rectangle-command", () => Equal(Content(d => d.DrawRectangle(Brushes.Red, pen, new Rect(0, 0, 10, 10)),
            new MatrixTransform(3, 0, 0, 2, 7, 11)), new Rect(1, 7, 42, 28)), true);
        check("retained-bounds-transformed-line-command", () => Equal(Content(d => d.DrawLine(pen, new Point(0, 0), new Point(10, 0)),
            new MatrixTransform(3, 0, 0, 2, 7, 11)), new Rect(7, 7, 30, 8)), true);
        check("retained-bounds-transformed-ellipse-command", () => Equal(Content(d => d.DrawEllipse(Brushes.Red, pen, new Point(10, 10), 6, 4),
            new MatrixTransform(3, 0, 0, 2, 7, 11)), new Rect(13, 19, 48, 24), 0.03), true);
        var cubic = new PathGeometry([new PathFigure(new Point(0, 0),
            [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)])
            { Transform = new MatrixTransform(2, 0, 0, 3, 40, 20) };
        check("retained-bounds-transformed-cubic-fill", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, null, cubic)), new Rect(40, 20, 40, 45)), true);
        check("retained-bounds-transformed-cubic-pen", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, pen, cubic)), new Rect(38, 18, 44, 49), 0.03), true);
        check("render-bounds-absolute-tolerance", () => Equal(cubic.GetRenderBounds(pen, 0.01, ToleranceType.Absolute), new Rect(38, 18, 44, 49), 0.03), true);
        check("render-bounds-relative-tolerance", () => Equal(cubic.GetRenderBounds(pen, 0.0001, ToleranceType.Relative), new Rect(38, 18, 44, 49), 0.03), true);
#if PORTABLE_GEOMETRY_TESTS
        check("render-bounds-negative-tolerance-precision-diagnostic", () => Unsupported(() => cubic.GetRenderBounds(pen, -1, ToleranceType.Absolute), "precision"), true);
        check("render-bounds-nan-tolerance-precision-diagnostic", () => Unsupported(() => cubic.GetRenderBounds(pen, double.NaN, ToleranceType.Relative), "precision"), true);
#else
        check("render-bounds-negative-tolerance", () => Equal(cubic.GetRenderBounds(pen, -1, ToleranceType.Absolute), new Rect(38, 18, 44, 49), 0.03), true);
        check("render-bounds-nan-tolerance", () => Equal(cubic.GetRenderBounds(pen, double.NaN, ToleranceType.Relative), new Rect(38, 18, 44, 49), 0.03), true);
#endif
        var hollow = new PathGeometry([new PathFigure(new Point(10, 10), [new LineSegment(new Point(20, 10), true)], false) { IsFilled = false }]);
        check("render-bounds-hollow-fill", () => Equal(hollow.GetRenderBounds(null), new Rect(0, 0, 0, 0)), true);
        check("retained-bounds-hollow-fill", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, null, hollow)), new Rect(0, 0, 0, 0)), true);
        check("render-bounds-hollow-stroke", () => Equal(hollow.GetRenderBounds(pen), new Rect(10, 8, 10, 4)), true);
        check("render-bounds-hollow-zero-stroke", () => Equal(hollow.GetRenderBounds(new Pen(Brushes.Red, 0)), new Rect(0, 0, 10, 10)), true);
        check("retained-bounds-zero-stroke-fallback-before-world", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, new Pen(Brushes.Red, 0), hollow),
            new MatrixTransform(2, 0, 0, 3, 40, 20)), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-stroke-smaller-than-tolerance", () => Equal(hollow.GetRenderBounds(pen, 1000, ToleranceType.Absolute), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-infinite-tolerance-skips-stroke", () => Equal(hollow.GetRenderBounds(pen, double.PositiveInfinity, ToleranceType.Absolute), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-relative-tolerance-retains-stroke", () => Equal(hollow.GetRenderBounds(pen, 10, ToleranceType.Relative), new Rect(10, 8, 10, 4)), true);
        check("render-bounds-relative-tolerance-skips-stroke", () => Equal(hollow.GetRenderBounds(pen, 100, ToleranceType.Relative), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-thin-stroke-below-threshold", () => Equal(hollow.GetRenderBounds(new Pen(Brushes.Red, 0.0019)), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-thin-stroke-above-threshold", () => Equal(hollow.GetRenderBounds(new Pen(Brushes.Red, 0.0021)), new Rect(10, 9.99895, 10, 0.0021)), true);
        check("retained-bounds-world-scale-retains-thin-stroke", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, new Pen(Brushes.Red, 0.001), hollow),
            new MatrixTransform(2, 0, 0, 3, 40, 20)), new Rect(60, 49.9985, 20, 0.003)), true);
        var collapsed = new PathGeometry([new PathFigure(new Point(10, 10), [new LineSegment(new Point(10, 10), true)], false) { IsFilled = false }]);
        check("render-bounds-hollow-collapsed-flat-stroke", () => Equal(collapsed.GetRenderBounds(pen), new Rect(10, 8, 0, 4)), true);
        check("render-bounds-hollow-collapsed-square-stroke", () => Equal(collapsed.GetRenderBounds(new Pen(Brushes.Red, 4)
            { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square }), new Rect(8, 8, 4, 4)), true);
        check("render-bounds-hollow-collapsed-round-stroke", () => Equal(collapsed.GetRenderBounds(new Pen(Brushes.Red, 4)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), new Rect(8, 8, 4, 4)), true);
        var closedCollapsed = collapsed.Clone();
        closedCollapsed.Figures[0].IsClosed = true;
        check("render-bounds-hollow-closed-collapsed-stroke", () => Equal(closedCollapsed.GetRenderBounds(pen), new Rect(8, 8, 4, 4)), true);
        var constantCurve = new PathGeometry([new PathFigure(new Point(100000, 100000),
            [new BezierSegment(new Point(100000, 100000), new Point(100000, 100000), new Point(100000, 100000), true)], true)]);
        check("render-bounds-constant-curve-keeps-exact-cap", () => Equal(constantCurve.GetRenderBounds(pen, -1, ToleranceType.Absolute),
            new Rect(99998, 99998, 4, 4)), true);
        check("retained-bounds-collapsed-world-transform", () => Equal(Content(d => d.DrawGeometry(Brushes.Red, pen,
            new PathGeometry([new PathFigure(new Point(0, 0), [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)])),
            new MatrixTransform(0, 0, 0, 0, 10, 20)), new Rect(0, 0, 10, 20)), true);
        var separated = new PathGeometry([new PathFigure(new Point(0, 0), [new LineSegment(new Point(0, 0), true)], false) { IsFilled = false },
            new PathFigure(new Point(100, 100), [new LineSegment(new Point(100, 100), true)], false) { IsFilled = false }]);
        check("render-bounds-separated-collapsed-flat-contours", () => Equal(separated.GetRenderBounds(pen), new Rect(0, -2, 100, 104)), true);
        check("render-bounds-separated-collapsed-round-contours", () => Equal(separated.GetRenderBounds(new Pen(Brushes.Red, 4)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), new Rect(-2, -2, 104, 104)), true);
        separated.Figures[0].Segments[0] = new LineSegment(new Point(10, 0), true);
        check("render-bounds-ordinary-and-collapsed-contours", () => Equal(separated.GetRenderBounds(pen), new Rect(0, -2, 100, 104)), true);
        check("render-bounds-nan-stroke", () => cubic.GetRenderBounds(new Pen(Brushes.Red, double.NaN)).IsEmpty, true);
        check("render-bounds-hollow-null-brush-pen", () => Equal(hollow.GetRenderBounds(new Pen(null, 4)), new Rect(10, 8, 10, 4)), true);
        check("render-bounds-rectangle-null-brush-pen", () => Equal(transformed.GetRenderBounds(new Pen(null, 4)), new Rect(40, 20, 20, 30)), true);
        check("retained-bounds-hollow-null-brush-pen", () => Content(d => d.DrawGeometry(null, new Pen(null, 4), hollow)).IsEmpty, true);
        var mixed = new PathGeometry([new PathFigure(new Point(0, 0), [new LineSegment(new Point(10, 0), true),
            new LineSegment(new Point(10, 10), true)], true), hollow.Figures[0].Clone()]);
        check("render-bounds-skip-hollow-fill-extent", () => Equal(mixed.GetRenderBounds(null), new Rect(0, 0, 10, 10)), true);
        check("render-bounds-include-hollow-stroke-extent", () => mixed.GetRenderBounds(pen).Right >= 20, true);
        check("render-bounds-zero-width-filled-path", () => Equal(new PathGeometry([new PathFigure(new Point(10, 10),
            [new LineSegment(new Point(10, 30), true)], true)]).GetRenderBounds(null), new Rect(10, 10, 0, 20)), true);
        check("render-bounds-empty", () => new PathGeometry().GetRenderBounds(pen).IsEmpty, true);
        check("render-bounds-invalid-transform", () => new PathGeometry([new PathFigure(new Point(0, 0),
            [new LineSegment(new Point(10, 10), true)], true)]) { Transform = new MatrixTransform(double.NaN, 0, 0, 1, 0, 0) }.GetRenderBounds(pen).IsEmpty, true);
        foreach (PenLineCap cap in new[] { PenLineCap.Flat, PenLineCap.Square, PenLineCap.Round })
        {
            var capped = new Pen(Brushes.Red, 4) { StartLineCap = cap, EndLineCap = cap };
            Rect expected = cap == PenLineCap.Flat ? new Rect(10, 8, 10, 4) : new Rect(8, 8, 14, 4);
            check($"render-bounds-line-cap-{cap}", () => Equal(new LineGeometry(new Point(10, 10), new Point(20, 10))
                { Transform = new TranslateTransform(0, 0) }.GetRenderBounds(capped), expected), true);
        }
#if PORTABLE_GEOMETRY_TESTS
        check("render-bounds-dashed-pen-diagnostic", () => Unsupported(() => path.GetRenderBounds(new Pen(Brushes.Red, 4) { DashStyle = DashStyles.Dash }), "dashed"), true);
        check("render-bounds-mixed-cap-diagnostic", () => Unsupported(() => path.GetRenderBounds(new Pen(Brushes.Red, 4) { EndLineCap = PenLineCap.Round }), "caps"), true);
#endif
    }

    internal static Dictionary<string, double[]> CapturePrecision()
    {
        return PrecisionSamples().ToDictionary(sample => sample.Name, sample =>
        {
            Rect bounds = sample.Bounds();
            return new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height };
        });
    }

    private static List<(string Name, Func<Rect> Bounds, bool IsRepresentable)> PrecisionSamples()
    {
        var result = new List<(string, Func<Rect>, bool)>();
        foreach (double size in new[] { 0.001, 1.0, 1000.0 })
            foreach (double origin in new[] { 0, 100000 * size, -100000 * size })
                foreach (bool skew in new[] { false, true })
                    Add(FormattableString.Invariant($"curve-{size}-{origin}-{skew}"), Curve(size), 4 * size, origin, skew, size == 0.001 && origin == 0);
        foreach (double width in new[] { 0.0021, 0.2, 40.0 })
            Add(FormattableString.Invariant($"curve-width-{width}"), Curve(1), width, 0, true, false);
        foreach (double size in new[] { 0.001, 1.0, 1000.0 })
            Add(FormattableString.Invariant($"miter-{size}"), new PathGeometry([new PathFigure(new Point(0, 0),
                [new LineSegment(new Point(20 * size, 0), true), new LineSegment(new Point(0, 20 * size), true)], true)]), 4 * size, 100000 * size, true, true);
        return result;

        static PathGeometry Curve(double size) => new PathGeometry([new PathFigure(new Point(0, 0),
            [new BezierSegment(new Point(0, 20 * size), new Point(20 * size, 20 * size), new Point(20 * size, 0), true)], true)]);

        void Add(string name, PathGeometry geometry, double width, double origin, bool skew, bool representable)
        {
            geometry.Transform = new TranslateTransform(origin, -origin / 2);
            var pen = new Pen(Brushes.Red, width);
            Matrix matrix = skew ? new Matrix(2, 0.25, 0.5, 3, 5, 7) : Matrix.Identity;
            foreach (bool nan in new[] { false, true })
            {
                result.Add((name + (nan ? "-nan" : "-negative"), () =>
                {
                    if (matrix.IsIdentity) return geometry.GetRenderBounds(pen, nan ? double.NaN : -1, nan ? ToleranceType.Relative : ToleranceType.Absolute);
                    var method = typeof(Geometry).GetMethod("GetBoundsInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null, [typeof(Pen), typeof(Matrix), typeof(double), typeof(ToleranceType)], null)!;
                    try { return (Rect)method.Invoke(geometry, [pen, matrix, nan ? double.NaN : -1, nan ? ToleranceType.Relative : ToleranceType.Absolute])!; }
                    catch (System.Reflection.TargetInvocationException exception)
                    { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
                }, representable));
            }
        }
    }

    private static Rect Content(Action<DrawingContext> draw, Transform? transform = null)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            if (transform != null) context.PushTransform(transform);
            draw(context);
            if (transform != null) context.Pop();
        }
        return VisualTreeHelper.GetContentBounds(visual);
    }

    private static bool Equal(Rect actual, Rect expected, double tolerance = 0.001)
    {
        if (actual.IsEmpty || !double.IsFinite(actual.X) || !double.IsFinite(actual.Y) || !double.IsFinite(actual.Width) || !double.IsFinite(actual.Height) ||
            Math.Abs(actual.X - expected.X) > tolerance || Math.Abs(actual.Y - expected.Y) > tolerance ||
            Math.Abs(actual.Width - expected.Width) > tolerance || Math.Abs(actual.Height - expected.Height) > tolerance)
            throw new InvalidOperationException($"Bounds were {actual}, expected {expected} within {tolerance}.");
        return true;
    }

#if PORTABLE_GEOMETRY_TESTS
    private static bool Unsupported(Action action, string diagnostic)
    {
        try { action(); return false; }
        catch (NotSupportedException exception) { return exception.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase); }
    }
#endif
}
