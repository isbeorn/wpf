using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FlowDocumentCases
{
    internal static string Paragraph()
    {
        _ = Application.Current ?? new Application();
        var run = new Run("VISIBLE DOCUMENT alpha beta gamma delta epsilon zeta eta theta.");
        var document = new FlowDocument(new Paragraph(run) { Margin = new Thickness(0) })
        { PagePadding = new Thickness(8), TextAlignment = TextAlignment.Left, FontSize = 20, Foreground = Brushes.Black, Background = Brushes.White };
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, IsSelectionEnabled = true };
        viewer.BeginInit(); viewer.EndInit();
        Layout(viewer, 380);
        Require(viewer.Template != null, "Original viewer template missing.");
        var first = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var last = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Require(!first.IsEmpty && first.Height > 0 && !last.IsEmpty && last.Height > 0, "Character geometry is missing.");
        var host = viewer.Template!.FindName("PART_ContentHost", viewer) as ScrollViewer;
        Require(host?.Content is FrameworkElement, "Original document render scope missing.");
        var scope = (FrameworkElement)host!.Content;
        var point = new Point(first.X + 3, first.Y + first.Height / 2);
        var visualHit = VisualTreeHelper.HitTest(scope, point)?.VisualHit;
        while (visualHit != null && visualHit is not IContentHost) visualHit = VisualTreeHelper.GetParent(visualHit) as Visual;
        var contentHit = (visualHit as IContentHost)?.InputHitTest(point);
        Require(ReferenceEquals(contentHit, run), $"Actual content-host hit differs: {contentHit?.GetType().FullName}; visual={visualHit?.GetType().FullName}; rect={first}; scope={scope.GetType().FullName}.");
        var textViewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
        var textView = ((IServiceProvider)scope).GetService(textViewType)!;
        var hitMethod = textViewType.GetMethod("GetTextPositionFromPoint")!;
        var hit = (TextPointer)hitMethod.Invoke(textView, new object[] { point, false })!;
        Require(hit != null && hit.Paragraph == run.ContentStart.Paragraph && Math.Abs(run.ContentStart.GetOffsetToPosition(hit)) <= 1,
            "Actual document text-view hit did not round-trip the first character.");
        Require(hitMethod.Invoke(textView, new object[] { new Point(-100, -100), false }) == null,
            "Non-snapping hit outside text returned a position.");
        Require(hitMethod.Invoke(textView, new object[] { new Point(-100, -100), true }) is TextPointer,
            "Snapping hit outside text failed.");
        var lineStart = run.ContentStart.GetLineStartPosition(0);
        var nextLine = run.ContentStart.GetLineStartPosition(1, out int forwardLines);
        Require(lineStart != null && new TextRange(lineStart, run.ContentStart).Text.Length == 0 && nextLine != null && forwardLines == 1 && nextLine.GetCharacterRect(LogicalDirection.Forward).Y > first.Y,
            "Public zero/forward line-start navigation failed.");
        var previousLine = nextLine!.GetLineStartPosition(-1, out int backwardLines);
        Require(previousLine != null && previousLine.CompareTo(lineStart!) == 0 && backwardLines == -1,
            "Public reverse line-start navigation did not restore the original position.");
        byte[] original = Raster(viewer);
        Require(BlackPixels(original) > 80, "Document has no recognizable dark text region.");
        viewer.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(7)!);
        Require(viewer.Selection.Text == "VISIBLE", "Forward selection differs.");
        viewer.Selection.Select(run.ContentStart.GetPositionAtOffset(7)!, run.ContentStart);
        Require(viewer.Selection.Text == "VISIBLE", "Reverse selection differs.");
        viewer.Selection.Select(run.ContentStart, run.ContentStart);
        Layout(viewer, 180);
        var narrow = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Require(narrow.Y > last.Y, "Narrow width did not wrap the actual document.");
        Layout(viewer, 380);
        var restored = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Require(Math.Abs(restored.Y - last.Y) < 0.01, "Width restore retained stale geometry.");
        Require(Raster(viewer).SequenceEqual(original), "Width restore retained stale ink.");
        run.Text = "CHANGED";
        Layout(viewer, 380);
        byte[] mutated = Raster(viewer);
        Require(!mutated.SequenceEqual(original), "Text mutation retained stale ink.");
        Require(run.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Y == first.Y, "Text mutation retained stale wrapping.");
        var replacementRun = new Run("REPLACEMENT");
        var replacement = new FlowDocument(new Paragraph(replacementRun) { Margin = new Thickness(0) })
        { TextAlignment = TextAlignment.Left, FontSize = 20, PagePadding = new Thickness(8) };
        viewer.Document = replacement;
        Layout(viewer, 380);
        Require(!document.ContentStart.HasValidLayout && replacementRun.ContentStart.HasValidLayout,
            "Document replacement retained the old text view.");
        byte[] replacementPixels = Raster(viewer);
        Require(BlackPixels(replacementPixels) > 20 && !replacementPixels.SequenceEqual(mutated), "Replacement document retained stale or empty ink.");
        Require(ReferenceEquals(HitFirstRun(viewer, replacementRun), replacementRun), "Replacement hit retained the old Run.");
        viewer.Document = document;
        Layout(viewer, 380);
        Require(run.ContentStart.HasValidLayout && !replacement.ContentStart.HasValidLayout,
            "Document A/B/A did not restore the original model's layout.");
        Require(Raster(viewer).SequenceEqual(mutated) && ReferenceEquals(HitFirstRun(viewer, run), run),
            "Document A/B/A retained replacement ink or hit identity.");
        viewer.Document = null;
        Layout(viewer, 380);
        Require(!document.ContentStart.HasValidLayout, "Detached document retained valid layout.");
        return "Original FlowDocumentScrollViewer template, Paragraph/Run ink, pointer rectangles, forward/reverse selection, hit round-trips, width restore, text mutation, A/B/A replacement and detach passed.";
    }

    private static IInputElement? HitFirstRun(FlowDocumentScrollViewer viewer, Run run)
    {
        var scope = (FrameworkElement)((ScrollViewer)viewer.Template!.FindName("PART_ContentHost", viewer)).Content;
        Rect rectangle = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var point = new Point(rectangle.X + 2, rectangle.Y + rectangle.Height / 2);
        var visual = VisualTreeHelper.HitTest(scope, point)?.VisualHit;
        while (visual != null && visual is not IContentHost) visual = VisualTreeHelper.GetParent(visual) as Visual;
        return (visual as IContentHost)?.InputHitTest(point);
    }

    private static void Layout(FrameworkElement viewer, double width)
    {
        viewer.Width = width; viewer.Height = 220;
        viewer.Measure(new Size(width, 220)); viewer.Arrange(new Rect(0, 0, width, 220)); viewer.UpdateLayout();
    }
    private static byte[] Raster(FrameworkElement viewer)
    {
        Type? targetType = typeof(Visual).Assembly.GetType("MS.Internal.Portability.Rendering.PortableRenderTarget");
        if (targetType != null)
        {
            using var target = (IDisposable)Activator.CreateInstance(targetType,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { viewer, 380, 220 }, null)!;
            targetType.GetMethod("Render")!.Invoke(target, null);
            using var snapshot = (IDisposable)targetType.GetMethod("Snapshot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, null)!;
            return (byte[])snapshot.GetType().GetProperty("Bytes")!.GetValue(snapshot)!;
        }
        var bitmap = new RenderTargetBitmap(380, 220, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewer);
        byte[] result = new byte[380 * 220 * 4]; bitmap.CopyPixels(result, 380 * 4, 0); return result;
    }
    private static int BlackPixels(byte[] pixels)
    {
        int count = 0;
        for (int y = 5; y < 160; y++)
            for (int x = 5; x < 350; x++)
            {
                int i = (y * 380 + x) * 4;
                if (pixels[i + 3] > 240 && pixels[i] < 80 && pixels[i + 1] < 80 && pixels[i + 2] < 80) count++;
            }
        return count;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
