// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    public sealed partial class PngBitmapEncoder
    {
        internal byte[] EncodePortable(BitmapFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (Interlace != PngInterlaceOption.Default && Interlace != PngInterlaceOption.Off)
                throw new NotSupportedException("Portable PNG encoding does not support interlacing or unknown interlace options.");
            if (frame is not BitmapFrameEncode || frame.PortableBuffer == null)
                throw new NotSupportedException("Portable PNG encoding requires a source-backed frame without decoder metadata or color contexts.");
            PixelFormat format = frame.Format;
            if (format != PixelFormats.Bgra32 && format != PixelFormats.Pbgra32 && format != PixelFormats.Bgr32)
                throw new NotSupportedException("Portable PNG encoding supports only Bgra32, Pbgra32 or Bgr32 samples.");
            uint dpiX = PixelsPerMeter(frame.DpiX);
            uint dpiY = PixelsPerMeter(frame.DpiY);
            SKAlphaType alpha = format == PixelFormats.Pbgra32 ? SKAlphaType.Premul :
                format == PixelFormats.Bgr32 ? SKAlphaType.Opaque : SKAlphaType.Unpremul;
            using SKColorSpace colorSpace = SKColorSpace.CreateSrgb();
            using var bitmap = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight,
                SKColorType.Bgra8888, alpha, colorSpace));
            if (bitmap.GetPixels() == IntPtr.Zero)
                throw new InvalidOperationException("Portable PNG staging storage could not be allocated.");
            frame.CopyPixels(Int32Rect.Empty, bitmap.GetPixels(), checked(bitmap.RowBytes * bitmap.Height), bitmap.RowBytes);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100) ??
                throw new InvalidOperationException("Portable PNG encoding failed.");
            byte[] bytes = encoded.ToArray();
            if (bytes.Length < 33 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13 ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4)) != 0x49484452)
                throw new InvalidOperationException("Portable PNG encoder returned an invalid header.");

            // Skia emits no pHYs; WPF records truncated pixels per meter at the source DPI.
            byte[] result = new byte[checked(bytes.Length + 21)];
            bytes.AsSpan(0, 33).CopyTo(result);
            Span<byte> chunk = result.AsSpan(33, 21);
            BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(4), 0x70485973);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(8), dpiX);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(12), dpiY);
            chunk[16] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(17), PortablePngDecoder.ComputeCrc(chunk.Slice(4, 13)));
            bytes.AsSpan(33).CopyTo(result.AsSpan(54));
            return result;
        }

        private static uint PixelsPerMeter(double dpi)
        {
            if (!double.IsFinite(dpi))
                throw new NotSupportedException("Portable PNG encoding requires finite DPI.");
            double value = (dpi <= 0 ? 96 : dpi) / 0.0254;
            if (value < 1 || value >= (double)uint.MaxValue + 1)
                throw new NotSupportedException("Portable PNG encoding requires representable nonzero pixels per meter.");
            return (uint)value;
        }
    }
}
#endif
