using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class DrawingImageCases
{
    internal static void BoundsAndState()
    {
        var drawing = new DrawingGroup { Transform = new TranslateTransform(3, 5) };
        drawing.Children.Add(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(10, 20, 20, 10))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(30, 20, 20, 10))));
        var image = new DrawingImage(drawing);
        Rect bounds = drawing.Bounds;
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(4, 6, 40, 20));
            context.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 8, 8));
        }
        byte[] pixels = Render(visual);
        Pixel(pixels, 8, 10, Colors.Red);
        Pixel(pixels, 40, 20, Colors.Blue);
        Pixel(pixels, 2, 10, Colors.Transparent);
        Pixel(pixels, 46, 10, Colors.Transparent);
        Pixel(pixels, 20, 28, Colors.Transparent);
        Pixel(pixels, 52, 8, Colors.Lime);
        if (drawing.Bounds != bounds || drawing.Transform.Value.OffsetX != 3)
            throw new InvalidOperationException("DrawingImage rendering changed the source drawing.");
        image.Freeze();
        if (!Render(visual).SequenceEqual(pixels))
            throw new InvalidOperationException("Freezing the DrawingImage changed its pixels.");
    }

    internal static void NestedAndEmpty()
    {
        var source = new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(8, 12, 20, 10))));
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 10, 10)) };
        group.Children.Add(new ImageDrawing(source, new Rect(0, 0, 20, 10)));
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(4, 6));
            context.DrawImage(new DrawingImage(group), new Rect(0, 0, 20, 20));
            context.Pop();
            context.DrawImage(new DrawingImage(), new Rect(32, 0, 16, 16));
            context.DrawImage(source, new Rect(32, 20, 0, 10));
            context.DrawImage(source, new Rect(32, 20, 10, 0));
        }
        byte[] pixels = Render(visual);
        Pixel(pixels, 8, 10, Colors.Red);
        Pixel(pixels, 20, 20, Colors.Red);
        Pixel(pixels, 2, 10, Colors.Transparent);
        Pixel(pixels, 28, 10, Colors.Transparent);
        Pixel(pixels, 40, 8, Colors.Transparent);
        Pixel(pixels, 36, 24, Colors.Transparent);
    }

    internal static void Retained(Action drain)
    {
        var brush = new SolidColorBrush(Colors.Red);
        var drawing = new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(10, 20, 20, 10)));
        var image = new DrawingImage(drawing);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
            context.DrawImage(image, new Rect(4, 6, 40, 20));
        using var target = new Target(visual, 64, 40);
        drain();
        byte[] initial = Snapshot();
        brush.Color = Colors.Blue;
        drain();
        if (Snapshot().SequenceEqual(initial))
            throw new InvalidOperationException("Changing the DrawingImage's child brush left stale pixels.");
        image.Drawing = null;
        drain();
        if (Snapshot().Any(value => value != 0))
            throw new InvalidOperationException("Removing the DrawingImage drawing left stale pixels.");
        image.Drawing = drawing;
        brush.Color = Colors.Red;
        drain();
        if (!Snapshot().SequenceEqual(initial))
            throw new InvalidOperationException("Restoring the DrawingImage did not restore its pixels.");
        byte[] Snapshot() { using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray(); }
    }

    private static byte[] Render(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(64, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[64 * 40 * 4];
        bitmap.CopyPixels(pixels, 64 * 4, 0);
        return pixels;
    }

    private static void Pixel(byte[] pixels, int x, int y, Color color)
    {
        int index = (y * 64 + x) * 4;
        byte[] expected = color.A == 0 ? new byte[4] : new byte[] { color.B, color.G, color.R, color.A };
        if (!pixels.AsSpan(index, 4).SequenceEqual(expected))
            throw new InvalidOperationException($"DrawingImage pixel ({x},{y}) did not match {color}.");
    }
}
