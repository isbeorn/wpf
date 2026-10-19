[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeDirectory,
    [Parameter(Mandatory)][string] $FrameworkRuntimeManifest,
    [Parameter(Mandatory)][string] $MarkupCompiler,
    [Parameter(Mandatory)][string] $WinFxTargets,
    [Parameter(Mandatory)][string] $PortableReferencesTargets,
    [Parameter(Mandatory)][string] $OriginalManagedClosure,
    [Parameter(Mandatory)][string] $AvalonDockPath,
    [Parameter(Mandatory)][string] $SystemEventsContractPath,
    [Parameter(Mandatory)][string] $MvvmGeneratorPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Portability/Tests/NinaPortableApplication'))
$pin = Get-Content (Join-Path $fixture 'upstream.json') -Raw | ConvertFrom-Json
$utf8 = [Text.UTF8Encoding]::new($false)

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
$runtimeManifest = Get-Content -LiteralPath $FrameworkRuntimeManifest -Raw | ConvertFrom-Json
$runtime = @($runtimeManifest | Where-Object os -eq 'Unix')
if ($runtime.Count -ne 1 -or $runtime[0].files.Count -ne 143) { throw 'Expected one whole143 Unix framework family.' }
foreach ($file in $runtime[0].files) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:') { throw 'Unexpected framework manifest path.' }
    Assert-Hash (Join-Path $FrameworkRuntimeDirectory $file.path) $file.sha256
}
if (@(Get-ChildItem -LiteralPath $FrameworkRuntimeDirectory -Recurse -File).Count -ne 143) { throw 'FrameworkRuntimeDirectory contains extra files.' }
$closure = Get-Content -LiteralPath $OriginalManagedClosure -Raw | ConvertFrom-Json
Assert-Hash $OriginalManagedClosure $pin.originalClosureManifestSha256
if ($closure.revision -cne $pin.revision -or $closure.files.Count -ne 160) { throw 'Expected the original pinned 160-PE closure.' }
$closureDirectory = Split-Path $OriginalManagedClosure -Parent
foreach ($file in $closure.files) {
    if ($file.outputPath -match '(^|/)\.\.(/|$)|\\|:') { throw 'Unexpected original closure path.' }
    $group = if ($file.outputPath.Contains('/')) { 'satellites' } else { 'managed' }
    Assert-Hash (Join-Path "$closureDirectory/$group" $file.outputPath) $file.sha256
}
Assert-Hash $AvalonDockPath $pin.avalonDockSha256
Assert-Hash $SystemEventsContractPath $pin.systemEventsContractSha256
Assert-Hash $MvvmGeneratorPath $pin.mvvmGeneratorSha256
Assert-Hash (Join-Path $fixture 'App.xaml.cs.patch') $pin.consolePatchSha256
Assert-Hash (Join-Path $fixture 'PortableEntry.cs') $pin.portableEntrySha256

