// Licensed under the Mozilla Public License, v. 2.0.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Accord.Imaging;
using Accord.Imaging.Filters;
using DrawingFormat = System.Drawing.Imaging.PixelFormat;

namespace NINA.Image.ImageAnalysis {
    internal sealed class PortableImageBuffer : IDisposable {
        private IntPtr memory;
        internal int Width { get; }
        internal int Height { get; }
        internal int Stride { get; }
        internal DrawingFormat Format { get; }
        internal IntPtr Memory => memory != IntPtr.Zero ? memory : throw new ObjectDisposedException(nameof(PortableImageBuffer));

        internal unsafe PortableImageBuffer(int width, int height, DrawingFormat format) {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            int bytesPerPixel = format == DrawingFormat.Format8bppIndexed ? 1 : format == DrawingFormat.Format16bppGrayScale ? 2 : format == DrawingFormat.Format48bppRgb ? 6 : throw new NotSupportedException();
            Width = width; Height = height; Format = format;
            int packedStride = checked(width * bytesPerPixel);
            Stride = bytesPerPixel == 1 ? checked((packedStride + 3) & ~3) : packedStride;
            int size = checked(Stride * height);
            memory = Marshal.AllocHGlobal(size);
            new Span<byte>((void*)memory, size).Clear();
        }

        internal UnmanagedImage Borrow() => new UnmanagedImage(Memory, Width, Height, Stride, Format);

        internal static PortableImageBuffer FromSource(BitmapSource source) {
            var format = source.Format == PixelFormats.Gray16 ? DrawingFormat.Format16bppGrayScale
                : source.Format == PixelFormats.Rgb48 ? DrawingFormat.Format48bppRgb
                : source.Format == PixelFormats.Gray8 ? DrawingFormat.Format8bppIndexed : throw new NotSupportedException();
            var result = new PortableImageBuffer(source.PixelWidth, source.PixelHeight, format);
            try { source.CopyPixels(Int32Rect.Empty, result.Memory, checked(result.Stride * result.Height), result.Stride); return result; }
            catch { result.Dispose(); throw; }
        }

        internal BitmapSource ToSource() {
            var format = Format == DrawingFormat.Format16bppGrayScale ? PixelFormats.Gray16
                : Format == DrawingFormat.Format48bppRgb ? PixelFormats.Rgb48 : PixelFormats.Gray8;
            var result = BitmapSource.Create(Width, Height, 96, 96, format, null, Memory, checked(Stride * Height), Stride);
            result.Freeze(); return result;
        }

        internal static PortableImageBuffer Grayscale(BitmapSource source) {
            var input = FromSource(source);
            if (input.Format == DrawingFormat.Format16bppGrayScale) return input;
            try {
                var result = new PortableImageBuffer(input.Width, input.Height, DrawingFormat.Format16bppGrayScale);
                try {
                    using (var src = input.Borrow()) using (var dst = result.Borrow()) new Grayscale(0.2125, 0.7154, 0.0721).Apply(src, dst);
                    return result;
                } catch { result.Dispose(); throw; }
            } finally { input.Dispose(); }
        }

        internal static unsafe PortableImageBuffer Gray8(BitmapSource source) {
            using (var gray = Grayscale(source)) {
                var result = new PortableImageBuffer(gray.Width, gray.Height, DrawingFormat.Format8bppIndexed);
                for (int y = 0; y < gray.Height; y++) {
                    var src = (ushort*)((byte*)gray.Memory + y * gray.Stride);
                    var dst = (byte*)result.Memory + y * result.Stride;
                    for (int x = 0; x < gray.Width; x++) dst[x] = (byte)(src[x] >> 8);
                }
                return result;
            }
        }

        public void Dispose() {
            var released = Interlocked.Exchange(ref memory, IntPtr.Zero);
            if (released != IntPtr.Zero) Marshal.FreeHGlobal(released);
        }
    }
}
