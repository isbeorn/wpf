# NINA launch priority, 21 October 2026

The user resumed work and prioritized NINA's required components and a usable
Linux x64 launch. General WPF capabilities that NINA does not currently use,
further accessibility qualification and native IME are later work. The earlier
ordering that deferred NINA.Image until all WPF gates finished is superseded.
Everything remains local on `codex/nina-cross-platform`.

## Active checkpoint

The latest 52E/549D runtime has completed native simulator capture, cancellation,
recovery and clean close/relaunch. Offline Framing loads, zooms and pans. The
application is now closed after a rounded dashed Slider focus visual exposed a
new hit-test bounds rejection. A hidden floating pane also changes its saved
Top by one caption height. Those two WPF repairs precede the remaining Framing
and sequence checks. The detailed receipts and final restart baseline follow.

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
- [ ] Preserve floating-window position after hiding its page and saving.
- [ ] Operate the actual offline Framing map and its input controls.
- [ ] Add and edit sequence instructions, complete a sequence, cancel a running
  sequence and complete it again after reset.
- [ ] Complete and cancel the original sequence drag operation with its blur.
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
The final closed state is `postclose-rounded-dash-20261021-1` in the guest
publication. Its profile SHA-256 is
`8C025CDAC9959624006BB15636CC9E62F90428D1EE442389FA44EC1B84117A7D`
and dock SHA-256 is
`290D7343F3157CD882ADB004BF2DD9B624E7CF7DE1E0BD8EE19F6053A648942A`.
Use this final baseline next: Statistics 1370x800 at 1074,516, DetectStars and
AutoStretch enabled, High/None, 1-second exposure, Loop/Save off and SKYATLAS.
Saved LastRotationAngle is 330; the failed attempt to return the UI to zero did
not apply.

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
all applicable notices. An isolated C-only, reentrant source-build feasibility
experiment on Ubuntu 22.04/glibc 2.35 is authorized; no managed adaptation,
consumer publication or scientific roundtrip is qualified yet.

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
