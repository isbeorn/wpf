// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal;
using MS.Win32.PresentationCore;
using System.Windows.Media.Composition;
#if PORTABLE_WPF
using MS.Internal.Portability.Rendering;
using SkiaSharp;
#endif

namespace System.Windows.Media.Imaging
{
    #region RenderTargetBitmap
    /// <summary>
    /// RenderTargetBitmap provides caching functionality for a BitmapSource.
    /// </summary>
    public sealed class RenderTargetBitmap : System.Windows.Media.Imaging.BitmapSource
    {
        /// <summary>
        /// RenderTargetBitmap - used in conjuction with BitmapVisualManager
        /// </summary>
        /// <param name="pixelWidth">The width of the Bitmap</param>
        /// <param name="pixelHeight">The height of the Bitmap</param>
        /// <param name="dpiX">Horizontal DPI of the Bitmap</param>
        /// <param name="dpiY">Vertical DPI of the Bitmap</param>
        /// <param name="pixelFormat">Format of the Bitmap.</param>
        public RenderTargetBitmap(
            int pixelWidth,
            int pixelHeight,
            double dpiX,
            double dpiY,
            PixelFormat pixelFormat
            ) : base(true)
        {
            if (pixelFormat.Format == PixelFormatEnum.Default)
            {
                pixelFormat = PixelFormats.Pbgra32;
            }
            else if (pixelFormat.Format != PixelFormatEnum.Pbgra32)
            {
                throw new System.ArgumentException(
                        SR.Format(SR.Effect_PixelFormat, pixelFormat),
                        nameof(pixelFormat)
                        );
            }

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

            if (dpiX < DoubleUtil.DBL_EPSILON)
            {
                dpiX = 96.0;
            }

            if (dpiY < DoubleUtil.DBL_EPSILON)
            {
                dpiY = 96.0;
            }

            _bitmapInit.BeginInit();
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            _dpiX = dpiX;
            _dpiY = dpiY;
            _format = pixelFormat;
            _bitmapInit.EndInit();
            FinalizeCreation();
        }

        /// <summary>
        /// Internal ctor
        /// </summary>
        internal RenderTargetBitmap() :
            base(true)
        {
        }

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.CreateInstanceCore">Freezable.CreateInstanceCore</see>.
        /// </summary>
        /// <returns>The new Freezable.</returns>
        protected override Freezable CreateInstanceCore()
        {
            return new RenderTargetBitmap();
        }

        /// <summary>
        /// Common Copy method used to implement CloneCore(), CloneCurrentValueCore(),
        /// GetAsFrozenCore(), and GetCurrentValueAsFrozenCore()
        /// </summary>
        private void CopyCommon(RenderTargetBitmap sourceBitmap)
        {
#if !PORTABLE_WPF
            _bitmapInit.BeginInit();
            _pixelWidth = sourceBitmap._pixelWidth;
            _pixelHeight = sourceBitmap._pixelHeight;
            _dpiX = sourceBitmap._dpiX;
            _dpiY = sourceBitmap._dpiY;
            _format = sourceBitmap._format;

            //
            // In order to make a deep clone we need to
            // create a new bitmap with the contents of the
            // existing bitmap and then create a render target
            // from the new bitmap.
            //
            using (FactoryMaker myFactory = new FactoryMaker())
            {
                // Create an IWICBitmap
                BitmapSourceSafeMILHandle newBitmapHandle = BitmapSource.CreateCachedBitmap(
                    null,
                    WicSourceHandle,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad,
                    null);

                lock (_syncObject)
                {
                    WicSourceHandle = newBitmapHandle;
                }

                // Now create a Render target from that Bitmap
                HRESULT.Check(UnsafeNativeMethods.MILFactory2.CreateBitmapRenderTargetForBitmap(
                    myFactory.FactoryPtr,
                    newBitmapHandle,
                    out _renderTargetBitmap
                    ));
            }

            _bitmapInit.EndInit();
#endif
        }

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.CloneCore(Freezable)">Freezable.CloneCore</see>.
        /// </summary>
        protected override void CloneCore(Freezable sourceFreezable)
        {
            RenderTargetBitmap sourceBitmap = (RenderTargetBitmap)sourceFreezable;

            base.CloneCore(sourceFreezable);

            CopyCommon(sourceBitmap);
        }

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.CloneCurrentValueCore(Freezable)">Freezable.CloneCurrentValueCore</see>.
        /// </summary>
        protected override void CloneCurrentValueCore(Freezable sourceFreezable)
        {
            RenderTargetBitmap sourceBitmap = (RenderTargetBitmap)sourceFreezable;

            base.CloneCurrentValueCore(sourceFreezable);

            CopyCommon(sourceBitmap);
        }

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.GetAsFrozenCore(Freezable)">Freezable.GetAsFrozenCore</see>.
        /// </summary>
        protected override void GetAsFrozenCore(Freezable sourceFreezable)
        {
            RenderTargetBitmap sourceBitmap = (RenderTargetBitmap)sourceFreezable;

            base.GetAsFrozenCore(sourceFreezable);

            CopyCommon(sourceBitmap);
        }

