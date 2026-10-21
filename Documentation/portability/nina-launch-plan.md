# NINA launch priority, 21 October 2026

The user resumed work and prioritized NINA's required components and a usable
Linux x64 launch. General WPF capabilities that NINA does not currently use,
further accessibility qualification and native IME are later work. The earlier
ordering that deferred NINA.Image until all WPF gates finished is superseded.
Everything remains local on `codex/nina-cross-platform`.

The implementation keeps real WPF controls and NINA application logic, with an
owned Linux layer supplying the operating-system services underneath. The
component overview below distinguishes that project code from retained libraries.

| Component | What has been built | Retained libraries and services |
| --- | --- | --- |
| Framework and XAML | Original WPF property system, bindings, commands, events, layout, controls and BAML compiler, adapted for portable builds. | .NET 10, WindowsBase, PresentationCore, PresentationFramework, System.Xaml and PresentationBuildTasks. |
| Linux windows and dispatcher | Owned NativeHost creates windows and connects Linux events to WPF's existing dispatcher and window lifecycle. | X11 and GLib; GNOME Wayland currently uses XWayland. |
| Keyboard, mouse and cursors | Owned adapters feed focus, capture, pointer and keyboard events into real WPF input routing. | XInput2, XKB, libxkbcommon, XFixes and Xcursor. |
| Drawing and effects | Owned renderer translates WPF geometry, brushes, clipping, images and selected effects into CPU-rendered surfaces. | SkiaSharp 3.119.4 and native Skia. |
| Text and Unicode | Owned text integration provides shaping, fonts, wrapping, caret and selection behavior for the covered controls. | HarfBuzzSharp 8.3.1.5, native HarfBuzz, Skia font services, Fontconfig and maintained Unicode 17 algorithms/data. |
| Bitmap storage and codecs | Owned buffers retain Gray16/RGB48 scientific precision; display conversion stays separate from raw samples. | Skia codecs decode PNG/JPEG assets. |
| Documents and supporting controls | Managed document layout supports covered Markdown content; original editor, chart and specialist controls run through portable WPF. | AvalonEdit, MdXaml, OxyPlot, Extended WPF Toolkit and Microsoft.Xaml.Behaviors. |
| Docking | Adapted window hosting retains NINA's original docking model, controls, templates and layout serialization. | Dirkster.AvalonDock. |
| Desktop integration | Owned clipboard, dialog and accessibility adapters expose the existing WPF-facing contracts. | X11 clipboard ownership, GIO/D-Bus desktop portals and AT-SPI. |
| NINA science and files | Original view models, mediators, profiles and simulator use owned image buffers and focused platform adaptations. | Accord processing kernels, SOFA, NOVAS, SQLite and CFITSIO 4.7.0. |
| Distribution | Runtime and compiler packages support ordinary external consumers and compile NINA's original App/MainWindow markup. | Wpf.Portable.Runtime and Wpf.Portable.Sdk. |

There is no Avalonia runtime, framework, build or source-import dependency. The
34 attributed Unicode algorithm/data derivatives are directly maintained source.
AvalonDock and AvalonEdit are separate control projects that NINA already uses.

This is an implementation inventory, not a claim that every API or component
workflow is qualified. Real NINA has launched and passed native sequence
workflows. The approved Accord metadata adaptation passes the component image
regressions and the actual simulator Snapshot saves a valid FITS file. Rendering
initially stopped at an unsupported ImageBrush. That renderer gap is now fixed,
passes the affected component tests and is included in newly qualified packages.
The successor NINA application has captured and displayed useful simulator
images, shown finite statistics and saved valid FITS files. The focused raw-load
repair also passes original thumbnail reload, annotation on/off preparation and
recovery capture. Sustained operation and the remaining desktop gates stay open.
Native IME, broader rich-text editing/printing, real hardware, ARM64 and macOS
remain unqualified or deferred. Detailed evidence and remaining gates follow.

## Active checkpoint

The original NINA App/MainWindow has run visibly on GNOME/XWayland through the
qualified portable SDK/runtime packages. Native sequence editing, completion,
cancellation, reset, restart and palette drag/drop/Escape pass in that packaged
application. Floating caption/redocking remains under diagnosis. The Image/Accord
repairs pass the component package graph's complete image regression without
GdiPlus. The earlier 317-file application saved a valid FITS through the original
Snapshot and worker path, then exposed unsupported nonempty ImageBrush rendering.
The fix is committed as `97c1da741` and passes 400 affected component checks.
Fresh ordinary SDK/runtime packages pass 250 consumer checks plus six
package-derived ImageBrush checks. The new coherent 317-file NINA publication
has opened visibly on GNOME/XWayland. No Snapshot was invoked during its first
lifetime. The user's subsequent navigation exposed another unsupported brush:
LinearGradientBrush through DrawRoundedRectangle. The application closed
normally and the user's final profile/layout are preserved. The gradient repair
is now included in the final4 packages and the next actual NINA lifetime has
passed capture, useful image display, finite statistics and FITS save. The Image
successor also passes original UI readback, annotation on/off preparation and
recovery capture as recorded below. Earlier capture and Framing passes retain
their own runtime scope.

### Final4 package and actual imaging checkpoint

Commit `49107f3e` adds the bounded sRGB/Pad linear-gradient fill implementation.
The ordinary package producer finishes with 27 warnings and no errors. Root
verified all 18 produced PE/PDB pairs, 3,929 physical source hashes and 360
embedded source records against their sealed PDB proofs. The released runtime
and SDK hashes are respectively
`3E082D4AE88FACD7D67F96ACD949A1DDCE8EE15B6AA17CF96EDE6F9F58202165`
and `059BB27FAB940EF2801CDF240077D4D516EA9EBEE0C3B022045FCD821BE3695B`.
The producer handoff is
`current/final-distribution-20261021-4-proof/handoff.json`, SHA-256
`0E5035C9C5E704CD54ECB1ECA41C19ED22ED7EAFD8693C454EFA9B92262526A7`.

Affected package consumers pass 200 checks on Windows, 200 on Ubuntu and 14 in
the ordinary Ubuntu SDK/native framework-dependent application. All actual test
processes exit zero with empty stderr. This is a targeted 414-check regression;
it does not repeat the preceding family's full consumer corpus or self-contained
run. The wrapper's immediate global Xvfb cleanup equality assertion failed and
its before-list was not retained. A later read-only observation found no Xvfb
process, but does not establish retrospective equality or the exact owned
display. That orchestration gap remains recorded without a native retry in
`current/final-distribution-20261021-4-consumer-proof/handoff.json`, SHA-256
`24C1FB68F22B60EEFFE1268ADDC5B0E40FFCD42ED7A8695B3278715150A6C2F5`.
Root verified its 271 evidence members and 91 entry source documents.

The coherent 317-file NINA publication uses that released PresentationCore,
SHA-256 `77AC91BB6953DD295ECCE9D22334232FB90464738FCC8E2D3EBD920C72CB9747`,
through ordinary package restore and publish. The existing App/BAML, Image and
Accord components remain unchanged. Root verified every installed member. Its
handoff is `current/nina/vm-package-gradient-20261021-1/HANDOFF.md`, SHA-256
`21DFD845DFD28F5D9B7E77B08B409B7D1E7D8F45ECD13080C29C361C76850C4A`.

With diagnostics off, the actual original Snapshot command captures a one-second
640x480 RANDOM simulator frame with Save enabled and Loop disabled. NINA displays
the noise image and reports mean 4999.52, standard deviation 99.89, median 4999
and range 4494-5440. Its zero-star/HFR 0.00 result is an ordinary no-star state,
not a quality-metric failure. The original save worker writes a 619,200-byte FITS,
SHA-256 `9B8639B75BF127130DEC7FA035671C3B2BAF457C3E768BFE2BF130A7A534D6C1`.
Independent readback of all 307,200 samples confirms those dimensions, range and
mean, with BITPIX 16 and BZERO 32768. No application error occurs before reload.

Single-clicking the original Image History thumbnail reaches
`Thumbnail.LoadOriginalImage`, then `ImageDataFactory.CreateFromFile`. That
factory constructs `LibRawConverter` before `BaseImageData.FromFile` dispatches
the FITS extension. Its constructor tries to load the absent Windows
`External/x64/Libraw/libraw_0_22_1.dll` and reload fails. The prior displayed image
remaining visible does not qualify readback. Annotation, recovery capture and
the resource pilot have not run. Root verified all 18 files in
`current/nina/vm-package-gradient-20261021-1/functional-first-fault-evidence.json`;
the fault receipt is `functional-first-fault.json`, SHA-256
`08FFDB933107BCBC07FBB72FC7B2B57B68793A7024C29DC8A9273EF9B7819045`.
The panes were restored through their original Info toggles and the application
closed normally with exit zero and empty stderr. Tab order followed the original
hide/show operations, so an exact layout-byte roundtrip is not claimed. The next
restart state is profile `77EF4D08...43DDC37` and dock `4F90777A...9E280FA`,
preserved by the same lane's `CLOSE-HANDOFF.md`.

The focused repair now keeps the original Windows constructor preload and moves
non-Windows native loading into `LibRawConverter.Convert`, after cancellation
and before copying or pinning the raw bytes. No factory/public API, format
dispatch or scientific algorithm changes. The four genuine factory tests fail
with the old Linux Image component and pass with the repair. Final Linux checks
also pass package science 27, annotation 5, FITS 13, legacy 1 and full-reference
science 25. Windows original/candidate science 25, annotation 5 and legacy 1 pass;
the corrected factory 4 passes with both the previous source-built component and
the candidate. Its Windows FITS case observes dispatch to missing CFITSIO, not a
successful native readback. Initial Windows fixture assumptions about failed
preload throwing were corrected against the original loader and their failed
reports remain retained.

The new Image DLL is
`F24F144FA5DD3CF20B3B9A57B14472B4BD7B5492A4A158A15AA0F0B9E766DF03`,
PDB `19C2EB077D18198A85A1616AA6CCEE5CAB0BD3DE5FBC809DBE153C30ADBF1EC7`.
Root verified all 72 resolved source-document hashes, the final five-file freeze,
11 handoff pins and eight Windows result reports. All other 73 original source
members match the preceding component and the 74 upstream inputs remain pinned.
The handoff is `current/nina/fits-factory-reload-20261021-1/final-handoff.json`,
SHA-256 `828FA3859B7816BC666EDD48E460142ED87F5535ECBB6FDCE7F9EA2775FCEA4C`.
Successful raw decoding and native Windows CFITSIO are outside this repair's
qualification.

### Actual App replay after the raw-load repair

The coherent successor retains the same 317 paths and changes only the Image
DLL/PDB pair. All other 315 members, including WPF, App/BAML, generated dependency
files, Accord and native assets remain byte-identical. Root rehashed every
member and its 16 publication evidence pins. The publication manifest is
`AEEA0374848D602EA45DC5CC2F3ADD5AFEA9DFF1E3842E5B24DE827BA9BF411D`;
the local handoff is `current/nina/vm-package-fits-factory-20261021-1/HANDOFF.md`,
SHA-256 `E42E78777F612E184F0D1A9616DB3DDA67C812CC65C90599FE24C1C3C4DA292B`.

On the actual GNOME/XWayland desktop with diagnostics off, the original one-second
Save-on/Loop-off Snapshot displays a useful 640x480 noise image and saves FITS
`310E659F04AECB044CF5B197A3D53002B0BB4F4588CAD440B424982231C1130E`.
Independent decoding verifies all 307,200 ushort samples and header semantics.
The original Image History thumbnail then reaches `LoadOriginalImage` and
`PrepareImage` successfully, without the former LibRaw error. The file is not an
injected history item; each fresh session starts with empty original history.

The original Options control persists AnnotateImage=true. A subsequent genuine
DetectStars-on Snapshot completes preparation/display and saving with that value.
The same control restores false and a third Snapshot completes recovery. All
three original save-worker successes are present and ordinary stderr is empty.
These RANDOM frames have zero detected stars; actual colored marks remain
separately qualified by the component annotation tests. Root inspected the
reloaded image and recovery statistics pixels, and verified all 24 native
functional evidence files. No error or FATAL appears in the complete 16,966-byte
functional log. Its SHA-256 is
`88B497FD5F972EB1679C75BFA96A525FBD5FB16C1165594C6E9DA44EB9E37963`.

The actual functional handoff is
`current/nina/vm-package-fits-factory-20261021-1/native-functional-handoff.json`,
SHA-256 `AF51FE714055D2156E06497BA23BC695631D7F8F5BFAFF082918519EFFE0CC7F`.
The application remains open and passive with its temporary panes restored,
Save=true, Loop=false and annotation=false. The 25-minute resource pilot is
prepared but unexecuted pending the extended desktop-idle window. This pass does
not close caption/redocking, focus restoration, actual pickers/clipboard, Xorg,
installed diagnostics, allocation or overnight gates. It is the actual 640x480
simulator workflow, not a 24-megapixel or sustained 1-Hz application result.

### Keyboard focus restoration

