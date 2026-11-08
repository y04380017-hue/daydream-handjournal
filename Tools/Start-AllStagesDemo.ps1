[CmdletBinding()]
param([ValidatePattern('^[A-Za-z0-9_-]{1,32}$')][string]$Profile='Default',[switch]$Offline)
$ErrorActionPreference='Stop'
$atelierProject=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$Offline){& (Join-Path $PSScriptRoot 'Start-AtelierSocialService.ps1')}
$atelierExecutable=Join-Path $atelierProject 'Build\Windows\VintageJournalDesk.exe'
if(!(Test-Path -LiteralPath $atelierExecutable)){throw 'Build/Windows/VintageJournalDesk.exe is missing.'}
# This is the interactive game the user explicitly launches; its own window remains visible.
$launchArguments=@('-screen-width','1600','-screen-height','900','-screen-fullscreen','0')
if($Profile -ne 'Default'){$launchArguments+=('--atelier-profile='+$Profile)}
foreach($running in @(Get-CimInstance Win32_Process -Filter "Name='VintageJournalDesk.exe'")){
    # Stage and delivery builds share Unity's product save folder, so check both paths.
    if($running.CommandLine -like '*validation*'){continue}
    $match=[regex]::Match($running.CommandLine,'--atelier-profile=([A-Za-z0-9_-]+)')
    $existingProfile=if($match.Success){$match.Groups[1].Value}else{'Default'}
    if($existingProfile -eq $Profile){throw ('This journal profile is already open: '+$Profile)}
}
Start-Process -FilePath $atelierExecutable -ArgumentList $launchArguments -WorkingDirectory $atelierProject|Out-Null
Write-Output ('Opened '+$Profile+'. Default continues the existing journal; other profile names have independent saves.')