foreach ($file in $pin.files) {
    $destination = Join-Path "$output/source" $file.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy((Join-Path $NinaSourceDirectory $file.path), $destination)
}
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
$references = Add-Element $project $root ItemGroup @{}
$null = Add-Element $project $references Compile @{ Include = "$fixture/PortableEntry.cs"; Link = 'Portability/PortableEntry.cs' }
foreach ($file in $closure.files | Where-Object { !$_.outputPath.Contains('/') -and $_.outputPath -notin @('NINA.dll', 'AvalonDock.dll') }) {
    $null = Add-Element $project $references Reference @{ Include = [IO.Path]::GetFileNameWithoutExtension($file.outputPath); HintPath = "$closureDirectory/managed/$($file.outputPath)"; Private = 'true' }
}
$null = Add-Element $project $references Reference @{ Include = 'AvalonDock'; HintPath = [IO.Path]::GetFullPath($AvalonDockPath); Private = 'true' }
$null = Add-Element $project $references Reference @{ Include = "$FrameworkRuntimeDirectory/*.dll"; Private = 'true' }
$null = Add-Element $project $references Reference @{ Include = 'Microsoft.Win32.SystemEvents'; HintPath = [IO.Path]::GetFullPath($SystemEventsContractPath); Private = 'false' }
$null = Add-Element $project $references Analyzer @{ Include = [IO.Path]::GetFullPath($MvvmGeneratorPath) }
$null = Add-Element $project $references None @{ Include = "$fixture/upstream.json"; Link = 'ThirdPartyNotices/NINA/upstream.json'; CopyToOutputDirectory = 'PreserveNewest' }
$null = Add-Element $project $references None @{ Include = "$output/source/NINA/LICENSE.txt"; Link = 'ThirdPartyNotices/NINA/LICENSE.txt'; CopyToOutputDirectory = 'PreserveNewest' }
$null = Add-Element $project $root Import @{ Project = 'Sdk.targets'; Sdk = 'Microsoft.NET.Sdk' }
$null = Add-Element $project $root Import @{ Project = [IO.Path]::GetFullPath($WinFxTargets) }
$null = Add-Element $project $root Import @{ Project = [IO.Path]::GetFullPath($PortableReferencesTargets) }
$projectPath = "$output/source/NINA/NINA.Portable.csproj"
Write-Utf8 $projectPath $project.OuterXml
Write-Utf8 "$output/global.json" '{"sdk":{"version":"10.0.401","rollForward":"disable","allowPrerelease":false}}'
Write-Utf8 "$output/Directory.Build.targets" '<Project/>'
Write-Utf8 "$output/NuGet.Config" '<configuration><packageSources><clear/></packageSources></configuration>'
Write-Utf8 "$output/build-substitutions.json" ($substitutions | ConvertTo-Json -Depth 5)

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
    $common = @('-p:Configuration=Release', "-p:DirectoryBuildTargetsPath=$output/Directory.Build.targets", "-p:BaseIntermediateOutputPath=$output/obj/", "-p:MSBuildProjectExtensionsPath=$output/obj/")
    $items = & $DotnetPath msbuild $projectPath -nologo '-getItem:Compile,Page,ApplicationDefinition,Resource,Content,SplashScreen' @common
    if ($LASTEXITCODE -ne 0) { throw 'Project item evaluation failed.' }
    $evaluated = ($items -join "`n") | ConvertFrom-Json
    $expected = @{ Compile = 196; Page = 103; ApplicationDefinition = 1; Resource = 10; Content = 17; SplashScreen = 1 }
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
    Assert-Hash (Join-Path "$output/source" $file.path) $expected
}
$assembly = "$output/bin/Release/net10.0/NINA.dll"
Assert-Hash (Join-Path $fixture 'PortableEntry.cs') $pin.portableEntrySha256
$identity = [Reflection.AssemblyName]::GetAssemblyName($assembly).FullName
if ($identity -cne $pin.assemblyIdentity) { throw 'The original NINA assembly identity changed.' }
if (Test-Path "$output/bin/Release/net10.0/Microsoft.Win32.SystemEvents.dll") { throw 'The compile-only SystemEvents contract was copied to runtime output.' }
$runtimeConfig = Get-Content "$output/bin/Release/net10.0/NINA.runtimeconfig.json" -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework.name -cne 'Microsoft.NETCore.App' -or $runtimeConfig.runtimeOptions.framework.version -cne '10.0.12') { throw 'Unexpected application runtime framework.' }
$originalKeys = @(Read-ResourceKeys "$closureDirectory/managed/NINA.dll" | Sort-Object)
$keys = @(Read-ResourceKeys $assembly | Sort-Object)
if ($keys.Count -ne 115 -or @($keys | Where-Object { $_ -like '*.baml' }).Count -ne 104 -or [string]::Join('|', $keys) -cne [string]::Join('|', $originalKeys)) { throw 'The original NINA embedded resource-key closure changed.' }
$result = [ordered]@{
    status = 'source-build-pass'; sourceRevision = $pin.revision; sourceFilesVerified = 680; adaptedSource = 'NINA/App.xaml.cs'; adaptedSourceSha256 = $pin.consoleOutputSha256
    startupObject = 'NINA.PortableEntry'; portableEntrySha256 = $pin.portableEntrySha256; compileItems = 196
    applicationExecution = $false; assembly = $assembly; assemblySha256 = (Get-FileHash $assembly).Hash; assemblyIdentity = $identity
    pdb = "$output/bin/Release/net10.0/NINA.pdb"; pdbSha256 = (Get-FileHash "$output/bin/Release/net10.0/NINA.pdb").Hash
    resourceKeys = $keys; bamlCount = 104; appMergedDictionaries = 34; sdk = $sdk; runtimeFrameworkVersion = '10.0.12'
    buildLog = "$output/build.log"; buildLogSha256 = (Get-FileHash "$output/build.log").Hash
    originalManagedClosure = $OriginalManagedClosure; originalManagedClosureSha256 = (Get-FileHash $OriginalManagedClosure).Hash
    frameworkRuntimeManifest = [IO.Path]::GetFullPath($FrameworkRuntimeManifest); frameworkRuntimeManifestSha256 = (Get-FileHash $FrameworkRuntimeManifest).Hash; frameworkRuntimeDirectory = $FrameworkRuntimeDirectory
    markupCompiler = [IO.Path]::GetFullPath($MarkupCompiler); markupCompilerSha256 = (Get-FileHash $MarkupCompiler).Hash
    avalonDockSha256 = $pin.avalonDockSha256; systemEventsCompileContractSha256 = $pin.systemEventsContractSha256; systemEventsRuntimeSupplied = $false
    nativeDeploymentOmissions = @($substitutions | Where-Object kind -eq 'absent-native-deployment-item').Count
}
Write-Utf8 "$output/results.json" ($result | ConvertTo-Json -Depth 6)
Write-Output "Original NINA source build passed; 104 BAML entries, one paired-console adaptation and no application execution. Evidence: $output/results.json"
