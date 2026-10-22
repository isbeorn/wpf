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

The test hosts implement a managed host contract. They do not qualify native focus, X11 capture, native coordinate mapping or native committed-text delivery. The separate native host fixture owns those checks. Dead-key character reports, native IME-client integration, native accessibility export and Windows Alt-numpad input remain explicit unsupported boundaries. Managed accessibility peers and identifier metadata are exercised separately below.

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

Clipboard, native IME composition, native accessibility export, native touch keyboard invocation and mouse-selection autoscroll timing remain unqualified. The profile's caret width, blink interval and mouse-vanish settings are explicit product preferences, not claims of desktop-preference parity.

`--unicode-commits --font=<pinned DejaVuSans.ttf>` uses the internal complete-string report through the registered source provider and existing TextCompositionManager/real editor path. It checks one exact routed payload, supplementary and combining text, selected replacement with one undo/redo, PreviewTextInput.Handled cancellation, empty/null/malformed input, unchanged legacy lone-surrogate/dead-key diagnostics, readonly/disabled/unfocused editing, inactive/foreign source rejection and source disposal/reacquisition. The existing pinned font and qualified U+1F600 glyph isolate editing from installed-font fallback coverage. Minimum/maximum supplementary scalar values are checked at the report boundary rather than requiring unassigned glyph rendering. This is committed-text support, not preedit or native IME-engine qualification.

## Managed automation peers

The controls executable accepts `--automation --automation-results=<absolute JSON path>`. It constructs genuine Button, TextBox, CheckBox and Window peers through `UIElementAutomationPeer.CreatePeerForElement`, checks retained identity, current names/roles/children and exercises actual Invoke, Toggle and Value providers against real commands and two-way bindings. Enabled/disabled/re-enabled and editable/read-only/editable transitions must preserve WPF behavior. The existing TextBox Value implementation passes these binding checks without a production change.

The source-attached peer checks use the existing managed host fixture style. Bounds and clickable points follow the ordinary element/root/device/screen transforms once, including a skewed child, asymmetric DPI and a moved physical origin. Detachment/closed sources return empty bounds and a NaN clickable point; peer objects remain ordinary managed WPF objects rather than exported native nodes. Peer focus uses the actual WPF keyboard path. The separate NativeWindowIntegration `--automation-only` mode checks the same bounds/focus/lifetime path against real dedicated-Xvfb native windows at 96 and 192 DPI, including a native move and fresh reopen.

Portable Window names use explicit AutomationProperties.Name, the real label peer or the native Window Title. Its peer bounds describe the WPF client viewport through the actual source, without fabricating an HWND or Windows nonclient frame. Ordinary Windows implementations remain unchanged.

Managed identifier IDs are the source's declared schema baseline, independent of probing Windows automation libraries. NotSupported and MixedAttributeValue have distinct stable managed identity. Reading this metadata does not implement TextPattern, native UIA, AT-SPI, Orca, registration or accessibility event transport. Native UIA export through ProviderFromPeer throws a named PlatformNotSupportedException, ClientsAreListening stays false and no event backend is installed. The Core fixture retains that explicit negative boundary while allowing managed peer construction. Linux checks require no Windows native modules; Windows records the pre-peer runtime USER32 baseline and requires no UIAutomationCore or PresentationNative loads from these peer operations.

## Managed preedit sessions

`--managed-preedit --font=<pinned DejaVuSans.ttf>` exercises the dispatcher/source-owned managed session with a genuine focused, templated TextBox, two-way PropertyChanged binding and retained composition adornment. Complete candidate strings and caret offsets use UTF-16; malformed strings, split surrogate caret positions and stale generations are rejected. The real editor owns tentative replacement, original selection, cancellation restoration and one commit undo/redo. Tentative TextChanged notifications remain observable while ordinary automatic binding writes are deferred until the final or restored value. Explicit UpdateSource, arbitrary application callouts, custom binding expressions and RichTextBox are outside this contract.

The managed policy cancels on focus loss, source deactivation or accepted closure. Canceled Window.Closing retains the session. Routed application text or selection changes abandon stale ownership without restoring over application edits. Cancellation restores an unchanged owned range even when undo is disabled and preserves unrelated earlier undo. Readonly/disabled editors, active application edit transactions and reentrant starts during teardown are rejected. Queued adornment updates are canceled with the session.

The checks include handled and PendingComplete routed events, nested generation replacement, replaced bindings, source handoff, dispatcher affinity, real managed Window closing and actual underline pixels. The fixture uses managed host peers and does not qualify a native IME client, engine, reconversion, native candidate geometry or native preedit delivery. Backend IME capability remains unadvertised. Windows native editor paths are preserved.

### Hidden managed preedit

The same `--managed-preedit` fixture also checks source/generation-validated visibility. Hide restores the original selected text and selection, removes the real composition adorner and retains the active generation plus the latest complete candidate/caret. It does not mean cancel or a visible empty candidate. Show reapplies the latest candidate over the original range. A hidden commit edits that original range once; one undo/redo restores/reapplies the final edit. Hidden cancellation ends ownership normally.

The internal provider supports an atomic visibility-bearing Begin/Update for initially invisible native packets, avoiding a candidate-text flash. Ordinary Update preserves the current visibility. Visibility-only Hide/Show is idempotent and does not manufacture routed Start/Update/Complete events; real packets keep their existing routed lifecycle even while hidden. Real TextChanged callbacks can observe restoration and resumption, while ordinary automatic two-way source updates remain deferred. The owner's final source-publication call rejects a reentrant same-editor Begin until that callback returns, preventing a candidate from being overwritten by the in-flight binding update or causing duplicate source writes. A new composition can start afterward.

Undo-enabled and disabled paths, unrelated prior undo, combining/supplementary original text, hidden empty candidates, foreign edits/bindings, restored-TextChanged reentry, readonly/disable/focus loss and canceled/accepted Closing are exercised through genuine controls. No native client, focused-editor lease/caret attachment or IME-engine capability is advertised by these managed checks.