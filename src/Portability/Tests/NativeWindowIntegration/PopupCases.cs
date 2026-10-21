extern alias WpfFork;

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Controls.Primitives;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using Window = WpfFork::System.Windows.Window;
using Visual = WpfFork::System.Windows.Media.Visual;
using Thickness = WpfFork::System.Windows.Thickness;

internal static class PopupCases
{
    internal static readonly List<object> Observations = [];

    internal static void ContextMenuLifetime(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath)
    {
        var owner = new Border { Width = 120, Height = 40, Background = Brushes.Green };
        var window = new Window { Title = "Genuine portable ContextMenu lifetime", Width = 300, Height = 180,
            Content = owner, Background = Brushes.White };
        var menu = new ContextMenu { PlacementTarget = owner,
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
        var submenu = new MenuItem { Header = "Instructions" };
        var instruction = new MenuItem { Header = "Utility" };
        submenu.Items.Add(instruction);
        menu.Items.Add(submenu);
        int opened = 0, closed = 0;
        menu.Opened += (_, _) => opened++;
        menu.Closed += (_, _) => closed++;
        PresentationSource? previousSource = null;
        try
        {
            window.Show();
            wait(() => owner.IsLoaded && owner.ActualWidth > 0);
            drain();
            var ownerSource = PresentationSource.FromVisual(window)!;
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                native.MovePointer(PopupHandle(ownerSource), 70, 55);
                drain();
                menu.IsOpen = true;
                wait(() => opened == cycle && PresentationSource.FromVisual(menu) is { IsDisposed: false });
                drain();
                var source = PresentationSource.FromVisual(menu)!;
                IntPtr xid = PopupHandle(source);
                Require(LogicalTreeHelper.GetParent(menu) is Popup { PopupAnimation: PopupAnimation.None, IsOpen: true } &&
                    menu.Placement == PlacementMode.MousePoint && menu.Template is not null && menu.IsMeasureValid && menu.IsArrangeValid &&
                    source != ownerSource && source != previousSource && native.IsMapped(xid),
                    "The genuine ContextMenu did not retain default placement, disabled animation and a fresh mapped Popup source.");
                Require(!submenu.IsSubmenuOpen && submenu.ActualWidth > 0 && submenu.ActualHeight > 0,
                    "The submenu must be closed and laid out before native pointer hover.");
                var headerPoint = submenu.TransformToAncestor(source.RootVisual).Transform(
                    new WpfFork::System.Windows.Point(submenu.ActualWidth / 2, submenu.ActualHeight / 2));
                headerPoint = source.CompositionTarget.TransformToDevice.Transform(headerPoint);
                native.MovePointer(xid, (int)Math.Round(headerPoint.X), (int)Math.Round(headerPoint.Y));
                wait(() => submenu.IsMouseOver && submenu.IsSubmenuOpen &&
                    PresentationSource.FromVisual(instruction) is { IsDisposed: false });
                drain();
                var submenuSource = PresentationSource.FromVisual(instruction)!;
                IntPtr submenuXid = PopupHandle(submenuSource);
                Require(submenu.Role == MenuItemRole.SubmenuHeader && submenuSource != source && native.IsMapped(submenuXid),
                    "A real native pointer hover did not open the submenu through its normal timer and separate mapped Popup source.");
                menu.IsOpen = false;
                wait(() => closed == cycle && source.IsDisposed && submenuSource.IsDisposed);
                drain();
                Require(!menu.IsOpen && !submenu.IsSubmenuOpen && !native.RootChildren.Contains(xid) &&
                    !native.RootChildren.Contains(submenuXid) && Mouse.Captured is null && native.CanAcquirePointerGrab(),
                    "Closing the genuine ContextMenu retained its parent/submenu source, native window or pointer capture.");
                Observations.Add(new { kind = "genuine-context-menu-open-close", cycle, opened, closed,
                    xid = xid.ToInt64().ToString("x"), source.IsDisposed, submenuXid = submenuXid.ToInt64().ToString("x"),
                    submenuDisposed = submenuSource.IsDisposed, openedByNativeHover = true, animation = "None", placement = menu.Placement.ToString() });
                previousSource = source;
            }
        }
        finally { menu.IsOpen = false; window.Close(); drain(); }
    }

    internal static void VerifyToolTipAnimationResources()
    {
        foreach (var setting in new (ResourceKey Key, Func<bool> Read)[]
        {
            (SystemParameters.ToolTipAnimationKey, () => SystemParameters.ToolTipAnimation),
            (SystemParameters.ToolTipFadeKey, () => SystemParameters.ToolTipFade)
        })
        {
            Require(!setting.Read(), "Portable tooltip animation settings must be disabled.");
            var consumer = new ToggleButton();
            consumer.SetResourceReference(ToggleButton.IsCheckedProperty, setting.Key);
            Require(consumer.IsChecked == false, "The tooltip setting resource did not resolve its portable value.");
            consumer.Resources[setting.Key] = true;
            Require(consumer.IsChecked == true, "The tooltip setting resource ignored a local override.");
            consumer.Resources.Remove(setting.Key);
            Require(consumer.IsChecked == false, "Removing the tooltip setting override did not restore the portable value.");
        }
        var popup = new Popup { AllowsTransparency = true };
        popup.SetResourceReference(Popup.PopupAnimationProperty, SystemParameters.ToolTipPopupAnimationKey);
        Require(SystemParameters.ToolTipPopupAnimation == PopupAnimation.None && popup.PopupAnimation == PopupAnimation.None,
            "The tooltip popup animation resource did not resolve None.");
        popup.Resources[SystemParameters.ToolTipPopupAnimationKey] = PopupAnimation.Fade;
        Require(popup.PopupAnimation == PopupAnimation.Fade, "The tooltip popup animation resource ignored a local override.");
        popup.Resources.Remove(SystemParameters.ToolTipPopupAnimationKey);
        Require(popup.PopupAnimation == PopupAnimation.None, "Removing the tooltip popup animation override did not restore None.");
        Observations.Add(new { kind = "portable-tooltip-animation-resources", animation = false, fade = false,
            popupAnimation = popup.PopupAnimation.ToString(), localOverridesAndRemoval = true });
    }

    internal static void ToolTipLifetime(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath)
    {
        var owner = new Border { Width = 120, Height = 40, Background = Brushes.Green };
        var window = new Window { Title = "Genuine portable ToolTip lifetime", Width = 300, Height = 180,
            Content = owner, Background = Brushes.White };
        var tooltip = new ToolTip { Content = new Border { Width = 80, Height = 24, Background = Brushes.Red },
            PlacementTarget = owner, Placement = PlacementMode.Bottom,
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
        int opened = 0, closed = 0;
        tooltip.Opened += (_, _) => opened++;
        tooltip.Closed += (_, _) => closed++;
        PresentationSource? previousSource = null;
        try
        {
            window.Show();
            wait(() => owner.IsLoaded && owner.ActualWidth > 0);
            drain();
            var ownerSource = PresentationSource.FromVisual(window)!;
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                tooltip.IsOpen = true;
                wait(() => opened == cycle && PresentationSource.FromVisual(tooltip) is { IsDisposed: false });
                drain();
                var source = PresentationSource.FromVisual(tooltip)!;
                IntPtr xid = PopupHandle(source);
                Require(tooltip.Template is not null && tooltip.IsMeasureValid && tooltip.IsArrangeValid &&
                    source != ownerSource && source != previousSource && native.IsMapped(xid),
                    "The genuine ToolTip did not construct, lay out and map a fresh separate Popup source.");
                Require(Mouse.Captured is null && native.CanAcquirePointerGrab(), "The default ToolTip unexpectedly captured the pointer.");
                tooltip.IsOpen = false;
                wait(() => closed == cycle && source.IsDisposed);
                drain();
                Require(!tooltip.IsOpen && !native.RootChildren.Contains(xid) && Mouse.Captured is null && native.CanAcquirePointerGrab(),
                    "Closing the genuine ToolTip retained its Popup source, native window or pointer capture.");
                Observations.Add(new { kind = "genuine-tooltip-open-close", cycle, opened, closed,
                    xid = xid.ToInt64().ToString("x"), source.IsDisposed });
                previousSource = source;
            }
        }
        finally { tooltip.IsOpen = false; window.Close(); drain(); }
    }

