# Compatibility alias: shared services only. Retired UI tests require Test-ArchivedUI.ps1.
param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'), [switch]$Integration)
& (Join-Path $PSScriptRoot 'Test-Shared.ps1') -Dotnet $Dotnet -Integration:$Integration
