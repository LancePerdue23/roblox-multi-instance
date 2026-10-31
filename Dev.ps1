param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1') -Dotnet $Dotnet
Start-Process -FilePath (Join-Path $PSScriptRoot '.build/desktop/RobloxMultiInstance.exe')
