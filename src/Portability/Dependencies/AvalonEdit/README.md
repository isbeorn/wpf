# Pinned AvalonEdit component

This wrapper builds AvalonEdit 6.3.1.120 from upstream commit
862415d51eddc9eac93f462dbc522ffbf929cd52. Original/ retains all 254 selected
component inputs, including 214 C# files, six XAML pages, resources, signing key
and MIT notices. upstream.json records the original Git blobs and SHA-256 values.
The build rejects changed, missing or extra original files. Original/ disables
Git text conversion so checkout does not alter those inputs. The exact upstream
.gitattributes blob is archived as .gitattributes.upstream with its original Git
path recorded in upstream.json; it cannot override the byte-preserving checkout
policy. Compiled source and resource paths are unchanged.

Assembly name ICSharpCode.AvalonEdit, version 6.3.1.120 and public key token
9cc39be672370310 are preserved. The upstream key's public bytes were compared
with the official package. Public signing preserves identity, not publisher
authenticity. This is source-adapted component compatibility, not a claim that
the portable assembly is the unchanged official binary.

PortableAvalonEdit=true selects three generated source derivatives under obj.
Both original and generated hashes are checked. Original files remain untouched.
PortableAvalonEdit=false compiles only the original sources and native statements.

- ImeSupport accepts a genuine presentation source that is not HwndSource without
  attaching Windows native IME. Managed focus, selection and routed input remain
  active. Native IME is deferred.
- CaretLayer reads the existing Core startup profile's caret timing through one
  private reflection binding when the actual source is not HwndSource. A genuine
  HwndSource still uses Win32.CaretBlinkTime. Missing or invalid internal timing
  raises PlatformNotSupportedException. There is no guessed timing fallback.
- TextArea implements portable typing-related cursor hiding through WPF editor
  QueryCursor events, Cursors.None and Mouse.UpdateCursor. It preserves handled
  child queries when visible and overrides them while hidden. Mouse/touch events,
  focus loss and unload restore the query state. It does not overwrite Cursor
  properties or use global Mouse.OverrideCursor. This scope differs from Windows
  Forms' desktop cursor counter. Nonportable statements retain WinForms behavior.

The internal caret binding is deliberately limited to PresentationCore's
MS.Internal.Portability.Settings.PortableSystemSettingsProvider.Current and
CaretBlinkTimeMilliseconds. Profiles supply -1 or 0 for a steady visible caret
and validated positive intervals for blinking. The component does not introduce
a WPF public API, host dependency or profile-registration path.

Build with SDK 10.0.401 and explicit coherent inputs:

- AvalonEditFrameworkReferenceDirectory: the complete released WPF family.
- AvalonEditMarkupTaskAssembly: its source markup compiler.
- AvalonEditWindowsFormsReferenceDirectory: genuine WindowsDesktop10.0.12
  reference assemblies for compilation only. They are never copied to Linux.
- AvalonEditArtifactsDir: an isolated output directory ending with a separator.

The wrapper has no package or project dependencies and does not build the
framework transitively. Its default target files reuse repository markup and
reference tooling. Explicit AvalonEditWinFxTargets and
AvalonEditPortableReferencesTargets can select the matching frozen tooling.
NET analyzers are disabled for the unmodified external sources, matching the
other dependency wrappers; compiler warnings remain visible. MIT license and
upstream metadata accompany output under ThirdPartyNotices/AvalonEdit.

The prepared fixture uses original pinned NINA MarkdownStyle, MdXaml fenced code,
genuine measured AvalonEdit controls and original PluginsView BAML construction.
The current source-adapted component passed all 55 checks against the complete
nina-integration-1 Linux Stage: the unchanged 21-case control workflow, ten editor
controls and eight checks for each -1/0/80ms caret profile. The source-unmodified
Windows reference also passed 21 checks on genuine
WindowsDesktop10.0.12 in a private NONINPUT desktop. Separate genuine editable
controls passed insertion, selection, Undo/Redo and readonly pairs. Actual caret
rasters checked show/hide, focus away/return and positive blinking. Isolated
portable -1/0/80ms profiles passed their raster checks and the current 12-argument
profile projection directly observed the existing DispatcherTimer. The 80ms case
observed Interval=80ms and IsEnabled=true; both sentinel profiles stopped it.

Actual private XTest pointer entry, native XFixes cursor hide/show, handled child
queries, movement, focus loss, unload and reload passed against that complete
family, including the separately qualified hover settings. No MouseHoverLogic
suppression or individual framework DLL overlay was used. All five child runs
exited zero with empty stderr and verified private Xvfb cleanup. Compact evidence
is under artifacts/portability/current/richtext/nina-integration-1. These checks
do not qualify physical keyboard, native IME or live GNOME behavior.

The bounded control workflow and detached PluginsView BAML do not qualify full
PluginsView startup, catalog/network operations, plugin installation or whole
NINA application startup. Rich-document composite coverage remains a separate
gate. Those retain separate source and execution gates.

Original sources: https://github.com/icsharpcode/AvalonEdit/tree/862415d51eddc9eac93f462dbc522ffbf929cd52
