# Portable backend decision

## Approved owned Linux backend: 19 October 2026

The user approved removing the Avalonia dependency. Replace the native adapter
inside `Wpf.Portable.NativeHost` with project-maintained Linux code, not a copied
or renamed Avalonia backend. Preserve the original WPF dispatcher, controls,
bindings, layout and input semantics and the existing retained Skia renderer.
The owned host/delivery cutover now has bounded exact-source managed and native
verification, as recorded below. Earlier ledger checkpoints retain their
historical scopes. Avalonia-backed runs do not qualify the owned backend.

Keep the focused SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.5 bindings accepted after
license review. Preserve the regular Linux Skia/Fontconfig native asset and add
the required Wuffs/HarfBuzz notices before producing packages. The user also
approved directly compiling 34 project-maintained pure Unicode derivatives with
their original attribution, licenses and provenance. Maintain one canonical
Unicode implementation and preserve the 91,707-case Unicode 17 bidi gate. This
limited source reuse does not authorize importing the Avalonia platform layer.

Use public GLib/GIO OS APIs for the event loop and D-Bus portals/AT-SPI, with no
new managed transport dependency. Own a private GLib context and the required
X11, XI2, XKB, XFixes, RandR and XRender integration. Begin native presentation
with a conservative XPutImage framebuffer. Retain WPF's operation queue and
nested frames rather than introducing another managed UI scheduler.

The selected startup calls are parameterless
`NativeDispatcherHost.InitializeForCurrentThread()` and
`NativeWindowHost.InitializeForCurrentThread()`. They must register native
services before the thread creates its WPF Dispatcher or consumes settings.
These replace the adapter's Avalonia AppBuilder dependency without changing
public WPF contracts. Reuse the existing internal boundaries:

- [DispatcherPlatform/IDispatcherPump](../../src/Microsoft.DotNet.Wpf/src/WindowsBase/MS/Internal/Portability/DispatcherPlatform.cs) for wake, pending work and bounded dispatch.
- [PortableWindowPlatform](../../src/Microsoft.DotNet.Wpf/src/PresentationFramework/MS/Internal/Portability/Windowing/PortableWindowPlatform.cs) for window factories, peers and Popups.
- [PortablePresentationSource](../../src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/Portability/Windowing/PortablePresentationSource.cs) and its presentation host for source lifetime, input and retained pixels.
- The existing WPF clipboard and [file-dialog contracts](../../src/Microsoft.DotNet.Wpf/src/PresentationFramework/MS/Internal/Portability/Windowing/PortableFileDialogs.cs) for desktop services.

Export the original WPF AutomationPeers through an owned AT-SPI server using
GIO. Session clipboard operation is allowed; persistence after process exit is
not assumed. Native IME remains explicitly user-deferred. Basic XKB keyboard
mapping and compose through the existing OS prerequisite libxkbcommon.so.0
are required and are not covered by that deferral.

### Owned family qualification checkpoint

Commit `2e3e2174` integrates the [owned host/delivery source intake](../../artifacts/portability/owned-backend-main-intake/intake-manifest.json):
69 changed files, 63 intake rows, 22 retired files, 16 preserved rows and ten
delivery rows. Main retires the active Avalonia platform projects, source
preparation and assembly dependencies while retaining the 34 attributed Unicode
derivatives and audited SkiaSharp/HarfBuzzSharp dependencies. Four preserved
helper source/PDB differences are only CRLF/LF; the final AtSpi fixture adds two
wire checks. Historical Source4 results retain their original scope.

The [sealed exact-source qualification](../../artifacts/portability/owned-current-main-qualification/README.md)
uses fresh locked 11-project Windows/Linux builds of `2e3e2174`. Each OS passes
1,031 managed checks; 18 private native X11 modes pass 161 checks. All those
runtime processes exit zero with empty stderr. Source/PDB, runtime copies,
dependencies and native assets have full family proof. The first path-substring
oracle failure is retained: complete binary roots were relocated byte-exact to
`<os>/portability/fixtures`, with before/after inventories and no assertion edit.

The raw Unix Stage contains all 120 output files without exclusions. It is
source-build output for local qualification, not a curated runtime package.
Additional fresh AT-SPI wire runs on that family pass basic 8, TextBox 13 and
disconnect 1 groups, with exact owned-process/display cleanup. Their retained
D-Bus activation/disconnect messages are separate from fixture failures. Wire
checks do not qualify actual Orca or GNOME/XWayland desktop interaction.

