[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeManifest,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedFrameworkRuntimeManifestSha256,
    [Parameter(Mandatory)][int] $ExpectedFrameworkRuntimeFileCount,
    [Parameter(Mandatory)][string] $OriginalManagedClosure,
    [Parameter(Mandatory)][string] $NinaCorePath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedNinaCoreSha256,
    [Parameter(Mandatory)][string] $CfitsioLibraryPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedCfitsioLibrarySha256,
    [Parameter(Mandatory)][string] $CfitsioNoticesDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Portability/Tests/NinaPortableImage'))
$pin = Get-Content "$fixture/upstream.json" -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
$utf8 = [Text.UTF8Encoding]::new($false)
function Save([string] $Path, [string] $Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Assert-Hash([string] $Path, [string] $Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $Expected.ToUpperInvariant()) { throw "Pinned image input differs: $Path" }
}
function Element($Document, $Parent, [string] $Name, [hashtable] $Attributes) {
    $node = $Document.CreateElement($Name)
    foreach ($pair in $Attributes.GetEnumerator()) { $node.SetAttribute($pair.Key, $pair.Value) }
    $null = $Parent.AppendChild($node)
    $node
}
foreach ($inputPath in @($NinaSourceDirectory, $FrameworkRuntimeDirectory, (Split-Path $OriginalManagedClosure -Parent), (Split-Path $NinaCorePath -Parent), (Split-Path $CfitsioLibraryPath -Parent), $CfitsioNoticesDirectory, $fixture)) {
    $full = [IO.Path]::GetFullPath($inputPath).TrimEnd('\', '/')
    if ($output -eq $full -or $output.StartsWith($full + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Image output must be outside input directories.' }
}
if (Test-Path -LiteralPath $output) { if (@(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Use a new or empty owned image output directory.' } }
if ($pin.revision -cne '4742afae0a09c79be772f23fcf163fb5852d2775' -or $pin.files.Count -ne 74) { throw 'Expected the complete pinned Image source closure.' }
$baseline = Get-Content "$fixture/../NinaPortableViews/CurrentBaselinePin.json" -Raw | ConvertFrom-Json
Assert-Hash $OriginalManagedClosure $baseline.closureManifestSha256
$closure = Get-Content $OriginalManagedClosure -Raw | ConvertFrom-Json
if ($closure.revision -cne $pin.revision -or $closure.pinnedClosureSha256 -cne $baseline.canonicalClosureSha256 -or $closure.files.Count -ne 160) { throw 'Unexpected original managed closure.' }
$closureDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OriginalManagedClosure))
foreach ($file in $closure.files) {
    $group = if ($file.outputPath.Contains('/')) { 'satellites' } else { 'managed' }
    Assert-Hash "$closureDirectory/$group/$($file.outputPath)" $file.sha256
}
Assert-Hash $NinaCorePath $ExpectedNinaCoreSha256
Assert-Hash $CfitsioLibraryPath $ExpectedCfitsioLibrarySha256
if (!(Test-Path -LiteralPath "$CfitsioNoticesDirectory/License.txt") -or !(Test-Path -LiteralPath "$CfitsioNoticesDirectory/release-notice-excerpts.json")) { throw 'The exact CFITSIO release license and embedded notices are required.' }
Assert-Hash $FrameworkRuntimeManifest $ExpectedFrameworkRuntimeManifestSha256
$family = @(Get-Content $FrameworkRuntimeManifest -Raw | ConvertFrom-Json | Where-Object os -CEQ 'Unix')
if ($family.Count -ne 1 -or $family[0].configuration -cne 'Release' -or $family[0].files.Count -ne $ExpectedFrameworkRuntimeFileCount) { throw 'Expected one complete pinned Unix Release family.' }
$frameworkRoot = [IO.Path]::GetFullPath($FrameworkRuntimeDirectory).TrimEnd('\', '/')
$frameworkPrefix = $frameworkRoot + [IO.Path]::DirectorySeparatorChar
$listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $family[0].files) {
    if ([IO.Path]::IsPathRooted($file.path) -or $file.path -match '\\|:' -or @($file.path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -or !$listed.Add($file.path)) { throw 'Unsafe or duplicate framework path.' }
    if ([IO.Path]::GetFileName($file.path) -match '^Avalonia(?:\.|$)') { throw 'Avalonia is outside the owned WPF family.' }
    $path = [IO.Path]::GetFullPath((Join-Path $frameworkRoot $file.path))
    if (!$path.StartsWith($frameworkPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Framework path escapes the runtime.' }
    Assert-Hash $path $file.sha256
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes) { throw 'Framework file length differs.' }
}
$actualFrameworkFiles = @(Get-ChildItem -LiteralPath $frameworkRoot -Recurse -File -Force)
if ($actualFrameworkFiles.Count -ne $ExpectedFrameworkRuntimeFileCount) { throw 'Framework runtime file count differs.' }
foreach ($file in $actualFrameworkFiles) {
    if (!$listed.Contains([IO.Path]::GetRelativePath($frameworkRoot, $file.FullName).Replace('\', '/'))) { throw 'Unlisted framework runtime file.' }
}
foreach ($file in $pin.files) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:' -or ($file.path -notlike 'NINA.Image/*' -and $file.path -notin @('CommonAssemblyInfo.cs', 'Directory.Build.props', 'LICENSE.txt'))) { throw 'Unexpected Image source path.' }
    Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256
    $dest = "$output/source/$($file.path)"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
    [IO.File]::Copy("$NinaSourceDirectory/$($file.path)", $dest)
}
Assert-Hash "$NinaSourceDirectory/.editorconfig" $pin.editorConfigSha256
[IO.File]::Copy("$NinaSourceDirectory/.editorconfig", "$output/source/.editorconfig")
& "$fixture/Apply-Boundary.ps1" -Source "$output/source"
$changes = @(foreach ($file in $pin.files) {
    $hash = (Get-FileHash "$output/source/$($file.path)").Hash
    if ($hash -cne $file.sha256) { [ordered]@{ path = $file.path; before = $file.sha256; after = $hash } }
})
if ($changes.Count -ne 10) { throw 'Expected ten adapted upstream Image files.' }
Save "$output/adaptations.json" ($changes | ConvertTo-Json -Depth 4)
Save "$output/global.json" '{"sdk":{"version":"10.0.401","rollForward":"disable"}}'
Save "$output/Directory.Build.props" '<Project/>'
Save "$output/Directory.Build.targets" '<Project/>'
Save "$output/NuGet.Config" '<configuration><packageSources><clear/></packageSources></configuration>'
[xml] $original = Get-Content "$output/source/NINA.Image/NINA.Image.csproj" -Raw
$project = [xml] '<Project/>'
$root = $project.DocumentElement
$null = Element $project $root Import @{ Project = 'Sdk.props'; Sdk = 'Microsoft.NET.Sdk' }
foreach ($node in $original.DocumentElement.ChildNodes) { $null = $root.AppendChild($project.ImportNode($node, $true)) }
foreach ($node in @($root.SelectNodes('ItemGroup/PackageReference|ItemGroup/ProjectReference|Target[@Name="PostBuild"]'))) { $null = $node.ParentNode.RemoveChild($node) }
$properties = Element $project $root PropertyGroup @{}
$settings = [ordered]@{
    TargetFramework = 'net10.0'; UseWPF = 'false'; UseWindowsForms = 'false'; ImportWindowsDesktopTargets = 'false'; EnableWindowsTargeting = 'false'
    RuntimeIdentifier = ''; SelfContained = 'false'; AssemblyName = 'NINA.Image'; RootNamespace = 'NINA.Image'; AllowUnsafeBlocks = 'true'
    GeneratePackageOnBuild = 'false'; EnableNETAnalyzers = 'false'; RuntimeFrameworkVersion = '10.0.12'; RollForward = 'Disable'; DeterministicSourcePaths = 'false'
    BaseOutputPath = "$output/bin/"; BaseIntermediateOutputPath = "$output/obj/"; MSBuildProjectExtensionsPath = "$output/obj/"
}
foreach ($pair in $settings.GetEnumerator()) { $node = Element $project $properties $pair.Key @{}; $node.InnerText = $pair.Value }
$references = Element $project $root ItemGroup @{}
foreach ($file in $closure.files | Where-Object { !$_.outputPath.Contains('/') -and $_.outputPath -cne 'NINA.Image.dll' }) {
    $referencePath = if ($file.outputPath -ceq 'NINA.Core.dll') { [IO.Path]::GetFullPath($NinaCorePath) } else { "$closureDirectory/managed/$($file.outputPath)" }
    $null = Element $project $references Reference @{ Include = [IO.Path]::GetFileNameWithoutExtension($file.outputPath); HintPath = $referencePath; Private = 'false' }
}
$null = Element $project $references Reference @{ Include = "$frameworkRoot/*.dll"; Private = 'false' }
$null = Element $project $root Import @{ Project = 'Sdk.targets'; Sdk = 'Microsoft.NET.Sdk' }
$projectPath = "$output/source/NINA.Image/NINA.Image.Portable.csproj"
Save $projectPath $project.OuterXml
Push-Location $output
try {
    if ((Invoke-PortableDotnet $DotnetPath --version).Trim() -cne '10.0.401') { throw 'Expected pinned SDK 10.0.401.' }
    Invoke-PortableDotnet $DotnetPath build $projectPath -c Release --nologo '-m:1' '-nr:false' --configfile "$output/NuGet.Config" '-p:UseSharedCompilation=false' '-p:NuGetAudit=false' "-p:BaseIntermediateOutputPath=$output/obj/" "-p:MSBuildProjectExtensionsPath=$output/obj/" *> "$output/NINA.Image.build.log"
    if ($LASTEXITCODE -ne 0) { throw "Image source build failed. See $output/NINA.Image.build.log" }
} finally { Pop-Location }
foreach ($file in $pin.files) { Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256 }
Assert-Hash $CfitsioLibraryPath $ExpectedCfitsioLibrarySha256
foreach ($change in $changes) { Assert-Hash "$output/source/$($change.path)" $change.after }
$dll = "$output/bin/Release/net10.0/NINA.Image.dll"
$nativeOutput = "$output/native/External/x64/Cfitsio/libcfitsio.so.10"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($nativeOutput)) | Out-Null
[IO.File]::Copy([IO.Path]::GetFullPath($CfitsioLibraryPath), $nativeOutput)
Assert-Hash $nativeOutput $ExpectedCfitsioLibrarySha256
$nativeNotices = @(Get-ChildItem -LiteralPath $CfitsioNoticesDirectory -File | ForEach-Object {
    $destination = "$output/native-notices/CFITSIO/$($_.Name)"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy($_.FullName, $destination)
    [ordered]@{ path = $destination; sha256 = (Get-FileHash -LiteralPath $destination).Hash }
})
$pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
$identity = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
if ($identity -cne [Reflection.AssemblyName]::GetAssemblyName("$closureDirectory/managed/NINA.Image.dll").FullName) { throw 'Image assembly identity changed.' }
$stream = [IO.File]::OpenRead($dll)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
$pdbStream = [IO.File]::OpenRead($pdb)
$provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $resourceNames = @($metadata.ManifestResources | ForEach-Object { $metadata.GetString($metadata.GetManifestResource($_).Name) })
    $pdbReader = $provider.GetMetadataReader()
    $sourceDocuments = @(foreach ($handle in $pdbReader.Documents) {
        $document = $pdbReader.GetDocument($handle)
        $path = $pdbReader.GetString($document.Name)
        $expected = [Convert]::ToHexString($pdbReader.GetBlobBytes($document.Hash))
        $resolvedPath = $path
        $sourceKind = 'physical'
        if (!(Test-Path -LiteralPath $path)) {
            $embedded = @($pdbReader.GetCustomDebugInformation($handle) | ForEach-Object { $pdbReader.GetCustomDebugInformation($_) } | Where-Object { $pdbReader.GetGuid($_.Kind) -eq [Guid]'0e8a571b-6926-466e-b4ad-8ab04611f5fe' })
            if ($embedded.Count -ne 1) { throw "PDB source has no physical or embedded content: $path" }
            $blob = $pdbReader.GetBlobBytes($embedded[0].Value)
            $size = [BitConverter]::ToInt32($blob, 0)
            if ($size -eq 0) { $bytes = [byte[]]$blob[4..($blob.Length - 1)] }
            else {
                $compressed = [IO.MemoryStream]::new($blob, 4, $blob.Length - 4)
                $inflater = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
                $expanded = [IO.MemoryStream]::new()
                try { $inflater.CopyTo($expanded); $bytes = $expanded.ToArray() }
                finally { $expanded.Dispose(); $inflater.Dispose(); $compressed.Dispose() }
                if ($bytes.Length -ne $size) { throw 'Embedded PDB source length differs.' }
            }
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -cne $expected) { throw 'Embedded PDB source hash differs.' }
            $resolvedPath = "$output/embedded-documents/$expected.cs"
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedPath)) | Out-Null
            [IO.File]::WriteAllBytes($resolvedPath, $bytes)
            $sourceKind = 'embedded-generated'
        }
        Assert-Hash $resolvedPath $expected
        [ordered]@{ path = $path; resolvedPath = $resolvedPath; kind = $sourceKind; sha256 = $expected }
    })
    $codeView = @($pe.ReadDebugDirectory() | Where-Object Type -EQ 'CodeView')
    if ($codeView.Count -ne 1) { throw 'Expected one PE CodeView record.' }
    $cv = $pe.ReadCodeViewDebugDirectoryData($codeView[0])
    $id = $pdbReader.DebugMetadataHeader.Id
    $pdbGuid = [Guid]::new([byte[]]$id[0..15])
    $pdbStamp = [BitConverter]::ToUInt32([byte[]]$id[16..19])
    if ($cv.Guid -ne $pdbGuid -or $codeView[0].Stamp -ne $pdbStamp -or $cv.Age -ne 1) { throw 'PE/PDB identity differs.' }
} finally { $provider.Dispose(); $pdbStream.Dispose(); $pe.Dispose(); $stream.Dispose() }
if ($resourceNames.Count -ne 0) { throw 'Original Image assembly has no manifest resources.' }
Save "$output/source-proof.json" ([ordered]@{ pePdbIdentityMatch = $true; sourceDocuments = $sourceDocuments; allSourceDocumentsResolveAndMatchActualFiles = $true } | ConvertTo-Json -Depth 5)
$activeSources = @(Get-ChildItem -LiteralPath $fixture -Recurse -File | ForEach-Object {
    $preserved = Join-Path "$output/wrapper-source" ([IO.Path]::GetRelativePath($fixture, $_.FullName))
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preserved)) | Out-Null
    [IO.File]::Copy($_.FullName, $preserved)
    [ordered]@{ path = $preserved; inputPath = $_.FullName; sha256 = (Get-FileHash $preserved).Hash }
})
[IO.File]::Copy($PSCommandPath, "$output/wrapper-source/Build-NinaPortableImage.ps1")
[IO.File]::Copy("$PSScriptRoot/Invoke-PortableDotnet.ps1", "$output/wrapper-source/Invoke-PortableDotnet.ps1")
Save "$output/image-consumer-intake.json" ([ordered]@{
    status = 'source-proven-image-component-behavior-qualification-separate'; scope = 'image-processing-and-annotation'
    sourceRevision = $pin.revision; assembly = $dll; sha256 = (Get-FileHash $dll).Hash; pdb = $pdb; pdbSha256 = (Get-FileHash $pdb).Hash; identity = $identity
    pePdbIdentityMatch = $true; sourceDocuments = $sourceDocuments.Count; allSourceDocumentsResolveAndMatchActualFiles = $true
    sourceProof = "$output/source-proof.json"; sourceProofSha256 = (Get-FileHash "$output/source-proof.json").Hash
    resourceNames = $resourceNames; resourceNamesAndKeysMatchOriginal = $true; adaptedUpstreamFiles = $changes.Count; changes = $changes
    originalSourceManifest = "$fixture/upstream.json"; originalSourceManifestSha256 = (Get-FileHash "$fixture/upstream.json").Hash
    originalUpstreamSourceFilesUnchanged = $pin.files.Count; activeSourceFiles = $activeSources
    buildScript = "$output/wrapper-source/Build-NinaPortableImage.ps1"; buildScriptSha256 = (Get-FileHash $PSCommandPath).Hash; generatedProject = $projectPath; generatedProjectSha256 = (Get-FileHash $projectPath).Hash
    sdkInvocationGuard = "$output/wrapper-source/Invoke-PortableDotnet.ps1"; sdkInvocationGuardSha256 = (Get-FileHash "$PSScriptRoot/Invoke-PortableDotnet.ps1").Hash
    originalClosureManifest = [IO.Path]::GetFullPath($OriginalManagedClosure); originalClosureManifestSha256 = (Get-FileHash $OriginalManagedClosure).Hash
    ninaCorePath = [IO.Path]::GetFullPath($NinaCorePath); ninaCoreSha256 = $ExpectedNinaCoreSha256
    frameworkRuntimeDirectory = $frameworkRoot; frameworkRuntimeManifest = [IO.Path]::GetFullPath($FrameworkRuntimeManifest)
    frameworkRuntimeManifestSha256 = $ExpectedFrameworkRuntimeManifestSha256; frameworkRuntimeFiles = $ExpectedFrameworkRuntimeFileCount; buildExit = 0
    cfitsioLibrary = $nativeOutput; cfitsioLibrarySha256 = $ExpectedCfitsioLibrarySha256; cfitsioRelativePath = 'External/x64/Cfitsio/libcfitsio.so.10'; cfitsioNotices = $nativeNotices
} | ConvertTo-Json -Depth 7)
Write-Output "Source-built NINA.Image candidate: $output/image-consumer-intake.json"
