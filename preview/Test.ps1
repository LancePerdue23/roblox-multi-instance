param([string]$Dotnet = (Join-Path $PSScriptRoot '..\.build\sdk\dotnet.exe'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'VerifyPreservation.ps1')
& $Dotnet run --project (Join-Path $PSScriptRoot 'host-tests\BridgeTests.csproj') -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Bridge boundary checks failed.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'backend\Tests\Backend.Tests.csproj') -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Backend checks failed.' }
& (Join-Path $PSScriptRoot 'Contracts.ps1') -Dotnet $Dotnet -NoBuild
Push-Location (Join-Path $PSScriptRoot 'web')
try {
    & npm.cmd run build; if ($LASTEXITCODE) { throw 'Frontend type/build checks failed.' }
    & npm.cmd test; if ($LASTEXITCODE) { throw 'Frontend bridge tests failed.' }
    & npm.cmd run test:ui; if ($LASTEXITCODE) { throw 'Frontend interaction tests failed.' }
} finally { Pop-Location }
