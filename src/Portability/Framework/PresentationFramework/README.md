# Current-source PresentationFramework build

This app-local product project links the full original `PresentationFramework.csproj` source manifest, original string resources and cursor resources. It preserves the original compiler definitions and uses the portable product's version 10 identity and original public key. It references the source-built PresentationCore, WindowsBase, System.Xaml and UI Automation projects.

The five `../PresentationFramework-System*` projects compile the original extension sources with their original ECMA friend identity. They support the framework's dynamic, data, XML, LINQ and System.Drawing extension loading contracts.

The opt-in portable backend's initial message-font profile uses `Global User Interface`, resolved by the actual portable Core adapter to an installed physical font, at the existing 11-DIP fallback size with normal style/weight and an empty frozen decoration collection. This profile applies consistently on portable builds on every OS. It does not claim parity with OS desktop font or theme preferences. Other portable system preferences use the separately tested immutable settings boundary. The ordinary build retains its existing native settings path.

Compile-only dependencies currently use genuine WindowsDesktop 10.0.13 implementation metadata for ReachFramework, System.Printing, PresentationUI and DirectWriteForwarder. These references have `Private=false`. The shared native-contract import also supplies genuine private Windows metadata. Their availability to the compiler does not establish that their Windows implementations execute on Linux. No original API or source file is removed to hide those dependencies.

Build from `src/Portability` with the workspace CLI/cache settings:

```text
dotnet build Framework/PresentationFramework/PresentationFramework.csproj --nologo
dotnet build Tests/MarkupRuntimeIntegration/Runner/Runner.csproj --nologo
```

The actual markup runtime integration fixture is described in `../../Tests/MarkupRuntimeIntegration/README.md`. The original stock Windows compiler/runtime reference remains in `Tests/MarkupIntegration`.

Portable pack-resource MIME classification retains the existing known XAML/BAML/JPG/XBAP mappings. TXT uses plain text and unknown or extensionless resources use octet-stream without OS association lookup or content sniffing. The original Windows UrlMon path remains in ordinary builds. The actual app-local BAML gates now pass on Windows and Linux for both fork-produced markup and an unchanged Microsoft-produced companion assembly; this is a bounded fixture result rather than complete application compatibility.
