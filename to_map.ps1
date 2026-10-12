param([string]$Name = 'Tester')
$t = $PSScriptRoot
& powershell -NoProfile -ExecutionPolicy Bypass -File "$t\to_create.ps1" | Select-String 'ERROR|EXITED|ABORT'
function C($x, $y, $text = '') {
    if ($text) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X $x -Y $y -Text $text | Out-Null }
    else { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\click.ps1" -X $x -Y $y | Out-Null }
    Start-Sleep -Milliseconds 1200
}
C 405 270
C 937 690
Start-Sleep 2
C 840 291 $Name
C 943 621
Start-Sleep 3
if (Get-Process Core -ErrorAction SilentlyContinue) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$t\shot.ps1" } else { 'GAME EXITED' }
