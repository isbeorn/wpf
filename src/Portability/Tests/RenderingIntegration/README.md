# Retained rendering integration

This executable uses the actual portable fork's `DrawingVisual.RenderOpen`, retained `RenderData`, `DrawingContextWalker`, visual tree and dispatcher `MediaContext`. The CPU target and walker are compiled inside the source-built `PresentationCore`, not linked into this fixture. Reflection accesses the internal target without adding a public WPF API for testing.

From `src/Portability`, build the complete reference graph and run:

```powershell
dotnet build Tests/RenderingIntegration/RenderingIntegration.csproj --artifacts-path ../../artifacts/portability/rendering-integration -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false
dotnet ../../artifacts/portability/rendering-integration/bin/RenderingIntegration/debug/RenderingIntegration.dll
```

For Linux, use the installed Linux SDK under `artifacts/portability/dotnet-linux-x64`, the Linux package cache and a separate artifacts directory. The fixture selects Arial from the Windows fonts directory or `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf` on Linux. A missing font fails the glyph check rather than silently skipping it.

The output is JSON and failures return exit code 1. Each check runs on an isolated dispatcher thread with a 15-second watchdog.

The behavior checklist covers:

- Actual recorded clipped and transformed rectangles with composite opacity.
- Brush and transform mutation repainting without another `RenderOpen`, including restoring values.
- Content replacement removing subscriptions to the original resources, checked through actual dispatcher render postings.
- Visual child order, offset, clip, opacity, addition and removal.
- `DrawingGroup`, ellipse, pen and transformed `StreamGeometry` traversal, including zero-width strokes and overlapping opacity layers.
- Actual `GlyphRun` indices, advance widths, offsets and baseline placement, plus retained foreground brush mutation.
- Surface growth, shrinking, expose invalidation, invalid dimensions, thread affinity and repeated close/reopen with pending work.
- Explicit failure for an unsupported opacity mask and successful rendering afterward with canvas state restored.

This establishes a bounded retained CPU rendering slice. It does not establish `PresentationFramework` view rendering, native window presentation, text shaping coverage, native input, monitor scaling, arbitrary brushes, image/video rendering, effects, dashed pens, right-to-left text or full WPF/NINA compatibility. Unsupported drawing operations throw a named `NotSupportedException`; they are not silently omitted.
