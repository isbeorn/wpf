using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

internal static class DashedPenCases
{
    internal sealed record Result(string name, bool passed, string? detail = null, object? evidence = null);
    private sealed record Reference(string coreIdentity, string corePath, string coreSha256, Dictionary<string, byte[]> alpha);

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render, string[] args)
    {
        var results = new List<Result>();
        var captured = new Dictionary<string, byte[]>();
        object? observation = null;
#if !STOCK
        var reference = JsonSerializer.Deserialize<Reference>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StockDashReference.json")))!;
#endif
        foreach (double dpi in new[] { 1d, 1.5d, 2d })
            Check($"dashes-captured-Classic-item-focus-{dpi}", () =>
            {
                var focus = new FocusVisual();
                byte[] pixels = render(focus, new DpiScale(dpi, dpi), (int)(192 * dpi), (int)(24 * dpi));
                byte[] explicitPath = render(new FocusVisual(true), new DpiScale(dpi, dpi), (int)(192 * dpi), (int)(24 * dpi));
                Equal(pixels, explicitPath, "The focus primitive differs from the complete top-left path control.");
                Stock($"focus-{dpi}", pixels);
                Require(pixels.Where((_, index) => index % 4 == 3).Any(value => value != 0), "The genuine focus drawing has no visible outline.");
            });

        Check("dashes-captured-focus-without-guidelines", () => Equal(
            render(new FocusVisual(guidelines: false), new DpiScale(1, 1), 192, 24),
            render(new FocusVisual(true, false), new DpiScale(1, 1), 192, 24), "The unguided focus contour has a different phase or seam."));
        foreach (bool vertical in new[] { false, true })
            foreach (double width in new[] { 1d, 2d })
                foreach (double offset in new[] { 0d, 0.5d, -0.5d })
                    Check($"dashes-units-and-phase-{vertical}-{width}-{offset}", () =>
                    {
                        byte[] actual = Line([1, 2], offset, width, PenLineCap.Flat, vertical);
                        Equal(actual, Manual(offset, width, PenLineCap.Flat, vertical), "Dash placement differs from explicit solid intervals.");
                        Equal(actual, Line([1, 2], offset + 6, width, PenLineCap.Flat, vertical), "Positive period wrap changed the raster.");
                        Equal(actual, Line([1, 2], offset - 6, width, PenLineCap.Flat, vertical), "Negative period wrap changed the raster.");
                    });

        foreach (PenLineCap cap in new[] { PenLineCap.Flat, PenLineCap.Round, PenLineCap.Square })
        {
            Check($"dashes-matching-cap-{cap}", () =>
            {
                byte[] actual = Line([1, 2], 0.5, 2, cap, false);
                byte[] expected = Manual(0.5, 2, cap, false);
                if (cap == PenLineCap.Round) EqualRoundCapEdge(actual, expected);
                else Equal(actual, expected, "Dash or outer endpoints differ from explicit matching-cap intervals.");
                Stock($"cap-{cap}", actual);
            });
            Check($"dashes-zero-on-{cap}", () =>
            {
#if !STOCK
                if (cap != PenLineCap.Flat)
                {
                    try { Line([0, 2], 0, 2, cap, false); }
                    catch (NotSupportedException error) when (error.Message.Contains("zero-length dash", StringComparison.Ordinal)) { return; }
                    throw new InvalidOperationException("The backend's missing endpoint dot was silently accepted.");
                }
#endif
                byte[] actual = Line([0, 2], 0, 2, cap, false);
                Stock($"zero-on-{cap}", actual);
                var manual = new DrawingVisual();
                using (var drawing = manual.RenderOpen())
                    for (int index = 0; index <= 15; index++)
                        drawing.DrawLine(Pen([], 0, 2, cap), new Point(8 + index * 4, 8), new Point(8 + index * 4, 8));
                Equal(actual, render(manual, new DpiScale(1, 1), 96, 80), "Zero-length dash caps differ from explicit point strokes.");
            });
            Check($"dashes-zero-gap-{cap}", () =>
            {
                byte[] actual = Line([2, 0], 0, 2, cap, false);
                Stock($"zero-gap-{cap}", actual);
                byte[] continuous = Line([], 0, 2, cap, false);
                if (cap == PenLineCap.Round) EqualRoundCapEdge(actual, continuous);
                else Equal(actual, continuous, "Zero gaps differ from the continuous stroke.");
            });
        }
        Check("dashes-negative-thickness", () => Equal(Line([1, 2], -0.5, -2, PenLineCap.Flat, false), Line([1, 2], -0.5, 2, PenLineCap.Flat, false), "Negative thickness changed dash units or phase."));
        Check("dashes-minimum-qualified-cycle", () => Require(Line([0.05, 0.05], 0, 1, PenLineCap.Flat, false).Any(value => value != 0), "The minimum qualified cycle did not paint."));
        Check("dashes-odd-negative-and-source-immutability", () =>
        {
            foreach (bool frozen in new[] { false, true })
            {
                var pen = Pen([-1, 2, -3], -0.5, 2, PenLineCap.Flat);
                var values = pen.DashStyle.Dashes.ToArray();
                if (frozen) pen.Freeze();
                var visual = Draw(pen);
                byte[] actual = render(visual, new DpiScale(1, 1), 96, 80);
                Equal(actual, Line([1, 2, 3, 1, 2, 3], -0.5, 2, PenLineCap.Flat, false), "Odd or negative dash normalization changed the pattern.");
                Require(pen.DashStyle.Dashes.SequenceEqual(values) && pen.DashStyle.Offset == -0.5 && pen.IsFrozen == frozen,
                    "Rendering mutated the original dash collection, phase or frozen state.");
            }
        });
        Check("dashes-contours-restart-phase", () =>
        {
            var geometry = new PathGeometry([
                new PathFigure(new Point(8, 8), [new LineSegment(new Point(63, 8), true)], false),
                new PathFigure(new Point(8, 18), [new LineSegment(new Point(57, 18), true)], false)]);
            var visual = new DrawingVisual();
            var pen = Pen([1, 2], 0.5, 2, PenLineCap.Flat);
            using (var drawing = visual.RenderOpen()) drawing.DrawGeometry(null, pen, geometry);
            var separate = new DrawingVisual();
            using (var drawing = separate.RenderOpen())
            {
                drawing.DrawLine(pen, new Point(8, 8), new Point(63, 8));
                drawing.DrawLine(pen, new Point(8, 18), new Point(57, 18));
            }
            Equal(render(visual, new DpiScale(1, 1), 96, 80), render(separate, new DpiScale(1, 1), 96, 80), "A new contour inherited the previous contour's phase.");
        });
#if !STOCK
        foreach (var entry in new (string Name, Action<Pen> Change)[] {
            ("all-zero", pen => pen.DashStyle = new DashStyle([0, 0], 0)),
            ("tiny-cycle", pen => pen.DashStyle = new DashStyle([0.01, 0.01], 0)),
            ("tiny-cycle-neighbor", pen => pen.DashStyle = new DashStyle([0.049, 0.05], 0)),
            ("nan-interval", pen => pen.DashStyle = new DashStyle([double.NaN, 2], 0)),
            ("infinite-interval", pen => pen.DashStyle = new DashStyle([double.PositiveInfinity, 2], 0)),
            ("overflow-interval", pen => pen.DashStyle = new DashStyle([double.MaxValue, 2], 0)),
            ("underflow-interval", pen => pen.DashStyle = new DashStyle([double.Epsilon, 2], 0)),
            ("overflow-cycle", pen => pen.DashStyle = new DashStyle([float.MaxValue * 0.75, float.MaxValue * 0.75], 0)),
            ("nan-phase", pen => pen.DashStyle.Offset = double.NaN),
            ("infinite-phase", pen => pen.DashStyle.Offset = double.PositiveInfinity),
            ("overflow-phase", pen => pen.DashStyle.Offset = double.MaxValue),
            ("infinite-width", pen => pen.Thickness = double.PositiveInfinity),
            ("nan-width", pen => pen.Thickness = double.NaN),
            ("underflow-width", pen => pen.Thickness = double.Epsilon),
            ("overflow-width", pen => pen.Thickness = double.MaxValue),
            ("triangle-cap", pen => pen.DashCap = PenLineCap.Triangle),
            ("mixed-dash-cap", pen => pen.DashCap = PenLineCap.Round),
            ("mixed-end-cap", pen => pen.EndLineCap = PenLineCap.Square) })
            Check($"dashes-explicit-{entry.Name}", () =>
            {
                var pen = Pen([1, 2], 0, 1, PenLineCap.Flat);
                entry.Change(pen);
                try { render(Draw(pen), new DpiScale(1, 1), 96, 80); }
                catch (NotSupportedException error) when (error.Message.Contains("dash", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("caps", StringComparison.Ordinal)) { return; }
                throw new InvalidOperationException("An unsupported dashed pen was silently accepted.");
            });
        Check("dashes-zero-width-is-not-hairline", () =>
            Require(Line([1, 2], 0, 0, PenLineCap.Flat, false).All(value => value == 0), "A zero-width dashed pen painted a hairline."));
#endif
        string? capture = args.FirstOrDefault(value => value.StartsWith("--capture-dashes=", StringComparison.Ordinal));
        if (capture != null)
        {
#if STOCK
            string path = typeof(Visual).Assembly.Location;
            File.WriteAllText(capture["--capture-dashes=".Length..], JsonSerializer.Serialize(new Reference(typeof(Visual).Assembly.FullName!, path,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), captured), new JsonSerializerOptions { WriteIndented = true }));
#else
            throw new InvalidOperationException("Only the stock profile can capture the stock dash reference.");
#endif
        }
        string? dump = args.FirstOrDefault(value => value.StartsWith("--dump-dashes=", StringComparison.Ordinal));
        if (dump != null) File.WriteAllText(dump["--dump-dashes=".Length..], JsonSerializer.Serialize(captured));
#if !STOCK
        if (dump != null)
        {
            using var path = new SkiaSharp.SKPath();
            path.AddRoundRect(new SkiaSharp.SKRect(0.5f, 0.5f, 177.5f, 13.46875f), 0, 0);
            File.WriteAllText(dump["--dump-dashes=".Length..] + ".path.json", JsonSerializer.Serialize(new { firstPoint = path.Points[0].ToString(), points = path.Points.Select(point => point.ToString()).ToArray() }));
        }
#endif
        return results;

        byte[] Line(double[] dashes, double offset, double width, PenLineCap cap, bool vertical)
            => render(Draw(Pen(dashes, offset, width, cap), vertical), new DpiScale(1, 1), 96, 80);
        byte[] Manual(double offset, double width, PenLineCap cap, bool vertical)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                for (int period = -3; period < 64; period++)
                {
                    double start = Math.Max(0, (3 * period - offset) * width);
                    double end = Math.Min(60, (3 * period + 1 - offset) * width);
                    if (end <= start) continue;
                    Point from = vertical ? new Point(8, 8 + start) : new Point(8 + start, 8);
                    Point to = vertical ? new Point(8, 8 + end) : new Point(8 + end, 8);
                    drawing.DrawGeometry(null, Pen([], 0, width, cap), new LineGeometry(from, to));
                }
            return render(visual, new DpiScale(1, 1), 96, 80);
        }
        void Stock(string key, byte[] pixels)
        {
            byte[] alpha = pixels.Where((_, index) => index % 4 == 3).ToArray();
            captured.Add(key, alpha);
#if !STOCK
            byte[] expected = reference.alpha[key];
            Require(alpha.Length == expected.Length, "Stock raster dimensions changed.");
            if (key.StartsWith("focus-", StringComparison.Ordinal))
            {
                double dpi = double.Parse(key["focus-".Length..], System.Globalization.CultureInfo.InvariantCulture);
                var metrics = DashedFocusOracle.Compare(alpha, expected, dpi);
                byte[] oldOrigin = render(new FocusVisual(true, wrongOrigin: true), new DpiScale(dpi, dpi), (int)(192 * dpi), (int)(24 * dpi)).Where((_, index) => index % 4 == 3).ToArray();
                var rejected = DashedFocusOracle.Compare(oldOrigin, expected, dpi);
                observation = new { actual = metrics, shiftedOriginControl = rejected };
                Require(metrics.strictContourSamples > 0 && metrics.strictMismatches == 0, "Focus body/phase pixels outside geometry-derived endpoint and AA regions differ from stock.");
                Require(rejected.strictMismatches > 0, "The fixed stock regions failed to reject the old shifted-origin pattern.");
            }
            else
            {
                int tolerance = key is "cap-Round" or "zero-gap-Round" ? 44 : 3;
                int maximum = 0;
                for (int index = 0; index < alpha.Length; index++)
                {
                    int difference = Math.Abs(alpha[index] - expected[index]);
                    maximum = Math.Max(maximum, difference);
                    if (difference > tolerance) throw new InvalidOperationException($"{key}: alpha at {index} was {alpha[index]}, stock was {expected[index]}.");
                }
                observation = new { stockMaximum = maximum, tolerance };
            }
#endif
        }
        void Check(string name, Action action)
        {
            observation = null;
            try { action(); results.Add(new(name, true, evidence: observation)); }
            catch (Exception error) { results.Add(new(name, false, error.ToString(), observation)); }
        }
    }

    private static Pen Pen(double[] dashes, double offset, double width, PenLineCap cap) => new(Brushes.Black, width)
        { DashStyle = new DashStyle(dashes, offset), StartLineCap = cap, EndLineCap = cap, DashCap = cap };
    private static DrawingVisual Draw(Pen pen, bool vertical = false)
    {
        var visual = new DrawingVisual();
        using var drawing = visual.RenderOpen();
        drawing.DrawLine(pen, new Point(8, 8), vertical ? new Point(8, 68) : new Point(68, 8));
        return visual;
    }
    private static void Equal(byte[] actual, byte[] expected, string message)
    {
        if (!actual.SequenceEqual(expected))
        {
            int index = Enumerable.Range(0, actual.Length).First(value => actual[value] != expected[value]);
            throw new InvalidOperationException($"{message} Byte {index}: {actual[index]} versus {expected[index]}.");
        }
    }
    private static void EqualRoundCapEdge(byte[] actual, byte[] expected)
    {
        // Skia's dashed outline and independent solid intervals differ by one alpha LSB at round edges.
        for (int index = 0; index < actual.Length; index++)
            if (Math.Abs(actual[index] - expected[index]) > 1)
                throw new InvalidOperationException($"Round-cap independent control exceeds one LSB at byte {index}.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FocusVisual : DrawingVisual
    {
        internal FocusVisual(bool explicitPath = false, bool guidelines = true, bool wrongOrigin = false)
        {
            if (guidelines) VisualXSnappingGuidelines = new DoubleCollection([0, 178]);
            if (guidelines) VisualYSnappingGuidelines = new DoubleCollection([0, 13.96875]);
            var pen = Pen([1, 2], 0, 1, PenLineCap.Flat);
            pen.Brush = new SolidColorBrush(Color.FromRgb(31, 35, 40));
            using var drawing = RenderOpen();
            if (explicitPath)
            {
                Point[] points = wrongOrigin ? [new(0.5, 13.46875), new(0.5, 0.5), new(177.5, 0.5), new(177.5, 13.46875)]
                    : [new(0.5, 0.5), new(177.5, 0.5), new(177.5, 13.46875), new(0.5, 13.46875)];
                drawing.DrawGeometry(null, pen, new PathGeometry([new PathFigure(points[0], points.Skip(1).Select(point => new LineSegment(point, true)), true)]));
            }
            else drawing.DrawRoundedRectangle(null, pen, new Rect(0.5, 0.5, 177, 12.96875), 0, 0);
        }
    }
}
