# Real WPF markup integration

The fixture imports the app-local compiler bridge and the original `Microsoft.WinFX.targets`. It compiles against the installed/downloaded stock WindowsDesktop reference assemblies to isolate markup compilation from the ongoing Linux framework runtime port. No WPF types or BAML payloads are substituted.

Behavior checklist:

- Build the current fork's original PresentationBuildTasks source closure on Windows and Linux.
- Generate real BAML and `InitializeComponent` for a view with spaces and Unicode in its source path.
- Generate named component fields, `IComponentConnector` and an actual button event hookup.
- Compile local control/model references through the actual temporary-assembly/pass2 pipeline.
- Compile a merged resource dictionary and data template.
- Embed and retrieve an actual resource whose source path contains mixed case, spaces and Unicode.
- Compare all embedded BAML and pack resource bytes between compiler hosts.
- Reject a wrongly cased source path on Linux-native storage.
- Construct the genuine compiled view with stock Windows WPF, invoke its event and verify repeated initialization does not duplicate the hookup.
- Materialize the compiled dictionary template and its binding on Windows.

Build from `src/Portability` with SDK 10.0.401 and the workspace-local CLI/cache environment variables:

```text
dotnet build Tests/MarkupIntegration/Fixture/Fixture.csproj --nologo
dotnet build Tests/MarkupIntegration/Verify/Verify.csproj --nologo
dotnet build Tests/MarkupIntegration/WindowsRuntime/WindowsRuntime.csproj --nologo
dotnet run --no-build --project Tests/MarkupIntegration/WindowsRuntime/WindowsRuntime.csproj
```

The last two commands are the Windows runtime reference gate. The portable `Verify` executable accepts the fixture assembly, generated view source and optional comparison assembly:

```text
Verify <fixture-assembly> <generated-view-source> [comparison-fixture-assembly]
```

Use `-t:Rebuild` once when starting a fresh pass2 certification. `PassEvidence.targets` records the actual pass1 decision on every build and records BAML SHA-256 values only after a required real second pass completes. An unchanged build truthfully reports `RequirePass2=False`; the verifier accepts reuse only when the prior pass2 certificate still matches every embedded BAML payload. Missing or changed certificates fail verification. Both evidence files participate in normal MSBuild clean tracking.

The compiler project reference isolates consumer output, intermediate, artifacts, target-framework and runtime properties. Its task assembly stays at the bridge's SDK-compatible `net10.0` location when the application uses `-o`, `OutDir`, `BaseOutputPath`, `ArtifactsPath` or a runtime identifier. Regression evidence is recorded separately in `artifacts/portability/markup/dewey-*.log`, `dewey-*.json` and `DeweyFixed*-<os>.log`: fresh custom output, normal output, three unchanged builds, invalid-certificate rejection and real stock Windows runtime construction from custom output.

For Debug builds, the fixture is at `artifacts/portability/markup-integration/bin/<Windows_NT|Unix>/Fixture/Debug/net10.0-windows/Portability.MarkupIntegration.Fixture.dll`. Generated view source is at `artifacts/portability/markup-integration/obj/<Windows_NT|Unix>/Fixture/Debug/net10.0-windows/Views/Orbit View Ω.g.cs`. Run `Verify.dll` with `dotnet`, or use `dotnet run --no-build --project Tests/MarkupIntegration/Verify/Verify.csproj -- <arguments>`.

On Linux run `sh Tests/MarkupIntegration/Verify-NativeCase.sh`. The script creates one disposable source file on native `/tmp`, confirms `Views` and `views` differ and requires the real compiler's BG1002 missing-file diagnostic for the incorrectly cased path. It removes the source after the check and keeps all caches and build outputs in the workspace. Native staging matters because the existing WSL `/mnt/e` mount is case-insensitive and cannot establish this behavior itself.

Recorded results are under `artifacts/portability/markup`: compiler/fixture build logs, `build-verification-windows.json`, `build-verification-linux.json`, `runtime-windows.json`, `native-case-linux.json` and the expected-failure `wrong-case-linux.log`. The first three resources compared byte-for-byte are the 1730-byte view BAML, 1172-byte dictionary BAML and 73-byte text resource. Resource IDs use WPF's actual percent-encoded canonical form, including `views/orbit%20view%20%ce%a9.baml`.

The separate `../MarkupRuntimeIntegration` gate executes both fork-produced BAML and an unchanged Microsoft-produced binary against the real portable source framework. This fixture continues to isolate compiler behavior against stock references. No NINA view, full desktop application or third-party control compatibility is claimed here.
