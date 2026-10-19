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

The owned PNG `BitmapImage` profile supports eager OnLoad caller streams and
local application pack resources. The decoder/frame profile supports eager
Default/OnLoad caller streams and local application pack resources through actual
`PngBitmapDecoder`, `IconBitmapDecoder`, `BitmapFrame.Create` and
`ImageSourceConverter` paths. Other schemes and create/cache options fail
explicitly. Callers retain their streams; frames own decoded pixels.

PNG factories preserve raw profile samples and return Bgr32 for opaque RGB or
Bgra32 for RGBA/transparency. BitmapImage retains its existing sRGB/Bgra32
conversion. The supported ICO profile contains nonoverlapping PNG entries or
32-bit BI_RGB BITMAPINFOHEADER DIB entries with complete XOR pixels and AND masks.
All entries must decode before the frozen frame collection is published. ICO
frames and their owned frozen thumbnails are Bgra32 at 96 DPI. ICO enumeration
uses file directory order. This intentionally differs from WIC's undocumented
sorting: the original six-frame NINA ICO starts with 256 pixels here and 128 on
the calibrated Windows codec. No resolution-specific ordering is implemented.

Decoder/frame metadata, color-context objects, codec information and metadata
writing fail explicitly. Palettes, other codecs, native WIC handles,
`BitmapFrame.Create(source)`, conversion graphs, cropping, transforms,
image brushes and animated image rectangles remain separate
gates. Unsupported source graphs may contain native methods in the compiled
closure; this slice does not qualify them.
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

RenderTargetBitmap has a separate portable implementation using the retained raster target.
Its focused tests cover source-over rendering, Clear, four clone paths and retained
consumer updates. The combined decoder, RenderTargetBitmap and native icon family
requires its own current-source qualification before a full NINA launch claim.
