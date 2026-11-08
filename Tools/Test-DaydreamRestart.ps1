param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Snapshot,[Parameter(Mandatory=$true)][string]$Evidence)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$record=(Resolve-Path -LiteralPath $Snapshot).Path
if([IO.Path]::GetFileName($exe) -ne 'VintageJournalDesk.exe' -or [IO.Path]::GetFileName((Split-Path -Parent $record)) -ne 'Stage4BNativeValidation' -or [IO.Path]::GetFileName($record) -notlike 'journal-v1.json.before-*'){throw 'Expected an isolated recorded native run.'}
$target=Join-Path (Split-Path -Parent $record) 'journal-v1.json'
$previous=[IO.File]::ReadAllBytes($target)
$previousBackup=if(Test-Path -LiteralPath ($target+'.backup')){[IO.File]::ReadAllBytes($target+'.backup')}
[IO.Directory]::CreateDirectory($Evidence)|Out-Null
$log=Join-Path $Evidence 'restart-player.log'
if(Test-Path -LiteralPath $log){throw 'Use a fresh evidence directory.'}
try{
    Copy-Item -LiteralPath $record -Destination $target -Force
    $expected=Get-Content -LiteralPath $record -Raw|ConvertFrom-Json
    $p=Start-Process -FilePath $exe -ArgumentList @('-batchmode','-nographics','--workspace-native-validation','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(35)
    do {
        Start-Sleep -Milliseconds 400
        if(Test-Path -LiteralPath $log){$content=Get-Content -LiteralPath $log -Raw}
        if($content -match '\[Workspace\] Ready:'){break}
    }while(!$p.HasExited -and [DateTime]::UtcNow -lt $deadline)
    if($content -notmatch '\[Workspace\] Ready:'){throw 'Workspace failed to start in the fresh process.'}
    $m=[regex]::Match($content,'\[Journal\] Loaded:(\{[^\r\n]+\})')
    if(!$m.Success){throw 'No loaded document in the player log.'}
    $actual=$m.Groups[1].Value|ConvertFrom-Json
    if(($actual|ConvertTo-Json -Depth 65 -Compress) -cne ($expected|ConvertTo-Json -Depth 65 -Compress)){throw 'Reloaded document differs from the recorded snapshot.'}
    $result=[ordered]@{passed=$true;processId=$p.Id;executable=$exe;sourceSnapshot=$record;sha256=(Get-FileHash -LiteralPath $record).Hash;pageCount=$actual.pages.Count;openBooks=@($actual.craftWorkspace.books|Where-Object isOpen).Count;itemRecords=$actual.craftWorkspace.items.Count;scope='Fresh standalone process, full JSON compared after load; no GUI input, rendering or shutdown acceptance claimed.'}
    $result|ConvertTo-Json|Set-Content (Join-Path $Evidence 'restart.json') -Encoding utf8
    $result|ConvertTo-Json
}finally{
    if($p -and !$p.HasExited){if(!$p.CloseMainWindow() -or !$p.WaitForExit(2500)){$p.Kill();$p.WaitForExit()}}
    [IO.File]::WriteAllBytes($target,$previous)
    if($null -ne $previousBackup){[IO.File]::WriteAllBytes($target+'.backup',[byte[]]$previousBackup)}
}

