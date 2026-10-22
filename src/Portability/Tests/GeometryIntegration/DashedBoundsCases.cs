using System.IO;
using System.Reflection;
using System.Security.Cryptography;
#if PORTABLE_GEOMETRY_TESTS
using SkiaSharp;
#endif
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class DashedBoundsCases
{
    internal static Dictionary<string, double[]> Capture() => Samples().ToDictionary(s => s.Name, s => Values(s.Query()));
    internal static Dictionary<string, double[]> CaptureHollow() => HollowSamples().ToDictionary(s => s.Name, s => Values(s.Query()));
    internal static object[] CaptureHollowInk(bool filledOnly)
    {
        var records = new List<object>();
        foreach (bool filled in filledOnly ? new[] { true } : new[] { false, true })
            foreach (bool round in new[] { false, true })
            {
                var geometry = (PathGeometry)Hollow(false, round);
                geometry.Figures[0].IsFilled = filled;
                var pen = Dash([1, 2], round ? 0 : .5, 2, round ? PenLineCap.Round : PenLineCap.Flat);
                var visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen()) context.DrawGeometry(null, pen, geometry);
                Rect bounds = VisualTreeHelper.GetContentBounds(visual);
                byte[] pixels = Pixels(visual, 64, 48);
                Rect ink = Rect.Empty;
                int outside = 0;
                for (int y = 0; y < 48; y++)
                    for (int x = 0; x < 64; x++)
                        if (pixels[(y * 64 + x) * 4 + 3] != 0)
                        {
                            ink.Union(new Rect(x, y, 1, 1));
                            if (x < Math.Floor(bounds.Left) || x >= Math.Ceiling(bounds.Right) ||
                                y < Math.Floor(bounds.Top) || y >= Math.Ceiling(bounds.Bottom)) outside++;
                        }
                records.Add(new { name = (round ? "closed-round" : "half-phase") + (filled ? "-filled-control" : "-hollow"),
                    bounds = Values(bounds), inkBounds = Values(ink), outsideReportedRasterCells = outside,
                    pixels = Convert.ToBase64String(pixels) });
            }
        return records.ToArray();
    }

    internal static void RunHollow(Action<string, Func<bool>, bool> check)
    {
        var reference = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "DashedBoundsReference.json")))!;
        foreach (var sample in HollowSamples())
#if PORTABLE_GEOMETRY_TESTS
            check("dashed-bounds-" + sample.Name, () => UsesHollowBackendOutline(sample.Name)
                ? HollowBackendOutline(sample.Name, sample.Query()) : Equal(sample.Query(), reference[sample.Name]), true);
#else
            check("dashed-bounds-" + sample.Name, () => Equal(sample.Query(), reference[sample.Name]), true);
#endif
    }

#if PORTABLE_GEOMETRY_TESTS
    private static bool UsesHollowBackendOutline(string name) => name is
        "hollow-path-closed-Round" or "hollow-path-closed-Round-retained" or
        "hollow-stream-closed-Round" or "hollow-stream-closed-Round-retained" or
        "hollow-phase-0.5" or "hollow-phase-3.5";

    private static bool HollowBackendOutline(string name, Rect actual)
    {
        bool round = name.Contains("closed", StringComparison.Ordinal);
        Geometry geometry = Hollow(name.Contains("stream", StringComparison.Ordinal), round);
        var pen = Dash([1, 2], round ? 0 : name == "hollow-phase-3.5" ? 3.5 : .5, 2,
            round ? PenLineCap.Round : PenLineCap.Flat);
        // Round uses the preserved old-Core filled outline; phase uses the stock occupied-pixel box.
        double[] outline = round ? [0, 0, 31, 25.400001525878906] : [0, 0, 31, 21];
        string raster = round ? "AB11FAF307F2A62771EDAB8551309685BF56CA885FC19D8E5CF9D75366EE9E47" :
            "07EE84EC1EFC20E09A891144A956D89ADD72265E7ED65F41FE058A0BF4E01AFC";
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen()) context.DrawGeometry(null, pen, geometry);
        if (!Equal(actual, outline) || !Equal(VisualTreeHelper.GetContentBounds(visual), outline)) return false;
        byte[] pixels = Pixels(visual, 64, 48);
        if (Convert.ToHexString(SHA256.HashData(pixels)) != raster) return false;
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++)
                if (pixels[(y * 64 + x) * 4 + 3] != 0 &&
                    (x < Math.Floor(actual.Left) || x >= Math.Ceiling(actual.Right) ||
                     y < Math.Floor(actual.Top) || y >= Math.Ceiling(actual.Bottom))) return false;
        return true;
    }
