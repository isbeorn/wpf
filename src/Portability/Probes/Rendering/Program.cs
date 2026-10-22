using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

var checks = new List<ProbeCheck>();
var output = Path.GetFullPath(args.Length == 1 ? args[0] :
    Path.Combine("artifacts", "portability", "rendering", RuntimeInformation.RuntimeIdentifier));

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: Rendering [output-directory]");
    return 2;
}

Check("cpu-raster-scene", () =>
{
    Directory.CreateDirectory(output);
    using var source = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
    source.Erase(SKColors.Cyan);
    using var initial = Draw(source, SKColors.Red);
    ExpectPixel(initial, 8, 8, SKColors.White);
    ExpectPixel(initial, 24, 24, SKColors.Red);
    ExpectPixel(initial, 72, 24, SKColors.Green);
    ExpectPixel(initial, 94, 24, SKColors.White);
    ExpectPixel(initial, 120, 24, SKColors.Magenta);
    ExpectPixel(initial, 104, 24, SKColors.White);
    ExpectPixel(initial, 24, 72, SKColors.Orange);
    ExpectPixel(initial, 176, 24, new SKColor(128, 0, 127), 1);
    ExpectPixel(initial, 224, 24, SKColors.Cyan);
    WritePng(initial, "initial.png");

    source.Erase(SKColors.Yellow);
    source.NotifyPixelsChanged();
    using var changed = Draw(source, SKColors.Blue);
    ExpectPixel(changed, 24, 24, SKColors.Blue);
    ExpectPixel(changed, 224, 24, SKColors.Yellow);
    ExpectPixel(changed, 72, 24, SKColors.Green);
    WritePng(changed, "changed.png");

    using var restored = Draw(source, SKColors.Red);
    ExpectPixel(restored, 24, 24, SKColors.Red);
    source.Erase(SKColors.Cyan);
    source.NotifyPixelsChanged();
    using var roundTrip = Draw(source, SKColors.Red);
    ExpectPixel(roundTrip, 224, 24, SKColors.Cyan);
    Require(initial.Bytes.AsSpan().SequenceEqual(roundTrip.Bytes), "Restoring scene values did not restore original pixels.");
    Require(!initial.Bytes.AsSpan().SequenceEqual(changed.Bytes), "Mutating scene values did not change pixels.");
    return "CPU raster readback verified primitive, path, clip, translation, opacity and bitmap pixels; changed and restored scene values.";
});

Check("png-artifacts", () =>
{
    var initialPath = Path.Combine(output, "initial.png");
    var changedPath = Path.Combine(output, "changed.png");
    using var decodedInitial = SKBitmap.Decode(initialPath) ?? throw new InvalidOperationException("Initial PNG could not be decoded.");
    using var decodedChanged = SKBitmap.Decode(changedPath) ?? throw new InvalidOperationException("Changed PNG could not be decoded.");
    Require(decodedInitial.Width == 256 && decodedInitial.Height == 144, "Unexpected PNG dimensions.");
    ExpectPixel(decodedInitial, 24, 24, SKColors.Red);
    ExpectPixel(decodedChanged, 24, 24, SKColors.Blue);
    ExpectPixel(decodedChanged, 224, 24, SKColors.Yellow);
    var initialHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(initialPath)));
    var changedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(changedPath)));
    Require(initialHash != changedHash, "PNG hashes did not change.");
    return $"Decoded artifacts at {output}; initial SHA256={initialHash}; changed SHA256={changedHash}.";
});

