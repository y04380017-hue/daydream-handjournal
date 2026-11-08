[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$PlayerLog,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\stage4b-screenshots'),
    [ValidateSet('Exercise','Restart','Benchmark','DeskRevision')][string]$Mode = 'Exercise',
    [string]$ExpectedStatePath
)

# This probe sends actual Windows mouse/keyboard events. It never calls Unity
# business methods, accesses a camera, or sends input to an unverified process.
# Launch VintageJournalDesk.exe first with --workspace-native-validation,
# -screen-fullscreen 0 and -logFile <path>. Restart verifies a fresh process.
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
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);
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
$processStartUtc = $player.StartTime.ToUniversalTime()
$readyDeadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    $player.Refresh()
    $hwnd = $player.MainWindowHandle
    $uiReady = (Test-Path -LiteralPath $PlayerLog) -and
        ([IO.File]::GetLastWriteTimeUtc($PlayerLog) -ge $processStartUtc) -and
        ((Get-Content -LiteralPath $PlayerLog -Raw) -match '\[Desk\] UI:Start')
    if ($hwnd -ne [IntPtr]::Zero -and $uiReady) { break }
    if ($player.HasExited) { throw 'Verified player exited before creating its window.' }
    Start-Sleep -Milliseconds 250
} while ([DateTime]::UtcNow -lt $readyDeadline)
if ($hwnd -eq [IntPtr]::Zero) { throw 'Verified player has no visible main window.' }
if (-not $uiReady) { throw 'No current-process UI:Start evidence; player log is absent, stale, or not ready.' }
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
    for ($attempt=0; $attempt -lt 2; $attempt++) {
        Confirm-Target
        # Refocusing can change the client origin, so recalculate the requested
        # physical pixel and reverify both pointer position and window ownership.
        $b = Client-Bounds
        $wantedX=[int]($b.X+$X*$b.Width); $wantedY=[int]($b.Y+$Y*$b.Height)
        if (-not [VintageDeskNative]::SetCursorPos($wantedX,$wantedY)) { throw 'SetCursorPos failed.' }
        Start-Sleep -Milliseconds 220
        $position=[VintageJournalPointerTelemetry+POINT]::new()
        if (-not [VintageJournalPointerTelemetry]::GetCursorPos([ref]$position)) { throw 'Cannot verify actual pointer position.' }
        $windowAtPointer=[VintageJournalPointerTelemetry]::WindowFromPoint($position)
        $rootAtPointer=[VintageJournalPointerTelemetry]::GetAncestor($windowAtPointer,2)
        $screenX=$position.X; $screenY=$position.Y
        [void][VintageJournalPointerTelemetry]::ScreenToClient($hwnd,[ref]$position)
        $script:pointerEvents.Add(@{kind='move';attempt=($attempt+1);wantedScreenX=$wantedX;wantedScreenY=$wantedY;actualScreenX=$screenX;actualScreenY=$screenY;clientX=$position.X;clientY=$position.Y;targetWindowUnderPointer=($rootAtPointer -eq $hwnd);timestamp=[DateTime]::UtcNow.ToString('o')})
        # An actual pointer mismatch can be user input. Never retry over it.
        if ([Math]::Abs($screenX-$wantedX) -gt 2 -or [Math]::Abs($screenY-$wantedY) -gt 2) { throw "Pointer did not reach target ($wantedX,$wantedY); actual ($screenX,$screenY); refusing click." }
        if ($rootAtPointer -ne $hwnd) {
            [uint32]$coveringPid=0
            [void][VintageDeskNative]::GetWindowThreadProcessId($rootAtPointer,[ref]$coveringPid)
            $coveringTitle=[Text.StringBuilder]::new(1024)
            $coveringClass=[Text.StringBuilder]::new(256)
            [void][VintageDeskNative]::GetWindowText($rootAtPointer,$coveringTitle,$coveringTitle.Capacity)
            [void][VintageDeskNative]::GetClassName($rootAtPointer,$coveringClass,$coveringClass.Capacity)
            $coveringWindow=@{handle=$rootAtPointer.ToInt64();hitHandle=$windowAtPointer.ToInt64();class=$coveringClass.ToString();title=$coveringTitle.ToString();processId=$coveringPid}
            $script:pointerEvents.Add(@{kind='occluded';attempt=($attempt+1);window=$coveringWindow;timestamp=[DateTime]::UtcNow.ToString('o')})
            $coveringDetail=$coveringWindow|ConvertTo-Json -Compress
            if ($attempt -eq 0) {
                Write-Host "Target pixel covered by $coveringDetail; refocusing verified player and retrying pointer placement once."
                Focus-Target
                continue
            }
            throw "Another window covers the target click pixel after one verified refocus retry; refusing input. $coveringDetail"
        }
        Confirm-Target
        return
    }
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

