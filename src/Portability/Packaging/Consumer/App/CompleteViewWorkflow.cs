using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FixtureControls;

namespace PackageConsumer;

internal interface ICompleteViewDesktop
{
    bool IsMapped(Window window);
    void Focus(Window window);
    void Click(Window window, FrameworkElement element);
    void ReplaceText(Window window, TextBox editor, string text);
    uint Pixel(Window window, FrameworkElement element, double x, double y);
    (int Width, int Height) ClientSize(Window window);
    void SetDpi(int dpi);
}

internal static class CompleteViewWorkflow
{
    internal static IEnumerable<string> RequiredChecks(bool interactive) => new[]
    {
        "complete-view-origin-and-identity", "complete-view-native-construction-frame-and-bindings",
        "complete-view-native-valid-invalid-recovery", "complete-view-native-command-disable-enable",
        "complete-view-native-command-modal-accept", "complete-view-native-command-modal-cancel",
        "complete-view-bound-rgb48-image-mutation-and-replacement", "complete-view-dynamic-resource-replace-remove-repaint",
        "complete-view-opacity-animation-reverse-and-remove", "complete-view-source-detached"
    }.Concat(interactive ? Array.Empty<string>() : new[]
    {
        "complete-view-fresh-lifetime-resize-scale-and-close-active-96",
        "complete-view-fresh-lifetime-resize-scale-and-close-active-144", "complete-view-unrelated-native-frame-after-lifetimes"
    });

