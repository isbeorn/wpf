# Source-built PresentationCore integration

This executable uses the actual PresentationCore source manifest, source-built
WindowsBase, System.Xaml and UIAutomation contracts. It records and walks genuine
DrawingVisual RenderData through the portable Skia CPU target. It introduces no
replacement WPF public types or test-only framework API.

`PORTABLE_WPF` is an explicit backend selection on Windows and Linux. The ordinary
upstream Windows project does not define it. The portable module initializer skips
eager DPI and DirectWrite initialization, while MediaContext uses the Stopwatch
clock and the existing Dispatcher queue without native notification windows or
DUCE channel startup. Native DirectWriteForwarder metadata is referenced only for
the source closure's inactive native paths. It is not copied to the portable
runtime output.

The portable scheduler registers internal render targets, queues one Render-priority
operation and retains work requested during a render for the following pass.
Visual invalidation notifies registered ancestors before native dirty-flag early
outs. The existing precompute flags remain dirty until actual bounds computation
updates their caches. DrawingVisual RenderOpen, RenderClose and resource change
subscriptions use their original implementations.

The existing composition lock contract uses a global reentrant managed monitor in
the portable backend. Generated drawing-property callbacks retain their original
Acquire/Dispose scopes. Child attachment inherits managed DPI flags and host scale
entries without querying HwndTarget. Until a host provides scale entries, GetDpi
returns 96 DPI. Physical monitor detection and DPI transitions remain unqualified.
The Windows IMM capability probe only runs on Windows in portable builds; this is
not an implementation of non-Windows input methods.

The fixture checks source assembly identity/location, empty construction, retained
rectangle and brush identity, mutable brushes, replacement/removal, coalescing,
second and third frames, old brush unsubscription, mutation during Rendering and
shutdown from Rendering. Its watchdog exits nonzero after 20 seconds. Reflection
only reaches the existing internal MediaContext event and portable render target;
rendered pixel assertions exercise their actual behavior.

Run from `src/Portability`, with the package cache in the repository's ignored
artifacts directory:

```powershell
dotnet run --project Tests/PresentationCoreIntegration/PresentationCoreIntegration.csproj -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false
```

For the installed WSL SDK:

```bash
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
cd /mnt/e/Projects/wpf/src/Portability
/mnt/e/Projects/wpf/artifacts/portability/dotnet-linux-x64/dotnet run --project Tests/PresentationCoreIntegration/PresentationCoreIntegration.csproj -p:NuGetAudit=false
```

This establishes the retained CPU drawing path. It does not establish framework
control layout, native input, accessibility, native window presentation, full text
layout or NINA compatibility. RenderingIntegration and TextIntegration cover their
separate supported operations and explicit unsupported cases.
