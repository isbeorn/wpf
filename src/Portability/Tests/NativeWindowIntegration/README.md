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
- Actual XSETTINGS origin, both byte orders, per-field published/fallback provenance and failure for missing time or malformed preferences.
- Watchdog, nonzero failures and completion guard; all native dispatcher regressions remain required, including same-thread Send and DisableProcessing boundaries.

Build the complete graph with a unique absolute `PortableFrameworkArtifactsDir` ending in `/`, for example `artifacts/portability/native-window-framework/`. Do not reuse canonical outputs predating the current Core/PF input/window contracts. Outputs and intermediate files for this fixture and backend are separated by OS.

The executable accepts `--font=<absolute verified pinned DejaVuSans.ttf>` and `--settings=little|big`. It creates an actual XSETTINGS owner/property with declared test values: `Net/DoubleClickDistance=5` physical client pixels and `Net/DoubleClickTime=450` milliseconds. It records the exact selection owner XID/property origin. Those are fixture values, not desktop preferences. The fixture refuses to replace an existing XSETTINGS owner; run it only on its dedicated Xvfb server.

`--settings-only` exercises genuine NativeWindowHost initialization and verifies that resolved values and per-field provenance reach the existing immutable WPF pointer snapshot. Generated `missing-distance-little` and `missing-distance-big` cases publish time=400 and verify the declared 5-physical-pixel fallback. `published-little` and `published-big` retain distance=11/time=725; `published-min` and `published-max` preserve positive distance boundaries. No published positive distance is replaced by a default.

`--settings=captured-gnome --settings-payload=<absolute xsettings.bin> --settings-only` installs the unchanged captured GNOME payload on the fixture-owned server. It requires 2044 bytes and SHA-256 `B8D5495AD53FA16EC0E96CBB5C9D8502E4DE59DFDB2BED357307FE9C6C412F57`, preserving its 52 original entries. The observed time=400 is published; the missing distance receives the declared fallback. This never reads or replaces a live GNOME selection.

`--settings=absent|header|count|truncated|type|range|duplicate|value-type|trailing` exercises real missing or malformed server properties, including duplicate preferences, a recognized string value where an integer is required and extra trailing bytes. These cases succeed only when native initialization rejects the property. No production test friend or test-only WPF interface is added.

Additional rejection cases are `absent-property`, `missing-time`, `both-absent`, `distance-duplicate`, `distance-value-type` and `distance-range`. Prefixing `header`, `count`, `under-count`, `truncated`, `type`, `value-type`, `range`, `duplicate` or `trailing` with `missing-distance-` verifies that the fallback cannot mask corrupt or incomplete payloads.

Name grammar cases use `name-<case>-little` or `name-<case>-big`. The cases `illegal-distance`, `unknown-illegal`, `nonascii`, `nul`, `empty`, `leading-slash`, `trailing-slash`, `empty-segment`, `digit-start` and `digit-segment` must reject the entire property despite a valid published time=400. The `valid` case accepts unknown `Other/valid_name0`, `_111/_background` and `_background` entries while applying the absent-distance fallback. All names are checked before decoding, including unknown entries.

`--settings=missing-distance-little --click-thresholds-only` uses actual native windows at 96/192 DPI and exact controlled raw reports through their real source providers. Routed click counts check physical deltas +/-4 and +/-5 on both axes and timestamp deltas 399/400ms. These are deterministic input-pipeline boundary checks, not claims of exact XTest/server timing or sleep accuracy. Existing mouse comparisons remain strict, so delta=5 or time=400 resets the count.

`--wm-only` runs the retained frame/image checks and genuine Window state/restore cases under a real EWMH window manager. The local qualification uses workspace-extracted Ubuntu Openbox on its own Xvfb server, with archive signatures, hashes and runtime closure recorded under ignored artifacts. It checks actual `_NET_WM_STATE`, mapped state, geometry and native pixels. This is separate from the no-window-manager Xvfb input/control run.

