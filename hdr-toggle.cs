// hdr-toggle.cs — HDR 开关 + 数据侧边栏（原生 WPF 版 v1.3：托盘 + 设置页 + 悬停详情面板）
// 用系统自带 csc.exe (.NET Framework 4.x, 仅支持 C#5 语法) 编译，见 build.ps1；Win10/11 零依赖运行
// 用法:
//   hdr-toggle.exe              启动侧边栏（单实例，带托盘图标）
//   hdr-toggle.exe -state       打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -toggle      切换一次（退出码=切换后状态: 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -on / -off   强制开/关（退出码 0=成功 2=失败）
//   hdr-toggle.exe -quit        退出（托盘+侧边栏）（0=已关闭 1=未找到）
//   hdr-toggle.exe -settings    启动并打开设置页（调试/高级用法）
//   hdr-toggle.exe -popup:ds|zc 启动并固定打开详情面板（布局调试用）
// 交互: 图标行悬停弹出详情面板（平滑滑入），离开延迟消失，点击固定、再点收回；
//       HDR 圆钮点击切换 HDR；窗口可拖动，拖近屏幕边缘吸附半隐藏；托盘左键显示/隐藏，
//       右键菜单：设置/数据配置/退出；侧边栏右键菜单同
// 数据: ZCode 用量 = {zcode_base}/api/monitor/usage/quota/limit，认证用本机 ZCode 凭证
//       (~/.zcode/v2/credentials.json 的 oauth:bigmodel:access_token，enc:v1 = AES-256-GCM,
//        key=sha256("zcode-credential-fallback:win32:{profile}:{user}")，BCrypt P/Invoke 解密)
//       unit=3,number=5 → 五小时圈；unit=6,number=1 → 本周圈
//       DeepSeek 余额 = {deepseek_base}/user/balance，Bearer key（hdr-sidebar.ini 里 deepseek_key）
// 配置: hdr-sidebar.ini（exe 旁），设置页可视化编辑；开机自启写 HKCU Run 注册表

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

