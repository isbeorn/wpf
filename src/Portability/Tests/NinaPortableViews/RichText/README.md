# Original NINA rich text and AvalonEdit controls

This child preserves the seven exact sources from the qualified55-check Linux
recipe. SourceProvenance.json records each byte hash and newline count. The
targeted .gitattributes rules retain the single embedded CRLF in Program.cs and
the LF bytes in the other sources regardless of checkout conversion settings.

The ordinary executable projects select explicit source sets:

| Project | Assembly | Process checks |
| --- | --- | --- |
| NinaRichText.csproj | NinaPortableViews | 21 |
| NinaEditorControls.csproj | AvalonEditControlChecks | 10 |
| NinaCaretProfiles.csproj | AvalonEditCaretProfiles | 8 for each -1/0/80 profile |
| NinaMarkdownComposite.csproj | NinaMarkdownComposite | 33 |

There is one shared copy of RichTextCases and the original in-memory provider.
The three Program variants and two EditorControlCases variants are retained
verbatim rather than refactored during source intake. OriginalStartupHook's
Initialize is never called: inherited DOTNET_STARTUP_HOOKS is forbidden and only
its existing nested provider type is instantiated. No original App startup,
catalog, remote command, hardware or plugin installation is invoked.

The shared NativeWindowIntegration/X11Fixture supplies actual native pointer and
XFixes cursor observations. The existing pinned DejaVu font and AvalonDock's
33-entry ResourceSources.json are linked. Original Hyperlink is inserted before
SplitButton by the unchanged host, producing the accepted34-dictionary closure
and actual Markdown Hyperlink BasedOn identity check. No templates, styles or
original BAML are replaced.

## Inputs and build

Use the pinned SDK10.0.401, runtime10.0.12 and shared package cache. The two native
packages reuse NativeImaging's exact versions and lockfile content hashes.
Projects do not reference framework projects or build a framework Stage. Each
uses a complete root-released Stage and generated SourceStageReferences.props,
plus a selected fixture containing Closure.json and References.props.

For the original three variants, the selected closure retains 159 original members
from the tracked BF610 stock160 baseline and one declared source-adapted AvalonEdit.
Preserve its source, PE/PDB, identity, resource and licensing proof. It is not an
unchanged160 binary claim. The unchanged host's historical check name
unchanged-160-original-closure verifies the selected manifest's actual hashes;
the reporting claim explicitly describes the declared runtime closure.

Reuse the existing preparation that generated the qualified workflow's selected
fixture and complete SourceStageReferences.props. NativeImaging shows the same
released Stage/reference/deps preparation pattern. Run-NinaPortableViews.ps1 is a
separate historical final2 diagnostic and must not select inputs for this child.
Do not call its old hardcoded PrepareOnly route or copy an old individual DLL.

On Windows, with already prepared explicit input paths:

```powershell
$env:NinaPortableArtifacts = $proof
$env:NinaFixtureDirectory = $selectedFixture
$env:NinaSourceStageDirectory = $releasedStage
$env:WSLENV = 'NinaPortableArtifacts/p:NinaFixtureDirectory/p:NinaSourceStageDirectory/p'
foreach ($project in 'NinaRichText', 'NinaEditorControls', 'NinaCaretProfiles') {
    ./eng/portability/Build-OwnedPortable.ps1 -Os linux -Label $project `
        -Project "src/Portability/Tests/NinaPortableViews/RichText/$project.csproj" `
        -Workspace $workspace -PackageCache $packageCache
}
```

Save and restore the caller's existing environment around those commands. The
ordinary build driver supplies locked Release output under its existing build
root. It does not need a framework build or another runtime clone. Generated
deps must declare all selected managed inputs and regular Linux native assets.
Capture source-to-PDB correspondence for the7/8/8 source documents, actual
PE/PDB identity and hashes, the complete Stage inventory and selected160 output.
Recheck the released Stage and frozen compiler inputs after preparation.

## Existing frozen replay recipe

The parameterized Run-NinaRichText.sh preserves the accepted runner recipe and
owns the timeout child/session plus Xvfb lifecycle. It uses the existing
PreparedConsumerFreeze shape: repository-relative files with sha256 values and
consumerOutputDirectory. Produce a separate freeze for each executable using
the existing input/PDB/deps capture tooling. Include all runtime files, the seven
preserved sources, SourceProvenance.json, linked native helper, project metadata,
runner and selected input proofs. Hash it before executing. This is ordinary
input pinning, not an additional approval/release protocol.

