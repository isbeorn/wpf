using System.Security.Cryptography;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PackDecoderCases
{
    internal static string Run()
    {
        _ = Application.Current ?? new Application();
        var basis = new Uri("pack://application:,,,/Portability.MarkupIntegration.Fixture;component/");
        var ico = new Uri(basis, "Assets/nina.ico");
        var png = new Uri(basis, "Assets/dockleft.png");
        using (var payload = Application.GetResourceStream(ico).Stream)
            Require(Convert.ToHexString(SHA256.HashData(payload)) == "9446DA2A471DAE3A3B16526418C927F28BFB330862540DBE5333009009EB4BB8", "Compiled ICO resource bytes differ from the original NINA payload.");
        var converted = (BitmapFrame)new ImageSourceConverter().ConvertFrom(null, CultureInfo.InvariantCulture, ico.ToString())!;
        VerifyIcon(converted);
        foreach (BitmapCacheOption cache in new[] { BitmapCacheOption.Default, BitmapCacheOption.OnLoad })
        {
            VerifyIcon(BitmapFrame.Create(ico, BitmapCreateOptions.None, cache));
            var typed = new IconBitmapDecoder(ico, BitmapCreateOptions.None, cache);
            VerifyIcon(typed.Frames[0]);
            var decodedPng = new PngBitmapDecoder(png, BitmapCreateOptions.None, cache);
            Require(decodedPng.Frames.Count == 1 && decodedPng.Frames[0].PixelWidth == 32 &&
                decodedPng.Frames[0].PixelHeight == 29 && decodedPng.Frames[0].Format == PixelFormats.Bgr32,
                "Concrete pack PNG constructor lost original dimensions or factory format.");
        }
        var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri(
            "/Portability.MarkupIntegration.Fixture;component/DecoderImages.xaml", UriKind.Relative));
        foreach (string key in new[] { "NinaIcon", "DockLeft", "DockRight", "DockTop", "DockBottom" })
        {
            var image = (Image)dictionary[key];
            Require(image.Source is BitmapFrame, "Compiled XAML Image.Source did not resolve to an actual decoder frame.");
            var frame = (BitmapFrame)image.Source;
            if (key == "NinaIcon") VerifyIcon(frame);
            else Require(frame.Decoder is PngBitmapDecoder && frame.Format == PixelFormats.Bgr32 && frame.Decoder.Frames.Count == 1,
                "Compiled original dock PNG changed decoder identity or format.");
            image.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            image.Arrange(new Rect(image.DesiredSize));
            Require(image.DesiredSize.Width == frame.Width && image.DesiredSize.Height == frame.Height,
                "Actual Image layout ignored decoded resource dimensions.");
        }
        try { _ = BitmapFrame.Create(new Uri(basis,"Assets/missing.ico")); throw new InvalidOperationException("Missing pack resource loaded."); }
        catch (System.IO.IOException) { }
        VerifyIcon(BitmapFrame.Create(ico));
        foreach (string uri in new[] { "https://example.invalid/icon.ico", "file:///unused-icon.ico", "pack://siteoforigin:,,,/icon.ico" })
            Expect<NotSupportedException>(() => BitmapFrame.Create(new Uri(uri)));
        Type context = typeof(BitmapFrame).Assembly.GetType("MS.Internal.XpsLoadingContext", true)!;
        var active = context.GetProperty("ActivePackageUri", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        object? previous = active.GetValue(null);
        try
        {
            active.SetValue(null, new Uri("file:///unrelated-decoder-package.xps"));
            Expect<System.IO.FileFormatException>(() => BitmapFrame.Create(ico));
        }
        finally { active.SetValue(null, previous); }
        VerifyIcon(BitmapFrame.Create(ico));
        return "Original ICO and four PNG payloads decoded through actual pack factories, ImageSourceConverter and compiled XAML Image.Source with real layout, ownership and recovery; ICO retains directory order.";
    }
    private static void VerifyIcon(BitmapFrame frame)
    {
        Require(frame.Decoder is IconBitmapDecoder && frame.Decoder.Frames.Count == 6 && frame.PixelWidth == 256 &&
            frame.IsFrozen && ReferenceEquals(frame, frame.Decoder.Frames[0]) && frame.Thumbnail is { IsFrozen: true },
            "Pack ICO factory bypassed the genuine six-frame decoder graph.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
