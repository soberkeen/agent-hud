# -*- coding: utf-8 -*-
# v1.7：GLM 表情驱动源可选（5时为主/周为主/取最差）+ 角色显示配置（GLM/DeepSeek 各自开关）
import io, os

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "hdr-toggle.cs")
s = io.open(p, encoding="utf-8").read()

# ---------- 1) 默认配置 ----------
old = '''            d["warn_low"] = "20";                    // 剩余 <20% 红
            d["warn_mid"] = "50";                    // 剩余 <50% 黄
            d["hover_details"] = "true";             // 悬停弹出详情
            d["show_tray"] = "true";                 // 托盘图标'''
new = '''            d["warn_low"] = "20";                    // 剩余 <20% 红
            d["warn_mid"] = "50";                    // 剩余 <50% 黄
            d["hover_details"] = "true";             // 悬停弹出详情
            d["show_tray"] = "true";                 // 托盘图标
            d["zc_mood_source"] = "five";            // GLM 表情驱动：five=5小时 / week=周 / worst=取最差
            d["show_ds"] = "true";                   // 显示 DeepSeek 角色
            d["show_zc"] = "true";                   // 显示 GLM 角色'''
assert old in s, "defaults not found"
s = s.replace(old, new)

# ---------- 2) Save 全量写盘 ----------
old = '''            sb.AppendLine("# 悬停图标弹出详情面板");
            sb.AppendLine("hover_details=" + d["hover_details"]);
            sb.AppendLine("# 显示托盘图标");
            sb.AppendLine("show_tray=" + d["show_tray"]);'''
new = '''            sb.AppendLine("# 悬停图标弹出详情面板");
            sb.AppendLine("hover_details=" + d["hover_details"]);
            sb.AppendLine("# 显示托盘图标");
            sb.AppendLine("show_tray=" + d["show_tray"]);
            sb.AppendLine("# GLM 角色表情由哪个额度驱动：five=5小时 / week=本周 / worst=取更差的一个");
            sb.AppendLine("zc_mood_source=" + d["zc_mood_source"]);
            sb.AppendLine("# 显示哪些角色（只看一个或都不要都可以）");
            sb.AppendLine("show_ds=" + d["show_ds"]);
            sb.AppendLine("show_zc=" + d["show_zc"]);'''
assert old in s, "save not found"
s = s.replace(old, new)

# ---------- 3) Widget：布局时按配置决定是否创建角色行 ----------
old = '''            // ---- DeepSeek 行 + 抽屉（角色下方滑出纯余额） ----
            _dsRow = IconRow("ds");'''
new = '''            // ---- DeepSeek 行 + 抽屉（show_ds=false 则整行不创建） ----
            bool showDs = SettingsData.Flag(_cfg, "show_ds");
            _dsRow = IconRow("ds");'''
assert old in s, "ds row cfg not found"
s = s.replace(old, new)

old = '''            _dsRow.ToolTip = "DeepSeek 余额（悬停展开，点击固定/收起）";
            stack.Children.Add(_dsRow);

            _dsSlide = new TranslateTransform();'''
new = '''            _dsRow.ToolTip = "DeepSeek 余额（悬停展开，点击固定/收起）";
            stack.Children.Add(_dsRow);
            if (!showDs) _dsRow.Visibility = Visibility.Collapsed;'''
assert old in s, "ds row visibility not found"
s = s.replace(old, new)

# 分隔线跟随：DS 抽屉后面的 sep 与 ZC 行/抽屉
old = '''            // ---- ZCode 行 + 抽屉（角色下方滑出双圈） ----
            _zcRow = IconRow("zc");'''
new = '''            // ---- ZCode 行 + 抽屉（show_zc=false 则整行不创建） ----
            bool showZc = SettingsData.Flag(_cfg, "show_zc");
            _zcRow = IconRow("zc");'''
assert old in s, "zc row cfg not found"
s = s.replace(old, new)

old = '''            _zcRow.ToolTip = "ZCode 订阅用量（悬停展开，点击固定/收起）";
            stack.Children.Add(_zcRow);'''
new = '''            _zcRow.ToolTip = "ZCode 订阅用量（悬停展开，点击固定/收起）";
            stack.Children.Add(_zcRow);
            if (!showZc) _zcRow.Visibility = Visibility.Collapsed;'''
