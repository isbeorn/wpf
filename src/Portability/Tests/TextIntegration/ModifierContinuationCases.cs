using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

static class ModifierContinuationCases
{
    internal sealed record Line(int First, int Length, int Newline, bool Continuation, string Text,
        int[] Spans, bool OriginalRuns, double[] Decorations, double[] GlyphOrigins, double DecorationWidth, double GlyphWidth);
    internal sealed record Case(string Name, string Flow, Line[] Lines);

    internal static Case[] Capture()
    {
        var cases = new List<Case>();
        var properties = new HiddenRunCases.Properties();
        using var formatter = TextFormatter.Create();
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var textValues = new Dictionary<TextRun, string>();
            void Add(string name, TextRun[] runs, double width = 1000)
            {
                var source = new Source(runs, textValues);
                var lines = new List<Line>();
                TextLineBreak? previous = null;
                int first = 0;
                try
                {
                    while (first < source.Length + 1)
                    {
                        using var line = formatter.FormatLine(source, first, width,
                            new HiddenRunCases.Paragraph(properties, TextWrapping.Wrap, flow), previous!);
                        previous?.Dispose();
                        previous = line.GetTextLineBreak();
                        var visual = new DrawingVisual();
                        using (var context = visual.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
                        var drawings = Walk(visual.Drawing).ToArray();
                        var glyphs = drawings.OfType<GlyphRunDrawing>().ToArray();
                        var spans = line.GetTextRunSpans();
                        lines.Add(new Line(first, line.Length, line.NewlineLength, previous != null,
                            string.Concat(line.GetIndexedGlyphRuns().Select(run => new string(run.GlyphRun.Characters.ToArray()))),
                            spans.Select(span => span.Length).ToArray(), spans.All(span => source.Returned.Contains(span.Value)),
                            drawings.OfType<GeometryDrawing>().Select(drawing => drawing.Bounds.Left).OrderBy(x => x).ToArray(),
                            glyphs.Select(glyph => glyph.GlyphRun.BaselineOrigin.X).ToArray(),
                            drawings.OfType<GeometryDrawing>().Sum(drawing => drawing.Bounds.Width), glyphs.Sum(glyph => glyph.GlyphRun.AdvanceWidths.Sum())));
                        if (line.Length <= 0 || lines.Count > 12) throw new InvalidOperationException("Modifier continuation made no source progress.");
                        first += line.Length;
                    }
                }
                finally { previous?.Dispose(); }
                cases.Add(new Case(name, flow.ToString(), lines.ToArray()));
            }
            TextCharacters Text(string value) { var run = new TextCharacters(value, properties); textValues.Add(run, value); return run; }
            Add("soft-single", new TextRun[] { new Modifier(TextDecorations.Underline), Text("A"), new TextEndOfLine(1), Text("B"), new TextEndOfSegment(1), Text("C") });
            Add("nested-soft", new TextRun[] { new Modifier(TextDecorations.Underline), Text("A"), new TextEndOfLine(1),
                new Modifier(TextDecorations.Strikethrough), Text("B"), new TextEndOfLine(1), Text("C"), new TextEndOfSegment(1), Text("D"),
                new TextEndOfLine(1), Text("E"), new TextEndOfSegment(1), Text("F") });
            Add("paragraph-reset", new TextRun[] { new Modifier(TextDecorations.Underline), Text("A"), new TextEndOfParagraph(1), Text("B") });
            Add("wrapped", new TextRun[] { new Modifier(TextDecorations.Underline), Text("A A A"), new TextEndOfSegment(1) }, 30);
            Add("no-scope-soft", new TextRun[] { Text("A"), new TextEndOfLine(1), Text("B") });
            foreach (var separator in new[] { ("LF", "\n"), ("CRLF", "\r\n"), ("LS", "\u2028"), ("PS", "\u2029") })
                Add(separator.Item1, new TextRun[] { new Modifier(TextDecorations.Underline), Text("A"), Text(separator.Item2), Text("B"), new TextEndOfSegment(1), Text("C") });
        }
        return cases.ToArray();
    }

