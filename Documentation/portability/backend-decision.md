# Initial portable backend decision

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

## Native host

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

## Retained rendering

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
