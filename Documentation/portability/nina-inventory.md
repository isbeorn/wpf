# NINA compatibility evidence

This inventory is a static feasibility input for Linux x64 first, then Linux ARM64 and macOS. It does not certify runtime, plugin binary or hardware compatibility. It does not use the separate local NINA checkout as canonical evidence.

## Reproduce

From the WPF repository root, run:

```powershell
./eng/portability/Collect-NinaCompatibility.ps1
```

The collector performs HTTPS GET requests only. It fetches the pinned Git commit response, follows its root tree ID and requires a complete recursive tree response. It selects all project files and a bounded explicit set of representative UI/source files. The default run selected 59 files totaling 814,264 source bytes. No native binaries, package contents or whole-repository archive are downloaded.

Evidence is stored in ignored `artifacts/portability/nina/<revision>/`: `inventory.json`, `run-status.json`, API metadata and downloaded source. Every download has a SHA-256 sidecar. Each selected source file also must match its pinned Git blob SHA-1 and tree size. A corrupt cache, missing required file, truncated tree or failed request terminates the run explicitly. Check `run-status.json` before consuming an existing inventory because a failed rerun can leave the previous inventory present.

The optional `-EvidencePath` parameter replaces the default file selection, with a 100-file and 1 MiB per-file limit. Such a run has different coverage. `-FixtureDirectory` reads disposable `commit.json`, `tree.json` and `source/<path>` fixtures through the same validation/extraction path. Use a separate `-OutputDirectory`; fixture and public evidence cannot share a cache context.

## Verified revision

