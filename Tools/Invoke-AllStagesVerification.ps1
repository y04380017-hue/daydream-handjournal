[CmdletBinding()]
param([ValidateSet('Data','Play','Build')][string]$Step='Play',[string]$UnityPath='E:\unity\Editor\Unity.exe',[string]$HttpFixtureRoot)
$ErrorActionPreference='Stop'
$allProject=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allEvidence=Join-Path $allProject 'docs\all-stages'
[void][IO.Directory]::CreateDirectory($allEvidence)
$allMethod=@{Data='AtelierFullValidation.Data';Play='AtelierFullValidation.Begin';Build='DeskSceneBuilder.BuildWindows'}
$allArgs=@('-batchmode','-projectPath',('"'+$allProject+'"'),'-executeMethod',('VintageJournal.Editor.'+$allMethod[$Step]),'-logFile',('"'+(Join-Path $allEvidence ($Step.ToLower()+'-editor.log'))+'"'))
if($Step -eq 'Play'){
    if(!$HttpFixtureRoot){$HttpFixtureRoot=Join-Path $allProject '.verification-data\http'}
    & (Join-Path $PSScriptRoot 'Start-AtelierSocialService.ps1') -Port 18748 -DataDirectory $HttpFixtureRoot
    if(!(Test-Path -LiteralPath (Join-Path $HttpFixtureRoot 'moderator-key.txt'))){throw 'The running test service uses another fixture directory; supply its explicit -HttpFixtureRoot.'}
    $allArgs+=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+[Guid]::NewGuid().ToString('N')),'--dream-night','--atelier-http-fixture',('"'+$HttpFixtureRoot+'"'))
}else{$allArgs+='-quit'}
$allProcess=Start-Process -FilePath $UnityPath -ArgumentList $allArgs -WindowStyle Hidden -PassThru
[pscustomobject]@{ProcessId=$allProcess.Id;Step=$Step;Log=(Join-Path $allEvidence ($Step.ToLower()+'-editor.log'))}
