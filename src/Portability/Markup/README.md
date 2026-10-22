# App-local markup compiler build

`PresentationBuildTasks/PresentationBuildTasks.csproj` builds the current fork's actual markup compiler on Windows and Linux using .NET SDK 10.0.401. It reads the complete original `Compile` manifest, preserves the original compiler definitions and links the original sources directly. It references MSBuild, CodeDom and MetadataLoadContext from the running SDK. Original resource values and diagnostic identifiers are embedded unchanged, using the shared framework resource accessor generator.

`PortableMarkup.targets` is an explicit opt-in bridge. A consuming project imports it after `Microsoft.NET.Sdk` targets. It builds the app-local compiler and imports the original `PresentationBuildTasks/Microsoft.WinFX.targets` without rewriting Arcade, the repository SDK or production target frameworks. The compiler assembly uses the portable product's coherent version 10.0.0.0 identity and runs on net10.0, while compiling the current fork's source.

The source manifest contains historical path casing such as `MS/internal`. The source resolver canonicalizes only those original source paths by traversing existing entries and rejecting ambiguous matches. Application XAML paths retain native filesystem behavior.

Run from `src/Portability`:

```text
dotnet build Markup/PresentationBuildTasks/PresentationBuildTasks.csproj --nologo
dotnet build Tests/MarkupIntegration/Fixture/Fixture.csproj --nologo
```

Use the workspace `DOTNET_CLI_HOME` and `NUGET_PACKAGES` cache settings. Compiler outputs are isolated below `artifacts/portability/markup` by OS and project. Integration fixture outputs are below `artifacts/portability/markup-integration`.

The real markup integration tests are documented in `../Tests/MarkupIntegration/README.md`. They compile genuine BAML, generated component hookups and pack resources with both hosts, compare their bytes and load the result using stock Windows WPF. Full Linux framework loading is a separate gate that depends on the real PresentationCore and PresentationFramework product build.
