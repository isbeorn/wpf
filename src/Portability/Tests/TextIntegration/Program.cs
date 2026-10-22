using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using SkiaSharp;

if (args.Contains("--capture-inversion")) { Console.WriteLine(JsonSerializer.Serialize(InvertedTextCases.Capture(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
if (args.Contains("--capture-hidden")) { Console.WriteLine(JsonSerializer.Serialize(HiddenRunCases.Capture(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
if (args.Contains("--capture-modifier-continuation")) { Console.WriteLine(JsonSerializer.Serialize(ModifierContinuationCases.Capture(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
var checks = new List<object>();
if (args.Contains("--capture-alignment"))
{
    Console.WriteLine(JsonSerializer.Serialize(TextOracleCases.CaptureAlignment(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Contains("--capture-oracle"))
{
    Console.WriteLine(JsonSerializer.Serialize(TextOracleCases.Capture(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Contains("--capture-boundaries"))
{
    Console.WriteLine(JsonSerializer.Serialize(TextOracleCases.CaptureBoundaries(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Contains("--measure-continuations"))
{
    Console.WriteLine(JsonSerializer.Serialize(ContinuationBenchmark.Capture(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
int exit = 0;
try
{
    string family = OperatingSystem.IsWindows() ? "Arial" : "DejaVu Sans";
    var properties = new RunProperties(new Typeface(family));
    var paragraph = new Paragraph(properties);
    using var formatter = TextFormatter.Create();
    Regression("horizontal-paragraph-alignment", () =>
    {
        TextOracleCases.VerifyAlignment();
        Require(true, "horizontal-paragraph-alignment", "Center and Right preserve shaping while aligning metrics, caret distances, bounds and retained drawing for both flow directions.");
    });
    BidiTests.Run(formatter, Require, Regression);
    Regression("hidden-source-runs", () => HiddenRunCases.Verify(formatter, Require));
    Regression("hidden-source-suffix", () => HiddenRunCases.VerifySourceSuffix(formatter, Require));
    Regression("automatic-line-height", () => HiddenRunCases.VerifyAutomaticHeight(Require));
    Regression("inverted-text-drawing", () =>
    {
        int cases = InvertedTextCases.Verify();
        Require(cases == 66, "inverted-text-drawing", "All axes preserve stock surface, origin, background, decoration, object callback and exception restoration contracts for both flows.");
    });
    Regression("document-source-runs", () => DocumentRunCases.Verify(formatter, Require));
    Regression("document-source-boundaries", () => DocumentRunCases.VerifyBoundaries(formatter, Require));
    Regression("modifier-soft-continuation", () => ModifierContinuationCases.Verify(Require));
    Regression("cache-backed-source-continuation", () => ContinuationCacheCases.Verify(formatter, Require));
    Regression("empty-and-end-caret-bounds-regression", () =>
    {
        foreach (string text in new[] { "", "abc", "abc\r\n" })
        {
            using var caretLine = formatter.FormatLine(new Source(text, properties), 0, 1000, paragraph, null);
            int end = caretLine.Length - caretLine.NewlineLength;
            foreach (int length in new[] { 0, caretLine.NewlineLength })
            {
                var bounds = caretLine.GetTextBounds(end, length);
                Require(bounds.Count == 1 && (bounds[0].TextRunBounds == null || bounds[0].TextRunBounds.Count == 1) &&
                    bounds[0].Rectangle.Width == 0 && Near(bounds[0].Rectangle.Height, caretLine.Height),
                    $"empty-end-caret-bounds-{text.Length}-{length}", "The real TextBoxLine single-position bounds contract accepts empty and terminator caret rectangles.");
            }
        }
    });
    Regression("exact-fit-word-regression", () =>
    {
        foreach (TextWrapping wrapping in new[] { TextWrapping.Wrap, TextWrapping.WrapWithOverflow })
        {
            foreach (int spaces in new[] { 1, 2, 3 })
            {
                string gap = new(' ', spaces);
                string fitting = "alpha" + gap + "beta";
                string text = fitting + gap + "gamma";
                using var measured = formatter.FormatLine(new Source(fitting, properties), 0, 1000, paragraph, null);
                using var above = formatter.FormatLine(new Source(text, properties), 0, measured.Width + 0.1, new Paragraph(properties, wrapping), null);
                Require(above.Length == fitting.Length + spaces && !above.IsTruncated && !above.HasOverflowed && Near(above.Width, measured.Width),
                    $"word-fit-{wrapping}-{spaces}", "A fully fitting last word stays on the line and all following spaces are consumed separately from visible width.");
                using var exact = formatter.FormatLine(new Source(text, properties), 0, measured.Width, new Paragraph(properties, wrapping), null);
                Require(exact.Length == above.Length && !exact.HasOverflowed && Near(exact.Width, measured.Width),
                    $"word-exact-fit-{wrapping}-{spaces}", "An exactly fitting visible word keeps the same source boundary as a slightly wider line.");
                using var below = formatter.FormatLine(new Source(text, properties), 0, measured.Width - 0.1, new Paragraph(properties, wrapping), null);
                Require(below.Length == 5 + spaces && !below.HasOverflowed,
                    $"word-below-fit-{wrapping}-{spaces}", "Narrowing past the last word moves that whole word to the next line without losing the preceding spaces.");
            }
            using var word = formatter.FormatLine(new Source("alpha", properties), 0, 1000, paragraph, null);
            using var afterWord = formatter.FormatLine(new Source("alpha   beta", properties), 0, word.Width + 0.1, new Paragraph(properties, wrapping), null);
            Require(afterWord.Length == 8 && !afterWord.IsTruncated && !afterWord.HasOverflowed && Near(afterWord.Width, word.Width),
                $"word-space-boundary-{wrapping}", "Spaces that exceed the available advance are consumed after a complete fitting word without becoming overflow.");
            using var beforeWord = formatter.FormatLine(new Source("alpha   beta", properties), 0, word.Width - 0.1, new Paragraph(properties, wrapping), null);
            Require(wrapping == TextWrapping.Wrap ? beforeWord.Length < 5 && beforeWord.IsTruncated && !beforeWord.HasOverflowed :
                beforeWord.Length == 8 && !beforeWord.IsTruncated && beforeWord.HasOverflowed,
                $"word-before-space-boundary-{wrapping}", "Below the first word's width, Wrap splits it while WrapWithOverflow retains it and reports overflow.");
            using var trailing = formatter.FormatLine(new Source("alpha   ", properties), 0, word.Width + 0.1, new Paragraph(properties, wrapping), null);
            Require(trailing.Length == 9 && trailing.NewlineLength == 1 && !trailing.HasOverflowed && Near(trailing.Width, word.Width),
                $"fitting-word-trailing-space-{wrapping}", "Trailing spaces and the paragraph terminator stay with the final fitting word.");
        }
    });
    Regression("split-run-crlf-regression", () =>
    {
        var lfProperties = new RunProperties(new Typeface(family), 30, Brushes.Blue);
        var source = new Source(new[] { ("abc\r", properties), ("\ndef", lfProperties) });
        using var first = formatter.FormatLine(source, 0, 1000, paragraph, null);
        using var second = formatter.FormatLine(source, first.Length, 1000, paragraph, null);
        Require(first.Length == 5 && first.NewlineLength == 2 && second.Length == 4 && second.NewlineLength == 1,
            "split-run-crlf-source-positions", "CRLF across differently formatted TextCharacters runs consumes both source positions before the next visible line.");
        Require(first.GetTextRunSpans().Sum(span => span.Length) == first.Length &&
            first.GetTextRunSpans().Any(span => span.Length == 1 && ReferenceEquals(span.Value.Properties, lfProperties)) &&
            second.GetIndexedGlyphRuns().Single().TextSourceCharacterIndex == 5,
            "split-run-crlf-property-span", "The consumed LF keeps its actual run properties and the next line starts after the entire CRLF.");
        var cr = new Source(new[] { ("abc\r", properties), ("def", lfProperties) });
        using var crFirst = formatter.FormatLine(cr, 0, 1000, paragraph, null);
        using var crSecond = formatter.FormatLine(cr, crFirst.Length, 1000, paragraph, null);
        Require(crFirst.Length == 4 && crFirst.NewlineLength == 1 && crSecond.Length == 4,
            "split-run-lone-cr-lookahead", "A CR followed by a non-LF run consumes only CR and preserves the next run's first character.");
        var emptyCrLf = new Source(new[] { ("\r", properties), ("\ndef", lfProperties) });
        using var emptyFirst = formatter.FormatLine(emptyCrLf, 0, 1000, paragraph, null);
        using var emptySecond = formatter.FormatLine(emptyCrLf, emptyFirst.Length, 1000, paragraph, null);
        Require(emptyFirst.Length == 2 && emptyFirst.NewlineLength == 2 && !emptyFirst.GetIndexedGlyphRuns().Any() &&
            emptyFirst.GetTextRunSpans().Sum(span => span.Length) == 2 && emptySecond.GetIndexedGlyphRuns().Single().TextSourceCharacterIndex == 2,
            "split-run-empty-crlf", "A split CRLF also forms one empty line while preserving the following text's absolute source position.");
    });
    Regression("signed-overhang-regression", () =>
    {
        using var normal = formatter.FormatLine(new Source("H", properties), 0, 1000, paragraph, null);
        Rect ink = LineInk(normal);
        Require(ink.Left > 0 && normal.Width > ink.Right && ink.Bottom < normal.Height &&
            Near(normal.OverhangLeading, ink.Left - normal.Start) && Near(normal.OverhangTrailing, normal.Start + normal.Width - ink.Right) &&
            Near(normal.OverhangAfter, ink.Bottom - normal.Height),
            "positive-margins-and-negative-after", "Positive leading/trailing side margins and a negative after margin retain their signed WPF metric values.");
        using var spaced = formatter.FormatLine(new Source("H   ", properties), 0, 1000, paragraph, null);
        Require(Near(spaced.OverhangTrailing, normal.OverhangTrailing) && spaced.WidthIncludingTrailingWhitespace > normal.WidthIncludingTrailingWhitespace,
            "overhang-excludes-trailing-whitespace", "Trailing overhang uses Width rather than WidthIncludingTrailingWhitespace.");
        var italic = new RunProperties(new Typeface(new FontFamily(family), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal));
        using var leading = formatter.FormatLine(new Source("j", italic), 0, 1000, new Paragraph(italic), null);
        Rect leadingInk = LineInk(leading);
        Require(leadingInk.Left < 0 && Near(leading.OverhangLeading, leadingInk.Left - leading.Start),
            "negative-leading-overhang", "Italic ink extending before the line retains a negative leading overhang.");
        using var trailing = formatter.FormatLine(new Source("f", italic), 0, 1000, new Paragraph(italic), null);
        Rect trailingInk = LineInk(trailing);
        Require(trailingInk.Right > trailing.Width && Near(trailing.OverhangTrailing, trailing.Start + trailing.Width - trailingInk.Right),
            "negative-trailing-overhang", "Italic ink extending after the visible advance retains a negative trailing overhang.");
    });
    Require(formatter.GetType().Name == "PortableTextFormatter" && typeof(TextFormatter).Assembly.Location.Contains("portability", StringComparison.OrdinalIgnoreCase),
        "actual-source-formatter", "The public TextFormatter factory selects the source-built portable implementation.");
    Require(properties.Typeface.TryGetGlyphTypeface(out var face) && File.Exists(face.FontUri.LocalPath),
        "real-typeface-resolution", "Actual Typeface resolves a real GlyphTypeface and installed font file.");
    ushort h = face.CharacterToGlyphMap['H'];
    Require(h != 0 && face.AdvanceWidths[h] > 0 && face.CharacterToGlyphMap.IsReadOnly,
        "public-glyph-map-and-advances", "Public GlyphTypeface character maps and nominal advances come from the real font and remain read-only.");

    using (var line = formatter.FormatLine(new Source("NINA exposure", properties), 0, 1000, paragraph, null))
    {
        var runs = line.GetIndexedGlyphRuns().ToArray();
        Require(runs.Length == 1 && runs[0].GlyphRun.GetType() == typeof(GlyphRun) && runs[0].TextSourceLength == 13,
            "real-indexed-glyph-output", "TextSource -> FormatLine -> actual IndexedGlyphRun/GlyphRun output.");
        Require(line.Length == 14 && line.NewlineLength == 1 && line.Baseline > 0 && line.Height > line.Baseline &&
            line.Width > 0 && Near(line.WidthIncludingTrailingWhitespace, runs.Sum(run => run.GlyphRun.AdvanceWidths.Sum())),
            "line-source-and-metrics", "Source length includes the paragraph terminator; line metrics and retained glyph advances agree.");
        Require(!runs[0].GlyphRun.ComputeInkBoundingBox().IsEmpty && line.Extent > 0,
            "portable-glyph-ink-bounds", "The actual GlyphRun obtains nonempty ink bounds without DirectWrite/MIL metrics.");
    }

    using (var line = formatter.FormatLine(new Source("NINA  ", properties), 0, 1000, paragraph, null))
    {
        Require(line.WidthIncludingTrailingWhitespace > line.Width && line.TrailingWhitespaceLength == 3,
            "trailing-space-and-terminator", "Two trailing spaces plus the paragraph terminator have source length but do not increase Width.");
    }

    var larger = new RunProperties(new Typeface(family), 30, Brushes.Blue);
    using (var line = formatter.FormatLine(new Source(new[] { ("small ", properties), ("large", larger) }), 0, 1000, paragraph, null))
    {
        var runs = line.GetIndexedGlyphRuns().ToArray();
        var spans = line.GetTextRunSpans();
        Require(runs.Length == 2 && runs[1].TextSourceCharacterIndex == 6 &&
            ReferenceEquals(spans[0].Value.Properties, properties) && ReferenceEquals(spans[1].Value.Properties, larger) &&
            spans.Sum(span => span.Length) == line.Length && line.Height >= larger.FontRenderingEmSize * runs[1].GlyphRun.GlyphTypeface.Height,
            "multiple-run-properties-and-spans", "Real runs retain their properties, source offsets and full line span lengths at mixed font sizes.");
    }

    using (var line = formatter.FormatLine(new Source("skip q\u0301X", properties), 5, 1000, paragraph, null))
    {
        var glyphs = line.GetIndexedGlyphRuns().Single().GlyphRun;
        Require(glyphs.ClusterMap[0] == 0 && glyphs.ClusterMap[1] == 0 && !glyphs.CaretStops[1],
            "combining-glyph-map", "A non-precomposed combining sequence retains glyph cluster 0 and suppresses the interior caret stop.");
        var next = line.GetNextCaretCharacterHit(new CharacterHit(5, 0));
        var previous = line.GetPreviousCaretCharacterHit(next);
        Require(next.FirstCharacterIndex + next.TrailingLength == 7 && previous == new CharacterHit(5, 0) &&
            line.GetBackspaceCaretCharacterHit(next) == previous,
            "caret-next-previous-backspace", "Both navigation directions and backspace preserve the combining cluster at a nonzero source index.");
        double distance = line.GetDistanceFromCharacterHit(next);
        Require(Near(distance, line.GetDistanceFromCharacterHit(line.GetCharacterHitFromDistance(distance))) &&
            line.GetCharacterHitFromDistance(-100).FirstCharacterIndex == 5 &&
            Near(line.GetDistanceFromCharacterHit(line.GetCharacterHitFromDistance(1e6)), line.WidthIncludingTrailingWhitespace),
            "caret-distance-and-boundaries", "Distance/hit round trips work and clamp on both sides of the actual text run.");
        Require(line.GetTextBounds(5, 2).Single().Rectangle.Width > 0,
            "text-selection-bounds", "A requested combining-cluster source range has an actual nonempty TextBounds rectangle.");
    }

    string unicodeFamily = OperatingSystem.IsWindows() ? "Segoe UI Symbol" : "DejaVu Sans";
    var unicodeProperties = new RunProperties(new Typeface(unicodeFamily));
    using (var line = formatter.FormatLine(new Source("\U0001f600X", unicodeProperties), 0, 1000, new Paragraph(unicodeProperties), null))
    {
        var glyph = line.GetIndexedGlyphRuns().Single().GlyphRun;
        var next = line.GetNextCaretCharacterHit(new CharacterHit(0, 0));
        Require(glyph.Characters.Count == 3 && glyph.ClusterMap[0] == glyph.ClusterMap[1] && !glyph.CaretStops[1] &&
            next.FirstCharacterIndex + next.TrailingLength == 2 && line.GetPreviousCaretCharacterHit(next) == new CharacterHit(0, 0),
            "surrogate-pair-navigation", "A shaped supplementary character consumes two UTF-16 units and cannot be split by navigation.");
    }

    var wrapped = new Paragraph(properties, TextWrapping.Wrap);
    using var reference = formatter.FormatLine(new Source("alpha beta", properties), 0, 1000, paragraph, null);
    var full = new Source("alpha beta gamma delta", properties);
    int offset = 0;
    string reconstructed = "";
    int lines = 0;
    while (offset <= full.Length)
    {
        using var line = formatter.FormatLine(full, offset, reference.Width + 0.1, wrapped, null);
        Require(line.Length > 0 && !line.HasOverflowed, "wrap-line-" + lines,
            "Greedy word wrapping makes source progress while fitting each line's visible width.");
        reconstructed += string.Concat(line.GetIndexedGlyphRuns().Select(run => new string(run.GlyphRun.Characters.ToArray())));
        offset += line.Length;
        lines++;
        if (line.NewlineLength != 0)
            break;
        if (lines > 10) throw new InvalidOperationException("Line breaking did not terminate.");
    }
    Require(lines > 1 && reconstructed == full.Text && offset == full.Length + 1,
        "wrapped-paragraph-source-preservation", "Formatting consecutive lines preserves every source character and consumes the terminator once.");
    using (var first = formatter.FormatLine(full, 0, reference.Width + 0.1, wrapped, null))
    using (var lineBreak = first.GetTextLineBreak())
    using (var clone = lineBreak.Clone())
    {
        lineBreak.Dispose();
        using var next = formatter.FormatLine(full, first.Length, reference.Width + 0.1, wrapped, clone);
        Require(!ReferenceEquals(lineBreak, clone) && next.Length > 0 && first.DependentLength > 0 &&
            next.GetIndexedGlyphRuns().First().TextSourceCharacterIndex == first.Length,
            "managed-break-clone-and-continuation", "Disposing the original TextLineBreak leaves its independent clone usable for continuation of the same actual source; dependent source is reported.");
        clone.Dispose();
        Require(first.Width > 0 && next.Width > 0, "break-disposal-keeps-lines",
            "Disposing both break objects leaves the already formatted source lines valid.");
    }

    using (var line = formatter.FormatLine(new Source("q\u0301X", properties), 0, 0.01, wrapped, null))
        Require(line.Length == 2 && line.IsTruncated, "narrow-wrap-keeps-cluster",
            "A width smaller than the first grapheme still makes progress through the complete combining cluster.");
    using (var line = formatter.FormatLine(new Source("unbreakable", properties), 0, 0.01, new Paragraph(properties, TextWrapping.WrapWithOverflow), null))
        Require(line.Length == 12 && line.HasOverflowed, "wrap-with-overflow-keeps-word",
            "WrapWithOverflow preserves a whole unbreakable word and reports overflow.");

    var crlf = new Source("abc\r\ndef", properties);
    using (var first = formatter.FormatLine(crlf, 0, 1000, paragraph, null))
    using (var second = formatter.FormatLine(crlf, first.Length, 1000, paragraph, null))
        Require(first.Length == 5 && first.NewlineLength == 2 && second.Length == 4 && second.NewlineLength == 1,
            "crlf-and-paragraph-end", "Embedded CRLF consumes two source positions before the next line and paragraph terminator.");
    using (var empty = formatter.FormatLine(new Source("", properties), 0, 1000, paragraph, null))
        Require(empty.Length == 1 && empty.Width == 0 && empty.Height > 0 && !empty.GetIndexedGlyphRuns().Any(),
            "empty-paragraph-metrics", "An empty paragraph keeps its terminator and font-based line metrics without a fabricated glyph.");

    var minmax = formatter.FormatMinMaxParagraphWidth(new Source("WWW i", properties), 0, paragraph);
    using (var maximum = formatter.FormatLine(new Source("WWW i", properties), 0, 1000, paragraph, null))
        Require(minmax.MinWidth > 0 && minmax.MaxWidth > minmax.MinWidth && Near(minmax.MaxWidth, maximum.Width),
            "paragraph-minmax-width", "Minimum word width and unwrapped paragraph width are meaningful measured values.");

    var dpiSource = new Source("DPI 123", properties) { PixelsPerDip = 1.5 };
    using (var displayFormatter = TextFormatter.Create(TextFormattingMode.Display))
    using (var display = displayFormatter.FormatLine(dpiSource, 0, 1000, paragraph, null))
        Require(display.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.AdvanceWidths).All(value => Near(value * 1.5, Math.Round(value * 1.5))),
            "display-advance-rounding", "Display-mode glyph advances align to physical pixels at a supplied 1.5 pixels-per-DIP.");

    var visual = new DrawingVisual();
    var textBrush = new SolidColorBrush(Colors.Red);
    var drawProperties = new RunProperties(new Typeface(family), foreground: textBrush);
    GlyphRun retained;
    using (var drawFormatter = TextFormatter.Create())
    using (var line = drawFormatter.FormatLine(new Source("Retained text", drawProperties), 0, 1000, new Paragraph(drawProperties), null))
    {
        retained = line.GetIndexedGlyphRuns().Single().GlyphRun;
        using (var context = visual.RenderOpen())
            line.Draw(context, new Point(10, 20), InvertAxes.None);
    }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var drawn = FindGlyphs(visual.Drawing).ToArray();
    Require(drawn.Length == 1 && ReferenceEquals(drawn[0].GlyphRun, retained) && !retained.ComputeInkBoundingBox().IsEmpty,
        "real-drawingcontext-and-retained-font", "TextLine.Draw records the actual GlyphRun and its font survives line/formatter disposal plus collection.");
    using (var target = new Target(visual, 320, 100))
    {
        Drain();
        using (var pixels = target.Snapshot())
        {
            var ink = InkBounds(pixels);
            Rect expected = retained.ComputeInkBoundingBox();
            expected.Offset(retained.BaselineOrigin.X + 10, retained.BaselineOrigin.Y + 20);
            Require(ink.Right > ink.Left && ink.Bottom > ink.Top && Math.Abs(ink.Left - expected.Left) <= 2 && Math.Abs(ink.Top - expected.Top) <= 2 &&
                Math.Abs(ink.Right - expected.Right) <= 2 && Math.Abs(ink.Bottom - expected.Bottom) <= 2,
                "actual-formatted-text-retained-pixels", "TextSource -> TextFormatter -> TextLine.Draw -> DrawingVisual -> retained replay produces ink at the measured bounds.");
            string imageDirectory = Path.Combine("artifacts", "portability", "text-integration", OperatingSystem.IsWindows() ? "windows" : "linux");
            Directory.CreateDirectory(imageDirectory);
            using var image = SKImage.FromBitmap(pixels);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(imageDirectory, "formatted-text.png"));
            data.SaveTo(stream);
        }
        textBrush.Color = Colors.Blue;
        Drain();
        using var changed = target.Snapshot();
        int count = 0;
        for (int y = 0; y < changed.Height; y++)
            for (int x = 0; x < changed.Width; x++)
            {
                SKColor color = changed.GetPixel(x, y);
                if (color.Alpha != 0)
                {
                    if (color.Red > 1 || color.Blue < 254) throw new InvalidOperationException("Formatted text retained stale brush pixels.");
                    count++;
                }
            }
        Require(count > 0, "formatted-text-brush-invalidation", "Mutating the retained text brush schedules a real dispatcher render and replaces the ink color.");
    }

    Expect<NotSupportedException>(() => formatter.FormatLine(new Source("a\tb", properties), 0, 1000, paragraph, null), "reject-tab-layout");
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source("soft\u00adhyphen", properties), 0, 1000, paragraph, null), "reject-discretionary-hyphen-insertion");
    using (var hebrew = formatter.FormatLine(new Source("\u05d0", properties), 0, 1000, paragraph, null))
        Require(hebrew.GetIndexedGlyphRuns().Single().GlyphRun.BidiLevel == 1, "supported-hebrew-bidi", "Hebrew produces a genuine RTL glyph run.");
    var fixedProperties = BidiTests.Properties("DejaVuSans.ttf", "DejaVu Sans");
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source("\U0001e900", fixedProperties), 0, 1000, new Paragraph(fixedProperties), null), "reject-uncovered-supplementary-glyph");
    using (var scripts = formatter.FormatLine(new Source("latin\u03b1", properties), 0, 1000, paragraph, null))
        Require(scripts.GetIndexedGlyphRuns().Count() == 2, "supported-mixed-script-itemization", "Latin and Greek are itemized into separately shaped WPF glyph runs.");
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source(new[] { ("q", properties), ("\u0301", larger) }), 0, 1000, paragraph, null), "reject-formatting-inside-grapheme");
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source("x", properties), 0, 1000, new Paragraph(properties, alignment: TextAlignment.Justify), null), "reject-justification");
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source("x", properties), 0, 1000, new Paragraph(properties, alignment: (TextAlignment)99), null), "reject-invalid-alignment");
    foreach (var alignment in new[] { TextAlignment.Center, TextAlignment.Right })
        Expect<ArgumentOutOfRangeException>(() => formatter.FormatLine(new Source("x", properties), 0, double.PositiveInfinity, new Paragraph(properties, alignment: alignment), null), "reject-infinite-alignment-width-" + alignment);
    Expect<NotSupportedException>(() => formatter.FormatLine(new Source(char.ConvertFromUtf32(0x10ffff), properties), 0, 1000, paragraph, null), "reject-missing-font-glyph");
    Expect<ArgumentOutOfRangeException>(() => formatter.FormatLine(new Source("x", properties), 0, double.NaN, paragraph, null), "reject-invalid-width");
    Expect<ArgumentOutOfRangeException>(() => formatter.FormatLine(new Source("", properties), int.MaxValue, 1000, paragraph, null), "reject-source-position-overflow");
    Expect<NotSupportedException>(() => _ = face.FamilyNames, "reject-unported-font-metadata");
    Expect<NotSupportedException>(() => _ = face.AdvanceHeights[h], "reject-unported-vertical-metrics");
    var disposed = TextFormatter.Create(); disposed.Dispose();
    Expect<ObjectDisposedException>(() => disposed.FormatLine(new Source("x", properties), 0, 1000, paragraph, null), "disposed-formatter");
    Regression("managed-typography-construction", () =>
    {
        var typography = new TestTypography();
        typography.Invalidate();
        Require(typography.StandardLigatures && typography.Kerning && typography.Variants == FontVariants.Normal,
            "typography-base-construction-and-invalidation", "An actual TextRunTypographyProperties subclass constructs and invalidates its cache without loading native feature storage.");
    });
    Regression("actual-span-vector-construction", () =>
    {
        Type vectorType = typeof(FormattedText).Assembly.GetType("MS.Internal.SpanVector", throwOnError: true)!;
        object vector = Activator.CreateInstance(vectorType, [null])!;
        vectorType.GetMethod("SetReference", [typeof(int), typeof(int), typeof(object)])!.Invoke(vector, [0, 3, "abc"]);
        Require((int)vectorType.GetProperty("Count")!.GetValue(vector)! == 1,
            "managed-span-vector-source-container", "The actual source SpanVector constructs and stores a range without loading the C++/CLI span container.");
    });
    Regression("public-formatted-text", () =>
    {
        const string text = "NINA exposure";
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            new Typeface(family), 20, Brushes.Green, 1) { Trimming = TextTrimming.None };
        double original = formatted.Width;
        formatted.SetFontSize(30, 5, 8);
        Require(formatted.Width > original && formatted.Height > 20,
            "formatted-text-range-split", "Public FormattedText range formatting splits the actual span vector and updates measured metrics.");
        formatted.SetFontSize(20, 5, 8);
        Require(Near(formatted.Width, original), "formatted-text-range-restore",
            "Restoring the range's font size rejoins equivalent formatting and restores the original measurement.");
        formatted.SetForegroundBrush(Brushes.Blue, 5, 8);
        var formattedVisual = new DrawingVisual();
        using (var context = formattedVisual.RenderOpen())
            context.DrawText(formatted, new Point(8, 10));
        var output = FindGlyphs(formattedVisual.Drawing).ToArray();
        Require(output.Length == 2 && output.SelectMany(run => run.GlyphRun.Characters).SequenceEqual(text) &&
            ReferenceEquals(output[1].ForegroundBrush, Brushes.Blue), "formatted-text-retained-glyphs",
            "DrawingContext.DrawText records actual GlyphRuns for both styled source ranges.");
        using var formattedTarget = new Target(formattedVisual, 320, 100);
        Drain();
        using var bitmap = formattedTarget.Snapshot();
        var bounds = InkBounds(bitmap);
        Require(bounds.Right > bounds.Left && bounds.Bottom > bounds.Top, "formatted-text-retained-pixels",
            "Public FormattedText construction, measurement and drawing reach real retained software pixels.");
    });
    Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is "DirectWriteForwarder" or "System.Windows.Input.Manipulations"),
        "no-windows-interop-loads", "All exercised text paths complete without loading Windows-only interop assemblies.");
    Require(!System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Any(module =>
        module.ModuleName.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase) || module.ModuleName.Contains("PresentationNative", StringComparison.OrdinalIgnoreCase)),
        "no-windows-wpf-native-modules", "Actual formatter, drawing and glyph-geometry paths complete without loading Windows WPF graphics DLLs on either operating system.");
}
catch (Exception exception)
{
    checks.Add(new { name = "runtime", status = "fail", detail = exception.ToString() });
    exit = 1;
}
Console.WriteLine(JsonSerializer.Serialize(new { probe = "actual-wpf-text-integration", os = Environment.OSVersion.Platform.ToString(),
    scope = "Source-built WPF TextFormatter/TextLine/Typeface/GlyphTypeface/GlyphRun and retained drawing; horizontal Unicode17 LTR/RTL paragraphs with an explicitly bounded feature profile.",
    frameworkAssembly = new { name = typeof(TextFormatter).Assembly.FullName, path = typeof(TextFormatter).Assembly.Location },
    loadedWindowsInteropAssemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name is "DirectWriteForwarder" or "System.Windows.Input.Manipulations")
        .Select(assembly => new { name = assembly.FullName, path = assembly.Location }).ToArray(),
    windowsWpfModuleMappings = OperatingSystem.IsLinux() ? File.ReadAllLines("/proc/self/maps")
        .Where(line => line.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase) || line.Contains("PresentationNative", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("DirectWriteForwarder", StringComparison.OrdinalIgnoreCase)).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()).Distinct().ToArray() : Array.Empty<string>(),
    checks }, new JsonSerializerOptions { WriteIndented = true }));
return exit;

void Require(bool condition, string name, string detail)
{
    if (!condition) throw new InvalidOperationException(name + ": " + detail);
    checks.Add(new { name, status = "pass", detail });
}
void Regression(string name, Action check)
{
    try { check(); }
    catch (Exception exception) { checks.Add(new { name, status = "fail", detail = exception.ToString() }); exit = 1; }
}
Rect LineInk(TextLine line)
{
    Rect ink = Rect.Empty;
    foreach (var indexed in line.GetIndexedGlyphRuns())
    {
        Rect bounds = indexed.GlyphRun.ComputeInkBoundingBox();
        if (!bounds.IsEmpty)
        {
            bounds.Offset(indexed.GlyphRun.BaselineOrigin.X, indexed.GlyphRun.BaselineOrigin.Y);
            ink.Union(bounds);
        }
    }
    return ink;
}
void Expect<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { checks.Add(new { name, status = "pass", detail = "The real API rejected the request with " + typeof(T).Name + "." }); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
bool Near(double left, double right) => Math.Abs(left - right) < 0.001;
void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}
(int Left, int Top, int Right, int Bottom) InkBounds(SKBitmap bitmap)
{
    int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
    for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Alpha != 0)
            {
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
    return (left, top, right, bottom);
}
IEnumerable<GlyphRunDrawing> FindGlyphs(Drawing drawing)
{
    if (drawing is GlyphRunDrawing glyph) yield return glyph;
    if (drawing is DrawingGroup group)
        foreach (var child in group.Children)
            foreach (var result in FindGlyphs(child)) yield return result;
}

sealed class RunProperties(Typeface typeface, double size = 20, Brush? foreground = null, NumberSubstitution? substitution = null,
    CultureInfo? culture = null, TextRunTypographyProperties? typography = null) : TextRunProperties
{
    public override Typeface Typeface => typeface;
    public override double FontRenderingEmSize => size;
    public override double FontHintingEmSize => size;
    public override TextDecorationCollection TextDecorations => null!;
    public override Brush ForegroundBrush => foreground ?? Brushes.Black;
    public override Brush BackgroundBrush => null!;
    public override CultureInfo CultureInfo => culture ?? CultureInfo.GetCultureInfo("en-US");
    public override NumberSubstitution NumberSubstitution => substitution!;
    public override TextRunTypographyProperties TypographyProperties => typography!;
    public override TextEffectCollection TextEffects => null!;
}
sealed class Paragraph(RunProperties properties, TextWrapping wrapping = TextWrapping.NoWrap, TextAlignment alignment = TextAlignment.Left, FlowDirection flow = FlowDirection.LeftToRight) : TextParagraphProperties
{
    public override FlowDirection FlowDirection => flow;
    public override TextAlignment TextAlignment => alignment;
    public override double LineHeight => 0;
    public override bool FirstLineInParagraph => true;
    public override TextRunProperties DefaultTextRunProperties => properties;
    public override TextWrapping TextWrapping => wrapping;
    public override TextMarkerProperties TextMarkerProperties => null!;
    public override double Indent => 0;
}
sealed class Source : TextSource
{
    private readonly (string Text, RunProperties Properties)[] _segments;
    private readonly int _chunkSize;
    public string Text { get; }
    public int Length => Text.Length;
    public Source(string text, RunProperties properties) : this(new[] { (text, properties) }) { }
    public Source(string text, RunProperties properties, int chunkSize) : this(text, properties) { _chunkSize = chunkSize; }
    public Source((string Text, RunProperties Properties)[] segments)
    {
        _segments = segments;
        Text = string.Concat(segments.Select(segment => segment.Text));
    }
    public override TextRun GetTextRun(int index)
    {
        int start = 0;
        foreach (var segment in _segments)
        {
            if (index < start + segment.Text.Length)
                return new TextCharacters(segment.Text, index - start,
                    Math.Min(segment.Text.Length - (index - start), _chunkSize == 0 ? int.MaxValue : _chunkSize), segment.Properties);
            start += segment.Text.Length;
        }
        return new TextEndOfParagraph(1);
    }
    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
        new(limit, new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), new CharacterBufferRange(Text, 0, Math.Min(limit, Text.Length))));
    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
}

sealed class Target : IDisposable
{
    private readonly Type _type = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget", throwOnError: true)!;
    private readonly object _instance;
    public Target(Visual visual, int width, int height, DpiScale? dpi = null)
    {
        object[] arguments = dpi.HasValue ? [visual, width, height, dpi.Value] : [visual, width, height];
        _instance = Activator.CreateInstance(_type, BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)!;
    }
    public SKBitmap Snapshot() => (SKBitmap)_type.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_instance, null)!;
    public void Dispose() => ((IDisposable)_instance).Dispose();
}
