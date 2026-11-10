[CmdletBinding()]
param(
    [ValidateSet('Synthetic','Live')][string]$Mode = 'Synthetic',
    [string]$RunDirectory,
    [string]$Dotnet = (Join-Path (Join-Path $PSScriptRoot '../..') '.build/sdk/dotnet.exe'),
    [ValidateRange(1,3600)][int]$SoakSeconds = 900,
    [uint32]$Seed = 3102026
)
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
if (!$RunDirectory) {
    if ($Mode -eq 'Live') { throw 'Live mode requires -RunDirectory from a completed synthetic build.' }
    $RunDirectory = Join-Path $taskRepositoryRoot ('.build/audits/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
}
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if ($Mode -eq 'Synthetic' -and (Test-Path -LiteralPath $RunDirectory) -and (Get-ChildItem -LiteralPath $RunDirectory -Force | Select-Object -First 1)) {
    throw 'Use a new, empty directory for each synthetic audit; existing evidence is never overwritten.'
}
New-Item -ItemType Directory -Force -Path $RunDirectory | Out-Null
$runLock = [IO.File]::Open((Join-Path $RunDirectory 'run.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$steps = [Collections.Generic.List[object]]::new()
$reportPath = Join-Path $RunDirectory 'report.json'
if (Test-Path -LiteralPath $reportPath) {
    $previous = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    foreach ($row in $previous.results) { if ($row.layer -ne 'live') { $steps.Add($row) } }
}
function Add-Result($Name, $Outcome, $Detail, $Duration = 0, $Persona = 'system', $Layer = 'infrastructure', $Artifact = $null) {
    $steps.Add([pscustomobject]@{scenario=$Name;persona=$Persona;layer=$Layer;outcome=$Outcome;durationMs=$Duration;detail=$Detail;artifact=$Artifact})
}
function Invoke-AuditStep($Name, [scriptblock]$Action) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $log = Join-Path $RunDirectory ($Name -replace '[^a-zA-Z0-9-]','-')
    $log += '.log'
    try { & $Action *> $log; Add-Result $Name 'passed' 'Command completed.' $timer.Elapsed.TotalMilliseconds 'system' 'infrastructure' $log; return $true }
    catch { Add-Result $Name 'failed' $_.Exception.Message $timer.Elapsed.TotalMilliseconds 'system' 'infrastructure' $log; return $false }
}
function Read-Layer($File,$Layer) {
    if (!(Test-Path -LiteralPath $File)) { Add-Result "$Layer results" 'blocked' 'The harness did not produce its result file.' 0 'system' $Layer; return }
    foreach ($row in (Get-Content -LiteralPath $File -Raw | ConvertFrom-Json)) {
        Add-Result $row.scenario $row.outcome $row.detail $row.durationMs $row.persona $Layer $row.artifact
    }
}
function Read-BrowserSuite($Suites) {
    foreach ($suite in $Suites) {
        foreach ($spec in $suite.specs) {
            foreach ($case in $spec.tests) {
                $result = $case.results | Select-Object -Last 1
                $outcome = if ($result.status -eq 'passed') {'passed'} elseif ($result.status -eq 'skipped') {'skipped'} else {'failed'}
                $persona = if ($spec.title -match '^(basic|power|stress):') {$Matches[1]} else {'system'}
                $attachments = @(foreach ($attachment in $result.attachments) {
                    if ($attachment.name -eq 'soak-metrics' -and $attachment.body) {
                        $metricsPath = Join-Path $RunDirectory 'soak-metrics.json'
                        [IO.File]::WriteAllBytes($metricsPath,[Convert]::FromBase64String($attachment.body))
                        @{name='soak-metrics';contentType='application/json';path=$metricsPath}
                    } else { $attachment }
                })
                Add-Result $spec.title $outcome ($result.errors | ConvertTo-Json -Depth 10 -Compress) $result.duration $persona 'browser' ($attachments | ConvertTo-Json -Depth 6 -Compress)
            }
        }
        Read-BrowserSuite $suite.suites
    }
}
function Save-Report {
    $identity = if (Test-Path (Join-Path $RunDirectory 'build.json')) { Get-Content (Join-Path $RunDirectory 'build.json') -Raw | ConvertFrom-Json } else {$null}
    $report = @{updatedUtc=[DateTime]::UtcNow.ToString('o');build=$identity;results=@($steps.ToArray())}
    $report | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath $reportPath -Encoding utf8
    & (Join-Path $taskRepositoryRoot 'src/desktop/audit/Write-Report.ps1') -RunDirectory $RunDirectory
    Write-Host "Audit report: $reportPath"
    $steps | Group-Object outcome | ForEach-Object { Write-Host "$($_.Name): $($_.Count)" }
}
$taskEnv = @('AUDIT_OUTPUT','AUDIT_WEB_ROOT','AUDIT_SOAK_SECONDS','AUDIT_SEED','DOTNET_CLI_HOME','NUGET_PACKAGES')
$savedEnv = @{}
foreach ($name in $taskEnv) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRepositoryRoot '.build/home'
    $env:NUGET_PACKAGES = Join-Path $taskRepositoryRoot '.build/packages'
    if ($Mode -eq 'Synthetic') {
        $build = Join-Path $RunDirectory 'build'
        $built = Invoke-AuditStep 'Build-current-desktop' { & (Join-Path $taskRepositoryRoot 'Build.ps1') -Dotnet $Dotnet -Output $build }
        if (!$built) { throw 'Current build failed; dependent audit layers cannot run.' }
        $harness = Join-Path $RunDirectory 'harness'
        $nativeBuilt = Invoke-AuditStep 'Build-native-harness' {
            & $Dotnet publish (Join-Path $taskRepositoryRoot 'src/desktop/audit/AuditHarness.csproj') -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers -o $harness
            if ($LASTEXITCODE) { throw 'Native harness build failed.' }
            Copy-Item -LiteralPath (Join-Path $build 'web') -Destination $harness -Recurse -Force
        }
        $files = @(Get-ChildItem -LiteralPath $build -File -Recurse) + @(Get-ChildItem -LiteralPath $harness -File -Recurse -ErrorAction SilentlyContinue)
        $hashes = foreach ($file in $files) { @{path=[IO.Path]::GetRelativePath($RunDirectory,$file.FullName);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash} }
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $helper = Join-Path $env:ProgramFiles "RobloxLaunchHelper/$sid/RobloxLaunchHelper.exe"
        $identity = @{createdUtc=[DateTime]::UtcNow.ToString('o');revision=(& git -C $taskRepositoryRoot rev-parse HEAD);dirty=[bool](& git -C $taskRepositoryRoot status --porcelain);helperVersion=if(Test-Path -LiteralPath $helper){[Diagnostics.FileVersionInfo]::GetVersionInfo($helper).FileVersion}else{'not installed'};seed=$Seed;soakSeconds=$SoakSeconds;files=@($hashes)}
        $identity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $RunDirectory 'build.json') -Encoding utf8
        $null = Invoke-AuditStep 'Current-desktop-regressions' { & (Join-Path $taskRepositoryRoot 'Test.ps1') -Dotnet $Dotnet }
        $sharedPassed = Invoke-AuditStep 'Shared-service-regressions' { & (Join-Path $taskRepositoryRoot 'Test-Shared.ps1') -Dotnet $Dotnet }
        $sharedReport = Join-Path $taskRepositoryRoot '.build/shared-test-results.json'
        if ($sharedPassed -and (Test-Path -LiteralPath $sharedReport)) { Copy-Item -LiteralPath $sharedReport -Destination (Join-Path $RunDirectory 'shared.json') -Force }
        $env:AUDIT_OUTPUT = $RunDirectory
        $env:AUDIT_WEB_ROOT = Join-Path $build 'web'
        $env:AUDIT_SOAK_SECONDS = [string]$SoakSeconds
        $env:AUDIT_SEED = [string]$Seed
        $null = Invoke-AuditStep 'Browser-personas-and-stress' {
            Push-Location (Join-Path $taskRepositoryRoot 'src/desktop/web')
            try { & npm.cmd exec playwright test -- --config audit.config.ts; if ($LASTEXITCODE) { throw 'One or more browser audit scenarios failed.' } } finally { Pop-Location }
        }
        $browser = Join-Path $RunDirectory 'browser.json'
        if (Test-Path -LiteralPath $browser) { Read-BrowserSuite (Get-Content -LiteralPath $browser -Raw | ConvertFrom-Json).suites }
        if ($nativeBuilt) {
            $nativeOutput = Join-Path $RunDirectory 'native'
            $null = Invoke-AuditStep 'Native-host-fixtures' {
                $process = Start-Process -FilePath (Join-Path $harness 'RobloxMultiInstance.exe') -ArgumentList ('"'+$nativeOutput+'"') -WindowStyle Hidden -PassThru
                if (!$process.WaitForExit(180000)) { $process.Kill(); throw 'Native fixture harness timed out.' }
                if ($process.ExitCode) { throw 'Native fixture harness reported failures.' }
            }
            Read-Layer (Join-Path $nativeOutput 'native.json') 'native'
        }
        $null = Invoke-AuditStep 'Production-startup-smoke' {
            $exe = Join-Path $build 'RobloxMultiInstance.exe'
            $process = Start-Process -FilePath $exe -ArgumentList '--smoke' -WindowStyle Hidden -PassThru
            if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Production startup smoke timed out.' }
            $marker = Join-Path $build 'host-smoke.txt'
            if ($process.ExitCode -or !(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw) -notmatch '^WebView2 initialized;') { throw 'Production startup smoke did not certify initialization.' }
        }
        # Production smoke can see existing client captions; do not retain its page capture as audit evidence.
        $pageCapture = Join-Path $build 'host-smoke.png'
        if (Test-Path -LiteralPath $pageCapture) { Remove-Item -LiteralPath $pageCapture }
        Add-Result 'Physical DPI and screen reader' 'skipped' 'Requires separate physical display transitions and screen-reader acceptance.' 0 'basic' 'manual'
        Add-Result 'Live Roblox phase' 'skipped' 'Run scripts/testing/Audit.ps1 -Mode Live -RunDirectory with this audit directory; manual checkpoints are required.' 0 'live' 'live'
    }
    else {
        $manifest = Join-Path $RunDirectory 'build.json'
        if (!(Test-Path -LiteralPath $manifest)) { throw 'No synthetic build manifest exists in this directory.' }
        $identity = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        $liveSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $liveHelper = Join-Path $env:ProgramFiles "RobloxLaunchHelper/$liveSid/RobloxLaunchHelper.exe"
        $liveHelperVersion = if (Test-Path -LiteralPath $liveHelper) {[Diagnostics.FileVersionInfo]::GetVersionInfo($liveHelper).FileVersion} else {'not installed'}
        if ($liveHelperVersion -ne $identity.helperVersion) { throw 'Installed helper changed since this build was audited. Create a new synthetic audit.' }
        foreach ($file in $identity.files) {
            $target = [IO.Path]::GetFullPath((Join-Path $RunDirectory $file.path))
            if (!$target.StartsWith($RunDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Build manifest path escapes audit directory.' }
            if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Audit build changed. Create a new synthetic audit before live testing.' }
        }
        $liveOutput = Join-Path $RunDirectory ('live-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
        New-Item -ItemType Directory -Force -Path $liveOutput | Out-Null
        Write-Host "Live checkpoints: $liveOutput/checkpoint.json; respond using matching id and answer in response.json."
        & (Join-Path $RunDirectory 'harness/RobloxMultiInstance.exe') $liveOutput --live
        $liveExitCode = $LASTEXITCODE
        Read-Layer (Join-Path $liveOutput 'live.json') 'live'
        if ($liveExitCode -notin @(0,2)) { Add-Result 'Live harness completion' 'failed' "Live harness exited with code $liveExitCode." 0 'live' 'live' }
    }
}
catch { Add-Result 'Audit orchestration' 'failed' $_.Exception.Message }
finally {
    try { Save-Report }
    finally {
        foreach ($name in $taskEnv) { [Environment]::SetEnvironmentVariable($name,$savedEnv[$name],'Process') }
        $runLock.Dispose()
    }
}
if ($steps.outcome -contains 'failed') { exit 1 }
if ($Mode -eq 'Live' -and ($steps | Where-Object {$_.layer -eq 'live' -and $_.outcome -in @('blocked','skipped')})) { exit 2 }
