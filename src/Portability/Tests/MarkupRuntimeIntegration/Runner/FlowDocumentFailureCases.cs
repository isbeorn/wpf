using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string FailedChildResize()
    {
        _ = Application.Current ?? new Application();
        var observations = new List<string>();
        foreach (bool failMeasure in new[] { false, true })
            foreach (bool widen in new[] { false, true })
                observations.Add(FailedChildResize(failMeasure, widen));
        observations.Add(FailedRestoration());
        return JsonSerializer.Serialize(observations);
    }

    private static string FailedChildResize(bool failMeasure, bool widen)
    {
        double originalWidth = widen ? 180 : 380;
        double failedWidth = widen ? 380 : 180;
        var retained = new Border { Background = Brushes.Blue,
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = new System.Windows.Shapes.Rectangle { Width = 400, Height = 50, Fill = Brushes.Lime } } };
        var document = new FlowDocument(new BlockUIContainer(retained)) { TextAlignment = TextAlignment.Left, PagePadding = new Thickness(8) };
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, Background = Brushes.White };
        viewer.BeginInit(); viewer.EndInit(); Layout(viewer, originalWidth);
        Size oldDesired = retained.DesiredSize, oldRender = retained.RenderSize;
        Vector oldOffset = VisualTreeHelper.GetOffset(retained);
        var oldParent = VisualTreeHelper.GetParent(retained);
        byte[] before = Raster(viewer);
        FrameworkElement rejected = failMeasure ? new MeasureThrowingChild() : new ThrowingChild();
        var rejectedBlock = new BlockUIContainer(rejected);
        document.Blocks.Add(rejectedBlock);
        bool caught = false;
        try { Layout(viewer, failedWidth); }
        catch (InvalidOperationException error) when (error.Message == (failMeasure ? "expected child measure failure" : "expected child arrange failure")) { caught = true; }
        Size failedDesired = retained.DesiredSize, failedRender = retained.RenderSize;
        Vector failedOffset = VisualTreeHelper.GetOffset(retained);
        bool parentPreserved = ReferenceEquals(oldParent, VisualTreeHelper.GetParent(retained));
        bool acquiredReleased = VisualTreeHelper.GetParent(rejected) == null;
        bool pixelsPreserved = Raster(viewer).SequenceEqual(before);
        document.Blocks.Remove(rejectedBlock);
        Layout(viewer, originalWidth);
        bool recovery = retained.RenderSize == oldRender && retained.DesiredSize == oldDesired && Raster(viewer).SequenceEqual(before);
        viewer.Document = null; Layout(viewer, originalWidth);
        string details = JsonSerializer.Serialize(new { failMeasure, widen, caught, oldDesired, oldRender, oldOffset, failedDesired, failedRender, failedOffset, parentPreserved, acquiredReleased, pixelsPreserved, recovery });
        Require(caught && parentPreserved && acquiredReleased && recovery, "Failure/recovery protocol differs: " + details);
        Require(failedRender == oldRender && failedDesired == oldDesired && failedOffset == oldOffset && pixelsPreserved, "Failed layout mutated the committed child's state: " + details);
        return details;
    }
    private static string FailedRestoration()
    {
        var first = new RestorationThrowingBorder { Height = 25, Background = Brushes.Blue };
        var second = new Border { Height = 20, Background = Brushes.Lime };
        var document = new FlowDocument { TextAlignment = TextAlignment.Left, PagePadding = new Thickness(8) };
        document.Blocks.Add(new BlockUIContainer(first));
        document.Blocks.Add(new BlockUIContainer(second));
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        viewer.BeginInit(); viewer.EndInit(); Layout(viewer, 380);
        double oldWidth = second.RenderSize.Width;
        var firstParent = VisualTreeHelper.GetParent(first);
        var rejected = new CallbackThrowingChild(() => first.Fail = true);
        var block = new BlockUIContainer(rejected);
        document.Blocks.Add(block);
        AggregateException? failure = null;
        try { Layout(viewer, 180); }
        catch (AggregateException error) { failure = error; }
        bool primaryPreserved = failure?.InnerExceptions[0].Message == "expected child arrange failure";
        bool restorationReported = failure?.InnerExceptions.Count == 2 && failure.InnerExceptions[1].Message == "expected restoration failure";
        bool otherRestored = second.RenderSize.Width == oldWidth;
        bool ownership = ReferenceEquals(firstParent, VisualTreeHelper.GetParent(first)) && VisualTreeHelper.GetParent(rejected) == null;
        first.Fail = false;
        document.Blocks.Remove(block);
        Layout(viewer, 380);
        bool recovered = first.RenderSize.Width == oldWidth && second.RenderSize.Width == oldWidth;
        viewer.Document = null; Layout(viewer, 380);
        Require(primaryPreserved && restorationReported && otherRestored && ownership && recovered,
            "Restoration failure hid the primary error, skipped another child or stranded ownership.");
        return "Primary and restoration errors preserved; remaining child restored; ownership and recovery passed.";
    }

    private sealed class RestorationThrowingBorder : Border
    {
        internal bool Fail;
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Fail) throw new InvalidOperationException("expected restoration failure");
            return base.MeasureOverride(availableSize);
        }
    }

    private sealed class CallbackThrowingChild : FrameworkElement
    {
        private readonly Action _beforeThrow;
        internal CallbackThrowingChild(Action beforeThrow) { _beforeThrow = beforeThrow; }
        protected override Size MeasureOverride(Size availableSize) => new Size(30, 20);
        protected override Size ArrangeOverride(Size finalSize)
        {
            _beforeThrow();
            throw new InvalidOperationException("expected child arrange failure");
        }
    }

    private sealed class MeasureThrowingChild : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) => throw new InvalidOperationException("expected child measure failure");
    }
}