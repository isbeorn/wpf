# WPF cross platform feasibility results

Checkpoint date: 22 October 2026. Branch: `codex/nina-cross-platform`.

The latest NINA status and resume instructions are in the
[NINA launch plan](nina-launch-plan.md). The earlier
[20 October handover](handover-2026-10-20.md) records the package,
document, Markdown and window-placement checkpoint before the subsequent
empty-window, docking and NINA.Image repairs.
Earlier checkpoints below retain their original scope.

## Current release checkpoint

The original offline, simulator-only NINA App/MainWindow runs through the owned
Linux WPF backend on Ubuntu 22.04 GNOME/XWayland. There is no Avalonia runtime,
framework, build or source-import dependency. Skia, HarfBuzz and the attributed
Unicode derivatives remain. Full Phase 5 is not yet qualified.

The first Phase 5 profile is single-monitor, as explicitly approved by the user
on 22 October 2026. Physical multi-monitor qualification is deferred and remains
unqualified. All other accepted gates and earlier deferrals remain in force.

The package family from `87e4b29bc` passes all 488 checks: 250 standard consumer,
10 diagnostics enabled/disabled, 55 package-derived imaging, 161 affected native
and 12 actual Ubuntu 22.04 compiler/managed checks. Root independently verified
raw results, package/module identities, producing source records and owned
cleanup. Windows user and machine PATH remain unchanged. See the
[producer verification](../../artifacts/portability/current/final-distribution-20261022-7-proof/root-verification.json)
and [full consumer verification](../../artifacts/portability/current/final-distribution-20261022-7-consumer-proof/root-full-verification.json).
The ordinary 319-file NINA publication preserves original App/BAML and scientific
binaries while selecting the repaired runtime and AvalonDock component; its
[publication verification](../../artifacts/portability/current/nina/vm-package-final7-20261022-3/root-verification.json)
records the actual files and origins.

The original indexed-PNG docking guides now decode and render correctly.
Actual final7 GNOME replay shows all inner/outer glyphs without the pane preview,
genuine center docking and byte-identical saved-layout pixels after restart.
Three application lifetimes close normally. Bounded motion profiles show up to
four pixels of transient lag at 30/60 ms and zero settled error or stationary
movement after 150 ms; they do not establish universal smoothness. Root verified
227 evidence pins and 27 unchanged PATH pairs in the
[desktop replay](../../artifacts/portability/current/nina/vm-final7-docking-replay-20261022-1/root-verification.json).
Original imaging, FITS reload, sequencer, Framing and desktop-service passes
below retain their exact earlier publication scopes.

Installed diagnostics and the non-interposing native-allocation observer now
work together in a normally closed original NINA lifetime: 12 allocator samples,
13 external process samples and 60 seconds of observation. Handled binding
diagnostics remain visible and unclassified; only the final screenshot is
retained. This verifies observer compatibility, not sustained acquisition,
exhaustive native allocations or a resource ceiling. See the
[actual execution receipt](../../artifacts/portability/current/nina/allocator-diagnostics-resume-20261021-1/EXECUTION-HANDOFF.json).

