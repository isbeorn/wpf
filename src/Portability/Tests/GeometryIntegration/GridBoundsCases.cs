using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
#if PORTABLE_GEOMETRY_TESTS
using SkiaSharp;
#endif

internal static class GridBoundsCases
{
    private const int Width = 1280, Height = 820;
    private sealed record Sample(string Name, StreamGeometry Geometry, Matrix World, double Phase);
    private static readonly Matrix Translation = new(1, 0, 0, 1, 723, 16);

    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GridBoundsReference.json")));
        var expected = reference.RootElement.GetProperty("samples").EnumerateArray()
            .Where(s => s.TryGetProperty("queries", out _)).ToDictionary(s => s.GetProperty("name").GetString()!);
        var original = Original();
        var rows = Rows(original);
        var pen = Pen();
        var rasters = new Dictionary<string, byte[]>();
        foreach (var sample in Samples(original, rows))
        {
            // These are the original stock calibration's scenarios, not a Cartesian corpus.
            pen.DashStyle.Offset = sample.Phase;
            var stock = expected[sample.Name];
            foreach (var query in Queries(sample.Geometry, pen))
                check("grid-" + sample.Name + "/" + query.Name, () => Equal(query.Query(), stock.GetProperty("queries").EnumerateArray()
                    .Single(q => q.GetProperty("name").GetString() == query.Name).GetProperty("bounds")), true);
            check("grid-" + sample.Name + "/retained-paint-controls", () =>
            {
                var drawing = new GeometryDrawing(null, pen, sample.Geometry);
                var group = new DrawingGroup { Transform = new MatrixTransform(sample.World) };
                group.Children.Add(drawing);
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen()) context.DrawDrawing(group);
                Rect before = VisualTreeHelper.GetContentBounds(visual);
                Rect descendants = VisualTreeHelper.GetDescendantBounds(visual);
                if (!Equal(before, stock.GetProperty("contentBefore")) || !Equal(descendants, stock.GetProperty("descendantBefore"))
                    || !Equal(drawing.Bounds, stock.GetProperty("geometryDrawingBounds")) || !Equal(group.Bounds, stock.GetProperty("drawingGroupBounds"))) return false;
                byte[] pixels = Pixels(visual);
                if (before != VisualTreeHelper.GetContentBounds(visual) || descendants != VisualTreeHelper.GetDescendantBounds(visual)) return false;
                var directed = Rows(sample.Geometry);
                if (!pixels.AsSpan().SequenceEqual(PaintRows(directed, pen, sample.Geometry.Transform.Value, sample.World))) return false;
                if (!Covered(pixels, before)) return false;
                rasters[sample.Name] = pixels;
                return true;
            }, true);
        }
        check("grid-negative-phase-period-and-restore", () => Same("phase-2.75", "phase--8.25") && Same("phase-1.5", "phase--9.5")
            && Same("phase-0", "phase-11") && Same("phase-0", "phase-restore0"), true);
        check("grid-geometry-world-translation-and-gap-ink", () => Same("exact-world-translate723-16", "geometry-translate723-16")
            && new[] { "leading-gap", "outside-origin-leading-gap", "trailing-gap", "outside-origin-both-gaps", "original-order-and-phase-restored" }
                .All(name => Same("exact-world-translate723-16", name)), true);
        check("grid-stroked-connectors-negative-control", () =>
        {
            var wrong = PathGeometry.CreateFromGeometry(original);
            foreach (var segment in wrong.Figures[0].Segments) segment.IsStroked = true;
            return !Paint(wrong, Pen(), Translation).AsSpan().SequenceEqual(rasters["exact-world-translate723-16"]);
        }, true);
        check("grid-pathgeometry-public-and-retained", () =>
        {
            var path = PathGeometry.CreateFromGeometry(original);
            return Equal(path.GetRenderBounds(Pen()), original.GetRenderBounds(Pen())) && Paint(path, Pen(), Translation).AsSpan().SequenceEqual(rasters["exact-world-translate723-16"]);
        }, true);
        check("grid-retained-pen-transform-mutation-and-restore", () => RetainedMutation(original, rasters), true);
        check("grid-arbitrary-direction-reflection-and-affine-controls", AffineControls, true);
