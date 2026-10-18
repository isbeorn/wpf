# Bounded Unicode dependency

Original files are byte-for-byte extracts from Avalonia 12.1.4 commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31`. The source archive SHA-256 is
`9E9EE6EE270BFDC02FBC34296DA51EE6242325156D2B67CBED92D2CCBC2E2029`.
`Prepare-Sources.ps1` extracts only the enumerated files from that verified
archive. `manifest.json` records each original's hash, license and adaptation
counts. The Unicode data version is 17.0.0, as recorded in upstream
`UnicodeDataSource.cs`.
`Original/**` disables Git text conversion so Windows checkout line endings
cannot invalidate the recorded upstream byte hashes.

The 34 C# files contain Unicode bidi, grapheme and legal line-break algorithms,
generated lookup data and their bounded utility closure. They contain no
Avalonia controls, font manager, service locator or text formatter. The WPF
adapter constructs the existing WPF `TextLine` and `GlyphRun` types.

The build verifies original hashes and exact adaptation counts before writing
generated sources under its isolated intermediate output. Only namespaces and
top-level public type accessibility are adapted. Utility type references are
qualified to avoid WPF's existing `System.Collections.Generic.ArrayBuilder`.
Public members of internal
types are preserved. Generated files carry an adaptation notice. Unchanged
generated files are not rewritten. A changed original fails the build.
The two guarded bracket/NSM corrections are documented in
`LocalCorrections.md`; the original source files remain unchanged.
Licenses, attribution, the source manifest and local correction notice are
copied to `ThirdPartyNotices/TextUnicode` for runtime output and publishing.

Licensing is per file. SixLabors bidi algorithms, array utilities and
RichTextKit trie/line-break code retain their Apache-2.0 notices. Other
Avalonia code uses its MIT license. Generated Unicode data additionally uses
Unicode License V3. Original `licence.md` and `NOTICE.md` are preserved, with
full Apache and Unicode terms under `Licenses`. These are not all MIT files.

Two unmodified test fonts come from the same verified archive. DejaVu Sans
has SHA-256 `35226AC1E50D1B60AF2FF7DA5878D8EDE0C83BC00FE84713F265CB6DED8F5F9F`.
Inter Regular has SHA-256
`41AB0F707A2BFAB8133CCDFCDAB52282F5F79E5751F43A264805451C7BB95FB8`.
DejaVu's embedded complete Bitstream Vera/Arev license and public domain
statement are preserved in `Licenses/DejaVu.txt`. Inter's original copyright
is preserved there alongside its OFL 1.1 license. The font files are fixtures,
not fonts downloaded or installed at runtime.