`--window-presentation-only` checks live `WindowStyle.None`/`SingleBorderWindow` changes, transparent Window pixels, Topmost changes and public transparency validation. On owned Openbox it also requires applied frame extents and matching native/WPF outer geometry through both decoration changes, plus actual stacking changes without activation. The transparent-window cases check alpha, repaint, resize, capture, hide/show and owner closure. ARGB storage on Xvfb does not qualify composited desktop appearance.

`--file-dialog-protocol <absolute artifact directory>` exercises actual WPF Open/Save APIs and genuine native owners against a small protocol-only portal on an explicitly owned session bus. Its runner sets `WPF_FILE_DIALOG_PRIVATE_BUS=1`, uses dedicated Xvfb and a private bus configuration without service activation directories. It never connects its fake portal to the live GNOME bus. Existing Tmds.DBus.Protocol is reused, with no new test framework or installed service. The tests cover accept/cancel/reuse, Unicode local paths, filters/multiselect, native final Save paths, `FileOk` tentative values/veto/exception rollback, real modality/owner state/focus restoration, reentry/thread rejection, malformed responses without bus disconnection, owner close before/after the method reply and explicit request-close/unavailable failures. Cancel responses intentionally contain no `uris`. This qualifies protocol and WPF state behavior only; real picker UI, native extension/overwrite interaction and NINA desktop flows require a fresh committed package on the VM.

The fixture supplies explicit simple Button/TextBox templates to isolate the native host boundary. Full application theme resources, mixed per-axis DPI, non-BMP text, full IME composition and physical desktop qualification remain separate work. Check the emitted JSON before treating any Window gate as passed. Reflection is confined to fixture diagnostics and existing internal boundary checks: the peer frame/cursor contracts, the actual input provider's hidden-cursor cleanup state and retained drawing guidelines. It adds no production test friend or interface. Native cursor image checks complement the managed control fixture's OverrideCursor/typing policy checks; they do not claim a native typing profile with MouseVanish enabled.

## Unicode clipboard profile

`--clipboard-only` runs the genuine public `Clipboard`/`IDataObject` path and unchanged TextBox Copy/Cut/Paste commands on a dedicated X11 server. Separate child processes own and request real X11 selections. The checks cover Unicode including supplementary characters, external ownership replacement, Clear, original-object `IsCurrent`, independent PRIMARY contents, text snapshots, conflicting aliases, unsupported export, Copying/Pasting cancellation and two-way text binding. The TextBox uses the pinned font's supported Latin/Greek repertoire; the selection-only cases also transfer Japanese and supplementary characters without making a font-rendering claim.

Native export is a validated Unicode text snapshot. Text, UnicodeText and System.String are accepted as text aliases; conflicting values and nontext formats reject before changing the selection. Mutating the input IDataObject after copying does not mutate the native snapshot. There is no OLE delayed rendering or COM transfer. Numeric DataFormats registration remains outside this profile; no custom native IDs are invented. CLIPBOARD is independent of PRIMARY.

Copies use the pinned backend's native best-effort/session behavior. SetText, Copy/Cut, `copy: true` and Flush do not guarantee that data survives process exit. The existing backend attempts available clipboard-manager storage when its own conditions permit; successful task completion is not evidence of persistence. These are the selected native semantics, not Windows OLE persistence emulation.

The pinned X11 source derivative bounds the optional SAVE_TARGETS acknowledgement to one second after actual CLIPBOARD ownership. A missing acknowledgement completes successfully with session ownership intact; actual native errors remain visible. `--clipboard-manager-only` isolates the real manager protocol cases: immediate acknowledgement, an unresponsive manager during actual bound TextBox Cut, external text transfer, later replacement and Clear. These cases also run in `--clipboard-only`. The manager fixture acknowledges the protocol without claiming persistent storage.

Synchronous WPF calls wait through a controlled WPF DispatcherFrame on the existing GLib/X11 pump. Native operations require the registered dispatcher thread and enabled dispatcher processing. A stalled external selection owner verifies that shutdown unwinds the pending public call instead of waiting for the native selection timeout. Run this mode separately because its final case intentionally shuts down the dispatcher. Every child selection process is closed and reaped even on an assertion failure.