The eight-hour coordinator has passed its finite regression and mutation
controls, but the real run has not executed. Remaining gates are actual
focus/keyboard/scale checks for the single-monitor profile, a distinct Xorg
desktop and sustained operation with measured resource acceptance. The
[active launch checkpoint](nina-launch-plan.md#active-checkpoint)
records the current evidence and remaining work. Native IME, further
accessibility, unused WPF, real hardware, ARM64 and macOS remain deferred or
outside this profile. The accepted RGB48 deadline remains unchanged.

## Earlier final5 release checkpoint

The following checkpoint retains its original package and application scope.

The owned Linux backend runs the original simulator-only NINA App/MainWindow
through ordinary portable SDK/runtime packages on Ubuntu GNOME/XWayland.
There is no Avalonia runtime, framework, build or source-import dependency.
The accepted Skia/HarfBuzz bindings and attributed Unicode derivatives remain.
The current WPF package source is `71e9f0f8f`; subsequent commits record evidence.

The final5 package producer passes its archive audits with no build errors.
Package-derived execution passes SDK native FDD 14, focus 19, input 28 and popup
11 checks on private Xvfb/Openbox. These are four scoped groups, not a count of
unique framework features. The [producer proof](../../artifacts/portability/current/final-distribution-20261021-5-proof/handoff.json)
and [consumer proof](../../artifacts/portability/current/final-distribution-20261021-5-consumer-proof/handoff.json)
bind the actual packages, sources and test outputs. Earlier full consumer and
Windows results retain their own package identities.

The same final5 pair also passes fresh Linux x64 self-contained publication,
12 managed apphost checks, 14 native apphost checks and seven content audits.
Both executions use the published .NET runtime with `DOTNET_ROOT` and
`DOTNET_ROOT_X64` unset. Source/PDB, compiler origins, exact publication contents
and owned test cleanup pass; see the
[self-contained proof](../../artifacts/portability/current/final-distribution-20261021-5-scd-proof/handoff.json).
These are private Xvfb results, separate from actual desktop qualification.
The [packaging guide](../../src/Portability/Packaging/README.md#linux-prerequisites-and-qualification-scope)
records the Linux library, service and font requirements for both deployment
forms. A self-contained application still needs those operating-system services.

Fresh final5 SDK compilation, FDD publication and all 12 managed consumer checks
also pass on the actual Ubuntu 22.04.5 VM with SDK 10.0.401, an empty package
cache and the local package feed. Compiler origins, loaded identities and 17
source documents are verified. This is separate from the Ubuntu 24.04.1
self-contained result and the installed NINA desktop runs; see the
[Ubuntu 22 compiler proof](../../artifacts/portability/current/final-distribution-20261021-5-ubuntu22-proof/handoff.json).

The fresh 319-file NINA publication preserves App/BAML, scientific components
and native astronomy libraries. Its actual desktop lifetime captures and
displays two 640x480 simulator images, saves FITS and invokes original thumbnail
reload without a logged error. The subsequent five-minute idle, fifteen-minute
capture and five-minute recovery pilot completes all 53 samples with exact
identity and sampler cleanup checks. Original Cancel works, and a later single
Snapshot displays a fresh image with finite statistics. Both saved files and
all 317 immutable installed members remain unchanged. See the
[actual UI handoff](../../artifacts/portability/current/nina/vm-package-final5-20261021-1/PILOT-UI-HANDOFF.json)
and [resource results](../../artifacts/portability/current/nina/overnight-preparation-20261021-1/pilot-final5-1/handoff.json).

The pilot's sampled RSS peaks at 646.0 MiB. Recovery ends at 626.7 MiB versus
the initial 554.4 MiB idle median, with two additional file descriptors. A later
passive observation of the same process measures 519.6 MiB and 18 threads, with
the descriptor count still 327. This establishes neither a leak nor an accepted
overnight ceiling. A separate 14-check private fixture verifies that the genuine
GuiderGraph, its plot and hosting Window collect after Close across two
lifetimes while their original VM and history remain alive. Other views and
native allocations retain their separate gates; see the
[bounded collection evidence](../../artifacts/portability/current/nina/closed-view-collection-20261021-1/handoff.json).

Physical floating-header dragging works without a crash, but the user's redock
attempt exposes misplaced guides. A read-only held-drag recording captures the
overlay's native bounds changing on mapping and retains the missing center-guide
observation. That defect remains under repair. Actual NINA ASCII clipboard
exchange passes both directions with a GTK peer. Its original sequence Save As
and Open workflows pass acceptance and cancellation through GNOME dialogs, with
real JSON and filename witnesses; see the
[desktop services evidence](../../artifacts/portability/current/nina/desktop-gates-20261021-2/SERVICES-HANDOFF.json).
Native allocation observation, installed diagnostics, correct redocking, Xorg
and overnight simulation remain unqualified. Native IME, further
Orca work and WPF features unused by NINA remain user-deferred. Linux ARM64 and
macOS remain later phases. No new RGB48 deadline optimization is planned.

Phases 0 and 1 passed within their recorded scopes. The complete Phase 2-5
gates have not passed, despite the substantial component and application
results. The accepted [plan](../cross-platform-plan.md) and its recorded user
revisions remain the contract; this checkpoint does not narrow those gates.

## Earlier 21 October application checkpoints

The following paragraphs retain the earlier integration-family results and
faults. They are not the current final5 publication status above.

The experiments establish a viable open-source Linux x64 path for this fork.
Actual source-built WPF assemblies, the dispatcher, retained renderer, bounded
text implementation and native controls run through the owned X11 adapter. The
app-local SDK compiles genuine BAML on Linux. Original simulator-only NINA now
captures and displays a 640x480 image through the full application on the real
GNOME/XWayland desktop, with AutoStretch, DetectStars, finite statistics and
histogram data. Native cancellation, recovery, normal close and a second launch
with the saved original layout pass. Virtual-screen metrics and DrawingBrush
shape fills clear the previous faults. The official-template plugin pane and
Installed/offline-catalog view also work. The current runtime and native receipts
are recorded in the launch plan.

Four required paths have framework repairs awaiting actual application replay:
floating-window height growth after restart, Offline Sky Map's grayscale
palette, sequence-drag Gaussian blur and nested-menu animation/delay settings.
Actual theme and locale
changes restore successfully, including byte-exact saved profile recovery.
The menu fault's original No button also completes normal shutdown. Its clipped
button labels remain a separate sizing observation. The sky-map/cache component
passes 42 checks on Windows and Linux
and the now-tracked original Framing/cache recipe passes 21 checks on the current
Linux runtime. These component results do not qualify the failed native Offline
Sky Map route. Remaining full application, desktop, final package and sustained
workload gates are open; unused WPF capabilities, native IME and further
accessibility work remain deferred at the user's request.

The combined released repair family passes 321 managed checks on each OS, including
the seven focused blur checks and full Rendering 75, Imaging 52,
SystemSettings 126 and VisualBrush 61 scopes. Its isolated frame lifecycle
matrix passes 100 checks per OS, including the focused 12, full 87 and
empty-window 1. All 109 native checks across 14 private processes pass, including
real submenu hover and captioned MessageBox button containment. The 12 blur
captures match between Windows and Linux;
against stock WPF, NINA's Radius 10 fixture differs by at most 4/255 while
preserving the measured geometry and clipping semantics. These are bounded
fixture measurements, not universal pixel tolerances. The actual NINA replay
passes capture/cancel/recovery, clean close/relaunch and stable outer size.
Offline SKYATLAS now loads, zooms and pans. Slider focus after rotation exposes
a new curved-dashed hit-test bounds rejection. A later hidden-page save also
changes the floating window's Top by one caption height, while its size stays
correct. Both are under diagnosis before the remaining Framing and sequence
checks. The launch plan records the closed state, exact runtime and evidence.

The Phase 0 reference/runtime gate and Phase 1 feasibility gate pass within the scopes recorded below. Phases 2 through 5 have not passed. The accepted [plan](../cross-platform-plan.md) remains the behavior contract. Physical desktop, full application and sustained-workload qualification remain later gates. Component checks, source compilation and managed test peers do not substitute for native WPF control behavior.

## Earlier owned-backend checkpoint

The paragraphs and table in this section preserve the earlier
`Stage-nina-integration-1` checkpoint. The user subsequently prioritized NINA's
required components and approved the focused image port. That port is committed
as `52f157449`; the latest native replay also verifies the CombinedGeometry
repair in `ec8a250a0`. See the launch plan for current publication identities,
accepted cache-filter differences and remaining integration failures. Later
component results do not retroactively change the scope of older families.

The released `Stage-nina-integration-1` family preserves complete 120-file
Windows and Linux runtime inventories. Its [handoff](../../artifacts/portability/current/stage-nina-integration-1/handoff.json)
records the actual source snapshot, held docking changes and compiler outputs.
Later Section/List and MessageBox changes are not covered by that frozen family.

| Gate | Current evidence | Limit |
| --- | --- | --- |
| Scientific display | The [tracked native recipe](../../src/Portability/Tests/NinaPortableViews/NativeImaging/README.md) passes Gray16 48/48 and Rgb48 48/48 at 6000x4000, with complete source ownership and native capture parity. [Raw qualification](../../artifacts/portability/current/reports/nina-imaging-durable-4/durable-qualification.json) | Private Xvfb, prepared frames and reported-only timing. AutoStretch and DetectStars are disabled. Acquisition, image preparation and annotations are not qualified. |
| Original editor/control workflow | The [AvalonEdit port](../../src/Portability/Dependencies/AvalonEdit/README.md) preserves 254 attributed originals and three guarded adaptations. The [sealed replay](../../artifacts/portability/current/richtext/nina-integration-1/closeout-seal.json) records 55 passing checks, including native cursor restoration and caret timer profiles. | Native IME, physical keyboard, live GNOME and the complete NINA document remain separate. |
| Managed document blocks | [Section/List qualification](../../artifacts/portability/current/section-list-verification/handoff.json) passes 31 checks on each OS and three stock checks. Genuine DOM, markers, pointers, RTL caret geometry and child rollback are exercised. | Table layout, inline images and full Markdown callback/link interaction remain open. |
| Original application startup | The [actual integration-1 attempt](../../artifacts/portability/current/nina/shell-integration-1/first-run-handoff.json) reaches original App.Main, composition and MainWindow.Show before asynchronous binding initializes StarAnnotator. | StarAnnotator requests Windows GDI+ through System.Drawing.Common. No usable native MainWindow or retained pixels were observed. The capture is inconclusive with zero frames. |

The user chose to finish WPF gates before adapting NINA.Image. That application
work, including any startup-only GDI deferral, remains deferred. The separate
MessageBox error-handler failure is being addressed through the approved private
managed dialog design; it is not the primary startup failure. Docking interruption,
document table/image integration, current package-only consumers, native desktop
interaction and Orca remain required. The accepted Rgb48 timing policy and canceled
optimization remain unchanged. No complete Phase 2-5 or full NINA claim follows.

## Historical 19 October checkpoint

On 19 October 2026 the user approved replacing the Avalonia adapter with an
owned Linux X11 backend inside `Wpf.Portable.NativeHost`, retaining WPF semantics
and the SkiaSharp 3.119.4/HarfBuzzSharp 8.3.1.5 bindings. The [backend decision](backend-decision.md)
records public GLib/GIO integration, the approved 34 Unicode derivatives and
the required behavior/package/desktop gates. Earlier Avalonia-backed results
retain their historical scope. The [owned bc9 checkpoint](../../artifacts/portability/owned-bc9-main-qualification/README.md)
passes 1,110 managed checks on each OS, 161 checks across eighteen existing native
modes and canonical Show 13/theme 20. Two icon rejection/recovery cases fail.
The [real NINA retry](../../artifacts/portability/nina-simulator-qualification-candidate/first-owned-shell-bc9-retry/handoff.json)
passes icon decoding but stops at WindowChrome's HWND access before a main window
maps. No full NINA window has opened in the VM. Later reviewed source commits add
RTL document mapping, wrapped-text cache refresh and icon preflight; their combined
family verification is pending. Separately, [commit 9f7638c85's scoped PNG proof](../../artifacts/portability/nina-png-frame-encoding/revision-2/Handoff.md)
passes Imaging 37/37 and Rendering 52/52 on both OSes using a Windows-built scoped
closure, not a fresh combined family or original 21/App replay. Source-backed
frames and clones retain live pixels and raw scientific samples; PNG saving is
limited to one Bgra32/Pbgra32/Bgr32 source-backed frame and Default/Off interlace,
without direct decoded-frame, metadata or scientific-save qualification. This
does not repair the current WindowChrome startup failure. The ledger now
preserves 85 checkpoints. Final
packages, complete workflows, desktop accessibility and overnight qualification
remain open. Native IME is user-deferred; basic XKB/compose input is required.

The evidence supports high confidence in technical feasibility. Confidence in the
full agreed NINA coverage remains moderate: original image and guider views run
in the bounded docking host and the original image controls pass native input
checks, but sustained imaging, broader plot input, strict live picker workflows
and screen-reader workflows remain unqualified. Native IME is user-deferred.

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

The exact pinned Windows SDK was recovered from the official CI feed after its primary endpoint returned 404. The ordinary `build.cmd -pack -plat x64 -c Release` passed with zero warnings and errors after selecting a supported Perl locale. The primary-screen regression build/pack passes with zero warnings/errors and all 208 upstream threading tests pass. The newer original Windows build after the exact five-file hidden-composition merge also passes with zero warnings/errors in 2m1.86s; no newer threading result is inferred from that build. This does not qualify native IME. Earlier font/guideline and managed-preedit Windows logs remain historical evidence. The [runtime decision](runtime-baseline.md) records commands, provenance and signing policy.

The portable projects compile this fork's actual implementation and resources with a consistent version-10 contract suitable for the pinned NINA runtime. Genuine Windows metadata remains an interim compile dependency for unported paths. Public signing preserves tested type identity; it does not authenticate these builds as Microsoft binaries.

## Executed source integration

These results have different scopes and must not be added into a general compatibility score.

| Integration | Windows result | Linux result | Established scope |
| --- | --- | --- | --- |
| [Foundation identity](../../src/Portability/Tests/FrameworkIdentity/README.md) | 8 checks pass | 8 checks pass | App-local WindowsBase/System.Xaml, original public key tokens, Default load context, cross-assembly dependency properties, Freezable and CLR XAML |
| [WPF dispatcher](../../src/Portability/Tests/DispatcherIntegration/README.md) | 14 checks pass | 13 pass, one STA-only case inapplicable | Actual priorities, frames, waits, timers, cancellation, exceptions, worker wake, shutdown and managed pump lifecycle |
| [Markup compiler](../../src/Portability/Tests/MarkupIntegration/README.md) | 3 artifact checks and 3 stock runtime checks pass | 4 artifact checks and native wrong-case rejection pass | Original PresentationBuildTasks/WinFX targets, pass2, generated hookup and byte-identical cross-OS BAML/resources |
| [BAML source runtime](../../src/Portability/Tests/MarkupRuntimeIntegration/README.md) | Fork-produced fixture 7/7; frozen Microsoft-produced fixture 5/5 | Fork-produced fixture 7/7; frozen Microsoft-produced fixture 5/5 | Real source PresentationFramework construction, generated fields/events, templates, bindings, dictionaries and pack resources; no native window qualification |
| [Retained rendering](../../src/Portability/Tests/RenderingIntegration/README.md) | Combined corpus 47 checks pass; focused visibility 27/27 | Combined corpus 47 checks pass; focused visibility 27/27 | Actual retained replay, invisible-subtree skipping/recovery and retained dash mutation; original 19-check result remains historical evidence |
| [Geometry](../../src/Portability/Tests/GeometryIntegration/README.md) | Combined corpus 355 checks pass | Combined corpus 355 checks pass | Existing containment, outlines, transforms, widened bounds and mixed strokes plus bounded Geometry.Combine; earlier stock 262/262 does not cover the expanded corpus |
| [Guideline snapping](../../src/Portability/Tests/GuidelineIntegration/README.md) | Combined corpus 301 checks pass; focused dashes 48/48 | Combined corpus 301 checks pass; focused dashes 48/48 | Native-derived snapping, retained VisualBrush and bounded dashed painting; latest stock default 194, focused dashes 29 and visibility 23 remain separate scopes |
| [WPF text](../../src/Portability/Tests/TextIntegration/README.md) | 344 checks pass | 344 checks pass | Actual paragraph wrapping, fallback, Unicode 17 bidi, combining marks, source-run spans, caret/selection geometry and retained glyph pixels within the declared profile |
| [Word selection](../../src/Portability/Tests/TextIntegration/Compatibility.md) | 210 control checks pass | 210 control checks pass | Actual TextBox move/select word commands in both flow directions; the user-approved Unicode 17 classification retains WPF's RichEdit algorithm and Latin table |
| [Scientific bitmaps](../../src/Portability/Tests/ImagingIntegration/README.md) | 12 groups pass | 12 groups pass | Actual CachedBitmap/WriteableBitmap, exact Gray16/Rgb48 samples, caller ownership, nested locks, dirty publication, cloning/freezing and retained DrawImage |
| [Native dispatcher](../../src/Portability/Tests/NativeDispatcherIntegration/README.md) | Builds; runtime intentionally unsupported | 17 checks pass on native Xvfb | Actual WPF dispatcher with pinned Avalonia X11/GLib pump, native input, pending queues, nested frames, inline Send, disabled-processing waits and callback exception safety |
| [Native IBus transport](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Combined native graph builds | 18 controlled private protocol/factory processes pass 48 checks | Pinned X11 IBus transport/factory only; actual composing engine, focused WPF editor attachment and live desktop remain unqualified |
| [PresentationSource](../../src/Portability/Tests/PresentationSourceIntegration/README.md) | 29 checks pass | 29 checks pass | Real source/root ownership, layout, rendering, DPI, reentrant callouts and stale callback cleanup with a managed host |
| [Core input](../../src/Portability/Tests/InputIntegration/README.md) | 36 checks pass | 36 checks pass | Actual InputManager staging, routed events, commands, focus, capture and cursor visibility with a managed host |
| [Settings](../../src/Portability/Tests/SystemSettingsIntegration/README.md) | 155 checks across seven processes | 155 checks across seven processes | Immutable settings, optional observed primary-screen pair and declared Classic defaults; no desktop font discovery, multiscreen updates or native Popup qualification |
| [Window lifecycle](../../src/Portability/Tests/WindowLifecycleIntegration/README.md) | 66 checks pass; stock oracle 26/26 | 66 checks pass | Genuine Window ownership, modal cleanup, asynchronous bounds, content sizing and public Application.Run startup/shutdown; independent lifetime, fractional acknowledgment and explicit-request replays pass |
| [Native WPF Window](../../src/Portability/Tests/NativeWindowIntegration/README.md) | Builds; runtime intentionally unsupported | Original byte-order suites pass; absent-distance repair adds 56 startup cases, two 23-check native suites and 20 threshold observations | Genuine WPF Window, Button.Click, TextBox input, synchronous dialog, scientific bitmap mutation, cover/uncover, five-button state, capture and minimum-pixel DPI behavior on Xvfb; live GNOME clipboard passes while strict picker/closure postconditions remain open |
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
requires every namespace candidate before resolving `Validation`. Commit
`3db204fb1` skips only genuinely absent optional namespace candidates while
continuing to load available contributors. Required types, explicit assembly
loads, malformed identities and dependency failures retain their errors. The
source suite passes 11 checks and the unchanged Microsoft BAML oracle passes
five checks on each OS, with independent replay. The later foundation package
and unchanged NINA layout replays below pass. The
Windows portable replay instead reaches the bundle's known lack of Windows
HarfBuzz native assets. Stock Windows retains its separate six-check baseline.

The subsequent committed foundation at `c21e514bb` qualifies all 17 distribution
records with 3,567 rehashed inputs, 53 managed assets and two Linux native
libraries. It combines Classic, the BAML repair, retained Linear/Nearest
sampling, disconnected strokes, the pointer fallback and bitmap metadata hoist.
The local feed is `artifacts/portability/distribution-phase3-foundation-final-1`:

| Local package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `5D66DB6D4D59291D1F59786F86C6F94A804CA440D3891902A9C5BBC5937B5B3A` |
| Wpf.Portable.Sdk | `53EDFB0D0A22C511EEE3EBF43C402D7BFFFFC22D42B753131347E18DC989ED8D` |

The unchanged package-only NINA Linux replay now passes all six checks with no
assembly overlay. The genuine view has its template and ContentPresenter, an
arranged 800x450 original Grid and the stock-observed column widths. Both replay
builds have zero warnings and errors. Windows passes five checks before the
known Linux-only native-font asset boundary. Interactive validation of NINA's
original controls remains a separate gate. The newer helper runtime profile,
clipboard, file dialogs, HighQuality/Fant, IME and accessibility are outside
this foundation package's qualification.

Subsequent stock and package-only Linux native observation each pass 13
non-input checks plus the completion guard. The original profile, three
UnitTextBox templates, actual bindings and rule boundaries are present. Linux
uses the genuine native Window with fresh 1000x600 layout after actual visual
attachment and observes nonuniform native pixels. This is presentation evidence,
not pixel parity or readability qualification. A subsequent identical-source
native editor replay passes 14 workflow/prerequisite checks plus its completion
guard on both stock Windows and package-only Linux. All 22 real edits match:
15 accepted values, seven rejections, per-keystroke profile retention until
actual focus departure, recovery and bound changes from 8 to 4 to 8. All seven
errors belong to the original outer BindingExpression and original validation
rule; inner errors remain empty. Independent review checks every case and the
native event traces. This qualifies the selected in-memory editor workflow,
not durable autosave or the full application. Linux public
RenderTargetBitmap export remains unsupported; the observation uses the existing
native pixel helper. No production template or reparenting repair was needed.

The unchanged OxyPlot 2.2 binaries and BAML also pass nine package-only Linux
checks on this foundation feed, without an assembly overlay. Actual zoom/pan,
both right-button releases and capture cleanup pass. Reverse pan approaches the
starting axis by over 99% while preserving its span. The fixture accounts for
the real scale change after tick-label layout rather than requiring an exact
inverse from equal screen-pixel deltas. This remains a selected control workflow,
not the full NINA shell.

The actual original GuiderGraph has a separate, narrower result. Stock Windows
passes 11 offscreen checks with all 33 original dictionaries, genuine guide
history events and the original Contrib axes/series/template. A fixture-owned
data context avoids equipment enumeration; original GuiderVM commands and
hardware behavior are not exercised. Package-only Linux on the c21 feed passes
five prerequisites, then fails during ComboBox construction:
`SystemParameters.PrimaryScreenHeight` still calls user32. No Linux Contrib plot
construction, update, mapped presentation or input is claimed. The revision-2
observer fixes the stock harness's unshown-Window measurement assumption by
laying out the actual view directly; it does not replace NINA resources or
templates. This historical c21 failure remains preserved independently of the
passing base OxyPlot slice.

After explicit early native initialization, an isolated source diagnostic passes
12 unchanged-GuiderGraph checks with all 33 dictionaries, real guide-history
updates and original Contrib objects. It preserves the stock fixture's direct
actual-view layout and leaves the Window unshown. The diagnostic substitutes
15 files and adds 90 source-stage files to the old package output, so it does not
qualify that package or a future feed. Mapped pixels, plot input, original
GuiderVM commands and equipment behavior remain unexecuted.

The genuine ImageControlVM stock reference separately passes 26 checks using
the unchanged ImageControlView/ImageView BAML, original mediators, 33 dictionaries
and real public VM commands. No NINA source is rebuilt. Its earlier coherent Linux
source diagnostic passes seven checks, fails the first Gray16 raster check and leaves
18 checks unreached, with no native load failures. All 143 consumed stage files,
160 original NINA closure files and exact image source bytes/geometry verify.
The failing null ImageBrush belongs to the Collapsed Bahtinov overlay with its
original VM flag false; the scientific image body uses a separate VisualBrush.
This does not establish a visible ImageBrush requirement or successful Linux
rotate/flip/crosshair/RGB48 rendering.

A paired renderer diagnostic passes 11 stock checks but reproduces eight
invisible-subtree failures on each portable OS, including Hidden/Collapsed
content, zero-opacity traversal and a hidden consumer's stale VisualBrush source
registration. It also verifies that an unattached visible offscreen root must
still paint. Those expected-red records remain unchanged. Commit `b81c905aa`
subsequently qualifies the bounded invisible-subtree repair in the combined
renderer graph described below; the original failing records are not relabeled
as passes.

The later [same-stage ImageControlVM diagnostic](../../artifacts/portability/nina-imagecontrolvm-invisible-subtree-diagnostic/revision-2/findings.md)
again passes seven checks, fails the first Gray16 raster and leaves 18 unreached.
It gets beyond the hidden Bahtinov ImageBrush and identifies the actual visible
`CornerScrollBarRectangle` in the original vertical scrollbar thumb: fractional
nonuniform guidelines reach an unsupported rounded-path conic. All 143 consumed
stage files and 160 original NINA files verify, with no native load failures.
The additional walker call/reflection observes the frozen renderer without
changing any of the original 26 assertions, NINA BAML, templates or VM bytes.
This preserves the earlier hidden-ImageBrush red as history and records the next
concrete owner; it does not pass Linux ImageControlVM, the full imaging workflow
or a package gate. The separately committed geometry and traversal results do
not substitute for a fresh original-view replay.

## Review and repaired failures

Independent reviews and failing regressions led to concrete repairs:

- Disabled-processing waits preserve Windows STA behavior.
- Markup task outputs are isolated from consumer output, artifacts, TFM and RID settings. Valid repeated incremental pass2 reuse verifies actual BAML hashes; changed certificates fail.
- Text fixes cover exact-fit word wrapping, CRLF across source runs and signed ink overhang, plus native metadata closure.
- Native pump fixes drain both actual Xlib and grouped input queues before blocking. Signal and Background callback exceptions cross the GLib C boundary safely instead of aborting the process. Signing-key and derived-source generation preserve no-change builds.
- Early closure of the original component host cannot report success with an incomplete suite.

Raw negative cases remain alongside the passing results. Local changes and commits have not written to GitHub.

## Earlier phase gate snapshot

This table preserves the earlier package and adapter checkpoint. Its open
issues and results retain that scope; use the current release checkpoint above
and the NINA launch plan for the selected owned-backend family.

| Gate | Current status | Required evidence |
| --- | --- | --- |
| Phase 0 source and runtime contract | Reference gate passed | Maintain Windows coverage and investigate newly reached native dependencies |
| Phase 0 NINA reference | Selected actual view tests and pinned dependency inventory pass | Broader view references and physical desktop hosts remain later qualification |
| Phase 1 assembly identity | Package-only closure, companion, forwarders and distribution pass | Broaden actual control and plugin binary execution in Phase 3 |
| Phase 1 markup/BAML | Both origins and case-sensitive packaged compiler path pass | Run the same complete view workflow under both origins in Phase 2 |
| Phase 1 event loop/window/input | Native dispatcher, WPF controls/dialogs, window-manager and packaged native checks pass | Combine validation, command and dialog interactions in Phase 2; physical desktop services remain later qualification |
| Phase 1 retained rendering | Drawing-content seam, native cover/uncover, image mutation and packaged presentation pass | Bound Image control, animation, large frames and sustained workloads remain later gates |
| Phase 1 replacement text | Bounded paragraphs, fallback, Unicode 17 bidi, caret/selection and complete Unicode commits pass | Native IME preedit/engines and broader document behavior remain open |
| Phase 2 integrated view | Functional Windows/Linux comparison, public Application.Run and short callback cadence pass; original NINA sustained workload still fails complete deadlines | Complete display qualification, presentation latency, measured budgets and sustained-memory bounds remain open |
| Phase 3 NINA controls/resources | Selected editor/base OxyPlot workflows, programmatic AvalonDock 9 and original image controls/crosshair pass within their scopes; gate open | Sustained imaging, broader plot input, pointer docking, plugins and sequencer coverage remain open |
| Phase 4 application shell | Gate open | Simulated equipment session, complete shell lifecycle and overnight stability remain open |
| Phase 5 packaged desktop | f7 package, live GNOME text clipboard and bounded Window/Button AT-SPI source protocols pass; gate open | Strict live picker selection/cancellation, screen-reader workflows and other platform/session gates remain open; native IME is user-deferred |

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

The refreshed foundation package subsequently passes 14 startup/render/close
checks on that Hyper-V GNOME XWayland session, exit 0 with empty stderr. A genuine
320x220 WPF Window maps, displays the expected native green pixel and closes
with one source detachment and eventual native-window disappearance. The probe
observes published 400 ms and declared-fallback 5 px provenance directly.
Settings ownership, all 2,044 XSETTINGS bytes and all 144 RESOURCE_MANAGER bytes
are identical before and after. Package/source hashes and 55 runtime assets are
verified on the VM. An artifact-only absent-fixture return moves before the
existing-owner rejection so the probe never acquires settings ownership. No
keyboard/mouse input or DPI changes occur. This qualifies startup/render/close
on that XWayland VM; interactive desktop, IME and screen-reader gates remain open.

The initial native X11 Window profile requires at least one physical client pixel on each axis. Explicit application requests for zero or subpixel client extents are an unsupported boundary, checked before issuing native configure requests. Stock Windows WPF instead coerces these requests to its desktop-specific native minimum and publishes that accepted size through Width/Height, ActualWidth/ActualHeight and RestoreBounds. Matching that behavior needs a logical/native minimum-size contract; this profile does not claim it. Preserving an already valid native window during a DPI change is a separate requirement and must not emit an illegal zero-pixel configure request.

The native implementation is committed as `cf4a52c96`. A pinned two-line Avalonia derivative preserves an existing window's one-pixel minimum when DPI conversion would truncate an axis to zero. Both asymmetric axes and the one-by-one case pass repeated 96/144-DPI round trips, including unrelated frames after each profile. Independent callback replay passes direct close during state, DPI, location and resize changes. No-change Windows/Linux builds preserve the signing key, both derived source files and X11 DLL bytes and timestamps while skipping compilation. The later patch-file context cleanup produces exactly the same verified derivative bytes.

## Evidence and continuation

The [source integration summary](source-integration-evidence.json) records hashes and named status counts for raw result files. The older [component evidence](feasibility-evidence.json) is retained separately. Raw logs, downloaded source, packages, SDKs and rendered images stay in ignored `artifacts/portability`. The [source index](../../src/Portability/README.md) links reproduction instructions.

Continue Phase 2's measured image-display and performance repairs alongside the bounded NINA default-theme work. Keep one owner for each production boundary and send measured framework failures to that owner. Native Xvfb demonstrates real WPF controls, synchronous dialogs and mutable scientific bitmap presentation. Core input separately passes 36 checks per OS with independent capture/focus/cursor repair review; its host is managed. These scoped results do not establish complete application or physical desktop compatibility.

The user-approved performance workload is 6000 x 4000 scientific frames, Gray16 and RGB48 measured separately, at one full-frame update per second with interactive zoom/pan. Measure Windows reference and Linux results, including startup, input-to-binding/presentation latency, allocations and post-warmup memory growth, before setting numerical budgets. The small correctness fixture does not establish large-frame throughput or an overnight bound.

Matching Windows SoftwareOnly and Linux CPU-backend runs completed three warmups
and 30 measured updates per format, plus six separate dirty-only updates. The initial runs below
use the same extracted algorithms, actual 1184x811 viewport at scale 1 and exact
full-size 16-bit validation. The Ryzen 9 7950X host also runs WSL2 with 32 logical
CPUs; host activity was not isolated. Linux execution uses Xvfb and the freshly
qualified Phase 2 package bytes. The refreshed foundation run follows below.

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
30-frame workload was subsequently rerun on the qualified foundation package.

That package-only rerun retains the original workload and viewport helper,
three warmups, 30 measured full transactions per format and six dirty-only
transactions per format. All six processes exit 0 and all 55 runtime assets
match the released package. Mean full callback transactions are 370.75 ms
Gray16 and 437.52 ms RGB48; p95 is 445.72/628.75 ms and maximum is
461.59/675.97 ms. All 60 full transactions finish below 1000 ms, with maximum
start lateness 3.61/4.10 ms. Dirty-only means are 475.21/482.68 ms. Exact
whole-buffer scientific source and caller-ownership checks pass. Independent
review recomputes all timing/memory rows and image comparisons from raw data.

This establishes a bounded WSL2/Xvfb callback cadence, not monitor presentation
latency or an overnight memory result. Sampled private peaks are 747.75 MiB
Gray16 and 1494.13 MiB RGB48. Initial Gray16 whole-image differences remain
unresolved against the stock offscreen capture, while final Gray16 and all
RGB48 comparisons differ by at most 2/255. One dirty Gray16 native sample
differs by 4/255; the preserved stock native/offscreen pair itself differs by
6/255 there. Neither golden data nor tolerances were changed. Raw results,
conditions and comparison limits are retained in
`artifacts/portability/scientific-workload-reference/foundation-5D66DB6D4D59291D/RESULTS-canonical-1.md`.

A later stock Gray16 capture diagnosis keeps source bytes and geometry fixed.
Settled and repeated RenderTargetBitmap captures are byte-identical; the initial
capture differs transiently. Linux differs from the settled offscreen comparison
by at most 2/255. Three native 22-point grids are stable, but only 20 points match
the settled RTB: two retain differences of 38 and 31 levels. Independent review
rehashes the raw records and reproduces these observations. This narrows the
reference problem without clearing the display gate, proving a whole native
raster or establishing the cause of the remaining differences.

In the initial pre-repair run, the 18 paired native pixel samples differ by up to 57/255. All 18 Windows samples
match their stock offscreen references, while Linux's first Gray16 center is 4
versus Windows 53. Source bit depth, channel order and complete readback remain
exact. Display parity is unqualified; expected pixels have not been changed to
hide this result.

The user subsequently accepted documented, bounded backend sampling differences
while preserving WPF mode/state semantics and exact scientific source values.
Calibration separates format-specific interior interpolation, geometry-derived
edge coverage and coordinate-justified nearest-texel boundary choices. Observed
1-2/255 interpolation and up to 17/255 edge-coverage differences are corpus
measurements, not a global tolerance. Commit `dc621a155` implements
inherited Linear and NearestNeighbor modes for visuals and retained drawing
groups, including state restoration after exceptions. The larger axis-aligned
CPU corpus passes 157 checks on Windows and Linux, with 145 stock-reference
checks. Interior interpolation uses case-specific bounds up to 3/255. Edge
colors use each backend's genuine geometry coverage control, independently
checked against geometry; raw coverage differences reach 32/255 in this corpus.
Adjacent nearest-texel choices are accepted only within precomputed coordinate
uncertainty intervals, never through a global pixel-error allowance. Complete
scientific source buffers remain exact. Independent review found a test blind
spot where a blank image and blank coverage control could pass together; the
repair rejects that case and 23 related corruptions without production changes.
The original failures and both reviews remain preserved. These Linear/Nearest
results do not qualify HighQuality/Fant.

Commit `c7902d7d1` implements HighQuality/Fant using native trilinear mip filtering
over an immutable private display copy, with draw-local image/shader ownership.
Source and caller scientific buffers remain untouched. The expanded sampling
corpus passes 192 checks per OS and 179 stock-reference checks; rendering 19,
geometry 268, text 344 and imaging 12 also pass per OS. Independent copied
headless replays pass 141 checks per OS and verify all 306 raster/coverage files.
Quality assertions reject a real Linear fallback and retain mode inheritance,
mutation, clipping, alpha and exception recovery. The native Skia quality profile
documents weaker reductions and possible native mip allocation fallback. It
does not promise Windows pixel parity. Fresh package, native-window and
performance checks of HighQuality/Fant remain pending.

The timings end at dispatcher/render callbacks, not confirmed presentation.
Native observations occur outside those timers and do not prove the latest
submitted frame. Memory includes retained 48/144 MB source and validation arrays;
Linux private counters are not equivalent to Windows commit. Short runs, bitmap
replacements and in-scope scene objects prevent leak or overnight conclusions.
Numerical latency, allocation and memory budgets remain unset. Raw runs, matching
source hashes and limitations are under
`artifacts/portability/scientific-workload-reference/countermeasurement`; the
earlier canonical Windows baseline remains unchanged.

Commit `d76a12ce9` builds the complete pinned original
`System.Private.Windows.Core` source as portable IL. Its 256 C# files are
unchanged, with original generator inputs, resources, public identity and friend
assemblies. This replaces the Windows ReadyToRun image-format barrier without
duplicating WPF's managed data store or enabling OLE on Linux. Default-CLR
managed DataObject construction and interface access pass on both OSes without a
ReadyToRun environment switch. All 261 extracted source files match their
pinned Git blobs, all 46 resource strings match the official helper and
independent review passes. Commit `13b3b14ba` selects that helper through the
normal project graph, retains genuine managed DataObject storage and JSON
retrieval and promotes the helper into the next runtime profile. Both default-CLR
managed smokes pass, with no generator version substitution. Independent review
also caught and closed an archive audit that inspected unmatched loose helper
bytes. The newer 54-managed-asset profile is qualified separately below.

The native clipboard source slice passes nine groups and four manager-specific
checks. Independent review reproduced an indefinite wait after successful
session ownership; the one-hunk pinned backend repair now bounds only optional
manager acknowledgement to one second. Actual Cut, external Unicode text,
replacement and clear pass independent replay. Native Open/Save source checks
pass 31 cases through real WPF APIs on a private portal bus. Review caught and
closed a hidden-owner cleanup defect with one guard change and three real
regressions. These results retain ordinary native filename and session-clipboard
behavior. The committed `804d13bc3` package then passes all 17 distribution
records, plus package-only clipboard 9/9, manager 4/4 and portal protocol 31/31
checks. All 3,849 actual producer inputs and 56 runtime assets match their
manifests, including the IL-only helper under ordinary CoreCLR settings.

| Native desktop package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `F4CB5C5872FD33D265332E69D3CDA9BE8F39A01D2B772182FA500A3A45A70D1F` |
| Wpf.Portable.Sdk | `8A23104303AF54108E92765F82DB056A01E60CB75329B3D18EF12C6683189112` |

These 804 packages exclude the later HighQuality/Fant, Unicode input, IME and
accessibility work. Their first live GNOME service probe passes package startup
and pointer provenance but stops while rendering its ordinary Classic TextBox,
before any clipboard command or native picker. Settings bytes and service owners
stay unchanged and the process is reaped. That failed probe remains preserved.

Commit `b49780026` closes the reproduced visual-order boundary. Classic's actual
visual guides alternate opposite edges and are finite; the native Windows visual
transport copies and sorts each axis before building its guideline frame. The
portable visual path now does the same without mutating original collections or
relaxing DrawingContext static/dynamic validation. Both portable OSes pass 150
checks and stock passes 131. A diagnostic copy of the original app passes its
previous TextBox-frame check with only repaired Core substituted, then stops at
an unrelated WSL child-launch path. That overlay is diagnostic evidence, not a
fresh package or live desktop service pass.

Commit `f7dcb2312` routes Icon, Caption, SmallCaption, Menu and Status font roles
through the existing declared Message policy: `Global User Interface`, 11 DIPs,
Normal style/weight and frozen empty decorations. The portable Classic
`DropShadow=false` policy remains overrideable through its genuine resource key.
These are product defaults, not detected desktop preferences. Three settings
profiles pass 27 checks each per OS, with four genuine Classic offscreen layout
cases and independent replays. Native Popup presentation remains unqualified;
normal Windows font/shadow branches are unchanged.

The fresh f7 package is independently qualified and root-released under
`artifacts/portability/distribution-desktop-services-retry-1`. All 17 distribution
records pass; root rehashes 3,850 actual build inputs and 7,766 clean tracked
inputs. Package-only default-CLR helper, clipboard 9, manager 4 and private
portal 31 checks pass. This feed includes the earlier HQ/Fant, Unicode commits,
managed peers and visual-order repair. It excludes native AT-SPI `9bd249881`,
uniform conic translation `9a90ed9b2`, VisualBrush and later preedit work. The
subsequent live GNOME probe 3 passes genuine bound-TextBox Copy/Cut/Paste and
all four dialogs return with native owner modality restored. Strict Open/Save
path and cancellation postconditions still fail, followed by an unrelated
fixture `XQueryTree` failure. Public filenames are not captured portal wire
URIs, so these observations do not prove a production selection-mapping defect.
The user subsequently confirmed intentionally selecting README in the first Open
dialog and clicking Save in the fourth dialog, which the fixture expected to
cancel. Those two failed postconditions match the reported actions and are not
evidence of production path-mapping or cancellation defects. The first Save
filename is not remembered, so that postcondition remains inconclusive. The
raw failed run is unchanged and no retry is running or requested. Raw settings
and service-owner records are preserved, exact app PIDs are gone and earlier
probe failures remain immutable. The live picker workflow has not passed.

An isolated diagnostic reproduces the global recursive X11 lookup race against
unrelated destroyed windows. Its owned-XID/DestroyNotify proposal passes 22
private native protocol checks and compiles the proposed full app with zero
warnings/errors. The WPF owned-handle getter has not executed in that proposal,
and no live payload was staged or run. This is a fixture repair proposal, not a
reclassification of the preserved picker results or proof of the original failed
XID's identity.

| Font/guideline desktop retry package | SHA-256 |
| --- | --- |
| Wpf.Portable.Runtime | `739D8950F21BC1AB654A64E629F5733B5E563736C19D859B264837A778EB9DC8` |
| Wpf.Portable.Sdk | `4A15DA9A7CADED93AD131BE5C6DF3B221C5CF4C8AF191947489E468BC26345AC` |

Commit `55287c201` preserves each complete, well-formed native UTF-16 commit
through the existing WPF composition route. Independent replays and fresh
combined-source checks pass on both OSes, including real TextBox selection,
binding and one undo/redo. Linux callback/control-key checks pass 5/5 and the
existing native suite passes 23/23; clipboard 9, manager 4 and portal 31 also
pass on that same combined host. This proves the native callback-to-editor
seam, not an actual IME engine or preedit session. Accepted-Tab layout, one
terminal-LF selection case and uncovered font glyphs remain explicit neighboring
limits. The ordinary Windows build/pack after the desktop, Unicode and managed
peer integration passes with zero warnings/errors and all 208 upstream
threading checks pass.

Commit `2cf305d65` enables genuine managed Button, TextBox, CheckBox and Window
automation peers. Ten peer/metadata/action checks pass on each OS, with real
commands, binding-preserving Value changes and Name/Label/Title precedence.
Native source bounds, movement, focus and close pass at 96 and 192 DPI. The
combined graph retains the existing input, Unicode and desktop protocol results.
This is the managed peer foundation. Native Windows UIA export still fails
explicitly; the later Linux Window/Button transport has its own bounded result.

Commit `9bd249881` connects genuine WPF Window/Button peers to native AT-SPI and
actual Invoke commands. Its combined-source freeze runs eight modes on private
buses and Xvfb: AT-SPI protocol 9, ordinary native window 23 with and without the
service, Unicode callbacks 5, managed automation 3, clipboard 9, manager 4 and
private portal 31. Both OS graphs build with zero errors and 13 retained warnings.
The service-enabled raw output retains its known registry startup prefix;
canonical no-service runs explicitly disable activation instead of inheriting an
ambient bus. Independent reviews/replays pass. Text/value/editable-text export,
dynamic-tree/property notifications, Orca and GNOME accessibility are not
qualified by this Window/Button slice.

Commit `9a90ed9b2` recognizes when every offset in both guideline axes is exactly
uniform and reuses the existing device-translation path. This preserves genuine
Skia rounded/ellipse conics without approximation or dropping guides. Both OSes
pass 166 checks and stock passes 142, including paired translations, DPI and
explicit nonuniform failures. Independent Lisa and Scrooge replays pass. General
nonuniform conic snapping remains unsupported, and post-fix NINA ImageView,
package and native presentation checks are pending.

Commit `d85b4fc74` closes the bounded retained VisualBrush auto-layout and recovery
slice. The combined source graph passes 227 full guideline checks and 61 focused
VisualBrush checks per OS, plus Rendering 19, Geometry 268, Text 344 and Imaging
12 per OS. Linux native Window 23 also passes with the same Core bytes. All 13
processes exit zero with empty stderr; builds have zero errors and retain their
recorded warnings. Non-tiled fills are qualified within the explicit profile;
tiling, effects, opacity masks, pen/glyph foreground and recursive sources remain
named unsupported boundaries. This source graph is not a released package or
physical desktop qualification.

Commit `a18122207` supplies observed primary-screen dimensions as an immutable
positive finite DIP pair, with explicit failure when no observation exists.
The native host acquires the observation before WPF settings consumption.
Classic animation/repeat values are declared product defaults rather than
desktop preference discovery. Five 30-check settings profiles, the unavailable
screen check and four theme/layout cases pass on each OS, totaling 155 per OS.
The corrected private-Xvfb follow-up passes 11 processes with 114 named checks
and 20 threshold observations. Prior runner/display-start failures remain
preserved. This is a source graph, not package, multiscreen or live monitor-change
qualification. The original Windows build/pack passes with zero warnings/errors
and all 208 upstream threading tests pass after this integration.

The accepted hidden-composition contract restores original selected text while
retaining the generation, latest candidate/caret and deferred automatic binding
ownership. Show resumes it; hidden Commit replaces the original range once with
one undo/redo step. Application text, selection and binding intervention wins.
Commit `8fbb2a7a3` preserves the exact five independently reviewed files. Its
final combined graph passes managed preedit 30, automation 10 and Core input 36
per OS, plus all nine existing regression processes per OS. Both separate native
graphs build successfully. Linux's eight private native modes pass: ordinary
Window 23 with and without service, Unicode 5, AT-SPI 9, managed peers 3,
clipboard 9, manager 4 and portal 31, totaling 107 checks. Fourteen final PDB
source checks match, and the freeze pins 658 runtime files and 1,154 derived
inputs. Root rehashes 219 evidence records, 13 manifests and all five main source
files. Della and Foghorn report no supported findings. Existing build warnings,
native registry prefixes and corrected setup/runner failures remain recorded;
they are not discarded or reported as zero-warning portable graphs. Native
focused-editor lease/caret attachment, real IME engines and a fresh package/live
desktop replay remain unqualified.

An earlier genuine Classic ComboBox mouse click records a separate boundary:
at that checkpoint `ItemsControl.AutoScrollTimeout` reaches user32
`GetDoubleClickTime` before native Popup construction. The expected-red diagnostic preserves the original
template and real input path. Phases 2 through 5 remain open.

## Committed transport and rendering milestones

Commit `db3162527` integrates the controlled native IBus transport/factory slice.
Its [combined freeze](../../artifacts/portability/native-ibus-combined-main-1/combined-freeze.json)
passes 18 private protocol/factory processes with 48 checks and ten existing
native regression processes with 111 checks. Both complete native graphs build
with zero errors and 14 retained warnings each. This qualifies transport against
the controlled private counterpart, not an actual composing engine, focused
WPF editor attachment, package or physical desktop.

The original IBus run compiled the pre-normalization fixture recorded in its
PDB. A [two-byte EOF normalization](../../artifacts/portability/native-ibus-combined-main-1/root-fixture-eof-normalization.json)
then removed one trailing empty line, preserving all C# tokens and production
bytes. That original PDB is not claimed to contain the final fixture bytes.
The newer rendering/dash graphs compile the current fixture content with the
archived newline convention; those copies match main after CRLF/LF normalization.
The original run, source hashes and caveat remain preserved.

Commit `15c7c697c` adds bounded Geometry.Combine for the four Boolean modes,
operand/result transforms, fill rules, empty and touching contours and the
declared supported curve inputs. The backend polynomial solver has its own
float-precision/tolerance profile, not universal Windows topology or raster
parity. Existing named precision/unsupported-input diagnostics remain. The
[coherent renderer freeze](../../artifacts/portability/rendering-combined-main-1/combined-freeze.json)
passes eight managed processes with 1,308 checks per OS, including Geometry 355,
plus 21 private native processes with 75 checks. The
[root stage release](../../artifacts/portability/rendering-combined-main-1/root-stage-release.json)
permits whole-stage diagnostics, not package or full application qualification.
Original Windows build/pack passes with zero warnings/errors and threading
208/208. Its initial inherited Perl-locale failure is retained; only the
process-local C-locale retry is counted as passed.

Commit `b81c905aa` closes the invisible-subtree traversal slice in that same graph.
Hidden/Collapsed and zero-opacity subtrees skip retained content and descendants;
visible unattached roots still paint. Round trips restore pixels, unsupported
content in skipped subtrees is not visited and siblings retain their state.
VisualBrush checks distinguish stale dependencies of skipped consumers from
dependencies needed to observe an invisible source's recovery. Focused Rendering
visibility 27 and the combined Rendering 46, Guideline 253 and VisualBrush 61
results are scoped to that graph. The Text runner's first cwd mistake and failed
freeze remain recorded; only the corrected isolated Text reruns are final gates.

Commit `81ad28a6d` adds bounded dashed painting and retained Pen/DashStyle mutation
coverage. The [final combined freeze](../../artifacts/portability/dashed-painting-combined-main-1/combined-freeze.json)
passes all 44 gates and 3,183 checks: 1,431 managed checks per OS, 75 private native
checks and 246 stock checks. All 28 portable project builds pass with zero errors
and 14 retained warnings per OS; the stock build has zero warnings/errors.
The [handoff](../../artifacts/portability/dashed-painting-combined-main-1/HANDOFF.md)
records the fixed stock-runner argument correction and preserves its initial
outputs. This run does not add a new upstream threading result.

The dash profile covers finite supported patterns, phase in both directions,
contour starts, calibrated caps, clear/restore and failure recovery. The actual
zero-radius Classic focus contour uses the correct top-left start. Explicit
unsupported profiles remain named failures. True rounded/ellipse dash phase,
arbitrary dash profiles, exact full fractional stock raster equivalence and
dashed Geometry.StrokeContains/GetRenderBounds remain unqualified. Painting does
not relax the existing geometry-query diagnostics.

| Frozen source evidence | SHA-256 |
| --- | --- |
| IBus combined freeze | `12B0B9A31A8493B01E55D76788B1D33BBBAF74EEDDFF64CC6BC15C01A031B435` |
| Geometry/visibility combined freeze | `8F0A59B6FD261F839663C0E2B68C40AFADE46E3137CC2E82AFB6AEBC2F5C8E0D` |
| Geometry/visibility root stage release | `AECB0E9C4D1CACC1B559ECA8A2E9DA3823C167A05468FB8681D4218D925646AE` |
| Dashed-painting combined freeze | `1B2512783703CEAB4238C71D473D51F482F2FF9D9F26A34CD94798481BD58FD0` |
| [Dashed-painting root stage release](../../artifacts/portability/dashed-painting-combined-main-1/root-stage-release.json) | `DA3FD65DC08206DE38598464EC3FA0B3187E5865D2DC36844385CE3DC84B8AF7` |

All 932 evidence records listed by these three freezes rehash at this checkpoint.
The current source ledger appends the four logical milestones and later NINA
diagnostic without replacing their historical negatives. These source-built
milestones pass within their stated scopes. Phases 0 and 1 remain bounded passes;
Phases 2 through 5 remain open. No new package, full NINA, live desktop, real IME
engine or AvalonDock behavior gate is established here.

## Later committed source milestones, 19 October

Commit `cc728889b62dbed70553e815766d868cac6742d4` adds bounded nonuniform rounded
fills. The three direct walker fill sites reuse original WPF cubic paths for
rounded primitives, RectangleGeometry fills and VisualBrush destination masks.
No/uniform guideline frames retain Skia conics. General conversion, geometry
queries, clipping, rounded strokes and curved dash phase are unchanged.
The [source-equivalence proof](../../artifacts/portability/cubic-fill-integration-main-1/source-equivalence.json)
establishes exact reuse of the reviewed owner source corpus, not a fresh combined
runtime. Both owner OSes pass focused cubic fills 23, full guidelines 324,
Rendering 47, Geometry 355, dashes 48, visibility 26 and VisualBrush 61. Stock
focused 22 and full 216 pass. Independent correctness replays pass focused 23
per portable OS and stock 22; the test-value review repeats full guidelines 324,
Rendering 47 and the causal red with 20 expected failures per OS. Both reviews
have no supported findings. The owner records its corrected fixture/setup and
runner issues. No fresh native/Classic stage, original NINA cubic replay,
package or physical desktop pass is established.

Commit `f6b152ca146bcd1cda7c265ca976dcf7fb30ea38` preserves the original public
ShowInTaskbar value before source creation and forwards live/hidden changes
through the existing peer without source recreation or a fabricated owner.
Its [source-equivalence proof](../../artifacts/portability/taskbar-integration-main-1/source-equivalence.json)
and [commit verification](../../artifacts/portability/taskbar-integration-main-1/commit-verification.json)
bind all eleven exact reviewed inputs. Owner lifecycle 68 and managed preedit 30
pass on both OSes; native taskbar 4 and ordinary native 23 pass. Independent
reviews repeat lifecycle/native and preedit respectively with no supported
findings. The private Xvfb observations cover initial/hidden hints and mapped
EWMH requests, not WM acknowledgement, desktop taskbar or Alt-Tab presentation.
Fixture-friend lifecycle closures must not be staged. A fresh combined graph,
AvalonDock Float and package qualification remain pending. Bounds REV2 is not
integrated or certified here.

| New source evidence | SHA-256 |
| --- | --- |
| Cubic source equivalence | `688DCBEA6AFAEEE611BFF138678079C797F1B2F8F44B3056BAAAA932D2476142` |
| [Cubic owner freeze](../../artifacts/portability/native-cubic-fills/review-freeze.json) | `2EDC097FC693351D0A01B357E790DB874D68C7A63D5FE51F63D1287A6539F3B2` |
| [Cubic correctness review](../../artifacts/portability/della-native-cubic-fill-review/review-results.json) | `5C88AEC7497598748FD2694EAF649D92C1D75E953914FCBA702AE4BC4806C261` |
| [Cubic test-value review](../../artifacts/portability/foghorn-native-cubic-fill-review/review-summary.json) | `C4167D8183E808509BA684DD299E00C078FE40EF605F266AB7A8A3E9C6DB6217` |
| Taskbar source equivalence | `1C8F276F9662A4BC5D4F447E2D65AC188D3D23605C77B44641F97570816D50F0` |
| [Taskbar owner freeze, verified copy](../../artifacts/portability/feasibility-checkpoint-20261019/evidence/taskbar-owner/freeze.json) | `E96AC9A13BFC0149D15C973E0A397AF35AE7045771F7E0DA8481FCEAC0117F19` |
| [Taskbar correctness review](../../artifacts/portability/della-taskbar-visibility-review/review-results.json) | `9FF3FA68A249CB4E4B54890A57D0B2EF8712631BE6AB43C132FFE8980211AAB5` |
| [Taskbar test-value review](../../artifacts/portability/foghorn-taskbar-visibility-review/review-summary.json) | `1803BAB63007D92001C8EC907557EB36143A9B597AC57FB3C2E293421BFD4A6B` |

The [original profile-selector diagnostic](../../artifacts/portability/nina-profile-selection-diagnostic/revision-1/verification.json)
stops at the original ProfileSelectView's Microsoft.Win32.SystemEvents assembly
dependency, with zero framework native-load failures. It does not pass original
selector construction or interaction. The corrected
[stock REV2 reference](../../artifacts/portability/nina-profile-selection-diagnostic/revision-2/runtime-results-1/result.json)
passes choose-forward 22 and fresh-process reload 7 on WindowsDesktop 10.0.12.
Original bindings are Unattached before Show and Active against the actual VM
after genuine Show, layout and dispatcher settlement. Selection persistence,
disk readback and released file locks pass. Both private-desktop children exit
zero with empty stderr and verified cleanup. Build warnings remain explicit:
stock 2, portable 1 and launcher 0. Preferences-file persistence, cancel, reverse,
full App startup, native input, the portable workflow and package qualification
remain open. The Linux SystemEvents boundary and earlier incomplete evidence
are unchanged; Phases 2 through 5 remain open.

## Bounds combined release and Grid component integration

Commit `d18213a4a1747087ba068dddd9bc54af4940bf65` integrates bounded dashed render
bounds and the REV2 zero-radius rectangle stroke-route repair. Supported bounds
reuse the actual dashed outline with the declared fill/transform/fallback
semantics. The REV2 audit records 28 exact raster and 25 exact bounds comparisons;
three all-gap routes retain a documented preexisting conservative-bounds
difference. Dashed containment and widened geometry remain unqualified.
The [combined freeze](../../artifacts/portability/combined-renderer-window-main-1/revision1/combined-freeze.json)
passes 3,341 checks across 28 runs and all 32 builds with zero errors. Ordinary
families pass 1,559 checks per OS, including Geometry 437; separate fixture-only
families pass lifecycle 68 and managed preedit 30 per OS. Linux native Window 23
and taskbar hints/requests 4 pass. Existing warnings remain explicit: ordinary
builds 14 per OS and verification builds 10 per OS.

The [root inspection](../../artifacts/portability/combined-renderer-window-main-1/revision1/root-inspection.json)
and [consumer release](../../artifacts/portability/combined-renderer-window-main-1/revision1/root-consumer-release.json)
bind all 35 exact source overlays and two ordinary 143-file stages. Fixture-friend
Core/PF variants are excluded from those stages. Only whole unchanged runtime
families are released for actual NINA diagnostics. This supersedes the earlier
owner-only pending-stage status without changing those historical records.
It is not a package, full application or physical desktop qualification.

Commit `b6569d9a43ae29dc881af8a168c1ab5a4346eb0f` adds bounds for one open hollow
figure containing isolated noncollapsed linear runs separated by explicit gaps,
with matching Flat caps. Query-origin and skipped-pen semantics are preserved;
converter, dasher and painting remain unchanged. Its
[source-equivalence proof](../../artifacts/portability/grid-bounds-integration-main-1/source-equivalence.json)
and [commit verification](../../artifacts/portability/grid-bounds-integration-main-1/commit-verification.json)
bind nine exact reviewed files and 30 unchanged seed files. Geometry 615 passes
per OS, retaining all previous 437 checks; Rendering 47 per OS and stock 570
also pass. Independent correctness and test-value reviews have no supported
findings. The component records 144 exact stock queries and 16 target rasters.
A preexisting skewed translucent multi-contour/control raster difference remains
308 pixels, 1,142 channels and maximum 15, with unchanged pre/fixed hashes and no
new tolerance. No-ink conservative bounds, dashed containment rejection and
unqualified native widening remain explicit limits.

| New frozen evidence | SHA-256 |
| --- | --- |
| Bounds combined freeze | `6E707788C5308623C962CD6797F2B58D4B0C81C70A629B926B16C9E3B1D8987E` |
| Bounds root inspection | `329D1EDDEF1FF8EDBF67E42F7D724F259DF1715036EF0660A7A0886473BFB669` |
| Bounds root consumer release | `F91C9D04129CECA89F46977C47E864D44CA7A92FC31DB4E003879FCDB885CA22` |
| Grid source equivalence | `6D38FE4DCC6B66D1822E8E3F937B6EEAC6FEB41628EBE753B65DCBB3CA27FC47` |
| [Grid owner freeze](../../artifacts/portability/grid-bounds-implementation/freeze.json) | `D91F643839DADD99A64A99123DAE688C20EFC88F602FB7B5FE09CA6963E71C8D` |
| [Grid correctness review](../../artifacts/portability/webby-grid-bounds-review/review-summary.json) | `42F9C0B7ED5607A8C55675E5C6C085ACE22D3DE92AE4B7BA0CB863C77715C95F` |
| [Grid test-value review](../../artifacts/portability/donald-grid-bounds-review/review-summary.json) | `516F8810CE58DA385B6DC560B93AC230BF688D6E789A60F56E98EF88840FE5C6` |

At that capture-time checkpoint, the released Bounds stage excluded Grid and
Popup revision 2. Grid's native stage and actual AvalonDock replay were pending,
and Popup cleanup was under review. The stock profile 29-check reference and
Linux SystemEvents failure retain their separate scopes. Current component and
capture results follow below; these historical records are not reclassified.

## Original ImageControlVM offscreen Linux result

The unchanged ImageControlVM/ImageControlView/ImageView now
[passes all 26 ordered checks](../../artifacts/portability/nina-imagecontrolvm-combined-renderer-window-diagnostic/revision-1/verification.json)
on the whole released `d18213a4a` Linux family. The original 160 PE files,
54 source blobs, 33 dictionaries and 143 framework-stage files verify. All 26
names match the stock reference and all 18 logical/source frame contracts agree.
Real public commands and retained bindings exercise 6000x4000 Gray16/Rgb48,
four rotations, flip on/off, crosshair on/off, image removal and VM disposal.
Image-body rasters change with operations and restore exactly; source identity
and CopyPixels remain exact. This is not cross-OS exact raster or HQ equivalence.

The offscreen run exits zero with zero native failures and empty stderr. Build
records zero errors and one existing CS8602 warning. The unchanged historical
Program mode label still names `a181`; the consumption pin, verification sidecar
and actual output hashes identify the consumed `d182` family. The preserved
preflight metadata mismatch was corrected only in bookkeeping before run-02.
No NINA, fixture assertion, BAML, template or framework binary was overlaid.
The original seven-pass/one-failure/18-unreached conic red remains unchanged.

| ImageControlVM evidence | SHA-256 |
| --- | --- |
| Verification | `F8425AE4ACF15D61C17CA1BEE15091987C4A464DD96B3729A232227F825757BD` |
| [Runtime result](../../artifacts/portability/nina-imagecontrolvm-combined-renderer-window-diagnostic/revision-1/run-02/runtime-linux.json) | `F993FBE8B0E83167057719FBC23FF9BA932933AB39DE7FBA0FAB796EDAACE1B5` |
| [Consumption pin](../../artifacts/portability/nina-imagecontrolvm-combined-renderer-window-diagnostic/revision-1/consumption-pin.json) | `89C094AC475A44ED342CCDA14E66D79A433A57721FEDE87E80837A6EA147CF71` |
| [1,052-record evidence manifest](../../artifacts/portability/nina-imagecontrolvm-combined-renderer-window-diagnostic/revision-1/evidence.sha256.json) | `5B82EB4F4C28D64A68913A13FF774C4B76FA9753DC84591AA3FB53767857B8F3` |

This establishes original command/binding operations and offscreen image pixels.
It uses no Show or native input and does not qualify fit/1:1/pan, acquisition,
PrepareImage/star processing, 1 Hz throughput, the shell or a package. Earlier
checkpoint descriptions of a running replay are historical capture-time status;
the completed result above is current. Phases 2 through 5 remain open.

## Empty ImageBrush and programmatic docking update

Commit `fb38f5f45` makes only an ImageBrush with null ImageSource paint transparent;
nonempty ImageBrush remains named unsupported. The
[focused source evidence](../../artifacts/portability/empty-image-brush/payload-manifest.json)
preserves exactly two new failures against the unchanged c5 runtime, with all
previous 47 checks passing, then 49/49 green on each OS. The
[coherent ordinary Release family](../../artifacts/portability/empty-image-brush-release-main-1/revision1/root-consumer-release.json)
passes Rendering 49 and native Window 23 with the whole standard 143-file stage.

The preceding NINA native-imaging revision 3 runs map the initial Window, but both
Gray16 and Rgb48 stop with eight passes, one failure and 39 unreached checks at
the first Fit click. The [Gray16 observation diagnostic](../../artifacts/portability/nina-native-imaging-preparation/revision-3/click-diagnostic-1/findings.md)
confirms the correct Button receives all scoped events, yet IsPressed remains
false, capture is null and Click count stays zero. All other completion conjuncts
are true. Subsequent disposable adapter instrumentation reaches CaptureMouse for
the actual held inside press and records core XGrabPointer=AlreadyGrabbed. The
core-grab/XI2-input pairing identified the repair boundary. Those failed runs
remain unchanged; the original 48-check native imaging workflow has not passed.
The earlier offscreen 26-check result retains its separate scope.

Commit `d4073e3e3` repairs the existing adapter's selected XI2 master grab/release
path without a public API change. Its
[compact handoff](../../artifacts/portability/native-held-capture/handoff.json)
retains the old-product held-inside Button red with 22 passes and one failure.
Final Linux native checks pass 42/42: ordinary 24, Popup 11, placement 3 with 12
internal observations and taskbar 4. The final two sources compile in four
affected projects with each Linux and Windows compiler, with zero errors and
recorded existing warnings. Correctness and test-value reviews are clear,
including an outside-pointer oracle based on actual bounds because IsMouseOver
stays true under capture. The
[root integration](../../artifacts/portability/native-held-capture/root-integration.json)
and [consumer release](../../artifacts/portability/native-held-capture/root-consumer-release.json)
bind the ordinary 143-file stage, 137 unchanged members and six expected native
DLL/PDB changes. That release preceded the subsequent original NINA replay
described below; it did not itself qualify full imaging or complete a phase.

Commit `2114907fc` adds the pinned source-adapted AvalonDock component and its
nine-check original-NINA-view fixture. The
[achieved-resize Release run](../../artifacts/portability/avalondock-implementation/achieved-resize-release-1/freeze.json)
passes 9/9 with native grow/shrink, six settled container stages and 12 view
round-trips, restoring both original views' content/DataContext identities
through docked XML. The earlier Debug assertion red remains preserved. Native
pointer docking, auto-hide and overlays remain open; the revised stock wrapper
is compiled only. This is programmatic source-adapted component evidence, not
untouched AvalonDock binary, full shell or package qualification. Phases 2
through 5 remain open.

Commit `27cb88615` repairs mixed-path bounds through one private helper that
unions raw line endpoints with Skia tight curve extrema. The original crosshair
previously reported Bounds 0.65 instead of the unit extent and now stretches to
the actual Path's 3,999-unit span. Ten stock-backed tests preserve the old-family
causal red: 625 total, 617 passes and eight new failures. Final Geometry 625 and
Rendering 49 pass on both OSes, with stock 580 and Linux native 42 passing. All
20 Release builds have zero errors; 14 PDB/runtime records and four ordinary
isolation records are clear. The
[consumer release](../../artifacts/portability/crosshair-bounds-combined-main-1/revision1/root-consumer-release.json)
binds the [build freeze](../../artifacts/portability/crosshair-bounds-combined-main-1/revision1/build-freeze.json)
and whole ordinary families.

The unchanged ten-input, 48-check original NINA revision 5 replay clears both
crosshair pairs and all preceding native controls and warmups, but the workload
does not pass. Both first failures are retained without retries:

| Format | Pass / fail / unreached | First missed frame | Native seen / next deadline (ms) | Full transaction (ms) |
| --- | --- | --- | --- | --- |
| Gray16 | 38 / 1 / 9 | 8 | 8651.746 / 9000 | 9052.991 |
| Rgb48 | 30 / 1 / 17 | 0 | 1075.813 / 1000 | 1733.335 |

Rgb48's first-frame synthetic preparation takes 444.074 ms. Native observation,
verification and input transactions are measured boundaries, not isolated
framework latency; no extra performance budgets or bottleneck attribution are
introduced. Revision 5's consumer is Debug (Debuggable 0x107,
DisableOptimizations) against the Release framework. Revision 6 recompiles only
the identical original ten-input/48-check/F03 consumer in Release against the
same 143-file family, with no source, cadence, sample or input change. Explicit
Configuration=Release and Optimize=true are backed by consumer PE Debuggable
0x2 metadata. Both once-per-format Release runs still fail complete cadence:

| Format | Pass / fail / unreached | First missed frame | Native seen / next deadline (ms) | Exact verification (ms) | Full transaction (ms) |
| --- | --- | --- | --- | --- | --- |
| Gray16 | 39 / 1 / 8 | 9 | 9827.897 / 10000 | 63.695 | 10213.742 |
| Rgb48 | 30 / 1 / 17 | 0 | 794.084 / 1000 | 158.067 | 1207.368 |

Release Rgb48 synthetic preparation takes 194.480 ms. Release materially improves
the recorded timing; Debug configuration does not fully explain the remaining
complete-cadence failures.
All preceding native controls, crosshair pairs and two warmups pass. Compact
[revision 6 results](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision6/original-replay-results.json),
[consumer PE metadata](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision6/consumer-pe-metadata.json)
and [cleanup](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision6/replay-cleanup.json)
retain zero native failures, empty stderr and absent owned replay/display
processes. Revision 5's first failures remain historical. No isolated framework
latency attribution or extra budget follows. Those revision 6 observations
preceded the fixture-only batched capture below. The earlier
short callback-cadence result has a different scope. Compact
[replay results](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision5/original-replay-results.json)
and [cleanup](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision5/replay-cleanup.json)
record the failures and exited owned replay/display processes. Full shell is
unlaunched. All 33-dictionary results here use the selected original guider/view
test closure, not the full 34-source App startup closure. Phases 2 through 5
remain open.

Revision 7 changes two of ten fixture copies only: batched X11 ReadPixels and
pre-workload stable parity with the legacy sampler. The other eight inputs,
F03 runner, all 48 assertions/timings, full scientific sources and native input
remain unchanged. It uses the same 143-file `27cb` family/root release, original
160 PE closure and explicit Release consumer with four PDB/source matches.
All three parity states match exactly at the same 66 window/root points outside
the timed workload.

The [revision 7 results](C:/Users/isbeo/AppData/Local/Temp/wpf-nina-native-imaging-revision7/original-replay-results.json)
record Gray16 48/48 with all 16 updates, worst complete transaction 889.942 ms
after due and minimum native deadline margin 478.343 ms. Rgb48 remains
30 passes, one failure and 17 unreached: its first frame is seen natively at
676.362 ms before the 1000 ms deadline, but completes at 1061.499 ms, 61.499 ms
late. Preparation is 184.248 ms, setter 137.460 ms, exact verification 142.844 ms
and input/remainder 242.293 ms. These boundaries do not isolate framework
latency and budgets remain unset. Both runs have empty stderr, zero native
failures and exited owned PIDs/displays. Revisions 4/5/6 reds remain historical.
The user explicitly accepts the observed 1061.499 ms complete transaction and
676.362 ms native first frame as the native Rgb48 timing outcome and requests
no further optimization. This is an authoritative scope decision, not a changed
test result: 30 passes, one failure and 17 unreached remain recorded, and no
overall Rgb48 pass is claimed. The frozen conversion cache is canceled and
unimplemented. Work continues toward full Phase 5 through the other actual
gates; unexecuted Rgb48 cases, complete imaging and the full shell remain open.
No extra general performance budget is introduced. Native IME remains explicitly
deferred by the user and Phases 2 through 5 remain open.

## Revision 8 functional continuation and source-workflow boundaries

The [Rgb48 functional continuation](../../artifacts/portability/nina-native-imaging-functional-continuation/revision8/handoff.json)
passes 48/48 and all 16 frames with no failure flags, under the user's approved
reported-only timing policy. It adds no production change or cache optimization.
Prior Gray16 48/48 and revision 7's Rgb48 red/accepted timing remain unchanged.
This closes the previously unreached functional checks, not sustained 1 Hz or
Phase 5 qualification.

Commit `0b49e8134` adds the [original-source build helper](../../artifacts/portability/nina-original-source-build/committable/candidate-freeze.json):
six reviewed files and 680 pinned upstream sources. It derives only paired
Windows console guards in App.xaml.cs, records native deployment omissions and
keeps SystemEvents metadata compile-only. The parameterized smoke builds with
zero errors and 320 retained obsolete warnings, preserving 104 BAML entries and
115 resource keys. The [paired invalid-CLI console candidate](../../artifacts/portability/nina-original-app-startup-trace/console-candidate-execution-1/runtime-handoff.json)
reaches all 34 original App resources and one Run/exit cycle with expected exit 1
on both origins. Generated splash is omitted for this probe. This is a source-
adapted App boundary with 159 original PEs plus one source assembly, not raw
generated Main, valid startup, MainWindow/hardware composition or full shell.

| Workflow | Achieved evidence and remaining boundary |
| --- | --- |
| Rich text/plugins | [Stock preparation 4](../../artifacts/portability/nina-richtext-plugin-workflow-design/stock-handoff.json) passes 21/21. The [Linux run](../../artifacts/portability/nina-richtext-plugin-workflow-design/LINUX-HANDOFF.md) passes eight, fails one and leaves 12 unreached at original PluginImage's local pack-PNG BitmapImage guard. No codec or replacement-control success is implied. |
| Sequencer | The [stock host contract](../../artifacts/portability/nina-sequencer-workflow-host-contract/result-summary.json) records runtime 17/17; its original report-format failure is retained. The [later report repair](../../artifacts/portability/nina-sequencer-workflow-report-repair/result-summary.json) has 17 runtime passes but a REJECTED foreground guard, not an overall qualified run. [Linux](../../artifacts/portability/nina-sequencer-workflow-linux-1/linux-result.json) passes four, fails one and leaves 12 unreached at PortableTextFormatter's paragraph guard before raster. |
| Framing | [Stock preparation 8](../../artifacts/portability/nina-framing-workflow-design/preparation-8/runtime-handoff.json) passes 18/18 with actual native astronomy/catalog operations and the genuine survey annotator. Only the fixture-owned toolbar selector was repaired. No Linux framing result is qualified. |

The approved private PresentationFramework-to-host bridge is conditional IVT
to pinned signed AvalonDock, following the user's rejection of public host APIs.
It remains unimplemented. Separate Window/Splash work is not qualified by any
coherent family recorded here. Full shell, overnight, final package and native
X11/XWayland/Orca workflows remain open. These local results do not complete
Phases 2 through 5; native IME remains user-deferred and optimization is canceled.

## Sealed managed compatibility checkpoint

The [combined managed proof](../../artifacts/portability/combined-managed-fixes-20261019/README.md)
for commit `27a8d1475` passes 992 existing checks per OS: Imaging 17, Markup 16,
Rendering 50, Guideline 324, Text 348, TextControls 211 and Core 26. Both OSes
also pass all 91,707 maintained Unicode 17 cases with zero input mutations.
Each uses one Release Core/PF producer family, matching all 40 selected source/PDB
documents and PE/PDB identities. All fixture processes exit zero with empty
stderr; builds retain ten existing framework warning lines and zero errors.
No source edits, test changes or binary overlays were used for this proof.

Linux origin traces verify regular SkiaSharp.NativeAssets.Linux 3.119.4 plus
actual Fontconfig/configuration reads. The Markup Runner's unchanged
System.Drawing.Common 10.0.13 graph selects the official System.Private.Windows.Core
runtime asset, SHA `704B2F1033961BE827E73BC04014C6925F2B38E4F72D023AB22E16ABE758C6BD`;
other suites use its source-built auxiliary counterpart. Identical Core/PF
producers do not imply identical transitive auxiliary binaries.

The separate [PresentationUI proof](../../artifacts/portability/presentation-ui-integrated-main/README.md)
at `05fdd11bb` passes PUI 5/5 and Markup 16/16 on each OS, with fresh normal
Core/PF/PUI graphs. All four processes exit zero with empty stderr. All 123
original inputs and 106 PUI PDB documents per OS match; three resource manifests
and all 13 BAML entries are byte-identical across OSes. Each graph has ten
existing dependency warning lines, zero PUI warnings and zero errors. The same
official auxiliary selection described above remains explicit.

This PUI gate covers original resources, themes, the genuine default template
and initial 320 x 180 viewport layout with Document=null. It creates no
Application or Window and does not qualify nonempty document text, raster,
printing or XPS. The historical PTS red remains unchanged and was not rerun.

These are standalone managed CPU results. Separate native input-router/focus
qualification and integration with the current Core remain pending; no full
owned NativeHost, package or desktop result follows. The accepted managed
FlowDocument direction is not a nonempty-formatting pass. Phases 2 through 5
remain open and the full NINA shell remains unlaunched.

The user has deferred native focused-editor/IME composition integration.
The investigation draft remains local and unqualified, with no new base/hook
implementation. Committed transport and ordinary complete Unicode text retain
their separate scopes. This is a current policy decision, not a passed check;
historical composition requirements and the open Phase 5 gate remain unchanged.
This user-approved deferred capability is a known limitation, not a pending
permission decision or a block on other phases. Ordinary Unicode input, managed
preedit and controlled IBus transport remain separate from unqualified genuine
native engine/editor startup. No hidden base copy or architecture hook is added.

The accepted bidi contract follows Unicode 17. All 91,707 Unicode bidi character level cases pass after a bounded correction to the pinned managed dependency. This algorithm result is separate from paragraph layout, fallback, glyph positioning and native editing qualification. Legacy Windows WPF bracket ordering and measured Display-mode metric differences are recorded compatibility differences, not silently relaxed test tolerances.

The approved direction remains the original WPF semantic core with a project-
maintained Linux X11/GLib/GIO backend and retained CPU Skia rendering. The
[backend decision](backend-decision.md) records the later owned-family results,
remaining native icon failures and actual WindowChrome startup boundary. These
do not qualify full NINA, final packages or actual GNOME/XWayland/Orca. The accepted
Rgb48 timing and canceled optimization policy remain unchanged; Phases 2 through
5 remain open.