#if PORTABLE_GEOMETRY_TESTS
        check("grid-same-path-pen-rejection-and-restoration", RejectAndRestore, true);
        check("grid-fully-stroked-hollow-bounds-cover-ink", () =>
        {
            var path = PathGeometry.CreateFromGeometry(original);
            foreach (var segment in path.Figures[0].Segments) segment.IsStroked = true;
            Rect bounds = VisualBounds(path, Pen(), Translation);
            return bounds.Contains(new Point(0, 0)) && Covered(Paint(path, Pen(), Translation), bounds);
        }, true);
        foreach (var sample in Rejected())
            check("grid-reject-and-recover-" + sample.Name, () =>
            {
                try { sample.Geometry.GetRenderBounds(sample.Pen); return false; }
                catch (NotSupportedException) { return Equal(original.GetRenderBounds(Pen()), expected["phase-0"].GetProperty("queries")[1].GetProperty("bounds")); }
            }, true);
        check("grid-dashed-containment-remains-explicit", () =>
        {
            try { original.StrokeContains(Pen(), new Point(40, 768.5)); return false; }
            catch (NotSupportedException error) { return error.Message.Contains("dashed", StringComparison.OrdinalIgnoreCase); }
        }, true);
#endif
        bool Same(string left, string right) => rasters.TryGetValue(left, out var a) && rasters.TryGetValue(right, out var b) && a.AsSpan().SequenceEqual(b);
    }

    private static IEnumerable<Sample> Samples(StreamGeometry original, (Point Start, Point End)[] rows)
    {
        yield return new("phase-0", original, Matrix.Identity, 0);
        foreach (double phase in new[] { 2.75, -8.25, 1.5, -9.5, 11d })
            yield return new("phase-" + phase.ToString(System.Globalization.CultureInfo.InvariantCulture), original, Matrix.Identity, phase);
        yield return new("phase-restore0", original, Matrix.Identity, 0);
        yield return new("exact-world-translate723-16", original, Translation, 0);
        var translated = original.Clone(); translated.Transform = new MatrixTransform(Translation);
        yield return new("geometry-translate723-16", translated, Matrix.Identity, 0);
        yield return new("reverse-row-order", BuildRows(rows.Reverse()), Translation, 2.75);
        yield return new("reverse-run-direction", BuildRows(rows.Select(r => (r.End, r.Start))), Translation, 2.75);
        yield return new("original-order-and-phase-restored", original, Translation, 0);
        yield return new("leading-gap", BuildRows(rows, new Point(5.5, 790.5)), Translation, 0);
        yield return new("outside-origin-leading-gap", BuildRows(rows, new Point(-100.5, -40.5)), Translation, 0);
        yield return new("trailing-gap", BuildRows(rows, tail: new Point(510.5, 5.5)), Translation, 0);
        yield return new("outside-origin-both-gaps", BuildRows(rows, new Point(-100.5, -40.5), new Point(510.5, 5.5)), Translation, 0);
    }

    private static IEnumerable<(string Name, Func<Rect> Query)> Queries(Geometry geometry, Pen pen)
    {
        yield return ("Geometry.Bounds", () => geometry.Bounds);
        yield return ("GetRenderBounds", () => geometry.GetRenderBounds(pen));
        yield return ("GetRenderBounds-null-pen", () => geometry.GetRenderBounds(null));
        var zero = pen.Clone(); zero.Thickness = 0;
        yield return ("GetRenderBounds-zero-width", () => geometry.GetRenderBounds(zero));
        var noBrush = pen.Clone(); noBrush.Brush = null;
        yield return ("GetRenderBounds-null-brush", () => geometry.GetRenderBounds(noBrush));
        foreach (double tolerance in new[] { 0d, .25, 1000 })
            yield return ("absolute-tolerance-" + tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture), () => geometry.GetRenderBounds(pen, tolerance, ToleranceType.Absolute));
        yield return ("relative-tolerance-.001", () => geometry.GetRenderBounds(pen, .001, ToleranceType.Relative));
    }

    private static bool RetainedMutation(StreamGeometry geometry, Dictionary<string, byte[]> expected)
    {
        var pen = Pen();
        var transform = new MatrixTransform();
        var group = new DrawingGroup { Transform = transform };
        group.Children.Add(new GeometryDrawing(null, pen, geometry));
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawDrawing(group);
        foreach (var state in new[] { (Phase: 0d, World: Matrix.Identity, Key: "phase-0"), (Phase: 1.5, World: Matrix.Identity, Key: "phase-1.5"),
            (Phase: -9.5, World: Matrix.Identity, Key: "phase--9.5"), (Phase: 0d, World: Translation, Key: "exact-world-translate723-16"), (Phase: 0d, World: Matrix.Identity, Key: "phase-0") })
        {
            pen.DashStyle.Offset = state.Phase; transform.Matrix = state.World;
            Rect before = VisualTreeHelper.GetContentBounds(visual);
            byte[] pixels = Pixels(visual);
            if (before != VisualTreeHelper.GetContentBounds(visual) || !Covered(pixels, before)
                || !pixels.AsSpan().SequenceEqual(expected[state.Key])) return false;
        }
        return true;
    }

    private static (Point Start, Point End)[] DiagonalRows() => [(new(10, 10), new(50, 30)), (new(70, 40), new(30, 60)), (new(20, 90), new(60, 110))];
    private static Matrix[] AffineMatrices() => [new(1.5, .25, .3, 1.25, 80, 40), new(-1.2, .2, .1, 1.4, 160, 5)];

    internal static object CaptureAffine()
    {
        var records = new List<object>();
        var rows = DiagonalRows();
        foreach (var matrix in AffineMatrices())
            foreach (bool geometryTransform in new[] { false, true })
            {
                var geometry = BuildRows(rows);
                Matrix local = geometryTransform ? matrix : Matrix.Identity;
                Matrix world = geometryTransform ? Matrix.Identity : matrix;
                geometry.Transform = new MatrixTransform(local);
                var pen = Pen(); pen.DashStyle.Offset = -2.75;
                byte[] actual = Paint(geometry, pen, world);
                byte[] independent = PaintRows(rows, pen, local, world);
                var differences = Enumerable.Range(0, actual.Length / 4).Where(i => !actual.AsSpan(i * 4, 4).SequenceEqual(independent.AsSpan(i * 4, 4))).ToArray();
                var rowBounds = rows.Select(row => new LineGeometry(row.Start, row.End) { Transform = new MatrixTransform(local) })
                    .Select(line => VisualBounds(line, pen, world)).ToArray();
                string bounds;
                try { bounds = VisualBounds(geometry, pen, world).ToString(System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception error) { bounds = error.GetType().Name + ": " + error.Message; }
                records.Add(new { geometryTransform, matrix = matrix.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    originalBounds = bounds, independentRowBounds = rowBounds.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    mismatchedPixels = differences.Length, mismatchedChannels = actual.Zip(independent).Count(p => p.First != p.Second),
                    maxChannelDifference = actual.Zip(independent).Max(p => Math.Abs(p.First - p.Second)),
                    actualSha256 = Convert.ToHexString(SHA256.HashData(actual)), independentSha256 = Convert.ToHexString(SHA256.HashData(independent)),
                    firstDifferences = differences.Take(8).Select(i => new { x = i % Width, y = i / Width, actual = actual.AsSpan(i * 4, 4).ToArray(), independent = independent.AsSpan(i * 4, 4).ToArray() }) });
            }
        return new { core = typeof(Geometry).Assembly.Location, coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Geometry).Assembly.Location))), records };
    }
    private static bool AffineControls()
    {
        using var stock = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GridAffineReference.json")));
        int index = 0;
        var rows = DiagonalRows();
        foreach (var matrix in AffineMatrices())
            foreach (bool geometryTransform in new[] { false, true })
            {
                var geometry = BuildRows(rows);
                Matrix local = geometryTransform ? matrix : Matrix.Identity;
                Matrix world = geometryTransform ? Matrix.Identity : matrix;
                geometry.Transform = new MatrixTransform(local);
                var pen = Pen(); pen.DashStyle.Offset = -2.75;
                byte[] actual = Paint(geometry, pen, world);
                if (!Covered(actual, VisualBounds(geometry, pen, world))) throw new InvalidOperationException("Affine ink escaped reported bounds.");
                var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) { context.PushTransform(new MatrixTransform(world)); context.DrawGeometry(null, pen, geometry); context.Pop(); }
                Rect reported = VisualTreeHelper.GetContentBounds(visual);
                string expected = stock.RootElement.GetProperty("records")[index++].GetProperty("originalBounds").GetString()!;
                if (!Equal(reported, Rect.Parse(expected))) throw new InvalidOperationException($"Affine stock bounds mismatch: {reported} vs {expected}.");
                Rect independent = new(0, 0, 0, 0);
                foreach (var row in rows)
                {
                    var single = new LineGeometry(row.Start, row.End) { Transform = new MatrixTransform(local) };
                    var rowVisual = new DrawingVisual(); using (var context = rowVisual.RenderOpen()) { context.PushTransform(new MatrixTransform(world)); context.DrawGeometry(null, pen, single); context.Pop(); }
                    independent.Union(VisualTreeHelper.GetContentBounds(rowVisual));
                }
                if (!Equal(reported, independent) || !Covered(actual, reported)) throw new InvalidOperationException($"Affine bounds mismatch: reported={reported}, independent={independent}, geometryTransform={geometryTransform}, matrix={matrix}.");
            }
        return true;
    }

