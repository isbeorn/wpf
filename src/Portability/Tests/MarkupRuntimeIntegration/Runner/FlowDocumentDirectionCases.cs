using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string DirectionUnicode()
    {
        _ = Application.Current ?? new Application();
        var font = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./DejaVuSans.ttf#DejaVu Sans");
        int cases = 0;
        foreach (string text in new[] { "abc \u05d0\u05d1\u05d2 123 \u0633\u0644\u0627\u0645", "office A\U0001f600e\u0301 \u05d0\u05d1\u05d2 42" })
        foreach (FlowDirection documentFlow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        foreach (FlowDirection paragraphFlow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            string label = $"document={documentFlow},paragraph={paragraphFlow},text={text}";
            var run = new Run(text);
            var paragraph = new Paragraph(run) { Margin = new Thickness(0), FlowDirection = paragraphFlow };
            var document = new FlowDocument(paragraph) { FlowDirection = documentFlow, TextAlignment = TextAlignment.Left,
                FontFamily = font, FontSize = 18, PagePadding = new Thickness(8), Foreground = Brushes.Black, Background = Brushes.White };
            var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, Background = Brushes.White };
            viewer.BeginInit(); viewer.EndInit();
            Layout(viewer, 380);
            try
            {
                var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
                var textViewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
                var textView = ((IServiceProvider)scope).GetService(textViewType)!;
                var hitMethod = textViewType.GetMethod("GetTextPositionFromPoint")!;
                GeneralTransform transform = scope.TransformToAncestor(viewer);
                var rectangles = new List<Rect>();
                for (TextPointer? position = run.ContentStart; position != null && position.CompareTo(run.ContentEnd) <= 0; position = position.GetNextInsertionPosition(LogicalDirection.Forward))
                {
                    int offset = run.ContentStart.GetOffsetToPosition(position);
                    Require(offset <= 0 || offset >= text.Length || !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]), "Insertion stopped inside a supplementary character: " + label);
                    Rect rect = position.GetCharacterRect(LogicalDirection.Forward);
                    Require(!rect.IsEmpty && rect.Height > 0 && double.IsFinite(rect.X), "Invalid public pointer rectangle: " + label);
                    rectangles.Add(transform.TransformBounds(rect));
                    var hit = (TextPointer?)hitMethod.Invoke(textView, new object[] { new Point(rect.X, rect.Y + rect.Height / 2), true });
                    Require(hit != null, "Actual text-view hit returned no position: " + label);
                    Rect hitRect = hit!.GetCharacterRect(hit.LogicalDirection);
                    Require(Math.Abs(hitRect.X - rect.X) < 0.6 && Math.Abs(hitRect.Y - rect.Y) < 0.6,
                        $"Public pointer/hit geometry differs at source{offset}: {rect} -> {hitRect}; {label}");
                }
                viewer.Selection.Select(run.ContentStart, run.ContentEnd);
                Require(viewer.Selection.Text == text, "Forward mixed-script selection changed source: " + label);
                viewer.Selection.Select(run.ContentEnd, run.ContentStart);
                Require(viewer.Selection.Text == text, "Reverse mixed-script selection changed source: " + label);
                viewer.Selection.Select(run.ContentStart, run.ContentStart);
                byte[] pixels = Raster(viewer);
                AssertDirectionInk(pixels, rectangles, label);
                paragraph.FlowDirection = paragraphFlow == FlowDirection.LeftToRight ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                Layout(viewer, 380);
                byte[] changedPixels = Raster(viewer);
                Require(!changedPixels.SequenceEqual(pixels), "Intermediate direction mutation did not change retained ink: " + label);
                var changedRectangles = new List<Rect>();
                for (TextPointer? position = run.ContentStart; position != null && position.CompareTo(run.ContentEnd) <= 0; position = position.GetNextInsertionPosition(LogicalDirection.Forward))
                {
                    Rect rect = position.GetCharacterRect(LogicalDirection.Forward);
                    Require(!rect.IsEmpty && rect.Height > 0 && double.IsFinite(rect.X), "Intermediate public pointer geometry is invalid: " + label);
                    changedRectangles.Add(transform.TransformBounds(rect));
                    var hit = (TextPointer?)hitMethod.Invoke(textView, new object[] { new Point(rect.X, rect.Y + rect.Height / 2), true });
                    Require(hit != null, "Intermediate text-view hit returned no position: " + label);
                    Rect hitRect = hit!.GetCharacterRect(hit.LogicalDirection);
                    Require(Math.Abs(hitRect.X - rect.X) < 0.6 && Math.Abs(hitRect.Y - rect.Y) < 0.6,
                        "Intermediate public pointer/hit geometry differs: " + label);
                }
                Require(changedRectangles.Count == rectangles.Count && changedRectangles.Zip(rectangles).Any(pair => Math.Abs(pair.First.X - pair.Second.X) > 18),
                    "Intermediate direction mutation did not move public pointer geometry: " + label);
                AssertDirectionInk(changedPixels, changedRectangles, "intermediate " + label);
                paragraph.FlowDirection = paragraphFlow;
                Layout(viewer, 380);
                Require(Raster(viewer).SequenceEqual(pixels), "Direction restore retained stale ink: " + label);
                cases++;
            }
            finally { viewer.Document = null; Layout(viewer, 380); }
        }
        return $"{cases} actual document/paragraph direction combinations preserve mixed-script source, supplementary insertion, forward/reverse selection, pointer/hit/retained ink agreement, observed intermediate direction change and direction restore.";
    }
    private static void AssertDirectionInk(byte[] pixels, List<Rect> rectangles, string label)
    {
        double top = rectangles.Min(rect => rect.Top), bottom = rectangles.Max(rect => rect.Bottom);
        int inkLeft = 380, inkRight = -1;
        for (int y = Math.Max(0, (int)Math.Floor(top)); y < Math.Min(220, (int)Math.Ceiling(bottom)); y++)
        for (int x = 4; x < 376; x++)
        {
            int i = (y * 380 + x) * 4;
            if (pixels[i + 3] > 240 && pixels[i] < 80 && pixels[i + 1] < 80 && pixels[i + 2] < 80)
            { inkLeft = Math.Min(inkLeft, x); inkRight = Math.Max(inkRight, x); }
        }
        double geometryLeft = rectangles.Min(rect => rect.Left), geometryRight = rectangles.Max(rect => rect.Right);
        Require(inkRight > inkLeft && Math.Abs(inkLeft - geometryLeft) < 18 && Math.Abs(inkRight - geometryRight) < 18,
            $"Actual retained ink and pointer envelope differ: ink={inkLeft}..{inkRight},geometry={geometryLeft}..{geometryRight}; {label}");
    }
}