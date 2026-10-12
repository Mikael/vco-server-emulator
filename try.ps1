param([string[]]$Reply, [string]$Clicks = '512,433;845,717', [int]$Wait = 3)
Set-Content "$PSScriptRoot\reply.hex" $Reply -Encoding ascii
$before = (Get-Content "$PSScriptRoot\capture.log").Count
foreach ($c in $Clicks.Split(';')) {
    $xy = $c.Split(',')
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\click.ps1" -X $xy[0] -Y $xy[1] | Out-Null
    if ($LASTEXITCODE) { 'ABORT: game window is not in the foreground'; exit 1 }
    Start-Sleep -Milliseconds 900
}
Start-Sleep $Wait
Get-Content "$PSScriptRoot\capture.log" | Select-Object -Skip $before | Select-String 'RECV|SEND|CONNECTED|CLOSED' | ForEach-Object { $_.Line }
& powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\shot.ps1"
"game running: $([bool](Get-Process Core -ErrorAction SilentlyContinue))"
