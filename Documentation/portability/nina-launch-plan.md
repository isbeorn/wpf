# NINA launch priority, 20 October 2026

The user resumed work and prioritized NINA's required components and a usable
Linux x64 launch. General WPF capabilities that NINA does not currently use,
further accessibility qualification and native IME are later work. The earlier
ordering that deferred NINA.Image until all WPF gates finished is superseded.
Everything remains local on `codex/nina-cross-platform`.

## Current startup boundary

The last original App/MainWindow attempt reached `MainWindow.Show`, then failed
asynchronously when binding metadata initialized StarAnnotator's GDI resources.
There was no retained native MainWindow map or useful pixel capture. The original
failure remains in
`artifacts/portability/current/nina/shell-integration-1/first-run-handoff.json`.

The persisted simulator profile enables AutoStretch and DetectStars, uses High
star sensitivity and None noise reduction and disables annotations. A visible
shell is the first milestone. A default simulator exposure also requires genuine
stretching and detection; the prepared-image display tests do not cover them.

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
