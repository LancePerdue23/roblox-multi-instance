param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'), [switch]$VerifyArchive)
$ErrorActionPreference = 'Stop'
if ($VerifyArchive) { & (Join-Path $PSScriptRoot 'VerifyArchive.ps1') }
& (Join-Path $PSScriptRoot 'src/desktop/Test.ps1') -Dotnet $Dotnet
