using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string TableBackgroundLayers()
    {
        _ = Application.Current ?? new Application();
        var document = BlockDocument();
        var table = new Table { Margin = new Thickness(0), CellSpacing = 10, Padding = new Thickness(0) };
        table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
        var yellow = new TableRowGroup { Background = Brushes.Yellow };
        var cyan = new TableRowGroup { Background = Brushes.Cyan };
        table.RowGroups.Add(yellow); table.RowGroups.Add(cyan);
        var halfBlue = new SolidColorBrush(Color.FromArgb(128, 0, 0, 255));
        var rows = new[] { new TableRow { Background = Brushes.Transparent }, new TableRow { Background = halfBlue }, new TableRow() };
        yellow.Rows.Add(rows[0]); yellow.Rows.Add(rows[1]); cyan.Rows.Add(rows[2]);
        var runs = new List<Run>();
        foreach (TableRow row in rows)
            for (int column = 0; column < 2; column++)
            {
                var run = new Run("X"); runs.Add(run);
                row.Cells.Add(new TableCell(new Paragraph(run) { Margin = new Thickness(0) }) { Padding = new Thickness(12, 8, 12, 8) });
            }
        document.Blocks.Add(table);
        int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
        string serialized = new TextRange(document.ContentStart, document.ContentEnd).Text;
        var viewer = BlockViewer(document);
        var observations = new List<object>();
        object Samples(double width)
        {
            Layout(viewer, width);
            byte[] pixels = Raster(viewer);
            Rect[] rectangles = runs.Select(run => run.ContentStart.GetCharacterRect(LogicalDirection.Forward)).ToArray();
            var transform = BlockScope(viewer).TransformToAncestor(viewer);
            Color Sample(Point point)
            {
                Point target = transform.Transform(point);
                int x = (int)Math.Floor(target.X), y = (int)Math.Floor(target.Y);
                Require(x >= 1 && x < 379 && y >= 1 && y < 219, "Layered table sample escaped the existing raster.");
                int offset = (y * 380 + x) * 4;
                return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            }
            void Expected(Color actual, Color expected, string location)
            {
                Require(actual.A == 255 && Math.Abs(actual.R - expected.R) <= 1 && Math.Abs(actual.G - expected.G) <= 1 && Math.Abs(actual.B - expected.B) <= 1,
                    "Table background layer differs at " + location + ": " + actual + " expected " + expected);
            }
            var samples = new List<object>();
            Color blend = Color.FromRgb(127, 127, 128);
            for (int row = 0; row < rows.Length; row++)
            {
                Rect first = rectangles[row * 2], second = rectangles[row * 2 + 1];
                Color expected = row == 0 ? Colors.Yellow : row == 1 ? blend : Colors.Cyan;
                Color body = Sample(new Point(first.X + 35, first.Y + first.Height / 2));
                Color horizontalGap = Sample(new Point(second.X - 12 - table.CellSpacing / 2, first.Y + first.Height / 2));
                Color topGap = Sample(new Point(first.X + 35, first.Y - 8 - 2));
                Color bottomGap = Sample(new Point(first.X + 35, first.Bottom + 8 + 2));
                samples.Add(new { row, body = body.ToString(), horizontalGap = horizontalGap.ToString(), topGap = topGap.ToString(), bottomGap = bottomGap.ToString(), expected = expected.ToString() });
                Expected(body, expected, "row body"); Expected(horizontalGap, expected, "horizontal cell gap");
                Expected(topGap, expected, "row's upper spacing half"); Expected(bottomGap, expected, "row's lower spacing half");
            }
            return new { width, samples };
        }
        try
        {
            observations.Add(Samples(380)); byte[] original = Raster(viewer);
            observations.Add(Samples(220)); observations.Add(Samples(380));
            Require(Raster(viewer).SequenceEqual(original), "Layered table width restoration changed pixels.");
            rows[1].Background = Brushes.Transparent; Layout(viewer, 380);
            Rect rowStart = runs[2].ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Rect region = BlockScope(viewer).TransformToAncestor(viewer).TransformBounds(new Rect(rowStart.X + 30, rowStart.Y + 2, 8, 8));
            Require(InlineColorCount(Raster(viewer), region, Colors.Yellow) > 20, "Making the translucent row transparent did not expose its group background.");
            rows[1].Background = halfBlue; observations.Add(Samples(380));
            Require(Raster(viewer).SequenceEqual(original) && document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols &&
                new TextRange(document.ContentStart, document.ContentEnd).Text == serialized, "Row-layer restoration changed pixels, source symbols or serialization.");
            return JsonSerializer.Serialize(new { scope = "genuine row/group layers with transparent and translucent rows, two groups and CellSpacing10", symbols, serialized, observations });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    internal static string TableAutoWidths()
    {
        _ = Application.Current ?? new Application();
        var document = BlockDocument();
        var table = new Table { CellSpacing = 0, Padding = new Thickness(0) };
        table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        var header = new TableRow(); var body = new TableRow(); group.Rows.Add(header); group.Rows.Add(body);
        var anchors = new[] { new Run("X"), new Run("X") };
        var headers = anchors.Select(run => new Paragraph(run) { Margin = new Thickness(0), TextAlignment = TextAlignment.Left }).ToArray();
        foreach (Paragraph paragraph in headers) header.Cells.Add(new TableCell(paragraph) { Padding = new Thickness(2) });
        var longText = new Run("A longer descriptive key wraps across several words and lines");
        var shortText = new Run("v");
        foreach (Run run in new[] { longText, shortText })
            body.Cells.Add(new TableCell(new Paragraph(run) { Margin = new Thickness(0) }) { Padding = new Thickness(2) });
        document.Blocks.Add(table);
        string serialized = new TextRange(document.ContentStart, document.ContentEnd).Text;
        int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
        var viewer = BlockViewer(document);
        var observations = new List<object>();
        try
        {
            foreach (double width in new[] { 380d, 220d })
            {
                foreach (Paragraph paragraph in headers) paragraph.TextAlignment = TextAlignment.Left;
                Layout(viewer, width);
                double[] left = anchors.Select(run => run.ContentStart.GetCharacterRect(LogicalDirection.Forward).X).ToArray();
                Rect start = longText.ContentStart.GetCharacterRect(LogicalDirection.Forward), end = longText.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                bool longWrapped = end.Y > start.Y;
                bool shortWrapped = shortText.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Y > shortText.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y;
                foreach (Paragraph paragraph in headers) paragraph.TextAlignment = TextAlignment.Right;
                Layout(viewer, width);
                double[] right = anchors.Select(run => run.ContentStart.GetCharacterRect(LogicalDirection.Forward).X).ToArray();
                double firstFlex = right[0] - left[0], secondFlex = right[1] - left[1];
                observations.Add(new { width, left, right, firstFlex, secondFlex, longWrapped, shortWrapped, longStart = start.ToString(), longEnd = end.ToString() });
                Require(firstFlex > 0 && secondFlex > 0 && Math.Abs(firstFlex - secondFlex) < 0.05 && longWrapped && !shortWrapped,
                    "Unequal content changed the stock equal Auto-column allocation or wrapping: " + JsonSerializer.Serialize(observations));
                Require(new TextRange(document.ContentStart, document.ContentEnd).Text == serialized && document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols,
                    "Auto allocation changed the original document source or serializer.");
            }
            return JsonSerializer.Serialize(new { scope = "two genuine Auto columns with unequal content and wrapped body, equal-width stock contract", symbols, serialized, observations });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }
}
