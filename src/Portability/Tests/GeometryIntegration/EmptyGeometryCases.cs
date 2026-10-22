using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class EmptyGeometryCases
{
    internal static void Layout(Stretch stretch) => OnDispatcher(() => LayoutOnDispatcher(stretch));

    private static void LayoutOnDispatcher(Stretch stretch)
    {
        foreach (bool nullData in new[] { true, false })
        {
            Geometry? source = nullData ? null : new StreamGeometry();
            var path = new System.Windows.Shapes.Path { Data = source, Width = 40, Margin = new Thickness(5),
                Stretch = stretch, Fill = Brushes.Red };
            path.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            path.Arrange(new Rect(0, 0, 80, 40));
            Geometry rendered = path.RenderedGeometry;
            Transform transform = rendered.Transform;
            long[] matrix = Bits(transform.Value);
            Require(rendered.IsEmpty(), "A null or empty Path acquired geometry during layout.");
            Require(path.DesiredSize == new Size(50, 10), "Empty Path layout changed its requested width or margin.");
            Require(path.RenderSize == (stretch == Stretch.None ? new Size(40, 30) : new Size(0, 0)),
                "Empty Path layout changed its native render size.");
            if (stretch != Stretch.None)
                Require(double.IsNaN(transform.Value.OffsetX) && double.IsNaN(transform.Value.OffsetY),
                    "The regression did not reach the native empty-bounds stretch transform.");
            var root = new ContainerVisual();
            root.Children.Add(path);
            root.Children.Add(Sibling());
            CheckPixels(Render(root));
            Require(ReferenceEquals(path.Data, source) && ReferenceEquals(rendered.Transform, transform) &&
                Bits(transform.Value).SequenceEqual(matrix), "Rendering changed the caller's geometry or transform.");
            if (source != null)
                Require(source.Transform.Value.IsIdentity, "Rendering changed the original empty stream's transform.");
        }
    }

    internal static void DrawingAndClip()
    {
        foreach (Geometry geometry in new Geometry[] { new StreamGeometry(), new PathGeometry(), new GeometryGroup() })
        {
            var transform = new MatrixTransform(1, 0, 0, 1, double.NaN, double.NaN);
            geometry.Transform = transform;
            long[] matrix = Bits(transform.Value);
            foreach (bool clip in new[] { false, true })
            {
                var visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen())
                {
                    if (clip)
                    {
                        context.PushClip(geometry);
                        context.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 32, 32));
                        context.Pop();
                    }
                    else context.DrawGeometry(Brushes.Red, new Pen(Brushes.Blue, 2), geometry);
                    DrawSibling(context);
                }
                CheckPixels(Render(visual));
            }
            var group = new GeometryGroup();
            group.Children.Add(geometry);
            group.Children.Add(new RectangleGeometry(new Rect(4, 4, 12, 12)));
            var grouped = new DrawingVisual();
            using (DrawingContext context = grouped.RenderOpen())
            {
                context.DrawGeometry(Brushes.Red, null, group);
                DrawSibling(context);
            }
            CheckPixels(Render(grouped), withRedChild: true);
            Require(ReferenceEquals(geometry.Transform, transform) && Bits(transform.Value).SequenceEqual(matrix),
                "Rendering an empty drawing or clip changed its transform.");
        }
    }

    internal static void NonemptyCommands()
    {
        var point = new LineGeometry(new Point(12, 12), new Point(12, 12));
        var rectangle = new RectangleGeometry(new Rect(24, 8, 0, 12));
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawGeometry(null, new Pen(Brushes.Red, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, point);
            context.DrawGeometry(null, new Pen(Brushes.Blue, 2), rectangle);
        }
        byte[] pixels = Render(visual);
        int center = (12 * 64 + 12) * 4;
        Require(pixels[center + 2] == 255 && pixels[center + 3] == 255, "A collapsed nonempty contour lost its stroke.");
        int edge = (12 * 64 + 24) * 4;
        Require(pixels[edge] == 255 && pixels[edge + 3] == 255, "A zero-width rectangle lost its stroke.");
#if PORTABLE_GEOMETRY_TESTS
        var move = new PathGeometry([new PathFigure { StartPoint = new Point(12, 12), IsFilled = true }]);
        foreach (Geometry geometry in new Geometry[] { point, rectangle, move })
        {
            geometry.Transform = new MatrixTransform(1, 0, 0, 1, double.NaN, double.NaN);
            var invalid = new DrawingVisual();
            using (DrawingContext context = invalid.RenderOpen()) context.DrawGeometry(Brushes.Red, null, geometry);
            try { Render(invalid); }
            catch (ArithmeticException) { continue; }
            throw new InvalidOperationException("A nonempty command path bypassed existing transform validation.");
        }
#endif
    }

    private static DrawingVisual Sibling()
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen()) DrawSibling(context);
        return visual;
    }

    private static void DrawSibling(DrawingContext context) =>
        context.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 12, 12));

    private static byte[] Render(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(64, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[64 * 40 * 4];
        bitmap.CopyPixels(pixels, 64 * 4, 0);
        return pixels;
    }

    private static void CheckPixels(byte[] pixels, bool withRedChild = false)
    {
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 64; x++)
            {
                bool lime = x >= 48 && x < 60 && y >= 4 && y < 16;
                bool red = withRedChild && x >= 4 && x < 16 && y >= 4 && y < 16;
                int index = (y * 64 + x) * 4;
                Require(pixels[index] == 0 && pixels[index + 1] == (lime ? 255 : 0) &&
                    pixels[index + 2] == (red ? 255 : 0) && pixels[index + 3] == (lime || red ? 255 : 0),
                    $"Empty geometry changed pixel ({x},{y}).");
            }
    }

    private static long[] Bits(Matrix matrix) => new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY }
        .Select(BitConverter.DoubleToInt64Bits).ToArray();

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
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Empty geometry layout exceeded its watchdog.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
