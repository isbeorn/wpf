using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal static class UnanchoredContextMenuReference
{
    internal static int Run()
    {
        var observations = new List<object>();
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Instructions" });
        var animation = new BooleanAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteBooleanKeyFrame(true, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        Storyboard.SetTargetProperty(animation, new PropertyPath("ContextMenu.IsOpen"));
        var storyboard = new Storyboard(); storyboard.Children.Add(animation);
        var trigger = new EventTrigger(Button.ClickEvent);
        trigger.Actions.Add(new BeginStoryboard { Storyboard = storyboard });
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, menu)); style.Triggers.Add(trigger);
        var button = new Button { Content = "Add", Width = 60, Height = 40, Style = style };
        var window = new Window { Title = "Stock targetless ContextMenu reference", Left = 160, Top = 140,
            Width = 360, Height = 260, Content = button };
        int opened = 0, closed = 0;
        menu.Opened += (_, _) => opened++;
        menu.Closed += (_, _) => closed++;
        Exception? failure = null;
        HwndSource? source = null;
        try
        {
            window.Show(); Drain(); window.Activate(); button.Focus(); Drain();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            Drain();
            source = PresentationSource.FromVisual(menu) as HwndSource;
            if (opened != 1 || menu.PlacementTarget != null || source == null || source.IsDisposed)
                throw new InvalidOperationException("The stock Click storyboard did not open a live targetless menu.");
            Snapshot("opened");
            window.Close(); Drain(); Snapshot("owner-closed");
            menu.BeginAnimation(ContextMenu.IsOpenProperty, null); menu.IsOpen = false; Drain(); Snapshot("explicitly-closed");
            if (menu.IsOpen || !source.IsDisposed) throw new InvalidOperationException("Stock explicit menu cleanup retained a source.");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            menu.BeginAnimation(ContextMenu.IsOpenProperty, null); menu.IsOpen = false; window.Close(); Drain();
        }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "stock-unanchored-context-menu", assembly = typeof(ContextMenu).Assembly.Location,
            scope = "Actual installed WindowsDesktop HWND/focus/capture and owner-close reference; programmatic Click routed event, no injected physical/native click",
            opened, closed, observations, status = failure == null ? "pass" : "fail", error = failure?.ToString() },
            new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;

        void Snapshot(string phase) => observations.Add(new { phase, menu.IsOpen, nullTarget = menu.PlacementTarget == null,
            placement = menu.Placement.ToString(), sourceDisposed = source?.IsDisposed, hwnd = source?.Handle.ToInt64(),
            nativeParent = source is { IsDisposed: false } ? GetParent(source.Handle).ToInt64() : 0,
            nativeOwner = source is { IsDisposed: false } ? GetWindow(source.Handle, 4).ToInt64() : 0,
            nativeCapture = GetCapture().ToInt64(), foreground = GetForegroundWindow().ToInt64(),
            keyboardSource = Keyboard.PrimaryDevice.ActiveSource?.GetType().FullName });
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
