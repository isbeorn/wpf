# Original NINA anchorables in AvalonDock

This extends the existing NinaPortableViews diagnostic tooling. It creates two
real LayoutAnchorables containing the unchanged ImageView and GuiderGraph with
their 33 original resource dictionaries. GraphSimulation supplies real
GuideStepsHistory data through the original view bindings. No layout document,
replacement view, template or docking model is used.

Prepare the original closure with eng/portability/Run-NinaPortableViews.ps1
-HostProfile PrepareOnly. NinaFixtureDirectory selects that prepared fixture or
an independently rehashed copy. NinaPortableArtifacts must select an owned
absolute output directory. The nested project keeps its bin/obj separate from
the original six-dictionary diagnostic.

Portable builds require NinaSourceStageDirectory to name a complete immutable
root-released stage, including native assets and Classic resources. Rehash its
entire manifest before building and verify the selected runtime copies after
building. Do not copy individual updated framework DLLs into an older closure.
The two native-asset PackageReferences match the existing NativeHost transport
closure and supply ordinary deps.json resolution, without a custom loader.

Example build from src/Portability on Linux:

```sh
dotnet build Tests/NinaPortableViews/AvalonDock/NinaAvalonDock.csproj \
  -p:NinaPortableArtifacts=/absolute/owned/output/ \
  -p:NinaFixtureDirectory=/absolute/verified/original-fixture \
  -p:NinaSourceStageDirectory=/absolute/released-stage/Unix \
  -m:1 -nr:false -p:UseSharedCompilation=false
```

The default AvalonDock reference is the original package binary. To select the
source-adapted component, set AvalonDockComponentPath at build time and
AVALONDOCK_EXPECTED_SHA256 to that frozen DLL hash at runtime. Every other
prepared closure record must still match its original bytes. The executable
checks the loaded AvalonDock identity and hash in the default load context.
Resource and source adaptation claims must remain separate from binary support.

For a portable replay, use a new private Xvfb server and pass --private-xvfb.
X11Fixture refuses an existing XSETTINGS owner. NINA_PORTABLE_VIEWS_STATE must
name a fresh absolute owned state directory. NINA_AVALONDOCK_RESULT_PATH can
select a separate result file so original NINA stdout diagnostics remain raw.
Use an external 45-second watchdog and retain stdout, stderr and exit code.
Do not run this diagnostic against a physical or existing user desktop.

Pointer replays require a 1024x768 logical desktop: use a 1280x960 physical
Xvfb screen at 120 DPI or 2048x1536 at 192 DPI and verify the measured WPF DPI
and native bounds.
Maximized-caption replays require the existing private Openbox profile so the
WM can acknowledge maximization and apply its native geometry before dragging.
Plain Xvfb cannot establish that precondition.

For stock calibration, build with NinaStockWindowsReference=true. Runtime is
stock WindowsDesktop10.0.12 and the child requires --private-noninput-desktop.
It refuses the input or Default desktop before constructing WPF resources.
Use the established reviewed private-desktop launcher with a fresh reviewed
child closure. Building this branch does not authorize executing it or reusing
a previous stock run directory.

The first stock calibration passed nine programmatic checks: real Loaded,
Float/Dock for each pane, resize in both directions, original docked XML restore,
live guide-history mutation and accepted closure. The untouched portable binary
failed in Loaded at the Windows focus hook. The first source adaptation gets
past that hook and reaches the renderer's unsupported dashed pen path. These
failures remain visible. The first eight-patch managed floating presenter now passes the nine existing
programmatic checks against the complete root-released ordinary Release family
from c5c0fe8746dd0fc13ec9f4b84509b0fa3252cb6d. The Debug family retains the
upstream-documented transient event-leapfrogging assertion and its exact abort
artifacts; no assertion or generator code was changed. Post-Settle public
container/item/selection round trips pass after initial layout, each Float/Dock
and XML restoration. Each saved floating Window is invisible and detached from
its PresentationSource after Dock. The portable fixture also verifies achieved
growth/shrinkage against the real source-owned XID, native client pixels,
acknowledged host DIPs, measured DPI, RenderSize and model geometry. This
particular private Xvfb profile requires zero native WM frame extents. Both
original view identities and the graph's simulation DataContext are checked
following docked XML restoration.

The later component7/ordinary fixture8 qualification passes ten private Linux
profiles with 140 check executions. These include native pointer drops for both
original views, overlapping native targets in both orders, nonactivating raises,
caption exclusions, Escape/outside-release/capture-transfer recovery, a two-tab
floating pane and the existing programmatic/XML neighbors. Actual 96, 120 and
192 DPI observations and corresponding physical viewports are retained. Openbox
profiles also verify maximized stationary release and held-motion restoration.

Canceled user Close now preserves the same floating source/model and permits
the next genuine native caption drag/drop. Accepted Close during an active drag
cleans the source, capture, overlays, input handler and native grab. The portable
close-state repair covers the pinned concrete controls and public Closing
handlers. External subclasses that cancel after their base OnClosing call are
outside that qualification; forced shutdown may ignore cancellation.

The focused and remaining-matrix handoffs are retained separately under
`artifacts/portability/current/docking-qualification/fixture8-preparation/`.
The 140 executions include repeated checks across profiles, not 140 distinct
tests. This remains a two-view Xvfb/Openbox result. It does not qualify full
Overview, auto-hide, floating XML restore, hardware, a user desktop, other window
managers or full NINA.

`NINA_FLOATING_TAB_HEADERS_ONLY=1` selects three focused original-style cases:
rendered two-tab titles and manager routes, canceled Close with Hide/Show and a
layout transferred to another real DockingManager. Each starts through the
existing genuine native group-float gesture. The original NINA header binding
must select the current manager's AnchorableHeaderTemplate by Loaded, render
both model Titles and follow later Title changes. Manager input and routed
command handlers must execute once without changing floating focus/capture.
Canceled Close and Hide preserve the live manager bridge; accepted drop/close
removes it. Reparenting checks the current manager and excludes the former one.
The existing programmatic/XML and owner-close neighbors still run, for thirteen
checks in this focused profile. `NINA_POINTER_RECOVERY_ONLY=2` retains its prior
meaning and behavior. Native tests remain private Xvfb/Openbox evidence rather
than full NINA or physical-desktop qualification.
