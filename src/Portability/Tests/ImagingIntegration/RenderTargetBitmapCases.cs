using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class RenderTargetBitmapCases
{
    internal static void Run(Action<string, Action> check)
    {
        check("render-target-constructor-format-dpi-and-validation", () =>
        {
            var image = new RenderTargetBitmap(4, 3, 192, 144, PixelFormats.Default);
            Require(image.Format == PixelFormats.Pbgra32 && image.PixelWidth == 4 && image.PixelHeight == 3 && image.Width == 2 && image.Height == 2, "RTB metadata changed.");
            Require(Pixels(image).All(value => value == 0), "New RTB is not transparent.");
            foreach (double dpi in new[] { -1.0, 0, double.Epsilon, double.NegativeInfinity }) Require(new RenderTargetBitmap(1, 1, dpi, dpi, PixelFormats.Pbgra32).DpiX == 96, "Default DPI differs from stock.");
            Expect<ArgumentOutOfRangeException>(() => new RenderTargetBitmap(0, 1, 96, 96, PixelFormats.Default));
            Expect<ArgumentOutOfRangeException>(() => new RenderTargetBitmap(1, -1, 96, 96, PixelFormats.Pbgra32));
            foreach (PixelFormat format in new[] { PixelFormats.Bgra32, PixelFormats.Gray16, PixelFormats.Rgb48 }) Expect<ArgumentException>(() => new RenderTargetBitmap(1, 1, 96, 96, format));
            foreach (double dpi in new[] { double.NaN, double.PositiveInfinity })
            {
                var unusual = new RenderTargetBitmap(1, 1, dpi, dpi, PixelFormats.Default);
                Require(double.IsNaN(dpi) ? double.IsNaN(unusual.DpiX) : double.IsPositiveInfinity(unusual.DpiX), "Nonfinite constructor metadata changed.");
#if !STOCK
                Expect<NotSupportedException>(() => unusual.Render(Visual(Brushes.Red, new Rect(0, 0, 1, 1))));
#endif
            }
        });
        check("render-target-visual-alpha-accumulation-clear-and-dpi", () =>
        {
            var image = new RenderTargetBitmap(3, 2, 96, 96, PixelFormats.Pbgra32);
            var red = Visual(new SolidColorBrush(Color.FromArgb(128, 255, 0, 0)), new Rect(0, 0, 1, 1));
            image.Render(red); Require(Pixels(image).Take(4).SequenceEqual(new byte[] { 0, 0, 128, 128 }), "Premultiplied visual pixels changed.");
            image.Render(red); Require(Pixels(image).Take(4).SequenceEqual(new byte[] { 0, 0, 192, 192 }), "Repeated Render did not accumulate source-over.");
            image.Render(Visual(Brushes.Blue, new Rect(2, 1, 1, 1)));
            byte[] all = Pixels(image); Require(all.Take(4).SequenceEqual(new byte[] { 0, 0, 192, 192 }) && all.Skip(20).SequenceEqual(new byte[] { 255, 0, 0, 255 }), "Disjoint rendering erased content.");
            image.Clear(); Require(Pixels(image).All(value => value == 0), "Clear did not clear every pixel.");
            image.Render(red); Require(Pixels(image).Take(4).SequenceEqual(new byte[] { 0, 0, 128, 128 }), "Render after Clear failed.");
            var scaled = new RenderTargetBitmap(4, 2, 192, 192, PixelFormats.Pbgra32); scaled.Render(Visual(Brushes.Green, new Rect(0, 0, 1, 1)));
            byte[] pixels = Pixels(scaled); Require(pixels[3] == 255 && pixels[7] == 255 && pixels[11] == 0 && pixels[19] == 255 && pixels[23] == 255 && pixels[27] == 0, "DIP scaling at RTB DPI failed.");
        });
        check("render-target-source-clones-freeze-and-thread-ownership", () =>
        {
            byte[] sourceBytes = [0, 0, 255, 255];
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, sourceBytes, 4);
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawImage(source, new Rect(0, 0, 1, 1));
            var image = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Default); image.Render(visual); Array.Clear(sourceBytes);
            byte[] original = [0, 0, 255, 255]; Require(Pixels(source).SequenceEqual(original), "Render modified source/caller ownership.");
            foreach (var clone in new[] { (RenderTargetBitmap)image.Clone(), (RenderTargetBitmap)image.CloneCurrentValue() })
            {
                Require(!clone.IsFrozen && Pixels(clone).SequenceEqual(original), "Clone lost pixels or mutability."); clone.Clear(); Require(Pixels(image).SequenceEqual(original), "Clone shares mutable storage.");
                clone.Render(Visual(Brushes.Blue, new Rect(0, 0, 1, 1))); Require(Pixels(clone)[0] == 255, "Clone cannot Render.");
            }
            foreach (var frozen in new[] { (RenderTargetBitmap)image.GetAsFrozen(), (RenderTargetBitmap)image.GetCurrentValueAsFrozen() })
            {
                Require(frozen.IsFrozen && Pixels(frozen).SequenceEqual(original), "Frozen clone lost pixels."); Require(Task.Run(() => Pixels(frozen).SequenceEqual(original)).GetAwaiter().GetResult(), "Frozen worker read changed pixels.");
                Expect<ArgumentException>(() => frozen.Render(visual)); frozen.Clear(); Require(Pixels(frozen).All(value => value == 0) && Pixels(image).SequenceEqual(original), "Frozen Clear changed source ownership.");
            }
            Task.Run(image.Clear).GetAwaiter().GetResult(); Require(Pixels(image).All(value => value == 0), "Worker Clear did not mutate actual pixels."); image.Render(visual);
            Require(Task.Run(() => { try { _ = Pixels(image); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Mutable RTB lost read affinity.");
            image.Freeze(); Task.Run(() => Pixels(image)).GetAwaiter().GetResult(); Expect<ArgumentException>(() => image.Render(visual));
        });
        check("render-target-clear-updates-retained-image-consumer", () =>
        {
            foreach (bool freeze in new[] { false, true })
            {
                var source = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Default); source.Render(Visual(Brushes.Red, new Rect(0, 0, 1, 1)));
                var retained = new DrawingVisual(); using (var drawing = retained.RenderOpen()) drawing.DrawImage(source, new Rect(0, 0, 1, 1));
                var consumer = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Default); consumer.Render(retained); Require(Pixels(consumer)[2] == 255, "Initial retained RTB image was absent.");
                if (freeze) { source.Freeze(); source.Clear(); } else Task.Run(source.Clear).GetAwaiter().GetResult();
                Require(Pixels(source).All(value => value == 0), "Clear was a silent no-op."); consumer.Clear(); consumer.Render(retained);
                Require(Pixels(consumer).All(value => value == 0), "Retained consumer did not observe Clear pixels.");
            }
        });
        check("render-target-failed-render-preserves-pixels-and-recovers", () =>
        {
            var image = new RenderTargetBitmap(2, 1, 96, 96, PixelFormats.Default); var good = Visual(Brushes.Red, new Rect(0, 0, 2, 1)); image.Render(good); byte[] before = Pixels(image);
            Expect<ArgumentNullException>(() => image.Render(null!)); var other = Task.Run(() => Visual(Brushes.Blue, new Rect(0, 0, 2, 1))).GetAwaiter().GetResult(); Expect<InvalidOperationException>(() => image.Render(other));
#if !STOCK
            var partial = new ContainerVisual(); partial.Children.Add(Visual(Brushes.Blue, new Rect(0, 0, 1, 1))); var unsupported = Visual(Brushes.Blue, new Rect(1, 0, 1, 1)); unsupported.OpacityMask = Brushes.White; partial.Children.Add(unsupported); Expect<NotSupportedException>(() => image.Render(partial));
#endif
            Require(Pixels(image).SequenceEqual(before), "Failed Render published partial pixels."); image.Clear(); image.Render(Visual(Brushes.Blue, new Rect(0, 0, 2, 1))); Require(Pixels(image)[0] == 255, "Render recovery failed.");
        });
    }
    private static DrawingVisual Visual(Brush brush, Rect rectangle) { var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, rectangle); return visual; }
    private static byte[] Pixels(BitmapSource image) { byte[] result = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(result, image.PixelWidth * 4, 0); return result; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