#endif

    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        var reference = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "DashedBoundsReference.json")))!;
        foreach (var sample in Samples())
        {
#if PORTABLE_GEOMETRY_TESTS
            // MIL treats dash/edge endpoints within .1 as coincident; Skia retains that corner continuation.
            if (sample.Name == "world-skew")
            {
                check("dashed-bounds-world-skew-backend-outline", () => Equal(sample.Query(), Values(Content(DrawManualFocus,
                    new MatrixTransform(2, .25, .5, 3, 7, -11)))), true);
                continue;
            }
#endif
            check("dashed-bounds-" + sample.Name, () => Equal(sample.Query(), reference[sample.Name]), true);
        }
        RunHollow(check);
        check("dashed-bounds-retained-mutation-and-restore", RetainedMutation, true);
        check("dashed-bounds-focus-paint-and-query", FocusPaint, true);
        check("dashed-bounds-rounded-curve", RoundedDashBoundsCases.LegacyCurve, true);
        foreach (var sample in RectangleRouteSamples())
            check("dashed-bounds-rectangle-route-" + sample.Name, () => CheckRectangleRoute(sample), true);
#if PORTABLE_GEOMETRY_TESTS
        check("dashed-bounds-affine-ink-outline-and-restore", AffineInk, true);
#endif
        check("dashed-bounds-collections-unchanged", () =>
        {
            var pen = Dash([1, 2, 3], -.5, 2);
            double[] original = pen.DashStyle.Dashes.ToArray();
            Rect before = Rectangle().GetRenderBounds(pen);
            bool mutable = pen.DashStyle.Dashes.SequenceEqual(original) && pen.DashStyle.Offset == -.5;
            pen.Freeze();
            return mutable && Equal(Rectangle().GetRenderBounds(pen), Values(before)) && pen.DashStyle.Dashes.SequenceEqual(original);
        }, true);
#if PORTABLE_GEOMETRY_TESTS
        foreach (var sample in UnsupportedSamples())
            check("dashed-bounds-reject-" + sample.Name, () => Reject(sample.Query, sample.Diagnostic), true);
