# WPF cross platform feasibility results

Checkpoint date: 18 October 2026. Branch: `codex/nina-cross-platform`.

The experiments support continuing the port's feasibility work. The current fork's System.Xaml implementation runs on Linux and the selected open-source graphics, shaping and window-host components are available and executable. They do not yet establish that WPF views or NINA run on Linux. Phase 0 and Phase 1 remain partial; Phases 2 through 5 have not started.

This is the requested feasibility checkpoint before moving into later implementation. The accepted [plan](../cross-platform-plan.md) and its required behavior remain in force.

## Baseline and environment

| Item | Recorded result |
| --- | --- |
| Original WPF source | `65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9` |
| Pinned NINA source | `4742afae0a09c79be772f23fcf163fb5852d2775` |
| NINA root tree | `4b0826a22ab9a558fe340b8ba360d745240380a7` |
| Production SDK/TFM/assembly major | SDK `12.0.100-alpha.1.26480.102`, `net11.0`, assembly major 12; unchanged |
| Experimental SDK/runtime | SDK 10.0.401 and runtime 10.0.12 on Windows and Linux x64 |
| Windows reference | Stock WindowsDesktop 10.0.12 on Windows 10.0.26200 |
| Linux environment | Ubuntu 24.04.1 under local WSL2/WSLg, x86_64, X11 handle descriptor `XID` |
| Linux SDK archive SHA-256 | `137268c8ad939c064ff1ee2a6fdf0899d8725377114ea012fbd1ad5fa2550418` |

The fork build command `dotnet build Microsoft.Dotnet.Wpf.sln --configuration Release --property:Platform=x64` failed before compilation because the pinned .NET 12 alpha SDK is not installed. The corresponding archive request at the primary builds.dotnet.microsoft.com endpoint returned HTTP 404. This does not establish that every distribution source is unavailable. The SDK, target framework and assembly version are separate compatibility dimensions, not by themselves proof of invalid source.

A read-only comparison of upstream `release/10.0` resolved to `b784c5bf8d2dddbf6a93444ad9bbdb5ab0e05363`, selecting SDK 10.0.112 with `latestFeature` roll-forward and targeting `net10.0`. That is a concrete baseline candidate for NINA, not an adopted production baseline. No source branch was replaced and no production target was retargeted.

The [NINA inventory](nina-inventory.md) verifies 59 selected files against pinned Git blob IDs and SHA-256 hashes. The complete tree has 196 XAML paths. It corrects the unpinned preliminary package observations, including AvalonDock 4.74.1, WebView2 1.0.4078.44 and Microsoft.Xaml.Behaviors.Wpf 1.1.142. It remains a static inventory, not a NINA build or runtime result.

## Executed experiments

| Experiment | Windows result | Linux result | Established scope |
| --- | --- | --- | --- |
| Stock WPF reference | 11 checks passed across seven isolated cases | Windows-only fixture | Actual compiled WPF App, Window and second-assembly control/dictionary; binding, validation, command recovery, images and dispatcher contracts |
| Current-source System.Xaml | Seven checks passed | Seven checks passed | Actual linked fork source with original resources, public CLR parsing, extensions/converters, references, serialization and diagnostics |
| Ordinary Core identity | Core WindowsBase shim observed | Two checks passed | The shim has no DependencyObject and the process requires only Microsoft.NETCore.App |
| Stock WindowsBase in an explicit isolated load context | Cross-assembly default/set/clear/callback operations passed | Expected failure at first dependency-object construction | Managed assembly loading can succeed before Windows-native execution fails; no app-local fork runtime proof |
| Skia CPU rendering | Three component checks passed | Three component checks passed | Primitive/clip/transform/opacity pixels, bidirectional scene/bitmap mutation, PNG readback and explicit disposal cycles |
| Skia/HarfBuzz text | Nine component checks passed | Nine component checks passed | Explicit-font run shaping, combining clusters, directed Arabic run, measurement and software rasterization |
| Avalonia host, headless | 13 checks passed | 13 checks passed | Avalonia object lifetime, invokes, cancellation, timer, owned modal, one nested frame and synthetic input |
| Avalonia host, native X11 | Used for early-close regression testing | 13 checks passed; native keyboard/text delivery unproven | Native WSLg window, nonzero XID, activation/control focus and host operations |

See the [probe index](../../src/Portability/README.md) for source and reproduction instructions. The checked [evidence summary](feasibility-evidence.json) records the individual result-file hashes and check counts; raw logs, downloaded source, SDK and images remain in ignored `artifacts/portability`.

### What the actual source experiment establishes

The isolated System.Xaml project links the current fork's implementation and shared sources. It embeds the original resx messages and restores the missing Arcade resource-accessor generation with a small MSBuild task. No replacement WPF types or dummy resource messages were introduced.

Both operating systems pass CLR content collection, custom extension/converter, forward-reference identity, save/reload, malformed XML location and resource-backed unknown-type diagnostic cases. A full build reports four unsuppressed CA1416 warnings for Windows-only access-level APIs. Those permission/private-member paths are not exercised. The probe library is unsigned and reports version 10.0.0.0; it does not establish shipping assembly identity, WPF resources, BAML or control compatibility.

### Where genuine WPF execution stops on Linux

The supplied-runtime experiment loads stock `WindowsBase, Version=10.0.0.0` into one named AssemblyLoadContext. The companion library then constructs a genuine DependencyObject. Its dispatcher type initializer calls `RegisterWindowMessage`, which fails with `DllNotFoundException` for `user32.dll`. The process correctly returns exit code 1.