    internal static void Run(ICompleteViewDesktop desktop, bool interactive, Action<string, Action> check,
        Action<Func<bool>, Func<string>?> wait)
    {
        check("complete-view-origin-and-identity", VerifyOrigin);
        var application = Application.Current ?? new Application();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/App;component/WorkflowResources.xaml", UriKind.Relative) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FixtureControls;component/FixtureResources.xaml", UriKind.Relative) });
        var family = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "DejaVuSans.ttf")), "DejaVu Sans");
        var model = new WorkflowModel();
        var view = new FixtureView { DataContext = model, FontFamily = family };
        var window = new Window { Title = "Complete unchanged WPF view", Left = 40, Top = 40, Width = 480, Height = 320,
            Content = view, FontFamily = family, Background = Brushes.White };
        var editor = (TextBox)view.FindName("Editor");
        var metric = (MetricControl)view.FindName("Metric");
        var increment = (Button)view.FindName("Increment");
        var preview = (Image)view.FindName("Preview");
        model.OpenDialog = () => ShowDialog(window, model, increment, null);
        int loaded = 0, rendered = 0, keyReleases = 0;
        view.Loaded += (_, _) => loaded++;
        window.ContentRendered += (_, _) => rendered++;
        editor.KeyUp += (_, _) => keyReleases++;
        try
        {
            check("complete-view-native-construction-frame-and-bindings", () =>
            {
                window.Show();
                wait(() => desktop.IsMapped(window) && rendered > 0 && editor.ActualHeight == 40 && desktop.Pixel(window, editor, 210, 30) == 0xffffff,
                    () => $"mapped={desktop.IsMapped(window)}, rendered={rendered}, loaded={loaded}, view={view.ActualWidth}x{view.ActualHeight}, editor={editor.ActualWidth}x{editor.ActualHeight}, template={view.Template != null}");
                metric.ApplyTemplate();
                var label = (TextBlock)metric.Template.FindName("PART_Value", metric);
                Require(loaded == 1 && metric.Value == 7 && editor.Text == "7" && label.Text == "7", "Unchanged view bindings or Loaded count differed.");
                Require(PresentationSource.FromVisual(view) != null && desktop.Pixel(window, editor, 210, 30) == 0xffffff,
                    "The real source or native TextBox background frame was absent.");
                foreach (string name in new[] { "Metric", "Editor", "Increment", "Preview" })
                    Require(ReferenceEquals(view.FindName(name), typeof(FixtureView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)),
                        "Generated connector differed: " + name);
            });
            check("complete-view-native-valid-invalid-recovery", () =>
            {
                desktop.Focus(window); desktop.Click(window, editor);
                wait(() => editor.IsKeyboardFocused, null);
                Edit("12", () => model.Value == 12 && metric.Value == 12 && !Validation.GetHasError(editor));
                Edit("-1", () => Validation.GetHasError(editor) && model.Value == 12 && metric.Value == 12);
                Edit("14", () => model.Value == 14 && metric.Value == 14 && !Validation.GetHasError(editor));
                Require(keyReleases >= 6, "Native key releases did not reach the unchanged TextBox.");
                var label = (TextBlock)metric.Template.FindName("PART_Value", metric);
                Require(label.Text == "14", "Recovery did not update the original compiled template binding.");
                model.Value = 6;
                wait(() => editor.Text == "6" && metric.Value == 6 && label.Text == "6", null);
                void Edit(string value, Func<bool> observed)
                {
                    desktop.ReplaceText(window, editor, value);
                    wait(() => editor.Text == value && observed(), () => $"editor={editor.Text}, model={model.Value}, metric={metric.Value}, validation={Validation.GetHasError(editor)}");
                }
            });
            check("complete-view-native-command-disable-enable", () =>
            {
                model.Enabled = false;
                wait(() => !increment.IsEnabled, null);
                int before = model.Executions;
                desktop.Click(window, increment); Drain();
                Require(model.Executions == before, "A disabled native Button click executed its command.");
                model.Enabled = true;
                wait(() => increment.IsEnabled && increment.IsMouseOver, () => $"enabled={increment.IsEnabled}, mouseOver={increment.IsMouseOver}");
            });
            foreach (bool accept in new[] { true, false })
            check("complete-view-native-command-modal-" + (accept ? "accept" : "cancel"), () =>
            {
                ExecuteCommand(window, model, increment, accept);
                desktop.Focus(window); desktop.Click(window, editor);
                wait(() => editor.IsKeyboardFocused, null);
            });
            check("complete-view-bound-rgb48-image-mutation-and-replacement", () =>
            {
                WriteSamples(model.Image, 65535, 257, 17);
                wait(() => ReferenceEquals(preview.Source, model.Image) && desktop.Pixel(window, preview, 8, 8) == 0xff0100, null);
                model.Image.Lock();
                try
                {
                    for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        int offset = y * model.Image.BackBufferStride + x * 6;
                        System.Runtime.InteropServices.Marshal.WriteInt16(model.Image.BackBuffer, offset, 17);
                        System.Runtime.InteropServices.Marshal.WriteInt16(model.Image.BackBuffer, offset + 2, 257);
                        System.Runtime.InteropServices.Marshal.WriteInt16(model.Image.BackBuffer, offset + 4, -1);
                    }
                    model.Image.AddDirtyRect(new Int32Rect(0, 0, 3, 2));
                }
                finally { model.Image.Unlock(); }
                VerifySamples(model.Image, 17, 257, 65535);
                wait(() => desktop.Pixel(window, preview, 8, 8) == 0x0001ff, null);
                WriteableBitmap old = model.Image;
                model.Image = new WriteableBitmap(3, 2, 96, 96, PixelFormats.Rgb48, null);
                WriteSamples(model.Image, 257, 65535, 17);
                wait(() => ReferenceEquals(preview.Source, model.Image) && desktop.Pixel(window, preview, 8, 8) == 0x01ff00, null);
                WriteSamples(old, 65535, 17, 65535); Drain();
                Require(desktop.Pixel(window, preview, 8, 8) == 0x01ff00, "Mutating the replaced bitmap changed the bound Image.");
                WriteSamples(model.Image, 65535, 257, 17);
                wait(() => desktop.Pixel(window, preview, 8, 8) == 0xff0100, null);
            });
            check("complete-view-dynamic-resource-replace-remove-repaint", () =>
            {
                var label = (TextBlock)metric.Template.FindName("PART_Value", metric);
                Brush original = label.Foreground;
                uint[] originalPixels = SampleLabel(label);
                Require(originalPixels.Any(pixel => pixel != 0xffffff), "The resource repaint sample missed all original glyph ink.");
                application.Resources["FixtureAccent"] = Brushes.Orange;
                wait(() => ReferenceEquals(label.Foreground, Brushes.Orange) && !SampleLabel(label).SequenceEqual(originalPixels),
                    () => $"brush={label.Foreground}, reference={ReferenceEquals(label.Foreground, Brushes.Orange)}, pixelsChanged={!SampleLabel(label).SequenceEqual(originalPixels)}, label={label.ActualWidth}x{label.ActualHeight}");
                application.Resources.Remove("FixtureAccent");
                wait(() => ReferenceEquals(label.Foreground, original) && SampleLabel(label).SequenceEqual(originalPixels), null);
            });
            check("complete-view-opacity-animation-reverse-and-remove", () =>
            {
                uint original = desktop.Pixel(window, preview, 8, 8);
                try
                {
                    preview.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(350)));
                    wait(() => preview.Opacity < 0.9 && preview.Opacity > 0.3 && desktop.Pixel(window, preview, 8, 8) != original,
                        () => $"opacity={preview.Opacity}, animated={preview.HasAnimatedProperties}, pixel={desktop.Pixel(window, preview, 8, 8):X6}, original={original:X6}");
                    wait(() => preview.Opacity == 0.25, null);
                    preview.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(350)));
                    wait(() => preview.Opacity == 1 && desktop.Pixel(window, preview, 8, 8) == original, null);
                }
                finally { preview.BeginAnimation(UIElement.OpacityProperty, null); Drain(); }
                Require(!preview.HasAnimatedProperties && preview.Opacity == 1 && desktop.Pixel(window, preview, 8, 8) == original,
                    "Removing the opacity clock did not restore the base value and native frame.");
            });
            model.OpenDialog = () => ShowDialog(window, model, increment, null);
            if (interactive)
            {
                window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                Dispatcher.Run();
            }
        }
        finally { window.Close(); preview.BeginAnimation(UIElement.OpacityProperty, null); }
        check("complete-view-source-detached", () => Require(PresentationSource.FromVisual(view) == null, "Closed view retained its presentation source."));
        if (!interactive)
        {
            foreach (int startDpi in new[] { 96, 144 })
            check("complete-view-fresh-lifetime-resize-scale-and-close-active-" + startDpi, () =>
            {
                desktop.SetDpi(startDpi); Drain();
                var nextModel = new WorkflowModel();
                WriteSamples(nextModel.Image, 65535, 257, 17);
                var nextView = new FixtureView { DataContext = nextModel, FontFamily = family };
                var nextEditor = (TextBox)nextView.FindName("Editor");
                var nextPreview = (Image)nextView.FindName("Preview");
                var nextButton = (Button)nextView.FindName("Increment");
                var next = new Window { Title = "Complete WPF fresh lifetime " + startDpi, Left = 40, Top = 40, Width = 480, Height = 320,
                    Content = nextView, FontFamily = family, Background = Brushes.White };
                int nextLoaded = 0, nextClosed = 0, nextMouseDown = 0, nextDoubleClick = 0;
                DpiScale? initializedDpi = null;
                nextView.Loaded += (_, _) => nextLoaded++;
                next.SourceInitialized += (_, _) => initializedDpi = VisualTreeHelper.GetDpi(next);
                next.Closed += (_, _) => nextClosed++;
                nextEditor.PreviewMouseDown += (_, input) => { nextMouseDown++; if (input.ClickCount == 2) nextDoubleClick++; };
                try
                {
                    next.Show(); wait(() => desktop.IsMapped(next) && nextEditor.ActualHeight == 40 && PresentationSource.FromVisual(nextPreview) != null &&
                        VisualTreeHelper.GetDpi(next).DpiScaleX == startDpi / 96d, null); desktop.Focus(next);
                    if (OperatingSystem.IsLinux()) Require(initializedDpi.HasValue && initializedDpi.Value.DpiScaleX == startDpi / 96d && initializedDpi.Value.DpiScaleY == startDpi / 96d,
                        "The native X11 initial DPI was not established at SourceInitialized before the first show.");
                    foreach (int dpi in new[] { startDpi, startDpi == 96 ? 144 : 96, startDpi })
                    {
                        desktop.SetDpi(dpi);
                        wait(() => VisualTreeHelper.GetDpi(next).DpiScaleX == dpi / 96d && VisualTreeHelper.GetDpi(next).DpiScaleY == dpi / 96d &&
                            desktop.Pixel(next, nextPreview, 8, 8) == 0xff0100, () => $"dpi={VisualTreeHelper.GetDpi(next).DpiScaleX}, expected={dpi / 96d}");
                        foreach (Size size in new[] { new Size(640, 400), new Size(480, 320) })
                        {
                            next.Width = size.Width; next.Height = size.Height;
                            wait(() => next.ActualWidth == size.Width && next.ActualHeight == size.Height && NativeClientMatches(next, nextView) &&
                                desktop.Pixel(next, nextPreview, 8, 8) == 0xff0100, null);
                            int mouseDownBefore = nextMouseDown, doubleClickBefore = nextDoubleClick;
                            string textBefore = nextEditor.Text;
                            desktop.Click(next, nextEditor); desktop.Click(next, nextEditor);
                            wait(() => nextEditor.IsKeyboardFocused && nextMouseDown == mouseDownBefore + 2 && nextDoubleClick == doubleClickBefore + 1 &&
                                nextEditor.SelectedText == textBefore && nextEditor.SelectionStart == 0 && nextEditor.SelectionLength == textBefore.Length,
                                () => $"down={nextMouseDown - mouseDownBefore}, doubles={nextDoubleClick - doubleClickBefore}, before={textBefore}, selected={nextEditor.SelectedText}, start={nextEditor.SelectionStart}, length={nextEditor.SelectionLength}");
                            desktop.ReplaceText(next, nextEditor, "21"); wait(() => nextModel.Value == 21 && !Validation.GetHasError(nextEditor), null);
                            nextModel.Value = 13; wait(() => nextEditor.Text == "13", null);
                            ExecuteCommand(next, nextModel, nextButton, true);
                            Require(desktop.Pixel(next, nextPreview, 8, 8) == 0xff0100, "The native modal command or geometry transition changed the bound image.");
                        }
                    }
                    Require(nextLoaded == 1, "Resize/scaling reloaded the unchanged view.");
                    nextPreview.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(5)) { RepeatBehavior = RepeatBehavior.Forever });
                    wait(() => nextPreview.HasAnimatedProperties && nextPreview.Opacity < 1, null);
                    desktop.SetDpi(96);
                    wait(() => VisualTreeHelper.GetDpi(next).DpiScaleX == 1, null);
                    next.Close();
                    Require(PresentationSource.FromVisual(nextView) == null && nextClosed == 1, "Closing an active animation retained the source or changed Closed count.");
                    wait(() => !desktop.IsMapped(next), null);
                    Require(nextClosed == 1, "Native disappearance changed the Closed count.");
                }
                finally
                {
                    try { if (next.IsVisible) desktop.SetDpi(96); }
                    finally { next.Close(); nextPreview.BeginAnimation(UIElement.OpacityProperty, null); Drain(); }
                }
                Require(nextClosed == 1, "Repeated close produced another Closed event.");
            });
            check("complete-view-unrelated-native-frame-after-lifetimes", () =>
            {
                var blue = new Border { Background = Brushes.Blue };
                var unrelated = new Window { Title = "Complete WPF final unrelated frame", Width = 180, Height = 100, Content = blue };
                try { unrelated.Show(); wait(() => desktop.IsMapped(unrelated) && blue.ActualWidth > 30 && blue.ActualHeight > 30 && desktop.Pixel(unrelated, blue, 30, 30) == 0x0000ff, null); }
                finally { unrelated.Close(); }
            });
        }

        uint[] SampleLabel(TextBlock label) => Enumerable.Range(0, 64).Select(index =>
            desktop.Pixel(window, label, Math.Min(label.ActualWidth, 16) * (index % 8 + 0.5) / 8, label.ActualHeight * (index / 8 + 0.5) / 8)).ToArray();

        bool NativeClientMatches(Window owner, FrameworkElement root)
        {
            var actual = desktop.ClientSize(owner);
            DpiScale dpi = VisualTreeHelper.GetDpi(owner);
            return PixelMatches(actual.Width, root.ActualWidth * dpi.DpiScaleX) && PixelMatches(actual.Height, root.ActualHeight * dpi.DpiScaleY);
            static bool PixelMatches(int actual, double requested) => actual == Math.Floor(Math.BitIncrement(requested)) || actual == Math.Ceiling(Math.BitDecrement(requested));
        }

        void ExecuteCommand(Window owner, WorkflowModel state, Button button, bool accept)
        {
            int before = state.Executions, value = state.Value;
            state.OpenDialog = () => ShowDialog(owner, state, button, accept);
            desktop.Click(owner, button);
            wait(() => state.Executions == before + 1 && state.LastResult == accept && !state.InDialog,
                () => $"executions={state.Executions}, expected={before + 1}, result={state.LastResult}, expectedResult={accept}, inDialog={state.InDialog}, enabled={button.IsEnabled}, mouseOver={button.IsMouseOver}, pressed={button.IsPressed}");
            Require(state.Value == value + (accept ? 1 : 0) && owner.OwnedWindows.Count == 0 && owner.IsEnabled,
                "Accepted/canceled synchronous command result or owner restoration differed.");
        }

        bool? ShowDialog(Window owner, WorkflowModel state, Button ownerButton, bool? automatedAccept)
        {
            var acceptButton = new Button { Content = "Accept" };
            var cancelButton = new Button { Content = "Cancel" };
            var panel = new StackPanel(); panel.Children.Add(acceptButton); panel.Children.Add(cancelButton);
            var dialog = new Window { Title = "Complete WPF owned dialog", Owner = owner, Left = 730, Top = 80, Width = 260, Height = 180, Content = panel, FontFamily = family };
            acceptButton.Click += (_, _) => dialog.DialogResult = true;
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            int executions = state.Executions;
            var driver = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
            driver.Tick += (_, _) =>
            {
                if (!desktop.IsMapped(dialog) || acceptButton.ActualWidth == 0) return;
                driver.Stop();
                Require(state.InDialog && ReferenceEquals(dialog.Owner, owner) && owner.OwnedWindows.Count == 1,
                    "The command did not remain inside a genuine owned modal frame.");
                desktop.Click(owner, ownerButton); Drain();
                Require(state.Executions == executions, "The modal owner accepted native command input.");
                desktop.Focus(dialog); desktop.Click(dialog, automatedAccept == true ? acceptButton : cancelButton);
            };
            try
            {
                if (automatedAccept.HasValue) driver.Start();
                return dialog.ShowDialog();
            }
            finally { driver.Stop(); dialog.Close(); }
        }
    }

    private static void WriteSamples(WriteableBitmap bitmap, ushort red, ushort green, ushort blue)
    {
        ushort[] padded = new ushort[24];
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 3; x++)
        { padded[y * 12 + x * 3] = red; padded[y * 12 + x * 3 + 1] = green; padded[y * 12 + x * 3 + 2] = blue; }
        bitmap.WritePixels(new Int32Rect(0, 0, 3, 2), padded, 24, 0);
        Array.Clear(padded);
        VerifySamples(bitmap, red, green, blue);
    }

    private static void VerifySamples(WriteableBitmap bitmap, ushort red, ushort green, ushort blue)
    {
        ushort[] copied = new ushort[18]; bitmap.CopyPixels(copied, 18, 0);
        Require(Enumerable.Range(0, 6).All(pixel => copied[pixel * 3] == red && copied[pixel * 3 + 1] == green && copied[pixel * 3 + 2] == blue),
            "RGB48 source data lost precision, row order or independent buffer ownership.");
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void VerifyOrigin()
    {
        using var origin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CompleteViewOrigin.json")));
        var expected = origin.RootElement;
        Assembly assembly = typeof(FixtureView).Assembly;
        Require(assembly.FullName == expected.GetProperty("assemblyIdentity").GetString(), "FixtureControls identity changed between compiler origins.");
        Require(assembly.Location == Path.Combine(AppContext.BaseDirectory, "FixtureControls.dll") && AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default,
            "FixtureControls did not use its ordinary app-local Default load context.");
        var generated = typeof(FixtureView).GetMethod("InitializeComponent")!.GetCustomAttribute<GeneratedCodeAttribute>()!;
        Require(generated.Tool == "PresentationBuildTasks" && generated.Version == expected.GetProperty("compilerVersion").GetString(), "The generated connector has the wrong compiler origin.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) == expected.GetProperty("fixtureSha256").GetString(), "The selected compiler-origin binary changed.");
        using var stream = assembly.GetManifestResourceStream("FixtureControls.g.resources")!;
        using var reader = new ResourceReader(stream);
        var names = reader.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).ToArray();
        Require(names.Contains("fixtureview.baml") && names.Contains("fixtureresources.baml"), "The original compiled view or dictionary is missing.");
    }

    private static void Require(bool condition, string detail)
    { if (!condition) throw new InvalidOperationException(detail); }

    private sealed class WorkflowModel : INotifyPropertyChanged, ICommand
    {
        private int _value = 7;
        private bool _enabled = true;
        private WriteableBitmap _image = new(3, 2, 96, 96, PixelFormats.Rgb48, null);
        public int Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public WriteableBitmap Image { get => _image; set { _image = value; PropertyChanged?.Invoke(this, new(nameof(Image))); } }
        public bool Enabled { get => _enabled; set { _enabled = value; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
        public Func<bool?>? OpenDialog { get; set; }
        public int Executions { get; private set; }
        public bool InDialog { get; private set; }
        public bool? LastResult { get; private set; }
        public ICommand IncrementCommand => this;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => Enabled;
        public void Execute(object? parameter)
        {
            Executions++; InDialog = true;
            try { LastResult = OpenDialog?.Invoke(); if (LastResult == true) Value++; }
            finally { InDialog = false; }
        }
    }
}
