param([string]$Dotnet = (Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'), [switch]$SkipFrontend, [string]$Output = "", [string]$TelemetryEndpoint="", [string]$SupportConfig="", [string]$UpdatePublisherSha256="")
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'src/desktop/Build.ps1') -Dotnet $Dotnet -SkipFrontend:$SkipFrontend -Output $Output -TelemetryEndpoint $TelemetryEndpoint -SupportConfig $SupportConfig -UpdatePublisherSha256 $UpdatePublisherSha256
