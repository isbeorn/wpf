extern alias WpfFork;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Threading;
using Point = WpfFork::System.Windows.Point;
using Rect = WpfFork::System.Windows.Rect;
using Size = WpfFork::System.Windows.Size;

internal static class NativeAutomationChecks
{
    internal static object[] Run(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var evidence = new List<object>();
        try
        {
            foreach (int dpi in new[] { 96, 192 })
            {
                native.SetDpi(dpi); drain();
                var button = new Button { Width = 60, Height = 30, Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(Border)) },
                    RenderTransform = new SkewTransform(15, 0) };
                var canvas = new Canvas { Background = Brushes.White };
                Canvas.SetLeft(button, 20); Canvas.SetTop(button, 12); canvas.Children.Add(button);
                var window = new Window { Title = "Managed WPF peers " + dpi, Width = 240, Height = 140, Content = canvas, ShowActivated = false };
                try
                {
                    window.Show(); var xid = native.FindWindow(window.Title);
                    wait(() => native.IsMapped(xid) && button.ActualWidth > 0 && VisualTreeHelper.GetDpi(window).DpiScaleX == dpi / 96d);
                    var rootPeer = UIElementAutomationPeer.CreatePeerForElement(window)!;
                    var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
                    Require(rootPeer.GetName() == window.Title && rootPeer.GetAutomationControlType() == AutomationControlType.Window, "Native Window peer title/role mismatch.");
                    Require(rootPeer.GetChildren()?.Contains(peer) == true, "Native Window peer did not enumerate its genuine Button.");
                    CheckBounds("initial");
                    var before = peer.GetBoundingRectangle();
                    native.Move(xid, 110, 90); wait(() => native.ClientOrigin(xid) == (110, 90) && peer.GetBoundingRectangle().TopLeft != before.TopLeft);
                    CheckBounds("moved");
                    peer.SetFocus(); wait(() => peer.HasKeyboardFocus() && ReferenceEquals(Keyboard.FocusedElement, button));
                    Rect bounds = peer.GetBoundingRectangle(); Point click = peer.GetClickablePoint();
                    Require(Math.Abs(click.X - (bounds.Left + bounds.Width / 2)) < 0.001 && Math.Abs(click.Y - (bounds.Top + bounds.Height / 2)) < 0.001, "Native clickable point ignored window coordinates.");
                    Require(!AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged), "Managed peer queries invented a native accessibility listener.");
                    window.Close();
                    Require(PresentationSource.FromVisual(window) == null && rootPeer.GetBoundingRectangle().IsEmpty && peer.GetBoundingRectangle().IsEmpty && !peer.HasKeyboardFocus(),
                        "Closed native source retained peer bounds or focus.");
                    Require(double.IsNaN(peer.GetClickablePoint().X), "Closed native peer invented a clickable point.");
                    evidence.Add(new { dpi, state = "closed", detached = true });

                    void CheckBounds(string state)
                    {
                        Equal(peer.GetBoundingRectangle(), Bounds(button));
                        Equal(rootPeer.GetBoundingRectangle(), Bounds(window));
                        evidence.Add(new { dpi, state, button = peer.GetBoundingRectangle().ToString(System.Globalization.CultureInfo.InvariantCulture),
                            window = rootPeer.GetBoundingRectangle().ToString(System.Globalization.CultureInfo.InvariantCulture), origin = native.ClientOrigin(xid).ToString() });
                    }
                }
                finally { if (PresentationSource.FromVisual(window) != null) window.Close(); }
            }
        }
        finally { native.SetDpi(96); drain(); }
        return evidence.ToArray();
    }
    private static Rect Bounds(UIElement element)
    {
        Point[] p = [element.PointToScreen(new Point()), element.PointToScreen(new Point(element.RenderSize.Width, 0)),
            element.PointToScreen(new Point(0, element.RenderSize.Height)), element.PointToScreen(new Point(element.RenderSize.Width, element.RenderSize.Height))];
        return new Rect(p.Min(x => x.X), p.Min(x => x.Y), p.Max(x => x.X) - p.Min(x => x.X), p.Max(x => x.Y) - p.Min(x => x.Y));
    }
    private static void Equal(Rect actual, Rect expected) => Require(!actual.IsEmpty && actual.Width > 0 && actual.Height > 0 &&
        Math.Abs(actual.X - expected.X) < 0.01 && Math.Abs(actual.Y - expected.Y) < 0.01 && Math.Abs(actual.Width - expected.Width) < 0.01 && Math.Abs(actual.Height - expected.Height) < 0.01,
        $"Peer screen bounds {actual} differ from native source visual bounds {expected}.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