[assembly: AssemblyTitle("HDR Toggle")]
[assembly: AssemblyProduct("HDR 开关")]
[assembly: AssemblyDescription("一键开关主显示器 HDR + DeepSeek 余额 + ZCode 用量侧边栏")]
[assembly: AssemblyCompany("HDR Toggle Project")]
[assembly: AssemblyCopyright("Copyright (C) 2026 HDR Toggle Project")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace HDRToggle
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
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (IsWindowVisible(h))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(h, sb, 256);
                    if (sb.ToString() == "HDR Toggle") { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); found = true; return false; }
                }
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

    // ---------- 配置（hdr-sidebar.ini） ----------
    public static class SettingsData
    {
        public static string ExeDir { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        public static string IniPath { get { return Path.Combine(ExeDir, "hdr-sidebar.ini"); } }

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
            return d;
        }

        public static void Save(Dictionary<string, string> d)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# HDR 侧边栏配置（可在设置页修改，或手动编辑）");
            sb.AppendLine("# DeepSeek API key: platform.deepseek.com 创建");
            sb.AppendLine("deepseek_key=" + d["deepseek_key"]);
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
        private const string RunValue = "HDR Toggle";

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
                    if (on) k.SetValue(RunValue, "\"" + Path.Combine(ExeDir, "hdr-toggle.exe") + "\"");
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
        // GET /user/balance → {"is_available":true,"balance_info":{"total_balance":"..","granted_balance":"..","topped_up_balance":"..","currency":"CNY"}}
        public static DsInfo FetchDeepSeek(string baseUrl, string key)
        {
            DsInfo r = new DsInfo();
            if (string.IsNullOrEmpty(key)) { r.Error = "未配置 deepseek_key"; return r; }
            try
            {
                long st;
                string body = HttpGet(baseUrl.TrimEnd('/') + "/user/balance", key, 12000, out st);
                JavaScriptSerializer js = new JavaScriptSerializer();
                IDictionary<string, object> root = JsonObj(js, body);
                if (root == null) throw new Exception("响应不是 JSON");
                object bal;
                if (!root.TryGetValue("balance_info", out bal) || !(bal is IDictionary<string, object>)) throw new Exception("缺少 balance_info");
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

    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // 只把 -/ 开头的参数当 CLI 命令；其余杂散参数（如快捷方式残留）忽略，直接进 GUI
                string first = args.Length > 0 ? args[0] : "";
                if (first.StartsWith("-") || first.StartsWith("/"))
                {
                    string cmd = first.TrimStart('-', '/').ToLowerInvariant();
                    if (cmd == "popup:ds" || cmd == "popup:zc") return RunGui(false, cmd.Substring(6));
                    if (cmd == "settings") return RunGui(true, null);
                    return RunCli(cmd);
                }
                return RunGui(false, null);
            }
            catch (Exception ex)
            {
                try { MessageBox(IntPtr.Zero, ex.ToString(), "HDR Toggle", 0x10); } catch { }
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
                    Out("usage: hdr-toggle.exe [-state|-toggle|-on|-off|-quit|-debugdata|-settings|-popup:ds|-popup:zc]");
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

        static int RunGui(bool openSettings, string popupDebug)
        {
            _mutex = new Mutex(false, "Local\\HdrToggleWidget");   // 单实例
            if (!_mutex.WaitOne(0)) return 0;
            Widget w = new Widget();
            w.Show();
            if (openSettings) w.OpenSettings();
            if (popupDebug != null) w.DebugPopup(popupDebug);
            Dispatcher.Run();   // 无 Application 对象，手动泵消息；Widget.Closed 里 BeginInvokeShutdown
            return 0;
        }
    }

    // ---------- 悬停详情面板（Popup：滑入淡入 / 离开延迟消失 / 点击固定） ----------
    public class DetailPopup
    {
        private readonly Widget _host;
        private readonly Popup _popup;
        private readonly Border _frame;
        private readonly TranslateTransform _slide;
        private string _section;        // "ds" | "zc" | null
        private bool _pinned;
        private bool _hiding;

        // 面板元素（Show 时重建，Refresh 时填充）
        private TextBlock _dsBalance, _dsSub, _dsState, _dsErrMsg;
        private StackPanel _dsErrorBox;
        private Ui.RingParts _zcFive, _zcWeek;
        private TextBlock _zcFiveLine, _zcWeekLine, _zcBadge, _zcErrMsg;
        private StackPanel _zcErrorBox;

        public DetailPopup(Widget host)
        {
            _host = host;
            _popup = new Popup();
            _popup.AllowsTransparency = true;
            _popup.StaysOpen = true;          // 不抢焦点、不被点外面关掉；显隐完全由我们控制
            _popup.PlacementTarget = host;
            _popup.Placement = PlacementMode.Absolute;
            _slide = new TranslateTransform();
            _frame = new Border();
            _frame.CornerRadius = new CornerRadius(12);
            _frame.Background = Ui.Brush(0xF2, 0x1D, 0x1D, 0x1D);
            _frame.BorderBrush = Ui.Brush(0x33, 0xFF, 0xFF, 0xFF);
            _frame.BorderThickness = new Thickness(1);
            _frame.Width = 252;
            _frame.Padding = new Thickness(14);
            _frame.RenderTransform = _slide;
            _frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 };
            _frame.MouseEnter += delegate { _host.CancelPanelHide(); };
            _frame.MouseLeave += delegate { _host.SchedulePanelHide(); };
            _popup.Child = _frame;
        }

        public bool Visible { get { return _popup.IsOpen; } }
        public bool Pinned { get { return _pinned; } }
        public string Section { get { return _section; } }

        private DockPanel Header(string resName, string title, bool withClose, bool withBadge)
        {
            DockPanel dp = new DockPanel { LastChildFill = true };
            if (withClose)
            {
                Button x = new Button();
                x.Content = "✕";
                x.Width = 22; x.Height = 22;
                x.Focusable = false;
                x.Background = Brushes.Transparent;
                x.Foreground = Ui.Brush(0x99, 0xFF, 0xFF, 0xFF);
                x.BorderThickness = new Thickness(0);
                x.Cursor = Cursors.Hand;
                x.VerticalAlignment = VerticalAlignment.Top;
                x.Click += delegate { _host.UnpinPanel(); };
                DockPanel.SetDock(x, Dock.Right);
                dp.Children.Add(x);
            }
            StackPanel h = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Image ic = Ui.IconImage(resName, 20);
            ic.VerticalAlignment = VerticalAlignment.Center;
            h.Children.Add(ic);
            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0xEE, 0xEE, 0xEE),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            h.Children.Add(t);
            if (withBadge)
            {
                _zcBadge = new TextBlock { Text = "", FontSize = 10.5, Foreground = Ui.Brush(0xFF, 0x7C, 0xFC, 0x9E) };
                Border badge = new Border();
                badge.CornerRadius = new CornerRadius(6);
                badge.Background = Ui.Brush(0x30, 0x7C, 0xFC, 0x9E);
                badge.Padding = new Thickness(7, 1, 7, 2);
                badge.Margin = new Thickness(8, 0, 0, 0);
                badge.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = _zcBadge;
                h.Children.Add(badge);
            }
            dp.Children.Add(h);
            return dp;
        }

        private StackPanel Body()
        {
            StackPanel sp = new StackPanel();

            sp.Children.Add(Header("Icon.Dafeiyu.png", "DeepSeek 余额", true, false));

            _dsBalance = new TextBlock
            {
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 8, 0, 0),
            };
            sp.Children.Add(_dsBalance);
            _dsSub = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0xAA, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 5, 0, 0),
            };
            sp.Children.Add(_dsSub);
            _dsState = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0xFF, 0x7C, 0xFC, 0x9E),
                Margin = new Thickness(0, 2, 0, 0),
            };
            sp.Children.Add(_dsState);
            _dsErrorBox = ErrorBox(true, out _dsErrMsg);
            sp.Children.Add(_dsErrorBox);

            sp.Children.Add(new Border
            {
                Height = 1,
                Background = Ui.Brush(0x22, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 12, 0, 12),
            });

            sp.Children.Add(Header("Icon.Zcode.png", "ZCode 订阅用量", false, true));

            StackPanel rings = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            _zcFive = Ui.MakeRing(52, 4.5, 12);
            _zcWeek = Ui.MakeRing(52, 4.5, 12);
            rings.Children.Add(RingCell(_zcFive, "五小时"));
            rings.Children.Add(new Border { Width = 18 });
            rings.Children.Add(RingCell(_zcWeek, "本周"));
            sp.Children.Add(rings);
            _zcFiveLine = new TextBlock { FontSize = 12, Foreground = Ui.Brush(0xCC, 0xFF, 0xFF, 0xFF), Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center };
            sp.Children.Add(_zcFiveLine);
            _zcWeekLine = new TextBlock { FontSize = 12, Foreground = Ui.Brush(0xCC, 0xFF, 0xFF, 0xFF), Margin = new Thickness(0, 2, 0, 0), TextAlignment = TextAlignment.Center };
            sp.Children.Add(_zcWeekLine);
            _zcErrorBox = ErrorBox(false, out _zcErrMsg);
            sp.Children.Add(_zcErrorBox);

            TextBlock foot = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Ui.Brush(0x55, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 12, 0, 0),
                Text = "点击侧边栏图标固定 / 收起  ·  悬停预览",
            };
            sp.Children.Add(foot);
            return sp;
        }

        private StackPanel RingCell(Ui.RingParts p, string caption)
        {
            StackPanel cell = new StackPanel { Width = 78 };
            p.Ring.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(p.Ring);
            TextBlock cap = new TextBlock
            {
                Text = caption,
                FontSize = 10.5,
                Foreground = Ui.Brush(0x99, 0xFF, 0xFF, 0xFF),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            cell.Children.Add(cap);
            return cell;
        }

        private StackPanel ErrorBox(bool withSettingsButton, out TextBlock msg)
        {
            StackPanel box = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            msg = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(0xFF, 0xFF, 0xB0, 0xB0), TextWrapping = TextWrapping.Wrap };
            box.Children.Add(msg);
            if (withSettingsButton)
            {
                Button open = new Button
                {
                    Content = "打开设置",
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(10, 3, 10, 4),
                };
                open.Click += delegate { _host.OpenSettings(); };
                box.Children.Add(open);
            }
            return box;
        }

        // 数据回填（面板开着时每次刷新都会调用）
        public void Refresh(DsInfo ds, ZcInfo zc, Dictionary<string, string> cfg)
        {
            if (ds != null && ds.Ok)
            {
                string sym = DataFeed.CurrencySymbol(ds.Currency);
                _dsBalance.Text = sym + " " + ds.Balance;
                _dsSub.Text = "赠送额度 " + sym + ds.Granted;
                _dsState.Text = "状态：可用 ✓";
                _dsState.Foreground = Ui.Brush(0xFF, 0x7C, 0xFC, 0x9E);
                _dsErrorBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                _dsBalance.Text = "--";
                _dsSub.Text = "";
                _dsState.Text = "";
                _dsErrMsg.Text = (ds != null ? ds.Error : "") + " —— 在设置里填入 DeepSeek API Key";
                _dsErrorBox.Visibility = Visibility.Visible;
            }

            double low = SettingsData.Num(cfg, "warn_low", 20) / 100.0;
            double mid = SettingsData.Num(cfg, "warn_mid", 50) / 100.0;
            if (zc != null && zc.Ok)
            {
                FillRing(_zcFive, zc.Five, low, mid);
                FillRing(_zcWeek, zc.Week, low, mid);
                _zcFiveLine.Text = "已用 " + zc.Five.Used.ToString("0") + " / " + zc.Five.Total.ToString("0") + ResetSuffix(zc.Five);
                _zcWeekLine.Text = "已用 " + zc.Week.Used.ToString("0") + " / " + zc.Week.Total.ToString("0") + ResetSuffix(zc.Week);
                _zcBadge.Text = zc.Level.Length > 0 ? zc.Level : "coding plan";
                _zcErrMsg.Text = "";
                _zcErrorBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                Ui.SetRing(_zcFive, -1, Colors.Black);
                Ui.SetRing(_zcWeek, -1, Colors.Black);
                _zcFiveLine.Text = "";
                _zcWeekLine.Text = "";
                _zcBadge.Text = "?";
                _zcBadge.Foreground = Ui.Brush(0xFF, 0xFF, 0xB0, 0xB0);
                _zcErrMsg.Text = (zc != null ? zc.Error : "") + " —— 需在本机 ZCode 桌面端保持登录";
                _zcErrorBox.Visibility = Visibility.Visible;
            }
        }

        private void FillRing(Ui.RingParts p, LimitInfo li, double low, double mid)
        {
            if (li.RemainFrac < 0) { Ui.SetRing(p, -1, Colors.Black); return; }
            int mood = DataFeed.MoodFromRemain(li.RemainFrac, low, mid);
            Color c = mood == 0 ? Ui.Col(0x7C, 0xFC, 0x9E) : (mood == 1 ? Ui.Col(0xFF, 0xD2, 0x4A) : Ui.Col(0xFF, 0x6B, 0x6B));
            Ui.SetRing(p, li.RemainFrac, c);
        }

        private string ResetSuffix(LimitInfo li)
        {
            return li.ResetAt != DateTime.MinValue ? " · " + li.ResetAt.ToString("MM-dd HH:mm") + " 重置" : "";
        }

        // 弹出（toRight 已由位置计算体现）；animate=false 用于瞬间收起
        public void Show(string section, bool pinned, bool animate)
        {
            _section = section;
            _pinned = pinned;
            _hiding = false;
            _frame.Child = Body();
            // 弹出方向：贴左缘时向右弹，贴右缘时向左弹，悬浮时选空间大的一侧
            Rect wa = SystemParameters.WorkArea;
            double wx = _host.Left + _host.Width / 2;
            bool toRight = _host.Dock == "left" || (_host.Dock != "right" && wx < wa.Left + wa.Width / 2);
            double py = _host.Top + (section == "ds" ? 84 : 148);   // 与对应图标行大致对齐
            double estH = 330;
            if (py + estH > wa.Bottom) py = wa.Bottom - estH;
            if (py < wa.Top) py = wa.Top;
            double px = toRight ? _host.Left + _host.Width + 10 : _host.Left - _frame.Width - 10;
            _popup.HorizontalOffset = px;
            _popup.VerticalOffset = py;
            Refresh(_host.LatestDs, _host.LatestZc, _host.CurrentConfig);
            _popup.IsOpen = true;
            // 滑入淡入（只动 opacity/transform，GPU 友好）
            double from = toRight ? -14 : 14;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = from;
            _frame.Opacity = 0;
            Duration dur = new Duration(TimeSpan.FromMilliseconds(180));
            CubicEase ease = new CubicEase(); ease.EasingMode = EasingMode.EaseOut;
            DoubleAnimation a1 = new DoubleAnimation(from, 0, dur);
            a1.EasingFunction = ease;
            DoubleAnimation a2 = new DoubleAnimation(0, 1, dur);
            a2.EasingFunction = ease;
            _slide.BeginAnimation(TranslateTransform.XProperty, a1);
            _frame.BeginAnimation(UIElement.OpacityProperty, a2);
        }

        public void Hide(bool animate)
        {
            if (!_popup.IsOpen) return;
            if (!animate) { _popup.IsOpen = false; _section = null; _pinned = false; return; }
            if (_hiding) return;
            _hiding = true;
            Duration dur = new Duration(TimeSpan.FromMilliseconds(130));
            DoubleAnimation a1 = new DoubleAnimation(0, 10, dur);
            DoubleAnimation a2 = new DoubleAnimation(1, 0, dur);
            a2.Completed += delegate
            {
                _popup.IsOpen = false;
                _hiding = false;
                _section = null;
                _pinned = false;
            };
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _frame.BeginAnimation(UIElement.OpacityProperty, null);
            _slide.BeginAnimation(TranslateTransform.XProperty, a1);
            _frame.BeginAnimation(UIElement.OpacityProperty, a2);
        }
    }

    // ---------- 设置窗口 ----------
    public class SettingsWindow : Window
    {
        private readonly Widget _host;
        private readonly Dictionary<string, string> _cfg;
        private CheckBox _chkAutostart, _chkTray, _chkHover, _chkShowKey;
        private ComboBox _cmbRefresh, _cmbLow, _cmbMid;
        private PasswordBox _pwdKey;
        private TextBox _txtKey, _txtDsBase, _txtZcBase;
        private TextBlock _testResult;

        public SettingsWindow(Widget host)
        {
            _host = host;
            _cfg = host.CurrentConfig;
            Title = "HDR 侧边栏 · 设置";
            Width = 470;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;                    // 侧边栏本身 topmost，设置页跟它平级才不会被盖住
            Background = Ui.Brush(0xFF, 0x1A, 0x1A, 0x1A);
            Icon = TryIcon();

            StackPanel root = new StackPanel { Margin = new Thickness(18) };
            Content = root;

            TextBlock head = new TextBlock
            {
                Text = "设置",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12),
            };
            root.Children.Add(head);

            // ===== 通用 =====
            StackPanel s1 = Section(root, "通用");
            _chkAutostart = AddCheck(s1, "开机自动启动侧边栏", SettingsData.GetAutostart());
            _chkTray = AddCheck(s1, "显示托盘图标（右键托盘可打开设置）", SettingsData.Flag(_cfg, "show_tray"));
            _chkHover = AddCheck(s1, "悬停图标时弹出详情面板", SettingsData.Flag(_cfg, "hover_details"));

            // ===== 显示 =====
            StackPanel s2 = Section(root, "显示");
            _cmbLow = AddCombo(s2, "额度「见底」阈值（剩余低于此百分比显示红色）", new string[] { "5", "10", "15", "20", "25", "30" }, _cfg["warn_low"]);
            _cmbMid = AddCombo(s2, "额度「中等」阈值（低于此显示黄色）", new string[] { "30", "40", "50", "60", "70" }, _cfg["warn_mid"]);

            // ===== 数据源 =====
            StackPanel s3 = Section(root, "数据源 · DeepSeek");
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
            AddLabeled(s3, "API Key（platform.deepseek.com 创建）", keyRow);
            StackPanel dsTestRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Button btnDs = DarkButton("测试连接", delegate
            {
                string key = _chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password;
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
            s3.Children.Add(dsTestRow);

            StackPanel s3b = Section(root, "数据源 · ZCode");
            _txtZcBase = AddText(s3b, "接口地址（留空=自动跟随本机 ZCode 登录）", _cfg["zcode_base"]);
            StackPanel zcTestRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
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
            zcTestRow.Children.Add(btnZc);
            s3b.Children.Add(zcTestRow);

            StackPanel s3c = Section(root, "刷新");
            _cmbRefresh = AddCombo(s3c, "数据刷新间隔（余额与额度）", new string[] { "1", "2", "5", "10", "15", "30" }, _cfg["refresh_minutes"], " 分钟");

            _testResult = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0xCC, 0x7C, 0xFC, 0x9E),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Text = "",
            };
            root.Children.Add(_testResult);

            // ===== 关于 =====
            StackPanel s4 = Section(root, "关于");
            TextBlock about = new TextBlock
            {
                FontSize = 12,
                Foreground = Ui.Brush(0x99, 0xFF, 0xFF, 0xFF),
                Text = "HDR 侧边栏 v1.3 · HDR 开关 + DeepSeek 余额 + ZCode 订阅用量",
            };
            s4.Children.Add(about);
            Button repo = DarkButton("GitHub 仓库", delegate { Ui.OpenUrl("https://github.com/soberkeen/hdr-toggle"); });
            repo.HorizontalAlignment = HorizontalAlignment.Left;
            repo.Margin = new Thickness(0, 6, 0, 0);
            s4.Children.Add(repo);

            // ===== 保存/取消 =====
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Button save = DarkButton("保存", delegate { SaveAndClose(); });
            save.Background = Ui.Brush(0xFF, 0x17, 0x7B, 0x4D);
            save.BorderBrush = Ui.Brush(0xFF, 0x22, 0x99, 0x66);
            Button cancel = DarkButton("取消", delegate { Close(); });
            cancel.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(save);
            btns.Children.Add(cancel);
            root.Children.Add(btns);
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
            Border box = new Border();
            box.CornerRadius = new CornerRadius(10);
            box.Background = Ui.Brush(0xFF, 0x22, 0x22, 0x22);
            box.BorderBrush = Ui.Brush(0x22, 0xFF, 0xFF, 0xFF);
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(14);
            box.Margin = new Thickness(0, 0, 0, 12);
            StackPanel sp = new StackPanel();
            box.Child = sp;
            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush(0xFF, 0x7C, 0xFC, 0x9E),
                Margin = new Thickness(0, 0, 0, 8),
            };
            sp.Children.Add(t);
            root.Children.Add(box);
            return sp;
        }

        private CheckBox AddCheck(StackPanel sp, string label, bool isChecked)
        {
            CheckBox c = new CheckBox
            {
                Content = label,
                FontSize = 12.5,
                Foreground = Ui.Brush(0xEE, 0xFF, 0xFF, 0xFF),
                IsChecked = isChecked,
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand,
            };
            sp.Children.Add(c);
            return c;
        }

        private TextBlock Label(StackPanel sp, string text)
        {
            TextBlock t = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Ui.Brush(0xAA, 0xFF, 0xFF, 0xFF),
                Margin = new Thickness(0, 8, 0, 3),
            };
            sp.Children.Add(t);
            return t;
        }

        private ComboBox AddCombo(StackPanel sp, string label, string[] options, string current, string suffix)
        {
            Label(sp, label);
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
            Label(sp, label);
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

        private void AddLabeled(StackPanel sp, string label, FrameworkElement control)
        {
            Label(sp, label);
            sp.Children.Add(control);
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

        private string ComboValue(ComboBox cb, string suffix)
        {
            string s = cb.SelectedItem as string;
            if (s == null) return "";
            return suffix != null ? s.Replace(suffix, "") : s;
        }

        private void SaveAndClose()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);
            d["deepseek_key"] = _chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password;
            d["deepseek_base"] = string.IsNullOrWhiteSpace(_txtDsBase.Text) ? "https://api.deepseek.com" : _txtDsBase.Text.Trim();
            d["zcode_base"] = _txtZcBase.Text.Trim();
            d["refresh_minutes"] = ComboValue(_cmbRefresh, " 分钟");
            d["warn_low"] = ComboValue(_cmbLow, null);
            d["warn_mid"] = ComboValue(_cmbMid, null);
            d["hover_details"] = _chkHover.IsChecked == true ? "true" : "false";
            d["show_tray"] = _chkTray.IsChecked == true ? "true" : "false";
            SettingsData.Save(d);
            SettingsData.SetAutostart(_chkAutostart.IsChecked == true);
            _host.ApplyConfigChanged();
            Close();
        }
    }

    // ---------- 侧边栏 ----------
    public class Widget : Window
    {
        private const double Sliver = 12;   // 半隐藏时保留可见的宽度
        private const double SnapPx = 28;   // 距边缘多少像素内判定为吸附

        private static readonly string PosFile = Path.Combine(SettingsData.ExeDir, "hdr-toggle.pos");

        // HDR 圆钮（美化：渐变圆角方块 + 状态发光 + 缩放反馈）
        private readonly Border _hdrBtn;
        private readonly TextBlock _label;
        private readonly SolidColorBrush _hdrFill;        // 非冻结，做颜色渐变动画
        private readonly DropShadowEffect _hdrGlow;
        private readonly ScaleTransform _hdrScale;

        private readonly Grid _root;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _tuckTimer;
        private readonly DispatcherTimer _dataTimer;
        private readonly DispatcherTimer _panelHideTimer;

        // 图标行
        private Ellipse _dsDot, _zcDot;                   // 角标状态点
        private Grid _dsRow, _zcRow;

        private DetailPopup _popup;
        private SettingsWindow _settings;
        private WF.NotifyIcon _tray;

        private Dictionary<string, string> _cfg;
        private DsInfo _ds;
        private ZcInfo _zc;
        private DateTime _lastFetch = DateTime.MinValue;

        private string _pinnedSection;                    // "ds"|"zc" 固定的面板；null 无
        private string _hoverSection;

        private string _dock = "";       // "" | left | right | top | bottom
        private bool _tucked;
        private int _lastState = -99;
        private DateTime _downAt;

        public string Dock { get { return _dock; } }
        public Dictionary<string, string> CurrentConfig { get { return _cfg; } }
        public DsInfo LatestDs { get { return _ds; } }
        public ZcInfo LatestZc { get { return _zc; } }

        public Widget()
        {
            Title = "HDR Toggle";
            Width = 80; Height = 190;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;   // 禁用系统贴边分屏放大（Aero Snap）
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;

            _cfg = SettingsData.Load();

            Border panel = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = Ui.Brush(0xE6, 0x20, 0x20, 0x20),
                BorderBrush = Ui.Brush(0x33, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(1),
            };
            StackPanel stack = new StackPanel();
            panel.Child = stack;
            _root = new Grid { Cursor = Cursors.Hand };
            _root.Children.Add(panel);
            Content = _root;

            // ---- HDR 圆钮（美化版） ----
            Grid hdrArea = new Grid
            {
                Height = 64,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            _hdrScale = new ScaleTransform(1, 1);
            _hdrFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x17, 0x7B, 0x4D));   // 初值会被 UpdateVisual 覆盖
            _hdrGlow = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.0 };
            _hdrBtn = new Border
            {
                Width = 56, Height = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(16),
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
                FontSize = 14,
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
            hdrArea.MouseLeftButtonDown += delegate
            {
                StopPosAnim();
                _downAt = DateTime.Now;
                double preX = Left, preY = Top;
                try { DragMove(); } catch { }
                double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
                if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
                {
                    if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏时第一击只唤醒
                    ToggleHdr();
                }
                else UpdateDock(true);
            };
            stack.Children.Add(hdrArea);

            stack.Children.Add(Sep());

            // ---- DeepSeek 图标行 ----
            _dsRow = IconRow(delegate { TogglePin("ds"); }, delegate { HoverRow("ds"); }, delegate { LeaveRow("ds"); });
            Image dsIcon = Ui.IconImage("Icon.Dafeiyu.png", 36);
            dsIcon.VerticalAlignment = VerticalAlignment.Center;
            dsIcon.HorizontalAlignment = HorizontalAlignment.Center;
            dsIcon.IsHitTestVisible = false;
            _dsDot = StatusDot();
            _dsRow.Children.Add(dsIcon);
            _dsRow.Children.Add(_dsDot);
            _dsRow.ToolTip = "DeepSeek 余额";
            stack.Children.Add(_dsRow);

            stack.Children.Add(Sep());

            // ---- ZCode 图标行 ----
            _zcRow = IconRow(delegate { TogglePin("zc"); }, delegate { HoverRow("zc"); }, delegate { LeaveRow("zc"); });
            Image zcIcon = Ui.IconImage("Icon.Zcode.png", 36);
            zcIcon.VerticalAlignment = VerticalAlignment.Center;
            zcIcon.HorizontalAlignment = HorizontalAlignment.Center;
            zcIcon.IsHitTestVisible = false;
            _zcDot = StatusDot();
            _zcRow.Children.Add(zcIcon);
            _zcRow.Children.Add(_zcDot);
            _zcRow.ToolTip = "ZCode 订阅用量";
            stack.Children.Add(_zcRow);

            // ---- 底部留白 ----
            Border pad = new Border { Height = 6, IsHitTestVisible = false };
            stack.Children.Add(pad);

            _popup = new DetailPopup(this);
            _panelHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _panelHideTimer.Tick += delegate
            {
                _panelHideTimer.Stop();
                if (_pinnedSection == null && _hoverSection == null) _popup.Hide(true);
            };

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
            _pollTimer.Tick += delegate { UpdateVisual(); };

            _tuckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _tuckTimer.Tick += delegate
            {
                _tuckTimer.Stop();
                if (_dock.Length > 0 && !_tucked && !_root.IsMouseOver) SetDockState(_dock, true, true);
            };

            _dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _dataTimer.Tick += delegate
            {
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
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();   // 弹出时顺带补数据
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
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                try { File.WriteAllText(PosFile, ((int)Left).ToString() + "," + ((int)Top).ToString()); } catch { }
                Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };

            SetupTray();
        }

        public void Start()
        {
            UpdateVisual();
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
                _tray.Text = "HDR 侧边栏 · HDR + DeepSeek + ZCode";
                _tray.Visible = SettingsData.Flag(_cfg, "show_tray");

                WF.ContextMenuStrip menu = new WF.ContextMenuStrip();
                menu.Items.Add("显示 / 隐藏侧边栏", null, delegate { ToggleVisible(); });
                ((WF.ToolStripMenuItem)menu.Items[0]).Font = new SD.Font("Microsoft YaHei UI", 9f);
                menu.Items.Add("设置…", null, delegate { OpenSettings(); });
                menu.Items.Add("数据配置", null, delegate { OpenIni(); });
                menu.Items.Add(new WF.ToolStripSeparator());
                menu.Items.Add("退出", null, delegate { Close(); });
                _tray.ContextMenuStrip = menu;
                _tray.MouseUp += delegate(object sender, WF.MouseEventArgs e)
                {
                    if (e.Button == WF.MouseButtons.Left) ToggleVisible();
                };
            }
            catch { _tray = null; }
        }

        private void ToggleVisible()
        {
            if (Visibility == Visibility.Visible) { HidePopupAndPin(); Visibility = Visibility.Hidden; }
            else { Visibility = Visibility.Visible; UpdateDock(false); }
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

        // 设置保存后：重读配置、同步托盘、立刻刷新数据
        public void ApplyConfigChanged()
        {
            _cfg = SettingsData.Load();
            if (_tray != null) _tray.Visible = SettingsData.Flag(_cfg, "show_tray");
            RefreshData();
        }

        // ---------- 详情面板（悬停/固定） ----------
        public void CancelPanelHide() { _panelHideTimer.Stop(); }
        public void SchedulePanelHide()
        {
            _panelHideTimer.Stop();
            _panelHideTimer.Start();
        }

        private void HoverRow(string section)
        {
            if (!SettingsData.Flag(_cfg, "hover_details")) return;
            if (_pinnedSection != null) return;                 // 固定中不跟随悬停
            _hoverSection = section;
            CancelPanelHide();
            if (_popup.Section != section || !_popup.Visible) { _popup.Show(section, false, true); RefreshPopup(); }
        }

        private void LeaveRow(string section)
        {
            if (_hoverSection == section) _hoverSection = null;
            if (_pinnedSection == null) SchedulePanelHide();
        }

        private void TogglePin(string section)
        {
            if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏首击只唤醒
            if (_pinnedSection == section)
            {
                _pinnedSection = null;
                _popup.Hide(true);
            }
            else
            {
                _pinnedSection = section;
                CancelPanelHide();
                _popup.Show(section, true, true);
                RefreshPopup();
            }
        }

        public void UnpinPanel()   // 面板上的 ✕
        {
            _pinnedSection = null;
            _hoverSection = null;
            _popup.Hide(true);
        }

        private void HidePopupAndPin()
        {
            _pinnedSection = null;
            _hoverSection = null;
            if (_popup != null) _popup.Hide(false);
        }

        private void RefreshPopup()
        {
            if (_popup != null && _popup.Visible) _popup.Refresh(_ds, _zc, _cfg);
        }

        public void DebugPopup(string section)
        {
            _pinnedSection = section;
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)delegate
            {
                _popup.Show(section, true, true);
                RefreshPopup();
            }, DispatcherPriority.ApplicationIdle);
        }

        // ---------- 图标行/角标 ----------
        private Grid IconRow(Action onClick, Action onEnter, Action onLeave)
        {
            Grid g = new Grid
            {
                Height = 48,
                Background = Brushes.Transparent,   // 保证空隙也能命中
                Cursor = Cursors.Hand,
            };
            g.MouseLeftButtonDown += delegate
            {
                StopPosAnim();
                _downAt = DateTime.Now;
                double preX = Left, preY = Top;
                try { DragMove(); } catch { }
                double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
                if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
                {
                    if (_tucked) { SetDockState(_dock, false, true); return; }
                    onClick();
                }
                else UpdateDock(true);
            };
            g.MouseEnter += delegate { onEnter(); };
            g.MouseLeave += delegate { onLeave(); };
            return g;
        }

        private Ellipse StatusDot()
        {
            Ellipse e = new Ellipse
            {
                Width = 9, Height = 9,
                Fill = Ui.Brush(0xFF, 0x99, 0x99, 0x99),
                Stroke = Ui.Brush(0xCC, 0x20, 0x20, 0x20),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 12, 4),
                IsHitTestVisible = false,
            };
            return e;
        }

        private void ToggleHdr()
        {
            HdrCtl.ToggleHdr();
            Thread.Sleep(800);   // 等 Windows 完成模式切换
            UpdateVisual();
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

        private Color MoodColor(double frac)
        {
            double low = SettingsData.Num(_cfg, "warn_low", 20) / 100.0;
            double mid = SettingsData.Num(_cfg, "warn_mid", 50) / 100.0;
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

            // 角标状态点
            if (_ds != null && _ds.Ok) SetDot(_dsDot, Ui.Col(0x7C, 0xFC, 0x9E));
            else if (_ds != null && _ds.Error != null && _ds.Error.Contains("未配置")) SetDot(_dsDot, Ui.Col(0x88, 0x99, 0x99, 0x99));
            else SetDot(_dsDot, Ui.Col(0xFF, 0x6B, 0x6B));
            if (_zc != null && _zc.Ok)
            {
                double worst = Math.Min(_zc.Five.RemainFrac < 0 ? 1 : _zc.Five.RemainFrac, _zc.Week.RemainFrac < 0 ? 1 : _zc.Week.RemainFrac);
                SetDot(_zcDot, MoodColor(worst));
            }
            else SetDot(_zcDot, Ui.Col(0xFF, 0x6B, 0x6B));

            // 面板内容
            if (_popup != null && _popup.Visible) _popup.Refresh(_ds, _zc, _cfg);
        }

        private void UpdateVisual()
        {
            int s = HdrCtl.GetHdrState();
            if (s == _lastState) return;
            _lastState = s;
            // 平滑颜色过渡（非冻结画刷 + 颜色动画）
            Color fill, glow, txt;
            if (s == 2) { fill = Ui.Col(0x1F, 0x9D, 0x5C); glow = Ui.Col(0x2F, 0xD5, 0x75); txt = Ui.Col(0xFF, 0xFF, 0xFF); }
            else if (s == 1) { fill = Ui.Col(0x35, 0x35, 0x35); glow = Ui.Col(0x00, 0x00, 0x00); txt = Ui.Col(0xCC, 0xCC, 0xCC); }
            else { fill = Ui.Col(0x8A, 0x64, 0x00); glow = Ui.Col(0xFF, 0xD2, 0x4A); txt = Ui.Col(0xFF, 0xFF, 0xD2, 0x4A); }
            Duration dur = new Duration(TimeSpan.FromMilliseconds(260));
            CubicEase ease = new CubicEase(); ease.EasingMode = EasingMode.EaseOut;
            ColorAnimation ca = new ColorAnimation(fill, dur);
            ca.EasingFunction = ease;
            ColorAnimation ga = new ColorAnimation(glow, dur);
            ga.EasingFunction = ease;
            DoubleAnimation go = new DoubleAnimation(s == 2 ? 0.55 : (s == 1 ? 0.25 : 0.5), dur);
            _hdrFill.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            _hdrGlow.BeginAnimation(DropShadowEffect.ColorProperty, ga);
            _hdrGlow.BeginAnimation(DropShadowEffect.OpacityProperty, go);
            SolidColorBrush txtBrush = new SolidColorBrush(txt);
            txtBrush.Freeze();
            _label.Foreground = txtBrush;
            _label.Text = "HDR";
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
            if (hidden) HidePopupAndPin();   // 缩回时收起面板
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