`--clipboard-managed-only` runs without a native host or display. It exercises genuine DataObject constructors, IDataObject interface dispatch, wrapping, format enumeration, typed access, JSON retrieval and autoConvert. It verifies that COM conversion is explicitly unsupported and records the loaded source-built System.Private.Windows.Core identity, hash and IL-only metadata. The normal framework graph selects that genuine helper at compile and runtime; the stock Windows runtime image is not a Linux managed execution asset. The initial diagnostic override and copied-closure evidence remain under `artifacts/portability/clipboard-investigation/`. Fresh package inclusion and desktop qualification require their own evidence.

These checks qualify virtual X11 selection transport. GNOME XWayland interoperability and desktop clipboard-manager persistence remain separate observations.

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

Both normal runs must return zero with all 23 checks passed. Each of the nine negative-settings processes must return zero with its two rejection/completion checks passed. Unsupported explicit zero/subpixel geometry is an expected named rejection in this initial native profile, followed by supported-size recovery; valid one-pixel DPI transitions remain supported. Any unrelated native error still fails the run.

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

`--text-commits-only --font=<pinned DejaVuSans.ttf>` first activates a genuine X11-backed WPF TextBox using ordinary XTest keyboard input, which must insert exactly once. It then invokes the existing NativeWindowPeer raw-input callback with a complete upstream RawTextInputEventArgs. Actual routed TextInput, selection replacement, two-way source values and one undo/redo must preserve the whole multi-character/supplementary commit. It also checks empty/malformed input, PreviewTextInput.Handled and disabled/hidden/closed native-window isolation. Reflection is confined to this controlled callback test and uses the same existing peer/implementation objects, with no production test hooks. This qualifies the callback-to-editor seam, not a real XIM/IBus/Fcitx engine or preedit session. The normal native keyboard test remains the transport proof for ordinary BMP keys.
The same focused native mode also sends actual XTest Return to single-line and multiline editors, default non-inserting Tab and Ctrl+A followed by selection replacement. JSON observations retain routed Text versus ControlText, the actual source value and selection state. Controlled callback checks cancel Return, Tab and Ctrl+A payloads at PreviewTextInput and require exact ControlText with empty ordinary Text. A one-character control commit keeps its prior route, so native key commands do not insert twice. Accepted Tab layout remains outside this slice: a preserved real-key replay reaches the existing explicit portable paragraph-tab-stop diagnostic. Complete printable strings remain unsplit. The pinned-font editor payload uses U+1F600; a separate U+1F642 replay is preserved as an existing uncovered-grapheme/font-directory limit rather than a transport success claim.
The bounded `--automation-only` mode reads actual managed WPF Window/Button peers and uses the existing native fixture lifetime, polling and private XSETTINGS owner. It verifies real child identity, title/role, client-viewport screen bounds before/after native movement, 96/192 DPI, peer-driven WPF focus and empty/NaN bounds/clickable points after native close. It reopens a fresh Window at the second DPI. This is an in-process managed peer/source qualification on a dedicated Xvfb server. It does not export AT-SPI objects, exercise Orca or claim native document TextPattern support. No live desktop input or settings are required.

## Window/Button AT-SPI profile

`--atspi-only` requires `WPF_ATSPI_PRIVATE_BUS=1` and an explicitly owned `dbus-run-session` plus dedicated Xvfb server. It uses installed `busctl` child processes to read the actual accessibility registry/tree and invoke the real bound WPF Button command. The existing pinned Tmds.DBus.Protocol client holds a separate real accessibility-bus subscription to verify WPF focus notifications. It adds no server, dependency or event loop.

The bounded checks cover Window/Button names, roles, stable identity, live name reads, WPF CanExecute disable/enable, dispatcher-owned Invoke, focus gain/loss/readback, focus-gain notifications, physical screen bounds at 96/192/96 DPI with movement, Hide/Show visibility and identity, close detachment and fresh reopen/action/close. The one non-rendered Avalonia Control is only the existing X11 server's root attachment. Cached peer facades forward to genuine WPF peers and providers; no Avalonia control tree is rendered or laid out.

