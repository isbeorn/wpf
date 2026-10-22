[CmdletBinding()]
param(
    [string] $EvidenceDirectory = (Join-Path $PSScriptRoot "../../artifacts/portability/sdk-guard-tests/$([Guid]::NewGuid().ToString('N'))")
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This guard check uses Windows native processes and registry reads.' }
. (Join-Path $PSScriptRoot 'Invoke-PortableDotnet.ps1') -PathEvidenceDirectory $EvidenceDirectory
$savedAdd = [Environment]::GetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'Process')
$savedCertificate = [Environment]::GetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'Process')
$before = Get-PortableSdkPersistentPath | ConvertTo-Json -Depth 6 -Compress
$checks = [Collections.Generic.List[string]]::new()
try {
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'caller-add'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'caller-certificate'
    $expected = & $env:ComSpec /d /c echo -p:Configuration=Release -nr:false -m:1 -getProperty:TargetPath
    $actual = Invoke-PortableDotnet $env:ComSpec /d /c echo '-p:Configuration=Release' '-nr:false' '-m:1' '-getProperty:TargetPath'
    if ($actual -cne $expected) { throw "Native arguments changed: expected '$expected', observed '$actual'." }
    $checks.Add('quoted-colon-arguments-match-native-call')
    $arguments = @('/d', '/c', 'echo', '-p:Configuration=Release', '-nr:false', '-m:1', '-getProperty:TargetPath')
    if ((Invoke-PortableDotnet $env:ComSpec @arguments) -cne $expected) { throw 'Splatted native arguments changed.' }
    $checks.Add('splatted-colon-arguments-match-native-call')
    $inherited = Invoke-PortableDotnet $env:ComSpec /d /c 'echo %DOTNET_ADD_GLOBAL_TOOLS_TO_PATH%/%DOTNET_GENERATE_ASPNET_CERTIFICATE%'
    if ($inherited -cne 'false/false') { throw "Child did not inherit both opt-outs: $inherited" }
    $checks.Add('child-inherits-both-opt-outs')
    $LASTEXITCODE = 91
    Invoke-PortableDotnet $env:ComSpec /d /c 'exit /b 7'
    if ($LASTEXITCODE -ne 7) { throw 'Caller-local native exit status stayed stale.' }
    $checks.Add('nonzero-exit-replaces-caller-local-status')
    $PSNativeCommandUseErrorActionPreference = $true
    $caught = $false
    try { Invoke-PortableDotnet $env:ComSpec /d /c 'exit /b 8' }
    catch { $caught = $_.Exception -is [Management.Automation.NativeCommandExitException] }
    if (!$caught -or $LASTEXITCODE -ne 8) { throw 'Native error preference or exit status changed.' }
    $PSNativeCommandUseErrorActionPreference = $false
    $checks.Add('native-error-preference-preserved')
    $stderr = Join-Path $EvidenceDirectory 'stderr.txt'
    $stdout = Invoke-PortableDotnet $env:ComSpec /d /c 'echo guard-output & echo guard-error 1>&2' 2> $stderr
    if ($stdout.Trim() -cne 'guard-output' -or (Get-Content -LiteralPath $stderr -Raw).Trim() -cne 'guard-error') { throw 'Native stream redirection changed.' }
    $checks.Add('native-streams-remain-separate')
    $caught = $false
    try { Invoke-PortableDotnet (Join-Path $EvidenceDirectory 'does-not-exist.exe') }
    catch { $caught = $true }
    if (!$caught) { throw 'Missing executable did not fail.' }
    if ($env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH -cne 'caller-add' -or $env:DOTNET_GENERATE_ASPNET_CERTIFICATE -cne 'caller-certificate') { throw 'Caller process settings were not restored.' }
    $checks.Add('process-settings-restored-after-failures')
    $records = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter '*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    if ($records.Count -ne 7 -or @($records | Where-Object { $null -eq $_.after -or $_.changedScopes.Count -ne 0 }).Count) { throw 'PATH after-check missing or PATH drifted.' }
    if ($before -cne (Get-PortableSdkPersistentPath | ConvertTo-Json -Depth 6 -Compress)) { throw 'Persistent PATH changed during guard checks.' }
    $checks.Add('raw-path-unchanged-and-after-checks-run-on-failures')
} finally {
    [Environment]::SetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', $savedAdd, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', $savedCertificate, 'Process')
}
@{ status = 'pass'; sdkInvoked = $false; checks = @($checks) } | ConvertTo-Json -Depth 5
