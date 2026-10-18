# Actual WPF dispatcher with a native X11 pump

This executable initializes the owned native host before creating the actual source-built WindowsBase dispatcher. Its tests use actual dependency properties, dispatcher operations, timers, frames and shutdown. Avalonia supplies the native X11 window and event delivery. It owns no WPF priority queue.

The test-only XTest interop submits key press/release requests to the actual X server over a separate connection. KeyDown, KeyUp, text and focus callbacks must arrive through native X11/GLib dispatch while a WPF frame owns the loop. This is OS-injected native input, not a physical keyboard test and not headless input simulation. The native key callback must mutate an actual WPF dependency object on its owner thread. This does not prove WPF InputManager, routed input events, controls or capture.

Behavior checklist:

- Actual WindowsBase assembly identity, backend signing identity and dependency-object owner-thread enforcement.
- Worker synchronous invocation and a burst of cross-thread WPF operations through the native wake path.
- An idle WPF frame wakes for a delayed worker operation without a WPF polling timer.
- Actual native queue reports pending XTest input before dispatch; press/release and text callbacks arrive before WPF Background work. The real WPF queue preserves Normal, Input and Background order.
- Native focus loss and regain are observed in both directions.
- Repeated native pending-input queries cannot strand socket data already read into Xlib's event queue.
- A native key callback can push a nested actual WPF frame and receive the key release before that frame exits.
- Actual WPF cancellation and timer start/stop work through the native pump.
- An owned native modal completes while a WPF frame pumps.
- WPF Background operations continue making observable progress while repeated native key events and native render callbacks run.
- Direct managed input-provider exceptions reach the WPF frame and allow subsequent WPF operations.
- Signal and Background exceptions cross actual GLib C callbacks safely, reach the managed WPF catch through the exception slot and leave both dispatchers usable.
- Native close and actual WPF shutdown complete, inactive operations are aborted and all tests finish before success is reported.

Build the portable framework first using its documented setup. Run `Backend/NativeHost/Prepare-AvaloniaSource.ps1` to download and verify the exact approved source commit before building its unchanged generators and one-facade X11 adaptation. The generated upstream source remains under ignored artifacts. To avoid racing framework source generation, the following commands reuse its outputs. Run from `src/Portability` with its SDK 10.0.401 and set CLI homes and NuGet caches beneath `artifacts/portability`:

```sh
dotnet build Backend/NativeHost/BuildTools/DevGenerators.csproj -p:RestoreLockedMode=true
dotnet build Backend/NativeHost/Avalonia.X11/Avalonia.X11.csproj -p:BuildProjectReferences=false -p:RestoreLockedMode=true
dotnet build Backend/NativeHost/NativeHost.csproj -p:BuildProjectReferences=false -p:RestoreLockedMode=true
dotnet build Tests/NativeDispatcherIntegration/NativeDispatcherIntegration.csproj -p:BuildProjectReferences=false -p:RestoreLockedMode=true
dotnet ../../artifacts/portability/native-dispatcher-integration/bin/Unix/Debug/net10.0/NativeDispatcherIntegration.dll
```

Runtime requirements are Linux, an X11 display, GLib, libX11, libXtst and usable fonts/rendering. Windows can compile these projects but the host explicitly rejects a Windows run. Headless Avalonia component evidence remains in the separate WindowHost probe and cannot replace this native integration run.

An existing Xvfb server can provide an isolated native virtual X11 display. This still exercises real Xlib windows, GLib sources and XTest events. It does not qualify GNOME, KDE, WSLg desktop focus or a physical keyboard. For example, with display `:93` unused:

```sh
Xvfb :93 -pn -screen 0 1024x768x24 -nolisten tcp -ac &
probe_xvfb_pid=$!
trap 'kill "$probe_xvfb_pid" 2>/dev/null; wait "$probe_xvfb_pid" 2>/dev/null' EXIT
sleep 1
DISPLAY=:93 dotnet ../../artifacts/portability/native-dispatcher-integration/bin/Unix/Debug/net10.0/NativeDispatcherIntegration.dll
```

Only that retained server PID is stopped. `-pn` permits the abstract Unix listener when WSLg has mounted `/tmp/.X11-unix` read-only; TCP remains disabled.

Standard output ends with JSON containing `probe`, `os`, `scope` and named checks with `status` and `detail`. Any failed or incomplete test returns exit 1. A 30-second watchdog also emits JSON and fails a stuck run. The intentionally enabled Avalonia private-API build target emits `AVA3001`; this warning documents the pinned maintenance dependency. The source-built X11 project also retains the existing upstream `CS0649` warning described in its provenance file.

## Recorded verification on 2026-10-18

Windows and Ubuntu 24.04.1 builds succeeded with SDK 10.0.401. The isolated native virtual X11 run on Xvfb `:93` passed all 15 checks and returned exit 0. Its `linux-xvfb.json` evidence under `artifacts/portability/native-dispatcher-integration` records native press/release/text, owner affinity, focus transfer, 24 pending-input probes, nested KeyUp delivery and Background progress. The stress case received 25 native key presses and 35 render callbacks while WPF Background work progressed from native event 1 through event 25. The idle worker wake completed in 60 ms without a WPF polling timer.

The Background callback defect was reproduced before the production repair: the independent repro returned exit 134 and the newly added in-repo regression aborted at `GLIB_BACKGROUND_CALLBACK`. Those logs are preserved as `glib-background-abort-before.log` and `glib-background-regression-before.log`. With the tracked source guard, the same independent repro prints `MANAGED_CATCH` and returns exit 0 (`glib-background-managed-catch-after.log`). The full runner separately verifies signal and Background callback stacks, managed catches and subsequent work on both dispatchers.

No-change build diagnostics in `windows-incremental.json` and `linux-incremental.json` confirm `CoreCompile` was skipped and the signing key, derivative source and assembly timestamps remained unchanged. The original verified source file hash also remains unchanged.

The WSLg desktop run remains failed evidence in `linux-wslg-after-grouped-dispatch.json`; XTest keyboard/focus delivery was inconsistent even when XGetInputFocus reported the host XID. Those failures are not superseded by the virtual-server result. Desktop/physical qualification remains open. Earlier failing queue evidence is preserved in `linux-native-before-grouped-dispatch.json`. The unsupported Windows runtime correctly returned exit 1 with incomplete-test failure in `windows-unsupported.json`.
