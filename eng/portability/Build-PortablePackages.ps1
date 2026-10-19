[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $Dotnet = 'dotnet'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$portable = Join-Path $repo 'src/Portability'
$packaging = Join-Path $portable 'Packaging'
Import-Module (Join-Path $packaging 'PackageTools.psm1') -Force
$profile = Get-Content (Join-Path $packaging 'PackageProfile.json') -Raw | ConvertFrom-Json -AsHashtable
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts/portability/distribution/' + [Guid]::NewGuid().ToString('N')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) { throw "Select a fresh output directory: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$env:NUGET_PACKAGES = Join-Path $repo 'artifacts/portability/packages'
$env:DOTNET_CLI_HOME = Join-Path $repo 'artifacts/portability/cli-home'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$isolation = @("-p:DirectoryBuildPropsPath=$(Join-Path $packaging 'IsolatedBuild.props')",
    "-p:PortableDistributionBuildRoot=$($output.Replace('\','/'))/graph/",
    "-p:_PresentationBuildTasksAssembly=$($output.Replace('\','/'))/graph/bin/PresentationBuildTasks/Release/net10.0/PresentationBuildTasks.dll")
function Invoke-Build([string[]] $Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location $portable
try {
    if ((& $Dotnet --version).Trim() -ne $profile.sdk) { throw "Portable package producer requires SDK $($profile.sdk)." }
    Invoke-Build (@('restore','Packaging/Stage/Stage.csproj','-r',$profile.runtimeIdentifier,
        '-p:SelfContained=true','-p:RuntimeFrameworkVersion=10.0.12') + $isolation)
    foreach ($assetsFile in Get-ChildItem (Join-Path $output 'graph/obj') -Filter 'project.assets.json' -Recurse) {
        $assets = Get-Content $assetsFile.FullName -Raw | ConvertFrom-Json -AsHashtable
        if (@($assets.libraries.Keys | Where-Object { $_ -match '^Avalonia(?:[./]|$)' }).Count -gt 0) {
            throw "Avalonia dependency entered the delivery graph: $($assetsFile.FullName)"
        }
    }
    Invoke-Build (@('publish','Packaging/Stage/Stage.csproj','-c','Release','-r',$profile.runtimeIdentifier,
        '--self-contained','false','--disable-build-servers','-v:diag','-p:UseAppHost=false','-o',(Join-Path $output 'published')) + $isolation)
    Invoke-Build (@('build','Markup/PresentationBuildTasks/PresentationBuildTasks.csproj','-c','Release','--disable-build-servers',
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $packaging 'CompilerClosure.targets')") + $isolation)
    $published = Join-Path $output 'published'
    $runtime = Join-Path $output 'runtime'
    $sdk = Join-Path $output 'sdk'
    $feed = Join-Path $output 'feed'
    New-Item -ItemType Directory -Path $runtime,$sdk,$feed | Out-Null
    foreach ($id in @('microsoft.netcore.app.runtime.linux-x64','microsoft.netcore.app.host.linux-x64','microsoft.aspnetcore.app.runtime.linux-x64')) {
        Copy-PortableFile (Join-Path $env:NUGET_PACKAGES "$id/10.0.12/$id.10.0.12.nupkg") (Join-Path $feed "$id.10.0.12.nupkg")
    }
    @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | ForEach-Object {
        [ordered]@{file=$_.Name;sha256=(Get-FileHash $_.FullName).Hash;role='Unchanged official SDK-requested offline restore pack; not a WPF runtime payload.'}
    }) | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'OfflineRestorePacks.json') -Encoding utf8NoBOM
    $deps = Get-Content (Join-Path $published 'Wpf.Portable.Distribution.Stage.deps.json') -Raw | ConvertFrom-Json -AsHashtable
    $target = $deps.targets[$deps.runtimeTarget.name]
    $manifest = [Xml.XmlDocument]::new()
    $root = $manifest.CreateElement('portableRuntime')
    $root.SetAttribute('version',$profile.version)
    $root.SetAttribute('rid',$profile.runtimeIdentifier)
    $manifest.AppendChild($root) | Out-Null
    $records = [Collections.Generic.List[object]]::new()
    $seen = @{}
    $noticeRoots = @{}
    function Add-Asset([string] $Source, [string] $Destination, [string] $Kind, [string] $Origin, [string] $PackageId, [string] $PackageVersion) {
        $name = [IO.Path]::GetFileName($Destination)
        $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        if ($seen.ContainsKey($name)) {
            if ($seen[$name] -ne $hash) { throw "Conflicting runtime assets named $name require an explicit profile decision." }
            return
        }
        $seen[$name] = $hash
        $identity = $null
        if ($Kind -eq 'managed') {
            $identity = Get-PortableAssemblyInfo $Source
            if ($identity.name -match '^Avalonia(?:\.|$)' -or @($identity.references | Where-Object name -match '^Avalonia(?:\.|$)').Count -gt 0) {
                throw "Avalonia identity or assembly reference entered the runtime: $name"
            }
            if ($name -eq 'System.Private.Windows.Core.dll' -and ($identity.machine -ne 'I386' -or $identity.corFlags -notmatch 'ILOnly' -or
                $identity.nativeHeaderSize -ne 0 -or $identity.version -ne '10.0.0.0' -or $identity.token -ne 'b77a5c561934e089')) {
                throw 'Windows.Core runtime must be the original-source AnyCPU IL-only ECMA helper.'
            }
        }
        else {
            Assert-PortableElf $Source
            if ($name -eq 'libSkiaSharp.so' -and $hash -ne '66C856EAF1A47A00B23204C30C6EE407987BF5086ECC0A1A6B4FD67526B0CD02') {
                throw 'The Linux Skia asset must be the pinned Fontconfig-enabled SkiaSharp.NativeAssets.Linux 3.119.4 implementation.'
            }
        }
        Copy-PortableFile $Source (Join-Path $runtime $Destination)
        $file = $manifest.CreateElement('file')
        $file.SetAttribute('path',$Destination.Replace('\','/'))
        $file.SetAttribute('kind',$Kind)
        $file.SetAttribute('sha256',$hash)
        if ($PackageId) {
            $compatible = $manifest.CreateElement('compatiblePackage')
            $compatible.SetAttribute('id',$PackageId); $compatible.SetAttribute('version',$PackageVersion)
            $file.AppendChild($compatible) | Out-Null
        }
        $root.AppendChild($file) | Out-Null
        $records.Add([ordered]@{ path=$Destination; kind=$Kind; sha256=$hash; origin=$Origin; assembly=$identity })
    }
    foreach ($entry in $target.GetEnumerator() | Sort-Object Key) {
        $library = $deps.libraries[$entry.Key]
        $separator = $entry.Key.LastIndexOf('/')
        $id = $entry.Key.Substring(0,$separator); $version = $entry.Key.Substring($separator+1)
        foreach ($kind in @('runtime','native')) {
            if (!$entry.Value.ContainsKey($kind)) { continue }
            foreach ($asset in $entry.Value[$kind].Keys) {
                if ($asset.EndsWith('/_._')) { continue }
                $name = [IO.Path]::GetFileName($asset)
                $assemblyName = [IO.Path]::GetFileNameWithoutExtension($name)
                if ($assemblyName -eq 'Wpf.Portable.Distribution.Stage' -or $assemblyName -in $profile.metadataOnly) { continue }
                $source = Join-Path $published $name
                if (!(Test-Path -LiteralPath $source)) { throw "Publish manifest asset missing from output: $asset" }
                if ($library.type -eq 'package') {
                    if (!$profile.packages.ContainsKey($id) -or $profile.packages[$id] -ne $version) { throw "Unqualified runtime dependency: $id $version ($asset)" }
                    $origin = Get-PortableAssetOrigin $source $asset $library $env:NUGET_PACKAGES
                    if (!$origin) { throw "Published asset differs from its declared package origin: $id $asset" }
                    $noticeRoots[$library.path] = $origin.root
                    $sourceOrigin = "$($library.path)/$asset"
                } elseif ($assemblyName -in ($profile.frameworkAssemblies + $profile.backendAssemblies)) {
                    $projectName=if($assemblyName -eq 'Wpf.Portable.NativeHost') {'NativeHost'} else {$assemblyName}
                    $projectOutput=Join-Path $output "graph/bin/$projectName/Release/net10.0/$name"
                    if(!(Test-Path $projectOutput) -or (Get-FileHash $projectOutput).Hash -ne (Get-FileHash $source).Hash) { throw "Runtime source assembly differs from its actual project output: $name" }
                    $sourceOrigin = if($assemblyName -in $profile.backendAssemblies) {"source:src/Portability/Backend/NativeHost/$projectName.csproj"} else {"source:src/Portability/Framework/$projectName/$projectName.csproj"}
                    $id = ''; $version = ''
                } elseif ($assemblyName -in $profile.genuineManagedContracts) {
                    $desktopRoot = Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.13'
                    $original = Join-Path $desktopRoot "runtimes/win-x64/lib/net10.0/$name"
                    if ((Get-FileHash $original).Hash -ne (Get-FileHash $source).Hash) { throw "Genuine managed helper differs from WindowsDesktop package: $name" }
                    $noticeRoots['microsoft.windowsdesktop.app.runtime.win-x64/10.0.13'] = $desktopRoot
                    $sourceOrigin = "microsoft.windowsdesktop.app.runtime.win-x64/10.0.13/runtimes/win-x64/lib/net10.0/$name"
                    $id = ''; $version = ''
                } else { throw "Unclassified published runtime assembly: $($entry.Key) $asset" }
                $destination = if ($kind -eq 'native') { "runtimes/linux-x64/native/$name" } else { "lib/net10.0/$name" }
                Add-Asset $source $destination $(if ($kind -eq 'native') {'native'} else {'managed'}) $sourceOrigin $id $version
            }
        }
    }
    foreach ($required in $profile.frameworkAssemblies + $profile.backendAssemblies + $profile.genuineManagedContracts) {
        if (!$seen.ContainsKey("$required.dll")) { throw "Required runtime assembly missing: $required" }
    }
    foreach ($entry in $noticeRoots.GetEnumerator()) { Export-PortablePackageNotices $entry.Value (Join-Path $runtime ('ThirdPartyNotices/packages/' + $entry.Key)) }
    $graphicsNotices = Join-Path $packaging 'ThirdPartyNotices/Graphics'
    Copy-Item -LiteralPath $graphicsNotices -Destination (Join-Path $runtime 'ThirdPartyNotices/Graphics') -Recurse
    Copy-PortableFile (Join-Path $repo 'LICENSE.TXT') (Join-Path $runtime 'LICENSE.txt')
    Copy-PortableFile (Join-Path $repo 'LICENSE.TXT') (Join-Path $sdk 'LICENSE.txt')
    $unicodeNotices = Join-Path $published 'ThirdPartyNotices/TextUnicode'
    if (!(Test-Path $unicodeNotices)) { throw 'The required Unicode source notices were not published.' }
    Copy-Item -LiteralPath $unicodeNotices -Destination (Join-Path $runtime 'ThirdPartyNotices/TextUnicode') -Recurse
    foreach ($name in @('LICENSE.TXT','SourcePin.json','SourceManifest.props')) {
        Copy-PortableFile (Join-Path $portable "Framework/System.Private.Windows.Core/$name") (Join-Path $runtime "ThirdPartyNotices/Windows.Core/$name")
    }
    $manifest.Save((Join-Path $runtime 'RuntimeManifest.xml'))
    Copy-PortableFile (Join-Path $packaging 'RuntimePolicy.targets') (Join-Path $runtime 'buildTransitive/Wpf.Portable.Runtime.targets')
    Copy-PortableFile (Join-Path $packaging 'PortableProfile.targets') (Join-Path $runtime 'policy/PortableProfile.targets')
    Copy-Item -LiteralPath (Join-Path $packaging 'Sdk') -Destination (Join-Path $sdk 'Sdk') -Recurse
    Copy-PortableFile (Join-Path $repo 'src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/Microsoft.WinFX.targets') (Join-Path $sdk 'targets/Microsoft.WinFX.targets')
    Copy-PortableFile (Join-Path $packaging 'PortableProfile.targets') (Join-Path $sdk 'targets/PortableProfile.targets')
    $compiler = Join-Path $output 'graph/bin/PresentationBuildTasks/Release/net10.0'
    $compilerFiles = @('PresentationBuildTasks.dll','PresentationBuildTasks.deps.json','System.CodeDom.dll','System.Reflection.MetadataLoadContext.dll')
    foreach ($name in $compilerFiles) { Copy-PortableFile (Join-Path $compiler $name) (Join-Path $sdk "tools/net10.0/$name") }
    foreach ($id in @('system.codedom','system.reflection.metadataloadcontext')) {
        $packageRoot=Join-Path $env:NUGET_PACKAGES "$id/10.0.13"
        $files=@(Get-ChildItem (Join-Path $packageRoot 'lib/net10.0') -Filter '*.dll')
        if($files.Count -ne 1) { throw "Unexpected compiler NuGet assembly closure: $id" }
        $file=$files[0]
        $info=Get-PortableAssemblyInfo $file.FullName
        if($info.nativeHeaderSize -ne 0 -or $info.corFlags -notmatch 'ILOnly' -or $info.machine -ne 'I386') { throw "Compiler dependency is not host-neutral IL: $($file.FullName)" }
        if($info.sha256 -ne (Get-FileHash (Join-Path $compiler $file.Name)).Hash) { throw "Compiler dependency differs from qualified NuGet implementation: $id" }
        Export-PortablePackageNotices $packageRoot (Join-Path $sdk "ThirdPartyNotices/$id/10.0.13")
    }
    $contracts = Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.13/runtimes/win-x64/lib/net10.0'
    foreach ($name in $profile.metadataOnly) { Copy-PortableFile (Join-Path $contracts "$name.dll") (Join-Path $sdk "tools/contracts/$name.dll") }
    Export-PortablePackageNotices (Split-Path (Split-Path (Split-Path (Split-Path $contracts -Parent) -Parent) -Parent) -Parent) (Join-Path $sdk 'ThirdPartyNotices/WindowsDesktopContracts')
    $provenance = [ordered]@{ profile=$profile; sourceRevision=(& git -C $repo rev-parse HEAD).Trim(); assets=$records; compilerFiles=$compilerFiles; producedUtc=[DateTime]::UtcNow.ToString('O') }
    $provenance | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $runtime 'Provenance.json') -Encoding utf8NoBOM
    Copy-PortableFile (Join-Path $runtime 'Provenance.json') (Join-Path $sdk 'Provenance.json')
    $sourceInputs=@(Get-ChildItem (Join-Path $output 'graph/obj') -Filter 'portable-inputs.txt' -Recurse | Get-Content | Sort-Object -Unique | Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} | ForEach-Object {
        $path=[IO.Path]::GetFullPath($_)
        $relative=if($path.StartsWith($repo,[StringComparison]::OrdinalIgnoreCase)) {[IO.Path]::GetRelativePath($repo,$path).Replace('\','/')} else {$path}
        [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $path).Hash}
    })
    foreach ($input in $sourceInputs) {
        $path = $input.path.Replace('\','/')
        if ($path -match '/NativeHost/(Avalonia\.X11|BuildTools)/' -or $path -match '/Avalonia[^/]*\.(props|targets|csproj)$') {
            throw "Avalonia source project or build import entered the delivery graph: $path"
        }
    }
    $sourceInputs | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runtime 'BuildInputs.json') -Encoding utf8NoBOM
    Copy-PortableFile (Join-Path $runtime 'BuildInputs.json') (Join-Path $sdk 'BuildInputs.json')
    @(Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($runtime,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName).Hash}
    }) | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runtime 'RuntimeContentManifest.json') -Encoding utf8NoBOM
    @(Get-ChildItem -LiteralPath $sdk -File -Recurse | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($sdk,$_.FullName).Replace('\','/'); sha256=(Get-FileHash $_.FullName).Hash }
    }) | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $sdk 'SdkManifest.json') -Encoding utf8NoBOM
    Invoke-Build @('pack','Packaging/RuntimePackage/RuntimePackage.csproj','-c','Release','--disable-build-servers',"-p:RuntimeStageDirectory=$runtime",'-o',$feed)
    Invoke-Build @('pack','Packaging/SdkPackage/SdkPackage.csproj','-c','Release','--disable-build-servers',"-p:SdkStageDirectory=$sdk",'-o',$feed)
    & (Join-Path $PSScriptRoot 'Test-PortablePackageContents.ps1') -DistributionDirectory $output
    [ordered]@{ output=$output; feed=$feed; managed=@($records | Where-Object kind -eq managed).Count; native=@($records | Where-Object kind -eq native).Count;
        packages=@(Get-ChildItem -LiteralPath $feed -Filter 'Wpf.Portable.*.nupkg' | ForEach-Object {@{file=$_.Name;sha256=(Get-FileHash $_.FullName).Hash}})
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'producer-results.json')
    Write-Output "Local portable packages: $feed"
} finally { Pop-Location }
