using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

static class WrappedTextBlockCases
{
    internal static object[] Verify(Func<TextBlock, int, int, byte[]> raster)
    {
        const string text = "Width-sensitive real text wraps into a different number of lines when the available width changes.";
        const int height = 500;
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = Brushes.Black,
            FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans") };
        var phases = new List<object>();
        byte[]? initial = null;
        double initialHeight = 0;
        int initialLines = 0;
        void Render(string phase, int width, bool measure = true, bool restore = false)
        {
            bool measureWasValid = block.IsMeasureValid;
            if (measure) block.Measure(new Size(width, height));
            else if (!measureWasValid) throw new InvalidOperationException("The render-only property change invalidated measure.");
            block.Arrange(new Rect(0, 0, width, height));
            block.UpdateLayout();
            var drawing = VisualTreeHelper.GetDrawing(block);
            var glyphs = Walk(drawing, Matrix.Identity).ToArray();
            string characters = string.Concat(glyphs.Select(item => new string(item.Glyph.GlyphRun.Characters.ToArray())));
            var colors = glyphs.Select(item => item.Glyph.ForegroundBrush.ToString()).Distinct().ToArray();
            int lines = glyphs.Select(item => Math.Round(item.Origin.Y, 6)).Distinct().Count();
            if (!block.IsMeasureValid || characters != block.Text || colors.Length != 1 || colors[0] != block.Foreground.ToString() || drawing.Bounds.IsEmpty)
                throw new InvalidOperationException("The genuine wrapped TextBlock retained stale text or foreground after " + phase);
            byte[] pixels = raster(block, width, height);
            if (pixels.Length != width * height * 4) throw new InvalidOperationException("The raster must contain tightly packed premultiplied BGRA pixels.");
            int ink = 0, bands = 0, redRoundingPixels = 0, maxRedRounding = 0;
            bool previousRow = false;
            bool red = ReferenceEquals(block.Foreground, Brushes.Red);
            for (int y = 0; y < height; y++)
            {
                bool row = false;
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    byte alpha = pixels[i + 3];
                    if (alpha == 0) continue;
                    int redRounding = red ? alpha - pixels[i + 2] : pixels[i + 2];
                    maxRedRounding = Math.Max(maxRedRounding, Math.Abs(redRounding));
                    if (redRounding != 0) redRoundingPixels++;
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || redRounding < 0 || redRounding > (red ? 1 : 0))
                        throw new InvalidOperationException($"A wrapped glyph kept the wrong foreground pixel after {phase}: ({x},{y}) BGRA={pixels[i]},{pixels[i+1]},{pixels[i+2]},{alpha}");
                    ink++;
                    row = true;
                }
                if (row && !previousRow) bands++;
                previousRow = row;
            }
            if (ink == 0 || lines < 1 || bands != lines)
                throw new InvalidOperationException("The actual raster lost one or more retained line bands after " + phase);
            if (initial == null)
            {
                if (lines < 2) throw new InvalidOperationException("The initial TextBlock did not actually wrap.");
                initial = pixels;
                initialHeight = block.DesiredSize.Height;
                initialLines = lines;
            }
            else if (restore && (!initial.SequenceEqual(pixels) || block.DesiredSize.Height != initialHeight || lines != initialLines))
                throw new InvalidOperationException("Restoring the wrapped TextBlock did not restore all pixels and line metrics after " + phase);
            if (phase == "widen" && !(lines < initialLines && block.DesiredSize.Height < initialHeight))
                throw new InvalidOperationException("Widening did not reduce the genuine wrapped line count.");
            phases.Add(new { name = "actual-wrapped-textblock-" + phase, status = "pass", width, block.FontSize,
                measureWasValid, measureCalled = measure, block.DesiredSize, characters, colors, glyphRuns = glyphs.Length, lines, bands, ink, redRoundingPixels, maxRedRounding });
        }
        Render("initial", 380);
        block.Foreground = Brushes.Red;
        Render("render-only-foreground-change", 380, measure: false);
        block.Foreground = Brushes.Black;
        Render("foreground-restore", 380, measure: false, restore: true);
        Render("widen", 760);
        Render("width-restore", 380, restore: true);
        block.Text = "Updated source. " + text;
        Render("text-insert", 380);
        block.Text = text;
        Render("text-restore", 380, restore: true);
        block.FontSize = 20;
        Render("font-enlarge", 380);
        block.FontSize = 16;
        Render("font-restore", 380, restore: true);
        return phases.ToArray();
    }
    private static IEnumerable<(GlyphRunDrawing Glyph, Point Origin)> Walk(Drawing drawing, Matrix transform)
    {
        if (drawing is GlyphRunDrawing glyph) yield return (glyph, transform.Transform(glyph.GlyphRun.BaselineOrigin));
        if (drawing is DrawingGroup group)
        {
            Matrix childTransform = group.Transform?.Value ?? Matrix.Identity;
            childTransform.Append(transform);
            foreach (Drawing child in group.Children) foreach (var item in Walk(child, childTransform)) yield return item;
        }
    }
}