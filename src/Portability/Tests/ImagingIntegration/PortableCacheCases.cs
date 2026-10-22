using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PortableCacheCases
{
    internal static void Run(Action<string, Action> check)
    {
        check("cache-preserve-png-format-keeps-embedded-profile-conversion", () =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "linear-rgb-profile.png");
            foreach (var option in new[] { BitmapCreateOptions.None, BitmapCreateOptions.PreservePixelFormat })
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = option; image.UriSource = new Uri(path); image.EndInit();
                Require(image.Format == (option == BitmapCreateOptions.None ? PixelFormats.Bgra32 : PixelFormats.Bgr32), "Embedded profile changed the requested PNG storage format.");
                Require(Pixels(image).SequenceEqual(Convert.FromHexString("898989FFBCBCBCFFE1E1E1FF")), "PreservePixelFormat disabled the embedded linear RGB to sRGB conversion.");
            }
            using var input = File.OpenRead(path); var frame = new PngBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Require(Pixels(frame).SequenceEqual(Convert.FromHexString("404040FF808080FFC0C0C0FF")), "Raw decoder samples were incorrectly color converted.");
        });
        check("cache-local-jpeg-onload-preserve-sizing-and-released-file", () =>
        {
            foreach (string name in new[] { "cache-color-q70.jpg", "cache-gray-q70.jpg" })
            foreach (var axes in new[] { (0, 0, 5, 3), (2, 0, 2, 1), (0, 2, 3, 2), (2, 4, 2, 4), (1, 0, 1, 1) })
            {
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
                try
                {
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", name), path);
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat; image.UriSource = new Uri(path);
                    image.DecodePixelWidth = axes.Item1; image.DecodePixelHeight = axes.Item2; image.EndInit(); image.Freeze();
                    File.Delete(path);
                    Require(image.PixelWidth == axes.Item3 && image.PixelHeight == axes.Item4 && image.DpiX == 120 && image.DpiY == 144, "Cache dimensions, aspect floor, minimum size or DPI changed.");
                    Require(image.Format == (name.Contains("gray") ? PixelFormats.Gray8 : PixelFormats.Bgr24), "PreservePixelFormat lost the cache JPEG format.");
                    if (axes.Item1 == 0 && axes.Item2 == 0) RequireFixtureContent(image, name.Contains("gray"));
                    byte[] pixels = Pixels(image); Task.Run(() => Require(Pixels(image).SequenceEqual(pixels), "Frozen eager cache changed after file deletion.")).GetAwaiter().GetResult();
                }
                finally { File.Delete(path); }
            }
        });
        check("cache-jpeg-factory-typed-decoder-stream-ownership-and-failure-recovery", () =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "cache-color-q70.jpg"));
            using var stream = new MemoryStream(bytes); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Require(decoder is JpegBitmapDecoder && decoder.Frames.Count == 1 && decoder.Frames[0].Format == PixelFormats.Bgr32 && stream.CanRead, "Factory JPEG identity, format or stream ownership changed.");
            byte[] pixels = Pixels(decoder.Frames[0]); stream.Dispose(); Require(Pixels(decoder.Frames[0]).SequenceEqual(pixels), "JPEG frame borrowed caller storage.");
            RequireFixtureContent(decoder.Frames[0], gray: false);
            using var typed = new MemoryStream(bytes); Require(new JpegBitmapDecoder(typed, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].DpiY == 144, "Typed JPEG density changed.");
            using var mismatch = new MemoryStream(bytes); Expect<FileFormatException>(() => new PngBitmapDecoder(mismatch, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            using var corrupt = new MemoryStream(bytes[..30]); Expect<FileFormatException>(() => BitmapFrame.Create(corrupt)); Require(corrupt.CanRead, "Failed decode closed caller stream.");
        });
        check("cache-typed-and-factory-jpeg-file-preserve-onload-owned-pixels", () =>
        {
            foreach (string name in new[] { "cache-color-q70.jpg", "cache-gray-q70.jpg" })
            foreach (bool factory in new[] { false, true })
            foreach (var options in new[] { BitmapCreateOptions.None, BitmapCreateOptions.PreservePixelFormat })
            {
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
                try
                {
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", name), path); var uri = new Uri(path);
                    foreach (var invalid in new[] { BitmapCreateOptions.DelayCreation, BitmapCreateOptions.IgnoreColorProfile, BitmapCreateOptions.IgnoreImageCache })
                        Expect<NotSupportedException>(() => new JpegBitmapDecoder(uri, invalid, BitmapCacheOption.OnLoad));
                    Expect<NotSupportedException>(() => new JpegBitmapDecoder(uri, options, BitmapCacheOption.Default));
                    bool gray = name.Contains("gray");
                    var frame = Task.Run(() =>
                    {
                        BitmapDecoder decoder = factory ? BitmapDecoder.Create(uri, options, BitmapCacheOption.OnLoad) : new JpegBitmapDecoder(uri, options, BitmapCacheOption.OnLoad);
                        var loaded = decoder.Frames[0]; Require(loaded.Format == (gray ? PixelFormats.Gray8 : PixelFormats.Bgr32) && loaded.DpiX == 120 && loaded.DpiY == 144 && loaded.IsFrozen, "Typed/factory PreservePixelFormat changed stock JPEG format, density or eager ownership.");
                        RequireFixtureContent(loaded, gray); byte[] pixels = Pixels(loaded); File.Delete(path); Require(Pixels(loaded).SequenceEqual(pixels), "Typed/factory JPEG retained a file or lost owned samples."); return loaded;
                    }).GetAwaiter().GetResult();
                    var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawImage(frame, new Rect(0, 0, 5, 3));
                    var rendered = new RenderTargetBitmap(5, 3, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual); RequireFixtureContent(rendered, gray);
                }
                finally { File.Delete(path); }
            }
            foreach (string external in new[] { "https://example.invalid/cache.jpg", "file://unc-host/share/cache.jpg" })
                Expect<NotSupportedException>(() => new JpegBitmapDecoder(new Uri(external), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
            foreach (string png in new[] { "rgb8.png", "nina.ico" })
            {
                using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", png)); Expect<NotSupportedException>(() => BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
            }
        });
        check("cache-jpeg-original-and-thumbnail-preserve-external-color-and-luminance", () =>
        {
            foreach (var format in new[] { PixelFormats.Bgra32, PixelFormats.Rgb48, PixelFormats.Gray8, PixelFormats.Gray16 })
            {
                const int width = 96, height = 32; int bytes = format.BitsPerPixel / 8; byte[] samples = new byte[width * height * bytes]; bool gray = format == PixelFormats.Gray8 || format == PixelFormats.Gray16;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int stripe = x / 32, offset = (y * width + x) * bytes;
                    if (format == PixelFormats.Gray8) samples[offset] = new byte[] { 16, 128, 240 }[stripe];
                    else if (format == PixelFormats.Gray16) BinaryPrimitives.WriteUInt16LittleEndian(samples.AsSpan(offset), new ushort[] { 1, 32768, 65534 }[stripe]);
                    else if (format == PixelFormats.Rgb48) BinaryPrimitives.WriteUInt16LittleEndian(samples.AsSpan(offset + stripe * 2), 65535);
                    else { samples[offset + 2 - stripe] = 255; samples[offset + 3] = 255; }
                }
                var source = BitmapSource.Create(width, height, 120, 144, format, null, samples, width * bytes); source.Freeze();
                foreach (int size in new[] { 0, 75 })
                {
                    BitmapSource view = size == 0 ? source : new TransformedBitmap(source, new ScaleTransform((double)size / width, (double)size / height));
                    var encoder = new JpegBitmapEncoder { QualityLevel = 70 }; encoder.Frames.Add(BitmapFrame.Create(view)); using var output = new MemoryStream(); encoder.Save(output); using var input = new MemoryStream(output.ToArray()); var decoded = new JpegBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    byte[] pixels = Pixels(decoded); int pixelBytes = decoded.Format.BitsPerPixel / 8; int row = decoded.PixelHeight / 2;
                    byte[] centers = Enumerable.Range(0, 3).Select(stripe => pixels[(row * decoded.PixelWidth + (2 * stripe + 1) * decoded.PixelWidth / 6) * pixelBytes]).ToArray();
                    if (gray) Require(decoded.Format == PixelFormats.Gray8 && centers[0] < centers[1] && centers[1] < centers[2] && centers[0] < 128 && centers[2] > 128, "JPEG staging lost independently known dark/mid/light luminance bands.");
                    else for (int stripe = 0; stripe < 3; stripe++)
                    {
                        int offset = (row * decoded.PixelWidth + (2 * stripe + 1) * decoded.PixelWidth / 6) * pixelBytes, channel = 2 - stripe;
                        Require(pixels[offset + channel] > pixels[offset + (channel + 1) % 3] && pixels[offset + channel] > pixels[offset + (channel + 2) % 3], "JPEG staging lost or swapped the independently known red/green/blue patches.");
                    }
                }
                Require(Pixels(source).SequenceEqual(samples), "Content verification changed source samples.");
            }
        });
        check("cache-file-loading-preserves-xps-package-boundary", () =>
        {
            var context = typeof(BitmapImage).Assembly.GetType("MS.Internal.XpsLoadingContext", throwOnError: true)!;
            var property = context.GetProperty("ActivePackageUri", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            object? previous = property.GetValue(null);
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "cache-color-q70.jpg");
            try
            {
                property.SetValue(null, new Uri("file:///unused-cache-origin.xps"));
                var denied = new BitmapImage(); denied.BeginInit(); denied.CacheOption = BitmapCacheOption.OnLoad; denied.UriSource = new Uri(path);
                Expect<FileFormatException>(denied.EndInit); Require(denied.PixelWidth == 0, "XPS file denial published pixels.");
                Expect<FileFormatException>(() => new JpegBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
                Expect<FileFormatException>(() => BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
            }
            finally { property.SetValue(null, previous); }
            var allowed = new BitmapImage(); allowed.BeginInit(); allowed.CacheOption = BitmapCacheOption.OnLoad; allowed.UriSource = new Uri(path); allowed.EndInit(); Require(allowed.PixelWidth == 5, "XPS boundary rejection contaminated ordinary file loads.");
        });
        check("cache-high-bit-flip-exact-both-directions-and-source-preservation", () =>
        {
            foreach (var format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48 })
            {
                int channels = format == PixelFormats.Gray16 ? 1 : 3;
                ushort[] samples = Enumerable.Range(0, 6 * channels).Select(i => new ushort[] { 0, 1, 257, 32768, 65534, 65535 }[i % 6]).ToArray();
                byte[] expected = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, expected, 0, expected.Length);
                var source = BitmapSource.Create(3, 2, 120, 144, format, null, samples, 3 * channels * 2); source.Freeze();
                foreach (var axes in new[] { (-1, 1), (1, -1) })
                {
                    var flip = new TransformedBitmap(source, new ScaleTransform(axes.Item1, axes.Item2)); var restore = new TransformedBitmap(flip, new ScaleTransform(axes.Item1, axes.Item2));
                    byte[] reversed = new byte[expected.Length]; int bytes = channels * 2;
                    for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) Array.Copy(expected, (y * 3 + x) * bytes, reversed, ((axes.Item2 < 0 ? 1 - y : y) * 3 + (axes.Item1 < 0 ? 2 - x : x)) * bytes, bytes);
                    Require(flip.Format == format && flip.DpiX == 120 && flip.DpiY == 144 && Pixels(flip).SequenceEqual(reversed), "High-bit flip lost channel samples or metadata.");
                    Require(Pixels(restore).SequenceEqual(expected) && Pixels(source).SequenceEqual(expected), "Flip twice or source preservation failed.");
                }
            }
        });
        check("cache-gray16-resize-retains-linear-float-precision", () =>
        {
            foreach (ushort level in new ushort[] { 0, 1, 257, 32768, 65534, 65535 })
            {
                var source = BitmapSource.Create(5, 3, 120, 144, PixelFormats.Gray16, null, Enumerable.Repeat(level, 15).ToArray(), 10);
                var scaled = new TransformedBitmap(source, new ScaleTransform(.4, 2d / 3));
                Require(scaled.Format == PixelFormats.Gray32Float && scaled.DpiX == 120 && scaled.DpiY == 144, "Gray16 resize lost the stock linear floating format or DPI.");
                float[] values = new float[4]; scaled.CopyPixels(values, 8, 0);
                double encoded = level / 65535d;
                float expected = (float)(encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4));
                foreach (float value in values) Require(value == expected, "A constant Gray16 field lost its IEC sRGB linear float value.");
                var clone = scaled.Clone(); Require(Pixels(clone).SequenceEqual(Pixels(scaled)), "Floating scale clone changed samples.");
                Require(Pixels(source).SequenceEqual(Enumerable.Repeat(level, 15).SelectMany(BitConverter.GetBytes)), "Float scaling changed the scientific source.");
            }
        });
        check("cache-standard-area-linear-mixed-axes-and-premultiplied-alpha", () =>
        {
            byte[] checker = new byte[5 * 3 * 4];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 5; x++) { byte value = (byte)((x + y) % 2 * 255); for (int c = 0; c < 3; c++) checker[(y * 5 + x) * 4 + c] = value; checker[(y * 5 + x) * 4 + 3] = 255; }
            var source = BitmapSource.Create(5, 3, 96, 96, PixelFormats.Pbgra32, null, checker, 20);
            var rounded = new TransformedBitmap(source, new ScaleTransform(.5, .5)); Require(rounded.PixelWidth == 3 && rounded.PixelHeight == 2, "Half-pixel dimensions did not round upward.");
            var shrink = new TransformedBitmap(source, new ScaleTransform(.4, 2d / 3));
            Require(Pixels(shrink).SequenceEqual(Enumerable.Range(0, 16).Select(i => (byte)(i % 4 == 3 ? 255 : 119))), "Area shrink aliased a symmetric checker or changed its mean.");
            var mixed = new TransformedBitmap(source, new ScaleTransform(.4, 2)); byte[] mixedPixels = Pixels(mixed);
            byte[] rowValues = [102, 128, 153, 128, 102, 102];
            for (int y = 0; y < 6; y++) for (int x = 0; x < 2; x++) Require(mixedPixels[(y * 2 + x) * 4] == rowValues[y], "Mixed shrink/grow axes lost area coverage or left-aligned interpolation.");
            ushort[] high = Enumerable.Range(0, 15).Select(i => (ushort)(i % 2 * 65535)).ToArray();
            var highSource = BitmapSource.Create(5, 3, 96, 96, PixelFormats.Gray16, null, high, 10); var floatShrink = new TransformedBitmap(highSource, new ScaleTransform(.4, 2d / 3)); float[] values = new float[4]; floatShrink.CopyPixels(values, 8, 0);
            Require(values.All(value => value == 7f / 15), "Gray16 area filtering quantized the linear working samples.");
            byte[] alpha = [0, 0, 128, 128, 128, 0, 0, 128, 0, 0, 0, 0];
            var transparent = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Pbgra32, null, alpha, 12); var grown = new TransformedBitmap(transparent, new ScaleTransform(2, 1));
            Require(Pixels(grown).SequenceEqual(Convert.FromHexString("000080804000408080000080400000400000000000000000")), "Premultiplied interpolation bled RGB into transparent samples.");
        });
        check("cache-actual-quality70-original-and-three-square-thumbnails", () =>
        {
            foreach (var format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48, PixelFormats.Gray8, PixelFormats.Bgra32, PixelFormats.Pbgra32 })
            {
                int stride = 5 * format.BitsPerPixel / 8; byte[] samples = Enumerable.Range(0, stride * 3).Select(i => (byte)(i * 71 + 17)).ToArray();
                if (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32) for (int i = 3; i < samples.Length; i += 4) samples[i] = 255;
                var source = BitmapSource.Create(5, 3, 120, 144, format, null, samples, stride); source.Freeze();
                foreach (int size in new[] { 0, 500, 150, 75 })
                {
                    BitmapSource view = size == 0 ? source : new TransformedBitmap(source, new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
                    var encoder = new JpegBitmapEncoder { QualityLevel = 70 }; encoder.Frames.Add(BitmapFrame.Create(view)); using var output = new MemoryStream(); encoder.Save(output);
                    Require(output.CanWrite && output.Length > 32, "JPEG Save closed output or emitted no bytes."); byte[] jpeg = output.ToArray();
                    Require(jpeg[0] == 255 && jpeg[1] == 216 && Quantization(jpeg)[0] == 10, "JPEG signature or calibrated quality70 luma table changed.");
                    using var input = new MemoryStream(jpeg); var result = new JpegBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    Require(result.PixelWidth == (size == 0 ? 5 : size) && result.PixelHeight == (size == 0 ? 3 : size) && result.DpiX == 120 && result.DpiY == 144, "Original or square thumbnail dimensions/DPI changed.");
                    Expect<InvalidOperationException>(() => encoder.Save(output));
                }
                Require(source.Format == format && Pixels(source).SequenceEqual(samples), "Lossy display-cache output changed scientific source samples.");
            }
        });
        check("cache-jpeg-quality-boundaries-options-and-correction-before-output", () =>
        {
            var frame = BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 70, 80, 90, 255 }, 4));
            foreach (var quality in new[] { (1, 255), (70, 10), (75, 8), (100, 1) })
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality.Item1 }; encoder.Frames.Add(frame); using var stream = new MemoryStream(); encoder.Save(stream); Require(Quantization(stream.ToArray())[0] == quality.Item2, "Quality was ignored or mapped incorrectly.");
            }
            Expect<ArgumentOutOfRangeException>(() => new JpegBitmapEncoder { QualityLevel = 0 }); Expect<ArgumentOutOfRangeException>(() => new JpegBitmapEncoder { QualityLevel = 101 });
            foreach (var density in new[] { (96.123456, 144.654321, 96d, 145d), (0d, -1d, 96d, 96d) })
            {
                var source = BitmapSource.Create(1, 1, density.Item1, density.Item2, PixelFormats.Gray8, null, new byte[] { 127 }, 1); var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var output = new MemoryStream(); encoder.Save(output);
                using var input = new MemoryStream(output.ToArray()); var result = new JpegBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; Require(result.DpiX == density.Item3 && result.DpiY == density.Item4, "JFIF density did not round or apply nonpositive defaults.");
            }
            foreach (double dpi in new[] { double.NaN, .1, 65536 })
            {
                var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, dpi, 96, PixelFormats.Gray8, null, new byte[] { 127 }, 1))); using var output = new MemoryStream(); Expect<NotSupportedException>(() => encoder.Save(output)); Require(output.Length == 0, "Invalid JFIF density partially wrote output."); encoder.Frames[0] = frame; encoder.Save(output);
            }
            foreach (Action<JpegBitmapEncoder> option in new Action<JpegBitmapEncoder>[] { e => e.Rotation = Rotation.Rotate90, e => e.FlipHorizontal = true, e => e.FlipVertical = true })
            {
                var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(frame); option(encoder); using var stream = new MemoryStream(); Expect<NotSupportedException>(() => encoder.Save(stream)); Require(stream.Length == 0, "Unsupported option wrote bytes.");
                encoder.Rotation = Rotation.Rotate0; encoder.FlipHorizontal = false; encoder.FlipVertical = false; encoder.Save(stream); Require(stream.Length > 0, "Preflight rejection consumed encoder.");
            }
        });
    }
    private static byte[] Pixels(BitmapSource source) { int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8; byte[] pixels = new byte[stride * source.PixelHeight]; source.CopyPixels(pixels, stride, 0); return pixels; }
    private static void RequireFixtureContent(BitmapSource source, bool gray)
    {
        byte[] pixels = Pixels(source);
        if (gray)
        {
            int bytes = source.Format.BitsPerPixel / 8;
            Require(pixels[bytes] > pixels[0] && pixels[0] > pixels[3 * bytes] && pixels[bytes] > 128 && pixels[3 * bytes] < 128, "Stock gray JPEG lost its independently captured bright and dark positions.");
        }
        else
        {
            int bytes = source.Format.BitsPerPixel / 8, green = 4 * bytes;
            Require(pixels[2] > pixels[0] && pixels[2] > pixels[1] && pixels[green + 1] > pixels[green] && pixels[green + 1] > pixels[green + 2], "Stock color JPEG lost its independently captured red and green positions.");
        }
    }
    private static byte[] Quantization(byte[] jpeg)
    {
        for (int offset = 2; offset + 4 < jpeg.Length;)
        {
            Require(jpeg[offset] == 255, "Invalid JPEG marker."); byte marker = jpeg[offset + 1]; int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2));
            if (marker == 0xdb) return jpeg.AsSpan(offset + 5, length - 3).ToArray(); offset += length + 2;
        }
        throw new InvalidOperationException("Missing JPEG quantization table.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
