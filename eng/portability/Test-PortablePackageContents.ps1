[CmdletBinding()]
param([Parameter(Mandatory)][string] $DistributionDirectory, [string] $ConsumerDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../../src/Portability/Packaging/PackageTools.psm1') -Force
$distribution=[IO.Path]::GetFullPath($DistributionDirectory)
$manifest=[xml](Get-Content (Join-Path $distribution 'runtime/RuntimeManifest.xml') -Raw)
$expected=@{}
foreach($file in $manifest.portableRuntime.file) { $expected[[IO.Path]::GetFileName($file.path)]=$file }
$checks=[Collections.Generic.List[object]]::new()
function Check([string] $Name,[scriptblock] $Action) {
    & $Action
    $checks.Add([ordered]@{name=$Name;status='pass';detail='Package bytes and ordinary SDK manifests match the declared profile.'})
}
Check 'archive-runtime-allowlist-and-hashes' {
    $package=Get-Item (Join-Path $distribution 'feed/Wpf.Portable.Runtime.10.0.0-portable.1.nupkg')
    $zip=[IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        foreach($entry in $zip.Entries | Where-Object {$_.FullName -match '^(lib|runtimes)/'}) {
            $name=[IO.Path]::GetFileName($entry.FullName)
            if(!$expected.ContainsKey($name) -or $expected[$name].path -ne $entry.FullName) { throw "Unlisted runtime package payload: $($entry.FullName)" }
            $stream=$entry.Open()
            try { $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if($hash -ne $expected[$name].sha256) { throw "Runtime package hash mismatch: $name" }
        }
        foreach($file in $manifest.portableRuntime.file) { if(!$zip.GetEntry($file.path)) { throw "Declared runtime asset absent from package: $($file.path)" } }
        if($expected.ContainsKey('System.Private.Windows.Core.dll')) {
            $helperPath=Join-Path $distribution 'runtime/lib/net10.0/System.Private.Windows.Core.dll'
            if((Get-FileHash -LiteralPath $helperPath).Hash -ne $expected['System.Private.Windows.Core.dll'].sha256) {throw 'Windows.Core loose helper bytes differ from the archive-verified manifest.'}
            $helper=Get-PortableAssemblyInfo $helperPath
            if($helper.machine -ne 'I386' -or $helper.corFlags -notmatch 'ILOnly' -or $helper.nativeHeaderSize -ne 0 -or
                $helper.version -ne '10.0.0.0' -or $helper.token -ne 'b77a5c561934e089') {throw 'Windows.Core is not the qualified source-built IL-only helper.'}
            foreach($name in @('LICENSE.TXT','SourcePin.json','SourceManifest.props')) {
                if(!$zip.GetEntry("ThirdPartyNotices/Windows.Core/$name")) {throw "Windows.Core source notice missing: $name"}
            }
        }
    } finally { $zip.Dispose() }
}
Check 'avalonia-free-runtime-and-assembly-references' {
    $provenance = Get-Content (Join-Path $distribution 'runtime/Provenance.json') -Raw | ConvertFrom-Json
    if (@($provenance.profile.packages.PSObject.Properties.Name | Where-Object { $_ -match '^Avalonia(?:\.|$)' }).Count -gt 0) { throw 'The runtime profile still permits Avalonia packages.' }
    foreach ($file in $manifest.portableRuntime.file | Where-Object kind -eq managed) {
        $assembly = Get-PortableAssemblyInfo (Join-Path $distribution "runtime/$($file.path)")
        if ($assembly.name -match '^Avalonia(?:\.|$)' -or @($assembly.references | Where-Object name -match '^Avalonia(?:\.|$)').Count -gt 0) { throw "Avalonia runtime identity or reference: $($file.path)" }
    }
    if ((Get-FileHash (Join-Path $distribution 'runtime/runtimes/linux-x64/native/libSkiaSharp.so')).Hash -ne '66C856EAF1A47A00B23204C30C6EE407987BF5086ECC0A1A6B4FD67526B0CD02') { throw 'The runtime contains a different Linux Skia native implementation.' }
}
Check 'sdk-tool-closure-and-package-relative-imports' {
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $distribution 'feed/Wpf.Portable.Sdk.10.0.0-portable.1.nupkg'))
    try {
        $tools=@($zip.Entries | Where-Object FullName -match '^tools/net10.0/.*\.dll$' | ForEach-Object FullName)
        foreach($required in @('PresentationBuildTasks','System.CodeDom','System.Reflection.MetadataLoadContext')) {
            if("tools/net10.0/$required.dll" -notin $tools) { throw "Compiler dependency missing: $required" }
        }
        if($tools.Count -ne 3) { throw "Unexpected compiler task closure: $($tools -join ', ')" }
        if($expected.ContainsKey('System.Private.Windows.Core.dll') -and $zip.GetEntry('tools/contracts/System.Private.Windows.Core.dll')) {throw 'The source-built runtime helper still has a competing SDK contract copy.'}
        foreach($entry in $zip.Entries | Where-Object FullName -match '\.(props|targets)$') {
            $reader=[IO.StreamReader]::new($entry.Open())
            try { $text=$reader.ReadToEnd() } finally { $reader.Dispose() }
            if($text -match '(?i)((?<![A-Za-z])[A-Z]:[\\/]|/mnt/[a-z]/|ProjectReference\s+(Include|Update)=|src/Portability)') { throw "SDK build file contains a repository dependency: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
}
Check 'archive-provenance-and-notices-hashes' {
    foreach($package in @(@{id='Runtime';folder='runtime';manifest='RuntimeContentManifest.json'},@{id='Sdk';folder='sdk';manifest='SdkManifest.json'})) {
        $files=Get-Content (Join-Path $distribution "$($package.folder)/$($package.manifest)") -Raw|ConvertFrom-Json
        $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $distribution "feed/Wpf.Portable.$($package.id).10.0.0-portable.1.nupkg"))
        try {
            foreach($file in $files) {
                $entry=$zip.GetEntry($file.path)
                if(!$entry) {throw "Declared package content is absent: $($package.id)/$($file.path)"}
                $stream=$entry.Open()
                try {$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
                if($hash -ne $file.sha256) {throw "Package content hash differs: $($package.id)/$($file.path)"}
            }
            foreach($entry in $zip.Entries) {
                if($entry.FullName -in @($files.path) -or $entry.FullName -eq $package.manifest -or
                    $entry.FullName -in @('_rels/.rels','[Content_Types].xml') -or $entry.FullName -match '^Wpf\.Portable\.(Runtime|Sdk)\.nuspec$|^package/services/metadata/core-properties/[^/]+\.psmdcp$') {continue}
                throw "Uninventoried package content: $($package.id)/$($entry.FullName)"
            }
        } finally {$zip.Dispose()}
    }
}
if($ConsumerDirectory) {
    $consumer=[IO.Path]::GetFullPath($ConsumerDirectory)
    Check 'consumer-compiler-load-origins' {
        [xml]$origins=Get-Content (Join-Path $consumer 'compiler-origins.xml') -Raw
        [xml]$control=Get-Content (Join-Path $consumer 'compiler-origins-control.xml') -Raw
        foreach($evidence in @($origins,$control)) {
            if($evidence.compilerOrigins.passive -ne 'true' -or $evidence.compilerOrigins.GetAttribute('matchingBefore') -ne $evidence.compilerOrigins.GetAttribute('matchingAfter') -or
                $evidence.compilerOrigins.observerReferences -match 'PresentationBuildTasks|System.CodeDom|System.Reflection.MetadataLoadContext') { throw 'Compiler observer changed or referenced its observed dependencies.' }
        }
        $sdkManifest=Get-Content (Join-Path $distribution 'sdk/SdkManifest.json') -Raw|ConvertFrom-Json
        foreach($name in @('PresentationBuildTasks','System.CodeDom','System.Reflection.MetadataLoadContext')) {
            $actual=@($origins.compilerOrigins.assembly|Where-Object name -eq $name)
            $expected=@($sdkManifest|Where-Object path -eq "tools/net10.0/$name.dll")
            if($actual.Count -ne 1 -or $expected.Count -ne 1 -or $actual[0].path.Replace('\','/') -notmatch '/\.packages/wpf\.portable\.sdk/10\.0\.0-portable\.1/tools/net10\.0/' -or
                $actual[0].sha256 -ne $expected[0].sha256) { throw "Already-loaded compiler dependency did not match the SDK package origin: $name" }
        }
        if(@($origins.compilerOrigins.assembly|Where-Object identity -like 'System.Reflection.MetadataLoadContext, Version=10.0.0.13,*').Count -ne 1) { throw 'Compiler loaded a different MetadataLoadContext identity.' }
        if(@($origins.compilerOrigins.assembly|ForEach-Object context|Select-Object -Unique).Count -ne 1) {throw 'Compiler dependencies were not observed in the actual PBT task load context.'}
    }
    $outputs=@(Join-Path $consumer 'App/bin/Release/net10.0') + @(Get-ChildItem -LiteralPath $consumer -Directory -Filter 'publish-*' | ForEach-Object FullName)
    foreach($directory in $outputs) {
        $leaf=Split-Path $directory -Leaf
        Check "consumer-$leaf-identities-and-no-compile-contracts" {
            foreach($file in $manifest.portableRuntime.file) {
                $candidates=@((Join-Path $directory ([IO.Path]::GetFileName($file.path))),(Join-Path $directory $file.path) | Select-Object -Unique | Where-Object {Test-Path $_ -PathType Leaf})
                if($candidates.Count -ne 1 -or (Get-FileHash $candidates[0]).Hash -ne $file.sha256) { throw "Consumer runtime asset differs or has ambiguous deployment locations: $($file.path)" }
            }
            foreach($name in @('System.Private.Windows.GdiPlus','DirectWriteForwarder','ReachFramework','System.Printing')) {
                if(Test-Path (Join-Path $directory "$name.dll")) { throw "Compile-only contract reached consumer output: $name" }
            }
            if(!$expected.ContainsKey('PresentationUI.dll') -and (Test-Path (Join-Path $directory 'PresentationUI.dll'))) {throw 'Unqualified PresentationUI reached an older consumer profile.'}
            if(!$expected.ContainsKey('System.Private.Windows.Core.dll') -and (Test-Path (Join-Path $directory 'System.Private.Windows.Core.dll'))) {throw 'Unqualified Windows.Core reached an older consumer profile.'}
            $deps=Get-Content (Join-Path $directory 'App.deps.json') -Raw | ConvertFrom-Json -AsHashtable
            $target=$deps.targets[$deps.runtimeTarget.name]
            if (@($deps.libraries.Keys | Where-Object { $_ -match '^Avalonia(?:[./]|$)' }).Count -gt 0) { throw 'Avalonia remains in the consumer dependency manifest.' }
            if(@($deps.libraries.Keys | Where-Object {$_ -match 'Microsoft.AspNetCore.App'}).Count -gt 0 -or
                @(Get-ChildItem -LiteralPath $directory -File -Filter 'Microsoft.AspNetCore.*').Count -gt 0) { throw 'Auxiliary ASP.NET restore pack entered the application runtime.' }
            $singleIdentities=@('WindowsBase','System.Windows.Extensions','Wpf.Portable.NativeHost')
            if($expected.ContainsKey('PresentationUI.dll')) {$singleIdentities+='PresentationUI'}
            if($expected.ContainsKey('PresentationFramework.Classic.dll')) {$singleIdentities+='PresentationFramework.Classic'}
            if($expected.ContainsKey('System.Private.Windows.Core.dll')) {$singleIdentities+='System.Private.Windows.Core'}
            foreach($name in $singleIdentities) {
                $matches=@($target.Values | ForEach-Object {if($_.ContainsKey('runtime')) {$_.runtime.Keys}} | Where-Object { [IO.Path]::GetFileName($_) -eq "$name.dll" })
                if($matches.Count -ne 1) { throw "Consumer .deps has $($matches.Count) runtime identities for $name" }
            }
            $config=Get-Content (Join-Path $directory 'App.runtimeconfig.json') -Raw
            if($config.Contains('Microsoft.WindowsDesktop.App')) { throw 'Consumer still requires the WindowsDesktop shared framework.' }
            if($deps.runtimeTarget.name.EndsWith('/linux-x64') -and !(Test-Path (Join-Path $directory 'libSkiaSharp.so'))) { throw 'RID-specific publication lacks its native Skia runtime asset.' }
        }
    }
    foreach($result in Get-ChildItem -LiteralPath $consumer -Filter '*results.json' -File) {
        $data=Get-Content $result.FullName -Raw | ConvertFrom-Json
        if(@($data.checks | Where-Object status -ne pass).Count -gt 0) { throw "Consumer reported a failed behavior: $($result.FullName)" }
    }
}
[ordered]@{probe='portable-package-audit';scope='Closed .NET10 Linuxx64 package profile';checks=$checks} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $distribution 'package-audit-results.json') -Encoding utf8NoBOM
$checks
