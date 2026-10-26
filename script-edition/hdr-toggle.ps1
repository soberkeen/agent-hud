# HDR Toggle - Win11 桌面 HDR 一键开关（悬浮小窗版）
# 用法:
#   .\hdr-toggle.ps1            启动桌面悬浮小窗（默认；单实例，重复启动自动退出）
#   .\hdr-toggle.ps1 -State     仅打印当前 HDR 状态后退出
#   .\hdr-toggle.ps1 -Toggle    切换一次 HDR 后退出（供快捷方式/脚本调用）
#   .\hdr-toggle.ps1 -On / -Off 强制开 / 强制关
#   .\hdr-toggle.ps1 -Quit      关闭正在运行的小窗
# 小窗交互: 点击切换；拖动移动；拖到屏幕边缘自动吸附并半隐藏（悬停弹出，移开缩回）；右键退出
# 依赖: Windows 10 1803+ / Windows 11，显示器需支持 HDR；无需管理员权限。

param(
    [switch]$State,
    [switch]$Toggle,
    [switch]$On,
    [switch]$Off,
    [switch]$Quit
)

$ErrorActionPreference = 'Stop'

# ---------- 核心 HDR API（Windows CCD，零依赖） ----------
$csSource = @'
using System;
using System.Runtime.InteropServices;

public static class HdrCtl {
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusAndFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint technology; public uint rotation; public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering;
        public uint targetAvailable; public uint statusAndFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; public uint colorEncoding; public uint bitsPerPixelFormat;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SDR_WHITE_LEVEL {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out int pc, out int mc);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref int pc, [In, Out] DISPLAYCONFIG_PATH_INFO[] paths, ref int mc, IntPtr modes, IntPtr topo);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO req);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL req);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE req);

    // 枚举活动显示路径，返回主显示器 path（statusAndFlags & 0x1 = SOURCE_IN_PRIMARY）
    static bool FindPrimary(out DISPLAYCONFIG_PATH_INFO primary) {
        primary = default(DISPLAYCONFIG_PATH_INFO);
        int pc, mc;
        // 注意: QDC_DATABASE_CURRENT(2) 不能与其他 QDC_* 组合，否则返回 87
        if (GetDisplayConfigBufferSizes(2, out pc, out mc) != 0) return false;
        var paths = new DISPLAYCONFIG_PATH_INFO[pc];
        var buf = Marshal.AllocHGlobal(mc * 64);
        try {
            int pn = pc, mn = mc;
            if (QueryDisplayConfig(2, ref pn, paths, ref mn, buf, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < pn; i++) {
                if ((paths[i].sourceInfo.statusAndFlags & 0x1) != 0) {
                    primary = paths[i];
                    return true;
                }
            }
            return false;
        } finally { Marshal.FreeHGlobal(buf); }
    }

    // 返回: -2 无主显 path, -1 API 调用失败, 1 HDR 关, 2 HDR 开
    // 本机(雷神 P275MS PLUS @240Hz)驱动 quirk 实测(diag-hdr-curve.ps1, 2026-10):
    //   开 = 0x3 (enabled+supported)；关 = 0x1 —— enabled 位在切关后滞留为 1 不清零，
    //   仅 supported 位正确清零，因此 enabled 位单独不可信（会把"关"读成"开"）。
    //   0x1 时用 SDR 白电平仲裁: HDR 关时恒为 1000，开时随「SDR 内容亮度」抬高(本机 2250)。
    //   唯一误判窗口: HDR 开且 SDR 亮度滑块拉到最低(白电平恰为 1000)，可接受。
    public static int GetHdrState() {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        info.header.type = 9;  // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO（值 9，不是 12！）
        info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
        info.header.adapterId = p.targetInfo.adapterId;
        info.header.id = p.targetInfo.id;
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
        int v = (int)info.value;
        if ((v & 0x3) == 0x3) return 2;       // enabled+supported: 确定开
        if ((v & 0x1) != 0) {                 // enabled 滞留: 需 SDR 白电平仲裁
            return GetSdrWhite() > 1000 ? 2 : 1;
        }
        return 1;
    }

    // SDR 白电平（1/1000 × 80nit；HDR 关=1000，开=随 SDR 内容亮度设置抬高）
    public static int GetSdrWhite() {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var info = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
        info.header.type = 11;  // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
        info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SDR_WHITE_LEVEL));
        info.header.adapterId = p.targetInfo.adapterId;
        info.header.id = p.targetInfo.id;
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
        return (int)info.SDRWhiteLevel;
    }

    // 返回 0 = 成功
    public static int SetHdr(bool enable) {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var pkt = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        pkt.header.type = 10;  // DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE（值 10，不是 13！）
        pkt.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
        pkt.header.adapterId = p.targetInfo.adapterId;
        pkt.header.id = p.targetInfo.id;
        pkt.value = enable ? 1u : 0u;
        return DisplayConfigSetDeviceInfo(ref pkt);
    }

    // 返回切换后的实测状态: 1(关) / 2(开)，失败或异常返回原状态码
    public static int ToggleHdr() {
        int s = GetHdrState();
        if (s == 2) return SetHdr(false) == 0 ? GetHdrState() : s;
        if (s == 1) return SetHdr(true) == 0 ? GetHdrState() : s;
        return s;
    }

    // 保留原 GetHdrStateOld 语义供诊断: -1 API 错; 0 驱动报告不支持; 1 关; 2 开
    public static int GetHdrSupported() {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        info.header.type = 9;
        info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
        info.header.adapterId = p.targetInfo.adapterId;
        info.header.id = p.targetInfo.id;
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
        return (info.value & 0x2) != 0 ? 1 : 0;
    }
}
'@

