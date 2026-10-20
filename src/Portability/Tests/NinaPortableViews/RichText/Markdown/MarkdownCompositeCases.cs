using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using NINA.Plugin.ManifestDefinition;

internal static partial class Program
{
    private static readonly string[] CompositeChecks = [
        "composite-original-BAML-model-binding-callback", "composite-original-DOM-and-styles",
        "composite-original-async-image-loader", "composite-table-and-child-layout",
        "composite-inline-image-retained-pixels", "composite-original-readonly-editor-selection",
        "composite-document-selection-forward-reverse", "composite-outer-scroll-return",
        "composite-width-A-B-A", "composite-original-command-enabled-disabled-restored",
        "composite-model-replace-restore", "composite-host-close"
    ];

    private static void RunMarkdownComposite()
    {
        var originalView = new NINA.View.Plugins.PluginsView();
        var outer = (ScrollViewer)originalView.FindName("DescriptionContent");
        var viewer = Graph(outer).OfType<MdXaml.MarkdownScrollViewer>().Single();
        var originalParent = outer.Parent as Panel;
        Require(originalParent != null && !originalView.IsLoaded && originalView.DataContext == null,
            "Original Description subtree must be detached before catalog-bearing view loading.");
        var binding = BindingOperations.GetBinding(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty);
        Require(binding?.Path.Path == "Descriptions.LongDescription" && binding.NotifyOnTargetUpdated,
            "Original BAML Markdown binding differs.");
        originalParent!.Children.Remove(outer);
        var window = new Window { Width = 780, Height = 320, ShowInTaskbar = false,
            ShowActivated = false, Title = "Original NINA Markdown composite", Content = outer };
        string inputs = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "CompositeInputs");
        string pngPath = Path.Combine(inputs, "local.png");
        string markdown = File.ReadAllText(Path.Combine(inputs, "composite.md"))
            .Replace("LOCAL_PNG_URI", new Uri(pngPath).AbsoluteUri, StringComparison.Ordinal);
        var description = new PluginDescription { LongDescription = markdown };
        var manifest = new PluginManifest { Descriptions = description };
        int transfers = 0;
        FlowDocument? document = null;
        InlineUIContainer? imageInline = null;
        UIElement? loadingChild = null;
        viewer.TargetUpdated += (_, e) =>
        {
            if (e.Property != MdXaml.MarkdownScrollViewer.MarkdownProperty) return;
            transfers++;
            var transferredDocument = viewer.GetValue(FlowDocumentScrollViewer.DocumentProperty) as FlowDocument;
            var inline = transferredDocument == null ? null : Graph(transferredDocument).OfType<InlineUIContainer>().SingleOrDefault();
            if (document == null && viewer.Markdown == markdown)
            {
                document = transferredDocument;
                imageInline = inline;
                loadingChild = inline?.Child;
            }
            observations.Add(new { stage = "composite-original-TargetUpdated", transfers,
                bindingStatus = BindingOperations.GetBindingExpression(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty)?.Status.ToString(),
                documentIdentity = transferredDocument == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(transferredDocument),
                child = inline?.Child?.GetType().FullName, label = (inline?.Child as Label)?.Content?.ToString(),
                actualManifest = ReferenceEquals(viewer.DataContext, manifest) });
        };
        try
        {
            Check(CompositeChecks[0], () =>
            {
                // Supply the genuine DisplayManifest value at the detached Description boundary.
                outer.DataContext = manifest;
                BindingOperations.GetBindingExpression(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty)!.UpdateTarget();
                observations.Add(new { stage = "composite-before-queued-transfer", transfers,
                    bindingStatus = BindingOperations.GetBindingExpression(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty)?.Status.ToString(),
                    markdownLength = viewer.Markdown.Length });
                Settle();
                Require(document != null && imageInline != null && ReferenceEquals(document, viewer.Document),
                    "Original TargetUpdated did not expose the current composite document and inline.");
                Require(loadingChild is Label label && label.Content?.ToString()?.StartsWith("Load file:", StringComparison.Ordinal) == true,
                    "Original asynchronous local-image placeholder was not observed during TargetUpdated.");
                Require(transfers > 0 && viewer.Markdown == description.LongDescription && ReferenceEquals(viewer.DataContext, manifest),
                    "Original binding did not transfer the genuine model value.");
                Require(ReferenceEquals(BindingOperations.GetBinding(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty), binding),
                    "Original Markdown binding was replaced.");
                var lists = Graph(document!).OfType<System.Windows.Documents.List>().ToArray();
                Require(lists.Where(l => l.MarkerStyle != TextMarkerStyle.Decimal).All(l =>
                    l.ReadLocalValue(System.Windows.Documents.List.MarkerStyleProperty) == DependencyProperty.UnsetValue && l.MarkerStyle == TextMarkerStyle.Box),
                    "Original TargetUpdated callback did not clear parser-local bullet markers into NINA's Box style.");
                Require(!originalView.IsLoaded && PresentationSource.FromVisual(originalView) == null,
                    "Full PluginsView was loaded.");
                observations.Add(new { stage = CompositeChecks[0], transfers, actualModel = manifest.GetType().FullName,
                    originalHandler = "PluginsView.OnMarkdownUpdated -> FormatMarkdown", fullViewLoaded = false,
                    boundary = "actual DescriptionContent subtree; genuine DisplayManifest supplied directly", pngPath });
            });
            Check(CompositeChecks[1], () => AssertCompositeDom(viewer));
            Check(CompositeChecks[2], () =>
            {
                AwaitCompositeImage(viewer);
                Require(ReferenceEquals(imageInline, Graph(viewer.Document!).OfType<InlineUIContainer>().Single()) &&
                    !ReferenceEquals(loadingChild, imageInline!.Child), "Original inline identity or asynchronous child replacement differs.");
                var image = (Image)imageInline!.Child;
                var bitmap = image.Source as BitmapImage;
                Require(bitmap is { PixelWidth: 2, PixelHeight: 2, UriSource: null, CacheOption: BitmapCacheOption.OnLoad,
                    CreateOptions: BitmapCreateOptions.None, DecodePixelWidth: 0, DecodePixelHeight: 0 } && bitmap.StreamSource is FileStream,
                    "Original stream-based local PNG loader contract differs.");
                Require(image.Width == 2 && double.IsNaN(image.Height) && image.Stretch == Stretch.Uniform &&
                    RenderOptions.GetBitmapScalingMode(image) == BitmapScalingMode.NearestNeighbor, "Original image sizing/style differs.");
                observations.Add(new { stage = CompositeChecks[2], placeholder = loadingChild!.GetType().FullName,
                    child = image.GetType().FullName, source = bitmap!.GetType().FullName,
                    symbolLength = imageInline.ContentStart.GetOffsetToPosition(imageInline.ContentEnd), bitmap.CacheOption });
            });
            Check(CompositeChecks[3], () =>
            {
                window.Show(); SettleLayout(window);
                Require(viewer.ActualWidth > 0 && viewer.ActualHeight > 0 && !originalView.IsLoaded, "Actual subtree did not lay out independently.");
                var cells = Graph(document!).OfType<TableCell>().ToArray();
                foreach (var cell in cells) Require(!cell.ContentStart.GetCharacterRect(LogicalDirection.Forward).IsEmpty, "Table cell lacks public geometry.");
                var editor = Graph(viewer).OfType<TextEditor>().Single();
                editor.TextArea.TextView.EnsureVisualLines();
                Require(editor.ActualWidth > 0 && editor.ActualHeight > 0 && editor.TextArea.TextView.VisualLines.Count > 0,
                    "Original AvalonEdit code child has no measured lines.");
                Require(imageInline!.Child.RenderSize.Width > 0 && imageInline.Child.RenderSize.Height > 0, "Original Image child was not arranged.");
            });
            Check(CompositeChecks[4], () => AssertCompositeImagePixels(window, outer, (Image)imageInline!.Child));
            Check(CompositeChecks[5], () =>
            {
                var editor = Graph(viewer).OfType<TextEditor>().Single();
                Require(Canonical(editor.Text) == "int genuine = 42;" && editor.IsReadOnly, "Original fenced code or readonly policy differs.");
                AssertStyle(editor);
                string code = editor.Text;
                foreach (var range in new (int Start, int Length)[] { (3, 4), (3, 9), (3, 2), (0, 3), (12, 3), (3, 0) })
                {
                    editor.Select(range.Start, range.Length);
                    Require(editor.SelectionStart == range.Start && editor.SelectionLength == range.Length &&
                        editor.SelectedText == code.Substring(range.Start, range.Length), "Original code child selection differs.");
                }
                Require(Canonical(editor.Text) == "int genuine = 42;" && editor.IsReadOnly, "Selection changed the actual code child.");
            });
            Check(CompositeChecks[6], () =>
            {
                var first = Graph(document!).OfType<Run>().Single(r => r.Text == "Outer item");
                var last = Graph(document!).OfType<Run>().Single(r => r.Text == "34");
                var start = first.ContentStart; var end = last.ContentEnd;
                var endRow = Graph(document!).OfType<TableRow>().Single(row => Graph(row).Contains(last));
                var normalizedEnd = endRow.ContentEnd;
                string expected = new TextRange(start, end).Text;
                Require(expected.Contains("Nested item", StringComparison.Ordinal) && expected.Contains("Alpha", StringComparison.Ordinal),
                    "Selection does not cross the nested list and table.");
                viewer.Selection.Select(start, end);
                Require(viewer.Selection.Text == expected && viewer.Selection.Start.CompareTo(start) == 0 && viewer.Selection.End.CompareTo(normalizedEnd) == 0,
                    "Forward actual document selection differs.");
                viewer.Selection.Select(end, start);
                Require(viewer.Selection.Text == expected && viewer.Selection.Start.CompareTo(start) == 0 && viewer.Selection.End.CompareTo(normalizedEnd) == 0,
                    "Reverse actual document selection differs.");
                viewer.Selection.Select(start, start); Require(viewer.Selection.IsEmpty, "Actual document selection did not clear.");
                var imageStart = imageInline!.ElementStart; var imageEnd = imageInline.ElementEnd;
                Require(imageInline.ContentStart.GetOffsetToPosition(imageInline.ContentEnd) == 1 &&
                    imageStart.GetOffsetToPosition(imageEnd) == 3 && imageStart.IsAtInsertionPosition && imageEnd.IsAtInsertionPosition,
                    "Original image must retain one embedded symbol and its two real element edges at public selection boundaries.");
                viewer.Selection.Select(imageStart, imageEnd);
                observations.Add(new { stage = "composite-image-selection-boundaries",
                    contentStart = document!.ContentStart.GetOffsetToPosition(imageInline.ContentStart), contentEnd = document.ContentStart.GetOffsetToPosition(imageInline.ContentEnd),
                    elementStart = document.ContentStart.GetOffsetToPosition(imageStart), elementEnd = document.ContentStart.GetOffsetToPosition(imageEnd),
                    selectedStart = document.ContentStart.GetOffsetToPosition(viewer.Selection.Start), selectedEnd = document.ContentStart.GetOffsetToPosition(viewer.Selection.End), viewer.Selection.IsEmpty });
                Require(viewer.Selection.Start.CompareTo(imageStart) == 0 && viewer.Selection.End.CompareTo(imageEnd) == 0, "Forward image selection lost its source symbol.");
                viewer.Selection.Select(imageEnd, imageStart);
                Require(viewer.Selection.Start.CompareTo(imageStart) == 0 && viewer.Selection.End.CompareTo(imageEnd) == 0, "Reverse image selection lost its source symbol.");
                viewer.Selection.Select(start, start);
                observations.Add(new { stage = CompositeChecks[6], serializedText = expected,
                    start = document!.ContentStart.GetOffsetToPosition(start), requestedEnd = document.ContentStart.GetOffsetToPosition(end),
                    normalizedEnd = document.ContentStart.GetOffsetToPosition(normalizedEnd),
                    normalization = "original BuildCrossTableSelection extends the endpoint to TableRow.ContentEnd" });
            });
            Check(CompositeChecks[7], () =>
            {
                outer.ScrollToBottom(); SettleLayout(window); double bottom = outer.VerticalOffset;
                Require(bottom > 0, "Actual original outer scrolling did not advance.");
                outer.ScrollToTop(); SettleLayout(window); Require(outer.VerticalOffset == 0, "Original outer scrolling did not return.");
                observations.Add(new { stage = CompositeChecks[7], bottom, returned = outer.VerticalOffset });
            });
            Check(CompositeChecks[8], () =>
            {
                var pointers = Graph(document!).OfType<Run>().Select(r => r.ContentStart).ToArray();
                var before = CompositeGeometry(pointers); string a = CompositeRaster(window, "composite-width-A").Hash;
                window.Width = 420; SettleLayout(window);
                var changed = CompositeGeometry(pointers); string b = CompositeRaster(window, "composite-width-B").Hash;
                Require(!before.SequenceEqual(changed) && a != b, "Intermediate width did not change actual geometry and retained raster.");
                window.Width = 780; SettleLayout(window);
                var restored = CompositeGeometry(pointers); string again = CompositeRaster(window, "composite-width-A-restored").Hash;
                Require(before.SequenceEqual(restored) && a == again && ReferenceEquals(viewer.Document, document),
                    "Width restoration changed document identity, public geometry or same-host raster.");
                observations.Add(new { stage = CompositeChecks[8], before, changed, restored, a, b, again });
            });
            Check(CompositeChecks[9], () => CompositeCommandPair(viewer));
            Check(CompositeChecks[10], () =>
            {
                var old = document!; var oldImage = imageInline!.Child;
                int before = transfers;
                description.LongDescription = markdown.Replace("Alpha", "Gamma", StringComparison.Ordinal);
                BindingOperations.GetBindingExpression(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty)!.UpdateTarget();
                AwaitCompositeImage(viewer); SettleLayout(window);
                Require(transfers > before && !ReferenceEquals(old, viewer.Document) && !Graph(viewer).Contains(oldImage), "Original model update retained the old document/child.");
                Require(new TextRange(viewer.Document!.ContentStart, viewer.Document.ContentEnd).Text.Contains("Gamma", StringComparison.Ordinal), "Original parser did not receive the changed model.");
                CompositeRaster(window, "composite-model-changed");
                description.LongDescription = markdown;
                BindingOperations.GetBindingExpression(viewer, MdXaml.MarkdownScrollViewer.MarkdownProperty)!.UpdateTarget();
                AwaitCompositeImage(viewer); SettleLayout(window); AssertCompositeDom(viewer);
                Require(viewer.Markdown == markdown && !Graph(viewer).Contains(oldImage), "Model restoration retained stale child ownership.");
                CompositeRaster(window, "composite-model-restored");
            });
        }
        finally { window.Close(); window.Content = null; outer.ClearValue(FrameworkElement.DataContextProperty); Settle(); }
        Check(CompositeChecks[11], () => Require(!window.IsVisible && !outer.IsLoaded && !originalView.IsLoaded &&
            PresentationSource.FromVisual(outer) == null, "Composite host/subtree remains attached after close."));
    }

    private static void AssertCompositeDom(MdXaml.MarkdownScrollViewer viewer)
    {
        var nodes = Graph(viewer.Document!).ToArray();
        var table = nodes.OfType<Table>().Single();
        var rows = table.RowGroups.Cast<TableRowGroup>().SelectMany(g => g.Rows.Cast<TableRow>()).ToArray();
        var cells = rows.SelectMany(r => r.Cells.Cast<TableCell>()).ToArray();
        Require(table.Columns.Count == 2 && table.Columns.Cast<TableColumn>().All(c => c.Width.IsAuto) && table.RowGroups.Count == 2 &&
            rows.Length == 3 && cells.Length == 6 && cells.All(c => c.ColumnSpan == 1 && c.RowSpan == 1), "Original genuine Table structure differs.");
        Require(table.CellSpacing == 0 && table.BorderThickness == new Thickness(.5) &&
            cells.All(c => c.Padding == new Thickness(2) && c.BorderThickness == new Thickness(.5)), "Original table style differs.");
        Require(cells.Where((_, i) => i % 2 == 0).All(c => c.TextAlignment == TextAlignment.Left) &&
            cells.Where((_, i) => i % 2 == 1).All(c => c.TextAlignment == TextAlignment.Right), "Original table column alignment differs.");
        Require(nodes.OfType<System.Windows.Documents.List>().Count() == 3 && nodes.OfType<ListItem>().Count() == 5 &&
            nodes.OfType<Section>().Count() == 1 && nodes.OfType<Hyperlink>().Count() == 1 &&
            nodes.OfType<InlineUIContainer>().Count() == 1 && nodes.OfType<BlockUIContainer>().Count() == 2,
            "Parser replaced or omitted a declared composite structure.");
        Require(ReferenceEquals(viewer.MarkdownStyle, app!.Resources["MarkdownStyle"]), "Original NINA document style identity changed.");
        Require(nodes.OfType<System.Windows.Documents.List>().Where(l => l.MarkerStyle != TextMarkerStyle.Decimal).All(l =>
            l.MarkerStyle == TextMarkerStyle.Box && l.ReadLocalValue(System.Windows.Documents.List.MarkerStyleProperty) == DependencyProperty.UnsetValue),
            "Original callback marker correction did not survive the current model transfer.");
    }

    private static void AwaitCompositeImage(MdXaml.MarkdownScrollViewer viewer)
    {
        var inline = Graph(viewer.Document!).OfType<InlineUIContainer>().Single();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (inline.Child is Image || elapsed.ElapsedMilliseconds >= 8000) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(inline.Child is Image, "Original local image loader failed/timed out: " + (inline.Child as Label)?.Content);
    }

    private static Rect[] CompositeGeometry(TextPointer[] pointers) => pointers.Select(p =>
    {
        var rect = p.GetCharacterRect(LogicalDirection.Forward);
        Require(!rect.IsEmpty && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && rect.Height > 0, "Public text geometry is missing.");
        return rect;
    }).ToArray();

    private static (string Hash, byte[] Pixels, int Width, int Height) CompositeRaster(Window window, string name)
    {
        int width = (int)Math.Ceiling(window.ActualWidth), height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        string hash = Convert.ToHexString(SHA256.HashData(pixels));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(Path.GetDirectoryName(result)!, name + ".png");
        using (var stream = File.Create(path)) encoder.Save(stream);
        observations.Add(new { stage = "composite-retained-raster", name, width, height, hash, path });
        return (hash, pixels, width, height);
    }

    private static void AssertCompositeImagePixels(Window window, ScrollViewer outer, Image image)
    {
        var beforeScroll = image.TransformToAncestor(window).TransformBounds(new Rect(image.RenderSize));
        outer.ScrollToBottom(); SettleLayout(window);
        var afterScroll = image.TransformToAncestor(window).TransformBounds(new Rect(image.RenderSize));
        var viewport = outer.TransformToAncestor(window).TransformBounds(new Rect(outer.RenderSize));
        observations.Add(new { stage = "composite-image-viewport", beforeScroll, afterScroll, viewport, outer.VerticalOffset });
        Require(viewport.Contains(afterScroll), "Actual image is outside the original outer viewport before capture.");
        var bitmap = (BitmapSource)image.Source;
        Require(bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32 || bitmap.Format == PixelFormats.Bgr32,
            "Selected PNG format changed; do not silently introduce a conversion graph.");
        var source = new byte[16]; bitmap.CopyPixels(source, 8, 0);
        byte[] pinnedBgra = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 255, 255, 255];
        bool hasAlpha = bitmap.Format != PixelFormats.Bgr32;
        for (int i = 0; i < source.Length; i++)
            Require((i % 4 == 3 && !hasAlpha) || source[i] == pinnedBgra[i], "Decoded PNG channel order/value differs from the pinned opaque 2x2 source.");
        var bounds = image.TransformToAncestor(window).TransformBounds(new Rect(image.RenderSize));
        var raster = CompositeRaster(window, "composite-inline-image");
        var samples = new List<(int X, int Y, int R, int G, int B, int A)>();
        for (int y = Math.Max(0, (int)Math.Floor(bounds.Top) - 1); y < Math.Min(raster.Height, Math.Ceiling(bounds.Bottom) + 1); y++)
            for (int x = Math.Max(0, (int)Math.Floor(bounds.Left) - 1); x < Math.Min(raster.Width, Math.Ceiling(bounds.Right) + 1); x++)
            {
                int i = (y * raster.Width + x) * 4;
                samples.Add((x, y, raster.Pixels[i + 2], raster.Pixels[i + 1], raster.Pixels[i], raster.Pixels[i + 3]));
            }
        bool Matches((int X, int Y, int R, int G, int B, int A) p, int index) => index switch
        {
            0 => p.R - Math.Max(p.G, p.B) >= 64 && Math.Abs(p.G - p.B) <= 1,
            1 => p.G - Math.Max(p.R, p.B) >= 64 && Math.Abs(p.R - p.B) <= 1,
            2 => p.B - Math.Max(p.R, p.G) >= 64 && Math.Abs(p.R - p.G) <= 1,
            3 => Math.Min(p.R, p.G) - p.B >= 64 && Math.Abs(p.R - p.G) <= 1,
            _ => false
        };
        var classes = Enumerable.Range(0, 4).Select(index => samples.Where(p => Matches(p, index)).ToArray()).ToArray();
        bool ordered = classes[0].Any(red => classes[1].Any(green => classes[2].Any(blue => classes[3].Any(yellow =>
            red.X < green.X && blue.X < yellow.X && red.Y < blue.Y && green.Y < yellow.Y &&
            red.X == blue.X && green.X == yellow.X && red.Y == green.Y && blue.Y == yellow.Y))));
        observations.Add(new { stage = CompositeChecks[4], bounds, sourceFormat = bitmap.Format.ToString(),
            sourceBgra = source.Select(v => (int)v).ToArray(), hasAlpha, sourceClassOrder = new[] { "red", "green", "blue", "yellow" },
            rendered = samples.Select(p => new { p.X, p.Y, p.R, p.G, p.B, p.A,
                visibleChroma = Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) }).ToArray(),
            matchesPerClass = classes.Select(c => c.Length).ToArray(), ordered,
            claim = "source identity and ordered retained presence with white-edge coverage; no pixel-parity claim" });
        Require(classes.All(c => c.Length > 0) && ordered, "Distinctive source-color classes or their row/column order are absent from the retained document image region.");
        outer.ScrollToTop(); SettleLayout(window);
    }

    private static void CompositeCommandPair(MdXaml.MarkdownScrollViewer viewer)
    {
        var link = Graph(viewer.Document!).OfType<Hyperlink>().Single();
        var wrapper = link.Command;
        Require(wrapper?.GetType() == typeof(MdXaml.LinkActions.FlowDocumentJumpAnchorIfNecessary), "Original OpenBrowser wrapper differs.");
        var field = wrapper!.GetType().GetField("_command", BindingFlags.Instance | BindingFlags.NonPublic);
        var existing = field?.GetValue(wrapper);
        Require(existing?.GetType() == typeof(MdXaml.LinkActions.OpenCommand), "Original existing OpenCommand differs.");
        var command = (MdXaml.LinkActions.OpenCommand)existing!;
        bool initial = command.IsExecutable;
        object? parameter = link.CommandParameter;
        Require(parameter is string target && target == "../guide.md" && link.NavigateUri == null, "Description callback link semantics differ from original source.");
        try
        {
            foreach (bool enabled in new[] { true, false, true })
            {
                command.IsExecutable = enabled; Settle();
                Require(ReferenceEquals(link.Command, wrapper) && ReferenceEquals(link.CommandParameter, parameter) &&
                    wrapper.CanExecute(parameter) == enabled && link.IsEnabled == enabled, "Real Hyperlink did not follow the existing command's CanExecuteChanged.");
                observations.Add(new { stage = CompositeChecks[9], enabled, link.IsEnabled, actualCanExecute = wrapper.CanExecute(parameter),
                    readOnlyUnwrap = "existing FlowDocumentJumpAnchorIfNecessary._command", executeInvoked = false, clickInvoked = false });
            }
        }
        finally { command.IsExecutable = initial; }
    }
}
