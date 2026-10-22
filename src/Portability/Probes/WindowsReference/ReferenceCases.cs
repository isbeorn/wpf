using FixtureControls;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WindowsReference;

internal static class ReferenceCases
{
    internal static readonly string[] Names = ["markup", "ordering", "nested", "affinity", "cancellation", "shutdown", "shutdown-queued"];

    internal static void Run(string name, List<App.CheckResult> checks)
    {
        switch (name)
        {
            case "markup": Markup(checks); break;
            case "ordering": Ordering(checks); break;
            case "nested": Nested(checks); break;
            case "affinity": Affinity(checks); break;
            case "cancellation": Cancellation(checks); break;
            case "shutdown": Shutdown(checks, queued: false); break;
            case "shutdown-queued": Shutdown(checks, queued: true); break;
            default: throw new ArgumentException("Unknown case: " + name);
        }
    }

    private static void Markup(List<App.CheckResult> checks)
    {
        RequireBaml(typeof(App).Assembly, "app.baml", "fixturewindow.baml");
        RequireBaml(typeof(FixtureView).Assembly, "fixtureview.baml", "fixtureresources.baml");
        var bitmap = new WriteableBitmap(3, 2, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 0, 0, 0, 0,
                         10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255, 0, 0, 0, 0];
        bitmap.WritePixels(new Int32Rect(0, 0, 3, 2), pixels, 16, 0);
        var model = new FixtureModel { Image = bitmap };
        var window = new FixtureWindow { DataContext = model };
        try
        {
            window.Measure(new Size(480, 320));
            window.Arrange(new Rect(0, 0, 480, 320));
            window.UpdateLayout();
            Drain();
            FixtureView view = window.Fixture;
            var editor = (TextBox)view.FindName("Editor");
            var metric = (MetricControl)view.FindName("Metric");
            var button = (Button)view.FindName("Increment");
            var preview = (Image)view.FindName("Preview");
            metric.ApplyTemplate();
            var label = (TextBlock)metric.Template.FindName("PART_Value", metric);
            Require(metric.Value == 7 && label.Text == "7" && editor.Text == "7", $"Compiled view/template initial binding failed: metric={metric.Value}, label={label.Text}, editor={editor.Text}.");
            Pass(checks, "markup.compiled-baml", "App, Window and second-assembly UserControl constructed; template binding resolved.");

            model.Value = 12;
            Drain();
            Require(editor.Text == "12" && label.Text == "12", "Source-to-target binding failed.");
            editor.Text = "-1";
            Drain();
            Require(Validation.GetHasError(editor) && model.Value == 12, "Invalid edit reached source or validation was lost.");
            editor.Text = "14";
            Drain();
            Require(!Validation.GetHasError(editor) && model.Value == 14, "Valid edit did not clear validation and update source.");
            Pass(checks, "markup.binding-validation", "Source and target changes propagate; invalid edit rejected and valid edit clears error.");

            model.Enabled = false;
            Drain();
            Require(!button.IsEnabled, "CanExecute false did not disable bound Button.");
            model.Enabled = true;
            Drain();
            Require(button.IsEnabled, "CanExecute true did not enable bound Button.");
            var provider = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
            provider.Invoke();
            Drain();
            Require(model.Value == 15, "Button invocation did not execute bound command exactly once.");
            Pass(checks, "markup.command", "Button disable/enable and automation invocation execute the compiled command binding.");

            var original = (SolidColorBrush)label.Foreground;
            Require(original.Color == Color.FromRgb(0x11, 0x77, 0xbb), "Component dictionary was not resolved.");
            Application.Current.Resources["FixtureAccent"] = Brushes.Orange;
            Drain();
            Require(ReferenceEquals(label.Foreground, Brushes.Orange), "DynamicResource replacement failed.");
            Application.Current.Resources.Remove("FixtureAccent");
            Drain();
            Require(ReferenceEquals(label.Foreground, original), "Merged dictionary fallback after removal failed.");
            Pass(checks, "markup.pack-resource", "Second-assembly pack URI loaded; dynamic replacement and removal restore merged resource.");

            Require(ReferenceEquals(preview.Source, bitmap), "Image binding did not retain source bitmap.");
            byte[] copied = new byte[24];
            bitmap.CopyPixels(copied, 12, 0);
            Require(copied.Take(12).SequenceEqual(pixels.Take(12)) && copied.Skip(12).SequenceEqual(pixels.Skip(16).Take(12)), "Padded stride or channel data changed.");
            bitmap.Lock();
            try
            {
                System.Runtime.InteropServices.Marshal.WriteByte(bitmap.BackBuffer, 0, 33);
                bitmap.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
            }
            finally { bitmap.Unlock(); }
            bitmap.CopyPixels(copied, 12, 0);
            Require(copied[0] == 33 && copied[1] == 2, "Locked buffer update failed.");
            var imageVisual = new DrawingVisual();
            using (DrawingContext drawing = imageVisual.RenderOpen())
            {
                drawing.DrawImage(bitmap, new Rect(0, 0, 3, 2));
            }
            var imageRender = new RenderTargetBitmap(3, 2, 96, 96, PixelFormats.Pbgra32);
            imageRender.Render(imageVisual);
            byte[] presented = new byte[24];
            imageRender.CopyPixels(presented, 12, 0);
            Require(presented.SequenceEqual(copied), "Image rendering changed opaque pixel channels or row order.");
            window.Content = null;
            view.Measure(new Size(480, 320));
            view.Arrange(new Rect(0, 0, 480, 320));
            view.UpdateLayout();
            var rendered = new RenderTargetBitmap(480, 320, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(view);
            byte[] renderedPixels = new byte[480 * 320 * 4];
            rendered.CopyPixels(renderedPixels, 480 * 4, 0);
            Require(renderedPixels.Any(value => value != 0), "Offscreen view render was empty.");
            Pass(checks, "markup.image", "Bound WriteableBitmap preserves padded rows and locked updates; rendered opaque pixels match readback exactly and view renders nonempty pixels.");
        }
        finally { window.Close(); }
    }

    private static void Ordering(List<App.CheckResult> checks)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        int thread = Environment.CurrentManagedThreadId;
        int invokedThread = dispatcher.Invoke(() => Environment.CurrentManagedThreadId, DispatcherPriority.Send);
        Require(invokedThread == thread, "Same-thread Send changed threads.");
        var order = new List<string>();
        dispatcher.BeginInvoke(() => order.Add("background"), DispatcherPriority.Background);
        dispatcher.BeginInvoke(() => order.Add("normal-1"), DispatcherPriority.Normal);
        dispatcher.BeginInvoke(() => order.Add("send"), DispatcherPriority.Send);
        dispatcher.BeginInvoke(() => order.Add("normal-2"), DispatcherPriority.Normal);
        Drain();
        Require(order.SequenceEqual(new[] { "send", "normal-1", "normal-2", "background" }), "Priority/FIFO order: " + string.Join(",", order));
        Pass(checks, "dispatcher.ordering", "Same-thread Send remains on caller; queued Send > Normal > Background with equal-priority FIFO.");
    }

