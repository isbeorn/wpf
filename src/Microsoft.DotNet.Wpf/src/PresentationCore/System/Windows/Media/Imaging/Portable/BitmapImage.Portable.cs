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
            if (!SourceRect.IsEmpty || DecodePixelWidth != 0 || DecodePixelHeight != 0 || Rotation != Rotation.Rotate0 ||
                CreateOptions != BitmapCreateOptions.None || UriCachePolicy != null ||
                (CacheOption != BitmapCacheOption.Default && CacheOption != BitmapCacheOption.OnLoad))
                throw UnsupportedPortableImage("crop, decode sizing, rotation, create options or cache policy");

            (PortableBitmapBuffer Buffer, double DpiX, double DpiY) decoded;
            if (StreamSource != null)
            {
                if (UriSource != null || CacheOption != BitmapCacheOption.OnLoad)
                    throw UnsupportedPortableImage("StreamSource without exclusive OnLoad ownership");
                decoded = PortablePngDecoder.Decode(StreamSource, colorManage: true);
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
                    decoded = PortablePngDecoder.Decode(stream, colorManage: true);
            }
            InstallPortableBuffer(decoded.Buffer, decoded.DpiX, decoded.DpiY);
        }

        private static NotSupportedException UnsupportedPortableImage(string feature) =>
            new NotSupportedException("Portable BitmapImage does not support " + feature + ".");
    }
}
#endif