Packages, actual GNOME X11/XWayland/Orca, NINA workflow replay and overnight
simulation remain open. The [first actual App.Main attempt](../../artifacts/portability/nina-simulator-qualification-candidate/first-owned-shell-source2e3/handoff.json)
enters original App.OnStartup, returns from real composition and reaches
MainWindow.InitializeComponent. It fails at MainWindow.xaml line 37's original
pack ICO through ImageSourceConverter/BitmapFrame/BitmapDecoder. MessageBox's
user32 failure is secondary. No usable Window is observed; owned cleanup is
verified afterward. This preserves the first failed startup as historical
evidence; the later checkpoint below records the subsequent source changes.

### Current NINA startup and source checkpoint

The [bc9 checkpoint](../../artifacts/portability/owned-bc9-main-qualification/README.md)
uses fresh eleven-project Windows/Linux builds of `bc9f04df9`. Each OS passes
1,110 managed checks with matching source/PDB and runtime-copy proof. Eighteen
existing native modes pass 161 checks. The separate canonical original-AvalonDock
consumer passes Show 13/13 and theme 20/20 against the same complete 120-file
Linux Stage. Native icon publication preserves all six original NINA frames.
Two added icon rejection/recovery cases fail; this checkpoint is not fully green.

The [actual App.Main retry](../../artifacts/portability/nina-simulator-qualification-candidate/first-owned-shell-bc9-retry/handoff.json)
passes the previous icon-decoder boundary, then fails while applying the original
MainWindow style. WindowChromeWorker requests WindowInteropHelper.Handle before
mapping, which remains unsupported. No native main window or usable preview is
observed. Cleanup passes; the secondary MessageBox/user32 failure remains distinct.

Main now contains the modifier/embedded-object formatter, genuine document child
layout, RenderTargetBitmap, PNG/ICO factories and native bitmap icons. Commits
`45ff16261` and `074aa3bab` add reviewed RTL document-coordinate mapping and the
wrapped-TextBlock cache repair. Their scoped stock/Windows/Linux checks pass.
The cache key includes the inherited initial modifier scope after a public
interleaving regression exposed stale decoration. Commit `749128b59` moves expected
icon validation before dependency-property mutation; its coherent native green
remains pending. These later commits are not covered by the bc9 family proof.

