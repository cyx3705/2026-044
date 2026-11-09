param([long]$WindowHandle,[int]$ExpectedProcessId,[int]$X=0,[int]$Y=0,[int]$Width=0,[int]$Height=0,[string]$OutputPath)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NormaWindowCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][NormaWindowCapture]::SetProcessDPIAware()
$handle=[IntPtr]$WindowHandle
[uint32]$actualProcessId=0
[void][NormaWindowCapture]::GetWindowThreadProcessId($handle,[ref]$actualProcessId)
if($actualProcessId -ne $ExpectedProcessId){throw '手册窗口句柄已失效或所属进程改变'}
if([NormaWindowCapture]::IsIconic($handle)){throw '手册窗口已最小化，请还原后重试'}
$rect=New-Object NormaWindowCapture+Rect
if(-not [NormaWindowCapture]::GetWindowRect($handle,[ref]$rect)){throw '无法读取手册窗口；请在交互桌面会话运行'}
$fullWidth=$rect.Right-$rect.Left
$fullHeight=$rect.Bottom-$rect.Top
if($fullWidth -lt 1 -or $fullHeight -lt 1 -or $fullWidth -gt 12000 -or $fullHeight -gt 12000){throw '窗口尺寸不可截图'}
if($Width -eq 0){$Width=$fullWidth-$X}
if($Height -eq 0){$Height=$fullHeight-$Y}
if($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or $X+$Width -gt $fullWidth -or $Y+$Height -gt $fullHeight){throw '裁切区域超出手册窗口范围'}
$bitmap=New-Object System.Drawing.Bitmap($fullWidth,$fullHeight)
$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
try {
 $graphics.Clear([System.Drawing.Color]::Magenta)
 $dc=$graphics.GetHdc()
 try { $ok=[NormaWindowCapture]::PrintWindow($handle,$dc,2) } finally { $graphics.ReleaseHdc($dc) }
 if(-not $ok){throw 'Windows PrintWindow 返回失败'}
 $colors=New-Object 'System.Collections.Generic.HashSet[int]'
 for($cy=0;$cy -lt $fullHeight;$cy += [Math]::Max(1,[int]($fullHeight/30))){for($cx=0;$cx -lt $fullWidth;$cx += [Math]::Max(1,[int]($fullWidth/30))){[void]$colors.Add($bitmap.GetPixel($cx,$cy).ToArgb())}}
 if($colors.Count -lt 3){throw '系统截图为空白或纯色，未保存为有效证据'}
 $crop=$bitmap.Clone((New-Object System.Drawing.Rectangle($X,$Y,$Width,$Height)),[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 try {$crop.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)} finally {$crop.Dispose()}
 @{imagePath=$OutputPath;width=$Width;height=$Height;x=$X;y=$Y;windowWidth=$fullWidth;windowHeight=$fullHeight;screenLeft=$rect.Left;screenTop=$rect.Top;processId=$ExpectedProcessId;windowHandle=$WindowHandle;evidence='native-window';method='Windows PrintWindow';note='原手册当前窗口像素截图；未重新渲染HTML，也未自动翻页。裁切坐标相对窗口左上角。'} | ConvertTo-Json -Compress
} finally {$graphics.Dispose();$bitmap.Dispose()}
