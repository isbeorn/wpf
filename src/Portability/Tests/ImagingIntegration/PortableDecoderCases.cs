using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

internal static class PortableDecoderCases
{
    private static readonly int[] IconWidths = [256, 128, 64, 48, 32, 16];
    private static readonly string[] IconHashes = ["497CC53712B2BA431361DD057EDD058232544142BA4F7F77B0F63DA0073E8306", "F77F5ED7F462E152D559A0EEE6133DA93956FA998077AEC5AC09A792EBFAD038", "2D2572D6385CADA31E31ECD2F3F1A425FD4AFBCF127667DC95BE8728AC2C2860", "1B64EDA05688FCE7EC6DEEA7671A6F5C5AC420F62FAA1C16F65CF52E1409423A", "B99E2539C32BD5C83D42F9BB6F6465FB43200BFAEE56F244A1B9B14E8740E012", "335B89EBD1D2A208A52048EF97FF596A9BE8CC46C27EA61233534EA4689C408C"];
    internal static void Run(Action<string, Action> check)
    {
        check("decoder-original-ico-all-directory-frames-thumbnails-and-owned-pixels", () =>
        {
            byte[] bytes = Asset("nina.ico");
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
            Require(decoder is IconBitmapDecoder && decoder.Frames.Count == 6 && ReferenceEquals(decoder.Frames, decoder.Frames), "ICO decoder identity, frame count or stable collection changed.");
            Require(stream.CanRead, "Caller stream was closed.");
            Array.Clear(bytes);
            stream.Dispose();
            for (int i = 0; i < 6; i++)
            {
                var frame = decoder.Frames[i];
                Require(frame.PixelWidth == IconWidths[i] && frame.PixelHeight == IconWidths[i] && frame.Format == PixelFormats.Bgra32 && frame.DpiX == 96 && frame.DpiY == 96 && frame.IsFrozen && ReferenceEquals(frame.Decoder, decoder), "ICO frame contract changed.");
                Require(Hash(frame) == IconHashes[i] && frame.Thumbnail != null && Hash(frame.Thumbnail) == IconHashes[i] && frame.Thumbnail.IsFrozen, "ICO pixels or actual thumbnail changed.");
            }
            Require(Task.Run(() => { try { _ = decoder.Frames; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Decoder lost dispatcher affinity.");
            var original = (BitmapFrame)decoder.Frames[0].Clone();
            foreach (var copy in new[] { (BitmapFrame)original.Clone(), (BitmapFrame)original.CloneCurrentValue(), (BitmapFrame)original.GetAsFrozen(), (BitmapFrame)original.GetCurrentValueAsFrozen() })
            {
                Require(Hash(copy) == IconHashes[0] && ReferenceEquals(copy.Decoder, decoder) && Hash(copy.Thumbnail) == IconHashes[0], "Frame cloning lost logical decoder, thumbnail or owned pixels.");
            }
            var frozen = decoder.Frames.ToArray();
            Task.Run(() => { foreach (var frame in frozen) Require(Hash(frame) == IconHashes[Array.IndexOf(frozen, frame)] && !frame.IsDownloading, "Frozen worker read changed pixels/load state."); }).GetAwaiter().GetResult();
            using var typedStream = new MemoryStream(Asset("nina.ico"));
            Require(new IconBitmapDecoder(typedStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Count == 6, "Concrete ICO constructor failed.");
            using var factoryStream = new MemoryStream(Asset("nina.ico"));
            var factory = BitmapFrame.Create(factoryStream);
            Require(factory.PixelWidth == 256 && ReferenceEquals(factory, factory.Decoder.Frames[0]), "BitmapFrame factory bypassed its decoder graph.");
        });
        check("decoder-original-rgb-png-factory-format-and-full-pixels", () =>
        {
            string[] names = ["dockleft.png", "dockright.png", "docktop.png", "dockbottom.png"];
            string[] hashes = ["1AC0AB44FB218B22BCE49C8511375752BC963A1D327F2573272B505F3BE533D2", "874D9B8C0235665775A9DCEA715A20C6D1D00D303850D6271CC0DEC0C26225A7", "4989DA2290C3CED91327208431ACA10BE03FEDEE4B2FC675ECF24CF4C9B1BEEA", "A72BA6F19972956C46AE5747C5CC0399460DE465701AC64204151A1724724C20"];
            for (int i = 0; i < 4; i++)
            {
                using var stream = new MemoryStream(Asset(names[i]));
                var d = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var f = d.Frames[0];
                Require(d.Frames.Count == 1 && f.Format == PixelFormats.Bgr32 && f.Thumbnail == null && f.DpiX == 96 && f.DpiY == 96 && Hash(f) == hashes[i], "Original RGB PNG factory altered format, pixels, DPI or thumbnail.");
            }
            using (var transparent = new MemoryStream(Asset("rgb-trns.png")))
            {
                var keyed = BitmapFrame.Create(transparent);
                Require(keyed.Format == PixelFormats.Bgra32 && Hash(keyed) == "4CEA40D175B9D80702860559B63F097A06FB0CCBD5B105C3E596BFF4B869C28C", "RGB tRNS factory discarded transparent color samples or alpha format.");
            }
            using var rgba = new MemoryStream(Asset("rgba8-dpi.png"));
            var frame = BitmapFrame.Create(rgba);
            Require(frame.Format == PixelFormats.Bgra32 && frame.PixelWidth == 2 && frame.PixelHeight == 2 && frame.DpiX == (double)(float)(3780 * .0254), "RGBA factory lost format or pHYs.");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 2, 2));
                dc.DrawImage(frame, new Rect(0, 0, 2, 2));
            }
            using var target = new Target(visual, 2, 2);
            target.Render();
            using SKBitmap pixels = target.Snapshot();
            Require(pixels.GetPixel(0, 0) == SKColors.Red && pixels.GetPixel(0, 1) == SKColors.White, "Actual retained factory frame failed to render.");
            Expect<NotSupportedException>(() => _ = frame.Metadata);
            Expect<NotSupportedException>(() => _ = frame.ColorContexts);
            Expect<NotSupportedException>(() => _ = frame.Decoder.CodecInfo);
            Expect<NotSupportedException>(() => frame.CreateInPlaceBitmapMetadataWriter());
        });
        check("decoder-malformed-later-ico-frame-options-and-recovery", () =>
        {
            byte[] png = Asset("rgba8-dpi.png");
            png[^1] ^= 1;
            using (var corrupt = new MemoryStream(png))
                Expect<FileFormatException>(() => BitmapFrame.Create(corrupt));
            byte[] ico = Asset("nina.ico");
            foreach (int n in new[] { 0, 1, 6, 22, ico.Length - 1 })
            {
                using var s = new MemoryStream(ico.Take(n).ToArray());
                Expect<FileFormatException>(() => BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
                Require(s.CanRead, "Failure closed caller stream.");
            }
            byte[] bad = (byte[])ico.Clone();
            bad[6 + 16 * 5 + 8] = 255;
            bad[6 + 16 * 5 + 9] = 255;
            bad[6 + 16 * 5 + 10] = 255;
            bad[6 + 16 * 5 + 11] = 127;
            using (var s = new MemoryStream(bad))
                Expect<FileFormatException>(() => new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            foreach (var option in new[] { BitmapCreateOptions.DelayCreation, BitmapCreateOptions.PreservePixelFormat, BitmapCreateOptions.IgnoreColorProfile, BitmapCreateOptions.IgnoreImageCache })
            {
                using var s = new MemoryStream(ico);
                Expect<NotSupportedException>(() => BitmapDecoder.Create(s, option, BitmapCacheOption.Default));
            }
            using (var s = new MemoryStream(ico))
                Expect<NotSupportedException>(() => BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.None));
            using (var s = new MemoryStream(ico))
                Expect<FileFormatException>(() => new PngBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.Default));
            using (var s = new MemoryStream(Asset("rgba16.png")))
                Expect<NotSupportedException>(() => BitmapFrame.Create(s));
            using (var s = new MemoryStream(Asset("animated.png")))
                Expect<NotSupportedException>(() => BitmapFrame.Create(s));
            using (var s = new MemoryStream(ico))
                Require(BitmapFrame.Create(s).Decoder.Frames.Count == 6, "Failure contaminated a subsequent decode.");
        });
    }
    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name));
    private static string Hash(BitmapSource source)
    {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        byte[] pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
