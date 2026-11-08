[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$PlayerLog,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\screenshots'),
    [switch]$Exercise
)

# This probe sends actual Windows mouse/keyboard events. It never calls Unity
# business methods, accesses a camera, or sends input to an unverified process.
# Launch VintageJournalDesk.exe first with: -screen-fullscreen 0 -logFile <path>
# Without -Exercise it only captures the verified player's client rectangle.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
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
    [void][VintageDeskNative]::SetCursorPos([int]($b.X+$X*$b.Width), [int]($b.Y+$Y*$b.Height))
    Start-Sleep -Milliseconds 220
}

function Click-Pointer([double]$X, [double]$Y) {
    Move-Pointer $X $Y
    Confirm-Target
    [VintageDeskNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    try { Start-Sleep -Milliseconds 100 }
    finally { [VintageDeskNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 220
}

function Click-UI([double]$X, [double]$Y) { Click-Pointer ($X/1920) ($Y/1080) }

function Press-Escape {
    Confirm-Target
    [VintageDeskNative]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
    try { Start-Sleep -Milliseconds 100 }
    finally { [VintageDeskNative]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 200
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

$status = 'PASS'
$failure = $null
$bounds = $null
try {
    Focus-Target
    $bounds = Client-Bounds
    # UI:Start is emitted before the first presented game frame on a cold launch.
    Start-Sleep -Milliseconds 1200
    Save-PlayerImage 'start' | Write-Output
    if ($Exercise) {
        $offset = (Read-PlayerLog).Length
        Click-UI 412 678
        Wait-NewLog $offset '\[Desk\] UI:Begin' 'Start button enters desk'
        Start-Sleep -Milliseconds 300
        Save-PlayerImage 'desk' | Write-Output
        foreach ($entry in @(@{kind='Tape';category='tape'}, @{kind='Sticker';category='sticker'}, @{kind='Pages';category='page'})) {
            $target = Hover-Target $entry.kind
            $offset = (Read-PlayerLog).Length
            Click-Pointer $target.x $target.y
            Wait-NewLog $offset ('\[Desk\] UI:Catalog:'+$entry.category+'; count=\d+') "Click $($entry.kind) opens preview"
            $offset = (Read-PlayerLog).Length
            Click-UI 560 424
            Wait-NewLog $offset '\[Desk\] UI:Select:[^\r\n]+' "Select $($entry.category) item"
            Save-PlayerImage $entry.category | Write-Output
            $offset = (Read-PlayerLog).Length
            if ($entry.category -eq 'sticker') { Press-Escape } else { Click-UI 1682 165 }
            Wait-NewLog $offset '\[Desk\] UI:Close' "Close $($entry.category) preview"
        }
        $journal = Hover-Target 'Journal'
        $offset = (Read-PlayerLog).Length
        Click-Pointer $journal.x $journal.y
        Wait-NewLog $offset '\[Desk\] Camera:Inspect' 'Click journal inspects'
        Start-Sleep -Milliseconds 1100
        Save-PlayerImage 'journal-inspect' | Write-Output
        $offset = (Read-PlayerLog).Length
        Press-Escape
        Wait-NewLog $offset '\[Desk\] Camera:Return' 'ESC returns camera'
        Start-Sleep -Milliseconds 1100
        $journal = Hover-Target 'Journal'
        $offset = (Read-PlayerLog).Length
        Click-Pointer $journal.x $journal.y
        Wait-NewLog $offset '\[Desk\] Camera:Inspect' 'Inspect journal again'
        Start-Sleep -Milliseconds 1100
        $offset = (Read-PlayerLog).Length
        Click-UI 126 149
        Wait-NewLog $offset '\[Desk\] Camera:Return' 'Back button returns camera'
        Start-Sleep -Milliseconds 1100
        $offset = (Read-PlayerLog).Length
        Click-UI 1815 57
        Wait-NewLog $offset '\[Desk\] UI:Settings' 'Settings button'
        $offset = (Read-PlayerLog).Length
        Click-UI 1155 553
        Wait-NewLog $offset '\[Desk\] Settings:Quality=Performance' 'Performance setting'
        $offset = (Read-PlayerLog).Length
        Click-UI 765 553
        Wait-NewLog $offset '\[Desk\] Settings:Quality=Detailed' 'Restore detailed shadows'
        $offset = (Read-PlayerLog).Length
        Click-UI 960 635
        Wait-NewLog $offset '\[Desk\] Settings:VSync=[01]' 'Toggle vertical sync'
        $offset = (Read-PlayerLog).Length
        Click-UI 960 635
        Wait-NewLog $offset '\[Desk\] Settings:VSync=[01]' 'Restore vertical sync'
        Save-PlayerImage 'settings' | Write-Output
        $offset = (Read-PlayerLog).Length
        Click-UI 960 823
        Wait-NewLog $offset '\[Desk\] UI:CloseSettings' 'Save settings and return'
        Save-PlayerImage 'final-desk' | Write-Output
    }
}
catch {
    $status = 'FAIL'
    $failure = $_.Exception.Message
    $script:checks.Add(@{ name='Probe stopped'; result='FAIL'; evidence=$failure })
}
finally {
    $report = @{ status=$status; timestamp=(Get-Date).ToString('o'); executable=$exe; processId=$player.Id;
        playerLog=$PlayerLog; exercise=[bool]$Exercise; client=$bounds;
        checks=@($script:checks.ToArray()); screenshots=@($script:screenshots.ToArray()); failure=$failure;
        scope='Real Windows pointer/keyboard events verified by fresh runtime logs. Screenshots are target client only. FPS and Unity Editor Play Mode require separate evidence.' }
    $reportPath = Join-Path $OutputDirectory "$run-os-input-results.json"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output $reportPath
}
if ($status -ne 'PASS') { throw $failure }
