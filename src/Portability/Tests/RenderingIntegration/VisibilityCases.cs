using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
#if !STOCK
using SkiaSharp;
#endif

internal static class VisibilityCases
{
    internal sealed record Result(string Name, bool passed, string? detail = null);
    internal static IReadOnlyList<Result> Run(Func<Visual, DpiScale, int, int, byte[]> render)
    {
        var results = new List<Result>();
        void Check(string name, Action test)
        {
            try { test(); results.Add(new(name, true)); }
            catch (Exception exception) { results.Add(new(name, false, exception.ToString())); }
        }
        byte[] Pixels(Visual visual) => render(visual, new DpiScale(1, 1), 32, 24);
        Check("visibility-unattached-visible-root-renders", () =>
        {
            var element = Element(Brushes.Red);
            Require(!element.IsVisible, "The root must remain unattached for this offscreen check.");
            Red(Pixels(element));
        });
        foreach (Visibility visibility in new[] { Visibility.Hidden, Visibility.Collapsed })
        {
            foreach (string content in new[] { "image-brush", "effect", "opacity-mask" })
            {
                Check($"visibility-{visibility}-root-{content}-skips", () =>
                {
                    var element = UnsupportedElement(content);
                    element.Visibility = visibility;
                    Clear(Pixels(element));
                });
                Check($"visibility-{visibility}-ancestor-{content}-skips", () =>
                {
                    var child = UnsupportedElement(content);
                    var ancestor = new Host(child);
                    Arrange(ancestor);
                    ancestor.Visibility = visibility;
                    Clear(Pixels(ancestor));
                });
            }
        }
        foreach (string content in new[] { "image-brush", "effect", "opacity-mask" })
        {
            Check($"visibility-zero-opacity-root-{content}-skips", () =>
            {
                var visual = UnsupportedDrawing(content);
                visual.Opacity = 0;
                Clear(Pixels(visual));
            });
            Check($"visibility-zero-opacity-ancestor-{content}-skips", () =>
            {
                var root = new ContainerVisual { Opacity = 0 };
                root.Children.Add(UnsupportedDrawing(content));
                Clear(Pixels(root));
            });
        }
        Check("visibility-hidden-collapsed-visible-pixel-round-trips", () =>
        {
            var element = Element(Brushes.Red);
            foreach (Visibility visibility in new[] { Visibility.Visible, Visibility.Hidden, Visibility.Visible, Visibility.Collapsed, Visibility.Visible })
            {
                element.Visibility = visibility;
                Arrange(element);
                byte[] pixels = Pixels(element);
                if (visibility == Visibility.Visible) Red(pixels); else Clear(pixels);
            }
        });
        Check("visibility-raw-opacity-zero-one-zero-pixels", () =>
        {
            var visual = Drawing(Brushes.Red);
            foreach (double opacity in new[] { 0.0, 1.0, 0.0 })
            {
                visual.Opacity = opacity;
                byte[] pixels = Pixels(visual);
                if (opacity == 0) Clear(pixels); else Red(pixels);
            }
        });
        Check("visibility-skipped-guidelines-and-sampling-do-not-affect-sibling", () =>
        {
            var skipped = Drawing(Image());
            skipped.Opacity = 0;
            skipped.XSnappingGuidelines = new DoubleCollection([0.25]);
            skipped.YSnappingGuidelines = new DoubleCollection([0.25]);
            RenderOptions.SetBitmapScalingMode(skipped, BitmapScalingMode.NearestNeighbor);
            var sibling = new DrawingVisual();
            byte[] source = [0, 0, 255, 255, 255, 0, 0, 255];
            var image = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, source, 8);
            using (DrawingContext dc = sibling.RenderOpen()) dc.DrawImage(image, new Rect(0.25, 0.25, 15, 12));
            byte[] expected = Pixels(sibling);
            var root = new ContainerVisual();
            root.Children.Add(skipped); root.Children.Add(sibling);
            Require(Pixels(root).SequenceEqual(expected), "Skipped visual guidelines or sampling changed sibling pixels.");
        });
        Check("visibility-skipped-recursive-brush-does-not-obscure-sibling", () =>
        {
            var skipped = new DrawingVisual { Opacity = 0 };
            using (DrawingContext dc = skipped.RenderOpen()) dc.DrawRectangle(new VisualBrush(skipped), null, new Rect(0, 0, 16, 16));
            var root = new ContainerVisual();
            root.Children.Add(skipped); root.Children.Add(Drawing(Brushes.Red));
            Red(Pixels(root));
        });
#if !STOCK
        Check("visibility-visible-unsupported-content-remains-explicit-and-recovers", () =>
        {
            var visual = UnsupportedDrawing("image-brush");
            visual.Opacity = 0;
            using var target = new LiveTarget(visual);
            target.Render(); Clear(target.Pixels());
            visual.Opacity = 1;
            try { target.Render(); throw new InvalidOperationException("Visible ImageBrush was silently accepted."); }
            catch (NotSupportedException exception) when (exception.Message.Contains("ImageBrush", StringComparison.Ordinal)) { }
            using (DrawingContext dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 16, 16));
            target.Render(); Red(target.Pixels());
            visual.Opacity = 0;
            target.Render(); Clear(target.Pixels());
        });
        Check("visibility-hidden-consumer-releases-and-restores-brush-source", () =>
        {
            var fill = new SolidColorBrush(Colors.Red);
            var source = Drawing(fill);
            var child = Drawing(new VisualBrush(source));
            var root = new ContainerVisual(); root.Children.Add(child);
            using var target = new LiveTarget(root);
            int frames = 0; target.FrameRendered += () => frames++;
            Drain(); Red(target.Pixels());
            root.Opacity = 0; Drain(); Clear(target.Pixels());
            int before = frames; fill.Color = Colors.Blue; Drain();
            Require(frames == before, "An off-tree source still invalidated its hidden consumer.");
            root.Opacity = 1; Drain();
            ColorAt(target.Pixels(), Colors.Blue);
            before = frames; fill.Color = Colors.Red; Drain();
            Require(frames > before, "The visible consumer did not regain its source dependency.");
            Red(target.Pixels());
            root.Opacity = 0; Drain();
            before = frames; fill.Color = Colors.Green; Drain();
            Require(frames == before, "A second hide retained the source dependency.");
        });
        Check("visibility-zero-opacity-direct-brush-source-recovers", () =>
        {
            var source = Drawing(Brushes.Red); source.Opacity = 0;
            using var target = new LiveTarget(Drawing(new VisualBrush(source)));
            Drain(); Clear(target.Pixels());
            source.Opacity = 1; Drain(); Red(target.Pixels());
            source.Opacity = 0; Drain(); Clear(target.Pixels());
            source.Opacity = 1; Drain(); Red(target.Pixels());
        });
