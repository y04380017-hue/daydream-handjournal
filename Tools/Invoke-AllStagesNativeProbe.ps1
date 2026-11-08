[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{32}$')][string]$RunId,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\Build\Windows\VintageJournalDesk.exe'),
    [ValidateSet('WindowCharacters','ScanCodes')][string]$TextTransport='WindowCharacters'
)
# Sends real native mouse/key events only to the verified test PID. Runtime gameplay APIs are never called.
$ErrorActionPreference='Stop'
if(@(Get-Process VintageJournalDesk -ErrorAction SilentlyContinue).Count -gt 0){throw 'A demo is running; refusing input and leaving it untouched.'}
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
if(-not (Test-Path -LiteralPath $allSavePath -PathType Leaf)){throw 'This test requires an existing FullProbe GUID-isolated save; no new save will be substituted.'}
$allChecks=[Collections.Generic.List[object]]::new()
$allScreenshots=[Collections.Generic.List[string]]::new()
$allPlayer=$null;$allWindow=[IntPtr]::Zero;$allInputSent=$false
$allImeChanged=$false;$allImeWasOpen=$false
$allStatus='FAIL';$allFailure=$null;$allDiarySaved=$false;$allNewArtId=$null
$allInitialTemplates=$null;$allFinalTemplates=$null
Add-Type -AssemblyName System.Drawing
if (-not ('AllStagesNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class AllStagesNative {
    delegate bool EnumWindowCallback(IntPtr window,IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort virtualKey,scanCode; public uint flags,time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT key; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION data; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code,uint mode);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint code,uint mode,IntPtr layout);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern short VkKeyScanExW(char character,IntPtr layout);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window,IntPtr context);
    [DllImport("imm32.dll")] static extern bool ImmGetOpenStatus(IntPtr context);
    [DllImport("imm32.dll")] static extern bool ImmSetOpenStatus(IntPtr context,bool open);
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
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetPhysicalCursorPos(int x,int y);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetPhysicalCursorPos(out POINT point);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetClipCursor(out RECT rectangle);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
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
    public static bool WindowCharacter(IntPtr window,char character) {
        UIntPtr result;
        // WM_CHAR is addressed only to the verified test window. This is an OS text event, not a hardware-key claim.
        return SendMessageTimeout(window,0x0102,new UIntPtr(character),new IntPtr(1),2,5000,out result)!=IntPtr.Zero;
    }
    public static void Mouse(uint flags) {
        var input=new INPUT{type=0,data=new UNION{mouse=new MOUSEINPUT{dwFlags=flags}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput mouse");
    }
    public static void Unicode(char character,bool down) {
        var input=new INPUT{type=1,data=new UNION{key=new KEYBDINPUT{scanCode=character,flags=4u|(down?0u:2u)}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput Unicode");
    }
    public static bool ImeOpen(IntPtr window) {
        var context=ImmGetContext(window);if(context==IntPtr.Zero)return false;
        try{return ImmGetOpenStatus(context);}finally{ImmReleaseContext(window,context);}
    }
    public static bool SetImeOpen(IntPtr window,bool open) {
        var context=ImmGetContext(window);if(context==IntPtr.Zero)return !open;
        try{return ImmSetOpenStatus(context,open);}finally{ImmReleaseContext(window,context);}
    }
    public static long LayoutForWindow(IntPtr window) {uint process;uint thread=GetWindowThreadProcessId(window,out process);return GetKeyboardLayout(thread).ToInt64();}
    public static bool ModifierDown(int virtualKey) {return (GetAsyncKeyState(virtualKey)&0x8000)!=0;}
    public static void AsciiKey(IntPtr window,char character,bool down) {
        if(character>127)throw new ArgumentException("Native probe covers ASCII typing only.");
        uint process;uint thread=GetWindowThreadProcessId(window,out process);var layout=GetKeyboardLayout(thread);
        short mapped=VkKeyScanExW(character,layout);
        if(mapped==-1||((mapped>>8)&255)!=0)throw new InvalidOperationException("ASCII character needs an unsupported layout modifier.");
        uint scan=MapVirtualKeyExW((uint)(mapped&255),4,layout);if(scan==0)throw new InvalidOperationException("Missing ASCII hardware scan code.");
        var input=new INPUT{type=1,data=new UNION{key=new KEYBDINPUT{scanCode=(ushort)(scan&255),flags=8u|((scan&0xff00)==0xe000?1u:0u)|(down?0u:2u)}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput ASCII scan code");
    }
    public static void Key(ushort key,bool down) {
        uint scan=MapVirtualKeyW(key,4);if(scan==0)throw new InvalidOperationException("Missing key scan code");
        var input=new INPUT{type=1,data=new UNION{key=new KEYBDINPUT{scanCode=(ushort)(scan&255),flags=8u|((scan&0xff00)==0xe000?1u:0u)|(down?0u:2u)}}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput key");
    }
}
'@
}
[void][AllStagesNative]::SetProcessDpiAwarenessContext([IntPtr](-4))
[void][AllStagesNative]::SetThreadDpiAwarenessContext([IntPtr](-4))

function Read-AllLog {
    if (-not (Test-Path -LiteralPath $allLog)) { return '' }
    $stream = [IO.File]::Open($allLog, 'Open', 'Read', 'ReadWrite')
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}
function Read-AllSave {
    if (-not (Test-Path -LiteralPath $allSavePath)) { return $null }
    try { return ([IO.File]::ReadAllText($allSavePath, [Text.Encoding]::UTF8) | ConvertFrom-Json) } catch { return $null }
}
function Add-AllCheck([string]$Name, [bool]$Passed, [string]$Evidence) {
    $allChecks.Add([ordered]@{name=$Name; passed=$Passed; evidence=$Evidence})
    $prefix = if ($Passed) { 'PASS ' } else { 'FAIL ' }
    Write-Output ($prefix + $Name + ': ' + $Evidence)
    if (-not $Passed) { throw ($Name + ': ' + $Evidence) }
}
function Wait-AllLog([string]$Pattern, [int]$TimeoutMs = 10000, [int]$Offset = 0) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $allPlayer.Refresh(); if ($allPlayer.HasExited) { throw 'Test player exited unexpectedly.' }
        $content = Read-AllLog
        if ($content.Length -ge $Offset -and $content.Substring($Offset) -match $Pattern) { return $Matches[0] }
        Start-Sleep -Milliseconds 100
    }
    throw ('No player log match within ' + $TimeoutMs + ' ms: ' + $Pattern)
}
function Wait-AllSave([scriptblock]$Predicate, [string]$Description, [int]$TimeoutMs = 8000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $document = Read-AllSave
        if ($null -ne $document -and (& $Predicate $document)) { return $document }
        Start-Sleep -Milliseconds 100
    }
    throw ('Saved state did not confirm ' + $Description + ' within ' + $TimeoutMs + ' ms.')
}
function Confirm-AllWindow {
    $allPlayer.Refresh(); if ($allPlayer.HasExited) { throw 'Test player has exited.' }
    [uint32]$allWindowPid = 0
    [void][AllStagesNative]::GetWindowThreadProcessId($allWindow, [ref]$allWindowPid)
    if ($allWindowPid -ne $allPlayer.Id) { throw 'Test window no longer belongs to the launched PID.' }
    if ([AllStagesNative]::GetForegroundWindow() -ne $allWindow) { throw 'Test player lost foreground; refusing global input.' }
}
function Focus-AllWindow {
    [void][AllStagesNative]::ShowWindow($allWindow,9)
    $foreground = [AllStagesNative]::GetForegroundWindow(); [uint32]$foregroundProcess = 0
    $foregroundThread = [AllStagesNative]::GetWindowThreadProcessId($foreground,[ref]$foregroundProcess)
    $currentThread = [AllStagesNative]::GetCurrentThreadId(); $attached = $false
    try {
        if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) { $attached = [AllStagesNative]::AttachThreadInput($currentThread,$foregroundThread,$true) }
        [void][AllStagesNative]::BringWindowToTop($allWindow); [void][AllStagesNative]::SetForegroundWindow($allWindow)
    } finally { if ($attached) { [void][AllStagesNative]::AttachThreadInput($currentThread,$foregroundThread,$false) } }
    Start-Sleep -Milliseconds 350; Confirm-AllWindow
}
function Get-AllClient {
    $rectangle = [AllStagesNative+RECT]::new(); $origin = [AllStagesNative+POINT]::new()
    if (-not [AllStagesNative]::GetClientRect($allWindow,[ref]$rectangle) -or -not [AllStagesNative]::ClientToScreen($allWindow,[ref]$origin)) { throw 'Cannot read test window client bounds.' }
    return @{X=$origin.X; Y=$origin.Y; W=$rectangle.Right-$rectangle.Left; H=$rectangle.Bottom-$rectangle.Top}
}
function Move-AllPointer([double]$X, [double]$Y) {
    if ($X -le 0 -or $Y -le 0 -or $X -ge 1 -or $Y -ge 1) { throw 'Pointer target must be inside test client.' }
    $lastDetail=''
    for($attempt=1;$attempt -le 3;$attempt++) {
        Confirm-AllWindow
        # Reapply the physical-pixel coordinate context on the calling thread after focus/AttachThreadInput.
        [void][AllStagesNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
        $client=Get-AllClient;$pixelX=[int]($client.X+$X*$client.W);$pixelY=[int]($client.Y+$Y*$client.H)
        $setOk=[AllStagesNative]::SetPhysicalCursorPos($pixelX,$pixelY)
        $setError=if($setOk){0}else{[Runtime.InteropServices.Marshal]::GetLastWin32Error()}
        Start-Sleep -Milliseconds 55
        $point=[AllStagesNative+POINT]::new();$physical=[AllStagesNative+POINT]::new();$clip=[AllStagesNative+RECT]::new()
        $getOk=[AllStagesNative]::GetCursorPos([ref]$point);$physicalOk=[AllStagesNative]::GetPhysicalCursorPos([ref]$physical)
        $clipOk=[AllStagesNative]::GetClipCursor([ref]$clip)
        $foreground=[AllStagesNative]::GetForegroundWindow();[uint32]$foregroundPid=0
        [void][AllStagesNative]::GetWindowThreadProcessId($foreground,[ref]$foregroundPid)
        $diagnostic=[ordered]@{
            timestampUtc=[DateTime]::UtcNow.ToString('O');attempt=$attempt
            targetNormalized=@{x=$X;y=$Y};expected=@{x=$pixelX;y=$pixelY}
            actual=@{x=$point.X;y=$point.Y;readSucceeded=$getOk};physical=@{x=$physical.X;y=$physical.Y;readSucceeded=$physicalOk}
            client=@{x=$client.X;y=$client.Y;width=$client.W;height=$client.H}
            clip=@{left=$clip.Left;top=$clip.Top;right=$clip.Right;bottom=$clip.Bottom;readSucceeded=$clipOk}
            testPid=$allPlayer.Id;testWindow=$allWindow.ToInt64();foregroundPid=$foregroundPid;foregroundWindow=$foreground.ToInt64()
            dpi=[AllStagesNative]::GetDpiForWindow($allWindow);threadDpiContext=[AllStagesNative]::GetThreadDpiAwarenessContext().ToInt64()
            setSucceeded=$setOk;setWin32Error=$setError
        }
        ($diagnostic|ConvertTo-Json -Depth 6 -Compress)|Add-Content -LiteralPath $allPointerLog -Encoding UTF8
        $lastDetail='expected=('+ $pixelX+','+$pixelY+'); actual=('+ $point.X+','+$point.Y+'); physical=('+ $physical.X+','+$physical.Y+'); client=('+ $client.X+','+$client.Y+','+$client.W+','+$client.H+'); clip=('+ $clip.Left+','+$clip.Top+','+$clip.Right+','+$clip.Bottom+'); foregroundPid='+$foregroundPid+'; testPid='+$allPlayer.Id+'; dpi='+$diagnostic.dpi+'; set='+$setOk+'; error='+$setError
        if($foreground -ne $allWindow -or $foregroundPid -ne $allPlayer.Id){throw ('Test player lost foreground during pointer placement; refusing input. '+$lastDetail)}
        $atTarget=$setOk -and $getOk -and $physicalOk -and [Math]::Abs($physical.X-$pixelX) -le 2 -and [Math]::Abs($physical.Y-$pixelY) -le 2 -and [Math]::Abs($point.X-$pixelX) -le 2 -and [Math]::Abs($point.Y-$pixelY) -le 2
        if($atTarget) {
            if([AllStagesNative]::GetAncestor([AllStagesNative]::WindowFromPoint($point),2) -ne $allWindow){throw ('Another window covers the target; refusing input. '+$lastDetail)}
            return
        }
        # A window can finish restoring between the first click and the next move. Recompute its client bounds on retry.
        Start-Sleep -Milliseconds 100
    }
    throw ('Pointer failed to reach the intended client position after 3 verified attempts. '+$lastDetail)
}
function Click-All([double]$X, [double]$Y) {
    Move-AllPointer $X $Y; Confirm-AllWindow
    [AllStagesNative]::Mouse(2)
    $script:allInputSent = $true
    try { Start-Sleep -Milliseconds 90 } finally { [AllStagesNative]::Mouse(4) }
    Start-Sleep -Milliseconds 250
}
function Key-All([uint16]$KeyCode) {
    Confirm-AllWindow; [AllStagesNative]::Key($KeyCode,$true)
    $script:allInputSent = $true
    try { Start-Sleep -Milliseconds 70 } finally { [AllStagesNative]::Key($KeyCode,$false) }
    Start-Sleep -Milliseconds 250
}
function Capture-All([string]$Name) {
    Confirm-AllWindow; $client = Get-AllClient
    if ($client.X -lt 0 -or $client.Y -lt 0 -or $client.X+$client.W -gt [AllStagesNative]::GetSystemMetrics(0) -or $client.Y+$client.H -gt [AllStagesNative]::GetSystemMetrics(1)) { throw 'Test window is outside primary screen; cannot honestly capture its UI.' }
    $bitmap = [Drawing.Bitmap]::new($client.W,$client.H); $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $path = Join-Path $allOutput ($allRunId + '-' + $allAttempt + '-' + $Name + '.png')
    try { $graphics.CopyFromScreen($client.X,$client.Y,0,0,[Drawing.Size]::new($client.W,$client.H)); $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $allScreenshots.Add($path)
}

function Click-AllVirtual([double]$X,[double]$Y){Click-All ($X/1920) ($Y/1080)}
function Type-AllDiary([string]$Text,[int]$ExistingLength){
    # Called only after a verified click inside the diary TextArea; no clipboard or global paste operation.
    Confirm-AllWindow
    $script:allImeWasOpen=[AllStagesNative]::ImeOpen($allWindow)
    if($allImeWasOpen){
        if(-not [AllStagesNative]::SetImeOpen($allWindow,$false)){throw 'Could not enter ASCII mode for the test window IME.'}
        $script:allImeChanged=$true
    }
    # Avoid a modifier chord altogether: some Unity/IME paths retain Ctrl in IMGUI after a synthetic Ctrl+A.
    # This FullProbe fixture has a single-line diary, checked before launch. End plus Backspace clears it using real keys.
    Key-All 0x23
    for($index=0;$index -lt $ExistingLength+4;$index++){
        Confirm-AllWindow;[AllStagesNative]::Key(0x08,$true)
        try{Start-Sleep -Milliseconds 80}finally{[AllStagesNative]::Key(0x08,$false)}
        Start-Sleep -Milliseconds 70
    }
    Capture-All 'diary-cleared-before-typing'
    $typingState=[ordered]@{timestampUtc=[DateTime]::UtcNow.ToString('O');eventType='ascii-keyboard-state';transport=$TextTransport;layout=[AllStagesNative]::LayoutForWindow($allWindow);imeWasOpen=$allImeWasOpen;imeNowOpen=[AllStagesNative]::ImeOpen($allWindow);ctrl=[AllStagesNative]::ModifierDown(0x11);alt=[AllStagesNative]::ModifierDown(0x12);shift=[AllStagesNative]::ModifierDown(0x10);characters=$Text.Length}
    ($typingState|ConvertTo-Json -Compress)|Add-Content -LiteralPath $allPointerLog -Encoding UTF8
    if($typingState.ctrl -or $typingState.alt -or $typingState.shift){throw 'A shortcut modifier is still down; refusing to type into an uncertain keyboard state.'}
    foreach($character in $Text.ToCharArray()){
        Confirm-AllWindow
        if($TextTransport -eq 'WindowCharacters') {
            if(-not [AllStagesNative]::WindowCharacter($allWindow,$character)){throw 'The verified test window did not accept a WM_CHAR message.'}
            Start-Sleep -Milliseconds 100
        }else{
            [AllStagesNative]::AsciiKey($allWindow,$character,$true)
            try{Start-Sleep -Milliseconds 80}finally{[AllStagesNative]::AsciiKey($allWindow,$character,$false)}
            Start-Sleep -Milliseconds 70
        }
    }
    Start-Sleep -Milliseconds 200
    $script:allInputSent=$true
}
function Drag-AllVirtual([double]$FromX,[double]$FromY,[double]$ToX,[double]$ToY){
    Move-AllPointer ($FromX/1920) ($FromY/1080);Confirm-AllWindow
    [AllStagesNative]::Mouse(2);$script:allInputSent=$true
    try{
        for($step=1;$step -le 15;$step++){
            Confirm-AllWindow;$fraction=$step/15.0
            Move-AllPointer (($FromX+($ToX-$FromX)*$fraction)/1920) (($FromY+($ToY-$FromY)*$fraction)/1080)
            Start-Sleep -Milliseconds 70
        }
    }finally{[AllStagesNative]::Mouse(4)}
    Start-Sleep -Milliseconds 250
}
function Test-AllArtwork([string]$PngBase64){
    $bytes=[Convert]::FromBase64String($PngBase64)
    if($bytes.Length -lt 24 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71){throw 'Saved artwork is not PNG.'}
    $stream=[IO.MemoryStream]::new($bytes);$bitmap=$null
    try{
        $bitmap=[Drawing.Bitmap]::new($stream)
        if($bitmap.Width -ne 256 -or $bitmap.Height -ne 256){throw 'Native drawing did not save a 256 x 256 PNG.'}
        $clear=0;$visible=0
        for($y=0;$y -lt $bitmap.Height;$y+=4){for($x=0;$x -lt $bitmap.Width;$x+=4){$alpha=$bitmap.GetPixel($x,$y).A;if($alpha -eq 0){$clear++};if($alpha -gt 200){$visible++}}}
        if($clear -lt 1 -or $visible -lt 1){throw 'Saved drawing lacks transparent margins or visible strokes.'}
        return [pscustomobject]@{width=$bitmap.Width;height=$bitmap.Height;pngBytes=$bytes.Length;transparentSamples=$clear;visibleSamples=$visible}
    }finally{if($bitmap){$bitmap.Dispose()};$stream.Dispose()}
}
try{
    $baseline=Read-AllSave
    if(!$baseline.personalStudio.initialized){throw 'The existing test save has no initialized personal studio.'}
    $allInitialTemplates=@($baseline.personalStudio.artworks).Count
    $fixtureDate=(Get-Date).ToString('yyyy-MM-dd')
    $fixtureBody=[string](@($baseline.personalStudio.diary|Where-Object{$_.dateKey -eq $fixtureDate}|Select-Object -First 1).body)
    if($fixtureBody.Contains("`n") -or $fixtureBody.Contains("`r")){throw 'This no-modifier native typing fixture requires the FullProbe single-line diary; it will not clear an arbitrary multi-line diary.'}
    if($allInitialTemplates -ge 24){throw 'The isolated test collection is full; no artwork will be removed.'}
    $replies=@($baseline.commissionDesk.letters|Where-Object {[int]$_.status -eq 3}).Count
    $unlocked=@($baseline.atelierProgress.purchases|Where-Object {$_.id -eq 'unlock-sticker-maker'}).Count -gt 0
    if($replies -lt 2 -and !$unlocked){throw 'Sticker maker is not unlocked in the supplied FullProbe save.'}
    $arguments=@('--atelier-full-validation','--desk-revision-validation',('--revision-run='+$allRunId),'--dream-night',
        '-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$allLog+'"'))
    $allPlayer=Start-Process -FilePath $allExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $ready=[Diagnostics.Stopwatch]::StartNew()
    while($ready.Elapsed.TotalSeconds -lt 45){
        $allPlayer.Refresh();if($allPlayer.HasExited){throw 'Test player exited during startup.'}
        $allWindow=[AllStagesNative]::FindWindowForProcess([uint32]$allPlayer.Id)
        if($allWindow -ne [IntPtr]::Zero -and (Read-AllLog) -match '\[PersonalStudio\] Ready'){break}
        Start-Sleep -Milliseconds 200
    }
    if($allWindow -eq [IntPtr]::Zero){throw 'No top-level test Unity window was found.'}
    Focus-AllWindow;Capture-All 'start-ui'
    $offset=(Read-AllLog).Length;Click-AllVirtual 410 679
    [void](Wait-AllLog '\[Desk\] UI:Begin' 10000 $offset)
    Add-AllCheck 'Native start button' $true 'Test PID entered the desk.'
    $offset=(Read-AllLog).Length;Click-AllVirtual 164 877
    [void](Wait-AllLog '\[PersonalStudio\] Open' 8000 $offset)
    Add-AllCheck 'Native Hub entry opens the modal' $true 'Hub Open log follows the mouse click.'
    Click-AllVirtual 514 241 # Tab 1: diary and sections.
    Capture-All 'diary-tab-before'
    Click-AllVirtual 609 507 # Exact centre of diary textarea: Rect(204,382,810,250).
    $diaryText='native diary '+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    Type-AllDiary $diaryText $fixtureBody.Length
    Capture-All 'diary-typed-before-save'
    Click-AllVirtual 324 678 # Save private diary: Rect(204,652,240,52).
    $today=(Get-Date).ToString('yyyy-MM-dd')
    [void](Wait-AllSave {param($d) @($d.personalStudio.diary|Where-Object {$_.dateKey -eq $today -and $_.body -eq $diaryText}).Count -eq 1} 'native textarea and save button persist one private diary entry')
    $allDiarySaved=$true
    if($TextTransport -eq 'WindowCharacters'){
        Add-AllCheck 'Native window text messages and diary save' $true ('Matched date and '+$diaryText.Length+' ASCII WM_CHAR events sent only to the verified HWND; hardware text entry not claimed; body omitted.')
    }else{
        Add-AllCheck 'Native ASCII keyboard typing and diary save' $true ('Matched date and '+$diaryText.Length+' ASCII characters via scan codes; body omitted.')
    }
    Capture-All 'diary-saved-ui'
    Click-AllVirtual 730 241 # Tab 2: drawing workshop.
    Drag-AllVirtual 250 544 540 544
    Drag-AllVirtual 396 400 396 690
    Capture-All 'painted-canvas-ui'
    Click-AllVirtual 882 703 # Die-cut save: Rect(622,676,520,54).
    $after=Wait-AllSave {param($d) @($d.personalStudio.artworks).Count -eq $allInitialTemplates+1} 'native canvas and save button add exactly one template'
    $previousIds=@($baseline.personalStudio.artworks|ForEach-Object {$_.assetId})
    $new=@($after.personalStudio.artworks|Where-Object {$_.assetId -notin $previousIds})
    if($new.Count -ne 1 -or $new[0].kind -ne 'sticker'){throw 'Native drawing produced an unexpected artwork identity or kind.'}
    $allNewArtId=$new[0].assetId;$stats=Test-AllArtwork $new[0].pngBase64
    Add-AllCheck 'Native canvas creates a real transparent sticker PNG' $true ('templates '+$allInitialTemplates+' -> '+@($after.personalStudio.artworks).Count+'; '+$stats.width+'x'+$stats.height+'; clear='+$stats.transparentSamples+'; visible='+$stats.visibleSamples+'; bytes='+$stats.pngBytes)
    Capture-All 'sticker-saved-ui'
    $exceptions=@([regex]::Matches((Read-AllLog),'(?m)^(?:NullReferenceException|[^\r\n]*Exception:)[^\r\n]*$'))
    Add-AllCheck 'No player exceptions during native creation' ($exceptions.Count -eq 0) ($exceptions.Count.ToString()+' exception lines')
    $allStatus='PASS'
}catch{
    $allFailure=$_.Exception.Message
    $allChecks.Add([ordered]@{name='Native personal studio sequence';passed=$false;evidence=$allFailure})
    try{if($allPlayer -and !$allPlayer.HasExited -and [AllStagesNative]::GetForegroundWindow() -eq $allWindow){Capture-All 'failure-ui'}}catch{}
}finally{
    if($allPlayer){
        $allPlayer.Refresh()
        if(!$allPlayer.HasExited){
            if($allImeChanged -and $allWindow -ne [IntPtr]::Zero){[void][AllStagesNative]::SetImeOpen($allWindow,$allImeWasOpen)}
            $closingWindow=[AllStagesNative]::FindWindowForProcess([uint32]$allPlayer.Id)
            if($closingWindow -ne [IntPtr]::Zero){[uint32]$owner=0;[void][AllStagesNative]::GetWindowThreadProcessId($closingWindow,[ref]$owner);if($owner -eq [uint32]$allPlayer.Id){[void][AllStagesNative]::RequestClose($closingWindow)}}
            if(!$allPlayer.WaitForExit(15000)){$allStatus='FAIL';$allFailure=($allFailure+' Test player did not close normally; it was left running.').Trim()}
        }
        if($allPlayer.HasExited -and $allPlayer.ExitCode -ne 0){$allStatus='FAIL';$allFailure=($allFailure+' Exit code: '+$allPlayer.ExitCode).Trim()}
    }
    $final=Read-AllSave;if($final){$allFinalTemplates=@($final.personalStudio.artworks).Count}
    [ordered]@{
        timestampUtc=[DateTime]::UtcNow.ToString('O');result=$allStatus;failure=$allFailure;runId=$allRunId
        nativeInput=$allInputSent;runtimeGameplayActionsInvoked=$false
        textTransport=$TextTransport
        keyboardCoverage=if($TextTransport -eq 'WindowCharacters'){'Native End/Backspace keys plus ASCII WM_CHAR window text messages. Hardware or Unicode text generation is not validated.'}else{'ASCII via mapped native hardware scan codes; Unicode input is not validated.'}
        executable=$allExe;processId=if($allPlayer){$allPlayer.Id}else{$null};savePath=$allSavePath;playerLog=$allLog;pointerDiagnostics=$allPointerLog
        screenshotScope='Real Windows 1280x720 client capture including IMGUI; coordinates scaled from the actual Hub virtual layout.'
        diarySaved=$allDiarySaved;initialTemplates=$allInitialTemplates;finalTemplates=$allFinalTemplates;newArtworkId=$allNewArtId
        checks=$allChecks;screenshots=$allScreenshots
        notValidated=@('All customer and daily flows with native input','All 7 Hub tabs and every tool with native input','Social actions with native input','New-process save restoration','Long-term player retention')
    }|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $allReportPath -Encoding UTF8
    Write-Output ('All-stage native result: '+$allStatus+'; report='+$allReportPath)
}
if($allStatus -ne 'PASS'){throw ('Native personal studio probe failed: '+$allFailure)}
