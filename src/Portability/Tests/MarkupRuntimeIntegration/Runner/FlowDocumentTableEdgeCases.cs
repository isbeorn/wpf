using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string TableEdges()
    {
        _ = Application.Current ?? new Application();
        var observations = new List<object>();
        bool failed = false;
        foreach (string name in new[] { "empty-table", "empty-groups", "empty-row", "empty-cells", "narrow", "table-rtl", "document-rtl", "spacing-padding" })
        {
            var document = BlockDocument();
            if (name == "document-rtl") document.FlowDirection = FlowDirection.RightToLeft;
            var first = new Run("BEFORE"); var last = new Run("AFTER");
            document.Blocks.Add(new Paragraph(first) { Margin = new Thickness(0) });
            var table = new Table { CellSpacing = name == "spacing-padding" ? 3 : 0, BorderThickness = new Thickness(0.5),
                BorderBrush = Brushes.Red, Padding = name == "spacing-padding" ? new Thickness(7, 3, 11, 5) : new Thickness(0) };
            table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
            if (name == "table-rtl") table.FlowDirection = FlowDirection.RightToLeft;
            var runs = new List<Run>(); var cells = new List<TableCell>();
            if (name != "empty-table")
            {
                var group = new TableRowGroup(); table.RowGroups.Add(group);
                if (name == "empty-groups") table.RowGroups.Add(new TableRowGroup());
                else
                {
                    if (name == "empty-row") group.Rows.Add(new TableRow());
                    for (int row = 0; row < 2; row++)
                    {
                        var tableRow = new TableRow(); group.Rows.Add(tableRow);
                        for (int column = 0; column < 2; column++)
                        {
                            var cell = new TableCell { Padding = new Thickness(2), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Blue,
                                Background = column == 0 ? Brushes.Yellow : null, TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right };
                            if (name != "empty-cells")
                            {
                                var run = new Run((column == 0 ? "LEFT" : "RIGHT") + row); runs.Add(run);
                                cell.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
                            }
                            cells.Add(cell); tableRow.Cells.Add(cell);
                        }
                    }
                }
            }
            document.Blocks.Add(table); document.Blocks.Add(new Paragraph(last) { Margin = new Thickness(0) });
            int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
            string text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            var viewer = BlockViewer(document);
            var movement = new List<object>();
            try
            {
                Layout(viewer, 380);
                Rect firstRect = first.ContentStart.GetCharacterRect(LogicalDirection.Forward), lastRect = last.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Rect[] wide = runs.Select(run => run.ContentStart.GetCharacterRect(LogicalDirection.Forward)).ToArray();
                Require(first.ContentStart.HasValidLayout && last.ContentStart.HasValidLayout && lastRect.Top >= firstRect.Bottom,
                    "Table edge case lost valid surrounding paragraph layout: " + name);
                foreach (Run run in runs) Require(ReferenceEquals(HitFirstRun(viewer, run), run), "Table edge case hit lost its original Run: " + name);
                byte[] ink = Raster(viewer);
                if (wide.Length != 0)
                {
                    Require(wide[2].Y > wide[0].Y && Math.Abs(wide[0].Y - wide[1].Y) < 0.05, "Table row geometry differs: " + name);
                    Require(name is "table-rtl" or "document-rtl" ? wide[0].X > wide[1].X : wide[0].X < wide[1].X,
                        "Table column order did not follow the genuine flow direction: " + name);
                }
                var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
                Type viewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
                object view = ((IServiceProvider)scope).GetService(viewType)!;
                MethodInfo moveLine = viewType.GetMethod("GetPositionAtNextLine")!;
                TextPointer Move(TextPointer source, double x, int count)
                {
                    object range = viewType.GetMethod("GetLineRange")!.Invoke(view, new object[] { source })!;
                    const BindingFlags rangeFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var lineStart = (TextPointer)range.GetType().GetProperty("Start", rangeFlags)!.GetValue(range)!;
                    var lineEnd = (TextPointer)range.GetType().GetProperty("End", rangeFlags)!.GetValue(range)!;
                    object[] arguments = { source, x, count, 0d, 0 };
                    var destination = (TextPointer)moveLine.Invoke(view, arguments)!;
                    movement.Add(new { source = document.ContentStart.GetOffsetToPosition(source), sourceBias = source.LogicalDirection.ToString(),
                        lineStart = document.ContentStart.GetOffsetToPosition(lineStart), lineEnd = document.ContentStart.GetOffsetToPosition(lineEnd), lineParent = lineStart.Parent.GetType().Name, x, count,
                        destination = document.ContentStart.GetOffsetToPosition(destination), moved = (int)arguments[4], parent = destination.Parent.GetType().Name });
                    Require((int)arguments[4] == count, "Table edge navigation line count differs: " + name);
                    return destination;
                }
                bool InCell(TextPointer position, TableCell cell)
                {
                    for (DependencyObject? parent = position.Parent; parent != null; parent = LogicalTreeHelper.GetParent(parent))
                        if (ReferenceEquals(parent, cell)) return true;
                    return false;
                }
                if (cells.Count == 0)
                {
                    Require(Move(first.ContentStart, 20, 1).Paragraph == last.ContentStart.Paragraph &&
                        Move(last.ContentStart, 20, -1).Paragraph == first.ContentStart.Paragraph,
                        "Empty table/group inserted a phantom navigation line: " + name);
                }
                else
                {
                    for (int column = 0; column < 2; column++)
                    {
                        double x = cells[column].ContentStart.GetCharacterRect(LogicalDirection.Forward).X + 2;
                        TextPointer top = cells[column].ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
                        TextPointer bottom = cells[column + 2].ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
                        Require(InCell(Move(top, x, 1), cells[column + 2]) && InCell(Move(bottom, x, -1), cells[column]),
                            "Table edge navigation crossed columns instead of following suggested X: " + name);
                        Require(InCell(Move(first.ContentStart, x, 1), cells[column]) &&
                            Move(bottom, x, 1).Paragraph == last.ContentStart.Paragraph &&
                            InCell(Move(last.ContentStart, x, -1), cells[column + 2]),
                            "Table edge entry/exit navigation differs: " + name);
                    }
                }
                var widths = new List<object>();
                foreach (double width in name == "narrow" ? new[] { 220d, 72d, 24d } : new[] { 220d })
                {
                    Layout(viewer, width);
                    widths.Add(new { width, first = first.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(),
                        last = last.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(),
                        cells = runs.Select(run => new { run.Text, start = run.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(),
                            end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward).ToString() }).ToArray() });
                    Require(document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols && new TextRange(document.ContentStart, document.ContentEnd).Text == text,
                        "Table width changed source symbols or serialization: " + name);
                }
                Layout(viewer, 380);
                Require(Raster(viewer).SequenceEqual(ink), "Table edge case width restoration retained stale pixels: " + name);
                observations.Add(new { name, symbols, text, direction = table.FlowDirection.ToString(), padding = table.Padding.ToString(), table.CellSpacing,
                    first = firstRect.ToString(), last = lastRect.ToString(), wide = wide.Select(rect => rect.ToString()).ToArray(),
                    cells = cells.Select(cell => new { blocks = cell.Blocks.Count, start = cell.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(),
                        end = cell.ContentEnd.GetCharacterRect(LogicalDirection.Backward).ToString() }).ToArray(), movement, widths });
            }
            catch (Exception error) { failed = true; observations.Add(new { name, movement, error = error.ToString() }); }
            finally { viewer.Document = null; Layout(viewer, 380); }
        }
        string details = JsonSerializer.Serialize(observations);
        Require(!failed, "Table edge-case stock calibration failed: " + details);
        return details;
    }
}
