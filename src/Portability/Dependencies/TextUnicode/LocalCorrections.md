# Local N0 correction

The original source remains unchanged. Its `BiDiAlgorithm.cs` SHA-256 is
`245D9E90D33FF619F0507993BA41CA2670EC278EE40152E4A04C6F311929AA5B`.
The maintained derivative preserves exactly two corrected assignments in
`SetPairedBracketDirection`, the loops processing original NSMs after an open
or close bracket. Both assignments change from
`_runOriginalClasses[i] = direction;` to
`_runResolvedClasses[i] = direction;`.

UAX #9 rule N0 assigns the bracket's resolved type to the following original
NSMs. Updating the original-class array instead both loses the resolution and
mutates caller input. Unicode 17 BidiCharacterTest original line 85,
`0061 0028 0062 0029 0331;1;1;2 2 2 2 2;0 1 2 3 4`, reproduces the error:
the final mark receives level 1 instead of 2.

An independent run of all 91,707 Unicode 17 BidiCharacterTest cases found nine
level failures and 23 original input mutations before the correction. With
these two assignments corrected it found zero level/paragraph-level failures
and zero input mutations. The bounded fixture includes the failing case.

The historical adaptation verified the original hash and required exactly two
matching assignments. The maintained source keeps its correction notice and
the SixLabors Apache-2.0 notice and terms. Verify-UnicodeBidi.ps1 compares the
unchanged original, historical two-assignment correction and maintained source
against all 91,707 official rows, including original input preservation.
