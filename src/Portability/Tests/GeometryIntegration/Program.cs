using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

if (args.Length == 1 && args[0] == "--capture-rounded-dashed-bounds")
{
    Console.WriteLine(JsonSerializer.Serialize(RoundedDashBoundsCases.Capture(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] is "--capture-hollow-dashed-ink" or "--capture-hollow-dashed-filled-ink")
{
    Console.WriteLine(JsonSerializer.Serialize(DashedBoundsCases.CaptureHollowInk(args[0] == "--capture-hollow-dashed-filled-ink"), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-hollow-dashed-bounds")
{
    Console.WriteLine(JsonSerializer.Serialize(DashedBoundsCases.CaptureHollow(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-grid-affine")
{
    Console.WriteLine(JsonSerializer.Serialize(GridBoundsCases.CaptureAffine(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
#if !PORTABLE_GEOMETRY_TESTS
if (args.Length == 1 && args[0] == "--capture-combine-sampling")
{
    Console.WriteLine(JsonSerializer.Serialize(CombineCases.CaptureSamplingMasks(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

#endif

if (args.Length == 1 && args[0] == "--capture-combine-contracts")
{
    Console.WriteLine(JsonSerializer.Serialize(CombineCases.CaptureContracts(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-dashed-rectangle-routes")
{
    Console.WriteLine(JsonSerializer.Serialize(DashedBoundsCases.CaptureRectangleRoutes(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-dashed-bounds-ink")
{
    Console.WriteLine(JsonSerializer.Serialize(DashedBoundsCases.CaptureInk(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-dashed-bounds")
{
    Console.WriteLine(JsonSerializer.Serialize(DashedBoundsCases.Capture(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 1 && args[0] == "--capture-bounds-precision")
{
    Console.WriteLine(JsonSerializer.Serialize(BoundsCases.CapturePrecision(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var checks = new List<object>();
var failed = false;
if (args.Contains("--rounded-dashed-bounds-only"))
{
    RoundedDashBoundsCases.Run(Check);
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "rounded-dashed-bounds", corePath = typeof(Geometry).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
if (!args.Contains("--hollow-dashed-bounds-only"))
{
    foreach (Stretch stretch in Enum.GetValues<Stretch>())
        Check($"empty-path-{stretch}-null-and-stream-layout", () => { EmptyGeometryCases.Layout(stretch); return true; }, true);
    Check("empty-transformed-geometries-drawing-and-clip", () => { EmptyGeometryCases.DrawingAndClip(); return true; }, true);
    Check("empty-path-guard-preserves-collapsed-and-move-only-validation", () => { EmptyGeometryCases.NonemptyCommands(); return true; }, true);
}
if (args.Contains("--empty-geometry-only"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "empty-geometry-rendering", corePath = typeof(Geometry).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
if (args.Contains("--hollow-dashed-bounds-only"))
{
    DashedBoundsCases.RunHollow(Check);
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "hollow-dashed-bounds", corePath = typeof(Geometry).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
var rectangle = new RectangleGeometry(new Rect(10, 10, 20, 20));
Check("rectangle-interior", () => rectangle.FillContains(new Point(20, 20)), true);
Check("rectangle-exterior", () => rectangle.FillContains(new Point(5, 20)), false);
Check("rectangle-left-boundary", () => rectangle.FillContains(new Point(10, 20), 0, ToleranceType.Absolute), true);
Check("rectangle-right-boundary", () => rectangle.FillContains(new Point(30, 20), 0, ToleranceType.Absolute), true);
Check("absolute-tolerance-near", () => rectangle.FillContains(new Point(9.8, 20), 0.25, ToleranceType.Absolute), true);
Check("absolute-tolerance-outside", () => rectangle.FillContains(new Point(9.7, 20), 0.25, ToleranceType.Absolute), false);
Check("absolute-tolerance-expanded", () => rectangle.FillContains(new Point(9.6, 20), 0.5, ToleranceType.Absolute), true);
Check("relative-tolerance-near", () => rectangle.FillContains(new Point(9.6, 20), 0.025, ToleranceType.Relative), true);
Check("relative-tolerance-outside", () => rectangle.FillContains(new Point(9.4, 20), 0.025, ToleranceType.Relative), false);
var tallRectangle = new RectangleGeometry(new Rect(10, 10, 20, 40));
Check("relative-tolerance-uses-largest-extent", () => tallRectangle.FillContains(new Point(9.7, 30), 0.01, ToleranceType.Relative), true);
Check("relative-tolerance-largest-extent-outside", () => tallRectangle.FillContains(new Point(9.5, 30), 0.01, ToleranceType.Relative), false);
Check("zero-tolerance-exterior", () => rectangle.FillContains(new Point(9.9, 20), 0, ToleranceType.Absolute), false);
Check("negative-tolerance-clamped", () => rectangle.FillContains(new Point(9.9, 20), -1, ToleranceType.Absolute), false);
Check("nan-point", () => rectangle.FillContains(new Point(double.NaN, 20)), false);
Check("infinite-point", () => rectangle.FillContains(new Point(double.PositiveInfinity, 20)), false);
Check("empty-rectangle", () => new RectangleGeometry(Rect.Empty).FillContains(new Point(0, 0)), false);
Check("zero-width-fill-on-edge", () => new RectangleGeometry(new Rect(10, 10, 0, 20)).FillContains(new Point(10, 20)), true);
Check("zero-width-fill-outside", () => new RectangleGeometry(new Rect(10, 10, 0, 20)).FillContains(new Point(12, 20)), false);
Check("rounded-rectangle-cut-corner", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 8, 8).FillContains(new Point(0.5, 0.5)), false);
Check("rounded-rectangle-interior", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 8, 8).FillContains(new Point(10, 10)), true);
Check("rounded-rectangle-large-x-small-y-outside", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 100, 2).FillContains(new Point(1, 0.5), 0, ToleranceType.Absolute), false);
Check("rounded-rectangle-small-x-large-y-outside", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 2, 100).FillContains(new Point(0.5, 1), 0, ToleranceType.Absolute), false);
Check("rounded-rectangle-large-x-small-y-inside", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 100, 2).FillContains(new Point(1, 1.5), 0, ToleranceType.Absolute), true);
Check("rounded-rectangle-small-x-large-y-inside", () => new RectangleGeometry(new Rect(0, 0, 20, 20), 2, 100).FillContains(new Point(1.5, 1), 0, ToleranceType.Absolute), true);
Check("ellipse-exterior-corner", () => new EllipseGeometry(new Point(10, 10), 10, 5).FillContains(new Point(19, 14)), false);
Check("ellipse-interior", () => new EllipseGeometry(new Point(10, 10), 10, 5).FillContains(new Point(10, 10)), true);
Check("ellipse-zero-axis-fill", () => new EllipseGeometry(new Point(10, 10), 0, 10).FillContains(new Point(10, 15)), true);
Check("ellipse-zero-point-fill", () => new EllipseGeometry(new Point(10, 10), 0, 0).FillContains(new Point(10, 10)), true);
var transformed = new RectangleGeometry(new Rect(0, 0, 10, 10)) { Transform = new MatrixTransform(2, 0, 0, 3, 40, 20) };
Check("transformed-fill-inside", () => transformed.FillContains(new Point(55, 35)), true);
Check("transformed-fill-original-outside", () => transformed.FillContains(new Point(5, 5)), false);
Check("transformed-absolute-tolerance", () => transformed.FillContains(new Point(39.6, 35), 0.5, ToleranceType.Absolute), true);
var triangle = new StreamGeometry();
using (var context = triangle.Open())
{
    context.BeginFigure(new Point(0, 0), true, false);
    context.LineTo(new Point(20, 0), true, false);
    context.LineTo(new Point(0, 20), true, false);
}
Check("open-filled-triangle-inside", () => triangle.FillContains(new Point(4, 4)), true);
Check("open-filled-triangle-exterior", () => triangle.FillContains(new Point(16, 16)), false);
Check("implicit-close-tolerance", () => triangle.FillContains(new Point(-0.2, 10), 0.25, ToleranceType.Absolute), true);
var hollow = new PathGeometry([new PathFigure(new Point(0, 0), [new LineSegment(new Point(20, 0), true), new LineSegment(new Point(0, 20), true)], true) { IsFilled = false }]);
Check("unfilled-figure-does-not-fill", () => hollow.FillContains(new Point(4, 4)), false);
var holes = new GeometryGroup { FillRule = FillRule.EvenOdd };
holes.Children.Add(new RectangleGeometry(new Rect(0, 0, 30, 30)));
holes.Children.Add(new RectangleGeometry(new Rect(10, 10, 10, 10)));
Check("even-odd-hole", () => holes.FillContains(new Point(15, 15)), false);
Check("even-odd-shell", () => holes.FillContains(new Point(5, 5)), true);
holes.FillRule = FillRule.Nonzero;
Check("nonzero-overlap-filled", () => holes.FillContains(new Point(15, 15)), true);
var pen = new Pen(Brushes.Red, 4);
var line = new LineGeometry(new Point(0, 0), new Point(20, 0));
Check("line-stroke-inside", () => line.StrokeContains(pen, new Point(10, 1.5)), true);
Check("line-stroke-outside", () => line.StrokeContains(pen, new Point(10, 2.5)), false);
Check("stroke-boundary-near-tolerance", () => line.StrokeContains(pen, new Point(10, 2.2), 0.25, ToleranceType.Absolute), true);
Check("flat-cap-outside", () => line.StrokeContains(pen, new Point(-1, 0), 0, ToleranceType.Absolute), false);
pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
Check("round-cap-inside", () => line.StrokeContains(pen, new Point(-1, 0), 0, ToleranceType.Absolute), true);
pen.StartLineCap = pen.EndLineCap = PenLineCap.Square;
Check("square-cap-inside", () => line.StrokeContains(pen, new Point(-1.5, 1.5), 0, ToleranceType.Absolute), true);
Check("zero-width-stroke", () => line.StrokeContains(new Pen(Brushes.Red, 0), new Point(10, 0)), false);
Check("null-pen-stroke", () => line.StrokeContains(null!, new Point(10, 0)), false);
Check("null-brush-stroke-still-hits", () => line.StrokeContains(new Pen(null, 4), new Point(10, 1)), true);
line.Transform = new ScaleTransform(2, 3);
Check("geometry-transform-does-not-scale-pen", () => line.StrokeContains(new Pen(Brushes.Red, 4), new Point(20, 2.5), 0, ToleranceType.Absolute), false);
Check("transformed-line-stroke-inside", () => line.StrokeContains(new Pen(Brushes.Red, 4), new Point(30, 1), 0, ToleranceType.Absolute), true);
Check("nan-tolerance-interior", () => rectangle.FillContains(new Point(20, 20), double.NaN, ToleranceType.Absolute), true);
Check("infinite-tolerance-exterior", () => rectangle.FillContains(new Point(-100, -100), double.PositiveInfinity, ToleranceType.Absolute), true);
Check("line-zero-fill-area", () => new LineGeometry(new Point(0, 0), new Point(20, 0)).FillContains(new Point(10, 0)), true);
Check("nan-tolerance-near-exterior", () => rectangle.FillContains(new Point(9.8, 20), double.NaN, ToleranceType.Absolute), false);
Check("singular-transform-fill", () => new RectangleGeometry(new Rect(0, 0, 10, 10)) { Transform = new ScaleTransform(0, 1) }.FillContains(new Point(0, 5)), true);
Check("nan-shape", () => new RectangleGeometry(new Rect(double.NaN, 0, 10, 10)).FillContains(new Point(0, 5)), false);
Check("negative-stroke-width", () => line.StrokeContains(new Pen(Brushes.Red, -4), new Point(20, 1)), true);
Check("zero-point-fill", () => new RectangleGeometry(new Rect(10, 10, 0, 0)).FillContains(new Point(10, 10)), true);
Check("zero-point-nan-tolerance", () => new RectangleGeometry(new Rect(10, 10, 0, 0)).FillContains(new Point(10, 10), double.NaN, ToleranceType.Absolute), true);
Check("zero-point-relative-infinite-tolerance", () => new RectangleGeometry(new Rect(10, 10, 0, 0)).FillContains(new Point(10, 10), double.PositiveInfinity, ToleranceType.Relative), false);
Check("zero-width-rectangle-stroke", () => new RectangleGeometry(new Rect(10, 10, 0, 20)).StrokeContains(new Pen(Brushes.Red, 4), new Point(11, 20)), true);
var combined = new CombinedGeometry(GeometryCombineMode.Exclude,
    new RectangleGeometry(new Rect(0, 0, 30, 30)), new RectangleGeometry(new Rect(10, 10, 10, 10)));
Check("combined-fill-excluded-hole", () => combined.FillContains(new Point(15, 15)), false);
Check("combined-fill-kept-shell", () => combined.FillContains(new Point(5, 5)), true);
Check("combined-stroke-outer-edge", () => combined.StrokeContains(new Pen(Brushes.Red, 4), new Point(0, 5)), true);
Check("combined-stroke-hole-edge", () => combined.StrokeContains(new Pen(Brushes.Red, 4), new Point(10, 15)), true);
Check("combined-stroke-hole-interior", () => combined.StrokeContains(new Pen(Brushes.Red, 4), new Point(15, 15)), false);
Check("combined-stroke-filled-interior", () => combined.StrokeContains(new Pen(Brushes.Red, 4), new Point(5, 5)), false);
var cubic = new PathGeometry([new PathFigure(new Point(0, 0),
    [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)]);
Check("cubic-fill-inside", () => cubic.FillContains(new Point(10, 5)), true);
Check("cubic-fill-outside", () => cubic.FillContains(new Point(10, 18)), false);
var tallCurve = new PathGeometry([new PathFigure(new Point(0, 0),
    [new BezierSegment(new Point(0, 100), new Point(10, 100), new Point(10, 0), true)], true)]);
Check("relative-curve-tight-extent-near", () => tallCurve.FillContains(new Point(5, -0.7), 0.01, ToleranceType.Relative), true);
Check("relative-curve-tight-extent-outside", () => tallCurve.FillContains(new Point(5, -0.9), 0.01, ToleranceType.Relative), false);
var distantHollow = new PathGeometry([
    new PathFigure(new Point(0, 0), [new LineSegment(new Point(10, 0), true), new LineSegment(new Point(10, 10), true), new LineSegment(new Point(0, 10), true)], true),
    new PathFigure(new Point(1000, 0), [new LineSegment(new Point(1010, 10), false)], false) { IsFilled = false }
]);
Check("relative-extent-includes-unfilled-figure", () => distantHollow.FillContains(new Point(-0.8, 5), 0.001, ToleranceType.Relative), true);
Check("unfilled-figure-is-not-a-hit", () => distantHollow.FillContains(new Point(1005, 5), 0.001, ToleranceType.Relative), false);
#if PORTABLE_GEOMETRY_TESTS
Reject("unsupported-dashed-stroke", () => line.StrokeContains(new Pen(Brushes.Red, 4) { DashStyle = DashStyles.Dash }, new Point(10, 0)), "dashed");
Reject("unsupported-mixed-end-caps", () => line.StrokeContains(new Pen(Brushes.Red, 4) { EndLineCap = PenLineCap.Round }, new Point(10, 0)), "end caps");
#endif

CombineCases.Run(Check);
OutlineCases.Run(Check);
BoundsCases.Run(Check);
CrosshairBoundsCases.Run(Check);
DashedBoundsCases.Run(Check);
RoundedDashBoundsCases.Run(Check);
GridBoundsCases.Run(Check);
MixedStrokeCases.Run(Check);
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "actual-geometry-containment",
    os = RuntimeInformation.OSDescription,
    core = typeof(Geometry).Assembly.FullName,
    corePath = typeof(Geometry).Assembly.Location,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void Check(string name, Func<bool> action, bool expected)
{
    try
    {
        bool actual = action();
        failed |= actual != expected;
        checks.Add(new { name, status = actual == expected ? "pass" : "fail", actual, expected });
    }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}
#if PORTABLE_GEOMETRY_TESTS
void Reject(string name, Func<bool> action, string diagnostic)
{
    try { action(); failed = true; checks.Add(new { name, status = "fail", detail = "Unsupported operation did not throw." }); }
    catch (NotSupportedException exception)
    {
        bool named = exception.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase);
        failed |= !named;
        checks.Add(new { name, status = named ? "pass" : "fail", detail = exception.Message });
    }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}
#endif
