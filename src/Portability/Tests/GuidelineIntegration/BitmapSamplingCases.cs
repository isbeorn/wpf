using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
#if !STOCK
using SkiaSharp;
#endif

internal static class BitmapSamplingCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null, string? pixels = null, BitmapSamplingMath.Statistics? statistics = null,
        string? rasterFile = null, string? rasterSha256 = null, string? sourceSha256 = null,
        string? coveragePixels = null, string? coverageFile = null, string? coverageSha256 = null);
    internal sealed record RasterCase(string Name, BitmapSource Source, Rect Rectangle, Vector Pan, DpiScale Dpi, BitmapScalingMode Mode, Matrix Transform, byte[] SourceBytes)
    {
        internal Rect? Clip { get; init; }
        internal bool Guided { get; init; }
        internal int SurfaceWidth { get; init; } = 32;
        internal int SurfaceHeight { get; init; } = 24;
        internal string? SourceHash { get; init; }
        internal int? LinearColorLimit { get; init; }
    }
    private sealed record Scene(string Name, Visual Visual, DpiScale Dpi, RasterCase? Raster = null)
    {
        public static implicit operator Scene((string Name, Visual Visual, DpiScale Dpi) value) => new(value.Name, value.Visual, value.Dpi);
    }

    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render, string[] args)
    {
        var results = new List<Result>();
        int largeArgument = Array.IndexOf(args, "--large-sampling");
        if (largeArgument == args.Length - 1 && largeArgument >= 0) throw new ArgumentException("--large-sampling requires an evidence directory.");
        string? largeDirectory = largeArgument < 0 ? null : Path.GetFullPath(args[largeArgument + 1]);
        if (largeDirectory != null) Directory.CreateDirectory(largeDirectory);
#if !STOCK
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StockBitmapSamplingReference.json")));
#endif
        foreach (var scenario in Scenes(largeDirectory != null))
        {
            byte[]? pixels = null;
            string? rasterFile = null, rasterHash = null;
            try
            {
                int width = scenario.Raster?.SurfaceWidth ?? 32, height = scenario.Raster?.SurfaceHeight ?? 24;
                bool large = width != 32 || height != 24;
                pixels = render(scenario.Visual, scenario.Dpi, width, height);
                byte[]? coverage = scenario.Raster == null ? null : ExtractCoverage(render(CreateRasterVisual(scenario.Raster, true), scenario.Dpi, width, height));
                string? coverageFile = null, coverageHash = null;
                if (large)
                {
#if STOCK
                    rasterFile = Path.Combine(largeDirectory!, "stock-" + scenario.Name + ".bgra");
#else
                    rasterFile = Path.Combine(largeDirectory!, (OperatingSystem.IsWindows() ? "windows-" : "linux-") + scenario.Name + ".bgra");
#endif
                    File.WriteAllBytes(rasterFile, pixels);
                    rasterHash = Convert.ToHexString(SHA256.HashData(pixels));
                    if (coverage != null)
                    {
                        coverageFile = Path.ChangeExtension(rasterFile, ".coverage");
                        File.WriteAllBytes(coverageFile, coverage);
                        coverageHash = Convert.ToHexString(SHA256.HashData(coverage));
                    }
                }
#if !STOCK
                byte[] stock = large ? File.ReadAllBytes(Path.Combine(largeDirectory!, "stock-" + scenario.Name + ".bgra"))
                    : Convert.FromBase64String(reference.RootElement.EnumerateArray().Single(item => item.GetProperty("Name").GetString() == scenario.Name).GetProperty("pixels").GetString()!);
                if (scenario.Raster != null)
                {
                    byte[] stockCoverage = large ? File.ReadAllBytes(Path.Combine(largeDirectory!, "stock-" + scenario.Name + ".coverage"))
                        : Convert.FromBase64String(reference.RootElement.EnumerateArray().Single(item => item.GetProperty("Name").GetString() == scenario.Name).GetProperty("coveragePixels").GetString()!);
                    var statistics = scenario.Raster.Mode == BitmapScalingMode.Linear
                        ? BitmapSamplingMath.CompareLinear(pixels, stock, coverage!, stockCoverage, scenario.Raster, width, height)
                        : BitmapSamplingMath.CompareNearest(pixels, stock, coverage!, stockCoverage, scenario.Raster, width, height);
                    results.Add(new Result(scenario.Name, statistics.Failures == 0, statistics.FirstFailure, large ? null : Convert.ToBase64String(pixels), statistics, rasterFile, rasterHash, scenario.Raster.SourceHash,
                        large ? null : Convert.ToBase64String(coverage!), coverageFile, coverageHash));
                    continue;
                }
                Compare(pixels, stock, scenario.Name);
#endif
                results.Add(new Result(scenario.Name, true, pixels: large ? null : Convert.ToBase64String(pixels), rasterFile: rasterFile, rasterSha256: rasterHash, sourceSha256: scenario.Raster?.SourceHash,
                    coveragePixels: coverage == null || large ? null : Convert.ToBase64String(coverage), coverageFile: coverageFile, coverageSha256: coverageHash));
            }
            catch (Exception exception) { results.Add(new Result(scenario.Name, false, exception.ToString(), pixels == null || rasterFile != null ? null : Convert.ToBase64String(pixels), rasterFile: rasterFile, rasterSha256: rasterHash)); }
        }
#if !STOCK
        try
        {
            CheckCoverageControlCorruption(reference.RootElement);
            results.Add(new Result("sampling-independent-coverage-corruption-guards", true));
        }
        catch (Exception exception) { results.Add(new Result("sampling-independent-coverage-corruption-guards", false, exception.ToString())); }
        try
        {
            CheckRetainedInvalidation(reference.RootElement);
            results.Add(new Result("sampling-retained-mode-and-bitmap-dispatcher-repaint", true));
        }
        catch (Exception exception) { results.Add(new Result("sampling-retained-mode-and-bitmap-dispatcher-repaint", false, exception.ToString())); }
        try
        {
            CheckUnsupportedQualityRecovery(reference.RootElement);
            results.Add(new Result("sampling-highquality-fant-failure-and-recovery", true));
        }
        catch (Exception exception) { results.Add(new Result("sampling-highquality-fant-failure-and-recovery", false, exception.ToString())); }
#endif
        return results;
    }

    private static IEnumerable<Scene> Scenes(bool large)
    {
        byte[] input = new byte[8 * 4 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * 8 + x) * 4;
                input[offset] = (byte)((x * 37 + y * 19) % 256);
                input[offset + 1] = (byte)((x + y) % 2 == 0 ? 0 : 255);
                input[offset + 2] = (byte)(x * 31);
                input[offset + 3] = 255;
            }
        byte[] expectedSource = input.ToArray();
        BitmapSource bitmap = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Pbgra32, null, input, 32);
        Array.Clear(input);
        bitmap.CopyPixels(input, 32, 0);
        if (!input.SequenceEqual(expectedSource)) throw new InvalidOperationException("BitmapSource did not retain exact caller-owned source bytes.");
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen()) drawing.DrawImage(bitmap, new Rect(0, 0, 24, 12));
        yield return new Scene("sampling-default-linear-scale", visual, new DpiScale(1, 1), new RasterCase("sampling-default-linear-scale", bitmap,
            new Rect(0, 0, 24, 12), new Vector(), new DpiScale(1, 1), BitmapScalingMode.Linear, Matrix.Identity, expectedSource));
        var element = new ImageElement(drawing => drawing.DrawImage(bitmap, new Rect(0, 0, 24, 12)));
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
        element.Measure(new Size(32, 24));
        element.Arrange(new Rect(0, 0, 32, 24));
        yield return ("sampling-explicit-nearest-scale", element, new DpiScale(1, 1));

        BitmapSource grid = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 0, 0, 0, 255, 128, 0, 128, 255, 128, 128, 0, 255, 0, 128, 128, 255 }, 8);
        var parent = new ImageElement(drawing => drawing.DrawImage(grid, new Rect(0, 0, 8, 8)));
        var first = new ImageElement(drawing => drawing.DrawImage(grid, new Rect(10, 0, 8, 8)));
        var sibling = new ImageElement(drawing => drawing.DrawImage(grid, new Rect(20, 0, 8, 8)));
        parent.Children.Add(first);
        parent.Children.Add(sibling);
        RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.NearestNeighbor);
        parent.Measure(new Size(32, 24));
        parent.Arrange(new Rect(0, 0, 32, 24));
        yield return ("sampling-ancestor-nearest-unspecified-children", parent, new DpiScale(1, 1));
        RenderOptions.SetBitmapScalingMode(first, BitmapScalingMode.Linear);
        yield return ("sampling-local-linear-sibling-restores-nearest", parent, new DpiScale(1, 1));
        first.ClearValue(RenderOptions.BitmapScalingModeProperty);
        yield return ("sampling-clear-local-restores-ancestor", parent, new DpiScale(1, 1));
        RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.Linear);
        RenderOptions.SetBitmapScalingMode(first, BitmapScalingMode.NearestNeighbor);
        yield return ("sampling-local-nearest-sibling-restores-linear", parent, new DpiScale(1, 1));
        parent.ClearValue(RenderOptions.BitmapScalingModeProperty);
        first.ClearValue(RenderOptions.BitmapScalingModeProperty);
        yield return ("sampling-clear-ancestor-restores-default-linear", parent, new DpiScale(1, 1));

        var outer = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(outer, BitmapScalingMode.Linear);
        outer.Children.Add(new ImageDrawing(grid, new Rect(0, 0, 8, 8)));
        var inner = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(inner, BitmapScalingMode.NearestNeighbor);
        inner.Children.Add(new ImageDrawing(grid, new Rect(10, 0, 8, 8)));
        outer.Children.Add(inner);
        outer.Children.Add(new ImageDrawing(grid, new Rect(20, 0, 8, 8)));
        var grouped = new ImageElement(drawing =>
        {
            drawing.DrawDrawing(outer);
            drawing.DrawImage(grid, new Rect(0, 10, 8, 8));
        });
        RenderOptions.SetBitmapScalingMode(grouped, BitmapScalingMode.NearestNeighbor);
        grouped.Measure(new Size(32, 24));
        grouped.Arrange(new Rect(0, 0, 32, 24));
        yield return ("sampling-nested-groups-restore-group-and-visual", grouped, new DpiScale(1, 1));

        var guided = new ImageElement(drawing =>
        {
            drawing.PushGuidelineSet(new GuidelineSet([0, 8], [0, 8]));
            drawing.DrawImage(grid, new Rect(0, 0, 8, 8));
            drawing.Pop();
        });
        guided.Measure(new Size(32, 24));
        guided.Arrange(new Rect(0, 0, 32, 24));
        RenderOptions.SetBitmapScalingMode(guided, BitmapScalingMode.NearestNeighbor);
        yield return ("sampling-guidelines-nearest", guided, new DpiScale(1, 1));
        RenderOptions.SetBitmapScalingMode(guided, BitmapScalingMode.LowQuality);
        yield return ("sampling-guidelines-lowquality-linear-alias", guided, new DpiScale(1, 1));

        foreach (var retained in RetainedCases()) yield return retained;

        foreach (RasterCase sample in CalibrationCases().Concat(BitmapSamplingCorpus.AlphaEdgeCases()).Concat(large ? BitmapSamplingCorpus.LargeCases() : []))
        {
            yield return new Scene(sample.Name, CreateRasterVisual(sample, false), sample.Dpi, sample);
        }
    }

    private static DrawingVisual CreateRasterVisual(RasterCase sample, bool coverage)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, sample.Mode);
        using (DrawingContext drawing = group.Open())
        {
            if (coverage) drawing.DrawGeometry(Brushes.White, null, new RectangleGeometry(sample.Rectangle));
            else drawing.DrawImage(sample.Source, sample.Rectangle);
        }
        var image = new DrawingVisual();
        using (DrawingContext drawing = image.RenderOpen())
        {
            drawing.PushTransform(new TranslateTransform(sample.Pan.X, sample.Pan.Y));
            drawing.PushTransform(new MatrixTransform(sample.Transform));
            if (sample.Clip is Rect clip) drawing.PushClip(new RectangleGeometry(clip));
            if (sample.Guided) drawing.PushGuidelineSet(new GuidelineSet([sample.Rectangle.Left, sample.Rectangle.Right], [sample.Rectangle.Top, sample.Rectangle.Bottom]));
            drawing.DrawDrawing(group);
            if (sample.Guided) drawing.Pop();
            if (sample.Clip.HasValue) drawing.Pop();
            drawing.Pop();
            drawing.Pop();
        }
        return image;
    }

    private static byte[] ExtractCoverage(byte[] pixels)
    {
        byte[] coverage = new byte[pixels.Length / 4];
        for (int index = 0; index < coverage.Length; index++)
        {
            int offset = index * 4;
            byte alpha = pixels[offset + 3];
            if (pixels[offset] != alpha || pixels[offset + 1] != alpha || pixels[offset + 2] != alpha)
                throw new InvalidOperationException("The genuine white geometry coverage control is not premultiplied white.");
            coverage[index] = alpha;
        }
        return coverage;
    }

    private static IEnumerable<(string Name, Visual Visual, DpiScale Dpi)> RetainedCases()
    {
        byte[] pixels = [0, 0, 0, 255, 128, 0, 128, 255, 128, 128, 0, 255, 0, 128, 128, 255];
        var source = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null);
        source.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);
        var element = new ImageElement(drawing => drawing.DrawImage(source, new Rect(0, 0, 8, 8)));
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
        element.Measure(new Size(32, 24));
        element.Arrange(new Rect(0, 0, 32, 24));
        yield return ("sampling-retained-nearest-initial", element, new DpiScale(1, 1));
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.LowQuality);
        yield return ("sampling-retained-lowquality-alias-linear", element, new DpiScale(1, 1));
        element.ClearValue(RenderOptions.BitmapScalingModeProperty);
        yield return ("sampling-retained-clear-default-linear", element, new DpiScale(1, 1));
        for (int index = 0; index < pixels.Length; index += 4)
            (pixels[index], pixels[index + 1]) = (pixels[index + 1], pixels[index]);
        byte[] expected = pixels.ToArray();
        source.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);
        Array.Clear(pixels);
        source.CopyPixels(pixels, 8, 0);
        if (!pixels.SequenceEqual(expected)) throw new InvalidOperationException("Retained bitmap mutation lost source bytes.");
        yield return ("sampling-retained-bitmap-mutation-linear", element, new DpiScale(1, 1));
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
        yield return ("sampling-retained-mutated-nearest", element, new DpiScale(1, 1));
    }

