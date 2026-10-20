using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string TableLayout()
    {
        _ = Application.Current ?? new Application();
        var document = BlockDocument();
        var before = new Run("BEFORE"); var after = new Run("AFTER");
        document.Blocks.Add(new Paragraph(before) { Margin = new Thickness(0) });
        var table = new Table { CellSpacing = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Red };
        table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
        var header = new TableRowGroup { FontWeight = FontWeights.SemiBold, Background = Brushes.Yellow };
        var body = new TableRowGroup(); table.RowGroups.Add(header); table.RowGroups.Add(body);
        var runs = new List<Run>();
        for (int row = 0; row < 3; row++)
        {
            var cells = new TableRow(); (row == 0 ? header : body).Rows.Add(cells);
            foreach (string label in new[] { "LEFT" + row, "RIGHT" + row })
            {
                var run = new Run(label); runs.Add(run);
                cells.Cells.Add(new TableCell(new Paragraph(run) { Margin = new Thickness(0) })
                { Padding = new Thickness(2), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Blue,
                    TextAlignment = label.StartsWith("LEFT", StringComparison.Ordinal) ? TextAlignment.Left : TextAlignment.Right });
            }
        }
        document.Blocks.Add(table); document.Blocks.Add(new Paragraph(after) { Margin = new Thickness(0) });
        int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
        string serialized = new TextRange(document.ContentStart, document.ContentEnd).Text;
        var viewer = BlockViewer(document);
        try
        {
            Layout(viewer, 380);
            Rect[] wide = runs.Select(run => run.ContentStart.GetCharacterRect(LogicalDirection.Forward)).ToArray();
            Require(wide.All(rect => !rect.IsEmpty) && wide[0].X < wide[1].X && wide[2].Y > wide[0].Y && wide[4].Y > wide[2].Y,
                "Genuine table cells did not occupy two columns and three rows.");
            Require(Math.Abs(wide[0].Y - wide[1].Y) < 0.05 && Math.Abs(wide[2].Y - wide[3].Y) < 0.05,
                "Cells in the same row did not share a top edge.");
            foreach (Run run in runs) Require(ReferenceEquals(HitFirstRun(viewer, run), run), "Cell hit lost the original Run identity: " + run.Text);
            byte[] ink = Raster(viewer);
            Require(InlineColorCount(ink, new Rect(0, 0, 380, 220), Colors.Yellow) > 50 && InlineColorCount(ink, new Rect(0, 0, 380, 220), Colors.Blue) > 10 && InlineColorCount(ink, new Rect(0, 0, 380, 220), Colors.Red) > 10,
                "Original header background, cell borders or table border have no distinctive ink.");
            var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
            Type textViewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
            object view = ((IServiceProvider)scope).GetService(textViewType)!;
            MethodInfo move = textViewType.GetMethod("GetPositionAtNextLine")!;
            TextPointer Move(TextPointer start, double x, int count)
            {
                object[] arguments = { start, x, count, 0d, 0 };
                var destination = (TextPointer)move.Invoke(view, arguments)!;
                Require((int)arguments[4] == count, "Table vertical movement reported the wrong line count.");
                return destination;
            }
            for (int column = 0; column < 2; column++)
            {
                double x = wide[column].X + 2;
                Require(Move(runs[column].ContentStart, x, 1).Paragraph == runs[column + 2].ContentStart.Paragraph &&
                    Move(runs[column + 2].ContentStart, x, -1).Paragraph == runs[column].ContentStart.Paragraph,
                    "Vertical navigation crossed columns instead of following suggested X.");
                Require(Move(before.ContentStart, x, 1).Paragraph == runs[column].ContentStart.Paragraph &&
                    Move(runs[column + 4].ContentStart, x, 1).Paragraph == after.ContentStart.Paragraph &&
                    Move(after.ContentStart, x, -1).Paragraph == runs[column + 4].ContentStart.Paragraph,
                    "Forward or reverse navigation lost a table edge.");
            }
            viewer.Selection.Select(runs[0].ContentStart, runs[5].ContentEnd); string selected = viewer.Selection.Text;
            viewer.Selection.Select(runs[5].ContentEnd, runs[0].ContentStart);
            string reverseSelected = viewer.Selection.Text;
            string headerText = "LEFT0\tRIGHT0" + Environment.NewLine;
            string bodyText = "LEFT1\tRIGHT1" + Environment.NewLine + "LEFT2\tRIGHT2" + Environment.NewLine;
            Require(selected == headerText && reverseSelected == bodyText,
                "Original anchor-dependent cross-RowGroup selection differs from stock: " + JsonSerializer.Serialize(new { selected, reverseSelected, serialized }));
            viewer.Selection.Select(runs[2].ContentStart, runs[5].ContentEnd); string bodySelected = viewer.Selection.Text;
            viewer.Selection.Select(runs[5].ContentEnd, runs[2].ContentStart);
            Require(bodySelected == bodyText && viewer.Selection.Text == bodySelected,
                "Forward/reverse selection within the same body RowGroup differs.");
            Require(serialized == "BEFORE" + Environment.NewLine + headerText + bodyText + "AFTER" + Environment.NewLine &&
                new TextRange(document.ContentStart, document.ContentEnd).Text == serialized,
                "Whole-document table serialization differs from stock.");
            viewer.Selection.Select(before.ContentStart, before.ContentStart);
            Layout(viewer, 220); Rect narrow = runs[1].ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(narrow.X < wide[1].X, "Auto columns did not respond to available width.");
            Layout(viewer, 380);
            Require(Raster(viewer).SequenceEqual(ink) && document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols &&
                new TextRange(document.ContentStart, document.ContentEnd).Text == serialized,
                "Table width restoration changed original symbols, serialization or pixels.");
            return JsonSerializer.Serialize(new { shape = "declared observed MdXaml table recipe", symbols, serialized, selected, reverseSelected, bodySelected,
                wide = wide.Select(rect => rect.ToString()).ToArray(), narrow = narrow.ToString() });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }
}