Run only against that frozen ordinary output. In WSL, with absolute Linux paths:

```bash
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$originalFreeze" "$originalHash" original21
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$controlsFreeze" "$controlsHash" controls10
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$profilesFreeze" "$profilesHash" profiles8 -1
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$profilesFreeze" "$profilesHash" profiles8 0
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$profilesFreeze" "$profilesHash" profiles8 80
```

Do not rebuild or change shared inputs while those five processes run. Each gets
fresh owned state, a private0700 runtime directory, absent session/system DBus
and an explicitly unused private Xvfb display with -pn and TCP disabled. It clears
inherited startup/IME/accessibility settings. Actual pointer operations target
only that owned display. The child retains its35-second managed watchdog and the
runner retains the45-second external timeout. It stops only its recorded child
session and recorded server, verifies process/socket/lock cleanup and retains
stdout, stderr, raw JSON and cleanup classification. Failure remains fatal and
is preserved. Do not retry unchanged or reinterpret a missing result as pass.

## Observable contracts

- Original21: genuine MarkdownStyle/MdXaml fenced code, measured AvalonEdit,
  selection, scroll, readonly input/delete, document replacement and reopen.
- Controls10: editable insertion, forward/reverse selection, Undo/Redo,
  readonly/reenable pairs, actual caret rasters and native cursor entry/hide/show.
  Handled child queries, movement, focus loss, unload and reload remain checked.
- Profiles24: -1/0 stop the actual timer and retain a steady caret;80 directly
  observes the actual private DispatcherTimer Interval=80ms and enabled state,
  alongside existing on/off/on raster proof. The12-argument profile factory is
  explicit test setup. There is no guessed constant or exact scheduling bound.

Retain first-chance dependency records. The qualified workflow observed
Ribbon/WindowsFormsIntegration FileNotFound probes during resource loading; they
did not escape or fail the checks. No assemblies are added to hide them. The raw
legacy nativeInput=false field does not erase explicit private XTest pointer
observations. Physical keyboard, native IME, live GNOME, full PluginsView Loaded,
catalog, external plugin, broader rich-document coverage and full NINA startup remain
separate gates. The earlier stock nonportable21+9 run is separate retained
WindowsDesktop evidence, not qualification of this new repository-native path.

## Initial tracked Linux replay

The repository-native route passed 55/55 on five first invocations against the
complete released nina-integration-1 Stage and source-adapted AvalonEdit 870514.
Three optimized Release builds had zero warnings/errors, exact PDB source
correspondence 7/8/8 and one shared 299-file consumer output. Generated deps and
selected 159-original-plus-one-adapted closure matched the declared inputs.

Original 21 and controls 10 passed, then profiles -1/0/80 each passed 8. Both
sentinels stopped the actual timer; 80 observed Interval=80ms and enabled state
alongside on/off/on rasters. Native XTest cursor entry/hide/show, handled child,
movement, focus loss and unload/reload passed. All children exited 0 with empty
stderr and verified private process/display/runtime cleanup. All 649 frozen files
and the original 55 seal remained unchanged. No physical keyboard, native IME,
live GNOME, rich-document composite or full NINA qualification is implied.

Compact receipts: artifacts/portability/current/richtext/tracked-replay-1.
PreparedConsumerFreeze.json SHA256
4AB8F5AD007B92C900E6FA5CF983AD4C14F21DC18B61D18349197DBEB6BB20BE;
replay-results.json SHA256
D6FFC469793965A213CD6B34569A7ABDE113805993088A57152E69DF92678823.
Future fixture or input changes require fresh build/PDB/closure and native replay
verification; this retained result does not qualify a different family.

## Original Markdown composite variant

NinaMarkdownComposite.csproj adds the calibrated Markdown33 workflow under
Markdown/. It reuses RichTextCases, OriginalStartupHook, X11Fixture, the existing
font and all original resource sources. Markdown/MarkdownCompositeCases.cs,
CompositeInputs/composite.md and local.png preserve their qualified bytes. The
additional SourceProvenance.markdownComposite entry records their hashes and
the source-intake adaptations without changing the original seven source entries.