#endif
        return results;
    }

    private static Probe Element(Brush fill) { var element = new Probe(fill); Arrange(element); return element; }
    private static Probe UnsupportedElement(string content)
    {
        var element = Element(content == "image-brush" ? Image() : Brushes.Red);
        if (content == "effect") element.Effect = new BlurEffect { Radius = 1 };
        if (content == "opacity-mask") element.OpacityMask = Brushes.White;
        return element;
    }
    private static DrawingVisual UnsupportedDrawing(string content)
    {
        var visual = Drawing(content == "image-brush" ? Image() : Brushes.Red);
        if (content == "effect") visual.Effect = new BlurEffect { Radius = 1 };
        if (content == "opacity-mask") visual.OpacityMask = Brushes.White;
        return visual;
    }
    private static DrawingVisual Drawing(Brush fill)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, 16, 16), 0, 0);
        return visual;
    }
    private static ImageBrush Image() => new(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4));
    private static void Arrange(UIElement element) { element.Measure(new Size(16, 16)); element.Arrange(new Rect(0, 0, 16, 16)); element.UpdateLayout(); }
    private static void Clear(byte[] pixels) => Require(pixels.Length == 32 * 24 * 4 && pixels.All(value => value == 0), "An invisible subtree contributed pixels.");
    private static void Red(byte[] pixels) => ColorAt(pixels, Colors.Red);
    private static void ColorAt(byte[] pixels, Color color)
    {
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 32; x++)
            {
                int offset = (y * 32 + x) * 4;
                bool inside = x < 16 && y < 16;
                byte b = inside ? color.B : (byte)0, g = inside ? color.G : (byte)0, r = inside ? color.R : (byte)0, a = inside ? color.A : (byte)0;
                Require(pixels[offset] == b && pixels[offset + 1] == g && pixels[offset + 2] == r && pixels[offset + 3] == a,
                    $"Visibility pixel ({x},{y}) did not match the retained rectangle.");
            }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
#if !STOCK
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private sealed class LiveTarget : IDisposable
    {
        private readonly Type _type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        private readonly object _instance;
        internal LiveTarget(Visual root) => _instance = Activator.CreateInstance(_type, BindingFlags.Instance | BindingFlags.NonPublic, null, [root, 32, 24], null)!;
        internal void Render() => Invoke("Render");
        internal byte[] Pixels() { using var bitmap = (SKBitmap)Invoke("Snapshot")!; return bitmap.GetPixelSpan().ToArray(); }
        internal event Action FrameRendered
        {
            add => _type.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(_instance, [value]);
            remove => _type.GetEvent("FrameRendered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetRemoveMethod(true)!.Invoke(_instance, [value]);
        }
        public void Dispose() => Invoke("Dispose");
        private object? Invoke(string name)
        {
            try { return _type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(_instance, null); }
            catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
        }
    }
#endif
    private sealed class Probe(Brush fill) : UIElement
    {
        protected override Size MeasureCore(Size availableSize) => new(16, 16);
        protected override void OnRender(DrawingContext dc) => dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, 16, 16), 0, 0);
    }
    private sealed class Host : UIElement
    {
        private readonly UIElement _child;
        internal Host(UIElement child) { _child = child; AddVisualChild(child); }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _child : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureCore(Size availableSize) { _child.Measure(availableSize); return new(16, 16); }
        protected override void ArrangeCore(Rect finalRect) { base.ArrangeCore(finalRect); _child.Arrange(new Rect(0, 0, 16, 16)); }
    }
}
