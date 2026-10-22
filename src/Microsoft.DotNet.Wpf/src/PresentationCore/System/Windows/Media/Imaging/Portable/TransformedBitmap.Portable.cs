// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
namespace System.Windows.Media.Imaging
{
    public sealed partial class TransformedBitmap
    {
        private void FinalizePortableCreation()
        {
            _bitmapInit.EnsureInitializedComplete();
            Matrix transform = Transform.Value;
            if (_source.PortableBuffer == null || transform.M12 != 0 || transform.M21 != 0 ||
                !double.IsFinite(transform.M11) || !double.IsFinite(transform.M22) || transform.M11 == 0 || transform.M22 == 0)
                throw new NotSupportedException("Portable TransformedBitmap supports owned pixels with finite axis scaling and mirrors.");
            double scaledWidth = Math.Abs(transform.M11) * _source.PixelWidth;
            double scaledHeight = Math.Abs(transform.M22) * _source.PixelHeight;
            if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) || scaledWidth + 0.5 > int.MaxValue || scaledHeight + 0.5 > int.MaxValue)
                throw new NotSupportedException("Portable TransformedBitmap dimensions exceed owned storage limits.");
            int width = Math.Max(1, (int)(scaledWidth + 0.5)), height = Math.Max(1, (int)(scaledHeight + 0.5));
            InstallPortableBuffer(_source.PortableBuffer.Transform(width, height, transform.M11 < 0, transform.M22 < 0), _source.DpiX, _source.DpiY);
        }
    }
}
#endif
