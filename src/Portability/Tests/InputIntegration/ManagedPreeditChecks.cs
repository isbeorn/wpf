using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Input;
using MS.Internal.Portability.Rendering;
using MS.Internal.Portability.Windowing;

internal static class ManagedPreeditChecks
{
    internal static void Run(ControlsHost host, PortablePresentationSource source, TextBox box, AdornerDecorator root, string fontPath)
    {
        box.FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans");
        var provider = (PortableInputProvider)source.GetInputProvider(typeof(KeyboardDevice));
        var model = new PreeditModel();
        var changed = new List<string>();
        var checks = new List<object>();
        var traces = new List<object>();
        box.TextChanged += (_, _) => changed.Add(box.Text);
        void Bind(PreeditModel next)
        {
            model = next;
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(PreeditModel.Value)) { Source = model,
                Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        }
        void Reset(string text = "ABCDE", bool undo = true)
        {
            box.IsReadOnly = false; box.IsEnabled = true;
            box.CharacterCasing = CharacterCasing.Normal; box.MaxLength = 0;
            Require(ReferenceEquals(Keyboard.Focus(box), box), "The real editor could not regain focus.");
            Bind(new PreeditModel { Value = text });
            box.IsUndoEnabled = false; box.IsUndoEnabled = undo;
            box.Select(Math.Min(1, text.Length), Math.Min(2, Math.Max(0, text.Length - 1)));
            root.UpdateLayout(); Drain();
            model.Writes.Clear(); changed.Clear();
        }
        void Exact(string text) => Require(box.Text == text && model.Value == text, "Actual text/binding mismatch: " + box.Text + " / " + model.Value);
        long Begin(string text, int caret) { long token = provider.BeginPreedit(text, caret); Drain(); Require(token != 0, "Focused editable TextBox rejected preedit."); return token; }
        void Record(string name, Action body)
        {
            try { body(); checks.Add(new { name, status = "pass", detail = "Actual TextBox/source/binding fixture" }); Console.WriteLine("PASS " + name); }
            catch (Exception exception) { checks.Add(new { name, status = "fail", detail = exception.ToString() }); Publish(); throw; }
        }
        void Trace(string name) => traces.Add(new { name, editor = box.Text, boundSource = model.Value,
            sourceWrites = model.Writes.ToArray(), textChanged = changed.ToArray() });
        void Publish()
        {
            string result = JsonSerializer.Serialize(new { probe = "managed-textbox-preedit", os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                scope = "Managed portable TextBox session only; no native IME/client or RichTextBox capability", checks, traces }, new JsonSerializerOptions { WriteIndented = true });
            string? path = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--preedit-results=", StringComparison.Ordinal))?[18..];
            if (path != null) File.WriteAllText(path, result);
        }

        Record("preedit-update-caret-textchanged-binding-trace", () =>
        {
            Reset(); long token = Begin("e\u0301\U0001F600", 2);
            Require(box.Text == "Ae\u0301\U0001F600DE" && box.CaretIndex == 3 && model.Writes.Count == 0 && model.Value == "ABCDE", "Start edited wrong range/caret or published candidate.");
            Require(provider.UpdatePreedit(token, "\U0001F600x", 2), "Current session update failed."); Drain();
            Require(box.Text == "A\U0001F600xDE" && box.CaretIndex == 3 && model.Writes.Count == 0 && changed.Count >= 2, "Update lost complete UTF-16 text or binding deferral.");
            Trace("active candidates deferred");
            Require(provider.CommitPreedit(token, "accepted\U0001F600"), "Commit was rejected."); Drain();
            Exact("Aaccepted\U0001F600DE");
            Require(model.Writes.SequenceEqual(new[] { box.Text }), "Completion published an old candidate or duplicate automatic source write.");
            Trace("accepted final source write");
            Require(!provider.CommitPreedit(token, "late") && !provider.UpdatePreedit(token, "late", 0), "Completed generation accepted late text.");
        });

        Record("commit-one-undo-redo-original-selection", () =>
        {
            Reset(); long token = Begin("tentative", 0);
            Require(provider.UpdatePreedit(token, "second", 6) && provider.CommitPreedit(token, "\U0001F600e\u0301"), "Commit sequence failed."); Drain();
            Exact("A\U0001F600e\u0301DE");
            box.Undo(); Drain(); Exact("ABCDE");
            Require(box.SelectionStart == 1 && box.SelectionLength == 2 && !box.CanUndo, "One undo did not restore original selected range.");
            box.Redo(); Drain(); Exact("A\U0001F600e\u0301DE");
            Require(box.CaretIndex == 5 && !box.CanRedo, "One redo did not restore final caret/text.");
        });

        Record("cancel-restores-range-selection-with-undo-disabled", () =>
        {
            foreach (bool undo in new[] { true, false })
            {
                Reset(undo: undo); long token = Begin("candidate", 4);
                Require(provider.UpdatePreedit(token, "\U0001F600", 2), "Cancel fixture update failed.");
                Require(provider.CancelPreedit(token), "Cancel rejected owned session."); Drain(); Exact("ABCDE");
                Require(box.SelectionStart == 1 && box.SelectionLength == 2 && !box.CanUndo && !box.CanRedo, "Cancel left text, selection or an undo entry.");
                Require(model.Writes.All(value => value == "ABCDE"), "Cancel published tentative text.");
                Require(!provider.CancelPreedit(token) && !provider.CommitPreedit(token, "late"), "Canceled generation remained valid.");
                Trace("cancel restored, undo=" + undo);
            }
        });

        Record("cancel-preserves-unrelated-undo-redo", () =>
        {
            Reset("ABC"); box.Select(3, 0);
            host.Report(new PortableTextInputReport(source, InputMode.Foreground, 1, "!")); Drain(); Exact("ABC!");
            box.Select(1, 1); long token = Begin("temporary", 3); provider.CancelPreedit(token); Drain(); Exact("ABC!");
            Require(box.SelectionStart == 1 && box.SelectionLength == 1 && box.CanUndo, "Cancel discarded earlier undo or original selection.");
            box.Undo(); Drain(); Exact("ABC"); Require(!box.CanUndo, "Canceled composition remained on the undo stack.");
            box.Redo(); Drain(); Exact("ABC!");
        });

        Record("empty-update-and-empty-commit", () =>
        {
            Reset(); long token = Begin("x", 1);
            Require(provider.UpdatePreedit(token, "", 0) && box.Text == "ADE", "Empty candidate did not preserve the replacement range.");
            Require(provider.UpdatePreedit(token, "next", 4) && provider.CommitPreedit(token, ""), "Empty result could not commit deletion."); Drain(); Exact("ADE");
            box.Undo(); Drain(); Exact("ABCDE");
        });

        Record("utf16-and-caret-boundaries-no-partial-update", () =>
        {
            Reset(); long token = Begin("\U0001F600", 0);
            foreach ((string text, int caret) in new[] { ("\ud800", 0), ("\udc00", 0), ("\U0001F600", 1), ("x", -1), ("x", 2) })
            {
                bool rejected = false;
                try { provider.UpdatePreedit(token, text, caret); } catch (ArgumentException) { rejected = true; }
                Require(rejected && box.Text == "A\U0001F600DE" && model.Value == "ABCDE", "Invalid UTF-16/caret partly changed the session.");
            }
            Require(provider.UpdatePreedit(token, "\U0001F600", 2) && box.CaretIndex == 3, "Maximum valid caret boundary failed.");
            provider.CancelPreedit(token); Drain(); Exact("ABCDE");
        });

        Record("handled-start-update-commit-and-pending-complete", () =>
        {
            foreach (string mode in new[] { "start", "update", "commit", "complete-request" })
            {
                Reset();
                TextCompositionEventHandler handler = (_, args) => { if (mode == "complete-request") args.TextComposition.Complete(); else args.Handled = true; };
                RoutedEvent routed = mode is "start" or "complete-request" ? TextCompositionManager.PreviewTextInputStartEvent :
                    mode == "update" ? TextCompositionManager.PreviewTextInputUpdateEvent : TextCompositionManager.PreviewTextInputEvent;
                long token = 0;
                if (mode is "update" or "commit") token = Begin("candidate", 4);
                box.AddHandler(routed, handler);
                try
                {
                    bool accepted = mode is "start" or "complete-request" ? provider.BeginPreedit("blocked", 0) != 0 :
                        mode == "update" ? provider.UpdatePreedit(token, "blocked", 0) : provider.CommitPreedit(token, "blocked");
                    Require(!accepted, "Handled/completion request was ignored: " + mode);
                }
                finally { box.RemoveHandler(routed, handler); }
                Drain(); Exact("ABCDE"); Require(!box.CanUndo, "Canceled event left composition undo: " + mode);
            }
        });

        Record("routed-text-and-selection-app-edits-abandon-stale-range", () =>
        {
            Reset();
            TextCompositionEventHandler textEdit = (_, _) => box.SetCurrentValue(TextBox.TextProperty, "application text");
            box.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, textEdit);
            try { Require(provider.BeginPreedit("candidate", 0) == 0, "App text edit did not reject stale start."); }
            finally { box.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent, textEdit); }
            Drain(); Exact("application text");
            Reset(); long token = Begin("candidate", 4);
            TextCompositionEventHandler selectionEdit = (_, _) => box.Select(0, 0);
            box.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, selectionEdit);
            try { Require(!provider.UpdatePreedit(token, "discarded", 0), "App selection edit did not reject stale update."); }
            finally { box.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent, selectionEdit); }
            Drain(); Exact("AcandidateDE");
            Require(box.SelectionStart == 0 && !provider.CommitPreedit(token, "late"), "Stale cleanup overwrote app selection or accepted late commit.");
        });

        Record("textchanged-app-edit-remains-authoritative", () =>
        {
            Reset(); bool edited = false;
            TextChangedEventHandler handler = (_, _) => { if (!edited && box.Text.Contains("candidate", StringComparison.Ordinal)) { edited = true; box.SetCurrentValue(TextBox.TextProperty, "app from TextChanged"); } };
            box.TextChanged += handler;
            try { Require(provider.BeginPreedit("candidate", 4) == 0, "TextChanged app edit did not abandon stale session."); }
            finally { box.TextChanged -= handler; }
            Drain(); Exact("app from TextChanged");
            Require(model.Writes.All(value => value == "app from TextChanged"), "Automatic source trace contains an overwritten candidate.");
        });

        Record("nested-new-generation-survives-old-update", () =>
        {
            Reset(); long old = Begin("old", 3); long nested = 0;
            TextCompositionEventHandler handler = (_, _) => { if (nested == 0) nested = provider.BeginPreedit("new", 3); };
            box.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, handler);
            try { Require(!provider.UpdatePreedit(old, "stale", 0), "Old generation survived nested replacement."); }
            finally { box.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent, handler); }
            Require(nested != 0 && box.Text == "AnewDE" && model.Value == "ABCDE", "Nested composition or binding deferral was overwritten.");
            Require(!provider.CommitPreedit(old, "stale") && provider.CommitPreedit(nested, "final"), "Generation guard blocked the new session or accepted the old one."); Drain(); Exact("AfinalDE");
            Require(model.Writes.All(value => value == "ABCDE" || value == "AfinalDE"), "Nested reset published an old candidate.");
        });

        Record("replaced-binding-is-not-flushed", () =>
        {
            Reset(); long token = Begin("candidate", 4); PreeditModel old = model;
            TextCompositionEventHandler replace = (_, _) => Bind(new PreeditModel { Value = "replacement binding" });
            box.AddHandler(TextCompositionManager.PreviewTextInputEvent, replace);
            try { Require(!provider.CommitPreedit(token, "final"), "Replaced binding retained stale composition ownership."); }
            finally { box.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, replace); }
            Drain(); Exact("replacement binding");
            Require(old.Value == "ABCDE" && old.Writes.Count == 0, "A detached/replaced binding was flushed.");
        });

        Record("focus-loss-readonly-disabled-cancel-and-recover", () =>
        {
            foreach (string mode in new[] { "focus", "readonly", "disabled" })
            {
                Reset(); long token = Begin("candidate", 4);
                if (mode == "focus") Keyboard.ClearFocus();
                else if (mode == "readonly") box.IsReadOnly = true;
                else box.IsEnabled = false;
                Drain(); Exact("ABCDE");
                Require(box.SelectionStart == 1 && box.SelectionLength == 2 && !box.CanUndo && !box.CanRedo && FindVisual(root, "CompositionAdorner") == null, "Invalidation retained candidate selection, undo or adornment: " + mode);
                Require(!provider.CommitPreedit(token, "late"), "Invalidated focus/editability accepted late commit: " + mode);
                box.IsReadOnly = false; box.IsEnabled = true;
                Require(!provider.CommitPreedit(token, "late"), "Re-enabled editor accepted canceled generation: " + mode);
                Require(ReferenceEquals(Keyboard.Focus(box), box), "Editor could not recover: " + mode);
                long next = Begin("new", 3); Require(provider.CommitPreedit(next, "ok"), "Recovered editor rejected commit."); Drain(); Exact("AokDE");
            }
        });

        Record("pending-complete-in-preview-update-and-commit", () =>
        {
            foreach (bool commit in new[] { false, true })
            {
                Reset(); long token = Begin("candidate", 4);
                TextCompositionEventHandler handler = (_, args) => args.TextComposition.Complete();
                RoutedEvent routed = commit ? TextCompositionManager.PreviewTextInputEvent : TextCompositionManager.PreviewTextInputUpdateEvent;
                box.AddHandler(routed, handler);
                try
                {
                    Require(!(commit ? provider.CommitPreedit(token, "unaccepted") : provider.UpdatePreedit(token, "unaccepted", 0)), "Preview completion request did not terminate owned input.");
                }
                finally { box.RemoveHandler(routed, handler); }
                Drain(); Exact("ABCDE");
                Require(!box.CanUndo && model.Writes.All(value => value == "ABCDE"), "Preview completion request inserted an unaccepted result.");
            }
        });

        Record("final-result-uses-editor-filter-and-caret", () =>
        {
            Reset("AB"); box.Select(1, 1); box.CharacterCasing = CharacterCasing.Lower; box.MaxLength = 4;
            long token = Begin("tentative", 9);
            Require(provider.CommitPreedit(token, "XYZ12"), "Filtered commit failed."); Drain(); Exact("Axyz");
            Require(box.CaretIndex == 4 && model.Writes.SequenceEqual(new[] { "Axyz" }), "Editor filter/caret or final source-write ordering was bypassed.");
            box.Undo(); Drain(); Exact("AB");
        });

        Record("dispatcher-affinity-and-external-edit-transaction", () =>
        {
            Reset(); long token = Begin("candidate", 4); Exception? foreign = null;
            var other = new Thread(() => { try { provider.UpdatePreedit(token, "foreign", 0); } catch (Exception exception) { foreign = exception; } });
            other.Start(); other.Join();
            Require(foreign is InvalidOperationException && box.Text == "AcandidateDE" && model.Value == "ABCDE", "Foreign dispatcher mutated the session or was not rejected.");
            provider.CancelPreedit(token); Drain(); Exact("ABCDE");
            box.BeginChange();
            try { Require(provider.BeginPreedit("nested change block", 0) == 0, "A foreign open editor transaction was captured by composition."); }
            finally { box.EndChange(); }
            Exact("ABCDE");
        });

        Record("source-handoff-and-independent-editor-owner", () =>
        {
            Reset(); long token = Begin("first candidate", 4);
            using var otherSource = new PortablePresentationSource(_ => { });
            var otherHost = new ControlsHost { Source = otherSource }; otherSource.AttachHost(otherHost);
            var otherBox = new TextBox { Width = 200, Height = 40, Template = box.Template, FontFamily = box.FontFamily };
            var otherModel = new PreeditModel { Value = "other" };
            otherBox.SetBinding(TextBox.TextProperty, new Binding(nameof(PreeditModel.Value)) { Source = otherModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var otherRoot = new AdornerDecorator { Child = otherBox }; otherSource.RootVisual = otherRoot;
            otherRoot.Measure(new Size(200, 40)); otherRoot.Arrange(new Rect(0, 0, 200, 40)); otherRoot.UpdateLayout();
            Require(ReferenceEquals(Keyboard.Focus(otherBox), otherBox), "Second source could not focus its real editor."); Drain(); Exact("ABCDE");
            Require(!provider.UpdatePreedit(token, "stale", 0), "Previous source retained its composition generation.");
            var second = (PortableInputProvider)otherSource.GetInputProvider(typeof(KeyboardDevice));
            otherBox.SelectAll(); otherModel.Writes.Clear(); long next = second.BeginPreedit("tentative", 0);
            Require(next != 0 && otherModel.Value == "other" && otherModel.Writes.Count == 0, "Second editor did not independently defer binding.");
            Require(second.CommitPreedit(next, "second final"), "Second source commit failed."); Drain();
            Require(otherBox.Text == "second final" && otherModel.Value == "second final", "Text crossed source/editor ownership.");
            otherSource.Dispose();
            Require(ReferenceEquals(Keyboard.Focus(box), box), "Surviving source could not reacquire focus.");
        });

        Record("real-window-canceled-and-accepted-closing", () =>
        {
            Reset(); PortableWindowPlatform.RegisterFactory(new ClosingWindowFactory());
            var closingBox = new TextBox { Template = box.Template, FontFamily = box.FontFamily };
            var closingModel = new PreeditModel { Value = "ABCDE" };
            closingBox.SetBinding(TextBox.TextProperty, new Binding(nameof(PreeditModel.Value)) { Source = closingModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var window = new Window { Width = 200, Height = 40, Content = closingBox, ShowActivated = false };
            bool cancel = true;
            window.Closing += (_, args) => args.Cancel = cancel;
            try
            {
                window.Show(); window.UpdateLayout(); Drain();
                Require(ReferenceEquals(Keyboard.Focus(closingBox), closingBox), "Managed Window editor could not focus.");
                var closingSource = (PortablePresentationSource)PresentationSource.FromVisual(closingBox);
                var closingProvider = (PortableInputProvider)closingSource.GetInputProvider(typeof(KeyboardDevice));
                closingBox.Select(1, 2); long token = closingProvider.BeginPreedit("candidate", 4); Require(token != 0, "Managed Window rejected preedit.");
                window.Close();
                Require(!closingSource.IsDisposed && closingProvider.UpdatePreedit(token, "still active", 4) && closingBox.Text == "Astill activeDE", "Canceled Closing ended the active session.");
                cancel = false; window.Close(); Drain();
                Require(closingSource.IsDisposed && closingBox.Text == "ABCDE" && closingModel.Value == "ABCDE" && !closingProvider.CommitPreedit(token, "late"), "Accepted close failed to restore/reset owned composition.");
            }
            finally { cancel = false; window.Close(); }
            Require(ReferenceEquals(Keyboard.Focus(box), box), "Original editor could not regain focus after accepted close.");
        });

        Record("real-composition-adorner-draws-and-detaches", () =>
        {
            Reset(); long token = Begin("   ", 3); root.UpdateLayout(); Drain();
            Require(FindVisual(root, "CompositionAdorner") != null, "Existing framework composition adorner did not attach.");
            using var target = new PortableRenderTarget(root, 200, 40);
            target.Render(); using var active = target.Snapshot(); byte[] before = active.GetPixelSpan().ToArray();
            Require(provider.CommitPreedit(token, "   "), "Whitespace composition did not commit."); root.UpdateLayout(); Drain();
            Require(FindVisual(root, "CompositionAdorner") == null, "Composition adorner survived commit.");
            target.Render(); using var completed = target.Snapshot(); byte[] after = completed.GetPixelSpan().ToArray();
            Rect caret = box.GetRectFromCharacterIndex(box.CaretIndex);
            int differences = 0;
            for (int pixel = 0; pixel < 200 * 40; pixel++)
            {
                int x = pixel % 200;
                if (x >= caret.Left - 2 && x <= caret.Right + 2) continue;
                if (!before.AsSpan(pixel * 4, 4).SequenceEqual(after.AsSpan(pixel * 4, 4))) differences++;
            }
            Require(differences >= 2, "Retained pixels did not show composition underline removal outside the unchanged caret.");
        });

        Record("source-disposal-restores-and-rejects-late-callbacks", () =>
        {
            Reset(); long token = Begin("candidate", 4); long reentrantToken = -1;
            model.OnWrite = _ => { model.OnWrite = null; reentrantToken = provider.BeginPreedit("resurrect", 0); };
            source.Dispose(); Drain(); Exact("ABCDE");
            Require(reentrantToken == 0, "Source disposal accepted reentrant preedit from the bound source setter.");
            Require(!provider.UpdatePreedit(token, "late", 0) && !provider.CommitPreedit(token, "late") && provider.BeginPreedit("late", 0) == 0,
                "Disposed source accepted late or fresh preedit.");
            Require(Keyboard.FocusedElement == null && PresentationSource.FromVisual(box) == null, "Disposed source retained editor focus/attachment.");
        });

        Record("no-windows-interop-runtime-load", () =>
        {
            string[] forbidden = ["DirectWriteForwarder", "System.Private.Windows.Core", "System.Private.Windows.GdiPlus", "PresentationNative_cor3", "wpfgfx_cor3"];
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => forbidden.Contains(assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)), "Managed preedit loaded a Windows interop assembly.");
            if (OperatingSystem.IsLinux()) Require(!File.ReadLines("/proc/self/maps").Any(line => forbidden.Any(name => line.Contains(name, StringComparison.OrdinalIgnoreCase)) || line.Contains("user32.dll", StringComparison.OrdinalIgnoreCase)), "Managed preedit mapped a Windows native library.");
        });
        Publish();
    }

    private static Visual? FindVisual(Visual visual, string name)
    {
        if (visual.GetType().Name == name) return visual;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child && FindVisual(child, name) is Visual found) return found;
        return null;
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class PreeditModel
    {
        private string _value = "";
        public List<string> Writes { get; } = new();
        public Action<string>? OnWrite { get; set; }
        public string Value { get => _value; set { _value = value; Writes.Add(value); OnWrite?.Invoke(value); } }
    }

    private sealed class ClosingWindowFactory : IPortableWindowFactory
    {
        public IPortableWindowPeer CreateWindow(PresentationSource source, PortableWindowOptions options) =>
            new ClosingWindowPeer { Source = (PortablePresentationSource)source };
    }

    private sealed class ClosingWindowPeer : ControlsHost, IPortableWindowPeer
    {
        public Size OuterSize => ClientSize;
        public Point DesktopPosition { get; private set; }
        public bool IsVisible { get; private set; }
        public bool IsEnabled { get; private set; } = true;
        public bool IsActive { get; private set; }
        public WindowState WindowState { get; private set; }
        public event Action? CloseRequested { add { } remove { } }
        public event Action<bool>? ActivationChanged;
        public event Action<WindowState>? StateChanged;
        public new event Action? MetricsChanged;
        public new event Action? Exposed;
        public new event Action? Closed;
        public void SetTitle(string title) { }
        public void SetClientSize(Size size) => MetricsChanged?.Invoke();
        public void SetDesktopPosition(Point position) { DesktopPosition = position; MetricsChanged?.Invoke(); }
        public void SetClientSizeConstraints(Size minimum, Size maximum) { }
        public void SetOwner(IPortableWindowPeer? owner) { }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
        public void SetWindowState(WindowState state) { WindowState = state; StateChanged?.Invoke(state); }
        public void Show(bool activate) { IsVisible = true; if (activate) Activate(); Exposed?.Invoke(); }
        public void Hide() { IsVisible = false; IsActive = false; ActivationChanged?.Invoke(false); }
        public bool Activate() { IsActive = true; ActivationChanged?.Invoke(true); return true; }
        public void Close() { IsVisible = false; IsActive = false; ActivationChanged?.Invoke(false); Closed?.Invoke(); }
        public void Dispose() { }
    }
}
