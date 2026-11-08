[CmdletBinding()]
param([string]$ExecutablePath=(Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe'))
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# This API probe sends no keyboard/mouse input and uses a fresh validation GUID.
# An existing player's different executable/save can remain open; native probes retain their strict guard.
$exe=(Resolve-Path -LiteralPath $ExecutablePath).Path
$run=[Guid]::NewGuid().ToString('N')
$output=Join-Path $project ('docs\desk-usability\player\'+$run)
[void][IO.Directory]::CreateDirectory($output)
if(-not ('DeskUsabilitySmokeWindow' -as [type])){Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class DeskUsabilitySmokeWindow {
 delegate bool Callback(IntPtr w,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr w,out uint id);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr w,int command);
 public static bool Show(uint own){bool shown=false;EnumWindows((w,p)=>{uint id;GetWindowThreadProcessId(w,out id);if(id==own){ShowWindow(w,4);shown=true;}return true;},IntPtr.Zero);return shown;}
}
'@}
$common=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+$run),'--desk-usability-probe',('"'+$output+'"'),'-screen-width','1280','-screen-height','720','-screen-fullscreen','0')
foreach($mode in @('first','restart')){
 $arguments=$common+@('-logFile',('"'+(Join-Path $output ($mode+'-player.log'))+'"'))
 if($mode -eq 'restart'){$arguments+='--desk-usability-restart'}
 $game=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
 $window=[Diagnostics.Stopwatch]::StartNew()
 while($window.Elapsed.TotalSeconds -lt 5){$game.Refresh();if($game.HasExited -or [DeskUsabilitySmokeWindow]::Show([uint32]$game.Id)){break};Start-Sleep -Milliseconds 100}
 if(!$game.WaitForExit(220000)){throw ('Own test player timed out; left running for inspection. PID='+$game.Id)}
 if($game.ExitCode -ne 0){throw ($mode+' usability probe failed: '+$output)}
 $name=if($mode -eq 'restart'){'restart.json'}else{'result.json'}
 $result=Get-Content -LiteralPath (Join-Path $output $name) -Raw|ConvertFrom-Json
 if($result.passed -ne $true){throw ('Probe report did not pass: '+$name)}
 Write-Output ($mode+' Windows usability PASS: '+$result.checks.Count+' checks')
}
Write-Output ('RUN_ID='+$run)
Write-Output ('OUTPUT='+$output)
