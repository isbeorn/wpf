# Source and runtime baseline

Decision date: 18 October 2026. Source revision: `65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9`.

Keep the upstream Windows build intact and build the portable product from the same implementation sources through the opt-in projects in `src/Portability/Framework`. The portable product targets NINA's .NET 10 runtime and deliberately uses assembly version `10.0.0.0` throughout the WPF assembly family. Source revision, target framework and assembly version are recorded separately.

## Upstream Windows reference

The root configuration is coherent: SDK `12.0.100-alpha.1.26480.102`, target `net11.0` and assembly major 12. The exact pinned runtime reference package and private WinForms package contain `net11.0` assets. These values do not require a source rebase or an SDK pin change.

The primary SDK download returned 404, but the standard CI feed supplied the exact SDK:

`https://ci.dot.net/public/Sdk/12.0.100-alpha.1.26480.102/dotnet-sdk-12.0.100-alpha.1.26480.102-win-x64.zip`

The archive is 336083946 bytes. Its locally measured SHA-512 is:

```text
f6061715bac79d0e3962e11100a333064e219c9e6f2fbf2cc8a7add510db024544687de17c49fc5b6a3fb53252a9f59cb30c01909e3488b0263944c877bd763b
```

The feed's `.sha512` sidecar returned 404. This digest records the downloaded artifact; it is not an independently obtained upstream checksum. The archive was extracted into the repository's ignored `.dotnet` directory and its executable reported the exact requested SDK version.

Run the source build from the repository root with Visual Studio's native C++ workload available:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD 'artifacts/portability/cli-home'
$env:NUGET_PACKAGES = Join-Path $PWD 'artifacts/portability/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:LC_ALL = 'C'
$env:LANG = 'C'
./build.cmd -pack -plat x64 -c Release
```

This command passed with zero warnings and errors. Local packages were produced under `artifacts/packages/Release/NonShipping`; nothing was published. The inherited `C.UTF-8` locale caused Strawberry Perl warnings to be logged as errors in the first attempt. Selecting the supported `C` locale fixed the environment without changing source or build targets.

The existing upstream threading tests also passed against the source-built Windows assemblies:

```powershell
./.dotnet/dotnet.exe artifacts/bin/WindowsBase.Tests/x64/Release/net11.0-windows/WindowsBase.Tests.dll --filter-namespace System.Windows.Threading.Tests --minimum-expected-tests 100 --timeout 3m --results-directory artifacts/portability/upstream-threading-tests
```

Result: 208 passed, zero failed and zero skipped. This tests the ordinary Windows compilation path, which does not define `PORTABLE_WPF`.

## Portable product contract

Use SDK 10.0.401, `net10.0` and assembly version `10.0.0.0` in the isolated portable projects. The initial product version is `10.0.0-portable.1`. Do not change the root SDK, root TFM or upstream version files to build this product.

This choice keeps NINA's .NET 10 target and genuine version-10 Windows contract metadata consistent. A bounded version-12 portable build exposed DirectWriteForwarder signatures referring to version-10 WindowsBase `Point` and `TextFormattingMode`. A version-10 source build resolves those identities directly. No assembly-reference rewriting, fake WPF declarations or public API omissions are used to make that compile.

Portable projects import the original source lists and resource messages. Public signing uses the original WPF public keys so existing friend contracts and tested type identities work. It does not authenticate these binaries as Microsoft builds. Distinct portable package IDs and a redistributable package remain later distribution work.

The current source-built WindowsBase, System.Xaml and System.Windows.Primitives run app-local on Windows and Linux without a WindowsDesktop shared-framework requirement. A separate companion assembly exercises dependency properties, Freezable and XAML construction in the normal Default load context. Eight integration checks pass on each OS. The real dispatcher additionally passes 13 checks on each OS through its new internal pump boundary.

Genuine WindowsDesktop private helper and DirectWriteForwarder metadata are interim compile dependencies for still-unported native paths. Their availability does not establish that those paths run on Linux. Linux packaging must exclude Windows native implementations and runtime tests must demonstrate that supported portable paths do not load them.

The version-10 identity decision still requires full PresentationCore/PresentationFramework loading, compiled BAML and selected unchanged control binaries to pass their gates. The foundation checks do not establish those capabilities or complete Phase 1.

## Evidence

Raw results remain in ignored `artifacts/portability`:

- `current-source-windows-build.log`: first locale failure
- `current-source-windows-build-locale-c.log`: successful source build and pack
- `upstream-threading-tests.log`: 208 upstream Windows threading tests
- `framework-identity-{windows,linux}.json`: eight actual foundation checks per OS
- `dispatcher-integration/windows-results.json` and `dispatcher-integration-linux/linux-results.json`: 13 real dispatcher checks per OS

See the [feasibility results](feasibility-results.md) for the complete gate status and the per-fixture READMEs for reproduction commands.
