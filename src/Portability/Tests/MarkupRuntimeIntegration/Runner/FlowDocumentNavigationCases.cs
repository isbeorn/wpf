using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string ChildNavigation()
    {
        _ = Application.Current ?? new Application();
        var first = new Run("BEFORE");
        var last = new Run("AFTER");
        var child = new Border { Height = 25, Width = 70, Background = Brushes.Blue };
        var block = new BlockUIContainer(child) { Margin = new Thickness(0) };
        var document = new FlowDocument { TextAlignment = TextAlignment.Left, PagePadding = new Thickness(8) };
        document.Blocks.Add(new Paragraph(first) { Margin = new Thickness(0) });
        document.Blocks.Add(block);
        document.Blocks.Add(new Paragraph(last) { Margin = new Thickness(0) });
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        viewer.BeginInit(); viewer.EndInit(); Layout(viewer, 380);
        try
        {
            var start = first.ContentStart.GetLineStartPosition(0)!;
            var next = start.GetLineStartPosition(1, out int nextCount)!;
            var following = next.GetLineStartPosition(1, out int followingCount)!;
            var previous = following.GetLineStartPosition(-1, out int previousCount)!;
            var restored = previous.GetLineStartPosition(-1, out int restoredCount)!;
            var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
            var textViewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
            var view = ((IServiceProvider)scope).GetService(textViewType)!;
            var range = textViewType.GetMethod("GetLineRange")!.Invoke(view, new object[] { block.ContentStart })!;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var rangeStart = (TextPointer)range.GetType().GetProperty("Start", flags)!.GetValue(range)!;
            var rangeEnd = (TextPointer)range.GetType().GetProperty("End", flags)!.GetValue(range)!;
            var move = textViewType.GetMethod("GetNextCaretUnitPosition")!;
            var forward = (TextPointer)move.Invoke(view, new object[] { block.ContentStart, LogicalDirection.Forward })!;
            var reverse = (TextPointer)move.Invoke(view, new object[] { block.ContentEnd, LogicalDirection.Backward })!;
            var boundary = textViewType.GetMethod("IsAtCaretUnitBoundary")!;
            bool startBoundary = (bool)boundary.Invoke(view, new object[] { block.ContentStart })!;
            bool endBoundary = (bool)boundary.Invoke(view, new object[] { block.ContentEnd })!;
            var moveLine = textViewType.GetMethod("GetPositionAtNextLine")!;
            var sidePositions = new List<int>();
            foreach (var input in new[] { (start, 1), (following, -1) })
                foreach (double suggestedX in new[] { 0d, 1000d })
                {
                    object[] arguments = { input.Item1, suggestedX, input.Item2, 0d, 0 };
                    var destination = (TextPointer)moveLine.Invoke(view, arguments)!;
                    sidePositions.Add(document.ContentStart.GetOffsetToPosition(destination));
                    Require(destination.CompareTo(suggestedX == 0 ? block.ContentStart : block.ContentEnd) == 0 && (int)arguments[4] == input.Item2,
                        "Finite-X child line navigation differs.");
                }
            var clampedEnd = start.GetLineStartPosition(10, out int forwardLimit)!;
            var clampedStart = following.GetLineStartPosition(-10, out int reverseLimit)!;
            Require(clampedEnd.CompareTo(following) == 0 && clampedStart.CompareTo(start) == 0 && forwardLimit == 2 && reverseLimit == -2,
                "Child line count differs at document boundaries.");
            int Offset(TextPointer pointer) => document.ContentStart.GetOffsetToPosition(pointer);
            string detail = JsonSerializer.Serialize(new { start = Offset(start), next = Offset(next), following = Offset(following), previous = Offset(previous), restored = Offset(restored),
                childStart = Offset(block.ContentStart), childEnd = Offset(block.ContentEnd), nextCount, followingCount, previousCount, restoredCount,
                rangeStart = Offset(rangeStart), rangeEnd = Offset(rangeEnd), forward = Offset(forward), reverse = Offset(reverse), startBoundary, endBoundary, sidePositions, forwardLimit, reverseLimit });
            Require(next.CompareTo(block.ContentStart) == 0 && following.Paragraph == last.ContentStart.Paragraph && nextCount == 1 && followingCount == 1,
                "Forward public line navigation skipped the real child: " + detail);
            Require(previous.CompareTo(block.ContentStart) == 0 && restored.CompareTo(start) == 0 && previousCount == -1 && restoredCount == -1,
                "Reverse public line navigation skipped the real child: " + detail);
            Require(rangeStart.CompareTo(block.ContentStart) == 0 && rangeEnd.CompareTo(block.ContentEnd) == 0 && forward.CompareTo(block.ContentStart) == 0 && reverse.CompareTo(block.ContentEnd) == 0 && !startBoundary && !endBoundary,
                "Embedded line range/caret positions differ: " + detail);
            return detail;
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }
}