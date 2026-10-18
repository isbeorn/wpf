extern alias WpfFork;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Media;

internal static class TaskbarChecks
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait,
        Action drain, Action<object> trace)
    {
        Require(!native.HasWindowManager, "Taskbar request checks require the owned Xvfb server without a window manager.");
        native.ObserveTaskbarRequests();
        run("native-taskbar-initial-hidden-hints-and-source-lifetime", () =>
        {
            var window = new Window { Title = "Genuine WPF taskbar false", Width = 200, Height = 120,
                ShowActivated = false, Background = Brushes.Red };
            int initialized = 0, closed = 0;
            window.SourceInitialized += (_, _) => initialized++;
            window.Closed += (_, _) => closed++;
            try
            {
                Require(window.ShowInTaskbar, "Public taskbar default changed.");
                window.ShowInTaskbar = false; window.ShowInTaskbar = true; window.ShowInTaskbar = false;
                Require(PresentationSource.FromVisual(window) is null, "A pre-show taskbar edit created a source.");
                window.Show(); IntPtr xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && native.SkipsTaskbar(xid));
                var source = PresentationSource.FromVisual(window);
                Require(native.TransientOwner(xid) == IntPtr.Zero, "Taskbar suppression fabricated a native owner.");
                native.TakeTaskbarRequests(xid);
                IntPtr focus = native.FocusedWindow; bool active = window.IsActive;
                Request(window, xid, true, 0); Request(window, xid, false, 1);
                Require(window.IsActive == active && native.FocusedWindow == focus &&
                    initialized == 1 && closed == 0 && ReferenceEquals(source, PresentationSource.FromVisual(window)),
                    "Live taskbar requests changed focus or source lifetime.");
                window.Hide(); wait(() => !native.IsMapped(xid));
                window.ShowInTaskbar = true; wait(() => !native.SkipsTaskbar(xid));
                window.ShowInTaskbar = false; wait(() => native.SkipsTaskbar(xid));
                window.Show(); wait(() => native.IsMapped(xid));
                Require(native.FindWindow(window.Title) == xid && native.SkipsTaskbar(xid) &&
                    native.TransientOwner(xid) == IntPtr.Zero && initialized == 1 &&
                    ReferenceEquals(source, PresentationSource.FromVisual(window)), "Hide/edit/show changed the native identity, owner or hint.");
                trace(new { kind = "taskbar-hint-roundtrip", xid = xid.ToInt64(), skipped = native.SkipsTaskbar(xid), initialized, closed });
            }
            finally { window.Close(); }
            Require(closed == 1 && PresentationSource.FromVisual(window) is null, "Taskbar window did not close and detach exactly once.");
        });
        run("native-taskbar-default-owned-live-requests-preserve-owner", () =>
        {
            var owner = new Window { Title = "Genuine WPF taskbar owner", Width = 220, Height = 140, ShowActivated = false };
            var window = new Window { Title = "Genuine WPF owned taskbar", Width = 180, Height = 100,
                ShowActivated = false };
            try
            {
                owner.Show(); IntPtr ownerXid = native.FindWindow(owner.Title);
                window.Owner = owner;
                window.Show(); IntPtr xid = native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && native.IsMapped(ownerXid));
                Require(window.ShowInTaskbar && !native.SkipsTaskbar(xid) && native.TransientOwner(xid) == ownerXid,
                    "The initial true option or genuine native owner changed.");
                var source = PresentationSource.FromVisual(window);
                native.TakeTaskbarRequests(xid);
                IntPtr focus = native.FocusedWindow;
                Request(window, xid, false, 1); Request(window, xid, true, 0);
                Require(ReferenceEquals(window.Owner, owner) && owner.OwnedWindows.Count == 1 &&
                    native.TransientOwner(xid) == ownerXid && native.FocusedWindow == focus &&
                    ReferenceEquals(source, PresentationSource.FromVisual(window)) && native.FindWindow(window.Title) == xid,
                    "Mapped taskbar requests altered the actual owner, focus or native/source identity.");
                trace(new { kind = "taskbar-owner-preserved", xid = xid.ToInt64(), owner = ownerXid.ToInt64(), focus = focus.ToInt64() });
            }
            finally { window.Close(); owner.Close(); }
            Require(PresentationSource.FromVisual(window) is null && owner.OwnedWindows.Count == 0, "Owned taskbar window retained its source/owner link.");
        });
        drain();

        void Request(Window window, IntPtr xid, bool show, int expectedAction)
        {
            var requests = new List<int>();
            window.ShowInTaskbar = show;
            wait(() => { requests.AddRange(native.TakeTaskbarRequests(xid)); return requests.Count > 0; });
            Require(requests.SequenceEqual(new[] { expectedAction }), "Mapped taskbar change did not send the expected native remove/add request.");
            trace(new { kind = "taskbar-native-request", xid = xid.ToInt64(), show, actions = requests.ToArray(),
                scope = "EWMH request without a WM acknowledgement or desktop taskbar observation" });
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
