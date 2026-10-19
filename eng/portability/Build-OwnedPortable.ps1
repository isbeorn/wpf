<#
.SYNOPSIS
Builds the owned portable WPF projects from a Windows host.
.DESCRIPTION
Run with PowerShell 7 on Windows. Linux builds invoke the pinned SDK through WSL.
#>
param(
    [Parameter(Mandatory)][ValidateSet('windows', 'linux')][string]$Os,
    [Parameter(Mandatory)][string]$Label,
    [string]$Project = 'src/Portability/Packaging/Stage/Stage.csproj',
    [switch]$Suites,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath("$PSScriptRoot/../.."),
    [string]$Workspace,
    [string]$PackageCache = "$([Environment]::GetFolderPath('UserProfile'))/.nuget/packages",
    [string]$LinuxDistribution = 'Ubuntu',
    [ValidateRange(0, 1048576)][double]$MinimumFreeSpaceGB = 20
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Run this driver on Windows; Linux builds use WSL.' }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).Replace('\', '/')
if (!$Workspace) { $Workspace = "$RepositoryRoot/artifacts/portability/current" }
$Workspace = [IO.Path]::GetFullPath($Workspace).Replace('\', '/')
$PackageCache = [IO.Path]::GetFullPath($PackageCache).Replace('\', '/')
if ($Label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw 'Use a simple report label.' }
$volumePaths = @($Workspace, [IO.Path]::GetFullPath($PackageCache), [IO.Path]::GetTempPath())
$volumeRoots = @($volumePaths | ForEach-Object { [IO.Path]::GetPathRoot($_) } | Sort-Object -Unique)
$minimumFreeBytes = [long]($MinimumFreeSpaceGB * 1GB)
$spaceChecks = @(foreach ($volumeRoot in $volumeRoots) {
    if ($volumeRoot -notmatch '^[A-Za-z]:[\\/]$') { throw "Build inputs require local volumes: $volumeRoot" }
    $drive = [IO.DriveInfo]::new($volumeRoot)
    if (!$drive.IsReady -or $drive.DriveType -eq [IO.DriveType]::Network) { throw "Build volume is not ready/local: $volumeRoot" }
    $free = $drive.AvailableFreeSpace
    if ($free -lt $minimumFreeBytes) { throw "Insufficient free space on ${volumeRoot}: $free bytes available, $minimumFreeBytes required." }
    [ordered]@{ volume = $volumeRoot; availableBytes = $free; minimumBytes = $minimumFreeBytes; checkedUtc = [DateTime]::UtcNow.ToString('o') }
})
foreach ($directory in @('build', 'tools', 'reports')) {
    [IO.Directory]::CreateDirectory("$Workspace/$directory") | Out-Null
}
$props = "$PSScriptRoot/OwnedPortable.Output.props"
$archive = "$Workspace/prereqs/windows-core.zip"
$pin = Get-Content "$RepositoryRoot/src/Portability/Framework/System.Private.Windows.Core/SourcePin.json" -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'Pinned WindowsCore archive hash differs.' }
if ($Suites) {
    $projects = @(
        'Tests/ImagingIntegration/ImagingIntegration.csproj',
        'Tests/MarkupRuntimeIntegration/Runner/Runner.csproj',
        'Tests/RenderingIntegration/RenderingIntegration.csproj',
        'Tests/GuidelineIntegration/GuidelineIntegration.csproj',
        'Tests/TextIntegration/TextIntegration.csproj',
        'Tests/TextIntegration/Controls/TextControlsIntegration.csproj',
        'Tests/PresentationCoreIntegration/PresentationCoreIntegration.csproj',
        'Tests/PresentationUIIntegration/PresentationUIIntegration.csproj',
        'Tests/NativeWindowIntegration/NativeWindowIntegration.csproj',
        'Tests/NativeDispatcherIntegration/NativeDispatcherIntegration.csproj'
    )
    $items = $projects | ForEach-Object { '<SuiteProject Include="$(OwnedPortableRepositoryRoot)src/Portability/' + $_ + '" />' }
    $driver = '<Project DefaultTargets="Build"><ItemGroup>' + ($items -join '') + '</ItemGroup><Target Name="Restore"><MSBuild Projects="@(SuiteProject)" Targets="Restore" BuildInParallel="false" /></Target><Target Name="Build"><MSBuild Projects="@(SuiteProject)" Targets="Build" BuildInParallel="false" /></Target></Project>'
    $Project = 'artifacts/portability/current/tools/Combined.proj'
    $projectPath = "$Workspace/tools/Combined.proj"
    [IO.File]::WriteAllText($projectPath, $driver, [Text.UTF8Encoding]::new($false))
} else {
    $projectPath = if ([IO.Path]::IsPathRooted($Project)) { $Project } else { "$RepositoryRoot/$Project" }
}

function LinuxPath([string]$path) {
    $path = [IO.Path]::GetFullPath($path).Replace('\', '/')
    if ($path -notmatch '^([A-Za-z]):/(.*)$') { throw "Expected a local drive path: $path" }
    return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2]
}

$osName = if ($Os -eq 'windows') { 'Windows_NT' } else { 'Unix' }
$values = [ordered]@{
    CustomAfterDirectoryBuildProps = $props
    OwnedPortableBuildRoot = "$Workspace/build"
    OwnedPortableRepositoryRoot = "$RepositoryRoot/"
    WindowsCoreArchive = $archive
    WindowsCoreSourceRoot = "$Workspace/prereqs/windows-core/upstream/"
    WpfSourceRoot = "$RepositoryRoot/src/Microsoft.DotNet.Wpf/src/"
    _PresentationBuildTasksAssembly = "$Workspace/build/bin/$osName/PresentationBuildTasks/Release/net10.0/PresentationBuildTasks.dll"
}
$arguments = @('build', $projectPath, '-c', 'Release', '-m:1', '-nr:false', '-p:RestoreLockedMode=true', '-p:NuGetAudit=false')
$log = "$Workspace/reports/$Label.log"
Push-Location "$RepositoryRoot/src/Portability"
try {
    if ($Os -eq 'windows') {
        $hostPath = "$env:ProgramFiles/dotnet/dotnet.exe"
        if ((& $hostPath --version) -ne '10.0.401') { throw 'SDK10.0.401 is required.' }
        $env:NUGET_PACKAGES = $PackageCache
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:PATH = "$env:ProgramFiles/Git/usr/bin;" + $env:PATH
        $arguments += @($values.GetEnumerator() | ForEach-Object { "-p:$($_.Key)=$($_.Value)" })
        & $hostPath @arguments *> $log
    } else {
        $sdk = LinuxPath "$Workspace/prereqs/sdk-linux-x64"
        $environment = @("DOTNET_ROOT=$sdk", 'DOTNET_MULTILEVEL_LOOKUP=0', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', "NUGET_PACKAGES=$(LinuxPath $PackageCache)")
        $arguments[1] = LinuxPath $projectPath
        $arguments += @($values.GetEnumerator() | ForEach-Object { "-p:$($_.Key)=$(LinuxPath $_.Value)" })
        $prefix = @('-d', $LinuxDistribution, '--cd', (LinuxPath "$RepositoryRoot/src/Portability"), '--', 'env') + $environment
        if ((& wsl.exe @prefix "$sdk/dotnet" --version) -ne '10.0.401') { throw 'SDK10.0.401 is required.' }
        & wsl.exe @prefix "$sdk/dotnet" @arguments *> $log
    }
    $code = $LASTEXITCODE
    [ordered]@{ os = $Os; project = $projectPath; arguments = $arguments; freeSpacePreflight = $spaceChecks; exitCode = $code; finishedUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 5 | Set-Content "$Workspace/reports/$Label-result.json" -Encoding utf8
} finally {
    Pop-Location
}
exit $code
