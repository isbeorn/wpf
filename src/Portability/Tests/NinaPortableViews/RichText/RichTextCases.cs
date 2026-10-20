using System.ComponentModel;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using Microsoft.Xaml.Behaviors;

internal static partial class Program
{
    private static readonly string[] RequiredChecks = [
        "unchanged-160-original-closure", "single-runtime-type-universe", "guarded-profile-and-log-paths",
        "original-in-memory-provider-before-preference-reads", "original-33-resource-dictionaries",
        "original-consumer-Hyperlink-dictionary", "actual-isolated-profile",
        "original-package-and-resource-identities", "detached-original-PluginsView-BAML-only",
        "consumer-recipe-bound-original-MarkdownStyle", "measured-genuine-code-editor-and-text-lines",
        "original-effective-AvalonEdit-style", "actual-codeblock-policy", "initial-control-raster",
        "outer-scroll-down-and-return", "editor-horizontal-scroll-and-return",
        "selection-extend-shrink-reverse-clear", "focus-away-and-return", "read-only-input-and-delete-policy",
        "document-replace-and-return", "fresh-close-and-reopen"
    ];
    private static readonly string Code = "// CODE_TOP\n" +
        string.Join("\n", Enumerable.Range(0, 24).Select(i => $"int sample{i:D2} = {i}; // stable scientific sample")) +
        "\n// LONG_LINE " + new string('x', 220) + "\n// CODE_END";
    private static string Markdown(string marker) => "# TOP_" + marker + "\n\nA deterministic control document.\n\n```csharp\n" + Code +
        "\n```\n\n```\nPLAIN_FENCE_" + marker + "\n```\n\n" +
        string.Join("\n\n", Enumerable.Range(0, 32).Select(i => $"Paragraph {i:D2} for {marker}. Scroll this actual document in both directions.")) +
        "\n\n# END_" + marker + "\n";

