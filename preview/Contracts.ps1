param([string]$Dotnet = (Join-Path $PSScriptRoot '..\.build\sdk\dotnet.exe'), [switch]$Write, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (!$NoBuild) {
    & $Dotnet build (Join-Path $PSScriptRoot 'backend\Tests\Backend.Tests.csproj') -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false --disable-build-servers
    if ($LASTEXITCODE) { throw 'Contract generator build failed.' }
}
$taskLines = & $Dotnet (Join-Path $PSScriptRoot 'backend\Tests\bin\Release\net10.0-windows\RobloxMultiInstancePreview.Backend.Tests.dll') --contracts
if ($LASTEXITCODE) { throw 'Contract generation failed.' }
$taskGenerated = ($taskLines -join "`n").TrimEnd() + "`n"
$taskTarget = Join-Path $PSScriptRoot 'web\src\backend.generated.ts'
if ($Write) { [IO.File]::WriteAllText($taskTarget, $taskGenerated, [Text.UTF8Encoding]::new($false)) }
elseif (!(Test-Path -LiteralPath $taskTarget) -or ([IO.File]::ReadAllText($taskTarget).Replace("`r`n","`n") -cne $taskGenerated)) { throw 'Backend TypeScript contracts drifted. Run preview/Contracts.ps1 -Write and review the generated changes.' }
else { Write-Output 'PASS: generated TypeScript contracts match C# DTOs.' }
