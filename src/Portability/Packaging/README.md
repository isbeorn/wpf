# Local portable distribution

This is the Phase 1 app-local distribution path for `net10.0` on Linux x64. It keeps the deliberate WPF assembly version 10 identities inside distinct `Wpf.Portable.Runtime` and `Wpf.Portable.Sdk` package IDs. Public signing preserves the reviewed identity contract; it does not authenticate the fork as a Microsoft build.

The runtime is a closed, explicitly inventoried bundle of genuine framework/backend managed assemblies and Linux x64 native assets. It does not contain reference assemblies, cycle breakers, fixtures, Windows native binaries or the .NET runtime. The SDK carries the original WinFX targets, the real PresentationBuildTasks compiler and measured non-host task dependencies. Genuine Windows metadata needed for source compilation lives only in `tools/contracts`, with copy-local disabled. MSBuild's own assemblies remain supplied by the invoking .NET 10.0.401 SDK.

The SDK adds its exact runtime package reference, owns markup item defaults and uses package-relative paths. Runtime policies import transitively, remove the .NET WindowsBase shim and select the qualified bundled Extensions/X11 identities. Conflicting consumer versions fail with a named diagnostic instead of silently substituting unrelated assemblies.

Qualification requires an external package-only consumer with its own SDK pin and local-only NuGet configuration. It must compile and run real x:Class/BAML, templates, binding, events, companion dependency properties and Unicode/case-sensitive pack resources. Framework-dependent execution precedes Linux x64 framework-dependent and self-contained publish. The native check uses the genuine packaged Window/backend with OS-injected input and compiled BAML. The package manifest records identities, hashes, provenance and notices; the output audit rejects unlisted runtime files.

The initial profile has explicit product settings and a generic resource policy. Built-in default themes, arbitrary WPF binaries, other RIDs, trimming, NativeAOT and single-file publication are not qualified by this package path. Desktop OS libraries remain prerequisites; no machine-wide .NET installation is replaced.

For native Linux X11 windows, call `NativeWindowHost.InitializeForCurrentThread(Avalonia.AppBuilder.Configure<...>())` in the application entry point before consuming WPF Dispatcher or system settings. The consumer fixture uses an explicit `StartupObject` for that ordering. Its genuine templates qualify the initial generic profile. The selected original Extensions DLL qualifies managed XAML access scopes; its Windows-only sound and certificate operations remain unsupported.

Behavior gates:

- Produce from a fresh isolated source graph and package the exact measured runtime allowlist.
- Preserve assembly identity, source/package provenance, licenses, notices and SHA-256 hashes.
- Resolve the SDK and its automatically added runtime from a local-only feed with an empty consumer package cache.
- Compile ApplicationDefinition and Page defaults, real x:Class BAML and companion resources without repository imports.
- Exercise dependency property changes in both directions, templates, binding, events and all four XAML access factories with non-public object construction.
- Publish and run ordinary framework-dependent, Linux x64 framework-dependent and Linux x64 self-contained outputs.
- Run compiled BAML in a genuine packaged Window with native Button and TextBox input under Xvfb.
- Audit package contents and every published runtime for one intended identity, matching bytes and no WindowsDesktop dependency or compile-only contracts.
- Resolve the preserved WindowsBase MarkupExtension and IComponentConnector forwarders to the actual System.Xaml types in Default ALC.
- Load the unchanged, hash-verified Microsoft SDK-produced FixtureControls binary, including its generated connectors, dictionary, template and binding.
- Compile the positive Linux application and companion dictionary on verified case-sensitive native Linux storage.

Run `eng/portability/Build-PortablePackages.ps1` from the checkout. It creates a fresh distribution directory and a local feed containing the two WPF packages plus the exact official Linux runtime/host packs needed for the self-contained verification. SDK 10.0.401 also requests its ASP.NET runtime pack during transitive framework discovery; that original package is available in the offline feed but does not become an application framework reference or published runtime. `Test-PortablePackages.ps1` creates the independent consumer; `-HostProfile PrepareLinux` writes a Linux-ready copy for `Test-PortablePackages.sh`. `Test-PortablePackageContents.ps1` audits the archives and selected consumer outputs. Producer prerequisites remain the current source build prerequisites, including Perl and the prepared pinned Avalonia sources. Consumer prerequisites are .NET SDK 10.0.401 plus the documented native desktop libraries; Xvfb and XTest belong to the verification fixture.

The Linux runner copies the prepared source into a fresh `/tmp/wpf-portable-package.*` directory and asserts distinct filename casing before compilation. NuGet cache, intermediate output, custom output, publication and execution all stay in that native Linux directory. It mirrors binaries and evidence back to the prepared artifact directory for inspection. Source and package caches are kept separate from the installed SDK. Fresh CLI homes disable automatic development-certificate generation and workload update notifications.

`Test-PortablePackageConflicts.ps1` verifies a genuine qualified Extensions package duplicate, rejects an older genuine package version and rejects unqualified publish profiles. The standard fixture uses the frozen Microsoft compiler oracle maintained by the existing markup tests; it verifies the recorded binary hash before copying and never recompiles the oracle. Application publication preserves the runtime's license, third-party notices and provenance sidecars. SDK dependencies CodeDom and MetadataLoadContext are pinned NuGet 10.0.13 host-neutral IL implementations. The first disposable Linux rehearsal rejected the Windows SDK's ReadyToRun MetadataLoadContext binary; host SDK implementation binaries are not used in the shipped task closure.

The same profile guard is packaged for both SDK and direct/transitive runtime PackageReference consumers. `Test-PortableRuntimePolicy.ps1` exercises ordinary Microsoft.NET.Sdk applications and a real intervening NuGet library. The fixture's passive compiler observer records already-loaded assemblies after genuine markup compilation and checks an earlier control invocation; it never loads either compiler dependency. Package archives are checked against complete content manifests, including notices and provenance. Original NuGet metadata is preserved byte-for-byte with the suffix `.nuspec.xml` because packing otherwise excludes nested `.nuspec` files.
