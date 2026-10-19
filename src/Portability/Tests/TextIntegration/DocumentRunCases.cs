using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using SkiaSharp;
using System.Windows.Threading;

static class DocumentRunCases
{
    internal static void Verify(TextFormatter formatter, Action<bool, string, string> require)
    {
        var properties = new HiddenRunCases.Properties();
        var paragraph = new HiddenRunCases.Paragraph(properties);
        var outer = new Modifier(Brushes.Red, TextDecorations.Strikethrough);
        var inner = new Modifier(Brushes.Blue, TextDecorations.Underline);
        var first = new TextCharacters("MMMM", properties);
        var middle = new TextCharacters("WWWW", properties);
        var last = new TextCharacters("ZZZZ", properties);
        var source = new RunSource(outer, first, inner, middle, new TextEndOfSegment(1), last, new TextEndOfSegment(1), new TextCharacters("tail", properties));
        using var line = formatter.FormatLine(source, 0, 1000, paragraph, null!);
        var spans = line.GetTextRunSpans();
        require(line.Length == source.Length + 1 && spans.Any(span => ReferenceEquals(span.Value, outer)) &&
            spans.Any(span => ReferenceEquals(span.Value, inner)) && spans.Any(span => ReferenceEquals(span.Value, first)) &&
            spans.Sum(span => span.Length) == line.Length,
            "document-modifier-original-source-spans", "Nested modifier boundaries consume original source symbols while spans retain the actual caller runs.");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) line.Draw(context, new Point(4, 4), InvertAxes.None);
        var glyphs = Drawings(visual.Drawing).OfType<GlyphRunDrawing>().ToArray();
        require(glyphs.Length == 4 && ReferenceEquals(glyphs[0].ForegroundBrush, Brushes.Red) &&
            ReferenceEquals(glyphs[1].ForegroundBrush, Brushes.Red) && ReferenceEquals(glyphs[2].ForegroundBrush, Brushes.Red) &&
            ReferenceEquals(glyphs[3].ForegroundBrush, Brushes.Black),
            "document-modifier-nesting-and-restoration", "Real ModifyProperties runs inner then outer, restores closed scopes and leaves following content unchanged.");
        require(Drawings(visual.Drawing).OfType<GeometryDrawing>().Count() >= 4,
            "document-modifier-visible-decorations", "Strikethrough and nested underline scopes create genuine retained decoration geometry.");
        foreach (var reference in new[] { (TextDecorationLocation.OverLine, 0.5983333333, 1.1966666667),
            (TextDecorationLocation.Underline, 22.22, 1.0533333333), (TextDecorationLocation.Strikethrough, 15.4683333333, 1.1966666667),
            (TextDecorationLocation.Baseline, 21.6783333333, 1.1966666667) })
        {
            using var decorated = formatter.FormatLine(new RunSource(new Modifier(Brushes.Black, new TextDecorationCollection {
                new TextDecoration { Location = reference.Item1 } }), new TextCharacters("x", properties), new TextEndOfSegment(1)), 0, 1000, paragraph, null!);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) decorated.Draw(context, new Point(), InvertAxes.None);
            Rect bounds = Drawings(drawing.Drawing).OfType<GeometryDrawing>().Single().Geometry.Bounds;
            require(Math.Abs(bounds.Y - reference.Item2) < 0.01 && Math.Abs(bounds.Height - reference.Item3) < 0.01,
                "document-decoration-stock-Ideal-metrics-" + reference.Item1,
                "Pinned-font size 24 Ideal decoration geometry matches stock within the measured backend metric allowance of 0.01 DIP; actualY=" + bounds.Y + ";height=" + bounds.Height);
        }
        var capDecoration = new TextDecoration { Location = TextDecorationLocation.Baseline,
            Pen = new Pen(Brushes.Red, 4) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square },
            PenThicknessUnit = TextDecorationUnit.Pixel, PenOffsetUnit = TextDecorationUnit.Pixel };
        using (var capLine = formatter.FormatLine(new RunSource(new Modifier(Brushes.Red, new TextDecorationCollection { capDecoration }),
            new TextCharacters(" ", properties), new TextEndOfSegment(1)), 0, 1000, paragraph, null!))
        {
            var capVisual = new DrawingVisual();
            using (var context = capVisual.RenderOpen()) capLine.Draw(context, new Point(), InvertAxes.None);
            Rect capBounds = VisualTreeHelper.GetContentBounds(capVisual);
            require(Near(capLine.OverhangLeading, capBounds.Left) && Near(capLine.OverhangTrailing, capLine.Width - capBounds.Right) &&
                Near(capLine.Extent, capBounds.Height) && capBounds.Left < 0,
                "document-decoration-pen-ink-matches-retained-geometry", "Decoration pen caps contribute the same real overhang and ink extent as their retained drawing geometry.");
        }
        var objectDecoration = new TextDecoration { Location = TextDecorationLocation.Baseline,
            Pen = new Pen(Brushes.Red, 4), PenThicknessUnit = TextDecorationUnit.Pixel,
            PenOffsetUnit = TextDecorationUnit.Pixel, PenOffset = 8 };
        using (var decoratedObject = formatter.FormatLine(new RunSource(new Modifier(Brushes.Black,
            new TextDecorationCollection { objectDecoration }), new ObjectRun(properties), new TextEndOfSegment(1)),
            0, 1000, paragraph, null!))
        {
            var decoratedVisual = new DrawingVisual();
            using (var context = decoratedVisual.RenderOpen()) decoratedObject.Draw(context, new Point(), InvertAxes.None);
            Rect retainedBounds = VisualTreeHelper.GetContentBounds(decoratedVisual);
            require(Near(decoratedObject.Extent, retainedBounds.Height) && Near(decoratedObject.OverhangAfter, retainedBounds.Bottom - decoratedObject.Height) &&
                retainedBounds.Bottom > decoratedObject.Height,
                "document-object-decoration-ink-matches-retained-geometry",
                "A decoration outside the embedded object's own box contributes the actual retained ink extent and overhang.");
        }
        var item = new ObjectRun(properties);
        var objectSource = new RunSource(new TextHidden(1), new TextCharacters("A", properties), item, new TextCharacters("B", properties), new TextHidden(1));
        using var objectLine = formatter.FormatLine(objectSource, 0, 1000, paragraph, null!);
        using var text = formatter.FormatLine(new RunSource(new TextCharacters("AB", properties)), 0, 1000, paragraph, null!);
        require(objectLine.Length == 6 && Near(objectLine.Width, text.Width + 18) && objectLine.Baseline >= 24 && objectLine.Height >= 30 &&
            objectLine.GetTextRunSpans().Any(span => ReferenceEquals(span.Value, item)) &&
            new string(objectLine.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters).ToArray()) == "AB",
            "document-object-real-metrics-and-source", "A genuine TextEmbeddedObject contributes its measured advance and baseline without manufacturing a glyph or losing original identity.");
        var objectBounds = objectLine.GetTextBounds(2, 1);
        require(objectBounds.Count == 1 && Near(objectBounds[0].Rectangle.Width, 18) &&
            objectBounds[0].TextRunBounds!.Single().TextRun == item &&
            Position(objectLine.GetNextCaretCharacterHit(new CharacterHit(2, 0))) == 3 &&
            Position(objectLine.GetPreviousCaretCharacterHit(new CharacterHit(3, 0))) == 2,
            "document-object-source-hit-caret", "Both sides of the embedded object use its source symbol and real layout width for bounds and caret navigation.");
        var objectVisual = new DrawingVisual();
        using (var context = objectVisual.RenderOpen()) objectLine.Draw(context, new Point(4, 4), InvertAxes.None);
        using (var target = new Target(objectVisual, 180, 90))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            using var bitmap = target.Snapshot();
            int x = (int)Math.Ceiling(objectLine.GetDistanceFromCharacterHit(new CharacterHit(2, 0))) + 7;
            int y = (int)Math.Ceiling(objectLine.Baseline - 24) + 7;
            require(item.DrawCount == 1 && bitmap.GetPixel(x, y) == new SKColor(255, 165, 0),
                "document-object-actual-retained-pixels", "The actual object Draw callback paints its orange content at the line's object metrics and baseline.");
        }
    }
    internal static void VerifyBoundaries(TextFormatter formatter, Action<bool, string, string> require)
    {
        var properties = new HiddenRunCases.Properties();
        var desired = new ObjectRun(properties);
        var desiredSource = new RunSource(new TextCharacters("A", properties), desired, new TextCharacters("B", properties));
        var wrapping = new HiddenRunCases.Paragraph(properties, TextWrapping.WrapWithOverflow);
        int first = 0;
        var lengths = new List<int>();
        TextLineBreak? previous = null;
        try
        {
            do
            {
                using var line = formatter.FormatLine(desiredSource, first, 20, wrapping, previous!);
                previous?.Dispose();
                previous = line.GetTextLineBreak();
                if (lengths.Count > 5 || line.Length <= 0) throw new InvalidOperationException("Object wrapping made no progress.");
                lengths.Add(line.Length);
                first += line.Length;
            } while (first < desiredSource.Length + 1);
        }
        finally { previous?.Dispose(); }
        require(lengths.SequenceEqual(new[] { 1, 1, 2 }) && first == 4,
            "document-object-break-desired-both-sides", "The actual inventory object's before and after BreakDesired contract matches stock source lengths across all three lines.");
        foreach (var condition in new[] { LineBreakCondition.BreakPossible, LineBreakCondition.BreakAlways, LineBreakCondition.BreakRestrained })
        {
            bool rejected = false;
            try { using var line = formatter.FormatLine(new RunSource(new ObjectRun(properties, condition)), 0, 20, wrapping, null!); }
            catch (NotSupportedException exception) { rejected = exception.Message.Contains("BreakDesired", StringComparison.Ordinal); }
            require(rejected, "document-object-unsupported-break-" + condition, "Unqualified object break conditions are diagnosed by name instead of silently adopting Unicode object-break behavior.");
        }
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var item = new ObjectRun(properties);
            using var line = formatter.FormatLine(new RunSource(new TextCharacters("A", properties), item, new TextCharacters("B", properties)),
                0, 1000, new HiddenRunCases.Paragraph(properties, flow: flow), null!);
            var bounds = line.GetTextBounds(1, 1).Single();
            double left = line.GetDistanceFromCharacterHit(new CharacterHit(1, 0));
            double right = line.GetDistanceFromCharacterHit(new CharacterHit(2, 0));
            var hit = line.GetCharacterHitFromDistance((left + right) / 2);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
            require(line.Length == 4 && Near(bounds.Rectangle.Width, 18) && hit.FirstCharacterIndex == 1 &&
                Position(line.GetNextCaretCharacterHit(new CharacterHit(1, 0))) == 2 &&
                Position(line.GetPreviousCaretCharacterHit(new CharacterHit(2, 0))) == 1 && item.LastRightToLeft == (flow == FlowDirection.RightToLeft),
                "document-object-atomic-source-and-direction-" + flow, "A one-symbol object stays atomic through both caret directions, distance hits and actual Draw flow direction.");
        }
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var small = new ObjectRun(properties, size: 1);
            using var mixed = formatter.FormatLine(new RunSource(new TextCharacters("A", properties), small, new TextCharacters("B", properties)),
                0, 1000, new HiddenRunCases.Paragraph(properties, flow: flow), null!);
            var outerBounds = mixed.GetTextBounds(1, 1).Single();
            var runBounds = outerBounds.TextRunBounds!.Single();
            require(Near(outerBounds.Rectangle.Width, 1) && Near(outerBounds.Rectangle.Height, mixed.Height) &&
                Near(runBounds.Rectangle.Width, 1) && Near(runBounds.Rectangle.Height, 1) && Near(runBounds.Rectangle.Y, mixed.Baseline - 1) &&
                runBounds.TextSourceCharacterIndex == 1 && runBounds.Length == 1 && ReferenceEquals(runBounds.TextRun, small),
                "document-small-object-run-geometry-" + flow,
                "Stock selection geometry keeps a full-line outer box but the original small object's own height and baseline offset in TextRunBounds.");
        }
        bool multiSymbolRejected = false;
        try { using var unsupported = formatter.FormatLine(new RunSource(new ObjectRun(properties, length: 3)), 0, 1000, new HiddenRunCases.Paragraph(properties), null!); }
        catch (NotSupportedException exception) { multiSymbolRejected = exception.Message.Contains("one source symbol", StringComparison.Ordinal); }
        require(multiSymbolRejected, "document-object-multi-symbol-profile-is-explicit",
            "Unrequired multi-symbol Line Services object-range behavior is rejected rather than inferred or silently approximated.");
        using (var adjacentMark = formatter.FormatLine(new RunSource(new ObjectRun(properties), new TextCharacters("\u0301A", properties)),
            0, 1000, new HiddenRunCases.Paragraph(properties), null!))
            require(adjacentMark.Length == 4 && new string(adjacentMark.GetIndexedGlyphRuns().SelectMany(run => run.GlyphRun.Characters).ToArray()) == "\u0301A",
                "document-object-adjacent-combining-mark", "An embedded object remains atomic and cannot consume a following visible combining character as part of its grapheme.");
        using (var pixelObject = formatter.FormatLine(new RunSource(new ObjectRun(properties, size: 1)), 0, 1000, new HiddenRunCases.Paragraph(properties), null!))
            require(pixelObject.Width == 1 && pixelObject.Height == 1 && pixelObject.Baseline == 1 && pixelObject.Extent == 1,
                "document-object-one-pixel-only-line", "The declared composite's one-pixel isolated object retains its stock measured line metrics without an invented font-height minimum.");
        using (var empty = formatter.FormatLine(new RunSource(), 0, 1000, new HiddenRunCases.Paragraph(properties), null!))
        using (var zeroObject = formatter.FormatLine(new RunSource(new ObjectRun(properties, size: 0)), 0, 1000, new HiddenRunCases.Paragraph(properties), null!))
            require(zeroObject.Width == 0 && zeroObject.Extent == 0 && !zeroObject.GetIndexedGlyphRuns().Any() &&
                Near(zeroObject.Height, empty.Height) && Near(zeroObject.Baseline, empty.Baseline) && zeroObject.Length == 2,
                "document-object-zero-size-empty-line-metrics", "A valid zero-sized object keeps its source symbol and restores stock empty-line font metrics without visible padding glyphs.");
        var throwing = new ObjectRun(properties) { ThrowOnDraw = true };
        using var failingLine = formatter.FormatLine(new RunSource(throwing), 0, 1000, new HiddenRunCases.Paragraph(properties), null!);
        var recovered = new DrawingVisual();
        bool caught = false;
        using (var context = recovered.RenderOpen())
        {
            try { failingLine.Draw(context, new Point(100, 100), InvertAxes.None); }
            catch (InvalidOperationException) { caught = true; }
            context.DrawRectangle(Brushes.Blue, null, new Rect(2, 2, 8, 8));
        }
        using (var target = new Target(recovered, 40, 40))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            using var bitmap = target.Snapshot();
            require(caught && bitmap.GetPixel(4, 4) == SKColors.Blue && bitmap.GetPixel(20, 20).Alpha == 0,
                "document-object-drawing-exception-restores-context", "An actual object Draw failure unwinds the line transform and permits later retained drawing at the caller's coordinates. caught=" + caught + ";inside=" + bitmap.GetPixel(4, 4) + ";outside=" + bitmap.GetPixel(20, 20));
        }
    }
    private static bool Near(double left, double right) => Math.Abs(left - right) < 0.001;
    private static int Position(CharacterHit hit) => hit.FirstCharacterIndex + hit.TrailingLength;
    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) foreach (Drawing nested in Drawings(child)) yield return nested;
    }
    private sealed class RunSource(params TextRun[] runs) : TextSource
    {
        public int Length => runs.Sum(run => run.Length);
        public override TextRun GetTextRun(int index)
        {
            foreach (var run in runs)
            {
                if (index == 0) return run;
                if (index < run.Length) throw new InvalidOperationException("This source case starts formatting only at its recorded run boundaries.");
                index -= run.Length;
            }
            return new TextEndOfParagraph(1);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new(0, new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
    private sealed class Modifier(Brush foreground, TextDecorationCollection decorations) : TextModifier
    {
        public override int Length => 1;
        public override TextRunProperties Properties => null!;
        public override bool HasDirectionalEmbedding => false;
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextRunProperties ModifyProperties(TextRunProperties properties) => new ModifiedProperties(properties, foreground, decorations);
    }
    private sealed class ModifiedProperties(TextRunProperties original, Brush foreground, TextDecorationCollection decorations) : TextRunProperties
    {
        public override Typeface Typeface => original.Typeface;
        public override double FontRenderingEmSize => original.FontRenderingEmSize;
        public override double FontHintingEmSize => original.FontHintingEmSize;
        public override Brush ForegroundBrush => foreground;
        public override Brush BackgroundBrush => original.BackgroundBrush;
        public override CultureInfo CultureInfo => original.CultureInfo;
        public override TextEffectCollection TextEffects => original.TextEffects;
        public override TextDecorationCollection TextDecorations { get; } = new TextDecorationCollection(decorations.Concat(original.TextDecorations ?? new TextDecorationCollection()));
    }
    private sealed class ObjectRun(TextRunProperties properties, LineBreakCondition condition = LineBreakCondition.BreakDesired, int length = 1, double size = 30) : TextEmbeddedObject
    {
        internal int DrawCount;
        internal bool ThrowOnDraw;
        internal bool LastRightToLeft;
        public override int Length => length;
        public override TextRunProperties Properties => properties;
        public override CharacterBufferReference CharacterBufferReference => new();
        public override LineBreakCondition BreakBefore => condition;
        public override LineBreakCondition BreakAfter => condition;
        public override bool HasFixedSize => true;
        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(size == 30 ? 18 : size, size, size == 30 ? 24 : size);
        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, size == 30 ? -24 : -size, size == 30 ? 18 : size, size);
        public override void Draw(DrawingContext context, Point origin, bool rightToLeft, bool sideways)
        {
            DrawCount++;
            LastRightToLeft = rightToLeft;
            if (ThrowOnDraw) throw new InvalidOperationException("Owned object Draw failure.");
            context.DrawRectangle(Brushes.Orange, null, new Rect(origin.X, origin.Y - (size == 30 ? 24 : size), size == 30 ? 18 : size, size));
        }
    }
}
