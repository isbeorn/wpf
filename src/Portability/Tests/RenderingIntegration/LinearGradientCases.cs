using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class LinearGradientCases
{
    private const int Width = 144, Height = 88;
    private static readonly Rect Bounds = new(4, 4, 100, 24);

    internal static void Run(Action<string, Action> run, Action drain)
    {
        run("linear-gradient-source-rounded-fill-and-opacity", SourceProfile);
        run("linear-gradient-four-fills-relative-absolute-and-guidelines", Mapping);
        run("linear-gradient-transform-projection-clip-and-state", Transforms);
        run("linear-gradient-sorted-duplicate-empty-and-single-stops", Stops);
        run("linear-gradient-retained-mutation-animation-remove-and-restore", () => Retained(drain));
        run("linear-gradient-unsupported-profiles-and-recovery", Unsupported);
    }

    private static LinearGradientBrush SourceBrush() => new(new GradientStopCollection
    {
        new(Color.FromArgb(0, 0, 0, 255), 0), new(Color.FromArgb(96, 0, 0, 255), 0.4),
        new(Color.FromArgb(96, 0, 0, 255), 0.6), new(Color.FromArgb(0, 0, 0, 255), 1)
    }, new Point(0, 0), new Point(1, 0));

    private static void SourceProfile()
    {
        var brush = SourceBrush(); brush.Freeze();
        byte[] pixels = Render(Scene(dc =>
        {
            dc.DrawRoundedRectangle(brush, null, Bounds, 3, 3);
            dc.DrawRectangle(Brushes.Lime, null, new Rect(116, 8, 8, 8));
        }));
        Pixel(pixels, 54, 16, 96, 0, 0, 96); Pixel(pixels, 120, 12, 0, 255, 0, 255);
        Pixel(pixels, 2, 16, 0, 0, 0, 0);
        var solid = new LinearGradientBrush(Colors.Red, Colors.Red, 0) { Opacity = 0.5 };
        byte[] opacity = Render(Scene(dc => { dc.PushOpacity(0.5); dc.DrawRoundedRectangle(solid, null, Bounds, 3, 3); dc.Pop(); }));
        Pixel(opacity, 54, 16, 0, 0, 64, 64);
        Require(brush.IsFrozen && brush.GradientStops.Count == 4 && brush.GradientStops[1].Offset == 0.4,
            "Rendering changed the frozen gradient or its stops.");
    }

    private static void Mapping()
    {
        Rect bounds = new(4.5, 4.5, 80, 20);
        var relative = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0, 0), new Point(1, 1));
        var absolute = relative.Clone(); absolute.MappingMode = BrushMappingMode.Absolute;
        absolute.StartPoint = bounds.TopLeft; absolute.EndPoint = bounds.BottomRight;
        foreach (int scale in new[] { 1, 2 })
        {
            Equal(Render(Scene(dc => dc.DrawRectangle(relative, null, bounds)), scale),
                Render(Scene(dc => dc.DrawRectangle(absolute, null, bounds)), scale), "Non-square relative projection differed from equivalent absolute endpoints.");
            byte[] pixels = Render(Scene(dc =>
            {
                dc.PushGuidelineSet(new GuidelineSet(new[] { 4.5, 84.5 }, new[] { 4.5, 24.5 }));
                dc.DrawRectangle(relative, null, bounds); dc.Pop();
                dc.DrawRoundedRectangle(relative, null, new Rect(4.5, 32.5, 32, 20), 4, 4);
                dc.DrawEllipse(relative, null, new Point(60.5, 42.5), 16, 10);
                dc.DrawGeometry(relative, null, new RectangleGeometry(new Rect(0, 0, 32, 20)) { Transform = new TranslateTransform(84.5, 32.5) });
            }), scale);
            foreach (int x in new[] { 20, 60, 100 }) Require(pixels[((42 * scale) * Width * scale + x * scale) * 4 + 3] == 255,
                "A gradient shape fill was missing.");
            byte[] guidedRelative = Render(Scene(dc => { dc.PushGuidelineSet(new GuidelineSet(new[] { 4.5, 84.5 }, new[] { 4.5, 24.5 })); dc.DrawRectangle(relative, null, bounds); dc.Pop(); }), scale);
            byte[] guidedAbsolute = Render(Scene(dc => { dc.PushGuidelineSet(new GuidelineSet(new[] { 4.5, 84.5 }, new[] { 4.5, 24.5 })); dc.DrawRectangle(absolute, null, bounds); dc.Pop(); }), scale);
            Equal(guidedRelative, guidedAbsolute, "Guidelines changed gradient mapping between relative and absolute space.");
            int center = ((14 * scale) * Width * scale + 44 * scale) * 4;
            Require(guidedRelative[center] > 80 && guidedRelative[center + 2] > 80 && guidedRelative[center + 3] == 255,
                "The guided gradient center lost the outer DPI transform or its red/blue mixture.");
        }
    }

    private static void Transforms()
    {
        var nullable = SourceBrush(); nullable.Transform = null!; nullable.RelativeTransform = null!;
        Equal(Render(Scene(dc => dc.DrawRoundedRectangle(nullable, null, Bounds, 3, 3))),
            Render(Scene(dc => dc.DrawRoundedRectangle(SourceBrush(), null, Bounds, 3, 3))), "Null gradient transforms differed from Identity.");
        Rect rectangle = new(4, 4, 120, 64);
        var matrix = new Matrix(1.5, 0.25, 0.5, 1, 8, 4);
        var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0, 0), new Point(80, 20))
            { MappingMode = BrushMappingMode.Absolute, Transform = new MatrixTransform(matrix) };
        foreach (Matrix transform in new[] { matrix, new Matrix(-1, 0, 0, 1, 120, 0) })
        {
            matrix = transform; brush.Transform = new MatrixTransform(matrix);
            byte[] transformed = Render(Scene(dc => dc.DrawRectangle(brush, null, rectangle)));
            var plain = brush.Clone(); plain.Transform = Transform.Identity;
            byte[] control = Render(Scene(dc =>
            {
                dc.PushClip(new RectangleGeometry(rectangle)); dc.PushTransform(new MatrixTransform(matrix));
                dc.DrawRectangle(plain, null, new Rect(-128, -128, 384, 384)); dc.Pop(); dc.Pop();
            }));
            Equal(transformed, control, "Brush transformation lost gradient band direction compared with a drawing transform.");
        }
        var relative = new Matrix(1, 0.125, 0.25, 1, 0.125, 0);
        brush.RelativeTransform = new MatrixTransform(relative);
        Matrix combined = new(1 / rectangle.Width, 0, 0, 1 / rectangle.Height, -rectangle.X / rectangle.Width, -rectangle.Y / rectangle.Height);
        combined.Append(relative); combined.Append(new Matrix(rectangle.Width, 0, 0, rectangle.Height, rectangle.X, rectangle.Y)); combined.Append(matrix);
        var equivalent = brush.Clone(); equivalent.RelativeTransform = Transform.Identity; equivalent.Transform = new MatrixTransform(combined);
        Equal(Render(Scene(dc => dc.DrawRectangle(brush, null, rectangle))), Render(Scene(dc => dc.DrawRectangle(equivalent, null, rectangle))),
            "Relative and absolute brush transform composition changed gradient projection.");
        var transparent = new LinearGradientBrush(Color.FromArgb(0, 255, 0, 0), Colors.Blue, 0);
        byte[] blended = Render(Scene(dc =>
        {
            dc.DrawRectangle(Brushes.Lime, null, rectangle);
            dc.PushClip(new RectangleGeometry(new Rect(4, 4, 80, 24)));
            dc.DrawRectangle(transparent, null, Bounds); dc.Pop();
            dc.DrawRectangle(Brushes.Red, null, new Rect(128, 8, 8, 8));
        }));
        int index = (16 * Width + 54) * 4;
        Require(blended[index + 2] > 40 && blended[index + 1] > 80 && blended[index] > 40 && blended[index + 3] == 255,
            "Transparent colored stops were interpolated in premultiplied space or lost the background.");
        Pixel(blended, 132, 12, 0, 0, 255, 255);
        Require(brush.Transform.Value == matrix && brush.RelativeTransform.Value == relative, "Rendering changed caller transforms.");
    }

    private static void Stops()
    {
        var unsorted = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.White, 1), new(Colors.Red, 0), new(Colors.Lime, 0.5), new(Colors.Blue, 0.5)
        }, new Point(0, 0), new Point(1, 0));
        var sorted = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Red, 0), new(Colors.Lime, 0.5), new(Colors.Blue, 0.5), new(Colors.White, 1)
        }, new Point(0, 0), new Point(1, 0));
        Equal(Render(Scene(dc => dc.DrawRectangle(unsorted, null, Bounds))), Render(Scene(dc => dc.DrawRectangle(sorted, null, Bounds))),
            "Sorting gradient stops changed duplicate-stop order.");
        byte[] edge = Render(Scene(dc => dc.DrawRectangle(sorted, null, Bounds)));
        Require(edge[(16 * Width + 53) * 4 + 1] > 240 && edge[(16 * Width + 54) * 4] > 240,
            "Coincident stops did not retain their left and right colors.");
        foreach (int count in new[] { 0, 1 })
        {
            var brush = new LinearGradientBrush();
            if (count == 1) brush.GradientStops.Add(new GradientStop(Colors.Red, 0.75));
            byte[] pixels = Render(Scene(dc => dc.DrawRoundedRectangle(brush, null, Bounds, 3, 3)));
            Pixel(pixels, 54, 16, 0, 0, count == 0 ? (byte)0 : (byte)255, count == 0 ? (byte)0 : (byte)255);
        }
        var coincident = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0.5, 0.5), new Point(0.5, 0.5));
        byte[] last = Render(Scene(dc => dc.DrawRoundedRectangle(coincident, null, Bounds, 3, 3)));
        foreach (int x in new[] { 10, 54, 98 }) Pixel(last, x, 16, 255, 0, 0, 255);
        Require(unsorted.GradientStops[0].Offset == 1 && unsorted.GradientStops[2].Color == Colors.Lime,
            "Gradient normalization changed the caller's stop collection.");
    }

    private static void Retained(Action drain)
    {
        var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0.25, 0), new Point(0.75, 0));
        var stops = brush.GradientStops;
        var clone = brush.CloneCurrentValue(); clone.Freeze();
        var visual = Scene(dc => { dc.DrawRoundedRectangle(brush, null, Bounds, 3, 3); dc.DrawRectangle(Brushes.Lime, null, new Rect(116, 8, 8, 8)); });
