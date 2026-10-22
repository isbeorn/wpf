using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static partial class FlowDocumentCases
{
    internal static string InlineChildFailures()
    {
        _ = Application.Current ?? new Application();
        var observations = new List<string>();
        try
        {
            foreach (bool replace in new[] { false, true })
                foreach (bool measure in new[] { false, true })
                    foreach (bool widen in new[] { false, true })
                        observations.Add(InlineFailureRollback(replace, measure, widen, false));
            foreach (bool replace in new[] { false, true })
                foreach (bool widen in new[] { false, true })
                    observations.Add(InlineFailureRollback(replace, false, widen, true));
            foreach (string change in new[] { "foreground", "desired-size", "child" })
                observations.Add(InlineArrangeInvalidation(change));
            observations.Add(InlineAttachmentInvalidation());
            observations.Add(InlineUpdatedFailure());
            observations.Add(InlineRestorationFailure());
            return JsonSerializer.Serialize(observations);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(new { completed = observations, failure = error.Message }), error);
        }
    }

    private static readonly FieldInfo InlineCacheField = typeof(InlineUIContainer).GetField("_uiElementIsland", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static object? InlineCache(InlineUIContainer inline) => InlineCacheField.GetValue(inline);
    private static object? InlineRoot(object? island) => island?.GetType().GetProperty("Root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island);
    private static Border InlineResizeChild() => new Border { Background = Brushes.Lime,
        Child = new Viewbox { Stretch = Stretch.Uniform, Child = new System.Windows.Shapes.Rectangle { Width = 400, Height = 50, Fill = Brushes.Blue } } };

    private static string InlineFailureRollback(bool replace, bool measure, bool widen, bool attachment)
    {
        double width = widen ? 180 : 380, failedWidth = widen ? 380 : 180;
        var retained = InlineResizeChild();
        var inline = new InlineUIContainer(retained);
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(new Run("BEFORE ")); paragraph.Inlines.Add(inline); paragraph.Inlines.Add(new Run(" AFTER"));
        var block = InlineResizeChild();
        var document = BlockDocument(); document.Background = Brushes.LightYellow;
        document.Blocks.Add(new BlockUIContainer(block)); document.Blocks.Add(paragraph);
        var viewer = BlockViewer(document);
        EventInfo? ancestorEvent = null; Delegate? handler = null; FrameworkElement? observed = null;
        try
        {
            Layout(viewer, width);
            object cache = InlineCache(inline)!;
            var parent = VisualTreeHelper.GetParent(retained);
            Size desired = retained.DesiredSize, rendered = retained.RenderSize;
            Vector offset = VisualTreeHelper.GetOffset(retained);
            Size blockDesired = block.DesiredSize, blockRendered = block.RenderSize;
            Vector blockOffset = VisualTreeHelper.GetOffset(block);
            var blockParent = VisualTreeHelper.GetParent(block);
            byte[] pixels = Raster(viewer);
            int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
            string serialized = new TextRange(document.ContentStart, document.ContentEnd).Text;
            FrameworkElement rejected = attachment ? InlineResizeChild() : measure ? new MeasureThrowingChild() : new ThrowingChild();
            InlineUIContainer? added = null;
            if (replace) inline.Child = rejected;
            else if (!attachment) { added = new InlineUIContainer(rejected); paragraph.Inlines.Add(added); }
            bool armed = attachment, attachmentReached = false;
            if (attachment)
            {
                observed = replace ? rejected : retained;
                ancestorEvent = typeof(Visual).GetEvent("VisualAncestorChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Action<object, object> callback = (sender, args) =>
                {
                    if (!armed) return;
                    for (DependencyObject? ancestor = (DependencyObject)sender; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                        if (ancestor.GetType().FullName == "MS.Internal.PtsHost.PageVisual")
                        {
                            armed = false; attachmentReached = true;
                            throw new InvalidOperationException("expected page attachment failure");
                        }
                };
                handler = Delegate.CreateDelegate(ancestorEvent.EventHandlerType!, callback.Target, callback.Method);
                ancestorEvent.GetAddMethod(true)!.Invoke(observed, new object[] { handler });
            }
            bool caught = false;
            try { Layout(viewer, failedWidth); }
            catch (InvalidOperationException error) when (error.Message == (attachment ? "expected page attachment failure" : measure ? "expected child measure failure" : "expected child arrange failure")) { caught = true; }
            bool cachePreserved = ReferenceEquals(InlineCache(inline), cache) && ReferenceEquals(InlineRoot(cache), retained);
            bool oldGeometry = retained.DesiredSize == desired && retained.RenderSize == rendered && VisualTreeHelper.GetOffset(retained) == offset &&
                block.DesiredSize == blockDesired && block.RenderSize == blockRendered && VisualTreeHelper.GetOffset(block) == blockOffset;
            bool ownership = ReferenceEquals(VisualTreeHelper.GetParent(retained), parent) && ReferenceEquals(VisualTreeHelper.GetParent(block), blockParent);
            bool released = !replace && attachment || VisualTreeHelper.GetParent(rejected) == null;
            bool oldInk = Raster(viewer).SequenceEqual(pixels);
            Require(caught && (!attachment || attachmentReached) && cachePreserved && oldGeometry && ownership && released && oldInk,
                JsonSerializer.Serialize(new { replace, measure, widen, attachment, caught, attachmentReached, cachePreserved, oldGeometry, ownership, released, oldInk }));
            Require(ReferenceEquals(inline.Child, replace ? rejected : retained), "Rollback changed the original document Child model.");
            if (replace) inline.Child = retained;
            if (added != null) paragraph.Inlines.Remove(added);
            Layout(viewer, width);
            Require(ReferenceEquals(InlineCache(inline), cache) && Raster(viewer).SequenceEqual(pixels), "Inline failure did not recover with its original cached island and pixels.");
            Require(document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols && new TextRange(document.ContentStart, document.ContentEnd).Text == serialized,
                "Inline rollback/recovery changed original document symbols or serialization.");
            return JsonSerializer.Serialize(new { replace, measure, widen, attachment, caught, attachmentReached, cachePreserved, oldGeometry, ownership, released, oldInk, recovery = true });
        }
        finally
        {
            if (handler != null) ancestorEvent!.GetRemoveMethod(true)!.Invoke(observed, new object[] { handler });
            viewer.Document = null; Layout(viewer, width);
        }
    }

    private static string InlineArrangeInvalidation(string change)
    {
        var child = new InlineArrangeCallback { Width = 60, Height = 24, Background = Brushes.Lime };
        var inline = new InlineUIContainer(child);
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(new Run("CALLBACK ")); paragraph.Inlines.Add(inline);
        var document = BlockDocument(); document.Blocks.Add(paragraph);
        var viewer = BlockViewer(document);
        try
        {
            Layout(viewer, 380);
            object cache = InlineCache(inline)!;
            bool called = false;
            var replacement = new Border { Width = 45, Height = 18, Background = Brushes.Blue };
            child.Callback = () =>
            {
                called = true;
                if (change == "desired-size") child.Width = 90;
                else if (change == "child") inline.Child = replacement;
                else paragraph.Foreground = Brushes.Red;
            };
            child.InvalidateArrange();
            Layout(viewer, 180);
            Require(called, "The legal Arrange callback was not exercised.");
            Layout(viewer, 380);
            Require(change == "child" ? ReferenceEquals(InlineRoot(InlineCache(inline)), replacement) && InlineRoot(cache) == null :
                ReferenceEquals(InlineCache(inline), cache) && ReferenceEquals(InlineRoot(cache), child), "Legal Arrange invalidation lost child/cache identity.");
            Require(paragraph.ContentStart.HasValidLayout, "Legal Arrange invalidation did not recover through normal formatting.");
            Require(change == "desired-size" ? child.RenderSize.Width == 90 : change == "child" ? replacement.RenderSize.Width == 45 :
                InlineColorCount(Raster(viewer), new Rect(0, 0, 380, 220), Colors.Red) > 10,
                "Legal Arrange callback invalidation was cleared or its new state was not rendered.");
            return JsonSerializer.Serialize(new { change, called, width = child.RenderSize.Width, cacheIdentityCorrect = true, layoutValid = true });
        }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }

    private static string InlineUpdatedFailure()
    {
        var oldChild = new Border { Width = 50, Height = 20, Background = Brushes.Lime };
        var inline = new InlineUIContainer(oldChild);
        var paragraph = new Paragraph(new Run("UPDATED ")) { Margin = new Thickness(0) };
        paragraph.Inlines.Add(inline);
        var document = BlockDocument(); document.Blocks.Add(paragraph);
        var viewer = BlockViewer(document);
        object? view = null; EventInfo? updated = null; EventHandler? handler = null;
        try
        {
            Layout(viewer, 380);
            object oldCache = InlineCache(inline)!;
            var scope = BlockScope(viewer);
            Type viewType = typeof(FlowDocument).Assembly.GetType("System.Windows.Documents.ITextView", true)!;
            view = ((IServiceProvider)scope).GetService(viewType)!;
            updated = viewType.GetEvent("Updated")!;
            var replacement = new Border { Width = 70, Height = 26, Background = Brushes.Red };
            bool armed = true, sawCommittedCache = false;
            handler = (sender, args) =>
            {
                if (!armed) return;
                armed = false;
                sawCommittedCache = ReferenceEquals(InlineRoot(InlineCache(inline)), replacement) && InlineRoot(oldCache) == null;
                paragraph.Foreground = Brushes.Blue;
                throw new InvalidOperationException("expected post-commit Updated failure");
            };
            updated.AddEventHandler(view, handler);
            inline.Child = replacement;
            bool caught = false;
            try { Layout(viewer, 380); }
            catch (InvalidOperationException error) when (error.Message == "expected post-commit Updated failure") { caught = true; }
            object cache = InlineCache(inline)!;
            Require(caught && sawCommittedCache && ReferenceEquals(InlineRoot(cache), replacement) && InlineRoot(oldCache) == null &&
                ReferenceEquals(VisualTreeHelper.GetParent(replacement), cache), "Updated failure undid a committed inline page.");
            updated.RemoveEventHandler(view, handler); handler = null;
            Layout(viewer, 380);
            Require(ReferenceEquals(InlineCache(inline), cache) && ColorPixels(Raster(viewer), Colors.Red) > 100 && InlineColorCount(Raster(viewer), new Rect(0, 0, 380, 220), Colors.Blue) > 10, "A post-commit Updated failure did not retain and recover the committed replacement.");
            return JsonSerializer.Serialize(new { caught, sawCommittedCache, replacementRetained = true, recovery = true });
        }
        finally
        {
            if (handler != null) updated!.RemoveEventHandler(view, handler);
            viewer.Document = null; Layout(viewer, 380);
        }
    }

    private static string InlineAttachmentInvalidation()
    {
        var child = new Border { Width = 60, Height = 24, Background = Brushes.Lime };
        var inline = new InlineUIContainer(child);
        var document = BlockDocument(); document.Background = Brushes.LightYellow;
        document.Blocks.Add(new Paragraph(inline) { Margin = new Thickness(0) });
        var viewer = BlockViewer(document);
        EventInfo attachment = typeof(Visual).GetEvent("VisualAncestorChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Delegate? handler = null;
        try
        {
            Layout(viewer, 180);
            byte[] before = Raster(viewer); object cache = InlineCache(inline)!;
            DrawingVisual page = InlinePage(child);
            byte[] pageInkBefore = Raster(page);
            FieldInfo brushField = page.GetType().GetField("_backgroundBrush", BindingFlags.Instance | BindingFlags.NonPublic)!;
            FieldInfo boundsField = page.GetType().GetField("_renderBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? brushBefore = brushField.GetValue(page), boundsBefore = boundsField.GetValue(page);
            string pageBefore = InlinePageBackground(page);
            Size viewerBefore = viewer.RenderSize, childBefore = child.RenderSize;
            object? parentBefore = VisualTreeHelper.GetParent(child);
            bool armed = true, reached = false;
            Action<object, object> callback = (sender, args) =>
            {
                if (!armed) return;
                for (DependencyObject? ancestor = child; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                    if (ancestor.GetType().FullName == "MS.Internal.PtsHost.PageVisual")
                    {
                        armed = false; reached = true;
                        document.Background = Brushes.Magenta;
                        break;
                    }
            };
            handler = Delegate.CreateDelegate(attachment.EventHandlerType!, callback.Target, callback.Method);
            attachment.GetAddMethod(true)!.Invoke(child, new object[] { handler });
            viewer.Width = 380; viewer.Measure(new Size(380, 220)); viewer.Arrange(new Rect(0, 0, 380, 220));
            byte[] after = Raster(viewer);
            int different = Enumerable.Range(0, before.Length).FirstOrDefault(index => before[index] != after[index], -1);
            bool pageInkRestored = Raster(page).SequenceEqual(pageInkBefore);
            bool backgroundCacheRestored = ReferenceEquals(brushField.GetValue(page), brushBefore) && Equals(boundsField.GetValue(page), boundsBefore);
            Require(reached && ReferenceEquals(document.Background, Brushes.Magenta) && ReferenceEquals(InlineCache(inline), cache) &&
                ReferenceEquals(VisualTreeHelper.GetParent(child), parentBefore) && child.RenderSize == childBefore && pageInkRestored && backgroundCacheRestored,
                JsonSerializer.Serialize(new { failure = "Legal attachment invalidation committed stale layout, undid the model change or failed to restore the displayed background.",
                    reached, pageInkRestored, backgroundCacheRestored, model = document.Background.ToString(), cacheSame = ReferenceEquals(InlineCache(inline), cache),
                    parentSame = ReferenceEquals(VisualTreeHelper.GetParent(child), parentBefore), childBefore = childBefore.ToString(), childAfter = child.RenderSize.ToString(),
                    viewerBefore = viewerBefore.ToString(), viewerAfter = viewer.RenderSize.ToString(), pageBefore, pageAfter = InlinePageBackground(page),
                    firstDifference = different < 0 ? null : new { x = different / 4 % 380, y = different / 4 / 380, channel = different % 4, before = before[different], after = after[different] },
                    magentaBefore = ColorPixels(before, Colors.Magenta), magentaAfter = ColorPixels(after, Colors.Magenta),
                    yellowBefore = ColorPixels(before, Colors.LightYellow), yellowAfter = ColorPixels(after, Colors.LightYellow) }));
            Layout(viewer, 380);
            Require(ColorPixels(Raster(viewer), Colors.Magenta) > 100 && ReferenceEquals(InlineCache(inline), cache), "Attachment invalidation was lost instead of reaching normal reformat.");
            document.Background = Brushes.LightYellow; Layout(viewer, 180);
            Require(Raster(viewer).SequenceEqual(before), "Background restoration left cached brush/bounds inconsistent.");
            return JsonSerializer.Serialize(new { reached, pageInkRestored, backgroundCacheRestored, previousBackgroundRestored = true, modelPreserved = true, normalReformat = true, recovery = true });
        }
        finally
        {
            if (handler != null) attachment.GetRemoveMethod(true)!.Invoke(child, new object[] { handler });
            viewer.Document = null; Layout(viewer, 380);
        }
    }

    private static DrawingVisual InlinePage(Visual child)
    {
        for (DependencyObject? ancestor = child; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor.GetType().FullName == "MS.Internal.PtsHost.PageVisual") return (DrawingVisual)ancestor;
        throw new InvalidOperationException("The committed inline child has no PageVisual ancestor.");
    }

    private static string InlinePageBackground(DrawingVisual page) => JsonSerializer.Serialize(new
    {
        drawingBounds = page.Drawing?.Bounds.ToString(),
        drawing = page.Drawing?.Children.OfType<GeometryDrawing>().Select(drawing => new { brush = drawing.Brush?.ToString(), bounds = drawing.Bounds.ToString() }).ToArray(),
        cachedBrush = page.GetType().GetField("_backgroundBrush", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page)?.ToString(),
        cachedBounds = page.GetType().GetField("_renderBounds", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page)?.ToString()
    });

    private static string InlineRestorationFailure()
    {
        var retained = new RestorationThrowingBorder { Height = 25, Background = Brushes.Blue,
            Child = new Viewbox { Child = new System.Windows.Shapes.Rectangle { Width = 400, Height = 50, Fill = Brushes.Lime } } };
        var inline = new InlineUIContainer(retained);
        var paragraph = new Paragraph(inline) { Margin = new Thickness(0) };
        var second = new Border { Height = 20, Background = Brushes.Red };
        var document = BlockDocument(); document.Blocks.Add(paragraph); document.Blocks.Add(new BlockUIContainer(second));
        var viewer = BlockViewer(document);
        try
        {
            Layout(viewer, 380);
            object cache = InlineCache(inline)!;
            double oldWidth = second.RenderSize.Width;
            var rejected = new CallbackThrowingChild(() => retained.Fail = true);
            var added = new InlineUIContainer(rejected); paragraph.Inlines.Add(added);
            AggregateException? failure = null;
            try { Layout(viewer, 180); } catch (AggregateException error) { failure = error; }
            bool primary = failure?.InnerExceptions[0].Message == "expected child arrange failure";
            bool restoration = failure?.InnerExceptions.Skip(1).Any(error => error.Message == "expected restoration failure") == true;
            Require(primary && restoration && second.RenderSize.Width == oldWidth && ReferenceEquals(InlineCache(inline), cache) &&
                ReferenceEquals(InlineRoot(cache), retained) && VisualTreeHelper.GetParent(rejected) == null,
                "Inline restoration failure hid the primary error, skipped another child or stranded ownership.");
            retained.Fail = false; paragraph.Inlines.Remove(added); Layout(viewer, 380);
            Require(ReferenceEquals(InlineCache(inline), cache) && second.RenderSize.Width == oldWidth, "Inline restoration failure did not recover.");
            return JsonSerializer.Serialize(new { primary, restoration, remainingChildRestored = true, cacheRetained = true, recovery = true });
        }
        finally { retained.Fail = false; viewer.Document = null; Layout(viewer, 380); }
    }
    private sealed class InlineArrangeCallback : Border
    {
        internal Action? Callback;
        protected override Size ArrangeOverride(Size finalSize)
        {
            Size result = base.ArrangeOverride(finalSize);
            Action? callback = Callback; Callback = null; callback?.Invoke();
            return result;
        }
    }
}