#if PORTABLE_GEOMETRY_TESTS
    private static bool RejectAndRestore()
    {
        var path = PathGeometry.CreateFromGeometry(Original()); var pen = Pen();
        Rect before = path.GetRenderBounds(pen); byte[] pixels = Paint(path, pen, Translation);
        foreach (var change in new (Action Reject, Action Restore)[]
        {
            (() => path.Figures[0].IsClosed = true, () => path.Figures[0].IsClosed = false),
            (() => path.Figures[0].Segments[1].IsStroked = true, () => path.Figures[0].Segments[1].IsStroked = false),
            (() => pen.StartLineCap = pen.EndLineCap = pen.DashCap = PenLineCap.Round, () => pen.StartLineCap = pen.EndLineCap = pen.DashCap = PenLineCap.Flat)
        })
        {
            change.Reject();
            try { path.GetRenderBounds(pen); return false; }
            catch (NotSupportedException) { }
            finally { change.Restore(); }
            if (!Equal(path.GetRenderBounds(pen), before) || !Paint(path, pen, Translation).AsSpan().SequenceEqual(pixels)) return false;
        }
        return true;
    }
    private static IEnumerable<(string Name, Geometry Geometry, Pen Pen)> Rejected()
    {
        var path = PathGeometry.CreateFromGeometry(Original());
        var closed = path.Clone(); closed.Figures[0].IsClosed = true; yield return ("closed", closed, Pen());
        var joined = path.Clone(); joined.Figures[0].Segments[1].IsStroked = true; yield return ("joined", joined, Pen());
        var empty = path.Clone(); foreach (var segment in empty.Figures[0].Segments) segment.IsStroked = false; yield return ("all-gap", empty, Pen());
        var collapsed = path.Clone(); ((LineSegment)collapsed.Figures[0].Segments[0]).Point = collapsed.Figures[0].StartPoint; yield return ("collapsed-run", collapsed, Pen());
        var curve = path.Clone(); curve.Figures[0].Segments[0] = new BezierSegment(new(40, 760), new(60, 770), new(469.5, 768.5), true); yield return ("curve", curve, Pen());
        var multi = path.Clone(); multi.Figures.Add(path.Figures[0].Clone()); yield return ("multiple-figures", multi, Pen());
        var singular = path.Clone(); singular.Transform = new ScaleTransform(0, 1); yield return ("singular", singular, Pen());
        var caps = Pen(); caps.StartLineCap = caps.EndLineCap = caps.DashCap = PenLineCap.Round; yield return ("round-caps", path, caps);
        var group = new GeometryGroup(); group.Children.Add(path); yield return ("group", group, Pen());
    }
