using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;

internal static class NativeCubicFillCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null);
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0, 112, 99));

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        var results = new List<Result>();
        Rect thumb = new(0, 0, 7, 786.06125);
        Matrix actual = new(1, 0, 0, 1, 1188, 40);
        foreach (string caller in new[] { "primitive", "geometry", "brush-primitive", "brush-geometry" })
            Check("cubic-fill-actual-thumb-" + caller, () => Compare(thumb, 5, 5, actual, new DpiScale(1, 1), caller, 1200, 850));
        Check("cubic-fill-captured-device-controls", () =>
        {
            byte[] expected = render(CapturedDevicePath(), new DpiScale(1, 1), 1200, 850);
            byte[] actualPixels = render(Visual(thumb, 5, 5, actual, "primitive"), new DpiScale(1, 1), 1200, 850);
            Equal(actualPixels, expected);
        });
        foreach (var radius in new[] { (0d, 0d), (0d, 3d), (3d, 0d), (-2d, -3d), (0.5, 2d), (3.5, 9.1875), (5d, 20d) })
            Check($"cubic-fill-radii-{radius.Item1}-{radius.Item2}", () => Compare(new Rect(0, 0, 7, 18.375), radius.Item1, radius.Item2,
                new Matrix(1, 0, 0, 1, 4.25, 3.25), new DpiScale(1, 1), "primitive"));
        Check("cubic-fill-transposed-clamp", () => Compare(new Rect(0, 0, 18.375, 7), 20, 5,
            new Matrix(1, 0, 0, 1, 4.25, 3.25), new DpiScale(1, 1), "geometry"));
        foreach (var sample in new[]
        {
            ("negative-translation", new Matrix(1, 0, 0, 1, -0.25, -0.25), new DpiScale(1, 1)),
            ("reflect-X", new Matrix(-1, 0, 0, 1, 28.25, 3.75), new DpiScale(1, 1)),
            ("reflect-Y", new Matrix(1, 0, 0, -1, 3.75, 28.25), new DpiScale(1, 1)),
            ("144DPI", new Matrix(1, 0, 0, 1, 2.25, 2.75), new DpiScale(1.5, 1.5)),
            ("192DPI", new Matrix(1, 0, 0, 1, 2.75, 2.25), new DpiScale(2, 2)),
            ("asymmetric-DPI", new Matrix(1, 0, 0, 1, 2.25, 2.75), new DpiScale(1.5, 1.25))
        }) Check("cubic-fill-" + sample.Item1, () => Compare(new Rect(1.25, 0.75, 7, 18.375), 5, 5, sample.Item2, sample.Item3, "geometry"));
        Check("cubic-fill-geometry-transform-once", () =>
        {
            var geometry = new RectangleGeometry(new Rect(0, 0, 7, 18.375), 5, 5, new MatrixTransform(1.5, 0, 0, 0.75, 3.25, 2.25));
            byte[] Paint(Geometry shape)
            {
                var visual = new GuidedVisual(new Rect(3.25, 2.25, 10.5, 13.78125), Matrix.Identity);
                using (var drawing = visual.RenderOpen()) drawing.DrawGeometry(Ink, null, shape);
                return render(visual, new DpiScale(1, 1), 64, 64);
            }
            Equal(Paint(geometry), Paint(PathGeometry.CreateFromGeometry(geometry)));
        });
        Check("cubic-fill-transparent", () =>
        {
            byte[] pixels = render(Visual(new Rect(0, 0, 7, 18.375), 5, 5, Matrix.Identity, "transparent"), new DpiScale(1, 1), 64, 64);
            if (pixels.Any(value => value != 0)) throw new InvalidOperationException("Transparent rounded fill painted pixels.");
        });
        Check("cubic-fill-clip-and-sibling-state", () =>
        {
            DrawingVisual Make(bool control)
            {
                var visual = new DrawingVisual();
                using var dc = visual.RenderOpen();
                dc.PushGuidelineSet(new GuidelineSet([0, 7], [0, 18.375]));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, 6, 16), 2, 2));
                if (control) dc.DrawGeometry(Ink, null, PathGeometry.CreateFromGeometry(new RectangleGeometry(new Rect(0, 0, 7, 18.375), 5, 5)));
                else dc.DrawRoundedRectangle(Ink, null, new Rect(0, 0, 7, 18.375), 5, 5);
                dc.Pop(); dc.Pop();
                dc.DrawRectangle(Brushes.Blue, null, new Rect(20.25, 2.25, 8, 8));
                return visual;
            }
            byte[] pixels = render(Make(false), new DpiScale(1, 1), 64, 64);
            Equal(pixels, render(Make(true), new DpiScale(1, 1), 64, 64));
            At(pixels, 64, 24, 5, Colors.Blue); At(pixels, 64, 3, 17, Colors.Transparent);
        });
#if !STOCK
        Check("cubic-fill-visual-brush-retained-recovery", VisualBrushCases.CheckRoundedFillRecovery);
