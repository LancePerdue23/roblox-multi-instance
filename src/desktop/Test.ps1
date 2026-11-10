param([string]$Dotnet = (Join-Path $PSScriptRoot '..\..\.build\sdk\dotnet.exe'), [switch]$VerifyArchive)
$ErrorActionPreference = 'Stop'
if ($VerifyArchive) { & (Join-Path $PSScriptRoot '../../scripts/legacy/VerifyArchive.ps1') }
& $Dotnet run --project (Join-Path $PSScriptRoot 'host-tests\BridgeTests.csproj') -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Bridge boundary checks failed.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'shortcut-tests\Shortcut.Tests.csproj') -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Shortcut lifecycle checks failed.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'preview-tests\Preview.Tests.csproj') -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Preview lifecycle checks failed.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'backend\Tests\Backend.Tests.csproj') -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE) { throw 'Backend checks failed.' }
& (Join-Path $PSScriptRoot 'Contracts.ps1') -Dotnet $Dotnet -NoBuild
Push-Location (Join-Path $PSScriptRoot 'web')
try {
    & npm.cmd run build; if ($LASTEXITCODE) { throw 'Frontend type/build checks failed.' }
    & npm.cmd test; if ($LASTEXITCODE) { throw 'Frontend bridge tests failed.' }
    & npm.cmd run test:ui; if ($LASTEXITCODE) { throw 'Frontend interaction tests failed.' }
} finally { Pop-Location }
