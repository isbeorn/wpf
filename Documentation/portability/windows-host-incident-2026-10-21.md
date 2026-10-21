# Windows host incident, 21 October 2026

Status: Windows usability was restored according to the user after System
Restore. The exact cause of the failure is still unproven. NINA implementation,
builds and qualification remain paused. Do not resume them on the basis of this
report alone. All future execution belongs in Ubuntu under the root AGENTS.md.

## What the history establishes

The audit reads the original session transcript and 60 linked agent transcripts
created on 18-21 October. It extracted 45,291 distinct recorded tool requests,
including 32,429 execution-related requests. Execution inputs are readable; many
inter-agent messages are encrypted in the local transcripts. The 267 excluded
WPF transcripts identify themselves as guardian reviews rather than descendants
of this chat. No unrelated project's history was included.

The relevant comparison window starts at the available System Restore checkpoint,
20 October at 05:54:29 Europe/Berlin. The earliest retained matching Task Manager
error found in the inspected System log is 21 October at 08:09:31. Requests after
the incident investigation began, 21 October at 20:23, are excluded from the
command index. Times below are local Europe/Berlin time.

| Time | Recorded evidence | What it establishes |
| --- | --- | --- |
| 20 October, 18:07-18:09 | WindowsUpdateClient events 43 and 19 name Visual Studio 2022 Security Update 17.14.42. | Windows records a Visual Studio update transaction. |
| 20 October, 18:09-18:12 | WindowsUpdateClient events 43 and 19 name Visual Studio 2026 Security Update 18.10.4. | A second Visual Studio update transaction occurred. |
| 20 October, 18:11:05 | Application/MsiInstaller record 694422 reports successful Microsoft .NET Runtime 10.0.13 x64 installation. | A system-wide .NET runtime change occurred. |
| 20 October, 18:11:09 | MsiInstaller record 694452 reports successful Microsoft Windows Desktop Runtime 10.0.13 x64 installation. | The system WindowsDesktop runtime changed. |
| 20 October, 18:11:26 | MsiInstaller record 694494 reports successful Microsoft .NET Toolset 10.0.402 x64 installation. | The system SDK/toolset changed too. |
| 20 October, 20:25:19 | Launchpad McQuack request call_eb367c81a3f04523a54a7dd84151e351 and its successful result. | Our private SDK verification used E:/Projects/wpf/artifacts/portability/current/prereqs/sdk-windows-x64-10.0.401 and runtime 10.0.12. It did not target the system installation. |
| 21 October, 02:34:10 | Application/.NET Runtime record 694836 and Application Error record 694837, matched to request call_daf953784517488293272e36b082caea. | Our GeometryIntegration capture crashed because libSkiaSharp or a dependency could not be found. The faulting executable was the project-local SDK's dotnet.exe. |
| 21 October, 02:34:25 | Request call_9d02cf40e2ba4e898d9ae128f18071a6 and its result. | The same capture succeeded after adding the project-local native-assets directory to the child process PATH and restoring PATH in finally. The retry did not install or replace an SDK. |
| 21 October, 04:19:20 | Phineas Flynn request call_f1dae97ae2534527a8a98517394c29f5 and its successful result. | The inspected cleanup removed 28 files, 365,093,296 bytes, from the exact project-local Skia native-assets cache. It checked reparse points and retained inputs. |
| 21 October, 08:09:31 | System/Application Popup 26, record 445635. | Taskmgr.exe failed to initialize with 0xc0000017. The error recurs later. |
| 21 October, 17:31 and 17:45 | The two retained Windows NINA reference runs and matching Jump List file timestamps. | Those runs interacted with real Windows shell state. They happened after the first retained Task Manager failure. |

The installer transaction paths are beneath
C:/ProgramData/Microsoft/VisualStudio/Packages and identify client process 62788
for the inspected Visual Studio 2026 payloads. The audit did not establish that
process's executable or parent from process-creation auditing. The Windows
Update and MSI records support an installer/update origin for the observed
system runtime changes. They do not prove that the update caused 0xc0000017.

## What our recorded actions did

The inspected private-SDK verification commands used an explicit project-local
SDK directory and process-scoped environment variables with restoration in
finally blocks. Other recorded commands invoked the installed dotnet executable
as a build or property-evaluation tool. Invoking that executable does not by
itself establish that its installation was modified. The
first exact-version verification failed under the repository's SDK selection;
the successor ran from a neutral directory and succeeded. Its output identifies
the private SDK and runtime paths. A failed verification is not an installation
of a different system SDK.

### The Skia crash supplied by the user

The supplied exception has the same type, message and stack as the retained
candidate1-windows-capture.stderr. Application Error record 694837 identifies process 51224
(0xC818) and the executable:

E:/Projects/wpf/artifacts/portability/current/prereqs/sdk-windows-x64-10.0.401/dotnet.exe

The exact recorded invocation at 02:34:10 launched GeometryIntegration.dll beneath
the project-local nina-rounded-focus-hidden-position-integration-20261021-1
external-windows output, with --capture-rounded-dashed-bounds. Its retained
runtime configuration requests Microsoft.NETCore.App, not the system
Microsoft.WindowsDesktop.App framework. Its dependency files and output contain
the fork's PresentationCore.dll and SkiaSharp.dll beside the test application.
The retained Core file matches the integration lane's source-proof hash. There
is no per-module load trace for the failed process, so source paths in the
exception are not being treated as module-load paths.

The launch omitted the native-assets search directory. The capture exited with
-532462766 (0xE0434352, an unhandled managed exception). At 02:34:25 the retry
prepended the existing project's runtimes/win-x64/native directory to the child
process PATH and restored it in finally. It exited 0, produced a 4,726,101-byte
capture and had empty stderr. That command contains no SDK/runtime installation,
file replacement or persistent environment change. This establishes a mistake
in our test launch and a narrowly corrected launch, not a repair of Windows.

The Skia failure is distinct from Task Manager's later 0xc0000017 initialization
failure. Neither event alone proves the cause of the other. These observations
were reconstructed from retained logs and files without executing the test again.

The inspected cleanup request resolves its target to an exact directory under
E:/Projects/wpf/artifacts, rejects reparse ancestors and children and checks
retained files. Its successful result names that same directory. This is
evidence for that cleanup's scope, not proof about every historical deletion.

The command scan found no recorded direct system .NET installer invocation,
shared-runtime replacement, persistent User/Machine environment mutation, boot
change, service installation, global ACL change or registry repair by this chat
or its linked agents. Searches include explicit Windows command routes, managed
registry APIs, symbolic links, encoded-command routes and system paths. Keyword
hits are leads to inspect, not proof of an action. For example, csc.exe matched
the sc.exe search but is not a service-control command.

This finding does not establish that executing builds, native libraries or
applications had no indirect effect. Commands can invoke scripts or code with
their own behavior. Not every historical script body has an immutable executed
copy. There is no complete before-and-after system snapshot or incident dump
that identifies the failing allocation.

## Confirmed isolation failure

The original NINA startup called WPF's Jump List APIs even on the private test
desktop. The file a5ad95d589b27636.customDestinations-ms was created at 17:31:03
and updated at 17:45:37. It contains our private dotnet.exe path. Its preserved
SHA-256 is EFA69E64526E23857EAE1434FAEF115B36C33FD073E081CF6C49D624F72F71BE.
The file was removed from active use with verified backups at 20:34.

The same reference runs could access real NINA database, sequence and raw user
configuration paths. Existing database metadata predates the runs but there
were no before hashes, so the report does not claim complete immutability.

Treating a private desktop and selected data-directory redirects as sufficient
host isolation was a mistake. These mechanisms do not isolate Windows shell
integration or arbitrary native/application side effects. The Jump List cannot
explain the earlier matching Task Manager error on the observed timeline.

## Evidence and remaining limit

Local evidence is under
artifacts/windows-recovery-20261021-1/history-audit:

- summary.json, threads.json and tool-calls.jsonl record the extraction scope.
- candidate-index.jsonl records source transcript paths, line numbers and call IDs.
- selected-command-details.json and selected-command-results.json retain the
  inspected private-SDK and cleanup requests and returned results.
- system-dotnet-installer-events.json and visual-studio-update-events.json retain
  the installer and Windows Update events.
- geometry-skia-crash-events.json and geometry-skia-crash-companion-events.json
  retain the matched .NET and Application Error/Windows Error Reporting events.
- geometry-skia-exact-commands.json retains the failed launch, native-path retry
  and correlated outputs. geometry-skia-dependency-evidence.json pins retained
  dependency files, binaries, exit receipts and stderr without loading binaries.
- evidence-manifest.json pins the completed extract and selected evidence.
- coverage-limitations.json records a canceled raw-text fallback search. That
  fallback and the unexecuted process-creation query are not counted as proof.

The successful System Restore changed more than a single runtime. It therefore
does not identify which reverted file or setting restored normal operation.
The history does not currently prove that our work replaced the system .NET
runtime or establish another specific action as the cause. Do not manufacture a
causal conclusion from timing alone and do not reproduce the failure on the
user's Windows host. NINA stays paused until the user accepts a further course
of action.

Proposed PR title: Restrict execution to Ubuntu and record the Windows incident audit.
