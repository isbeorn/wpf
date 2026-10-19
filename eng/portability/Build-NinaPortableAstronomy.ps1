[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeManifest,
    [Parameter(Mandatory)][string] $MarkupCompiler,
    [Parameter(Mandatory)][string] $WinFxTargets,
    [Parameter(Mandatory)][string] $PortableReferencesTargets,
    [Parameter(Mandatory)][string] $OriginalManagedClosure,
    [Parameter(Mandatory)][string] $MvvmGeneratorPath,
    [Parameter(Mandatory)][string] $SystemEventsContractPath,
    [Parameter(Mandatory)][string] $ProtoCompilerPath,
    [Parameter(Mandatory)][string] $GrpcPluginPath,
    [Parameter(Mandatory)][string] $ProtoIncludeDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Portability/Tests/NinaPortableAstronomy'))
$pin = Get-Content "$fixture/upstream.json" -Raw | ConvertFrom-Json
if ($pin.revision -cne '4742afae0a09c79be772f23fcf163fb5852d2775' -or $pin.files.Count -ne 670) { throw 'Expected the complete pinned 670-file Core/Astrometry/native closure.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$utf8 = [Text.UTF8Encoding]::new($false)
function Save([string] $Path, [string] $Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Assert-Hash([string] $Path, [string] $Hash) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $Hash) { throw "Pinned input differs: $Path" }
}
function Element($Document, $Parent, [string] $Name, [hashtable] $Attributes) {
    $node = $Document.CreateElement($Name)
    foreach ($pair in $Attributes.GetEnumerator()) { $node.SetAttribute($pair.Key, $pair.Value) }
    $null = $Parent.AppendChild($node)
    return $node
}
foreach ($inputPath in @($NinaSourceDirectory, $FrameworkRuntimeDirectory, (Split-Path $OriginalManagedClosure -Parent), $fixture)) {
    $full = [IO.Path]::GetFullPath($inputPath).TrimEnd('\', '/')
    if ($output -eq $full -or $output.StartsWith($full + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside input directories.' }
}
if (Test-Path $output) { if (@(Get-ChildItem $output -Force).Count) { throw 'Use a new or empty owned output directory.' } }
foreach ($tool in $pin.tools) {
    $path = Get-Variable $tool.parameter -ValueOnly
    Assert-Hash $path $tool.sha256
}
Assert-Hash $OriginalManagedClosure $pin.originalClosureSha256
$closure = Get-Content $OriginalManagedClosure -Raw | ConvertFrom-Json
if ($closure.revision -cne $pin.revision -or $closure.files.Count -ne 160) { throw 'Expected original160 closure.' }
$closureDirectory = Split-Path $OriginalManagedClosure -Parent
foreach ($file in $closure.files) {
    $group = if ($file.outputPath.Contains('/')) { 'satellites' } else { 'managed' }
    Assert-Hash "$closureDirectory/$group/$($file.outputPath)" $file.sha256
}
$family = @(Get-Content $FrameworkRuntimeManifest -Raw | ConvertFrom-Json | Where-Object os -eq Unix)
if ($family.Count -ne 1 -or $family[0].files.Count -ne 143) { throw 'Expected coherent whole143 Unix family.' }
foreach ($file in $family[0].files) { Assert-Hash "$FrameworkRuntimeDirectory/$($file.path)" $file.sha256 }
if (@(Get-ChildItem -LiteralPath $FrameworkRuntimeDirectory -Recurse -File).Count -ne 143) { throw 'Framework runtime directory contains extra files.' }
foreach ($file in $pin.files) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:') { throw 'Unsafe source manifest path.' }
    Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256
    $dest = "$output/source/$($file.path)"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
    [IO.File]::Copy("$NinaSourceDirectory/$($file.path)", $dest)
}
& "$fixture/Apply-Boundary.ps1" -Source "$output/source"
$changes = foreach ($file in $pin.files) {
    $hash = (Get-FileHash "$output/source/$($file.path)").Hash
    if ($hash -cne $file.sha256) { [ordered]@{ path = $file.path; before = $file.sha256; after = $hash } }
}
if (@($changes).Count -ne 6) { throw 'Expected exactly six adapted upstream files.' }
Save "$output/adaptations.json" ($changes | ConvertTo-Json -Depth 4)
Save "$output/global.json" '{"sdk":{"version":"10.0.401","rollForward":"disable"}}'
Save "$output/Directory.Build.props" '<Project/>'
Save "$output/Directory.Build.targets" '<Project/>'
Save "$output/NuGet.Config" '<configuration><packageSources><clear/></packageSources></configuration>'
Push-Location $output
try {
    if ((& $DotnetPath --version).Trim() -cne '10.0.401') { throw 'Expected SDK 10.0.401.' }
    $generated = "$output/source/NINA.Core/Generated"
    New-Item -ItemType Directory -Force $generated | Out-Null
    Assert-Hash "$ProtoIncludeDirectory/google/protobuf/empty.proto" $pin.protobufEmptySha256
    & $ProtoCompilerPath "--proto_path=$output/source/NINA.Core" "--proto_path=$ProtoIncludeDirectory" "--csharp_out=$generated" "--grpc_out=$generated" "--plugin=protoc-gen-grpc=$GrpcPluginPath" 'Protos/API/ASCOM/Camera/CameraService.proto'
    if ($LASTEXITCODE -ne 0) { throw 'Original protocol generation failed.' }
    foreach ($name in @('NINA.Core', 'NINA.Astrometry')) {
        [xml] $original = Get-Content "$output/source/$name/$name.csproj" -Raw
        $project = [xml] '<Project/>'
        $root = $project.DocumentElement
        $null = Element $project $root Import @{ Project = 'Sdk.props'; Sdk = 'Microsoft.NET.Sdk' }
        $defaults = Element $project $root ItemGroup @{}
        $null = Element $project $defaults Page @{ Include = '**/*.xaml' }
        foreach ($node in $original.DocumentElement.ChildNodes) { $null = $root.AppendChild($project.ImportNode($node, $true)) }
        foreach ($node in @($root.SelectNodes('ItemGroup/PackageReference|ItemGroup/ProjectReference|ItemGroup/Protobuf|Target[@Name="PostBuild"]'))) { $null = $node.ParentNode.RemoveChild($node) }
        $properties = Element $project $root PropertyGroup @{}
        $settings = [ordered]@{
            TargetFramework = 'net10.0'; UseWPF = 'false'; ImportWindowsDesktopTargets = 'false'; EnableWindowsTargeting = 'false'; RuntimeIdentifier = ''; SelfContained = 'false'
            AssemblyName = $name; RootNamespace = $name; AllowUnsafeBlocks = 'true'; GeneratePackageOnBuild = 'false'; EnableNETAnalyzers = 'false'; RuntimeFrameworkVersion = '10.0.12'; RollForward = 'Disable'
            BaseOutputPath = "$output/bin/$name/"; BaseIntermediateOutputPath = "$output/obj/$name/"; MSBuildProjectExtensionsPath = "$output/obj/$name/"
            WpfPortableTargetOS = 'Unix'; WpfPortableRuntimeDirectory = [IO.Path]::GetFullPath($FrameworkRuntimeDirectory); _PresentationBuildTasksAssembly = [IO.Path]::GetFullPath($MarkupCompiler)
        }
        foreach ($pair in $settings.GetEnumerator()) { $node = Element $project $properties $pair.Key @{}; $node.InnerText = $pair.Value }
        $references = Element $project $root ItemGroup @{}
        foreach ($file in $closure.files | Where-Object { !$_.outputPath.Contains('/') -and $_.outputPath -notlike 'NINA*' }) {
            $null = Element $project $references Reference @{ Include = [IO.Path]::GetFileNameWithoutExtension($file.outputPath); HintPath = "$closureDirectory/managed/$($file.outputPath)"; Private = 'true' }
        }
        if ($name -eq 'NINA.Astrometry') {
            $null = Element $project $references Reference @{ Include = 'NINA.Core'; HintPath = "$output/bin/NINA.Core/Release/net10.0/NINA.Core.dll"; Private = 'true' }
            $null = Element $project $references Reference @{ Include = 'NINA.Profile'; HintPath = "$closureDirectory/managed/NINA.Profile.dll"; Private = 'true' }
        }
        $null = Element $project $references Reference @{ Include = "$FrameworkRuntimeDirectory/*.dll"; Private = 'true' }
        $null = Element $project $references Analyzer @{ Include = [IO.Path]::GetFullPath($MvvmGeneratorPath) }
        $null = Element $project $references Reference @{ Include = 'Microsoft.Win32.SystemEvents'; HintPath = [IO.Path]::GetFullPath($SystemEventsContractPath); Private = 'false' }
        $null = Element $project $root Import @{ Project = 'Sdk.targets'; Sdk = 'Microsoft.NET.Sdk' }
        $null = Element $project $root Import @{ Project = [IO.Path]::GetFullPath($WinFxTargets) }
        $null = Element $project $root Import @{ Project = [IO.Path]::GetFullPath($PortableReferencesTargets) }
        $path = "$output/source/$name/$name.Portable.csproj"
        Save $path $project.OuterXml
        & $DotnetPath build $path -c Release --nologo -m:1 -nr:false --configfile "$output/NuGet.Config" -p:UseSharedCompilation=false -p:NuGetAudit=false "-p:BaseIntermediateOutputPath=$output/obj/$name/" "-p:MSBuildProjectExtensionsPath=$output/obj/$name/" *> "$output/$name.build.log"
        if ($LASTEXITCODE -ne 0) { throw "Source build failed: $name. See $output/$name.build.log" }
    }
} finally { Pop-Location }
foreach ($file in $pin.files) { Assert-Hash "$NinaSourceDirectory/$($file.path)" $file.sha256 }
$outputs = foreach ($name in @('NINA.Core', 'NINA.Astrometry')) {
    $dll = "$output/bin/$name/Release/net10.0/$name.dll"
    $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
    [ordered]@{ name = $name; dll = $dll; sha256 = (Get-FileHash $dll).Hash; pdb = $pdb; pdbSha256 = (Get-FileHash $pdb).Hash; identity = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName }
}
Save "$output/managed-build.json" ([ordered]@{ status = 'managed-source-build-pass-native-execution-separate'; revision = $pin.revision; frameworkManifestSha256 = (Get-FileHash $FrameworkRuntimeManifest).Hash; outputs = @($outputs); changes = @($changes) } | ConvertTo-Json -Depth 6)
