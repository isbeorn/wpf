using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

static class ContinuationCacheCases
{
    internal static void Verify(TextFormatter formatter, Action<bool, string, string> require)
    {
        var properties = BidiTests.Properties("DejaVuSans.ttf", "DejaVu Sans", 20);
        var paragraph = new Paragraph(properties, TextWrapping.Wrap);
        string text = string.Concat(Enumerable.Repeat("hello ", 30));
        var original = new CountingSource(new Source(text, properties));
        var cache = new TextRunCache();
        var measured = new List<(int First, int Length, TextLineBreak? Before)>();
        TextLineBreak? previous = null;
        int first = 0;
        try
        {
            while (first < text.Length)
            {
                using var line = formatter.FormatLine(original, first, 100, paragraph, previous!, cache);
                measured.Add((first, line.Length, previous?.Clone()));
                previous?.Dispose();
                previous = line.GetTextLineBreak();
                first += line.Length;
            }
            var blue = new RunProperties(properties.Typeface, 20, Brushes.Blue);
            var refreshed = new CountingSource(new Source(text, blue));
            var characters = new List<char>();
            bool currentPaint = true;
            foreach (var item in measured)
            {
                using var line = formatter.FormatLine(refreshed, item.First, 100, paragraph, item.Before!, cache);
                characters.AddRange(line.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters));
                currentPaint &= Paint(line, Brushes.Blue);
            }
            require(measured.Count == 30 && first == text.Length + 1 && original.Queries == 2 && refreshed.Queries == 2 &&
                new string(characters.ToArray()) == text && currentPaint,
                "cache-recreated-wrapper-refreshes-once", "Old measured continuations use the new wrapper's complete current paragraph and paint with exactly one text/terminator fetch, not a full reread per line.");
            var record = measured[1];
            var supplied = new TextRunCache();
            original.Inner = new Source(text, blue);
            original.Queries = 0;
            using (var line = formatter.FormatLine(original, record.First, 100, paragraph, record.Before!, supplied))
                require(original.Queries == 2 && Paint(line, Brushes.Blue), "cache-new-slot-refetches-identical-source",
                    "A newly supplied cache cannot reuse the older snapshot even when the TextSource wrapper identity is unchanged.");
            original.Inner = new Source(text, new RunProperties(properties.Typeface, 20, Brushes.Green));
            original.Queries = 0;
            supplied.Invalidate();
            using (var line = formatter.FormatLine(original, record.First, 100, paragraph, record.Before!, supplied))
                require(original.Queries == 2 && Paint(line, Brushes.Green), "cache-invalidate-refetches-current-properties",
                    "Client Invalidate refetches the same wrapper's current properties instead of the retained old line-break snapshot.");
            original.Inner = new Source(text, new RunProperties(properties.Typeface, 20, Brushes.Red));
            original.Queries = 0;
            supplied.Change(record.First, 1, 1);
            using (var line = formatter.FormatLine(original, record.First, 100, paragraph, record.Before!, supplied))
                require(original.Queries == 2 && Paint(line, Brushes.Red), "cache-change-refetches-current-properties",
                    "Client Change clears the portable paragraph through the existing cache contract and updates actual retained paint.");
            foreach (int invalidFirst in new[] { record.First - 1, record.First + 1 })
            {
                original.Queries = 0;
                bool rejected = false;
                try { using var line = formatter.FormatLine(original, invalidFirst, 100, paragraph, record.Before!, supplied); }
                catch (ArgumentException exception) { rejected = exception.ParamName == "previous"; }
                require(rejected && original.Queries == 0, "cache-next-position-mismatch-" + invalidFirst,
                    "Both sides of the expected Next position are rejected before a populated cache can bypass continuation validation.");
            }
            original.Queries = 0;
            bool wrongDirection = false;
            try { using var line = formatter.FormatLine(original, record.First, 100, new Paragraph(properties, TextWrapping.Wrap, flow: FlowDirection.RightToLeft), record.Before!, supplied); }
            catch (ArgumentException exception) { wrongDirection = exception.ParamName == "previous"; }
            using var disposed = record.Before!.Clone();
            disposed.Dispose();
            bool disposedRejected = false;
            try { using var line = formatter.FormatLine(original, record.First, 100, paragraph, disposed, supplied); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            require(wrongDirection && disposedRejected && original.Queries == 0, "cache-direction-and-disposal-before-lookup",
                "Current direction and disposed continuation checks remain authoritative even with a valid current cache entry.");
            var unrelated = new CountingSource(new Source("current word", properties));
            using (var line = formatter.FormatLine(unrelated, record.First, 100, paragraph, record.Before!, supplied))
                require(new string(line.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters).ToArray()) == "current word"[record.First..] && unrelated.Queries == 2,
                    "cache-other-wrapper-uses-current-content", "Another source wrapper is read from the original paragraph start and never receives the previous source's cached characters.");
            foreach (string changed in new[] { "x", "abcdeq\u0301Z" })
            {
                original.Inner = new Source(changed, properties);
                original.Queries = 0;
                supplied.Invalidate();
                bool rejected = false;
                try { using var line = formatter.FormatLine(original, record.First, 100, paragraph, record.Before!, supplied); }
                catch (ArgumentException exception) { rejected = exception.ParamName == "previous" && exception.Message.Contains("grapheme boundary", StringComparison.Ordinal); }
                require(rejected && original.Queries == 2, "cache-current-boundary-reject-" + (changed.Length == 1 ? "past-end" : "inside-grapheme"),
                    "A refetched current source rejects a retained Next that no longer exists or splits a new grapheme, rather than flooring the source position.");
            }
            original.Inner = new Source(text, properties);
            supplied.Invalidate();
            using (var recovered = formatter.FormatLine(original, record.First, 100, paragraph, record.Before!, supplied))
                require(Paint(recovered, Brushes.Black) && recovered.Length > 0, "cache-boundary-failure-recovery",
                    "A rejected changed-source position does not publish a corrupt paragraph and permits a valid caller restoration.");
            VerifyInheritedScope(formatter, properties, require);
        }
        finally
        {
            previous?.Dispose();
            foreach (var item in measured) item.Before?.Dispose();
        }
    }
    private static void VerifyInheritedScope(TextFormatter formatter, RunProperties properties, Action<bool, string, string> require)
    {
        var paragraph = new Paragraph(properties, TextWrapping.Wrap);
        using var reference = formatter.FormatLine(new Source("\u05d0\u05d1\u05d2", properties), 0, 1000, paragraph, null!);
        double width = reference.Width + 1;
        var original = new ScopedSource(properties);
        var cache = new TextRunCache();
        using var first = formatter.FormatLine(original, 0, width, paragraph, null!, cache);
        using var soft = first.GetTextLineBreak();
        using var hebrew = formatter.FormatLine(original, first.Length, width, paragraph, soft, cache);
        using var continuation = hebrew.GetTextLineBreak();
        var replacement = new ScopedSource(properties);
        using var digits = formatter.FormatLine(replacement, first.Length + hebrew.Length, width, paragraph, continuation, new TextRunCache());
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) digits.Draw(context, new Point(), InvertAxes.None);
        require(first.Length == 3 && hebrew.Length == 4 && digits.GetIndexedGlyphRuns().Any(run => run.GlyphRun.BidiLevel == 2 && run.GlyphRun.Characters.Any(char.IsDigit)) &&
            Drawings(visual.Drawing).OfType<GeometryDrawing>().Any(), "cache-refetch-keeps-full-bidi-and-initial-scope",
            "Refreshing a wrapper after a soft break reanalyzes the complete Hebrew prefix and retains the modifier inherited at that paragraph start.");
        using var plain = formatter.FormatLine(original, first.Length, width, paragraph, null!, cache);
        using var plainContinuation = plain.GetTextLineBreak();
        using var resumedUnderline = formatter.FormatLine(original, first.Length + hebrew.Length, width, paragraph, continuation, cache);
        bool underlineRestored = HasDecoration(resumedUnderline);
        using var resumedPlain = formatter.FormatLine(original, first.Length + plain.Length, width, paragraph, plainContinuation, cache);
        require(plain.Length == hebrew.Length && HasDecoration(hebrew) && !HasDecoration(plain),
            "cache-scope-interleave-distinct-start-contexts", "The same immutable source and paragraph start can be formatted with inherited underline or no initial modifier scope.");
        require(underlineRestored, "cache-scope-interleave-restores-inherited-decoration",
            "A saved underlined continuation cannot reuse a plain snapshot placed in the same client cache by an independent formatting request.");
        require(!HasDecoration(resumedPlain), "cache-scope-interleave-restores-plain-decoration",
            "The reverse interleaving cannot reuse an underlined snapshot for a saved plain continuation.");
    }
    private static bool HasDecoration(TextLine line)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
        return Drawings(visual.Drawing).OfType<GeometryDrawing>().Any();
    }
    private static bool Paint(TextLine line, Brush brush)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
        var glyphs = Drawings(visual.Drawing).OfType<GlyphRunDrawing>().ToArray();
        return glyphs.Length != 0 && glyphs.All(glyph => ReferenceEquals(glyph.ForegroundBrush, brush));
    }
    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) foreach (Drawing nested in Drawings(child)) yield return nested;
    }
    private sealed class CountingSource(TextSource inner) : TextSource
    {
        internal TextSource Inner { get; set; } = inner;
        internal int Queries { get; set; }
        public override TextRun GetTextRun(int index) { Queries++; return Inner.GetTextRun(index); }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => Inner.GetPrecedingText(limit);
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => Inner.GetTextEffectCharacterIndexFromTextSourceCharacterIndex(index);
    }
    private sealed class ScopedSource(RunProperties properties) : TextSource
    {
        private const string Text = "A\n\u05d0\u05d1\u05d2 123 xyz";
        public override TextRun GetTextRun(int index) => index == 0 ? new Underline() : index <= Text.Length ?
            new TextCharacters(Text, index - 1, Text.Length - index + 1, properties) : index == Text.Length + 1 ? new TextEndOfSegment(1) : new TextEndOfParagraph(1);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new(0, new CultureSpecificCharacterBufferRange(properties.CultureInfo, new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
    private sealed class Underline : TextModifier
    {
        public override int Length => 1;
        public override TextRunProperties Properties => null!;
        public override bool HasDirectionalEmbedding => false;
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextRunProperties ModifyProperties(TextRunProperties properties) => properties == null ? null! : new Decorated(properties);
    }
    private sealed class Decorated(TextRunProperties original) : TextRunProperties
    {
        public override Typeface Typeface => original.Typeface;
        public override double FontRenderingEmSize => original.FontRenderingEmSize;
        public override double FontHintingEmSize => original.FontHintingEmSize;
        public override Brush ForegroundBrush => original.ForegroundBrush;
        public override Brush BackgroundBrush => original.BackgroundBrush;
        public override CultureInfo CultureInfo => original.CultureInfo;
        public override TextEffectCollection TextEffects => original.TextEffects;
        public override TextDecorationCollection TextDecorations => System.Windows.TextDecorations.Underline;
    }
}