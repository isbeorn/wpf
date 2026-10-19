using System.Runtime.InteropServices;
using static Wpf.Portable.NativeHost.Linux.X11WindowNative;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class X11Framebuffer(X11Window window) : IDisposable
{
    private byte[]? _retained;
    private int _retainedWidth, _retainedHeight;
    private nint _image, _gc;
    private int _width, _height, _stride;
    private bool _disposed;

    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int rowBytes;
        try { rowBytes = checked(width * 4); }
        catch (OverflowException error) { throw new ArgumentException("The BGRA frame width is invalid.", nameof(width), error); }
        if (width <= 0 || height <= 0 || stride < rowBytes || (long)stride * (height - 1) + rowBytes > pixels.Length)
            throw new ArgumentException("The premultiplied BGRA frame dimensions or stride are invalid.");
        var retained = new byte[checked(rowBytes * height)];
        for (int row = 0; row < height; row++) pixels.Slice(checked(row * stride), rowBytes).CopyTo(retained.AsSpan(checked(row * rowBytes), rowBytes));
        _retained = retained;
        _retainedWidth = width;
        _retainedHeight = height;
        Repaint();
    }
    public unsafe void Repaint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retained is null) return;
        var metrics = window.ReadMetrics();
        EnsureImage(metrics.ClientWidth, metrics.ClientHeight);
        var native = Marshal.PtrToStructure<Image>(_image);
        var destination = new Span<byte>((void*)native.Data, checked(_stride * _height));
        destination.Clear();
        int copyBytes = checked(Math.Min(_retainedWidth, _width) * 4);
        int copyRows = Math.Min(_retainedHeight, _height);
        for (int row = 0; row < copyRows; row++)
            _retained.AsSpan(checked(row * _retainedWidth * 4), copyBytes).CopyTo(destination.Slice(checked(row * _stride), copyBytes));
        XPutImage(window.Connection.Display, window.Handle, _gc, _image, 0, 0, 0, 0, (uint)_width, (uint)_height);
        window.Connection.Synchronize();
    }
    private void EnsureImage(int width, int height)
    {
        if (_image != 0 && _width == width && _height == height) return;
        if (_image != 0) { XDestroyImage(_image); _image = 0; }
        if (_gc == 0)
        {
            _gc = XCreateGC(window.Connection.Display, window.Handle, 0, 0);
            if (_gc == 0) throw new InvalidOperationException("XCreateGC failed for the CPU framebuffer.");
        }
        int allocationSize = checked(width * height * 4);
        nint data = Malloc((nuint)allocationSize);
        if (data == 0) throw new InvalidOperationException("Cannot allocate the native X11 framebuffer.");
        nint image = 0;
        try
        {
            image = XCreateImage(window.Connection.Display, window.Visual, (uint)window.Depth, 2, 0, data,
                (uint)width, (uint)height, 32, checked(width * 4));
            if (image == 0) throw new InvalidOperationException("XCreateImage failed for the CPU framebuffer.");
            // XCreateImage takes data ownership only when it returns an image.
            data = 0;
            var layout = Marshal.PtrToStructure<Image>(image);
            if (layout.Data == 0 || layout.Format != 2 || layout.Width != width || layout.Height != height ||
                layout.XOffset != 0 || layout.ByteOrder != 0 || layout.BitsPerPixel != 32 || layout.BitmapPad != 32 ||
                layout.BytesPerLine != checked(width * 4) || layout.Depth != window.Depth ||
                layout.RedMask != 0xff0000 || layout.GreenMask != 0xff00 || layout.BlueMask != 0xff)
                throw new PlatformNotSupportedException("X11 framebuffer upload requires little-endian 32-bit BGRA pixels with standard RGB masks.");
            _image = image;
            image = 0;
            _width = width;
            _height = height;
            _stride = layout.BytesPerLine;
        }
        finally
        {
            if (image != 0) XDestroyImage(image);
            if (data != 0) Free(data);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_image != 0) { XDestroyImage(_image); _image = 0; }
        if (_gc != 0) { XFreeGC(window.Connection.Display, _gc); _gc = 0; }
        _retained = null;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Image
    {
        public int Width, Height, XOffset, Format;
        public nint Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
        public nuint RedMask, GreenMask, BlueMask;
    }
    [DllImport(Xlib)] private static extern nint XCreateImage(nint display, nint visual, uint depth, int format,
        int offset, nint data, uint width, uint height, int bitmapPad, int bytesPerLine);
    [DllImport(Xlib)] private static extern int XDestroyImage(nint image);
    [DllImport(Xlib)] private static extern nint XCreateGC(nint display, nuint drawable, nuint mask, nint values);
    [DllImport(Xlib)] private static extern int XFreeGC(nint display, nint gc);
    [DllImport(Xlib)] private static extern int XPutImage(nint display, nuint drawable, nint gc, nint image,
        int sourceX, int sourceY, int destinationX, int destinationY, uint width, uint height);
    [DllImport("libc.so.6", EntryPoint = "malloc")] private static extern nint Malloc(nuint size);
    [DllImport("libc.so.6", EntryPoint = "free")] private static extern void Free(nint data);
}
