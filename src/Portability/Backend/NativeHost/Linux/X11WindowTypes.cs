namespace Wpf.Portable.NativeHost.Linux;

internal enum X11WindowState { Normal, Minimized, Maximized, FullScreen }
internal enum X11WindowDecorations { None, Border, Full }
internal readonly record struct X11PixelRect(int X, int Y, int Width, int Height)
{
    public long Right => (long)X + Width;
    public long Bottom => (long)Y + Height;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
}
internal readonly record struct X11FrameExtents(int Left, int Top, int Right, int Bottom);
internal readonly record struct X11WindowMetrics(int ClientWidth, int ClientHeight, int ClientX, int ClientY,
    X11FrameExtents FrameExtents, X11WindowState State);
internal sealed class X11WindowOptions
{
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    public int X { get; init; }
    public int Y { get; init; }
    public bool Popup { get; init; }
    public bool Splash { get; init; }
    public bool Transparent { get; init; }
    public X11Window? Owner { get; init; }
}
internal readonly record struct X11Screen(nuint Id, string Name, bool IsPrimary, X11PixelRect Bounds,
    X11PixelRect WorkingArea, double Dpi);
