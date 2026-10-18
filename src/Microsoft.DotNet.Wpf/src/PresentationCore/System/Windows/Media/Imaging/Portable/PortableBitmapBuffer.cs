// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Buffers.Binary;
using SkiaSharp;

namespace System.Windows.Media.Imaging
{
    internal sealed class PortableBitmapBuffer
    {
        private readonly byte[] _pixels;
        private byte[] _front;
        private bool _frozen;
        internal int Width { get; }
        internal int Height { get; }
        internal int Stride { get; }
        internal PixelFormat Format { get; }
        internal int BytesPerPixel => Format.BitsPerPixel / 8;
        internal uint Size => checked((uint)_pixels.Length);

        internal PortableBitmapBuffer(int width, int height, PixelFormat format, bool mutable)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (format != PixelFormats.Gray8 && format != PixelFormats.Gray16 && format != PixelFormats.Rgb48 &&
                format != PixelFormats.Bgra32 && format != PixelFormats.Pbgra32 && format != PixelFormats.Bgr32 &&
                format != PixelFormats.Bgr24 && format != PixelFormats.Rgb24)
                throw new NotSupportedException($"Portable bitmap storage does not support pixel format {format}.");
            Width = width;
            Height = height;
            Format = format;
            Stride = checked((checked(width * BytesPerPixel) + 3) & ~3);
            _pixels = GC.AllocateArray<byte>(checked(Stride * height), pinned: true);
            _front = mutable ? new byte[_pixels.Length] : _pixels;
        }

        internal unsafe IntPtr BackBuffer
        {
            get { fixed (byte* pixels = _pixels) return (IntPtr)pixels; }
        }

        internal PortableBitmapBuffer Clone(bool mutable)
        {
            var result = new PortableBitmapBuffer(Width, Height, Format, mutable);
            (_frozen ? _front : _pixels).CopyTo(result._pixels, 0);
            result.Commit();
            return result;
        }

        internal void Commit()
        {
            if (!ReferenceEquals(_pixels, _front))
                _pixels.CopyTo(_front, 0);
        }

        internal void Freeze()
        {
            _front = (byte[])_pixels.Clone();
            _frozen = true;
        }

        internal unsafe void CopyTo(Int32Rect rectangle, IntPtr destination, uint size, int stride)
        {
            ValidateRectangle(rectangle);
            int rowBytes = checked(rectangle.Width * BytesPerPixel);
            ValidateBuffer(size, stride, rowBytes, rectangle.Height);
            for (int row = 0; row < rectangle.Height; row++)
                (_frozen ? _front : _pixels).AsSpan(checked((rectangle.Y + row) * Stride + rectangle.X * BytesPerPixel), rowBytes)
                    .CopyTo(new Span<byte>((byte*)destination + checked(row * stride), rowBytes));
        }

        internal unsafe void Write(Int32Rect source, IntPtr pixels, uint size, int stride, int x, int y)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(source.X);
            ArgumentOutOfRangeException.ThrowIfNegative(source.Y);
            int rowBytes = checked(source.Width * BytesPerPixel);
            ValidateBuffer(size, stride, checked((source.X + source.Width) * BytesPerPixel), checked(source.Y + source.Height));
            ValidateRectangle(new Int32Rect(x, y, source.Width, source.Height));
            for (int row = 0; row < source.Height; row++)
                new ReadOnlySpan<byte>((byte*)pixels + checked((source.Y + row) * stride + source.X * BytesPerPixel), rowBytes)
                    .CopyTo(_pixels.AsSpan(checked((y + row) * Stride + x * BytesPerPixel), rowBytes));
        }

        private void ValidateRectangle(Int32Rect rectangle)
        {
            if (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Width < 0 || rectangle.Height < 0 ||
                rectangle.X > Width - rectangle.Width || rectangle.Y > Height - rectangle.Height)
                throw new ArgumentOutOfRangeException(nameof(rectangle));
        }

        private static void ValidateBuffer(uint size, int stride, int rowBytes, int height)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(stride, rowBytes);
            if (size < checked((long)stride * (height - 1) + rowBytes))
                throw new ArgumentException("The pixel buffer is too small for the requested rows.", nameof(size));
        }

        internal unsafe SKBitmap CreateDisplayBitmap()
        {
            int minimumStride = checked(Width * 4);
            int bytesPerPixel = BytesPerPixel;
            bool gray16 = Format == PixelFormats.Gray16;
            bool gray8 = Format == PixelFormats.Gray8;
            bool rgb48 = Format == PixelFormats.Rgb48;
            bool rgb24 = Format == PixelFormats.Rgb24;
            bool premultiplied = Format == PixelFormats.Pbgra32;
            bool hasAlpha = Format == PixelFormats.Bgra32 || premultiplied;
            var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888,
                premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul));
            try
            {
                byte* output = (byte*)bitmap.GetPixels();
                int destinationRowBytes = bitmap.RowBytes;
                if (output == null || destinationRowBytes < minimumStride)
                    throw new InvalidOperationException("The portable display bitmap could not be allocated.");
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int offset = y * Stride + x * bytesPerPixel;
                        byte red, green, blue, alpha = 255;
                        if (gray16)
                        {
                            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_front.AsSpan(offset, 2));
                            red = green = blue = (byte)((value + 128) / 257);
                        }
                        else if (gray8)
                            red = green = blue = _front[offset];
                        else if (rgb48)
                        {
                            red = _front[offset + 1]; green = _front[offset + 3]; blue = _front[offset + 5];
                        }
                        else if (rgb24)
                        {
                            red = _front[offset]; green = _front[offset + 1]; blue = _front[offset + 2];
                        }
                        else
                        {
                            blue = _front[offset]; green = _front[offset + 1]; red = _front[offset + 2];
                            if (hasAlpha)
                                alpha = _front[offset + 3];
                        }
                        byte* destination = output + checked((long)y * destinationRowBytes + (long)x * 4);
                        destination[0] = blue; destination[1] = green; destination[2] = red; destination[3] = alpha;
                    }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }
}
#endif
