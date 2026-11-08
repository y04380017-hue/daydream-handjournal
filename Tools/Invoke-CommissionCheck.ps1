param([ValidateSet('Setup','Data','Play','Regression','Build')][string]$Step='Play')
# Commission-letter prototype: scene authoring, data checks, Play loop, Stage04-B regression and Windows build.
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence=Join-Path $project 'docs\commission'
[IO.Directory]::CreateDirectory($evidence)|Out-Null
function Invoke-Editor([string]$method,[string]$log,[string[]]$flags){
    $arguments=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-logFile',('"'+(Join-Path $evidence $log)+'"'))+$flags
    $editor=Start-Process -FilePath 'E:\unity\Editor\Unity.exe' -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if($editor.ExitCode){throw "Unity exited $($editor.ExitCode), see docs\commission\$log"}
}
# Earlier-stage suites rewrite their own evidence folders; keep those historical files and file new results under docs\commission.
$historical=@('docs\logs','docs\stage2-logs','docs\stage3-logs','docs\stage4b-logs','docs\stage4b-screenshots')
$snapshot=Join-Path $env:TEMP ('commission-evidence-'+[guid]::NewGuid().ToString('N'))
foreach($folder in $historical){$source=Join-Path $project $folder;if(Test-Path $source){Copy-Item $source (Join-Path $snapshot $folder) -Recurse -Force}}
try{
switch($Step){
    'Setup'      {Invoke-Editor 'VintageJournal.Editor.CommissionDeskSetup.Apply' 'setup.log' @('-quit')}
    'Data'       {Invoke-Editor 'VintageJournal.Editor.CommissionDataTests.Run' 'data.log' @('-quit')}
    'Play'       {Invoke-Editor 'VintageJournal.Editor.CommissionValidation.Begin' 'play.log' @('--commission-validation')}
    'Regression' {
        Invoke-Editor 'VintageJournal.Editor.WorkspaceStage4BValidation.Begin' 'stage4b-regression-play.log' @('--workspace-validation')
        Copy-Item (Join-Path $project 'docs/stage4b-logs/play-tests.json') (Join-Path $evidence 'stage4b-regression-play.json') -Force
    }
    'Build'      {Invoke-Editor 'VintageJournal.Editor.DeskSceneBuilder.BuildWindows' 'build.log' @('-quit')}
}
}
finally{
    $regression=Join-Path $evidence 'regression';[IO.Directory]::CreateDirectory($regression)|Out-Null
    foreach($folder in $historical){
        $live=Join-Path $project $folder;$saved=Join-Path $snapshot $folder
        if(!(Test-Path $saved)){continue}
        Get-ChildItem $live -File|ForEach-Object{
            $old=Join-Path $saved $_.Name
            if(!(Test-Path $old) -or (Get-FileHash $old).Hash -ne (Get-FileHash $_.FullName).Hash){Copy-Item $_.FullName (Join-Path $regression $_.Name) -Force}
            if(!(Test-Path $old)){Remove-Item $_.FullName}
        }
        Copy-Item (Join-Path $saved '*') $live -Recurse -Force
    }
    Remove-Item $snapshot -Recurse -Force -ErrorAction SilentlyContinue
}
