using System.Windows;
using System.Windows.Media;

internal static class CombineCases
{
    // Stock-captured contour bands keep both backends on the same sample points.
    private static readonly Dictionary<string, int[]> BoundaryExclusions = new()
    {
        ["cubic"] = [46, 101],
        ["touching"] = [],
        ["disjoint"] = [],
        ["transforms"] = [62, 101],
        ["rounded"] = [135],
        ["arc"] = [],
        ["stream"] = [65],
        ["fill-EvenOdd"] = [],
        ["fill-Nonzero"] = [],
    };

#if !PORTABLE_GEOMETRY_TESTS
    internal static object[] CaptureSamplingMasks()
    {
        var cases = new List<(string Name, Geometry A, Geometry B)>
        {
            ("cubic", Cubic(), new EllipseGeometry(new Point(15, 7), 9, 6)),
            ("touching", new RectangleGeometry(new Rect(0, 0, 20, 20)), new RectangleGeometry(new Rect(20, 0, 10, 20))),
            ("disjoint", new RectangleGeometry(new Rect(0, 0, 20, 20)), new RectangleGeometry(new Rect(40, 0, 10, 20))),
            ("transforms", new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(4, 6) },
                new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new MatrixTransform(1, 0, 0.5, 1, 10, 2) }),
            ("rounded", new RectangleGeometry(new Rect(0, 0, 30, 20), 4, 6), new EllipseGeometry(new Point(25, 10), 12, 8)),
            ("arc", new PathGeometry([new PathFigure(new Point(0, 10), [new ArcSegment(new Point(20, 10), new Size(10, 8), 0, false, SweepDirection.Clockwise, false)], true)]),
                new RectangleGeometry(new Rect(10, 0, 20, 20)))
        };
        var stream = new StreamGeometry { Transform = new TranslateTransform(3, 4) };
        using (StreamGeometryContext context = stream.Open())
        { context.BeginFigure(new Point(0, 0), true, true); context.QuadraticBezierTo(new Point(10, 20), new Point(20, 0), true, false); }
        cases.Add(("stream", stream, new RectangleGeometry(new Rect(10, 0, 20, 20))));
        foreach (FillRule rule in Enum.GetValues<FillRule>())
        {
            var group = new GeometryGroup { FillRule = rule };
            group.Children.Add(new RectangleGeometry(new Rect(0, 0, 30, 25)));
            group.Children.Add(new RectangleGeometry(new Rect(8, 6, 14, 12)));
            cases.Add(("fill-" + rule, group, new RectangleGeometry(new Rect(12, -2, 15, 30))));
        }
        return cases.Select(c =>
        {
            var excluded = new List<int>(); int index = 0;
            var pen = new Pen(Brushes.Black, 0.2);
            for (double y = -3.57; y < 35; y += 3.31)
            for (double x = -3.63; x < 55; x += 3.17, index++)
                if (c.A.StrokeContains(pen, new Point(x, y), 0.01, ToleranceType.Absolute) || c.B.StrokeContains(pen, new Point(x, y), 0.01, ToleranceType.Absolute)) excluded.Add(index);
            return (object)new { name = c.Name, sampleCount = index, excluded = excluded.ToArray() };
        }).ToArray();
    }
