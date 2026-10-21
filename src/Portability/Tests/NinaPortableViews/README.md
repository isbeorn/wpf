# Unchanged NINA view diagnostic

This host compares the same Microsoft-built NINA `DirectGuiderDetailView` on the
portable runtime and stock WindowsDesktop. It does not rebuild NINA source or
markup. Portable execution uses the immutable `distribution-final-2` package feed
as a diagnostic baseline. Phase3 qualification requires refreshed packages after
Phase2 repairs and completion of the remaining view and workflow gates.

Run from the repository root:

```powershell
./eng/portability/Run-NinaPortableViews.ps1 -HostProfile PrepareOnly
./eng/portability/Run-NinaPortableViews.ps1 -HostProfile StockWindows
./eng/portability/Run-NinaPortableViews.ps1 -HostProfile Linux
```

`Windows` selects the portable package host on Windows. `StockWindows` is a
separate comparison host with an explicit WindowsDesktop framework reference.
Neither host compiles XAML. Outputs, caches and redirected NINA state are under
`artifacts/portability/nina-portable-views`.

## Provenance and scope

`BaselinePin.json` fixes revision `4742afae0a09c79be772f23fcf163fb5852d2775`,
the original NINA assembly hash, the complete prepared closure hash and the
immutable runtime package hash. Preparation verifies all 3370 pinned source blobs
against the original Git tree. It measures assembly references from the existing
successful stock build without executing its test bootstrap. The closure contains
82 managed assemblies and 78 culture satellites with 985 assembly-reference edges.
Every copied identity and byte hash is checked again before NINA initialization.
No stock framework DLLs or native binaries are copied into the portable host.

Six unresolved transitive references are recorded rather than substituted:
`System.CodeDom`, `Microsoft.Windows.SDK.NET`, `WinRT.Runtime`, `ReachFramework`,
`System.Printing` and `System.Windows.Forms`. The selected construction path does
not request them. This measured closure is not a claim that every NINA path runs.

The host uses the original six dictionaries from `DirectGuiderDetailViewTest`:
ProfileService, Brushes, TextBlock, TextBox, CheckBox and Tooltip. It verifies the
real profile resource type, the unsigned NINA `3.3.0.1068` identity and the original
`InitializeComponent` compiler attribute (`PresentationBuildTasks 10.0.12.0`).
Whole-assembly byte pins preserve the original BAML payload and companion resources.
All assemblies load through ordinary references in the Default ALC.

Harness adaptations are limited to a separate net10 executable/runtimeconfig,
public artifact-state field redirection before initialization and headless
Measure/Arrange at 800x450. The original App startup, hardware bootstrap, profile
loading, native display and equipment operations are not invoked. There are no
replacement templates, rewritten controls or custom assembly loaders. The original
minimum-dither binding retains its real `DoubleRangeRule`; validation interaction
and focus commits are not exercised here.

## Frozen diagnostic result

Both final builds had zero warnings and zero errors. The stock comparison passed
all six checks with process exit 0. Linux passed the first five checks then failed
the real content-layout check with process exit 1:

| Observation | Stock Windows | Portable Linux final-2 |
| --- | --- | --- |
| View visibility | Visible | Visible |
| View desired size | 800x120 | 0x0 |
| Arranged view size | 800x450 | 800x450 |
| ControlTemplate present | Yes | No |
| View visual children | 1 | 0 |
| Original Grid measured | Yes | No |
| Original Grid actual size | 800x450 | 0x0 |
| Grid visual parent | ContentPresenter | None |
| Grid column widths | 200, 600, 0 | 0, 0, 0 |

The source Grid has columns `200`, `*` and `Auto` with one implicit row. Its content
is Visible in both hosts. Layout observations snapshot validity before inspecting
the empty RowDefinitions collection because creating that collection invalidates
measure on stock WPF.

Linux verifies unchanged binary compatibility and original BAML construction,
but visual layout fails at the absent ordinary UserControl template. Stock results
isolate this as the default-theme/template resource boundary. They do not establish
which product theme implementation should be selected. No production fix is made
by this diagnostic. Both hosts recorded zero native-load failures and Linux recorded
no Windows graphics module mappings.

The full evidence is in `runtime-linux.json`, `runtime-stock-windows.json`,
the matching `process-*.json` files and `fixture/Closure.json` under the output
directory. `host-source.json` freezes source and evidence hashes for handoff.

App startup's full 34-dictionary closure, real profile interactions, display,
validation commits and further views remain outside this bounded result.

## Portable entry diagnostics

The existing runner's `--portable-entry-diagnostics` mode compiles the exact pinned NINA `PortableEntry.cs` and invokes its private configuration method through test-only reflection. It does not call NINA `App.Main`. Use `--expect-enabled` only in a fresh Linux process started with `WPF_NINA_PORTABLE_DIAGNOSTICS=1`; otherwise start a separate process with the switch absent. The fixture exercises a real missing-property binding with source/target/property text, valid binding recovery, repeated setup without duplicate output and a genuinely loaded Window's missing-resource key plus add/remove/restore and normal close. It captures the ordinary stderr listener's actual text in its JSON observations.

The native portion requires the existing owned private Xvfb/settings runner. It must not run on a live desktop. Use released references with `Private=false` for a consumer-only build and borrow that runtime's deps/config and its existing Stage additional-deps; do not copy or rebuild a framework graph for this mode. The diagnostic entry link/dispatch is excluded from the stock Windows reference build, which does not reference NativeHost. A previous entry without configuration remains usable as a causal baseline: the test observes a genuine binding `PathError` and fails because diagnostic text is missing. Final installed NINA startup with the same switch remains a separate publication gate.
