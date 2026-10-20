# NINA launch priority, 20 October 2026

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
- Inspect the existing NINA window in VMConnect for Linux-Physical-SSD. The
  replacement lifetime is PID 1368971, start ticks 1845563 and XID `0x1600006`.
  Refresh these identities and the real session/auth path before acting later.
  The preceding PID 1252728 exited normally with original plugin teardown.
- The VM publication is
  `/home/stefan/wpf-portability-local/phase5/nina-shell-combined-20261020-1`.
  Its `close-owned.py` checks the recorded process and window identities before
  requesting normal window close. It has been prepared but not executed.
- Use the full Image3 consumer at
  `current/nina/shell-vm-combined-20261020-1/consumer`. Its manifest and the live
  desktop handoff bind the complete publication, source proofs, launch arguments
  and owned state. Do not substitute the intermediate annotation-only consumer.
- Next verify a simulator exposure through the actual full MainWindow UI, then
  qualify the NINA pages and interactions required for the initial target. The
  existing default preparation fixture is now a passing diagnostic baseline.
  Framing/Sky Atlas image drawing and other remaining application dependencies
  need their own evidence before a full NINA compatibility claim.
- General WPF features unused by NINA, native IME and further accessibility
  qualification remain deferred. Vendor/USB drivers and physical equipment are
  excluded from this simulator-only build.

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
The focused codec/transform candidate passes 45 Windows checks. Its independent
review, Linux family qualification and real cached-survey integration remain
pending. The associated original NINA.WPF.Base renderer/cache candidate passes
40 Windows checks; nine of its Linux checks still require the new DrawingImage
and file/JPEG framework paths. These candidates are not in the live publication.

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
