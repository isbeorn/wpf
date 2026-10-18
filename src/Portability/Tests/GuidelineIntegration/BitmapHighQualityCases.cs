#if !STOCK
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

internal static class BitmapHighQualityCases
{
    internal static void CheckRetainedState()
    {
        CheckDrawingGroupState();
        byte[] input = BitmapSamplingCorpus.CheckerBytes(64, 32, PixelFormats.Pbgra32, 1);
        var source = new WriteableBitmap(64, 32, 96, 96, PixelFormats.Pbgra32, null);
        source.WritePixels(new Int32Rect(0, 0, 64, 32), input, 256, 0);
        var parent = new BitmapSamplingCases.ImageElement(_ => { });
        var first = new BitmapSamplingCases.ImageElement(drawing => drawing.DrawImage(source, new Rect(0, 0, 24, 12)));
        var sibling = new BitmapSamplingCases.ImageElement(drawing => drawing.DrawImage(source, new Rect(0, 12, 24, 12)));
        parent.Children.Add(first);
        parent.Children.Add(sibling);
        RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.HighQuality);
        parent.Measure(new Size(32, 24));
        parent.Arrange(new Rect(0, 0, 32, 24));
        Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
            [parent, 32, 24, new DpiScale(1, 1)], null)!;
        int frames = 0;
        byte[]? pixels = null;
        Action rendered = () =>
        {
            using var snapshot = (SKBitmap)Invoke(type, target, "Snapshot")!;
            pixels = snapshot.GetPixelSpan().ToArray();
            frames++;
        };
        type.GetEvent("FrameRendered", BindingFlags.NonPublic | BindingFlags.Instance)!.GetAddMethod(true)!.Invoke(target, [rendered]);
        // The first direct render also gives the pre-implementation diagnostic a controlled RED path.
        Invoke(type, target, "Render");
        AssertContrast(pixels!, 0, "hq"); AssertContrast(pixels!, 12, "hq");
        byte[] hq = pixels!.ToArray();
        RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.Fant);
        Invoke(type, target, "Render");
        if (!pixels!.SequenceEqual(hq)) throw new InvalidOperationException("HighQuality/Fant aliases changed pixels.");

        void Change(Action mutation, string firstMode, string siblingMode)
        {
            int previous = frames;
            mutation();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            if (frames <= previous) throw new InvalidOperationException("HQ retained mutation did not queue a frame.");
            AssertContrast(pixels!, 0, firstMode); AssertContrast(pixels!, 12, siblingMode);
            if (parent.RenderCount != 1 || first.RenderCount != 1 || sibling.RenderCount != 1)
                throw new InvalidOperationException("HQ retained state/resource mutation rerecorded drawing content.");
        }
        Change(() => RenderOptions.SetBitmapScalingMode(first, BitmapScalingMode.NearestNeighbor), "nearest", "hq");
        Change(() => first.ClearValue(RenderOptions.BitmapScalingModeProperty), "hq", "hq");
        Change(() => RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.NearestNeighbor), "nearest", "nearest");
        Change(() => RenderOptions.SetBitmapScalingMode(first, BitmapScalingMode.HighQuality), "hq", "nearest");
        Change(() => parent.ClearValue(RenderOptions.BitmapScalingModeProperty), "hq", "linear");
        Change(() => first.ClearValue(RenderOptions.BitmapScalingModeProperty), "linear", "linear");
        Change(() => RenderOptions.SetBitmapScalingMode(parent, BitmapScalingMode.HighQuality), "hq", "hq");
        for (int offset = 0; offset < input.Length; offset += 4)
        { input[offset] = input[offset + 1] = input[offset + 2] = 64; input[offset + 3] = 255; }
        source.WritePixels(new Int32Rect(0, 0, 64, 32), input, 256, 0);
        Array.Clear(input);
        int beforeMutationFrame = frames;
        var mutationFrame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => mutationFrame.Continue = false));
        Dispatcher.PushFrame(mutationFrame);
        if (frames <= beforeMutationFrame || first.RenderCount != 1 || sibling.RenderCount != 1)
            throw new InvalidOperationException("HQ bitmap mutation lost its real retained frame or rerecorded content.");
        source.CopyPixels(input, 256, 0);
        for (int offset = 0; offset < input.Length; offset++)
            if (input[offset] != (offset % 4 == 3 ? 255 : 64)) throw new InvalidOperationException("HQ mutated source/caller bytes.");
        for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++) for (int c = 0; c < 4; c++)
            if (pixels![(y * 32 + x) * 4 + c] != (x >= 24 ? 0 : c == 3 ? 255 : 64))
                throw new InvalidOperationException("HQ retained bitmap mutation changed uniform color, alpha or extent.");
    }

    private static void CheckDrawingGroupState()
    {
        byte[] bytes = BitmapSamplingCorpus.CheckerBytes(64, 32, PixelFormats.Pbgra32, 1);
        BitmapSource source = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Pbgra32, null, bytes, 256);
        var outer = new DrawingGroup();
        var inner = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(outer, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(inner, BitmapScalingMode.NearestNeighbor);
        outer.Children.Add(new ImageDrawing(source, new Rect(0, 0, 24, 12)));
        inner.Children.Add(new ImageDrawing(source, new Rect(0, 12, 24, 12)));
        outer.Children.Add(inner);
        outer.Children.Add(new ImageDrawing(source, new Rect(0, 24, 24, 12)));
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawDrawing(outer);
            drawing.DrawImage(source, new Rect(0, 36, 24, 12));
        }
        Type type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
            [visual, 32, 48, new DpiScale(1, 1)], null)!;
        Invoke(type, target, "Render");
        using var snapshot = (SKBitmap)Invoke(type, target, "Snapshot")!;
        byte[] pixels = snapshot.GetPixelSpan().ToArray();
        AssertContrast(pixels, 0, "hq"); AssertContrast(pixels, 12, "nearest");
        AssertContrast(pixels, 24, "hq"); AssertContrast(pixels, 36, "linear");

        using var surface = SKSurface.Create(new SKImageInfo(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var precision = SKColorFilter.CreateColorMatrix(new float[] { 1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,1,0 });
        Type walkerType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.SkiaDrawingContextWalker", true)!;
        var walker = (DrawingContext)Activator.CreateInstance(walkerType, BindingFlags.NonPublic | BindingFlags.Instance, null,
            [surface.Canvas, precision], null)!;
        var failed = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(failed, BitmapScalingMode.HighQuality);
        failed.Children.Add(new ImageDrawing(source, new Rect(0, 0, 24, 12)));
        failed.Children.Add(new DrawingGroup { OpacityMask = Brushes.White, Children = { new ImageDrawing(source, new Rect(0, 0, 24, 12)) } });
        bool threw = false;
        try { walker.DrawDrawing(failed); }
        catch (NotSupportedException exception) when (exception.Message.Contains("opacity mask", StringComparison.Ordinal)) { threw = true; }
        if (!threw) throw new InvalidOperationException("Nested HQ failure did not reach the real unsupported command.");
        surface.Canvas.Clear(SKColors.Transparent);
        walker.DrawImage(source, new Rect(0, 0, 24, 12));
        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKBitmap output = SKBitmap.FromImage(image);
        AssertContrast(output.GetPixelSpan().ToArray(), 0, "linear");
    }

    private static void AssertContrast(byte[] pixels, int top, string mode)
    {
        double sum = 0, squared = 0; int count = 0;
        for (int y = top + 2; y < top + 9; y++) for (int x = 2; x < 21; x++)
        {
            int offset = (y * 32 + x) * 4;
            if (pixels[offset + 3] != 255) throw new InvalidOperationException("Retained HQ interior lost coverage.");
            double value = pixels[offset]; sum += value; squared += (value - 127.5) * (value - 127.5); count++;
        }
        double mean = sum / count, contrast = Math.Sqrt(squared / count);
        bool valid = mode switch { "hq" => contrast <= 5, "nearest" => contrast >= 120, _ => contrast is >= 30 and <= 45 };
        if (mean < 120 || mean > 135 || !valid) throw new InvalidOperationException($"Retained {mode}: mean {mean}, contrast {contrast}.");
    }

    private static object? Invoke(Type type, object target, string method)
    {
        try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }
}
#endif
