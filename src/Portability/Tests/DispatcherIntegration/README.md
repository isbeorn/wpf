# Source-built Dispatcher integration

This executable references the fork's source-built `WindowsBase` and exercises its
real `DependencyObject`, dependency-property store, `Dispatcher`, operations,
frames, timers and synchronization context. It does not substitute framework types.
Each behavioral case gets its own background thread and a 10-second watchdog.
Failures produce JSON and exit code 1.

## Behavior checklist

- Record the loaded WindowsBase identity, location and module version ID.
- Deliver a real worker PropertyChangedEventManager notification exactly once after contending with event registration, then remove the subscription.
- Unblock contended wrapper reads and writes without pumping queued Dispatcher work or calling the parent synchronization context; preserve recursion and restore the parent after acquisition, release and a failed release.
- Construct a real dependency object, store/read its property and reject foreign-thread mutation.
- Refresh managed PresentationTraceSources and mutate a property afterward.
- Reject null, duplicate, late and recursive pump factories without publishing a partial dispatcher.
- Recover after factory failure, keep registration thread-local and dispose a successfully registered real managed pump at shutdown.
- Preserve priority ordering and equal-priority FIFO; promote and demote an inactive operation.
- Enter/unwind nested frames and execute same-thread Send without pumping pending work.
- Reject PushFrame while processing is disabled, then resume queued work after enabling it.
- Check signaled/timeout waits with processing enabled and disabled, including WaitAny and WaitAll.
- On Windows STA, reject enabled multi-handle WaitAll, permit it inside DisableProcessing and restore enabled behavior afterward; verify WaitAny signals/timeouts without dispatching queued work.
- Cancel pending and already-canceled operations; propagate InvokeAsync task exceptions and deliver BeginInvoke exceptions to the dispatcher handler.
- Start, stop and restart a real DispatcherTimer.
- Wake an idle dispatcher from another thread and execute synchronous Invoke on its owner thread.
- Verify queued and immediate shutdown events, abort pending work and reject later enqueue attempts.

## Implementation boundary

`PORTABLE_WPF` selects the new internal `IDispatcherPump` boundary. The managed
implementation uses an AutoResetEvent for wake/wait and has no native input queue.
The original WPF priority queue, operation invocation, cancellation, exception
handling, timer promotion and shutdown machinery remain authoritative. The managed
pump owns no second operation queue.

The native backend can register `DispatcherPlatform.RegisterPumpFactory` on its
owner thread before that thread creates its first Dispatcher. Registration is
thread-local and consumed once; worker threads retain the managed default. Null,
duplicate, late and recursive registrations fail explicitly. A failed factory does
not publish a partially constructed Dispatcher into the thread map. `Wake` must be
nonblocking and callable across threads; `WaitAndDispatch` runs on the owner thread
and must support nested frames, zero-time polls and bounded waits.

The ordinary production project does not define this symbol, so its existing
message-only HWND, Win32 message loop and native timers remain selected. The
isolated portable WindowsBase project links the new portability source directory.
`DispatcherSynchronizationContext.Wait` uses the CLR wait helper on non-Windows
portable builds. On Windows it preserves the native wait inside DisableProcessing,
avoiding message pumping and permitting multi-handle WaitAll on STA threads.

Native host wake integration, native input deferral, IME/COM pumping and platform
accessibility are outside this fixture. `HasPendingInput` is false in the managed
pump because no native host is attached. Native-host tests must establish behavior
with actual queued native input before claiming desktop event-loop compatibility.
Timer tests use actual elapsed time; they do not force Environment.TickCount rollover.

Native ETW is explicitly disabled in this initial portable build. The disabled
provider reports no enabled events and rejects direct native registration/writes
with PlatformNotSupportedException. It does not pretend to collect ETW events.
Managed PresentationTraceSources remain available, including explicit Refresh;
the Windows-only registry enable switch is skipped on non-Windows portable builds.

## Commands

