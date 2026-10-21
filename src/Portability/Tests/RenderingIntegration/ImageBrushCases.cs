using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ImageBrushCases
{
    private const int Width = 160, Height = 144;

    internal static void Run(Action<string, Action> run, Action drain)
    {
        run("image-brush-thumbnail-uniform-to-fill-and-frozen-source", Thumbnail);
        run("image-brush-stretch-dip-viewbox-and-transform", Mapping);
        run("image-brush-four-shapes-clip-opacity-and-siblings", ShapesAndState);
        run("image-brush-inherited-sampling-and-guidelines", SamplingAndGuidelines);
        run("image-brush-retained-source-mutation-and-high-bit-bytes", () => Retained(drain));
        run("image-brush-empty-unsupported-and-recovery", EmptyAndUnsupported);
    }

    internal static void Thumbnail()
    {
        BitmapSource source = Stripes();
        source.Freeze();
        var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        var visual = Scene(context =>
        {
            context.DrawRectangle(brush, null, new Rect(4, 4, 120, 120));
            context.DrawRectangle(Brushes.Lime, null, new Rect(132, 4, 8, 8));
        });
        byte[] pixels = Render(visual);
        Pixel(pixels, 34, 64, Colors.Lime);
        Pixel(pixels, 94, 64, Colors.Blue);
        Pixel(pixels, 2, 64, Colors.Transparent);
        Pixel(pixels, 136, 8, Colors.Lime);
        Require(ReferenceEquals(brush.ImageSource, source) && brush.Stretch == Stretch.UniformToFill && source.IsFrozen,
            "Thumbnail ImageBrush rendering changed its caller state.");
    }

    private static void Mapping()
    {
        var source = Stripes();
        foreach (Stretch stretch in new[] { Stretch.Fill, Stretch.Uniform, Stretch.None })
        {
            var brush = new ImageBrush(source) { Stretch = stretch };
            byte[] pixels = Render(Scene(dc => dc.DrawRectangle(brush, null, new Rect(4, 4, 32, 32))));
            if (stretch == Stretch.None)
            {
                Pixel(pixels, 18, 19, Colors.Red); Pixel(pixels, 21, 19, Colors.White);
                Pixel(pixels, 17, 19, Colors.Transparent);
            }
            else
            {
                Pixel(pixels, 8, 20, Colors.Red); Pixel(pixels, 32, 20, Colors.White);
                Pixel(pixels, 8, 6, stretch == Stretch.Fill ? Colors.Red : Colors.Transparent);
            }
        }
        foreach (AlignmentY alignment in new[] { AlignmentY.Top, AlignmentY.Bottom })
        {
            var brush = new ImageBrush(source) { Stretch = Stretch.Uniform, AlignmentY = alignment };
            byte[] pixels = Render(Scene(dc => dc.DrawRectangle(brush, null, new Rect(4, 4, 32, 32))));
            Pixel(pixels, 8, alignment == AlignmentY.Top ? 8 : 32, Colors.Red);
            Pixel(pixels, 8, alignment == AlignmentY.Top ? 32 : 8, Colors.Transparent);
        }
        foreach (int dpi in new[] { 96, 192 })
        {
            var brush = new ImageBrush(Stripes(dpi)) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 2, 1) };
            byte[] pixels = Render(Scene(dc => dc.DrawRectangle(brush, null, new Rect(4, 4, 32, 16))));
            Pixel(pixels, 32, 12, dpi == 96 ? Colors.Lime : Colors.White);
        }
        var mapped = new ImageBrush(source)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(1, 0, 2, 2),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(12, 8, 24, 16),
            Transform = new TranslateTransform(16, 4)
        };
        byte[] mappedPixels = Render(Scene(dc => dc.DrawRectangle(mapped, null, new Rect(0, 0, 80, 40))));
        Pixel(mappedPixels, 32, 20, Colors.Lime); Pixel(mappedPixels, 48, 20, Colors.Blue);
        Pixel(mappedPixels, 20, 20, Colors.Transparent);
        Require(mapped.Viewbox == new Rect(1, 0, 2, 2) && mapped.Transform.Value.OffsetX == 16,
            "ImageBrush mapping changed caller transforms or viewbox.");
    }

    private static void ShapesAndState()
    {
        var brush = new ImageBrush(Solid(Colors.Red));
        var geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) { Transform = new TranslateTransform(84, 4) };
        byte[] shapes = Render(Scene(dc =>
        {
            dc.DrawRectangle(brush, null, new Rect(4, 4, 16, 16));
            dc.DrawRoundedRectangle(brush, null, new Rect(28, 4, 16, 16), 5, 5);
            dc.DrawEllipse(brush, null, new Point(60, 12), 8, 8);
            dc.DrawGeometry(brush, null, geometry);
        }));
        foreach (int x in new[] { 12, 36, 60, 92 }) Pixel(shapes, x, 12, Colors.Red);
        Pixel(shapes, 28, 4, Colors.Transparent); Pixel(shapes, 52, 4, Colors.Transparent);
        brush.Opacity = 0.5;
        brush.RelativeTransform = new TranslateTransform(0.25, 0);
        brush.Transform = new TranslateTransform(4, 0);
        var visual = Scene(dc =>
        {
            dc.PushTransform(new TranslateTransform(8, 8));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 36, 32)));
            dc.PushOpacity(0.5);
            dc.DrawRectangle(brush, null, new Rect(0, 0, 32, 24));
            dc.Pop(); dc.Pop(); dc.Pop();
            dc.DrawRectangle(Brushes.Lime, null, new Rect(60, 8, 8, 8));
        });
        byte[] state = Render(visual);
        Pixel(state, 28, 16, Color.FromArgb(64, 255, 0, 0));
        Pixel(state, 12, 16, Colors.Transparent); Pixel(state, 48, 16, Colors.Transparent);
        Pixel(state, 64, 12, Colors.Lime);
        Require(geometry.Transform.Value.OffsetX == 84 && brush.Opacity == 0.5 && visual.Opacity == 1,
            "ImageBrush drawing changed caller state.");
        foreach (string profile in EdgeProfiles)
        foreach (int scale in new[] { 1, 2 })
        {
            int maximumDifference = 0;
#if !STOCK
            // These two 2x fixtures differ by one unit between Skia bitmap-shader and solid-color AA.
            if (scale == 2 && (profile == "rounded" || profile == "ellipse")) maximumDifference = 1;
#endif
            SameAlpha(EdgeScene(profile, scale, true), EdgeScene(profile, scale, false), $"{profile} at {scale}x", maximumDifference);
        }
    }

    private static void SamplingAndGuidelines()
    {
        foreach (int scale in new[] { 1, 2 })
        {
            byte[]? nearest = null;
            foreach (BitmapScalingMode mode in new[] { BitmapScalingMode.NearestNeighbor, BitmapScalingMode.Linear })
            {
                byte[] brush = SamplingScene(mode, scale, true, false, true);
                byte[] direct = SamplingScene(mode, scale, false, false);
                byte[] localMode = SamplingScene(mode, scale, true, true);
                SameAlpha(brush, direct, $"guided {mode} at {scale}x");
                bool differsFromNearest = false;
                for (int y = 6 * scale; y < 18 * scale; y++)
                for (int x = 6 * scale; x < 35 * scale; x++)
                {
                    int index = (y * Width * scale + x) * 4;
                    Require(brush.AsSpan(index, 4).SequenceEqual(direct.AsSpan(index, 4)),
                        $"ImageBrush inherited {mode} sampling differs from DrawImage at {scale}x, x={x}.");
                    Require(brush.AsSpan(index, 4).SequenceEqual(localMode.AsSpan(index, 4)),
                        "ImageBrush applied a brush-local scaling mode instead of the inherited visual mode.");
                    if (nearest != null && !brush.AsSpan(index, 4).SequenceEqual(nearest.AsSpan(index, 4))) differsFromNearest = true;
                }
                if (nearest != null) Require(differsFromNearest, "ImageBrush NearestNeighbor and Linear transition pixels were identical.");
                nearest = brush;
                Pixel(brush, 60 * scale, 12 * scale, Colors.Lime, scale);
            }
        }
    }

    private static readonly string[] EdgeProfiles = ["fractional-fill", "uniform-letterbox", "absolute-viewport", "rounded", "ellipse"];

    private static byte[] EdgeScene(string profile, int scale, bool useBrush)
    {
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 }, 8);
        var brush = new ImageBrush(source);
        Rect rectangle = new(4.25, 4.25, 32, 16);
        Rect content = rectangle;
        if (profile == "uniform-letterbox")
        {
            rectangle.Height = 32;
            content.Y += 8;
            brush.Stretch = Stretch.Uniform;
        }
        if (profile == "absolute-viewport")
        {
            rectangle = new Rect(4, 4, 40, 24);
            content = new Rect(8.25, 6.25, 24, 12);
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = content;
        }
        return Render(Scene(dc =>
        {
            if (profile == "rounded") dc.DrawRoundedRectangle(useBrush ? brush : Brushes.White, null, rectangle, 5.25, 5.25);
            else if (profile == "ellipse") dc.DrawEllipse(useBrush ? brush : Brushes.White, null,
                new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2), rectangle.Width / 2, rectangle.Height / 2);
            else if (useBrush) dc.DrawRectangle(brush, null, rectangle);
            else dc.DrawImage(source, content);
        }), scale);
    }

    private static void SameAlpha(byte[] actual, byte[] expected, string profile, int maximumDifference = 0)
    {
        for (int index = 3; index < actual.Length; index += 4)
            Require(Math.Abs(actual[index] - expected[index]) <= maximumDifference && (actual[index] == 0) == (expected[index] == 0),
                $"ImageBrush {profile} alpha at pixel {(index - 3) / 4} was {actual[index]}, expected {expected[index]}.");
    }

    private static byte[] SamplingScene(BitmapScalingMode mode, int scale, bool useBrush, bool localMode, bool roundTrip = false)
    {
        var source = Stripes();
        var brush = new ImageBrush(source);
        if (localMode) RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        var child = new SamplingElement(dc =>
        {
            dc.PushGuidelineSet(new GuidelineSet(new[] { 4.25, 36.25 }, new[] { 4.25, 20.25 }));
            if (useBrush) dc.DrawRectangle(brush, null, new Rect(4.25, 4.25, 32, 16));
            else dc.DrawImage(source, new Rect(4.25, 4.25, 32, 16));
            dc.Pop();
            dc.DrawRectangle(Brushes.Lime, null, new Rect(56, 8, 8, 8));
        });
        var parent = new SamplingElement(_ => { });
        RenderOptions.SetBitmapScalingMode(parent, mode);
        parent.Children.Add(child);
        parent.Measure(new Size(Width, Height));
        parent.Arrange(new Rect(0, 0, Width, Height));
        if (roundTrip)
        {
            byte[] initial = Render(parent, scale);
            RenderOptions.SetBitmapScalingMode(child, mode == BitmapScalingMode.NearestNeighbor ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
            byte[] overridden = Render(parent, scale);
            Require(!initial.SequenceEqual(overridden), "A local UIElement sampling override did not change transition pixels.");
            child.ClearValue(RenderOptions.BitmapScalingModeProperty);
            Require(Render(parent, scale).SequenceEqual(initial), "Clearing a child sampling mode did not restore its ancestor mode.");
            parent.ClearValue(RenderOptions.BitmapScalingModeProperty);
            byte[] cleared = Render(parent, scale);
            RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.Linear);
            Require(Render(parent, scale).SequenceEqual(cleared), "Clearing the ancestor sampling mode did not restore default Linear.");
            RenderOptions.SetBitmapScalingMode(parent, mode);
            Require(Render(parent, scale).SequenceEqual(initial), "Restoring the ancestor sampling mode changed its pixels.");
        }
        return Render(parent, scale);
    }

    private sealed class SamplingElement(Action<DrawingContext> draw) : UIElement
    {
        private VisualCollection? _children;
        internal VisualCollection Children => _children ??= new VisualCollection(this);
        protected override int VisualChildrenCount => Children.Count;
        protected override Visual GetVisualChild(int index) => Children[index];
        protected override Size MeasureCore(Size availableSize)
        {
            foreach (UIElement child in Children) child.Measure(availableSize);
            return new Size(Width, Height);
        }
        protected override void ArrangeCore(Rect finalRect)
        {
            base.ArrangeCore(finalRect);
            foreach (UIElement child in Children) child.Arrange(new Rect(new Point(), finalRect.Size));
        }
        protected override void OnRender(DrawingContext drawingContext) => draw(drawingContext);
    }

    private static void Retained(Action drain)
    {
        var writable = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null);
        Set(Colors.Red);
        var frozen = Solid(Colors.Blue); frozen.Freeze();
        var brush = new ImageBrush(writable);
        var visual = Scene(dc =>
        {
            dc.DrawRectangle(brush, null, new Rect(4, 4, 32, 24));
            dc.DrawRectangle(Brushes.Lime, null, new Rect(48, 4, 8, 8));
        });