Set `WPF_ATSPI_OBSERVATIONS` to an absolute ignored artifact path to retain raw protocol replies and measured bounds. The existing service startup remains optional, so an unavailable accessibility bus does not block ordinary WPF input. An external child query before first layout is tested on the existing native dispatcher queue, followed by genuine WPF layout/Loaded. One initial Loaded notification resets an existing real peer child cache and refreshes the native tree; it creates no peer when the service is absent. A separate close-before-Loaded case verifies detachment before the pending layout queue drains. Show/Hide retains protocol identity; native Close removes the tree before root subscriptions and cached facades are released. Other peer roles are unqualified custom nodes and only actual Invoke is exposed. Text/value/editable-text patterns, dynamic child/property notifications, text ranges, Orca speech and GNOME/XWayland screen-reader qualification remain later gates. The pinned server's native focus-gain notification policy is retained; this slice does not promise a Windows UIA event stream.

`--primary-screen-only <scale>` compares the actual backend screen bounds with independent X11 root geometry on a dedicated single-screen Xvfb server, then checks both registered DIP metrics and genuine closed ComboBox height metadata. Run separate processes with `AVALONIA_GLOBAL_SCALE_FACTOR=1` and `1.25`, clearing competing process scale variables. JSON retains raw pixel bounds, the actual primary flag, selection provenance and scale. The initial native evidence covers the sole-observed-screen branch. Marked-primary and ambiguous multi-screen selection remain explicit qualification gaps; this fixture does not designate a primary or change a live desktop. No popup opening or monitor hotplug is covered.

## Bounded native IBus transport

`--ibus-transport <absolute-results.json>` requires `WPF_IBUS_PRIVATE_BUS=1`, a dedicated Xvfb and an owned session bus with activation/service directories disabled. Its C# portal/context counterpart sends actual IBus wire signals to the pinned native client selected by `IWindowImpl.TryGetFeature`. No real engine or focused WPF editor is attached. Existing Tmds.DBus.Protocol and the existing native fixture are reused.

The transport preserves full text, the once-converted UTF-16 caret and native visibility. Its internal Update/Show/Hide event keeps cached candidate text/caret across Hide/Show. The legacy Avalonia callback keeps its upstream behavior: Update receives full text/caret even when Visible=false, Hide receives empty text/caret zero and Show is a no-op. The private event preserves visibility independently; no WPF cancellation or hidden-document policy is inferred. Same-text visible/hidden caret changes and a visible empty candidate are separate assertions. Actual Preedit/Commit/Preedit order stays P,C,Q. Commit and real key filtering retain the original native paths, with no second FIFO.

`ChangeClientAsync` detaches immediately, uses the existing D-Bus queue's actual Reset reply and attaches only the latest requested target. `ResetAsync` preserves the current target through that same barrier. Superseded requests complete canceled. The native Reset guard suppresses composition during reset; error, disconnect and disposal complete pending tasks and remain detached. This is a bounded protocol barrier, not a claim that arbitrary signals after its acknowledgement carry an editor generation.

The ordinary mode also holds a genuine ProcessKeyEvent reply while preedit and Commit arrive, then verifies filtered native down/up. Separate `--case=reset-failure`, `--case=queue-failure`, `--case=disconnect` and `--case=dispose` runs prove pending task completion through actual method error, preceding queued cursor failure with remote Destroy held, name-owner loss and disposal. `--factory=ibus`, `disable`, `gtk-disable`, `fcitx`, `fcitx5`, `gtk-precedence`, `qt`, `xmodifiers` and `missing-connection` start separate native processes to preserve environment selection, disablement and missing-connection behavior. These process-local settings do not change desktop settings. Original XIM fallback remains in the hash-guarded pinned initialization path.

The source-derived client reuses packaged FreeDesktop base/proxies and the original connection. Only an originally detected typed IBus factory is replaced, before any native context is created. No WPF/native editor attachment, managed preedit session change, real-engine qualification, candidate-window support or editable accessibility capability is included. The original artifact observer and initial meaningful caret red are retained separately.