#if !STOCK
        using var target = new Target(visual, Width, Height);
#endif
        drain(); byte[] initial = Snapshot(); Pixel(initial, 10, 16, 0, 0, 255, 255);
        stops[0].Color = Colors.Lime; drain(); Pixel(Snapshot(), 10, 16, 0, 255, 0, 255);
        byte[] changedColor = Snapshot();
        var added = new GradientStop(Colors.Yellow, 0.5); stops.Add(added); drain();
        Require(!Snapshot().SequenceEqual(changedColor), "Adding a retained gradient stop did not change pixels.");
        stops.Remove(added); stops[0].Color = Colors.Red; drain(); Equal(Snapshot(), initial, "Stop removal did not restore pixels.");
        stops[0].Offset = 0.4; drain(); Require(!Snapshot().SequenceEqual(initial), "Changing a stop offset did not invalidate the gradient.");
        stops[0].Offset = 0; drain(); Equal(Snapshot(), initial, "Restoring a stop offset did not restore pixels.");
        brush.GradientStops = new GradientStopCollection(); drain(); Pixel(Snapshot(), 54, 16, 0, 0, 0, 0);
        brush.GradientStops = stops; drain(); Equal(Snapshot(), initial, "Restoring the gradient collection left stale pixels.");
        brush.GradientStops = null!; drain(); Pixel(Snapshot(), 54, 16, 0, 0, 0, 0);
        brush.GradientStops = stops; drain(); Equal(Snapshot(), initial, "Restoring a null gradient collection left stale pixels.");
        brush.Opacity = 0; drain(); Pixel(Snapshot(), 54, 16, 0, 0, 0, 0);
        brush.Opacity = 1; drain(); Equal(Snapshot(), initial, "Restoring brush opacity left stale pixels.");
