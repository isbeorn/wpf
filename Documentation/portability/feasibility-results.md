# WPF cross platform feasibility results

Checkpoint date: 18 October 2026. Branch: `codex/nina-cross-platform`.

The Phase 1 experiments establish a viable open-source Linux x64 path for this fork. Actual source-built WPF assemblies, the dispatcher, retained renderer, bounded text implementation and native controls run through the selected X11 adapter. The app-local SDK compiles genuine BAML on Linux and its runtime runs both new and unchanged Microsoft-produced fixtures. Framework-dependent and self-contained package consumers pass. The same complete compiled view workflow now also passes on stock Windows and all four Linux compiler-origin/deployment combinations. Public Application.Run startup is repaired. Large-frame measurements expose a missed RGB48 update cadence and display differences; NINA itself has not passed the Linux compatibility gates.

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
| [Settings](../../src/Portability/Tests/SystemSettingsIntegration/README.md) | 20 checks pass | 20 checks pass | Explicit immutable palette, editing preferences and four scrollbar metrics, genuine resource/theme lookup and XAML access policy; source-built Classic control layout has a separate four-case result |
| [Window lifecycle](../../src/Portability/Tests/WindowLifecycleIntegration/README.md) | 66 checks pass; stock oracle 26/26 | 66 checks pass | Genuine Window ownership, modal cleanup, asynchronous bounds, content sizing and public Application.Run startup/shutdown; independent lifetime, fractional acknowledgment and explicit-request replays pass |
| [Native WPF Window](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Builds; runtime intentionally unsupported | Original byte-order suites pass; absent-distance repair adds 56 startup cases, two 23-check native suites and 20 threshold observations | Genuine WPF Window, Button.Click, TextBox input, synchronous dialog, scientific bitmap mutation, cover/uncover, five-button state, capture and minimum-pixel DPI behavior on Xvfb; fresh packaged GNOME replay remains pending |
| [Native window manager](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Not applicable | Ten fresh Openbox runs, 13/13 each | Actual state, frame extents, maximize/minimize/restore, pending bounds intents, callback-triggered close and settled native pixels |
| [App-local SDK/runtime](../../src/Portability/Packaging/README.md) | Classic feed: external consumer 12 checks per build/publish/custom path; archive/output audit 6/6 | Classic feed: external consumer 12 checks per path; native FDD and self-contained 14/14 each; archive/output audit 8/8 | Local-feed restore, actual compiler, both BAML origins, default Classic templates, companion assembly, forwarders, case-sensitive compilation, identities, licenses and exact contents; Windows package checks exclude native text layout |
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

Commit `360e1f601` repairs public `Application.Run` by excluding the Win32 parking
HWND from the portable path. The existing managed startup, showing, shutdown and
exit logic remains in use. Seven new process-isolated public-entry-point cases
fail before the repair and pass afterward, alongside the original lifecycle
cases: 66/66 on each OS and 26/26 in the stock oracle. Independent replays pass.
The ordinary Windows build/pack passes with zero warnings/errors and all 208
upstream threading tests pass. Application-wide native activation/session messages
and CommonDialog ownership remain separate unqualified services.

A fresh producer from that commit passes the retained package matrix, audits and
policy checks. All 3,554 input hashes matched at capture. Its local feed is
`artifacts/portability/distribution-phase2-final-2`:

| Local package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `D4D12C5FA1FC17732D6BDA321F8318835F07A2ADA1A26E8439873D8FA5B912E5` |
| Wpf.Portable.Sdk | `D6C4B11A307343FE43E90E16E27B1F83D85E162A67A8CF26BDFE94774F30FB9B` |

The unchanged workflow passes 19/19 in all four Linux processes again with these
packages. Its newly built fixture has identical BAML, resources and executable
IL to the fixture in the stock reference. PE differences are confined to MVID,
deterministic timestamps and debug records. Stock executed the earlier fixture
bytes; the later binary retains its separate Linux qualification. The source
index preserves the detailed comparison and both compiler-origin records.

The functional comparison and public startup pass. Phase 2 remains open for
display fidelity and the approved large-frame workload. Earlier native startup
failures remain preserved. The matched workload now executes the real public
entry point, with no fixture dispatcher-loop substitute. Explicit Manual window
placement is shared by both measurement peers; CenterScreen remains unqualified.

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
than establishing a layout pass. No NINA template is replaced.

Commit `f2363124d` builds the complete original Classic theme and supplies four
immutable scrollbar metrics with declared 17-DIP defaults. The original XAML and
eight original compile items remain intact. Windows and Linux produce identical
Classic BAML. Ordinary UserControl, Button and TextBox layout plus custom-control
generic fallback and explicit-style removal pass four checks on each OS using a
private Classic profile. All three 20-check settings profiles pass, and independent
correctness and maintainability reviews pass. The normal Windows build/pack and
208 upstream threading tests also pass after the metric changes.

Commit `db753499b` selects that Classic profile through the two source mapping
lines. Normal source graphs without an overlay pass the same settings and layout
checks on both OSes. Commit `000afc7` adds the theme to the runtime package graph
and selects a fresh isolated markup compiler for its genuine BAML build. Commit
`c0b2b469a` repairs temporary markup-project asset selection. Its clean producer
qualifies all 17 package records with 3,567 unchanged inputs, 53 managed assets
and two Linux native libraries. The separately pinned consumer `a790c7299`
constructs genuine Classic templates and native-free layout on Windows; Linux
also requires positive Button/TextBox text layout. Both focused reviews pass,
and the older generic feed still fails the new default-template check.
PresentationUI remains compile-only and was not copied or loaded by these cases.
The full deferred document-resource closure, broader views, control libraries
and shell qualification remain open.

The qualified local feed is
`artifacts/portability/distribution-phase3-classic-final-2`:

| Local package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `0C2B3AFEDEC2EB86DD1731130DED5F3DF7AEA574DC5B9359FD63525929C0B266` |
| Wpf.Portable.Sdk | `4802533D0BE91B4A69E13D73C5556ED921259348C3938AE56EA15B17A6BC609A` |

The unchanged NINA replay passes its first five checks on Linux, then fails real
layout while resolving `(Validation.Errors).CurrentItem.ErrorContent` in the
original TextBox style. Its genuine Microsoft BAML maps the presentation xmlns
to Ribbon and WindowsFormsIntegration as well as the core assemblies, although
it references no Ribbon or WinForms control type. The schema loader eagerly
requires every namespace candidate before resolving `Validation`. A narrow
namespace-loading repair is in progress; this is not a NINA layout pass. The
Windows portable replay instead reaches the bundle's known lack of Windows
HarfBuzz native assets. Stock Windows retains its separate six-check baseline.

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
| Phase 2 integrated view | Functional Windows/Linux comparison and public Application.Run pass; phase gate open | RGB48 misses the approved 1 Hz cadence in WSL/Xvfb; display fidelity, measured budgets and sustained-memory bounds remain open |
| Phases 3 through 5 | Gates not passed | Real NINA view/control interactions, simulated shell/overnight session and packaged native Linux qualification |

Scientific Gray16/Rgb48 source storage and readback pass the bounded imaging slice. Gray16 conversion differs by at most one output level at the small measured native WIC boundary samples; this does not establish scaled-image display parity. The large-frame workflow has larger observed display differences while scientific source samples remain exact. Image codecs, conversion graphs and large-frame memory/performance remain open. IME, accessibility, physical desktop focus/DPI transitions, native Wayland, ARM64 and macOS remain unqualified. CPU rendering and short disposal loops do not establish overnight memory or responsiveness bounds.

The first additional desktop target is Ubuntu 22.04.5 with GNOME 42.9 and
XWayland on Hyper-V. Its package-only probe stops during native startup before
any WPF Window or input: GNOME publishes a valid 400 ms double-click time but
omits distance. Independent decoding confirms the parser's result. A separate
read-only GTK query reports an effective 5-pixel distance. Desktop settings owner,
XSETTINGS bytes and RESOURCE_MANAGER bytes remain unchanged after both probes.
The user approved a declared 5-pixel fallback only for absent distance, preserving
published values and all invalid-data failures. Commit `06bac0b52` implements that
policy after complete payload and raw-name validation, with per-field provenance.
All 56 startup cases, two 23-check native suites and 20 deterministic threshold
observations pass. Independent review caught and closed acceptance of malformed
setting names; the original failures remain preserved. These are dedicated Xvfb
checks, including replay of the captured GNOME property. A fresh packaged desktop
replay remains pending. This is neither a desktop pass nor a native Wayland result.

The initial native X11 Window profile requires at least one physical client pixel on each axis. Explicit application requests for zero or subpixel client extents are an unsupported boundary, checked before issuing native configure requests. Stock Windows WPF instead coerces these requests to its desktop-specific native minimum and publishes that accepted size through Width/Height, ActualWidth/ActualHeight and RestoreBounds. Matching that behavior needs a logical/native minimum-size contract; this profile does not claim it. Preserving an already valid native window during a DPI change is a separate requirement and must not emit an illegal zero-pixel configure request.

The native implementation is committed as `cf4a52c96`. A pinned two-line Avalonia derivative preserves an existing window's one-pixel minimum when DPI conversion would truncate an axis to zero. Both asymmetric axes and the one-by-one case pass repeated 96/144-DPI round trips, including unrelated frames after each profile. Independent callback replay passes direct close during state, DPI, location and resize changes. No-change Windows/Linux builds preserve the signing key, both derived source files and X11 DLL bytes and timestamps while skipping compilation. The later patch-file context cleanup produces exactly the same verified derivative bytes.

## Evidence and continuation

The [source integration summary](source-integration-evidence.json) records hashes and named status counts for raw result files. The older [component evidence](feasibility-evidence.json) is retained separately. Raw logs, downloaded source, packages, SDKs and rendered images stay in ignored `artifacts/portability`. The [source index](../../src/Portability/README.md) links reproduction instructions.

Continue Phase 2's measured image-display and performance repairs alongside the bounded NINA default-theme work. Keep one owner for each production boundary and send measured framework failures to that owner. Native Xvfb demonstrates real WPF controls, synchronous dialogs and mutable scientific bitmap presentation. Core input separately passes 36 checks per OS with independent capture/focus/cursor repair review; its host is managed. These scoped results do not establish complete application or physical desktop compatibility.

The user-approved performance workload is 6000 x 4000 scientific frames, Gray16 and RGB48 measured separately, at one full-frame update per second with interactive zoom/pan. Measure Windows reference and Linux results, including startup, input-to-binding/presentation latency, allocations and post-warmup memory growth, before setting numerical budgets. The small correctness fixture does not establish large-frame throughput or an overnight bound.

Matching Windows SoftwareOnly and Linux CPU-backend runs completed three warmups
and 30 measured updates per format, plus six separate dirty-only updates. Both
use the same extracted algorithms, actual 1184x811 viewport at scale 1 and exact
full-size 16-bit validation. The Ryzen 9 7950X host also runs WSL2 with 32 logical
CPUs; host activity was not isolated. Linux execution uses Xvfb and the freshly
qualified package bytes above.

| Measurement | Windows Gray16 | WSL2 Gray16 | Windows RGB48 | WSL2 RGB48 |
| --- | ---: | ---: | ---: | ---: |
| Mean full WritePixels | 2.28 ms | 13.51 ms | 6.50 ms | 42.96 ms |
| Mean dispatcher/render-callback transaction | 26.75 ms | 828.41 ms | 55.38 ms | 1581.07 ms |
| Transaction p95 | 39.04 ms | 883.07 ms | 71.93 ms | 1834.58 ms |
| Thirty-update span | 29.033 s | 29.824 s | 29.056 s | 47.549 s |
| Sampled private-counter peak | 526.04 MiB | 738.03 MiB | 894.11 MiB | 1489.63 MiB |

Linux RGB48 accumulates 17.02 seconds of start lateness and cannot sustain the
approved one-second update interval in this configuration. Dirty-only publication
also remains expensive: mean callback transactions are 1163.69/2144.74 ms on
Linux versus 8.26/8.51 ms on Windows. Subsequent one-variable controls and native
leaf profiles identify repeated whole-source display conversion as the dominant
measured cost in this configuration. Commit `0f57b2d73` hoists invariant pixel
format and row-stride metadata out of those loops without changing conversion
math, storage or ownership. All 32 complete converted-buffer cases per OS remain
byte-exact, Imaging 12/12 and Rendering 19/19 pass on both systems and independent
reviews find no supported defect.

A profiled source-built comparison subsequently uses 99 runtime binaries per
side, with only PresentationCore differing. Three warmups and three measured
24 MP transactions per format produce mean callback times of 841.58 to 377.58 ms
for Gray16 and 1576.00 to 506.67 ms for RGB48. Each transaction has two presents
and no GC collections. Full initial/final native images match across variants,
source samples remain exact and independent review reproduces the raw results.
The diagnostic source preserves the original callback boundary but adds tracing
and does not traverse the full zoom/replacement sequence. It measures an
intermediate renderer state in a fixed run order. The fresh combined-package
30-frame workload and performance gate remain open.

The 18 paired native pixel samples differ by up to 57/255. All 18 Windows samples
match their stock offscreen references, while Linux's first Gray16 center is 4
versus Windows 53. Source bit depth, channel order and complete readback remain
exact. Display parity is unqualified; expected pixels have not been changed to
hide this result.

The user subsequently accepted documented, bounded backend sampling differences
while preserving WPF mode/state semantics and exact scientific source values.
Calibration separates format-specific interior interpolation, geometry-derived
edge coverage and coordinate-justified nearest-texel boundary choices. Observed
1-2/255 interpolation and up to 17/255 edge-coverage differences are corpus
measurements, not a global tolerance. Implementation, large-coordinate checks and
native requalification remain in progress. HighQuality/Fant prefiltering remains
required by the NINA image paths and is not qualified by that policy decision.

The timings end at dispatcher/render callbacks, not confirmed presentation.
Native observations occur outside those timers and do not prove the latest
submitted frame. Memory includes retained 48/144 MB source and validation arrays;
Linux private counters are not equivalent to Windows commit. Short runs, bitmap
replacements and in-scope scene objects prevent leak or overnight conclusions.
Numerical latency, allocation and memory budgets remain unset. Raw runs, matching
source hashes and limitations are under
`artifacts/portability/scientific-workload-reference/countermeasurement`; the
earlier canonical Windows baseline remains unchanged.

The accepted bidi contract follows Unicode 17. All 91,707 Unicode bidi character level cases pass after a bounded correction to the pinned managed dependency. This algorithm result is separate from paragraph layout, fallback, glyph positioning and native editing qualification. Legacy Windows WPF bracket ordering and measured Display-mode metric differences are recorded compatibility differences, not silently relaxed test tolerances.

The selected direction is the source WPF semantic core with an owned pinned Avalonia X11/GLib adapter and CPU Skia rendering. The [backend decision](backend-decision.md) records the source-supported rejection of the public-only bounded pump and lower DUCE seam, along with the absence of equivalent head-to-head candidate tests. This direction remains subject to the unpassed behavior and distribution gates above.
