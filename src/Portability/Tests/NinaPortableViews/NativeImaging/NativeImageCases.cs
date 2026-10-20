using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.WPF.Base.Mediator;
using NINA.WPF.Base.View;

internal static partial class Program
{
    private static readonly List<string> NativeCompleted = [];
    private static string NativeFormat => Environment.GetEnvironmentVariable("NINA_NATIVE_IMAGE_FORMAT") ?? "UNSPECIFIED";
    private static readonly string[] NativeOperations = ["mapped-original-window-and-attachment", "caller-owned-full-source-and-native-body",
        "native-fit", "native-fit-repeat", "native-one-to-one", "native-toolbar-zoom-in", "native-toolbar-zoom-out",
        "native-wheel-in", "native-wheel-out", "native-fit-minimum-clamp", "native-left-pan-forward-and-release", "native-left-pan-reverse-and-release",
        "native-fit-before-paired-controls", "native-rotate-1", "native-rotate-2", "native-rotate-3", "native-rotate-4",
        "native-flip-True", "native-flip-False", "native-crosshair-True", "native-crosshair-False"];
    private static string[] NativeRequiredChecks(string format) => new[] { "unchanged-managed-closure", "explicit-runtime-identity", "isolated-original-profile-paths",
        "original-33-resource-dictionaries", "actual-isolated-profile", "actual-ImageControlVM-public-constructor-original-mediators", "original-ImageControlView-and-ImageView-BAML" }
        .Concat(NativeOperations.Select(name => format + "/" + name))
        .Concat(Enumerable.Range(0, 2).Select(index => format + "/warmup-frame-" + index))
        .Concat(Enumerable.Range(0, 16).Select(index => format + "/measured-frame-" + index))
        .Concat(new[] { format + "/public-image-removal", format + "/actual-window-close-and-original-VM-disposal" }).ToArray();

