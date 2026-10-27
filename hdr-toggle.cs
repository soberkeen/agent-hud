// hdr-toggle.cs — HDR 开关 + 数据侧边栏（原生 WPF 版 v1.2）
// 用系统自带 csc.exe (.NET Framework 4.x, 仅支持 C#5 语法) 编译，见 build.ps1；Win10/11 零依赖运行
// 用法:
//   hdr-toggle.exe              启动侧边栏（单实例）
//   hdr-toggle.exe -state       打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -toggle      切换一次（退出码=切换后状态: 0=ON 1=OFF 2=错误）
//   hdr-toggle.exe -on / -off   强制开/关（退出码 0=成功 2=失败）
//   hdr-toggle.exe -quit        关闭运行中的小窗（0=已关闭 1=未找到）
// 交互: HDR 圆钮点击切换；DeepSeek 行点击开用量页；ZCode 圈点击开套餐页；窗口可拖动，
//       拖近屏幕边缘自动吸附并半隐藏（悬停弹出，移开缩回）；右键菜单：数据配置/退出
// 数据: ZCode 用量 = {zcode_base}/api/monitor/usage/quota/limit，认证用本机 ZCode 凭证
//       (~/.zcode/v2/credentials.json 的 oauth:bigmodel:access_token，enc:v1 = AES-256-GCM,
//        key=sha256("zcode-credential-fallback:win32:{profile}:{user}")，BCrypt P/Invoke 解密)
//       unit=3,number=5 → 五小时圈；unit=6,number=1 → 本周圈
//       DeepSeek 余额 = {deepseek_base}/user/balance，Bearer key（hdr-sidebar.ini 里 deepseek_key）
// 注: winexe 无控制台，CLI 输出经 AttachConsole 写回调用方终端；脚本自动化请用退出码

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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("HDR Toggle")]
[assembly: AssemblyProduct("HDR 开关")]
[assembly: AssemblyDescription("一键开关主显示器 HDR + DeepSeek 余额 + ZCode 用量侧边栏")]
[assembly: AssemblyCompany("HDR Toggle Project")]
[assembly: AssemblyCopyright("Copyright (C) 2026 HDR Toggle Project")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

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

        // 关闭正在运行的小窗（标题 HDR Toggle 的可见窗口发 WM_CLOSE）
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
        public static string ExeDir
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }
        public static string IniPath { get { return Path.Combine(ExeDir, "hdr-sidebar.ini"); } }

        public static Dictionary<string, string> LoadIni()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["deepseek_key"] = "";
            d["deepseek_base"] = "https://api.deepseek.com";
            d["zcode_base"] = "";                    // 空=自动（按凭证 family 选 bigmodel/z.ai）
            d["zcode_credentials"] = "";             // 空=默认 ~/.zcode/v2/credentials.json
            d["refresh_minutes"] = "5";
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

        // ---------- DeepSeek ----------
        // GET /user/balance → {"is_available":true,"balance_info":{"total_balance":"..","granted_balance":"..","topped_up_balance":"..","currency":"CNY"}}
        public static DsInfo FetchDeepSeek(string baseUrl, string key)
        {
            DsInfo r = new DsInfo();
            if (string.IsNullOrEmpty(key)) { r.Error = "未配置 deepseek_key（右键→数据配置）"; return r; }
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
            throw new Exception("凭证里没有 oauth access_token（先在 ZCode 里登录）");
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

        private static object Get(IDictionary<string, object> d, string k)
        {
            object o; d.TryGetValue(k, out o); return o;
        }

        public static string CurrencySymbol(string c)
        {
            if (c == "CNY") return "¥";
            if (c == "USD") return "$";
            return c + " ";
        }

        // 额度→表情分档（图标动态表情预留：本版只用于圈色）
        // 够用>0.5 正常 / 0.2~0.5 慌张 / <0.2 痴呆
        public static int MoodFromRemain(double frac)
        {
            if (frac > 0.5) return 0;
            if (frac > 0.2) return 1;
            return 2;
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
                if (first.StartsWith("-") || first.StartsWith("/")) return RunCli(first.TrimStart('-', '/').ToLowerInvariant());
                return RunGui();
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
                        Dictionary<string, string> cfg = DataFeed.LoadIni();
                        DsInfo ds = DataFeed.FetchDeepSeek(cfg["deepseek_base"], cfg["deepseek_key"]);
                        Out("DeepSeek: " + (ds.Ok ? "OK balance=" + ds.Balance + " " + ds.Currency + " granted=" + ds.Granted : "FAIL " + ds.Error));
                        ZcInfo zc = DataFeed.FetchZcode(cfg);
                        Out("ZCode: " + (zc.Ok
                            ? "OK level=" + zc.Level + " 5h=" + zc.Five.RemainFrac.ToString("P0") + " (" + zc.Five.Used + "/" + zc.Five.Total + ") week=" + zc.Week.RemainFrac.ToString("P0") + " (" + zc.Week.Used + "/" + zc.Week.Total + ")"
                            : "FAIL " + zc.Error));
                        return (ds.Ok && zc.Ok) ? 0 : 2;
                    }
                default:
                    Out("usage: hdr-toggle.exe [-state|-toggle|-on|-off|-quit|-debugdata]");
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

        static int RunGui()
        {
            _mutex = new Mutex(false, "Local\\HdrToggleWidget");   // 单实例
            if (!_mutex.WaitOne(0)) return 0;
            Widget w = new Widget();
            w.Start();
            return 0;
        }
    }

    // ---------- 侧边栏 ----------
    public class Widget : Window
    {
        private const double Sliver = 12;   // 半隐藏时保留可见的宽度
        private const double SnapPx = 28;   // 距边缘多少像素内判定为吸附

        private static readonly string PosFile = Path.Combine(DataFeed.ExeDir, "hdr-toggle.pos");

        private readonly Ellipse _circle;
        private readonly TextBlock _label;
        private readonly Grid _root;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _tuckTimer;
        private readonly DispatcherTimer _dataTimer;

        // DeepSeek 区
        private readonly TextBlock _dsText;
        private Grid _dsRow;
        // ZCode 区
        private readonly TextBlock _fiveNum, _weekNum;
        private readonly System.Windows.Shapes.Path _fiveArc, _weekArc;
        private readonly Ellipse _fiveFull, _weekFull;
        private Grid _zcRow;

        private Dictionary<string, string> _cfg;
        private DsInfo _ds;
        private ZcInfo _zc;
        private DateTime _lastFetch = DateTime.MinValue;

        private string _dock = "";       // "" | left | right | top | bottom
        private bool _tucked;
        private int _lastState = -99;
        private DateTime _downAt;

        public Widget()
        {
            Title = "HDR Toggle";
            Width = 80; Height = 236;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;   // 禁用系统贴边分屏放大（Aero Snap）
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;

            _cfg = DataFeed.LoadIni();

            Border panel = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = Brush(0xE6, 0x20, 0x20, 0x20),
                BorderBrush = Brush(0x33, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(1),
            };
            StackPanel stack = new StackPanel();
            panel.Child = stack;
            _root = new Grid { Cursor = Cursors.Hand };
            _root.Children.Add(panel);
            Content = _root;

            // ---- HDR 圆钮（原 64×64 交互原样保留） ----
            Grid hdrArea = Row(64, ToggleClick);
            _circle = new Ellipse
            {
                Width = 56, Height = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = Brush(0xE0, 0x33, 0x33, 0x33),
                Stroke = Brush(0x66, 0xFF, 0xFF, 0xFF),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            _label = new TextBlock
            {
                Text = "HDR",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(0xFF, 0xAA, 0xAA, 0xAA),
                IsHitTestVisible = false,
            };
            hdrArea.Children.Add(_circle);
            hdrArea.Children.Add(_label);
            stack.Children.Add(hdrArea);

            stack.Children.Add(Sep());

            // ---- DeepSeek 余额 ----
            Grid dsRow = Row(46, delegate { OpenUrl("https://platform.deepseek.com/usage"); });
            _dsRow = dsRow;
            Image dsIcon = new Image
            {
                Width = 30, Height = 30, Margin = new Thickness(6, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Source = ResImage("Icon.Dafeiyu.png"),
            };
            RenderOptions.SetBitmapScalingMode(dsIcon, BitmapScalingMode.HighQuality);
            _dsText = new TextBlock
            {
                Text = "--",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(0xFF, 0xEE, 0xEE, 0xEE),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
                IsHitTestVisible = false,
            };
            dsRow.Children.Add(dsIcon);
            dsRow.Children.Add(_dsText);
            dsRow.ToolTip = "DeepSeek 余额";
            stack.Children.Add(dsRow);

            stack.Children.Add(Sep());

            // ---- ZCode 订阅用量：两个额度圈 ----
            Grid zcRow = Row(112, delegate { OpenUrl("https://bigmodel.cn/coding-plan/personal/overview"); });
            _zcRow = zcRow;
            Image zcIcon = new Image
            {
                Width = 38, Height = 38, Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Source = ResImage("Icon.Zcode.png"),
            };
            RenderOptions.SetBitmapScalingMode(zcIcon, BitmapScalingMode.HighQuality);
            StackPanel rings = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8),
            };
            _fiveArc = new System.Windows.Shapes.Path { StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _fiveFull = RingBg();
            _fiveNum = RingNum();
            rings.Children.Add(RingCell(_fiveFull, _fiveArc, _fiveNum, "5时"));
            TextBlock gap = new TextBlock { Width = 10 };
            rings.Children.Add(gap);
            _weekArc = new System.Windows.Shapes.Path { StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _weekFull = RingBg();
            _weekNum = RingNum();
            rings.Children.Add(RingCell(_weekFull, _weekArc, _weekNum, "本周"));
            zcRow.Children.Add(zcIcon);
            zcRow.Children.Add(rings);
            zcRow.ToolTip = "ZCode 订阅用量";
            stack.Children.Add(zcRow);

            ContextMenu menu = new ContextMenu();
            MenuItem cfgItem = new MenuItem { Header = "数据配置" };
            cfgItem.Click += delegate
            {
                try
                {
                    if (!File.Exists(DataFeed.IniPath)) File.WriteAllText(DataFeed.IniPath, "# DeepSeek API key（platform.deepseek.com 申请）\ndeepseek_key=\n\n# 可选：刷新间隔（分钟）\n#refresh_minutes=5\n");
                    System.Diagnostics.Process.Start("notepad.exe", DataFeed.IniPath);
                }
                catch { }
            };
            menu.Items.Add(cfgItem);
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
                double mins;
                if (!double.TryParse(_cfg["refresh_minutes"], out mins) || mins < 1) mins = 5;
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData(false);
            };

            MouseEnter += delegate
            {
                if (_dock.Length > 0)
                {
                    _tuckTimer.Stop();
                    if (_tucked) SetDockState(_dock, false, true);   // 悬停弹出
                }
                double mins;
                if (!double.TryParse(_cfg["refresh_minutes"], out mins) || mins < 1) mins = 5;
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData(false);   // 弹出时顺带补数据
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
                try { File.WriteAllText(PosFile, ((int)Left).ToString() + "," + ((int)Top).ToString()); } catch { }
            };
        }

        public void Start()
        {
            UpdateVisual();
            _pollTimer.Start();
            UpdateDock(false);   // 上次关闭时若贴边，启动即恢复半隐藏
            RefreshData(true);
            _dataTimer.Start();
            ShowDialog();
        }

        private void ToggleClick()
        {
            if (_tucked) return;   // 不会走到：RowDown 里半隐藏首击只唤醒
            HdrCtl.ToggleHdr();
            Thread.Sleep(800);   // 等 Windows 完成模式切换
            UpdateVisual();
        }

        // ---------- 数据刷新（后台线程 → UI 线程回填） ----------
        private void RefreshData(bool initial)
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

        private void ApplyData()
        {
            // DeepSeek
            if (_ds != null && _ds.Ok)
            {
                string text = DataFeed.CurrencySymbol(_ds.Currency) + _ds.Balance;
                _dsText.Text = text;
                _dsText.FontSize = text.Length > 9 ? 10 : (text.Length > 7 ? 10.5 : 12);
                _dsText.Foreground = Brush(0xFF, 0xEE, 0xEE, 0xEE);
                _dsRow.ToolTip = "DeepSeek 余额 " + DataFeed.CurrencySymbol(_ds.Currency) + _ds.Balance
                    + "（其中赠送 " + DataFeed.CurrencySymbol(_ds.Currency) + _ds.Granted + "）";
            }
            else
            {
                _dsText.Text = "未配置";
                _dsText.Foreground = Brush(0x88, 0xFF, 0xFF, 0xFF);
                _dsRow.ToolTip = (_ds != null ? _ds.Error : "") + "\n右键 → 数据配置，填 deepseek_key";
            }

            // ZCode 双圈
            if (_zc != null && _zc.Ok)
            {
                string tip = "ZCode 订阅用量（" + (_zc.Level.Length > 0 ? _zc.Level : "coding plan") + "）";
                ApplyRing(_fiveFull, _fiveArc, _fiveNum, _zc.Five, "五小时额度", ref tip);
                tip += "\n";
                ApplyRing(_weekFull, _weekArc, _weekNum, _zc.Week, "本周额度", ref tip);
                _zcRow.ToolTip = tip;
            }
            else
            {
                SetRing(_fiveFull, _fiveArc, _fiveNum, -1);
                SetRing(_weekFull, _weekArc, _weekNum, -1);
                _zcRow.ToolTip = "ZCode 用量获取失败：" + (_zc != null ? _zc.Error : "") + "\n（需在 ZCode 桌面端保持登录）";
            }
        }

        private void ApplyRing(Ellipse full, System.Windows.Shapes.Path arc, TextBlock num, LimitInfo li, string name, ref string tip)
        {
            SetRing(full, arc, num, li.RemainFrac);
            tip += name + "：已用 " + li.Used.ToString("0") + " / " + li.Total.ToString("0")
                + "（剩 " + (li.RemainFrac * 100).ToString("0") + "%）";
            if (li.ResetAt != DateTime.MinValue) tip += "，" + li.ResetAt.ToString("MM-dd HH:mm") + " 重置";
        }

        // frac<0 → 错误态灰圈 "?"
        private void SetRing(Ellipse full, System.Windows.Shapes.Path arc, TextBlock num, double frac)
        {
            Color c;
            if (frac < 0) c = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
            else
            {
                int mood = DataFeed.MoodFromRemain(frac);
                if (mood == 0) c = Color.FromArgb(0xFF, 0x7C, 0xFC, 0x9E);       // 充足-绿
                else if (mood == 1) c = Color.FromArgb(0xFF, 0xFF, 0xD2, 0x4A);  // 中等-黄
                else c = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);                 // 见底-红
            }
            SolidColorBrush b = new SolidColorBrush(c); b.Freeze();
            arc.Stroke = b;
            num.Foreground = frac >= 0 ? b : Brush(0xFF, 0x99, 0x99, 0x99);
            full.Stroke = Brush(0x33, 0xFF, 0xFF, 0xFF);

            const double size = 32, stroke = 3.5;
            double r = (size - stroke) / 2, c0 = size / 2;
            if (frac < 0)
            {
                arc.Data = null;
                num.Text = "?";
                return;
            }
            if (frac >= 0.995)
            {
                arc.Data = null;
                full.Stroke = b;   // 满圈直接画整圆
                num.Text = ((int)Math.Round(frac * 100)).ToString();
                return;
            }
            double sweep = 360.0 * frac;
            double a0 = -Math.PI / 2, a1 = a0 + sweep * Math.PI / 180.0;
            Point start = new Point(c0, c0 - r), end = new Point(c0 + r * Math.Cos(a1), c0 + r * Math.Sin(a1));
            PathFigure fig = new PathFigure();
            fig.StartPoint = start;
            ArcSegment seg = new ArcSegment();
            seg.Point = end;
            seg.Size = new Size(r, r);
            seg.IsLargeArc = sweep > 180;
            seg.SweepDirection = SweepDirection.Clockwise;
            fig.Segments.Add(seg);
            PathGeometry pg = new PathGeometry();
            pg.Figures.Add(fig);
            arc.Data = pg;
            num.Text = ((int)Math.Round(frac * 100)).ToString();
        }

        private Ellipse RingBg()
        {
            return new Ellipse
            {
                Stroke = Brush(0x33, 0xFF, 0xFF, 0xFF),
                StrokeThickness = 3.5,
                IsHitTestVisible = false,
            };
        }
        private TextBlock RingNum()
        {
            return new TextBlock
            {
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
        }
        private Border RingCell(Ellipse bg, System.Windows.Shapes.Path arc, TextBlock num, string caption)
        {
            Grid g = new Grid { Width = 32, Height = 32 };
            g.Children.Add(bg);
            g.Children.Add(arc);
            g.Children.Add(num);
            StackPanel cell = new StackPanel();
            cell.Children.Add(g);
            TextBlock cap = new TextBlock
            {
                Text = caption,
                FontSize = 8.5,
                Foreground = Brush(0x99, 0xFF, 0xFF, 0xFF),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
                IsHitTestVisible = false,
            };
            cell.Children.Add(cap);
            Border wrap = new Border { Width = 32, Child = cell };
            return wrap;
        }

        private ImageSource ResImage(string resName)
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

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        // 行容器：注册按下行为（clickAction=null → 仅拖动）
        private Grid Row(double height, Action clickAction)
        {
            Grid g = new Grid
            {
                Height = height,
                Background = Brushes.Transparent,   // 保证空隙也能命中
                Cursor = Cursors.Hand,
                Tag = clickAction,
            };
            g.MouseLeftButtonDown += RowDown;
            return g;
        }

        private void RowDown(object sender, MouseButtonEventArgs e)
        {
            Grid g = (Grid)sender;
            Action click = g.Tag as Action;
            StopPosAnim();
            _downAt = DateTime.Now;
            double preX = Left, preY = Top;
            try { DragMove(); } catch { }
            double moved = Math.Abs(Left - preX) + Math.Abs(Top - preY);
            if (moved < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 1500)
            {
                if (_tucked) { SetDockState(_dock, false, true); return; }   // 半隐藏时第一击只唤醒
                if (click != null) click();
            }
            else
            {
                UpdateDock(true);    // 拖放结束：贴近边缘则吸附+半隐藏
            }
        }

        private void UpdateVisual()
        {
            int s = HdrCtl.GetHdrState();
            if (s == _lastState) return;
            _lastState = s;
            if (s == 2) { _circle.Fill = Brush(0xE0, 0x17, 0x7B, 0x4D); _label.Foreground = Brush(0xFF, 0x7C, 0xFC, 0x9E); _label.Text = "HDR"; }
            else if (s == 1) { _circle.Fill = Brush(0xE0, 0x33, 0x33, 0x33); _label.Foreground = Brush(0xFF, 0xAA, 0xAA, 0xAA); _label.Text = "HDR"; }
            else { _circle.Fill = Brush(0xE0, 0x6B, 0x4A, 0x00); _label.Foreground = Brush(0xFF, 0xFF, 0xD2, 0x4A); _label.Text = "?"; }
        }

        private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush b2 = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            b2.Freeze();
            return b2;
        }

        private static Border Sep()
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(10, 3, 10, 3),
                Background = Brush(0x26, 0xFF, 0xFF, 0xFF),
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
