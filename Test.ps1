param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'), [switch]$VerifyArchive)
$ErrorActionPreference = 'Stop'
$taskOriginalHome=$env:DOTNET_CLI_HOME
$taskOriginalPackages=$env:NUGET_PACKAGES
try {
    $env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.build/home'
    $env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.build/packages'
    if ($VerifyArchive) { & (Join-Path $PSScriptRoot 'scripts/legacy/VerifyArchive.ps1') }
    & (Join-Path $PSScriptRoot 'src/desktop/Test.ps1') -Dotnet $Dotnet
    & (Join-Path $PSScriptRoot 'scripts/testing/Test-Features.ps1') -Dotnet $Dotnet
} finally { $env:DOTNET_CLI_HOME=$taskOriginalHome;$env:NUGET_PACKAGES=$taskOriginalPackages }
