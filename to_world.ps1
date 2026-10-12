param([int]$Wait = 20)
$t = $PSScriptRoot
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\restart_game.ps1" | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\login_flow.ps1" -Wait 7 | Select-String 'ABORT|ERROR'
Remove-Item "$t\crashwatch.log" -ErrorAction SilentlyContinue -Confirm:$false
$cw = Start-Process python -ArgumentList "`"$t\crashwatch.py`" $($Wait + 15)" -WorkingDirectory $t -WindowStyle Hidden -PassThru
Start-Sleep 3
$n = (Get-Content "$t\capture.log").Count
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X 768 -Y 714 | Out-Null
Start-Sleep $Wait
Get-Content "$t\capture.log" | Select-Object -Skip $n | Select-String 'RECV|SEND|CLOSED|ERROR|ENTER' | ForEach-Object { $_.Line }
'--- crashwatch'; Start-Sleep 2; Get-Content "$t\crashwatch.log" -ErrorAction SilentlyContinue
'--- game log'
$log = [Text.Encoding]::GetEncoding(936).GetString([IO.File]::ReadAllBytes("$t\..\voyage\system.log")) -split "`r?`n" | Where-Object { $_ -and $_ -notmatch 'WSAAsyncSelect|geos warning|can.t create image' }
$i = [Array]::LastIndexOf($log, 'Download animation 3300038')
$log[($i + 1)..($log.Count - 1)] | Select-Object -Last 22
if (Get-Process Core -ErrorAction SilentlyContinue) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\shot.ps1" } else { 'GAME EXITED' }

