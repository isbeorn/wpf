# Portable backend decision

## Approved owned Linux backend: 19 October 2026

The user approved removing the Avalonia dependency. Replace the native adapter
inside `Wpf.Portable.NativeHost` with project-maintained Linux code, not a copied
or renamed Avalonia backend. Preserve the original WPF dispatcher, controls,
bindings, layout and input semantics and the existing retained Skia renderer.
This is an approved implementation direction, not an executed replacement or
new compatibility pass. All 77 existing ledger checkpoints retain their
historical scopes; Avalonia-backed runs do not qualify the owned backend.

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

Implementation is proceeding in isolated ownership lanes. Integration still
requires a fresh coherent source/package family and the gates above. Phases 2
through 5 remain open and the full NINA shell remains unlaunched. The user's
accepted Rgb48 timing stands; no new CPU/latency budget or canceled conversion
cache work is introduced.

## Approved managed FlowDocument layout: 19 October 2026

The user approved an owned managed bottomless layout engine for NINA's Markdown.
Keep FlowDocumentScrollViewer, FlowDocument, TextContainer, TextPointer,
TextEditor, selection, undo and automation consumers genuine. Preserve ordinary
Windows-native layout. Add no dependency, public API or replacement control tree.
The checkout's PresentationNative redist project only copies packaged binaries;
nonempty portable documents currently fail when acquiring the unavailable PTS
engine. PresentationUI source/resource qualification does not resolve that gap.

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
implementation and its test strategy; it is not a document-layout pass.

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
