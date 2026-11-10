param([string]$Dotnet=(Join-Path (Join-Path $PSScriptRoot '../..') '.build/sdk/dotnet.exe'))
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference='Stop'
& (Join-Path $taskRepositoryRoot 'scripts/Test-DownloadCounts.ps1')
foreach($taskProject in @('src/desktop/account-tests/Account.Tests.csproj','src/desktop/support-tests/Support.Tests.csproj')) {
    & $Dotnet run --project (Join-Path $taskRepositoryRoot $taskProject) -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
    if($LASTEXITCODE){throw "Feature checks failed: $taskProject"}
}
foreach($taskService in @('services/telemetry','services/support')) {
    Push-Location (Join-Path $taskRepositoryRoot $taskService)
    try { & npm.cmd test; if($LASTEXITCODE){throw "Worker checks failed: $taskService"} }
    finally { Pop-Location }
}