#endif
    }

    private static List<(string Name, Func<Rect> Query)> Samples()
    {
        var samples = new List<(string, Func<Rect>)>();
        foreach (double offset in new[] { 0d, .5, -.5, 3.5, -3.5 })
        {
            var pen = Dash([1, 2], offset);
            Add(FormattableString.Invariant($"focus-rectangle-{offset}"), () => Rectangle().GetRenderBounds(pen));
            Add(FormattableString.Invariant($"focus-path-{offset}"), () => RectPath().GetRenderBounds(pen));
            Add(FormattableString.Invariant($"focus-retained-{offset}"), () => Content(d => d.DrawRoundedRectangle(null, pen, FocusRect, 0, 0)));
        }
        foreach (PenLineCap cap in new[] { PenLineCap.Flat, PenLineCap.Round, PenLineCap.Square })
            foreach (double offset in new[] { 0d, 2d, -2d, 11d, -11d })
            {
                var pen = Dash([1, 8], offset, 2, cap);
                Add(FormattableString.Invariant($"sparse-line-{cap}-{offset}"), () => Line().GetRenderBounds(pen));
            }
        foreach (var pair in new[] { (Name: "odd", Dashes: new[] { 1d, 2, 3 }), (Name: "even", Dashes: new[] { 1d, 2, 3, 1, 2, 3 }),
            (Name: "negative", Dashes: new[] { -1d, -2, -3 }), (Name: "zero-off", Dashes: new[] { 1d, 0, 2, 3 }), (Name: "zero-on", Dashes: new[] { 0d, 2, 3, 1 }) })
        {
            var pen = Dash(pair.Dashes, -.5, 2);
            Add(pair.Name, () => RectPath().GetRenderBounds(pen));
        }
        var focus = Dash([1, 2]);
        foreach (var entry in new[] { (Name: "scale", Matrix: new Matrix(2, 0, 0, 3, 7, -11)),
            (Name: "skew", Matrix: new Matrix(2, .25, .5, 3, 7, -11)), (Name: "reflection", Matrix: new Matrix(-2, 0, 0, 1.5, 400, 5)) })
        {
            Add("geometry-" + entry.Name, () => { var geometry = Rectangle(); geometry.Transform = new MatrixTransform(entry.Matrix); return geometry.GetRenderBounds(focus); });
            Add("world-" + entry.Name, () => Content(d => d.DrawRoundedRectangle(null, focus, FocusRect, 0, 0), new MatrixTransform(entry.Matrix)));
        }
        foreach (double tolerance in new[] { 0d, .01, .25, 1000 })
            Add(FormattableString.Invariant($"tolerance-{tolerance}"), () => Rectangle().GetRenderBounds(focus, tolerance, ToleranceType.Absolute));
        Add("relative-tolerance", () => Rectangle().GetRenderBounds(focus, .001, ToleranceType.Relative));
        Add("zero-width", () => Rectangle().GetRenderBounds(Dash([1, 2], 0, 0)));
        Add("width-skips-invalid-cycle", () => Rectangle().GetRenderBounds(Dash([0, 0], 0, 0)));
        Add("tolerance-skips-invalid-cycle", () => Rectangle().GetRenderBounds(Dash([0, 0]), 1000, ToleranceType.Absolute));
        Add("rectangle-null-brush", () => Rectangle().GetRenderBounds(new Pen(null, 1) { DashStyle = new DashStyle([1, 2], 0), DashCap = PenLineCap.Flat }));
        Add("path-null-brush", () => RectPath().GetRenderBounds(new Pen(null, 1) { DashStyle = new DashStyle([1, 2], 0), DashCap = PenLineCap.Flat }));
        Add("filled-open-path", () => OpenPath().GetRenderBounds(Dash([1, 8], -2, 2, PenLineCap.Square)));
        Add("stream", () => Stream().GetRenderBounds(focus));
        Add("retained-line", () => Content(d => d.DrawLine(Dash([1, 8], 0, 2, PenLineCap.Square), new Point(10, 10), new Point(20, 10))));
        Add("retained-path", () => Content(d => d.DrawGeometry(null, focus, RectPath())));
        Add("sparse-solid-control", () => Line().GetRenderBounds(new Pen(Brushes.Black, 2) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square }));
        return samples;
        void Add(string name, Func<Rect> query) => samples.Add((name, query));
    }

    private static IEnumerable<(string Name, Func<Rect> Query)> HollowSamples()
    {
        yield return ("hollow-oxyplot-longdash-polyline", () => OxyPlotLine().GetRenderBounds(Dash([10, 1])));
        yield return ("hollow-oxyplot-longdash-retained", () => Content(d => d.DrawGeometry(null, Dash([10, 1]), OxyPlotLine())));
        yield return ("hollow-oxyplot-longdash-path-layout", HollowPathLayout);
        yield return ("hollow-simple-open-line", () => { var path = OpenPath(); path.Figures[0].IsFilled = false; return path.GetRenderBounds(Dash([1, 2])); });
        foreach (bool stream in new[] { false, true })
            foreach (bool closed in new[] { false, true })
                foreach (PenLineCap cap in new[] { PenLineCap.Flat, PenLineCap.Round, PenLineCap.Square })
                {
                    var geometry = Hollow(stream, closed);
                    var pen = Dash([1, 2], 0, 2, cap);
                    string name = $"hollow-{(stream ? "stream" : "path")}-{(closed ? "closed" : "open")}-{cap}";
                    yield return (name, () => geometry.GetRenderBounds(pen));
                    yield return (name + "-retained", () => Content(d => d.DrawGeometry(null, pen, geometry)));
                }
        foreach (double offset in new[] { .5, -.5, 3.5, -3.5 })
        {
            var pen = Dash([1, 2], offset, 2);
            yield return (FormattableString.Invariant($"hollow-phase-{offset}"), () => Hollow(false, false).GetRenderBounds(pen));
        }
        foreach (var entry in new[] { (Name: "translation", Matrix: new Matrix(1, 0, 0, 1, 40, 30)),
            (Name: "scale", Matrix: new Matrix(2, 0, 0, 3, 7, 9)), (Name: "reflection", Matrix: new Matrix(-2, 0, 0, 1.5, 120, 5)) })
        {
            var geometry = Hollow(false, false);
            var pen = Dash([1, 2], 0, 2);
            yield return ("hollow-geometry-" + entry.Name, () => { geometry.Transform = new MatrixTransform(entry.Matrix); return geometry.GetRenderBounds(pen); });
            yield return ("hollow-world-" + entry.Name, () => Content(d => d.DrawGeometry(null, pen, Hollow(false, false)), new MatrixTransform(entry.Matrix)));
        }
        yield return ("hollow-zero-width", () => Hollow(false, false).GetRenderBounds(Dash([0, 0], 0, 0)));
        yield return ("hollow-skipped-pen", () => Hollow(false, false).GetRenderBounds(Dash([0, 0]), 1000, ToleranceType.Absolute));
        yield return ("hollow-world-skipped-pen", () => Content(d => d.DrawGeometry(null, Dash([0, 0], 0, 0), Hollow(false, false)), new TranslateTransform(40, 30)));
        yield return ("hollow-all-ink-skipped", () =>
        {
            var path = OpenPath(); path.Figures[0].IsFilled = false;
            return path.GetRenderBounds(Dash([1, 100], 2, 2));
        });
        yield return ("hollow-multiple-figures", () =>
        {
            var path = (PathGeometry)Hollow(false, false);
            path.Figures.Add(new PathFigure(new Point(60, 40), [new LineSegment(new Point(70, 40), true)], false) { IsFilled = false });
            return path.GetRenderBounds(Dash([1, 2], 0, 2));
        });
        yield return ("hollow-mixed-filled-figures", () =>
        {
            var path = (PathGeometry)Hollow(false, false);
            path.Figures.Add(new PathFigure(new Point(60, 40), [new LineSegment(new Point(70, 40), true), new LineSegment(new Point(70, 50), true)], true));
            return path.GetRenderBounds(Dash([1, 2], 0, 2));
        });
    }

    private static StreamGeometry OxyPlotLine()
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(10, 20), false, false);
            context.PolyLineTo([new Point(90, 20)], true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Rect HollowPathLayout()
    {
        Rect result = Rect.Empty;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var path = new System.Windows.Shapes.Path { Data = OxyPlotLine(), Stroke = Brushes.Black,
                    StrokeThickness = 1, StrokeDashArray = new DoubleCollection([10, 1]), Stretch = Stretch.None };
                path.Measure(new Size(128, 64));
                path.Arrange(new Rect(0, 0, 128, 64));
                result = VisualTreeHelper.GetContentBounds(path);
            }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static Geometry Hollow(bool stream, bool closed)
    {
        if (!stream)
            return new PathGeometry([new PathFigure(new Point(10, 10),
                [new LineSegment(new Point(30, 10), true), new LineSegment(new Point(30, 25), true)], closed) { IsFilled = false }]);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(10, 10), false, closed);
            context.LineTo(new Point(30, 10), true, false);
            context.LineTo(new Point(30, 25), true, false);
        }
        return geometry;
    }

    private static bool RetainedMutation()
    {
        var pen = Dash([1, 8], 0, 2, PenLineCap.Square);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawLine(pen, new Point(10, 10), new Point(20, 10));
        Rect Bounds() => VisualTreeHelper.GetContentBounds(visual);
        bool Frame(double[] expected)
        {
            Rect before = Bounds();
            Pixels(visual, 32, 24);
            return Equal(before, expected) && Equal(Bounds(), expected);
        }
        if (!Frame([9, 9, 11, 2])) return false;
        pen.DashStyle.Offset = 2;
        if (!Frame([10, 10, 10, 0])) return false;
        pen.DashStyle.Offset = -2;
        if (!Frame([10, 9, 10, 2])) return false;
        pen.DashStyle.Offset = 0;
        if (!Frame([9, 9, 11, 2])) return false;
        pen.DashStyle.Dashes[1] = 1;
        if (!Frame([9, 9, 12, 2])) return false;
        pen.DashStyle.Dashes[1] = 8;
        return Frame([9, 9, 11, 2]);
    }

    internal static object CaptureInk()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(2, .25, .5, 3, 7, 9));
            context.DrawRoundedRectangle(null, Dash([1, 2]), FocusRect, 0, 0);
            context.Pop();
        }
        Rect bounds = VisualTreeHelper.GetContentBounds(visual);
        byte[] bytes = Pixels(visual, 480, 120);
        Rect ink = Rect.Empty;
        int outside = 0, outsideStock = 0;
        Rect stock = new(7.734375, 9.125, 362, 85.65625);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 480; x++)
                if (bytes[(y * 480 + x) * 4 + 3] != 0)
                {
                    ink.Union(new Rect(x, y, 1, 1));
                    if (!Within(bounds, x, y)) outside++;
                    if (!Within(stock, x, y)) outsideStock++;
                }
        return new { bounds = Values(bounds), inkPixelBounds = Values(ink), outsideOutlineRasterCells = outside, outsideStockRasterCells = outsideStock };
        static bool Within(Rect rect, int x, int y) => x >= Math.Floor(rect.Left) && y >= Math.Floor(rect.Top) && x < Math.Ceiling(rect.Right) && y < Math.Ceiling(rect.Bottom);
    }

    private static byte[] Pixels(DrawingVisual visual, int width, int height)
    {
#if PORTABLE_GEOMETRY_TESTS
        Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [visual, width, height, new DpiScale(1, 1)], null)!;
        type.GetMethod("Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null);
        using var bitmap = (SKBitmap)type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null)!;
        return bitmap.GetPixelSpan().ToArray();
