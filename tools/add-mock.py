# -*- coding: utf-8 -*-
# v1.6.4：-mockcycle 调试模式——合成数据轮播三种状态，全链路验收动画/双点/圈/余额
import io, os

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "hdr-toggle.cs")
s = io.open(p, encoding="utf-8").read()

# 1) CLI 入口
old = '''                    if (cmd == "popup:ds" || cmd == "popup:zc") return RunGui(false, cmd.Substring(6));
                    if (cmd == "settings") return RunGui(true, null);'''
new = '''                    if (cmd == "popup:ds" || cmd == "popup:zc") return RunGui(false, cmd.Substring(6));
                    if (cmd == "settings") return RunGui(true, null);
                    if (cmd == "mockcycle") return RunGui(false, null, true);'''
assert old in s, "cli not found"
s = s.replace(old, new)

# 2) RunGui 传递 mock 标记
old = '''        static int RunGui(bool openSettings, string popupDebug)
        {'''
new = '''        static int RunGui(bool openSettings, string popupDebug, bool mockCycle = false)
        {'''
assert old in s, "RunGui sig not found"
s = s.replace(old, new)

old = '''            Widget w = new Widget(showEvent);
            w.Show();'''
new = '''            Widget w = new Widget(showEvent);
            w.MockCycle = mockCycle;
            w.Show();'''
assert old in s, "RunGui body not found"
s = s.replace(old, new)

# 3) Widget 字段
old = '''        private int _dfMoodNow = -1, _zcMoodNow = -1;'''
new = '''        private int _dfMoodNow = -1, _zcMoodNow = -1;
        public bool MockCycle;          // 调试：合成数据轮播三种状态，不联网
        private int _mockMood = -1;
        private DispatcherTimer _mockTimer;'''
assert old in s, "mock fields not found"
s = s.replace(old, new)

# 4) Start：mock 模式走合成数据 + 轮播时钟；真实刷新全部短路
old = '''        public void Start()
        {
            UpdateVisual(false);
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };'''
new = '''        public void Start()
        {
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
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };'''
assert old in s, "Start not found"
s = s.replace(old, new)

# 5) 数据定时器与悬停补拉在 mock 模式下短路
old = '''            _dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _dataTimer.Tick += delegate
            {
                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();
            };'''
new = '''            _dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _dataTimer.Tick += delegate
            {
                if (MockCycle) return;
                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();
            };'''
assert old in s, "dataTimer not found"
s = s.replace(old, new)

old = '''                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if ((DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();   // 弹出时顺带补数据
            };'''
new = '''                double mins = SettingsData.Num(_cfg, "refresh_minutes", 5);
                if (!MockCycle && (DateTime.Now - _lastFetch).TotalMinutes >= mins) RefreshData();   // 弹出时顺带补数据
            };'''
assert old in s, "mouseEnter fetch not found"
s = s.replace(old, new)

# 6) ApplyMock：三步合成数据（5时/本周刻意不同档，验证双点独立分档）
old = '''        // ---------- HDR 双击确认（防误触） ----------'''
new = '''        // ---------- 调试：合成数据轮播 ----------
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

        // ---------- HDR 双击确认（防误触） ----------'''
assert old in s, "hdr confirm anchor not found"
s = s.replace(old, new)

# 7) usage 字符串
old = 'Out("usage: hdr-toggle.exe [-state|-toggle|-on|-off|-quit|-debugdata|-settings|-popup:ds|-popup:zc]");'
new = 'Out("usage: hdr-toggle.exe [-state|-toggle|-on|-off|-quit|-debugdata|-settings|-popup:ds|-popup:zc|-mockcycle]");'
assert old in s, "usage not found"
s = s.replace(old, new)

io.open(p, "w", encoding="utf-8").write(s)
print("patched ok")
