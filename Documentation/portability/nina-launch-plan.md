# NINA launch priority, 21 October 2026

The user resumed work and prioritized NINA's required components and a usable
Linux x64 launch. General WPF capabilities that NINA does not currently use,
further accessibility qualification and native IME are later work. The earlier
ordering that deferred NINA.Image until all WPF gates finished is superseded.
Everything remains local on `codex/nina-cross-platform`.

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

## Current progress

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

## Resume from this milestone

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

## Latest native application replay

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
logs and original failed dock XML. Both the original error dialog's No action
and an ordinary main-window close failed to exit. An exact-process TERM was
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

The pending DrawingBrush repair covers the declared NINA and Classic shape-fill
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
