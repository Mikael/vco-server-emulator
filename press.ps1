param([int[]]$Keys = @(13))
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
'@
$p = Get-Process Core -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[W.U]::SetForegroundWindow($h) | Out-Null
foreach ($k in $Keys) {
    [W.U]::PostMessage($h, 0x100, [IntPtr]$k, [IntPtr]1) | Out-Null
    if ($k -ge 8 -and $k -lt 128) { [W.U]::PostMessage($h, 0x102, [IntPtr]$k, [IntPtr]1) | Out-Null }
    [W.U]::PostMessage($h, 0x101, [IntPtr]$k, [IntPtr]0xC0000001) | Out-Null
    Start-Sleep -Milliseconds 80
}
"sent $($Keys -join ',') to window $h"
