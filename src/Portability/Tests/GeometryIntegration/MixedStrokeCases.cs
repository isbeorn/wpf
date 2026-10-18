using System.Windows;
using System.Windows.Media;

internal static class MixedStrokeCases
{
    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        var pen = new Pen(Brushes.Red, 4) { DashCap = PenLineCap.Flat };
        var pairs = new StreamGeometry();
        using (var context = pairs.Open())
        {
            context.BeginFigure(new Point(10, 10), false, false);
            context.LineTo(new Point(30, 10), true, false);
            context.LineTo(new Point(10, 30), false, false);
            context.LineTo(new Point(30, 30), true, false);
        }
        pairs.Freeze();
        check("mixed-oxyplot-disconnected-pairs-bounds", () => Equal(pairs.GetRenderBounds(pen), MixedBounds(new Rect(10, 8, 20, 24), new Point(10, 10))), true);
        check("mixed-oxyplot-disconnected-pairs-first-hit", () => pairs.StrokeContains(pen, new Point(20, 10), 0, ToleranceType.Absolute), true);
        check("mixed-oxyplot-disconnected-pairs-second-hit", () => pairs.StrokeContains(pen, new Point(20, 30), 0, ToleranceType.Absolute), true);
        check("mixed-oxyplot-disconnected-pairs-gap-miss", () => pairs.StrokeContains(pen, new Point(20, 20), 0, ToleranceType.Absolute), false);
        check("mixed-oxyplot-retained-bounds", () => Equal(Content(pairs, pen), MixedBounds(new Rect(10, 8, 20, 24), new Point(10, 10))), true);
        check("mixed-oxyplot-path-canvas-layout-and-pixels", () => LayoutAndRender(pairs), true);
        check("mixed-oxyplot-source-bounds", () => Equal(pairs.Bounds, new Rect(10, 10, 20, 20)), true);

        foreach (bool closed in new[] { false, true })
            foreach (string gap in new[] { "first", "middle", "last", "all" })
            {
                var mixed = Lines(closed, gap);
                var expected = ExpectedLines(closed, gap);
                string name = $"mixed-{(closed ? "closed" : "open")}-{gap}";
                check(name + "-bounds", () => Equal(mixed.GetRenderBounds(pen), MixedBounds(expected.GetRenderBounds(pen), new Point(10, 10))), true);
                check(name + "-stroke-grid", () => SameStroke(mixed, expected, pen), true);
                var filled = mixed.Clone();
                filled.Figures[0].IsFilled = true;
                var solid = filled.Clone();
                foreach (PathSegment segment in solid.Figures[0].Segments) segment.IsStroked = true;
                check(name + "-fill-unchanged", () => SameFill(filled, solid), true);
                check(name + "-source-bounds", () => Equal(mixed.Bounds, solid.Bounds), true);
            }

        foreach (PenLineCap cap in new[] { PenLineCap.Flat, PenLineCap.Round, PenLineCap.Square })
        {
            var capped = new Pen(Brushes.Red, 4) { StartLineCap = cap, EndLineCap = cap, DashCap = cap };
            foreach (bool closed in new[] { false, true })
                check($"mixed-matching-caps-{cap}-{closed}", () => SameStroke(Lines(closed, "middle"), ExpectedLines(closed, "middle"), capped), true);
        }
        var outerRound = new Pen(Brushes.Red, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Flat };
#if PORTABLE_GEOMETRY_TESTS
        check("mixed-distinct-gap-cap-diagnostic", () => Unsupported(() => pairs.GetRenderBounds(outerRound), "gap"), true);
        check("mixed-distinct-gap-cap-rendering-diagnostic", () => Unsupported(() => Render(pairs, outerRound), "gap"), true);
        check("mixed-distinct-gap-cap-containment-diagnostic", () => Unsupported(() => pairs.StrokeContains(outerRound, new Point(20, 10)), "gap"), true);
#else
        check("mixed-gap-uses-dash-cap", () => pairs.StrokeContains(outerRound, new Point(31, 10), 0, ToleranceType.Absolute), false);
        check("mixed-outer-start-uses-start-cap", () => pairs.StrokeContains(outerRound, new Point(9, 10), 0, ToleranceType.Absolute), true);
        check("mixed-outer-end-uses-end-cap", () => pairs.StrokeContains(outerRound, new Point(31, 30), 0, ToleranceType.Absolute), true);
#endif