#if !STOCK
    private static void CheckRetainedInvalidation(JsonElement references)
    {
        Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        object? target = null;
        byte[]? lastFrame = null;
        int frames = 0;
        try
        {
            foreach (var sample in RetainedCases())
            {
                if (target == null)
                {
                    target = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [sample.Visual, 32, 24, sample.Dpi], null)!;
                    Action rendered = () =>
                    {
                        using var bitmap = (SKBitmap)Invoke(type, target, "Snapshot")!;
                        lastFrame = bitmap.GetPixelSpan().ToArray();
                        frames++;
                    };
                    type.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(target, [rendered]);
                }
                int previousFrames = frames;
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                if (frames <= previousFrames || lastFrame == null) throw new InvalidOperationException($"{sample.Name}: mutation did not queue a real retained frame.");
                var expected = references.EnumerateArray().Single(item => item.GetProperty("Name").GetString() == sample.Name);
                Compare(lastFrame, Convert.FromBase64String(expected.GetProperty("pixels").GetString()!), sample.Name);
                if (((ImageElement)sample.Visual).RenderCount != 1) throw new InvalidOperationException("A retained state/resource mutation rerecorded UIElement drawing content.");
            }
        }
        finally { if (target != null) ((IDisposable)target).Dispose(); }
    }

    private static object? Invoke(Type type, object target, string name)
    {
        try { return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }

    private static void CheckUnsupportedQualityRecovery(JsonElement references)
    {
        BitmapSource source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 0, 0, 0, 255, 128, 0, 128, 255, 128, 128, 0, 255, 0, 128, 128, 255 }, 8);
        byte[] expectedLinear = Convert.FromBase64String(references.EnumerateArray().Single(item =>
            item.GetProperty("Name").GetString() == "sampling-retained-lowquality-alias-linear").GetProperty("pixels").GetString()!);
        var element = new ImageElement(drawing => drawing.DrawImage(source, new Rect(0, 0, 8, 8)));
        element.Measure(new Size(32, 24));
        element.Arrange(new Rect(0, 0, 32, 24));
        Type targetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(targetType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [element, 32, 24, new DpiScale(1, 1)], null)!;
        int frames = 0;
        Action rendered = () => frames++;
        targetType.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(target, [rendered]);
        foreach (BitmapScalingMode mode in new[] { BitmapScalingMode.HighQuality, BitmapScalingMode.Fant })
        {
            RenderOptions.SetBitmapScalingMode(element, mode);
            int previousFrames = frames;
            ExpectQualityFailure(() => Invoke(targetType, target, "Render"));
            if (frames != previousFrames) throw new InvalidOperationException("A failed quality frame raised FrameRendered.");
            element.ClearValue(RenderOptions.BitmapScalingModeProperty);
            Invoke(targetType, target, "Render");
            using var snapshot = (SKBitmap)Invoke(targetType, target, "Snapshot")!;
            Compare(snapshot.GetPixelSpan().ToArray(), expectedLinear, "quality-frame-recovery");

            using var surface = SKSurface.Create(new SKImageInfo(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            using var precision = SKColorFilter.CreateColorMatrix(new float[]
            {
                1, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 1, 0, 0, 0, 0, 0, 1, 0
            });
            Type walkerType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.SkiaDrawingContextWalker", true)!;
            var walker = (DrawingContext)Activator.CreateInstance(walkerType, BindingFlags.Instance | BindingFlags.NonPublic, null, [surface.Canvas, precision], null)!;
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, mode);
            group.Children.Add(new ImageDrawing(source, new Rect(0, 0, 8, 8)));
            ExpectQualityFailure(() => walker.DrawDrawing(group));
            walker.DrawImage(source, new Rect(0, 0, 8, 8));
            surface.Canvas.Flush();
            using SKImage image = surface.Snapshot();
            using SKBitmap output = SKBitmap.FromImage(image);
            Compare(output.GetPixelSpan().ToArray(), expectedLinear, "quality-group-finally-recovery");
        }
    }

    private static void ExpectQualityFailure(Action action)
    {
        try { action(); }
        catch (NotSupportedException exception) when (exception.Message.Contains("HighQuality/Fant", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Unimplemented quality prefiltering did not raise its named diagnostic.");
    }
#endif

    internal static IEnumerable<RasterCase> CalibrationCases()
    {
        foreach (PixelFormat format in new[] { PixelFormats.Pbgra32, PixelFormats.Gray16, PixelFormats.Rgb48 })
        {
            int stride = 8 * format.BitsPerPixel / 8;
            byte[] bytes = new byte[4 * stride];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                {
                    int offset = y * stride + x * format.BitsPerPixel / 8;
                    if (format == PixelFormats.Pbgra32)
                    {
                        int alpha = (x + y) % 3 == 0 ? 128 : 255;
                        bytes[offset] = (byte)(((x * 37 + y * 19) % 256) * alpha / 255);
                        bytes[offset + 1] = (byte)((x + y) % 2 == 0 ? 0 : alpha);
                        bytes[offset + 2] = (byte)(x * 31 * alpha / 255);
                        bytes[offset + 3] = (byte)alpha;
                    }
                    else
                        for (int channel = 0; channel < format.BitsPerPixel / 16; channel++)
                        {
                            ushort value = (ushort)((((y * 8 + x) * 3 + channel) * 4093 + 997) & 65535);
                            bytes[offset + channel * 2] = (byte)value;
                            bytes[offset + channel * 2 + 1] = (byte)(value >> 8);
                        }
                }
            byte[] expected = bytes.ToArray();
            BitmapSource source = BitmapSource.Create(8, 4, 96, 96, format, null, bytes, stride);
            Array.Clear(bytes);
            source.CopyPixels(bytes, stride, 0);
            if (!bytes.SequenceEqual(expected)) throw new InvalidOperationException($"{format}: source bytes changed.");
            foreach (BitmapScalingMode mode in new[] { BitmapScalingMode.Linear, BitmapScalingMode.NearestNeighbor })
                foreach (var view in new[]
                {
                    (Name: "magnified", Rectangle: new Rect(0, 0, 24, 12), Pan: new Vector(), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity),
                    (Name: "reduced", Rectangle: new Rect(0, 0, 6, 3), Pan: new Vector(), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity),
                    (Name: "fractional", Rectangle: new Rect(0, 0, 17, 9), Pan: new Vector(2.375, 3.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity),
                    (Name: "edges", Rectangle: new Rect(0, 0, 20, 11), Pan: new Vector(-3.375, -1.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity),
                    (Name: "dpi", Rectangle: new Rect(0, 0, 17, 9), Pan: new Vector(0.375, 0.25), Dpi: new DpiScale(1.5, 1.25), Transform: Matrix.Identity),
                    (Name: "flip-x", Rectangle: new Rect(0, 0, 17, 9), Pan: new Vector(0.375, 3.25), Dpi: new DpiScale(1, 1), Transform: new Matrix(-1, 0, 0, 1, 24, 0)),
                    (Name: "flip-y", Rectangle: new Rect(0, 0, 17, 9), Pan: new Vector(2.375, 0.25), Dpi: new DpiScale(1, 1), Transform: new Matrix(1, 0, 0, -1, 0, 16)),
                    (Name: "flip-xy", Rectangle: new Rect(0, 0, 17, 9), Pan: new Vector(0.375, 0.25), Dpi: new DpiScale(1, 1), Transform: new Matrix(-1, 0, 0, -1, 24, 16))
                })
                    yield return new RasterCase($"sampling-{format}-{mode}-{view.Name}", source, view.Rectangle, view.Pan, view.Dpi, mode, view.Transform, expected);
        }
    }

#if !STOCK
    private static void CheckCoverageControlCorruption(JsonElement references)
    {
        foreach (RasterCase sample in CalibrationCases().Where(item => item.Name.StartsWith("sampling-Pbgra32-", StringComparison.Ordinal) && item.Name.EndsWith("-magnified", StringComparison.Ordinal)))
        {
            JsonElement reference = references.EnumerateArray().Single(item => item.GetProperty("Name").GetString() == sample.Name);
            byte[] image = Convert.FromBase64String(reference.GetProperty("pixels").GetString()!);
            byte[] geometry = Convert.FromBase64String(reference.GetProperty("coveragePixels").GetString()!);
            foreach (string side in new[] { "portable", "stock", "both" })
                foreach (string corruption in new[] { "blank-pair", "shift-pair", "interior-hole", "exterior-ink" })
                {
                    byte[] changedImage = image.ToArray(), changedGeometry = geometry.ToArray();
                    if (corruption == "blank-pair")
                    {
                        Array.Clear(changedImage);
                        Array.Clear(changedGeometry);
                    }
                    else if (corruption == "shift-pair")
                    {
                        Array.Clear(changedImage);
                        Array.Clear(changedGeometry);
                        for (int y = 0; y < 24; y++)
                            for (int x = 4; x < 32; x++)
                            {
                                image.AsSpan((y * 32 + x - 4) * 4, 4).CopyTo(changedImage.AsSpan((y * 32 + x) * 4, 4));
                                changedGeometry[y * 32 + x] = geometry[y * 32 + x - 4];
                            }
                    }
                    else
                    {
                        int pixel = corruption == "interior-hole" ? 3 * 32 + 3 : 20 * 32 + 29;
                        byte value = corruption == "interior-hole" ? (byte)0 : (byte)255;
                        changedImage.AsSpan(pixel * 4, 4).Fill(value);
                        changedGeometry[pixel] = value;
                    }
                    byte[] actual = side == "stock" ? image : changedImage, stock = side == "portable" ? image : changedImage;
                    byte[] actualGeometry = side == "stock" ? geometry : changedGeometry, stockGeometry = side == "portable" ? geometry : changedGeometry;
                    BitmapSamplingMath.Statistics statistics = sample.Mode == BitmapScalingMode.Linear
                        ? BitmapSamplingMath.CompareLinear(actual, stock, actualGeometry, stockGeometry, sample, 32, 24)
                        : BitmapSamplingMath.CompareNearest(actual, stock, actualGeometry, stockGeometry, sample, 32, 24);
                    if (statistics.Failures == 0)
                        throw new InvalidOperationException($"{sample.Name}: {side} {corruption} escaped independent geometry validation.");
                }
        }
    }
#endif

    private static void Compare(byte[] actual, byte[] expected, string name)
    {
        if (actual.Length != expected.Length) throw new InvalidOperationException($"{name}: raster byte counts differ.");
        int count = 0, maximum = 0, first = -1;
        for (int index = 0; index < expected.Length; index++)
        {
            int difference = Math.Abs(actual[index] - expected[index]);
            if (difference == 0) continue;
            if (first < 0) first = index;
            count++;
            maximum = Math.Max(maximum, difference);
        }
        if (first >= 0)
            throw new InvalidOperationException($"{name}: {count} differing channels, maximum {maximum}; first pixel ({first / 4 % 32},{first / 4 / 32}) channel {first % 4} was {actual[first]}, stock WPF was {expected[first]}.");
    }

    private sealed class ImageElement(Action<DrawingContext> draw) : UIElement
    {
        internal int RenderCount { get; private set; }
        private VisualCollection? _children;
        internal VisualCollection Children => _children ??= new VisualCollection(this);
        protected override int VisualChildrenCount => Children.Count;
        protected override Visual GetVisualChild(int index) => Children[index];
        protected override Size MeasureCore(Size availableSize)
        {
            foreach (UIElement child in Children) child.Measure(availableSize);
            return new Size(32, 24);
        }
        protected override void ArrangeCore(Rect finalRect)
        {
            base.ArrangeCore(finalRect);
            foreach (UIElement child in Children) child.Arrange(new Rect(new Point(), finalRect.Size));
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            RenderCount++;
            draw(drawingContext);
        }
    }
}
