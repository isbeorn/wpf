# Actual geometry containment

These checks call the actual WPF `Geometry.FillContains` and `StrokeContains` APIs. The same source runs against stock Windows WPF and the source-built portable fork. This fixture contains no replacement geometry or hit-test implementation.

From `src/Portability`, calibrate stock Windows behavior:

```powershell
dotnet build Tests/GeometryIntegration/Reference/Reference.csproj --artifacts-path ../../artifacts/portability/geometry-reference -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false
dotnet ../../artifacts/portability/geometry-reference/bin/Reference/debug/Reference.dll
```

Build and run the actual portable source implementation:

```powershell
dotnet build Tests/GeometryIntegration/GeometryIntegration.csproj --artifacts-path ../../artifacts/portability/geometry-integration -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/framework-geometry-windows/ -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false -m:1
dotnet ../../artifacts/portability/geometry-integration/bin/GeometryIntegration/debug/GeometryIntegration.dll
```

Linux uses the installed Linux SDK and Linux package cache with separate fixture/framework output directories. The reference project runs only on Windows. Output is JSON and any failed assertion returns exit code 1. Reports include the loaded framework assembly identity and path.

The stock fixture calibrates inside/outside boundaries, near-edge tolerance, relative tolerance based on the largest transformed geometry extent, independently clamped rounded corners, ellipses, open filled figures, cubic paths, fill-rule holes, transforms, degenerate lines/rectangles/points and stroke widths/caps. A pen's brush does not affect its geometric stroke containment, and a geometry transform does not scale pen thickness.

The portable implementation reuses the actual renderer's geometry conversion. Fill containment includes an implicit closing segment for open filled contours. Near-boundary tolerance is represented by the widened contour, preserving measured default, absolute and relative behavior rather than substituting bounding-box hits. Its relative extent uses tight geometric bounds including unfilled figures, matching the native implementation's cached-bounds calculation without making those figures hittable. Invalid point or geometry coordinates are rejected before passing them to Skia. Stock native source remains unchanged in the normal Windows build.

The outline cases exercise genuine `GetOutlinedPathGeometry` Boolean cleanup, overlap merging, self-crossings, fill rules, hollow/degenerate contours, transformed polynomial curves and ellipse conics. Polynomial curves retain their order. Positive rational conics are subdivided in homogeneous coordinates until their control-hull/chord bound plus endpoint rounding fits the requested absolute or relative tolerance. Unrepresentable precision or subdivision limits produce a named `NotSupportedException`. Simplification occurs after this conversion so the returned WPF figures describe the cleaned topology. Bounds checks exercise real `Geometry`, `PathGeometry` and `StreamGeometry` getters, retaining valid zero-area rectangles and including unfilled figures.

Coincident glyph checks use actual `GlyphRun.BuildGeometry` and verify both the O counter and surrounding ink. The text integration fixture supplies the broader pinned-font stock oracle for eight LTR/RTL, Ideal/Display and DPI cases; the rendering fixture verifies outlined union pixels through actual retained replay.

Retained bounds checks query genuine `DrawingVisual` content before rendering and exercise public render bounds with pen widening between the geometry and world transforms. They cover hollow figures, null-brush pens, ordinary and coincident contours, open/closed caps, zero-width and tolerance-suppressed pens. The stock skipped-pen fallback includes the first point before the world transform. Its relative tolerance uses the native loose-bounds rule, including the zero-area shortcut. Source primitive arrays retain their line/cubic types, closed flags and unsupported stroked-gap diagnostics in the separate internal primitive-array overload.

`BoundsPrecisionReference.json` captures 48 cases against stock Microsoft WPF 10.0.12: tiny/normal/large curves, positive/negative origins, nonuniform scale/skew, thin/thick strokes and miter extensions, each with negative absolute and NaN relative tolerance. Stock clamps widening precision to a minimum of 1e-6. Portable widening keeps that clamp, then diagnoses a nonconstant curved stroke when its requested error is smaller than one local float coordinate/stroke-width ULP transformed by the world's Frobenius norm. Constant contours, collapsed world transforms and linear/miter paths retain their supported exact bounds. Ordinary default and 0.01 curved bounds remain covered.

This precision limit is explicit because the pinned Skia offset solver can omit curve extents at extreme resolution. Merely raising tolerance to the float representation floor did not make all 48 cases accurate: thin skewed strokes still produced smaller bounds. The implementation throws a named `NotSupportedException` in that unsupported range instead of silently relaxing tolerance. Representable cases compare with the stock snapshot within one output float ULP or 1e-6, whichever is larger. To recapture the stock snapshot deliberately, run the reference executable with `--capture-bounds-precision`; the portable fixture also checks the named diagnostics against the same case definitions.

Portable-only containment checks require named `NotSupportedException` diagnostics for dashed pens, different start/end caps and combined-geometry strokes. Other unsupported renderer geometry retains its existing explicit diagnostics. This does not implement geometry-versus-geometry intersection, arbitrary boolean path widening or every WPF geometry API. Genuine mouse `LocalHitTest` and input routing are qualified separately by the input pipeline fixture.

