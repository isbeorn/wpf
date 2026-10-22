# Actual PresentationSource with a managed contract host

This fixture exercises the source-built PresentationSource, CompositionTarget,
Visual ownership, SourceChanged notifications and retained raster handoff. Its host
is a labelled managed fixture implementation. It proves the Core host contract,
not native window creation, native input or desktop DPI detection.

The first case must run in a fresh process. It sets a root to 2x before any source
exists, verifies independent visuals retain the default 96 DPI, verifies child
inheritance and restores 1x. Source cases cover deferred logical layout callbacks,
1x, 2x and asymmetric physical raster dimensions, ceil rounding, expose,
attachment validation, failure cleanup, root replacement, duplicate ownership,
FromVisual, actual UIElement SourceChanged, borrowed host ownership, repeated
disposal, host reuse, thread affinity and dispatcher shutdown. Each case has a
15-second watchdog. The fixture exits nonzero if any case fails.

Public CompositionTarget root assignment and disposal use the same source-owned
lifecycle. Pixel presentation callbacks may dispose or replace the source root;
ContentRendered is raised only for the target whose frame remains current. The
source also owns one actual typed input provider for both keyboard and mouse,
with one registration that is removed before source teardown. A host without the
optional input capabilities cannot report focus or capture success.

DPI callbacks can replace the root, dispose the source, transfer the previous
root to another source, change client metrics or throw after a nested replacement.
The cases check actual source publication and UIElement visibility, surviving
pixels, current target registration count and the latest independent DPI axes.
Failed raster allocations preserve the previous surface and metrics, leave a
rejected root unpublished and allow the source to recover.
An inner root assignment rejected by validation, a DPI callback or allocation
can be caught by the outer callback without abandoning the valid outer root.
A failed transition cannot reclaim an old root already moved to another source.

Captured host event invocation lists can outlive unsubscription when an earlier
listener closes the source. Metrics, expose and raw-input callback cases dispose
the actual source in that earlier listener, then verify no later frame, source
publication or input registration is created. Closed remains idempotent. Stale
callbacks retain dispatcher affinity, active raw reports retain argument checks
and direct source operations retain their disposed-state exceptions. The input
fixture separately verifies synchronous capture-loss input during disposal while
the source's real provider is still live.

The fixture implements an internal production contract. A fixture-owned standard
MSBuild extension hook adds its friend attribute only to an isolated Core build.
The normal product retains its original friend declarations and adds the production
NativeHost friend, without this fixture friend. These explicit properties are
required; a bare build intentionally has no test access.
No production test identifier or special test property is added to Core.

Run these standalone commands from `E:/Projects/wpf/src/Portability`:

```powershell
dotnet run --project Tests/PresentationSourceIntegration/PresentationSourceIntegration.csproj -p:PortableFrameworkArtifactsDir=E:/Projects/wpf/artifacts/portability/framework-presentation-source/ -p:CustomAfterMicrosoftCommonTargets=E:/Projects/wpf/src/Portability/Tests/PresentationSourceIntegration/CoreTestAccess.targets -p:RestorePackagesPath=E:/Projects/wpf/artifacts/portability/packages/ -p:NuGetAudit=false -m:1 -nodeReuse:false
```

For WSL Ubuntu, run the installed Linux SDK:

```bash
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
cd /mnt/e/Projects/wpf/src/Portability
/mnt/e/Projects/wpf/artifacts/portability/dotnet-linux-x64/dotnet run --project Tests/PresentationSourceIntegration/PresentationSourceIntegration.csproj -p:PortableFrameworkArtifactsDir=/mnt/e/Projects/wpf/artifacts/portability/framework-presentation-source/ -p:CustomAfterMicrosoftCommonTargets=/mnt/e/Projects/wpf/src/Portability/Tests/PresentationSourceIntegration/CoreTestAccess.targets -p:NuGetAudit=false -m:1 -nodeReuse:false
```

Use `dotnet build` with the same properties for a separate build. The executable
is under `artifacts/portability/presentation-source/bin/<OS>/Debug/net10.0/`.
The extra friend is supplied by CoreTestAccess.targets and CoreTestAccess.cs in
this fixture directory. The fixture also public-signs with the original public
key for internal access; this does not establish Microsoft build authenticity.

The fixture reaches real InputManager and UIElement visibility/source notification
paths. It does not synthesize device state to qualify a native input backend.
Loaded/Unloaded behavior belongs to the PresentationFramework Window lifecycle
integration fixture.
