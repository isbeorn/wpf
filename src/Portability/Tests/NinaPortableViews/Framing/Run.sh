#!/usr/bin/env bash
set -euo pipefail
umask 077
ulimit -c 0
script=$(realpath -- "$0")
if [[ ${1:-} != child ]]; then
  [[ $# == 5 && $1 == --execute-reviewed && $5 =~ ^[A-Fa-f0-9]{64}$ ]]
  repo=$(realpath -- "$2");sdk=$(realpath -- "$3");freeze=$(realpath -- "$4");expected=$5
  mapfile -t prepared < <(python3 - "$repo" "$sdk" "$freeze" "$expected" <<'PY'
import hashlib,json,pathlib,sys
repo,sdk,freeze=map(pathlib.Path,sys.argv[1:4]);sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
assert sha(freeze)==sys.argv[4].upper(),'Prepared freeze differs'
p=json.loads(freeze.read_text(encoding='utf-8-sig'))
assert p['status']=='prepared-not-executed' and p['expectedChecks']==21
assert pathlib.Path(p['repository'])==repo and pathlib.Path(p['linuxSdkDirectory'])==sdk
base=pathlib.Path(p['outputDirectory']);assert base.is_relative_to(repo/'artifacts/portability')
for row in p['files']:assert sha(pathlib.Path(row['path']))==row['sha256'],row['path']
print(base)
PY
)
  [[ ${#prepared[@]} == 1 ]]
  base=${prepared[0]}
  [[ ! -e "$base/execution-started.json" ]]
  printf '%s\n' '{"attempt":1,"scope":"Original Framing18 plus3 actual VM cache checks, private Xvfb"}' > "$base/execution-started.json"
  mkdir -p "$base/isolated-runs"
  run=$(mktemp -d "$base/isolated-runs/run.XXXXXXXX")
  owned=$(mktemp -d /tmp/wpf-nina-framing.XXXXXXXX)
  printf '%s\n' "$run" > "$base/last-run-path.txt"
  printf '%s\n' "$owned" > "$run/native-root.txt"
  set +e
  timeout --signal=TERM --kill-after=5s 115s unshare -Urn -- /bin/bash "$script" child "$repo" "$sdk" "$freeze" "$run" "$owned" > "$run/launcher.stdout" 2> "$run/launcher.stderr"
  status=$?
  set -e
  printf '%s\n' "$status" > "$run/launcher.exit"
  if [[ -d "$owned/state" ]]; then cp -a "$owned/state" "$run/preserved-state"; fi
  python3 - "$freeze" "$run" "$status" <<'PY'
import hashlib,json,pathlib,sys
freeze,run=map(pathlib.Path,sys.argv[1:3]);sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
p=json.loads(freeze.read_text(encoding='utf-8-sig'));errors=[]
known={r['path']:r['sha256'] for r in p['files']}
for path,expected in known.items():
    if sha(pathlib.Path(path))!=expected:errors.append(path)
report=json.loads((run/'runtime.json').read_text()) if (run/'runtime.json').is_file() else {}
loaded=[];bcl=[];generated=[];seen=set()
sdk_input=pathlib.Path(p['linuxSdkDirectory']);sdk_root=sdk_input.resolve(strict=True)
shared=(sdk_root/'shared/Microsoft.NETCore.App/10.0.12').resolve(strict=True)
expected_bcl={f.resolve(strict=True):f.stem for f in shared.rglob('*.dll') if f.is_file()}
consumer_root=pathlib.Path(p['consumerDirectory']).resolve(strict=True)
entry_root=pathlib.Path(p['entryDirectory']).resolve(strict=True)
expected_generated={
    'Anonymously Hosted DynamicMethods Assembly, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null',
    'ProxyBuilder, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null',
    'EntityFrameworkDynamicProxies-NINA.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'}
events=[(r,True) for r in report.get('loads',[])]+[(r,False) for r in report.get('observations',[]) if 'runtime' in r]
for row,is_event in events:
    path=row.get('path');identity=row.get('identity',row.get('runtime'));context=row.get('context')
    if (is_event and context!='Default') or (not is_event and context not in (None,'Default')):
        errors.append('load context:'+str(identity));continue
    if not path:
        if is_event and identity in expected_generated:
            generated.append(dict(identity=identity,context=context,classification='known generated assembly; no PE location or hash'))
        else:errors.append('unknown empty load:'+str(identity))
        continue
    location=pathlib.Path(path).resolve(strict=True)
    if location.is_relative_to(consumer_root) or location.is_relative_to(entry_root):
        if path not in known or sha(location)!=known[path]:
            errors.append('unpinned or changed consumer/entry load:'+path);continue
        evidence=loaded;digest=known[path]
    elif location.is_relative_to(shared) and location in expected_bcl and identity and identity.split(',')[0]==expected_bcl[location]:
        evidence=bcl;digest=sha(location)
    else:
        errors.append('unknown nonframework load:'+path);continue
    if path in seen:continue
    seen.add(path)
    evidence.append(dict(path=path,realpath=str(location),sha256=digest,context=context,identity=identity))
consumer=pathlib.Path(p['consumerDirectory'])
for name in ['WindowsBase','System.Xaml','PresentationCore','PresentationFramework','Wpf.Portable.NativeHost','NINA','NINA.Core','NINA.Image','NINA.Astrometry','NINA.WPF.Base']:
    if str(consumer/(name+'.dll')) not in seen:errors.append('missing loaded:'+name)
continuity=dict(unchanged=not errors,errors=errors,files=len(known),loadedPinnedMembers=loaded,selectedSdkRootInput=str(sdk_input),selectedSdkRootRealpath=str(sdk_root),selectedSharedFrameworkRealpath=str(shared),sdkBclHashPolicy='Observed hashes of exact selected shared-framework files; not consumer input pins',loadedSdkBcl=bcl,generatedUnhashedLoads=generated)
(run/'input-continuity.json').write_text(json.dumps(continuity,indent=2))
assert not errors,errors
source=json.loads(pathlib.Path(p['sourceProvenance']).read_text(encoding='utf-8-sig'))
checks=report.get('checks',[])
assert int(sys.argv[3])==0 and not report.get('failed',True),report.get('error','Fixture did not complete')
assert [r['name'] for r in checks]==source['executionCheckNames'] and len(checks)==21 and all(r['status']=='pass' for r in checks)
assert not report.get('dependencyFailures',[])
assert (run/'stderr.log').stat().st_size==0
cleanup=json.loads((run/'owned-cleanup.json').read_text());assert cleanup['cleanupFailed']==0
(run/'qualification-summary.json').write_text(json.dumps(dict(status='PASS',checks=21,scope=source['scope'],runtimeIntakeSha256=p['runtimeIntakeSha256'],actualRuntimeFamilySha256=p['frameworkManifestSha256'],inputFiles=len(known),loadedPinnedMembers=len(loaded),fixtureRebuiltForRecipe=True,consumerCopies=0),indent=2))
PY
  printf 'run=%s status=%s\n' "$run" "$status"
  exit "$status"
fi
[[ $# == 6 ]]
repo=$2;sdk=$3;freeze=$4;run=$5;owned=$6
mapfile -t prepared < <(python3 - "$freeze" <<'PY'
import json,pathlib,sys
p=json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
for key in ['outputDirectory','consumerDirectory','entryDirectory','inputPins','inputPinsSha256']:print(p[key])
PY
)
[[ ${#prepared[@]} == 5 ]]
base=${prepared[0]};consumer=${prepared[1]};entry=${prepared[2]};input=${prepared[3]};input_hash=${prepared[4]}
[[ "$run" == "$base"/isolated-runs/run.* && "$owned" == /tmp/wpf-nina-framing.* ]]
mkdir -p "$owned"/{home,data,config,cache,state,tmp,cli,runtime}
export HOME="$owned/home" XDG_DATA_HOME="$owned/data" XDG_CONFIG_HOME="$owned/config" XDG_CACHE_HOME="$owned/cache" XDG_STATE_HOME="$owned/state" TMPDIR="$owned/tmp" XDG_RUNTIME_DIR="$owned/runtime" DOTNET_CLI_HOME="$owned/cli"
export DOTNET_ROOT="$sdk" DOTNET_MULTILEVEL_LOOKUP=0 DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset WAYLAND_DISPLAY XAUTHORITY DOTNET_STARTUP_HOOKS SESSION_MANAGER DBUS_STARTER_ADDRESS DBUS_STARTER_BUS_TYPE AT_SPI_BUS_ADDRESS WPF_ATSPI_PRIVATE_BUS WPF_FILE_DIALOG_PRIVATE_BUS WPF_ATSPI_OBSERVATIONS IBUS_ADDRESS
export DBUS_SYSTEM_BUS_ADDRESS="unix:path=$owned/absent-system-bus"
export NINA_FRAMING_STATE="$owned/state/NINA" NINA_FRAMING_RESULT="$run/runtime.json" NINA_FRAMING_ALLOWED_ROOT="$owned/state" NINA_FRAMING_PREPARATION="$base"
export LD_LIBRARY_PATH="$consumer/runtimes/linux-x64/native"
xvfb_pid=
number=
cleanup() {
  status=$?
  trap - EXIT
  if [[ -n "$xvfb_pid" ]]; then kill -TERM "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; fi
  cleanup_failed=0
  if [[ -n "$number" && ( -e /tmp/.X${number}-lock || -e /tmp/.X11-unix/X${number} ) ]]; then cleanup_failed=1; fi
  printf '{"fixtureExit":%s,"xvfbPid":"%s","display":":%s","cleanupFailed":%s,"ownedRootPreserved":true}\n' "$status" "$xvfb_pid" "$number" "$cleanup_failed" > "$run/owned-cleanup.json"
  [[ "$cleanup_failed" == 0 ]] || exit 91
  exit "$status"
}
trap cleanup EXIT
for candidate in $(seq 1501 1540); do
  [[ ! -e /tmp/.X${candidate}-lock && ! -e /tmp/.X11-unix/X${candidate} ]] || continue
  if ss -xlH | grep -Eq "(@|[[:space:]])/tmp/\.X11-unix/X${candidate}([[:space:]]|$)"; then continue; fi
  number=$candidate;break
done
[[ -n "$number" ]]
export DISPLAY=:$number NINA_FRAMING_PRIVATE_DISPLAY=:$number
Xvfb "$DISPLAY" -pn -screen 0 1600x1000x24 -nolisten tcp -ac > "$run/xvfb.stdout" 2> "$run/xvfb.stderr" & xvfb_pid=$!
ready=0
for attempt in $(seq 1 100); do
  kill -0 "$xvfb_pid"
  if python3 - <<'PY' > "$run/display-readiness.log" 2>&1
import ctypes
x=ctypes.CDLL('libX11.so.6');x.XOpenDisplay.restype=ctypes.c_void_p;x.XCloseDisplay.argtypes=[ctypes.c_void_p]
d=x.XOpenDisplay(None)
if not d:raise SystemExit(1)
x.XCloseDisplay(d)
PY
  then ready=1;break;fi
  sleep .05
done
[[ "$ready" == 1 ]]
cat > "$owned/private-bus.conf" <<'BUS'
<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow user="*"/><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>
BUS
cd "$consumer"
timeout --signal=TERM --kill-after=3s 100s dbus-run-session --config-file="$owned/private-bus.conf" -- "$sdk/dotnet" exec --depsfile "$consumer/NINA.deps.json" --runtimeconfig "$consumer/NINA.runtimeconfig.json" --additional-deps "$consumer/Wpf.Portable.Distribution.Stage.deps.json" "$entry/NinaFramingPortable.dll" "$input" "$input_hash" > "$run/stdout.log" 2> "$run/stderr.log"