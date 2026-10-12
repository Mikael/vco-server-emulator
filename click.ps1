param([int]$X = -1, [int]$Y = -1, [string]$Text = '', [int[]]$Keys = @(), [switch]$RightButton)
Add-Type -Namespace W -Name C -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, System.UIntPtr e);
[DllImport("user32.dll")] public static extern short VkKeyScan(char c);
'@
[W.C]::SetProcessDPIAware() | Out-Null
$p = Get-Process Core -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[W.C]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
function Key([byte]$vk, [bool]$shift) {
    if ($shift) { [W.C]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero) }
    [W.C]::keybd_event($vk, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [W.C]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    if ($shift) { [W.C]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 60
}
if ($X -ge 0) {
    $r = New-Object W.C+RECT
    [W.C]::GetWindowRect($h, [ref]$r) | Out-Null
    [W.C]::SetCursorPos($r.L + $X, $r.T + $Y) | Out-Null
    Start-Sleep -Milliseconds 250
    [W.C]::mouse_event(1, 2, 2, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150; [W.C]::mouse_event(1, 4294967294, 4294967294, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
    if ($RightButton) { [W.C]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250; [W.C]::mouse_event(0x10, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300; "done"; exit 0 }
    [W.C]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 250
    [W.C]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
}
foreach ($ch in $Text.ToCharArray()) { $v = [W.C]::VkKeyScan($ch); Key ([byte]($v -band 0xff)) (($v -band 0x100) -ne 0) }
foreach ($k in $Keys) { Key ([byte]$k) $false }
"done"




