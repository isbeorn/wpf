// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
            if (format != PixelFormats.Gray8 && format != PixelFormats.Gray16 && format != PixelFormats.Gray32Float && format != PixelFormats.Rgb48 &&
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
            bool grayFloat = Format == PixelFormats.Gray32Float;
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
                        if (grayFloat)
                            red = green = blue = EncodeGrayFloat(BitConverter.ToSingle(_front, offset));
                        else if (gray16)
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

        internal static byte EncodeGrayFloat(float value)
        {
            if (!float.IsFinite(value)) throw new NotSupportedException("Portable grayscale display requires finite linear samples.");
            double linear = Math.Clamp(value, 0, 1);
            double encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            return (byte)Math.Clamp((int)(encoded * 255 + 0.5), 0, 255);
        }

        internal unsafe PortableBitmapBuffer Transform(int width, int height, bool flipX, bool flipY)
        {
            if (width == Width && height == Height)
            {
                var copy = new PortableBitmapBuffer(width, height, Format, mutable: false);
                byte* output = (byte*)copy.BackBuffer;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    _front.AsSpan((flipY ? height - 1 - y : y) * Stride + (flipX ? width - 1 - x : x) * BytesPerPixel, BytesPerPixel)
                        .CopyTo(new Span<byte>(output + y * copy.Stride + x * BytesPerPixel, BytesPerPixel));
                return copy;
            }
            bool floating = Format == PixelFormats.Gray16 || Format == PixelFormats.Gray32Float;
            PixelFormat resultFormat = floating ? PixelFormats.Gray32Float : Format == PixelFormats.Rgb48 ? PixelFormats.Pbgra32 : Format;
            var result = new PortableBitmapBuffer(width, height, resultFormat, mutable: false);
            using SKBitmap source = floating ? CreateFloatBitmap() : CreateDisplayBitmap();
            var info = new SKImageInfo(width, height, floating ? SKColorType.RgbaF32 : SKColorType.Bgra8888,
                floating ? SKAlphaType.Opaque : SKAlphaType.Premul);
            using var premultiplied = new SKBitmap(source.Info.WithAlphaType(floating ? SKAlphaType.Opaque : SKAlphaType.Premul));
            using SKPixmap sourcePixels = source.PeekPixels();
            if (premultiplied.GetPixels() == IntPtr.Zero || !sourcePixels.ReadPixels(premultiplied.Info, premultiplied.GetPixels(), premultiplied.RowBytes, 0, 0))
                throw new InvalidOperationException("Portable scale staging storage could not be populated.");
            using var scaled = new SKBitmap(info);
            if (scaled.GetPixels() == IntPtr.Zero) throw new InvalidOperationException("Portable scaled storage could not be allocated.");
            Resample(premultiplied, scaled, floating);
            var readInfo = info.WithAlphaType(resultFormat == PixelFormats.Pbgra32 ? SKAlphaType.Premul : floating ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            using var pixels = new SKBitmap(readInfo);
            using SKPixmap scaledPixels = scaled.PeekPixels();
            if (pixels.GetPixels() == IntPtr.Zero || !scaledPixels.ReadPixels(readInfo, pixels.GetPixels(), pixels.RowBytes, 0, 0))
                throw new InvalidOperationException("Portable scaled samples could not be populated.");
            byte* input = (byte*)pixels.GetPixels(), destination = (byte*)result.BackBuffer;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                byte* sample = input + (long)(flipY ? height - 1 - y : y) * pixels.RowBytes + (long)(flipX ? width - 1 - x : x) * (floating ? 16 : 4);
                byte* target = destination + y * result.Stride + x * result.BytesPerPixel;
                if (floating) *(float*)target = *(float*)sample;
                else if (resultFormat == PixelFormats.Gray8) *target = sample[0];
                else if (resultFormat == PixelFormats.Rgb24) { target[0] = sample[2]; target[1] = sample[1]; target[2] = sample[0]; }
                else new ReadOnlySpan<byte>(sample, result.BytesPerPixel).CopyTo(new Span<byte>(target, result.BytesPerPixel));
            }
            return result;
        }

        private static unsafe void Resample(SKBitmap source, SKBitmap destination, bool floating)
        {
            var horizontal = AxisWeights(source.Width, destination.Width);
            var vertical = AxisWeights(source.Height, destination.Height);
            byte* input = (byte*)source.GetPixels(), output = (byte*)destination.GetPixels();
            int pixelSize = floating ? 16 : 4;
            for (int y = 0; y < destination.Height; y++) for (int x = 0; x < destination.Width; x++)
            {
                byte* target = output + (long)y * destination.RowBytes + (long)x * pixelSize;
                for (int channel = 0; channel < 4; channel++)
                {
                    double sum = 0;
                    foreach (var row in vertical[y]) foreach (var column in horizontal[x])
                    {
                        byte* sample = input + (long)row.Index * source.RowBytes + (long)column.Index * pixelSize;
                        sum += (floating ? ((float*)sample)[channel] : sample[channel]) * row.Weight * column.Weight;
                    }
                    if (floating) ((float*)target)[channel] = (float)sum;
                    else target[channel] = (byte)Math.Clamp((int)Math.Floor(sum + 0.5), 0, 255);
                }
            }
        }

        private static (int Index, double Weight)[][] AxisWeights(int source, int destination)
        {
            var result = new (int Index, double Weight)[destination][];
            double ratio = (double)source / destination;
            for (int i = 0; i < destination; i++)
            {
                if (destination >= source)
                {
                    double position = i * ratio; int first = (int)position; double fraction = position - first;
                    result[i] = fraction == 0 ? new[] { (first, 1d) } : new[] { (first, 1 - fraction), (Math.Min(first + 1, source - 1), fraction) };
                }
                else
                {
                    double start = i * ratio, end = (i + 1) * ratio;
                    var weights = new List<(int, double)>();
                    for (int index = (int)start; index < Math.Ceiling(end); index++)
                        weights.Add((Math.Min(index, source - 1), (Math.Min(end, index + 1) - Math.Max(start, index)) / ratio));
                    result[i] = weights.ToArray();
                }
            }
            return result;
        }

        private unsafe SKBitmap CreateFloatBitmap()
        {
            var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.RgbaF32, SKAlphaType.Opaque));
            try
            {
                byte* output = (byte*)bitmap.GetPixels();
                if (output == null) throw new InvalidOperationException("Portable floating scale storage could not be allocated.");
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                {
                    int offset = y * Stride + x * BytesPerPixel;
                    float value;
                    if (Format == PixelFormats.Gray32Float) value = BitConverter.ToSingle(_front, offset);
                    else
                    {
                        double encoded = BinaryPrimitives.ReadUInt16LittleEndian(_front.AsSpan(offset, 2)) / 65535d;
                        value = (float)(encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4));
                    }
                    float* pixel = (float*)(output + (long)y * bitmap.RowBytes + (long)x * 16);
                    pixel[0] = pixel[1] = pixel[2] = value; pixel[3] = 1;
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        internal unsafe SKBitmap CreateGrayBitmap()
        {
            var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Gray8, SKAlphaType.Opaque));
            try
            {
                byte* output = (byte*)bitmap.GetPixels();
                if (output == null) throw new InvalidOperationException("Portable grayscale staging storage could not be allocated.");
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                {
                    int offset = y * Stride + x * BytesPerPixel;
                    output[y * bitmap.RowBytes + x] = Format == PixelFormats.Gray8 ? _front[offset] :
                        Format == PixelFormats.Gray16 ? (byte)((BinaryPrimitives.ReadUInt16LittleEndian(_front.AsSpan(offset, 2)) + 128) / 257) :
                        EncodeGrayFloat(BitConverter.ToSingle(_front, offset));
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }
}
#endif
