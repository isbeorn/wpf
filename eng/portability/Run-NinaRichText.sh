#!/usr/bin/env bash
set -euo pipefail
set +m
umask 077
ulimit -c 0

if [[ $# -lt 5 || $# -gt 6 ]]; then
    printf '%s\n' 'Usage: Run-NinaRichText.sh repository dotnet PreparedConsumerFreeze.json SHA256 original21|controls10|profiles8 [profile]'
    exit 2
fi
repository=$(realpath -- "$1")
dotnet=$(realpath -- "$2")
freeze=$(realpath -- "$3")
freeze_hash=$4
suite=$5
profile=${6:-}
proof=$(dirname -- "$freeze")
case "$proof/" in "$repository/artifacts/portability/"*) ;; *) exit 2;; esac
[[ "$freeze_hash" =~ ^[A-Fa-f0-9]{64}$ ]]
case "$suite" in
    original21) [[ -z "$profile" ]]; mode=richtext-control; assembly=NinaPortableViews; expected=21;;
    controls10) [[ -z "$profile" ]]; mode=editor-controls; assembly=AvalonEditControlChecks; expected=10;;
    profiles8) [[ "$profile" == -1 || "$profile" == 0 || "$profile" == 80 ]]; mode=caret-profile; assembly=AvalonEditCaretProfiles; expected=8;;
    *) exit 2;;
esac

verify_freeze() {
    python3 - "$repository" "$freeze" "$freeze_hash" "$assembly" <<'PY'
import hashlib,json,pathlib,shutil,sys
root,freeze=map(pathlib.Path,sys.argv[1:3]);expected,assembly=sys.argv[3:]
def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest().upper()
assert sha(freeze)==expected.upper(),'Prepared freeze hash differs'
manifest=json.loads(freeze.read_text())
listed={}
for item in manifest['files']:
    relative=pathlib.PurePosixPath(item['path'])
    assert not relative.is_absolute() and '..' not in relative.parts and '\\' not in str(relative)
    path=(root/relative).resolve();assert path.is_relative_to(root)
    assert str(relative) not in listed,'Duplicate frozen path'
    assert sha(path)==item['sha256'],str(relative)
    listed[str(relative)]=item
output=(root/manifest['consumerOutputDirectory']).resolve()
assert output.is_relative_to(root/'artifacts/portability') and output.is_dir()
assert (output/(assembly+'.dll')).is_file() and (output/(assembly+'.pdb')).is_file()
for path in output.rglob('*'):
    assert not path.is_symlink(),'Runtime links are forbidden'
    if path.is_file():assert path.relative_to(root).as_posix() in listed,'Unfrozen runtime file'
child=root/'src/Portability/Tests/NinaPortableViews/RichText'
provenance=child/'SourceProvenance.json'
assert provenance.relative_to(root).as_posix() in listed,'Source provenance must be frozen'
source=json.loads(provenance.read_text())
for item in source['sources']:
    path=child/item['path']
    assert path.relative_to(root).as_posix() in listed
    assert sha(path)==item['sha256'],'Qualified source changed'
assert sha(child/'RichTextCases.cs')=='435643ACAABBD7E4ED7A66E256519CDEA3C9661E6007D677CC06DD1C3FEDF216'
for relative in ['eng/portability/Run-NinaRichText.sh','src/Portability/Tests/NativeWindowIntegration/X11Fixture.cs']:
    assert relative in listed,'Runner and shared native helper must be frozen'
for path in [root,pathlib.Path('/tmp')]:assert shutil.disk_usage(path).free>=2*1024**3,'Free-space floor'
print(str(output))
PY
}
binary=$(verify_freeze)
mkdir -p "$proof/runs"
run=$(mktemp -d "$proof/runs/$suite${profile:+-$profile}.XXXXXXXX")
mkdir "$run/home" "$run/data" "$run/config" "$run/cache" "$run/xdg-state" "$run/tmp" "$run/cli"
export HOME="$run/home" XDG_DATA_HOME="$run/data" XDG_CONFIG_HOME="$run/config" XDG_CACHE_HOME="$run/cache" XDG_STATE_HOME="$run/xdg-state"
export TMPDIR="$run/tmp" DOTNET_ROOT="${dotnet%/dotnet}" DOTNET_MULTILEVEL_LOOKUP=0
export DOTNET_CLI_HOME="$run/cli" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1
unset WAYLAND_DISPLAY XAUTHORITY DOTNET_STARTUP_HOOKS SESSION_MANAGER AVALONEDIT_BLINK_FIXTURE
unset DBUS_STARTER_ADDRESS DBUS_STARTER_BUS_TYPE DBUS_SESSION_BUS_PID
unset WPF_IBUS_PRIVATE_BUS WPF_ATSPI_PRIVATE_BUS WPF_ATSPI_OBSERVATIONS WPF_FILE_DIALOG_PRIVATE_BUS
unset AVALONIA_IM_MODULE GTK_IM_MODULE QT_IM_MODULE XMODIFIERS IBUS_ADDRESS IBUS_DAEMON_ADDRESS
unset AT_SPI_BUS_ADDRESS AT_SPI_DISPLAY GTK_MODULES GNOME_ACCESSIBILITY QT_ACCESSIBILITY QT_LINUX_ACCESSIBILITY_ALWAYS_ON
unset AVALONIA_SCREEN_SCALE_FACTORS QT_SCALE_FACTOR QT_SCREEN_SCALE_FACTORS QT_AUTO_SCREEN_SCALE_FACTOR QT_USE_PHYSICAL_DPI AVALONIA_USE_PHYSICAL_DPI
runtime_root=
xvfb_pid=; xvfb_start=
child_pid=; child_start=; child_group=
number=
cleanup_failed=0

