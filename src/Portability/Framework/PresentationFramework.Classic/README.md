# Portable Classic theme source build

This wrapper builds the complete original `PresentationFramework.Classic` Compile
manifest and `Themes/Classic.xaml` through the genuine portable markup compiler.
It preserves the `THEME_CLASSIC` code path, KnownTypeHelper, assembly identity
`10.0.0.0` and the original MicrosoftShared public key. Original theme files are
linked rather than copied or rewritten.

`ClassicBuild.targets` prepares the original source items and signing input before
MarkupCompilePass1 so the compiler can build its temporary assembly and run Pass2
for local theme types. NoInternalTypeHelper matches the original project.

The pinned WindowsDesktop 10.0.13 PresentationUI DLL is a compile-only metadata
reference with `Private=false`. It is not a qualified portable runtime dependency.
The original Classic dictionary references PresentationUIStyleResources for the
DocumentViewer context menu and toolbar. The genuine PresentationUI Classic
dictionary has its own component resources with a generic fallback dictionary;
the full PresentationUI project also depends on ReachFramework, System.Printing
and Windows Forms. No placeholder resource-key type or Windows runtime binary is
provided here.

This wrapper does not select the portable default theme or include it in a package.
That requires a separately qualified settings and packaging change. Classic is a
named portable profile, without a Windows desktop or Aero2 pixel-parity claim.

The existing SystemSettingsIntegration executable adds two isolated modes:

```text
--classic-dictionary-diagnostic
--default-theme-layout
```

The first loads the original compiled dictionary through Application.LoadComponent
without merging it into application resources, then materializes every deferred
entry until the first failure. Its JSON identifies the failing resource and loaded
assembly paths, hashes and contexts. It does not prove default lookup is fixed.

The second verifies custom-control generic fallback, explicit-style override and
removal, then requires an ordinary UserControl's default template to connect its
content through ContentPresenter and complete positive layout. It also initializes
and lays out ordinary Button and TextBox controls, checking TextBox's genuine
PART_ContentHost ScrollViewer. It supplies no local templates. UserControl remains
a red regression while settings select `generic`.

Use `PortableFrameworkArtifactsDir` and `SystemSettingsArtifactsDirectory` to isolate
the framework graph and test outputs. Existing default output paths are preserved
when those properties are omitted.

## Diagnostic result

The complete source build passes on Windows and Linux. A clean Windows build log
records the genuine temporary assembly and MarkupCompilePass2. Windows and Linux
produce identical Classic BAML bytes, SHA256
`A6B38E3E5DD06F808D0C34376619A489471FF80149CE9781530661B345769F9D`.
Both ordinary loaders open the full 204-entry dictionary. Materializing every
deferred entry is a separate diagnostic: Windows first reaches missing
PresentationUI through FlowDocumentReader; Linux first reaches native keyboard
repeat settings through RepeatButton. Neither sweep justifies adding PresentationUI
to the selected ordinary-control path.

An artifact-only rehearsal swaps exactly two lines in a snapshot of the genuine
PortableSystemSettings source: ThemeName becomes `Classic` and ThemedResourceName
becomes `themes/classic`. Live theme selection remains generic and Classic has
not yet been integrated into the runtime package. The rehearsal uses normal
Default ALC references with no PresentationUI runtime binary.

The initial pre-metric rehearsal passed all four checks on Windows. Linux passed
custom generic precedence/removal, UserControl content layout and Button layout,
then TextBox reached SystemParameters.VerticalScrollBarWidth and native
GetSystemMetrics/user32.dll while its real ScrollViewer theme initialized. That
historical failure is preserved under `artifacts/portability/classic/rehearsal`.

The metric-qualified rehearsal refreshes the source snapshot from the approved
four-metric settings implementation, retaining its immutable 17-DIP defaults and
changing only the two theme mapping lines. Both Windows and Linux now pass all
four ordinary-control checks, including TextBox's genuine content host and layout.
The settings suite also passes 20/20 checks on both systems for default metrics,
asymmetric fractional metrics and high contrast with asymmetric metrics. These
runs use `--expect-classic-theme` to assert the rehearsal's selected policy; the
flag does not change that policy. The live generic baseline passes the same three
20-check profiles without that expectation flag.

Current evidence is under `artifacts/portability/classic/rehearsal-metrics`, with
the source overlay, provenance, compiled-source checksums and results pinned by
`handoff.json`. The original review freeze remains unchanged; a focused
`readme-revision.json` records this documentation correction and its revised hash.

Live theme selection remains `generic`. Classic selection, package integration
and a fresh unchanged-binary NINA view replay remain pending. The full deferred
document/resource closure remains unqualified, including the earlier PresentationUI
and keyboard-repeat paths. This rehearsal is not the delivered runtime or a
Phase3 qualification result.