    private static void RunRichText()
    {
        Check("original-package-and-resource-identities", () =>
        {
            ObservePackage(typeof(MdXaml.MarkdownScrollViewer).Assembly, "MdXaml.g.resources");
            ObservePackage(typeof(TextEditor).Assembly, "ICSharpCode.AvalonEdit.g.resources");
            ObservePackage(Assembly.Load("MdXaml.Plugins"), null);
            ObserveBaml(typeof(NINA.View.Plugins.PluginsView), "view/plugins/pluginsview.baml");
            ObserveResource(typeof(NINA.View.Plugins.PluginsView).Assembly, "NINA.g.resources", "resources/styles/markdown.baml");
            ObserveResource(typeof(NINA.WPF.Base.Behaviors.BubbleScrollEvent).Assembly, "NINA.WPF.Base.g.resources", "resources/styles/hyperlink.baml");
        });
        Check("detached-original-PluginsView-BAML-only", () =>
        {
            var view = new NINA.View.Plugins.PluginsView();
            var description = (ScrollViewer)view.FindName("DescriptionContent");
            var markdown = Graph(description).OfType<MdXaml.MarkdownScrollViewer>().Single();
            Require(!view.IsLoaded && PresentationSource.FromVisual(view) == null && view.DataContext == null, "Original view must remain detached without a PluginsVM.");
            Require(markdown.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, "Original description recipe differs.");
            Require(ReferenceEquals(markdown.MarkdownStyle, app!.Resources["MarkdownStyle"]), "Original description style identity differs.");
            observations.Add(new { stage = "detached-original-BAML-only", type = view.GetType().AssemblyQualifiedName,
                view.IsLoaded, shown = false, catalogInvoked = false, originalHandlersExecuted = false });
        });

        var data = new DescriptionData { LongDescription = Markdown("FIRST") };
        var host = CreateHost(data);
        try
        {
            Check("consumer-recipe-bound-original-MarkdownStyle", () =>
            {
                host.Window.Show(); SettleLayout(host.Window);
                Require(host.Markdown.ActualWidth > 0 && host.Markdown.ActualHeight > 0, "Markdown control is not measured.");
                Require(ReferenceEquals(host.Markdown.MarkdownStyle, app!.Resources["MarkdownStyle"]), "Original style was replaced.");
                var binding = BindingOperations.GetBinding(host.Markdown, MdXaml.MarkdownScrollViewer.MarkdownProperty);
                Require(binding?.Path.Path == nameof(DescriptionData.LongDescription) && binding.NotifyOnTargetUpdated, "Consumer-like public Markdown binding missing.");
                Require(host.Markdown.Markdown == data.LongDescription, "Bound source did not reach the control.");
                observations.Add(new { stage = "consumer-control-recipe", fullPluginsViewShown = false, originalCodebehindHandlersClaimed = false,
                    host.Markdown.ActualWidth, host.Markdown.ActualHeight, markdownSha256 = HashText(data.LongDescription),
                    bubbleBehavior = Interaction.GetBehaviors(host.Stack).Single().GetType().AssemblyQualifiedName });
            });
            TextEditor? editor = null;
            Check("measured-genuine-code-editor-and-text-lines", () =>
            {
                editor = FindCodeEditor(host.Markdown);
                Require(Canonical(editor.Text) == Code, "Fenced code document differs.");
                editor.TextArea.TextView.EnsureVisualLines();
                Require(editor.ActualWidth > 0 && editor.ActualHeight > 0 && editor.TextArea.TextView.ActualHeight > 0 && editor.TextArea.TextView.VisualLines.Count > 0,
                    "Actual AvalonEdit TextView must have measured visual lines.");
                observations.Add(EditorObservation("measured-editor", editor));
            });
            Check("original-effective-AvalonEdit-style", () => AssertStyle(editor!));
            Check("actual-codeblock-policy", () =>
            {
                observations.Add(new { stage = "stock-or-portable-codeblock-policy", editor!.IsReadOnly, editor.Focusable,
                    textAreaFocusable = editor.TextArea.Focusable, editableApiOverride = false });
                Require(editor.IsReadOnly, "Pinned description editor is editable; stop for stock policy assessment instead of adding an editable subcase.");
            });
            Check("initial-control-raster", () => Capture(host, "initial"));
            Check("outer-scroll-down-and-return", () => ScrollPair(host));
            Check("editor-horizontal-scroll-and-return", () =>
            {
                editor!.ScrollToHorizontalOffset(100); SettleLayout(host.Window);
                double forward = editor.HorizontalOffset;
                Require(forward > 0, "Long code line did not scroll horizontally.");
                editor.ScrollToHorizontalOffset(0); SettleLayout(host.Window);
                Require(editor.HorizontalOffset == 0, "Horizontal scroll did not return.");
                observations.Add(new { stage = "horizontal-scroll", forward, returned = editor.HorizontalOffset, inputOrigin = "public control API" });
            });
            Check("selection-extend-shrink-reverse-clear", () => SelectionPair(editor!));
            Check("focus-away-and-return", () =>
            {
                Require(editor!.TextArea.Focus(), "Cannot focus actual editor TextArea."); Settle();
                Require(editor.IsKeyboardFocusWithin, "Editor focus did not attach.");
                Require(host.Neighbor.Focus(), "Cannot focus neighbor."); Settle();
                Require(host.Neighbor.IsKeyboardFocused && !editor.IsKeyboardFocusWithin, "Focus did not move away.");
                Require(editor.TextArea.Focus(), "Cannot return focus to actual editor."); Settle();
                Require(editor.IsKeyboardFocusWithin, "Focus did not return.");
                observations.Add(new { stage = "focus-pair", inputOrigin = "public WPF focus API on private host", nativeInputQualified = false });
            });
            Check("read-only-input-and-delete-policy", () =>
            {
                string before = editor!.Text, markdownBefore = data.LongDescription;
                int documentChanges = 0;
                EventHandler<ICSharpCode.AvalonEdit.Document.DocumentChangeEventArgs> changed = (_, _) => documentChanges++;
                editor.Document.Changed += changed;
                try
                {
                    editor.Select(0, 2);
                    editor.TextArea.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,
                        new TextComposition(InputManager.Current, editor.TextArea, "Z")) { RoutedEvent = TextCompositionManager.TextInputEvent });
                    bool deleteEnabled = ApplicationCommands.Delete.CanExecute(null, editor.TextArea);
                    observations.Add(new { stage = "read-only-delete-eligibility", editor.IsReadOnly, editor.SelectionLength, deleteEnabled });
                    if (deleteEnabled) ApplicationCommands.Delete.Execute(null, editor.TextArea);
                    SettleLayout(host.Window);
                    Require(editor.IsReadOnly && documentChanges == 0 && editor.Text == before && data.LongDescription == markdownBefore, "Read-only input or executed Delete mutated code or Markdown.");
                    editor.Select(0, 0);
                    observations.Add(new { stage = "read-only-input-policy", deleteEnabled, deleteExecuted = deleteEnabled,
                        documentChanges, documentUnchanged = true, markdownUnchanged = true,
                        inputOrigin = "routed TextInput plus actual Delete command execution", physicalKeyboardQualified = false });
                }
                finally { editor.Document.Changed -= changed; }
            });
            Check("document-replace-and-return", () =>
            {
                var oldEditor = editor!; string oldText = oldEditor.Text;
                data.LongDescription = Markdown("SECOND"); SettleLayout(host.Window);
                var replacement = FindCodeEditor(host.Markdown);
                Require(!ReferenceEquals(oldEditor, replacement) && host.Markdown.Markdown == data.LongDescription, "Public Markdown replacement did not replace actual editor.");
                Require(!Graph(host.Markdown).Contains(oldEditor) && !oldEditor.IsKeyboardFocusWithin && oldEditor.Text == oldText, "Old editor remains attached/active or changed.");
                Require(FlowText(host.Markdown).Contains("END_SECOND", StringComparison.Ordinal), "Replacement prose missing.");
                AssertStyle(replacement); Capture(host, "replacement");
                data.LongDescription = Markdown("FIRST"); SettleLayout(host.Window);
                var restored = FindCodeEditor(host.Markdown);
                Require(!ReferenceEquals(restored, replacement) && Canonical(restored.Text) == Code && FlowText(host.Markdown).Contains("END_FIRST", StringComparison.Ordinal), "Original Markdown did not return.");
                Require(oldEditor.Text == oldText, "Detached original document changed during replacement.");
                observations.Add(new { stage = "document-replace-return", actualEditorsRecreated = true, oldDocumentUnchanged = true, sourceSha256 = HashText(data.LongDescription) });
            });
        }
        finally { host.Window.Close(); Settle(); }
        Check("fresh-close-and-reopen", () =>
        {
            Require(!host.Window.IsVisible, "First host remains visible after close.");
            var fresh = CreateHost(new DescriptionData { LongDescription = Markdown("REOPEN") });
            try
            {
                fresh.Window.Show(); SettleLayout(fresh.Window);
                var editor = FindCodeEditor(fresh.Markdown);
                Require(Canonical(editor.Text) == Code && editor.IsReadOnly, "Fresh editor content/policy differs.");
                AssertStyle(editor); SelectionPair(editor); ScrollPair(fresh); Capture(fresh, "reopen");
            }
            finally { fresh.Window.Close(); Settle(); }
            Require(!fresh.Window.IsVisible, "Fresh host failed to close.");
        });
    }

    private sealed class DescriptionData : INotifyPropertyChanged
    {
        private string value = "";
        public string LongDescription { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LongDescription))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed record Host(Window Window, ScrollViewer Outer, StackPanel Stack, MdXaml.MarkdownScrollViewer Markdown, Button Neighbor);
    private static Host CreateHost(DescriptionData data)
    {
        var markdown = new MdXaml.MarkdownScrollViewer { MarkdownStyle = (Style)app!.Resources["MarkdownStyle"], VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var clickAction = markdown.GetType().GetProperty("ClickAction")!;
        clickAction.SetValue(markdown, Enum.Parse(clickAction.PropertyType, "OpenBrowser"));
        BindingOperations.SetBinding(markdown, MdXaml.MarkdownScrollViewer.MarkdownProperty,
            new Binding(nameof(DescriptionData.LongDescription)) { Source = data, NotifyOnTargetUpdated = true });
        var stack = new StackPanel { Margin = new Thickness(0, 12, 8, 8) };
        Interaction.GetBehaviors(stack).Add(new NINA.WPF.Base.Behaviors.BubbleScrollEvent()); stack.Children.Add(markdown);
        var outer = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var neighbor = new Button { Content = "Focus neighbor", Height = 30 };
        var panel = new DockPanel(); DockPanel.SetDock(neighbor, Dock.Bottom); panel.Children.Add(neighbor); panel.Children.Add(outer);
        var window = new Window { Title = "NINA actual Markdown control diagnostic", Width = 780, Height = 520, ShowInTaskbar = false, Content = panel };
        return new Host(window, outer, stack, markdown, neighbor);
    }
    private static TextEditor FindCodeEditor(DependencyObject root)
    {
        var all = Graph(root).OfType<TextEditor>().ToArray();
        observations.Add(new { stage = "editor-discovery", count = all.Length, editors = all.Select(e => new { type = e.GetType().AssemblyQualifiedName, e.IsReadOnly, e.ActualWidth, e.ActualHeight, text = e.Text }).ToArray() });
        var editor = all.SingleOrDefault(e => Canonical(e.Text) == Code);
        Require(editor != null, "Actual AvalonEdit fenced csharp editor absent; a Paragraph substitute does not qualify.");
        Require(editor!.GetType() == typeof(TextEditor) && AssemblyLoadContext.GetLoadContext(editor.GetType().Assembly) == AssemblyLoadContext.Default, "Unexpected editor/type universe.");
        return editor;
    }
    private static void AssertStyle(TextEditor editor)
    {
        Require(editor.BorderThickness == new Thickness(0, 2, 0, 2) && editor.Padding == new Thickness(5, 0, 5, 0), "Original NINA editor spacing style missing.");
        Require(editor.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && editor.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "Original scrollbar style missing.");
        AssertBrush(editor.Background, "SecondaryBackgroundBrush", Control.BackgroundProperty);
        AssertBrush(editor.Foreground, "ButtonForegroundBrush", Control.ForegroundProperty);
        AssertBrush(editor.BorderBrush, "BorderBrush", Control.BorderBrushProperty);

        void AssertBrush(Brush actual, string resourceKey, DependencyProperty property)
        {
            var expected = (SolidColorBrush)app!.Resources[resourceKey];
            Require(actual is SolidColorBrush, "Original editor solid brush type missing: " + resourceKey);
            var effective = (SolidColorBrush)actual;
            observations.Add(new { stage = "original-editor-effective-brush", resourceKey,
                actualColor = effective.Color.ToString(), expectedColor = expected.Color.ToString(),
                sameReference = ReferenceEquals(actual, expected), effective.IsFrozen,
                valueSource = DependencyPropertyHelper.GetValueSource(editor, property).BaseValueSource.ToString() });
            Require(effective.Color == expected.Color, "Original editor brush color differs: " + resourceKey);
        }
    }
    private static void SelectionPair(TextEditor editor)
    {
        editor.Select(3, 4); Require(editor.SelectedText == Code.Substring(3, 4), "Initial selection differs.");
        editor.Select(3, 9); Require(editor.SelectionLength == 9 && editor.SelectedText == Code.Substring(3, 9), "Selection did not extend.");
        editor.Select(3, 2); Require(editor.SelectionLength == 2 && editor.SelectedText == Code.Substring(3, 2), "Selection did not shrink.");
        editor.Select(0, 3); Require(editor.SelectionStart == 0 && editor.SelectedText == "// ", "Selection did not move backward.");
        editor.Select(12, 3); Require(editor.SelectionStart == 12 && editor.SelectedText == Code.Substring(12, 3), "Selection did not move forward.");
        editor.CaretOffset = 3; editor.Select(3, 0); Require(editor.SelectionLength == 0 && editor.SelectedText == "", "Selection did not clear.");
        observations.Add(new { stage = "selection-pairs", inputOrigin = "real AvalonEdit public selection/caret API", nativeSelectionQualified = false });
    }
    private static void ScrollPair(Host host)
    {
        Require(host.Outer.ScrollableHeight > 0, "No actual vertical scroll extent.");
        Require(FlowText(host.Markdown).Contains("TOP_", StringComparison.Ordinal) && FlowText(host.Markdown).Contains("END_", StringComparison.Ordinal), "Document sentinels missing.");
        host.Outer.ScrollToBottom(); SettleLayout(host.Window); double bottom = host.Outer.VerticalOffset;
        Require(bottom > 0 && Math.Abs(bottom - host.Outer.ScrollableHeight) < 0.01, "Outer document did not reach bottom.");
        Capture(host, "scroll-bottom-" + observations.Count);
        host.Outer.ScrollToTop(); SettleLayout(host.Window); Require(host.Outer.VerticalOffset == 0, "Outer document did not return to top.");
        observations.Add(new { stage = "outer-scroll-pair", bottom, returned = host.Outer.VerticalOffset, host.Outer.ExtentHeight, host.Outer.ViewportHeight, inputOrigin = "public ScrollViewer API", nativeWheelQualified = false });
    }
    private static void SettleLayout(Window window) { Settle(); window.UpdateLayout(); Settle(); window.UpdateLayout(); }
    private static object EditorObservation(string stage, TextEditor editor) => new { stage, type = editor.GetType().AssemblyQualifiedName,
        editor.ActualWidth, editor.ActualHeight, textViewWidth = editor.TextArea.TextView.ActualWidth, textViewHeight = editor.TextArea.TextView.ActualHeight,
        visualLines = editor.TextArea.TextView.VisualLines.Count, editor.IsReadOnly, textSha256 = HashText(editor.Text) };
    private static string Canonical(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private static string FlowText(DependencyObject root) => string.Join("\n", Graph(root).OfType<FlowDocument>().Select(document => new TextRange(document.ContentStart, document.ContentEnd).Text));
    private static IEnumerable<DependencyObject> Graph(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance); var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.Count != 0)
        {
            var current = pending.Pop(); if (!seen.Add(current)) continue; yield return current;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) pending.Push(VisualTreeHelper.GetChild(current, i));
            foreach (var item in LogicalTreeHelper.GetChildren(current)) if (item is DependencyObject child) pending.Push(child);
        }
    }
    private static void Capture(Host host, string label)
    {
        int width = (int)Math.Ceiling(host.Window.ActualWidth), height = (int)Math.Ceiling(host.Window.ActualHeight);
        Require(width > 0 && height > 0, "Host raster extent empty.");
        var raster = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); raster.Render(host.Window);
        byte[] pixels = new byte[width * height * 4]; raster.CopyPixels(pixels, width * 4, 0);
        Require(pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha != 0), "Actual control raster is entirely transparent.");
        string path = Path.Combine(Path.GetDirectoryName(result)!, label + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(raster)); using (var output = File.Create(path)) encoder.Save(output);
        observations.Add(new { stage = "actual-control-raster", label, width, height, path, rawSha256 = Convert.ToHexString(SHA256.HashData(pixels)), visualCount = Graph(host.Window).OfType<Visual>().Count() });
    }
    private static void ObservePackage(Assembly assembly, string? resourceName)
    {
        Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Control loaded outside DefaultALC.");
        observations.Add(new { stage = "original-package", identity = assembly.FullName, path = assembly.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) });
        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Require(stream != null, "Original compiled resource stream missing: " + resourceName);
            using var reader = new ResourceReader(stream!);
            var entries = reader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).EndsWith(".baml", StringComparison.Ordinal)).Select(entry => (string)entry.Key).ToArray();
            Require(entries.Length != 0, "Package contains no original BAML resources.");
            observations.Add(new { stage = "original-package-BAML", identity = assembly.FullName, resourceName, entries });
        }
    }
    private static void ObserveResource(Assembly assembly, string resourceName, string key)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!; using var reader = new ResourceReader(stream);
        foreach (System.Collections.DictionaryEntry entry in reader) if ((string)entry.Key == key)
        {
            using var bytes = new MemoryStream(); ((Stream)entry.Value!).CopyTo(bytes);
            observations.Add(new { stage = "original-resource-BAML", key, length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())) }); return;
        }
        throw new InvalidDataException("Original BAML not found: " + key);
    }
}
