[CmdletBinding()]
param([ValidateSet('PrepareOnly', 'Windows', 'StockWindows', 'Linux')][string] $HostProfile = 'PrepareOnly')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$revision = '4742afae0a09c79be772f23fcf163fb5852d2775'
$baseline = Join-Path $repository 'artifacts/portability/nina-windows-reference'
$source = Join-Path $baseline "source/nina-$revision"
$binaryRoot = Join-Path $baseline 'build/bin/NINA.Test/debug'
$distribution = Join-Path $repository 'artifacts/portability/distribution-final-2'
$output = Join-Path $repository 'artifacts/portability/nina-portable-views'
$fixture = Join-Path $output 'fixture'
$project = Join-Path $repository 'src/Portability/Tests/NinaPortableViews/NinaPortableViews.csproj'
$pin = Get-Content (Join-Path (Split-Path $project -Parent) 'BaselinePin.json') -Raw | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-Utf8([string] $Path, [string] $Value) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, $utf8)
}

function Git-BlobHash([byte[]] $Bytes) {
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $hash.AppendData([Text.Encoding]::ASCII.GetBytes("blob $($Bytes.Length)`0"))
        $hash.AppendData($Bytes)
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose() }
}

function Read-AssemblyReferences([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if ($null -eq $pe.PEHeaders.CorHeader) { throw "Managed closure reached a native PE: $Path" }
        if (($pe.PEHeaders.CorHeader.Flags -band [System.Reflection.PortableExecutable.CorFlags]::ILOnly) -eq 0) {
            throw "Managed closure reached a mixed-mode PE: $Path"
        }
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $reader.AssemblyReferences) {
            $reference = $reader.GetAssemblyReference($handle)
            [pscustomobject]@{ name = $reader.GetString($reference.Name); version = $reference.Version.ToString() }
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

$provenance = Get-Content (Join-Path $baseline 'provenance.json') -Raw | ConvertFrom-Json
$baselineResults = Get-Content (Join-Path $baseline 'results.json') -Raw | ConvertFrom-Json
if ($provenance.revision -cne $revision -or $baselineResults.status -cne 'pass') { throw 'Pinned stock view reference is not a successful verified baseline.' }
$metadata = Join-Path $repository "artifacts/portability/nina/$revision/metadata"
$treeFile = Join-Path $metadata 'tree.json'
if ((Get-FileHash $treeFile).Hash.ToLowerInvariant() -cne (Get-Content "$treeFile.sha256" -Raw).Trim()) { throw 'Pinned source tree metadata hash differs.' }
$tree = Get-Content $treeFile -Raw | ConvertFrom-Json
if ($tree.sha -cne $provenance.tree -or $tree.truncated) { throw 'Pinned source tree is incomplete or changed.' }
$verified = 0
foreach ($blob in $tree.tree | Where-Object type -eq blob) {
    $path = Join-Path $source $blob.path
    if ((Git-BlobHash ([IO.File]::ReadAllBytes($path))) -cne $blob.sha) { throw "Pinned source blob differs: $($blob.path)" }
    $verified++
}
$runtimePackage = Join-Path $distribution 'feed/Wpf.Portable.Runtime.10.0.0-portable.1.nupkg'
if ((Get-FileHash $runtimePackage).Hash -cne $pin.runtimePackageSha256) { throw 'Immutable final-2 runtime package hash differs.' }
[xml] $runtime = Get-Content (Join-Path $distribution 'runtime/RuntimeManifest.xml')
$runtimeNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $runtime.portableRuntime.file | Where-Object kind -eq managed) {
    $null = $runtimeNames.Add([IO.Path]::GetFileNameWithoutExtension($item.path))
}
$frameworkNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in Get-ChildItem 'C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/ref/net10.0' -Filter '*.dll') {
    $null = $frameworkNames.Add($file.BaseName)
}
$queue = [Collections.Generic.Queue[string]]::new()
foreach ($name in @('NINA', 'NINA.Core', 'NINA.Profile', 'NINA.WPF.Base', 'NINA.CustomControlLibrary')) { $queue.Enqueue($name) }
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$files = [Collections.Generic.List[object]]::new()
$edges = [Collections.Generic.List[object]]::new()
$unresolved = [Collections.Generic.List[string]]::new()
$props = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Project'))
$references = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('ItemGroup'))
$props.Add($references)
while ($queue.Count -gt 0) {
    $name = $queue.Dequeue()
    if (!$visited.Add($name)) { continue }
    if ($runtimeNames.Contains($name) -or $frameworkNames.Contains($name)) { continue }
    $path = Join-Path $binaryRoot "$name.dll"
    if (!(Test-Path -LiteralPath $path)) { $unresolved.Add($name); continue }
    $identity = [Reflection.AssemblyName]::GetAssemblyName($path)
    $dependencies = @(Read-AssemblyReferences $path)
    if ($identity.Name.StartsWith('NINA', [StringComparison]::Ordinal) -and $identity.Version.ToString() -cne '3.3.0.1068') { throw "NINA identity changed: $identity" }
    $relative = $identity.Name + '.dll'
    $copyPath = Join-Path $fixture "managed/$relative"
    [IO.Directory]::CreateDirectory((Split-Path $copyPath -Parent)) | Out-Null
    [IO.File]::Copy($path, $copyPath, $true)
    $files.Add([ordered]@{ outputPath = $relative; sourcePath = $path; identity = $identity.FullName; sha256 = (Get-FileHash $path).Hash })
    $reference = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Reference'))
    $reference.SetAttributeValue('Include', $identity.Name)
    $reference.SetAttributeValue('HintPath', '$(MSBuildThisFileDirectory)managed/' + $relative)
    $reference.SetAttributeValue('Private', 'true')
    $references.Add($reference)
    foreach ($dependency in $dependencies) {
        $resolution = if ($runtimeNames.Contains($dependency.name)) { 'qualified-runtime-package' } elseif ($frameworkNames.Contains($dependency.name)) { 'Microsoft.NETCore.App' } else { 'baseline-managed-closure' }
        $edges.Add([ordered]@{ from = $identity.Name; to = $dependency.name; requestedVersion = $dependency.version; resolution = $resolution })
        $queue.Enqueue($dependency.name)
    }
}
foreach ($culture in Get-ChildItem $binaryRoot -Directory) {
    foreach ($parent in @($files)) {
        $name = [IO.Path]::GetFileNameWithoutExtension($parent.outputPath) + '.resources.dll'
        $path = Join-Path $culture.FullName $name
        if (!(Test-Path -LiteralPath $path)) { continue }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($path)
        if ([string]::IsNullOrEmpty($identity.CultureName)) { throw "Satellite has no culture: $path" }
        $relative = $identity.CultureName + '/' + $name
        $copyPath = Join-Path $fixture "satellites/$relative"
        [IO.Directory]::CreateDirectory((Split-Path $copyPath -Parent)) | Out-Null
        [IO.File]::Copy($path, $copyPath, $true)
        $files.Add([ordered]@{ outputPath = $relative; sourcePath = $path; identity = $identity.FullName; sha256 = (Get-FileHash $path).Hash })
        $content = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('None'))
        $content.SetAttributeValue('Include', '$(MSBuildThisFileDirectory)satellites/' + $relative)
        $content.SetAttributeValue('Link', $relative)
        $content.SetAttributeValue('CopyToOutputDirectory', 'PreserveNewest')
        $references.Add($content)
    }
}
$generated = Join-Path $baseline 'build/obj/NINA/debug_win-x64/View/Equipment/Guider/DirectGuiderDetailView.g.cs'
if (!(Get-Content $generated -Raw).Contains('GeneratedCodeAttribute("PresentationBuildTasks", "10.0.12.0")')) { throw 'Original generated component compiler identity differs.' }
$records = [Collections.Generic.List[string]]::new()
foreach ($file in $files) { $records.Add("$($file.outputPath)|$($file.identity)|$($file.sha256)") }
$records.Sort([StringComparer]::Ordinal)
$closureHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $records) + "`n")))
if ($pin.revision -cne $revision -or $closureHash -cne $pin.closureSha256 -or
    (Get-FileHash (Join-Path $binaryRoot 'NINA.dll')).Hash -cne $pin.ninaSha256) { throw 'Pinned unchanged managed closure differs; do not silently rebaseline it.' }
