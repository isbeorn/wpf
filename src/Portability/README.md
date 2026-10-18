# WPF portability work

This directory contains isolated feasibility probes and the opt-in source-built framework work in the accepted [cross-platform plan](../../Documentation/cross-platform-plan.md). The [feasibility report](../../Documentation/portability/feasibility-results.md) records the checkpoint and remaining gates. A complete portable WPF runtime or SDK is not yet qualified.

The production projects and their Windows implementation remain the reference source. This directory selects .NET SDK 10.0.401 and supplies its own MSBuild boundaries so the experiments do not inherit the root's .NET 12 alpha SDK/Arcade requirements. Run SDK commands from inside `src/Portability`; selecting a project below it while staying at the repository root still selects the root SDK.

The [runtime baseline](../../Documentation/portability/runtime-baseline.md) records the successful upstream Windows source build and the separate .NET 10 portable assembly contract. The opt-in `Framework` projects compile the fork's actual implementation sources and original resources. Their public signing preserves tested assembly identity without asserting Microsoft authentication.

| Source integration | Evidence and instructions |
| --- | --- |
| [FrameworkIdentity](Tests/FrameworkIdentity/README.md) | Ordinary app-local source-built WindowsBase/System.Xaml, cross-assembly dependency properties, Freezable, XAML and runtime identity on Windows and Linux |
| [DispatcherIntegration](Tests/DispatcherIntegration/README.md) | Real WPF queue, priorities, frames, waits, timers, cancellation, shutdown and internal pump registration on Windows and Linux |
| [MarkupIntegration](Tests/MarkupIntegration/README.md) | Actual PresentationBuildTasks, pass2, BAML, output isolation and incremental verification on both OS |
| [MarkupRuntimeIntegration](Tests/MarkupRuntimeIntegration/README.md) | Separate source-built and unchanged Microsoft BAML origins; both complete real view construction and resource checks on Windows and Linux |
| [PresentationCoreIntegration](Tests/PresentationCoreIntegration/README.md) | Source-built PresentationCore identities, actual visuals, resources and native-load boundaries |
| [RenderingIntegration](Tests/RenderingIntegration/README.md) | Actual DrawingVisual/UIElement, RenderData, layout, DPI-scaled surfaces and retained invalidation on both OS |
| [ImagingIntegration](Tests/ImagingIntegration/README.md) | Real scientific bitmap storage, CopyPixels, locks, ownership, freeze/clone and retained image redraw on both OS |
| [GeometryIntegration](Tests/GeometryIntegration/README.md) | Actual WPF geometry containment, outline topology and bounds with separately recorded stock references |
| [GuidelineIntegration](Tests/GuidelineIntegration/README.md) | Native-calibrated retained geometry, image and glyph-baseline snapping with nested scopes and DPI checks |
| [PresentationSourceIntegration](Tests/PresentationSourceIntegration/README.md) | Actual WPF root/source ownership, layout, render scheduling, DPI changes and callout lifetime failures using a managed host |
| [InputIntegration](Tests/InputIntegration/README.md) | Actual InputManager providers, routed events, focus, capture, commands and basic editing; managed-host checks remain separate from native input |
| [SystemSettingsIntegration](Tests/SystemSettingsIntegration/README.md) | Explicit immutable settings profiles, actual resource/theme paths, XAML access checks and published runtime asset evidence |
| [TextIntegration](Tests/TextIntegration/README.md) | Real WPF paragraph formatting, Unicode 17 bidi, fallback, caret/selection metrics and retained glyph output within the declared profile |
| [NativeDispatcherIntegration](Tests/NativeDispatcherIntegration/README.md) | Real WPF dispatcher with native X11/GLib pump and XTest input; WPF controls remain separate |
| [WindowLifecycleIntegration](Tests/WindowLifecycleIntegration/README.md) | Real Window lifecycle, modal cleanup, asynchronous geometry and content sizing against a managed peer and stock Windows oracle |
| [NativeWindowIntegration](Tests/NativeWindowIntegration/README.md) | Actual WPF controls, synchronous dialogs, mutable scientific images and native X11 input/pixels; window-manager results remain separate |
| [Portable distribution](Packaging/README.md) | External local-feed SDK/runtime consumers, genuine BAML on case-sensitive Linux storage, preserved identities/forwarders, audited contents and native FDD/self-contained execution |
| [NinaWindowsReference](Tests/NinaWindowsReference/README.md) | Nine unchanged pinned NINA view tests against stock Windows WPF, with explicit native payload omissions |

| Experiment | Evidence and instructions |
| --- | --- |
| [AssemblyIdentity](Probes/AssemblyIdentity/README.md) | Core WindowsBase shim, genuine Windows dependency properties across an assembly boundary and an explicitly supplied runtime in an isolated load context |
| [WindowsReference](Probes/WindowsReference/README.md) | Stock Windows WPF compiled XAML, templates, binding, commands, images and dispatcher behavior |
| [SystemXaml](Probes/SystemXaml/README.md) | Current fork source-built System.Xaml parsing and serialization on Windows and Linux |
| [Rendering](Probes/Rendering/README.md) | Portable Skia CPU rasterization, mutation, readback and disposal component checks |
| [Text](Probes/Text/README.md) | Portable explicit-font shaping, measurement and rasterization, with unsupported WPF text behavior listed separately |
| [WindowHost](Probes/WindowHost/README.md) | Avalonia headless and native X11 host operations, with native input and WPF integration reported independently |

The older SystemXaml probe is unsigned; the separate Framework projects use the documented public signing contract. Windows reference probes use stock WindowsDesktop 10.0.12. Read the scope of each result: a backend component pass does not establish WPF compatibility.

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

The upstream Windows source build and selected pinned NINA Windows view tests pass. Phase 1 feasibility also passes its declared Linux x64 profile: both BAML origins, actual WPF dispatcher/native controls, retained rendering, bounded Unicode 17 paragraphs, window-manager state/lifetime and the final package-only distribution matrix. The [feasibility report](../../Documentation/portability/feasibility-results.md) records exact scopes, hashes and unsupported boundaries. Phase 2's same complete compiled view workflow and later NINA/physical desktop qualification remain open.