#if !STOCK
        using var target = new Target(visual, Width, Height);
#endif
        drain();
        byte[] initial = Snapshot();
        Pixel(initial, 12, 12, Colors.Red);
        brush.ImageSource = frozen; drain(); Pixel(Snapshot(), 12, 12, Colors.Blue);
        brush.ImageSource = null; drain();
        byte[] removed = Snapshot(); Pixel(removed, 12, 12, Colors.Transparent); Pixel(removed, 52, 8, Colors.Lime);
        brush.ImageSource = writable; drain(); Require(Snapshot().SequenceEqual(initial), "ImageBrush source restoration left stale pixels.");
        Set(Colors.Lime); drain(); Pixel(Snapshot(), 12, 12, Colors.Lime);
        Set(Colors.Red); drain(); Require(Snapshot().SequenceEqual(initial), "Mutable ImageBrush pixels did not restore.");
        ushort[] gray = [0, 0x0102, 0x80FF, 21845, 65534, 65535];
        var highBit = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Gray16, null, gray, 6);
        brush.ImageSource = highBit; drain();
        Pixel(Snapshot(), 8, 8, Colors.Black); Pixel(Snapshot(), 30, 24, Colors.White);
        var after = new ushort[6]; highBit.CopyPixels(after, 6, 0);
        Require(after.SequenceEqual(gray) && highBit.Format == PixelFormats.Gray16 && frozen.IsFrozen,
            "ImageBrush display changed high-bit source bytes or frozen source state.");
        ushort[] rgb = [65535, 0x0102, 0x80FF, 0xCAFE, 65534, 0x0123];
        var rgbSource = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Rgb48, null, rgb, 12);
        brush.ImageSource = rgbSource; drain();
        byte[] rgbPixels = Snapshot();
        Require(rgbPixels[(12 * Width + 8) * 4 + 3] == 255 && rgbPixels[(12 * Width + 30) * 4 + 3] == 255,
            "RGB48 ImageBrush lost opaque source pixels.");
        var rgbAfter = new ushort[6]; rgbSource.CopyPixels(rgbAfter, 12, 0);
        Require(rgbAfter.SequenceEqual(rgb) && rgbSource.Format == PixelFormats.Rgb48,
            "ImageBrush display changed RGB48 source bytes.");
        brush.ImageSource = writable; drain(); Require(Snapshot().SequenceEqual(initial), "High-bit source removal did not restore the original image.");
        byte[] Snapshot()
        {
#if STOCK
            return Render(visual);
#else
            using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray();
#endif
        }
        void Set(Color color)
        {
            byte[] data = Enumerable.Repeat(new[] { color.B, color.G, color.R, color.A }, 4).SelectMany(pixel => pixel).ToArray();
            writable.WritePixels(new Int32Rect(0, 0, 2, 2), data, 8, 0);
        }
    }

    private static void EmptyAndUnsupported()
    {
        foreach (TileMode mode in Enum.GetValues<TileMode>())
        {
            var brush = new ImageBrush { TileMode = mode };
            byte[] empty = Render(Scene(dc =>
            {
                dc.DrawRectangle(brush, null, new Rect(4, 4, 24, 24));
                dc.DrawRectangle(Brushes.Lime, null, new Rect(40, 4, 8, 8));
            }));
            Pixel(empty, 12, 12, Colors.Transparent); Pixel(empty, 44, 8, Colors.Lime);
        }
#if !STOCK
        var valid = new ImageBrush(Stripes());
        foreach (TileMode mode in new[] { TileMode.Tile, TileMode.FlipX, TileMode.FlipY, TileMode.FlipXY })
        {
            valid.TileMode = mode;
            Reject(dc => dc.DrawRectangle(valid, null, new Rect(4, 4, 24, 24)), "ImageBrush tile mode");
        }
        valid.TileMode = TileMode.None;
        Pixel(Render(Scene(dc => dc.DrawRectangle(valid, null, new Rect(4, 4, 32, 16)))), 8, 12, Colors.Red);
        var drawing = new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        Reject(dc => dc.DrawRectangle(new ImageBrush(drawing), null, new Rect(4, 4, 24, 24)), "ImageBrush sources without portable bitmap storage");
        Reject(dc => dc.DrawRectangle(null, new Pen(valid, 2), new Rect(4, 4, 24, 24)), "brush type System.Windows.Media.ImageBrush");
        string fontPath = OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        var typeface = new GlyphTypeface(new Uri(fontPath));
        var glyph = new GlyphRun(typeface, 0, false, 12, 1, new ushort[] { typeface.CharacterToGlyphMap['A'] },
            new Point(4, 20), new double[] { 12 }, null, null, null, null, null, null);
        Reject(dc => dc.DrawGlyphRun(valid, glyph), "brush type System.Windows.Media.ImageBrush");
        Thumbnail();
        static void Reject(Action<DrawingContext> draw, string diagnostic)
        {
            try { Render(Scene(draw)); }
            catch (NotSupportedException exception) when (exception.Message.Contains(diagnostic)) { return; }
            throw new InvalidOperationException($"ImageBrush did not preserve unsupported boundary: {diagnostic}.");
        }
#endif
    }

    internal static object[] CaptureCalibration()
    {
        object[] sampling = (from mode in new[] { BitmapScalingMode.NearestNeighbor, BitmapScalingMode.Linear }
                from scale in new[] { 1, 2 }
                from useBrush in new[] { false, true }
                select (object)new { mode = mode.ToString(), scale, useBrush, width = Width * scale, height = Height * scale,
                    pixels = Convert.ToBase64String(SamplingScene(mode, scale, useBrush, false)) }).ToArray();
        return sampling.Concat(from profile in EdgeProfiles from scale in new[] { 1, 2 } from useBrush in new[] { false, true }
            select (object)new { profile, scale, useBrush, width = Width * scale, height = Height * scale,
                pixels = Convert.ToBase64String(EdgeScene(profile, scale, useBrush)) }).ToArray();
    }

    private static BitmapSource Solid(Color color) => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null,
        new byte[] { color.B, color.G, color.R, color.A }, 4);

    private static BitmapSource Stripes(double dpi = 96)
    {
        Color[] colors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.White];
        // Four-pixel bands leave flat interiors under both native and Skia sampling.
        var pixels = new byte[16 * 8 * 4];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 16; x++)
            {
                int index = (y * 16 + x) * 4;
                pixels[index] = colors[x / 4].B; pixels[index + 1] = colors[x / 4].G;
                pixels[index + 2] = colors[x / 4].R; pixels[index + 3] = 255;
            }
        return BitmapSource.Create(16, 8, dpi * 4, dpi * 4, PixelFormats.Pbgra32, null, pixels, 64);
    }

    private static DrawingVisual Scene(Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen()) draw(context);
        return visual;
    }

    private static byte[] Render(Visual visual, int scale = 1)
    {
#if STOCK
        var bitmap = new RenderTargetBitmap(Width * scale, Height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[Width * Height * scale * scale * 4];
        bitmap.CopyPixels(pixels, Width * scale * 4, 0);
        return pixels;
#else
        using var target = new Target(visual, Width * scale, Height * scale, new DpiScale(scale, scale));
        target.Render();
        using var pixels = target.Snapshot();
        return pixels.GetPixelSpan().ToArray();
#endif
    }

    private static void Pixel(byte[] pixels, int x, int y, Color color, int scale = 1)
    {
        int index = (y * Width * scale + x) * 4;
        byte[] expected = [(byte)((color.B * color.A + 127) / 255), (byte)((color.G * color.A + 127) / 255),
            (byte)((color.R * color.A + 127) / 255), color.A];
        Require(pixels.AsSpan(index, 4).SequenceEqual(expected),
            $"ImageBrush pixel ({x},{y}) was {Convert.ToHexString(pixels.AsSpan(index, 4))}, expected {color}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
