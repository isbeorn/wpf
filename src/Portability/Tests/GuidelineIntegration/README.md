# Portable retained guideline calibration

The fixture renders genuine WPF DrawingVisual records through the portable CPU
target. Its stock profile renders the same records with Microsoft's installed
WPF and RenderTargetBitmap. No product test friend or alternate WPF type is used.
The shared pinned DejaVu Sans font is copied from the existing TextUnicode source
dependency; this fixture adds no package dependency.

The checked-in StockGuidelineReference.json records the stock assembly identity,
location and SHA-256 plus sampled pixel rows and columns. It was captured with
SDK 10.0.401 and installed WPF 10.0.12. NativeCaretCapture.json records an actual
Linux native-window TextBox's CaretElement+CaretSubElement: static X guidelines
[-0.5, 0.5], no Y guides and visual-to-root translation (102, 60). The associated
native unsupported-guideline red remains in the native-window artifact ledger.

Covered behavior:

- Static nearest-guideline offsets in device coordinates, half ties toward the
  larger integer, negative device coordinates, negative scale and nearest-guide
  midpoint ties toward the lower guide.
- 1x, 2x and asymmetric fractional DPI, transforms before/after guideline push,
  null versus empty sets, nested Pop restoration and child visual isolation.
- Stable dynamic Y1/Y2 leading/shift pairs, widened strokes, cubic controls and
  retained quadratic/polyquadratic curves elevated to cubics before snapping.
- Device-Y-only glyph baseline correction without changing X placement or
  logical bounds, verified against a complete explicitly positioned raster.
- Color-gradient and two-dimensional checkerboard images whose snapped coverage
  preserves the original source sampling phase. Stock demonstrates that merely
  stretching an image into the snapped rectangle changes interior colors.
  The checkerboard also carries independent red/X and green/Y gradients; every
  interior BGRA sample must preserve its original phase, and deliberately
  stretched-X and stretched-Y controls must differ in their respective channels.
- Exact same-backend controls for snapped cubic paths, rotated empty frames,
  quadratic elevation, glyph baselines and single-guide conic translation. These controls compare the
  full bitmap, not just the stock samples.

Static snapping follows WpfGfx/core/common/guidelinecollection.cpp's
StoreRangeStatic, StoreRangeDynamic, SnapCoordinate and SnapTransform.
common/shared/real.h defines the float rounding rule. ShapeClipperForFEB.cpp
snaps transformed path points/control points; native strokes widen before this
stage. PathGeometryWrapper.cpp's SetQuadraticBezier degree-elevates unsnapped
local quadratic points before the resulting cubic controls are transformed and
snapped. baseglyphpainter.cpp applies guideline correction to the baseline's Y
coordinate only. drawingcontext.cpp distinguishes a null drawing guideline set
(preserve the active frame) from each visual's null frame (suppress its parent).
BoundsDrawingContextWalker deliberately leaves logical bounds unchanged.

Qualification limits: dynamic animation detection/landing history is not ported.
Multiple guidelines over rational conic paths raise a specific diagnostic because
Skia's conics and native WPF's cubic shape controls differ. Single-guide conic
translation is covered. Arbitrary curve pixel parity is not claimed. Stock and
Skia antialiasing differ on curves/rotation, so those stock samples allow 64/255
coverage variation while the explicit same-backend controls must match exactly.
Bitmap interpolation differs in fixed-point precision; the stock color-gradient
comparison allows 16/255 and the bilinear checkerboard allows 32/255, while the
preserved sampling-phase checks are exact. Integral polygon edge comparisons
allow only 3/255. Image sampling also observes the scoped mode contract below.

Run from E:/Projects/wpf/src/Portability:

```powershell
dotnet run --project Tests/GuidelineIntegration/GuidelineIntegration.csproj -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/framework-guidelines/ -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false -m:1 -nodeReuse:false
dotnet run --project Tests/GuidelineIntegration/StockOracle/StockOracle.csproj -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false -m:1 -nodeReuse:false
```

On WSL Ubuntu:

