using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class FlowDocumentCases
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

    internal static string EmbeddedChild()
    {
        _ = Application.Current ?? new Application();
        var editor = new TextBox { Text = "REAL CHILD", IsReadOnly = true, Height = 55, Background = Brushes.Lime, Foreground = Brushes.Black };
        var block = new BlockUIContainer(editor) { Margin = new Thickness(0) };
        var tail = new Run("AFTER CHILD");
        var document = new FlowDocument { TextAlignment = TextAlignment.Left, FontSize = 20, PagePadding = new Thickness(8) };
        document.Blocks.Add(new Paragraph(new Run("BEFORE CHILD")) { Margin = new Thickness(0) });
        document.Blocks.Add(block);
        document.Blocks.Add(new Paragraph(tail) { Margin = new Thickness(0) });
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        viewer.BeginInit(); viewer.EndInit();
        Layout(viewer, 380);
        Require(editor.ActualWidth > 0 && editor.ActualHeight == 55 && VisualTreeHelper.GetParent(editor) != null, "Actual embedded child was not arranged/parented.");
        Require(ReferenceEquals(editor.Parent, block), "Embedded child's logical identity changed.");
        editor.Select(0, 4);
        Require(editor.SelectedText == "REAL", "Actual child selection did not run.");
        byte[] original = Raster(viewer);
        Require(ColorPixels(original, Colors.Lime) > 300, "The real child subtree has no distinctive background pixels.");
        editor.Background = Brushes.Magenta;
        editor.Text = "CHANGED CHILD";
        Layout(viewer, 380);
        byte[] changedChild = Raster(viewer);
        Require(ColorPixels(changedChild, Colors.Magenta) > 300 && !changedChild.SequenceEqual(original), "Child background/content mutation retained stale pixels.");
        editor.Background = Brushes.Lime;
        editor.Text = "REAL CHILD";
        editor.Select(0, 4);
        Layout(viewer, 380);
        Require(Raster(viewer).SequenceEqual(original) && editor.SelectedText == "REAL", "Child content/background restore differs.");
        Rect before = tail.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        block.ClearValue(Block.MarginProperty);
        Layout(viewer, 380);
        Require(tail.ContentStart.GetCharacterRect(LogicalDirection.Forward) == before && Raster(viewer).SequenceEqual(original), "Default embedded block margin differs from explicit zero.");
        block.Margin = new Thickness(double.NaN);
        Layout(viewer, 380);
        Require(tail.ContentStart.GetCharacterRect(LogicalDirection.Forward) == before && Raster(viewer).SequenceEqual(original), "Automatic embedded block margin differs from explicit zero.");
        block.Margin = new Thickness(0);
        Layout(viewer, 380);
        Require(tail.ContentStart.GetCharacterRect(LogicalDirection.Forward) == before, "Explicit embedded block margin restore differs.");
        editor.Height = 90;
        Layout(viewer, 380);
        Require(editor.ActualHeight == 90 && tail.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y > before.Y + 30, "Child desired-size mutation did not move following text.");
        editor.Height = 55;
        Layout(viewer, 380);
        Require(tail.ContentStart.GetCharacterRect(LogicalDirection.Forward) == before && Raster(viewer).SequenceEqual(original), "Child size restore differs.");
        viewer.Document = null;
        Layout(viewer, 380);
        Require(VisualTreeHelper.GetParent(editor) == null, "Detached document retained its embedded visual.");
        viewer.Document = document;
        Layout(viewer, 380);
        Require(VisualTreeHelper.GetParent(editor) != null && ReferenceEquals(editor.Parent, block) && editor.Text == "REAL CHILD", "Reattachment lost actual embedded model/visual.");
        viewer.Document = null;
        Layout(viewer, 380);
        return "Actual embedded TextBox identity, layout, selection, size mutation/restore and detach/reattach passed.";
    }
    internal static string FailedChildLayout()
    {
        _ = Application.Current ?? new Application();
        var retained = new Border { Width = 80, Height = 30, Background = Brushes.Blue };
        var document = new FlowDocument(new BlockUIContainer(retained)) { TextAlignment = TextAlignment.Left };
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        viewer.BeginInit(); viewer.EndInit(); Layout(viewer, 380);
        var oldParent = VisualTreeHelper.GetParent(retained);
        var rejected = new ThrowingChild();
        var rejectedBlock = new BlockUIContainer(rejected);
        document.Blocks.Add(rejectedBlock);
        bool caught = false;
        try { Layout(viewer, 380); }
        catch (InvalidOperationException error) when (error.Message == "expected child arrange failure") { caught = true; }
        Require(caught, "Actual embedded child Arrange did not fail.");
        Require(ReferenceEquals(oldParent, VisualTreeHelper.GetParent(retained)), "Failed layout detached the committed child.");
        Require(VisualTreeHelper.GetParent(rejected) == null, "Failed layout retained its newly acquired child island.");
        document.Blocks.Remove(rejectedBlock);
        Layout(viewer, 380);
        Require(retained.ActualHeight == 30 && document.ContentStart.HasValidLayout, "Document did not recover after failed child removal.");
        viewer.Document = null; Layout(viewer, 380);
        Require(VisualTreeHelper.GetParent(retained) == null, "Recovered child remained attached after detach.");
        return "Failed real child Arrange preserves committed child ownership, releases new acquisition and recovers after removal.";
    }

    private sealed class ThrowingChild : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) => new Size(50, 30);
        protected override Size ArrangeOverride(Size finalSize) => throw new InvalidOperationException("expected child arrange failure");
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
    private static byte[] Raster(Visual viewer)
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
    private static int ColorPixels(byte[] pixels, Color color)
    {
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
            if (pixels[index + 3] == 255 && pixels[index] == color.B && pixels[index + 1] == color.G && pixels[index + 2] == color.R) count++;
        return count;
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
