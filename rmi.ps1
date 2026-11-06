# Development launcher. Installed builds register rmi on PATH instead.
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CliArguments)
$ErrorActionPreference = 'Stop'
$taskDotnet = Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'The local .NET SDK is missing. Run the project build setup first.' }
Push-Location $PSScriptRoot
try {
    $taskOutput = Join-Path $PSScriptRoot '.build/helper-cli'
    & $taskDotnet build (Join-Path $PSScriptRoot 'src/cli/rmi.csproj') -c Release -o $taskOutput --no-restore --disable-build-servers -p:UseSharedCompilation=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
    & $taskDotnet (Join-Path $taskOutput 'rmi.dll') @CliArguments
    $taskExitCode = $LASTEXITCODE
}
finally { Pop-Location }
exit $taskExitCode
