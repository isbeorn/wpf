#if !STOCK
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

internal static class VisualBrushCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null);
    private static readonly Type TargetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        var results = new List<Result>();
        void Check(string name, Action action)
        {
            try { action(); results.Add(new(name, true)); }
            catch (Exception exception) { results.Add(new(name, false, exception.ToString())); }
        }
        DrawingVisual Source() => Drawing(dc => dc.DrawRectangle(Brushes.Red, null, new Rect(3, 4, 8, 6)));
        byte[] Paint(VisualBrush brush, Action<DrawingContext, Brush>? fill = null, DpiScale? dpi = null) =>
            render(Drawing(dc => (fill ?? Rectangle)(dc, brush)), dpi ?? new DpiScale(1, 1), 32, 24);

        Check("visual-brush-detached-default-fill", () =>
        {
            byte[] pixels = Paint(new VisualBrush(Source()));
            ColorAt(pixels, 7, 5, Colors.Red); ColorAt(pixels, 20, 20, Colors.Transparent);
        });
        foreach (string shape in new[] { "rectangle", "rounded", "ellipse", "geometry" })
            Check("visual-brush-fill-" + shape, () =>
            {
                void Fill(DrawingContext dc, Brush brush)
                {
                    switch (shape)
                    {
                        case "rectangle": Rectangle(dc, brush); break;
                        case "rounded": dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, 16, 12), 3, 3); break;
                        case "ellipse": dc.DrawEllipse(brush, null, new Point(8, 6), 8, 6); break;
                        default: dc.DrawGeometry(brush, null, Geometry.Parse("M0,0 L16,0 8,12 Z")); break;
                    }
                }
                byte[] actual = Paint(new VisualBrush(Source()), Fill);
                byte[] coverage = render(Drawing(dc => Fill(dc, Brushes.White)), new DpiScale(1, 1), 32, 24);
                for (int i = 0; i < actual.Length; i += 4)
                {
                    if (actual[i] != 0 || actual[i + 1] != 0 || actual[i + 2] != actual[i + 3])
                        throw new InvalidOperationException($"{shape} fill lost premultiplied source color at byte {i}.");
                    if (shape != "ellipse")
                    {
                        if (actual[i + 3] != coverage[i + 3]) throw new InvalidOperationException($"{shape} coverage differed at byte {i}.");
                    }
                    else
                    {
                        // Clip and oval primitives use different edge coverage. Check every pixel against the analytic ellipse cell bounds.
                        double x = i / 4 % 32, y = i / 4 / 32;
                        double minX = x <= 8 && x + 1 >= 8 ? 0 : Math.Min(Math.Abs(x - 8), Math.Abs(x + 1 - 8)) / 8;
                        double minY = y <= 6 && y + 1 >= 6 ? 0 : Math.Min(Math.Abs(y - 6), Math.Abs(y + 1 - 6)) / 6;
                        double maxX = Math.Max(Math.Abs(x - 8), Math.Abs(x + 1 - 8)) / 8;
                        double maxY = Math.Max(Math.Abs(y - 6), Math.Abs(y + 1 - 6)) / 6;
                        if ((minX * minX + minY * minY >= 1 && actual[i + 3] != 0) ||
                            (maxX * maxX + maxY * maxY < 1 && actual[i + 3] != 255))
                            throw new InvalidOperationException($"Ellipse escaped its analytic cell bounds at ({x},{y}).");
                    }
                }
            });

        Check("visual-brush-viewbox-crop-relative-and-absolute", () =>
        {
            Visual source = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(3, 4, 4, 6)); dc.DrawRectangle(Brushes.Blue, null, new Rect(7, 4, 4, 6)); });
            var brush = new VisualBrush(source) { Viewbox = new Rect(0, 0, 0.5, 1) };
            byte[] left = Paint(brush); ColorAt(left, 2, 5, Colors.Red); ColorAt(left, 14, 5, Colors.Red);
            brush.ViewboxUnits = BrushMappingMode.Absolute; brush.Viewbox = new Rect(7, 4, 4, 6);
            byte[] right = Paint(brush); ColorAt(right, 2, 5, Colors.Blue); ColorAt(right, 14, 5, Colors.Blue);
        });
        Check("visual-brush-viewport-relative-and-absolute", () =>
        {
            var brush = new VisualBrush(Source()) { Viewport = new Rect(0.25, 0.25, 0.5, 0.5) };
            byte[] relative = Paint(brush); ColorAt(relative, 5, 4, Colors.Red); ColorAt(relative, 2, 5, Colors.Transparent); ColorAt(relative, 14, 5, Colors.Transparent);
            brush.ViewportUnits = BrushMappingMode.Absolute; brush.Viewport = new Rect(4, 3, 8, 6);
            Equal(relative, Paint(brush), "Equivalent relative/absolute viewports differed.");
        });
        foreach (Stretch stretch in Enum.GetValues<Stretch>())
            foreach (AlignmentX x in Enum.GetValues<AlignmentX>())
                foreach (AlignmentY y in Enum.GetValues<AlignmentY>())
                    Check($"visual-brush-stretch-{stretch}-{x}-{y}", () =>
                    {
                        var source = Drawing(dc => dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 8, 4)));
                        var brush = new VisualBrush(source) { Stretch = stretch, AlignmentX = x, AlignmentY = y };
                        byte[] pixels = Paint(brush);
                        double width = stretch switch { Stretch.None => 8, Stretch.Uniform => 16, Stretch.UniformToFill => 24, _ => 16 };
                        double height = stretch switch { Stretch.None => 4, Stretch.Uniform => 8, _ => 12 };
                        double left = (16 - width) * ((int)x / 2d), top = (12 - height) * ((int)y / 2d);
                        for (int py = 0; py < 24; py++) for (int px = 0; px < 32; px++)
                        {
                            bool inside = px + 0.5 >= Math.Max(0, left) && px + 0.5 < Math.Min(16, left + width) &&
                                py + 0.5 >= Math.Max(0, top) && py + 0.5 < Math.Min(12, top + height);
                            ColorAt(pixels, px, py, inside ? Colors.Red : Colors.Transparent);
                        }
                    });

        Check("visual-brush-root-transform-and-offset-bounds", () =>
        {
            DrawingVisual source = Source(); source.Transform = new ScaleTransform(2, 3); source.Offset = new Vector(7, -5);
            Equal(Paint(new VisualBrush(Source())), Paint(new VisualBrush(source)), "The root outer-space bounds and traversal disagreed.");
        });
        Check("visual-brush-transform-relative-before-absolute-and-reflection", () =>
        {
            var source = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 6)); dc.DrawRectangle(Brushes.Blue, null, new Rect(4, 0, 4, 6)); });
            var brush = new VisualBrush(source) { RelativeTransform = new ScaleTransform(-0.5, 1, 0.5, 0.5), Transform = new TranslateTransform(2, 0) };
            byte[] pixels = Paint(brush); ColorAt(pixels, 7, 5, Colors.Blue); ColorAt(pixels, 13, 5, Colors.Red);
            ColorAt(pixels, 3, 5, Colors.Transparent); ColorAt(pixels, 15, 5, Colors.Transparent);
        });
        Check("visual-brush-UniformToFill-pattern-alignment-both-axes", () =>
        {
            var horizontal = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4)); dc.DrawRectangle(Brushes.Blue, null, new Rect(4, 0, 4, 4)); });
            foreach (var pair in new[] { (AlignmentX.Left, 12), (AlignmentX.Center, 8), (AlignmentX.Right, 4) })
            {
                byte[] pixels = Paint(new VisualBrush(horizontal) { Stretch = Stretch.UniformToFill, AlignmentX = pair.Item1 });
                ColorAt(pixels, pair.Item2 - 1, 5, Colors.Red); ColorAt(pixels, pair.Item2 + 1, 5, Colors.Blue);
            }
            var vertical = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4)); dc.DrawRectangle(Brushes.Blue, null, new Rect(0, 4, 4, 4)); });
            foreach (var pair in new[] { (AlignmentY.Top, Colors.Red, Colors.Red), (AlignmentY.Center, Colors.Red, Colors.Blue), (AlignmentY.Bottom, Colors.Blue, Colors.Blue) })
            {
                byte[] pixels = Paint(new VisualBrush(vertical) { Stretch = Stretch.UniformToFill, AlignmentY = pair.Item1 });
                ColorAt(pixels, 7, 2, pair.Item2); ColorAt(pixels, 7, 10, pair.Item3);
            }
        });
        Check("visual-brush-root-rotation-preserves-source-orientation", () =>
        {
            var source = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4)); dc.DrawRectangle(Brushes.Blue, null, new Rect(4, 0, 4, 4)); });
            source.Transform = new RotateTransform(90); source.Offset = new Vector(5, 9);
            byte[] pixels = Paint(new VisualBrush(source)); ColorAt(pixels, 7, 2, Colors.Red); ColorAt(pixels, 7, 9, Colors.Blue);
        });
        Check("visual-brush-DPI-applied-once", () =>
        {
            byte[] pixels = Paint(new VisualBrush(Source()), dpi: new DpiScale(1.5, 1.25));
            ColorAt(pixels, 22, 13, Colors.Red); ColorAt(pixels, 25, 13, Colors.Transparent); ColorAt(pixels, 20, 16, Colors.Transparent);
        });
        Check("visual-brush-opacity-is-one-group", () =>
        {
            Visual source = Drawing(dc => { dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 8, 6)); dc.DrawRectangle(Brushes.Red, null, new Rect(2, 0, 6, 6)); });
            byte[] pixels = Paint(new VisualBrush(source) { Opacity = 0.5 });
            for (int x = 1; x < 15; x++) if (pixels[(5 * 32 + x) * 4 + 3] != 128)
                throw new InvalidOperationException("Brush opacity was applied per child rather than to the complete source group.");
        });
        Check("visual-brush-null-empty-zero-and-singular", () =>
        {
            foreach (var brush in new[] { new VisualBrush(), new VisualBrush(new DrawingVisual()), new VisualBrush(Source()) { Viewbox = new Rect(0, 0, 0, 1) }, new VisualBrush(Source()) { Transform = new ScaleTransform(0, 1) } })
                if (Paint(brush).Any(b => b != 0)) throw new InvalidOperationException("An empty brush painted pixels.");
        });
        Check("visual-brush-source-mutation-replacement-null-and-restore", CheckRetained);
        Check("visual-brush-shared-source-last-consumer-and-dispose", CheckShared);
        Check("visual-brush-failed-source-recovery-and-cycle-rejection", CheckRecovery);
        Check("visual-brush-guidelines-and-source-state-restore", () => CheckState(render));
        Check("visual-brush-dispose-after-dispatcher-shutdown", CheckShutdown);
        Check("visual-brush-persistent-target-preserves-original-auto-layout", () =>
        {
            var source = new SizedElement(); var brush = new VisualBrush(source);
            using var target = new Target(Drawing(dc => Rectangle(dc, brush)));
            target.Render();
            if (source.Measures != 0) throw new InvalidOperationException("Rendering performed source layout.");
            Pump();
            if (source.Measures != 1 || source.RenderSize != new Size(8, 6))
                throw new InvalidOperationException($"A persistent brush target suppressed original AutoLayoutContent: Measures={source.Measures}, RenderSize={source.RenderSize}.");
            ColorAt(target.Pixels!, 7, 5, Colors.Red);
        });
        Check("visual-brush-current-and-former-source-can-be-parented", () =>
        {
            var source = Source(); var brush = new VisualBrush(source);
            var root = Drawing(dc => Rectangle(dc, brush));
            using var target = new Target(root); target.Render(); Pump();
            var parent = new ContainerVisual(); parent.Children.Add(source); parent.Children.Remove(source);
            target.Change(() => brush.Visual = Source());
            parent.Children.Add(source); parent.Children.Remove(source);
            bool rejected = false;
            try { parent.Children.Add(root); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("A genuine composition-target root lost its parenting restriction.");
        });
        Check("visual-brush-managed-auto-layout-and-disabled-layout", () =>
        {
            var source = new SizedElement(); var brush = new VisualBrush(source);
            Paint(brush);
            if (source.Measures != 0) throw new InvalidOperationException("Rendering performed source layout.");
            Pump();
            if (source.Measures != 1 || source.RenderSize != new Size(8, 6)) throw new InvalidOperationException("Original VisualBrush auto-layout did not run.");
            ColorAt(Paint(brush), 7, 5, Colors.Red);
            var manual = new SizedElement(); var disabled = new VisualBrush { AutoLayoutContent = false, Visual = manual };
            Pump(); Paint(disabled);
            if (manual.Measures != 0) throw new InvalidOperationException("AutoLayoutContent=false performed layout.");
        });
        Check("visual-brush-explicit-unsupported-profiles", () =>
        {
            foreach (TileMode mode in new[] { TileMode.Tile, TileMode.FlipX, TileMode.FlipY, TileMode.FlipXY })
                Unsupported(() => Paint(new VisualBrush(Source()) { TileMode = mode }), "tile mode");
            Unsupported(() => render(Drawing(dc => dc.DrawLine(new Pen(new VisualBrush(Source()), 2), new Point(), new Point(10, 10))), new DpiScale(1, 1), 32, 24), "brush type");
            var face = new GlyphTypeface(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf")));
            var run = new GlyphRun(face, 0, false, 12, 1, [face.CharacterToGlyphMap['H']], new Point(0, 12), [10], null, null, null, null, null, null);
            Unsupported(() => render(Drawing(dc => dc.DrawGlyphRun(new VisualBrush(Source()), run)), new DpiScale(1, 1), 32, 24), "brush type");
            DrawingVisual masked = Source(); masked.OpacityMask = Brushes.White;
            Unsupported(() => Paint(new VisualBrush(masked)), "opacity mask");
            DrawingVisual effect = Source(); effect.Effect = new BlurEffect();
            Unsupported(() => Paint(new VisualBrush(effect)), "effects");
        });
        foreach (PixelFormat format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48 })
            Check("visual-brush-scientific-source-and-sampler-" + format, () => CheckScientific(format, render));
        return results;
    }

    private static void Rectangle(DrawingContext dc, Brush brush) => dc.DrawRectangle(brush, null, new Rect(0, 0, 16, 12));
    private static DrawingVisual Drawing(Action<DrawingContext> draw)
    { var visual = new DrawingVisual(); using (DrawingContext dc = visual.RenderOpen()) draw(dc); return visual; }
    private static void ColorAt(byte[] pixels, int x, int y, Color expected)
    {
        int i = (y * 32 + x) * 4;
        if (pixels[i] != (expected.B * expected.A + 127) / 255 || pixels[i + 1] != (expected.G * expected.A + 127) / 255 ||
            pixels[i + 2] != (expected.R * expected.A + 127) / 255 || pixels[i + 3] != expected.A)
            throw new InvalidOperationException($"Pixel ({x},{y}) was {pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}, expected {expected}.");
    }
    private static void Equal(byte[] first, byte[] second, string message)
    { if (!first.SequenceEqual(second)) throw new InvalidOperationException(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Unsupported(Action action, string detail)
    {
        try { action(); }
        catch (NotSupportedException exception) when (exception.Message.Contains(detail, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Unsupported rendering was accepted: " + detail);
    }
    private static WriteableBitmap Bitmap(byte value = 255)
    {
        var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null);
        Write(bitmap, value); return bitmap;
    }
    private static void Write(WriteableBitmap bitmap, byte value)
    {
        byte[] pixels = Enumerable.Range(0, 16).Select(i => (byte)(i % 4 == 3 ? 255 : i % 4 == 2 ? value : 0)).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);
    }
    private static DrawingVisual Image(BitmapSource bitmap) => Drawing(dc => dc.DrawImage(bitmap, new Rect(0, 0, 8, 6)));
    private static void CheckRetained()
    {
        WriteableBitmap bitmap = Bitmap(); DrawingVisual source = Image(bitmap);
        var brush = new VisualBrush(source);
        using var target = new Target(Drawing(dc => Rectangle(dc, brush)));
        target.Render(); Pump();
        target.Change(() => Write(bitmap, 64)); ColorAt(target.Pixels!, 7, 5, Color.FromRgb(64, 0, 0));
        target.Change(() => source.Opacity = 0.5); ColorAt(target.Pixels!, 7, 5, Color.FromArgb(128, 64, 0, 0));
        target.Change(() => source.Opacity = 1); ColorAt(target.Pixels!, 7, 5, Color.FromRgb(64, 0, 0));
        var other = Image(Bitmap(128));
        target.Change(() => brush.Visual = other); ColorAt(target.Pixels!, 7, 5, Color.FromRgb(128, 0, 0));
        int frames = target.Frames; Write(bitmap, 32); Pump();
        if (target.Frames != frames) throw new InvalidOperationException("An obsolete detached source still scheduled frames.");
        target.Change(() => brush.Visual = null); ColorAt(target.Pixels!, 7, 5, Colors.Transparent);
        target.Change(() => brush.Visual = source); ColorAt(target.Pixels!, 7, 5, Color.FromRgb(32, 0, 0));
    }
    private static void CheckShared()
    {
        WriteableBitmap bitmap = Bitmap(); DrawingVisual source = Image(bitmap);
        using var first = new Target(Drawing(dc => Rectangle(dc, new VisualBrush(source))));
        using var second = new Target(Drawing(dc => { Rectangle(dc, new VisualBrush(source)); dc.DrawRectangle(new VisualBrush(source), null, new Rect(16, 0, 16, 12)); }));
        first.Render(); second.Render(); Pump();
        first.Dispose();
        second.Change(() => Write(bitmap, 64)); ColorAt(second.Pixels!, 7, 5, Color.FromRgb(64, 0, 0)); ColorAt(second.Pixels!, 23, 5, Color.FromRgb(64, 0, 0));
        int count = second.Frames; second.Dispose(); Write(bitmap, 32); Pump();
        if (second.Frames != count) throw new InvalidOperationException("Disposed targets still rendered.");
    }
    private static void FailedDispatcherFrame(Action mutation, string detail)
    {
        int failures = 0;
        DispatcherUnhandledExceptionEventHandler handler = (_, args) =>
        {
            if (args.Exception is NotSupportedException exception && exception.Message.Contains(detail, StringComparison.Ordinal))
            { failures++; args.Handled = true; }
        };
        Dispatcher.CurrentDispatcher.UnhandledException += handler;
        try
        {
            mutation(); Pump();
            if (failures != 1) throw new InvalidOperationException($"Expected one drained dispatcher failure for {detail}, observed {failures}.");
            Pump();
            if (failures != 1) throw new InvalidOperationException("A failed frame left a queued render behind.");
        }
        finally { Dispatcher.CurrentDispatcher.UnhandledException -= handler; }
    }
    private static void CheckRecovery()
    {
        DrawingVisual source = Drawing(dc => dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 8, 6)));
        source.OpacityMask = Brushes.White;
        var brush = new VisualBrush(source);
        using (var target = new Target(Drawing(dc => Rectangle(dc, brush))))
        {
            Unsupported(target.Render, "opacity mask");
            FailedDispatcherFrame(() => { }, "opacity mask");
            if (target.Frames != 0) throw new InvalidOperationException("A failed frame reported success.");
            target.Change(() => source.OpacityMask = null); ColorAt(target.Pixels!, 7, 5, Colors.Red);
            var replacement = Drawing(dc => dc.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 8, 6)));
            replacement.OpacityMask = Brushes.White;
            FailedDispatcherFrame(() => brush.Visual = replacement, "opacity mask");
            // The formerly successful source must remain registered until the failed replacement can render.
            FailedDispatcherFrame(() => source.Opacity = 0.5, "opacity mask");
            target.Change(() => replacement.OpacityMask = null); ColorAt(target.Pixels!, 7, 5, Colors.Blue);
            int frames = target.Frames; source.Opacity = 1; Pump();
            if (target.Frames != frames) throw new InvalidOperationException("A successful replacement retained its obsolete source dependency.");
        }
        DrawingVisual host = Drawing(dc => Rectangle(dc, brush)); brush.Visual = host;
        using var cyclic = new Target(host);
        Unsupported(cyclic.Render, "recursive VisualBrush");
        FailedDispatcherFrame(() => { }, "recursive VisualBrush");
        cyclic.Change(() => brush.Visual = source);
        var otherBrush = new VisualBrush(host);
        var other = Drawing(dc => Rectangle(dc, otherBrush));
        FailedDispatcherFrame(() => brush.Visual = other, "recursive VisualBrush");
        cyclic.Change(() => brush.Visual = source);
    }
    private static void CheckScientific(PixelFormat format, Func<Visual, DpiScale, int, int, byte[]> render)
    {
        int stride = 64 * format.BitsPerPixel / 8;
        byte[] bytes = BitmapSamplingCorpus.CheckerBytes(64, 32, format, 1);
        var bitmap = new WriteableBitmap(64, 32, 96, 96, format, null);
        bitmap.WritePixels(new Int32Rect(0, 0, 64, 32), bytes, stride, 0);
        var source = new BitmapSamplingCases.ImageElement(dc => dc.DrawImage(bitmap, new Rect(0, 0, 64, 32)));
        source.Measure(new Size(64, 32)); source.Arrange(new Rect(0, 0, 64, 32));
        RenderOptions.SetBitmapScalingMode(source, BitmapScalingMode.HighQuality);
        var brush = new VisualBrush(source);
        using var target = new Target(Drawing(dc => dc.DrawRectangle(brush, null, new Rect(0, 0, 24, 12))));
        target.Render();
        var control = new BitmapSamplingCases.ImageElement(dc => dc.DrawImage(bitmap, new Rect(0, 0, 24, 12)));
        control.Measure(new Size(32, 24)); control.Arrange(new Rect(0, 0, 32, 24));
        RenderOptions.SetBitmapScalingMode(control, BitmapScalingMode.HighQuality);
        Equal(target.Pixels!, render(control, new DpiScale(1, 1), 32, 24), "VisualBrush introduced another image resampling stage.");
        Pump(); target.Change(() => RenderOptions.SetBitmapScalingMode(source, BitmapScalingMode.NearestNeighbor));
        RenderOptions.SetBitmapScalingMode(control, BitmapScalingMode.NearestNeighbor);
        Equal(target.Pixels!, render(control, new DpiScale(1, 1), 32, 24), "Source sampling mode did not flow through VisualBrush.");
        byte[] copied = new byte[bytes.Length]; bitmap.CopyPixels(copied, stride, 0); Equal(bytes, copied, "Rendering modified scientific source bytes.");
        target.Change(() => RenderOptions.SetBitmapScalingMode(source, BitmapScalingMode.HighQuality));
        RenderOptions.SetBitmapScalingMode(control, BitmapScalingMode.HighQuality);
        Equal(target.Pixels!, render(control, new DpiScale(1, 1), 32, 24), "Restoring HighQuality lost the source sampler.");
        byte[] reversed = bytes.Reverse().ToArray(); target.Change(() => bitmap.WritePixels(new Int32Rect(0, 0, 64, 32), reversed, stride, 0));
        bitmap.CopyPixels(copied, stride, 0); Equal(reversed, copied, "Retained rendering modified mutated scientific bytes.");
        Equal(target.Pixels!, render(control, new DpiScale(1, 1), 32, 24), "The mutated scientific display did not use the original sampler.");
    }
    private static void CheckState(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        byte[] data = BitmapSamplingCorpus.CheckerBytes(64, 32, PixelFormats.Pbgra32, 1);
        var bitmap = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Pbgra32, null, data, 256);
        var source = new BitmapSamplingCases.ImageElement(dc => dc.DrawImage(bitmap, new Rect(0, 0, 64, 32)));
        source.Measure(new Size(64, 32)); source.Arrange(new Rect(0, 0, 64, 32));
        RenderOptions.SetBitmapScalingMode(source, BitmapScalingMode.HighQuality);
        var visual = Drawing(dc =>
        {
            dc.PushGuidelineSet(new GuidelineSet([0.25, 24.25], [0.25, 12.25]));
            dc.DrawRectangle(new VisualBrush(source) { Opacity = 0.5 }, null, new Rect(0.25, 0.25, 24, 12));
            dc.Pop();
            dc.DrawImage(bitmap, new Rect(0, 12, 24, 12));
        });
        var control = Drawing(dc => dc.DrawImage(bitmap, new Rect(0, 12, 24, 12)));
        byte[] pixels = render(visual, new DpiScale(1, 1), 32, 24), expected = render(control, new DpiScale(1, 1), 32, 24);
        for (int y = 13; y < 24; y++) for (int x = 0; x < 32; x++) for (int c = 0; c < 4; c++)
            if (pixels[(y * 32 + x) * 4 + c] != expected[(y * 32 + x) * 4 + c])
                throw new InvalidOperationException("VisualBrush leaked sampling, opacity, guidelines or clip state into its following sibling.");
        if (pixels[(5 * 32 + 10) * 4 + 3] != 128 || pixels[(5 * 32 + 25) * 4 + 3] != 0)
            throw new InvalidOperationException("The guided brush lost its interior group alpha or escaped its destination clip.");
    }
    private static void CheckShutdown()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = Drawing(dc => dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 8, 6)));
                using var target = new Target(Drawing(dc => Rectangle(dc, new VisualBrush(source))));
                target.Render(); Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("VisualBrush shutdown fixture exceeded its watchdog.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class SizedElement : UIElement
    {
        internal int Measures { get; private set; }
        protected override Size MeasureCore(Size availableSize) { Measures++; return new Size(8, 6); }
        protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(Brushes.Red, null, new Rect(RenderSize));
    }
    private sealed class Target : IDisposable
    {
        private readonly object _target;
        private bool _disposed;
        internal int Frames { get; private set; }
        internal byte[]? Pixels { get; private set; }
        internal Target(Visual visual)
        {
            _target = Activator.CreateInstance(TargetType, BindingFlags.Instance | BindingFlags.NonPublic, null, [visual, 32, 24, new DpiScale(1, 1)], null)!;
            Action rendered = () =>
            {
                using var bitmap = (SKBitmap)Invoke("Snapshot")!;
                Pixels = bitmap.GetPixelSpan().ToArray(); Frames++;
            };
            TargetType.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(_target, [rendered]);
        }
        internal void Render() => Invoke("Render");
        internal void Change(Action action)
        {
            int before = Frames; action(); Pump();
            if (Frames <= before) throw new InvalidOperationException("A detached source change did not schedule a real frame.");
        }
        private object? Invoke(string name)
        {
            try { return TargetType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(_target, null); }
            catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
        }
        public void Dispose() { if (!_disposed) { ((IDisposable)_target).Dispose(); _disposed = true; } }
    }
}
#endif
