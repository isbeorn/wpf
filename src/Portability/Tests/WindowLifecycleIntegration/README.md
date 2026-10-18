# Real Window lifecycle fixture

This fixture constructs the source-built `System.Windows.Window`, `PortablePresentationSource` and actual WPF dispatcher. Its peer is a managed contract implementation. A pass proves managed lifecycle and raster integration, not native window creation, native modal isolation, hardware input or theme assembly compatibility.

The portable Window profile uses WPF's existing `StyleHelper.GetThemeStyle` metadata fallback. Its default style retains the stock Window shape from `PresentationFramework.Aero2/Themes/Aero2.NormalColor.xaml`: Border with TemplateBinding for Background, BorderBrush and BorderThickness, then AdornerDecorator and ContentPresenter. Foreground and Background retain dynamic WindowTextBrushKey/WindowBrushKey resource references. Explicit style and template values retain precedence. The normal Windows build does not use this profile.

Behavior checklist:

- Real constructor and missing-backend diagnostic.
- Source initialization, show/hide reuse, activation and exactly one close/disposal.
- Title and outer size before/after showing, min/max constraints in both directions and client/frame conversion.
- Owner add/remove and owned-window close through existing WPF collections.
- Cancelable native close request, accepted close, required synchronous disposal acknowledgement, missing/duplicate acknowledgement, close failure and forced shutdown policy.
- Real synchronous DispatcherFrame with ComponentDispatcher modal scope, routed DialogCancelCommand, nested owners, canceled result, hide, early SourceInitialized close and modal-enter hide/close before frame creation.
- Restore previous enabled/active states after normal completion, show failure, dispatcher failure and enter/leave modal event failure.
- Capture release can close/hide an owner or change IsEnabled. Restoration attempts every surviving owner even when one restore fails, clears every modal flag and preserves the original body/PopModal exception before cleanup failures.
- Real whole-Window measure/arrange, client content layout, Loaded once, ContentRendered, red raster pixels, hidden/host resize and DPI changes with stable logical layout.
- Dynamic resource replacement, explicit style precedence and local template replacement.
- Application.Windows/MainWindow/OwnedWindows collection cleanup, including child Closing/Closed exceptions and sibling cleanup.
- Public `Application.Run()` and `Run(window)` in separate processes, with Startup/Exit ordering, Startup arguments, MainWindow, queued Show, return/Exit override codes, all three shutdown policies, canceled last/main closes, forced shutdown, affinity validation and singleton/repeated-Run rejection.
- RestoreBounds tracks normal geometry independently from displayed min/max geometry, including partial-axis edits, both callback orders, both state transitions, raw restore requests outside constraints and clamping when Normal geometry is confirmed.
- Bounds edits from a Normal StateChanged handler wait for coherent Normal metrics, including externally observed state changes.
- Width/Height retain the stock distinction between application-requested min/max transitions and externally observed transitions, which restore the dimension properties from RestoreBounds.
- Physical desktop positioning uses the current root DPI independently on each axis, with negative origins, explicit zero placement and existing WPF LocationChanged behavior.
- LocationChanged handlers can edit all four bounds axes, close, hide or request a state transition during a live move or Normal restoration in either callback order.
- Initial requested outer size remains pending until frame insets become known after first map.
- Initial pending sizes do not require an unchanged Show metrics event to preserve programmatic min/max dimension semantics. SourceInitialized edits and failed initialization retries run with and without that event.
- SizeToContent Manual, Width, Height and WidthAndHeight follow actual content layout through growth/shrink, independent axes, min/max constraints and return to Manual. Automatic sizes share pending native bounds acknowledgements, including stale/older callbacks and Manual return before the outstanding resize completes.
- Fractional automatic content sizes retain accepted floor/ceiling physical pixels through repeated layout, with both axes independent, asymmetric DPI, constraints and later content changes. Unchanged desired content does not overwrite acknowledged actual RestoreBounds or resend requests.
- Native size/position calls are requests. Each pending axis survives stale Normal metrics and older acknowledgements until the latest request matches actual geometry. Sent requests are tracked independently so unchanged snapshots do not cause endless retries.
- Every newly pending size or position intent issues one peer request even when the first Normal snapshot already matches. A transient match before a late window-manager restore is not an acknowledgement of an unsent request. Tests cover both axis pairs, max/min, callback orders and clamped sizes without retrying unchanged layout.

Native boundaries remain explicit. The initial profile does not support HWND interop, WindowStyle changes, ResizeMode changes (including CanResizeWithGrip), transparency, taskbar customization, icons, topmost, startup centering, FlowDirection changes or Fluent ThemeMode. These features throw named PlatformNotSupportedException errors rather than falling through to SourceWindowHelper. Native peer integration and raw input are separate fixtures. The managed fixture explicitly registers the portable light palette and test-only pointer preferences of 4 by 4 raw client pixels and 500 milliseconds before WPF reads settings. Those values are not native preference evidence.

Portable Application.Run uses the existing WPF Startup, dispatcher, shutdown and Exit machinery without creating the Win32 parking HWND. Application-wide OS Activated/Deactivated and SessionEnding delivery, plus Windows CommonDialog ownership through that parking HWND, remain unqualified. This does not synthesize an application activation/session bridge or an HWND. Ordinary upstream Windows builds retain the original parking-window path. Window activation is tested separately through the peer contract.

