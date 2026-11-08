param([ValidateSet('Install','Build','Player','Play','Regression','Tune')] [string]$Mode, [string]$Unity='E:\unity\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$docs=Join-Path $project 'docs\luminous-post'
if($Mode -eq 'Player'){
    $folder=Join-Path $docs 'player';[void][IO.Directory]::CreateDirectory($folder)
    if(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue){throw 'Another demo is running.'}
    $argv=@('--desk-revision-validation',('--revision-run='+[Guid]::NewGuid().ToString('N')),'--desk-revision-probe',('"'+$folder+'"'),'-screen-width','1920','-screen-height','1080','-screen-fullscreen','0','-logFile',('"'+(Join-Path $folder 'player.log')+'"'))
    $p=Start-Process -FilePath (Join-Path $project 'Build\Windows\VintageJournalDesk.exe') -ArgumentList $argv -PassThru -Wait
} else {
    $methods=@{Install='LuminousAtelierSetup.Install';Tune='LuminousAtelierSetup.Tune';Build='DeskSceneBuilder.BuildWindows';Play='DeskRevisionValidation.Begin';Regression='WorkspaceStage4BValidation.Begin'}
    $argv=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',('VintageJournal.Editor.'+$methods[$Mode]),'-logFile',('"'+(Join-Path $docs ($Mode+'.log'))+'"'))
    if($Mode -eq 'Play'){$argv+=@('--desk-revision-validation','--luminous-validation')}elseif($Mode -eq 'Regression'){$argv+='--workspace-validation'}else{$argv+='-quit'}
    $p=Start-Process -FilePath $Unity -ArgumentList $argv -WindowStyle Hidden -PassThru -Wait
}
exit $p.ExitCode

