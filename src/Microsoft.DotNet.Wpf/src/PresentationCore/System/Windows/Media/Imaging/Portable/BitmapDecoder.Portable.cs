// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Windows.Navigation;
using MS.Internal;
using MS.Internal.PresentationCore;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    public abstract partial class BitmapDecoder
    {
        private bool _isPortable;

        private static NotSupportedException UnsupportedPortableDecoder(string feature) =>
            new NotSupportedException("Portable bitmap decoding does not support " + feature + ".");

        private static void ValidatePortableOptions(BitmapCreateOptions options, BitmapCacheOption cache, RequestCachePolicy policy)
        {
            if (options != BitmapCreateOptions.None || policy != null ||
                (cache != BitmapCacheOption.Default && cache != BitmapCacheOption.OnLoad))
                throw UnsupportedPortableDecoder("create options or cache policies outside eager Default/OnLoad");
        }

        private static byte[] ReadPortableInput(Uri baseUri, Uri uri, Stream stream)
        {
            if (uri != null)
            {
                Uri resolved = baseUri == null ? uri : BaseUriHelper.GetResolvedUri(baseUri, uri);
                if (resolved.IsAbsoluteUri && !XpsLoadingContext.IsUriAllowedInCurrentContext(resolved))
                    throw new FileFormatException(SR.Resource_XpsPackageBoundaryViolation);
                if (!resolved.IsAbsoluteUri || resolved.Scheme != "pack" || !BaseUriHelper.IsPackApplicationUri(resolved))
                    throw UnsupportedPortableDecoder("URI sources outside the local application pack");
                using (WebResponse response = WpfWebRequestHelper.CreateRequestAndGetResponse(resolved))
                using (Stream owned = response.GetResponseStream())
                    return ReadPortableInput(null, null, owned);
            }
            ArgumentNullException.ThrowIfNull(stream);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static Guid PortableContainer(byte[] bytes)
        {
            if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                return MILGuidData.GUID_ContainerFormatPng;
            if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == 0 &&
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) == 1)
                return MILGuidData.GUID_ContainerFormatIco;
            if (bytes.Length < 8) throw new FileFormatException("Incomplete portable bitmap header.");
            throw UnsupportedPortableDecoder("encoded formats other than PNG or ICO");
        }

        private static BitmapDecoder CreatePortableDecoder(Uri baseUri, Uri uri, Stream stream,
            BitmapCreateOptions options, BitmapCacheOption cache, RequestCachePolicy policy)
        {
            ValidatePortableOptions(options, cache, policy);
            byte[] bytes = ReadPortableInput(baseUri, uri, stream);
            Guid container = PortableContainer(bytes);
            if (container == MILGuidData.GUID_ContainerFormatPng)
                return new PngBitmapDecoder(bytes, baseUri, uri, stream, options, cache);
            return new IconBitmapDecoder(bytes, baseUri, uri, stream, options, cache);
        }

        internal void InitializePortableDecoder(Uri baseUri, Uri uri, Stream stream, BitmapCreateOptions options,
            BitmapCacheOption cache, Guid expectedContainer, byte[] encoded)
        {
            ValidatePortableOptions(options, cache, null);
            if (expectedContainer != MILGuidData.GUID_ContainerFormatPng && expectedContainer != MILGuidData.GUID_ContainerFormatIco)
                throw UnsupportedPortableDecoder("codecs other than PNG or ICO");
            byte[] bytes = encoded ?? ReadPortableInput(baseUri, uri, stream);
            Guid container = PortableContainer(bytes);
            if (container != expectedContainer)
                throw new FileFormatException("The encoded bitmap does not match the requested decoder.");
            bool icon = container == MILGuidData.GUID_ContainerFormatIco;
            var decoded = icon ? DecodePortableIcon(bytes) :
                new List<(PortableBitmapBuffer Buffer, double DpiX, double DpiY)> { PortablePngDecoder.Decode(bytes, colorManage: false) };
            _baseUri = baseUri;
            _uri = uri;
            _stream = stream;
            _createOptions = options;
            _cacheOption = cache;
            _isPortable = true;
            _isBuiltInDecoder = true;
            var frames = new List<BitmapFrame>(decoded.Count);
            for (int i = 0; i < decoded.Count; i++)
            {
                var pixels = decoded[i];
                var frame = new BitmapFrameDecode(i, this, pixels.Buffer, pixels.DpiX, pixels.DpiY, icon);
                frame.Freeze();
                frames.Add(frame);
            }
            _frames = frames;
            _readOnlyFrames = new ReadOnlyCollection<BitmapFrame>(frames);
            GC.SuppressFinalize(this);
        }

        private static List<(PortableBitmapBuffer Buffer, double DpiX, double DpiY)> DecodePortableIcon(byte[] bytes)
        {
            if (bytes.Length < 6) throw new FileFormatException("Incomplete portable ICO header.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
            int tableEnd = 6 + count * 16;
            if (count == 0 || tableEnd > bytes.Length) throw new FileFormatException("Invalid portable ICO directory.");
            var frames = new List<(PortableBitmapBuffer Buffer, double DpiX, double DpiY)>(count);
            var ranges = new List<(int Offset, int End)>(count);
            // Directory order is deterministic; WIC's undocumented frame sorting is not reproduced.
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = bytes.AsSpan(6 + i * 16, 16);
                int width = entry[0] == 0 ? 256 : entry[0], height = entry[1] == 0 ? 256 : entry[1];
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8));
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12));
                if (entry[3] != 0 || offset < tableEnd || offset > bytes.Length || length == 0 || length > bytes.Length - offset)
                    throw new FileFormatException("Invalid portable ICO payload bounds.");
                int start = (int)offset, end = checked(start + (int)length);
                foreach (var range in ranges)
                    if (start < range.End && range.Offset < end) throw new FileFormatException("Overlapping portable ICO payloads.");
                ranges.Add((start, end));
                if (entry[2] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(4)) != 1 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6)) != 32)
                    throw UnsupportedPortableDecoder("ICO entries other than 32-bit truecolor");
                ReadOnlySpan<byte> payload = bytes.AsSpan(start, (int)length);
                if (payload.Length >= 8 && payload.Slice(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                {
                    var header = PortablePngDecoder.ReadPortablePngMetadata(payload);
                    if (header.Width != width || header.Height != height)
                        throw new FileFormatException("ICO directory and PNG dimensions differ.");
                }
                else
                {
                    if (payload.Length < 40) throw new FileFormatException("Incomplete portable ICO DIB.");
                    if (BinaryPrimitives.ReadUInt32LittleEndian(payload) != 40 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14)) != 32 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16)) != 0 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32)) != 0)
                        throw UnsupportedPortableDecoder("ICO DIB headers outside 32-bit BI_RGB");
                    if (BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)) != width ||
                        BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8)) != 2 * height ||
                        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12)) != 1 ||
                        payload.Length != 40 + width * height * 4 + ((width + 31) / 32) * 4 * height)
                        throw new FileFormatException("Invalid portable ICO DIB dimensions or pixel/mask length.");
                }
                byte[] single = new byte[checked(22 + payload.Length)];
                bytes.AsSpan(0, 6).CopyTo(single);
                BinaryPrimitives.WriteUInt16LittleEndian(single.AsSpan(4), 1);
                entry.CopyTo(single.AsSpan(6));
                BinaryPrimitives.WriteUInt32LittleEndian(single.AsSpan(18), 22);
                payload.CopyTo(single.AsSpan(22));
                using SKData data = SKData.CreateCopy(single);
                using SKCodec codec = SKCodec.Create(data) ?? throw new FileFormatException("Invalid portable ICO pixels.");
                if (codec.EncodedFormat != SKEncodedImageFormat.Ico || codec.Info.Width != width || codec.Info.Height != height)
                    throw new FileFormatException("Portable ICO decoded dimensions differ.");
                frames.Add((PortablePngDecoder.DecodePixels(codec, PixelFormats.Bgra32, colorManage: false), 96, 96));
            }
            return frames;
        }
    }
}
#endif
