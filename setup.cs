// setup.cs — Agent HUD 安装向导（WinForms，内嵌 hdr-toggle.exe 与 assets，C#5 / 内置 csc）
// 用法:
//   HDR开关安装器.exe                 打开安装向导（选路径、勾选项、安装）
//   HDR开关安装器.exe /uninstall      卸载（带确认框；同时清除快捷方式/自启/注册表卸载项）
//   HDR开关安装器.exe /uninstall /quiet   静默卸载（无任何对话框，供自动化）
// 行为:
//   - 默认装到 %LOCALAPPDATA%\Programs\Agent HUD（per-user，免管理员），可选任意路径
//   - 桌面/开始菜单快捷方式、控制面板“应用和功能”卸载入口（HKCU Uninstall 键）
//   - 开机自启默认关闭；安装时自动结束正在运行的旧版小窗并清理旧版目录

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Agent HUD 安装向导")]
[assembly: AssemblyProduct("Agent HUD")]
[assembly: AssemblyDescription("Agent HUD 安装向导")]
[assembly: AssemblyCompany("Agent HUD Project")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Agent HUD Project")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

static class Setup
{
    public const string AppName = "Agent HUD";
    public const string Publisher = "Agent HUD Project";
    public const string AppVersion = "1.1.0.0";
    public const string DirName = "Agent HUD";
    public const string ExeName = "agent-hud.exe";
    public const string DesktopLnkName = "Agent HUD.lnk";
    public const string RunValueName = "Agent HUD";
    public const string UninstallKeyName = "{D4A61F02-8C7B-4E17-9A34-1A2B3C4D5E6F}";

    // 命名避开 System.Windows.Forms.MessageBox（向导里用的是它的 Show 实例方法）
    [DllImport("user32.dll")] static extern int MessageBoxNative(IntPtr hWnd, string text, string caption, int type);

    public static string InstallDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", DirName);
    }

    public static string LegacyDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirName);
    }

    public static string UninstallKeyPath()
    {
        return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + UninstallKeyName;
    }

    public static string ShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopLnkName);
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool uninstall = false, quiet = false;
        foreach (string a in args)
        {
            string l = a.TrimStart('-', '/').ToLowerInvariant();
            if (l == "uninstall") uninstall = true;
            if (l == "quiet") quiet = true;
        }
        if (uninstall) return RunUninstall(quiet);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new WizardForm());
        return 0;
    }

    // ---------- 公共步骤 ----------
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);

    // 关闭正在运行的小窗（ps1 版与 exe 版窗口标题同为 HDR Toggle）
    public static void CloseRunningWidget()
    {
        EnumWindows(delegate(IntPtr h, IntPtr lp)
        {
            if (IsWindowVisible(h))
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.ToString() == "Agent HUD" || sb.ToString() == "HDR Toggle") PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }

    public static void KillAppProcesses()
    {
        foreach (string nm in new string[] { ExeName.Replace(".exe", ""), "hdr-toggle" })
        foreach (Process p in Process.GetProcessesByName(nm))
        {
            try { p.Kill(); } catch { }
        }
    }

    public static void ExtractApp(string dir)
    {
        Directory.CreateDirectory(dir);
        string exePath = Path.Combine(dir, ExeName);
        using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("HDR.Toggle.app.exe"))
        {
            if (src == null) throw new IOException("内嵌资源缺失（打包错误）");
            using (FileStream dst = File.Create(exePath))
            {
                src.CopyTo(dst);
            }
        }
        ExtractAssets(dir);
    }

    // 释放 assets 文件夹（动画精灵图与角色静态图，资源名以 Asset. 为前缀）
    public static void ExtractAssets(string dir)
    {
        string aDir = Path.Combine(dir, "assets");
        Directory.CreateDirectory(aDir);
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (string res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("Asset.")) continue;
            string file = res.Substring("Asset.".Length);
            using (Stream src = asm.GetManifestResourceStream(res))
            {
                if (src == null) continue;
                using (FileStream dst = File.Create(Path.Combine(aDir, file)))
                {
                    src.CopyTo(dst);
                }
            }
        }
    }

    public static void CreateShortcut(string lnkPath, string targetPath, string arguments, string description)
    {
        if (File.Exists(lnkPath)) File.Delete(lnkPath);   // 重建，避免继承旧 lnk 的残留参数
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = targetPath;
        lnk.Arguments = arguments;
        lnk.WorkingDirectory = Path.GetDirectoryName(targetPath);
        lnk.IconLocation = (targetPath.ToLowerInvariant().EndsWith(".exe") ? targetPath : Path.Combine(InstallDir(), ExeName)) + ",0";
        lnk.Description = description;
        lnk.Save();
    }

    public static void WriteRegistry(string dir, bool autostart)
    {
        string exePath = Path.Combine(dir, ExeName);
        string uninstallerPath = Path.Combine(dir, "uninstall.exe");
        long estKb = 0;
        if (File.Exists(exePath)) estKb += new FileInfo(exePath).Length / 1024;
        if (File.Exists(uninstallerPath)) estKb += new FileInfo(uninstallerPath).Length / 1024;

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath()))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", AppVersion);
            k.SetValue("Publisher", Publisher);
            k.SetValue("DisplayIcon", exePath);
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + uninstallerPath + "\" /uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (estKb > 0) k.SetValue("EstimatedSize", (int)estKb, RegistryValueKind.DWord);
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            if (autostart) k.SetValue(RunValueName, "\"" + exePath + "\"");
            else { try { k.DeleteValue(RunValueName, false); } catch { } }
        }
    }

    // ---------- 卸载 ----------
    static int RunUninstall(bool quiet)
    {
        if (!quiet)
        {
            int r = MessageBoxNative(IntPtr.Zero,
                "确定要卸载 " + AppName + " 吗？\n\n小窗设置（悬浮位置）将一并删除。",
                "卸载 " + AppName, 0x24 /* YESNO|ICONQUESTION */);
            if (r != 6) return 0;   // IDYES=6
        }

        string dir = InstalledLocationOrFallback();
        CloseRunningWidget();
        KillAppProcesses();
        Thread.Sleep(500);

        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (k != null) { try { k.DeleteValue(RunValueName, false); } catch { } }
        }
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath(), false))
        {
            // 卸载目录以注册表登记为准（兼容自定义路径），找不到再回退默认
            if (k != null)
            {
                string loc = (k.GetValue("InstallLocation") as string);
                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) dir = loc;
            }
        }
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath(), true))
        {
            if (k != null) { try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath()); } catch { } }
        }

        try { if (File.Exists(ShortcutPath())) File.Delete(ShortcutPath()); } catch { }
        try
        {
            string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", DirName);
            if (Directory.Exists(sm)) Directory.Delete(sm, true);
        }
        catch { }

        // 自删除：uninstall.exe 在被删目录内，先退出再由隐藏的 cmd 延迟删除
        if (!quiet) MessageBoxNative(IntPtr.Zero, AppName + " 已卸载。", "卸载 " + AppName, 0x40);
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c timeout /t 2 /nobreak > nul & rd /s /q \"" + dir + "\"";
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch { }
        return 0;
    }

    static string InstalledLocationOrFallback()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath()))
        {
            if (k != null)
            {
                string loc = k.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(loc)) return loc;
            }
        }
        return InstallDir();
    }
}

