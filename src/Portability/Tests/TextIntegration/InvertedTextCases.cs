using System.Text.Json;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

internal static class InvertedTextCases
{
    internal static object Capture()
    {
        using var formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        var cases = new List<object>();
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        foreach (string profile in new[] { "plain", "decorated", "object", "mixed-object" })
        foreach (Point origin in new[] { new Point(), new Point(13, 17) })
        foreach (InvertAxes axes in new[] { InvertAxes.None, InvertAxes.Horizontal, InvertAxes.Vertical, InvertAxes.Both })
            cases.Add(Draw(formatter, flow, profile, origin, axes, false));
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            cases.Add(Draw(formatter, flow, "object", new Point(13, 17), InvertAxes.Both, true));
        return new { framework = typeof(TextFormatter).Assembly.FullName,
            frameworkFileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(TextFormatter).Assembly.Location).FileVersion, cases };
    }

    internal static int Verify()
    {
        JsonElement capture = JsonSerializer.SerializeToElement(Capture());
        var cases = capture.GetProperty("cases").EnumerateArray().ToArray();
        foreach (JsonElement scene in cases)
        {
            bool rtl = scene.GetProperty("flow").GetString() == "RightToLeft";
            string axes = scene.GetProperty("axes").GetString()!;
            string profile = scene.GetProperty("profile").GetString()!;
            var origin = Numbers(scene.GetProperty("origin"));
            var parent = MatrixOf(scene.GetProperty("parent"));
            var leaves = scene.GetProperty("leaves").EnumerateArray().ToArray();
            bool failedObject = scene.GetProperty("failObject").GetBoolean();
            string? error = scene.GetProperty("error").GetString();
            if (failedObject ? error != "System.InvalidOperationException: Owned inverted object draw failure." : error != null)
                throw new InvalidOperationException($"Unexpected draw result for {profile}/{rtl}/{axes}: {error}");
            var marker = leaves.Single(leaf => leaf.TryGetProperty("fill", out var fill) && fill.GetString() == "#FFFF00FF");
            EqualMatrix(MatrixOf(marker.GetProperty("matrix")), parent, "Sibling marker retained an inverted line transform.");
            if (profile.EndsWith("object", StringComparison.Ordinal))
            {
                if (profile == "object" && axes == "None")
                {
                    Near(scene.GetProperty("Extent").GetDouble(), 25, "Embedded line ink height changed.");
                    Near(scene.GetProperty("OverhangLeading").GetDouble(), 2, "Embedded UV leading ink edge changed.");
                    Near(scene.GetProperty("OverhangTrailing").GetDouble(), 9, "Embedded UV trailing ink edge changed.");
                    Near(scene.GetProperty("OverhangAfter").GetDouble(), -1, "Embedded UV lower ink edge changed.");
                }
                var callback = scene.GetProperty("objectCalls").EnumerateArray().Single();
                double left = scene.GetProperty("objectTextBounds")[0][0].GetDouble();
                double width = scene.GetProperty("paragraphWidth").GetDouble();
                double expectedX = origin[0] + (rtl && axes == "None" ? width - left : left);
                var point = Numbers(callback.GetProperty("origin"));
                Near(point[0], expectedX, "Embedded callback did not use the stock paragraph edge.");
                Near(point[1], origin[1] + scene.GetProperty("Baseline").GetDouble(), "Embedded callback lost the caller origin.");
                if (callback.GetProperty("rightToLeft").GetBoolean() != rtl || callback.GetProperty("sideways").GetBoolean())
                    throw new InvalidOperationException("Embedded callback orientation flags changed.");
                foreach (var leaf in leaves.Where(leaf => leaf.TryGetProperty("fill", out var fill) && fill.GetString() is "#FFFFA500" or "#FFFF0000"))
                    EqualMatrix(MatrixOf(leaf.GetProperty("matrix")), parent, "Embedded drawing did not use the caller surface.");
            }
            if (axes == "None" || failedObject) continue;
            var none = cases.Single(other => !other.GetProperty("failObject").GetBoolean() &&
                other.GetProperty("axes").GetString() == "None" && other.GetProperty("flow").GetString() == scene.GetProperty("flow").GetString() &&
                other.GetProperty("profile").GetString() == profile && Numbers(other.GetProperty("origin")).SequenceEqual(origin));
            var before = none.GetProperty("leaves").EnumerateArray().Where(IsLineContent).ToArray();
            var after = leaves.Where(IsLineContent).ToArray();
            if (before.Length != after.Length) throw new InvalidOperationException("Inversion lost glyph, background or decoration drawing.");
            double sx = axes is "Horizontal" or "Both" ? -1 : 1;
            double sy = axes is "Vertical" or "Both" ? -1 : 1;
            var anti = new Matrix(sx, 0, 0, sy, sx < 0 ? scene.GetProperty("paragraphWidth").GetDouble() : 0,
                sy < 0 ? scene.GetProperty("Height").GetDouble() : 0);
            Matrix inverseParent = parent;
            inverseParent.Invert();
            Point Expected(Point world)
            {
                Point local = inverseParent.Transform(world);
                if (rtl) local.X -= 2 * origin[0];
                return parent.Transform(anti.Transform(local));
            }
            for (int index = 0; index < before.Length; index++)
            {
                if (before[index].GetProperty("kind").GetString() != after[index].GetProperty("kind").GetString())
                    throw new InvalidOperationException("Inversion changed drawing order.");
                if (before[index].GetProperty("kind").GetString() == "glyph")
                {
                    Point expected = Expected(PointOf(before[index].GetProperty("worldBaseline")));
                    EqualPoint(PointOf(after[index].GetProperty("worldBaseline")), expected, "Inverted glyph origin differs from the stock surface contract.");
                }
                else
                {
                    double[] oldBounds = Numbers(before[index].GetProperty("localBounds"));
                    double[] newBounds = Numbers(after[index].GetProperty("localBounds"));
                    Matrix oldMatrix = MatrixOf(before[index].GetProperty("matrix"));
                    Matrix newMatrix = MatrixOf(after[index].GetProperty("matrix"));
                    foreach ((double x, double y) in new[] { (0d, 0d), (1d, 0d), (0d, 1d), (1d, 1d) })
                        EqualPoint(newMatrix.Transform(new Point(newBounds[0] + x * newBounds[2], newBounds[1] + y * newBounds[3])),
                            Expected(oldMatrix.Transform(new Point(oldBounds[0] + x * oldBounds[2], oldBounds[1] + y * oldBounds[3]))),
                            "Inverted background or decoration differs from the stock surface contract.");
                }
                Matrix actualMatrix = MatrixOf(after[index].GetProperty("matrix"));
                Near(actualMatrix.M11, sx * parent.M11, "Horizontal glyph/geometry orientation changed.");
                Near(actualMatrix.M12, sx * parent.M12, "Horizontal glyph/geometry orientation changed.");
                Near(actualMatrix.M21, sy * parent.M21, "Vertical glyph/geometry orientation changed.");
                Near(actualMatrix.M22, sy * parent.M22, "Vertical glyph/geometry orientation changed.");
            }
        }
        return cases.Length;
    }
    private static bool IsLineContent(JsonElement leaf) => leaf.GetProperty("kind").GetString() == "glyph" ||
        leaf.TryGetProperty("fill", out var fill) && fill.GetString() is "#FF000000" or "#FF0000FF";
    private static double[] Numbers(JsonElement array) => array.EnumerateArray().Select(value => value.GetDouble()).ToArray();
    private static Point PointOf(JsonElement array) { double[] p = Numbers(array); return new Point(p[0], p[1]); }
    private static Matrix MatrixOf(JsonElement array) { double[] m = Numbers(array); return new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]); }
    private static void EqualPoint(Point actual, Point expected, string message) { Near(actual.X, expected.X, message); Near(actual.Y, expected.Y, message); }
    private static void EqualMatrix(Matrix actual, Matrix expected, string message)
    {
        foreach ((double a, double e) in Values(actual).Zip(Values(expected))) Near(a, e, message);
    }
    private static void Near(double actual, double expected, string message)
    {
        // This bounds only arithmetic roundoff in the fixed affine test scene, not backend raster or font differences.
        double roundoff = 256 * Math.ScaleB(1, -52) * Math.Max(400, Math.Max(Math.Abs(actual), Math.Abs(expected)));
        if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > roundoff)
            throw new InvalidOperationException($"{message} actual={actual:R}; expected={expected:R}");
    }
    private static object Draw(TextFormatter formatter, FlowDirection flow, string profile, Point origin, InvertAxes axes, bool failObject)
    {
        const double paragraphWidth = 200;
        var properties = new Properties(profile == "decorated" || profile == "mixed-object");
        var item = profile.EndsWith("object", StringComparison.Ordinal) ? new ObjectRun(properties, failObject) : null;
        using var line = formatter.FormatLine(new Source(properties, item, profile == "mixed-object"), 0, paragraphWidth, new Paragraph(properties, flow), null!);
        var parent = new Matrix(1.25, .25, -.125, .75, 60, 40);
        var visual = new DrawingVisual();
        string? error = null;
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(parent));
            try { line.Draw(context, origin, axes); }
            catch (Exception exception) { error = exception.GetType().FullName + ": " + exception.Message; }
            context.DrawRectangle(Brushes.Magenta, null, new Rect(300, 200, 7, 11));
            context.Pop();
        }
        var leaves = new List<object>();
        Describe(visual.Drawing, Matrix.Identity, leaves);
        return new { flow = flow.ToString(), profile, origin = new[] { origin.X, origin.Y }, axes = axes.ToString(), failObject,
            paragraphWidth, line.Width, line.Height, line.Baseline, line.Length, line.Extent, line.OverhangLeading, line.OverhangTrailing, line.OverhangAfter, parent = Values(parent), error,
            objectCalls = item?.Calls, objectTextBounds = item == null ? null : line.GetTextBounds(profile == "mixed-object" ? 1 : 0, 1).Select(bound => Box(bound.Rectangle)).ToArray(), leaves };
    }

    private static void Describe(Drawing drawing, Matrix parent, List<object> leaves)
    {
        if (drawing is DrawingGroup group)
        {
            Matrix transform = group.Transform?.Value ?? Matrix.Identity;
            transform.Append(parent);
            foreach (Drawing child in group.Children) Describe(child, transform, leaves);
        }
        else if (drawing is GlyphRunDrawing glyph)
        {
            Rect bounds = glyph.GlyphRun.ComputeInkBoundingBox();
            bounds.Offset(glyph.GlyphRun.BaselineOrigin.X, glyph.GlyphRun.BaselineOrigin.Y);
            bounds.Transform(parent);
            leaves.Add(new { kind = "glyph", color = glyph.ForegroundBrush.ToString(),
                text = new string(glyph.GlyphRun.Characters.ToArray()), glyph.GlyphRun.BidiLevel,
                baseline = new[] { glyph.GlyphRun.BaselineOrigin.X, glyph.GlyphRun.BaselineOrigin.Y },
                worldBaseline = PointValues(parent.Transform(glyph.GlyphRun.BaselineOrigin)),
                matrix = Values(parent), bounds = Box(bounds) });
        }
        else if (drawing is GeometryDrawing geometry)
        {
            Rect bounds = geometry.Bounds;
            bounds.Transform(parent);
            leaves.Add(new { kind = "geometry", fill = geometry.Brush?.ToString(), pen = geometry.Pen?.Brush?.ToString(),
                localBounds = Box(geometry.Bounds), matrix = Values(parent), bounds = Box(bounds) });
        }
    }
    private static double[] Values(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY];
    private static double[] PointValues(Point point) => [point.X, point.Y];
    private static double[] Box(Rect bounds) => bounds.IsEmpty ? [] : [bounds.X, bounds.Y, bounds.Width, bounds.Height];

    private sealed class Properties(bool decorated) : TextRunProperties
    {
        public override Typeface Typeface { get; } = new(new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public override double FontRenderingEmSize => 24;
        public override double FontHintingEmSize => 24;
        public override TextDecorationCollection TextDecorations => decorated ? System.Windows.TextDecorations.Underline : null!;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush BackgroundBrush => decorated ? Brushes.Blue : null!;
        public override CultureInfo CultureInfo => CultureInfo.GetCultureInfo("en-US");
        public override TextEffectCollection TextEffects => null!;
    }
    private sealed class Paragraph(Properties properties, FlowDirection flow) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => flow;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties TextMarkerProperties => null!;
        public override double Indent => 0;
    }
    private sealed class Source(Properties properties, ObjectRun? item, bool mixed) : TextSource
    {
        public override TextRun GetTextRun(int index)
        {
            if (item == null) return index >= 2 ? new TextEndOfParagraph(1) : new TextCharacters("AV", index, 2 - index, properties);
            if (!mixed) return index == 0 ? item : new TextEndOfParagraph(1);
            return index switch { 0 => new TextCharacters("A", properties), 1 => item, 2 => new TextCharacters("V", properties), _ => new TextEndOfParagraph(1) };
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new(0,
            new CultureSpecificCharacterBufferRange(properties.CultureInfo, new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
    private sealed class ObjectRun(Properties properties, bool fail) : TextEmbeddedObject
    {
        internal List<object> Calls { get; } = new();
        public override int Length => 1;
        public override TextRunProperties Properties => properties;
        public override CharacterBufferReference CharacterBufferReference => new();
        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakDesired;
        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;
        public override bool HasFixedSize => true;
        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(18, 30, 24);
        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(2, -20, 7, 25);
        public override void Draw(DrawingContext context, Point origin, bool rightToLeft, bool sideways)
        {
            Calls.Add(new { origin = new[] { origin.X, origin.Y }, rightToLeft, sideways });
            if (fail) throw new InvalidOperationException("Owned inverted object draw failure.");
            context.DrawRectangle(Brushes.Orange, null, new Rect(origin.X + (rightToLeft ? -9 : 2), origin.Y - 20, 7, 25));
            context.DrawRectangle(Brushes.Red, null, new Rect(origin.X + (rightToLeft ? -4 : 2), origin.Y - 20, 2, 7));
        }
    }
}