    private static void RunOriginalNativeImaging()
    {
        Require(NativeFormat is "Gray16" or "Rgb48", "Explicit Gray16 or Rgb48 process required.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Check("original-33-resource-dictionaries", () =>
        {
            var sources = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ResourceSources.json")))!;
            Require(sources.Length == 33, "Original dictionary count differs.");
            foreach (var source in sources) { app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) }); resources.Add(source); }
        });
        Check("actual-isolated-profile", () =>
        {
            Directory.CreateDirectory(NINA.Profile.ProfileService.PROFILEFOLDER);
            Require(!Directory.EnumerateFileSystemEntries(NINA.Profile.ProfileService.PROFILEFOLDER).Any(), "Fresh owned profile state required.");
            profile = (NINA.Profile.ProfileService)app.Resources["ProfileService"];
            Require(profile.TryLoad(string.Empty) && profile.Profiles.Count == 1, "Original isolated profile failed.");
            profile.ActiveProfile.ImageSettings.AutoStretch = false;
            profile.ActiveProfile.ImageSettings.DetectStars = false;
        });
        IImageControlVM? vm = null;
        Check("actual-ImageControlVM-public-constructor-original-mediators", () =>
        {
            var type = typeof(NINA.View.ImageControlView).Assembly.GetType("NINA.ViewModel.ImageControlVM", true)!;
            vm = (IImageControlVM)Activator.CreateInstance(type, profile!, new CameraMediator(), new TelescopeMediator(), new ImagingMediator(), new ApplicationStatusMediator())!;
            Require(vm.GetType() == type && vm.Image is null && vm.RenderedImage is null && vm.ImageRotation == 0 && !vm.ShowCrossHair, "Original VM initial state differs.");
        });
        NINA.View.ImageControlView? view = null;
        Check("original-ImageControlView-and-ImageView-BAML", () =>
        {
            ObserveBaml(typeof(NINA.View.ImageControlView), "NINA.g.resources", "view/imagecontrolview.baml");
            ObserveBaml(typeof(ImageView), "NINA.WPF.Base.g.resources", "view/imageview.baml");
            view = new NINA.View.ImageControlView { DataContext = vm };
        });
        phase = NativeFormat + "/mapped-original-window-and-attachment";
        using var session = new NativeImageSession(app, vm!, view!);
        Exception? failure = null;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await session.Run(); }
            catch (Exception exception) { failure = exception; }
            finally
            {
                try { session.ReleasePendingInput(); if (session.Window.IsVisible) session.Window.Close(); }
                catch (Exception cleanup) { failure = failure == null ? cleanup : new AggregateException(failure, cleanup); }
                finally { app.Shutdown(); }
            }
        }));
        session.Window.Show();
        app.Run();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class NativeImageSession : IDisposable
    {
        internal Window Window { get; }
        private readonly Application app;
        private readonly IImageControlVM vm;
        private readonly NINA.View.ImageControlView view;
        private readonly ImageView child;
        private readonly ScrollViewer scroll;
        private readonly Canvas canvas;
        private readonly Image image;
        private readonly ScaleTransform scale;
        private readonly Button[] toolbar;
        private readonly Button rotate;
        private readonly ToggleButton flip, crosshair;
        private readonly System.Windows.Shapes.Path crosshairPath;
        private readonly BindingBase? flipBinding, crosshairBinding;
        private readonly int channels, stride;
        private readonly ushort[] readback;
        private BitmapSource? source;
        private IntPtr xid;
        private long eventSequence, downSequence, upSequence, moveSequence, wheelSequence;
        private int closed;
        private bool disposed, streamPanHeld;
        private Point lastDownPoint;
        private string? baselineSignature;
        private List<(int X, int Y)> points = [];
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly List<object> heartbeats = [];
        private readonly DispatcherTimer heartbeat;
        private double lastHeartbeat;

        internal NativeImageSession(Application application, IImageControlVM actualVm, NINA.View.ImageControlView originalView)
        {
            app = application; vm = actualVm; view = originalView;
            channels = NativeFormat == "Rgb48" ? 3 : 1; stride = SourceWidth * channels * 2;
            readback = new ushort[SourceWidth * SourceHeight * channels];
            child = Descendants(view).OfType<ImageView>().Single();
            scroll = (ScrollViewer)child.FindName("PART_ScrollViewer"); canvas = (Canvas)child.FindName("PART_Canvas");
            image = (Image)child.FindName("PART_Image"); scale = (ScaleTransform)child.FindName("PART_ScaleTransform");
            toolbar = Enumerable.Range(0, 4).Select(column => Descendants(child).OfType<Button>()
                .Single(button => button.Parent is Grid grid && grid.ColumnDefinitions.Count == 7 && Grid.GetColumn(button) == column)).ToArray();
            var header = (DependencyObject)child.ButtonHeaderContent;
            rotate = Descendants(header).OfType<Button>().Single(button => BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path == "RotateImageCommand");
            flip = Descendants(header).OfType<ToggleButton>().Single(button => BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Path.Path == "ImageFlip");
            crosshair = Descendants(header).OfType<ToggleButton>().Single(button => BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Path.Path == "ShowCrossHair");
            crosshairPath = Descendants((DependencyObject)child.ImageAreaContent).OfType<System.Windows.Shapes.Path>()
                .Single(path => BindingOperations.GetBinding(path, UIElement.VisibilityProperty)?.Path.Path == "ShowCrossHair");
            flipBinding = BindingOperations.GetBindingBase(flip, ToggleButton.IsCheckedProperty); crosshairBinding = BindingOperations.GetBindingBase(crosshair, ToggleButton.IsCheckedProperty);
            Window = new Window { Title = "NINA native imaging " + NativeFormat + " " + Guid.NewGuid().ToString("N"), Width = ViewWidth, Height = ViewHeight, Left = 16, Top = 12, Content = view };
            Window.Closed += (_, _) => closed++;
            Window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, args) => RecordInput("down", args)), true);
            Window.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, args) => RecordInput("up", args)), true);
            Window.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler((_, args) => RecordInput("move", args)), true);
            Window.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, args) => RecordInput("wheel", args)), true);
            heartbeat = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) =>
            {
                double now = elapsed.Elapsed.TotalMilliseconds;
                heartbeats.Add(new { inputAtMs = now, gapMs = now - lastHeartbeat }); lastHeartbeat = now;
                double posted = now;
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => heartbeats.Add(new { backgroundPostedMs = posted, backgroundExecutedMs = elapsed.Elapsed.TotalMilliseconds })));
            }, app.Dispatcher);
        }

        internal async Task Run()
        {
            await Step("mapped-original-window-and-attachment", async () =>
            {
                await Wait(() => { xid = native!.FindWindow(Window.Title); return xid != IntPtr.Zero && native.IsMapped(xid) && Attached(); });
                Require(Window.Template != null && ReferenceEquals(Window.Content, view), "Original host template/content missing.");
                var dpi = VisualTreeHelper.GetDpi(Window); Require(dpi.DpiScaleX == 1 && dpi.DpiScaleY == 1, "This declared first native lane requires actual scale1.");
                native!.Focus(xid);
                var size = native.Size(xid); var origin = native.ClientOrigin(xid);
                observations.Add(new { stage = "native-attachment", title = Window.Title, xid = xid.ToInt64(), source = PresentationSource.FromVisual(view)?.GetType().FullName,
                    windowSource = PresentationSource.FromVisual(Window)?.GetType().FullName, template = Window.Template!.GetType().FullName, parents = ParentChain(view),
                    nativeWidth = size.Width, nativeHeight = size.Height, rootX = origin.X, rootY = origin.Y });
            });
            await Step("caller-owned-full-source-and-native-body", async () =>
            {
                await AssignFrame(0); await Wait(() => Attached() && image.ActualWidth == SourceWidth && image.ActualHeight == SourceHeight);
                Require(ReferenceEquals(view.DataContext, vm) && ReferenceEquals(child.DataContext, vm), "Original VM inheritance differs.");
                Require(ReferenceEquals(rotate.Command, vm.GetType().GetProperty("RotateImageCommand")!.GetValue(vm)) && rotate.Command!.CanExecute(rotate.CommandParameter), "Original rotate command identity/execution differs.");
                Require(child.ZoomInCommand is null && child.ZoomOutCommand is null, "Original toolbar fallback handler seam changed.");
                await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1);
                points = BodyPoints();
                baselineSignature = await Surface(null, "initial"); VerifySource(0); await PixelTransportParity("initial");
            });
            await Step("native-fit", async () => { await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1); ObserveGeometry("fit"); });
            await Step("native-fit-repeat", async () => { double before = scale.ScaleX; await Click(toolbar[2], () => scale.ScaleX == before); ObserveGeometry("fit-repeat"); });
            await Step("native-one-to-one", async () => { await Click(toolbar[3], () => scale.ScaleX == 1 && scale.ScaleY == 1); await Surface(baselineSignature, "one-to-one"); ObserveGeometry("one-to-one"); });
            await Step("native-toolbar-zoom-in", async () => { double before = scale.ScaleX; await Click(toolbar[0], () => scale.ScaleX > before); Require(RenderOptions.GetBitmapScalingMode(image) == BitmapScalingMode.NearestNeighbor, "Original above1 sampling request differs."); });
            await Step("native-toolbar-zoom-out", async () => { double before = scale.ScaleX; await Click(toolbar[1], () => scale.ScaleX < before); });
            await Step("native-wheel-in", async () => { await Click(toolbar[3], () => scale.ScaleX == 1); await Wheel(true); Require(RenderOptions.GetBitmapScalingMode(image) == BitmapScalingMode.NearestNeighbor, "Wheel did not request nearest above1."); });
            await Step("native-wheel-out", async () => { await Wheel(false); Require(RenderOptions.GetBitmapScalingMode(image) == BitmapScalingMode.HighQuality, "Original <=1 sampling request differs."); });
            await Step("native-fit-minimum-clamp", async () => { await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1); double fit = scale.ScaleX; await Wheel(false, clamp: true); Require(scale.ScaleX == fit, "Wheel escaped genuine fit minimum."); });
            PanState? forward = null;
            await Step("native-left-pan-forward-and-release", async () => { await Click(toolbar[3], () => scale.ScaleX == 1); forward = await PanForward(false); });
            await Step("native-left-pan-reverse-and-release", async () => await PanReverse(forward!, false));
            await Step("native-fit-before-paired-controls", async () => { await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1); baselineSignature = await Surface(null, "paired-baseline"); });
            for (int turn = 1; turn <= 4; turn++)
            {
                int expected = turn * 90 % 360;
                await Step("native-rotate-" + turn, async () => { await Click(rotate, () => vm.ImageRotation == expected && child.ImageRotation == expected); VerifySource(0); await Surface(expected == 0 ? null : baselineSignature, "rotate-" + expected, expected == 0 ? baselineSignature : null); });
            }
            foreach (bool enabled in new[] { true, false })
                await Step("native-flip-" + enabled, async () => { await Flip(enabled); VerifySource(0); await Surface(enabled ? baselineSignature : null, "flip-" + enabled, enabled ? null : baselineSignature); });
            foreach (bool enabled in new[] { true, false })
                await Step("native-crosshair-" + enabled, async () => { await Crosshair(enabled); VerifySource(0); await Surface(enabled ? baselineSignature : null, "crosshair-" + enabled, enabled ? null : baselineSignature); await PixelTransportParity("crosshair-" + enabled); });
            for (int index = 0; index < 2; index++)
            {
                int phaseIndex = index + 1;
                await Step("warmup-frame-" + index, async () => { string previous = await Surface(null, "warmup-before-" + phaseIndex); await AssignFrame(phaseIndex); await Surface(previous, "warmup-" + phaseIndex); VerifySource(phaseIndex); });
                await Task.Delay(1000);
            }
            var workload = Stopwatch.StartNew();
            for (int index = 0; index < 16; index++)
            {
                int frameIndex = index, phaseIndex = index + 3;
                await Step("measured-frame-" + index, async () =>
                {
                    using var process = Process.GetCurrentProcess(); double cpuBefore = process.TotalProcessorTime.TotalMilliseconds;
                    long allocatedBefore = GC.GetTotalAllocatedBytes(false); int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                    var preparation = Stopwatch.StartNew(); ushort[] data = await Prepare(phaseIndex); preparation.Stop();
                    double due = frameIndex * 1000d, remaining = due - workload.Elapsed.TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining));
                    double began = workload.Elapsed.TotalMilliseconds;
                    string previous = await Surface(null, "frame-before-" + frameIndex);
                    var update = Stopwatch.StartNew(); Assign(data); double assigned = update.Elapsed.TotalMilliseconds;
                    string visible = await Surface(previous, "measured-" + frameIndex); double nativeObserved = workload.Elapsed.TotalMilliseconds;
                    var verification = Stopwatch.StartNew(); string hash = VerifySource(phaseIndex); verification.Stop();
                    await StreamInput(frameIndex);
                    double end = workload.Elapsed.TotalMilliseconds;
                    observations.Add(new { stage = "full-frame-workload", format = NativeFormat, frameIndex, phaseIndex, dueMs = due, beganMs = began, startLatenessMs = began - due,
                        preparationMs = preparation.Elapsed.TotalMilliseconds, copyFreezeAndSetterMs = assigned, nativeObservedMs = nativeObserved, sourceVerificationMs = verification.Elapsed.TotalMilliseconds,
                        transactionCompletedMs = end, sourceHash = hash, nativeSignature = visible, bytes = (long)SourceWidth * SourceHeight * channels * 2, fullFrame = true,
                        processCpuMs = process.TotalProcessorTime.TotalMilliseconds - cpuBefore, allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore,
                        gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1, gen2 = GC.CollectionCount(2) - gen2,
                        verificationOverheadSeparate = true, extraPerformanceBudget = "UNSET", monitorPresentationClaim = false,
                        timingDeadlineMs = due + 1000d, nativeDeadlineMissed = nativeObserved >= due + 1000d,
                        transactionDeadlineMissed = end >= due + 1000d, timingDeadlinePolicy = "reported-only-user-accepted" });
                });
            }
            await Step("public-image-removal", async () => { vm.Image = null; await Wait(() => child.Image is null && image.Source is null); source = null; });
            await Step("actual-window-close-and-original-VM-disposal", async () =>
            {
                Require(!streamPanHeld && Mouse.Captured is null && Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released, "Native input/capture retained.");
                Window.Close(); await Wait(() => closed == 1 && PresentationSource.FromVisual(Window) is null && !native!.IsMapped(xid));
                vm.Dispose(); disposed = true;
                profile!.Release();
                foreach (var path in Directory.EnumerateFiles(NINA.Profile.ProfileService.PROFILEFOLDER, "*.profile")) using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            });
        }

        private async Task Step(string name, Func<Task> action)
        {
            phase = NativeFormat + "/" + name; await action(); checks.Add(new { name = phase, status = "pass" }); NativeCompleted.Add(phase);
        }
        private bool Attached() => PresentationSource.FromVisual(view) != null && ReferenceEquals(PresentationSource.FromVisual(view), PresentationSource.FromVisual(Window)) &&
            view.IsMeasureValid && view.IsArrangeValid && child.IsMeasureValid && child.IsArrangeValid && view.ActualWidth > 0 && child.ActualHeight > 0;
        private async Task Wait(Func<bool> predicate)
        {
            var clock = Stopwatch.StartNew(); while (!predicate() && clock.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10);
            Require(predicate(), "Actual native/control condition did not complete within3s during " + phase);
        }
        private void RecordInput(string kind, MouseEventArgs args)
        {
            long sequence = ++eventSequence;
            if (kind == "down") downSequence = sequence; if (kind == "up") upSequence = sequence; if (kind == "move") moveSequence = sequence; if (kind == "wheel") wheelSequence = sequence;
            Point point = args.GetPosition(Window);
            if (kind == "down") lastDownPoint = point;
            observations.Add(new { stage = "native-routed-input", kind, sequence, elapsedMs = elapsed.Elapsed.TotalMilliseconds, point.X, point.Y,
                originalSource = args.OriginalSource?.GetType().FullName, captured = Mouse.Captured?.GetType().FullName, left = Mouse.LeftButton.ToString(), right = Mouse.RightButton.ToString(), wheel = (args as MouseWheelEventArgs)?.Delta });
        }
        private Point Client(FrameworkElement element, Point local)
        {
            Point point = element.TranslatePoint(local, Window); var dpi = VisualTreeHelper.GetDpi(Window);
            return new Point(Math.Round(point.X * dpi.DpiScaleX), Math.Round(point.Y * dpi.DpiScaleY));
        }
        private void Move(Point point) => native!.MovePointer(xid, (int)point.X, (int)point.Y);
        private async Task Click(ButtonBase target, Func<bool> completed)
        {
            Require(target.IsVisible && target.IsEnabled && target.ActualWidth > 0 && target.ActualHeight > 0, "Original native click target is unavailable.");
            int clicks = 0; RoutedEventHandler observer = (_, _) => clicks++;
            target.AddHandler(ButtonBase.ClickEvent, observer, true);
            long began = eventSequence;
            try
            {
                Point requested = Client(target, new Point(target.ActualWidth / 2, target.ActualHeight / 2)); Move(requested);
                try { native!.Button(1, true); await Wait(() => downSequence > began && Mouse.LeftButton == MouseButtonState.Pressed); }
                finally { native!.Button(1, false); }
                await Wait(() => clicks == 1 && upSequence > began && Mouse.LeftButton == MouseButtonState.Released && Mouse.Captured is null && completed() && Attached());
                observations.Add(new { stage = "native-click-coordinate", target = target.GetType().FullName, target.Name,
                    requestedX = requested.X, requestedY = requested.Y, observedX = lastDownPoint.X, observedY = lastDownPoint.Y,
                    coordinateDifferenceDip = (lastDownPoint - requested).Length, freshDown = downSequence > began, freshUp = upSequence > downSequence, clicks });
                Require((lastDownPoint - requested).Length <= 2, "Native client/WPF pointer coordinates differ by more than2DIP at scale1.");
                Move(Client(scroll, new Point(scroll.ViewportWidth * .35, scroll.ViewportHeight * .35)));
            }
            finally { target.RemoveHandler(ButtonBase.ClickEvent, observer); }
        }
        private async Task Wheel(bool positive, bool clamp = false)
        {
            Point point = Client(scroll, new Point(scroll.ViewportWidth * .35, scroll.ViewportHeight * .35)); Move(point);
            long before = eventSequence; double oldScale = scale.ScaleX; Point anchor = Mouse.GetPosition(canvas);
            uint button = positive ? 4u : 5u; try { native!.Button(button, true); } finally { native!.Button(button, false); }
            await Wait(() => wheelSequence > before && Attached() && (clamp ? scale.ScaleX == oldScale : positive ? scale.ScaleX > oldScale : scale.ScaleX < oldScale));
            Point after = Mouse.GetPosition(canvas);
            observations.Add(new { stage = "wheel-anchor", positive, clamp, beforeScale = oldScale, afterScale = scale.ScaleX,
                before = new { anchor.X, anchor.Y }, after = new { after.X, after.Y }, displacement = (after - anchor).Length, sampling = RenderOptions.GetBitmapScalingMode(image).ToString(), calibratedTolerance = "UNSET" });
        }
        private sealed record PanState(Point Point, double X, double Y, double ForwardX, double ForwardY, double Width, double Height, double Scale);
        private async Task<PanState> PanForward(bool hold)
        {
            Require(!vm.ShowCrossHair && !(bool)vm.GetType().GetProperty("ShowBahtinovAnalyzer")!.GetValue(vm)!, "Pan requires original overlays off.");
            Point point = Client(scroll, new Point(scroll.ViewportWidth * .5, scroll.ViewportHeight * .5)); Move(point);
            await Task.Delay(20);
            var content = (FrameworkElement)child.FindName("PART_ImageViewContent"); Require(!content.IsHitTestVisible || !content.IsMouseOver, "Original content overlay owns this pan target.");
            double x = scroll.HorizontalOffset, y = scroll.VerticalOffset, width = scroll.ExtentWidth, height = scroll.ExtentHeight, oldScale = scale.ScaleX;
            long before = eventSequence;
            try
            {
                native!.Button(1, true); await Wait(() => downSequence > before && ReferenceEquals(Mouse.Captured, scroll) && Mouse.LeftButton == MouseButtonState.Pressed);
                Move(new Point(point.X - 48, point.Y - 32)); await Wait(() => moveSequence > downSequence && scroll.HorizontalOffset > x + 1 && scroll.VerticalOffset > y + 1);
                if (hold) streamPanHeld = true;
            }
            finally { if (!hold || !streamPanHeld) native!.Button(1, false); }
            if (!hold) await Wait(() => upSequence > downSequence && Mouse.Captured is null && Mouse.LeftButton == MouseButtonState.Released);
            Require(scroll.HorizontalOffset <= scroll.ScrollableWidth && scroll.VerticalOffset <= scroll.ScrollableHeight, "Pan escaped scroll bounds.");
            return new PanState(point, x, y, scroll.HorizontalOffset, scroll.VerticalOffset, width, height, oldScale);
        }
        private async Task PanReverse(PanState forward, bool held)
        {
            long before = eventSequence;
            try
            {
                if (!held)
                {
                    Move(new Point(forward.Point.X - 48, forward.Point.Y - 32)); native!.Button(1, true);
                    await Wait(() => downSequence > before && ReferenceEquals(Mouse.Captured, scroll) && Mouse.LeftButton == MouseButtonState.Pressed);
                }
                Move(forward.Point); await Wait(() => moveSequence > before && scroll.HorizontalOffset < forward.ForwardX - 1 && scroll.VerticalOffset < forward.ForwardY - 1);
            }
            finally { native!.Button(1, false); streamPanHeld = false; }
            await Wait(() => upSequence > downSequence && upSequence > before && Mouse.Captured is null && Mouse.LeftButton == MouseButtonState.Released);
            Require(Math.Abs(scroll.HorizontalOffset - forward.X) < Math.Abs(forward.ForwardX - forward.X) * .5 && Math.Abs(scroll.VerticalOffset - forward.Y) < Math.Abs(forward.ForwardY - forward.Y) * .5, "Reverse pan did not approach both original offsets.");
            Require(scale.ScaleX == forward.Scale && scroll.ExtentWidth == forward.Width && scroll.ExtentHeight == forward.Height, "Pan changed scale or extent.");
            ObserveGeometry("reverse-pan-released");
        }
        private async Task Flip(bool enabled)
        {
            await Click(flip, () => (bool)vm.GetType().GetProperty("ImageFlip")!.GetValue(vm)! == enabled && child.ImageFlip == (enabled ? -1 : 1));
            Require(ReferenceEquals(BindingOperations.GetBindingBase(flip, ToggleButton.IsCheckedProperty), flipBinding), "Original flip binding replaced.");
        }
        private async Task Crosshair(bool enabled)
        {
            await Click(crosshair, () => vm.ShowCrossHair == enabled && (crosshairPath.Visibility == Visibility.Visible) == enabled);
            Require(ReferenceEquals(BindingOperations.GetBindingBase(crosshair, ToggleButton.IsCheckedProperty), crosshairBinding), "Original crosshair binding replaced.");
        }
        private PanState? streamPan;
        private async Task StreamInput(int index)
        {
            switch (index)
            {
                case 0: await Click(toolbar[3], () => scale.ScaleX == 1); break;
                case 1: await Wheel(true); break;
                case 2: await Wheel(false); break;
                case 3: await Click(toolbar[3], () => scale.ScaleX == 1); break;
                case 4: streamPan = await PanForward(true); break;
                case 5: Require(streamPanHeld && Mouse.LeftButton == MouseButtonState.Pressed && ReferenceEquals(Mouse.Captured, scroll), "Full frame did not retain genuine drag capture."); await PanReverse(streamPan!, true); break;
                case 6: await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1); break;
                case >= 7 and <= 10: int angle = (index - 6) * 90 % 360; await Click(rotate, () => vm.ImageRotation == angle); break;
                case 11: await Flip(true); break;
                case 12: await Flip(false); break;
                case 13: await Crosshair(true); break;
                case 14: await Crosshair(false); break;
                case 15: await Click(toolbar[2], () => scale.ScaleX > 0 && scale.ScaleX < 1); break;
            }
            ObserveGeometry("stream-input-" + index);
        }
        private Task<ushort[]> Prepare(int framePhase) => Task.Run(() => { var data = new ushort[readback.Length]; for (int index = 0; index < data.Length; index++) data[index] = Expected(index, framePhase); return data; });
        private async Task AssignFrame(int framePhase) => Assign(await Prepare(framePhase));
        private void Assign(ushort[] data)
        {
            source = BitmapSource.Create(SourceWidth, SourceHeight, 96, 96, channels == 3 ? PixelFormats.Rgb48 : PixelFormats.Gray16, null, data, stride);
            source.Freeze(); vm.Image = source; Array.Clear(data);
        }
        private string VerifySource(int framePhase)
        {
            Require(source != null && ReferenceEquals(vm.Image, source) && ReferenceEquals(child.Image, source) && ReferenceEquals(image.Source, source), "Original source identity lost.");
            Require(source!.IsFrozen && source.PixelWidth == SourceWidth && source.PixelHeight == SourceHeight && source.DpiX == 96 && source.DpiY == 96 && source.Format == (channels == 3 ? PixelFormats.Rgb48 : PixelFormats.Gray16), "Original source format/dimensions/DPI/frozen state differs.");
            source!.CopyPixels(readback, stride, 0);
            for (int index = 0; index < readback.Length; index++) if (readback[index] != Expected(index, framePhase)) throw new InvalidDataException("Exact source/caller ownership differs at " + index);
            string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(readback.AsSpan())));
            observations.Add(new { stage = "exact-source-ownership", format = NativeFormat, framePhase, source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
                source.IsFrozen, sourceHash = hash, bytes = readback.LongLength * 2, fullCopyPixelsExact = true, callerArrayCleared = true, sourceIdentityRetained = true });
            return hash;
        }
        private List<(int X, int Y)> BodyPoints()
        {
            var result = new List<(int X, int Y)>();
            foreach (double x in new[] { .1, .27, .43, .61, .79, .9 }) foreach (double y in new[] { .12, .3, .53, .72, .9 })
            { Point p = Client(scroll, new Point(scroll.ViewportWidth * x, scroll.ViewportHeight * y)); result.Add(((int)p.X, (int)p.Y)); }
            foreach (var fraction in new[] { new Point(.5, .2), new Point(.5, .8), new Point(.2, .5), new Point(.8, .5) })
            {
                Point p = Client(canvas, new Point(SourceWidth * fraction.X, SourceHeight * fraction.Y));
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) result.Add(((int)p.X + dx, (int)p.Y + dy));
            }
            var size = native!.Size(xid);
            return result.Where(p => p.X > 5 && p.Y > 45 && p.X < size.Width - 5 && p.Y < size.Height - 5).Distinct().ToList();
        }
        private sealed record NativePixels(string Hash, uint[] Window, uint[] Root);
        private NativePixels ReadPixels()
        {
            var pixels = native!.Pixels(xid, points);
            return new NativePixels(Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.Window.AsSpan()))), pixels.Window, pixels.Root);
        }
        private NativePixels ReadLegacyPixels()
        {
            uint[] window = points.Select(point => native!.Pixel(xid, point.X, point.Y)).ToArray();
            uint[] root = points.Select(point => native!.ScreenPixel(xid, point.X, point.Y)).ToArray();
            return new NativePixels(Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(window.AsSpan()))), window, root);
        }
        private async Task PixelTransportParity(string label)
        {
            Require(points.Count == 66, "Original native point set differs.");
            NativePixels? legacyBefore = null, batched = null, legacyAfter = null; int attempts = 0;
            await Wait(() =>
            {
                if (!Attached() || !native!.IsMapped(xid)) return false;
                attempts++;
                legacyBefore = ReadLegacyPixels(); batched = ReadPixels(); legacyAfter = ReadLegacyPixels();
                bool stable = legacyBefore.Window.AsSpan().SequenceEqual(legacyAfter.Window) &&
                    legacyBefore.Root.AsSpan().SequenceEqual(legacyAfter.Root) &&
                    legacyBefore.Window.AsSpan().SequenceEqual(legacyBefore.Root) &&
                    legacyAfter.Window.AsSpan().SequenceEqual(legacyAfter.Root);
                if (!stable) return false;
                Require(legacyBefore.Window.AsSpan().SequenceEqual(batched.Window) &&
                    legacyBefore.Root.AsSpan().SequenceEqual(batched.Root), "Batched native pixels differ from the stable legacy reads during " + label);
                return true;
            });
            observations.Add(new { stage = "native-pixel-transport-parity", label, format = NativeFormat, attempts,
                points = points.Select(point => new { x = point.X, y = point.Y }).ToArray(),
                legacyWindow = legacyBefore!.Window, legacyRoot = legacyBefore.Root,
                batchedWindow = batched!.Window, batchedRoot = batched.Root,
                legacyAfterWindow = legacyAfter!.Window, legacyAfterRoot = legacyAfter.Root,
                allPointsExact = true, legacyBeforeAfterStable = true, rootAndWindowEqual = true, outsideTimedWorkload = true });
        }
        private async Task<string> Surface(string? changedFrom, string label, string? restored = null)
        {
            NativePixels? accepted = null; string? prior = null;
            await Wait(() =>
            {
                if (!Attached() || points.Count < 20 || !native!.IsMapped(xid)) return false;
                var pixels = ReadPixels(); bool stable = prior == pixels.Hash; prior = pixels.Hash;
                if (!pixels.Window.AsSpan().SequenceEqual(pixels.Root) || !stable || (changedFrom != null && pixels.Hash == changedFrom) || (restored != null && pixels.Hash != restored)) return false;
                accepted = pixels; return pixels.Window.Distinct().Count() > 4;
            });
            observations.Add(new { stage = "actual-native-surface", label, format = NativeFormat, elapsedMs = elapsed.Elapsed.TotalMilliseconds, signature = accepted!.Hash,
                points = points.Select(point => new { x = point.X, y = point.Y }).ToArray(), windowPixels = accepted.Window, rootPixels = accepted.Root, repeatedStableObservation = true, rootAndWindowEqual = true,
                offscreenRendererUsed = false, monitorScanoutClaim = false });
            return accepted.Hash;
        }
        private void ObserveGeometry(string label)
        {
            var transform = canvas.TransformToAncestor(Window); Point origin = transform.Transform(new Point(0, 0));
            Vector x = transform.Transform(new Point(1, 0)) - origin, y = transform.Transform(new Point(0, 1)) - origin;
            Require(double.IsFinite(origin.X) && double.IsFinite(origin.Y) && x.Length > 0 && y.Length > 0, "Source basis is non-finite/degenerate.");
            observations.Add(new { stage = "native-image-geometry", label, scale.ScaleX, scale.ScaleY, basisX = new { x.X, x.Y }, basisY = new { y.X, y.Y },
                scroll.HorizontalOffset, scroll.VerticalOffset, scroll.ViewportWidth, scroll.ViewportHeight, scroll.ExtentWidth, scroll.ExtentHeight,
                vm.ImageRotation, child.ImageFlip, vm.ShowCrossHair, dpiX = VisualTreeHelper.GetDpi(Window).DpiScaleX, sampling = RenderOptions.GetBitmapScalingMode(image).ToString() });
        }
        private static string[] ParentChain(DependencyObject visual)
        {
            var result = new List<string>(); DependencyObject? current = visual;
            while (current != null) { result.Add(current.GetType().FullName!); current = VisualTreeHelper.GetParent(current); }
            return result.ToArray();
        }
        internal void ReleasePendingInput() { if (streamPanHeld) { native!.Button(1, false); streamPanHeld = false; } }
        public void Dispose()
        {
            heartbeat.Stop(); ReleasePendingInput();
            observations.Add(new { stage = "dispatcher-progress", heartbeats, extraLatencyBudget = "UNSET", shortWorkload = true });
            if (!disposed) vm.Dispose();
        }
    }
}