#else
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bytes = new byte[width * height * 4];
        bitmap.CopyPixels(bytes, width * 4, 0);
        return bytes;
#endif
    }

    private static void DrawManualFocus(DrawingContext context)
    {
        Point[] vertices = [FocusRect.TopLeft, FocusRect.TopRight, FocusRect.BottomRight, FocusRect.BottomLeft, FocusRect.TopLeft];
        double[] distances = [0, FocusRect.Width, FocusRect.Width + FocusRect.Height, 2 * FocusRect.Width + FocusRect.Height,
            2 * (FocusRect.Width + FocusRect.Height)];
        var pen = new Pen(Brushes.Black, 1) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Miter };
        var runs = new PathGeometry();
        // Independent solid runs: retain intermediate vertices when an ON interval crosses a corner.
        for (double start = 0; start < distances[4]; start += 3)
        {
            double end = Math.Min(start + 1, distances[4]);
            var figure = new PathFigure { StartPoint = At(start) };
            for (int index = 1; index < 4; index++)
                if (distances[index] > start && distances[index] < end) figure.Segments.Add(new LineSegment(vertices[index], true));
            figure.Segments.Add(new LineSegment(At(end), true));
            runs.Figures.Add(figure);
        }
        context.DrawGeometry(null, pen, runs);
        Point At(double distance)
        {
            int edge = 0;
            while (edge < 3 && distance > distances[edge + 1]) edge++;
            double ratio = (distance - distances[edge]) / (distances[edge + 1] - distances[edge]);
            return vertices[edge] + ratio * (vertices[edge + 1] - vertices[edge]);
        }
    }