This is an observed execution boundary. Loading a managed assembly, changing an RID or resolving the Core WindowsBase shim does not supply a portable WPF dispatcher. The fork's dispatcher and lifecycle adaptation remain required.

### Rendering and text limits

Rendering uses a standalone Skia CPU scene. It does not traverse WPF RenderData, Visual or DUCE resources. The mutation checks redraw a scene; they do not implement WPF invalidation. Disposal checks verify released handles, not long-running native memory bounds. No scientific Gray16/Rgb48 WPF storage path is implemented.

Text reports seven WPF contract areas as unimplemented: discovery/fallback, mixed-direction paragraph layout, paragraph formatting, character hit testing, native WPF seam replacement, WPF metric/raster parity and cross-thread resource lifetime. Windows Arial and Linux DejaVu Sans are different font files, with their hashes recorded; their results do not establish cross-platform metric equality.

The source still imports native Line Services from PresentationNative, including line creation and hit testing. Its redistribution project obtains a prebuilt binary; the engine implementation is absent from this checkout. A portable replacement must implement the required TextFormatter/TextLine behavior. Removing C++/CLI or installing HarfBuzz alone does not close that gap.

### Native host limits

The native host experiment uses public Avalonia APIs. An owned asynchronous modal and one nested frame work in that host, but WPF synchronous ShowDialog, dispatcher priorities/reentrancy, HWND interop and input delivery through WPF remain untested. Native keyboard and text events were not delivered during the timed X11 run, so that check remains explicitly unproven. Headless synthetic input is recorded separately.

WSLg is useful native Linux process/X11 evidence. It does not qualify an independent GNOME/KDE desktop, physical monitor transitions, native Wayland, screen-reader behavior or an overnight NINA workload. No ARM64 or macOS result is claimed.

## Review and regression repair

Independent correctness and maintainability/test-value reviews identified one supported defect: closing the host window while its asynchronous checks were still running could return success with a partial suite.

A disposable Windows process reproduced the failure using `Process.CloseMainWindow`: the original implementation returned exit 0 after only five recorded checks. A completion guard now requires the entire sequence to finish. The same early-close interaction returns exit 1 with the completion check failing. Normal Windows headless, Linux headless and native X11 runs pass all 13 host checks after the repair. Required-but-absent native input also returns failure. The correctness reviewer rechecked the repair and found the issue resolved.

The other probe owners exercised invalid arguments/output/font or invalid-candidate paths and observed nonzero failures. The NINA collector passed native parsing, a public/cache rerun and disposable fixtures that reject corrupt/mismatched source, missing evidence and truncated trees. Generated dependencies and downloaded source remain ignored. No GitHub writes were performed.

## Phase gate status

| Gate | Status | Required next evidence |
| --- | --- | --- |
| Phase 0 pinned source inventory | Substantial static evidence collected | Evaluated dependency closure, actual NINA view construction and control binaries |
| Phase 0 Windows framework reference | Partial | Reproducible build of the chosen fork/source baseline, not only the stock runtime fixture |
| Phase 0 production runtime/source contract | Open | Choose and build one coherent baseline; experimental SDK 10 does not settle the production contract |
| Phase 1 ordinary app-local WPF identity | Open | Fork-built assembly closure and companion behavior under ordinary publication without WindowsDesktop |
| Phase 1 Linux WPF markup/BAML | Open | Native Linux PresentationBuildTasks, compiled component hookup, pack resources and both BAML origins against the port |
| Phase 1 WPF event loop | Open | Genuine WPF dispatcher/window/input adaptation and accepted modal/reentrancy behavior |
| Phase 1 retained rendering | Open | Actual WPF drawing resources through a selected portable seam with mutation/invalidation/lifetime checks |
| Phase 1 replacement WPF text | Open | TextFormatter/TextLine behavior, glyph resources and editing metrics through the proposed portable path |
| Phases 2 through 5 | Not started | The accepted Phase 1 gate or an explicit reviewed contract revision |

## Recommended continuation

Keep the source and binary compatibility claims unchanged. The next work is deeper Phase 1 integration:

1. Compare the concrete .NET 10 source candidate with this fork in isolation and establish a reproducible Windows source build. Decide the production baseline and signing/type-identity policy before porting shared core files. A baseline change needs its concrete compatibility consequences reviewed.
2. Produce a real WindowsBase closure and an owned native event-loop boundary. Replace the actual dispatcher wake/message paths and run the existing priority, frame, DisableProcessing, cancellation and shutdown contracts against the Linux host. Do not substitute Avalonia's dispatcher semantics as proof.
3. Connect a small real WPF retained drawing slice to the selected rendering candidate, including mutable resources and disposal. Keep this independent of the text-replacement lane where the boundary permits.
4. Implement the first portable WPF TextLine/font/glyph slice with agreed measurement and caret behavior, then prove editable text through the real WPF path.
5. Bring PresentationBuildTasks and actual compiled WPF resources into the same process. Only after these results agree should Phase 2's integrated control view begin.

The results favor further evaluation of Avalonia's open-source host plus Skia/HarfBuzz components, but they do not yet select a permanent backend. SDK/identity, dispatcher, retained rendering, text and markup integration can have separate owners after their contracts are explicit. The current checkpoint should not be converted into a Phase 1 pass by counting component checks.
