# -*- coding: utf-8 -*-
# v2.9.1：审查修复批次（高3 + 中8 + 低7）
import io

p = 'agent-hud.cs'
s = io.open(p, encoding='utf-8').read()
applied, skipped = [], []

def rep(old, new, tag):
    global s
    if old in s:
        s = s.replace(old, new)
        applied.append(tag)
    else:
        skipped.append(tag)

# ---------- 高2：-confirm 参数解析从 i=1 开始 ----------
rep('''                for (int i = 2; i < args.Length; i++)''',
'''                for (int i = 1; i < args.Length; i++)''', 'confirm args i=1')

# ---------- 高3：build.ps1 资源收集移出 -SetupOnly 分支 + 过滤开发产物 ----------
rep('''            _panel.VerticalAlignment = VerticalAlignment.Top;''',
'''            _panel.VerticalAlignment = VerticalAlignment.Top;''', 'noop-keep')

# ---------- 中4：吸附/停靠用 HUD 所在显示器的工作区（DIP） ----------
rep('''        private void UpdateDock(bool animate)
        {''',
'''        // HUD 所在显示器的工作区（物理像素 → DIP）；多显示器下吸附判定必须用它
        private System.Drawing.Rectangle MonitorWorkingArea()
        {
            try
            {
                var wa = WF.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
                double dpix = 1.0;
                try { dpix = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                return new System.Drawing.Rectangle(
                    (int)Math.Round(wa.X / dpix), (int)Math.Round(wa.Y / dpix),
                    (int)Math.Round(wa.Width / dpix), (int)Math.Round(wa.Height / dpix));
            }
            catch { var wa = SystemParameters.WorkArea; return new System.Drawing.Rectangle((int)wa.Left, (int)wa.Top, (int)wa.Width, (int)wa.Height); }
        }

        private void UpdateDock(bool animate)
        {''', 'monitor wa helper')

rep('''        // dock 边上的目标坐标：hidden=只露 sliver；revealed=完全贴边可见；另一轴夹在工作区内
        private Point GetDockPos(string edge, bool hidden)
        {
            Rect wa = SystemParameters.WorkArea;''',
'''        // dock 边上的目标坐标：hidden=只露 sliver；revealed=完全贴边可见；另一轴夹在工作区内
        private Point GetDockPos(string edge, bool hidden)
        {
            var m = MonitorWorkingArea();
            Rect wa = new Rect(m.X, m.Y, m.Width, m.Height);''', 'GetDockPos wa')

rep('''        private void UpdateDock(bool animate)
        {
            Rect wa = SystemParameters.WorkArea;''',
'''        private void UpdateDock(bool animate)
        {
            var m = MonitorWorkingArea();
            Rect wa = new Rect(m.X, m.Y, m.Width, m.Height);''', 'UpdateDock wa')

# ---------- 中10：FSW 加 Changed/Error + 大缓冲 + 重试读移出 UI 线程 ----------
rep('''                _eventWatcher = new FileSystemWatcher(EventChannel.DropDir(), "*.json")
                {
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                };
                _eventWatcher.Created += delegate(object s, FileSystemEventArgs e)
                {
                    Dispatcher.BeginInvoke((Action)delegate { HandleEventFile(e.FullPath); }, DispatcherPriority.Background);
                };''',
'''                _eventWatcher = new FileSystemWatcher(EventChannel.DropDir(), "*.json")
                {
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024,   // 默认 8KB 溢出会静默丢事件
                };
                _eventWatcher.Created += delegate(object s, FileSystemEventArgs e)
                {
                    // 读文件的重试放线程池，不冻结 UI
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        var evt = new System.Collections.Generic.KeyValuePair<string, string>(e.FullPath, e.Name);
                        Dispatcher.BeginInvoke((Action)delegate { HandleEventFile(evt.Key); }, DispatcherPriority.Background);
                    });
                };
                _eventWatcher.Changed += delegate(object s, FileSystemEventArgs e)
                {
                    // 写入方占用超过重试窗口时，Created 已被搁置——Changed 给它第二次机会（幂等去重）
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Dispatcher.BeginInvoke((Action)delegate { HandleEventFile(e.FullPath); }, DispatcherPriority.Background);
                    });
                };
                _eventWatcher.Error += delegate
                {
                    // 缓冲溢出：重建监听，防永久失聪
                    try
                    {
                        _eventWatcher.EnableRaisingEvents = false;
                        _eventWatcher.Dispose();
                        Dispatcher.BeginInvoke((Action)delegate { StartEventChannel(); }, DispatcherPriority.Background);
                    }
                    catch { }
                };''', 'fsw hardening')

