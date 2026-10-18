# Cross platform WPF plan for NINA

Status: accepted design, 18 October 2026. Phases 0 through 5 are authorized for local implementation, with a feasibility report before proceeding beyond the early experiments. Local branches and logical commits are authorized; GitHub writes require separate explicit human confirmation.

Build an open-source WPF implementation that runs NINA's existing WPF views and control libraries on Linux, then extend the same architecture to Linux ARM64 and macOS. Keep WPF's programming model, XAML and managed framework behavior. Use NINA to choose implementation priorities and demonstrate compatibility through real application views.

The first release target is Linux x64 using X11, with XWayland qualified separately. Linux ARM64 follows, then macOS. Native Wayland is a separate backend qualification, not an automatic consequence of running under XWayland.

NINA's device drivers, native camera SDKs and other non-WPF Windows dependencies remain a separate application workstream. Framework qualification uses deterministic services and image data so those dependencies do not prevent progress. Complete NINA application support requires both workstreams to pass.

## Compatibility contract

The implementation must satisfy this checklist for the selected NINA compatibility profile:

- [ ] Existing WPF view source, bindings, commands, styles and templates work with only recorded build or platform-integration adaptations.
- [ ] Existing compiled XAML resources load through `InitializeComponent`, component pack URIs and the WPF BAML loader.
- [ ] The selected third-party controls and plugin fixtures load with one consistent identity for every WPF type.
- [ ] Property changes, validation, routed input, layout and rendering produce observable expected results.
- [ ] Dispatcher priorities, thread affinity, nested frames, dialogs, cancellation and shutdown satisfy the agreed Windows behavior.
- [ ] Scientific image buffers preserve their format, precision, stride and ownership while presentation remains correct.
- [ ] Keyboard, pointer, text editing, focus, capture, popups and the required desktop services work through native platform interactions.
- [ ] Repeated window, view and image lifetimes release resources and an overnight simulated session remains responsive.
- [ ] Existing Windows behavior remains covered throughout the port.
- [ ] The runtime, compiler and dependencies can be built and redistributed using open-source components without an XPF license or service.

Publish separate results for source compatibility, managed binary compatibility, BAML compatibility, behavioral compatibility and platform support. Loading an assembly proves neither its templates nor its interactions work. Rebuilding a control proves neither its original binary nor its original BAML works.

The initial promise is source compatibility for the pinned NINA corpus plus explicitly tested unchanged managed control binaries. Full compatibility with arbitrary WPF applications, third-party binaries or Windows interop is a later objective. Unsupported operations must have explicit diagnostics and ledger entries; silent no-op implementations do not count as support.

## Source baseline and existing constraints

The inspected WPF revision is `65d15adece1df9a73e3d1abc11f8bbf99e9fc7a9`. Its [global.json](../global.json) selects a .NET 12 alpha SDK, [Directory.Build.props](../Directory.Build.props) targets `net11.0` and [Versions.props](../eng/Versions.props) uses assembly major version 12. The [baseline decision and build evidence](portability/runtime-baseline.md) retain this coherent upstream Windows build and select a separate opt-in .NET 10 portable product with assembly version 10 for NINA. Do not infer the baseline from the README.

