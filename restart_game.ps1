$t = $PSScriptRoot
Get-Process Core -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$log = "$t\..\voyage\system.log"
$m = (Get-Content $log).Count
cmd /c "$t\..\start_offline.bat"
$w = 0
while ($w -lt 60 -and -not (Get-Content $log | Select-Object -Skip $m | Select-String 'Download animation 3300038' -Quiet)) { Start-Sleep 2; $w += 2 }
Start-Sleep 4
"game ready after about $($w + 5)s"
