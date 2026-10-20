# Genuine WPF Window on the owned Linux backend

This fixture uses actual source-built WPF Window, controls, presentation source, retained renderer and InputManager with the owned X11 peer. Bootstrap is `NativeWindowHost.InitializeForCurrentThread()`. Independent XTest requests, X11 geometry/properties, selection peers and pixel readback observe native behavior. No other UI framework owns the controls.

Run only on a dedicated Xvfb server. X11Fixture publishes private XSETTINGS and refuses to replace an existing owner. Both byte orders, positive boundaries, the declared 5-pixel missing-distance fallback, malformed payloads and setting-name grammar remain covered. Controlled click thresholds test both signs of each axis at 4/5 physical pixels and 399/400 milliseconds through the actual WPF source provider, without claiming exact native sleep timing.

The default sequence preserves hidden construction, actual source/layout, synchronous premultiplied BGRA presentation, buffer validation, previous-frame overlap clear/clip, retained/expose/occlusion repaint, stale-cache and already-equal size requests, paired grow/shrink, zero/subpixel allocation/recovery, one-pixel/DPI round trips, exact RGB48 source preservation and repeated lifetimes. Real Button/TextBox input covers paired keys, all five buttons, held snapshots, outside release, capture transfer/loss/recovery, cursor visibility/shape, disable/enable, activation, Hide/Show, ownership, synchronous modal frames and canceled/accepted/reentrant close.

`--empty-window-only --chrome-dpi=96` isolates a genuine transparent borderless
notification-style Window: synchronous empty Show/Hide, item add/remove, native
pixels, same-source reopen and close cleanup. `--minimum-window-extents-only`
also checks both axes around one physical pixel, initial/live sizing, finite
maximum hints, invalid requests and unrelated-window recovery. Repeat at 144
DPI for fractional scaling. The default mode additionally covers the minimum
pixel DPI round trips. The existing watchdog and private-display rules apply.

| Focused mode | Required observable behavior |
|---|---|
| `--wm-only` | Real EWMH state/restore in both directions, pending geometry, state/DPI callback close and unrelated subsequent frames. Requires a real WM. |
| `--window-presentation-only` | Decorations, topmost and transparent pixels, resize, capture, Hide/Show and owner closure. |
| `--popup-only` | Genuine Classic ComboBox template, separate PopupRoot source, native mouse selection/dismissal, paired Enter/Escape/Tab/Shift+Tab, owner native focus, real grabs, movement, lifecycle and callback reentry. |
| `--mouse-placement-only` | Natural default ToolTip hover, native departure and reopen; Mouse/MousePoint anchors, cursor hotspots, both motion directions and four screen corners. Repeat with `--chrome-dpi=192`. |
| `--popup-placement-only` | Native edge nudging and content-height restriction. Startup scaling comes from native DPI policy/settings. |
| `--popup-reconciliation-only` | Real release over owner/popup/unrelated/root, queued reconciliation, recapture and unmap cleanup. |
| `--popup-alpha-only` | Both StaysOpen values, premultiplied ARGB32 storage, foreign-grab rejection and retry. |
| `--text-commits-only` | Actual BMP keyboard transport plus controlled whole-string/supplementary callback delivery, one routed event, binding, selection replacement, undo/redo, malformed rejection and disabled/hidden/closed isolation. Native Enter/Tab/Ctrl+A remain separate transport assertions. |
| `--owned-input-only` | XKB remapping, Shift/lock snapshots, keypad NumLock, dead/Multi_key compose, supplementary/scalar commits and cancellation through the native decoder and genuine editors. |
| `--clipboard-only` | Public Clipboard/IDataObject/editor paths, independent selections, Unicode, ownership replacement, PRIMARY isolation, snapshots, validation, cancellation, read-only commands and shutdown. |
| `--clipboard-manager-only` | Session-only copies/Flush stay prompt with responsive or stalled managers and do not request persistence. |
| `--file-dialog-protocol <directory>` | Actual Open/Save APIs/native owners against a private protocol-only portal. Accept/cancel/reuse, Unicode paths, filters/multiselect, final Save paths, FileOk veto/exception rollback, focus/modality and malformed/closed-request cleanup. |
| `--automation-only` | Genuine managed WPF peers, bounds, movement, DPI, focus, close and reopen. This alone does not prove exported AT-SPI. |
| `--atspi-only` | External busctl tree queries and real bound Button invocation, identity, disable/enable, focus, bounds, Hide/Show, pre-layout access and cleanup. |
| `--atspi-text-only` | External text/selection/editing protocol backed by an actual TextBox provider. |
| `--atspi-richtext-only` | Also selects the actual RichTextBox provider case. Qualification remains pending native PTS/managed bottomless document support. |
| `--atspi-disconnect-only` | Real owned-bus disconnect before the queued Closed callback, followed by actual Window/source cleanup. Requires the runner's exact bus PID/start identity. |
| `--primary-screen-only <scale>` | Independent root pixels, observed native screen snapshot, once-only DIP conversion and closed ComboBox metadata. |
| `--splash-only --dpi=96` or `192` | Existing SplashScreen resource/lifetime checks plus independent splash/focus observations. `--splash-unregistered` uses a fresh process for missing-backend failure. |

