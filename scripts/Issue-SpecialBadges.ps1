param([switch]$Initialize,[string]$OwnerDirectory=(Join-Path $PSScriptRoot '../badge-owner'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
$taskKeyPath=Join-Path $OwnerDirectory 'issuer.dpapi'
$taskListPath=Join-Path $OwnerDirectory 'assignments.json'
$taskPublicPath=Join-Path $PSScriptRoot '../src/desktop/backend/badge-public-key.txt'
$taskEncoding=[Text.UTF8Encoding]::new($false)
if($Initialize){
    if((Test-Path $taskKeyPath) -or (Test-Path $taskPublicPath)){throw 'Issuer already exists. Preserve the original private key; do not replace a released verification key.'}
    New-Item -ItemType Directory -Force $OwnerDirectory | Out-Null
    $taskKey=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    try {
        $taskPrivate=$taskKey.ExportPkcs8PrivateKey()
        [IO.File]::WriteAllBytes($taskKeyPath,[Security.Cryptography.ProtectedData]::Protect($taskPrivate,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
        [IO.File]::WriteAllText($taskPublicPath,[Convert]::ToBase64String($taskKey.ExportSubjectPublicKeyInfo()),$taskEncoding)
    } finally {if($taskPrivate){[Array]::Clear($taskPrivate,0,$taskPrivate.Length)};if($taskKey){$taskKey.Dispose()}}
    [IO.File]::WriteAllText($taskListPath,'{"badges":[{"id":"alpha-tester","title":"Alpha Tester","users":[]}]}',$taskEncoding)
    Write-Output 'Private issuer and master list created under badge-owner. Back up this folder securely; it is never packaged or committed.'
    return
}
$taskList=Get-Content -LiteralPath $taskListPath -Raw | ConvertFrom-Json
$taskKey=[Security.Cryptography.ECDsa]::Create()
$taskPrivate=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskKeyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $taskRead=0;$taskKey.ImportPkcs8PrivateKey($taskPrivate,[ref]$taskRead)
    if([Convert]::ToBase64String($taskKey.ExportSubjectPublicKeyInfo()) -ne ([IO.File]::ReadAllText($taskPublicPath)).Trim()){throw 'Issuer does not match the public verification key.'}
    $taskOut=Join-Path $OwnerDirectory 'grants';New-Item -ItemType Directory -Force $taskOut | Out-Null
    foreach($taskBadge in $taskList.badges){
        if($taskBadge.id -cnotmatch '^[a-z0-9-]{1,48}$' -or !$taskBadge.title -or $taskBadge.title.Length -gt 48 -or $taskBadge.title -match '[\x00-\x1f]'){throw 'Invalid badge definition.'}
        $taskUsers=@($taskBadge.users | Select-Object -Unique)
        if($taskUsers.Count -lt 1 -or $taskUsers.Count -gt 10000){throw 'Provide between 1 and 10000 tester IDs.'}
        $taskSalt=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $taskHashes=@(foreach($taskUser in $taskUsers){
            $taskNumeric=0L
            if([string]$taskUser -cnotmatch '^[1-9][0-9]{0,18}$' -or ![long]::TryParse([string]$taskUser,[ref]$taskNumeric)){throw 'Use positive numeric Roblox user IDs.'}
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskEncoding.GetBytes("rmi.badge.v2`n"+$taskSalt+"`n"+[string]$taskUser)))
        })
        $taskPayload=$taskEncoding.GetBytes((@{schema=2;product='roblox-multi-instance';badge=$taskBadge.id;title=$taskBadge.title;salt=$taskSalt;eligibleHashes=$taskHashes} | ConvertTo-Json -Compress -Depth 4))
        $taskSignature=$taskKey.SignData($taskPayload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
        $taskReceipt=[Convert]::ToBase64String($taskPayload)+'.'+[Convert]::ToBase64String($taskSignature)
        if($taskReceipt.Length -gt 1048576){throw 'Shared grant exceeds the application limit.'}
        [IO.File]::WriteAllText((Join-Path $taskOut ($taskBadge.id+'.rmibadge')),$taskReceipt,$taskEncoding)

    }
    Write-Output 'Shared signed grants issued. Deliver only the intended badge file; keep the issuer and assignments private.'
} finally {[Array]::Clear($taskPrivate,0,$taskPrivate.Length);if($taskKey){$taskKey.Dispose()}}
