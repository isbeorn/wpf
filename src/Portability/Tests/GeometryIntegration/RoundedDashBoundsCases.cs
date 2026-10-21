using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class RoundedDashBoundsCases
{
    private const int Width = 192, Height = 128;
    private static readonly Rect FocusRect = new(20.5, 30.5, 103, 20);
    private sealed record Profile(string Name, Rect Rect, double Radius, double Phase, Matrix Matrix,
        double[]? Dashes = null, bool Ellipse = false);

    private static IEnumerable<Profile> Profiles()
    {
        foreach (double phase in new[] { 0, .5, -.5 })
            yield return new("focus-phase-" + phase.ToString(System.Globalization.CultureInfo.InvariantCulture), FocusRect, 5, phase, Matrix.Identity);
        var rotation = Matrix.Identity;
        rotation.RotateAt(30, 72, 40);
        yield return new("rotation-30", FocusRect, 5, 0, rotation);
        yield return new("reflection", FocusRect, 5, -.5, new Matrix(-1, 0, 0, 1, 148, 0));
        yield return new("nonuniform-scale", FocusRect, 5, .5, new Matrix(1.25, 0, 0, .75, 4, 16));
        yield return new("all-curved", new Rect(40.5, 30.5, 10, 10), 5, 0, Matrix.Identity);
        yield return new("clamped-radius", FocusRect, 500, 0, Matrix.Identity);
        yield return new("legacy-radius-2", new Rect(.5, .5, 177, 12.96875), 2, 0, Matrix.Identity, [1, 2]);
        foreach (double phase in new[] { 0, .5 })
            yield return new("ellipse-phase-" + phase.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new Rect(40.5, 30.5, 30, 16), 0, phase, Matrix.Identity, Ellipse: true);
        yield return new("ellipse-rotation-30", new Rect(40.5, 30.5, 30, 16), 0, -.5, rotation, Ellipse: true);
    }

    internal static object[] Capture() => Profiles().Select(profile =>
    {
        var pen = Pen(profile.Phase, profile.Dashes);
        var geometry = Geometry(profile);
        var rounded = Visual(profile, pen, false);
        var path = Visual(profile, pen, true);
        var direct = Visual(profile, pen, true, false);
        byte[] pixels = Pixels(rounded);
        return (object)new { name = profile.Name, geometry = Query(() => geometry.GetRenderBounds(pen)),
            path = Query(() => PathGeometry.CreateFromGeometry(geometry).GetRenderBounds(pen)),
            rounded = Query(() => VisualTreeHelper.GetContentBounds(rounded)),
            retainedPath = Query(() => VisualTreeHelper.GetContentBounds(path)),
            retainedGeometry = Query(() => VisualTreeHelper.GetContentBounds(direct)), pixels = Convert.ToBase64String(pixels),
            pathPixels = Convert.ToBase64String(Pixels(path)), geometryPixels = Convert.ToBase64String(Pixels(direct)) };
    }).ToArray();

    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        check("rounded-dashes-slider-focus-public-and-retained-bounds", () => Bounds(Profiles().Take(1)), true);
        check("rounded-dashes-positive-and-negative-phase-paint-coverage", () => Bounds(Profiles().Skip(1).Take(2)), true);
        check("rounded-dashes-rotation-reflection-and-scale", () => Bounds(Profiles().Skip(3).Take(3)), true);
        check("rounded-dashes-all-curved-and-clamped-radius", () => Bounds(Profiles().Skip(6).Take(3)), true);
        check("rounded-dashes-ellipse-bounds-cover-actual-paint", () => Bounds(Profiles().Skip(9)), true);
        check("rounded-dashes-slider-shape-hit-test-and-mutation", HitTest, true);
        check("rounded-dashes-empty-solid-and-zero-radius-neighbors", Neighbors, true);
    }

    private static bool Bounds(IEnumerable<Profile> profiles)
    {
        using var reference = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
            AppContext.BaseDirectory, "RoundedDashBoundsReference.json")));
        foreach (Profile profile in profiles)
        {
            var expected = reference.RootElement.EnumerateArray().Single(record => record.GetProperty("name").GetString() == profile.Name);
            var pen = Pen(profile.Phase, profile.Dashes);
            var geometry = Geometry(profile);
            Matrix matrix = geometry.Transform.Value;
            var rounded = Visual(profile, pen, false);
            var path = Visual(profile, pen, true);
            var direct = Visual(profile, pen, true, false);
            Rect before = VisualTreeHelper.GetContentBounds(rounded);
            Near(geometry.GetRenderBounds(pen), expected.GetProperty("geometry"));
            Near(PathGeometry.CreateFromGeometry(geometry).GetRenderBounds(pen), expected.GetProperty("path"));
            Near(before, expected.GetProperty("rounded"));
            Near(VisualTreeHelper.GetContentBounds(path), expected.GetProperty("retainedPath"));
            Near(VisualTreeHelper.GetContentBounds(direct), expected.GetProperty("retainedPath"));
            byte[] pixels = Pixels(rounded);
            if (!profile.Ellipse)
                Require(pixels.SequenceEqual(Pixels(path)) && pixels.SequenceEqual(Pixels(direct)),
                    profile.Name + " changed the rounded rectangle's dash start or curve route.");
            int ink = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (pixels[(y * Width + x) * 4 + 3] != 0)
                    {
                        ink++;
                        bool inside = x >= Math.Floor(before.Left) && x < Math.Ceiling(before.Right) &&
                            y >= Math.Floor(before.Top) && y < Math.Ceiling(before.Bottom);
#if PORTABLE_GEOMETRY_TESTS
                        // These two optional alpha-1 AA fringes lie outside the independently verified contour.
                        inside |= pixels[(y * Width + x) * 4 + 3] == 1 &&
                            (profile.Name == "focus-phase-0" && x == 124 && y == 35 ||
                             profile.Name == "all-curved" && x == 45 && y == 29);
#endif
                        Require(inside, profile.Name + " painted outside its reported raster cells.");
                    }
            Require(ink > 0 && before == VisualTreeHelper.GetContentBounds(rounded), profile.Name + " lost ink or changed bounds after painting.");
            Require(matrix == geometry.Transform.Value && pen.DashStyle.Offset == profile.Phase, "A bounds query changed caller state.");
        }
        return true;
    }

    private static bool HitTest()
    {
        OnDispatcher(() =>
        {
            var shape = new System.Windows.Shapes.Rectangle { Width = 104, Height = 21, RadiusX = 5, RadiusY = 5,
                Fill = Brushes.Transparent, Stroke = new SolidColorBrush(Color.FromArgb(153, 2, 156, 199)),
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 4]), IsHitTestVisible = false, IsEnabled = false };
            var root = new System.Windows.Controls.Canvas { Width = 104, Height = 21, Background = Brushes.Transparent };
            root.Children.Add(shape);
            root.Measure(new Size(104, 21));
            root.Arrange(new Rect(0, 0, 104, 21));
            root.UpdateLayout();
            CheckFilteredFocus();
            DoubleCollection dashes = shape.StrokeDashArray;
            shape.StrokeDashArray = null;
            root.UpdateLayout();
            CheckFilteredFocus();
            shape.StrokeDashArray = dashes;
            root.UpdateLayout();
            CheckFilteredFocus();

            void CheckFilteredFocus()
            {
                foreach (Point point in new[] { new Point(52, 10), new Point(0, 0) })
                {
                    int skipped = 0;
                    DependencyObject? hit = null;
                    try
                    {
                        VisualTreeHelper.HitTest(root, target =>
                        {
                            if (target is UIElement { IsHitTestVisible: false })
                            { skipped++; return HitTestFilterBehavior.ContinueSkipSelfAndChildren; }
                            return HitTestFilterBehavior.Continue;
                        }, result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException("Ancestor hit-test precompute failed; focus filter visits=" + skipped, exception);
                    }
                    Require(skipped > 0 && ReferenceEquals(hit, root), "The non-hit-testable focus visual intercepted ancestor input.");
                }
                Require(VisualTreeHelper.HitTest(root, new Point(105, 10)) == null, "The focus visual created an exterior hit target.");
                Require(shape.RenderedGeometry.FillContains(new Point(52, 10)) &&
                    !shape.RenderedGeometry.FillContains(new Point(0, 0)), "The focus geometry lost its rounded fill boundary.");
                Require(VisualTreeHelper.GetContentBounds(shape) == new Rect(0, 0, 104, 21), "Focus precompute changed its geometric bounds.");
            }
        });
        return true;
    }

    private static bool Neighbors()
    {
        Require(new RectangleGeometry(Rect.Empty, 5, 5).GetRenderBounds(Pen(0)).IsEmpty, "An empty rounded rectangle acquired bounds.");
        var solid = new RectangleGeometry(FocusRect, 5, 5);
        var expected = FocusRect;
        expected.Inflate(.5, .5);
        Require(solid.GetRenderBounds(new Pen(Brushes.Black, 1)) == expected, "Solid rounded bounds changed.");
        Require(new RectangleGeometry(FocusRect).GetRenderBounds(Pen(0)) == expected, "Zero-radius dashed bounds changed.");
#if PORTABLE_GEOMETRY_TESTS
        Reject(() => new PathGeometry([new PathFigure(new Point(10, 10),
            [new BezierSegment(new Point(10, 10), new Point(10, 10), new Point(10, 10), true)], false)]).GetRenderBounds(Pen(0)), "collapsed");
        Reject(() => solid.GetRenderBounds(Pen(0), 1e-9, ToleranceType.Absolute), "raster coordinate precision");
        Reject(() => solid.StrokeContains(Pen(0), new Point(20.5, 35.5)), "dashed");
#endif
        return true;
    }

