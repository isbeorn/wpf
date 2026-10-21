# Windows host incident, 21 October 2026

Status: Windows usability was restored after System Restore. The later registry
snapshot and command comparison identifies our repeated .NET SDK first-run PATH
additions as the strongly supported cause of the startup failures. The approved
cleanup is complete and verified. The user subsequently authorized resuming NINA
work and Windows testing with the safeguards below. Everything remains local.

## Cause and responsibility

Our Windows build lanes used separate `DOTNET_CLI_HOME` directories. The SDK's
first-run setup appended each home's `.dotnet\tools` directory to persistent
`HKCU\Environment\Path`. We did not disable that behavior. Setting environment
variables only in a child process and restoring its PATH afterward did not undo
the SDK's writes to the user registry. This was a toolchain side effect caused
by our test setup, missed by the initial command audit.

The machine PATH was unchanged. The failed user's PATH, combined with the
expanded machine PATH, exceeded Windows' 32,767-character per-variable limit.
This is the limit on an individual variable, not a claimed limit on the total
Unicode environment block.

| Registry state | User PATH characters | Entries | WPF tool-path entries | Reconstructed expanded combined PATH characters |
| --- | ---: | ---: | ---: | ---: |
| Restore checkpoint, snapshot 2 | 24,552 | 246 | 218 | 26,540 |
| Failed state, snapshot 4 | 32,174 | 313 | 283 | 34,162 |
| Working after restore, snapshot 6 | 24,552 | 246 | 218 | 26,540 |
| After approved cleanup | 1,644 | 28 | 0 | Not used as incident evidence |

Snapshot 4's user PATH is exactly the checkpoint string followed by 67 appended
entries. Entry 56 of those additions is the first that crosses the limit:

`E:/Projects/wpf/artifacts/portability/current/linear-gradient-rendering-20261021-1/cli-home-windows\.dotnet\tools`

At that point the raw user PATH was 30,892 characters and the reconstructed
expanded combined PATH was 32,880. The preceding entry left the combined value
just below the limit. The reconstruction uses the unchanged machine PATH,
USERPROFILE and SystemRoot values; it is not a captured environment block from
the failed Task Manager process.

The machine PATH is identical in snapshots 2, 4 and 6: 1,996 raw characters,
last written on 14 October at 22:32:04.776 UTC. The user Environment key in the
failed snapshot was last written on 21 October at 15:04:06.854 UTC. Snapshot 4's
NTUSER hive has unequal header sequences, 432647/432646. It was read directly
without replaying transaction logs, so it is not presented as a fully recovered
registry image. The checkpoint and working hives have matching sequences and
their user PATH values match byte for byte.

### Recorded trigger and reboot behavior

At 08:08:13.234 on 21 October, request
`call_97bcf5350e594b67984e4ceefff5ac10` launched the linear-gradient lane's
`Build-Stock.ps1` and `Build-Red-Windows.ps1 RenderingIntegration`. The retained
scripts select `cli-home` and `cli-home-windows` respectively and invoke the
SDK without the global-tools PATH opt-out. Their SDK 10.0.401 tool-path
sentinels were created at 08:08:14.229 and 08:08:14.395. The second home's tools
directory is the first limit-crossing entry above.

The first retained memory-related service error follows at 08:08:50.427
(CaptureService, System record 445626). Task Manager then fails with
`0xc0000017` at 08:09:31.935 (System record 445635). This connects the retained
SDK commands, their first-run artifacts, the persistent PATH growth and the
first observed failures. The earlier focus on completed rendering probes missed
this preceding SDK setup side effect.

A reboot removed the test processes but reread the oversized persistent PATH.
The failed full boot at 20:38:44 again shows memory-related per-user service
failures from 20:38:58 and Task Manager failing at 20:39:43, without a newly
recorded test launch before them. Shell-Core records repeated taskbar startup
attempts. After System Restore, the 21:19 boot reaches completed taskbar startup
in about 0.4 seconds and Start menu initialization completes. The same memory
and Task Manager errors are absent in the inspected working boot windows,
including the later 21:51 boot.

