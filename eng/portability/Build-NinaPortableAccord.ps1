[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $OriginalManagedDirectory,
    [Parameter(Mandatory)][string] $ExistingPackageCache,
    [Parameter(Mandatory)][string] $RestorePackageDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $DotnetPath
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Portability/Tests/NinaPortableImage'))
$pin = Get-Content "$fixture/Accord.upstream.json" -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
$utf8 = [Text.UTF8Encoding]::new($false)
function Save([string] $Path, [string] $Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Assert-Hash([string] $Path, [string] $Expected) {
    if ((Get-FileHash -LiteralPath $Path).Hash -cne $Expected) { throw "Pinned Accord input differs: $Path" }
}
foreach ($inputPath in @($NinaSourceDirectory, $OriginalManagedDirectory, $ExistingPackageCache, $fixture)) {
    $full = [IO.Path]::GetFullPath($inputPath).TrimEnd('\', '/')
    if ($output -eq $full -or $output.StartsWith($full + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Accord output must be outside input directories.' }
}
if (Test-Path -LiteralPath $output) { throw 'Use a new owned Accord output directory.' }
if ($pin.revision -cne '4742afae0a09c79be772f23fcf163fb5852d2775' -or $pin.files.Count -ne 296) { throw 'Expected the complete pinned Accord source closure.' }
foreach ($file in @($pin.files) + @($pin.additionalInputs)) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:' -or ($file.path -notlike 'Accord.Imaging/*' -and $file.path -notin @('.editorconfig', 'Directory.Build.props', 'NINA/3rd-party-licenses.txt'))) { throw 'Unexpected Accord input path.' }
    Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256
    $dest = "$output/source/$($file.path)"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
    [IO.File]::Copy("$NinaSourceDirectory/$($file.path)", $dest)
}
Assert-Hash "$OriginalManagedDirectory/Accord.Imaging.dll" $pin.originalAssemblySha256
foreach ($file in $pin.companions) { Assert-Hash "$OriginalManagedDirectory/$($file.name)" $file.sha256 }
$drawing = "$ExistingPackageCache/system.drawing.common/10.0.2/lib/netstandard2.0/System.Drawing.Common.dll"
Assert-Hash $drawing '4FCD098B15222356782F30F29F8BE6E34D60B12F15C4A8493FAA7763EBE1F9AA'
& "$fixture/Apply-AccordMetadata.ps1" -Source "$output/source/Accord.Imaging"
$changes = @(foreach ($file in $pin.files) {
    $hash = (Get-FileHash "$output/source/$($file.path)").Hash
    if ($hash -cne $file.sha256) { [ordered]@{ path = $file.path; before = $file.sha256; after = $hash } }
})
if ($changes.Count -ne 7) { throw 'Expected seven metadata-only Accord adaptations.' }
Save "$output/adaptations.json" ($changes | ConvertTo-Json)
Save "$output/global.json" '{"sdk":{"version":"10.0.401","rollForward":"disable"}}'
Save "$output/NuGet.Config" '<configuration><packageSources><clear/></packageSources></configuration>'
$packages = @(
    @('netstandard.library/2.0.3/netstandard.library.2.0.3.nupkg', '3EB87644F79BCFFB3C0331DBDAC3C7837265F2CDF58A7BFD93E431776F77C9BA'),
    @('microsoft.netcore.platforms/1.1.0/microsoft.netcore.platforms.1.1.0.nupkg', '15E338D24B5C39B4099389CC612841EB51FF13C07BB4829F97D39B27420E7023')
)
foreach ($package in $packages) {
    $path = "$ExistingPackageCache/$($package[0])"
    Assert-Hash $path $package[1]
    [IO.Directory]::CreateDirectory("$output/feed") | Out-Null
    [IO.File]::Copy($path, "$output/feed/$([IO.Path]::GetFileName($path))")
}
[xml] $project = Get-Content "$output/source/$($pin.project)" -Raw
$root = $project.DocumentElement
foreach ($node in @($root.SelectNodes('ItemGroup/PackageReference'))) { $null = $node.ParentNode.RemoveChild($node) }
$properties = $project.CreateElement('PropertyGroup')
$null = $root.AppendChild($properties)
$settings = [ordered]@{
    LangVersion = '7.3'; Configuration = 'Debug'; Optimize = 'false'; AllowUnsafeBlocks = 'true'; DebugType = 'portable'; DebugSymbols = 'true'
    GeneratePackageOnBuild = 'false'; EnableNETAnalyzers = 'false'; DeterministicSourcePaths = 'false'
    BaseOutputPath = "$output/bin/"; BaseIntermediateOutputPath = "$output/obj/"; MSBuildProjectExtensionsPath = "$output/obj/"
}
foreach ($pair in $settings.GetEnumerator()) { $node = $project.CreateElement($pair.Key); $node.InnerText = $pair.Value; $null = $properties.AppendChild($node) }
$references = $project.CreateElement('ItemGroup')
$null = $root.AppendChild($references)
foreach ($path in @($pin.companions | ForEach-Object { "$OriginalManagedDirectory/$($_.name)" }) + @($drawing)) {
    $node = $project.CreateElement('Reference')
    $node.SetAttribute('Include', [IO.Path]::GetFileNameWithoutExtension($path))
    $node.SetAttribute('HintPath', [IO.Path]::GetFullPath($path))
    $node.SetAttribute('Private', 'false')
    $null = $references.AppendChild($node)
}
$projectPath = "$output/source/Accord.Imaging/Accord.Imaging.Portable.csproj"
Save $projectPath $project.OuterXml
$globals = @('-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', '-p:TargetFramework=netstandard2.0', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', "-p:BaseOutputPath=$output/bin/", "-p:BaseIntermediateOutputPath=$output/obj/", "-p:MSBuildProjectExtensionsPath=$output/obj/")
Push-Location $output
try {
    if ((& $DotnetPath --version).Trim() -cne '10.0.401') { throw 'Expected SDK 10.0.401.' }
    & $DotnetPath restore $projectPath --configfile "$output/NuGet.Config" --source "$output/feed" --packages $RestorePackageDirectory @globals *> "$output/restore.log"
    if ($LASTEXITCODE) { throw 'Accord offline restore failed.' }
    & $DotnetPath msbuild $projectPath @globals -nologo '-target:ResolveReferences,GenerateAssemblyInfo,GenerateTargetFrameworkMonikerAttribute' '-getItem:Compile,ReferencePath,EmbeddedResource' '-getProperty:TargetFramework,DefineConstants,LangVersion,AllowUnsafeBlocks,Optimize,Configuration' 1> "$output/evaluated-inputs.json" 2> "$output/evaluated-inputs.stderr"
    if ($LASTEXITCODE) { throw 'Accord input evaluation failed.' }
    $evaluation = Get-Content "$output/evaluated-inputs.json" -Raw | ConvertFrom-Json
    if ($evaluation.Items.Compile.Count -ne 290 -or $evaluation.Items.EmbeddedResource.Count -ne 0 -or $evaluation.Properties.DefineConstants.Replace(';', ',') -cne $pin.symbols) { throw 'Accord compilation items, resources or symbols differ from the original profile.' }
    & $DotnetPath build $projectPath --no-restore -c Debug --nologo -m:1 -nr:false @globals *> "$output/build.log"
    if ($LASTEXITCODE) { throw 'Accord source build failed.' }
} finally { Pop-Location }
foreach ($file in @($pin.files) + @($pin.additionalInputs)) { Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256 }
foreach ($change in $changes) { Assert-Hash "$output/source/$($change.path)" $change.after }
$dll = "$output/bin/Debug/netstandard2.0/Accord.Imaging.dll"
$pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
if ([Reflection.AssemblyName]::GetAssemblyName($dll).FullName -cne $pin.originalAssemblyIdentity) { throw 'Accord assembly identity changed.' }
$stream = [IO.File]::OpenRead($dll)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
$pdbStream = [IO.File]::OpenRead($pdb)
$provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    if ($metadata.ManifestResources.Count -ne 0) { throw 'Original Accord has no manifest resources.' }
    $reader = $provider.GetMetadataReader()
    $documents = @(foreach ($handle in $reader.Documents) {
        $document = $reader.GetDocument($handle)
        $path = $reader.GetString($document.Name)
        $hash = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash))
        Assert-Hash $path $hash
        [ordered]@{ path = $path; sha256 = $hash }
    })
    $cv = @($pe.ReadDebugDirectory() | Where-Object Type -EQ 'CodeView')
    $id = $reader.DebugMetadataHeader.Id
    if ($cv.Count -ne 1 -or $pe.ReadCodeViewDebugDirectoryData($cv[0]).Guid -ne [Guid]::new([byte[]]$id[0..15]) -or $cv[0].Stamp -ne [BitConverter]::ToUInt32([byte[]]$id[16..19])) { throw 'Accord PE/PDB identity differs.' }
    $options = @($reader.CustomDebugInformation | ForEach-Object { $reader.GetCustomDebugInformation($_) } | Where-Object { $reader.GetGuid($_.Kind) -eq [Guid]'b5feec05-8cd0-4a83-96da-466284bb4bd8' })
    Save "$output/source-proof.json" ([ordered]@{ pePdbIdentityMatch = $true; documents = $documents; compilationOptions = [Text.Encoding]::UTF8.GetString($reader.GetBlobBytes($options[0].Value)).Split([char]0) } | ConvertTo-Json -Depth 5)
} finally { $provider.Dispose(); $pdbStream.Dispose(); $pe.Dispose(); $stream.Dispose() }
foreach ($path in @($PSCommandPath, "$fixture/Apply-AccordMetadata.ps1", "$fixture/Accord.upstream.json", "$fixture/README.md")) {
    $destination = "$output/build-instructions/$([IO.Path]::GetFileName($path))"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy($path, $destination)
}
Save "$output/accord-consumer-intake.json" ([ordered]@{
    status = 'source-proven-Accord-component-behavior-results-separate'; revision = $pin.revision; sourceFiles = 288; sourceDocuments = $documents.Count
    assembly = $dll; sha256 = (Get-FileHash $dll).Hash; pdb = $pdb; pdbSha256 = (Get-FileHash $pdb).Hash
    sourceProofSha256 = (Get-FileHash "$output/source-proof.json").Hash; generatedProject = $projectPath; generatedProjectSha256 = (Get-FileHash $projectPath).Hash
    evaluatedInputsSha256 = (Get-FileHash "$output/evaluated-inputs.json").Hash; adaptationsSha256 = (Get-FileHash "$output/adaptations.json").Hash
    originalAssemblySha256 = $pin.originalAssemblySha256; companions = $pin.companions; drawingCompileContract = $drawing; drawingCompileContractSha256 = (Get-FileHash $drawing).Hash
    license = "$output/source/NINA/3rd-party-licenses.txt"; scope = 'Eight metadata expressions in seven files; numerical kernels unchanged; no runtime GdiPlus'
} | ConvertTo-Json -Depth 5)
Write-Output "Source-built Accord candidate: $output/accord-consumer-intake.json"
