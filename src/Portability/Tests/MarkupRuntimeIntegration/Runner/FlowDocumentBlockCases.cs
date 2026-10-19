using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string SectionListLayout()
    {
        _ = Application.Current ?? new Application();
        var first = new Run("FIRST"); var last = new Run("LAST");
        var section = new Section(new Paragraph(first) { Margin = new Thickness(0, 13, 0, 17) })
        { Margin = new Thickness(0, 7, 0, 11), Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(5, 0, 0, 0), BorderBrush = Brushes.Red };
        var document = BlockDocument(); document.Blocks.Add(section); document.Blocks.Add(new Paragraph(last) { Margin = new Thickness(0, 19, 0, 0) });
        var viewer = BlockViewer(document);
        try
        {
            Layout(viewer, 380);
            Rect start = first.ContentStart.GetCharacterRect(LogicalDirection.Forward), following = last.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(Math.Abs(start.X - 23) < 0.05 && Math.Abs(start.Y - 13) < 0.05 && Math.Abs(following.Y - (start.Bottom + 41)) < 0.05,
                $"Padded Section spacing differs: {start}, {following}.");
            byte[] original = Raster(viewer); Require(ColorPixels(original, Colors.Red) > 100, "Original Section left border has no red ink.");
            section.Padding = new Thickness(0); section.BorderThickness = new Thickness(0); Layout(viewer, 380);
            Rect collapsed = first.ContentStart.GetCharacterRect(LogicalDirection.Forward), collapsedFollowing = last.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(Math.Abs(collapsed.X - 8) < 0.05 && Math.Abs(collapsed.Y - 8) < 0.05 && Math.Abs(collapsedFollowing.Y - collapsed.Bottom - 19) < 0.05,
                "Unpadded Section did not collapse its child/outer margins.");
            section.Padding = new Thickness(10, 5, 10, 5); section.BorderThickness = new Thickness(5, 0, 0, 0); Layout(viewer, 380);
            Require(Raster(viewer).SequenceEqual(original), "Section padding/border restore retained stale ink.");

            var nestedRun = new Run("NESTED");
            var nested = new System.Windows.Documents.List(new ListItem(new Paragraph(nestedRun) { Margin = new Thickness(0) }))
            { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 3, Margin = new Thickness(0), Padding = new Thickness(35, 0, 0, 0) };
            var firstRun = new Run("ALPHA beta gamma delta epsilon zeta eta theta iota kappa lambda mu."); var finalRun = new Run("OMEGA");
            var item = new ListItem(new Paragraph(firstRun) { Margin = new Thickness(0) }); item.Blocks.Add(nested);
            var list = new System.Windows.Documents.List(item) { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 9, Margin = new Thickness(0), Padding = new Thickness(55, 0, 0, 0) };
            list.ListItems.Add(new ListItem(new Paragraph(finalRun) { Margin = new Thickness(0) }));
            section.Blocks.Clear(); section.Blocks.Add(list); document.Blocks.Remove(last.ContentStart.Paragraph!); Layout(viewer, 380);
            string text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Require(text.StartsWith("9.\tALPHA", StringComparison.Ordinal) && text.Contains("3.\tNESTED", StringComparison.Ordinal) && text.Contains("10.\tOMEGA", StringComparison.Ordinal), "Original nested list range serialization differs.");
            viewer.Selection.Select(firstRun.ContentStart, finalRun.ContentEnd); string forward = viewer.Selection.Text;
            viewer.Selection.Select(finalRun.ContentEnd, firstRun.ContentStart); Require(viewer.Selection.Text == forward, "Reverse nested list selection differs.");
            viewer.Selection.Select(firstRun.ContentStart, firstRun.ContentStart);
            Require(ReferenceEquals(HitFirstRun(viewer, nestedRun), nestedRun), "Nested list text hit lost original Run identity.");
            TextPointer end = firstRun.ContentEnd.GetLineStartPosition(0)!;
            TextPointer next = end.GetLineStartPosition(1, out int moved)!;
            Require(moved == 1 && next.Paragraph == nestedRun.ContentStart.Paragraph && next.GetLineStartPosition(-1, out int back)!.CompareTo(end) == 0 && back == -1,
                "Nested list forward/reverse line navigation inserted a marker line or skipped content.");
            int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
            Rect wide = finalRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward); byte[] wideInk = Raster(viewer);
            Layout(viewer, 210); Require(finalRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Y > wide.Y, "Nested list content did not rewrap.");
            Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(wideInk) && document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols, "Nested list resize changed original symbols or retained stale ink.");
            return JsonSerializer.Serialize(new { start = start.ToString(), following = following.ToString(), collapsed = collapsed.ToString(), collapsedFollowing = collapsedFollowing.ToString(), text, symbols });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    internal static string ListMarkers()
    {
        _ = Application.Current ?? new Application();
        var observations = new List<object>();
        foreach (var test in new[] { (TextMarkerStyle.Decimal, 1, "1.", "2."), (TextMarkerStyle.Decimal, 9, "9.", "10."), (TextMarkerStyle.LowerLatin, 26, "z.", "aa."), (TextMarkerStyle.Decimal, int.MaxValue, "2147483647.", "2147483647.") })
        {
            var first = new Run("ALPHA"); var last = new Run("OMEGA");
            var list = new System.Windows.Documents.List(new ListItem(new Paragraph(first) { Margin = new Thickness(0) }))
            { MarkerStyle = test.Item1, StartIndex = test.Item2, Margin = new Thickness(0) };
            list.ListItems.Add(new ListItem(new Paragraph(last) { Margin = new Thickness(0) }));
            var document = BlockDocument(); document.Blocks.Add(list); var viewer = BlockViewer(document);
            try
            {
                Layout(viewer, 380);
                string expected = test.Item3 + "\tALPHA" + Environment.NewLine + test.Item4 + "\tOMEGA" + Environment.NewLine;
                if (test.Item2 != int.MaxValue)
                    Require(new TextRange(document.ContentStart, document.ContentEnd).Text == expected, "Numbered range serialization differs at " + test.Item2);
                var labels = BlockGlyphs(viewer).Where(g => g.Text != "ALPHA" && g.Text != "OMEGA" && !string.IsNullOrWhiteSpace(g.Text)).ToArray();
                Require(labels.Length == 2 && labels[0].Text == test.Item3 && labels[1].Text == test.Item4, "Actual numbered marker glyphs differ at " + test.Item2);
                Rect text = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                double gap = text.Left - labels[0].Baseline.X - labels[0].Width;
                Require(Math.Abs(gap - text.Height / 2) < 0.05, "Default marker offset differs.");
                Require(BlockMarkerInk(viewer, labels[0], text.Height, Colors.Black) > 2, "Numbered marker has a drawing record but no actual ink.");
                var textViewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
                var textView = ((IServiceProvider)BlockScope(viewer)).GetService(textViewType)!;
                var markerPoint = new Point(labels[0].Baseline.X + labels[0].Width / 2, labels[0].Baseline.Y - text.Height / 3);
                var markerHit = (TextPointer)textViewType.GetMethod("GetTextPositionFromPoint")!.Invoke(textView, new object[] { markerPoint, true })!;
                Require(markerHit.Paragraph == first.ContentStart.Paragraph && markerHit.CompareTo(first.ContentStart) >= 0 && markerHit.CompareTo(first.ContentEnd) <= 0,
                    "Snapping marker-area hit invented a pointer outside the original item paragraph.");
                byte[] original = Raster(viewer); list.MarkerOffset = 3; Layout(viewer, 380);
                var shifted = BlockGlyphs(viewer).First(g => g.Text == test.Item3);
                Require(Math.Abs(first.ContentStart.GetCharacterRect(LogicalDirection.Forward).X - text.X) < 0.05 && Math.Abs(text.Left - shifted.Baseline.X - shifted.Width - 3) < 0.05,
                    "Explicit marker offset moved text or retained the default marker gap.");
                list.MarkerOffset = double.NaN; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Marker offset restore retained stale ink.");
                if (test.Item1 == TextMarkerStyle.Decimal && test.Item2 == 9)
                {
                    list.StartIndex = 99; Layout(viewer, 380); Require(first.ContentStart.GetCharacterRect(LogicalDirection.Forward).X > text.X + 10, "Auto list padding ignored wider 100. marker.");
                    list.StartIndex = 9; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "Numbering/padding restore retained stale ink.");
                }
                observations.Add(new { test.Item1, test.Item2, rangeChecked = test.Item2 != int.MaxValue, expected, gap, labels = labels.Select(g => g.Text).ToArray() });
            }
            finally { viewer.Document = null; Layout(viewer, 380); }
        }
        foreach (TextMarkerStyle style in new[] { TextMarkerStyle.Disc, TextMarkerStyle.Circle, TextMarkerStyle.Square, TextMarkerStyle.Box })
        {
            var run = new Run("SYMBOL"); var list = new System.Windows.Documents.List(new ListItem(new Paragraph(run) { Margin = new Thickness(0) })) { MarkerStyle = style, Margin = new Thickness(0) };
            var document = BlockDocument(); document.Blocks.Add(list); var viewer = BlockViewer(document);
            try
            {
                Layout(viewer, 380); var glyph = BlockGlyphs(viewer).Single(g => g.Text != "SYMBOL" && !string.IsNullOrWhiteSpace(g.Text));
                string expected = style switch { TextMarkerStyle.Disc => "\u2022", TextMarkerStyle.Circle => "\u25cb", TextMarkerStyle.Square => "\u25a1", _ => "\u25a0" };
                bool portable = typeof(FlowDocument).Assembly.GetType("MS.Internal.Documents.PortableDocumentLayout") != null;
                Require(!portable || glyph.Text == expected && !glyph.Font.Contains("wingding", StringComparison.OrdinalIgnoreCase), "Portable symbol did not use its Unicode equivalent in the list font.");
                double height = run.ContentStart.GetCharacterRect(LogicalDirection.Forward).Height;
                Require(BlockMarkerInk(viewer, glyph, height, Colors.Black) > 2, "Symbol marker has no actual ink.");
                byte[] original = Raster(viewer); list.Foreground = Brushes.Red; Layout(viewer, 380);
                int redMarkerPixels = BlockMarkerInk(viewer, glyph, height, Colors.Red);
                Require(redMarkerPixels > 2 && !Raster(viewer).SequenceEqual(original), "List foreground mutation did not reach the marker's own ink region.");
                list.Foreground = Brushes.Black; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(original), "List foreground restore retained stale ink.");
                observations.Add(new { style, glyph.Text, glyph.Font, glyph.Width, redMarkerPixels });
            }
            finally { viewer.Document = null; Layout(viewer, 380); }
        }
        return JsonSerializer.Serialize(observations);
    }

    internal static string ListBaselines()
    {
        _ = Application.Current ?? new Application();
        var observations = new List<object>();
        foreach (string kind in new[] { "child", "nested", "empty", "section", "empty-list" })
        {
            var child = new Border { Height = 30, Background = Brushes.Blue };
            var last = new Run("LAST"); var item = new ListItem();
            if (kind == "child") item.Blocks.Add(new BlockUIContainer(child) { Margin = new Thickness(0) });
            if (kind == "nested") item.Blocks.Add(new System.Windows.Documents.List(new ListItem(new Paragraph(new Run("NESTED")) { Margin = new Thickness(0) })) { StartIndex = 3, MarkerStyle = TextMarkerStyle.Decimal, Margin = new Thickness(0) });
            if (kind == "section") item.Blocks.Add(new Section());
            if (kind == "empty-list") item.Blocks.Add(new System.Windows.Documents.List { Margin = new Thickness(0) });
            var list = new System.Windows.Documents.List(item) { StartIndex = 9, MarkerStyle = TextMarkerStyle.Decimal, Margin = new Thickness(0), Padding = new Thickness(55, 0, 0, 0) };
            list.ListItems.Add(new ListItem(new Paragraph(last) { Margin = new Thickness(0) }));
            var document = BlockDocument(); document.Blocks.Add(list); var viewer = BlockViewer(document);
            try
            {
                Layout(viewer, 380); var glyphs = BlockGlyphs(viewer); var marker = glyphs.Single(g => g.Text == "9."); var following = glyphs.Single(g => g.Text == "LAST");
                if (kind == "child")
                {
                    var scope = BlockScope(viewer); double bottom = child.TransformToAncestor(scope).Transform(new Point()).Y + child.RenderSize.Height;
                    Require(Math.Abs(marker.Baseline.Y - bottom) < 0.05, "Child-first marker baseline did not use the child's bottom.");
                }
                else if (kind == "nested") Require(Math.Abs(marker.Baseline.Y - glyphs.Single(g => g.Text == "3.").Baseline.Y) < 0.05, "Nested-first marker baseline differs from first nested child.");
                else if (kind == "empty-list") Require(Math.Abs(marker.Baseline.Y - 8) < 0.05 && Math.Abs(last.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y - 8) < 0.05, "Empty List unexpectedly created an empty text line.");
                else Require(Math.Abs(following.Baseline.Y - marker.Baseline.Y - last.ContentStart.GetCharacterRect(LogicalDirection.Forward).Height) < 0.05, "Empty ListItem/Section did not retain its empty text line.");
                observations.Add(new { kind, marker = marker.Baseline.ToString(), following = following.Baseline.ToString() });
                list.FlowDirection = FlowDirection.RightToLeft; list.Padding = new Thickness(31, 4, 7, 6); Layout(viewer, 380);
                var rtlMarker = BlockGlyphs(viewer).First(g => g.Text == "9" || g.Text == "9.");
                Require(Math.Abs(rtlMarker.Baseline.Y - marker.Baseline.Y - 4) < 0.05, "RTL marker did not retain the calibrated first-child baseline plus top padding.");
                Require(Math.Abs(last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).X - (BlockScope(viewer).RenderSize.Width - 39)) < 0.05,
                    $"RTL list did not mirror its asymmetric padding to the right content edge: kind={kind}, end={last.ContentEnd.GetCharacterRect(LogicalDirection.Backward)}, scope={BlockScope(viewer).RenderSize}.");
            }
            finally { viewer.Document = null; Layout(viewer, 380); }
        }
        return JsonSerializer.Serialize(observations);
    }

    internal static string NestedBlockFailure()
    {
        _ = Application.Current ?? new Application();
        foreach (bool failMeasure in new[] { false, true }) foreach (bool widen in new[] { false, true })
        {
            var retained = new Border { Background = Brushes.Blue, Child = new Viewbox { Child = new Border { Width = 400, Height = 50, Background = Brushes.Lime } } };
            var item = new ListItem(); item.Blocks.Add(new BlockUIContainer(retained) { Margin = new Thickness(0) });
            var list = new System.Windows.Documents.List(item) { MarkerStyle = TextMarkerStyle.Box, Margin = new Thickness(0) };
            var section = new Section(list) { Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(5, 0, 0, 0), BorderBrush = Brushes.Red };
            var document = BlockDocument(); document.Blocks.Add(section); var viewer = BlockViewer(document); double width = widen ? 210 : 380;
            try
            {
                Layout(viewer, width); var parent = VisualTreeHelper.GetParent(retained); Size desired = retained.DesiredSize, render = retained.RenderSize; Vector offset = VisualTreeHelper.GetOffset(retained); byte[] ink = Raster(viewer);
                var rejected = new BlockFailureChild(failMeasure); var block = new BlockUIContainer(rejected); item.Blocks.Add(block);
                bool caught = false; try { Layout(viewer, widen ? 380 : 210); } catch (InvalidOperationException error) when (error.Message == "expected nested child failure") { caught = true; }
                Require(caught && ReferenceEquals(parent, VisualTreeHelper.GetParent(retained)) && VisualTreeHelper.GetParent(rejected) == null && retained.DesiredSize == desired && retained.RenderSize == render && VisualTreeHelper.GetOffset(retained) == offset && Raster(viewer).SequenceEqual(ink), "Nested failed layout changed committed child ownership, geometry or ink.");
                item.Blocks.Remove(block); Layout(viewer, width); Require(Raster(viewer).SequenceEqual(ink), "Nested child failure did not recover.");
            }
            finally { viewer.Document = null; Layout(viewer, width); }
        }
        return "Nested Section/List child measure and arrange failures preserve committed ownership/geometry/ink in both resize directions and recover after removal.";
    }

    private static FlowDocument BlockDocument() => new FlowDocument { FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans"), FontSize = 20, TextAlignment = TextAlignment.Left, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new Thickness(8) };
    private static FlowDocumentScrollViewer BlockViewer(FlowDocument document) { var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, IsSelectionEnabled = true, Background = Brushes.White }; viewer.BeginInit(); viewer.EndInit(); return viewer; }
    private static FrameworkElement BlockScope(FlowDocumentScrollViewer viewer) => (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
    private static int BlockMarkerInk(FlowDocumentScrollViewer viewer, BlockGlyph glyph, double height, Color color)
    {
        Point origin = BlockScope(viewer).TransformToAncestor(viewer).Transform(glyph.Baseline); byte[] pixels = Raster(viewer); int count = 0;
        for (int y = Math.Max(0, (int)Math.Floor(origin.Y - height)); y < Math.Min(220, (int)Math.Ceiling(origin.Y + 2)); y++)
            for (int x = Math.Max(0, (int)Math.Floor(origin.X - 1)); x < Math.Min(380, (int)Math.Ceiling(origin.X + glyph.Width + 1)); x++)
            { int i = (y * 380 + x) * 4; if (pixels[i + 3] > 240 && Math.Abs(pixels[i] - color.B) < 80 && Math.Abs(pixels[i + 1] - color.G) < 80 && Math.Abs(pixels[i + 2] - color.R) < 80) count++; }
        return count;
    }
    private static List<BlockGlyph> BlockGlyphs(FlowDocumentScrollViewer viewer)
    {
        var result = new List<BlockGlyph>(); var scope = BlockScope(viewer);
        void Drawing(Drawing drawing, Matrix matrix)
        {
            if (drawing is DrawingGroup group) { var next = group.Transform?.Value ?? Matrix.Identity; next.Append(matrix); foreach (Drawing child in group.Children) Drawing(child, next); }
            else if (drawing is GlyphRunDrawing glyph && glyph.GlyphRun.Characters != null)
            { var run = glyph.GlyphRun; result.Add(new BlockGlyph(new string(run.Characters.ToArray()), matrix.Transform(run.BaselineOrigin), run.AdvanceWidths.Sum(), run.GlyphTypeface.FontUri.ToString())); }
        }
        void Visit(Visual visual)
        {
            var drawing = VisualTreeHelper.GetDrawing(visual);
            if (drawing != null) { var transform = visual.TransformToAncestor(scope); Point o = transform.Transform(new Point()), x = transform.Transform(new Point(1, 0)), y = transform.Transform(new Point(0, 1)); Drawing(drawing, new Matrix(x.X-o.X,x.Y-o.Y,y.X-o.X,y.Y-o.Y,o.X,o.Y)); }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++) if (VisualTreeHelper.GetChild(visual, i) is Visual child) Visit(child);
        }
        Visit(scope); return result;
    }
    private sealed record BlockGlyph(string Text, Point Baseline, double Width, string Font);
    private sealed class BlockFailureChild : FrameworkElement
    {
        private readonly bool _measure;
        internal BlockFailureChild(bool measure) { _measure = measure; }
        protected override Size MeasureOverride(Size available) => _measure ? throw new InvalidOperationException("expected nested child failure") : new Size(30, 20);
        protected override Size ArrangeOverride(Size final) => throw new InvalidOperationException("expected nested child failure");
    }
}
