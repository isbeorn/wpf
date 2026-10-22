param(
    [Parameter(Mandatory)][string] $Stock,
    [Parameter(Mandatory)][string] $Portable,
    [Parameter(Mandatory)][string] $NativeDirectory,
    [Parameter(Mandatory)][string] $NinaSourceDirectory,
    [Parameter(Mandatory)][string] $Report
)
$ErrorActionPreference = 'Stop'
$stockResult = Get-Content $Stock -Raw | ConvertFrom-Json -AsHashtable
$portableResult = Get-Content $Portable -Raw | ConvertFrom-Json -AsHashtable
if ($stockResult.status -cne 'pass' -or $portableResult.status -cne 'pass') { throw 'A calculation run failed.' }
if ($stockResult.values.Count -ne 94 -or $portableResult.values.Count -ne 94) { throw 'Calculation coverage changed.' }
$maxDelta = 0.0
foreach ($key in $stockResult.values.Keys) {
    if (!$portableResult.values.Contains($key)) { throw "Missing value $key" }
    $delta = [Math]::Abs($stockResult.values[$key] - $portableResult.values[$key])
    if (![double]::IsFinite($delta) -or $delta -gt 1e-10) { throw "Stock numerical mismatch: $key" }
    $maxDelta = [Math]::Max($maxDelta, $delta)
}
$nativeLayout = @{}
foreach ($line in Get-Content "$NativeDirectory/native-abi.log") {
    $pair = $line.Split('=')
    $nativeLayout.Add($pair[0], [int]$pair[1])
}
if ($nativeLayout.Count -ne 66 -or $portableResult.layout.Count -ne 66 -or $stockResult.layout.Count -ne 66) { throw 'Structure coverage changed.' }
foreach ($key in $nativeLayout.Keys) {
    if (!$portableResult.layout.Contains($key) -or $nativeLayout[$key] -ne $portableResult.layout[$key] -or $stockResult.layout[$key] -ne $nativeLayout[$key]) { throw "Native/managed/stock structure mismatch: $key" }
}
if ((Get-Content "$NativeDirectory/sofa-tests.log" -Raw).Trim() -cne 't_sofa_c validation successful') { throw 'Original SOFA validation failed.' }
$original = (Get-Content "$NinaSourceDirectory/NOVAS31/NOVAS31/checkout-stars-usno.txt" -Raw).Replace("`r", '').Trim()
$actual = (Get-Content "$NativeDirectory/novas-original-checkout.log" -Raw).Replace("`r", '').Trim()
if ($original -cne $actual) { throw 'Original USNO solsys3 checkout differs.' }
$fullOriginal = (Get-Content "$NinaSourceDirectory/NOVAS31/NOVAS31/checkout-stars-full-usno.txt" -Raw).Replace("`r", '').Trim()
$fullActual = (Get-Content "$NativeDirectory/novas-full-checkout.log" -Raw).Replace("`r", '').Trim()
$fullExpectedValues = @([regex]::Matches($fullOriginal, '(?:RA|Dec) =\s*(-?[0-9.]+)') | ForEach-Object { [double]::Parse($_.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) })
$fullActualValues = @([regex]::Matches($fullActual, '(?:RA|Dec) =\s*(-?[0-9.]+)') | ForEach-Object { [double]::Parse($_.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) })
if ($fullExpectedValues.Count -ne 24 -or $fullActualValues.Count -ne 24) { throw 'USNO full checkout sample count differs.' }
$fullDelta = 0.0
for ($index = 0; $index -lt 24; $index++) {
    $delta = [Math]::Abs($fullExpectedValues[$index] - $fullActualValues[$index])
    if ($delta -gt 1e-8) { throw 'USNO DE421/DE405 coordinate difference exceeds printed-precision bound.' }
    $fullDelta = [Math]::Max($fullDelta, $delta)
}
foreach ($pair in @(@('valid', 0), @('missing', 1), @('truncated', 2))) {
    if ((Get-Content "$NativeDirectory/ephem-$($pair[0]).log" -Raw) -notmatch "(?m)^open=$($pair[1])$") { throw "Wrong ephemeris result $($pair[0])" }
}
if ((Get-Content "$NativeDirectory/ephem-valid.log" -Raw) -notmatch '(?m)^close=0$') { throw 'Genuine ephemeris close failed.' }
$exports = @{}
foreach ($name in @('SOFA', 'NOVAS')) {
    $source = Get-Content "$NinaSourceDirectory/NINA.Astrometry/$name.cs" -Raw
    $available = Get-Content "$NativeDirectory/$($name.ToLowerInvariant())-exports.txt" -Raw
    $entries = @([regex]::Matches($source, 'EntryPoint = "([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($entry in $entries) {
        $exists = $available -match "(?m)\b$([regex]::Escape($entry))$"
        if (!$exists -and $entry -cne 'set_racio_file') { throw "Missing genuine export $entry" }
        $exports[$entry] = if ($exists) { 'present' } else { 'absent-in-original-unused-not-implemented' }
    }
}
[ordered]@{ status = 'pass'; numericalValues = 94; tolerance = 1e-10; maximumAbsoluteDelta = $maxDelta; nativeManagedStockLayoutEntries = 66; sofaOriginalValidation = 'pass'; novasOriginalCheckout = 'exact-text-match'; novasFullCheckout = '24-coordinate-values-within-1e-8-DE421-vs-reference-DE405'; novasFullMaximumDelta = $fullDelta; nativeExports = $exports; ephemerisOpenCloseAndErrors = 'pass'; scope = 'Public genuine calculation path only; no App, MainWindow, real workflow view model or simulator camera executed.' } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