# Stage 04-B actions below use only raw OS events and read-only runtime evidence.
# The application must be launched with --workspace-native-validation. The
# fixture is prepared separately by the editor; this script never writes a save.
$script:targetBatch=@()
$script:pageBatch=@()
$script:savePath=$null
$script:lastDocument=$null

function Add-Result([string]$Name,[bool]$Passed,[string]$Evidence) {
    $script:checks.Add(@{name=$Name;result=$(if($Passed){'PASS'}else{'FAIL'});evidence=$Evidence;input='Verified Windows events + read-only save/log evidence'})
    if(-not $Passed){throw "$Name : $Evidence"}
}

function Wait-RawLog([int]$Offset,[string]$Pattern,[int]$TimeoutMs=10000) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $content=Read-PlayerLog
        if($content.Length -ge $Offset -and $content.Substring($Offset) -match $Pattern){return $Matches[0]}
        Confirm-Target
        Start-Sleep -Milliseconds 100
    }
    throw "No new runtime evidence: $Pattern"
}

function Normalize-ReadDocument($Document) {
    # Unity can serialize an absent inline serializable class as an empty object.
    # Normalize that exact semantic null only in this parsed, read-only snapshot.
    $workspace=$Document.craftWorkspace
    if($null -eq $workspace){return $Document}
    $pending=$workspace.pending
    $held=@($workspace.items|Where-Object {[int]$_.owner -eq 4})
    if($null -ne $pending -and $held.Count -eq 0 -and
        [string]::IsNullOrEmpty([string]$pending.instanceId) -and -not [bool]$pending.created -and
        [string]::IsNullOrEmpty([string]$pending.origin.instanceId)) {
        $workspace.pending=$null
    }
    return $Document
}

function Read-WorkspaceDocument {
    if(-not $script:savePath) {
        $matches=@([regex]::Matches((Read-PlayerLog),'\[Journal\] SavePath:([^\r\n]+)'))
        if($matches.Count -eq 0){return $null}
        $script:savePath=$matches[-1].Groups[1].Value.Trim()
        if($script:savePath.IndexOf('Stage4BNativeValidation',[StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Refusing to inspect a normal player save; expected isolated Stage4BNativeValidation: $script:savePath"
        }
    }
    try {
        $stream=[IO.File]::Open($script:savePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8)
        try {$text=$reader.ReadToEnd()}finally{$reader.Dispose();$stream.Dispose()}
        $doc=Normalize-ReadDocument ($text|ConvertFrom-Json)
        if($null -eq $doc.craftWorkspace){return $null}
        $heldRows=@($doc.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4})
        if($heldRows.Count -gt 1 -or ($heldRows.Count -eq 1 -and
            ($null -eq $doc.craftWorkspace.pending -or $doc.craftWorkspace.pending.instanceId -ne $heldRows[0].instanceId))) {
            throw [IO.InvalidDataException]::new('Persisted Held material has no unique matching pending transfer; refusing to treat it as an inline-null ghost.')
        }
        $script:lastDocument=$doc
        return $doc
    } catch [IO.IOException] {return $null}
      catch [System.ArgumentException] {return $null}
}

function Await-Document([string]$Label,[scriptblock]$Condition,[int]$TimeoutMs=15000) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        Confirm-Target
        $document=Read-WorkspaceDocument
        if($null -ne $document -and (& $Condition $document)) {
            Add-Result $Label $true ("Persisted isolated JSON condition observed after "+$timer.ElapsedMilliseconds+'ms')
            return $document
        }
        Start-Sleep -Milliseconds 130
    }
    throw "Persisted condition timed out: $Label"
}

function Item-Row($Document,[string]$Id) {
    return @($Document.craftWorkspace.items|Where-Object {$_.instanceId -eq $Id})|Select-Object -First 1
}
function Projection-Count($Document,[string]$Id) {
    return @($Document.pages|ForEach-Object {$_.decorations}|Where-Object {$_.instanceId -eq $Id}).Count
}
function Assert-UniqueOwnership($Document,[string]$Id,[int]$Owner,[string]$Container) {
    $rows=@($Document.craftWorkspace.items|Where-Object {$_.instanceId -eq $Id})
    $projected=Projection-Count $Document $Id
    return $rows.Count -eq 1 -and [int]$rows[0].owner -eq $Owner -and
        ([string]::IsNullOrEmpty($Container) -or $rows[0].containerId -eq $Container) -and
        $projected -eq $(if($Owner -eq 2){1}else{0})
}