#if PORTABLE_GEOMETRY_TESTS
    private static bool AffineInk()
    {
        var transform = new MatrixTransform();
        var actual = new DrawingVisual();
        using (var context = actual.RenderOpen())
        {
            context.PushTransform(transform);
            context.DrawRoundedRectangle(null, Dash([1, 2]), FocusRect, 0, 0);
            context.Pop();
        }
        var manual = new DrawingVisual();
        using (var context = manual.RenderOpen()) { context.PushTransform(transform); DrawManualFocus(context); context.Pop(); }
        Matrix skew = new(2, .25, .5, 3, 7, 9);
        byte[]? original = null;
        foreach (Matrix matrix in new[] { skew, new Matrix(-2, 0, 0, 1.5, 400, 5), skew })
        {
            transform.Matrix = matrix;
            Rect before = VisualTreeHelper.GetContentBounds(actual);
            if (!Equal(before, Values(VisualTreeHelper.GetContentBounds(manual)))) throw new InvalidOperationException("Independent affine outline bounds differ: " + matrix);
            byte[] pixels = Pixels(actual, 480, 120);
            var control = Pixels(manual, 480, 120);
            if (!pixels.SequenceEqual(control))
            {
                var differences = pixels.Select((v, i) => (Index: i, Delta: Math.Abs(v - control[i]))).Where(d => d.Delta != 0).ToArray();
                throw new InvalidOperationException($"Independent affine raster differs at {differences.Length} channels, max {differences.Max(d => d.Delta)}, first {differences[0].Index}: {matrix}");
            }
            for (int y = 0; y < 120; y++)
                for (int x = 0; x < 480; x++)
                    if (pixels[(y * 480 + x) * 4 + 3] != 0 &&
                        (x < Math.Floor(before.Left) || x >= Math.Ceiling(before.Right) || y < Math.Floor(before.Top) || y >= Math.Ceiling(before.Bottom))) return false;
            if (!Equal(VisualTreeHelper.GetContentBounds(actual), Values(before))) return false;
            if (matrix == skew)
            {
                if (original != null && !pixels.SequenceEqual(original)) return false;
                original = pixels;
            }
        }
        return true;
    }
