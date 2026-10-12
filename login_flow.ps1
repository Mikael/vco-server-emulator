param([string]$Account = 'testacc', [string]$Password = 'testpw', [string]$Code = '1234', [int]$Wait = 5)
$t = $PSScriptRoot
function Click($x, $y, $text = '') {
    if ($text) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X $x -Y $y -Text $text | Out-Null } else { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X $x -Y $y | Out-Null }
    if ($LASTEXITCODE) { 'ABORT: game window is not in the foreground'; exit 1 }
    Start-Sleep -Milliseconds 700
}
$before = (Get-Content "$t\capture.log").Count
Click 512 433
Click 845 717
Start-Sleep 2
Click 238 308
Start-Sleep 2
Click 915 340 $Account
Click 915 387 $Password
Click 915 435 $Code
Click 882 478
Start-Sleep $Wait
Get-Content "$t\capture.log" | Select-Object -Skip $before | Select-String 'RECV|SEND|CONNECTED|CLOSED|LOGIN' | ForEach-Object { $_.Line }
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\shot.ps1"
"game running: $([bool](Get-Process Core -ErrorAction SilentlyContinue))"

