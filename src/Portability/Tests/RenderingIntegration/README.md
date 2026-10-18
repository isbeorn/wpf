# Retained rendering integration

This executable uses the actual portable fork's `DrawingVisual.RenderOpen`, `UIElement` layout and `OnRender`, retained `RenderData`, `DrawingContextWalker`, visual tree and dispatcher `MediaContext`. The CPU target and walker are compiled inside the source-built `PresentationCore`, not linked into this fixture. Reflection accesses the internal target without adding a public WPF API for testing.

From `src/Portability`, build the complete reference graph and run:

```powershell
dotnet build Tests/RenderingIntegration/RenderingIntegration.csproj --artifacts-path ../../artifacts/portability/rendering-integration -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false
dotnet ../../artifacts/portability/rendering-integration/bin/RenderingIntegration/debug/RenderingIntegration.dll
```

For Linux, use the installed Linux SDK under `artifacts/portability/dotnet-linux-x64`, the Linux package cache and a separate artifacts directory. The fixture selects Arial from the Windows fonts directory or `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf` on Linux. A missing font fails the glyph check rather than silently skipping it.

The output is JSON and failures return exit code 1. Each check runs on an isolated dispatcher thread with a 15-second watchdog.

The behavior checklist covers:

- Actual recorded clipped and transformed rectangles with composite opacity.
- Rounded rectangle radii clamped independently on each axis, through both retained geometry and the direct drawing command.
- A real outlined Boolean union preserving the source geometry's retained pixels.
- Genuine rectangle-command and cubic-geometry content bounds before and after retained rendering, including transform mutation and corresponding changed pixels.
- Real `UIElement.Measure`/`Arrange`, retained `OnRender` pixels, brush invalidation and `InvalidateVisual`/`UpdateLayout` recording.
- Brush and transform mutation repainting without another `RenderOpen`, including restoring values.
- Content replacement removing subscriptions to the original resources, checked through actual dispatcher render postings.
- Visual child order, offset, clip, opacity, addition and removal.
- `DrawingGroup`, ellipse, pen and transformed `StreamGeometry` traversal, including zero-width strokes and overlapping opacity layers.
- Actual `GlyphRun` indices, advance widths, offsets and baseline placement, plus retained foreground brush mutation and physical DPI scaling.
- Surface growth, shrinking, expose invalidation, invalid dimensions, thread affinity and repeated close/reopen with pending work.
- Independent X/Y DPI changes, 1x/2x/1x round trips, physical surface bounds and preservation of the previous surface/scale after invalid inputs or failed allocation.
- Successful-frame notifications after canvas restoration, including callbacks that mutate content, reenter rendering, dispose the target or throw, plus absent notifications after failed frames or disposal.
- Explicit failure for an unsupported opacity mask and successful rendering afterward with canvas state restored.

This establishes a bounded retained CPU rendering slice. It does not establish `PresentationFramework` view rendering, native window presentation, text shaping coverage, native input, automatic monitor DPI detection, arbitrary brushes, image/video rendering, effects, dashed pens, right-to-left text or full WPF/NINA compatibility. Unsupported drawing operations throw a named `NotSupportedException`; they are not silently omitted.
