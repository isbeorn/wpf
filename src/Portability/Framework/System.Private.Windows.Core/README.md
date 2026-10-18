# Source-built Windows.Core helper

This isolated project compiles the complete original managed helper. It does not select it for existing framework projects or packages. Windows API declarations and their implementations remain present; building portable IL does not qualify their native operations on Linux.

The source pin is dotnet/dotnet `4099f83977f4d29604a9aeb971fb0a7bc1d5f699`, subtree `4c14b7596637357025a4d8867b0c79f6df1e3a2e`. That VMR's `src/source-manifest.json` maps winforms to `3c92f9f1cc1dcc74779795d518c67aa80f472df7`. The 20 MB winforms archive avoids downloading the whole VMR. `SourceManifest.props` records all 261 subtree blobs, including all 256 C# files (796171 bytes), original project, NativeMethods inputs and SR.resx. Every extracted file is checked against its SHA-256 before compilation. The manifest's Git blob identities were verified against the exact VMR subtree.

Run `Prepare-Sources.ps1` once, then build this project with SDK 10.0.401. Source preparation only downloads the pinned archive when missing. The build verifies the archive, extracts the selected unchanged files into ignored artifacts and prepares the original ECMA public key. No upstream source file is patched or omitted. The original AssemblyInfo, IVTs and System.PublicKeys compile directly. Public signing preserves the binding identity; it does not authenticate the assembly as a Microsoft build.

The original CsWin32 0.3.151 is available on the original public Windows SDK feed, rather than nuget.org. The original Nrbf version is 10.0.13. For example, from `src/Portability`, with `NUGET_PACKAGES` and `DOTNET_CLI_HOME` set to dedicated artifacts directories:

```powershell
./Framework/System.Private.Windows.Core/Prepare-Sources.ps1
dotnet build Framework/System.Private.Windows.Core/System.Private.Windows.Core.csproj -c Release --disable-build-servers --source https://api.nuget.org/v3/index.json --source https://pkgs.dev.azure.com/azure-public/winsdk/_packaging/CI/nuget/v3/index.json
```

The same command runs under the workspace Linux SDK after acquisition. SDK GenerateResource generates the strongly typed SR accessors from the genuine resx and embeds the original `System.Private.Windows.Core.Resources.SR.resources` name. CsWin32 consumes the unchanged original NativeMethods.txt/json. Neither resource generation nor native declaration generation needs a hand-written replacement type.

The output is net10.0 AnyCPU, IL-only, without a ReadyToRun managed native header. It retains assembly version 10.0.0.0 and ECMA token b77a5c561934e089. Its only NuGet runtime dependency is System.Formats.Nrbf 10.0.13; generator/Win32 metadata dependencies are private build inputs. There is no framework reference to WindowsDesktop, native project reference or runtime switch disabling ReadyToRun. SDK 10.0.401 reports one CS9336 redundant-pattern warning in unchanged upstream SafeArrayScope.cs; it is retained rather than hidden.

Native COM/OLE, registry, GDI and other Windows services in this helper remain unported. The bounded qualification is default-CLR loading of the genuine interface closure and managed WPF DataObject store, with native operations owned by their actual platform backends.