#endif
        return results;

        void Check(string name, Action action)
        {
            try { action(); results.Add(new(name, true)); }
            catch (Exception error) { results.Add(new(name, false, error.ToString())); }
        }
        void Compare(Rect rectangle, double rx, double ry, Matrix matrix, DpiScale dpi, string caller, int width = 64, int height = 64)
        {
            DrawingVisual visual = Visual(rectangle, rx, ry, matrix, caller);
            Rect bounds = VisualTreeHelper.GetContentBounds(visual);
            byte[] pixels = render(visual, dpi, width, height);
            byte[] expected = render(Visual(rectangle, rx, ry, matrix, caller.StartsWith("brush-", StringComparison.Ordinal) ? "brush-converted" : "converted"), dpi, width, height);
            string? evidence = Environment.GetEnvironmentVariable("CUBIC_FILL_EVIDENCE");
            if (evidence != null)
            {
                Directory.CreateDirectory(evidence);
                string name = results.Count.ToString("D2") + "-" + caller;
                File.WriteAllBytes(Path.Combine(evidence, name + "-actual.bgra"), pixels);
                File.WriteAllBytes(Path.Combine(evidence, name + "-control.bgra"), expected);
            }
            Equal(pixels, expected);
            if (VisualTreeHelper.GetContentBounds(visual) != bounds) throw new InvalidOperationException("Guideline rendering changed logical bounds.");
            if (!pixels.Where((_, index) => index % 4 == 3).Any(value => value == 255)) throw new InvalidOperationException("Rounded fill lost its opaque interior.");
            if (caller is "primitive" or "geometry")
                for (int i = 0; i < pixels.Length; i += 4)
                    if (Math.Abs(255 * pixels[i] - 99 * pixels[i + 3]) > 255 || Math.Abs(255 * pixels[i + 1] - 112 * pixels[i + 3]) > 255 || pixels[i + 2] != 0)
                        throw new InvalidOperationException("Rounded fill lost its premultiplied source color.");
            if (width == 1200) { At(pixels, width, 1191, 600, Color.FromRgb(0, 112, 99)); At(pixels, width, 1187, 600, Colors.Transparent); }
        }
    }

    private static GuidedVisual Visual(Rect rectangle, double rx, double ry, Matrix matrix, string caller)
    {
        var visual = new GuidedVisual(rectangle, matrix);
        Brush brush = caller == "transparent" ? Brushes.Transparent : Ink;
        if (caller.StartsWith("brush-", StringComparison.Ordinal))
        {
            var source = new DrawingVisual();
            using (var dc = source.RenderOpen()) { dc.DrawRectangle(Ink, null, new Rect(0, 0, 8, 8)); dc.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 4, 4)); }
            brush = new VisualBrush(source);
        }
        using var drawing = visual.RenderOpen();
        var geometry = new RectangleGeometry(rectangle, rx, ry);
        if (caller is "primitive" or "brush-primitive" or "transparent") drawing.DrawRoundedRectangle(brush, null, rectangle, rx, ry);
        else drawing.DrawGeometry(brush, null, caller.EndsWith("converted", StringComparison.Ordinal) ? PathGeometry.CreateFromGeometry(geometry) : geometry);
        return visual;
    }

    private sealed class GuidedVisual : DrawingVisual
    {
        internal GuidedVisual(Rect rectangle, Matrix matrix)
        {
            Transform = new MatrixTransform(matrix);
            VisualXSnappingGuidelines = new DoubleCollection([rectangle.Left, rectangle.Right]);
            VisualYSnappingGuidelines = new DoubleCollection([rectangle.Top, rectangle.Bottom]);
        }
    }

    private static DrawingVisual CapturedDevicePath()
    {
        Point Device(double x, double y)
        {
            float dx = (float)x + 1188, dy = (float)y + 40;
            return new Point(dx, dy + (dy > 433.03064f ? -0.061279297f : 0));
        }
        var figure = new PathFigure { StartPoint = Device(0, 5), IsClosed = true, IsFilled = true };
        void Curve(double x1, double y1, double x2, double y2, double x3, double y3) => figure.Segments.Add(new BezierSegment(Device(x1, y1), Device(x2, y2), Device(x3, y3), true));
        Curve(0, 2.238576250846033, 1.5670033755922228, 0, 3.5, 0);
        figure.Segments.Add(new LineSegment(Device(3.5, 0), true));
        Curve(5.432996624407777, 0, 7, 2.238576250846033, 7, 5);
        figure.Segments.Add(new LineSegment(Device(7, 781.06125), true));
        Curve(7, 783.822673749154, 5.432996624407777, 786.06125, 3.5, 786.06125);
        figure.Segments.Add(new LineSegment(Device(3.5, 786.06125), true));
        Curve(1.5670033755922228, 786.06125, 0, 783.822673749154, 0, 781.06125);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawGeometry(Ink, null, new PathGeometry([figure]));
        return visual;
    }

    private static void At(byte[] pixels, int width, int x, int y, Color expected)
    {
        int index = (y * width + x) * 4;
        if (pixels[index] != (expected.B * expected.A + 127) / 255 || pixels[index + 1] != (expected.G * expected.A + 127) / 255 || pixels[index + 2] != (expected.R * expected.A + 127) / 255 || pixels[index + 3] != expected.A)
            throw new InvalidOperationException($"Unexpected rounded-fill pixel at {x},{y}.");
    }
    private static void Equal(byte[] actual, byte[] expected)
    {
        int differences = actual.Zip(expected).Count(pair => pair.First != pair.Second);
        if (differences != 0) throw new InvalidOperationException($"Rounded primitive differs from native managed cubic control in {differences} channels; actual {Convert.ToHexString(SHA256.HashData(actual))}, control {Convert.ToHexString(SHA256.HashData(expected))}.");
    }
}
