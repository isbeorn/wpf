using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;

var checks = new List<string>();
using var watchdog = new Timer(_ =>
{
    Console.Error.WriteLine("FAIL: PresentationCore integration exceeded its 20-second watchdog.");
    Environment.Exit(1);
}, null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
try
{
    var core = typeof(DrawingVisual).Assembly;
    Require(core.GetName().Version == new Version(10, 0, 0, 0), "source-built PresentationCore identity");
    Require(core.Location.Contains("portability", StringComparison.OrdinalIgnoreCase), "source-built PresentationCore location");
    var acquire = core.GetType("System.Windows.Media.Composition.CompositionEngineLock", true)!
        .GetMethod("Acquire", BindingFlags.Static | BindingFlags.NonPublic)!;
    try
    {
        using var outer = (IDisposable)acquire.Invoke(null, null)!;
        using var inner = (IDisposable)acquire.Invoke(null, null)!;
        var geometryDrawing = new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 2, 2)));
        Require(geometryDrawing.Brush == Brushes.Red, "composition gate permits nested real drawing construction");
        throw new InvalidOperationException("expected composition gate release");
    }
    catch (InvalidOperationException exception) when (exception.Message == "expected composition gate release") { }
    Exception? lockFailure = null;
    var lockThread = new Thread(() =>
    {
        try { _ = new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(0, 0, 2, 2))); }
        catch (Exception exception) { lockFailure = exception; }
    }) { IsBackground = true };
    lockThread.Start();
    Require(lockThread.Join(TimeSpan.FromSeconds(5)) && lockFailure == null,
        "composition gate releases after exception for another thread");
    var visual = new DrawingVisual();
    Require(visual.Drawing == null, "empty DrawingVisual construction");
    var parent = new ContainerVisual();
    parent.Children.Add(visual);
    Require(ReferenceEquals(VisualTreeHelper.GetParent(visual), parent), "real visual child attachment");
    Require(VisualTreeHelper.GetDpi(visual).Equals(new DpiScale(1, 1)), "unattached portable host defaults to 96 DPI");
    parent.Children.Remove(visual);
    Require(VisualTreeHelper.GetParent(visual) == null && parent.Children.Count == 0, "real visual child removal");
    parent.Children.Add(visual);
    Require(parent.Children.Count == 1, "real visual child reattachment");
    parent.Children.Remove(visual);
    var brush = new SolidColorBrush(Colors.Red);
    using (var context = visual.RenderOpen())
    {
        context.DrawRectangle(brush, null, new Rect(2, 3, 20, 10));
    }
    Require(visual.Drawing!.Children.Count == 1, "retained DrawingContext recording");
    var drawing = (GeometryDrawing)visual.Drawing!.Children[0];
    Require(((RectangleGeometry)drawing.Geometry).Rect == new Rect(2, 3, 20, 10), "retained rectangle coordinates");
    Require(visual.ContentBounds == new Rect(2, 3, 20, 10), "actual retained content bounds");
    Require(visual.DescendantBounds == new Rect(2, 3, 20, 10), "actual visual precompute bounds");
    Require(ReferenceEquals(drawing.Brush, brush), "retained brush identity");
    brush.Color = Colors.Blue;
    Require(((SolidColorBrush)((GeometryDrawing)visual.Drawing!.Children[0]).Brush).Color == Colors.Blue, "retained mutable brush");
    using (var context = visual.RenderOpen())
    {
        context.DrawRectangle(Brushes.Green, null, new Rect(0, 0, 5, 6));
    }
    Require(visual.Drawing!.Children.Count == 1, "RenderOpen replaces old content");
    using (visual.RenderOpen()) { }
    Require(visual.Drawing == null, "empty RenderOpen removes content");

    var retained = new DrawingVisual();
    var retainedBrush = new SolidColorBrush(Colors.Red);
    using (var context = retained.RenderOpen())
        context.DrawRectangle(retainedBrush, null, new Rect(0, 0, 12, 12));
    using (var target = new Target(retained))
    {
        Drain();
        var mediaContext = MediaContext();
        var rendering = mediaContext.GetType().GetEvent("Rendering", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int frames = 0;
        EventHandler? handler = null;
        handler = (sender, args) =>
        {
            frames++;
            rendering.GetRemoveMethod(true)!.Invoke(mediaContext, [handler]);
        };
        rendering.GetAddMethod(true)!.Invoke(mediaContext, [handler]);
        retainedBrush.Color = Colors.Blue;
        retainedBrush.Color = Colors.Green;
        retainedBrush.Color = Colors.Yellow;
        Drain();
        Require(frames == 1, "Render priority coalesces resource mutations");
        using (var pixels = target.Snapshot())
            Require(pixels.GetPixel(4, 4) == SKColors.Yellow, "coalesced frame reads latest retained brush");

        retainedBrush.Color = Colors.Magenta;
        Drain();
        using (var pixels = target.Snapshot())
            Require(pixels.GetPixel(4, 4) == SKColors.Magenta, "second frame after unchanged native dirty flags");
        retainedBrush.Color = Colors.Cyan;
        Drain();
        using (var pixels = target.Snapshot())
            Require(pixels.GetPixel(4, 4) == SKColors.Cyan, "third frame after unchanged native dirty flags");

        var replacementBrush = new SolidColorBrush(Colors.Red);
        using (var context = retained.RenderOpen())
            context.DrawRectangle(replacementBrush, null, new Rect(0, 0, 12, 12));
        Drain();
        frames = 0;
        rendering.GetAddMethod(true)!.Invoke(mediaContext, [handler]);
        retainedBrush.Color = Colors.Black;
        Drain();
        rendering.GetRemoveMethod(true)!.Invoke(mediaContext, [handler]);
        Require(frames == 0, "RenderClose unsubscribes detached brush invalidation");

        int passes = 0;
        EventHandler? reentrant = null;
        reentrant = (sender, args) =>
        {
            passes++;
            if (passes == 1) replacementBrush.Color = Colors.Blue;
            else rendering.GetRemoveMethod(true)!.Invoke(mediaContext, [reentrant]);
        };
        rendering.GetAddMethod(true)!.Invoke(mediaContext, [reentrant]);
        replacementBrush.Color = Colors.Green;
        Drain();
        Require(passes == 2, "mutation during rendering schedules following pass");
        using (var pixels = target.Snapshot())
            Require(pixels.GetPixel(4, 4) == SKColors.Blue, "reentrant frame retains latest resource value");
    }
    Exception? shutdownFailure = null;
    var shutdownThread = new Thread(() =>
    {
        try
        {
            using var target = new Target(new DrawingVisual());
            var mediaContext = MediaContext();
            var rendering = mediaContext.GetType().GetEvent("Rendering", BindingFlags.Instance | BindingFlags.NonPublic)!;
            EventHandler shutdown = (sender, args) => Dispatcher.CurrentDispatcher.InvokeShutdown();
            rendering.GetAddMethod(true)!.Invoke(mediaContext, [shutdown]);
            Drain();
            if (!Dispatcher.CurrentDispatcher.HasShutdownFinished)
                throw new InvalidOperationException("Rendering shutdown did not finish.");
        }
        catch (Exception exception) { shutdownFailure = exception; }
    }) { IsBackground = true };
    shutdownThread.Start();
    Require(shutdownThread.Join(TimeSpan.FromSeconds(5)), "rendering shutdown completes within watchdog");
    Require(shutdownFailure == null, $"shutdown during rendering preserves lifecycle: {shutdownFailure}");
    Dispatcher.CurrentDispatcher.InvokeShutdown();
    Console.WriteLine($"PASS {checks.Count}: {string.Join("; ", checks)}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL after {checks.Count} checks: {exception}");
    return 1;
}

void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks.Add(name);
}

static void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}

static object MediaContext() => typeof(Visual).Assembly.GetType("System.Windows.Media.MediaContext", true)!
    .GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Dispatcher.CurrentDispatcher])!;

internal sealed class Target : IDisposable
{
    private readonly object instance;
    private readonly Type type;

    internal Target(Visual root)
    {
        type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [root, 16, 16], null)!;
    }

    internal SKBitmap Snapshot() => (SKBitmap)type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null)!;
    public void Dispose() => ((IDisposable)instance).Dispose();
}