#endif

    internal static object[] CaptureContracts()
    {
        var records = new List<object>();
        Geometry a = new RectangleGeometry(new Rect(10, 20, 20, 20));
        Geometry b = new RectangleGeometry(new Rect(20, 20, 20, 20));
        foreach (GeometryCombineMode mode in Enum.GetValues<GeometryCombineMode>())
        {
            Capture($"rectangle-{mode}", () => Geometry.Combine(a, b, mode, null));
            Capture($"touching-{mode}", () => Geometry.Combine(a, new RectangleGeometry(new Rect(30, 20, 20, 20)), mode, null));
            Capture($"empty-left-{mode}", () => Geometry.Combine(Geometry.Empty, b, mode, null));
            Capture($"empty-right-{mode}", () => Geometry.Combine(a, Geometry.Empty, mode, null));
        }
        foreach (ToleranceType type in Enum.GetValues<ToleranceType>())
        foreach (double tolerance in new[] { double.NegativeInfinity, -1d, 0d, 1e-20, 0.01, 0.25, double.PositiveInfinity, double.NaN })
        {
            string suffix = $"{type}-{tolerance:R}";
            Capture("rectangle-tolerance-" + suffix, () => Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, tolerance, type));
            Capture("ellipse-tolerance-" + suffix, () => Geometry.Combine(new EllipseGeometry(new Point(20, 20), 16, 8),
                new RectangleGeometry(new Rect(20, 0, 30, 40)), GeometryCombineMode.Intersect, null, tolerance, type));
            Capture("cubic-tolerance-" + suffix, () => Geometry.Combine(Cubic(), new RectangleGeometry(new Rect(10, -5, 20, 30)),
                GeometryCombineMode.Intersect, null, tolerance, type));
        }
        Capture("result-transform", () => Geometry.Combine(a, b, GeometryCombineMode.Intersect, new MatrixTransform(2, 0, 0, 3, 100, 200)));
        Capture("singular-result-transform", () => Geometry.Combine(a, b, GeometryCombineMode.Union, new ScaleTransform(0, 1)));
        Capture("nan-result-transform", () => Geometry.Combine(a, b, GeometryCombineMode.Union, new MatrixTransform(double.NaN, 0, 0, 1, 0, 0)));
        Capture("infinite-result-transform", () => Geometry.Combine(a, b, GeometryCombineMode.Union, new MatrixTransform(double.PositiveInfinity, 0, 0, 1, 0, 0)));
        Capture("nan-rectangle", () => Geometry.Combine(new RectangleGeometry(new Rect(double.NaN, 0, 10, 10)), b, GeometryCombineMode.Union, null));
        Capture("infinite-path", () => Geometry.Combine(new PathGeometry([new PathFigure(new Point(double.PositiveInfinity, 0),
            [new LineSegment(new Point(20, 20), true)], true)]), b, GeometryCombineMode.Union, null));
        Capture("float-overflow-path", () => Geometry.Combine(new PathGeometry([new PathFigure(new Point(1e100, 0),
            [new LineSegment(new Point(20, 20), true)], true)]), b, GeometryCombineMode.Union, null));
        Capture("zero-extent-relative-infinite", () => Geometry.Combine(new LineGeometry(new Point(10, 10), new Point(10, 10)), Geometry.Empty,
            GeometryCombineMode.Union, null, double.PositiveInfinity, ToleranceType.Relative));
        Capture("empty-relative-infinite", () => Geometry.Combine(Geometry.Empty, Geometry.Empty, GeometryCombineMode.Union, null,
            double.PositiveInfinity, ToleranceType.Relative));
        Capture("transform-arithmetic-overflow", () => Geometry.Combine(a, b, GeometryCombineMode.Union, new ScaleTransform(1e38, 1e38)));
        Capture("null-left", () => Geometry.Combine(null!, b, GeometryCombineMode.Union, null));
        Capture("null-right", () => Geometry.Combine(a, null!, GeometryCombineMode.Union, null));
        Capture("invalid-mode", () => Geometry.Combine(a, b, (GeometryCombineMode)123, null));
        Capture("invalid-tolerance-type", () => Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, 0.01, (ToleranceType)123));
        return records.ToArray();

        void Capture(string name, Func<PathGeometry> action)
        {
            try
            {
                PathGeometry result = action();
                records.Add(new { name, status = "result", fillRule = result.FillRule.ToString(), figures = result.Figures.Count,
                    bounds = result.Bounds.ToString(System.Globalization.CultureInfo.InvariantCulture), transform = result.Transform?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    filled = result.Figures.All(f => f.IsFilled), closed = result.Figures.All(f => f.IsClosed),
                    samples = new[] { new Point(15, 25), new Point(25, 25), new Point(35, 25), new Point(25, 20), new Point(145, 275) }
                        .Select(p => result.FillContains(p, 0, ToleranceType.Absolute)).ToArray() });
            }
            catch (Exception exception) { records.Add(new { name, status = "exception", type = exception.GetType().FullName, message = exception.Message }); }
        }
    }

    internal static void Run(Action<string, Func<bool>, bool> check)
    {
        Geometry a = new RectangleGeometry(new Rect(0, 0, 20, 20));
        Geometry b = new RectangleGeometry(new Rect(10, 0, 20, 20));
        bool[][] expected = [[true, true, true], [false, true, false], [true, false, true], [true, false, false]];
        Rect[] expectedBounds = [new Rect(0, 0, 30, 20), new Rect(10, 0, 10, 20), new Rect(0, 0, 30, 20), new Rect(0, 0, 10, 20)];
        foreach (GeometryCombineMode mode in Enum.GetValues<GeometryCombineMode>())
        {
            check($"combine-overlap-{mode}", () =>
            {
                PathGeometry result = Geometry.Combine(a, b, mode, null);
                return Close(result.Bounds, expectedBounds[(int)mode], 0.001) && new[] { new Point(5, 10), new Point(15, 10), new Point(25, 10) }
                    .Select(p => result.FillContains(p, 0, ToleranceType.Absolute)).SequenceEqual(expected[(int)mode]) &&
                    !result.FillContains(new Point(40, 10), 0, ToleranceType.Absolute);
            }, true);
            check($"combine-empty-both-sides-{mode}", () =>
            {
                PathGeometry left = Geometry.Combine(Geometry.Empty, a, mode, null);
                PathGeometry right = Geometry.Combine(a, Geometry.Empty, mode, null);
                bool leftFilled = mode is GeometryCombineMode.Union or GeometryCombineMode.Xor;
                bool rightFilled = mode != GeometryCombineMode.Intersect;
                return left.FillContains(new Point(5, 5), 0, ToleranceType.Absolute) == leftFilled &&
                    right.FillContains(new Point(5, 5), 0, ToleranceType.Absolute) == rightFilled &&
                    Geometry.Combine(Geometry.Empty, Geometry.Empty, mode, null).Figures.Count == 0;
            }, true);
            check($"combine-reversed-and-identical-{mode}", () =>
            {
                PathGeometry reverse = Geometry.Combine(b, a, mode, null);
                bool[] reverseExpected = mode == GeometryCombineMode.Exclude ? [false, false, true] : expected[(int)mode];
                return Close(reverse.Bounds, mode == GeometryCombineMode.Exclude ? new Rect(20, 0, 10, 20) : expectedBounds[(int)mode], 0.001) && new[] { new Point(5, 10), new Point(15, 10), new Point(25, 10) }.Select(p => reverse.FillContains(p, 0, ToleranceType.Absolute)).SequenceEqual(reverseExpected) &&
                    Geometry.Combine(a, a, mode, null).FillContains(new Point(5, 5), 0, ToleranceType.Absolute) == (mode is GeometryCombineMode.Union or GeometryCombineMode.Intersect);
            }, true);
            check($"combine-curve-regions-{mode}", () => Regions(Cubic(), new EllipseGeometry(new Point(15, 7), 9, 6), mode, "cubic"), true);
            check($"combine-touching-and-disjoint-{mode}", () =>
                Regions(a, new RectangleGeometry(new Rect(20, 0, 10, 20)), mode, "touching") && Regions(a, new RectangleGeometry(new Rect(40, 0, 10, 20)), mode, "disjoint"), true);
        }
        check("combine-result-transform-before-boolean", () =>
        {
            var transform = new MatrixTransform(2, 0, 0, 3, 100, 200);
            PathGeometry result = Geometry.Combine(a, b, GeometryCombineMode.Intersect, transform);
            return Close(result.Bounds, new Rect(120, 200, 20, 60), 0.001) && result.FillContains(new Point(130, 230), 0, ToleranceType.Absolute) &&
                (result.Transform == null || result.Transform.Value.IsIdentity) && transform.Value == new Matrix(2, 0, 0, 3, 100, 200);
        }, true);
        check("combine-input-transforms-and-source-preservation", () =>
        {
            var first = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new TranslateTransform(4, 6) };
            var second = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new MatrixTransform(1, 0, 0.5, 1, 10, 2) };
            first.Freeze(); second.Freeze();
            return Regions(first, second, GeometryCombineMode.Intersect, "transforms") && first.Rect == new Rect(0, 0, 20, 20) && first.Transform.Value.OffsetX == 4 && second.Transform.Value.M21 == 0.5;
        }, true);
        check("combine-result-reflection", () => Close(Geometry.Combine(a, b, GeometryCombineMode.Exclude, new ScaleTransform(-2, 3)).Bounds,
            new Rect(-20, 0, 20, 60), 0.001), true);
        check("combine-hollow-and-stroke-flags", () =>
        {
            var filled = new PathFigure(new Point(0, 0), [new LineSegment(new Point(20, 0), false), new LineSegment(new Point(20, 20), true),
                new LineSegment(new Point(0, 20), false)], false);
            var hollow = new PathFigure(new Point(100, 100), [new LineSegment(new Point(120, 100), true), new LineSegment(new Point(120, 120), true)], false) { IsFilled = false };
            var path = new PathGeometry([filled, hollow]);
            PathGeometry result = Geometry.Combine(path, new RectangleGeometry(new Rect(5, 5, 10, 10)), GeometryCombineMode.Intersect, null);
            return Close(result.Bounds, new Rect(5, 5, 10, 10), 0.001) && result.FillContains(new Point(10, 10), 0, ToleranceType.Absolute) &&
                !result.FillContains(new Point(110, 110), 0, ToleranceType.Absolute) && !filled.IsClosed && !((LineSegment)filled.Segments[0]).IsStroked;
        }, true);
        foreach (FillRule rule in Enum.GetValues<FillRule>())
        foreach (GeometryCombineMode mode in Enum.GetValues<GeometryCombineMode>())
        {
            var group = new GeometryGroup { FillRule = rule };
            group.Children.Add(new RectangleGeometry(new Rect(0, 0, 30, 25)));
            group.Children.Add(new RectangleGeometry(new Rect(8, 6, 14, 12)));
            check($"combine-fill-rule-{rule}-{mode}", () => Regions(group, new RectangleGeometry(new Rect(12, -2, 15, 30)), mode, "fill-" + rule), true);
        }
        var stream = new StreamGeometry { Transform = new TranslateTransform(3, 4) };
        using (StreamGeometryContext context = stream.Open())
        {
            context.BeginFigure(new Point(0, 0), true, true);
            context.QuadraticBezierTo(new Point(10, 20), new Point(20, 0), true, false);
        }
        check("combine-stream-quadratic", () => Regions(stream, b, GeometryCombineMode.Xor, "stream"), true);
        check("combine-rounded-and-ellipse", () => Regions(new RectangleGeometry(new Rect(0, 0, 30, 20), 4, 6),
            new EllipseGeometry(new Point(25, 10), 12, 8), GeometryCombineMode.Union, "rounded"), true);
        var arc = new PathGeometry([new PathFigure(new Point(0, 10), [new ArcSegment(new Point(20, 10), new Size(10, 8), 0, false, SweepDirection.Clockwise, false)], true)]);
        check("combine-arc", () => Regions(arc, b, GeometryCombineMode.Exclude, "arc"), true);
        RunContracts(check);
    }

    private static void RunContracts(Action<string, Func<bool>, bool> check)
    {
        var a = new RectangleGeometry(new Rect(0, 0, 20, 20));
        var b = new RectangleGeometry(new Rect(10, 0, 20, 20));
        check("combine-absolute-tolerance-in-result-space", () =>
        {
            PathGeometry result = Geometry.Combine(new EllipseGeometry(new Point(20, 20), 16, 8), new RectangleGeometry(new Rect(0, 0, 50, 40)),
                GeometryCombineMode.Intersect, new ScaleTransform(100, 100), 0.5, ToleranceType.Absolute);
            double angle = Math.PI / 5;
            return result.FillContains(new Point((20 + 16 * 0.999 * Math.Cos(angle)) * 100, (20 + 8 * 0.999 * Math.Sin(angle)) * 100), 0, ToleranceType.Absolute);
        }, true);
        check("combine-touching-and-disjoint-intersections-are-empty", () =>
        {
            foreach (double x in new[] { 20d, 40d })
            {
                var other = new RectangleGeometry(new Rect(x, 0, 10, 20));
                if (Geometry.Combine(a, other, GeometryCombineMode.Intersect, null).Figures.Count != 0 ||
                    Geometry.Combine(other, a, GeometryCombineMode.Intersect, null).Figures.Count != 0) return false;
            }
            return true;
        }, true);
        check("combine-polynomial-curves-remain-curved", () => Geometry.Combine(Cubic(), Geometry.Empty, GeometryCombineMode.Union, null)
            .Figures.SelectMany(f => f.Segments).Any(s => s is BezierSegment or QuadraticBezierSegment), true);        check("combine-valid-result-metadata", () => Enum.GetValues<GeometryCombineMode>().All(mode =>
        {
            PathGeometry result = Geometry.Combine(a, b, mode, null);
            return result.FillRule == FillRule.Nonzero && result.Transform == null && result.Figures.All(f => f.IsFilled && f.IsClosed && f.Segments.All(s => s.IsStroked));
        }), true);
        check("combine-empty-result-metadata", () =>
        {
            PathGeometry result = Geometry.Combine(a, a, GeometryCombineMode.Exclude, null);
            return result.Figures.Count == 0 && result.FillRule == FillRule.Nonzero && result.Transform == null;
        }, true);
        check("combine-singular-result-transform", () =>
        {
            PathGeometry result = Geometry.Combine(a, b, GeometryCombineMode.Union, new ScaleTransform(0, 1));
            return result.Figures.Count == 0 && result.FillRule == FillRule.Nonzero && result.Transform == null;
        }, true);
        foreach (ToleranceType type in Enum.GetValues<ToleranceType>())
        foreach (double tolerance in new[] { double.NegativeInfinity, -1d, 0d, 1e-20, 0.01, 0.25, double.PositiveInfinity, double.NaN })
        {
            check($"combine-tolerance-{type}-{tolerance:R}", () =>
            {
                PathGeometry rectangle = Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, tolerance, type);
                PathGeometry cubic = Geometry.Combine(Cubic(), b, GeometryCombineMode.Intersect, null, tolerance, type);
                #if PORTABLE_GEOMETRY_TESTS
                bool cubicInterior = true;
#else
                bool cubicInterior = !double.IsPositiveInfinity(tolerance);
#endif
                return Close(rectangle.Bounds, new Rect(10, 0, 10, 20), 0.001) && cubic.FillContains(new Point(15, 5), 0, ToleranceType.Absolute) == cubicInterior &&
                    !cubic.FillContains(new Point(15, 30), 0, ToleranceType.Absolute);
            }, true);
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e100 })
        {
            check($"combine-nonfinite-source-{invalid:R}", () =>
            {
                var geometry = new PathGeometry([new PathFigure(new Point(invalid, 0), [new LineSegment(new Point(20, 20), true)], true)]);
                return BadNumber(Geometry.Combine(geometry, b, GeometryCombineMode.Union, null)) && BadNumber(Geometry.Combine(b, geometry, GeometryCombineMode.Union, null));
            }, true);
            check($"combine-nonfinite-input-transform-{invalid:R}", () =>
            {
                var transformed = new RectangleGeometry(new Rect(0, 0, 20, 20)) { Transform = new MatrixTransform(invalid, 0, 0, 1, 0, 0) };
                return BadNumber(Geometry.Combine(transformed, b, GeometryCombineMode.Union, null)) && BadNumber(Geometry.Combine(b, transformed, GeometryCombineMode.Union, null));
            }, true);
            check($"combine-nonfinite-result-transform-{invalid:R}", () => BadNumber(Geometry.Combine(a, b, GeometryCombineMode.Union,
                new MatrixTransform(invalid, 0, 0, 1, 0, 0))), true);
        }
        check("combine-degenerate-relative-infinite-tolerance", () =>
        {
            foreach (Geometry geometry in new Geometry[] { Geometry.Empty, new LineGeometry(new Point(10, 10), new Point(10, 10)) })
            {
                PathGeometry result = Geometry.Combine(geometry, Geometry.Empty, GeometryCombineMode.Union, null, double.PositiveInfinity, ToleranceType.Relative);
                if (result.Figures.Count != 0 || result.FillRule != FillRule.Nonzero || result.Transform != null) return false;
            }
            return true;
        }, true);
        check("combine-result-transform-arithmetic-overflow", () => BadNumber(Geometry.Combine(a, b, GeometryCombineMode.Union, new ScaleTransform(1e38, 1e38))), true);
        check("combine-invalid-tolerance-type-uses-absolute", () => Close(Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, 0.01, (ToleranceType)123).Bounds,
            new Rect(10, 0, 10, 20), 0.001), true);
        check("combine-null-operands", () => Throws<NullReferenceException>(() => Geometry.Combine(null!, b, GeometryCombineMode.Union, null)) &&
            Throws<NullReferenceException>(() => Geometry.Combine(a, null!, GeometryCombineMode.Union, null)), true);
