# Genuine WPF native Window integration

This fixture links the source-built `Window`, `Button`, `TextBox`, presentation source, retained renderer and input pipeline to the owned Linux X11 peer. It uses the pinned Avalonia platform window and CPU framebuffer directly. It creates no Avalonia control tree and injects no WPF routed events.

The fixture runs on a dedicated Xvfb server with test-only XTest mouse/key injection and native pixel readback. That scope is a native virtual X11 server, not physical input or GNOME/KDE desktop qualification. WSLg's earlier dispatcher-component focus/input failures remain separate evidence.

Requested behavior checklist:

- Hidden native construction before the source attaches, then genuine Window source/layout/frame delivery.
- Synchronous premultiplied BGRA upload, native pixel readback, retained mutation and expose repaint.
- Actual covering native window, retained mutations while occluded and final screen/native pixels after uncover.
- Valid previous-frame overlap clearing/clipping during native viewport changes, paired grow/shrink settlement and malformed stride/buffer rejection.
- Explicit client-size requests with stale backend equality, server-already-equal geometry and unchanged dimensions, with actual native pixels after settlement.
- Named managed rejection of initial/shown zero and subpixel client extents, supported-size recovery, unrelated Window frames and minimum one-pixel grow/shrink.
- Genuine `WriteableBitmap`/`DrawImage` retained RGB48 mutation, exact 16-bit source preservation and repeated Window/source lifetime.
- Genuine Button press/release/capture/Click through native mouse reports and the real InputManager.
- All five native mouse button transitions and held-button activation snapshots, unexpected server-unmap capture loss, recovery and close with active capture/hidden cursor.
- Native keyboard reactivation after disable/enable and close from a real LostMouseCapture handler during hide/disable.
- Genuine TextBox committed BMP text and key release through native keyboard/text reports.
- Native size changes and uniform X11 DPI propagation.
- Repeated 96/144-DPI round trips for both one-pixel axes and each asymmetric minimum axis, followed by unrelated native frames.
- Direct close from native StateChanged and DpiChanged, followed by an unrelated Window frame to detect invalid upstream native continuations.
- Real Window ownership and synchronous ShowDialog nested-frame input/close.
- Native close cancellation, accepted disposal acknowledgement and a single Closed event.
- Actual XSETTINGS origin, both byte orders and named failure for missing/malformed preferences.
- Watchdog, nonzero failures and completion guard; all native dispatcher regressions remain required, including same-thread Send and DisableProcessing boundaries.

Build the complete graph with a unique absolute `PortableFrameworkArtifactsDir` ending in `/`, for example `artifacts/portability/native-window-framework/`. Do not reuse canonical outputs predating the current Core/PF input/window contracts. Outputs and intermediate files for this fixture and backend are separated by OS.

The executable accepts `--font=<absolute verified pinned DejaVuSans.ttf>` and `--settings=little|big`. It creates an actual XSETTINGS owner/property with declared test values: `Net/DoubleClickDistance=5` physical client pixels and `Net/DoubleClickTime=450` milliseconds. It records the exact selection owner XID/property origin. Those are fixture values, not desktop preferences. The production host requires real preferences and has no silent fallback to Avalonia's fixed defaults.

`--settings=absent|header|count|truncated|type|range|duplicate|value-type|trailing` exercises real missing or malformed server properties, including duplicate preferences, a recognized string value where an integer is required and extra trailing bytes. These cases succeed only when native initialization rejects the property. No production test friend or test-only WPF interface is added.

`--wm-only` runs the retained frame/image checks and genuine Window state/restore cases under a real EWMH window manager. The local qualification uses workspace-extracted Ubuntu Openbox on its own Xvfb server, with archive signatures, hashes and runtime closure recorded under ignored artifacts. It checks actual `_NET_WM_STATE`, mapped state, geometry and native pixels. This is separate from the no-window-manager Xvfb input/control run.

The fixture supplies explicit simple Button/TextBox templates to isolate the native host boundary. Full application theme resources, mixed per-axis DPI, non-BMP text, full IME composition and physical desktop qualification remain separate work. Check the emitted JSON before treating any Window gate as passed. Reflection is confined to fixture diagnostics and existing internal boundary checks: the peer frame/cursor contracts, the actual input provider's hidden-cursor cleanup state and retained drawing guidelines. It adds no production test friend or interface. Native cursor image checks complement the managed control fixture's OverrideCursor/typing policy checks; they do not claim a native typing profile with MouseVanish enabled.

## Reproduce the build

Use SDK 10.0.401, Linux with the existing Avalonia X11/Skia runtime dependencies, GLib, libX11, libXi, libXtst, libXfixes and Xvfb. Prepare the pinned source archive once using the existing PowerShell script from the repository root. Windows PowerShell can prepare the shared checkout before a WSL build; Linux can use an already available PowerShell installation:

```powershell
./src/Portability/Backend/NativeHost/Prepare-AvaloniaSource.ps1
```

The script verifies the archive hash recorded in the backend provenance. It recreates only its generated upstream directory. The build verifies and derives the two patched files separately.

Run the following shell commands with an absolute checkout path. `dotnet` must resolve to SDK 10.0.401; when using the workspace SDK, set `sdk="$repo/artifacts/portability/dotnet-linux-x64/dotnet"` instead:

```sh
set -eu
repo=/absolute/path/to/wpf
artifact="$repo/artifacts/portability"
export DOTNET_CLI_HOME="$artifact/native-host/cli-home-linux"
export NUGET_PACKAGES="$artifact/windowhost/packages-linux"
export NUGET_HTTP_CACHE_PATH="$artifact/windowhost/http-cache-linux"
sdk=dotnet
cd "$repo/src/Portability"
framework="$artifact/native-window-framework/"
"$sdk" restore Tests/NativeWindowIntegration/NativeWindowIntegration.csproj \
  --locked-mode -p:PortableFrameworkArtifactsDir="$framework"
"$sdk" build Tests/NativeWindowIntegration/NativeWindowIntegration.csproj \
  --no-restore -m:1 -p:PortableFrameworkArtifactsDir="$framework"
out="$artifact/native-window-integration"
fixture="$out/bin/Unix/Debug/net10.0/NativeWindowIntegration.dll"
font="$artifact/native-host/upstream/tests/Avalonia.Skia.UnitTests/Fonts/DejaVuSans.ttf"
```

The trailing slash on `framework` is required. This builds the real Core/PF/WindowsBase reference graph in the isolated directory; it does not reuse earlier framework outputs. Windows builds use `bin/Windows_NT/Debug/net10.0/` and the native fixture rejects execution there with its named Linux-only diagnostic. `AVA3001` and the documented upstream `CS0649` warnings remain visible.

## Run the native fixture

Select an unused display number. This subshell starts and stops only its retained Xvfb PID, disables TCP and keeps runtime files under artifacts. `-pn` supports WSLg's read-only `/tmp/.X11-unix` mount through the abstract Unix listener:

```sh
(
  set -eu
  ulimit -c 0
  mkdir -p "$out/runtime-xdg/cache" "$out/runtime-xdg/config" "$out/runtime-xdg/data"
  export XDG_CACHE_HOME="$out/runtime-xdg/cache"
  export XDG_CONFIG_HOME="$out/runtime-xdg/config"
  export XDG_DATA_HOME="$out/runtime-xdg/data"
  Xvfb :95 -pn -screen 0 1024x768x24 -nolisten tcp -ac > "$out/xvfb.log" 2>&1 &
  owned_xvfb_pid=$!
  trap 'kill "$owned_xvfb_pid" 2>/dev/null || true; wait "$owned_xvfb_pid" 2>/dev/null || true' EXIT
  sleep 1
  kill -0 "$owned_xvfb_pid"
  export DISPLAY=:95
  "$sdk" "$fixture" --font="$font" --settings=little > "$out/linux-xvfb.json"
  "$sdk" "$fixture" --font="$font" --settings=big > "$out/linux-xvfb-big.json"
  for variant in absent header count truncated type range duplicate value-type trailing; do
    "$sdk" "$fixture" --settings="$variant" > "$out/settings-$variant.json"
  done
)
```

Both normal runs must return zero with all 22 checks passed. Each of the nine negative-settings processes must return zero with its two rejection/completion checks passed. Unsupported explicit zero/subpixel geometry is an expected named rejection in this initial native profile, followed by supported-size recovery; valid one-pixel DPI transitions remain supported. Any unrelated native error still fails the run.

## Optional Openbox state and restore fixture

An EWMH window manager is required for `--wm-only`; Xvfb alone cannot qualify state or frame behavior. Use an already available Openbox, or the separately authenticated workspace extraction. The recorded qualification used Openbox 3.6.1-12build5 with its runtime closure unpacked under `artifacts/portability/native-window-wm/root`, without a system installation. For that extraction, prefix the run with:

```sh
wmroot="$artifact/native-window-wm/root"
export LD_LIBRARY_PATH="$wmroot/usr/lib/x86_64-linux-gnu"
export XDG_DATA_DIRS="$wmroot/usr/share:/usr/share"
openbox_bin="$wmroot/usr/bin/openbox"
openbox_config="$wmroot/etc/xdg/openbox/rc.xml"
```

For an existing system Openbox, use `openbox_bin=openbox` and its existing configuration, for example `openbox_config=/etc/xdg/openbox/rc.xml`. No installation or desktop settings changes are performed by these commands:

```sh
(
  set -eu
  ulimit -c 0
  mkdir -p "$out/wm-xdg/config" "$out/wm-xdg/cache" "$out/wm-xdg/data"
  export XDG_CONFIG_HOME="$out/wm-xdg/config"
  export XDG_CACHE_HOME="$out/wm-xdg/cache"
  export XDG_DATA_HOME="$out/wm-xdg/data"
  Xvfb :97 -pn -screen 0 1024x768x24 -nolisten tcp -ac > "$out/xvfb-wm.log" 2>&1 &
  owned_xvfb_pid=$!
  owned_wm_pid=
  trap 'if [ -n "$owned_wm_pid" ]; then kill "$owned_wm_pid" 2>/dev/null || true; wait "$owned_wm_pid" 2>/dev/null || true; fi; kill "$owned_xvfb_pid" 2>/dev/null || true; wait "$owned_xvfb_pid" 2>/dev/null || true' EXIT
  sleep 1
  kill -0 "$owned_xvfb_pid"
  export DISPLAY=:97
  "$openbox_bin" --sm-disable --config-file "$openbox_config" > "$out/openbox.log" 2>&1 &
  owned_wm_pid=$!
  sleep 1
  kill -0 "$owned_wm_pid"
  "$sdk" "$fixture" --wm-only --font="$font" > "$out/linux-openbox.json"
)
```

The run must return zero with all 13 checks passed, including direct state/DPI close followed by unrelated frames, maximized/minimized restoration, pending size/position intents and settled viewport-edge pixels. The recorded final matrix repeated this complete server/manager/process lifecycle ten times successfully. Standard output is machine-readable JSON; retain stderr separately when diagnosing failures.
