[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeManifest,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ExpectedFrameworkRuntimeManifestSha256,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int] $ExpectedFrameworkRuntimeFileCount,
    [Parameter(Mandatory)][string] $MarkupCompiler,
    [Parameter(Mandatory)][string] $WinFxTargets,
    [Parameter(Mandatory)][string] $PortableReferencesTargets,
    [Parameter(Mandatory)][string] $OriginalManagedClosure,
    [Parameter(Mandatory)][string] $AvalonDockPath,
    [string] $AvalonEditPath,
    [Parameter(Mandatory)][string] $SystemEventsContractPath,
    [Parameter(Mandatory)][string] $MvvmGeneratorPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DotnetPath = 'dotnet',
    [switch] $SimulatorQualification,
    [string] $OfficialPluginDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Portability/Tests/NinaPortableApplication'))
$pin = Get-Content (Join-Path $fixture 'upstream.json') -Raw | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)
$simulationFixture = Join-Path $fixture 'SimulatorQualification'
$simulationEdits = @()
$pluginAssembly = $null
$pluginIntakeSource = "$simulationFixture/SimulatorPluginIntake.cs"

function Write-Utf8([string] $Path, [string] $Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Assert-Hash([string] $Path, [string] $Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $Expected) { throw "Pinned input differs: $Path" }
}

function Add-Element($Document, $Parent, [string] $Name, [hashtable] $Attributes) {
    $element = $Document.CreateElement($Name)
    foreach ($entry in $Attributes.GetEnumerator()) { $element.SetAttribute($entry.Key, $entry.Value) }
    $null = $Parent.AppendChild($element)
    return $element
}

