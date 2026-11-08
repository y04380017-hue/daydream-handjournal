[CmdletBinding()]
param(
    [ValidateSet('Play', 'Build')][string]$Step = 'Play',
    [string]$UnityPath = 'E:\unity\Editor\Unity.exe',
    [switch]$WaitForExit
)
$ErrorActionPreference = 'Stop'
$nightProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$nightEvidenceRoot = Join-Path $nightProjectRoot 'docs\night-gameplay'
[void][IO.Directory]::CreateDirectory($nightEvidenceRoot)
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable missing: $UnityPath" }
$nightLogPath = Join-Path $nightEvidenceRoot ($Step.ToLowerInvariant() + '-editor.log')
$nightMethod = if ($Step -eq 'Play') { 'VintageJournal.Editor.NightGameplayValidation.Begin' } else { 'VintageJournal.Editor.DeskSceneBuilder.BuildWindows' }
$nightArguments = @('-batchmode', '-projectPath', ('"' + $nightProjectRoot + '"'), '-executeMethod', $nightMethod, '-logFile', ('"' + $nightLogPath + '"'))
if ($Step -eq 'Play') {
    $nightArguments += @('--desk-revision-validation', '--night-gameplay-validation', ('--revision-run=' + [Guid]::NewGuid().ToString('N')), '--dream-night')
} else { $nightArguments += '-quit' }
$nightStartOptions = @{ FilePath = $UnityPath; ArgumentList = $nightArguments; WindowStyle = 'Hidden'; PassThru = $true }
if ($WaitForExit) { $nightStartOptions.Wait = $true }
$nightProcess = Start-Process @nightStartOptions
if ($WaitForExit -and $nightProcess.ExitCode -ne 0) { throw "Unity $Step failed with exit code $($nightProcess.ExitCode); inspect $nightLogPath" }
[pscustomobject]@{ Step = $Step; ProcessId = $nightProcess.Id; Log = $nightLogPath; Report = (Join-Path $nightEvidenceRoot 'play\result.json'); Waiting = [bool]$WaitForExit }
