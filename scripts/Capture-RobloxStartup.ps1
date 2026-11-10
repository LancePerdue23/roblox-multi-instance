param(
    [ValidateSet('BeforeManager','ManagerOpen','AfterFirstLaunch')][string]$Phase = 'BeforeManager',
    [ValidateRange(0,1800)][int]$WatchSeconds = 0,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../.build/startup-evidence')
)
# Read-only observer. Never starts/stops Roblox, the manager, or a scheduled task.
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$taskCapture = Join-Path $OutputDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $Phase + '.jsonl')
function Write-Evidence($value) {
    ConvertTo-Json -InputObject $value -Depth 7 -Compress | Add-Content -LiteralPath $taskCapture -Encoding utf8
}
function Protect-Command([string]$command) {
    # Do not collect join tickets, authenticated launcher URLs, cookies or badge claims.
    $command = $command -replace '(?i)roblox(?:-player)?:[^\s"]+', '<Roblox URI redacted>'
    $command = $command -replace '(?i)(?<!\S)(-t|-j|--claim-badge)\s+("[^"]*"|\S+)', '$1 <redacted>'
    if ($command -match '(?i)cookie|authenticationticket|\.ROBLOSECURITY') { return '<sensitive command redacted>' }
    return $command
}
function Process-Evidence($process) {
    [ordered]@{ name=$process.Name; pid=$process.ProcessId; parentPid=$process.ParentProcessId
        createdUtc=if($process.CreationDate){$process.CreationDate.ToUniversalTime().ToString('O')}else{$null}
        executable=$process.ExecutablePath; commandLine=(Protect-Command $process.CommandLine) }
}
$taskBoot = Get-CimInstance Win32_OperatingSystem
Write-Evidence @{kind='phase';phase=$Phase;utc=[DateTime]::UtcNow.ToString('O');bootUtc=$taskBoot.LastBootUpTime.ToUniversalTime().ToString('O')}
foreach($taskKey in @('HKCU:/Software/Microsoft/Windows/CurrentVersion/Run','HKLM:/Software/Microsoft/Windows/CurrentVersion/Run')) {
    try {
        if(Test-Path $taskKey) {
            (Get-ItemProperty $taskKey).PSObject.Properties | Where-Object { $_.Name -match 'Roblox' -and $_.Name -notmatch '^PS' } |
                ForEach-Object { Write-Evidence @{kind='startup-registration';key=$taskKey;name=$_.Name;commandLine=(Protect-Command ([string]$_.Value))} }
        }
    } catch { Write-Evidence @{kind='startup-registration-unavailable';key=$taskKey;error=$_.Exception.Message} }
}
$taskDeadline = [DateTime]::UtcNow.AddSeconds($WatchSeconds)
$taskSource = 'RobloxStartup-' + [guid]::NewGuid().ToString('N')
$taskSubscription = $null
try {
    try {
        $taskSubscription = Register-CimIndicationEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName LIKE 'Roblox%'" -SourceIdentifier $taskSource
    } catch { Write-Evidence @{kind='event-trace-unavailable';error=$_.Exception.Message} }
    do {
        $taskAll = @(Get-CimInstance Win32_Process)
        $taskClients = @($taskAll | Where-Object { $_.Name -match '^(Roblox|rmi(?:\.exe$))' })
        $taskParents = @($taskAll | Where-Object { $_.ProcessId -in $taskClients.ParentProcessId })
        Write-Evidence @{kind='snapshot';utc=[DateTime]::UtcNow.ToString('O');processes=@($taskClients | ForEach-Object { Process-Evidence $_ });parents=@($taskParents | ForEach-Object { Process-Evidence $_ })}
        if($taskSubscription) {
            foreach($taskEvent in @(Get-Event -SourceIdentifier $taskSource -ErrorAction SilentlyContinue)) {
                $taskStarted = $taskEvent.SourceEventArgs.NewEvent
                Write-Evidence @{kind='process-start';name=$taskStarted.ProcessName;pid=$taskStarted.ProcessID;parentPid=$taskStarted.ParentProcessID;createdUtc=[DateTime]::FromFileTimeUtc([long]$taskStarted.TIME_CREATED).ToString('O')}
                Remove-Event -EventIdentifier $taskEvent.EventIdentifier
            }
        }
        if([DateTime]::UtcNow -ge $taskDeadline){break}
        Start-Sleep -Milliseconds 500
    } while($true)
} finally {
    if($taskSubscription){Unregister-Event -SourceIdentifier $taskSource;Remove-Job -Job $taskSubscription -Force}
    # Extract only the relevant safe markers; full Roblox logs can contain account/join data.
    foreach($taskDirectory in @("$env:LOCALAPPDATA/Roblox/logs","$env:LOCALAPPDATA/RobloxLaunchHelper/logs")) {
        Get-ChildItem -LiteralPath $taskDirectory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $taskBoot.LastBootUpTime.ToUniversalTime() } | ForEach-Object {
                $taskLog = $_.FullName
                Select-String -LiteralPath $taskLog -Pattern '\[FLog::Systray\]|AppState/TrayMode| (desktop-entry|launch-requested|launch-baseline|roblox-launched|roblox-launch-origin|launch-background-cleanup) ' |
                    ForEach-Object { Write-Evidence @{kind='log';path=$taskLog;line=$_.LineNumber;text=(Protect-Command $_.Line)} }
            }
    }
}
Write-Output $taskCapture
