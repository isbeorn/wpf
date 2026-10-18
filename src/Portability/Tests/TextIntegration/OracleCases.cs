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
    private sealed class OracleParagraph(TextRunProperties properties, FlowDirection flow, TextWrapping wrapping = TextWrapping.NoWrap) : TextParagraphProperties
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
