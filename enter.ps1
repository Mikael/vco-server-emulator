param([int]$Watch = 120)
$t = $PSScriptRoot
Start-Process powershell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$t\to_world.ps1", '-Wait', $Watch -WindowStyle Hidden
Start-Sleep 5
$w = 0
while ($w -lt 220) {
    $l = Get-Content "$t\capture.log"
    $i = ($l | Select-String 'listening' | Select-Object -Last 1).LineNumber
    if ($l[$i..($l.Count - 1)] | Select-String 'RECV id=0x000f' -Quiet) { break }
    Start-Sleep 3; $w += 3
}
Start-Sleep 4
if ($w -ge 220) { 'TIMEOUT waiting for the client to enter the world' } else { "in world after about $($w + 9)s" }