function Refresh-Locators {
    $offset=(Read-PlayerLog).Length
    Press-Key 0x77 # F8: observes actual scene/rays; does not alter business state.
    [void](Wait-RawLog $offset '\[WorkspaceInput\] (Target|Page) ' 15000)
    Start-Sleep -Milliseconds 400
    $content=Read-PlayerLog
    if($content.Length -lt $offset){throw 'Player log was truncated during locator acquisition.'}
    $fresh=$content.Substring($offset)
    $script:targetBatch=@([regex]::Matches($fresh,'\[WorkspaceInput\] Target (\{[^\r\n]+\})')|ForEach-Object {$_.Groups[1].Value|ConvertFrom-Json})
    $script:pageBatch=@([regex]::Matches($fresh,'\[WorkspaceInput\] Page (\{[^\r\n]+\})')|ForEach-Object {$_.Groups[1].Value|ConvertFrom-Json})
}

function Locator([string]$Type,[string]$Id='') {
    Refresh-Locators
    $values=@($script:targetBatch|Where-Object {$_.type -eq $Type -and ([string]::IsNullOrEmpty($Id)-or $_.id -eq $Id)})
    if($values.Count -eq 0){throw "No freshly ray-verified $Type locator for '$Id'."}
    return $values[-1]
}

function Page-Locator([string]$BookId) {
    Refresh-Locators
    $values=@($script:pageBatch|Where-Object {$_.id -eq $BookId})
    if($values.Count -eq 0){throw "No freshly visible open page for '$BookId'."}
    return $values[0]
}

function Right-Click([double]$X,[double]$Y) {
    Move-Pointer $X $Y;Confirm-Target
    [VintageDeskNative]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
    try {Start-Sleep -Milliseconds 90;$script:pointerEvents.Add(@{kind='right-down';nativeStateDown=([VintageJournalPointerTelemetry]::GetAsyncKeyState(2)-lt 0);timestamp=[DateTime]::UtcNow.ToString('o')})}
    finally {[VintageDeskNative]::mouse_event(16,0,0,0,[UIntPtr]::Zero)}
    Start-Sleep -Milliseconds 150
}

function Press-Chord([byte]$Key) {
    Confirm-Target
    [VintageJournalKeyboardInput]::Key(0x11,$true)
    try {Start-Sleep -Milliseconds 60;Press-Key $Key}
    finally {[VintageJournalKeyboardInput]::Key(0x11,$false)}
    $script:pointerEvents.Add(@{kind='control-chord';key=[int]$Key;timestamp=[DateTime]::UtcNow.ToString('o')})
    Start-Sleep -Milliseconds 180
}

