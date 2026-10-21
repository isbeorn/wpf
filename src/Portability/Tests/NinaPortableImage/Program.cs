using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Image.ImageAnalysis;

internal static partial class Program {
    private static readonly List<object> cases = new();
    private static int failures;

    [STAThread]
    private static int Main(string[] args) {
        if (args.Length != 5 && args.Length != 7) throw new ArgumentException("Expected report, Image DLL, Core DLL, stock directory, framework directory and optional qualification mode/work directory.");
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string? candidate = name.Name == "NINA.Image" ? args[1] : name.Name == "NINA.Core" ? args[2] : null;
            if (candidate != null) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(candidate));
            foreach (var directory in new[] { args[4], args[3] }) {
                var path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        if (args.Length == 5) RunAnnotationCases();
        else if (args[5] == "fits") RunFitsCases(args[6]);
        else if (args[5] == "fits-legacy-reference" || args[5] == "fits-legacy-compare") RunLegacyFitsCase(args[6], args[5] == "fits-legacy-reference");
        else RunProcessingCases(args[6], args[5] == "science-reference", args[5] == "science-package-compare");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => new { name = a.GetName().Name, path = a.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) });
        var nativeLibraries = args.Length == 7 && args[5] == "fits" ? FitsNativeLibraries() : [];
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { status = failures == 0 ? "pass" : "fail", scope = "NINA.Image focused component; no application or acquisition qualification", cases, assemblies, nativeLibraries }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunAnnotationCases() {
        Check("actual-StarAnnotator-construction-and-empty-Gray16-background", () => {
            var annotator = new StarAnnotator();
            Require(annotator.Name == "NINA" && annotator.ContentId == typeof(StarAnnotator).FullName, "Plugin identity changed.");
            ushort[] raw = [0, 1, 255, 256, 32768, 65535, 257, 511, 512, 65280];
            var before = raw.ToArray();
            var source = Source(raw, 5, 2, PixelFormats.Gray16);
            var output = Annotate(source, new StarDetectionParams(), []);
            Require(output.Format == PixelFormats.Bgr24 && output.IsFrozen && output.PixelWidth == 5 && output.PixelHeight == 2, "Output contract changed.");
            Require(Pixels(output).SequenceEqual(raw.SelectMany(value => Enumerable.Repeat((byte)(value >> 8), 3))), "Gray16 high-byte background changed.");
            Require(raw.SequenceEqual(before) && Words(source).SequenceEqual(before), "Annotation modified source samples.");
            Require(Task.Run(() => Pixels(output)).Result.SequenceEqual(Pixels(output)), "Frozen output cannot cross threads.");
        });
        Check("Rgb48-original-grayscale-coefficients-and-odd-width-background", () => {
            ushort[] raw = [65535, 0, 0, 0, 65535, 0, 0, 0, 65535, 256, 512, 768, 65535, 65535, 65535, 0, 0, 0];
            var before = raw.ToArray();
            var source = Source(raw, 3, 2, PixelFormats.Rgb48);
            var output = Annotate(source, new StarDetectionParams(), []);
            var expected = Enumerable.Range(0, 6).SelectMany(index => {
                int i = index * 3;
                byte gray = (byte)(((ushort)(0.0721 * raw[i] + 0.7154 * raw[i + 1] + 0.2125 * raw[i + 2])) >> 8);
                return Enumerable.Repeat(gray, 3);
            });
            Require(Pixels(output).SequenceEqual(expected), "Rgb48 original stored channel coefficients changed.");
            Require(raw.SequenceEqual(before) && Words(source).SequenceEqual(before), "Rgb48 samples changed.");
        });
        Check("real-star-geometry-labels-maxStars-and-result-list-ownership", () => {
            var source = Source(new ushort[240 * 140], 240, 140, PixelFormats.Gray16);
            var faint = Star(40, 40, 1, 2.25);
            var bright = Star(170, 40, 20, 4.5);
            var list = new List<DetectedStar> { faint, bright };
            var limited = Annotate(source, new StarDetectionParams(), list, 1);
            var unlimited = Annotate(source, new StarDetectionParams(), list, -1);
            Require(Colored(limited, 155, 25, 35, 35) > 0 && Colored(limited, 25, 25, 35, 35) == 0, "maxStars did not select the brightest star.");
            Require(Colored(unlimited, 25, 25, 35, 35) > 0, "Unlimited annotation omitted a star.");
            Require(Yellow(limited, 140, 50, 90, 45) > 0, "HFR label pixels are absent.");
            Require(list.Count == 2 && ReferenceEquals(list[0], faint) && ReferenceEquals(list[1], bright), "Annotation sorted the caller's list.");
            Require(Words(source).All(value => value == 0), "Drawing modified the source.");
        });
        Check("inner-and-outer-ROI-render-real-rectangles", () => {
            var source = Source(new ushort[120 * 100], 120, 100, PixelFormats.Gray16);
            var inner = Annotate(source, new StarDetectionParams { UseROI = true, InnerCropRatio = 0.5, OuterCropRatio = 1 }, []);
            var both = Annotate(source, new StarDetectionParams { UseROI = true, InnerCropRatio = 0.5, OuterCropRatio = 0.8 }, []);
            Require(Colored(inner, 28, 23, 5, 5) > 0, "Inner crop border is absent.");
            Require(Colored(inner, 10, 8, 5, 5) == 0 && Colored(both, 10, 8, 5, 5) > 0, "Outer crop condition or geometry changed.");
            Require(Pixels(Annotate(source, new StarDetectionParams { UseROI = false }, [])).All(value => value == 0), "Disabled ROI still draws.");
        });
        Check("cancellation-reaches-actual-star-loop", () => {
            var source = Source(new ushort[80 * 80], 80, 80, PixelFormats.Gray16);
            using var token = new CancellationTokenSource(); token.Cancel();
            try { _ = new StarAnnotator().GetAnnotatedImage(new StarDetectionParams(), new StarDetectionResult { StarList = [Star(40, 40, 1, 3)] }, source, token: token.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Canceled annotation completed.");
        });
    }

    private static DetectedStar Star(int x, int y, double brightness, double hfr) => new() {
        Position = new Accord.Point(x, y), BoundingBox = new System.Drawing.Rectangle(x - 10, y - 10, 20, 20), AverageBrightness = brightness, HFR = hfr
    };
    private static BitmapSource Annotate(BitmapSource source, StarDetectionParams parameters, List<DetectedStar> stars, int maxStars = 200) =>
        new StarAnnotator().GetAnnotatedImage(parameters, new StarDetectionResult { StarList = stars }, source, maxStars).GetAwaiter().GetResult();
    private static BitmapSource Source(ushort[] raw, int width, int height, PixelFormat format) {
        var source = BitmapSource.Create(width, height, 96, 96, format, null, raw, width * format.BitsPerPixel / 8); source.Freeze(); return source;
    }
    private static ushort[] Words(BitmapSource source) { var result = new ushort[source.PixelWidth * source.PixelHeight * source.Format.BitsPerPixel / 16]; source.CopyPixels(result, source.PixelWidth * source.Format.BitsPerPixel / 8, 0); return result; }
    private static byte[] Pixels(BitmapSource source) { var result = new byte[source.PixelWidth * source.PixelHeight * 3]; source.CopyPixels(result, source.PixelWidth * 3, 0); return result; }
    private static int Colored(BitmapSource source, int x, int y, int width, int height) {
        byte[] pixels = Pixels(source); int count = 0;
        for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) { int i = (yy * source.PixelWidth + xx) * 3; if (pixels[i] != pixels[i + 1] || pixels[i + 1] != pixels[i + 2]) count++; }
        return count;
    }
    private static int Yellow(BitmapSource source, int x, int y, int width, int height) {
        byte[] pixels = Pixels(source); int count = 0;
        for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) { int i = (yy * source.PixelWidth + xx) * 3; if (pixels[i + 1] > 0 && pixels[i + 2] > 0 && pixels[i] < pixels[i + 1] / 2) count++; }
        return count;
    }
    private static void Check(string name, Action action) { try { action(); cases.Add(new { name, status = "pass" }); } catch (Exception error) { failures++; cases.Add(new { name, status = "fail", error = error.ToString() }); } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
