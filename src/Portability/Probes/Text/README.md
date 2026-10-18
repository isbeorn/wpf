# Text component feasibility probe

This executable tests reusable native text components without loading WPF. It
does not implement `TextFormatter`, `TextLine`, `GlyphRun` or a WPF backend.
A successful run establishes component availability and observable shaped-run
output, not WPF text compatibility.

## Behavior checklist

- Load an existing font file explicitly and report its path, SHA-256 and family.
- Shape, measure and software-render a NINA-style Latin label, combining acute
  sequence and explicitly directed homogeneous Arabic run.
- Record glyph IDs, UTF-16 clusters, positions, advance widths and actual ink
  bounds. Save a PNG for each sample.
- Check combining-cluster mapping, RTL cluster ordering, missing-glyph detection,
  proportional advances at three font sizes and repeated shaping.
- Report unsupported WPF behavior as `unimplemented`, including paragraph layout,
  mixed-direction bidi, fallback and character hit testing.
- Return exit code 1 for actual font, native load, assertion, encoding, argument
  or output errors. Return 0 when the bounded component checks pass even though
  WPF contract items remain unimplemented.

The CLI writes JSON to stdout and `<output>/results.json` with
`probe`, `os`, `scope` and `checks: [{name, status, detail}]`. Check statuses are
`pass`, `fail` and `unimplemented`. Raw sample records make the assertions
inspectable. An unimplemented item is not a pass or a compatibility exception.

## Pinned dependencies and font policy

Exact package versions and transitive content hashes are in
`packages.lock.json`. The official NuGet flat-container metadata was checked
before selecting these versions:

