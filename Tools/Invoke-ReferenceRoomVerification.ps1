[CmdletBinding()]
param([ValidateSet('Scene','Data','Play','Build','Final')][string]$Step='Play',[string]$LegacySnapshot,[string]$UnityPath='E:\unity\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$evidence=Join-Path $project 'docs\reference-room'
[void][IO.Directory]::CreateDirectory($evidence)
if($Step -eq 'Play' -and (Test-Path -LiteralPath (Join-Path $evidence 'play\result.json'))){
 $archive=Join-Path $evidence ('attempts\'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
 & robocopy (Join-Path $evidence 'play') $archive /E /COPY:DAT /R:1 /W:1 /NJH /NJS /NFL /NDL /NP
 if($LASTEXITCODE -gt 7){throw 'Cannot preserve previous evidence.'}
 Copy-Item -LiteralPath (Join-Path $evidence 'play-editor.log') -Destination (Join-Path $archive 'play-editor.log') -ErrorAction SilentlyContinue
}
$methods=@{Scene='ReferenceRoomValidation.Prepare';Data='ReferenceRoomValidation.Data';Play='ReferenceRoomValidation.Begin';Build='DeskSceneBuilder.BuildWindows';Final='ReferenceRoomValidation.Final'}
$arguments=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',('VintageJournal.Editor.'+$methods[$Step]),'-logFile',('"'+(Join-Path $evidence ($Step.ToLower()+'-editor.log'))+'"'))
if($Step -eq 'Play'){$arguments+=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+[Guid]::NewGuid().ToString('N')))}else{$arguments+='-quit'}
if($Step -eq 'Final'){if(!$LegacySnapshot){throw 'Explicit recovery snapshot required.'};$arguments+=@('--usability-legacy-snapshot',('"'+[IO.Path]::GetFullPath($LegacySnapshot)+'"'))}
$game=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Unity '+$Step+' PID='+$game.Id);$game.WaitForExit();if($game.ExitCode -ne 0){throw ('Unity '+$Step+' failed: '+$game.ExitCode)}
Write-Output ($Step+' completed; inspect actual evidence.')
