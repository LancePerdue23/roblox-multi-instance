[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'LancePerdue23/roblox-multi-instance',
    [string]$SnapshotPath,
    [string]$ReleaseFixture
)
$ErrorActionPreference = 'Stop'
$downloadBinaryNames = @('RobloxMultiInstance-Setup.exe', 'RobloxLaunchHelper-Setup.exe', 'RobloxMultiInstance-Portable.zip')
if ($ReleaseFixture) {
    $downloadReleases = @(Get-Content -LiteralPath $ReleaseFixture -Raw | ConvertFrom-Json)
}
else {
    $downloadHeaders = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'RMI-owner-download-counts' }
    # Authentication stays in the owner's shell; never write it to output or snapshots.
    $downloadToken = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }
    if ($downloadToken) { $downloadHeaders.Authorization = 'Bearer ' + $downloadToken }
    $downloadReleases = @()
    $downloadPage = 1
    do {
        $downloadPageRows = @(Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$downloadPage" -Headers $downloadHeaders)
        $downloadReleases += $downloadPageRows
        $downloadPage++
    } while ($downloadPageRows.Count -eq 100)
}
$downloadRows = @(
    foreach ($downloadRelease in $downloadReleases) {
        if ($downloadRelease.draft) { continue }
        foreach ($downloadAsset in $downloadRelease.assets) {
            if ($downloadAsset.name -cnotin $downloadBinaryNames) { continue }
            if ($null -eq $downloadAsset.id -or $downloadAsset.download_count -notmatch '^\d+$') { throw 'GitHub returned an invalid asset count.' }
            [pscustomobject]@{ AssetId = [long]$downloadAsset.id; Release = [string]$downloadRelease.tag_name; Prerelease = [bool]$downloadRelease.prerelease; Name = [string]$downloadAsset.name; Downloads = [long]$downloadAsset.download_count }
        }
    }
)
# Avoid double-counting an asset if releases changed while paginating.
$downloadUnique = @($downloadRows | Group-Object AssetId | ForEach-Object { $_.Group | Sort-Object Downloads -Descending | Select-Object -First 1 })
$downloadTotal = ($downloadUnique | Measure-Object -Property Downloads -Sum).Sum
if ($null -eq $downloadTotal) { $downloadTotal = 0 }
$downloadResult = [pscustomobject]@{
    ObservedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Repository = $Repository
    Metric = 'Binary asset download requests; includes repeat downloads, updates and published prereleases. Not unique people or installations.'
    Total = [long]$downloadTotal
    Assets = @($downloadUnique | Sort-Object Release, Name)
}
if ($SnapshotPath) {
    $downloadDestination = [IO.Path]::GetFullPath($SnapshotPath)
    if (-not [IO.Directory]::Exists([IO.Path]::GetDirectoryName($downloadDestination))) { throw 'Snapshot parent directory must already exist.' }
    # Append each observation; removed/replaced assets do not erase previous observations.
    Add-Content -LiteralPath $downloadDestination -Value ($downloadResult | ConvertTo-Json -Depth 6 -Compress) -Encoding utf8
}
$downloadResult
