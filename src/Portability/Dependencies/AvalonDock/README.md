# Pinned AvalonDock component

This wrapper builds Dirkster.AvalonDock 4.74.1 from commit
028a5f328eb86a7abfb2195bbaae37683c818d1e. Original/ retains every component
source/resource plus the original license, shared version props and editor config.
upstream.json binds all 213 files to Git blobs and SHA-256 values. Original files
disable Git text conversion and the build rejects changed or extra inputs.

The component retains assembly name AvalonDock, version 4.74.1.0 and public key
token 3e4669d2f30244f4. The upstream key's public bytes were compared with the
official 4.74.1 package. Public signing preserves identity; it does not assert
publisher authenticity. WPF's Microsoft key/version policy is not imported.

All 164 original C# files, genuine generic XAML, 18 resx files and 24 PNGs are
included. Markup uses the existing source PresentationBuildTasks tooling. The
wrapper does not import upstream's six-target framework list or floating analyzer
package versions. SDK NET analyzers are disabled, matching the portable framework
wrappers, rather than rewriting upstream source to satisfy WPF repository rules.
Compiler warnings remain visible. The Ms-PL license and upstream manifest are
copied to ThirdPartyNotices/AvalonDock.

The unchanged component builds with SDK 10.0.401 on
Windows and Linux, with 663 CLS-related warnings and zero errors per OS against
the selected source-built WPF references. Original resource keys and signing
identity are inspected separately. The first root-cwd SDK12/analyzer attempt is
preserved as an unqualified tooling failure.

This wrapper explicitly selects PortableAvalonDock=true. Set it to false to
compile every original source file without adaptation. The portable selection
uses Patches/ and PortablePatches.props to generate derivatives under obj; both
the original and resulting SHA-256 values are checked. Generated text uses LF
and a final newline. Original/ is never modified. Ordinary upstream Windows
code remains in the guarded branches and the false selection compiles only the
untouched sources.

The first two patches retain managed PreviewGotKeyboardFocus tracking and
Keyboard.Focus restoration without installing a Windows child-HWND hook. They
balance the Application.Exit subscription when the last manager unloads. Unused
native auto-hide and overlay presentation are not constructed during Loaded.
An actual request for either unimplemented presentation path raises a named
PlatformNotSupportedException.

Seven selected source files now also prepare the first managed floating path:
the real floating Window contains a Border/AdornerDecorator/ContentPresenter,
content-root accessors use that tree, managed activation/location/size events
update the original model and Window.Owner replaces only the selected Windows
ownership calls. Public HwndHost-derived classes remain in the complete source
and the original path is preserved. Pointer requests fail before model mutation;
programmatic Float/Dock remains the intended first runtime slice. With the eighth WindowChrome patch, this presenter increment passes the nine
programmatic original-view checks on the root-released ordinary Release family.
The immutable earlier rendering/native-chrome failures remain evidence of the
prerequisites that were reached.

The untouched official binary passes nine programmatic private-desktop stock
checks with the original ImageView and GuiderGraph, including Float/Dock for
both panes, resize in both directions and a docked XML round trip. On the
released portable closure it fails in genuine DockingManager.Loaded at
WindowHookHandler.Attach/GetCurrentThreadId. The first adapted run passes that
hook boundary, then the genuine view reaches the renderer's unsupported dashed
pen path. This rendering prerequisite is retained. The later full-family Release replay passes both panes' Float/resize/Dock,
docked XML restoration and lifetime cleanup. Settled container/item/content and
achieved native/model geometry checks pass. The Debug assertion is retained as a
documented transient event-ordering boundary, not suppressed or repaired.

Normal builds reference the source WPF projects and compiler. For a coherent
frozen dependency intake, AvalonDockFrameworkReferenceDirectory selects the
six genuine WPF assemblies and AvalonDockMarkupTaskAssembly selects an existing
compiler assembly. These paths are build inputs and must be hash-recorded with
the producer source closure. AvalonDockArtifactsDir isolates outputs per graph.

The approved design requires a real two-LayoutAnchorable NINA fixture, untouched
binary Loaded red and stock reference before any behavior adaptation. Window
ShowInTaskbar=false is separately owned. Pointer overlays/ordering, interactive
auto-hide, document panes, full Overview and shell behavior remain later gates.
Programmatic Float/Dock and XML restoration do not qualify pointer docking.

Original notices and Ms-PL remain applicable to this source-derived component.
No original Windows path or public HwndHost-derived type has been replaced.

The portable WindowChrome derivative retains the original XAML type, attached
property values and generic resources. It does not bind Windows shell metrics or
install WindowChromeWorker. The existing portable Window owns native decoration
and its lifetime; Windows glass, custom nonclient hit testing and system-menu
semantics are not reproduced. Both original paths remain selected when
PortableAvalonDock=false.
