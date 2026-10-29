# -*- coding: utf-8 -*-
# v2.2：DeepSeek key 存 Windows 凭据管理器（CredWrite/CredRead），ini 不再含密钥
#   - Load：从凭据管理器读取；发现旧 ini 明文 key 则自动迁移并从文件移除
#   - Save/SaveAndClose：key 写入凭据管理器
import io, os

p = 'agent-hud.cs'
s = io.open(p, encoding='utf-8').read()

# ---------- 1) SettingsData：凭据 P/Invoke + Get/Set ----------
old = '''    // ---------- 配置（agent-hud.ini） ----------'''
new = '''    // ---------- DeepSeek key：Windows 凭据管理器存储（ini 不落密钥） ----------
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

    // ---------- 配置（agent-hud.ini） ----------'''
assert old in s, "SettingsData anchor not found"
s = s.replace(old, new)

# ---------- 2) Load：key 从凭据读取 + 旧 ini 明文自动迁移 ----------
old = '''            catch { }
            return d;
        }'''
new = '''            catch { }
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
        }'''
assert old in s, "Load tail not found"
s = s.replace(old, new)

# ---------- 3) Save：不再写 key 进 ini ----------
old = '''            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Agent HUD 配置（可在设置页修改，或手动编辑）");
            sb.AppendLine("# DeepSeek API key: platform.deepseek.com 创建");
            sb.AppendLine("deepseek_key=" + d["deepseek_key"]);
            sb.AppendLine("deepseek_base=" + d["deepseek_base"]);'''
new = '''            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Agent HUD 配置（可在设置页修改，或手动编辑）");
            sb.AppendLine("# DeepSeek API key 存放在 Windows 凭据管理器（设置页可修改），不落本文件");
            sb.AppendLine("deepseek_base=" + d["deepseek_base"]);'''
assert old in s, "Save key line not found"
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8').write(s)
print('settingsdata ok')

# ---------- SettingsWindow.SaveAndClose：key 写凭据 ----------
p = 'agent-hud.cs'
s = io.open(p, encoding='utf-8').read()
old = '''            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);
            d["deepseek_key"] = _chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password;
            d["deepseek_base"] = string.IsNullOrWhiteSpace(_txtDsBase.Text) ? "https://api.deepseek.com" : _txtDsBase.Text.Trim();'''
new = '''            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);
            SettingsData.SetSecret(_chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password);
            d["deepseek_key"] = SettingsData.GetSecret();
            d["deepseek_base"] = string.IsNullOrWhiteSpace(_txtDsBase.Text) ? "https://api.deepseek.com" : _txtDsBase.Text.Trim();'''
assert old in s, "SaveAndClose not found"
s = s.replace(old, new)

# 设置页打开时从凭据填充（ctor 用 _cfg["deepseek_key"]，Load 已从凭据读取，无需改）

# ---------- 版本号 ----------
old = '[assembly: AssemblyVersion("2.1.0.0")]'
new = '[assembly: AssemblyVersion("2.2.0.0")]'
assert old in s
s = s.replace(old, new)
old = '[assembly: AssemblyFileVersion("2.1.0.0")]'
new = '[assembly: AssemblyFileVersion("2.2.0.0")]'
assert old in s
s = s.replace(old, new)
old = 'Text = "Agent HUD v2.1 · HDR 开关 + DeepSeek 余额 + GLM 订阅用量",'
new = 'Text = "Agent HUD v2.2 · HDR 开关 + DeepSeek 余额 + GLM 订阅用量",'
assert old in s
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8').write(s)
print('settings window ok')