The accepted next frame implementation, still awaiting qualification, keeps
original NINA styles and caption controls,
uses genuine WPF hit testing and delegates interactive move/resize to the owned
X11 window manager through its [standard protocol](https://specifications.freedesktop.org/wm/latest/ar01s04.html).
It adds no public WPF API or fabricated HWND. The portable profile declares an
8-DIP resize band, 23-DIP default caption and square native corners. Explicit
application values, including NINA's 30-DIP caption and CornerRadius=1, remain
observable; native corner clipping and Windows glass effects are not reproduced.
SystemParameters.WindowNonClientFrameThickness remains declared (8,31,8,8).
Only the portable WindowChrome default glass binding is omitted, retaining its
zero metadata default. These are declared framework geometry choices, not
observed desktop preferences, following the user's simplicity preference.

Verification must cover real caption dragging, all resize directions, client
opt-out inheritance, genuine caption commands, maximize/restore, DPI, mode changes
and attach/replace/remove/close cleanup. On 19 October the user chose to preserve
programmatic maximize and document desktop shortcut differences. [Openbox requires
native resize permission for maximize](https://sources.debian.org/src/openbox/3.6.1-12/openbox/client.c/),
so the host retains native operation permissions and requests mode-specific
decoration hints for ordinary windows. These hints are advisory: a window manager
may retain its native buttons.
The owned custom caption and borders still honor NoResize/CanMinimize/CanResize.
Window-manager actions, including keyboard shortcuts and menus, can resize or
maximize restrictive-mode windows. This is an accepted Linux difference, not
strict native ResizeMode enforcement. State requests originate from explicit
application requests and show transitions; metrics notifications observe state
without indefinitely resending an unfulfilled request. CanResizeWithGrip and the
synchronous public DragMove contract remain separate until implemented and tested.
Scoped checks now pass caption movement, all eight resize directions, capture
handoff and state-denial recovery. Four window-manager executions pass 15/15 and
the 96-DPI custom-chrome suite passes 21/21. The focused denial and capture suites
also pass at both 96 and 192 DPI. An [intermittent ordinary-window restore-position
failure at 192 DPI](../../artifacts/portability/window-chrome-final-intake/qualification-gap.json)
remains open: native and public state return to Normal with correct size, but the
observed native origin differs from its saved origin. No speculative position
cache or retry has been added. This is an explicit qualification gap while an
isolated actual NINA startup trial proceeds from one coherent framework family.
Original NINA workflows, complete Markdown, final packages, desktop/Orca and
overnight qualification remain open. There is still no successful full-shell
launch and Phases 2 through 5 have not passed.

## Replacement behavior checklist and gates

Reuse existing fixtures and their observable WPF operations. Establish relevant
reds, then replay both directions and failure/lifetime cases against one
coherent owned-backend family. None of the following is passed by this decision.

| Boundary | Required observable behavior |
| --- | --- |
| Dispatcher | Worker wake, pending-input queries, zero/bounded/infinite waits, priorities and nested frames; callback exceptions return through the managed boundary; shutdown, disposal and fresh-thread initialization leave no live callbacks or handles. |
| Windows and Popups | Show/hide/reopen, owner activation, grow/shrink, minimize/maximize/restore, DPI transitions and source detachment; mouse/keyboard opening and selection, Enter/Escape, outside click, capture loss and edge placement. Accepted close cancels owned work; canceled Closing retains the live source. Modal owner disable/restore, requested alpha and genuine splash behavior need native evidence. |
| Input | Real XI2 pointer events, held inside/outside capture and release, buttons, scrolling, focus and modifiers; ordinary Unicode, XKB dead keys and compose reach actual WPF controls. Preserve managed preedit cancellation/retention rules separately from deferred native composing-engine integration. |
| Settings and presentation | Observe real screen bounds and scaling, convert physical pixels to DIPs once and preserve absent/invalid settings diagnostics. Verify XPutImage byte order, stride, alpha handling, expose/cover recovery, resize and exact retained pixels without substituting controls or templates. |
| Clipboard and portal | Exchange text with an independent client, change/clear ownership and report unsupported operations truthfully. Real portal accept/cancel/error paths preserve owner identity, nested pumping, enablement and close cleanup; no second event loop or manufactured cancellation. |
| Accessibility | Export the actual peer tree, roles, names, focus, text, selection, caret and screen geometry through AT-SPI. Verify updates and removal, keyboard-only core workflows and actual Orca output on the declared desktop. Protocol-only checks are not the screen-reader gate. |
| Unicode | Compile the approved 34 derivatives with provenance and licenses, preserve the 91,707 Unicode 17 cases and rerun actual text/editing regressions. Historical algorithm passes are not a fresh owned-backend run. |
| Distribution | Fresh restore/build/publish/run plus native assets and notices. Audit the evaluated build graph, AssemblyRefs, deps files and loaded identities for no Avalonia package, platform project or runtime assembly dependency; retained Unicode provenance is explicitly distinguished. Test the produced packages, not mixed development DLLs. |
| Application and desktop | Replay the original NINA workflows and the simulation-only shell below. Qualify native GNOME X11 and XWayland separately, then repeated lifetimes and overnight simulation. Native Wayland, full WPF parity and performance bounds are not inferred. |

The approved NINA simulation build preserves the real App, generated startup,
MainWindow, VMs and SimulatorCamera. Load an allowlisted official fixture plugin
through the actual Loader and exercise genuine astronomy plus profile/layout
persistence. Use offline fixture data; do not run hardware discovery, vendor USB
initialization or downloads. Record every application adaptation and keep its
failures separate from framework failures. This is a pending shell workflow,
not permission to replace the application with a synthetic host or fake VMs.

The bounded source-family result above does not complete package or desktop
qualification. Phases 2 through 5 remain open and there has been no successful
full-shell launch. The user's
accepted Rgb48 timing stands; no new CPU/latency budget or canceled conversion
cache work is introduced.

## Approved managed FlowDocument layout: 19 October 2026

The user approved an owned managed bottomless layout engine for NINA's Markdown.
Keep FlowDocumentScrollViewer, FlowDocument, TextContainer, TextPointer,
TextEditor, selection, undo and automation consumers genuine. Preserve ordinary
Windows-native layout. Add no dependency, public API or replacement control tree.
The checkout's PresentationNative redist project only copies packaged binaries.
At acceptance, nonempty portable documents failed when acquiring the unavailable
PTS engine. PresentationUI source/resource qualification alone did not resolve
that gap; the bounded paragraph milestone below now bypasses it for its scope.

Own the portable layout inside the existing formatter, page and text-view
responsibilities. One page-owned committed snapshot supplies formatted lines,
block and child geometry and mappings to original TextPointers. Painting, hit
testing, caret navigation, selection and automation use that same geometry.
Start with full relayout, preserve reentrancy guards and invalidate without a PTS
Section. Publish successful layout transactionally, dispose superseded lines and
detach old children. Do not emulate opaque PTS handles or flatten away document
symbol positions.

The accepted sequence begins with a real nonempty Paragraph/Run and consistent
visible ink, hit testing and selection. It then covers the selected 35 paragraphs
and two genuine read-only AvalonEdit children, original NINA list/link callbacks
and the declared Section/table/local-image/Separator/decorated-span fixture.
Extend the existing portable formatter for required hidden, modifier and embedded
object runs while preserving source positions. Intrinsic list markers retain
their semantic shapes and numbering without redistributing Wingdings.

Preserve the existing 21 Markdown checks and add visible text, geometry,
bidirectional selection, scroll sentinels and A/B/A replacement checks. Source
text and nontransparent background pixels alone do not prove painting. Reuse
existing fixtures, stock semantic oracles and Unicode 17 regressions. Verify
child measure/arrange, focus, horizontal scrolling and detachment, invalidation
after content/width/property changes and clean shutdown after failures.
Pagination, printing, optimal breaking, general rich-text editing, arbitrary
table spans, columns and floaters remain separate qualification targets. Native
AT-SPI and Orca still require their own native evidence. This decision approves
implementation and its test strategy; approval alone is not a layout pass.

The committed [hidden-run revision 2](../../artifacts/portability/flow-text-runs/hidden-revision2/handoff.md)
in `49ecb1dea` preserves source symbols and caller run identities while shaping
visible text. TextIntegration passes 373/373 on each OS, including 25 new checks.
Modifier scopes, objects and markers are separate work.

The [automatic-height guard](../../artifacts/portability/flow-text-runs/automatic-height-revision1/handoff.md)
in `9eb8a6525` accepts NaN alongside zero, matching original AvalonEdit's standard
metrics path and stock zero/NaN metrics. TextIntegration passes 381/381 on each
OS and in fresh root Windows intake. Finite custom heights and infinities still
fail explicitly; hidden revision 2's historical 373 checks remain unchanged.

The [paragraph revision 2](../../artifacts/portability/managed-flow-document-tracer/paragraph-revision2/handoff.json)
in `8972f8086` passes genuine FlowDocumentScrollViewer paragraph layout, ink,
public line navigation, selection, hit testing, width restore, text mutation and
A/B/A raster/Run-hit restoration with stock, Windows and Linux observations.
Root intake changes only EOF bytes in four project/test files; production layout
bytes remain unchanged. Its final build has zero warnings/errors, with paragraph
1/1 and unchanged Markup 16/16 passing.

This is paragraph-only progress. The selected real NINA Markdown, AvalonEdit
children, list/link callbacks, table/local-image/decorated composite and broader
document behavior remain unqualified. The owned-family results above do not
qualify these NINA paths or packages. Phases 2 through 5 remain open, native IME
is user-deferred and there has been no successful full-shell launch.

## Historical initial decision: 18 October 2026

The native-host choice below is superseded by the approved owned backend above.
Its source observations and executed evidence remain historical. The managed
retained-rendering seam is preserved by the new direction.

Decision date: 18 October 2026. This records the Phase 1 implementation direction,
not completion of its gates or qualification of a Linux desktop profile.

Retain the source WPF dispatcher and semantic core. Use an owned, pinned Avalonia
X11/GLib adapter for native windows, input and bounded event pumping. Replay WPF's
managed retained drawing content into CPU Skia surfaces on the owning UI thread.
Use the bounded WPF text replacement described in the
[text compatibility ledger](../../src/Portability/Tests/TextIntegration/Compatibility.md).

The plan proposed common-suite comparisons of host strategies and rendering
seams. No equivalent head-to-head behavioral suite or comparative performance
experiment was completed for the rejected candidates. The decision instead uses
source-supported structural rejection and successful bounded execution of the
selected strategy. It makes no comparative speed or memory claim.

### Native host

The inspected Avalonia version is 12.1.4, commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31`. Its archive hash, license, exact source
patch and private API obligations are recorded in
[UPSTREAM.md](../../src/Portability/Backend/NativeHost/Avalonia.X11/UPSTREAM.md).

The selected contract keeps WPF as the owner of its operation queue, priorities
and nested frames. A native adapter must expose pending native work and one
bounded wait/dispatch iteration. The public Avalonia APIs inspected do not
directly provide that contract:

- [`RunJobs`](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.Base/Threading/Dispatcher.Queue.cs#L45)
  runs Avalonia managed jobs while ignoring pending OS events.
- [`PushFrame` and `MainLoop`](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.Base/Threading/Dispatcher.MainLoop.cs#L35)
  run Avalonia's own controlled loop and scheduling context until frame exit.
- The platform dispatcher and native pending-input interfaces are marked private
  API in the pinned
  [dispatcher interfaces](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.Base/Threading/IDispatcherImpl.cs#L23).

Reject public APIs alone as the direct implementation of this bounded contract.
This does not prove that a substantially different public-host architecture is
impossible. The [public host probe](../../src/Portability/Probes/WindowHost/README.md)
passed 13 native Avalonia component checks, with native input unproven. It did
not implement WPF dispatcher semantics.

The selected [owned adapter](../../src/Portability/Backend/NativeHost/README.md)
uses the existing Xlib/grouped input queues and a bounded GLib iteration without
creating another WPF queue. Its actual WPF native dispatcher fixture records
priorities, native input, nested frames, worker wakeups, timers, cancellation,
callback exceptions and shutdown separately from the public-host probe. Native
Window/control and window-manager checks are additional evidence. Their latest
results and remaining gates are in the [feasibility report](feasibility-results.md).

Pinning and private interfaces impose an explicit maintenance cost. Every
Avalonia upgrade requires source-delta review and the WPF native regressions.
Two exact source derivatives guard the GLib Background callback and preserve a
positive native extent during DPI changes. The owned facades expose existing
queues and native services, including a pinned explicit resize-request body
that bypasses stale cached equality. The backend does not install an Avalonia
control tree or translate WPF views into Avalonia controls.

An independently owned platform host using Skia was not implemented or tested.
Defer it because it would add native window, event-loop, input and desktop-service
ownership. This is a scope decision, not evidence that the alternative failed.

### Retained rendering

The existing
[`RenderData.WalkContent`](../../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/RenderData.cs)
replays semantic drawing operations. The lower channel path also owns DUCE
resource acquisition/release and serialized command updates. Both RenderData and
Visual implement `DUCE.IResource`; replacing the final surface alone does not
replace those responsibilities.

Choose the managed drawing-content seam to bound the initial port. A lower
DUCE/resource adaptation was not implemented or run against the same scene.
Source inspection supports the scope decision, not claims that the lower seam
is impossible, slower or incorrect. The early
[rendering component probe](../../src/Portability/Probes/Rendering/README.md)
documented this dependency graph but did not itself execute retained WPF content.

The selected
[`PortableRenderTarget`](../../src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/Portability/Rendering/PortableRenderTarget.cs)
walks actual DrawingVisual/UIElement records while preserving visual ordering,
offsets, transforms, clips and opacity. It connects mutation and disposal to the
existing MediaContext. The
[retained integration fixture](../../src/Portability/Tests/RenderingIntegration/README.md)
observes real pixels, mutation, layout, bounds, resize, DPI and resource lifetime.
The [guideline fixture](../../src/Portability/Tests/GuidelineIntegration/README.md)
adds native-calibrated geometry, image and glyph-baseline snapping.

These results establish a bounded CPU rendering seam. They do not establish full
MIL behavior, arbitrary drawing support, accelerated rendering, comparative
throughput or overnight memory bounds. Native presentation, cover/uncover and
packaged execution retain their own tests and status.
