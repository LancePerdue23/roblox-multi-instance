$ErrorActionPreference = 'Stop'
$downloadTestRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.build'))
$downloadTestDirectory = [IO.Path]::GetFullPath((Join-Path $downloadTestRoot ('download-metrics-test-' + [Guid]::NewGuid().ToString('N'))))
if (-not $downloadTestDirectory.StartsWith($downloadTestRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path escaped build directory.' }
New-Item -ItemType Directory -Path $downloadTestDirectory -Force | Out-Null
try {
    $downloadFixture = Join-Path $downloadTestDirectory 'releases.json'
    $downloadSnapshot = Join-Path $downloadTestDirectory 'snapshots.jsonl'
    @(
        @{tag_name='v1.0.0';draft=$false;prerelease=$false;assets=@(@{id=1;name='RobloxMultiInstance-Setup.exe';download_count=10},@{id=2;name='SHA256SUMS.txt';download_count=900},@{id=3;name='RobloxMultiInstance-Portable.zip';download_count=4})},
        @{tag_name='helper-v1.0.0';draft=$false;prerelease=$true;assets=@(@{id=4;name='RobloxLaunchHelper-Setup.exe';download_count=3},@{id=1;name='RobloxMultiInstance-Setup.exe';download_count=9})},
        @{tag_name='draft';draft=$true;prerelease=$false;assets=@(@{id=5;name='RobloxMultiInstance-Setup.exe';download_count=500})}
    ) | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $downloadFixture
    $downloadResult = & (Join-Path $PSScriptRoot 'Get-DownloadCounts.ps1') -ReleaseFixture $downloadFixture -SnapshotPath $downloadSnapshot
    if ($downloadResult.Total -ne 17 -or $downloadResult.Assets.Count -ne 3) { throw 'Binary allowlist, prerelease or asset deduplication failed.' }
    $null = & (Join-Path $PSScriptRoot 'Get-DownloadCounts.ps1') -ReleaseFixture $downloadFixture -SnapshotPath $downloadSnapshot
    if (@(Get-Content -LiteralPath $downloadSnapshot).Count -ne 2) { throw 'Historical snapshots were replaced.' }
    Write-Output 'PASS Download metrics filter binaries, ignore drafts/checksums, deduplicate assets and append snapshots.'
}
finally {
    $downloadResolvedTarget = [IO.Path]::GetFullPath($downloadTestDirectory)
    if (-not $downloadResolvedTarget.StartsWith($downloadTestRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside build directory.' }
    Remove-Item -LiteralPath $downloadResolvedTarget -Recurse -Force
}
