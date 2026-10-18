[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$directory=Join-Path $repo 'artifacts/portability/windows-core-source'
$archive=Join-Path $directory 'winforms-3c92f9f1cc1dcc74779795d518c67aa80f472df7.zip'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
if(!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://codeload.github.com/dotnet/winforms/zip/3c92f9f1cc1dcc74779795d518c67aa80f472df7' -OutFile $archive
}
if((Get-FileHash -LiteralPath $archive).Hash -ne '6964320B93BBE473A5775F20D187159981F47BA2C5FA034EE7996ACC72B3A24E') {
    throw "Pinned source archive hash mismatch: $archive"
}
Write-Output $archive
