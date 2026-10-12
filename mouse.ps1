param([int]$X = 512, [int]$Y = 400, [int]$Wheel = 0, [int]$DragX = 0, [int]$DragY = 0, [switch]$Right)
Add-Type -Namespace W -Name M2 -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, System.UIntPtr e);
'@
[W.M2]::SetProcessDPIAware() | Out-Null
$h = (Get-Process Core -ErrorAction Stop | Select-Object -First 1).MainWindowHandle
[W.M2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
if ([W.M2]::GetForegroundWindow() -ne $h) { 'ABORT: game window is not in the foreground'; exit 1 }
$r = New-Object W.M2+RECT
[W.M2]::GetWindowRect($h, [ref]$r) | Out-Null
[W.M2]::SetCursorPos($r.L + $X, $r.T + $Y) | Out-Null
Start-Sleep -Milliseconds 200
[W.M2]::mouse_event(1, 2, 2, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 100
if ($Wheel) {
    $step = if ($Wheel -gt 0) { 120 } else { -120 }
    1..[Math]::Abs($Wheel) | ForEach-Object { [W.M2]::mouse_event(0x800, 0, 0, $step, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120 }
}
if ($DragX -or $DragY) {
    $down = if ($Right) { 8 } else { 2 }; $up = if ($Right) { 0x10 } else { 4 }
    [W.M2]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150
    1..10 | ForEach-Object { [W.M2]::mouse_event(1, [int]($DragX / 10), [int]($DragY / 10), 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
    [W.M2]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
}
'done'
