param([string]$Dotnet=(Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'))
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'scripts/Test-DownloadCounts.ps1')
foreach($taskProject in @('src/desktop/account-tests/Account.Tests.csproj','src/desktop/support-tests/Support.Tests.csproj')) {
    & $Dotnet run --project (Join-Path $PSScriptRoot $taskProject) -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
    if($LASTEXITCODE){throw "Feature checks failed: $taskProject"}
}
foreach($taskService in @('services/telemetry','services/support')) {
    Push-Location (Join-Path $PSScriptRoot $taskService)
    try { & npm.cmd test; if($LASTEXITCODE){throw "Worker checks failed: $taskService"} }
    finally { Pop-Location }
}
