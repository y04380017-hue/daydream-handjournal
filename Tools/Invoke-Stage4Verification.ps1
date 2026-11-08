[CmdletBinding()]
param(
    [ValidateSet('Scene','Data','Play','Build')][string]$Step='Play',
    [string]$UnityPath='E:\unity\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$stage4ProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage4LogRoot=Join-Path $stage4ProjectRoot 'docs\stage4-logs'
New-Item -ItemType Directory -Path $stage4LogRoot -Force|Out-Null
$stage4Methods=@{
    Scene='VintageJournal.Editor.PhysicalWorkspaceIntegration.BuildAndConnect'
    Data='VintageJournal.Editor.PhysicalWorkspaceDataTests.RunFromMenu'
    Play='VintageJournal.Editor.PhysicalWorkspaceValidation.Begin'
    Build='VintageJournal.Editor.DeskSceneBuilder.BuildWindows'
}
$stage4EditorArgs=@('-batchmode','-projectPath',('"'+$stage4ProjectRoot+'"'),'-executeMethod',$stage4Methods[$Step],'-logFile',('"'+(Join-Path $stage4LogRoot ($Step.ToLower()+'-editor.log'))+'"'))
if($Step -eq 'Play'){$stage4EditorArgs+='--physical-validation'}else{$stage4EditorArgs+='-quit'}
$stage4Editor=Start-Process -FilePath $UnityPath -ArgumentList $stage4EditorArgs -WindowStyle Hidden -PassThru -Wait
if($stage4Editor.ExitCode -ne 0){throw "Unity $Step failed with exit code $($stage4Editor.ExitCode). See $stage4LogRoot."}
Write-Output "Unity $Step completed; inspect reports in $stage4LogRoot. Play reports distinguish automated checks from manual acceptance."