System Restore restored the earlier user PATH exactly. It also restored other
state, so that observation alone would not isolate the cause. The registry
comparison and matching SDK first-run timeline provide the additional evidence.
There is no failure-time allocation trace or controlled reproduction, and we
will not reproduce the oversized-PATH failure on this host. This conclusion
does not attribute every historical network-port warning or unrelated error to
PATH exhaustion.

Windows documents the per-variable limit in
[Environment Variables](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables).
The SDK documents its automatic PATH behavior and opt-out in
[DOTNET_ADD_GLOBAL_TOOLS_TO_PATH](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_add_global_tools_to_path).
[SDK issue 56512](https://github.com/dotnet/sdk/issues/56512) independently reports
the same CLI-home accumulation mechanism and Windows shell/Task Manager
symptoms. That report supports the mechanism; it is not a substitute for the
local evidence or a maintainer-confirmed diagnosis of this machine.

## Approved cleanup and prevention

Before mutation, the raw user and machine PATH values, registry value kinds,
hashes and exact removal manifest were backed up under
`artifacts/windows-recovery-20261021-1/path-cleanup-20261021-1/backup.json`.
The original global instructions were backed up beside it as `AGENTS.md.before`.

The authorized cleanup removed exactly 218 known WPF-generated tool-path
entries from the restored user PATH. It preserved all 28 retained entries in
their original order and spelling, including existing global tools and nine
older NINA entries whose ownership was not established. The user PATH remains
`REG_EXPAND_SZ`. Machine PATH and all other user and machine environment values
were verified unchanged. Only the approved host-safety section was appended to
the global instructions. No SDK, test or NINA application was launched during
cleanup.

The verification receipt is
`artifacts/windows-recovery-20261021-1/path-cleanup-20261021-1/verification.json`,
completed at 20:45:08.641 UTC on 21 October. SHA-256 values below hash the raw
PATH strings as UTF-16LE:

| Value | SHA-256 |
| --- | --- |
| User PATH before cleanup | `58A2A2AAA852D957B6FC157A12DBF4A3F676BA1550B73A5AFD5D71FAEE6D409B` |
| User PATH after cleanup | `DF896C2617E7A08D187E0BD0FB771C4658B4A016821EA45750E2E1200221BA62` |
| Unchanged machine PATH | `ABFD5CC845DCF50E28A5693D18A66BEFE92BBDDB4C0F40485154ED0DD8DE1EE3` |

Future SDK invocations must set `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` and
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false` in the invocation's process
environment, inherited by children. Do not persist these settings. Windows
toolchain batches must capture raw user and machine PATH before and after and
stop on unexpected changes. Shared and generated portability entry points must
enforce these rules before qualification resumes. A CLI home, private desktop
or temporary directory must never be treated as a host isolation boundary.

The user's subsequent instruction explicitly permits Windows testing again.
It does not authorize installing or changing shared SDKs/runtimes or other host
configuration. The root AGENTS.md records this exception and the safeguards.

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
shared-runtime replacement, explicit persistent User/Machine environment write, boot
change, service installation, global ACL change or registry repair by this chat
or its linked agents. Searches include explicit Windows command routes, managed
registry APIs, symbolic links, encoded-command routes and system paths. Keyword
hits are leads to inspect, not proof of an action. For example, csc.exe matched
the sc.exe search but is not a service-control command.

The SDK's indirect persistent user PATH writes, established above, correct the
initial audit's incomplete conclusion. Searching only for explicit environment
mutation commands was insufficient. Not every historical script body has an
immutable executed copy, and there is no incident dump identifying the failing
allocation. Those limits do not erase the retained PATH and first-run evidence.

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

The later read-only snapshot comparison found the checked Explorer, Task
Manager, Start menu and core Windows binaries identical before and after the
failure. The checked .NET 10.0.13 runtime binaries were present in both failed
and working post-restore snapshots. The Visual Studio update ledger identifies
`VSUpdateHelper.exe`, but retained process-creation evidence does not establish
its parent or who triggered it. These updates are not established as the cause.

The SDK first-run PATH side effect is the supported incident cause. The
approved cleanup and safeguards address that mechanism. NINA work resumes only
under the user's explicit renewed authorization and the updated root AGENTS.md.
