// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using System.IO;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    internal static class PortablePngDecoder
    {
        internal static (PortableBitmapBuffer Buffer, double DpiX, double DpiY) Decode(Stream stream, bool colorManage)
        {
            using var encoded = new MemoryStream();
            stream.CopyTo(encoded);
            byte[] bytes = encoded.ToArray();
            return Decode(bytes, colorManage);
        }

        internal static (PortableBitmapBuffer Buffer, double DpiX, double DpiY) Decode(byte[] bytes, bool colorManage, bool preservePixelFormat = false)
        {
            using SKData data = SKData.CreateCopy(bytes);
            using SKCodec codec = SKCodec.Create(data) ?? throw new FileFormatException("Invalid portable PNG data.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Png)
                throw UnsupportedPortableImage("encoded formats other than PNG");
            var metadata = ReadPortablePngMetadata(bytes);
            if (codec.Info.Width != metadata.Width || codec.Info.Height != metadata.Height)
                throw new FileFormatException("Portable PNG header and decoded dimensions differ.");
            PixelFormat format = (colorManage && !preservePixelFormat) || metadata.HasAlpha || codec.Info.AlphaType != SKAlphaType.Opaque ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            PortableBitmapBuffer buffer = DecodePixels(codec, format, colorManage);
            return (buffer, metadata.DpiX, metadata.DpiY);
        }

        internal static (int Width, int Height, double DpiX, double DpiY, bool HasAlpha) ReadPortablePngMetadata(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 8 || !bytes.Slice(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new FileFormatException("Invalid portable PNG signature.");
            int width = 0, height = 0;
            bool alpha = false;
            double dpiX = 96, dpiY = 96;
            bool pixels = false, complete = false;
            for (int offset = 8; offset <= bytes.Length - 12;)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
                uint type = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 4, 4));
                if (length > (uint)(bytes.Length - offset - 12))
                    throw new FileFormatException("Truncated portable PNG chunk.");
                if (ComputeCrc(bytes.Slice(offset + 4, checked((int)length + 4))) !=
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + (int)length, 4)))
                    throw new FileFormatException("Invalid portable PNG chunk checksum.");
                ReadOnlySpan<byte> chunk = bytes.Slice(offset + 8, (int)length);
                if (offset == 8 && type != 0x49484452)
                    throw new FileFormatException("Portable PNG must begin with IHDR.");
                switch (type)
                {
                    case 0x49484452: // IHDR
                        if (offset != 8 || length != 13)
                            throw new FileFormatException("Invalid portable PNG IHDR.");
                        uint w = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(0, 4));
                        uint h = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4));
                        if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
                            throw new FileFormatException("Invalid portable PNG dimensions.");
                        if (chunk[8] != 8 || (chunk[9] != 2 && chunk[9] != 6))
                            throw UnsupportedPortableImage("PNG formats other than 8-bit RGB or RGBA");
                        width = (int)w; height = (int)h; alpha = chunk[9] == 6;
                        break;
                    case 0x70485973: // pHYs
                        if (length != 9)
                            throw new FileFormatException("Invalid portable PNG pHYs.");
                        uint x = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(0, 4));
                        uint y = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4));
                        if (chunk[8] == 1 && x != 0 && y != 0)
                        {
                            dpiX = (float)(x * 0.0254);
                            dpiY = (float)(y * 0.0254);
                        }
                        break;
                    case 0x6163544C: // acTL
                        throw UnsupportedPortableImage("animated PNG");
                    case 0x49444154: // IDAT
                        pixels = true;
                        break;
                    case 0x49454E44: // IEND
                        if (length != 0)
                            throw new FileFormatException("Invalid portable PNG IEND.");
                        complete = true;
                        break;
                }
                offset += (int)length + 12;
                if (complete) break;
            }
            if (width == 0 || !pixels || !complete)
                throw new FileFormatException("Incomplete portable PNG data.");
            return (width, height, dpiX, dpiY, alpha);
        }

        internal static uint ComputeCrc(ReadOnlySpan<byte> bytes)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
            return ~crc;
        }

        internal static PortableBitmapBuffer DecodePixels(SKCodec codec, PixelFormat format, bool colorManage)
        {
            using SKColorSpace colorSpace = colorManage ? SKColorSpace.CreateSrgb() : null;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, colorSpace);
            var buffer = new PortableBitmapBuffer(info.Width, info.Height, format, mutable: false);
            if (codec.GetPixels(info, buffer.BackBuffer, buffer.Stride, SKCodecOptions.Default) != SKCodecResult.Success)
                throw new FileFormatException("Portable pixels could not be completely decoded.");
            return buffer;
        }

        private static NotSupportedException UnsupportedPortableImage(string feature) =>
            new NotSupportedException("Portable BitmapImage does not support " + feature + ".");
    }
}
#endif