// ---------- 安装向导窗体 ----------
public class WizardForm : Form
{
    private TextBox txtPath;
    private CheckBox chkDesktop, chkStartMenu, chkAutostart, chkRun;
    private ProgressBar prog;
    private Label lblStatus;
    private Button btnInstall;
    private Button btnBrowse;
    private Label lblInfo;
    private bool _done;

    public WizardForm()
    {
        Text = "Agent HUD 安装向导";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 400);
        Font = new Font("Microsoft YaHei UI", 9F);

        Label lblTitle = new Label();
        lblTitle.Text = "欢迎使用 " + Setup.AppName + " 安装向导";
        lblTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        lblTitle.Location = new Point(20, 18);
        lblTitle.Size = new Size(520, 32);
        Controls.Add(lblTitle);

        lblInfo = new Label();
        lblInfo.ForeColor = SystemColors.GrayText;
        lblInfo.Location = new Point(22, 56);
        lblInfo.Size = new Size(520, 40);
        Controls.Add(lblInfo);

        GroupBox grp = new GroupBox();
        grp.Text = "安装选项";
        grp.Location = new Point(20, 110);
        grp.Size = new Size(520, 150);
        Controls.Add(grp);

        Label lblPath = new Label();
        lblPath.Text = "安装路径：";
        lblPath.Location = new Point(18, 32);
        lblPath.Size = new Size(70, 22);
        grp.Controls.Add(lblPath);

        txtPath = new TextBox();
        txtPath.Location = new Point(90, 29);
        txtPath.Size = new Size(330, 24);
        grp.Controls.Add(txtPath);

