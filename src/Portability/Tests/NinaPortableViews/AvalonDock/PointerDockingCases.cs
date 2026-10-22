using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

internal static partial class DockingProgram
{
#if !NINA_STOCK_REFERENCE
    private static void ExercisePointerDocking(X11Fixture native, Window owner, DockingManager manager, LayoutAnchorable image, LayoutAnchorable graph)
    {
        PointerWait(() => native.IsMapped(NativeHandle(owner)), "The original owner did not become native viewable before pointer injection.");
        var dpi = VisualTreeHelper.GetDpi(owner);
        int expectedDpi = int.Parse(Environment.GetEnvironmentVariable("NINA_POINTER_DPI") ?? "96", System.Globalization.CultureInfo.InvariantCulture);
        Require(Math.Abs(dpi.DpiScaleX - expectedDpi / 96.0) < .00001 && owner.Left > 0 && owner.Top > 0,
            "The pointer fixture did not establish its declared DPI and nonzero origin.");
        if (Environment.GetEnvironmentVariable("NINA_DRAG_ANCHOR_ONLY") == "1")
        {
            Check("pointer-desktop-constrained-caption-anchor", () => DragAnchor(native, manager, image));
            return;
        }
        if (Environment.GetEnvironmentVariable("NINA_POINTER_CANCELED_CLOSE_ONLY") == "1")
        {
            Check("pointer-canceled-floating-close-and-next-drop", () => CanceledCloseAndRecovery(native, manager, image));
            return;
        }
        if (Environment.GetEnvironmentVariable("NINA_POINTER_MAXIMIZED_CAPTION_ONLY") == "1")
        {
            Check("pointer-maximized-caption-restores-and-next-drop", () => MaximizedCaption(native, manager, image));
            return;
        }
        if (Environment.GetEnvironmentVariable("NINA_FLOATING_TAB_HEADERS_ONLY") == "1")
        {
            ((LayoutAnchorablePane)graph.Parent).Children.Remove(graph);
            ((LayoutAnchorablePane)image.Parent).Children.Add(graph);
            manager.Layout.CollectGarbage(); Settle();
            Check("pointer-original-NINA-floating-tab-titles-and-manager-route", () => FloatingTabHeaders(native, manager, image));
            Check("pointer-original-NINA-floating-tab-canceled-close", () => CanceledCloseAndRecovery(native, manager, image, true));
            Check("pointer-original-NINA-floating-tab-current-manager", () => FloatingTabCurrentManager(native, manager, image));
            return;
        }
        if (Environment.GetEnvironmentVariable("NINA_GUIDE_GEOMETRY_ONLY") == "1")
        {
            Check("pointer-overlay-native-relocation-independent-pane-geometry", () => GuideGeometry(native, owner, manager, image, graph));
            return;
        }
        var recoveryOnly = Environment.GetEnvironmentVariable("NINA_POINTER_RECOVERY_ONLY");
        if (recoveryOnly is "1" or "2")
        {
            if (recoveryOnly == "2")
            {
                ((LayoutAnchorablePane)graph.Parent).Children.Remove(graph);
                ((LayoutAnchorablePane)image.Parent).Children.Add(graph);
                manager.Layout.CollectGarbage(); Settle();
            }
            Check("pointer-capture-transfer-and-next-drag", () => CaptureTransferAndRecovery(native, manager, image));
            return;
        }
        Check("pointer-original-image-right-drop", () =>
        {
            Require(!image.IsFloating && image.Parent is LayoutAnchorablePane imageParent && imageParent.Children.Count == 1,
                "The initial ImageView gesture must float a single docked pane.");
            PointerDrop(native, manager, image, "Right");
        });
        Check("pointer-original-guider-left-drop", () =>
        {
            Require(!graph.IsFloating && graph.Parent is LayoutAnchorablePane graphParent && graphParent.Children.Count == 1,
                "The initial GuiderGraph gesture must float a single docked pane.");
            PointerDrop(native, manager, graph, "Left");
        });
        Check("native-overlap-order-and-nonactivating-raise", () => NativeStacking(native));
        Check("pointer-original-caption-hit-test-exclusions", () => CaptionExclusions(native, manager, image));
        Check("pointer-original-generic-chrome-button-exclusion", () => CaptionExclusions(native, manager, image, true));
        Check("pointer-overlapping-native-host-selection", () => OverlappingHosts(native, owner, manager, image, graph));
        Check("pointer-Escape-and-next-drag", () =>
        {
            var content = image.Content;
            try
            {
                var floating = BeginPointerFloat(native, manager, image);
                var originalFocus = Keyboard.FocusedElement;
                var originalNativeFocus = native.FocusedWindow;
                var keys = new List<object>();
                void ObserveKey(object sender, ProcessInputEventArgs args)
                {
                    if (args.StagingItem.Input is KeyEventArgs key)
                        keys.Add(new { routedEvent = key.RoutedEvent.Name, key = key.Key.ToString(), key.Handled,
                            source = key.Source?.GetType().FullName, focused = Keyboard.FocusedElement?.GetType().FullName,
                            floating.IsKeyboardFocusWithin, floating.IsActive, nativeFocus = native.FocusedWindow.ToInt64(),
                            floatingXid = NativeHandle(floating).ToInt64(), servicePresent = Field(floating, "_dragService") is not null });
                }
                InputManager.Current.PostProcessInput += ObserveKey;
                try
                {
                    native.Key((char)0xff1b, true);
                    native.Key((char)0xff1b, false);
                    PointerWait(() => Mouse.Captured != floating && !VisibleOverlays().Any(), "Escape did not cancel the active drag.");
                }
                finally
                {
                    InputManager.Current.PostProcessInput -= ObserveKey;
                    Observations.Add(new { stage = "native-Escape-routing", keys = keys.ToArray(), floating.IsActive,
                        floating.IsKeyboardFocusWithin, focused = Keyboard.FocusedElement?.GetType().FullName,
                        nativeFocus = native.FocusedWindow.ToInt64(), floatingXid = NativeHandle(floating).ToInt64() });
                }
                Require(image.IsFloating && ReferenceEquals(image.Content, content), "Escape changed the floated model/content instead of aborting the drag.");
                Require(ReferenceEquals(Keyboard.FocusedElement, originalFocus) && native.FocusedWindow == originalNativeFocus,
                    "Escape cancellation changed logical or native keyboard focus.");
                Require(DragInputHandlers(floating) == 0, "Escape cancellation retained the active drag input handler.");
            }
            finally { native.Button(1, false); Mouse.Capture(null); }
            PointerDrop(native, manager, image, "Left");
        });
        Check("pointer-outside-release-and-next-drag", () =>
        {
            var content = graph.Content;
            try
            {
                var floating = BeginPointerFloat(native, manager, graph);
                native.MovePointer(3, 3);
                PointerWait(() => !VisibleOverlays().Any(), "Moving outside all hosts retained an overlay.");
                native.Button(1, false);
                PointerWait(() => Mouse.Captured != floating, "Outside release retained drag capture.");
                Require(graph.IsFloating && ReferenceEquals(graph.Content, content), "An outside release unexpectedly docked or replaced the original pane.");
            }
            finally { native.Button(1, false); Mouse.Capture(null); }
            PointerDrop(native, manager, graph, "Right");
        });
        Check("pointer-capture-transfer-and-next-drag", () => CaptureTransferAndRecovery(native, manager, image));
    }

    private static void DragAnchor(X11Fixture native, DockingManager manager, LayoutAnchorable pane)
    {
        LayoutFloatingWindowControl? floating = null;
        try
        {
            floating = BeginPointerFloat(native, manager, pane);
            native.MovePointer(3, 3); Settle(); native.Button(1, false);
            PointerWait(() => Mouse.Captured is null && Field(floating, "_dragService") is null,
                "The initial float retained drag state before the constrained position request.");
            var xid = NativeHandle(floating);
            var scale = VisualTreeHelper.GetDpi(floating);
            var content = pane.Content;
            var source = PresentationSource.FromVisual(floating);
            var model = floating.Model;
            var screen = native.ScreenSize;
            bool constrained = false;
            foreach (bool remap in new[] { false, true })
            {
                floating.Left = 100; floating.Top = 80; Settle();
                if (remap) floating.Hide();
                floating.Top = (screen.Height + 200) / scale.DpiScaleY;
                if (remap) floating.Show();
                Pause();
                Record(remap ? "hidden-request-remap" : "visible-request");
                var actual = ActualOuter();
                var press = CaptionPoint();
                constrained = Math.Abs(floating.Top * scale.DpiScaleY - actual.Y) > 4
                    && Field(floating, "_portablePendingBounds")!.ToString()!.Contains("Top", StringComparison.Ordinal)
                    && Field(floating, "_portableBoundsAwaitingAck")!.ToString()!.Contains("Top", StringComparison.Ordinal)
                    && press.X >= 0 && press.X < screen.Width && press.Y >= 0 && press.Y < screen.Height;
                if (constrained) break;
            }
            Require(constrained, "Openbox did not reproduce an on-screen desktop-constrained pending requested position; no causal drag conclusion is available.");
            var start = CaptionPoint();
            native.MovePointer((int)Math.Round(start.X), (int)Math.Round(start.Y)); Settle();
            var before = native.ClientOrigin(xid);
            var pointer = native.PointerPosition;
            var anchor = new Point(pointer.X - before.X, pointer.Y - before.Y);
            Record("before-caption-press");
            native.Button(1, true);
            PointerWait(() => Mouse.Captured == floating && Field(floating, "_dragService") is not null,
                "The constrained floating caption did not start a genuine captured drag.");
            Record("caption-press");
            Require(native.ClientOrigin(xid) == before, "A stationary constrained caption press moved the native window.");
            var service = Field(floating, "_dragService");
            var failures = new List<string>();
            foreach (var delta in new[] { new Point(-80, -350), new Point(40, -400), new Point(-30, -310), new Point(0, -350) })
            {
                var target = new Point(start.X + delta.X, start.Y + delta.Y);
                MoveHeldPointerAndWait(native, floating, target); Pause();
                Record("held-motion");
                var origin = native.ClientOrigin(xid); pointer = native.PointerPosition;
                if (Math.Abs(pointer.X - origin.X - anchor.X) > 1 || Math.Abs(pointer.Y - origin.Y - anchor.Y) > 1)
                    failures.Add($"Pointer/client anchor changed: expected {anchor}, actual {pointer.X - origin.X},{pointer.Y - origin.Y}.");
                var held = origin; Pause(); Record("stationary-hold");
                if (native.ClientOrigin(xid) != held) failures.Add("The stationary held pointer changed native client position.");
                var logicalScreen = floating.PointToScreen(Mouse.GetPosition(floating));
                if (Math.Abs(logicalScreen.X - pointer.X) > 1 || Math.Abs(logicalScreen.Y - pointer.Y) > 1)
                    failures.Add($"Logical pointer {logicalScreen} differs from native pointer {pointer.X},{pointer.Y} after hold.");
                Require(ReferenceEquals(service, Field(floating, "_dragService")) && Mouse.Captured == floating && DragInputHandlers(floating) == 1,
                    "Constrained caption movement replaced its drag service or capture owner.");
            }
            var canceledPosition = native.ClientOrigin(xid);
            native.Key((char)0xff1b, true); native.Key((char)0xff1b, false);
            PointerWait(() => Mouse.Captured is null && Field(floating, "_dragService") is null && !VisibleOverlays().Any(),
                "Escape retained constrained drag capture, service or overlay.");
            native.Button(1, false);
            Require(native.ClientOrigin(xid) == canceledPosition && DragInputHandlers(floating) == 0 && native.CanAcquirePointerGrab(),
                "Escape changed the constrained floating position or retained input handlers/native capture.");
            Require(ReferenceEquals(BeginPointerFloat(native, manager, pane, 48), floating),
                "The post-cancel caption press did not reuse the original floating source.");
            ReleaseHeaderDrag(native, floating);
            Require(pane.IsFloating && ReferenceEquals(pane.Content, content) && ReferenceEquals(floating.Model, model)
                && ReferenceEquals(PresentationSource.FromVisual(floating), source) && NativeHandle(floating) == xid
                && native.FocusedWindow == xid && DragInputHandlers(floating) == 0,
                "The constrained drag cancel/reentry/release changed content, source/model identity, focus or handler cleanup.");
            Require(failures.Count == 0, string.Join(" ", failures));

            void Pause()
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                PointerWait(() => elapsed.ElapsedMilliseconds >= 300, "The constrained caption observation did not settle.");
            }

            Point CaptionPoint()
            {
                var title = FloatingCaption(floating);
                var top = title.PointToScreen(new Point());
                double visibleHeight = (screen.Height - top.Y - 2) / scale.DpiScaleY;
                return title.PointToScreen(new Point(Math.Min(48, title.ActualWidth / 2), Math.Min(title.ActualHeight / 2, visibleHeight)));
            }

            Point ActualOuter()
            {
                var client = native.ClientOrigin(xid);
                var frame = native.FrameExtents(xid);
                var measured = frame ?? throw new InvalidDataException("The native WM did not publish measured frame extents.");
                return new Point(client.X - measured.Left, client.Y - measured.Top);
            }
            void Record(string stage)
            {
                var client = native.ClientOrigin(xid);
                var actual = ActualOuter();
                var physical = native.PointerPosition;
                var bounds = (Rect)typeof(Window).GetMethod("PortableReadBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(floating, null)!;
                var peer = Field(PresentationSource.FromVisual(floating)!, "_host")!;
                var input = Field(peer, "_input")!;
                var router = Field(input, "_pointer")!;
                Observations.Add(new { stage = "drag-anchor-" + stage, floating.Left, floating.Top, clientX = client.X, clientY = client.Y,
                    outerX = actual.X, outerY = actual.Y, actualBounds = bounds.ToString(), pointerX = physical.X, pointerY = physical.Y,
                    pending = Field(floating, "_portablePendingBounds")?.ToString(), awaiting = Field(floating, "_portableBoundsAwaitingAck")?.ToString(),
                    requested = Field(floating, "_portableRequestedDesktopPosition")?.ToString(), offset = Field(floating, "_portableDragOffset")?.ToString(),
                    floating.IsActive, captured = Mouse.Captured == floating, handlers = DragInputHandlers(floating),
                    servicePresent = Field(floating, "_dragService") is not null,
                    nativeMoveResizeOwner = Field(router, "_moveResizeOwner") is not null,
                    nativeCaptureOwnerIsSource = ReferenceEquals(Field(router, "_captureOwner"), input),
                    attachPending = Field(floating, "_attachDrag"), mouseLocal = Mouse.GetPosition(floating).ToString(),
                    mouseScreen = floating.PointToScreen(Mouse.GetPosition(floating)).ToString() });
            }
        }
        finally { native.Button(1, false); Mouse.Capture(null); }
    }