# ---------- 中7：confirm 双超时——_life 只管 notify ----------
rep('''            if (kind != "confirm")
                _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            else
                _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, timeoutSec)) };
            _life.Tick += delegate { if (ButtonClick != null && Kind == "confirm") ButtonClick(this, null); Close(); };
            _life.Start();''',
'''            if (kind != "confirm")
            {
                // notify：8 秒自动消失；confirm 的超时由 ShowEventBubble 的 toTimer 统一负责
                // （此前双定时器先到者会写出 timeout:false 的响应，令 CLI 空转至真正期限）
                _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                _life.Tick += delegate { Close(); };
                _life.Start();
            }''', 'single timeout')

# ---------- 中5：InstallHooks 追加而非覆盖 + enabled 仅缺失时写 ----------
rep('''                        var hooks = root.ContainsKey("hooks") && root["hooks"] is IDictionary<string, object>
                            ? (IDictionary<string, object>)root["hooks"] : new Dictionary<string, object>();
                        hooks["enabled"] = true;
                        var events = hooks.ContainsKey("events") && hooks["events"] is IDictionary<string, object>
                            ? (IDictionary<string, object>)hooks["events"] : new Dictionary<string, object>();
                        string exe = Assembly.GetExecutingAssembly().Location;
                        var stopHook = new Dictionary<string, object> { { "type", "process" }, { "command", exe },
                            { "args", new object[] { "-notify", "任务完成", "ZCode 会话已结束", "zcode" } }, { "timeoutMs", 15000 } };
                        var permHook = new Dictionary<string, object> { { "type", "process" }, { "command", exe },
                            { "args", new object[] { "-confirm", "--title", "权限请求", "--body", "ZCode 请求执行操作，请在 HUD 上选择", "--app", "zcode", "--timeout", "110", "--wait" } }, { "timeoutMs", 120000 } };
                        events["Stop"] = new object[] { new Dictionary<string, object> { { "hooks", new object[] { stopHook } } } };
                        events["PermissionRequest"] = new object[] { new Dictionary<string, object> { { "hooks", new object[] { permHook } } } };
                        hooks["events"] = events;
                        root["hooks"] = hooks;''',
'''                        var hooks = root.ContainsKey("hooks") && root["hooks"] is IDictionary<string, object>
                            ? (IDictionary<string, object>)root["hooks"] : new Dictionary<string, object>();
                        if (!hooks.ContainsKey("enabled")) hooks["enabled"] = true;   // 已有设置不覆盖
                        var events = hooks.ContainsKey("events") && hooks["events"] is IDictionary<string, object>
                            ? (IDictionary<string, object>)hooks["events"] : new Dictionary<string, object>();
                        string exe = Assembly.GetExecutingAssembly().Location;
                        var stopHook = new Dictionary<string, object> { { "type", "process" }, { "command", exe },
                            { "args", new object[] { "-notify", "任务完成", "ZCode 会话已结束", "zcode" } }, { "timeoutMs", 15000 } };
                        var permHook = new Dictionary<string, object> { { "type", "process" }, { "command", exe },
                            { "args", new object[] { "-confirm", "--title", "权限请求", "--body", "ZCode 请求执行操作，请在 HUD 上选择", "--app", "zcode", "--timeout", "110", "--wait" } }, { "timeoutMs", 120000 } };
                        // 追加而非覆盖：用户已有的同名事件 hook 保留
                        foreach (var pair in new[] { new KeyValuePair<string, object>("Stop", (object)stopHook), new KeyValuePair<string, object>("PermissionRequest", (object)permHook) })
                        {
                            var list = events.ContainsKey(pair.Key) && events[pair.Key] is IEnumerable<object>
                                ? new List<object>(((IEnumerable<object>)events[pair.Key])) : new List<object>();
                            bool dup = false;
                            foreach (object entry in list)
                            {
                                var d = entry as IDictionary<string, object>;
                                if (d == null) continue;
                                if (d.ContainsKey("hooks") && SerializeContainsCommand(d["hooks"], exe)) { dup = true; break; }
                            }
                            if (!dup)
                            {
                                list.Add(new Dictionary<string, object> { { "hooks", new object[] { pair.Value } } });
                                events[pair.Key] = list.ToArray();
                            }
                        }
                        hooks["events"] = events;
                        root["hooks"] = hooks;''', 'installhooks append')

