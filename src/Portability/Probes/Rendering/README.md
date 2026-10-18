# Rendering component probe

This executable tests SkiaSharp CPU raster capability. It does not port WPF,
exercise the fork's retained rendering path or render a NINA view. Passing it is
evidence for a possible rendering component, not framework compatibility.

## Behavior checklist

- Draw a rectangle and triangle with observable interior pixels.
- Restrict drawing to a clip and verify pixels inside and outside it.
- Translate drawing and verify the translated location and untouched origin.
- Composite a half-opacity red layer over blue and read back blended pixels.
- Change a rectangle's color and bitmap pixels, redraw and verify the changes.
- Restore both values and verify the entire original raster is restored.
- Encode initial and changed PNGs, decode them and verify pixels and distinct hashes.
- Repeat native resource construction, drawing, readback and disposal 32 times.
  Verify handles exist before disposal and are zero afterward. This checks explicit
  disposal behavior; it does not measure native memory leaks or WPF resource lifetime.
- Emit JSON with `probe`, `os`, `scope` and named `checks`. A component check failure
  exits with code 1. `fork-retained-rendering` always reports `not-tested`.

No GPU, windowing, text, fonts, visual invalidation, dependency properties, hit testing,
animation, effects, WPF pixel-format semantics or retained composition is tested.
Changing values redraws a fresh raster in this probe; it does not simulate WPF invalidation.

## Run

Use the SDK selected by `src/Portability/global.json` by working inside that tree.
Keep the output argument explicit to place artifacts under the repository artifact root.
From PowerShell at the repository root:

```powershell
$env:NUGET_PACKAGES = "$PWD/artifacts/portability/packages"
$env:DOTNET_CLI_HOME = "$PWD/artifacts/portability/dotnet-home"
$renderingOutput = "$PWD/artifacts/portability/rendering/win-x64"
Set-Location src/Portability/Probes/Rendering
dotnet restore Rendering.csproj --locked-mode
dotnet run --project Rendering.csproj -c Release -- $renderingOutput
```

From Bash at the repository root on Linux:

```bash
export NUGET_PACKAGES="$PWD/artifacts/portability/packages-linux"
export DOTNET_CLI_HOME="$PWD/artifacts/portability/dotnet-home-linux"
rendering_output="$PWD/artifacts/portability/rendering/linux-x64"
cd src/Portability/Probes/Rendering
dotnet restore Rendering.csproj --locked-mode
dotnet run --project Rendering.csproj -c Release -- "$rendering_output"
```

Successful execution writes `initial.png` and `changed.png`. JSON goes to stdout so
the caller can capture it separately. Outputs and intermediate files are separated
by `$(OS)` under `artifacts/portability/rendering` for Windows and Linux execution.

## Dependency choice

The experiment pins matching `SkiaSharp` and
`SkiaSharp.NativeAssets.Linux.NoDependencies` versions to **3.119.4**, an available
stable version verified against the official NuGet indexes. This is a reproducible
candidate for the experiment, not a claim that it is the newest release or the final
framework dependency. `packages.lock.json` locks transitive versions and content hashes.

- [SkiaSharp 3.119.4](https://www.nuget.org/packages/SkiaSharp/3.119.4)
- [Linux native assets 3.119.4](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/3.119.4)
- [SkiaSharp version index](https://api.nuget.org/v3-flatcontainer/skiasharp/index.json)
- [Linux asset version index](https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/index.json)

The restored Linux package contains `runtimes/linux-x64/native/libSkiaSharp.so`.
Its nuspec declares MIT licensing and dependencies on glibc system libraries
(`libpthread`, `libdl`, `libm`, `libc` and the dynamic loader). It excludes Fontconfig.
The package name does not mean no operating-system dependencies. This probe requires
no fonts, display server, HarfBuzz package or separate test framework. The SkiaSharp
package also brings matching Windows and macOS native assets transitively; their
presence does not verify macOS execution.

## Fork boundary evidence and next experiment

Source inspection found a candidate semantic boundary in
`src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/RenderData.cs`:
`IDrawingContent.WalkContent(DrawingContextWalker)` replays recorded drawing operations.
However, `RenderData` is an internal partial `Freezable` implementing `DUCE.IResource`.
`DrawingContextWalker` inherits the full WPF `DrawingContext`, and `Visual` also
implements `DUCE.IResource`. Those files cannot be linked into this isolated probe
without bringing the framework graph or manufacturing fake framework types.

Loading the current `PresentationCore` also runs `ModuleInitializer.Initialize`,
which invokes user32 DPI setup and DirectWrite loading.
`PresentationCore.csproj` directly references the C++/CLI `DirectWriteForwarder`
project. This component experiment deliberately does not claim to bypass those blockers.

The next fork experiment needs a buildable portable framework slice and an explicit
decision about its initialization and composition boundary. It should record a real
WPF `DrawingVisual`, replay its actual drawing content into a backend, change the
original WPF properties and verify invalidation, clipping, transforms and resource
lifetime on Linux. Keep the existing Windows MIL path available as the reference.
Do not expand this probe into a fake WPF API or use its fresh-raster redraw as evidence
that a retained `Visual` seam has been established.
