using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static partial class DockingProgram
{
    private static int ObserveShowReadiness()
    {
        var observations = new List<object>();
        var checks = new List<string>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        object Capture(string stage, ReadyWindow window) => new { stage, window.IsVisible, window.IsLoaded,
            window.IsActive, window.IsMeasureValid, window.IsArrangeValid, window.ActualWidth, window.ActualHeight,
            window.ApplyCount, partPresent = window.Template.FindName("PART_Readiness", window) is Border,
            visualChildren = VisualTreeHelper.GetChildrenCount(window), source = PresentationSource.FromVisual(window) is not null };
        void Ready(ReadyWindow window, double width, double height, string stage)
        {
            observations.Add(Capture(stage, window));
            Require(window.IsVisible && window.Template.FindName("PART_Readiness", window) is Border && window.ApplyCount == 1
                && window.IsMeasureValid && window.IsArrangeValid && Math.Abs(window.ActualWidth - width) < .001
                && Math.Abs(window.ActualHeight - height) < .001, "Immediate Window.Show layout differs: " + stage);
            checks.Add(stage);
        }
        try
        {
            var window = new ReadyWindow();
            bool sourceReady = false;
            window.SourceInitialized += (_, _) => { observations.Add(Capture("source-initialized", window));
                sourceReady = window.IsMeasureValid && window.IsArrangeValid && window.ActualWidth == 260 && window.ActualHeight == 180
                    && window.Template.FindName("PART_Readiness", window) is Border; };
            window.Show(); Ready(window, 260, 180, "immediate-first-show");
            Require(sourceReady, "SourceInitialized ran before initial template and layout readiness."); checks.Add("source-initialized-ready");
            Require(!window.IsActive, "ShowActivated=false activated the window."); checks.Add("nonactivating-show");
            window.Hide(); Require(!window.IsVisible, "Hide retained visible state.");
            window.Show(); Ready(window, 260, 180, "immediate-reshow");
            window.Hide(); window.Width = 310; window.Height = 210;
            window.Show(); Ready(window, 310, 210, "hidden-resize-immediate-reshow");
            window.Close(); Require(PresentationSource.FromVisual(window) is null, "Closed window retained a presentation source.");
            var closeDuringSource = new ReadyWindow();
            closeDuringSource.SourceInitialized += (_, _) => closeDuringSource.Close();
            closeDuringSource.Show();
            Require(!closeDuringSource.IsVisible && PresentationSource.FromVisual(closeDuringSource) is null, "SourceInitialized close retained a live source.");
            checks.Add("close-during-source-initialization");
            var closeDuringActivation = new ReadyWindow { ShowActivated = true };
            int activated = 0; closeDuringActivation.Activated += (_, _) => { activated++; closeDuringActivation.Close(); };
            closeDuringActivation.Show(); Settle();
            Require(activated == 1 && !closeDuringActivation.IsVisible && PresentationSource.FromVisual(closeDuringActivation) is null, "Activation close failed or retained a source.");
            checks.Add("close-during-activation");
            var failed = new ReadyWindow { Content = new FailedMeasure() };
            bool measureFailed = false;
            try { failed.Show(); }
            catch (InvalidDataException exception) when (exception.Message == "Controlled Window.Show measure failure.") { measureFailed = true; }
            finally { failed.Close(); }
            Require(measureFailed && PresentationSource.FromVisual(failed) is null, "Show did not surface the layout failure or cleanup retained a source.");
            checks.Add("failed-initial-layout-cleanup");
            foreach (var mode in new[] { SizeToContent.Width, SizeToContent.Height, SizeToContent.WidthAndHeight })
            {
                var sized = new ReadyWindow { SizeToContent = mode, Content = new Border { Width = 90, Height = 45 } };
                sized.Show(); Ready(sized, mode == SizeToContent.Height ? 260 : sized.DesiredSize.Width, mode == SizeToContent.Width ? 180 : sized.DesiredSize.Height, "initial-size-to-content-" + mode);
                sized.Close();
            }
            foreach (bool minimum in new[] { true, false })
            {
                var constrained = new ReadyWindow();
                if (minimum) { constrained.Width = 10; constrained.Height = 10; constrained.MinWidth = 140; constrained.MinHeight = 100; }
                else { constrained.MaxWidth = 200; constrained.MaxHeight = 150; }
                constrained.Show(); Ready(constrained, minimum ? 140 : 200, minimum ? 100 : 150, minimum ? "initial-minimum" : "initial-maximum");
                constrained.Close();
            }
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", checks, observations }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "fail", checks, error = exception.ToString(), observations }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { app.Shutdown(); }
    }
    private sealed class ReadyWindow : Window
    {
        static ReadyWindow() => VisibilityProperty.OverrideMetadata(typeof(ReadyWindow), new FrameworkPropertyMetadata(Visibility.Hidden));
        internal int ApplyCount { get; private set; }
        internal ReadyWindow()
        {
            Width = 260; Height = 180; Left = 50; Top = 50; WindowStyle = WindowStyle.None; ShowActivated = false;
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            var border = new FrameworkElementFactory(typeof(Border), "PART_Readiness"); border.AppendChild(content);
            Template = new ControlTemplate(typeof(Window)) { VisualTree = border };
        }
        public override void OnApplyTemplate() { ApplyCount++; base.OnApplyTemplate(); }
    }
    private sealed class FailedMeasure : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) => throw new InvalidDataException("Controlled Window.Show measure failure.");
    }
}