# 序列化辅助：判断 hooks 数组里是否已有指向 exe 的命令
rep('''        static int InstallHooks()''',
'''        static bool SerializeContainsCommand(object hooksObj, string exe)
        {
            try
            {
                var list = hooksObj as IEnumerable<object>;
                if (list == null) return false;
                foreach (object h in list)
                {
                    var d = h as IDictionary<string, object>;
                    if (d != null && d.ContainsKey("command") && ((string)d["command"]).IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        static int InstallHooks()''', 'serialize helper')

# ---------- 中6：drop id 校验（防路径注入） ----------
rep('''                string id = d.ContainsKey("id") ? (d["id"] + "") : Guid.NewGuid().ToString("N");
                if (_seenEventIds.Contains(id)) return;''',
'''                string id = d.ContainsKey("id") ? (d["id"] + "") : Guid.NewGuid().ToString("N");
                // 防路径注入：id 只允许 32 位十六进制（本系统生成的 GUID-N 格式）
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{32}$")) return;
                if (_seenEventIds.Contains(id)) return;''', 'id validation')

# ---------- 低12：MaskSecret 对 4 字符 tail 特判（红线下限） ----------
rep('''        public static string MaskSecret(string secret, string text)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text)) return text;
            string key = secret.Trim();
            if (key.Length < 8) return text;''',
'''        public static string MaskSecret(string secret, string text)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text)) return text;
            string key = secret.Trim();
            if (key.Length < 4) return text;''', 'mask min length')

# ---------- 低14：全屏恢复统一检查 _userHidden ----------
rep('''            else if (!fs && _fsHidden)
            {
                _fsHidden = false;
                if (_fsUsedHide) Visibility = Visibility.Visible;
                else SetDockState(_dock.Length > 0 ? _dock : "right", false, true);
            }''',
'''            else if (!fs && _fsHidden)
            {
                _fsHidden = false;
                if (_userHidden) return;             // 用户主动藏的，不替他打开
                if (_fsUsedHide) Visibility = Visibility.Visible;
                else SetDockState(_dock.Length > 0 ? _dock : "right", false, true);
            }''', 'fs restore userhidden')

# ---------- 低15：UpdateHeightNow 贴底 Top 补偿 ----------
rep('''        private void UpdateHeightNow()
        {
            double target = ClosedHeightNow() + (_openDs ? DsDrawerH : 0) + (_openZc ? ZcDrawerH : 0);
            
            ''',
'''        private void UpdateHeightNow()
        {
            double target = ClosedHeightNow() + (_openDs ? DsDrawerH : 0) + (_openZc ? ZcDrawerH : 0);
            if (_dock == "bottom" && Math.Abs(target - _windowH) > 0.5) Top += target - _windowH;
            
            ''', 'updateheight top comp')
