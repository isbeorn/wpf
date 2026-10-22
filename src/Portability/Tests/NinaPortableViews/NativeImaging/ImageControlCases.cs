using System.CodeDom.Compiler;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.WPF.Base.Mediator;

internal static partial class Program
{
    private const int SourceWidth = 6000, SourceHeight = 4000;
    private const int ViewWidth = 1200, ViewHeight = 850;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunOriginalImageControl()
    {
        phase = "original-33-resource-dictionaries";
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var sources = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ResourceSources.json")))!;
        Require(sources.Length == 33, "Original resource count differs.");
        foreach (var source in sources)
        {
            phase = "resource:" + source;
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
            resources.Add(source);
        }
        checks.Add(new { name = "original-33-resource-dictionaries", status = "pass" });
        Check("actual-isolated-profile", () =>
        {
            Directory.CreateDirectory(NINA.Profile.ProfileService.PROFILEFOLDER);
            Require(!Directory.EnumerateFileSystemEntries(NINA.Profile.ProfileService.PROFILEFOLDER).Any(), "Fresh profile state is required.");
            profile = (NINA.Profile.ProfileService)app.Resources["ProfileService"];
            Require(profile.TryLoad(string.Empty) && profile.Profiles.Count == 1, "Original isolated profile failed.");
            profile.ActiveProfile.ImageSettings.AutoStretch = false;
            profile.ActiveProfile.ImageSettings.DetectStars = false;
        });
        IImageControlVM? vm = null;
        Check("actual-ImageControlVM-public-constructor-original-mediators", () =>
        {
            var type = typeof(NINA.View.ImageControlView).Assembly.GetType("NINA.ViewModel.ImageControlVM", true)!;
            var camera = new CameraMediator();
            var telescope = new TelescopeMediator();
            var imaging = new ImagingMediator();
            var status = new ApplicationStatusMediator();
            vm = (IImageControlVM)Activator.CreateInstance(type, profile!, camera, telescope, imaging, status)!;
            Require(vm.GetType() == type && type.IsNotPublic, "Original internal VM identity differs.");
            Require(vm.Image is null && vm.RenderedImage is null && vm.ImageRotation == 0 && !vm.ShowCrossHair, "Unexpected original VM initial state.");
            observations.Add(new { stage = "original-VM-construction", vmType = type.AssemblyQualifiedName, constructor = "public constructor on internal type via reflection",
                mediatorTypes = new[] { camera.GetType().FullName, telescope.GetType().FullName, imaging.GetType().FullName, status.GetType().FullName },
                registeredEquipmentHandlers = false, acquisition = false, pluginDiscovery = false });
        });
        try
        {
            NINA.View.ImageControlView? view = null;
            Check("original-ImageControlView-and-ImageView-BAML", () =>
            {
                ObserveBaml(typeof(NINA.View.ImageControlView), "NINA.g.resources", "view/imagecontrolview.baml");
                ObserveBaml(typeof(NINA.WPF.Base.View.ImageView), "NINA.WPF.Base.g.resources", "view/imageview.baml");
                view = new NINA.View.ImageControlView { DataContext = vm };
            });
            foreach (bool color in new[] { false, true }) ExerciseFormat(vm!, view!, color);
            Check("public-image-removal-and-real-VM-disposal", () =>
            {
                vm!.Image = null;
                Layout(view!);
                var child = Descendants(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
                Require(child.Image is null && ((Image)child.FindName("PART_Image")).Source is null, "Original binding retained removed image.");
            });
            GC.KeepAlive(app);
        }
        finally { vm?.Dispose(); }
    }

    private static void ExerciseFormat(IImageControlVM vm, NINA.View.ImageControlView view, bool color)
    {
        string format = color ? "Rgb48" : "Gray16";
        int channels = color ? 3 : 1, stride = SourceWidth * channels * 2;
        ushort[] expected = new ushort[SourceWidth * SourceHeight * channels];
        for (int index = 0; index < expected.Length; index++) expected[index] = Expected(index, 0);
        var source = BitmapSource.Create(SourceWidth, SourceHeight, 96, 96, color ? PixelFormats.Rgb48 : PixelFormats.Gray16, null, expected, stride);
        source.Freeze();
        var readback = new ushort[expected.Length];
        Array.Clear(expected);
        source.CopyPixels(readback, stride, 0);
        for (int index = 0; index < expected.Length; index++) expected[index] = Expected(index, 0);
        Require(readback.AsSpan().SequenceEqual(expected), "Scientific source caller ownership failed.");
        string sourceHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(expected.AsSpan())));
        vm.Image = source;
        Layout(view);
        var child = Descendants(view).OfType<NINA.WPF.Base.View.ImageView>().Single();
        var canvas = (Canvas)child.FindName("PART_Canvas");
        var image = (Image)child.FindName("PART_Image");
        var scroll = (ScrollViewer)child.FindName("PART_ScrollViewer");
        var header = (DependencyObject)child.ButtonHeaderContent;
        var rotate = Descendants(header).OfType<Button>().Single(button => BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path == "RotateImageCommand");
        var flip = Descendants(header).OfType<ToggleButton>().Single(button => BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Path.Path == "ImageFlip");
        var crosshair = Descendants(header).OfType<ToggleButton>().Single(button => BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Path.Path == "ShowCrossHair");
        var flipBinding = BindingOperations.GetBinding(flip, ToggleButton.IsCheckedProperty);
        var crosshairBinding = BindingOperations.GetBinding(crosshair, ToggleButton.IsCheckedProperty);
        var transform = (TransformGroup)((Grid)scroll.Content).RenderTransform;
        var rotation = transform.Children.OfType<RotateTransform>().Single();
        var flipTransform = transform.Children.OfType<ScaleTransform>().Single();
        var crosshairPath = Descendants((DependencyObject)child.ImageAreaContent).OfType<System.Windows.Shapes.Path>()
            .Single(path => BindingOperations.GetBinding(path, UIElement.VisibilityProperty)?.Path.Path == "ShowCrossHair");
        string? baselineRaster = null;
        string? baselineImageBodyRaster = null;
        Check(format + "/original-bound-24MP-source-layout", () =>
        {
            Require(ReferenceEquals(view.DataContext, vm) && ReferenceEquals(child.DataContext, vm), "Actual VM inheritance failed.");
            Require(ReferenceEquals(child.Image, source) && ReferenceEquals(image.Source, source), "Original image binding failed.");
            Require(view.IsMeasureValid && view.IsArrangeValid && view.ActualWidth > 0 && view.ActualHeight > 0 && child.IsMeasureValid && child.IsArrangeValid && child.ActualWidth > 0 && child.ActualHeight > 0, "Original parent/child view layout failed.");
            Require(canvas.ActualWidth == SourceWidth && canvas.ActualHeight == SourceHeight && image.ActualWidth == SourceWidth && image.ActualHeight == SourceHeight, "Original VisualBrush image/canvas dimensions differ.");
            Require(rotation.Angle == 0 && flipTransform.ScaleX == 1 && !vm.ShowCrossHair && crosshairPath.Visibility != Visibility.Visible, "Initial transform/crosshair state differs.");
            Require(ReferenceEquals(rotate.Command, vm.GetType().GetProperty("RotateImageCommand")!.GetValue(vm)) && rotate.Command!.CanExecute(rotate.CommandParameter), "Original bound rotate command differs or cannot execute.");
            baselineRaster = Snapshot("initial");
        });
        for (int turn = 1; turn <= 4; turn++)
        {
            int expectedRotation = turn * 90 % 360;
            Check(format + "/bound-rotate-command-" + turn, () =>
            {
                rotate.Command.Execute(rotate.CommandParameter);
                Layout(view);
                Require(vm.ImageRotation == expectedRotation && child.ImageRotation == expectedRotation && rotation.Angle == expectedRotation, "Original rotate command/binding did not advance exactly once.");
                var hash = Snapshot("rotate-" + turn);
                Require(expectedRotation == 0 ? hash == baselineRaster : hash != baselineRaster, "Rotate raster did not change or four turns failed to restore it.");
            });
        }
        foreach (bool enabled in new[] { true, false })
            Check(format + "/bound-horizontal-flip-" + enabled, () =>
            {
                flip.SetCurrentValue(ToggleButton.IsCheckedProperty, enabled);
                Layout(view);
                Require(ReferenceEquals(BindingOperations.GetBinding(flip, ToggleButton.IsCheckedProperty), flipBinding), "Original flip binding was replaced.");
                Require((bool)vm.GetType().GetProperty("ImageFlip")!.GetValue(vm)! == enabled && child.ImageFlip == (enabled ? -1 : 1) && flipTransform.ScaleX == (enabled ? -1 : 1), "Original horizontal flip binding did not round-trip.");
                var hash = Snapshot("flip-" + enabled);
                Require(enabled ? hash != baselineRaster : hash == baselineRaster, "Flip raster did not change or restore.");
            });
        foreach (bool enabled in new[] { true, false })
            Check(format + "/bound-crosshair-" + enabled, () =>
            {
                crosshair.SetCurrentValue(ToggleButton.IsCheckedProperty, enabled);
                Layout(view);
                Require(ReferenceEquals(BindingOperations.GetBinding(crosshair, ToggleButton.IsCheckedProperty), crosshairBinding), "Original crosshair binding was replaced.");
                Require(vm.ShowCrossHair == enabled && (crosshairPath.Visibility == Visibility.Visible) == enabled && !vm.ShowBahtinovAnalyzer, "Original crosshair state/visibility did not round-trip.");
                var hash = Snapshot("crosshair-" + enabled);
                Require(enabled ? hash != baselineRaster : hash == baselineRaster, "Crosshair raster did not change or restore.");
            });
        vm.Image = null;
        Layout(view);

