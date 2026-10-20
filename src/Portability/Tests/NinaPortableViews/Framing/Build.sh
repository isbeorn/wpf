#!/usr/bin/env bash
set -euo pipefail
[[ $# == 5 ]]
repo=$1;sdk=$2;output=$3;source=$4;packages=$5
export DOTNET_ROOT="$sdk" DOTNET_MULTILEVEL_LOOKUP=0
export DOTNET_CLI_HOME="$output/cli-home" NUGET_PACKAGES="$packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DOTNET_STARTUP_HOOKS
cd "$output"
[[ "$("$sdk/dotnet" --version)" == 10.0.401 ]]
"$sdk/dotnet" build "$repo/src/Portability/Tests/NinaPortableViews/NinaPortableViews.csproj" -c Release --nologo -m:1 -nr:false \
 -p:UseSharedCompilation=false -p:NinaStockWindowsReference=false -p:AssemblyName=NinaFramingPortable \
 -p:TargetFramework=net10.0 -p:RuntimeFrameworkVersion=10.0.12 -p:RollForward=Disable -p:BuildProjectReferences=false \
 "-p:NinaPortableArtifacts=$output/build" "-p:NinaFixtureDirectory=$output/fixture" \
 "-p:CustomAfterMicrosoftCommonTargets=$source/Fixture.targets" "-p:FramingSourceDirectory=$source" \
 "-p:FramingAssetDirectory=$output/assets" "-p:FramingRepositoryRoot=$repo" \
 "-p:FramingReleasedReferences=$output/inputs/References.props" \
 "-p:RestoreConfigFile=$output/NuGet.Config" -p:NuGetAudit=false > "$output/build.log" 2>&1