param([Parameter(Mandatory = $true)][string]$Line, [int]$Wait = 4)
$t = $PSScriptRoot
$n = (Get-Content "$t\capture.log").Count
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -Keys 13 | Out-Null
if ($LASTEXITCODE) { 'ABORT: game window is not in the foreground'; exit 1 }
Start-Sleep -Milliseconds 500
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -Text $Line | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -Keys 13 | Out-Null
Start-Sleep $Wait
Get-Content "$t\capture.log" | Select-Object -Skip $n | Select-String 'RECV|SEND|SLASH|CHAT|COMMAND|MOVE|INTERACT|ERROR|CLOSED' | ForEach-Object { $_.Line }
if (Get-Process Core -ErrorAction SilentlyContinue) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\shot.ps1" } else { 'GAME EXITED'; Get-Content "$t\crashwatch.log" -ErrorAction SilentlyContinue | Select-String 'ACCESS VIOLATION at (?!voyclient)' -Context 0, 2 | Select-Object -Last 2 }
