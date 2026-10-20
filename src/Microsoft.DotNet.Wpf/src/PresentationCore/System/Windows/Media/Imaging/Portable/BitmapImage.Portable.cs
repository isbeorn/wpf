// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.IO;
using System.Net;
using System.Windows.Navigation;
using MS.Internal;
using MS.Internal.PresentationCore;

namespace System.Windows.Media.Imaging
{
    public sealed partial class BitmapImage
    {
        private void FinalizePortableCreation()
        {
            _bitmapInit.EnsureInitializedComplete();
            if (!SourceRect.IsEmpty || Rotation != Rotation.Rotate0 ||
                (CreateOptions != BitmapCreateOptions.None && CreateOptions != BitmapCreateOptions.PreservePixelFormat) || UriCachePolicy != null ||
                (CacheOption != BitmapCacheOption.Default && CacheOption != BitmapCacheOption.OnLoad))
                throw UnsupportedPortableImage("crop, decode sizing, rotation, create options or cache policy");

            (PortableBitmapBuffer Buffer, double DpiX, double DpiY) decoded;
            if (StreamSource != null)
            {
                if (UriSource != null || CacheOption != BitmapCacheOption.OnLoad)
                    throw UnsupportedPortableImage("StreamSource without exclusive OnLoad ownership");
                decoded = DecodePortableInput(StreamSource);
            }
            else
            {
                Uri uri = _baseUri == null ? UriSource : new Uri(_baseUri, UriSource);
                if (_xpsPackageOrigin != null && !XpsLoadingContext.IsUriAllowedAgainstPackage(_xpsPackageOrigin, uri))
                    throw new FileFormatException(SR.Resource_XpsPackageBoundaryViolation);
                if (uri != null && uri.IsAbsoluteUri && uri.IsFile && !uri.IsUnc && CacheOption == BitmapCacheOption.OnLoad)
                {
                    using Stream stream = File.OpenRead(uri.LocalPath);
                    decoded = DecodePortableInput(stream);
                }
                else
                {
                    if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != "pack" || !BaseUriHelper.IsPackApplicationUri(uri))
                        throw UnsupportedPortableImage("URI sources outside the local application pack or eager local files");
                    using (WebResponse response = WpfWebRequestHelper.CreateRequestAndGetResponse(uri))
                    using (Stream stream = response.GetResponseStream())
                        decoded = DecodePortableInput(stream);
                }
            }
            if (DecodePixelWidth != 0 || DecodePixelHeight != 0)
            {
                int width = DecodePixelWidth == 0 ? Math.Max(1, checked((int)((long)decoded.Buffer.Width * DecodePixelHeight / decoded.Buffer.Height))) : DecodePixelWidth;
                int height = DecodePixelHeight == 0 ? Math.Max(1, checked((int)((long)decoded.Buffer.Height * DecodePixelWidth / decoded.Buffer.Width))) : DecodePixelHeight;
                decoded.Buffer = decoded.Buffer.Transform(width, height, flipX: false, flipY: false);
            }
            InstallPortableBuffer(decoded.Buffer, decoded.DpiX, decoded.DpiY);
        }

        private (PortableBitmapBuffer Buffer, double DpiX, double DpiY) DecodePortableInput(Stream stream)
        {
            using var encoded = new MemoryStream(); stream.CopyTo(encoded); byte[] bytes = encoded.ToArray();
            bool preserve = CreateOptions == BitmapCreateOptions.PreservePixelFormat;
            if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)
                return PortableJpegDecoder.Decode(bytes, preserve, colorManage: true);
            return PortablePngDecoder.Decode(bytes, colorManage: true, preservePixelFormat: preserve);
        }

        private static NotSupportedException UnsupportedPortableImage(string feature) =>
            new NotSupportedException("Portable BitmapImage does not support " + feature + ".");
    }
}
#endif
