[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable,[ValidateSet('Benchmark','Exercise','Restart')][string]$Mode='Exercise',[Parameter(Mandatory=$true)][string]$Record)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
if([IO.Path]::GetFileName($exe) -ne 'VintageJournalDesk.exe'){throw 'Expected VintageJournalDesk.exe'}
if([Diagnostics.Process]::GetProcessesByName('VintageJournalDesk').Count){throw 'A player is already in use.'}
$folder=[IO.Path]::GetFullPath($Record)
[IO.Directory]::CreateDirectory($folder)|Out-Null
$logFile=Join-Path $folder 'player.log'
$argsPlayer=@('-screen-fullscreen','0','-screen-width','1600','-screen-height','900','--workspace-native-validation','-logFile',('"'+$logFile+'"'))
if($Mode -eq 'Benchmark'){$argsPlayer+=@('--desk-probe-output',('"'+(Join-Path $folder 'frames.json')+'"'))}
# OS-input regression requires a visible client window. The probe verifies process, foreground and pointer before clicks.
$p=Start-Process -FilePath $exe -ArgumentList $argsPlayer -WorkingDirectory (Split-Path -Parent $exe) -PassThru
try {
    & (Join-Path $PSScriptRoot 'Invoke-WorkspaceStage4BGuiProbe.ps1') -ExecutablePath $exe -PlayerLog $logFile -OutputDirectory $folder -Mode $Mode -ExpectedStatePath (Join-Path (Split-Path -Parent $folder) 'native-state.json')
} finally {
    if(!$p.HasExited){[void]$p.CloseMainWindow();if(!$p.WaitForExit(18000)){throw "Measured player did not exit cleanly: PID $($p.Id)"}}
}