#if PORTABLE_GEOMETRY_TESTS
        check("combine-relative-tolerance-includes-hollow-bounds-both-operands", () =>
        {
            var ellipse = new EllipseGeometry(new Point(20, 20), 16, 8);
            var rectangle = new RectangleGeometry(new Rect(20, 0, 30, 40));
            var hollow = new PathGeometry([new PathFigure(new Point(100000000, 0), [], false) { IsFilled = false }]);
            var first = new GeometryGroup { FillRule = FillRule.Nonzero }; first.Children.Add(ellipse); first.Children.Add(hollow);
            var second = new GeometryGroup { FillRule = FillRule.Nonzero }; second.Children.Add(rectangle); second.Children.Add(hollow);
            PathGeometry fromFirst = Geometry.Combine(first, rectangle, GeometryCombineMode.Intersect, null, 1e-20, ToleranceType.Relative);
            PathGeometry fromSecond = Geometry.Combine(ellipse, second, GeometryCombineMode.Intersect, null, 1e-20, ToleranceType.Relative);
            return fromFirst.FillContains(new Point(25, 20), 0, ToleranceType.Absolute) && fromSecond.FillContains(new Point(25, 20), 0, ToleranceType.Absolute) &&
                !fromFirst.FillContains(new Point(100000000, 0), 0, ToleranceType.Absolute) && !fromSecond.FillContains(new Point(100000000, 0), 0, ToleranceType.Absolute);
        }, true);
        check("combine-empty-operand-does-not-expand-relative-extent", () => Throws<NotSupportedException>(() => Geometry.Combine(
            new EllipseGeometry(new Point(100000000, 100000000), 16, 8), Geometry.Empty, GeometryCombineMode.Union, null, 1e-5, ToleranceType.Relative), "tolerance"), true);
        check("combine-invalid-mode-diagnostic", () => Throws<NotSupportedException>(() => Geometry.Combine(a, b, (GeometryCombineMode)123, null), "123"), true);
        check("combine-unsupported-operand-diagnostic", () => Throws<NotSupportedException>(() => Geometry.Combine(new CombinedGeometry(GeometryCombineMode.Union, a, b), b,
            GeometryCombineMode.Union, null), "CombinedGeometry"), true);
        foreach (ToleranceType type in Enum.GetValues<ToleranceType>())
        foreach (double tolerance in new[] { double.NegativeInfinity, -1d, 0d, 1e-20, double.NaN })
            check($"combine-conic-precision-diagnostic-{type}-{tolerance:R}", () => Throws<NotSupportedException>(() => Geometry.Combine(
                new EllipseGeometry(new Point(20, 20), 16, 8), b, GeometryCombineMode.Intersect, null, tolerance, type), "tolerance"), true);
