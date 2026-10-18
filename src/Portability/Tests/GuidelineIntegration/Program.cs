using System.Reflection;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
#if !STOCK
using SkiaSharp;
#endif

var results = new List<object>();
var failed = false;
var samples = new Dictionary<string, (int[] Row, int[] Column, byte[] Pixels)>();
#if !STOCK
using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StockGuidelineReference.json")));
#endif
var thread = new Thread(() =>
{
    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    foreach (var scenario in Cases())
    {
        try
        {
            Rect bounds = VisualTreeHelper.GetContentBounds(scenario.Visual);
            byte[] pixels = Render(scenario.Visual, scenario.Dpi);
            if (VisualTreeHelper.GetContentBounds(scenario.Visual) != bounds) throw new InvalidOperationException("Device snapping changed actual logical content bounds.");
            int[] row = Enumerable.Range(0, 16).SelectMany(x => pixels.AsSpan((2 * 32 + x) * 4, 4).ToArray().Select(value => (int)value)).ToArray();
            int[] column = Enumerable.Range(0, 24).Select(y => (int)pixels[(y * 32 + 2) * 4 + 3]).ToArray();
#if !STOCK
            var expected = reference.RootElement.GetProperty("results").EnumerateArray().Single(item => item.GetProperty("Name").GetString() == scenario.Name);
            if (!args.Contains("--capture") && !scenario.Name.StartsWith("glyph-", StringComparison.Ordinal) && (!scenario.Name.StartsWith("image-", StringComparison.Ordinal) || scenario.Name is "image-guideline" or "image-checker-guideline"))
            {
                bool curved = scenario.Name.StartsWith("conic-", StringComparison.Ordinal) || scenario.Name.StartsWith("quadratic-", StringComparison.Ordinal) || scenario.Name.StartsWith("polyquadratic-", StringComparison.Ordinal) || scenario.Name is "rotated-push" or "rotated-plain" or "cubic-controls" or "cubic-manual";
                int tolerance = scenario.Name == "image-guideline" ? 16 : scenario.Name == "image-checker-guideline" ? 32 : curved ? 64 : 3;
                Compare(row, expected.GetProperty("row"), scenario.Name, tolerance);
                Compare(column, expected.GetProperty("column"), scenario.Name, tolerance);
            }
#endif
            samples.Add(scenario.Name, (row, column, pixels));
            results.Add(new { scenario.Name, passed = true, row, column });
        }
        catch (Exception exception) { failed = true; results.Add(new { scenario.Name, passed = false, detail = exception.ToString() }); }
    }
#if !STOCK
    Check("cubic-control-snapping-matches-explicit-device-path", () => EqualSamples("cubic-controls", "cubic-manual"));
    Check("quadratic-elevation-precedes-control-snapping", () => EqualSamples("quadratic-guideline", "quadratic-manual"));
    Check("polyquadratic-elevation-precedes-control-snapping", () => EqualSamples("polyquadratic-guideline", "polyquadratic-manual"));
    Check("rotated-guideline-frame-is-empty", () => EqualSamples("rotated-push", "rotated-plain"));
    Check("glyph-baseline-snaps-Y-without-changing-X", () => EqualSamples("glyph-guideline", "glyph-manual"));
    Check("simple-conic-frame-is-a-device-translation", () => EqualSamples("conic-single-guide", "conic-manual"));
    Check("multi-guide-conic-profile-is-explicit", () =>
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushGuidelineSet(new GuidelineSet([0.25, 5.75], null));
            drawing.DrawEllipse(Brushes.Red, null, new Point(3, 3), 2, 2);
            drawing.Pop();
        }
        try { Render(visual, new DpiScale(1, 1)); }
        catch (NotSupportedException exception) when (exception.Message.Contains("rational conic", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Uncalibrated conic snapping was silently accepted.");
    });
    Check("image-snapping-preserves-source-sampling", () =>
    {
        int[] guided = samples["image-guideline"].Row;
        int[] original = samples["image-identity-guideline"].Row;
        if (!guided.Skip(4).Take(12).SequenceEqual(original.Skip(4).Take(12)))
            throw new InvalidOperationException("Guideline snapping changed the original bitmap's interior sampling coordinates.");
        if (guided[3] != 255 || original[3] >= 255) throw new InvalidOperationException("Image edge coverage was not snapped independently of sampling.");
    });
    Check("checkerboard-snapping-preserves-both-sampling-axes", () =>
    {
        byte[] guided = samples["image-checker-guideline"].Pixels;
        byte[] original = samples["image-checker-identity"].Pixels;
        bool differsOnX = false, differsOnY = false;
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
            {
                int offset = (y * 32 + x) * 4;
                if (!guided.AsSpan(offset, 4).SequenceEqual(original.AsSpan(offset, 4)))
                    throw new InvalidOperationException("Snapping changed the checkerboard's original source sampling phase.");
                differsOnX |= guided[offset + 2] != samples["image-checker-stretch-x"].Pixels[offset + 2];
                differsOnY |= guided[offset + 1] != samples["image-checker-stretch-y"].Pixels[offset + 1];
            }
        if (!differsOnX || !differsOnY) throw new InvalidOperationException("The source-sampling controls cannot distinguish stretching on both axes.");
    });
#endif
    var sampling = BitmapSamplingCases.Run(Render, args);
    results.AddRange(sampling);
    failed |= sampling.Any(item => !item.passed);
    Dispatcher.CurrentDispatcher.InvokeShutdown();
}) { IsBackground = true };
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start();
if (!thread.Join(TimeSpan.FromSeconds(args.Contains("--large-sampling") ? 240 : 30))) throw new TimeoutException("Guideline fixture exceeded its watchdog.");
string corePath = typeof(Visual).Assembly.Location;
Console.WriteLine(JsonSerializer.Serialize(new { core = corePath, coreIdentity = typeof(Visual).Assembly.FullName,
    coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corePath))), results }, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