#endif

    private sealed record RectangleRouteSample(string Name, double Offset, bool Filled, Matrix Transform, double RadiusX = 0, double RadiusY = 0);
    private sealed record RectangleRoute(string Name, Rect PublicBounds, Rect Before, Rect After, byte[] Pixels, Rect Ink, int Outside);

    private static RectangleRouteSample[] RectangleRouteSamples() =>
    [
        new("sparse-null", 0, false, Matrix.Identity),
        new("sparse-fill", 0, true, Matrix.Identity),
        new("phase-positive", 1, false, Matrix.Identity),
        new("phase-negative", -1, false, Matrix.Identity),
        new("phase-positive-wrap", 101, false, Matrix.Identity),
        new("phase-negative-wrap", -101, false, Matrix.Identity),
        new("scaled-geometry", 0, false, new Matrix(2, 0, 0, 1.5, 5, 3)),
        new("reflected-geometry", 0, false, new Matrix(-1, 0, 0, 1, 80, 0)),
        new("zero-x-radius", 0, false, Matrix.Identity, 0, 7),
        new("zero-y-radius", 0, false, Matrix.Identity, 7, 0)
    ];

    internal static object CaptureRectangleRoutes() => RectangleRouteSamples().Select(sample =>
    {
        RectangleRoute[] routes = RectangleRoutes(sample);
        byte[] reference = routes.Single(route => route.Name == "genuine-path").Pixels;
        return new
        {
            sample.Name, sample.Offset, sample.Filled, matrix = sample.Transform.ToString(System.Globalization.CultureInfo.InvariantCulture),
            routes = routes.Select(route => new
            {
                route.Name, publicBounds = Values(route.PublicBounds), before = Values(route.Before), after = Values(route.After),
                ink = route.Ink.IsEmpty ? null : Values(route.Ink), outsideRasterCells = route.Outside,
                mismatchChannels = route.Pixels.Zip(reference).Count(pair => pair.First != pair.Second),
                maximumDifference = route.Pixels.Zip(reference).Max(pair => Math.Abs(pair.First - pair.Second)),
                pixelsSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(route.Pixels))
            })
        };
    }).ToArray();

    private static bool CheckRectangleRoute(RectangleRouteSample sample)
    {
        RectangleRoute[] routes = RectangleRoutes(sample);
        RectangleRoute reference = routes.Single(route => route.Name == "genuine-path");
        foreach (RectangleRoute route in routes)
        {
            if (!Equal(route.PublicBounds, Values(reference.PublicBounds)) ||
                !Equal(route.Before, Values(route.PublicBounds)) || !Equal(route.After, Values(route.Before)))
                throw new InvalidOperationException($"{sample.Name}/{route.Name}: public and retained bounds changed across rendering.");
            if (route.Outside != 0)
                throw new InvalidOperationException($"{sample.Name}/{route.Name}: {route.Outside} occupied raster cells lie outside reported bounds.");
            if (!route.Pixels.SequenceEqual(reference.Pixels))
                throw new InvalidOperationException($"{sample.Name}/{route.Name}: raster differs from the genuine top-left WPF path.");
        }
        return true;
    }

    private static RectangleRoute[] RectangleRoutes(RectangleRouteSample sample)
    {
        var rectangle = new RectangleGeometry(new Rect(10, 10, 20, 40), sample.RadiusX, sample.RadiusY)
            { Transform = new MatrixTransform(sample.Transform) };
        PathGeometry path = PathGeometry.CreateFromGeometry(rectangle);
        var pen = Dash([1, 100], sample.Offset, 2);
        Brush? fill = sample.Filled ? Brushes.Blue : null;
        var routes = new List<RectangleRoute>
        {
            Capture("rectangle-geometry", rectangle, context => context.DrawGeometry(fill, pen, rectangle)),
            Capture("genuine-path", path, context => context.DrawGeometry(fill, pen, path))
        };
        if (sample.Transform.IsIdentity)
            routes.Add(Capture("rounded-rectangle-command", rectangle,
                context => context.DrawRoundedRectangle(fill, pen, rectangle.Rect, sample.RadiusX, sample.RadiusY)));
        return routes.ToArray();

        RectangleRoute Capture(string name, Geometry geometry, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) draw(context);
            Rect before = VisualTreeHelper.GetContentBounds(visual);
            byte[] pixels = Pixels(visual, 128, 128);
            Rect after = VisualTreeHelper.GetContentBounds(visual);
            Rect ink = Rect.Empty;
            int outside = 0;
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    if (pixels[(y * 128 + x) * 4 + 3] != 0)
                    {
                        ink.Union(new Rect(x, y, 1, 1));
                        if (x < Math.Floor(before.Left) || x >= Math.Ceiling(before.Right) ||
                            y < Math.Floor(before.Top) || y >= Math.Ceiling(before.Bottom)) outside++;
                    }
            return new(name, geometry.GetRenderBounds(pen), before, after, pixels, ink, outside);
        }
    }

    private static bool FocusPaint()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRoundedRectangle(null, Dash([1, 2]), FocusRect, 0, 0);
        Rect before = VisualTreeHelper.GetContentBounds(visual);
        byte[] bytes = Pixels(visual, 182, 20);
        int ink = 0;
        var sides = new bool[4];
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 182; x++)
            {
                if (bytes[(y * 182 + x) * 4 + 3] == 0) continue;
                if (x >= Math.Ceiling(before.Right) || y >= Math.Ceiling(before.Bottom)) return false;
                ink++;
                if (x == 0) sides[0] = true;
                if (y == 0) sides[1] = true;
                if (x == 177) sides[2] = true;
                if (y == 13) sides[3] = true;
            }
        return ink > 0 && sides.All(s => s) && Equal(before, [0, 0, 178, 13.96875]) && Equal(VisualTreeHelper.GetContentBounds(visual), Values(before));
    }