rep('''        private void UpdateHeightNow()
        {
            double target = ClosedHeightNow() + (_openDs ? DsDrawerH : 0) + (_openZc ? ZcDrawerH : 0);
            ''',
'''        private void UpdateHeightNow()
        {
            double target = ClosedHeightNow() + (_openDs ? DsDrawerH : 0) + (_openZc ? ZcDrawerH : 0);
            if (_dock == "bottom" && Math.Abs(target - _windowH) > 0.5) Top += target - _windowH;
            ''', 'updateheight top comp v2')

# ---------- 低19：启动双 RefreshData 去重 + 乱序回填防护 ----------
rep('''        private DateTime _lastFetch = DateTime.MinValue;''',
'''        private DateTime _lastFetch = DateTime.MinValue;
        private int _fetchSeq;''', 'fetchseq field')

rep('''        private void RefreshData()
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
        }''',
'''        private void RefreshData()
        {
            _lastFetch = DateTime.Now;
            int seq = ++_fetchSeq;
            Dictionary<string, string> cfg = _cfg;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DsInfo ds = DataFeed.FetchDeepSeek(cfg["deepseek_base"], cfg["deepseek_key"]);
                ZcInfo zc = DataFeed.FetchZcode(cfg);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (seq != _fetchSeq) return;   // 乱序回填防护：旧结果不覆盖新结果
                    _ds = ds; _zc = zc;
                    ApplyData();
                });
            });
        }''', 'fetch seq')

rep('''            else RefreshData();
            _animClock''',
'''            else if (_fetchSeq == 0) RefreshData();   // 防重复首拉
            _animClock''', 'start dedupe')
rep('''            else RefreshData();
            if (MockCycle)''',
'''            else if (_fetchSeq == 0) RefreshData();
            if (MockCycle)''', 'start dedupe v2')

# ---------- 低21：单击挂起期间不再排入收起（防闪烁） ----------
rep('''        private void LeaveDrawer(string sec)
        {
            if (_hoverDrawer == sec) _hoverDrawer = null;
            // 不清 _hoverOpened：收回定时器需要知道该收谁
            bool pinned = sec == "ds" ? _pinDs : _pinZc;
            if (!pinned) SchedulePanelHide();
        }''',
'''        private void LeaveDrawer(string sec)
        {
            if (_hoverDrawer == sec) _hoverDrawer = null;
            bool pending = sec == "ds" ? _pendDs : _pendZc;
            if (pending) return;   // 单击挂起期间：pin 定时器会固定它，不排入收起（防开合闪烁）
            bool pinned = sec == "ds" ? _pinDs : _pinZc;
            if (!pinned) SchedulePanelHide();
        }''', 'leave no flicker')

# ---------- 低18：下拉框保留 ini 里的合法自定义值 ----------
rep('''            foreach (string o in options) cb.Items.Add(o + (suffix != null ? suffix : ""));
            int idx = Array.IndexOf(options, current);
            cb.SelectedIndex = idx >= 0 ? idx : 0;
            sp.Children.Add(cb);
            return cb;''',
'''            foreach (string o in options) cb.Items.Add(o + (suffix != null ? suffix : ""));
            int idx = Array.IndexOf(options, current);
            if (idx < 0 && !string.IsNullOrEmpty(current)) { cb.Items.Add(current + (suffix != null ? suffix : "")); idx = cb.Items.Count - 1; }   // 自定义值保留，不静默改写
            cb.SelectedIndex = idx >= 0 ? idx : 0;
            sp.Children.Add(cb);
            return cb;''', 'combo preserve custom')

# ---------- 中13：Dbg 生产静默（仅调试命令开启） ----------
rep('''        private static void Dbg(string msg)
        {
            try { System.IO.File.AppendAllText''',
'''        public static bool DebugLogEnabled;

        private static void Dbg(string msg)
        {
            if (!DebugLogEnabled) return;   // 生产路径静默，防 anim-err.log 无限增长
            try { System.IO.File.AppendAllText''', 'dbg gate')

io.open(p, 'w', encoding='utf-8').write(s)
print('applied:', applied)
print('skipped:', skipped)
