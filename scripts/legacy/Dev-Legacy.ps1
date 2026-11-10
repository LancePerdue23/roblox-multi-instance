param([string]$Dotnet = 'dotnet')
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
$taskRoot = Join-Path $taskRepositoryRoot '.build'
$taskDev = Join-Path $taskRoot 'dev'
$taskBackups = Join-Path $taskRoot 'dev-backups'
$taskStage = Join-Path $taskRoot 'dev-staging'
$taskExe = Join-Path $taskDev 'RobloxLaunchHelper.exe'
# Build first so a compiler/publish failure never disturbs the current development EXE.
& (Join-Path $taskRepositoryRoot 'scripts/legacy/Build-Legacy.ps1') -Dotnet $Dotnet -Output $taskStage
if (Test-Path -LiteralPath $taskExe) {
    # Opening exclusively detects a running EXE before creating a backup or replacing it.
    try { $taskLock = [IO.File]::Open($taskExe, 'Open', 'ReadWrite', 'None'); $taskLock.Dispose() }
    catch { throw 'Close the development app (including its tray process), then run Dev.ps1 again.' }
    $taskBackup = Join-Path $taskBackups ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
    Copy-Item -LiteralPath $taskExe -Destination $taskBackup
}
New-Item -ItemType Directory -Path $taskDev -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskStage 'RobloxLaunchHelper.exe') -Destination $taskExe -Force
# Retain the immediately previous version plus three older versions.
if (Test-Path -LiteralPath $taskBackups) {
    Get-ChildItem -LiteralPath $taskBackups -Directory | Sort-Object Name -Descending | Select-Object -Skip 4 | ForEach-Object {
        $taskTarget = [IO.Path]::GetFullPath($_.FullName)
        if ([IO.Path]::GetDirectoryName($taskTarget) -ne [IO.Path]::GetFullPath($taskBackups) -or
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected backup path; cleanup stopped.' }
        Remove-Item -LiteralPath $taskTarget -Recurse -Force
    }
}
Write-Host "Development EXE: $taskExe"