The isolated caption probe showed that native activation, Window.IsActive and
WPF's active keyboard source could all switch successfully while
Keyboard.FocusedElement stayed null. FocusManager still remembered the live
TextBox, but the next genuine key reached neither that TextBox nor the window's
PreviewKeyDown route. The same failure reproduces without custom chrome.

The repair stays in PresentationCore. Each portable presentation source captures
Keyboard.DefaultRestoreFocusMode at construction, before its lazy provider is
created. External native activation restores the existing validated logical
focus only while that source still owns native and WPF keyboard input. Explicit
focus requests retain precedence, including during nested acquisition, and
callback-selected targets are rechecked before restoration. No Host, chrome or
public API changes are needed.

The final source freeze is `source-ready-v4.json`, SHA-256
`77AC510EA4A31B082EAE2A52FF07FE2F7DBF593711E7B91CDE646DC2BEA730C6`.
The ordinary Core passes 19 focused native checks, 28 broader input checks and
11 anchored-popup neighbors on private Xvfb, with Openbox for caption checks.
Existing headless input 36
and presentation-source lifetime 29 pass on each OS using a separate test-friend
Core that must not enter a release. All processes exit zero with empty stderr;
owned display/process/socket/lock cleanup passes. Root independently verified
the final reports, 28 handoff pins, normal Core's 1,428 source documents and the
final native entry's 25 source documents. Both independent reviews are clear.
The handoff is `current/caption-activation-20261021-1/final-handoff.json`, SHA-256
`C01916804F674C40D1431451D9849BD5251336B616053045B80502E91C3FE7CD`.

Earlier fixture failures are retained: Windows foreign-source construction
needed its existing thread to be STA, a witness needed native/source activation
before calculating its click coordinates and compose selection needed actual
mouse-release delivery. These are setup corrections, not additional production
repairs. The separate GNOME/XWayland synthetic-input activation observation
remains unresolved; the actual NINA focus/caption replay is still required.

The ordinary final5 producer uses committed source `71e9f0f8f`, finishes with
27 warnings and no errors and preserves all 10,855 protected records. Runtime
and SDK hashes are respectively
`E4D90962B6187FBB42D5CF3EE68D3ED6693EEDE70B8BC843AD7652DB626FACB5`
and `136A08A838A8C2218EC19A5E128A222092F82F31AE8B45B75BA909AABFF95B6D`.
Root verified all 199 immutable package files, 18 PE/PDB modules and 4,289 source
record bindings. Its handoff is
`current/final-distribution-20261021-5-proof/handoff.json`, SHA-256
`6A57A12E15310587B8501AC8C9D762614EA8DA78C86F1DB158263CD9841B1746`.

Package-derived execution passes ordinary SDK FDD 14, focus 19, input 28 and
popup 11 checks, all with zero exit codes, empty test stderr and exact owned
process/display cleanup. Root verified 85 evidence pins and all 42 consumer
source documents. Normal package Core is `1E85B20C...75C9A90`; neither author nor
test-friend Core is promoted. Earlier setup and loader failures remain retained:
an optional metadata file was absent, Xvfb exposed an abstract socket, the leaf
runner omitted the RID native search path and its cleanup comparison incorrectly
treated mutable process context as identity. The corrected runs preserve their
actual scope without rewriting the failures. Consumer handoff is
`current/final-distribution-20261021-5-consumer-proof/handoff.json`, SHA-256
`0A218B256FFCA48765EFB44149FEA86864B946388C13D762F2D02C304C6613F3`.

The fresh NINA publication retains its App/BAML, Image, Accord and native science
components. Compared with the preceding 317-file publication, it adds two notice
paths, changes 18 normal WPF/dependency/provenance paths and preserves 299 members
byte-for-byte. Root verified all 319 files and 20 publication evidence pins.
Its manifest is `84200BFEE8468FD122C3F78283709A68BBA214920E3A2A51D819D8B922588A68`.
The whole publication is staged at
`/home/stefan/wpf-portability-local/phase5/nina-shell-package-final5-20261021-1/app`.
Native dependencies resolve and the existing App and six protected state/data
files remain unchanged. Staging handoff is
`current/nina/vm-package-final5-20261021-1/staging-handoff.json`, SHA-256
`A19BCCD75BB11D4E95FED95CD857422D21D258D313C72C96B862285E61AD8D4A`.
The staged fixture profile `72D23CA3...EE729` and SQLite `4DD072` remain immutable
publication seeds; no dock file is included. After the user released a new
35-minute idle window, the preceding application closed normally at 09:46:31 UTC
with exit zero and empty stderr. Its exact PID and supervisor were absent before
copying the closed live profile `3710235A...ACBF6`, dock `0D2EBFEC...D78B2` and
settings into the final5 installation. Historical `77EF4D08/4F90777A` remains
separate from this actual carry.

Final5 runs as PID 562971, start ticks 6408255 and supervisor 562969 on the same
GNOME/XWayland desktop. Its loaded App, Image, Accord and normal package Core
match the selected publication; all 317 immutable installed members remain
unchanged. Original one-second Snapshot commands display two useful 640x480
images and save two 619,200-byte FITS files, `F68D5E35...4962F` and
`43706851...D3708`. The original thumbnail invokes `LoadOriginalImage` on the
first saved file without a subsequent logged error. Initial thumbnail clicks
hit overlapping floating panes; original Info controls uncover the thumbnail
and restore the panes afterwards. These missed clicks and other driver setup
failures remain retained separately from application results.

Root verified all 29 files in the native functional evidence manifest and
inspected both completed image captures. The handoff is
`current/nina/vm-package-final5-20261021-1/native-functional-handoff.json`, SHA-256
`F27963FF136A76E31E6B20DE55A8659425C065DA5BCD3BF9EBB67CF5B353A62D`.
Annotation and caption interaction were not repeated in this lifetime. An
inherited identity-receipt label mentioning annotation is explicitly corrected
by `identity-scope-correction.json`, SHA-256
`AF71F69D45D71F24BBC9D13EBBA690CFB14C1342F6C92B8E516C40261421C1F3`.

The actual resource pilot measures five image-loaded idle minutes, fifteen
minutes of original 640x480 RANDOM one-second Snapshot Loop and five minutes
after original Cancel/drain. Save stays off, AutoStretch/DetectStars stays on
and sensitivity/noise reduction stays High/None. All 53 thirty-second samples,
three terminal records, 106 identity checks and 53 smaps reads pass. All three
owned samplers exit zero with empty stderr and are reaped and absent. Root
independently checked every raw row and all 71 handoff evidence pins.

| Sampled measure | Initial idle | Active capture | End of recovery |
| --- | --- | --- | --- |
| RSS | 554.4 MiB median | 557.3-646.0 MiB range | 626.7 MiB |
| Threads | 18 | 21-24 | 19 |
| File descriptors | 325 | 325-327 | 327 |

The five three-minute active RSS medians cycle rather than rise monotonically.
Recovery ends 75,821,056 bytes above the initial idle median and rises 1,396,736
bytes during recovery itself. The retained memory and two additional file
descriptors remain unexplained follow-up observations. This establishes neither
a leak nor an acceptable overnight ceiling. The sampler does not attribute
allocations or inventory descriptor targets, and no extra GC was injected.

Actual midpoint and end images have changing finite statistics, with no logged
ERROR/FATAL or stderr. After the full recovery measurement, one original
Save-off/Loop-off Snapshot displays a new image with mean 4999.41 and standard
deviation 100.14. Both original FITS hashes and all 317 immutable installed
members remain unchanged. All UI/guest observation ends around 10:20:36 UTC,
inside the released window, with NINA left passive. Root inspected the images
and verified all 34 UI evidence files. This does not qualify histogram refresh,
caption/redocking, overnight operation, native allocation totals or 24MP App
throughput. The pilot receipts' histogram "placeholder" description overstates
the observation: original `HistogramView.xaml` always draws the center icon,
independently of its AreaSeries. The visible left-hand peak matches the original
raw 16-bit histogram's bins 6-8 for these samples. No missing-renderer defect is
established; exact asynchronous refresh timing remains unmeasured.

Sampler handoff is
`current/nina/overnight-preparation-20261021-1/pilot-final5-1/handoff.json`, SHA-256
`ED0AC599844589B759F33BB2B9EC2D42FD2038C986BDAFDD60B8F822F27C3A2E`.
The separate UI handoff is
`current/nina/vm-package-final5-20261021-1/PILOT-UI-HANDOFF.json`, SHA-256
`A847A2D5B757913ED6DD9B35BE1E17B6E39B31F7C7A047CFE35DFD604015C519`.
Combined App/publication evidence occupies 400,321,832 bytes, below its 400 MiB
cap; sampler reports stay below their separate 8 MiB combined cap. The first
local date-cast setup failure occurred before SSH or sampling and remains
retained with the narrow wrapper correction. Earlier pilot packets remain
unchanged and unexecuted.

Commit `b6b30ebc6` also includes AvalonDock's existing license and provenance
receipt in generated NINA build output and normal publication. Disposable SDK
copy targets verify both files byte-for-byte at both destinations, with all six
original App item sets unchanged. Existing publications remain frozen; the next
application publication must include this pair. Current runtime/SDK notices,
graphics/text notices and adapted Accord source material are present. A complete
outward NINA corresponding-source distribution remains separate from the
retained local source/build receipts and is not claimed here.

### Earlier targeted repairs and application runs

The targetless sequence ContextMenu opening is repaired in commit `0b12eb772`. The original null
PlacementTarget remains unchanged; an eligible active keyboard source supplies
the portable native owner. The genuine WindowsDesktop 10.0.12 reference passes,
including dismissal and capture release on window close. All 56 affected Linux
native checks pass at their declared 96/192-DPI settings with empty stderr and
complete cleanup. Both production reviews and the focused fixture-successor
review are clear. The initial fixture readiness and cleanup failures remain
preserved separately; no production change was needed after the first build.
The released ordinary runtime has 120 files, manifest
`871EBFBA4E372033847EBD6A1694E3D950DFBD703702853E5245744138671E06`,
and intake `current/nina-unanchored-context-menu-integration-20261021-1/consumer-intake.json`,
SHA-256 `D079C0F1B72918D1CA13B8ED89CB75A5F5B15C792FD1A651786997082F446228`.
Root rehashed all members and 24 nested intake pins. Only PF, NativeHost and the
Stage wrapper DLL/PDB pairs changed from 9A; Core was reused unchanged.
The new whole-App composition includes the filename-corrected Core, FITS Image
and diagnostics-capable entry: 334 files and 685,834,090 bytes. Its manifest is
`7DB92138CB02618A5B67B893C8D9EC4B07E3C098F780A4B027E9C7302EF88C4A`
and composition receipt is
`45F0E2B62BFC277AF52F2FA118E19393F44BB4BADD82CD5537F4F2F30973CD4C`,
under `current/nina/vm-sequence-fits-ui-20261021-1`. Root rehashed every member.
The whole guest publication and native linkage verify, and the original App
has launched once on GNOME/XWayland with diagnostics off and the saved state.
Its actual Add menu now opens without the former animation/popup exception;
root inspected startup and menu pixels. The original menu has inserted a real
Sequential Instruction Set. Native controls added and edited two annotations and
a one-second wait, duplicated/deleted a spare item and moved an item down and
back up. The original Play command completed all three items in order. A later
60-second wait was observed running, then stopped before the final annotation.
Reset/restart then exposed a new first fault: after Wait1 finished, its status
notification contended on the weak-event read lock. The private
`ReaderWriterLockWrapper.NonPumpingSynchronizationContext.Wait` entered
`kernel32.dll` and threw DllNotFoundException on Linux. NINA's original
ContinueOnError ran the final annotation, leaving the wait failed; this is not a
successful restart. Further UI input is held while the raw exception and state
are preserved. Genuine custom drag and saved capture remain pending. Raw
observations are in the same run directory, including
`sequence-first-run-observe.json` and `sequence-cancel-observe.json`.
The restart record is `sequence-restart-observe.json`; its filtered log omits
some exception frames. The complete `sequence-restart-original.log` is retained,
SHA-256 `F69710939B3BC0819F9F4D9AC11A4E78DF94CB8BA349244201CE331F193FE074`.

The contended wait now uses the CLR wait helper on non-Windows portable builds.
The four-line guard leaves the original Windows wait and lock/context ownership
unchanged. Both fresh weak-event modes reproduce the old failure through real
PropertyChangedEventManager registration and worker notification. The new
ordinary WindowsBase builds pass with no warnings or errors. On each OS, both
focused modes pass four checks; the full dispatcher suite passes 17 on Windows
and 16 on Linux, with one Windows-STA row not applicable on Linux. All six
processes exit zero with empty stderr. Both independent reviews are clear.
The sealed successor is
`current/nina-weak-event-wait-integration-20261021-1/handoff.json`, SHA-256
`4053C6AEBE25E14478693A4FE39720B50528C194475BBA1BF6193EE88907141B`.
Root rehashed its 24 receipt pins and both producer DLL/PDB pairs. These remain
targeted WindowsBase results; the separately observed actual application replay
follows.

