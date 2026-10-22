using System.IO;
using System.Windows;
using System.Windows.Media;

internal static class OutlineCases
{
    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        var overlap = new GeometryGroup { FillRule = FillRule.Nonzero };
        overlap.Children.Add(new RectangleGeometry(new Rect(0, 0, 20, 20)));
        overlap.Children.Add(new RectangleGeometry(new Rect(10, 0, 20, 20)));
        check("outlined-overlap-merges-contours", () => overlap.GetOutlinedPathGeometry().Figures.Count == 1, true);
        check("outlined-overlap-filled", () => overlap.GetOutlinedPathGeometry().FillContains(new Point(15, 10), 0, ToleranceType.Absolute), true);
        check("outlined-overlap-exterior", () => overlap.GetOutlinedPathGeometry().FillContains(new Point(35, 10), 0, ToleranceType.Absolute), false);
        overlap.FillRule = FillRule.EvenOdd;
        check("outlined-evenodd-overlap-hole", () => overlap.GetOutlinedPathGeometry().FillContains(new Point(15, 10), 0, ToleranceType.Absolute), false);
        check("outlined-evenodd-shell", () => overlap.GetOutlinedPathGeometry().FillContains(new Point(5, 10), 0, ToleranceType.Absolute), true);
        check("outlined-fill-rule-preserved", () => overlap.GetOutlinedPathGeometry().FillRule == FillRule.EvenOdd, true);
        check("outlined-empty", () => new PathGeometry().GetOutlinedPathGeometry().Figures.Count == 0, true);
        check("outlined-degenerate", () => new LineGeometry(new Point(0, 0), new Point(20, 0)).GetOutlinedPathGeometry().Figures.Count == 0, true);
        check("outlined-diagonal-degenerate", () => new LineGeometry(new Point(0, 0), new Point(20, 20)).GetOutlinedPathGeometry().Figures.Count == 0, true);
        var crossing = new PathGeometry([new PathFigure(new Point(0, 0), [new LineSegment(new Point(20, 20), true),
            new LineSegment(new Point(0, 20), true), new LineSegment(new Point(20, 0), true)], true)]);
        check("outlined-self-crossing-lobes", () => crossing.GetOutlinedPathGeometry().FillContains(new Point(5, 3), 0, ToleranceType.Absolute) &&
            crossing.GetOutlinedPathGeometry().FillContains(new Point(5, 17), 0, ToleranceType.Absolute), true);
        check("outlined-self-crossing-exterior", () => crossing.GetOutlinedPathGeometry().FillContains(new Point(2, 10), 0, ToleranceType.Absolute), false);

        var curve = new PathGeometry([new PathFigure(new Point(0, 0),
            [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)])
            { Transform = new MatrixTransform(2, 0, 0, 3, 40, 20) };
        check("outlined-transformed-cubic-fill", () => curve.GetOutlinedPathGeometry(0.01, ToleranceType.Relative).FillContains(new Point(60, 35), 0, ToleranceType.Absolute), true);
        check("outlined-transformed-cubic-bounds", () => Close(curve.GetOutlinedPathGeometry().Bounds, new Rect(40, 20, 40, 45), 0.02), true);
        check("actual-path-tight-bounds", () => Close(curve.Bounds, new Rect(40, 20, 40, 45), 0.001), true);
        var stream = new StreamGeometry { Transform = new TranslateTransform(4, 8) };
        using (var context = stream.Open())
        {
            context.BeginFigure(new Point(0, 0), true, false);
            context.LineTo(new Point(20, 0), true, false);
            context.LineTo(new Point(0, 10), true, false);
        }
        check("actual-stream-bounds", () => Close(stream.Bounds, new Rect(4, 8, 20, 10), 0.001), true);
        check("actual-group-bounds", () => Close(overlap.Bounds, new Rect(0, 0, 30, 20), 0.001), true);
        var hollow = new PathGeometry([new PathFigure(new Point(10, 10), [new LineSegment(new Point(20, 10), true)], false) { IsFilled = false }]);
        check("hollow-bounds-retain-zero-area", () => !hollow.Bounds.IsEmpty && Close(hollow.Bounds, new Rect(10, 10, 10, 0), 0.001), true);
        check("hollow-outline-is-empty", () => hollow.GetOutlinedPathGeometry().Figures.Count == 0, true);
        check("empty-path-bounds", () => new PathGeometry().Bounds.IsEmpty, true);
        check("empty-stream-bounds", () => new StreamGeometry().Bounds.IsEmpty, true);
        check("point-only-path-bounds", () => Close(new PathGeometry([new PathFigure { StartPoint = new Point(10, 10) }]).Bounds, new Rect(10, 10, 0, 0), 0.001), true);

        var ellipse = new EllipseGeometry(new Point(20, 20), 16, 8);
        foreach (ToleranceType type in new[] { ToleranceType.Absolute, ToleranceType.Relative })
        {
            double tolerance = type == ToleranceType.Absolute ? 0.01 : 0.0003125;
            check($"outlined-conic-inside-{type}", () => ellipse.GetOutlinedPathGeometry(tolerance, type)
                .FillContains(new Point(20 + 15.9 / Math.Sqrt(2), 20 + 7.95 / Math.Sqrt(2)), 0, ToleranceType.Absolute), true);
            check($"outlined-conic-outside-{type}", () => ellipse.GetOutlinedPathGeometry(tolerance, type)
                .FillContains(new Point(20 + 16.1 / Math.Sqrt(2), 20 + 8.05 / Math.Sqrt(2)), 0, ToleranceType.Absolute), false);
        }
        string font = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        var face = new GlyphTypeface(new Uri(font));
        ushort glyph = face.CharacterToGlyphMap['O'];
        var run = new GlyphRun(face, 0, false, 40, 1, [glyph, glyph], new Point(10, 50), [0, 0], null, null, null, null, null, null);
        check("actual-coincident-glyph-counter", () =>
        {
            Geometry geometry = run.BuildGeometry();
            Rect bounds = geometry.Bounds;
            return !geometry.FillContains(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), 0, ToleranceType.Absolute);
        }, true);
        check("actual-coincident-glyph-ink", () =>
        {
            Geometry geometry = run.BuildGeometry();
            Rect bounds = geometry.Bounds;
            return geometry.FillContains(new Point(bounds.Left + 1, bounds.Y + bounds.Height / 2), 0, ToleranceType.Absolute);
        }, true);
#if PORTABLE_GEOMETRY_TESTS
        check("outlined-conic-unrepresentable-tolerance", () =>
        {
            try { ellipse.GetOutlinedPathGeometry(1e-20, ToleranceType.Absolute); return false; }
            catch (NotSupportedException exception) { return exception.Message.Contains("tolerance", StringComparison.OrdinalIgnoreCase); }
        }, true);
#endif
    }

    private static bool Close(Rect actual, Rect expected, double tolerance) => !actual.IsEmpty &&
        Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance && Math.Abs(actual.Height - expected.Height) <= tolerance;
}
