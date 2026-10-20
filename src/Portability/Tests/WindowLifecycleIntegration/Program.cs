using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;
using MS.Internal.Portability.Settings;

if (args.Length == 2 && args[0] == "--application-run")
    return ApplicationRunCases.RunChild(args[1], () =>
    {
        PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(
            pointerSettings: new PortablePointerSettings(4, 4, 500)));
        PortableWindowPlatform.RegisterFactory(new ManagedFactory());
    });

var checks = new List<Check>();
var thread = new Thread(() =>
{
    PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(
        pointerSettings: new PortablePointerSettings(4, 4, 500)));
    void Run(string name, Action body)
    {
        try { body(); checks.Add(new(name, "pass", "Observed real Window with managed peer.")); }
        catch (Exception exception) { checks.Add(new(name, "fail", exception.ToString())); }
    }
    Run("missing-backend-is-explicit", () =>
    {
        var window = new Window();
        try { window.Show(); throw new InvalidOperationException("Expected backend failure"); }
        catch (PlatformNotSupportedException exception) { Assert(exception.Message.Contains("backend"), "Named backend diagnostic"); }
        finally { window.Close(); }
    });
    var factory = new ManagedFactory();
    PortableWindowPlatform.RegisterFactory(factory);
    Run("construction-title-size", () =>
    {
        var window = new Window { Title = "Before", Width = 320, Height = 240 };
        Assert(window.Title == "Before" && window.Width == 320 && window.Height == 240, "Initial values");
        window.Close();
    });
    Run("taskbar-before-source-and-source-initialized-latest-value", () =>
    {
        var window = new Window { ShowActivated = false };
        int before = factory.CreateCount;
        try
        {
            Assert(window.ShowInTaskbar, "Default ShowInTaskbar must remain true.");
            window.ShowInTaskbar = false; window.ShowInTaskbar = true; window.ShowInTaskbar = false;
            Assert(PresentationSource.FromVisual(window) == null && factory.CreateCount == before, "Pre-source taskbar changes created a peer.");
            window.Show();
            var peer = factory.Last;
            Assert(!peer.ShowInTaskbar && !peer.TaskbarAtShow, "The initial false taskbar option was not applied before Show.");
        }
        finally { window.Close(); }
        var during = new Window { ShowActivated = false };
        during.SourceInitialized += (_, _) => during.ShowInTaskbar = false;
        try
        {
            during.Show();
            Assert(!factory.Last.TaskbarAtShow && !during.ShowInTaskbar, "SourceInitialized taskbar change missed the first Show.");
        }
        finally { during.Close(); }
    });
    Run("taskbar-live-hidden-owner-and-lifetime-preserved", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var window = new Window { Owner = owner, ShowActivated = false };
        int initialized = 0, closed = 0;
        window.SourceInitialized += (_, _) => initialized++;
        window.Closed += (_, _) => closed++;
        try
        {
            window.Show(); var peer = factory.Last; var source = PresentationSource.FromVisual(window);
            Assert(peer.ShowInTaskbar && peer.TaskbarAtShow, "Default true was not applied before Show.");
            int created = factory.CreateCount, shows = peer.ShowCount, hides = peer.HideCount;
            bool active = window.IsActive, ownerActive = owner.IsActive;
            window.ShowInTaskbar = false; window.ShowInTaskbar = true; window.ShowInTaskbar = false;
            Assert(peer.TaskbarRequests.SequenceEqual(new[] { false, true, false }), "Live taskbar changes were not forwarded in both directions.");
            Assert(factory.CreateCount == created && peer.ShowCount == shows && peer.HideCount == hides &&
                initialized == 1 && closed == 0 && ReferenceEquals(source, PresentationSource.FromVisual(window)),
                "Taskbar changes recreated, showed, hid or closed the source.");
            Assert(ReferenceEquals(window.Owner, owner) && ReferenceEquals(peer.Owner, ownerPeer) && owner.OwnedWindows.Count == 1 &&
                window.IsActive == active && owner.IsActive == ownerActive, "Taskbar changes altered ownership or activation.");
            window.Hide();
            window.ShowInTaskbar = true; Assert(peer.ShowInTaskbar, "Hidden taskbar enable did not reach the peer.");
            window.ShowInTaskbar = false; Assert(!peer.ShowInTaskbar, "Hidden taskbar disable did not reach the peer.");
            window.Show();
            Assert(!peer.TaskbarAtShow && factory.Last == peer && ReferenceEquals(source, PresentationSource.FromVisual(window)) && initialized == 1,
                "Hide/edit/show lost taskbar state or source identity.");
            System.ComponentModel.CancelEventHandler cancel = (_, e) => e.Cancel = true;
            window.Closing += cancel; window.Close();
            Assert(!peer.Disposed && closed == 0 && ReferenceEquals(window.Owner, owner), "Canceled close lost taskbar peer ownership.");
            window.ShowInTaskbar = true; Assert(peer.ShowInTaskbar, "Canceled close prevented a later taskbar change.");
            window.Closing -= cancel;
        }
        finally { window.Close(); owner.Close(); }
        Assert(closed == 1 && owner.OwnedWindows.Count == 0, "Taskbar window did not close exactly once.");
    });
    Run("source-show-hide-activation-close", () =>
    {
        var window = new Window { Title = "Before", Width = 320, Height = 240 };
        int sourceCount = 0, closedCount = 0, activatedCount = 0;
        window.SourceInitialized += (_, _) => sourceCount++;
        window.Closed += (_, _) => closedCount++;
        window.Activated += (_, _) => activatedCount++;
        window.Show();
        var peer = factory.Last;
        Assert(PresentationSource.FromVisual(window) != null && peer.IsVisible, "Real source attached");
        Assert(sourceCount == 1 && window.IsActive && activatedCount == 1, "Source/activation events");
        Assert(peer.ClientSize == new Size(304, 200), "Outer dimensions subtract native frame");
        window.Title = "After";
        window.Width = 400; window.Height = 300;
        Assert(peer.Title == "After" && peer.OuterSize == new Size(400, 300), "Live properties");
        window.Hide();
        Assert(!peer.IsVisible && PresentationSource.FromVisual(window) != null, "Hide retains source");
        window.Show();
        Assert(sourceCount == 1 && factory.Last == peer, "Show reuses peer");
        window.Close();
        Assert(peer.Disposed && closedCount == 1 && PresentationSource.FromVisual(window) == null, "Close releases source/peer once");
        window.Close(); Assert(closedCount == 1, "Repeated close");
    });
    Run("minimum-maximum-both-directions", () =>
    {
        var window = new Window { Width = 320, Height = 240 };
        window.Show(); var peer = factory.Last;
        window.MinWidth = 500; window.MinHeight = 350;
        Assert(peer.OuterSize == new Size(500, 350), "Minimum grows both dimensions");
        Assert(peer.Minimum == new Size(484, 310), "Minimum passed in client dimensions");
        window.MinWidth = 100; window.MinHeight = 80;
        window.MaxWidth = 200; window.MaxHeight = 160;
        Assert(peer.OuterSize == new Size(200, 160), "Maximum shrinks both dimensions");
        Assert(peer.Maximum == new Size(184, 120), "Maximum passed in client dimensions");
        window.MaxWidth = 600; window.MaxHeight = 500;
        window.Width = 550; window.Height = 450;
        Assert(peer.OuterSize == new Size(550, 450), "Relaxed maximum permits growth");
        window.MinWidth = 700; window.MinHeight = 600;
        Assert(peer.OuterSize == new Size(700, 600), "Minimum wins conflicting maximum");
        window.MinWidth = 0; window.MinHeight = 0;
        window.MaxWidth = double.PositiveInfinity; window.MaxHeight = double.PositiveInfinity;
        window.Width = 16; window.Height = 40;
        Assert(peer.ClientSize == new Size(), "Zero minimum permits an empty client at frame dimensions");
        window.Width = 320; window.Height = 240;
        Assert(peer.OuterSize == new Size(320, 240), "Infinite maximum permits growth after the zero boundary");
        window.Close();
    });
    Run("owner-add-remove-and-close-owned", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var child = new Window { Owner = owner }; child.Show(); var childPeer = factory.Last;
        Assert(owner.OwnedWindows.Count == 1 && childPeer.Owner == ownerPeer, "Owner add");
        child.Owner = null;
        Assert(owner.OwnedWindows.Count == 0 && childPeer.Owner == null, "Owner remove");
        child.Owner = owner;
        child.Closing += (_, e) => e.Cancel = true;
        owner.Close();
        Assert(childPeer.Disposed && ownerPeer.Disposed, "Owner closes owned window");
    });
    Run("close-cancel-native-request-accept", () =>
    {
        var window = new Window(); window.Show(); var peer = factory.Last;
        bool cancel = true; int closing = 0, closed = 0;
        window.Closing += (_, e) => { closing++; e.Cancel = cancel; };
        window.Closed += (_, _) => closed++;
        peer.RequestClose();
        Assert(!peer.Disposed && peer.IsVisible && closing == 1 && closed == 0, "Canceled native close");
        cancel = false; peer.RequestClose();
        Assert(peer.Disposed && closing == 2 && closed == 1, "Accepted native close");
    });
    Run("close-ack-contract-and-failure-cleanup", () =>
    {
        foreach (int mode in new[] { 1, 2, 3 })
        {
            var window = new Window(); window.Show(); var peer = factory.Last;
            peer.CloseMode = mode; int closed = 0;
            window.Closed += (_, _) => closed++;
            if (mode == 1)
            {
                try { window.Close(); throw new InvalidDataException("Expected missing acknowledgement failure"); }
                catch (InvalidOperationException exception) { Assert(exception.Message.Contains("acknowledge"), "Missing acknowledgement diagnostic"); }
            }
            else if (mode == 2)
            {
                try { window.Close(); throw new InvalidDataException("Expected close failure"); }
                catch (MarkerException) { }
            }
            else window.Close();
            Assert(peer.DisposeCount == 1 && PresentationSource.FromVisual(window) == null, "Failure releases source/peer once");
            Assert(closed == (mode == 3 ? 1 : 0), "Failed close never synthesizes successful Closed");
        }
    });
    Run("forced-shutdown-ignores-close-cancel", () =>
    {
        var window = new Window(); window.Show(); var peer = factory.Last;
        window.Closing += (_, e) => e.Cancel = true;
        window.InternalClose(true, false);
        Assert(peer.Disposed, "Shutdown cannot be canceled");
    });
    Run("modal-sync-restores-enabled-and-active", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var disabled = new Window { IsEnabled = false }; disabled.Show(); var disabledPeer = factory.Last;
        owner.Activate();
        var dialog = new Window { Owner = owner };
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            Assert(!ownerPeer.IsEnabled && !disabledPeer.IsEnabled, "Modal disables enabled windows only");
            Assert(System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Real modal scope");
            dialog.DialogResult = true;
        }));
        Assert(dialog.ShowDialog() == true, "Synchronous result");
        Assert(ownerPeer.IsEnabled && !disabledPeer.IsEnabled && ownerPeer.IsActive, "Original enable/activation restored");
        Assert(!System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Modal scope balanced");
        disabled.Close(); owner.Close();
    });
    Run("modal-dialog-cancel-command", () =>
    {
        var dialog = new Window();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            Assert(Window.DialogCancelCommand.CanExecute(null, dialog), "Real dialog cancel command is registered");
            Window.DialogCancelCommand.Execute(null, dialog);
        }));
        Assert(dialog.ShowDialog() == false && factory.Last.Disposed, "Routed cancel command exits dialog");
    });
    Run("modal-canceled-result-keeps-frame-running", () =>
    {
        var dialog = new Window(); bool cancel = true;
        dialog.Closing += (_, e) => e.Cancel = cancel;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            dialog.DialogResult = true;
            Assert(dialog.DialogResult == null && factory.Last.IsVisible, "Canceled result keeps dialog visible");
            cancel = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => dialog.DialogResult = false));
        }));
        Assert(dialog.ShowDialog() == false && factory.Last.Disposed, "Later accepted result exits frame");
    });
    Run("modal-hide-and-nested-owners", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var outer = new Window { Owner = owner };
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            var outerPeer = factory.Last;
            var inner = new Window { Owner = outer };
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                Assert(!outerPeer.IsEnabled && !ownerPeer.IsEnabled, "Nested owners disabled");
                inner.Hide();
            }));
            Assert(inner.ShowDialog() == false && outerPeer.IsEnabled && !ownerPeer.IsEnabled, "Inner scope restores outer only");
            inner.Close(); outer.Hide();
        }));
        Assert(outer.ShowDialog() == false && ownerPeer.IsEnabled, "Hide exits real nested frame");
        outer.Close(); owner.Close();
    });
    Run("closing-exception-disposes-and-restores-modal", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var dialog = new Window { Owner = owner };
        dialog.Closing += (_, _) => throw new MarkerException();
        Exception? observed = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            try { dialog.Close(); } catch (MarkerException exception) { observed = exception; }
        }));
        Assert(dialog.ShowDialog() == false && observed is MarkerException && factory.Last.Disposed, "Closing exception cleanup");
        Assert(ownerPeer.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Owner/modal restored");
        owner.Close();
    });
    Run("modal-show-exception-restores-owner", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var dialog = new Window { Owner = owner };
        factory.FailNextShow = true;
        try { dialog.ShowDialog(); throw new InvalidOperationException("Expected show failure"); }
        catch (MarkerException) { }
        Assert(ownerPeer.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal && !factory.Last.IsVisible, "Failed show hides peer and restores owner/scope");
        dialog.Close(); owner.Close();
    });
    Run("modal-dispatch-exception-hides-and-restores", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var dialog = new Window { Owner = owner };
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => throw new MarkerException()));
        try { dialog.ShowDialog(); throw new InvalidOperationException("Expected dispatcher exception"); }
        catch (MarkerException) { }
        Assert(!factory.Last.IsVisible && ownerPeer.IsEnabled, "Dispatcher failure hides dialog and restores owner");
        Assert(!System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Exception modal scope balanced");
        dialog.Close(); owner.Close();
    });
    Run("modal-enter-leave-handler-exception-cleanup", () =>
    {
        var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
        var enterDialog = new Window { Owner = owner };
        EventHandler fail = (_, _) => throw new MarkerException();
        System.Windows.Interop.ComponentDispatcher.EnterThreadModal += fail;
        try
        {
            try { enterDialog.ShowDialog(); throw new InvalidOperationException("Expected EnterThreadModal failure"); }
            catch (MarkerException) { }
        }
        finally { System.Windows.Interop.ComponentDispatcher.EnterThreadModal -= fail; }
        Assert(!System.Windows.Interop.ComponentDispatcher.IsThreadModal && ownerPeer.IsEnabled, "Enter failure balances scope and restores owner");
        enterDialog.Close();
        var leaveDialog = new Window { Owner = owner };
        System.Windows.Interop.ComponentDispatcher.LeaveThreadModal += fail;
        try
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => leaveDialog.Close()));
            try { leaveDialog.ShowDialog(); throw new InvalidOperationException("Expected LeaveThreadModal failure"); }
            catch (MarkerException) { }
        }
        finally { System.Windows.Interop.ComponentDispatcher.LeaveThreadModal -= fail; }
        Assert(!System.Windows.Interop.ComponentDispatcher.IsThreadModal && ownerPeer.IsEnabled, "Leave failure restores owner after event throws");
        owner.Close();
    });
    Run("modal-enter-hide-close-skips-frame", () =>
    {
        foreach (bool close in new[] { false, true })
        {
            var dialog = new Window();
            bool rescueRan = false;
            DispatcherOperation? rescue = null;
            EventHandler handler = (_, _) =>
            {
                if (close) dialog.Close(); else dialog.Hide();
                rescue = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                { rescueRan = true; Dispatcher.ExitAllFrames(); }));
            };
            System.Windows.Interop.ComponentDispatcher.EnterThreadModal += handler;
            try { Assert(dialog.ShowDialog() == false, "Enter callback ends dialog synchronously"); }
            finally
            {
                System.Windows.Interop.ComponentDispatcher.EnterThreadModal -= handler;
                rescue?.Abort();
            }
            Assert(!rescueRan && !System.Windows.Interop.ComponentDispatcher.IsThreadModal, "No frame created after enter callback hides/closes dialog");
            if (!close) dialog.Close();
        }
    });
    Run("source-initialized-hide-close-skips-frame", () =>
    {
        foreach (bool close in new[] { false, true })
        {
            var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
            var dialog = new Window { Owner = owner };
            bool rescueRan = false;
            DispatcherOperation? rescue = null;
            dialog.SourceInitialized += (_, _) =>
            {
                if (close) dialog.Close(); else dialog.Hide();
                rescue = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                { rescueRan = true; Dispatcher.ExitAllFrames(); }));
            };
            try { Assert(dialog.ShowDialog() == false, "Source callback ends dialog synchronously"); }
            finally { rescue?.Abort(); }
            Assert(!rescueRan && ownerPeer.IsEnabled && !factory.Last.IsVisible, "Source hide/close skips frame and restores owner");
            if (close) Assert(factory.Last.Disposed, "Early close disposes peer");
            else dialog.Close();
            owner.Close();
        }
    });
    Run("root-layout-loaded-and-rendered", () =>
    {
        var content = new MeasuringElement();
        var window = new Window { Width = 320, Height = 240, Content = content };
        int loaded = 0; bool rendered = false;
        window.Loaded += (_, _) => loaded++;
        window.ContentRendered += (_, _) => rendered = true;
        window.Show(); var peer = factory.Last;
        Drain();
        Assert(window.RenderSize == new Size(320, 240), "Whole Window measured/arranged");
        Assert(content.RenderSize == new Size(304, 200), "Content receives client dimensions");
        Assert(loaded == 1 && rendered && peer.PresentCount > 0 && peer.RedPixelCount > 0, "Actual Loaded once and rendered pixels");
        window.Hide(); window.Width = 400; window.Height = 300; Drain();
        window.Show(); Drain();
        Assert(loaded == 1 && content.RenderSize == new Size(384, 260), "Hidden resize and show preserve root/client layout without reload");
        peer.SetClientSize(new Size(300, 180)); Drain();
        Assert(window.Width == 316 && window.Height == 220 && content.RenderSize == new Size(300, 180), "Host resize updates dimensions and layout");
        int dpiChanged = 0; window.DpiChanged += (_, _) => dpiChanged++;
        peer.SetDpi(new DpiScale(2, 2)); Drain();
        Assert(dpiChanged == 1 && peer.PixelWidth == 600 && peer.PixelHeight == 360 && content.RenderSize == new Size(300, 180), "DPI changes raster pixels while preserving logical layout");
        window.Close();
    });
    Run("explicit-style-template-and-dynamic-resources", () =>
    {
        var window = new Window { Width = 320, Height = 240 };
        window.Resources[SystemColors.WindowBrushKey] = Brushes.Blue;
        window.Show(); Drain();
        Assert(ReferenceEquals(window.Background, Brushes.Blue), "Dynamic resource resolves Window resource");
        window.Resources[SystemColors.WindowBrushKey] = Brushes.Green;
        Assert(ReferenceEquals(window.Background, Brushes.Green), "Dynamic resource responds to replacement");
        var replacement = new ControlTemplate(typeof(Window)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        var style = new Style(typeof(Window));
        style.Setters.Add(new Setter(Control.TemplateProperty, replacement));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Yellow));
        window.Style = style; Drain();
        Assert(ReferenceEquals(window.Template, replacement) && ReferenceEquals(window.Background, Brushes.Yellow), "Explicit style overrides portable fallback");
        var local = new ControlTemplate(typeof(Window)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        window.Template = local; Drain();
        Assert(ReferenceEquals(window.Template, local), "Explicit local template remains honored");
        window.Close();
    });
    Run("state-and-enable-symmetric", () =>
    {
        var window = new Window(); window.Show(); var peer = factory.Last;
        int changed = 0; window.StateChanged += (_, _) => changed++;
        window.WindowState = WindowState.Maximized; window.WindowState = WindowState.Normal;
        Assert(changed == 2 && peer.State == WindowState.Normal, "State roundtrip");
        window.IsEnabled = false; Assert(!peer.IsEnabled, "Disable");
        window.IsEnabled = true; Assert(peer.IsEnabled, "Enable"); window.Close();
    });
    Run("initial-map-frame-preserves-requested-outer-bounds", () =>
    {
        factory.DecorateOnShow = true;
        try
        {
            var window = new Window { Width = 320, Height = 240 };
            window.Show(); var peer = factory.Last;
            Assert(peer.OuterSize == new Size(320, 240) && peer.ClientSize == new Size(304, 200), "First map reapplies outer dimensions after frame becomes known");
            Assert(window.RestoreBounds.TopLeft == new Point(120, 80), "Unrequested position follows window manager placement");
            window.Close();
            var zero = new Window { Left = 0, Top = 0, Width = 320, Height = 240 };
            zero.Show();
            Assert(factory.Last.DesktopPosition == new Point(), "Explicit zero position retains user placement intent");
            zero.Close();
        }
        finally { factory.DecorateOnShow = false; }
    });
    Run("restore-size-partial-axes-both-states-orders-directions", () =>
    {
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool geometryFirst in new[] { false, true })
        foreach (bool increase in new[] { true, false })
        {
            factory.GeometryFirst = geometryFirst;
            var window = new Window { Width = 400, Height = 300 }; window.Show(); var peer = factory.Last;
            window.WindowState = state; Size displayed = peer.OuterSize;
            Assert(window.Width == displayed.Width && window.Height == displayed.Height, "Application-requested state initially exposes displayed dimensions like stock WPF");
            double width = increase ? 520 : 280, height = increase ? 380 : 220;
            window.Width = width;
            Assert(peer.OuterSize == displayed, "Width edit must not resize min/max native bounds");
            Assert(window.RestoreBounds.Width == width && window.RestoreBounds.Height == 300, "Width-only edit retains normal height");
            window.Height = height;
            Assert(peer.OuterSize == displayed && window.RestoreBounds.Size == new Size(width, height), "Height edit retains normal width");
            window.WindowState = WindowState.Normal;
            Assert(peer.OuterSize == new Size(width, height) && window.Width == width && window.Height == height, "Normal applies both restore axes");
            window.Close();
        }
        factory.GeometryFirst = false;
    });
    Run("observed-native-state-keeps-normal-cache", () =>
    {
        foreach (bool geometryFirst in new[] { false, true })
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        {
            factory.GeometryFirst = geometryFirst;
            var window = new Window { Left = -100, Top = 50, Width = 400, Height = 300 };
            window.Show(); var peer = factory.Last;
            peer.SetWindowState(state);
            Assert(window.WindowState == state && window.RestoreBounds.Size == new Size(400, 300), "Observed state does not replace normal bounds with min/max geometry");
            Assert(window.Width == 400 && window.Height == 300, "Observed min/max dimensions reflect restore bounds like stock WPF");
            window.Width = 520;
            peer.SetWindowState(WindowState.Normal);
            Assert(window.WindowState == WindowState.Normal && peer.OuterSize == new Size(520, 300), "Observed restore applies pending width and retains normal height");
            window.Close();
        }
        factory.GeometryFirst = false;
    });
    Run("initial-minmax-retains-requested-normal-bounds", () =>
    {
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        {
            var window = new Window { Left = -100, Top = 50, Width = 400, Height = 300, WindowState = state };
            window.Show(); var peer = factory.Last;
            Assert(peer.WindowState == state && window.RestoreBounds == new Rect(-100, 50, 400, 300), "Initial requested state stays distinct from hidden normal geometry");
            window.WindowState = WindowState.Normal;
            Assert(peer.OuterSize == new Size(400, 300) && peer.DesktopPosition == new Point(-100, 50), "Initial state restores requested normal bounds");
            window.Close();
        }
    });
    Run("normal-state-handler-edits-wait-for-coherent-geometry", () =>
    {
        factory.GeometryFirst = false;
        try
        {
            foreach (bool external in new[] { false, true })
            {
                var window = new Window { Width = 400, Height = 300, Left = 100, Top = 80 };
                window.Show(); var peer = factory.Last;
                window.WindowState = WindowState.Maximized;
                window.StateChanged += (_, _) =>
                {
                    if (window.WindowState != WindowState.Normal) return;
                    window.Width = 520; window.Height = 380;
                    window.Left = -120; window.Top = 60;
                };
                if (external) peer.SetWindowState(WindowState.Normal);
                else window.WindowState = WindowState.Normal;
                Assert(peer.OuterSize == new Size(520, 380) && peer.DesktopPosition == new Point(-120, 60),
                    "Reentrant Normal edits apply with confirmed normal frame geometry on both axes");
                Assert(window.RestoreBounds == new Rect(-120, 60, 520, 380), "Restore cache retains reentrant requests");
                window.Close();
            }
        }
        finally { factory.GeometryFirst = false; }
    });
    Run("restore-constraints-clamp-on-normal-not-while-minmax", () =>
    {
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool large in new[] { false, true })
        {
            var window = new Window { Width = 400, Height = 300, MinWidth = 200, MinHeight = 160, MaxWidth = 600, MaxHeight = 500 };
            window.Show(); var peer = factory.Last; window.WindowState = state;
            Size displayed = peer.OuterSize;
            window.Width = large ? 800 : 100; window.Height = large ? 650 : 80;
            Assert(window.RestoreBounds.Size == new Size(large ? 800 : 100, large ? 650 : 80) && peer.OuterSize == displayed, "Min/max retains raw requested restore axes");
            window.WindowState = WindowState.Normal;
            Assert(peer.OuterSize == new Size(large ? 600 : 200, large ? 500 : 160), "Normal restoration applies constraints");
            window.Close();
        }
    });
    Run("desktop-position-negative-origins-asymmetric-dpi", () =>
    {
        factory.InitialDpi = new DpiScale(1.5, 2);
        try
        {
            var window = new Window { Left = -240, Top = -120, Width = 400, Height = 300 };
            window.Show(); var peer = factory.Last;
            Assert(peer.DesktopPosition == new Point(-360, -240), "Initial logical position converts with current root DPI");
            window.WindowState = WindowState.Maximized; Point displayed = peer.DesktopPosition;
            window.Left = 120; window.Top = -80;
            Assert(peer.DesktopPosition == displayed && window.RestoreBounds.TopLeft == new Point(120, -80), "Maximized position edits are deferred");
            window.WindowState = WindowState.Normal;
            Assert(peer.DesktopPosition == new Point(180, -160) && window.Left == 120 && window.Top == -80, "Normal applies physical desktop position");
            int changed = 0; window.LocationChanged += (_, _) => changed++;
            peer.SetDesktopPosition(new Point(-450, 300));
            Assert(window.Left == -300 && window.Top == 150 && changed == 1, "Host move converts both axes and raises location event");
            peer.SetDesktopPosition(new Point(-450, 300));
            Assert(changed == 1, "Unchanged host position does not raise another location event");
            window.Close();
        }
        finally { factory.InitialDpi = new DpiScale(1, 1); }
    });
    Run("location-handler-bounds-edits", () =>
    {
        foreach (bool restore in new[] { false, true })
        foreach (bool geometryFirst in new[] { false, true })
        {
            factory.GeometryFirst = geometryFirst;
            var window = new Window { Left = 100, Top = 80, Width = 400, Height = 300 };
            window.Show(); var peer = factory.Last; bool edited = false;
            if (restore) window.WindowState = WindowState.Maximized;
            window.LocationChanged += (_, _) =>
            {
                if (edited) return;
                edited = true;
                window.Width = 520; window.Height = 380; window.Left = -120; window.Top = 60;
            };
            if (restore) window.WindowState = WindowState.Normal;
            else window.Left = 120;
            Assert(edited && peer.OuterSize == new Size(520, 380) && peer.DesktopPosition == new Point(-120, 60),
                "LocationChanged edits reach peer on all four axes");
            Assert(window.RestoreBounds == new Rect(-120, 60, 520, 380), "LocationChanged edits reach restore bounds");
            window.Close();
        }
        factory.GeometryFirst = false;
    });
    foreach (string action in new[] { "close", "hide", "state" })
    Run("location-handler-" + action + "-reentry", () =>
    {
        foreach (bool restore in new[] { false, true })
        foreach (bool geometryFirst in new[] { false, true })
        {
            factory.GeometryFirst = geometryFirst;
            var window = new Window { Left = 100, Top = 80, Width = 400, Height = 300 };
            window.Show(); var peer = factory.Last; bool handled = false;
            if (restore) window.WindowState = WindowState.Maximized;
            window.LocationChanged += (_, _) =>
            {
                if (handled) return;
                handled = true;
                if (action == "close") window.Close();
                else if (action == "hide") window.Hide();
                else window.WindowState = WindowState.Minimized;
            };
            if (restore) window.WindowState = WindowState.Normal;
            else peer.SetDesktopPosition(new Point(120, 80));
            Assert(handled, $"LocationChanged invokes handler (restore={restore}, geometryFirst={geometryFirst}, action={action}, logical={window.Left},{window.Top}, physical={peer.DesktopPosition}, dpi={peer.DpiScale.DpiScaleX},{peer.DpiScale.DpiScaleY})");
            if (action == "close") Assert(peer.DisposeCount == 1 && PresentationSource.FromVisual(window) == null, "LocationChanged close detaches and disposes once");
            else if (action == "hide") Assert(!peer.IsVisible && window.Visibility == Visibility.Hidden, $"LocationChanged hide reaches peer (restore={restore}, geometryFirst={geometryFirst}, visible={peer.IsVisible}, visibility={window.Visibility})");
            else Assert(peer.WindowState == WindowState.Minimized && window.WindowState == WindowState.Minimized, $"LocationChanged state request reaches peer (restore={restore}, geometryFirst={geometryFirst}, peer={peer.WindowState}, window={window.WindowState})");
            window.Close();
        }
        factory.GeometryFirst = false;
    });
    foreach (SizeToContent mode in Enum.GetValues<SizeToContent>())
    foreach (bool constrained in new[] { false, true })
    Run("size-to-content-" + mode + (constrained ? "-constrained" : "-free"), () =>
    {
        var element = new SizingElement();
        var window = new Window { Width = 280, Height = 220, Content = element };
        if (constrained) { window.MinWidth = 200; window.MinHeight = 160; window.MaxWidth = 300; window.MaxHeight = 240; }
        window.Show(); var peer = factory.Last; Drain(); window.SizeToContent = mode;
        foreach (Size desired in new[] { new Size(160, 120), new Size(400, 300), new Size(140, 100) })
        {
            element.SetDesired(desired); window.UpdateLayout(); Drain();
            double width = mode is SizeToContent.Width or SizeToContent.WidthAndHeight ? desired.Width + 16 : 280;
            double height = mode is SizeToContent.Height or SizeToContent.WidthAndHeight ? desired.Height + 40 : 220;
            if (constrained) { width = Math.Clamp(width, 200, 300); height = Math.Clamp(height, 160, 240); }
            Assert(peer.OuterSize == new Size(width, height) && window.ActualWidth == width && window.ActualHeight == height,
                $"Actual content layout drives only selected axes within constraints (desired={desired}, actual={peer.OuterSize})");
            Assert(peer.PresentCount > 0, "SizeToContent view produces a real retained frame");
        }
        Size manual = peer.OuterSize;
        window.SizeToContent = SizeToContent.Manual;
        element.SetDesired(new Size(260, 180)); window.UpdateLayout(); Drain();
        Assert(peer.OuterSize == manual, "Returning to Manual retains current size through content changes");
        window.Width = constrained ? 250 : 360; window.Height = constrained ? 200 : 280; window.UpdateLayout(); Drain();
        Assert(peer.OuterSize == new Size(constrained ? 250 : 360, constrained ? 200 : 280), "Manual dimensions regain control");
        window.Close();
    });
    foreach (SizeToContent mode in new[] { SizeToContent.Width, SizeToContent.Height, SizeToContent.WidthAndHeight })
    foreach (bool constrained in new[] { false, true })
    Run("size-to-content-async-" + mode + (constrained ? "-constrained" : "-free"), () =>
    {
        var element = new SizingElement();
        var window = new Window { Width = 280, Height = 220, Content = element };
        if (constrained) { window.MinWidth = 200; window.MinHeight = 160; window.MaxWidth = 300; window.MaxHeight = 240; }
        Size Expected(Size desired)
        {
            double width = mode is SizeToContent.Width or SizeToContent.WidthAndHeight ? desired.Width + 16 : 280;
            double height = mode is SizeToContent.Height or SizeToContent.WidthAndHeight ? desired.Height + 40 : 220;
            return constrained ? new Size(Math.Clamp(width, 200, 300), Math.Clamp(height, 160, 240)) : new Size(width, height);
        }
        window.Show(); var peer = factory.Last; Drain(); peer.DeferBounds = true;
        window.SizeToContent = mode; window.UpdateLayout(); Drain();
        Size first = Expected(new Size(160, 120));
        Assert(window.RestoreBounds.Size == first, "Initial content size enters pending bounds intent");
        int requests = peer.BoundsRequests;
        window.UpdateLayout(); Drain(); peer.PublishGeometry(new Rect(new Point(), new Size(280, 220))); window.UpdateLayout(); Drain();
        Assert(window.RestoreBounds.Size == first && peer.BoundsRequests == requests, "Unchanged layout and stale geometry do not resend pending size requests");
        element.SetDesired(new Size(400, 300)); window.UpdateLayout(); Drain();
        Size grown = Expected(new Size(400, 300));
        Assert(window.RestoreBounds.Size == grown, "Content growth supersedes pending size on selected axes");
        requests = peer.BoundsRequests;
        peer.PublishGeometry(new Rect(new Point(), first)); window.UpdateLayout(); Drain();
        Assert(window.RestoreBounds.Size == grown && peer.BoundsRequests == requests, "Older content acknowledgement cannot replace new content size or cause retries");
        peer.PublishGeometry(new Rect(new Point(), grown)); window.UpdateLayout(); Drain();
        Assert(peer.OuterSize == grown && window.ActualWidth == grown.Width && window.ActualHeight == grown.Height, "Growth acknowledgement reaches actual Window layout");
        element.SetDesired(new Size(140, 100)); window.UpdateLayout(); Drain();
        Size shrunk = Expected(new Size(140, 100));
        Assert(window.RestoreBounds.Size == shrunk, "Content shrink becomes the latest pending size");
        window.SizeToContent = SizeToContent.Manual;
        requests = peer.BoundsRequests;
        element.SetDesired(new Size(260, 180)); window.UpdateLayout(); Drain();
        Assert(window.RestoreBounds.Size == shrunk && peer.BoundsRequests == requests, "Manual return retains outstanding size and stops new automatic resize requests");
        peer.PublishGeometry(new Rect(new Point(), shrunk)); window.UpdateLayout(); Drain();
        Assert(peer.OuterSize == shrunk && peer.BoundsRequests == requests, "Outstanding acknowledgement completes after Manual return");
        window.Width = constrained ? 250 : 360; window.Height = constrained ? 200 : 280;
        var manual = new Size(constrained ? 250 : 360, constrained ? 200 : 280);
        peer.PublishGeometry(new Rect(new Point(), manual)); window.UpdateLayout(); Drain();
        Assert(window.ActualWidth == manual.Width && window.ActualHeight == manual.Height, "Explicit Manual dimensions use the same acknowledgement path");
        window.Close();
    });
    Run("size-to-content-fractional-acks-settle-without-resubmission", () =>
    {
        factory.InitialDpi = new DpiScale(1.5, 2);
        try
        {
            foreach (SizeToContent mode in new[] { SizeToContent.Width, SizeToContent.Height, SizeToContent.WidthAndHeight })
            foreach (bool ceiling in new[] { false, true })
            foreach (bool constrained in new[] { false, true })
            {
                Size Quantized(Size client) => new(
                    (ceiling ? Math.Ceiling(client.Width * 1.5) : Math.Floor(client.Width * 1.5)) / 1.5 + 16,
                    (ceiling ? Math.Ceiling(client.Height * 2) : Math.Floor(client.Height * 2)) / 2 + 40);
                var element = new SizingElement(); element.SetDesired(new Size(160.6, 120.4));
                var window = new Window { Width = 280, Height = 220, Content = element };
                if (constrained) { window.MinWidth = 200.6; window.MinHeight = 160.4; window.MaxWidth = 300.6; window.MaxHeight = 240.4; }
                window.Show(); var peer = factory.Last; Drain(); peer.DeferBounds = true;
                window.SizeToContent = mode; window.UpdateLayout(); Drain();
                Size first = Quantized(peer.LastClientRequest);
                int requests = peer.BoundsRequests;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    peer.PublishGeometry(first, peer.DesktopPosition); window.UpdateLayout(); Drain();
                    Assert(window.RestoreBounds.Size == first && peer.BoundsRequests == requests,
                        "Unchanged content retains acknowledged physical pixels without resubmitting desired fractional DIPs");
                }
                element.SetDesired(new Size(400.6, 300.4)); window.UpdateLayout(); Drain();
                Assert(peer.BoundsRequests > requests, "Changed content creates a new size intent after fractional settlement");
                Size grown = Quantized(peer.LastClientRequest);
                requests = peer.BoundsRequests;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    peer.PublishGeometry(grown, peer.DesktopPosition); window.UpdateLayout(); Drain();
                    Assert(window.RestoreBounds.Size == grown && peer.BoundsRequests == requests, "New fractional content size also settles without retries");
                }
                window.SizeToContent = SizeToContent.Manual;
                element.SetDesired(new Size(260.6, 180.4)); window.UpdateLayout(); Drain();
                Assert(window.RestoreBounds.Size == grown && peer.BoundsRequests == requests, "Manual return preserves acknowledged fractional geometry");
                window.Close();
            }
        }
        finally { factory.InitialDpi = new DpiScale(1, 1); }
    });
    Run("modal-capture-release-owner-close-hide-and-enable-reentry", () =>
    {
        foreach (string action in new[] { "close", "hide", "disable", "throw" })
        {
            var owner = new Window(); owner.Show(); var peer = factory.Last;
            var survivor = new Window(); survivor.Show(); var survivorPeer = factory.Last;
            var releaseFailure = new MarkerException();
            peer.ReleasingCapture = () =>
            {
                if (action == "close") owner.Close();
                else if (action == "hide") owner.Hide();
                else if (action == "disable") owner.IsEnabled = false;
                else throw releaseFailure;
            };
            var dialog = new Window();
            DispatcherOperation close = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(dialog.Close));
            if (action == "throw")
            {
                try { dialog.ShowDialog(); throw new InvalidDataException("Expected capture release failure"); }
                catch (MarkerException exception) { Assert(ReferenceEquals(exception, releaseFailure), "Capture release failure retained after restoration"); }
                close.Abort(); dialog.Close();
            }
            else Assert(dialog.ShowDialog() == false, "Capture-release callback does not abort modal entry");
            Assert(survivorPeer.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Surviving owner and modal scope restored");
            if (action == "close") Assert(peer.DisposeCount == 1 && PresentationSource.FromVisual(owner) == null, "Reentrant owner close releases peer");
            else if (action == "hide") Assert(!peer.IsVisible && peer.IsEnabled, "Reentrant owner hide stays hidden and enabled");
            else if (action == "disable") Assert(!owner.IsEnabled && !peer.IsEnabled, "Reentrant IsEnabled edit remains disabled");
            else Assert(peer.IsEnabled, "Failed release restores the visited owner");
            owner.Close(); survivor.Close();
        }
    });
    Run("modal-restores-all-windows-and-preserves-first-failure", () =>
    {
        foreach (string primary in new[] { "none", "body", "pop" })
        {
            var first = new Window(); first.Show(); var firstPeer = factory.Last;
            var second = new Window(); second.Show(); var secondPeer = factory.Last;
            var restored = new HashSet<ManagedPeer>();
            var cleanupFailure = new MarkerException(); var primaryFailure = new MarkerException();
            void Restore(ManagedPeer peer, bool enabled)
            {
                if (!enabled) return;
                restored.Add(peer);
                if (restored.Count == 1) throw cleanupFailure;
            }
            firstPeer.BeforeSetEnabled = enabled => Restore(firstPeer, enabled);
            secondPeer.BeforeSetEnabled = enabled => Restore(secondPeer, enabled);
            EventHandler leave = (_, _) => throw primaryFailure;
            if (primary == "pop") System.Windows.Interop.ComponentDispatcher.LeaveThreadModal += leave;
            var dialog = new Window();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (primary == "body") throw primaryFailure;
                dialog.Close();
            }));
            try { dialog.ShowDialog(); throw new InvalidDataException("Expected modal failure"); }
            catch (MarkerException exception) { Assert(ReferenceEquals(exception, primary == "none" ? cleanupFailure : primaryFailure), "Original primary exception object retained"); }
            finally { if (primary == "pop") System.Windows.Interop.ComponentDispatcher.LeaveThreadModal -= leave; }
            Assert(restored.Contains(firstPeer) && restored.Contains(secondPeer) && !System.Windows.Interop.ComponentDispatcher.IsThreadModal,
                "Every owner restoration is attempted and modal scope is balanced");
            firstPeer.BeforeSetEnabled = null; secondPeer.BeforeSetEnabled = null;
            first.IsEnabled = false; first.IsEnabled = true; second.IsEnabled = false; second.IsEnabled = true;
            Assert(firstPeer.IsEnabled && secondPeer.IsEnabled, "All modal flags reset even after cleanup throws");
            dialog.Close(); first.Close(); second.Close();
        }
    });
    Run("modal-restore-callout-owner-lifetime-and-activation-failure", () =>
    {
        var first = new Window(); first.Show(); var firstPeer = factory.Last;
        var second = new Window(); second.Show(); var secondPeer = factory.Last;
        bool closed = false;
        firstPeer.BeforeSetEnabled = enabled => { if (enabled) { second.Close(); closed = true; } };
        var dialog = new Window(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(dialog.Close));
        Assert(dialog.ShowDialog() == false && closed && secondPeer.Disposed && firstPeer.IsEnabled, "Restoration callout can close another owner safely");
        firstPeer.BeforeSetEnabled = null; first.Activate();
        var activationFailure = new MarkerException(); firstPeer.BeforeActivate = () => throw activationFailure;
        var next = new Window(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(next.Close));
        try { next.ShowDialog(); throw new InvalidDataException("Expected activation cleanup failure"); }
        catch (MarkerException exception) { Assert(ReferenceEquals(exception, activationFailure), "First activation failure retained after restoration"); }
        Assert(firstPeer.IsEnabled && !System.Windows.Interop.ComponentDispatcher.IsThreadModal, "Enable restoration and modal scope survive activation failure");
        firstPeer.BeforeActivate = null; next.Close(); first.Close();
    });
    Run("initial-size-intent-does-not-require-unchanged-show-metrics", () =>
    {
        try
        {
            foreach (bool showMetrics in new[] { false, true })
            foreach (bool sourceEdit in new[] { false, true })
            foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
            {
                factory.ReportMetricsOnShow = showMetrics;
                var window = new Window { Width = 400, Height = 300 };
                if (sourceEdit) window.SourceInitialized += (_, _) => { window.Width = 420; window.Height = 310; };
                window.Show(); var peer = factory.Last;
                window.WindowState = state;
                Assert(window.Width == peer.OuterSize.Width && window.Height == peer.OuterSize.Height,
                    "Programmatic state displays actual dimensions without requiring an unchanged Show metrics event");
                window.Width = 520;
                Assert(window.Width == 520 && window.Height == peer.OuterSize.Height && window.RestoreBounds.Height == (sourceEdit ? 310 : 300),
                    "After-show width edit preserves unedited initial restore height and displayed state height");
                window.WindowState = WindowState.Normal;
                Assert(peer.OuterSize == new Size(520, sourceEdit ? 310 : 300), "Normal restores retained initial height and new width");
                window.Close();
            }
        }
        finally { factory.ReportMetricsOnShow = true; }
    });
    Run("source-initialization-retry-does-not-inherit-peer-requests", () =>
    {
        try
        {
            foreach (bool showMetrics in new[] { false, true })
            {
                factory.ReportMetricsOnShow = showMetrics;
                var window = new Window { Width = 320, Height = 240 };
                EventHandler fail = (_, _) => throw new MarkerException();
                window.SourceInitialized += fail;
                try { window.Show(); throw new InvalidDataException("Expected source initialization failure"); }
                catch (MarkerException) { }
                Assert(factory.Last.Disposed && PresentationSource.FromVisual(window) == null, "Failed initialization releases source and peer");
                window.SourceInitialized -= fail;
                window.Width = 400; window.Height = 300;
                window.Show();
                Assert(factory.Last.OuterSize == new Size(400, 300), "Replacement peer receives current bounds instead of previous peer acknowledgements");
                window.Close();
            }
        }
        finally { factory.ReportMetricsOnShow = true; }
    });
    Run("async-bounds-intent-survives-stale-snapshots-and-older-acks", () =>
    {
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
        foreach (bool large in new[] { false, true })
        {
            var window = new Window { Left = 100, Top = 80, Width = 400, Height = 300,
                MinWidth = 200, MinHeight = 160, MaxWidth = 600, MaxHeight = 500 };
            window.Show(); var peer = factory.Last;
            window.WindowState = state;
            window.Width = large ? 800 : 100; window.Height = large ? 650 : 80;
            window.Left = -120; window.Top = 60;
            peer.DeferBounds = true;
            int moves = 0; Point observedPosition = default;
            window.LocationChanged += (_, _) => { moves++; observedPosition = peer.DesktopPosition; };
            window.WindowState = WindowState.Normal;
            var target = new Rect(-120, 60, large ? 600 : 200, large ? 500 : 160);
            Assert(window.RestoreBounds == target, "Normal clamps retained intent before asynchronous acknowledgement");
            int requests = peer.BoundsRequests;
            int previousMoves = moves;
            peer.PublishGeometry(new Rect(110, 90, 400, 300));
            Assert(window.RestoreBounds == target && peer.BoundsRequests == requests, "Stale Normal geometry neither overwrites target nor retries requests");
            Assert(moves > previousMoves && observedPosition == new Point(110, 90) && window.Left == -120 && window.Top == 60,
                "Actual movement remains observable while requested position axes stay pending");
            window.Width = 520; window.Height = 380; window.Left = -140; window.Top = 70;
            var newer = new Rect(-140, 70, 520, 380);
            requests = peer.BoundsRequests;
            peer.PublishGeometry(target);
            Assert(window.RestoreBounds == newer && peer.BoundsRequests == requests, "Older acknowledgement cannot replace newer intent or cause retries");
            peer.PublishGeometry(newer);
            Assert(window.RestoreBounds == newer && window.Width == 520 && window.Height == 380 && window.Left == -140 && window.Top == 70,
                "Latest acknowledgement settles all four public axes");
            var userResize = new Rect(-100, 90, 450, 350);
            peer.PublishGeometry(userResize);
            Assert(window.RestoreBounds == userResize && window.Width == 450 && window.Height == 350 && window.Left == -100 && window.Top == 90,
                "Native user move/resize updates normal geometry after acknowledgement");
            window.Close();
        }
    });
    Run("async-bounds-quantization-partial-acks-and-integer-pixels", () =>
    {
        factory.InitialDpi = new DpiScale(1.5, 2);
        try
        {
            foreach (bool ceiling in new[] { false, true })
            {
                double Quantize(double value, double scale) => (ceiling ? Math.Ceiling(value * scale) : Math.Floor(value * scale)) / scale;
                var window = new Window { Left = 100, Top = 80, Width = 400, Height = 300 };
                window.Show(); var peer = factory.Last; peer.DeferBounds = true;
                window.Width = 520.6; window.Height = 380.4; window.Left = -120.4; window.Top = 60.4;
                Point position = peer.LastDesktopRequest;
                double width = Quantize(peer.LastClientRequest.Width, 1.5) + 16;
                double height = Quantize(peer.LastClientRequest.Height, 2) + 40;
                peer.PublishGeometry(new Size(width, 300), new Point(position.X, 180));
                Assert(window.RestoreBounds == new Rect(position.X / 1.5, 60.4, width, 380.4), "Partial acknowledgement settles only its matching width and position axes");
                window.Width = 540;
                int requests = peer.BoundsRequests;
                peer.PublishGeometry(new Size(width, height), position);
                Assert(window.RestoreBounds == new Rect(position.X / 1.5, position.Y / 2, 540, height) && peer.BoundsRequests == requests,
                    "Rounded older acknowledgement settles unchanged axes and preserves newer width");
                peer.PublishGeometry(new Size(540 + 1 / 1.5, height), position);
                Assert(window.RestoreBounds.Width == 540 && peer.BoundsRequests == requests, "Integer physical request rejects the adjacent pixel without retrying");
                peer.PublishGeometry(new Size(540, height), position);
                peer.PublishGeometry(new Size(490, 360), new Point(-225, 140));
                Assert(window.RestoreBounds == new Rect(-150, 70, 490, 360) && peer.BoundsRequests == requests,
                    "Normal native move/resize resumes on both axes after exact acknowledgement");
                window.Close();
            }
        }
        finally { factory.InitialDpi = new DpiScale(1, 1); }
    });
    Run("async-fractional-constraints-do-not-retry-acknowledged-pixels", () =>
    {
        factory.InitialDpi = new DpiScale(1.5, 2);
        try
        {
            foreach (bool minimum in new[] { false, true })
            foreach (bool ceiling in new[] { false, true })
            foreach (bool widthFirst in new[] { false, true })
            {
                double Quantize(double value, double scale) => (ceiling ? Math.Ceiling(value * scale) : Math.Floor(value * scale)) / scale;
                var window = new Window { Width = minimum ? 400 : 600, Height = minimum ? 300 : 500 };
                window.Show(); var peer = factory.Last; peer.DeferBounds = true;
                if (minimum) { window.MinWidth = 520.6; window.MinHeight = 380.4; }
                else { window.MaxWidth = 520.6; window.MaxHeight = 380.4; }
                var size = new Size(Quantize(peer.LastClientRequest.Width, 1.5) + 16, Quantize(peer.LastClientRequest.Height, 2) + 40);
                int requests = peer.BoundsRequests;
                var partial = widthFirst ? new Size(size.Width, peer.OuterSize.Height) : new Size(peer.OuterSize.Width, size.Height);
                peer.PublishGeometry(partial, peer.DesktopPosition);
                peer.PublishGeometry(partial, peer.DesktopPosition);
                Assert((widthFirst ? window.RestoreBounds.Width == size.Width : window.RestoreBounds.Height == size.Height) && peer.BoundsRequests == requests,
                    "Acknowledged fractional axis is not reclamped while the other axis remains pending");
                peer.PublishGeometry(size, peer.DesktopPosition);
                peer.PublishGeometry(size, peer.DesktopPosition);
                Assert(window.RestoreBounds.Size == size && window.Width == size.Width && window.Height == size.Height && peer.BoundsRequests == requests,
                    "Measured fractional constraint acknowledgement publishes actual pixels without a clamp/resubmit loop");
                window.Close();
            }
        }
        finally { factory.InitialDpi = new DpiScale(1, 1); }
    });
    foreach (string intent in new[] { "size", "position", "both" })
    Run("transient-normal-match-still-sends-" + intent + "-intent", () =>
    {
        try
        {
            foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Minimized })
            foreach (bool geometryFirst in new[] { false, true })
            foreach (bool belowMinimum in new[] { false, true })
            {
                factory.GeometryFirst = geometryFirst;
                var original = new Rect(100, 80, 320, 200);
                var window = new Window { Left = original.X, Top = original.Y, Width = original.Width, Height = original.Height,
                    MinWidth = 200, MinHeight = 120, MaxWidth = 500, MaxHeight = 400 };
                window.Show(); var peer = factory.Last;
                window.WindowState = state;
                Point targetPosition = intent == "size" ? original.TopLeft : peer.DesktopPosition;
                Size targetSize = intent == "position" ? original.Size : belowMinimum ? new Size(200, 120) : new Size(500, 400);
                if (intent != "position") { window.Width = belowMinimum ? 100 : 800; window.Height = belowMinimum ? 80 : 650; }
                if (intent != "size") { window.Left = targetPosition.X; window.Top = targetPosition.Y; }
                var target = new Rect(targetPosition, targetSize);
                peer.TransientNormalBounds = target;
                peer.DeferBounds = true;
                int sizeRequests = peer.ClientSizeRequests, positionRequests = peer.DesktopPositionRequests;
                window.WindowState = WindowState.Normal;
                int queuedSizes = peer.ClientSizeRequests - sizeRequests, queuedPositions = peer.DesktopPositionRequests - positionRequests;
                Size queuedSize = peer.LastClientRequest; Point queuedPosition = peer.LastDesktopRequest;
                peer.PublishGeometry(original); window.UpdateLayout(); Drain();
                Assert(peer.ClientSizeRequests - sizeRequests == queuedSizes && peer.DesktopPositionRequests - positionRequests == queuedPositions,
                    "Late old restore and unchanged layout do not retry a sent intent");
                if (queuedPositions != 0) peer.PublishGeometry(peer.OuterSize, queuedPosition);
                if (queuedSizes != 0) peer.PublishGeometry(new Size(queuedSize.Width + 16, queuedSize.Height + 40), peer.DesktopPosition);
                window.UpdateLayout(); Drain();
                Assert(new Rect(peer.DesktopPosition, peer.OuterSize) == target && window.RestoreBounds == target,
                    "Actually queued explicit requests win over a late old Normal restore despite an initially matching snapshot");
                Assert(queuedSizes == (intent == "position" ? 0 : 1) && queuedPositions == (intent == "size" ? 0 : 1),
                    "Each newly pending axis pair issues exactly one peer request even when current geometry matches");
                Assert(peer.ClientSizeRequests - sizeRequests == queuedSizes && peer.DesktopPositionRequests - positionRequests == queuedPositions,
                    "Acknowledged intent and unchanged layout issue no further requests");
                window.Close();
            }
        }
        finally { factory.GeometryFirst = false; }
    });
    Run("application-window-main-and-owned-collections", () =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var main = new Window();
        Assert(app.MainWindow == main && app.Windows.Count == 1, "Application tracks constructed main window");
        main.Show();
        var child = new Window { Owner = main }; child.Show();
        Assert(app.Windows.Count == 2 && main.OwnedWindows.Count == 1, "Application tracks owned child");
        main.Close();
        Assert(app.Windows.Count == 0 && app.MainWindow == null && main.OwnedWindows.Count == 0, "Close removes main/owned application entries");
        foreach (bool closing in new[] { true, false })
        {
            var owner = new Window(); owner.Show(); var ownerPeer = factory.Last;
            var failingChild = new Window { Owner = owner }; failingChild.Show(); var childPeer = factory.Last;
            var sibling = new Window { Owner = owner }; sibling.Show(); var siblingPeer = factory.Last;
            if (closing) failingChild.Closing += (_, _) => throw new MarkerException();
            else failingChild.Closed += (_, _) => throw new MarkerException();
            try { owner.Close(); throw new InvalidDataException("Expected owned close failure"); }
            catch (MarkerException) { }
            Assert(ownerPeer.DisposeCount == 1 && childPeer.DisposeCount == 1 && siblingPeer.DisposeCount == 1, "Owned exception releases all three peers");
            Assert(PresentationSource.FromVisual(owner) == null && owner.OwnedWindows.Count == 0, "Owned exception detaches owner source/collection");
            Assert(app.Windows.Count == 0 && app.MainWindow == null, "Owned exception clears application/main entries");
        }
    });

    MessageBoxCases.Exercise(factory, Run);

}) { IsBackground = true };
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start();
if (!thread.Join(TimeSpan.FromSeconds(30))) checks.Add(new("watchdog", "fail", "Window lifecycle exceeded 30 seconds."));
foreach (ApplicationRunResult result in ApplicationRunCases.RunAll())
    checks.Add(new(result.name, result.status, result.detail));