The fresh package-built application now passes the original native sequence
workflow on GNOME/XWayland. Its Add menus inserted a Sequential Instruction Set,
two annotations and Wait1. Editing, spare duplicate/remove and paired move
down/up worked. The first run completed all three items in order. A RUNNING
60-second wait was stopped, with a positive cancellation line and no trailing
annotation in that invocation. Resetting to Wait1 and restarting completed all
three items again, without the former kernel32 exception. Individual statuses,
the original log's nested Items[FINISHED]:3 and captured pixels agree. Ordinary
stderr is empty and all 304 immutable members remain exact. No camera capture
or saving occurred during this milestone.

The actual replay uses the 306-file package publication described below. Its
receipt is `current/nina/vm-package-sequence-fits-20261021-1/SEQUENCE-GREEN.md`,
SHA-256 `A70C474624D0CC2E6270F4460361EA94262DB62DB0AFD507AE3182B0F9C6A7F5`.
Root rehashed all 14 evidence pins and inspected the unfiltered log's separate
completion, cancellation and restart ranges.

The same lifetime then passed the original palette drag and Escape paths. A
drop into the last instruction's lower insertion band displayed the original
adorner and inserted a fourth Annotation. A second held drag followed by native
Escape cleared the clone and Radius10 blur without adding another item. The
blank inserted item was removed through the original Detach command. The first
attempt had targeted blank space below the insertion band; its clean rejection
is retained as a driver correction, not a successful drop or product failure.
Root inspected the held/drop/Escape screenshots, input receipts and health
receipt. All 304 immutable members remained unchanged, stderr was empty and
there were no application errors at that milestone. The receipt is
`current/nina/vm-package-sequence-fits-20261021-1/PALETTE-GREEN.md`, SHA-256
`AC2D966B9324C688424A2FF59BD370246B12CB7C037CC9F08926753374F99A18`.
Float/redock and actual FITS save/readback remain separate pending workflows.

The current package graph then exposed a missing `System.Private.Windows.GdiPlus`
dependency in `StarDetection.Detect`. The intended Save-on snapshot click had
actually invoked LiveView: enabling Save adds a Target row and moves the
buttons. The unchanged input receipt and a separate correction preserve that
driver error. The full original exception reaches the real DetectStars pipeline;
no single Snapshot, saved file or UI reload is claimed. The mapped Image PE is
the exact selected `9C5EA87B...303FC8A` successor. Its shared async method still
declares a Windows Bitmap before the Linux branch. Earlier component science
tests resolved GdiPlus from the fuller historical app graph, which concealed
this dependency. A fresh regression now uses the current package graph.

LiveView had ended after that fault. The application then closed normally,
disconnected SimulatorCamera and completed plugin teardown, with exit zero,
empty stderr and all 304 immutable members unchanged. Root rehashed all 15
evidence records in
`current/nina/vm-package-sequence-fits-20261021-1/liveview-first-fault-evidence.json`.
The fault/close handoff is `LIVEVIEW-FIRST-FAULT.md`, SHA-256
`ED99C1090954AA0EAE9CEA8A1DAC062ED1C3DBC3CA8AFC1D19F881E9A63DADB5`.
The next normal-close profile baseline is
`3C3B32282CC4E1B85CD498E143C67220C1DE42760F031CA9005F4CC11A040926`
and dock layout is
`1EE33B4B51E0EF7E2388BB7D4EBCCE1718563A865900ABCE672D22F2C9D1307E`.
The absolute Framing cache remains unchanged. Preserve this current state for
the next coherent Image successor rather than restoring the older seed.

The focused Image helper candidate isolates Windows Bitmap types behind three
private noninlined methods for detection preparation, RGB48 initialization and
debayer. Both independent reviews are clear. All 72 producing source documents
match the candidate PE/PDB. Original and adapted Windows each pass science25,
annotation5 and deterministic legacy1; Linux passes annotation5 and FITS13.
The latter required a physical CFITSIO copy at the fixture's declared native
path after its strict module-path check rejected a symlink. That failed
orchestration attempt is retained separately.

This is a partial repair, not a released Image successor. The genuine package
regression improves from 4/27 to 13/27; 14 cases now reach Accord's convolution,
median and pixel-size methods before requesting GdiPlus. The candidate Image is
`0394CC2D08577FD2117C38793FE1FA35881CA1F8E3CF0CD74D03D99147681935`.
Its component compile references use the retained coherent 120-file family,
while package execution uses the unchanged current 306-file graph. A bounded
Accord metadata-query source adaptation is now qualified separately below. This
Image-only intermediate was not released to the actual App.

The user approved the focused design below on 21 October 2026. It promotes the existing
`Accord.Imaging` dependency to an explicitly source-adapted component. It retains
the pinned NINA source revision, original netstandard2.0 target, C# 7.3 settings,
assembly identity and public API. The initial proposal listed seven metadata
expressions in six files. The unchanged Canny path then exposed one more
pixel-size query in `Drawing.Line`; it uses the same metadata ownership rule.
The implementation changes eight expressions in seven files, with numerical
loops untouched:

| Existing source | Proposed change |
| --- | --- |
| `AForge.Imaging/Image.cs` | Implement its existing PixelFormat bit-size helper as `((int)format >> 8) & 255`; have its byte-size helper divide that result by 8. |
| `AForge.Imaging/UnmanagedImage.cs` | Route `PixelSize` through the existing byte-size helper. |
| `AForge.Imaging/Filters/Convolution/Convolution.cs` | Route its reached metadata query explicitly through `Accord.Imaging.Image`. |
| `AForge.Imaging/Filters/Smooting/Median.cs` | Route its reached metadata query through that same owner. |
| `Blob Processing/BlobCounter.cs` | Route the query in the reached `BuildObjectsMap` method through that owner. |
| `Blob Processing/BlobCounterBase.cs` | Route the query in the reached `CollectObjectsInfo` method through that owner. |
| `AForge.Imaging/Drawing.cs` | Route the reached `Line(UnmanagedImage, ...)` pixel-size query through the same owner; preserve its format checks, clipping and pointer drawing. |

The actual packaged System.Drawing method's IL establishes the formula above.
Stock Accord's Convolution and Median call that Windows type directly, despite
the similar existing Accord helper name. The two blob methods retain the same
dependency in their color branches even when processing Gray8. Other methods
and their platform limits stay outside this proposal.

The original Accord PE is `BE51CE9A...A018F`, version 3.5.3.0, with no public key
or manifest resources. Its matching PDB verifies all 288 pinned C# sources and
two generated documents. The proposed offline SDK 10.0.401 build preserves its
evaluated source membership, original compilation symbols and four Accord
companion PEs. It reuses the original System.Drawing compile contract and the
already available NETStandard.Library 2.0.3 reference package. Its intake must
record exact sources, changes, compiler references and matching PE/PDB checksums.
Preserve applicable LGPL notices, mark modified files and retain corresponding
source plus build instructions with any distributable successor.

Acceptance requires stock Windows calibration of named, undefined, negative,
flag-only and sub-byte PixelFormat values, complete exported API comparison,
exact existing scientific pixels/results, package27, science25, annotation5,
FITS13 and legacy1. Package operations must succeed without deploying or loading
GdiPlus. Existing borrowed-buffer tests cover stride, padding and source
ownership. Keep the combined local/guest preparation below 96 MiB.

The final component passes those checks with the reviewed Image `0394CC2D`
and Accord `B2867867F025A5AE85A0B094AB4C6783AC38A538B5830BA05CB1D01FC1A749E1`.
Windows passes metadata/API6, science25, annotation5 and legacy1. Linux passes
package metadata6, package science27, full-reference science25, annotation5,
FITS13 and legacy1. Every process exits zero with empty stderr. The package
profiles select the exact new Accord PE against the unchanged 306-file graph,
without GdiPlus; the full-reference reflection profile has its separate declared
closure. Original failures and intermediate producers remain retained.

Both independent reviews are clear. Root verified all eight recipe/test source
pins and all 290 producing PDB source documents, with matching PE/PDB identity.
The explicit final source descriptor is
`current/nina/star-detection-package-20261021-1/accord-source-freeze-v3.json`,
SHA-256 `763EB2B8D04BEF6C3F14D307F25855F3374B2915040894E6F9BAB4D39CADC9D6`.
The source/license distribution is sealed in
`current/nina/star-detection-package-20261021-1/accord-final-handoff.json`,
SHA-256 `2DBFF9BB3AD14A24A723F02424EA9C726DE5D9DE8255B933612C3B288FF84D3D`.
Root verified all 24 evidence pins and all 14 distribution rows. The retained
source archive has 613 entries, each independently checked against its manifest,
with no duplicates or hash/length mismatches. It includes original and modified
sources, notices, build instructions and the exact recipe. Retained component
data stays below the 96 MiB cap.

The fresh whole application is
`current/nina/vm-package-image-retry-20261021-1/consumer`: 317 files and
130,112,917 bytes. Its manifest SHA-256 is
`FCD4945DB61CC4CF086AC284A313A299303A3634355B37B4BF7ABFCCD7C7FAAF`,
and composition handoff is
`18CFC732B5140A0631FF68EAC7D18AF8591F96608FB0B02CCC6C5A02D0BD1F33`.
Root rehashed every member: 303 base files are unchanged, three existing files
are replaced and 11 are added. Ordinary dependency metadata and runtimeconfig
are byte-identical to the qualified base. The final closed profile/layout are
carried separately and the absolute Framing cache is unchanged. Whole guest
transfer and native linkage verify. The original App has mapped successfully
with diagnostics off.

The first actual Snapshot used the observed original "Starts an exposure"
tooltip and lower aperture button with Save on, Loop off, 1 s, RANDOM 640x480,
AutoStretch and DetectStars on, High sensitivity and None noise reduction. The
worker saved the unchanged default legacy FITS pattern into the fresh owned
output directory. The 619,200-byte file has SHA-256
`620D18E91042668B61BEDF0DD4BC946FB1D4A76799F381366683464CC072A2A8`.
Root independently checked its headers, complete 307,200-sample payload and
block alignment: BITPIX 16, BZERO 32768, minimum 4565, maximum 5492 and mean
4999.647861328125. Root also rehashed all 16 first-fault evidence pins.

Rendering then threw `NotSupportedException` for `ImageBrush` through
`SkiaDrawingContextWalker.CreateBrush` and `DrawRectangle`. The original image
history thumbnail declares a bitmap ImageBrush with UniformToFill, making it a
source-backed likely owner; the live brush identity was not captured. Main still
showed the placeholder, so useful display, finite UI statistics, reload and
annotation checks did not pass. Reviewed Image and Accord were loaded
from their exact selected paths, with no GdiPlus mapping.

The observed error dialog's No button performed normal original shutdown:
exit zero, empty stderr, complete teardown and no remaining App or supervisor.
All 315 immutable members and the absolute Framing cache remain unchanged.
The final profile is
`C6BEDB2187551C5C1A4949857AE9B4C717D73193EE2FFC65666D08EE14501C0B`
and dock layout is
`C593AF675F5482B62B01E5EC864059D7E33E07367E24837BBCE0DC92D69456B1`.
Keep that restart state and saved FITS when retiring inactive runtime copies.
The handoff is `current/nina/vm-package-image-retry-20261021-1/HANDOFF.md`,
SHA-256 `9D2584C8B82C1ED3705E5FA27B976AA5975E07CDC0483250B54972D486CD7E90`.
Root rehashed all seven close-handoff pins. Combined local/guest data stays
below the 400 MiB cap. No current NINA process remains.

The focused renderer continuation reuses the existing private tile mapping and
DrawImage path for TileMode.None bitmap ImageBrush fills. It adds no public API,
dependency, persistent cache or application workaround. Its behavior checklist is:

- Render the thumbnail's centered UniformToFill profile through DrawRectangle,
  plus the existing rounded, ellipse and geometry fill entries.
- Preserve bitmap DIP dimensions, existing stretch/viewbox/viewport mapping,
  inherited sampling, source precision, clip, opacity and sibling state.
- Repaint source replacement, mutation, clear and restoration through retained
  invalidation without rerecording the drawing.
- Preserve null-source transparency and named unsupported boundaries for other
  tile modes, non-bitmap sources, pens and glyph brushes.
- Reproduce the old failure before editing production code, calibrate windowless
  scenes against stock WPF and run affected renderer/image regressions before
  a fresh coherent package and actual NINA Snapshot/save/readback replay.

The first provisional Core integration passed the functional matrix but raw
stock/portable comparison found repeated antialias coverage at fractional
ImageBrush edges: native alpha 255 became 80 at a 96-DPI corner and 16 at a
192-DPI corner. The viewport clip and content rectangle attenuated the same
edge repeatedly. The correction combines shape, viewport and content coverage
before painting once through the existing bitmap conversion and sampler. It
keeps VisualBrush behavior unchanged and adds full-edge regressions to the
existing cases. The old candidate fails the new guided and unguided controls.
The corrected rectangular and guided profiles have exact brush/control alpha.
Diagnosis is retained in
`current/image-brush-rendering-20261021-1/reports/edge-coverage-diagnosis.json`,
SHA-256 `D4487EBB40F806677B2C53FC50B79908046DA687896E843461CCC20E1B0BA684`.

