param([string]$Dotnet = (Join-Path $PSScriptRoot '..\..\.build\sdk\dotnet.exe'), [switch]$SkipFrontend, [string]$Output = "", [string]$TelemetryEndpoint="", [string]$SupportConfig="", [string]$UpdatePublisherSha256="", [switch]$OmitSymbols)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskOutput = if ($Output) { [IO.Path]::GetFullPath($Output) } else { Join-Path $taskRoot '.build\desktop' }
# Refuse replacement of a running development build before publishing any files.
$taskExistingExe = Join-Path $taskOutput 'RobloxMultiInstance.exe'
if (Test-Path -LiteralPath $taskExistingExe) {
    try { $taskExeLock = [IO.File]::Open($taskExistingExe, 'Open', 'ReadWrite', 'None'); $taskExeLock.Dispose() }
    catch { throw "Close the development app at $taskExistingExe, then rebuild. The latest build keeps this same location." }
}
$taskOriginalHome = $env:DOTNET_CLI_HOME
$taskOriginalPackages = $env:NUGET_PACKAGES
$taskFeatureProperties=@("-p:TelemetryEndpoint=$TelemetryEndpoint", "-p:UpdatePublisherSha256=$UpdatePublisherSha256")
if($OmitSymbols){$taskFeatureProperties+=@('-p:DebugType=none','-p:DebugSymbols=false',"-p:PathMap=$taskRoot=/_/")}
if($SupportConfig){$taskFeatureProperties+="-p:SupportConfigPath=$([IO.Path]::GetFullPath($SupportConfig))"}
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.build\home'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.build\packages'
    if ([version](& $Dotnet --version) -lt [version]'10.0.100') { throw 'A supported .NET 10 SDK is required.' }
    if (!$SkipFrontend) {
        Push-Location (Join-Path $PSScriptRoot 'web')
        try { & npm.cmd ci; if ($LASTEXITCODE) { throw 'Frontend restore failed.' }; & npm.cmd run build; if ($LASTEXITCODE) { throw 'Frontend build failed.' } } finally { Pop-Location }
    }
    # Machine-local customization applies only to the default development output.
    # Package.ps1 always supplies an explicit output and never runs this hook.
    $taskLocalFrontend = Join-Path $taskRoot '.build/local-roles/prepare.ps1'
    if (!$Output -and (Test-Path -LiteralPath $taskLocalFrontend)) {
        & $taskLocalFrontend
    }
    & $Dotnet publish (Join-Path $PSScriptRoot 'host\RobloxMultiInstance.csproj') -c Release -r win-x64 --self-contained true -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers -o $taskOutput @taskFeatureProperties
    if ($LASTEXITCODE) { throw 'Desktop publish failed.' }
    if(!$SupportConfig -and (Test-Path -LiteralPath (Join-Path $taskOutput 'support-config.json'))){Remove-Item -LiteralPath (Join-Path $taskOutput 'support-config.json')}
    $taskWeb = Join-Path $taskOutput 'web'
    New-Item -ItemType Directory -Force $taskWeb | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'web/dist/local-supporter-preview.json'))) {
        foreach ($taskLocalFile in @('local-supporter-preview.json','local-role-switcher.js')) {
            $taskLocalPath=Join-Path $taskWeb $taskLocalFile
            if(Test-Path -LiteralPath $taskLocalPath){Remove-Item -LiteralPath $taskLocalPath -Force}
        }
    }
    Copy-Item (Join-Path $PSScriptRoot 'web\dist\*') $taskWeb -Recurse -Force
    Write-Host "Latest desktop build: $taskExistingExe"
    Get-FileHash -LiteralPath (Join-Path $taskOutput 'RobloxMultiInstance.exe') -Algorithm SHA256
} finally { $env:DOTNET_CLI_HOME = $taskOriginalHome; $env:NUGET_PACKAGES = $taskOriginalPackages }
