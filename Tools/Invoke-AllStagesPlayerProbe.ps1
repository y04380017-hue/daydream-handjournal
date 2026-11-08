[CmdletBinding()]
param([string]$HttpFixtureRoot)
$ErrorActionPreference='Stop'
$fullProject=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue){throw 'Another demo is running; leave it untouched.'}
if(!$HttpFixtureRoot){$HttpFixtureRoot=Join-Path $fullProject '.verification-data\http'}
& (Join-Path $PSScriptRoot 'Start-AtelierSocialService.ps1') -Port 18748 -DataDirectory $HttpFixtureRoot
if(!(Test-Path -LiteralPath (Join-Path $HttpFixtureRoot 'moderator-key.txt'))){throw 'Use the explicit fixture directory of the running test service.'}
if(-not('FullSmokeWindow' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FullSmokeWindow {
 public delegate bool Callback(IntPtr w,IntPtr p);
 [DllImport("user32.dll")]public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr w,out uint id);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr w,int mode);
 public static bool Show(uint id){bool ok=false;EnumWindows((w,p)=>{uint owner;GetWindowThreadProcessId(w,out owner);if(owner==id){ShowWindow(w,4);ok=true;}return true;},IntPtr.Zero);return ok;}
}
'@}
$fullRun=[Guid]::NewGuid().ToString('N')
$fullOutput=Join-Path $fullProject ('docs\all-stages\player\'+$fullRun)
[void][IO.Directory]::CreateDirectory($fullOutput)
$fullCommon=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+$fullRun),'--dream-night',
    '--atelier-full-probe',('"'+$fullOutput+'"'),'--atelier-http-fixture',('"'+$HttpFixtureRoot+'"'),'-screen-width','1280','-screen-height','720','-screen-fullscreen','0')
foreach($mode in @('first','restart')){
    if($mode -eq 'restart'){
        # Only the test server whose explicit store matches this fixture may be restarted.
        $pidPath=Join-Path $HttpFixtureRoot 'service-pid.txt'
        if(!(Test-Path -LiteralPath $pidPath)){throw 'Test server ownership PID is missing.'}
        $fixturePid=[int](Get-Content -LiteralPath $pidPath -Raw)
        $owned=Get-CimInstance Win32_Process -Filter ('ProcessId='+$fixturePid)
        if(!$owned -or $owned.Name -ne 'node.exe' -or $owned.CommandLine -notlike ('*'+[IO.Path]::GetFullPath($HttpFixtureRoot)+'*') -or $owned.CommandLine -notlike '*--port 18748*'){throw 'Fixture server identity changed; leave it untouched.'}
        Stop-Process -Id $fixturePid
        Start-Sleep -Milliseconds 200
        & (Join-Path $PSScriptRoot 'Start-AtelierSocialService.ps1') -Port 18748 -DataDirectory $HttpFixtureRoot
    }
    $fullArguments=$fullCommon+@('-logFile',('"'+(Join-Path $fullOutput ($mode+'-player.log'))+'"'))
    if($mode -eq 'restart'){$fullArguments+='--atelier-full-restart'}
    $fullProcess=Start-Process -FilePath (Join-Path $fullProject 'Build\Windows\VintageJournalDesk.exe') -ArgumentList $fullArguments -WindowStyle Hidden -PassThru
    $ready=[Diagnostics.Stopwatch]::StartNew()
    while($ready.Elapsed.TotalSeconds -lt 5){$fullProcess.Refresh();if($fullProcess.HasExited -or [FullSmokeWindow]::Show($fullProcess.Id)){break};Start-Sleep -Milliseconds 50}
    $fullProcess.WaitForExit()
    if($fullProcess.ExitCode -ne 0){throw ($mode+' full probe failed; inspect '+$fullOutput)}
    Write-Output ($mode+' full player PASS')
}
Write-Output ('OUTPUT='+$fullOutput)
