using System.Reflection;
using System.Windows.Markup;
using System.Windows.Media.TextFormatting;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string StyledInlines()
    {
        _ = Application.Current ?? new Application();
        var scopeBrush = new SolidColorBrush(Colors.Blue);
        var decorations = TextDecorations.Underline.Clone();
        var boldRun = new Run("BOLD "); var italicRun = new Run("ITALIC ");
        var linkRun = new Run("LINK") { Foreground = Brushes.Red };
        var link = new Hyperlink(linkRun) { NavigateUri = new Uri("https://example.invalid/guide"), TextDecorations = new TextDecorationCollection() };
        var span = new Span { Foreground = scopeBrush, TextDecorations = decorations };
        var bold = new Bold(boldRun); var italic = new Italic(italicRun);
        span.Inlines.Add(bold); span.Inlines.Add(italic); span.Inlines.Add(link);
        var first = new Run("BEGIN "); var last = new Run(" END alpha beta gamma delta epsilon.");
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(first); paragraph.Inlines.Add(span); paragraph.Inlines.Add(last);
        var document = InlineDocument(paragraph);
        document.FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Arial" : "Liberation Sans");
        var boldTypeface = new Typeface(document.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var italicTypeface = new Typeface(document.FontFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
        Require(boldTypeface.TryGetGlyphTypeface(out GlyphTypeface boldFace) && boldFace.Weight.ToOpenTypeWeight() >= 600 && boldFace.StyleSimulations == StyleSimulations.None, "The named family has no genuine Bold face.");
        Require(italicTypeface.TryGetGlyphTypeface(out GlyphTypeface italicFace) && italicFace.Style != FontStyles.Normal && italicFace.StyleSimulations == StyleSimulations.None, "The named family has no genuine Italic face.");
        var viewer = InlineViewer(document);
        try
        {
            Layout(viewer, 380);
            Require(boldRun.FontWeight == FontWeights.Bold && italicRun.FontStyle == FontStyles.Italic && ReferenceEquals(boldRun.Foreground, scopeBrush), "Nested genuine properties were not inherited.");
            Require(ReferenceEquals(HitFirstRun(viewer, linkRun), linkRun) && ReferenceEquals(linkRun.Parent, link), "Hyperlink hit lost original Run/Hyperlink identity.");
            string expected = "BEGIN BOLD ITALIC LINK END alpha beta gamma delta epsilon.";
            viewer.Selection.Select(first.ContentStart, last.ContentEnd); Require(viewer.Selection.Text == expected, "Forward nested selection lost original symbols.");
            viewer.Selection.Select(last.ContentEnd, first.ContentStart); Require(viewer.Selection.Text == expected, "Reverse nested selection differs.");
            viewer.Selection.Select(first.ContentStart, first.ContentStart);
            byte[] original = Raster(viewer); Rect linkBounds = InlineBounds(viewer, linkRun);
            Require(InlineColorCount(original, linkBounds, Colors.Blue) > 2 && InlineColorCount(original, linkBounds, Colors.Red) > 2, $"Outer decoration brush and child text brush were not independently visible: bounds={linkBounds}, blue={InlineColorCount(original, linkBounds, Colors.Blue)}, red={InlineColorCount(original, linkBounds, Colors.Red)}.");
            bold.FontWeight = FontWeights.Normal; Layout(viewer, 380);
            Require(!Raster(viewer).SequenceEqual(original), "Bold mutation did not change actual styled ink.");
            bold.FontWeight = FontWeights.Bold; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Bold restoration differs.");
            italic.FontStyle = FontStyles.Normal; Layout(viewer, 380);
            Require(!Raster(viewer).SequenceEqual(original), "Italic mutation did not change actual styled ink.");
            italic.FontStyle = FontStyles.Italic; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Italic restoration differs.");
            span.Foreground = Brushes.Green; Layout(viewer, 380); byte[] green = Raster(viewer);
            Require(!green.SequenceEqual(original) && InlineColorCount(green, linkBounds, Colors.Green) > 2 && InlineColorCount(green, linkBounds, Colors.Blue) == 0, "Decoration brush mutation did not change the current link underline.");
            span.Foreground = scopeBrush; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Decoration brush restore retained stale ink.");
            decorations.Clear(); Layout(viewer, 380); byte[] undecorated = Raster(viewer);
            Require(!undecorated.SequenceEqual(original) && InlineColorCount(undecorated, linkBounds, Colors.Blue) == 0 && InlineColorCount(undecorated, linkBounds, Colors.Red) > 2, "Decoration removal did not preserve distinct child text.");
            decorations.Add(TextDecorations.Underline[0]); Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Decoration restoration retained stale ink.");
            linkRun.Foreground = Brushes.Magenta; Layout(viewer, 380); byte[] magenta = Raster(viewer);
            Require(!magenta.SequenceEqual(original) && InlineColorCount(magenta, linkBounds, Colors.Magenta) > 2 && InlineColorCount(magenta, linkBounds, Colors.Blue) > 2, "Child color mutation lost scope-owned decoration.");
            linkRun.Foreground = Brushes.Red; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Child color restore differs.");
            Rect wideEnd = last.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            Layout(viewer, 180); Require(last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Y > wideEnd.Y, "Nested inline text did not wrap at narrower width.");
            Require(ReferenceEquals(HitFirstRun(viewer, linkRun), linkRun), "Wrapped hyperlink hit lost original identity.");
            Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Nested inline wrap restoration retained stale ink.");
            return JsonSerializer.Serialize(new { fontFamily = document.FontFamily.Source, boldFace = boldFace.FontUri.ToString(), italicFace = italicFace.FontUri.ToString(), boldWeight = boldFace.Weight.ToOpenTypeWeight(), italicStyle = italicFace.Style.ToString(), simulations = "None", detail = "Nested Bold/Italic/Hyperlink preserve original hit/selection, actual face ink mutations/restores, scoped decoration/child colors and wrapping." });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    internal static string InlineMetrics()
    {
        _ = Application.Current ?? new Application();
        var first = new Run("BEFORE"); var last = new Run("AFTER");
        var empty = new Span { FontSize = 50 }; var emptyRun = new Run { FontSize = 40 };
        var lineBreak = new LineBreak();
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(first); paragraph.Inlines.Add(empty); paragraph.Inlines.Add(emptyRun); paragraph.Inlines.Add(lineBreak); paragraph.Inlines.Add(last);
        var viewer = InlineViewer(InlineDocument(paragraph));
        try
        {
            Layout(viewer, 380);
            Rect large = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Rect after = last.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(after.Y >= large.Bottom - 0.1, $"Empty styled element metrics or explicit LineBreak changed: first={large}, after={after}.");
            var edges = new[] { empty.ElementStart, empty.ContentStart, empty.ContentEnd, empty.ElementEnd, emptyRun.ElementStart, emptyRun.ElementEnd, lineBreak.ElementStart, lineBreak.ElementEnd };
            foreach (var edge in edges) foreach (var direction in new[] { LogicalDirection.Forward, LogicalDirection.Backward })
            { Rect rectangle = edge.GetCharacterRect(direction); Require(!rectangle.IsEmpty && rectangle.Height > 0 && double.IsFinite(rectangle.X), "An original empty/LineBreak element edge has invalid geometry."); }
            viewer.Selection.Select(first.ContentStart, last.ContentEnd); Require(viewer.Selection.Text == "BEFORE" + Environment.NewLine + "AFTER", "LineBreak selection changed text or leaked synthetic empty-element characters.");
            viewer.Selection.Select(last.ContentEnd, first.ContentStart); Require(viewer.Selection.Text == "BEFORE" + Environment.NewLine + "AFTER", "Reverse LineBreak selection differs.");
            viewer.Selection.Select(first.ContentStart, first.ContentStart); byte[] original = Raster(viewer);
            empty.FontSize = 20; emptyRun.FontSize = 20; Layout(viewer, 380);
            Rect small = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            double smallAfterTop = last.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y;
            Require(smallAfterTop < after.Y - 15 && !Raster(viewer).SequenceEqual(original), $"Empty inline style mutation did not affect actual line metrics/ink: large={large}, small={small}, after={after}, smallAfter={last.ContentStart.GetCharacterRect(LogicalDirection.Forward)}.");
            empty.FontSize = 50; emptyRun.FontSize = 40; Layout(viewer, 380);
            Require(first.ContentStart.GetCharacterRect(LogicalDirection.Forward) == large && Raster(viewer).SequenceEqual(original), "Empty inline metric restoration retained stale layout.");
            return JsonSerializer.Serialize(new { largeTop = large.Y, largeHeight = large.Height, smallTop = small.Y, smallHeight = small.Height, afterTop = after.Y, smallAfterTop, edgeSymbols = empty.ElementStart.GetOffsetToPosition(empty.ElementEnd), lineBreakSymbols = lineBreak.ElementStart.GetOffsetToPosition(lineBreak.ElementEnd) });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    internal static string InlineSourceContracts()
    {
        var prior = new Run("alpha") { Language = XmlLanguage.GetLanguage("fr-FR") };
        var span = new Span(prior) { TextDecorations = TextDecorations.Strikethrough };
        var next = new Run("omega"); var empty = new Span { FontSize = 50 }; var lineBreak = new LineBreak();
        TextSource source; TextPointer start;
        Type? portable = typeof(FlowDocument).Assembly.GetType("MS.Internal.Documents.PortableDocumentLayout+ParagraphSource");
        if (portable != null)
        {
            var paragraph = new Paragraph(); paragraph.Inlines.Add(span); paragraph.Inlines.Add(next); paragraph.Inlines.Add(empty); paragraph.Inlines.Add(lineBreak);
            _ = InlineDocument(paragraph); start = paragraph.ContentStart;
            source = (TextSource)Activator.CreateInstance(portable, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { paragraph, 1d }, null)!;
        }
        else
        {
            var block = new TextBlock(); block.Inlines.Add(span); block.Inlines.Add(next); block.Inlines.Add(empty); block.Inlines.Add(lineBreak); start = block.ContentStart;
            Type original = typeof(TextBlock).Assembly.GetType("MS.Internal.Text.ComplexLine", true)!;
            source = (TextSource)Activator.CreateInstance(original, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { block }, null)!;
        }
        try
        {
            Require(source.GetTextRun(0) is TextModifier modifier && modifier.Length == 1 && !modifier.HasDirectionalEmbedding, "Decoration start did not preserve its original edge modifier.");
            Require(source.GetTextRun(start.GetOffsetToPosition(span.ContentEnd)) is TextEndOfSegment, "Decoration end did not close its original scope edge.");
            Require(source.GetTextRun(start.GetOffsetToPosition(empty.ElementStart)) is TextCharacters emptyRun && emptyRun.Length == 2 && emptyRun.Properties.FontRenderingEmSize == 50, "Empty span did not retain two source symbols with its actual font metrics.");
            Require(source.GetTextRun(start.GetOffsetToPosition(lineBreak.ElementStart)) is TextEndOfLine end && end.Length == 2, "LineBreak did not consume its two original symbols.");
            var atStart = source.GetPrecedingText(0); Require(atStart.Length == 0 && atStart.Value.CharacterBufferRange.Length == 0 && atStart.Value.CultureInfo == null, "Preceding text at source start differs.");
            foreach (var pointer in new[] { span.ElementEnd, next.ContentStart })
            {
                int index = start.GetOffsetToPosition(pointer);
                var preceding = source.GetPrecedingText(index);
                Require(preceding.Length == prior.Text.Length + prior.ContentEnd.GetOffsetToPosition(pointer) && preceding.Value.CharacterBufferRange.Length == prior.Text.Length && preceding.Value.CultureInfo.Name == "fr-FR", "Preceding text lost hidden edge consumption or the actual preceding Run culture.");
            }
            return "Actual source runs preserve decoration/empty/LineBreak edges and preceding text length/culture across nested hidden symbols.";
        }
        finally { (source as IDisposable)?.Dispose(); }
    }

    internal static string InlineFailure()
    {
        _ = Application.Current ?? new Application();
        var child = new Border { Height = 30, Background = Brushes.Blue };
        var paragraph = new Paragraph(new Span(new Run("COMMITTED INLINE"))) { Margin = new Thickness(0) };
        var document = InlineDocument(paragraph); document.Blocks.InsertBefore(paragraph, new BlockUIContainer(child));
        var viewer = InlineViewer(document);
        try
        {
            Layout(viewer, 380); Size oldDesired = child.DesiredSize, oldRender = child.RenderSize;
            Vector oldOffset = VisualTreeHelper.GetOffset(child); var parent = VisualTreeHelper.GetParent(child); byte[] original = Raster(viewer);
            var rejected = new InlineUIContainer(new Border()); paragraph.Inlines.Add(rejected);
            PlatformNotSupportedException? failure = null;
            try { Layout(viewer, 180); } catch (PlatformNotSupportedException error) { failure = error; }
            Require(failure != null && failure.Message.Contains("InlineUIContainer", StringComparison.Ordinal), "Unsupported inline did not preserve the primary type-specific error.");
            Require(child.DesiredSize == oldDesired && child.RenderSize == oldRender && VisualTreeHelper.GetOffset(child) == oldOffset && ReferenceEquals(VisualTreeHelper.GetParent(child), parent), "Failed inline layout changed committed child state or ownership.");
            Require(Raster(viewer).SequenceEqual(original), "Failed inline layout changed committed pixels.");
            paragraph.Inlines.Remove(rejected); Layout(viewer, 380);
            Require(Raster(viewer).SequenceEqual(original) && paragraph.ContentStart.HasValidLayout, "Layout did not recover after removing unsupported inline.");
            var directional = new Span(new Run("DIRECTION")) { FlowDirection = FlowDirection.RightToLeft }; paragraph.Inlines.Add(directional);
            bool rejectedDirection = false; try { Layout(viewer, 380); } catch (NotSupportedException error) when (error.Message.Contains("direction", StringComparison.OrdinalIgnoreCase)) { rejectedDirection = true; }
            Require(rejectedDirection, "Directional inline embedding was silently accepted.");
            paragraph.Inlines.Remove(directional); Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Directional rejection did not recover.");
            return "Unsupported inline/embedding preserve primary error, committed child geometry/ownership/ink and recover after removal.";
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    private static FlowDocument InlineDocument(Paragraph paragraph) => new FlowDocument(paragraph)
    {
        FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans"),
        FontSize = 20, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new Thickness(8), TextAlignment = TextAlignment.Left
    };
    private static FlowDocumentScrollViewer InlineViewer(FlowDocument document)
    { var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, IsSelectionEnabled = true, Background = Brushes.White }; viewer.BeginInit(); viewer.EndInit(); return viewer; }
    private static Rect InlineBounds(FlowDocumentScrollViewer viewer, Run run)
    {
        var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
        Rect first = run.ContentStart.GetCharacterRect(LogicalDirection.Forward), last = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        return scope.TransformToAncestor(viewer).TransformBounds(new Rect(new Point(first.Left + 0.5, first.Top), new Point(last.Right - 0.5, first.Bottom)));
    }
    private static bool InlineColorPair(byte first, byte second, byte expectedFirst, byte expectedSecond) =>
        expectedFirst == expectedSecond ? Math.Abs(first - second) < 35 : expectedFirst > expectedSecond ? first > second + 32 : second > first + 32;
    private static int InlineColorCount(byte[] pixels, Rect bounds, Color color)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)Math.Floor(bounds.Top)); y < Math.Min(220, (int)Math.Ceiling(bounds.Bottom)); y++)
        for (int x = Math.Max(0, (int)Math.Ceiling(bounds.Left)); x < Math.Min(380, (int)Math.Floor(bounds.Right)); x++)
        { int index = (y * 380 + x) * 4; if (pixels[index + 3] > 220 && InlineColorPair(pixels[index], pixels[index + 1], color.B, color.G) && InlineColorPair(pixels[index], pixels[index + 2], color.B, color.R) && InlineColorPair(pixels[index + 1], pixels[index + 2], color.G, color.R)) count++; }
        return count;
    }
}
