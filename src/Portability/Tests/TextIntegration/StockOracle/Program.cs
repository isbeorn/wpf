using System.Text.Json;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;

object? result = null;
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        if (args.Contains("--modifier-continuation")) result = ModifierContinuationCases.Capture();
        else if (args.Contains("--capture-hidden")) result = HiddenRunCases.Capture();
        else if (args.Contains("--verify-alignment"))
        {
            TextOracleCases.VerifyAlignment(idealOnly: true);
            result = new { check = "horizontal-paragraph-alignment-Ideal", status = "pass" };
        }
        else if (args.Contains("--capture-alignment")) result = TextOracleCases.CaptureAlignment();
        else if (args.Contains("--words")) result = WordSelectionCases.Capture();
        else if (args.Contains("--controls"))
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var block = new TextBlock { Text = "Default 123", FontSize = 24,
                FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans") };
            var paragraph = (TextParagraphProperties)typeof(TextBlock).GetMethod("GetLineProperties", BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null)!.Invoke(block, null)!;
            var properties = paragraph.DefaultTextRunProperties;
            block.Measure(new Size(300, 80));
            block.Arrange(new Rect(0, 0, 300, 80));
            block.UpdateLayout();
            var bitmap = new RenderTargetBitmap(300, 80, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(block);
            byte[] pixels = new byte[300 * 80 * 4];
            bitmap.CopyPixels(pixels, 300 * 4, 0);
            result = new { framework = typeof(TextFormatter).Assembly.FullName, culture = properties.CultureInfo.Name, language = block.Language.IetfLanguageTag,
                source = properties.NumberSubstitution.CultureSource.ToString(), substitution = properties.NumberSubstitution.Substitution.ToString(),
                digitSubstitution = CultureInfo.CurrentCulture.NumberFormat.DigitSubstitution.ToString(), nativeDigits = CultureInfo.CurrentCulture.NumberFormat.NativeDigits,
                ink = pixels.Where((_, index) => index % 4 == 3).Count(value => value != 0) };
        }
        else result = args.Contains("--capture-boundaries") ? TextOracleCases.CaptureBoundaries() : TextOracleCases.Capture();
    }
    catch (Exception exception) { failure = exception; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure != null) throw failure;
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
