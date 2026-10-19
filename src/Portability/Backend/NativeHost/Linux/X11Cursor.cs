extern alias WpfFork;

using System.Runtime.InteropServices;
using WpfCursor = WpfFork::System.Windows.Input.Cursor;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class X11Cursor(X11Window window) : IDisposable
{
    private readonly Dictionary<string, nuint> _cursors = new(StringComparer.Ordinal);
    private string _shape = "left_ptr";
    private bool _visible = true;
    private bool _disposed;

    internal bool SetCursor(WpfCursor cursor)
    {
        window.Connection.VerifyOwner();
        if (_disposed) return false;
        string? shape = cursor?.CursorType.ToString() switch
        {
            null or "Arrow" => "left_ptr", "None" => "", "Hand" => "hand2",
            "IBeam" => "xterm", "Cross" => "crosshair", "Wait" => "watch",
            "SizeAll" => "fleur", "SizeNS" => "sb_v_double_arrow", "SizeWE" => "sb_h_double_arrow",
            "SizeNWSE" => "top_left_corner", "SizeNESW" => "top_right_corner", "No" => "crossed_circle",
            "Help" => "question_arrow", "AppStarting" => "left_ptr_watch", "UpArrow" => "center_ptr", _ => null
        };
        if (shape == null) return false;
        if (!Apply(_visible ? shape : "")) return false;
        _shape = shape;
        return true;
    }

    internal bool SetVisibility(bool visible)
    {
        window.Connection.VerifyOwner();
        if (_disposed || !Apply(visible ? _shape : "")) return false;
        _visible = visible;
        return true;
    }

    private bool Apply(string shape)
    {
        var display = window.Connection.Display;
        if (!_cursors.TryGetValue(shape, out var cursor))
        {
            if (shape.Length != 0) cursor = XcursorLibraryLoadCursor(display, shape);
            else
            {
                var bitmap = XCreateBitmapFromData(display, window.Connection.RootWindow, new byte[1], 1, 1);
                if (bitmap == 0) return false;
                try
                {
                    XColor color = default;
                    cursor = XCreatePixmapCursor(display, bitmap, bitmap, ref color, ref color, 0, 0);
                }
                finally { XFreePixmap(display, bitmap); }
            }
            if (cursor == 0) return false;
            _cursors.Add(shape, cursor);
        }
        XDefineCursor(display, window.Handle, cursor);
        window.Connection.Flush();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        window.Connection.VerifyOwner();
        _disposed = true;
        foreach (var cursor in _cursors.Values) XFreeCursor(window.Connection.Display, cursor);
        _cursors.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XColor { internal nuint Pixel; internal ushort Red, Green, Blue; internal byte Flags, Pad; }
    [DllImport("libXcursor.so.1")] private static extern nuint XcursorLibraryLoadCursor(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libX11.so.6")] private static extern nuint XCreateBitmapFromData(nint display, nuint drawable, byte[] data, uint width, uint height);
    [DllImport("libX11.so.6")] private static extern nuint XCreatePixmapCursor(nint display, nuint source, nuint mask, ref XColor foreground, ref XColor background, uint x, uint y);
    [DllImport("libX11.so.6")] private static extern int XFreePixmap(nint display, nuint pixmap);
    [DllImport("libX11.so.6")] private static extern int XDefineCursor(nint display, nuint window, nuint cursor);
    [DllImport("libX11.so.6")] private static extern int XFreeCursor(nint display, nuint cursor);
}
