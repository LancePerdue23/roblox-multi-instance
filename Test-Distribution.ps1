param([string]$Artifacts=(Join-Path $PSScriptRoot 'artifacts'),[version]$ExpectedVersion,[switch]$RequireSigned)
$ErrorActionPreference='Stop'
if($null -eq $ExpectedVersion){
    [xml]$taskVersionProps=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
    $taskProductVersion=[version]$taskVersionProps.Project.PropertyGroup.Version
    $ExpectedVersion=[version]::new($taskProductVersion.Major,$taskProductVersion.Minor,[Math]::Max(0,$taskProductVersion.Build),[Math]::Max(0,$taskProductVersion.Revision))
}
$taskManifest=Get-Content -LiteralPath (Join-Path $Artifacts 'assets.json') -Raw | ConvertFrom-Json
if($RequireSigned -and !$taskManifest.Release){throw 'Signed release artifacts required; these are unsigned validation artifacts.'}
if($taskManifest.Release -and $taskManifest.PublisherCertificateSha256 -notmatch '\A[a-fA-F0-9]{64}\z'){throw 'Signed release publisher fingerprint is missing or malformed.'}
if($taskManifest.Schema -ne 1 -or @($taskManifest.Assets).Count -ne 2){throw 'Expected exactly two installer editions.'}
if(@($taskManifest.Assets.Edition | Sort-Object -Unique).Count -ne 2 -or 'Desktop' -notin $taskManifest.Assets.Edition -or 'Helper' -notin $taskManifest.Assets.Edition){throw 'Missing installer edition.'}
foreach($taskAsset in $taskManifest.Assets) {
    $taskFile=Join-Path $Artifacts $taskAsset.Name
    $taskExpectedName=if($taskAsset.Edition -eq 'Desktop'){'RobloxMultiInstance-Setup.exe'}else{'RobloxLaunchHelper-Setup.exe'}
    if($taskAsset.Name -ne $taskExpectedName -or [version]$taskAsset.Version -ne $ExpectedVersion -or [version][Diagnostics.FileVersionInfo]::GetVersionInfo($taskFile).FileVersion -ne $ExpectedVersion){throw 'Installer name or version mismatch.'}
    if((Get-Item -LiteralPath $taskFile).Length -ne $taskAsset.Size -or (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskAsset.Sha256){throw "Asset mismatch: $($taskAsset.Name)"}
    if($taskAsset.Edition -eq 'Helper' -and ($taskAsset.Payload.Path | Where-Object {$_ -match '(^web/|WebView|Presentation|RobloxMultiInstance\.exe)'})){throw 'Helper payload contains desktop files.'}
    if($taskAsset.Protocol -ne 1){throw 'Unsupported helper protocol.'}
    if((Get-Content -LiteralPath ($taskFile+'.sha256') -Raw).Trim() -cne ($taskAsset.Sha256+'  '+$taskAsset.Name)){throw 'Installer checksum sidecar mismatch.'}
    if($taskManifest.Release){
        $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskFile
        if($taskSignature.Status -ne 'Valid' -or !$taskSignature.TimeStamperCertificate -or $taskSignature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) -ne $taskManifest.PublisherCertificateSha256){throw 'Installer publisher signature or timestamp failed verification.'}
    }
    $taskProcess=Start-Process -FilePath $taskFile -ArgumentList @('--inspect','--expect-edition',$taskAsset.Edition,'--silent') -PassThru -Wait -WindowStyle Hidden
    if($taskProcess.ExitCode -ne 0){throw "Embedded payload inspection failed: $($taskAsset.Name)"}
    Write-Output "PASS: $($taskAsset.Edition) installer digest, edition, embedded allowlist and payload hashes."
}
if(!(Test-Path -LiteralPath (Join-Path $Artifacts 'RobloxMultiInstance-Portable.zip'))){throw 'Portable package is missing.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskZip=[IO.Compression.ZipFile]::OpenRead((Join-Path $Artifacts 'RobloxMultiInstance-Portable.zip'))
try {
    if(!($taskZip.Entries.FullName -contains 'RobloxMultiInstance-Setup.exe')){throw 'Portable desktop is missing the matching setup payload.'}
    if(!($taskZip.Entries.FullName -contains 'RobloxMultiInstance.exe')){throw 'Portable desktop is missing its executable.'}
    $taskDesktop=$taskManifest.Assets | Where-Object Edition -eq 'Desktop'
    $taskPortable=$taskManifest.Portable
    $taskPortableFile=Join-Path $Artifacts 'RobloxMultiInstance-Portable.zip'
    if($taskPortable.Name -ne 'RobloxMultiInstance-Portable.zip' -or $taskPortable.Edition -ne 'Portable' -or $taskPortable.Protocol -ne 1 -or [version]$taskPortable.Version -ne $ExpectedVersion -or $taskPortable.Size -ne (Get-Item -LiteralPath $taskPortableFile).Length -or $taskPortable.Sha256 -ne (Get-FileHash -LiteralPath $taskPortableFile -Algorithm SHA256).Hash){throw 'Portable manifest mismatch.'}
    if((Get-Content -LiteralPath ($taskPortableFile+'.sha256') -Raw).Trim() -cne ($taskPortable.Sha256+'  '+$taskPortable.Name)){throw 'Portable checksum sidecar mismatch.'}
    $taskExpected=@{}
    foreach($taskItem in $taskDesktop.Payload){$taskExpected[$taskItem.Path]=$taskItem}
    $taskExpected['RobloxMultiInstance-Setup.exe']=$taskDesktop
    $taskSeen=@{}
    foreach($taskEntry in $taskZip.Entries){
        if($taskSeen.ContainsKey($taskEntry.FullName)){throw 'Duplicate portable member.'};$taskSeen[$taskEntry.FullName]=$true
        if($taskEntry.FullName -eq 'payload.json'){
            $taskReader=[IO.StreamReader]::new($taskEntry.Open())
            try{$taskEmbedded=$taskReader.ReadToEnd() | ConvertFrom-Json}finally{$taskReader.Dispose()}
            if($taskEmbedded.Edition -ne 'Desktop' -or [version]$taskEmbedded.Version -ne $ExpectedVersion -or $taskEmbedded.Protocol -ne 1 -or $taskEmbedded.OwnerSid -ne '' -or @($taskEmbedded.Files).Count -ne @($taskDesktop.Payload).Count){throw 'Portable embedded manifest mismatch.'}
            if(@($taskEmbedded.Files.Path | Sort-Object -Unique).Count -ne @($taskEmbedded.Files).Count){throw 'Duplicate portable allowlist member.'}
            foreach($taskItem in $taskEmbedded.Files){$taskMatch=$taskExpected[$taskItem.Path];if(!$taskMatch -or $taskMatch.Size -ne $taskItem.Size -or $taskMatch.Sha256 -ne $taskItem.Sha256){throw 'Portable allowlist mismatch.'}}
            continue
        }
        $taskMatch=$taskExpected[$taskEntry.FullName]
        if(!$taskMatch -or $taskEntry.Length -ne $taskMatch.Size){throw 'Unexpected portable member or size.'}
        $taskStream=$taskEntry.Open()
        try{$taskHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream))}finally{$taskStream.Dispose()}
        if($taskHash -ne $taskMatch.Sha256){throw "Portable member digest mismatch: $($taskEntry.FullName)"}
        if($taskEntry.FullName -in @('RobloxMultiInstance.exe','RobloxLaunchHelper.exe','rmi.exe','RobloxMultiInstance.dll','RobloxLaunchHelper.dll','rmi.dll','RobloxMultiInstance.Core.dll','RobloxMultiInstance.Backend.dll','RobloxMultiInstance.Presentation.dll')){
            $taskVersionDirectory=Join-Path $PSScriptRoot ('.build/release-version-check/'+[guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force $taskVersionDirectory | Out-Null
            $taskVersionFile=Join-Path $taskVersionDirectory $taskEntry.FullName
            [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry,$taskVersionFile)
            if([version][Diagnostics.FileVersionInfo]::GetVersionInfo($taskVersionFile).FileVersion -ne $ExpectedVersion){throw "Portable product version mismatch: $($taskEntry.FullName)"}
            $taskBinaryBytes=[IO.File]::ReadAllBytes($taskVersionFile)
            if([Text.Encoding]::UTF8.GetString($taskBinaryBytes) -match '(?i)[a-z]:[\\/]+Users[\\/]+' -or [Text.Encoding]::Unicode.GetString($taskBinaryBytes) -match '(?i)[a-z]:[\\/]+Users[\\/]+'){throw "Developer user-profile path embedded in release binary: $($taskEntry.FullName)"}
        }
    }
    if($taskSeen.Count -ne $taskExpected.Count+1 -or !$taskSeen.ContainsKey('payload.json')){throw 'Incomplete portable payload.'}
} finally {$taskZip.Dispose()}
$taskExpectedChecksums=@($taskManifest.Assets)+@($taskManifest.Portable) | ForEach-Object { $_.Sha256+'  '+$_.Name }
$taskExpectedChecksums+=((Get-FileHash -LiteralPath (Join-Path $Artifacts 'assets.json') -Algorithm SHA256).Hash+'  assets.json')
if(@(Compare-Object $taskExpectedChecksums @(Get-Content -LiteralPath (Join-Path $Artifacts 'SHA256SUMS.txt'))).Count){throw 'Aggregate checksum manifest mismatch.'}
Write-Output 'PASS: portable desktop includes explicit helper setup. No installation or Roblox process was started.'