#if PORTABLE_GEOMETRY_TESTS
    private static IEnumerable<(string Name, Func<Rect> Query, string Diagnostic)> UnsupportedSamples()
    {
        var pen = Dash([1, 2]);
        yield return ("collapsed", () => new LineGeometry(new Point(10, 10), new Point(10, 10)).GetRenderBounds(pen), "collapsed");
        yield return ("post-transform-collapse", () => new LineGeometry(new Point(10, 10), new Point(20, 10)) { Transform = new ScaleTransform(0, 1) }.GetRenderBounds(pen), "collapsed");
        yield return ("mixed-gap", () => { var p = OpenPath(); p.Figures[0].Segments[0].IsStroked = false; return p.GetRenderBounds(pen); }, "unstroked");
        yield return ("quadratic-curve", () => new PathGeometry([new PathFigure(new Point(10, 10),
            [new QuadraticBezierSegment(new Point(20, 0), new Point(30, 10), true)], false)]).GetRenderBounds(pen), "linear or cubic");
        yield return ("conic-curve", () => new PathGeometry([new PathFigure(new Point(10, 10),
            [new ArcSegment(new Point(30, 10), new Size(10, 10), 0, false, SweepDirection.Clockwise, true)], false)]).GetRenderBounds(pen), "linear or cubic");
        yield return ("group", () => new GeometryGroup { Children = { Rectangle() } }.GetRenderBounds(pen), "type");
        yield return ("singular-world", () => Content(d => d.DrawRoundedRectangle(null, pen, FocusRect, 0, 0), new ScaleTransform(0, 1)), "singular");
        foreach (var entry in new[] { (Name: "tiny-cycle", Pen: Dash([.001, .001]), Diagnostic: "tiny"), (Name: "zero-cycle", Pen: Dash([0, 0]), Diagnostic: "zero"),
            (Name: "invalid-offset", Pen: Dash([1, 2], double.PositiveInfinity), Diagnostic: "offset"),
            (Name: "mixed-caps", Pen: new Pen(Brushes.Black, 1) { DashStyle = new DashStyle([1, 2], 0) }, Diagnostic: "caps"),
            (Name: "zero-round-on", Pen: Dash([0, 2], 0, 1, PenLineCap.Round), Diagnostic: "zero-length") })
            yield return (entry.Name, () => Rectangle().GetRenderBounds(entry.Pen), entry.Diagnostic);
    }
    private static bool Reject(Func<Rect> query, string text)
    {
        try { query(); return false; }
        catch (NotSupportedException error) { return error.Message.Contains(text, StringComparison.OrdinalIgnoreCase); }
    }
