using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using SkiaSharp;

var results = new List<object>();
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        results.AddRange(WrappedTextBlockCases.Verify(RasterWrappedTextBlock));
        results.Add(VerifyAlignedTextBlock());
        foreach (object result in WordSelectionCases.Verify()) results.Add(result);
        var block = new TextBlock { Text = "Default 123", FontSize = 24,
            FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans") };
        var paragraph = (TextParagraphProperties)typeof(TextBlock).GetMethod("GetLineProperties", BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null)!.Invoke(block, null)!;
        var properties = paragraph.DefaultTextRunProperties;
        if (properties.NumberSubstitution is not { CultureSource: NumberCultureSource.User, Substitution: NumberSubstitutionMethod.AsCulture })
            throw new InvalidOperationException("The real TextBlock default number substitution was not exercised.");
        var box = new TextBox { Text = "office 123", FontSize = 24, FontFamily = block.FontFamily };
        Type textPropertiesType = typeof(TextBox).Assembly.GetType("MS.Internal.Text.TextProperties", true)!;
        var boxProperties = (TextRunProperties)Activator.CreateInstance(textPropertiesType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, [box, false], null)!;
        if (boxProperties.TypographyProperties is not { StandardLigatures: true, ContextualLigatures: true, ContextualAlternates: true, Kerning: true })
            throw new InvalidOperationException("Actual TextBox run properties did not exercise the non-null default typography profile.");
        using (var formatter = TextFormatter.Create())
        using (var boxLine = formatter.FormatLine(new ControlSource(box.Text, boxProperties), 0, 300, new ControlParagraph(boxProperties), null))
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) boxLine.Draw(context, new Point(4, 4), InvertAxes.None);
            if (boxLine.Width <= 0 || visual.Drawing.Children.Count == 0) throw new InvalidOperationException("Actual TextBox default typography produced no retained glyph drawing.");
            results.Add(new { name = "actual-textbox-default-typography-run-and-drawing", status = "pass", boxLine.Width,
                profile = boxProperties.TypographyProperties.GetType().FullName });
        }
        block.Measure(new Size(300, 80));
        block.Arrange(new Rect(0, 0, 300, 80));
        block.UpdateLayout();
        Type targetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
        using var target = (IDisposable)Activator.CreateInstance(targetType, BindingFlags.Instance | BindingFlags.NonPublic, null, [block, 300, 80], null)!;
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        using var bitmap = (SKBitmap)targetType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, y).Alpha != 0) ink++;
        if (ink == 0 || block.DesiredSize.Width <= 0) throw new InvalidOperationException("Actual TextBlock default properties produced no retained ink.");
        results.Add(new { name = "actual-textblock-default-number-substitution", status = "pass", culture = properties.CultureInfo.Name,
            language = block.Language.IetfLanguageTag, source = properties.NumberSubstitution.CultureSource.ToString(),
            substitution = properties.NumberSubstitution.Substitution.ToString(), nativeDigits = CultureInfo.CurrentCulture.NumberFormat.NativeDigits,
            digitSubstitution = CultureInfo.CurrentCulture.NumberFormat.DigitSubstitution.ToString(), ink });
    }
    catch (Exception exception) { failure = exception; }
    finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
});
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine(JsonSerializer.Serialize(new { fixture = "actual-text-controls-and-word-selection", results, failure = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
return failure == null ? 0 : 1;

static byte[] RasterWrappedTextBlock(TextBlock block, int width, int height)
{
    Type targetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
    using var target = (IDisposable)Activator.CreateInstance(targetType, BindingFlags.Instance | BindingFlags.NonPublic, null, [block, width, height], null)!;
    targetType.GetMethod("Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null);
    using var bitmap = (SKBitmap)targetType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    return bitmap.Bytes;
}
static object VerifyAlignedTextBlock()
{
    const int width = 1100, height = 100;
    var block = new TextBlock
    {
        Text = "Sequence Target Area\nAdd Deep Sky Object Sequences here or add Targets via Sky Atlas or Framing Assistant",
        FontSize = 16, FontStyle = FontStyles.Italic, Foreground = Brushes.Black,
        FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans"),
        TextAlignment = TextAlignment.Center
    };
    block.Measure(new Size(width, height));
    block.Arrange(new Rect(0, 0, width, height));
    block.UpdateLayout();
    Size measured = block.DesiredSize;
    Type targetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", true)!;
    using var target = (IDisposable)Activator.CreateInstance(targetType, BindingFlags.Instance | BindingFlags.NonPublic, null, [block, width, height], null)!;
    byte[]? original = null;
    var positions = new List<int[]>();
    foreach (TextAlignment alignment in new[] { TextAlignment.Center, TextAlignment.Right, TextAlignment.Left, TextAlignment.Center })
    {
        block.TextAlignment = alignment;
        block.Measure(new Size(width, height));
        block.Arrange(new Rect(0, 0, width, height));
        block.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        using var bitmap = (SKBitmap)targetType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
        var left = new[] { width, width };
        int split = (int)Math.Round(measured.Height / 2);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha != 0) left[y < split ? 0 : 1] = Math.Min(left[y < split ? 0 : 1], x);
        if (left.Any(value => value == width) || block.DesiredSize != measured)
            throw new InvalidOperationException("The original two-line message lost ink or changed its measured size under alignment.");
        if (original == null) original = bitmap.Bytes;
        else if (positions.Count == 3 && !original.SequenceEqual(bitmap.Bytes))
            throw new InvalidOperationException("Restoring Center did not restore the retained TextBlock pixels.");
        positions.Add(left);
    }
    for (int line = 0; line < 2; line++)
        if (!(positions[2][line] < positions[0][line] && positions[0][line] < positions[1][line]))
            throw new InvalidOperationException("The two actual rendered lines did not move Left < Center < Right.");
    return new { name = "actual-nina-two-line-alignment-retained-restore", status = "pass", positions, measured.Width, measured.Height };
}

sealed class ControlParagraph(TextRunProperties properties) : TextParagraphProperties
{
    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
    public override TextAlignment TextAlignment => TextAlignment.Left;
    public override double LineHeight => 0;
    public override bool FirstLineInParagraph => true;
    public override TextRunProperties DefaultTextRunProperties => properties;
    public override TextWrapping TextWrapping => TextWrapping.NoWrap;
    public override TextMarkerProperties TextMarkerProperties => null!;
    public override double Indent => 0;
}
sealed class ControlSource(string text, TextRunProperties properties) : TextSource
{
    public override TextRun GetTextRun(int index) => index < text.Length ? new TextCharacters(text, index, text.Length - index, properties) : new TextEndOfParagraph(1);
    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
        new(limit, new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), new CharacterBufferRange(text, 0, Math.Min(limit, text.Length))));
    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
}