assert old in s, "zc row visibility not found"
s = s.replace(old, new)

# 分隔线也要跟着隐藏：找出创建时的引用
old = '''            stack.Children.Add(_dsDrawer);

            stack.Children.Add(Sep());

            // ---- ZCode 行'''
new = '''            stack.Children.Add(_dsDrawer);

            Border sepMid = Sep();
            stack.Children.Add(sepMid);

            // ---- ZCode 行'''
assert old in s, "sep mid not found"
s = s.replace(old, new)

old = '''            stack.Children.Add(_zcDrawer);

            Border pad = new Border { Height = 6, IsHitTestVisible = false };'''
new = '''            stack.Children.Add(_zcDrawer);

            Border pad = new Border { Height = 6, IsHitTestVisible = false };'''
assert old in s, "pad anchor not found"
s = s.replace(old, new)

# ApplyConfigChanged 同步可见性（配置改了立即生效；分隔线用 Tag 关联）
old = '''        // 设置保存后：重读配置、同步托盘、立刻刷新数据
        public void ApplyConfigChanged()
        {
            _cfg = SettingsData.Load();
            if (_tray != null) _tray.Visible = SettingsData.Flag(_cfg, "show_tray");
            RefreshData();
        }'''
new = '''        // 设置保存后：重读配置、同步托盘/角色可见性、立刻刷新数据
        public void ApplyConfigChanged()
        {
            _cfg = SettingsData.Load();
            if (_tray != null) _tray.Visible = SettingsData.Flag(_cfg, "show_tray");
            if (_dsRow != null) _dsRow.Visibility = SettingsData.Flag(_cfg, "show_ds") ? Visibility.Visible : Visibility.Collapsed;
            if (_zcRow != null) _zcRow.Visibility = SettingsData.Flag(_cfg, "show_zc") ? Visibility.Visible : Visibility.Collapsed;
            if (_sepMid != null) _sepMid.Visibility = (_dsRow != null && _dsRow.Visibility == Visibility.Visible && _zcRow != null && _zcRow.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            if (_sepTop != null) _sepTop.Visibility = (_dsRow != null && _dsRow.Visibility == Visibility.Visible && SettingsData.Flag(_cfg, "show_ds")) ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeightNow();
            RefreshData();
        }'''
assert old in s, "ApplyConfigChanged not found"
s = s.replace(old, new)

# ---------- 4) 分隔线字段化 ----------
old = '''        private Ellipse _dsDot, _zcFiveDot, _zcWeekDot;
        private Grid _dsRow, _zcRow;'''
new = '''        private Ellipse _dsDot, _zcFiveDot, _zcWeekDot;
        private Grid _dsRow, _zcRow;
        private Border _sepTop, _sepMid;'''
assert old in s, "sep fields not found"
s = s.replace(old, new)

old = '''            stack.Children.Add(hdrArea);

            stack.Children.Add(Sep());

            // ---- DeepSeek 行'''
new = '''            stack.Children.Add(hdrArea);

            _sepTop = Sep();
            stack.Children.Add(_sepTop);

            // ---- DeepSeek 行'''
assert old in s, "sep top not found"
s = s.replace(old, new)

old = '''            Border sepMid = Sep();
            stack.Children.Add(sepMid);'''
new = '''            _sepMid = Sep();
            stack.Children.Add(_sepMid);'''
assert old in s, "sepMid field not found"
s = s.replace(old, new)

# ---------- 5) 窗口高度：角色隐藏时 ClosedH 相应缩小 ----------
old = '''        private const double ClosedH = 254;    // 抽屉全收起的窗口高度'''
new = '''        private const double ClosedH = 254;    // 抽屉全收起（双角色）的窗口高度
        private const double RowH = 76;        // 单个角色行高（隐藏角色时从 ClosedH 扣除）'''
assert old in s, "ClosedH not found"
s = s.replace(old, new)

old = '''        private double _windowH = ClosedH;     // 当前窗口高度基准值（动画起点/终点计算用）'''
new = '''        private double _windowH = ClosedH;     // 当前窗口高度基准值（动画起点/终点计算用）

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
        }'''
assert old in s, "windowH not found"
s = s.replace(old, new)

# Start 时按配置初始化高度（在 UpdateDock 之前）
old = '''        public void Start()
        {
            UpdateVisual(false);'''
new = '''        public void Start()
        {
            UpdateHeightNow();
            UpdateVisual(false);'''
