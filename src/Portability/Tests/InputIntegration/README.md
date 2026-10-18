# Real WPF input integration

The Core fixture sends actual raw reports through the source-owned registered input provider. Existing InputManager staging, KeyboardDevice, MouseDevice and TextCompositionManager create the routed events and execute the KeyBinding command.

Behavior checklist:

- One input manager and device state per dispatcher and one combined provider per source.
- Real preview/bubble key routes, key release, repeat and live modifier state.
- Two-element keyboard focus, native acquisition failure and side-effect-free checkOnly.
- Native activation key snapshots, toggle transitions and stale pressed-state cleanup.
- Real mouse hit testing, all five button pairs and routed commands.
- Successful/failed capture, explicit release, unexpected loss and disposal cleanup.
- Capture reacquired by a real LostMouseCapture handler survives the original native release callback and remains releasable.
- Physical client/screen coordinates with a nonzero origin and asymmetric DPI.
- Rejection of foreign, disposed and unattached source mappings, cross-thread use and disposed providers.
- Inactive-source report isolation and cleanup when host capture release throws.
- Legacy BMP character reports plus atomic well-formed UTF-16 committed strings, through real composition events, TextBox editing and two-way binding.

The test hosts implement a managed host contract. They do not qualify native focus, X11 capture, native coordinate mapping or native committed-text delivery. The separate native host fixture owns those checks. Dead-key character reports, preedit/IME-client integration, accessibility peers and Windows Alt-numpad input remain explicit unsupported boundaries. Direct UIAutomationTypes identifiers still require a Windows accessibility backend.

Run from `src/Portability` with SDK 10.0.401:

```powershell
dotnet run --project Tests/InputIntegration/InputIntegration.csproj -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/input-framework/ -p:CustomAfterMicrosoftCommonTargets=E:/Projects/wpf/src/Portability/Tests/InputIntegration/CoreTestAccess.targets -p:BuildInParallel=false -m:1
dotnet run --project Tests/InputIntegration/InputControlsIntegration.csproj -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/input-controls-framework/ -p:CustomAfterMicrosoftCommonTargets=E:/Projects/wpf/src/Portability/Tests/InputIntegration/CoreTestAccess.targets -p:BuildInParallel=false -m:1
```

Use equivalent `/mnt/e/Projects/wpf/` paths and the bundled Linux SDK under WSL. Each fixture uses an isolated framework build and fixture-owned standard MSBuild hook for internal access. Normal product assemblies contain no test fixture access identifiers.

Regression evidence is saved under `artifacts/portability/input-*.log`. `input-buttons-red.log` reproduces the right/middle state mapping defect. `input-controls-red.log` records the original empty-text caret failfast. `input-pipeline-linux-red.log` records the eager Windows UIAutomation initialization failure before the bounded accessibility guard.

The controls fixture uses a real `AdornerDecorator` and checks that the actual caret visual remains attached to the presentation source while selection moves. It also updates an actual `Selector.SelectedValue` binding. The plain TextBox binding starts without constructing ComboBox; this verifies that unrelated bindings no longer trigger ComboBox's desktop-metrics initialization. Actual ComboBox default display metrics remain outside this fixture.

After building, run the controls binary with `--mouse-vanish` and `--mouse-vanish --reject-cursor` to verify profile-enabled cursor hiding/restoration and rejected native cursor changes. Physical capture loss preserves held button state. Deactivation clears device state, while failed native releases retain cleanup ownership for a retry.

`--mouse-vanish --override-cursor` checks the current override shape before hiding and changes it while hidden. The optional native visibility capability keeps shape and visibility separate: shape changes remain hidden until an accepted show request. The fixture also checks rejected and throwing show requests, cleanup retries, source changes, deactivation and close. Hosts without this capability return false. `--culture=he-IL` exercises the real bidi caret geometry in the same attached TextBox.

`--close-during-hide` checks callbacks that close the source before and after the native host applies a hide, including successful, rejected and throwing compensating show operations. A closed source cannot accept new provider visibility ownership. Successful native hiding is compensated through the borrowed host before returning failure to the caller.

Clipboard, IME composition, accessibility peers, native touch keyboard invocation and mouse-selection autoscroll timing remain unqualified. The profile's caret width, blink interval and mouse-vanish settings are explicit product preferences, not claims of desktop-preference parity.

`--unicode-commits --font=<pinned DejaVuSans.ttf>` uses the internal complete-string report through the registered source provider and existing TextCompositionManager/real editor path. It checks one exact routed payload, supplementary and combining text, selected replacement with one undo/redo, PreviewTextInput.Handled cancellation, empty/null/malformed input, unchanged legacy lone-surrogate/dead-key diagnostics, readonly/disabled/unfocused editing, inactive/foreign source rejection and source disposal/reacquisition. The existing pinned font and qualified U+1F600 glyph isolate editing from installed-font fallback coverage. Minimum/maximum supplementary scalar values are checked at the report boundary rather than requiring unassigned glyph rendering. This is committed-text support, not preedit or native IME-engine qualification.