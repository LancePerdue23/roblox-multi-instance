# Compatibility alias: shared services only. Retired UI tests require scripts/legacy/Test-ArchivedUI.ps1.
param([string]$Dotnet = (Join-Path (Join-Path $PSScriptRoot '../..') '.build/sdk/dotnet.exe'), [switch]$Integration)
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& (Join-Path $taskRepositoryRoot 'Test-Shared.ps1') -Dotnet $Dotnet -Integration:$Integration
