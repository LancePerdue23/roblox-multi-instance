param([string]$Dotnet=(Join-Path $PSScriptRoot '.build/sdk/dotnet.exe'),[switch]$SkipFrontend,[string]$ExistingHelperPayload="",[string]$ProductVersion="",[string]$Artifacts="",[string]$TelemetryEndpoint=(Import-PowerShellDataFile (Join-Path $PSScriptRoot 'BuildConfig.psd1')).TelemetryEndpoint,[string]$SupportConfig="",[switch]$Release,[string]$SigningCertificateThumbprint="",[string]$PublisherCertificateSha256="",[string]$SignTool="signtool.exe",[string]$TimestampServer="")
$ErrorActionPreference='Stop'
if($Release) {
    if($SigningCertificateThumbprint -notmatch '\A[a-fA-F0-9]{40}\z' -or $PublisherCertificateSha256 -notmatch '\A[a-fA-F0-9]{64}\z'){throw 'Release packaging requires a certificate store thumbprint and its pinned SHA-256 certificate fingerprint.'}
    if($TimestampServer -notmatch '^https://'){throw 'Release packaging requires an HTTPS RFC 3161 timestamp server.'}
    $taskSigningCertificate=Get-Item -LiteralPath ("Cert:\CurrentUser\My\"+$SigningCertificateThumbprint)
    if(!$taskSigningCertificate.HasPrivateKey -or $taskSigningCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) -ne $PublisherCertificateSha256){throw 'Signing certificate does not match the publisher pin or has no private key.'}
    if($taskSigningCertificate.NotAfter -le (Get-Date)){throw 'Signing certificate has expired.'}
    $null=Get-Command $SignTool -ErrorAction Stop
} elseif($SigningCertificateThumbprint -or $PublisherCertificateSha256) {throw 'Use -Release to configure publisher signing; unsigned validation packages must not advertise a publisher pin.'}
function Sign-TaskFile([string]$taskSignPath) {
    if(!$Release){return}
    & $SignTool sign /sha1 $SigningCertificateThumbprint /s My /fd SHA256 /tr $TimestampServer /td SHA256 $taskSignPath
    if($LASTEXITCODE){throw "Signing failed: $taskSignPath"}
    & $SignTool verify /pa /all $taskSignPath
    if($LASTEXITCODE){throw "Signature verification failed: $taskSignPath"}
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskSignPath
    if($taskSignature.Status -ne 'Valid' -or !$taskSignature.TimeStamperCertificate -or $taskSignature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) -ne $PublisherCertificateSha256){throw 'Signed output failed publisher or timestamp verification.'}
}
$taskStage=Join-Path $PSScriptRoot ('.build/distribution/'+[guid]::NewGuid().ToString('N'))
$taskArtifacts=if($Artifacts){[IO.Path]::GetFullPath($Artifacts)}else{Join-Path $PSScriptRoot 'artifacts'}
if($ProductVersion -and $ProductVersion -notmatch '\A[0-9]+(?:\.[0-9]+){1,3}\z'){throw 'Invalid product version.'}
New-Item -ItemType Directory -Force $taskStage,$taskArtifacts | Out-Null
function Publish-TaskProject([string]$project,[string]$output,[string[]]$extra=@()) {
    $taskVersionProperty=@();if($ProductVersion){$taskVersionProperty+="-p:Version=$ProductVersion"}
    & $Dotnet publish (Join-Path $PSScriptRoot $project) -c Release -r win-x64 --self-contained true -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -p:BuildInParallel=false -p:DebugType=none -p:DebugSymbols=false "-p:PathMap=$PSScriptRoot=/_/" --disable-build-servers -o $output "-p:TelemetryEndpoint=$TelemetryEndpoint" "-p:UpdatePublisherSha256=$PublisherCertificateSha256" "-p:CoreVersion=$taskCoreVersion" @taskVersionProperty @extra
    if($LASTEXITCODE){throw "Publish failed: $project"}
}
function Assert-TaskPayloadPath([string]$taskPath) {
    for($taskAncestor=[IO.Path]::GetFullPath($taskPath);$taskAncestor;$taskAncestor=[IO.Path]::GetDirectoryName($taskAncestor)) {
        if((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Helper payload links and junctions are unsupported.'}
    }
}
$taskCoreVersion=''
if($ExistingHelperPayload) {
    Assert-TaskPayloadPath $ExistingHelperPayload
    $ExistingHelperPayload=(Resolve-Path -LiteralPath $ExistingHelperPayload).Path
    Assert-TaskPayloadPath (Join-Path $ExistingHelperPayload 'payload.json')
    if((Get-Item -LiteralPath (Join-Path $ExistingHelperPayload 'payload.json')).Length -gt 1048576){throw 'Oversized helper baseline manifest.'}
    $taskBaseline=Get-Content -LiteralPath (Join-Path $ExistingHelperPayload 'payload.json') -Raw | ConvertFrom-Json
    $taskCoreVersion=([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $ExistingHelperPayload 'RobloxMultiInstance.Core.dll'))).ProductVersion.Split('+')[0]
    if($taskCoreVersion -notmatch '\A[0-9]+(?:\.[0-9]+){1,3}\z'){throw 'Invalid baseline helper version.'}
}
$taskHelper=Join-Path $taskStage 'helper'
$taskCli=Join-Path $taskStage 'cli'
$taskDesktop=Join-Path $taskStage 'desktop'
# Share one runtime and core assembly inside each allowlisted edition payload.
Publish-TaskProject 'src/helper/RobloxLaunchHelper.csproj' $taskHelper @('-p:PublishSingleFile=false')
Publish-TaskProject 'src/cli/rmi.csproj' $taskCli @('-p:PublishSingleFile=false')
& (Join-Path $PSScriptRoot 'src/desktop/Build.ps1') -Dotnet $Dotnet -Output $taskDesktop -SkipFrontend:$SkipFrontend -TelemetryEndpoint $TelemetryEndpoint -SupportConfig $SupportConfig -UpdatePublisherSha256 $PublisherCertificateSha256 -OmitSymbols -CoreVersion $taskCoreVersion -ProductVersion $ProductVersion
if (Test-Path -LiteralPath (Join-Path $taskDesktop 'web/local-supporter-preview.json')) { throw 'Local supporter preview assets cannot be packaged. Rebuild the frontend in the default mode.' }
if (Test-Path -LiteralPath (Join-Path $taskDesktop 'web/local-role-switcher.js')) { throw 'Local role switcher cannot be packaged.' }
if($LASTEXITCODE){throw 'Desktop build failed.'}
# Sign first-party payloads before producing embedded manifests and hashes.
$taskSignedBinaries=@{}
if($Release){foreach($taskPayloadRoot in @($taskHelper,$taskCli,$taskDesktop)) {
    foreach($taskBinary in Get-ChildItem -LiteralPath $taskPayloadRoot -File | Where-Object {$_.Extension -in @('.exe','.dll') -and ($_.BaseName -like 'Roblox*' -or $_.BaseName -eq 'rmi')}) {
        $taskUnsignedHash=(Get-FileHash -LiteralPath $taskBinary.FullName -Algorithm SHA256).Hash
        if($taskSignedBinaries.ContainsKey($taskUnsignedHash)){Copy-Item -LiteralPath $taskSignedBinaries[$taskUnsignedHash] -Destination $taskBinary.FullName -Force}
        else {Sign-TaskFile $taskBinary.FullName; $taskSignedBinaries[$taskUnsignedHash]=$taskBinary.FullName}
    }
}}
$taskVersion=([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskDesktop 'RobloxMultiInstance.exe'))).FileVersion
$taskHelperVersion=$taskVersion
if($ExistingHelperPayload) {
    # Reuse a previously verified complete helper payload, including CLI/runtime dependencies.
    Assert-TaskPayloadPath $ExistingHelperPayload
    $ExistingHelperPayload=(Resolve-Path -LiteralPath $ExistingHelperPayload).Path
    Assert-TaskPayloadPath (Join-Path $ExistingHelperPayload 'payload.json')
    if((Get-Item -LiteralPath (Join-Path $ExistingHelperPayload 'payload.json')).Length -gt 1048576){throw 'Oversized helper baseline manifest.'}
    $taskBaseline=Get-Content -LiteralPath (Join-Path $ExistingHelperPayload 'payload.json') -Raw | ConvertFrom-Json
    if($taskBaseline.Schema -ne 1 -or $taskBaseline.Edition -ne 'Helper' -or $taskBaseline.Protocol -ne 1){throw 'Expected a protocol-1 helper payload.'}
    $taskHelperVersion=$taskBaseline.Version
    if([version]$taskHelperVersion -gt [version]$taskVersion){throw 'Cannot reuse a newer helper.'}
    foreach($taskFile in $taskBaseline.Files) {
        $taskRelative=$taskFile.Path
        if($taskRelative -eq 'payload.json' -or $taskRelative -match '(^web/|WebView|Presentation|RobloxMultiInstance\.exe)'){throw 'Expected a helper-only payload.'}
        if([IO.Path]::IsPathRooted($taskRelative) -or $taskRelative -match '(^|[/\\])\.\.?([/\\]|$)|:'){throw 'Unsafe helper payload entry.'}
        $taskFilePath=Join-Path $ExistingHelperPayload $taskRelative
        Assert-TaskPayloadPath $taskFilePath
        if((Get-Item -LiteralPath $taskFilePath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Helper payload links are unsupported.'}
        if((Get-Item -LiteralPath $taskFilePath).Length -ne $taskFile.Size -or (Get-FileHash -LiteralPath $taskFilePath -Algorithm SHA256).Hash -ne $taskFile.Sha256){throw 'Helper payload integrity failed.'}
    }
    if(@($taskBaseline.Files.Path | Sort-Object -Unique).Count -ne @($taskBaseline.Files).Count -or 'RobloxLaunchHelper.exe' -notin $taskBaseline.Files.Path -or 'rmi.exe' -notin $taskBaseline.Files.Path){throw 'Incomplete or duplicate baseline allowlist.'}
    foreach($taskExe in @('RobloxLaunchHelper.exe','rmi.exe')) {
        if([version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $ExistingHelperPayload $taskExe)).FileVersion -ne [version]$taskHelperVersion){throw 'Baseline executable version mismatch.'}
    }
    $taskHelper=$ExistingHelperPayload
    $taskCli=$ExistingHelperPayload
}

$taskAssets=@()
foreach($taskEdition in $(if($ExistingHelperPayload){@('Desktop')}else{@('Desktop','Helper')})) {
    $taskPayload=Join-Path $taskStage ($taskEdition+'/payload')
    New-Item -ItemType Directory -Force $taskPayload | Out-Null
    $taskSources=if($taskEdition -eq 'Desktop'){@($taskDesktop,$taskHelper,$taskCli)}else{@($taskHelper,$taskCli)}
    foreach($taskSource in $taskSources) {
        foreach($taskFile in Get-ChildItem -LiteralPath $taskSource -File -Recurse) {
            if($taskFile.Extension -eq '.pdb' -or $taskFile.Name -eq 'payload.json'){continue}
            if($ExistingHelperPayload -and $taskSource -eq $ExistingHelperPayload -and $taskFile.FullName -notin @($taskBaseline.Files | ForEach-Object {Join-Path $ExistingHelperPayload $_.Path})){throw 'Unexpected helper payload file.'}
            $taskRelative=[IO.Path]::GetRelativePath($taskSource,$taskFile.FullName)
            $taskTarget=Join-Path $taskPayload $taskRelative
            if(Test-Path -LiteralPath $taskTarget) {
                if((Get-FileHash -LiteralPath $taskTarget).Hash -ne (Get-FileHash -LiteralPath $taskFile.FullName).Hash){
                    # WindowsDesktop intentionally replaces these base-runtime facades.
                    if($taskEdition -eq 'Desktop' -and $taskRelative -in @('Microsoft.VisualBasic.dll','System.Drawing.dll','WindowsBase.dll')){continue}
                    throw "Conflicting payload: $taskRelative"
                }
                continue
            }
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($taskTarget)) | Out-Null
            Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget
        }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $taskPayload 'LICENSE') -Force
    $taskFiles=@(Get-ChildItem -LiteralPath $taskPayload -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{Path=[IO.Path]::GetRelativePath($taskPayload,$_.FullName).Replace('\','/');Size=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    if($taskEdition -eq 'Helper' -and ($taskFiles.Path | Where-Object {$_ -match '(^web/|WebView|Presentation|RobloxMultiInstance\.exe)'})){throw 'Desktop files leaked into helper payload.'}
    $taskManifest=@{Schema=1;Edition=$taskEdition;Version=$taskVersion;HelperVersion=$taskHelperVersion;Protocol=1;OwnerSid='';Files=$taskFiles}
    $taskJson=ConvertTo-Json -InputObject $taskManifest -Depth 8
    [IO.File]::WriteAllText((Join-Path $taskPayload 'payload.json'),$taskJson,[Text.UTF8Encoding]::new($false))
    $taskZip=Join-Path $taskStage ($taskEdition+'/payload.zip')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($taskPayload,$taskZip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $taskSetupOutput=Join-Path $taskStage ($taskEdition+'/setup')
    Publish-TaskProject 'src/setup/RobloxMultiInstance.Setup.csproj' $taskSetupOutput @("-p:PayloadZip=$taskZip",'-p:IncludeNativeLibrariesForSelfExtract=true')
    $taskName=if($taskEdition -eq 'Desktop'){'RobloxMultiInstance-Setup.exe'}else{'RobloxLaunchHelper-Setup.exe'}
    $taskDestination=Join-Path $taskArtifacts $taskName
    Copy-Item -LiteralPath (Join-Path $taskSetupOutput 'RobloxMultiInstance.Setup.exe') -Destination $taskDestination -Force
    Sign-TaskFile $taskDestination
    $taskHash=(Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(($taskDestination+'.sha256'),($taskHash+'  '+$taskName+"`n"))
    $taskAssets+=@{Edition=$taskEdition;Version=$taskVersion;HelperVersion=$taskHelperVersion;Protocol=1;Name=$taskName;Size=(Get-Item -LiteralPath $taskDestination).Length;Sha256=$taskHash;Payload=$taskFiles}
    if($taskEdition -eq 'Desktop') {
        $taskPortable=Join-Path $taskStage 'portable'
        New-Item -ItemType Directory -Force $taskPortable | Out-Null
        Copy-Item -Path (Join-Path $taskPayload '*') -Destination $taskPortable -Recurse
        Copy-Item -LiteralPath $taskDestination -Destination (Join-Path $taskPortable $taskName)
        $taskPortableZip=Join-Path $taskArtifacts 'RobloxMultiInstance-Portable.zip'
        if(Test-Path -LiteralPath $taskPortableZip){Remove-Item -LiteralPath $taskPortableZip}
        [IO.Compression.ZipFile]::CreateFromDirectory($taskPortable,$taskPortableZip,[IO.Compression.CompressionLevel]::Optimal,$false)
        $taskPortableAsset=@{Edition='Portable';Version=$taskVersion;Protocol=1;Name='RobloxMultiInstance-Portable.zip';Size=(Get-Item -LiteralPath $taskPortableZip).Length;Sha256=(Get-FileHash -LiteralPath $taskPortableZip -Algorithm SHA256).Hash}
        [IO.File]::WriteAllText(($taskPortableZip+'.sha256'),($taskPortableAsset.Sha256+'  '+$taskPortableAsset.Name+"`n"))
    }
}
[IO.File]::WriteAllText((Join-Path $taskArtifacts 'assets.json'),(ConvertTo-Json -InputObject @{Schema=1;Release=[bool]$Release;PublisherCertificateSha256=$PublisherCertificateSha256;Assets=$taskAssets;Portable=$taskPortableAsset} -Depth 10),[Text.UTF8Encoding]::new($false))
$taskChecksumLines=@($taskAssets)+@($taskPortableAsset) | ForEach-Object { $_.Sha256+'  '+$_.Name }
$taskChecksumLines+=((Get-FileHash -LiteralPath (Join-Path $taskArtifacts 'assets.json') -Algorithm SHA256).Hash+'  assets.json')
[IO.File]::WriteAllLines((Join-Path $taskArtifacts 'SHA256SUMS.txt'),$taskChecksumLines,[Text.UTF8Encoding]::new($false))
Write-Output "Built selected installers and portable desktop in $taskArtifacts"
