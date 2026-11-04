<# Grand Ocean Sprint candidate 015. Private-lobby test; no networking, settings or save edits. #>
[CmdletBinding()]
param(
    [ValidateSet('Inspect','Build','Install','Restore')][string]$Action='Inspect',
    [string]$GameFolder,
    [string]$SaveFolder
)
$ErrorActionPreference='Stop'
if (-not $GameFolder) { $GameFolder=Read-Host 'Paste the game folder containing NFS11Remastered.exe' }
$GameFolder=$GameFolder.Trim().Trim('"')
$nfsTarget=Join-Path $GameFolder 'GAMELOGIC\CHALLENGEINSTANCES.BNDL'
$nfsState=Join-Path $PSScriptRoot 'state'
$nfsOriginal=Join-Path $nfsState 'original-CHALLENGEINSTANCES.BNDL'
$nfsBuilt=Join-Path $nfsState 'candidate-CHALLENGEINSTANCES.BNDL'
$nfsOriginalHash='db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052'
$nfsCandidateHash='0d5c4cda823bce57874e615f21c13a1f60741a0006c2a0f49ac70eac1878ef3b'
function HashFile([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function ReplaceArchive([string]$Source, [string]$BeforeHash, [string]$AfterHash) {
    if (Get-Process -Name NFS11Remastered -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
    if ((HashFile $Source) -ne $AfterHash -or (HashFile $nfsTarget) -ne $BeforeHash) { throw 'Source or installed hash changed.' }
    $nfsStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
    $nfsStaged=$nfsTarget+'.grand-ocean-015-'+$nfsStamp+'.tmp'
    $nfsPrevious=$nfsTarget+'.grand-ocean-015-'+$nfsStamp+'.bak'
    if ((Test-Path -LiteralPath $nfsStaged) -or (Test-Path -LiteralPath $nfsPrevious)) { throw 'Refusing to overwrite staging or backup paths.' }
    [ordered]@{TimeUTC=[DateTime]::UtcNow.ToString('o');Action=$Action;BeforeHash=$BeforeHash;AfterHash=$AfterHash;Target=$nfsTarget;Staging=$nfsStaged;RetainedBackup=$nfsPrevious} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $nfsState 'replacement-intents.jsonl') -Encoding UTF8
    Copy-Item -LiteralPath $Source -Destination $nfsStaged
    if ((HashFile $nfsStaged) -ne $AfterHash) { throw 'Staging mismatch; original target untouched.' }
    if (Get-Process -Name NFS11Remastered -ErrorAction SilentlyContinue) { throw 'Game opened while preparing.' }
    if ((HashFile $nfsTarget) -ne $BeforeHash) { throw 'Installed archive changed; replacement refused.' }
    [IO.File]::Replace($nfsStaged,$nfsTarget,$nfsPrevious)
    if ((HashFile $nfsTarget) -ne $AfterHash -or (HashFile $nfsPrevious) -ne $BeforeHash) { throw 'Replacement verification failed; keep game closed and inspect retained backups.' }
}
$nfsCurrent=HashFile $nfsTarget
if ($nfsCurrent -notin @($nfsOriginalHash,$nfsCandidateHash)) { throw 'Unknown archive hash; no files changed.' }
$nfsInstalled=if ($nfsCurrent -eq $nfsOriginalHash) {'original'} else {'multiplayer_test'}
$nfsRunning=[bool](Get-Process -Name 'NFS11Remastered' -ErrorAction SilentlyContinue)
if ($Action -eq 'Inspect') { [ordered]@{State=$nfsInstalled;GameRunning=$nfsRunning;SHA256=$nfsCurrent} | ConvertTo-Json; return }
if ($nfsRunning) { throw 'Close the game first.' }
if ($Action -eq 'Restore') {
    if ($nfsInstalled -eq 'original') { Write-Output 'Already original.'; return }
    if ((HashFile $nfsOriginal) -ne $nfsOriginalHash) { throw 'Original backup missing or changed.' }
    ReplaceArchive $nfsOriginal $nfsCandidateHash $nfsOriginalHash
    if ((HashFile $nfsTarget) -ne $nfsOriginalHash) { throw 'Restore verification failed; keep game closed.' }
} else {
    if ($nfsInstalled -ne 'original') { Write-Output 'Verified multiplayer candidate is already installed.'; return }
    $nfsExpected=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'expected-hashes.json') -Raw | ConvertFrom-Json
    foreach ($nfsFile in $nfsExpected) {
        if ((HashFile (Join-Path $GameFolder $nfsFile.RelativePath)) -ne $nfsFile.SHA256.ToLowerInvariant()) { throw ('Baseline mismatch: '+$nfsFile.RelativePath) }
    }
    $nfsPatch=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'patch.json') -Raw | ConvertFrom-Json
    if ($nfsPatch.original_sha256 -ne $nfsOriginalHash -or $nfsPatch.candidate_sha256 -ne $nfsCandidateHash) { throw 'Unexpected patch identity.' }
    $nfsSource=[IO.File]::ReadAllBytes($nfsTarget)
    $nfsStream=New-Object IO.MemoryStream
    try {
        foreach ($nfsOp in $nfsPatch.operations) {
            if ($null -ne $nfsOp.data) { $nfsBytes=[Convert]::FromBase64String($nfsOp.data); $nfsStream.Write($nfsBytes,0,$nfsBytes.Length) }
            else {
                if ($nfsOp.offset -lt 0 -or $nfsOp.length -lt 0 -or ($nfsOp.offset+$nfsOp.length) -gt $nfsSource.Length) { throw 'Invalid copy bounds.' }
                $nfsStream.Write($nfsSource,[int]$nfsOp.offset,[int]$nfsOp.length)
            }
            if ($nfsStream.Length -gt 1048576) { throw 'Unexpected output size.' }
        }
        $nfsBytes=$nfsStream.ToArray()
    } finally { $nfsStream.Dispose() }
    $nfsHasher=[Security.Cryptography.SHA256]::Create()
    try { $nfsResultHash=([BitConverter]::ToString($nfsHasher.ComputeHash($nfsBytes))).Replace('-','').ToLowerInvariant() } finally { $nfsHasher.Dispose() }
    if ($nfsResultHash -ne $nfsCandidateHash) { throw 'Candidate reconstruction failed; game untouched.' }
    if ($nfsBytes.Length -ne $nfsPatch.candidate_length) { throw 'Candidate length mismatch.' }
    New-Item -ItemType Directory -Path $nfsState -Force | Out-Null
    [IO.File]::WriteAllBytes($nfsBuilt,$nfsBytes)
    if ($Action -eq 'Build') { Write-Output ('Build verified: '+$nfsResultHash); return }
    if (Test-Path -LiteralPath $nfsOriginal) {
        if ((HashFile $nfsOriginal) -ne $nfsOriginalHash) { throw 'Existing original backup has an unexpected hash.' }
    } else { Copy-Item -LiteralPath $nfsTarget -Destination $nfsOriginal }
    if ((HashFile $nfsOriginal) -ne $nfsOriginalHash) { throw 'Archive backup verification failed.' }
    if (-not $SaveFolder) { $SaveFolder=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Criterion Games\Need for Speed(TM) Hot Pursuit Remastered' }
    if (-not (Test-Path -LiteralPath $SaveFolder -PathType Container)) { throw 'Save folder not found. Supply -SaveFolder with its correct location.' }
    $nfsSaveRoot=(Get-Item -LiteralPath $SaveFolder).FullName.TrimEnd('\')
    $nfsItems=@(Get-ChildItem -LiteralPath $nfsSaveRoot -Recurse -Force)
    if ($nfsItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Unexpected save reparse point.' }
    $nfsSaveFiles=@($nfsItems | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    if ($nfsSaveFiles.Count -eq 0) { throw 'No save files found.' }
    $nfsSaveBackup=Join-Path $nfsState ('saves-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $nfsSaveBackup | Out-Null
    $nfsRecords=@()
    foreach ($nfsFile in $nfsSaveFiles) {
        $nfsRelative=$nfsFile.FullName.Substring($nfsSaveRoot.Length+1)
        $nfsRecords += [pscustomobject]@{RelativePath=$nfsRelative;SHA256=(HashFile $nfsFile.FullName)}
    }
    foreach ($nfsRecord in $nfsRecords) {
        $nfsDest=Join-Path $nfsSaveBackup $nfsRecord.RelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $nfsDest) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $nfsSaveRoot $nfsRecord.RelativePath) -Destination $nfsDest
    }
    foreach ($nfsRecord in $nfsRecords) {
        if ((HashFile (Join-Path $nfsSaveRoot $nfsRecord.RelativePath)) -ne $nfsRecord.SHA256 -or (HashFile (Join-Path $nfsSaveBackup $nfsRecord.RelativePath)) -ne $nfsRecord.SHA256) { throw 'Save backup changed or failed verification.' }
    }
    $nfsAfter=@(Get-ChildItem -LiteralPath $nfsSaveRoot -Recurse -Force -File | Sort-Object FullName | ForEach-Object { $_.FullName })
    if (Compare-Object @($nfsSaveFiles.FullName) $nfsAfter) { throw 'Save file list changed during backup.' }
    [ordered]@{SourceRoot=$nfsSaveRoot;Files=$nfsRecords} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nfsSaveBackup 'backup-manifest.json') -Encoding UTF8
    if (Get-Process -Name 'NFS11Remastered' -ErrorAction SilentlyContinue) { throw 'Game opened during preparation.' }
    if ((HashFile $nfsTarget) -ne $nfsOriginalHash) { throw 'Installed file changed during preparation.' }
    ReplaceArchive $nfsBuilt $nfsOriginalHash $nfsCandidateHash
    if ((HashFile $nfsTarget) -ne $nfsCandidateHash) { throw 'Install verification failed; keep game closed and run Restore.' }
    Write-Output ('Save backup: '+$nfsSaveBackup)
}
$nfsRecord=[ordered]@{TimeUTC=[DateTime]::UtcNow.ToString('o');Action=$Action;SHA256=(HashFile $nfsTarget);SaveFilesModified=$false}
$nfsRecord | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $nfsState 'deployment-history.jsonl') -Encoding UTF8
$nfsRecord | ConvertTo-Json

