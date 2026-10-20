// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using System.IO;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    internal static class PortableJpegDecoder
    {
        internal static unsafe (PortableBitmapBuffer Buffer, double DpiX, double DpiY) Decode(byte[] bytes, bool preservePixelFormat, bool colorManage)
        {
            if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216)
                throw new FileFormatException("Invalid portable JPEG signature.");
            double dpiX = 96, dpiY = 96;
            for (int offset = 2; offset + 4 <= bytes.Length;)
            {
                if (bytes[offset] != 255) throw new FileFormatException("Invalid portable JPEG marker.");
                while (offset + 1 < bytes.Length && bytes[offset + 1] == 255) offset++;
                if (offset + 4 > bytes.Length) throw new FileFormatException("Truncated portable JPEG marker.");
                byte marker = bytes[offset + 1];
                if (marker == 0xda || marker == 0xd9) break;
                int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2));
                if (length < 2 || length > bytes.Length - offset - 2)
                    throw new FileFormatException("Truncated portable JPEG segment.");
                if (marker == 0xe0 && length >= 16 && bytes.AsSpan(offset + 4, 5).SequenceEqual(new byte[] { 74, 70, 73, 70, 0 }))
                {
                    int x = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 12));
                    int y = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 14));
                    byte unit = bytes[offset + 11];
                    if (x != 0 && y != 0 && (unit == 1 || unit == 2))
                    { dpiX = unit == 1 ? x : x * 2.54; dpiY = unit == 1 ? y : y * 2.54; }
                }
                offset += length + 2;
            }
            using SKData data = SKData.CreateCopy(bytes);
            using SKCodec codec = SKCodec.Create(data) ?? throw new FileFormatException("Invalid portable JPEG data.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg)
                throw new FileFormatException("The encoded bitmap does not match the requested JPEG decoder.");
            if (codec.Info.ColorType == SKColorType.Gray8)
            {
                var gray = new PortableBitmapBuffer(codec.Info.Width, codec.Info.Height, PixelFormats.Gray8, mutable: false);
                var info = new SKImageInfo(gray.Width, gray.Height, SKColorType.Gray8, SKAlphaType.Opaque);
                if (codec.GetPixels(info, gray.BackBuffer, gray.Stride, SKCodecOptions.Default) != SKCodecResult.Success)
                    throw new FileFormatException("Portable JPEG pixels could not be completely decoded.");
                return (gray, dpiX, dpiY);
            }
            PortableBitmapBuffer decoded = PortablePngDecoder.DecodePixels(codec, PixelFormats.Bgr32, colorManage);
            if (!preservePixelFormat) return (decoded, dpiX, dpiY);
            var result = new PortableBitmapBuffer(decoded.Width, decoded.Height, PixelFormats.Bgr24, mutable: false);
            byte* source = (byte*)decoded.BackBuffer, target = (byte*)result.BackBuffer;
            for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x++)
                new ReadOnlySpan<byte>(source + y * decoded.Stride + x * 4, 3).CopyTo(new Span<byte>(target + y * result.Stride + x * 3, 3));
            return (result, dpiX, dpiY);
        }
    }
}
#endif
