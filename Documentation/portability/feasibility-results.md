# WPF cross platform feasibility results

Checkpoint date: 18 October 2026. Branch: `codex/nina-cross-platform`.

The Phase 1 experiments establish a viable open-source Linux x64 path for this fork. Actual source-built WPF assemblies, the dispatcher, retained renderer, bounded text implementation and native controls run through the selected X11 adapter. The app-local SDK compiles genuine BAML on Linux and its runtime runs both new and unchanged Microsoft-produced fixtures. Framework-dependent and self-contained package consumers pass. The same complete compiled view workflow now also passes on stock Windows and all four Linux compiler-origin/deployment combinations. Public Application.Run startup and large-frame performance remain open; NINA itself has not passed the Linux compatibility gates.

The Phase 0 reference/runtime gate and Phase 1 feasibility gate pass within the scopes recorded below. Phases 2 through 5 have not passed. The accepted [plan](../cross-platform-plan.md) remains the behavior contract. Physical desktop, full application and sustained-workload qualification remain later gates. Component checks, source compilation and managed test peers do not substitute for native WPF control behavior.

## Baseline and environment

| Item | Recorded result |
| --- | --- |
| Original WPF source | `65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9` |
| Pinned NINA source | `4742afae0a09c79be772f23fcf163fb5852d2775` |
| NINA root tree | `4b0826a22ab9a558fe340b8ba360d745240380a7` |
| Original Windows product | SDK `12.0.100-alpha.1.26480.102`, `net11.0`, assembly major 12; root pins unchanged |
| Separate opt-in portable product | SDK 10.0.401, `net10.0`, assembly version 10.0.0.0 |
| Windows reference | Stock WindowsDesktop 10.0.12 on Windows 10.0.26200 |
| Linux execution | Ubuntu 24.04.1 under WSL2, x86_64; WSLg and isolated native Xvfb tests recorded separately |
| Linux SDK archive SHA-256 | `137268c8ad939c064ff1ee2a6fdf0899d8725377114ea012fbd1ad5fa2550418` |

The exact pinned Windows SDK was recovered from the official CI feed after its primary endpoint returned 404. The ordinary `build.cmd -pack -plat x64 -c Release` passed with zero warnings and errors after selecting a supported Perl locale. The latest Windows regression run also passes after repairing a thread-static initializer introduced during portable settings work. All 208 existing upstream threading tests pass in that rebuilt product. These are recorded checkpoint runs; later source changes require their relevant checks again. The [runtime decision](runtime-baseline.md) records commands, provenance and signing policy.

The portable projects compile this fork's actual implementation and resources with a consistent version-10 contract suitable for the pinned NINA runtime. Genuine Windows metadata remains an interim compile dependency for unported paths. Public signing preserves tested type identity; it does not authenticate these builds as Microsoft binaries.

## Executed source integration

These results have different scopes and must not be added into a general compatibility score.