Check("resource-disposal", () =>
{
    for (var iteration = 0; iteration < 32; iteration++)
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8)) ?? throw new InvalidOperationException("Raster surface creation failed.");
        using var paint = new SKPaint { Color = SKColors.Red };
        using var path = new SKPath();
        path.AddRect(new SKRect(0, 0, 8, 8));
        surface.Canvas.DrawPath(path, paint);
        using var image = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(image);
        ExpectPixel(pixels, 4, 4, SKColors.Red);
        Require(surface.Handle != IntPtr.Zero && paint.Handle != IntPtr.Zero && path.Handle != IntPtr.Zero &&
            image.Handle != IntPtr.Zero && pixels.Handle != IntPtr.Zero, "A live native handle was missing.");
        pixels.Dispose();
        image.Dispose();
        path.Dispose();
        paint.Dispose();
        surface.Dispose();
        Require(surface.Handle == IntPtr.Zero && paint.Handle == IntPtr.Zero && path.Handle == IntPtr.Zero &&
            image.Handle == IntPtr.Zero && pixels.Handle == IntPtr.Zero, "Dispose did not clear a native handle.");
    }
    return "32 create/draw/readback/dispose cycles passed; surface, paint, path, image and bitmap handles cleared. This is not a native leak measurement.";
});

checks.Add(new("fork-retained-rendering", "not-tested",
    "Component proof only. No fork WindowsBase/PresentationCore/PresentationFramework assembly is loaded and no RenderData, Visual, dependency property, BAML or NINA view is exercised. See README.md for the retained seam blockers."));

var report = new
{
    probe = "rendering",
    os = RuntimeInformation.OSDescription,
    scope = "component: SkiaSharp CPU raster only; fork retained rendering unproven",
    checks
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return checks.Any(check => check.status == "fail") ? 1 : 0;

void Check(string name, Func<string> action)
{
    try
    {
        checks.Add(new(name, "pass", action()));
    }
    catch (Exception exception)
    {
        checks.Add(new(name, "fail", exception.ToString()));
    }
}

void WritePng(SKBitmap pixels, string name)
{
    using var image = SKImage.FromBitmap(pixels);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("PNG encoding failed.");
    using var stream = File.Create(Path.Combine(output, name));
    encoded.SaveTo(stream);
}

static SKBitmap Draw(SKBitmap source, SKColor primitive)
{
    using var surface = SKSurface.Create(new SKImageInfo(256, 144, SKColorType.Bgra8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Raster surface creation failed.");
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.White);
    using var paint = new SKPaint { Color = primitive, IsAntialias = false };
    canvas.DrawRect(new SKRect(16, 16, 48, 48), paint);

    canvas.Save();
    canvas.ClipRect(new SKRect(64, 16, 88, 48));
    paint.Color = SKColors.Green;
    canvas.DrawRect(new SKRect(56, 8, 104, 56), paint);
    canvas.Restore();

    canvas.Save();
    canvas.Translate(112, 16);
    paint.Color = SKColors.Magenta;
    canvas.DrawRect(new SKRect(0, 0, 32, 32), paint);
    canvas.Restore();

    using var path = new SKPath();
    path.MoveTo(16, 64);
    path.LineTo(48, 64);
    path.LineTo(16, 96);
    path.Close();
    paint.Color = SKColors.Orange;
    canvas.DrawPath(path, paint);

    paint.Color = SKColors.Blue;
    canvas.DrawRect(new SKRect(160, 16, 192, 48), paint);
    using var opacity = new SKPaint { Color = new SKColor(255, 255, 255, 128) };
    canvas.SaveLayer(opacity);
    paint.Color = SKColors.Red;
    canvas.DrawRect(new SKRect(160, 16, 192, 48), paint);
    canvas.Restore();

    canvas.DrawBitmap(source, new SKRect(208, 16, 240, 48));
    canvas.Flush();
    using var snapshot = surface.Snapshot();
    return SKBitmap.FromImage(snapshot);
}

static void ExpectPixel(SKBitmap bitmap, int x, int y, SKColor expected, int tolerance = 0)
{
    var actual = bitmap.GetPixel(x, y);
    Require(Math.Abs(actual.Red - expected.Red) <= tolerance && Math.Abs(actual.Green - expected.Green) <= tolerance &&
        Math.Abs(actual.Blue - expected.Blue) <= tolerance && Math.Abs(actual.Alpha - expected.Alpha) <= tolerance,
        $"Pixel ({x},{y}) was {actual}, expected {expected} within {tolerance}.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record ProbeCheck(string name, string status, string detail);
