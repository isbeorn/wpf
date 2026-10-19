using System.Net.Cache;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

internal static class PortablePngCases
{
    private static readonly byte[] Expected = [0, 0, 255, 255, 0, 255, 0, 128, 0, 0, 0, 0, 56, 34, 12, 64];

    internal static void Run(Action<string, Action> check)
    {
        check("png-onload-copied-pixels-dpi-alpha-and-caller-stream", () =>
        {
            byte[] bytes = Asset("rgba8-dpi.png");
            using var stream = new MemoryStream(bytes);
            BitmapImage image = Load(stream);
            Require(stream.CanRead && ReferenceEquals(image.StreamSource, stream), "OnLoad closed or replaced the caller stream.");
            Array.Clear(bytes); stream.Dispose();
            Verify(image);
            Require(image.DpiX == (double)(float)(3780 * 0.0254) && image.DpiY == (double)(float)(7560 * 0.0254), "PNG pHYs DPI was discarded or changed.");
            Require(image.Width < 2 && image.Height < 1, "PNG dimensions ignored non-square DPI.");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 2, 2));
                drawing.DrawImage(image, new Rect(0, 0, 2, 2));
            }
            using var target = new Target(visual, 2, 2);
            target.Render();
            using SKBitmap pixels = target.Snapshot();
            Require(pixels.GetPixel(0, 0) == SKColors.Red && pixels.GetPixel(0, 1) == SKColors.White, "Decoded opaque or transparent pixels rendered incorrectly.");
            SKColor partial = pixels.GetPixel(1, 0);
            Require(Math.Abs(partial.Red - 127) <= 1 && partial.Green == 255 && Math.Abs(partial.Blue - 127) <= 1 && partial.Alpha == 255, "Decoded partial alpha did not composite over white.");
        });
        check("png-freeze-clone-current-value-and-thread-ownership", () =>
        {
            using var stream = new MemoryStream(Asset("rgba8-dpi.png"));
            BitmapImage image = Load(stream); stream.Dispose();
            Require(Task.Run(() => { try { _ = image.PixelWidth; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Mutable BitmapImage lost dispatcher affinity.");
            foreach (BitmapImage copy in new[] { image.Clone(), image.CloneCurrentValue(), (BitmapImage)image.GetAsFrozen(), (BitmapImage)image.GetCurrentValueAsFrozen() })
            {
                Verify(copy);
                Require(copy.DpiX == image.DpiX && copy.DpiY == image.DpiY, "Cloning changed PNG metadata.");
            }
            image.Freeze();
            Task.Run(() => Verify(image)).GetAwaiter().GetResult();
            BitmapImage mutable = image.Clone();
            Require(!mutable.IsFrozen && image.IsFrozen, "Cloning a frozen PNG lost independent mutable ownership.");
            Verify(mutable);
            Expect<InvalidOperationException>(image.BeginInit);
        });
        check("png-rgb-missing-dpi-and-initialization-contract", () =>
        {
            using var stream = new MemoryStream(Asset("rgb8.png"));
            var image = new BitmapImage();
            Expect<InvalidOperationException>(image.EndInit);
            image.BeginInit();
            Expect<InvalidOperationException>(image.BeginInit);
            image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
            Require(image.PixelWidth == 1 && image.PixelHeight == 1 && image.DpiX == 96 && image.DpiY == 96, "Unspecified PNG DPI did not use the default.");
            byte[] pixels = new byte[4]; image.CopyPixels(pixels, 4, 0);
            Require(pixels.SequenceEqual(new byte[] { 56, 34, 12, 255 }), "RGB PNG channel order or opaque alpha changed.");
            Expect<InvalidOperationException>(image.BeginInit);
        });
        check("png-malformed-and-truncated-loads-publish-no-pixels", () =>
        {
            byte[] encoded = Asset("rgba8-dpi.png");
            foreach (int length in new[] { 0, 1, 8, 16, 33, encoded.Length - 12, encoded.Length - 5 })
            {
                using var stream = new MemoryStream(encoded.Take(length).ToArray());
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
                Expect<FileFormatException>(image.EndInit);
                Require(stream.CanRead && image.PixelWidth == 0 && image.PixelHeight == 0, "Failed decode closed caller storage or published partial dimensions.");
            }
            using var valid = new MemoryStream(encoded);
            Verify(Load(valid));
        });
        check("png-explicit-unsupported-formats-options-and-uri-sources", () =>
        {
            foreach (string name in new[] { "rgba16.png", "animated.png" })
            {
                using var stream = new MemoryStream(Asset(name));
                Unsupported(() => Load(stream));
                Require(stream.CanRead, "Unsupported decode closed caller stream.");
            }
            using (var bitmap = new SKBitmap(1, 1))
            using (SKData jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100))
            using (var stream = new MemoryStream(jpeg.ToArray())) Unsupported(() => Load(stream));
            foreach (Action<BitmapImage> setting in new Action<BitmapImage>[]
            {
                image => image.DecodePixelWidth = 1,
                image => image.DecodePixelHeight = 1,
                image => image.SourceRect = new Int32Rect(0, 0, 1, 1),
                image => image.Rotation = Rotation.Rotate90,
                image => image.CreateOptions = BitmapCreateOptions.PreservePixelFormat,
                image => image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile,
                image => image.CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                image => image.CacheOption = BitmapCacheOption.None,
                image => image.UriCachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore)
            })
            {
                using var stream = new MemoryStream(Asset("rgba8-dpi.png"));
                Unsupported(() => Load(stream, setting));
                Require(stream.CanRead, "Unsupported option closed caller stream.");
            }
            using (var stream = new MemoryStream(Asset("rgba8-dpi.png")))
                Unsupported(() => { var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream; image.EndInit(); });
            _ = System.IO.Packaging.PackUriHelper.Create(new Uri("file:///unused-test-package.xps"));
            foreach (string uri in new[] { "https://example.invalid/image.png", "file:///unused-portable-image.png", "pack://siteoforigin:,,,/image.png" })
                Unsupported(() => new BitmapImage(new Uri(uri)));
            using var delayed = new MemoryStream(Asset("rgba8-dpi.png"));
            var pending = new BitmapImage(); pending.BeginInit(); pending.CacheOption = BitmapCacheOption.OnLoad; pending.StreamSource = delayed;
            pending.CreateOptions = BitmapCreateOptions.DelayCreation; pending.EndInit();
            Unsupported(() => _ = pending.PixelWidth);
        });
    }

    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name));
    private static BitmapImage Load(Stream stream, Action<BitmapImage>? setting = null)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        setting?.Invoke(image); image.EndInit(); return image;
    }
    private static void Verify(BitmapImage image)
    {
        Require(image.PixelWidth == 2 && image.PixelHeight == 2 && image.Format == PixelFormats.Bgra32 && !image.IsDownloading, "PNG source format, dimensions or load state changed.");
        byte[] pixels = new byte[16]; image.CopyPixels(pixels, 8, 0);
        Require(pixels.SequenceEqual(Expected), "PNG full CopyPixels lost channel values or alpha.");
    }
    private static void Unsupported(Action action)
    {
        try { action(); } catch (NotSupportedException exception) when (exception.Message.Contains("Portable BitmapImage", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected named portable BitmapImage rejection.");
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
