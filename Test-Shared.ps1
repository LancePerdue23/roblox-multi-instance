param(
    [string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'),
    [switch]$Integration
)
$ErrorActionPreference = 'Stop'
$taskBuildDirectory = Join-Path $PSScriptRoot '.build'
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
    $taskProject = Join-Path $PSScriptRoot 'tests\RobloxLaunchHelper.Tests.csproj'
    & $Dotnet restore $taskProject --configfile (Join-Path $PSScriptRoot 'NuGet.Config') -p:PublishSingleFile=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
    & $Dotnet build $taskProject -c Release --no-restore --disable-build-servers -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    $taskArguments = @((Join-Path $PSScriptRoot 'tests\bin\Release\net10.0-windows\RobloxLaunchHelper.Tests.dll'), '--report', (Join-Path $taskBuildDirectory 'shared-test-results.json'))
    if ($Integration) { $taskArguments += '--integration' }
    & $Dotnet @taskArguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    foreach ($taskName in $taskOriginalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($taskName, $taskOriginalEnvironment[$taskName], 'Process')
    }
}
