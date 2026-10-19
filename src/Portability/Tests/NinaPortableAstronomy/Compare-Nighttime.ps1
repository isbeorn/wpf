param(
    [Parameter(Mandatory)][string] $Stock,
    [Parameter(Mandatory)][string] $Portable,
    [Parameter(Mandatory)][string] $AdaptedWindows,
    [Parameter(Mandatory)][string] $Red,
    [Parameter(Mandatory)][string] $PortableLog,
    [Parameter(Mandatory)][string] $Report
)
$ErrorActionPreference = 'Stop'
$stockResult = Get-Content $Stock -Raw | ConvertFrom-Json -AsHashtable
$portableResult = Get-Content $Portable -Raw | ConvertFrom-Json -AsHashtable
$windowsResult = Get-Content $AdaptedWindows -Raw | ConvertFrom-Json -AsHashtable
$redResult = Get-Content $Red -Raw | ConvertFrom-Json -AsHashtable
if ($redResult.status -cne 'fail' -or $redResult.phase -cne 'actual-database-path-containment' -or $redResult.values.Count -ne 36) { throw 'The original-path negative control did not reach the genuine calculation/path failure.' }
$deltas = foreach ($result in @($portableResult, $windowsResult)) {
    if ($stockResult.status -cne 'pass' -or $result.status -cne 'pass' -or $stockResult.values.Count -ne 36 -or $result.values.Count -ne 36) { throw 'Genuine nighttime calculation coverage differs.' }
    if ($result.profileType -cne 'NINA.Profile.Profile' -or $result.calculatorType -cne 'NINA.Astrometry.NighttimeCalculator') { throw 'Original concrete profile and calculator required.' }
    if ($result.databaseSha256After -cne '4DD072B013F015C3E4460B0E6AFF29538DE2774331293726F81E8DF14E2DDAAC') { throw 'The original covered Earth-rotation database changed.' }
    $maximum = 0.0
    foreach ($key in $stockResult.values.Keys) {
        if (!$result.values.Contains($key)) { throw "Missing genuine result $key" }
        $delta = [Math]::Abs($stockResult.values[$key] - $result.values[$key])
        if (![double]::IsFinite($delta) -or $delta -gt 1e-10) { throw "Original nighttime mismatch $key" }
        $maximum = [Math]::Max($maximum, $delta)
    }
    $maximum
}
if ((Get-Content $PortableLog -Raw) -notlike '*holding the nearest UT1-TAI sample constant*') { throw 'Original stale-EOP warning was not observed.' }
[ordered]@{ status = 'pass'; values = 36; tolerance = 1e-10; linuxMaxDelta = $deltas[0]; adaptedWindowsMaxDelta = $deltas[1]; redPhase = $redResult.phase; redDefaultDatabase = $redResult.defaultDataSource; greenDefaultDatabase = $portableResult.defaultDataSource; genuineDatabaseSha256 = $portableResult.databaseSha256After; coveredReferenceDates = @('2024-10-01', '2024-10-02'); staleSample = '2026-07-28 retains original held-nearest UT1-TAI warning contract'; scope = $portableResult.scope } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8
