# Original NINA framing and cache workflow

This directory preserves the qualified Cache21 recipe: the original 18 Framing
checks plus 3 actual VM cache checks. The ten C# files retain their producing byte
hashes in SourceProvenance.json. The focused .gitattributes rule prevents checkout
newline conversion. The historical X11Fixture.cs is a frozen test transport input,
not a fork of the current native backend. OriginalStartupHook.cs is linked only
for its existing in-memory settings provider; DOTNET_STARTUP_HOOKS is forbidden.

The tracked recipe built with SDK 10.0.401 and passed all 21 checks in one private
Linux Xvfb run on the complete shared 329 consumer with whole runtime 4E804208.
The build had zero warnings/errors and verified the PE/PDB identity and all 13
producing source documents. All 412 pinned inputs remained unchanged. The 53
pinned modules comprise 49 explicit Default-context load events and 4 runtime-only
observations whose original runtime guard asserts Default, path and hash. The
report records 63 selected-SDK assemblies and three generated identities
separately; the latter have no PE location or hash. The child exited 0 with empty stderr and
owned Xvfb cleanup passed. No runtime was copied and no producer was rebuilt.
The terminal handoff is
artifacts/portability/current/nina/framing-tracked-replay-20261021-1/framing21-handoff-corrected.json,
SHA 80B37BDEF8972E36AEEC8A54C7787F8038521818DD0D7F82D07B912FC70E2D27.
It preserves the original receipt and corrects only its context aggregation:
context-less runtime observations are tied to the passing original six-module
Default-context assertion. All 115 raw load events have Default context. No raw
context value, source code or runtime result was changed.
The producing README and recipe freeze are retained under that lane's
isolated-runs/run.iltFzwoe/producing-documentation directory. This result update
changes documentation only and does not require another build or run.

Historical Cache21 passed all 21 checks on the complete shared 329 consumer with
whole runtime F9D58F. Its
terminal handoff SHA is EB1680B739FE43BA4F3D3DE7711AE04D1FE7B68CC89E128865F3E8A8B9A7207B.
The historical fixture PE/PDB and13 producing documents are separate evidence.
The tracked build has its own PE/PDB/source proof; its pass does not derive from
the historical binaries.

## Behavior

The fixture constructs the real original 17-argument FramingAssistantVM, real
annotator and original source-built views/BAML. It initializes the genuine SQLite
catalog, requires nonempty results from all five original catalog queries and
loads the original 34 dictionaries in their original order. The existing public
survey/nighttime seams supply a deterministic offline 800x600 image and date.

The original checks cover static and dynamic zoom, field size, rotation, mosaic
counts, both overlap units, signed drag, owner/background SetCoordinates, original
image bindings, layout/raster and public disposal/source detach. The three cache
checks enable the public VM setting and invoke LoadImageCommand to save original
metadata/JPEG and 500/150/75 thumbnails, select CACHE to reload through the original
VM and verify its actual frozen image/annotator/bindings, then disable saving and
change the public FoV to prove a real parameter-cache miss and fresh survey request.
They restore the prior save setting and verify unchanged cache files and original
frozen source samples. Lossy JPEG output is checked through meaningful display
content rather than raw equality. The original loader's XML Source omission is
recorded without changing its behavior.

The scope is managed commands and routed Button.Click on private Linux Xvfb.
There is no full App.Main, guest desktop, hardware, remote survey, file/WCS import,
plate solve or scientific Gray16/Rgb48 pipeline claim. Retained/offscreen raster
checks do not establish native physical-desktop pixel equality. Original
constructor/resize background work has no public completion/cancellation contract;
readiness checks, public disposal and process isolation bound its remaining life.
The first dispatcher fault is captured before shutdown and stops later assertions.

## Inputs and build

