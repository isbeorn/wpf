# Pinned Avalonia.X11 source adaptation

- Repository: https://github.com/AvaloniaUI/Avalonia
- Version/tag: `12.1.4`
- Commit: `2d79ba55fae1b1e71c69f93a668080c9029e6a31`
- Archive: https://codeload.github.com/AvaloniaUI/Avalonia/tar.gz/2d79ba55fae1b1e71c69f93a668080c9029e6a31
- Archive SHA-256: `9E9EE6EE270BFDC02FBC34296DA51EE6242325156D2B67CBED92D2CCBC2E2029`
- License: MIT, preserved in `LICENSE.md` and copied beside build outputs.

`../Prepare-AvaloniaSource.ps1` verifies the archive before replacing the generated extraction under `artifacts/portability/native-host/upstream`. The complete upstream checkout remains ignored build input. Its 83 original X11 source files remain untouched. The project compiles 82 unchanged files, one verified derivative of `GlibDispatcherImplBase.cs`, original shared raw-event grouping code and unchanged source generators. The owned maintenance surface is `ExternalDispatcherBridge.cs` plus the single source patch `Patches/GuardBackgroundCallback.patch`.

Every build verifies the original GLib dispatcher file SHA-256 `5070CE8FC7CD809B06C4B424CD1330F7D33CC0CCC36A48303E88B25B37922965`, then replays the one exact patch hunk into `.portable-derived/<OS>/GlibDispatcherImplBase.cs`. The derivative is separate from the original file and remains under the upstream editorconfig tree so its analyzer policy is preserved. The original source and verified archive are never patched. The derivative's resulting SHA-256 is `A5057CE83D488795BD8A37106847B46C3F14B79133A19EBFF9F1C386208FA2DC`.

The new typed private-API facade returns Avalonia's existing `ManagedDispatcherImpl.IManagedDispatcherInputProvider` interface over its actual Xlib event dispatcher and manual grouped input queue. Its small provider delegates pending Xlib events to the original `X11EventDispatcher.DispatchX11Events`, then dispatches at most one already-grouped input through the existing manual provider. The owned WPF adapter calls it before waiting for more OS events. It creates no queue, translates no WPF priorities and changes no Avalonia application behavior when unused.

The first native regression requiring this seam enters a WPF nested frame from an actual XTest-delivered KeyDown. Avalonia's GLib socket callback has already drained KeyUp into the manual managed queue, leaving no readable X11 socket. A bare GLib iteration cannot dispatch that queued release until the outer callback returns. `Dispatcher.RunJobs` also cannot help: the manual queue's `Add` only enqueues; the separate automatic queue implementation posts dispatcher jobs. The manual queue removes each item before invoking its callback and has no recursion guard, so the existing provider can safely dispatch the next item on the same owner thread.

The second regression occurs before grouping: `HasPendingInput` calls `XPending`, which can read socket bytes into Xlib's own event queue. The GLib file descriptor source may then be non-readable even though Xlib already holds the key events. Delegating that existing native queue to its original event dispatcher closes this gap. The integration runner repeatedly probes pending native input before dispatch to verify that querying does not strand it.

The third regression concerns the native C callback boundary. The upstream `RequestBackgroundProcessing` idle callback invokes `ReadyForBackgroundProcessing` without the exception guard used by its signal and timer callbacks. A thrown Avalonia Background operation therefore aborted the native process with exit 134 instead of reaching the WPF frame's managed catch. The one source patch catches that callback's exception and routes it through the existing `HandleException` path. The configured external logger can then capture it and the owned pump rethrows after `g_main_context_iteration` returns. Other callback behavior is unchanged. Separate signal and Background tests require both the native callback and the managed rethrow in the exception stack and verify subsequent Avalonia/WPF dispatcher work. The direct managed input-provider exception case remains distinct.

The rebuilt assembly retains `Avalonia.X11, Version=12.1.4.0` with the original `c8d484a7012f9a8b` public signing identity. Its informational version explicitly identifies this source adaptation and upstream commit. Public signing preserves identity for existing friend access; it is not an upstream signature. The original public key is read from the verified upstream `build/AvaloniaPublicKey.props`. No metadata, assembly references or package DLLs are patched.

Runtime dependencies use ordinary project/package references:

| Assembly | Source |
| --- | --- |
| WindowsBase and its framework dependencies | Existing source-built portable WPF projects |
| Wpf.Portable.NativeHost | Owned native pump adapter |
| Avalonia.X11 | This source-built project, selected by the dependency graph instead of the package X11 binary |
| Avalonia, Avalonia.Skia, Avalonia.FreeDesktop and Avalonia.FreeDesktop.AtSpi | Exact NuGet 12.1.4 dependencies with lock files |

The unchanged upstream development generators use the upstream-pinned Roslyn `Microsoft.CodeAnalysis.CSharp` 4.14.0. Their separate build project compiles only the upstream generator sources and shared attribute support. Both Windows and Linux outputs remain under separate artifact directories.

The signing key and derived source are written only when their bytes differ. Verified second builds on Windows and Linux skip `CoreCompile` and leave both files and the compiled X11 assembly unchanged. This avoids recompiling all upstream sources on an ordinary no-change build.

`AVA3001` intentionally reports the unstable private API dependency. `CS0649` also remains visible for the unchanged upstream `X11Screens.MonitorInfo.RefreshRate` field. Only that source project permits the existing warning without treating it as an error; owned adapter and test source retain warnings-as-errors.
