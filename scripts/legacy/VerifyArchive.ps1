param([string]$Archive = (Join-Path (Join-Path $PSScriptRoot '../..') 'archive/legacy-winforms'))
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
foreach ($taskEntry in (Get-Content -LiteralPath (Join-Path $Archive 'sha256.json') -Raw | ConvertFrom-Json)) {
    if ([IO.Path]::GetFileName($taskEntry.Name) -ne $taskEntry.Name) { throw 'Invalid archive manifest path.' }
    if ((Get-FileHash -LiteralPath (Join-Path $Archive $taskEntry.Name) -Algorithm SHA256).Hash -ne $taskEntry.Hash) { throw "Archive integrity failure: $($taskEntry.Name)" }
}
Write-Output 'PASS: frozen WinForms archive hashes match.'
