param([ValidateSet('AuthorPlay','Play','Build','Fixture')][string]$Step='Play')
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence=Join-Path $project 'docs\daydream'
[IO.Directory]::CreateDirectory($evidence)|Out-Null
function Invoke-Editor([string]$method,[string]$log,[string]$flag){
    $arguments=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-logFile',('"'+(Join-Path $evidence $log)+'"'),$flag)
    $editor=Start-Process -FilePath 'E:\unity\Editor\Unity.exe' -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if($editor.ExitCode){throw "Unity exited $($editor.ExitCode), see $log"}
}
if($Step -eq 'AuthorPlay'){Invoke-Editor 'VintageJournal.Editor.DaydreamRoom.Apply' 'author-final.log' '-quit'}
if($Step -eq 'AuthorPlay' -or $Step -eq 'Play'){
    Invoke-Editor 'VintageJournal.Editor.WorkspaceStage4BValidation.Begin' 'play.log' '--workspace-validation'
    Copy-Item (Join-Path $project 'docs/stage4b-logs/play-tests.json') (Join-Path $evidence 'play.json') -Force
    foreach($file in 'workspace-default.png','blank-binder.png','drawers-open.png','animal-stands-loaded.png','release-book-a-decorated.png'){
        Copy-Item -LiteralPath (Join-Path $project ('docs/stage4b-screenshots/'+$file)) -Destination (Join-Path $evidence $file) -Force
    }
}
if($Step -eq 'Build'){Invoke-Editor 'VintageJournal.Editor.DeskSceneBuilder.BuildWindows' 'build.log' '-quit'}
if($Step -eq 'Fixture'){Invoke-Editor 'VintageJournal.Editor.WorkspaceStage4BIntegration.PrepareNativeFixture' 'fixture.log' '-quit'}

