# -*- coding: utf-8 -*-
# 修复：托盘左键误藏窗口 + -quit 关不掉隐藏窗口 + 第二实例唤醒 + 托盘钉任务栏
import io, os

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "hdr-toggle.cs")
s = io.open(p, encoding="utf-8").read()

# 0) -quit 也要能关隐藏窗口（去掉 IsWindowVisible 过滤）
old = '''        public static bool CloseRunningWidget()
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
        }'''
new = '''        public static bool CloseRunningWidget()
        {
            // 不筛可见性：窗口可能处于托盘隐藏状态，也要能被 -quit 关掉
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.ToString() == "HDR Toggle") { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }'''
assert old in s, "CloseRunningWidget not found"
s = s.replace(old, new)

# 1) RunGui：第二实例发唤醒信号；把信号量传给 Widget
old = '''        static int RunGui(bool openSettings, string popupDebug)
        {
            _mutex = new Mutex(false, "Local\\\\HdrToggleWidget");   // 单实例
            if (!_mutex.WaitOne(0)) return 0;
            Widget w = new Widget();
            w.Show();'''
new = '''        static int RunGui(bool openSettings, string popupDebug)
        {
            _mutex = new Mutex(false, "Local\\\\HdrToggleWidget");   // 单实例
            bool owned;
            try { owned = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                // 已有实例在跑（窗口可能被藏了）：发信号让老实例把窗口亮出来
                EventWaitHandle show;
                if (EventWaitHandle.TryOpenExisting("Local\\\\HdrToggleWidgetShow", out show))
                {
                    try { show.Set(); } catch { } finally { show.Dispose(); }
                }
                return 0;
            }
            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\\\HdrToggleWidgetShow");
            Widget w = new Widget(showEvent);
            w.Show();'''
assert old in s, "RunGui not found"
s = s.replace(old, new)

# 2) Widget 构造收信号量 + 监听线程 + 托盘钉任务栏
old = '''        public Widget()
        {
            Title = "HDR Toggle";'''
new = '''        public Widget(EventWaitHandle showEvent)
        {
            Title = "HDR Toggle";'''
assert old in s, "Widget ctor not found"
s = s.replace(old, new)

old = "            SetupTray();\n        }\n\n        public void Start()"
new = '''            SetupTray();
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
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Control Panel\\\\NotifyIconSettings", true))
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

        public void Start()'''
assert old in s, "SetupTray anchor not found"
s = s.replace(old, new)

# 3) 托盘左键：只显示/带回前台，不再隐藏（防误藏）；隐藏走菜单
old = '''                _tray.MouseUp += delegate(object sender, WF.MouseEventArgs e)
                {
                    if (e.Button == WF.MouseButtons.Left) ToggleVisible();
                };'''
new = '''                _tray.MouseUp += delegate(object sender, WF.MouseEventArgs e)
                {
                    // 左键只把窗口亮出来（含带回前台）；藏窗口走右键菜单，防止误点后找不到
                    if (e.Button != WF.MouseButtons.Left) return;
                    if (Visibility != Visibility.Visible) ToggleVisible();
                    else { Show(); WindowState = WindowState.Normal; }
                };'''
assert old in s, "tray mouse not found"
s = s.replace(old, new)

io.open(p, "w", encoding="utf-8").write(s)
print("patched ok")
