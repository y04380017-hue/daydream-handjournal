[CmdletBinding()]
param([ValidateSet('Scene','Data','Play','Build','Final')][string]$Step='Play',[string]$UnityPath='E:\unity\Editor\Unity.exe',[string]$LegacySnapshot)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence=Join-Path $project 'docs\desk-usability'
[void][IO.Directory]::CreateDirectory($evidence)
if($Step -eq 'Play' -and (Test-Path -LiteralPath (Join-Path $evidence 'play\result.json'))){
 $archive=Join-Path $evidence ('attempts\play-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
 & robocopy (Join-Path $evidence 'play') $archive /E /COPY:DAT /R:1 /W:1 /NJH /NJS /NFL /NDL /NP
 if($LASTEXITCODE -gt 7){throw 'Previous Play evidence could not be preserved.'}
 Copy-Item -LiteralPath (Join-Path $evidence 'play-editor.log') -Destination (Join-Path $archive 'play-editor.log') -ErrorAction SilentlyContinue
}
$methods=@{Scene='DeskUsabilityValidation.Prepare';Data='DeskUsabilityValidation.Data';Play='DeskUsabilityValidation.Begin';Build='DeskSceneBuilder.BuildWindows';Final='DeskUsabilityValidation.FinalCandidate'}
$arguments=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',('VintageJournal.Editor.'+$methods[$Step]),'-logFile',('"'+(Join-Path $evidence ($Step.ToLower()+'-editor.log'))+'"'))
if($Step -eq 'Play'){$arguments+=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+[Guid]::NewGuid().ToString('N')))}else{$arguments+='-quit'}
if($Step -eq 'Final'){
 if(!$LegacySnapshot){throw 'Final requires an explicit backed-up legacy snapshot; never substitutes the default journal.'}
 $arguments+=@('--usability-legacy-snapshot',('"'+[IO.Path]::GetFullPath($LegacySnapshot)+'"'))
}
$process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('PID='+$process.Id+'; step='+$Step+'; log='+ (Join-Path $evidence ($Step.ToLower()+'-editor.log')))
$process.WaitForExit()
if($process.ExitCode -ne 0){throw ('Unity '+$Step+' failed: '+$process.ExitCode)}
Write-Output ($Step+' finished; inspect the actual reports for coverage.')
