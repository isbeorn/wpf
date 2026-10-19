# WPF dispatcher with the owned X11 pump

This executable initializes the owned native host before constructing the actual source-built WindowsBase Dispatcher. Its Window and TextBox are genuine WPF objects. Independent XTest requests pass through the owned decoder and WPF InputManager. Native key callbacks mutate an actual DependencyObject on its owner thread.

Preserved behavior checklist:

- Inline same-thread Send leaves native and queued WPF work pending until an explicit pump.
- DisableProcessing rejects PushFrame. WaitAny/WaitAll observe timeout, signal and reset without dispatching either queue, then both resume.
- Worker Invoke, bursts and an idle blocking frame use the native wake path without a WPF polling timer.
- Repeated pending-input probes preserve buffered Xlib events, Normal/Input/Background ordering and native input before Background work.
- Native focus loss/regain, paired keys and a nested WPF frame retain delivery.
- Cancellation, timer start/stop and genuine synchronous owned ShowDialog retain WPF semantics.
- Native keys, actual retained frames and Background work each make progress.
- Real native backlog and connection.Post pressure both make progress at budgets 1 and 32 while the combined batch count stays bounded.
- Native input exceptions preserve exact identity and subsequent native input/WPF work succeeds.
- GLib callbacks return to C after capturing the first original exception. The production pump rethrows it with its stack and stays usable.
- Native close and actual WPF shutdown abort Inactive work and finish before success.

The historical signal/background check names remain stable. The removed backend's delegate methods no longer exist. Those checks now attach fixture-owned idle/timeout GLib C callbacks to the real owned context and use production CaptureCallbackException/ThrowPendingException. They verify original exception identity, first-exception preservation, return to C and subsequent native callbacks, real key pairs and WPF work. They do not force an Xlib error inside the production FD callback.

A fresh-process `--late-host-profile-only` mode consumes a genuine public WPF system setting before the first native initialization. Two startup attempts must reject the consumed snapshot and dispose their actual dispatchers/connections, preserving that snapshot after failure.

The fixture links the existing X11Fixture for its private XSETTINGS owner and independent native observation. Reflection reads existing private pump/peer/target state; no public WPF API or test-only production hook is added.

Build from src/Portability with SDK 10.0.401 and existing framework prerequisites:

```sh
dotnet build Tests/NativeDispatcherIntegration/NativeDispatcherIntegration.csproj -c Release -p:RestoreLockedMode=true
```

Run on a dedicated Linux Xvfb server with GLib/GIO, Xlib, XInput2, XTest, XFixes, RandR, libxkbcommon and the existing Skia/HarfBuzz native assets. Pass `--font=<absolute pinned DejaVuSans.ttf>`. X11Fixture refuses to replace an existing XSETTINGS owner. Initialization rejects non-Linux execution.

JSON statuses, zero process exit, completion and native cleanup are all required. The watchdog is 30 seconds. Native virtual-server results do not qualify GNOME X11, XWayland, physical input or native IME. Prior backend results remain historical evidence.
