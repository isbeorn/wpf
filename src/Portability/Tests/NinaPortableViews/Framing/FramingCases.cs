using System.Collections;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Image.Interfaces;
using NINA.Sequencer.Mediator;
using NINA.WPF.Base.Behaviors;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Mediator;
using NINA.WPF.Base.SkySurvey;
using ProfileService = NINA.Profile.ProfileService;

internal static partial class Program
{
    private static NINA.View.FramingAssistantView? view;
    private static FixedSurveyFactory? survey;
    private static void RunFraming()
    {
        Check(required[5], () =>
        {
            string[] uris = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(System.IO.Path.Combine(TestAssetDirectory, "ResourceSources.json")))!;
            Require(uris.SequenceEqual(ExpectedResourceSources()), "Expected exact original App34 dictionary sequence, including Hyperlink before SplitButton.");
            foreach (string uri in uris) { phase = "dictionary:" + uri; app!.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) }); resources.Add(uri); }
            profile = (ProfileService)app!.Resources["ProfileService"];
            Require(profile.TryLoad("") && profile.Profiles.Count == 1, "Isolated real profile failed.");
            var p = profile.ActiveProfile;
            p.ApplicationSettings.SkySurveyCacheDirectory = Owned(System.IO.Path.Combine(state, "cache"));
            p.FramingAssistantSettings.LastSelectedImageSource = SkySurveySource.NASA;
            p.FramingAssistantSettings.FieldOfView = 5;
            p.FramingAssistantSettings.CameraWidth = 1000;
            p.FramingAssistantSettings.CameraHeight = 800;
            p.FramingAssistantSettings.SaveImageInOfflineCache = false;
            p.CameraSettings.PixelSize = 3.76; p.TelescopeSettings.FocalLength = 500;
            Owned(p.ApplicationSettings.SkySurveyCacheDirectory);
        });
        Check(required[6], () =>
        {
            Type type = typeof(NINA.View.FramingAssistantView).Assembly.GetType("NINA.ViewModel.FramingAssistant.FramingAssistantVM", true)!;
            Require(type.GetConstructors().Single().GetParameters().Length == 17, "Original constructor shape differs.");
            foreach (string field in new[] { "FRAMINGASSISTANTCACHEPATH", "FRAMINGASSISTANTCACHEINFOPATH" })
                Owned((string)type.GetField(field, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!);
            Type searchType = type.Assembly.GetType("NINA.ViewModel.DeepSkyObjectSearchVM", true)!;
            var search = (IDeepSkyObjectSearchVM)Activator.CreateInstance(searchType)!;
            var night = new FixedNighttime();
            var camera = new CameraMediator();
            camera.RegisterHandler(FixtureServices.DisconnectedCameraHandler.Create());
            Require(camera.GetInfo() is { Connected: false }, "Declared disconnected camera info required.");
            object[] args = [profile!, camera, new TelescopeMediator(), new ApplicationStatusMediator(), night,
                ForbiddenEquipment.Create<IPlanetariumFactory>(), FixtureServices.InactiveSequenceMediator.Create(), new ApplicationMediator(), search,
                new ImagingMediator(), new FilterWheelMediator(), new GuiderMediator(), new RotatorMediator(), new DomeMediator(),
                ForbiddenEquipment.Create<IDomeFollower>(), ForbiddenEquipment.Create<IImageDataFactory>(), ForbiddenEquipment.Create<IWindowServiceFactory>()];
            vm = (IFramingAssistantVM)Activator.CreateInstance(type, args)!;
            Require(vm.GetType() == type && vm.SkyMapAnnotator.GetType().FullName == "NINA.WPF.Base.SkySurvey.SkyMapAnnotator", "Replacement VM/annotator forbidden.");
            survey = new FixedSurveyFactory(observations); vm.SkySurveyFactory = survey;
            object time = GetPublic<object>(vm, "TimeContext");
            foreach ((string name, int value) in new[] { ("Year", 2026), ("Month", 7), ("Day", 28), ("Hour", 20), ("Minute", 15) }) SetPublic(time, name, value);
            vm.SkyMapAnnotator.ProjectionMode = SkyMapProjectionMode.Equatorial;
            vm.SkyMapAnnotator.ShowHorizon = false;
            observations.Add(new { stage = "real-constructor", type = type.AssemblyQualifiedName, arguments = args.Select(a => a.GetType().FullName).ToArray(),
                search = "original parameterless public constructor on internal search VM", nighttime = "fixed date with original NighttimeData and stopped Ticker",
                observerAstrometry = "not claimed; equatorial and no horizon", time = GetPublic<DateTime>(time, "SelectedDateTime"),
                cameraService = "original CameraMediator with declared disconnected ICameraVM GetDeviceInfo only; all unrelated handler calls fail; no camera qualification",
                sequenceService = "declared inactive ISequenceMediator: Initialized=false, every other member fails; no sequencing qualification" });
        });
        Check(required[7], () =>
        {
            ObserveBaml(typeof(NINA.View.FramingAssistantView), "NINA.g.resources", "view/framingassistantview.baml");
            ObserveBaml(typeof(NINA.View.SkyMapOverlayView), "NINA.g.resources", "view/skymapoverlayview.baml");
            ObserveBaml(typeof(NINA.WPF.Base.View.ImageView), "NINA.WPF.Base.g.resources", "view/imageview.baml");
            view = new NINA.View.FramingAssistantView { DataContext = vm };
            window = new Window { Title = "Original NINA framing fixture", Width = 1280, Height = 900, Content = view, ShowInTaskbar = false };
            window.Show(); Layout();
            Require(vm!.BoundWidth > 0 && vm.BoundHeight > 0, "Original viewport size binding did not supply real dimensions.");
        });
        Check(required[8], () =>
        {
            PumpUntil(() => vm!.Cache != null && GetPublic<IList>(vm, "HipsSkyMaps").Count > 0, "Real constructor cache/HiPS readiness");
            observations.Add(new { stage = "constructor-readiness", hips = GetPublic<IList>(vm!, "HipsSkyMaps").Count, cache = profile!.ActiveProfile.ApplicationSettings.SkySurveyCacheDirectory,
                taskCompletion = "not publicly exposed; populated real collections and cache observed" });
        });
        Check(required[9], () =>
        {
            var target = new DeepSkyObject("owned fixed framing target", new Coordinates(333.7, 73.4, Epoch.J2000, Coordinates.RAType.Degrees), profile!.ActiveProfile.AstrometrySettings.Horizon);
            Await(vm!.SetCoordinates(target)); Loaded();
            Require(!vm.SkyMapAnnotator.DynamicFoV && !vm.ZoomInCommand.CanExecute(null) && !vm.ZoomOutCommand.CanExecute(null), "Static source zoom ownership differs.");
        });
        Check(required[10], StaticZoom);
        Check(required[11], () =>
        {
            foreach (double fov in new[] { 4d, 5d }) { vm!.FieldOfView = fov; Await(vm.LoadImageCommand.ExecuteAsync(null)); Loaded(); Near(vm.SkyMapAnnotator.ViewportFoV.VFoV, fov, "Real FoV"); }
            foreach (double rotation in new[] { 37d, 323d, 0d })
            {
                SetPublic(vm!, "RectangleRotation", rotation); Layout(); Near(vm!.Rectangle.Rotation, rotation, "Rotation");
                var projected = GetPublic<SkyMapCameraRectanglePlacement>(vm, "ProjectedRectangle");
                Require(double.IsFinite(projected.Rotation), "Projected rotation is not finite.");
                observations.Add(new { stage = "rotation", requested = rotation, projected.Rotation, projected.X, projected.Y });
            }
        });
        Check(required[12], Mosaic);
        Check(required[13], () =>
        {
            Require(!vm!.SkyMapAnnotator.DynamicFoV, "Static projected drag contract required.");
            Point initial = ProjectedCenter();
            Point initialVisual = OriginalMainCenter();
            foreach (var delta in new[] { new Vector(5, -3), new Vector(-5, 3) })
            {
                Point beforeCenter = ProjectedCenter();
                var before = vm.Rectangle.Coordinates; double ra = before.RADegrees, dec = before.Dec;
                vm.DragStartCommand.Execute(null); vm.DragMoveCommand.Execute(new DragResult { Mode = DragMode.Move, Delta = delta }); vm.DragStopCommand.Execute(null); Layout();
                Require(vm.Rectangle.Coordinates.RADegrees != ra || vm.Rectangle.Coordinates.Dec != dec, "Managed real drag did not move the target.");
                Point afterCenter = ProjectedCenter();
                Require(Math.Abs(afterCenter.X - beforeCenter.X - delta.X) < 1E-9 && Math.Abs(afterCenter.Y - beforeCenter.Y - delta.Y) < 1E-9,
                    "Static projected center did not follow the signed drag delta.");
                Point actualVisual = OriginalMainCenter();
                observations.Add(new { stage = "managed-drag-not-native", delta.X, delta.Y, beforeCenter, afterCenter, actualVisual,
                    beforeRa = ra, beforeDec = dec, afterRa = vm.Rectangle.Coordinates.RADegrees, afterDec = vm.Rectangle.Coordinates.Dec });
            }
            Point restored = ProjectedCenter();
            Point restoredVisual = OriginalMainCenter();
            Require(Math.Abs(restored.X - initial.X) < 1E-9 && Math.Abs(restored.Y - initial.Y) < 1E-9, "Static projected center did not return after opposite deltas.");
            Require(Math.Abs(restoredVisual.X - initialVisual.X) < 0.01 && Math.Abs(restoredVisual.Y - initialVisual.Y) < 0.01, "Original visual center did not return after opposite deltas.");
            observations.Add(new { stage = "static-drag-return", initial, restored, initialVisual, restoredVisual,
                modelTolerance = 1E-9, visualTolerance = 0.01, celestialCoordinatesAndRotationRestorationClaimed = false });
        });
        Check(required[14], () =>
        {
            vm!.FramingAssistantSource = SkySurveySource.SKYATLAS; Await(vm.LoadImageCommand.ExecuteAsync(null)); Loaded();
            Require(vm.SkyMapAnnotator.DynamicFoV && vm.ZoomInCommand.CanExecute(null) && vm.ZoomOutCommand.CanExecute(null), "Dynamic source commands unavailable.");
            foreach (var pair in new (double start, bool zoomIn, double expected)[] { (1d, true, 1d), (1d, false, 1.5d), (200d, true, 180d), (200d, false, 200d) })
            {
                vm.FieldOfView = pair.start; vm.SkyMapAnnotator.ChangeFoV(pair.start);
                (pair.zoomIn ? vm.ZoomInCommand : vm.ZoomOutCommand).Execute(null); Layout();
                Near(vm.FieldOfView, pair.expected, "Dynamic zoom boundary");
            }
            vm.FieldOfView = 5; vm.SkyMapAnnotator.ChangeFoV(5); vm.SkyMapAnnotator.UpdateSkyMap(); Layout();
        });
        Check(required[15], () =>
        {
            profile!.ActiveProfile.FramingAssistantSettings.LastSelectedImageSource = SkySurveySource.NASA;
            vm!.FramingAssistantSource = SkySurveySource.NASA;
            Require(profile.ActiveProfile.FramingAssistantSettings.LastSelectedImageSource == SkySurveySource.NASA, "Explicit NASA profile reset failed.");
            foreach (bool background in new[] { false, true })
            {
                var target = new DeepSkyObject(background ? "background target" : "owner target", new Coordinates(333.7, 73.4, Epoch.J2000, Coordinates.RAType.Degrees), profile!.ActiveProfile.AstrometrySettings.Horizon);
                Task<bool> operation = background ? Task.Run(() => vm!.SetCoordinates(target)) : vm!.SetCoordinates(target);
                Require(Await(operation), "Public SetCoordinates reported false."); Loaded();
                Require(vm!.FramingAssistantSource == SkySurveySource.NASA && !vm.SkyMapAnnotator.DynamicFoV, "SetCoordinates did not use the explicitly reset profile NASA mode.");
            }

        });
        CacheRoundTrip();
        Check(required[16], LayoutOverlay);
        Check(required[17], () =>
        {
            window!.Close(); window = null;
            Require(PresentationSource.FromVisual(view!) == null, "Closed view retains source.");
            vm!.Dispose(); vm = null; profile!.Release(); profile = null;
            observations.Add(new { stage = "public-disposal", backgroundTaskCancellation = "not exposed by original VM", processIsolation = true });
        });
    }

    private static Point ProjectedCenter()
    {
        var placement = GetPublic<SkyMapCameraRectanglePlacement>(vm!, "ProjectedRectangle");
        return new Point(placement.X + placement.Width / 2, placement.Y + placement.Height / 2);
    }

    private static Point OriginalMainCenter()
    {
        var image = Visuals(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
        var canvas = Visuals(image).OfType<Canvas>().Single(c => BindingOperations.GetBinding(c, FrameworkElement.WidthProperty)?.Path.Path == "ImageParameter.Image.PixelWidth");
        var main = Visuals(image).OfType<Rectangle>().Single(r => BindingOperations.GetBinding(r, FrameworkElement.WidthProperty)?.Path.Path == "ProjectedRectangle.Width");
        Point actual = main.TranslatePoint(new Point(main.ActualWidth / 2, main.ActualHeight / 2), image);
        Point expected = canvas.TranslatePoint(ProjectedCenter(), image);
        Require(Math.Abs(actual.X - expected.X) < 0.01 && Math.Abs(actual.Y - expected.Y) < 0.01, "Original bound visual center differs from the projected center.");
        return actual;
    }
    private static void Loaded()
    {
        PumpUntil(() => vm!.ImageParameter != null && vm.SkyMapAnnotator.Initialized && vm.RectangleCalculated, "Actual load and annotator");
        Layout();
        Require(vm!.ImageParameter.Image.PixelWidth == 800 && vm.ImageParameter.Image.PixelHeight == 600 && vm.ImageParameter.Image.IsFrozen, "Original frozen image differs.");
        Require(vm.SkyMapAnnotator.Projection != null && vm.SkyMapAnnotator.SkyMapOverlay != null && vm.CameraRectangles.Count > 0, "Real annotation/placement was not reached.");
        var imageView = Visuals(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
        PumpUntil(() => ReferenceEquals(imageView.Image, vm.ImageParameter.Image), "Original async source binding");
        Require(ReferenceEquals(((Image)imageView.FindName("PART_Image")).Source, vm.ImageParameter.Image), "Original child image source binding differs.");
        byte[] actual = new byte[800 * 600 * 4]; vm.ImageParameter.Image.CopyPixels(actual, 3200, 0);
        Require(actual.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(TestAssetDirectory, "survey.bgra"))), "Fixed survey pixels changed through the actual VM.");
    }

    private static void StaticZoom()
    {
        var image = Visuals(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
        var scale = (ScaleTransform)image.FindName("PART_ScaleTransform");
        var root = (Grid)image.Content;
        var toolbar = root.Children.OfType<Grid>().Single(g => Grid.GetRow(g) == 0);
        var buttons = toolbar.Children.OfType<Button>().ToArray();
        Button ButtonAt(int column) => buttons.Single(b => Grid.GetColumn(b) == column);
        double start = scale.ScaleX;
        ButtonAt(0).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); Require(scale.ScaleX > start, "Static original fallback zoom in failed.");
        double larger = scale.ScaleX;
        ButtonAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); Require(scale.ScaleX < larger, "Static original fallback zoom out failed.");
        ButtonAt(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); Near(scale.ScaleX, start, "Static reset");
        observations.Add(new { stage = "static-imageview-fallback", start, afterReset = scale.ScaleX, nativeInput = false, routedClick = true });
    }

    private static void Mosaic()
    {
        foreach ((int horizontal, int vertical) in new[] { (1, 1), (3, 1), (1, 3), (2, 2), (1, 1) })
        {
            vm!.HorizontalPanels = horizontal; vm.VerticalPanels = vertical; Layout();
            Require(vm.CameraRectangles.Count == horizontal * vertical, "Actual mosaic panel count differs.");
            var placements = GetPublic<IEnumerable<SkyMapCameraRectanglePlacement>>(vm, "ProjectedCameraRectangles").ToArray();
            Require(placements.Length == horizontal * vertical && placements.All(p => p.Width > 0 && p.Height > 0 && double.IsFinite(p.X) && double.IsFinite(p.Y)), "Projected mosaic is invalid.");
            observations.Add(new { stage = "mosaic", horizontal, vertical, placements = placements.Select(p => new { p.Id, p.X, p.Y, p.Width, p.Height, p.Rotation }).ToArray() });
        }
        vm!.HorizontalPanels = 2;
        var combo = (ComboBox)view!.FindName("OverlapUnitCombobox");
        var stepper = (NINA.CustomControlLibrary.IntStepperControl)view.FindName("OverlapValueStepperControl");
        foreach (string unit in new[] { "px", "%" })
        {
            combo.SetCurrentValue(ComboBox.SelectedItemProperty, unit); Layout(); Require(vm.SelectedOverlapUnit == unit, "Original unit binding failed.");
            Binding binding = BindingOperations.GetBinding(stepper, NINA.CustomControlLibrary.IntStepperControl.ValueProperty)!;
            Require(binding != null && (unit != "%" || binding.Converter?.GetType().Name == "PercentageConverter"), "Original overlap handler did not install binding/converter.");
            foreach (int value in unit == "px" ? new[] { 100, 200, 100 } : new[] { 20, 30, 20 })
            {
                stepper.SetCurrentValue(NINA.CustomControlLibrary.IntStepperControl.ValueProperty, value); Layout();
                Near(vm.OverlapValue, unit == "%" ? value / 100d : value, "Original overlap two-way binding");
            }
        }
        vm.HorizontalPanels = 1; Layout();
    }

    private static void LayoutOverlay()
    {
        Layout();
        var image = Visuals(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
        var canvas = (Canvas)image.FindName("PART_Canvas");
        Require(canvas.ActualWidth == 800 && canvas.ActualHeight == 600 && view!.IsArrangeValid && view.IsMeasureValid, "Original image canvas/layout differs.");
        var rectangles = Visuals(image).OfType<Rectangle>().Where(r => BindingOperations.GetBinding(r, FrameworkElement.WidthProperty)?.Path.Path is "ProjectedRectangle.Width" or "Width").ToArray();
        Require(rectangles.Length >= 2, "Real original main/panel rectangle visuals absent.");
        var projected = GetPublic<SkyMapCameraRectanglePlacement>(vm!, "ProjectedRectangle");
        var main = rectangles.Single(r => BindingOperations.GetBinding(r, FrameworkElement.WidthProperty)?.Path.Path == "ProjectedRectangle.Width");
        Near(main.ActualWidth, projected.Width, "Projected main visual width"); Near(main.ActualHeight, projected.Height, "Projected main visual height");
        foreach (Rectangle rectangle in rectangles)
        {
            var placement = ReferenceEquals(rectangle, main) ? projected : (SkyMapCameraRectanglePlacement)rectangle.DataContext;
            Point center = rectangle.TranslatePoint(new Point(rectangle.ActualWidth / 2, rectangle.ActualHeight / 2), canvas);
            Near(center.X, placement.X + placement.Width / 2, "Original overlay center X");
            Near(center.Y, placement.Y + placement.Height / 2, "Original overlay center Y");
            Near(((RotateTransform)rectangle.RenderTransform).Angle, placement.Rotation, "Original overlay rotation binding");
        }
        var target = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32); target.Render(view!);
        byte[] pixels = new byte[1280 * 900 * 4]; target.CopyPixels(pixels, 5120, 0);
        Require(pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 32, "Whole original retained view raster has insufficient variation.");
        observations.Add(new { stage = "retained-layout-and-offscreen-raster", nativePixelReadback = false,
            rasterSha256 = Convert.ToHexString(SHA256.HashData(pixels)), rectangles = rectangles.Select(r => new { r.ActualWidth, r.ActualHeight, center = r.TranslatePoint(new Point(r.ActualWidth / 2, r.ActualHeight / 2), canvas) }).ToArray() });
    }
    private static T GetPublic<T>(object owner, string name) => (T)owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(owner)!;
    private static void SetPublic(object owner, string name, object value) => owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(owner, value);
    private static void Near(double actual, double expected, string name) => Require(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-6, name + ": " + actual + " != " + expected);
    private static void Layout() { view!.UpdateLayout(); PumpUntil(() => view.IsArrangeValid && view.IsMeasureValid, "View layout"); }
    private static void PumpUntil(Func<bool> predicate, string label)
    {
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (predicate() || watch.Elapsed > TimeSpan.FromSeconds(15)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        if (failed) throw new DiagnosticAbortException();
        Require(predicate(), label + " did not settle within 15 seconds.");
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted, "Original asynchronous operation"); task.GetAwaiter().GetResult(); }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Visuals(child)) yield return nested; }
    }
    private static void ObserveBaml(Type type, string bundle, string key)
    {
        GeneratedCodeAttribute? compiler = type.GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>();
        Require(compiler?.Tool == "PresentationBuildTasks" && compiler.Version == "10.0.0.0", "Unexpected released compiler3 BAML attribute.");
        using var stream = type.Assembly.GetManifestResourceStream(bundle)!;
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry item in reader)
            if ((string)item.Key == key)
            {
                using var bytes = new MemoryStream(); ((Stream)item.Value!).CopyTo(bytes);
                observations.Add(new { stage = "original-BAML", key, compiler = compiler!.Version, sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())) }); return;
            }
        throw new InvalidDataException("Original BAML missing: " + key);
    }
}
