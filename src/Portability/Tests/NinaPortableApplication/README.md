# Original NINA application source build

`eng/portability/Build-NinaPortableApplication.ps1` builds the pinned NINA application from verified external sources. It does not launch NINA. This is the source-build, paired-console-guard and app-local native-bootstrap portion of Phase 4, not a complete portable application or hardware support claim.

`upstream.json` records NINA revision 4742afae0a09c79be772f23fcf163fb5852d2775 and the SHA-256 and Git blob identities of its 680 selected files: the complete NINA application directory, CommonAssemblyInfo.cs and Directory.Build.props. Sources and the original managed closure are supplied externally rather than vendored. The original source directory is never modified.

The helper creates a fresh owned output, copies only those pinned source files and derives only App.xaml.cs. Both AttachConsole and FreeConsole calls gain OperatingSystem.IsWindows guards. Parsing remains between the calls and all other branches are unchanged. Exact source and derived hashes guard the transformation. App.xaml.cs.patch is the reviewable diff and remains byte-stable through .gitattributes. Original copyright and MPL headers remain in the derived source. The full MPL license and source provenance manifest accompany the build under ThirdPartyNotices/NINA.

The generated project retains original application resources and content while replacing the project/package build graph with explicit verified inputs. It removes the Windows packaging PostBuild command and absent External native copy items, recording each omission. It does not silently replace native behavior.

PortableEntry.cs is one additional internal source file in the NINA assembly. Its STAThread Main is selected through StartupObject. On Linux it initializes the existing NativeWindowHost before calling the untouched generated NINA.App.Main. This preserves EntryAssembly=NINA, process arguments and the generated splash/App/InitializeComponent/Run order. Native-only types remain in one private NoInlining initializer so the outer entry can be prepared on stock Windows without native-host assemblies. The existing Application/Dispatcher owns its normal event loop and pump shutdown. There is no second loop, host lease, module initializer or external startup hook.

The bootstrap hash is recorded separately in upstream.json; all 680 upstream source pins and the existing paired-console transformation remain unchanged. This build product needs a coherent framework family with genuine portable SplashScreen support before complete entry execution can pass. A real desktop must supply XSETTINGS. An isolated Xvfb test needs an external owned settings publisher; production PortableEntry never fabricates desktop settings.

Required inputs:

- NinaSourceDirectory: root of the pinned, unchanged upstream source tree.
- FrameworkRuntimeDirectory and FrameworkRuntimeManifest: one released whole143 Unix framework family and the existing stage-manifests.json containing its file hashes. The directory must contain exactly those 143 files.
- MarkupCompiler, WinFxTargets and PortableReferencesTargets: the coherent source-built portable PresentationBuildTasks DLL and existing Microsoft.WinFX.targets/PortableWpfReferences.targets imports.
- OriginalManagedClosure: the original 160-PE fixture's Closure.json, with managed/ and satellites/ siblings. Its exact manifest and every PE hash are verified.
- AvalonDockPath: the qualified source-adapted AvalonDock component pinned in upstream.json. Its runtime qualification remains limited to the declared programmatic docking paths.
- SystemEventsContractPath: the existing official Microsoft.Win32.SystemEvents 10.0.10 lib/net10.0 host-neutral contract asset. Its exact hash is pinned. It is compile-only, has Private=false and remains outside the WPF runtime family. A later runtime consumer needs its own explicit companion intake.
- MvvmGeneratorPath: the pinned CommunityToolkit.Mvvm 8.4.2 analyzers/dotnet/roslyn5.0/cs/CommunityToolkit.Mvvm.SourceGenerators.dll from the existing package cache.
- OutputDirectory: a new or empty owned directory outside the inputs.
- DotnetPath: optional SDK executable, defaulting to dotnet.

Invoke with the same parameter names on Windows or Linux PowerShell. All paths must be expressed for the current host. For example, after assigning the reviewed input paths to a hashtable:

```powershell
& ./eng/portability/Build-NinaPortableApplication.ps1 @ninaBuildInputs
```

The helper selects SDK 10.0.401 with roll-forward disabled, targets net10.0 and requires runtime 10.0.12. It uses no package feeds or new packages. Existing compilation inputs and required .NET reference packs must already be present. CLI home, caches, project, generated sources, intermediates, logs and outputs stay in OutputDirectory. Original application code is never executed.

OutputDirectory contains a build product rather than a runnable closure. Only managed closure entries are compilation references; the verified original satellites are not staged into that output. A later runtime consumer must separately stage the original dependencies and satellites, the rebuilt NINA assembly and its explicitly approved runtime companions. The helper does not establish localization or full runtime loading through compilation alone.

Verification requires 196 Compile items: the original 195 plus PortableEntry.cs. The original 103 Page, one ApplicationDefinition, 10 Resource, 17 Content and one SplashScreen items remain. Both original App.xaml and MainWindow.xaml are compiled. Metadata-only inspection requires the original NINA assembly identity and exactly the original 115 embedded resource keys, including 104 BAML entries. All source files and the added bootstrap are checked again after building. results.json records input/output hashes, StartupObject, native deployment omissions and the build log.

The original source-build feasibility and paired-console-guard candidate each built on Windows and Linux. Each reported zero errors and 320 CS0612/CS0618 obsolete API warnings. Their full App resource payloads matched before and after the console-only adaptation. Separately isolated invalid-CLI App-boundary probes reached all 34 original App dictionaries and returned the expected exit code 1 on Linux and stock Windows. These bounded results do not establish valid startup, MainWindow construction, profile workflows, hardware composition, raw generated Main or package qualification.
