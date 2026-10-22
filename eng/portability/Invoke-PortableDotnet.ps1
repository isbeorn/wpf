param(
    [string] $PathEvidenceDirectory = (Join-Path $PSScriptRoot '../../artifacts/portability/sdk-path-guard')
)

$script:PortableSdkPathEvidenceDirectory = [IO.Path]::GetFullPath($PathEvidenceDirectory)
$script:PortableSdkPathDriftDetected = $false

function Get-PortableSdkPersistentPath {
    $paths = [ordered]@{}
    foreach ($scope in 'User', 'Machine') {
        $root = if ($scope -eq 'User') { [Microsoft.Win32.Registry]::CurrentUser } else { [Microsoft.Win32.Registry]::LocalMachine }
        $subkey = if ($scope -eq 'User') { 'Environment' } else { 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment' }
        $key = $root.OpenSubKey($subkey, $false)
        try {
            $exists = $null -ne $key -and $key.GetValueNames() -contains 'Path'
            $paths[$scope] = [ordered]@{
                exists = $exists
                kind = if ($exists) { $key.GetValueKind('Path').ToString() } else { $null }
                value = if ($exists) { $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { $null }
            }
        } finally {
            if ($null -ne $key) { $key.Dispose() }
        }
    }
    return $paths
}

function Invoke-PortableDotnet {
    # Callers must quote colon-bearing arguments to prevent PowerShell function argument splitting.
    $executable = $args[0]
    $arguments = @($args | Select-Object -Skip 1)
    if ($script:PortableSdkPathDriftDetected) { throw 'Persistent PATH changed during an earlier SDK invocation. Stop and inspect its PATH evidence before continuing.' }
    $savedAddTools = [Environment]::GetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'Process')
    $savedCertificate = [Environment]::GetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'Process')
    $windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $before = $null
    $evidence = $null
    try {
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        if ($windowsHost) {
            $before = Get-PortableSdkPersistentPath
            [IO.Directory]::CreateDirectory($script:PortableSdkPathEvidenceDirectory) | Out-Null
            $evidence = Join-Path $script:PortableSdkPathEvidenceDirectory ("$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))-$([Guid]::NewGuid().ToString('N')).json")
            [ordered]@{ executable = [string] $executable; before = $before; after = $null } |
                ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidence -Encoding utf8NoBOM
        }
        try {
            & $executable @arguments
        } finally {
            $nativeStatus = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
            if ($null -ne $nativeStatus) { Set-Variable -Name LASTEXITCODE -Value $nativeStatus.Value -Scope 1 }
        }
    } finally {
        try {
            if ($null -ne $evidence) {
                $after = Get-PortableSdkPersistentPath
                $changed = @('User', 'Machine' | Where-Object {
                    ($before[$_] | ConvertTo-Json -Compress) -cne ($after[$_] | ConvertTo-Json -Compress)
                })
                $script:PortableSdkPathDriftDetected = $changed.Count -ne 0
                [ordered]@{ executable = [string] $executable; before = $before; after = $after; changedScopes = $changed } |
                    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidence -Encoding utf8NoBOM
                if ($script:PortableSdkPathDriftDetected) {
                    throw "Persistent PATH drift detected ($($changed -join ', ')). Stop further execution. Exact raw before/after values: $evidence"
                }
            }
        } finally {
            [Environment]::SetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', $savedAddTools, 'Process')
            [Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', $savedCertificate, 'Process')
        }
    }
}
