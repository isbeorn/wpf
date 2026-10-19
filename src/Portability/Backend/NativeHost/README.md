# Owned Linux native host for WPF

`Wpf.Portable.NativeHost` attaches the source-built WPF dispatcher, presentation source and input providers directly to Linux X11. The initial native ABI is Linux x64. XWayland uses the same X11 protocol; native Wayland and macOS require separate adapters.

Initialize the intended UI thread before its first WPF Dispatcher or DispatcherObject:

```csharp
NativeWindowHost.InitializeForCurrentThread();
```

`NativeDispatcherHost.InitializeForCurrentThread()` installs only the native pump. Both entry points are parameterless. WPF owns dispatcher priorities, timers, synchronization contexts, nested frames, input routing, control trees and retained CPU rendering. The native assembly supplies OS operations behind the existing internal portability contracts.

## Ownership and callbacks

Each initialized thread owns an X11 connection and private GLib context. The existing WPF pump factory creates an `OwnedGlibDispatcherPump` borrowing that connection. Native source callbacks return safely to C before managed callback exceptions propagate through a WPF frame. Cross-thread wake does not execute WPF work.

The host owns services and the native window factory. WPF ShutdownFinished closes those resources in reverse acquisition order, continuing cleanup if a resource fails. A small host pump wrapper disposes the unwound native pump before releasing its connection/context and then propagates the first cleanup failure. Shutdown never releases a context still borrowed by the pump.

Each genuine WPF Window owns close policy, close cancellation and its native peer. The source borrows the peer. Native close requests invoke that existing policy; successful native disposal is acknowledged exactly once. The acknowledgement describes synchronous client disposal, not an X server round trip. Native metric notifications coalesce through one lifetime-guarded WPF Send operation, keeping state and geometry snapshots consistent and allowing safe close from WPF state/DPI handlers.

The peer creates a hidden native window before attaching the real WPF source. Its framebuffer accepts only valid premultiplied BGRA frames and retains no caller memory. The renderer, text formatter and imaging storage remain in PresentationCore. The explicit `SkiaSharp.NativeAssets.Linux` 3.119.4 package preserves the qualified Fontconfig-backed binary selection.

## Desktop contracts

The native peer preserves requested versus observed window state, physical client/screen conversion, visibility, enablement, ownership, focus and capture policy. Pointer and key decoding belongs to the X11 input adapter; WPF report creation, activation and popup keyboard-recipient policy stay in the peer. Complete committed text enters the existing WPF text report path. Native IME transport/preedit is deferred; that does not permit splitting or dropping complete Unicode text commits. Visibility and enablement transitions reset native compose state without changing physical held-key truth. An active popup recipient also resets composition on its native keyboard owner when hidden, disabled or closed. Native horizontal-wheel flags pass through the existing WPF input path, which does not currently produce a routed horizontal-scroll event; no public horizontal-scrolling support is claimed.

Startup reads actual XSETTINGS pointer preferences. Net/DoubleClickTime must be a positive observed integer. A valid payload omitting only Net/DoubleClickDistance uses the declared five-physical-pixel product fallback. Malformed values, missing time and missing owners remain failures. The bounded parser and per-field provenance are unchanged. Colors and drag thresholds remain the explicit portable-light-v1 profile. Primary-screen DIPs derive once from actual monitor pixels and published positive desktop logical DPI. When XSETTINGS and Xresources publish no logical DPI, the declared fallback is 96 DPI; physical monitor density does not select desktop scale. Global XSETTINGS DPI changes retain the native physical client extent while updating the reported scale. Logical Window dimensions therefore change until the application explicitly resizes the window; reapplying logical dimensions requests their new physical extent. Ambiguous primary observations remain absent rather than inventing a monitor. Settings remain one immutable process snapshot. The first successful native settings registration supplies it before WPF consumes it; additional UI threads validate their native observations and reuse that snapshot.

Clipboard ownership uses CLIPBOARD independently of PRIMARY. Text export snapshots the original IDataObject. Flush is session-only and does not claim persistence after process exit. Clipboard waits, portal waits and modal windows use genuine WPF dispatcher frames.

OpenFileDialog and SaveFileDialog use the desktop FileChooser portal with the actual x11 owner. Owner closure cancels the returned request; cancellation and service failure remain distinct. The existing WPF dialog implementation owns modal disablement, restoration, tentative fields and FileOk rollback. Save preserves the native final path without appending an extension after confirmation.

AT-SPI registers real WPF automation peers with stable native object identities. Its per-window registration is created before Show and removed before native destruction. An absent accessibility bus does not prevent ordinary startup. A native accessibility client must qualify the exported tree, focus, bounds and supported actions; creating a managed AutomationPeer alone is not native accessibility evidence.

## Verification checklist

The candidate must pass the existing meaningful native fixtures after their framework-specific plumbing is retargeted:

- Actual WPF priority ordering, timers, cross-thread wake, bounded waits, nested frames, input fairness, exception recovery and shutdown.
- Window grow/shrink, legal one-pixel boundaries, rejected zero/subpixel requests, DPI round trips, state/restore transitions and reentrant close followed by unrelated native activity.
- Show/hide, enable/disable, focus gain/loss, both sides of key/button transitions, all five buttons, held snapshots, capture transfer/loss/release and cursor shape/visibility restoration.
- Independent pixel readback, retained invalidation, expose/uncover, ARGB storage, popup placement, nonactivation and owner/modal lifetime.
- Unicode clipboard transfer, ownership replacement, PRIMARY isolation and real TextBox copy/cut/paste commands and bindings.
- Portal open/save/accept/cancel/failure, filters, final paths, owner cancellation, FileOk veto/exception rollback and connection reuse.
- AT-SPI external tree identity, actual command invocation, disable/enable, focus signals, bounds/DPI, pre-Loaded queries, hide/show and close cleanup; real Orca qualification remains separate.
- Fresh package/consumer graphs with no Avalonia package, assembly, analyzer or source-project dependency and the existing Unicode 17/rendering/markup/NINA regression gates preserved.

Private Xvfb plus a real window manager establishes deterministic protocol behavior. Physical desktop and GNOME XWayland qualification, native picker interaction, Orca and original NINA workflow replay are distinct gates. This implementation document does not itself record those gates as passed. Historical Avalonia-backed evidence remains historical evidence.