`ApplicationRunCases.cs` is shared unchanged with the stock oracle. Each scenario starts a fresh process because WPF permits only one Application lifetime per AppDomain. It invokes the public Run overloads, not a replacement Dispatcher.Run or Exit adapter. Normal exit clears Application.Current and MainWindow before dispatcher shutdown completes. The exceptional Exit-handler case preserves the stock behavior: the original exception escapes Run after ownership cleanup and ShutdownStarted, while ShutdownFinished has not occurred on that exceptional unwind.

## Run on Windows

From this directory with .NET SDK 10.0.401, keeping package and CLI caches under the repository's artifacts/portability directory:

```powershell
$env:NUGET_PACKAGES = 'E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME = 'E:\Projects\wpf\artifacts\portability\dotnet-home'
& 'C:\Program Files\dotnet\dotnet.exe' build `
  -p:CustomAfterMicrosoftCommonTargets=E:/Projects/wpf/src/Portability/Tests/WindowLifecycleIntegration/WindowTestAccess.targets `
  -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/window-lifecycle/framework-variant/ `
  --artifacts-path E:/Projects/wpf/artifacts/portability/window-lifecycle/fixture
& 'C:\Program Files\dotnet\dotnet.exe' E:/Projects/wpf/artifacts/portability/window-lifecycle/fixture/bin/WindowLifecycleIntegration/debug/Wpf.Portability.WindowLifecycleIntegration.dll
```

On Linux use the same build properties with absolute Linux paths and a separate artifacts variant, then run the produced DLL with the Linux .NET 10 host. The fixture sets STA only on Windows. It makes no Linux STA claim.

`WindowTestAccess.targets` is a fixture-owned standard MSBuild extension hook. It grants the signed fixture internal access only in the explicitly isolated Core/PF variant selected above. Normal product wrappers do not emit this fixture friend. Do not point this hook at the canonical framework output directory.

JSON output contains probe, os, scope and named checks. Any failed check returns a nonzero exit code. The original Linux constructor failure is recorded under artifacts/portability/window-lifecycle-linux/construction-before.json. Modal cleanup and missing content template failures before their repair are recorded under artifacts/portability/window-lifecycle/windows-before-cleanup.json.

The public Run pre-fix reproduction is retained under `artifacts/portability/application-run-investigation/red-linux-results.json`: the existing 59 cases passed and all seven new Run cases failed at EnsureHwndSource/HwndWrapper/RegisterWindowMessage before Startup. The same public cases are calibrated against installed Microsoft WindowsDesktop WPF. Isolated before/after build logs, result JSON, warning baselines and source/binary hashes remain beside that evidence. These managed contract cases do not qualify the scientific workload's bitmap storage, timing or native display path; that original package workload must be rerun after a fresh producer includes the repair.

Final SDK 10.0.401 verification on 2026-10-18 passed all 66 portable cases on Windows and Linux and all 26 stock oracle cases, including the seven shared Application cases. Full portable builds returned zero errors and the same nine warnings found in the pre-guard full source build; `warning-baseline.json` records their IDs and locations. Final fixture-only builds returned zero warnings and zero errors.

## Restore-bounds evidence limits

The managed peer deliberately exposes state-before-geometry and geometry-before-state orders with its getter identifying the known state associated with each metrics callback. This proves the framework bookkeeping against that contract. A getter cannot infer future window-manager intent when a native manager publishes geometry before publishing its state property. Actual native window-manager qualification is separate. Mixed-monitor DPI transitions, initial automatically placed min/max windows without a normal geometry observation and other window managers remain unqualified until native evidence covers them.

The delayed-acknowledgement peer also publishes stale Normal geometry between a bounds request and acknowledgement, then acknowledges an older request after a newer one. Matching uses the requested client dimension in physical pixels: an integer request requires that pixel, while a fractional request admits its floor or ceiling pixel with floating calculation precision only. Physical position matches the already rounded point sent to the peer. Tests cover floor and ceiling acknowledgements with asymmetric DPI, partial-axis settlement, fractional min/max constraints and normal native user resize after settlement. Observed actual movement still raises LocationChanged while requested position properties remain pending. An unacknowledged request stays pending without automatic retries until a matching snapshot or a newer explicit request; denial policy is not inferred from stale geometry.

`StockOracle/StockOracle.csproj` uses the installed Microsoft WindowsDesktop WPF, with no fork project references. It records the actual assembly path, native displayed rectangles and RestoreBounds through max/min, both size directions and min/max constraints. Run it on Windows with `dotnet build StockOracle/StockOracle.csproj --artifacts-path <isolated-output>` then run its DLL. The evidence is `artifacts/portability/window-lifecycle/stock-restore-oracle.json`; native displayed bounds remain unchanged during restore edits and constraints apply upon Normal restoration. External native maximize and minimize survive Hide/Show with ShowActivated enabled. The existing WPF ShowActivated=false restriction rejects showing a maximized Window. The oracle also verifies that a stock Window with null Content does not raise Window.ContentRendered, while assigning content does.

Taskbar checks use the genuine public ShowInTaskbar property before source creation, during SourceInitialized, while live and through hide/edit/show. The managed peer records initial/native-Show state and both setter directions; source creation/closure counts, peer identity, actual Owner/OwnedWindows and activation state must remain unchanged. Canceled close retains the same mutable peer. The other preedit fixture peer implements the new internal setter as a no-op because its existing composition-close tests do not model taskbar presentation.