- [SkiaSharp 3.119.4 metadata](https://api.nuget.org/v3-flatcontainer/skiasharp/3.119.4/skiasharp.nuspec)
- [SkiaSharp.HarfBuzz 3.119.4 metadata](https://api.nuget.org/v3-flatcontainer/skiasharp.harfbuzz/3.119.4/skiasharp.harfbuzz.nuspec)
- [SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.4 metadata](https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/3.119.4/skiasharp.nativeassets.linux.nodependencies.nuspec)
- [HarfBuzzSharp 8.3.1.5 metadata](https://api.nuget.org/v3-flatcontainer/harfbuzzsharp/8.3.1.5/harfbuzzsharp.nuspec)
- [HarfBuzzSharp.NativeAssets.Linux 8.3.1.5 metadata](https://api.nuget.org/v3-flatcontainer/harfbuzzsharp.nativeassets.linux/8.3.1.5/harfbuzzsharp.nativeassets.linux.nuspec)

These packages declare MIT licenses. The shaping package identifies source
commit `f568ac94dd768ef9a2f593537cfde2dd0d348ef5` in its metadata. This package
selection is a feasibility input, not a permanent backend decision. Keep package
license notices if redistributing their native assets.

No font file is checked in, downloaded or copied into build output. Pass
`--font` with an existing font covering Latin, combining acute and Arabic.
Without that argument, the probe tries installed Arial on Windows, DejaVu Sans
then Liberation Sans on Linux and installed Arial on macOS. A font that lacks
required sample glyphs causes a failure. A successful Arial run on Windows and
DejaVu run on Linux does not establish cross-platform metric parity because
they are different font files. The recorded hashes expose that difference.

Linux uses the NoDependencies Skia native package and explicit file loading, so
this probe does not test Fontconfig. Normal platform C runtime dependencies
remain. macOS native assets are present transitively but are unverified until
the executable is run there.

## Run

Use the SDK selected by `src/Portability/global.json`. Run restore/build from
`src/Portability` to select that SDK and its isolated build configuration.
Keep caches and outputs inside the workspace. Examples use this checkout's
absolute paths; adapt the checkout root if needed.

PowerShell:

```powershell
$env:NUGET_PACKAGES = 'E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME = 'E:\Projects\wpf\artifacts\portability\dotnet-home'
Set-Location E:\Projects\wpf\src\Portability
dotnet build Probes/Text/Text.csproj --artifacts-path E:\Projects\wpf\artifacts\portability\text\build\windows -p:RestoreLockedMode=true
dotnet E:\Projects\wpf\artifacts\portability\text\build\windows\bin\Text\debug\Portability.Text.dll --font C:\Windows\Fonts\arial.ttf --output E:\Projects\wpf\artifacts\portability\text\windows
```

Linux/WSL, with a Linux SDK installed:

```bash
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/packages-linux
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/dotnet-home-linux
cd /mnt/e/Projects/wpf/src/Portability
dotnet build Probes/Text/Text.csproj --artifacts-path /mnt/e/Projects/wpf/artifacts/portability/text/build/linux -p:RestoreLockedMode=true
dotnet /mnt/e/Projects/wpf/artifacts/portability/text/build/linux/bin/Text/debug/Portability.Text.dll --font /usr/share/fonts/truetype/dejavu/DejaVuSans.ttf --output /mnt/e/Projects/wpf/artifacts/portability/text/linux
```

To verify a failure, rerun the executable with a nonexistent `--font` and a
separate output directory. Expect `probe-runtime: fail` and exit code 1.

## Replacement scope and remaining gaps

The initial reusable scope is an explicit-font, directed-run service returning
glyph IDs, cluster maps, positions and advances that can be rasterized with
matching font ownership. This can inform a future internal WPF text adapter.
It cannot be dropped into the existing WPF pipeline unchanged:

| Existing source seam | Missing behavior |
| --- | --- |
| `PresentationCore/MS/internal/TextFormatting/TextFormatterImp.cs`, `FormatLineInternal` | Selects simple/full formatting paths with WPF paragraph semantics. Neither path is exercised here. |
| `PresentationCore/System/Windows/Media/textformatting/TextFormatterContext.cs`, `LoCreateContext` | Imports native Line Services context creation. |
| `PresentationCore/MS/internal/TextFormatting/LineServices.cs`, `LoCreateLine`, `LoCreateBreaks`, `LoQueryLineCpPpoint`, `LoQueryLinePointPcp` | Native paragraph/line layout, break records and character hit testing need an open implementation. |
| `redist/PresentationNative/PresentationNative.vcxproj` | Copies a prebuilt native binary from package references. The native Lo* engine source is not in this checkout. |
| `DirectWriteForwarder/CPP/DWriteWrapper/TextAnalyzer.cpp`, `Itemize`, `GetGlyphs`, `GetGlyphPlacements` | C++/CLI, DirectWrite and PresentationNative source/sink delegates remain. Replacing C++/CLI alone cannot provide the native engine. |
| `PresentationCore/System/Windows/Media/GlyphRun.cs`, `CreateOnChannel` | DUCE still receives retained `IDWriteFont` pointers. A portable glyph resource/lifetime contract is not implemented. |

Paths in the table are relative to `src/Microsoft.DotNet.Wpf` (`PresentationCore`
and `DirectWriteForwarder` are under its `src` directory).

[HarfBuzz documents](https://harfbuzz.github.io/what-harfbuzz-doesnt-do.html) that
run shaping does not implement bidi paragraph analysis, font-run segmentation,
line breaking, hyphenation or justification. Those responsibilities remain
even when every check here passes. WPF's `CharacterHit` needs directional edge
affinity and cluster/caret policy beyond a list of glyph positions. Ink bounds
also differ from advance width and cannot substitute for paragraph metrics.

Further evidence is required for WPF Ideal/Display measuring modes, line
metrics, font fallback, OpenType options, decorations, DPI changes, selection,
IME, accessibility, cross-thread font/resource lifetime and memory bounds.
This feasibility checkpoint must remain separate from any claim that NINA's
unchanged views or WPF text editing work on Linux.
