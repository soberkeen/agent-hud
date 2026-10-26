# diag-hdr-curve.ps1 (v2) — 切关后 HDR 状态曲线 + SDR 白电平交叉验证
# 背景: v1 发现从「陈旧 0x1」状态 SET-OFF 后 12 秒读数不变。v2 改为: 先 SET-ON 制造新鲜 0x3，
#       再 SET-OFF，用 SDR 白电平(type 11, HDR 关=1000, 开=被抬高)验证物理状态 vs 读数。
# info1 bit0=advancedColorEnabled, bit1=advancedColorSupported
# 用法: pwsh -NoProfile -File .\diag-hdr-curve.ps1
param()
$ErrorActionPreference = 'Stop'

$csSource = @'
using System;
using System.Runtime.InteropServices;

public static class HdrDiag {
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

    static bool FindPrimary(out DISPLAYCONFIG_PATH_INFO primary) {
        primary = default(DISPLAYCONFIG_PATH_INFO);
        int pc, mc;
        if (GetDisplayConfigBufferSizes(2, out pc, out mc) != 0) return false;
        var paths = new DISPLAYCONFIG_PATH_INFO[pc];
        var buf = Marshal.AllocHGlobal(mc * 64);
        try {
            int pn = pc, mn = mc;
            if (QueryDisplayConfig(2, ref pn, paths, ref mn, buf, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < pn; i++) {
                if ((paths[i].sourceInfo.statusAndFlags & 0x1) != 0) { primary = paths[i]; return true; }
            }
            return false;
        } finally { Marshal.FreeHGlobal(buf); }
    }

    // info1 原始 value（bit0=enabled, bit1=supported），-1 API 错，-2 无主显 path
    public static int GetRaw1() {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        info.header.type = 9;
        info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
        info.header.adapterId = p.targetInfo.adapterId;
        info.header.id = p.targetInfo.id;
        if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
        return (int)info.value;
    }

    // SDR 白电平（1/1000 × 80nit 单位; HDR 关闭时=1000, 开启时被 SDR 内容亮度抬高）
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

    // 当前刷新率 Hz（用于排查 HDR 模式切换伴随的模式重建）
    public static double GetRefreshHz() {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -1;
        if (p.targetInfo.refreshRate.Denominator == 0) return -1;
        return Math.Round((double)p.targetInfo.refreshRate.Numerator / p.targetInfo.refreshRate.Denominator, 3);
    }

    public static int SetHdr(bool enable) {
        DISPLAYCONFIG_PATH_INFO p;
        if (!FindPrimary(out p)) return -2;
        var pkt = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        pkt.header.type = 10;
        pkt.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
        pkt.header.adapterId = p.targetInfo.adapterId;
        pkt.header.id = p.targetInfo.id;
        pkt.value = enable ? 1u : 0u;
        return DisplayConfigSetDeviceInfo(ref pkt);
    }
}
'@

Add-Type -TypeDefinition $csSource -Language CSharp

$sw = [System.Diagnostics.Stopwatch]::StartNew()
function Log([string]$phase) {
    $r1 = [HdrDiag]::GetRaw1()
    $w  = [HdrDiag]::GetSdrWhite()
    $ac1 = if ($r1 -lt 0) { '-' } elseif (($r1 -band 1) -ne 0) { 'ON ' } else { 'off' }
    $sup = if ($r1 -lt 0) { '-' } elseif (($r1 -band 2) -ne 0) { 'Y' } else { 'N' }
    '{0,7} ms  {1,-13}  info1=0x{2:X} (AC={3} SUP={4})  sdrWhite={5}' -f `
        $sw.ElapsedMilliseconds, $phase, $r1, $ac1, $sup, $w
}

# 显示器身份 + 刷新率
$mon = Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorID | ForEach-Object {
    $n = ($_.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join ''
    $m = ($_.ManufacturerName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join ''
    "$m $n"
}
Write-Host "Monitor: $($mon -join ' | ')   Refresh: $([HdrDiag]::GetRefreshHz()) Hz"
Write-Host "OS: $([Environment]::OSVersion.VersionString)  pwsh: $($PSVersionTable.PSVersion)"
Log 'initial'

# 1) 先制造「新鲜 ON」(期望 0x3)
$rc = [HdrDiag]::SetHdr($true)
Log "SET-ON rc=$rc"
1..8 | ForEach-Object { Start-Sleep -Milliseconds 250; Log 'fresh-on' }

# 2) 切关: 前 1.2s 每 50ms 快采（捕捉瞬态），之后 17s 每 250ms（捕捉回弹）
$rc = [HdrDiag]::SetHdr($false)
Log "SET-OFF rc=$rc"
1..24 | ForEach-Object { Start-Sleep -Milliseconds 50; Log 'off-fast' }
1..68 | ForEach-Object { Start-Sleep -Milliseconds 250; Log 'after-off' }

# 3) 切回开（结束态=开）
$rc = [HdrDiag]::SetHdr($true)
Log "SET-ON rc=$rc"
1..16 | ForEach-Object { Start-Sleep -Milliseconds 250; Log 'after-on' }

Log 'final'