| Field | Observed value |
| --- | --- |
| Repository | `isbeorn/nina` |
| Commit | [`4742afae0a09c79be772f23fcf163fb5852d2775`](https://github.com/isbeorn/nina/commit/4742afae0a09c79be772f23fcf163fb5852d2775) |
| Root tree | `4b0826a22ab9a558fe340b8ba360d745240380a7` |
| Recursive tree truncated | `false` |
| Tree blob paths | 3,370 |
| XAML paths in complete tree | 196 |
| Commit response SHA-256 | `74e85c99dfcd77b7fbf3fabfe12f2aaf7c01e1baf29a89d46db397144800be35` |
| Tree response SHA-256 | `64a48803e90c9bad4b7a29addb2000463debe732ad894bddf648956f9e4703a6` |

These response hashes describe the collected API representation, while Git object IDs identify the revision and source blobs. The generated manifest records every selected path, raw URL, Git blob ID, byte count and SHA-256.

## Build and UI dependencies

At the pin, [NINA.csproj](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/NINA.csproj) targets `net10.0-windows`, uses WPF and has `RuntimeIdentifier=win-x64`. [NINA.Image.csproj](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Image/NINA.Image.csproj) enables both WPF and Windows Forms. The collector records project property declarations, not evaluated MSBuild values or a resolved transitive dependency graph.

Observed direct UI package references in NINA and NINA.WPF.Base:

| Package | Version |
| --- | --- |
| `Dirkster.AvalonDock` | `4.74.1` |
| `DotNetProjects.Extended.Wpf.Toolkit` | `5.0.129` |
| `Microsoft.Xaml.Behaviors.Wpf` | `1.1.142` |
| `OxyPlot.Wpf` | `2.2.0` |
| `OxyPlot.Contrib` | `2.1.2` |
| `OxyPlot.Contrib.Wpf` | `2.1.2` |
| `AvalonEdit` | `6.3.1.120` |
| `MdXaml` | `1.27.0` |
| `Microsoft.Web.WebView2` | `1.0.4078.44` |

Sources: [NINA.csproj](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/NINA.csproj) and [NINA.WPF.Base.csproj](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/NINA.WPF.Base.csproj). The complete direct-reference list, including non-UI packages and historical projects, is in the manifest. These package binaries and their transitive WPF/native calls have not been inspected or executed.

## Representative compatibility gates

| Gate | Real source/workflow | Required evidence before claiming support |
| --- | --- | --- |
| Startup, resources and dispatcher | [App.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/App.xaml), [SharedResourceDictionary](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/Utility/SharedResourceDictionary.cs) and [ProfileSelectVM](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/ViewModel/ProfileSelectVM.cs#L91) | Actual compiled XAML/resource loading, assembly component and pack URIs, dictionary ordering, bindings, templates, nested DispatcherFrame, timers, selection cancellation and shutdown. No BAML output was built or inspected in this pass. |
| Controls and theme changes | [StepperControl](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.CustomControlLibrary/StepperControl.cs), [Generic.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.CustomControlLibrary/Themes/Generic.xaml) and [Brushes.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/Resources/StaticResources/Brushes.xaml) | Dependency property metadata, template parts, default-style lookup, increment/decrement, min/max, command enablement and live profile-bound brushes. |
| Imaging workspace | [OverView.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/Imaging/OverView.xaml), [ImageView](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/View/ImageView.xaml) and [ImageUtility](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Image/ImageAnalysis/ImageUtility.cs) | Gray16/Rgb48 precision, stride/channel order, CopyPixels, Freeze across threads, zoom in/out, pan, rotate/flip, resize, float/redock and layout save/restore. Include histogram and autofocus plots, not just image presentation. |
| Framing Assistant | [FramingAssistantView](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/FramingAssistantView.xaml) and [MosaicSkySurvey](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/SkySurvey/MosaicSkySurvey.cs) | Deterministic saved survey imagery, transforms, overlays, DrawingVisual/RenderTargetBitmap composition, autocomplete and coordinate edits. Avoid a network service dependency in the acceptance fixture. |
| Sequencer editing | [AdvancedSequencerView](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/Sequencer/AdvancedSequencer/AdvancedSequencerView.xaml) and [SimpleDSOContainerView](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/Sequencer/SimpleSequencer/SimpleDSOContainerView.xaml) | Add/remove/reorder, keyboard focus and commands, valid/invalid edits, MultiBinding, validation feedback, CollectionViewSource, load/save and cancel using simulated devices. |
| Plugins and rich content | [PluginLoader](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Plugin/PluginLoader.cs#L825), [PluginsView](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/Plugins/PluginsView.xaml) and [Browser](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/View/Browser.xaml) | Pin an actual plugin and test exported ResourceDictionary/templates, dockable content and missing-template diagnostics. Test MdXaml/AvalonEdit rendering separately from WebView2 native browser integration. No plugin package was collected here. |

[OutlinedTextBlock](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.CustomControlLibrary/OutlinedTextBlock.cs#L186) also exercises DrawingContext, FormattedText, text geometry and custom measurement. Correct image rendering does not establish text compatibility.

## Native boundary and separate NINA scope

[MainWindow.OnSourceInitialized](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/MainWindow.xaml.cs#L49) unconditionally casts PresentationSource to HwndSource and adds a hook. The same file contains a WM_NCHITTEST workaround, direct `user32.dll` GetWindowPlacement/SetWindowPlacement calls and display-change handling. [WPFExtensions](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Core/Utility/Extensions/WPFExtensions.cs) and the [notification work-area provider](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Core/Utility/Notification/SameScreenAsApplicationWorkAreaProvider.cs) use Win32 monitor APIs and window handles. [App.xaml.cs](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/App.xaml.cs) imports kernel32 console functions and invokes Windows JumpList integration. These application calls require an explicit compatibility decision even if framework rendering works.

The observed startup uses one WPF dispatcher with nested frames. [DeviceChooserVM](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/ViewModel/Equipment/DeviceChooserVM.cs#L203) starts a separate STA thread for driver setup dialogs. This is evidence for driver-owned UI, not proof of multiple startup WPF dispatchers.

Keep separate from WPF implementation: `ASCOM.Com.Components` 3.1.0, vendor device DLLs and SDK processes, System.Drawing/Windows Forms usage in the imaging graph, System.Management/WMI in [Logger](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Core/Utility/Logger.cs), NTFS plugin handling and the WebView2 browser engine. Direct references show their presence; this pass did not certify reachability or platform behavior for all paths. Linux ARM64 additionally needs architecture-specific native dependencies and validation; a Linux x64 result cannot establish it.

### Subsequent application imaging audit: 20 October 2026

The [actual integration-1 startup](../../artifacts/portability/current/nina/shell-integration-1/first-run-handoff.json)
reaches original MainWindow.Show before asynchronous binding initializes
NINA.Image's StarAnnotator. Its static System.Drawing brush resources enter GDI+
and fail to load gdiplus.dll. This is an observed application dependency, separate
from WPF's prepared-frame display support. The user deferred NINA.Image adaptation
until the remaining WPF gates are finished.

Framing has an additional application drawing dependency. Source inspection of
the pinned FramingAssistantVM.LoadImage and SkyMapAnnotator.Initialize shows that
the real workflow creates SkyMapRasterRenderer, including offline SKYATLAS mode.
The renderer declares static System.Drawing brush, pen, hatch and font resources
and uses Bitmap storage. It belongs to NINA.WPF.Base, not the WPF framework.
Its retained source SHA256 is
45274D62225185D8B2FFAC964ACEAC1D4BDAA53820D7BC1E81FBBBD59B52527C;
SkyMapAnnotator is
8A00B3B3B20F9DA9F382D5808DDA556E1FEA703AD8D200B4E68AEE05FF7806A8.
This identifies another required application boundary; no Linux framing run or
replacement renderer is qualified by that inspection. FILE mode additionally
enters the deferred NINA.Image preparation path.

The historical 18-check stock framing result remains a historical pointer. Its
preparation-8 test sources, assertions and raw survey fixture were absent during
this audit. Retained catalog/native astronomy inputs and an empty survey-cache
metadata file cannot reconstruct that evidence or establish a Linux replay.

No shader `.ps`, `.fx`, `.hlsl` or `Shader`-named path occurs in the complete pinned tree. No ShaderEffect, PixelShader or D3DImage match occurs in the selected source content. This does not exclude shaders or native rendering inside third-party controls or unselected source.

## Verification performed

- PowerShell native parser: no errors after the final script edit.
- Public collector: completed and repeated from validated cache, 59 source blobs verified against the pinned tree.
- Disposable fixture: package/property extraction and Git blob hashing passed; corrupt cached content, missing evidence, truncated tree and incorrect downloaded blob content were rejected.
- `git check-ignore` confirms the generated inventory is ignored by the existing `artifacts/` rule. Downloaded MPL source remains in that ignored directory.
- No NINA/WPF build, runtime view construction, package binary inspection or native device execution was performed. The feasibility checkpoint must retain these gaps.