Mixed-segment cases reproduce OxyPlot 2.2.0's `CanvasRenderContext.DrawLineSegments`: one hollow `StreamGeometry` figure alternates stroked line pairs and unstroked connectors. The fixture exercises real `GetRenderBounds`, `StrokeContains`, retained drawing bounds, `Path`/`Canvas` measure-arrange and raster pixels. `PresentationFramework` is referenced only to exercise that actual layout path. Stock Windows uses an STA thread for its UI construction and renders with `RenderTargetBitmap`; the portable fixture reuses `PortableRenderTarget` through the same reflection approach as the rendering integration fixture.

The portable figure converter starts each new stroke run at the skipped source segment's endpoint. A closed mixed figure starts immediately after a gap so the original closing edge joins the first and last runs. First, middle, last, all-skipped and multiple-gap cases compare stroke hit grids with ordinary independent figures. Matching Flat, Round and Square outer/gap caps are calibrated against stock. Curve, polyline, poly-Bezier, poly-quadratic and arc cases cover skipped endpoints and drawn segments after gaps. Empty poly segments keep their neighboring join. Fill and source `Bounds` retain skipped segments and transforms.

Mixed strokes currently require equal `StartLineCap`, `EndLineCap` and `DashCap`. Bounds, containment and retained rendering reject other combinations with the same named gap-cap diagnostic. `Pen.DashCap` defaults to Square while `Shape.StrokeDashCap` defaults to Flat; OxyPlot's default `Path` therefore uses the supported all-Flat case. Existing no-gap pen behavior remains unchanged. The separate internal raw primitive-array bounds overload still rejects gap flags. Dashed pens, triangle caps and unequal outer caps retain their prior limits.

For newly supported mixed hollow figures, portable render bounds describe the native backend stroke extent. Stock Windows conservatively unions the origin and source figure start when widening gap-containing figures. `MixedBounds` records that stock-only expectation; portable bounds intentionally omit those extra points. An all-gap open portable figure consequently has the existing origin fallback without retaining its source start. The actual `Path`/`Canvas` layout check and retained pair pixels cover the application's required path without reproducing that conservative Windows quirk. This fixture does not establish that the unchanged OxyPlot assemblies or NINA plotting have passed; package replay is a separate qualification step.

## Boolean combination

The shared Combine cases exercise the actual Geometry.Combine API through all four modes, A-minus-B order, reversed/empty/equal/disjoint/touching shapes, bounds, input/result transforms, singular/reflected transforms, independent fill rules and holes, open/hollow/gap flags, StreamGeometry and polynomial/arc/conic curves. The stock-only --capture-combine-contracts mode records metadata and parameter boundaries. --capture-combine-sampling deliberately captures contour bands only in the stock reference. Its frozen index arrays are compiled into CombineCases for both backends: 228 sample points per scenario with zero to two excluded indices, not a backend-generated mask. Initial stock near-contour discrepancies remain in the ignored geometry-combine evidence.

Portable InternalCombine uses the existing geometry conversion and outline helpers. Operand transforms are already applied by conversion; the supplied result transform is then applied to both operands before their Boolean operation and tolerance extent calculation. Relative tolerance uses the largest extent of the union of transformed tight bounds, including hollow figures but not an empty operand's origin. The source 1e-12 relative/extent floor remains. Positive rational conics use the existing checked approximation. Existing tolerance/subdivision diagnostics remain explicit when the requested precision cannot be represented. Skia operations are simplified before winding conversion so touching XOR contours retain their holes. Successful results have Nonzero fill, closed filled contours and null Transform. Nonfinite coordinates/transforms, including arithmetic overflow, produce the ordinary empty EvenOdd geometry.

Skia's polynomial Boolean solver has no MIL flattening-tolerance parameter. Polynomial curves remain curved and use the backend's float precision. This is an explicit backend profile, not Windows topology/pixel parity at every requested tolerance or near a contour. In particular, stock infinite tolerance collapses the tested single-cubic filled contour to empty; the portable polynomial solver retains that contour. Rectangle results remain unchanged. Very tight conic tolerances that stock accepts can produce the existing named portable precision diagnostic. Invalid combination modes produce a named NotSupportedException containing the value, while stock reports a native scanner COMException. Null operands retain NullReferenceException and an unrecognized ToleranceType continues to mean Absolute. Existing unsupported input types, including a direct CombinedGeometry operand, retain their named conversion diagnostic.

The closest FrameworkElement regression is Gadget's unchanged actual Classic Popup native click, preserving origin (20,20), its original layout translation and ClipToBounds. This geometry slice does not add a Framework dependency or bypass clipping. Existing geometry raster cases and the retained rendering integration fixture cover Core drawing. Popup qualification is recorded separately against the frozen Core.

## Dashed render bounds

