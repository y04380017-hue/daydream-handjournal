[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$probeProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (Get-Process VintageJournalDesk -ErrorAction SilentlyContinue) { throw 'Another demo is running; leave it untouched.' }
$probeRun = [Guid]::NewGuid().ToString('N')
$probeOutput = Join-Path $probeProject ('docs\night-gameplay\player\' + $probeRun)
[void][IO.Directory]::CreateDirectory($probeOutput)
if (-not ('NightSmokeWindow' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NightSmokeWindow {
 public delegate bool EnumProc(IntPtr window,IntPtr param);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
 public static bool Show(uint pid){bool found=false;EnumWindows((w,p)=>{uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid){ShowWindow(w,4);found=true;}return true;},IntPtr.Zero);return found;}
}
'@
}
$probeCommon = @('--night-gameplay-validation', '--desk-revision-validation', ('--revision-run=' + $probeRun),
    '--night-gameplay-player-probe', ('"' + $probeOutput + '"'), '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0')
foreach ($mode in @('first', 'reload')) {
    $probeArguments = $probeCommon + @('-logFile', ('"' + (Join-Path $probeOutput ($mode + '-player.log')) + '"'))
    if ($mode -eq 'reload') { $probeArguments += '--probe-reload' }
    $probeProcess = Start-Process -FilePath (Join-Path $probeProject 'Build\Windows\VintageJournalDesk.exe') -ArgumentList $probeArguments -WindowStyle Hidden -PassThru
    # A hidden Unity window has no rendered backbuffer. Show only this test PID without stealing foreground.
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 5) {
        $probeProcess.Refresh()
        if ($probeProcess.HasExited -or [NightSmokeWindow]::Show($probeProcess.Id)) { break }
        Start-Sleep -Milliseconds 50
    }
    $probeProcess.WaitForExit()
    if ($probeProcess.ExitCode -ne 0) { throw "$mode probe failed ($($probeProcess.ExitCode)); inspect $probeOutput" }
    Write-Output ($mode + ' process PASS')
}
Write-Output ('OUTPUT=' + $probeOutput)
