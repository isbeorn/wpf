using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections;
using System.Reflection;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.WPF.Base.SkySurvey;

internal static partial class Program {
    private static void CacheCases(bool recordReference) {
        string fixture = Path.Combine(referenceDirectory, "cache-tiles");
        string state = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referenceDirectory))!, "cache-state-" + (OperatingSystem.IsWindows() ? "windows-" : "linux-") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        CoreUtil.APPLICATIONTEMPPATH = state;
        Check("original-cache-fixtures", () => {
            if (recordReference && !Directory.Exists(fixture)) {
                var cache = new CacheSkySurvey(fixture);
                foreach (var entry in new[] { ("FirstLeft", 23.9), ("FirstCenter", 0d), ("FirstRight", .1), ("SecondLeft", 11.9), ("SecondCenter", 12d), ("SecondRight", 12.1) }) {
                    cache.SaveImageToCache(new SkySurveyImage { Id = Guid.NewGuid(), Name = entry.Item1, Source = "NASASkySurvey", Image = Asymmetric(), Coordinates = new Coordinates(entry.Item2, 0, Epoch.J2000, Coordinates.RAType.Hours), FoVWidth = 60, FoVHeight = 60, Rotation = 17 });
                }
            }
            Require(File.Exists(Path.Combine(fixture, "CacheInfo.xml")), "Frozen original Windows cache fixture absent.");
            foreach (string source in Directory.GetFiles(fixture)) File.Copy(source, Path.Combine(state, Path.GetFileName(source)));
        });
        var first = new ViewportFoV(new Coordinates(0, 0, Epoch.J2000, Coordinates.RAType.Degrees), 5, 400, 400, 0);
        var second = new ViewportFoV(new Coordinates(180, 0, Epoch.J2000, Coordinates.RAType.Degrees), 5, 400, 400, 0);
        Check("SkyMapImageCache_WhenPanningRepeatedly_KeepsActiveTilesWithoutAccumulatingHistory", () => {
            var original = new CacheSkySurvey(state);
            using var cache = new SkyMapImageCache(original, decodedImageCapacity: 1, maximumEstimatedBytes: 1);
            foreach (var viewport in new[] { first, second, first, second }) {
                cache.LoadAsync(viewport, CancellationToken.None).GetAwaiter().GetResult();
                var placements = cache.GetPlacements(new SkyMapViewportProjection(viewport));
                Require(placements.Count == 3 && cache.DecodedImageCount == 3, "Visible tiles were evicted or history accumulated.");
                Require(placements.All(p => p.Image.IsFrozen && p.Image.PixelWidth == 75 && double.IsFinite(p.Rotation)), "Original thumbnail decode, freeze or projection contract changed.");
            }
            Require(cache.GetPlacements(new SkyMapViewportProjection(first)).Count == 0, "Inactive least-recently-used tiles were retained outside the budget.");
        });
        Check("SkyMapImageCache_WhenByteBudgetIsExceeded_EvictsLeastRecentlyUsedImage", () => {
            var original = new CacheSkySurvey(state);
            using var cache = new SkyMapImageCache(original, decodedImageCapacity: 32, maximumEstimatedBytes: 75L * 75 * 4 * 3);
            cache.LoadAsync(first, CancellationToken.None).GetAwaiter().GetResult();
            cache.LoadAsync(second, CancellationToken.None).GetAwaiter().GetResult();
            Require(cache.EstimatedBytes <= cache.MaximumEstimatedBytes && cache.DecodedImageCount == 3 && cache.GetPlacements(new SkyMapViewportProjection(first)).Count == 0, "Byte budget did not evict inactive tiles.");
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            using var renderer = new SkyMapRasterRenderer(400, 400);
            byte[] pixels = Pixels((System.Windows.Media.Imaging.BitmapSource)renderer.Render(Empty(), cache.GetPlacements(new SkyMapViewportProjection(second)), null));
            Require(pixels.Where((_, i) => i % 4 == 3).Any(a => a > 0), "Actual decoded cached survey images did not render.");
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        });
        Check("SkyMapImageCache_RendererDoesNotRetainEvictedDecodedTiles", () => {
            var previousMode = RenderOptions.ProcessRenderMode;
            try {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                using var renderer = new SkyMapRasterRenderer(400, 400);
                using var cache = new SkyMapImageCache(new CacheSkySurvey(state), decodedImageCapacity: 1, maximumEstimatedBytes: 1);
                BitmapSource external = Pixel(0, 255, 0);
                renderer.Render(Empty(), [new SkyMapImagePlacement(external, new Point(200, 200), 20, 20, 0)], null);
                cache.LoadAsync(first, CancellationToken.None).GetAwaiter().GetResult();
                var firstPlacements = cache.GetPlacements(new SkyMapViewportProjection(first));
                BitmapSource[] firstSources = firstPlacements.Select(p => p.Image).ToArray();
                Require(firstSources.Length == 3, "Initial decoded viewport tiles are absent.");
                var initialFrame = (BitmapSource)renderer.Render(Empty(), firstPlacements, null);
                Require(Pixels(initialFrame).Where((_, i) => i % 4 == 3).Any(a => a > 0), "First decoded viewport did not render.");
                cache.LoadAsync(second, CancellationToken.None).GetAwaiter().GetResult();
                var nextPlacements = cache.GetPlacements(new SkyMapViewportProjection(second));
                Require(cache.GetPlacements(new SkyMapViewportProjection(first)).Count == 0 && nextPlacements.Count == 3, "Panning did not evict the first viewport.");
                var nextFrame = (BitmapSource)renderer.Render(Empty(), nextPlacements, null);
                Require(ReferenceEquals(initialFrame, nextFrame) && Pixels(nextFrame).Where((_, i) => i % 4 == 3).Any(a => a > 0), "The same renderer did not draw the next viewport.");
                BitmapSource[] retained = RendererSnapshotSources(renderer);
                Require(retained.Any(source => ReferenceEquals(source, external)), "External snapshot ownership control is absent.");
                Require(!retained.Any(source => firstSources.Any(evicted => ReferenceEquals(source, evicted))), "Renderer snapshot ownership retains evicted decoded cache tiles.");
            } finally { RenderOptions.ProcessRenderMode = previousMode; }
        });
        Check("SkyMapImageCache_CancellationRecoveryAndDispose", () => {
            using var cache = new SkyMapImageCache(new CacheSkySurvey(state));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool observed = false;
            try { cache.LoadAsync(first, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { observed = true; }
            Require(observed && cache.DecodedImageCount == 0 && cache.EstimatedBytes == 0, "Canceled load published decoded tiles.");
            cache.LoadAsync(first, CancellationToken.None).GetAwaiter().GetResult();
            Require(cache.GetPlacements(new SkyMapViewportProjection(first)).Count == 3, "Same cache could not recover from cancellation.");
            cache.Dispose(); cache.Dispose();
            Require(cache.DecodedImageCount == 0 && cache.EstimatedBytes == 0 && cache.GetPlacements(new SkyMapViewportProjection(first)).Count == 0, "Disposal did not clear decoded image ownership.");
            bool disposed = false;
            try { cache.LoadAsync(first, CancellationToken.None).GetAwaiter().GetResult(); } catch (ObjectDisposedException) { disposed = true; }
            Require(disposed, "Original post-disposal load contract changed.");
        });
        Check("CacheSkySurvey_SaveImageToCache_GetImage_Roundtrip", () => {
            var cache = new CacheSkySurvey(Path.Combine(state, "roundtrip"));
            foreach (bool gray in new[] { false, true }) {
                const int width = 96, height = 64;
                ushort[] words = new ushort[width * height];
                byte[] color = new byte[width * height * 4];
                ushort[] levels = [0, 1, 255, 256, 257, 32768, 65280, 65535];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                    words[y * width + x] = levels[x * levels.Length / width];
                    int i = (y * width + x) * 4;
                    color[i] = x < width / 2 ? (byte)20 : y < height / 2 ? (byte)220 : (byte)200;
                    color[i + 1] = y < height / 2 ? (byte)30 : x < width / 2 ? (byte)220 : (byte)200;
                    color[i + 2] = x < width / 2 && y < height / 2 ? (byte)220 : x >= width / 2 && y >= height / 2 ? (byte)200 : (byte)20;
                    color[i + 3] = 255;
                }
                var source = gray
                    ? BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, words, width * 2)
                    : BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, color, width * 4);
                source.Freeze();
                byte[] sourceBefore = SourceSamples(source);
                ushort[] wordsBefore = words.ToArray(); byte[] colorBefore = color.ToArray();
                var image = new SkySurveyImage { Id = Guid.NewGuid(), Name = gray ? "Roundtrip Gray16" : "Roundtrip Color", Source = "FileSkySurvey", Image = source,
                    Coordinates = new Coordinates(gray ? 6.123456789 : 5.123456789, -11.5, Epoch.J2000, Coordinates.RAType.Hours), FoVWidth = 120.25, FoVHeight = 80.75, Rotation = 37.25 };
                var element = cache.SaveImageToCache(image);
                var byId = cache.GetImage(image.Id).GetAwaiter().GetResult();
                var byCoordinates = cache.GetImage(image.Source, image.Coordinates.RA, image.Coordinates.Dec, image.Rotation, image.FoVWidth).GetAwaiter().GetResult();
                foreach (var restored in new[] { byId, byCoordinates }) {
                    Require(restored != null && restored.Id == image.Id && restored.Name == image.Name && restored.Rotation == image.Rotation && restored.FoVWidth == image.FoVWidth && restored.FoVHeight == image.FoVHeight, "GetImage did not restore original cache metadata.");
                    Require(Math.Abs(restored!.Coordinates.RA - image.Coordinates.RA) <= 1e-12 && Math.Abs(restored.Coordinates.Dec - image.Coordinates.Dec) <= 1e-12, "Cache coordinates changed beyond the original roundtrip test tolerance.");
                    Require(restored.Image.IsFrozen && restored.Image.PixelWidth == width && restored.Image.PixelHeight == height && restored.Image.DpiX == 96, "Original LoadJpg bitmap ownership, dimensions or DPI changed.");
                    CheckSurveyContent(restored.Image, gray);
                }
                Require(element.Attribute("Source")!.Value == image.Source && element.Attribute("Id")!.Value == image.Id.ToString(), "Persisted source or ID metadata changed.");
                string original = Path.Combine(cache.framingAssistantCachePath, element.Attribute("FileName")!.Value);
                foreach (int size in new[] { CacheImage.BigThumbnailSize, CacheImage.MediumThumbnailSize, CacheImage.SmallThumbnailSize }) {
                    string path = CacheImage.GetImagePathForThumbnail(original, size);
                    Require(File.Exists(path), "Original cache thumbnail is absent.");
                    var decoder = new JpegBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapFrame thumbnail = decoder.Frames[0];
                    Require(thumbnail.PixelWidth == size && thumbnail.PixelHeight == size, "Independent square thumbnail scaling changed.");
                    CheckSurveyContent(thumbnail, gray);
                }
                Require(SourceSamples(source).SequenceEqual(sourceBefore) && words.SequenceEqual(wordsBefore) && color.SequenceEqual(colorBefore), "Cache save/load or thumbnail transforms changed scientific source samples.");
            }
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ReproduceLoadJpgGuard() {
        Check("actual-CacheSkySurvey-GetImage-LoadJpg-old-family-guard", () => {
            var cache = new CacheSkySurvey(Path.Combine(referenceDirectory, "cache-tiles"));
            Guid id = Guid.Parse(cache.Cache.Elements("Image").First().Attribute("Id")!.Value);
            Exception? failure = null;
            try { cache.GetImage(id).GetAwaiter().GetResult(); } catch (Exception error) { failure = error; }
            Require(failure is NotSupportedException && failure.StackTrace!.Contains("CacheSkySurvey.LoadJpg", StringComparison.Ordinal), "Expected actual LoadJpg prerequisite guard was not reached: " + failure);
            cases.Add(new { actualLoadJpgGuard = failure!.ToString() });
        });
    }

    private static byte[] SourceSamples(BitmapSource source) {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var bytes = new byte[stride * source.PixelHeight]; source.CopyPixels(bytes, stride, 0); return bytes;
    }

    private static void CheckSurveyContent(BitmapSource source, bool gray) {
        byte[] pixels = Frame(source, source.PixelWidth, source.PixelHeight);
        (byte R, byte G, byte B) ColorAt(double x, double y) {
            byte[] bytes = At(pixels, source.PixelWidth, (int)(source.PixelWidth * x), (int)(source.PixelHeight * y));
            return (bytes[2], bytes[1], bytes[0]);
        }
        if (gray) {
            byte low = ColorAt(.0625, .5).R, middle = ColorAt(.6875, .5).R, high = ColorAt(.9375, .5).R;
            Require(low < middle && middle < high, "Gray16 cache content lost its ordered dark/middle/bright bands.");
        } else {
            var red = ColorAt(.25, .25); var blue = ColorAt(.75, .25); var green = ColorAt(.25, .75);
            Require(red.R > red.G && red.R > red.B && blue.B > blue.R && blue.B > blue.G && green.G > green.R && green.G > green.B, "Asymmetric JPEG cache content was lost, rotated or mirrored.");
        }
    }

    private static BitmapSource[] RendererSnapshotSources(SkyMapRasterRenderer renderer) {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object owner = renderer;
        string cacheName = "rasterCache";
        if (OperatingSystem.IsLinux()) {
            owner = typeof(SkyMapRasterRenderer).GetField("portableDrawing", fields)!.GetValue(renderer)!;
            cacheName = "snapshots";
        }
        // Inspect existing ownership keys directly; collection lifetime does not depend on a GC schedule.
        var cache = (IDictionary)owner.GetType().GetField(cacheName, fields)!.GetValue(owner)!;
        return cache.Keys.Cast<object>().Select(key => (BitmapSource)(key.GetType().GetProperty("Image")?.GetValue(key) ?? key.GetType().GetField("Item1")!.GetValue(key)!)).ToArray();
    }
}
