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
source runs preserves both consumed spans. Mandatory LS, PS, NEL, VT and FF
separators also preserve consumed spans across source chunks and empty lines.
Combining sequences, surrogate pairs and ligatures may cross source chunks
with equivalent formatting. Paint must share its brush object or use equal
frozen solid brushes so later independent brush changes cannot be lost.
Selection rectangles and `TextRunBounds` project merged glyphs back onto
original source chunks, including partial graphemes and wrapped absolute offsets.
Signed overhang checks compare
actual glyph ink with positive side margins, negative italic overhang and
trailing whitespace excluded from visible width.

Horizontal alignment supports Left, Center and Right for finite paragraph
widths. The offset uses visible line width, excluding trailing whitespace, and
may be negative when the line is wider than its paragraph. Ideal placement,
caret distances, selection bounds and retained drawing follow the stock WPF
relationships for both flow directions. Display rounds the alignment offset
once to a device pixel and applies that same offset throughout the line.
Stock Display may round individual coordinates differently and change an
overhang by up to one device pixel in the calibrated cases. This bounded
backend does not reproduce Line Services' per-coordinate pixel snapping.
Left shaping and indexed glyph data remain unchanged. Justify, collapsing
symbols and other unsupported paragraph features still fail explicitly.

`StockOracle --capture-alignment` records stock metrics, carets, bounds,
indexed glyph origins and retained drawings for both modes. Its
`--verify-alignment` checks Ideal relationships only. The portable test also
checks consistent Display placement at 1x, 1.5x and 2x DPI, zero/equal/narrow/
wide paragraph widths, wrapping, explicit newlines and trailing whitespace.
The Controls fixture uses the original two-line NINA target-area message in
a real TextBlock, checking Measure/Arrange/OnRender through Center -> Right
-> Left -> Center and exact restoration of retained pixels. This does not
qualify the full NINA sequencer workflow.

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

Public `FormattedText` construction, range font-size split/restore, brush range
formatting, metrics and `DrawingContext.DrawText` are also exercised through
retained pixels. Actual `SpanVector` construction and a public typography
subclass cover the two previously eager C++/CLI dependencies. The portable
span element preserves the reference semantics and two fields of
`DirectWriteForwarder/CPP/DWriteWrapper/ItemSpan.{h,cpp}`; the existing vector's
mutation and range algorithms are unchanged. This is a managed container port,
not a replacement for DirectWrite itemization.

The JSON report records the actual loaded framework assembly, loaded
`DirectWriteForwarder`/manipulation assemblies and Linux `/proc/self/maps`
entries for Windows WPF modules. Compile-time metadata references remain in
the full source closure. Successful bounded active paths do not establish that
every assembly method or reflected type can execute without Windows natives.
The exercised paths assert that no Windows interop assembly was loaded. A
successful run also requires process exit code zero, since JSON emitted before
shutdown alone does not establish that native module cleanup succeeded.

## Unicode 17 paragraph extension

The same WPF model now supports horizontal mixed Arabic, Hebrew, Latin and
digit paragraphs in both directions. Whole-paragraph Unicode 17 bidi analysis,
grapheme boundaries and legal line breaks precede script/direction/font
segmentation. HarfBuzz receives actual line context across formatting runs;
wrapping reshapes the final boundary. Real managed TextLineBreak objects retain
the paragraph through independent clones and reject disposed continuation.
Trailing whitespace levels reset before visual run reordering.

GlyphRuns keep logical cluster maps/source ranges while drawing uses visual
order. Carets include grapheme boundaries inside ligatures. Hit distances and
selections use the paragraph leading edge; RTL drawing uses its supplied right
edge. Multiple and reversed selection ranges retain direction-specific bounds.
Empty/end caret bounds preserve actual TextBoxLine's fallback contract.

The new cases use byte-verified, unmodified DejaVu Sans and Inter from the pinned
fixture directory ../Fixtures/Fonts, which also retains the full font licenses.
A known missing Inter grapheme must select the expected DejaVu file as a whole.
Retained RTL drawing is checked after formatter/line disposal and collection,
including Ideal/Display, asymmetric DPI and repeated native shaping lifetimes.

