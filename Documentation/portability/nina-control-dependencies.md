# Pinned NINA control dependency closure

This is Phase 0 evidence for the accepted [plan](../cross-platform-plan.md), using NINA commit `4742afae0a09c79be772f23fcf163fb5852d2775`. The nine root package pins below were checked against the hash-verified NINA, NINA.WPF.Base and NINA.Core project files from the [pinned inventory](nina-inventory.md).

The important result is a concrete control-library portability boundary. AvalonDock's floating-window implementation owns an HwndHost/HwndSource path and direct Win32 calls. AvalonEdit owns Win32 caret and IME paths. These are required UI-library investigations, separate from NINA's camera SDK, ASCOM, browser engine and other application dependencies. Matching the WPF assembly identity alone does not port those control paths. No selected control binary was executed, so the findings below are static adaptation requirements and pending qualification results, not observed Linux runtime failures.

## Reproduce and inspect

```powershell
./eng/portability/Collect-NinaCompatibility.ps1
./eng/portability/Inspect-NinaControlDependencies.ps1
# Recheck existing archives, extracted DLLs, metadata, IL and startup sources:
./eng/portability/Inspect-NinaControlDependencies.ps1 -SkipRestore
```

The inspector requires PowerShell 7 and installed .NET SDK 10.0.401. It writes only ignored `artifacts/portability/nina-control-dependencies/`. The isolated restore project targets NINA's `net10.0-windows` and `win-x64`, fixes each root version exactly and locks the resolved graph. It selects official nuget.org, already configured in the user's NuGet configuration; no repository feed configuration changes. The repository's configured dotnet-public mirror returned HTTP 404 for MdXaml 1.27.0. Private and local feeds are excluded.

`inventory.json` records archive URLs, SHA-256 and SHA-512, NuGet content hashes, licenses, all nuspec dependency groups, selected compile/runtime assets, assembly identities/references, P/Invoke declarations, COM-import types, embedded resource keys and filtered IL call sites with caller, target and IL offset. `restore/obj/project.assets.json`, `restore/packages.lock.json` and `restore.log` preserve NuGet's actual selection. Signed-package NuGet content hashes are checked with the SDK's PackageArchiveReader; they need not equal the raw ZIP SHA-512. Every extracted inspected DLL is checked against its bytes in the validated archive.

The run resolved 13 packages and 11 DLLs from 6,867,195 archive bytes. Compile and runtime DLL selections are identical in this bounded graph. No native runtime asset is selected. Imported operating-system DLLs are still required on the relevant paths. Package assemblies were read with PEReader/MetadataReader, not loaded for execution. ResourceReader enumerated embedded resource keys without constructing resource values. IL decoding examined 17,304 method bodies; the retained call list is filtered for relevant desktop/native operations, not a whole-program reachability proof.

## Actual target assets and immediate closure