    private static void GuideGeometry(X11Fixture native, Window owner, DockingManager manager, LayoutAnchorable image, LayoutAnchorable graph)
    {
        var failures = new List<string>();
        LayoutFloatingWindowControl? floating = null;
        var originalView = (NINA.WPF.Base.View.ImageView)image.Content;
        var reserve = new LayoutAnchorable
        {
            ContentId = "guide-geometry-neighbor",
            Title = "Original neighboring ImageView",
            Content = new NINA.WPF.Base.View.ImageView { Image = originalView.Image }
        };
        manager.Layout.RootPanel.Children.Insert(0, new LayoutAnchorablePane(reserve));
        Settle();
        try
        {
            floating = BeginPointerFloat(native, manager, image);
            var pane = VisualDescendants(manager).OfType<LayoutAnchorablePaneControl>().Single(p => ReferenceEquals(p.Model, graph.Parent));
            var paneRect = ScreenRect(pane, owner);
            MoveGuidePointer(new Point(paneRect.X + paneRect.Width / 2, paneRect.Y + paneRect.Height / 2));
            PointerWait(() => VisibleOverlays().Any(), "No genuine overlay was shown over the remaining original pane.");
            var overlay = VisibleOverlays().Single();
            Settle();
            ReenterGraph();
            var restore = new Rect(overlay.Left, overlay.Top, overlay.Width, overlay.Height);
            var normalOrigin = native.ClientOrigin(NativeHandle(overlay));
            var focus = native.FocusedWindow;
            var logicalFocus = Keyboard.FocusedElement;
            Require(paneRect.Width < ScreenRect(manager, owner).Width && paneRect.X > ScreenRect(manager, owner).X,
                "The pane geometry fixture did not establish a strict, independently offset manager subrectangle.");
            Capture("normal-before");
            var reserveControl = VisualDescendants(manager).OfType<LayoutAnchorablePaneControl>().Single(p => ReferenceEquals(p.Model, reserve.Parent));
            var reserveRect = ScreenRect(reserveControl, owner);
            MoveGuidePointer(new Point(reserveRect.X + reserveRect.Width / 2, reserveRect.Y + reserveRect.Height / 2));
            PointerWait(() => !Entered(pane) && Entered(reserveControl), "The original graph pane remained entered after the pointer moved into its neighboring pane.");
            var group = (FrameworkElement)overlay.Template.FindName("PART_AnchorablePaneDropTargets", overlay)!;
            var reserveGuideRect = ScreenRect(group, overlay);
            Require(Math.Abs(reserveGuideRect.X - reserveRect.X) <= 1 && Math.Abs(reserveGuideRect.Width - reserveRect.Width) <= 1,
                "The single pane-guide group did not follow the actually entered neighboring pane.");
            Observations.Add(new { stage = "independent-neighbor-pane-eligibility", graphEntered = Entered(pane), neighborEntered = Entered(reserveControl), reserveRect, reserveGuideRect });
            ReenterGraph();
            native.RequestMaximized(NativeHandle(overlay), true);
            PointerWait(() => native.State(NativeHandle(overlay)) == "Maximized" && overlay.WindowState == WindowState.Maximized
                && native.ClientOrigin(NativeHandle(overlay)) != normalOrigin, "The private WM did not relocate the genuine overlay on maximize.");
            Settle();
            Require(overlay.Left == restore.Left && overlay.Top == restore.Top, "The causal transition did not retain requested restore position.");
            Capture("maximized-after-active-areas");
            MoveGuidePointer(new Point(reserveRect.X + reserveRect.Width / 2, reserveRect.Y + reserveRect.Height / 2));
            PointerWait(() => !Entered(pane) && Entered(reserveControl), "Maximized pane exit did not follow genuine native motion.");
            ReenterGraph();
            Settle();
            Capture("maximized-reentered-areas");
            native.RequestMaximized(NativeHandle(overlay), false);
            PointerWait(() => native.State(NativeHandle(overlay)) == "Normal" && overlay.WindowState == WindowState.Normal
                && native.ClientOrigin(NativeHandle(overlay)) == normalOrigin, "The private WM did not restore the genuine overlay origin.");
            Settle();
            Capture("normal-restored");
            var template = overlay.Template;
            var previousPreview = Field(overlay, "_previewBox");
            var previousCanvas = overlay.Template.FindName("PART_DropTargetsContainer", overlay);
            overlay.Template = null;
            Settle();
            overlay.Template = template;
            overlay.ApplyTemplate();
            Settle();
            var newPreview = (System.Windows.Shapes.Path)Field(overlay, "_previewBox")!;
            Require(!ReferenceEquals(newPreview, previousPreview)
                && !ReferenceEquals(overlay.Template.FindName("PART_DropTargetsContainer", overlay), previousCanvas)
                && newPreview.IsVisible && newPreview.Data is not null,
                "Retemplating lost the selected preview before any pointer reentry.");
            Capture("active-retemplate");
            var canvas = (Canvas)overlay.Template.FindName("PART_DropTargetsContainer", overlay)!;
            var originalTransform = canvas.ReadLocalValue(UIElement.RenderTransformProperty);
            var translation = new TranslateTransform();
            var binding = new System.Windows.Data.Binding { Source = translation };
            System.Windows.Data.BindingOperations.SetBinding(canvas, UIElement.RenderTransformProperty, binding);
            var expression = System.Windows.Data.BindingOperations.GetBindingExpression(canvas, UIElement.RenderTransformProperty);
            var guide = (FrameworkElement)overlay.Template.FindName("PART_AnchorablePaneDropTargetInto", overlay)!;
            var beforeTranslation = ScreenRect(guide, overlay);
            translation.X = 2;
            translation.Y = 3;
            Settle();
            var afterTranslation = ScreenRect(guide, overlay);
            var dpi = VisualTreeHelper.GetDpi(overlay);
            Require(ReferenceEquals(expression, System.Windows.Data.BindingOperations.GetBindingExpression(canvas, UIElement.RenderTransformProperty))
                && ReferenceEquals(canvas.RenderTransform, translation)
                && Math.Abs(afterTranslation.X - beforeTranslation.X - 2 * dpi.DpiScaleX) < .001
                && Math.Abs(afterTranslation.Y - beforeTranslation.Y - 3 * dpi.DpiScaleY) < .001,
                "Geometry refresh replaced the existing template transform binding or lost its later update.");
            System.Windows.Data.BindingOperations.ClearBinding(canvas, UIElement.RenderTransformProperty);
            if (originalTransform != DependencyProperty.UnsetValue) canvas.SetValue(UIElement.RenderTransformProperty, originalTransform);
            Settle();
            Observations.Add(new { stage = "template-transform-binding-update-preserved", sameExpression = true, dpi.DpiScaleX });
            Require(Mouse.Captured == floating && Mouse.LeftButton == MouseButtonState.Pressed, "Geometry transitions changed held drag capture.");
            Require(failures.Count == 0, string.Join("; ", failures));
            paneRect = ScreenRect(pane, owner);
            MoveGuidePointer(new Point(paneRect.X + paneRect.Width / 2, paneRect.Y + paneRect.Height / 2));
            PointerWait(() => SelectedType() == "AnchorablePaneDockInside", "The independent pane center did not select its genuine Into target.");
            var oldContent = image.Content;
            native.Button(1, false);
            PointerWait(() => !image.IsFloating && ReferenceEquals(image.Parent, graph.Parent) && Mouse.Captured is null && !VisibleOverlays().Any(),
                "The actual center release did not dock into the intended original pane and clean up drag state.");
            Require(ReferenceEquals(oldContent, image.Content) && native.CanAcquirePointerGrab(), "Center docking changed content identity or retained native capture.");
            Observations.Add(new { stage = "independent-center-native-drop", image.IsFloating, samePane = ReferenceEquals(image.Parent, graph.Parent), captureReleased = Mouse.Captured is null });
            PointerDrop(native, manager, image, "Right");

            bool Entered(LayoutAnchorablePaneControl control) => ((IEnumerable<IDropArea>)Field(overlay, "_visibleAreas")!)
                .Any(a => a is DropArea<LayoutAnchorablePaneControl> area && ReferenceEquals(area.AreaElement, control));

            void ReenterGraph()
            {
                var bounds = ScreenRect(pane, owner);
                var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                MoveGuidePointer(center);
                PointerWait(() => Entered(pane), "The independent graph center did not enter its original pane area.");
                Settle();
                MoveGuidePointer(center);
                PointerWait(() => SelectedType() == "AnchorablePaneDockInside",
                    "The independent graph center did not reestablish its original entered area and selected Into target.");
                Settle();
            }

            void Capture(string stage)
            {
                var scale = VisualTreeHelper.GetDpi(overlay);
                var origin = native.ClientOrigin(NativeHandle(overlay));
                var service = Field(floating!, "_dragService")!;
                var areas = ((IEnumerable<IDropArea>)Field(manager, "_areas")!).ToArray();
                var entered = ((IEnumerable<IDropArea>)Field(overlay, "_visibleAreas")!).ToArray();
                var expectedManager = ScreenRect(manager, owner);
                var expectedPane = ScreenRect(pane, owner);
                var managerGroup = (FrameworkElement)overlay.Template.FindName("PART_DockingManagerDropTargets", overlay)!;
                var paneGroup = (FrameworkElement)overlay.Template.FindName("PART_AnchorablePaneDropTargets", overlay)!;
                var into = (FrameworkElement)overlay.Template.FindName("PART_AnchorablePaneDropTargetInto", overlay)!;
                var actualManager = ScreenRect(managerGroup, overlay);
                var actualPane = ScreenRect(paneGroup, overlay);
                var actualCenter = ScreenRect(into, overlay);
                var managerError = new Vector(actualManager.X - expectedManager.X, actualManager.Y - expectedManager.Y);
                var paneError = new Vector(actualPane.X - expectedPane.X, actualPane.Y - expectedPane.Y);
                var centerError = new Vector(actualCenter.X + actualCenter.Width / 2 - expectedPane.X - expectedPane.Width / 2,
                    actualCenter.Y + actualCenter.Height / 2 - expectedPane.Y - expectedPane.Height / 2);
                bool paneEntered = entered.Any(a => a is DropArea<LayoutAnchorablePaneControl> area && ReferenceEquals(area.AreaElement, pane));
                Observations.Add(new { stage = "independent-guide-geometry/" + stage, overlay.Left, overlay.Top, overlay.WindowState,
                    nativeOriginX = origin.X, nativeOriginY = origin.Y, expectedManager, expectedPane, actualManager, actualPane, actualCenter,
                    managerError, paneError, centerError, paneEntered, paneVisible = paneGroup.IsVisible, intoVisible = into.IsVisible,
                    currentHost = Field(service, "_currentHost")?.GetType().FullName, pointer = native.PointerPosition.ToString(),
                    areaGeometry = areas.Select(a => new { type = a.Type.ToString(), a.DetectionRect, entered = entered.Contains(a) }).ToArray(),
                    scale.DpiScaleX, scale.DpiScaleY });
                if (!paneEntered || !into.IsVisible) failures.Add(stage + ": remaining original pane has no eligible center guide");
                if (managerError.Length > 1 || paneError.Length > 1 || centerError.Length > 1) failures.Add(stage + ": guides differ from independently native-anchored panes by manager=" + managerError + " pane=" + paneError + " center=" + centerError);
                var activePreview = (System.Windows.Shapes.Path)Field(overlay, "_previewBox")!;
                var activeBounds = activePreview.Data?.Bounds ?? Rect.Empty;
                var activePreviewScreen = activeBounds.IsEmpty ? Rect.Empty : new Rect(origin.X + activeBounds.X * scale.DpiScaleX,
                    origin.Y + activeBounds.Y * scale.DpiScaleY, activeBounds.Width * scale.DpiScaleX, activeBounds.Height * scale.DpiScaleY);
                Observations.Add(new { stage = "active-pane-preview-before-motion/" + stage, activePreviewScreen = activePreviewScreen.IsEmpty ? (Rect?)null : activePreviewScreen, expectedPane, selected = SelectedType() });
                if (SelectedType() != "AnchorablePaneDockInside" || !activePreview.IsVisible || activePreviewScreen.IsEmpty
                    || Math.Abs(activePreviewScreen.X - expectedPane.X) > 1 || Math.Abs(activePreviewScreen.Y - expectedPane.Y) > 1
                    || Math.Abs(activePreviewScreen.Width - expectedPane.Width) > 1 || Math.Abs(activePreviewScreen.Height - expectedPane.Height) > 1)
                    failures.Add(stage + ": existing selected pane preview failed to refresh before pointer motion");
                var expectedCenter = new Point(expectedPane.X + expectedPane.Width / 2, expectedPane.Y + expectedPane.Height / 2);
                Require(into is Border { Child: Image { Source: BitmapSource { PixelWidth: 25, PixelHeight: 25 } } centerImage }
                    && centerImage.ActualWidth > 0 && centerImage.ActualHeight > 0,
                    "The original indexed Into guide image was not decoded and arranged.");
                Require(overlay.Template.FindName("PART_DockingManagerDropTargetLeft", overlay) is Image
                    { Source: BitmapSource { PixelWidth: 32, PixelHeight: 29 } },
                    "The paired original RGB manager guide image was not decoded.");
                MoveGuidePointer(new Point(expectedPane.X + 4 * scale.DpiScaleX, expectedPane.Y + expectedPane.Height / 3));
                Settle();
                Require(SelectedType() == null && !activePreview.IsVisible,
                    "The guide artwork probe must be outside drop targets with no selected preview.");
                if (!Ink(expectedCenter)) failures.Add(stage + ": pane center has no guide artwork without a preview");
                MoveGuidePointer(expectedCenter);
                Settle();
                if (SelectedType() != "AnchorablePaneDockInside") failures.Add(stage + ": independent pane center did not hit the Into target");
                bool centerInk = Ink(expectedCenter);
                if (!centerInk) failures.Add(stage + ": independent pane center has no guide pixels");
                var left = (FrameworkElement)overlay.Template.FindName("PART_DockingManagerDropTargetLeft", overlay)!;
                var expectedLeft = new Point(expectedManager.X + left.ActualWidth * scale.DpiScaleX / 2, expectedManager.Y + expectedManager.Height / 2);
                MoveGuidePointer(expectedLeft);
                Settle();
                if (SelectedType() != "DockingManagerDockLeft") failures.Add(stage + ": independent manager-left center did not hit the edge target");
                var previewPart = (System.Windows.Shapes.Path)Field(overlay, "_previewBox")!;
                var bounds = previewPart.Data?.Bounds ?? Rect.Empty;
                var previewScreen = bounds.IsEmpty ? Rect.Empty : new Rect(origin.X + bounds.X * scale.DpiScaleX, origin.Y + bounds.Y * scale.DpiScaleY,
                    bounds.Width * scale.DpiScaleX, bounds.Height * scale.DpiScaleY);
                bool previewInsideInk = Ink(new Point(expectedManager.X + 8 * scale.DpiScaleX, expectedManager.Y + 8 * scale.DpiScaleY));
                bool previewOutsideInk = Ink(new Point(expectedManager.Right - 8 * scale.DpiScaleX, expectedManager.Y + 8 * scale.DpiScaleY));
                var stableGeometry = previewPart.Data;
                for (int i = 0; i < 8; i++) Settle();
                Require(ReferenceEquals(previewPart.Data, stableGeometry), "Unchanged layout repeatedly replaced selected preview geometry.");
                Require(native.FocusedWindow == focus && ReferenceEquals(Keyboard.FocusedElement, logicalFocus)
                    && Mouse.Captured == floating, "Guide/preview interaction changed focus or drag capture.");
                Observations.Add(new { stage = "independent-selected-preview/" + stage, expectedCenter, centerInk, expectedLeft,
                    selected = SelectedType(), previewPart.IsVisible, previewScreen = previewScreen.IsEmpty ? (Rect?)null : previewScreen, expectedManager, previewInsideInk, previewOutsideInk });
                if (previewScreen.IsEmpty || !previewPart.IsVisible || Math.Abs(previewScreen.X - expectedManager.X) > 1
                    || Math.Abs(previewScreen.Y - expectedManager.Y) > 1 || Math.Abs(previewScreen.Height - expectedManager.Height) > 1)
                    failures.Add(stage + ": selected manager-left preview is displaced");
                if (!previewInsideInk || previewOutsideInk) failures.Add(stage + ": selected manager-left preview pixels do not match its independent inside/outside probes");
                ReenterGraph();

                bool Ink(Point screen)
                {
                    int x = (int)Math.Round(screen.X - origin.X), y = (int)Math.Round(screen.Y - origin.Y);
                    return (native.Pixel(NativeHandle(overlay), x, y, true) >> 24) != 0;
                }
            }
        }
        finally
        {
            native.Button(1, false);
            Mouse.Capture(null);
            if (image.IsFloating) image.Dock();
            if (reserve.Parent is LayoutAnchorablePane reserveParent) reserveParent.Children.Remove(reserve);
            manager.Layout.CollectGarbage();
            Settle();
        }

        void MoveGuidePointer(Point point)
        {
            var current = native.PointerPosition;
            if (current.X == (int)Math.Round(point.X) && current.Y == (int)Math.Round(point.Y))
                MoveHeldPointerAndWait(native, floating!, new Point(point.X + 2, point.Y));
            MoveHeldPointerAndWait(native, floating!, point);
        }

        string? SelectedType()
        {
            var service = Field(floating!, "_dragService");
            var target = service is null ? null : Field(service, "_currentDropTarget");
            return target?.GetType().GetProperty("Type")?.GetValue(target)?.ToString();
        }

        Rect ScreenRect(FrameworkElement element, Window window)
        {
            var origin = native.ClientOrigin(NativeHandle(window));
            var scale = VisualTreeHelper.GetDpi(window);
            var local = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new Rect(origin.X + local.X * scale.DpiScaleX, origin.Y + local.Y * scale.DpiScaleY,
                local.Width * scale.DpiScaleX, local.Height * scale.DpiScaleY);
        }
    }

