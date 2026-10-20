using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class CombinedGeometryCases
{
    internal static void Clips(GeometryCombineMode mode)
    {
        var combined = new CombinedGeometry(mode,
            new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(4, 4) },
            new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(16, 4) })
            { Transform = new TranslateTransform(8, 6) };
        bool left = mode is GeometryCombineMode.Union or GeometryCombineMode.Xor or GeometryCombineMode.Exclude;
        bool overlap = mode is GeometryCombineMode.Union or GeometryCombineMode.Intersect;
        bool right = mode is GeometryCombineMode.Union or GeometryCombineMode.Xor;
        foreach (bool visualClip in new[] { false, true })
        {
            byte[] pixels = Capture(combined, visualClip);
            Pixel(pixels, 16, 18, left);
            Pixel(pixels, 28, 18, overlap);
            Pixel(pixels, 40, 18, right);
            Pixel(pixels, 4, 18, false);
            Pixel(pixels, 48, 18, false);
            Pixel(pixels, 28, 4, false);
            Pixel(pixels, 28, 34, false);
        }
    }

    internal static void Operands()
    {
        foreach (FillRule rule in Enum.GetValues<FillRule>())
        {
            var group = new GeometryGroup { FillRule = rule };
            group.Children.Add(new RectangleGeometry(new Rect(4, 4, 32, 28)));
            group.Children.Add(new RectangleGeometry(new Rect(12, 12, 16, 12)));
            var hollow = new PathGeometry([new PathFigure(new Point(40, 4),
                [new LineSegment(new Point(56, 4), true), new LineSegment(new Point(56, 32), true),
                 new LineSegment(new Point(40, 32), true)], true) { IsFilled = false }]);
            var nested = new CombinedGeometry(GeometryCombineMode.Union,
                new CombinedGeometry(GeometryCombineMode.Union, group, hollow), null);
            byte[] pixels = Capture(nested, false);
            Pixel(pixels, 8, 18, true);
            Pixel(pixels, 20, 18, rule == FillRule.Nonzero);
            Pixel(pixels, 48, 18, false);
            Pixel(pixels, 60, 18, false);
        }
        foreach (GeometryCombineMode mode in Enum.GetValues<GeometryCombineMode>())
            foreach (bool emptyFirst in new[] { false, true })
            {
                var rectangle = new RectangleGeometry(new Rect(4, 4, 20, 20));
                var combined = new CombinedGeometry(mode, emptyFirst ? null : rectangle, emptyFirst ? rectangle : null);
                byte[] pixels = Capture(combined, false);
                Pixel(pixels, 12, 12, mode is GeometryCombineMode.Union or GeometryCombineMode.Xor ||
                    mode == GeometryCombineMode.Exclude && !emptyFirst);
                Pixel(pixels, 32, 12, false);
            }
    }

    internal static void Retained(Action drain)
    {
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(4, 4, 32, 24)), new RectangleGeometry(new Rect(16, 4, 20, 24)));
        var root = new DrawingVisual { Clip = combined };
        using (DrawingContext drawing = root.RenderOpen())
            drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 64, 40));
        using var target = new Target(root, 64, 40);
        drain();
        byte[] original = Snapshot();
        foreach (GeometryCombineMode mode in new[] { GeometryCombineMode.Union, GeometryCombineMode.Exclude })
        {
            combined.GeometryCombineMode = mode;
            drain();
            byte[] actual = Snapshot();
            if ((mode == GeometryCombineMode.Exclude) != actual.SequenceEqual(original))
                throw new InvalidOperationException("Changing and restoring the retained combined clip did not change and restore its raster.");
        }
        root.Clip = null;
        drain();
        if (Snapshot().SequenceEqual(original)) throw new InvalidOperationException("Removing the combined clip left stale pixels.");
        root.Clip = combined;
        drain();
        if (!Snapshot().SequenceEqual(original)) throw new InvalidOperationException("Restoring the combined clip did not restore its raster.");
        byte[] Snapshot() { using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray(); }
    }

    internal static void Draw()
    {
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(4, 4, 32, 28)), new RectangleGeometry(new Rect(12, 12, 16, 12)))
            { Transform = new TranslateTransform(8, 4) };
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
            drawing.DrawGeometry(Brushes.Red, new Pen(Brushes.Blue, 4), combined);
        byte[] pixels = Render(visual);
        Pixel(pixels, 16, 22, true);
        Pixel(pixels, 28, 22, false);
        Pixel(pixels, 4, 22, false);
        foreach (int x in new[] { 12, 20 })
        {
            int index = (22 * 64 + x) * 4;
            if (!pixels.AsSpan(index, 4).SequenceEqual(new byte[] { 255, 0, 0, 255 }))
                throw new InvalidOperationException($"Combined geometry outer or hole stroke was missing at ({x},22).");
        }
    }

    private static byte[] Capture(Geometry clip, bool visualClip)
    {
        var visual = new DrawingVisual();
        if (visualClip) visual.Clip = clip;
        using (DrawingContext drawing = visual.RenderOpen())
        {
            if (!visualClip) drawing.PushClip(clip);
            drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 64, 40));
            if (!visualClip) drawing.Pop();
        }
        return Render(visual);
    }

    private static byte[] Render(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(64, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[64 * 40 * 4];
        bitmap.CopyPixels(pixels, 64 * 4, 0);
        return pixels;
    }

    private static void Pixel(byte[] pixels, int x, int y, bool filled)
    {
        int index = (y * 64 + x) * 4;
        if (pixels[index] != 0 || pixels[index + 1] != 0 || pixels[index + 2] != (filled ? 255 : 0) ||
            pixels[index + 3] != (filled ? 255 : 0))
            throw new InvalidOperationException($"Combined clip pixel ({x},{y}) was BGRA {string.Join(',', pixels.AsSpan(index, 4).ToArray())}, expected {(filled ? "red" : "transparent")}.");
    }
}