The candidate public NINA revision is [`4742afae0a09c79be772f23fcf163fb5852d2775`](https://github.com/isbeorn/nina/tree/4742afae0a09c79be772f23fcf163fb5852d2775), whose application targets .NET 10 Windows. A separate local NINA source tree supplied preliminary usage examples and differs from that public revision. Phase 0 must reproduce the inventory against the chosen revision and record dependency versions. Local counts or package versions are not release criteria.

| Existing constraint | Inspected implementation | Planning consequence |
| --- | --- | --- |
| Dispatcher owns a Windows message window and message pump | [Dispatcher.cs](../src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/Dispatcher.cs), including `MessageOnlyHwndWrapper` and `PushFrameImpl` | Preserve WPF scheduling and nested-loop semantics while replacing native wakeups and event pumping. |
| Window creation and dialogs use Win32 infrastructure | [Window.cs](../src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs) and [HwndSource.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndSource.cs) | Define a portable window source and prove modal ownership, activation and focus restoration. Native non-Windows handles are not HWNDs. |
| Visual resources and composition are coupled to DUCE/MIL | [Visual.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Visual.cs), [MediaContext.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs) and [exports.cs](../src/Microsoft.DotNet.Wpf/src/Common/Graphics/exports.cs) | Replacing only the final render target is insufficient. Resource creation, mutation, scheduling and lifetime need a portable implementation. |
| Drawing content has a possible managed traversal seam | [RenderData.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/RenderData.cs), `IDrawingContent.WalkContent` | Prototype this seam against the existing DUCE boundary before choosing how much native composition to replace. |
| Module initialization eagerly enters Windows text services | [ModuleInitializer.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/ModuleInitializer.cs) and [DirectWriteForwarder.vcxproj](../src/Microsoft.DotNet.Wpf/src/DirectWriteForwarder/DirectWriteForwarder.vcxproj) | Audit static initialization and replace the C++/CLI/DirectWrite path, including apparently nonvisual object construction. |
| Text formatting calls native Line Services | [TextFormatterImp.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/TextFormatting/TextFormatterImp.cs) and [LineServices.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/TextFormatting/LineServices.cs) | This checkout imports native text exports whose implementation is absent. Replacing the forwarding assembly alone will not port text layout. |
| PresentationNative is an internal shipping component | [ShippingProjects.props](../eng/WpfArcadeSdk/tools/ShippingProjects.props), [RefAssemblyAttrs.cs](../src/Microsoft.DotNet.Wpf/src/Shared/RefAssemblyAttrs.cs) and the [binary redistribution project](../src/Microsoft.DotNet.Wpf/redist/PresentationNative/PresentationNative.vcxproj) | Existing Windows binaries can support the Windows reference build. Required non-Windows behavior needs an open implementation. |
| Bitmap APIs combine WIC storage with compositor resources | [BitmapSource.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Imaging/BitmapSource.cs) and [WriteableBitmap.cs](../src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Imaging/WriteableBitmap.cs) | Separate pixel ownership and format semantics from display conversion and presentation. |
| SDK targets inject WindowsDesktop framework references | [WindowsDesktop.props](../packaging/Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.props) | Supply a portable build and app-local runtime distribution without requiring `Microsoft.WindowsDesktop.App` on Linux. |
| Framework references can collide with runtime shims | [SdkReferences.targets](../eng/WpfArcadeSdk/tools/SdkReferences.targets) explicitly excludes the runtime's WindowsBase reference | Prove compile-time references and runtime resolution, including strong names and type forwarding, before promising binary compatibility. |

NINA's initial coverage priorities are resources and templates, nested dispatcher frames during profile selection, scientific images, OxyPlot plots, AvalonDock docking, sequencer editing and dynamically loaded plugin dictionaries. The preliminary inventory also finds direct Win32 calls in NINA's main-window hooks and placement code. Record those application call sites separately from missing WPF behavior.

## Proposed architecture

Retain semantic ownership in the current assemblies:

| Owner | Responsibility retained |
| --- | --- |
| WindowsBase | Dependency properties, Freezable behavior, dispatcher contracts, synchronization context and weak events |
| PresentationCore | Visuals, drawing APIs, animation, routed input and image APIs |
| PresentationFramework | Controls, templates, binding, styles, resources, application lifetime and window behavior |
| System.Xaml and WPF markup code | XAML schema, BAML conventions, object construction and generated component hookup |
| Portable adapters | Native windows, event-loop integration, input delivery, rendering surfaces, text services, bitmap storage/codecs and desktop services |

Create small internal boundaries at native call clusters. Keep backend types out of the public WPF API and keep platform branches out of controls and binding code. Preserve the existing Windows implementation as the reference path. Add an abstraction only when an identified native responsibility needs two implementations.

```text
WPF XAML/BAML and application code
    -> existing WPF objects, resources and dependency properties
    -> existing binding, layout and drawing content
    -> portable rendering and text/image adapters
    -> native platform surface

Native events
    -> platform window/input adapter
    -> WPF dispatcher and InputManager
    -> existing routed events, commands and bindings
```

WPF remains the sole owner of WPF operation priorities, thread affinity and frame semantics. A host framework may own the native event loop, but it must not independently redefine when WPF operations run. Native callbacks marshal to the owning dispatcher. A render thread must consume an agreed resource snapshot or retained representation rather than traverse mutable WPF objects from another thread.

Text measurement, line breaking, shaping, fallback, glyph drawing, caret movement and hit testing must share a consistent contract. Evaluate open-source text infrastructure behind WPF's `TextFormatter` behavior, including an alternative to the missing native Line Services implementation. HarfBuzz supplies shaping, not a complete WPF text formatter.

The accepted initial bidi contract follows Unicode 17. The user selected this behavior after a same-font Windows comparison found different legacy WPF bracket ordering in mixed Arabic/Hebrew text. Preserve that difference and measured Display-mode advance differences in the compatibility ledger; do not claim identical native text metrics or legacy ordering.

The user separately selected Unicode 17 character classification for word selection and Ctrl+Arrow navigation. Retain WPF's existing RichEdit-based selection algorithm and Latin table, replace the Windows character-query dependency with open Unicode data and document differences from Windows NLS punctuation, whitespace and East Asian classification. This does not adopt a different general word-breaking algorithm.

Image storage retains original data, including Gray16 and Rgb48. Conversion to a display surface must not discard precision in `CopyPixels` or change channel order. A backend must preserve snapshot and ownership semantics when callers release or reuse source memory, plus the existing freeze and dispatcher handoff behavior.

### Backend selection

Recommend evaluating Avalonia's open-source infrastructure first because it already provides desktop platform integration. This is reuse beneath WPF, with NINA's views remaining WPF. Avalonia is [MIT licensed](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md) and has an existing [layered platform architecture](https://docs.avaloniaui.net/docs/fundamentals/architecture). XPF code and commercial runtime components are excluded.

| Candidate | What to prove | Maintenance cost |
| --- | --- | --- |
| Public Avalonia window/custom rendering host | Native lifetime, input and drawing can support WPF windows, popups and synchronous modal behavior without translating controls | Possible extra host layer and coordination of two framework lifetimes |
| Selected Avalonia platform infrastructure behind owned adapters | Existing window, input and rendering services can satisfy WPF semantics directly | Several backend interfaces are unstable or private; pin a revision and own the adaptation or source fork |
| SkiaSharp with an independently owned platform host | Direct rendering and text integration fit WPF while a suitable host supplies windows and native services | More responsibility for event loops, IME, clipboard, dialogs, accessibility and platform maintenance |

Avalonia's [`IWindowImpl`](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/main/src/Avalonia.Controls/Platform/IWindowImpl.cs) is marked unstable. A publicly visible interface is not necessarily a stable extension contract. Phase 1 must inspect the exact selected revisions and record the API and source-maintenance obligations. [SkiaSharp](https://github.com/mono/SkiaSharp) is a graphics dependency, not a complete desktop backend.

Prefer the highest existing WPF rendering seam that preserves retained behavior and avoids recreating the entire MIL binary protocol. Compare the managed drawing-content path with a bounded DUCE/resource adaptation using the same scene and mutation tests. Choose from measured results; neither approach is yet established as sufficient.

Do not attempt a general Win32 emulation layer. Retain Windows interop signatures where compatibility requires them, keep their Windows behavior and classify non-Windows usage individually. Arbitrary `user32.dll` calls, embedded Win32 controls and COM cannot be made portable by returning a fabricated handle.

The first default-control theme will reuse the complete source-built WPF Classic
theme through the existing ThemeInfo and SystemResources lookup. The unchanged
NINA view diagnostic establishes that the initial generic-only profile cannot
present ordinary UserControl content. Classic is a fixed portable appearance,
with its own qualified resource and chrome closure; it does not promise Aero2
pixels or native Linux theming. NINA's original styles and custom-control generic
dictionaries retain precedence and identity.

The user-approved initial X11 pointer profile preserves positive integer
XSETTINGS double-click values. If `Net/DoubleClickDistance` alone is absent, it
uses a declared portable fallback of 5 raw client pixels on each axis. This is
product policy, not an OS-detected distance. The native GNOME reference publishes
`Net/DoubleClickTime=400` but omits distance; its separate GTK query reports an
effective 5-pixel distance. Published distance values take precedence. Missing
time, absent settings ownership and malformed, duplicate, wrong-type or
nonpositive values still fail explicitly. Validate the complete payload before
resolving absent distance and retain per-field provenance. This policy adds no
GTK runtime dependency or live settings subscription.

## Phased delivery

Every phase produces a runnable artifact or an explicit design decision with evidence. Each gate records passed, failed, blocked and intentionally unsupported cases separately. A build, screenshot or stubbed startup alone does not pass a behavior gate.

### Phase 0 Freeze the target and establish the Windows reference

Run these investigations in parallel:

1. Pin WPF, NINA, .NET SDK/runtime, control packages, source licenses and backend candidate revisions. Recommend aligning the first runtime contract with the pinned NINA runtime where feasible; compare using a matching WPF release baseline with adapting this checkout. Record the decision and a consistent assembly-signing/friend-assembly policy before changing versions.
2. Inventory NINA's WPF API usage, third-party transitive calls, XAML/BAML resources, reflection and plugin surfaces. Combine static inventory with Windows runtime traces and actual view construction. Static reachability is a lower bound.
3. Reproduce a clean Windows WPF build and selected NINA view tests. Record commands, loaded assembly identities, results and artifact hashes. Establish behavior and rendering references.
4. Trace native dependency closure, including PresentationNative, DirectWriteForwarder, System.Windows.Primitives and private WinForms dependencies. Mark components that have no portable source path.
5. Establish Windows and native Linux x64 test hosts, a real X11 session and an interactive Wayland/XWayland session. Use WSL or containers for build and selected CI checks where useful, with desktop qualification on real sessions. Reserve native ARM64 and Mac hosts for later phases and an early main-thread feasibility probe if available.

Deliver a versioned compatibility ledger with feature, owning component, source evidence, calling view/control, target platform, test, status and blocking dependency. Keep NINA application adaptations in a separate column or linked ledger.

**Gate:** the revision/runtime contract is explicit, the reference build and selected views run on Windows and each critical dependency has an implementation or investigation owner. A failed baseline remains a baseline failure, not a port regression. No general-framework implementation starts from unresolved runtime assumptions.

### Phase 1 Prove the hard boundaries and select the backend

Use small disposable experiments with separate ownership and output directories. These are feasibility implementations after design acceptance, not changes made while preparing this plan.

| Experiment | Required evidence |
| --- | --- |
| Assembly and SDK identity | A stock Linux .NET process loads the intended WindowsBase, PresentationCore, PresentationFramework and System.Xaml identities. Record versions, public key tokens, paths, load contexts, type forwarding, `.deps.json` and runtime configuration. Load a separately compiled companion/control assembly and exercise a member across the shared type boundary. Resolve shared-framework and WindowsBase shim conflicts. |
| Markup pipeline | Compile a genuine WPF `x:Class` view and dictionary on a case-sensitive Linux checkout. Exercise generated component hookup, event wiring, a template and component pack resources. Independently load Microsoft-produced BAML fixtures without recompilation. |
| Native event loop | Create a native window, deliver input through WPF and reproduce a nested dispatcher frame, worker-thread invoke, timer, synchronous dialog, cancellation and shutdown. Test `Dispatcher.DisableProcessing`, same-thread `Invoke` at `Send` priority and background scheduling while native input is pending. Compare candidate host strategies against the same tests. |
| Retained rendering | Present a WPF drawing scene with geometry, clipping, transforms, opacity and a mutable bitmap. Change properties, resize, expose/occlude and dispose resources repeatedly. Demonstrate real redraw and resource lifetime through the candidate seam. |
| Text replacement | Format and draw a paragraph through the proposed WPF text path. Exercise wrapping, fallback, bidi, combining characters, caret hit testing and selection metrics. Identify and replace every native Line Services/DirectWrite dependency reached by this slice. |

Build a small app-local SDK/package path first, reusing PresentationBuildTasks and existing WinFX/markup sources where practical. Package the task dependency closure, not just its main DLL. Keep package IDs distinct while deliberately selecting WPF assembly identities. Exclude reference-only assemblies, cycle breakers and Windows native assets from Linux runtime packages. Prove framework-dependent execution first, then self-contained publication; defer trimming, NativeAOT and single-file support until separately qualified. Avoid replacing a machine-wide .NET installation or rewriting the entire upstream build system. Windows cross-compilation can help isolate problems, but does not satisfy native Linux markup compilation.

Prefer correct CPU rendering on the owning UI thread for the first rendering experiment when it simplifies lifetime management. Add render-thread snapshots and acceleration only when required by the measured workload and agreed ownership contract.

**Gate:** select one host strategy, renderer seam and text implementation strategy with evidence from the experiments. Prove a viable identity and distribution path. Record unresolved source-versus-binary limits and any contract changes for design review before expanding the team. A custom .NET runtime or broad assembly renaming, if needed, is a material design decision.

### Phase 2 Deliver a complete portable WPF view

Integrate the selected experiments into one continuously running Linux artifact. Implement only the foundation needed for an unchanged compiled WPF view with a templated button, editable text, a validated binding, a command, an image and a dialog.

Run workstreams for the SDK/markup path, dispatcher/lifetime, native windows/input, retained rendering, text/fonts and image storage in parallel after their boundaries are agreed. Keep one owner for each shared core file and one integration owner for the combined artifact.

Support resource dictionaries, styles, dynamic resource replacement, property metadata, template application, basic animation invalidation, layout and hit testing. Audit eager native initialization so unsupported services are not loaded merely by constructing ordinary controls. Preserve threading, error propagation and explicit unsupported-feature behavior.

**Gate:** a clean Linux machine restores, builds, publishes and runs the view. Native input changes a bound value and invokes a command. Text editing, dialog ownership, image updates, resize, scale changes and close/reopen pass. Microsoft-produced and port-produced BAML both work for the fixture. The selected Windows regression tests pass after the final integration change.

### Phase 3 Qualify NINA views and control libraries

Create a NINA compatibility host that constructs real views and loads their real resources, templates and dependencies. Use dependency injection or existing service seams for deterministic data and simulated services. Record every source adaptation; do not replace a failing WPF control with a different framework control to claim compatibility.

Use these independent view groups to drive the backlog:

| View group | Representative behavior | Main framework coverage |
| --- | --- | --- |
| Guider views and validated settings | Construct GuiderGraph, AnchorableGuiderView and detail/setup views; update plotted data and edit valid/invalid values | Resource dictionaries, templates, plots, binding and validation |
| Profile selection | Choose/cancel a profile, process timers and close while waiting | Nested DispatcherFrame, focus, application state and shutdown |
| Imaging overview | Present deterministic Gray16/Rgb48 images, update pixels, zoom/pan, flip/rotate and update histogram/statistics | Bitmap storage, display conversion, transforms, scrolling, OxyPlot and redraw |
| Docking | Float/redock panes, activate owner/owned windows and save/restore layout | AvalonDock, native windows, mouse capture, geometry and persistence |
| Framing and mosaic | Compose fixed survey images, draw overlays and change scale/rotation | DrawingVisual, TransformedBitmap, WriteableBitmap and RenderTargetBitmap |
| Advanced Sequencer | Add/remove/reorder items, expand/collapse, edit values and execute/cancel a simulation | Routed input, commands, collection views, MultiBinding and template selection |
| Plugin resources and rich text | Load a pinned plugin dictionary and dockable pane; show Markdown and editor content | MEF/resource loading, deferred templates, AvalonEdit and MdXaml |

Upstream NINA's [GuiderChartViewTest](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Test/View/GuiderChartViewTest.cs) and [DirectGuiderDetailViewTest](https://github.com/isbeorn/nina/blob/4742afae0a09c79be772f23fcf163fb5852d2775/NINA.Test/View/DirectGuiderDetailViewTest.cs) provide useful real resource/view construction and binding patterns. Reuse those cases and its process-isolation pattern where applicable. Preserve each project's existing test framework.

Prioritize the actual pinned versions of AvalonDock, OxyPlot, Extended WPF Toolkit and Microsoft.Xaml.Behaviors before less central controls. Eagerly merged application styles mean a library can be required before its screen is opened. Begin with explicit subsets for small fixtures, then require the entire selected startup resource closure at the shell gate.

The preliminary local inventory found no direct application use of ShaderEffect, D3DImage or embedded Win32 host controls. Treat that as a prioritization hint pending Phase 0's transitive and plugin audit. FlowDocument behavior cannot simply be deferred if the selected MdXaml path requires it. WebView2's native browser engine is an explicit application dependency, with any exclusion visible in the profile.

**Gate:** each selected view constructs through its actual compiled initialization path and passes its declared interactions on Windows and Linux. Every selected control library has execution evidence, including template and input paths, plus an explicit source/binary result. Missing views or excluded features are visible in the ledger.

### Phase 4 Integrate the NINA shell

Run the pinned application shell with the complete selected App resource dictionaries, profile workflow, navigation, imaging panels, sequencer and plugin fixture. Exercise theme changes, localization, persisted window/layout state and repeated startup/shutdown.

Keep the application adaptation workstream separate but integrate its required shell changes here. Known examples include direct HwndSource hooks, Win32 window placement, monitor queries and Windows JumpList integration. Decide whether each call needs a small application adaptation, a narrowly scoped WPF interop behavior or a supported-feature exclusion. Arbitrary application P/Invokes are outside WPF API compatibility.

**Gate:** the shell completes the agreed profile, imaging, framing, docking and simulated sequencer workflows repeatedly on Linux. Run an overnight simulated session and retain results for responsiveness, memory, native allocations and thread growth. Distinguish framework failures from application/native-driver blockers. This milestone certifies the defined WPF-dependent workflows; production NINA readiness also needs its separate portability workstream.

### Phase 5 Release the Linux x64 compatibility profile

Qualify supported distributions and desktop sessions, publish dependency/font requirements and produce a redistributable app-local package and SDK. Include clean-machine restore/build/publish/install/run checks and usable crash/assembly/binding diagnostics. Test packaged artifacts, not only development outputs.

Require native keyboard and pointer input, clipboard, file dialogs, focus, window ownership, text composition, desktop scaling and accessibility for the selected profile. Include keyboard-only use and a real screen-reader check for the core workflows. Software rendering must remain a tested fallback; qualify the selected acceleration path on representative hardware.

X11 and XWayland require distinct environment results. Native Wayland can proceed as a separate adapter qualification once the chosen open-source host supports the required semantics. It must pass its own popup, input, clipboard, IME, scaling and ownership cases before being advertised. Do not treat XWayland results as native Wayland support.

**Gate:** the declared Linux x64 matrix passes the compatibility corpus, package checks, native desktop checks and overnight simulation. Known limitations are enumerated. Core supported workflows have no unresolved correctness failures or unmeasured performance blockers. Make no full-WPF-parity claim.

### Phase 6 Qualify Linux ARM64

Reuse the Linux semantics and view corpus. Add ARM64 runtime/native packages, ABI and alignment checks, image-buffer checks and real ARM64 desktop execution. Verify software rendering and the selected GPU path on representative observatory hardware. Set image-size and refresh budgets for the selected hardware rather than inheriting x64 performance expectations.

**Gate:** clean ARM64 build/deployment and the same functional corpus pass on native hardware, including large-image replacement, repeated lifetimes and the simulated overnight session. Emulation and cross-compilation are supporting checks, not this gate's evidence. Certify the available display-server backends independently.

### Phase 7 Qualify macOS

Implement or adapt the macOS host while preserving the established WPF semantics. Schedule native UI work on the required main thread. Qualify Cocoa lifetime, application menus, command modifiers, activation, owned/modal windows, native dialogs, clipboard, IME, accessibility, Retina scaling and multiple monitors.

Start with Apple Silicon; add Intel macOS as a separately tested target when required by the supported runtime/backend matrix. Package the application bundle and validate distribution requirements separately from local development execution. The architecture should receive an early main-thread/modal feasibility probe during Phase 1 where a Mac is available; full platform qualification remains after ARM64 Linux.

**Gate:** the framework corpus and selected NINA views/workflows pass on real supported Macs, including packaged application startup and sustained operation. Linux success does not substitute for these results.

### Phase 8 Expand beyond the first NINA profile

Choose additional WPF consumers and close demonstrated gaps in documents/printing, advanced effects, media, 3D, complex interop and multi-dispatcher configurations. Maintain explicit platform behavior for APIs that fundamentally expose Windows facilities. Extend the compatibility matrix and benchmarks with each new promise.

Run an upstream maintenance lane throughout implementation: periodically merge or cherry-pick relevant fixes, rerun Windows and portable gates and track divergence. Release a profile/version contract so an application can pin a known supported framework/runtime combination.

## Parallel agent execution

Maximize independent ready work, with a single owner of integration and design decisions. Launch additional agents only when a bounded assignment has a stable input contract, distinct file ownership and an observable result. Discovery, library audits, fixture creation and independent review can scale further than changes to the same core scheduler or visual lifecycle.

| Stage | Useful concurrent capacity, including coordination and review | Work assignment |
| --- | --- | --- |
| Phase 0 | About 6 to 10 agents | Independent architecture, implementation and verification leads plus focused NINA, native dependency, build and test inventory tasks |
| Phase 1 | About 7 to 10 agents | Separate owners for identity/SDK, markup, host/event loop, rendering and text experiments, with integration and independent challenge review |
| Phase 2 | About 10 to 12 agents | Component ownership lanes below plus integration and two reviewers |
| Phase 3 | About 12 to 20 agents where file ownership permits | Component owners plus independent control-library/view groups and isolated regression investigations |
| Qualification | About 6 to 10 agents | Native platform sessions, packages, image/performance runs, accessibility, regression triage and review |

These are proposed capacity bands, not a speedup forecast. Use fewer agents when the critical path or available native hosts cannot supply independent work. Expand within available capacity when the ready queue contains additional separable cases. Do not create parallel implementations of the same subsystem merely to keep agents busy.

After Phase 1, use these ownership lanes:

| Lane | Owned responsibility | Dependencies and shared-file rule |
| --- | --- | --- |
| SDK and runtime distribution | Portable references, package identities, dependency graph and clean-machine loading | Owns central build/package files; other lanes request changes |
| Markup and resources | PresentationBuildTasks, BAML, component URIs, dictionaries and satellites | Agrees assembly/resource contracts with SDK owner |
| Dispatcher and application lifetime | Scheduling, frames, timers, synchronization, shutdown and modal coordination | Sole owner of Dispatcher and central lifetime changes |
| Native windows and input | Window adapters, focus, capture, screens/DPI and routed input delivery | Coordinates Window and popup changes with lifetime owner |
| Rendering | Visual invalidation, retained resources, drawing translation, surfaces and render lifetime | Sole owner of Visual, MediaContext and chosen composition seam changes |
| Text | Fonts, line layout, shaping, glyphs, editing metrics and text services | Agrees immutable text/glyph data and draw contracts with renderer |
| Imaging | Pixel storage, codecs, formats, WriteableBitmap and offscreen rendering | Agrees surface/storage lifetime and lock contracts with renderer |
| Desktop services | Dialogs, clipboard, drag/drop, theme/system settings and accessibility adapters | Uses established host contracts; does not introduce another event loop |
| NINA compatibility | Original views, pinned library fixtures, recorded application adaptations and workflow scenarios | Splits into library/view owners only when source files and fixtures do not overlap |

Use isolated worktrees and per-task build outputs for parallel implementation. Establish small reviewed contracts before agents implement opposite sides. Merge small vertical changes continuously into a locally runnable integration branch, with a smoke gate after every merge. Shared generated files have one owner and are regenerated after source integration.

Every implementation assignment includes its allowed files, API/behavior contract, test command or scenario, expected output and dependency revision. The developer owns its production code and associated tests. Reassign or stop work when a shared contract changes; do not merge incompatible adapters and fix the architecture afterwards.

Use three independent planning perspectives for consequential decisions, then independent correctness and maintainability/test-value review for substantial changes. Follow the repository's feature-workflow model policy: Astra medium for architecture, Sol high for implementation/simplicity and behavior planning, Sol high for implementation, Astra high for correctness review and Sol high for maintainability/test-value review. Keep the lead on the selected chat model and disclose unavailable configurations. Delegated agents do not recursively start teams. Select fresh full comic/cartoon character names for new assignments under the existing AGENTS.md preference.

All preparation remains local. Before any push, PR, issue, comment, workflow trigger or other GitHub write, present the concrete repository action and content and obtain explicit human confirmation. That rule applies to every agent and tool route.

## Verification strategy

Reuse existing C# tooling and semantic cases. WPF unit projects already contain xUnit and STA/WPF fixtures, but several target Windows and copy native dependencies. Extract or share appropriate cases in a portable test host instead of blindly retargeting entire projects. Keep NINA's NUnit fixtures in their existing tooling. Reuse existing API compatibility targets and DRT markup cases.

| Behavior or risk | Required observable evidence | Closest meaningful layer |
| --- | --- | --- |
| Assembly identity and runtime shims | Loaded identities/paths, successful companion/control calls, no accidental WindowsDesktop framework dependency | Published process and SDK integration tests |
| API and binary promises | Reference/implementation API comparison plus unchanged selected binaries executing actual members/templates | Existing ApiCompat machinery and runtime fixtures |
| Markup and resources | Both compiler origins load real BAML, dictionaries, templates, events and pack resources | PresentationBuildTasks, DrtXaml and view construction |
| Binding/property/resource behavior | Value propagation, validation, command effects, resource replacement/removal and template hookup | Existing WindowsBase/PresentationFramework cases and real views |
| Dispatcher and lifetime | Priority/order traces, worker and same-thread invoke, processing-disable guards, scheduling with native input pending, exception propagation, cancellation, timers, nested frames and deterministic exit | Existing dispatcher cases plus native-window subprocess tests |
| Rendering and text | Geometry/layout assertions, hit/caret metrics, deterministic pixel readback and reviewed screenshot tolerances | Real renderer with on-screen and offscreen fixtures |
| Scientific images | Exact source values, bit depth, stride, channel order and safe lifetime across replacement/display conversion | Bitmap APIs, actual NINA image paths and visible presentation |
| Native input and services | OS-delivered keyboard, pointer, IME, focus, capture, clipboard and dialog interactions | Real desktop sessions and a small C# input adapter where needed |
| Third-party controls/plugins | Load, template, edit/plot/dock interactions and dynamically added resources | Pinned control assemblies and NINA view/plugin host |
| Leaks and performance | Stable post-warmup resource growth, no retained closed views, bounded allocations and acceptable latency under declared workloads | Repeated lifetimes, representative image runs and overnight simulation |
| Packaged platform support | Clean-machine build/install/run and declared backend/session checks | Native x64, ARM64 and Mac hosts |

Start with a small process-isolated C# integration host and reuse test result formats. DRT's current native input implementation uses user32 and needs a platform adapter. Add a new harness dependency only for a demonstrated gap that the existing tooling cannot cover. Headless tests and Xvfb are useful for CI, but do not qualify physical monitor transitions, IME, accessibility or compositor interactions alone.

Always verify paired operations: open/close, subscribe/unsubscribe, attach/detach, enable/disable, focus gain/loss, capture acquire/release, dock/undock, theme change/restore and image lock/unlock. Exercise cancellation before scheduling, while pending and after completion, plus closing/shutdown during a modal frame.

Image cases include odd widths, padded stride, minimum dimensions, edge dirty regions, invalid rectangles, overflow/truncated buffers, nested locks, freeze behavior and worker-to-dispatcher handoff. Preserve scientific source data while testing the separate display conversion. Text cases include fallback, non-Latin input, combining characters, dead keys, bidi, IME commit/cancel and selection/caret placement. Scale and layout cases run in both directions, including fractional DPI and moving windows between monitors.

Measure Windows references and Linux results on comparable declared hardware with fixed image sizes, plot histories and update rates. Phase 0 records the workloads; Phase 2 sets numerical latency, memory and allocation budgets from those measurements before the shell gate. Label CPU rasterization, GPU paths and warm/cold runs. Avoid invented performance targets or silently treating a growing cache as a leak.

The approved first imaging workload is 6000 x 4000 scientific frames, Gray16 and RGB48 tested separately, at one full-frame update per second with interactive zoom/pan. Record measured reference distributions before setting numerical budgets for that workload.

The final gate for each change includes targeted tests, the relevant broader regressions, build/static checks, affected view instantiation and inspection of the actual diff. Independent reviewers assess correctness, maintainability, useful test coverage and scope. Fix supported findings and rerun affected checks after the last edit.

## Critical path and decisions

The main dependency chain is baseline/runtime identity, then viable text/event/render boundaries, then one integrated WPF view, then NINA control/resource closure, then shell and native platform qualification. Inventories and component work can run in parallel, but additional agents cannot bypass this chain.

Estimate calendar delivery after Phase 1 establishes the implementation seams and after the first few real NINA views establish repair throughput. Plan against measured completed behavior and remaining dependency clusters, not lines changed, total APIs declared or agent count. Re-estimate at each gate and stop broad implementation if the agreed compatibility contract requires a material redesign.

The design review should confirm these recommendations before implementation:

1. Preserve WPF APIs and views, with NINA as the first versioned compatibility profile.
2. Keep the entire new portable implementation and its required runtime dependencies open source; exclude XPF components.
3. Use the accepted platform order: Linux x64, Linux ARM64, then macOS. Start Linux desktop support with X11 and explicitly qualify XWayland/native Wayland.
4. Run the early feasibility experiments before committing to an Avalonia host, private backend APIs, a direct Skia host or a rendering seam.
5. Begin with an app-local portable SDK/runtime path and source compatibility, admitting unchanged control binaries only after concrete tests.
6. Preserve the Windows reference path and run continuous Windows regression gates.
7. Keep NINA's other Windows dependencies separate, while recording the application adaptations needed for each framework integration milestone.
8. Scale parallel agents by independent contracts and file ownership, with one integration owner and independent review.

Current implementation evidence and phase gate status are recorded in the [feasibility results](portability/feasibility-results.md). The gates described here are acceptance requirements, not claims that those phases have passed.