    private static void CanceledCloseAndRecovery(X11Fixture native, DockingManager manager, LayoutAnchorable pane, bool verifyHeaders = false)
    {
        LayoutFloatingWindowControl? floating = null;
        MouseButtonEventHandler? observePress = null;
        var presses = new List<object>();
        bool canHide = pane.CanHide;
        int closing = 0, closed = 0;
        bool canceled = false;
        System.ComponentModel.CancelEventHandler observeClosing = (_, args) => { closing++; canceled = args.Cancel; };
        EventHandler observeClosed = (_, _) => closed++;
        try
        {
            floating = BeginPointerFloat(native, manager, pane);
            MoveHeldPointerAndWait(native, floating, new Point(3, 3));
            native.Button(1, false);
            PointerWait(() => Mouse.Captured == null && Field(floating, "_dragService") is null && !VisibleOverlays().Any(),
                "The initial outside release did not clean up the floating drag.");
            Require(DragInputHandlers(floating) == 0 && native.CanAcquirePointerGrab(), "The released floating window retained its input handler or native grab.");
            var source = PresentationSource.FromVisual(floating);
            var xid = NativeHandle(floating);
            var model = floating.Model;
            var contents = model.Descendents().OfType<LayoutAnchorable>().ToArray();
            var contentIdentities = contents.Select(item => item.Content).ToArray();
            floating.Closing += observeClosing;
            floating.Closed += observeClosed;
            pane.CanHide = false;
            Require(!((LayoutAnchorableFloatingWindowControl)floating).HideWindowCommand.CanExecute(null),
                "The original anchorable close policy did not become ineligible to hide.");
            floating.Close();
            Settle();
            Require(closing == 1 && canceled && closed == 0 && floating.IsVisible && native.IsMapped(xid)
                && ReferenceEquals(PresentationSource.FromVisual(floating), source) && NativeHandle(floating) == xid
                && ReferenceEquals(floating.Model, model) && manager.FloatingWindows.Contains(floating)
                && model.Descendents().OfType<LayoutAnchorable>().SequenceEqual(contents)
                && contents.Select(item => item.Content).SequenceEqual(contentIdentities),
                "The canceled original close changed the live source, model, content or tab identities.");
            Require(Mouse.Captured == null && Field(floating, "_dragService") is null && DragInputHandlers(floating) == 0
                && !VisibleOverlays().Any() && native.CanAcquirePointerGrab(), "A canceled close acquired drag state or retained native input resources.");
            var bounds = native.Bounds(xid);
            Observations.Add(new { stage = "native-canceled-floating-close", closing, canceled, closed,
                xid = xid.ToInt64(), bounds.X, bounds.Y, bounds.Width, bounds.Height,
                floating.IsVisible, floating.IsActive, nativeFocus = native.FocusedWindow.ToInt64(),
                originalClosingFlag = Field(floating, "_isClosing"), contents = contents.Select(item => item.ContentId).ToArray() });
            if (verifyHeaders)
            {
                VerifyTabHeaders(floating, manager, "canceled-close-original-tab-headers");
                Require(ReferenceEquals(FloatingUIParent(floating), manager), "Canceled close lost the live manager bridge.");
                floating.Hide(); Settle();
                Require(!native.IsMapped(xid) && ReferenceEquals(PresentationSource.FromVisual(floating), source)
                    && ReferenceEquals(FloatingUIParent(floating), manager), "Hide lost the retained source or manager bridge.");
                floating.Show(); Settle(); PointerWait(() => native.IsMapped(xid), "The hidden original floating source did not remap.");
                VerifyTabHeaders(floating, manager, "reshown-original-tab-headers"); VerifyFloatingManagerRoute(native, manager, floating);
            }
            pane.CanHide = canHide;
            observePress = (_, args) => presses.Add(new { stage = "native-canceled-close-recovery-press", args.ClickCount,
                args.Timestamp, args.Handled, originalSource = args.OriginalSource?.GetType().FullName,
                xid = NativeHandle(floating).ToInt64(), nativeFocus = native.FocusedWindow.ToInt64(),
                originalClosingFlag = Field(floating, "_isClosing") });
            floating.AddHandler(Mouse.PreviewMouseDownEvent, observePress, true);
            PointerDrop(native, manager, pane, "Right", 64);
            Require(presses.Count == 1 && closing == 2 && closed == 1,
                "Canceled-close recovery did not deliver one native press and one subsequent accepted drop close.");
            if (verifyHeaders) Require(FloatingUIParent(floating) == null, "Accepted close retained the manager bridge.");
        }
        finally
        {
            pane.CanHide = canHide;
            native.Button(1, false);
            Mouse.Capture(null);
            if (floating != null)
            {
                floating.Closing -= observeClosing;
                floating.Closed -= observeClosed;
                if (observePress != null) floating.RemoveHandler(Mouse.PreviewMouseDownEvent, observePress);
            }
            Observations.Add(new { stage = "native-canceled-close-recovery-input", presses = presses.ToArray(), closing, canceled, closed });
        }
    }