Console.WriteLine(JsonSerializer.Serialize(new { probe = "window-lifecycle-integration", os = RuntimeInformation.OSDescription,
    scope = "real source-built Window/PresentationSource with managed contract peer and process-isolated public Application.Run; no native host or raw input proof", checks }, new JsonSerializerOptions { WriteIndented = true }));
return checks.Any(check => check.status == "fail") ? 1 : 0;

static void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}
static void Assert(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
sealed record Check(string name, string status, string detail);
sealed class MarkerException : Exception;
sealed class ManagedFactory : IPortableWindowFactory
{
    public ManagedPeer Last { get; private set; } = null!;
    public int CreateCount { get; private set; }
    public bool FailNextShow { get; set; }
    public bool GeometryFirst { get; set; }
    public bool DecorateOnShow { get; set; }
    public bool ReportMetricsOnShow { get; set; } = true;
    public DpiScale InitialDpi { get; set; } = new(1, 1);
    private ManagedPeer? _active;
    public void Activate(ManagedPeer peer)
    {
        if (_active != peer) _active?.Deactivate();
        _active = peer;
    }
    public IPortableWindowPeer CreateWindow(PresentationSource source, PortableWindowOptions options)
    {
        CreateCount++; Last = new ManagedPeer(this, options); return Last;
    }
}
sealed class ManagedPeer : IPortableWindowPeer
{
    private readonly ManagedFactory _factory;
    public ManagedPeer(ManagedFactory factory, PortableWindowOptions options)
    {
        _factory = factory; Title = options.Title; Owner = options.Owner; IsEnabled = options.IsEnabled;
        ShowInTaskbar = options.ShowInTaskbar;
        _initialState = options.WindowState; DpiScale = factory.InitialDpi;
        if (factory.DecorateOnShow) _frameSize = new Size();
        if (options.DesktopPosition is Point position) { DesktopPosition = position; _positionRequested = true; }
    }
    private readonly WindowState _initialState;
    private Size _normalClientSize = new(624, 440);
    private Point _normalPosition;
    private Size _frameSize = new(16, 40);
    private bool _positionRequested;
    public Size ClientSize { get; private set; } = new(624, 440);
    public Size OuterSize => new(ClientSize.Width + _frameSize.Width, ClientSize.Height + _frameSize.Height);
    public Point DesktopPosition { get; private set; }
    public bool DeferBounds { get; set; }
    public int BoundsRequests { get; private set; }
    public int ClientSizeRequests { get; private set; }
    public int DesktopPositionRequests { get; private set; }
    public Rect? TransientNormalBounds { get; set; }
    public Size LastClientRequest { get; private set; }
    public Point LastDesktopRequest { get; private set; }
    public void SetDesktopPosition(Point position)
    {
        BoundsRequests++;
        DesktopPositionRequests++;
        LastDesktopRequest = position;
        if (DeferBounds) return;
        DesktopPosition = position;
        _positionRequested = true;
        if (State == WindowState.Normal) _normalPosition = position;
        MetricsChanged?.Invoke();
    }
    public DpiScale DpiScale { get; private set; } = new(1, 1);
    public void SetDpi(DpiScale dpi) { DpiScale = dpi; MetricsChanged?.Invoke(); }
    public bool IsVisible { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsActive { get; private set; }
    public bool Disposed { get; private set; }
    public int DisposeCount { get; private set; }
    public int CloseMode { get; set; }
    public int PresentCount { get; private set; }
    public int RedPixelCount { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public string Title { get; private set; }
    public bool ShowInTaskbar { get; private set; } = true;
    public bool TaskbarAtShow { get; private set; }
    public List<bool> TaskbarRequests { get; } = new();
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public IPortableWindowPeer? Owner { get; private set; }
    public WindowState State { get; private set; }
    public WindowState WindowState => State;
    public Size Minimum { get; private set; }
    public Size Maximum { get; private set; } = new(double.PositiveInfinity, double.PositiveInfinity);
    public event Action? CloseRequested;
    public event Action<bool>? ActivationChanged;
    public event Action<WindowState>? StateChanged;
    public event Action? MetricsChanged;
    public event Action? Exposed;
    public event Action? Closed;
    public event Action<InputReport>? RawInput;
    public Action? ReleasingCapture { get; set; }
    public Action<bool>? BeforeSetEnabled { get; set; }
    public Action? BeforeActivate { get; set; }
    public void SetTitle(string title) => Title = title;
    public void SetShowInTaskbar(bool show) { TaskbarRequests.Add(show); ShowInTaskbar = show; }
    public void SetClientSize(Size size)
    {
        BoundsRequests++;
        ClientSizeRequests++;
        LastClientRequest = size;
        if (DeferBounds) return;
        ClientSize = size;
        if (State == WindowState.Normal) _normalClientSize = size;
        MetricsChanged?.Invoke();
    }
    public void PublishGeometry(Rect bounds)
        => PublishGeometry(bounds.Size, new Point(bounds.X * DpiScale.DpiScaleX, bounds.Y * DpiScale.DpiScaleY));
    public void PublishGeometry(Size size, Point position)
    {
        ClientSize = new Size(size.Width - _frameSize.Width, size.Height - _frameSize.Height);
        DesktopPosition = position;
        if (State == WindowState.Normal) { _normalClientSize = ClientSize; _normalPosition = DesktopPosition; }
        MetricsChanged?.Invoke();
    }
    public void SetClientSizeConstraints(Size minimum, Size maximum) { Minimum = minimum; Maximum = maximum; }
    public void SetOwner(IPortableWindowPeer? owner) => Owner = owner;
    public void SetEnabled(bool enabled) { BeforeSetEnabled?.Invoke(enabled); IsEnabled = enabled; }
    public void SetWindowState(WindowState state)
    {
        if (State == state) return;
        if (State == WindowState.Normal) { _normalClientSize = ClientSize; _normalPosition = DesktopPosition; }
        State = state;
        void Geometry()
        {
            _frameSize = state == WindowState.Normal ? new Size(16, 40) : new Size();
            ClientSize = state == WindowState.Normal ? _normalClientSize : state == WindowState.Maximized ? new Size(1280, 760) : new Size(160, 40);
            DesktopPosition = state == WindowState.Normal ? _normalPosition : state == WindowState.Maximized ? new Point(-8, -8) : new Point(-32000, -32000);
            if (state == WindowState.Normal && TransientNormalBounds is Rect transient)
            {
                ClientSize = new Size(transient.Width - _frameSize.Width, transient.Height - _frameSize.Height);
                DesktopPosition = new Point(transient.X * DpiScale.DpiScaleX, transient.Y * DpiScale.DpiScaleY);
                TransientNormalBounds = null;
            }
            MetricsChanged?.Invoke();
        }
        if (_factory.GeometryFirst) { Geometry(); if (State == state) StateChanged?.Invoke(state); }
        else { StateChanged?.Invoke(state); Geometry(); }
    }
    public void Show(bool activate)
    {
        ShowCount++; TaskbarAtShow = ShowInTaskbar;
        IsVisible = true;
        if (_factory.DecorateOnShow)
        {
            _frameSize = new Size(16, 40);
            if (!_positionRequested) { DesktopPosition = new Point(120, 80); _normalPosition = DesktopPosition; }
        }
        if (_factory.ReportMetricsOnShow) MetricsChanged?.Invoke();
        if (_factory.FailNextShow) { _factory.FailNextShow = false; throw new MarkerException(); }
        if (_initialState != WindowState.Normal && State == WindowState.Normal) SetWindowState(_initialState);
        if (activate) Activate(); Exposed?.Invoke();
    }
    public void Hide() { HideCount++; IsVisible = false; IsActive = false; ActivationChanged?.Invoke(false); }
    public bool Activate() { BeforeActivate?.Invoke(); _factory.Activate(this); IsActive = true; ActivationChanged?.Invoke(true); return true; }
    public void Deactivate() { IsActive = false; ActivationChanged?.Invoke(false); }
    public bool CaptureMouse() => true;
    public void ReleaseMouseCapture() => ReleasingCapture?.Invoke();
    public void RequestClose() => CloseRequested?.Invoke();
    public void Close()
    {
        if (CloseMode == 1) return;
        if (CloseMode == 2) throw new MarkerException();
        IsVisible = false; Deactivate();
        Action? acknowledgement = Closed;
        acknowledgement?.Invoke();
        if (CloseMode == 3) acknowledgement?.Invoke();
    }
    public void Dispose() { Disposed = true; DisposeCount++; }
    public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        PresentCount++; PixelWidth = width; PixelHeight = height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                if (pixels[offset + 2] == 255 && pixels[offset + 1] == 0 && pixels[offset] == 0 && pixels[offset + 3] == 255) RedPixelCount++;
            }
    }
    public void Report(InputReport report) => RawInput?.Invoke(report);
}

sealed class MeasuringElement : FrameworkElement
{
    protected override Size MeasureOverride(Size constraint) => new(100, 80);
    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(Brushes.Red, null, new Rect(RenderSize));
}
sealed class SizingElement : FrameworkElement
{
    private Size _desired = new(160, 120);
    public void SetDesired(Size value) { _desired = value; InvalidateMeasure(); }
    protected override Size MeasureOverride(Size constraint) => _desired;
    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(Brushes.Blue, null, new Rect(RenderSize));
}