Bounds queries support finite noncollapsed linear contours for LineGeometry, zero-radius RectangleGeometry and fully stroked filled PathGeometry/StreamGeometry. They share painting's dash normalization once, widen the actual dashed stroke and union its extent with the existing geometry fill extent. Width-zero and tolerance-skipped strokes preserve their geometry fallback before creating the dash effect. Geometry transforms precede widening and world transforms follow it. Named dashed bounds diagnostics retain hollow, collapsed, mixed-gap, curved, group and singular-transform boundaries. Dashed StrokeContains and widened geometry remain outside this support.

DashedBoundsCases uses the unchanged stock WindowsDesktop 10.0.12 Reference project to capture DashedBoundsReference.json. It covers the genuine Classic ComboBox item focus pen: Rectangle (0.5,0.5,177,12.96875), width 1, Flat caps, [1,2], offset 0. Direct RectangleGeometry, explicit top-left PathGeometry and retained DrawRoundedRectangle return exactly (0,0,178,13.96875). The suite also checks positive/negative phase and period wrap, odd/even/negative and supported zero intervals, collection immutability, mutable retained queries, painting before/after bounds, tolerance and skip cases.

The retained world-skew stock vector is a documented backend difference, not a float tolerance. Native CDasher treats dash/edge endpoints within MIN_DASH_ARRAY_LENGTH (.1) as coincident. For matrix (2,.25,.5,3,7,-11), stock widens to (7.734375,-10.875,362,85.65625); Skia's preserved corner continuation widens to (7.71875,-10.875,362.265625,86.28125). The raw stock vector remains unchanged. Portable bounds must cover the outline actually painted: the independent control records solid ON-runs, including intermediate corner vertices, in one PathGeometry with no dash effect. Its complete raster and bounds match across skew, reflection and skew restoration, with occupied raster cells contained by the returned bounds and unchanged queries after rendering. An extra identity-plus-translation manual-control experiment had three alpha samples differing by at most 4, so exact manual-control identity raster equivalence is not claimed; the existing exact explicit-path axis-aligned focus controls remain in GuidelineIntegration. No broad bounds or raster tolerance is added.

Run `--capture-dashed-bounds` in Reference to regenerate the raw stock values. `--capture-dashed-bounds-ink` records the affine occupied-pixel box and both outline-bound and stock-bound containment counts. These offscreen checks do not qualify the genuine native ComboBox keyboard workflow; coherent native replay is separate.
REV2 also pairs retained DrawGeometry(RectangleGeometry) with the genuine WPF PathGeometry conversion and DrawRoundedRectangle. The sparse (10,10,20,40) rectangle with width 2, Flat caps and [1,100] has a single 2-DIP ON run: its public bounds are (10,9,20,41) and the run must start at top-left. Before the repair, DrawGeometry alone started at bottom-left and painted two pixels outside its own reported bounds. Only dashed rectangles with either radius zero now obtain their stroke path from RectangleGeometry.GetAsPathGeometry, preserving its transform. General conversion, solid strokes and truly rounded shapes are unchanged. Exact full raster, public/retained bounds before and after rendering and occupied-cell containment cover null/fill, +/- phase and wrap, geometry scaling/reflection and either radius zero.
The REV2 stock audit records 28 exact raster comparisons and 25 exact bounds comparisons. In the three offset +1 all-gap routes, both backends paint no ink, but stock reports (10,9,20,41) while portable reports the source extent (10,10,20,40). This native conservative extent was already present before the route repair. It is recorded as a measured preexisting difference, not counted as stock-exact and not hidden by a tolerance.

The bounded grid cases reuse the SHA-256-pinned original OxyPlot StreamGeometry
and the offscreen stock 10.0.12 calibration in GridBoundsReference.json. They
cover one open unfilled figure with at least two isolated noncollapsed straight
stroked runs, explicit linear gaps and matching Flat caps. Signed dash phases,
period/restoration, original extent, skipped-pen start, final-coordinate query
origin, leading/trailing gaps and geometry/world translation are compared to
stock. The 16 calibrated raster cases compare full BGRA bytes against separately
drawn directed rows and include a deliberately stroked-connector negative
control. Retained queries run before/after paint and same-object mutations.
Joined, fully stroked hollow, all-gap, closed, curved, grouped, collapsed and
non-Flat-cap neighbors remain rejected. Existing no-ink conservative bounds
behavior remains separately documented above; this slice does not widen that
qualification.

GridAffineReference.json records four disjoint diagonal/reflected/skewed stock
bounds cases. All four bounds and independent-row unions are checked, with
occupied raster cells intersecting the reported bounds. One pre-existing Skia
world-skew multi-contour raster differs from separate translucent draws by up to
15 channel levels (308 pixels); stock does not show that difference. The same
before/fixed raster hashes establish that this bounds-only change preserves it.
The `--capture-grid-affine` mode retains that paired evidence without weakening
the 16 exact-grid raster checks. Geometry.GetWidenedPathGeometry retains its
original native entrypoint and is unqualified here; dashed containment retains
its portable rejection. No NINA view, native window, package or interactive
workflow is qualified by this offscreen fixture.