    private static void AcceptedFloatingClose(X11Fixture native, DockingManager manager, LayoutAnchorable pane)
    {
        LayoutFloatingWindowControl? floating = null;
        bool canHide = pane.CanHide;
        int closing = 0, closed = 0;
        bool canceled = true;
        System.ComponentModel.CancelEventHandler observeClosing = (_, args) => { closing++; canceled = args.Cancel; };
        EventHandler observeClosed = (_, _) => closed++;
        try
        {
            pane.CanHide = true;
            floating = BeginPointerFloat(native, manager, pane);
            var xid = NativeHandle(floating);
            floating.Closing += observeClosing;
            floating.Closed += observeClosed;
            floating.Close();
            PointerWait(() => PresentationSource.FromVisual(floating) == null && !native.IsMapped(xid)
                && Mouse.Captured == null && !VisibleOverlays().Any(), "Accepted close retained the floating native source, capture or overlays.");
            Require(closing == 1 && !canceled && closed == 1 && !floating.IsVisible && !manager.FloatingWindows.Contains(floating)
                && Field(floating, "_dragService") is null && DragInputHandlers(floating) == 0,
                "Accepted close did not run the original floating cleanup exactly once.");
            native.Button(1, false);
            PointerWait(native.CanAcquirePointerGrab, "Accepted floating close retained the native server grab.");
            Observations.Add(new { stage = "native-accepted-floating-close", closing, canceled, closed, xid = xid.ToInt64(),
                nativeFocus = native.FocusedWindow.ToInt64(), handlerCount = DragInputHandlers(floating), sourcePresent = PresentationSource.FromVisual(floating) != null });
        }
        finally
        {
            pane.CanHide = canHide;
            native.Button(1, false);
            Mouse.Capture(null);
            if (floating != null) { floating.Closing -= observeClosing; floating.Closed -= observeClosed; }
        }
    }

    private static void MaximizedCaption(X11Fixture native, DockingManager manager, LayoutAnchorable pane)
    {
        LayoutFloatingWindowControl? floating = null;
        var presses = new List<int>();
        bool observingRecoveryPress = false;
        MouseButtonEventHandler observe = (sender, mouse) =>
        {
            if (mouse.ChangedButton != MouseButton.Left) return;
            presses.Add(mouse.ClickCount);
            if (observingRecoveryPress && floating is not null)
            {
                var physical = native.PointerPosition;
                var local = mouse.GetPosition(floating);
                var hit = floating.InputHitTest(local);
                var bounds = native.Bounds(NativeHandle(floating));
                Observations.Add(new { stage = "native-maximized-recovery-caption-press", mouse.ClickCount, mouse.Timestamp,
                    routedEvent = mouse.RoutedEvent.Name, mouse.Handled, senderType = sender.GetType().FullName,
                    originalSource = mouse.OriginalSource?.GetType().FullName, source = mouse.Source?.GetType().FullName,
                    hit = hit?.GetType().FullName, localX = local.X, localY = local.Y, physicalX = physical.X, physicalY = physical.Y,
                    bounds.X, bounds.Y, bounds.Width, bounds.Height, floating.WindowState, floating.IsMaximized,
                    floating.IsActive, floating.IsVisible, floating.IsEnabled, nativeFocus = native.FocusedWindow.ToInt64(),
                    xid = NativeHandle(floating).ToInt64(), capture = Mouse.Captured?.GetType().FullName,
                    servicePresent = Field(floating, "_dragService") is not null, presses = presses.ToArray() });
            }
        };
        try
        {
            floating = BeginPointerFloat(native, manager, pane);
            native.MovePointer(3, 3);
            PointerWait(() => !VisibleOverlays().Any(), "The initial native float did not leave all drop targets.");
            native.Button(1, false);
            PointerWait(() => Mouse.Captured is null && Field(floating, "_dragService") is null,
                "The initial float retained drag state before maximize setup.");
            var model = floating.Model;
            var source = PresentationSource.FromVisual(floating);
            var xid = NativeHandle(floating);
            var parent = (LayoutAnchorablePane)pane.Parent;
            var contents = parent.Children.ToArray();
            var identities = contents.ToDictionary(item => item, item => item.Content);
            var normal = native.Bounds(xid);
            floating.WindowState = WindowState.Maximized;
            PointerWait(() => floating.WindowState == WindowState.Maximized && floating.IsMaximized && contents.All(item => item.IsMaximized),
                "The genuine floating Window/model did not enter maximized state.");
            PointerWait(() => native.Bounds(xid).Width > normal.Width && native.Bounds(xid).Height > normal.Height,
                "The native floating source did not actually grow for maximization.");
            var maximized = native.Bounds(xid);
            floating.AddHandler(Mouse.PreviewMouseDownEvent, observe, true);
            var caption = FloatingCaption(floating);
            var stationaryPoint = caption.PointToScreen(new Point(Math.Min(64, caption.ActualWidth / 2), caption.ActualHeight / 2));
            native.MovePointer((int)Math.Round(stationaryPoint.X), (int)Math.Round(stationaryPoint.Y)); Settle();
            var stationaryPointer = native.PointerPosition;
            native.Button(1, true);
            PointerWait(() => presses.Count == 1 && Mouse.LeftButton == MouseButtonState.Pressed
                && Mouse.Captured == floating && Field(floating, "_dragService") is not null,
                "The stationary maximized native caption press did not reach the genuine drag source.");
            Require(presses[0] == 1 && floating.WindowState == WindowState.Maximized && floating.IsMaximized
                && contents.All(item => item.IsMaximized) && native.Bounds(xid) == maximized && native.PointerPosition == stationaryPointer,
                "A stationary native caption press restored or moved the maximized pane.");
            native.Button(1, false);
            PointerWait(() => Mouse.LeftButton == MouseButtonState.Released && Mouse.Captured is null
                && Field(floating, "_dragService") is null && DragInputHandlers(floating) == 0,
                "The stationary maximized caption release retained input state.");
            var stationaryBounds = native.Bounds(xid);
            Require(floating.WindowState == WindowState.Maximized && floating.IsMaximized && contents.All(item => item.IsMaximized)
                && stationaryBounds == maximized && native.PointerPosition == stationaryPointer && native.CanAcquirePointerGrab()
                && native.FocusedWindow == xid && ReferenceEquals(PresentationSource.FromVisual(floating), source)
                && ReferenceEquals(floating.Model, model) && parent.Children.SequenceEqual(contents),
                "A stationary native caption release changed maximized state, geometry, identity, focus or native capture.");
            Observations.Add(new { stage = "native-maximized-caption-stationary-release", floating.WindowState, floating.IsMaximized,
                modelMaximized = contents.Select(item => item.IsMaximized).ToArray(), stationaryBounds.X, stationaryBounds.Y,
                stationaryBounds.Width, stationaryBounds.Height, pointerX = stationaryPointer.X, pointerY = stationaryPointer.Y,
                nativeFocus = native.FocusedWindow.ToInt64(), xid = xid.ToInt64(), presses = presses.ToArray(),
                servicePresent = Field(floating, "_dragService") is not null, capture = Mouse.Captured?.GetType().FullName });
            presses.Clear();
            var dragged = BeginPointerFloat(native, manager, pane);
            var restored = native.Bounds(xid);
            var pointer = native.PointerPosition;
            Observations.Add(new { stage = "native-maximized-caption-held",
                normal = new { normal.X, normal.Y, normal.Width, normal.Height },
                maximized = new { maximized.X, maximized.Y, maximized.Width, maximized.Height },
                restored = new { restored.X, restored.Y, restored.Width, restored.Height },
                floating.WindowState, floating.IsMaximized, modelMaximized = contents.Select(item => item.IsMaximized).ToArray(),
                presses = presses.ToArray(), nativeFocus = native.FocusedWindow.ToInt64(), xid = xid.ToInt64(),
                pointerX = pointer.X, pointerY = pointer.Y, captured = Mouse.Captured == floating, logicalButton = Mouse.LeftButton.ToString(),
                servicePresent = Field(floating, "_dragService") is not null });
            Require(presses.SequenceEqual(new[] { 1 }) && ReferenceEquals(dragged, floating),
                "The maximized caption did not receive one genuine qualifying native left press.");
            Require(floating.WindowState == WindowState.Normal && !floating.IsMaximized && contents.All(item => !item.IsMaximized),
                "Native maximized caption drag did not restore WindowState and all original model maximized flags.");
            PointerWait(() => native.Bounds(xid).Width < maximized.Width && native.Bounds(xid).Height < maximized.Height,
                "The restored caption drag retained the maximized native geometry.");
            Require(ReferenceEquals(floating.Model, model) && ReferenceEquals(PresentationSource.FromVisual(floating), source)
                && NativeHandle(floating) == xid && native.FocusedWindow == xid && Mouse.Captured == floating
                && Mouse.LeftButton == MouseButtonState.Pressed && parent.Children.SequenceEqual(contents)
                && contents.All(item => ReferenceEquals(item.Content, identities[item])),
                "Maximized caption restoration changed model/content/tab/source identity, focus or held capture.");
            native.MovePointer(3, 3);
            PointerWait(() => !VisibleOverlays().Any(), "Restored maximized drag retained an outside overlay.");
            native.Button(1, false);
            PointerWait(() => Mouse.Captured is null && Field(floating, "_dragService") is null && DragInputHandlers(floating) == 0,
                "Restored maximized caption release retained capture/service/input handlers.");
            Require(native.CanAcquirePointerGrab(), "Restored maximized caption release retained the native grab.");
            observingRecoveryPress = true;
            PointerDrop(native, manager, pane, "Right", 64);
        }
        finally
        {
            if (floating is not null) floating.RemoveHandler(Mouse.PreviewMouseDownEvent, observe);
            native.Button(1, false); Mouse.Capture(null);
        }
    }