    internal static void VerifyAutoScrollTime()
    {
        var profile = typeof(Mouse).Assembly.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider")!
            .GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var pointer = profile.GetType().GetProperty("PointerSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profile)!;
        int observedTime = (int)pointer.GetType().GetProperty("DoubleClickTimeMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pointer)!;
        var actual = (TimeSpan)typeof(ItemsControl).GetProperty("AutoScrollTimeout", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Require(actual == TimeSpan.FromMilliseconds(observedTime * 0.8), "ItemsControl did not preserve its 0.8 multiplier over the observed native pointer preference.");
        Observations.Add(new { kind = "autoscroll-settings-consumer", observedTimeMilliseconds = observedTime, actualMilliseconds = actual.TotalMilliseconds });
    }

    internal static void VerifyMenuDropAlignment()
    {
        Require(!SystemParameters.MenuDropAlignment, "The declared initial portable Classic menu alignment changed.");
        var consumer = new ToggleButton();
        consumer.SetResourceReference(ToggleButton.IsCheckedProperty, SystemParameters.MenuDropAlignmentKey);
        Require(consumer.IsChecked == false, "The original system resource key did not resolve the fixed portable preference.");
        consumer.Resources[SystemParameters.MenuDropAlignmentKey] = true;
        Require(consumer.IsChecked == true, "A real local resource override did not replace menu alignment.");
        consumer.Resources.Remove(SystemParameters.MenuDropAlignmentKey);
        Require(consumer.IsChecked == false, "Removing the local resource did not restore the original system setting.");
        Observations.Add(new { kind = "fixed-menu-drop-alignment-resource", productDefault = false, localOverride = true, removalFallback = false });
    }

    internal static void OpenByNativeClick(X11Fixture native, Action<Func<bool>> wait, Action drain, string fontPath, bool originZero = false, bool mouseOnly = false)
    {
        Window? window = null;
        ComboBox? combo = null;
        Popup? popup = null;
        var rawObservers = new List<(object Peer, EventInfo Event, Delegate Callback)>();
        string phase = "closed-control";
        EventHandler<FirstChanceExceptionEventArgs> observer = (_, e) =>
        {
            if (e.Exception is DllNotFoundException or TypeInitializationException or PlatformNotSupportedException)
                Observations.Add(new { kind = "first-chance-exception", phase, exception = e.Exception.ToString(), callerStack = Environment.StackTrace });
        };
        AppDomain.CurrentDomain.FirstChanceException += observer;
        try
        {
            Observations.Add(new { kind = "fixture-input-configuration", originZero, mouseOnly, keyboardGate = mouseOnly ? "Not run in this focused mouse-only process" : "Required" });
            combo = new ComboBox { Width = 180, Height = 32, Margin = new Thickness(originZero ? 0 : 20), IsEditable = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
            combo.Items.Add("First"); combo.Items.Add("Second"); combo.Items.Add("Third"); combo.SelectedIndex = 0;
            var nextButton = new Button { Content = "After popup", Width = 180, Height = 26, HorizontalAlignment = HorizontalAlignment.Left,
                FontFamily = combo.FontFamily, FontSize = 12 };
            var layoutRoot = new StackPanel();
            layoutRoot.Children.Add(combo); layoutRoot.Children.Add(nextButton);
            window = new Window { Title = "Genuine Classic portable Popup", Width = 320, Height = originZero ? 32 : 180, Content = layoutRoot, Background = Brushes.White };
            foreach (UIElement element in new UIElement[] { window, combo })
            {
                string label = element.GetType().Name;
                foreach (var routed in new[] { Mouse.GotMouseCaptureEvent, Mouse.LostMouseCaptureEvent })
                    element.AddHandler(routed, new MouseEventHandler((_, e) => Snapshot(label + ":" + e.RoutedEvent.Name)), true);
                foreach (var routed in new[] { Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent, Mouse.PreviewMouseUpEvent, Mouse.MouseUpEvent })
                    element.AddHandler(routed, new MouseButtonEventHandler((_, e) => Snapshot(label + ":" + e.RoutedEvent.Name)), true);
                element.GotKeyboardFocus += (_, _) => Snapshot(label + ":GotKeyboardFocus");
                element.LostKeyboardFocus += (_, _) => Snapshot(label + ":LostKeyboardFocus");
            }
            combo.DropDownOpened += (_, _) => Snapshot("DropDownOpened");
            combo.DropDownClosed += (_, _) => Snapshot("DropDownClosed");
            window.Closed += (_, _) => Snapshot("Closed");
            window.Show();
            wait(() => combo.IsLoaded && combo.ActualWidth > 0 && native.FindWindow(window.Title) != IntPtr.Zero);
            drain();
            Require(combo.Template is not null && combo.IsMeasureValid && combo.IsArrangeValid && !combo.IsDropDownOpen,
                "Closed genuine Classic ComboBox did not complete normal layout.");
            popup = (Popup?)combo.Template!.FindName("PART_Popup", combo);
            var toggle = Descendants(combo).OfType<ToggleButton>().Single();
            var classic = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "PresentationFramework.Classic");
            Require(popup is not null && !popup.IsOpen && Descendants(combo).Any(v => v.GetType().Assembly == classic), "The genuine Classic template was not applied.");
            Observations.Add(new { kind = "closed-genuine-Classic-template", classic = classic.FullName, classic.Location,
                popup!.AllowsTransparency, popup.HasDropShadow, animation = popup.PopupAnimation.ToString(), placement = popup.Placement.ToString(), combo.ActualWidth, combo.ActualHeight });
            Snapshot("closed-layout");
            ObserveRaw(PresentationSource.FromVisual(window)!, "owner");
            phase = "native-toggle-click";
            IntPtr xid = native.FindWindow(window.Title);
            native.Focus(xid); drain();
            var p = toggle.TransformToAncestor(window).Transform(new WpfFork::System.Windows.Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
            var physical = PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice.Transform(p);
            Observations.Add(new { kind = "native-XTest-click", xid = xid.ToInt64().ToString("x"), dip = p.ToString(), physical = physical.ToString() });
            native.Click(xid, (int)Math.Round(physical.X), (int)Math.Round(physical.Y));
            drain();
            Snapshot("after-click");
            var popupSource = PresentationSource.FromVisual(popup.Child);
            Require(combo.IsDropDownOpen && popup.IsOpen && popupSource is { IsDisposed: false } &&
                !ReferenceEquals(popupSource, PresentationSource.FromVisual(window)),
                "The actual native toggle click did not attach genuine Popup content to its separate source. An IsOpen value alone is insufficient; inspect the first-chance boundary trace.");
            IntPtr popupXid = ObserveMappedPopup(popupSource!, xid);
            if (!mouseOnly)
            {
                phase = "native-keyboard-recipient-and-boundaries";
                PressKey('\uff54');
                Require(Keyboard.FocusedElement is ComboBoxItem && PresentationSource.FromVisual((Visual)Keyboard.FocusedElement) == popupSource,
                    "Native Down did not focus an actual item in the Popup source.");
                Require(native.FocusedWindow == xid, "Logical Popup focus moved native X focus away from the owner.");
                for (int i = 0; i < 4; i++) PressKey('\uff52');
                Require(ReferenceEquals(Keyboard.FocusedElement, combo.ItemContainerGenerator.ContainerFromIndex(0)), "Up did not stop at the first real item.");
                for (int i = 0; i < 4; i++) PressKey('\uff54');
                Require(ReferenceEquals(Keyboard.FocusedElement, combo.ItemContainerGenerator.ContainerFromIndex(2)) && combo.SelectedIndex == 0,
                    "Down did not stop at the last real item without prematurely committing selection.");
                PressKey('\uff0d');
                wait(() => !popup.IsOpen && combo.SelectedIndex == 2);
                drain();
                Require(!native.RootChildren.Contains(popupXid) && native.CanAcquirePointerGrab() && !Keyboard.IsKeyDown(Key.Enter), "Enter did not commit, destroy the popup and release server capture/key state.");
                Snapshot("keyboard-Enter-commit");
                phase = "native-F4-Escape-cancel";
                PressKey('\uffc1'); wait(() => popup.IsOpen); drain();
                var secondSource = PresentationSource.FromVisual(popup.Child)!;
                IntPtr secondXid = ObserveMappedPopup(secondSource, xid);
                PressKey('\uff52'); PressKey('\uff1b'); wait(() => !popup.IsOpen); drain();
                Require(combo.SelectedIndex == 2 && !native.RootChildren.Contains(secondXid) && native.CanAcquirePointerGrab() && !Keyboard.IsKeyDown(Key.Escape),
                    "Escape committed a highlighted item or leaked the source, capture or held key.");
                Snapshot("keyboard-Escape-cancel");
                phase = "native-Tab-item-traversal-and-wrap";
                PressKey('\uffc1'); wait(() => popup.IsOpen); drain();
                var tabSource = PresentationSource.FromVisual(popup.Child)!;
                IntPtr tabXid = ObserveMappedPopup(tabSource, xid);
                PressKey('\uff52'); RequireTabItem(1);
                PressKey('\uff09'); RequireTabItem(2); Snapshot("keyboard-Tab-next-item");
                PressShiftTab(); RequireTabItem(1); Snapshot("keyboard-ShiftTab-previous-item");
                foreach (int expected in new[] { 2, 0, 1, 2 })
                {
                    PressKey('\uff09'); RequireTabItem(expected); Snapshot("keyboard-Tab-forward-wrap");
                }
                foreach (int expected in new[] { 1, 0, 2, 1 })
                {
                    PressShiftTab(); RequireTabItem(expected); Snapshot("keyboard-ShiftTab-reverse-wrap");
                }
                phase = "native-Escape-source-departure-and-owner-Tab";
                PressKey('\uff1b'); drain();
                Require(!popup.IsOpen && tabSource.IsDisposed && !native.RootChildren.Contains(tabXid) && combo.SelectedIndex == 2 &&
                    ReferenceEquals(Keyboard.FocusedElement, combo) && native.CanAcquirePointerGrab(),
                    "Escape after item traversal did not close the Popup source, preserve selection and restore ComboBox focus.");
                PressKey('\uff09'); drain();
                Require(ReferenceEquals(Keyboard.FocusedElement, nextButton) && !popup.IsOpen,
                    "Closed-owner Tab did not focus the next genuine control.");
                PressShiftTab();
                Require(ReferenceEquals(Keyboard.FocusedElement, combo) && Keyboard.Modifiers == ModifierKeys.None && !Keyboard.IsKeyDown(Key.Tab),
                    "Closed-owner Shift+Tab did not return focus to the ComboBox or retained a paired key.");
                Snapshot("keyboard-Escape-owner-Tab-and-return");
                phase = "native-Alt-Down-mouse-selection";
                native.Key('\uffe9', true); drain(); PressKey('\uff54'); native.Key('\uffe9', false); drain();
                wait(() => popup.IsOpen); drain();

                void RequireTabItem(int index)
                {
                    Require(popup.IsOpen && combo.SelectedIndex == 2 && ReferenceEquals(Keyboard.FocusedElement, combo.ItemContainerGenerator.ContainerFromIndex(index)) &&
                        PresentationSource.FromVisual((Visual)Keyboard.FocusedElement) == tabSource && native.FocusedWindow == xid &&
                        !native.CanAcquirePointerGrab() && Keyboard.Modifiers == ModifierKeys.None && !Keyboard.IsKeyDown(Key.Tab),
                        "Native Tab traversal changed the measured Classic item/source/selection/capture contract at index " + index);
                }
            }
            else phase = "native-mouse-selection";
            var mouseSource = PresentationSource.FromVisual(popup.Child)!;
            IntPtr mouseXid = ObserveMappedPopup(mouseSource, xid);
            var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
            var itemPoint = item.TransformToAncestor(mouseSource.RootVisual).Transform(new WpfFork::System.Windows.Point(item.ActualWidth / 2, item.ActualHeight / 2));
            itemPoint = mouseSource.CompositionTarget.TransformToDevice.Transform(itemPoint);
            native.Click(mouseXid, (int)Math.Round(itemPoint.X), (int)Math.Round(itemPoint.Y)); drain();
            Snapshot("after-native-item-click");
            Require(combo.SelectedIndex == 1 && !popup.IsOpen, "An actual native item click did not select and close the genuine ComboBox.");
            Require(native.FocusedWindow == xid && !Keyboard.IsKeyDown(Key.LeftAlt) && native.CanAcquirePointerGrab(), "Mouse selection changed owner X focus or leaked modifier/capture state.");
            Snapshot("mouse-selection");
            phase = "native-outside-cancel-and-repeated-lifetime";
            for (int i = 0; i < 3; i++)
            {
                native.Click(xid, (int)Math.Round(physical.X), (int)Math.Round(physical.Y)); drain();
                Require(popup.IsOpen, "Repeated native toggle click did not reopen Popup.");
                var source = PresentationSource.FromVisual(popup.Child)!;
                IntPtr repeatedXid = ObserveMappedPopup(source, xid);
                native.Click(xid, 280, 15); drain();
                Require(!popup.IsOpen && combo.SelectedIndex == 1 && source.IsDisposed && !native.RootChildren.Contains(repeatedXid) && native.CanAcquirePointerGrab(),
                    "Outside click changed selection or failed paired popup/source/native capture cleanup.");
            }
            Snapshot("outside-cancel-repeated-cleanup");
            phase = "capture-transfer-and-unrelated-window";
            var other = new Window { Title = "Popup unrelated pointer recovery", Left = 500, Top = 200, Width = 160, Height = 80, Background = Brushes.Green };
            int otherDown = 0, otherUp = 0, ownerDown = 0, ownerUp = 0;
            Exception? transferFailure = null;
            other.PreviewMouseLeftButtonDown += (_, _) => otherDown++;
            other.PreviewMouseLeftButtonUp += (_, _) => otherUp++;
            window.PreviewMouseLeftButtonDown += (_, _) => ownerDown++;
            window.PreviewMouseLeftButtonUp += (_, _) => ownerUp++;
            try
            {
                other.Show(); drain();
                IntPtr otherXid = native.FindWindow(other.Title);
                ObserveRaw(PresentationSource.FromVisual(other)!, "unrelated");
                native.Focus(xid); drain();
                native.Click(xid, (int)Math.Round(physical.X), (int)Math.Round(physical.Y)); drain();
                Require(popup.IsOpen, "Native reopen before capture transfer failed.");
                var transferSource = PresentationSource.FromVisual(popup.Child)!;
                IntPtr transferXid = ObserveMappedPopup(transferSource, xid);
                VerifyNativeCrossingOrder(PresentationSource.FromVisual(window)!, PresentationSource.FromVisual(other)!);
                var captureItem = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
                Require(captureItem.CaptureMouse() && ReferenceEquals(Mouse.Captured, captureItem), "Capture did not transfer into the actual Popup source.");
                captureItem.ReleaseMouseCapture();
                Snapshot("capture-restored-before-native-drain");
                Observations.Add(new { kind = "capture-restored-native-before-drain", free = native.CanAcquirePointerGrab() });
                drain();
                Require(popup.IsOpen && ReferenceEquals(Mouse.Captured, combo) && !native.CanAcquirePointerGrab(),
                    "Releasing descendant capture did not restore genuine ComboBox/server capture.");
                Require(other.CaptureMouse(), "An unrelated actual source could not acquire native capture."); drain();
                Require(!popup.IsOpen && transferSource.IsDisposed && !native.RootChildren.Contains(transferXid) && ReferenceEquals(Mouse.Captured, other),
                    "Transfer outside the ComboBox did not dismiss the popup while preserving the new real capture owner.");
                other.ReleaseMouseCapture(); drain();
                Require(Mouse.Captured is null && native.CanAcquirePointerGrab(), "Unrelated capture release leaked managed or server capture.");
                native.Focus(otherXid); drain(); native.Click(otherXid, 30, 30); drain();
                Require(otherDown == 1 && otherUp == 1 && Mouse.LeftButton == MouseButtonState.Released,
                    "The unrelated source did not recover both native button transitions after popup capture transfer.");
                int previousDown = ownerDown, previousUp = ownerUp;
                native.Focus(xid); drain(); native.Click(xid, 280, 15); drain();
                Require(ownerDown == previousDown + 1 && ownerUp == previousUp + 1 && Mouse.LeftButton == MouseButtonState.Released,
                    "Returning to the owner lost a native button transition after unrelated source activation.");
                Snapshot("capture-transfer-unrelated-owner-recovery");
            }
            catch (Exception failure)
            {
                transferFailure = failure;
                Observations.Add(new { kind = "capture-transfer-failure", exception = failure.ToString() });
            }
            finally { other.Close(); drain(); }
            phase = "placement-target-detach";
            native.Focus(xid); drain();
            native.Click(xid, (int)Math.Round(physical.X), (int)Math.Round(physical.Y)); drain();
            Require(popup.IsOpen, "Native reopen before target detach failed.");
            var detachedSource = PresentationSource.FromVisual(popup.Child)!;
            IntPtr detachedXid = ObserveMappedPopup(detachedSource, xid);
            int detachedClosed = 0;
            EventHandler detachedHandler = (_, _) => detachedClosed++;
            popup.Closed += detachedHandler;
            try
            {
                window.Content = null; drain();
                Require(!popup.IsOpen && detachedSource.IsDisposed && !native.RootChildren.Contains(detachedXid) && detachedClosed == 1 && native.CanAcquirePointerGrab(),
                    "Removing the genuine placement target did not close/dispose its Popup exactly once.");
                Snapshot("placement-target-detached");
            }
            finally { popup.Closed -= detachedHandler; }
            combo.IsDropDownOpen = false;
            window.Content = layoutRoot; drain();
            phase = "accepted-owner-close";
            native.Focus(xid); drain();
            native.Click(xid, (int)Math.Round(physical.X), (int)Math.Round(physical.Y)); drain();
            Require(popup.IsOpen, "Native reopen before owner close failed.");
            var closingSource = PresentationSource.FromVisual(popup.Child)!;
            IntPtr closingXid = ObserveMappedPopup(closingSource, xid);
            phase = "canceled-owner-close";
            System.ComponentModel.CancelEventHandler cancelClose = (_, e) => e.Cancel = true;
            window.Closing += cancelClose;
            try
            {
                window.Close(); drain();
                Require(popup.IsOpen && !closingSource.IsDisposed && native.IsMapped(closingXid) && !native.CanAcquirePointerGrab(),
                    "Canceled owner Closing changed its live Popup/source/capture.");
                Snapshot("canceled-owner-close-preserved");
            }
            finally { window.Closing -= cancelClose; }
            phase = "accepted-owner-close";
            int popupClosed = 0;
            popup.Closed += (_, _) => popupClosed++;
            window.Close(); drain();
            Observations.Add(new { kind = "accepted-owner-close-state", popup.IsOpen, closingSource.IsDisposed, nativePresent = native.RootChildren.Contains(closingXid), popupClosed, captureFree = native.CanAcquirePointerGrab() });
            Require(!popup.IsOpen && closingSource.IsDisposed && !native.RootChildren.Contains(closingXid) && popupClosed == 1 && native.CanAcquirePointerGrab(),
                "Accepted owner close did not close the genuine Popup once and release source/native capture.");
            Snapshot("accepted-owner-close-cleanup");
            if (transferFailure != null) throw new AggregateException("Popup capture transfer failed before the independent owner-close case.", transferFailure);

            void PressKey(char keysym)
            {
                native.Key(keysym, true); drain(); native.Key(keysym, false); drain();
                Require(native.FocusedWindow == xid, "Native owner focus changed during popup key routing.");
            }
            void PressShiftTab()
            {
                native.Key('\uffe1', true); drain();
                try { PressKey('\uff09'); }
                finally { native.Key('\uffe1', false); drain(); }
            }
            IntPtr ObserveMappedPopup(PresentationSource source, IntPtr ownerXid)
            {
                object peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
                IntPtr popupId = OwnedNativeAccess.Handle(source);
                ObserveRaw(source, "popup");
                Require(popupId != ownerXid && native.IsMapped(popupId), "The genuine Popup has no separately mapped native X11 window.");
                var attributes = native.PopupAttributes(popupId);
                Require(attributes.Depth == 32 && attributes.OverrideRedirect, "The native Popup lacks actual ARGB32 override-redirect storage.");
                Require(native.SkipsTaskbar(popupId), "The native Popup inherited the normal Window taskbar default.");
                Require(native.FocusedWindow == ownerXid && !native.CanAcquirePointerGrab(), "The popup changed owner native focus or did not retain real server capture.");
                var location = native.ClientOrigin(popupId); var ownerLocation = native.ClientOrigin(ownerXid); var size = native.Size(popupId);
                if (originZero) Require(location.Y >= ownerLocation.Y + native.Size(ownerXid).Height, "Bottom popup did not extend outside its owner's client area.");
                Require(native.Pixel(popupId, Math.Min(2, size.Width - 1), Math.Min(2, size.Height - 1)) != 0, "No genuine popup item/background pixels reached the native surface.");
                Observations.Add(new { kind = "mapped-native-popup", popupId = popupId.ToInt64().ToString("x"), owner = ownerXid.ToInt64().ToString("x"),
                    x = location.X, y = location.Y, width = size.Width, height = size.Height, attributes.Depth, attributes.OverrideRedirect, skipsTaskbar = native.SkipsTaskbar(popupId),
                    composited = peer.GetType().GetProperty("PopupComposited", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer),
                    appearanceScope = "Native ARGB32 premultiplied storage only; compositor appearance unqualified" });
                return popupId;
            }

            void ObserveRaw(PresentationSource source, string label)
            {
                object peer = source.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
                if (rawObservers.Any(value => ReferenceEquals(value.Peer, peer))) return;
                EventInfo raw = peer.GetType().GetEvent("RawInput")!;
                Action<object> callback = report =>
                {
                    object? Read(string name) => report.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(report);
                    Observations.Add(new { kind = "native-peer-raw-report", phase, peer = label, type = report.GetType().Name,
                        actions = Read("Actions")?.ToString(), x = Read("X"), y = Read("Y"), inputSourceMatchesPeer = ReferenceEquals(Read("InputSource"), source),
                        activeMouseSourceMatchesPeer = ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, source) });
                };
                Delegate typed = Delegate.CreateDelegate(raw.EventHandlerType!, callback.Target, callback.Method);
                raw.AddEventHandler(peer, typed);
                rawObservers.Add((peer, raw, typed));
            }
        }
        catch
        {
            var root = popup is null ? null : typeof(Popup).GetField("_popupRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup) as Visual;
            if (root != null)
                foreach (var visual in new[] { root }.Concat(Descendants(root).OfType<Visual>()).Take(80))
                {
                    if (visual is FrameworkElement element)
                        Observations.Add(new { kind = "failed-popup-layout", type = element.GetType().FullName, element.Name,
                            desired = element.DesiredSize.ToString(), rendered = element.RenderSize.ToString(),
                            width = element.Width.ToString(), height = element.Height.ToString(),
                            maxWidth = element.MaxWidth.ToString(), maxHeight = element.MaxHeight.ToString(),
                            minWidth = element.MinWidth.ToString(), minHeight = element.MinHeight.ToString(),
                            element.IsMeasureValid, element.IsArrangeValid, element.ClipToBounds,
                            layout = element.LayoutTransform.Value.ToString(), render = element.RenderTransform.Value.ToString() });
                    try { CapturePens(VisualTreeHelper.GetDrawing(visual), visual); }
                    catch (Exception diagnostic) { Observations.Add(new { kind = "drawing-observer-error", detail = diagnostic.ToString() }); }
                }
            throw;
        }
        finally
        {
            phase = "cleanup";
            try { window?.Close(); Snapshot("close-returned"); }
            finally
            {
                foreach (var raw in rawObservers) raw.Event.RemoveEventHandler(raw.Peer, raw.Callback);
                AppDomain.CurrentDomain.FirstChanceException -= observer;
            }
        }
        void Snapshot(string kind) => Observations.Add(new { kind, phase, dropOpen = combo?.IsDropDownOpen,
            selectedIndex = combo?.SelectedIndex, directlyOver = Mouse.DirectlyOver?.GetType().FullName,
            focusedItemIndex = Keyboard.FocusedElement is ComboBoxItem focusedItem && combo != null ? combo.ItemContainerGenerator.IndexFromContainer(focusedItem) : -1,
            focused = Keyboard.FocusedElement?.GetType().FullName, captured = Mouse.Captured?.GetType().FullName,
            focusedSource = Keyboard.FocusedElement is Visual v ? PresentationSource.FromVisual(v)?.GetType().FullName : null,
            ownerSource = window is null ? null : PresentationSource.FromVisual(window)?.GetType().FullName });
    }

    internal static void OwnerLifecycle(X11Fixture native, Action drain, string fontPath)
    {
        var combo = new ComboBox { Width = 180, Height = 32, Margin = new Thickness(20), IsEditable = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
        combo.Items.Add("First"); combo.Items.Add("Second"); combo.SelectedIndex = 0;
        var owner = new Window { Title = "Genuine Classic Popup owner lifecycle", Left = 100, Top = 100, Width = 320, Height = 180, Content = combo, Background = Brushes.White };
        try
        {
            owner.Show(); drain();
            IntPtr ownerXid = native.FindWindow(owner.Title);
            native.Focus(ownerXid); drain();
            var ownerSource = PresentationSource.FromVisual(owner)!;
            var popup = (Popup)combo.Template!.FindName("PART_Popup", combo)!;
            int opened = 0, closed = 0;
            popup.Opened += (_, _) => opened++;
            popup.Closed += (_, _) => closed++;
            void Toggle()
            {
                var toggle = Descendants(combo).OfType<ToggleButton>().Single();
                var point = toggle.TransformToAncestor(owner).Transform(new WpfFork::System.Windows.Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
                point = ownerSource.CompositionTarget.TransformToDevice.Transform(point);
                native.Click(ownerXid, (int)Math.Round(point.X), (int)Math.Round(point.Y)); drain();
                Require(popup.IsOpen && PresentationSource.FromVisual(popup.Child) is { IsDisposed: false }, "The actual native owner toggle did not open Popup.");
            }
            Toggle();
            var source = PresentationSource.FromVisual(popup.Child)!;
            IntPtr xid = PopupHandle(source);
            var before = native.ClientOrigin(xid);
            owner.Left += 30; owner.Top += 20; owner.Width += 40; owner.Height += 20; drain();
            Require(popup.IsOpen && ReferenceEquals(PresentationSource.FromVisual(popup.Child), source) && native.IsMapped(xid),
                "Owner move/resize destroyed or replaced the open Popup source.");
            var after = native.ClientOrigin(xid);
            var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
            var itemPoint = item.TransformToAncestor(source.RootVisual).Transform(new WpfFork::System.Windows.Point(item.ActualWidth / 2, item.ActualHeight / 2));
            itemPoint = source.CompositionTarget.TransformToDevice.Transform(itemPoint);
            native.Click(xid, (int)Math.Round(itemPoint.X), (int)Math.Round(itemPoint.Y)); drain();
            Require(combo.SelectedIndex == 1 && !popup.IsOpen && source.IsDisposed && closed == 1 && native.CanAcquirePointerGrab(),
                "Actual item input after owner coordinate changes lost captured-target mapping or cleanup.");
            Observations.Add(new { kind = "native-Popup-owner-move-resize", beforeX = before.X, beforeY = before.Y, afterX = after.X, afterY = after.Y,
                owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight, originalPlacementLogicRetained = true });
            Toggle();
            source = PresentationSource.FromVisual(popup.Child)!; xid = PopupHandle(source);
            Require(owner.IsActive && native.FocusedWindow == ownerXid, "Hide observation did not establish the genuinely active native owner path.");
            owner.Hide(); drain();
            Observations.Add(new { kind = "active-native-Popup-owner-hide-observed", combo.IsDropDownOpen, popup.IsOpen, source.IsDisposed, closed,
                nativeOwnerMapped = native.IsMapped(ownerXid), nativePopupPresent = native.RootChildren.Contains(xid),
                scope = "Linux active-owner deactivation, not parity with the calibrated background/unowned stock path" });
            Require(!native.IsMapped(ownerXid) && !popup.IsOpen && source.IsDisposed && !native.RootChildren.Contains(xid) && closed == 2 && native.CanAcquirePointerGrab(),
                "Native active-owner hide failed its existing deactivation/capture/source cleanup.");
            owner.Show(); drain(); native.Focus(ownerXid); drain();
            Require(ReferenceEquals(ownerSource, PresentationSource.FromVisual(owner)) && opened == 2 && !combo.IsDropDownOpen,
                "Owner Show replaced its source or unexpectedly reopened a deactivated ComboBox.");
            Toggle();
            var replacement = PresentationSource.FromVisual(popup.Child)!;
            Require(!ReferenceEquals(source, replacement) && opened == 3 && native.IsMapped(PopupHandle(replacement)), "Native reopening after Hide/Show did not use a fresh Popup source.");
            var unrelated = new Window { Title = "Popup native focus departure", Left = 600, Top = 100, Width = 160, Height = 80, ShowActivated = false, Background = Brushes.Green };
            try
            {
                unrelated.Show(); drain();
                IntPtr unrelatedXid = native.FindWindow(unrelated.Title);
                native.Focus(unrelatedXid); drain();
                Require(native.FocusedWindow == unrelatedXid && !owner.IsActive && !popup.IsOpen && replacement.IsDisposed && closed == 3 && native.CanAcquirePointerGrab(),
                    "Actual owner X focus departure did not dismiss Popup and clear its logical recipient/capture.");
                Observations.Add(new { kind = "native-Popup-owner-focus-departure", popup.IsOpen, replacement.IsDisposed, closed, owner.IsActive });
                native.Focus(ownerXid); drain(); Toggle();
                replacement = PresentationSource.FromVisual(popup.Child)!;
            }
            finally { unrelated.Close(); drain(); }
            owner.Close(); drain();
            Require(replacement.IsDisposed && closed == 4 && native.CanAcquirePointerGrab(), "Final accepted owner close after Hide/Show leaked Popup resources.");
        }
        finally { if (PresentationSource.FromVisual(owner) != null) owner.Close(); drain(); }
    }
    internal static void PlacementByNativeClick(X11Fixture native, Action drain, string fontPath)
    {
        foreach (string scenario in new[] { "bottom-flip", "right-edge", "left-edge", "height-constrained" })
        {
            var screen = native.ScreenSize;
            var combo = new ComboBox { Width = 180, Height = 32, Margin = new Thickness(20), IsEditable = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
            int count = scenario == "height-constrained" ? 150 : 3;
            for (int index = 0; index < count; index++) combo.Items.Add("Item " + index);
            combo.SelectedIndex = 0;
            var owner = new Window { Title = "Genuine Classic Popup placement " + scenario, Width = 320, Height = 180, Content = combo, Background = Brushes.White };
            try
            {
                owner.Show(); drain();
                var ownerSource = PresentationSource.FromVisual(owner)!;
                double scale = ownerSource.CompositionTarget.TransformToDevice.M11;
                owner.Left = scenario == "right-edge" ? screen.Width / scale - 140 : scenario == "left-edge" ? -30 : 100 / scale;
                owner.Top = scenario == "bottom-flip" ? screen.Height / scale - 52 - 4 / scale : 100 / scale;
                if (scenario == "height-constrained") combo.MaxDropDownHeight = screen.Height / scale * 2;
                drain();
                IntPtr ownerXid = native.FindWindow(owner.Title);
                native.Focus(ownerXid); drain();
                Require(combo.Template != null && combo.IsMeasureValid && combo.IsArrangeValid, "The ordinary Classic placement control failed layout.");
                var popup = (Popup)combo.Template!.FindName("PART_Popup", combo)!;
                var toggle = Descendants(combo).OfType<ToggleButton>().Single();
                var point = toggle.TransformToAncestor(owner).Transform(new WpfFork::System.Windows.Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
                point = ownerSource.CompositionTarget.TransformToDevice.Transform(point);
                var observedOwner = native.ClientOrigin(ownerXid);
                Observations.Add(new { kind = "native-placement-click-precondition", scenario, scale, owner.Left, owner.Top, ownerX = observedOwner.X, ownerY = observedOwner.Y,
                    clientX = point.X, clientY = point.Y, screenWidth = screen.Width, screenHeight = screen.Height });
                native.Click(ownerXid, (int)Math.Round(point.X), (int)Math.Round(point.Y)); drain();
                var source = PresentationSource.FromVisual(popup.Child);
                Require(popup.IsOpen && source is { IsDisposed: false }, "The actual native edge click did not open a separate genuine Popup source.");
                IntPtr xid = PopupHandle(source!);
                var position = native.ClientOrigin(xid); var size = native.Size(xid);
                Require(native.IsMapped(xid) && native.FocusedWindow == ownerXid && !native.CanAcquirePointerGrab(), "Edge placement lost native mapping/owner focus/capture.");
                Require(position.X >= 0 && position.Y >= 0 && position.X + size.Width <= screen.Width && position.Y + size.Height <= screen.Height,
                    "Original WPF placement did not fit the actual observed native screen bounds.");
                var ownerPosition = native.ClientOrigin(ownerXid);
                var target = combo.TransformToAncestor(owner).Transform(new WpfFork::System.Windows.Point());
                target = ownerSource.CompositionTarget.TransformToDevice.Transform(target);
                if (scenario == "bottom-flip") Require(position.Y + size.Height <= ownerPosition.Y + target.Y + 1, "Bottom placement did not flip above its actual target at the lower screen edge.");
                if (scenario == "right-edge") Require(position.X + size.Width == screen.Width, "The right-edge popup did not nudge to the actual screen edge.");
                if (scenario == "left-edge") Require(position.X == 0, "The left-edge popup did not nudge to the actual screen edge.");
                if (scenario == "height-constrained") Require(size.Height < count * 12 * scale && size.Height <= screen.Height,
                    "The actual PopupRoot/ScrollViewer did not constrain tall content.");
                Require(Math.Abs(size.Width - combo.ActualWidth * scale) <= 1, "Popup target width was scaled more or less than once.");
                native.MovePointer(2, 2); native.Button(1, true); drain(); native.Button(1, false); drain();
                Observations.Add(new { kind = "native-placement-outside-cancel-state", scenario, popup.IsOpen, source!.IsDisposed, nativePresent = native.RootChildren.Contains(xid),
                    captured = Mouse.Captured?.GetType().FullName, leftButton = Mouse.LeftButton.ToString(), captureFree = native.CanAcquirePointerGrab(), combo.SelectedIndex,
                    x = position.X, y = position.Y, width = size.Width, height = size.Height, scale });
                Require(!popup.IsOpen && source!.IsDisposed && !native.RootChildren.Contains(xid) && Mouse.Captured == null &&
                    Mouse.LeftButton == MouseButtonState.Released && native.CanAcquirePointerGrab() && combo.SelectedIndex == 0,
                    "Actual outside-screen-content cancellation changed selection or retained source/capture/button state.");
                Observations.Add(new { kind = "genuine-Classic-native-placement", scenario, scale, screenWidth = screen.Width, screenHeight = screen.Height,
                    x = position.X, y = position.Y, width = size.Width, height = size.Height, outsidePairedButtonRelease = true });
            }
            finally { owner.Close(); drain(); }
        }
    }
    internal static void ReentryByNativeClick(X11Fixture native, Action drain, string fontPath)
    {
        foreach (string scenario in new[] { "opened-owner-close", "selection-owner-close", "closed-inline-reopen", "closed-queued-reopen", "closed-handler-throws" })
        {
            var combo = new ComboBox { Width = 180, Height = 32, Margin = new Thickness(20), IsEditable = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), FontSize = 12 };
            combo.Items.Add("First"); combo.Items.Add("Second"); combo.SelectedIndex = 0;
            var window = new Window { Title = "Genuine Popup reentry " + scenario, Width = 320, Height = 180, Content = combo, Background = Brushes.White };
            int opened = 0, closed = 0, ownerClosed = 0;
            bool rejected = false, queued = false;
            Popup? popup = null;
            PresentationSource? firstSource = null;
            IntPtr firstXid = IntPtr.Zero;
            window.Closed += (_, _) => ownerClosed++;
            try
            {
                window.Show(); drain();
                IntPtr ownerXid = native.FindWindow(window.Title);
                Require(ownerXid != IntPtr.Zero && combo.Template != null, "The reentry case did not construct a genuine shown Classic ComboBox.");
                popup = (Popup)combo.Template!.FindName("PART_Popup", combo)!;
                popup.Opened += (_, _) =>
                {
                    opened++;
                    if (opened != 1) return;
                    firstSource = PresentationSource.FromVisual(popup.Child);
                    firstXid = PopupHandle(firstSource!);
                    if (scenario == "opened-owner-close") window.Close();
                };
                popup.Closed += (_, _) =>
                {
                    closed++;
                    if (closed != 1) return;
                    if (scenario == "closed-handler-throws") throw new InvalidOperationException("Expected fixture Popup.Closed failure.");
                    if (scenario == "closed-inline-reopen")
                    {
                        try { popup.IsOpen = true; }
                        catch (InvalidOperationException) { rejected = true; }
                    }
                    if (scenario == "closed-queued-reopen")
                        window.Dispatcher.BeginInvoke(new Action(() => { queued = true; combo.IsDropDownOpen = true; }));
                };
                if (scenario == "selection-owner-close")
                    combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex == 1) window.Close(); };
                native.Focus(ownerXid); drain();
                var toggle = Descendants(combo).OfType<ToggleButton>().Single();
                var point = toggle.TransformToAncestor(window).Transform(new WpfFork::System.Windows.Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
                point = PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice.Transform(point);
                native.Click(ownerXid, (int)Math.Round(point.X), (int)Math.Round(point.Y)); drain();
                Require(opened == 1 && firstSource != null && firstXid != IntPtr.Zero, "The native toggle did not open the genuine Popup before reentry.");
                if (scenario == "closed-handler-throws")
                {
                    bool observedFailure = false;
                    try { window.Close(); }
                    catch (InvalidOperationException failure) when (failure.Message == "Expected fixture Popup.Closed failure.") { observedFailure = true; }
                    drain();
                    Require(observedFailure && closed == 1 && firstSource!.IsDisposed && PresentationSource.FromVisual(window) == null &&
                        !native.RootChildren.Contains(firstXid) && !native.RootChildren.Contains(ownerXid) && native.CanAcquirePointerGrab(),
                        "A real throwing Popup.Closed handler hid the exception or leaked popup/owner source/native ownership.");
                    Observations.Add(new { kind = "genuine-Popup-callback-reentry", scenario, opened, closed, observedFailure, sourceAndNativeCleanup = true });
                    continue;
                }
                if (scenario == "selection-owner-close")
                {
                    var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
                    var itemPoint = item.TransformToAncestor(firstSource!.RootVisual).Transform(new WpfFork::System.Windows.Point(item.ActualWidth / 2, item.ActualHeight / 2));
                    itemPoint = firstSource.CompositionTarget.TransformToDevice.Transform(itemPoint);
                    native.Click(firstXid, (int)Math.Round(itemPoint.X), (int)Math.Round(itemPoint.Y)); drain();
                    Require(combo.SelectedIndex == 1, "Actual native item selection did not invoke the selection callback.");
                }
                else if (scenario != "opened-owner-close") { native.Click(ownerXid, 280, 15); drain(); }
                Require(firstSource!.IsDisposed && !native.RootChildren.Contains(firstXid) && closed == 1,
                    "Reentry failed first Popup source/native destruction or emitted duplicate Closed.");
                if (scenario.EndsWith("owner-close", StringComparison.Ordinal))
                    Require(ownerClosed == 1 && PresentationSource.FromVisual(window) == null && native.CanAcquirePointerGrab(),
                        "Closing the owner from a real Popup callback leaked ownership or capture.");
                else if (scenario == "closed-inline-reopen")
                    Require(rejected && opened == 1 && PresentationSource.FromVisual(popup.Child) == null && native.CanAcquirePointerGrab(),
                        "Original synchronous Closed-handler reopen rejection was not preserved.");
                else
                {
                    var replacement = PresentationSource.FromVisual(popup.Child);
                    Require(queued && opened == 2 && popup.IsOpen && replacement is { IsDisposed: false } && !ReferenceEquals(firstSource, replacement) &&
                        native.IsMapped(PopupHandle(replacement)) && !native.CanAcquirePointerGrab(), "Dispatcher-queued reopen did not acquire a fresh source/native popup/capture.");
                    native.Click(ownerXid, 280, 15); drain();
                    Require(closed == 2 && replacement!.IsDisposed && native.CanAcquirePointerGrab(), "Queued replacement did not close/release normally.");
                }
                Observations.Add(new { kind = "genuine-Popup-callback-reentry", scenario, opened, closed, ownerClosed, rejected, queued });
            }
            finally { if (PresentationSource.FromVisual(window) != null) window.Close(); drain(); }
            Require(Mouse.LeftButton == MouseButtonState.Released && Mouse.Captured == null && native.CanAcquirePointerGrab(),
                "Popup reentry cleanup retained a held button or native/managed capture.");
        }
        MultipleThrowingClosed(native, drain);
    }

    private static void MultipleThrowingClosed(X11Fixture native, Action drain)
    {
        foreach (int throwing in new[] { 3, 0, 1, 2 })
        {
            var owner = new Window { Title = "Two owned Popup callbacks " + throwing, Width = 260, Height = 140, Background = Brushes.White };
            var children = new[] { new Border { Width = 40, Height = 24, Background = Brushes.Red }, new Border { Width = 44, Height = 28, Background = Brushes.Blue } };
            var popups = children.Select((child, index) => new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Bottom,
                PlacementTarget = owner, HorizontalOffset = index * 50, Child = child }).ToArray();
            var failures = new[] { new InvalidOperationException("Expected first owned Popup.Closed failure."), new InvalidOperationException("Expected second owned Popup.Closed failure.") };
            int[] closed = new int[2];
            var callbackOrder = new List<int>();
            IDisposable? retainedPeer = null;
            try
            {
                owner.Show(); drain();
                var ownerSource = PresentationSource.FromVisual(owner)!;
                retainedPeer = (IDisposable)ownerSource.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ownerSource)!;
                IntPtr ownerXid = native.FindWindow(owner.Title);
                native.Focus(ownerXid); drain();
                foreach (var popup in popups) { popup.IsOpen = true; drain(); }
                var sources = children.Select(child => PresentationSource.FromVisual(child)!).ToArray();
                var xids = sources.Select(PopupHandle).ToArray();
                Require(popups.All(popup => popup.IsOpen) && sources.All(source => source is { IsDisposed: false }) && xids.Distinct().Count() == 2 &&
                    xids.All(native.IsMapped) && native.FocusedWindow == ownerXid, "Both genuine owned popups must be mapped before callback cleanup.");
                Require(children[1].CaptureMouse() && ReferenceEquals(Mouse.Captured, children[1]) && !native.CanAcquirePointerGrab(),
                    "The second owned popup did not obtain real native capture.");
                for (int index = 0; index < popups.Length; index++)
                {
                    int callback = index;
                    popups[index].Closed += (_, _) => { closed[callback]++; callbackOrder.Add(callback); if ((throwing & (1 << callback)) != 0) throw failures[callback]; };
                }
                Exception? observed = null;
                try { owner.Close(); } catch (Exception failure) { observed = failure; }
                drain();
                int firstThrow = callbackOrder.FirstOrDefault(index => (throwing & (1 << index)) != 0, -1);
                bool ownerPresent = native.RootChildren.Contains(ownerXid);
                bool[] popupPresent = xids.Select(xid => native.RootChildren.Contains(xid)).ToArray();
                bool captureFree = native.CanAcquirePointerGrab();
                Observations.Add(new { kind = "two-owned-Popup-throwing-Closed", throwing, closed, callbackOrder, ownerPresent, popupPresent,
                    open = popups.Select(popup => popup.IsOpen).ToArray(), disposed = sources.Select(source => source.IsDisposed).ToArray(),
                    ownerSourceDisposed = ownerSource.IsDisposed, ownerSourceRemoved = PresentationSource.FromVisual(owner) == null,
                    captureFree, captured = Mouse.Captured?.GetType().FullName, observedException = observed?.ToString(),
                    originalExceptionPreserved = firstThrow < 0 ? observed == null : ReferenceEquals(observed, failures[firstThrow]) });
                Require((firstThrow < 0 ? observed == null : ReferenceEquals(observed, failures[firstThrow])) && closed.All(count => count == 1) &&
                    popups.All(popup => !popup.IsOpen) && sources.All(source => source.IsDisposed) && ownerSource.IsDisposed && PresentationSource.FromVisual(owner) == null &&
                    !ownerPresent && popupPresent.All(present => !present) && Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Released && captureFree,
                    "Throwing owned Popup callbacks interrupted cleanup, hid the original exception or leaked native owner/source/capture.");
            }
            finally
            {
                foreach (var popup in popups) { try { popup.IsOpen = false; } catch { } }
                try { if (PresentationSource.FromVisual(owner) != null) owner.Close(); } catch { }
                try { retainedPeer?.Dispose(); } catch { }
                drain();
            }
        }
    }
    private static IntPtr PopupHandle(PresentationSource source) => OwnedNativeAccess.Handle(source);
    internal static void TransparentPopupStorage(X11Fixture native, Action drain)
    {
        foreach (bool staysOpen in new[] { true, false })
        {
            var owner = new Window { Title = "Genuine Popup retained alpha " + staysOpen, Width = 220, Height = 100, Background = Brushes.White };
            var popup = new Popup { AllowsTransparency = true, StaysOpen = staysOpen, Placement = PlacementMode.Bottom, PlacementTarget = owner,
                Child = new Border { Width = 40, Height = 24, Background = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0)) } };
            int closed = 0;
            popup.Closed += (_, _) => closed++;
            try
            {
                owner.Show(); drain();
                IntPtr ownerXid = native.FindWindow(owner.Title);
                native.Focus(ownerXid); drain();
                popup.IsOpen = true; drain();
                var source = PresentationSource.FromVisual(popup.Child);
                Observations.Add(new { kind = "transparent-Popup-after-create", staysOpen, popup.IsOpen, hasLiveSource = source is { IsDisposed: false }, closed });
                Require(popup.IsOpen && source is { IsDisposed: false }, "The genuine transparent Popup closed during initial capture before a live mapped source was retained.");
                IntPtr xid = PopupHandle(source!);
                Require(native.IsMapped(xid) && native.PopupAttributes(xid).Depth == 32 && native.FocusedWindow == ownerXid,
                    "The genuine Popup lacks native ARGB storage or changed owner X focus.");
                uint pixel = native.Pixel(xid, 10, 10, preserveAlpha: true);
                Require(pixel == 0x80800000, "Actual native Popup storage did not preserve the rendered premultiplied half-alpha red pixel: " + pixel.ToString("X8"));
                Require(native.CanAcquirePointerGrab() == staysOpen && (staysOpen || ReferenceEquals(Mouse.Captured, source.RootVisual)),
                    "StaysOpen did not govern actual WPF/server capture truthfully.");
                if (staysOpen) popup.IsOpen = false;
                else native.Click(ownerXid, 150, 30);
                drain();
                Require(!popup.IsOpen && source.IsDisposed && !native.RootChildren.Contains(xid) && closed == 1 && Mouse.Captured == null && native.CanAcquirePointerGrab(),
                    "The genuine transparent Popup failed close/outside-dismiss capture and source cleanup.");
                Observations.Add(new { kind = "genuine-Popup-premultiplied-native-storage", staysOpen, pixel = pixel.ToString("X8"), closed,
                    scope = "ARGB32 native storage and input; composited appearance remains unqualified" });
            }
            finally { popup.IsOpen = false; owner.Close(); drain(); }
        }
    }
    internal static void InitialCaptureFailure(X11Fixture native, Action drain)
    {
        var owner = new Window { Title = "Genuine Popup initial native capture conflict", Width = 220, Height = 100, Background = Brushes.White };
        var popup = new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = owner,
            Child = new Border { Width = 40, Height = 24, Background = Brushes.Red } };
        int opened = 0, closed = 0;
        popup.Opened += (_, _) => opened++;
        popup.Closed += (_, _) => closed++;
        try
        {
            owner.Show(); drain();
            IntPtr ownerXid = native.FindWindow(owner.Title);
            native.Focus(ownerXid); drain();
            var children = native.RootChildren.ToHashSet();
            int sourceCount = PresentationSource.CurrentSources.Cast<PresentationSource>().Count();
            using (native.HoldPointerGrab())
            {
                popup.IsOpen = true; drain();
                Require(!popup.IsOpen && PresentationSource.FromVisual(popup.Child) == null && Mouse.Captured == null && opened == 0 && closed == 1,
                    "Initial real capture failure fabricated an open/captured Popup or skipped its original close cleanup.");
                Require(native.RootChildren.ToHashSet().SetEquals(children) && PresentationSource.CurrentSources.Cast<PresentationSource>().Count() == sourceCount,
                    "Initial capture failure leaked a native window or registered presentation source.");
            }
            Require(native.CanAcquirePointerGrab(), "The foreign capture lease did not release after the failure case.");
            popup.IsOpen = true; drain();
            var source = PresentationSource.FromVisual(popup.Child)!;
            IntPtr xid = PopupHandle(source);
            Require(popup.IsOpen && native.IsMapped(xid) && opened == 1 && !native.CanAcquirePointerGrab(),
                "The same genuine Popup could not retry after actual capture contention ended.");
            native.Click(ownerXid, 150, 30); drain();
            Require(!popup.IsOpen && source.IsDisposed && !native.RootChildren.Contains(xid) && opened == 1 && closed == 2 &&
                Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Released && native.CanAcquirePointerGrab(),
                "Retried Popup did not finish its paired source/capture/button lifetime.");
            Observations.Add(new { kind = "genuine-Popup-initial-capture-failure-and-retry", opened, closed, failedOpenEmittedOpened = false,
                independentClientGrab = true, noSourceOrNativeLeak = true });
        }
        finally { popup.IsOpen = false; owner.Close(); drain(); }
    }
    internal static void ReleaseSourceReconciliation(X11Fixture native, Action drain)
    {
        var owner = new Window { Title = "Popup release owner", Left = 50, Top = 50, Width = 220, Height = 100, Background = Brushes.White };
        var other = new Window { Title = "Popup release unrelated", Left = 600, Top = 100, Width = 160, Height = 80, Background = Brushes.Green };
        var popup = new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Bottom, PlacementTarget = owner,
            Child = new Border { Width = 40, Height = 24, Background = Brushes.Red } };
        int ownerDown = 0, ownerUp = 0;
        owner.PreviewMouseLeftButtonDown += (_, _) => ownerDown++;
        owner.PreviewMouseLeftButtonUp += (_, _) => ownerUp++;
        try
        {
            owner.Show(); other.Show(); drain();
            IntPtr ownerXid = native.FindWindow(owner.Title), otherXid = native.FindWindow(other.Title);
            native.Focus(ownerXid); drain();
            popup.IsOpen = true; drain();
            var ownerSource = PresentationSource.FromVisual(owner)!;
            var otherSource = PresentationSource.FromVisual(other)!;
            var popupSource = PresentationSource.FromVisual(popup.Child)!;
            var ownerPosition = native.ClientOrigin(ownerXid); var otherPosition = native.ClientOrigin(otherXid); var popupPosition = native.ClientOrigin(PopupHandle(popupSource));
            foreach (string target in new[] { "owner", "popup", "unrelated", "root", "root-recapture-before-dispatch" })
            {
                Require(owner.CaptureMouse(), "The real owner failed capture before release reconciliation.");
                var position = target == "owner" ? ownerPosition : target == "popup" ? popupPosition : otherPosition;
                native.MovePointer(target.StartsWith("root", StringComparison.Ordinal) ? 2 : position.X + 10,
                    target.StartsWith("root", StringComparison.Ordinal) ? 2 : position.Y + 10);
                drain();
                int beforeDown = ownerDown, beforeUp = ownerUp;
                native.Button(1, true); drain(); native.Button(1, false); drain();
                Require(ownerDown == beforeDown + 1 && ownerUp == beforeUp + 1 && ReferenceEquals(Mouse.Captured, owner) &&
                    ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, ownerSource) && !native.CanAcquirePointerGrab(),
                    "Actual paired native buttons did not preserve the owner's genuine captured target/source.");
                owner.ReleaseMouseCapture();
                if (target == "root-recapture-before-dispatch")
                {
                    Require(other.CaptureMouse(), "Reentrant unrelated capture failed before the release callback.");
                    drain();
                    Require(ReferenceEquals(Mouse.Captured, other) && ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, otherSource) && !native.CanAcquirePointerGrab(),
                        "The old source's queued release canceled a newer actual capture.");
                    other.ReleaseMouseCapture(); drain();
                    Require(Mouse.PrimaryDevice.ActiveSource == null, "The replacement source did not deactivate after its own actual outside release.");
                }
                else
                {
                    drain();
                    PresentationSource? expected = target == "root" ? null : target == "owner" ? ownerSource : target == "popup" ? popupSource : otherSource;
                    Require(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, expected), "Capture release did not preserve the actual owned pointer source or deactivate only outside all owned sources: " + target);
                }
                Require(Mouse.Captured == null && Mouse.LeftButton == MouseButtonState.Released && native.CanAcquirePointerGrab(),
                    "Release reconciliation leaked capture or retained a held button: " + target);
                Observations.Add(new { kind = "native-Popup-release-source-reconciliation", target, activeSource = Mouse.PrimaryDevice.ActiveSource?.GetType().Name,
                    captureFree = true, ordinaryPointerObservation = true });
            }
            Require(other.CaptureMouse(), "The nonfocused live source did not acquire capture before latest-loss verification.");
            native.Unmap(otherXid); drain();
            object peer = otherSource.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(otherSource)!;
            object input = OwnedNativeAccess.Get(peer, "_input");
            object observer = OwnedNativeAccess.Get(input, "_pointer");
            var captureOwnerField = observer.GetType().GetField("_captureOwner", OwnedNativeAccess.Members)!;
            object? captureOwner = captureOwnerField.GetValue(observer);
            Observations.Add(new { kind = "latest-native-unmap-capture-loss", hasRetainedNativeCaptureOwner = captureOwner != null,
                managedCapture = Mouse.Captured?.GetType().Name, captureFree = native.CanAcquirePointerGrab() });
            Require(Mouse.Captured == null && native.CanAcquirePointerGrab() && captureOwner == null,
                "Latest actual Unmap loss did not clear the one real grab owner or retained its bridge reference.");
            other.Hide(); other.Show(); drain();
            bool replacementCaptured = false;
            MouseEventHandler recapture = (_, _) => replacementCaptured = owner.CaptureMouse();
            other.LostMouseCapture += recapture;
            try
            {
                Require(other.CaptureMouse(), "The remapped source could not reacquire actual capture.");
                native.Unmap(otherXid); drain();
                object ownerPeer = ownerSource.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ownerSource)!;
                object ownerInput = OwnedNativeAccess.Get(ownerPeer, "_input");
                Require(replacementCaptured && ReferenceEquals(Mouse.Captured, owner) && !native.CanAcquirePointerGrab() &&
                    ReferenceEquals(captureOwnerField.GetValue(observer), ownerInput),
                    "The native-loss callback erased a newer real capture acquired by its managed handler.");
                Observations.Add(new { kind = "latest-native-unmap-reentrant-recapture", replacementCaptured, nativeOwnerMatchesReplacement = true });
            }
            finally { other.LostMouseCapture -= recapture; owner.ReleaseMouseCapture(); drain(); }
        }
        finally { popup.IsOpen = false; owner.Close(); other.Close(); drain(); }
    }
    private static void VerifyNativeCrossingOrder(PresentationSource owner, PresentationSource other)
    {
        const BindingFlags members = OwnedNativeAccess.Members;
        object ownerInput = OwnedNativeAccess.Input(owner), otherInput = OwnedNativeAccess.Input(other);
        object router = OwnedNativeAccess.Get(ownerInput, "_pointer");
        Require(ReferenceEquals(router, OwnedNativeAccess.Get(otherInput, "_pointer")), "Owned sources do not share the connection's pointer router.");
        var assembly = ownerInput.GetType().Assembly;
        var serialField = router.GetType().GetField("_captureSerial", members)!;
        var ownerField = router.GetType().GetField("_captureOwner", members)!;
        nuint originalSerial = (nuint)serialField.GetValue(router)!;
        object? originalOwner = ownerField.GetValue(router);
        Require(ReferenceEquals(originalOwner, ownerInput), "Protocol boundary control lacks actual owner native capture.");
        int opcode = (int)OwnedNativeAccess.Get(router, "_opcode");
        var eventType = assembly.GetType("Wpf.Portable.NativeHost.Linux.XEvent")!;
        var coreType = assembly.GetType("Wpf.Portable.NativeHost.Linux.XCrossingEvent")!;
        var xiType = assembly.GetType("Wpf.Portable.NativeHost.Linux.XIEnterLeaveEvent")!;
        var cookieType = eventType.GetField("GenericCookie", members)!.FieldType;
        var lostField = ownerInput.GetType().GetField("CaptureLost", members)!;
        var pointerField = ownerInput.GetType().GetField("PointerInput", members)!;
        var savedLost = lostField.GetValue(ownerInput);
        var savedOwnerPointer = pointerField.GetValue(ownerInput);
        var savedOtherPointer = pointerField.GetValue(otherInput);
        int lost = 0;
        lostField.SetValue(ownerInput, (Action)(() => lost++));
        var pointerCounter = new NativePointerCounter();
        var countMethod = typeof(NativePointerCounter).GetMethod(nameof(NativePointerCounter.OnInput))!
            .MakeGenericMethod(pointerField.FieldType.GenericTypeArguments.Single());
        var countPointer = Delegate.CreateDelegate(pointerField.FieldType, pointerCounter, countMethod);
        pointerField.SetValue(ownerInput, countPointer);
        pointerField.SetValue(otherInput, countPointer);
        void Set(object value, string field, object data) => value.GetType().GetField(field, members)!.SetValue(value, data);
        void Deliver(bool xi, object input, nuint serial, bool leave, int mode)
        {
            object crossing = Activator.CreateInstance(xi ? xiType : coreType)!;
            nuint handle = (nuint)OwnedNativeAccess.Get(OwnedNativeAccess.Get(input, "_window"), "Handle");
            Set(crossing, "Type", xi ? 35 : leave ? 8 : 7);
            Set(crossing, "Serial", serial);
            Set(crossing, xi ? "Event" : "Window", handle);
            Set(crossing, "Mode", mode);
            object nativeEvent = Activator.CreateInstance(eventType)!;
            if (!xi)
            {
                eventType.GetField("Crossing", members)!.SetValue(nativeEvent, crossing);
                OwnedNativeAccess.Call(input, "OnEvent", nativeEvent);
                return;
            }
            int device = (int)OwnedNativeAccess.Call(router, "Device", input)!;
            Set(crossing, "DeviceId", device);
            Set(crossing, "Extension", opcode);
            Set(crossing, "EventType", leave ? 8 : 7);
            IntPtr payload = Marshal.AllocHGlobal(Marshal.SizeOf(xiType));
            try
            {
                Marshal.StructureToPtr(crossing, payload, false);
                object cookie = Activator.CreateInstance(cookieType)!;
                Set(cookie, "Type", 35); Set(cookie, "Extension", opcode);
                Set(cookie, "EventType", leave ? 8 : 7); Set(cookie, "Data", payload);
                eventType.GetField("GenericCookie", members)!.SetValue(nativeEvent, cookie);
                OwnedNativeAccess.Call(router, "OnExtension", nativeEvent);
            }
            finally { Marshal.FreeHGlobal(payload); }
        }
        int assertions = 0;
        try
        {
            foreach (nuint guard in new[] { originalSerial, (nuint)0, nuint.MaxValue })
                foreach (bool xi in new[] { false, true })
                    foreach (bool leave in new[] { false, true })
                    {
                        void Retained(object input, nuint serial, int mode, bool eligible = false)
                        {
                            serialField.SetValue(router, guard); ownerField.SetValue(router, ownerInput); lost = 0; pointerCounter.Count = 0;
                            Deliver(xi, input, serial, leave, mode);
                            Require(ReferenceEquals(ownerField.GetValue(router), ownerInput) && lost == 0 && pointerCounter.Count == (xi && eligible ? 1 : 0),
                                $"A crossing erased current capture: XI2={xi}, leave={leave}, guard={guard}, serial={serial}, mode={mode}.");
                            assertions++;
                        }
                        foreach (int mode in new[] { 1, 2 })
                        {
                            Retained(ownerInput, unchecked(guard - 1), mode);
                            Retained(otherInput, guard, mode);
                        }
                        Retained(ownerInput, guard, 1, true);
                        Retained(otherInput, unchecked(guard + 1), 1, true);
                        Retained(otherInput, unchecked(guard - 1), 0, true);
                        foreach (nuint serial in new[] { guard, unchecked(guard + 1) })
                        {
                            serialField.SetValue(router, guard); ownerField.SetValue(router, ownerInput); lost = 0; pointerCounter.Count = 0;
                            Deliver(xi, ownerInput, serial, leave, 2);
                            Require(ownerField.GetValue(router) is null && lost == 1 && pointerCounter.Count == (xi ? 1 : 0),
                                "A current or newer native Ungrab did not clear the owner and notify exactly once.");
                            assertions++;
                        }
                    }
        }
        finally
        {
            serialField.SetValue(router, originalSerial); ownerField.SetValue(router, originalOwner);
            lostField.SetValue(ownerInput, savedLost);
            pointerField.SetValue(ownerInput, savedOwnerPointer);
            pointerField.SetValue(otherInput, savedOtherPointer);
        }
        Observations.Add(new { kind = "core-and-XI2-crossing-request-boundaries", assertions, nativeSerialBits = IntPtr.Size * 8,
            originalSerial = originalSerial.ToString(), minimumAndMaximumWrap = true, noNativeCaptureMutation = true,
            delivery = "Owned core/XI2 ABI handlers; real grab held; temporary observers count capture-loss callbacks without releasing that grab." });
    }
    private sealed class NativePointerCounter
    {
        internal int Count;
        public void OnInput<T>(T report) => Count++;
    }
    private static void CapturePens(Drawing? drawing, Visual visual)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) CapturePens(child, visual);
        if (drawing is GeometryDrawing { Pen: { } pen } geometry && pen.DashStyle?.Dashes.Count > 0)
        {
            string? Guides(string name) => typeof(Visual).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(visual)?.ToString();
            Observations.Add(new { kind = "actual-dashed-focus-pen", visual = visual.GetType().FullName,
                pen.Thickness, startCap = pen.StartLineCap.ToString(), endCap = pen.EndLineCap.ToString(), dashCap = pen.DashCap.ToString(),
                lineJoin = pen.LineJoin.ToString(), pen.MiterLimit, offset = pen.DashStyle.Offset, dashes = pen.DashStyle.Dashes.ToArray(),
                brush = pen.Brush is SolidColorBrush solid ? solid.Color.ToString() : pen.Brush?.GetType().FullName,
                geometry = geometry.Geometry.GetType().FullName, rectangle = (geometry.Geometry as RectangleGeometry)?.Rect.ToString(),
                radiusX = (geometry.Geometry as RectangleGeometry)?.RadiusX, radiusY = (geometry.Geometry as RectangleGeometry)?.RadiusY,
                transform = VisualTreeHelper.GetTransform(visual)?.Value.ToString(), offsetVisual = VisualTreeHelper.GetOffset(visual).ToString(),
                xGuidelines = Guides("VisualXSnappingGuidelines"), yGuidelines = Guides("VisualYSnappingGuidelines") });
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
