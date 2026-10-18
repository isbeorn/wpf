using System.Security.Cryptography;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using SkiaSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

static class BidiTests
{
    internal static RunProperties Properties(string file, string family, double size = 24) =>
        new(new Typeface(new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./" + file + "#" + family),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size);

    internal static void Run(TextFormatter formatter, Action<bool, string, string> require, Action<string, Action> regression)
    {
        regression("fixed-font-provenance", () =>
        {
            foreach (var item in new[] { ("DejaVuSans.ttf", "35226AC1E50D1B60AF2FF7DA5878D8EDE0C83BC00FE84713F265CB6DED8F5F9F"),
                ("Inter-Regular.ttf", "41AB0F707A2BFAB8133CCDFCDAB52282F5F79E5751F43A264805451C7BB95FB8") })
                require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", item.Item1)))) == item.Item2,
                    "fixed-font-hash-" + item.Item1, "Both operating systems use identical, unmodified redistributable font bytes.");
        });
        var properties = Properties("DejaVuSans.ttf", "DejaVu Sans");
        regression("mandatory-unicode-line-terminators", () =>
        {
            foreach (char separator in new[] { '\u2028', '\u2029', '\u0085', '\u000b', '\u000c' })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (TextWrapping wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
            foreach (bool split in new[] { false, true })
            {
                string text = "a" + separator + "b" + separator + separator + "c";
                var source = split ? new Source(new[] { ("a", properties), (separator.ToString(), properties),
                    ("b" + separator + separator + "c", properties) }) : new Source(text, properties);
                var paragraph = new Paragraph(properties, wrapping, flow: flow);
                int first = 0;
                var lengths = new List<int>();
                var newlines = new List<int>();
                var visible = new List<string>();
                while (first < text.Length)
                {
                    using var line = formatter.FormatLine(source, first, 1000, paragraph, null);
                    lengths.Add(line.Length);
                    newlines.Add(line.NewlineLength);
                    visible.Add(string.Concat(line.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters)));
                    if (line.Length <= 0 || line.GetTextRunSpans().Sum(span => span.Length) != line.Length)
                        throw new InvalidOperationException("A mandatory line terminator lost a consumed source span.");
                    first += line.Length;
                }
                require(lengths.SequenceEqual(new[] { 2, 2, 1, 2 }) && newlines.All(value => value == 1) &&
                    visible.SequenceEqual(new[] { "a", "b", "", "c" }),
                    $"mandatory-separator-{(int)separator:X4}-{flow}-{wrapping}-{split}",
                    "Mandatory Unicode separators end a line before shaping, including split source runs, empty lines and following absolute source positions.");
            }
        });
        regression("identical-properties-grapheme-source-chunks", () =>
        {
            foreach (string text in new[] { "q\u0301", "\ud83d\ude00", "office" })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (int chunks in new[] { 0, 1, 2, 3, 4 })
            {
                var frozenBlack = new SolidColorBrush(Colors.Black);
                frozenBlack.Freeze();
                RunProperties second = chunks switch {
                    0 => properties,
                    1 => new RunProperties(properties.Typeface, 24),
                    2 => Properties("DejaVuSans.ttf", "DejaVu Sans"),
                    3 => new RunProperties(properties.Typeface, 24, frozenBlack),
                    _ => new RunProperties(properties.Typeface, 24, typography: new TestTypography()) };
                var paragraph = new Paragraph(properties, flow: flow);
                int splitAt = text == "office" ? 2 : 1;
                using var whole = formatter.FormatLine(new Source(text, properties), 0, 1000, paragraph, null);
                using var split = formatter.FormatLine(new Source(new[] { (text[..splitAt], properties), (text[splitAt..], second) }),
                    0, 1000, paragraph, null);
                CharacterHit next = split.GetNextCaretCharacterHit(new CharacterHit(0, 0));
                require(split.Length == text.Length + 1 && split.NewlineLength == 1 && Math.Abs(whole.Width - split.Width) < 0.001 &&
                    whole.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices).SequenceEqual(
                        split.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)) &&
                    split.GetTextRunSpans().Select(span => span.Length).SequenceEqual(new[] { splitAt, text.Length - splitAt, 1 }) &&
                    ReferenceEquals(split.GetTextRunSpans()[1].Value.Properties, second) &&
                    next.FirstCharacterIndex + next.TrailingLength == (text == "office" ? 1 : text.Length),
                    $"equivalent-properties-grapheme-chunks-{(int)text[0]:X4}-{flow}-{chunks}",
                    "Equivalent source formatting retains original spans, whole grapheme shaping and caret stops across arbitrary TextCharacters chunks.");
            }
            foreach (Brush paint in new Brush[] { new SolidColorBrush(Colors.Black), Brushes.Blue })
            {
                var painted = new RunProperties(properties.Typeface, 24, paint);
                bool rejected = false;
                try { using var line = formatter.FormatLine(new Source(new[] { ("q", properties), ("\u0301", painted) }),
                    0, 1000, new Paragraph(properties), null); }
                catch (NotSupportedException exception) { rejected = exception.Message.Contains("formatting properties"); }
                require(rejected, paint.IsFrozen ? "reject-different-paint-inside-grapheme" : "reject-independent-mutable-paint-inside-grapheme",
                    "A grapheme cannot discard a distinct paint value or an independently mutable source brush.");
            }
        });
        regression("coalesced-shape-original-source-bounds", () =>
        {
            foreach (var item in new[] { (Text: "abcdef", Split: 3), (Text: "q\u0301", Split: 1),
                (Text: "\ud83d\ude00", Split: 1), (Text: "office", Split: 2) })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                using var line = formatter.FormatLine(new Source(new[] { (item.Text[..item.Split], properties),
                    (item.Text[item.Split..], properties) }), 0, 1000, new Paragraph(properties, flow: flow), null);
                var spans = line.GetTextRunSpans();
                foreach (var range in new[] { (First: item.Split, Length: item.Text.Length - item.Split),
                    (First: 0, Length: item.Text.Length), (First: item.Text.Length, Length: -item.Text.Length),
                    (First: 0, Length: 1), (First: 1, Length: 1) }.Distinct())
                {
                    var bounds = line.GetTextBounds(range.First, range.Length).SelectMany(bound => bound.TextRunBounds).ToArray();
                    int first = Math.Min(range.First, range.First + range.Length), end = first + Math.Abs(range.Length);
                    require(bounds.Sum(bound => bound.Length) == end - first && bounds.All(bound =>
                        bound.TextSourceCharacterIndex >= first && bound.TextSourceCharacterIndex + bound.Length <= end &&
                        ReferenceEquals(bound.TextRun, spans[bound.TextSourceCharacterIndex < item.Split ? 0 : 1].Value) &&
                        bound.TextSourceCharacterIndex + bound.Length <= (bound.TextSourceCharacterIndex < item.Split ? item.Split : item.Text.Length)),
                        $"original-source-bounds-{(int)item.Text[0]:X4}-{item.Text.Length}-{item.Split}-{flow}-{range.First}-{range.Length}",
                        "Merged glyph shaping projects selection metadata onto each original source run, including partial clusters and reversed ranges.");
                }
                if (item.Text.Length == 2)
                {
                    var first = line.GetTextBounds(0, 1).Single();
                    var second = line.GetTextBounds(1, 1).Single();
                    require(Math.Abs(first.Rectangle.Width - line.Width) < 0.001 && second.Rectangle.Width == 0,
                        $"partial-grapheme-selection-width-{(int)item.Text[0]:X4}-{flow}",
                        "Stock WPF assigns a grapheme's visible selection width to its first code unit and zero width to following mark or low-surrogate code units.");
                }
            }
        });
        regression("wrapped-source-bounds-offsets", () =>
        {
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (TextWrapping wrapping in new[] { TextWrapping.Wrap, TextWrapping.WrapWithOverflow })
            {
                var paragraph = new Paragraph(properties, wrapping, flow: flow);
                using var measured = formatter.FormatLine(new Source("abc", properties), 0, 1000, paragraph, null);
                var source = new Source("zzabc def ghi", properties, 1);
                using var first = formatter.FormatLine(source, 2, measured.Width + 1, paragraph, null);
                using var continuation = first.GetTextLineBreak();
                using var second = formatter.FormatLine(source, 2 + first.Length, measured.Width + 1, paragraph, continuation);
                var spans = second.GetTextRunSpans();
                var forward = second.GetTextBounds(6, 3).SelectMany(bound => bound.TextRunBounds).OrderBy(bound => bound.TextSourceCharacterIndex).ToArray();
                var reverse = second.GetTextBounds(9, -3).SelectMany(bound => bound.TextRunBounds).OrderBy(bound => bound.TextSourceCharacterIndex).ToArray();
                require(first.Length == 4 && second.Length == 4 && forward.Length == 3 && reverse.Length == 3 &&
                    forward.Select(bound => bound.TextSourceCharacterIndex).SequenceEqual(new[] { 6, 7, 8 }) &&
                    forward.Select((bound, index) => bound.Length == 1 && ReferenceEquals(bound.TextRun, spans[index].Value) &&
                        ReferenceEquals(bound.TextRun, reverse[index].TextRun) && bound.Rectangle == reverse[index].Rectangle).All(value => value),
                    $"wrapped-original-source-bounds-{flow}-{wrapping}",
                    "Continued wrapped lines retain absolute source run identities and symmetric selection rectangles after a nonzero paragraph start.");
            }
        });
        regression("distinct-mutable-source-paints", () =>
        {
            var left = new SolidColorBrush(Colors.Black);
            var right = new SolidColorBrush(Colors.Black);
            var leftProperties = new RunProperties(properties.Typeface, 24, left);
            var rightProperties = new RunProperties(properties.Typeface, 24, right);
            var visual = new DrawingVisual();
            using (var line = formatter.FormatLine(new Source(new[] { ("a", leftProperties), ("b", rightProperties) }),
                0, 1000, new Paragraph(leftProperties), null))
            using (var context = visual.RenderOpen()) line.Draw(context, new Point(8, 8), InvertAxes.None);
            left.Color = Colors.Red;
            right.Color = Colors.Blue;
            using var target = new Target(visual, 100, 80, new DpiScale(1, 1));
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            using var bitmap = target.Snapshot();
            bool red = false, blue = false;
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                red |= color.Alpha > 100 && color.Red > 200 && color.Blue < 20;
                blue |= color.Alpha > 100 && color.Blue > 200 && color.Red < 20;
            }
            require(red && blue, "separate-source-paints-retain-independent-mutation",
                "Distinct initially equal mutable brushes preserve independent retained red and blue pixels after line disposal.");
        });
        regression("default-and-custom-typography", () =>
        {
            var defaults = new RunProperties(properties.Typeface, typography: new TestTypography());
            using var defaultLine = formatter.FormatLine(new Source("office 123", defaults), 0, 1000, new Paragraph(defaults), null);
            var unspecified = new RunProperties(properties.Typeface);
            using var unspecifiedLine = formatter.FormatLine(new Source("office 123", unspecified), 0, 1000, new Paragraph(unspecified), null);
            require(Math.Abs(defaultLine.Width - unspecifiedLine.Width) < 0.001, "accept-semantic-default-typography",
                "A non-null WPF default typography profile produces the same default shaping as unspecified typography.");
            foreach (var option in new[] { new TestTypography(disableKerning: true), new TestTypography(stylisticSet: true) })
            {
                var custom = new RunProperties(properties.Typeface, typography: option);
                bool rejected = false;
                try { using var line = formatter.FormatLine(new Source("office", custom), 0, 1000, new Paragraph(custom), null); }
                catch (NotSupportedException exception) { rejected = exception.Message.Contains("OpenType typography"); }
                require(rejected, option.Kerning ? "reject-enabled-custom-typography" : "reject-disabled-default-typography",
                    "Optional feature enablement and default feature disablement receive the named custom-typography diagnostic.");
            }
        });
        regression("unicode-bidi-character-conformance", () =>
        {
            int number = 0;
            foreach (string row in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "BidiCharacterCases.txt")))
            {
                if (row.Length == 0 || row[0] == '#') continue;
                string[] fields = row.Split(';');
                int[] scalars = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => Convert.ToInt32(value, 16)).ToArray();
                string text = string.Concat(scalars.Select(char.ConvertFromUtf32));
                int level = int.Parse(fields[2], CultureInfo.InvariantCulture);
                using var line = formatter.FormatLine(new Source(text, properties), 0, 1000,
                    new Paragraph(properties, flow: level == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft), null);
                object paragraph = line.GetType().GetField("_paragraph", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(line)!;
                int[] levels = (int[])paragraph.GetType().GetProperty("Levels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(paragraph)!;
                string[] expected = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int offset = 0;
                bool matches = expected.Length == scalars.Length;
                for (int index = 0; index < scalars.Length; index++)
                {
                    if (expected[index] != "x" && levels[offset] != int.Parse(expected[index], CultureInfo.InvariantCulture)) matches = false;
                    offset += scalars[index] > 0xffff ? 2 : 1;
                }
                require(matches, "unicode-bidi-character-case-" + ++number, "Actual paragraph analysis matches the pinned Unicode 17 conformance levels, including isolates, embeddings, brackets and marks. Expected " + fields[3] + "; actual " + string.Join(" ", levels));
            }
            require(number == 48, "unicode-conformance-fixture-count", "Every selected official conformance case ran; missing data cannot silently pass.");
        });
        regression("resolved-number-substitution", () =>
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                foreach (NumberSubstitution substitution in new[] {
                    new NumberSubstitution(NumberCultureSource.User, null!, NumberSubstitutionMethod.AsCulture),
                    new NumberSubstitution(NumberCultureSource.Text, null!, NumberSubstitutionMethod.NativeNational),
                    new NumberSubstitution(NumberCultureSource.Override, CultureInfo.GetCultureInfo("ar-SA"), NumberSubstitutionMethod.European) })
                {
                    var noOp = new RunProperties(properties.Typeface, substitution: substitution);
                    using var line = formatter.FormatLine(new Source("Text 123", noOp), 0, 1000, new Paragraph(noOp), null);
                    require(line.Width > 0, "no-op-substitution-" + substitution.CultureSource + "-" + substitution.Substitution,
                        "The existing WPF DigitState resolver accepts settings that leave the source digits unchanged.");
                }
                foreach (NumberSubstitutionMethod method in new[] { NumberSubstitutionMethod.Context, NumberSubstitutionMethod.NativeNational, NumberSubstitutionMethod.Traditional })
                {
                    var arabic = new RunProperties(properties.Typeface, substitution: new NumberSubstitution(NumberCultureSource.Override,
                        CultureInfo.GetCultureInfo("ar-SA"), method));
                    bool rejected = false;
                    try { using var line = formatter.FormatLine(new Source("Text 123", arabic), 0, 1000, new Paragraph(arabic), null); }
                    catch (NotSupportedException exception) { rejected = exception.Message.Contains("digit substitution"); }
                    require(rejected, "reject-required-digit-substitution-" + method, "Requested nontrivial digit substitution fails with the named capability diagnostic.");
                }
            }
            finally { CultureInfo.CurrentCulture = saved; }
        });
        regression("mixed-bidi-paragraphs", () =>
        {
            foreach (FlowDirection direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                string text = "abc \u0633\u0644\u0627\u0645 123 \u05e9\u05dc\u05d5\u05dd (xyz)!";
                using var line = formatter.FormatLine(new Source(text, properties), 0, 1000, new Paragraph(properties, flow: direction), null);
                var runs = line.GetIndexedGlyphRuns().ToArray();
                require(runs.Any(run => (run.GlyphRun.BidiLevel & 1) != 0) && runs.Any(run => (run.GlyphRun.BidiLevel & 1) == 0) &&
                    runs.Sum(run => run.TextSourceLength) == text.Length && runs.SelectMany(run => run.GlyphRun.Characters).SequenceEqual(text),
                    "mixed-genuine-glyph-runs-" + direction, "Actual WPF glyph runs retain logical UTF-16 source ranges for both paragraph directions.");
                require(runs.All(run => run.GlyphRun.ClusterMap.Zip(run.GlyphRun.ClusterMap.Skip(1), (a, b) => a <= b).All(value => value)) &&
                    runs.All(run => run.GlyphRun.GlyphIndices.All(glyph => glyph != 0)),
                    "mixed-cluster-maps-" + direction, "WPF cluster maps remain logical and ascending while odd-level glyph placement runs right to left.");
                foreach (var run in runs)
                {
                    for (int index = 0; index < run.TextSourceLength; index++)
                    {
                        if (!run.GlyphRun.CaretStops[index]) continue;
                        int absolute = run.TextSourceCharacterIndex + index;
                        double distance = line.GetDistanceFromCharacterHit(new CharacterHit(absolute, 0));
                        CharacterHit hit = line.GetCharacterHitFromDistance(distance);
                        require(Math.Abs(line.GetDistanceFromCharacterHit(hit) - distance) < 0.01,
                            $"bidi-caret-distance-{direction}-{absolute}", "A hit retains a valid affinity at bidi boundaries and maps back to the same visual position.");
                    }
                }
                var selection = line.GetTextBounds(2, text.Length - 4);
                require(selection.Count >= 3 && selection.All(item => item.Rectangle.Width > 0) &&
                    selection.Any(item => item.FlowDirection == FlowDirection.RightToLeft),
                    "bidi-disjoint-selection-" + direction, "Mixed selection returns direction-specific visual rectangles with positive RTL edge widths.");
                var reverse = line.GetTextBounds(text.Length - 2, -(text.Length - 4));
                require(reverse.Select(item => item.Rectangle).SequenceEqual(selection.Select(item => item.Rectangle)),
                    "bidi-reversed-selection-range-" + direction, "A negative source length selects the same visual rectangles while reversing the logical range endpoints.");
                int nextPosition = 0;
                CharacterHit next = new(0, 0);
                for (int step = 0; step < text.Length + 1; step++)
                {
                    CharacterHit candidate = line.GetNextCaretCharacterHit(next);
                    int position = candidate.FirstCharacterIndex + candidate.TrailingLength;
                    if (position == nextPosition) break;
                    if (position < nextPosition) throw new InvalidOperationException("Logical caret navigation moved backwards.");
                    next = candidate;
                    nextPosition = position;
                }
                require(nextPosition == text.Length && line.GetPreviousCaretCharacterHit(next).FirstCharacterIndex < nextPosition,
                    "bidi-logical-caret-both-directions-" + direction, "Next and previous caret operations traverse logical text independently of visual run order.");
            }
        });
        regression("paragraph-continuation-context", () =>
        {
            var source = new Source("\u05d0\u05d1\u05d2 123 xyz", properties);
            var paragraph = new Paragraph(properties, TextWrapping.Wrap);
            using var reference = formatter.FormatLine(new Source("\u05d0\u05d1\u05d2", properties), 0, 1000, paragraph, null);
            using var first = formatter.FormatLine(source, 0, reference.Width + 1, paragraph, null);
            using var original = first.GetTextLineBreak();
            using var clone = original.Clone();
            original.Dispose();
            using var second = formatter.FormatLine(source, first.Length, reference.Width + 1, paragraph, clone);
            require(first.Length == 4 && second.GetIndexedGlyphRuns().Any(run => run.GlyphRun.BidiLevel == 2),
                "wrapped-bidi-retains-paragraph-context", "A wrapped digit run retains its paragraph-resolved level after its preceding Hebrew text moves to another line.");
            require(first.GetIndexedGlyphRuns().Last().GlyphRun.BidiLevel == 0,
                "wrapped-trailing-whitespace-level", "Line trailing whitespace resets to paragraph level after the line break.");
            clone.Dispose();
            bool rejected = false;
            try { using var invalid = formatter.FormatLine(source, first.Length, reference.Width + 1, paragraph, clone); }
            catch (ObjectDisposedException) { rejected = true; }
            require(rejected && second.Width > 0, "disposed-continuation-rejected", "Disposed continuation is rejected while independently formatted lines remain valid.");
        });
        regression("complete-grapheme-font-fallback", () =>
        {
            var primary = Properties("Inter-Regular.ttf", "Inter");
            using var line = formatter.FormatLine(new Source("latin \u0633\u064e\u0644\u0627\u0645", primary), 0, 1000, new Paragraph(primary), null);
            var runs = line.GetIndexedGlyphRuns().ToArray();
            require(runs.Any(run => Path.GetFileName(run.GlyphRun.GlyphTypeface.FontUri.LocalPath) == "Inter-Regular.ttf") &&
                runs.Any(run => Path.GetFileName(run.GlyphRun.GlyphTypeface.FontUri.LocalPath) == "DejaVuSans.ttf") &&
                runs.All(run => run.GlyphRun.GlyphIndices.All(glyph => glyph != 0)),
                "deterministic-file-font-fallback", "A known missing Arabic grapheme selects the expected local fallback file while Latin retains Inter. Actual: " +
                    string.Join("; ", runs.Select(run => run.TextSourceCharacterIndex + ":" + run.GlyphRun.GlyphTypeface.FontUri.LocalPath)));
            require(line.Baseline > 0 && line.Height >= line.Baseline && line.Width > 0,
                "fallback-line-metrics", "Fallback faces contribute real baseline, descent and shaped advances.");
        });
        regression("graphemes-and-arabic-joining", () =>
        {
            string text = "\u0633\u0644\u0627\u0645\u0633\u0644\u0627\u0645";
            var colored = new RunProperties(properties.Typeface, properties.FontRenderingEmSize, Brushes.Blue);
            using var whole = formatter.FormatLine(new Source(text, properties), 0, 1000, new Paragraph(properties), null);
            using var split = formatter.FormatLine(new Source(new[] { (text[..1], properties), (text[1..], colored) }), 0, 1000, new Paragraph(properties), null);
            require(whole.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices).SequenceEqual(split.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)),
                "arabic-joining-across-formatting-runs", "HarfBuzz receives line context across a brush change so Arabic joining forms survive separate real WPF source runs.");
            var source = new Source(text, properties);
            using var line = formatter.FormatLine(source, 0, 45, new Paragraph(properties, TextWrapping.Wrap), null);
            string prefix = text[..line.Length];
            using var reshaped = formatter.FormatLine(new Source(prefix, properties), 0, 1000, new Paragraph(properties), null);
            require(line.Length < text.Length && line.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)
                .SequenceEqual(reshaped.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.GlyphIndices)),
                "arabic-wrap-boundary-reshape", "A broken Arabic word is shaped again with the actual line boundary and correct joining forms.");
            using var clustered = formatter.FormatLine(new Source("q\u0301\U0001f600X", properties), 0, 1000, new Paragraph(properties), null);
            CharacterHit first = clustered.GetNextCaretCharacterHit(new CharacterHit(0, 0));
            CharacterHit second = clustered.GetNextCaretCharacterHit(first);
            require(first.FirstCharacterIndex + first.TrailingLength == 2 && second.FirstCharacterIndex + second.TrailingLength == 4 &&
                clustered.GetPreviousCaretCharacterHit(second).FirstCharacterIndex == 2,
                "unicode-grapheme-caret-pairs", "Combining marks and surrogate pairs remain whole through forward and backward caret navigation.");
        });
        regression("retained-rtl-pixels-and-glyph-geometry", () =>
        {
            foreach (FlowDirection direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
                foreach (TextFormattingMode mode in new[] { TextFormattingMode.Ideal, TextFormattingMode.Display })
                {
                    var local = TextFormatter.Create(mode);
                    var source = new Source("abc \u0633\u0644\u0627\u0645 123 \u05e9\u05dc\u05d5\u05dd", properties) { PixelsPerDip = 1.5 };
                    var line = local.FormatLine(source, 0, 280, new Paragraph(properties, flow: direction), null);
                    var visual = new DrawingVisual();
                    Rect geometry = Rect.Empty;
                    foreach (var run in line.GetIndexedGlyphRuns()) geometry.Union(run.GlyphRun.BuildGeometry().Bounds);
                    geometry.Offset(8, 8);
                    using (var context = visual.RenderOpen()) line.Draw(context, new Point(8, 8), InvertAxes.None);
                    line.Dispose();
                    local.Dispose();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    using var target = new Target(visual, 480, 140, new DpiScale(1.5, 2));
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    using var bitmap = target.Snapshot();
                    int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
                    for (int y = 0; y < bitmap.Height; y++)
                        for (int x = 0; x < bitmap.Width; x++)
                            if (bitmap.GetPixel(x, y).Alpha != 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
                    require(right > left && bottom > top && Math.Abs(left - geometry.Left * 1.5) < 4 && Math.Abs(right - geometry.Right * 1.5) < 4 &&
                        Math.Abs(top - geometry.Top * 2) < 4 && Math.Abs(bottom - geometry.Bottom * 2) < 4,
                        $"retained-rtl-geometry-pixels-{direction}-{mode}", "After line/formatter disposal and collection, actual RTL glyph origins agree with GlyphRun.BuildGeometry and retained pixels at asymmetric DPI.");
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"rtl-{direction}-{mode}.png"));
                    data.SaveTo(output);
                }
            for (int repeat = 0; repeat < 256; repeat++)
            {
                using var local = TextFormatter.Create();
                using var line = local.FormatLine(new Source("\u0633\u0644\u0627\u0645 123 \u05e9\u05dc\u05d5\u05dd", properties), 0, 280, new Paragraph(properties), null);
                if (line.Width <= 0) throw new InvalidOperationException("Repeated native font/shaper allocation lost output.");
            }
            require(true, "repeated-native-shaping-disposal", "256 independent formatter/line lifetimes dispose temporary fonts, shapers and buffers while immutable glyph faces remain usable.");
        });
        regression("stock-wpf-glyph-position-contract", () =>
        {
            using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StockPositionReference.json")));
            using var actual = JsonDocument.Parse(JsonSerializer.Serialize(TextOracleCases.CapturePositions()));
            var expectedCases = reference.RootElement.GetProperty("positionCases").EnumerateArray().ToArray();
            var actualCases = actual.RootElement.EnumerateArray().ToArray();
            require(expectedCases.Length == 8 && actualCases.Length == 8, "stock-glyph-position-case-count", "Eight stock WPF glyph-position cases cover LTR/RTL, Ideal/Display and two DPI values.");
            for (int index = 0; index < expectedCases.Length; index++)
            {
                bool matches = new[] { "X", "Y", "Width", "Height" }.All(name => Math.Abs(expectedCases[index].GetProperty("geometry").GetProperty(name).GetDouble() -
                    actualCases[index].GetProperty("geometry").GetProperty(name).GetDouble()) < reference.RootElement.GetProperty("toleranceDip").GetDouble());
                require(matches, "stock-glyph-position-" + index, "Fixed-font explicit glyph advances, combining-glyph offsets and RTL nominal-advance rounding agree with actual stock WPF geometry within0.03DIP.");
            }
        });
    }
}
