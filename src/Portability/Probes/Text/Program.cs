using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using TextBuffer = HarfBuzzSharp.Buffer;

return TextProbe.Run(args);

internal static class TextProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        var checks = new List<Check>();
        var samples = new List<Sample>();
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos";
        string? output = null;
        string? fontPath = null;
        string? fontHash = null;
        string? family = null;

        try
        {
            var options = ParseOptions(args);
            output = Path.GetFullPath(options.Output ?? Path.Combine("artifacts", "portability", "text", os));
            Directory.CreateDirectory(output);
            fontPath = FindFont(options.Font);
            fontHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath)));
            using var typeface = SKTypeface.FromFile(fontPath)
                ?? throw new InvalidOperationException($"Skia could not load font '{fontPath}'.");
            using var font = new SKFont(typeface, 24);
            using var shaper = new SKShaper(typeface);
            family = typeface.FamilyName;
            checks.Add(new("explicit-font-load", "pass", $"Loaded {family} from {fontPath}; SHA-256 {fontHash}. No font downloaded or redistributed."));

            CheckSample("latin", "NINA exposure 60.0 s, gain 100", Direction.LeftToRight,
                font, shaper, output, samples, checks);
            var combining = CheckSample("combining", "e\u0301", Direction.LeftToRight,
                font, shaper, output, samples, checks);
            if (combining is not null)
            {
                Assert(checks, "combining-cluster", combining.Clusters.All(cluster => cluster == 0),
                    "UTF-16 e + combining acute maps to cluster 0. WPF caret policy is not supplied by this cluster result.");
            }

            var rtl = CheckSample("arabic-rtl", "\u0645\u0631\u062d\u0628\u0627", Direction.RightToLeft,
                font, shaper, output, samples, checks);
            if (rtl is not null)
            {
                Assert(checks, "rtl-run-order", rtl.Clusters.Length > 1 &&
                    rtl.Clusters.Zip(rtl.Clusters.Skip(1)).All(pair => pair.First >= pair.Second),
                    "The explicitly directed homogeneous Arabic run returns descending logical clusters. This does not exercise mixed-direction paragraph reordering.");
            }

            using (var missingBuffer = new TextBuffer())
            {
                missingBuffer.AddUtf16(char.ConvertFromUtf32(0x10FFFF));
                missingBuffer.GuessSegmentProperties();
                var missing = shaper.Shape(missingBuffer, font);
                Assert(checks, "missing-glyph-detection", missing.Codepoints.Contains(0u),
                    "U+10FFFF returns glyph 0 in the chosen face. Missing glyphs are observable; no fallback face was silently substituted.");
            }

            CheckScale(font, shaper, checks);
            CheckRepeatability(font, shaper, checks);
        }
        catch (Exception exception)
        {
            checks.Add(new("probe-runtime", "fail", $"{exception.GetType().Name}: {exception.Message}"));
        }

        checks.AddRange([
            new("font-discovery-and-fallback", "unimplemented", "Explicit installed font loading only. Fontconfig/system matching, WPF composite fonts, culture-sensitive fallback and mixed-face segmentation need an owned adapter."),
            new("mixed-direction-paragraph", "unimplemented", "HarfBuzz shapes a directed run. Unicode bidi paragraph analysis, run ordering and directional caret affinity are not implemented."),
            new("paragraph-layout", "unimplemented", "No WPF TextFormatter/TextLine implementation: wrapping, break records, trimming, tabs, justification, inline objects and paragraph metrics are absent."),
            new("character-hit-testing", "unimplemented", "Glyph clusters and positions are recorded but are not WPF CharacterHit. No distance-to-character, trailing-edge, ligature-caret or selection behavior is claimed."),
            new("wpf-native-seams", "unimplemented", "PresentationNative Lo* layout and hit-test exports, DirectWriteForwarder and GlyphRun DUCE IDWriteFont resources are not replaced by this standalone component."),
            new("wpf-metric-and-raster-parity", "unimplemented", "No comparison against WPF Ideal/Display metrics, pixels-per-DIP, ClearType, fallback or decoration behavior has been performed."),
            new("cross-thread-resource-lifetime", "unimplemented", "The probe disposes native fonts, shaping buffers, blobs and bitmaps on one thread. WPF dispatcher/render-thread resource retention remains untested.")
        ]);

        var report = new
        {
            probe = "text-component",
            os,
            scope = "Standalone SkiaSharp/HarfBuzz run shaping, measurement and software rasterization; no WPF implementation or compatibility claim.",
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            font = new { path = fontPath, sha256 = fontHash, family },
            packages = new { skiaSharp = "3.119.4", skiaSharpHarfBuzz = "3.119.4", harfBuzzSharp = "8.3.1.5" },
            sampleClusterUnits = "UTF-16 code units",
            checks,
            samples
        };
        string json = JsonSerializer.Serialize(report, JsonOptions);
        Console.WriteLine(json);
        if (output is not null)
        {
            try
            {
                File.WriteAllText(Path.Combine(output, "results.json"), json + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Cannot write results.json: {exception.Message}");
                return 1;
            }
        }
        return checks.Any(check => check.Status == "fail") ? 1 : 0;
    }

    private static Sample? CheckSample(string name, string text, Direction direction,
        SKFont font, SKShaper shaper, string output, List<Sample> samples, List<Check> checks)
    {
        try
        {
            using var buffer = new TextBuffer();
            buffer.AddUtf16(text);
            buffer.Direction = direction;
            buffer.GuessSegmentProperties();
            var shaped = shaper.Shape(buffer, font);
            if (shaped.Codepoints.Length == 0 || shaped.Codepoints.Contains(0u) ||
                !float.IsFinite(shaped.Width) || shaped.Width <= 0 ||
                shaped.Points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            {
                throw new InvalidOperationException("Run has empty/missing glyphs or invalid positions/advance width; supply a font covering Latin, combining acute and Arabic.");
            }

            string imagePath = Path.Combine(output, name + ".png");
            var ink = Draw(shaped, font, imagePath);
            var sample = new Sample(name, text, direction.ToString(), shaped.Width,
                shaped.Codepoints, shaped.Clusters,
                shaped.Points.Select(point => new Point(point.X, point.Y)).ToArray(), ink, imagePath);
            samples.Add(sample);
            checks.Add(new(name + "-shape-measure-draw", "pass",
                FormattableString.Invariant($"{shaped.Codepoints.Length} glyphs, advance {shaped.Width:F3} px at 24 px; {ink.Pixels} nonwhite pixels within ({ink.Left},{ink.Top})-({ink.Right},{ink.Bottom}). Saved {imagePath}.")));
            return sample;
        }
        catch (Exception exception)
        {
            checks.Add(new(name + "-shape-measure-draw", "fail", $"{exception.GetType().Name}: {exception.Message}"));
            return null;
        }
    }

    private static Ink Draw(SKShaper.Result shaped, SKFont font, string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1200, 100, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocateRawPositionedRun(font, shaped.Codepoints.Length);
        for (int index = 0; index < shaped.Codepoints.Length; index++)
        {
            run.Glyphs[index] = checked((ushort)shaped.Codepoints[index]);
            run.Positions[index] = shaped.Points[index];
        }
        using var blob = builder.Build() ?? throw new InvalidOperationException("Could not create shaped text blob.");
        canvas.DrawText(blob, 24, 64, paint);
        canvas.Flush();

        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1, pixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == SKColors.White)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                pixels++;
            }
        }
        if (pixels == 0 || left == 0 || top == 0 || right == bitmap.Width - 1 || bottom == bitmap.Height - 1)
            throw new InvalidOperationException("Rendered text is empty or touches the surface boundary.");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return new(left, top, right, bottom, pixels);
    }

    private static void CheckScale(SKFont font, SKShaper shaper, List<Check> checks)
    {
        const string text = "NINA 123.45";
        float baseWidth = shaper.Shape(text, font).Width;
        var widths = new List<string>();
        bool matches = true;
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using var scaledFont = new SKFont(font.Typeface, font.Size * scale);
            float width = shaper.Shape(text, scaledFont).Width;
            matches &= float.IsFinite(width) && Math.Abs(width - baseWidth * scale) < 0.05f;
            widths.Add(FormattableString.Invariant($"{scale:F1}x={width:F3}px"));
        }
        Assert(checks, "run-advance-scaling", matches,
            "Shaped advances scale at equivalent 96/144/192 DPI font sizes: " + string.Join(", ", widths) + ". This is not a WPF DPI test.");
    }

    private static void CheckRepeatability(SKFont font, SKShaper shaper, List<Check> checks)
    {
        const string text = "Repeat e\u0301 123";
        var expected = shaper.Shape(text, font);
        bool matches = true;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var actual = shaper.Shape(text, font);
            matches &= expected.Width == actual.Width && expected.Codepoints.SequenceEqual(actual.Codepoints) &&
                expected.Clusters.SequenceEqual(actual.Clusters) && expected.Points.SequenceEqual(actual.Points);
        }
        Assert(checks, "repeated-run-shaping", matches,
            "100 shapes with the same font/shaper have identical glyphs, UTF-8 clusters, positions and advances. This does not establish memory bounds or cross-thread safety.");
    }

    private static void Assert(List<Check> checks, string name, bool condition, string detail) =>
        checks.Add(new(name, condition ? "pass" : "fail", condition ? detail : "Expected condition was not met: " + detail));

    private static (string? Font, string? Output) ParseOptions(string[] args)
    {
        string? font = null, output = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] is not ("--font" or "--output") || index + 1 >= args.Length)
                throw new ArgumentException("Usage: Portability.Text [--font existing-font.ttf] [--output artifact-directory]");
            if (args[index] == "--font")
                font = args[++index];
            else
                output = args[++index];
        }
        return (font, output);
    }

    private static string FindFont(string? requested)
    {
        string[] candidates = requested is not null ? [Path.GetFullPath(requested)] :
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf"
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No known installed font found. Pass --font with an installed font covering Latin, combining acute and Arabic. No font is downloaded.");
    }

    private sealed record Check(string Name, string Status, string Detail);
    private sealed record Point(float X, float Y);
    private sealed record Ink(int Left, int Top, int Right, int Bottom, int Pixels);
    private sealed record Sample(string Name, string Text, string Direction, float AdvanceWidth,
        uint[] GlyphIds, uint[] Clusters, Point[] Positions, Ink Ink, string Artifact);
}
