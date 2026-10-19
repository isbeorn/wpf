using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

static class HiddenRunCases
{
    internal static object Capture()
    {
        var properties = new Properties();
        using var formatter = TextFormatter.Create();
        return new[] { new object[] { 2, "abc", 3 }, new object[] { "a", 2, "b" }, new object[] { 4 },
            new object[] { "f", 1, "i" }, new object[] { "a", 1, "\u0301" },
            new object[] { 1, "abc \u05d0\u05d1\u05d2", 1 }, new object[] { "\u0633", 1, "\u0644\u0627\u0645" } }
            .Select((parts, number) => {
                var source = new RunSource(parts, properties);
                using var line = formatter.FormatLine(source, 0, 1000, new Paragraph(properties), null!);
                return new { number, line.Length, line.NewlineLength, line.Width, line.Extent,
                    spans = line.GetTextRunSpans().Select(span => new { span.Length, type = span.Value.GetType().Name }).ToArray(),
                    glyphs = line.GetIndexedGlyphRuns().Select(run => new { run.TextSourceCharacterIndex, run.TextSourceLength,
                        text = new string(run.GlyphRun.Characters.ToArray()), glyphs = run.GlyphRun.GlyphIndices, run.GlyphRun.BaselineOrigin }).ToArray(),
                    distances = Enumerable.Range(0, source.Length + 1).Select(index => new { index,
                        distance = line.GetDistanceFromCharacterHit(new CharacterHit(index, 0)),
                        next = Position(line.GetNextCaretCharacterHit(new CharacterHit(index, 0))),
                        previous = Position(line.GetPreviousCaretCharacterHit(new CharacterHit(index, 0))) }).ToArray() };
            }).ToArray();
    }
    internal static void Verify(TextFormatter formatter, Action<bool, string, string> require)
    {
        var properties = new Properties();
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var paragraph = new Paragraph(properties, flow: flow);
            var source = new RunSource(new object[] { 2, "abc", 3 }, properties);
            using var line = formatter.FormatLine(source, 0, 1000, paragraph, null!);
            using var plain = formatter.FormatLine(new RunSource(new object[] { "abc" }, properties), 0, 1000, paragraph, null!);
            var spans = line.GetTextRunSpans();
            var glyphs = line.GetIndexedGlyphRuns().ToArray();
            require(line.Length == 9 && line.NewlineLength == 1 && spans.Select(span => span.Length).SequenceEqual(new[] { 2, 3, 3, 1 }) &&
                spans[0].Value is TextHidden && spans[2].Value is TextHidden && glyphs.Length == 1 &&
                glyphs[0].TextSourceCharacterIndex == 2 && glyphs[0].TextSourceLength == 3,
                "hidden-source-spans-" + flow, "Hidden element edges consume exact source symbols and remain genuine run spans without entering glyph output.");
            require(Near(line.Width, plain.Width) && Near(line.Extent, plain.Extent) &&
                Near(line.GetDistanceFromCharacterHit(new CharacterHit(0, 0)), line.GetDistanceFromCharacterHit(new CharacterHit(2, 0))) &&
                Near(line.GetDistanceFromCharacterHit(new CharacterHit(5, 0)), line.GetDistanceFromCharacterHit(new CharacterHit(8, 0))) &&
                line.GetTextBounds(0, 2).All(bounds => bounds.Rectangle.Width == 0) &&
                line.GetTextBounds(5, 3).All(bounds => bounds.Rectangle.Width == 0),
                "hidden-zero-geometry-" + flow, "Hidden edges add no advance or ink and preserve both ends of source-indexed zero-width range geometry.");
            require(Position(line.GetNextCaretCharacterHit(new CharacterHit(0, 0))) == 3 &&
                Position(line.GetPreviousCaretCharacterHit(new CharacterHit(3, 0))) == 2 &&
                Position(line.GetPreviousCaretCharacterHit(new CharacterHit(8, 0))) == 4,
                "hidden-caret-both-directions-" + flow, "Forward and reverse navigation cross hidden edges using source positions, without creating caret stops inside them.");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) line.Draw(drawing, new Point(), InvertAxes.None);
            double hitDistance = (line.GetDistanceFromCharacterHit(new CharacterHit(3, 0)) + line.GetDistanceFromCharacterHit(new CharacterHit(4, 0))) / 2;
            var hit = line.GetCharacterHitFromDistance(hitDistance);
            require(hit.FirstCharacterIndex == 3, "hidden-hit-source-index-" + flow, "Distance hit testing returns the original source index inside visible ink after hidden edges.");
            require(visual.Drawing.Children.Count != 0 && line.GetTextBounds(2, 3).Any(bounds => bounds.Rectangle.Width > 0),
                "hidden-visible-content-draws-" + flow, "Real retained glyph drawing and visible selection geometry survive hidden element edges.");
            using var bidi = formatter.FormatLine(new RunSource(new object[] { 1, "abc \u05d0\u05d1\u05d2", 1 }, properties), 0, 1000, paragraph, null!);
            using var plainBidi = formatter.FormatLine(new RunSource(new object[] { "abc \u05d0\u05d1\u05d2" }, properties), 0, 1000, paragraph, null!);
            require(Near(bidi.Width, plainBidi.Width) && bidi.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)
                .SequenceEqual(plainBidi.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)) &&
                bidi.GetIndexedGlyphRuns().Select(run => run.GlyphRun.BidiLevel).SequenceEqual(plainBidi.GetIndexedGlyphRuns().Select(run => run.GlyphRun.BidiLevel)),
                "hidden-bidi-source-mapping-" + flow, "Hidden symbols preserve visible Unicode shaping and bidi ordering without entering the Unicode character buffer.");
        }
        using (var hidden = formatter.FormatLine(new RunSource(new object[] { 2, 3 }, properties), 0, 1000, new Paragraph(properties), null!))
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) hidden.Draw(drawing, new Point(), InvertAxes.None);
            require(hidden.Length == 6 && hidden.Width == 0 && hidden.Extent == 0 && !hidden.GetIndexedGlyphRuns().Any() &&
                VisualTreeHelper.GetContentBounds(visual).IsEmpty && hidden.GetTextRunSpans().Select(span => span.Length).SequenceEqual(new[] { 2, 3, 1 }) &&
                hidden.GetTextBounds(0, 5).Single().Rectangle.Width == 0 && hidden.GetTextBounds(0, 5).Single().Rectangle.Height > 0,
                "hidden-only-retained-line", "Consecutive hidden runs retain source length and empty drawing with ordinary caret height.");
        }
        using (var interior = formatter.FormatLine(new RunSource(new object[] { "a", 2, "b" }, properties), 0, 1000, new Paragraph(properties), null!))
            require(Position(interior.GetNextCaretCharacterHit(new CharacterHit(1, 0))) == 4 &&
                Position(interior.GetPreviousCaretCharacterHit(new CharacterHit(3, 0))) == 0 &&
                Near(interior.GetDistanceFromCharacterHit(new CharacterHit(1, 0)), interior.GetDistanceFromCharacterHit(new CharacterHit(3, 0))) &&
                interior.GetTextBounds(1, 2).All(bounds => bounds.Rectangle.Width == 0),
                "hidden-interior-caret-gap", "Both directions cross an interior hidden range without manufacturing width or visible characters.");
        using (var unicode = formatter.FormatLine(new RunSource(new object[] { 1, "A\U0001F600e\u0301", 1 }, properties), 0, 1000, new Paragraph(properties), null!))
        {
            int firstStop = Position(unicode.GetNextCaretCharacterHit(new CharacterHit(1, 0)));
            int secondStop = Position(unicode.GetNextCaretCharacterHit(new CharacterHit(firstStop, 0)));
            int thirdStop = Position(unicode.GetNextCaretCharacterHit(new CharacterHit(secondStop, 0)));
            require(firstStop == 2 && secondStop == 4 && thirdStop == 6 &&
                Position(unicode.GetPreviousCaretCharacterHit(new CharacterHit(6, 0))) == 4 && unicode.Length == 8,
                "hidden-unicode-grapheme-positions", "Hidden edges retain UTF-16 source positions around supplementary characters and combining clusters in both caret directions.");
        }
        var wrappingSource = new RunSource(new object[] { 1, "ab cd", 1 }, properties);
        var wrapping = new Paragraph(properties, TextWrapping.Wrap);
        using var measure = formatter.FormatLine(new RunSource(new object[] { "ab " }, properties), 0, 1000, wrapping, null!);
        int first = 0, lines = 0, totalSpans = 0;
        TextLineBreak? previous = null;
        var visible = new List<char>();
        try
        {
            do
            {
                using var line = formatter.FormatLine(wrappingSource, first, measure.WidthIncludingTrailingWhitespace + 0.1, wrapping, previous!);
                previous?.Dispose();
                previous = line.GetTextLineBreak();
                if (line.Length <= 0 || ++lines > 10) throw new InvalidOperationException("Hidden wrapping did not make source progress.");
                totalSpans += line.GetTextRunSpans().Sum(span => span.Length);
                visible.AddRange(line.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters));
                first += line.Length;
            } while (previous != null);
        }
        finally { previous?.Dispose(); }
        require(lines == 2 && first == wrappingSource.Length + 1 && totalSpans == first && new string(visible.ToArray()) == "ab cd",
            "hidden-wrapped-source-continuation", "Real line continuations consume source symbols exactly once and preserve visible characters across wrapping.");
    }
    internal static void VerifySourceSuffix(TextFormatter formatter, Action<bool, string, string> require)
    {
        var properties = new Properties();
        using (var interior = formatter.FormatLine(new RunSource(new object[] { "a", 2, "b" }, properties), 0, 1000, new Paragraph(properties), null!))
        {
            var glyph = interior.GetIndexedGlyphRuns().Single();
            var visible = interior.GetTextRunSpans().Where(span => span.Value is TextCharacters).Select(span => span.Value).ToArray();
            var left = interior.GetTextBounds(0, 1).Single().TextRunBounds!.Single();
            var right = interior.GetTextBounds(3, 1).Single().TextRunBounds!.Single();
            require(glyph.TextSourceCharacterIndex == 0 && glyph.TextSourceLength == 4 && new string(glyph.GlyphRun.Characters.ToArray()) == "ab" &&
                left.TextSourceCharacterIndex == 0 && left.Length == 1 && ReferenceEquals(left.TextRun, visible[0]) &&
                right.TextSourceCharacterIndex == 3 && right.Length == 1 && ReferenceEquals(right.TextRun, visible[1]),
                "hidden-interior-indexed-and-original-range-mapping", "Compatible shaping may span hidden source symbols: IndexedGlyphRun permits a source length different from its character count; per-run bounds retain original source indices and identities.");
        }
        var cases = new[] { (new object[] { "a ", 2 }, 4), (new object[] { "a", 2, " " }, 2),
            (new object[] { 4 }, 5), (new object[] { 2, " ", 3 }, 5), (new object[] { 2, "  " }, 3),
            (new object[] { " ", 2 }, 4), (new object[] { 2, " ", 2, " " }, 5), (new object[] { "abc", 3 }, 1) };
        for (int index = 0; index < cases.Length; index++)
        {
            using var line = formatter.FormatLine(new RunSource(cases[index].Item1, properties), 0, 1000, new Paragraph(properties), null!);
            require(line.TrailingWhitespaceLength == cases[index].Item2, "hidden-source-whitespace-suffix-" + index,
                "TrailingWhitespaceLength consumes the measured stock source suffix, including only hidden positions after the first trailing visible whitespace, or the entire hidden-only line.");
        }
    }
    private static bool Near(double left, double right) => Math.Abs(left - right) < 0.001;
    private static int Position(CharacterHit hit) => hit.FirstCharacterIndex + hit.TrailingLength;
    internal sealed class Properties : TextRunProperties
    {
        public override Typeface Typeface => new(new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public override double FontRenderingEmSize => 24;
        public override double FontHintingEmSize => 24;
        public override TextDecorationCollection TextDecorations => null!;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush BackgroundBrush => null!;
        public override CultureInfo CultureInfo => CultureInfo.GetCultureInfo("en-US");
        public override TextEffectCollection TextEffects => null!;
    }
    internal sealed class Paragraph(Properties properties, TextWrapping wrapping = TextWrapping.NoWrap, FlowDirection flow = FlowDirection.LeftToRight) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => flow;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => wrapping;
        public override TextMarkerProperties TextMarkerProperties => null!;
        public override double Indent => 0;
    }
    internal sealed class RunSource(object[] parts, Properties properties) : TextSource
    {
        public int Length => parts.Sum(part => part is int count ? count : ((string)part).Length);
        public override TextRun GetTextRun(int index)
        {
            foreach (object part in parts)
            {
                int length = part is int count ? count : ((string)part).Length;
                if (index < length) return part is int ? new TextHidden(length - index) : new TextCharacters((string)part, index, length - index, properties);
                index -= length;
            }
            return new TextEndOfParagraph(1);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new(0, new CultureSpecificCharacterBufferRange(properties.CultureInfo, new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
}
