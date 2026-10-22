# Current-source System.Xaml probe

This bounded feasibility probe compiles the fork's real `System.Xaml` implementation into an unsigned `System.Xaml, Version=10.0.0.0` library targeting `net10.0`. It does not modify production files. It is not a replacement for the signed shipping assembly or evidence that WPF, BAML or NINA runs on Linux.

Behavior checklist:

- Compile the actual `System.Xaml` source closure with the original `OLDRESOURCES` and `SYSTEM_XAML` definitions and actual type forwarders.
- Resolve public CLR fixture types from a separate assembly.
- Parse scalar properties and an implicit `[ContentProperty]` collection.
- Execute a custom markup extension and a custom type converter.
- Resolve a forward `x:Reference` with shared object identity.
- Save and reload properties, both collection entries, converted values and shared identity.
- Reject malformed XML with line and position information.
- Produce a real localized-resource diagnostic for an unknown CLR type.

`SourceLibrary.csproj` includes original sources directly, mirrors the original project's shared-source additions and reads its `Compile Remove` entries at build time. It preserves `OtherAssemblyAttrs.cs` and its actual `System.Security.Permissions` and `System.Windows.Extensions` forwarders using pinned `10.0.0` packages. The original resource file is embedded under the original `FxResources.System.Xaml.SR` manifest identity. The small inline C# MSBuild task replaces only unavailable Arcade resource accessor generation: it reads the actual resource keys and generates a partial `System.SR` using the original shared resource lookup implementation. It never invents message strings. SDK resource generation embeds the original message values.

Build and run from `src/Portability` with .NET SDK 10.0.401:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/../../artifacts/portability/cli-home"
$env:NUGET_PACKAGES = "$PWD/../../artifacts/portability/packages"
dotnet build Probes/SystemXaml/Runner/Runner.csproj --nologo
dotnet run --no-build --project Probes/SystemXaml/Runner/Runner.csproj
```

```sh
export DOTNET_CLI_HOME="$PWD/../../artifacts/portability/linux-cli-home"
export NUGET_PACKAGES="$PWD/../../artifacts/portability/linux-packages"
dotnet build Probes/SystemXaml/Runner/Runner.csproj --nologo
dotnet run --no-build --project Probes/SystemXaml/Runner/Runner.csproj
```

All generated sources and binaries are placed below `artifacts/portability/system-xaml`, separated by MSBuild OS and project name. The executable emits JSON and returns a nonzero exit code if a check fails. Run output includes the saved XAML and observed diagnostics. `build-windows.log`, `windows.json`, `build-linux.log` and `linux.json` contain the recorded results when run using the workspace's validation procedure.

Recorded on 2026-10-18 with SDK 10.0.401:

| Host | Build | Runtime checks |
| --- | --- | --- |
| Windows 10.0.26200 | Pass, four CA1416 warnings on a full build | 7/7 pass |
| Ubuntu 24.04.1 LTS under WSL | Pass, four CA1416 warnings | 7/7 pass |

Known limits: four CA1416 warnings identify Windows-only `XamlAccessLevel` APIs on paths outside the exercised public CLR flow. Security/access-level semantics and private-member construction are not proved. The unknown CLR type diagnostic from `XamlServices.Parse` reports line 0 and position 0; malformed XML retains its actual line and position. This probe does not cover WPF dependency objects, controls, bindings, resources, templates, compiled markup, BAML, strong-name compatibility or NINA integration.
