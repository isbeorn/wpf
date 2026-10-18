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

Portable-only checks require named `NotSupportedException` diagnostics for dashed pens, different start/end caps and combined-geometry strokes. Other unsupported renderer geometry and unstroked segments in a stroked path retain their existing explicit diagnostics. This does not implement geometry-versus-geometry intersection, arbitrary boolean path widening or every WPF geometry API. Genuine mouse `LocalHitTest` and input routing are qualified separately by the input pipeline fixture.
