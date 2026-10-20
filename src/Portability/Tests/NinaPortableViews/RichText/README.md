# Original NINA rich text and AvalonEdit controls

This child preserves the seven exact sources from the qualified55-check Linux
recipe. SourceProvenance.json records each byte hash and newline count. The
targeted .gitattributes rules retain the single embedded CRLF in Program.cs and
the LF bytes in the other sources regardless of checkout conversion settings.

The three ordinary executable projects select explicit source sets:

| Project | Assembly | Process checks |
| --- | --- | --- |
| NinaRichText.csproj | NinaPortableViews | 21 |
| NinaEditorControls.csproj | AvalonEditControlChecks | 10 |
| NinaCaretProfiles.csproj | AvalonEditCaretProfiles | 8 for each -1/0/80 profile |

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

The selected closure must retain159 original members from the tracked BF610
stock160 baseline and exactly one explicitly declared source-adapted AvalonEdit.
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
catalog, external plugin, rich-document composite and full NINA startup remain
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
