param([switch]$SkipBuild,[string]$Dotnet=(Join-Path $PSScriptRoot '../../.build/sdk/dotnet.exe'))
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot '../../Package.ps1') -Dotnet $Dotnet -SkipFrontend:$SkipBuild
