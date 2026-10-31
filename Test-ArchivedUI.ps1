# Explicit historical runner; never called by Test.ps1, Test-Shared.ps1 or Audit.ps1.
param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'))
$ErrorActionPreference = 'Stop'
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests/retired-ui/RobloxLaunchHelper.Tests.csproj') -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Archived historical tests failed.' }