Full captures also isolated Skia bitmap-shader versus solid-color rounding:
rounded and ellipse controls at 192 DPI differ at 12 and 23 alpha cells by one
unit. Only those two portable fixture comparisons allow that measured delta;
occupied support and every other alpha comparison remain exact. Independent
shader probes reproduce it without any path intersection, so no production
shortcut or custom sampler was added. Separate cross-native curve differences
also exist in the unchanged solid controls and are documented in the rendering
README; earlier 17/32 corpus measurements are not universal bounds. The
NearestNeighbor texel-boundary differences likewise occur in direct DrawImage.
Stock passes the final six cases and 28 captured profiles. Both production
reviews are clear, including the final test expectation and diagnostic-report
ordering repairs. The final portable matrix passes ImageBrush6, Rendering81,
Imaging52 and VisualBrush61 on each OS: 400 checks across eight processes,
all exit zero with empty stderr and clean builds. All 28 raw rasters are
pixel-identical across Windows and Linux; JSON file differences are line endings.
Root independently verified both Core PE/PDB pairs and 2,856 producing documents,
all six leaf pairs and 72 documents, all 121 protected inputs and all 590 final
artifact pins. Those component results precede the package and actual App
qualification below.
The final source descriptor is
`current/image-brush-edge-rendering-20261021-1/root-report-order-1/final-source-freeze.json`,
SHA-256 `CF953E081620C41A705AB0054B1581F144B70AF1854685917A701CD5D7B1A3C4`.
The first integration lane remains immutable. Its successor retains the failed
and diagnostic leaves in place and reuses both proven Core producers, with a
384 MiB combined local/WSL cap for the final leaves and raw evidence. Actual
retention is 328,287,933 bytes. The sealed handoff is
`current/nina-image-brush-integration-20261021-2/final-leaves-1/reports/final-handoff.json`,
SHA-256 `C296886C1F4743C557B2BC2540D6183EF4F0BD0E28374D60BDAC66F3B0829BBE`.

The normal package producer now includes commit `97c1da741` in
`current/final-distribution-20261021-3`. Its runtime package is SHA-256
`27AB554BD97AD991FD0E31AEF4E2E12FA762CCAB69C2892C7104E752F1999087`
and SDK package is
`6D8398CDC1EE1A77A4C697601F351D4C2AD43BE0531226CF70440A48F5C03B68`.
The build has zero errors and 27 warnings. All 18 PE/PDB pairs and 4,289 source
records verify, with all 9,604 protected records unchanged. Root separately
verified the 199 immutable package/output files, all producing pairs and source
records. The released Core is
`7C99BE9C47EF5D33A4532CA89200742C04D1508C4A26DEA4DF05D16B9BFA6978`;
its producing renderer document matches the reviewed source. Producer evidence
is `current/final-distribution-20261021-3-proof/handoff.json`, SHA-256
`39C1C43305628CC39821C4C0C393EBFEC967A44F03ACC7AE13E17A601ED5F675`.

Fresh Windows and Ubuntu consumers pass all 250 ordinary checks, including
native framework-dependent/self-contained execution and both compiler-origin
complete-view consumers. The package-derived ImageBrush replay passes six more.
All 199 producer files remain unchanged. Root verified the 952-row evidence
manifest; the proof covers 52 module paths and 341 source documents. The handoff
is `current/final-distribution-20261021-3-consumer-proof/handoff.json`, SHA-256
`9A8775F0F32FC8855B1729241D79F10C1C02B889B88F4EE6CB2349325C0BA0C3`.
The raw preflight failures remain retained. An unexpectedly expanded supplemental
restore cache was retired only after proving it inactive and recoverable from
the original package archives. This reclaimed 1,017,691,893 bytes without
deleting sources, publications or proof. Final local/guest consumer retention is
3,201,014,750 bytes, below the declared 3 GiB cap. All producer and consumer
writers are released.

The successor App uses a normal restore and publish against that exact package
pair. It retains the qualified prebuilt App/PDB, original BAML/resources and all
159 original application companions, without recompiling App or overlaying
individual WPF assemblies. The 289-file normal publication is combined with
17 qualified application inputs and the frozen Image/Accord payload to produce
317 files totaling 130,106,937 bytes. Root verified every member and the generated
dependency file's package identity. The composition handoff is
`current/nina/vm-package-imagebrush-20261021-1/composition-handoff.json`, SHA-256
`0CD62E58D7E6D479AE1A2455041F8A587ABF0FE0EBF4857CF7F2D9AB8ABA79A0`;
its publication manifest is
`16EA5AA2D6BF005CD9330EA7C013A488A54C4D56550951F8203B930D5D2A2EFC`.
The whole publication and native linkage verify in the VM. Startup and original
Imaging controls are visibly present with diagnostics off. This establishes
startup only; no new Snapshot, displayed-image or UI reload pass is claimed.

The first lifetime subsequently reached `LinearGradientBrush` through
`CreateBrush` and `DrawRoundedRectangle`, which remains unsupported in this
renderer. The stack does not identify its particular NINA resource or control.
The complete original log is retained as
`current/nina/vm-package-imagebrush-20261021-1/final-original-unfiltered.log`,
SHA-256 `D786C57223F549081AF99747AE1BDCE852C41900A4E09DB1E0AA13A71B4236DB`.
The user confirmed their intervening UI activity; no Snapshot was submitted by
the driver and there are no new FITS files. App and supervisor are gone after
normal teardown, exit zero and empty stderr. All 315 immutable publication
members and the absolute Framing cache remain exact. Preserve the final profile
`431D8B0FF1B8A1E731FBB1C0741F6E1F7427ACDDBFAAD4D58BF393BAFFE2A54D`
and dock layout
`C7C2F56D141568496A7B39A02D2E86B30826654FBB3A526B946248BAB05A39AB`.
Final combined local/guest retention is 390,720,677 bytes, below the 400 MiB cap.
Root verified the 17-member terminal fault manifest. Actual NINA imaging waits
for a coherent package successor containing the gradient repair below.

The gradient repair adds a private paint branch in the existing renderer. Its
behavior checklist covers four fill shapes, bounds-relative and absolute
projection, affine brush transforms, guideline/DPI coordinates, stop alpha and
opacity, stable duplicate ordering, retained mutation/animation and restoration
after an unsupported mode. Non-sRGB interpolation, non-Pad spread, invalid
multiple-stop offsets and singular/nonfinite transforms remain explicit
unsupported profiles; gradient pens/glyphs and radial gradients stay outside
this slice. Stock also confirms that null brush transforms mean identity. The
small fix applies symmetrically to the existing shared tile brush mapper.

The old final3 Core reproduces the unsupported gradient failure. Stock WPF
passes Gradient6 and ImageBrush6, including the null-transform controls. The
isolated portable Core builds with zero warnings/errors and passes the same
12 focused checks, exit zero with empty stderr. Both independent production
reviews are clear. Root verified all seven frozen sources, both author PE/PDB
pairs and 1,440 actual producing documents. The frozen source descriptor is
`current/linear-gradient-rendering-20261021-1/reports/source-freeze.json`, SHA-256
`89566C0577A8E22FB8940CB1273968252D96744D480F7331AAE2FF887AE3DED3`.
The author Core is
`AB8C0D3DF39D5296A16199E92C4A6B200725F992F85E0F77D20E492D9D8C2D92`;
it is an isolated verification build, not the next distribution.

Eight raw stock/portable profiles were compared independently. The source-derived
blue-alpha gradient differs by at most one byte unit at 96 DPI and two at
192 DPI; diagonal color differences are at most one with exact alpha.
Transparent-colored stops over green differ by at most one color unit at
96 DPI and two at 192 DPI with exact alpha. Coincident-endpoint
solid blue has exact interior pixels and separate rounded-edge differences of
51/57 units in the existing shape path. These are fixture measurements, not a
universal tolerance or a claim of Windows pixel identity. The comparison is
`current/linear-gradient-rendering-20261021-1/reports/author-native-gradient-comparison.json`,
SHA-256 `521825FB9CA96B8544083B47210B43EF2000DB0B1C8CD29F65898AACB3123729`.
The new package will run the affected rendering/image regressions on each OS
and an ordinary native Linux SDK consumer before the next actual NINA replay.
The terminal author handoff is
`current/linear-gradient-rendering-20261021-1/reports/author-green-handoff.json`,
SHA-256 `55A2D7D90289BFB0E2ED152F080295FFCC4ED7281EAAEB94B6DAE582E01FAF8F`.
All 143 protected inputs remain exact and no command remains active. Retention
is 73,109,717 bytes, below the 128 MiB author cap.

Separately, the corrected private caption diagnostic passes all 11 checks on
the current 306-file runtime. Independent native client origin and WPF screen
origin agree; both the selected caption midpoint and client50,10 are valid
caption hits in that fixture. Capture, original drag service and both tab drops
pass. This rejects the guessed invalid-caption hypothesis for that profile,
but does not reproduce the full App's Camera/Focuser behavior. The real restored
profile, content and GNOME frame remain different. Root rehashed its six
evidence pins. The sealed handoff is
`current/docking-qualification/caption-current306-observation/origin-native-handoff.json`,
SHA-256 `9F7D3BFFA29C5CDC04B45AB97A0A4E0AFAA479CFB4DBF83A18E435689E5F3D9C`.
Its earlier parent-relative-origin observation remains explicitly invalid for
hypothetical root-hit inference. Trace-only sources stay ignored; no production
docking change or new tracked test was made.

The preceding 334-file application lifetime had closed normally with exit zero,
empty stderr and no remaining app or supervisor. All 332 immutable members
and the absolute Framing cache stayed unchanged. Its final profile is
`AB2022699B8D3F6A4C6A69E7B2A8698922A02F83BE6304A014600BFB8E62B4DA`
and dock layout remains
`0C565FAC8A94E619BE71A229672AC56B22E2FDB7A3254CDE765E39D83AEA3136`.
Those values remain that historical family's restart baseline. Its normal-close receipt is
`current/nina/vm-sequence-fits-ui-20261021-1/POSTCLOSE.md`, SHA-256
`3EA18FCE98C34B7F96B6E1BF362EFC20FD9F6FB69A61DB359C998D7161227EAE`.

The complete original simulator-only application now completes a native
1-second capture, a 10-second capture cancellation and a subsequent recovery
capture on GNOME/XWayland. AutoStretch and DetectStars were enabled for that
first lifetime. The completed images have finite statistics and histogram data.
Ordinary close exits zero, saves the initialized original layout, disconnects
SimulatorCamera and completes the original plugin teardown. A second lifetime
restores that layout without the earlier fallback. The official-template plugin
pane hides and reopens and its Installed/offline-catalog view works.

The earlier 4E runtime publication was the complete 329-file consumer at
`current/nina/shell-vm-brush-20261021-1/consumer`, manifest
`70D6BBF844298277EC5FF89DCA6D5E62CAA664929D15CA731E5C3A377542AF36`.
Its whole framework manifest is `4E804208D2420743C7DF47DEC3DFC44967C6A9558EC82F6CE089ED25006E997A`.
The guest directory is
`/home/stefan/wpf-portability-local/phase5/nina-shell-brush-20261021-1`.
The second lifetime was PID 1884015, start ticks 2540746. It has now exited zero
after the original error dialog's observed No button was clicked. No NINA
process remains in that guest directory. Native capture,
cancellation, close and relaunch evidence is
`current/nina/vm-brush-ui-20261021-1/capture-lifecycle-handoff.json`, SHA-256
`6FD2CF31F0E3BC3862E2EAEE76D61C6C2F888D1C43B6ACF707BB127C779A6776`.

Four focused NINA repairs are now committed and qualified as one framework
family. Their actual application replay is recorded below:

- Floating Statistics previously saved outer height 726 as 763 after the caption
  arrived late, then restored that enlarged value. The affected size axes in
  `Window.Portable.cs` are now reconciled before acknowledging old client geometry.
  Preserve outer
  dimensions through frame addition/removal, DPI and save/recreate while still
  accepting later native user resizing. Do not adjust AvalonDock serialization.
- Original Offline Sky Map creation evaluates `BitmapPalettes.Gray256` before
  creating its non-indexed Gray8 background. The managed palette now supplies
  its 256 immutable opaque grayscale entries and the buffer accepts/ignores
  the Gray8 palette as native WPF does. Preserve source bytes,
  freezing, cloning, rendering and null Gray8.Palette; indexed images and unused
  palette types remain outside this repair.
- Original sequence dragging uses a zero-duration Radius 0 -> 10 BlurEffect
  animation on the source subtree. The retained walker now provides a transient
  Gaussian layer for the default Performance profile and axis-aligned
  transforms. Preserve native radius truncation, minimum scale and device cap,
  clip/effect/opacity ordering, retained invalidation and restoration on drop or
  Escape. Other effects, kernels, bias profiles and skew stay explicit unsupported.
