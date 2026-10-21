[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
Set-StrictMode -Version Latest
$revision = '4742afae0a09c79be772f23fcf163fb5852d2775'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repositoryRoot 'artifacts/portability/nina-windows-reference'
$metadata = Join-Path $repositoryRoot "artifacts/portability/nina/$revision/metadata"
$fixture = Join-Path $repositoryRoot 'src/Portability/Tests/NinaWindowsReference'
$source = Join-Path $output "source/nina-$revision"
$checks = [Collections.Generic.List[object]]::new()
$result = [ordered]@{
    probe = 'NinaWindowsReference'
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    scope = 'Pinned NINA view tests on Windows stock WPF 10.0.12; native copy payload omitted; not this fork or whole NINA baseline'
    revision = $revision
    status = 'running'
    checks = $checks
}
$environmentNames = @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'NINA_WINDOWS_REFERENCE_STATE', 'NINA_WINDOWS_REFERENCE_ASSEMBLIES', 'TEMP', 'TMP')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Add-Check([string] $Name, [string] $Detail) {
    $checks.Add([ordered]@{ name = $Name; status = 'pass'; detail = $Detail })
}

function Read-VerifiedMetadata([string] $Name) {
    $path = Join-Path $metadata $Name
    if (!(Test-Path -LiteralPath "$path.sha256")) {
        throw "Missing pinned metadata or hash. Run Collect-NinaCompatibility.ps1 first: $path"
    }
    $expected = (Get-Content -LiteralPath "$path.sha256" -Raw).Trim()
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) {
        throw "Metadata hash mismatch: $path"
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Get-GitBlobId([byte[]] $Bytes) {
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $hasher.AppendData([Text.Encoding]::ASCII.GetBytes("blob $($Bytes.Length)`0"))
        $hasher.AppendData($Bytes)
        return [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
    } finally { $hasher.Dispose() }
}

function Invoke-DotnetLog([string[]] $Arguments, [string] $Log) {
    Invoke-PortableDotnet 'dotnet' @Arguments *> (Join-Path $output $Log)
    return $LASTEXITCODE
}

$exitCode = 1
Push-Location (Join-Path $repositoryRoot 'src/Portability')
try {
    if (![OperatingSystem]::IsWindows()) { throw 'This stock Windows WPF reference requires Windows.' }
    $env:DOTNET_CLI_HOME = Join-Path $output 'cli-home'
    $env:NUGET_PACKAGES = Join-Path $repositoryRoot 'artifacts/portability/packages'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:TEMP = Join-Path $output 'temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    $sdk = (Invoke-PortableDotnet 'dotnet' --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -cne '10.0.401') { throw "Expected SDK 10.0.401; found $sdk" }
    $result.compilerOrigin = "Microsoft.NET.Sdk.WindowsDesktop $sdk"

    $commit = Read-VerifiedMetadata 'commit.json'
    $tree = Read-VerifiedMetadata 'tree.json'
    if ($commit.sha -cne $revision -or $tree.sha -cne $commit.tree.sha -or $tree.truncated -ne $false) {
        throw 'Metadata does not describe the complete pinned NINA commit tree.'
    }
    $blobs = @($tree.tree | Where-Object type -eq 'blob')
    $blobPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cachePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($blob in $blobs) {
        $null = $blobPaths.Add($blob.path)
        $null = $cachePaths.Add($blob.path)
    }
    $archive = Join-Path $output "nina-$revision.zip"
    if (!(Test-Path -LiteralPath $archive)) {
        $temporary = "$archive.download"
        Invoke-WebRequest "https://codeload.github.com/isbeorn/nina/zip/$revision" -OutFile $temporary -TimeoutSec 60
        Move-Item -LiteralPath $temporary -Destination $archive
    }
    $archiveRoot = "nina-$revision/"
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if (!$entry.FullName.StartsWith($archiveRoot, [StringComparison]::Ordinal) -or
                $entry.FullName -match '(^|/)\.\.(/|$)|\\|:') {
                throw "Unexpected archive path: $($entry.FullName)"
            }
            if (!$entry.FullName.EndsWith('/') -and !$blobPaths.Contains($entry.FullName.Substring($archiveRoot.Length))) {
                throw "Archive contains a file outside the pinned Git tree: $($entry.FullName)"
            }
        }
    } finally { $zip.Dispose() }
    if (!(Test-Path -LiteralPath $source)) {
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $output 'source')
    }
    foreach ($sourceFile in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        $relativePath = [IO.Path]::GetRelativePath($source, $sourceFile.FullName).Replace('\', '/')
        if (!$cachePaths.Contains($relativePath)) { throw "Source cache contains an unpinned file: $relativePath" }
    }
    $verified = 0
    foreach ($blob in $blobs) {
        $path = Join-Path $source $blob.path
        if (!(Test-Path -LiteralPath $path)) { throw "Missing source blob: $($blob.path)" }
        if ((Get-GitBlobId ([IO.File]::ReadAllBytes($path))) -cne $blob.sha) {
            throw "Pinned source blob mismatch: $($blob.path)"
        }
        $verified++
    }
    $provenance = [ordered]@{
        revision = $revision
        tree = $tree.sha
        verifiedBlobs = $verified
        archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        absentSubmodules = @($tree.tree | Where-Object type -eq 'commit' | Select-Object path, sha)
    }
    $provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'provenance.json') -Encoding utf8
    Add-Check 'source-provenance' "Verified all $verified source blobs against the pinned Git tree before building."

    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue((Join-Path $source 'NINA.Test/NINA.Test.csproj'))
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $graph = @()
    while ($queue.Count -gt 0) {
        $project = $queue.Dequeue()
        if (!$visited.Add($project)) { continue }
        if (!$project.StartsWith("$source\", [StringComparison]::OrdinalIgnoreCase)) { throw 'Project reference escapes pinned source.' }
        [xml] $xml = Get-Content -LiteralPath $project -Raw
        $references = @($xml.SelectNodes('//ProjectReference') | ForEach-Object {
            [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $project) $_.Include))
        })
        foreach ($reference in $references) { $queue.Enqueue($reference) }
        $graph += [ordered]@{
            project = [IO.Path]::GetRelativePath($source, $project)
            references = $references.Count
            packages = @($xml.SelectNodes('//PackageReference') | ForEach-Object { $_.Include })
        }
    }
    $graph | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'project-closure.json') -Encoding utf8
    Add-Check 'dependency-closure' "Assessed $($graph.Count) projects and $(@($graph.packages | Sort-Object -Unique).Count) distinct package IDs before restore."

    $projectPath = Join-Path $source 'NINA.Test/NINA.Test.csproj'
    $buildRoot = Join-Path $output 'build'
    $common = @('--artifacts-path', $buildRoot,
        "-p:DirectoryBuildTargetsPath=$(Join-Path $fixture 'Baseline.targets')",
        '-p:GeneratePackageOnBuild=false', '-p:RuntimeFrameworkVersion=10.0.12',
        '-p:RollForward=Disable', '-p:SelfContained=false', '--nologo')
    $restoreArguments = @('restore', $projectPath, '--configfile', (Join-Path $repositoryRoot 'src/Portability/NuGet.Config')) + $common
    if ((Invoke-DotnetLog $restoreArguments 'restore.log') -ne 0) { throw 'Pinned dependency restore failed; see restore.log.' }
    Add-Check 'restore' 'Pinned upstream package dependencies restored using workspace cache and the configured read-only NuGet feed.'

    $buildArguments = @('build', $projectPath, '--no-restore', '--configuration', 'Debug') + $common
    $originalExit = Invoke-DotnetLog $buildArguments 'build-original.log'
    $originalErrors = @(Get-Content -LiteralPath (Join-Path $output 'build-original.log') | Where-Object { $_ -match ': error ' } | Sort-Object -Unique)
    $result.originalApplicationBuild = [ordered]@{ exitCode = $originalExit; errors = $originalErrors; log = 'build-original.log' }

    $omissionsFile = Join-Path $output 'native-omissions.txt'
    $viewArguments = $buildArguments + @('-p:NinaViewOnlyBaseline=true', '-p:NinaViewReferenceEvidence=true', "-p:NinaNativeOmissionsFile=$omissionsFile")
    if ((Invoke-DotnetLog $viewArguments 'build-view-debug.log') -ne 0) { throw 'View-reference managed build failed; see build-view-debug.log.' }
    $omissions = @(Get-Content -LiteralPath $omissionsFile | Sort-Object -Unique)
    foreach ($omission in $omissions) {
        $omittedPath = $omission.Split('|')[0]
        if (!$omittedPath.StartsWith((Join-Path $source 'NINA/External/'), [StringComparison]::OrdinalIgnoreCase) -or
            (Test-Path -LiteralPath $omittedPath)) { throw "Native omission exceeded the absent External-file scope: $omittedPath" }
    }
    $result.nativePayloadOmissions = $omissions
    Add-Check 'managed-view-build' "Debug managed build succeeded; $($omissions.Count) absent External native copy items omitted explicitly. Upstream warnings remain in build-view-debug.log."

    $testAssembly = Join-Path $buildRoot 'bin/NINA.Test/debug/NINA.Test.dll'
    $cases = @(
        @{ name = 'guider'; fixture = 'NINA.Test.View.GuiderChartViewTest'; expected = 8 },
        @{ name = 'direct'; fixture = 'NINA.Test.View.DirectGuiderDetailViewTest'; expected = 1 }
    )
    foreach ($case in $cases) {
        $env:NINA_WINDOWS_REFERENCE_STATE = Join-Path $output "state/$($case.name)"
        $env:NINA_WINDOWS_REFERENCE_ASSEMBLIES = Join-Path $output "$($case.name)-assemblies.json"
        $resultsDirectory = Join-Path $output "test-results/$($case.name)"
        $testArguments = @('test', $testAssembly, '--filter', "FullyQualifiedName~$($case.fixture)",
            '--settings', (Join-Path $fixture 'views.runsettings'), '--results-directory', $resultsDirectory,
            '--logger', "trx;LogFileName=$($case.name).trx", '--blame-hang', '--blame-hang-timeout', '30s', '--nologo')
        $testExit = Invoke-DotnetLog $testArguments "$($case.name)-tests.log"
        [xml] $trx = Get-Content -LiteralPath (Join-Path $resultsDirectory "$($case.name).trx") -Raw
        $testResults = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        if ($testExit -ne 0 -or $testResults.Count -ne $case.expected -or @($testResults | Where-Object outcome -ne 'Passed').Count -ne 0) {
            throw "Selected upstream fixture failed or returned an unexpected case count: $($case.fixture)"
        }
        foreach ($test in $testResults) { Add-Check $test.testName "Unchanged upstream NUnit case passed in isolated $($case.name) testhost; duration $($test.duration)." }
        $assemblyEvidence = Get-Content -LiteralPath $env:NINA_WINDOWS_REFERENCE_ASSEMBLIES -Raw | ConvertFrom-Json
        $framework = @($assemblyEvidence.assemblies | Where-Object name -in @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml'))
        if ($framework.Count -ne 4 -or @($framework | Where-Object { $_.path -notmatch 'Microsoft.WindowsDesktop.App[\\/]10\.0\.12[\\/]' }).Count -ne 0) {
            throw 'Testhost did not record the expected stock WPF 10.0.12 framework assemblies.'
        }
        $ninaAssemblies = @($assemblyEvidence.assemblies | Where-Object name -in @('NINA', 'NINA.WPF.Base', 'NINA.CustomControlLibrary'))
        if ($ninaAssemblies.Count -ne 3 -or @($ninaAssemblies | Where-Object {
            $_.version -cne '3.3.0.1068' -or !$_.path.StartsWith("$buildRoot\", [StringComparison]::OrdinalIgnoreCase)
        }).Count -ne 0) { throw 'Testhost did not load the pinned NINA assemblies from the reference build.' }
        Add-Check "$($case.name)-assembly-origin" 'Recorded genuine NINA, control and stock WPF assembly paths, versions and public key tokens.'
    }
    foreach ($blob in $blobs) {
        if ((Get-GitBlobId ([IO.File]::ReadAllBytes((Join-Path $source $blob.path)))) -cne $blob.sha) {
            throw "Pinned source changed during the build/test: $($blob.path)"
        }
    }
    Add-Check 'source-unchanged' 'All pinned source blobs remain unchanged after build and selected tests.'
    $result.status = 'pass'
    $exitCode = 0
} catch {
    $checks.Add([ordered]@{ name = 'baseline-execution'; status = 'fail'; detail = $_.Exception.ToString() })
    $result.status = 'fail'
} finally {
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
    Pop-Location
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
$result | ConvertTo-Json -Depth 12
exit $exitCode
