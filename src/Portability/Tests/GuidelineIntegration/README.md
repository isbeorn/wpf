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
allow only 3/255. Unguided image sampling retains the existing backend behavior.

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
