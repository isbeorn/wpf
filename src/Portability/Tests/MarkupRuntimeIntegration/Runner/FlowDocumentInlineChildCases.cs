using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static partial class FlowDocumentCases
{
    internal static string InlineChild()
    {
        _ = Application.Current ?? new Application();
        var label = new Label { Content = "Load local.png", Width = 100, Height = 32, Background = Brushes.Lime };
        var inline = new InlineUIContainer(label);
        var first = new Run("BEFORE "); var last = new Run(" AFTER");
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(first); paragraph.Inlines.Add(inline); paragraph.Inlines.Add(last);
        var document = BlockDocument(); document.Blocks.Add(paragraph);
        var viewer = BlockViewer(document);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo cache = typeof(InlineUIContainer).GetField("_uiElementIsland", flags)!;
        var observations = new List<object>();
        int symbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
        string serialized = new TextRange(document.ContentStart, document.ContentEnd).Text;
        object Island() => cache.GetValue(inline)!;
        object? Root(object island) => island.GetType().GetProperty("Root", flags)!.GetValue(island);
        Point ChildOrigin(FrameworkElement child) => child.TransformToAncestor(BlockScope(viewer)).Transform(new Point());
        double Baseline() => BlockGlyphs(viewer).First(glyph => glyph.Text.Contains("BEFORE", StringComparison.Ordinal)).Baseline.Y;
        void Observe(string phase, FrameworkElement child)
        {
            observations.Add(new { phase, child = child.GetType().FullName, desired = child.DesiredSize.ToString(), rendered = child.RenderSize.ToString(),
                origin = ChildOrigin(child).ToString(), baseline = Baseline(), sourceSymbols = document.ContentStart.GetOffsetToPosition(document.ContentEnd),
                inlineSymbols = inline.ContentStart.GetOffsetToPosition(inline.ContentEnd), serializer = new TextRange(document.ContentStart, document.ContentEnd).Text,
                cachedRootMatches = ReferenceEquals(Root(Island()), child), logicalParentMatches = ReferenceEquals(LogicalTreeHelper.GetParent(child), inline) });
        }
        try
        {
            Layout(viewer, 380);
            Require(inline.ContentStart.GetOffsetToPosition(inline.ContentEnd) == 1 && inline.ElementStart.GetOffsetToPosition(inline.ElementEnd) == 3,
                "InlineUIContainer did not preserve one embedded symbol plus its two original edges.");
            object labelIsland = Island();
            Require(ReferenceEquals(VisualTreeHelper.GetParent(label), labelIsland) && ReferenceEquals(Root(labelIsland), label), "Label was not hosted by its original container island.");
            Observe("label-default-baseline", label);
            Require(Math.Abs(Baseline() - ChildOrigin(label).Y - label.RenderSize.Height) < 0.05, "Default inline child baseline did not use its bottom edge.");
            byte[] labelInk = Raster(viewer); Require(ColorPixels(labelInk, Colors.Lime) > 300, "Real inline Label background has no ink.");
            label.SetValue(TextBlock.BaselineOffsetProperty, 10d); Layout(viewer, 380); Observe("label-explicit-baseline", label);
            Require(Math.Abs(Baseline() - ChildOrigin(label).Y - 10) < 0.05, "Inline child baseline offset was not honored.");
            label.ClearValue(TextBlock.BaselineOffsetProperty); Layout(viewer, 380);
            Require(Raster(viewer).SequenceEqual(labelInk) && ReferenceEquals(Island(), labelIsland), "Baseline restoration changed pixels or replaced the unchanged child island.");

            var bitmap = new BitmapImage();
            using (var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "local.png")))
            { bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); }
            var image = new Image { Source = bitmap, Width = 2, Stretch = Stretch.Uniform };
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { inline.Child = image; frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
            bool oldCacheBeforeLayout = ReferenceEquals(cache.GetValue(inline), labelIsland);
            Layout(viewer, 380); object imageIsland = Island(); Observe("queued-image-replacement", image);
            Require(bitmap.PixelWidth == 1 && bitmap.PixelHeight == 1 && image.RenderSize == new Size(2, 2), "Existing tracked OnLoad PNG did not produce a genuine 2x2 inline Image.");
            Require(!ReferenceEquals(labelIsland, imageIsland) && ReferenceEquals(Root(imageIsland), image) && ReferenceEquals(VisualTreeHelper.GetParent(image), imageIsland) &&
                ReferenceEquals(LogicalTreeHelper.GetParent(image), inline) && Root(labelIsland) == null, "Inline replacement did not retire the old island and retain one current child owner.");
            Require(document.ContentStart.GetOffsetToPosition(document.ContentEnd) == symbols && new TextRange(document.ContentStart, document.ContentEnd).Text == serialized,
                "Inline replacement changed original source symbols or plain-text serialization.");
            byte[] imageInk = Raster(viewer); Require(!imageInk.SequenceEqual(labelInk) && ColorPixels(imageInk, Colors.Lime) == 0, "Inline replacement retained stale Label ink.");
            image.Width = 12; Layout(viewer, 380); Observe("image-size-mutation", image);
            Require(image.RenderSize == new Size(12, 12) && ReferenceEquals(Island(), imageIsland), "Inline image size mutation replaced its island or retained its old size.");
            image.Width = 2; Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(imageInk), "Inline image size restoration retained stale ink.");
            Layout(viewer, 180);
            var scope = BlockScope(viewer);
            Rect lastCharacter = last.ContentStart.GetPositionAtOffset(1)!.GetCharacterRect(LogicalDirection.Forward);
            var point = new Point(lastCharacter.X + 2, lastCharacter.Y + lastCharacter.Height / 2);
            var visual = VisualTreeHelper.HitTest(scope, point)?.VisualHit;
            while (visual != null && visual is not IContentHost) visual = VisualTreeHelper.GetParent(visual) as Visual;
            IInputElement? lastNonWhitespaceHit = (visual as IContentHost)?.InputHitTest(point);
            IInputElement? leadingSpaceHit = HitFirstRun(viewer, last);
            observations.Add(new { phase = "wrapped-neighbor-hit", firstHitMatches = ReferenceEquals(HitFirstRun(viewer, first), first),
                leadingSpaceHitType = leadingSpaceHit?.GetType().FullName, leadingSpaceHitText = (leadingSpaceHit as Run)?.Text,
                nonWhitespaceHitType = lastNonWhitespaceHit?.GetType().FullName, nonWhitespaceHitText = (lastNonWhitespaceHit as Run)?.Text,
                nonWhitespacePosition = document.ContentStart.GetOffsetToPosition(last.ContentStart.GetPositionAtOffset(1)!), rectangle = lastCharacter.ToString() });
            Require(ReferenceEquals(HitFirstRun(viewer, first), first) && ReferenceEquals(lastNonWhitespaceHit, last), "Wrapped inline neighbors lost their original non-whitespace Run identity.");
            Layout(viewer, 380); Require(Raster(viewer).SequenceEqual(imageInk), "Inline width restoration retained stale ink.");
            viewer.Document = null; Layout(viewer, 380);
            bool cacheSurvivesDetach = ReferenceEquals(Island(), imageIsland) && ReferenceEquals(Root(imageIsland), image);
            bool islandDetached = VisualTreeHelper.GetParent((Visual)imageIsland) == null;
            bool viewerStillAncestor = viewer.IsAncestorOf((Visual)imageIsland);
            observations.Add(new { phase = "page-detach", cacheSurvivesDetach, islandDetached, viewerStillAncestor,
                islandParent = VisualTreeHelper.GetParent((Visual)imageIsland)?.GetType().FullName });
            Require(cacheSurvivesDetach && !viewerStillAncestor, "Page detach disposed a container-owned inline island or retained its live viewer attachment.");
            viewer.Document = document; Layout(viewer, 380);
            Require(ReferenceEquals(Island(), imageIsland) && Raster(viewer).SequenceEqual(imageInk), "Inline reattachment replaced a valid cached island or changed pixels.");
            return JsonSerializer.Serialize(new { recipe = "declared genuine Label-to-existing tracked 1x1 PNG displayed at 2x2 lifecycle, actual MdXaml callback remains a separate fixture", symbols, serialized,
                oldCacheBeforeLayout, cacheSurvivesDetach, islandDetached, viewerStillAncestor, observations });
        }
        catch (Exception error) { throw new InvalidOperationException("Inline child calibration observations: " + JsonSerializer.Serialize(observations), error); }
        finally { viewer.Document = null; Layout(viewer, 380); }
    }
}
