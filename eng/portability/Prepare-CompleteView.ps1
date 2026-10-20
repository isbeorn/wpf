[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DistributionDirectory,
    [Parameter(Mandatory)][string] $DestinationDirectory,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedRuntimeSha256 = '0A263235AF0529E4BACBBDF446B26C21995CEB958F77B839BAEDA98D5915844E',
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedSdkSha256 = '2B957A26B154EEC2467618A1478D97D520A6E42606F92609D68FC121D91D0F80',
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._-]*$')][string] $OracleName = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$distribution = [IO.Path]::GetFullPath($DistributionDirectory)
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
if (Test-Path -LiteralPath $destination) { throw "Use a fresh complete-view destination: $destination" }
Import-Module (Join-Path $repo 'src/Portability/Packaging/PackageTools.psm1') -Force
$expectedPackages = @{
    'Wpf.Portable.Runtime.10.0.0-portable.1.nupkg' = $ExpectedRuntimeSha256
    'Wpf.Portable.Sdk.10.0.0-portable.1.nupkg' = $ExpectedSdkSha256
}
$packages = foreach ($name in $expectedPackages.Keys) {
    $path = Join-Path $distribution "feed/$name"
    $hash = (Get-FileHash -LiteralPath $path).Hash
    if ($hash -ne $expectedPackages[$name]) { throw "The approved Phase 1 package changed: $name" }
    [ordered]@{ path = $path; sha256 = $hash }
}
$feed = Join-Path $distribution 'feed'
if ($feed -notmatch '^([A-Za-z]):[\\/](.*)$') { throw 'This preparation requires a WSL-mounted Windows workspace.' }
$linuxFeed = '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\', '/')
$escapedFeed = [Security.SecurityElement]::Escape($linuxFeed)

$source = Join-Path $repo 'src/Portability/Probes/WindowsReference/FixtureControls'
$sourceFiles = @('FixtureView.xaml', 'FixtureView.xaml.cs', 'FixtureResources.xaml')
foreach ($origin in @('Microsoft', 'Port')) {
    $consumer = Join-Path $destination $origin
    New-Item -ItemType Directory -Path $consumer | Out-Null
    Copy-Item -Path (Join-Path $repo 'src/Portability/Packaging/Consumer/*') -Destination $consumer -Recurse
    Copy-PortableFrozenOracle $repo $consumer -OracleName $OracleName
    $frozen = Join-Path $consumer 'Frozen/FixtureControls.dll'
    $identity = [Reflection.AssemblyName]::GetAssemblyName($frozen).FullName
    $frozenHash = (Get-FileHash -LiteralPath $frozen).Hash
    Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Tests/NativeWindowIntegration/X11Fixture.cs') -Destination (Join-Path $consumer 'App/X11Fixture.cs')
    Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Tests/Fixtures/Fonts/DejaVuSans.ttf') -Destination (Join-Path $consumer 'DejaVuSans.ttf')
    Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Dependencies/TextUnicode/Licenses/DejaVu.txt') -Destination (Join-Path $consumer 'DejaVu.txt')
    $copied = foreach ($name in $sourceFiles) {
        $original = Join-Path $source $name
        $target = Join-Path $consumer "FixtureControls/$name"
        Copy-Item -LiteralPath $original -Destination $target
        $hash = (Get-FileHash -LiteralPath $original).Hash
        if ((Get-FileHash -LiteralPath $target).Hash -ne $hash) { throw "Changed fixture source: $target" }
        [ordered]@{ name = $name; sha256 = $hash }
    }
    ((@($copied | ForEach-Object { $_.sha256.ToLowerInvariant() + '  FixtureControls/' + $_.name }) -join "`n") + "`n") | Set-Content (Join-Path $consumer 'fixture-sources.sha256') -Encoding utf8NoBOM -NoNewline
    $hostFiles = foreach ($name in @('App/Program.cs', 'App/CompleteViewWorkflow.cs', 'App/X11CompleteViewDesktop.cs', 'App/WorkflowResources.xaml', 'App/App.csproj', 'App/X11Fixture.cs', 'FixtureControls/FixtureControls.csproj', 'CompilerEvidence.targets')) {
        [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $consumer $name)).Hash }
    }
    ((@($hostFiles | ForEach-Object { $_.sha256.ToLowerInvariant() + '  ' + $_.name }) -join "`n") + "`n") | Set-Content (Join-Path $consumer 'workflow-sources.sha256') -Encoding utf8NoBOM -NoNewline
    "<Project><PropertyGroup><FixtureOrigin>$origin</FixtureOrigin></PropertyGroup></Project>" | Set-Content (Join-Path $consumer 'Directory.Build.props') -Encoding utf8NoBOM
    '<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.targets') -Encoding utf8NoBOM
    '{"sdk":{"version":"10.0.401","rollForward":"disable"}}' | Set-Content (Join-Path $consumer 'global.json') -Encoding utf8NoBOM
    "<configuration><packageSources><clear/><add key=`"approved-local`" value=`"$escapedFeed`"/></packageSources></configuration>" | Set-Content (Join-Path $consumer 'NuGet.Config') -Encoding utf8NoBOM
    [ordered]@{ origin = $origin; assemblyIdentity = $identity; compilerVersion = $(if ($origin -eq 'Microsoft') { '10.0.12.0' } else { '10.0.0.0' });
        fixtureSha256 = $frozenHash; sourceFiles = $copied; hostFiles = $hostFiles; packages = @($packages) } |
        ConvertTo-Json -Depth 8 | Set-Content (Join-Path $consumer 'CompleteViewOrigin.json') -Encoding utf8NoBOM
}
Write-Output $destination
