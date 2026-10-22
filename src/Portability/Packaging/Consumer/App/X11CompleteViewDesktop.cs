using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PackageConsumer;

internal sealed class X11CompleteViewDesktop(X11Fixture native) : ICompleteViewDesktop
{
    public bool IsMapped(Window window) => native.IsMapped(native.FindWindow(window.Title));
    public void Focus(Window window) => native.Focus(native.FindWindow(window.Title));
    public void Click(Window window, FrameworkElement element)
    {
        var point = ClientPoint(window, element, element.ActualWidth / 2, element.ActualHeight / 2);
        native.Click(native.FindWindow(window.Title), point.X, point.Y);
    }
    public void ReplaceText(Window window, TextBox editor, string text)
    {
        try { native.Key('\uffe3', true); native.Type('a'); }
        finally { native.Key('\uffe3', false); }
        foreach (char character in text) native.Type(character);
    }
    public uint Pixel(Window window, FrameworkElement element, double x, double y)
    {
        var point = ClientPoint(window, element, x, y);
        return native.Pixel(native.FindWindow(window.Title), point.X, point.Y);
    }
    public void SetDpi(int dpi) => native.SetDpi(dpi);
    public (int Width, int Height) ClientSize(Window window) => native.Size(native.FindWindow(window.Title));
    private static (int X, int Y) ClientPoint(Window window, FrameworkElement element, double x, double y)
    {
        Point logical = element.TransformToAncestor(window).Transform(new Point(x, y));
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        return ((int)Math.Round(logical.X * dpi.DpiScaleX), (int)Math.Round(logical.Y * dpi.DpiScaleY));
    }
}
