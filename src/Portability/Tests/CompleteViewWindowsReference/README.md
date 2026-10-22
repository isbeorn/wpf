# Complete-view stock Windows reference

This wrapper links the exact `Packaging/Consumer/App/CompleteViewWorkflow.cs` and
`WorkflowResources.xaml` used by the portable package consumer. It implements only
the fixture's desktop adapter. It loads the selected `FixtureControls.dll` in the
ordinary Default load context and requires Microsoft WindowsDesktop 10.0.12.
The application identity is `App` so the shared resource URI remains unchanged.
Before Application construction it selects `RenderOptions.ProcessRenderMode =
SoftwareOnly`, matching the CPU rendering profile, and records that choice.

Behavior checklist:

- Verify the frozen Microsoft fixture hash or the prepared port compiler hash,
  compiler origin, linked source hashes and loaded framework identities.
- Use native mouse press/release and keyboard input on owned foreground windows
  for editing, validation, command enable/disable and synchronous accept/cancel dialogs.
- Observe native client pixels for image mutation/replacement, resource
  replacement/removal and animation forward/reverse/removal.
- Exercise fresh window lifetimes, grow/shrink and both synthetic DPI directions,
  close during animation then render an unrelated window.
- Return nonzero for a failed check, incomplete workflow or watchdog expiration.

Run from a Windows interactive desktop with SDK 10.0.401 and the exact stock
WindowsDesktop 10.0.12 runtime already installed. Prepare both fixture origins with
`eng/portability/Prepare-CompleteView.ps1` and the consumer build described in
the packaging documentation. The example uses the existing prepared closure; set
`$prepared` to another prepared root containing both per-origin `publish` folders.
No framework or NativeHost project is built by this wrapper.

```powershell
Set-Location E:/Projects/wpf/src/Portability
$prepared = 'E:/Projects/wpf/artifacts/portability/complete-view-lifetimes'
$output = 'E:/Projects/wpf/artifacts/portability/complete-view-windows-reference'
$env:DOTNET_CLI_HOME = "$output/cli-home"
$env:NUGET_PACKAGES = 'E:/Projects/wpf/artifacts/portability/windowhost/packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:/Projects/wpf/artifacts/portability/windowhost/http-cache'
New-Item -ItemType Directory -Force -Path $output | Out-Null

foreach ($origin in @('Microsoft', 'Port')) {
    & 'C:/Program Files/dotnet/dotnet.exe' build `
        Tests/CompleteViewWindowsReference/CompleteViewWindowsReference.csproj `
        "-p:FixtureOrigin=$origin" `
        "-p:FixtureControlsPath=$prepared/$origin/publish/FixtureControls.dll" `
        "-p:CompleteViewOriginPath=$prepared/$origin/publish/CompleteViewOrigin.json" `
        -v:minimal *> "$output/build-$origin.log"
    if ($LASTEXITCODE -ne 0) { throw "Stock wrapper build failed: $origin" }

    $process = Start-Process `
        -FilePath "$output/$origin/bin/Debug/net10.0-windows/App.exe" `
        -ArgumentList '--ordinary-apphost' `
        -RedirectStandardOutput "$output/$origin.json" `
        -RedirectStandardError "$output/$origin.stderr.log" -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Stock workflow failed: $origin" }
}
```

Launch the GUI apphost normally. Its own windows must be visible for native input
and pixel sampling. `dotnet App.dll` does not apply the embedded apphost manifest
and `Start-Process -WindowStyle Hidden` requests an unsuitable initial GUI state.
Run the two origins sequentially and avoid using the desktop during the run.
Before each input batch the adapter verifies foreground ownership. A covered
click point or refused foreground activation fails without injecting into another
application. The modal-owner isolation click may target the disabled owner while
its owned dialog has foreground. There are no functional click retries or global
input blocking. `--interactive` leaves the initial workflow window available for
manual use and disables the watchdog; it is not the automated completion gate.

The input implementation follows the existing DRT `TestServices/DrtBaseInput.cs`
SendInput scan-code and absolute virtual-desktop coordinate patterns. Text uses
the current keyboard layout with paired modifier/key releases and a native Unicode
fallback for unmapped BMP characters. The numeric workflow remains real native
input, not a direct `TextBox.Text` assignment or manually raised routed event.

The DPI reference sends `WM_DPICHANGED` only to the wrapper's own live HWND with
an owned suggested rectangle. `HwndTarget.HandleDpiChangedMessage` reads its DPI
and rectangle, `HwndSource.ChangeDpi` routes the genuine WPF notification and
`HwndTarget.OnDpiChanged` updates the root transform, render target and native
rectangle. The adapter waits for the resulting actual WPF scale. Its PerMonitorV2
manifest is process local. The JSON records the synthetic message and unchanged
`GetDpiForWindow` monitor value separately. No desktop DPI setting changes and no
physical monitor transition qualification are claimed.
When the shared fixture requests DPI before creating its next Window, this
adapter records the request and applies it once when that owned Window is first
observed mapped. This tests an owned synthetic message on a newly mapped Window,
not construction under a physical 144-DPI monitor. `ClientSize` reads actual
physical client dimensions with `GetClientRect`.

Each JSON includes the prepared origin manifest with original fixture source
hashes, compiler version, compiled input hashes, actual loaded assembly paths and
hashes, foreground diagnostics, key mapping observations, pixel changes and all
check outcomes. Pixel reads always query the native client DC; only duplicate
logging is suppressed. The five-second observable-result timeout and sixty-second
process watchdog are failure bounds, not functional timing assertions.
