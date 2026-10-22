[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DistributionDirectory,
    [ValidateSet('Windows','PrepareLinux')][string] $HostProfile = 'Windows',
    [string] $Dotnet = 'dotnet',
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._-]*$')][string] $OracleName = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $repo 'src/Portability/Packaging/PackageTools.psm1') -Force
$distribution=[IO.Path]::GetFullPath($DistributionDirectory)
$consumer=Join-Path $distribution ('external-' + $HostProfile.ToLowerInvariant())
if(Test-Path $consumer) { throw "Use a clean external consumer directory: $consumer" }
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item -Path (Join-Path $repo 'src/Portability/Packaging/Consumer/*') -Destination $consumer -Recurse
Copy-PortableFrozenOracle $repo $consumer -OracleName $OracleName
Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Tests/NativeWindowIntegration/X11Fixture.cs') -Destination (Join-Path $consumer 'App/X11Fixture.cs')
Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Tests/Fixtures/Fonts/DejaVuSans.ttf') -Destination (Join-Path $consumer 'DejaVuSans.ttf')
Copy-Item -LiteralPath (Join-Path $repo 'src/Portability/Dependencies/TextUnicode/Licenses/DejaVu.txt') -Destination (Join-Path $consumer 'DejaVu.txt')
'<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.props') -Encoding utf8NoBOM
'<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.targets') -Encoding utf8NoBOM
'{"sdk":{"version":"10.0.401","rollForward":"disable"}}' | Set-Content (Join-Path $consumer 'global.json') -Encoding utf8NoBOM
$feed=Join-Path $distribution 'feed'
if($HostProfile -eq 'PrepareLinux') {
    if($feed -notmatch '^([A-Za-z]):[\\/](.*)$') { throw 'Linux preparation currently expects a WSL-mounted Windows workspace.' }
    $feed='/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\','/')
}
$escaped=[Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key=`"local-portable`" value=`"$escaped`"/></packageSources><packageSourceMapping><packageSource key=`"local-portable`"><package pattern=`"*`"/></packageSource></packageSourceMapping></configuration>" | Set-Content (Join-Path $consumer 'NuGet.Config') -Encoding utf8NoBOM
if($HostProfile -eq 'PrepareLinux') { Write-Output $consumer; return }
$env:NUGET_PACKAGES=Join-Path $consumer '.packages'
$env:DOTNET_CLI_HOME=Join-Path $consumer '.cli'
$env:MSBUILDDISABLENODEREUSE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $consumer
try {
    Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --disable-build-servers '-v:diag' *> build.log
    if($LASTEXITCODE -ne 0) { throw "External package consumer build failed: $consumer/build.log" }
    Copy-Item App/obj/Release/net10.0/compiler-origins.xml compiler-origins.xml
    Copy-Item Companion/obj/Release/net10.0/compiler-origins-control.xml compiler-origins-control.xml
    Invoke-PortableDotnet $Dotnet App/bin/Release/net10.0/App.dll > bin-results.json
    if($LASTEXITCODE -ne 0) { throw "External package consumer run failed: $consumer/bin-results.json" }
    Invoke-PortableDotnet $Dotnet publish App/App.csproj -c Release --self-contained false --disable-build-servers -o publish-fdd *> publish.log
    if($LASTEXITCODE -ne 0) { throw "External package consumer publish failed: $consumer/publish.log" }
    Invoke-PortableDotnet $Dotnet publish-fdd/App.dll > publish-results.json
    if($LASTEXITCODE -ne 0) { throw "External package consumer published run failed: $consumer/publish-results.json" }
    Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --disable-build-servers "-p:DirectoryBuildPropsPath=$consumer/CustomPaths.props" *> custom-build.log
    if($LASTEXITCODE -ne 0) { throw "External custom-path build failed: $consumer/custom-build.log" }
    Invoke-PortableDotnet $Dotnet custom-bin/App/Release/net10.0/App.dll > custom-results.json
    if($LASTEXITCODE -ne 0) { throw "External custom-path run failed: $consumer/custom-results.json" }
    $observed=@(Get-ChildItem custom-obj -Recurse -Filter '*.g.cs') + @(Get-Item custom-bin/App/Release/net10.0/App.dll,custom-bin/Companion/Release/net10.0/Companion.dll)
    $timestamps=@{}; foreach($file in $observed) {$timestamps[$file.FullName]=$file.LastWriteTimeUtc.Ticks}
    Invoke-PortableDotnet $Dotnet build App/App.csproj -c Release --disable-build-servers "-p:DirectoryBuildPropsPath=$consumer/CustomPaths.props" '-v:diag' *> incremental-build.log
    if($LASTEXITCODE -ne 0) { throw "External incremental build failed: $consumer/incremental-build.log" }
    foreach($file in $observed) { if((Get-Item $file.FullName).LastWriteTimeUtc.Ticks -ne $timestamps[$file.FullName]) { throw "Unchanged build rewrote $($file.FullName)" } }
    if(!(Select-String -Path incremental-build.log -SimpleMatch -Pattern 'Skipping target "CoreCompile" because all output files')) { throw 'Unchanged package consumer did not skip compilation.' }
    [ordered]@{probe='package-incremental';checks=@(@{name='unchanged-second-build';status='pass';detail='Generated sources and assemblies retained timestamps; CoreCompile skipped.'});files=$timestamps} | ConvertTo-Json -Depth 5 | Set-Content incremental-results.json
    Write-Output "External package consumer passed: $consumer"
} finally { Pop-Location }