$manifest = [ordered]@{
    revision = $revision; verifiedSourceBlobs = $verified; sourceProvenance = $provenance
    sourceRebuild = $false; originalApplicationStartup = $false; originalNUnitHardwareBootstrap = $false
    diagnosticPackage = @{ producerCommit = '7874ee356'; file = $runtimePackage; sha256 = (Get-FileHash $runtimePackage).Hash; knownLimitations = 'Final-2 animation/word-selection repairs are pending; not final Phase3 qualification.' }
    originalGeneratedSource = @{ path = $generated; sha256 = (Get-FileHash $generated).Hash; compiler = 'PresentationBuildTasks 10.0.12.0 from Microsoft SDK 10.0.401' }
    pinnedClosureSha256 = $closureHash
    files = @($files); assemblyReferenceEdges = @($edges); unresolvedReferences = @($unresolved)
    harnessAdaptations = @('Separate net10 package-only host and its runtimeconfig, without original application startup or NUnit global setup.', 'Public artifact state fields redirected before NINA initialization.', 'Exactly the six unchanged DirectGuiderDetailViewTest dictionaries, with original ResourceDictionary construction.', 'Actual view measured/arranged without Window.Show; no display or equipment startup.')
}
Write-Utf8 (Join-Path $fixture 'References.props') $props.ToString()
Write-Utf8 (Join-Path $fixture 'Closure.json') ($manifest | ConvertTo-Json -Depth 12)
$linuxRepository = '/mnt/' + $repository.Substring(0, 1).ToLowerInvariant() + $repository.Substring(2).Replace('\', '/')
$linuxOutput = "$linuxRepository/artifacts/portability/nina-portable-views"
foreach ($profile in @('windows', 'linux')) {
    $feed = if ($profile -eq 'windows') { Join-Path $distribution 'feed' } else { "$linuxRepository/artifacts/portability/distribution-final-2/feed" }
    Write-Utf8 (Join-Path $output "NuGet.$profile.Config") ('<configuration><packageSources><clear/><add key="immutable-final-2" value="' + [Security.SecurityElement]::Escape($feed) + '"/></packageSources></configuration>')
}
Write-Output "Verified $verified pinned source blobs; prepared $($files.Count) managed files with $($edges.Count) measured assembly edges and $($unresolved.Count) unresolved references."
if ($HostProfile -eq 'PrepareOnly') { exit 0 }
if ($HostProfile -eq 'Linux') {
    $script = @'
cd REPOSITORY/src/Portability
export DOTNET_CLI_HOME=OUTPUT/cli/linux NUGET_PACKAGES=OUTPUT/packages/linux MSBUILDDISABLENODEREUSE=1 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
sdk=REPOSITORY/artifacts/portability/dotnet-linux-x64/dotnet
"$sdk" build Tests/NinaPortableViews/NinaPortableViews.csproj --nologo -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:RestoreConfigFile=OUTPUT/NuGet.linux.Config -p:NinaPortableArtifacts=OUTPUT > OUTPUT/build-linux.log 2>&1 || { tail -n 15 OUTPUT/build-linux.log; exit 2; }
export NINA_PORTABLE_VIEWS_STATE=OUTPUT/state/linux
"$sdk" OUTPUT/bin/Unix/Debug/net10.0/NinaPortableViews.dll > OUTPUT/runtime-linux.json 2> OUTPUT/runtime-linux.stderr.log
result=$?
printf '{"processExitCode":%s}\n' "$result" > OUTPUT/process-linux.json
printf 'Diagnostic process exit=%s; evidence: OUTPUT/runtime-linux.json\n' "$result"
exit "$result"
'@
    $script = $script.Replace('REPOSITORY', $linuxRepository).Replace('OUTPUT', $linuxOutput)
    & wsl.exe --distribution Ubuntu --exec /bin/sh -lc $script
    exit $LASTEXITCODE
}
$saved = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'MSBUILDDISABLENODEREUSE', 'NINA_PORTABLE_VIEWS_STATE', 'NINA_PORTABLE_VIEWS_STOCK', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location (Join-Path $repository 'src/Portability')
try {
    $env:DOTNET_CLI_HOME = Join-Path $output 'cli/windows'
    $env:NUGET_PACKAGES = Join-Path $output 'packages/windows'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $stock = $HostProfile -eq 'StockWindows'
    $label = if ($stock) { 'stock-windows' } else { 'windows' }
    [string[]] $extra = @()
    if ($stock) { $extra = @('-p:NinaStockWindowsReference=true') }
    Invoke-PortableDotnet 'dotnet' build $project --nologo '-m:1' '-nr:false' '-p:UseSharedCompilation=false' '-p:NuGetAudit=false' "-p:RestoreConfigFile=$output/NuGet.windows.Config" "-p:NinaPortableArtifacts=$output" @extra *> (Join-Path $output "build-$label.log")
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $output "build-$label.log") -Tail 15; exit 2 }
    $env:NINA_PORTABLE_VIEWS_STATE = Join-Path $output "state/$label"
    $env:NINA_PORTABLE_VIEWS_STOCK = if ($stock) { '1' } else { '0' }
    $assembly = if ($stock) { 'bin/Windows_NT/stock/Debug/net10.0-windows/NinaPortableViews.dll' } else { 'bin/Windows_NT/Debug/net10.0/NinaPortableViews.dll' }
    Invoke-PortableDotnet 'dotnet' (Join-Path $output $assembly) > (Join-Path $output "runtime-$label.json") 2> (Join-Path $output "runtime-$label.stderr.log")
    $result = $LASTEXITCODE
    @{ processExitCode = $result } | ConvertTo-Json | Set-Content (Join-Path $output "process-$label.json")
    Write-Output "Diagnostic process exit=$result; evidence: $output/runtime-$label.json"
    exit $result
} finally {
    Pop-Location
    foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
}
