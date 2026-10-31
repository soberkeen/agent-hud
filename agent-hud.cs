// hdr-toggle.cs — Agent HUD（HDR 开关 + DeepSeek 余额 + GLM 订阅用量悬浮侧边栏）
// 用系统自带 csc.exe (.NET Framework 4.x, 仅支持 C#5 语法) 编译，见 build.ps1；Win10/11 零依赖运行
// 用法:
//   agent-hud.exe              启动侧边栏（单实例，带托盘图标）
//   agent-hud.exe -state       打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
//   agent-hud.exe -toggle      切换一次（退出码=切换后状态: 0=ON 1=OFF 2=错误）
//   agent-hud.exe -on / -off   强制开/关（退出码 0=成功 2=失败）
//   agent-hud.exe -quit        退出（托盘+侧边栏）（0=已关闭 1=未找到）
//   agent-hud.exe -settings    启动并打开设置页（调试/高级用法）
//   agent-hud.exe -popup:ds|zc 启动并固定展开对应抽屉（布局调试用）
// 交互: 图标悬停时在角色下方原位滑出额度抽屉（DeepSeek=纯余额，ZCode=双圈套数字），
//       离开延迟消失，点击固定、再点收回；HDR 钮双击确认（单击进入「确认?」琥珀态，3 秒内再点才切换，超时自动还原）防误触；
//       HDR 圆钮点击切换 HDR；窗口可拖动，拖近屏幕边缘吸附半隐藏；托盘左键显示/隐藏，
//       右键菜单：设置/数据配置/退出；侧边栏右键菜单同
// 数据: ZCode 用量 = {zcode_base}/api/monitor/usage/quota/limit，认证用本机 ZCode 凭证
//       (~/.zcode/v2/credentials.json 的 oauth:bigmodel:access_token，enc:v1 = AES-256-GCM,
//        key=sha256("zcode-credential-fallback:win32:{profile}:{user}")，BCrypt P/Invoke 解密)
//       unit=3,number=5 → 五小时圈；unit=6,number=1 → 本周圈
//       DeepSeek 余额 = {deepseek_base}/user/balance，Bearer key（agent-hud.ini 里 deepseek_key）
// 配置: agent-hud.ini（exe 旁），设置页可视化编辑；开机自启写 HKCU Run 注册表

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;
using WF = System.Windows.Forms;
using Ellipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("Agent HUD")]
[assembly: AssemblyProduct("Agent HUD")]
[assembly: AssemblyDescription("Agent HUD：HDR 开关 + DeepSeek 余额 + GLM 订阅用量悬浮侧边栏")]
[assembly: AssemblyCompany("Agent HUD Project")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Agent HUD Project")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]

namespace AgentHud
{
    // ---------- HDR CCD API（勿改判定逻辑） ----------
    public static class HdrCtl
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusAndFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public uint technology; public uint rotation; public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering;
            public uint targetAvailable; public uint statusAndFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value; public uint colorEncoding; public uint bitsPerPixelFormat;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SDR_WHITE_LEVEL
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint SDRWhiteLevel;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out int pc, out int mc);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref int pc, [In, Out] DISPLAYCONFIG_PATH_INFO[] paths, ref int mc, IntPtr modes, IntPtr topo);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO req);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL req);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE req);

        private static bool FindPrimary(out DISPLAYCONFIG_PATH_INFO primary)
        {
            primary = new DISPLAYCONFIG_PATH_INFO();
            int pc, mc;
            // 注意: QDC_DATABASE_CURRENT(2) 不能与其他 QDC_* 组合，否则返回 87
            if (GetDisplayConfigBufferSizes(2, out pc, out mc) != 0) return false;
            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pc];
            IntPtr buf = Marshal.AllocHGlobal(mc * 64);
            try
            {
                int pn = pc, mn = mc;
                if (QueryDisplayConfig(2, ref pn, paths, ref mn, buf, IntPtr.Zero) != 0) return false;
                for (int i = 0; i < pn; i++)
                {
                    if ((paths[i].sourceInfo.statusAndFlags & 0x1) != 0) { primary = paths[i]; return true; }
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // 返回: -2 无主显 path, -1 API 调用失败, 1 HDR 关, 2 HDR 开
        // 本机(雷神 P275MS PLUS @240Hz)驱动 quirk: 开=0x3(enabled+supported)；关=0x1——
        // enabled 位切关后滞留为 1 不清零，单独不可信。0x1 时用 SDR 白电平仲裁:
        // HDR 关时恒为 1000，开时随「SDR 内容亮度」抬高。唯一误判窗口: HDR 开且滑块拉到最低。
        public static int GetHdrState()
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header.type = 9;   // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO（不是 12！）
            info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
            info.header.adapterId = p.targetInfo.adapterId;
            info.header.id = p.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
            int v = (int)info.value;
            if ((v & 0x3) == 0x3) return 2;
            if ((v & 0x1) != 0) return GetSdrWhite() > 1000 ? 2 : 1;
            return 1;
        }

        // SDR 白电平（1/1000 × 80nit；HDR 关=1000，开=随 SDR 内容亮度抬高）
        public static int GetSdrWhite()
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_SDR_WHITE_LEVEL info = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
            info.header.type = 11;  // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
            info.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SDR_WHITE_LEVEL));
            info.header.adapterId = p.targetInfo.adapterId;
            info.header.id = p.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref info) != 0) return -1;
            return (int)info.SDRWhiteLevel;
        }

        // 返回 0 = 成功
        public static int SetHdr(bool enable)
        {
            DISPLAYCONFIG_PATH_INFO p;
            if (!FindPrimary(out p)) return -2;
            DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE pkt = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            pkt.header.type = 10;   // DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE（不是 13！）
            pkt.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
            pkt.header.adapterId = p.targetInfo.adapterId;
            pkt.header.id = p.targetInfo.id;
            pkt.value = enable ? 1u : 0u;
            return DisplayConfigSetDeviceInfo(ref pkt);
        }

        // 返回切换后的实测状态: 1(关) / 2(开)，失败或异常返回原状态码
        public static int ToggleHdr()
        {
            int s = GetHdrState();
            if (s == 2) return SetHdr(false) == 0 ? GetHdrState() : s;
            if (s == 1) return SetHdr(true) == 0 ? GetHdrState() : s;
            return s;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);

        // 关闭正在运行的实例（标题 HDR Toggle 的可见窗口发 WM_CLOSE）
        public static bool CloseRunningWidget()
        {
            // 不筛可见性：窗口可能处于托盘隐藏状态，也要能被 -quit 关掉
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.ToString() == "Agent HUD" || sb.ToString() == "HDR Toggle")   // 兼容改名前的旧实例
                { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    // ---------- ZCode 凭证解密（enc:v1 = AES-256-GCM，key=sha256(secret)，经 CNG BCrypt） ----------
    public static class ZcodeCred
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AUTH_INFO
        {
            // 注意：真实内存布局 cbSize 在前、dwInfoVersion 在后（MSDN 文档写反了，按文档写会 0xC000000D）
            public int cbSize; public uint dwInfoVersion;
            public IntPtr pbNonce; public int cbNonce;
            public IntPtr pbAuthData; public int cbAuthData;
            public IntPtr pbTag; public int cbTag;
            public IntPtr pbMacContext; public int cbMacContext;
            public int cbAAD; public long cbData;
            public uint dwFlags;
        }
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] private static extern int BCryptOpenAlgorithmProvider(out IntPtr hAlg, string alg, string impl, uint flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] private static extern int BCryptSetProperty(IntPtr hObj, string prop, byte[] val, int size, uint flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] private static extern int BCryptGetProperty(IntPtr hObj, string prop, byte[] val, int size, out int result, uint flags);
        [DllImport("bcrypt.dll")] private static extern int BCryptGenerateSymmetricKey(IntPtr hAlg, out IntPtr hKey, IntPtr pbKeyObject, int cbKeyObject, byte[] secret, int cbSecret, uint flags);
        [DllImport("bcrypt.dll")] private static extern int BCryptDestroyKey(IntPtr hKey);
        [DllImport("bcrypt.dll")] private static extern int BCryptCloseAlgorithmProvider(IntPtr hAlg, uint flags);
        [DllImport("bcrypt.dll")] private static extern int BCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, ref AUTH_INFO pPaddingInfo, IntPtr pbIV, int cbIV, byte[] pbOutput, int cbOutput, out int pcbResult, uint flags);

        // 与 zcode 桌面端一致：env ZCODE_CREDENTIAL_SECRET 优先，否则回退串
        private static string Secret()
        {
            string e = Environment.GetEnvironmentVariable("ZCODE_CREDENTIAL_SECRET");
            if (!string.IsNullOrEmpty(e)) return e;
            return "zcode-credential-fallback:win32:" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + ":" + Environment.UserName;
        }

        public static string Decrypt(string enc)
        {
            if (!enc.StartsWith("enc:v1:")) return enc;
            string[] parts = enc.Substring(7).Split('.');
            if (parts.Length != 3) throw new Exception("密文格式非法");
            byte[] iv = FromB64Url(parts[0]), tag = FromB64Url(parts[1]), ct = FromB64Url(parts[2]);
            if (iv.Length != 12 || tag.Length != 16) throw new Exception("IV/AuthTag 长度非法");
            byte[] key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Secret()));

            IntPtr hAlg, hKey;
            if (BCryptOpenAlgorithmProvider(out hAlg, "AES", null, 0) != 0) throw new Exception("BCryptOpen 失败");
            try
            {
                byte[] gcmVal = Encoding.Unicode.GetBytes("ChainingModeGCM\0");   // 必须 UTF-16 且带空终止符
                if (BCryptSetProperty(hAlg, "ChainingMode", gcmVal, gcmVal.Length, 0) != 0) throw new Exception("设 GCM 失败");
                // 官方示例要求按 ObjectLength 分配 key object（传 NULL 会在解密时报 0xC000000D）
                byte[] lenBuf = new byte[4]; int gotLen;
                if (BCryptGetProperty(hAlg, "ObjectLength", lenBuf, 4, out gotLen, 0) != 0) throw new Exception("取 ObjectLength 失败");
                int objLen = BitConverter.ToInt32(lenBuf, 0);
                IntPtr keyObj = objLen > 0 && objLen < 0x10000 ? Marshal.AllocHGlobal(objLen) : IntPtr.Zero;
                try
                {
                    if (BCryptGenerateSymmetricKey(hAlg, out hKey, keyObj, objLen, key, key.Length, 0) != 0) throw new Exception("导 key 失败");
                    try
                    {
                        IntPtr pn = Marshal.AllocHGlobal(iv.Length);
                        IntPtr pt = Marshal.AllocHGlobal(tag.Length);
                        Marshal.Copy(iv, 0, pn, iv.Length);
                        Marshal.Copy(tag, 0, pt, tag.Length);
                        AUTH_INFO ai = new AUTH_INFO();
                        ai.cbSize = Marshal.SizeOf(typeof(AUTH_INFO)); ai.dwInfoVersion = 1;
                        ai.pbNonce = pn; ai.cbNonce = iv.Length;
                        ai.pbTag = pt; ai.cbTag = tag.Length;
                        try
                        {
                            byte[] ob = new byte[ct.Length]; int got;
                            int st = BCryptDecrypt(hKey, ct, ct.Length, ref ai, IntPtr.Zero, 0, ob, ob.Length, out got, 0);
                            if (st != 0) throw new Exception("解密失败 status=0x" + st.ToString("X"));
                            return Encoding.UTF8.GetString(ob, 0, got);
                        }
                        finally { Marshal.FreeHGlobal(pn); Marshal.FreeHGlobal(pt); }
                    }
                    finally { BCryptDestroyKey(hKey); }
                }
                finally { if (keyObj != IntPtr.Zero) Marshal.FreeHGlobal(keyObj); }
            }
            finally { BCryptCloseAlgorithmProvider(hAlg, 0); }
        }

        private static byte[] FromB64Url(string s)
        {
            string b = s.Replace('-', '+').Replace('_', '/');
            while (b.Length % 4 != 0) b += "=";
            return Convert.FromBase64String(b);
        }
    }

    // ---------- v2.1 改名迁移：pos / ini / 自启注册表 旧名 -> 新名 ----------
    internal static class LegacyMigrator
    {
        public static void Run()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                MigrateFile(Path.Combine(dir, "hdr-toggle.pos"), Path.Combine(dir, "agent-hud.pos"));
                MigrateFile(Path.Combine(dir, "hdr-sidebar.ini"), Path.Combine(dir, "agent-hud.ini"));
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    object oldV = k.GetValue("HDR Toggle");
                    object newV = k.GetValue("Agent HUD");
                    if (oldV is string && newV == null)
                    {
                        // 仅当新值不存在时才沿用旧路径（避免覆盖用户在设置里写入的新值）
                        string p = ((string)oldV).Trim('"');
                        if (File.Exists(p)) k.SetValue("Agent HUD", "\"" + p + "\"");
                    }
                    if (oldV != null) k.DeleteValue("HDR Toggle", false);
                }
            }
            catch { }
        }

        private static void MigrateFile(string oldPath, string newPath)
        {
            try
            {
                if (File.Exists(oldPath) && !File.Exists(newPath)) File.Copy(oldPath, newPath);
            }
            catch { }
        }
    }

    // ---------- DeepSeek key：Windows 凭据管理器存储（ini 不落密钥） ----------
    public static class SecretStore
    {
        private const string Target = "AgentHUD/DeepSeekKey";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref CREDENTIAL cred, int flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, int type, int flags, out IntPtr credPtr);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr cred);

        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        public static string Get()
        {
            IntPtr p;
            if (!CredReadW(Target, CRED_TYPE_GENERIC, 0, out p)) return "";
            try
            {
                CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                byte[] blob = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, blob, 0, blob.Length);
                return Encoding.Unicode.GetString(blob);
            }
            finally { CredFree(p); }
        }

        public static void Set(string key)
        {
            byte[] blob = string.IsNullOrEmpty(key) ? new byte[0] : Encoding.Unicode.GetBytes(key);
            IntPtr pBlob = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
            try
            {
                Marshal.Copy(blob.Length == 0 ? new byte[1] : blob, 0, pBlob, blob.Length);
                CREDENTIAL c = new CREDENTIAL();
                c.Flags = 0; c.Type = CRED_TYPE_GENERIC;
                c.TargetName = Target;
                c.Comment = "Agent HUD DeepSeek API key";
                c.CredentialBlobSize = blob.Length;
                c.CredentialBlob = pBlob;
                c.Persist = CRED_PERSIST_LOCAL_MACHINE;
                c.AttributeCount = 0; c.Attributes = IntPtr.Zero;
                c.TargetAlias = null;
                c.UserName = Environment.UserName;
                if (!CredWriteW(ref c, 0)) throw new Exception("CredWrite 失败");
            }
            finally { Marshal.FreeHGlobal(pBlob); }
        }
    }

    // ---------- 配置（agent-hud.ini） ----------
    public static class SettingsData
    {
        public static string ExeDir { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        public static string IniPath { get { return Path.Combine(ExeDir, "agent-hud.ini"); } }

        public static Dictionary<string, string> Defaults()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["deepseek_key"] = "";
            d["deepseek_base"] = "https://api.deepseek.com";
            d["zcode_base"] = "";                    // 空=自动（按凭证 family 选 bigmodel/z.ai）
            d["zcode_credentials"] = "";             // 空=默认 ~/.zcode/v2/credentials.json
            d["refresh_minutes"] = "5";
            d["warn_low"] = "20";                    // 剩余 <20% 红
            d["warn_mid"] = "50";                    // 剩余 <50% 黄
            d["hover_details"] = "true";             // 悬停弹出详情
            d["show_tray"] = "true";                 // 托盘图标
            d["zc_mood_source"] = "five";            // GLM 表情驱动：five=5小时 / week=周 / worst=取最差
            d["show_ds"] = "true";                   // 显示 DeepSeek 角色
            d["show_zc"] = "true";                   // 显示 GLM 角色
            d["launch_zcode"] = "";                  // 双击 GLM 角色启动的程序
            d["launch_dsh"] = "";                    // 双击 DeepSeek 角色启动的程序
            d["fullscreen_action"] = "hide";         // 前台全屏程序时：hide=隐藏 / tuck=缩边 / none=不动
            d["skin"] = "dark";                      // 面板材质：dark=黑 / glass=玻璃 / blur=毛玻璃
            d["blur_tint"] = "C8141E26";             // （仅旧系统生效）毛玻璃 Accent 色调
            d["blur_veil"] = "30";                   // 毛玻璃蒙版浓度（十六进制 alpha，越小越透）
            d["ds_warn"] = "20";                     // DeepSeek 余额 <X 元 → 紧张
            d["ds_daze"] = "1";                      // DeepSeek 余额 ≤Y 元 → 痴呆
            return d;
        }

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> d = Defaults();
            try
            {
                if (File.Exists(IniPath))
                {
                    foreach (string raw in File.ReadAllLines(IniPath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim().Trim('"');
                        if (d.ContainsKey(k)) d[k] = v;
                    }
                }
            }
            catch { }
            // 双击启动路径兜底：launch_zcode 未配置时探测 ZCode 桌面端安装目录
            try
            {
                if (d["launch_zcode"].Length == 0)
                {
                    string zdir = Environment.GetEnvironmentVariable("ZCODE_WINDOWS_APP_INSTALL_DIR");
                    string zexe = zdir == null ? "" : Path.Combine(zdir, "ZCode.exe");
                    if (zexe.Length > 0 && File.Exists(zexe)) d["launch_zcode"] = zexe;
                }
            }
            catch { }

            // DeepSeek key 从凭据管理器读取；旧版 ini 明文 key 自动迁移并从文件移除
            try
            {
                string key = SecretStore.Get();
                if (key.Length == 0 && d["deepseek_key"].Length > 0)
                {
                    SecretStore.Set(d["deepseek_key"]);
                    key = d["deepseek_key"];
                    try
                    {
                        if (File.Exists(IniPath))
                        {
                            var kept = new List<string>();
                            foreach (string raw in File.ReadAllLines(IniPath))
                                if (!raw.TrimStart().StartsWith("deepseek_key=")) kept.Add(raw);
                            File.WriteAllLines(IniPath, kept);
                        }
                    }
                    catch { }
                }
                d["deepseek_key"] = key;
            }
            catch { }
            return d;
        }

        public static void Save(Dictionary<string, string> d)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Agent HUD 配置（可在设置页修改，或手动编辑）");
            sb.AppendLine("# DeepSeek API key 存放在 Windows 凭据管理器（设置页可修改），不落本文件");
            sb.AppendLine("deepseek_base=" + d["deepseek_base"]);
            sb.AppendLine("# ZCode 接口地址：留空自动（按本机 ZCode 凭证选择 bigmodel.cn / z.ai）");
            sb.AppendLine("zcode_base=" + d["zcode_base"]);
            sb.AppendLine("# 数据刷新间隔（分钟）");
            sb.AppendLine("refresh_minutes=" + d["refresh_minutes"]);
            sb.AppendLine("# 额度变色阈值（剩余百分比）：低于 warn_low 红，低于 warn_mid 黄");
            sb.AppendLine("warn_low=" + d["warn_low"]);
            sb.AppendLine("warn_mid=" + d["warn_mid"]);
            sb.AppendLine("# 悬停图标弹出详情面板");
            sb.AppendLine("hover_details=" + d["hover_details"]);
            sb.AppendLine("# 显示托盘图标");
            sb.AppendLine("show_tray=" + d["show_tray"]);
            sb.AppendLine("# GLM 角色表情由哪个额度驱动：five=5小时 / week=本周 / worst=取更差的一个");
            sb.AppendLine("zc_mood_source=" + d["zc_mood_source"]);
            sb.AppendLine("# 显示哪些角色（只看一个或都不要都可以）");
            sb.AppendLine("show_ds=" + d["show_ds"]);
            sb.AppendLine("show_zc=" + d["show_zc"]);
            sb.AppendLine("# 双击角色启动的程序（GLM 角色双击启动 zcode / DeepSeek 角色双击启动 dsh）");
            sb.AppendLine("launch_zcode=" + d["launch_zcode"]);
            sb.AppendLine("launch_dsh=" + d["launch_dsh"]);
            sb.AppendLine("fullscreen_action=" + d["fullscreen_action"]);
            sb.AppendLine("skin=" + d["skin"]);
            sb.AppendLine("blur_tint=" + d["blur_tint"]);
            sb.AppendLine("blur_veil=" + d["blur_veil"]);
            sb.AppendLine("# DeepSeek 余额分档（元）：低于 ds_warn 紧张，低于等于 ds_daze 痴呆");
            sb.AppendLine("ds_warn=" + d["ds_warn"]);
            sb.AppendLine("ds_daze=" + d["ds_daze"]);
            File.WriteAllText(IniPath, sb.ToString(), Encoding.UTF8);
        }

        public static bool Flag(Dictionary<string, string> d, string key)
        {
            string v = d[key].Trim().ToLowerInvariant();
            return v != "false" && v != "0" && v != "off" && v != "no";
        }

        public static double Num(Dictionary<string, string> d, string key, double fallback)
        {
            double v;
            return double.TryParse(d[key], out v) ? v : fallback;
        }

        // ---------- 开机自启（HKCU Run） ----------
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Agent HUD";

        public static bool GetAutostart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return k != null && k.GetValue(RunValue) != null;
                }
            }
            catch { return false; }
        }

        public static void SetAutostart(bool on)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = Path.Combine(ExeDir, "agent-hud.exe");
                        if (!File.Exists(exe)) return;   // 目标不存在就不写，避免残留死路径
                        k.SetValue(RunValue, "\"" + exe + "\"");
                    }
                    else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
                }
            }
            catch { }
        }
    }

    // ---------- 数据源：DeepSeek 余额 + ZCode 订阅用量 ----------
    public class DsInfo
    {
        public bool Ok; public string Balance; public string Currency; public string Granted; public string Error;
    }
    public class LimitInfo
    {
        public double RemainFrac = -1;   // 剩余比例 0~1；-1=未知
        public double Used, Total;
        public DateTime ResetAt = DateTime.MinValue;
    }
    public class ZcInfo
    {
        public bool Ok; public LimitInfo Five = new LimitInfo(); public LimitInfo Week = new LimitInfo();
        public string Level = ""; public string Error;
    }

    public static class DataFeed
    {
        private static void SetTls()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }   // TLS 1.2
        }

        private static string HttpGet(string url, string bearer, int timeoutMs, out long httpStatus)
        {
            SetTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            req.Accept = "application/json";
            httpStatus = -1;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    httpStatus = (long)resp.StatusCode;
                    return sr.ReadToEnd();
                }
            }
            catch (WebException we)
            {
                HttpWebResponse er = we.Response as HttpWebResponse;
                if (er != null)
                {
                    httpStatus = (long)er.StatusCode;
                    using (StreamReader sr = new StreamReader(er.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
                }
                throw;
            }
        }

        private static IDictionary<string, object> JsonObj(JavaScriptSerializer js, string s)
        {
            return js.DeserializeObject(s) as IDictionary<string, object>;
        }
        private static double Num(object o)
        {
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            if (o is double) return (double)o;
            if (o is decimal) return (double)(decimal)o;
            if (o is string) { double v; if (double.TryParse((string)o, out v)) return v; }
            return double.NaN;
        }
        private static object Get(IDictionary<string, object> d, string k)
        {
            object o; d.TryGetValue(k, out o); return o;
        }

        // ---------- DeepSeek ----------
        // GET /user/balance → 新版 {"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"..","granted_balance":"..","topped_up_balance":".."}]}
        //                    （旧版 balance_info 单对象，兼容解析）
        public static DsInfo FetchDeepSeek(string baseUrl, string key)
        {
            DsInfo r = new DsInfo();
            if (key == null) key = "";
            key = key.Trim();
            if (key.Length == 0) { r.Error = "未配置 API Key"; return r; }
            try
            {
                long st;
                string body = HttpGet(baseUrl.TrimEnd('/') + "/user/balance", key, 12000, out st);
                JavaScriptSerializer js = new JavaScriptSerializer();
                IDictionary<string, object> root = JsonObj(js, body);
                if (root == null) throw new Exception("响应不是 JSON（HTTP " + st + "）");
                if (st != 200)
                {
                    // 透传服务器错误：{"error":{"message":"..."}} 或 {"msg":"..."}
                    string msg = "";
                    object errO;
                    if (root.TryGetValue("error", out errO) && errO is IDictionary<string, object>)
                    {
                        object m;
                        if (((IDictionary<string, object>)errO).TryGetValue("message", out m) && m != null) msg = m.ToString();
                    }
                    if (msg.Length == 0) { object m2; if (root.TryGetValue("msg", out m2) && m2 != null) msg = m2.ToString(); }
                    if (msg.Length == 0) msg = body.Length > 120 ? body.Substring(0, 120) : body;
                    throw new Exception("HTTP " + st + (msg.Length > 0 ? "：" + msg : ""));
                }
                // 新版 balance_infos 数组（取第一条）；兼容旧版 balance_info 对象
                object bal;
                if (!root.TryGetValue("balance_infos", out bal) || !(bal is IEnumerable<object>))
                    root.TryGetValue("balance_info", out bal);
                if (bal is IEnumerable<object>)
                {
                    foreach (object it in (IEnumerable<object>)bal)
                    {
                        bal = it as IDictionary<string, object>;
                        if (bal != null) break;
                    }
                }
                if (!(bal is IDictionary<string, object>)) throw new Exception("响应里没有余额字段（HTTP " + st + "）");
                IDictionary<string, object> b = (IDictionary<string, object>)bal;
                object tb, gb, cur;
                b.TryGetValue("total_balance", out tb);
                b.TryGetValue("granted_balance", out gb);
                b.TryGetValue("currency", out cur);
                r.Balance = tb == null ? "?" : tb.ToString();
                r.Granted = gb == null ? "?" : gb.ToString();
                r.Currency = cur == null ? "CNY" : cur.ToString();
                r.Ok = true;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        // ---------- ZCode ----------
        private static string CredentialsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".zcode", "v2", "credentials.json");
        }

        // 从本机 zcode 凭证取 access token：优先 bigmodel，其次 zai（返回 null 表示 family 未找到）
        private static string LoadAccessToken(string preferredFamily, out string family)
        {
            family = null;
            string path = CredentialsPath();
            if (!File.Exists(path)) throw new Exception("未找到 ZCode 凭证文件");
            JavaScriptSerializer js = new JavaScriptSerializer();
            IDictionary<string, object> root = JsonObj(js, File.ReadAllText(path));
            if (root == null) throw new Exception("凭证文件不是 JSON");
            string[] order = preferredFamily == "zai"
                ? new string[] { "oauth:zai:access_token", "oauth:bigmodel:access_token" }
                : new string[] { "oauth:bigmodel:access_token", "oauth:zai:access_token" };
            foreach (string k in order)
            {
                object v;
                if (root.TryGetValue(k, out v) && v is string)
                {
                    family = k.Contains("zai") ? "zai" : "bigmodel";
                    return ZcodeCred.Decrypt((string)v);
                }
            }
            throw new Exception("凭证里没有 oauth access_token（先在 ZCode 桌面端登录）");
        }

        // GET /api/monitor/usage/quota/limit → {code:200,data:{limits:[{unit,number,usage,currentValue,remaining,percentage,nextResetTime}],level}}
        public static ZcInfo FetchZcode(Dictionary<string, string> cfg)
        {
            ZcInfo r = new ZcInfo();
            try
            {
                string family;
                string token = LoadAccessToken(null, out family);
                string baseUrl = cfg["zcode_base"];
                if (string.IsNullOrEmpty(baseUrl))
                    baseUrl = family == "zai" ? "https://api.z.ai" : "https://open.bigmodel.cn";
                long st;
                string body = HttpGet(baseUrl.TrimEnd('/') + "/api/monitor/usage/quota/limit", token, 15000, out st);
                JavaScriptSerializer js = new JavaScriptSerializer();
                IDictionary<string, object> root = JsonObj(js, body);
                if (root == null) throw new Exception("响应不是 JSON");
                object codeO; root.TryGetValue("code", out codeO);
                if (Num(codeO) != 200) throw new Exception("code=" + (codeO == null ? "?" : codeO.ToString()));
                object dataO;
                if (!root.TryGetValue("data", out dataO) || !(dataO is IDictionary<string, object>)) throw new Exception("缺少 data");
                IDictionary<string, object> data = (IDictionary<string, object>)dataO;
                object lvl; data.TryGetValue("level", out lvl);
                r.Level = lvl == null ? "" : lvl.ToString();
                object limO;
                if (!data.TryGetValue("limits", out limO) || !(limO is IEnumerable<object>)) throw new Exception("缺少 limits");
                foreach (object item in (IEnumerable<object>)limO)
                {
                    IDictionary<string, object> m = item as IDictionary<string, object>;
                    if (m == null) continue;
                    double unit = Num(Get(m, "unit")), number = Num(Get(m, "number"));
                    double total = Num(Get(m, "usage")), used = Num(Get(m, "currentValue")), remain = Num(Get(m, "remaining"));
                    LimitInfo li = null;
                    if (unit == 3 && number == 5) li = r.Five;          // 5 小时窗
                    else if (unit == 6 && number == 1) li = r.Week;     // 1 周
                    if (li == null) continue;
                    li.Used = used; li.Total = total;
                    if (!double.IsNaN(remain) && total > 0) li.RemainFrac = Math.Max(0, Math.Min(1, remain / total));
                    object nr; m.TryGetValue("nextResetTime", out nr);
                    if (nr != null)
                    {
                        double ms = Num(nr);
                        if (!double.IsNaN(ms) && ms > 0) li.ResetAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
                    }
                }
                if (r.Five.RemainFrac < 0 && r.Week.RemainFrac < 0) throw new Exception("limits 里没有 5h/周额度");
                r.Ok = true;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        public static string CurrencySymbol(string c)
        {
            if (c == "CNY") return "¥";
            if (c == "USD") return "$";
            return c + " ";
        }

        // 额度→分档：0 充足 / 1 中等 / 2 见底（阈值可在设置页调）
        public static int MoodFromRemain(double frac, double low, double mid)
        {
            if (frac > mid) return 0;
            if (frac > low) return 1;
            return 2;
        }
    }

    // ---------- 小工具 ----------
    internal static class Ui
    {
        public static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            b2.Freeze();
            return b2;
        }
        public static Color Col(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }
        public static Color Col(byte a, byte r, byte g, byte b) { return Color.FromArgb(a, r, g, b); }

        // 圆环零件（进度弧 + 底环 + 中央数字）
        public class RingParts
        {
            public Grid Ring; public Ellipse Bg; public System.Windows.Shapes.Path Arc; public TextBlock Num;
            public double Size, Stroke;
        }
        public static RingParts MakeRing(double size, double stroke, double font)
        {
            RingParts p = new RingParts();
            p.Size = size; p.Stroke = stroke;
            p.Ring = new Grid { Width = size, Height = size };
            p.Bg = new Ellipse { Stroke = Ui.Brush(0x33, 0xFF, 0xFF, 0xFF), StrokeThickness = stroke, IsHitTestVisible = false };
            p.Arc = new System.Windows.Shapes.Path
            {
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            p.Num = new TextBlock
            {
                FontSize = font,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            p.Ring.Children.Add(p.Bg);
            p.Ring.Children.Add(p.Arc);
            p.Ring.Children.Add(p.Num);
            return p;
        }

        // frac<0 → 错误态灰圈 "?"
        public static void SetRing(RingParts p, double frac, Color color)
        {
            SolidColorBrush b = new SolidColorBrush(frac < 0 ? Ui.Col(0x99, 0x99, 0x99) : color);
            b.Freeze();
            p.Arc.Stroke = b;
            p.Num.Foreground = frac >= 0 ? b : Ui.Brush(0xFF, 0x99, 0x99, 0x99);
            p.Bg.Stroke = Ui.Brush(0x33, 0xFF, 0xFF, 0xFF);
            if (frac < 0) { p.Arc.Data = null; p.Num.Text = "?"; return; }
            p.Num.Text = ((int)Math.Round(frac * 100)).ToString();
            if (frac >= 0.995) { p.Arc.Data = null; p.Bg.Stroke = b; return; }   // 满圈直接画整圆
            double r = (p.Size - p.Stroke) / 2, c0 = p.Size / 2;
            double sweep = 360.0 * frac;
            double a1 = -Math.PI / 2 + sweep * Math.PI / 180.0;
            PathFigure fig = new PathFigure();
            fig.StartPoint = new Point(c0, c0 - r);
            ArcSegment seg = new ArcSegment();
            seg.Point = new Point(c0 + r * Math.Cos(a1), c0 + r * Math.Sin(a1));
            seg.Size = new Size(r, r);
            seg.IsLargeArc = sweep > 180;
            seg.SweepDirection = SweepDirection.Clockwise;
            fig.Segments.Add(seg);
            PathGeometry pg = new PathGeometry();
            pg.Figures.Add(fig);
            p.Arc.Data = pg;
        }

        public static ImageSource ResImage(string resName)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = s;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return null; }
        }

        public static Image IconImage(string resName, double size)
        {
            Image img = new Image
            {
                Width = size, Height = size,
                Source = ResImage(resName),
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }
    }

    // ---------- 帧动画播放器（精灵图 + 乒乓循环，按额度状态切表） ----------
    internal class FramePlayer
    {
        private readonly Image _img = new Image();
        private readonly string _prefix;      // assets 文件名前缀，如 "anim-df-"
        private readonly string _fallbackRes; // 全部缺失时的静态兜底（内嵌资源）
        private readonly Dictionary<string, BitmapSource> _cache = new Dictionary<string, BitmapSource>();
        private string _sheet;
        private int _frames = 1;
        private BitmapSource[] _frameArr;     // GIF 模式：独立帧
        private int[] _delays;                // GIF 模式：每帧延时 ms
        private bool _sheetMode = true;       // true=PNG 精灵图裁切 / false=独立帧数组
        private int _loopMs;                  // GIF 模式：整圈毫秒
        private const int FrameW = 96, FrameH = 96;

        private readonly TranslateTransform _shift = new TranslateTransform();

        public FramePlayer(string prefix, double size, string fallbackRes)
        {
            _prefix = prefix;
            _fallbackRes = fallbackRes;
            _img.Width = size;
            _img.Height = size;
            _img.IsHitTestVisible = false;
            _img.RenderTransform = _shift;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            SetSheet("normal");
        }

        public Image Visual { get { return _img; } }

        // 紧张档发抖：小幅水平位移
        public void ShiftX(double x) { _shift.X = x; }

        private string _lastKey;

        public void SetSheet(string key)
        {
            _lastKey = key;
            if (_sheet == key) return;
            // 1) 用户自制 GIF（assets\anim-XX-<状态>.gif）
            string gif = Path.Combine(Assets.Dir(), _prefix + key + ".gif");
            if (LoadGif(gif)) { _sheet = key; SetFrame(0); return; }
            // 2) PNG 精灵图（流水线产物）
            BitmapSource bmp = Assets.Load(_prefix + key + ".png", _fallbackRes);
            if (bmp == null) { _sheet = null; return; }
            _frameArr = null; _delays = null; _sheetMode = true;
            _frames = Math.Max(1, bmp.PixelWidth / FrameW);
            _loopMs = _frames * 33;
            _sheet = key;
            SetFrame(0);
        }

        // 设置保存/素材更换后调用：清缓存重新加载（无需重启）
        public void Reload()
        {
            _cache.Clear();
            string k = _lastKey;
            _sheet = null; _lastKey = null;
            if (k != null) SetSheet(k);
        }

        private bool LoadGif(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                GifBitmapDecoder dec = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (dec.Frames == null || dec.Frames.Count == 0) return false;
                List<BitmapSource> fl = new List<BitmapSource>();
                List<int> dl = new List<int>();
                foreach (BitmapFrame f in dec.Frames)
                {
                    double sc = 96.0 / Math.Max(1, f.PixelWidth);
                    TransformedBitmap tb = new TransformedBitmap(f.Clone(), new ScaleTransform(sc, sc));
                    tb.Freeze();
                    fl.Add(tb);
                    int ms = 60;
                    try
                    {
                        BitmapMetadata md = f.Metadata as BitmapMetadata;
                        if (md != null)
                        {
                            object d = md.GetQuery("/grctlext/Delay");   // 单位 1/100 秒
                            if (d != null) ms = Math.Max(20, Math.Min(500, Convert.ToInt32(d) * 10));
                        }
                    }
                    catch { }
                    dl.Add(ms);
                }
                _frameArr = fl.ToArray();
                _delays = dl.ToArray();
                _frames = _frameArr.Length;
                _sheetMode = false;
                _loopMs = 0;
                foreach (int x in _delays) _loopMs += x;
                return _frames > 0;
            }
            catch { return false; }
        }

        // 按绝对时间推进到对应帧（时基由 Widget 的秒表提供）
        public void Advance(int elapsedMs)
        {
            if (_sheetMode) { SetFrame(elapsedMs / 33); return; }          // 精灵图 30fps
            if (_loopMs <= 0) return;
            int t = ((elapsedMs % _loopMs) + _loopMs) % _loopMs;
            int acc = 0;
            for (int i = 0; i < _delays.Length; i++)
            {
                acc += _delays[i];
                if (t < acc) { ShowFrameArr(i); return; }
            }
            ShowFrameArr(_delays.Length - 1);
        }

        private void ShowFrameArr(int i)
        {
            if (_frameArr == null || _frameArr.Length == 0) return;
            i = Math.Max(0, Math.Min(_frameArr.Length - 1, i));
            if (i != _lastShownArr) { _lastShownArr = i; _img.Source = _frameArr[i]; }
        }
        private int _lastShownArr = -1;

        public void SetFrame(int i)
        {
            if (!_sheetMode || _sheet == null || _frames <= 0) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(_sheet, out bmp) || bmp == null) return;
            i = ((i % _frames) + _frames) % _frames;   // 循环取帧
            _img.Source = new CroppedBitmap(bmp, new Int32Rect(i * FrameW, 0, FrameW, FrameH));
        }
    }

    internal static class Assets
    {
        public static string Dir()
        {
            return Path.Combine(SettingsData.ExeDir, "assets");
        }

        // 优先加载 assets 文件夹里的文件，缺失则回退内嵌资源（静态兜底）
        public static BitmapSource Load(string fileName, string embeddedFallback)
        {
            try
            {
                string p = Path.Combine(Dir(), fileName);
                if (File.Exists(p))
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(p);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }
            return string.IsNullOrEmpty(embeddedFallback) ? null : Ui.ResImage(embeddedFallback) as BitmapSource;
        }
    }

    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate(object snd, UnhandledExceptionEventArgs ea)
                {
                    try { System.IO.File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "crash.log"),
                        DateTime.Now + " APPDOMAIN\r\n" + ea.ExceptionObject + "\r\n"); } catch { }
                };
                LegacyMigrator.Run();
                // 只把 -/ 开头的参数当 CLI 命令；其余杂散参数（如快捷方式残留）忽略，直接进 GUI
                string first = args.Length > 0 ? args[0] : "";
                if (first.StartsWith("-") || first.StartsWith("/"))
                {
                    string cmd = first.TrimStart('-', '/').ToLowerInvariant();
                    if (cmd == "popup:ds" || cmd == "popup:zc") return RunGui(false, cmd.Substring(6));
                    if (cmd == "settings") return RunGui(true, null);
                    if (cmd == "mockcycle") return RunGui(false, null, true);
                    if (cmd == "hover:ds" || cmd == "hover:zc") return RunGui(false, null, false, cmd.Substring(6));
                    return RunCli(cmd);
                }
                return RunGui(false, null);
            }
            catch (Exception ex)
            {
                try { MessageBox(IntPtr.Zero, ex.ToString(), "Agent HUD", 0x10); } catch { }
                return 3;
            }
        }

        static int RunCli(string arg)
        {
            AttachConsoleOut();
            switch (arg)
            {
                case "state":
                    {
                        int s = HdrCtl.GetHdrState();
                        Out(StateText(s));
                        return s == 2 ? 0 : (s == 1 ? 1 : 2);
                    }
                case "toggle":
                    {
                        int s = HdrCtl.ToggleHdr();
                        Out(StateText(s));
                        return s == 2 ? 0 : (s == 1 ? 1 : 2);
                    }
                case "on":
                case "off":
                    {
                        int r = HdrCtl.SetHdr(arg == "on");
                        Out(r == 0 ? "OK" : "ERROR(" + r + ")");
                        return r == 0 ? 0 : 2;
                    }
                case "quit":
                    {
                        bool ok = HdrCtl.CloseRunningWidget();
                        Out(ok ? "QUIT_OK" : "QUIT_NONE");
                        return ok ? 0 : 1;
                    }
                case "debugdata":
                    {
                        Dictionary<string, string> cfg = SettingsData.Load();
                        DsInfo ds = DataFeed.FetchDeepSeek(cfg["deepseek_base"], cfg["deepseek_key"]);
                        Out("DeepSeek: " + (ds.Ok ? "OK balance=" + ds.Balance + " " + ds.Currency + " granted=" + ds.Granted : "FAIL " + ds.Error));
                        ZcInfo zc = DataFeed.FetchZcode(cfg);
                        Out("ZCode: " + (zc.Ok
                            ? "OK level=" + zc.Level + " 5h=" + zc.Five.RemainFrac.ToString("P0") + " (" + zc.Five.Used + "/" + zc.Five.Total + ") week=" + zc.Week.RemainFrac.ToString("P0") + " (" + zc.Week.Used + "/" + zc.Week.Total + ")"
                            : "FAIL " + zc.Error));
                        return (ds.Ok && zc.Ok) ? 0 : 2;
                    }
                default:
                    Out("usage: agent-hud.exe [-state|-toggle|-on|-off|-quit|-debugdata|-settings|-popup:ds|-popup:zc|-mockcycle]");
                    return 9;
            }
        }

        static string StateText(int s)
        {
            if (s == 2) return "ON";
            if (s == 1) return "OFF";
            if (s == -2) return "NO_PRIMARY_DISPLAY";
            return "ERROR(" + s + ")";
        }

        static void Out(string s) { try { Console.WriteLine(s); } catch { } }

        // winexe 默认无控制台：挂到父进程终端后重接 stdout，让 CLI 输出可见
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
        [DllImport("user32.dll")] private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

        static void AttachConsoleOut()
        {
            try
            {
                if (AttachConsole(-1))
                {
                    StreamWriter w = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(w);
                }
            }
            catch { }
        }

        static int RunGui(bool openSettings, string popupDebug, bool mockCycle = false, string hoverDebug = null)
        {
            _mutex = new Mutex(false, "Local\\AgentHud");   // 单实例
            bool owned;
            try { owned = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                // 已有实例在跑（窗口可能被藏了）：发信号让老实例把窗口亮出来
                EventWaitHandle show;
                if (EventWaitHandle.TryOpenExisting("Local\\AgentHudShow", out show))
                {
                    try { show.Set(); } catch { } finally { show.Dispose(); }
                }
                return 0;
            }
            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AgentHudShow");
            Widget w = new Widget(showEvent);
            w.MockCycle = mockCycle;
            w.Show();
            if (hoverDebug != null) w.DebugHover(hoverDebug);
            if (openSettings) w.OpenSettings();
            if (popupDebug != null) w.DebugPopup(popupDebug);
            w.Start();          // v1.3 起漏掉过这行：轮询/数据刷新定时器全靠它
            Dispatcher.CurrentDispatcher.UnhandledException += delegate(object snd, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs ea)
            {
                try { System.IO.File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "crash.log"),
                    DateTime.Now + " DISPATCHER\r\n" + ea.Exception + "\r\n"); } catch { }
                ea.Handled = false;
            };
            Dispatcher.Run();   // 无 Application 对象，手动泵消息；Widget.Closed 里 BeginInvokeShutdown
            return 0;
        }
    }

    // ---------- 设置窗口（固定尺寸 + 页签：通用/数据源/关于） ----------
    public class SettingsWindow : Window
    {
        private readonly Widget _host;
        private readonly Dictionary<string, string> _cfg;
        private readonly Dictionary<string, string> _initialCfg;
        private CheckBox _chkAutostart, _chkTray, _chkHover, _chkShowKey, _chkShowDs, _chkShowZc;
        private ComboBox _cmbRefresh, _cmbLow, _cmbMid, _cmbMoodSrc, _cmbFs, _cmbSkin, _cmbDsWarn, _cmbDsDaze, _cmbBlurTint;
        private PasswordBox _pwdKey;
        private TextBox _txtKey, _txtDsBase, _txtZcBase, _txtLaunchZc, _txtLaunchDs;
        private TextBlock _testResult;
        private bool _saved;
        private readonly List<Border> _tabBtns = new List<Border>();
        private readonly List<StackPanel> _tabPages = new List<StackPanel>();

        public SettingsWindow(Widget host)
        {
            _host = host;
            _cfg = host.CurrentConfig;
            _initialCfg = new Dictionary<string, string>(_cfg);
            Title = "Agent HUD · 设置";
            Width = 480; Height = 470;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;                    // 侧边栏本身 topmost，设置页跟它平级才不会被盖住
            Background = Ui.Brush(0xFF, 0x1A, 0x1A, 0x1A);
            Icon = TryIcon();

            Grid root = new Grid { Margin = new Thickness(12) };
            RowDefinition r0 = new RowDefinition(); r0.Height = GridLength.Auto;
            RowDefinition r1 = new RowDefinition(); r1.Height = new GridLength(1, GridUnitType.Star);
            RowDefinition r2 = new RowDefinition(); r2.Height = GridLength.Auto;
            RowDefinition r3 = new RowDefinition(); r3.Height = GridLength.Auto;
            root.RowDefinitions.Add(r0); root.RowDefinitions.Add(r1);
            root.RowDefinitions.Add(r2); root.RowDefinitions.Add(r3);
            Content = root;

            // ---- 页签栏 ----
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetRow(bar, 0);
            root.Children.Add(bar);

            // ---- 页签内容区 ----
            Border contentBox = new Border
            {
                CornerRadius = new CornerRadius(0, 10, 10, 10),
                Background = Ui.Brush(0xFF, 0x22, 0x22, 0x22),
                BorderBrush = Ui.Brush(0x22, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };
            Grid.SetRow(contentBox, 1);
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(4, 0, 6, 0),
            };
            Grid pages = new Grid();
            scroll.Content = pages;
            contentBox.Child = scroll;
            root.Children.Add(contentBox);

            // ===== 页签 0：通用 =====
            StackPanel p0 = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Visible };
            pages.Children.Add(p0); _tabPages.Add(p0);
            AddTab(bar, "通用", 0);
            _chkAutostart = AddCheck(p0, "开机自动启动侧边栏", SettingsData.GetAutostart());
            _chkTray = AddCheck(p0, "显示托盘图标（右键托盘可打开设置）", SettingsData.Flag(_cfg, "show_tray"));
            _chkHover = AddCheck(p0, "悬停图标时弹出详情抽屉", SettingsData.Flag(_cfg, "hover_details"));
            AddTitle(p0, "GLM 角色与额度分档");
            _chkShowDs = AddCheck(p0, "显示 DeepSeek 角色（大肥鱼，余额）", SettingsData.Flag(_cfg, "show_ds"));
            _chkShowZc = AddCheck(p0, "显示 GLM 角色（订阅额度）", SettingsData.Flag(_cfg, "show_zc"));
            _cmbMoodSrc = AddCombo(p0, "GLM 表情由哪个额度驱动", new string[] { "five", "week", "worst" }, _cfg["zc_mood_source"]);
            AddTitle(p0, "面板材质");
            _cmbSkin = AddCombo(p0, "皮肤", new string[] { "黑色", "玻璃", "毛玻璃" }, SkinLabel(_cfg["skin"]));
            _cmbBlurTint = AddCombo(p0, "毛玻璃蒙版浓度", new string[] { "清透", "标准", "深邃" }, VeilLabel(_cfg["blur_veil"]));
            AddTitle(p0, "前台全屏程序（游戏/全屏视频）运行时");
            _cmbFs = AddCombo(p0, "侧边栏行为", new string[] { "自动隐藏", "缩边显示", "保持显示" }, FsLabel(_cfg["fullscreen_action"]));
            AddHint(p0, "five=5小时额度（推荐） / week=本周 / worst=两者取更差");
            AddThresholds(p0, _cfg["warn_low"], _cfg["warn_mid"]);
            AddTitle(p0, "双击角色启动程序");
            _txtLaunchZc = AddText(p0, "双击 GLM 角色启动（留空=不启动）", _cfg["launch_zcode"]);
            _txtLaunchDs = AddText(p0, "双击 DeepSeek 角色启动（留空=不启动）", _cfg["launch_dsh"]);
            AddTitle(p0, "DeepSeek 余额分档");
            _cmbDsWarn = AddCombo(p0, "「紧张」阈值（余额低于此元数）", new string[] { "10", "20", "30", "50" }, _cfg["ds_warn"]);
            _cmbDsDaze = AddCombo(p0, "「痴呆」阈值（余额低于等于此元数）", new string[] { "0.5", "1", "2", "5" }, _cfg["ds_daze"]);

            // ===== 页签 1：数据源 =====
            StackPanel p1 = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Collapsed };
            pages.Children.Add(p1); _tabPages.Add(p1);
            AddTab(bar, "数据源", 1);

            AddLabel(p1, "DeepSeek API Key（platform.deepseek.com 创建）");
            StackPanel keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            _pwdKey = new PasswordBox { Width = 250, FontSize = 12, Background = FieldBrush(), Foreground = Brushes.White, BorderBrush = Ui.Brush(0x44, 0xFF, 0xFF, 0xFF), Padding = new Thickness(6, 4, 6, 4) };
            _pwdKey.Password = _cfg["deepseek_key"];
            _txtKey = new TextBox { Width = 250, FontSize = 12, Visibility = Visibility.Collapsed, Background = FieldBrush(), Foreground = Brushes.White, BorderBrush = Ui.Brush(0x44, 0xFF, 0xFF, 0xFF), Padding = new Thickness(6, 4, 6, 4) };
            _txtKey.Text = _cfg["deepseek_key"];
            keyRow.Children.Add(_pwdKey);
            keyRow.Children.Add(_txtKey);
            _chkShowKey = new CheckBox { Content = "显示", FontSize = 12, Foreground = Ui.Brush(0xCC, 0xFF, 0xFF, 0xFF), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _chkShowKey.Click += delegate
            {
                if (_chkShowKey.IsChecked == true) { _txtKey.Text = _pwdKey.Password; _txtKey.Visibility = Visibility.Visible; _pwdKey.Visibility = Visibility.Collapsed; }
                else { _pwdKey.Password = _txtKey.Text; _pwdKey.Visibility = Visibility.Visible; _txtKey.Visibility = Visibility.Collapsed; }
            };
            keyRow.Children.Add(_chkShowKey);
            p1.Children.Add(keyRow);
            StackPanel dsTestRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Button btnDs = DarkButton("测试连接", delegate
            {
                string key = (_chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password).Trim();
                _testResult.Text = "DeepSeek: 查询中…";
                string baseUrl = _txtDsBase.Text.Trim();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    DsInfo r = DataFeed.FetchDeepSeek(baseUrl.Length > 0 ? baseUrl : "https://api.deepseek.com", key);
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        _testResult.Text = "DeepSeek: " + (r.Ok ? "✓ 余额 " + DataFeed.CurrencySymbol(r.Currency) + r.Balance : "✗ " + r.Error);
                    });
                });
            });
            dsTestRow.Children.Add(btnDs);
            p1.Children.Add(dsTestRow);
            _txtDsBase = AddText(p1, "接口地址（默认 https://api.deepseek.com）", _cfg["deepseek_base"]);

            AddLabel(p1, "ZCode 接口地址（留空=自动跟随本机 ZCode 登录）");
            StackPanel zcRow = new StackPanel { Orientation = Orientation.Horizontal };
            _txtZcBase = new TextBox
            {
                Width = 250, FontSize = 12, Background = FieldBrush(), Foreground = Brushes.White,
                BorderBrush = Ui.Brush(0x44, 0xFF, 0xFF, 0xFF), Padding = new Thickness(6, 4, 6, 4),
                Text = _cfg["zcode_base"], HorizontalAlignment = HorizontalAlignment.Left,
            };
            zcRow.Children.Add(_txtZcBase);
            Button btnZc = DarkButton("测试连接", delegate
            {
                _testResult.Text = "ZCode: 查询中…";
                Dictionary<string, string> c = SettingsData.Load();
                c["zcode_base"] = _txtZcBase.Text.Trim();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    ZcInfo r = DataFeed.FetchZcode(c);
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        _testResult.Text = "ZCode: " + (r.Ok
                            ? "✓ " + r.Level + " · 5h " + (r.Five.RemainFrac * 100).ToString("0") + "% · 周 " + (r.Week.RemainFrac * 100).ToString("0") + "%"
                            : "✗ " + r.Error);
                    });
                });
            });
            btnZc.Margin = new Thickness(8, 0, 0, 0);
            zcRow.Children.Add(btnZc);
            p1.Children.Add(zcRow);

            _cmbRefresh = AddCombo(p1, "数据刷新间隔（余额与额度）", new string[] { "1", "2", "5", "10", "15", "30" }, _cfg["refresh_minutes"], " 分钟");

            // ===== 页签 2：关于 =====
            StackPanel p2 = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Collapsed };
            pages.Children.Add(p2); _tabPages.Add(p2);
            AddTab(bar, "关于", 2);
            TextBlock about = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0x99, 0xFF, 0xFF, 0xFF),
                Text = "Agent HUD v2.3 · HDR 开关 + DeepSeek 余额 + GLM 订阅用量",
            };
            p2.Children.Add(about);
            Button repo = DarkButton("GitHub 仓库", delegate { Ui.OpenUrl("https://github.com/soberkeen/agent-hud"); });
            repo.HorizontalAlignment = HorizontalAlignment.Left;
            repo.Margin = new Thickness(0, 8, 0, 0);
            p2.Children.Add(repo);

            // ---- 测试结果（常驻底部） ----
            _testResult = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0xCC, 0x7C, 0xFC, 0x9E),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 2, 4),
                Text = "",
                MaxHeight = 34,
            };
            Grid.SetRow(_testResult, 2);
            root.Children.Add(_testResult);

            // ---- 保存/取消 ----
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(btns, 3);
            Button save = DarkButton("保存", delegate { SaveAndClose(); });
            save.Background = Ui.Brush(0xFF, 0x17, 0x7B, 0x4D);
            save.BorderBrush = Ui.Brush(0xFF, 0x22, 0x99, 0x66);
            Button cancel = DarkButton("取消", delegate { _saved = true; _host.ApplyPreview(_initialCfg); Close(); });
            cancel.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(save);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            Closed += delegate
            {
                if (!_saved) _host.ApplyPreview(_initialCfg);   // 未保存关闭：还原预览改动
            };

            SwitchTab(0);

            // 实时预览挂接：选择类控件一变就套用
            foreach (ComboBox cb in new ComboBox[] { _cmbRefresh, _cmbLow, _cmbMid, _cmbMoodSrc, _cmbFs, _cmbSkin, _cmbBlurTint, _cmbDsWarn, _cmbDsDaze })
                cb.SelectionChanged += delegate { LivePreview(); };
            foreach (CheckBox ck in new CheckBox[] { _chkTray, _chkHover, _chkShowDs, _chkShowZc })
                ck.Click += delegate { LivePreview(); };
            _txtLaunchZc.TextChanged += delegate { LivePreview(); };
            _txtLaunchDs.TextChanged += delegate { LivePreview(); };
        }

        // 页签：手动实现的按钮 + 页面切换（比系统 TabControl 好做暗色样式）
        private void AddTab(StackPanel bar, string label, int index)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(14, 6, 14, 7),
                Background = Ui.Brush(0xFF, 0x2A, 0x2A, 0x2A),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            TextBlock t = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0xAA, 0xAA, 0xAA),
            };
            b.Child = t;
            int i = index;
            b.MouseLeftButtonDown += delegate { SwitchTab(i); };
            bar.Children.Add(b);
            _tabBtns.Add(b);
        }

        private void SwitchTab(int sel)
        {
            for (int k = 0; k < _tabBtns.Count && k < _tabPages.Count; k++)
            {
                bool on = k == sel;
                _tabBtns[k].Background = on ? Ui.Brush(0xFF, 0x1F, 0x6E, 0x47) : Ui.Brush(0xFF, 0x2A, 0x2A, 0x2A);
                ((TextBlock)_tabBtns[k].Child).Foreground = on ? Brushes.White : Ui.Brush(0xFF, 0xAA, 0xAA, 0xAA);
                _tabPages[k].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private BitmapSource TryIcon()
        {
            try
            {
                using (SD.Icon ic = SD.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location))
                {
                    BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    return bs;
                }
            }
            catch { return null; }
        }

        private Brush FieldBrush() { return Ui.Brush(0xFF, 0x28, 0x28, 0x28); }

        private StackPanel Section(StackPanel root, string title)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            AddTitle(sp, title);
            root.Children.Add(sp);
            return sp;
        }

        private void AddTitle(StackPanel sp, string title)
        {
            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0x7C, 0xFC, 0x9E),
                Margin = new Thickness(0, 0, 0, 6),
            };
            sp.Children.Add(t);
        }

        private CheckBox AddCheck(StackPanel sp, string label, bool isChecked)
        {
            CheckBox c = new CheckBox
            {
                Content = label,
                FontSize = 12.5,
                Foreground = Ui.Brush(0xEE, 0xFF, 0xFF, 0xFF),
                IsChecked = isChecked,
                Margin = new Thickness(0, 3, 0, 3),
                Cursor = Cursors.Hand,
            };
            sp.Children.Add(c);
            return c;
        }

        private void AddLabel(StackPanel sp, string text)
        {
            TextBlock t = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Ui.Brush(0xAA, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 6, 0, 3),
            };
            sp.Children.Add(t);
        }

        private void AddHint(StackPanel sp, string text)
        {
            TextBlock t = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = Ui.Brush(0x77, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 2, 0, 0),
            };
            sp.Children.Add(t);
        }

        private ComboBox AddCombo(StackPanel sp, string label, string[] options, string current, string suffix)
        {
            AddLabel(sp, label);
            ComboBox cb = new ComboBox
            {
                FontSize = 12,
                Width = 130,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = FieldBrush(),
            };
            foreach (string o in options) cb.Items.Add(o + (suffix != null ? suffix : ""));
            int idx = Array.IndexOf(options, current);
            cb.SelectedIndex = idx >= 0 ? idx : 0;
            sp.Children.Add(cb);
            return cb;
        }

        private ComboBox AddCombo(StackPanel sp, string label, string[] options, string current)
        {
            return AddCombo(sp, label, options, current, null);
        }

        private TextBox AddText(StackPanel sp, string label, string current)
        {
            AddLabel(sp, label);
            TextBox tb = new TextBox
            {
                FontSize = 12,
                Background = FieldBrush(),
                Foreground = Brushes.White,
                BorderBrush = Ui.Brush(0x44, 0xFF, 0xFF, 0xFF),
                Padding = new Thickness(6, 4, 6, 4),
                Text = current,
            };
            sp.Children.Add(tb);
            return tb;
        }

        private Button DarkButton(string label, RoutedEventHandler onClick)
        {
            Button b = new Button
            {
                Content = label,
                FontSize = 12.5,
                Cursor = Cursors.Hand,
                Padding = new Thickness(14, 5, 14, 6),
                Background = Ui.Brush(0xFF, 0x33, 0x33, 0x33),
                Foreground = Brushes.White,
                BorderBrush = Ui.Brush(0x44, 0xFF, 0xFF, 0xFF),
            };
            b.Click += onClick;
            return b;
        }

        private void AddThresholds(StackPanel sp, string low, string mid)
        {
            AddLabel(sp, "额度变色阈值（剩余百分比：低于红色档显红、低于黄色档显黄）");
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "见底", FontSize = 12, Foreground = Ui.Brush(0xCC, 0xFF, 0x8B, 0x8B), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _cmbLow = new ComboBox { FontSize = 12, Width = 70, Background = FieldBrush() };
            foreach (string o in new string[] { "5", "10", "15", "20", "25", "30" }) _cmbLow.Items.Add(o);
            _cmbLow.SelectedIndex = Math.Max(0, Array.IndexOf(new string[] { "5", "10", "15", "20", "25", "30" }, low));
            row.Children.Add(_cmbLow);
            row.Children.Add(new TextBlock { Text = "  中等", FontSize = 12, Foreground = Ui.Brush(0xCC, 0xFF, 0xD2, 0x4A), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _cmbMid = new ComboBox { FontSize = 12, Width = 70, Background = FieldBrush() };
            foreach (string o in new string[] { "30", "40", "50", "60", "70" }) _cmbMid.Items.Add(o);
            _cmbMid.SelectedIndex = Math.Max(0, Array.IndexOf(new string[] { "30", "40", "50", "60", "70" }, mid));
            row.Children.Add(_cmbMid);
            sp.Children.Add(row);
        }

        private string VeilLabel(string v)
        {
            if (v == "18") return "清透";
            if (v == "55") return "深邃";
            return "标准";
        }

        private string SkinLabel(string v)
        {
            if (v == "glass") return "玻璃";
            if (v == "blur") return "毛玻璃";
            return "黑色";
        }

        private Dictionary<string, string> CollectUi()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);
            d["deepseek_key"] = _chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password;
            d["deepseek_base"] = string.IsNullOrWhiteSpace(_txtDsBase.Text) ? "https://api.deepseek.com" : _txtDsBase.Text.Trim();
            d["zcode_base"] = _txtZcBase.Text.Trim();
            d["refresh_minutes"] = ComboValue(_cmbRefresh, " 分钟");
            d["warn_low"] = ComboValue(_cmbLow, null);
            d["warn_mid"] = ComboValue(_cmbMid, null);
            d["ds_warn"] = ComboValue(_cmbDsWarn, null);
            d["ds_daze"] = ComboValue(_cmbDsDaze, null);
            d["hover_details"] = _chkHover.IsChecked == true ? "true" : "false";
            d["show_tray"] = _chkTray.IsChecked == true ? "true" : "false";
            d["show_ds"] = _chkShowDs.IsChecked == true ? "true" : "false";
            d["show_zc"] = _chkShowZc.IsChecked == true ? "true" : "false";
            d["launch_zcode"] = _txtLaunchZc.Text.Trim();
            d["launch_dsh"] = _txtLaunchDs.Text.Trim();
            d["zc_mood_source"] = (string)_cmbMoodSrc.SelectedItem ?? "five";
            d["fullscreen_action"] = new string[] { "hide", "tuck", "none" }[Math.Max(0, _cmbFs.SelectedIndex)];
            d["skin"] = new string[] { "dark", "glass", "blur" }[Math.Max(0, _cmbSkin.SelectedIndex)];
            d["blur_veil"] = new string[] { "18", "30", "55" }[Math.Max(0, _cmbBlurTint.SelectedIndex)];
            return d;
        }

        private void LivePreview()
        {
            try
            {
                Dictionary<string, string> d = CollectUi();
                if (d["show_ds"] != "true" && d["show_zc"] != "true") return;   // 全关非法，不预览
                _host.ApplyPreview(d);
            }
            catch { }
        }

        private string FsLabel(string v)
        {
            if (v == "tuck") return "缩边显示";
            if (v == "none") return "保持显示";
            return "自动隐藏";
        }

        private string ComboValue(ComboBox cb, string suffix)
        {
            string s = cb.SelectedItem as string;
            if (s == null) return "";
            return suffix != null ? s.Replace(suffix, "") : s;
        }

        private void SaveAndClose()
        {
            if (_chkShowDs.IsChecked != true && _chkShowZc.IsChecked != true)
            {
                _testResult.Text = "至少保留一个角色，否则侧边栏只剩 HDR 开关。";
                _testResult.Foreground = Ui.Brush(0xFF, 0xFF, 0xB0, 0xB0);
                return;
            }
            Dictionary<string, string> d = CollectUi();
            SecretStore.Set(d["deepseek_key"].Trim());
            d["deepseek_key"] = SecretStore.Get();
            string newSkin = d["skin"];
            if (newSkin != _cfg["skin"] &&
                (Skins.IsAcrylic(newSkin) != Skins.IsAcrylic(_cfg["skin"])))
                System.Windows.MessageBox.Show("面板材质已保存。毛玻璃 ⇄ 黑色/玻璃 切换需重启侧边栏后生效。",
                    "Agent HUD", MessageBoxButton.OK, MessageBoxImage.Information);
            SettingsData.Save(d);
            SettingsData.SetAutostart(_chkAutostart.IsChecked == true);
            _saved = true;
            _host.ApplyConfigChanged();
            Close();
        }
    }

    // ---------- 真毛玻璃：SetWindowCompositionAttribute + ACRYLICBLURBEHIND ----------
    internal static class AccentGlass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;      // 4 = ACRYLICBLURBEHIND
            public int AccentFlags;
            public uint GradientColor;   // AABBGGRR：模糊后的色调叠加
            public int AnimationId;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WCAData
        {
            public int Attribute;        // 19 = WCA_ACCENT_POLICY
            public IntPtr Data;
            public int SizeOfData;
        }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WCAData data);

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ACRYLIC = 4;

        // tint = #AARRGGBB
        public static void Apply(IntPtr hwnd, string tint)
        {
            uint abgr = 0xC8141E26;   // 默认暗磨砂 #C8141E26
            try
            {
                if (!string.IsNullOrEmpty(tint) && tint.Length == 8)
                {
                    uint a = Convert.ToUInt32(tint.Substring(0, 2), 16);
                    uint r = Convert.ToUInt32(tint.Substring(2, 2), 16);
                    uint g = Convert.ToUInt32(tint.Substring(4, 2), 16);
                    uint b = Convert.ToUInt32(tint.Substring(6, 2), 16);
                    abgr = (a << 24) | (b << 16) | (g << 8) | r;
                }
            }
            catch { }
            AccentPolicy pol = new AccentPolicy();
            pol.AccentState = ACCENT_ACRYLIC;
            pol.AccentFlags = 2;
            pol.GradientColor = abgr;
            pol.AnimationId = 0;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AccentPolicy)));
            try
            {
                Marshal.StructureToPtr(pol, ptr, false);
                WCAData d = new WCAData();
                d.Attribute = WCA_ACCENT_POLICY;
                d.Data = ptr;
                d.SizeOfData = Marshal.SizeOf(typeof(AccentPolicy));
                SetWindowCompositionAttribute(hwnd, ref d);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }

    // ---------- Win11 系统级 Acrylic 背景（DWMWA_SYSTEMBACKDROP_TYPE，22H2+） ----------
    internal static class DwmBackdrop
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int L, R, T, B; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // Win11 22H2 = build 22621
        public static bool Supported
        {
            get
            {
                try { return Environment.OSVersion.Version.Build >= 22621; }
                catch { return false; }
            }
        }

        public static void Apply(IntPtr hwnd, bool enable, bool darkMode)
        {
            int dark = darkMode ? 1 : 0, acrylic = enable ? 3 : 1, round = 2;
            MARGINS m = new MARGINS(); m.L = -1; m.R = -1; m.T = -1; m.B = -1;
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        }
    }

    // ---------- 面板皮肤 ----------
    internal static class Skins
    {
        // dark=分层窗口实心；glass/blur=DWM Acrylic 上的浅色调板
        public static Brush PanelBackground(string skin)
        {
            if (skin == "glass") return Ui.Brush(0x38, 0xFF, 0xFF, 0xFF);          // 纯透明：低透明白纱，壁纸直透
            if (skin == "blur") return Ui.Brush(0x66, 0x14, 0x18, 0x20);           // acrylic 上的深纱
            return Ui.Brush(0xE6, 0x20, 0x20, 0x20);                               // dark
        }

        public static Brush PanelBorder(string skin)
        {
            if (skin == "glass") return Ui.Brush(0x55, 0xFF, 0xFF, 0xFF);
            if (skin == "blur") return Ui.Brush(0x66, 0xFF, 0xFF, 0xFF);
            return Ui.Brush(0x33, 0xFF, 0xFF, 0xFF);
        }

        public static bool IsAcrylic(string skin) { return skin == "blur"; }   // 玻璃=纯透明（分层），毛玻璃=Acrylic 模糊

        // 玻璃高光斜纹（顶部一道渐变提亮，模拟玻璃反光）
        public static Border GlossOverlay(string skin)
        {
            if (skin != "glass" && skin != "blur") return null;
            LinearGradientBrush lg = new LinearGradientBrush(
                Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90);
            lg.StartPoint = new Point(0, 0);
            lg.EndPoint = new Point(0, 1);
            Border g = new Border
            {
                CornerRadius = new CornerRadius(16, 16, 8, 8),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Top,
                Height = 60,
                Background = lg,
            };
            return g;
        }
    }

    // ---------- 侧边栏（v1.4：原位抽屉展开 + HDR 双击确认） ----------

    public class Widget : Window
    {
        private const double Sliver = 12;   // 半隐藏时保留可见的宽度
        private const double SnapPx = 28;   // 距边缘多少像素内判定为吸附
        private const double DsDrawerH = 40;   // DeepSeek 抽屉展开高（纯余额一行）
        private const double ZcDrawerH = 54;   // ZCode 抽屉展开高（34 圈+标签）
        private const double ClosedH = 254;    // 抽屉全收起（双角色）的窗口高度
        private const double RowH = 76;        // 单个角色行高（隐藏角色时从 ClosedH 扣除）
        private double _windowH = ClosedH;     // 当前窗口高度基准值（动画起点/终点计算用）

        // 当前配置下的收起高度（隐藏角色会减行高；分隔线 7px 在行内已含，单角色减 1 条 mid sep≈7）
        private double ClosedHeightNow()
        {
            double h = ClosedH;
            bool ds = SettingsData.Flag(_cfg, "show_ds"), zc = SettingsData.Flag(_cfg, "show_zc");
            if (!ds) h -= RowH;
            if (!zc) h -= RowH;
            if (!(ds && zc)) h -= 7;
            return h;
        }

        private void UpdateHeightNow()
        {
            double target = ClosedHeightNow() + (_openDrawer == "ds" ? DsDrawerH : (_openDrawer == "zc" ? ZcDrawerH : 0));
            BeginAnimation(Window.HeightProperty, null);
            Height = target;
            _windowH = target;
        }

        private static readonly string PosFile = Path.Combine(SettingsData.ExeDir, "agent-hud.pos");

        // HDR 圆钮（渐变圆角方块 + 状态发光 + 缩放反馈 + 双击确认）
        private readonly Border _hdrBtn;
        private readonly TextBlock _label;
        private readonly SolidColorBrush _hdrFill;        // 非冻结，做颜色渐变动画
        private readonly DropShadowEffect _hdrGlow;
        private readonly ScaleTransform _hdrScale;
        private bool _hdrArmed;                            // 已单击待确认
        private readonly DispatcherTimer _armTimer;

        private readonly Grid _root;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _tuckTimer;
        private readonly DispatcherTimer _dataTimer;
        private readonly DispatcherTimer _panelHideTimer;

        // 图标行 + 抽屉
        private Ellipse _dsDot, _zcFiveDot, _zcWeekDot;
        private Grid _dsRow, _zcRow;
        private Border _sepTop, _sepMid;
        private FramePlayer _dfPlayer, _zcPlayer;
        private System.Diagnostics.Stopwatch _animClock;
        private int _animIdx = -1;
        private Border _panel;
        private Border _gloss;
        private Grid _panelGrid;
        private bool _acrylicMode;
        private bool _acrylicApplied;
        private bool _dfFrozen, _zcFrozen;   // 数据异常时定格，不瞎动
        private int _lastZcMood = -1;        // 额度分档迟滞用，防阈值附近反复横跳

        private Grid _dsDrawer, _zcDrawer;
        private StackPanel _dsDrawerContent, _zcDrawerContent;
        private TranslateTransform _dsSlide, _zcSlide;
        private TextBlock _dsBalanceText;
        private Ui.RingParts _zcFiveRing, _zcWeekRing;

        private WF.NotifyIcon _tray;
        private SettingsWindow _settings;

        private Dictionary<string, string> _cfg;
        private DsInfo _ds;
        private ZcInfo _zc;
        private DateTime _lastFetch = DateTime.MinValue;

        private string _pinnedDrawer;    // null | "ds" | "zc"（单击固定）
        private string _hoverDrawer;
        private string _openDrawer;
        private string _pinPending;      // 单击延迟固定：等待双击消歧
        private DispatcherTimer _pinTimer;
        private bool _fsHidden;          // 前台全屏程序运行中，已自动让位
        private bool _fsUsedHide;        // 本次让位方式：true=整体隐藏 / false=缩边
        private bool _userHidden;        // 用户经托盘主动隐藏（全屏恢复时不打扰）
        private DispatcherTimer _fsTimer;

        private string _dock = "";       // "" | left | right | top | bottom
        private bool _tucked;
        private int _lastState = -99;
        private DateTime _downAt;

        public string Dock { get { return _dock; } }
        public Dictionary<string, string> CurrentConfig { get { return _cfg; } }
        public DsInfo LatestDs { get { return _ds; } }
        public ZcInfo LatestZc { get { return _zc; } }

        public Widget(EventWaitHandle showEvent)
        {
            Title = "Agent HUD";
            Width = 104; Height = ClosedH;   // 高度跟随抽屉开合伸缩，不留透明空底
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;   // 禁用系统贴边分屏放大（Aero Snap）
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;

            _cfg = SettingsData.Load();
            string startSkin;
            try { startSkin = _cfg["skin"]; } catch { startSkin = "dark"; }
            bool acrylic = Skins.IsAcrylic(startSkin) && DwmBackdrop.Supported;
            _acrylicMode = acrylic;
            if (acrylic)
            {
                AllowsTransparency = false;   // 系统背景要求非分层窗口
                var chrome = new System.Windows.Shell.WindowChrome
                {
                    GlassFrameThickness = new Thickness(-1),   // 背景延伸至整个窗口
                    CaptionHeight = 0,
                    UseAeroCaptionButtons = false,
                    CornerRadius = new CornerRadius(0),
                };
                System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
            }

            _panel = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top,   // 抽屉收起时面板贴顶，底部保持透明
            };
            StackPanel stack = new StackPanel();
            Grid panelGrid = new Grid();          // gloss 垫底，stack 在上
            _panel.Child = panelGrid;
            panelGrid.Children.Add(stack);
            _panelGrid = panelGrid;
            ApplySkin(SettingsData.Load());       // 需在 _panelGrid 就绪后调用（会插高光层）
            _root = new Grid { Cursor = Cursors.Hand };
            _root.Children.Add(_panel);
            Content = _root;

            // ---- HDR 圆钮（美化 + 双击确认防误触） ----
            Grid hdrArea = new Grid { Height = 76, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _hdrScale = new ScaleTransform(1, 1);
            _hdrFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x35, 0x35, 0x35));
            _hdrGlow = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.25 };
            _hdrBtn = new Border
            {
                Width = 68, Height = 68,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(19),
                Background = _hdrFill,
                BorderBrush = Ui.Brush(0x55, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(1),
                Effect = _hdrGlow,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _hdrScale,
                IsHitTestVisible = false,
            };
            _label = new TextBlock
            {
                Text = "HDR",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0xEE, 0xEE, 0xEE),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            hdrArea.Children.Add(_hdrBtn);
            hdrArea.Children.Add(_label);
            hdrArea.MouseEnter += delegate { AnimateScale(1.07); };
            hdrArea.MouseLeave += delegate { AnimateScale(1.0); };
            hdrArea.MouseLeftButtonDown += delegate { OnHdrDown(); };
            stack.Children.Add(hdrArea);

            _sepTop = Sep();
            stack.Children.Add(_sepTop);

            // ---- DeepSeek 行 + 抽屉（show_ds=false 则整行不创建） ----
            bool showDs = SettingsData.Flag(_cfg, "show_ds");
            _dsRow = IconRow("ds");
            _dfPlayer = new FramePlayer("anim-df-", 74, "Icon.Dafeiyu.png");
            _dsDot = StatusDot(10);
            _dsDot.Margin = new Thickness(0, 0, 8, 4);
            _dsDot.HorizontalAlignment = HorizontalAlignment.Right;
            _dsDot.VerticalAlignment = VerticalAlignment.Bottom;
            _dsRow.Children.Add(_dfPlayer.Visual);
            _dsRow.Children.Add(_dsDot);
            _dsRow.ToolTip = "DeepSeek：悬停展开余额 · 单击固定 · 双击启动 DSH";
            stack.Children.Add(_dsRow);
            if (!showDs) _dsRow.Visibility = Visibility.Collapsed;
            _dsSlide = new TranslateTransform();
            _dsDrawerContent = new StackPanel { RenderTransform = _dsSlide, Opacity = 0 };
            _dsBalanceText = new TextBlock
            {
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0xFF, 0xFF, 0xFF),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                IsHitTestVisible = false,
            };
            _dsDrawerContent.Children.Add(_dsBalanceText);
            _dsDrawer = new Grid { Height = 0, ClipToBounds = true, Background = Brushes.Transparent };
            _dsDrawer.Children.Add(_dsDrawerContent);
            _dsDrawer.MouseEnter += delegate { CancelPanelHide(); };
            _dsDrawer.MouseLeave += delegate { SchedulePanelHide(); };
            stack.Children.Add(_dsDrawer);

            _sepMid = Sep();
            stack.Children.Add(_sepMid);

            // ---- ZCode 行 + 抽屉（show_zc=false 则整行不创建） ----
            bool showZc = SettingsData.Flag(_cfg, "show_zc");
            _zcRow = IconRow("zc");
            _zcPlayer = new FramePlayer("anim-zc-", 74, "Icon.Zcode.png");
            _zcFiveDot = StatusDot(9);
            _zcWeekDot = StatusDot(9);
            _zcFiveDot.ToolTip = "5 小时额度";
            _zcWeekDot.ToolTip = "本周额度";
            StackPanel zcDots = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 7, 3),
            };
            zcDots.Children.Add(_zcFiveDot);
            zcDots.Children.Add(new Border { Height = 2 });
            zcDots.Children.Add(_zcWeekDot);
            _zcRow.Children.Add(_zcPlayer.Visual);
            _zcRow.Children.Add(zcDots);
            _zcRow.ToolTip = "GLM 订阅：悬停展开额度 · 单击固定 · 双击启动 ZCode";
            stack.Children.Add(_zcRow);
            if (!showZc) _zcRow.Visibility = Visibility.Collapsed;

            _zcSlide = new TranslateTransform();
            _zcDrawerContent = new StackPanel
            {
                RenderTransform = _zcSlide,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            StackPanel rings = new StackPanel { Orientation = Orientation.Horizontal };
            _zcFiveRing = Ui.MakeRing(34, 3.5, 10);
            _zcWeekRing = Ui.MakeRing(34, 3.5, 10);
            rings.Children.Add(RingMini(_zcFiveRing, "5时"));
            rings.Children.Add(new Border { Width = 5 });
            rings.Children.Add(RingMini(_zcWeekRing, "本周"));
            _zcDrawerContent.Children.Add(rings);
            _zcDrawer = new Grid { Height = 0, ClipToBounds = true, Background = Brushes.Transparent };
            _zcDrawer.Children.Add(_zcDrawerContent);
            _zcDrawer.MouseEnter += delegate { CancelPanelHide(); };
            _zcDrawer.MouseLeave += delegate { SchedulePanelHide(); };
            stack.Children.Add(_zcDrawer);

            Border pad = new Border { Height = 6, IsHitTestVisible = false };
            stack.Children.Add(pad);

            _pinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _pinTimer.Tick += delegate
            {
                _pinTimer.Stop();
                if (_pinPending != null && _pinnedDrawer == null) { string sec = _pinPending; _pinPending = null; TogglePin(sec); }
            };
            _panelHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _panelHideTimer.Tick += delegate
            {
                _panelHideTimer.Stop();
                if (_pinnedDrawer == null && _hoverDrawer == null && _openDrawer != null) AnimateDrawer(_openDrawer, false);
            };

            _armTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _armTimer.Tick += delegate { DisarmHdr(); };   // 超时未确认自动还原，误触零代价

            ContextMenu menu = new ContextMenu();
            MenuItem miSettings = new MenuItem { Header = "设置…" };
            miSettings.Click += delegate { OpenSettings(); };
            menu.Items.Add(miSettings);
            MenuItem miIni = new MenuItem { Header = "数据配置" };
            miIni.Click += delegate { OpenIni(); };
            menu.Items.Add(miIni);
            MenuItem exit = new MenuItem { Header = "退出" };
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            ContextMenu = menu;

            try   // 恢复上次位置（含贴边半隐藏坐标）
            {
                if (File.Exists(PosFile))
                {
                    string[] p = File.ReadAllText(PosFile).Trim().Split(',');
                    Left = double.Parse(p[0]);
                    Top = double.Parse(p[1]);
                }
            }
            catch { }

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pollTimer.Tick += delegate { UpdateVisual(false); };

            _tuckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _tuckTimer.Tick += delegate
            {
                _tuckTimer.Stop();
                if (_dock.Length > 0 && !_tucked && !_root.IsMouseOver) SetDockState(_dock, true, true);
            };

            _dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _dataTimer.Tick += delegate
            {
                if (MockCycle) return;
                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();
            };

            MouseEnter += delegate
            {
                if (_dock.Length > 0)
                {
                    _tuckTimer.Stop();
                    if (_tucked) SetDockState(_dock, false, true);   // 悬停弹出
                }
                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if (!MockCycle && (DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();   // 弹出时顺带补数据
            };
            MouseLeave += delegate
            {
                if (_dock.Length > 0 && !_tucked) { _tuckTimer.Stop(); _tuckTimer.Start(); }   // 移开延迟缩回
            };
            StateChanged += delegate
            {
                if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;   // 兜底弹回
            };
            Closed += delegate
            {
                _pollTimer.Stop();
                _dataTimer.Stop();
                _armTimer.Stop();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                try { File.WriteAllText(PosFile, ((int)Left).ToString() + "," + ((int)Top).ToString()); } catch { }
                Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };

            SourceInitialized += delegate
            {
                try
                {
                    if (_acrylicMode)
                    {
                        string sk;
                        try { sk = _cfg["skin"]; } catch { sk = "glass"; }
                        IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        // Win11 24H2 (build 26100) 移除了 undocumented Accent Acrylic——
                        // 忽略它窗口会渲染成黑底。24H2+ 一律用系统 Acrylic，旧系统才走 Accent。
                        bool legacyAccent = Environment.OSVersion.Version.Build < 26100;
                        if (sk == "blur" && legacyAccent)
                        {
                            DwmBackdrop.Apply(h, false, true);
                            AccentGlass.Apply(h, _cfg.ContainsKey("blur_tint") ? _cfg["blur_tint"] : "C8141E26");
                        }
                        else
                        {
                            DwmBackdrop.Apply(h, true, sk == "blur" ? false : true);   // blur=亮色 Acrylic
                        }
                        _acrylicApplied = true;
                    }
                }
                catch { }
            };

            SetupTray();
            PromoteTrayIcon();

            if (showEvent != null)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        while (showEvent.WaitOne())
                        {
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                if (Visibility != Visibility.Visible) ToggleVisible();
                                else { Show(); WindowState = WindowState.Normal; }   // 已显示则带回前台
                            });
                        }
                    }
                    catch { }
                });
            }
        }

        // Win11 默认把新托盘图标收进隐藏区：写 IsPromoted 直接钉在任务栏上
        private void PromoteTrayIcon()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Control Panel\\NotifyIconSettings", true))
                {
                    if (k == null) return;
                    foreach (string name in k.GetSubKeyNames())
                    {
                        using (Microsoft.Win32.RegistryKey sub = k.OpenSubKey(name, true))
                        {
                            object v = sub == null ? null : sub.GetValue("ExecutablePath");
                            if (v is string && string.Equals((string)v, exe, StringComparison.OrdinalIgnoreCase))
                            {
                                sub.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void Start()
        {
            UpdateHeightNow();
            UpdateVisual(false);
            if (MockCycle)
            {
                _mockMood = 0;
                ApplyMock();
                _mockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                _mockTimer.Tick += delegate { _mockMood = (_mockMood + 1) % 3; ApplyMock(); };
                _mockTimer.Start();
            }
            else RefreshData();
            // 前台全屏检测：窗口化全屏游戏/全屏视频铺满屏幕时按设置让位（系统只对真·独占全屏自动让位，无边框窗口需要自己检测）
            _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _fsTimer.Tick += delegate { CheckForegroundFullscreen(); };
            _fsTimer.Start();

            // 粘性置顶：浏览器等窗口带残留置顶标志/前台提升时会盖住侧边栏，
            // 每 2 秒重新声明一次 TOPMOST（不移动不激活），被盖后最多 2 秒自动浮回
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            DispatcherTimer sticky = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            sticky.Tick += delegate
            {
                try
                {
                    IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                }
                catch { }
            };
            sticky.Start();

            // 帧动画：15ms 轮询秒表，帧序号按绝对时间换算（30fps 素材恰好每 33.3ms 一帧）
            // 注意不能用 CompositionTarget.Rendering——窗口静止时 WPF 不渲染，事件不触发，动画会冻结
            _animClock = System.Diagnostics.Stopwatch.StartNew();
            DispatcherTimer animClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            animClock.Tick += delegate
            {
                int t = (int)(_animClock.Elapsed.TotalMilliseconds);
                if (!_dfFrozen) _dfPlayer.Advance(t);   // Advance 内部按各自动画的循环时长取帧
                if (!_zcFrozen) _zcPlayer.Advance(t);

            };
            animClock.Start();
            _pollTimer.Start();
            UpdateDock(false);   // 上次关闭时若贴边，启动即恢复半隐藏
            RefreshData();
            _dataTimer.Start();
        }

        // ---------- 托盘 ----------
        private void SetupTray()
        {
            try
            {
                _tray = new WF.NotifyIcon();
                _tray.Icon = SD.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                _tray.Text = "Agent HUD · HDR 开关 + DeepSeek + GLM";
                _tray.Visible = SettingsData.Flag(_cfg, "show_tray");

                WF.ContextMenuStrip menu = new WF.ContextMenuStrip();
                menu.Items.Add("显示 / 隐藏侧边栏", null, (EventHandler)delegate { ToggleVisible(); });
                menu.Items.Add("设置…", null, (EventHandler)delegate { OpenSettings(); });
                menu.Items.Add("数据配置", null, (EventHandler)delegate { OpenIni(); });
                menu.Items.Add(new WF.ToolStripSeparator());
                menu.Items.Add("退出", null, (EventHandler)delegate { Close(); });
                _tray.ContextMenuStrip = menu;
                _tray.MouseUp += delegate(object sender, WF.MouseEventArgs e)
                {
                    // 左键只把窗口亮出来（含带回前台）；藏窗口走右键菜单，防止误点后找不到
                    if (e.Button != WF.MouseButtons.Left) return;
                    if (Visibility != Visibility.Visible) ToggleVisible();
                    else { Show(); WindowState = WindowState.Normal; }
                };
            }
            catch { _tray = null; }
        }

        private void ToggleVisible()
        {
            if (Visibility == Visibility.Visible) { CloseDrawersAndUnpin(); _userHidden = true; Visibility = Visibility.Hidden; }
            else { _userHidden = false; Visibility = Visibility.Visible; UpdateDock(false); }
        }

        public void OpenIni()
        {
            try
            {
                if (!File.Exists(SettingsData.IniPath)) SettingsData.Save(SettingsData.Load());
                System.Diagnostics.Process.Start("notepad.exe", SettingsData.IniPath);
            }
            catch { }
        }

        public void OpenSettings()
        {
            if (_settings != null && _settings.IsLoaded) { _settings.Activate(); return; }
            _settings = new SettingsWindow(this);
            _settings.Show();
        }

        // 应用面板皮肤（设置保存时即时切换）
        public void ApplySkin(Dictionary<string, string> cfg)
        {
            string skin;
            try { skin = cfg["skin"]; } catch { skin = "dark"; }
            _panel.Background = Skins.PanelBackground(skin);
            _panel.BorderBrush = Skins.PanelBorder(skin);
            if (Skins.IsAcrylic(skin) && DwmBackdrop.Supported)
            {
                // acrylic 模式：面板充满窗口，圆角交给 DWM（8px），材质由系统合成
                _panel.CornerRadius = new CornerRadius(8);
                _panel.Margin = new Thickness(0);
                _panel.VerticalAlignment = VerticalAlignment.Stretch;
                if (skin == "blur")
                {
                    // 毛玻璃：亮色 Acrylic 之上盖一层可调浓度的白纱（清透=几乎全透）
                    byte va;
                    try { va = Convert.ToByte(cfg["blur_veil"], 16); }
                    catch { va = 0x30; }
                    _panel.Background = Ui.Brush(va, 0xFA, 0xFB, 0xFE);
                }
            }
            else
            {
                _panel.CornerRadius = new CornerRadius(16);
                _panel.Margin = new Thickness(0);
                _panel.VerticalAlignment = VerticalAlignment.Top;
            }
            // 换肤：先移除旧高光层，再垫底插入新层
            if (_gloss != null && _panelGrid != null && _panelGrid.Children.Contains(_gloss))
                _panelGrid.Children.Remove(_gloss);
            _gloss = Skins.GlossOverlay(skin);
            if (_gloss != null && _panelGrid != null)
            {
                _panelGrid.Children.Insert(0, _gloss);
                System.Windows.Controls.Panel.SetZIndex(_gloss, -1);
            }
        }

        // 设置页实时预览：把改动立即套到 HUD（不落盘）
        public void ApplyPreview(Dictionary<string, string> d)
        {
            _cfg = new Dictionary<string, string>(d);
            ApplySkin(_cfg);
            bool showTray = SettingsData.Flag(_cfg, "show_tray");
            if (_tray != null) _tray.Visible = showTray;
            bool ds = SettingsData.Flag(_cfg, "show_ds"), zc = SettingsData.Flag(_cfg, "show_zc");
            if (_dsRow != null) _dsRow.Visibility = ds ? Visibility.Visible : Visibility.Collapsed;
            if (_zcRow != null) _zcRow.Visibility = zc ? Visibility.Visible : Visibility.Collapsed;
            if (_sepMid != null) _sepMid.Visibility = (ds && zc) ? Visibility.Visible : Visibility.Collapsed;
            if (_sepTop != null) _sepTop.Visibility = ds ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeightNow();
            if (_ds != null || _zc != null) ApplyData();   // 分档变了立刻用现有数据重画
        }

        // 设置保存后：重读配置、同步托盘/角色可见性、立刻刷新数据
        public void ApplyConfigChanged()
        {
            _cfg = SettingsData.Load();
            ApplySkin(_cfg);
            if (_tray != null) _tray.Visible = SettingsData.Flag(_cfg, "show_tray");
            if (_dfPlayer != null) _dfPlayer.Reload();
            if (_zcPlayer != null) _zcPlayer.Reload();
            if (_dsRow != null) _dsRow.Visibility = SettingsData.Flag(_cfg, "show_ds") ? Visibility.Visible : Visibility.Collapsed;
            if (_zcRow != null) _zcRow.Visibility = SettingsData.Flag(_cfg, "show_zc") ? Visibility.Visible : Visibility.Collapsed;
            if (_sepMid != null) _sepMid.Visibility = (_dsRow != null && _dsRow.Visibility == Visibility.Visible && _zcRow != null && _zcRow.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            if (_sepTop != null) _sepTop.Visibility = (_dsRow != null && _dsRow.Visibility == Visibility.Visible && SettingsData.Flag(_cfg, "show_ds")) ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeightNow();
            RefreshData();
        }

        // ---------- 抽屉（角色下方原位滑出） ----------
        public void CancelPanelHide() { _panelHideTimer.Stop(); }
        public void SchedulePanelHide()
        {
            _panelHideTimer.Stop();
            _panelHideTimer.Start();
        }

        private Grid IconRow(string sec)
        {
            Grid g = new Grid
            {
                Height = 76,
                Background = Brushes.Transparent,   // 保证空隙也能命中
                Cursor = Cursors.Hand,
            };
            g.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs me) { OnRowDown(sec, me); };
            g.MouseEnter += delegate { HoverDrawer(sec); };
            g.MouseLeave += delegate { LeaveDrawer(sec); };
            return g;
        }

        private void HoverDrawer(string sec)
        {
            if (!SettingsData.Flag(_cfg, "hover_details")) return;
            if (_pinnedDrawer != null) return;          // 固定中不跟随悬停
            _hoverDrawer = sec;
            CancelPanelHide();
            if (_openDrawer != sec) OpenDrawer(sec);
        }

        private void LeaveDrawer(string sec)
        {
            if (_hoverDrawer == sec) _hoverDrawer = null;
            if (_pinnedDrawer == null) SchedulePanelHide();
        }

        private void OnRowDown(string sec, MouseButtonEventArgs e)
        {
            StopPosAnim();
            _downAt = DateTime.Now;
            double preX = Left, preY = Top;
            try { DragMove(); } catch { }
            double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
            if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
            {
                if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏首击只唤醒
                if (e.ClickCount >= 2)
                {
                    // 双击：启动对应程序，取消挂起的单击固定
                    _pinPending = null;
                    _pinTimer.Stop();
                    LaunchFor(sec);
                }
                else if (e.ClickCount == 1)
                {
                    // 单击延迟 350ms 固定：期间出现第二次点击则按双击处理
                    _pinPending = sec;
                    _pinTimer.Stop();
                    _pinTimer.Start();
                }
            }
            else UpdateDock(true);
        }

        private void LaunchFor(string sec)
        {
            try
            {
                string key = sec == "ds" ? "launch_dsh" : "launch_zcode";
                string path = _cfg[key];
                if (path.Length == 0)
                {
                    OpenSettings();   // 未配置 → 引导到设置页
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path.Length > 0 ? path : ".") ?? ".",
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        // ---------- 前台全屏让位 ----------
        private void CheckForegroundFullscreen()
        {
            string action;
            try { action = _cfg["fullscreen_action"]; } catch { action = "hide"; }
            if (action == "none") { RestoreFromFullscreen(); return; }

            bool fs = false;
            try
            {
                IntPtr fg = GetForegroundWindow();
                IntPtr me = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (fg != IntPtr.Zero && fg != me)
                {
                    RECT r;
                    if (GetWindowRect(fg, out r))
                    {
                        var scr = WF.Screen.FromHandle(me);
                        var b = scr.Bounds;
                        fs = r.Left <= b.Left + 8 && r.Top <= b.Top + 8 &&
                             r.Right >= b.Right - 8 && r.Bottom >= b.Bottom - 8;
                    }
                }
            }
            catch { }

            if (fs && !_fsHidden)
            {
                _fsHidden = true;
                _fsUsedHide = action != "tuck";
                CloseDrawersAndUnpin();
                if (_fsUsedHide) Visibility = Visibility.Hidden;
                else SetDockState(_dock.Length > 0 ? _dock : "right", true, true);
            }
            else if (!fs && _fsHidden)
            {
                _fsHidden = false;
                if (_fsUsedHide) Visibility = Visibility.Visible;
                else SetDockState(_dock.Length > 0 ? _dock : "right", false, true);
            }
        }

        private void RestoreFromFullscreen()
        {
            if (!_fsHidden) return;
            _fsHidden = false;
            if (_userHidden) return;             // 用户主动藏的，不替他打开
            if (_fsUsedHide) Visibility = Visibility.Visible;
            else SetDockState(_dock.Length > 0 ? _dock : "right", false, true);
        }

        // 单击固定走 350ms 延迟：给双击启动留出消歧窗口
        private void TogglePinDelayed(string sec)
        {
            _pinPending = sec;
            _pinTimer.Stop();
            _pinTimer.Start();
        }

        private void TogglePin(string sec)
        {
            if (_pinnedDrawer == sec)
            {
                _pinnedDrawer = null;
                AnimateDrawer(sec, false);
            }
            else
            {
                _pinnedDrawer = sec;
                CancelPanelHide();
                OpenDrawer(sec);
            }
        }

        private void OpenDrawer(string sec)
        {
            CancelPanelHide();
            string other = sec == "ds" ? "zc" : "ds";
            if (_openDrawer == other) AnimateDrawer(other, false);   // 互斥：开一个收另一个
            AnimateDrawer(sec, true);
        }

        private void AnimateDrawer(string sec, bool open)
        {
            Grid g = sec == "ds" ? _dsDrawer : _zcDrawer;
            StackPanel content = sec == "ds" ? _dsDrawerContent : _zcDrawerContent;
            TranslateTransform slide = sec == "ds" ? _dsSlide : _zcSlide;
            double target = sec == "ds" ? DsDrawerH : ZcDrawerH;
            Duration dur = new Duration(TimeSpan.FromMilliseconds(180));
            CubicEase ease = new CubicEase(); ease.EasingMode = EasingMode.EaseOut;
            // 从当前值出发（DoubleAnimation 只给 To），中断时无跳变
            DoubleAnimation ah = new DoubleAnimation(open ? target : 0, dur);
            ah.EasingFunction = ease;
            g.BeginAnimation(FrameworkElement.HeightProperty, ah);
            DoubleAnimation ao = new DoubleAnimation(open ? 1 : 0, new Duration(TimeSpan.FromMilliseconds(open ? 200 : 110)));
            content.BeginAnimation(UIElement.OpacityProperty, ao);
            if (open)
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = -10;
                DoubleAnimation asl = new DoubleAnimation(-10, 0, dur);
                asl.EasingFunction = ease;
                slide.BeginAnimation(TranslateTransform.YProperty, asl);
            }
            _openDrawer = open ? sec : (_openDrawer == sec ? null : _openDrawer);
            if (!open && _pinnedDrawer == sec) _pinnedDrawer = null;

            // 窗口高度跟随抽屉伸缩，底部不留透明空底
            double newH = ClosedHeightNow() + (open ? target : 0);
            Duration wdur = new Duration(TimeSpan.FromMilliseconds(180));
            CubicEase wease = new CubicEase(); wease.EasingMode = EasingMode.EaseOut;
            DoubleAnimation aw = new DoubleAnimation(newH, wdur);
            aw.EasingFunction = wease;
            BeginAnimation(Window.HeightProperty, aw);
            if (_dock == "bottom" && Math.Abs(newH - _windowH) > 0.5)
            {
                // 贴底时窗口向下长会压到任务栏：Top 同步上移
                DoubleAnimation at = new DoubleAnimation(Top - (newH - _windowH), wdur);
                at.EasingFunction = wease;
                BeginAnimation(Window.TopProperty, at);
            }
            _windowH = newH;
        }

        private void CloseDrawersAndUnpin()
        {
            _pinnedDrawer = null;
            _hoverDrawer = null;
            if (_openDrawer != null) AnimateDrawer(_openDrawer, false);
        }

        // 布局调试：-popup:ds|zc 启动即固定展开对应抽屉
        private static void Dbg(string msg)
        {
            try { System.IO.File.AppendAllText(Path.Combine(SettingsData.ExeDir, "anim-err.log"), msg + Environment.NewLine); } catch { }
        }

        // 调试：程序化触发 MouseEnter（复现 HoverDrawer→AnimateDrawer 路径）
        public void DebugHover(string section)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)delegate
            {
                Grid row = section == "ds" ? _dsRow : _zcRow;
                try
                {
                    Dbg("DebugHover raise " + section);
                    row.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
                    {
                        RoutedEvent = Mouse.MouseEnterEvent
                    });
                    Dbg("DebugHover raised ok");
                }
                catch (Exception ex) { Dbg("hover ERR " + ex); }
            }, DispatcherPriority.ApplicationIdle);
        }

        // 布局调试：-popup:ds|zc 启动即固定展开对应抽屉
        public void DebugPopup(string section)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)delegate
            {
                _pinnedDrawer = section;
                OpenDrawer(section);
            }, DispatcherPriority.ApplicationIdle);
        }

        private StackPanel RingMini(Ui.RingParts p, string caption)
        {
            StackPanel cell = new StackPanel { Width = 38 };
            p.Ring.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(p.Ring);
            TextBlock cap = new TextBlock
            {
                Text = caption,
                FontSize = 9.5,
                Foreground = Ui.Brush(0x99, 0xFF, 0xFF, 0xFF),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                IsHitTestVisible = false,
            };
            cell.Children.Add(cap);
            return cell;
        }

        private Ellipse StatusDot(double size)
        {
            Ellipse e = new Ellipse
            {
                Width = size, Height = size,
                Fill = Ui.Brush(0xFF, 0x99, 0x99, 0x99),
                Stroke = Ui.Brush(0xCC, 0x20, 0x20, 0x20),
                StrokeThickness = 2,
                IsHitTestVisible = true,   // 让 Tooltip 可用
            };
            return e;
        }

        // ---------- 调试：合成数据轮播 ----------
        // 步骤0: 全绿；步骤1: 5时黄/本周绿/紧张；步骤2: 5时红/本周黄/痴呆，余额负数
        private void ApplyMock()
        {
            double[] five = { 0.80, 0.35, 0.10 };
            double[] week = { 0.60, 0.90, 0.45 };
            string[] bal = { "128.40", "12.00", "-3.50" };
            int m = _mockMood;
            ZcInfo zc = new ZcInfo();
            zc.Ok = true; zc.Level = "mock";
            zc.Five = MockLimit(five[m], 2000, DateTime.Now.AddHours(2));
            zc.Week = MockLimit(week[m], 10000, DateTime.Now.AddDays(3));
            DsInfo ds = new DsInfo();
            ds.Ok = true; ds.Currency = "CNY"; ds.Balance = bal[m]; ds.Granted = "10.00";
            _ds = ds; _zc = zc;
            ApplyData();
        }

        private LimitInfo MockLimit(double frac, double total, DateTime reset)
        {
            LimitInfo li = new LimitInfo();
            li.RemainFrac = frac;
            li.Total = total;
            li.Used = Math.Round(total * (1 - frac));
            li.ResetAt = reset;
            return li;
        }

        // ---------- HDR 双击确认（防误触） ----------
        // 单击只「武装」：按钮变琥珀色显示「确认？」，3 秒内再点才真正切换；超时自动还原。
        // 误触代价从「显示器切一次模式（黑屏几秒）」降为「按钮闪一下琥珀色」。
        private void OnHdrDown()
        {
            StopPosAnim();
            _downAt = DateTime.Now;
            double preX = Left, preY = Top;
            try { DragMove(); } catch { }
            double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
            if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
            {
                if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏首击只唤醒
                if (!_hdrArmed) ArmHdr();
                else { DisarmHdr(); ToggleHdrCore(); }
            }
            else UpdateDock(true);
        }

        private void ArmHdr()
        {
            _hdrArmed = true;
            AnimateHdrVisual(Ui.Col(0xB0, 0x8A, 0x50, 0x00), Ui.Col(0xFF, 0xD2, 0x4A), 0.65);
            _label.Text = "确认?";
            _label.FontSize = 13;
            SolidColorBrush tb = new SolidColorBrush(Ui.Col(0xFF, 0xFF, 0xFF));
            tb.Freeze();
            _label.Foreground = tb;
            _armTimer.Stop();
            _armTimer.Start();
        }

        private void DisarmHdr()
        {
            if (!_hdrArmed) return;
            _hdrArmed = false;
            _armTimer.Stop();
            UpdateVisual(true);
        }

        private void ToggleHdrCore()
        {
            HdrCtl.ToggleHdr();
            Thread.Sleep(800);   // 等 Windows 完成模式切换
            UpdateVisual(true);
        }

        private void AnimateScale(double to)
        {
            Duration dur = new Duration(TimeSpan.FromMilliseconds(150));
            CubicEase ease = new CubicEase(); ease.EasingMode = EasingMode.EaseOut;
            DoubleAnimation a = new DoubleAnimation(to, dur);
            a.EasingFunction = ease;
            _hdrScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _hdrScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ---------- 数据刷新（后台线程 → UI 线程回填） ----------
        private void RefreshData()
        {
            _lastFetch = DateTime.Now;
            Dictionary<string, string> cfg = _cfg;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DsInfo ds = DataFeed.FetchDeepSeek(cfg["deepseek_base"], cfg["deepseek_key"]);
                ZcInfo zc = DataFeed.FetchZcode(cfg);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    _ds = ds; _zc = zc;
                    ApplyData();
                });
            });
        }

        private void FillRing(Ui.RingParts p, LimitInfo li, double low, double mid)
        {
            if (li.RemainFrac < 0) { Ui.SetRing(p, -1, Colors.Black); return; }
            int mood = DataFeed.MoodFromRemain(li.RemainFrac, low, mid);
            Color c = mood == 0 ? Ui.Col(0x7C, 0xFC, 0x9E) : (mood == 1 ? Ui.Col(0xFF, 0xD2, 0x4A) : Ui.Col(0xFF, 0x6B, 0x6B));
            Ui.SetRing(p, li.RemainFrac, c);
        }

        private int _dfMoodNow = -1, _zcMoodNow = -1;

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private struct RECT { public int Left, Top, Right, Bottom; }
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public bool MockCycle;          // 调试：合成数据轮播三种状态，不联网
        private int _mockMood = -1;
        private DispatcherTimer _mockTimer;

        private Color MoodColor(double frac, double low, double mid)
        {
            if (frac < 0) return Ui.Col(0xFF, 0x99, 0x99, 0x99);
            int mood = DataFeed.MoodFromRemain(frac, low, mid);
            if (mood == 0) return Ui.Col(0x7C, 0xFC, 0x9E);
            if (mood == 1) return Ui.Col(0xFF, 0xD2, 0x4A);
            return Ui.Col(0xFF, 0x6B, 0x6B);
        }

        private void SetDot(Ellipse dot, Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            dot.Fill = b;
        }

        private void ApplyData()
        {
            double low = SettingsData.Num(_cfg, "warn_low", 20) / 100.0;
            double mid = SettingsData.Num(_cfg, "warn_mid", 50) / 100.0;

            // 抽屉内容（收起时也照常填充，展开即见最新值）
            if (_ds != null && _ds.Ok) _dsBalanceText.Text = DataFeed.CurrencySymbol(_ds.Currency) + " " + _ds.Balance;
            else _dsBalanceText.Text = "--";
            if (_zc != null && _zc.Ok)
            {
                FillRing(_zcFiveRing, _zc.Five, low, mid);
                FillRing(_zcWeekRing, _zc.Week, low, mid);
            }
            else
            {
                Ui.SetRing(_zcFiveRing, -1, Colors.Black);
                Ui.SetRing(_zcWeekRing, -1, Colors.Black);
            }

            // 动画表切换（DS：余额映射心情；异常时定格不播）
            if (_ds != null && _ds.Ok)
            {
                double bal; bool num = double.TryParse(_ds.Balance, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out bal);
                double dsWarn = SettingsData.Num(_cfg, "ds_warn", 20);
                double dsDaze = SettingsData.Num(_cfg, "ds_daze", 1);
                int mood = !num ? 0 : (bal <= dsDaze ? 2 : (bal < dsWarn ? 1 : 0));
                _dfPlayer.SetSheet(mood == 0 ? "normal" : (mood == 1 ? "worry" : "daze"));
                _dfMoodNow = mood;
                _dfFrozen = false;
            }
            else { _dfFrozen = true; _dfMoodNow = -1; }
            if (_zc != null && _zc.Ok)
            {
                // 表情驱动源：five=5小时为主 / week=周为主 / worst=取更差（设置页可选）
                double driver;
                switch (_cfg["zc_mood_source"])
                {
                    case "week": driver = _zc.Week.RemainFrac < 0 ? 1 : _zc.Week.RemainFrac; break;
                    case "worst": driver = Math.Min(_zc.Five.RemainFrac < 0 ? 1 : _zc.Five.RemainFrac, _zc.Week.RemainFrac < 0 ? 1 : _zc.Week.RemainFrac); break;
                    default: driver = _zc.Five.RemainFrac < 0 ? 1 : _zc.Five.RemainFrac; break;
                }
                double worst = driver;
                int cand = DataFeed.MoodFromRemain(worst, low, mid);
                int m = _lastZcMood;
                if (cand != m)
                {
                    bool allow = true;
                    if (m == 1 && cand == 0) allow = worst > mid + 0.05;   // 退档要高出 5 个点才回去
                    if (m == 0 && cand == 1) allow = worst <= mid;
                    if (m == 2 && cand == 1) allow = worst > low + 0.05;
                    if (m == 1 && cand == 2) allow = worst <= low;
                    if (allow) m = cand;
                }
                _lastZcMood = m;
                _zcMoodNow = m;
                _zcPlayer.SetSheet(m == 0 ? "normal" : (m == 1 ? "worry" : "daze"));
                _zcFrozen = false;
            }
            else _zcFrozen = true;

            // 角标状态点：DS 单点；ZC 双点（5时/本周各自独立分档）
            if (_ds != null && _ds.Ok) SetDot(_dsDot, Ui.Col(0x7C, 0xFC, 0x9E));
            else if (_ds != null && _ds.Error != null && _ds.Error.Contains("未配置")) SetDot(_dsDot, Ui.Col(0x88, 0x99, 0x99, 0x99));
            else SetDot(_dsDot, Ui.Col(0xFF, 0x6B, 0x6B));
            if (_zc != null && _zc.Ok)
            {
                SetDot(_zcFiveDot, MoodColor(_zc.Five.RemainFrac, low, mid));
                SetDot(_zcWeekDot, MoodColor(_zc.Week.RemainFrac, low, mid));
            }
            else
            {
                SetDot(_zcFiveDot, Ui.Col(0xFF, 0x6B, 0x6B));
                SetDot(_zcWeekDot, Ui.Col(0xFF, 0x6B, 0x6B));
            }
        }

        private void UpdateVisual(bool force)
        {
            int s = HdrCtl.GetHdrState();
            if (!force && s == _lastState) return;
            _lastState = s;
            if (_hdrArmed) return;   // 确认态视觉优先，不被状态轮询覆盖
            Color fill, glow, txt; double glowO;
            if (s == 2) { fill = Ui.Col(0x1F, 0x9D, 0x5C); glow = Ui.Col(0x2F, 0xD5, 0x75); glowO = 0.55; txt = Ui.Col(0xFF, 0xFF, 0xFF); }
            else if (s == 1) { fill = Ui.Col(0x35, 0x35, 0x35); glow = Ui.Col(0x00, 0x00, 0x00); glowO = 0.25; txt = Ui.Col(0xCC, 0xCC, 0xCC); }
            else { fill = Ui.Col(0x8A, 0x64, 0x00); glow = Ui.Col(0xFF, 0xD2, 0x4A); glowO = 0.5; txt = Ui.Col(0xFF, 0xFF, 0xD2, 0x4A); }
            AnimateHdrVisual(fill, glow, glowO);
            _label.Text = "HDR";
            _label.FontSize = 14;
            SolidColorBrush tb = new SolidColorBrush(txt);
            tb.Freeze();
            _label.Foreground = tb;
        }

        private void AnimateHdrVisual(Color fill, Color glow, double glowOpacity)
        {
            Duration dur = new Duration(TimeSpan.FromMilliseconds(260));
            CubicEase ease = new CubicEase(); ease.EasingMode = EasingMode.EaseOut;
            ColorAnimation ca = new ColorAnimation(fill, dur);
            ca.EasingFunction = ease;
            ColorAnimation ga = new ColorAnimation(glow, dur);
            ga.EasingFunction = ease;
            DoubleAnimation go = new DoubleAnimation(glowOpacity, dur);
            _hdrFill.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            _hdrGlow.BeginAnimation(DropShadowEffect.ColorProperty, ga);
            _hdrGlow.BeginAnimation(DropShadowEffect.OpacityProperty, go);
        }

        private static Border Sep()
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(10, 3, 10, 3),
                Background = Ui.Brush(0x26, 0xFF, 0xFF, 0xFF),
                IsHitTestVisible = false,
            };
        }

        // ---------- 边缘吸附 + 半隐藏 ----------
        // 释放 Left/Top 上的 HoldEnd 动画并把当前值固化为基础值，避免回跳/与 DragMove 打架
        private void StopPosAnim()
        {
            DependencyProperty lp = Window.LeftProperty;
            DependencyProperty tp = Window.TopProperty;
            double x = Left, y = Top;
            BeginAnimation(lp, null);
            BeginAnimation(tp, null);
            Left = x; Top = y;
        }

        private void MoveWidgetTo(double x, double y, bool animate)
        {
            if (!animate) { Left = x; Top = y; return; }
            Duration dur = new Duration(TimeSpan.FromMilliseconds(180));
            CubicEase ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseOut;
            DoubleAnimation ax = new DoubleAnimation(Left, x, dur);
            DoubleAnimation ay = new DoubleAnimation(Top, y, dur);
            ax.EasingFunction = ease; ay.EasingFunction = ease;
            BeginAnimation(Window.LeftProperty, ax);
            BeginAnimation(Window.TopProperty, ay);
        }

        // dock 边上的目标坐标：hidden=只露 sliver；revealed=完全贴边可见；另一轴夹在工作区内
        private Point GetDockPos(string edge, bool hidden)
        {
            Rect wa = SystemParameters.WorkArea;
            double w = Width, h = Height;
            double cx = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
            double cy = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
            // 竖版侧栏 w≠h：左右边弹出按 w，上下边弹出按 h（旧版 64×64 正方时两者等价）
            double s;
            if (hidden) s = Sliver;
            else s = (edge == "left" || edge == "right") ? w : h;
            switch (edge)
            {
                case "left": return new Point(wa.Left + s - w, cy);
                case "right": return new Point(wa.Right - s, cy);
                case "top": return new Point(cx, wa.Top + s - h);
                default: return new Point(cx, wa.Bottom - s);
            }
        }

        private void SetDockState(string edge, bool hidden, bool animate)
        {
            _dock = edge; _tucked = hidden;
            if (hidden) CloseDrawersAndUnpin();   // 缩回时收起抽屉
            Point p = GetDockPos(edge, hidden);
            MoveWidgetTo(p.X, p.Y, animate);
        }

        // 按当前窗口位置评估吸附：与某边缘的有符号距离 < SnapPx（负值=已越出边缘）即吸附并半隐藏
        private void UpdateDock(bool animate)
        {
            Rect wa = SystemParameters.WorkArea;
            double L = Left, T = Top, w = Width, h = Height;
            string best = ""; double bestV = double.MaxValue;
            if (L - wa.Left < SnapPx && L - wa.Left < bestV) { best = "left"; bestV = L - wa.Left; }
            if (wa.Right - (L + w) < SnapPx && wa.Right - (L + w) < bestV) { best = "right"; bestV = wa.Right - (L + w); }
            if (T - wa.Top < SnapPx && T - wa.Top < bestV) { best = "top"; bestV = T - wa.Top; }
            if (wa.Bottom - (T + h) < SnapPx && wa.Bottom - (T + h) < bestV) { best = "bottom"; bestV = wa.Bottom - (T + h); }
            if (best.Length == 0) { _dock = ""; _tucked = false; }
            else SetDockState(best, true, animate);
        }
    }
}