#endif
    private static readonly Rect FocusRect = new(.5, .5, 177, 12.96875);
    private static RectangleGeometry Rectangle() => new(FocusRect);
    private static LineGeometry Line() => new(new Point(10, 10), new Point(20, 10));
    private static PathGeometry RectPath() => new([new PathFigure(FocusRect.TopLeft,
        [new LineSegment(FocusRect.TopRight, true), new LineSegment(FocusRect.BottomRight, true), new LineSegment(FocusRect.BottomLeft, true)], true)]);
    private static PathGeometry OpenPath() => new([new PathFigure(new Point(10, 10), [new LineSegment(new Point(20, 10), true)], false)]);
    private static StreamGeometry Stream()
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(FocusRect.TopLeft, true, true);
        context.PolyLineTo([FocusRect.TopRight, FocusRect.BottomRight, FocusRect.BottomLeft], true, false);
        return geometry;
    }
    private static Pen Dash(double[] intervals, double offset = 0, double width = 1, PenLineCap cap = PenLineCap.Flat) => new(Brushes.Black, width)
        { DashStyle = new DashStyle(intervals, offset), StartLineCap = cap, EndLineCap = cap, DashCap = cap };
    private static Rect Content(Action<DrawingContext> draw, Transform? world = null)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) { if (world != null) context.PushTransform(world); draw(context); if (world != null) context.Pop(); }
        return VisualTreeHelper.GetContentBounds(visual);
    }
    private static double[] Values(Rect rect) => [rect.X, rect.Y, rect.Width, rect.Height];
    private static bool Equal(Rect actual, double[] expected)
    {
        double magnitude = expected.Max(Math.Abs);
        float scalar = (float)magnitude;
        double tolerance = Math.Max(1e-6, (double)MathF.BitIncrement(scalar) - scalar);
        return !actual.IsEmpty && Values(actual).Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
    }
}
