[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ConsumerDirectory,
    [Parameter(Mandatory)][string] $PublicationManifest,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedPublicationSha256,
    [Parameter(Mandatory)][string] $RuntimeIntake,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedRuntimeIntakeSha256,
    [Parameter(Mandatory)][string] $WpfBaseIntake,
    [Parameter(Mandatory)][string] $OriginalAppXaml,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $LinuxSdkDirectory,
    [string] $PackageCache = "$([Environment]::GetFolderPath('UserProfile'))/.nuget/packages",
    [string] $LinuxDistribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath("$PSScriptRoot/../../../../..").Replace('\', '/')
$sourceDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
function Sha([string] $Path) { (Get-FileHash -LiteralPath $Path).Hash }
function Assert-Hash([string] $Path, [string] $Expected) {
    if ((Sha $Path) -cne $Expected.ToUpperInvariant()) { throw "Input differs: $Path" }
}
function Save-Json([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 14) + "`n", $utf8)
}
function Local-Path([string] $Path) {
    if ($Path -match '^/mnt/([a-z])/(.*)$') { return $Matches[1].ToUpperInvariant() + ':/' + $Matches[2] }
    [IO.Path]::GetFullPath($Path).Replace('\', '/')
}
function Linux-Path([string] $Path) {
    $full = Local-Path $Path
    if ($full -notmatch '^([A-Za-z]):/(.*)$') { throw "Expected local drive path: $Path" }
    '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2]
}
function Relative-Input([string] $Root, [string] $Relative) {
    if (!$Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '[:\\]' -or @($Relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw "Unsafe publication path: $Relative"
    }
    Join-Path $Root $Relative
}
$provenance = Get-Content -LiteralPath "$sourceDirectory/SourceProvenance.json" -Raw | ConvertFrom-Json
foreach ($file in $provenance.sources) { Assert-Hash "$sourceDirectory/$($file.path)" $file.sha256 }
foreach ($file in $provenance.sharedSources) { Assert-Hash "$repository/$($file.path)" $file.sha256 }
Assert-Hash $PublicationManifest $ExpectedPublicationSha256
Assert-Hash $RuntimeIntake $ExpectedRuntimeIntakeSha256
Assert-Hash $WpfBaseIntake $provenance.baseIntakeSha256
Assert-Hash $OriginalAppXaml $provenance.originalAppXamlSha256
$ConsumerDirectory = Local-Path $ConsumerDirectory
$outputRoot = Local-Path $OutputDirectory
if (!$outputRoot.StartsWith("$repository/artifacts/portability/", [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputRoot)) {
    throw 'Use a fresh output directory under this repository artifacts/portability.'
}
if ($outputRoot.StartsWith($ConsumerDirectory.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be disjoint from the immutable consumer.' }
$intake = Get-Content -LiteralPath $RuntimeIntake -Raw | ConvertFrom-Json
$runtime = @($intake.consumerInputs | Where-Object os -ceq 'linux')
if ($runtime.Count -ne 1 -or $runtime[0].stageFiles -ne 120) { throw 'One released whole 120 Linux family is required.' }
$runtime = $runtime[0]
Assert-Hash $runtime.stageManifest $runtime.stageManifestSha256
Assert-Hash $runtime.familyProof $runtime.familyProofSha256
Assert-Hash $runtime.wrapperProof $runtime.wrapperProofSha256
$family = @(Get-Content -LiteralPath $runtime.stageManifest -Raw | ConvertFrom-Json | Where-Object os -ceq 'Unix')
if ($family.Count -ne 1 -or $family[0].files.Count -ne 120) { throw 'Expected one complete Unix 120 manifest.' }
$publication = @(Get-Content -LiteralPath $PublicationManifest -Raw | ConvertFrom-Json)
if ($publication.Count -ne 329) { throw 'Expected the released complete 329 NINA publication.' }
$listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $publication) {
    if (!$listed.Add($file.path)) { throw 'Duplicate publication member.' }
    $path = Relative-Input $ConsumerDirectory $file.path
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Consumer links are unsupported.' }
    Assert-Hash $path $file.sha256
}
foreach ($item in @(Get-Item -LiteralPath $ConsumerDirectory) + @(Get-ChildItem -LiteralPath $ConsumerDirectory -Recurse -Force)) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Consumer contains a link.' }
}
if (@(Get-ChildItem -LiteralPath $ConsumerDirectory -Recurse -File -Force).Count -ne $listed.Count) { throw 'Consumer has missing or unlisted members.' }
foreach ($file in $family[0].files) { Assert-Hash (Relative-Input $ConsumerDirectory $file.path) $file.sha256 }
Assert-Hash "$ConsumerDirectory/NINA.WPF.Base.dll" $provenance.baseDllSha256
Assert-Hash "$ConsumerDirectory/NINA.WPF.Base.pdb" $provenance.basePdbSha256
$component = Get-Content -LiteralPath $WpfBaseIntake -Raw | ConvertFrom-Json
Assert-Hash $component.sourceProof $component.sourceProofSha256
if (!$component.pePdbIdentityMatch -or !$component.resourceNamesAndKeysMatchOriginal) { throw 'Base component proof is incomplete.' }

[IO.Directory]::CreateDirectory("$outputRoot/assets") | Out-Null
[IO.Directory]::CreateDirectory("$outputRoot/inputs") | Out-Null
[IO.Directory]::CreateDirectory("$outputRoot/fixture") | Out-Null
[IO.File]::WriteAllText("$outputRoot/fixture/References.props", '<Project />', $utf8)
[IO.File]::WriteAllText("$outputRoot/global.json", '{"sdk":{"version":"10.0.401","rollForward":"disable"}}', $utf8)
[IO.File]::WriteAllText("$outputRoot/NuGet.Config", '<configuration><packageSources><clear /></packageSources></configuration>', $utf8)
[xml] $app = Get-Content -LiteralPath $OriginalAppXaml -Raw
$uris = @($app.SelectNodes("//*[local-name()='ResourceDictionary.MergedDictionaries']/*[@Source]") | ForEach-Object {
    $uri = $_.Source.Replace('\', '/')
    if (!$uri.StartsWith('/')) { $uri = '/NINA;component/' + $uri }
    $uri
})
if ($uris.Count -ne 34 -or ($uris | ConvertTo-Json -Compress) -cne ($provenance.resourceSources | ConvertTo-Json -Compress)) { throw 'Original App 34 resource order differs.' }
Save-Json "$outputRoot/assets/ResourceSources.json" $uris
$pixels = [byte[]]::new(800 * 600 * 4)
for ($y = 0; $y -lt 600; $y++) {
    for ($x = 0; $x -lt 800; $x++) {
        $i = ($y * 800 + $x) * 4
        $pixels[$i] = [byte](($x * 3 + $y) % 256)
        $pixels[$i + 1] = [byte](($x + $y * 5) % 256)
        $pixels[$i + 2] = if (([int][Math]::Floor($x / 32) + [int][Math]::Floor($y / 24)) % 2) { 180 } else { 30 }
        $pixels[$i + 3] = 255
    }
}
[IO.File]::WriteAllBytes("$outputRoot/assets/survey.bgra", $pixels)
Assert-Hash "$outputRoot/assets/survey.bgra" $provenance.surveySha256
foreach ($file in $publication | Where-Object path -like 'Database/*') {
    $target = Relative-Input "$outputRoot/assets" $file.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::Copy((Relative-Input $ConsumerDirectory $file.path), $target)
    Assert-Hash $target $file.sha256
}
$references = [xml] '<Project><ItemGroup /></Project>'
foreach ($file in $publication | Where-Object { $_.path -notmatch '/' -and $_.path.EndsWith('.dll') } | Sort-Object path) {
    $path = Relative-Input $ConsumerDirectory $file.path
    try { $name = [Reflection.AssemblyName]::GetAssemblyName($path).Name } catch [BadImageFormatException] { continue }
    $reference = $references.CreateElement('Reference')
    $reference.SetAttribute('Include', $name)
    $reference.SetAttribute('HintPath', (Linux-Path $path))
    $reference.SetAttribute('Private', 'false')
    $null = $references.SelectSingleNode('/Project/ItemGroup').AppendChild($reference)
}
$references.Save("$outputRoot/inputs/References.props")
$records = [Collections.Generic.List[object]]::new()
$paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-Input([string] $Path) {
    $local = Local-Path $Path
    if (!$paths.Add($local)) { return }
    $row = [ordered]@{ path = (Linux-Path $local); sha256 = (Sha $local) }
    if ($local.EndsWith('.dll')) {
        try { $row.identity = [Reflection.AssemblyName]::GetAssemblyName($local).FullName } catch [BadImageFormatException] { }
    }
    $records.Add($row)
}
foreach ($file in $publication) { Add-Input (Relative-Input $ConsumerDirectory $file.path) }
foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File) { Add-Input $file.FullName }
foreach ($file in $provenance.sharedSources) { Add-Input "$repository/$($file.path)" }
foreach ($file in Get-ChildItem -LiteralPath "$outputRoot/assets" -Recurse -File) { Add-Input $file.FullName }
foreach ($path in @($PublicationManifest, $RuntimeIntake, $WpfBaseIntake, $OriginalAppXaml, $runtime.stageManifest, $runtime.familyProof, $runtime.wrapperProof, $component.sourceProof,
    "$repository/src/Portability/Tests/NinaPortableViews/NinaPortableViews.csproj", "$repository/src/Portability/Tests/NinaPortableViews/Directory.Build.props", "$repository/src/Portability/Tests/NinaPortableViews/Directory.Build.targets",
    "$outputRoot/inputs/References.props", "$outputRoot/fixture/References.props", "$outputRoot/global.json", "$outputRoot/NuGet.Config")) { Add-Input $path }
$pin = [ordered]@{
    consumerDirectory = (Linux-Path $ConsumerDirectory); frameworkDirectory = (Linux-Path $ConsumerDirectory); frameworkFiles = 120
    runtimeIntakeSha256 = (Sha $RuntimeIntake); frameworkManifestSha256 = $runtime.stageManifestSha256; componentIntakeSha256 = (Sha $WpfBaseIntake)
    publicationManifest = (Linux-Path $PublicationManifest); publicationManifestSha256 = (Sha $PublicationManifest)
    qualifiedHistoricalCompileFamilySha256 = $provenance.qualifiedCompileFamilySha256
    historicalBaseReferenceFamilySha256 = $provenance.baseProducerReferenceFamilySha256; historicalBaseCompilerManifestSha256 = $provenance.baseCompilerManifestSha256
    sourceProvenance = (Linux-Path "$sourceDirectory/SourceProvenance.json"); entryDirectory = (Linux-Path "$outputRoot/build/bin/Unix/Release/net10.0")
    linuxSdkDirectory = (Linux-Path $LinuxSdkDirectory); resourceSources = $uris; files = $records.ToArray()
}
Save-Json "$outputRoot/input-pins.json" $pin
$buildArgs = @('--distribution', $LinuxDistribution, '--exec', '/bin/bash', (Linux-Path "$sourceDirectory/Build.sh"), (Linux-Path $repository), (Linux-Path $LinuxSdkDirectory), (Linux-Path $outputRoot), (Linux-Path $sourceDirectory), (Linux-Path $PackageCache))
& wsl @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Fixture-only build failed; preserve build.log.' }
$dll = "$outputRoot/build/bin/Unix/Release/net10.0/NinaFramingPortable.dll"
$pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
if (@(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($dll)) -Filter '*.dll' -File).Count -ne 1) { throw 'Fixture copied runtime assemblies.' }
$stream = [IO.File]::OpenRead($dll)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
$pdbStream = [IO.File]::OpenRead($pdb)
$provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
try {
    $reader = $provider.GetMetadataReader()
    $documents = @(foreach ($handle in $reader.Documents) {
        $doc = $reader.GetDocument($handle)
        $path = $reader.GetString($doc.Name)
        $hash = [Convert]::ToHexString($reader.GetBlobBytes($doc.Hash))
        $resolved = Local-Path $path
        Assert-Hash $resolved $hash
        [ordered]@{ path = $path; resolvedPath = $resolved; sha256 = $hash }
    })
    $codeView = @($pe.ReadDebugDirectory() | Where-Object Type -eq 'CodeView')
    if ($codeView.Count -ne 1 -or $documents.Count -ne 13) { throw 'Expected one CodeView and 13 source documents.' }
    $cv = $pe.ReadCodeViewDebugDirectoryData($codeView[0])
    $id = $reader.DebugMetadataHeader.Id
    if ($cv.Guid -ne [Guid]::new([byte[]]$id[0..15]) -or $codeView[0].Stamp -ne [BitConverter]::ToUInt32([byte[]]$id[16..19]) -or $cv.Age -ne 1) { throw 'Fixture PE/PDB identity differs.' }
} finally { $provider.Dispose(); $pdbStream.Dispose(); $pe.Dispose(); $stream.Dispose() }
Save-Json "$outputRoot/fixture-source-proof.json" ([ordered]@{ pePdbIdentityMatch = $true; assemblySha256 = (Sha $dll); pdbSha256 = (Sha $pdb); documents = $documents })
foreach ($row in $records) { Assert-Hash (Local-Path $row.path) $row.sha256 }
foreach ($doc in $documents) { Add-Input $doc.resolvedPath }
foreach ($file in Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($dll)) -Recurse -File) { Add-Input $file.FullName }
Add-Input "$outputRoot/fixture-source-proof.json"
$pin.files = $records.ToArray()
Save-Json "$outputRoot/input-pins.json" $pin
Add-Input "$outputRoot/input-pins.json"
Save-Json "$outputRoot/PreparedConsumerFreeze.json" ([ordered]@{
    status = 'prepared-not-executed'; expectedChecks = 21; outputDirectory = (Linux-Path $outputRoot); repository = (Linux-Path $repository)
    consumerDirectory = $pin.consumerDirectory; entryDirectory = $pin.entryDirectory; linuxSdkDirectory = $pin.linuxSdkDirectory
    inputPins = (Linux-Path "$outputRoot/input-pins.json"); inputPinsSha256 = (Sha "$outputRoot/input-pins.json")
    runtimeIntakeSha256 = $pin.runtimeIntakeSha256; frameworkManifestSha256 = $pin.frameworkManifestSha256
    sourceProvenance = $pin.sourceProvenance; files = $records.ToArray()
})
Get-FileHash -LiteralPath "$outputRoot/PreparedConsumerFreeze.json"
Write-Output 'Prepared only. Run the frozen entry after the existing root release and recipe review.'