#endif

    private static StreamGeometry Original()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GridDrawingSnapshot.json"));
        if (Convert.ToHexString(SHA256.HashData(bytes)) != "5228C2FBE4F0D317E91100C4B18D7730C4D76A57D916C001211350CBF06B311E")
            throw new InvalidDataException("Original grid snapshot changed.");
        using var json = JsonDocument.Parse(bytes);
        var figure = json.RootElement.GetProperty("dashedDrawings")[0].GetProperty("geometry").GetProperty("figures")[0];
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(Point(figure.GetProperty("StartPoint")), false, false);
        foreach (var segment in figure.GetProperty("segments").EnumerateArray())
            context.LineTo(Point(segment.GetProperty("points")[0]), segment.GetProperty("IsStroked").GetBoolean(), false);
        return geometry;
    }
    private static (Point Start, Point End)[] Rows(StreamGeometry geometry)
    {
        var figure = PathGeometry.CreateFromGeometry(geometry).Figures[0];
        var rows = new List<(Point, Point)>(); var previous = figure.StartPoint;
        foreach (LineSegment segment in figure.Segments) { if (segment.IsStroked) rows.Add((previous, segment.Point)); previous = segment.Point; }
        return rows.ToArray();
    }
    private static StreamGeometry BuildRows(IEnumerable<(Point Start, Point End)> rows, Point? origin = null, Point? tail = null)
    {
        var values = rows.ToArray(); var geometry = new StreamGeometry(); using var context = geometry.Open();
        var previous = origin ?? values[0].Start; context.BeginFigure(previous, false, false);
        foreach (var row in values) { if (previous != row.Start) context.LineTo(row.Start, false, false); context.LineTo(row.End, true, false); previous = row.End; }
        if (tail.HasValue) context.LineTo(tail.Value, false, false);
        return geometry;
    }
    private static Rect VisualBounds(Geometry geometry, Pen pen, Matrix world)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) { context.PushTransform(new MatrixTransform(world)); context.DrawGeometry(null, pen, geometry); context.Pop(); }
        return VisualTreeHelper.GetContentBounds(visual);
    }
    private static byte[] Paint(Geometry geometry, Pen pen, Matrix world)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) { context.PushTransform(new MatrixTransform(world)); context.DrawGeometry(null, pen, geometry); context.Pop(); }
        return Pixels(visual);
    }
    private static byte[] PaintRows(IEnumerable<(Point Start, Point End)> rows, Pen pen, Matrix local, Matrix world)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(world));
            foreach (var row in rows)
            {
                var line = new LineGeometry(row.Start, row.End) { Transform = new MatrixTransform(local) };
                context.DrawGeometry(null, pen, line);
            }
            context.Pop();
        }
        return Pixels(visual);
    }
    private static byte[] Pixels(DrawingVisual visual)
    {
#if PORTABLE_GEOMETRY_TESTS
        Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [visual, Width, Height, new DpiScale(1, 1)], null)!;
        type.GetMethod("Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null);
        using var bitmap = (SKBitmap)type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null)!;
        return bitmap.GetPixelSpan().ToArray();
