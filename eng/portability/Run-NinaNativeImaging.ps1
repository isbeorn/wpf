[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Build', 'Run')][string]$Action,
    [string]$FrameworkRelease,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedFrameworkReleaseSha256,
    [string]$OriginalClosure,
    [string]$OutputDirectory,
    [string]$ExecutionRelease,
    [ValidateSet('Gray16', 'Rgb48')][string]$Format,
    [string]$Workspace,
    [string]$PackageCache = "$([Environment]::GetFolderPath('UserProfile'))/.nuget/packages",
    [string]$LinuxDistribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath("$PSScriptRoot/../..").Replace('\', '/')
if (!$Workspace) { $Workspace = "$repository/artifacts/portability/current" }
$Workspace = [IO.Path]::GetFullPath($Workspace).Replace('\', '/')
$fixture = 'src/Portability/Tests/NinaPortableViews/NativeImaging'
$sources = @("$fixture/Program.cs", "$fixture/ImageControlCases.cs", "$fixture/NativeImageCases.cs", 'src/Portability/Tests/NativeWindowIntegration/X11Fixture.cs')
$scientificPins = [ordered]@{
    'Program.cs' = '84FEDE1D0CA67016E04C29EB677784BA77CD7A9A0A727131279504E072252D55'
    'ImageControlCases.cs' = 'F38751E2F4FF829560B25646FF19798D82283DD5BB84A2C7048D86CCAF206E1F'
    'NativeImageCases.cs' = '983726529CA49F77E0EE418ADCFB46639D10EC93FB8C1C5757554B4C81BD8766'
}
$utf8 = [Text.UTF8Encoding]::new($false)
function Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20) + "`n", $utf8) }
function Assert-Hash([string]$Path, [string]$Expected) {
    if (!$Expected -or (Sha $Path) -cne $Expected.ToUpperInvariant()) { throw "Pinned input differs: $Path" }
}
function Local-Path([string]$Path) {
    if ($Path -match '^/mnt/([a-z])/(.*)$') { return $Matches[1].ToUpperInvariant() + ':/' + $Matches[2] }
    [IO.Path]::GetFullPath($Path).Replace('\', '/')
}
function Linux-Path([string]$Path) {
    $path = Local-Path $Path
    if ($path -notmatch '^([A-Za-z]):/(.*)$') { throw "Expected a local drive path: $Path" }
    '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2]
}
function Relative-Path([string]$Path) {
    $path = Local-Path $Path
    if (!$path.StartsWith($repository + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Proof files must stay inside this repository.' }
    $path.Substring($repository.Length + 1)
}
function Assert-Relative([string]$Path) {
    if (!$Path -or [IO.Path]::IsPathRooted($Path) -or $Path -match '[:\\]' -or @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) { throw "Unsafe relative path: $Path" }
}
function Assert-Space {
    foreach ($volume in @($Workspace, $OutputDirectory, $PackageCache, [IO.Path]::GetTempPath()) | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($_)) } | Sort-Object -Unique) {
        $drive = [IO.DriveInfo]::new($volume)
        if (!$drive.IsReady -or $drive.AvailableFreeSpace -lt 2GB) { throw "Insufficient free space on $volume" }
    }
}
function Assert-ProofLocation([string]$Path, $Pin) {
    $null = Relative-Path $Path
    foreach ($inputPath in @($Pin.stageDirectory, $Pin.originalFixtureDirectory, "$repository/src", "$repository/eng", "$Workspace/build", "$Workspace/prereqs", $PackageCache)) {
        $inputRoot = (Local-Path $inputPath).TrimEnd('/')
        if ($Path -eq $inputRoot -or $Path.StartsWith($inputRoot + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Proof output must stay outside source, runtime inputs, caches and the build graph.' }
    }
}
function Assert-Inputs($Pin) {
    if ($Pin.status -cne 'root-released-owned-host-imaging' -or $Pin.overlayFiles -ne 0) { throw 'One released family without overlays is required.' }
    Assert-Hash $Pin.rootReleasePath $Pin.rootReleaseSha256
    Assert-Hash $Pin.stageManifestPath $Pin.stageManifestSha256
    Assert-Hash $Pin.familyProofPath $Pin.familyProofSha256
    $records = @(Get-Content -LiteralPath $Pin.stageManifestPath -Raw | ConvertFrom-Json)
    if ($records.Count -ne 1 -or $records[0].os -cne 'Unix' -or $records[0].configuration -cne 'Release') { throw 'One released Unix Release Stage is required.' }
    $stage = Local-Path $Pin.stageDirectory
    if ((Get-Item -LiteralPath $stage).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Stage root is a link.' }
    if ((Local-Path $records[0].published) -cne $stage) { throw 'Released Stage directory differs.' }
    if (($records[0].files | ConvertTo-Json -Depth 5 -Compress) -cne ($Pin.stageFiles | ConvertTo-Json -Depth 5 -Compress)) { throw 'Complete Stage inventory differs.' }
    $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $Pin.stageFiles) {
        Assert-Relative $file.path
        if (!$listed.Add($file.path) -or [IO.Path]::GetFileName($file.path) -match '^(Avalonia|MicroCom)|NoDependencies') { throw 'Duplicate or forbidden Stage file.' }
        $path = Join-Path $stage $file.path
        Assert-Hash $path $file.sha256
        if ((Get-Item -LiteralPath $path).Length -ne $file.bytes) { throw "Stage length differs: $path" }
    }
    $actual = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force)
    if ($actual.Count -ne $listed.Count) { throw 'Stage has missing or extra files.' }
    foreach ($item in Get-ChildItem -LiteralPath $stage -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Stage contains a link.' }
        if (!$item.PSIsContainer -and !$listed.Contains([IO.Path]::GetRelativePath($stage, $item.FullName).Replace('\', '/'))) { throw 'Stage inventory differs.' }
    }
    $family = Get-Content -LiteralPath $Pin.familyProofPath -Raw | ConvertFrom-Json
    if ($family.inputCommit -cne $Pin.baseline -or $family.os -cne 'linux' -or $family.configuration -cne 'Release' -or $family.sourceInventorySha256 -cne $Pin.stageSourceInventorySha256) { throw 'Released family provenance differs.' }
    $closures = @($family.closures | Where-Object name -CEQ 'stage')
    if ($closures.Count -ne 1) { throw 'One captured Stage closure is required.' }
    $captured = $closures[0]
    $entry = @($Pin.stageFiles | Where-Object path -CEQ ([IO.Path]::GetFileName((Local-Path $captured.fixture.path))))
    if ($entry.Count -ne 1 -or $entry[0].sha256 -cne $captured.fixture.sha256) { throw 'Stage fixture proof differs.' }
    foreach ($copy in $captured.frameworkCopies) {
        Assert-Hash "$stage/$($copy.name).dll" $copy.sha256
        if ($copy.pdbSha256) { Assert-Hash "$stage/$($copy.name).pdb" $copy.pdbSha256 }
    }
    Assert-Hash "$stage/runtimes/linux-x64/native/libSkiaSharp.so" $captured.regularLinuxSkiaSha256
    $baseline = Get-Content -LiteralPath "$repository/src/Portability/Tests/NinaPortableViews/CurrentBaselinePin.json" -Raw | ConvertFrom-Json
    Assert-Hash "$($Pin.originalFixtureDirectory)/Closure.json" $baseline.closureManifestSha256
    if ($Pin.originalClosureSha256 -cne $baseline.closureManifestSha256 -or $Pin.originalCanonicalSha256 -cne $baseline.canonicalClosureSha256) { throw 'Selected original closure is not the tracked current baseline.' }
    $closure = Get-Content -LiteralPath "$($Pin.originalFixtureDirectory)/Closure.json" -Raw | ConvertFrom-Json
    if ($closure.revision -cne $baseline.revision -or !$closure.sourceRebuild -or $closure.historicalClosureReproduced -or $closure.files.Count -ne 160 -or $closure.managedFiles -ne 82 -or $closure.satellites -ne 78) { throw 'Expected the original source-rebuilt stock160 closure.' }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($file in $closure.files) {
        $relative = $file.outputPath.Replace('\', '/')
        Assert-Relative $relative
        $path = if ($relative.Contains('/')) { "$($Pin.originalFixtureDirectory)/satellites/$relative" } else { "$($Pin.originalFixtureDirectory)/managed/$relative" }
        Assert-Hash $path $file.sha256
        if ([Reflection.AssemblyName]::GetAssemblyName($path).FullName -cne $file.identity) { throw "Original assembly identity differs: $path" }
        $lines.Add($file.outputPath + '|' + $file.identity + '|' + $file.sha256)
    }
    $lines.Sort([StringComparer]::Ordinal)
    $canonical = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")))
    if ($canonical -cne $Pin.originalCanonicalSha256 -or $closure.pinnedClosureSha256 -cne $canonical) { throw 'Original closure canonical hash differs.' }
    $closure
}
function Capture-Consumer([string]$Binary, [string]$ProofRoot) {
    $stream = [IO.File]::OpenRead("$Binary/NinaNativeImaging.dll")
    $pdbStream = [IO.File]::OpenRead("$Binary/NinaNativeImaging.pdb")
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        try {
            $reader = $provider.GetMetadataReader()
            $id = [Reflection.Metadata.BlobContentId]::new($reader.DebugMetadataHeader.Id)
            $entries = @($pe.ReadDebugDirectory() | Where-Object { $_.Type.ToString() -eq 'CodeView' })
            if ($entries.Count -ne 1) { throw 'One CodeView entry required.' }
            $cv = $pe.ReadCodeViewDebugDirectoryData($entries[0])
            if ($cv.Guid -ne $id.Guid -or $entries[0].Stamp -ne $id.Stamp) { throw 'PE/PDB identity differs.' }
            $documents = @(foreach ($relative in $sources) {
                $found = @(foreach ($handle in $reader.Documents) {
                    $document = $reader.GetDocument($handle)
                    $name = $reader.GetString($document.Name).Replace('\', '/')
                    if ($name.EndsWith('/' + $relative, [StringComparison]::Ordinal)) {
                        if ($reader.GetGuid($document.HashAlgorithm).ToString() -ne '8829d00f-11b8-4213-878b-770e8597ac16') { throw 'SHA256 PDB documents required.' }
                        $hash = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash))
                        Assert-Hash "$repository/$relative" $hash
                        [ordered]@{ document = $name; relativePath = $relative; sha256 = $hash; matches = $true }
                    }
                })
                if ($found.Count -ne 1) { throw "One source document required: $relative" }
                $found[0]
            })
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $attributes = @(foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
                $attribute = $metadata.GetCustomAttribute($handle)
                if ($attribute.Constructor.Kind.ToString() -ne 'MemberReference') { continue }
                $member = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
                if ($member.Parent.Kind.ToString() -ne 'TypeReference') { continue }
                $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
                $name = $metadata.GetString($type.Name)
                if ($name -in @('DebuggableAttribute', 'AssemblyConfigurationAttribute')) { [ordered]@{ name = $name; blob = [Convert]::ToHexString($metadata.GetBlobBytes($attribute.Value)) } }
            })
            if (($attributes | Where-Object { $_.name -eq 'DebuggableAttribute' }).blob -cne '0100020000000000' -or ($attributes | Where-Object { $_.name -eq 'AssemblyConfigurationAttribute' }).blob -cne '01000752656C656173650000') { throw 'Optimized Release PE attributes required.' }
            Write-Json "$ProofRoot/pdb-source-correspondence.json" $documents
            Write-Json "$ProofRoot/consumer-pe-metadata.json" ([ordered]@{ attributes = $attributes; pdbMatched = $true; codeViewGuid = $cv.Guid.ToString() })
        } finally { $provider.Dispose(); $pe.Dispose() }
    } finally { $pdbStream.Dispose(); $stream.Dispose() }
}

foreach ($entry in $scientificPins.GetEnumerator()) { Assert-Hash "$repository/$fixture/$($entry.Key)" $entry.Value }

if ($Action -eq 'Build') {
    if (!$FrameworkRelease -or !$ExpectedFrameworkReleaseSha256 -or !$OriginalClosure -or !$OutputDirectory) { throw 'Build requires the released family path/hash, original Closure.json and a fresh proof directory.' }
    $OutputDirectory = Local-Path $OutputDirectory
    $null = Relative-Path $OutputDirectory
    if ([IO.Path]::GetFileName($OriginalClosure) -cne 'Closure.json') { throw 'Select the original Closure.json manifest.' }
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new proof directory.' }
    Assert-Space
    Assert-Hash $FrameworkRelease $ExpectedFrameworkReleaseSha256
    $release = Get-Content -LiteralPath $FrameworkRelease -Raw | ConvertFrom-Json
    $baseline = Get-Content -LiteralPath "$repository/src/Portability/Tests/NinaPortableViews/CurrentBaselinePin.json" -Raw | ConvertFrom-Json
    $manifest = @(Get-Content -LiteralPath $release.Manifest -Raw | ConvertFrom-Json)
    $pin = [ordered]@{ status = 'root-released-owned-host-imaging'; rootReleasePath = (Local-Path $FrameworkRelease); rootReleaseSha256 = $ExpectedFrameworkReleaseSha256.ToUpperInvariant(); baseline = $release.InputCommit; overlayFiles = 0; stageDirectory = (Local-Path $release.Stage); stageManifestPath = (Local-Path $release.Manifest); stageManifestSha256 = $release.ManifestSha256; familyProofPath = (Local-Path $release.FamilyProof); familyProofSha256 = $release.FamilyProofSha256; stageSourceInventorySha256 = $release.SourceInventorySha256; stageFiles = $manifest[0].files; originalFixtureDirectory = (Local-Path (Split-Path $OriginalClosure -Parent)); originalClosureSha256 = $baseline.closureManifestSha256; originalCanonicalSha256 = $baseline.canonicalClosureSha256; consumerConfiguration = 'Release'; timingAcceptance = 'reported-only-user-accepted'; packageQualification = $false }
    Assert-ProofLocation $OutputDirectory $pin
    if ($pin.stageFiles.Count -ne $release.FileCount) { throw 'Released Stage count differs.' }
    $closure = Assert-Inputs $pin
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    Copy-Item -LiteralPath $FrameworkRelease -Destination "$OutputDirectory/root-source-release.json"
    $pin.rootReleasePath = "$OutputDirectory/root-source-release.json"
    Write-Json "$OutputDirectory/consumption-pin.json" $pin
    $project = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Project'))
    $group = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('ItemGroup')); $project.Add($group)
    foreach ($file in $pin.stageFiles | Where-Object { $_.path -notmatch '/' -and $_.path.EndsWith('.dll', [StringComparison]::Ordinal) }) {
        $reference = [Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Reference')); $reference.SetAttributeValue('Include', [IO.Path]::GetFileNameWithoutExtension($file.path))
        $reference.Add([Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('HintPath'), (Linux-Path "$($pin.stageDirectory)/$($file.path)")))
        $reference.Add([Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Private'), 'true')); $group.Add($reference)
    }
    [IO.File]::WriteAllText("$OutputDirectory/SourceStageReferences.props", $project.ToString(), $utf8)
    $names = @('NinaPortableArtifacts', 'NinaFixtureDirectory', 'NinaSourceStageDirectory', 'AssemblySearchPath_UseCandidateAssemblyFiles', 'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO', 'TMPDIR', 'WSLENV')
    $saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    $label = 'nina-imaging-' + [IO.Path]::GetFileName($OutputDirectory)
    if ($label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or (Test-Path "$Workspace/reports/$label.log")) { throw 'Use a new simple proof directory name.' }
    try {
        $env:NinaPortableArtifacts = $OutputDirectory; $env:NinaFixtureDirectory = $pin.originalFixtureDirectory; $env:NinaSourceStageDirectory = $pin.stageDirectory
        $env:AssemblySearchPath_UseCandidateAssemblyFiles = 'false'
        [IO.Directory]::CreateDirectory("$OutputDirectory/tmp") | Out-Null
        $env:DOTNET_CLI_HOME = "$Workspace/cli/nina-native-imaging"; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'; $env:DOTNET_NOLOGO = '1'; $env:TMPDIR = "$OutputDirectory/tmp"
        $env:WSLENV = (@($env:WSLENV, 'NinaPortableArtifacts/p:NinaFixtureDirectory/p:NinaSourceStageDirectory/p:AssemblySearchPath_UseCandidateAssemblyFiles:DOTNET_CLI_HOME/p:TMPDIR/p:DOTNET_CLI_TELEMETRY_OPTOUT:DOTNET_GENERATE_ASPNET_CERTIFICATE:DOTNET_SKIP_FIRST_TIME_EXPERIENCE:DOTNET_NOLOGO') | Where-Object { $_ }) -join ':'
        Assert-Hash "$Workspace/prereqs/sdk-linux-x64/dotnet" '01D89E0A0191052BFEA616CD4CE624C8FAF13B05BBDDF7F64499C23E2A9D9269'
        & wsl.exe -d $LinuxDistribution --cd (Linux-Path "$repository/src/Portability") -- env "DOTNET_ROOT=$(Linux-Path "$Workspace/prereqs/sdk-linux-x64")" DOTNET_MULTILEVEL_LOOKUP=0 "$((Linux-Path $Workspace))/prereqs/sdk-linux-x64/dotnet" msbuild (Linux-Path "$repository/$fixture/NinaNativeImaging.csproj") -nologo -p:Configuration=Release '-getProperty:Configuration' '-getProperty:Optimize' '-getProperty:AssemblySearchPaths' '-getProperty:AssemblySearchPath_UseCandidateAssemblyFiles' > "$OutputDirectory/evaluated-reference-policy.json"
        if ($LASTEXITCODE -ne 0) { throw 'Consumer property evaluation failed.' }
        $policy = Get-Content -LiteralPath "$OutputDirectory/evaluated-reference-policy.json" -Raw | ConvertFrom-Json
        if ($policy.Properties.Configuration -cne 'Release' -or $policy.Properties.Optimize -cne 'true' -or $policy.Properties.AssemblySearchPath_UseCandidateAssemblyFiles -cne 'false' -or $policy.Properties.AssemblySearchPaths.Contains('{CandidateAssemblyFiles}') -or !$policy.Properties.AssemblySearchPaths.Contains('{HintPathFromItem}')) { throw 'Explicit Release and HintPath reference policy required.' }
        & ([Environment]::ProcessPath) -NoProfile -File "$PSScriptRoot/Build-OwnedPortable.ps1" -Os linux -Label $label -Project "$fixture/NinaNativeImaging.csproj" -Workspace $Workspace -PackageCache $PackageCache -LinuxDistribution $LinuxDistribution
        $buildExit = $LASTEXITCODE
    } finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
    Copy-Item -LiteralPath "$Workspace/reports/$label.log" -Destination "$OutputDirectory/build-linux.log"
    Copy-Item -LiteralPath "$Workspace/reports/$label-result.json" -Destination "$OutputDirectory/process-linux.json"
    if ($buildExit -ne 0) { throw "Consumer build failed: $OutputDirectory/build-linux.log" }
    $binary = "$Workspace/build/bin/Unix/NinaNativeImaging/Release/net10.0"
    $null = Assert-Inputs $pin
    foreach ($file in $pin.stageFiles) { Assert-Hash "$binary/$($file.path)" $file.sha256 }
    Assert-Hash "$binary/Closure.json" $pin.originalClosureSha256
    foreach ($file in $closure.files) { Assert-Hash "$binary/$($file.outputPath.Replace('\', '/'))" $file.sha256 }
    $deps = Get-Content -LiteralPath "$binary/NinaNativeImaging.deps.json" -Raw | ConvertFrom-Json
    $libraries = @($deps.libraries.PSObject.Properties.Name)
    if (@($libraries | Where-Object { $_ -match '^(Avalonia|MicroCom)' -or $_ -like '*NoDependencies*' }).Count -or $libraries -notcontains 'SkiaSharp.NativeAssets.Linux/3.119.4' -or $libraries -notcontains 'HarfBuzzSharp.NativeAssets.Linux/8.3.1.5') { throw 'Consumer dependency closure differs.' }
    $runtimeNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $nativeNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($target in $deps.targets.PSObject.Properties.Value) {
        foreach ($library in $target.PSObject.Properties.Value) {
            foreach ($name in $library.runtime.PSObject.Properties.Name) { $null = $runtimeNames.Add([IO.Path]::GetFileName($name)) }
            foreach ($entry in $library.runtimeTargets.PSObject.Properties) {
                if ($entry.Value.assetType -ceq 'native' -and $entry.Value.rid -ceq 'linux-x64') { $null = $nativeNames.Add($entry.Name) }
            }
        }
    }
    $managedNames = @($pin.stageFiles | Where-Object { $_.path -notmatch '/' -and $_.path.EndsWith('.dll', [StringComparison]::Ordinal) } | ForEach-Object path)
    $managedNames += @($closure.files | Where-Object { $_.outputPath -notmatch '[\\/]' } | ForEach-Object outputPath)
    foreach ($name in $managedNames) { if (!$runtimeNames.Contains($name)) { throw "Generated deps omits a declared managed input: $name" } }
    foreach ($name in @('runtimes/linux-x64/native/libSkiaSharp.so', 'runtimes/linux-x64/native/libHarfBuzzSharp.so')) { if (!$nativeNames.Contains($name)) { throw "Generated deps omits native asset: $name" } }
    Assert-Hash "$binary/runtimes/linux-x64/native/libSkiaSharp.so" '66C856EAF1A47A00B23204C30C6EE407987BF5086ECC0A1A6B4FD67526B0CD02'
    Assert-Hash "$binary/runtimes/linux-x64/native/libHarfBuzzSharp.so" '1D5C3AFEF13545BF34BF8F068B14E25EE619C3B6DEE235C44E260FE61CB24018'
    Capture-Consumer $binary $OutputDirectory
    $assemblyInfo = "$Workspace/build/obj/Unix/NinaNativeImaging/Release/net10.0/NinaNativeImaging.AssemblyInfo.cs"
    if (![IO.File]::ReadAllText($assemblyInfo).Contains('AssemblyConfigurationAttribute("Release")')) { throw 'Generated Release AssemblyInfo required.' }
    Write-Json "$OutputDirectory/output-verification.json" ([ordered]@{ stageFilesVerified = $pin.stageFiles.Count; originalNinaFilesVerified = $closure.files.Count; noEditedDeps = $true; generatedDepsSha256 = (Sha "$binary/NinaNativeImaging.deps.json") })
    $extra = $sources + @("$fixture/NinaNativeImaging.csproj", "$fixture/packages.lock.json", "$fixture/README.md", "$fixture/.gitattributes", 'eng/portability/Run-NinaNativeImaging.ps1', 'eng/portability/Build-OwnedPortable.ps1', 'eng/portability/OwnedPortable.Output.props', 'src/Portability/Tests/NinaPortableViews/NinaPortableViews.csproj', 'src/Portability/Tests/NinaPortableViews/CurrentBaselinePin.json', 'src/Portability/Tests/NinaPortableViews/AvalonDock/ResourceSources.json', 'src/Portability/Tests/NinaPortableViews/Directory.Build.props', 'src/Portability/Tests/NinaPortableViews/Directory.Build.targets', 'src/Portability/global.json')
    $paths = @($extra | ForEach-Object { "$repository/$_" }) + @($assemblyInfo) + @(Get-ChildItem -LiteralPath $binary -Recurse -File | ForEach-Object FullName)
    $paths += @('root-source-release.json', 'consumption-pin.json', 'SourceStageReferences.props', 'consumer-pe-metadata.json', 'pdb-source-correspondence.json', 'output-verification.json', 'evaluated-reference-policy.json') | ForEach-Object { "$OutputDirectory/$_" }
    $files = @(foreach ($path in $paths) { $item = Get-Item -LiteralPath $path; [ordered]@{ path = (Relative-Path $item.FullName); sha256 = (Sha $item.FullName); length = $item.Length } })
    $freeze = [ordered]@{ status = 'prepared-functional-continuation-Release-timing-reported-only'; consumerBuildDirectory = (Relative-Path "$Workspace/build"); consumerOutputDirectory = (Relative-Path $binary); consumerCompileConfiguration = 'Release'; consumerSourceCommit = (git -C $repository rev-parse HEAD); expectedCountPerFormat = 48; sourceInputs = 4; changedFromRecoveredInputs = 1; scientificSourceInputs = 3; scientificSourceInputsChanged = 0; rootReleaseSha256 = $pin.rootReleaseSha256; runnerCompatibilityAlias = $null; files = $files }
    Write-Json "$OutputDirectory/PreparedConsumerFreeze.json" $freeze
    Write-Output "PreparedConsumerFreeze SHA256 $(Sha "$OutputDirectory/PreparedConsumerFreeze.json"). Use the existing reviewed ExecutionRelease.json gate before Run."
    exit 0
}

if (!$ExecutionRelease -or !$Format) { throw 'Run requires an existing reviewed ExecutionRelease.json and a separate format.' }
$OutputDirectory = Local-Path (Split-Path $ExecutionRelease -Parent)
$gate = Get-Content -LiteralPath $ExecutionRelease -Raw | ConvertFrom-Json
if ($gate.status -cne 'root-reviewed-native-imaging' -or !$gate.includesReviewedGridAndPopup -or $Format -cnotin $gate.allowedFormats -or $gate.timingPolicy -cne 'reported-only-user-accepted') { throw 'The existing reviewed imaging gate does not authorize this run.' }
Assert-Hash "$OutputDirectory/PreparedConsumerFreeze.json" $gate.compiledFreezeSha256
$freeze = Get-Content -LiteralPath "$OutputDirectory/PreparedConsumerFreeze.json" -Raw | ConvertFrom-Json
if ($freeze.consumerBuildDirectory -cne $gate.consumerBuildDirectory -or $freeze.rootReleaseSha256 -cne $gate.wholeFamilyRootReleaseSha256 -or $freeze.expectedCountPerFormat -ne 48) { throw 'Reviewed consumer/family freeze differs.' }
foreach ($file in $freeze.files) {
    Assert-Relative $file.path
    Assert-Hash "$repository/$($file.path)" $file.sha256
    if ((Get-Item -LiteralPath "$repository/$($file.path)").Length -ne $file.length) { throw 'Frozen length differs.' }
}
$pin = Get-Content -LiteralPath "$OutputDirectory/consumption-pin.json" -Raw | ConvertFrom-Json
Assert-ProofLocation $OutputDirectory $pin
$null = Assert-Inputs $pin
if ($pin.rootReleaseSha256 -cne $gate.wholeFamilyRootReleaseSha256) { throw 'Reviewed root release differs.' }
Assert-Relative $freeze.consumerOutputDirectory
$binary = "$repository/$($freeze.consumerOutputDirectory)"
Assert-Space
$nativeRoot = "$Workspace/prereqs/native/root"
Assert-Hash "$Workspace/prereqs/sdk-linux-x64/dotnet" '01D89E0A0191052BFEA616CD4CE624C8FAF13B05BBDDF7F64499C23E2A9D9269'
Assert-Hash "$nativeRoot/usr/bin/xdpyinfo" '0D6934A0A06AFE4DCE863FC5F5990C4455BA8CFCB7F84359CE1C34777884A3B1'
Assert-Hash "$nativeRoot/usr/lib/x86_64-linux-gnu/libXxf86dga.so.1.0.0" 'AFA0441608B47D4DC9314FF1532E8AFFAB5AB4405B2998B5078C38493298181A'
$run = "$OutputDirectory/native-runs/$Format-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))"
[IO.Directory]::CreateDirectory($run) | Out-Null
$nativeScript = @'
set -euo pipefail
set +m
umask 077
sdk=$1; app=$2; format=$3; report=$4; native_root=$5
[[ $(df -PB1 /tmp | awk 'NR==2 {print $4}') -ge 2147483648 ]]
work=$(mktemp -d /tmp/wpf-nina-native-imaging.XXXXXXXX)
xvfb_pid=; child_pid=; child_group=; child_start=; number=
group_members() {
 ps -eo pid=,ppid=,pgid=,sid=,stat=,comm= | awk -v group="$child_group" '$3 == group'
}
stop_child_group() {
 if [[ -n "$child_pid" && -z "$child_group" ]]; then
  for attempt in $(seq 1 100); do [[ -s "$work/child-group" ]] && break; sleep .01; done
 fi
 if [[ -z "$child_group" && -s "$work/child-group" ]]; then read -r child_group child_start < "$work/child-group"; fi
 if [[ -z "$child_group" ]]; then
  [[ -z "$child_pid" ]] && return 0
  return 1
 fi
 [[ "$child_group" =~ ^[1-9][0-9]*$ && "$child_start" =~ ^[0-9]+$ && "$child_group" == "$child_pid" ]] || return 1
 members=$(group_members) || return 1
 printf '%s\n' "$members" > "$report/process-group-before-cleanup.txt"
 if [[ -z "$members" ]]; then
  : > "$report/process-group-after-cleanup.txt"
  if kill -0 -- "-$child_group" 2>/dev/null; then return 1; fi
  return 0
 fi
 [[ -z $(awk -v group="$child_group" '$4 != group' <<< "$members") ]] || return 1
 if [[ -e "/proc/$child_group/stat" && $(awk '{print $22}' "/proc/$child_group/stat") != "$child_start" ]]; then return 1; fi
 kill -TERM -- "-$child_group" 2>/dev/null || true
 for attempt in $(seq 1 50); do
  members=$(group_members) || return 1
  [[ -n "$members" ]] || break
  sleep .1
 done
 if [[ -n "$members" ]]; then kill -KILL -- "-$child_group" 2>/dev/null || true; fi
 for attempt in $(seq 1 50); do
  members=$(group_members) || return 1
  [[ -n "$members" ]] || break
  sleep .1
 done
 printf '%s\n' "$members" > "$report/process-group-after-cleanup.txt"
 [[ -z "$members" ]] && ! kill -0 -- "-$child_group" 2>/dev/null
}
finish() {
 code=$?; trap - EXIT
 trap '' INT TERM
 stopped=true; socket=true; lock=true; absent=false
 stop_child_group || stopped=false
 if [[ -n "$child_pid" && "$stopped" == true ]]; then wait "$child_pid" 2>/dev/null || true; fi
 if [[ -n "$xvfb_pid" ]]; then
  kill -TERM "$xvfb_pid" 2>/dev/null || true
  for attempt in $(seq 1 50); do kill -0 "$xvfb_pid" 2>/dev/null || break; sleep .1; done
  if kill -0 "$xvfb_pid" 2>/dev/null; then kill -KILL "$xvfb_pid" 2>/dev/null || true; fi
  wait "$xvfb_pid" 2>/dev/null || true
  ! kill -0 "$xvfb_pid" 2>/dev/null || stopped=false
 fi
 [[ -z "$number" || ! -e /tmp/.X11-unix/X$number ]] || socket=false
 [[ -z "$number" || ! -e /tmp/.X$number-lock ]] || lock=false
 if [[ -n "$number" ]] && ss -xlH | grep -Eq "(@|[[:space:]])/tmp/\.X11-unix/X${number}([[:space:]]|$)"; then socket=false; fi
 preserved=true
 if [[ -d "$work/state" ]] && ! cp -a "$work/state" "$report/state"; then preserved=false; code=1; fi
 if [[ "$preserved" == true && "$stopped" == true && "$socket" == true && "$lock" == true && ! -L "$work" && $(readlink -f -- "$work") == "$work" && "$work" == /tmp/wpf-nina-native-imaging.* ]]; then rm -rf -- "$work"; fi
 [[ ! -e "$work" ]] && absent=true
 printf '{"fixtureExit":%s,"display":":%s","work":"%s","xvfbPid":"%s","childPid":"%s","childProcessGroup":"%s","childStartTicks":"%s","ownedProcessesStopped":%s,"socketAbsent":%s,"lockAbsent":%s,"workAbsent":%s,"reportsPreserved":%s,"mountNamespace":"%s"}\n' "$code" "$number" "$work" "$xvfb_pid" "$child_pid" "$child_group" "$child_start" "$stopped" "$socket" "$lock" "$absent" "$preserved" "$(readlink /proc/self/ns/mnt)" > "$report/owned-cleanup.json"
 [[ "$absent" == true ]] || code=1
 exit "$code"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$work/cli" "$work/tmp" "$work/cache" "$work/config" "$work/data" "$work/runtime"
export DOTNET_ROOT="${sdk%/dotnet}" DOTNET_MULTILEVEL_LOOKUP=0 DOTNET_CLI_HOME="$work/cli"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1
export TMPDIR="$work/tmp" XDG_CACHE_HOME="$work/cache" XDG_CONFIG_HOME="$work/config" XDG_DATA_HOME="$work/data" XDG_RUNTIME_DIR="$work/runtime"
export LD_LIBRARY_PATH="$native_root/usr/lib/x86_64-linux-gnu"
unset DOTNET_STARTUP_HOOKS WAYLAND_DISPLAY WPF_ATSPI_PRIVATE_BUS WPF_ATSPI_OBSERVATIONS WPF_FILE_DIALOG_PRIVATE_BUS DBUS_STARTER_ADDRESS DBUS_STARTER_BUS_TYPE AT_SPI_BUS_ADDRESS
export DBUS_SESSION_BUS_ADDRESS="unix:path=$work/absent-bus" XAUTHORITY="$work/absent-authority"
[[ $("$sdk" --version) == 10.0.401 ]]
ulimit -c 0
for candidate in $(seq 1101 1120); do
 [[ ! -e /tmp/.X$candidate-lock && ! -e /tmp/.X11-unix/X$candidate ]] || continue
 if ss -xlH | grep -Eq "(@|[[:space:]])/tmp/\.X11-unix/X${candidate}([[:space:]]|$)"; then continue; fi
 number=$candidate; break
done
[[ -n "$number" ]]
Xvfb ":$number" -pn -screen 0 1311x877x24 -nolisten tcp -ac > "$report/xvfb.log" 2>&1 & xvfb_pid=$!
export DISPLAY=":$number"
ready=0
for attempt in $(seq 1 100); do
 kill -0 "$xvfb_pid"
 if timeout 1 "$native_root/usr/bin/xdpyinfo" -display "$DISPLAY" > "$report/display-readiness.txt" 2> "$report/display-readiness.stderr.log"; then ready=1; break; fi
 sleep .05
done
[[ "$ready" == 1 ]]
cat > "$work/private-bus.conf" <<'BUS'
<busconfig>
  <type>session</type>
  <listen>unix:tmpdir=/tmp</listen>
  <auth>EXTERNAL</auth>
  <policy context="default">
    <allow user="*" />
    <allow own="*" />
    <allow send_destination="*" />
    <allow receive_sender="*" />
  </policy>
</busconfig>
BUS
export NINA_PORTABLE_VIEWS_STOCK=0 NINA_PORTABLE_VIEWS_STATE="$work/state/$format"
export NINA_NATIVE_IMAGE_FORMAT="$format" NINA_NATIVE_IMAGING_EXECUTION_AUTHORIZED=reviewed-whole-grid-popup-stage
export NINA_IMAGE_VM_RESULT_PATH="$report/runtime-linux.json"
cd "$app"
setsid bash -ec 'read -r -a process_stat < /proc/$$/stat; printf "%s %s\n" "$$" "${process_stat[21]}" > "$1.tmp"; mv -- "$1.tmp" "$1"; shift; exec "$@"' bash "$work/child-group" timeout -k 5s 110s dbus-run-session --config-file="$work/private-bus.conf" -- "$sdk" "$app/NinaNativeImaging.dll" > "$report/runtime-linux.stdout.log" 2> "$report/runtime-linux.stderr.log" & child_pid=$!
for attempt in $(seq 1 100); do [[ -s "$work/child-group" ]] && break; sleep .01; done
read -r child_group child_start < "$work/child-group"
[[ "$child_group" == "$child_pid" ]]
set +e
wait "$child_pid"
result=$?
set -e
exit "$result"
'@
($nativeScript.Replace("`r`n", "`n") + "`n") | & wsl.exe -d $LinuxDistribution --cd (Linux-Path "$repository/src/Portability") --exec bash -s -- (Linux-Path "$Workspace/prereqs/sdk-linux-x64/dotnet") (Linux-Path $binary) $Format (Linux-Path $run) (Linux-Path $nativeRoot) > "$run/launcher.stdout.log" 2> "$run/launcher.stderr.log"
$nativeExit = $LASTEXITCODE
Write-Json "$run/process-linux.json" ([ordered]@{ runnerExitCode = $nativeExit; format = $Format; privateXvfbOnly = $true; sourceDiagnostic = $true; packageQualification = $false })
if ($nativeExit -ne 0) { throw "Native process failed; preserve first result: $run" }
$result = Get-Content -LiteralPath "$run/runtime-linux.json" -Raw | ConvertFrom-Json
if ($result.failed -or @($result.nativeFailures).Count -or @($result.unreachedOrFailedChecks).Count -or $result.checks.Count -ne 48 -or @($result.checks | Where-Object status -CNE 'pass').Count -or (($result.checks.name | ConvertTo-Json -Compress) -cne ($result.expectedChecks | ConvertTo-Json -Compress))) { throw "Functional imaging assertion failed: $run" }
Write-Output "$Format passed 48/48. Timing is reported only under the existing user acceptance. Evidence: $run"