    private static void VerifyTabHeaders(DependencyObject root, DockingManager manager, string stage)
    {
        var tabs = VisualDescendants(root).OfType<LayoutAnchorableTabItem>().Where(tab => tab.IsVisible).ToArray();
        Require(tabs.Length == 2, "The original two-tab header probe did not realize both tabs.");
        var values = tabs.Select(tab =>
        {
            var presenter = VisualDescendants(tab).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, tab.Model));
            var texts = VisualDescendants(presenter).OfType<TextBlock>().Select(block => block.Text).ToArray();
            var binding = BindingOperations.GetBindingExpression(presenter, ContentPresenter.ContentTemplateProperty);
            return new { tab.Model.ContentId, tab.Model.Title, texts, templateMatches = ReferenceEquals(presenter.ContentTemplate, manager.AnchorableHeaderTemplate),
                bindingStatus = binding?.Status.ToString(), managerMatches = ReferenceEquals(binding?.DataItem, manager), tab.ActualWidth, tab.ActualHeight };
        }).ToArray();
        Observations.Add(new { stage, values });
        Require(values.All(value => value.templateMatches && value.managerMatches && value.texts.Contains(value.Title)),
            "Original NINA tabs did not render their model Titles through their actual manager header template.");
    }

    private static object? FloatingUIParent(LayoutFloatingWindowControl floating) =>
        typeof(LayoutFloatingWindowControl).GetMethod("GetUIParentCore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(floating, null);

    private static void VerifyFloatingManagerRoute(X11Fixture native, DockingManager manager, LayoutFloatingWindowControl floating, DockingManager? stale = null)
    {
        var tab = VisualDescendants(floating).OfType<LayoutAnchorableTabItem>().First(item => item.IsVisible);
        int presses = 0, commands = 0, staleCalls = 0;
        MouseButtonEventHandler press = (_, _) => presses++;
        MouseButtonEventHandler stalePress = (_, _) => staleCalls++;
        var command = new RoutedCommand("FloatingManagerRoute", typeof(DockingProgram));
        var binding = new CommandBinding(command, (_, args) => { commands++; args.Handled = true; });
        var staleBinding = new CommandBinding(command, (_, args) => { staleCalls++; args.Handled = true; });
        manager.AddHandler(Mouse.PreviewMouseDownEvent, press, true); manager.CommandBindings.Add(binding);
        stale?.AddHandler(Mouse.PreviewMouseDownEvent, stalePress, true); stale?.CommandBindings.Add(staleBinding);
        try
        {
            command.Execute(null, tab);
            var point = tab.PointToScreen(new Point(tab.ActualWidth / 2, tab.ActualHeight / 2));
            native.MovePointer((int)Math.Round(point.X), (int)Math.Round(point.Y)); Settle();
            native.Button(1, true); native.Button(1, false);
            PointerWait(() => presses == 1 && Mouse.LeftButton == MouseButtonState.Released, "The original manager did not receive exactly one floating-tab native press.");
            Require(commands == 1 && staleCalls == 0 && ReferenceEquals(FloatingUIParent(floating), manager)
                && native.FocusedWindow == NativeHandle(floating) && Mouse.Captured == null && native.CanAcquirePointerGrab(),
                "Floating command/input ancestry duplicated, reached a stale manager or changed native focus/capture.");
            Observations.Add(new { stage = "floating-original-manager-route", presses, commands, staleCalls,
                xid = NativeHandle(floating).ToInt64(), nativeFocus = native.FocusedWindow.ToInt64() });
        }
        finally
        {
            native.Button(1, false); manager.RemoveHandler(Mouse.PreviewMouseDownEvent, press); manager.CommandBindings.Remove(binding);
            stale?.RemoveHandler(Mouse.PreviewMouseDownEvent, stalePress); stale?.CommandBindings.Remove(staleBinding);
        }
    }

    private static void ReleaseHeaderDrag(X11Fixture native, LayoutFloatingWindowControl floating)
    {
        MoveHeldPointerAndWait(native, floating, new Point(3, 3)); native.Button(1, false);
        PointerWait(() => Mouse.Captured == null && Field(floating, "_dragService") is null && !VisibleOverlays().Any(), "The header probe retained its native drag.");
        Require(native.CanAcquirePointerGrab(), "The header probe retained its native pointer grab.");
    }

    private static void FloatingTabHeaders(X11Fixture native, DockingManager manager, LayoutAnchorable image)
    {
        VerifyTabHeaders(manager, manager, "docked-original-tab-headers");
        var panes = ((LayoutAnchorablePane)image.Parent).Children.ToArray(); var titles = panes.Select(pane => pane.Title).ToArray();
        try
        {
            var floating = BeginPointerFloat(native, manager, image);
            VerifyTabHeaders(floating, manager, "loaded-floating-original-tab-headers");
            ReleaseHeaderDrag(native, floating); VerifyFloatingManagerRoute(native, manager, floating);
            foreach (var pane in panes) pane.Title += " [floating]";
            Settle(); VerifyTabHeaders(floating, manager, "changed-floating-original-tab-headers");
            PointerDrop(native, manager, image, "Right", 64);
            Require(FloatingUIParent(floating) == null, "A closed floating source retained its manager bridge.");
            VerifyTabHeaders(manager, manager, "redocked-original-tab-headers");
        }
        finally { native.Button(1, false); Mouse.Capture(null); for (int index = 0; index < panes.Length; index++) panes[index].Title = titles[index]; }
    }

    private static void FloatingTabCurrentManager(X11Fixture native, DockingManager manager, LayoutAnchorable image)
    {
        var layout = manager.Layout; var next = new DockingManager();
        var owner = new Window { Title = "Original header next manager", Content = next, Left = 700, Top = 300, Width = 320, Height = 300, ShowActivated = false };
        try
        {
            owner.Show(); Settle(); PointerWait(() => native.IsMapped(NativeHandle(owner)), "The second original manager did not map.");
            manager.Layout = new LayoutRoot { RootPanel = new LayoutPanel() }; next.Layout = layout; Settle();
            VerifyTabHeaders(next, next, "reparented-original-manager-docked");
            var floating = BeginPointerFloat(native, next, image);
            VerifyTabHeaders(floating, next, "reparented-original-manager-floating");
            ReleaseHeaderDrag(native, floating); VerifyFloatingManagerRoute(native, next, floating, manager);
            PointerDrop(native, next, image, "Right", 64);
            Require(FloatingUIParent(floating) == null, "The reparented closed source retained its current manager.");
        }
        finally
        {
            native.Button(1, false); Mouse.Capture(null);
            if (ReferenceEquals(next.Layout, layout)) { next.Layout = new LayoutRoot { RootPanel = new LayoutPanel() }; manager.Layout = layout; }
            owner.Close(); Settle();
        }
        VerifyTabHeaders(manager, manager, "restored-original-manager-docked");
    }

    private static void CaptureTransferAndRecovery(X11Fixture native, DockingManager manager, LayoutAnchorable image)
    {
        var recipient = new TextBox { Text = "Unrelated Escape recipient", Background = Brushes.DarkBlue };
        var other = new Window { Title = "Pointer interruption recipient", Content = recipient, Width = 120, Height = 90, Left = 820, Top = 50, ShowActivated = false };
        try
        {
            other.Show(); Settle();
            var floating = BeginPointerFloat(native, manager, image);
            Require(recipient.CaptureMouse(), "The real WPF capture transfer was refused.");
            PointerWait(() => Mouse.Captured == recipient && !VisibleOverlays().Any(), "Capture loss retained the old drag overlay.");
            Require(Field(floating, "_dragService") == null && image.IsFloating, "Capture loss retained an active drag service or changed the model.");
            Require(DragInputHandlers(floating) == 0, "Capture transfer retained the old drag input handler.");
            native.Button(1, false); Mouse.Capture(null);
            var point = recipient.PointToScreen(new Point(recipient.ActualWidth / 2, recipient.ActualHeight / 2));
            native.MovePointer((int)Math.Round(point.X), (int)Math.Round(point.Y));
            native.Button(1, true); native.Button(1, false);
            PointerWait(() => Keyboard.FocusedElement == recipient && native.FocusedWindow == NativeHandle(other),
                "The unrelated editor did not receive focus through its real native click.");
            int escapes = 0;
            KeyEventHandler observe = (_, key) => { if (key.Key == Key.Escape && !key.Handled) escapes++; };
            recipient.PreviewKeyDown += observe;
            try
            {
                native.Key((char)0xff1b, true); native.Key((char)0xff1b, false);
                PointerWait(() => escapes == 1, "A subsequent native Escape did not reach its normal unrelated target.");
            }
            finally { recipient.PreviewKeyDown -= observe; }
        }
        finally { native.Button(1, false); Mouse.Capture(null); other.Close(); }
        PointerDrop(native, manager, image, "Right");
    }

    private static void ExercisePointerOwnerClose(X11Fixture native, Window owner, DockingManager manager, LayoutAnchorable pane)
    {
        if (Environment.GetEnvironmentVariable("NINA_POINTER_CANCELED_CLOSE_ONLY") == "1")
        {
            Check("pointer-accepted-floating-close-native-lifetime", () => AcceptedFloatingClose(native, manager, pane));
            return;
        }
        Check("pointer-owner-close-native-lifetime", () =>
        {
            Window? floating = null;
            try
            {
                floating = BeginPointerFloat(native, manager, pane);
                owner.Close();
                PointerWait(() => PresentationSource.FromVisual(floating) == null && Mouse.Captured == null && !VisibleOverlays().Any(),
                    "Owner close retained a floating source, overlay or mouse capture.");
            }
            finally { native.Button(1, false); Mouse.Capture(null); }
            PointerWait(native.CanAcquirePointerGrab, "Owner close left a server pointer grab behind.");
            Require(floating is not null && DragInputHandlers(floating) == 0, "Owner close retained the drag input handler.");
        });
    }

    private static LayoutFloatingWindowControl BeginPointerFloat(X11Fixture native, DockingManager manager, LayoutAnchorable pane, double captionPointX = 24)
    {
        bool startedFloating = pane.IsFloating;
        DependencyObject titleRoot = startedFloating
            ? manager.FloatingWindows.Single(window => ReferenceEquals(window.Model, pane.FindParent<LayoutFloatingWindow>()))
            : manager;
        Observations.Add(new { stage = "before-pointer-pane-selection", pane.ContentId, pane.IsSelected, pane.IsFloating,
            visibleTitles = VisualDescendants(titleRoot).OfType<AnchorablePaneTitle>().Where(element => element.IsVisible)
                .Select(element => element.Model?.ContentId).ToArray() });
        pane.IsSelected = true;
        Settle();
        FrameworkElement title = startedFloating
            ? FloatingCaption((LayoutFloatingWindowControl)titleRoot)
            : VisualDescendants(titleRoot).OfType<AnchorablePaneTitle>().Single(element => element.IsVisible && ReferenceEquals(element.Model, pane));
        Require(title.ActualWidth > 0 && title.ActualHeight > 0, "The genuine pane title has no native pointer area.");
        var start = title.PointToScreen(new Point(Math.Min(captionPointX, title.ActualWidth / 2), title.ActualHeight / 2));
        native.MovePointer((int)Math.Round(start.X), (int)Math.Round(start.Y));
        Settle();
        native.Button(1, true);
        PointerWait(() => Mouse.LeftButton == MouseButtonState.Pressed, "The native title press was not observed by WPF.");
        if (startedFloating)
        {
            var existing = (LayoutFloatingWindowControl)titleRoot;
            PointerWait(() => Mouse.Captured == existing && Field(existing, "_dragService") is not null,
                "The genuine existing floating caption press did not reach portable drag/capture.");
            Observations.Add(new { stage = "native-existing-caption-press", pane.ContentId, xid = NativeHandle(existing).ToInt64(), start });
        }
        var outsideTitle = title.PointToScreen(new Point(Math.Min(captionPointX, title.ActualWidth / 2), title.ActualHeight + 30));
        if (startedFloating) MoveHeldPointerAndWait(native, (LayoutFloatingWindowControl)titleRoot, outsideTitle);
        else native.MovePointer((int)Math.Round(outsideTitle.X), (int)Math.Round(outsideTitle.Y));
        bool MatchesDraggedPane(LayoutFloatingWindowControl window) => ReferenceEquals(window.Model, pane.FindParent<LayoutFloatingWindow>());
        PointerWait(() => pane.IsFloating && manager.FloatingWindows.Any(window => MatchesDraggedPane(window) && window.IsVisible && window.IsLoaded
            && PresentationSource.FromVisual(window) != null && native.IsMapped(NativeHandle(window))),
            "Held native title motion did not create a shown/Loaded floating window.");
        var floating = manager.FloatingWindows.Single(MatchesDraggedPane);
        PointerWait(() => Mouse.Captured == floating, "The shown floating window did not own genuine WPF drag capture.");
        Require(Mouse.LeftButton == MouseButtonState.Pressed && !native.CanAcquirePointerGrab(), "Floating lost the held button or its actual server grab.");
        Require(DragInputHandlers(floating) == 1, "The active native drag does not have exactly one scoped input handler.");
        Require(ReferenceEquals(PresentationSource.FromVisual((Visual)pane.Content), PresentationSource.FromVisual(floating)), "The original content was not attached to the floating source.");
        var pointer = native.PointerPosition;
        Observations.Add(new { stage = "native-held-float", pane.ContentId, startedFloating, floating.Left, floating.Top, pointerX = pointer.X, pointerY = pointer.Y,
            logicalButton = Mouse.LeftButton.ToString(), capture = Mouse.Captured?.GetType().FullName, floating.IsLoaded, floating.IsVisible });
        return floating;
    }

    private static void PointerDrop(X11Fixture native, DockingManager manager, LayoutAnchorable pane, string direction, double captionPointX = 24)
    {
        var content = pane.Content;
        var initialModel = pane.FindParent<LayoutFloatingWindow>();
        var otherWindows = manager.FloatingWindows.Where(window => !ReferenceEquals(window.Model, initialModel))
            .Select(window => new { Window = window, window.Model, Source = PresentationSource.FromVisual(window), Xid = NativeHandle(window) }).ToArray();
        LayoutFloatingWindowControl? floating = null;
        object? tracedPeer = null;
        EventInfo? rawInput = null;
        Delegate? rawObserver = null;
        ProcessInputEventHandler? processedMoveObserver = null;
        var heldMoves = new List<object>();
        try
        {
            floating = BeginPointerFloat(native, manager, pane, captionPointX);
            var draggedPane = (LayoutAnchorablePane)pane.Parent;
            var draggedContents = draggedPane.Children.ToArray();
            var contentIdentities = draggedContents.ToDictionary(model => model, model => model.Content);
            if (Environment.GetEnvironmentVariable("NINA_POINTER_RECOVERY_ONLY") is not null)
            {
                var source = PresentationSource.FromVisual(floating)!;
                tracedPeer = Field(source, "_host")!;
                rawInput = tracedPeer.GetType().GetEvent("RawInput")!;
                var initialService = Field(floating, "_dragService");
                Action<object> observe = report =>
                {
                    var type = report.GetType();
                    if (type.Name != "RawMouseInputReport" || PresentationSource.FromVisual(floating) is null) return;
                    object Read(string name) => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(report)!;
                    var observedPointer = native.PointerPosition;
                    var bounds = native.Bounds(NativeHandle(floating));
                    var local = Mouse.GetPosition(floating);
                    var service = Field(floating, "_dragService");
                    var offset = (Point)Field(floating, "_portableDragOffset")!;
                    var observedScale = VisualTreeHelper.GetDpi(floating);
                    if (heldMoves.Count == 12) heldMoves.RemoveAt(0);
                    heldMoves.Add(new { phase = "native-report", timestamp = Read("Timestamp"), actions = Read("Actions").ToString(), rawX = Read("X"), rawY = Read("Y"),
                        physicalX = observedPointer.X, physicalY = observedPointer.Y, clientX = bounds.X, clientY = bounds.Y, clientWidth = bounds.Width, clientHeight = bounds.Height,
                        local, logicalScreen = floating.PointToScreen(local), floating.Left, floating.Top,
                        servicePointFromAppliedPosition = new Point((floating.Left + offset.X) * observedScale.DpiScaleX, (floating.Top + offset.Y) * observedScale.DpiScaleY),
                        currentService = ReferenceEquals(initialService, service), servicePresent = service is not null,
                        host = service is null ? null : Field(service, "_currentHost")?.GetType().FullName,
                        nativeFocus = native.FocusedWindow.ToInt64(), floating.IsActive, captured = Mouse.Captured == floating });
                };
                rawObserver = Delegate.CreateDelegate(rawInput.EventHandlerType!, observe.Target, observe.Method);
                rawInput.AddEventHandler(tracedPeer, rawObserver);
                processedMoveObserver = (_, args) =>
                {
                    if (args.StagingItem.Input is not MouseEventArgs mouse || mouse.RoutedEvent != Mouse.PreviewMouseMoveEvent
                        || mouse.LeftButton != MouseButtonState.Pressed || PresentationSource.FromVisual(floating) is null) return;
                    var observedPointer = native.PointerPosition;
                    var bounds = native.Bounds(NativeHandle(floating));
                    var local = mouse.GetPosition(floating);
                    var service = Field(floating, "_dragService");
                    var offset = (Point)Field(floating, "_portableDragOffset")!;
                    var observedScale = VisualTreeHelper.GetDpi(floating);
                    if (heldMoves.Count == 12) heldMoves.RemoveAt(0);
                    heldMoves.Add(new { phase = "processed-routed-move", timestamp = mouse.Timestamp,
                        physicalX = observedPointer.X, physicalY = observedPointer.Y, clientX = bounds.X, clientY = bounds.Y,
                        clientWidth = bounds.Width, clientHeight = bounds.Height, local, logicalScreen = floating.PointToScreen(local), floating.Left, floating.Top,
                        servicePointFromAppliedPosition = new Point((floating.Left + offset.X) * observedScale.DpiScaleX, (floating.Top + offset.Y) * observedScale.DpiScaleY),
                        currentService = ReferenceEquals(initialService, service), servicePresent = service is not null,
                        host = service is null ? null : Field(service, "_currentHost")?.GetType().FullName,
                        nativeFocus = native.FocusedWindow.ToInt64(), floating.IsActive, captured = Mouse.Captured == floating });
                };
                InputManager.Current.PostProcessInput += processedMoveObserver;
            }
            var center = manager.PointToScreen(new Point(manager.ActualWidth / 2, manager.ActualHeight / 2));
            native.MovePointer((int)Math.Round(center.X), (int)Math.Round(center.Y));
            PointerWait(() => VisibleOverlays().Any(), "The original DragService did not show its genuine overlay.");
            var overlay = VisibleOverlays().Single();
            Require(overlay.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue && overlay.Template != null,
                "The overlay was given a substitute local template.");
            string partName = "PART_DockingManagerDropTarget" + direction;
            var target = (FrameworkElement?)overlay.Template.FindName(partName, overlay);
            Require(target != null && target.IsVisible && target.ActualWidth > 0 && target.ActualHeight > 0,
                "The original overlay target is missing or has zero geometry: " + partName);
            var targetPoint = target!.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
            native.MovePointer((int)Math.Round(targetPoint.X), (int)Math.Round(targetPoint.Y));
            try
            {
                PointerWait(() => Field(floating, "_dragService") is object service && Field(service, "_currentDropTarget") is object drop
                    && drop.GetType().GetProperty("Type")!.GetValue(drop)!.ToString() == "DockingManagerDock" + direction,
                    "The actual held pointer did not select the visible original drop target.");
            }
            finally
            {
                var service = Field(floating, "_dragService");
                var drop = service is null ? null : Field(service, "_currentDropTarget");
                var pointer = native.PointerPosition;
                Observations.Add(new { stage = "native-target-selection", pane.ContentId, direction, targetPoint,
                    pointerX = pointer.X, pointerY = pointer.Y, target.ActualWidth, target.ActualHeight,
                    host = service is null ? null : Field(service, "_currentHost")?.GetType().FullName,
                    selected = drop?.GetType().GetProperty("Type")?.GetValue(drop)?.ToString(),
                    overlayXid = PresentationSource.FromVisual(overlay) is null ? 0 : NativeHandle(overlay).ToInt64(),
                    floatingXid = PresentationSource.FromVisual(floating) is null ? 0 : NativeHandle(floating).ToInt64(),
                    nativeFocus = native.FocusedWindow.ToInt64(), capture = Mouse.Captured?.GetType().FullName, floating.IsActive,
                    servicePresent = service is not null,
                    modelContents = manager.Layout.Descendents().OfType<LayoutAnchorable>().Select(model => new { model.ContentId, model.IsFloating }).ToArray() });
            }
            var overlayXid = NativeHandle(overlay);
            var floatingXid = NativeHandle(floating);
            var rootOrder = native.RootChildren.ToList();
            Require(rootOrder.IndexOf(native.StackingAncestor(overlayXid)) > rootOrder.IndexOf(native.StackingAncestor(floatingXid)),
                "The visible native overlay is below the dragged window.");
            var clientPoint = overlay.PointFromScreen(targetPoint);
            var scale = VisualTreeHelper.GetDpi(overlay);
            int x = (int)Math.Round(clientPoint.X * scale.DpiScaleX), y = (int)Math.Round(clientPoint.Y * scale.DpiScaleY);
            bool ink = false;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++) ink |= (native.Pixel(overlayXid, x + dx, y + dy, true) >> 24) != 0;
            Require(ink, "The visible original target has no painted native alpha pixels.");
            var physical = native.PointerPosition;
            Require(Math.Abs(physical.X - targetPoint.X) <= 1 && Math.Abs(physical.Y - targetPoint.Y) <= 1,
                "The physical pointer did not reach the original target at the measured DPI.");
            Require(Mouse.Captured == floating && Mouse.LeftButton == MouseButtonState.Pressed,
                "Overlay selection changed the drag capture or held state.");
            Observations.Add(new { stage = "visible-original-target", pane.ContentId, direction, targetPoint, physicalX = physical.X, physicalY = physical.Y,
                target.ActualWidth, target.ActualHeight, nativeFocus = native.FocusedWindow.ToInt64(), overlayXid = overlayXid.ToInt64(),
                floatingXid = floatingXid.ToInt64(), logicalScreen = floating.PointToScreen(Mouse.GetPosition(floating)) });
            var releaseEvents = new List<object>();
            void ObserveRelease(object sender, PreProcessInputEventArgs args)
            {
                if (releaseEvents.Count >= 20 || args.StagingItem.Input is not MouseEventArgs mouse
                    || (mouse.RoutedEvent != Mouse.PreviewMouseMoveEvent && mouse.RoutedEvent != Mouse.PreviewMouseUpEvent)) return;
                Point? screen = PresentationSource.FromVisual(floating) is null ? null : floating.PointToScreen(mouse.GetPosition(floating));
                var physical = native.PointerPosition;
                releaseEvents.Add(new { routedEvent = mouse.RoutedEvent.Name, mouse.Timestamp, left = mouse.LeftButton.ToString(),
                    servicePresent = Field(floating, "_dragService") is not null, screen, physicalX = physical.X, physicalY = physical.Y,
                    capture = Mouse.Captured?.GetType().FullName });
            }
            InputManager.Current.PreProcessInput += ObserveRelease;
            try
            {
                native.Button(1, false);
                PointerWait(() => !pane.IsFloating && !manager.FloatingWindows.Contains(floating) && !VisibleOverlays().Any(),
                    "Native release did not execute the original drop/model/overlay cleanup.");
            }
            finally
            {
                InputManager.Current.PreProcessInput -= ObserveRelease;
                Observations.Add(new { stage = "native-release-state", events = releaseEvents, pane.IsFloating,
                    floatingWindows = manager.FloatingWindows.Count(), overlays = VisibleOverlays().Count(),
                    servicePresent = Field(floating, "_dragService") is not null, sourcePresent = PresentationSource.FromVisual(floating) is not null,
                    capture = Mouse.Captured?.GetType().FullName, left = Mouse.LeftButton.ToString() });
            }
            Require(ReferenceEquals(content, pane.Content) && PresentationSource.FromVisual(floating) == null && Mouse.Captured == null,
                "A completed pointer drop replaced original content or retained the old source/capture.");
            Require(!native.IsMapped(floatingXid) && native.CanAcquirePointerGrab(), "A completed drop retained a native source or server capture.");
            Require(manager.FloatingWindows.Count() == otherWindows.Length && otherWindows.All(other => manager.FloatingWindows.Contains(other.Window)
                && ReferenceEquals(other.Window.Model, other.Model) && ReferenceEquals(PresentationSource.FromVisual(other.Window), other.Source)
                && NativeHandle(other.Window) == other.Xid && native.IsMapped(other.Xid)),
                "Dropping the dragged pane changed another floating model, Window, source or live native handle.");
            Require(ReferenceEquals(pane.Parent, draggedPane) && draggedPane.Children.SequenceEqual(draggedContents)
                && draggedContents.All(model => ReferenceEquals(model.Content, contentIdentities[model])),
                "The original drop changed dragged pane/content identities or intra-pane tab order.");
            var ordered = manager.Layout.RootPanel.Descendents().OfType<LayoutAnchorablePane>().ToArray();
            Require(ReferenceEquals(direction == "Right" ? ordered.Last() : ordered.First(), draggedPane),
                "The original drop model did not move the complete dragged pane to the selected opposite outer edge.");
            Observations.Add(new { stage = "specific-drop-preserved-other-windows", pane.ContentId, direction,
                contents = draggedContents.Select(model => model.ContentId).ToArray(), otherXids = otherWindows.Select(other => other.Xid.ToInt64()).ToArray() });
        }
        finally
        {
            if (processedMoveObserver is not null) InputManager.Current.PostProcessInput -= processedMoveObserver;
            if (rawInput is not null) rawInput.RemoveEventHandler(tracedPeer, rawObserver);
            if (heldMoves.Count != 0) Observations.Add(new { stage = "focused-recovery-held-moves", moves = heldMoves.ToArray() });
            native.Button(1, false);
            if (floating != null && Mouse.Captured == floating) Mouse.Capture(null);
        }
    }

    private static void OverlappingHosts(X11Fixture native, Window owner, DockingManager manager, LayoutAnchorable image, LayoutAnchorable graph)
    {
        LayoutFloatingWindowControl? dragged = null;
        try
        {
            graph.Float(); Settle();
            var target = manager.FloatingWindows.Single();
            target.OwnedByDockingManagerWindow = false;
            target.Left = owner.Left + owner.Width * .2; target.Top = owner.Top + owner.Height * .2;
            target.Width = owner.Width * .6; target.Height = owner.Height * .6; Settle();
            dragged = BeginPointerFloat(native, manager, image);
            PointerWait(() => native.FocusedWindow == NativeHandle(dragged), "The dragged source did not complete native activation.");
            var focus = native.FocusedWindow;
            var overlap = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
            foreach (var front in new Window[] { target, owner })
            {
                FrameworkWindowOperation("RaiseWithoutActivation", front);
                var back = ReferenceEquals(front, target) ? owner : target;
                PointerWait(() => native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(front)))
                    > native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(back))),
                    "The overlapping hosts did not achieve the requested actual native order.");
                // Leaving the host refreshes the original DragService's native host snapshot.
                native.MovePointer(3, 3);
                PointerWait(() => !VisibleOverlays().Any(), "Leaving overlapping hosts retained a drag overlay.");
                native.MovePointer((int)Math.Round(overlap.X), (int)Math.Round(overlap.Y));
                object expected = ReferenceEquals(front, target) ? target : manager;
                PointerWait(() => Field(dragged, "_dragService") is object service && ReferenceEquals(Field(service, "_currentHost"), expected)
                    && VisibleOverlays().Any(), "Original DragService selected the wrong overlapping native host.");
                Require(Mouse.Captured == dragged && native.FocusedWindow == focus && !native.CanAcquirePointerGrab(),
                    "Changing the native drop host changed focus or capture.");
                Observations.Add(new { stage = "overlapping-original-host", front = front.GetType().FullName,
                    selectedHost = expected.GetType().FullName, overlap, nativeFocus = focus.ToInt64() });
            }
            var rootOnly = manager.PointToScreen(new Point(10, 10));
            manager.Visibility = Visibility.Hidden;
            MoveHeldPointerAndWait(native, dragged, new Point(3, 3));
            PointerWait(() => !VisibleOverlays().Any(), "Leaving the hidden manager retained an overlay.");
            MoveHeldPointerAndWait(native, dragged, rootOnly);
            PointerWait(() => Field(dragged, "_dragService") is object hiddenService && Field(hiddenService, "_currentHost") == null
                && !VisibleOverlays().Any(), "A hidden manager was inserted as a drop host.");
            manager.Visibility = Visibility.Visible; Settle();
            FrameworkWindowOperation("RaiseWithoutActivation", target);
            PointerWait(() => native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(target)))
                > native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(owner))),
                "The visibility recovery did not establish actual target-above-owner native ordering.");
            Require(native.FocusedWindow == focus && Mouse.Captured == dragged,
                "Restoring visibility recovery order changed focus or capture.");
            MoveHeldPointerAndWait(native, dragged, new Point(3, 3));
            native.MovePointer((int)Math.Round(overlap.X), (int)Math.Round(overlap.Y));
            PointerWait(() => Field(dragged, "_dragService") is object targetService && ReferenceEquals(Field(targetService, "_currentHost"), target),
                "Restoring manager visibility prevented the next floating-host transition.");
            dragged.OwnedByDockingManagerWindow = false;
            native.Unmap(NativeHandle(owner)); Settle();
            Require(manager.IsVisible, "The native-parent exclusion needs a logically visible manager.");
            MoveHeldPointerAndWait(native, dragged, new Point(3, 3));
            PointerWait(() => !VisibleOverlays().Any(), "Leaving the natively unmapped parent retained an overlay.");
            MoveHeldPointerAndWait(native, dragged, rootOnly);
            PointerWait(() => Field(dragged, "_dragService") is object unmappedService && Field(unmappedService, "_currentHost") == null
                && !VisibleOverlays().Any(), "A manager whose parent has no native rank was inserted as a drop host.");
            owner.Hide(); owner.Show(); Settle();
            PointerWait(() => native.IsMapped(NativeHandle(owner)), "The native parent did not remap on public Hide/Show.");
            Observations.Add(new { stage = "remapped-overlap-native-order", ownerXid = NativeHandle(owner).ToInt64(), targetXid = NativeHandle(target).ToInt64(),
                draggedXid = NativeHandle(dragged).ToInt64(), roots = native.RootChildren.Select(xid => xid.ToInt64()).ToArray(),
                ownerFrame = native.StackingAncestor(NativeHandle(owner)).ToInt64(), targetFrame = native.StackingAncestor(NativeHandle(target)).ToInt64(),
                focused = native.FocusedWindow.ToInt64(), capture = Mouse.Captured?.GetType().FullName });
            FrameworkWindowOperation("RaiseWithoutActivation", target);
            PointerWait(() => native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(target)))
                > native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(owner))),
                "The remapped overlap fixture did not restore actual target-above-owner ordering.");
            Require(native.FocusedWindow == focus && Mouse.Captured == dragged, "Restoring overlap order changed focus or capture.");
            MoveHeldPointerAndWait(native, dragged, new Point(3, 3));
            native.MovePointer((int)Math.Round(overlap.X), (int)Math.Round(overlap.Y));
            PointerWait(() => Field(dragged, "_dragService") is object renewedService && ReferenceEquals(Field(renewedService, "_currentHost"), target),
                "Remapping the native parent prevented floating-host recovery.");
            native.MovePointer(3, 3);
            PointerWait(() => !VisibleOverlays().Any(), "Recovery did not leave its floating host.");
            native.MovePointer((int)Math.Round(rootOnly.X), (int)Math.Round(rootOnly.Y));
            PointerWait(() => Field(dragged, "_dragService") is object restoredService && ReferenceEquals(Field(restoredService, "_currentHost"), manager),
                "Restoring both manager eligibility conditions did not restore the original root target.");
            Require(Mouse.Captured == dragged && native.FocusedWindow == focus, "Manager eligibility transitions changed drag focus or capture.");
            native.MovePointer(3, 3); Settle(); native.Button(1, false);
            PointerWait(() => Mouse.Captured != dragged && !VisibleOverlays().Any(), "The overlap gesture did not clean up on outside release.");
            PointerDrop(native, manager, image, "Right");
            graph.Dock(); Settle();
            Require(!image.IsFloating && !graph.IsFloating && !manager.FloatingWindows.Any(),
                "The overlap fixture did not restore its docked precondition through native drop and original Dock.");
        }
        finally
        {
            native.Button(1, false);
            if (Mouse.Captured == dragged) Mouse.Capture(null);
            manager.Visibility = Visibility.Visible;
            if (!native.IsMapped(NativeHandle(owner))) { owner.Hide(); owner.Show(); }
            if (image.IsFloating) image.Dock();
            if (graph.IsFloating) graph.Dock();
            Settle();
        }
    }

    private static void MoveHeldPointerAndWait(X11Fixture native, Window captured, Point screenPoint)
    {
        int x = (int)Math.Round(screenPoint.X), y = (int)Math.Round(screenPoint.Y);
        bool observed = false;
        int timestamp = 0;
        void Observe(object sender, PreProcessInputEventArgs args)
        {
            if (args.StagingItem.Input is not MouseEventArgs mouse || mouse.RoutedEvent != Mouse.PreviewMouseMoveEvent
                || Mouse.Captured != captured || mouse.LeftButton != MouseButtonState.Pressed) return;
            // Sample before the floating control moves itself in OnPreviewMouseMove.
            var physical = captured.PointToScreen(mouse.GetPosition(captured));
            if (Math.Abs(physical.X - x) <= 1 && Math.Abs(physical.Y - y) <= 1)
            {
                observed = true;
                timestamp = mouse.Timestamp;
            }
        }
        InputManager.Current.PreProcessInput += Observe;
        try
        {
            native.MovePointer(x, y);
            PointerWait(() => observed, "WPF did not process the held pointer motion to " + x + "," + y + ".");
        }
        finally { InputManager.Current.PreProcessInput -= Observe; }
        Observations.Add(new { stage = "processed-held-pointer-motion", x, y, timestamp });
    }

    private static DropDownControlArea FloatingCaption(LayoutFloatingWindowControl window)
    {
        var chrome = Microsoft.Windows.Shell.WindowChrome.GetWindowChrome(window);
        Require(chrome is not null && chrome.CaptionHeight > 0, "The original floating style has no attached caption geometry.");
        return VisualDescendants(window).OfType<DropDownControlArea>().Single(area =>
        {
            if (!area.IsVisible || area.ActualWidth <= 0 || area.ActualHeight <= 0) return false;
            var point = area.TranslatePoint(new Point(Math.Min(24, area.ActualWidth / 2), area.ActualHeight / 2), window);
            return point.Y >= chrome!.ResizeBorderThickness.Top && point.Y < chrome.ResizeBorderThickness.Top + chrome.CaptionHeight;
        });
    }

    private static void CaptionExclusions(X11Fixture native, DockingManager manager, LayoutAnchorable pane, bool genericCaption = false)
    {
        pane.Float(); Settle();
        var floating = manager.FloatingWindows.Single(window => ReferenceEquals(window.Model, pane.FindParent<LayoutFloatingWindow>()));
        if (genericCaption)
        {
            var resources = new ResourceDictionary { Source = new Uri("/AvalonDock;component/Themes/generic.xaml", UriKind.Relative) };
            floating.Style = (Style)resources[typeof(LayoutAnchorableFloatingWindowControl)];
            Settle();
        }
        var button = (Button)floating.Template.FindName(genericCaption ? "CloseButton" : "PART_HidePin", floating)!;
        Require(button is not null, "The original floating template has no caption Button.");
        var binding = BindingOperations.GetBindingBase(button!, Button.CommandProperty);
        var local = button!.ReadLocalValue(Button.CommandProperty);
        var command = new RoutedCommand("CaptionExclusionProbe", typeof(DockingProgram));
        int executed = 0;
        var commandBinding = new CommandBinding(command, (_, args) => { executed++; args.Handled = true; });
        floating.CommandBindings.Add(commandBinding);
        button.Command = command;
        void Press(Point physical, uint number)
        {
            native.MovePointer((int)Math.Round(physical.X), (int)Math.Round(physical.Y)); Settle();
            native.Button(number, true);
            PointerWait(() => (number == 1 ? Mouse.LeftButton : Mouse.RightButton) == MouseButtonState.Pressed,
                "The caption exclusion press was not processed by WPF.");
        }
        void Release(uint number)
        {
            native.Button(number, false);
            PointerWait(() => (number == 1 ? Mouse.LeftButton : Mouse.RightButton) == MouseButtonState.Released,
                "The caption exclusion release was not processed by WPF.");
        }
        try
        {
            Settle();
            Require(button.IsVisible && button.IsEnabled, "The genuine floating caption button is not actionable with its routed probe command.");
            var buttonPoint = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            Observations.Add(new { stage = "original-caption-button", genericCaption, button.Name, buttonPoint, button.ActualWidth, button.ActualHeight,
                localPoint = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), floating),
                chromeHit = Microsoft.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(button) });
            if (genericCaption)
            {
                var genericChrome = Microsoft.Windows.Shell.WindowChrome.GetWindowChrome(floating);
                var localPoint = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), floating);
                Require(localPoint.Y >= genericChrome.ResizeBorderThickness.Top && localPoint.Y < genericChrome.ResizeBorderThickness.Top + genericChrome.CaptionHeight,
                    "The generic chrome Button check does not overlap the actual caption band.");
            }
            Press(buttonPoint, 1);
            Require(Field(floating, "_dragService") is null && Mouse.Captured != floating,
                "An actionable original caption button began a docking drag.");
            Release(1); PointerWait(() => executed == 1, "The original caption Button did not execute its genuine routed command.");
            if (genericCaption)
            {
                Require(Microsoft.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(button), "The original generic Button lost its inherited chrome hit-test metadata.");
                return;
            }
            var caption = FloatingCaption(floating);
            var captionPoint = caption.PointToScreen(new Point(Math.Min(24, caption.ActualWidth / 2), caption.ActualHeight / 2));
            var counts = new List<int>();
            MouseButtonEventHandler observe = (_, mouse) =>
            {
                if (mouse.ChangedButton == MouseButton.Left) counts.Add(mouse.ClickCount);
            };
            floating.AddHandler(Mouse.PreviewMouseDownEvent, observe, true);
            try
            {
                Press(captionPoint, 1);
                PointerWait(() => Field(floating, "_dragService") is not null && Mouse.Captured == floating,
                    "The first real caption click did not establish a genuine drag.");
                Release(1); PointerWait(() => Field(floating, "_dragService") is null && Mouse.Captured != floating,
                    "The first caption click retained drag state after its native release.");
                Press(captionPoint, 1);
                Observations.Add(new { stage = "native-caption-second-press", counts = counts.ToArray(), servicePresent = Field(floating, "_dragService") is not null, capture = Mouse.Captured?.GetType().FullName });
                Require(counts.SequenceEqual(new[] { 1, 2 }) && Field(floating, "_dragService") is null && Mouse.Captured != floating,
                    "The native caption double-click was treated as an ordinary docking drag.");
                Release(1);
            }
            finally { floating.RemoveHandler(Mouse.PreviewMouseDownEvent, observe); }
            var chrome = Microsoft.Windows.Shell.WindowChrome.GetWindowChrome(floating);
            var edge = floating.PointToScreen(new Point(chrome.ResizeBorderThickness.Left / 2, chrome.ResizeBorderThickness.Top / 2));
            Press(edge, 1);
            Require(Field(floating, "_dragService") is null && Mouse.Captured != floating, "A resize-edge press began a caption drag.");
            Release(1);
            Press(captionPoint, 3);
            Require(Field(floating, "_dragService") is null && Mouse.Captured != floating, "A right-button caption press began a docking drag.");
            Release(3);
            if (caption.DropDownContextMenu is { IsOpen: true } menu) { menu.IsOpen = false; Settle(); }
            Observations.Add(new { stage = "native-caption-exclusions", xid = NativeHandle(floating).ToInt64(), buttonPoint,
                captionPoint, edge, executed, nativeClickCounts = counts.ToArray() });
            PointerDrop(native, manager, pane, "Right");
        }
        finally
        {
            native.Button(1, false); native.Button(3, false); Mouse.Capture(null);
            floating.CommandBindings.Remove(commandBinding);
            if (binding is not null) BindingOperations.SetBinding(button, Button.CommandProperty, binding);
            else if (local == DependencyProperty.UnsetValue) button.ClearValue(Button.CommandProperty);
            else button.SetValue(Button.CommandProperty, local);
            if (pane.IsFloating) pane.Dock();
            Settle();
        }
    }

    private static void NativeStacking(X11Fixture native)
    {
        var first = new Window { Title = "Docking native stack first", Width = 240, Height = 180, Left = 60, Top = 80, ShowActivated = false, Content = new Border { Background = Brushes.Red } };
        var second = new Window { Title = "Docking native stack second", Width = 240, Height = 180, Left = 80, Top = 100, ShowActivated = false, Content = new Border { Background = Brushes.Blue } };
        try
        {
            first.Show(); second.Show(); Settle();
            var firstXid = NativeHandle(first); var secondXid = NativeHandle(second);
            PointerWait(() => native.IsMapped(firstXid) && native.IsMapped(secondXid), "The overlapping native windows did not become viewable.");
            native.Focus(firstXid); PointerWait(() => first.IsActive, "The stacking focus sentinel was not activated.");
            native.MovePointer(firstXid, 12, 12); native.Button(1, true);
            PointerWait(() => Mouse.LeftButton == MouseButtonState.Pressed, "The real stacking press did not pass through the WM to WPF.");
            Require(first.CaptureMouse(), "Cannot acquire genuine native capture for nonactivation testing.");
            var focus = native.FocusedWindow;
            foreach (var raised in new[] { first, second, first, second })
            {
                FrameworkWindowOperation("RaiseWithoutActivation", raised);
                var raisedXid = NativeHandle(raised);
                PointerWait(() => native.RootChildren.ToList().IndexOf(native.StackingAncestor(raisedXid))
                    > native.RootChildren.ToList().IndexOf(native.StackingAncestor(NativeHandle(raised == first ? second : first))),
                    "The requested native raise did not change actual WM frame ordering.");
                var ordered = FrameworkOrder(first, second);
                Require(ordered.Count == 2 && ReferenceEquals(ordered[0], raised), "The private bridge disagrees with actual native stacking order.");
                Require(native.FocusedWindow == focus && Mouse.Captured == first && !native.CanAcquirePointerGrab(),
                    "A nonactivating raise changed focus or pointer capture.");
            }
            second.Hide(); Settle();
            Require(FrameworkOrder(first, second).SequenceEqual(new[] { first }), "A hidden native source retained a stacking rank.");
            FrameworkWindowOperation("RaiseWithoutActivation", second);
            second.Show(); Settle();
            Require(FrameworkOrder(first, second).Count == 2, "Showing a source did not restore its actual native stacking rank.");
            second.Close(); Settle();
            Require(FrameworkOrder(first, second).SequenceEqual(new[] { first }), "A closed native source retained a stacking rank.");
            FrameworkWindowOperation("RaiseWithoutActivation", second);
            Require(native.FocusedWindow == focus && Mouse.Captured == first, "Hidden/closed-source handling changed focus or capture.");
            Observations.Add(new { stage = "native-order-both-directions", native.HasWindowManager,
                firstXid = firstXid.ToInt64(), secondXid = secondXid.ToInt64(), nativeFocus = focus.ToInt64() });
        }
        finally { native.Button(1, false); Mouse.Capture(null); second.Close(); first.Close(); }
    }

    private static IReadOnlyList<Window> FrameworkOrder(params Window[] windows) => (IReadOnlyList<Window>)FrameworkWindowOperation("GetWindowsByZOrder", windows)!;
    private static object? FrameworkWindowOperation(string name, object argument)
    {
        var platform = typeof(Window).Assembly.GetType("MS.Internal.Portability.Windowing.PortableWindowPlatform", true)!;
        try { return platform.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { argument }); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static int DragInputHandlers(Window window)
    {
        var handlers = Field(InputManager.Current, "_preProcessInput");
        if (handlers is null) return 0;
        return ((Delegate[])handlers.GetType().GetProperty("Item2")!.GetValue(handlers)!)
            .Count(handler => ReferenceEquals(handler.Target, window) && handler.Method.Name == "OnPortablePreProcessInput");
    }
    private static object? Field(object instance, string name)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is FieldInfo field) return field.GetValue(instance);
        throw new MissingFieldException(instance.GetType().FullName, name);
    }
    private static IntPtr NativeHandle(Window window)
    {
        var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("The genuine native source is disconnected.");
        var peer = Field(source, "_host")!;
        var nativeWindow = Field(peer, "_window")!;
        return unchecked((IntPtr)(nint)(nuint)nativeWindow.GetType().GetProperty("Handle")!.GetValue(nativeWindow)!);
    }
    private static IEnumerable<OverlayWindow> VisibleOverlays() => Application.Current.Windows.OfType<OverlayWindow>().Where(window => window.IsVisible);
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
    private static void PointerWait(Func<bool> condition, string failure)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidDataException(failure);
            Settle();
            Thread.Sleep(5);
        }
    }
#endif
}
