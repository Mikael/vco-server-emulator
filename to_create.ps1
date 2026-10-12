$t = $PSScriptRoot
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\restart_game.ps1" | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\login_flow.ps1" -Wait 6 | Select-String 'LOGIN|ABORT'
$n = (Get-Content "$t\capture.log").Count
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X 245 -Y 753 | Out-Null
Start-Sleep 6
Get-Content "$t\capture.log" | Select-Object -Skip $n | Select-String 'RECV|SEND|CLOSED|ERROR' | ForEach-Object { $_.Line }
if (Get-Process Core -ErrorAction SilentlyContinue) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\shot.ps1" } else { 'GAME EXITED' }
