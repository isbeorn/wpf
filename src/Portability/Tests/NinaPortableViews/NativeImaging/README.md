# Original NINA native scientific imaging

This project preserves the existing 48-check native ImageControlVM workload for
each of Gray16 and Rgb48. Both use 6000x4000 scientific sources, two warmups and
16 measured frames, the original 1 Hz target schedule, complete CopyPixels and
caller-ownership verification, real zoom/pan/transforms and private-Xvfb input.
Numeric timing deadlines remain reported metrics under the user's accepted RGB48
timing policy. Functional assertions, native three-second waits and the original
process watchdog remain fatal. This is not a physical desktop qualification.

The three recovered source files are unchanged:

| File | SHA256 |
| --- | --- |
| Program.cs | 84FEDE1D0CA67016E04C29EB677784BA77CD7A9A0A727131279504E072252D55 |
| ImageControlCases.cs | F38751E2F4FF829560B25646FF19798D82283DD5BB84A2C7048D86CCAF206E1F |
| NativeImageCases.cs | 983726529CA49F77E0EE418ADCFB46639D10EC93FB8C1C5757554B4C81BD8766 |

The focused .gitattributes rules disable newline conversion for these three files.
Program.cs retains CRLF while both case files retain LF, preserving their accepted
byte hashes on fresh checkouts regardless of core.autocrlf.

The shared NativeWindowIntegration X11Fixture provides native transport, including
the qualified batched Pixels method and the committed exact-query IsMapped
BadWindow handling. The original legacy-versus-batched comparisons retain all
66 window/root values in initial and crosshair on/off states. No alternate render
or capture API is used.

The workload assigns prepared scientific frames through the original ImageControlVM
Image property. AutoStretch and DetectStars are disabled. Acquisition, PrepareImage,
star detection and annotation processing are outside this display qualification;
the separate NINA.Image application port remains deferred.

## Retained inputs

The recovered BaselinePin.json and empty Directory.Build.targets already exist in
the parent directory. The 33-entry ResourceSources.json is linked from AvalonDock.
The parent props and pinned SDK driver supply common settings; the project keeps
the recovered HintPath policy, disabled runtime roll-forward and native package
versions. The old DirectGuider host remains a separate historical diagnostic.

CurrentBaselinePin.json selects the original source-rebuilt stock160 closure,
BF6107020B8CD5EB07AD402DCB799F80CEF777DBD19282DAE1B0EC04409F0163,
with canonical hash 8E83C21CCA096FE0585949E1CB7143E53128018274A657C59359A53616684418.
The historical BaselinePin.json is not the native workload's current byte baseline.
Portable Core or Astrometry substitutions are not part of this fixture.

## Build and run

Use PowerShell 7 on Windows with the existing Ubuntu WSL prerequisite installation.
Set `$release`, `$releaseHash` and `$closure` to the root-released complete Unix
Stage record, its SHA256 and the pinned stock Closure.json. The record uses the
existing InputCommit, Stage, Manifest, ManifestSha256, FileCount, FamilyProof,
FamilyProofSha256 and SourceInventorySha256 fields. The complete actual inventory
is checked, with no fixed framework file count or per-DLL overlay.

```powershell
$proof = 'E:/Projects/wpf/artifacts/portability/current/reports/nina-imaging-1'
./eng/portability/Run-NinaNativeImaging.ps1 -Action Build `
  -FrameworkRelease $release -ExpectedFrameworkReleaseSha256 $releaseHash `
  -OriginalClosure $closure -OutputDirectory $proof
```

Build invokes Build-OwnedPortable.ps1 for this consumer only, using the existing
build graph and global package cache. It references retained Stage and stock
inputs directly. Only the ordinary consumer output is populated. It does not copy
input graphs or emit a new framework Stage. It verifies generated deps, exact
native assets, optimized Release PE attributes, four source/PDB documents and the
complete Stage/stock output before writing PreparedConsumerFreeze.json.

Freeze file paths are relative to the repository root. consumerBuildDirectory
identifies the shared build root and consumerOutputDirectory identifies this
project's ordinary output. The existing ExecutionRelease.json gate must pin that
compiled freeze, root family release, allowed formats and reported-only timing
policy. Build does not create a review gate or launch a display.

Once that existing gate has been reviewed, run each format separately:

```powershell
./eng/portability/Run-NinaNativeImaging.ps1 -Action Run `
  -ExecutionRelease "$proof/ExecutionRelease.json" -Format Gray16
./eng/portability/Run-NinaNativeImaging.ps1 -Action Run `
  -ExecutionRelease "$proof/ExecutionRelease.json" -Format Rgb48
```

Keep this consumer's shared output unchanged until both processes finish. Each
process rechecks the frozen files and released inputs, executes the frozen output
directly and owns only private Xvfb, bus and state paths. It retains reports and
profile evidence, verifies process/socket cleanup and removes its exact disposable
tree in the same WSL namespace. Stop on the first unexpected failure. Do not retry
unchanged or reinterpret an earlier negative result as green.

## Accepted historical result

Both formats passed 48/48 against owned Stage704636, consumer freeze
80ABA4A647FD51CF8260F783EC6360007AAC711C01CA6AD78132DF5DB3FC5B37.
The compact result pin is
9F5167278A7E67EE5CDF617D50CEEA4F9ED3AD93384E99365BA27E126BC29DB3.
RGB48 reported one 60.4041 ms transaction deadline miss, with no native deadline
miss. This is accepted functional evidence with timing reported only, not a
sustained 1 Hz claim. The original adapter F465420C...5A4947 and repaired adapter
07706C12...65E210 remain historical source pins. No performance optimization is
part of this migration.

## Current tracked recipe qualification

The tracked recipe passed 48/48 checks for each format against the complete
Stage-nina-integration-1 runtime. Both processes returned zero, with empty stderr
and confirmed cleanup of their private processes, display sockets and state trees.
Each format retained 27 complete source ownership checks and three states of the
66-point legacy-versus-batched window/root pixel comparison.

The prepared consumer freeze is
6E65DEC6760860CE3823A53C3B5B2DDB808502097F5DFA7E91CDEFE494EA7B66.
The raw qualification record is
artifacts/portability/current/reports/nina-imaging-durable-4/durable-qualification.json,
SHA256 96DE34363BD9CA23CA36AA67534C89539E5C6673A94243C6509D18AE84EC9625.
Neither format reported a native or transaction deadline miss in these runs.
Timing remains reported only; this is no sustained cadence, physical desktop,
package or full NINA startup qualification. Earlier failed orchestration attempts
and the accepted historical result remain preserved separately.