        /// <summary>
        /// Implementation of <see cref="System.Windows.Freezable.GetCurrentValueAsFrozenCore(Freezable)">Freezable.GetCurrentValueAsFrozenCore</see>.
        /// </summary>
        protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
        {
            RenderTargetBitmap sourceBitmap = (RenderTargetBitmap)sourceFreezable;

            base.GetCurrentValueAsFrozenCore(sourceFreezable);

            CopyCommon(sourceBitmap);
        }


        /// <summary>
        /// Renders the specified Visual tree to the BitmapRenderTarget.
        /// </summary>
        public void Render(Visual visual)
        {
#if PORTABLE_WPF
            if (IsFrozen)
                throw new System.ArgumentException(SR.Image_CantBeFrozen);
            WritePreamble();
            System.ArgumentNullException.ThrowIfNull(visual);
            if (!double.IsFinite(_dpiX) || !double.IsFinite(_dpiY))
                throw new System.NotSupportedException("Portable RenderTargetBitmap rendering requires finite DPI.");
            using (var target = new PortableRenderTarget(visual, _pixelWidth, _pixelHeight, new DpiScale(_dpiX / 96, _dpiY / 96)))
            {
                target.Render();
                using SKBitmap rendered = target.Snapshot();
                using SKBitmap combined = PortableBuffer.CreateDisplayBitmap();
                using (var canvas = new SKCanvas(combined))
                {
                    canvas.DrawBitmap(rendered, 0, 0);
                    canvas.Flush();
                }
                PortableBuffer.Write(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), combined.GetPixels(),
                    checked((uint)(combined.RowBytes * combined.Height)), combined.RowBytes, 0, 0);
                PortableBuffer.Commit();
            }
            RenderTargetContentsChanged();
#else
            BitmapVisualManager bmv = new BitmapVisualManager(this);
            bmv.Render(visual); // Render indirectly calls RenderTargetContentsChanged();
#endif
        }

        /// <summary>
        /// Clears the render target and sets every pixel to black transparent
        /// </summary>
        public void Clear()
        {
#if PORTABLE_WPF
            unsafe
            {
                new System.Span<byte>((void*)PortableBuffer.BackBuffer, checked((int)PortableBuffer.Size)).Clear();
            }
            PortableBuffer.Commit();
#else
            HRESULT.Check(MILRenderTargetBitmap.Clear(_renderTargetBitmap));
#endif
            RenderTargetContentsChanged();
        }

        ///
        /// Get the MIL RenderTarget
        ///
        internal SafeMILHandle MILRenderTarget
        {
            get
            {
                return _renderTargetBitmap;
            }
        }

        ///
        /// Notify that the contents of the render target have changed
        ///
        internal void RenderTargetContentsChanged()
        {
#if !PORTABLE_WPF
            // If the render target has changed, we need to update the UCE resource.  We ensure
            // this happens by throwing away our reference to the previous DUCE bitmap source
            // and forcing the creation of a new one.
            _isSourceCached = false;
            _convertedDUCEPtr?.Close();
            _convertedDUCEPtr = null;

            // Register for update in the next render pass
            RegisterForAsyncUpdateResource();
#endif
            FireChanged();
        }

        ///
        /// Create the unmanaged resources
        ///
        internal override void FinalizeCreation()
        {
#if PORTABLE_WPF
            InstallPortableBuffer(new PortableBitmapBuffer(_pixelWidth, _pixelHeight, _format, mutable: false), (float)_dpiX, (float)_dpiY);
#else
            try
            {
                using (FactoryMaker myFactory = new FactoryMaker())
                {
                    SafeMILHandle renderTargetBitmap = null;
                    HRESULT.Check(UnsafeNativeMethods.MILFactory2.CreateBitmapRenderTarget(
                        myFactory.FactoryPtr,
                        (uint)_pixelWidth,
                        (uint)_pixelHeight,
                        _format.Format,
                        (float)_dpiX,
                        (float)_dpiY,
                        MILRTInitializationFlags.MIL_RT_INITIALIZE_DEFAULT,
                        out renderTargetBitmap));

                    Debug.Assert(renderTargetBitmap != null && !renderTargetBitmap.IsInvalid);

                    BitmapSourceSafeMILHandle bitmapSource = null;
                    HRESULT.Check(MILRenderTargetBitmap.GetBitmap(
                        renderTargetBitmap,
                        out bitmapSource));
                    Debug.Assert(bitmapSource != null && !bitmapSource.IsInvalid);

                    lock (_syncObject)
                    {
                        _renderTargetBitmap = renderTargetBitmap;
                        bitmapSource.CalculateSize();
                        WicSourceHandle = bitmapSource;

                        // For the purpose of rendering a RenderTargetBitmap, we always treat it as if it's
                        // not cached.  This is to ensure we never render and write to the same bitmap source
                        // by the UCE thread and managed thread.
                        _isSourceCached = false;
                    }
                }

                CreationCompleted = true;
                UpdateCachedSettings();
            }
            catch
            {
                _bitmapInit.Reset();
                throw;
            }
#endif
}

        private SafeMILHandle /* IMILRenderTargetBitmap */ _renderTargetBitmap = null;
}
    #endregion // RenderTargetBitmap
}
