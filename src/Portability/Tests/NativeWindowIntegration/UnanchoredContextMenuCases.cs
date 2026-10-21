extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Controls.Primitives;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Media.Animation;

internal static class UnanchoredContextMenuCases
{
    internal static void Exercise(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath)
    {
        var menu = new ContextMenu { FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
        var instructions = new MenuItem { Header = "Instructions" };
        var command = new MenuItem { Header = "Utility" };
        instructions.Items.Add(command); menu.Items.Add(instructions);
        var button = new Button { Content = "Add", Width = 60, Height = 40, Style = CreateStyle(menu) };
        var window = new Window { Title = "Unanchored ContextMenu storyboard", Left = 120, Top = 100,
            Width = 360, Height = 260, Content = button };
        int clicks = 0, opened = 0, closed = 0;
        int commands = 0;
        command.Click += (_, _) => commands++;
        button.Click += (_, _) => clicks++;
        menu.Opened += (_, _) => opened++;
        menu.Closed += (_, _) => closed++;
        Exception? primary = null;
        try
        {
            window.Show(); drain();
            var ownerSource = PresentationSource.FromVisual(window)!;
            IntPtr ownerXid = OwnedNativeAccess.Handle(ownerSource);
            wait(() => native.IsMapped(ownerXid));
            native.Focus(ownerXid); wait(() => ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource));
            Require(menu.PlacementTarget == null, "The original programmatic menu must start without a placement target.");
            Click(native, button);
            wait(() => opened == 1 && PresentationSource.FromVisual(menu) is { IsDisposed: false });
            drain();
            var source = PresentationSource.FromVisual(menu)!;
            IntPtr xid = OwnedNativeAccess.Handle(source);
            Require(clicks == 1 && menu.PlacementTarget == null &&
                LogicalTreeHelper.GetParent(menu) is Popup { PlacementTarget: null, Placement: PlacementMode.MousePoint } &&
                !ReferenceEquals(source, ownerSource) && native.IsMapped(xid),
                "The Click storyboard did not create a distinct mapped, targetless mouse-point menu.");
            VerifyOwner(native, menu, ownerSource);
            var pointer = native.PointerPosition;
            var origin = native.ClientOrigin(xid);
            PopupCases.Observations.Add(new { kind = "unanchored-ContextMenu-storyboard", clicks, opened,
                nullTarget = menu.PlacementTarget == null, xid = xid.ToInt64(), ownerXid = ownerXid.ToInt64(),
                pointerX = pointer.X, pointerY = pointer.Y, popupX = origin.X, popupY = origin.Y,
                nativeOwner = native.TransientOwner(xid).ToInt64(), keyboardSource = Keyboard.PrimaryDevice.ActiveSource?.GetType().FullName });
            native.Key((char)0xff1b, true); native.Key((char)0xff1b, false);
            wait(() => closed == 1 && source.IsDisposed); drain();
            Require(!menu.IsOpen && !native.RootChildren.Contains(xid) && Mouse.Captured == null && native.CanAcquirePointerGrab(),
                "Native Escape retained the targetless menu source/window or capture.");

            Click(native, button);
            wait(() => opened == 2 && PresentationSource.FromVisual(menu) is { IsDisposed: false }); drain();
            source = PresentationSource.FromVisual(menu)!; xid = OwnedNativeAccess.Handle(source);
            Move(native, instructions);
            wait(() => instructions.IsSubmenuOpen && PresentationSource.FromVisual(command) is { IsDisposed: false }); drain();
            var submenuSource = PresentationSource.FromVisual(command)!;
            IntPtr submenuXid = OwnedNativeAccess.Handle(submenuSource);
            Click(native, command);
            wait(() => commands == 1 && closed == 2 && source.IsDisposed && submenuSource.IsDisposed); drain();
            Require(!native.RootChildren.Contains(xid) && !native.RootChildren.Contains(submenuXid) && native.CanAcquirePointerGrab(),
                "The genuine submenu command did not close both native sources and release capture.");

            Click(native, button); wait(() => opened == 3); drain();
            source = PresentationSource.FromVisual(menu)!;
            System.ComponentModel.CancelEventHandler cancel = (_, e) => e.Cancel = true;
            window.Closing += cancel;
            try { window.Close(); }
            finally { window.Closing -= cancel; }
            Require(menu.IsOpen && !source.IsDisposed, "Canceled owner close dismissed the unanchored menu.");
            native.Click(ownerXid, 5, 5);
            wait(() => closed == 3 && source.IsDisposed); drain();
            Require(Mouse.Captured == null && native.CanAcquirePointerGrab(), "Outside dismissal retained capture.");

            ExerciseInputOwner(native, wait, drain, window, button, menu);

            native.Focus(ownerXid); wait(() => ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource));
            Click(native, button); wait(() => menu.IsOpen && PresentationSource.FromVisual(menu) is { IsDisposed: false });
            source = PresentationSource.FromVisual(menu)!;
            window.Hide(); wait(() => !menu.IsOpen && source.IsDisposed); drain();
            RejectNativeAllocation(OwnedNativeAccess.Peer(ownerSource), "hidden");
            window.Show(); wait(() => native.IsMapped(ownerXid));
            native.Focus(ownerXid); wait(() => ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource));
            window.IsEnabled = false;
            RejectNativeAllocation(OwnedNativeAccess.Peer(ownerSource), "disabled");
            window.IsEnabled = true; window.Activate();
            wait(() => native.FocusedWindow == ownerXid && ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource));
            Click(native, button); wait(() => menu.IsOpen && PresentationSource.FromVisual(menu) is { IsDisposed: false });
            source = PresentationSource.FromVisual(menu)!; xid = OwnedNativeAccess.Handle(source);
            var popupPeer = OwnedNativeAccess.Peer(source);
            bool closingRejected = false;
            Action closing = () => { RejectNativeAllocation(OwnedNativeAccess.Peer(ownerSource), "closing"); closingRejected = true; };
            var closingEvent = popupPeer.GetType().GetEvent("OwnerClosing")!;
            closingEvent.AddEventHandler(popupPeer, closing);
            try { window.Close(); }
            finally { closingEvent.RemoveEventHandler(popupPeer, closing); }
            wait(() => !menu.IsOpen && source.IsDisposed); drain();
            Require(closingRejected && !native.RootChildren.Contains(xid) && Mouse.Captured == null && native.CanAcquirePointerGrab(),
                "Accepted owner close retained a menu/source/grab or allowed allocation while closing.");
            ExerciseDetachedAndOwnerless(native, wait, drain, fontPath);
        }
        catch (Exception error)
        {
            primary = error;
            PopupCases.Observations.Add(new { kind = "unanchored-primary-failure", phase = "exercise", error = error.ToString() });
            throw;
        }
        finally
        {
            Cleanup(primary, () => menu.BeginAnimation(ContextMenu.IsOpenProperty, null),
                () => menu.IsOpen = false, window.Close, drain);
        }
    }

    private static void ExerciseInputOwner(X11Fixture native, Action<Func<bool>> wait, Action drain,
        Window owner, Button button, ContextMenu menu)
    {
        var other = new Window { Title = "Unanchored menu pointer-only window", Left = 560, Top = 100,
            Width = 300, Height = 220, ShowActivated = false, Background = Brushes.Blue };
        Exception? primary = null;
        string phase = "show-B";
        try
        {
            other.Show(); drain();
            Observe();
            var ownerSource = PresentationSource.FromVisual(owner)!;
            var otherSource = PresentationSource.FromVisual(other)!;
            IntPtr ownerXid = OwnedNativeAccess.Handle(ownerSource), otherXid = OwnedNativeAccess.Handle(otherSource);
            wait(() => native.IsMapped(otherXid));
            phase = "focus-B";
            native.Focus(otherXid);
            wait(() => native.FocusedWindow == otherXid && ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, otherSource));
            Observe();
            phase = "focus-A";
            native.Focus(ownerXid);
            wait(() => native.FocusedWindow == ownerXid && ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource));
            Observe();
            phase = "pointer-B";
            native.MovePointer(otherXid, 40, 50);
            wait(() => ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, otherSource));
            Observe();
            Require(ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, ownerSource), "Pointer-only movement changed keyboard ownership.");
            phase = "Click-storyboard";
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            wait(() => menu.IsOpen && PresentationSource.FromVisual(menu) is { IsDisposed: false }); drain();
            Observe();
            var source = PresentationSource.FromVisual(menu)!;
            VerifyOwner(native, menu, ownerSource);
            var pointer = native.PointerPosition; Point menuOrigin = menu.PointToScreen(new Point());
            PopupCases.Observations.Add(new { kind = "unanchored-focus-A-pointer-B", ownerXid = ownerXid.ToInt64(),
                pointerXid = otherXid.ToInt64(), pointerX = pointer.X, pointerY = pointer.Y, menuX = menuOrigin.X, menuY = menuOrigin.Y,
                opening = "programmatic Click storyboard after actual pointer-only movement", menu.Placement });
            Require(Math.Abs(menuOrigin.X - pointer.X) < 1 && Math.Abs(menuOrigin.Y - pointer.Y) < 1,
                "Targetless mouse-point placement used owner-local coordinates instead of the native cursor.");
            phase = "deactivate-to-B";
            native.Focus(otherXid);
            wait(() => !menu.IsOpen && source.IsDisposed); drain();
            Observe();
            Require(native.FocusedWindow == otherXid && native.CanAcquirePointerGrab(), "Deactivation cleanup stole focus or retained capture.");
        }
        catch (Exception error)
        {
            primary = error;
            Observe();
            PopupCases.Observations.Add(new { kind = "unanchored-primary-failure", phase, error = error.ToString() });
            throw;
        }
        finally
        {
            Cleanup(primary, () => menu.BeginAnimation(ContextMenu.IsOpenProperty, null),
                () => menu.IsOpen = false, other.Close, drain);
        }

        void Observe() => PopupCases.Observations.Add(new { kind = "unanchored-input-owner-phase", phase,
            menu.IsOpen, nativeFocus = native.FocusedWindow.ToInt64(), keyboardSource = SourceXid(Keyboard.PrimaryDevice.ActiveSource),
            mouseSource = SourceXid(Mouse.PrimaryDevice.ActiveSource), menuSource = SourceXid(PresentationSource.FromVisual(menu)) });
    }

    private static long SourceXid(PresentationSource? source) => source is { IsDisposed: false } ? OwnedNativeAccess.Handle(source).ToInt64() : 0;

    private static void Cleanup(Exception? primary, params Action[] actions)
    {
        var errors = new List<Exception>();
        foreach (Action action in actions)
        {
            try { action(); }
            catch (Exception error)
            {
                errors.Add(error);
                PopupCases.Observations.Add(new { kind = "unanchored-cleanup-failure", error = error.ToString() });
            }
        }
        if (primary == null && errors.Count != 0) throw new AggregateException("Unanchored menu cleanup failed.", errors);
    }

    private static void ExerciseDetachedAndOwnerless(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath)
    {
        var window = new Window { Title = "Detached target must not borrow input ownership", Width = 220, Height = 160 };
        var detached = new ContextMenu { PlacementTarget = new Border(),
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans") };
        detached.Items.Add(new MenuItem { Header = "Detached" });
        try
        {
            window.Show(); drain();
            var source = PresentationSource.FromVisual(window)!;
            wait(() => native.IsMapped(OwnedNativeAccess.Handle(source)));
            native.Focus(OwnedNativeAccess.Handle(source)); wait(() => ReferenceEquals(Keyboard.PrimaryDevice.ActiveSource, source));
            var before = native.RootChildren.ToHashSet();
            detached.IsOpen = true; wait(() => !detached.IsOpen); drain();
            Require(PresentationSource.FromVisual(detached) == null && before.SetEquals(native.RootChildren),
                "An explicit detached target borrowed the active source or leaked a native popup.");
        }
        finally { detached.IsOpen = false; window.Close(); drain(); }
        var ownerless = new ContextMenu(); ownerless.Items.Add(new MenuItem { Header = "Ownerless" });
        bool rejected = false;
        try { ownerless.IsOpen = true; }
        catch (PlatformNotSupportedException) { rejected = true; }
        finally { ownerless.IsOpen = false; drain(); }
        Require(rejected && PresentationSource.FromVisual(ownerless) == null && native.CanAcquirePointerGrab(),
            "A truly ownerless menu silently substituted an unrelated source or retained capture.");
    }

    private static void RejectNativeAllocation(object peer, string state)
    {
        bool rejected = false;
        try
        {
            object? window = OwnedNativeAccess.Call(peer, "CreateNativePopup", true);
            if (window is IDisposable disposable) disposable.Dispose();
        }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A native popup was allocated from a " + state + " owner.");
        PopupCases.Observations.Add(new { kind = "unanchored-native-allocation-rejected", state });
    }

    private static void VerifyOwner(X11Fixture native, ContextMenu menu, PresentationSource expected)
    {
        var source = PresentationSource.FromVisual(menu)!;
        var peer = OwnedNativeAccess.Peer(source);
        var owner = OwnedNativeAccess.Get(peer, "_popupOwner");
        Require(ReferenceEquals(OwnedNativeAccess.Get(owner, "Source"), expected) && menu.PlacementTarget == null &&
            native.TransientOwner(OwnedNativeAccess.Handle(source)) == OwnedNativeAccess.Handle(expected) &&
            native.FocusedWindow == OwnedNativeAccess.Handle(expected),
            "The targetless popup did not retain the selected keyboard input owner and native focus.");
    }

    private static void Move(X11Fixture native, FrameworkElement element)
    {
        var source = PresentationSource.FromVisual(element)!;
        IntPtr xid = OwnedNativeAccess.Handle(source);
        Point screen = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var origin = native.ClientOrigin(xid);
        native.MovePointer(xid, checked((int)Math.Round(screen.X - origin.X)), checked((int)Math.Round(screen.Y - origin.Y)));
    }

    internal static Style CreateStyle(ContextMenu menu)
    {
        var animation = new BooleanAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteBooleanKeyFrame(true, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        Storyboard.SetTargetProperty(animation, new PropertyPath("ContextMenu.IsOpen"));
        var storyboard = new Storyboard(); storyboard.Children.Add(animation);
        var trigger = new EventTrigger(Button.ClickEvent);
        trigger.Actions.Add(new BeginStoryboard { Storyboard = storyboard });
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, menu));
        style.Triggers.Add(trigger);
        return style;
    }

    private static void Click(X11Fixture native, FrameworkElement element)
    {
        var source = PresentationSource.FromVisual(element)!;
        IntPtr xid = OwnedNativeAccess.Handle(source);
        Point screen = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var origin = native.ClientOrigin(xid);
        native.Click(xid, checked((int)Math.Round(screen.X - origin.X)), checked((int)Math.Round(screen.Y - origin.Y)));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
