using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;

internal static partial class Program
{
    private sealed class DiagnosticAbortException : Exception { }
    private static bool geometryFaultCaptured;
    private static void CaptureFirstGeometryFault(Exception fault)
    {
        if (geometryFaultCaptured) return;
        geometryFaultCaptured = true;
        var invalid = new List<object>();
        var captureErrors = new List<object>();
        var pathOwners = new Dictionary<string, System.Windows.Shapes.Path>();
        var invalidPaths = new List<string>();
        int visualCount = 0, drawingCount = 0, geometryCount = 0;
        var activeDrawings = new HashSet<Drawing>(ReferenceEqualityComparer.Instance);
        var activeGeometries = new HashSet<Geometry>(ReferenceEqualityComparer.Instance);
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        object PointValue(Point p) => new { x = Number(p.X), y = Number(p.Y) };
        object MatrixValue(Matrix m) => new { m11 = Number(m.M11), m21 = Number(m.M21), offsetX = Number(m.OffsetX), m12 = Number(m.M12), m22 = Number(m.M22), offsetY = Number(m.OffsetY) };
        object? TransformValue(Transform? t) => t == null ? null : new { type = t.GetType().FullName, matrix = MatrixValue(t.Value) };
        object RectValue(Rect r) => new { x = Number(r.X), y = Number(r.Y), width = Number(r.Width), height = Number(r.Height), r.IsEmpty };
        object SegmentValue(PathSegment segment) => new { type = segment.GetType().FullName, segment.IsStroked, segment.IsSmoothJoin, values = segment switch
        {
            LineSegment s => (object)new { point = PointValue(s.Point) },
            BezierSegment s => new { point1 = PointValue(s.Point1), point2 = PointValue(s.Point2), point3 = PointValue(s.Point3) },
            QuadraticBezierSegment s => new { point1 = PointValue(s.Point1), point2 = PointValue(s.Point2) },
            ArcSegment s => new { point = PointValue(s.Point), width = Number(s.Size.Width), height = Number(s.Size.Height), rotation = Number(s.RotationAngle), s.IsLargeArc, sweep = s.SweepDirection.ToString() },
            PolyLineSegment s => new { points = s.Points.Select(PointValue).ToArray() },
            PolyBezierSegment s => new { points = s.Points.Select(PointValue).ToArray() },
            PolyQuadraticBezierSegment s => new { points = s.Points.Select(PointValue).ToArray() },
            _ => new { unsupportedSegment = segment.GetType().FullName }
        } };
        object ShapeValue(Geometry geometry) => geometry switch
        {
            RectangleGeometry g => new { rect = RectValue(g.Rect), radiusX = Number(g.RadiusX), radiusY = Number(g.RadiusY) },
            EllipseGeometry g => new { center = PointValue(g.Center), radiusX = Number(g.RadiusX), radiusY = Number(g.RadiusY) },
            LineGeometry g => new { start = PointValue(g.StartPoint), end = PointValue(g.EndPoint) },
            PathGeometry g => new { fillRule = g.FillRule.ToString(), figures = g.Figures.Select(f => new { start = PointValue(f.StartPoint), f.IsClosed, f.IsFilled, segments = f.Segments.Select(SegmentValue).ToArray() }).ToArray() },
            StreamGeometry g => new { fillRule = g.FillRule.ToString(), contourObservation = "No GetAsPathGeometry, Bounds or serialization invoked." },
            GeometryGroup g => new { fillRule = g.FillRule.ToString(), childCount = g.Children.Count },
            CombinedGeometry g => new { mode = g.GeometryCombineMode.ToString() },
            _ => new { geometryType = geometry.GetType().FullName }
        };
        void GeometryNode(Geometry? geometry, string path, object[] owners, object[] ancestry)
        {
            if (geometry == null || !activeGeometries.Add(geometry)) return;
            try
            {
                if (++geometryCount > 100000) throw new InvalidDataException("Diagnostic geometry traversal bound exceeded.");
                Matrix m = geometry.Transform?.Value ?? Matrix.Identity;
                string[] names = ["M11", "M21", "OffsetX", "M12", "M22", "OffsetY"];
                double[] values = [m.M11, m.M21, m.OffsetX, m.M12, m.M22, m.OffsetY];
                string[] rejected = names.Where((_, i) => !float.IsFinite((float)values[i])).ToArray();
                object node = new { path, type = geometry.GetType().FullName, transform = TransformValue(geometry.Transform) };
                if (rejected.Length != 0) { invalidPaths.Add(path); invalid.Add(new { path, geometryType = geometry.GetType().FullName, transform = TransformValue(geometry.Transform), rejectedInScalarOrder = rejected,
                    rawDoubleBitsInScalarOrder = values.Select(v => BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture)).ToArray(), owners, ancestry, shape = ShapeValue(geometry) }); }
                object[] next = [.. ancestry, node];
                if (geometry is GeometryGroup group)
                    for (int i = 0; i < group.Children.Count; i++) GeometryNode(group.Children[i], path + "/geometry-child[" + i + "]", owners, next);
                else if (geometry is CombinedGeometry combined)
                {
                    GeometryNode(combined.Geometry1, path + "/operand1", owners, next);
                    GeometryNode(combined.Geometry2, path + "/operand2", owners, next);
                }
            }
            catch (Exception e) { captureErrors.Add(new { path, error = e.ToString() }); }
            finally { activeGeometries.Remove(geometry); }
        }
        void DrawingNode(Drawing? drawing, string path, object[] owners, object[] ancestry)
        {
            if (drawing == null || !activeDrawings.Add(drawing)) return;
            try
            {
                if (++drawingCount > 100000) throw new InvalidDataException("Diagnostic drawing traversal bound exceeded.");
                object node = new { path, type = drawing.GetType().FullName, transform = drawing is DrawingGroup g ? TransformValue(g.Transform) : null };
                object[] next = [.. ancestry, node];
                if (drawing is GeometryDrawing geometry)
                {
                    GeometryNode(geometry.Geometry, path + "/geometry", owners, next);
                    if (geometry.Brush is DrawingBrush brush) DrawingNode(brush.Drawing, path + "/drawing-brush", owners, next);
                }
                else if (drawing is DrawingGroup group)
                {
                    GeometryNode(group.ClipGeometry, path + "/drawing-clip", owners, next);
                    for (int i = 0; i < group.Children.Count; i++) DrawingNode(group.Children[i], path + "/drawing-child[" + i + "]", owners, next);
                }
                else if (drawing is ImageDrawing image && image.ImageSource is DrawingImage source)
                    DrawingNode(source.Drawing, path + "/drawing-image", owners, next);
            }
            catch (Exception e) { captureErrors.Add(new { path, error = e.ToString() }); }
            finally { activeDrawings.Remove(drawing); }
        }
        void VisualNode(Visual visual, string path, object[] ancestry)
        {
            try
            {
                if (++visualCount > 100000) throw new InvalidDataException("Diagnostic visual traversal bound exceeded.");
                var element = visual as FrameworkElement;
                if (visual is System.Windows.Shapes.Path sourcePath) pathOwners.Add(path, sourcePath);
                object owner = new { path, type = visual.GetType().FullName, name = element?.Name, dataContextType = element?.DataContext?.GetType().FullName,
                    transform = TransformValue(VisualTreeHelper.GetTransform(visual)), offset = new { x = Number(VisualTreeHelper.GetOffset(visual).X), y = Number(VisualTreeHelper.GetOffset(visual).Y) },
                    sourcePathGeometryType = visual is System.Windows.Shapes.Path shape ? shape.Data?.GetType().FullName : null };
                object[] owners = [.. ancestry, owner];
                GeometryNode(VisualTreeHelper.GetClip(visual), path + "/visual-clip", owners, []);
                DrawingNode(VisualTreeHelper.GetDrawing(visual), path + "/retained-drawing", owners, []);
                int count = VisualTreeHelper.GetChildrenCount(visual);
                for (int i = 0; i < count; i++) if (VisualTreeHelper.GetChild(visual, i) is Visual child) VisualNode(child, path + "/visual-child[" + i + "]", owners);
            }
            catch (Exception e) { captureErrors.Add(new { path, error = e.ToString() }); }
        }
        try { if (window != null) VisualNode(window, "window", []); else if (view != null) VisualNode(view, "view", []); }
        catch (Exception e) { captureErrors.Add(new { path = "capture-root", error = e.ToString() }); }
        File.WriteAllText(result + ".geometry-matrices.json", JsonSerializer.Serialize(new { phase, firstFault = fault.ToString(), visualCount, drawingCount, geometryCount, invalid, captureErrors,
            mechanism = "Public retained drawing/clip/visual traversal before Shutdown; matrices persisted before optional source-bound observation; no GetAsPathGeometry, renderer mutation or failure suppression." }, new JsonSerializerOptions { WriteIndented = true }));
        object? SourceGeometry(Geometry? geometry, int depth)
        {
            if (geometry == null) return null;
            if (depth > 32) return new { truncated = "Source geometry nesting exceeds32." };
            return new { type = geometry.GetType().FullName, transform = TransformValue(geometry.Transform), shape = ShapeValue(geometry),
                children = geometry is GeometryGroup group ? group.Children.Select(g => SourceGeometry(g, depth + 1)).ToArray() :
                    geometry is CombinedGeometry combined ? new[] { SourceGeometry(combined.Geometry1, depth + 1), SourceGeometry(combined.Geometry2, depth + 1) } : null };
        }
        var sourceOwners = new List<object>();
        foreach (var pair in pathOwners.Where(p => invalidPaths.Any(i => i.StartsWith(p.Key + "/", StringComparison.Ordinal))))
        {
            var source = pair.Value;
            object? sourceBounds = null;
            string? sourceBoundsError = null;
            try { if (source.Data != null) sourceBounds = RectValue(source.Data.Bounds); }
            catch (Exception e) { sourceBoundsError = e.ToString(); }
            try { sourceOwners.Add(new { path = pair.Key, ownerType = source.GetType().FullName, source.Name, stretch = source.Stretch.ToString(), width = Number(source.Width), height = Number(source.Height),
                actualWidth = Number(source.ActualWidth), actualHeight = Number(source.ActualHeight), desiredWidth = Number(source.DesiredSize.Width), desiredHeight = Number(source.DesiredSize.Height),
                renderWidth = Number(source.RenderSize.Width), renderHeight = Number(source.RenderSize.Height), geometryTransform = TransformValue(source.GeometryTransform), sourceGeometry = SourceGeometry(source.Data, 0), sourceBounds, sourceBoundsError }); }
            catch (Exception e) { captureErrors.Add(new { path = pair.Key + "/source-owner", error = e.ToString() }); }
        }
        File.WriteAllText(result + ".geometry.json", JsonSerializer.Serialize(new { phase, firstFault = fault.ToString(), visualCount, drawingCount, geometryCount, invalid, sourceOwners, captureErrors,
            mechanism = "Public retained drawing/clip/visual traversal before Shutdown. Source Path.Data.Bounds is observed only after matrix-only evidence is durable; no GetAsPathGeometry, renderer mutation or failure suppression." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}