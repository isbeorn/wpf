#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -ne 2 ]; then
    echo 'Usage: Test-PortablePackages.sh <prepared-external-consumer> <dotnet10.0.401>' >&2
    exit 2
fi
prepared="$(realpath "$1")"
test ! -d "$prepared/.packages"
test ! -d "$prepared/App/obj"
work="$(mktemp -d /tmp/wpf-portable-package.XXXXXXXX)"
cp -a "$prepared/." "$work/"
cd "$work"
copy_evidence() {
    status=$?
    trap - EXIT
    set +e
    for file in ./*.log ./*.json ./*.txt ./*.xml; do
        if [ -f "$file" ]; then cp -a "$file" "$prepared/"; fi
    done
    if [ -d App/bin ]; then cp -a App/bin "$prepared/App/"; fi
    if [ -d App/obj ]; then cp -a App/obj "$prepared/App/"; fi
    if [ -d Companion/obj ]; then cp -a Companion/obj "$prepared/Companion/"; fi
    for directory in custom-bin custom-obj publish-fdd publish-linux-fdd publish-linux-sc; do
        if [ -d "$directory" ]; then cp -a "$directory" "$prepared/"; fi
    done
    exit "$status"
}
trap copy_evidence EXIT
touch .case-probe
if [ -e .CASE-PROBE ]; then echo 'Positive markup consumer requires a case-sensitive Linux filesystem.' >&2; exit 1; fi
rm -- .case-probe
printf '{"probe":"package-source-filesystem","executionRoot":"%s","filesystem":"%s","checks":[{"name":"positive-case-sensitive-source-build","status":"pass","detail":"Distinct filename case verified before genuine App/Page/dictionary compilation."}]}\n' "$PWD" "$(stat -f -c %T .)" >case-sensitivity-results.json
dotnet="$2"
export NUGET_PACKAGES="$PWD/.packages"
export DOTNET_CLI_HOME="$PWD/.cli"
export MSBUILDDISABLENODEREUSE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1
test "$($dotnet --version)" = 10.0.401
"$dotnet" build App/App.csproj -c Release --disable-build-servers -v:diag >build.log 2>&1
cp App/obj/Release/net10.0/compiler-origins.xml compiler-origins.xml
cp Companion/obj/Release/net10.0/compiler-origins-control.xml compiler-origins-control.xml
"$dotnet" App/bin/Release/net10.0/App.dll >bin-results.json
"$dotnet" build App/App.csproj -c Release --disable-build-servers "-p:DirectoryBuildPropsPath=$PWD/CustomPaths.props" >custom-build.log 2>&1
"$dotnet" custom-bin/App/Release/net10.0/App.dll >custom-results.json
find custom-obj -name '*.g.cs' -printf '%p %T@\n' | sort >incremental-before.txt
stat -c '%n %y' custom-bin/App/Release/net10.0/App.dll custom-bin/Companion/Release/net10.0/Companion.dll >>incremental-before.txt
"$dotnet" build App/App.csproj -c Release --disable-build-servers "-p:DirectoryBuildPropsPath=$PWD/CustomPaths.props" -v:diag >incremental-build.log 2>&1
find custom-obj -name '*.g.cs' -printf '%p %T@\n' | sort >incremental-after.txt
stat -c '%n %y' custom-bin/App/Release/net10.0/App.dll custom-bin/Companion/Release/net10.0/Companion.dll >>incremental-after.txt
cmp incremental-before.txt incremental-after.txt
grep -q 'Skipping target "CoreCompile" because all output files' incremental-build.log
printf '%s\n' '{"probe":"package-incremental","checks":[{"name":"unchanged-second-build","status":"pass","detail":"Generated sources and assemblies retained timestamps; CoreCompile skipped."}]}' >incremental-results.json
"$dotnet" publish App/App.csproj -c Release --self-contained false --disable-build-servers -o publish-fdd >publish-fdd.log 2>&1
"$dotnet" publish-fdd/App.dll >publish-fdd-results.json
"$dotnet" publish App/App.csproj -c Release -r linux-x64 --self-contained false --disable-build-servers -o publish-linux-fdd >publish-linux-fdd.log 2>&1
"$dotnet" publish-linux-fdd/App.dll >publish-linux-fdd-results.json
ldd publish-linux-fdd/libSkiaSharp.so >native-linkage.txt
ldd publish-linux-fdd/libHarfBuzzSharp.so >>native-linkage.txt
if grep -q 'not found' native-linkage.txt; then cat native-linkage.txt >&2; exit 1; fi
xvfb-run -a "$dotnet" publish-linux-fdd/App.dll --native >native-fdd-results.json
"$dotnet" msbuild App/App.csproj -p:RuntimeIdentifier=linux-x64 -p:SelfContained=true -p:RuntimeFrameworkVersion=10.0.12 -t:ProcessFrameworkReferences -getItem:FrameworkReference,PackageDownload -getProperty:DisableTransitiveFrameworkReferenceDownloads >framework-download-evaluation.json
"$dotnet" publish App/App.csproj -c Release -r linux-x64 --self-contained true --disable-build-servers -o publish-linux-sc >publish-linux-sc.log 2>&1
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 ./publish-linux-sc/App >publish-linux-sc-results.json
xvfb-run -a env -u DOTNET_ROOT -u DOTNET_ROOT_X64 ./publish-linux-sc/App --native >native-sc-results.json
printf 'External package consumer passed: %s\n' "$PWD"
