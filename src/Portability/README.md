# WPF portability feasibility probes

These isolated experiments implement the early evidence-gathering work in the accepted [cross-platform plan](../../Documentation/cross-platform-plan.md). The [feasibility report](../../Documentation/portability/feasibility-results.md) records the checkpoint and remaining gates. This directory is not a portable WPF runtime or SDK.

The production projects and their Windows implementation remain the reference source. This directory selects .NET SDK 10.0.401 and supplies its own MSBuild boundaries so the experiments do not inherit the root's .NET 12 alpha SDK/Arcade requirements. Run SDK commands from inside `src/Portability`; selecting a project below it while staying at the repository root still selects the root SDK.

| Experiment | Evidence and instructions |
| --- | --- |
| [AssemblyIdentity](Probes/AssemblyIdentity/README.md) | Core WindowsBase shim, genuine Windows dependency properties across an assembly boundary and an explicitly supplied runtime in an isolated load context |
| [WindowsReference](Probes/WindowsReference/README.md) | Stock Windows WPF compiled XAML, templates, binding, commands, images and dispatcher behavior |
| [SystemXaml](Probes/SystemXaml/README.md) | Current fork source-built System.Xaml parsing and serialization on Windows and Linux |
| [Rendering](Probes/Rendering/README.md) | Portable Skia CPU rasterization, mutation, readback and disposal component checks |
| [Text](Probes/Text/README.md) | Portable explicit-font shaping, measurement and rasterization, with unsupported WPF text behavior listed separately |
| [WindowHost](Probes/WindowHost/README.md) | Avalonia headless and native X11 host operations, with native input and WPF integration reported independently |

The source-built System.Xaml library is unsigned and has an experimental .NET 10 assembly identity. The Windows reference and dependency-property fixtures use stock WindowsDesktop 10.0.12. Neither is the fork's shipping framework baseline. Skia, HarfBuzz and Avalonia are candidate components; their passing checks do not establish WPF compatibility.

## Reproduce

Use the per-experiment commands above. Keep NuGet caches, CLI state and generated outputs inside the ignored `artifacts/portability` directory. Use separate Windows and Linux build/intermediate directories where an experiment does not already select them by OS. The rendering, text and window-host package lock files record their dependency content hashes.

Windows-only fixtures require the matching WindowsDesktop targeting/runtime installation. Portable experiments require .NET 10, their restored native assets and, for text, an installed font covering the sample scripts. The native host additionally requires a working display and its platform libraries. Headless execution does not qualify a native desktop.

For this checkpoint, Linux execution used Ubuntu 24.04.1 under WSL2 with WSLg. A workspace-local official SDK was unpacked at `artifacts/portability/dotnet-linux-x64`. Its executable can be prepended to PATH or used explicitly in the per-experiment commands. No system SDK installation is required by the experiments.

Collect the pinned NINA source evidence from the repository root:

```powershell
./eng/portability/Collect-NinaCompatibility.ps1
```

That collector uses read-only HTTPS requests. Downloaded NINA source stays in ignored artifacts; its [inventory](../../Documentation/portability/nina-inventory.md) records the exact revision and coverage limits.

## Interpret results

Each executable emits named JSON checks and returns a nonzero exit code for an actual failure. Read its `scope` and every check status. `unimplemented`, `not-tested` and `unproven` are evidence gaps and must not be counted as passes. An intentionally negative experiment passes its investigation only when it produces the expected failure; the underlying capability remains unsupported.

Full WPF build, native Linux WPF markup compilation, BAML/control loading against the fork, WPF dispatcher adaptation, retained rendering and a replacement WPF text formatter remain separate gates. The current checkpoint does not authorize treating them as completed or entering Phase 2 on component results alone.
