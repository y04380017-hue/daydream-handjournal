[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$PlayerLog,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\stage3-screenshots'),
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
    if ([Math]::Abs($screenX-$wantedX) -gt 2 -or [Math]::Abs($screenY-$wantedY) -gt 2) { throw 'Pointer did not reach the intended physical pixel; refusing click.' }
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

function Read-LoadedDocument {
    $entry = [regex]::Matches((Read-PlayerLog), '\[Journal\] Loaded:(\{[^\r\n]+\})')
    if ($entry.Count -eq 0) { throw 'No journal document was reported at player startup.' }
    return ($entry[$entry.Count-1].Groups[1].Value | ConvertFrom-Json)
}

function Read-SavePath {
    $entry = [regex]::Matches((Read-PlayerLog), '\[Journal\] SavePath:([^\r\n]+)')
    if ($entry.Count -eq 0) { throw 'No journal save path reported by runtime.' }
    $path = [IO.Path]::GetFullPath($entry[$entry.Count-1].Groups[1].Value.Trim())
    if ($path -notmatch '[\\/]Stage3Validation[\\/]') { throw 'Refusing automation with the normal player save. Launch with --scrapbook-validation.' }
    return $path
}

function Read-SaveDocument {
    $path = Read-SavePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Save file is missing: $path" }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

function Assert-Observed([bool]$Condition,[string]$Label,[string]$Evidence) {
    if (-not $Condition) { throw "$Label : $Evidence" }
    $script:checks.Add(@{name=$Label;result='PASS';evidence=$Evidence;input='Observed runtime state / file, following Windows OS events'})
}

function Read-PagePoint([string]$Side) {
    $entries=@([regex]::Matches((Read-PlayerLog),'\[Scrapbook\] PagePoint (\{[^\r\n]+\})') | ForEach-Object {$_.Groups[1].Value|ConvertFrom-Json} | Where-Object {$_.name -eq $Side})
    if(-not $entries.Count){throw "No ray-projected $Side page point reported."};return $entries[-1]
}
function Read-Handles {
    $entries=[regex]::Matches((Read-PlayerLog),'\[Scrapbook\] Handles (\{[^\r\n]+\})')
    if(-not $entries.Count){throw 'No selected-object handles reported.'};return ($entries[$entries.Count-1].Groups[1].Value|ConvertFrom-Json)
}
function All-Decorations($Document) {
    foreach($page in @($Document.pages)){foreach($item in @($page.decorations)){if($null -ne $item){$item}}}
}
function Find-Decoration($Document,[string]$Id) {return @(All-Decorations $Document | Where-Object {$_.instanceId -eq $Id})[0]}
function Decoration-Snapshot($Document) {
    $records=@(foreach($page in @($Document.pages)){foreach($d in @($page.decorations)){if($null -ne $d){[pscustomobject][ordered]@{
        pageId=[string]$page.pageId;instanceId=[string]$d.instanceId;assetId=[string]$d.assetId;type=[string]$d.type;
        x=[double]$d.position.x;y=[double]$d.position.y;rotation=[double]$d.rotationDegrees;
        scaleX=[double]$d.scale.x;scaleY=[double]$d.scale.y;sizeX=[double]$d.size.x;sizeY=[double]$d.size.y;
        length=[double]$d.length;layer=[int]$d.sortOrder
    }}}})
    return (@($records|Sort-Object pageId,instanceId)|ConvertTo-Json -Depth 8 -Compress)
}
function Press-Chord([byte]$Modifier,[byte]$Key) {
    Confirm-Target;[VintageJournalKeyboardInput]::Key($Modifier,$true)
    try {Start-Sleep -Milliseconds 70;Press-Key $Key}
    finally {[VintageJournalKeyboardInput]::Key($Modifier,$false)}
    Start-Sleep -Milliseconds 180
}
function Drag-Pointer([double]$FromX,[double]$FromY,[double]$ToX,[double]$ToY,[int]$Steps=8) {
    Move-Pointer $FromX $FromY;Confirm-Target;[VintageDeskNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    try {
        Start-Sleep -Milliseconds 130
        for($i=1;$i -le $Steps;$i++){Move-Pointer ($FromX+($ToX-$FromX)*$i/$Steps) ($FromY+($ToY-$FromY)*$i/$Steps)}
    }
    finally {[VintageDeskNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
    Start-Sleep -Milliseconds 250
}
function Enter-Journal {
    $target=Hover-Target 'Journal';$offset=(Read-PlayerLog).Length;Click-Pointer $target.x $target.y
    Wait-NewLog $offset '\[Journal\] Enter' 'Native journal entry'
    Wait-NewLog $offset '\[Journal\] Mode:EditingMode' 'Native camera settles';Start-Sleep -Milliseconds 300
}
function Open-Material([string]$Category) {
    $x=switch($Category){'Tape'{550};'Sticker'{730};'Note'{910};default{throw 'Unknown material category'}}
    $offset=(Read-PlayerLog).Length;Click-UI $x 128
    Wait-NewLog $offset ('\[Scrapbook\] Browse:'+$Category) ('Browse '+$Category)
}
function Choose-First {
    $offset=(Read-PlayerLog).Length;Click-UI 102 370
    Wait-NewLog $offset '\[Scrapbook\] Choose:[^\r\n]+' 'Native choose material';Start-Sleep -Milliseconds 250
}
function New-Decoration($Before,$After) {
    $old=@(All-Decorations $Before | ForEach-Object {$_.instanceId});$added=@(All-Decorations $After|Where-Object {$_.instanceId -notin $old})
    if($added.Count -ne 1){throw "Expected exactly one placed object, observed $($added.Count)."};return $added[0]
}
function Ensure-Deselected {
    $offset=(Read-PlayerLog).Length;Press-Escape;Start-Sleep -Milliseconds 150
    $fresh=(Read-PlayerLog).Substring($offset)
    Assert-Observed ($fresh -notmatch '\[Journal\] Exit') 'ESC clears material operation before leaving editor' 'No journal exit from one selected/pending operation'
}

$status='PASS';$failure=$null;$bounds=$null
try {
    Focus-Target;$bounds=Client-Bounds;Start-Sleep -Milliseconds 1600
    $loaded=Read-LoadedDocument;$savePath=Read-SavePath
    Save-PlayerImage 'stage3-start'|Write-Output
    $offset=(Read-PlayerLog).Length;Click-UI 412 678;Wait-NewLog $offset '\[Desk\] UI:Begin' 'Native start button'
    Start-Sleep -Milliseconds 300
    if($Mode -eq 'Restart') {
        if(-not $ExpectedStatePath -or -not(Test-Path -LiteralPath $ExpectedStatePath)){throw 'Restart requires the fixture from Exercise.'}
        $expected=Get-Content -LiteralPath $ExpectedStatePath -Raw|ConvertFrom-Json
        Assert-Observed ($expected.processId -ne $player.Id) 'T20 fresh operating-system process' "Before=$($expected.processId); after=$($player.Id)"
        Assert-Observed ($loaded.currentSpread -eq $expected.currentSpread) 'T20 current spread restored' "spread=$($loaded.currentSpread)"
        Assert-Observed ((Decoration-Snapshot $loaded) -eq $expected.decorationSnapshot) 'T20 complete decoration records restored' 'Page ownership, IDs, UV, angle, uniform scale, base size, tape length and layer match saved data'
        Assert-Observed (@(All-Decorations $loaded).Count -ge 3) 'T20 real placed objects present' "decorations=$(@(All-Decorations $loaded).Count)"
        Enter-Journal;Save-PlayerImage 'stage3-restarted'|Write-Output
        $offset=(Read-PlayerLog).Length;Click-UI 124 57;Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'Restarted game returns desk'
    }
    else {
        foreach($category in @('Tape','Sticker','Note')) {
            try {$target=Find-Target $category}
            catch {
                if($category -ne 'Note'){throw}
                $entries=@([regex]::Matches((Read-PlayerLog),'\[Desk\] Target (\{[^\r\n]+\})')|ForEach-Object {$_.Groups[1].Value|ConvertFrom-Json}|Where-Object {$_.name -match '便签'})
                if(-not $entries.Count){throw 'No note storage target was reported.'};$target=$entries[-1]
            }
            $offset=(Read-PlayerLog).Length;Click-Pointer $target.x $target.y
            Wait-NewLog $offset ('\[Scrapbook\] Browse:'+$category) ('T01/T02/T03 physical '+$category+' storage')
            Save-PlayerImage ('stage3-storage-'+$category)|Write-Output
            Press-Escape;Start-Sleep -Milliseconds 200
        }
        Enter-Journal
        Open-Material 'Sticker';Choose-First
        $left=Read-PagePoint 'Left';$right=Read-PagePoint 'Right';$before=Read-SaveDocument
        Move-Pointer $left.x $left.y;Save-PlayerImage 'stage3-preview'|Write-Output
        $offset=(Read-PlayerLog).Length;Click-Pointer $left.x $left.y;Wait-NewLog $offset '\[Scrapbook\] Place:' 'T06 sticker placed on left'
        $after=Read-SaveDocument;$sticker=New-Decoration $before $after
        Assert-Observed ($sticker.pageId -eq $after.pages[$after.currentSpread*2].pageId) 'T06 left page identity saved' $sticker.pageId
        Ensure-Deselected

        Open-Material 'Note';$before=Read-SaveDocument;$offset=(Read-PlayerLog).Length
        Drag-Pointer (102/1920) (370/1080) $right.x $right.y
        Wait-NewLog $offset '\[Scrapbook\] Place:' 'T07 direct browser-to-right-page drag'
        $after=Read-SaveDocument;$note=New-Decoration $before $after
        Assert-Observed ($note.pageId -eq $after.pages[$after.currentSpread*2+1].pageId) 'T07 right page identity saved' $note.pageId
        Ensure-Deselected

        Open-Material 'Tape';Choose-First;$before=Read-SaveDocument;$offset=(Read-PlayerLog).Length
        Drag-Pointer ($left.x-.035) ($left.y-.10) ($left.x+.035) ($left.y-.10)
        Wait-NewLog $offset '\[Scrapbook\] Place:' 'T08 tape cut and placed'
        $after=Read-SaveDocument;$tape=New-Decoration $before $after;$tapeId=[string]$tape.instanceId
        Assert-Observed ($tape.length -gt 1) 'T08 native tape length changes' "length=$($tape.length)"
        $handles=Read-Handles;$originalPosition=@([double]$tape.position.x,[double]$tape.position.y)
        if($null -eq $handles.hitX -or $null -eq $handles.hitY){throw 'Runtime did not publish an opaque selection hit point.'}
        $offset=(Read-PlayerLog).Length;Drag-Pointer $handles.hitX $handles.hitY ($handles.hitX-.025) ($handles.hitY+.04)
        Wait-NewLog $offset '\[Scrapbook\] Commit:move' 'T09/T10 reselect and move existing material'
        $moved=Find-Decoration (Read-SaveDocument) $tapeId
        Assert-Observed ([Math]::Abs($moved.position.x-$originalPosition[0])+[Math]::Abs($moved.position.y-$originalPosition[1]) -gt .01) 'T10 moved coordinates stored' "UV=$($moved.position.x),$($moved.position.y)"

        $offset=(Read-PlayerLog).Length;Press-Key 0x45;Wait-NewLog $offset '\[Scrapbook\] Commit:rotate' 'T11 E shortcut rotates'
        $rotated=Find-Decoration (Read-SaveDocument) $tapeId
        Assert-Observed ([Math]::Abs($rotated.rotationDegrees) -gt .1) 'T11 stored angle changes' "angle=$($rotated.rotationDegrees)"
        $handles=Read-Handles;$offset=(Read-PlayerLog).Length
        Drag-Pointer $handles.rotateX $handles.rotateY ($handles.rotateX+.025) ($handles.rotateY+.012)
        Wait-NewLog $offset '\[Scrapbook\] Commit:rotate' 'T11 rotation control handle'
        $offset=(Read-PlayerLog).Length;Click-UI 232 378;Wait-NewLog $offset '\[Scrapbook\] Commit:scale' 'T12 uniform scaling button'
        $scaled=Find-Decoration (Read-SaveDocument) $tapeId
        Assert-Observed ([Math]::Abs($scaled.scale.x-$scaled.scale.y) -lt .0001 -and $scaled.scale.x -gt 1) 'T12 aspect-preserving stored scale' "$($scaled.scale.x),$($scaled.scale.y)"
        $handles=Read-Handles;$offset=(Read-PlayerLog).Length
        Drag-Pointer $handles.scaleX $handles.scaleY ($handles.scaleX+.012) ($handles.scaleY-.012)
        Wait-NewLog $offset '\[Scrapbook\] Commit:scale' 'T12 corner scale handle'
        $offset=(Read-PlayerLog).Length;Click-UI 232 438;Wait-NewLog $offset '\[Scrapbook\] Commit:length' 'T08 placed tape length adjustment'
        $offset=(Read-PlayerLog).Length;Click-UI 102 518;Wait-NewLog $offset '\[Scrapbook\] Layer:bottom' 'T16 send to bottom'
        $bottom=Find-Decoration (Read-SaveDocument) $tapeId
        $offset=(Read-PlayerLog).Length;Click-UI 232 518;Wait-NewLog $offset '\[Scrapbook\] Layer:top' 'T16 bring to top'
        $top=Find-Decoration (Read-SaveDocument) $tapeId
        Assert-Observed ($top.sortOrder -gt $bottom.sortOrder) 'T16 saved layer order changes' "bottom=$($bottom.sortOrder); top=$($top.sortOrder)"
        Save-PlayerImage 'stage3-selected'|Write-Output
        $offset=(Read-PlayerLog).Length;Press-Key 0x2E;Wait-NewLog $offset '\[Scrapbook\] Delete:' 'T13 Delete key'
        Assert-Observed ($null -eq (Find-Decoration (Read-SaveDocument) $tapeId)) 'T13 selected instance deleted from data' $tapeId
        $offset=(Read-PlayerLog).Length;Press-Chord 0xA2 0x5A;Wait-NewLog $offset '\[Scrapbook\] Undo' 'T14 CtrlZ restores deletion'
        Assert-Observed ($null -ne (Find-Decoration (Read-SaveDocument) $tapeId)) 'T14 same instance restored' $tapeId
        $offset=(Read-PlayerLog).Length;Press-Chord 0xA2 0x59;Wait-NewLog $offset '\[Scrapbook\] Redo' 'T15 CtrlY redoes deletion'
        $offset=(Read-PlayerLog).Length;Press-Chord 0xA2 0x5A;Wait-NewLog $offset '\[Scrapbook\] Undo' 'T15 restore creation for persistence fixture'
        $pageBefore=Read-SaveDocument;$snapshot=Decoration-Snapshot $pageBefore
        $offset=(Read-PlayerLog).Length;Press-Key 0x27;Wait-NewLog $offset '\[Journal\] Flip:Complete:' 'T17 next spread'
        $offset=(Read-PlayerLog).Length;Press-Key 0x25;Wait-NewLog $offset '\[Journal\] Flip:Complete:' 'T17 return spread'
        Assert-Observed ((Decoration-Snapshot (Read-SaveDocument)) -eq $snapshot) 'T17 page turn preserves all decoration records' 'All page-owned transforms and layers match'
        Move-Pointer .50 .50;$offset=(Read-PlayerLog).Length;[VintageDeskNative]::mouse_event(0x800,0,0,120,[UIntPtr]::Zero)
        Wait-NewLog $offset '\[Journal\] Zoom:' 'T18 journal zoom with unselected decorations'
        Save-PlayerImage 'stage3-zoom-alignment'|Write-Output
        $offset=(Read-PlayerLog).Length;Click-UI 1806 316;Wait-NewLog $offset '\[Journal\] Reset' 'Restore journal view'
        Start-Sleep -Milliseconds 350
        $offset=(Read-PlayerLog).Length;Press-Escape;Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'T19 return desk'
        Enter-Journal
        Assert-Observed ((Decoration-Snapshot (Read-SaveDocument)) -eq $snapshot) 'T19 reenter keeps creation' 'No reset to static scene decoration'
        Save-PlayerImage 'stage3-completed'|Write-Output
        $offset=(Read-PlayerLog).Length;Click-UI 124 57;Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'Exit editing before restart'
        $saved=Read-SaveDocument
        if(-not $ExpectedStatePath){$ExpectedStatePath=Join-Path $OutputDirectory 'stage3-restart-expected.json'}
        @{processId=$player.Id;currentSpread=$saved.currentSpread;decorationSnapshot=(Decoration-Snapshot $saved);savePath=$savePath}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $ExpectedStatePath -Encoding utf8
        Assert-Observed (@(All-Decorations $saved).Count -ge 3) 'T20 save fixture contains real decorations' $ExpectedStatePath
    }
    $playerErrors=[regex]::Matches((Read-PlayerLog),'(?m)^.*(?:NullReferenceException|InvalidOperationException|ArgumentException|Assertion failed|\[Journal\].*失败).*$')
    Assert-Observed ($playerErrors.Count -eq 0) 'T24/T25 native player has no blocking exceptions' "$($playerErrors.Count) detected"
}
catch {
    $status='FAIL';$failure=$_.Exception.Message;$script:checks.Add(@{name='Native probe stopped';result='FAIL';evidence=$failure})
    try {Save-PlayerImage 'stage3-failure'|Write-Output}catch{}
}
finally {
    $report=@{status=$status;mode=$Mode;timestamp=(Get-Date).ToString('o');executable=$exe;processId=$player.Id;playerLog=$PlayerLog;client=$bounds;checks=@($script:checks.ToArray());screenshots=@($script:screenshots.ToArray());pointerEvents=@($script:pointerEvents.ToArray());failure=$failure;expectedStatePath=$ExpectedStatePath;scope='Real Windows pointer, drag, scan-code keyboard and wheel. Persisted JSON and runtime observations only; no gameplay methods are injected.'}
    $reportPath=Join-Path $OutputDirectory "$run-stage3-native-$Mode.json";$report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $reportPath -Encoding utf8;Write-Output $reportPath
}
if($status -ne 'PASS'){throw $failure}