        btnBrowse = new Button();
        btnBrowse.Text = "浏览...";
        btnBrowse.Location = new Point(430, 28);
        btnBrowse.Size = new Size(75, 25);
        btnBrowse.Click += delegate
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 " + Setup.AppName + " 的安装目录";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPath.Text = dlg.SelectedPath;
            }
        };
        grp.Controls.Add(btnBrowse);

        chkDesktop = new CheckBox();
        chkDesktop.Text = "创建桌面快捷方式";
        chkDesktop.Location = new Point(20, 70);
        chkDesktop.Size = new Size(180, 22);
        chkDesktop.Checked = true;
        grp.Controls.Add(chkDesktop);

        chkStartMenu = new CheckBox();
        chkStartMenu.Text = "创建开始菜单快捷方式";
        chkStartMenu.Location = new Point(230, 70);
        chkStartMenu.Size = new Size(220, 22);
        chkStartMenu.Checked = true;
        grp.Controls.Add(chkStartMenu);

        chkRun = new CheckBox();
        chkRun.Text = "安装完成后立即运行";
        chkRun.Location = new Point(20, 100);
        chkRun.Size = new Size(180, 22);
        chkRun.Checked = true;
        grp.Controls.Add(chkRun);

        chkAutostart = new CheckBox();
        chkAutostart.Text = "开机自动启动";
        chkAutostart.Location = new Point(230, 100);
        chkAutostart.Size = new Size(220, 22);
        chkAutostart.Checked = false;
        grp.Controls.Add(chkAutostart);

        prog = new ProgressBar();
        prog.Location = new Point(20, 285);
        prog.Size = new Size(520, 22);
        prog.Visible = false;
        Controls.Add(prog);

        lblStatus = new Label();
        lblStatus.Location = new Point(22, 315);
        lblStatus.Size = new Size(520, 22);
        lblStatus.ForeColor = SystemColors.GrayText;
        Controls.Add(lblStatus);

        btnInstall = new Button();
        btnInstall.Text = "安装";
        btnInstall.Location = new Point(414, 350);
        btnInstall.Size = new Size(126, 32);
        btnInstall.Click += delegate { OnInstall(); };
        Controls.Add(btnInstall);

        AcceptButton = btnInstall;

        // 已安装则预填并提示覆盖升级
        string existing = GetInstalledLocation();
        if (existing != null)
        {
            txtPath.Text = existing;
            lblInfo.Text = "检测到已安装，将继续覆盖升级。\n" + Setup.Publisher + " 发布 · v" + Setup.AppVersion + " · 免管理员";
        }
        else
        {
            txtPath.Text = Setup.InstallDir();
            lblInfo.Text = "一键开关主显示器 HDR · v" + Setup.AppVersion + " · 免管理员安装\n" + Setup.Publisher + " 发布";
        }
    }

    static string GetInstalledLocation()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Setup.UninstallKeyPath()))
            {
                string loc = k == null ? null : (k.GetValue("InstallLocation") as string);
                return string.IsNullOrEmpty(loc) ? null : loc;
            }
        }
        catch { return null; }
    }

    private void Step(string text, int value)
    {
        lblStatus.Text = text;
        prog.Value = Math.Min(value, 100);
        Update();
    }

    private void OnInstall()
    {
        if (_done) { Close(); return; }

        string dir = txtPath.Text == null ? "" : txtPath.Text.Trim().TrimEnd('\\');
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "请填写安装路径。", Setup.AppName);
            return;
        }
        if (!Path.IsPathRooted(dir))
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        }

        try
        {
            btnInstall.Enabled = false;
            txtPath.Enabled = false;
            btnBrowse.Enabled = false;
            prog.Visible = true;

            Step("关闭正在运行的小窗…", 10);
            Setup.CloseRunningWidget();
            Setup.KillAppProcesses();
            Thread.Sleep(500);

            Step("释放程序文件…", 30);
            Directory.CreateDirectory(dir);
            Setup.ExtractApp(dir);
            try
            {
                // 自动配置：合并 ZCode hooks（通知/权限气泡）+ 探测双击启动路径
                var psi = new ProcessStartInfo(Path.Combine(dir, "agent-hud.exe"), "-install-hooks")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                Process.Start(psi).WaitForExit(15000);
            }
            catch { }
            string self = Process.GetCurrentProcess().MainModule.FileName;
            File.Copy(self, Path.Combine(dir, "uninstall.exe"), true);

            Step("创建快捷方式…", 55);
            string exePath = Path.Combine(dir, Setup.ExeName);
            string uninstaller = Path.Combine(dir, "uninstall.exe");
            if (chkDesktop.Checked) Setup.CreateShortcut(Setup.ShortcutPath(), exePath, "", "一键开关主显示器 HDR");
            if (chkStartMenu.Checked)
            {
                string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", Setup.DirName);
                Directory.CreateDirectory(sm);
                Setup.CreateShortcut(Path.Combine(sm, Setup.DesktopLnkName), exePath, "", "一键开关主显示器 HDR");
                Setup.CreateShortcut(Path.Combine(sm, "卸载 " + Setup.AppName + ".lnk"), uninstaller, "/uninstall", "卸载 " + Setup.AppName);
            }

            Step("写入注册表…", 75);
            Setup.WriteRegistry(dir, chkAutostart.Checked);

            Step("清理旧版残留…", 88);
            string legacy = Setup.LegacyDir();
            if (string.Compare(legacy, dir, StringComparison.OrdinalIgnoreCase) != 0 && Directory.Exists(legacy))
            {
                try { Directory.Delete(legacy, true); } catch { }
            }

            if (chkRun.Checked)
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = dir });
            }

            Step("安装完成", 100);
            lblStatus.Text = "安装完成：" + dir;
            btnInstall.Text = "完成";
            btnInstall.Enabled = true;
            _done = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "安装失败：\n" + ex.Message, Setup.AppName);
            btnInstall.Enabled = true;
            txtPath.Enabled = true;
            btnBrowse.Enabled = true;
        }
    }
}
