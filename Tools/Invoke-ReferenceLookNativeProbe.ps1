[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{32}$')][string]$RunId,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$ExecutablePath=(Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe')
)
# Real pointer movement and F8/ESC keys only. F8 observes camera state and never performs a gameplay action.
$ErrorActionPreference='Stop'
if(@(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue).Count){throw 'Another demo is running; leave it untouched.'}
$allRunId=([Guid]::ParseExact($RunId,'N')).ToString('N')
$allExe=(Resolve-Path -LiteralPath $ExecutablePath).Path
if([IO.Path]::GetFileName($allExe) -ne 'VintageJournalDesk.exe'){throw 'Expected VintageJournalDesk.exe.'}
$allOutput=[IO.Path]::GetFullPath($EvidenceDirectory)
[void][IO.Directory]::CreateDirectory($allOutput)
$allAttempt=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$allLog=Join-Path $allOutput ('player-'+$allAttempt+'.log')
$allPointerLog=Join-Path $allOutput ('pointer-'+$allAttempt+'.jsonl')
$allReportPath=Join-Path $allOutput 'result.json'
if(Test-Path -LiteralPath $allReportPath){Copy-Item -LiteralPath $allReportPath -Destination (Join-Path $allOutput ('result-before-'+$allAttempt+'.json'))}
$allSavePath=Join-Path ([Environment]::GetFolderPath('UserProfile')) ('AppData\LocalLow\Afternoon Paper Studio\午后手账 · Vintage Journal Desk\DeskRevisionValidation\'+$allRunId+'\journal-v1.json')
if(!(Test-Path -LiteralPath $allSavePath -PathType Leaf)){throw 'Requires an existing GUID-isolated runtime probe save; default saves will not be used.'}
$allChecks=[Collections.Generic.List[object]]::new();$allScreenshots=[Collections.Generic.List[string]]::new()
$lookObservations=[Collections.Generic.List[object]]::new()
$allPlayer=$null;$allWindow=[IntPtr]::Zero;$allInputSent=$false;$allStatus='FAIL';$allFailure=$null
$lookOrigin=$null;$lookBack=$null;$lookReset=$null

# Load reviewed Win32 protection/functions by AST; never execute the shared helper's test body.
$sharedPath=Join-Path $PSScriptRoot 'Invoke-AllStagesNativeProbe.ps1'
$parseErrors=$null;$sharedAst=[System.Management.Automation.Language.Parser]::ParseFile($sharedPath,[ref]$null,[ref]$parseErrors)
if($parseErrors){throw 'Shared native helper has parse errors.'}
$interop=$sharedAst.FindAll({param($a) $a -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $a.Value.Contains('public static class AllStagesNative')},$true)|Select-Object -First 1
if(!$interop){throw 'Shared native interop declaration was not found.'}
Add-Type -AssemblyName System.Drawing
if(-not('AllStagesNative' -as [type])){Add-Type -TypeDefinition $interop.Value}
foreach($name in @('Read-AllLog','Add-AllCheck','Wait-AllLog','Confirm-AllWindow','Focus-AllWindow','Get-AllClient','Move-AllPointer','Click-All','Key-All','Capture-All','Click-AllVirtual')){
    $definition=$sharedAst.FindAll({param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $name},$true)|Select-Object -First 1
    if(!$definition){throw ('Shared native function missing: '+$name)}
    . ([ScriptBlock]::Create($definition.Extent.Text))
}
[void][AllStagesNative]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][AllStagesNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
function Get-LookMode {
    Confirm-AllWindow;$offset=(Read-AllLog).Length
    [AllStagesNative]::Key(0x77,$true);$script:allInputSent=$true
    try{Start-Sleep -Milliseconds 35}finally{[AllStagesNative]::Key(0x77,$false)}
    [void](Wait-AllLog '\[WorkspaceInput\] Mode ' 5000 $offset)
    $new=(Read-AllLog).Substring($offset)
    $rows=@([regex]::Matches($new,'(?m)^\[WorkspaceInput\] Mode (\{[^\r\n]+\})\s*$'))
    if(!$rows.Count){throw 'F8 camera diagnostics were not produced.'}
    $mode=$rows[$rows.Count-1].Groups[1].Value|ConvertFrom-Json
    foreach($field in @('seatedYaw','seatedPitch','cameraPosition','forwardFacing','mouseLookEnabled')){
        if($mode.PSObject.Properties.Name -notcontains $field){throw ('Current build lacks required camera observation: '+$field)}
    }
    $lookObservations.Add([ordered]@{frame=$mode.frame;yaw=$mode.seatedYaw;pitch=$mode.seatedPitch;position=$mode.cameraPosition;forwardFacing=$mode.forwardFacing;mouseLookEnabled=$mode.mouseLookEnabled})
    return $mode
}
function Get-LookDistance([object]$A,[object]$B){return [Math]::Sqrt([Math]::Pow(($A.x-$B.x),2)+[Math]::Pow(($A.y-$B.y),2)+[Math]::Pow(($A.z-$B.z),2))}
function Wait-LookBack {
    $clock=[Diagnostics.Stopwatch]::StartNew();$leftObserved=$false
    while($clock.Elapsed.TotalSeconds -lt 8){
        # Sustained edge input must remain real and uninterrupted throughout the wait.
        # Observe only; never move the cursor back against a person's input.
        Confirm-AllWindow
        $lookClient=Get-AllClient
        $lookExpectedX=[int]($lookClient.X+.0025*$lookClient.W)
        $lookExpectedY=[int]($lookClient.Y+.52*$lookClient.H)
        $lookPointer=[AllStagesNative+POINT]::new()
        if(![AllStagesNative]::GetPhysicalCursorPos([ref]$lookPointer)){throw 'Cannot verify the physical pointer; native look test stopped.'}
        if([Math]::Abs($lookPointer.X-$lookExpectedX) -gt 4 -or [Math]::Abs($lookPointer.Y-$lookExpectedY) -gt 4){
            throw ('External pointer movement detected during native look; expected=('+ $lookExpectedX+','+$lookExpectedY+'), actual=('+ $lookPointer.X+','+$lookPointer.Y+'). Test stopped without moving the cursor.')
        }
        $mode=Get-LookMode
        if([double]$mode.seatedYaw -lt -20){$leftObserved=$true}
        if($leftObserved -and [Math]::Abs([double]$mode.seatedYaw) -ge 155 -and [Math]::Abs([double]$mode.seatedYaw) -le 179.99){return $mode}
        Start-Sleep -Milliseconds 60
    }
    throw 'Left-edge movement did not produce an observed left turn followed by a back-facing yaw.'
}
try{
    $arguments=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+$allRunId),'--workspace-native-validation',
        '-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$allLog+'"'))
    $allPlayer=Start-Process -FilePath $allExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $ready=[Diagnostics.Stopwatch]::StartNew()
    while($ready.Elapsed.TotalSeconds -lt 45){
        $allPlayer.Refresh();if($allPlayer.HasExited){throw 'Own test player exited during startup.'}
        $allWindow=[AllStagesNative]::FindWindowForProcess([uint32]$allPlayer.Id)
        if($allWindow -ne [IntPtr]::Zero -and (Read-AllLog) -match '\[PersonalStudio\] Ready'){break}
        Start-Sleep -Milliseconds 150
    }
    if($allWindow -eq [IntPtr]::Zero){throw 'No own Unity window was found.'}
    Focus-AllWindow
    $offset=(Read-AllLog).Length;Click-AllVirtual 410 679
    [void](Wait-AllLog '\[Desk\] UI:Begin' 10000 $offset)
    Move-AllPointer .5 .52;Start-Sleep -Milliseconds 400
    $lookOrigin=Get-LookMode
    Add-AllCheck 'Native normal entry begins in forward seated pose' ($lookOrigin.mouseLookEnabled -and $lookOrigin.forwardFacing -and [Math]::Abs([double]$lookOrigin.seatedYaw) -lt .1) 'F8 actual camera fields; isolated save only.'
    Capture-All 'look-forward-before'
    Move-AllPointer .0025 .52
    [void](Wait-LookBack)
    # Stop at a neutral pointer position before observing the exact final angle; no camera API is invoked.
    Move-AllPointer .5 .52;Start-Sleep -Milliseconds 100
    $lookBack=Get-LookMode
    Add-AllCheck 'Native left edge continuously turns to the back' ([Math]::Abs([double]$lookBack.seatedYaw) -ge 150 -and !$lookBack.forwardFacing) ('yaw='+$lookBack.seatedYaw+' degrees; native edge movement only.')
    Add-AllCheck 'Native back turn does not move the seated viewpoint' ((Get-LookDistance $lookOrigin.cameraPosition $lookBack.cameraPosition) -lt .0001) 'Runtime world position remained identical within 0.1 mm.'
    Capture-All 'look-back-pink-sky'
    # Return while the cursor is at an active edge. It must remain forward rather than immediately turning again.
    Move-AllPointer .0025 .52;Key-All 0x1B
    Start-Sleep -Milliseconds 1300
    $lookReset=Get-LookMode
    Add-AllCheck 'Native ESC returns forward while the mouse stays at the edge' ($lookReset.forwardFacing -and [Math]::Abs([double]$lookReset.seatedYaw) -lt .1 -and [Math]::Abs([double]$lookReset.seatedPitch) -lt .1 -and (Get-LookDistance $lookOrigin.cameraPosition $lookReset.cameraPosition) -lt .0001) 'ESC reset and neutral-pointer rearm prevent immediate edge rotation.'
    Start-Sleep -Milliseconds 350;$still=Get-LookMode
    Add-AllCheck 'Forward reset stays stable until the pointer leaves the edge' ([Math]::Abs([double]$still.seatedYaw) -lt .1) 'No inertial or repeated edge look after reset.'
    Move-AllPointer .5 .52;Capture-All 'look-forward-after-esc'
    $exceptions=@([regex]::Matches((Read-AllLog),'(?m)^(?:NullReferenceException|[^\r\n]*Exception:)[^\r\n]*$'))
    Add-AllCheck 'No player exceptions during native seated look' ($exceptions.Count -eq 0) ($exceptions.Count.ToString()+' exception lines')
    $allStatus='PASS'
}catch{
    $allFailure=$_.Exception.Message;$allChecks.Add([ordered]@{name='Native seated look sequence';passed=$false;evidence=$allFailure})
    try{if($allPlayer -and !$allPlayer.HasExited -and [AllStagesNative]::GetForegroundWindow() -eq $allWindow){Capture-All 'failure-ui'}}catch{}
}finally{
    if($allPlayer){
        $allPlayer.Refresh()
        if(!$allPlayer.HasExited){
            $own=[AllStagesNative]::FindWindowForProcess([uint32]$allPlayer.Id)
            if($own -ne [IntPtr]::Zero){[uint32]$owner=0;[void][AllStagesNative]::GetWindowThreadProcessId($own,[ref]$owner);if($owner -eq [uint32]$allPlayer.Id){[void][AllStagesNative]::RequestClose($own)}}
            if(!$allPlayer.WaitForExit(15000)){$allStatus='FAIL';$allFailure=($allFailure+' Own player did not close normally; left running.').Trim()}
        }
        if($allPlayer.HasExited -and $allPlayer.ExitCode -ne 0){$allStatus='FAIL';$allFailure=($allFailure+' Exit code '+$allPlayer.ExitCode).Trim()}
    }
    [ordered]@{timestampUtc=[DateTime]::UtcNow.ToString('O');result=$allStatus;failure=$allFailure;runId=$allRunId
        nativeInput=$allInputSent;runtimeGameplayActionsInvoked=$false;diagnostics='F8 observes actual yaw/pitch/world position; only native pointer and ESC change the view.'
        executable=$allExe;processId=if($allPlayer){$allPlayer.Id}else{$null};playerLog=$allLog;pointerDiagnostics=$allPointerLog
        checks=$allChecks;observations=$lookObservations;screenshots=$allScreenshots
        notValidated=@('Every material and drawer drag under native look','Rear pixel colour is visually reviewed and GPU-tested by the runtime probe','Hardware or Chinese IME text input')}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $allReportPath -Encoding UTF8
    Write-Output ('Native reference look '+$allStatus+'; report='+$allReportPath)
}
if($allStatus -ne 'PASS'){throw ('Native reference look failed: '+$allFailure)}