Popup serial checks construct owned X11 ABI payloads and enter actual core/XI2 handlers. They preserve obsolete/current/newer ordering, both crossing directions and native zero/maximum rollover. Temporary fixture observers count CaptureLost without releasing the independently held real server grab; original callback delegates/router state are restored before ordinary native activity resumes. No production test hook is added.

Native IME is deferred. IbusTransportChecks.cs is excluded from active compilation and its former mode fails explicitly. Its prior source/evidence cannot qualify composition for this backend. Ordinary native keyboard compose and whole-string commits remain required.

Build from src/Portability with SDK 10.0.401:

```sh
dotnet build Tests/NativeWindowIntegration/NativeWindowIntegration.csproj -c Release -p:RestoreLockedMode=true
```

Existing Tmds.DBus.Protocol is now a direct test reference after removal of the former transitive dependency. No test framework is added. Linux requires Xlib, XInput2, XTest, XFixes, RandR, GLib/GIO, libxkbcommon, fonts and the existing Skia/HarfBuzz native assets. Pass `--font=<absolute pinned DejaVuSans.ttf>` for editor modes. Portal/AT-SPI modes require owned buses and their WPF_FILE_DIALOG_PRIVATE_BUS=1/WPF_ATSPI_PRIVATE_BUS=1 guards. Never attach the protocol-only portal to a live desktop bus.

Each mode requires passing JSON, completion, zero process exit and cleanup. The default watchdog is 40 seconds. Keep exact source/binary/PDB and display/WM scope with results. Prior backend proofs remain historical. Fresh package consumers, GNOME X11, GNOME XWayland, composited ARGB appearance, actual picker UI and Orca are separate qualification gates.

Mouse placement reads one XFixes cursor image snapshot for physical screen
position, occupied alpha extent and hotspot. MousePoint uses the pointer itself;
Mouse clears the occupied cursor bottom using the existing WPF pixel margins.
The existing popup placement algorithm retains DPI conversion, work area
restriction and screen edge nudging. Private Xvfb/Openbox verification passed
all 59 checks: five mouse-placement checks at each of 96 and 192 DPI plus the
existing 49 tooltip, popup and window checks. Actual NINA input and physical
desktop qualification remain separate.

`--message-box-only` exercises the private managed MessageBox through public overloads. It uses actual XTest Enter/Escape/button input, all seven button sets and native close, owner-only modality/focus, repeated and concurrent ownerless calls while the original UI is blocked, exact offhost exception identity and post-failure recovery. The controller resolves concurrent dialog handles before dispatching close input, then joins before another case starts. This qualifies owned private-X11 software input, not physical input or the earlier incomplete Windows stock seven-set calibration.

`--tooltip-only` exercises genuine ToolTip construction, layout, native mapping,
close and reopen with fresh popup sources and pointer-capture cleanup. It also
checks both tooltip animation settings and their resource keys, including local
override/removal. The portable backend disables tooltip animation and fade using
the existing ComboBox animation policy; native Windows branches remain unchanged.
The resource probe enables transparency, matching ToolTip's own popup and its
animation coercion rules. The four focused checks and the existing popup,
window-presentation and ordinary native-window regressions pass on private X11.
Actual NINA capture remains a separate full-application check.
