param([ValidateSet('Author','Calibrate','Play','Commission','Build','Preview')][string]$Step='Play')
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence=Join-Path $project 'docs\floating'
[void][IO.Directory]::CreateDirectory($evidence)
$methods=@{
    Author='VintageJournal.Editor.FloatingAtelierSetup.Apply'
    Calibrate='VintageJournal.Editor.FloatingAtelierSetup.Calibrate'
    Play='VintageJournal.Editor.WorkspaceStage4BValidation.Begin'
    Commission='VintageJournal.Editor.CommissionValidation.Begin'
    Build='VintageJournal.Editor.DeskSceneBuilder.BuildWindows'
    Preview='VintageJournal.Editor.DaydreamBrightPass.Preview'
}
$argsEditor=@('-batchmode','-projectPath',$project,'-executeMethod',$methods[$Step],'-logFile',(Join-Path $evidence ($Step.ToLower()+'.log')))
if($Step -eq 'Play'){$argsEditor+='--workspace-validation'}
elseif($Step -eq 'Commission'){$argsEditor+='--commission-validation'}
else{$argsEditor+='-quit'}
$editor=Start-Process -FilePath 'E:\unity\Editor\Unity.exe' -ArgumentList $argsEditor -WindowStyle Hidden -Wait -PassThru
if($editor.ExitCode){throw "Unity failed with $($editor.ExitCode). See $evidence"}
if($Step -eq 'Play'){
    Copy-Item -LiteralPath (Join-Path $project 'docs\stage4b-logs\play-tests.json') -Destination (Join-Path $evidence 'play.json')
    Get-ChildItem (Join-Path $project 'docs\stage4b-screenshots') -Filter '*.png' | Copy-Item -Destination $evidence
}
if($Step -eq 'Commission'){Copy-Item -LiteralPath (Join-Path $project 'docs\commission\play-tests.json') -Destination (Join-Path $evidence 'commission.json')}
if($Step -eq 'Build'){Copy-Item -LiteralPath (Join-Path $project 'docs\logs\windows-build.txt') -Destination (Join-Path $evidence 'windows-build.txt')}
Write-Output ($Step+' completed')