function Drag-Pointer($Target,[double]$EndX,[double]$EndY,[string]$GrabId='') {
    Move-Pointer $Target.x $Target.y;Confirm-Target
    $offset=(Read-PlayerLog).Length
    [VintageDeskNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    $script:pointerEvents.Add(@{kind='left-drag-down';id=$Target.id;timestamp=[DateTime]::UtcNow.ToString('o')})
    try {
        Start-Sleep -Milliseconds 140
        for($step=1;$step -le 10;$step++) {
            Move-Pointer ($Target.x+($EndX-$Target.x)*$step/10) ($Target.y+($EndY-$Target.y)*$step/10)
        }
        if(-not [string]::IsNullOrEmpty($GrabId)) {
            [void](Wait-RawLog $offset ('\[Physical\] (State:'+ [regex]::Escape($GrabId)+':Grabbed|Grab:'+ [regex]::Escape($GrabId)+':)') 10000)
        }
        Start-Sleep -Milliseconds 250
    } finally {
        [VintageDeskNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        $script:pointerEvents.Add(@{kind='left-drag-up';id=$Target.id;timestamp=[DateTime]::UtcNow.ToString('o')})
    }
    return $offset
}

function Set-DrawerNative([string]$Id,[bool]$Open) {
    $target=Locator 'Drawer' $Id
    $endX=$(if($Open){$target.openX}else{$target.closedX})
    $endY=$(if($Open){$target.openY}else{$target.closedY})
    if([Math]::Abs($endX-$target.x)+[Math]::Abs($endY-$target.y)-lt .003) {
        Click-Pointer $target.x $target.y
    } else {
        [void](Drag-Pointer $target ($target.x+($endX-$target.x)*1.25) ($target.y+($endY-$target.y)*1.25))
    }
    [void](Await-Document ("Drawer $Id "+$(if($Open){'opens with bounded drag'}else{'closes with bounded drag'})) {
        param($doc)
        $state=@($doc.craftWorkspace.drawers|Where-Object {$_.id -eq $Id})|Select-Object -First 1
        $null -ne $state -and $state.openFraction -ge 0 -and $state.openFraction -le 1 -and
            $(if($Open){$state.isOpen -and $state.openFraction -gt .9}else{-not $state.isOpen -and $state.openFraction -lt .1})
    })
}

function Set-ShaftNative([string]$Id,[bool]$Open) {
    $target=Locator 'Shaft' $Id
    $endX=$(if($Open){$target.openX}else{$target.closedX})
    $endY=$(if($Open){$target.openY}else{$target.closedY})
    [void](Drag-Pointer $target ($target.x+($endX-$target.x)*1.3) ($target.y+($endY-$target.y)*1.3))
    [void](Await-Document ("Shaft $Id "+$(if($Open){'withdrawn'}else{'inserted'})) {
        param($doc)
        $state=@($doc.craftWorkspace.drawers|Where-Object {$_.id -eq ('shaft:'+$Id)})|Select-Object -First 1
        $null -ne $state -and $state.isOpen -eq $Open -and $state.openFraction -ge 0 -and $state.openFraction -le 1
    })
}

function Select-TapeNative([string]$Id) {
    $target=Locator 'Tape' $Id;$offset=(Read-PlayerLog).Length
    Click-Pointer $target.x $target.y
    Wait-NewLog $offset ('\[Workspace\] Select:'+ [regex]::Escape($Id)) 'Left click selects tape without detaching'
    return $target
}

function Cut-TapeNative([string]$TapeId,[bool]$Adjust) {
    $target=Select-TapeNative $TapeId
    $offset=(Read-PlayerLog).Length
    Right-Click $target.x $target.y
    Wait-NewLog $offset ('\[Workspace\] TapeLengthStart:'+ [regex]::Escape($TapeId)+':0\.050') 'Right click starts true 5 cm preview'
    Save-PlayerImage 'tape-length-preview'|Write-Host
    $x=$target.x;$y=$target.y
    if($Adjust){$x=[Math]::Min(.95,$x+.012);Move-Pointer $x $y}
    $offset=(Read-PlayerLog).Length;Right-Click $x $y
    Wait-NewLog $offset '\[Workspace\] TapeCut:[^\r\n]+' 'Second right click cuts independent tape strip'
    $doc=Await-Document 'Cut creates exactly one held strip and no duplicate source roll' {
        param($document)
        $held=@($document.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4})
        $held.Count -eq 1 -and [int]$held[0].kind -eq 1 -and $held[0].lengthMeters -ge .02 -and $held[0].lengthMeters -le .30 -and
            @($document.craftWorkspace.items|Where-Object {$_.instanceId -eq $TapeId}).Count -eq 1
    }
    $strip=@($doc.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4})[0]
    if($Adjust){Add-Result 'Mouse motion changes physical cut length' ([Math]::Abs($strip.lengthMeters-.05)-gt .001) ("actual metres="+$strip.lengthMeters)}
    return $strip
}

function Attach-Native($Page,[string]$Id) {
    $offset=(Read-PlayerLog).Length
    Click-Pointer $Page.x $Page.y
    Wait-NewLog $offset ('\[Workspace\] Attach:'+ [regex]::Escape($Id)+':') ('Attach same instance '+$Id)
    $owner=$(if($Page.id -eq 'journal'){2}else{3})
    return Await-Document ('Single owner and matching projection: '+$Id+' -> '+$Page.id) {
        param($document)
        (Assert-UniqueOwnership $document $Id $owner $Page.id) -and (Item-Row $document $Id).pageId -eq $Page.pageId -and $null -eq $document.craftWorkspace.pending
    }
}

function Peel-Native($Page,[string]$Id) {
    $offset=(Read-PlayerLog).Length
    Click-Pointer $Page.x $Page.y
    [void](Wait-RawLog $offset ('\[Workspace\] SelectMaterial:'+ [regex]::Escape($Id)))
    $offset=(Read-PlayerLog).Length;Press-Key 0x50
    Wait-NewLog $offset ('\[Workspace\] Peel:'+ [regex]::Escape($Id)) ('P peels same material '+$Id)
    [void](Await-Document ('Peel removes old page owner without copying '+$Id) {param($document)Assert-UniqueOwnership $document $Id 4 ''})
}

