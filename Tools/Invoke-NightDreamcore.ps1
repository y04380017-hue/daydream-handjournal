param([ValidateSet('Install','Remove','Check','PlayerCheck')][string]$Step='Check',[string]$Name='play')
# Night dreamcore (2026-11-07): incremental install/remove, and the measured grayscale check in Play Mode or the Windows player.
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out=Join-Path $project "docs\night-dreamcore\$Name"
[IO.Directory]::CreateDirectory($out)|Out-Null
function Editor([string]$method,[string]$log,[string[]]$flags){
    $a=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-logFile',('"'+(Join-Path $out $log)+'"'))+$flags
    $p=Start-Process -FilePath 'E:\unity\Editor\Unity.exe' -ArgumentList $a -WindowStyle Hidden -Wait -PassThru
    "Unity exit $($p.ExitCode) ($log)"
}
switch($Step){
    'Install' { Editor 'VintageJournal.Editor.NightDreamcoreSetup.Install' 'install.log' @('-quit') }
    'Remove'  { Editor 'VintageJournal.Editor.NightDreamcoreSetup.Remove' 'remove.log' @('-quit') }
    'Check'   { Editor 'VintageJournal.Editor.NightDreamcoreCheck.Begin' 'check.log' @('--commission-validation','--night-dreamcore-check',('"'+$out+'"')) }
    'PlayerCheck' {
        $exe=Join-Path $project 'Build\Windows\VintageJournalDesk.exe'
        $p=Start-Process $exe -PassThru -ArgumentList @('--commission-validation','--night-dreamcore-check',('"'+$out+'"'),'-screen-width','1920','-screen-height','1080','-screen-fullscreen','0','-logFile',('"'+(Join-Path $out 'player.log')+'"'))
        if(-not $p.WaitForExit(240000)){ $p.Kill(); 'player killed after 240 s' } else { "player exit $($p.ExitCode)" }
    }
}
