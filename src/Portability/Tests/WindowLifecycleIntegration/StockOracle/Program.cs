using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var checks = new List<object>();
        bool failed = false;
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool increase in new[] { true, false })
        {
            Window? window = null;
            var snapshots = new List<object>();
            try
            {
                window = new Window { Title = "WPF stock restore oracle", Left = 160, Top = 140, Width = 400, Height = 300, ShowActivated = false };
                window.Show(); Drain(); snapshots.Add(Capture("normal", window));
                window.WindowState = state; Drain(); snapshots.Add(Capture("state", window));
                Rect stateBounds = NativeBounds(window);
                double width = increase ? 520 : 280, height = increase ? 380 : 220;
                double left = increase ? 200 : 120, top = increase ? 170 : 100;
                window.Width = width; window.Height = height; window.Left = left; window.Top = top;
                Drain(); snapshots.Add(Capture("edited", window));
                Assert(NativeBounds(window) == stateBounds, "Editing restore bounds changed the displayed state bounds.");
                Rect restore = window.RestoreBounds;
                Assert(Near(restore.Width, width) && Near(restore.Height, height) && Near(restore.Left, left) && Near(restore.Top, top), "RestoreBounds did not retain edits on all axes.");
                window.WindowState = WindowState.Normal; Drain(); snapshots.Add(Capture("restored", window));
                Assert(Near(window.Width, width) && Near(window.Height, height) && Near(window.Left, left) && Near(window.Top, top), "Normal state did not apply edited restore bounds.");
                checks.Add(new { name = state + (increase ? "-increase" : "-decrease"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true;
                checks.Add(new { name = state + (increase ? "-increase" : "-decrease"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { window?.Close(); }
        }
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool large in new[] { false, true })
        {
            var constrained = new Window { Title = "WPF restore constraint oracle", Width = 400, Height = 300,
                MinWidth = 200, MinHeight = 160, MaxWidth = 600, MaxHeight = 500, ShowActivated = false };
            var snapshots = new List<object>();
            try
            {
                constrained.Show(); Drain(); constrained.WindowState = state; Drain();
                constrained.Width = large ? 800 : 100; constrained.Height = large ? 650 : 80;
                snapshots.Add(Capture("edited-constrained-state", constrained));
                constrained.WindowState = WindowState.Normal; Drain(); snapshots.Add(Capture("restored-constrained", constrained));
                Assert(Near(constrained.Width, large ? 600 : 200) && Near(constrained.Height, large ? 500 : 160), "Restored size did not respect min/max constraints.");
                checks.Add(new { name = state + (large ? "-above-maximum" : "-below-minimum"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true; checks.Add(new { name = state + (large ? "-above-maximum" : "-below-minimum"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { constrained.Close(); }
        }
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        {
            var external = new Window { Title = "WPF external state visibility oracle", Width = 400, Height = 300 };
            var snapshots = new List<object>();
            try
            {
                external.Show(); Drain();
                IntPtr handle = new WindowInteropHelper(external).Handle;
                ShowWindow(handle, state == WindowState.Maximized ? 3 : 6); Drain();
                snapshots.Add(Capture("external-state", external));
                Assert(external.WindowState == state, "WPF did not observe the external native state.");
                Assert(Near(external.Width, 400) && Near(external.Height, 300), "Externally observed state did not expose restore dimensions.");
                external.Hide(); Drain(); snapshots.Add(Capture("hidden", external));
                external.Show(); Drain(); snapshots.Add(Capture("reshown", external));
                Assert(external.WindowState == state && (state == WindowState.Maximized ? IsZoomed(handle) : IsIconic(handle)),
                    "Hide/Show did not preserve an externally selected native window state.");
                checks.Add(new { name = "external-" + state + "-hide-show", status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true;
                checks.Add(new { name = "external-" + state + "-hide-show", status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { external.Close(); }
        }
        foreach (SizeToContent mode in Enum.GetValues<SizeToContent>())
        foreach (bool constrained in new[] { false, true })
        {
            var element = new SizingElement();
            var sizing = new Window { Title = "WPF SizeToContent oracle", Width = 280, Height = 220, Content = element, ShowActivated = false };
            var snapshots = new List<object>();
            try
            {
                if (constrained) { sizing.MinWidth = 200; sizing.MinHeight = 160; sizing.MaxWidth = 300; sizing.MaxHeight = 240; }
                sizing.Show(); Drain();
                Size frame = FrameSize(sizing);
                sizing.SizeToContent = mode;
                foreach (Size desired in new[] { new Size(160, 120), new Size(400, 300), new Size(140, 100) })
                {
                    element.SetDesired(desired); sizing.UpdateLayout(); Drain();
                    Rect displayed = NativeBounds(sizing);
                    double width = mode is SizeToContent.Width or SizeToContent.WidthAndHeight ? desired.Width + frame.Width : 280;
                    double height = mode is SizeToContent.Height or SizeToContent.WidthAndHeight ? desired.Height + frame.Height : 220;
                    if (constrained) { width = Math.Clamp(width, 200, 300); height = Math.Clamp(height, 160, 240); }
                    snapshots.Add(new { stage = "desired-" + desired, observed = Capture("layout", sizing), element.ActualWidth, element.ActualHeight });
                    Assert(Near(displayed.Width, width) && Near(displayed.Height, height), "SizeToContent axes or constraints did not follow content layout.");
                }
                Rect manual = NativeBounds(sizing);
                sizing.SizeToContent = SizeToContent.Manual;
                element.SetDesired(new Size(260, 180)); sizing.UpdateLayout(); Drain();
                Assert(NativeBounds(sizing) == manual, "Returning to Manual did not preserve current size.");
                sizing.Width = constrained ? 250 : 360; sizing.Height = constrained ? 200 : 280; sizing.UpdateLayout(); Drain();
                Assert(Near(NativeBounds(sizing).Width, constrained ? 250 : 360) && Near(NativeBounds(sizing).Height, constrained ? 200 : 280), "Manual dimensions did not resume control.");
                checks.Add(new { name = "size-to-content-" + mode + (constrained ? "-constrained" : "-free"), status = "pass", snapshots });
            }
            catch (Exception exception)
            {
                failed = true; checks.Add(new { name = "size-to-content-" + mode + (constrained ? "-constrained" : "-free"), status = "fail", detail = exception.ToString(), snapshots });
            }
            finally { sizing.Close(); }
        }
        var contentProbe = new Window { Title = "WPF content notification oracle", Width = 320, Height = 240, ShowActivated = false };
        try
        {
            int notifications = 0;
            contentProbe.ContentRendered += (_, _) => notifications++;
            contentProbe.Show(); Drain();
            Assert(notifications == 0, "Stock bare Window unexpectedly raised ContentRendered.");
            contentProbe.Content = new FrameworkElement(); Drain();
            Assert(notifications == 1, "Stock Window did not raise ContentRendered after content assignment.");
            checks.Add(new { name = "bare-window-content-notification", status = "pass", detail = "No ContentRendered with null Content, one notification after assigning content." });
        }
        catch (Exception exception)
        {
            failed = true;
            checks.Add(new { name = "bare-window-content-notification", status = "fail", detail = exception.ToString() });
        }
        finally { contentProbe.Close(); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "stock-window-restore-oracle", os = RuntimeInformation.OSDescription,
            scope = "Microsoft WindowsDesktop WPF native state and restore bounds", assembly = typeof(Window).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }
    private static bool Near(double left, double right) => Math.Abs(left - right) < 2;
    private static void Assert(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static object Capture(string stage, Window window) => new { stage, state = window.WindowState.ToString(), window.Left, window.Top, window.Width, window.Height, restore = RectData(window.RestoreBounds), displayed = RectData(NativeBounds(window)) };
    private static object RectData(Rect rect) => new { rect.X, rect.Y, rect.Width, rect.Height };
    private static Rect NativeBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out RECT rect)) throw new Win32Exception();
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    private static Size FrameSize(Window window)
    {
        if (!GetClientRect(new WindowInteropHelper(window).Handle, out RECT client)) throw new Win32Exception();
        Rect outer = NativeBounds(window);
        return new Size(outer.Width - (client.Right - client.Left), outer.Height - (client.Bottom - client.Top));
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { internal int Left, Top, Right, Bottom; }
}

internal sealed class SizingElement : FrameworkElement
{
    private Size _desired = new(160, 120);
    internal void SetDesired(Size value) { _desired = value; InvalidateMeasure(); }
    protected override Size MeasureOverride(Size constraint) => _desired;
}