group_members() {
    ps -eo pid=,ppid=,pgid=,sid=,stat=,comm= | awk -v group="$child_group" '$3 == group'
}
stop_child_group() {
    [[ -n "$child_pid" ]] || return 0
    if [[ -z "$child_group" && -s "$run/child-group" ]]; then read -r child_group child_start < "$run/child-group"; fi
    if [[ -z "$child_group" ]]; then
        if [[ -e "/proc/$child_pid/stat" ]]; then
            [[ -n "$child_start" && $(awk '{print $22}' "/proc/$child_pid/stat") == "$child_start" ]] || return 1
            kill -TERM "$child_pid" 2>/dev/null || true
            for attempt in $(seq 1 50); do kill -0 "$child_pid" 2>/dev/null || break; sleep .1; done
            if kill -0 "$child_pid" 2>/dev/null; then
                [[ $(awk '{print $22}' "/proc/$child_pid/stat") == "$child_start" ]] || return 1
                kill -KILL "$child_pid" 2>/dev/null || true
            fi
        fi
        wait "$child_pid" 2>/dev/null || true
        ! kill -0 "$child_pid" 2>/dev/null
        return
    fi
    [[ "$child_group" =~ ^[1-9][0-9]*$ && "$child_start" =~ ^[0-9]+$ && "$child_group" == "$child_pid" ]] || return 1
    members=$(group_members) || return 1
    printf '%s\n' "$members" > "$run/process-group-before-cleanup.txt"
    if [[ -z "$members" ]]; then
        : > "$run/process-group-after-cleanup.txt"
        ! kill -0 -- "-$child_group" 2>/dev/null
        return
    fi
    [[ -z $(awk -v group="$child_group" '$4 != group' <<< "$members") ]] || return 1
    if [[ -e "/proc/$child_group/stat" && $(awk '{print $22}' "/proc/$child_group/stat") != "$child_start" ]]; then return 1; fi
    kill -TERM -- "-$child_group" 2>/dev/null || true
    for attempt in $(seq 1 50); do
        members=$(group_members) || return 1
        [[ -n "$members" ]] || break
        sleep .1
    done
    if [[ -n "$members" ]]; then kill -KILL -- "-$child_group" 2>/dev/null || true; fi
    for attempt in $(seq 1 50); do
        members=$(group_members) || return 1
        [[ -n "$members" ]] || break
        sleep .1
    done
    printf '%s\n' "$members" > "$run/process-group-after-cleanup.txt"
    [[ -z "$members" ]] && ! kill -0 -- "-$child_group" 2>/dev/null
}
cleanup() {
    stop_child_group || cleanup_failed=1
    if [[ -n "$child_pid" && "$cleanup_failed" == 0 ]]; then wait "$child_pid" 2>/dev/null || true; fi
    if [[ -n "$xvfb_pid" ]]; then
        if [[ -e "/proc/$xvfb_pid/stat" ]]; then
            if [[ $(awk '{print $22}' "/proc/$xvfb_pid/stat") == "$xvfb_start" ]]; then
                kill -TERM "$xvfb_pid" 2>/dev/null || true
                for attempt in $(seq 1 50); do kill -0 "$xvfb_pid" 2>/dev/null || break; sleep .1; done
                if kill -0 "$xvfb_pid" 2>/dev/null; then kill -KILL "$xvfb_pid" 2>/dev/null || true; fi
            else
                cleanup_failed=1
            fi
        fi
        set +e; wait "$xvfb_pid"; server_exit=$?; set -e
        printf '%s\n' "$server_exit" > "$run/xvfb-wait-exit.txt"
        ! kill -0 "$xvfb_pid" 2>/dev/null || cleanup_failed=1
    fi
    if [[ -n "$runtime_root" ]]; then
        if rmdir -- "$runtime_root"; then
            printf '%s\n' 'Owned0700 runtime directory removed.' > "$run/runtime-cleanup.txt"
        else
            printf '%s\n' 'Owned runtime directory is not empty; preserve for diagnosis.' > "$run/runtime-cleanup-failed.txt"
            cleanup_failed=1
        fi
    fi
    if [[ -n "$number" ]]; then
        [[ ! -e "/tmp/.X$number-lock" && ! -e "/tmp/.X11-unix/X$number" ]] || cleanup_failed=1
        if ss -xlH | grep -Eq "(@|[[:space:]])/tmp/\.X11-unix/X${number}([[:space:]]|$)"; then cleanup_failed=1; fi
    fi
}
finish() {
    code=$?
    trap - EXIT
    trap '' INT TERM
    cleanup
    unchanged=true
    verify_freeze > "$run/post-run-runtime.txt" 2> "$run/post-run-freeze.stderr.log" || unchanged=false
    python3 - "$run" "$code" "$cleanup_failed" "$expected" "$unchanged" "$suite" "$profile" <<'PY'
import json,pathlib,sys
run=pathlib.Path(sys.argv[1]);code,cleanup,expected=map(int,sys.argv[2:5]);unchanged=sys.argv[5]=='true'
raw=json.loads((run/'runtime.json').read_text()) if (run/'runtime.json').exists() else None
stderr=(run/'stderr.log').stat().st_size if (run/'stderr.log').exists() else None
passed=sum(item['status']=='pass' for item in raw['checks']) if raw else 0
complete=bool(code==0 and not cleanup and unchanged and stderr==0 and raw and not raw['failed'] and len(raw['checks'])==expected and passed==expected and raw['expectedCheckCount']==expected and not raw['unreachedOrFailedChecks'])
report=dict(childExit=code,deadlineSeconds=45,cleanupVerified=not cleanup,runtimeUnchanged=unchanged,stderrBytes=stderr,passed=passed,expectedChecks=expected,complete=complete,suite=sys.argv[6],profile=sys.argv[7],privateXvfb=True,physicalKeyboard=False,nativeIme=False,rawResult=str(run/'runtime.json'))
(run/'launcher-result.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report));print(str(run))
sys.exit(0 if complete else 1)
PY
    result=$?
    exit "$result"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
runtime_root=$(mktemp -d /tmp/wpf-nina-richtext-runtime.XXXXXXXX)
chmod 0700 "$runtime_root"
[[ $(stat -c %a "$runtime_root") == 700 && $(stat -c %u "$runtime_root") == "$(id -u)" ]]
export XDG_RUNTIME_DIR="$runtime_root"
export DBUS_SESSION_BUS_ADDRESS="unix:path=$runtime_root/absent-session-bus" DBUS_SYSTEM_BUS_ADDRESS="unix:path=$runtime_root/absent-system-bus"
printf '%s\n' "$runtime_root" "$DBUS_SESSION_BUS_ADDRESS" "$DBUS_SYSTEM_BUS_ADDRESS" > "$run/owned-runtime-and-absent-bus.txt"
for candidate in $(seq 1161 1180); do
    [[ ! -e /tmp/.X$candidate-lock && ! -e /tmp/.X11-unix/X$candidate ]] || continue
    if ss -xlH | grep -Eq "(@|[[:space:]])/tmp/\.X11-unix/X${candidate}([[:space:]]|$)"; then continue; fi
    number=$candidate; break
done
[[ -n "$number" ]]
printf '%s\n' "$number" > "$run/display-number"
Xvfb ":$number" -pn -screen 0 1600x1000x24 -nolisten tcp -ac > "$run/xvfb.stdout.log" 2> "$run/xvfb.stderr.log" & xvfb_pid=$!
xvfb_start=$(awk '{print $22}' "/proc/$xvfb_pid/stat")
export DISPLAY=":$number"
ready=0
for attempt in $(seq 1 100); do
    kill -0 "$xvfb_pid"
    if timeout 1 python3 - > "$run/display-readiness.json" 2> "$run/display-readiness.stderr.log" <<'PY'
import ctypes,json,os
x11=ctypes.CDLL("libX11.so.6")
x11.XOpenDisplay.argtypes=[ctypes.c_char_p]
x11.XOpenDisplay.restype=ctypes.c_void_p
x11.XCloseDisplay.argtypes=[ctypes.c_void_p]
display=x11.XOpenDisplay(None)
assert display,"Owned display is not ready"
x11.XCloseDisplay(display)
print(json.dumps(dict(display=os.environ["DISPLAY"],actualXOpenDisplay=True)))
PY
    then ready=1; break; fi
    sleep .05
done
[[ "$ready" == 1 ]]
ps -p "$xvfb_pid" -o pid=,args= > "$run/owned-Xvfb.txt"
export NINA_RICHTEXT_CASE="$mode" NINA_RICHTEXT_ALLOWED_ROOT="$proof" NINA_RICHTEXT_STATE="$run/state" NINA_RICHTEXT_RESULT="$run/runtime.json"
if [[ "$suite" == profiles8 ]]; then export AVALONEDIT_BLINK_FIXTURE="$profile"; fi
cd "$binary"
setsid bash -ec 'read -r -a process_stat < /proc/$$/stat; printf "%s %s\n" "$$" "${process_stat[21]}" > "$1.tmp"; mv -- "$1.tmp" "$1"; shift; exec "$@"' bash "$run/child-group" timeout --signal=TERM --kill-after=3s 45s "$dotnet" "$assembly.dll" > "$run/stdout.log" 2> "$run/stderr.log" & child_pid=$!
if [[ -e "/proc/$child_pid/stat" ]]; then child_start=$(awk '{print $22}' "/proc/$child_pid/stat"); fi
for attempt in $(seq 1 100); do [[ -s "$run/child-group" ]] && break; sleep .01; done
read -r child_group child_start < "$run/child-group"
[[ "$child_group" == "$child_pid" ]]
set +e
wait "$child_pid"
code=$?
set -e
printf '%s\n' "$code" > "$run/exit.txt"
exit "$code"