```bash
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
cd /mnt/e/Projects/wpf/src/Portability
/mnt/e/Projects/wpf/artifacts/portability/dotnet-linux-x64/dotnet run --project Tests/GuidelineIntegration/GuidelineIntegration.csproj -p:PortableFrameworkArtifactsDir=/mnt/e/Projects/wpf/artifacts/portability/framework-guidelines/ -p:NuGetAudit=false -m:1 -nodeReuse:false
```

The portable fixture exits nonzero on any failed comparison and has a 30-second
watchdog. --capture skips stock sample assertions for diagnosis; it still runs
the exact raster relationship checks and must not be used as acceptance evidence.


## Retained bitmap sampling

`BitmapSamplingCases.cs` exercises real UIElement OnRender, DrawingVisual content,
DrawingGroup recursion and the real dispatcher. The portable root defaults to
Linear. Unspecified preserves the active mode during the walk; this is render
state, not dependency-property inheritance. Linear/LowQuality are aliases.
NearestNeighbor selects the pinned Skia nearest sampler. Visuals and nested
DrawingGroups restore the previous state, including exception paths. Tests cover
ancestor/local override and clear in both directions, siblings, drawing groups,
guidelines, bitmap mutation and successful-frame callbacks. A persistent target
repaints those changes while the UIElement's OnRender count remains one.
HighQuality/Fant selects the pinned native trilinear mip sampler. Its source-copy
ownership and measured quality profile are described below.

