[CmdletBinding()]
param([ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._-]*$')][string] $OracleName = '')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$manifestFile = if ($OracleName) { "MicrosoftCompilerOracle.$OracleName.json" } else { 'MicrosoftCompilerOracle.json' }
$manifestPath = Join-Path $PSScriptRoot $manifestFile
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($OracleName -and $manifest.oracleName -cne $OracleName) { throw 'Named Microsoft oracle manifest does not match the requested name.' }
$checks = @(
    @{ Path = Join-Path $repositoryRoot $manifest.fixtureOriginalPath; Expected = $manifest.fixtureSha256 },
    @{ Path = Join-Path $repositoryRoot $manifest.fixtureProject; Expected = $manifest.fixtureProjectSha256 },
    @{ Path = Join-Path $repositoryRoot $manifest.generatedSource; Expected = $manifest.generatedSourceSha256 },
    @{ Path = $manifest.compilerOriginalPath; Expected = $manifest.compilerSha256 },
    @{ Path = $manifest.targetsOriginalPath; Expected = $manifest.targetsSha256 }
)
foreach ($check in $checks) {
    if ((Get-FileHash -LiteralPath $check.Path -Algorithm SHA256).Hash -ne $check.Expected) {
        throw "Frozen Microsoft compiler oracle provenance differs: $($check.Path)"
    }
}
$generated = Get-Content -LiteralPath (Join-Path $repositoryRoot $manifest.generatedSource) -Raw
if (-not $generated.Contains('GeneratedCodeAttribute("PresentationBuildTasks", "10.0.12.0")')) {
    throw 'Original generated source does not identify the installed Microsoft compiler.'
}
$archivePath = if ($OracleName) { "artifacts/portability/markup-runtime-integration/microsoft-oracles/$OracleName" } else { 'artifacts/portability/markup-runtime-integration/microsoft-oracle' }
$archive = Join-Path $repositoryRoot $archivePath
New-Item -ItemType Directory -Force -Path $archive | Out-Null
$binary = Join-Path $archive $manifest.fixtureAssembly
if (Test-Path -LiteralPath $binary) {
    if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $manifest.fixtureSha256) {
        throw 'Existing frozen Microsoft oracle archive differs. Preserve it for inspection.'
    }
} else {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $manifest.fixtureOriginalPath) -Destination $binary
}
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $manifest.fixtureSha256) {
    throw 'Frozen Microsoft oracle copy changed bytes.'
}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $archive 'origin.json')
[PSCustomObject]@{ status = 'pass'; assembly = $binary; sha256 = $manifest.fixtureSha256; compiler = $manifest.compilerIdentity } | ConvertTo-Json