Use PowerShell7 on Windows with the existing Ubuntu WSL SDK/prerequisites. Supply
one root-released complete shared consumer, its publication manifest/hash and the
matching whole Linux runtime intake/hash. Build never composes or copies a runtime,
changes deps/runtimeconfig, rebuilds NINA/framework producers or restores a backend
package. It uses the existing NinaPortableViews.csproj with Fixture.targets,
ordinary released references and Private=false. Only the small entry is compiled.

The runtime intake uses the existing consumerInputs Linux record with stageDirectory,
stageManifest/stageManifestSha256, stageFiles, familyProof/familyProofSha256 and
wrapperProof/wrapperProofSha256. The current fixture contract is whole 120 framework
members and complete 329 publication members, with unchanged Base4. The complete
manifest and every actual member are verified before initialization. A changed
component/contract needs explicit recipe review rather than a silent substitute.

Use a fresh output directory under artifacts/portability. SQL initial/migration
assets come only from the hash-verified consumer. The original pinned App.xaml
produces the exact 34 resource URIs. The deterministic survey generator must yield
D4591C53990EFB220952E32E3F125A9C46F9C1E92D257AD05690E41BE83AAB1A.
The already-tracked Fixtures/Fonts/DejaVuSans.ttf is linked directly; no Windows
system fonts are copied.

```powershell
./src/Portability/Tests/NinaPortableViews/Framing/Build.ps1 `
  -ConsumerDirectory $sharedConsumer `
  -PublicationManifest $publication -ExpectedPublicationSha256 $publicationHash `
  -RuntimeIntake $intake -ExpectedRuntimeIntakeSha256 $intakeHash `
  -WpfBaseIntake $base4Intake -OriginalAppXaml $pinnedAppXaml `
  -OutputDirectory $freshProof -LinuxSdkDirectory $linuxSdk
```

Build emits input-pins.json, fixture-source-proof.json and PreparedConsumerFreeze.json.
The proof checks the PE CodeView/PDB identity and all 13 actual source documents.
It rejects copied runtime DLLs and rehashes inputs after compilation. It records
actual compile/runtime family hashes separately from the historical Cache21
compile family and Base4 producer reference family/compiler. These historical
pins do not describe a newer actual loaded family.

## Released execution

After root releases the selected family and reviews the actual recipe diff, call
Run.sh with the reviewed freeze and its SHA. Paths passed to bash are Linux paths.
The SDK argument is its directory, not the dotnet executable.

```powershell
wsl --distribution Ubuntu --exec /bin/bash $trackedRunSh `
  --execute-reviewed $repositoryLinux $linuxSdkLinux $freezeLinux $freezeHash
```

The runner executes that exact entry against the shared consumer's ordinary deps,
runtimeconfig and whole Stage additional-deps. A private network namespace, Xvfb,
bus and state paths isolate the process. The original 90-second watchdog and
bounded retained-child deadlines remain fatal. The result must contain exactly
21 ordered passing checks, exit 0, no child stderr or dependency failures, Default
ALC loaded modules matching the pinned publication, input continuity and Xvfb
cleanup. An execution marker prevents unchanged retries. No later checks or cached
metadata are used to reinterpret an earlier failure as a pass.

The runner preserves owned state into the result directory and leaves its named
/tmp directory for inspection. It terminates/waits its retained Xvfb process and
verifies removal of that display's socket/lock. It never touches the user's display
or Gretchen's actual VM lifetime. Keep the shared consumer and all frozen source
inputs immutable for the whole run.
The runner validates every load event before deduplicating evidence, including
repeated locations. Consumer and entry loads must be pinned and in the Default
ALC. BCL loads must name an actual file under the selected SDK's real
shared/Microsoft.NETCore.App/10.0.12 directory. The report distinguishes the SDK
root from the freeze, its resolved path and observed BCL hashes from consumer
input pins. The three exact generated identities already observed in Cache21
are accepted only with empty location and Default context and reported separately
as generated/unhashed. Other empty locations, unknown nonframework paths and
non-Default contexts fail. This qualifies evidence; it is not security isolation.