    private static void Nested(List<App.CheckResult> checks)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var events = new List<string>();
        var outer = new DispatcherFrame();
        dispatcher.BeginInvoke(() =>
        {
            events.Add("outer-enter");
            var inner = new DispatcherFrame();
            dispatcher.BeginInvoke(() => { events.Add("inner-callback"); inner.Continue = false; }, DispatcherPriority.Background);
            Dispatcher.PushFrame(inner);
            events.Add("outer-exit");
            outer.Continue = false;
        }, DispatcherPriority.Normal);
        Dispatcher.PushFrame(outer);
        Require(events.SequenceEqual(new[] { "outer-enter", "inner-callback", "outer-exit" }), "Nested frame did not reenter in expected order.");
        bool rejected = false;
        using (dispatcher.DisableProcessing())
        {
            try { Dispatcher.PushFrame(new DispatcherFrame()); }
            catch (InvalidOperationException) { rejected = true; }
        }
        Require(rejected, "DisableProcessing permitted a nested frame.");
        bool resumed = false;
        dispatcher.BeginInvoke(() => resumed = true);
        Drain();
        Require(resumed, "Disposing DisableProcessing did not resume callbacks.");
        Pass(checks, "dispatcher.nested-disable", "Nested frame processes callback; DisableProcessing rejects pumping and disposal resumes work.");
    }

    private static void Affinity(List<App.CheckResult> checks)
    {
        var target = new DependencyObject();
        target.SetValue(FrameworkElement.TagProperty, "owner-thread");
        bool access = true;
        bool rejected = false;
        var worker = new Thread(() =>
        {
            access = target.CheckAccess();
            try { target.SetValue(FrameworkElement.TagProperty, "wrong-thread"); }
            catch (InvalidOperationException) { rejected = true; }
        });
        worker.Start();
        Require(worker.Join(TimeSpan.FromSeconds(5)), "Affinity worker hung.");
        Require(!access && rejected && target.CheckAccess(), "Thread affinity contract failed.");
        Require(Equals(target.GetValue(FrameworkElement.TagProperty), "owner-thread"), "Rejected worker mutation changed the owner value.");
        target.ClearValue(FrameworkElement.TagProperty);
        Require(target.GetValue(FrameworkElement.TagProperty) is null, "Owning thread could not clear its value.");
        Pass(checks, "dispatcher.affinity", "Owning thread accepted; worker CheckAccess false and dependency-property mutation rejected.");
    }

    private static void Cancellation(List<App.CheckResult> checks)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        bool ran = false;
        using var pending = new CancellationTokenSource();
        DispatcherOperation operation = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Background, pending.Token);
        pending.Cancel();
        Drain();
        Require(!ran && operation.Status == DispatcherOperationStatus.Aborted && operation.Task.IsCanceled, "Pending cancellation did not abort work.");
        using var early = new CancellationTokenSource();
        early.Cancel();
        DispatcherOperation canceled = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Normal, early.Token);
        Drain();
        Require(!ran && canceled.Task.IsCanceled, "Pre-canceled operation executed.");
        using var late = new CancellationTokenSource();
        DispatcherOperation completed = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Normal, late.Token);
        Drain();
        late.Cancel();
        Require(ran && completed.Status == DispatcherOperationStatus.Completed && completed.Task.IsCompletedSuccessfully, "Late cancellation rewrote completed result.");
        Pass(checks, "dispatcher.cancellation", "Cancellation before enqueue and while pending aborts; cancellation after completion preserves success.");
    }

    private static void Shutdown(List<App.CheckResult> checks, bool queued)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                var events = new List<string>();
                dispatcher.ShutdownStarted += (_, _) => events.Add("started");
                dispatcher.ShutdownFinished += (_, _) => events.Add("finished");
                bool ran = false;
                DispatcherOperation? pending = null;
                dispatcher.BeginInvoke(() =>
                {
                    pending = dispatcher.InvokeAsync(() => ran = true, DispatcherPriority.Background);
                    if (queued) dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    else dispatcher.InvokeShutdown();
                }, DispatcherPriority.Send);
                Dispatcher.Run();
                Require(dispatcher.HasShutdownStarted && dispatcher.HasShutdownFinished, "Shutdown flags not completed after message loop exit.");
                Require(events.SequenceEqual(new[] { "started", "finished" }), "Shutdown event order failed.");
                Require(!ran && pending?.Status == DispatcherOperationStatus.Aborted, "Shutdown did not abort pending work.");
                DispatcherOperation after = dispatcher.InvokeAsync(() => ran = true);
                Require(!ran && after.Status == DispatcherOperationStatus.Aborted, "Post-shutdown work was accepted.");
            }
            catch (Exception exception) { failure = exception; }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Require(worker.Join(TimeSpan.FromSeconds(5)), "Shutdown worker did not exit its message loop.");
        if (failure is not null) throw new InvalidOperationException("Shutdown worker failed.", failure);
        Pass(checks, queued ? "dispatcher.shutdown-queued" : "dispatcher.shutdown", "Isolated dispatcher reports ordered shutdown events, aborts pending work and rejects later work.");
    }

    private static void RequireBaml(System.Reflection.Assembly assembly, params string[] expected)
    {
        using Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources")
            ?? throw new InvalidOperationException("Compiled resource stream missing from " + assembly.GetName().Name);
        using var reader = new ResourceReader(resource);
        var names = new HashSet<string>();
        foreach (System.Collections.DictionaryEntry entry in reader) names.Add((string)entry.Key);
        Require(expected.All(names.Contains), "Expected compiled BAML entries missing from " + assembly.GetName().Name);
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.SystemIdle);
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void Pass(List<App.CheckResult> checks, string name, string detail) => checks.Add(new(name, "pass", detail));
}