function Read-ResourceKeys([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.ManifestResources) {
            $resource = $metadata.GetManifestResource($handle)
            if ($metadata.GetString($resource.Name) -cne 'NINA.g.resources') { continue }
            $blob = $pe.GetSectionData($pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress + [int]$resource.Offset).GetReader()
            $resourceStream = [IO.MemoryStream]::new($blob.ReadBytes($blob.ReadInt32()), $false)
            $reader = [Resources.ResourceReader]::new($resourceStream)
            try {
                $enumerator = $reader.GetEnumerator()
                while ($enumerator.MoveNext()) { [string]$enumerator.Key }
            } finally { $reader.Dispose(); $resourceStream.Dispose() }
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

$NinaSourceDirectory = [IO.Path]::GetFullPath($NinaSourceDirectory)
$FrameworkRuntimeDirectory = [IO.Path]::GetFullPath($FrameworkRuntimeDirectory)
$OriginalManagedClosure = [IO.Path]::GetFullPath($OriginalManagedClosure)
$output = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($inputDirectory in @($NinaSourceDirectory, $FrameworkRuntimeDirectory, (Split-Path $OriginalManagedClosure -Parent), $fixture)) {
    $prefix = $inputDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($output -eq $inputDirectory -or $output.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside all input directories.' }
}
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Use a new or empty owned OutputDirectory.' }
}
if ($pin.files.Count -ne 680 -or $pin.revision -cne '4742afae0a09c79be772f23fcf163fb5852d2775') { throw 'Expected the complete pinned 680-file application source manifest.' }
foreach ($file in $pin.files) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:' -or ($file.path -notlike 'NINA/*' -and $file.path -notin @('CommonAssemblyInfo.cs', 'Directory.Build.props'))) { throw 'Unexpected source manifest path.' }
    Assert-Hash (Join-Path $NinaSourceDirectory $file.path) $file.sha256
}
Assert-Hash $FrameworkRuntimeManifest $ExpectedFrameworkRuntimeManifestSha256.ToUpperInvariant()
$family = @(Get-Content -LiteralPath $FrameworkRuntimeManifest -Raw | ConvertFrom-Json | Where-Object os -CEQ 'Unix')
if ($family.Count -ne 1 -or $family[0].configuration -cne 'Release' -or $family[0].files.Count -ne $ExpectedFrameworkRuntimeFileCount) { throw 'Expected exactly one pinned whole Unix Release framework family with the supplied file count.' }
$frameworkRoot = [IO.Path]::GetFullPath($FrameworkRuntimeDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$frameworkPrefix = $frameworkRoot + [IO.Path]::DirectorySeparatorChar
$frameworkPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $family[0].files) {
    if ($file.path -isnot [string] -or [string]::IsNullOrWhiteSpace($file.path) -or [IO.Path]::IsPathRooted($file.path) -or $file.path -match '\\|:' -or @($file.path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) { throw 'Unsafe framework manifest path.' }
    if (!$frameworkPaths.Add($file.path)) { throw 'Duplicate framework manifest path.' }
    if ($file.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid framework file SHA-256.' }
    if ([IO.Path]::GetFileName($file.path) -match '^Avalonia(?:\.|$)' -and [IO.Path]::GetExtension($file.path) -in @('.dll', '.pdb', '.so', '.dylib')) { throw 'Legacy Avalonia runtime asset is outside the owned framework profile.' }
    $path = [IO.Path]::GetFullPath((Join-Path $frameworkRoot $file.path))
    if (!$path.StartsWith($frameworkPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Framework manifest path escapes the runtime directory.' }
    Assert-Hash $path $file.sha256.ToUpperInvariant()
    if ($file.PSObject.Properties.Name -contains 'bytes' -and (Get-Item -LiteralPath $path).Length -ne $file.bytes) { throw 'Framework file length differs from its manifest.' }
}
$frameworkFiles = @(Get-ChildItem -LiteralPath $frameworkRoot -Recurse -File -Force)
if ($frameworkFiles.Count -ne $ExpectedFrameworkRuntimeFileCount) { throw 'Framework runtime directory file count differs from the pinned family.' }
foreach ($file in $frameworkFiles) {
    $relative = [IO.Path]::GetRelativePath($frameworkRoot, $file.FullName).Replace('\', '/')
    if (!$frameworkPaths.Contains($relative)) { throw 'Framework runtime directory contains an unlisted file.' }
}
$frameworkProfile = Get-Content (Join-Path $PSScriptRoot '../../src/Portability/Packaging/PackageProfile.json') -Raw | ConvertFrom-Json
foreach ($name in @($frameworkProfile.frameworkAssemblies) + @($frameworkProfile.backendAssemblies) + @($frameworkProfile.genuineManagedContracts)) {
    if (!$frameworkPaths.Contains("$name.dll")) { throw "Required profile runtime assembly is missing: $name" }
}
$closure = Get-Content -LiteralPath $OriginalManagedClosure -Raw | ConvertFrom-Json
$originalBaseline = 'historical-original160'
if ((Get-FileHash $OriginalManagedClosure).Hash -cne $pin.originalClosureManifestSha256) {
    $currentBaseline = Get-Content (Join-Path $fixture '../NinaPortableViews/CurrentBaselinePin.json') -Raw | ConvertFrom-Json
    Assert-Hash $OriginalManagedClosure $currentBaseline.closureManifestSha256
    if ($currentBaseline.revision -cne $pin.revision -or $closure.pinnedClosureSha256 -cne $currentBaseline.canonicalClosureSha256) { throw 'The named current stock baseline differs.' }
    $originalBaseline = $currentBaseline.name
}
if ($closure.revision -cne $pin.revision -or $closure.files.Count -ne 160) { throw 'Expected the original pinned 160-PE closure.' }
$closureDirectory = Split-Path $OriginalManagedClosure -Parent
foreach ($file in $closure.files) {
    if ($file.outputPath -match '(^|/)\.\.(/|$)|\\|:') { throw 'Unexpected original closure path.' }
    $group = if ($file.outputPath.Contains('/')) { 'satellites' } else { 'managed' }
    Assert-Hash (Join-Path "$closureDirectory/$group" $file.outputPath) $file.sha256
}
$currentComponents = $null
$componentBaseline = 'historical-source-built-components'
$avalonDockHash = $pin.avalonDockSha256
if ($originalBaseline -cne 'historical-original160') {
    Assert-Hash "$fixture/CurrentComponents.json" $pin.currentComponentsManifestSha256
    $currentComponents = Get-Content "$fixture/CurrentComponents.json" -Raw | ConvertFrom-Json
    if ($currentComponents.originalBaseline -cne $originalBaseline -or $currentComponents.frameworkManifestSha256 -cne $ExpectedFrameworkRuntimeManifestSha256.ToUpperInvariant()) { throw 'Current source-built components require their exact stock baseline and framework family.' }
    $componentBaseline = $currentComponents.name
    $avalonDockHash = $currentComponents.avalonDockSha256
    if ([Reflection.AssemblyName]::GetAssemblyName($AvalonDockPath).FullName -cne $currentComponents.avalonDockIdentity) { throw 'Current source-built AvalonDock identity differs.' }
}
Assert-Hash $AvalonDockPath $avalonDockHash
$sourceBuiltAvalonEdit = ![string]::IsNullOrWhiteSpace($AvalonEditPath)
$avalonEditHash = $null
if ($sourceBuiltAvalonEdit) {
    if ($null -eq $currentComponents) { throw 'Source-adapted AvalonEdit requires current component pins and an exact coherent framework family.' }
    $avalonEditHash = $currentComponents.avalonEditSha256
    Assert-Hash $AvalonEditPath $avalonEditHash
    if ([Reflection.AssemblyName]::GetAssemblyName($AvalonEditPath).FullName -cne $currentComponents.avalonEditIdentity) { throw 'Current source-built AvalonEdit identity differs.' }
}
Assert-Hash $SystemEventsContractPath $pin.systemEventsContractSha256
Assert-Hash $MvvmGeneratorPath $pin.mvvmGeneratorSha256
Assert-Hash (Join-Path $fixture 'App.xaml.cs.patch') $pin.consolePatchSha256
Assert-Hash (Join-Path $fixture 'PortableEntry.cs') $pin.portableEntrySha256

if ($SimulatorQualification) {
    if ([string]::IsNullOrWhiteSpace($OfficialPluginDirectory)) { throw 'Simulator qualification requires the approved original official-template-derived fixture directory.' }
    Assert-Hash "$simulationFixture/manifest.json" $pin.simulatorQualificationManifestSha256
    $simulationPin = Get-Content "$simulationFixture/manifest.json" -Raw | ConvertFrom-Json
    if ($simulationPin.buildFlag -cne 'NINA_SIMULATOR_QUALIFICATION') { throw 'Unexpected immutable simulation build flag.' }
    foreach ($file in $simulationPin.files) {
        if ($file.path -match '[/\\:]|^\.\.$') { throw 'Unexpected simulation fixture path.' }
        Assert-Hash (Join-Path $simulationFixture $file.path) $file.sha256
    }
    $pluginPin = Get-Content "$simulationFixture/plugin-upstream.json" -Raw | ConvertFrom-Json
    foreach ($file in $pluginPin.files) {
        if ($file.path -notlike 'NINA.Plugin/*' -or $file.path -match '(^|/)\.\.(/|$)|\\|:') { throw 'Unexpected plugin source path.' }
        Assert-Hash (Join-Path $NinaSourceDirectory $file.path) $file.sha256
        $destination = Join-Path "$output/source" $file.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy((Join-Path $NinaSourceDirectory $file.path), $destination)
    }
    $officialPluginPin = Get-Content "$simulationFixture/official-plugin-files.json" -Raw | ConvertFrom-Json
    if ($null -ne $currentComponents) {
        $historicalPluginPin = $officialPluginPin
        $officialPluginPin = $currentComponents.officialTemplateFixture
        if ((($officialPluginPin.files.path | Sort-Object) -join '|') -cne (($historicalPluginPin.files.path | Sort-Object) -join '|')) { throw 'Current fixture filenames differ from the approved four-file closure.' }
        $runtimeIntakeText = [IO.File]::ReadAllText($pluginIntakeSource)
        foreach ($historicalFile in $historicalPluginPin.files) {
            $replacement = @($officialPluginPin.files | Where-Object path -CEQ $historicalFile.path)
            if ($replacement.Count -ne 1 -or $replacement[0].sha256 -cnotmatch '^[A-F0-9]{64}$') { throw 'Invalid current fixture hash binding.' }
            if ($replacement[0].sha256 -cne $historicalFile.sha256) {
                if ($runtimeIntakeText.IndexOf($historicalFile.sha256, [StringComparison]::Ordinal) -lt 0 -or $runtimeIntakeText.IndexOf($historicalFile.sha256, [StringComparison]::Ordinal) -ne $runtimeIntakeText.LastIndexOf($historicalFile.sha256, [StringComparison]::Ordinal)) { throw 'Expected exactly one historical runtime fixture hash.' }
                $runtimeIntakeText = $runtimeIntakeText.Replace($historicalFile.sha256, $replacement[0].sha256)
            }
        }
        $pluginIntakeSource = "$output/generated/SimulatorPluginIntake.cs"
        Write-Utf8 $pluginIntakeSource $runtimeIntakeText
    }
    foreach ($file in $officialPluginPin.files) {
        Assert-Hash (Join-Path $OfficialPluginDirectory $file.path) $file.sha256
        $destination = Join-Path "$output/qualification-inputs/QualificationPlugins" $file.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy((Join-Path $OfficialPluginDirectory $file.path), $destination)
    }
}

foreach ($file in $pin.files) {
    $destination = Join-Path "$output/source" $file.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy((Join-Path $NinaSourceDirectory $file.path), $destination)
}
Assert-Hash (Join-Path $NinaSourceDirectory '.editorconfig') $pin.editorConfigSha256
[IO.File]::Copy((Join-Path $NinaSourceDirectory '.editorconfig'), "$output/source/.editorconfig")
$appPath = "$output/source/NINA/App.xaml.cs"
Assert-Hash $appPath $pin.consoleSourceSha256
$source = [IO.File]::ReadAllText($appPath)
$before = @'
            AttachConsole(ATTACH_PARENT_PROCESS);
            _commandLineOptions = new CommandLineOptions(e.Args);
            FreeConsole(ATTACH_PARENT_PROCESS);
'@
$after = @'
            if (OperatingSystem.IsWindows()) {
                AttachConsole(ATTACH_PARENT_PROCESS);
            }
            _commandLineOptions = new CommandLineOptions(e.Args);
            if (OperatingSystem.IsWindows()) {
                FreeConsole(ATTACH_PARENT_PROCESS);
            }
'@
$before = $before.Replace("`r`n", "`n")
$after = $after.Replace("`r`n", "`n")
if ($source.IndexOf($before, [StringComparison]::Ordinal) -lt 0 -or $source.IndexOf($before, [StringComparison]::Ordinal) -ne $source.LastIndexOf($before, [StringComparison]::Ordinal)) { throw 'The original console/parser block must match exactly once.' }
Write-Utf8 $appPath ($source.Replace($before, $after))
Assert-Hash $appPath $pin.consoleOutputSha256
if ($SimulatorQualification) {
    $simulationEdits = @(Get-Content "$simulationFixture/source-edits.json" -Raw | ConvertFrom-Json)
    foreach ($file in $simulationEdits) {
        $path = Join-Path "$output/source" $file.path
        Assert-Hash $path $file.inputSha256
        $value = [IO.File]::ReadAllText($path)
        foreach ($edit in $file.edits) {
            if ($value.IndexOf($edit.before, [StringComparison]::Ordinal) -lt 0 -or $value.IndexOf($edit.before, [StringComparison]::Ordinal) -ne $value.LastIndexOf($edit.before, [StringComparison]::Ordinal)) { throw "Simulation edit must match exactly once: $($file.path)" }
            $value = $value.Replace($edit.before, $edit.after)
        }
        Write-Utf8 $path $value
        Assert-Hash $path $file.outputSha256
    }
}

[xml] $originalProject = Get-Content "$NinaSourceDirectory/NINA/NINA.csproj" -Raw
$project = [xml]'<Project/>'
$root = $project.DocumentElement
$null = Add-Element $project $root Import @{ Project = 'Sdk.props'; Sdk = 'Microsoft.NET.Sdk' }
$defaults = Add-Element $project $root ItemGroup @{}
$null = Add-Element $project $defaults ApplicationDefinition @{ Include = 'App.xaml' }
$null = Add-Element $project $defaults Page @{ Include = '**/*.xaml'; Exclude = 'App.xaml' }
foreach ($node in $originalProject.DocumentElement.ChildNodes) { $null = $root.AppendChild($project.ImportNode($node, $true)) }
$substitutions = [Collections.Generic.List[object]]::new()
foreach ($node in @($root.SelectNodes('ItemGroup/ProjectReference|ItemGroup/PackageReference|Target[@Name="PostBuild"]'))) {
    $substitutions.Add([ordered]@{ kind = 'build-graph-substitution'; xml = $node.OuterXml })
    $null = $node.ParentNode.RemoveChild($node)
}
foreach ($node in @($root.SelectNodes('ItemGroup/Content|ItemGroup/ContentWithTargetPath'))) {
    if ($node.Include -like 'External\*' -and !(Test-Path -LiteralPath (Join-Path "$output/source/NINA" $node.Include))) {
        $substitutions.Add([ordered]@{ kind = 'absent-native-deployment-item'; xml = $node.OuterXml })
        $null = $node.ParentNode.RemoveChild($node)
    }
}
$properties = Add-Element $project $root PropertyGroup @{}
$settings = [ordered]@{
    AssemblyName = 'NINA'; RootNamespace = 'NINA'; StartupObject = 'NINA.PortableEntry'; TargetFramework = 'net10.0'; UseWPF = 'false'; ImportWindowsDesktopTargets = 'false'
    EnableWindowsTargeting = 'false'; SelfContained = 'false'; RuntimeIdentifier = ''; UseAppHost = 'false'; GenerateManifests = 'false'
    RuntimeFrameworkVersion = '10.0.12'; RollForward = 'Disable'; AllowUnsafeBlocks = 'true'; GeneratePackageOnBuild = 'false'; EnableNETAnalyzers = 'false'
    BaseOutputPath = "$output/bin/"; BaseIntermediateOutputPath = "$output/obj/"; MSBuildProjectExtensionsPath = "$output/obj/"
    _PresentationBuildTasksAssembly = [IO.Path]::GetFullPath($MarkupCompiler)
}
foreach ($entry in $settings.GetEnumerator()) { $element = Add-Element $project $properties $entry.Key @{}; $element.InnerText = $entry.Value }
if ($SimulatorQualification) {
    $element = Add-Element $project $properties DefineConstants @{}
    $element.InnerText = '$(DefineConstants);NINA_SIMULATOR_QUALIFICATION'
}
$references = Add-Element $project $root ItemGroup @{}
if ($SimulatorQualification) {
    foreach ($name in @('SimulatorQualification.cs', 'SimulationDeviceChooser.cs', 'SimulationUsbDeviceWatcher.cs', 'PortableWindowPlacement.cs')) {
        $null = Add-Element $project $references Compile @{ Include = "$simulationFixture/$name"; Link = "Portability/$name" }
    }
}
$null = Add-Element $project $references Compile @{ Include = "$fixture/PortableEntry.cs"; Link = 'Portability/PortableEntry.cs' }
foreach ($file in $closure.files | Where-Object { !$_.outputPath.Contains('/') -and $_.outputPath -notin @('NINA.dll', 'AvalonDock.dll') }) {
    $referencePath = if ($sourceBuiltAvalonEdit -and $file.outputPath -ceq 'ICSharpCode.AvalonEdit.dll') { [IO.Path]::GetFullPath($AvalonEditPath) } elseif ($SimulatorQualification -and $file.outputPath -ceq 'NINA.Plugin.dll') { "$output/plugin-bin/Release/net10.0/NINA.Plugin.dll" } else { "$closureDirectory/managed/$($file.outputPath)" }
    $null = Add-Element $project $references Reference @{ Include = [IO.Path]::GetFileNameWithoutExtension($file.outputPath); HintPath = $referencePath; Private = 'true' }
}
$null = Add-Element $project $references Reference @{ Include = 'AvalonDock'; HintPath = [IO.Path]::GetFullPath($AvalonDockPath); Private = 'true' }
$null = Add-Element $project $references Reference @{ Include = "$FrameworkRuntimeDirectory/*.dll"; Private = 'true' }
$null = Add-Element $project $references Reference @{ Include = 'Microsoft.Win32.SystemEvents'; HintPath = [IO.Path]::GetFullPath($SystemEventsContractPath); Private = 'false' }
$null = Add-Element $project $references Analyzer @{ Include = [IO.Path]::GetFullPath($MvvmGeneratorPath) }
$null = Add-Element $project $references None @{ Include = "$fixture/upstream.json"; Link = 'ThirdPartyNotices/NINA/upstream.json'; CopyToOutputDirectory = 'PreserveNewest' }
$null = Add-Element $project $references None @{ Include = "$output/source/NINA/LICENSE.txt"; Link = 'ThirdPartyNotices/NINA/LICENSE.txt'; CopyToOutputDirectory = 'PreserveNewest' }
foreach ($name in @('LICENSE', 'upstream.json')) {
    $null = Add-Element $project $references None @{ Include = "$fixture/../../Dependencies/AvalonDock/$name"; Link = "ThirdPartyNotices/AvalonDock/$name"; CopyToOutputDirectory = 'PreserveNewest'; CopyToPublishDirectory = 'PreserveNewest' }
}
$null = Add-Element $project $root Import @{ Project = 'Sdk.targets'; Sdk = 'Microsoft.NET.Sdk' }
$null = Add-Element $project $root Import @{ Project = [IO.Path]::GetFullPath($WinFxTargets) }
$null = Add-Element $project $root Import @{ Project = [IO.Path]::GetFullPath($PortableReferencesTargets) }
$projectPath = "$output/source/NINA/NINA.Portable.csproj"
Write-Utf8 $projectPath $project.OuterXml
Write-Utf8 "$output/global.json" '{"sdk":{"version":"10.0.401","rollForward":"disable","allowPrerelease":false}}'
Write-Utf8 "$output/Directory.Build.targets" '<Project/>'
Write-Utf8 "$output/NuGet.Config" '<configuration><packageSources><clear/></packageSources></configuration>'
Write-Utf8 "$output/build-substitutions.json" ($substitutions | ConvertTo-Json -Depth 5)
if ($SimulatorQualification) {
    [xml] $originalPluginProject = Get-Content "$NinaSourceDirectory/NINA.Plugin/NINA.Plugin.csproj" -Raw
    $pluginProject = [xml]'<Project/>'
    $pluginRoot = $pluginProject.DocumentElement
    $null = Add-Element $pluginProject $pluginRoot Import @{ Project = 'Sdk.props'; Sdk = 'Microsoft.NET.Sdk' }
    foreach ($node in $originalPluginProject.DocumentElement.ChildNodes) { $null = $pluginRoot.AppendChild($pluginProject.ImportNode($node, $true)) }
    foreach ($node in @($pluginRoot.SelectNodes('ItemGroup/ProjectReference|ItemGroup/PackageReference|Target[@Name="PostBuild"]'))) { $null = $node.ParentNode.RemoveChild($node) }
    $pluginProperties = Add-Element $pluginProject $pluginRoot PropertyGroup @{}
    $pluginSettings = [ordered]@{
        AssemblyName = 'NINA.Plugin'; RootNamespace = 'NINA.Plugin'; TargetFramework = 'net10.0'; UseWPF = 'false'; ImportWindowsDesktopTargets = 'false'; EnableWindowsTargeting = 'false'
        GeneratePackageOnBuild = 'false'; EnableNETAnalyzers = 'false'; AllowUnsafeBlocks = 'true'; DefineConstants = '$(DefineConstants);NINA_SIMULATOR_QUALIFICATION'
        BaseOutputPath = "$output/plugin-bin/"; BaseIntermediateOutputPath = "$output/plugin-obj/"; MSBuildProjectExtensionsPath = "$output/plugin-obj/"
    }
    foreach ($entry in $pluginSettings.GetEnumerator()) { $element = Add-Element $pluginProject $pluginProperties $entry.Key @{}; $element.InnerText = $entry.Value }
    $pluginReferences = Add-Element $pluginProject $pluginRoot ItemGroup @{}
    $null = Add-Element $pluginProject $pluginReferences Compile @{ Include = $pluginIntakeSource; Link = 'Portability/SimulatorPluginIntake.cs' }
    foreach ($file in $closure.files | Where-Object { !$_.outputPath.Contains('/') -and $_.outputPath -notin @('NINA.dll', 'NINA.Plugin.dll', 'AvalonDock.dll') }) {
        $pluginReferencePath = if ($sourceBuiltAvalonEdit -and $file.outputPath -ceq 'ICSharpCode.AvalonEdit.dll') { [IO.Path]::GetFullPath($AvalonEditPath) } else { "$closureDirectory/managed/$($file.outputPath)" }
        $null = Add-Element $pluginProject $pluginReferences Reference @{ Include = [IO.Path]::GetFileNameWithoutExtension($file.outputPath); HintPath = $pluginReferencePath; Private = 'true' }
    }
    $null = Add-Element $pluginProject $pluginReferences Reference @{ Include = 'AvalonDock'; HintPath = [IO.Path]::GetFullPath($AvalonDockPath); Private = 'true' }
    $null = Add-Element $pluginProject $pluginReferences Reference @{ Include = "$FrameworkRuntimeDirectory/*.dll"; Private = 'true' }
    $null = Add-Element $pluginProject $pluginReferences Reference @{ Include = 'Microsoft.Win32.SystemEvents'; HintPath = [IO.Path]::GetFullPath($SystemEventsContractPath); Private = 'false' }
    $null = Add-Element $pluginProject $pluginRoot Import @{ Project = 'Sdk.targets'; Sdk = 'Microsoft.NET.Sdk' }
    $null = Add-Element $pluginProject $pluginRoot Import @{ Project = [IO.Path]::GetFullPath($PortableReferencesTargets) }
    $pluginProjectPath = "$output/source/NINA.Plugin/NINA.Plugin.Portable.csproj"
    Write-Utf8 $pluginProjectPath $pluginProject.OuterXml
}

$environmentNames = @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'MSBUILDDISABLENODEREUSE')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $output
try {
    $env:DOTNET_CLI_HOME = "$output/cli-home"
    $env:NUGET_PACKAGES = "$output/packages"
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $sdk = (& $DotnetPath --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -cne '10.0.401') { throw "Expected SDK 10.0.401; found $sdk" }
    if ($SimulatorQualification) {
        & $DotnetPath build $pluginProjectPath --configuration Release --nologo -m:1 -nr:false --configfile "$output/NuGet.Config" -p:UseSharedCompilation=false -p:NuGetAudit=false "-p:DirectoryBuildTargetsPath=$output/Directory.Build.targets" "-p:BaseIntermediateOutputPath=$output/plugin-obj/" "-p:MSBuildProjectExtensionsPath=$output/plugin-obj/" *> "$output/plugin-build.log"
        if ($LASTEXITCODE -ne 0) { throw "NINA.Plugin qualification build failed. See $output/plugin-build.log" }
        $pluginAssembly = "$output/plugin-bin/Release/net10.0/NINA.Plugin.dll"
        if ([Reflection.AssemblyName]::GetAssemblyName($pluginAssembly).FullName -cne $pluginPin.assemblyIdentity) { throw 'The original NINA.Plugin assembly identity changed.' }
    }
    $common = @('-p:Configuration=Release' , "-p:DirectoryBuildTargetsPath=$output/Directory.Build.targets", "-p:BaseIntermediateOutputPath=$output/obj/", "-p:MSBuildProjectExtensionsPath=$output/obj/")
    $items = & $DotnetPath msbuild $projectPath -nologo '-getItem:Compile,Page,ApplicationDefinition,Resource,Content,SplashScreen' @common
    if ($LASTEXITCODE -ne 0) { throw 'Project item evaluation failed.' }
    $evaluated = ($items -join "`n") | ConvertFrom-Json
    $expected = @{ Compile = $(if ($SimulatorQualification) { 200 } else { 196 }); Page = 103; ApplicationDefinition = 1; Resource = 10; Content = 17; SplashScreen = 1 }
    foreach ($name in $expected.Keys) { if (@($evaluated.Items.$name).Count -ne $expected[$name]) { throw "Original application item count differs: $name" } }
    Write-Utf8 "$output/evaluated-item-counts.json" ($expected | ConvertTo-Json)
    & $DotnetPath build $projectPath --configuration Release --nologo -m:1 -nr:false --configfile "$output/NuGet.Config" -p:UseSharedCompilation=false -p:NuGetAudit=false @common *> "$output/build.log"
    $buildExit = $LASTEXITCODE
    if ($buildExit -ne 0) { throw "NINA portable build failed ($buildExit). See $output/build.log" }
} finally {
    Pop-Location
    foreach ($entry in $savedEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
}

foreach ($file in $pin.files) {
    Assert-Hash (Join-Path $NinaSourceDirectory $file.path) $file.sha256
    $expected = if ($file.path -ceq 'NINA/App.xaml.cs') { $pin.consoleOutputSha256 } else { $file.sha256 }
    $adaptation = @($simulationEdits | Where-Object path -ceq $file.path)
    if ($adaptation.Count -eq 1) { $expected = $adaptation[0].outputSha256 }
    Assert-Hash (Join-Path "$output/source" $file.path) $expected
}
if ($SimulatorQualification) {
    foreach ($file in $pluginPin.files) {
        Assert-Hash (Join-Path $NinaSourceDirectory $file.path) $file.sha256
        $adaptation = @($simulationEdits | Where-Object path -ceq $file.path)
        $expectedHash = if ($adaptation.Count -eq 1) { $adaptation[0].outputSha256 } else { $file.sha256 }
        Assert-Hash (Join-Path "$output/source" $file.path) $expectedHash
    }
    foreach ($file in $simulationPin.files) { Assert-Hash (Join-Path $simulationFixture $file.path) $file.sha256 }
}
$assembly = "$output/bin/Release/net10.0/NINA.dll"
Assert-Hash (Join-Path $fixture 'PortableEntry.cs') $pin.portableEntrySha256
$identity = [Reflection.AssemblyName]::GetAssemblyName($assembly).FullName
if ($identity -cne $pin.assemblyIdentity) { throw 'The original NINA assembly identity changed.' }
$systemEventsRuntimeSupplied = $false
$systemEventsRuntimeSha256 = $null
$systemEventsOutput = "$output/bin/Release/net10.0/Microsoft.Win32.SystemEvents.dll"
if (Test-Path -LiteralPath $systemEventsOutput) {
    $systemEventsRuntime = @($family[0].files | Where-Object path -CEQ 'Microsoft.Win32.SystemEvents.dll')
    if ($systemEventsRuntime.Count -ne 1) { throw 'Undeclared SystemEvents runtime asset was copied to application output.' }
    $deliveredSystemEventsHash = (Get-FileHash -LiteralPath $systemEventsOutput).Hash
    if ($deliveredSystemEventsHash -ceq $pin.systemEventsContractSha256) { throw 'The compile-only SystemEvents contract was copied to runtime output.' }
    Assert-Hash $systemEventsOutput $systemEventsRuntime[0].sha256.ToUpperInvariant()
    $systemEventsRuntimeSha256 = $deliveredSystemEventsHash
    $systemEventsRuntimeSupplied = $true
}
$runtimeConfig = Get-Content "$output/bin/Release/net10.0/NINA.runtimeconfig.json" -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework.name -cne 'Microsoft.NETCore.App' -or $runtimeConfig.runtimeOptions.framework.version -cne '10.0.12') { throw 'Unexpected application runtime framework.' }
$originalKeys = @(Read-ResourceKeys "$closureDirectory/managed/NINA.dll" | Sort-Object)
$keys = @(Read-ResourceKeys $assembly | Sort-Object)
if ($keys.Count -ne 115 -or @($keys | Where-Object { $_ -like '*.baml' }).Count -ne 104 -or [string]::Join('|', $keys) -cne [string]::Join('|', $originalKeys)) { throw 'The original NINA embedded resource-key closure changed.' }
$result = [ordered]@{
    status = 'source-build-pass'; sourceRevision = $pin.revision; sourceFilesVerified = 680; adaptedSource = 'NINA/App.xaml.cs'; adaptedSourceSha256 = $pin.consoleOutputSha256
    startupObject = 'NINA.PortableEntry'; portableEntrySha256 = $pin.portableEntrySha256; compileItems = $(if ($SimulatorQualification) { 200 } else { 196 })
    applicationExecution = $false; assembly = $assembly; assemblySha256 = (Get-FileHash $assembly).Hash; assemblyIdentity = $identity
    pdb = "$output/bin/Release/net10.0/NINA.pdb"; pdbSha256 = (Get-FileHash "$output/bin/Release/net10.0/NINA.pdb").Hash
    resourceKeys = $keys; bamlCount = 104; appMergedDictionaries = 34; sdk = $sdk; runtimeFrameworkVersion = '10.0.12'
    buildLog = "$output/build.log"; buildLogSha256 = (Get-FileHash "$output/build.log").Hash
    originalManagedClosure = $OriginalManagedClosure; originalManagedClosureSha256 = (Get-FileHash $OriginalManagedClosure).Hash; originalBaseline = $originalBaseline; editorConfigSha256 = $pin.editorConfigSha256
    frameworkRuntimeManifest = [IO.Path]::GetFullPath($FrameworkRuntimeManifest); frameworkRuntimeManifestSha256 = (Get-FileHash $FrameworkRuntimeManifest).Hash; frameworkRuntimeFileCount = $ExpectedFrameworkRuntimeFileCount; frameworkRuntimeConfiguration = 'Release'; frameworkRuntimeDirectory = $FrameworkRuntimeDirectory
    markupCompiler = [IO.Path]::GetFullPath($MarkupCompiler); markupCompilerSha256 = (Get-FileHash $MarkupCompiler).Hash
    avalonDockSha256 = $avalonDockHash; avalonEditSourceAdapted = $sourceBuiltAvalonEdit; avalonEditSha256 = $avalonEditHash; componentBaseline = $componentBaseline; systemEventsCompileContractSha256 = $pin.systemEventsContractSha256; systemEventsRuntimeSupplied = $systemEventsRuntimeSupplied; systemEventsRuntimeSha256 = $systemEventsRuntimeSha256
    nativeDeploymentOmissions = @($substitutions | Where-Object kind -eq 'absent-native-deployment-item').Count
}
if ($SimulatorQualification) {
    $result.status = 'simulator-qualification-source-build-pass'
    $result.adaptedSource = @($simulationEdits | ForEach-Object path)
    $result.adaptedSourceSha256 = @($simulationEdits | Select-Object path,outputSha256)
    $result.simulatorQualification = [ordered]@{
        buildFlag = 'NINA_SIMULATOR_QUALIFICATION'; runtimeModeSwitch = $false; fixtureManifestSha256 = $pin.simulatorQualificationManifestSha256
        pluginSourceFilesVerified = $pluginPin.files.Count; pluginAssembly = $pluginAssembly; pluginAssemblySha256 = (Get-FileHash $pluginAssembly).Hash
        pluginPdb = "$output/plugin-bin/Release/net10.0/NINA.Plugin.pdb"; pluginPdbSha256 = (Get-FileHash "$output/plugin-bin/Release/net10.0/NINA.Plugin.pdb").Hash
        unchangedOriginalPEs = $(if ($sourceBuiltAvalonEdit) { 156 } else { 157 }); derivedOriginalPEs = @('NINA.dll', 'NINA.Plugin.dll', 'AvalonDock.dll') + $(if ($sourceBuiltAvalonEdit) { @('ICSharpCode.AvalonEdit.dll') } else { @() })
        officialTemplateFixture = $officialPluginPin; stagedFixture = "$output/qualification-inputs/QualificationPlugins"
        runtimePluginIntakeSource = $pluginIntakeSource; runtimePluginIntakeSourceSha256 = (Get-FileHash $pluginIntakeSource).Hash
        fullShellQualified = $false; nativeAstronomySupplied = $false; windowsValidStartupIsolationQualified = $false
    }
}
Write-Utf8 "$output/results.json" ($result | ConvertTo-Json -Depth 9)
if ($SimulatorQualification) {
    Write-Output "NINA simulator qualification source build passed; original 104 BAML entries and source-built Plugin. No full application execution. Evidence: $output/results.json"
} else {
    Write-Output "Original NINA source build passed; 104 BAML entries, one paired-console adaptation and no application execution. Evidence: $output/results.json"
}