#endif
    }

    private static bool BadNumber(PathGeometry result) => result.Figures.Count == 0 && result.FillRule == FillRule.EvenOdd && result.Transform?.Value.IsIdentity == true;
    private static bool Throws<T>(Action action, string diagnostic = "") where T : Exception
    {
        try { action(); return false; }
        catch (T exception) { return exception.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase); }
    }
    private static PathGeometry Cubic() => new([new PathFigure(new Point(0, 0),
        [new BezierSegment(new Point(0, 20), new Point(20, 20), new Point(20, 0), true)], true)]);

    private static bool Regions(Geometry a, Geometry b, GeometryCombineMode mode, string sampling)
    {
        PathGeometry result = Geometry.Combine(a, b, mode, null, 0.001, ToleranceType.Absolute);
        int index = 0;
        for (double y = -3.57; y < 35; y += 3.31)
        for (double x = -3.63; x < 55; x += 3.17, index++)
        {
            if (BoundaryExclusions.TryGetValue(sampling, out int[]? excluded) && excluded.Contains(index)) continue;
            Point point = new(x, y);
            bool first = a.FillContains(point, 0, ToleranceType.Absolute), second = b.FillContains(point, 0, ToleranceType.Absolute);
            bool expected = mode switch { GeometryCombineMode.Union => first || second, GeometryCombineMode.Intersect => first && second,
                GeometryCombineMode.Xor => first != second, _ => first && !second };
            bool actual = result.FillContains(point, 0, ToleranceType.Absolute);
            if (actual != expected) throw new InvalidOperationException($"{mode} mismatch at {point}: {actual}, expected {expected}. Result: {result.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return true;
    }

    private static bool Close(Rect actual, Rect expected, double tolerance) => !actual.IsEmpty &&
        Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance && Math.Abs(actual.Height - expected.Height) <= tolerance;
}