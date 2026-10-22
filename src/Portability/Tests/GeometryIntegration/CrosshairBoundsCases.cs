using System.Windows;
using System.Windows.Media;

internal static class CrosshairBoundsCases
{
    private const string Lines = "M0.5,0 L0.5,0.4 M0.5,0.6 L0.5,1 M0,0.5 L0.4,0.5 M0.6,0.5 L1,0.5";
    private const string Rings = "M0.35,0.5 A0.15,0.15 1 1 1 0.35,0.50001 M0.4,0.5 A0.1,0.1 1 1 1 0.4,0.50001 M0.45,0.5 A0.05,0.05 1 1 1 0.45,0.50001 M0.48,0.5 A0.02,0.02 1 1 1 0.48,0.50001 M0.49,0.5 A0.01,0.01 1 1 1 0.49,0.50001 M0.495,0.5 A0.005,0.005 1 1 1 0.495,0.50001";

    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        Geometry crosshair = Geometry.Parse(Lines + " " + Rings);
        var unit = new Rect(0, 0, 1, 1);
        check("crosshair-original-bounds", () => Equal(crosshair.Bounds, unit), true);
        check("crosshair-clone-bounds", () => Equal(crosshair.Clone().Bounds, unit), true);
        check("crosshair-current-clone-bounds", () => Equal(crosshair.CloneCurrentValue().Bounds, unit), true);
        check("crosshair-path-conversion-bounds", () => Equal(PathGeometry.CreateFromGeometry(crosshair).Bounds, unit), true);
        check("crosshair-reordered-contours-bounds", () => Equal(Geometry.Parse(Rings + " " + Lines).Bounds, unit), true);
        check("crosshair-lines-only-bounds", () => Equal(Geometry.Parse(Lines).Bounds, unit), true);
        check("crosshair-translated-bounds", () =>
        {
            Geometry translated = crosshair.Clone(); translated.Transform = new TranslateTransform(17, -11);
            return Equal(translated.Bounds, new Rect(17, -11, 1, 1));
        }, true);
        check("crosshair-reflected-rotated-bounds", () =>
        {
            Geometry transformed = crosshair.Clone(); transformed.Transform = new MatrixTransform(0, -2, -3, 0, 19, 23);
            return Equal(transformed.Bounds, new Rect(16, 21, 3, 2));
        }, true);
        check("mixed-curves-keep-tight-extrema", () => Equal(
            Geometry.Parse("M0,0 C0,20 20,20 20,0 M1,1 L2,1").Bounds, new Rect(0, 0, 20, 15)), true);
        check("crosshair-path-uniform-stretch", ArrangeCrosshair, true);
    }

    private static bool ArrangeCrosshair()
    {
        bool result = false; Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var path = new System.Windows.Shapes.Path
                {
                    Width = 6000, Height = 4000, Data = Geometry.Parse(Lines + " " + Rings), Stretch = Stretch.Uniform,
                    Stroke = Brushes.Gray, StrokeThickness = 1, UseLayoutRounding = true
                };
                path.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                path.Arrange(new Rect(0, 0, 6000, 4000));
                Matrix transform = path.RenderedGeometry.Transform.Value;
                result = path.ActualWidth == 4000 && path.ActualHeight == 4000 &&
                    Equal(path.RenderedGeometry.Bounds, new Rect(.5, .5, 3999, 3999)) &&
                    transform == new Matrix(3999, 0, 0, 3999, .5, .5);
            }
            catch (Exception exception) { failure = exception; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static bool Equal(Rect actual, Rect expected) => !actual.IsEmpty &&
        Math.Abs(actual.X - expected.X) <= 1e-6 && Math.Abs(actual.Y - expected.Y) <= 1e-6 &&
        Math.Abs(actual.Width - expected.Width) <= 1e-6 && Math.Abs(actual.Height - expected.Height) <= 1e-6;
}