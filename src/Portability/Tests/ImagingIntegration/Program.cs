using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

var checks = new List<object>();
bool failed = false;
void Check(string name, Action action)
{
    try { action(); checks.Add(new { name, status = "pass", detail = "Actual framework APIs and observable pixels passed." }); }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}
Check("actual-scientific-source-precision-and-ownership", () =>
{
    foreach (PixelFormat format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48 })
    {
        int channels = format == PixelFormats.Gray16 ? 1 : 3;
        ushort[] input = Enumerable.Range(0, 6 * channels).Select(index => (ushort)(index * 3271 + 1)).ToArray();
        ushort[] original = (ushort[])input.Clone();
        BitmapSource source = BitmapSource.Create(3, 2, 120, 144, format, null, input, 3 * channels * 2);
        Array.Clear(input);
        var copied = new ushort[input.Length]; source.CopyPixels(copied, 3 * channels * 2, 0);
        Require(source is CachedBitmap && source.Format == format && copied.SequenceEqual(original), "Create must copy and retain every scientific sample.");
        Require(Math.Abs(source.Width - 3 * 96d / 120) < 1e-6 && Math.Abs(source.Height - 2 * 96d / 144) < 1e-6, "Pixel dimensions and DIP dimensions differ.");
        source.Freeze();
        Task.Run(() => { var cross = new ushort[input.Length]; source.CopyPixels(cross, 3 * channels * 2, 0); Require(cross.SequenceEqual(original), "Frozen cross-thread read changed samples."); }).GetAwaiter().GetResult();
    }
});
PortablePngCases.Run(Check);
PortableDecoderCases.Run(Check);
PortablePngEncodeCases.Run(Check);
PortableCacheCases.Run(Check);
RenderTargetBitmapCases.Run(Check);
Check("render-target-invalidates-real-retained-consumer", () =>
{
    var image = new RenderTargetBitmap(2, 1, 96, 96, PixelFormats.Pbgra32);
    var retained = new DrawingVisual(); using (var drawing = retained.RenderOpen()) drawing.DrawImage(image, new Rect(0, 0, 2, 1));
    using var target = new Target(retained, 2, 1); Drain();
    using (var initial = target.Snapshot()) Require(initial.GetPixel(0, 0).Alpha == 0, "Initial RTB consumer is not transparent.");
    var red = new DrawingVisual(); using (var drawing = red.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 1));
    image.Render(red); Drain(); using (var rendered = target.Snapshot()) Require(rendered.GetPixel(0, 0) == SKColors.Red, "Render did not invalidate the retained image consumer.");
    image.Clear(); Drain(); using (var cleared = target.Snapshot()) Require(cleared.GetPixel(0, 0).Alpha == 0, "Clear did not invalidate the retained image consumer.");
    image.Render(red); Drain(); using (var restored = target.Snapshot()) Require(restored.GetPixel(0, 0) == SKColors.Red, "Render after Clear did not restore the retained consumer.");
});
Check("pointer-source-copies-before-caller-release", () =>
{
    IntPtr memory = Marshal.AllocHGlobal(8);
    BitmapSource source;
    try
    {
        Marshal.Copy(new short[] { 0, 1, 32767, -1 }, 0, memory, 4);
        source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray16, null, memory, 8, 4);
    }
    finally { Marshal.FreeHGlobal(memory); }
    GC.Collect(); GC.WaitForPendingFinalizers();
    var result = new ushort[4]; source.CopyPixels(result, 4, 0);
    Require(result.SequenceEqual(new ushort[] { 0, 1, 32767, 65535 }), "The source retained caller pointer storage.");
});
Check("roi-padded-stride-and-element-offset", () =>
{
    ushort[] input = [1, 2, 3, 999, 4, 5, 6];
    var image = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Gray16, null, input, 8);
    ushort[] result = Enumerable.Repeat((ushort)777, 8).ToArray();
    image.CopyPixels(new Int32Rect(1, 0, 2, 2), result, 6, 1);
    Require(result.SequenceEqual(new ushort[] { 777, 2, 3, 777, 5, 6, 777, 777 }), "ROI, padding or offset was copied incorrectly.");
    Expect<ArgumentOutOfRangeException>(() => image.CopyPixels(new Int32Rect(2, 0, 2, 1), new ushort[4], 8, 0));
    Expect<ArgumentException>(() => image.CopyPixels(new ushort[1], 6, 0));
    Expect<OverflowException>(() => BitmapSource.Create(int.MaxValue, 2, 96, 96, PixelFormats.Rgb48, null, new ushort[3], 6));
});
Check("writepixels-source-roi-destination-and-caller-reuse", () =>
{
    var image = new WriteableBitmap(3, 2, 96, 96, PixelFormats.Gray16, null);
    ushort[] input = [11, 12, 13, 99, 21, 22, 23];
    image.WritePixels(new Int32Rect(1, 0, 2, 2), input, 8, 1, 0);
    Array.Clear(input);
    ushort[] result = new ushort[6]; image.CopyPixels(result, 6, 0);
    Require(result.SequenceEqual(new ushort[] { 0, 12, 13, 0, 22, 23 }), "WritePixels source ROI and destination were confused.");
    image.WritePixels(new Int32Rect(0, 1, 2, 1), new ushort[] { 999, 31, 32 }, 4, 1);
    image.CopyPixels(result, 6, 0);
    Require(result.SequenceEqual(new ushort[] { 0, 12, 13, 31, 32, 23 }), "Legacy WritePixels offset must count elements and rectangle is destination.");
});
Check("pointer-writepixels-and-source-constructor", () =>
{
    var source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Gray16, null, new ushort[] { 10, 20, 30, 40, 50, 60 }, 6);
    source.Freeze();
    var image = new WriteableBitmap(source);
    IntPtr pixels = Marshal.AllocHGlobal(12);
    try
    {
        Marshal.Copy(new short[] { 1, 2, 99, 3, 4, 99 }, 0, pixels, 6);
        image.WritePixels(new Int32Rect(0, 1, 2, 1), pixels, 12, 6, 1, 0);
    }
    finally { Marshal.FreeHGlobal(pixels); }
    var result = new ushort[6]; image.CopyPixels(result, 6, 0);
    Require(result.SequenceEqual(new ushort[] { 10, 3, 4, 40, 50, 60 }), "Pointer source Y/stride or WriteableBitmap source copy was incorrect.");
    source.CopyPixels(result, 6, 0);
    Require(result.SequenceEqual(new ushort[] { 10, 20, 30, 40, 50, 60 }), "The mutable source constructor shared caller source samples.");
});
Check("nested-lock-dirty-publication-and-retained-redraw", () =>
{
    var image = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
    image.WritePixels(new Int32Rect(0, 0, 2, 1), new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, 8, 0);
    var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawImage(image, new Rect(0, 0, 2, 1));
    using var target = new Target(visual, 2, 1); Drain();
    using (var initial = target.Snapshot()) Require(initial.GetPixel(0, 0) == SKColors.Red, "Initial retained image did not render.");
    int changes = 0; image.Changed += (_, _) => changes++;
    Expect<InvalidOperationException>(() => image.AddDirtyRect(new Int32Rect(0, 0, 1, 1)));
    Expect<InvalidOperationException>(image.Unlock);
    Expect<ArgumentOutOfRangeException>(() => image.TryLock(Duration.Automatic));
    Require(image.TryLock(new Duration(TimeSpan.Zero)), "Zero timeout lock failed without a concurrent renderer.");
    IntPtr pointer = image.BackBuffer; image.Lock();
    Require(pointer != IntPtr.Zero && pointer == image.BackBuffer && image.BackBufferStride >= 8 && !image.CanFreeze, "Lock nesting must retain a stable pointer and prevent Freeze.");
    Expect<InvalidOperationException>(image.Freeze);
    Expect<ArgumentOutOfRangeException>(() => image.AddDirtyRect(new Int32Rect(-1, 0, 1, 1)));
    Expect<ArgumentException>(() => image.AddDirtyRect(new Int32Rect(1, 0, 2, 1)));
    Task.Run(() => Marshal.WriteInt32(pointer, unchecked((int)0xff0000ff))).GetAwaiter().GetResult();
    image.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
    image.Unlock(); target.Render();
    using (var pending = target.Snapshot()) Require(pending.GetPixel(0, 0) == SKColors.Red && changes == 0, "A nested unlock published unfinished pixels.");
    image.Unlock(); Drain();
    using (var changed = target.Snapshot()) Require(changed.GetPixel(0, 0) == SKColors.Blue && changed.GetPixel(1, 0) == SKColors.Red && changes == 1, "Outermost dirty unlock did not invalidate and redraw the actual retained scene.");
    GC.Collect(); image.Lock(); Require(image.BackBuffer == pointer, "The back-buffer pointer changed after collection and relocking.");
    image.AddDirtyRect(Int32Rect.Empty); image.Unlock(); Require(changes == 1, "A clean lock cycle emitted a change.");
});
Check("clone-freeze-and-thread-ownership", () =>
{
    var original = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Gray16, null);
    original.WritePixels(new Int32Rect(0, 0, 1, 1), new ushort[] { 65535 }, 2, 0);
    Require(Task.Run(() => { try { _ = original.PixelWidth; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "A mutable image lost dispatcher affinity.");
    var clone = original.Clone(); original.Freeze();
    clone.WritePixels(new Int32Rect(0, 0, 1, 1), new ushort[] { 1 }, 2, 0);
    var result = new ushort[1]; original.CopyPixels(result, 2, 0); Require(result[0] == 65535, "Clone mutation changed frozen source.");
    var copied = new CachedBitmap(original, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    copied.CopyPixels(result, 2, 0); Require(result[0] == 65535, "Actual CachedBitmap OnLoad changed source samples.");
    var thawed = original.CloneCurrentValue(); thawed.WritePixels(new Int32Rect(0, 0, 1, 1), new ushort[] { 32768 }, 2, 0);
    original.CopyPixels(result, 2, 0); Require(result[0] == 65535 && !thawed.IsFrozen, "Cloning frozen data did not create independent mutable ownership.");
    Expect<InvalidOperationException>(original.Lock);
    Task.Run(() => original.CopyPixels(new ushort[1], 2, 0)).GetAwaiter().GetResult();
});
Check("scientific-display-quantization-boundaries", () =>
{
    ushort[] samples = [0, 1, 127, 128, 129, 254, 255, 256, 257, 258, 32767, 32768, 32896, 65407, 65408, 65534, 65535];
    byte[] stockGray = [0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 128, 128, 128, 254, 254, 255, 255];
    var stockGrayBySample = samples.Zip(stockGray).ToDictionary(pair => pair.First, pair => pair.Second);
    samples = samples.Concat(samples.Select(value => (ushort)(ushort.MaxValue - value))).Distinct().Order().ToArray();
    foreach (PixelFormat format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48 })
    {
        ushort[] data = format == PixelFormats.Gray16 ? samples : samples.SelectMany(value => new[] { value, value, value }).ToArray();
        var source = BitmapSource.Create(samples.Length, 1, 96, 96, format, null, data, data.Length * 2);
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawImage(source, new Rect(0, 0, samples.Length, 1));
        using var target = new Target(visual, samples.Length, 1); Drain(); using var pixels = target.Snapshot();
        for (int index = 0; index < samples.Length; index++)
        {
            int expected = format == PixelFormats.Gray16 ? (samples[index] + 128) / 257 : samples[index] >> 8;
            SKColor pixel = pixels.GetPixel(index, 0);
            Require(pixel.Red == expected && pixel.Green == expected && pixel.Blue == expected && pixel.Alpha == 255, "Display conversion changed channel order or quantization.");
            if (format == PixelFormats.Gray16)
            {
                if (stockGrayBySample.TryGetValue(samples[index], out byte stock))
                    Require(Math.Abs(pixel.Red - stock) <= 1, "Gray16 exceeded the measured stock display tolerance.");
                int opposite = Array.IndexOf(samples, (ushort)(ushort.MaxValue - samples[index]));
                Require(pixel.Red + pixels.GetPixel(opposite, 0).Red == 255, "Gray16 quantization is asymmetric around the midpoint.");
            }
        }
        ushort[] restored = new ushort[data.Length]; source.CopyPixels(restored, data.Length * 2, 0);
        Require(restored.SequenceEqual(data), "Display conversion destroyed scientific source precision.");
    }
});
Check("alpha-and-retained-source-lifetime", () =>
{
    foreach (PixelFormat format in new[] { PixelFormats.Bgra32, PixelFormats.Pbgra32 })
    {
        var visual = new DrawingVisual();
        byte[] bytes = format == PixelFormats.Bgra32 ? [10, 20, 100, 128] : [5, 10, 50, 128];
        var source = BitmapSource.Create(1, 1, 96, 96, format, null, bytes, 4);
        using (var context = visual.RenderOpen()) context.DrawImage(source, new Rect(0, 0, 1, 1));
        source = null; Array.Clear(bytes); GC.Collect(); GC.WaitForPendingFinalizers();
        using var target = new Target(visual, 1, 1); Drain(); using var pixels = target.Snapshot();
        SKColor pixel = pixels.GetPixel(0, 0);
        Require(Math.Abs(pixel.Red - 100) <= 1 && Math.Abs(pixel.Green - 20) <= 1 && Math.Abs(pixel.Blue - 10) <= 1 && pixel.Alpha == 128, "Straight/premultiplied image alpha or retained lifetime is wrong.");
    }
});
Check("rgb-and-byte-format-display-channels", () =>
{
    var color = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Rgb48, null, new ushort[] { 65535, 256, 0 }, 6);
    var sources = new[]
    {
        (color, new SKColor(255, 1, 0)),
        (BitmapSource.Create(1, 1, 96, 96, PixelFormats.Rgb24, null, new byte[] { 1, 2, 3 }, 3), new SKColor(1, 2, 3)),
        (BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 3, 2, 1 }, 3), new SKColor(1, 2, 3)),
        (BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[] { 3, 2, 1, 199 }, 4), new SKColor(1, 2, 3)),
        (BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 17 }, 1), new SKColor(17, 17, 17))
    };
    foreach (var (source, expected) in sources)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawImage(source, new Rect(0, 0, 1, 1));
        using var target = new Target(visual, 1, 1); Drain(); using var pixels = target.Snapshot();
        Require(pixels.GetPixel(0, 0) == expected, "The display backend swapped channels or used padding as alpha.");
    }
});
Check("named-unsupported-codecs-and-formats", () =>
{
    ExpectPortableUnsupported(() => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Rgba128Float, null, new float[4], 16));
    Expect<FileFormatException>(() => BitmapFrame.Create(new MemoryStream([1, 2, 3])));
    Expect<FileFormatException>(() => new PngBitmapDecoder(new MemoryStream([1, 2, 3]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
    ExpectPortableUnsupported(() => { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream([1, 2, 3]); bitmap.EndInit(); });
    Expect<NotSupportedException>(() => new PngBitmapEncoder().Save(new MemoryStream()));
});
string[] nativeAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name is "DirectWriteForwarder" or "System.Windows.Input.Manipulations").Select(assembly => assembly.FullName!).ToArray();
string[] nativeMappings = OperatingSystem.IsLinux() ? File.ReadAllLines("/proc/self/maps").Where(IsWindowsNative).ToArray() :
    System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Where(module => IsWindowsNative(module.ModuleName)).Select(module => module.FileName).ToArray();
Check("no-windows-native-runtime-dependencies", () => Require(nativeAssemblies.Length == 0 && nativeMappings.Length == 0, "Windows native modules were loaded."));
Console.WriteLine(JsonSerializer.Serialize(new { probe = "actual-wpf-imaging-integration", os = Environment.OSVersion.Platform.ToString(), framework = typeof(BitmapSource).Assembly.Location, scope = "Owned scientific samples, mutable bitmaps, eager PNG/ICO factory frames, RenderTargetBitmap and retained DrawImage; other codec/conversion graphs unsupported.", nativeAssemblies, nativeMappings, checks }, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
void ExpectPortableUnsupported(Action action) { try { action(); } catch (NotSupportedException exception) when (exception.Message.Contains("Portable", StringComparison.OrdinalIgnoreCase)) { return; } throw new InvalidOperationException("Expected a named portable diagnostic."); }
bool IsWindowsNative(string name) => name.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase) || name.Contains("PresentationNative", StringComparison.OrdinalIgnoreCase) || name.Contains("DirectWriteForwarder", StringComparison.OrdinalIgnoreCase);
void Drain() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
sealed class Target : IDisposable
{
    private readonly Type _type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
    private readonly object _target;
    public Target(Visual visual, int width, int height) => _target = Activator.CreateInstance(_type, BindingFlags.Instance | BindingFlags.NonPublic, null, [visual, width, height], null)!;
    public void Render() => _type.GetMethod("Render")!.Invoke(_target, null);
    public SKBitmap Snapshot() => (SKBitmap)_type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_target, null)!;
    public void Dispose() => ((IDisposable)_target).Dispose();
}