- The original sequence add menu reaches `MenuAnimation` through
  `MenuPopupAnimation`; its nested hover route also reads `MenuShowDelay`.
  The portable disabled-animation policy now extends to these menus, with a 400 ms
  Classic hover delay. This is framework policy, matching the current Windows
  reference profile value, not an observed Linux desktop preference. Preserve
  original menu resources, timers and native pointer operation.

Each repair uses existing tests and native/reference tooling, with a failing
reproduction before its production change. No new dependency, public API or
backend service is planned. Qualify one combined framework family, then replay
the actual application through restored floating dimensions, offline Framing
and sequence drag/drop/cancel. Component tests do not replace those native gates.
The offline Framing first-fault receipt is
`current/nina/vm-brush-ui-20261021-1/lifetime2/broader-first-fault-handoff.json`,
SHA-256 `4E41355D7D24E128BAEFDE55ED810D76197976F7F8027BBA7A0C8FD427D0E5F1`.

Commit `aeb11e6` preserves the original Framing/cache recipe in
`src/Portability/Tests/NinaPortableViews/Framing`. A fresh entry-only build and
private Xvfb replay passed all 21 checks on the then-current 70D6 consumer, with
412 unchanged inputs and clean shutdown. Its corrected handoff is
`current/nina/framing-tracked-replay-20261021-1/framing21-handoff-corrected.json`,
SHA-256 `80B37BDEF8972E36AEEC8A54C7787F8038521818DD0D7F82D07B912FC70E2D27`.
This is managed view/VM/cache coverage, not actual native Offline Sky Map input.

Actual theme and language changes also pass in the second lifetime. The original
eye button changes Persian Faint/Dark and restores it. The original Language
list changes en-GB -> de-DE -> en-GB without restart. The full saved profile
returns to its original bytes. The receipt is
`current/nina/vm-brush-ui-20261021-1/lifetime2/appearance-roundtrip-handoff.json`,
SHA-256 `49FF8C1873EB9B50C87247D68BA754C1138E50A3318981E90E089C70CA93F8A9`.

The subsequent original sequence root add menu stopped at the menu-animation
fault before any item was added or sequence was executed. Its error dialog
clipped the button labels; a pointer click on the observed No-button strip did
execute the original shutdown path, including layout save, camera disconnect
and plugin teardown. Button visibility remains a separate sizing observation
for candidate verification. The normal error-route close receipt is
`current/nina/vm-brush-ui-20261021-1/lifetime2/sequence-first-fault-close-handoff.json`,
SHA-256 `09E79D1D47012AEF322FD92077A2FB7890566DDFCFA41B86E333AB9E9837176B`.
This proves the actual No-button outcome, unlike the earlier unobserved Alt+N
handler attempt. There is no native sequencer run/cancel claim yet.

The earlier 4E family's final closed Statistics window was saved at outer
1370x800, left 1074 and top 553. Use this final value for the next restart check,
not the historical 726 or 763 heights. The final saved profile has AutoStretch
enabled but DetectStars disabled, SHA-256
`74F993927416440399CC9A51002923F8F4EDEA0FDBE9AB9A990204E057FBED6D`.
The cause of that later toggle is not established. Restore DetectStars through
the original UI before testing the declared processing defaults.

Preserve the configured cache directory at its existing absolute path:
`/home/stefan/wpf-portability-local/phase5/nina-shell-tooltip-20261020-1/app/QualificationState/FramingAssistantCache`.
It contains only `CacheInfo.xml`, 60 bytes, SHA-256
`7E8BB328C35AB7C7BFF8DDC2315FA67B3826730542B240252F89BC1736BD433D`.
The original settings loader resets a missing directory. Obsolete runtime
files around this state directory do not need to be retained.

The current behavior checklist is:

- [x] Capture, cancel, recover and close the original application normally.
- [x] Restore the original layout, plugin pane, theme and language.
- [x] Qualify the combined palette, blur, menu and frame repairs on both OSes
  and through the existing native window tests.
- [x] Publish one complete framework family with the unchanged NINA components.
- [x] Preserve Statistics outer height 800 across native close and relaunch.
- [x] Preserve floating-window position after hiding its page and saving.
- [x] Operate the actual offline Framing map and its input controls.
- [x] Open the original unanchored sequence ContextMenu through its Click animation.
- [x] Add and edit sequence instructions, complete a sequence, cancel a running
  sequence and complete it again after reset.
- [x] Complete and cancel the original sequence drag operation with its blur.
- [ ] Complete final package and sustained NINA workload gates.

The combined family passes 321 managed checks on each OS:
Rendering 75, Imaging 52, SystemSettings 126, VisualBrush 61 and the seven
focused blur checks. These include repeated coverage, not 321 distinct new
tests. Both ordinary Core and PresentationFramework builds succeeded. The
isolated friend lifecycle runs pass 100 checks per OS: full 87, focused 12 and
empty-window 1. The focused run includes five frame cases and seven existing
Application.Run child checks. Its first
attempt failed autosizing; the retained failure led to invalidating Window's
cached measure when the native frame changes. Friend assemblies are separate
from the production publication.

The released production family is the 120-member Stage in
`current/nina-gray-blur-frame-integration-20261021-1`, manifest SHA-256
`52E93D1C3479F4E901CE9A9F30E02486AA9136835F24E195DE0CE52AE80EFD4F`.
Root independently rehashed every member: 591,034,968 bytes and no mismatch.
The six changes from the previous family are Core/PF and the Stage wrapper's
DLL/PDB pairs. All 109 native checks pass across 14 private processes, including
frame addition/removal at 96/192 DPI, genuine submenu hover and the long-text
MessageBox's full button containment and No action. Every process exits zero
with empty stderr and complete native cleanup. The ordinary runtime has two
changed producers and 14 unchanged producers, including NativeHost.

The final intake is
`current/nina-gray-blur-frame-integration-20261021-1/consumer-intake.json`,
SHA-256 `167661832466F962B22FFCD7768A95C407400DB474C4F6628582716170AC4A7A`.
Root also verified all 40 nested artifact pins. The producer retained 25 source
snapshots and producing documentation; its 759 protected records are unchanged.
Retained logical size is 11,012,810,700 bytes before the final receipt, below
the declared 11 GiB cap. The source commits are `1923fe2d0` (palette),
`6866ac92e` (blur), `3c84dd950` (frame sizing) and `425a842a4` (menus).

All 12 blur captures match exactly between the two portable OSes. NINA's Radius
10 fixture differs from native by at most 4/255 at both DPI settings; Radius 3
differs by up to 18/255. Quantization, device cap, clipping, scaling, reflection
and sibling isolation hold. The measured scope is recorded in
`current/nina-blur-proposal-20261021-1/quantitative-comparison-1/REPORT.md`.
The tracked Rendering README received these results after producing snapshots
were retained; no compiled source changed after verification.

The fresh complete NINA publication is
`current/nina/shell-vm-gray-blur-frame-20261021-1/consumer`, 329 files and
684,233,030 bytes. Its manifest is
`549D4ECFFD25A054605EC925C280DD7282157038168BAD368301D9EBC8154F4E`
and composition receipt is
`3037B2377D9B6F158855C96135A0DB3D0930925AD18426785978F5211EF5CBF8`.
Root rehashed all 329 members without a mismatch. It contains the whole new
120-member Stage and 209 unchanged companions; all eight NINA components are
unchanged. The original App ran on the real desktop in two new lifetimes.
Simulator connection and original Imaging layout restore work without fallback.
Root inspected
`current/nina/vm-gray-blur-frame-ui-20261021-1/imaging-restored.png`.
The first lifetime completed explicit 1-second capture, active 10-second exposure
cancellation and 1-second recovery with AutoStretch and DetectStars enabled.
Original close exited zero and saved Statistics at outer 1370x800 at 1074,553.
The second lifetime restored exactly that size and position: native client
1370x763 at 1074,590 plus independently observed 37-pixel caption.

The actual offline SKYATLAS then loaded the local catalog, grid and camera
rectangle. Zoom 3 -> 2 -> 3 and native drag/inverse changed and restored the
coordinates. Rotation 30 rendered, but subsequent Tab focus triggered a new
required WPF fault: `Portable dashed bounds require linear contours` during
`UIElement.GetHitTestBounds`/`MouseDevice.Synchronize`. The genuine owner is the
Slider focus rectangle in `NINA.WPF.Base/Resources/Styles/Slider.xaml`: transparent
fill, corner radii 5, stroke thickness 1 and dash array [4,4]. Camera overlay
rotation itself rendered successfully. Rotation return, mosaic controls and
all sequence/menu/drag operations remain unqualified.

The error dialog now fully displays its buttons at 527x127. One genuine No click
executed the original shutdown with exit zero, empty stderr and all 327 immutable
runtime members unchanged. No owned NINA process remains. The sealed workflow
receipt is `current/nina/vm-gray-blur-frame-ui-20261021-1/workflow-handoff.json`,
SHA-256 `B5D05919CCF450C689558F0C2C00AB2E5953E9EC8F6E51A9BF254B594520D030`.

The later hidden-page save retained height 800 but changed Top 553 -> 516.
This is separate from the successful visible save/relaunch result. Diagnose the
hidden native-position reconciliation before claiming full placement stability.
The second lifetime's closed state is `postclose-rounded-dash-20261021-1` in the guest
publication. Its profile SHA-256 is
`8C025CDAC9959624006BB15636CC9E62F90428D1EE442389FA44EC1B84117A7D`
and dock SHA-256 is
`290D7343F3157CD882ADB004BF2DD9B624E7CF7DE1E0BD8EE19F6053A648942A`.
That baseline has Statistics 1370x800 at 1074,516, DetectStars and
AutoStretch enabled, High/None, 1-second exposure, Loop/Save off and SKYATLAS.
Saved LastRotationAngle is 330; the failed attempt to return the UI to zero did
not apply.

A third, diagnostic-only lifetime identified the hidden-position cause. Hiding
Imaging unmapped the Statistics client and reparented it to the root at the old
outer position, while GNOME retained a 37-pixel top frame extent. Subtracting
that stale frame from the hidden client origin changes the logical Top again.
Re-show restored the prior geometry. A normal close after re-show saved Top 516
unchanged, so the defect is specifically the hidden save path. This lifetime
did not connect the camera or capture an image. Exit was zero, stderr was empty
and all 327 immutable runtime members matched.

The latest restart baseline is now `postclose-top-drift-20261021-1`, with profile
SHA-256 `8AB144C03FE393E55814CF14A3356BEA70E5EA3CA96D7D55D47EE424C85D555A`
and dock SHA-256
`439FBE5E5F49747AFB9995CA5448852824FDD6F804A854EF4CC130226713A3EC`.
Statistics remains 1370x800 at 1074,516. The sealed diagnosis is
`current/nina/vm-gray-blur-frame-ui-20261021-1/lifetime3-top-drift/diagnosis-handoff.json`,
SHA-256 `3E40A68DC6EE81EF5A001AB49747135F3533A32CC7A10E74CE839CB8FCCE39D9`.

Both focused repairs are source-ready and independently reviewed. The position
repair retains accepted logical coordinates and pending position requests while
hidden, without changing size or native coordinate reporting. Rounded dashed
painting and bounds use WPF's canonical cubic contour. The revised focus test
retains the old-Core precompute failure before filtering the non-hit-testable
focus visual. Native and candidate Windows focused checks pass 7/7 and Windows
Geometry passes 689/689. Two exact alpha-1 antialiasing fringe cells are recorded
as optional raster witnesses; all other occupied cells remain strictly checked.
The rounded-focus repair is committed as `723e5860a` and hidden placement as
`c898bed42`. Both OSes pass Geometry 7 focused/689 full, Rendering 75, guideline
dashes 48 and isolated lifecycle 104. The latter is focused 14, full 89 and empty
window 1, including repeated coverage. Native frame controls pass five checks at
96 DPI and five at 192 DPI with complete private Xvfb/Openbox cleanup. Windows
Geometry reuses the reviewed successor entry against the identical ordinary Core;
it was not repeated solely to produce another copy of the same result.

The new ordinary Stage has 120 files and 591,035,032 bytes, manifest SHA-256
`9A69056C7CC061303A7F164F37BF795AB98BEABE24E331C2842CAF8AA8C92109`.
Only Core/PF and the Stage wrapper's DLL/PDB pairs change. The intake is
`current/nina-rounded-focus-hidden-position-integration-20261021-1/consumer-intake.json`,
SHA-256 `3804F2EBC6A364383B08904F35FEE31CD5A574619C480D0718B45030112C8944`.
Root independently verified all 120 members and 37 nested intake pins. All 882
protected records remain unchanged. Ordinary runtime and isolated friend test
assemblies remain separate. The two author lanes plus integration retain
2,002,176,068 bytes, below their combined 2 GiB cap.

