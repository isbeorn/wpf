[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$artifactRoot = Join-Path $repositoryRoot 'artifacts/portability/native-host'
$upstreamRoot = Join-Path $artifactRoot 'upstream'
$commit = '2d79ba55fae1b1e71c69f93a668080c9029e6a31'
$archive = Join-Path $artifactRoot "avalonia-$commit.tar.gz"
$expectedHash = '9E9EE6EE270BFDC02FBC34296DA51EE6242325156D2B67CBED92D2CCBC2E2029'
New-Item -ItemType Directory -Force $artifactRoot | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest "https://codeload.github.com/AvaloniaUI/Avalonia/tar.gz/$commit" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The pinned Avalonia source archive SHA-256 does not match.'
}
if (Test-Path -LiteralPath $upstreamRoot) {
    $resolvedUpstream = (Resolve-Path -LiteralPath $upstreamRoot).Path
    $expectedUpstream = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/portability/native-host/upstream'))
    if ($resolvedUpstream -ne $expectedUpstream) { throw 'Refusing to replace a source directory outside the generated upstream path.' }
    Remove-Item -LiteralPath $resolvedUpstream -Recurse -Force
}
New-Item -ItemType Directory -Force $upstreamRoot | Out-Null
tar -xzf $archive -C $upstreamRoot --strip-components 1
if ($LASTEXITCODE -ne 0) { throw 'The pinned source archive could not be extracted.' }
Write-Output "Prepared Avalonia $commit at $upstreamRoot (SHA-256 $expectedHash)."
