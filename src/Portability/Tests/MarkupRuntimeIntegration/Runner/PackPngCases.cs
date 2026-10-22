using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PackPngCases
{
    private static readonly byte[] Expected = [0, 0, 255, 255, 0, 255, 0, 128, 0, 0, 0, 0, 56, 34, 12, 64];
    internal static string Run()
    {
        _ = Application.Current ?? new Application();
        var basis = new Uri("pack://application:,,,/Portability.MarkupIntegration.Fixture;component/");
        var relative = new Uri("assets/png%20sample%20%ce%a9.png", UriKind.Relative);
        var absolute = new Uri(basis, relative);
        Verify(new BitmapImage(absolute));
        var loaded = new BitmapImage(); loaded.BeginInit(); loaded.CacheOption = BitmapCacheOption.OnLoad; loaded.UriSource = absolute; loaded.EndInit();
        Verify(loaded);
        var delayed = new BitmapImage(); delayed.BeginInit(); delayed.UriSource = relative; delayed.EndInit(); delayed.BaseUri = basis;
        Verify(delayed);
        foreach (BitmapImage copy in new[] { delayed.Clone(), delayed.CloneCurrentValue(), (BitmapImage)delayed.GetAsFrozen(), (BitmapImage)delayed.GetCurrentValueAsFrozen() }) Verify(copy);
        delayed.Freeze();
        Task.Run(() => Verify(delayed)).GetAwaiter().GetResult();
        Verify(delayed.Clone());
        Verify(delayed.CloneCurrentValue());
        Verify((BitmapImage)delayed.GetAsFrozen());
        Verify((BitmapImage)delayed.GetCurrentValueAsFrozen());
        try { _ = new BitmapImage(new Uri(basis, "assets/missing.png")); throw new InvalidOperationException("Missing resource unexpectedly loaded."); }
        catch (IOException) { }
        Verify(new BitmapImage(absolute));

        Type context = typeof(BitmapImage).Assembly.GetType("MS.Internal.XpsLoadingContext", true)!;
        PropertyInfo active = context.GetProperty("ActivePackageUri", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previous = active.GetValue(null);
        var blocked = new BitmapImage();
        try
        {
            active.SetValue(null, new Uri("file:///unrelated-test-package.xps"));
            blocked.BeginInit(); blocked.UriSource = relative; blocked.EndInit();
        }
        finally { active.SetValue(null, previous); }
        blocked.BaseUri = basis;
        try { _ = blocked.PixelWidth; throw new InvalidOperationException("Delayed image escaped its captured XPS package."); }
        catch (FileFormatException) { }
        return "Actual compiled PNG resource retained full pixels, DPI and alpha through default/OnLoad, delayed BaseUri, clone/freeze, missing-resource recovery and captured XPS containment.";
    }

    internal static string CloneDelayed(string operation)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("assets/png%20sample%20%ce%a9.png", UriKind.Relative);
        image.EndInit();
        image.BaseUri = new Uri("pack://application:,,,/Portability.MarkupIntegration.Fixture;component/");
        BitmapImage copy = operation switch
        {
            "Clone" => image.Clone(),
            "CloneCurrentValue" => image.CloneCurrentValue(),
            "GetAsFrozen" => (BitmapImage)image.GetAsFrozen(),
            "GetCurrentValueAsFrozen" => (BitmapImage)image.GetCurrentValueAsFrozen(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        bool frozen = operation is "GetAsFrozen" or "GetCurrentValueAsFrozen";
        if (image.IsFrozen || copy.IsFrozen != frozen || ReferenceEquals(image, copy))
            throw new InvalidOperationException("Delayed clone freeze or identity semantics changed.");
        Verify(copy);
        Verify(image);
        if (frozen) Task.Run(() => Verify(copy)).GetAwaiter().GetResult();
        return operation + " cloned a fresh delayed relative pack image before its first pixel read, preserving full pixels, DPI and freeze semantics.";
    }

    private static void Verify(BitmapImage image)
    {
        if (image.PixelWidth != 2 || image.PixelHeight != 2 || image.Format != PixelFormats.Bgra32 ||
            image.DpiX != (double)(float)(3780 * 0.0254) || image.DpiY != (double)(float)(7560 * 0.0254))
            throw new InvalidOperationException("Compiled PNG metadata changed.");
        byte[] pixels = new byte[16]; image.CopyPixels(pixels, 8, 0);
        if (!pixels.SequenceEqual(Expected)) throw new InvalidOperationException("Compiled pack PNG full pixels changed.");
    }
}