The initial missing-native-search-path and generated-obj-glob runner failures
are preserved separately from product behavior. Corrected small leaves reuse
the existing native assets and output-routing props. Both PF builds retain the
parent's 26 warnings with zero errors; ordinary Core, Stage and final external
leaves have zero warnings/errors. No new dependency or harness was added.

The actual replay used the fresh whole publication under
`current/nina/vm-focus-position-ui-20261021-1`, 329 files and 684,233,094 bytes.
Manifest SHA-256 is
`9D067830319C42AC1D503B32E6D2CA3F22BC0521E1D989B0534DF12883E39CCC`
and composition SHA-256 is
`7A5E59FED63655AAB39774DCDDC34FD543BA06A76D4E158286609847B22751A4`.
Root verified all 329 members. All eight NINA components remain unchanged.

On real GNOME, normal close while Imaging was hidden saved Statistics at exactly
1370x800 and 1074,516. A second lifetime restored client 1370x763 at 1074,553
plus the observed 37-pixel caption, giving the same outer dimensions/position.
The original Framing SKYATLAS loaded its local catalog and overlays. Rotation
30 -> 0, mosaic 1x1 -> 2x1 -> 2x2 -> 2x1 -> 1x1 and overlap 20% -> 25% -> 20%
visibly update and restore. Root inspected both focused-30 and restored-zero
screenshots. No camera connection or exposure was submitted in these lifetimes.

The next original sequence Add button stopped at
`A portable Popup requires an attached placement target`, wrapped by the original
BooleanAnimation error reporting. Its style-set ContextMenu has no target and
opens directly through a zero-time Click storyboard, bypassing the context-menu
service that ordinarily assigns one. Existing anchored popup tests cannot cover
this path. No item insertion, sequence run or custom drag is claimed.

The observed original No button completed normal shutdown: exit zero, empty
stderr, original layout/equipment/profile/NOVAS/plugin teardown and all 327
immutable members unchanged. Both processes and supervisors are absent. The
sealed replay is `current/nina/vm-focus-position-ui-20261021-1/replay-handoff.json`,
SHA-256 `CCFAE8237BDA6A63B205436AA74122E3FF3614FEFA1A4896FF2D1FB25F29A62A`.
Latest guest state is `after-no-pointer-20261021-1` under
`/home/stefan/wpf-portability-local/phase5/nina-shell-focus-position-20261021-1`.
Its profile SHA-256 is
`B0DBB6712C6F623012AF40FD851CAD7C74EAD41926396714184085E7F8E853F6`
and dock SHA-256 is
`0C565FAC8A94E619BE71A229672AC56B22E2FDB7A3254CDE765E39D83AEA3136`.
Statistics remains 1370x800 at 1074,516 even after later hidden-page saves.

One failed helper returned before keyboard input, but its caller still submitted
a queued mosaic click. That driver failure is retained without a mosaic claim.
The existing helper now throws on nonzero; a disposable local exit-7 stub proved
that later statements stop, without submitting VM input. The two unmatched
earlier exposures remain unattributed. The read-only timing audit is
`current/nina/unmatched-exposure-audit-20261021-1/receipt.json`, SHA-256
`51042CDF1B53810B1D28DCD631AF19257468CB4A1AA2CDACD25812BB031DA2AD`.
Neither an automatic capture nor a duplicate-input cause is established.

A fresh entry-only build against this exact shared publication also passes the
tracked Framing/cache 21 checks on private Xvfb. All 412 inputs remain unchanged,
the PE/PDB match 13 producing documents and cleanup passes. The sealed receipt is
`current/nina/framing-gray-blur-replay-20261021-1/framing21-handoff.json`, SHA-256
`852D6665E04B8585DC70F9C66DB5EC70753F01BA73D658423DB0AF5407ABB0AE`.
The complete return is below 64 MiB and contains no duplicate runtime. This
managed result does not establish actual native Framing input.

The 52E/549D family now has its own capture/lifecycle evidence. The next WPF
repairs address curved dashed hit-test bounds and hidden-window placement before
resuming Framing and sequence checks. This is no Phase 2-5 completion claim.

## Required application file I/O

The current capture checks intentionally use Save off. Original default FITS
settings are NONE compression and `FITSUseLegacyWriter=true`; that writer is
managed. All original FITS loading still calls CFITSIO, which is absent from the
current publication. Nonlegacy saving also needs it even with compression NONE.
TIFF encoding and XISF roundtrips are separate, unqualified paths. Changing the
default format is not a substitute for the original FITS requirement.

Independent source audits recommend retaining upstream CFITSIO and correcting
the private Linux interop at NINA.Image's existing native boundary. Public method
signatures, original metadata and scientific conversion algorithms remain the
contract. Linux needs fixed-width pixel coordinates/dimensions, a by-value
`size_t` memory delta and an assembly-scoped exact library resolver. Native
constructor/decompression failure cleanup, datatype/buffer mismatches and
unsupported dimension handling need explicit tests before qualification.

The official 4.7.0 source tag names commit
`d6d27653740864a0483e74cff23c53e34e1dceee`. A bounded source/license intake retained
one archive, SHA-256
`B99C555309EEE9187615A9D64CB9A7AD84C2F3E94A0241C11BB024CCD5C56DDE`,
and exact notices at `current/nina/cfitsio-intake-20261021-1`. Its receipt is
`951E56898760E14E723763E2E4AF3BA367E3E6110161FE867BBB3BCC46BA76AF`.
Upstream `configure --without-fortran` excludes unused Fortran wrappers; the
ordinary CMake profile does not. The complete archive has additional notices
and must not be described as uniformly permissive. Runtime selection must retain
all applicable notices.

The isolated C-only, reentrant build now passes on Ubuntu 22.04/glibc 2.35.
Upstream tests report 54 passes, one coverage-only skip and no failures. The
selected 1,463,656-byte shared library has SHA-256
`4558BDC93601DAB4ADCA9FB36502198BC98C926BA9BB7038A417E6A4E17420E8`.
Its required exports and system dependencies resolve, Fortran wrappers are
absent and the reentrancy flag is enabled. Build warnings and applicable notices
are retained. The source is unchanged and temporary guest files were removed.
The feasibility receipt is `current/nina/cfitsio-feasibility-20261021-1/feasibility-receipt.json`,
SHA-256 `0F801595ABFF8ECCA945046017832E62A1E901AA3594F465B8C6252BF92D365C`.
That checkpoint proved the native library only. The managed boundary has now
passed its focused Linux qualification and independent correctness and
maintainability/test-value review. It retains original scientific conversion
loops and Windows branches, supplies correct Linux coordinate/axis widths,
loads the exact application-local library and closes native handles and
temporary decompression files on failures. A 3D input retains the original
first-plane behavior with a complete coordinate vector. The unused unsafe
float-writer datatype mismatch rejects before native sample access.

The final component is `current/nina/cfitsio-managed-20261021-1/component-successor-1`,
intake SHA-256 `4D4BCBE9FB5C4B086D1C1CDBABD546DAD665D638C033D4FC78FD6E88E943B2EC`.
Its Image PE is `9C5EA87B8C44BEA5E7D8AE57E8A884FA06DA5F88732514A5CD6F46F78303FC8A`.
Linux passes 13 FITS, 25 science/public-contract, one legacy-byte and five
annotation checks. Original and adapted Windows each pass the latter 31 checks;
native Windows FITS roundtrips remain unperformed because its binary is absent.
The source freeze is `147FE96EC01D5746366C0FDB8CD9E264C9F790C79C8EF64210F829F9F60FFE19`.
Root verified 60 source, snapshot, binary and evidence pins in
`root-final-verification.json`, SHA-256
`6CA9ABD5DDC0B7C485C9734CA80C97D6FBD8318677CBF144EF9920769D4FB619`.
The existing fixture and builder carry the exact native library and four
upstream notice files. No native shim, public API or new test harness was added.
Whole-App publication and actual saving/reloading remain pending. Selected
signed-int writing and wider import types still need their own qualification.

Reuse existing image/save fixtures for odd-width ushort legacy-write/original
reload, alternate writer, selected lossless compression, row reads, metadata,
source preservation, failures and recovery. Then verify an original App snapshot
with Save on and Loop off, waiting for the worker's success log and actual file.
Accepted save work has its own timeout; exposure cancellation does not promise
to cancel an already queued write or drain it during shutdown.

The unchanged default filename pattern also uses Windows backslashes. Linux's
platform separators alone do not turn those into nested date/type directories.
Include this actual default-pattern case rather than hiding the gap with a
simple temporary pattern. These are required NINA file-I/O gates, separate from
the already-passing prepared-frame display and capture-only workflows.

The filename-pattern correction is committed as `ae0644dbe`: normalize template
backslashes before macro substitution, leaving native sanitization and custom
token values intact. The old Core fails seven of 15 Linux cases while Windows
passes all 15. The new source-proven Core passes 15 pattern and 13 existing URI
checks on each OS. Its PE is
`422502A5902374CAD7D6CF661B085FD47F777C4BB54E9CD2C1A618C084BA78F4`.
The existing generated Core project was built alone; no Astrometry/App rebuild
or new harness was needed. Actual App saving still awaits integration.

Commit `b09c297eb` adds Linux startup-only opt-in WPF diagnostics through
`WPF_NINA_PORTABLE_DIAGNOSTICS=1`. Standard Warning trace sources and one shared
flushed stderr listener run after NativeHost initialization and before App.Main.
Default and Windows startup are unchanged. The existing view fixture links the
actual entry; enabled and disabled runs pass five checks each, including genuine
binding/resource errors, recovery and source cleanup. Self-review and independent
maintainability/test-value review are clear. The App-only producer now passes
with 469 matching PDB documents and all resource payloads unchanged from the
previous portable App, including 104 BAML entries and 34 ordered App dictionaries.
Its intake is
`current/nina/app-diagnostics-build-20261021-1/application-consumer-intake.json`,
SHA-256 `665DB9402E5B4AB49881DCE5F20FD7CC7847E8F8E23B0B0AF9669B939FA03EFC`.
Root rehashed all 448 build-receipt members. No Plugin, control or framework
rebuild was required. The whole App now launches with diagnostics off. A separate
diagnostics-enabled installed lifetime remains pending; the small fixture only
tests the actual entry's diagnostics helper and does not execute App.Main.

## Remaining Phase 5 execution evidence

The earlier candidate packages build through the ordinary producer at source
`5967f51cb`. The Runtime package is
`D164CE8942B6DD96BD841AC29F496DFAC263BA87081984A4934765D4518AACB6`
and SDK package is
`6AEF82281C5E34CB30A1EA1D0F303DE720C1EB1A093D2F1E9B3F61F31CD6766D`.
The producer exits zero with 27 warnings and no errors or stderr. All four
archive audits pass. Its actual 18 producer/compiler PE/PDB pairs have 4,289
matching source-document records. These are new producer outputs, not byte
copies of the earlier Stage. The sealed handoff is
`current/final-distribution-20261021-1-proof/handoff.json`, SHA-256
`FBA8B33D5154D91981EC683C81B73CCBF0DADBEFFF4DB071A0555F496C592B96`.
Root independently rehashed its 22 receipt pins and all 199 immutable payload
members. The original producer audit is preserved before consumer execution.
The frozen candidate now passes Windows consumer 37, actual Ubuntu managed 62,
native FDD 14 and SCD 14, dependency conflicts eight, runtime policy 14, contents
seven and both compiler-origin complete-view groups totaling 94. The consumer
handoff is `current/final-distribution-20261021-1-consumer-proof/handoff.json`,
SHA-256 `C3FDEFEB115CFD5AB0F4FD4A041EFB4B190511ED2E96C83BA6D32A1A8E406FDB`.
Root rehashed its seven receipt pins. The guest uses an exact official Ubuntu
Xvfb package extracted into owned test prerequisites, without a system install
or live desktop change. Initial missing-prerequisite and transfer failures are
preserved separately. The 51 consumer PE-module proofs cover 330 source-document
records. Redundant inactive cache/transfer files were removed after scoped
verification; original source, generated files and producing evidence remain.
This qualifies the frozen `5967f51cb` packages, not the WindowsBase successor or
the current Stage-based NINA publication on the final package boundary.

The repaired successor packages now build through the same ordinary producer at
source `b71565e01`, including the contended weak-event wait fix. Runtime SHA-256 is
`8C6BC1E355D1E11DB6DDD0759D3FB439D75CE3148D663707EC1CFDE37C32A5C7`
and SDK SHA-256 is
`6473EDC052D8503B6FD20C18D025A14E8638CB448B48A85C6D38AAEA4329FE5A`.
The producer again exits zero with 27 warnings, no errors and empty stderr.
All four archive audits and 4,289 source-document checks pass. Its handoff is
`current/final-distribution-20261021-2-proof/handoff.json`, SHA-256
`C934DE3908F8DFCAA2CA57CA78A1603557BB9F3264E6F981DDB4D2C0AFDAB39B`.