Add-Type -TypeDefinition $csSource -Language CSharp

# ---------- CLI 模式 ----------
if ($State -or $Toggle -or $On -or $Off) {
    if ($State) {
        $s = [HdrCtl]::GetHdrState()
        switch ($s) {
            2 { 'ON' }
            1 { 'OFF' }
            -2 { 'NO_PRIMARY_DISPLAY' }
            default { "ERROR($s)" }
        }
        return
    }
    if ($Toggle) {
        $s = [HdrCtl]::ToggleHdr()
        switch ($s) { 2 { 'ON' } 1 { 'OFF' } -2 { 'NO_PRIMARY_DISPLAY' } default { "ERROR($s)" } }
        return
    }
    if ($On)  { $r = [HdrCtl]::SetHdr($true) }
    if ($Off) { $r = [HdrCtl]::SetHdr($false) }
    if ($On -or $Off) { if ($r -eq 0) { 'OK' } else { "ERROR($r)" } }
    return
}

# -Quit: 关闭正在运行的小窗（向标题为 HDR Toggle 的可见窗口发 WM_CLOSE）
if ($Quit) {
    Add-Type -Name QuitWin -Namespace HdrToggle -MemberDefinition @'
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
'@
    $script:found = [IntPtr]::Zero
    $cb = [HdrToggle.QuitWin+EnumWindowsProc] {
        param($h, $lp)
        if ([HdrToggle.QuitWin]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            $null = [HdrToggle.QuitWin]::GetWindowText($h, $sb, 256)
            if ($sb.ToString() -eq 'HDR Toggle') { $script:found = $h; return $false }
        }
        return $true
    }
    $null = [HdrToggle.QuitWin]::EnumWindows($cb, [IntPtr]::Zero)
    if ($script:found -ne [IntPtr]::Zero) {
        $null = [HdrToggle.QuitWin]::PostMessage($script:found, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        'QUIT_OK'
    } else {
        'QUIT_NONE'
    }
    return
}

# ---------- GUI 模式：WPF 悬浮小窗 ----------
# WPF 需要 STA 线程；pwsh 默认可能是 MTA，必要时以 -STA 重启自身
if ([Threading.Thread]::CurrentThread.ApartmentState -ne [Threading.ApartmentState]::STA) {
    & pwsh -NoProfile -STA -WindowStyle Hidden -File $PSCommandPath
    return
}

# 单实例：已有小窗在跑时直接退出（防止双击快捷方式开出多个）
$script:mutex = New-Object System.Threading.Mutex($false, 'Local\HdrToggleWidget')
if (-not $script:mutex.WaitOne(0)) { return }

# 隐藏控制台窗口（从快捷方式启动时几乎无感）
Add-Type -Name Win -Namespace Native -MemberDefinition '
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'
$null = [Native.Win]::ShowWindow([Native.Win]::GetConsoleWindow(), 0)

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$posFile = Join-Path $PSScriptRoot 'hdr-toggle.pos'

$xaml = [Windows.Markup.XamlReader]::Parse(@"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="HDR Toggle" Width="64" Height="64"
        WindowStyle="None" ResizeMode="NoResize" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False">
    <Grid x:Name="Root" Cursor="Hand">
        <Ellipse x:Name="Circle" Fill="#E0333333" Stroke="#66FFFFFF" StrokeThickness="1"/>
        <TextBlock x:Name="Label" Text="HDR" Foreground="#FF888888" FontSize="14" FontWeight="Bold"
                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
    <Window.ContextMenu>
        <ContextMenu>
            <MenuItem Header="退出" x:Name="MenuExit"/>
        </ContextMenu>
    </Window.ContextMenu>
</Window>
"@)

$win   = $xaml
$circle = $xaml.FindName('Circle')
$label  = $xaml.FindName('Label')
$root   = $xaml.FindName('Root')
$menuExit = $xaml.FindName('MenuExit')

# 恢复上次位置
if (Test-Path $posFile) {
    try {
        $pos = (Get-Content $posFile -Raw).Trim() -split ','
        $win.Left = [double]$pos[0]
        $win.Top  = [double]$pos[1]
    } catch { }
}

$script:lastState = -99

function Update-Visual {
    $s = [HdrCtl]::GetHdrState()
    if ($s -eq $script:lastState) { return }
    $script:lastState = $s
    switch ($s) {
        2 { $circle.Fill = '#E0177B4D'; $label.Text = 'HDR';  $label.Foreground = '#FF7CFC9E' }  # 开：绿
        1 { $circle.Fill = '#E0333333'; $label.Text = 'HDR';  $label.Foreground = '#FFAAAAAA' }  # 关：灰
        default { $circle.Fill = '#E06B4A00'; $label.Text = '?'; $label.Foreground = '#FFFFD24A' }  # -1/-2: API/主显异常
    }
}

# ---------- 边缘吸附 + 半隐藏 ----------
# 贴边拖放后吸附到工作区边缘并只露一条小边（半隐藏）；悬停弹出，移开 600ms 后自动缩回；
# 拖离边缘则恢复自由悬浮。配合 XAML 的 ResizeMode="NoResize" 禁用系统贴边分屏放大。
$script:dock   = ''       # '' | left | right | top | bottom
$script:tucked = $false
$sliver = 12              # 半隐藏时保留可见的宽度
$snapPx = 28              # 距边缘多少像素内判定为吸附

function Stop-PosAnim {
    # 释放 Left/Top 上的 HoldEnd 动画并把当前值固化为基础值，避免回跳/与 DragMove 打架
    $lp = [System.Windows.Window]::LeftProperty
    $tp = [System.Windows.Window]::TopProperty
    $x = $win.Left; $y = $win.Top
    $win.BeginAnimation($lp, $null); $win.BeginAnimation($tp, $null)
    $win.Left = $x; $win.Top = $y
}

function Move-WidgetTo([double]$x, [double]$y, [bool]$animate) {
    if (-not $animate) { $win.Left = $x; $win.Top = $y; return }
    $dur  = New-Object System.Windows.Duration([TimeSpan]::FromMilliseconds(180))
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut
    $ax = New-Object System.Windows.Media.Animation.DoubleAnimation($win.Left, $x, $dur)
    $ay = New-Object System.Windows.Media.Animation.DoubleAnimation($win.Top, $y, $dur)
    $ax.EasingFunction = $ease; $ay.EasingFunction = $ease
    $win.BeginAnimation([System.Windows.Window]::LeftProperty, $ax)
    $win.BeginAnimation([System.Windows.Window]::TopProperty, $ay)
}

# dock 边上的目标坐标：hidden=只露 sliver 一条边；revealed=完全贴边可见。
# 另一轴始终夹在工作区内。
function Get-DockPos([string]$edge, [bool]$hidden) {
    $wa = [System.Windows.SystemParameters]::WorkArea
    $w = $win.Width; $h = $win.Height
    $cx = [Math]::Max($wa.Left, [Math]::Min($win.Left, $wa.Right - $w))
    $cy = [Math]::Max($wa.Top,  [Math]::Min($win.Top,  $wa.Bottom - $h))
    $s = if ($hidden) { $sliver } else { [Math]::Max($w, $h) }
    switch ($edge) {
        'left'   { return @{ X = $wa.Left  + $s - $w; Y = $cy } }
        'right'  { return @{ X = $wa.Right - $s;      Y = $cy } }
        'top'    { return @{ Y = $wa.Top    + $s - $h; X = $cx } }
        'bottom' { return @{ Y = $wa.Bottom - $s;     X = $cx } }
    }
}

function Set-DockState([string]$edge, [bool]$hidden, [bool]$animate) {
    $script:dock = $edge; $script:tucked = $hidden
    $p = Get-DockPos $edge $hidden
    Move-WidgetTo $p.X $p.Y $animate
}

# 按当前窗口位置评估吸附：与某边缘的有符号距离 < snapPx（含越出边缘）即吸附并半隐藏
function Update-Dock([bool]$animate) {
    $wa = [System.Windows.SystemParameters]::WorkArea
    $L = $win.Left; $T = $win.Top; $w = $win.Width; $h = $win.Height
    $dist = @{
        left   = $L - $wa.Left
        right  = $wa.Right - ($L + $w)
        top    = $T - $wa.Top
        bottom = $wa.Bottom - ($T + $h)
    }
    $best = ''; $bestV = [double]::MaxValue
    foreach ($k in @('left', 'right', 'top', 'bottom')) {
        if ($dist[$k] -lt $snapPx -and $dist[$k] -lt $bestV) { $best = $k; $bestV = $dist[$k] }
    }
    if ($best -eq '') {
        $script:dock = ''; $script:tucked = $false
    } else {
        Set-DockState $best $true $animate
    }
}

$script:tuckTimer = New-Object System.Windows.Threading.DispatcherTimer
$script:tuckTimer.Interval = [TimeSpan]::FromMilliseconds(600)
$script:tuckTimer.Add_Tick({
    $script:tuckTimer.Stop()
    if ($script:dock -and -not $script:tucked -and -not $root.IsMouseOver) {
        Set-DockState $script:dock $true $true
    }
})

# 拖动 vs 点击的区分：DragMove() 的模态循环会吞掉 MouseLeftButtonUp 事件，
# 因此只能在 DragMove 返回后比较窗口位置——位置未变（且按下时长 <1.5s）视为点击
$root.Add_MouseLeftButtonDown({
    Stop-PosAnim
    $script:downAt = [DateTime]::Now
    $preX = $win.Left; $preY = $win.Top
    try { $win.DragMove() } catch { }
    $moved = [Math]::Abs($win.Left - $preX) + [Math]::Abs($win.Top - $preY)
    if ($moved -lt 4 -and (([DateTime]::Now - $script:downAt).TotalMilliseconds -lt 1500)) {
        if ($script:tucked) { Set-DockState $script:dock $false $true; return }  # 半隐藏时第一击只唤醒，不切换
        [HdrCtl]::ToggleHdr() | Out-Null
        Start-Sleep -Milliseconds 800   # 等 Windows 完成模式切换
        Update-Visual
    } else {
        Update-Dock $true               # 拖放结束：贴近边缘则吸附+半隐藏
    }
})

$root.Add_MouseEnter({
    if ($script:dock) {
        $script:tuckTimer.Stop()
        if ($script:tucked) { Set-DockState $script:dock $false $true }   # 悬停弹出
    }
})

$root.Add_MouseLeave({
    if ($script:dock -and -not $script:tucked) {
        $script:tuckTimer.Stop(); $script:tuckTimer.Start()               # 移开后延迟缩回
    }
})

# 兜底：任何最大化/系统贴靠立即弹回（NoResize 后理论上不会再触发）
$win.Add_StateChanged({
    if ($win.WindowState -ne [System.Windows.WindowState]::Normal) {
        $win.WindowState = [System.Windows.WindowState]::Normal
    }
})

$menuExit.Add_Click({ $win.Close() })

# 每 3 秒轮询状态（外部切换/显示器变化时小窗自动同步）
$timer = New-Object Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(3)
$timer.Add_Tick({ Update-Visual })
$timer.Start()

Update-Visual
Update-Dock $false   # 上次关闭时若贴边，启动即恢复半隐藏

$win.Add_Closed({
    $timer.Stop()
    try {
        "$([int]$win.Left),$([int]$win.Top)" | Set-Content $posFile -Encoding ascii
    } catch { }
})

[void]$win.ShowDialog()
