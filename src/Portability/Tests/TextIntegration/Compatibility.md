# Horizontal text compatibility ledger

The user selected Unicode 17 paragraph behavior. Native Windows Line Services
is a reference, not an alternate formatter or hidden compatibility mode.

| Area | Portable contract | Stock Windows observation |
| --- | --- | --- |
| Bidi brackets | Unicode 17 N0, including bracket-following NSMs | For RTL `سلام abc (123) שלום`, native WPF places the closing `)` in the surrounding RTL run; Unicode 17 keeps the complete Latin bracket span LTR |
| Bidi level metadata | Full Unicode 17 resolved levels | Native WPF commonly emits level 0 for even runs whose resolved level is 2; a pure U+1F600 RTL line also emits level 0 while Unicode 17 resolves the neutral character to level 1 |
| Explicit line boundaries | CR/LF, LS, PS, NEL, VT and FF terminate before glyph shaping | The 108-case boundary capture compares absolute source starts, consumed lengths, newline lengths and continuation presence in both directions and wrapping modes, including equivalent source chunks |
| Coordinates | Hit/selection distances from the paragraph leading edge; RTL drawing from its supplied right edge | Confirmed through actual TextLine distances, bounds and retained GlyphRun origins |
| Ligature carets | Grapheme stops inside Arabic ligatures | Native WPF interpolates caret positions inside the ligature |
| Explicit glyph placement | WPF nominal advances, offsets and Display rounding | Eight fixed-font BuildGeometry cases cover LTR/RTL, two DPI values and a zero-advance combining mark, with 0.03 DIP tolerance |
| Ideal shaping | HarfBuzz/Skia advances from the pinned font | At 24 DIP and 1.5 pixels/DIP, two captured paragraphs differ by approximately 0.114 and 0.193 DIP in whole-line width |
| Display shaping | Advances rounded to the physical pixel grid | The same paragraphs differ from DirectWrite by 2 and 4 DIP in whole-line width; native hinting/raster parity is unqualified |
| Default numbers | Existing WPF DigitState resolves no-op settings | Actual TextBlock uses User/AsCulture, en-US/en-us, native digits 0-9 and DigitSubstitution.None in the pinned reference |
| Required substitution | Named unsupported diagnostic | Arabic Context, NativeNational and Traditional settings remain outside the profile |
| Fallback | One face per complete grapheme, style-ranked then ordinal local path | The fixed fixture forces Inter's missing Arabic coverage to DejaVu; installed font differences are excluded |
| Word selection | User separately selected Unicode 17 category/script/width data within WPF's existing RichEdit selection algorithm and Latin table | Win32 classification is retained on normal Windows builds; portable differences below are deliberate, independent of the earlier bidi decision |

## Word selection classification

The portable classifier supplies the CType flags consumed by
`SelectionWordBreaker`. Word movement, selection direction, apostrophe rules,
kana grouping and whitespace handling still run through the existing WPF
algorithm. This does not introduce general UAX #29 word segmentation.

On Windows build 26200 with stock WPF 10.0.1226.42308, the initial category
prototype differed in 142 BMP `GetClasses` results: 138 punctuation characters,
three historical symbol classifications and U+180E. The final classifier has
141 differences because Unicode 17's punctuation Diacritic entries preserve
U+1CD3's existing alphanumeric treatment. U+00B7 and U+A92E are the other
punctuation Diacritic entries from Unicode 17 PropList.

The operational C3 comparison invokes actual stock `IsSameClass` for each BMP
unit against seven representative neighbors in both orders. Its 222 changed
characters are U+3300..3357, U+4DB6..4DBF, U+9FC4..9FFF and U+FA2E..FA6D.
Raw flag differences also include fullwidth bits that do not change the
selection algorithm's result.

For example, `a<U+061D>b`, `a<U+3300>b` and `a<U+4DB6>b` have new boundaries;
`a<U+166D>b` and `a<U+180E>b` become single words. The controls fixture checks
these explicitly and preserves the unmodified stock reference separately.
Valid supplementary pairs and isolated surrogate units have no consumed C3
flags in the observed native pair/single-unit calls. The portable path retains
UTF-16 code-unit classification and real TextBox insertion-position handling.

`Controls/WordSelectionReference.json` records stock command traces for both
flow directions. Regenerate it with StockOracle `--words`; the portable fixture
uses the stock traces for common cases and explicit Unicode 17 expectations
for the changed cases. Investigation and full-BMP diagnostics are under
`artifacts/portability/word-selection-investigation`; their NLS data is test
evidence and is not a runtime classification table.

StockPositionReference.json records actual stock WPF file version
10.0.1226.42308 and the pinned font SHA. Reproduce captures with StockOracle and
the main fixture's --capture-oracle option. Both also accept
--capture-boundaries for mandatory terminators, identical-properties grapheme
chunks and explicit LS context. Raw captures preserve source spans and full
bidi levels, including the differences listed above. Local evidence is under
artifacts/portability/text-stock-oracle and artifacts/portability/text-bidi.
Verify-UnicodeBidi.ps1 reproduces the full Unicode-level negative/positive
comparison. None of these records claims whole-framework, ClearType or native
Line Services parity.