        foreach (var sample in SkippedSegments())
        {
            var mixed = new PathGeometry([new PathFigure(new Point(10, 10),
                [sample.Segment, new LineSegment(new Point(60, 30), true)], false) { IsFilled = false }]);
            var expected = new LineGeometry(sample.End, new Point(60, 30));
            check("mixed-skipped-" + sample.Name + "-endpoint", () => SameStroke(mixed, expected, pen), true);
            check("mixed-skipped-" + sample.Name + "-bounds", () => Equal(mixed.GetRenderBounds(pen), MixedBounds(expected.GetRenderBounds(pen), new Point(10, 10))), true);
            PathSegment drawn = sample.Segment.Clone();
            drawn.IsStroked = true;
            var following = new PathGeometry([new PathFigure(new Point(10, 10),
                [new LineSegment(new Point(20, 10), false), drawn], false) { IsFilled = false }]);
            var followingExpected = new PathGeometry([new PathFigure(new Point(20, 10), [drawn.Clone()], false) { IsFilled = false }]);
            check("mixed-drawn-" + sample.Name + "-after-gap", () => SameStroke(following, followingExpected, pen), true);
        }
        var emptyPoly = new PathGeometry([new PathFigure(new Point(10, 30),
            [new LineSegment(new Point(30, 10), true), new PolyLineSegment { IsStroked = false },
                new LineSegment(new Point(50, 30), true)], false) { IsFilled = false }]);
        var joined = new PathGeometry([new PathFigure(new Point(10, 30),
            [new LineSegment(new Point(30, 10), true), new LineSegment(new Point(50, 30), true)], false) { IsFilled = false }]);
        check("mixed-empty-poly-keeps-join", () => SameStroke(emptyPoly, joined, pen) && Equal(emptyPoly.GetRenderBounds(pen), joined.GetRenderBounds(pen)), true);
        var multipleGaps = new PathGeometry([new PathFigure(new Point(10, 10),
            [new LineSegment(new Point(30, 10), true), new LineSegment(new Point(40, 20), false),
             new LineSegment(new Point(40, 40), true), new LineSegment(new Point(20, 40), false),
             new LineSegment(new Point(10, 30), true)], true) { IsFilled = false }]);
        var multipleExpected = new PathGeometry([
            new PathFigure(new Point(20, 40), [new LineSegment(new Point(10, 30), true), new LineSegment(new Point(10, 10), true),
                new LineSegment(new Point(30, 10), true)], false) { IsFilled = false },
            new PathFigure(new Point(40, 20), [new LineSegment(new Point(40, 40), true)], false) { IsFilled = false }]);
        check("mixed-multiple-gaps-closed-runs", () => SameStroke(multipleGaps, multipleExpected, pen), true);
        check("mixed-multiple-gaps-closed-bounds", () => Equal(multipleGaps.GetRenderBounds(pen),
            MixedBounds(multipleExpected.GetRenderBounds(pen), new Point(10, 10))), true);
        var transformed = Lines(true, "middle");
        transformed.Transform = new MatrixTransform(2, 0.25, 0.5, 3, 5, 7);
        var transformedExpected = ExpectedLines(true, "middle");
        transformedExpected.Transform = transformed.Transform;
        check("mixed-geometry-transform-bounds", () => Equal(transformed.GetRenderBounds(pen), MixedBounds(transformedExpected.GetRenderBounds(pen), new Point(30, 39.5))), true);
        check("mixed-geometry-transform-stroke", () => SameStroke(transformed, transformedExpected, pen), true);
    }

    private static PathGeometry Lines(bool closed, string gap) => new([new PathFigure(new Point(10, 10),
        [new LineSegment(new Point(30, 10), gap != "first" && gap != "all"),
         new LineSegment(new Point(30, 30), gap != "middle" && gap != "all"),
         new LineSegment(new Point(10, 30), gap != "last" && gap != "all")], closed) { IsFilled = false }]);

    private static PathGeometry ExpectedLines(bool closed, string gap)
    {
        var figures = new List<PathFigure>();
        if (gap == "first") Add(new Point(30, 10), closed ? [new Point(30, 30), new Point(10, 30), new Point(10, 10)] : [new Point(30, 30), new Point(10, 30)]);
        if (gap == "middle")
        {
            if (closed) Add(new Point(30, 30), [new Point(10, 30), new Point(10, 10), new Point(30, 10)]);
            else { Add(new Point(10, 10), [new Point(30, 10)]); Add(new Point(30, 30), [new Point(10, 30)]); }
        }
        if (gap == "last") Add(closed ? new Point(10, 30) : new Point(10, 10), closed ? [new Point(10, 10), new Point(30, 10), new Point(30, 30)] : [new Point(30, 10), new Point(30, 30)]);
        if (gap == "all" && closed) Add(new Point(10, 30), [new Point(10, 10)]);
        // Keep the all-gap expected geometry nonempty so both implementations use their empty-stroke fallback.
        if (figures.Count == 0) figures.Add(new PathFigure(new Point(0, 0), [new LineSegment(new Point(0, 0), false)], false) { IsFilled = false });
        return new PathGeometry(figures);
        void Add(Point start, Point[] ends) => figures.Add(new PathFigure(start, ends.Select(p => new LineSegment(p, true)), false) { IsFilled = false });
    }

    private static IEnumerable<(string Name, PathSegment Segment, Point End)> SkippedSegments()
    {
        var end = new Point(40, 30);
        yield return ("line", new LineSegment(end, false), end);
        yield return ("cubic", new BezierSegment(new Point(80, 90), new Point(-20, 70), end, false), end);
        yield return ("quadratic", new QuadraticBezierSegment(new Point(80, 90), end, false), end);
        yield return ("polyline", new PolyLineSegment([new Point(80, 90), end], false), end);
        yield return ("polycubic", new PolyBezierSegment([new Point(80, 90), new Point(-20, 70), end], false), end);
        yield return ("polyquadratic", new PolyQuadraticBezierSegment([new Point(80, 90), end], false), end);
        yield return ("arc", new ArcSegment(end, new Size(40, 20), 25, true, SweepDirection.Clockwise, false), end);
    }

    private static bool SameStroke(Geometry actual, Geometry expected, Pen pen)
    {
        for (double y = 5.375; y < 115; y += 2)
            for (double x = 5.375; x < 155; x += 2)
                if (actual.StrokeContains(pen, new Point(x, y), 0, ToleranceType.Absolute) != expected.StrokeContains(pen, new Point(x, y), 0, ToleranceType.Absolute))
                    throw new InvalidOperationException($"Stroke differs at {x},{y}.");
        return true;
    }

    private static bool SameFill(Geometry actual, Geometry expected)
    {
        for (double y = 5.375; y < 35; y += 2)
            for (double x = 5.375; x < 35; x += 2)
                if (actual.FillContains(new Point(x, y), 0, ToleranceType.Absolute) != expected.FillContains(new Point(x, y), 0, ToleranceType.Absolute)) return false;
        return true;
    }

    private static Rect Content(Geometry geometry, Pen pen)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen()) drawing.DrawGeometry(null, pen, geometry);
        return VisualTreeHelper.GetContentBounds(visual);
    }

    private static bool LayoutAndRender(Geometry geometry)
    {
        if (!OperatingSystem.IsWindows()) return LayoutAndRenderCore(geometry);
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { LayoutAndRenderCore(geometry); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }

    private static bool LayoutAndRenderCore(Geometry geometry)
    {
        var path = new System.Windows.Shapes.Path { Data = geometry, Stroke = Brushes.Red, StrokeThickness = 4 };
        var canvas = new System.Windows.Controls.Canvas { Width = 64, Height = 48 };
        canvas.Children.Add(path);
        canvas.Measure(new Size(64, 48));
        canvas.Arrange(new Rect(0, 0, 64, 48));
        canvas.UpdateLayout();
        if (!canvas.IsMeasureValid || !canvas.IsArrangeValid || !path.IsMeasureValid || !path.IsArrangeValid)
            throw new InvalidOperationException("Mixed-segment Path/Canvas layout did not complete.");
        if (!PositiveExtent(path.DesiredSize) || !PositiveExtent(path.RenderSize))
            throw new InvalidOperationException($"Mixed-segment Path has no positive layout extent: desired {path.DesiredSize}, rendered {path.RenderSize}.");
        byte[] alpha = Render(canvas);
        if (alpha[10 * 64 + 20] != 255 || alpha[30 * 64 + 20] != 255 || alpha[20 * 64 + 20] != 0)
            throw new InvalidOperationException("Disconnected pairs did not retain stroke pixels and a transparent connector.");
        return true;
    }

    private static byte[] Render(Geometry geometry, Pen pen)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen()) drawing.DrawGeometry(null, pen, geometry);
        return Render(visual);
    }

    private static bool PositiveExtent(Size size) => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;

    private static byte[] Render(Visual visual)
    {
        var alpha = new byte[64 * 48];
#if PORTABLE_GEOMETRY_TESTS
        var type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(type, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [visual, 64, 48], null)!;
        try { type.GetMethod("Render")!.Invoke(target, null); }
        catch (System.Reflection.TargetInvocationException exception)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
        using var pixels = (SkiaSharp.SKBitmap)type.GetMethod("Snapshot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, null)!;
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++) alpha[y * 64 + x] = pixels.GetPixel(x, y).Alpha;
#else
        var pixels = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 48, 96, 96, PixelFormats.Pbgra32);
        pixels.Render(visual);
        var bytes = new byte[64 * 48 * 4];
        pixels.CopyPixels(bytes, 64 * 4, 0);
        for (int i = 0; i < alpha.Length; i++) alpha[i] = bytes[i * 4 + 3];
#endif
        return alpha;
    }
    private static Rect MixedBounds(Rect stroke, Point start)
    {
#if !PORTABLE_GEOMETRY_TESTS
        // Stock widening conservatively retains the origin and source start for figures with gaps.
        stroke.Union(new Rect(0, 0, 0, 0));
        stroke.Union(start);
#endif
        return stroke;
    }

    private static bool Equal(Rect actual, Rect expected)
    {
        if (actual.IsEmpty != expected.IsEmpty || !actual.IsEmpty && (Math.Abs(actual.X - expected.X) > 0.002 || Math.Abs(actual.Y - expected.Y) > 0.002 ||
            Math.Abs(actual.Width - expected.Width) > 0.002 || Math.Abs(actual.Height - expected.Height) > 0.002))
            throw new InvalidOperationException($"Bounds were {actual}, expected {expected}.");
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
