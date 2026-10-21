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

The offline NINA sky map's `BitmapPalettes.Gray256` is a managed read-only list
of all 256 opaque `Color.FromRgb(i, i, i)` entries. Gray8 creation accepts its
palette argument and ignores it, matching stock non-indexed behavior:
`BitmapSource.Palette` remains null and samples, DPI, clones and display stay
unchanged. Other predefined palettes, custom native palette handles,
quantization and indexed pixel storage remain explicitly unsupported.

The PNG decoder also accepts the original docking guides' 8-bit indexed PNG
input. It expands PLTE/tRNS samples through the existing Skia decoder into owned
Bgr32 or Bgra32 storage, preserving density and alpha without exposing indexed
bitmap storage or palette objects. Tests cover the ten original pane guide
assets, opaque and partial alpha input, malformed palette/transparency chunks
and recovery. Indexed PNG depths 1, 2 and 4 remain outside this profile.
The existing Skia format selection returns Bgra32 when tRNS is present even if
every transparency entry is 255; the expanded samples remain fully opaque.

The focused checks cover worker-first palette access, every immutable color,
stable cache identity after initialization, the exact worker-created/frozen
NINA Gray8 image and an odd-width padded ramp through clones and real rendering.
Stock WindowsDesktop 10.0.12 returned different palette instances during a
concurrent first access. The original test incorrectly required those instances
to be identical; that expectation was corrected and existing cache publication
was left unchanged. Revised stock checks pass all three positive cases. Initial
and corrected stock/red evidence is retained under
`artifacts/portability/current/nina-gray-blur-frame-integration-20261021-1`.

`WriteableBitmap` exposes a stable pinned back buffer. The existing dispatcher
affinity and lock count govern API access; worker code may borrow the pointer
while the owner keeps the bitmap locked. A nested unlock does not publish.
The outermost dirty unlock copies the samples to committed front storage and
calls the actual `WritePostscript`. Existing Freezable ownership in RenderData
invalidates the retained scene and schedules a dispatcher render. Dirty areas
currently cause a complete front-buffer copy, which the public dirty-rectangle
contract permits. Clean lock cycles do not emit changes.

Bitmap clones own independent samples, except source-backed BitmapFrame views
and all four frame clone forms, which retain the original source storage.
Freezing a locked bitmap is rejected. Freezing
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
`ImageSourceConverter` paths. Other decoder schemes and create/cache options fail
explicitly. Callers retain their streams; frames own decoded pixels.

PNG factories preserve raw profile samples and return Bgr32 for opaque RGB or
Bgra32 for RGBA/transparency. BitmapImage retains its existing sRGB/Bgra32
conversion. PreservePixelFormat selects Bgr32 for opaque RGB without disabling
embedded profile conversion. The supported ICO profile contains nonoverlapping PNG entries or
32-bit BI_RGB BITMAPINFOHEADER DIB entries with complete XOR pixels and AND masks.
All entries must decode before the frozen frame collection is published. ICO
frames and their owned frozen thumbnails are Bgra32 at 96 DPI. ICO enumeration
uses file directory order. This intentionally differs from WIC's undocumented
sorting: the original six-frame NINA ICO starts with 256 pixels here and 128 on
the calibrated Windows codec. No resolution-specific ordering is implemented.

Decoder/frame metadata, color-context objects, codec information and metadata
writing fail explicitly. Palettes, codecs beyond PNG/JPEG/ICO, native WIC handles,
conversion graphs, cropping, transforms beyond axis scaling and mirrors,
image brushes and animated image rectangles remain separate
gates. Unsupported source graphs may contain native methods in the compiled
closure; this slice does not qualify them.
`BitmapFrame.Create(source)` preserves the actual source format, dimensions and
DPI in a genuine source-backed frame. The source is strongly retained. Freezing
the frame leaves the source mutable, and the frozen frame can read the installed
buffer on another thread after an explicit owner-to-worker handoff. Source
writes/restores and RTB Render/Clear remain visible through every frame clone.
No source Changed event forwarding or atomic concurrent-write snapshot is
promised. Separately frozen source rules remain unchanged.

The PNG encoder supports exactly one source-backed frame containing Bgra32,
Pbgra32 or Bgr32 samples and Default/Off interlace. The actual NINA plugin cache
and thumbnail paths both create these frames. Direct decoder-frame Save rejects
before writing because decoder metadata/color contexts are unsupported. Explicit
frame thumbnails, metadata and color contexts reject at creation. Encoder
palettes, global metadata, thumbnails, previews, other codecs and other pixel
formats remain unsupported. Gray16/Rgb48 frame views and raw clones preserve
all samples, but PNG Save does not quantize them to a display format.

PNG encoding stages owned bytes before output, tags default sRGB and writes a
CRC-protected pHYs chunk with truncated pixels per meter. At 96 DPI the stored
value is 3779 and decoded DPI is about 95.9866. Fractional and non-square DPI
are covered. Nonfinite DPI and positive values outside representable nonzero
pixels per meter reject; nonpositive finite DPI uses 96. Bgra32 straight samples,
hidden RGB under zero alpha, Bgr32 opaque samples and alpha values are exact.
Skia Pbgra32 unpremultiplication differs from calibrated WIC by at most one RGB
level (1/255); that measured backend difference is accepted without WIC rounding
emulation.