| Package/version | Selected DLL and asset directory | Nuspec license | Selected package dependencies |
| --- | --- | --- | --- |
| Dirkster.AvalonDock 4.74.1 | `AvalonDock.dll`, `lib/net10.0-windows7.0` | Archive LICENSE contains MS-PL | None |
| DotNetProjects.Extended.Wpf.Toolkit 5.0.129 | `DotNetProjects.Wpf.Extended.Toolkit.dll`, `lib/net8.0-windows7.0` | MS-PL | None |
| Microsoft.Xaml.Behaviors.Wpf 1.1.142 | `Microsoft.Xaml.Behaviors.dll`, `lib/net8.0-windows7.0` | MIT | None |
| AvalonEdit 6.3.1.120 | `ICSharpCode.AvalonEdit.dll`, `lib/net8.0-windows7.0` | MIT | None |
| MdXaml 1.27.0 | `MdXaml.dll`, `lib/net6.0-windows7.0` | MIT | MdXaml.Plugins 1.27.0; AvalonEdit minimum 6.3.0.90, resolved to NINA's 6.3.1.120 |
| MdXaml.Plugins 1.27.0 | `MdXaml.Plugins.dll`, `lib/net6.0-windows7.0` | MIT | None |
| OxyPlot.Wpf 2.2.0 | `OxyPlot.Wpf.dll`, `lib/net8.0-windows7.0` | MIT | OxyPlot.Core 2.2.0 and OxyPlot.Wpf.Shared 2.2.0 |
| OxyPlot.Wpf.Shared 2.2.0 | `OxyPlot.Wpf.Shared.dll`, `lib/net8.0-windows7.0` | MIT | OxyPlot.Core 2.2.0 |
| OxyPlot.Core 2.2.0 | `OxyPlot.dll`, `lib/net8.0` | MIT | None |
| OxyPlot.Contrib.Wpf 2.1.2 | `OxyPlot.Contrib.Wpf.dll`, `lib/netcoreapp3.1` | MIT | OxyPlot.Wpf minimum 2.1.2, resolved to 2.2.0 |
| OxyPlot.Contrib 2.1.2 | `OxyPlot.Contrib.dll`, `lib/netstandard1.0` | Legacy URL to OxyPlot LICENSE | NETStandard.Library 1.6.1 |
| NETStandard.Library 1.6.1 | No selected DLL | Legacy Microsoft license URL | Microsoft.NETCore.Platforms 1.1.0 |
| Microsoft.NETCore.Platforms 1.1.0 | No selected DLL | Legacy Microsoft license URL | None |

This records package declarations and the archived license file, not a redistribution approval. Legacy license URLs and missing repository commits remain provenance work. MdXaml's nuspec has no repository commit; neither OxyPlot.Contrib package declares a source commit. Source rebuilding these versions must establish the precise source revision rather than assume current repository heads.

Archive SHA-256 values:

| Package/version | SHA-256 |
| --- | --- |
| AvalonEdit/6.3.1.120 | `8ac3e5aa2bb6178d92cbe72c7900a683fab0aee66a8eafa481611c444dbee3e3` |
| Dirkster.AvalonDock/4.74.1 | `98ed2a6db9c4c2dd7ee2e96cb0af07e6a602b37bf515327a3ea82ae0d92d74a0` |
| DotNetProjects.Extended.Wpf.Toolkit/5.0.129 | `c9d80e88d6e4178485cf00835cd58f97368581b08fdbff8eeb1d416f202a4097` |
| MdXaml/1.27.0 | `a3927ae3cd24bdcea19ab9ee7ea63ff205044c7dd8a4e2f007e6a44fe61d436e` |
| MdXaml.Plugins/1.27.0 | `962a4626f57d97a063e1dc9e61b2b42b0dcdabeb121adb3285015495cb7b9658` |
| Microsoft.NETCore.Platforms/1.1.0 | `15e338d24b5c39b4099389cc612841eb51ff13c07bb4829f97d39b27420e7023` |
| Microsoft.Xaml.Behaviors.Wpf/1.1.142 | `890f26d79a3b30a25b652449f8f81254fea758a1699b66d3715916c0ec225332` |
| NETStandard.Library/1.6.1 | `88d6a7d62c7b46d9dc1960bd023019dac93bd03a313ac9843a0435d1f5e6e0fc` |
| OxyPlot.Contrib/2.1.2 | `4b1460f2863860c737b328e809c818e30850ced955dd295950a2f024012f5faf` |
| OxyPlot.Contrib.Wpf/2.1.2 | `a77028fab97dffc3a56f3350c28d53b8d3a81a24a5dcdc94361812770064a4c5` |
| OxyPlot.Core/2.2.0 | `fe9462955c41ebbdda575aba59344d3c7740a4a8145609c2e3ccf2353c1808b0` |
| OxyPlot.Wpf/2.2.0 | `c7e9c54301154aabe5b188e5e1886dc471c923923fa0dec5ddb1c5385ae68a6b` |
| OxyPlot.Wpf.Shared/2.2.0 | `a398a8a0dbca3baf579b48ee07b3e9e9521ea69643284ca9d54b30b432563419` |

## Assembly identity and resources

The portable product's [version-10 contract](runtime-baseline.md) and original public tokens are necessary inputs. The selected package references vary:

| Selected assemblies | Referenced WindowsBase/PresentationCore/PresentationFramework version | System.Xaml version | Embedded BAML keys |
| --- | --- | --- | --- |
| AvalonDock | 10.0.0.0 | 10.0.0.0 | 1 |
| Toolkit and AvalonEdit | 8.0.0.0 | 8.0.0.0 | 84 and 6 |
| Microsoft.Xaml.Behaviors | 8.0.0.0 | 8.0.0.0 | 0 |
| MdXaml | 6.0.2.0 | 6.0.2.0 | 1 |
| MdXaml.Plugins | PresentationCore/PresentationFramework 6.0.2.0; no WindowsBase reference | 6.0.2.0 | 0 |
| OxyPlot.Wpf and OxyPlot.Wpf.Shared | 8.0.0.0 | 8.0.0.0 | 0 and 1 |
| OxyPlot.Contrib.Wpf | 4.0.0.0 | 4.0.0.0 | 1 |
| OxyPlot and OxyPlot.Contrib | None | None | 0 |

All observed WindowsBase/PresentationCore/PresentationFramework references have public key token `31bf3856ad364e35`; System.Xaml references have `b77a5c561934e089`. The manifest also records each control assembly's own version/token and every AssemblyRef. OxyPlot.Contrib.Wpf references OxyPlot, OxyPlot.Wpf and OxyPlot.Wpf.Shared **2.1.2.0**, while the selected assemblies are **2.2.0.0**. MdXaml references AvalonEdit **6.3.0.90**, while NINA selects **6.3.1.120**. These existing graph differences require actual loading/member/BAML tests; static inspection establishes neither successful unification nor failure.

The 94 BAML keys include AvalonDock `themes/generic.baml`, AvalonEdit `texteditor.baml`, completion/search controls and `themes/generic.baml`, Toolkit color picker/spinner/split button templates and theme dictionaries, MdXaml `markdown.style.baml` and both OxyPlot shared/contrib `themes/generic.baml`. This confirms genuine binary resource payloads are present. Their records were not parsed or executed by a BAML loader.

Additional desktop assembly closure is explicit: AvalonEdit references UIAutomationProvider and UIAutomationTypes 8.0.0.0, Toolkit references UIAutomationProvider 8.0.0.0 and OxyPlot.Wpf references ReachFramework 8.0.0.0, all with token `31bf3856ad364e35`. Observed types include IValueProvider/ITextProvider, IRawElementProviderSimple/ITextRangeProvider and XpsDocument. These are framework/distribution investigations, not missing NuGet package DLLs.

## NINA startup resource closure

Following the pinned [App.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/App.xaml) finds 34 direct ResourceDictionary.Source edges and 35 files including App. Every source byte was verified against the complete pinned NINA tree; files and hashes are in `inventory.json` under `StartupResources`. All dictionary targets are within NINA/NINA.WPF.Base. The scan follows dictionary source edges only; it does not instantiate DataTemplates or treat every type mentioned in a template as executed at startup.

Important startup type/resource dependencies are:

- [ColorPicker.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/Resources/Styles/ColorPicker.xaml#L13): implicit Xceed ColorPicker style. [SplitButton.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.WPF.Base/Resources/Styles/SplitButton.xaml#L14): Xceed ButtonChrome and SplitButton styles.
- [AvalonDock.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/Resources/Styles/AvalonDock.xaml): DockingManager, layout control and floating-window styles. The [imaging overview](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/View/Imaging/OverView.xaml) uses that docking library.
- [Oxyplot.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/Resources/Styles/Oxyplot.xaml#L14): TrackerControl style. Histogram and autofocus views use OxyPlot.Contrib.Wpf Plot and series.
- [Markdown.xaml](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA/Resources/Styles/Markdown.xaml#L31): FlowDocument styles include an AvalonEdit TextEditor style. Plugin details instantiate MdXaml MarkdownScrollViewer later.

Thus excluding a later plugin page does not remove its style dictionary from App's resource-loading test. It also does not establish that the dictionary eagerly constructs every later control.

## Required-feature ledger

Every row applies to Linux x64 first. ARM64 and macOS require separate native/backend qualification. The original source-compatibility promise remains in force. Adapted/rebuilt controls must be reported separately from unchanged binaries and original BAML.

| Priority and owner | Calling feature and exact evidence | Current status/blocking dependency | Required future test |
| --- | --- | --- | --- |
| P0, markup/identity owner | NINA App startup dictionaries above; package BAML keys and mixed version references | Static closure recorded. Unchanged binary/BAML loading unqualified. | Construct actual App resource set against one version-10 portable family; verify pack URIs, resource lookup, implicit styles and templates. Record exact loaded paths/tokens and failures. |
| P0, docking/control adaptation owner | AvalonDock `LayoutFloatingWindowControl.CoerceContentValue` creates `FloatingWindowContentHost : HwndHost`; its `BuildWindowCore` constructs HwndSource. `OnLoaded` calls HwndSource.AddHook. `DragService.BringWindowToTop2` calls Win32Helper.SetWindowPos. | Observed feature-path dependency requiring a control/platform adaptation decision. No general HWND emulation is promised. Unchanged floating-window binary support remains unqualified. | Open imaging, float/redock, drag, resize, activate/focus, restore persisted layout and close/reopen. Test with native input and actual owned windows. |
| P1, editor/input adaptation owner | AvalonEdit `Caret.ShowInternal` calls Win32.CreateCaret/SetCaretPosition; `CaretLayer.StartBlinkAnimation` calls Win32.get_CaretBlinkTime, whose IL directly calls user32 GetCaretBlinkTime. `ImeSupport.CreateContext` casts to HwndSource and calls ImmGetContext/ImmAssociateContext/AddHook. | Control-library dependency, not a camera/device issue. CreateCaret/SetCaretPosition guard a failed as-HwndSource conversion, while IME creation is conditional on EnableImeSupport/focus and uses an explicit cast. The complete feature paths need adaptation/qualification. | Render a real Markdown code block/editor, focus it, move/show/hide caret, select/copy, toggle read-only and exercise IME composition on the native host. Keep read-only rendering and editable input results separate. |
| P1, Toolkit/control owner | ColorPicker/SplitButton startup styles; 84 Toolkit BAML keys. `WindowColors.GetDWMIntValue` calls Microsoft.Win32.RegistryKey.CreateSubKey/GetValue. MessageBox.CreateContainer uses WindowInteropHelper.Owner. | No P/Invoke declarations in this DLL. Specific Windows API paths still exist; the selected NINA style reachability of WindowColors was not established. | Instantiate actual color picker/split button templates, open/close popups, edit values, switch theme and exercise keyboard/focus. Probe registry-dependent palette and owned message-box paths separately. |
| P1, plots/compatibility owner | Contrib Plot templates and 2.1.2-to-2.2.0 references; `PlotViewBase.BeginInvoke` uses Dispatcher.CheckAccess/BeginInvoke. `SetClipboardText` and PlotView.DoCopy use Clipboard. | No direct native imports observed in selected plot DLLs. Framework and control binary behavior remain unqualified. | Construct histogram and autofocus charts, mutate series, pan/zoom, show tracker and copy image/text through portable clipboard. |
| P1, document/resource owner | MdXaml MarkdownStyle.LoadDictionary calls Application.LoadComponent; image loaders marshal through Dispatcher.Invoke/InvokeAsync. It adds MdXaml.Plugins and AvalonEdit. | Source provenance for exact MdXaml release not pinned by nuspec. Rich document and code-block binary behavior unqualified. | Instantiate plugin description Markdown with code, hyperlink and deterministic image; resolve markdown.style BAML and verify async image lifetime/cancellation. |
| P1, desktop-contract/distribution owner | AvalonEdit automation interfaces; Toolkit IValueProvider; OxyPlot.Wpf.XpsExporter.Export calls ReachFramework.XpsDocument constructor/CreateXpsDocumentWriter | Additional framework assembly closure identified; no portable implementation or runtime claim from this scan. | Construct actual automation peers against supplied contract assemblies. Qualify XPS export as its own feature or give an explicit unsupported diagnostic. |
| P2, behavior/effects owner | NINA uses EventTrigger/InvokeCommandAction; binary TimerTrigger uses DispatcherTimer. Binary TransitionEffect constructors/static initializer call ShaderEffect and sampler registration. | No direct native imports. Shader-effect types exist transitively although NINA's source tree has no shader files; selected NINA reachability of TransitionEffect was not established. | Attach actual event/command behaviors and timers first. Qualify transition effects only when selected by the compatibility profile. |

For scale only, AvalonDock declares 125 P/Invokes across user32, gdi32, gdiplus, kernel32, ole32, shell32, dwmapi and uxtheme plus 19 COM-import types. AvalonEdit declares 13 imports across user32, imm32 and msctf plus ITfThreadMgr. Many AvalonDock imports belong to bundled shell helpers. These counts are not executed reachability, release-failure counts or a claim that NINA uses every helper.

## Primary source corroboration

NuGet declares AvalonDock source commit `028a5f328eb86a7abfb2195bbaae37683c818d1e`, AvalonEdit `862415d51eddc9eac93f462dbc522ffbf929cd52` and Toolkit `e9fb4e2a7d8eb7140c27a0de2ae15db16bec14ec`. Relevant files were retrieved from those commits and checked against Git blob IDs; `primary-source-evidence.json` records their hashes separately. This corroborates the IL and source attribution, not reproducible-build equivalence.

- [AvalonDock floating-window host](https://github.com/Dirkster99/AvalonDock/blob/028a5f328eb86a7abfb2195bbaae37683c818d1e/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs#L575): content coercion, OnLoaded HwndSource hook and nested HwndHost implementation. [WindowChromeWorker](https://github.com/Dirkster99/AvalonDock/blob/028a5f328eb86a7abfb2195bbaae37683c818d1e/source/Components/AvalonDock/Controls/Shell/WindowChromeWorker.cs#L202) also adds hooks and calls native SetWindowPos.
- [AvalonEdit Win32 caret wrappers](https://github.com/icsharpcode/AvalonEdit/blob/862415d51eddc9eac93f462dbc522ffbf929cd52/ICSharpCode.AvalonEdit/Utils/Win32.cs#L35), [Caret.ShowInternal](https://github.com/icsharpcode/AvalonEdit/blob/862415d51eddc9eac93f462dbc522ffbf929cd52/ICSharpCode.AvalonEdit/Editing/Caret.cs#L490) and [ImeSupport.CreateContext](https://github.com/icsharpcode/AvalonEdit/blob/862415d51eddc9eac93f462dbc522ffbf929cd52/ICSharpCode.AvalonEdit/Editing/ImeSupport.cs#L102) show the relevant guards and Windows calls.
- [Toolkit WindowColors](https://github.com/dotnetprojects/WpfExtendedToolkit/blob/e9fb4e2a7d8eb7140c27a0de2ae15db16bec14ec/Src/Xceed.Wpf.Toolkit/Core/Media/WindowColors.cs#L91) reads Windows DWM registry values. This path must not be labeled portable merely because the library has no DllImport rows.

## Verification and limits

Native PowerShell parsing, locked NuGet restore, archive SHA-512/NuGet content-hash checks, selected DLL-to-archive comparisons and startup source Git-blob checks passed. A disposable mutation of the extracted AvalonEdit DLL was rejected before metadata inspection, then the original bytes were restored and the collector passed again. Targeted sentinel assertions verify known native imports, IL callers, BAML keys, asset selection and the 35-file startup closure. The generated directory is ignored by the existing artifacts rule.

No WPF production source changed. No control source rebuild, control execution, actual BAML load, native UI interaction, license approval or redistribution packaging was performed. The source-built portable family compiling with original version-10 identity does not close these qualification gaps. Browser/WebView2, ASCOM and other NINA dependencies are excluded from this bounded control graph and remain in their separate application workstream.
