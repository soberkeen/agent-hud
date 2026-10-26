# widget-rect.ps1 — 调试工具：打印 HDR Toggle 小窗的窗口矩形（进程虚拟坐标）与工作区
param()
$ErrorActionPreference = 'Stop'

Add-Type -Name W -Namespace WidgetRect -MemberDefinition @'
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    public struct RECT { public int Left, Top, Right, Bottom; }
'@

$script:hwnd = [IntPtr]::Zero
$cb = [WidgetRect.W+EnumWindowsProc] {
    param($h, $lp)
    if ([WidgetRect.W]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 256
        $null = [WidgetRect.W]::GetWindowText($h, $sb, 256)
        if ($sb.ToString() -eq 'HDR Toggle') { $script:hwnd = $h; return $false }
    }
    return $true
}
$null = [WidgetRect.W]::EnumWindows($cb, [IntPtr]::Zero)

Add-Type -AssemblyName System.Windows.Forms
$wa = [System.Windows.Forms.SystemInformation]::WorkingArea

if ($script:hwnd -eq [IntPtr]::Zero) {
    "WIDGET_NOT_FOUND"
} else {
    $r = New-Object WidgetRect.W+RECT
    $null = [WidgetRect.W]::GetWindowRect($script:hwnd, [ref]$r)
    "rect: $($r.Left),$($r.Top) - $($r.Right),$($r.Bottom)  (size $($r.Right - $r.Left)x$($r.Bottom - $r.Top))"
}
"workarea: $($wa.X),$($wa.Y) - $($wa.X + $wa.Width),$($wa.Y + $wa.Height)"
