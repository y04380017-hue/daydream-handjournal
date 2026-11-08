[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{32}$')][string]$RunId,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$ExecutablePath=(Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe')
)
# Real Windows pointer/key events only. F8 reads runtime state; it never performs gameplay actions.
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
if(!(Test-Path -LiteralPath $allSavePath -PathType Leaf)){throw 'Requires an existing GUID-isolated usability save; default saves will not be used.'}
$allChecks=[Collections.Generic.List[object]]::new();$allScreenshots=[Collections.Generic.List[string]]::new()
$allPlayer=$null;$allWindow=[IntPtr]::Zero;$allInputSent=$false;$allStatus='FAIL';$allFailure=$null
$manual=[Collections.Generic.List[string]]::new()
$manual.Add('Native right-drag note transfer and every drawer/material combination remain separate manual checks.')
$manual.Add('Native TN inspection/page turns and daytime collectible clicks are covered by other runtime checks, not this native probe.')
$manual.Add('Hardware or Chinese IME text input is outside this pointer interaction probe.')

# Reuse the existing reviewed PID/focus/DPI/physical-cursor protection without executing its test body.
# Only constant C# interop and explicitly named function definitions from this local helper are loaded.
$sharedPath=Join-Path $PSScriptRoot 'Invoke-AllStagesNativeProbe.ps1'
$parseErrors=$null;$sharedAst=[System.Management.Automation.Language.Parser]::ParseFile($sharedPath,[ref]$null,[ref]$parseErrors)
if($parseErrors){throw 'Shared native helper has parse errors.'}
$interop=$sharedAst.FindAll({param($a) $a -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $a.Value.Contains('public static class AllStagesNative')},$true)|Select-Object -First 1
if(!$interop){throw 'Shared native interop declaration was not found.'}
Add-Type -AssemblyName System.Drawing
if(-not('AllStagesNative' -as [type])){Add-Type -TypeDefinition $interop.Value}
foreach($name in @('Read-AllLog','Read-AllSave','Add-AllCheck','Wait-AllLog','Wait-AllSave','Confirm-AllWindow','Focus-AllWindow','Get-AllClient','Move-AllPointer','Click-All','Key-All','Capture-All','Click-AllVirtual')){
    $definition=$sharedAst.FindAll({param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $name},$true)|Select-Object -First 1
    if(!$definition){throw ('Shared native function missing: '+$name)}
    . ([ScriptBlock]::Create($definition.Extent.Text))
}
[void][AllStagesNative]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][AllStagesNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
function Read-DeskRows([string]$Text,[string]$Kind){
    $pattern='(?m)^\[WorkspaceInput\] '+[regex]::Escape($Kind)+' (\{[^\r\n]+\})\s*$'
    foreach($match in [regex]::Matches($Text,$pattern)){ $match.Groups[1].Value|ConvertFrom-Json }
}
function Get-DeskSnapshot {
    $offset=(Read-AllLog).Length;Key-All 0x77
    [void](Wait-AllLog '\[WorkspaceInput\] Mode ' 6000 $offset)
    $text=(Read-AllLog).Substring($offset)
    $mode=@(Read-DeskRows $text 'Mode')|Select-Object -Last 1
    if(!$mode){throw 'F8 runtime mode diagnostics are missing; no pose assertions can be substituted with saved safe origins.'}
    return @{mode=$mode;targets=@(Read-DeskRows $text 'Target');drops=@(Read-DeskRows $text 'BookDrop')}
}
function Get-DeskItem([object]$Document,[string]$Id){return @($Document.craftWorkspace.items|Where-Object {$_.instanceId -eq $Id})|Select-Object -First 1}
function Get-DeskDistance([object]$A,[object]$B){return [Math]::Sqrt([Math]::Pow(($A.x-$B.x),2)+[Math]::Pow(($A.y-$B.y),2)+[Math]::Pow(($A.z-$B.z),2))}
function Click-DeskTarget([object]$Target,[int]$Button=0){
    if(!$Target){throw 'No collider-verified visible target.'}
    if($Button -eq 0){Click-All ([double]$Target.x) ([double]$Target.y);return}
    Move-AllPointer ([double]$Target.x) ([double]$Target.y);Confirm-AllWindow
    [AllStagesNative]::Mouse(8);$script:allInputSent=$true
    try{Start-Sleep -Milliseconds 90}finally{[AllStagesNative]::Mouse(16)}
    Start-Sleep -Milliseconds 250
}
function Drag-DeskTarget([object]$From,[object]$To,[int]$Button=0){
    Move-AllPointer ([double]$From.x) ([double]$From.y);Confirm-AllWindow
    $down=if($Button -eq 0){2}else{8};$up=if($Button -eq 0){4}else{16}
    [AllStagesNative]::Mouse($down);$script:allInputSent=$true
    try{
        Start-Sleep -Milliseconds 100
        for($step=1;$step -le 18;$step++){
            $f=$step/18.0
            Move-AllPointer ($From.x+($To.x-$From.x)*$f) ($From.y+($To.y-$From.y)*$f)
            Start-Sleep -Milliseconds 75
        }
    }finally{[AllStagesNative]::Mouse($up)}
    Start-Sleep -Milliseconds 250
}
function Select-DeskView([int]$Index,[string]$Name){
    $offset=(Read-AllLog).Length
    # Read the authored UI rectangles so a layout-only move cannot leave stale native click coordinates.
    $uiSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\Assets\Scripts\Interaction\DeskViewControls.cs'))
    $rectangles=@{}
    foreach($rectName in @('ToggleRect','MenuRect')){
        $match=[regex]::Match($uiSource,([regex]::Escape($rectName)+'\s*=\s*new\s+Rect\s*\((?<values>[^)]*)\)'))
        if(!$match.Success){throw ('Desk view rectangle declaration missing: '+$rectName)}
        $values=@($match.Groups['values'].Value.Split(',')|ForEach-Object {[double]::Parse($_.Trim().TrimEnd('f'),[Globalization.CultureInfo]::InvariantCulture)})
        if($values.Count -ne 4){throw ('Invalid desk view rectangle: '+$rectName)}
        $rectangles[$rectName]=$values
    }
    $toggle=$rectangles.ToggleRect;$menu=$rectangles.MenuRect
    Click-AllVirtual ($toggle[0]+$toggle[2]/2) ($toggle[1]+$toggle[3]/2)
    Click-AllVirtual ($menu[0]+$menu[2]/2) ($menu[1]+8+$Index*48+43/2)
    [void](Wait-AllLog ('\[Desk\] Camera:View:'+ [regex]::Escape($Name)) 8000 $offset)
    Start-Sleep -Milliseconds 1200
}
try{
    $initial=Read-AllSave
    if($initial.craftWorkspace.deskLayoutVersion -ne 1){throw 'Usability layout migration has not been run for this isolated save.'}
    $arguments=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+$allRunId),'--dream-night','--workspace-native-validation',
        '-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$allLog+'"'))
    $allPlayer=Start-Process -FilePath $allExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while($timer.Elapsed.TotalSeconds -lt 45){
        $allPlayer.Refresh();if($allPlayer.HasExited){throw 'Player exited before readiness.'}
        $allWindow=[AllStagesNative]::FindWindowForProcess([uint32]$allPlayer.Id)
        if($allWindow -ne [IntPtr]::Zero -and (Read-AllLog) -match '\[PersonalStudio\] Ready'){break}
        Start-Sleep -Milliseconds 150
    }
    if($allWindow -eq [IntPtr]::Zero){throw 'No test Unity window.'}
    Focus-AllWindow;Capture-All 'start-ui'
    $offset=(Read-AllLog).Length;Click-AllVirtual 410 679
    [void](Wait-AllLog '\[Desk\] UI:Begin' 10000 $offset)
    Add-AllCheck 'Native start button' $true 'Only the newly launched test PID received input.'
    Select-DeskView 1 'Overview'
    Add-AllCheck 'Native camera picker selects controlled overview' $true 'Mouse menu selection produced Camera:View:Overview.'
    $snapshot=Get-DeskSnapshot
    $book=@($snapshot.targets|Where-Object {$_.type -eq 'ReleaseBook'}|Sort-Object @{Expression={if($_.id -like '*ReleaseBook_B*'){0}else{1}}})|Select-Object -First 1
    if(!$book){throw 'No release-book collider is reachable in overview.'}
    $before=Get-DeskItem (Read-AllSave) $book.id
    if(!$before){throw 'Collider target has no unique persisted material identity.'}
    $releaseId=if($before.assetId -eq 'ReleaseBook_B'){'release-b'}elseif($before.assetId -eq 'ReleaseBook_A'){'release-a'}else{throw 'Unknown stable release-book asset ID.'}
    $beforePosition=$before.worldPosition
    Click-DeskTarget $book
    $selected=Get-DeskSnapshot
    Add-AllCheck 'Left click selects release book without lift or grab' ($selected.mode.selectedPhysical -eq $book.id -and !$selected.mode.physicalHeldItem -and (Get-DeskDistance $selected.mode.selectedPhysicalPosition $beforePosition) -lt .001) 'F8 actual runtime position matches original pose; no physical held item.'
    Capture-All 'release-selected-original-height'
    $client=Get-AllClient
    # An open book's reachable page can sit left of its logical root. Pick a candidate
    # whose cursor travel also exceeds the real drag threshold, rather than only root distance.
    $drop=@($selected.drops|Where-Object {$_.instanceId -eq $book.id -and
        [Math]::Sqrt([Math]::Pow(($_.x-$book.x)*$client.W,2)+[Math]::Pow(($_.y-$book.y)*$client.H,2)) -gt 18}|
        Sort-Object @{Expression={[Math]::Pow(($_.x-$book.x)*$client.W,2)+[Math]::Pow(($_.y-$book.y)*$client.H,2)};Descending=$true})|Select-Object -First 1
    if(!$drop){throw 'No distinct collider-validated book-sized tabletop drop was reported.'}
    Drag-DeskTarget $book $drop 0
    $placed=Wait-AllSave {param($d) $i=Get-DeskItem $d $book.id; $i -and [int]$i.owner -eq 1 -and (Get-DeskDistance $i.worldPosition $beforePosition) -gt .04} 'left drag commits the same release book to Desktop at a distinct pose' 12000
    $actual=Get-DeskItem $placed $book.id
    $after=Get-DeskSnapshot
    $closedAfterDrop=@($placed.craftWorkspace.books|Where-Object {$_.bookId -eq $releaseId -and !$_.isOpen}).Count -eq 1
    Add-AllCheck 'Native left drag places the same release book beside journal' (!$after.mode.physicalHeldItem -and $closedAfterDrop -and (Get-DeskDistance $after.mode.selectedPhysicalPosition $actual.worldPosition) -lt .003) ('same ID '+$book.id+'; closed and Desktop owner; moved='+[Math]::Round((Get-DeskDistance $actual.worldPosition $beforePosition),3)+'m')
    Capture-All 'release-dropped-beside-journal'
    # Re-query the exact collider after settling; a source-screen coordinate is never reused as a target.
    $bookNow=@($after.targets|Where-Object {$_.id -eq $book.id -and $_.type -eq 'ReleaseBook'})|Select-Object -First 1
    if(!$bookNow){throw 'The placed release book has no visible verified collider target.'}
    Click-DeskTarget $bookNow 1
    [void](Wait-AllSave {param($d) @($d.craftWorkspace.books|Where-Object {$_.bookId -eq $releaseId -and $_.isOpen}).Count -eq 1} 'native right click opens the placed release book')
    Add-AllCheck 'Native right click directly opens release book' $true 'Persisted open flag changed for the same book ID.'
    Capture-All 'release-open'
    Key-All 0x45
    $inspection=Get-DeskSnapshot
    Add-AllCheck 'Native E opens real inner-page inspection' ($inspection.mode.isInspecting -and $inspection.mode.inspectedInstance -eq $book.id) 'Only a native key was used; no gameplay API called.'
    $oldSpread=[int]$inspection.mode.inspectedSpread
    $next=if($oldSpread -ge 2){-1}else{1}
    if($next -gt 0){Click-AllVirtual 1350 141}else{Click-AllVirtual 552 141}
    $turned=Get-DeskSnapshot
    Add-AllCheck 'Native inspection page button turns release-book pages' ($turned.mode.inspectedSpread -eq $oldSpread+$next) ('spread '+$oldSpread+' -> '+$turned.mode.inspectedSpread)
    Capture-All 'release-inspection-page-turned'
    $offset=(Read-AllLog).Length;Key-All 0x1B
    [void](Wait-AllLog ('\[Physical\] Settled:'+ [regex]::Escape($book.id)+':') 12000 $offset)
    Select-DeskView 3 'TapeRack'
    $snapshot=Get-DeskSnapshot
    $tape=@($snapshot.targets|Where-Object {$_.type -eq 'Tape'})|Select-Object -First 1
    if(!$tape){throw 'No tape roll has a collider-verified screen target.'}
    $roll=Get-DeskItem (Read-AllSave) $tape.id
    Click-DeskTarget $tape
    $tapeSelected=Get-DeskSnapshot
    Add-AllCheck 'Native tape selection preserves roll pose' ($tapeSelected.mode.selectedTape -eq $tape.id -and !$tapeSelected.mode.physicalHeldItem -and !$tapeSelected.mode.tapeLengthSelecting -and (Get-DeskDistance $tapeSelected.mode.selectedPhysicalPosition $roll.worldPosition) -lt .001) 'Click only selects; no physical grab and no length selection.'
    $client=Get-AllClient
    $pull=@{x=[double]$tape.x;y=[double]$tape.y+60.0/$client.H}
    if($pull.y -ge .97){throw 'A 60px forward pull would leave the test window.'}
    Drag-DeskTarget $tape $pull 0
    $length=Get-DeskSnapshot
    # Compare against Unity's exact single-precision configuration values, not different double literals.
    $minimumTape=[double]([single]0.02);$maximumTape=[double]([single]0.30)
    Add-AllCheck 'Native forward drag starts metric length selection without moving roll' ($length.mode.tapeLengthSelecting -and !$length.mode.physicalHeldItem -and (Get-DeskDistance $length.mode.selectedPhysicalPosition $roll.worldPosition) -lt .001 -and $length.mode.tapeLengthMeters -ge $minimumTape -and $length.mode.tapeLengthMeters -le $maximumTape) ('selected length='+$length.mode.tapeLengthMeters+'m')
    Capture-All 'tape-forward-length-selection'
    Click-DeskTarget $pull 1
    $cut=Get-DeskSnapshot
    $cutSave=Wait-AllSave {param($d) @($d.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4 -and [int]$_.kind -eq 1}).Count -eq 1} 'right-click confirmation creates one held measured strip'
    $strip=@($cutSave.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4 -and [int]$_.kind -eq 1})|Select-Object -First 1
    $sameRoll=Get-DeskItem $cutSave $tape.id
    Add-AllCheck 'Native right-click confirms exact strip and keeps original roll' (!$cut.mode.tapeLengthSelecting -and $cut.mode.materialHeldItem -eq $strip.instanceId -and [Math]::Abs($strip.lengthMeters-$length.mode.tapeLengthMeters) -lt .001 -and $sameRoll.containerId -eq $roll.containerId -and (Get-DeskDistance $sameRoll.worldPosition $roll.worldPosition) -lt .001) ('strip='+$strip.instanceId+'; original roll='+$tape.id)
    Capture-All 'tape-cut-held'
    Key-All 0x1B
    [void](Wait-AllSave {param($d) @($d.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4}).Count -eq 0} 'ESC safely cancels the held strip')
    Add-AllCheck 'Native ESC cancels the held strip safely' $true 'No Held owner remains after cancellation.'
    $exceptions=@([regex]::Matches((Read-AllLog),'(?m)^(?:NullReferenceException|[^\r\n]*Exception:)[^\r\n]*$'))
    Add-AllCheck 'No player exceptions during native usability flow' ($exceptions.Count -eq 0) ($exceptions.Count.ToString()+' exception lines')
    $allStatus='PASS'
}catch{
    $allFailure=$_.Exception.Message
    $allChecks.Add([ordered]@{name='Native usability flow';passed=$false;evidence=$allFailure})
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
        nativeInput=$allInputSent;runtimeGameplayActionsInvoked=$false;diagnostics='F8 reads runtime positions, collider-hit targets and legal book-sized drop candidates only.'
        executable=$allExe;processId=if($allPlayer){$allPlayer.Id}else{$null};savePath=$allSavePath;playerLog=$allLog;pointerDiagnostics=$allPointerLog
        checks=$allChecks;screenshots=$allScreenshots;manual=$manual}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $allReportPath -Encoding UTF8
    Write-Output ('Native usability '+$allStatus+'; report='+$allReportPath)
}
if($allStatus -ne 'PASS'){throw ('Native usability failed: '+$allFailure)}
