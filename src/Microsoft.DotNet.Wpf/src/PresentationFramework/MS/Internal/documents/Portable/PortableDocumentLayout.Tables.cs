// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;

namespace MS.Internal.Documents
{
    internal sealed partial class PortableDocumentLayout
    {
        private readonly List<Cell> _cells = new List<Cell>();

        private static bool TableHasCells(Table table)
        {
            foreach (TableRowGroup group in table.RowGroups)
                foreach (TableRow row in group.Rows)
                    if (row.Cells.Count != 0) return true;
            return false;
        }

        private BlockResult FormatTable(Table table, double x, double y, double width, PortableDocumentLayout previous)
        {
            int columns = table.Columns.Count;
            foreach (TableColumn column in table.Columns)
                if (!column.Width.IsAuto)
                    throw new PlatformNotSupportedException("Portable FlowDocument table columns must use Auto width.");
            foreach (TableRowGroup group in table.RowGroups)
                foreach (TableRow row in group.Rows)
                {
                    columns = Math.Max(columns, row.Cells.Count);
                    foreach (TableCell cell in row.Cells)
                        if (cell.RowSpan != 1 || cell.ColumnSpan != 1)
                            throw new PlatformNotSupportedException("Portable FlowDocument table cell spans are not supported.");
                }
            double top = y, baseline = y;
            bool first = true;
            double spacing = table.InternalCellSpacing;
            double columnWidth = columns == 0 ? width : width / columns;
            foreach (TableRowGroup group in table.RowGroups)
            {
                int groupBackgroundIndex = Visual.Children.Count;
                double groupTop = 0, groupBottom = 0;
                bool groupHasRows = false;
                foreach (TableRow row in group.Rows)
                {
                    if (row.Cells.Count == 0) continue;
                    y += first ? spacing / 2 : spacing;
                    double rowTop = y - spacing / 2;
                    if (!groupHasRows) { groupTop = rowTop; groupHasRows = true; }
                    int backgroundIndex = Visual.Children.Count;
                    var cells = new List<Cell>();
                    double bottom = y;
                    for (int column = 0; column < row.Cells.Count; column++)
                    {
                        TableCell element = row.Cells[column];
                        double cellX = table.FlowDirection == FlowDirection.RightToLeft
                            ? x + width - (column + 1) * columnWidth + spacing / 2 : x + column * columnWidth + spacing / 2;
                        double cellWidth = Math.Max(1, columnWidth - spacing);
                        var mbp = PtsHost.MbpInfo.FromElement(element, _document.PixelsPerDip);
                        if (element.FlowDirection == FlowDirection.RightToLeft) mbp.MirrorBP();
                        int start = _navigationLines.Count;
                        double margin = 0;
                        BlockResult content = FormatBlocks(element.Blocks, element,
                            cellX + mbp.Border.Left + mbp.Padding.Left, y + mbp.Border.Top + mbp.Padding.Top,
                            Math.Max(1, cellWidth - mbp.Border.Left - mbp.Padding.Left - mbp.Border.Right - mbp.Padding.Right),
                            ref margin, true, element.FlowDirection, previous);
                        var cell = new Cell(element, cells, start, _navigationLines.Count - 1,
                            new Rect(cellX, y, cellWidth, Math.Max(0, content.Bottom + margin + mbp.Border.Bottom + mbp.Padding.Bottom - y)));
                        cells.Add(cell);
                        _cells.Add(cell);
                        for (int index = start; index < _navigationLines.Count; index++)
                            _navigationLines[index].Cell ??= cell;
                        if (first && column == 0) baseline = content.Baseline;
                        bottom = Math.Max(bottom, cell.Bounds.Bottom);
                    }
                    if (row.Background != null)
                    {
                        var background = new PtsHost.ParagraphVisual();
                        background.DrawBackgroundAndBorder(row.Background, null, new Thickness(),
                            new Rect(x, rowTop, width, bottom + spacing / 2 - rowTop), true, true);
                        Visual.Children.Insert(backgroundIndex++, background);
                    }
                    foreach (Cell cell in cells)
                    {
                        cell.Bounds = new Rect(cell.Bounds.X, y, cell.Bounds.Width, bottom - y);
                        var mbp = PtsHost.MbpInfo.FromElement(cell.Element, _document.PixelsPerDip);
                        if (cell.Element.FlowDirection == FlowDirection.RightToLeft) mbp.MirrorBP();
                        if (cell.Element.Background == null && mbp.Border == new Thickness()) continue;
                        var background = new PtsHost.ParagraphVisual();
                        background.DrawBackgroundAndBorder(cell.Element.Background, mbp.BorderBrush, mbp.Border, cell.Bounds, true, true);
                        Visual.Children.Insert(backgroundIndex++, background);
                    }
                    groupBottom = bottom + spacing / 2;
                    y = bottom;
                    first = false;
                }
                if (groupHasRows && group.Background != null)
                {
                    var background = new PtsHost.ParagraphVisual();
                    background.DrawBackgroundAndBorder(group.Background, null, new Thickness(),
                        new Rect(x, groupTop, width, groupBottom - groupTop), true, true);
                    Visual.Children.Insert(groupBackgroundIndex, background);
                }
            }
            return new BlockResult(top, first ? y : y + spacing / 2, baseline);
        }

        private Cell FindCell(Point point)
        {
            Cell found = null;
            foreach (Cell cell in _cells)
                if (cell.Bounds.Contains(point) && (found == null || cell.Bounds.Width * cell.Bounds.Height < found.Bounds.Width * found.Bounds.Height))
                    found = cell;
            return found;
        }

        private int AdjacentLine(int index, int direction, double x)
        {
            Cell current = _navigationLines[index].Cell;
            int next = index + direction;
            if (current != null && (next < current.First || next > current.Last))
            {
                int boundary = direction > 0 ? current.Last : current.First;
                foreach (Cell cell in current.Row)
                    boundary = direction > 0 ? Math.Max(boundary, cell.Last) : Math.Min(boundary, cell.First);
                next = boundary + direction;
            }
            if (next < 0 || next >= _navigationLines.Count) return index;
            Cell target = _navigationLines[next].Cell;
            if (target != null && !ReferenceEquals(target, current))
            {
                Cell nearest = target;
                double distance = double.PositiveInfinity;
                foreach (Cell cell in target.Row)
                {
                    double dx = double.IsNaN(x) ? 0 : Math.Max(cell.Bounds.Left - x, Math.Max(0, x - cell.Bounds.Right));
                    if (dx < distance) { nearest = cell; distance = dx; }
                }
                next = direction > 0 ? nearest.First : nearest.Last;
            }
            return next;
        }

        private sealed class Cell
        {
            internal readonly TableCell Element;
            internal readonly List<Cell> Row;
            internal readonly int First, Last;
            internal Rect Bounds;
            internal Cell(TableCell element, List<Cell> row, int first, int last, Rect bounds)
            { Element = element; Row = row; First = first; Last = last; Bounds = bounds; }
        }
    }
}
