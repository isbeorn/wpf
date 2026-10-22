# Retired native host feasibility probe

Retired on 19 October 2026 when the user selected the project-owned Linux host.
The executable project, source and package lock are removed from active build
discovery. The record below preserves the original probe's scope and historical
results. Its commands are historical and require restoring the corresponding
Git revision. Use NativeDispatcherIntegration and NativeWindowIntegration for
the owned backend's current qualification.

This executable tests public Avalonia APIs as a possible native window and event-loop host. It implements no WPF types and does not translate NINA views. Passing proves only the listed Avalonia behavior on the tested backend. It does not prove that WPF can use that backend.

Dependencies are pinned to Avalonia 12.1.4 with a NuGet lock file. Primary sources checked on 2026-10-18:

- [Avalonia.Desktop 12.1.4 package metadata](https://www.nuget.org/packages/Avalonia.Desktop/12.1.4)
- [Avalonia.Headless 12.1.4 package metadata](https://www.nuget.org/packages/Avalonia.Headless/12.1.4)
- [Avalonia MIT license at 12.1.4](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/licence.md)
- [Headless platform documentation](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)
- [Dispatcher public API](https://api-docs.avaloniaui.net/docs/T_Avalonia_Threading_Dispatcher)

## Behavior checklist

- Create and close a window, with a nonzero native platform handle in native mode.
- Verify UI thread ownership, reject access from a worker and invoke back synchronously and asynchronously.
- Cancel a queued operation before it executes.
- Start a dispatcher timer, observe a UI thread tick and stop further ticks.
- Open an owned asynchronous modal, pump a callback and close it with a result.
- Run one nested dispatcher frame when supported by the backend.
- Preserve the loaded Avalonia window assembly identity across those operations and retain UI thread ownership after awaits.
- Focus a text control and distinguish headless synthetic key/text delivery from observed native input.
- Fail if the window closes before the asynchronous host checks complete.

The nested-frame test exercises one callback and one frame. Priority ordering, WPF reentrancy, COM pumping, WPF synchronous ShowDialog, HWND interop, multiple UI threads and the WPF dispatcher contract remain unproven. A native handle and control focus do not prove native keyboard input.

## Build and run

Run from `src/Portability` so `global.json` selects the isolated SDK. Set `DOTNET_CLI_HOME`, `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` beneath `artifacts/portability`. Keep Windows and Linux output/intermediate paths separate. For example, on Linux from that directory:

```sh
dotnet build Probes/WindowHost/WindowHost.csproj -p:RestoreLockedMode=true \
  -p:BaseIntermediateOutputPath="$PWD/../../artifacts/portability/windowhost/obj-linux/" \
  -p:BaseOutputPath="$PWD/../../artifacts/portability/windowhost/bin-linux/"
dotnet ../../artifacts/portability/windowhost/bin-linux/Debug/net10.0/Portability.WindowHost.dll --smoke
dotnet ../../artifacts/portability/windowhost/bin-linux/Debug/net10.0/Portability.WindowHost.dll --headless --smoke
```

Native Linux mode requires an available X11 display, graphics libraries and fonts. Headless success cannot substitute for this native run. Native mode uses Avalonia's platform detection and reports the actual handle descriptor, so verify `XID` for X11 evidence.

`--smoke` closes automatically after the checks plus three seconds. `--duration-seconds 10` changes that input observation window to 1-120 seconds. A separate watchdog terminates a stuck smoke run after that duration plus 15 seconds. Without `--smoke`, close the window manually. Type into the input to record native keyboard and text events. `--require-native-input` makes absence of both event types a failure. It cannot be combined with `--headless`.

Standard output ends with JSON containing `probe`, `os`, `scope` and `checks` entries with `name`, `status` and `detail`. Failures return exit code 1. `unproven` identifies evidence gaps and is not success evidence. Native input remains unproven if no native events are observed. Headless input helpers are used only in headless mode.

## Recorded verification on 2026-10-18

Both Windows and Ubuntu builds passed with SDK 10.0.401, zero warnings and zero errors. Smoke results are saved locally under `artifacts/portability/windowhost`:

| Run | Result | Evidence |
| --- | --- | --- |
| Windows headless | Exit 0, 13 passing checks | `windows-headless.json` |
| Ubuntu 24.04.1 WSLg headless | Exit 0, 13 passing checks | `linux-headless.json` |
| Ubuntu 24.04.1 WSLg native X11 | Exit 0, 13 passing checks, native input unproven | `linux-native.json` |
| Invalid headless/native-input options | Exit 1 with JSON failure | `windows-invalid-options.json` |
| Native input required with no delivered input | Exit 1 with native-input failure | `linux-required-input-missing.json` |
| Window closed before asynchronous checks complete | Exit 1 with completion failure | `windows-early-close-after.json` |

The native run reported a nonzero `XID`, active window and focused input. No native keyboard or text event was delivered during the timed run. The successful host checks do not close that gap or establish WPF dispatcher compatibility.

The early-close regression was reproduced with a disposable Windows native process. Calling `Process.CloseMainWindow` when its first HWND appeared previously returned exit 0 with only four passing checks and one unproven check (`windows-early-close-before.json`). The same interaction after adding the completion guard returned exit 1 with `host-check-sequence-completed` failing. No native automation harness or test-only command-line switch was added.
