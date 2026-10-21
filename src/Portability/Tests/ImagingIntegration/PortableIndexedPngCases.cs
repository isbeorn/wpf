using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

internal static class PortableIndexedPngCases
{
    internal static void Run(Action<string, Action> check)
    {
        check("decoder-original-indexed-docking-guides-owned-alpha-pixels", () =>
        {
            foreach (var (name, width, height, hash, alpha) in new[]
            {
                ("DockPaneEmpty.png", 88, 88, "F53E1660D02132E57360FD001E46E1A834D7CCC243EFD7E4977029D4F305CECC", true),
                ("InnerDockDocPaneBottom.png", 23, 26, "6165F1784E59217E68F11CEE696ACDC8CF7D802503A3334B16A90521D560CCFD", false),
                ("InnerDockDocPaneLeft.png", 26, 23, "26621F933AA1D870BC7F10221022E6E9D05781E23688A4F9F337D0548DD2EB6A", false),
                ("InnerDockDocPaneRight.png", 26, 22, "AED6B1782E2896C0609537CA3981F68B415DB635F88C4A00DE685D494D411D34", false),
                ("InnerDockDocPaneTop.png", 23, 26, "461691B8C9854818AC2283D71B348CC199D2D12CFD9B725B645AE4268FE1FA57", false),
                ("InnerDockPaneBottom.png", 23, 26, "9BEB5442884AEB431ED06ED9D2DFC448638B2EAC375E01EA09750F8FE83EB6D0", false),
                ("InnerDockPaneInside.png", 25, 25, "7269EB22B5ED650B934E8EFEE6BD6351144FDDF76E51C679DBE6B1D3ABFBB200", false),
                ("InnerDockPaneLeft.png", 26, 23, "EA40BAFB42AE148AA7E10DBDF6A3B0B7F0923B6F1A4BE8499FA351FB2D4B508B", false),
                ("InnerDockPaneRight.png", 26, 22, "76DDA18BAA30AC290432DFB95904FEFF48E0B310C4091AE6FE05A489DB16DED1", false),
                ("InnerDockPaneTop.png", 23, 26, "D5DEC1017DE08036F75A5D212A61522DAFD682F7EE9D15D4F913C7D0CE200B84", false)
            })
            {
                using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", name));
                var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                stream.Dispose();
                Require(frame.PixelWidth == width && frame.PixelHeight == height
                    && frame.Format == (alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32)
                    && frame.Palette == null && frame.IsFrozen && Hash(frame) == hash,
                    "Original indexed guide changed expanded pixels, format or owned storage: " + name);
                Task.Run(() => Require(Hash(frame) == hash, "Frozen indexed frame changed on worker: " + name)).GetAwaiter().GetResult();
            }
        });
        check("decoder-indexed-palette-alpha-dpi-owned-pixels-and-render", () =>
        {
            byte[] encoded = Png(Palette, Transparency);
            using var stream = new MemoryStream(encoded);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Array.Clear(encoded); stream.Dispose();
            byte[] pixels = new byte[16]; frame.CopyPixels(pixels, 8, 0);
            Require(frame.Format == PixelFormats.Bgra32 && frame.Palette == null
                && frame.DpiX == (double)(float)(3780 * .0254) && frame.DpiY == (double)(float)(7560 * .0254)
                && pixels.SequenceEqual(new byte[] { 0, 0, 255, 255, 0, 255, 0, 128, 0, 0, 0, 0, 0, 0, 255, 255 }),
                "Indexed expansion lost alpha, raw colors, density or copied storage.");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 2, 2));
                drawing.DrawImage(frame, new Rect(0, 0, 2, 2));
            }
            using var target = new Target(visual, 2, 2); target.Render();
            using var rendered = target.Snapshot();
            Require(rendered.GetPixel(0, 0) == SKColors.Red && rendered.GetPixel(0, 1) == SKColors.White
                && rendered.GetPixel(1, 0).Green == 255 && Math.Abs(rendered.GetPixel(1, 0).Red - 127) <= 1,
                "Expanded indexed alpha failed the retained image renderer.");
            foreach (var (transparency, format) in new[]
            {
                ((byte[]?)null, PixelFormats.Bgr32),
                (new byte[] { 255, 255, 255 }, PixelFormats.Bgra32)
            })
            {
                using var opaque = new MemoryStream(Png(Palette, transparency));
                var decoded = new PngBitmapDecoder(opaque, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                byte[] expanded = new byte[16]; decoded.CopyPixels(expanded, 8, 0);
                Require(decoded.Format == format && decoded.PixelWidth == 2 && decoded.PixelHeight == 2
                    && expanded.SequenceEqual(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 255, 255 }),
                    "Opaque indexed PNG changed the existing codec format selection or exact expanded samples.");
            }
        });
        check("decoder-indexed-malformed-palette-transparency-and-recovery", () =>
        {
            foreach (byte[] invalid in new[]
            {
                Png(null, null), Png([], null), Png([1, 2], null), Png(new byte[771], null),
                Png(Palette, Transparency, duplicatePalette: true), Png(Palette, null, paletteAfterPixels: true),
                Png(null, Transparency), Png(Palette, []), Png(Palette, new byte[4]),
                Png(Palette, Transparency, duplicateTransparency: true), Png(Palette, Transparency, transparencyAfterPixels: true)
            })
            {
                using var input = new MemoryStream(invalid);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input;
                Expect<FileFormatException>(image.EndInit);
                Require(input.CanRead && image.PixelWidth == 0 && image.PixelHeight == 0, "Malformed indexed decode published partial pixels or closed caller storage.");
            }
            foreach (byte depth in new byte[] { 1, 2, 4 })
            {
                using var input = new MemoryStream(Png(Palette, Transparency, depth: depth));
                Expect<NotSupportedException>(() => new PngBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            }
            using var valid = new MemoryStream(Png(Palette, Transparency));
            Require(new PngBitmapDecoder(valid, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth == 2,
                "Malformed indexed input contaminated the next valid decode.");
        });
    }

    private static readonly byte[] Palette = [255, 0, 0, 0, 255, 0, 0, 0, 0];
    private static readonly byte[] Transparency = [255, 128, 0];
    private static byte[] Png(byte[]? palette, byte[]? transparency, bool duplicatePalette = false,
        bool paletteAfterPixels = false, bool duplicateTransparency = false, bool transparencyAfterPixels = false, byte depth = 8)
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 2); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 2); header[8] = depth; header[9] = 3;
        Chunk("IHDR", header);
        byte[] density = new byte[9]; BinaryPrimitives.WriteUInt32BigEndian(density, 3780); BinaryPrimitives.WriteUInt32BigEndian(density.AsSpan(4), 7560); density[8] = 1; Chunk("pHYs", density);
        if (palette != null && !paletteAfterPixels) Chunk("PLTE", palette);
        if (duplicatePalette && palette != null) Chunk("PLTE", palette);
        if (transparency != null && !transparencyAfterPixels) Chunk("tRNS", transparency);
        if (duplicateTransparency && transparency != null) Chunk("tRNS", transparency);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(new byte[] { 0, 0, 1, 0, 2, 0 });
        Chunk("IDAT", compressed.ToArray());
        if (paletteAfterPixels && palette != null) Chunk("PLTE", palette);
        if (transparencyAfterPixels && transparency != null) Chunk("tRNS", transparency);
        Chunk("IEND", []); return output.ToArray();

        void Chunk(string type, byte[] data)
        {
            Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length); output.Write(value);
            byte[] kind = System.Text.Encoding.ASCII.GetBytes(type); output.Write(kind); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte b in kind.Concat(data))
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(value, ~crc); output.Write(value);
        }
    }
    private static string Hash(BitmapSource frame)
    {
        byte[] pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4]; frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
