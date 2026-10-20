// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    public sealed partial class JpegBitmapEncoder
    {
        internal byte[] EncodePortable(BitmapFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (Rotation != Rotation.Rotate0 || FlipHorizontal || FlipVertical)
                throw new NotSupportedException("Portable JPEG encoding does not support lossless transformations.");
            if (frame is not BitmapFrameEncode || frame.PortableBuffer == null)
                throw new NotSupportedException("Portable JPEG encoding requires a source-backed frame without decoder metadata or color contexts.");
            ushort dpiX = Density(frame.DpiX), dpiY = Density(frame.DpiY);
            bool gray = frame.Format == PixelFormats.Gray8 || frame.Format == PixelFormats.Gray16 || frame.Format == PixelFormats.Gray32Float;
            using SKBitmap source = gray ? frame.PortableBuffer.CreateGrayBitmap() : frame.PortableBuffer.CreateDisplayBitmap();
            var info = new SKImageInfo(source.Width, source.Height, gray ? SKColorType.Gray8 : SKColorType.Bgra8888,
                gray ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            using var staging = new SKBitmap(info);
            using SKPixmap sourcePixels = source.PeekPixels();
            if (staging.GetPixels() == IntPtr.Zero || !sourcePixels.ReadPixels(info, staging.GetPixels(), staging.RowBytes, 0, 0))
                throw new InvalidOperationException("Portable JPEG staging storage could not be populated.");
            using SKImage image = SKImage.FromBitmap(staging);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, QualityLevel) ?? throw new InvalidOperationException("Portable JPEG encoding failed.");
            byte[] bytes = encoded.ToArray();
            if (bytes.Length < 20 || bytes[2] != 255 || bytes[3] != 224 || !bytes.AsSpan(6, 5).SequenceEqual(new byte[] { 74, 70, 73, 70, 0 }))
                throw new InvalidOperationException("Portable JPEG encoder returned no JFIF density header.");
            bytes[13] = 1;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), dpiX);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), dpiY);
            return bytes;
        }

        private static ushort Density(double dpi)
        {
            if (!double.IsFinite(dpi)) throw new NotSupportedException("Portable JPEG encoding requires finite DPI.");
            double value = dpi <= 0 ? 96 : Math.Floor(dpi + 0.5);
            if (value < 1 || value > ushort.MaxValue) throw new NotSupportedException("Portable JPEG encoding requires representable nonzero JFIF density.");
            return (ushort)value;
        }
    }
}
#endif
