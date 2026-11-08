[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$PlayerLog,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\stage2-screenshots'),
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
    if ($path -notmatch '[\\/]Stage2Validation[\\/]') { throw 'Refusing automation with the normal player save. Launch with --journal-validation.' }
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

function Enter-Journal {
    $target = Hover-Target 'Journal'
    $offset = (Read-PlayerLog).Length
    Click-Pointer $target.x $target.y
    Wait-NewLog $offset '\[Journal\] Enter' 'T01 native journal click'
    Wait-NewLog $offset '\[Journal\] Mode:EditingMode' 'T02 editor camera settled'
    Start-Sleep -Milliseconds 300
}

function Turn-Key([byte]$Code,[int]$Expected) {
    $offset = (Read-PlayerLog).Length
    Press-Key $Code
    Wait-NewLog $offset ('\[Journal\] Flip:Complete:'+$Expected+'(?:\r?\n|$)') ('Native arrow reaches spread '+$Expected)
    Start-Sleep -Milliseconds 180
}

$status = 'PASS'; $failure=$null; $bounds=$null
try {
    Focus-Target
    $bounds=Client-Bounds
    Start-Sleep -Milliseconds 1300
    $loaded=Read-LoadedDocument
    $savePath=Read-SavePath
    Save-PlayerImage 'stage2-start' | Write-Output
    $offset=(Read-PlayerLog).Length
    Click-UI 412 678
    Wait-NewLog $offset '\[Desk\] UI:Begin' 'Native start button'
    Start-Sleep -Milliseconds 300

    if ($Mode -eq 'Restart') {
        if (-not $ExpectedStatePath -or -not (Test-Path -LiteralPath $ExpectedStatePath)) { throw 'Restart probe requires the expected state file from the first native run.' }
        $expected=Get-Content -LiteralPath $ExpectedStatePath -Raw | ConvertFrom-Json
        Assert-Observed ($expected.processId -ne $player.Id) 'T14 new process' "Before=$($expected.processId); after=$($player.Id)"
        Assert-Observed ($loaded.schemaVersion -eq 1 -and $loaded.currentSpread -eq $expected.currentSpread) 'T14 startup restores spread' "Loaded spread=$($loaded.currentSpread)"
        Assert-Observed ($loaded.pages.Count -eq 24 -and $loaded.pages[0].paperId -eq $expected.leftPaper -and $loaded.pages[1].paperId -eq $expected.rightPaper) 'T14 startup restores independent paper IDs' "left=$($loaded.pages[0].paperId); right=$($loaded.pages[1].paperId)"
        Enter-Journal
        Save-PlayerImage 'stage2-restarted-edit' | Write-Output
        $offset=(Read-PlayerLog).Length
        Click-UI 124 57
        Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'Restarted editor returns desk'
    }
    else {
        Enter-Journal
        Save-PlayerImage 'stage2-edit' | Write-Output
        for ($spread=[int]$loaded.currentSpread; $spread -gt 0; $spread--) { Turn-Key 0x25 ($spread-1) }
        $offset=(Read-PlayerLog).Length
        Press-Key 0x25
        Wait-NewLog $offset '\[Journal\] Flip:Boundary' 'T05 native first boundary'
        $offset=(Read-PlayerLog).Length
        Press-Key 0x27
        Press-Key 0x27
        Wait-NewLog $offset '\[Journal\] Flip:Complete:1(?:\r?\n|$)' 'T03 native next and T06 repeat lock'
        Start-Sleep -Milliseconds 300
        $saved=Read-SaveDocument
        Assert-Observed ($saved.currentSpread -eq 1) 'T06 rapid arrows do not skip spreads' "Saved spread=$($saved.currentSpread)"
        Turn-Key 0x25 0

        $offset=(Read-PlayerLog).Length
        Click-UI 960 1015
        Wait-NewLog $offset '\[Journal\] Picker:Open' 'Native paper picker'
        $offset=(Read-PlayerLog).Length
        Click-UI 228 522
        Wait-NewLog $offset '\[Journal\] Preview:Left:page_05' 'T07 native left live preview'
        Save-PlayerImage 'stage2-picker' | Write-Output
        $offset=(Read-PlayerLog).Length
        Click-UI 134 881
        Wait-NewLog $offset '\[Journal\] Paper:Confirm:Left:page_05' 'T07 confirm left'

        $offset=(Read-PlayerLog).Length
        Click-UI 960 1015
        Wait-NewLog $offset '\[Journal\] Picker:Open' 'Open right paper picker'
        Click-UI 334 247
        $offset=(Read-PlayerLog).Length
        Click-UI 354 522
        Wait-NewLog $offset '\[Journal\] Preview:Right:page_06' 'T08 native right live preview'
        $offset=(Read-PlayerLog).Length
        Click-UI 134 881
        Wait-NewLog $offset '\[Journal\] Paper:Confirm:Right:page_06' 'T08 confirm right'
        $saved=Read-SaveDocument
        Assert-Observed ($saved.pages[0].paperId -eq 'page_05' -and $saved.pages[1].paperId -eq 'page_06') 'T07/T08 independent save records' "left=$($saved.pages[0].paperId); right=$($saved.pages[1].paperId)"

        $offset=(Read-PlayerLog).Length
        Click-UI 960 1015
        Wait-NewLog $offset '\[Journal\] Picker:Open' 'Open cancel check'
        Click-UI 228 679
        $offset=(Read-PlayerLog).Length
        Click-UI 334 881
        Wait-NewLog $offset '\[Journal\] Paper:Cancel' 'T09 native cancel'
        $saved=Read-SaveDocument
        Assert-Observed ($saved.pages[0].paperId -eq 'page_05' -and $saved.pages[1].paperId -eq 'page_06') 'T09 cancel preserves saved choices' 'No cancelled paper leaked into disk data'

        $offset=(Read-PlayerLog).Length
        Click-UI 1826 186
        Wait-NewLog $offset '\[Journal\] Zoom:' 'T10 plus button'
        Move-Pointer .5 .5
        $offset=(Read-PlayerLog).Length
        [VintageDeskNative]::mouse_event(0x800,0,0,120,[UIntPtr]::Zero)
        Wait-NewLog $offset '\[Journal\] Zoom:' 'T10 native mouse wheel'
        $offset=(Read-PlayerLog).Length
        [VintageDeskNative]::mouse_event(8,0,0,0,[UIntPtr]::Zero)
        try { Start-Sleep -Milliseconds 120; Move-Pointer .58 .54 }
        finally { [VintageDeskNative]::mouse_event(16,0,0,0,[UIntPtr]::Zero) }
        Wait-NewLog $offset '\[Journal\] Pan:' 'T11 right-button pan'
        Save-PlayerImage 'stage2-zoom' | Write-Output
        $offset=(Read-PlayerLog).Length
        Click-UI 1826 251
        Wait-NewLog $offset '\[Journal\] Zoom:' 'T10 minus button'
        $offset=(Read-PlayerLog).Length
        Click-UI 1806 316
        Wait-NewLog $offset '\[Journal\] Reset' 'T10 reset button'
        Start-Sleep -Milliseconds 400

        $offset=(Read-PlayerLog).Length
        Click-UI 1480 1015
        Wait-NewLog $offset '\[Journal\] Flip:Complete:1(?:\r?\n|$)' 'Native next page button'
        $offset=(Read-PlayerLog).Length
        Press-Escape
        Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'T12 ESC returns desk'
        Start-Sleep -Milliseconds 200
        $target=Hover-Target 'Tape'
        $offset=(Read-PlayerLog).Length
        Click-Pointer $target.x $target.y
        Wait-NewLog $offset '\[Desk\] UI:Catalog:tape;' 'T13 existing desk preview still works'
        $offset=(Read-PlayerLog).Length
        Press-Escape
        Wait-NewLog $offset '\[Desk\] UI:Close' 'Close legacy preview'
        Enter-Journal
        $offset=(Read-PlayerLog).Length
        Click-UI 124 57
        Wait-NewLog $offset '\[Journal\] Mode:DeskMode' 'T12 back button returns desk'
        Save-PlayerImage 'stage2-final-desk' | Write-Output
        $saved=Read-SaveDocument
        if (-not $ExpectedStatePath) { $ExpectedStatePath=Join-Path $OutputDirectory 'stage2-restart-expected.json' }
        @{processId=$player.Id;currentSpread=$saved.currentSpread;leftPaper=$saved.pages[0].paperId;rightPaper=$saved.pages[1].paperId;savePath=$savePath} |
            ConvertTo-Json | Set-Content -LiteralPath $ExpectedStatePath -Encoding utf8
        Assert-Observed ($saved.schemaVersion -eq 1 -and $saved.currentSpread -eq 1) 'T14 saved restart fixture' $ExpectedStatePath
    }
    $playerErrors=[regex]::Matches((Read-PlayerLog),'(?m)^.*(?:NullReferenceException|InvalidOperationException|ArgumentException|Assertion failed|\[Journal\].*失败).*$')
    Assert-Observed ($playerErrors.Count -eq 0) 'T16 native player has no blocking exceptions' "$($playerErrors.Count) detected"
}
catch {
    $status='FAIL';$failure=$_.Exception.Message;$script:checks.Add(@{name='Native probe stopped';result='FAIL';evidence=$failure})
    try { Save-PlayerImage 'stage2-failure' | Write-Output } catch { }
}
finally {
    $report=@{status=$status;mode=$Mode;timestamp=(Get-Date).ToString('o');executable=$exe;processId=$player.Id;playerLog=$PlayerLog;client=$bounds;
        checks=@($script:checks.ToArray());screenshots=@($script:screenshots.ToArray());pointerEvents=@($script:pointerEvents.ToArray());failure=$failure;expectedStatePath=$ExpectedStatePath;
        scope='Actual Windows pointer, wheel, drag and keyboard events. Runtime logs and persisted JSON are observed; no business methods are injected.'}
    $reportPath=Join-Path $OutputDirectory "$run-stage2-native-$Mode.json"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output $reportPath
}
if ($status -ne 'PASS') { throw $failure }