#if STOCK
        EventHandler tick = (_, _) => { };
        CompositionTarget.Rendering += tick;
        try
        {
#endif
            brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0, TimeSpan.Zero));
            WaitUntil(() => brush.HasAnimatedProperties && brush.Opacity == 0 && Snapshot()[(16 * Width + 54) * 4 + 3] == 0);
            brush.BeginAnimation(Brush.OpacityProperty, null); drain();
            Equal(Snapshot(), initial, "Removing gradient animation did not restore the base opacity.");
#if STOCK
        }
        finally { CompositionTarget.Rendering -= tick; }
#endif
        Require(clone.IsFrozen && clone.GradientStops[0].Color == Colors.Red && clone.Opacity == 1,
            "Retained mutation changed the frozen pre-render clone.");
        byte[] Snapshot()
        {
#if STOCK
            return Render(visual);
#else
            using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray();
#endif
        }
    }

    private static void Unsupported()
    {
#if !STOCK
        foreach (GradientSpreadMethod spread in new[] { GradientSpreadMethod.Repeat, GradientSpreadMethod.Reflect })
        {
            var brush = SourceBrush(); brush.SpreadMethod = spread;
            Reject(dc => dc.DrawRectangle(brush, null, Bounds), "LinearGradientBrush spread");
        }
        var interpolation = SourceBrush(); interpolation.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
        Reject(dc => dc.DrawRectangle(interpolation, null, Bounds), "LinearGradientBrush color interpolation");
        foreach (double offset in new[] { -0.25, 1.25, double.NaN, double.PositiveInfinity })
        {
            var brush = SourceBrush(); brush.GradientStops[1].Offset = offset;
            Reject(dc => dc.DrawRectangle(brush, null, Bounds), "LinearGradientBrush stop offsets");
        }
        var singular = SourceBrush(); singular.Transform = new ScaleTransform(0, 1);
        Reject(dc => dc.DrawRectangle(singular, null, Bounds), "LinearGradientBrush transforms");
        var nonfinite = SourceBrush(); nonfinite.EndPoint = new Point(double.NaN, 0);
        Reject(dc => dc.DrawRectangle(nonfinite, null, Bounds), "LinearGradientBrush points");
        Reject(dc => dc.DrawRectangle(null, new Pen(SourceBrush(), 2), Bounds), "brush type System.Windows.Media.LinearGradientBrush");
        Reject(dc => dc.DrawRectangle(new RadialGradientBrush(Colors.Red, Colors.Blue), null, Bounds), "brush type System.Windows.Media.RadialGradientBrush");
        string fontPath = OperatingSystem.IsWindows() ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf") : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        var typeface = new GlyphTypeface(new Uri(fontPath));
        var glyph = new GlyphRun(typeface, 0, false, 12, 1, new ushort[] { typeface.CharacterToGlyphMap['A'] }, new Point(4, 20), new double[] { 12 }, null, null, null, null, null, null);
        Reject(dc => dc.DrawGlyphRun(SourceBrush(), glyph), "brush type System.Windows.Media.LinearGradientBrush");
        var retained = SourceBrush();
        var visual = Scene(dc => { dc.DrawRoundedRectangle(retained, null, Bounds, 3, 3); dc.DrawRectangle(Brushes.Lime, null, new Rect(116, 8, 8, 8)); });
        using var target = new Target(visual, Width, Height);
        target.Render();
        using var initial = target.Snapshot();
        retained.SpreadMethod = GradientSpreadMethod.Repeat;
        try { target.Render(); throw new InvalidOperationException("Unsupported retained spread completed a frame."); }
        catch (NotSupportedException exception) when (exception.Message.Contains("LinearGradientBrush spread")) { }
        retained.SpreadMethod = GradientSpreadMethod.Pad;
        target.Render();
        using var recovered = target.Snapshot();
        Equal(recovered.GetPixelSpan().ToArray(), initial.GetPixelSpan().ToArray(), "Restoring supported spread did not restore the same target and sibling pixels.");
#endif
        SourceProfile();
    }

    internal static object[] CaptureCalibration()
    {
        var coincident = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0.5, 0.5), new Point(0.5, 0.5));
        var scenes = new (string Profile, DrawingVisual Visual)[]
        {
            ("source-profile", Scene(dc => dc.DrawRoundedRectangle(SourceBrush(), null, Bounds, 3, 3))),
            ("non-square-diagonal", Scene(dc => dc.DrawRectangle(new LinearGradientBrush(Colors.Red, Colors.Blue, 45), null, new Rect(4.5, 4.5, 80, 20)))),
            ("transparent-colored", Scene(dc => { dc.DrawRectangle(Brushes.Lime, null, Bounds); dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 255, 0, 0), Colors.Blue, 0), null, Bounds); })),
            ("coincident-endpoints", Scene(dc => dc.DrawRoundedRectangle(coincident, null, Bounds, 3, 3)))
        };
        return (from scene in scenes from scale in new[] { 1, 2 } select (object)new
            { profile = scene.Profile, scale, width = Width * scale, height = Height * scale, pixels = Convert.ToBase64String(Render(scene.Visual, scale)) }).ToArray();
    }

    private static DrawingVisual Scene(Action<DrawingContext> draw) { var visual = new DrawingVisual(); using (DrawingContext dc = visual.RenderOpen()) draw(dc); return visual; }
    private static byte[] Render(Visual visual, int scale = 1)
    {
#if STOCK
        var bitmap = new RenderTargetBitmap(Width * scale, Height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(visual);
        var pixels = new byte[Width * Height * scale * scale * 4]; bitmap.CopyPixels(pixels, Width * scale * 4, 0); return pixels;
#else
        using var target = new Target(visual, Width * scale, Height * scale, new DpiScale(scale, scale)); target.Render();
        using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray();
#endif
    }
    private static void WaitUntil(Func<bool> condition)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew(); var frame = new DispatcherFrame(); bool passed = false;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
        { passed = condition(); if (passed || elapsed.Elapsed > TimeSpan.FromSeconds(3)) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(passed, "Current animated gradient opacity did not reach its held value and retained pixels.");
    }
    private static void Pixel(byte[] pixels, int x, int y, byte b, byte g, byte r, byte a) => Require(pixels.AsSpan((y * Width + x) * 4, 4).SequenceEqual(new byte[] { b, g, r, a }),
        $"Gradient pixel ({x},{y}) was {Convert.ToHexString(pixels.AsSpan((y * Width + x) * 4, 4))}, expected {Convert.ToHexString(new byte[] { b, g, r, a })}.");
    private static void Equal(byte[] actual, byte[] expected, string message) => Require(actual.SequenceEqual(expected), message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
#if !STOCK
    private static void Reject(Action<DrawingContext> draw, string diagnostic)
    {
        try { Render(Scene(draw)); } catch (NotSupportedException exception) when (exception.Message.Contains(diagnostic)) { return; }
        throw new InvalidOperationException($"The unsupported gradient profile did not retain its diagnostic: {diagnostic}.");
    }
#endif
}
