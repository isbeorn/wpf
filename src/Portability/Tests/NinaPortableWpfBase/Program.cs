using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.WPF.Base.SkySurvey;

internal static partial class Program {
    private static readonly List<object> cases = [];
    private static int failures;
    private static string referenceDirectory = "";

    [STAThread]
    private static int Main(string[] args) {
        if (args.Length != 9) throw new ArgumentException("Expected report, WpfBase, Core, Image, Astrometry, original managed directory, framework directory, mode and reference directory.");
        referenceDirectory = args[8];
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string? path = name.Name switch { "NINA.WPF.Base" => args[1], "NINA.Core" => args[2], "NINA.Image" => args[3], "NINA.Astrometry" => args[4], _ => null };
            if (path != null) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            foreach (string directory in new[] { args[6], args[5] }) {
                path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        if (args[7] == "red") ReproduceOriginalGdi();
        else if (args[7] == "loadjpg-red") ReproduceLoadJpgGuard();
        else if (args[7] == "cache") CacheCases(false);
        else RunCases(args[7] == "reference");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => new { name = a.GetName().Name, path = a.Location, sha256 = Hash(File.ReadAllBytes(a.Location)) });
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { status = failures == 0 ? "pass" : "fail", scope = "Focused original sky-map renderer and cache qualification; framing VM integration is separate", cases, assemblies }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReproduceOriginalGdi() {
        Check("original-renderer-GDI-failure-before-first-render", () => {
            Exception? failure = null;
            try { using var renderer = new SkyMapRasterRenderer(16, 16); renderer.Render(Empty(), [], null); }
            catch (Exception error) { failure = error; }
            Require(failure != null && (failure.ToString().Contains("gdiplus", StringComparison.OrdinalIgnoreCase) || failure.ToString().Contains("System.Drawing.Common is not supported", StringComparison.Ordinal)), "Original GDI failure was not reproduced: " + failure);
            cases.Add(new { originalFailure = failure!.ToString() });
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCases(bool recordReference) {
        Directory.CreateDirectory(referenceDirectory);
        RendererCases(recordReference);
        foreach (var mode in new[] { System.Windows.Interop.RenderMode.Default, System.Windows.Interop.RenderMode.SoftwareOnly }) {
            RenderOptions.ProcessRenderMode = mode;
            foreach (var size in new[] { (16, 16), (17, 13), (100, 100) }) {
                using var renderer = new SkyMapRasterRenderer(size.Item1, size.Item2);
                var mask = new SkyMapPath([new Point(0, 0), new Point(size.Item1, 0), new Point(size.Item1, size.Item2), new Point(0, size.Item2)], true);
                var scene = new SkyMapScene([], [], [], [], [], horizonMaskAreas: [mask]);
                foreach (var quality in new[] { SkyMapRenderQuality.Final, SkyMapRenderQuality.InteractionPreview }) {
                    string name = $"hatch-{mode}-{quality}-{size.Item1}x{size.Item2}";
                    Check(name, () => {
                        byte[] pixels = Pixels((BitmapSource)renderer.Render(scene, [], null, quality));
                        string path = Path.Combine(referenceDirectory, name + ".bin");
                        if (recordReference) File.WriteAllBytes(path, pixels);
                        Require(pixels.Where((_, i) => i % 4 == 3).All(a => a == 255), "Horizon mask is not opaque.");
                        if (!recordReference && OperatingSystem.IsWindows()) Require(pixels.SequenceEqual(File.ReadAllBytes(path)), "Original Windows hatch bytes changed.");
                        if (!recordReference && !OperatingSystem.IsWindows()) {
                            byte[] original = File.ReadAllBytes(path);
                            byte maximum = pixels.Where((_, i) => i % 4 == 2).Max();
                            byte originalMaximum = original.Where((_, i) => i % 4 == 2).Max();
                            for (int i = 2; i < pixels.Length; i += 4) Require((pixels[i] == maximum) == (original[i] == originalMaximum), "Calibrated device-space hatch phase, spacing or nearest expansion changed.");
                        }
                        if (mode == System.Windows.Interop.RenderMode.SoftwareOnly && quality == SkyMapRenderQuality.InteractionPreview) Require(pixels.Chunk(4).Select(p => Convert.ToHexString(p)).Distinct().Count() <= 3, "Preview interpolated hatch colors instead of nearest-neighbor expansion.");
                        if (!recordReference) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referenceDirectory))!, name + (OperatingSystem.IsWindows() ? "-windows" : "-linux") + ".bin"), pixels);
                        cases.Add(new { hatch = name, sha256 = Hash(pixels), colors = pixels.Chunk(4).Select(p => Convert.ToHexString(p)).Distinct().Order().ToArray() });
                    });
                }
            }
        }
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        CacheCases(recordReference);
    }

    private static SkyMapScene Empty() => new([], [], [], [], []);
    private static byte[] Pixels(BitmapSource source) { int stride = source.PixelWidth * 4; var bytes = new byte[stride * source.PixelHeight]; source.CopyPixels(bytes, stride, 0); return bytes; }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Check(string name, Action test) { try { test(); cases.Add(new { name, status = "pass" }); } catch (Exception error) { failures++; cases.Add(new { name, status = "fail", error = error.ToString() }); } }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