function Round-Number($Value){return [Math]::Round([double]$Value,6)}
function Snapshot-Workspace($Document) {
    $Document=Normalize-ReadDocument $Document
    $rows=@($Document.craftWorkspace.items|Sort-Object instanceId|ForEach-Object {
        [ordered]@{instanceId=$_.instanceId;assetId=$_.assetId;kind=[int]$_.kind;owner=[int]$_.owner;containerId=$_.containerId;pageId=$_.pageId;
            position=@((Round-Number $_.position.x),(Round-Number $_.position.y));sizeMeters=@((Round-Number $_.sizeMeters.x),(Round-Number $_.sizeMeters.y));
            rotationDegrees=(Round-Number $_.rotationDegrees);scale=(Round-Number $_.scale);lengthMeters=(Round-Number $_.lengthMeters);widthMeters=(Round-Number $_.widthMeters);sortOrder=[int]$_.sortOrder;
            worldPosition=@((Round-Number $_.worldPosition.x),(Round-Number $_.worldPosition.y),(Round-Number $_.worldPosition.z));worldEulerAngles=@((Round-Number $_.worldEulerAngles.x),(Round-Number $_.worldEulerAngles.y),(Round-Number $_.worldEulerAngles.z))}
    })
    $books=@($Document.craftWorkspace.books|Sort-Object bookId|ForEach-Object {
        [ordered]@{bookId=$_.bookId;currentPage=[int]$_.currentPage;isOpen=[bool]$_.isOpen;pages=@($_.pages|ForEach-Object {$_.pageId});pageSizeMeters=@((Round-Number $_.pageSizeMeters.x),(Round-Number $_.pageSizeMeters.y))}
    })
    $pages=@($Document.pages|ForEach-Object {
        [ordered]@{pageId=$_.pageId;paperId=$_.paperId;decorations=@($_.decorations|Sort-Object instanceId|ForEach-Object {
            [ordered]@{instanceId=$_.instanceId;assetId=$_.assetId;position=@((Round-Number $_.position.x),(Round-Number $_.position.y));rotationDegrees=(Round-Number $_.rotationDegrees);scale=@((Round-Number $_.scale.x),(Round-Number $_.scale.y));size=@((Round-Number $_.size.x),(Round-Number $_.size.y));length=(Round-Number $_.length);lengthMeters=(Round-Number $_.lengthMeters);sortOrder=[int]$_.sortOrder}
        })}
    })
    return [ordered]@{items=$rows;books=$books;journalPages=$pages;pending=($null -ne $Document.craftWorkspace.pending)}
}

