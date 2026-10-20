using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

var checks = new List<object>();
var failed = false;
if (!args.Contains("--render-preference-only") && !args.Contains("--visibility-only") && !args.Contains("--drawing-image-only"))
{
    foreach (GeometryCombineMode mode in Enum.GetValues<GeometryCombineMode>())
        Run($"combined-geometry-{mode}-drawing-and-visual-clips", () => CombinedGeometryCases.Clips(mode));
    Run("combined-geometry-fill-rules-hollows-empty-and-nested-operands", CombinedGeometryCases.Operands);
    Run("combined-geometry-draw-fill-and-stroke", CombinedGeometryCases.Draw);
    Run("combined-geometry-retained-clip-mutate-and-restore", () => CombinedGeometryCases.Retained(Drain));
}
if (args.Contains("--combined-geometry-only"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "combined-geometry-rendering", corePath = typeof(DrawingVisual).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
if (!args.Contains("--render-preference-only") && !args.Contains("--visibility-only"))
{
    Run("drawing-image-normalizes-bounds-and-restores-state", DrawingImageCases.BoundsAndState);
    Run("drawing-image-nested-clipped-empty-and-zero-destination", DrawingImageCases.NestedAndEmpty);
    Run("drawing-image-retained-child-mutate-remove-and-restore", () => DrawingImageCases.Retained(Drain));
}
if (args.Contains("--drawing-image-only"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "drawing-image-rendering", corePath = typeof(DrawingVisual).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
Run("process-render-mode-preference-roundtrip-invalid-and-cpu-raster", () =>
{
    Require(RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.Default, "Initial process rendering preference must be Default.");
    foreach (var mode in new[] { System.Windows.Interop.RenderMode.SoftwareOnly, System.Windows.Interop.RenderMode.Default, System.Windows.Interop.RenderMode.SoftwareOnly })
    {
        RenderOptions.ProcessRenderMode = mode;
        Require(RenderOptions.ProcessRenderMode == mode, "Process rendering preference did not retain the requested value.");
        Require(Task.Run(() => RenderOptions.ProcessRenderMode).GetAwaiter().GetResult() == mode, "Process rendering preference was not shared across threads.");
        foreach (int invalid in new[] { -1, 2 })
        {
            var rejection = Expect<System.ComponentModel.InvalidEnumArgumentException>(() => RenderOptions.ProcessRenderMode = (System.Windows.Interop.RenderMode)invalid);
            Require(rejection.ParamName == "value", "Invalid process preference lost its existing argument contract.");
            Require(RenderOptions.ProcessRenderMode == mode, "Rejected process preference changed the stored value.");
        }
        using var target = new Target(Rectangle(Colors.Red, new Rect(0, 0, 8, 8)), 16, 16);
        target.Render();
        using var pixels = target.Snapshot();
        Pixel(pixels, 4, 4, SKColors.Red);
        Pixel(pixels, 12, 12, SKColors.Transparent);
    }
});
if (args.Contains("--render-preference-only"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "process-render-mode-preference", corePath = typeof(DrawingVisual).Assembly.Location, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
var visibility = new List<VisibilityCases.Result>();
Run("visibility-cases-dispatcher", () => visibility.AddRange(VisibilityCases.Run((visual, dpi, width, height) =>
{
    using var target = new Target(visual, width, height, dpi);
    target.Render();
    using var pixels = target.Snapshot();
    return pixels.GetPixelSpan().ToArray();
})));
checks.AddRange(visibility.Select(item => new { name = item.Name, status = item.passed ? "pass" : "fail", detail = item.detail }));
failed |= visibility.Any(item => !item.passed);
if (args.Contains("--visibility-only"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { probe = "retained-visibility", checks }, new JsonSerializerOptions { WriteIndented = true }));
    return failed ? 1 : 0;
}
Run("empty-image-brush-rounded-fill-and-pen-leave-sibling-pixels", () =>
{
    var empty = new ImageBrush();
    var root = new DrawingVisual();
    using (DrawingContext drawing = root.RenderOpen())
    {
        drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 64, 40));
        drawing.DrawRoundedRectangle(empty, new Pen(empty, 4), new Rect(4, 4, 36, 28), 5, 5);
        drawing.DrawRectangle(Brushes.Green, null, new Rect(48, 4, 12, 12));
    }
    using var target = new Target(root, 64, 40);
    Drain();
    using SKBitmap pixels = target.Snapshot();
    for (int y = 0; y < pixels.Height; y++)
        for (int x = 0; x < pixels.Width; x++)
            Require(pixels.GetPixel(x, y) == (x >= 48 && x < 60 && y >= 4 && y < 16 ? SKColors.Green : SKColors.Blue),
                $"Empty ImageBrush changed background or sibling pixel ({x},{y}).");
});
Run("image-brush-source-reject-clear-and-retained-recovery", () =>
{
    BitmapSource image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
        new byte[] { 0, 0, 255, 255 }, 4);
    var brush = new ImageBrush(image);
    var root = new DrawingVisual();
    using (DrawingContext drawing = root.RenderOpen())
    {
        drawing.DrawRoundedRectangle(brush, null, new Rect(2, 2, 20, 20), 3, 3);
        drawing.DrawRectangle(Brushes.Green, null, new Rect(28, 4, 8, 8));
    }
    using var target = new Target(root, 40, 28);
    int frames = 0;
    target.FrameRendered += () => frames++;
    NotSupportedException rejection = Expect<NotSupportedException>(target.Render);
    Require(rejection.Message.Contains(typeof(ImageBrush).FullName!), "Nonempty ImageBrush rejection lost its named diagnostic.");
    Require(frames == 0, "Unsupported ImageBrush completed a frame.");
    brush.ImageSource = null;
    Drain();
    Require(frames > 0, "Clearing ImageSource did not invalidate the retained brush after failure.");
    using SKBitmap cleared = target.Snapshot();
    for (int y = 0; y < cleared.Height; y++)
        for (int x = 0; x < cleared.Width; x++)
            Require(cleared.GetPixel(x, y) == (x >= 28 && x < 36 && y >= 4 && y < 12 ? SKColors.Green : new SKColor(0, 0, 0, 0)),
                $"Cleared ImageBrush left stale paint or lost sibling ({x},{y}).");
    int completed = frames;
    brush.ImageSource = image;
    rejection = Expect<NotSupportedException>(target.Render);
    Require(rejection.Message.Contains(typeof(ImageBrush).FullName!) && frames == completed,
        "Restoring ImageSource did not preserve unsupported-frame semantics.");
    brush.ImageSource = null;
    Drain();
    Require(frames > completed, "Second clear did not produce a retained recovery frame.");
    using SKBitmap restored = target.Snapshot();
    Require(restored.GetPixelSpan().SequenceEqual(cleared.GetPixelSpan()), "Repeated source clear did not restore the complete raster.");
});
Run("retained-dashes-mutate-clear-restore-sibling-and-recover", () => DashedPenRetentionChecks.Run(Drain));
Run("retained-command-bounds-survive-render-and-mutation", () =>
{
    var transform = new MatrixTransform(2, 0, 0, 3, 8, 9);
    var root = new DrawingVisual();
    using (DrawingContext drawing = root.RenderOpen())
    {
        drawing.PushTransform(transform);
        drawing.DrawRectangle(Brushes.Red, new Pen(Brushes.Blue, 2), new Rect(0, 0, 10, 10));
        drawing.Pop();
    }
    Rect before = VisualTreeHelper.GetContentBounds(root);
    Require(before == new Rect(6, 6, 24, 36), $"Retained bounds before rendering were {before}.");
    using var target = new Target(root, 48, 56);
    Drain();
    Require(VisualTreeHelper.GetContentBounds(root) == before, "Rendering changed retained rectangle bounds.");
    using (SKBitmap pixels = target.Snapshot()) Pixel(pixels, 16, 18, SKColors.Red);
    Matrix shifted = transform.Matrix;
    shifted.OffsetX = 12;
    transform.Matrix = shifted;
    Drain();
    Require(VisualTreeHelper.GetContentBounds(root) == new Rect(10, 6, 24, 36), "Retained transform mutation did not update command bounds.");
    using SKBitmap moved = target.Snapshot();
    Pixel(moved, 7, 18, SKColors.Transparent);
    Pixel(moved, 20, 18, SKColors.Red);
});
Run("retained-cubic-geometry-bounds-survive-render", () =>
{
    var curve = new PathGeometry([new PathFigure(new Point(0, 0),
        [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)])
        { Transform = new MatrixTransform(2, 0, 0, 3, 4, 8) };
    var root = new DrawingVisual();
    using (DrawingContext drawing = root.RenderOpen()) drawing.DrawGeometry(Brushes.Red, null, curve);
    Rect before = VisualTreeHelper.GetContentBounds(root);
    Require(before == new Rect(4, 8, 40, 45), $"Retained cubic bounds were {before}.");
    using var target = new Target(root, 52, 60);
    Drain();
    Require(VisualTreeHelper.GetContentBounds(root) == before, "Rendering changed retained cubic bounds.");
    using SKBitmap pixels = target.Snapshot();
    Pixel(pixels, 24, 24, SKColors.Red);
    Pixel(pixels, 24, 56, SKColors.Transparent);
});
Run("outlined-geometry-retains-union-pixels", () =>
{
    var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
    geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, 20, 20)));
    geometry.Children.Add(new RectangleGeometry(new Rect(10, 0, 20, 20)));
    PathGeometry outline = geometry.GetOutlinedPathGeometry();
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.DrawGeometry(Brushes.Red, null, geometry);
        drawing.PushTransform(new TranslateTransform(40, 0));
        drawing.DrawGeometry(Brushes.Red, null, outline);
        drawing.Pop();
    }
    using var target = new Target(root, 80, 24);
    Drain();
    using var pixels = target.Snapshot();
    for (int y = 0; y < 22; y++)
        for (int x = 0; x < 32; x++)
            Require(pixels.GetPixel(x, y) == pixels.GetPixel(x + 40, y), "Outlined union did not preserve retained source pixels.");
});
Run("rounded-rectangle-independent-radius-clamping", () =>
{
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.DrawGeometry(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 20, 20), 100, 2));
        drawing.DrawGeometry(Brushes.Blue, null, new RectangleGeometry(new Rect(24, 0, 20, 20), 2, 100));
        drawing.DrawRoundedRectangle(Brushes.Green, null, new Rect(48, 0, 20, 20), 100, 2);
        drawing.DrawRoundedRectangle(Brushes.Magenta, null, new Rect(72, 0, 20, 20), 2, 100);
    }
    using var target = new Target(root, 96, 24);
    Drain();
    using var pixels = target.Snapshot();
    Pixel(pixels, 0, 0, SKColors.Transparent);
    Pixel(pixels, 24, 0, SKColors.Transparent);
    Pixel(pixels, 48, 0, SKColors.Transparent);
    Pixel(pixels, 72, 0, SKColors.Transparent);
    Pixel(pixels, 10, 10, SKColors.Red);
    Pixel(pixels, 34, 10, SKColors.Blue);
    Pixel(pixels, 58, 10, SKColors.Green);
    Pixel(pixels, 82, 10, SKColors.Magenta);
});
Run("derived-container-retains-children-state-and-invalidation", () =>
{
    var root = new ContainerVisual();
    var container = new ContentContainer { Transform = new TranslateTransform(4, 5), Clip = new RectangleGeometry(new Rect(0, 0, 8, 8)) };
    var brush = new SolidColorBrush(Colors.Red);
    var child = new DrawingVisual();
    using (var context = child.RenderOpen()) context.DrawRectangle(brush, null, new Rect(0, 0, 20, 20));
    container.Children.Add(child);
    root.Children.Add(container);
    root.Children.Add(Rectangle(Colors.Blue, new Rect(22, 2, 6, 6)));
    using var target = new Target(root, 32, 24);
    target.Render();
    using (var pixels = target.Snapshot()) { Pixel(pixels, 6, 7, SKColors.Red); Pixel(pixels, 14, 7, SKColors.Transparent); Pixel(pixels, 24, 4, SKColors.Blue); }
    brush.Color = Colors.Green;
    Drain();
    using (var pixels = target.Snapshot()) Pixel(pixels, 6, 7, SKColors.Green);
    container.Children.Remove(child);
    Drain();
    using (var pixels = target.Snapshot()) { Pixel(pixels, 6, 7, SKColors.Transparent); Pixel(pixels, 24, 4, SKColors.Blue); }
    container.Children.Add(child);
    Drain();
    using (var pixels = target.Snapshot()) Pixel(pixels, 6, 7, SKColors.Green);
    container.Effect = new System.Windows.Media.Effects.BlurEffect();
    var exception = Expect<NotSupportedException>(target.Render);
    Require(exception.Message.Contains("effects", StringComparison.OrdinalIgnoreCase), "Derived container effects were silently dropped.");
    container.Effect = null;
    target.Render();
    using (var pixels = target.Snapshot()) Pixel(pixels, 6, 7, SKColors.Green);
});
Run("content-bearing-visual-neighbors-remain-explicit", () =>
{
    foreach (Visual visual in new Visual[] { new HostVisual(), new System.Windows.Media.Media3D.Viewport3DVisual() })
    {
        using var target = new Target(visual, 12, 12);
        var exception = Expect<NotSupportedException>(target.Render);
        Require(exception.Message.Contains(visual.GetType().FullName!, StringComparison.Ordinal), "A content-bearing visual lost its named unsupported diagnostic.");
    }
});
Run("first-root-dpi-registration-and-child-inheritance", () =>
{
    var root = new ContainerVisual();
    VisualTreeHelper.SetRootDpi(root, new DpiScale(2, 2));
    var independent = new DrawingVisual();
    Require(VisualTreeHelper.GetDpi(independent).DpiScaleX == 1 && VisualTreeHelper.GetDpi(independent).DpiScaleY == 1,
        "The first scaled root changed the default DPI of an unrelated visual.");
    var child = new RenderElement();
    root.Children.Add(child);
    Require(VisualTreeHelper.GetDpi(child).DpiScaleX == 2 && VisualTreeHelper.GetDpi(child).DpiScaleY == 2,
        "An added UIElement did not inherit the root's DPI.");
    Require(child.DpiChangeCount == 1, "Adding a child at a new DPI did not invoke its actual OnDpiChanged.");
    VisualTreeHelper.SetRootDpi(root, new DpiScale(1, 1));
    Require(VisualTreeHelper.GetDpi(child).DpiScaleX == 1 && VisualTreeHelper.GetDpi(child).DpiScaleY == 1 && child.DpiChangeCount == 2,
        "Returning the root to default DPI did not update the child and its DPI callback.");
});
Run("ui-element-layout-retained-content-and-invalidation", () =>
{
    var root = new RenderElement();
    root.Measure(new Size(32, 24));
    root.Arrange(new Rect(0, 0, 32, 24));
    Require(root.RenderCount == 1 && root.IsMeasureValid && root.IsArrangeValid, "Real UIElement layout did not invoke OnRender.");
    using var target = new Target(root, 40, 32);
    Drain();
    using (var initial = target.Snapshot()) Pixel(initial, 8, 8, SKColors.Red);
    root.Fill.Color = Colors.Blue;
    Drain();
    using (var changed = target.Snapshot()) Pixel(changed, 8, 8, SKColors.Blue);
    Require(root.RenderCount == 1, "Retained brush mutation unnecessarily recorded the UIElement again.");
    root.InvalidateVisual();
    root.UpdateLayout();
    Drain();
    Require(root.RenderCount == 2, "InvalidateVisual did not run real UIElement OnRender again.");
    using var repainted = target.Snapshot();
    Pixel(repainted, 8, 8, SKColors.Blue);
});
Run("opacity-animation-forward-background-progress-and-removal", () => ObserveOpacityAnimation(1, 0.25, 1));
Run("opacity-animation-reverse-background-progress-and-removal", () => ObserveOpacityAnimation(0.25, 1, 0.8));
Run("physical-dpi-scaling-and-atomic-resize", () =>
{
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen())
    {
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, 20, 16)));
        drawing.PushTransform(new TranslateTransform(4, 2));
        drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 24, 20));
        drawing.Pop(); drawing.Pop();
    }
    using var target = new Target(root, 32, 24, new DpiScale(1, 1));
    Drain();
    using (var one = target.Snapshot())
    {
        Pixel(one, 6, 4, SKColors.Red);
        Pixel(one, 21, 4, SKColors.Transparent);
    }
    target.Resize(64, 24, new DpiScale(2, 1));
    Drain();
    using (var wide = target.Snapshot())
    {
        Pixel(wide, 12, 4, SKColors.Red);
        Pixel(wide, 12, 18, SKColors.Transparent);
        Pixel(wide, 42, 4, SKColors.Transparent);
    }
    target.Resize(32, 48, new DpiScale(1, 2));
    Drain();
    using (var tall = target.Snapshot())
    {
        Pixel(tall, 6, 8, SKColors.Red);
        Pixel(tall, 21, 8, SKColors.Transparent);
        Pixel(tall, 6, 34, SKColors.Transparent);
    }
    target.Resize(64, 48, new DpiScale(2, 2));
    Drain();
    using (var two = target.Snapshot())
    {
        Require(two.Width == 64 && two.Height == 48, "DPI resize did not use physical surface dimensions.");
        Pixel(two, 12, 8, SKColors.Red);
        Pixel(two, 42, 8, SKColors.Transparent);
        Pixel(two, 4, 8, SKColors.Transparent);
        Pixel(two, 12, 34, SKColors.Transparent);
    }
    foreach (var invalid in new[] { new DpiScale(0, 1), new DpiScale(1, -1), new DpiScale(double.NaN, 1), new DpiScale(1, double.PositiveInfinity) })
        Expect<ArgumentOutOfRangeException>(() => target.Resize(8, 8, invalid));
    Expect<ArgumentOutOfRangeException>(() => target.Resize(0, 8, new DpiScale(1, 1)));
    Expect<InvalidOperationException>(() => target.Resize(int.MaxValue, int.MaxValue, new DpiScale(1, 1)));
    target.Render();
    using (var preserved = target.Snapshot())
    {
        Require(preserved.Width == 64 && preserved.Height == 48, "Rejected resize changed the previous surface.");
        Pixel(preserved, 12, 8, SKColors.Red);
        Pixel(preserved, 4, 8, SKColors.Transparent);
    }
    target.Resize(32, 24, new DpiScale(1, 1));
    Drain();
    using var restored = target.Snapshot();
    Pixel(restored, 6, 4, SKColors.Red);
    Pixel(restored, 21, 4, SKColors.Transparent);
});
Run("frame-notification-mutation-reentry-and-disposal", () =>
{
    var brush = new SolidColorBrush(Colors.Red);
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen()) drawing.DrawRectangle(brush, null, new Rect(0, 0, 16, 16));
    var target = new Target(root, 24, 24);
    int frames = 0;
    target.FrameRendered += () =>
    {
        frames++;
        using var frame = target.Snapshot();
        Pixel(frame, 8, 8, frames == 1 ? SKColors.Red : SKColors.Blue);
        if (frames == 1) brush.Color = Colors.Blue;
    };
    Drain();
    Require(frames == 2, "Mutation from frame notification did not schedule the next frame.");
    target.Dispose();

    var reentrant = new Target(root, 24, 24);
    int reentries = 0;
    reentrant.FrameRendered += () => { if (++reentries == 1) reentrant.Render(); };
    reentrant.Render();
    Require(reentries == 2, "Completed-frame callback could not reenter rendering.");
    reentrant.Dispose();
    Drain();
    Require(reentries == 2, "A disposed target notified pending queued work.");

    var closing = new Target(root, 24, 24);
    int closingFrames = 0;
    closing.FrameRendered += () => { closingFrames++; closing.Dispose(); };
    closing.Render();
    Expect<ObjectDisposedException>(() => closing.Snapshot());
    Drain();
    Require(closingFrames == 1, "Disposal from a completed-frame callback left queued notifications.");
});
Run("frame-notification-failure-and-exception-recovery", () =>
{
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen()) { drawing.PushOpacityMask(Brushes.Red); drawing.Pop(); }
    using var target = new Target(root, 24, 24);
    int frames = 0;
    target.FrameRendered += () => frames++;
    Expect<NotSupportedException>(() => target.Render());
    Require(frames == 0, "A failed frame emitted a successful-frame notification.");
    using (var drawing = root.RenderOpen()) drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 16, 16));
    Drain();
    Require(frames == 1, "Recovery did not produce a completed frame.");
    Action throwing = () => throw new FrameCallbackException();
    target.FrameRendered += throwing;
    Expect<FrameCallbackException>(() => target.Render());
    target.FrameRendered -= throwing;
    target.Render();
    using var recovered = target.Snapshot();
    Pixel(recovered, 8, 8, SKColors.Blue);
    Pixel(recovered, 20, 8, SKColors.Transparent);
});
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
    var oneBounds = InkBounds(changed, 0, 44);
    byte[] onePixels = changed.Bytes;
    target.Resize(192, 112, new DpiScale(2, 2));
    Drain();
    using (var scaled = target.Snapshot())
    {
        var twoBounds = InkBounds(scaled, 0, 88);
        Require(Math.Abs(twoBounds.Left - 2 * oneBounds.Left) <= 2 && Math.Abs(twoBounds.Top - 2 * oneBounds.Top) <= 2 &&
            Math.Abs(twoBounds.Right - 2 * oneBounds.Right) <= 2 && Math.Abs(twoBounds.Bottom - 2 * oneBounds.Bottom) <= 2,
            "Glyph ink did not follow physical DPI scaling on both axes.");
    }
    target.Resize(96, 56, new DpiScale(1, 1));
    Drain();
    using var restored = target.Snapshot();
    Require(onePixels.AsSpan().SequenceEqual(restored.Bytes), "Returning to 1x DPI did not restore exact glyph pixels.");
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
    scope = "Actual fork DrawingVisual, UIElement layout, RenderData and MediaContext rendering to DPI-scaled Skia CPU surfaces; no native window or framework controls.",
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
static void ObserveOpacityAnimation(double from, double to, double baseOpacity)
{
    var root = new RenderElement { Opacity = baseOpacity };
    root.Measure(new Size(32, 24));
    root.Arrange(new Rect(0, 0, 32, 24));
    using var target = new Target(root, 32, 24);
    Drain();
    var clock = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(350)).CreateClock();
    bool intermediateObserved = false;
    int observations = 0;
    var elapsed = Stopwatch.StartNew();
    var frame = new DispatcherFrame();
    root.ApplyAnimationClock(UIElement.OpacityProperty, clock);
    var observer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
    {
        observations++;
        using var pixels = target.Snapshot();
        double opacity = root.Opacity;
        SKColor pixel = pixels.GetPixel(8, 8);
        if (clock.CurrentState == ClockState.Active && clock.CurrentProgress > 0 && clock.CurrentProgress < 1 &&
            opacity > 0.3 && opacity < 0.9 && Math.Abs(pixel.Alpha - opacity * 255) <= 2 &&
            pixel.Red == 255 && pixel.Green == 0 && pixel.Blue == 0)
            intermediateObserved = true;
        if (clock.CurrentState == ClockState.Filling || elapsed.Elapsed > TimeSpan.FromSeconds(3))
            frame.Continue = false;
    }, Dispatcher.CurrentDispatcher);
    try
    {
        Dispatcher.PushFrame(frame);
        Require(intermediateObserved,
            $"Background work missed intermediate clock/property/pixels for {from}->{to}; observations={observations}, opacity={root.Opacity}, progress={clock.CurrentProgress}.");
        Require(clock.CurrentState == ClockState.Filling && root.Opacity == to && root.HasAnimatedProperties,
            "The opacity clock did not reach its held endpoint.");
        using var endpoint = target.Snapshot();
        Pixel(endpoint, 8, 8, new SKColor(255, 0, 0, (byte)Math.Round(to * 255)));
    }
    finally
    {
        observer.Stop();
        root.ApplyAnimationClock(UIElement.OpacityProperty, null);
        Drain();
    }
    Require(!root.HasAnimatedProperties && root.Opacity == baseOpacity, "Removing the opacity clock did not restore the base value.");
    using var restored = target.Snapshot();
    Pixel(restored, 8, 8, new SKColor(255, 0, 0, (byte)Math.Round(baseOpacity * 255)));
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
    internal Target(Visual root, int width, int height, DpiScale? dpi = null)
    {
        type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", throwOnError: true)!;
        object[] arguments = dpi.HasValue ? [root, width, height, dpi.Value] : [root, width, height];
        try { instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)!; }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }
    internal SKBitmap Snapshot() => (SKBitmap)Invoke("Snapshot")!;
    internal void Render() => Invoke("Render");
    internal void Resize(int width, int height) => Invoke("Resize", width, height);
    internal void Resize(int width, int height, DpiScale dpi) => Invoke("Resize", width, height, dpi);
    internal event Action FrameRendered
    {
        add => type.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(instance, [value]);
        remove => type.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetRemoveMethod(true)!.Invoke(instance, [value]);
    }
    internal void Invalidate() => Invoke("Invalidate");
    public void Dispose() => Invoke("Dispose");
    private object? Invoke(string method, params object[] arguments)
    {
        try { return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length).Invoke(instance, arguments); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }
}
internal sealed class RenderElement : UIElement
{
    internal SolidColorBrush Fill { get; } = new SolidColorBrush(Colors.Red);
    internal int RenderCount { get; private set; }
    internal int DpiChangeCount { get; private set; }
    protected override Size MeasureCore(Size availableSize) => new Size(32, 24);
    protected override void OnRender(DrawingContext drawingContext)
    {
        RenderCount++;
        drawingContext.DrawRectangle(Fill, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        DpiChangeCount++;
        base.OnDpiChanged(oldDpi, newDpi);
    }
}
internal sealed class FrameCallbackException : Exception;

internal sealed class ContentContainer : ContainerVisual;