The lifetime regression modes `--held-create` and `--held-create --hold-destroy` close the actual native Window before CreateInputContext replies, then require one late-context Destroy, no post-disposal context calls and no delivered signal. Connecting resources remain local until every subscription await completes while alive; incomplete connections dispose partial subscriptions and destroy their own returned context without relying on the base connected flag. Native callbacks capture their context identity and require the current eligible context. Terminal failure clears that identity before releasing pending operations, so late signals cannot escape while remote teardown is held.

`--case=reset-signals` audits visible/hidden preedit, Show, Hide, Commit and actual ForwardKeyEvent A down/up while Reset is pending. The ordinary attached control must still deliver A KeyDown/KeyUp through the original native forwarding route. Reset failure, queued cursor failure with Destroy held, disconnect and disposal repeat the same signal audit. This adds no input FIFO, native context or editor attachment. The original review reds and freeze remain immutable under separate evidence paths.

--taskbar-only uses the existing native Window loop under owned private Xvfb without a WM. It independently reads the unmapped _NET_WM_STATE_SKIP_TASKBAR hint, captures mapped remove/add EWMH ClientMessages on the fixture connection and verifies hidden edits/re-show, stable XID/source, WM_TRANSIENT_FOR ownership and native focus. These are native taskbar requests, not WM acknowledgements or desktop taskbar/Alt-Tab presentation qualification. No Win32 hidden-owner behavior or AvalonDock changes are introduced.

## Genuine native Popup profile

`--popup-only --font=<pinned DejaVuSans.ttf>` uses the actual Classic noneditable ComboBox template in a shown genuine Window. XTest clicks its toggle, then paired native keys exercise the separate PopupRoot source, owner-native-focus retention, item navigation, commit, cancel, Tab/Shift+Tab item cycling and closed-owner traversal. The original target has a 20 DIP margin; `--popup-origin-zero` is a separate diagnostic rather than a replacement for that placement gate. The fixture references the genuine same-graph Classic project so normal generated dependency metadata identifies the loaded theme.

The bounded adapter uses WPF's existing placement, layout, capture and close paths with a real X11 popup. Mouse cases cover repeated native selection and outside dismissal, capture restoration/transfer, unrelated-window recovery, target detach, canceled/accepted owner close, owner movement and active-owner Hide/Show, native focus departure and reentrant Opened/Closed handlers. Private observer checks cover obsolete core and XI2 grab-crossing serials in both directions and native-width wrap boundaries. Actual Unmap loss must release both real capture and its adapter owner reference. These checks run only on an explicitly owned Xvfb server.

`--popup-placement-only` measures bottom/left/right screen nudging and tall-content restriction against actual server bounds. Run fresh processes at scale 1, 1.25, 1.5 and 2, clearing competing scale variables. `--popup-reconciliation-only` checks real capture release over owner, popup, unrelated owned source and root, including recapture before the queued callback. `--popup-alpha-only` verifies both StaysOpen values, actual ARGB32 premultiplied framebuffer bytes, initial foreign-grab rejection and successful retry. A compositor-less server qualifies storage and input only; composited appearance remains open.

`--popup-mouse-only` explicitly omits the keyboard workflow when diagnosing a separate prerequisite. It must not be reported as a complete Popup result. Original dashed painting/bounds failures and the incorrect one-Tab-immediate-close assertion remain preserved. A separately guarded original WindowsDesktop10.0.12 Classic probe measured public managed Tab moving between item tab stops and wrapping while open, with Escape closing before owner Tab traversal. The native fixture verifies that contract through actual paired XTest Tab/Shift+Tab, Enter/Escape and owner source routing; the stock public ProcessInput/MoveFocus evidence is not OS keyboard qualification. No template, focus visual or bounds calculation is bypassed. Multi-monitor placement, monitor changes, mouse-relative placement, Popup IME, popup accessibility and real desktop/compositor behavior remain unqualified. The active Linux owner-hide observation does not establish parity with the separately calibrated background/unowned stock Window path.
The callback group also opens two genuine simultaneous StaysOpen popups on one owner and obtains actual capture on the second child. Neither, first-only, second-only and both throwing Closed handlers must destroy both popup sources/XIDs and the owner source/XID, release capture and propagate the first original exception with its callback stack. State is recorded before best-effort fixture cleanup, so cleanup cannot mask a leaked owner.
