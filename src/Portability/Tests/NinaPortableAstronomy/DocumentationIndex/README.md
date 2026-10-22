# Real DocumentationIndex URI getters

This console calls all13 public getters on the supplied complete NINA.Core assembly. It creates no App, window or documentation file and opens no browser. It checks absolute file-URI identity, exact decoded native LocalPath and the unchanged Fragment.

Build with SDK10.0.401, `-p:ImportDirectoryBuildProps=false`, `-p:ImportDirectoryBuildTargets=false`, an owned `--artifacts-path` and `-p:NinaCorePath=<absolute original or derived Core DLL>`. The only runtime framework is Microsoft.NETCore.App10.0.12. The invoked utility path does not consume portable WPF or a native backend. Compile references may report the existing WindowsBase metadata version conflict when using the original Windows Core contract; the tested utility path resolves no WindowsBase type.

Run the DLL in a fresh process for each absolute application-directory argument. Use an ordinary path, spaces, literal `#`, literal `%20`, literal `%2520` and Unicode. The fixture sets the existing public CoreUtil.APPLICATIONDIRECTORY before the separately JITted getter method, then restores it. The getters cache their directories once per process, so do not combine roots in one invocation.

Also deploy the probe and its declared Core/dependencies beneath an actual directory containing spaces, `#` and `%20`, then run with `--assembly-directory`. This case uses the genuine Core assembly location without assigning the public field. Documentation files must remain absent; URI construction has no existence dependency.

The original Core produces13 UriFormatException results on Linux and13 passes for an ordinary root on Windows. The explicit absolute-Uri derivative passes13getters x7path cases x2OS,182 total. Keep raw JSONL, stderr and exit codes along with source/PE/PDB pins. A passing URI fixture does not qualify application startup or the rest of the framework.

`--image-patterns <original-NINA.Profile.dll> <expected-Core-SHA256>` exercises
public ImagePatterns against an actual default ImageFileSettings instance from
the hash-verified original Profile assembly. Build this route with
`OriginalManagedDirectory` for the existing CommunityToolkit.Mvvm dependency and
`FrameworkRuntimeDirectory` for the managed WPF assembly attributes used during
locale-resource lookup. Both optional inputs use normal project references and
copy-local behavior. No native runtime tree or assembly resolver is supplied.

The 15 checks cover actual default date/type folders, forward and backslash
templates, mixed and repeated separators, reversible image-type overrides,
formatting and trimming from the original test examples, native filename
sanitization and a custom literal-backslash Value added through the public API.
Windows colon/question-mark expectations remain Windows-specific. Template
normalization occurs before substitution, preserving a backslash in a substituted
Linux filename segment. The original test sources are unchanged.

The qualified old Core passed all 15 checks on Windows and failed seven path
checks on Linux. The template-normalized Core passes all 15 on both OSes. Its
existing 13 URI getters also pass on each OS with an ordinary absent documentation
root. This is a public model/settings regression without App startup or file-save
execution; it does not qualify application persistence or a saved image.
