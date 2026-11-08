[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\commission\native'),
    [int]$Width = 1600, [int]$Height = 900
)
# Real Windows mouse/keyboard events against the built player, on an isolated save
# (--commission-native-validation). Never calls Unity methods; target positions come
# from the player's own [CommissionTargets] log (F9) and assertions from its log and
# a read-only parse of the isolated save file.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('CommissionNative2' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class CommissionNative2 {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort virtualKey,scanCode; public uint flags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT keyboard; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION data; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code,uint mode);
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
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    public static void Mouse(uint flags) {
        var i = new INPUT { type = 0, data = new INPUTUNION { mouse = new MOUSEINPUT { dwFlags = flags } } };
        if (SendInput(1, new[]{ i }, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput mouse");
    }
    public static void Key(ushort key, bool down) {
        uint scan = MapVirtualKeyW(key, 4); if (scan == 0) throw new InvalidOperationException("No scan code " + key);
        bool ext = (scan & 0xff00) == 0xe000;
        var i = new INPUT { type = 1, data = new INPUTUNION { keyboard = new KEYBDINPUT { scanCode = (ushort)(scan & 0xff), flags = 8u | (ext ? 1u : 0u) | (down ? 0u : 2u) } } };
        if (SendInput(1, new[]{ i }, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput key");
    }
}
'@
}
[void][CommissionNative2]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][CommissionNative2]::SetThreadDpiAwarenessContext([IntPtr](-4))

$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($exe) -ne 'VintageJournalDesk.exe') { throw 'Refusing: expected VintageJournalDesk.exe.' }
if (@(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue).Count) { throw 'Another VintageJournalDesk is running; close it first.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$run = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $OutputDirectory "$run-player.log"
$checks = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()

# Isolated save: archive anything from an earlier probe (nothing is deleted).
$saveDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Afternoon Paper Studio\午后手账 · Vintage Journal Desk\CommissionNativeValidation'
if (Test-Path $saveDir) {
    $archive = Join-Path $saveDir "archive-$run"; New-Item -ItemType Directory -Force $archive | Out-Null
    Get-ChildItem $saveDir -File | Move-Item -Destination $archive
    if (Test-Path (Join-Path $saveDir 'commission-photos')) { Move-Item (Join-Path $saveDir 'commission-photos') (Join-Path $archive 'commission-photos') }
}

$player = Start-Process -FilePath $exe -PassThru -ArgumentList @('--commission-native-validation','-screen-width',$Width,'-screen-height',$Height,'-screen-fullscreen','0','-logFile',('"'+$log+'"'))
function Read-Log { if (-not (Test-Path $log)) { return '' }; $s=[IO.File]::Open($log,'Open','Read','ReadWrite'); $r=[IO.StreamReader]::new($s,[Text.Encoding]::UTF8); try { $r.ReadToEnd() } finally { $r.Dispose(); $s.Dispose() } }
function Pass([string]$name,[string]$evidence) { $checks.Add([ordered]@{name=$name;result='PASS';evidence=$evidence}); Write-Host "PASS $name :: $evidence" }
function Fail([string]$name,[string]$evidence) { $checks.Add([ordered]@{name=$name;result='FAIL';evidence=$evidence}); Write-Host "FAIL $name :: $evidence"; throw "$name failed: $evidence" }
function Wait-Log([int]$offset,[string]$pattern,[string]$name,[int]$ms=8000) {
    $t=[Diagnostics.Stopwatch]::StartNew()
    while ($t.ElapsedMilliseconds -lt $ms) { $c=Read-Log; if ($c.Length -ge $offset -and $c.Substring($offset) -match $pattern) { Pass $name $Matches[0]; return $Matches[0] }; Start-Sleep -Milliseconds 120 }
    Fail $name "no log match for $pattern within $ms ms"
}
$hwnd=[IntPtr]::Zero; $deadline=(Get-Date).AddSeconds(45)
while ((Get-Date) -lt $deadline) { $player.Refresh(); $hwnd=$player.MainWindowHandle; if ($hwnd -ne [IntPtr]::Zero -and (Read-Log) -match '\[Desk\] UI:Start') { break }; Start-Sleep -Milliseconds 250 }
if ($hwnd -eq [IntPtr]::Zero) { throw 'Player window never appeared.' }

function Confirm-Target {
    if ($player.HasExited) { throw 'Player exited.' }
    [uint32]$p=0; [void][CommissionNative2]::GetWindowThreadProcessId($hwnd,[ref]$p)
    if ($p -ne $player.Id) { throw 'Window identity changed.' }
    if ([CommissionNative2]::GetForegroundWindow() -ne $hwnd) { throw 'Player lost foreground; refusing global input.' }
}
function Focus-Target {
    [void][CommissionNative2]::ShowWindow($hwnd,9)
    $fg=[CommissionNative2]::GetForegroundWindow(); [uint32]$fp=0
    $ft=[CommissionNative2]::GetWindowThreadProcessId($fg,[ref]$fp); $ct=[CommissionNative2]::GetCurrentThreadId(); $att=$false
    try { if ($ft -ne 0 -and $ft -ne $ct) { $att=[CommissionNative2]::AttachThreadInput($ct,$ft,$true) }; [void][CommissionNative2]::BringWindowToTop($hwnd); [void][CommissionNative2]::SetForegroundWindow($hwnd) }
    finally { if ($att) { [void][CommissionNative2]::AttachThreadInput($ct,$ft,$false) } }
    Start-Sleep -Milliseconds 400; Confirm-Target
}
function Client { $r=[CommissionNative2+RECT]::new(); $o=[CommissionNative2+POINT]::new(); [void][CommissionNative2]::GetClientRect($hwnd,[ref]$r); [void][CommissionNative2]::ClientToScreen($hwnd,[ref]$o); @{X=$o.X;Y=$o.Y;W=$r.Right-$r.Left;H=$r.Bottom-$r.Top} }
function Move-To([double]$fx,[double]$fy) {
    Confirm-Target; $b=Client; $x=[int]($b.X+$fx*$b.W); $y=[int]($b.Y+$fy*$b.H)
    [void][CommissionNative2]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 160
    $p=[CommissionNative2+POINT]::new(); [void][CommissionNative2]::GetCursorPos([ref]$p)
    if ([Math]::Abs($p.X-$x) -gt 2 -or [Math]::Abs($p.Y-$y) -gt 2) { throw "Pointer not at target ($x,$y) but ($($p.X),$($p.Y)); client=$($b.X),$($b.Y),$($b.W)x$($b.H) screen=$([CommissionNative2]::GetSystemMetrics(0))x$([CommissionNative2]::GetSystemMetrics(1)); refusing to click." }
    if ([CommissionNative2]::GetAncestor([CommissionNative2]::WindowFromPoint($p),2) -ne $hwnd) { throw 'Another window covers the click point; refusing.' }
}
function Click([double]$fx,[double]$fy) { Move-To $fx $fy; [CommissionNative2]::Mouse(2); Start-Sleep -Milliseconds 90; [CommissionNative2]::Mouse(4); Start-Sleep -Milliseconds 300 }
function Drag([double[]]$from,[double[]]$to) {
    Move-To $from[0] $from[1]; [CommissionNative2]::Mouse(2); Start-Sleep -Milliseconds 150
    try { for ($i=1; $i -le 14; $i++) { $k=$i/14; Move-To ($from[0]+($to[0]-$from[0])*$k) ($from[1]+($to[1]-$from[1])*$k); Start-Sleep -Milliseconds 30 } }
    finally { Start-Sleep -Milliseconds 150; [CommissionNative2]::Mouse(4) }
    Start-Sleep -Milliseconds 400
}
function Key([uint16]$vk,[uint16]$mod=0) { Confirm-Target; if ($mod) { [CommissionNative2]::Key($mod,$true) }; [CommissionNative2]::Key($vk,$true); Start-Sleep -Milliseconds 70; [CommissionNative2]::Key($vk,$false); if ($mod) { [CommissionNative2]::Key($mod,$false) }; Start-Sleep -Milliseconds 250 }
function Targets {
    $offset=(Read-Log).Length; Key 0x78
    $line=Wait-Log $offset '\[CommissionTargets\] [^\r\n]+' 'F9 target report' 4000
    $map=@{}; foreach ($part in ($line -replace '^\[CommissionTargets\] ','').Split(';')) { $kv=$part.Split('=',2); if ($kv.Count -eq 2) { $map[$kv[0]]=$kv[1] } }
    return $map
}
function Pt($map,[string]$key) { if (-not $map.ContainsKey($key) -or $map[$key] -eq 'behind') { Fail "Target $key" 'not reported on screen' }; $v=$map[$key].Split(','); return @([double]$v[0],[double]$v[1]) }
function Shot([string]$name) {
    Confirm-Target; $b=Client
    if ($b.X -lt 0 -or $b.Y -lt 0 -or $b.X+$b.W -gt [CommissionNative2]::GetSystemMetrics(0) -or $b.Y+$b.H -gt [CommissionNative2]::GetSystemMetrics(1)) { Write-Host "skip screenshot $name (client off primary screen)"; return }
    $bmp=[Drawing.Bitmap]::new($b.W,$b.H); $g=[Drawing.Graphics]::FromImage($bmp); $path=Join-Path $OutputDirectory "$run-$name.png"
    try { $g.CopyFromScreen($b.X,$b.Y,0,0,[Drawing.Size]::new($b.W,$b.H)); $bmp.Save($path,[Drawing.Imaging.ImageFormat]::Png) } finally { $g.Dispose(); $bmp.Dispose() }
    $shots.Add([IO.Path]::GetFileName($path))
}

$status='FAIL'; $error1=$null
try {
    Focus-Target; Shot 'start'
    $o=(Read-Log).Length; Click (411/1920) (679/1080); Wait-Log $o '\[Desk\] UI:Begin' 'Click 坐到桌前 starts the desk'
    Wait-Log 0 '\[Commission\] Arrived:letter-001' 'First letter arrives' 10000 | Out-Null
    Move-To .5 .5; Start-Sleep -Milliseconds 400
    $t=Targets; Shot 'desk-unread'
    $m=Pt $t 'mailbox'; $o=(Read-Log).Length; Click $m[0] $m[1]
    Wait-Log $o '\[Commission\] Panel:open:letter:letter-001' 'Real click on the floating mailbox opens letter-001' | Out-Null
    Shot 'letter-panel'

    $t=Targets
    $o=(Read-Log).Length; Drag (Pt $t 'sticker_02') (Pt $t 'journal-page-001@0.45')
    Wait-Log $o '\[Workspace\] Attach:[^:\r\n]+:JournalPage:journal:journal-page-001' 'Drag rose sticker from rack onto journal page 1' | Out-Null
    $t=Targets
    $o=(Read-Log).Length; Drag (Pt $t 'sticker_01') (Pt $t 'journal-page-001@0.55')
    Wait-Log $o '\[Workspace\] Attach:[^:\r\n]+:JournalPage:journal:journal-page-001' 'Drag botanical sticker onto the same page' | Out-Null
    Move-To .5 .93; Start-Sleep -Milliseconds 300
    $t=Targets; Shot 'checklist'
    if (-not $t.ContainsKey('ui:拍照寄出')) { Fail 'Send button drawn' ("panel=" + $t['panel']) }
    $b=Pt $t 'ui:拍照寄出'; $o=(Read-Log).Length; Click $b[0] $b[1]
    Wait-Log $o '\[CommissionUI\] Click:拍照寄出' 'Real click reaches the send button' | Out-Null
    $sent=Wait-Log $o '\[Commission\] Sent:letter-001:JournalPage:journal-page-001:tier\d:photo=True' 'Send photographs journal page 1'
    $leak=(Read-Log).Substring($o) -match '\[Workspace\] (Select|MaterialGrab|PhysicalGrab|Peel)'
    if ($leak) { Fail 'Panel click does not leak to the desk' 'workspace select/grab logged during panel click' } else { Pass 'Panel click does not leak to the desk' 'no workspace select/grab after the button click' }
    Shot 'sent'

    $o=(Read-Log).Length; Key 0x5A 0x11
    Wait-Log $o '\[Craft\] 撤销制作操作' 'Ctrl+Z undoes the last sticker placement' | Out-Null
    $o=(Read-Log).Length; Key 0x59 0x11
    Wait-Log $o '\[Craft\] 重做制作操作' 'Ctrl+Y redoes it' | Out-Null

    Wait-Log 0 '\[Commission\] Arrived:letter-002' 'Next letter arrives after sending' 15000 | Out-Null
    Wait-Log 0 '\[Commission\] Reply:letter-001:tier\d' 'Reply arrives in real time' 40000 | Out-Null
    $t=Targets
    if ($t['panel'] -ne 'closed') { $x=Pt $t 'ui:×'; $o=(Read-Log).Length; Click $x[0] $x[1]; Wait-Log $o '\[Commission\] Panel:closed' 'Real click on × closes the panel' | Out-Null }
    Move-To .5 .93; Start-Sleep -Milliseconds 300
    $t=Targets; $c=Pt $t 'ui:chip'; $o=(Read-Log).Length; Click $c[0] $c[1]
    Wait-Log $o '\[Commission\] Panel:open:reply:letter-001' 'Real click on the letter chip opens the reply' | Out-Null
    Shot 'reply'
    $t=Targets; $k=Pt $t 'ui:继续看委托'; $o=(Read-Log).Length; Click $k[0] $k[1]
    Wait-Log $o '\[CommissionUI\] Click:继续看委托' 'Continue button switches to the next commission' | Out-Null
    $t=Targets; $k=Pt $t 'ui:收起'; $o=(Read-Log).Length; Click $k[0] $k[1]
    Wait-Log $o '\[CommissionUI\] Click:收起' 'Collapse button' | Out-Null
    Move-To .5 .93; Start-Sleep -Milliseconds 300; $t=Targets
    if ($t['panel'] -ne 'compact') { Fail 'Panel is compact after collapse' $t['panel'] } else { Pass 'Panel is compact after collapse' 'panel=compact' }
    Shot 'compact'

    # Graceful close, then read-only save check.
    $o=(Read-Log).Length; [void]$player.CloseMainWindow()
    if (-not $player.WaitForExit(20000)) { Fail 'Graceful close' 'player did not exit in 20 s' }
    Pass 'Graceful close' ("exit code " + $player.ExitCode)
    $save=Join-Path $saveDir 'journal-v1.json'
    $doc=[IO.File]::ReadAllText($save,[Text.Encoding]::UTF8) | ConvertFrom-Json
    $letters=@($doc.commissionDesk.letters); $first=$letters | Where-Object { $_.letterId -eq 'letter-001' }
    $onPage=@($doc.craftWorkspace.items | Where-Object { [int]$_.owner -eq 2 -and $_.pageId -eq 'journal-page-001' })
    $ok = $doc.commissionDeskEnabled -and [int]$first.status -eq 3 -and [int]$doc.commissionDesk.stamps -eq [int]$first.stamps -and $onPage.Count -eq 2 -and
          (Test-Path (Join-Path $saveDir ('commission-photos\' + $first.photoFile)))
    if ($ok) { Pass 'Saved file after close: letter replied, stamps match, both stickers still on page 1, photo exists' ("letters=$($letters.Count) stamps=$($doc.commissionDesk.stamps) tier=$($first.tier)") }
    else { Fail 'Saved file after close' ("status=$($first.status) stamps=$($doc.commissionDesk.stamps)/$($first.stamps) onPage=$($onPage.Count)") }
    $errs=([regex]::Matches((Read-Log),'(?m)^(NullReferenceException|.*Exception:).*$')).Count
    if ($errs -eq 0) { Pass 'No exceptions in player log' '0' } else { Fail 'No exceptions in player log' "$errs exception lines" }
    $status='PASS'
}
catch { $error1=$_.Exception.Message; Write-Host "ABORT: $error1" }
finally {
    if (-not $player.HasExited) { [void]$player.CloseMainWindow(); if (-not $player.WaitForExit(10000)) { $player.Kill() } }
    $report=[ordered]@{ timestampUtc=[DateTime]::UtcNow.ToString('o'); result=$status; abort=$error1; input='Windows SendInput mouse and scan-code keyboard to the verified foreground player'; save=$saveDir;
        client="$Width x $Height windowed"; checks=$checks; screenshots=$shots; playerLog=[IO.Path]::GetFileName($log) }
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory "$run-native-results.json")
    Write-Host "Result: $status ($(@($checks | Where-Object { $_.result -eq 'PASS' }).Count) PASS)"
}
