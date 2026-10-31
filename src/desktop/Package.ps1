param([switch]$SkipBuild, [string]$Dotnet = (Join-Path $PSScriptRoot '..\..\.build\sdk\dotnet.exe'))
$ErrorActionPreference = 'Stop'
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Dotnet $Dotnet }
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskOutput = Join-Path $taskRoot '.build\desktop'
if (!(Test-Path -LiteralPath (Join-Path $taskOutput 'RobloxMultiInstance.exe'))) { throw 'Build the portable preview first.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $taskOutput 'README.md') -Force
$taskPackages = Join-Path $taskRoot '.build\desktop-package'
New-Item -ItemType Directory -Force $taskPackages | Out-Null
$taskZip = Join-Path $taskPackages ('RobloxMultiInstance-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive = [IO.Compression.ZipFile]::Open($taskZip, 'Create')
try {
    Get-ChildItem -LiteralPath $taskOutput -Recurse -File | Where-Object { $_.Name -notlike 'host-*smoke*' } | ForEach-Object {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $_.FullName, $_.FullName.Substring($taskOutput.Length + 1).Replace('\','/')) | Out-Null
    }
} finally { $taskArchive.Dispose() }
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256