#if !STOCK
void Check(string name, Action check)
{
    try { check(); results.Add(new { Name = name, passed = true }); }
    catch (Exception exception) { failed = true; results.Add(new { Name = name, passed = false, detail = exception.ToString() }); }
}
void EqualSamples(string first, string second)
{
    if (!samples[first].Pixels.SequenceEqual(samples[second].Pixels))
        throw new InvalidOperationException($"{first} differs from {second} through the same raster backend.");
}
#endif

static IEnumerable<(string Name, Visual Visual, DpiScale Dpi)> Cases()
{
    foreach (double scale in new[] { 1d, 2d, 1.5d })
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushGuidelineSet(new GuidelineSet([0.25, 4.75], [0.25, 4.75]));
            drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0.25, 4.5, 4.5));
            drawing.Pop();
        }
        yield return ($"static-rectangle-{scale}", visual, new DpiScale(scale, scale == 1.5 ? 1.25 : scale));
    }
    byte[] colors = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
    var bitmap = BitmapSource.Create(4, 1, 96, 96, PixelFormats.Pbgra32, null, colors, 16);
    bitmap.Freeze();
    foreach (string mode in new[] { "original", "guideline", "stretched", "identity-guideline" })
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (mode == "guideline") drawing.PushGuidelineSet(new GuidelineSet([0.25, 4.75], null));
            if (mode == "identity-guideline") drawing.PushGuidelineSet(new GuidelineSet([0], null));
            drawing.DrawImage(bitmap, mode == "stretched" ? new Rect(0, 0, 5, 6) : new Rect(0.25, 0, 4.5, 6));
            if (mode is "guideline" or "identity-guideline") drawing.Pop();
        }
        yield return ($"image-{mode}", visual, new DpiScale(1, 1));
    }
    byte[] checker = new byte[4 * 4 * 4];
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            int offset = (y * 4 + x) * 4;
            checker[offset] = (byte)((x + y) % 2 == 0 ? 0 : 255);
            checker[offset + 1] = (byte)(y * 60);
            checker[offset + 2] = (byte)(x * 60);
            checker[offset + 3] = 255;
        }
    var checkerboard = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Pbgra32, null, checker, 16);
    checkerboard.Freeze();
    foreach (string mode in new[] { "guideline", "identity", "stretch-x", "stretch-y" })
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushGuidelineSet(mode == "guideline" ? new GuidelineSet([0.25, 4.75], [0.25, 4.75]) : new GuidelineSet([0], [0]));
            drawing.DrawImage(checkerboard, mode == "stretch-x" ? new Rect(0, 0.25, 5, 4.5) : mode == "stretch-y" ? new Rect(0.25, 0, 4.5, 5) : new Rect(0.25, 0.25, 4.5, 4.5));
            drawing.Pop();
        }
        yield return ($"image-checker-{mode}", visual, new DpiScale(1, 1));
    }
    foreach (string mode in new[] { "half-ties", "negative-device-ties", "negative-scale", "nearest-midpoint", "nested-null", "nested-empty", "rotated-push", "rotated-plain", "transform-after-push", "dynamic-Y1", "dynamic-Y2", "widened-stroke", "cubic-controls", "cubic-manual", "captured-caret" })
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (mode == "half-ties" || mode == "captured-caret")
            {
                drawing.PushTransform(new TranslateTransform(8, 0));
                drawing.PushGuidelineSet(new GuidelineSet([-0.5, 0.5], null));
                drawing.DrawRectangle(Brushes.Red, null, new Rect(-0.5, 0, 1, 6));
                drawing.Pop(); drawing.Pop();
            }
            else if (mode == "negative-device-ties")
            {
                drawing.PushGuidelineSet(new GuidelineSet([-4.5, -0.5], null));
                drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0, 4, 6));
                drawing.Pop();
            }
            else if (mode == "negative-scale")
            {
                drawing.PushTransform(new MatrixTransform(-1, 0, 0, 1, 8, 0));
                drawing.PushGuidelineSet(new GuidelineSet([0.25, 4.75], null));
                drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0, 4.5, 6));
                drawing.Pop(); drawing.Pop();
            }
            else if (mode.StartsWith("dynamic-", StringComparison.Ordinal))
            {
                string method = mode == "dynamic-Y1" ? "PushGuidelineY1" : "PushGuidelineY2";
                typeof(DrawingContext).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawing,
                    mode == "dynamic-Y1" ? [3.25] : [3.25, 1.4]);
                drawing.DrawRectangle(Brushes.Red, null, new Rect(0, mode == "dynamic-Y1" ? 3.25 : 4.65, 6, 1));
                drawing.Pop();
            }
            else
            {
                if (mode.StartsWith("rotated-", StringComparison.Ordinal)) drawing.PushTransform(new RotateTransform(20, 4, 4));
                bool guided = mode is not ("rotated-plain" or "cubic-manual");
                if (guided) drawing.PushGuidelineSet(new GuidelineSet([0.25, 4.75], [0.25, 4.75]));
                if (mode == "nested-null") drawing.PushGuidelineSet(null!);
                if (mode == "nested-empty") drawing.PushGuidelineSet(new GuidelineSet());
                if (mode == "transform-after-push") drawing.PushTransform(new TranslateTransform(0.25, 0.25));
                if (mode == "widened-stroke") drawing.DrawLine(new Pen(Brushes.Red, 1.5), new Point(0.5, 0), new Point(0.5, 6));
                else if (mode is "cubic-controls" or "cubic-manual")
                {
                    var path = mode == "cubic-manual"
                        ? new PathGeometry([new PathFigure(new Point(0, 0), [new BezierSegment(new Point(1, 6.5), new Point(4, 6.5), new Point(5, 0), true)], true)])
                        : new PathGeometry([new PathFigure(new Point(0.25, 0.25), [new BezierSegment(new Point(1.25, 6.25), new Point(3.75, 6.25), new Point(4.75, 0.25), true)], true)]);
                    drawing.DrawGeometry(Brushes.Red, null, path);
                }
                else drawing.DrawRectangle(Brushes.Red, null, mode == "nearest-midpoint" ? new Rect(2.5, 0, 0.25, 6) : new Rect(0.25, 0.25, 4.5, 4.5));
                if (mode == "transform-after-push") drawing.Pop();
                if (mode.StartsWith("nested-", StringComparison.Ordinal)) { drawing.Pop(); drawing.DrawRectangle(Brushes.Blue, null, new Rect(0.25, 7.25, 4.5, 1)); }
                if (guided) drawing.Pop();
                if (mode.StartsWith("rotated-", StringComparison.Ordinal)) drawing.Pop();
            }
        }
        yield return (mode, visual, new DpiScale(1, 1));
    }
    var parent = new ContainerVisual();
    typeof(Visual).GetProperty("VisualXSnappingGuidelines", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(parent, new DoubleCollection { 0.25, 4.75 });
    typeof(Visual).GetProperty("VisualYSnappingGuidelines", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(parent, new DoubleCollection { 0.25, 4.75 });
    var child = new DrawingVisual();
    using (var drawing = child.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0.25, 0.25, 4.5, 4.5));
    parent.Children.Add(child);
    yield return ("visual-child-suppresses-parent-guidelines", parent, new DpiScale(1, 1));
    foreach (bool poly in new[] { false, true })
        foreach (bool guided in new[] { true, false })
        {
            var figure = new PathFigure { StartPoint = guided ? new Point(0.25, 0.25) : new Point(0, 0), IsClosed = true };
            if (guided)
                figure.Segments.Add(poly
                    ? new PolyQuadraticBezierSegment([new Point(2.5, 6.25), new Point(4.75, 0.25)], true)
                    : new QuadraticBezierSegment(new Point(2.5, 6.25), new Point(4.75, 0.25), true));
            else figure.Segments.Add(new BezierSegment(new Point(1.5, 4.5), new Point(3.5, 4.5), new Point(5, 0), true));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                if (guided) drawing.PushGuidelineSet(new GuidelineSet([0.25, 4.75], [0.25, 4.75]));
                drawing.DrawGeometry(Brushes.Red, null, new PathGeometry([figure]));
                if (guided) drawing.Pop();
            }
            yield return ($"{(poly ? "polyquadratic" : "quadratic")}-{(guided ? "guideline" : "manual")}", visual, new DpiScale(1, 1));
        }
    var face = new GlyphTypeface(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf")));
    foreach (bool guided in new[] { true, false })
    {
        var run = new GlyphRun(face, 0, false, 16, 1, [face.CharacterToGlyphMap['H']], new Point(4.25, guided ? 18.65 : 19), [12], null, null, null, null, null, null);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (guided) drawing.PushGuidelineSet(new GuidelineSet([4.5], [18.65]));
            drawing.DrawGlyphRun(Brushes.Red, run);
            if (guided) drawing.Pop();
        }
        yield return (guided ? "glyph-guideline" : "glyph-manual", visual, new DpiScale(1, 1));
    }
    foreach (bool guided in new[] { true, false })
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (guided) drawing.PushGuidelineSet(new GuidelineSet([3.25], [3.25]));
            drawing.DrawEllipse(Brushes.Red, null, new Point(guided ? 3.25 : 3, guided ? 3.25 : 3), 2, 2);
            if (guided) drawing.Pop();
        }
        yield return (guided ? "conic-single-guide" : "conic-manual", visual, new DpiScale(1, 1));
    }
}

static byte[] Render(Visual visual, DpiScale dpi, int width = 32, int height = 24)
{
#if STOCK
    var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    byte[] pixels = new byte[width * height * 4];
    bitmap.CopyPixels(pixels, width * 4, 0);
    return pixels;
#else
    Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
    object target = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [visual, width, height, dpi], null)!;
    try
    {
        Invoke(type, target, "Render");
        using var bitmap = (SKBitmap)Invoke(type, target, "Snapshot")!;
        return bitmap.GetPixelSpan().ToArray();
    }
    finally { ((IDisposable)target).Dispose(); }
#endif
}
#if !STOCK
static void Compare(int[] actual, JsonElement expected, string name, int tolerance)
{
    int[] values = expected.EnumerateArray().Select(value => value.GetInt32()).ToArray();
    for (int index = 0; index < values.Length; index++)
        if (Math.Abs(actual[index] - values[index]) > tolerance)
            throw new InvalidOperationException($"{name}: sample {index} was {actual[index]}, stock WPF was {values[index]}.");
}
static object? Invoke(Type type, object target, string name)
{
    try { return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null); }
    catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
}
#endif
