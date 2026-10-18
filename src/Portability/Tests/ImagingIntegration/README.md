# Actual WPF imaging integration

The fixture uses source-built `PresentationCore` and `WindowsBase` with
`PORTABLE_WPF`. The public objects are real `BitmapSource`, `CachedBitmap` and
`WriteableBitmap`; drawing records real `DrawingVisual`/`RenderData` content.
Reflection constructs the existing internal renderer target. No production
test access or substitute public bitmap type is introduced.

## Implemented slice

`BitmapSource.Create` copies array/pointer data synchronously into owned
storage. `CopyPixels` preserves the original samples, source rectangles,
destination strides and array element offsets. Supported formats are Gray8,
Gray16, Rgb24, Bgr24, Bgr32, Bgra32, Pbgra32 and Rgb48. `CachedBitmap` source
copies currently require `OnLoad` with no conversion options.

`WriteableBitmap` exposes a stable pinned back buffer. The existing dispatcher
affinity and lock count govern API access; worker code may borrow the pointer
while the owner keeps the bitmap locked. A nested unlock does not publish.
The outermost dirty unlock copies the samples to committed front storage and
calls the actual `WritePostscript`. Existing Freezable ownership in RenderData
invalidates the retained scene and schedules a dispatcher render. Dirty areas
currently cause a complete front-buffer copy, which the public dirty-rectangle
contract permits. Clean lock cycles do not emit changes.

Clones own independent samples. Freezing a locked bitmap is rejected. Freezing
an unlocked mutable bitmap makes a private snapshot for subsequent reads and
display, allowing frozen cross-thread reads. This implementation does not
provide native operating-system page protection for a previously borrowed raw
pointer. Writing through that pointer outside its lock is outside the supported
API contract.

The retained renderer converts committed data to a temporary owned Skia bitmap
and disposes it after the draw. Straight and premultiplied alpha remain distinct.
The scientific source remains Gray16 or Rgb48; display conversion does not
implement `FormatConvertedBitmap` or change what `CopyPixels` returns.

## Precision and measured display gap

Scientific sample readback is exact, including both 16-bit endpoints. Rgb48
display uses each channel's high byte, matching the captured stock WPF result.
Gray16 display uses nearest full-range unsigned normalization,
`(sample + 128) / 257`. Its symmetric thresholds and endpoints are tested.
Stock WPF's WIC path differs by at most one output level for the captured
boundary samples. Gray16 display is not claimed to be bit-exact WIC parity.

The calibration inputs, stock results and baseline failure reports are retained
under `artifacts/portability/bitmap-quantization` and `bitmap-feasibility`.
The stock capture identifies the genuine installed WindowsDesktop assembly.
Before repair, the actual fork's Linux bitmap creation failed in
`MILFactory2.CreateFactory` or `MILSwDoubleBufferedBitmap.Create` because
`wpfgfx_cor3.dll` was unavailable. Native Windows creation succeeded.

## Verification and open gates

Checks cover real factory types, array and pointer caller reuse, padded rows,
ROI/element offsets, source and destination rectangles, buffer bounds,
overflow, nested locks, a worker pointer write, dirty publication, real retained
redraw, clean cycles, stable pointers across collection, clone/freeze independence,
dispatcher affinity, frozen cross-thread reads, alpha, channel order and retained
source lifetime. Both OS runs assert no loaded Windows interop assemblies or
WPF native module mappings and require process exit zero.

BitmapImage/decoder entry points and encoder Save report named unsupported
codec diagnostics. Palettes, other formats, codec metadata/color profiles,
native WIC handles, decoder frames, `BitmapFrame.Create(source)`, conversion
graphs, cropping, transforms, RenderTargetBitmap, image brushes and animated
image rectangles are separate gates. Unsupported source graphs may still contain
native methods in the full compiled closure; this slice does not qualify them.
Interpolation/scaling parity and color-managed/HDR display are not established.

Storage is GC-owned. Mutable images retain a pinned raw buffer plus committed
raw data, and a draw temporarily allocates a BGRA display bitmap. Large camera
frames therefore need a separate peak-memory/performance gate. There is no
native leak or interactive frame-rate claim.

## Run

Build from `src/Portability` with the configured SDK. Keep framework outputs
isolated from other integration fixtures.

```powershell
$env:NUGET_PACKAGES = 'E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME = 'E:\Projects\wpf\artifacts\portability\dotnet-home'
dotnet build Tests/ImagingIntegration/ImagingIntegration.csproj -m:1 -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/framework-image/ --artifacts-path E:\Projects\wpf\artifacts\portability\imaging-integration\windows
```

Run the resulting ImagingIntegration DLL from the repository root. Ubuntu uses
the workspace's Linux SDK, `linux-packages`, `linux-cli-home`, isolated
`framework-image-linux` outputs and the corresponding Linux artifact directory.
