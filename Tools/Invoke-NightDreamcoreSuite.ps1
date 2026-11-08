param([string[]]$Steps=@('Install','Commission','Workspace','Luminous','Build'))
# Night dreamcore acceptance: reruns the existing suites against the new night layer. Earlier stages' evidence folders
# are snapshotted first; anything the suites rewrite is filed under docs\night-dreamcore\regression and the originals restored.
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$docs=Join-Path $project 'docs'; $mine=Join-Path $docs 'night-dreamcore'; $reg=Join-Path $mine 'regression'
[IO.Directory]::CreateDirectory($reg)|Out-Null
$snap=Join-Path $env:TEMP ('nd-docs-'+[guid]::NewGuid().ToString('N'))
robocopy $docs $snap /E /XD $mine /NFL /NDL /NJH /NJS /NP | Out-Null
$results=[ordered]@{}
function Editor([string]$name,[string]$method,[string[]]$flags){
    $log=Join-Path $reg "$name.log"
    $a=@('-batchmode','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-logFile',('"'+$log+'"'))+$flags
    $p=Start-Process -FilePath 'E:\unity\Editor\Unity.exe' -ArgumentList $a -WindowStyle Hidden -Wait -PassThru
    $script:results[$name]=$p.ExitCode; Write-Host "$name exit $($p.ExitCode)"
}
try{
    foreach($s in $Steps){
        switch($s){
            'Install'    { Editor 'install'    'VintageJournal.Editor.NightDreamcoreSetup.Install' @('-quit') }
            'Commission' { Editor 'commission' 'VintageJournal.Editor.CommissionValidation.Begin' @('--commission-validation') }
            'Workspace'  { Editor 'workspace'  'VintageJournal.Editor.WorkspaceStage4BValidation.Begin' @('--workspace-validation') }
            'Luminous'   { Editor 'luminous'   'VintageJournal.Editor.DeskRevisionValidation.Begin' @('--desk-revision-validation','--luminous-validation') }
            'Build'      { Editor 'build'      'VintageJournal.Editor.DeskSceneBuilder.BuildWindows' @('-quit') }
        }
    }
}
finally{
    # File every new/changed evidence file under regression\<original path>, then restore the original docs.
    Get-ChildItem $docs -Recurse -File | Where-Object { -not $_.FullName.StartsWith($mine) } | ForEach-Object {
        $rel=$_.FullName.Substring($docs.Length+1); $old=Join-Path $snap $rel
        if(!(Test-Path $old) -or (Get-FileHash $old).Hash -ne (Get-FileHash $_.FullName).Hash){
            $dst=Join-Path $reg $rel; [IO.Directory]::CreateDirectory((Split-Path $dst))|Out-Null; Copy-Item $_.FullName $dst -Force
            if(!(Test-Path $old)){ Remove-Item $_.FullName }
        }
    }
    robocopy $snap $docs /E /NFL /NDL /NJH /NJS /NP | Out-Null
    Remove-Item $snap -Recurse -Force -ErrorAction SilentlyContinue
    $results | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $reg 'suite-exit-codes.json')
}

$global:LASTEXITCODE=0; exit (@($results.Values | Where-Object { $_ -ne 0 }).Count)
