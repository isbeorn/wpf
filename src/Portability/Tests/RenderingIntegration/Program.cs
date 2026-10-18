using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;

var checks = new List<object>();
var failed = false;
Run("retained-drawing-and-resource-invalidation", () =>
{
    var root = new DrawingVisual();
    var brush = new SolidColorBrush(Colors.Red);
    var transform = new TranslateTransform(12, 8);
    using (var drawing = root.RenderOpen())
    {
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, 48, 48)));
        drawing.PushTransform(transform);
        drawing.PushOpacity(0.5);
        drawing.DrawRectangle(brush, null, new Rect(0, 0, 48, 48));
        drawing.Pop(); drawing.Pop(); drawing.Pop();
    }
    using var target = new Target(root, 80, 64);
    Drain();
    using (var initial = target.Snapshot())
    {
        Pixel(initial, 16, 16, new SKColor(255, 0, 0, 128));
        Pixel(initial, 50, 16, SKColors.Transparent);
        Pixel(initial, 4, 16, SKColors.Transparent);
    }
    brush.Color = Colors.Blue;
    Drain();
    using (var changed = target.Snapshot()) Pixel(changed, 16, 16, new SKColor(0, 0, 255, 128));
    transform.X = 24;
    Drain();
    using (var moved = target.Snapshot())
    {
        Pixel(moved, 16, 16, SKColors.Transparent);
        Pixel(moved, 28, 16, new SKColor(0, 0, 255, 128));
    }
    transform.X = 12;
    brush.Color = Colors.Red;
    Drain();
    using (var restored = target.Snapshot()) Pixel(restored, 16, 16, new SKColor(255, 0, 0, 128));
    var replacement = new SolidColorBrush(Colors.Green);
    using (var drawing = root.RenderOpen()) drawing.DrawRectangle(replacement, null, new Rect(0, 0, 10, 10));
    Drain();
    using (var replaced = target.Snapshot())
    {
        Pixel(replaced, 4, 4, SKColors.Green);
        Pixel(replaced, 28, 16, SKColors.Transparent);
    }
    int renderPosts = 0;
    DispatcherHookEventHandler onPosted = (_, args) => { if (args.Operation.Priority == DispatcherPriority.Render) renderPosts++; };
    Dispatcher.CurrentDispatcher.Hooks.OperationPosted += onPosted;
    brush.Color = Colors.Yellow;
    transform.X = 40;
    Drain();
    Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= onPosted;
    Require(renderPosts == 0, "Replaced content retained old resource change subscriptions.");
    using (var detached = target.Snapshot()) Pixel(detached, 4, 4, SKColors.Green);
    replacement.Color = Colors.Magenta;
    Drain();
    using (var attached = target.Snapshot()) Pixel(attached, 4, 4, SKColors.Magenta);
});
Run("children-order-properties-add-and-remove", () =>
{
    var root = new ContainerVisual();
    var bottom = Rectangle(Colors.Red, new Rect(0, 0, 32, 32));
    var top = Rectangle(Colors.Blue, new Rect(0, 0, 32, 32));
    top.Offset = new Vector(8, 8);
    top.Clip = new RectangleGeometry(new Rect(0, 0, 16, 16));
    root.Children.Add(bottom);
    root.Children.Add(top);
    using var target = new Target(root, 48, 48);
    Drain();
    using (var pixels = target.Snapshot())
    {
        Pixel(pixels, 12, 12, SKColors.Blue);
        Pixel(pixels, 28, 12, SKColors.Red);
    }
    top.Opacity = 0;
    Drain();
    using (var hidden = target.Snapshot()) Pixel(hidden, 12, 12, SKColors.Red);
    top.Opacity = 1;
    Drain();
    using (var shown = target.Snapshot()) Pixel(shown, 12, 12, SKColors.Blue);
    root.Children.Remove(top);
    Drain();
    using (var removed = target.Snapshot()) Pixel(removed, 12, 12, SKColors.Red);
    root.Children.Add(top);
    Drain();
    using (var added = target.Snapshot()) Pixel(added, 12, 12, SKColors.Blue);
});
Run("geometry-drawing-group-and-stack", () =>
{
    var group = new DrawingGroup { Transform = new TranslateTransform(8, 0) };
    group.Children.Add(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.DrawDrawing(group);
        drawing.DrawEllipse(Brushes.Blue, null, new Point(40, 8), 6, 6);
        drawing.DrawLine(new Pen(Brushes.Green, 4), new Point(4, 28), new Point(44, 28));
    }
    using var target = new Target(root, 64, 40);
    Drain();
    using var pixels = target.Snapshot();
    Pixel(pixels, 12, 8, SKColors.Red);
    Pixel(pixels, 40, 8, SKColors.Blue);
    Pixel(pixels, 20, 28, SKColors.Green);
    Pixel(pixels, 4, 8, SKColors.Transparent);
});
Run("actual-glyph-run-advances-offsets-and-brush-invalidation", () =>
{
    string fontPath = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
        : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    var typeface = new GlyphTypeface(new Uri(fontPath));
    ushort glyph = typeface.CharacterToGlyphMap['H'];
    var run = new GlyphRun(typeface, 0, false, 24, 1, [glyph, glyph], new Point(8, 40),
        [40, 40], [new Point(0, 0), new Point(4, 8)], null, null, null, null, null);
    var brush = new SolidColorBrush(Colors.Red);
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen()) drawing.DrawGlyphRun(brush, run);
    using var target = new Target(root, 96, 56);
    Drain();
    using (var pixels = target.Snapshot())
    {
        var first = InkBounds(pixels, 0, 44);
        var second = InkBounds(pixels, 44, 96);
        Require(first.Right > first.Left && second.Right > second.Left, "The actual GlyphRun did not draw both glyphs.");
        Require(second.Left - first.Left == 44 && second.Top - first.Top == -8,
            "The renderer did not honor supplied glyph advance widths and offsets.");
        Require(first.Top > 0 && first.Bottom <= 40 && second.Bottom <= 32, "Glyph baseline placement changed.");
    }
    brush.Color = Colors.Blue;
    Drain();
    using var changed = target.Snapshot();
    bool blue = false;
    for (int y = 0; y < changed.Height; y++)
        for (int x = 0; x < changed.Width; x++)
        {
            SKColor color = changed.GetPixel(x, y);
            if (color.Alpha > 0) { Require(color.Blue > 250 && color.Red < 2, "Glyph brush mutation left old ink."); blue = true; }
        }
    Require(blue, "Glyph run had no ink after brush mutation.");
});
Run("stream-path-transform-and-composite-opacity", () =>
{
    var path = new StreamGeometry { Transform = new TranslateTransform(8, 0) };
    using (var context = path.Open())
    {
        context.BeginFigure(new Point(0, 0), true, true);
        context.LineTo(new Point(24, 0), true, false);
        context.LineTo(new Point(0, 24), true, false);
    }
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.DrawGeometry(Brushes.Green, null, path);
        drawing.PushOpacity(0.5);
        drawing.DrawRectangle(Brushes.Red, null, new Rect(40, 0, 24, 24));
        drawing.DrawRectangle(Brushes.Blue, null, new Rect(48, 0, 24, 24));
        drawing.Pop();
        drawing.DrawLine(new Pen(Brushes.Red, 0), new Point(0, 30), new Point(72, 30));
    }
    using var target = new Target(root, 80, 40);
    Drain();
    using var pixels = target.Snapshot();
    Pixel(pixels, 12, 4, SKColors.Green);
    Pixel(pixels, 4, 4, SKColors.Transparent);
    Pixel(pixels, 28, 16, SKColors.Transparent);
    Pixel(pixels, 44, 12, new SKColor(255, 0, 0, 128));
    Pixel(pixels, 52, 12, new SKColor(0, 0, 255, 128));
    Pixel(pixels, 12, 30, SKColors.Transparent);
});
Run("resize-expose-and-dispose-pending-frame", () =>
{
    var root = Rectangle(Colors.Red, new Rect(0, 0, 96, 96));
    for (var index = 0; index < 16; index++)
    {
        var target = new Target(root, 8, 8);
        Drain();
        target.Resize(32, 24);
        Drain();
        using (var resized = target.Snapshot())
        {
            Require(resized.Width == 32 && resized.Height == 24, "Resize retained the old surface dimensions.");
            Pixel(resized, 28, 20, SKColors.Red);
        }
        target.Invalidate();
        Drain();
        using (var exposed = target.Snapshot()) Pixel(exposed, 28, 20, SKColors.Red);
        target.Resize(16, 16);
        target.Dispose();
        target.Dispose();
        Drain();
        Expect<ObjectDisposedException>(() => target.Snapshot());
        int closedRenderPosts = 0;
        DispatcherHookEventHandler onPosted = (_, args) => { if (args.Operation.Priority == DispatcherPriority.Render) closedRenderPosts++; };
        Dispatcher.CurrentDispatcher.Hooks.OperationPosted += onPosted;
        root.Opacity = root.Opacity == 1 ? 0.75 : 1;
        Drain();
        Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= onPosted;
        Require(closedRenderPosts == 0, "A closed render target kept scheduling frames.");
        root.Opacity = 1;
    }
});
Run("surface-affinity-and-invalid-resize", () =>
{
    var root = Rectangle(Colors.Red, new Rect(0, 0, 32, 32));
    using var target = new Target(root, 16, 16);
    Drain();
    Expect<ArgumentOutOfRangeException>(() => target.Resize(0, 16));
    Expect<ArgumentOutOfRangeException>(() => target.Resize(16, -1));
    using (var valid = target.Snapshot())
    {
        Require(valid.Width == 16 && valid.Height == 16, "Rejected resize destroyed the existing surface.");
        Pixel(valid, 8, 8, SKColors.Red);
    }
    Exception? wrongThread = null;
    var worker = new Thread(() => { try { target.Dispose(); } catch (Exception exception) { wrongThread = exception; } });
    worker.Start();
    Require(worker.Join(2000), "Affinity worker did not finish.");
    Require(wrongThread is InvalidOperationException, "Foreign-thread disposal did not reject access.");
    using var after = target.Snapshot();
    Pixel(after, 8, 8, SKColors.Red);
});
Run("unsupported-command-and-canvas-recovery", () =>
{
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.PushTransform(new TranslateTransform(20, 0));
        drawing.PushOpacityMask(Brushes.Red);
        drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 16, 16));
        drawing.Pop(); drawing.Pop();
    }
    using var target = new Target(root, 40, 24);
    var exception = Expect<NotSupportedException>(() => target.Render());
    Require(exception.Message.Contains("opacity mask", StringComparison.OrdinalIgnoreCase), "Unsupported operation omitted its name.");
    using (var drawing = root.RenderOpen()) drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 16, 16));
    Drain();
    using var recovered = target.Snapshot();
    Pixel(recovered, 4, 4, SKColors.Blue);
    Pixel(recovered, 24, 4, SKColors.Transparent);
});
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "retained-rendering-integration",
    os = RuntimeInformation.OSDescription,
    core = typeof(DrawingVisual).Assembly.FullName,
    corePath = typeof(DrawingVisual).Assembly.Location,
    scope = "Actual fork DrawingVisual, RenderData and MediaContext rendering to Skia CPU surfaces; no native window or framework controls.",
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void Run(string name, Action test)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { test(); }
        catch (Exception exception) { failure = exception; }
        finally
        {
            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            if (dispatcher != null && !dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown();
        }
    }) { IsBackground = true, Name = name };
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(15))) failure = new TimeoutException("Rendering test exceeded its watchdog.");
    failed |= failure != null;
    checks.Add(new { name, status = failure == null ? "pass" : "fail", detail = failure?.ToString() ?? "Observable retained pixels passed." });
}
static DrawingVisual Rectangle(Color color, Rect bounds)
{
    var visual = new DrawingVisual();
    using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(color), null, bounds);
    return visual;
}
static void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}
static void Pixel(SKBitmap bitmap, int x, int y, SKColor expected)
{
    var actual = bitmap.GetPixel(x, y);
    Require((expected.Alpha == 0 || Math.Abs(actual.Red - expected.Red) <= 1 && Math.Abs(actual.Green - expected.Green) <= 1 &&
        Math.Abs(actual.Blue - expected.Blue) <= 1) && Math.Abs(actual.Alpha - expected.Alpha) <= 1,
        $"Pixel ({x},{y}) was {actual}, expected {expected}.");
}
static (int Left, int Top, int Right, int Bottom) InkBounds(SKBitmap pixels, int left, int right)
{
    var bounds = (Left: right, Top: pixels.Height, Right: left, Bottom: 0);
    for (int y = 0; y < pixels.Height; y++)
        for (int x = left; x < right; x++)
            if (pixels.GetPixel(x, y).Alpha > 0)
                bounds = (Math.Min(bounds.Left, x), Math.Min(bounds.Top, y), Math.Max(bounds.Right, x + 1), Math.Max(bounds.Bottom, y + 1));
    return bounds;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static T Expect<T>(Action action) where T : Exception
{
    try { action(); } catch (T exception) { return exception; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
internal sealed class Target : IDisposable
{
    private readonly object instance;
    private readonly Type type;
    internal Target(Visual root, int width, int height)
    {
        type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", throwOnError: true)!;
        try { instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [root, width, height], null)!; }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }
    internal SKBitmap Snapshot() => (SKBitmap)Invoke("Snapshot")!;
    internal void Render() => Invoke("Render");
    internal void Resize(int width, int height) => Invoke("Resize", width, height);
    internal void Invalidate() => Invoke("Invalidate");
    public void Dispose() => Invoke("Dispose");
    private object? Invoke(string method, params object[] arguments)
    {
        try { return type.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(instance, arguments); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }
}
