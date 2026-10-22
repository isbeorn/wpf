# Assembly identity feasibility probes

These experiments distinguish the ordinary .NET WindowsBase shim from stock WPF
and a supplied candidate runtime. No probe contains replacement WPF types.
The candidate loader is an explicit isolated-context experiment. A passing run
does not prove normal app-local publication, Linux portability or a fork build.

Behavior checklist:

- An ordinary net10 console resolves the Core WindowsBase shim and confirms that
  it has no `System.Windows.DependencyObject`.
- A separately compiled companion uses real dependency properties to observe the
  default value, SetValue, ClearValue and both change callbacks.
- The Windows fixture checks this across the companion boundary in the normal
  desktop runtime's Default load context.
- The candidate mode loads WindowsBase and the companion in one named context,
  checks assignability across that boundary and performs the same operations.
- Metadata inspection reads PE references without executing the inspected file.
- Runtime failures produce failed JSON checks and exit code 1. Build failures
  retain the build tool's nonzero exit code.

Run from this directory on Windows:

```powershell
$env:NUGET_PACKAGES='E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME='E:\Projects\wpf\artifacts\portability\cli-home'
dotnet build MetadataProbe/MetadataProbe.csproj
dotnet build WindowsFixture/WindowsFixture.csproj
dotnet MetadataProbe/bin/Debug/net10.0/MetadataProbe.dll
dotnet WindowsFixture/bin/Debug/net10.0-windows/WindowsFixture.dll
dotnet MetadataProbe/bin/Debug/net10.0/MetadataProbe.dll --inspect Companion/bin/Debug/net10.0-windows/Companion.dll
dotnet MetadataProbe/bin/Debug/net10.0/MetadataProbe.dll --runtime-directory 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.12' --companion Companion/bin/Debug/net10.0-windows/Companion.dll
```

For Linux, build the ordinary console with the workspace's selected SDK or run
the already built managed DLL. Set the cache/home to Linux paths under the
workspace artifacts directory. From the repository root under WSL:

```sh
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
dotnet src/Portability/Probes/AssemblyIdentity/MetadataProbe/bin/Debug/net10.0/MetadataProbe.dll
```

Candidate mode requires an explicitly supplied directory with WindowsBase.dll
and its runtime dependencies. Stock Windows native libraries remain Windows
libraries even if their managed assemblies load in a Linux context. Failure at
the first native operation is evidence of that boundary, not a passed port.
The companion targets Windows only to acquire the genuine stock reference
contract. Its use in candidate mode does not confer WindowsDesktop framework
references on the console host.

Observed Windows results with SDK 10.0.401 and runtime 10.0.12:

| Experiment | Result |
| --- | --- |
| Ordinary Core host | Pass: WindowsBase 4.0.0.0 in Default, DependencyObject absent |
| Stock Windows fixture | Pass: WindowsBase 10.0.0.0 in Default, default=7;set=23;clear=7;callbacks=2 |
| Stock desktop assemblies in isolated context | Pass: same operation result and cross-assembly assignability |
| Candidate directory pointed at Core shim | Expected failure: missing DependencyObject, exit 1 |
| Companion metadata | Pass: references System.Runtime 10.0.0.0 and WindowsBase 10.0.0.0 |

## Bounded source closure attempt

`SourceClosure/SourceBuild.props` and `.targets` allow one explicit experiment
against the unmodified production System.Xaml project using SDK 10. They
isolate output under artifacts and remove only its nonruntime handwritten ref
project. This intentionally bypasses Arcade to expose the generated-source
closure. It is not an alternative production build or a supported retarget.

```powershell
dotnet build E:\Projects\wpf\src\Microsoft.DotNet.Wpf\src\System.Xaml\System.Xaml.csproj -p:DirectoryBuildPropsPath=E:\Projects\wpf\src\Portability\Probes\AssemblyIdentity\SourceClosure\SourceBuild.props -p:DirectoryBuildTargetsPath=E:\Projects\wpf\src\Portability\Probes\AssemblyIdentity\SourceClosure\SourceBuild.targets -p:Platform=AnyCPU
```

This attempt restored packages and reached actual System.Xaml source compilation
but failed with 360 errors, primarily missing generated SR resource properties.
The log is `artifacts/portability/system-xaml-source-build.log`. The next source
experiment belongs to the separate SystemXaml probe and must restore resource
generation instead of supplying placeholder SR members.

WindowsBase source compilation was not attempted here. Its production project
also requires System.Windows.Primitives, private WinForms contracts, Perl
generation and trace generation. These probes do not assert that closure works
on net10 or Linux. No fork-built WindowsBase assembly was available for runtime
verification at this checkpoint.
