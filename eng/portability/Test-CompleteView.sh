#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -ne 2 ]; then
    echo 'Usage: Test-CompleteView.sh <prepared-dual-origin-directory> <dotnet10.0.401>' >&2
    exit 2
fi
prepared="$(realpath "$1")"
dotnet="$(realpath "$2")"
work="$(mktemp -d /tmp/wpf-complete-view.XXXXXXXX)"
cp -a "$prepared/." "$work/"
failed=0
for origin in Microsoft Port; do
    test ! -d "$prepared/$origin/.packages"
    set +e
    (
        set -euo pipefail
        cd "$work/$origin"
        test "$("$dotnet" --version)" = 10.0.401
        export NUGET_PACKAGES="$PWD/.packages"
        export DOTNET_CLI_HOME="$PWD/.cli"
        export MSBUILDDISABLENODEREUSE=1
        export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
        export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
        export DOTNET_CLI_TELEMETRY_OPTOUT=1
        touch .case-probe
        test ! -e .CASE-PROBE
        rm .case-probe
        sha256sum -c fixture-sources.sha256 >source-hash-results.txt
        sha256sum -c workflow-sources.sha256 >>source-hash-results.txt
        "$dotnet" build App/App.csproj -c Release --disable-build-servers -v:q >build.log 2>&1
        fixture_hash="$(sha256sum App/bin/Release/net10.0/FixtureControls.dll | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
        if [ "$origin" = Port ]; then sed -i -E "s/\"fixtureSha256\": \"[A-F0-9]+\"/\"fixtureSha256\": \"$fixture_hash\"/" CompleteViewOrigin.json; fi
        cp App/obj/Release/net10.0/compiler-origins.xml compiler-origins.xml
        if [ "$origin" = Port ]; then cp FixtureControls/obj/Release/net10.0/compiler-origins.xml fixture-compiler-origins.xml; fi
        "$dotnet" publish App/App.csproj -c Release --self-contained false --disable-build-servers -o publish >publish.log 2>&1
        "$dotnet" publish App/App.csproj -c Release -r linux-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 --disable-build-servers -o publish-sc >publish-sc.log 2>&1
        native_failed=0
        set +e
        xvfb-run -a "$dotnet" publish/App.dll --complete-view --verify >results.json 2>stderr.log
        if [ "$?" -ne 0 ]; then native_failed=1; fi
        "$dotnet" publish/App.dll --audit-complete-view-results results.json >audit-results.json 2>audit-stderr.log
        if [ "$?" -ne 0 ]; then native_failed=1; fi
        xvfb-run -a env -u DOTNET_ROOT -u DOTNET_ROOT_X64 ./publish-sc/App --complete-view --verify >results-sc.json 2>stderr-sc.log
        if [ "$?" -ne 0 ]; then native_failed=1; fi
        "$dotnet" publish/App.dll --audit-complete-view-results results-sc.json >audit-results-sc.json 2>audit-stderr-sc.log
        if [ "$?" -ne 0 ]; then native_failed=1; fi
        if [ "$origin" = Microsoft ]; then
            xvfb-run -a "$dotnet" publish/App.dll --native >phase1-default-results.json 2>phase1-default-stderr.log
            if [ "$?" -ne 0 ]; then native_failed=1; fi
        fi
        set -e
        sha256sum -c fixture-sources.sha256 >>source-hash-results.txt
        sha256sum -c workflow-sources.sha256 >>source-hash-results.txt
        exit "$native_failed"
    )
    status=$?
    set -e
    if [ "$status" -ne 0 ]; then failed=1; fi
    for file in "$work/$origin/"*.log "$work/$origin/"*.json "$work/$origin/"*.xml "$work/$origin/"*.txt; do
        if [ -f "$file" ]; then cp -a "$file" "$prepared/$origin/"; fi
    done
    if [ -d "$work/$origin/publish" ]; then cp -a "$work/$origin/publish" "$prepared/$origin/"; fi
    if [ -d "$work/$origin/publish-sc" ]; then cp -a "$work/$origin/publish-sc" "$prepared/$origin/"; fi
done
printf 'Complete-view execution root: %s\n' "$work"
exit "$failed"
