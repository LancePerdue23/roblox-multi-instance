param([string]$Dotnet = (Join-Path $PSScriptRoot '..\..\.build\sdk\dotnet.exe'), [switch]$SkipFrontend, [string]$Output = "")
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskOutput = if ($Output) { [IO.Path]::GetFullPath($Output) } else { Join-Path $taskRoot '.build\desktop' }
$taskOriginalHome = $env:DOTNET_CLI_HOME
$taskOriginalPackages = $env:NUGET_PACKAGES
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.build\home'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.build\packages'
    if ([version](& $Dotnet --version) -lt [version]'10.0.100') { throw 'A supported .NET 10 SDK is required.' }
    if (!$SkipFrontend) {
        Push-Location (Join-Path $PSScriptRoot 'web')
        try { & npm.cmd ci; if ($LASTEXITCODE) { throw 'Frontend restore failed.' }; & npm.cmd run build; if ($LASTEXITCODE) { throw 'Frontend build failed.' } } finally { Pop-Location }
    }
    & $Dotnet publish (Join-Path $PSScriptRoot 'host\RobloxMultiInstance.csproj') -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers -o $taskOutput
    if ($LASTEXITCODE) { throw 'Desktop publish failed.' }
    $taskWeb = Join-Path $taskOutput 'web'
    New-Item -ItemType Directory -Force $taskWeb | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'web\dist\*') $taskWeb -Recurse -Force
    Get-FileHash -LiteralPath (Join-Path $taskOutput 'RobloxMultiInstance.exe') -Algorithm SHA256
} finally { $env:DOTNET_CLI_HOME = $taskOriginalHome; $env:NUGET_PACKAGES = $taskOriginalPackages }
