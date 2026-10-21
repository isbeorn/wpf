[CmdletBinding()]
param([Parameter(Mandatory)][string] $DistributionDirectory,[string] $Dotnet='dotnet',
    [string] $PackageCache = "$([Environment]::GetFolderPath('UserProfile'))/.nuget/packages",
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._-]*$')][string] $OracleName = '')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $repo 'src/Portability/Packaging/PackageTools.psm1') -Force
$distribution=[IO.Path]::GetFullPath($DistributionDirectory)
$testRoot=Join-Path $distribution 'dependency-policy'
if(Test-Path $testRoot) { throw "Dependency policy verification requires a fresh directory: $testRoot" }
$feed=Join-Path $testRoot 'feed'
New-Item -ItemType Directory -Path $feed | Out-Null
Copy-Item -Path (Join-Path $distribution 'feed/*.nupkg') -Destination $feed
foreach($version in @('10.0.13','10.0.12')) {
    Copy-Item -LiteralPath (Join-Path $PackageCache "system.windows.extensions/$version/system.windows.extensions.$version.nupkg") -Destination $feed
}
$nativePackageId='SkiaSharp.NativeAssets.Win32'
$nativePackageVersion='3.119.4'
$nativePackageDirectory=$nativePackageId.ToLowerInvariant()
Copy-Item -LiteralPath (Join-Path $PackageCache "$nativePackageDirectory/$nativePackageVersion/$nativePackageDirectory.$nativePackageVersion.nupkg") -Destination $feed
$results=[Collections.Generic.List[object]]::new()
foreach($version in @('10.0.13','10.0.12')) {
    $consumer=Join-Path $testRoot $version
    New-Item -ItemType Directory -Path $consumer | Out-Null
    Copy-Item -Path (Join-Path $repo 'src/Portability/Packaging/Consumer/*') -Destination $consumer -Recurse
    Copy-PortableFrozenOracle $repo $consumer -OracleName $OracleName
    Copy-Item (Join-Path $repo 'src/Portability/Tests/NativeWindowIntegration/X11Fixture.cs') (Join-Path $consumer 'App/X11Fixture.cs')
    Copy-Item (Join-Path $repo 'src/Portability/Tests/Fixtures/Fonts/DejaVuSans.ttf') (Join-Path $consumer 'DejaVuSans.ttf')
    '<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.props')
    '<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.targets')
    '{"sdk":{"version":"10.0.401","rollForward":"disable"}}' | Set-Content (Join-Path $consumer 'global.json')
    $escaped=[Security.SecurityElement]::Escape($feed)
    "<configuration><packageSources><clear/><add key=`"local`" value=`"$escaped`"/></packageSources></configuration>" | Set-Content (Join-Path $consumer 'NuGet.Config')
    $project=Join-Path $consumer 'App/App.csproj'
    [xml]$xml=Get-Content $project -Raw
    $group=$xml.CreateElement('ItemGroup');$reference=$xml.CreateElement('PackageReference')
    $reference.SetAttribute('Include','System.Windows.Extensions');$reference.SetAttribute('Version',"[$version]")
    $group.AppendChild($reference)|Out-Null;$xml.Project.AppendChild($group)|Out-Null;$xml.Save($project)
    $env:NUGET_PACKAGES=Join-Path $consumer '.packages';$env:DOTNET_CLI_HOME=Join-Path $consumer '.cli'
    Push-Location $consumer
    try {
        Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --disable-build-servers *> build.log
        if($version -eq '10.0.12') {
            if($LASTEXITCODE -eq 0 -or !(Select-String -Path build.log -SimpleMatch -Pattern "Consumer dependency 'System.Windows.Extensions 10.0.12'")) { throw "Unqualified dependency did not fail with the manifest diagnostic: $consumer/build.log" }
            $results.Add(@{name='unqualified-version-rejected';status='pass';detail='Actual NuGet 10.0.12 dependency failed with the named manifest diagnostic.'})
        } else {
            if($LASTEXITCODE -ne 0) { throw "Qualified dependency did not build: $consumer/build.log" }
            Invoke-PortableDotnet $Dotnet App/bin/Release/net10.0/App.dll >results.json
            if($LASTEXITCODE -ne 0) { throw "Qualified dependency lost runtime semantics: $consumer/results.json" }
            $results.Add(@{name='qualified-version-one-identity';status='pass';detail='Actual NuGet 10.0.13 duplicate compiled and ran using the manifest-owned implementation.'})
            $invalidProfiles=@(
                @{name='unsupported-rid';argument='-p:RuntimeIdentifier=linux-arm64';diagnostic='only an empty RID or linux-x64'},
                @{name='trim';argument='-p:PublishTrimmed=true';diagnostic='Trimming, NativeAOT and single-file'},
                @{name='aot';argument='-p:PublishAot=true';diagnostic='Trimming, NativeAOT and single-file'},
                @{name='single-file';argument='-p:PublishSingleFile=true';diagnostic='Trimming, NativeAOT and single-file'},
                @{name='stock-desktop-injection';argument='-p:UseWPF=true';diagnostic='Remove UseWPF=true'})
            foreach($invalid in $invalidProfiles) {
                $log="$($invalid.name).log"
                Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --no-restore --disable-build-servers $invalid.argument *> $log
                if($LASTEXITCODE -eq 0 -or !(Select-String -Path $log -SimpleMatch -Pattern $invalid.diagnostic)) { throw "Unqualified profile did not fail with its SDK diagnostic: $consumer/$log" }
                $results.Add(@{name="reject-$($invalid.name)";status='pass';detail='The actual build failed with the explicit SDK profile diagnostic.'})
            }
            $nativeGroup=$xml.CreateElement('ItemGroup');$nativeReference=$xml.CreateElement('PackageReference')
            $nativeReference.SetAttribute('Include',$nativePackageId);$nativeReference.SetAttribute('Version',"[$nativePackageVersion]")
            $nativeGroup.AppendChild($nativeReference)|Out-Null;$xml.Project.AppendChild($nativeGroup)|Out-Null;$xml.Save($project)
            Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --disable-build-servers *> unknown-native.log
            if($LASTEXITCODE -eq 0 -or !(Select-String -Path unknown-native.log -SimpleMatch -Pattern 'Consumer native asset is outside the qualified Linux x64 runtime manifest')) { throw "Actual unqualified native package did not fail with the runtime manifest diagnostic: $consumer/unknown-native.log" }
            $results.Add(@{name='unqualified-native-asset-rejected';status='pass';detail="Actual $nativePackageId $nativePackageVersion native runtime asset failed with the named manifest diagnostic."})
        }
    } finally { Pop-Location }
}
@{probe='portable-package-dependency-policy';checks=$results}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $distribution 'dependency-policy-results.json')