$status='PASS';$failure=$null
try {
    Focus-Target
    [void](Wait-RawLog 0 '\[Workspace\] Ready:' 30000)
    Start-Sleep -Milliseconds 500
    Save-PlayerImage 'stage4b-start-screen'|Write-Host
    $offset=(Read-PlayerLog).Length
    Click-UI 412 678
    $beginTimer=[Diagnostics.Stopwatch]::StartNew()
    do {
        Confirm-Target
        $beginLog=Read-PlayerLog
        $beginObserved=$beginLog.Length -ge $offset -and $beginLog.Substring($offset) -match '\[Desk\] UI:Begin'
        if ($beginObserved) { break }
        Start-Sleep -Milliseconds 100
    } while ($beginTimer.ElapsedMilliseconds -lt 5000)
    if (-not $beginObserved) {
        Save-PlayerImage 'stage4b-start-no-response'|Write-Host
        Focus-Target # Includes a 300 ms settle and strict foreground identity check.
        # Do not send a second click if the first was processed while refocusing.
        $beginLog=Read-PlayerLog
        if (-not ($beginLog.Length -ge $offset -and $beginLog.Substring($offset) -match '\[Desk\] UI:Begin')) {
            $script:checks.Add(@{name='Start click retry';result='INFO';evidence='No fresh UI:Begin for five seconds; refocused the verified player and clicked the same start button once more.';input='Windows OS events'})
            Click-UI 412 678
        }
    }
    Wait-NewLog $offset '\[Desk\] UI:Begin' 'Start enters Stage 04-B workspace' 10000
    Start-Sleep -Milliseconds 650
    [void](Await-Document 'Workspace native run uses an isolated compatible v1 save with both books' {
        param($doc)
        $doc.schemaVersion -eq 1 -and $null -ne $doc.craftWorkspace -and @($doc.craftWorkspace.books).Count -eq 2 -and
            @($doc.craftWorkspace.books|Where-Object {$_.bookId -eq 'release-a'}).Count -eq 1 -and
            @($doc.craftWorkspace.books|Where-Object {$_.bookId -eq 'release-b'}).Count -eq 1
    })
    Save-PlayerImage 'stage4b-default-desk'|Write-Host
    if([string]::IsNullOrEmpty($ExpectedStatePath)){$ExpectedStatePath=Join-Path $PSScriptRoot '..\docs\stage4b-logs\native-restart-expected.json'}

    if($Mode -eq 'DeskRevision') {
        $offset=(Read-PlayerLog).Length
        Click-UI 1818 130
        Wait-NewLog $offset '\[Tutorial\] Open' 'Native tutorial button opens'
        Press-Escape
        Wait-NewLog $offset '\[Tutorial\] Close' 'Native ESC closes tutorial'
        $tn=Locator 'TravelersNotebook' 'physical-tn_01-001'
        $offset=(Read-PlayerLog).Length
        Click-Pointer $tn.x $tn.y
        Wait-NewLog $offset '\[Workspace\] Select:physical-tn_01-001' 'Native TN shelf click selects its own instance'
        Click-UI 1698 465
        Wait-NewLog $offset '\[Notebook\] Active:tn_01' 'Native click puts Standard TN in center'
        $offset=(Read-PlayerLog).Length
        Click-UI 145 137
        Wait-NewLog $offset '\[Notebook\] Active:binder' 'Native return restores binder'
        $page=Page-Locator 'journal'
        $tn=Locator 'TravelersNotebook' 'physical-tn_03-001'
        $offset=Drag-Pointer $tn $page.x $page.y $tn.id
        Wait-NewLog $offset '\[Notebook\] Active:tn_03' 'Native Passport TN drag replaces central book'
        $page=Page-Locator 'journal'
        $offset=(Read-PlayerLog).Length
        Click-Pointer $page.x $page.y
        Wait-NewLog $offset '\[Journal\] Mode:EditingMode' 'Native click opens travel journal'
        Click-UI 1480 1015
        Wait-NewLog $offset '\[Journal\] Flip:Complete:1' 'Next page remains clickable'
        Press-Escape
        Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'Native ESC returns to desk'
        Click-UI 145 137
        Wait-NewLog $offset '\[Notebook\] Active:binder' 'Binder restored after travel creation'
        Save-PlayerImage 'revision-native-final'|Write-Host
    } elseif($Mode -eq 'Benchmark') {
        Wait-NewLog $offset '\[Desk\] Performance:Saved:' '600 frames captured in the focused default desk' 60000
    } elseif($Mode -eq 'Restart') {
        if(-not (Test-Path -LiteralPath $ExpectedStatePath)){throw 'Missing prior Exercise restart expectation.'}
        $expected=Get-Content -LiteralPath $ExpectedStatePath -Raw|ConvertFrom-Json
        $actual=Snapshot-Workspace (Read-WorkspaceDocument)
        $expectedText=$expected.state|ConvertTo-Json -Depth 20 -Compress
        $actualText=$actual|ConvertTo-Json -Depth 20 -Compress
        Add-Result 'Fresh process restores both books and exact single-owner material records' ($expectedText -ceq $actualText) 'Sorted semantic snapshot includes all IDs, owners, pages, UV poses, sizes, rotations, layers and original cut lengths; float comparison rounded to 1 micrometre.'
        $loadedEntries=@([regex]::Matches((Read-PlayerLog),'\[Journal\] Loaded:(\{[^\r\n]+\})'))
        if($loadedEntries.Count -eq 0){throw 'Fresh process did not report the document it actually loaded.'}
        $loadedDocument=$loadedEntries[-1].Groups[1].Value|ConvertFrom-Json
        $loadedText=(Snapshot-Workspace $loadedDocument)|ConvertTo-Json -Depth 20 -Compress
        Add-Result 'Fresh process loaded the expected snapshot before scene reconciliation' ($loadedText -ceq $expectedText) 'Actual [Journal] Loaded JSON matches the saved semantic expectation, independently of the current file read.'
        Refresh-Locators
        Add-Result 'Restart restores both independent books open and rendered' (@($script:pageBatch|Where-Object {$_.id -eq 'release-a'}).Count -gt 0 -and @($script:pageBatch|Where-Object {$_.id -eq 'release-b'}).Count -gt 0) 'Freshly logged real page anchors for both restored book views.'
        Save-PlayerImage 'stage4b-restored-books'|Write-Host
    } else {
        Refresh-Locators
        $drawerIds=@($script:targetBatch|Where-Object {$_.type -eq 'Drawer'}|ForEach-Object {$_.id}|Sort-Object -Unique)
        Add-Result 'All three independent drawer controls are ray-visible' ($drawerIds.Count -eq 3) ($drawerIds -join ', ')
        Set-DrawerNative $drawerIds[0] $true
        Set-DrawerNative $drawerIds[0] $false
        foreach($drawerId in $drawerIds){Set-DrawerNative $drawerId $true}
        Save-PlayerImage 'stage4b-open-drawers'|Write-Host
        Refresh-Locators
        $shaftIds=@($script:targetBatch|Where-Object {$_.type -eq 'Shaft'}|ForEach-Object {$_.id}|Sort-Object -Unique)
        Add-Result 'Animal stands expose real independent shaft controls' ($shaftIds.Count -ge 3) ($shaftIds.Count.ToString()+' ray-visible shafts')
        $shaftId=$shaftIds[0];Set-ShaftNative $shaftId $true
        $slot=Locator 'ShaftSlot' $shaftId
        Refresh-Locators
        $tape=@($script:targetBatch|Where-Object {$_.type -eq 'Tape' -and $_.containerId -eq 'drawer-tape-top'})[0]
        if($null -eq $tape){throw 'No exposed tape roll after drawers opened.'}
        $tapeId=$tape.id;$sourceContainer=(Item-Row (Read-WorkspaceDocument) $tapeId).containerId
        $offset=Drag-Pointer $tape $slot.x $slot.y $tapeId
        Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($tapeId)+':Stored') 'Roll settles on withdrawn shaft'
        [void](Await-Document 'Drawer-to-shaft move persists one instance and vacates old source' {
            param($doc)
            (Assert-UniqueOwnership $doc $tapeId 0 $slot.containerId) -and (Item-Row $doc $tapeId).containerId -ne $sourceContainer
        })
        Set-ShaftNative $shaftId $false
        Save-PlayerImage 'stage4b-mounted-roll'|Write-Host

        $strip=Cut-TapeNative $tapeId $true
        $firstStripId=$strip.instanceId
        [void](Attach-Native (Page-Locator 'journal') $firstStripId)
        Save-PlayerImage 'stage4b-tape-on-journal'|Write-Host
        Press-Key 0x1B

        # At equal extension the upper trays correctly cover the lower drawer's books.
        # Close both upper drawers through their real handles before accessing books.
        Set-DrawerNative 'drawer-tape-top' $false
        Set-DrawerNative 'drawer-tape-middle' $false

        foreach($entry in @(@{asset='ReleaseBook_A';book='release-a';area='work-area-a'},@{asset='ReleaseBook_B';book='release-b';area='work-area-b'})) {
            $bookId='physical-'+$entry.asset+'-001'
            $target=Locator 'ReleaseBook' $bookId
            $area=Locator 'WorkArea' $entry.area
            $offset=Drag-Pointer $target $area.x $area.y $bookId
            Wait-NewLog $offset ('\[Physical\] Settled:'+ [regex]::Escape($bookId)+':Placed') ('Take '+$entry.asset+' from drawer onto table')
            $target=Locator 'ReleaseBook' $bookId;$offset=(Read-PlayerLog).Length
            Click-Pointer $target.x $target.y
            Wait-NewLog $offset ('\[Workspace\] OpenBook:'+ [regex]::Escape($entry.book)) ('Click opens actual '+$entry.asset)
            [void](Await-Document ('Independent '+$entry.book+' open state persists') {param($doc)@($doc.craftWorkspace.books|Where-Object {$_.bookId -eq $entry.book -and $_.isOpen}).Count -eq 1})
        }
        Save-PlayerImage 'stage4b-two-open-release-books'|Write-Host
        $strip=Cut-TapeNative $tapeId $false;$transferId=$strip.instanceId
        $pageA=Page-Locator 'release-a';[void](Attach-Native $pageA $transferId)
        $pageA=Page-Locator 'release-a';Peel-Native $pageA $transferId
        $oldPageId=$pageA.pageId;$offset=(Read-PlayerLog).Length
        Click-UI 1058 54 # Explicit target-book pager while the same peeled material remains held.
        Wait-NewLog $offset '\[Workspace\] BookPage:release-a:2' 'Explicit target pager remains usable after peel'
        $pageA=Page-Locator 'release-a'
        Add-Result 'Target navigation exposes a different persistent release-book page' ($pageA.pageId -ne $oldPageId) ($oldPageId+' -> '+$pageA.pageId)
        [void](Attach-Native $pageA $transferId)
        $pageA=Page-Locator 'release-a';Peel-Native $pageA $transferId
        $pageB=Page-Locator 'release-b';[void](Attach-Native $pageB $transferId)
        Save-PlayerImage 'stage4b-transferred-same-strip-to-B'|Write-Host
        $pageB=Page-Locator 'release-b';Peel-Native $pageB $transferId
        [void](Attach-Native (Page-Locator 'journal') $transferId)
        Press-Chord 0x5A
        [void](Await-Document 'Ctrl+Z moves same ID back to release book B without duplication' {param($doc)Assert-UniqueOwnership $doc $transferId 3 'release-b'})
        Press-Chord 0x59
        [void](Await-Document 'Ctrl+Y moves same ID forward to journal without duplication' {param($doc)Assert-UniqueOwnership $doc $transferId 2 'journal'})
        Press-Key 0x1B

        $sticker=Locator 'Sticker';$stickerId=$sticker.id;$offset=(Read-PlayerLog).Length
        Click-Pointer $sticker.x $sticker.y
        Wait-NewLog $offset ('\[Workspace\] MaterialGrab:'+ [regex]::Escape($stickerId)) 'Take a real hanging sticker'
        [void](Attach-Native (Page-Locator 'release-a') $stickerId)
        # The latest sticker is the topmost object at that exact page position.
        $pageA=Page-Locator 'release-a';Peel-Native $pageA $stickerId
        [void](Attach-Native (Page-Locator 'release-a') $stickerId)
        Press-Key 0x1B
        # Take the currently exposed top sheet from each of the three independent pads.
        $noteBoxIndex=0
        foreach($noteIndex in @(3,6,8)) {
            $noteBoxIndex++
            $noteId='physical-note_'+('{0:D2}' -f $noteIndex)+'-001'
            $note=Locator 'Note' $noteId;$offset=(Read-PlayerLog).Length
            $noteRow=Item-Row (Read-WorkspaceDocument) $noteId
            Add-Result ('Note style collection '+$noteBoxIndex+' resolves correct finite stock') ($note.containerId -eq ('SmallNoteBox_'+$noteBoxIndex) -and $noteRow.assetId -eq ('note_'+('{0:D2}' -f $noteIndex)) -and [int]$noteRow.kind -eq 3) ($note.containerId+' / '+$noteRow.assetId)
            Click-Pointer $note.x $note.y
            Wait-NewLog $offset ('\[Workspace\] MaterialGrab:'+ [regex]::Escape($noteId)) ('Take note from style box '+$noteIndex)
            [void](Attach-Native (Page-Locator 'journal') $noteId)
            Press-Key 0x1B
        }
        Save-PlayerImage 'stage4b-sticker-and-three-note-styles'|Write-Host

        $before=@((Read-WorkspaceDocument).craftWorkspace.items|Where-Object {[int]$_.kind -eq 1}).Count
        $cancelled=Cut-TapeNative $tapeId $false;$cancelledId=$cancelled.instanceId
        Press-Key 0x1B
        [void](Await-Document 'ESC rolls back new strip without cloning or losing original material' {
            param($doc)
            $null -eq $doc.craftWorkspace.pending -and @($doc.craftWorkspace.items|Where-Object {[int]$_.owner -eq 4}).Count -eq 0 -and
                @($doc.craftWorkspace.items|Where-Object {[int]$_.kind -eq 1}).Count -eq $before -and
                @($doc.craftWorkspace.items|Where-Object {$_.instanceId -eq $cancelledId}).Count -eq 0
        })
        Press-Key 0x1B
        Start-Sleep -Milliseconds 1100
        $document=Read-WorkspaceDocument
        $duplicateIds=@($document.craftWorkspace.items|Group-Object instanceId|Where-Object {$_.Count -ne 1})
        $badProjection=@($document.craftWorkspace.items|Where-Object {
            (Projection-Count $document $_.instanceId) -ne $(if([int]$_.owner -eq 2){1}else{0})
        })
        Add-Result 'Complete persisted registry has unique IDs and no duplicate page projections' ($duplicateIds.Count -eq 0 -and $badProjection.Count -eq 0 -and $null -eq $document.craftWorkspace.pending) ($document.craftWorkspace.items.Count.ToString()+' uniquely owned records checked against journal projections')
        $expected=@{schema='Stage04BNativeSnapshot-v1';timestamp=(Get-Date).ToString('o');savePath=$script:savePath;state=(Snapshot-Workspace $document)}
        $expectDirectory=Split-Path -Parent ([IO.Path]::GetFullPath($ExpectedStatePath))
        New-Item -ItemType Directory -Path $expectDirectory -Force|Out-Null
        $expected|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $ExpectedStatePath -Encoding utf8
        Add-Result 'Restart expectation recorded outside player save' $true $ExpectedStatePath
        Save-PlayerImage 'stage4b-final-persistent-collage'|Write-Host
    }
} catch {
    $status='FAIL';$failure=$_.Exception.Message
    $script:checks.Add(@{name='Probe stopped safely';result='FAIL';evidence=$failure})
    try { Save-PlayerImage 'stage4b-probe-failure'|Write-Host }
    catch { $script:checks.Add(@{name='Failure screenshot unavailable';result='INFO';evidence=$_.Exception.Message}) }
} finally {
    $report=@{status=$status;mode=$Mode;timestamp=(Get-Date).ToString('o');executable=$exe;processId=$player.Id;processStartTimeUtc=$processStartUtc.ToString('o');playerLog=$PlayerLog;savePath=$script:savePath;
        checks=@($script:checks.ToArray());screenshots=@($script:screenshots.ToArray());pointerEvents=@($script:pointerEvents.ToArray());failure=$failure;expectedRestartPath=$ExpectedStatePath;
        scope='Actual verified Windows mouse and scan-code keyboard events; fresh ray-tested locators, runtime state logs and read-only isolated JSON. No Unity business-method shortcuts and no direct save mutation.'}
    $reportPath=Join-Path $OutputDirectory $(if($Mode -eq 'Restart'){'os-input-restart-results.json'}else{'os-input-results.json'})
    $report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output $reportPath
}
if($status -ne 'PASS'){throw $failure}
