[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\night-gameplay\native'),
    [int]$Width = 1280,
    [int]$Height = 720,
    [double]$CatchVirtualX = 666,
    [double]$CatchVirtualY = 198,
    [switch]$IncludePlacement
)
# Native Windows mouse/keyboard only. Runtime logs supply optional targets; the isolated JSON save supplies assertions.
# This file does not invoke Unity actions, modify a save, delete earlier evidence, or kill any process.
$ErrorActionPreference = 'Stop'
if (@(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue).Count -gt 0) { throw 'A demo is already running; refusing to send input or interrupt it.' }
$nightExe = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($nightExe) -ne 'VintageJournalDesk.exe') { throw 'Expected VintageJournalDesk.exe.' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory))
$nightOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$nightRunId = [Guid]::NewGuid().ToString('N')
$nightLog = Join-Path $nightOutput 'player.log'
$nightReportPath = Join-Path $nightOutput 'result.json'
if (Test-Path -LiteralPath $nightLog) { Copy-Item -LiteralPath $nightLog -Destination (Join-Path $nightOutput ('player-before-' + $nightRunId + '.log')) }
if (Test-Path -LiteralPath $nightReportPath) { Copy-Item -LiteralPath $nightReportPath -Destination (Join-Path $nightOutput ('result-before-' + $nightRunId + '.json')) }
$nightSavePath = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('AppData\LocalLow\Afternoon Paper Studio\午后手账 · Vintage Journal Desk\DeskRevisionValidation\' + $nightRunId + '\journal-v1.json')
$nightChecks = [Collections.Generic.List[object]]::new()
$nightScreenshots = [Collections.Generic.List[string]]::new()
$nightPlayer = $null
$nightWindow = [IntPtr]::Zero
$nightStatus = 'FAIL'
$nightFailure = $null
$nightSourceId = $null
$nightInitialPending = $null
$nightInputSent = $false

Add-Type -AssemblyName System.Drawing
if (-not ('NightGameplayNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class NightGameplayNative {
    delegate bool EnumWindowCallback(IntPtr window,IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort virtualKey,scanCode; public uint flags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT key; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION data; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code,uint mode);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window,out RECT rectangle);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window,ref POINT point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    public static IntPtr FindWindowForProcess(uint requestedProcess) {
        IntPtr candidate=IntPtr.Zero,unity=IntPtr.Zero;
        EnumWindows((window,parameter)=>{
            uint process;GetWindowThreadProcessId(window,out process);
            if(process!=requestedProcess)return true;
            if(candidate==IntPtr.Zero)candidate=window;
            var name=new StringBuilder(256);GetClassName(window,name,name.Capacity);
            if(name.ToString().IndexOf("Unity",StringComparison.OrdinalIgnoreCase)>=0){unity=window;return false;}
            return true;
        },IntPtr.Zero);
        return unity!=IntPtr.Zero?unity:candidate;
    }
    public static bool RequestClose(IntPtr window) {
        UIntPtr result;
        // WM_CLOSE addressed to the verified test HWND. SMTO_ABORTIFHUNG keeps a stuck player from blocking the harness indefinitely.
        return SendMessageTimeout(window,0x0010,UIntPtr.Zero,IntPtr.Zero,2,5000,out result)!=IntPtr.Zero;
    }
    public static void Mouse(uint flags) {
        var input=new INPUT{type=0,data=new UNION{mouse=new MOUSEINPUT{dwFlags=flags}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput mouse");
    }
    public static void Key(ushort key,bool down) {
        uint scan=MapVirtualKeyW(key,4);if(scan==0)throw new InvalidOperationException("Missing key scan code");
        var input=new INPUT{type=1,data=new UNION{key=new KEYBDINPUT{scanCode=(ushort)(scan&255),flags=8u|((scan&0xff00)==0xe000?1u:0u)|(down?0u:2u)}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput key");
    }
}
'@
}
[void][NightGameplayNative]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][NightGameplayNative]::SetThreadDpiAwarenessContext([IntPtr](-4))

function Read-NightLog {
    if (-not (Test-Path -LiteralPath $nightLog)) { return '' }
    $stream = [IO.File]::Open($nightLog, 'Open', 'Read', 'ReadWrite')
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}
function Read-NightSave {
    if (-not (Test-Path -LiteralPath $nightSavePath)) { return $null }
    try { return ([IO.File]::ReadAllText($nightSavePath, [Text.Encoding]::UTF8) | ConvertFrom-Json) } catch { return $null }
}
function Add-NightCheck([string]$Name, [bool]$Passed, [string]$Evidence) {
    $nightChecks.Add([ordered]@{name=$Name; passed=$Passed; evidence=$Evidence})
    $prefix = if ($Passed) { 'PASS ' } else { 'FAIL ' }
    Write-Output ($prefix + $Name + ': ' + $Evidence)
    if (-not $Passed) { throw ($Name + ': ' + $Evidence) }
}
function Wait-NightLog([string]$Pattern, [int]$TimeoutMs = 10000, [int]$Offset = 0) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $nightPlayer.Refresh(); if ($nightPlayer.HasExited) { throw 'Test player exited unexpectedly.' }
        $content = Read-NightLog
        if ($content.Length -ge $Offset -and $content.Substring($Offset) -match $Pattern) { return $Matches[0] }
        Start-Sleep -Milliseconds 100
    }
    throw ('No player log match within ' + $TimeoutMs + ' ms: ' + $Pattern)
}
function Wait-NightSave([scriptblock]$Predicate, [string]$Description, [int]$TimeoutMs = 8000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $document = Read-NightSave
        if ($null -ne $document -and (& $Predicate $document)) { return $document }
        Start-Sleep -Milliseconds 100
    }
    throw ('Saved state did not confirm ' + $Description + ' within ' + $TimeoutMs + ' ms.')
}
function Confirm-NightWindow {
    $nightPlayer.Refresh(); if ($nightPlayer.HasExited) { throw 'Test player has exited.' }
    [uint32]$nightWindowPid = 0
    [void][NightGameplayNative]::GetWindowThreadProcessId($nightWindow, [ref]$nightWindowPid)
    if ($nightWindowPid -ne $nightPlayer.Id) { throw 'Test window no longer belongs to the launched PID.' }
    if ([NightGameplayNative]::GetForegroundWindow() -ne $nightWindow) { throw 'Test player lost foreground; refusing global input.' }
}
function Focus-NightWindow {
    [void][NightGameplayNative]::ShowWindow($nightWindow,9)
    $foreground = [NightGameplayNative]::GetForegroundWindow(); [uint32]$foregroundProcess = 0
    $foregroundThread = [NightGameplayNative]::GetWindowThreadProcessId($foreground,[ref]$foregroundProcess)
    $currentThread = [NightGameplayNative]::GetCurrentThreadId(); $attached = $false
    try {
        if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) { $attached = [NightGameplayNative]::AttachThreadInput($currentThread,$foregroundThread,$true) }
        [void][NightGameplayNative]::BringWindowToTop($nightWindow); [void][NightGameplayNative]::SetForegroundWindow($nightWindow)
    } finally { if ($attached) { [void][NightGameplayNative]::AttachThreadInput($currentThread,$foregroundThread,$false) } }
    Start-Sleep -Milliseconds 350; Confirm-NightWindow
}
function Get-NightClient {
    $rectangle = [NightGameplayNative+RECT]::new(); $origin = [NightGameplayNative+POINT]::new()
    if (-not [NightGameplayNative]::GetClientRect($nightWindow,[ref]$rectangle) -or -not [NightGameplayNative]::ClientToScreen($nightWindow,[ref]$origin)) { throw 'Cannot read test window client bounds.' }
    return @{X=$origin.X; Y=$origin.Y; W=$rectangle.Right-$rectangle.Left; H=$rectangle.Bottom-$rectangle.Top}
}
function Move-NightPointer([double]$X, [double]$Y) {
    Confirm-NightWindow
    if ($X -le 0 -or $Y -le 0 -or $X -ge 1 -or $Y -ge 1) { throw 'Pointer target must be inside test client.' }
    $client = Get-NightClient; $pixelX = [int]($client.X + $X*$client.W); $pixelY = [int]($client.Y + $Y*$client.H)
    [void][NightGameplayNative]::SetCursorPos($pixelX,$pixelY); Start-Sleep -Milliseconds 120
    $point = [NightGameplayNative+POINT]::new(); [void][NightGameplayNative]::GetCursorPos([ref]$point)
    if ([Math]::Abs($point.X-$pixelX) -gt 2 -or [Math]::Abs($point.Y-$pixelY) -gt 2) { throw 'Pointer failed to reach the intended client position.' }
    if ([NightGameplayNative]::GetAncestor([NightGameplayNative]::WindowFromPoint($point),2) -ne $nightWindow) { throw 'Another window covers the target; refusing input.' }
}
function Click-Night([double]$X, [double]$Y) {
    Move-NightPointer $X $Y; Confirm-NightWindow
    [NightGameplayNative]::Mouse(2)
    $script:nightInputSent = $true
    try { Start-Sleep -Milliseconds 90 } finally { [NightGameplayNative]::Mouse(4) }
    Start-Sleep -Milliseconds 250
}
function Key-Night([uint16]$KeyCode) {
    Confirm-NightWindow; [NightGameplayNative]::Key($KeyCode,$true)
    $script:nightInputSent = $true
    try { Start-Sleep -Milliseconds 70 } finally { [NightGameplayNative]::Key($KeyCode,$false) }
    Start-Sleep -Milliseconds 250
}
function Capture-Night([string]$Name) {
    Confirm-NightWindow; $client = Get-NightClient
    if ($client.X -lt 0 -or $client.Y -lt 0 -or $client.X+$client.W -gt [NightGameplayNative]::GetSystemMetrics(0) -or $client.Y+$client.H -gt [NightGameplayNative]::GetSystemMetrics(1)) { throw 'Test window is outside primary screen; cannot honestly capture its UI.' }
    $bitmap = [Drawing.Bitmap]::new($client.W,$client.H); $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $path = Join-Path $nightOutput ($nightRunId + '-' + $Name + '.png')
    try { $graphics.CopyFromScreen($client.X,$client.Y,0,0,[Drawing.Size]::new($client.W,$client.H)); $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $nightScreenshots.Add($path)
}

try {
    $arguments = @('--desk-revision-validation',('--revision-run='+$nightRunId),'--night-gameplay-validation','--dream-night','--commission-native-validation','--workspace-native-validation',
        '-screen-width',$Width,'-screen-height',$Height,'-screen-fullscreen','0','-logFile',('"'+$nightLog+'"'))
    $nightPlayer = Start-Process -FilePath $nightExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $ready = [Diagnostics.Stopwatch]::StartNew()
    while ($ready.Elapsed.TotalSeconds -lt 45) {
        $nightPlayer.Refresh(); if ($nightPlayer.HasExited) { throw 'Test player exited during startup.' }
        # Process.MainWindowHandle intentionally ignores hidden windows. Discover this PID's top-level Unity HWND directly.
        $nightWindow = [NightGameplayNative]::FindWindowForProcess([uint32]$nightPlayer.Id)
        if ($nightWindow -ne [IntPtr]::Zero -and (Read-NightLog) -match '\[NightGameplay\] Ready') { break }
        Start-Sleep -Milliseconds 200
    }
    if ($nightWindow -eq [IntPtr]::Zero) { throw 'Test player did not create a window.' }
    Focus-NightWindow; Capture-Night 'start-ui'
    $offset = (Read-NightLog).Length
    Click-Night (410/1920) (679/1080)
    $evidence = Wait-NightLog '\[Desk\] UI:Begin' 10000 $offset
    Add-NightCheck 'Native start button' $true $evidence
    [void](Wait-NightLog '\[NightGameplay\] Ready' 10000)
    $initial = Wait-NightSave { param($d) $d.dreamGameplay.initialized -and @($d.dreamGameplay.pending).Count -eq 3 } 'three initial collection tokens'
    $nightInitialPending = @($initial.dreamGameplay.pending).Count
    Capture-Night 'night-desk-ui'
    Key-Night 0x77 # F8 requests diagnostics; it never invokes gameplay actions.
    $catchX = $CatchVirtualX/1920; $catchY = $CatchVirtualY/1080
    # Future diagnostic targets take precedence; current builds fall back to the calibrated fixed seated-camera position.
    $targetLines = [regex]::Matches((Read-NightLog),'\[(?:NightCollection|WorkspaceInput)\] Target (\{[^\r\n]+\})')
    foreach ($match in $targetLines) {
        try { $target = $match.Groups[1].Value | ConvertFrom-Json } catch { continue }
        if ($target.type -eq 'NightCollectible' -and [double]$target.x -gt 0 -and [double]$target.y -gt 0) { $catchX=[double]$target.x; $catchY=[double]$target.y; break }
    }
    Click-Night $catchX $catchY
    $caught = Wait-NightSave { param($d) @($d.dreamGameplay.pending).Count -eq 2 -and @($d.craftWorkspace.items | Where-Object { $_.instanceId -like 'dream-catch-*' -and [int]$_.owner -eq 0 }).Count -eq 1 } 'one click consumes one token and grants one stock instance'
    $stock = @($caught.craftWorkspace.items | Where-Object { $_.instanceId -like 'dream-catch-*' -and [int]$_.owner -eq 0 })[0]
    $nightSourceId = $stock.instanceId
    Add-NightCheck 'Native collection grants finite stock once' $true ('pending 3 -> 2; ' + $nightSourceId + '; asset=' + $stock.assetId)
    Capture-Night 'caught-ui'
    $bagAssets = @('night-glow-ink','night-star-sticker','night-lantern-paper','night-moonlit-star','night-forgotten-dream')
    $bagIndex = [Array]::IndexOf($bagAssets,[string]$stock.assetId)
    if ($bagIndex -lt 0) { throw 'Collected asset has no visible bag button.' }
    $takeX = (444+246*$bagIndex)/1920
    Click-Night (1818/1920) (188/1080); Capture-Night 'material-bag-ui'
    Click-Night $takeX (644/1080)
    [void](Wait-NightSave { param($d) @($d.craftWorkspace.items | Where-Object { $_.instanceId -eq $nightSourceId -and [int]$_.owner -eq 4 }).Count -eq 1 } 'bag take button creates held transaction')
    Add-NightCheck 'Native bag and take buttons create held transaction' $true $nightSourceId
    Capture-Night 'held-ui'
    Key-Night 0x1B
    [void](Wait-NightSave { param($d) @($d.craftWorkspace.items | Where-Object { $_.instanceId -eq $nightSourceId -and [int]$_.owner -eq 0 }).Count -eq 1 -and @($d.craftWorkspace.items | Where-Object { [int]$_.owner -eq 4 }).Count -eq 0 } 'ESC restores the same stock instance')
    Add-NightCheck 'Native ESC restores identical finite stock' $true $nightSourceId
    Click-Night (1818/1920) (188/1080); Capture-Night 'bag-after-cancel-ui'
    if ($IncludePlacement) {
        Click-Night $takeX (644/1080)
        [void](Wait-NightSave { param($d) @($d.craftWorkspace.items | Where-Object { $_.instanceId -eq $nightSourceId -and [int]$_.owner -eq 4 }).Count -eq 1 } 'second take reuses source instance')
        $targetOffset = (Read-NightLog).Length; Key-Night 0x77
        $pageX = 925/1920; $pageY = 625/1080
        $recent = (Read-NightLog).Substring($targetOffset)
        foreach ($match in [regex]::Matches($recent,'\[WorkspaceInput\] Page (\{[^\r\n]+\})')) {
            $target = $match.Groups[1].Value | ConvertFrom-Json
            if ($target.type -eq 'JournalPage' -and $target.pageId -eq 'journal-page-001') { $pageX=[double]$target.x; $pageY=[double]$target.y; break }
        }
        Click-Night $pageX $pageY
        $placed = Wait-NightSave { param($d) @($d.craftWorkspace.items | Where-Object { $_.instanceId -eq $nightSourceId -and [int]$_.owner -eq 2 }).Count -eq 1 } 'native click places collected material on journal'
        $projections = @($placed.pages | ForEach-Object { $_.decorations } | Where-Object { $_.instanceId -eq $nightSourceId })
        Add-NightCheck 'Native placement preserves one source and one page projection' ($projections.Count -eq 1 -and @($placed.craftWorkspace.items | Where-Object { $_.instanceId -eq $nightSourceId }).Count -eq 1) $nightSourceId
        Capture-Night 'placed-material-ui'
    } else {
        Key-Night 0x1B
    }
    $exceptionLines = @([regex]::Matches((Read-NightLog),'(?m)^(?:NullReferenceException|[^\r\n]*Exception:)[^\r\n]*$'))
    Add-NightCheck 'No player exceptions during native actions' ($exceptionLines.Count -eq 0) ($exceptionLines.Count.ToString() + ' exception lines')
    $nightStatus = 'PASS'
} catch {
    $nightFailure = $_.Exception.Message
    $nightChecks.Add([ordered]@{name='Native probe completed';passed=$false;evidence=$nightFailure})
    try { if ($nightPlayer -and -not $nightPlayer.HasExited -and [NightGameplayNative]::GetForegroundWindow() -eq $nightWindow) { Capture-Night 'failure-ui' } } catch { }
} finally {
    if ($nightPlayer) {
        $nightPlayer.Refresh()
        if (-not $nightPlayer.HasExited) {
            # Address only the HWND owned by the process this script started, including a still-hidden startup window.
            $closingWindow = [NightGameplayNative]::FindWindowForProcess([uint32]$nightPlayer.Id)
            if ($closingWindow -ne [IntPtr]::Zero) {
                [uint32]$closingProcess = 0
                [void][NightGameplayNative]::GetWindowThreadProcessId($closingWindow,[ref]$closingProcess)
                if ($closingProcess -eq [uint32]$nightPlayer.Id) { [void][NightGameplayNative]::RequestClose($closingWindow) }
            }
            if (-not $nightPlayer.WaitForExit(15000)) { $nightStatus='FAIL'; $nightFailure=($nightFailure + ' Test player did not close normally; it was left running.').Trim() }
        }
        if ($nightPlayer.HasExited -and $nightPlayer.ExitCode -ne 0) { $nightStatus='FAIL'; $nightFailure=($nightFailure + ' Test player exit code: ' + $nightPlayer.ExitCode).Trim() }
    }
    $final = Read-NightSave
    $nightReport = [ordered]@{
        timestampUtc=[DateTime]::UtcNow.ToString('o'); result=$nightStatus; failure=$nightFailure; runId=$nightRunId
        actualNativeInput=$nightInputSent; runtimeActionsInvoked=$false; screenshotScope='Real Windows client capture, including IMGUI and text'
        executable=$nightExe; processId=if($nightPlayer){$nightPlayer.Id}else{$null}; savePath=$nightSavePath; logPath=$nightLog
        client=($Width.ToString()+'x'+$Height+' windowed'); initialPending=$nightInitialPending
        finalPending=if($final){@($final.dreamGameplay.pending).Count}else{$null}; sourceInstanceId=$nightSourceId
        checks=$nightChecks; screenshots=$nightScreenshots
        placementRequested=[bool]$IncludePlacement
        notValidated=@('Full commission/reply flow with native mouse','Fresh-process restart','Multi-hour replenishment') + $(if(-not $IncludePlacement){@('Native page placement')}else{@()})
    }
    $nightReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $nightReportPath -Encoding UTF8
    Write-Output ('Native night result: '+$nightStatus+'; report='+$nightReportPath)
}
if ($nightStatus -ne 'PASS') { throw ('Native night probe failed: '+$nightFailure) }
