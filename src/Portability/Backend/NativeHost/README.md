# Native pump for the actual WPF dispatcher

`Wpf.Portable.NativeHost` connects the source-built WindowsBase dispatcher to a Linux X11/GLib event pump. It does not implement another WPF dispatcher or priority queue. The actual WPF dispatcher retains operation ordering, timers, cancellation, synchronization contexts, nested frames and shutdown.

Call `NativeDispatcherHost.InitializeForCurrentThread(AppBuilder.Configure<YourApplication>())` on the intended UI thread before constructing its first WPF `DispatcherObject` or obtaining `Dispatcher.CurrentDispatcher`. This configures Avalonia's GLib X11 backend, initializes its application and registers the thread-local WPF pump factory. Other WPF dispatcher threads retain their managed pump. This entry point supports the pinned Linux GLib backend only.

The adapter uses Avalonia 12.1.4's typed `[PrivateApi]` `Dispatcher.PlatformImpl` and `IControlledDispatcherImpl`. The package's own `AvaloniaAccessUnstablePrivateApis` build target permits those references without reflection. Its `AVA3001` maintenance warning remains visible. Packages and transitive content hashes are locked and startup rejects another native implementation or version. The package target compiles against its net8.0 implementation assemblies, which the .NET 10 host can load. A narrow [source-built X11 adaptation](Avalonia.X11/UPSTREAM.md) supplies access to the actual queued native input and guards the existing GLib Background callback. This is an explicit unstable dependency requiring review and regression tests before any Avalonia upgrade.

The supported public dispatcher surface alone was insufficient: `RunJobs` explicitly omits OS events and `MainLoop` exposes a whole loop rather than a bounded native iteration. The owned adapter uses the stable GLib C API for a single `g_main_context_iteration` on the same default context used by Avalonia's GLib X11 backend.

- `Wake` coalesces cross-thread notifications and calls `g_main_context_wakeup` without invoking WPF work.
- Each `WaitAndDispatch` first dispatches existing Xlib events through Avalonia's original event dispatcher and at most one event from its existing grouped input queue. It skips blocking when that path made native progress. `WaitAndDispatch(0)` never waits for a source. A positive timeout attaches a temporary monotonic GLib timeout source. `-1` may wait indefinitely until a native source or wake arrives. The timeout bounds waiting; callback execution and upstream native queue draining can take longer.
- Nested WPF frames may recursively iterate the same owner-thread GLib context. Avalonia's GLib X11 source explicitly permits recursion.
- The context is acquired on the owner thread and released during WPF pump disposal after native iterations unwind.
- Signal and Background callback exceptions are captured through Avalonia's external GLib exception logger and rethrown after the C callback boundary returns. One tracked upstream source guard is required for Background callbacks, which otherwise abort the process. Direct managed input-provider callbacks propagate directly to the WPF frame and are tested separately.

`HasPendingInput` checks both Avalonia's actual X11/grouped native event queue and `g_main_context_pending`. Avalonia reports `CanQueryPendingInput=false` for GLib because a narrow X11 query omits ready GLib sources. The additional GLib query is deliberately conservative: non-input ready work may defer WPF Input/Background work. The Windows WPF implementation similarly includes `QS_INPUT`, `QS_EVENT` and `QS_POSTMESSAGE`, but these source sets are not equivalent. Continuously ready foreign GLib sources could starve lower WPF priorities. The integration tests measure Background progress amid native key events and rendering; they do not establish arbitrary GTK integration or complete Win32 priority equivalence.

The native window and keyboard event target in the integration runner are Avalonia objects. Actual WindowsBase dependency objects and dispatcher operations run beneath them. WPF `Window`, `InputManager`, routed input events, controls, capture and retained WPF rendering are separate integration work.

Primary implementation references, pinned to 12.1.4 where applicable:

- [Avalonia dispatcher implementation access](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Threading/Dispatcher.cs)
- [Typed private dispatcher interfaces](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Threading/IDispatcherImpl.cs)
- [Avalonia private API build opt-in](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/packages/Avalonia/AvaloniaPrivateApis.targets)
- [GLib X11 source, pending queue and recursion](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.X11/Dispatching/GLibDispatcherImpl.cs)
- [GLib source handling and exception propagation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.X11/Dispatching/GlibDispatcherImplBase.cs)
- [GLib single iteration](https://docs.gtk.org/glib/method.MainContext.iteration.html), [wake semantics](https://docs.gtk.org/glib/method.MainContext.wakeup.html) and [pending sources](https://docs.gtk.org/glib/method.MainContext.pending.html)
