param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
$report = Get-Content -LiteralPath (Join-Path $RunDirectory 'report.json') -Raw | ConvertFrom-Json
function Encode($Value) { [Net.WebUtility]::HtmlEncode([string]$Value) }
$summary = 'Revision: {0} | Uncommitted changes: {1} | Helper: {2} | Seed: {3} | Soak: {4} seconds' -f $report.build.revision,$report.build.dirty,$report.build.helperVersion,$report.build.seed,$report.build.soakSeconds
$counts = ($report.results | Group-Object outcome | ForEach-Object { '{0}: {1}' -f $_.Name,$_.Count }) -join ' · '
$stressSummary = ''
$metricsPath = Join-Path $RunDirectory 'soak-metrics.json'
if (Test-Path -LiteralPath $metricsPath) {
    $metrics = Get-Content -LiteralPath $metricsPath -Raw | ConvertFrom-Json
    $heap = if ($null -ne $metrics.heapGrowthBytes) { '{0:N1} MiB' -f ($metrics.heapGrowthBytes / 1MB) } else { 'unavailable' }
    $stressSummary = '<p>Soak: {0} seconds · {1} iterations · workflow p95: {2} ms · net sampled heap change: {3}. <a href="soak-metrics.json">Full metrics</a></p><p>Workflow timings include fixture delays and automation. Heap samples include instrumentation and do not certify absence of leaks. {4}</p>' -f (Encode $metrics.seconds),(Encode $metrics.iterations),(Encode $metrics.operationP95Ms),(Encode $heap),(Encode ($metrics.reviewFlags -join ' '))
}
$rows = foreach ($row in $report.results) {
    '<tr><td>{0}</td><td>{1}</td><td>{2}</td><td class="{3}">{3}</td><td>{4:N0}</td><td>{5}</td></tr>' -f (Encode $row.scenario),(Encode $row.persona),(Encode $row.layer),(Encode $row.outcome),$row.durationMs,(Encode $row.detail)
}
$attention = @($report.results | Where-Object outcome -in @('failed','blocked'))
$findings = if ($attention.Count) {
    '<ul>' + (($attention | ForEach-Object {'<li><strong>{0}</strong> ({1}): {2}</li>' -f (Encode $_.scenario),(Encode $_.outcome),(Encode $_.detail)}) -join '') + '</ul>'
} else { '<p>No failures or blocked checks recorded. Review skipped and manually confirmed acceptance separately.</p>' }
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><title>Desktop milestone audit</title>
<style>body{font:15px system-ui;margin:2rem;background:#151923;color:#eee}table{border-collapse:collapse;width:100%}th,td{padding:.6rem;border:1px solid #555;text-align:left;vertical-align:top}a{color:#9fc4ff}.failed{color:#ffb4b4}.blocked,.skipped{color:#ffdc9a}.passed{color:#a5e9b5}code{overflow-wrap:anywhere}</style>
<h1>Desktop milestone audit</h1><p>$(Encode $counts)</p><p>$(Encode $summary)</p>
<p><a href="build.json">Full build manifest and hashes</a> · <a href="report.json">JSON results</a> · <a href="browser-report/index.html">Browser traces and attachments</a></p>
<p>Fixture tests, native-host tests, production smoke, live observations and manual confirmations are separate evidence. Blocked and skipped checks are not passes. Memory trends do not certify absence of leaks.</p>
$stressSummary
<h2>Checks requiring attention</h2>$findings
<h2>Results</h2><table><tr><th>Scenario</th><th>Persona</th><th>Layer</th><th>Outcome</th><th>ms</th><th>Detail</th></tr>$($rows -join "`n")</table></html>
"@
Set-Content -LiteralPath (Join-Path $RunDirectory 'report.html') -Value $html -Encoding utf8
