using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.WPF.Base.SkySurvey;

internal static partial class Program {
    private static void RendererCases(bool recordReference) {
        Check("complete-public-contract-matches-original", () => {
            var api = typeof(SkyMapRasterRenderer).Assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).Select(t => new {
                type = t.FullName,
                members = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.ToString()).OrderBy(m => m, StringComparer.Ordinal).ToArray()
            });
            string text = JsonSerializer.Serialize(api, new JsonSerializerOptions { WriteIndented = true });
            string path = Path.Combine(referenceDirectory, "public-contract.json");
            if (recordReference) File.WriteAllText(path, text);
            else {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referenceDirectory))!, OperatingSystem.IsWindows() ? "windows-public-contract.json" : "linux-public-contract.json"), text);
                Require(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(text), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))), "Complete original WPF.Base public API differs.");
            }
        });
        foreach (var mode in new[] { System.Windows.Interop.RenderMode.Default, System.Windows.Interop.RenderMode.SoftwareOnly }) {
            RenderOptions.ProcessRenderMode = mode;
            bool software = mode == System.Windows.Interop.RenderMode.SoftwareOnly;
            Check(mode + "/RasterRenderer_ReusesWritableViewportSurface", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                var scene = new SkyMapScene([new SkyMapStar(new Point(20, 20), 3)], [new SkyMapLine(new Point(20, 20), new Point(30, 30))], [new SkyMapDeepSkyObject("M42", "BRTNB", new Point(60, 60), 10, 8, 30)], [], []);
                ImageSource first = renderer.Render(scene, [], null), second = renderer.Render(scene, [], null);
                Require(!first.IsFrozen && first.Width == 100 && first.Height == 100 && ReferenceEquals(first, second) && renderer.UsesSoftwareComposition == software, "Reusable unfrozen viewport contract changed.");
            });
            Check(mode + "/RasterRenderer_WithCachedImage_ReturnsOriginalCompositionContract", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                var placement = new SkyMapImagePlacement(Pixel(0, 0, 255), new Point(50, 50), 100, 100, 0);
                ImageSource first = renderer.Render(Empty(), [placement], null), second = renderer.Render(Empty(), [placement], null);
                Require(software ? first is WriteableBitmap && ReferenceEquals(first, second) : first is DrawingImage && second is DrawingImage && !ReferenceEquals(first, second), "Cached-image result/binding refresh contract changed.");
                Require(At(Frame(second), 100, 50, 50).SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Cached image did not reach rendered pixels.");
            });
            Check(mode + "/RasterRenderer_GridLabelAtViewportEdge_RemainsVisible", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                var scene = new SkyMapScene([], [], [], [], [], [new SkyMapLabel("05:40h", new Point(99, 99), SkyMapLabelKind.Grid)]);
                Require(Region(Frame(renderer.Render(scene, [], null)), 100, 50, 70, 50, 30, p => p[3] > 0), "Clamped edge grid label disappeared.");
            });
            Check(mode + "/RasterRenderer_PreservesFlipAndClearsPreviousFrame", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                var first = renderer.Render(Empty(), [new SkyMapImagePlacement(Asymmetric(), new Point(50, 50), 40, 20, 0, true)], null);
                byte[] pixels = Frame(first);
                Require(At(pixels, 100, 40, 50)[0] > At(pixels, 100, 40, 50)[2] && At(pixels, 100, 60, 50)[2] > At(pixels, 100, 60, 50)[0], "Horizontal flip changed.");
                pixels = Frame(renderer.Render(Empty(), [], null));
                Require(At(pixels, 100, 40, 50)[3] == 0 && At(pixels, 100, 60, 50)[3] == 0, "Previous frame was retained.");
            });
            Check(mode + "/RasterRenderer_RotatesCachedImageAroundCenter", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                byte[] pixels = Frame(renderer.Render(Empty(), [new SkyMapImagePlacement(Asymmetric(), new Point(50, 50), 40, 20, 90)], null));
                Require(At(pixels, 100, 50, 40)[2] > At(pixels, 100, 50, 40)[0] && At(pixels, 100, 50, 60)[0] > At(pixels, 100, 50, 60)[2], "Cached tile rotation changed.");
            });
            Check(mode + "/RasterRenderer_CardinalDirectionAndDsoStyles", () => {
                using var renderer = new SkyMapRasterRenderer(240, 140);
                var dsos = new[] { "GALXY", "GALCL", "PLNNB", "BRTNB", "CL+NB", "GLOCL", "OTHER" }.Select((type, i) => new SkyMapDeepSkyObject("D", type, new Point(20 + i * 32, 80), 10, 6, 37)).ToArray();
                var scene = new SkyMapScene([], [], dsos, [], [], [new SkyMapLabel("N", new Point(120, 20), SkyMapLabelKind.CardinalDirection)]);
                byte[] pixels = Frame(renderer.Render(scene, [], null), 240, 140);
                Require(Region(pixels, 240, 100, 0, 40, 45, p => p[2] > 128 && p[0] < 64 && p[1] < 64), "Large red cardinal label is absent.");
                foreach (var dso in dsos) Require(At(pixels, 240, (int)dso.Center.X, (int)dso.Center.Y)[3] > 0, "DSO ellipse fill missing for " + dso.Type);
            });
            Check(mode + "/RasterRenderer_HorizonMask_HidesCachedImagesAndKeepsCardinalVisible", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                var mask = new SkyMapPath([new Point(0, 50), new Point(100, 50), new Point(100, 100), new Point(0, 100)], true);
                var scene = new SkyMapScene([], [], [], [], [], [new SkyMapLabel("S", new Point(50, 80), SkyMapLabelKind.CardinalDirection)], horizonMaskAreas: [mask]);
                byte[] pixels = Frame(renderer.Render(scene, [new SkyMapImagePlacement(Pixel(0, 0, 255), new Point(50, 50), 100, 100, 0)], null));
                Require(At(pixels, 100, 10, 25).SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Mask covered visible sky.");
                foreach (int x in new[] { 10, 50, 90 }) { byte[] p = At(pixels, 100, x, 60); Require(p[3] == 255 && !p.SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Opaque horizon leaked cached sky."); }
                Require(Region(pixels, 100, 35, 65, 30, 30, p => p[2] > 128 && p[0] < 64 && p[1] < 64), "Cardinal label was masked.");
            });
            Check(mode + "/RasterRenderer_ConsecutiveCachedFrames_RenderThroughWpfBinding", () => {
                using var renderer = new SkyMapRasterRenderer(100, 100);
                using var annotator = new SkyMapAnnotator();
                var image = new Image { Width = 100, Height = 100 };
                BindingOperations.SetBinding(image, Image.SourceProperty, new Binding(nameof(SkyMapAnnotator.SkyMapOverlay)) { Source = annotator });
                annotator.SkyMapOverlay = renderer.Render(Empty(), [new SkyMapImagePlacement(Pixel(0, 0, 255), new Point(25, 50), 20, 20, 0)], null);
                Require(At(Frame(image), 100, 25, 50).SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Initial bound tile is absent.");
                ImageSource first = image.Source;
                annotator.SkyMapOverlay = renderer.Render(Empty(), [new SkyMapImagePlacement(Pixel(255, 0, 0), new Point(75, 50), 20, 20, 0)], null);
                byte[] pixels = Frame(image);
                Require(ReferenceEquals(image.Source, annotator.SkyMapOverlay) && (software ? ReferenceEquals(first, image.Source) : !ReferenceEquals(first, image.Source)), "Binding publication contract changed.");
                Require(At(pixels, 100, 25, 50)[3] == 0 && At(pixels, 100, 75, 50).SequenceEqual(new byte[] { 255, 0, 0, 255 }), "Bound tile pixels did not refresh.");
            });
        }
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        foreach (var size in new[] { (1, 1), (3, 5), (1200, 800) }) Check($"SoftwareOnly/PreservesViewportCoordinateSpaceDuringInteraction-{size}", () => {
            using var renderer = new SkyMapRasterRenderer(size.Item1, size.Item2);
            var preview = (BitmapSource)renderer.Render(Empty(), [], null, SkyMapRenderQuality.InteractionPreview);
            var final = (BitmapSource)renderer.Render(Empty(), [], null, SkyMapRenderQuality.Final);
            Require(preview.PixelWidth == size.Item1 && preview.PixelHeight == size.Item2 && ReferenceEquals(preview, final), "Half-preview viewport dimensions or identity changed.");
        });
        Check("SoftwareOnly/PreviewKeepsCachedTargetAlignedWithViewport", () => {
            using var renderer = new SkyMapRasterRenderer(100, 100);
            var pixels = Pixels((BitmapSource)renderer.Render(Empty(), [new SkyMapImagePlacement(Pixel(0, 0, 255), new Point(20, 30), 20, 10, 0)], null, SkyMapRenderQuality.InteractionPreview));
            Require(At(pixels, 100, 20, 30).SequenceEqual(new byte[] { 0, 0, 255, 255 }) && At(pixels, 100, 40, 60)[3] == 0, "Half-preview changed viewport coordinates.");
        });
        Check("SoftwareOnly/PreviewPreservesDsoAndFollowingLabelTransform", () => {
            using var renderer = new SkyMapRasterRenderer(100, 100);
            var scene = new SkyMapScene([], [], [new SkyMapDeepSkyObject("", "BRTNB", new Point(20, 75), 6, 4, 30), new SkyMapDeepSkyObject("", "GLOCL", new Point(75, 75), 8, 8, 0)], [], [], [new SkyMapLabel("STAR", new Point(75, 20), SkyMapLabelKind.Star)]);
            foreach (var quality in new[] { SkyMapRenderQuality.Final, SkyMapRenderQuality.InteractionPreview }) {
                byte[] pixels = Pixels((BitmapSource)renderer.Render(scene, [], null, quality));
                Require(At(pixels, 100, 75, 75)[3] > 0 && Region(pixels, 100, 55, 15, 40, 25, p => p[3] > 0), "Nested DSO rotation discarded the incoming preview transform.");
            }
        });
        Check("SoftwareOnly/MutableSourceSnapshotAndFlipKeys", () => {
            using var renderer = new SkyMapRasterRenderer(32, 32);
            var mutable = new WriteableBitmap(Pixel(0, 0, 255));
            var normal = new SkyMapImagePlacement(mutable, new Point(16, 16), 20, 20, 0);
            Require(At(Pixels((BitmapSource)renderer.Render(Empty(), [normal], null)), 32, 16, 16)[2] == 255, "Initial snapshot absent.");
            mutable.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 255, 0, 0, 255 }, 4, 0);
            Require(At(Pixels((BitmapSource)renderer.Render(Empty(), [normal], null)), 32, 16, 16)[2] == 255, "Existing cache key reread mutable pixels.");
            Require(At(Pixels((BitmapSource)renderer.Render(Empty(), [normal with { FlipHorizontally = true }], null)), 32, 16, 16)[0] == 255, "Flip key did not acquire its own snapshot.");
            for (int i = 0; i < 64; i++) renderer.Render(Empty(), [normal with { Image = Pixel((byte)i, 0, 0) }], null);
            Require(At(Pixels((BitmapSource)renderer.Render(Empty(), [normal], null)), 32, 16, 16)[0] == 255, "64-entry LRU did not evict the original snapshot.");
        });
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        Check("Default/MutableCachedSourceRemainsLiveInDrawingImage", () => {
            using var renderer = new SkyMapRasterRenderer(32, 32);
            var mutable = new WriteableBitmap(Pixel(0, 0, 255));
            ImageSource composite = renderer.Render(Empty(), [new SkyMapImagePlacement(mutable, new Point(16, 16), 20, 20, 0)], null);
            Require(At(Frame(composite, 32, 32), 32, 16, 16)[2] == 255, "Initial live tile absent.");
            mutable.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 255, 0, 0, 255 }, 4, 0);
            Require(At(Frame(composite, 32, 32), 32, 16, 16)[0] == 255, "Hardware/default composition stopped observing mutable source pixels.");
        });
    }

    private static BitmapSource Pixel(byte b, byte g, byte r) { var value = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { b, g, r, 255 }, 4); value.Freeze(); return value; }
    private static BitmapSource Asymmetric() { var bytes = new byte[20 * 10 * 4]; for (int y = 0; y < 10; y++) for (int x = 0; x < 20; x++) { int i = (y * 20 + x) * 4; bytes[i] = x < 10 ? (byte)0 : (byte)255; bytes[i + 2] = x < 10 ? (byte)255 : (byte)0; bytes[i + 3] = 255; } var value = BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgra32, null, bytes, 80); value.Freeze(); return value; }
    private static byte[] At(byte[] pixels, int width, int x, int y) => pixels.AsSpan((y * width + x) * 4, 4).ToArray();
    private static bool Region(byte[] pixels, int width, int x, int y, int w, int h, Func<byte[], bool> test) { for (int row = y; row < y + h; row++) for (int column = x; column < x + w; column++) if (test(At(pixels, width, column, row))) return true; return false; }
    private static byte[] Frame(ImageSource source, int width = 100, int height = 100) { var image = new Image { Source = source, Width = width, Height = height }; return Frame(image, width, height); }
    private static byte[] Frame(Image image, int width = 100, int height = 100) { image.Measure(new Size(width, height)); image.Arrange(new Rect(0, 0, width, height)); image.UpdateLayout(); var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); target.Render(image); return Pixels(target); }
}