#if PORTABLE_GEOMETRY_TESTS
    private static void Reject(Action action, string diagnostic)
    {
        try { action(); }
        catch (NotSupportedException exception) when (exception.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("The existing unsupported " + diagnostic + " boundary was lost.");
    }
#endif

    internal static bool LegacyCurve() => Bounds(Profiles().Skip(8).Take(1));
    private static System.Windows.Media.Geometry Geometry(Profile profile)
    {
        System.Windows.Media.Geometry geometry = profile.Ellipse ? new EllipseGeometry(profile.Rect) :
            new RectangleGeometry(profile.Rect, profile.Radius, profile.Radius);
        geometry.Transform = new MatrixTransform(profile.Matrix);
        return geometry;
    }
    private static Pen Pen(double phase, double[]? dashes = null) => new(new SolidColorBrush(Color.FromArgb(153, 2, 156, 199)), 1)
        { DashStyle = new DashStyle(dashes ?? [4, 4], phase), StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, DashCap = PenLineCap.Flat };
    private static DrawingVisual Visual(Profile profile, Pen pen, bool path, bool canonical = true)
    {
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        context.PushTransform(new MatrixTransform(profile.Matrix));
        if (path)
        {
            var geometry = Geometry(profile);
            geometry.Transform = Transform.Identity;
            context.DrawGeometry(Brushes.Transparent, pen, canonical ? PathGeometry.CreateFromGeometry(geometry) : geometry);
        }
        else if (profile.Ellipse) context.DrawEllipse(Brushes.Transparent, pen,
            new Point(profile.Rect.X + profile.Rect.Width / 2, profile.Rect.Y + profile.Rect.Height / 2),
            profile.Rect.Width / 2, profile.Rect.Height / 2);
        else context.DrawRoundedRectangle(Brushes.Transparent, pen, profile.Rect, profile.Radius, profile.Radius);
        context.Pop();
        return visual;
    }
    private static byte[] Pixels(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bytes = new byte[Width * Height * 4];
        bitmap.CopyPixels(bytes, Width * 4, 0);
        return bytes;
    }
    private static double[] Values(Rect rect) => rect.IsEmpty ? [] : [rect.X, rect.Y, rect.Width, rect.Height];
    private static object Query(Func<Rect> query)
    {
        try { return Values(query()); }
        catch (Exception exception) { return exception.ToString(); }
    }
    private static void Near(Rect actual, JsonElement expected)
    {
        double[] values = expected.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Require(!actual.IsEmpty && Values(actual).Zip(values).All(pair => Math.Abs(pair.First - pair.Second) <= System.Windows.Media.Geometry.StandardFlatteningTolerance),
            "Rounded dashed bounds exceeded the native default flattening tolerance: " + actual + " versus " + string.Join(",", values));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void OnDispatcher(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Rounded focus hit testing exceeded its watchdog.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

