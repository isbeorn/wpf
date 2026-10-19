using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;

internal static partial class DockingProgram
{
    private static int ObserveWindowTheme()
    {
        var checks = new List<string>();
        var observations = new List<object>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        void Check(bool value, string name) { Require(value, name); checks.Add(name); }
        void Layout(Window window) { window.UpdateLayout(); }
        Style? Theme(FrameworkElement element) => (Style?)typeof(FrameworkElement).GetProperty("ThemeStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(element);
        try
        {
            var content = new Border { Width = 80, Height = 30 };
            var ordinary = new Window { Width = 300, Height = 220, WindowStyle = WindowStyle.None, ShowActivated = false, Content = content };
            ordinary.Show(); Layout(ordinary);
            Check(ordinary.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue && ordinary.Template is not null
                && VisualTreeHelper.GetParent(content) is ContentPresenter && content.ActualWidth == 80 && content.ActualHeight == 30, "ordinary-window-theme-content");
            var ordinaryTheme = ordinary.Template;
            var localRoot = new FrameworkElementFactory(typeof(Border), "PART_LocalTemplate");
            var localTemplate = new ControlTemplate(typeof(Window)) { VisualTree = localRoot };
            ordinary.Template = localTemplate; Layout(ordinary);
            Check(ordinary.Template.FindName("PART_LocalTemplate", ordinary) is Border, "local-template-assignment");
            ordinary.ClearValue(Control.TemplateProperty); Layout(ordinary);
            Check(ReferenceEquals(ordinary.Template, ordinaryTheme) && content.ActualWidth == 80, "local-template-clear-restores-theme");
            ordinary.Template = localTemplate; Layout(ordinary);
            Check(ReferenceEquals(ordinary.Template, localTemplate), "local-template-reassignment");
            ordinary.ClearValue(Control.TemplateProperty); Layout(ordinary); ordinary.Close();
            var inherited = new ThemeKeyWindow(); inherited.Show(); Layout(inherited);
            Check(inherited.Template is not null && ReferenceEquals(inherited.GetThemeKey(), typeof(Window)), "inherited-exact-window-key");
            inherited.Style = null; Layout(inherited);
            Check(inherited.Template is not null && !inherited.OverridesDefaultStyle, "style-null-preserves-window-theme");
            inherited.OverridesDefaultStyle = true; Layout(inherited);
            Check(Theme(inherited) is null, "overrides-default-style-opt-out");
            inherited.OverridesDefaultStyle = false; Layout(inherited);
            Check(inherited.Template is not null, "overrides-default-style-restore");
            inherited.SetThemeKey(null); Layout(inherited);
            Check(Theme(inherited) is null, "null-theme-key-opt-out");
            inherited.SetThemeKey("missing-window-theme-key"); Layout(inherited);
            Check(Theme(inherited) is null, "unrelated-theme-key-no-window-fallback");
            inherited.SetThemeKey(typeof(Window)); Layout(inherited);
            Check(inherited.Template is not null, "window-theme-key-restore"); inherited.Close();
            var manager = new DockingManager();
            var overlay = (OverlayWindow)Activator.CreateInstance(typeof(OverlayWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { manager }, null)!;
            overlay.Width = 400; overlay.Height = 300; overlay.Show(); Layout(overlay);
            var themeTemplate = overlay.Template;
            bool Parts() => overlay.Template?.FindName("PART_DockingManagerDropTargets", overlay) is Grid
                && overlay.Template.FindName("PART_DropTargetsContainer", overlay) is Canvas
                && overlay.Template.FindName("PART_PreviewBox", overlay) is System.Windows.Shapes.Path;
            Check(overlay.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue && Parts()
                && DependencyPropertyHelper.GetValueSource(overlay, Control.TemplateProperty).BaseValueSource == BaseValueSource.DefaultStyle, "actual-overlay-generic-theme-parts");
            var explicitStyle = new Style(typeof(OverlayWindow)); explicitStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Red));
            overlay.Style = explicitStyle; Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && ReferenceEquals(overlay.Background, Brushes.Red), "explicit-style-without-template-preserves-overlay-theme");
            overlay.ClearValue(FrameworkElement.StyleProperty); Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && !ReferenceEquals(overlay.Background, Brushes.Red), "remove-explicit-style-restores-overlay-theme");
            var implicitStyle = new Style(typeof(OverlayWindow)); implicitStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Blue));
            overlay.Resources[typeof(OverlayWindow)] = implicitStyle; Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && ReferenceEquals(overlay.Background, Brushes.Blue), "exact-type-implicit-style-preserves-overlay-theme");
            overlay.Resources.Remove(typeof(OverlayWindow)); Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && !ReferenceEquals(overlay.Background, Brushes.Blue), "remove-implicit-style-restores-overlay-theme");
            overlay.Resources[typeof(OverlayWindow)] = implicitStyle; Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && ReferenceEquals(overlay.Background, Brushes.Blue), "readd-implicit-style-preserves-overlay-theme");
            overlay.Style = null; Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts() && !ReferenceEquals(overlay.Background, Brushes.Blue), "local-style-null-retains-overlay-theme");
            overlay.OverridesDefaultStyle = true; Layout(overlay);
            Check(Theme(overlay) is null, "overlay-overrides-default-style-opt-out");
            overlay.OverridesDefaultStyle = false; Layout(overlay);
            Check(ReferenceEquals(overlay.Template, themeTemplate) && Parts(), "overlay-theme-restored-after-opt-out");
            observations.Add(new { templateSource = DependencyPropertyHelper.GetValueSource(overlay, Control.TemplateProperty).BaseValueSource.ToString(),
                localTemplate = overlay.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue,
                overlay.IsActive, overlay.ActualWidth, overlay.ActualHeight });
            overlay.Close();
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", checks, observations }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "fail", checks, error = exception.ToString(), observations }, new JsonSerializerOptions { WriteIndented = true })); return 1;
        }
        finally { app.Shutdown(); }
    }
    private sealed class ThemeKeyWindow : Window
    {
        internal ThemeKeyWindow() { Width = 300; Height = 220; WindowStyle = WindowStyle.None; ShowActivated = false; }
        internal object GetThemeKey() => DefaultStyleKey;
        internal void SetThemeKey(object? key) => DefaultStyleKey = key;
    }
}