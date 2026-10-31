param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'), [switch]$SkipFrontend, [string]$Output = "")
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'src/desktop/Build.ps1') -Dotnet $Dotnet -SkipFrontend:$SkipFrontend -Output $Output
