// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Windows.Navigation;
using MS.Internal;
using MS.Internal.PresentationCore;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    public sealed partial class BitmapImage
    {
        private void FinalizePortableCreation()
        {
            _bitmapInit.EnsureInitializedComplete();
            if (!SourceRect.IsEmpty || DecodePixelWidth != 0 || DecodePixelHeight != 0 || Rotation != Rotation.Rotate0 ||
                CreateOptions != BitmapCreateOptions.None || UriCachePolicy != null ||
                (CacheOption != BitmapCacheOption.Default && CacheOption != BitmapCacheOption.OnLoad))
                throw UnsupportedPortableImage("crop, decode sizing, rotation, create options or cache policy");

            (PortableBitmapBuffer Buffer, double DpiX, double DpiY) decoded;
            if (StreamSource != null)
            {
                if (UriSource != null || CacheOption != BitmapCacheOption.OnLoad)
                    throw UnsupportedPortableImage("StreamSource without exclusive OnLoad ownership");
                decoded = DecodePortablePng(StreamSource);
            }
            else
            {
                Uri uri = _baseUri == null ? UriSource : new Uri(_baseUri, UriSource);
                if (_xpsPackageOrigin != null && !XpsLoadingContext.IsUriAllowedAgainstPackage(_xpsPackageOrigin, uri))
                    throw new FileFormatException(SR.Resource_XpsPackageBoundaryViolation);
                if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != "pack" || !BaseUriHelper.IsPackApplicationUri(uri))
                    throw UnsupportedPortableImage("URI sources outside the local application pack");
                using (WebResponse response = WpfWebRequestHelper.CreateRequestAndGetResponse(uri))
                using (Stream stream = response.GetResponseStream())
                    decoded = DecodePortablePng(stream);
            }
            InstallPortableBuffer(decoded.Buffer, decoded.DpiX, decoded.DpiY);
        }

        private static (PortableBitmapBuffer Buffer, double DpiX, double DpiY) DecodePortablePng(Stream stream)
        {
            using var encoded = new MemoryStream();
            stream.CopyTo(encoded);
            byte[] bytes = encoded.ToArray();
            using SKData data = SKData.CreateCopy(bytes);
            using SKCodec codec = SKCodec.Create(data) ?? throw new FileFormatException("Invalid portable PNG data.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Png)
                throw UnsupportedPortableImage("encoded formats other than PNG");
            var metadata = ReadPortablePngMetadata(bytes);
            if (codec.Info.Width != metadata.Width || codec.Info.Height != metadata.Height)
                throw new FileFormatException("Portable PNG header and decoded dimensions differ.");
            using SKColorSpace colorSpace = SKColorSpace.CreateSrgb();
            var info = new SKImageInfo(metadata.Width, metadata.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, colorSpace);
            var buffer = new PortableBitmapBuffer(metadata.Width, metadata.Height, PixelFormats.Bgra32, mutable: false);
            if (codec.GetPixels(info, buffer.BackBuffer, buffer.Stride, SKCodecOptions.Default) != SKCodecResult.Success)
                throw new FileFormatException("Portable PNG pixels could not be completely decoded.");
            return (buffer, metadata.DpiX, metadata.DpiY);
        }

        private static (int Width, int Height, double DpiX, double DpiY) ReadPortablePngMetadata(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 8 || !bytes.Slice(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new FileFormatException("Invalid portable PNG signature.");
            int width = 0, height = 0;
            double dpiX = 96, dpiY = 96;
            bool pixels = false, complete = false;
            for (int offset = 8; offset <= bytes.Length - 12;)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
                uint type = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 4, 4));
                if (length > (uint)(bytes.Length - offset - 12))
                    throw new FileFormatException("Truncated portable PNG chunk.");
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
                        width = (int)w; height = (int)h;
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
            return (width, height, dpiX, dpiY);
        }

        private static NotSupportedException UnsupportedPortableImage(string feature) =>
            new NotSupportedException("Portable BitmapImage does not support " + feature + ".");
    }
}
#endif
