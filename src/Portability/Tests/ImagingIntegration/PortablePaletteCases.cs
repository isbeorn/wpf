using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PortablePaletteCases
{
    internal static void Run(Action<string, Action> check)
    {
        RunPositive(check);
        check("palette-unselected-presets-custom-quantization-and-indexed-remain-unsupported", () =>
        {
            foreach (Func<BitmapPalette> palette in new Func<BitmapPalette>[]
            {
                () => BitmapPalettes.Gray4, () => BitmapPalettes.Gray16,
                () => BitmapPalettes.Gray256Transparent, () => BitmapPalettes.Halftone8
            }) Expect<NotSupportedException>(() => _ = palette());
            Expect<NotSupportedException>(() => new BitmapPalette(new[] { Colors.Red }));
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 30 }, 1);
            Expect<NotSupportedException>(() => new BitmapPalette(source, 16));
            Expect<NotSupportedException>(() => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Indexed8, BitmapPalettes.Gray256, new byte[] { 30 }, 1));
        });
    }

    internal static void RunPositive(Action<string, Action> check)
    {
        check("palette-gray256-worker-first-access-exact-readonly-stable-cache", () =>
        {
            const int count = 8;
            var palettes = new BitmapPalette[count]; var errors = new Exception?[count]; var threads = new Thread[count];
            using var ready = new CountdownEvent(count); using var start = new ManualResetEventSlim();
            for (int i = 0; i < count; i++)
            {
                int index = i;
                threads[i] = new Thread(() =>
                {
                    ready.Signal(); start.Wait();
                    try { palettes[index] = BitmapPalettes.Gray256; }
                    catch (Exception exception) { errors[index] = exception; }
                });
                threads[i].Start();
            }
            ready.Wait(); start.Set(); foreach (Thread thread in threads) Require(thread.Join(TimeSpan.FromSeconds(10)), "Gray256 first access did not finish.");
            foreach (Exception? error in errors) if (error != null) throw new InvalidOperationException("Gray256 worker first access failed.", error);
            foreach (BitmapPalette palette in palettes)
            {
                var colors = palette.Colors;
                Require(colors.Count == 256 && colors.IsReadOnly && ReferenceEquals(colors, palette.Colors), "Gray256 colors lost their stable read-only collection.");
                for (int i = 0; i < colors.Count; i++) Require(colors[i] == Color.FromRgb((byte)i, (byte)i, (byte)i), "Gray256 altered an opaque ramp entry.");
                Expect<NotSupportedException>(() => colors[0] = Colors.Red);
                Expect<NotSupportedException>(() => colors.Add(Colors.Red));
                Expect<NotSupportedException>(() => colors.RemoveAt(0));
            }
            var cached = BitmapPalettes.Gray256;
            Require(ReferenceEquals(BitmapPalettes.Gray256, cached), "Gray256 cache identity changed after initialization.");
            Task.Run(() => Require(cached.Colors[30] == Color.FromRgb(30, 30, 30) && ReferenceEquals(BitmapPalettes.Gray256, cached), "Initialized Gray256 cache or colors changed on another worker.")).GetAwaiter().GetResult();
        });
        check("palette-nina-offline-gray8-worker-create-freeze-and-render", () =>
        {
            var image = Task.Run(() =>
            {
                byte[] samples = Enumerable.Repeat((byte)30, 15).ToArray();
                var source = BitmapSource.Create(5, 3, 96, 96, PixelFormats.Gray8, BitmapPalettes.Gray256, samples, 5);
                Array.Clear(samples); source.Freeze(); return source;
            }).GetAwaiter().GetResult();
            Require(image.Format == PixelFormats.Gray8 && image.PixelWidth == 5 && image.PixelHeight == 3 && image.DpiX == 96 && image.DpiY == 96 && image.IsFrozen && image.Palette == null, "The NINA Gray8 call changed format, metadata, freeze or non-indexed palette semantics.");
            byte[] expected = Enumerable.Repeat((byte)30, 15).ToArray();
            Require(Pixels(image).SequenceEqual(expected), "Gray8 creation retained caller data or changed raw samples.");
            RequireRendered(image, expected);
        });
        check("palette-gray8-ramp-padded-stride-clones-and-frozen-worker-read", () =>
        {
            const int width = 17, height = 16, stride = 20;
            byte[] expected = Enumerable.Range(0, width * height).Select(i => (byte)i).ToArray();
            byte[] samples = Enumerable.Repeat((byte)0xa5, stride * height).ToArray();
            for (int y = 0; y < height; y++) Array.Copy(expected, y * width, samples, y * stride, width);
            var source = BitmapSource.Create(width, height, 120, 144, PixelFormats.Gray8, BitmapPalettes.Gray256, samples, stride);
            Array.Fill(samples, (byte)0x5a); source.Freeze();
            foreach (var clone in new[] { source.Clone(), source.CloneCurrentValue(), (BitmapSource)source.GetAsFrozen(), (BitmapSource)source.GetCurrentValueAsFrozen() })
            {
                clone.Freeze(); Require(clone.Palette == null && clone.Format == PixelFormats.Gray8 && clone.DpiX == 120 && clone.DpiY == 144, "A Gray8 clone retained a palette or lost metadata.");
                Task.Run(() => Require(Pixels(clone).SequenceEqual(expected), "A frozen Gray8 clone changed ramp values, copied padding or retained caller storage.")).GetAwaiter().GetResult();
            }
            Require(Pixels(source).SequenceEqual(expected), "Gray8 clone operations changed original samples.");
            RequireRendered(source, expected);
        });
    }

    private static byte[] Pixels(BitmapSource source)
    {
        byte[] pixels = new byte[source.PixelWidth * source.PixelHeight]; source.CopyPixels(pixels, source.PixelWidth, 0); return pixels;
    }

    private static void RequireRendered(BitmapSource source, byte[] expected)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32); target.Render(visual);
        byte[] rendered = new byte[expected.Length * 4]; target.CopyPixels(rendered, source.PixelWidth * 4, 0);
        for (int i = 0; i < expected.Length; i++) Require(rendered[i * 4] == expected[i] && rendered[i * 4 + 1] == expected[i] && rendered[i * 4 + 2] == expected[i] && rendered[i * 4 + 3] == 255, "Gray8 rendering changed a ramp sample or channel.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
