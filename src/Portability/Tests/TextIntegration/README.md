# Actual WPF text integration

This fixture builds and exercises the real current-source `PresentationCore`
and `WindowsBase` assemblies with `PORTABLE_WPF`. It does not define substitute
WPF public types. The standalone `Probes/Text` component proof is separate.

The active path is:

`TextSource -> TextFormatter.Create -> PortableTextFormatter -> actual TextLine`
`-> actual GlyphTypeface/GlyphRun -> TextLine.Draw -> DrawingVisual/RenderData`
`-> portable retained drawing walker -> software pixels`

The framework closure and renderer are owned by their corresponding projects.
The fixture uses reflection to construct the renderer's existing internal
target, keeping test access out of the public WPF API.

## Verified behavior

The executable checks actual factory selection and assembly location, installed
font resolution, Unicode cmap formats 4/12, nominal horizontal advances,
multiple source runs, source spans, source offsets, trailing spaces, CRLF and
paragraph terminators, empty paragraphs, greedy word wrapping, overflow modes,
min/max widths, baseline/height, ink bounds and Display advance rounding.
Wrapping regressions cover exact fitting word boundaries, widths on both sides,
repeated spaces and both wrapping modes. CRLF split across differently styled
source runs preserves both consumed spans. Signed overhang checks compare
actual glyph ink with positive side margins, negative italic overhang and
trailing whitespace excluded from visible width.

Caret and selection checks use real `CharacterHit`, `GlyphRun.ClusterMap` and
`CaretStops`, including a combining sequence with multiple glyphs, a surrogate
pair, both navigation directions, backspace, distance round trips and boundary
clamping. Soft breaks return real zero-native-record `TextLineBreak` instances.
Clone/Dispose independence is checked while continuing the same source.

The drawing check disposes the originating line and formatter, forces
collection then verifies the retained real glyph and font remain usable. It
compares rendered pixel extents against measured glyph bounds and checks that
a mutable text brush causes an actual dispatcher render with changed pixels.
It writes `formatted-text.png` below the test artifact directory.

The JSON report records the actual loaded framework assembly, loaded
`DirectWriteForwarder`/manipulation assemblies and Linux `/proc/self/maps`
entries for Windows WPF modules. Compile-time metadata references remain in
the full source closure. Successful bounded active paths do not establish that
every assembly method or reflected type can execute without Windows natives.

## Supported initial contract and open gates

This is an initial horizontal, LTR physical-font contract. It supports left
alignment, ordinary space-delimited greedy wrapping and explicit source runs.
Each run may contain one strong Unicode script plus common/inherited
characters. Real font files are retained by their `GlyphTypeface`; per-operation
fonts and shaping buffers are disposed independently.

Named `NotSupportedException` diagnostics reject tabs, RTL scripts and bidi
controls, mixed-script run itemization, unsupported/missing glyph fallback,
formatting boundaries inside a grapheme, justification, custom line heights,
indentation, markers, paragraph decorations, effects, number substitution,
collapsing, optimal paragraph caches and native Line Services contexts. Font
style simulations, font collections/face indexes and non-file sources remain
unsupported. The current line limit is 32767 UTF-16 units. Only Unicode cmap
formats 4 and 12 are supported by `CharacterToGlyphMap`.

`GlyphTypeface`'s supported data includes file identity/stream, style, weight,
stretch, horizontal normalized line metrics, `GlyphCount`, Unicode character
mapping, nominal `AdvanceWidths` and supported glyph outlines. Localized font
metadata, version/embedding metadata, symbol classification, subsetting,
vertical advances and side-bearing access are unimplemented. They are not
qualified by these tests. The preserved full native implementation is still
compiled for other paths; this is not a full native dependency removal.

The portable font catalog scans existing local `.ttf`/`.otf` files. It does not
download or redistribute fonts. Arial/Segoe UI Symbol are used on Windows and
DejaVu Sans on Linux. This is not a same-font Windows WPF metric or raster parity
test. OpenType feature controls, language-dependent fallback, full UAX bidi and
line breaking, ligature editing policy, text decorations, IME, accessibility,
per-monitor DPI behavior and native window integration remain separate gates.

## Run from this checkout

Build from `src/Portability` so its SDK and source-build configuration apply.
Use isolated framework output because other integration projects may build
the same source concurrently.

```powershell
$env:NUGET_PACKAGES = 'E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME = 'E:\Projects\wpf\artifacts\portability\dotnet-home'
Set-Location E:\Projects\wpf\src\Portability
dotnet build Tests/TextIntegration/TextIntegration.csproj -m:1 -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/framework-text/ --artifacts-path E:\Projects\wpf\artifacts\portability\text-integration\windows
Set-Location E:\Projects\wpf
dotnet artifacts/portability/text-integration/windows/bin/TextIntegration/debug/TextIntegration.dll
```

```bash
export DOTNET_ROOT=/mnt/e/Projects/wpf/artifacts/portability/dotnet-linux-x64
export PATH="$DOTNET_ROOT:$PATH"
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
cd /mnt/e/Projects/wpf/src/Portability
dotnet build Tests/TextIntegration/TextIntegration.csproj -m:1 -p:PortableFrameworkArtifactsDir=/mnt/e/Projects/wpf/artifacts/portability/framework-text-linux/ --artifacts-path /mnt/e/Projects/wpf/artifacts/portability/text-integration/linux
cd /mnt/e/Projects/wpf
dotnet artifacts/portability/text-integration/linux/bin/TextIntegration/debug/TextIntegration.dll
```

The test process exits nonzero on any failed assertion or unexpected exception.
Unsupported-case checks verify rejection, not implemented feature support.
