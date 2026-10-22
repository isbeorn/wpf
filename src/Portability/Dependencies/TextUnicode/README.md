# Maintained Unicode sources

`Sources/*.cs` are project-maintained derivatives compiled directly by
`Sources.props`. There is no source fetch, upstream project import, build-time
namespace/accessibility rewrite or generated intermediate source step. Future
algorithm and data fixes belong in these maintained files with corresponding
behavior verification and provenance updates.

These 34 files were frozen byte-for-byte from the existing adapted compiler
inputs at WPF commit `66984503da99872af955a4fb35a49121a665177d`.
Their retained "Generated adaptation" headers describe that historical origin,
not the current build. `maintained-sources.json` records each frozen derivative
hash, its upstream path/hash and its license. This is attributed derivative
maintenance, not independently authored Unicode code.

The ancestry is Avalonia 12.1.4 commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31`, archive SHA-256
`9E9EE6EE270BFDC02FBC34296DA51EE6242325156D2B67CBED92D2CCBC2E2029`.
`Original/**` retains byte-for-byte source snapshots and notices. `manifest.json`
retains the upstream hashes, licenses and historical adaptation counts.
`Original/Prepare-Sources.ps1.txt` records the retired extraction procedure as
historical text only. It is not a current maintenance or build command.
Git text conversion is disabled for original and frozen source bytes.

The maintained algorithms cover Unicode 17 bidi, graphemes and legal line
breaks, lookup data and their bounded utility closure. They contain no Avalonia
controls, runtime, font manager, service locator or text formatter. The original
namespace and top-level accessibility adaptation and utility qualifications are
already present. Public members of internal types are preserved. The two N0
bracket/NSM corrections remain in `Sources/BiDiAlgorithm.cs`; see
`LocalCorrections.md`. The full-corpus conformance probe checks maintained
sources and preserves the original-versus-corrected negative control.

Licensing remains per file. SixLabors bidi algorithms, array utilities and
RichTextKit trie/line-break code retain Apache-2.0 notices. Other Avalonia code
uses MIT. Generated Unicode data additionally uses Unicode License V3.
Original `licence.md` and `NOTICE.md` and the full terms under `Licenses` remain.
The build copies notices, provenance manifests, this description and the local
correction record to `ThirdPartyNotices/TextUnicode` for output and publishing.

The two unchanged test fonts now live under `../../Tests/Fixtures/Fonts` with
their licenses and original path/hash provenance. Historical copies remain
under `Original` for provenance; active fixtures do not consume those copies.
