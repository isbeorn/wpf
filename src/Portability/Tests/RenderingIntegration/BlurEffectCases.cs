using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class BlurEffectCases
{
    private const int Width = 96, Height = 80;

    internal static void Gaussian()
    {
        foreach (int scale in new[] { 1, 2 })
        {
            var scene = Scene(0);
            byte[] zero = Render(scene.Root, scale);
            scene.Subject.Effect = null;
            Equal(zero, Render(scene.Root, scale), "Radius zero changed unfiltered pixels.");
            scene.Subject.Effect = new BlurEffect { Radius = 10 };
            byte[] blurred = Render(scene.Root, scale);
            Require(!zero.SequenceEqual(blurred), "Radius ten did not change the image.");
            int halo = Alpha(blurred, 23 * scale, 36 * scale, Width * scale);
            Require(halo > 0 && halo < 255, "Gaussian blur did not produce a translucent halo.");
            Require(Alpha(blurred, 24 * scale, 36 * scale, Width * scale) < 255, "Gaussian blur did not soften the original edge.");
            Require(Alpha(blurred, 8 * scale, 8 * scale, Width * scale) == 0, "Gaussian blur painted a distant empty region.");
        }
        Equal(Render(Scene(-1).Root), Render(Scene(0).Root), "Negative radius did not retain native zero-radius behavior.");
    }

    internal static void Retained(Action drain)
    {
        var scene = Scene(3);
        var previous = scene.Subject.Effect;
        using var target = new Live(scene.Root);
        drain();
        byte[] prior = target.Pixels();
        var blur = new BlurEffect { Radius = 0 };
        scene.Subject.Effect = blur;
        drain();
        byte[] zero = target.Pixels();
        blur.Radius = 10;
        drain();
        byte[] ten = target.Pixels();
        Require(!ten.SequenceEqual(zero), "Retained radius mutation left stale pixels.");
        scene.Fill.Color = Colors.Blue;
        drain();
        Require(!target.Pixels().SequenceEqual(ten), "Blurred child mutation left stale pixels.");
        scene.Fill.Color = Colors.Red;
        drain();
        Equal(target.Pixels(), ten, "Restoring blurred child paint did not restore pixels.");
        blur.Radius = 0;
        drain();
        Equal(target.Pixels(), zero, "Radius 10 -> 0 did not restore pixels.");
        scene.Subject.Effect = null;
        drain();
        Equal(target.Pixels(), zero, "Removing the effect changed radius-zero pixels.");
        scene.Subject.Effect = previous;
        drain();
        Require(ReferenceEquals(scene.Subject.Effect, previous), "Previous effect instance was replaced.");
        Equal(target.Pixels(), prior, "Restoring the previous effect did not restore pixels.");
    }

    internal static void Animated(Action drain)
    {
        var scene = Scene(0);
        var blur = (BlurEffect)scene.Subject.Effect;
        using var target = new Live(scene.Root);
        drain();
        byte[] zero = target.Pixels();
        byte[] ten = Render(Scene(10).Root);
        foreach (double end in new[] { 10d, 0d })
        {
            double start = end == 10 ? 0 : 10;
            blur.BeginAnimation(BlurEffect.RadiusProperty,
                new DoubleAnimation(start, end, TimeSpan.Zero) { BeginTime = TimeSpan.Zero });
            byte[] expected = end == 10 ? ten : zero;
            WaitUntil(() => blur.HasAnimatedProperties && blur.Radius == end && target.Pixels().SequenceEqual(expected),
                () => $"Effective radius={blur.Radius:R}, expected={end:R}, animated={blur.HasAnimatedProperties}.");
            Require((double)blur.GetAnimationBaseValue(BlurEffect.RadiusProperty) == 0,
                "The animation changed its base radius.");
            drain();
            Equal(target.Pixels(), expected, "The held animated value did not retain its pixels.");
        }
        blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        drain();
        Require(!blur.HasAnimatedProperties && blur.Radius == 0, "Animation removal did not restore the base radius.");
        Equal(target.Pixels(), zero, "Animation removal did not restore unblurred pixels.");
        scene.Subject.Effect = null;
        drain();
        Equal(target.Pixels(), zero, "Removing the animated owner effect left stale pixels.");
    }

    internal static void VisibilityAndIsolation(Action drain)
    {
        Equal(Render(Grouped(true)), Render(Grouped(false)), "The visual subtree was not blurred as one group.");
        var child = new Probe();
        var host = new Host(child) { Effect = new BlurEffect { Radius = 10 } };
        host.Measure(new Size(Width, Height));
        host.Arrange(new Rect(0, 0, Width, Height));
        var root = new ContainerVisual();
        root.Children.Add(host);
        root.Children.Add(Sibling());
        using var target = new Live(root);
        drain();
        byte[] visible = target.Pixels();
        var onlySibling = new ContainerVisual();
        onlySibling.Children.Add(Sibling());
        byte[] hidden = Render(onlySibling);
        foreach (UIElement element in new UIElement[] { child, host })
        {
            element.Visibility = Visibility.Hidden;
            drain();
            Equal(target.Pixels(), hidden, "Hidden blur content changed its sibling.");
            element.Visibility = Visibility.Visible;
            drain();
            Equal(target.Pixels(), visible, "Visibility restoration did not restore blurred pixels.");
            element.Opacity = 0;
            drain();
            Equal(target.Pixels(), hidden, "Zero-opacity blur content changed its sibling.");
            element.Opacity = 1;
            drain();
            Equal(target.Pixels(), visible, "Opacity restoration did not restore blurred pixels.");
        }
    }

    internal static void Clipping(Action drain)
    {
        var scene = Scene(10);
        using var target = new Live(scene.Root);
        drain();
        byte[] full = target.Pixels();
        scene.Subject.Clip = new RectangleGeometry(new Rect(32, 16, 32, 48));
        drain();
        byte[] ownClip = target.Pixels();
        Require(Alpha(ownClip, 31, 36) == 0 && Alpha(ownClip, 32, 36) > 0, "Visual clip did not bound the blur output.");
        Pixel(ownClip, 84, 12, Colors.Lime);
        scene.Subject.Clip = null;
        drain();
        Equal(target.Pixels(), full, "Removing the visual clip did not restore pixels.");
        scene.Root.Clip = new RectangleGeometry(new Rect(0, 30, Width, 26));
        drain();
        Equal(target.Pixels(), Crop(full, new Rect(0, 30, Width, 26)), "Ancestor clip changed interior blur pixels or leaked outside.");
        scene.Root.Clip = null;
        drain();
        Equal(target.Pixels(), full, "Removing the ancestor clip did not restore pixels.");
#if !STOCK
        var unsupported = new DrawingVisual();
        using (DrawingContext context = unsupported.RenderOpen()) { context.PushOpacityMask(Brushes.White); context.Pop(); }
        scene.Subject.Children.Add(unsupported);
        Reject(target.Render, "opacity mask");
        scene.Subject.Children.Remove(unsupported);
        drain();
        Equal(target.Pixels(), full, "Failure inside a blur layer leaked state into the recovery frame.");
#endif
    }

    internal static void TransformsAndBounds()
    {
        Equal(Render(Scene(3).Root), Render(Scene(3.9).Root), "Local radius was not truncated before scaling.");
        Equal(Render(Scene(100).Root), Render(Scene(200).Root), "Device radius cap changed with a larger local radius.");
        var scaled = Scene(10);
        scaled.Root.Transform = new ScaleTransform(2, 2);
        Equal(Render(scaled.Root, 1, Width * 2, Height * 2), Render(Scene(10).Root, 2), "Uniform visual scale differed from equivalent DPI scaling.");
        foreach (Matrix matrix in new[] { new Matrix(1, 0, 0, 1, 4, 5), new Matrix(1.5, 0, 0, 2, 0, 0), new Matrix(-1, 0, 0, 1, 72, 0) })
        {
            var transformed = Scene(3.9);
            transformed.Subject.Transform = new MatrixTransform(matrix);
            double effective = Math.Floor(3 * Math.Min(Math.Abs(matrix.M11), Math.Abs(matrix.M22)));
            var reference = Scene(effective);
            foreach (Visual child in reference.Subject.Children) ((ContainerVisual)child).Transform = new MatrixTransform(matrix);
            Equal(Render(transformed.Root, 1, Width * 2, Height * 2), Render(reference.Root, 1, Width * 2, Height * 2),
                "Axis-aligned blur did not preserve native minimum-scale radius behavior.");
        }
#if !STOCK
        foreach (Effect effect in new Effect[]
        {
            new DropShadowEffect(), new BlurEffect { KernelType = KernelType.Box },
            new BlurEffect { RenderingBias = RenderingBias.Quality }, new BlurEffect { Radius = double.NaN },
            new BlurEffect { Radius = double.PositiveInfinity }
        })
        {
            var unsupported = Scene(10);
            unsupported.Subject.Effect = effect;
            Reject(() => Render(unsupported.Root));
        }
        var skewed = Scene(10);
        skewed.Subject.Transform = new SkewTransform(10, 0);
        Reject(() => Render(skewed.Root));
#endif
    }

    internal static void SourceAndClone()
    {
        byte[] source = Enumerable.Range(0, 64).SelectMany(value => BitConverter.GetBytes((ushort)(value * 1040))).ToArray();
        var image = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Gray16, null, source, 16);
        image.Freeze();
        var scene = Scene(0);
        scene.Subject.Children.Clear();
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen()) context.DrawImage(image, new Rect(24, 24, 24, 24));
        scene.Subject.Children.Add(visual);
        var clone = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        clone.Render(scene.Root);
        byte[] beforeClone = Pixels(clone, Width, Height);
        ((BlurEffect)scene.Subject.Effect).Radius = 10;
        byte[] blurred = Render(scene.Root);
        Require(Alpha(blurred, 23, 36) > 0, "Bitmap content was not blurred.");
        var after = new byte[source.Length];
        image.CopyPixels(after, 16, 0);
        Equal(after, source, "Blur changed Gray16 source samples.");
        Require(image.Format == PixelFormats.Gray16 && image.PixelWidth == 8 && image.PixelHeight == 8 && image.IsFrozen,
            "Blur changed source metadata or ownership.");
        Equal(Pixels(clone, Width, Height), beforeClone, "Enabling blur changed the previously captured clone.");
    }

    internal sealed record Calibration(string Name, int Width, int Height, byte[] Pixels, int[] AlphaRow, int[] InkBounds);

    internal static IEnumerable<Calibration> CaptureCalibration()
    {
        foreach (double radius in new[] { 0d, 3d, 3.9, 10d, 100d, 200d })
            yield return Capture($"radius-{radius:R}", Render(Scene(radius).Root), Width, Height);
        yield return Capture("radius-10-dpi-192", Render(Scene(10).Root, 2), Width * 2, Height * 2);
        var clipped = Scene(10);
        clipped.Subject.Clip = new RectangleGeometry(new Rect(32, 16, 32, 48));
        yield return Capture("radius-10-own-clip", Render(clipped.Root), Width, Height);
        clipped.Subject.Clip = null;
        clipped.Root.Clip = new RectangleGeometry(new Rect(0, 30, Width, 26));
        yield return Capture("radius-10-ancestor-clip", Render(clipped.Root), Width, Height);
        var transformed = Scene(3.9);
        transformed.Subject.Transform = new ScaleTransform(1.5, 2);
        yield return Capture("radius-3.9-scale-1.5-2", Render(transformed.Root, 1, Width * 2, Height * 2), Width * 2, Height * 2);
        transformed.Subject.Transform = new MatrixTransform(-1, 0, 0, 1, 72, 0);
        yield return Capture("radius-3.9-mirror", Render(transformed.Root), Width, Height);
        yield return Capture("group-opacity-half", Render(Grouped(true)), Width, Height);
    }

    private static Calibration Capture(string name, byte[] pixels, int width, int height)
    {
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Alpha(pixels, x, y, width) != 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        return new(name, width, height, pixels, Enumerable.Range(0, width).Select(x => Alpha(pixels, x, 36 * width / Width, width)).ToArray(), new[] { left, top, right, bottom });
    }

    private static (ContainerVisual Root, ContainerVisual Subject, SolidColorBrush Fill) Scene(double radius)
    {
        var fill = new SolidColorBrush(Colors.Red);
        var subject = new ContainerVisual { Effect = new BlurEffect { Radius = radius } };
        subject.Children.Add(Rectangle(fill, new Rect(24, 24, 24, 24)));
        subject.Children.Add(Rectangle(Brushes.White, new Rect(56, 24, 1, 24)));
        var root = new ContainerVisual();
        root.Children.Add(subject);
        root.Children.Add(Sibling());
        return (root, subject, fill);
    }

    private static ContainerVisual Grouped(bool split)
    {
        var root = new ContainerVisual { Effect = new BlurEffect { Radius = 10 }, Opacity = 0.5 };
        root.Children.Add(Rectangle(Brushes.Red, new Rect(24, 24, split ? 18 : 36, 24)));
        if (split) root.Children.Add(Rectangle(Brushes.Red, new Rect(36, 24, 24, 24)));
        return root;
    }

    private static DrawingVisual Sibling() => Rectangle(Brushes.Lime, new Rect(80, 8, 8, 8));
    private static DrawingVisual Rectangle(Brush fill, Rect rectangle)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen()) context.DrawRectangle(fill, null, rectangle);
        return visual;
    }

    private static byte[] Render(Visual visual, int scale = 1, int width = Width, int height = Height)
    {
        var bitmap = new RenderTargetBitmap(width * scale, height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Pixels(bitmap, width * scale, height * scale);
    }
    private static byte[] Pixels(BitmapSource bitmap, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
    private static byte[] Crop(byte[] pixels, Rect clip)
    {
        byte[] result = pixels.ToArray();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (x < clip.Left || x >= clip.Right || y < clip.Top || y >= clip.Bottom) result.AsSpan((y * Width + x) * 4, 4).Clear();
        return result;
    }
    private static int Alpha(byte[] pixels, int x, int y, int width = Width) => pixels[(y * width + x) * 4 + 3];
    private static void Pixel(byte[] pixels, int x, int y, Color color) =>
        Require(pixels.AsSpan((y * Width + x) * 4, 4).SequenceEqual(new byte[] { color.B, color.G, color.R, color.A }), "Blur changed an unrelated sibling pixel.");
    private static void Equal(byte[] actual, byte[] expected, string message) => Require(actual.SequenceEqual(expected), message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
#if !STOCK
    private static void Reject(Action action, string? diagnostic = null)
    {
        try { action(); } catch (NotSupportedException exception) when (diagnostic == null || exception.Message.Contains(diagnostic)) { return; }
        throw new InvalidOperationException("An unsupported effect profile was silently admitted.");
    }
#endif
    private static void WaitUntil(Func<bool> condition, Func<string> detail)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        bool passed = false;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
        {
            passed = condition();
            if (passed || elapsed.Elapsed > TimeSpan.FromSeconds(3)) frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(passed, "Animated radius and automatically presented pixels did not reach the held endpoint. " + detail());
    }

    private sealed class Live : IDisposable
    {
#if STOCK
        private readonly Visual root;
        internal Live(Visual root) { this.root = root; }
        internal byte[] Pixels() => Render(root);
        public void Dispose() { }
#else
        private readonly Target target;
        internal Live(Visual root) { target = new Target(root, Width, Height); }
        internal byte[] Pixels() { using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray(); }
        internal void Render() => target.Render();
        public void Dispose() => target.Dispose();
#endif
    }
    private sealed class Probe : UIElement
    {
        protected override Size MeasureCore(Size availableSize) => new(Width, Height);
        protected override void OnRender(DrawingContext context) => context.DrawRectangle(Brushes.Red, null, new Rect(24, 24, 24, 24));
    }
    private sealed class Host : UIElement
    {
        private readonly UIElement child;
        internal Host(UIElement child) { this.child = child; AddVisualChild(child); }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? child : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureCore(Size availableSize) { child.Measure(availableSize); return new(Width, Height); }
        protected override void ArrangeCore(Rect finalRect) { base.ArrangeCore(finalRect); child.Arrange(new Rect(0, 0, Width, Height)); }
    }
}
