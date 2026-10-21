using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Core.Enum;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using DrawingFormat = System.Drawing.Imaging.PixelFormat;

internal static partial class Program {
    private static readonly JsonSerializerOptions scienceJson = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, IncludeFields = true, WriteIndented = true };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunProcessingCases(string referenceDirectory, bool writeReference, bool packageRuntime = false) {
        if (writeReference && !OperatingSystem.IsWindows()) throw new InvalidOperationException("Only the original Windows implementation supplies references.");
        Directory.CreateDirectory(referenceDirectory);
        var state = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(referenceDirectory))!, "science-state-" + (writeReference ? "stock" : "portable"));
        Directory.CreateDirectory(state);
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = state;
        foreach (var fixture in new[] { ("empty", 32, 32), ("uniform", 32, 32), ("circular", 32, 32), ("elliptical", 48, 48), ("catalog", 640, 480) }) {
            Check("real-stretch-High-None-detect-" + fixture.Item1, () => ScienceImage(referenceDirectory, writeReference, fixture.Item1, fixture.Item2, fixture.Item3, NoiseReductionEnum.None));
        }
        foreach (var noise in new[] { NoiseReductionEnum.None, NoiseReductionEnum.Median, NoiseReductionEnum.Normal, NoiseReductionEnum.High, NoiseReductionEnum.Highest }) {
            Check("large-image-real-resize-detect-" + noise, () => ScienceImage(referenceDirectory, writeReference, "large-" + noise, 1600, 96, noise));
        }
        foreach (var pattern in new[] { SensorType.RGGB, SensorType.RGBG, SensorType.GRGB, SensorType.GRBG, SensorType.GBGR, SensorType.GBRG, SensorType.BGRG, SensorType.BGGR }) {
            Check("real-debayer-linked-unlinked-and-luminance-" + pattern, () => ColorImage(referenceDirectory, writeReference, pattern));
        }
        Check("real-Rgb48-stretch-High-None-detect", () => ColorDetection(referenceDirectory, writeReference));
        Check("real-Detect-cancel-at-structures-preserves-partial-result-and-recovers", () => DetectionCancellation(referenceDirectory));
        Check("odd-width-stretch-processes-every-real-sample", () => OddWidthStretch(OperatingSystem.IsWindows()));
        Check("odd-width-Rgb48-linked-and-unlinked-process-every-sample", () => OddWidthColorStretch(OperatingSystem.IsWindows()));
        if (packageRuntime) {
            Check("package-public-Detect-Gray16-default-High-Highest", () => PackageDetection(referenceDirectory, false));
            Check("package-public-Detect-Rgb48-default-High-Highest", () => PackageDetection(referenceDirectory, true));
        } else {
            Check("public-contract-matches-original", () => PublicContract(referenceDirectory, writeReference));
        }
        Check("original-analytic-circular-measurement", () => AnalyticMeasurement(false));
        Check("original-analytic-elliptical-measurement", () => AnalyticMeasurement(true));
        if (packageRuntime) Check("package-runtime-does-not-load-Windows-GDI-contracts", () => {
            var names = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name);
            Require(!names.Contains("System.Private.Windows.GdiPlus"), "Windows GDI was loaded by package image processing.");
        });
    }

    private static void PackageDetection(string directory, bool color) {
        const int width = 640, height = 480;
        var raw = new ushort[width * height];
        Buffer.BlockCopy(File.ReadAllBytes(Path.Combine(directory, "catalog.raw")), 0, raw, 0, raw.Length * 2);
        var before = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, color, new ImageMetaData(), null!, detection, null!);
        var rendered = data.RenderImage();
        if (color) rendered = rendered.Debayer(true, true, SensorType.RGGB);
        var image = rendered.Stretch(0.2, -2.8, color).GetAwaiter().GetResult();
        var pixelsBefore = Words(image.Image);
        foreach (var parameters in new[] { new StarDetectionParams(), new StarDetectionParams { Sensitivity = StarSensitivityEnum.High }, new StarDetectionParams { Sensitivity = StarSensitivityEnum.Highest } }) {
            var statuses = new List<string>();
            var progress = new SynchronousProgress(status => statuses.Add(status.Status));
            var result = detection.Detect(image, image.Image.Format, parameters, progress, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StarList?.Count > 0 && result.DetectedStars > 0 && double.IsFinite(result.AverageHFR) && double.IsFinite(result.AverageFWHM), "Public detection omitted stars or finite measurements.");
            Require(statuses.Contains("Analyzing stars") && statuses[^1] == string.Empty, "Public detection progress did not complete.");
            Require(raw.SequenceEqual(before) && data.Data.FlatArray.SequenceEqual(before) && Words(image.Image).SequenceEqual(pixelsBefore), "Public detection changed raw samples or rendered pixels.");
        }
    }

    private static void ScienceImage(string directory, bool write, string name, int width, int height, NoiseReductionEnum noise) {
        string inputPath = Path.Combine(directory, name + ".raw");
        ushort[] raw;
        if (write) { raw = SensorFixture(name, width, height); File.WriteAllBytes(inputPath, Bytes(raw)); }
        else { raw = new ushort[width * height]; Buffer.BlockCopy(File.ReadAllBytes(inputPath), 0, raw, 0, raw.Length * 2); }
        var original = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, false, new ImageMetaData(), null!, detection, null!);
        var source = data.RenderImage();
        var statistics = data.Statistics.Task.GetAwaiter().GetResult();
        var stretched = ImageUtility.Stretch(source, 0.2, -2.8).GetAwaiter().GetResult();
        Require(stretched.Format == PixelFormats.Gray16 && stretched.IsFrozen, "Stretch output contract changed.");
        var wrapped = new RenderedImage(stretched, data, null!, detection, null!);
        var result = detection.Detect(wrapped, PixelFormats.Gray16, new StarDetectionParams { Sensitivity = StarSensitivityEnum.High, NoiseReduction = noise }, null!, CancellationToken.None).GetAwaiter().GetResult();
        Require(raw.SequenceEqual(original) && Words(source.Image).SequenceEqual(original), "Processing altered scientific samples.");
        var snapshot = new { statistics.Mean, statistics.Median, statistics.StDev, result };
        Reference(Path.Combine(directory, name + ".science.json"), JsonSerializer.Serialize(snapshot, scienceJson), write);
        ReferenceBytes(Path.Combine(directory, name + ".stretch16"), Bytes(Words(stretched)), write);
        if (name == "circular" || name == "elliptical" || name == "catalog" || name.StartsWith("large-", StringComparison.Ordinal)) Require(result.StarList?.Count > 0, "Synthetic stars were not actually detected.");
    }

    private static void ColorImage(string directory, bool write, SensorType pattern) {
        const int width = 6, height = 8;
        ushort[] raw = Enumerable.Range(0, width * height).Select(i => (ushort)(100 + (i * 677) % 62000)).ToArray();
        var original = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, true, new ImageMetaData(), null!, detection, null!);
        var rendered = data.RenderImage().Debayer(saveColorChannels: true, saveLumChannel: true, bayerPattern: pattern);
        Require(rendered.Image.Format == PixelFormats.Rgb48 && rendered.Image.IsFrozen, "Debayer output contract changed.");
        string prefix = Path.Combine(directory, "bayer-" + pattern);
        ReferenceBytes(prefix + ".rgb48", Bytes(Words(rendered.Image)), write);
        Reference(prefix + ".channels.json", JsonSerializer.Serialize(rendered.DebayeredData, scienceJson), write);
        ReferenceBytes(prefix + ".linked48", Bytes(Words(rendered.Stretch(0.2, -2.8, false).GetAwaiter().GetResult().Image)), write);
        ReferenceBytes(prefix + ".unlinked48", Bytes(Words(rendered.Stretch(0.2, -2.8, true).GetAwaiter().GetResult().Image)), write);
        Require(raw.SequenceEqual(original) && data.Data.FlatArray.SequenceEqual(original), "Debayer or stretch changed raw samples.");
    }

    private static unsafe void OddWidthStretch(bool stock) {
        const int width = 5, height = 4;
        ushort[] raw = [0, 1, 255, 256, 32768, 65535, 12345, 23456, 34567, 45678, 99, 100, 101, 102, 103, 1000, 2000, 3000, 4000, 5000];
        var before = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, false, new ImageMetaData(), null!, detection, null!);
        var statistics = data.Statistics.Task.GetAwaiter().GetResult();
        var expected = raw.ToArray();
        fixed (ushort* pointer = expected) {
            using var view = new Accord.Imaging.UnmanagedImage((IntPtr)pointer, width, height, width * 2, DrawingFormat.Format16bppGrayScale);
            ImageUtility.GetColorRemappingFilter(statistics, 0.2, -2.8, PixelFormats.Gray16).ApplyInPlace(view);
        }
        var actual = Words(ImageUtility.Stretch(data.RenderImage(), 0.2, -2.8).GetAwaiter().GetResult());
        if (stock) Require(!actual.SequenceEqual(expected), "The documented stock padded-row remapping bug was not reproduced.");
        else Require(actual.SequenceEqual(expected), "Odd-width stretch skipped real samples or transformed padding.");
        Require(raw.SequenceEqual(before), "Odd-width stretch modified raw data.");
    }

    private static void ColorDetection(string directory, bool write) {
        const int width = 640, height = 480;
        var raw = new ushort[width * height];
        Buffer.BlockCopy(File.ReadAllBytes(Path.Combine(directory, "catalog.raw")), 0, raw, 0, raw.Length * 2);
        var before = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, true, new ImageMetaData(), null!, detection, null!);
        var image = data.RenderImage().Debayer(true, true, SensorType.RGGB).Stretch(0.2, -2.8, true).GetAwaiter().GetResult();
        var result = detection.Detect(image, PixelFormats.Rgb48, new StarDetectionParams { Sensitivity = StarSensitivityEnum.High, NoiseReduction = NoiseReductionEnum.None }, null!, CancellationToken.None).GetAwaiter().GetResult();
        ReferenceBytes(Path.Combine(directory, "color-catalog.stretch48"), Bytes(Words(image.Image)), write);
        Reference(Path.Combine(directory, "color-catalog.science.json"), JsonSerializer.Serialize(result, scienceJson), write);
        Require(result.StarList?.Count > 0 && raw.SequenceEqual(before), "Color detection omitted stars or changed raw samples.");
    }

    private static void DetectionCancellation(string directory) {
        const int width = 640, height = 480;
        var raw = new ushort[width * height];
        Buffer.BlockCopy(File.ReadAllBytes(Path.Combine(directory, "catalog.raw")), 0, raw, 0, raw.Length * 2);
        var before = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, false, new ImageMetaData(), null!, detection, null!);
        var source = new RenderedImage(ImageUtility.Stretch(data.RenderImage(), 0.2, -2.8).GetAwaiter().GetResult(), data, null!, detection, null!);
        var pixelsBefore = Words(source.Image);
        var parameters = new StarDetectionParams { Sensitivity = StarSensitivityEnum.High, NoiseReduction = NoiseReductionEnum.None };
        using var cancellation = new CancellationTokenSource();
        var statuses = new List<string>();
        var progress = new SynchronousProgress(status => {
            statuses.Add(status.Status);
            if (status.Status == "Detecting structures") cancellation.Cancel();
        });
        var partial = detection.Detect(source, PixelFormats.Gray16, parameters, progress, cancellation.Token).GetAwaiter().GetResult();
        Require(cancellation.IsCancellationRequested && statuses.Contains("Detecting structures"), "Cancellation did not reach the actual structures stage.");
        Require(partial.StarList is null && !statuses.Contains("Analyzing stars") && statuses[^1] == string.Empty, "Canceled Detect did not preserve its original partial-result and progress behavior.");
        Require(raw.SequenceEqual(before) && Words(source.Image).SequenceEqual(pixelsBefore), "Canceled detection changed scientific samples or source pixels.");
        var result = detection.Detect(source, PixelFormats.Gray16, parameters, null!, CancellationToken.None).GetAwaiter().GetResult();
        Require(result.StarList?.Count > 0, "Detection did not recover on the same source.");
        var statistics = data.Statistics.Task.GetAwaiter().GetResult();
        Reference(Path.Combine(directory, "catalog.science.json"), JsonSerializer.Serialize(new { statistics.Mean, statistics.Median, statistics.StDev, result }, scienceJson), false);
        Require(raw.SequenceEqual(before) && Words(source.Image).SequenceEqual(pixelsBefore), "Recovery changed scientific samples or source pixels.");
    }

    private sealed class SynchronousProgress(Action<NINA.Core.Model.ApplicationStatus> report) : IProgress<NINA.Core.Model.ApplicationStatus> {
        public void Report(NINA.Core.Model.ApplicationStatus status) => report(status);
    }

    private static unsafe void OddWidthColorStretch(bool stock) {
        const int width = 5, height = 8;
        ushort[] raw = Enumerable.Range(0, width * height).Select(i => (ushort)(100 + (i * 677) % 62000)).ToArray();
        var before = raw.ToArray();
        var detection = new StarDetection();
        var data = new BaseImageData(raw, width, height, 16, true, new ImageMetaData(), null!, detection, null!);
        var rendered = data.RenderImage().Debayer(true, true, SensorType.RGGB);
        foreach (bool unlinked in new[] { false, true }) {
            var expected = Words(rendered.Image);
            ColorRemappingGeneral filter;
            if (unlinked) {
                var channels = rendered.DebayeredData;
                var red = ImageStatistics.Create(data.Properties, channels.Red);
                var green = ImageStatistics.Create(data.Properties, channels.Green);
                var blue = ImageStatistics.Create(data.Properties, channels.Blue);
                filter = ImageUtility.GetColorRemappingFilterUnlinked(blue, green, red, 0.2, -2.8, PixelFormats.Rgb48);
            } else filter = ImageUtility.GetColorRemappingFilter(data.Statistics.Task.GetAwaiter().GetResult(), 0.2, -2.8, PixelFormats.Rgb48);
            fixed (ushort* pointer = expected) {
                using var view = new Accord.Imaging.UnmanagedImage((IntPtr)pointer, width, height, width * 6, DrawingFormat.Format48bppRgb);
                filter.ApplyInPlace(view);
            }
            var actual = Words(rendered.Stretch(0.2, -2.8, unlinked).GetAwaiter().GetResult().Image);
            if (stock) Require(!actual.SequenceEqual(expected), "Stock padded-row color bug was not reproduced.");
            else Require(actual.SequenceEqual(expected), "Odd-width color stretch omitted a sample or changed channel mapping.");
        }
        Require(raw.SequenceEqual(before), "Odd-width color processing modified raw samples.");
    }

    private static void PublicContract(string directory, bool write) {
        var api = typeof(StarAnnotator).Assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type => new {
            type = type.FullName,
            members = type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(member => member.ToString()).OrderBy(member => member, StringComparer.Ordinal).ToArray()
        });
        Reference(Path.Combine(directory, "public-contract.json"), JsonSerializer.Serialize(api, scienceJson), write);
    }

    private static ushort[] SensorFixture(string name, int width, int height) {
        var raw = new ushort[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            double value = name == "empty" ? 0 : name == "uniform" ? 100 : name == "elliptical" ? 80 : 100;
            if (name == "circular") value += 1000 * Gaussian(x, y, 15.35, 14.7, 2, 2);
            if (name == "elliptical") value += 900 * Gaussian(x, y, 20.1, 18.9, 3, 2);
            if (name == "catalog" || name.StartsWith("large-", StringComparison.Ordinal)) {
                value += (x * 19 + y * 31) % 11;
                for (int i = 0; i < 5; i++) value += (900 + i * 70) * Gaussian(x, y, width * (i + 1) / 6.0 + .2, height * .5 + (i % 2 == 0 ? -12 : 12), 2 + i * .2, 2 + i * .2);
            }
            raw[x + width * y] = (ushort)value;
        }
        return raw;
    }
    private static double Gaussian(int x, int y, double cx, double cy, double sx, double sy) {
        double dx = x - cx, dy = y - cy; return Math.Exp(-((dx * dx) / (2 * sx * sx) + (dy * dy) / (2 * sy * sy)));
    }
    private static void AnalyticMeasurement(bool elliptical) {
        int size = elliptical ? 41 : 31;
        double cx = elliptical ? 20.1 : 15.35, cy = elliptical ? 18.9 : 14.7, sx = elliptical ? 3 : 2, sy = 2, background = elliptical ? 80 : 100, amplitude = elliptical ? 900 : 1000;
        var pixels = new List<StarDetection.PixelData>();
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++) pixels.Add(new(x, y, background + amplitude * Gaussian(x, y, cx, cy, sx, sy)));
        var star = new StarDetection.Star {
            Position = elliptical ? new Accord.Point(20f, 19f) : new Accord.Point(15f, 15f), Radius = elliptical ? 8 : 6,
            Rectangle = elliptical ? new System.Drawing.Rectangle(12, 11, 16, 16) : new System.Drawing.Rectangle(9, 9, 12, 12), SurroundingMean = background, MaxPixelValue = background + amplitude
        };
        star.Calculate(pixels);
        Require(Math.Abs(star.Position.X - cx) <= 0.1 && Math.Abs(star.Position.Y - cy) <= 0.1, "Analytic centroid tolerance changed.");
        if (elliptical) Require(Math.Abs(star.Eccentricity - Math.Sqrt(1 - sy * sy / (sx * sx))) <= .05, "Analytic eccentricity tolerance changed.");
        else {
            double hfr = sx * Math.Sqrt(2 * Math.Log(2));
            Require(Math.Abs(star.HFR - hfr) <= .2 && Math.Abs(star.FWHM - 2 * hfr) <= .35, "Analytic HFR/FWHM tolerance changed.");
        }
        var detected = star.ToDetectedStar();
        Require(detected.HFR == star.HFR && detected.FWHM == star.FWHM && detected.Eccentricity == star.Eccentricity, "Detected star lost measured values.");
    }
    private static byte[] Bytes(ushort[] data) { var result = new byte[data.Length * 2]; Buffer.BlockCopy(data, 0, result, 0, result.Length); return result; }
    private static void Reference(string path, string value, bool write) { if (write) File.WriteAllText(path, value); else Require(File.ReadAllText(path).ReplaceLineEndings("\n") == value.ReplaceLineEndings("\n"), "Stock scientific result differs: " + Path.GetFileName(path)); }
    private static void ReferenceBytes(string path, byte[] value, bool write) { if (write) File.WriteAllBytes(path, value); else Require(File.ReadAllBytes(path).SequenceEqual(value), "Stock pixels differ: " + Path.GetFileName(path)); }
}
