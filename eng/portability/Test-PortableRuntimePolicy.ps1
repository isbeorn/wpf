[CmdletBinding()]
param([Parameter(Mandatory)][string] $DistributionDirectory,[string] $Dotnet='dotnet')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$distribution=[IO.Path]::GetFullPath($DistributionDirectory)
$root=Join-Path $distribution 'runtime-policy'
if(Test-Path $root) {throw "Runtime policy verification requires a fresh directory: $root"}
$feed=Join-Path $root 'feed'
New-Item -ItemType Directory -Path $feed | Out-Null
Copy-Item -Path (Join-Path $distribution 'feed/*.nupkg') -Destination $feed
'<Project />' | Set-Content (Join-Path $root 'Directory.Build.props')
'<Project />' | Set-Content (Join-Path $root 'Directory.Build.targets')
'{"sdk":{"version":"10.0.401","rollForward":"disable"}}' | Set-Content (Join-Path $root 'global.json')
$escaped=[Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key=`"local`" value=`"$escaped`"/></packageSources></configuration>" | Set-Content (Join-Path $root 'NuGet.Config')
$env:NUGET_PACKAGES=Join-Path $root '.packages';$env:DOTNET_CLI_HOME=Join-Path $root '.cli'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false';$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$bridge=Join-Path $root 'Bridge';New-Item -ItemType Directory -Path $bridge | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>PortablePolicy.Bridge</PackageId><Version>1.0.0</Version></PropertyGroup>
 <ItemGroup><PackageReference Include="Wpf.Portable.Runtime" Version="[10.0.0-portable.1]" /></ItemGroup>
</Project>
'@ | Set-Content (Join-Path $bridge 'Bridge.csproj')
@'
namespace PortablePolicy;
public static class Bridge
{
    private static readonly System.Windows.DependencyProperty Property=System.Windows.DependencyProperty.RegisterAttached(
        "Value",typeof(int),typeof(Bridge),new System.Windows.PropertyMetadata(1));
    public static int RoundTrip()
    {
        var value=new System.Windows.DependencyObject();value.SetValue(Property,7);value.SetValue(Property,2);
        return (int)value.GetValue(Property);
    }
}
'@ | Set-Content (Join-Path $bridge 'Bridge.cs')
$checks=[Collections.Generic.List[object]]::new()
Push-Location $root
try {
    & $Dotnet pack Bridge/Bridge.csproj -c Release --disable-build-servers -o $feed *> bridge-pack.log
    if($LASTEXITCODE -ne 0) {throw "Real transitive package fixture failed: $root/bridge-pack.log"}
    foreach($mode in @('direct','transitive')) {
        $directory=Join-Path $root $mode;New-Item -ItemType Directory -Path $directory | Out-Null
        $package=if($mode -eq 'direct') {'Wpf.Portable.Runtime'} else {'PortablePolicy.Bridge'}
        $version=if($mode -eq 'direct') {'10.0.0-portable.1'} else {'1.0.0'}
        "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include=`"$package`" Version=`"[$version]`" /></ItemGroup></Project>" | Set-Content (Join-Path $directory 'App.csproj')
        @'
using System.Windows;
var property=DependencyProperty.RegisterAttached("PackageValue",typeof(int),typeof(DependencyObject),new PropertyMetadata(4));
var value=new DependencyObject();value.SetValue(property,9);value.SetValue(property,3);
if((int)value.GetValue(property)!=3) return 1;
if(File.Exists(Path.Combine(AppContext.BaseDirectory,"Bridge.dll")))
{
    var bridge=System.Reflection.Assembly.Load("Bridge");
    if((int)bridge.GetType("PortablePolicy.Bridge")!.GetMethod("RoundTrip")!.Invoke(null,null)!=2) return 2;
}
Console.WriteLine(typeof(DependencyObject).Assembly.FullName);
return 0;
'@ | Set-Content (Join-Path $directory 'Program.cs')
        $project=Join-Path $directory 'App.csproj'
        & $Dotnet build $project -c Release --disable-build-servers *> "$mode-build.log"
        if($LASTEXITCODE -ne 0) {throw "Allowed $mode runtime-only consumer failed: $root/$mode-build.log"}
        & $Dotnet (Join-Path $directory 'bin/Release/net10.0/App.dll') > "$mode-run.txt"
        if($LASTEXITCODE -ne 0) {throw "Allowed $mode runtime-only consumer could not execute real dependency properties."}
        $checks.Add(@{name="$mode-allowed-empty-rid";status='pass';detail='Ordinary Microsoft.NET.Sdk consumer built and ran real dependency property updates in both directions.'})
        foreach($invalid in @(
            @{name='rid';argument='-p:RuntimeIdentifier=linux-arm64';diagnostic='only an empty RID or linux-x64'},
            @{name='tfm';argument='-p:TargetFramework=net9.0';diagnostic='qualified only for net10.0'},
            @{name='desktop';argument='-p:UseWPF=true';diagnostic='Remove UseWPF=true'},
            @{name='trim';argument='-p:PublishTrimmed=true';diagnostic='Trimming, NativeAOT and single-file'},
            @{name='aot';argument='-p:PublishAot=true';diagnostic='Trimming, NativeAOT and single-file'},
            @{name='single-file';argument='-p:PublishSingleFile=true';diagnostic='Trimming, NativeAOT and single-file'})) {
            $log="$mode-reject-$($invalid.name).log"
            & $Dotnet build $project -c Release --no-restore --disable-build-servers $invalid.argument *> $log
            if($LASTEXITCODE -eq 0 -or !(Select-String -Path $log -SimpleMatch -Pattern $invalid.diagnostic)) {throw "Runtime policy did not reject $mode $($invalid.name): $root/$log"}
            $checks.Add(@{name="$mode-reject-$($invalid.name)";status='pass';detail='The real ordinary SDK build failed with the shared portable profile diagnostic.'})
        }
    }
    @{probe='portable-runtime-direct-and-transitive-policy';checks=$checks}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $distribution 'runtime-policy-results.json')
} finally {Pop-Location}
