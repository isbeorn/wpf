# Portable framework source build

This opt-in build compiles the current fork's actual WindowsBase, System.Xaml,
PresentationCore, PresentationFramework, UIAutomation support, framework extension
and System.Windows.Primitives sources for .NET 10. It leaves the root SDK,
WindowsDesktop SDK and production project defaults unchanged. The portable
product deliberately uses assembly version 10.0.0.0 and package version
10.0.0-portable.1 across its framework family, preserving .NET 10 metadata
contracts for NINA and genuine .NET 10 dependencies. The source revision remains
the current fork source. This is a distinct portable product identity, not a
claim that upstream version-12 binaries run on .NET 10 or that the portable
product is Microsoft's stock WPF release.

WindowsBase retains its original unconditional Compile manifest, all public
types and type forwarders. The original optional WCP_REACH_API source group
stays disabled, as in the default production build. Original generated trace
sources are compiled and the repository's Perl generator creates the package
XML string table. System.Xaml retains its default source glob plus the
original Compile Include and Remove entries. Resource accessors are generated
from the original resx files and resolve the genuine embedded messages.

The PORTABLE_WPF symbol enables the owned platform boundaries for the dispatcher,
retained CPU renderer, text, bitmap storage, settings, input and window sources.
The [source integration index](../README.md) records the tests and remaining gates.
Native Windows feature implementations remain in the assembly;
compiling them does not establish that those features work on Linux.

Build from `src/Portability` using its pinned SDK:

```powershell
$env:NUGET_PACKAGES='E:\Projects\wpf\artifacts\portability\packages'
$env:DOTNET_CLI_HOME='E:\Projects\wpf\artifacts\portability\cli-home'
dotnet build Framework/WindowsBase/WindowsBase.csproj
dotnet run --project Tests/FrameworkIdentity/Runner/Runner.csproj
```

On Linux, set equivalent artifact cache/home paths and run the same commands.
Windows and Unix outputs are separated under
`artifacts/portability/framework/{bin,obj}/<OS>/<project>`.
Set PortableFrameworkArtifactsDir to an absolute directory with a trailing
separator when running an isolated diagnostic build variant.
Perl must be on PATH on Unix. Windows defaults to Git for Windows' bundled
Perl; override `PerlCommand` when using another installation.

## Build contracts

Consumers import `Build/PortableWpfReferences.targets` to exclude only the
implicit Core WindowsBase shim before reference conflict resolution. They use
normal ProjectReferences. Runtime configuration requires Microsoft.NETCore.App
and loads the source-built WindowsBase from the application directory in the
Default AssemblyLoadContext.

Consumers must also import `Build/SystemWindowsExtensions.targets`. It selects
the genuine managed XamlAccessLevel implementation from the pinned official
System.Windows.Extensions package after ordinary package resolution. The
repository's portability subtree imports this policy through Directory.Build.targets;
external consumers do not inherit that file. The local
[portable SDK/runtime packages](../Packaging/README.md) deliver the qualified
policy transitively and verify it in external consumers. No custom assembly
resolver is used.

- `Build/SourceManifest.targets`: set WpfSourceProject. Imports the original
  Compile includes and removes. It normalizes casing only when locating the
  upstream source manifest's files, fails ambiguous paths and rejects unknown
  conditional source groups.
- `Build/SourceResources.targets`: set WpfResourceFile,
  WpfResourceNamespace and WpfResourceManifestName. Embed the original resx
  under `<manifest>.resources` and compile the original Common SR.cs.
- `Build/PublicSigning.targets`: public-signs with the original MicrosoftShared
  public key by default. Set WpfSigningKey=Ecma for System.Xaml. Keys come from
  the existing source identity declarations. This preserves friend assembly
  identity; it provides no Microsoft authentication or release signing.

## Explicit native dependency

`Build/WindowsNativeContracts.props` downloads the official
Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.13 package for the real
System.Private.Windows.Core and Accessibility assemblies. The original
Windows-only SplashScreen and COM/GDI code requires these contracts. They are
copied app-locally as ordinary managed references; there is no desktop shared
framework requirement and no system runtime replacement.

These assemblies are an explicit temporary Windows native feature dependency.
The source build does not replace COM, GDI, splash screens, Windows rights
management or the native compression implementation. Porting or isolating
those features remains required before claiming complete WindowsBase platform
support. No replacement interfaces or placeholder WPF types were introduced
to make compilation or tests pass.

PresentationCore and PresentationFramework also use genuine Windows metadata
for unported code during compilation. A successful compile does not qualify
those APIs or permit shipping Windows native binaries as a Linux implementation.
The runtime package separates compile-only metadata from its executable closure
and audits the exact managed/native allowlist in built and published consumers.