That exact repaired pair passes the same 250 consumer checks listed above,
including actual Ubuntu managed/native FDD/SCD and both compiler-origin
complete-view groups. The sealed consumer handoff is
`current/final-distribution-20261021-2-consumer-proof/handoff.json`, SHA-256
`565FBDF92DB3CE7185137AEF46ED3811F07DBF0EF5D2E671A28F15D892354C0E`.
Root independently rehashed its eight receipt pins, 624 evidence members and all
199 immutable producer members after execution. The 51 module proofs cover 330
source-document records and the 18 actual compiler load records match the new
SDK. Private Xvfb cleanup passes. Original producing sources and publications
remain retained; only the preauthorized redundant cache and transfer files were
removed. Combined retained consumer storage stays below its 3 GiB reservation.
This qualifies the repaired package boundary, not the pending full NINA replay.

The App-only source build and ordinary Linux FDD publish through that exact
released SDK/runtime pair now pass in `current/nina/package-app-20261021-1`.
The final handoff is `package-app-build-handoff.json`, SHA-256
`439BE6156EEAB5FDED1DDDD03E3AB7BFB5E4D338DBB5CF0BF7B389F8CA7B1968`.
The publish has 289 files and 101,489,546 bytes. Its new App DLL is
`3668564ED3DB26E2E4FAF1AE1567FFE3C5F16F6ECEE803E4920AB7104150C45F`
and PDB is
`BB08BC4A8E7C87129CAE500C64A817A2CB2514590BE3609641F44699B901403C`.
All 469 PDB documents verify, including 401 physical files and 68 embedded
generator sources. All 104 BAML entries, 115 resource keys and 34 ordered App
dictionaries are preserved, with zero resource payload differences from the
predecessor. All 159 reused companion assemblies match their selected pins.
The ordinary build has the existing 320 obsolete-API warnings, no errors and
empty stderr; publish also exits zero with empty stderr.

Two consumer configuration corrections preserve the original item graph and
companion selection. Disable default Page items because NINA already explicitly
includes its pages and excludes three resources. Point only NINA.Core's existing
HintPath at a small verified directory containing the same Core DLL/PDB and 35
original satellites. Normal reference resolution otherwise chooses newly built
adjacent satellites. Those have identical identities and resource payloads but
different provenance. Both initial mismatches and the first App pair, generated
sources and output manifests remain retained. Redundant failed bin/publish
copies were removed after guarded checks. The final lane stays below 512 MiB.
The independent correctness finding is closed and maintenance review has no
additional finding.

The final dependency metadata is SDK-generated. All 32 managed and two native
package runtime assets and 53 runtime notices match the package. Stage and
metadata-only GDI contracts are absent from the runtime graph and publish.
GdiPlus remains a compile-only reference with Private and CopyLocal false.
The unselected adjacent NINA.Core.dll.config is absent, as it was in the earlier
qualified application; the original App config remains. Companion producer
histories retain their original compile-reference origins.

The fresh whole application is
`current/nina/vm-package-sequence-fits-20261021-1/consumer`: all 289 normal publish
members plus 17 qualified application native/data/plugin/state/notice files,
306 files and 126,826,014 bytes in total. Its manifest SHA-256 is
`339A743250EAEED5C609EC6A22902A60686932047B087E1F266A4C86102A888F`.
Root rehashed all 306 members, all 401 physical source documents and the 33 final
evidence pins. The fresh VM transfer and native replay are released, using the final
closed profile/layout above, diagnostics off and ordinary dependency metadata.
No full application result follows from these build and composition checks.

Final package execution, actual Xorg desktop qualification and the sustained
original simulator session remain open. The current guest session is GNOME Wayland with
XWayland, not Xorg. The installed login choices include Ubuntu on Xorg; switching
requires coordinating a normal logout/login with the user after closing NINA.
Native IME and further Orca work remain explicitly deferred.

Actual desktop clipboard exchange and exact Open/Save accept/cancel outcomes
remain separate gates. The retained private fixtures pass clipboard nine and
portal 31 checks, but historical human picker runs selected different files or
accepted an intended Cancel. Do not count those as strict outcome passes. Use
the original NINA sequence Save As/Open paths for the next bounded desktop run.

The resource ledger still has no accepted shell memory, native-allocation or
thread-growth budgets. The proposed overnight workload uses the original
640x480 RANDOM simulator loop and must remain separate from the qualified 6k
prepared-frame workload and accepted RGB48 timing. No new timing optimization
is planned. A bounded installed Massif probe could not start the unchanged CLR
and aborted with Out of memory; it is not an application failure or a pass.
A separate installed glibc memusage probe passes the real managed legacy FITS
case without CLR, GC or ISA changes. Its receipt is
`current/nina/native-allocation-probe-20261021-1/memusage-1/receipt.json`,
SHA-256 `074127F862DD456C7332D1179278CA95388466037AD72D1DB24D14AA15F7528B`.
This establishes allocator-observer feasibility only. Cumulative allocation
traffic, tool-reported peak, process RSS/PSS and end-live bytes are distinct;
the probe does not establish the latter, an overnight budget or whole-App
compatibility. The unchanged fixture's small generated-file location exception
is recorded, with released consumers and pinned inputs unchanged.

A bounded read-only process sampler now extends the existing health reader.
Its five one-second validation samples checked the same PID, start time, UID,
executable, arguments and working directory before and after each sample.
All 19 offline controls and the single live smoke pass, with exit zero, empty
stderr and no app signals or input. The sealed packet is
`current/nina/overnight-preparation-20261021-1/sampling-1/handoff.json`, SHA-256
`0A8FE915E41FD30EED7BC3F99E296586D7B3DFBF8EA58E42F67C2FE86CEB4D4A`.
This qualifies the sampler only. Its samples overlap sequence interaction and
are neither idle baseline nor sustained-workload measurements.

The later whole-App allocator observer is prepared separately from the resource
pilot. Its final review packet is
`current/nina/overnight-preparation-20261021-1/memusage-app-summary-3/handoff.json`,
SHA-256 `D69692CC6D3C1F18D88AB4EE5C263C0B150AAFFCCCD30C6AF8C02A693AED33FF`.
Root verified its 22 file pins. Nine disposable local process checks cover normal
exit propagation, wrapper-only launch failure, surviving owned group members,
interrupted exits and exclusion of unrelated groups. Independent review closed
the supported cleanup and exit-classification findings. The observer retains
the direct wrapper until group cleanup completes and reports an App exit only
after actual App discovery without observer interruption. The installed App and
profiler have not run through this observer. It requires a separately released,
normally closed prior lifetime, exact state carry and refreshed guest/tool
facts. Its summary-only output cannot establish end-live allocation bytes,
native-memory totals or overnight stability.

The sections below retain earlier attempts and their original limitations.

## Original startup boundary

The initial original App/MainWindow attempt reached `MainWindow.Show`, then failed
asynchronously when binding metadata initialized StarAnnotator's GDI resources.
There was no retained native MainWindow map or useful pixel capture. The original
failure remains in
`artifacts/portability/current/nina/shell-integration-1/first-run-handoff.json`.

The persisted simulator profile enables AutoStretch and DetectStars, uses High
star sensitivity and None noise reduction and disables annotations. A visible
shell is the first milestone. A default simulator exposure also requires genuine
stretching and detection; the prepared-image display tests do not cover them.

## Earlier startup and image-processing checkpoints

The original simulator-only App/MainWindow now opens on the owned Linux backend.
The retained run `shell-startup-20261020-2/runs/run.rVHt3aYM` contains two actual
800x600 window captures. Both show the original Camera page, navigation, caption
and controls. Root inspected the second capture. The application closed with
exit zero, no standard-error output and observed original plugin teardown.

The startup observer subsequently failed its unconditional docking-layout file
check. The original application starts on Equipment and creates its Imaging
docking manager only after selecting that page. Its save method deliberately
requires that initialization. This startup run performed no UI actions, so it
does not qualify initialized docking-layout persistence. The original failed
observer result remains intact; no application change is needed for this check.

The complete five-file image-processing port is committed as `52f157449`.
Focused qualification passed 25 checks against original Windows, 30 against
adapted Windows and 30 on Linux, including actual cancellation and recovery on
the same detector and source image. Independent correctness and maintainability
reviews found no remaining supported defect. Evidence is under
`current/nina/image-cancellation-20261020-1/cancellation-handoff.json`.

A fresh full application publication with this final image component has also
opened on the real Ubuntu 22.04 GNOME/XWayland desktop in Linux-Physical-SSD.
All 328 published files matched before launch. Its SOFA and NOVAS libraries use
the same qualified original sources compiled for the VM's glibc 2.35. Root
inspected its actual Camera-page capture and the application was left running
for the user. Evidence is under `current/nina/vm-desktop-launch-20261020-1`.
No hardware or exposure command was issued in this full application session.

The original SimulatorCamera, CameraVM, camera mediator and ImageControlVM
preparation now pass 13 native Linux integration checks with the persisted
processing defaults. A real RANDOM 640x480 monochrome capture was downloaded,
stretched, analyzed and displayed through the original ImageControlView binding.
Annotation off/on/off preserved all raw samples and restored the exact original
stretched display. Camera disconnect, normal window close and cleanup passed.
The random-noise image contained zero detected stars, which is valid. Separate
component checks cover actual star geometry and labels.

Evidence is under `current/nina/image-vm-preparation-20261020-1`, corrected run
`run.UDktsGBp`. The first attempt skipped the original equipment composition
step and stopped before download. The corrected fixture follows the original
initialization order and changes no application code. This integration uses the
existing native view fixture with genuine NINA objects; it does not establish
the full MainWindow capture-button workflow or a full application feature pass.

## Earlier integration sequence

- The next complete publication fixed actual simulator connection through the
  original Camera page. Commit `ec8a250a0` routes CombinedGeometry through the
  existing path conversion. Windows and Linux pass seven focused rendering
  checks, all 59 rendering checks and all 631 geometry checks for that family.
  The native full-app replay then reached Imaging's real docking-layout
  initialization and exposed a hollow dashed-geometry bounds rejection. No
  exposure was requested. Evidence is under
  `current/nina/vm-combined-ui-20261020-1/handoff.json`; this is progress through
  the original application, not a full Imaging workflow pass.
- The latest faulted NINA lifetime on Linux-Physical-SSD was PID 1674980,
  start ticks 2260119 and XID `0x1600006`. It is now stopped with preserved
  profile/layout state. Observe fresh identities and the real session/auth path
  before the next publication.
  The preceding PID 1252728 exited normally with original plugin teardown.
  The subsequent bounds-fault lifetime PID 1368971 and tooltip-animation-fault
  lifetime PID 1492672 required exact-process TERM after both ordinary close
  routes failed. Their state and abnormal exits remain recorded; these are not
  clean shutdown results.
  The subsequent PID 1560085 exited normally and saved its initialized docking
  layout, disconnected the simulator and completed original plugin teardown.
- The VM publication is
  `/home/stefan/wpf-portability-local/phase5/nina-shell-joint-20261021-1`.
  Its close receipts record both failed ordinary close routes and the subsequent
  exact-process TERM. No normal shutdown is claimed for this faulted lifetime.
- Use the full Image3 consumer at
  `current/nina/shell-vm-joint-20261021-1/consumer`. Its manifest and the live
  desktop handoff bind the complete publication, source proofs, launch arguments
  and owned state. Do not substitute the intermediate annotation-only consumer.
- The original full MainWindow now captures and displays a simulator image with
  its processing defaults, finite statistics and histogram. Next repair the
  reached DrawingBrush and saved-layout faults, then verify cancellation and
  recovery and continue the remaining original application workflows.
- General WPF features unused by NINA, native IME and further accessibility
  qualification remain deferred. Vendor/USB drivers and physical equipment are
  excluded from this simulator-only build.

The latest whole framework family passes Imaging 48, focused DrawingImage 3,
full Rendering 62 and Geometry 674 plus focused hollow bounds 44 on Windows and
Linux. Its final intake is
`current/nina-imaging-rendering-integration-20261020-1/final-consumer-intake.json`.
DrawingImage is committed in `02be96020`, cache codecs and transforms in
`9b4fea5aa` and hollow dashed bounds in `73efdfb23`. The six initial strict
bounds comparisons remain preserved as failed evidence. Independent native and
old-backend raster comparisons establish the documented conservative-extent
and Round-stroke differences; native golden vectors remain unchanged.

With this runtime, the original full application now initializes its Imaging
dock, statistics area, Guider/HFR plots and the allowlisted example plugin pane
on the real GNOME/XWayland desktop. Root inspected the native capture. One
original 1-second snapshot was requested with Loop and Save off. A tooltip then
called the unported `SystemParameters.ToolTipAnimation` user32 entry point.
The view remained at image preparation with no completed image or populated
statistics. Snapshot completion, cancellation and recovery are not qualified.
Evidence is under `current/nina/vm-imaging-ui-20261020-1/handoff.json`.

