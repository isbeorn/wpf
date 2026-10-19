using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

static class TextOracleCases
{
    internal static object CaptureAlignment()
    {
        var family = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans");
        var properties = new OracleProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
        var cases = new List<object>();
        foreach (TextFormattingMode mode in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
        {
            using var formatter = TextFormatter.Create(mode);
            foreach (string text in new[] { "", "abc", "abc  ", "\u05d0\u05d1\u05d2  ", "abc \u05d0\u05d1\u05d2  ", "short\nlonger line  " })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                var source = new OracleSource(text, properties);
                using var natural = formatter.FormatLine(source, 0, 1000, new OracleParagraph(properties, flow), null);
                foreach (double width in new[] { natural.Width + 60, natural.Width, Math.Max(1, natural.Width / 2), 0 }.Distinct())
                foreach (TextAlignment alignment in new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right })
                {
                    int first = 0;
                    TextLineBreak? previous = null;
                    var lines = new List<object>();
                    try
                    {
                        do
                        {
                            using var line = formatter.FormatLine(source, first, width, new OracleParagraph(properties, flow, alignment: alignment), previous!);
                            previous?.Dispose();
                            previous = line.GetTextLineBreak();
                            int count = Math.Min(text.Length - first, line.Length - line.NewlineLength);
                            var visual = new DrawingVisual();
                            using (var context = visual.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
                            lines.Add(new { first, line.Length, line.NewlineLength, line.Start, line.Width, line.WidthIncludingTrailingWhitespace,
                                line.OverhangLeading, line.OverhangTrailing,
                                carets = Enumerable.Range(first, count + 1).Select(index => {
                                    double distance = line.GetDistanceFromCharacterHit(new CharacterHit(index, 0));
                                    var hit = line.GetCharacterHitFromDistance(distance);
                                    return new { index, distance, hit.FirstCharacterIndex, hit.TrailingLength, roundtrip = line.GetDistanceFromCharacterHit(hit) };
                                }).ToArray(),
                                bounds = count == 0 ? Array.Empty<object>() : line.GetTextBounds(first, count).Select(bound => new { rectangle = Box(bound.Rectangle), flow = bound.FlowDirection.ToString() }).ToArray(),
                                runs = line.GetIndexedGlyphRuns().Select(run => new { run.TextSourceCharacterIndex, run.TextSourceLength, run.GlyphRun.BidiLevel,
                                    x = run.GlyphRun.BaselineOrigin.X, y = run.GlyphRun.BaselineOrigin.Y }).ToArray(), drawing = Describe(visual.Drawing) });
                            first += line.Length;
                        } while (first < text.Length);
                    }
                    finally { previous?.Dispose(); }
                    cases.Add(new { text, mode = mode.ToString(), flow = flow.ToString(), alignment = alignment.ToString(), paragraphWidth = width, lines });
                }
            }
        }
        return new { framework = typeof(TextFormatter).Assembly.FullName,
            frameworkFileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(TextFormatter).Assembly.Location).FileVersion, cases };
    }
    internal static void VerifyAlignment(bool idealOnly = false)
    {
        var family = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans");
        var properties = new OracleProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
        foreach (TextFormattingMode mode in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
        {
            if (idealOnly && mode != TextFormattingMode.Ideal) continue;
            using var formatter = TextFormatter.Create(mode);
            foreach (double pixelsPerDip in new[] { 1d, 1.5, 2 })
            foreach (string text in new[] { "", "abc", "abc  ", "\u05d0\u05d1\u05d2  ", "abc \u05d0\u05d1\u05d2  ", "short\nlonger line  " })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                var source = new OracleSource(text, properties) { PixelsPerDip = pixelsPerDip };
                using var natural = formatter.FormatLine(source, 0, 1000, new OracleParagraph(properties, flow), null);
                foreach (double width in new[] { natural.Width + 60, natural.Width, Math.Max(1, natural.Width / 2), 0 }.Distinct())
                foreach (TextWrapping wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
                foreach (TextAlignment alignment in new[] { TextAlignment.Center, TextAlignment.Right })
                {
                    int first = 0;
                    do
                    {
                        using var left = formatter.FormatLine(source, first, width, new OracleParagraph(properties, flow, wrapping), null);
                        using var aligned = formatter.FormatLine(source, first, width, new OracleParagraph(properties, flow, wrapping, alignment), null);
                        double shift = (width - left.Width) / (alignment == TextAlignment.Center ? 2 : 1);
                        if (mode == TextFormattingMode.Display) shift = Math.Round(shift * pixelsPerDip) / pixelsPerDip;
                        string context = $"{mode}/{pixelsPerDip}/{flow}/{wrapping}/{alignment}/{width:R}/{first}/{text}";
                        void Near(double actual, double expected, string property)
                        {
                            if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > 0.01)
                                throw new InvalidOperationException($"{context}: {property} expected {expected:R}, got {actual:R}.");
                        }
                        Near(aligned.Start, shift, "Start");
                        Near(aligned.Length, left.Length, "Length");
                        Near(aligned.TrailingWhitespaceLength, left.TrailingWhitespaceLength, "TrailingWhitespaceLength");
                        Near(aligned.Height, left.Height, "Height");
                        Near(aligned.Width, left.Width, "Width");
                        Near(aligned.WidthIncludingTrailingWhitespace, left.WidthIncludingTrailingWhitespace, "WidthIncludingTrailingWhitespace");
                        Near(aligned.OverhangLeading, left.OverhangLeading, "OverhangLeading");
                        Near(aligned.OverhangTrailing, left.OverhangTrailing, "OverhangTrailing");
                        int count = Math.Min(text.Length - first, left.Length - left.NewlineLength);
                        for (int index = first; index <= first + count; index++)
                        {
                            double distance = aligned.GetDistanceFromCharacterHit(new CharacterHit(index, 0));
                            Near(distance, left.GetDistanceFromCharacterHit(new CharacterHit(index, 0)) + shift, "caret distance");
                            Near(aligned.GetDistanceFromCharacterHit(aligned.GetCharacterHitFromDistance(distance)), distance, "caret roundtrip");
                        }
                        if (count > 0)
                        {
                            var originalBounds = left.GetTextBounds(first, count).ToArray();
                            var bounds = aligned.GetTextBounds(first, count).ToArray();
                            if (bounds.Length != originalBounds.Length) throw new InvalidOperationException(context + ": bound count changed.");
                            for (int index = 0; index < bounds.Length; index++)
                            {
                                double expectedLeft = originalBounds[index].Rectangle.Left + shift;
                                double expectedRight = originalBounds[index].Rectangle.Right + shift;
                                Near(bounds[index].Rectangle.X, expectedLeft, "selection X");
                                Near(bounds[index].Rectangle.Width, expectedRight - expectedLeft, "selection width");
                            }
                        }
                        var originalRuns = left.GetIndexedGlyphRuns().ToArray();
                        var runs = aligned.GetIndexedGlyphRuns().ToArray();
                        if (runs.Length != originalRuns.Length) throw new InvalidOperationException(context + ": glyph run count changed.");
                        for (int index = 0; index < runs.Length; index++)
                        {
                            Near(runs[index].GlyphRun.BaselineOrigin.X, originalRuns[index].GlyphRun.BaselineOrigin.X, "indexed glyph origin");
                            if (!runs[index].GlyphRun.GlyphIndices.SequenceEqual(originalRuns[index].GlyphRun.GlyphIndices) ||
                                !runs[index].GlyphRun.AdvanceWidths.SequenceEqual(originalRuns[index].GlyphRun.AdvanceWidths) ||
                                !runs[index].GlyphRun.ClusterMap.SequenceEqual(originalRuns[index].GlyphRun.ClusterMap))
                                throw new InvalidOperationException(context + ": alignment changed shaping.");
                        }
                        Rect DrawBounds(TextLine line)
                        {
                            var visual = new DrawingVisual();
                            using (var drawing = visual.RenderOpen()) line.Draw(drawing, new Point(7, 9), InvertAxes.None);
                            return visual.Drawing.Bounds;
                        }
                        Rect originalInk = DrawBounds(left), ink = DrawBounds(aligned);
                        if (!originalInk.IsEmpty)
                        {
                            Near(ink.X, originalInk.X + (flow == FlowDirection.LeftToRight ? shift : -shift), "drawing X");
                            Near(ink.Width, originalInk.Width, "drawing width");
                        }
                        first += left.Length;
                    } while (first < text.Length);
                }
            }
        }
    }
    internal static object CaptureBoundaries()
    {
        var family = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans");
        var properties = new OracleProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
        var cases = new List<object>();
        using var formatter = TextFormatter.Create();
        foreach (string text in new[] { "a\u2028b", "a\u2029b", "a\u0085b", "a\u000bb", "a\u000cb", "q\u0301", "\ud83d\ude00", "office", "\u05d0\u05d1\u05d2\u2028 123 abc" })
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        foreach (TextWrapping wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
        foreach (int chunks in new[] { 0, 1, 2 })
        {
            var source = new OracleSource(text, properties, chunks == 0 ? -1 : text == "office" ? 2 : 1,
                chunks == 2 ? new OracleProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)) : null);
            var paragraph = new OracleParagraph(properties, flow, wrapping);
            var lines = new List<object>();
            int first = 0;
            TextLineBreak? previous = null;
            try
            {
                while (first < text.Length)
                {
                    using var line = formatter.FormatLine(source, first, 1000, paragraph, previous!);
                    previous?.Dispose();
                    previous = line.GetTextLineBreak();
                    var spans = line.GetTextRunSpans().ToArray();
                    int visible = line.Length - line.NewlineLength;
                    lines.Add(new { first, line.Length, line.NewlineLength, continuation = previous != null,
                        spans = spans.Select(span => span.Length).ToArray(),
                        selections = Enumerable.Range(0, visible).Select(offset => (Offset: offset, Length: 1)).Append((Offset: 0, Length: visible)).Distinct()
                            .Where(range => range.Length > 0).Select(range => new { first = first + range.Offset, length = range.Length,
                                bounds = line.GetTextBounds(first + range.Offset, range.Length).SelectMany(bound => bound.TextRunBounds ?? Array.Empty<TextRunBounds>())
                                    .Select(bound => new { bound.TextSourceCharacterIndex, bound.Length, rectangle = Box(bound.Rectangle),
                                        span = Array.FindIndex(spans, span => ReferenceEquals(span.Value, bound.TextRun)) }).ToArray() }).ToArray(),
                        runs = line.GetIndexedGlyphRuns().Select(run => new { run.TextSourceCharacterIndex, run.TextSourceLength,
                            run.GlyphRun.BidiLevel, characters = new string(run.GlyphRun.Characters.ToArray()) }).ToArray() });
                    first += line.Length;
                }
            }
            finally { previous?.Dispose(); }
            cases.Add(new { text, flow = flow.ToString(), wrapping = wrapping.ToString(), split = chunks != 0, distinctProperties = chunks == 2, lines });
        }
        return new { framework = typeof(TextFormatter).Assembly.FullName, cases };
    }
    internal static object Capture()
    {
        var cases = new List<object>();
        var family = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans");
        var properties = new OracleProperties(new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
        foreach (string text in new[] { "abc \u05d0\u05d1\u05d2 123 (xy)", "\u0633\u0644\u0627\u0645 abc (123) \u05e9\u05dc\u05d5\u05dd" })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
                foreach (TextFormattingMode mode in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
                {
                    using var formatter = TextFormatter.Create(mode);
                    var source = new OracleSource(text, properties) { PixelsPerDip = 1.5 };
                    using var line = formatter.FormatLine(source, 0, 1000, new OracleParagraph(properties, flow), null);
                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen()) line.Draw(context, new Point(0, 0), InvertAxes.None);
                    cases.Add(new { text, flow = flow.ToString(), mode = mode.ToString(), line.Start, line.Width, line.WidthIncludingTrailingWhitespace,
                        line.Height, line.Baseline, distances = Enumerable.Range(0, text.Length + 1).Select(index => line.GetDistanceFromCharacterHit(new CharacterHit(index, 0))).ToArray(),
                        bounds = line.GetTextBounds(0, text.Length).Select(item => new { rectangle = Box(item.Rectangle), flow = item.FlowDirection.ToString() }).ToArray(),
                        drawing = Describe(visual.Drawing),
                        runs = line.GetIndexedGlyphRuns().OrderBy(run => run.TextSourceCharacterIndex).Select(run => new {
                            start = run.TextSourceCharacterIndex, length = run.TextSourceLength, run.GlyphRun.BidiLevel,
                            font = Path.GetFileName(run.GlyphRun.GlyphTypeface.FontUri.LocalPath),
                            fontUri = run.GlyphRun.GlyphTypeface.FontUri.AbsoluteUri,
                            originX = run.GlyphRun.BaselineOrigin.X, originY = run.GlyphRun.BaselineOrigin.Y,
                            glyphs = run.GlyphRun.GlyphIndices.ToArray(), advances = run.GlyphRun.AdvanceWidths.ToArray(),
                            offsets = run.GlyphRun.GlyphOffsets.Select(value => new { x = value.X, y = value.Y }).ToArray(),
                            map = run.GlyphRun.ClusterMap.ToArray(),
                            ink = Box(run.GlyphRun.ComputeInkBoundingBox()), geometry = Box(run.GlyphRun.BuildGeometry().Bounds) }).ToArray() });
                }
        return new { framework = typeof(TextFormatter).Assembly.FullName,
            frameworkFileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(TextFormatter).Assembly.Location).FileVersion,
            culture = CultureInfo.CurrentCulture.Name, cases, positionCases = CapturePositions() };
    }
    internal static object[] CapturePositions()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        var face = new GlyphTypeface(new Uri(path));
        ushort[] glyphs = { face.CharacterToGlyphMap['\u0633'], face.CharacterToGlyphMap['\u064e'], face.CharacterToGlyphMap['\u0644'] };
        Point[] offsets = { new(.5, 1), new(-3, 4), new(.25, -2) };
        var results = new List<object>();
        MethodInfo factory = typeof(GlyphRun).GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (int level in new[] { 0, 1 })
            foreach (TextFormattingMode mode in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
                foreach (float dpi in new[] { 1f, 1.5f })
                {
                    double[] advances = { 19.25, 0, 11.5 };
                    if (mode == TextFormattingMode.Display) advances = advances.Select(value => Math.Round(value * dpi) / dpi).ToArray();
                    var run = (GlyphRun)factory.Invoke(null, new object?[] { face, level, false, 24d, dpi, glyphs, new Point(100, 40),
                        advances, offsets, new[] { '\u0633', '\u064e', '\u0644' }, null, new ushort[] { 0, 1, 2 },
                        new[] { true, false, true, true }, XmlLanguage.GetLanguage("ar-SA"), mode })!;
                    results.Add(new { level, mode = mode.ToString(), dpi, geometry = Box(run.BuildGeometry().Bounds) });
                }
        return results.ToArray();
    }
    private static object? Box(Rect rectangle) => rectangle.IsEmpty ? null : new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
    private static object Describe(Drawing drawing) => drawing is GlyphRunDrawing glyph ?
        new { kind = "glyph", x = glyph.GlyphRun.BaselineOrigin.X, y = glyph.GlyphRun.BaselineOrigin.Y, level = glyph.GlyphRun.BidiLevel, text = new string(glyph.GlyphRun.Characters.ToArray()) } :
        drawing is DrawingGroup group ? (object)new { kind = "group", transform = group.Transform?.Value.ToString(CultureInfo.InvariantCulture), children = group.Children.Select(Describe).ToArray() } :
        new { kind = drawing.GetType().Name };
    private sealed class OracleProperties(Typeface face) : TextRunProperties
    {
        public override Typeface Typeface => face;
        public override double FontRenderingEmSize => 24;
        public override double FontHintingEmSize => 24;
        public override TextDecorationCollection TextDecorations => null!;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush BackgroundBrush => null!;
        public override CultureInfo CultureInfo => CultureInfo.GetCultureInfo("en-US");
        public override TextEffectCollection TextEffects => null!;
    }
    private sealed class OracleParagraph(TextRunProperties properties, FlowDirection flow, TextWrapping wrapping = TextWrapping.NoWrap, TextAlignment alignment = TextAlignment.Left) : TextParagraphProperties
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
    private sealed class OracleSource(string text, TextRunProperties properties, int split = -1, TextRunProperties? followingProperties = null) : TextSource
    {
        public override TextRun GetTextRun(int index) => index >= text.Length ? new TextEndOfParagraph(1) :
            new TextCharacters(text, index, (split > index ? split : text.Length) - index,
                split >= 0 && index >= split && followingProperties != null ? followingProperties : properties);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), new CharacterBufferRange(text, 0, Math.Min(limit, text.Length))));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
}