        string Snapshot(string stage)
        {
            Require(ReferenceEquals(vm.Image, source) && ReferenceEquals(child.Image, source) && ReferenceEquals(image.Source, source), "Display operation replaced the scientific source.");
            source.CopyPixels(readback, stride, 0);
            Require(readback.AsSpan().SequenceEqual(expected), "Display operation altered exact scientific pixels.");
            var mapping = canvas.TransformToAncestor(view);
            var center = mapping.Transform(new Point(SourceWidth * .5, SourceHeight * .5));
            var x = mapping.Transform(new Point(SourceWidth * .5 + 1, SourceHeight * .5)) - center;
            var y = mapping.Transform(new Point(SourceWidth * .5, SourceHeight * .5 + 1)) - center;
            Require(double.IsFinite(center.X) && double.IsFinite(center.Y) && x.Length > 0 && y.Length > 0, "Actual transformed source geometry is non-finite or degenerate.");
            var expectedX = vm.ImageRotation switch { 0 => new Vector(1, 0), 90 => new Vector(0, 1), 180 => new Vector(-1, 0), _ => new Vector(0, -1) };
            var expectedY = vm.ImageRotation switch { 0 => new Vector(0, 1), 90 => new Vector(-1, 0), 180 => new Vector(0, -1), _ => new Vector(1, 0) };
            expectedX.X *= child.ImageFlip; expectedY.X *= child.ImageFlip;
            x.Normalize(); y.Normalize();
            Require((x - expectedX).Length < 1e-6 && (y - expectedY).Length < 1e-6, "Actual source-axis transform orientation differs.");
            observations.Add(new { stage = format + "/" + stage + "/before-raster", sourceHash, sourceBytes = expected.LongLength * 2,
                sourceFormat = source.Format.ToString(), source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
                viewWidth = view.ActualWidth, viewHeight = view.ActualHeight, childWidth = child.ActualWidth, childHeight = child.ActualHeight,
                canvasWidth = canvas.ActualWidth, canvasHeight = canvas.ActualHeight, scroll.ViewportWidth, scroll.ViewportHeight,
                vm.ImageRotation, child.ImageFlip, vm.ShowCrossHair,
                showBahtinovAnalyzer = vm.GetType().GetProperty("ShowBahtinovAnalyzer")!.GetValue(vm),
                sourceIdentityAndExactCopyPixelsVerified = true, sourceGeometryVerified = true });
            byte[] pixels = RenderOriginalView(view, ViewWidth, ViewHeight, format + "-" + stage);
            var rasterHash = Convert.ToHexString(SHA256.HashData(pixels));
            const int bodyX = 40, bodyY = 80, bodyWidth = 1120, bodyHeight = 700;
            byte[] body = new byte[bodyWidth * bodyHeight * 4];
            for (int row = 0; row < bodyHeight; row++)
                Buffer.BlockCopy(pixels, ((bodyY + row) * ViewWidth + bodyX) * 4, body, row * bodyWidth * 4, bodyWidth * 4);
            var imageBodyRasterHash = Convert.ToHexString(SHA256.HashData(body));
            if (stage == "initial")
            {
                baselineImageBodyRaster = imageBodyRasterHash;
                int minimum = 255, maximum = 0;
                for (int index = 0; index < body.Length; index += 4)
                {
                    minimum = Math.Min(minimum, body[index]);
                    maximum = Math.Max(maximum, body[index]);
                }
                Require(maximum - minimum > 4, "The canonical image body has no meaningful raster variation.");
            }
            else
            {
                bool restored = vm.ImageRotation == 0 && child.ImageFlip == 1 && !vm.ShowCrossHair;
                Require(restored ? imageBodyRasterHash == baselineImageBodyRaster : imageBodyRasterHash != baselineImageBodyRaster,
                    "The actual image body did not change or restore; toolbar pixels are excluded.");
            }
            observations.Add(new { stage = format + "/" + stage, logicalInvocation = true, nativeInput = false,
                sourceFormat = source.Format.ToString(), source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, sourceHash,
                sourceIdentity = RuntimeHelpers.GetHashCode(source), sourceBytes = expected.LongLength * 2,
                viewWidth = view.ActualWidth, viewHeight = view.ActualHeight, childWidth = child.ActualWidth, childHeight = child.ActualHeight,
                canvasWidth = canvas.ActualWidth, canvasHeight = canvas.ActualHeight, scroll.ViewportWidth, scroll.ViewportHeight,
                vm.ImageRotation, child.ImageFlip, vm.ShowCrossHair, crosshairVisibility = crosshairPath.Visibility.ToString(),
                crosshairWidth = crosshairPath.ActualWidth, crosshairHeight = crosshairPath.ActualHeight,
                sourceCenter = new { center.X, center.Y }, sourceAxisX = new { x.X, x.Y }, sourceAxisY = new { y.X, y.Y },
                sampling = RenderOptions.GetBitmapScalingMode(image).ToString(), rasterHash,
                imageBodyRasterHash, imageBodyRectangle = new { x = bodyX, y = bodyY, width = bodyWidth, height = bodyHeight } });
            return rasterHash;
        }
    }

    private static void ObserveBaml(Type type, string resource, string key)
    {
        var generated = type.GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>();
        Require(generated?.Tool == "PresentationBuildTasks" && generated.Version == "10.0.12.0", "Original Microsoft BAML compiler differs.");
        using var stream = type.Assembly.GetManifestResourceStream(resource)!;
        using var reader = new ResourceReader(stream);
        bool found = false;
        foreach (System.Collections.DictionaryEntry entry in reader)
            if ((string)entry.Key == key)
            {
                using var payload = (Stream)entry.Value!;
                using var bytes = new MemoryStream(); payload.CopyTo(bytes);
                observations.Add(new { stage = "original-BAML", type = type.FullName, resource, key, length = bytes.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())), compiler = generated.Tool, compilerVersion = generated.Version });
                found = true;
            }
        Require(found, "Original BAML was not found: " + key);
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(ViewWidth, ViewHeight));
        view.Arrange(new Rect(0, 0, ViewWidth, ViewHeight));
        view.UpdateLayout(); Settle(); view.UpdateLayout();
    }

    private static ushort Expected(int index, int phase) => index switch
    { 0 => 0, 1 => 1, 2 => 255, 3 => 256, 4 => 257, 5 => 32767, 6 => 32768, 7 => 65534, 8 => 65535, _ => unchecked((ushort)(index * 37 + phase * 997)) };

    private static byte[] RenderOriginalView(Visual visual, int width, int height, string name)
    {
        ObserveTileBrushes(visual, name);
        var type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        object target = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [visual, width, height], null)!;
        try
        {
            type.GetMethod("Render")!.Invoke(target, null);
            using var bitmap = (SkiaSharp.SKBitmap)type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
            Require(bitmap.RowBytes == width * 4, "Unexpected existing raster observation row stride.");
            var pixels = new byte[height * bitmap.RowBytes];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            var output = Environment.GetEnvironmentVariable("NINA_IMAGE_VM_CAPTURE_DIRECTORY")!;
            Directory.CreateDirectory(output);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoded.SaveTo(stream);
            return pixels;
        }
        finally { ((IDisposable)target).Dispose(); }
    }

    private static void ObserveTileBrushes(Visual root, string name)
    {
        void Walk(DependencyObject visual, string path, List<object> ancestors)
        {
            var element = visual as UIElement;
            var framework = visual as FrameworkElement;
            var state = new { type = visual.GetType().FullName, name = framework?.Name,
                visibility = element?.Visibility.ToString(), element?.IsVisible, element?.Opacity,
                width = framework?.ActualWidth, height = framework?.ActualHeight };
            var chain = new List<object>(ancestors) { state };
            var brushes = new List<(string Property, Brush? Brush)>();
            if (visual is System.Windows.Shapes.Shape shape) brushes.Add(("Fill", shape.Fill));
            if (visual is Panel panel) brushes.Add(("Background", panel.Background));
            if (visual is Border border) brushes.Add(("Background", border.Background));
            if (visual is Control control) brushes.Add(("Background", control.Background));
            foreach (var (property, brush) in brushes)
            {
                if (brush is not TileBrush tile) continue;
                var imageBrush = brush as ImageBrush;
                var bitmap = imageBrush?.ImageSource as BitmapSource;
                var visualBrush = brush as VisualBrush;
                var imageVisual = visualBrush?.Visual as Image;
                var visualBitmap = imageVisual?.Source as BitmapSource;
                observations.Add(new { stage = "pre-raster-tile-brush", capture = name, visualPath = path, property, owner = state, ancestors = chain,
                    brushType = brush.GetType().FullName, brush.Opacity, tile.TileMode, tile.Stretch, tile.AlignmentX, tile.AlignmentY,
                    viewbox = tile.Viewbox.ToString(), tile.ViewboxUnits, viewport = tile.Viewport.ToString(), tile.ViewportUnits,
                    transform = brush.Transform.Value.ToString(), relativeTransform = brush.RelativeTransform.Value.ToString(),
                    imageSourceBinding = imageBrush == null ? null : BindingOperations.GetBinding(imageBrush, ImageBrush.ImageSourceProperty)?.Path.Path,
                    imageSourceType = imageBrush?.ImageSource?.GetType().FullName, imageSourceIsNull = imageBrush?.ImageSource == null,
                    imagePixelWidth = bitmap?.PixelWidth, imagePixelHeight = bitmap?.PixelHeight, imagePixelFormat = bitmap?.Format.ToString(),
                    visualSourceType = visualBrush?.Visual?.GetType().FullName, visualSourceName = imageVisual?.Name,
                    visualPixelWidth = visualBitmap?.PixelWidth, visualPixelHeight = visualBitmap?.PixelHeight, visualPixelFormat = visualBitmap?.Format.ToString() });
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
                Walk(VisualTreeHelper.GetChild(visual, index), path + "/" + index, chain);
        }
        Walk(root, "root", []);
    }
}
