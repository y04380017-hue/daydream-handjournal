[CmdletBinding()]
param(
    [string]$BaselinePath = 'E:\TapeGestureDemo\backups\handbook-before-stage4b-20261103',
    [string]$MasterPath = 'E:\HandbookGameDemo',
    [string]$WorkPath = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$baselineRoot = [IO.Path]::GetFullPath($BaselinePath).TrimEnd('\')
$masterRoot = [IO.Path]::GetFullPath($MasterPath).TrimEnd('\')
$workRoot = [IO.Path]::GetFullPath($WorkPath).TrimEnd('\')
$auditFolders = @('Assets','Packages','ProjectSettings','Source','Tools','docs')
$auditRootFiles = @('.gitignore','AGENTS.md','Open-Unity.cmd','README.md')

function Read-AuditInventory([string]$rootPath) {
    $inventory = @{}
    $files = [Collections.Generic.List[object]]::new()
    foreach ($folder in $auditFolders) {
        $folderPath = Join-Path $rootPath $folder
        if (Test-Path -LiteralPath $folderPath) {
            foreach ($entry in (Get-ChildItem -LiteralPath $folderPath -File -Recurse -Force)) {
                $files.Add($entry)
            }
        }
    }
    foreach ($name in $auditRootFiles) {
        $entryPath = Join-Path $rootPath $name
        if (Test-Path -LiteralPath $entryPath) { $files.Add((Get-Item -LiteralPath $entryPath)) }
    }
    foreach ($entry in $files) {
        $relativePath = $entry.FullName.Substring($rootPath.Length + 1).Replace('\','/')
        # Verification logs and captures are reported separately from protected source.
        if ($relativePath -match '^docs/(?:[^/]*logs|[^/]*screenshots)/') { continue }
        $inventory[$relativePath] = @{
            bytes = $entry.Length
            sha256 = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    return $inventory
}

function Compare-AuditInventory($reference, $candidate) {
    $modified = [Collections.Generic.List[object]]::new()
    $missing = [Collections.Generic.List[string]]::new()
    $added = [Collections.Generic.List[string]]::new()
    $unchanged = 0
    foreach ($path in ($reference.Keys | Sort-Object)) {
        if (-not $candidate.ContainsKey($path)) { $missing.Add($path); continue }
        if ($reference[$path].sha256 -ne $candidate[$path].sha256) {
            $modified.Add(@{ path=$path; baselineSha256=$reference[$path].sha256; candidateSha256=$candidate[$path].sha256 })
        } else { $unchanged++ }
    }
    foreach ($path in ($candidate.Keys | Sort-Object)) {
        if (-not $reference.ContainsKey($path)) { $added.Add($path) }
    }
    return @{
        unchangedCount=$unchanged
        modified=@($modified.ToArray())
        missing=@($missing.ToArray())
        added=@($added.ToArray())
    }
}

$baselineInventory = Read-AuditInventory $baselineRoot
$masterInventory = Read-AuditInventory $masterRoot
$workInventory = Read-AuditInventory $workRoot
$masterComparison = Compare-AuditInventory $baselineInventory $masterInventory
$workComparison = Compare-AuditInventory $baselineInventory $workInventory
$artExtensions = @('.prefab','.mat','.png','.jpg','.jpeg','.tga','.psd','.fbx','.obj','.blend','.wav','.mp3','.ogg')
$modifiedOriginalArt = @($workComparison.modified | Where-Object {
    $_.path.StartsWith('Assets/') -and $artExtensions -contains [IO.Path]::GetExtension($_.path).ToLowerInvariant()
})
$assetCounts = @{}
foreach ($rootEntry in @(@{name='baseline';root=$baselineRoot},@{name='work';root=$workRoot})) {
    $assetFiles = @(Get-ChildItem -LiteralPath (Join-Path $rootEntry.root 'Assets') -File -Recurse)
    $assetCounts[$rootEntry.name] = @{
        prefab = @($assetFiles | Where-Object Extension -eq '.prefab').Count
        material = @($assetFiles | Where-Object Extension -eq '.mat').Count
        png = @($assetFiles | Where-Object Extension -eq '.png').Count
        allFiles = $assetFiles.Count
    }
}
$report = @{
    timestampUtc = [DateTime]::UtcNow.ToString('o')
    mode = 'Read-only SHA-256 source comparison; no Unity launch, no master mutation'
    baselinePath = $baselineRoot
    masterPath = $masterRoot
    workPath = $workRoot
    scopeFolders = $auditFolders
    scopeRootFiles = $auditRootFiles
    excluded = 'Library, Build, Logs, Temp, obj, UserSettings and docs/*logs or docs/*screenshots verification output; protected source otherwise compared byte-for-byte'
    inventoryCounts = @{baseline=$baselineInventory.Count;master=$masterInventory.Count;work=$workInventory.Count}
    masterSinceBaseline = $masterComparison
    workSinceBaseline = $workComparison
    modifiedOriginalArt = $modifiedOriginalArt
    assetCounts = $assetCounts
    noConcurrentMasterSourceChanges = ($masterComparison.modified.Count -eq 0 -and $masterComparison.missing.Count -eq 0 -and $masterComparison.added.Count -eq 0)
    noBaselineFilesMissingFromWork = ($workComparison.missing.Count -eq 0)
}
$reportPath = Join-Path $workRoot 'docs\stage4b-logs\delivery-preflight.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
@{
    reportPath=$reportPath
    baselineFiles=$baselineInventory.Count
    masterModified=$masterComparison.modified.Count
    masterMissing=$masterComparison.missing.Count
    masterAdded=$masterComparison.added.Count
    workModified=$workComparison.modified.Count
    workMissing=$workComparison.missing.Count
    workAdded=$workComparison.added.Count
    modifiedOriginalArt=$modifiedOriginalArt.Count
    assetCounts=$assetCounts
} | ConvertTo-Json -Depth 5
