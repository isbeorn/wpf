using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class InvertedTextBlockCases
{
    internal static object[] Capture(Func<Visual, int, int, byte[]> raster)
    {
        var results = new List<object>();
        foreach (string text in new[] { "AV 123", "\u05d0\u05d1\u05d2 123" })
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var block = new TextBlock { Text = text, FlowDirection = flow, FontSize = 24, TextAlignment = TextAlignment.Left,
                Foreground = Brushes.Black, FontFamily = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans") };
            var root = new ContainerVisual();
            root.Children.Add(block);
            block.Measure(new Size(240, 80));
            block.Arrange(new Rect(0, 0, 240, 80));
            block.UpdateLayout();
            Matrix transform = VisualTreeHelper.GetTransform(block)?.Value ?? Matrix.Identity;
            Vector offset = VisualTreeHelper.GetOffset(block);
            transform.Translate(offset.X, offset.Y);
            var glyphs = new List<object>();
            Walk(VisualTreeHelper.GetDrawing(block), transform, glyphs);
            byte[] pixels = raster(root, 240, 80);
            int ink = pixels.Where((value, index) => index % 4 == 3 && value != 0).Count();
            results.Add(new { text, flow = flow.ToString(), desired = new[] { block.DesiredSize.Width, block.DesiredSize.Height },
                renderSize = new[] { block.RenderSize.Width, block.RenderSize.Height }, glyphs, ink, pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)) });
        }
        return results.ToArray();
    }
    internal static object[] Verify(Func<Visual, int, int, byte[]> raster)
    {
        object[] results = Capture(raster);
        var verified = new List<object>();
        foreach (JsonElement scene in JsonSerializer.SerializeToElement(results).EnumerateArray())
        {
            var glyphs = scene.GetProperty("glyphs").EnumerateArray().ToArray();
            if (scene.GetProperty("ink").GetInt32() <= 0 || glyphs.Length == 0)
                throw new InvalidOperationException("A genuine inverted TextBlock produced no glyphs or raster ink.");
            string actual = string.Concat(glyphs.Select(glyph => glyph.GetProperty("text").GetString()));
            string expected = scene.GetProperty("text").GetString()!;
            if (!actual.OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)))
                throw new InvalidOperationException("The genuine TextBlock lost visible source characters.");
            foreach (var glyph in glyphs)
            {
                double[] matrix = glyph.GetProperty("matrix").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                if (matrix[0] != 1 || matrix[1] != 0 || matrix[2] != 0 || matrix[3] != 1)
                    throw new InvalidOperationException("TextBlock left its glyph shapes mirrored by its actual framework flow transform.");
                double[] bounds = glyph.GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                if (bounds.Any(value => !double.IsFinite(value)) || bounds[1] < 0 || bounds[1] + bounds[3] > 80 ||
                    scene.GetProperty("flow").GetString() == "RightToLeft" && (bounds[0] < 0 || bounds[0] + bounds[2] > 240))
                    throw new InvalidOperationException("A genuine TextBlock moved its glyph ink outside the calibrated layout extent.");
            }
            verified.Add(new { name = "actual-inverted-textblock-" + (expected.StartsWith("AV", StringComparison.Ordinal) ? "latin-" : "hebrew-") + scene.GetProperty("flow").GetString(),
                status = "pass", observation = results[verified.Count] });
        }
        return verified.ToArray();
    }
    private static void Walk(Drawing drawing, Matrix parent, List<object> glyphs)
    {
        if (drawing is DrawingGroup group)
        {
            Matrix transform = group.Transform?.Value ?? Matrix.Identity;
            transform.Append(parent);
            foreach (Drawing child in group.Children) Walk(child, transform, glyphs);
        }
        else if (drawing is GlyphRunDrawing glyph)
        {
            Rect bounds = glyph.GlyphRun.ComputeInkBoundingBox();
            bounds.Offset(glyph.GlyphRun.BaselineOrigin.X, glyph.GlyphRun.BaselineOrigin.Y);
            bounds.Transform(parent);
            glyphs.Add(new { text = new string(glyph.GlyphRun.Characters.ToArray()), glyph.GlyphRun.BidiLevel,
                matrix = new[] { parent.M11, parent.M12, parent.M21, parent.M22, parent.OffsetX, parent.OffsetY },
                bounds = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height } });
        }
    }
}
