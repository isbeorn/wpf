extern alias WpfFork;

using WpfFork::System.Windows;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Media.Imaging;

internal static class IconCases
{
    internal static void Exercise(X11Fixture native, Action<string, Action> run, Action<Func<bool>> wait, Action drain, string? originalIconPath = null)
    {
        run("native-window-icon-before-source-initialization", () => BeforeSource(native, wait));
        run("native-window-icon-set-replace-clear-restore-and-alpha", () => Lifecycle(native, wait, drain));
        run("native-window-icon-BIG-REQUESTS-complete-publication", () => LargeBitmap(native, wait, drain));
        run("native-window-icon-negotiated-capacity-boundary", () => Capacity(native, wait, drain));
        run("native-window-icon-failure-preserves-previous-property", () => Failure(native, wait, drain));
        if (originalIconPath != null)
            run("native-window-icon-original-NINA-six-frames", () => OriginalIcon(native, wait, drain, originalIconPath));
    }

    private static void BeforeSource(X11Fixture native, Action<Func<bool>> wait)
    {
        var icon = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0x33, 0x22, 0x11, 0xff, 0x66, 0x55, 0x44, 0x80 }, 8);
        var window = new Window { Title = "Owned native Window.Icon before source", Width = 120, Height = 80 };
        bool initialized = false;
        try
        {
            window.Icon = icon;
            window.SourceInitialized += (_, _) =>
            {
                var xid = native.FindWindow(window.Title);
                Require(xid != IntPtr.Zero && !native.IsMapped(xid), "The icon source initialization observer ran after native map or before native creation.");
                wait(() => native.WindowIcon(xid)?.SequenceEqual(new uint[] { 2, 1, 0xff112233, 0x80445566 }) == true);
                Require(!native.IsMapped(xid), "The native icon was only installed after mapping.");
                initialized = true;
            };
            window.Show();
            wait(() => initialized && native.IsMapped(native.FindWindow(window.Title)));
            Require(ReferenceEquals(window.Icon, icon), "The native icon path replaced or disposed the caller's ImageSource.");
            var pixels = new byte[8]; icon.CopyPixels(pixels, 8, 0);
            Require(pixels.SequenceEqual(new byte[] { 0x33, 0x22, 0x11, 0xff, 0x66, 0x55, 0x44, 0x80 }), "Publishing the icon mutated caller pixels.");
        }
        finally { window.Close(); }
        var afterClose = new byte[8]; icon.CopyPixels(afterClose, 8, 0);
        Require(afterClose[0] == 0x33 && afterClose[7] == 0x80, "Closing the native window invalidated caller-owned bitmap storage.");
    }

    private static void Lifecycle(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var window = new Window { Title = "Owned native icon lifecycle", Width = 120, Height = 80 };
        var straight = BitmapSource.Create(1, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0x30, 0x20, 0x10, 0xff, 3, 2, 1, 0 }, 4);
        var premultiplied = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 0x30, 0x20, 0x10, 0xff, 16, 32, 64, 128, 0, 0, 0, 0 }, 12);
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
            Require(native.WindowIcon(xid) == null, "A default Window published an explicit placeholder icon.");
            window.Icon = straight; drain();
            Require(native.WindowIcon(xid)?.SequenceEqual(new uint[] { 1, 2, 0xff102030, 0x00010203 }) == true, "Straight bitmap alpha/channels or row order changed.");
            window.Icon = premultiplied; drain();
            Require(native.WindowIcon(xid)?.SequenceEqual(new uint[] { 3, 1, 0xff102030, 0x80804020, 0 }) == true, "Premultiplied icon pixels were not converted to straight packed ARGB.");
            window.Icon = null; drain();
            Require(native.WindowIcon(xid) == null, "Clearing Window.Icon retained an empty or stale native property.");
            window.Icon = straight; drain();
            Require(native.WindowIcon(xid)?.SequenceEqual(new uint[] { 1, 2, 0xff102030, 0x00010203 }) == true && ReferenceEquals(window.Icon, straight), "Restoring the icon retained stale dimensions or replaced its caller object.");
        }
        finally { window.Close(); }
        var pixels = new byte[12]; premultiplied.CopyPixels(pixels, 12, 0);
        Require(pixels[6] == 64 && pixels[7] == 128, "Native icon conversion mutated or disposed its caller's premultiplied pixels.");
    }

    private static void LargeBitmap(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var pixels = Enumerable.Range(0, 256 * 256).Select(index => 0xff000000u | (uint)index).ToArray();
        var icon = BitmapSource.Create(256, 256, 96, 96, PixelFormats.Bgra32, null, pixels, 256 * 4);
        var window = new Window { Title = "Owned native large icon", Width = 120, Height = 80 };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
            var limits = native.IconRequestLimits;
            Require(pixels.Length + 8 > limits.Standard && pixels.Length + 9 <= limits.Extended,
                "The private X server did not exercise the negotiated BIG-REQUESTS window icon path.");
            window.Icon = icon; drain();
            var actual = native.WindowIcon(xid);
            Require(actual is { Length: 65538 } && actual[0] == 256 && actual[1] == 256 && actual.AsSpan(2).SequenceEqual(pixels),
                "The large icon property was clipped, reordered or truncated at the standard request boundary.");
        }
        finally { window.Close(); }
    }

    private static void Failure(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var valid = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 3, 2, 1, 0xff }, 4);
        var window = new Window { Title = "Owned native icon failure", Width = 120, Height = 80, Icon = valid };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
            var previous = native.WindowIcon(xid)!;
            Expect<PlatformNotSupportedException>(() => window.Icon = new DrawingImage());
            Require(native.WindowIcon(xid)?.SequenceEqual(previous) == true, "Unsupported icon conversion changed the previous native property.");
            foreach (var dimensions in new[] { (0, 1), (1, 0) })
            {
                Expect<ArgumentException>(() => window.Icon = new InvalidSizeBitmap(dimensions.Item1, dimensions.Item2));
                Require(native.WindowIcon(xid)?.SequenceEqual(previous) == true, "Invalid icon dimensions changed the previous native property.");
            }
            window.Icon = null; drain(); Require(native.WindowIcon(xid) == null, "Icon failure prevented a subsequent public clear.");
            window.Icon = valid; drain(); Require(native.WindowIcon(xid)?.SequenceEqual(previous) == true, "Icon failure prevented a subsequent valid publication.");
        }
        finally { window.Close(); }
    }

    private static void Capacity(X11Fixture native, Action<Func<bool>> wait, Action drain)
    {
        var limits = native.IconRequestLimits;
        int capacity = checked((int)(limits.Extended > limits.Standard ? limits.Extended - 7 : limits.Standard - 6));
        int width = checked(capacity - 2);
        var maximum = BitmapSource.Create(width, 1, 96, 96, PixelFormats.Bgra32, null, new byte[checked(width * 4)], checked(width * 4));
        var window = new Window { Title = "Owned native icon capacity", Width = 120, Height = 80 };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
            window.Icon = maximum; drain();
            var previous = native.WindowIcon(xid);
            Require(previous != null && previous.Length == capacity && previous[0] == width && previous[1] == 1 && previous.AsSpan(2).IndexOfAnyExcept(0u) == -1,
                "The largest negotiated complete icon request did not publish every value.");
            var tooLarge = BitmapSource.Create(width + 1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[checked((width + 1) * 4)], checked((width + 1) * 4));
            Expect<ArgumentException>(() => window.Icon = tooLarge);
            Require(native.WindowIcon(xid)?.SequenceEqual(previous!) == true, "One icon value above negotiated capacity changed the previous native property.");
            window.Icon = null; drain(); Require(native.WindowIcon(xid) == null, "Clearing the maximum-size icon retained native data.");
        }
        finally { window.Close(); }
    }

    private static void OriginalIcon(X11Fixture native, Action<Func<bool>> wait, Action drain, string path)
    {
        byte[] encoded = File.ReadAllBytes(path);
        Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded)) == "9446DA2A471DAE3A3B16526418C927F28BFB330862540DBE5333009009EB4BB8", "The icon corpus is not the pinned original NINA resource.");
        IconBitmapDecoder decoder;
        using (var stream = new MemoryStream(encoded)) decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Require(decoder.Frames.Count == 6 && decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(new[] { 16, 32, 48, 64, 128, 256 }), "The original ICO decoder did not expose all six public resolutions.");
        var expected = new List<uint>();
        foreach (var frame in decoder.Frames)
        {
            Require(frame.PixelWidth == frame.PixelHeight && frame.Format == PixelFormats.Bgra32, "The original icon decoder did not provide the accepted square straight-BGRA frames.");
            expected.Add((uint)frame.PixelWidth); expected.Add((uint)frame.PixelHeight);
            var pixels = new uint[checked(frame.PixelWidth * frame.PixelHeight)]; frame.CopyPixels(pixels, checked(frame.PixelWidth * 4), 0); expected.AddRange(pixels);
        }
        var window = new Window { Title = "Owned original NINA icon", Width = 120, Height = 80, Icon = decoder.Frames[0] };
        try
        {
            window.Show(); var xid = native.FindWindow(window.Title); wait(() => native.IsMapped(xid));
            Require(native.WindowIcon(xid)?.SequenceEqual(expected) == true, "Native icon publication dropped, sorted or changed original ICO frames/pixels.");
            window.Icon = null; drain(); Require(native.WindowIcon(xid) == null, "Clearing the original multi-frame icon left native data behind.");
            window.Icon = decoder.Frames[0]; drain(); Require(native.WindowIcon(xid)?.SequenceEqual(expected) == true, "Restoring the original ICO appended stale frames or lost its decoder graph.");
        }
        finally { window.Close(); }
        var afterClose = new byte[checked(decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight * 4)];
        decoder.Frames[0].CopyPixels(afterClose, checked(decoder.Frames[0].PixelWidth * 4), 0);
    }

    private sealed class InvalidSizeBitmap(int width, int height) : BitmapSource
    {
        public override int PixelWidth => width;
        public override int PixelHeight => height;
        protected override Freezable CreateInstanceCore() => new InvalidSizeBitmap(width, height);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("The public icon operation did not reject invalid input with " + typeof(T).Name + ".");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