Set `NUGET_PACKAGES` and `DOTNET_CLI_HOME` to repository-local artifact directories.
Run from this fixture directory so `src/Portability/global.json` selects SDK10:

```powershell
dotnet build DispatcherIntegration.csproj -c Release --artifacts-path E:/Projects/wpf/artifacts/portability/dispatcher-integration
dotnet E:/Projects/wpf/artifacts/portability/dispatcher-integration/bin/DispatcherIntegration/release/DispatcherIntegration.dll
```

For the installed Linux SDK under WSL:

```bash
export NUGET_PACKAGES=/mnt/e/Projects/wpf/artifacts/portability/linux-packages
export DOTNET_CLI_HOME=/mnt/e/Projects/wpf/artifacts/portability/linux-cli-home
cd /mnt/e/Projects/wpf/src/Portability/Tests/DispatcherIntegration
/mnt/e/Projects/wpf/artifacts/portability/dotnet-linux-x64/dotnet run --project DispatcherIntegration.csproj -c Release --artifacts-path /mnt/e/Projects/wpf/artifacts/portability/dispatcher-integration-linux
```

The reference-filter target removes only the implicit framework WindowsBase stub
before reference conflict resolution. The project reference still selects the real
source-built assembly. An extern alias makes the intended compile-time provenance
explicit, while the identity check records the actual runtime-loaded assembly.

Factory tests reach the existing internal backend configuration contract through
reflection. They use the actual managed pump implementation, then exercise public
dispatcher construction, queue operations and shutdown. They add no friend assembly,
fake WPF type or production API for test access.

## Weak-event contention regression

`--weak-event-waits-only` selects the three focused weak-event cases plus assembly identity. Start separate processes with `--weak-event-memory-legacy` and `--weak-event-memory-enabled` to exercise both cached `Switch.MS.Internal.EnableWeakEventMemoryImprovements` modes before first use. The public case uses the actual `PropertyChangedEventManager.AddHandler`, a controlled event-add accessor and a background source notification. It waits for a genuine read waiter before releasing registration, then requires one delivery, working removal and normal dispatcher recovery.

Test-only reflection observes the existing weak table's ReaderWriterLockSlim waiter count and reaches the existing private wrapper for symmetric read/write contention and recursion/release-failure coverage. Actual read/write waiter counts exclude the lock's preliminary SpinWait; thread waiting-state flags are not a portable witness for the native Windows wait. No private production hook or friend access is added. Cleanup/context checks preserve a primary failure. Existing WaitAny/WaitAll, timeout/signal, disabled-queue and Windows STA cases remain the broader wait-semantics checks.

The private non-pumping context retains Win32 waiting on Windows. Only non-Windows `PORTABLE_WPF` uses the CLR `SynchronizationContext.WaitHelper`; the wrapper still installs/restores its existing context and keeps its original recursion policy. A green leaf must load the exact released successor WindowsBase. Compiling tests against new source while executing an old runtime does not qualify the change.

## Verified evidence

The coherent portable10 family passes 14 checks on Windows, including a Windows STA
wait regression. Ubuntu24.04.1 LTS under WSL passes the 13 applicable checks and
explicitly marks Windows STA behavior not applicable. Both builds have zero warnings
and errors.
Both runs loaded the source-built app-local WindowsBase10 assembly with its original
public key token. Captured JSON is under
`artifacts/portability/dispatcher-integration/windows-results.json` and
`artifacts/portability/dispatcher-integration-linux/linux-results.json`.

Earlier Linux runs reproduced real registry and native debugger tracing failures
before the conditional platform guards were applied. The final run includes property
registration and managed trace refresh to exercise those formerly failing paths.
This evidence validates the managed dispatcher slice, not native desktop event-loop
compatibility or completion of the wider framework port.

The Windows STA regression was reproduced before its production repair: a real
two-handle WaitAll inside DisableProcessing threw NotSupportedException in the
portable implementation. `sta-before-repair.json` records that failing run. The
repaired Windows path retains the original native disabled-processing wait while
non-Windows builds use the managed wait helper. No Linux STA support is claimed.