assert old in s, "Start height not found"
s = s.replace(old, new)

# AnimateDrawer 里的 ClosedH 换成动态值
old = '''            // 窗口高度跟随抽屉伸缩，底部不留透明空底
            double newH = ClosedH + (open ? target : 0);'''
new = '''            // 窗口高度跟随抽屉伸缩，底部不留透明空底
            double newH = ClosedHeightNow() + (open ? target : 0);'''
assert old in s, "drawer height not found"
s = s.replace(old, new)

# ---------- 6) GLM 表情驱动源 ----------
old = '''            if (_zc != null && _zc.Ok)
            {
                double worst = Math.Min(_zc.Five.RemainFrac < 0 ? 1 : _zc.Five.RemainFrac, _zc.Week.RemainFrac < 0 ? 1 : _zc.Week.RemainFrac);
                int cand = DataFeed.MoodFromRemain(worst, low, mid);'''
new = '''            if (_zc != null && _zc.Ok)
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
                int cand = DataFeed.MoodFromRemain(worst, low, mid);'''
assert old in s, "mood source not found"
s = s.replace(old, new)

# ---------- 7) 设置页：显示角色 + 表情驱动源 ----------
old = '''            _chkHover = AddCheck(s1, "悬停图标时弹出详情面板", SettingsData.Flag(_cfg, "hover_details"));'''
new = '''            _chkHover = AddCheck(s1, "悬停图标时弹出详情面板", SettingsData.Flag(_cfg, "hover_details"));

            StackPanel s1b = Section(root, "显示角色");
            _chkShowDs = AddCheck(s1b, "DeepSeek 角色（大肥鱼，显示 API 余额）", SettingsData.Flag(_cfg, "show_ds"));
            _chkShowZc = AddCheck(s1b, "GLM 角色（显示订阅额度）", SettingsData.Flag(_cfg, "show_zc"));
            _cmbMoodSrc = AddCombo(s1b, "GLM 表情由哪个额度驱动", new string[] { "five", "week", "worst" }, _cfg["zc_mood_source"]);
            TextBlock moodHint = new TextBlock
            {
                FontSize = 11.5,
                Foreground = Ui.Brush(0x88, 0xFF, 0xFF, 0xFF),
                Text = "five=5小时额度（推荐） / week=本周额度 / worst=两者取更差",
                Margin = new Thickness(0, 2, 0, 0),
            };
            s1b.Children.Add(moodHint);'''
assert old in s, "settings section not found"
s = s.replace(old, new)

# 设置页字段
old = '''        private CheckBox _chkAutostart, _chkTray, _chkHover, _chkShowKey;'''
new = '''        private CheckBox _chkAutostart, _chkTray, _chkHover, _chkShowKey, _chkShowDs, _chkShowZc;
        private ComboBox _cmbMoodSrc;'''
assert old in s, "settings fields not found"
s = s.replace(old, new)

# 保存逻辑
old = '''            d["hover_details"] = _chkHover.IsChecked == true ? "true" : "false";
            d["show_tray"] = _chkTray.IsChecked == true ? "true" : "false";'''
new = '''            d["hover_details"] = _chkHover.IsChecked == true ? "true" : "false";
            d["show_tray"] = _chkTray.IsChecked == true ? "true" : "false";
            d["show_ds"] = _chkShowDs.IsChecked == true ? "true" : "false";
            d["show_zc"] = _chkShowZc.IsChecked == true ? "true" : "false";
            d["zc_mood_source"] = (string)_cmbMoodSrc.SelectedItem ?? "five";'''
assert old in s, "save logic not found"
s = s.replace(old, new)

# 双角色都关掉时的兜底提示（保存时至少留一个，不然侧栏只剩 HDR 钮）
old = '''        private void SaveAndClose()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);'''
new = '''        private void SaveAndClose()
        {
            if (_chkShowDs.IsChecked != true && _chkShowZc.IsChecked != true)
            {
                _testResult.Text = "至少保留一个角色，否则侧边栏只剩 HDR 开关。";
                _testResult.Foreground = Ui.Brush(0xFF, 0xFF, 0xB0, 0xB0);
                return;
            }
            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);'''
assert old in s, "save guard not found"
s = s.replace(old, new)

io.open(p, "w", encoding="utf-8").write(s)
print("patched ok")
