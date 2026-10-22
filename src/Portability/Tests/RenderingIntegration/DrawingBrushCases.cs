using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class DrawingBrushCases
{
    internal static void ClassicTile()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, Geometry.Parse("M 1 1 L 1 3 L 3 3 L 3 1 z")));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(160, 160, 160)), null, Geometry.Parse("M 0 0 L 0 2 L 2 2 L 2 0 z")));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewboxUnits = BrushMappingMode.Absolute,
            ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4), Viewport = new Rect(0, 0, 4, 4)
        };
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(brush, null, new Rect(4, 4, 36, 24));
            context.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 8, 8));
        }
        foreach (int scale in new[] { 1, 2 })
        {
            byte[] pixels = Render(visual, scale);
            for (int y = 4 * scale; y < 28 * scale; y++)
                for (int x = 4 * scale; x < 40 * scale; x++)
                    Pixel(pixels, x, y, x / scale % 4 < 2 && y / scale % 4 < 2 ? Color.FromRgb(160, 160, 160) :
                        x / scale % 4 >= 1 && x / scale % 4 < 3 && y / scale % 4 >= 1 && y / scale % 4 < 3 ? Colors.White : Colors.Transparent, scale);
            Pixel(pixels, 3 * scale, 8 * scale, Colors.Transparent, scale);
            Pixel(pixels, 40 * scale, 8 * scale, Colors.Transparent, scale);
            Pixel(pixels, 52 * scale, 8 * scale, Colors.Lime, scale);
        }
        byte[] initial = Render(visual);
        brush.Freeze();
        if (!Render(visual).SequenceEqual(initial)) throw new InvalidOperationException("Freezing the tiled DrawingBrush changed its pixels.");
    }

    internal static void UniformAndState()
    {
        var drawing = new DrawingGroup { Transform = new TranslateTransform(3, 5) };
        drawing.Children.Add(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(10, 20, 10, 10))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(20, 20, 10, 10))));
        var brush = new DrawingBrush(drawing) { Stretch = Stretch.Uniform, Transform = new TranslateTransform(2, 0), Opacity = 0.5 };
        Rect bounds = drawing.Bounds;
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushClip(new RectangleGeometry(new Rect(4, 4, 34, 28)));
            context.DrawRectangle(brush, null, new Rect(4, 4, 40, 28));
            context.Pop();
            context.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 8, 8));
            context.DrawRectangle(new DrawingBrush(), null, new Rect(48, 20, 8, 8));
        }
        byte[] pixels = Render(visual);
        Pixel(pixels, 8, 12, Color.FromArgb(128, 255, 0, 0));
        Pixel(pixels, 30, 24, Color.FromArgb(128, 0, 0, 255));
        Pixel(pixels, 5, 12, Colors.Transparent);
        Pixel(pixels, 12, 6, Colors.Transparent);
        Pixel(pixels, 12, 30, Colors.Transparent);
        Pixel(pixels, 39, 12, Colors.Transparent);
        Pixel(pixels, 52, 8, Colors.Lime);
        Pixel(pixels, 52, 24, Colors.Transparent);
        if (drawing.Bounds != bounds || drawing.Transform.Value.OffsetX != 3 || brush.Transform.Value.OffsetX != 2)
            throw new InvalidOperationException("DrawingBrush rendering changed its retained source or transform.");
    }

    internal static void TilePadding()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(10, 20, 2, 2))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(12, 20, 2, 2))));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.Uniform,
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(10, 20, 4, 2),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 8, 8),
            RelativeTransform = new TranslateTransform(0.1, 0), Transform = new TranslateTransform(2, 0)
        };
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(2, 0));
            context.PushGuidelineSet(new GuidelineSet(new double[] { 4, 44 }, new double[] { 4, 28 }));
            context.DrawRectangle(brush, null, new Rect(4, 4, 40, 24));
            context.Pop();
            context.Pop();
        }
        byte[] pixels = Render(visual);
        foreach (int y in new[] { 4, 12, 20 })
        {
            Pixel(pixels, 10, y, Colors.Red);
            Pixel(pixels, 14, y, Colors.Blue);
            Pixel(pixels, 18, y, Colors.Red);
        }
        foreach (int y in new[] { 6, 8, 14, 16, 22, 24 }) Pixel(pixels, 10, y, Colors.Transparent);
        Pixel(pixels, 5, 4, Colors.Transparent);
        Pixel(pixels, 46, 4, Colors.Transparent);
    }

    internal static void ShapesAndEmpty()
    {
        var brush = new DrawingBrush(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(10, 20, 8, 8))));
        var empty = new DrawingBrush(new DrawingGroup());
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawEllipse(brush, null, new Point(10, 10), 6, 6);
            context.DrawRoundedRectangle(brush, null, new Rect(20, 4, 12, 12), 3, 3);
            context.DrawGeometry(brush, null, new RectangleGeometry(new Rect(36, 4, 12, 12)));
            context.DrawRectangle(empty, null, new Rect(4, 24, 12, 12));
            context.DrawRectangle(new DrawingBrush(brush.Drawing) { Viewport = new Rect(0, 0, 0, 1) }, null, new Rect(20, 24, 12, 12));
            context.DrawRectangle(new DrawingBrush(brush.Drawing)
            {
                ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(-8, -4, 8, 8),
                Transform = new MatrixTransform(2, 0, 0, 2, 60, 28)
            }, null, new Rect(40, 16, 24, 24));
        }
        byte[] pixels = Render(visual);
        foreach (int x in new[] { 10, 26, 42 }) Pixel(pixels, x, 10, Colors.Red);
        Pixel(pixels, 4, 4, Colors.Transparent);
        Pixel(pixels, 20, 3, Colors.Transparent);
        Pixel(pixels, 10, 30, Colors.Transparent);
        Pixel(pixels, 26, 30, Colors.Transparent);
        Pixel(pixels, 46, 22, Colors.Red);
        Pixel(pixels, 42, 22, Colors.Transparent);
        Pixel(pixels, 62, 22, Colors.Transparent);
    }

    internal static void Unsupported()
    {
        var drawing = new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 8, 8)));
        foreach (TileMode mode in new[] { TileMode.FlipX, TileMode.FlipY, TileMode.FlipXY })
            Reject(context => context.DrawRectangle(new DrawingBrush(drawing) { TileMode = mode }, null, new Rect(0, 0, 16, 16)), "DrawingBrush tile mode");
        Reject(context => context.DrawRectangle(null, new Pen(new DrawingBrush(drawing), 2), new Rect(2, 2, 16, 16)), "brush type System.Windows.Media.DrawingBrush");
        var group = new DrawingGroup { OpacityMask = Brushes.Red };
        group.Children.Add(drawing);
        Reject(context => context.DrawRectangle(new DrawingBrush(group), null, new Rect(0, 0, 16, 16)), "opacity mask");
        static void Reject(Action<DrawingContext> draw, string diagnostic)
        {
            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen()) draw(context);
            try { Render(visual); }
            catch (NotSupportedException exception) when (exception.Message.Contains(diagnostic)) { return; }
            throw new InvalidOperationException($"DrawingBrush did not preserve its unsupported boundary: {diagnostic}.");
        }
    }

    internal static void Retained(Action drain)
    {
        var color = new SolidColorBrush(Colors.Red);
        var drawing = new GeometryDrawing(color, null, new RectangleGeometry(new Rect(10, 20, 20, 10)));
        var brush = new DrawingBrush(drawing);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(brush, null, new Rect(4, 6, 40, 20));
            context.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 8, 8));
        }
        using var target = new Target(visual, 64, 40);
        drain();
        byte[] initial = Snapshot();
        Pixel(initial, 8, 10, Colors.Red);
        Pixel(initial, 52, 8, Colors.Lime);
        color.Color = Colors.Blue;
        drain();
        byte[] changed = Snapshot();
        Pixel(changed, 8, 10, Colors.Blue);
        if (changed.SequenceEqual(initial)) throw new InvalidOperationException("DrawingBrush child mutation left stale pixels.");
        brush.Drawing = null;
        drain();
        byte[] removed = Snapshot();
        Pixel(removed, 8, 10, Colors.Transparent);
        Pixel(removed, 52, 8, Colors.Lime);
        brush.Drawing = drawing;
        color.Color = Colors.Red;
        drain();
        if (!Snapshot().SequenceEqual(initial)) throw new InvalidOperationException("Restoring DrawingBrush content did not restore its pixels.");
        byte[] Snapshot() { using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray(); }
    }

    private static byte[] Render(Visual visual, int scale = 1)
    {
        var bitmap = new RenderTargetBitmap(64 * scale, 40 * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[64 * 40 * 4 * scale * scale];
        bitmap.CopyPixels(pixels, 64 * scale * 4, 0);
        return pixels;
    }

    private static void Pixel(byte[] pixels, int x, int y, Color color, int scale = 1)
    {
        int index = (y * 64 * scale + x) * 4;
        byte[] expected = { (byte)((color.B * color.A + 127) / 255), (byte)((color.G * color.A + 127) / 255), (byte)((color.R * color.A + 127) / 255), color.A };
        if (!pixels.AsSpan(index, 4).SequenceEqual(expected))
            throw new InvalidOperationException($"DrawingBrush pixel ({x},{y}) was {Convert.ToHexString(pixels.AsSpan(index, 4))}, expected {color}.");
    }
}