    internal static void Verify(Action<bool, string, string> require)
    {
        foreach (var item in Capture())
        {
            if (item.Name is "soft-single" or "LF" or "CRLF" or "LS" or "PS")
                require(item.Lines.Length == 2 && item.Lines.Select(line => line.Length).SequenceEqual(new[] { item.Name == "CRLF" ? 4 : 3, 4 }) &&
                    item.Lines[0].Continuation && !item.Lines[1].Continuation && item.Lines[1].Text == "BC" &&
                    item.Lines[1].Decorations.Length == 1 && Math.Abs(item.Lines[1].GlyphOrigins[0] - item.Lines[1].Decorations[0]) < 0.01,
                    "modifier-soft-line-survives-and-restores-" + item.Name + "-" + item.Flow,
                    "A soft line break retains the underline on B until TextEndOfSegment, then leaves C plain, matching the stock source-length and drawing control.");
            else if (item.Name == "nested-soft")
                require(item.Lines.Select(line => line.Length).SequenceEqual(new[] { 3, 3, 4, 4 }) &&
                    item.Lines.Select(line => line.Decorations.Length).SequenceEqual(new[] { 1, 2, 3, 1 }) &&
                    item.Lines.Select(line => line.Continuation).SequenceEqual(new[] { true, true, true, false }) &&
                    item.Lines.Select(line => line.Text).SequenceEqual(new[] { "A", "B", "CD", "EF" }),
                    "modifier-nested-soft-scope-restoration-" + item.Flow,
                    "Balanced nested scopes survive three genuine soft breaks and restore the inner then outer decorations at their original end segments.");
            else if (item.Name == "paragraph-reset")
                require(item.Lines.Select(line => line.Length).SequenceEqual(new[] { 3, 2 }) &&
                    item.Lines.Select(line => line.Decorations.Length).SequenceEqual(new[] { 1, 0 }) && item.Lines.All(line => !line.Continuation),
                    "modifier-paragraph-end-resets-scope-" + item.Flow,
                    "A genuine TextEndOfParagraph terminates an open modifier and does not decorate the following paragraph.");
            else if (item.Name == "wrapped")
                require(item.Lines.Select(line => line.Length).SequenceEqual(new[] { 3, 2, 3 }) && item.Lines.All(line => line.Decorations.Length != 0 && Math.Abs(line.DecorationWidth - line.GlyphWidth) < 0.01) &&
                    item.Lines.Select(line => line.Continuation).SequenceEqual(new[] { true, true, false }),
                    "modifier-ordinary-wrap-retains-scope-" + item.Flow,
                    "The existing cached paragraph wrap path retains the same modifier and exact source consumption on every wrapped line.");
            else if (item.Name == "no-scope-soft")
                require(item.Lines.Select(line => line.Length).SequenceEqual(new[] { 2, 2 }) &&
                    item.Lines.All(line => !line.Continuation && line.Decorations.Length == 0),
                    "modifier-plain-soft-break-has-no-scope-record-" + item.Flow,
                    "A soft break without an active modifier preserves the stock null continuation behavior.");
            require(item.Lines.All(line => line.Spans.Sum() == line.Length && line.OriginalRuns) &&
                item.Lines.Skip(1).Select((line, index) => line.First == item.Lines[index].First + item.Lines[index].Length).All(value => value),
                "modifier-source-progress-" + item.Name + "-" + item.Flow,
                "Continuation spans retain the actual returned runs and consume original source positions exactly once.");
        }
        var properties = new HiddenRunCases.Properties();
        using var formatter = TextFormatter.Create();
        var a = new TextCharacters("A", properties);
        var b = new TextCharacters("B", properties);
        var source = new Source(new TextRun[] { new Modifier(TextDecorations.Underline), a, new TextEndOfLine(1), b, new TextEndOfSegment(1) },
            new Dictionary<TextRun, string> { [a] = "A", [b] = "B" });
        var paragraph = new HiddenRunCases.Paragraph(properties);
        using var firstLine = formatter.FormatLine(source, 0, 1000, paragraph, null!);
        using var original = firstLine.GetTextLineBreak();
        using var clone = original!.Clone();
        original.Dispose();
        using var secondLine = formatter.FormatLine(source, firstLine.Length, 1000, paragraph, clone);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) secondLine.Draw(context, new Point(), InvertAxes.None);
        bool disposedRejected = false;
        try { using var invalid = formatter.FormatLine(source, firstLine.Length, 1000, paragraph, original); }
        catch (ObjectDisposedException) { disposedRejected = true; }
        require(Walk(visual.Drawing).OfType<GeometryDrawing>().Count() == 1 && disposedRejected,
            "modifier-soft-continuation-clone-and-dispose", "A cloned native-free scope record remains usable after disposing the original, which cannot be reused for formatting.");
        bool directionalRejected = false;
        try { using var unsupported = formatter.FormatLine(new Source(new TextRun[] { new Modifier(TextDecorations.Underline, directional: true), a },
            new Dictionary<TextRun, string> { [a] = "A" }), 0, 1000, paragraph, null!); }
        catch (NotSupportedException exception) { directionalRejected = exception.Message.Contains("directional", StringComparison.Ordinal); }
        require(directionalRejected, "modifier-directional-embedding-stays-unsupported", "Soft continuation support does not silently enable the unqualified directional modifier profile.");
    }
    private static IEnumerable<Drawing> Walk(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) foreach (Drawing nested in Walk(child)) yield return nested;
    }
    private sealed class Source(TextRun[] runs, Dictionary<TextRun, string> textValues) : TextSource
    {
        internal int Length => runs.Sum(run => run.Length);
        internal HashSet<TextRun> Returned { get; } = new();
        public override TextRun GetTextRun(int index)
        {
            TextRun result = new TextEndOfParagraph(1);
            foreach (TextRun run in runs)
            {
                if (index < run.Length)
                {
                    if (index == 0) result = run;
                    else if (run is TextCharacters) result = new TextCharacters(textValues[run], index, run.Length - index, run.Properties);
                    else throw new InvalidOperationException("This structural source run must be consumed atomically.");
                    break;
                }
                index -= run.Length;
            }
            Returned.Add(result);
            return result;
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new(0, new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
    private sealed class Modifier(TextDecorationCollection decorations, bool directional = false) : TextModifier
    {
        public override int Length => 1;
        public override TextRunProperties Properties => null!;
        public override bool HasDirectionalEmbedding => directional;
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextRunProperties ModifyProperties(TextRunProperties properties) => properties == null ? null! : new Modified(properties, decorations);
    }
    private sealed class Modified(TextRunProperties original, TextDecorationCollection decorations) : TextRunProperties
    {
        public override Typeface Typeface => original.Typeface;
        public override double FontRenderingEmSize => original.FontRenderingEmSize;
        public override double FontHintingEmSize => original.FontHintingEmSize;
        public override Brush ForegroundBrush => original.ForegroundBrush;
        public override Brush BackgroundBrush => original.BackgroundBrush;
        public override CultureInfo CultureInfo => original.CultureInfo;
        public override TextEffectCollection TextEffects => original.TextEffects;
        public override TextDecorationCollection TextDecorations { get; } = new(decorations.Concat(original.TextDecorations ?? new TextDecorationCollection()));
    }
}