Prepare this variant with the existing NinaFixtureDirectory,
NinaSourceStageDirectory and generated SourceStageReferences.props contract.
The selected160 closure declares158 original members plus the explicitly
source-adapted AvalonDock and AvalonEdit members. Freeze the complete released
Stage inventory, selected closure, ordinary runtime output, source/PDB proof,
project metadata, new Markdown sources/inputs and the existing runner. This
variant reads the copied Closure.json and verifies loaded assemblies against
its own ordinary output. It does not read a temporary shared consumer root or
borrow another consumer's deps/runtimeconfig.

Use the existing build driver with the additional ordinary project:

```powershell
./eng/portability/Build-OwnedPortable.ps1 -Os linux -Label NinaMarkdownComposite `
    -Project src/Portability/Tests/NinaPortableViews/RichText/NinaMarkdownComposite.csproj `
    -Workspace $workspace -PackageCache $packageCache
```

With its separately prepared ordinary output freeze, replay through the same
private-display runner:

```bash
bash eng/portability/Run-NinaRichText.sh "$repository" "$dotnet" "$markdownFreeze" "$markdownHash" markdown33
python3 src/Portability/Tests/NinaPortableViews/RichText/Markdown/Verify-Classification.py
```

Markdown33 retains the original21 behavior checks plus12 composite checks:
original PluginsView Description subtree and model binding, original DOM/style
and callback marker policy, asynchronous local PNG loading, real table/list/code
layout, exact retained image channels/chroma, forward/reverse document and child
selection, outer scroll, width A-B-A, command enable/disable/restore, model
replace/restore and host close. Full PluginsView Loaded, catalog, browser command
execution, physical keyboard, native IME and full NINA startup remain separate.

Every first-chance dependency event remains in raw dependencyFailures with its
exception type, missing identity, inner-exception state and live caller evidence.
The markdown33 classifier accepts only the four qualified full/short Ribbon and
WindowsFormsIntegration identities as FileNotFoundException with no inner
exception and the genuine typed ResolveAssembly -> GetAssembly ->
EnsureXmlnsAssembliesLoaded chain. All other events fail qualification. It also
requires the exact ordered33 names, all pass statuses, child exit0, empty stderr,
unchanged frozen inputs and verified cleanup. The other three suite classifiers
are unchanged. The saved-data checker exercises this actual runner branch; it
does not replace runtime verification of the C# caller predicate.

The ignored projection passed33/33 with all96 raw events preserved and qualified
as optional namespace probes, zero unclassified events and all289 shared runtime
files unchanged. Its compiled freeze is
8F1062A530224F87706B1968F271E7F414760C9FF3445A20702BBF92427DA7AB.

The ordinary project and input transport then passed 33/33 against the complete
window-placement-integration-3 EWMH-qualified Stage family, root release
97571F0CF9F3CB68B9004BE25CCE55A7D51531AAC3892C73C857C9B6275511A6.
One optimized Release build had zero warnings/errors and exact PE/PDB identity
with eight source documents. Its own 292-file output contained the 120 released
Stage files and selected closure of 158 original members plus the two adapted
components. No framework was rebuilt and the existing 289-file shared runtime
remained unchanged.

The single private-display replay retained all 96 typed optional namespace probes,
24 for each full/short identity, with zero unknown dependency failures. All 33
ordered checks passed, the child exited 0, stderr was empty and process, display
and private runtime cleanup were verified. All 684 frozen rows were unchanged at
replay closeout. The actual runner classifier's 33 saved-data controls also passed.
The older variants' 55 checks have not been replayed against this newest family.
Full PluginsView Loaded, browser command execution, catalog, physical keyboard,
native IME, desktop-session behavior and full NINA startup remain separate gates.

Receipts: artifacts/portability/current/richtext/markdown33-stage3-replay-1.
PreparedConsumerFreeze.json SHA256
3B100EBF4351E635F9135329862B0F0BA1073E858F6DFA39CA35C9A787D7EE43;
qualified-result.json SHA256
7C2BD9B889839489AF521C65044F357F57F04BCF187F6A174F5D89A277EB194C;
qualification-seal.json SHA256
FCE66E47D19ECCDB158B860E3606215CC27DED015AB7F2F062F83FA88FEF105C.
This README and SourceProvenance.json describe that completed run after closeout.
Their documentation-only delta is recorded separately; the historical source-ready
manifest and compiled freeze retain the exact pre-closeout documentation bytes.
