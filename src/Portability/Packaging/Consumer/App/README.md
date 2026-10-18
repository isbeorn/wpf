# Complete unchanged WPF view

The default consumer scenario remains the Phase 1 package gate. `--complete-view --verify` runs the integrated workflow against the selected `FixtureControls` binary. `--complete-view` runs the checks and leaves that same Window open for interactive editing and owned accept/cancel dialogs.

The view, CLR source and component dictionary come unchanged from `Probes/WindowsReference/FixtureControls`. The host supplies explicit UserControl, Button and TextBox templates in `WorkflowResources.xaml`, including a derived FixtureView style, PART_ContentHost and the real ScrollContentPresenter. Its original MetricControl template and dynamic brush remain in the original component dictionary. This is an explicit host resource profile rather than stock theme qualification.

`Prepare-CompleteView.ps1` copies two independent external consumers. The Microsoft origin uses the existing frozen DLL and trusted hash. The Port origin compiles the exact copied source with the packaged SDK and retains the same assembly identity. Each publication pins its actual fixture bytes. Both use ordinary references and the Default load context in separate processes. Source hashes are checked before and after compilation/execution. Package hashes must match the supplied approved values.

From the repository root:

```powershell
./eng/portability/Prepare-CompleteView.ps1 `
  -DistributionDirectory artifacts/portability/distribution-final-2 `
  -DestinationDirectory artifacts/portability/complete-view-new
```

For a refreshed approved feed, pass its exact `-ExpectedRuntimeSha256` and `-ExpectedSdkSha256`. Preparation rejects an existing destination. The frozen compiler oracle must already exist.

Run on Linux with SDK 10.0.401, the existing native X11/Skia prerequisites, Xvfb and XTest:

```sh
bash eng/portability/Test-CompleteView.sh \
  /absolute/path/to/wpf/artifacts/portability/complete-view-new \
  /absolute/path/to/dotnet
```

The script builds on fresh case-sensitive native Linux storage with independent package caches. It publishes and runs framework-dependent and self-contained outputs for both origins, mirrors evidence and publications back to the prepared directory and preserves a failed process exit. It also reruns the original Phase 1 native consumer scenario. Standard output and stderr are retained separately; named check failures and watchdogs return nonzero.

The workflow checks native valid/invalid/recovery edits, downward source updates, command disable/enable, synchronous owned accept/cancel dialogs, bound RGB48 padded rows and lock updates, image replacement, dynamic resource replace/remove, opacity forward/reverse/removal, paired resize, both uniform 96/144-DPI directions and fresh lifetimes including close with an active clock. Each settled resize/scale requires fresh routed editor mouse input and a bound command with native modal acceptance, with exactly one execution and restored owner state. Actual native client dimensions are compared with the arranged client root and current DPI; a retained keyboard focus cannot mask a dropped mouse click. Closed must occur exactly once.

Native X11 startup scaling is set and pumped before each fresh Window is constructed, then the first mapped DPI is checked. Click coordinates are recalculated from the actual visual tree and current root DPI. Native input uses the copied existing X11Fixture, with declared XSETTINGS test preferences. Both modes verify every runtime-manifest file hash and reject compile-only contracts in runtime output. The completion guard and dependency-free `--audit-complete-view-results <file>` mode require the exact probe, 19 unique required checks and all passing statuses.

`CompleteViewWorkflow.cs` and the resource profile are also linked unchanged into the stock Windows reference wrapper. That adapter uses actual SendInput and its declared owned-window DPI message scope. Virtual X11 results do not qualify physical monitor transitions, IME, native Wayland or full WPF themes. The small correctness image does not qualify the separate large scientific-image performance workload.
