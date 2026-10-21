[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/portability/nina-control-dependencies'),
    [string] $NinaEvidenceDirectory = (Join-Path $PSScriptRoot '../../artifacts/portability/nina/4742afae0a09c79be772f23fcf163fb5852d2775'),
    [string] $DotnetPath = (Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'),
    [switch] $SkipRestore
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
Set-StrictMode -Version Latest
$revision = '4742afae0a09c79be772f23fcf163fb5852d2775'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$projectDirectory = Join-Path $output 'restore'
$packageDirectory = Join-Path $output 'packages'
New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null

function Write-Json($Value, [string] $Path) {
    $Value | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-Token([byte[]] $PublicKey, [bool] $IsFullKey) {
    if (!$PublicKey.Length) { return 'null' }
    if ($IsFullKey) {
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $digest = $sha.ComputeHash($PublicKey) } finally { $sha.Dispose() }
        $PublicKey = $digest[($digest.Length - 1)..($digest.Length - 8)]
    }
    return ([BitConverter]::ToString($PublicKey)).Replace('-', '').ToLowerInvariant()
}

function Get-TypeIdentity($Metadata, [Reflection.Metadata.EntityHandle] $Handle, [string] $OwnAssembly) {
    switch ($Handle.Kind.ToString()) {
        'TypeReference' {
            $type = $Metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle] $Handle)
            $name = $Metadata.GetString($type.Name)
            $namespace = $Metadata.GetString($type.Namespace)
            $assembly = $OwnAssembly
            if ($type.ResolutionScope.Kind -eq [Reflection.Metadata.HandleKind]::AssemblyReference) {
                $assembly = $Metadata.GetString($Metadata.GetAssemblyReference([Reflection.Metadata.AssemblyReferenceHandle] $type.ResolutionScope).Name)
            } elseif ($type.ResolutionScope.Kind -eq [Reflection.Metadata.HandleKind]::TypeReference) {
                $parent = Get-TypeIdentity $Metadata $type.ResolutionScope $OwnAssembly
                $name = "$($parent.Type)+$name"
                $assembly = $parent.Assembly
            }
            if ($namespace) { $name = "$namespace.$name" }
            return [pscustomobject]@{ Assembly = $assembly; Type = $name }
        }
        'TypeDefinition' {
            $type = $Metadata.GetTypeDefinition([Reflection.Metadata.TypeDefinitionHandle] $Handle)
            $name = $Metadata.GetString($type.Name)
            $namespace = $Metadata.GetString($type.Namespace)
            if (!$type.GetDeclaringType().IsNil) {
                $parent = Get-TypeIdentity $Metadata $type.GetDeclaringType() $OwnAssembly
                $name = "$($parent.Type)+$name"
            } elseif ($namespace) { $name = "$namespace.$name" }
            return [pscustomobject]@{ Assembly = $OwnAssembly; Type = $name }
        }
        default { return [pscustomobject]@{ Assembly = ''; Type = "<$($Handle.Kind)>" } }
    }
}

function Get-MethodIdentity($Metadata, [Reflection.Metadata.EntityHandle] $Handle, [string] $OwnAssembly) {
    switch ($Handle.Kind.ToString()) {
        'MethodDefinition' {
            $method = $Metadata.GetMethodDefinition([Reflection.Metadata.MethodDefinitionHandle] $Handle)
            $owner = Get-TypeIdentity $Metadata $method.GetDeclaringType() $OwnAssembly
            return [pscustomobject]@{ Assembly = $owner.Assembly; Type = $owner.Type; Member = $Metadata.GetString($method.Name) }
        }
        'MemberReference' {
            $member = $Metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle] $Handle)
            $owner = Get-TypeIdentity $Metadata $member.Parent $OwnAssembly
            return [pscustomobject]@{ Assembly = $owner.Assembly; Type = $owner.Type; Member = $Metadata.GetString($member.Name) }
        }
        'MethodSpecification' {
            return Get-MethodIdentity $Metadata $Metadata.GetMethodSpecification([Reflection.Metadata.MethodSpecificationHandle] $Handle).Method $OwnAssembly
        }
        default { throw "Unexpected IL method handle: $($Handle.Kind)" }
    }
}

$opcodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $code = $field.GetValue($null)
    $opcodes[[int] $code.Value -band 0xffff] = $code
}

function Read-Assembly([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (!$pe.HasMetadata) { throw "Selected DLL has no managed metadata: $Path" }
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $definition = $metadata.GetAssemblyDefinition()
        $name = $metadata.GetString($definition.Name)
        $references = @(
            foreach ($handle in $metadata.AssemblyReferences) {
                $reference = $metadata.GetAssemblyReference($handle)
                [pscustomobject]@{
                    Name = $metadata.GetString($reference.Name); Version = $reference.Version.ToString()
                    PublicKeyToken = Get-Token $metadata.GetBlobBytes($reference.PublicKeyOrToken) (($reference.Flags -band [Reflection.AssemblyFlags]::PublicKey) -ne 0)
                }
            }
        )
        $imports = [Collections.Generic.List[object]]::new()
        $importTokens = @{}
        $additionalDesktopTypes = @(
            foreach ($handle in $metadata.TypeReferences) {
                $identity = Get-TypeIdentity $metadata $handle $name
                if ($identity.Assembly -in 'UIAutomationProvider', 'UIAutomationTypes', 'ReachFramework') { $identity }
            }
        )
        $comTypes = @(
            foreach ($handle in $metadata.TypeDefinitions) {
                $type = $metadata.GetTypeDefinition($handle)
                if (($type.Attributes -band [Reflection.TypeAttributes]::Import) -ne 0) {
                    (Get-TypeIdentity $metadata $handle $name).Type
                }
            }
        )
        foreach ($handle in $metadata.MethodDefinitions) {
            $method = $metadata.GetMethodDefinition($handle)
            if (($method.Attributes -band [Reflection.MethodAttributes]::PinvokeImpl) -ne 0) {
                $import = $method.GetImport()
                $identity = Get-MethodIdentity $metadata $handle $name
                $token = [Reflection.Metadata.Ecma335.MetadataTokens]::GetToken([Reflection.Metadata.EntityHandle] $handle)
                $importTokens[$token] = $true
                $imports.Add([pscustomobject]@{
                    Type = $identity.Type; Method = $identity.Member
                    Module = $metadata.GetString($metadata.GetModuleReference($import.Module).Name)
                    EntryPoint = $metadata.GetString($import.Name); Attributes = $import.Attributes.ToString(); Token = $token
                })
            }
        }
        $resources = @(
            foreach ($handle in $metadata.ManifestResources) {
                $resource = $metadata.GetManifestResource($handle)
                $resourceName = $metadata.GetString($resource.Name)
                $keys = [Collections.Generic.List[string]]::new()
                if ($resource.Implementation.IsNil -and $resourceName.EndsWith('.resources')) {
                    [byte[]] $section = $pe.GetSectionData($pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).GetContent()
                    $length = [BitConverter]::ToInt32($section, [int] $resource.Offset)
                    $resourceStream = [IO.MemoryStream]::new($section, [int] $resource.Offset + 4, $length, $false)
                    $reader = [Resources.ResourceReader]::new($resourceStream)
                    try {
                        $iterator = $reader.GetEnumerator()
                        while ($iterator.MoveNext()) { $keys.Add([string] $iterator.Key) }
                    } finally { $reader.Dispose(); $resourceStream.Dispose() }
                }
                [pscustomobject]@{ Name = $resourceName; Embedded = $resource.Implementation.IsNil; Keys = @($keys.ToArray() | Sort-Object) }
            }
        )
        $calls = [Collections.Generic.List[object]]::new()
        $strings = [Collections.Generic.List[object]]::new()
        $methodBodies = 0
        foreach ($handle in $metadata.MethodDefinitions) {
            $method = $metadata.GetMethodDefinition($handle)
            if (!$method.RelativeVirtualAddress) { continue }
            $methodBodies++
            $caller = Get-MethodIdentity $metadata $handle $name
            $callerName = "$($caller.Type).$($caller.Member)"
            [byte[]] $il = [Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe, $method.RelativeVirtualAddress).GetILBytes()
            $offset = 0
            while ($offset -lt $il.Length) {
                $instructionOffset = $offset
                [int] $codeValue = $il[$offset++]
                if ($codeValue -eq 0xfe) { $codeValue = 0xfe00 -bor $il[$offset++] }
                if (!$opcodes.ContainsKey($codeValue)) { throw "Unknown IL opcode in ${Path}: $codeValue" }
                $code = $opcodes[$codeValue]
                $operand = $code.OperandType.ToString()
                if ($operand -eq 'InlineMethod') {
                    $token = [BitConverter]::ToInt32($il, $offset)
                    $target = Get-MethodIdentity $metadata ([Reflection.Metadata.Ecma335.MetadataTokens]::EntityHandle($token)) $name
                    $targetName = "$($target.Type).$($target.Member)"
                    if ($importTokens.ContainsKey($token) -or $target.Assembly -in 'UIAutomationProvider', 'UIAutomationTypes', 'ReachFramework' -or $targetName -match 'NativeMethods|Win32|Hwnd|WindowInterop|WindowChrome|Marshal\.|D3DImage|ShaderEffect|PixelShader|Clipboard|PrintDialog|OpenFileDialog|SaveFileDialog|DragDrop|LoadComponent|InitializeComponent|EnsureHandle|Dispatcher|SystemParameters|SetResourceReference') {
                        $calls.Add([pscustomobject]@{ Caller = $callerName; Offset = $instructionOffset; Op = $code.Name; TargetAssembly = $target.Assembly; Target = $targetName; DirectPInvoke = $importTokens.ContainsKey($token) })
                    }
                } elseif ($operand -eq 'InlineString') {
                    $token = [BitConverter]::ToInt32($il, $offset)
                    $value = $metadata.GetUserString([Reflection.Metadata.Ecma335.MetadataTokens]::UserStringHandle($token -band 0xffffff))
                    if ($value -match 'pack://|;component|\.baml|\.dll$|\.ps$') {
                        $strings.Add([pscustomobject]@{ Caller = $callerName; Offset = $instructionOffset; Value = $value })
                    }
                }
                switch ($operand) {
                    'InlineNone' { }
                    { $_ -in 'ShortInlineBrTarget', 'ShortInlineI', 'ShortInlineVar' } { $offset += 1 }
                    'InlineVar' { $offset += 2 }
                    { $_ -in 'InlineI8', 'InlineR' } { $offset += 8 }
                    'InlineSwitch' { $offset += 4 + (4 * [BitConverter]::ToInt32($il, $offset)) }
                    { $_ -in 'InlineBrTarget', 'InlineField', 'InlineI', 'InlineMethod', 'InlineSig', 'InlineString', 'InlineTok', 'InlineType', 'ShortInlineR' } { $offset += 4 }
                    default { throw "Unknown IL operand: $operand" }
                }
                if ($offset -gt $il.Length) { throw "IL operand overruns method: $callerName" }
            }
        }
        return [pscustomobject]@{
            Name = $name; Version = $definition.Version.ToString(); PublicKeyToken = Get-Token $metadata.GetBlobBytes($definition.PublicKey) $true
            Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
            References = $references; AdditionalDesktopTypes = $additionalDesktopTypes; PInvokes = $imports.ToArray(); ComImportTypes = $comTypes
            Resources = $resources; MethodBodiesScanned = $methodBodies; CriticalCalls = $calls.ToArray(); ResourceStrings = $strings.ToArray()
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

function Read-StartupResources($NinaInventory) {
    $treePath = Join-Path $NinaEvidenceDirectory 'metadata/tree.json'
    if ((Get-FileHash -LiteralPath $treePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $NinaInventory.TreeResponseSha256) { throw 'Pinned NINA tree response hash mismatch.' }
    $tree = Get-Content -LiteralPath $treePath -Raw | ConvertFrom-Json
    if ($tree.sha -cne $NinaInventory.RootTreeId -or $tree.truncated -ne $false) { throw 'Pinned NINA tree identity or completeness mismatch.' }
    $blobs = @{}
    foreach ($entry in $tree.tree) { if ($entry.type -eq 'blob') { $blobs[$entry.path] = $entry } }
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue('NINA/App.xaml')
    $seen = @{}
    $files = [Collections.Generic.List[object]]::new()
    $edges = [Collections.Generic.List[object]]::new()
    while ($pending.Count) {
        $path = $pending.Dequeue()
        if ($seen.ContainsKey($path)) { continue }
        $seen[$path] = $true
        if ($seen.Count -gt 100) { throw 'Startup resource closure exceeds 100-file bound.' }
        if (!$blobs.ContainsKey($path) -or $blobs[$path].size -gt 1048576) { throw "Missing or oversized startup dictionary: $path" }
        $destination = Join-Path $output "nina-startup-source/$path"
        $url = "https://raw.githubusercontent.com/isbeorn/nina/$revision/$path"
        if (!(Test-Path -LiteralPath $destination)) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Invoke-WebRequest -Uri $url -OutFile $destination -TimeoutSec 45
        }
        [byte[]] $bytes = [IO.File]::ReadAllBytes($destination)
        $prefix = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $blobId = [Convert]::ToHexString($sha.ComputeHash($prefix + $bytes)).ToLowerInvariant() } finally { $sha.Dispose() }
        if ($blobId -cne $blobs[$path].sha -or $bytes.Length -ne $blobs[$path].size) { throw "Pinned startup blob mismatch: $path" }
        $text = [IO.File]::ReadAllText($destination)
        [xml] $xml = $text
        $namespaces = @($xml.DocumentElement.Attributes | Where-Object { $_.Name -eq 'xmlns' -or $_.Prefix -eq 'xmlns' } | ForEach-Object { [pscustomobject]@{ Alias = $_.LocalName; Uri = $_.Value } })
        $hints = @()
        $lines = $text -split '\r?\n'
        for ($line = 0; $line -lt $lines.Length; $line++) {
            if ($lines[$line] -match 'xctk:|xceed:|oxy:|mdxam:|dock:|avalonEdit:|i:Interaction|i:EventTrigger|i:InvokeCommandAction') {
                $hints += [pscustomobject]@{ Line = $line + 1; Text = $lines[$line].Trim() }
            }
        }
        $files.Add([pscustomobject]@{ Path = $path; BlobId = $blobId; Sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant(); Url = $url; Namespaces = $namespaces; ControlHints = $hints })
        foreach ($dictionary in $xml.SelectNodes('//*[local-name()="ResourceDictionary" or local-name()="SharedResourceDictionary"][@Source]')) {
            $source = $dictionary.GetAttribute('Source').Replace('\', '/')
            $source = $source -replace '^pack://application:,,,/', ''
            $targetPath = $null
            $external = $false
            if ($source -match '^/?([^/;]+);component/(.+)$') {
                $assemblyName = $Matches[1]
                if ($assemblyName -like 'NINA*') { $targetPath = "$assemblyName/$($Matches[2])" } else { $external = $true }
            } else {
                $baseUri = [Uri]::new("https://nina.invalid/$path")
                $targetPath = [Uri]::new($baseUri, $source).GetComponents([UriComponents]::Path, [UriFormat]::Unescaped)
            }
            $edges.Add([pscustomobject]@{ From = $path; Source = $dictionary.GetAttribute('Source'); To = $targetPath; ExternalAssembly = $external })
            if (!$external) { $pending.Enqueue($targetPath) }
        }
    }
    return [pscustomobject]@{ EntryPoint = 'NINA/App.xaml'; Files = $files.ToArray(); DictionaryEdges = $edges.ToArray(); Limitation = 'Static ResourceDictionary.Source closure only. DataTemplate instantiation, BAML record traversal and runtime resource lookup were not executed.' }
}

try {
    Write-Json @{ Status = 'running'; Revision = $revision } (Join-Path $output 'run-status.json')
    $inventory = Get-Content -LiteralPath (Join-Path $NinaEvidenceDirectory 'inventory.json') -Raw | ConvertFrom-Json
    $sourceStatus = Get-Content -LiteralPath (Join-Path $NinaEvidenceDirectory 'run-status.json') -Raw | ConvertFrom-Json
    if ($inventory.Revision -cne $revision -or $inventory.Source -ne 'github-https-get' -or $sourceStatus.Status -ne 'complete') { throw 'Pinned public NINA inventory is not complete.' }
    $pins = [ordered]@{
        'Dirkster.AvalonDock' = '4.74.1'; 'DotNetProjects.Extended.Wpf.Toolkit' = '5.0.129'
        'Microsoft.Xaml.Behaviors.Wpf' = '1.1.142'; 'OxyPlot.Wpf' = '2.2.0'; 'OxyPlot.Contrib' = '2.1.2'
        'OxyPlot.Contrib.Wpf' = '2.1.2'; 'OxyPlot.Core' = '2.2.0'; 'AvalonEdit' = '6.3.1.120'; 'MdXaml' = '1.27.0'
    }
    foreach ($projectPath in 'NINA/NINA.csproj', 'NINA.WPF.Base/NINA.WPF.Base.csproj', 'NINA.Core/NINA.Core.csproj') {
        $record = @($inventory.Files | Where-Object Path -eq $projectPath)
        if ($record.Count -ne 1) { throw "Missing pinned project evidence: $projectPath" }
        $actualHash = (Get-FileHash -LiteralPath (Join-Path $NinaEvidenceDirectory "source/$projectPath") -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $record[0].Sha256) { throw "Pinned project cache hash mismatch: $projectPath" }
    }
    foreach ($id in $pins.Keys) {
        $declared = @($inventory.Packages | Where-Object { $_.Name -eq $id -and $_.Project -in 'NINA/NINA.csproj', 'NINA.WPF.Base/NINA.WPF.Base.csproj', 'NINA.Core/NINA.Core.csproj' })
        if (!$declared.Count -or @($declared | Where-Object Version -ne $pins[$id]).Count) { throw "Pinned package version mismatch: $id" }
    }
    if (!$SkipRestore) {
        Set-Content -LiteralPath (Join-Path $projectDirectory 'Directory.Build.props') '<Project />'
        Set-Content -LiteralPath (Join-Path $projectDirectory 'Directory.Build.targets') '<Project />'
        Set-Content -LiteralPath (Join-Path $projectDirectory 'global.json') '{"sdk":{"version":"10.0.401","rollForward":"disable"}}'
        Set-Content -LiteralPath (Join-Path $projectDirectory 'NuGet.Config') '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>'
        $references = @($pins.Keys | ForEach-Object { '<PackageReference Include="' + $_ + '" Version="[' + $pins[$_] + ']" />' }) -join ''
        Set-Content -LiteralPath (Join-Path $projectDirectory 'Controls.csproj') ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableWindowsTargeting>true</EnableWindowsTargeting><NuGetAudit>false</NuGetAudit><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup>' + $references + '</ItemGroup></Project>')
        Push-Location $projectDirectory
        try {
            $restoreArguments = @('restore', 'Controls.csproj', '--configfile', 'NuGet.Config', '--packages', $packageDirectory, '--verbosity', 'minimal')
            if (Test-Path -LiteralPath 'packages.lock.json') { $restoreArguments += '--locked-mode' }
            $restoreOutput = Invoke-PortableDotnet $DotnetPath @restoreArguments 2>&1
            $restoreExitCode = $LASTEXITCODE
            $restoreOutput | Set-Content -LiteralPath (Join-Path $output 'restore.log')
            if ($restoreExitCode -ne 0) { throw "NuGet restore failed ($restoreExitCode). See restore.log." }
        } finally { Pop-Location }
    }
    $assetsPath = Join-Path $projectDirectory 'obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $nugetPackagingPath = Join-Path (Split-Path -Parent $DotnetPath) 'sdk/10.0.401/NuGet.Packaging.dll'
    [Reflection.Assembly]::LoadFrom($nugetPackagingPath) | Out-Null
    $target = $assets.targets.'net10.0-windows/win-x64'
    foreach ($id in $pins.Keys) {
        if ($target.psobject.Properties.Name -notcontains "$id/$($pins[$id])") { throw "Resolved assets omit pinned root package: $id" }
    }
    $packages = [Collections.Generic.List[object]]::new()
    $assemblies = [Collections.Generic.List[object]]::new()
    foreach ($property in $target.psobject.Properties) {
        $identity = $property.Name
        $details = $property.Value
        if ($details.type -ne 'package') { continue }
        $library = $assets.libraries.$identity
        $packagePath = Join-Path $packageDirectory $library.path
        $id, $version = $identity.Split('/')
        $archive = Join-Path $packagePath "$($id.ToLowerInvariant()).$version.nupkg"
        $archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        $sha = [Security.Cryptography.SHA512]::Create()
        try { $sha512 = [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($archive))) } finally { $sha.Dispose() }
        $expected = (Get-Content -LiteralPath "$archive.sha512" -Raw).Trim()
        if ($sha512 -cne $expected) { throw "NuGet archive SHA-512 mismatch: $identity" }
        $nugetMetadata = Get-Content -LiteralPath (Join-Path $packagePath '.nupkg.metadata') -Raw | ConvertFrom-Json
        if ($nugetMetadata.source -ne 'https://api.nuget.org/v3/index.json') { throw "Unexpected package source: $identity" }
        $packageReader = [NuGet.Packaging.PackageArchiveReader]::new($archive)
        try { $contentHash = $packageReader.GetContentHash([Threading.CancellationToken]::None, $null) } finally { $packageReader.Dispose() }
        if ($contentHash -cne $library.sha512 -or $contentHash -cne $nugetMetadata.contentHash) { throw "NuGet content hash mismatch: $identity" }
        $specArchive = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $specEntry = @($specArchive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
            if ($specEntry.Count -ne 1) { throw "Expected one nuspec: $identity" }
            $specReader = [IO.StreamReader]::new($specEntry[0].Open())
            try { [xml] $nuspec = $specReader.ReadToEnd() } finally { $specReader.Dispose() }
        } finally { $specArchive.Dispose() }
        $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $licenseNode = $metadata.SelectSingleNode('*[local-name()="license"]')
        $repositoryNode = $metadata.SelectSingleNode('*[local-name()="repository"]')
        $dependencyNode = $metadata.SelectSingleNode('*[local-name()="dependencies"]')
        $compileAssets = @(); $runtimeAssets = @(); $nativeAssets = @(); $dependencyEdges = @()
        if ($details.psobject.Properties.Name -contains 'compile') { $compileAssets = @($details.compile.psobject.Properties.Name | Where-Object { $_ -ne '_._' -and $_ -notlike '*/_._' }) }
        if ($details.psobject.Properties.Name -contains 'runtime') { $runtimeAssets = @($details.runtime.psobject.Properties.Name | Where-Object { $_ -ne '_._' -and $_ -notlike '*/_._' }) }
        if ($details.psobject.Properties.Name -contains 'native') { $nativeAssets = @($details.native.psobject.Properties.Name) }
        if ($details.psobject.Properties.Name -contains 'dependencies') { $dependencyEdges = @($details.dependencies.psobject.Properties | ForEach-Object { [pscustomobject]@{ Id = $_.Name; Requested = $_.Value } }) }
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        $licenseFile = $null
        try {
            $archiveEntries = @($zip.Entries | ForEach-Object { $_.FullName })
            if ($null -ne $licenseNode -and $licenseNode.GetAttribute('type') -eq 'file') {
                $licenseEntry = $zip.GetEntry($licenseNode.InnerText)
                if ($null -eq $licenseEntry) { throw "Declared license file is missing: $identity" }
                $licenseStream = $licenseEntry.Open()
                $licenseMemory = [IO.MemoryStream]::new()
                try {
                    $licenseStream.CopyTo($licenseMemory)
                    $licenseBytes = $licenseMemory.ToArray()
                    $licenseFile = @{ Path = $licenseEntry.FullName; Sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($licenseBytes)).ToLowerInvariant(); Text = [Text.Encoding]::UTF8.GetString($licenseBytes) }
                } finally { $licenseStream.Dispose(); $licenseMemory.Dispose() }
            }
            foreach ($asset in @($compileAssets + $runtimeAssets | Where-Object { $_ -like '*.dll' } | Sort-Object -Unique)) {
                $entry = $zip.GetEntry($asset)
                if ($null -eq $entry) { throw "Selected asset missing from archive: $identity/$asset" }
                $memory = [IO.MemoryStream]::new()
                $entryStream = $entry.Open()
                try { $entryStream.CopyTo($memory); $archiveAssetSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant() } finally { $entryStream.Dispose(); $memory.Dispose() }
                $assetPath = Join-Path $packagePath $asset
                if ((Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $archiveAssetSha256) { throw "Extracted asset differs from archive: $identity/$asset" }
                $assembly = Read-Assembly $assetPath
                $assembly | Add-Member -NotePropertyName Package -NotePropertyValue $identity
                $assembly | Add-Member -NotePropertyName Asset -NotePropertyValue $asset
                $assemblies.Add($assembly)
            }
        } finally { $zip.Dispose() }
        $license = if ($null -ne $licenseNode) { @{ Type = $licenseNode.GetAttribute('type'); Value = $licenseNode.InnerText } } else { @{ Type = 'legacy'; Value = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]').InnerText } }
        $packages.Add([pscustomobject]@{
            Id = $id; Version = $version; ArchiveSha256 = $archiveSha256; ArchiveSha512 = $sha512; NuGetContentHash = $contentHash
            Source = $nugetMetadata.source; Url = "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).$version.nupkg"
            License = $license; LicenseFile = $licenseFile; Repository = $(if ($null -ne $repositoryNode) { $repositoryNode.OuterXml } else { $null })
            DependencyGroups = $(if ($null -ne $dependencyNode) { $dependencyNode.OuterXml } else { $null })
            SelectedDependencies = $dependencyEdges; CompileAssets = $compileAssets; RuntimeAssets = $runtimeAssets; NativeAssets = $nativeAssets
            ArchiveEntries = $archiveEntries
        })
    }
    $startupResources = Read-StartupResources $inventory
    $result = [ordered]@{
        SchemaVersion = 1; NinaRevision = $revision; CollectedUtc = [DateTime]::UtcNow.ToString('o')
        Framework = 'net10.0-windows'; RuntimeIdentifier = 'win-x64'; Sdk = '10.0.401'; Source = 'https://api.nuget.org/v3/index.json'
        RootPackages = $pins; AssetsSha256 = (Get-FileHash -LiteralPath $assetsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        LockFileSha256 = (Get-FileHash -LiteralPath (Join-Path $projectDirectory 'packages.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        Packages = $packages.ToArray(); Assemblies = $assemblies.ToArray(); StartupResources = $startupResources
        Limitations = @('Bounded control graph excludes browser, ASCOM and other NINA application dependencies.', 'Metadata, embedded resource keys and selected IL call sites were inspected without executing package assemblies.', 'This is static evidence, not assembly load, BAML load, source rebuild or runtime compatibility qualification.')
    }
    Write-Json $result (Join-Path $output 'inventory.json')
    Write-Json @{ Status = 'complete'; Packages = $packages.Count; Assemblies = $assemblies.Count; CompletedUtc = [DateTime]::UtcNow.ToString('o') } (Join-Path $output 'run-status.json')
    Write-Output "NINA control closure: $($packages.Count) packages, $($assemblies.Count) selected assemblies."
    Write-Output (Join-Path $output 'inventory.json')
} catch {
    Write-Json @{ Status = 'failed'; Error = $_.Exception.Message } (Join-Path $output 'run-status.json')
    throw
}
