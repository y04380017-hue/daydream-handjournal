[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$PlayerLog,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\stage4-screenshots'),
    [ValidateSet('Exercise','Restart')][string]$Mode = 'Exercise',
    [string]$ExpectedStatePath
)

# This probe sends actual Windows mouse/keyboard events. It never calls Unity
# business methods, accesses a camera, or sends input to an unverified process.
# Launch VintageJournalDesk.exe first with: -screen-fullscreen 0 -logFile <path>
# Without -Exercise it only captures the verified player's client rectangle.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('VintageJournalPointerTelemetry' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VintageJournalPointerTelemetry {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
}
# PowerShell hosts may already have fixed process DPI awareness. Set this probe
# thread explicitly so ClientToScreen and SetCursorPos share physical pixels.
[void][VintageJournalPointerTelemetry]::SetThreadDpiAwarenessContext([IntPtr](-4))
if (-not ('VintageJournalKeyboardInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class VintageJournalKeyboardInput {
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
        public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
        public ushort virtualKey,scanCode; public uint flags,time; public UIntPtr extraInfo;
    }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION data; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyW(uint code,uint mode);
    public static uint ScanCode(ushort key) { return MapVirtualKeyW(key,4); }
    public static void Key(ushort key,bool down) {
        uint mapped=ScanCode(key);
        if(mapped==0) throw new InvalidOperationException("No scan code for virtual key "+key);
        bool extended=(mapped&0xff00)==0xe000 || (mapped&0xff00)==0xe100 || (key>=0x21&&key<=0x2e);
        var input=new INPUT {type=1,data=new INPUTUNION {keyboard=new KEYBDINPUT {
            virtualKey=0,scanCode=(ushort)(mapped&0xff),flags=8u|(extended?1u:0u)|(down?0u:2u),time=0,extraInfo=UIntPtr.Zero}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput did not send physical key "+key);
    }
}
'@
}
if (-not ('VintageDeskNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VintageDeskNative
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
}

[void][VintageDeskNative]::SetProcessDpiAwarenessContext([IntPtr](-4))
$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($exe) -ne 'VintageJournalDesk.exe') { throw 'Refusing input: expected VintageJournalDesk.exe.' }
$players = @([Diagnostics.Process]::GetProcessesByName('VintageJournalDesk') | Where-Object {
    try { [IO.Path]::GetFullPath($_.MainModule.FileName) -eq [IO.Path]::GetFullPath($exe) } catch { $false }
})
if ($players.Count -ne 1) { throw "Expected exactly one running player at $exe; found $($players.Count)." }
$player = $players[0]
$readyDeadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    $player.Refresh()
    $hwnd = $player.MainWindowHandle
    $uiReady = (Test-Path -LiteralPath $PlayerLog) -and ((Get-Content -LiteralPath $PlayerLog -Raw) -match '\[Desk\] UI:Start')
    if ($hwnd -ne [IntPtr]::Zero -and $uiReady) { break }
    if ($player.HasExited) { throw 'Verified player exited before creating its window.' }
    Start-Sleep -Milliseconds 250
} while ([DateTime]::UtcNow -lt $readyDeadline)
if ($hwnd -eq [IntPtr]::Zero) { throw 'Verified player has no visible main window.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$run = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:checks = [Collections.Generic.List[object]]::new()
$script:screenshots = [Collections.Generic.List[string]]::new()
$script:pointerEvents = [Collections.Generic.List[object]]::new()

function Confirm-Target {
    if ($player.HasExited) { throw 'Target player exited.' }
    [uint32]$pidValue = 0
    [void][VintageDeskNative]::GetWindowThreadProcessId($hwnd, [ref]$pidValue)
    if ($pidValue -ne $player.Id) { throw 'Window process identity changed.' }
    if ([VintageDeskNative]::GetForegroundWindow() -ne $hwnd) { throw 'Target player lost foreground; refusing global input/capture.' }
}

function Focus-Target {
    [void][VintageDeskNative]::ShowWindow($hwnd, 9)
    $foreground = [VintageDeskNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    $foregroundThread = [VintageDeskNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid)
    $currentThread = [VintageDeskNative]::GetCurrentThreadId()
    $attached = $false
    try {
        if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) {
            $attached = [VintageDeskNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
        }
        [void][VintageDeskNative]::BringWindowToTop($hwnd)
        [void][VintageDeskNative]::SetForegroundWindow($hwnd)
    }
    finally { if ($attached) { [void][VintageDeskNative]::AttachThreadInput($currentThread, $foregroundThread, $false) } }
    Start-Sleep -Milliseconds 300
    Confirm-Target
}

function Client-Bounds {
    $rect = [VintageDeskNative+RECT]::new()
    $origin = [VintageDeskNative+POINT]::new()
    if (-not [VintageDeskNative]::GetClientRect($hwnd, [ref]$rect)) { throw 'Cannot read client bounds.' }
    if (-not [VintageDeskNative]::ClientToScreen($hwnd, [ref]$origin)) { throw 'Cannot map client origin.' }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 640 -or $height -lt 360) { throw 'Player client is too small for validation.' }
    return @{ X=$origin.X; Y=$origin.Y; Width=$width; Height=$height }
}

function Read-PlayerLog {
    if (-not (Test-Path -LiteralPath $PlayerLog)) { return '' }
    # ReadWrite sharing allows Unity to keep its log open while the probe reads it.
    $stream = [IO.File]::Open($PlayerLog, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}

function Wait-NewLog([int]$Offset, [string]$Pattern, [string]$Label, [int]$TimeoutMs=5000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $content = Read-PlayerLog
        if ($content.Length -ge $Offset -and $content.Substring($Offset) -match $Pattern) {
            $script:checks.Add(@{ name=$Label; result='PASS'; evidence=$Matches[0]; input='Windows OS events'; elapsedMs=$timer.ElapsedMilliseconds })
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw "No fresh runtime evidence for $Label ($Pattern)."
}

function Move-Pointer([double]$X, [double]$Y) {
    Confirm-Target
    $b = Client-Bounds
    $wantedX=[int]($b.X+$X*$b.Width); $wantedY=[int]($b.Y+$Y*$b.Height)
    if (-not [VintageDeskNative]::SetCursorPos($wantedX,$wantedY)) { throw 'SetCursorPos failed.' }
    Start-Sleep -Milliseconds 220
    $position=[VintageJournalPointerTelemetry+POINT]::new()
    if (-not [VintageJournalPointerTelemetry]::GetCursorPos([ref]$position)) { throw 'Cannot verify actual pointer position.' }
    $rootAtPointer=[VintageJournalPointerTelemetry]::GetAncestor([VintageJournalPointerTelemetry]::WindowFromPoint($position),2)
    $screenX=$position.X; $screenY=$position.Y
    [void][VintageJournalPointerTelemetry]::ScreenToClient($hwnd,[ref]$position)
    $script:pointerEvents.Add(@{kind='move';wantedScreenX=$wantedX;wantedScreenY=$wantedY;actualScreenX=$screenX;actualScreenY=$screenY;clientX=$position.X;clientY=$position.Y;targetWindowUnderPointer=($rootAtPointer -eq $hwnd);timestamp=[DateTime]::UtcNow.ToString('o')})
    if ([Math]::Abs($screenX-$wantedX) -gt 2 -or [Math]::Abs($screenY-$wantedY) -gt 2) { throw "Pointer did not reach target ($wantedX,$wantedY); actual ($screenX,$screenY); refusing click." }
    if ($rootAtPointer -ne $hwnd) { throw 'Another window covers the target click pixel; refusing input.' }
}

function Click-Pointer([double]$X, [double]$Y) {
    Move-Pointer $X $Y
    Confirm-Target
    [VintageDeskNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    try { Start-Sleep -Milliseconds 100; $script:pointerEvents.Add(@{kind='left-down';nativeStateDown=([VintageJournalPointerTelemetry]::GetAsyncKeyState(1) -lt 0);timestamp=[DateTime]::UtcNow.ToString('o')}) }
    finally { [VintageDeskNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 220
}

function Click-UI([double]$X, [double]$Y) { Click-Pointer ($X/1920) ($Y/1080) }

function Press-Escape {
    Press-Key 0x1B
}

function Save-PlayerImage([string]$Name) {
    Confirm-Target
    $b = Client-Bounds
    # Copy only the foreground player's client rectangle, and only when it is
    # entirely on the primary display. Never capture unrelated desktop areas.
    if ($b.X -lt 0 -or $b.Y -lt 0 -or $b.X+$b.Width -gt [VintageDeskNative]::GetSystemMetrics(0) -or
        $b.Y+$b.Height -gt [VintageDeskNative]::GetSystemMetrics(1)) {
        throw 'Target client extends outside the primary screen; resize/move the player before capture.'
    }
    $bitmap = [Drawing.Bitmap]::new([int]$b.Width, [int]$b.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $path = Join-Path $OutputDirectory "$run-$Name.png"
    try {
        $graphics.CopyFromScreen([int]$b.X, [int]$b.Y, 0, 0, [Drawing.Size]::new([int]$b.Width, [int]$b.Height))
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $script:screenshots.Add($path)
    return $path
}

function Find-Target([string]$Kind) {
    $entries = @([regex]::Matches((Read-PlayerLog), '\[Desk\] Target (\{[^\r\n]+\})') | ForEach-Object {
        $_.Groups[1].Value | ConvertFrom-Json
    } | Where-Object { $_.kind -eq $Kind })
    if ($entries.Count -eq 0) { throw "Runtime did not report a ray-visible $Kind target." }
    return $entries[-1]
}

function Hover-Target([string]$Kind) {
    $target = Find-Target $Kind
    Move-Pointer (.025) (.04) # Clears previous hover through the UI header.
    $offset = (Read-PlayerLog).Length
    Move-Pointer $target.x $target.y
    Wait-NewLog $offset ('\[Desk\] Hover:'+[regex]::Escape($Kind)+':') "Hover $Kind"
    return $target
}

function Press-Key([byte]$Code) {
    Confirm-Target
    $scan=[VintageJournalKeyboardInput]::ScanCode($Code)
    [VintageJournalKeyboardInput]::Key($Code,$true)
    try {
        Start-Sleep -Milliseconds 80
        $script:pointerEvents.Add(@{kind='key-down';virtualKey=[int]$Code;mappedScanCode=('0x{0:X4}' -f $scan);sender='SendInput SCANCODE + EXTENDEDKEY where needed';expectedNativeDown=([VintageJournalPointerTelemetry]::GetAsyncKeyState($Code) -lt 0);leftNativeDown=([VintageJournalPointerTelemetry]::GetAsyncKeyState(0x25) -lt 0);rightNativeDown=([VintageJournalPointerTelemetry]::GetAsyncKeyState(0x27) -lt 0);timestamp=[DateTime]::UtcNow.ToString('o')})
    }
    finally { [VintageJournalKeyboardInput]::Key($Code,$false) }
    Start-Sleep -Milliseconds 120
}

function Find-Physical([string]$Kind) {
    $targets=@([regex]::Matches((Read-PlayerLog),'\[PhysicalInput\] Target (\{[^\r\n]+\})') | ForEach-Object {$_.Groups[1].Value|ConvertFrom-Json} | Where-Object {$_.type -eq $Kind})
    if($targets.Count -eq 0){throw "No ray-visible physical $Kind"}
    return $targets[0]
}
function Await-State([int]$Offset,[string]$Id,[string]$State) {
    Wait-NewLog $Offset ('\[Physical\] State:'+ [regex]::Escape($Id)+':'+$State) ($State+' '+$Id) 15000
}
$status='PASS';$failure=$null
try {
    Focus-Target
    Start-Sleep -Milliseconds 1400
    $offset=(Read-PlayerLog).Length
    Click-UI 412 678
    Wait-NewLog $offset '\[Desk\] UI:Begin' 'Start enters desk'
    Start-Sleep -Milliseconds 600
    Save-PlayerImage 'stage4-desk' | Write-Output
    $visibleIds=@([regex]::Matches((Read-PlayerLog),'\[PhysicalInput\] Target (\{[^\r\n]+\})') | ForEach-Object {($_.Groups[1].Value|ConvertFrom-Json).id} | Sort-Object -Unique)
    $script:checks.Add(@{name='All 37 physical instances are ray visible';result=$(if($visibleIds.Count -eq 37){'PASS'}else{'FAIL'});evidence=($visibleIds.Count.ToString()+' unique ray-visible IDs')})
    if($visibleIds.Count -ne 37){throw ('Expected 37 ray-visible stock instances; found '+$visibleIds.Count)}
    $placementMatch=[regex]::Match((Read-PlayerLog),'\[PhysicalInput\] PlacementPoint (\{[^\r\n]+\})')
    if(-not $placementMatch.Success){throw 'Missing projected legal desktop placement point.'}
    $placement=$placementMatch.Groups[1].Value|ConvertFrom-Json
    foreach($kind in @('Tape','Sticker','Note','LoosePage','TravelersNotebook')) {
        $target=Find-Physical $kind
        Move-Pointer $target.x $target.y
        $offset=(Read-PlayerLog).Length
        Confirm-Target
        [VintageDeskNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
        try {
            Await-State $offset $target.id 'Grabbed'
            Start-Sleep -Milliseconds 500
            for($n=1;$n -le 8;$n++) { Move-Pointer ($target.x+($placement.x-$target.x)*$n/8) ($target.y+($placement.y-$target.y)*$n/8) }
            Save-PlayerImage ('held-'+$kind) | Write-Output
        } finally { [VintageDeskNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
        Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($target.id)+':Placed') ('Place '+$kind+' on desktop') 15000
        $offset=(Read-PlayerLog).Length
        Press-Key 0x52
        Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($target.id)+':Stored') ('Return '+$kind) 15000
    }
    # Two successive toggles prove the native controls can close and reopen each box.
    foreach($x in @(690,918)) {
        $offset=(Read-PlayerLog).Length
        Click-UI $x 52
        Wait-NewLog $offset '\[PhysicalInput\] Container:[^\r\n]+:False' 'Close container'
        $offset=(Read-PlayerLog).Length
        Click-UI $x 52
        Wait-NewLog $offset '\[PhysicalInput\] Container:[^\r\n]+:True' 'Open container'
    }
    $tn=Find-Physical 'TravelersNotebook';Move-Pointer $tn.x $tn.y
    $offset=(Read-PlayerLog).Length;Press-Key 0x45
    Wait-NewLog $offset ('\[Physical\] Inspect:'+ [regex]::Escape($tn.id)) 'Inspect TN with E'
    Start-Sleep -Milliseconds 1200;Save-PlayerImage 'tn-inspection'|Write-Output
    $offset=(Read-PlayerLog).Length;Press-Key 0x52
    Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($tn.id)+':Stored') 'Return inspected TN' 15000
    # Cancel one real drag through ESC.
    $tape=Find-Physical 'Tape';Move-Pointer $tape.x $tape.y
    $offset=(Read-PlayerLog).Length
    [VintageDeskNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    try { Await-State $offset $tape.id 'Grabbed';Move-Pointer .65 .76;Press-Key 0x1B }
    finally{[VintageDeskNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
    Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($tape.id)+':Stored') 'ESC restores stored tape' 15000
    $journalTarget=Find-Target 'Journal'
    $offset=(Read-PlayerLog).Length
    Click-Pointer $journalTarget.x $journalTarget.y
    Wait-NewLog $offset '\[Desk\] Camera:Inspect' 'Native click enters original journal editor'
    Start-Sleep -Milliseconds 1200
    Save-PlayerImage 'stage4-digital-editor'|Write-Output
    $offset=(Read-PlayerLog).Length
    Press-Key 0x1B
    Wait-NewLog $offset '\[Desk\] Camera:Return' 'Native ESC returns from original editor'
    Start-Sleep -Milliseconds 1200
    Save-PlayerImage 'stage4-final'|Write-Output
} catch {$status='FAIL';$failure=$_.Exception.Message;$script:checks.Add(@{name='Probe stopped';result='FAIL';evidence=$failure})}
finally {
    $report=@{status=$status;timestamp=(Get-Date).ToString('o');executable=$exe;processId=$player.Id;playerLog=$PlayerLog;checks=@($script:checks.ToArray());screenshots=@($script:screenshots.ToArray());pointerEvents=@($script:pointerEvents.ToArray());failure=$failure;scope='Real Windows mouse and scan-code keyboard events. Fresh runtime state/settled logs, verified foreground player client screenshots. Play Mode results recorded separately.'}
    $reportPath=Join-Path $OutputDirectory 'os-input-results.json';$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8;Write-Output $reportPath
}
if($status -ne 'PASS'){throw $failure}

