# Windows stock WPF reference fixture

This self-checking executable records **Windows stock WPF 10.0.12** behavior.
It does not exercise this repository's WPF implementation, establish NINA
compatibility or qualify a Linux/macOS backend. Both shared framework versions
are pinned to 10.0.12 with runtime roll-forward disabled.

Run from this directory with the shared Portability SDK pin (10.0.401):

```powershell
$env:DOTNET_CLI_HOME = 'E:\Projects\wpf\artifacts\portability\cli-home'
$env:NUGET_PACKAGES = 'E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet build WindowsReference.csproj --configuration Release --artifacts-path E:\Projects\wpf\artifacts\portability\windows-reference --nologo
dotnet E:\Projects\wpf\artifacts\portability\windows-reference\bin\WindowsReference\release\WindowsReference.dll
```

The normal invocation runs seven isolated subprocess cases with a 20 second
watchdog per child. It prints one JSON object containing `probe`, `os`, `scope`,
loaded framework assembly identities and `checks[{name,status,detail}]`. A failed
check, invalid arguments, malformed child result or watchdog expiration produces
a nonzero exit code. `--case <name>` runs one case for diagnosis without the
parent watchdog. No external test packages are required.

The behavior checklist is:

- Compiled App.xaml and Window construct using a second assembly's compiled
  UserControl and component pack URI dictionary. Embedded BAML entries must exist.
- A real control template resolves its templated-parent binding. Model changes
  reach the TextBox and template; valid edits reach the model, invalid edits are
  rejected and subsequent valid edits clear validation errors.
- Bound command state disables and enables the Button. Its automation provider
  invokes the command once.
- Dynamic resource replacement changes the template brush. Removing the local
  replacement restores the merged dictionary brush.
- A bound odd-width BGRA bitmap accepts padded source rows. Lock, dirty rectangle
  and unlock preserve an updated pixel. RenderTargetBitmap output matches opaque
  image pixel readback exactly and the instantiated view renders nonempty pixels.
- Same-thread Send stays on the caller. Queued priorities and equal-priority FIFO
  produce the expected order.
- Nested frames allow reentrancy. DisableProcessing rejects a nested pump and
  disposal restores processing.
- The owning thread has access. Another thread is denied dependency-property
  mutation.
- Cancellation before enqueue and while pending prevents execution. Cancellation
  after completion preserves the completed result.
- Both immediate and queued dispatcher shutdown finish after the dedicated STA
  message loop exits, emit ordered events, abort pending work and reject new work.

The views are constructed and laid out without showing a native window. The
rendering checks are offscreen checks, not compositor screenshots. This fixture
does not cover native input, focus, IME, actual modal windows, monitor DPI changes,
third-party controls, NINA views, plugin loading, a full assembly API comparison,
hardware or long-running lifetime behavior. It compiles and loads fresh stock
BAML; it does not yet load archived NINA BAML or compare against a port runtime.
