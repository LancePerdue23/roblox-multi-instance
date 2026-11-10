param(
    [string]$Dotnet = 'dotnet',
    [string]$Output = (Join-Path (Join-Path $PSScriptRoot '../..') 'release')
)
$taskRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
$taskBuildDirectory = Join-Path $taskRepositoryRoot '.build'
$taskEnvironment = @{
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_NOLOGO = '1'
    DOTNET_CLI_HOME = Join-Path $taskBuildDirectory 'home'
    APPDATA = Join-Path $taskBuildDirectory 'appdata'
    NUGET_PACKAGES = Join-Path $taskBuildDirectory 'packages'
}
$taskOriginalEnvironment = @{}
try {
    foreach ($taskName in $taskEnvironment.Keys) {
        $taskOriginalEnvironment[$taskName] = [Environment]::GetEnvironmentVariable($taskName, 'Process')
        [Environment]::SetEnvironmentVariable($taskName, $taskEnvironment[$taskName], 'Process')
    }
    New-Item -ItemType Directory -Force -Path $taskEnvironment.APPDATA | Out-Null
    $taskVersion = & $Dotnet --version
    if ($LASTEXITCODE -ne 0 -or [version]$taskVersion -lt [version]'10.0.100') { throw 'A supported .NET 10 SDK is required.' }
    $taskProject = Join-Path $taskRepositoryRoot 'src\helper\RobloxLaunchHelper.csproj'
    $taskConfig = Join-Path $taskRepositoryRoot 'NuGet.Config'
    & $Dotnet restore $taskProject --configfile $taskConfig -r win-x64 -p:SelfContained=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $Dotnet publish $taskProject -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -p:UseSharedCompilation=false -o $Output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Get-FileHash -LiteralPath (Join-Path $Output 'RobloxLaunchHelper.exe') -Algorithm SHA256
}
finally {
    foreach ($taskName in $taskOriginalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($taskName, $taskOriginalEnvironment[$taskName], 'Process')
    }
}
