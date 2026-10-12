param([string]$Out = "$PSScriptRoot\dumps\shot.png")
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name S -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
'@
[W.S]::SetProcessDPIAware() | Out-Null
$p = Get-Process Core -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[W.S]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
if ([W.S]::GetForegroundWindow() -ne $h) { 'ABORT: game window is not in the foreground'; exit 1 }
$r = New-Object W.S+RECT
[W.S]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out ($w x $ht at $($r.L),$($r.T))"