The 48 identified official rows in BidiCharacterCases.txt exercise actual WPF
paragraph levels. Verify-UnicodeBidi.ps1 independently checks all 91,707 official
Unicode 17 rows and input preservation for the directly maintained sources,
with the original and exactly two guarded N0 corrections as historical controls. This full-corpus probe qualifies levels, not rendering
or visual order. Original source bytes and per-file licenses remain preserved.

StockOracle runs the same fonts against actual Windows WPF. Eight explicit
GlyphRun geometry cases in StockPositionReference.json cover LTR/RTL,
Ideal/Display, two DPI values, a zero-advance mark and nonzero offsets.
Controls/TextControlsIntegration.csproj measures and draws a real TextBlock
with its actual User/AsCulture default number-substitution properties. Existing
WPF DigitState resolution accepts no-op settings and rejects required changes.
See [Compatibility.md](Compatibility.md) for the accepted Unicode 17 policy and
measured native Windows differences. The main fixture accepts --capture-oracle;
StockOracle also accepts --controls.

Windows WPF graphics module absence is checked on both operating systems:
successful execution on Windows must not hide an unported native call.

The Controls fixture also executes real TextBox word movement and selection
commands corresponding to Ctrl+Left/Right and Ctrl+Shift+Left/Right. It checks
both flow directions, full forward/backward traces and minimum/maximum clamps.
The stock `--words` capture supplies common-case reference traces; explicit
Unicode 17 cases record the separately accepted word-classification differences.
These command checks exercise WPF editing behavior without requiring a native
window. The complete-view native repeated-click workflow provides the mouse
integration check after resize and DPI transitions.

## Supported contract and open gates

This is a horizontal LTR/RTL physical-font contract. It supports left alignment,
Unicode legal greedy wrapping and explicit source runs. Script/direction/font
segments are internal implementation details. Real font files are retained by their `GlyphTypeface`; per-operation
fonts and shaping buffers are disposed independently.

Named `NotSupportedException` diagnostics reject tabs, discretionary hyphen
insertion, uncovered graphemes, custom OpenType typography, non-baseline inline
alignment, changed formatting inside a grapheme, justification, custom line heights,
indentation, markers, paragraph decorations and effects,
required digit substitution, collapsing, optimal paragraph caches and native Line Services contexts. Font
style simulations, font collections/face indexes and non-file sources remain
unsupported. The current paragraph limit is 32767 UTF-16 units. Only Unicode cmap
formats 4 and 12 are supported by `CharacterToGlyphMap`.
The legacy `TypefaceMap.GetShapeableText` DirectWrite itemizer and native
typography feature cache remain unavailable. Portable typography construction
and invalidation do not implement custom OpenType feature controls.

`GlyphTypeface`'s supported data includes file identity/stream, style, weight,
stretch, horizontal normalized line metrics, `GlyphCount`, Unicode character
mapping, nominal `AdvanceWidths` and supported glyph outlines. Localized font
metadata, version/embedding metadata, symbol classification, subsetting,
vertical advances and side-bearing access are unimplemented. They are not
qualified by these tests. The preserved full native implementation is still
compiled for other paths; this is not a full native dependency removal.

The production font catalog scans existing local `.ttf`/`.otf` files. Fallback
uses complete graphemes, style ranking then ordinal paths in the primary font's
directory. It downloads no fonts. Older regression cases still use installed
Arial/Segoe UI Symbol on Windows and DejaVu Sans on Linux; new reference cases
use identical pinned fonts. Full DirectWrite metric/raster parity is not claimed.
OpenType feature controls, language-dependent fallback, advanced paragraph
breaking, text decorations, IME, accessibility,
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

Run the same executable with `--measure-continuations` to measure public
`FormatLine`/`TextLineBreak` loops at 7,998, 15,996 and 32,760 UTF-16 units.
The fixture warms up first and uses the pinned DejaVu font at size 20, with
widths 100 and 1,000,000 to cover many narrow lines and one wide line, using
both a whole-string source run and constant-time one-character source runs. It verifies
source consumption and expected line counts and reports timings without a
machine-dependent threshold. The formatter brackets legal break candidates
exponentially then searches the bracket, avoiding full-suffix shaping on every
narrow line and a scan of increasingly long prefixes on a wide line.
