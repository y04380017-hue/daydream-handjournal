param([ValidateSet('Exercise','Restart')][string]$Mode='Exercise')
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output=Join-Path $project ('docs\wood-rack\native-'+$Mode.ToLower())
[void][IO.Directory]::CreateDirectory($output)
$testSave=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Afternoon Paper Studio\午后手账 · Vintage Journal Desk\Stage4BNativeValidation'
if($Mode -eq 'Exercise' -and (Test-Path -LiteralPath $testSave)){
    $resolved=(Resolve-Path -LiteralPath $testSave).Path
    if($resolved -ne [IO.Path]::GetFullPath($testSave) -or (Split-Path $resolved -Leaf) -ne 'Stage4BNativeValidation'){throw 'Unexpected isolated save path'}
    $archive=Join-Path $resolved ('wood-rack-before-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    [void][IO.Directory]::CreateDirectory($archive)
    foreach($file in Get-ChildItem -LiteralPath $resolved -File){Move-Item -LiteralPath $file.FullName -Destination (Join-Path $archive $file.Name)}
}
if(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue){throw 'Another demo is running; will not interrupt it.'}
$exe=Join-Path $project 'Build\Windows\VintageJournalDesk.exe'
$log=Join-Path $output 'player.log'
$player=Start-Process -FilePath $exe -ArgumentList @('--workspace-native-validation','-screen-width','1600','-screen-height','900','-screen-fullscreen','0','-logFile',('"'+$log+'"')) -PassThru
try{
    $ready=[Diagnostics.Stopwatch]::StartNew()
    while($ready.Elapsed.TotalSeconds -lt 40){$player.Refresh();if($player.HasExited){throw 'Player exited during startup'};if($player.MainWindowHandle -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 250}
    & (Join-Path $PSScriptRoot 'Invoke-WorkspaceStage4BGuiProbe.ps1') -ExecutablePath $exe -PlayerLog $log -OutputDirectory $output -Mode $Mode -ExpectedStatePath (Join-Path $project 'docs\wood-rack\native-expected.json')
}
finally{
    $player.Refresh()
    if(!$player.HasExited){[void]$player.CloseMainWindow();if(!$player.WaitForExit(15000)){throw 'The tested player did not close normally.'}}
}