| Integration | Windows result | Linux result | Established scope |
| --- | --- | --- | --- |
| [Foundation identity](../../src/Portability/Tests/FrameworkIdentity/README.md) | 8 checks pass | 8 checks pass | App-local WindowsBase/System.Xaml, original public key tokens, Default load context, cross-assembly dependency properties, Freezable and CLR XAML |
| [WPF dispatcher](../../src/Portability/Tests/DispatcherIntegration/README.md) | 14 checks pass | 13 pass, one STA-only case inapplicable | Actual priorities, frames, waits, timers, cancellation, exceptions, worker wake, shutdown and managed pump lifecycle |
| [Markup compiler](../../src/Portability/Tests/MarkupIntegration/README.md) | 3 artifact checks and 3 stock runtime checks pass | 4 artifact checks and native wrong-case rejection pass | Original PresentationBuildTasks/WinFX targets, pass2, generated hookup and byte-identical cross-OS BAML/resources |
| [BAML source runtime](../../src/Portability/Tests/MarkupRuntimeIntegration/README.md) | Fork-produced fixture 7/7; frozen Microsoft-produced fixture 5/5 | Fork-produced fixture 7/7; frozen Microsoft-produced fixture 5/5 | Real source PresentationFramework construction, generated fields/events, templates, bindings, dictionaries and pack resources; no native window qualification |
| [Retained rendering](../../src/Portability/Tests/RenderingIntegration/README.md) | 19 checks pass | 19 checks pass | Actual DrawingVisual/UIElement, RenderData, layout, MediaContext, geometry, brushes, glyphs, invalidation, independent DPI scales, surface lifetime and animation scheduling without starving Background observations |
| [Geometry](../../src/Portability/Tests/GeometryIntegration/README.md) | 194 checks pass; stock oracle 188/188 | 194 checks pass | Actual containment, outline topology, glyph counters, transforms, widened stroke bounds and retained drawing-content bounds |
| [Guideline snapping](../../src/Portability/Tests/GuidelineIntegration/README.md) | 44 checks pass; separate stock raster oracle | 44 checks pass | Native-derived geometry, image and glyph-baseline snapping, nested scopes and visual-boundary isolation |
| [WPF text](../../src/Portability/Tests/TextIntegration/README.md) | 344 checks pass | 344 checks pass | Actual paragraph wrapping, fallback, Unicode 17 bidi, combining marks, source-run spans, caret/selection geometry and retained glyph pixels within the declared profile |
| [Word selection](../../src/Portability/Tests/TextIntegration/Compatibility.md) | 210 control checks pass | 210 control checks pass | Actual TextBox move/select word commands in both flow directions; the user-approved Unicode 17 classification retains WPF's RichEdit algorithm and Latin table |
| [Scientific bitmaps](../../src/Portability/Tests/ImagingIntegration/README.md) | 12 groups pass | 12 groups pass | Actual CachedBitmap/WriteableBitmap, exact Gray16/Rgb48 samples, caller ownership, nested locks, dirty publication, cloning/freezing and retained DrawImage |
| [Native dispatcher](../../src/Portability/Tests/NativeDispatcherIntegration/README.md) | Builds; runtime intentionally unsupported | 17 checks pass on native Xvfb | Actual WPF dispatcher with pinned Avalonia X11/GLib pump, native input, pending queues, nested frames, inline Send, disabled-processing waits and callback exception safety |
| [PresentationSource](../../src/Portability/Tests/PresentationSourceIntegration/README.md) | 29 checks pass | 29 checks pass | Real source/root ownership, layout, rendering, DPI, reentrant callouts and stale callback cleanup with a managed host |
| [Core input](../../src/Portability/Tests/InputIntegration/README.md) | 36 checks pass | 36 checks pass | Actual InputManager staging, routed events, commands, focus, capture and cursor visibility with a managed host |
| [Settings](../../src/Portability/Tests/SystemSettingsIntegration/README.md) | 19 checks pass | 19 checks pass | Explicit immutable palette and editing preferences, genuine resource/theme lookup and XAML access policy |
| [Window lifecycle](../../src/Portability/Tests/WindowLifecycleIntegration/README.md) | 59 checks pass; stock oracle 19/19 | 59 checks pass | Genuine Window ownership, modal cleanup, asynchronous bounds and content sizing with a managed peer; independent lifetime, fractional acknowledgment and explicit-request replays pass |
| [Native WPF Window](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Builds; runtime intentionally unsupported | 22/22 for each XSETTINGS byte order; nine rejection cases pass | Genuine WPF Window, Button.Click, TextBox input, synchronous dialog, scientific bitmap mutation, cover/uncover, five-button state, capture and minimum-pixel DPI behavior on Xvfb |
| [Native window manager](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Not applicable | Ten fresh Openbox runs, 13/13 each | Actual state, frame extents, maximize/minimize/restore, pending bounds intents, callback-triggered close and settled native pixels |
| [App-local SDK/runtime](../../src/Portability/Packaging/README.md) | External consumer 11 checks per build/publish/custom path; archive/output audit 6/6 | External consumer 11 checks per path; native FDD and self-contained 13/13 each; archive/output audit 8/8 | Local-feed restore, actual compiler, two BAML origins, companion assembly, forwarders, case-sensitive Linux compilation, Unicode pack resources, identities, compiler origins, licenses and exact runtime/native contents |
| [Complete unchanged view](../../src/Portability/Packaging/Consumer/App/README.md) | Stock reference 15/15 for each compiler origin | 19/19 in each of four package-only processes | Same compiled view and original dictionary, native validation/editing/commands, owned dialogs, bound RGB48, resources, clocks, both DPI directions and fresh lifetimes; explicit host templates remain part of this profile |

The text runs load no Windows interop assemblies. A faithful managed span container removes a C++/CLI metadata dependency from the existing span algorithms. Native typography caches and the inactive legacy itemizer have explicit portable diagnostics. This closes the observed DirectWrite startup dependency for these paths, not every framework text feature.

The 344 text, 29 source and 36 core-input checks also pass on both operating systems from an isolated archive of commit `dd62f1432`, without the uncommitted PresentationFramework or native-window work. Guideline checks separately pass from an archive of commit `ed97e8604`. These runs verify that the logical commits contain their exercised dependencies.

The source-built PresentationFramework, its five extension assemblies and the settings/editing/resource/Window integration are committed as `d3962709e`. A fresh archive of that commit passes all 56 managed Window checks on each operating system. Its original Windows build/pack and 208 upstream dispatcher checks also pass. Later native hosting and package-only consumer checks have their own source and artifact records.

The subsequent restore-intent repair in `171c1dde7` passes 59 managed Window checks on each OS and the 19-case stock oracle. It sends each newly pending size/position intent once even when a transient Normal snapshot already matches, avoiding loss of that intent before a later window-manager restore. Independent reviewers replay the original lifetime and fractional cases successfully; native protocol traces retain the original skipped-request failure separately.

Bounds tests cover hollow figures, null brushes, widening before world transforms, miter/cap behavior and collapsed contours. Nonconstant curved strokes requesting a tolerance below local floating-point coordinate precision fail explicitly. Default and tested 0.01-DIP tolerances pass the bounded profile; this is not arbitrary-tolerance native geometry parity.

The native dispatcher uses the actual WPF operation queue. Its original window and raw event target are Avalonia objects. The separate native WPF Window fixture now routes OS input through the real WPF InputManager and controls. The WSLg keyboard/focus run remains failed evidence; passing Xvfb does not supersede it or qualify a physical desktop.

Full source PresentationFramework and five framework extension assemblies compile under the portable identity. Both BAML origins now construct on Linux using only the Microsoft.NETCore.App shared framework. The source-built and unchanged fixture binaries remain separate evidence. Settings, actual input providers, source/window lifetime and the default portable Window style have separate tests. Native controls pass the declared explicit-template profile, with window-manager behavior recorded separately. No managed peer result counts as native window qualification.

The XAML access checks retain the genuine managed implementation from System.Windows.Extensions 10.0.13. Its default Linux asset throws before exercising XamlAccessLevel. An explicit MSBuild asset policy selects the same package's managed implementation and records its unchanged hash in published output. This does not port that assembly's Windows native features. The app-local runtime package delivers this policy transitively to ordinary external SDK consumers.

The Phase 1 package graph was produced from commit `7874ee35628d6ba9d338754dae2d3c0a8e274fce`. All 3,554 captured build inputs matched their recorded hashes at qualification. Independent artifact review replays the archive/output audits and verifies both package hashes, compiler origins, notices and the frozen Microsoft fixture bytes. The runtime contains 52 managed assets and two Linux x64 native libraries in a closed allowlist. The SDK carries the genuine compiler task closure; compile-only Windows metadata is excluded from runtime outputs. Eight SDK/dependency policy checks and fourteen direct/transitive runtime policy checks pass, including named rejection of unqualified RIDs and publication modes.

| Local package | Version | SHA-256 |
| --- | --- | --- |
| Wpf.Portable.Runtime | 10.0.0-portable.1 | `0A263235AF0529E4BACBBDF446B26C21995CEB958F77B839BAEDA98D5915844E` |
| Wpf.Portable.Sdk | 10.0.0-portable.1 | `2B957A26B154EEC2467618A1478D97D520A6E42606F92609D68FC121D91D0F80` |

These artifacts remain local under `artifacts/portability/distribution-final-2/feed`. External Linux compilation, caches, publication and execution use a fresh case-sensitive native `/tmp` directory with a local-only feed. This establishes build independence from repository imports; it does not establish a pristine physical desktop installation. The final original Windows build/pack passes with zero warnings/errors and all 208 upstream threading tests pass after the native/package integration.

## Integrated view checkpoint

The unchanged `FixtureControls.FixtureView` now passes the same shared workflow
under both compiler origins. Stock WindowsDesktop 10.0.12 passes 15 checks per
origin. Linux passes 19 checks per process for Microsoft-produced and newly
fork-produced BAML under framework-dependent and self-contained deployment. The
additional Linux checks audit native startup, runtime payload bytes, load contexts
and the NETCore-only deployment. Both origins retain their original assembly
identity, view sources and dictionary. Explicit host control templates remain
declared; this does not qualify ordinary default themes.

The native workflow covers valid/invalid/recovery editing, source updates in both
directions, command disable/enable, synchronous owned-dialog accept/cancel,
exact RGB48 storage and displayed mutation/replacement, dynamic-resource
replacement/removal and real opacity clocks. Each grow/shrink and 96/144-DPI
transition requires physical client geometry, a fresh routed double-click with
full word selection and a real bound command entering an owned dialog. Fresh
windows close during active animation, detach synchronously and raise `Closed`
once. Native disappearance is observed separately through the existing bounded
event-pump wait. Windows DPI messages target owned windows only; they do not
qualify physical monitor transitions. Linux uses Xvfb/XTest and declared
XSETTINGS values.

This workflow exposed two production defects. Commit `22479f00b` prevents
clock-generated render requests from starving Background work. Commit
`e01fc4e20` replaces portable Windows character queries with open Unicode 17
classification inside the existing selection algorithm. Their independent
reviews and Windows/Linux regressions pass. The normal Windows build/pack
subsequently passes with zero warnings/errors and all 208 upstream threading
tests pass. The close-observation repair in `0f1baa44d` changes the fixture only;
its original four failing native runs remain preserved.

The refreshed package producer is commit
`b3126d3a3d0c135a872cea680fc26a6e0d72d6b5`. Its 3,554 inputs matched at final
qualification and independent review. The retained Windows/Linux package matrix,
archive/output audits and policy checks pass. The complete-view runs use these
exact local package bytes under `artifacts/portability/distribution-phase2-final-1`:

| Local package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `EDDAA19ACA776D356EC6BE90DF0FE443E41A1A7F229EA7BD31A898E7FF74CFF2` |
| Wpf.Portable.Sdk | `62F9E6099AF9D7F2E5876AE2B8DCEF4DBE09D192D6138C120456850D8D71FD95` |

The functional comparison is passed. Phase 2 remains open while public
`Application.Run` startup and the approved large-frame performance workload are
completed. The first Linux workload preflight builds and audits successfully but
fails before bitmap allocation because `Application.Run` still creates its
Windows parking HWND. That failure is a framework startup gap, not an image-size
or throughput result. A real public-entry-point repair is in progress; the
measurement does not substitute a fixture dispatcher loop.

## Pinned NINA reference and dependency closure

The [source inventory](nina-inventory.md) verifies selected files against Git blob IDs and SHA-256 hashes. The [control dependency audit](nina-control-dependencies.md) verifies 13 exact NuGet packages, 11 selected implementation assets, 94 BAML keys, 35 pinned startup resource files and 34 direct dictionary edges. It inspects 17,304 method bodies without executing the packages.

This audit identifies real Windows integration inside control libraries, including AvalonDock floating window hooks and AvalonEdit caret/IME paths. These dependencies are separate from NINA camera/device SDKs. Matching WPF assembly identities alone cannot make every unchanged control binary portable. Package-specific licenses and source adaptations need to be tracked individually.

The [Windows reference runner](../../src/Portability/Tests/NinaWindowsReference/README.md) verifies all 3,370 source blobs before and after building. Nine unchanged upstream NUnit cases pass in isolated stock WPF processes: eight GuiderChart cases and one DirectGuiderDetail case. Assembly evidence verifies the actual loaded stock framework and pinned NINA/control paths.

The ordinary NINA build still records missing native payload copy failures. The view-only runner explicitly omits 50 absent External copy items, redirects test state to artifacts and records every omission. It neither edits managed source/XAML nor supplies dummy native libraries. Selected view tests are a Windows baseline, not a full NINA application or hardware result.

The [unchanged NINA diagnostic](../../src/Portability/Tests/NinaPortableViews/README.md)
loads the actual `DirectGuiderDetailView` and its six upstream dictionaries on
Linux. Its pinned closure contains 82 managed assemblies and 78 satellites, with
six unresolved references recorded explicitly. Stock Windows passes six checks.
Linux passes unchanged-binary and BAML construction checks then fails positive
content layout: the UserControl has no template or visual children and its
original Grid remains unmeasured. The same stock view has a ContentPresenter and
an arranged 800x450 Grid. This isolates a missing default-theme profile rather
than establishing a layout pass. No NINA template is replaced. A genuine
source-built Classic theme is the next bounded implementation; broader view,
control-library and shell qualification remain open.

## Review and repaired failures

Independent reviews and failing regressions led to concrete repairs:

- Disabled-processing waits preserve Windows STA behavior.
- Markup task outputs are isolated from consumer output, artifacts, TFM and RID settings. Valid repeated incremental pass2 reuse verifies actual BAML hashes; changed certificates fail.
- Text fixes cover exact-fit word wrapping, CRLF across source runs and signed ink overhang, plus native metadata closure.
- Native pump fixes drain both actual Xlib and grouped input queues before blocking. Signal and Background callback exceptions cross the GLib C boundary safely instead of aborting the process. Signing-key and derived-source generation preserve no-change builds.
- Early closure of the original component host cannot report success with an incomplete suite.

Raw negative cases remain alongside the passing results. Local changes and commits have not written to GitHub.

## Phase gates

| Gate | Current status | Required evidence |
| --- | --- | --- |
| Phase 0 source and runtime contract | Reference gate passed | Maintain Windows coverage and investigate newly reached native dependencies |
| Phase 0 NINA reference | Selected actual view tests and pinned dependency inventory pass | Broader view references and physical desktop hosts remain later qualification |
| Phase 1 assembly identity | Package-only closure, companion, forwarders and distribution pass | Broaden actual control and plugin binary execution in Phase 3 |
| Phase 1 markup/BAML | Both origins and case-sensitive packaged compiler path pass | Run the same complete view workflow under both origins in Phase 2 |
| Phase 1 event loop/window/input | Native dispatcher, WPF controls/dialogs, window-manager and packaged native checks pass | Combine validation, command and dialog interactions in Phase 2; physical desktop services remain later qualification |
| Phase 1 retained rendering | Drawing-content seam, native cover/uncover, image mutation and packaged presentation pass | Bound Image control, animation, large frames and sustained workloads remain later gates |
| Phase 1 replacement text | Bounded paragraphs, fallback, Unicode 17 bidi, caret/selection and native BMP editing pass | Integrated validated editing in Phase 2; full IME and supplementary committed input remain open |
| Phase 2 integrated view | Functional Windows/Linux comparison passed; phase gate open | Public Application.Run startup and measured large-frame budgets remain incomplete |
| Phases 3 through 5 | Gates not passed | Real NINA view/control interactions, simulated shell/overnight session and packaged native Linux qualification |

Scientific Gray16/Rgb48 source storage and readback now pass the bounded imaging slice. Gray16 display differs by at most one output level from the measured native WIC boundary samples; scientific source samples remain exact. Image codecs, conversion graphs and large-frame memory/performance remain open. IME, accessibility, physical desktop focus/DPI transitions, native Wayland, ARM64 and macOS remain unqualified. CPU rendering and short disposal loops do not establish overnight memory or responsiveness bounds.

The initial native X11 Window profile requires at least one physical client pixel on each axis. Explicit application requests for zero or subpixel client extents are an unsupported boundary, checked before issuing native configure requests. Stock Windows WPF instead coerces these requests to its desktop-specific native minimum and publishes that accepted size through Width/Height, ActualWidth/ActualHeight and RestoreBounds. Matching that behavior needs a logical/native minimum-size contract; this profile does not claim it. Preserving an already valid native window during a DPI change is a separate requirement and must not emit an illegal zero-pixel configure request.

The native implementation is committed as `cf4a52c96`. A pinned two-line Avalonia derivative preserves an existing window's one-pixel minimum when DPI conversion would truncate an axis to zero. Both asymmetric axes and the one-by-one case pass repeated 96/144-DPI round trips, including unrelated frames after each profile. Independent callback replay passes direct close during state, DPI, location and resize changes. No-change Windows/Linux builds preserve the signing key, both derived source files and X11 DLL bytes and timestamps while skipping compilation. The later patch-file context cleanup produces exactly the same verified derivative bytes.

## Evidence and continuation

The [source integration summary](source-integration-evidence.json) records hashes and named status counts for raw result files. The older [component evidence](feasibility-evidence.json) is retained separately. Raw logs, downloaded source, packages, SDKs and rendered images stay in ignored `artifacts/portability`. The [source index](../../src/Portability/README.md) links reproduction instructions.

Continue into Phase 2's unchanged compiled view using the qualified local package feed. Keep one owner for the combined workflow and send measured framework failures to the existing component owners. Native Xvfb demonstrates real WPF controls, synchronous dialogs and mutable scientific bitmap presentation. Core input separately passes 36 checks per OS with independent capture/focus/cursor repair review; its host is managed. These scoped results do not establish complete application or physical desktop compatibility.

The user-approved performance workload is 6000 x 4000 scientific frames, Gray16 and RGB48 measured separately, at one full-frame update per second with interactive zoom/pan. Measure Windows reference and Linux results, including startup, input-to-binding/presentation latency, allocations and post-warmup memory growth, before setting numerical budgets. The small correctness fixture does not establish large-frame throughput or an overnight bound.

The stock Windows SoftwareOnly reference completed 30 measured updates plus
three warmups for each format with exact full-size 16-bit readback. On the
declared Ryzen 9 7950X host and 1184x811 viewport, mean WritePixels calls were
2.38 ms for Gray16 and 6.16 ms for RGB48. Dispatcher/render-callback transactions
had means of 24.03/55.40 ms and p95 values of 38.93/69.60 ms. These boundaries
include work before presentation and do not measure presentation latency.
Sampled peak private bytes were 526.31/799.51 MiB, including retained 48/144 MB
source and validation arrays. Short-run growth and weak-reference observations
do not establish leaks or overnight bounds. The matching Linux workload shares
the same extracted update algorithms and timing blocks; its public startup
failure is recorded above, so there is no Linux performance verdict or accepted
numerical budget yet. Raw measurements, source hashes and limitations are under
`artifacts/portability/scientific-workload-reference`.

The accepted bidi contract follows Unicode 17. All 91,707 Unicode bidi character level cases pass after a bounded correction to the pinned managed dependency. This algorithm result is separate from paragraph layout, fallback, glyph positioning and native editing qualification. Legacy Windows WPF bracket ordering and measured Display-mode metric differences are recorded compatibility differences, not silently relaxed test tolerances.

The selected direction is the source WPF semantic core with an owned pinned Avalonia X11/GLib adapter and CPU Skia rendering. The [backend decision](backend-decision.md) records the source-supported rejection of the public-only bounded pump and lower DUCE seam, along with the absence of equivalent head-to-head candidate tests. This direction remains subject to the unpassed behavior and distribution gates above.