Commit `03edd88ba` applies the existing portable disabled-animation policy to
ToolTipAnimation and ToolTipFade. All 49 targeted native checks pass, including
genuine ToolTip open/close/reopen and popup/window regressions. The subsequent
fresh 329-file full application publication clears that user32 call, initializes
Imaging and starts one original 1-second snapshot. Natural tooltip display then
hits the deliberately unimplemented mouse-relative Popup placement path.
Evidence is under `current/nina/vm-tooltip-ui-20261020-1/handoff.json`. The
latest passive observation shows no further exposure progress and unchanged
logs. Image completion, cancellation and recovery remain unqualified.

Commit `cc34a3290` supplies the current pointer and cursor extent through the
existing private native-window factory. It keeps physical screen coordinates
and the original popup placement algorithm. All 59 selected private native
checks pass, including mouse placement at 96 and 192 DPI. Commit `f0a179f5e`
skips matrix conversion for empty paths; all 680 geometry and 62 rendering
checks pass on Windows and Linux. The complete joint runtime intake is
`current/nina-popup-mouse-empty-geometry-integration-20261020-1/consumer-intake.json`,
SHA-256 `EF8E2D4B562387CC7400A9110197A1069D0FD0195147AD861CDE8FBE5CF99C39`.
Its complete 120-file runtime manifest is
`F9D58F074575455B26653CECFB7426EAA3E5D0403AAF908B919F27E9581E1FD8`.

## Prior faulted native application replay

The fresh shared 329-file publication uses this joint runtime and the unchanged
eight declared application/control adaptations. Its manifest is
`068B97C99731D808B720B5C71EBC7FD01EE3229F817C4AA45873D243A4B6BB22`.
The real GNOME/XWayland run preserves the previous owned profile and layout.
The saved profile had DetectStars disabled; the original UI toggle restored it
before the workload, with AutoStretch enabled, High sensitivity and None noise
reduction. Loop and Save remained off.

The original 1-second simulator snapshot now displays a useful RANDOM 640x480
image through the full application. Root inspected its native capture. The
original Statistics pane reports finite values, including mean 4999.72 and
standard deviation 100.07, and the original histogram displays its narrow raw
16-bit peak. Zero detected stars is valid for this random-noise frame. This is
functional acquisition and processing evidence, not the separate 6000x4000
performance workload.

Two defects remain explicit. Saved-layout deserialization calls the unported
VirtualScreen metrics through AvalonDock. Original NINA catches that error and
loads its own default layout, so the imaging observations above use that actual
fallback. A later redraw rejects DrawingBrush and opens the original error
dialog. The exact brush owner is not proven; the Classic ToolBar thumb is one
concrete source candidate. The dialog holds keyboard focus, so the attempted
10-second exposure edit did not take effect and does not qualify cancellation.
Two additional 1-second captures do not match recorded test submissions; no
human or automation attribution is inferred.

The faulted lifetime has been stopped after preserving its profile, settings,
logs and original failed dock XML. An attempted Alt+N accelerator for the error
dialog's No button and an ordinary main-window close both failed to exit. Native
key submission and focus were verified; No-handler execution was not observed.
An exact-process TERM was
required; no KILL was sent and this does not qualify normal shutdown. The
separate cleanup receipt is
`current/nina/vm-joint-ui-20261021-1/close-handoff.json`, SHA-256
`AE157FF3ED9BFFA08091993888C03751B842C38458E7EDFA191B1840254A1766`.
The original imaging evidence is sealed
under `current/nina/vm-joint-ui-20261021-1/imaging-unhandled-handoff.json`,
SHA-256 `4A288B158C3661CFAD16E28C3E7412F9D31CC18CC60D7D8067700FEFA0E39665`,
and `imaging-evidence-freeze.json`, SHA-256
`BFB693CCE5AB99535780C09C2D85C6EA115A150EA166C81EC6CB300FB60B40DB`.
Cancellation, recovery, actual Framing navigation and restored docking layout
remain unqualified. The next repairs cover required DrawingBrush rendering and
the four virtual-screen metrics, using the existing renderer and startup
settings provider without a new dependency or public API.

Both repairs are now committed and pass the targeted framework qualification.
Commit `138cb5f3c` provides process-wide virtual-screen metrics from the existing
native startup inventory. Commit `953c2be4a` renders DrawingBrush shape fills
through the existing walker, including viewport-origin correction. The final
family passes all 68 rendering checks, including six focused brush cases, and
121 positive settings checks on each OS plus 87 native checks with owned cleanup.
The standalone unregistered ComboBox constructor remains a failed readiness
diagnostic, outside the positive suite. Earlier invalid-cycle and pixel failures
are retained with their exact producing sources.

The final intake is
`current/nina-drawingbrush-virtualscreen-integration-20261021-1/origin-correction/consumer-intake.json`,
SHA-256 `46CC619B0132EED283B5AA1C9AD9B146ED8BD51C52E5BCFC29C2DA72430757B2`.
Its whole 120-file runtime manifest is
`4E804208D2420743C7DF47DEC3DFC44967C6A9558EC82F6CE089ED25006E997A`.
Both PF builds have 23 warnings: the prior 19 and four native cache fields that
the portable getter guards no longer assign. Core, Host and final test builds
have zero warnings and errors. The source-proven new Core reuses the exact new
PF/Host outputs after the brush correction; it does not rebuild NINA.

The fresh shared 329-file application publication is
`current/nina/shell-vm-brush-20261021-1/consumer`, manifest
`70D6BBF844298277EC5FF89DCA6D5E62CAA664929D15CA731E5C3A377542AF36`.
Root independently verified every runtime and publication member. The native
replay uses the preserved original failed layout and profile. At composition,
component passes did not establish native restore, cancellation or normal
shutdown. The subsequent actual results are in the active checkpoint above.

## Accepted cached-image behavior

For NINA's survey cache and thumbnails, use standard area downsampling and
linear enlargement. The user accepted documented differences from Windows WIC's
Fant filter and transfer-table approximation rather than implementing those
tables. This applies to display/cache transforms, not the scientific processing
algorithms or original camera samples. Exact identity and mirror transforms
preserve source samples and format. Fractional Gray16 transforms retain float
precision in Gray32Float, as observed in the Windows reference; they use the
standard IEC sRGB transfer function.

The reference calibration's tiny 5x3 checker differs by up to 31/255 after
downsampling and its ramp by 9/255. These are measured fixture differences, not
global error bounds. Exact mirroring and unchanged raw inputs remain required.
The focused codec/transform implementation passes 48 checks on each OS and both
independent reviews are clear. The original NINA.WPF.Base renderer/cache
candidate passes 42 checks on stock Windows, adapted Windows and Linux. These
include real cache save/load, all three thumbnail sizes, decoded content,
source preservation and renderer/cache eviction ownership. The Base port is
committed in `16e092972` and selected in the current complete VM publication.
Actual Framing interaction has not been attempted there. Its actual Linux
runtime proof and original producer reference family proof remain separate in
`current/nina/wpfbase-framing-20261020-1/fresh-linux42-1/linux42-handoff.json`.

The recovered original Framing18 integration reaches initial view paint, where
an empty Path in a button template exposes a separate framework defect. WPF
layout produces an empty StreamGeometry with NaN translation offsets. Stock
Windows skips the empty geometry; the portable renderer incorrectly converts
its transform. A minimal public-API probe passes all eight cases on stock
Windows and reproduces six failures on the old portable renderer. The proposed
repair skips transforms only when the constructed raster path is empty.
Framing18 now passes all 18 checks on the joint family and shared publication:
`current/nina/framing-replay-20261021-1/framing18-handoff.json`, SHA-256
`24377093792D0151D6D505D3010E8D92BF7A899DF53E231910B7C7B202B52816`.
All 378 input pins remain unchanged and 53 loaded modules match. The original
fixture covers views, zoom, rotation, mosaic/overlap, signed dragging,
owner/background coordinate updates and public disposal. It uses managed
commands and routed clicks on private Xvfb, not native input. Original
constructor background tasks have no public completion contract; process
isolation bounds that remaining lifetime. Actual full-app Framing interaction
and sustained resource qualification remain separate.

The extended Framing21 integration also passes its original 18 checks and three
cache workflow checks on that same shared publication. It saves through the
original VM command, verifies the cache XML, JPEG and 500/150/75-pixel thumbnails,
then reloads the selected cache entry into the real ImageView binding without a
survey request. With saving disabled, a declared field-of-view cache miss makes
exactly one new offline survey request and preserves every existing cache file
and original source sample. Evidence is
`current/nina/framing-cache-workflow-20261021-1/cache21-handoff.json`, SHA-256
`EB1680B739FE43BA4F3D3DE7711AE04D1FE7B68CC89E128865F3E8A8B9A7207B`.
This remains an isolated original-view/VM workflow, not full-app native input.

The committed DrawingBrush repair covers the declared NINA and Classic shape-fill
profiles with TileMode.None or Tile. Outer placement guidelines are included.
The existing managed drawing walker does not apply DrawingGroup.GuidelineSet
inside the source drawing; none of the four inspected brush declarations uses
that property. General source-guideline parity remains later WPF work.

## Accepted focused port

Keep NINA.Image's public interfaces, assembly identity, scientific algorithms
and Windows paths. Adapt these existing ImageAnalysis files:

- `StarAnnotator.cs`: isolate Windows GDI resources and implement functional
  annotations through existing WPF drawing and rendering APIs.
- `ImageUtility.cs`: route scientific image conversion, stretching and debayer
  plumbing through owned buffers and the existing numerical kernels.
- `StarDetection.cs`: use owned buffers for preparation and structure detection
  while retaining the measurement arithmetic.
- `DetectionUtility.cs`: provide the internal buffer resize path.
- `FastGaussianBlur.cs`: provide buffer input/output around the existing blur
  calculation.

One internal buffer owner supplies checked sizes, explicit strides, borrowed
Accord views and deterministic disposal. Keep allocation ownership outside views
that the existing NoBlurCanny implementation disposes. Use provided-destination
filters and the supported in-place threshold operation. No additional library,
native shim or public framework API is planned.

The user explicitly chose to process every real pixel correctly on odd-width
images and document the existing Windows remapping padding bug. Preserve raw
scientific samples. Preserve the original RGB/BGR interpretation, grayscale
coefficients, ushort truncation and annotation's `sample >> 8` background
conversion. Existing accepted rendering differences apply to fonts and edge
rasterization; they do not authorize changing scientific results.

For default Canny, retain the original Gaussian parameters, sigma 1.4 and size 5,
followed by unchanged NoBlurCanny with thresholds 10 and 80. A headless Windows
comparison passed 38 pixel cases plus the constructor/default check with exact
output bytes, including padding and destination initialization. This supports
the composition but does not replace complete star-pipeline verification.
Evidence is under `artifacts/portability/current/nina/canny-composition-probe-1`.

## Implementation and launch sequence

1. Build a source-proven NINA.Image component with functional WPF annotations.
   Verify actual annotation output and initialization against the preserved GDI
   failure. Preserve original sources and explicit adaptation provenance.
2. Use that bounded component for an initial original-shell trial while the
   remaining image buffer paths are implemented. Label this intermediate scope
   explicitly and issue no exposure commands yet.
3. Complete stretching, debayer and detection plumbing. Compare selected
   scientific outputs with stock and existing analytic measurement expectations.
   Check source preservation, cancellation and exceptional buffer cleanup.
4. Run the actual simulator preparation path with its unchanged persisted
   defaults. Exercise annotations enabled and disabled through the real pipeline.
5. Compose one fresh coherent application consumer, retain native MainWindow
   identity and inspect useful captured pixels before normal close. Transfer the
   whole proven publication to a fresh Linux VM directory and launch original
   NINA there.

Use the existing source wrapper, ordinary consumer and isolated shell driver
patterns. Preserve original App/MainWindow resources, real VMs, mediators,
simulator camera, profile/layout state and allowlisted plugin. Equipment vendor
initialization and downloads remain excluded by the accepted offline simulator
qualification build. Do not replace algorithms, reset profile defaults or hide
the annotation failure through a startup-only lazy workaround.

## Completed prerequisites

- Empty notification-window sizing, work-area handling and initial centering
  are committed and targeted Windows/Linux checks pass.
- Original pointer docking passes its declared 10-profile, 140-check execution
  matrix. This is component coverage, not a full shell result.
- Guider22, Sequencer24 and Profile29 pass with explicit source-adapted component
  classification. The Windows-only SystemEvents guards are committed.
- Current clean package consumers and both compiler-origin complete-view
  workflows pass. The already-running final Linux managed regression completed
  18 routes and 1,166 checks; further broad WPF qualification is deferred.
- The fresh packaged WPF test app opened and closed on GNOME/XWayland with exit
  zero. Physical editing and real Orca focus/selection responses were observed.
  The VM has no sound and the second lifetime was canceled when priorities
  changed. No full accessibility pass is claimed. Owned reader processes were
  stopped and settings restored.

Current source, package and workflow evidence stays in the ignored
`artifacts/portability` tree. Earlier failed runs and source families retain their
original scope. Linux ARM64, macOS, full shell workflow qualification and overnight
simulation remain unqualified.

Proposed PR title: `Port WPF to Linux with an owned backend and NINA compatibility`.
