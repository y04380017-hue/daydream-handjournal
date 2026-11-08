[CmdletBinding()]
param([int]$Port = 18747, [string]$NodePath = 'D:\node.exe', [string]$DataDirectory)
$ErrorActionPreference = 'Stop'
$atelierRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) {
    $nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
    if (!$nodeCommand) { throw 'Node.js 20+ is required to run the local post office.' }
    $NodePath = $nodeCommand.Source
}
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be 1024-65535.' }
if (!$DataDirectory) { $DataDirectory = Join-Path $atelierRoot 'SocialService\data' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($DataDirectory))
$serviceLog = Join-Path $DataDirectory 'service.log'
$serviceError = Join-Path $DataDirectory 'service-error.log'
function Read-AtelierHealth {
    $request=[Net.HttpWebRequest]::Create('http://127.0.0.1:'+$Port+'/health');$request.Proxy=$null;$request.Timeout=700
    try { $response=$request.GetResponse();$reader=[IO.StreamReader]::new($response.GetResponseStream());try{return ($reader.ReadToEnd()|ConvertFrom-Json)}finally{$reader.Dispose();$response.Dispose()} } catch { return $null }
}
$health=Read-AtelierHealth
if($health){if($health.localOnly -and $health.schemaVersion -eq 1){Write-Output ('Local post office already running: http://127.0.0.1:'+$Port);return};throw 'The port is occupied by another service.'}
$argv = @(('"' + (Join-Path $atelierRoot 'SocialService\server.mjs') + '"'), '--port', $Port, '--data', ('"' + (Join-Path $DataDirectory 'social-store.json') + '"'))
$process = Start-Process -FilePath $NodePath -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardOutput $serviceLog -RedirectStandardError $serviceError
$process.Id|Set-Content (Join-Path $DataDirectory 'service-pid.txt')
$wait=[Diagnostics.Stopwatch]::StartNew()
while($wait.Elapsed.TotalSeconds -lt 8){$process.Refresh();if($process.HasExited){throw ('Post office failed to start; inspect '+$serviceError)};$health=Read-AtelierHealth;if($health.localOnly){break};Start-Sleep -Milliseconds 100}
if(!$health.localOnly){throw ('Post office health check failed; inspect '+$serviceError)}
Write-Output ('Started local post office PID ' + $process.Id + ' at http://127.0.0.1:' + $Port)
Write-Output ('Private moderator key: ' + (Join-Path $DataDirectory 'moderator-key.txt'))