#else
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var bytes = new byte[Width * Height * 4]; bitmap.CopyPixels(bytes, Width * 4, 0); return bytes;
#endif
    }
    private static bool Covered(byte[] pixels, Rect bounds)
    {
        bool ink = false;
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            if (pixels[(y * Width + x) * 4 + 3] != 0)
            {
                ink = true;
                if (x + 1 < bounds.Left || x > bounds.Right || y + 1 < bounds.Top || y > bounds.Bottom) return false;
            }
        return ink;
    }
    private static bool Equal(Rect actual, JsonElement expected) => expected.ValueKind != JsonValueKind.Null && Equal(actual, new Rect(expected[0].GetDouble(), expected[1].GetDouble(), expected[2].GetDouble(), expected[3].GetDouble()));
    private static bool Equal(Rect actual, Rect expected)
    {
        double[] values = [expected.X, expected.Y, expected.Width, expected.Height];
        float magnitude = (float)values.Max(Math.Abs); double tolerance = Math.Max(1e-6, (double)MathF.BitIncrement(magnitude) - magnitude);
        return !actual.IsEmpty && new[] { actual.X, actual.Y, actual.Width, actual.Height }.Zip(values).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
    }
    private static Point Point(JsonElement value) => new(value.GetProperty("X").GetDouble(), value.GetProperty("Y").GetDouble());
    private static Pen Pen() => new(new SolidColorBrush(Color.FromArgb(100, 189, 195, 199)), 1)
    { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, DashCap = PenLineCap.Flat, LineJoin = PenLineJoin.Miter, MiterLimit = 10, DashStyle = new DashStyle([10, 1], 0) };
}