The target owns one identity color filter for its lifetime, including resize.
Walkers borrow it and image shaders/paints remain draw-local. Target disposal and
constructor failure release it. This selects Skia's raster-stage Linear path
rather than its legacy four-bit interpolation path. It does not change the color
matrix or select cubic/mipmap filtering. The pinned implementation is SkiaSharp
3.119.4's Skia submodule
[7dbfc07d](https://github.com/mono/skia/tree/7dbfc07dd33181f84e0958afb7ee805c6c769f0b):
SkImageShader.cpp, SkColorFilterShader.cpp and SkBitmapProcState_opts.h.

The accepted profile documents backend differences rather than emulating Windows
software pixels. Every raster channel is checked. Each image has a genuine WPF
DrawGeometry(RectangleGeometry) white coverage control with the same transform,
clip and guidelines, rendered separately on each backend. Opaque image alpha must
match that control exactly. Premultiplied color comparisons use coverage
cross-products, with no division by low alpha. The edge inequality is abs(A*Cs-E*Cp) <= T*Cs*Cp+Cs+Cp: T is the case color bound and the final two terms account for covered-byte quantization. Exterior pixels must be exactly
transparent. Uniform sources with alpha 0, 1, 16, 128 and 255 independently check
pixels reached by only one backend's coverage. No edge pixels are discarded.
Each control also has independent geometric constraints: outward-rounded device
rectangle bounds, intersected with the transformed clip, identify guaranteed
fully covered interior cells and guaranteed transparent exterior cells. Static
guidelines enlarge the uncertain boundary by their maximum half-pixel movement.
These constraints inspect both controls separately and do not use image pixels
or observed coverage to classify cells. Corruption checks blank and shift paired
image/control rasters, punch interior holes and paint exterior pixels for Linear
and Nearest, on either backend and both together.

Measured Linear interior bounds for the small corpus are 1/255 for RGB48 and
2/255 for Gray16/Pbgra32. The 6000x4000 corpus additionally measures RGB48 fit/DPI
at 2/255 and guided reduction at 3/255 for all three formats. These limits are
explicit per case. The original 1-2/255 observations and fractional-DPI edge
maximum 17/255 were not universal bounds: the large fitted edge reaches 32/255
because backend coverage differs. That raw difference is reported and explained
by each backend's geometry control, not accepted as a global color tolerance.
Native bilinearspan.cpp uses 16.16 coefficients and eight-bit interpolation
weights, so its accumulation phase differs from Skia's floating-point sampler.

Nearest boundary candidates are computed before reading image pixels, from
outward-rounded single-precision axis arithmetic and native 16.16 origin/step
quantization. Each arithmetic operation is rounded outwards to adjacent float values. The native scan-step bound also includes float subtraction/division rounding and at most (viewportWidth+2)/131072 source pixels from 16.16 quantization. Any native clipped scan is bounded by the viewport width. A case
whose uncertainty admits more than adjacent source texels fails. Both stock and
portable pixels must match a permitted source texel with their own geometry
coverage. Fully covered RGB pixels away from those boundaries are exact; Gray16
retains its independently measured one-value display-conversion bound. There is
no 239/255 or 255/255 channel allowance. Large adjacent differences remain visible
in the raw results and must be explained by permitted source coordinates.

## Large sampling run

The ordinary fixture includes 141 checks, including the unchanged 44 guideline
checks. `--large-sampling` adds 51 cases with 6000x4000 Gray16, RGB48 and Pbgra32
sources and 1184x811 output. Inputs include independent 16-bit low bytes,
coordinate-coded high bytes and premultiplied alpha zones. Caller arrays are
cleared and complete CopyPixels results must retain the exact input bytes.
Views cover fitted reduction, positive/negative pan, distant coordinates,
reflection, asymmetric DPI, rectangle clipping and guidelines.

Capture stock first, then run portable Windows and Linux against that directory:

```text
dotnet <stock-output>/StockOracle.dll --large-sampling <evidence-directory>
dotnet <windows-output>/GuidelineIntegration.dll --large-sampling <evidence-directory>
<linux-dotnet>/dotnet <linux-output>/GuidelineIntegration.dll --large-sampling <evidence-directory>
```

The stock oracle does not compare itself against portable limits. It writes
`stock-*.bgra` and `stock-*.coverage`; portable runs write separate `windows-*`
and `linux-*` files. Large binary rasters stay in ignored artifacts. JSON records
source/raster hashes and full-raster statistics. Small stock color/coverage
rasters are checked in as StockBitmapSamplingReference.json. The opt-in large
run has a 240-second watchdog; ordinary runs retain the 30-second watchdog.
This qualifies the measured CPU sampling profile. It does not assert native
window presentation parity or arbitrary transformed-image parity.

## Native HighQuality/Fant

Numeric alias 2 selects Linear filtering with Linear mip-level interpolation.
Only the new private display bitmap is marked immutable and wrapped in a
draw-local SKImage, allowing Skia to share its pixels. The shader, image and
private bitmap are disposed in that order. Scientific source storage, caller
buffers, transforms, clips, guidelines and the target-owned precision filter keep
their existing responsibilities. No retained source/image cache is added.

This is native Skia minification, not a reproduction of Windows Fant pixels.
Pinned Skia's mip-level selection has a sharpening bias, so filtering around
0.625/0.7 is weaker than stock WPF; asymmetric shrinking can blur the less
reduced axis. SkiaSharp 3.119.4 exposes no explicit mip creation/status API.
Native mip acquisition can fall back to the base image. That backend limitation
is documented rather than reported as a managed allocation-success guarantee.

The original 157 portable/145 stock checks and raw evidence are preserved.
The expanded ordinary fixture has 141 checks and the large run 192; stock has 179.
The obsolete HQ-unsupported assertion now exercises real opacity-mask failure
and recovery. New retained checks cover HQ/Fant alias equality, ancestor/local
override and clear, siblings, nested groups, failed-group restoration and bitmap
mutation while OnRender remains recorded once.

HQ full-raster differences against stock are reported without a Windows pixel
tolerance. Every channel still receives extent, transparency and premultiplication
checks against independently validated geometry controls. Opaque grayscale
sources must retain equal RGB channels. Uniform alpha/color sources are checked
at every edge. A transparent/colored checker also preserves its source-derived
premultiplied color/alpha ratio within two byte quantizations, including guided,
clipped reflected edges; no division by low alpha is used.

Source-derived checker quality checks use a geometry-defined inner rectangle.
Mean must remain 126..129 and RMS contrast about 127.5 must not exceed these
case-specific limits: 0.375 reduction 5, 0.625 reduction 35, 0.7 reduction 44,
asymmetric 0.125x/0.75x 1, 24 MP fit 13 and 24 MP half 88. These are bounds for the
declared patterns, not general image tolerances. The 0.375 assertion explicitly
rejects an actual Linear fallback on the same source. At 1:1 HQ also satisfies
independent nearest-source texel checks; both 1:1 and magnification must match
the native Linear raster exactly. Gray16/RGB48 low bytes remain independent and
complete source CopyPixels comparisons remain exact after clearing caller data.
