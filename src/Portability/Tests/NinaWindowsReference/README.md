# Pinned NINA Windows view reference

Run from the repository with PowerShell 7:

```powershell
./eng/portability/Collect-NinaCompatibility.ps1
./eng/portability/Build-NinaWindowsReference.ps1
```

This runner exercises unchanged upstream NUnit view tests from NINA commit
`4742afae0a09c79be772f23fcf163fb5852d2775`, using Microsoft's SDK 10.0.401 and
stock WindowsDesktop 10.0.12. It is a Windows view reference, not a test of this
fork, full NINA packaging, hardware support or Linux/macOS compatibility.

The runner downloads a read-only GitHub source snapshot into ignored artifacts,
validates its paths and verifies every source blob against the cached pinned
commit/tree metadata before and after building. No upstream source is vendored
here or edited. MPL notices remain in the cached source files. The static project
closure is assessed before package restore. SDK, NuGet, compiler scratch, NINA
logs and test results use workspace artifact paths. Existing public
`CoreUtil.APPLICATIONTEMPPATH` and `ProfileService.PROFILEFOLDER` fields redirect
NINA state before its original test bootstrap executes.

The exact upstream fixtures run in separate testhost processes:

- `GuiderChartViewTest`: eight cases constructing four real views, checking OxyPlot
  chart axis/series relationships and checking both star metric toggle bindings.
- `DirectGuiderDetailViewTest`: one case constructing the real view and inspecting
  its minimum-dither TextBox binding and production validation rule.

Both retain upstream STA and nonparallel attributes. Test execution has a 30
second hang watchdog and 60 second session limit. The runner rejects zero,
skipped, failed or unexpected test counts. It records and checks loaded stock
framework and pinned NINA assembly identities and paths. It adds only assembly
evidence and state-path instrumentation to the original test project. NUnit,
FluentAssertions and all original package versions come from the pinned project.

Two build constraints are explicit:

1. NINA's `InternalsVisibleTo("NINA.Test")` declarations are conditional on
   `DEBUG`. The upstream test project therefore needs a Debug build. An attempted
   Release test build produced CS0122 errors; production access modifiers were
   preserved.
2. The source snapshot excludes its `NINA/External` native submodule. The runner
   first attempts the normal Debug build and records its actual copy failures.
   For the view-only build, the imported target omits only nonexistent copy items
   under that directory and records each full path and reason. It does not omit
   managed source, XAML, existing native files or other missing content. The
   original global test bootstrap still runs and logs its missing SOFA/NOVAS
   preload errors. These particular view tests do not call those native routines.
   No replacement DLLs are supplied.

All output lives under `artifacts/portability/nina-windows-reference/`:

- `results.json`: view-reference checks and the separate normal application-build
  outcome. Exit zero means the selected view-reference checks passed.
- `provenance.json` and `project-closure.json`: source and dependency provenance.
- `build-original.log`, `build-view-debug.log` and `native-omissions.txt`: actual
  build outcomes, unchanged upstream warnings and exact omitted native payload.
- `guider-assemblies.json` and `direct-assemblies.json`: observed loaded binaries.
- `test-results/guider/guider.trx` and `test-results/direct/direct.trx`: all nine
  individual upstream test results.
- `state/`: isolated NINA logging and profile paths.

The tests construct and lay out real compiled views and resources. They do not
load a user profile or connect equipment. They inspect binding definitions and
validation rules without establishing every live binding, rendered chart,
interactive workflow, native input, DPI transition or overnight lifetime. The
full application remains blocked by missing native payload and is not reported
as passing when these view tests pass.
