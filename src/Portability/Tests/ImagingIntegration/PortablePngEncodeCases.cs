using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PortablePngEncodeCases
{
    private static readonly byte[] Red = [0, 0, 255, 255];
    private static readonly byte[] Blue = [255, 0, 0, 255];

    internal static void Run(Action<string, Action> check)
    {
        check("encode-frame-genuine-source-view-and-validation", () =>
        {
            var source = Source([1, 2, 3, 4], PixelFormats.Bgra32, 120, 144);
            var frame = BitmapFrame.Create(source);
            Require(frame.Decoder == null && frame.BaseUri == null && frame.Thumbnail == null && frame.Metadata == null && frame.ColorContexts == null, "Source-backed frame identity changed.");
            Require(frame.Format == source.Format && frame.PixelWidth == 1 && frame.PixelHeight == 1 && frame.DpiX == source.DpiX && frame.DpiY == source.DpiY && Pixels(frame).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Source-backed frame changed source properties or samples.");
            Expect<ArgumentNullException>(() => BitmapFrame.Create((BitmapSource)null!));
            Expect<InvalidOperationException>(() => BitmapFrame.Create(new BitmapImage()));
            Require(Task.Run(() => Throws<InvalidOperationException>(() => BitmapFrame.Create(source))).GetAwaiter().GetResult(), "Frame construction ignored source thread affinity.");
            Expect<NotSupportedException>(() => BitmapFrame.Create(source, source));
            Expect<NotSupportedException>(() => BitmapFrame.Create(source, null, null, new ReadOnlyCollection<ColorContext>([])));
        });
        check("encode-frame-all-clones-follow-write-restore-and-frozen-worker-handoff", () =>
        {
            var source = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
            source.WritePixels(new Int32Rect(0, 0, 1, 1), Red, 4, 0);
            var frame = BitmapFrame.Create(source); var views = Views(frame);
            frame.Freeze(); Require(!source.IsFrozen, "Frame Freeze froze its source.");
            foreach (var color in new[] { Blue, Red })
            {
                source.WritePixels(new Int32Rect(0, 0, 1, 1), color, 4, 0);
                foreach (var view in views) Require(Pixels(view).SequenceEqual(color), "A frame or clone stopped viewing source writes/restores.");
                var frozen = views.Where(view => view.IsFrozen).ToArray();
                Task.Run(() => { foreach (var view in frozen) Require(Pixels(view).SequenceEqual(color), "Frozen worker frame read touched mutable source properties or stale pixels."); }).GetAwaiter().GetResult();
            }
            Require(Task.Run(() => Throws<InvalidOperationException>(() => Pixels(views[1]))).GetAwaiter().GetResult(), "Mutable frame clone lost thread affinity.");
            var independent = source.Clone(); independent.WritePixels(new Int32Rect(0, 0, 1, 1), Blue, 4, 0);
            Require(Pixels(frame).SequenceEqual(Red), "The view-sharing rule leaked to ordinary WriteableBitmap clones.");
        });
        check("encode-frame-render-clear-render-and-separately-frozen-sources", () =>
        {
            var source = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            var frame = BitmapFrame.Create(source); var views = Views(frame); frame.Freeze();
            source.Render(Visual(Brushes.Red)); AssertViews(views, Red);
            source.Clear(); AssertViews(views, new byte[4]);
            source.Render(Visual(Brushes.Blue)); AssertViews(views, Blue);
            source.Freeze(); source.Clear(); AssertViews(views, new byte[4]);
            Expect<ArgumentException>(() => source.Render(Visual(Brushes.Red))); AssertViews(views, new byte[4]);
            var mutable = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
            mutable.WritePixels(new Int32Rect(0, 0, 1, 1), Red, 4, 0);
            mutable.Lock(); IntPtr stale = mutable.BackBuffer; mutable.Unlock(); mutable.Freeze();
            var frozenViews = Views(BitmapFrame.Create(mutable));
            Marshal.WriteInt32(stale, unchecked((int)0xff0000ff));
            AssertViews(frozenViews, Red);
            Expect<InvalidOperationException>(() => mutable.WritePixels(new Int32Rect(0, 0, 1, 1), Blue, 4, 0));
        });
        check("encode-png-live-write-restore-and-render-clear-render", () =>
        {
            var writeable = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
            var frame = BitmapFrame.Create(writeable);
            var attachedBeforeWrite = Encoder(frame);
            writeable.WritePixels(new Int32Rect(0, 0, 1, 1), Red, 4, 0);
            AssertSaved(attachedBeforeWrite, Red);
            writeable.WritePixels(new Int32Rect(0, 0, 1, 1), Blue, 4, 0);
            AssertSaved(Encoder(frame), Blue);
            writeable.WritePixels(new Int32Rect(0, 0, 1, 1), Red, 4, 0);
            AssertSaved(Encoder(frame), Red);

            var rendered = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            var renderedFrame = BitmapFrame.Create(rendered);
            var attachedBeforeRender = Encoder(renderedFrame);
            rendered.Render(Visual(Brushes.Red)); AssertSaved(attachedBeforeRender, Red);
            rendered.Clear(); AssertSaved(Encoder(renderedFrame), new byte[4]);
            rendered.Render(Visual(Brushes.Red)); AssertSaved(Encoder(renderedFrame), Red);
        });
        check("encode-frame-strong-source-lifetime-and-exact-scientific-clones", () =>
        {
            var (frame, weak) = RetainedFrame(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Require(weak.IsAlive && Pixels(frame).SequenceEqual(Red), "Frame did not retain its source.");
            foreach (var format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48 })
            {
                ushort[] data = format == PixelFormats.Gray16 ? [0, 1, 255, 256, 32768, 65534, 65535] : [1, 256, 65535, 65534, 32768, 257];
                int width = data.Length / (format == PixelFormats.Gray16 ? 1 : 3);
                var source = new WriteableBitmap(width, 1, 120, 144, format, null);
                source.WritePixels(new Int32Rect(0, 0, width, 1), data, data.Length * 2, 0);
                var views = Views(BitmapFrame.Create(source)); byte[] raw = new byte[data.Length * 2]; Buffer.BlockCopy(data, 0, raw, 0, raw.Length);
                AssertViews(views, raw);
                Array.Clear(data); source.WritePixels(new Int32Rect(0, 0, width, 1), data, data.Length * 2, 0); AssertViews(views, new byte[raw.Length]);
                var output = new MemoryStream(); var encoder = Encoder(views[0]); Expect<NotSupportedException>(() => encoder.Save(output)); Require(output.Length == 0, "High-bit PNG rejection wrote display-quantized samples.");
                encoder.Frames[0] = BitmapFrame.Create(Source(Red, PixelFormats.Bgra32)); encoder.Save(output); Require(output.Length > 0, "Unsupported format validation consumed encoder.");
            }
        });
        check("encode-png-straight-premultiplied-and-opaque-stock-samples", () =>
        {
            byte[] straight = Convert.FromHexString("172D4300FF7F3F010102030201020303013F7F7F01408080017FFEFE0180FFFF");
            byte[] premul = Convert.FromHexString("00000000010001010102000201020303013F7F7F01408080017FFEFE0180FFFF");
            byte[] stockPremul = Convert.FromHexString("00000000FF00FF017FFF000255AAFF03027EFE7F017FFF80017FFEFE0180FFFF");
            foreach (var format in new[] { PixelFormats.Bgra32, PixelFormats.Pbgra32, PixelFormats.Bgr32 })
            {
                byte[] raw = format == PixelFormats.Pbgra32 ? premul : straight;
                var source = Source(raw, format); var frame = BitmapFrame.Create(source);
                byte[] encoded = Save(frame); var decoded = Decode(encoded); byte[] result = Pixels(decoded);
                byte[] expected = (byte[])(format == PixelFormats.Pbgra32 ? stockPremul : raw).Clone();
                if (format == PixelFormats.Bgr32) for (int i = 3; i < expected.Length; i += 4) expected[i] = 255;
                for (int i = 0; i < result.Length; i++) Require(Math.Abs(result[i] - expected[i]) <= (format == PixelFormats.Pbgra32 && i % 4 != 3 ? 1 : 0), "PNG channel, hidden straight RGB, alpha or measured premultiplication tolerance changed.");
                Require(Pixels(source).SequenceEqual(raw), "PNG Save modified source samples.");
                Require(decoded.PixelWidth == 8 && decoded.PixelHeight == 1, "PNG dimensions changed.");
                Require(Chunks(encoded).Any(chunk => chunk.Type == "sRGB"), "Default sRGB tagging was omitted.");
            }
        });
        check("encode-png-real-density-truncation-crc-and-boundaries", () =>
        {
            foreach (var pair in new[] { (96d, 120d, 3779u, 4724u), (144d, 192d, 5669u, 7559u), (96.123456, 144.654321, 3784u, 5695u), (0d, -1d, 3779u, 3779u), (0.0254, 600d, 1u, 23622u) })
            {
                byte[] png = Save(BitmapFrame.Create(Source(Red, PixelFormats.Bgra32, pair.Item1, pair.Item2)));
                var density = Chunks(png).Single(chunk => chunk.Type == "pHYs").Data;
                Require(BinaryPrimitives.ReadUInt32BigEndian(density) == pair.Item3 && BinaryPrimitives.ReadUInt32BigEndian(density.AsSpan(4)) == pair.Item4 && density[8] == 1, "pHYs did not truncate DPI to pixels per meter.");
                var decoded = Decode(png); Require(decoded.DpiX == (double)(float)(pair.Item3 * 0.0254) && decoded.DpiY == (double)(float)(pair.Item4 * 0.0254), "Decoded DPI did not reflect recorded quantum.");
            }
            foreach (double bad in new[] { 0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity, ((double)uint.MaxValue + 1) * 0.0254 })
            {
                var output = new MemoryStream(); var encoder = Encoder(BitmapFrame.Create(Source(Red, PixelFormats.Bgra32, bad, 96)));
                Expect<NotSupportedException>(() => encoder.Save(output)); Require(output.Length == 0, "Unsupported DPI partially wrote PNG.");
                encoder.Frames[0] = BitmapFrame.Create(Source(Red, PixelFormats.Bgra32)); encoder.Save(output);
            }
        });
        check("encode-png-options-decoded-frame-rejection-and-recovery", () =>
        {
            var frame = BitmapFrame.Create(Source(Red, PixelFormats.Bgra32)); using var output = new MemoryStream();
            var encoder = new PngBitmapEncoder(); Expect<NotSupportedException>(() => encoder.Save(output));
            encoder.Frames.Add(null!); Expect<ArgumentNullException>(() => encoder.Save(output)); encoder.Frames[0] = frame; encoder.Frames.Add(frame); Expect<NotSupportedException>(() => encoder.Save(output)); encoder.Frames.RemoveAt(1);
            foreach (var invalid in new[] { PngInterlaceOption.On, (PngInterlaceOption)3 }) { encoder.Interlace = invalid; Expect<NotSupportedException>(() => encoder.Save(output)); }
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "dockleft.png"));
            encoder.Interlace = PngInterlaceOption.Off; encoder.Frames[0] = new PngBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Expect<NotSupportedException>(() => encoder.Save(output)); Require(output.Length == 0, "Unsupported frame/options wrote partial output.");
            encoder.Frames[0] = frame; encoder.Save(output); Require(Chunks(output.ToArray()).Single(chunk => chunk.Type == "IHDR").Data[12] == 0, "Off interlace did not emit noninterlaced PNG.");
            Expect<InvalidOperationException>(() => encoder.Save(output));
            var untouched = new MemoryStream();
            Expect<InvalidOperationException>(() => Encoder(frame).ColorContexts = new ReadOnlyCollection<ColorContext>([]));
            Expect<InvalidOperationException>(() => new PngBitmapEncoder().Thumbnail = frame);
            Expect<InvalidOperationException>(() => new PngBitmapEncoder().Preview = frame);
            Expect<NotSupportedException>(() => { _ = new PngBitmapEncoder().CodecInfo; });
            Expect<NotSupportedException>(() => { var jpeg = new JpegBitmapEncoder(); jpeg.Frames.Add(frame); jpeg.Save(untouched); });
        });
        check("encode-png-cache-shaped-frozen-onload-image-and-closed-input", () =>
        {
            var input = new MemoryStream(Save(BitmapFrame.Create(Source(Red, PixelFormats.Bgra32, 120, 144))));
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input; image.EndInit(); image.Freeze(); input.Dispose();
            var frame = BitmapFrame.Create(image); frame.Freeze();
            var copied = Decode(Save(frame)); Require(Pixels(copied).SequenceEqual(Red), "Plugin cache-shaped frozen BitmapImage pipeline failed.");
            Task.Run(() => Require(Pixels(frame).SequenceEqual(Red), "Frozen source-backed frame is not worker readable.")).GetAwaiter().GetResult();
        });
        check("encode-png-stream-position-prefix-tail-ownership-and-one-shot", () =>
        {
            foreach (var layout in new[] { (0, 0), (8, 8), (1024, 4), (1024, 0) })
            {
                byte[] initial = Enumerable.Repeat((byte)0xa5, layout.Item1).ToArray();
                using var stream = new OutputStream(initial); stream.Position = layout.Item2; var encoder = Encoder(BitmapFrame.Create(Source(Red, PixelFormats.Bgra32)));
                encoder.Save(stream); long end = stream.Position; byte[] bytes = stream.Bytes;
                Require(bytes.Take(layout.Item2).All(value => value == 0xa5) && bytes.Skip(layout.Item2).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Save ignored stream position or prefix.");
                Require(stream.Length == Math.Max(layout.Item1, end) && bytes.Skip((int)end).All(value => value == 0xa5) && stream.Flushes == 0 && !stream.Disposed, "Save truncated tail, flushed or closed caller stream.");
                int writes = stream.Writes; Expect<InvalidOperationException>(() => encoder.Save(stream)); Require(stream.Writes == writes && stream.Position == end, "Second Save attempted output.");
            }
        });
        check("encode-png-stream-validation-retry-and-write-failure-poison", () =>
        {
            var frame = BitmapFrame.Create(Source(Red, PixelFormats.Bgra32));
            foreach (string kind in new[] { "null", "disposed", "read-only", "nonseekable" })
            {
                var encoder = Encoder(frame); using var stream = new MemoryStream();
                if (kind == "null") Expect<ArgumentNullException>(() => encoder.Save(null!));
                if (kind == "disposed") { stream.Dispose(); Expect<ObjectDisposedException>(() => encoder.Save(stream)); }
                if (kind == "read-only") { using var readOnly = new MemoryStream(new byte[100], writable: false); Expect<NotSupportedException>(() => encoder.Save(readOnly)); }
                if (kind == "nonseekable") { using var nonseek = new OutputStream([], seekable: false); Expect<NotSupportedException>(() => encoder.Save(nonseek)); Require(nonseek.Writes == 0, "Nonseekable validation attempted output."); }
                using var retry = new MemoryStream(); encoder.Save(retry); Require(retry.Length > 0, "Stream preflight consumed encoder.");
            }
            foreach (int limit in new[] { 0, 16 })
            {
                var encoder = Encoder(frame); using var stream = new OutputStream([], failAfter: limit);
                Expect<InvalidOperationException>(() => encoder.Save(stream)); Require(stream.Length == limit && !stream.Disposed && stream.Flushes == 0, "Failed write ownership or partial output changed.");
                using var retry = new OutputStream([]); Expect<InvalidOperationException>(() => encoder.Save(retry)); Require(retry.Writes == 0, "I/O failure did not poison encoder.");
            }
        });
    }

    private static BitmapSource Source(byte[] pixels, PixelFormat format, double dpiX = 96, double dpiY = 96) => BitmapSource.Create(pixels.Length / 4, 1, dpiX, dpiY, format, null, pixels, pixels.Length);
    private static BitmapFrame[] Views(BitmapFrame frame) => [frame, (BitmapFrame)frame.Clone(), (BitmapFrame)frame.CloneCurrentValue(), (BitmapFrame)frame.GetAsFrozen(), (BitmapFrame)frame.GetCurrentValueAsFrozen(), BitmapFrame.Create(frame)];
    private static byte[] Pixels(BitmapSource source) { int stride = source.PixelWidth * source.Format.BitsPerPixel / 8; byte[] result = new byte[stride * source.PixelHeight]; source.CopyPixels(result, stride, 0); return result; }
    private static void AssertViews(IEnumerable<BitmapFrame> views, byte[] expected) { foreach (var view in views) Require(Pixels(view).SequenceEqual(expected), "Frame view did not observe current source samples."); }
    private static PngBitmapEncoder Encoder(BitmapFrame frame) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame); return encoder; }
    private static void AssertSaved(PngBitmapEncoder encoder, byte[] expected)
    {
        using var stream = new MemoryStream(); encoder.Save(stream);
        Require(Pixels(Decode(stream.ToArray())).SequenceEqual(expected), "PNG Save captured stale source pixels.");
    }
    private static byte[] Save(BitmapFrame frame) { using var stream = new MemoryStream(); Encoder(frame).Save(stream); return stream.ToArray(); }
    private static BitmapFrame Decode(byte[] bytes) { using var stream = new MemoryStream(bytes); return new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; }
    private static DrawingVisual Visual(Brush brush) { var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, new Rect(0, 0, 1, 1)); return visual; }
    [MethodImpl(MethodImplOptions.NoInlining)] private static (BitmapFrame, WeakReference) RetainedFrame() { var source = Source(Red, PixelFormats.Bgra32); return (BitmapFrame.Create(source), new WeakReference(source)); }
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static bool Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return true; } return false; }
    private static void Expect<T>(Action action) where T : Exception => Require(Throws<T>(action), "Expected " + typeof(T).Name);
    private static IEnumerable<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        for (int offset = 8; offset < png.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            uint crc = uint.MaxValue;
            foreach (byte value in png.AsSpan(offset + 4, length + 4)) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
            Require(~crc == BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + length + 8, 4)), "Encoded PNG chunk CRC is invalid.");
            yield return (type, png.AsSpan(offset + 8, length).ToArray()); offset += length + 12;
        }
    }
    private sealed class OutputStream : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly bool _seekable;
        private readonly int _failAfter;
        internal int Writes { get; private set; }
        internal int Flushes { get; private set; }
        internal bool Disposed { get; private set; }
        internal byte[] Bytes => _inner.ToArray();
        internal OutputStream(byte[] initial, bool seekable = true, int failAfter = -1) { _inner.Write(initial); _inner.Position = 0; _seekable = seekable; _failAfter = failAfter; }
        public override bool CanRead => false;
        public override bool CanSeek => _seekable;
        public override bool CanWrite => !Disposed;
        public override long Length => _inner.Length;
        public override long Position { get => _seekable ? _inner.Position : throw new NotSupportedException(); set => _inner.Position = value; }
        public override void Flush() { Flushes++; throw new IOException("Caller does not permit Flush."); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new InvalidOperationException("Caller does not permit truncation.");
        public override void Write(byte[] buffer, int offset, int count) { Writes++; if (_failAfter >= 0) { int bytes = Math.Min(count, Math.Max(0, _failAfter - (int)_inner.Length)); _inner.Write(buffer, offset, bytes); throw new IOException("Injected caller output failure."); } _inner.Write(buffer, offset, count); }
        protected override void Dispose(bool disposing) { Disposed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
