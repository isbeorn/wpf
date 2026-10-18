using System.Windows;
using System.Windows.Media;

internal static class DashedPenRetentionChecks
{
    internal static void Run(Action drain)
    {
        var style = new DashStyle([1, 2], 0);
        var pen = new Pen(Brushes.Black, 2) { DashStyle = style, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, DashCap = PenLineCap.Flat };
        var root = new ContainerVisual();
        var line = new DrawingVisual();
        using (var drawing = line.RenderOpen()) drawing.DrawLine(pen, new Point(8, 8), new Point(68, 8));
        var sibling = new DrawingVisual();
        using (var drawing = sibling.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(90, 4, 16, 16));
        root.Children.Add(line); root.Children.Add(sibling);
        using var target = new Target(root, 128, 32);
        drain();
        byte[] original = Snapshot();
        foreach (double offset in new[] { 1d, -1d, 0d })
        {
            style.Offset = offset;
            drain();
            byte[] actual = Snapshot();
            SameSibling(actual);
            if (offset == 0) Equal(actual, original, "Restoring the retained offset did not restore the original raster.");
            else Different(actual, original, "Changing the retained offset left the previous raster.");
        }
        style.Dashes[0] = 2;
        drain();
        Different(Snapshot(), original, "Changing a retained dash entry did not invalidate the target.");
        style.Dashes[0] = 1;
        drain();
        Equal(Snapshot(), original, "Restoring the retained entry did not restore the raster.");
        pen.DashStyle = DashStyles.Solid;
        drain();
        byte[] solid = Snapshot();
        Different(solid, original, "Clearing dashes left a dashed raster.");
        SameSibling(solid);
        pen.DashStyle = style;
        drain();
        Equal(Snapshot(), original, "Restoring the retained DashStyle did not restore dashes.");
        style.Dashes[0] = double.NaN;
        try { target.Render(); throw new InvalidOperationException("An invalid retained dash interval was silently painted."); }
        catch (NotSupportedException error) when (error.Message.Contains("dash", StringComparison.OrdinalIgnoreCase)) { }
        style.Dashes[0] = 1;
        drain();
        Equal(Snapshot(), original, "A valid retained frame did not recover after a rejected dashed frame.");

        byte[] Snapshot() { using var bitmap = target.Snapshot(); return bitmap.GetPixelSpan().ToArray(); }
        void SameSibling(byte[] actual)
        {
            for (int y = 0; y < 32; y++)
                if (!actual.AsSpan((y * 128 + 80) * 4, 48 * 4).SequenceEqual(original.AsSpan((y * 128 + 80) * 4, 48 * 4)))
                    throw new InvalidOperationException("Dash mutation changed the unrelated sibling raster.");
        }
    }
    private static void Equal(byte[] actual, byte[] expected, string message)
    { if (!actual.SequenceEqual(expected)) throw new InvalidOperationException(message); }
    private static void Different(byte[] actual, byte[] expected, string message)
    { if (actual.SequenceEqual(expected)) throw new InvalidOperationException(message); }
}
