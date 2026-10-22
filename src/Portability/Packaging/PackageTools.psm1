Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PortableAssemblyInfo([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (!$reader.HasMetadata) { throw "Runtime DLL has no managed metadata: $Path" }
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
        foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($metadata.GetString($type.Name) -eq 'ReferenceAssemblyAttribute') { throw "Reference-only assembly cannot enter runtime package: $Path" }
        }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($Path)
        $references = @($metadata.AssemblyReferences | ForEach-Object {
            $reference=$metadata.GetAssemblyReference($_)
            [ordered]@{name=$metadata.GetString($reference.Name);version=$reference.Version.ToString();publicKeyOrToken=[Convert]::ToHexString($metadata.GetBlobBytes($reference.PublicKeyOrToken)).ToLowerInvariant()}
        })
        [pscustomobject]@{
            name = $identity.Name; version = $identity.Version.ToString(); identity = $identity.FullName
            token = [Convert]::ToHexString($identity.GetPublicKeyToken()).ToLowerInvariant()
            sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
            machine = $reader.PEHeaders.CoffHeader.Machine.ToString()
            corFlags = $reader.PEHeaders.CorHeader.Flags.ToString()
            nativeHeaderSize = $reader.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size
            references = $references
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

function Assert-PortableElf([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 127 -or $bytes[1] -ne 69 -or $bytes[2] -ne 76 -or $bytes[3] -ne 70 -or
        $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes,18) -ne 62) {
        throw "Native runtime file is not a Linux x64 ELF image: $Path"
    }
}

function Copy-PortableFile([string] $Source, [string] $Destination) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Destination)) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Export-PortablePackageNotices([string] $PackageRoot, [string] $Destination) {
    $files = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse | Where-Object {
        $_.Name -match '(?i)^(licen[cs]e|notice|third.?party)' -and $_.Extension -ne '.dll'
    })
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($PackageRoot, $file.FullName)
        Copy-PortableFile $file.FullName (Join-Path $Destination $relative)
    }
    foreach ($nuspec in Get-ChildItem -LiteralPath $PackageRoot -Filter '*.nuspec' -File) {
        Copy-PortableFile $nuspec.FullName (Join-Path $Destination ($nuspec.Name + '.xml'))
    }
    if ($files.Count -eq 0) {
        $spec = Get-ChildItem -LiteralPath $PackageRoot -Filter '*.nuspec' -File | Select-Object -First 1
        if (!$spec) { throw "No license metadata exists for $PackageRoot" }
        [xml]$xml = Get-Content -LiteralPath $spec.FullName -Raw
        if (!$xml.SelectSingleNode("//*[local-name()='license']")) { throw "No license file or declared license found for $PackageRoot" }
    }
}

function Get-PortableAssetOrigin([string] $StageFile, [string] $AssetPath, $Library, [string] $PackageCache) {
    if ($Library.type -eq 'package') {
        $packageRoot = Join-Path $PackageCache $Library.path
        $candidate = Join-Path $packageRoot $AssetPath
        if (!(Test-Path -LiteralPath $candidate)) { throw "Runtime asset provenance is missing: $candidate" }
        if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $StageFile -Algorithm SHA256).Hash) {
            return $null
        }
        return [pscustomobject]@{ root = $packageRoot; path = $candidate }
    }
    return $null
}

function Copy-PortableFrozenOracle([string] $RepositoryRoot,[string] $ConsumerDirectory,
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._-]*$')][string] $OracleName = '') {
    $manifestFile=if($OracleName) {"MicrosoftCompilerOracle.$OracleName.json"} else {'MicrosoftCompilerOracle.json'}
    $originPath=Join-Path $RepositoryRoot "src/Portability/Tests/MarkupRuntimeIntegration/$manifestFile"
    $origin=Get-Content $originPath -Raw | ConvertFrom-Json
    if($OracleName -and $origin.oracleName -cne $OracleName) {throw 'Named Microsoft oracle manifest does not match the requested name.'}
    $archivePath=if($OracleName) {"artifacts/portability/markup-runtime-integration/microsoft-oracles/$OracleName"} else {'artifacts/portability/markup-runtime-integration/microsoft-oracle'}
    $binary=Join-Path $RepositoryRoot "$archivePath/FixtureControls.dll"
    if((Get-FileHash $binary).Hash -ne $origin.fixtureSha256) { throw 'The frozen Microsoft BAML oracle bytes differ from their recorded origin.' }
    Copy-PortableFile $binary (Join-Path $ConsumerDirectory 'Frozen/FixtureControls.dll')
    Copy-PortableFile $originPath (Join-Path $ConsumerDirectory 'Frozen/MicrosoftCompilerOracle.json')
}

Export-ModuleMember -Function Get-PortableAssemblyInfo,Assert-PortableElf,Copy-PortableFile,Export-PortablePackageNotices,Get-PortableAssetOrigin,Copy-PortableFrozenOracle