Save writes at the caller's current stream position, preserves an existing tail,
leaves the stream open and never flushes it. Successful Save is one-shot. Null,
disposed, nonseekable or read-only streams reject before writes and allow retry.
Read-only rejection is an explicit deviation from stock WIC's observed false
success without output. An attempted output write consumes the encoder even if
it fails after partial bytes; no rollback is implied. Unsupported options/formats
also reject before output and allow correction/retry.

Stock source-view, stream, alpha and DPI observations with source/runtime/PDB
pins are retained under `artifacts/portability/nina-png-frame-encoding/stock-calibration-1`.
The original red reports retain both the WIC source-handle failure and blanket
PNG Save guard. Direct decoded-frame Save is intentionally still unsupported;
the positive public Create(source)-to-Save pipeline now crosses both old guards.

## NINA sky-survey cache

BitmapImage also supports eager OnLoad absolute local file URIs, JPEG input,
PreservePixelFormat and DecodePixelWidth/Height. A single requested axis keeps
the integer aspect ratio with a minimum of one pixel; two axes set both sizes.
JPEG factory frames are Gray8 or Bgr32. PreservePixelFormat BitmapImage color
JPEGs use Bgr24. JFIF density is read in inches or centimeters, with a default
of 96 DPI. The existing XPS package-origin check runs before local file access.
Caller streams stay open and owned file streams close after the synchronous
decode. Remote URIs, delayed loading, rotation and other create options remain
unsupported.

The actual CacheSkySurvey.LoadJpg path also uses JpegBitmapDecoder with an
absolute local file URI, OnLoad and PreservePixelFormat. This typed decoder
and the BitmapDecoder factory support eager local JPEG files with None or
PreservePixelFormat. Both return the stock Gray8/Bgr32 raw decoder samples;
PreservePixelFormat does not change decoder-frame color conversion or format.
PNG/ICO decoder create options retain their prior restrictions. Remote/UNC
URIs and local files outside OnLoad remain unsupported. The ambient XPS
package check precedes file access and owned file streams close before return.

Typed/factory file tests create frozen frames on a worker, delete the file after
same-thread sample verification and render the frames through genuine DrawImage
and RenderTargetBitmap on the UI thread. Raw URI-frame CopyPixels on another
thread retains an observed stock limitation: URI serialization touches the
thread-affine decoder even when the frame is frozen. No BitmapSource security
or serialization path was changed to bypass that limitation.

The source-backed JPEG encoder supports the actual original and thumbnail cache
pipeline with QualityLevel 1 through 100, including NINA's quality 70. It writes
rounded integer JFIF density and uses 96 DPI for nonpositive finite source DPI.
Nonfinite or unrepresentable density rejects before output. Transform flags,
decoder metadata and nondefault global/frame metadata remain unsupported.
JPEG uses the same Save ownership, preflight retry and one-shot rules as PNG.
Gray16, Rgb48 and Gray32Float sources are converted only in owned output staging;
their source format and CopyPixels samples remain unchanged.

TransformedBitmap supports finite axis scales and mirrors over owned pixels.
Identity and pure mirrors copy samples exactly, including Gray16/Rgb48 low bits.
Sizes round to the nearest integer with a minimum of one pixel. Resizing Gray16
uses the IEC sRGB inverse transfer function before filtering and returns
Gray32Float; Rgb48 resizing returns Pbgra32, matching the stock result formats.
Gray32Float storage, copies and clones retain floating samples. Display and JPEG
staging apply the forward transfer function to those linear grayscale samples.

The selected filters are standard area coverage on shrinking axes and linear
interpolation at source coordinate `destination * sourceSize / destinationSize`
on growing axes, with clamped edges. Each axis chooses independently. Filtering
uses premultiplied color or linear floating grayscale, preserving transparent
edges and grayscale working precision. These filters intentionally differ from
Windows WIC Fant. No arbitrary pixel tolerance or WIC rounding emulation is used.
The user accepted this bounded filter difference for the cache on 2026-10-20.

The retained genuine WindowsDesktop 10.0.12 calibration compares a 5x3 ramp and
checker with complete pixels. The standard shrink differs from WIC by at most
9/255 for the ramp and 31/255 for the checker at 3x2, and 8/255 and 9/255 at 2x2.
The aligned 10x6 linear expansion matches both byte corpora exactly. Gray16
checker shrink differs by up to 0.122222215 in linear float and its ramp by
0.04790604 because the filters differ. Separately, the exact IEC transfer at
32768 is 0.2140482 while WIC records about 0.21405274. The captured uniform and
expansion transfer differences stay below 0.0000053 in linear float. These are
case-specific observations, not a universal error bound. JPEG remains lossy and
the codecs are not claimed to produce identical pixels or encoded bytes.

Stock source, complete observations and red reports are retained under
`artifacts/portability/current/nina-cache-codecs-20261020-1`. Integration checks
exercise actual 500x500, 150x150 and 75x75 cache thumbnails, both high-bit source
formats, exact mirror/restore, frozen file deletion, independent decode axes,
quality tables, float constant/area invariants and premultiplied alpha. General
color-managed/HDR display and other conversion graphs remain unqualified.

The embedded linear RGB PNG regression verifies independently known 64/128/192
linear samples become 137/188/225 sRGB samples with both ordinary BitmapImage
and PreservePixelFormat. Raw decoder samples remain unchanged. JPEG content
checks retain externally known red/green positions in the stock fixtures and
verify independent red/green/blue patches and dark/mid/light bands through
eight-bit and high-bit original/thumbnail encoding. Channel dominance and
luminance order catch black output or channel swaps without lossy tolerances.

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
