#!/bin/sh
set -eu

repository=$(CDPATH= cd -- "$(dirname -- "$0")/../../../.." && pwd)
dotnet_exe=${DOTNET_EXE:-"$repository/artifacts/portability/dotnet-linux-x64/dotnet"}
export DOTNET_CLI_HOME=${DOTNET_CLI_HOME:-"$repository/artifacts/portability/linux-cli-home"}
export NUGET_PACKAGES=${NUGET_PACKAGES:-"$repository/artifacts/portability/linux-packages"}
output="$repository/artifacts/portability/markup"
mkdir -p "$output"
native_source=$(mktemp -d /tmp/wpf-markup-case.XXXXXX)
cleanup() {
    rm -f -- "$native_source/Views/Orbit View Ω.xaml"
    rmdir -- "$native_source/Views" "$native_source"
}
trap cleanup EXIT
mkdir "$native_source/Views"
cp "$repository/src/Portability/Tests/MarkupIntegration/Fixture/Views/Orbit View Ω.xaml" "$native_source/Views/Orbit View Ω.xaml"
test -f "$native_source/Views/Orbit View Ω.xaml"
test ! -e "$native_source/views/Orbit View Ω.xaml"
cd "$repository/src/Portability"
if "$dotnet_exe" build Tests/MarkupIntegration/Fixture/Fixture.csproj --nologo \
    -p:MarkupIntegrationCaseSensitiveNegative=true \
    -p:MarkupIntegrationArtifactSuffix=native-wrong-case/ \
    "-p:MarkupIntegrationWrongCaseViewSource=$native_source/views/Orbit View Ω.xaml" \
    > "$output/wrong-case-linux.log" 2>&1; then
    printf '%s\n' 'Wrong-case native source unexpectedly compiled.' >&2
    exit 1
fi
if ! grep -Fq 'error BG1002:' "$output/wrong-case-linux.log"; then
    cat "$output/wrong-case-linux.log" >&2
    exit 1
fi
printf '%s\n' '{"probe":"real-wpf-markup-native-case","scope":"Original compiler rejects a wrongly cased XAML path on native Linux storage; build outputs remain in workspace.","checks":[{"name":"case-sensitive-source-path-rejection","status":"pass","detail":"Correct Views source exists; incorrect views path does not exist; actual compiler rejected the incorrect path."}]}' > "$output/native-case-linux.json"
