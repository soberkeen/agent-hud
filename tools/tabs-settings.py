# -*- coding: utf-8 -*-
# v1.8：设置页重构——固定窗口 + 页签分层（通用/数据源/关于）
import io, os, re

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "hdr-toggle.cs")
s = io.open(p, encoding="utf-8").read()

pat = re.compile(
    r"    // ---------- 设置窗口 ----------\r?\n"
    r"    public class SettingsWindow : Window\r?\n.*?\r?\n    \}\r?\n\r?\n"
    r"    // ---------- 侧边栏（v1\.4：原位抽屉展开 \+ HDR 双击确认） ----------",
    re.S)

NEW = r'''    // ---------- 设置窗口（固定尺寸 + 页签：通用/数据源/关于） ----------
    public class SettingsWindow : Window
    {
        private readonly Widget _host;
        private readonly Dictionary<string, string> _cfg;
        private CheckBox _chkAutostart, _chkTray, _chkHover, _chkShowKey, _chkShowDs, _chkShowZc;
        private ComboBox _cmbRefresh, _cmbLow, _cmbMid, _cmbMoodSrc;
        private PasswordBox _pwdKey;
        private TextBox _txtKey, _txtDsBase, _txtZcBase;
        private TextBlock _testResult;
        private readonly List<Border> _tabBtns = new List<Border>();
        private readonly List<StackPanel> _tabPages = new List<StackPanel>();

        public SettingsWindow(Widget host)
        {
            _host = host;
            _cfg = host.CurrentConfig;
            Title = "HDR 侧边栏 · 设置";
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
            Grid pages = new Grid();
            contentBox.Child = pages;
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
            AddHint(p0, "five=5小时额度（推荐） / week=本周 / worst=两者取更差");
            AddThresholds(p0, _cfg["warn_low"], _cfg["warn_mid"]);

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
                Text = "HDR 侧边栏 v1.8 · HDR 开关 + DeepSeek 余额 + ZCode 订阅用量",
            };
            p2.Children.Add(about);
            Button repo = DarkButton("GitHub 仓库", delegate { Ui.OpenUrl("https://github.com/soberkeen/hdr-toggle"); });
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
            Button cancel = DarkButton("取消", delegate { Close(); });
            cancel.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(save);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            SwitchTab(0);
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
            Dictionary<string, string> d = new Dictionary<string, string>(_cfg);
            d["deepseek_key"] = _chkShowKey.IsChecked == true ? _txtKey.Text : _pwdKey.Password;
            d["deepseek_base"] = string.IsNullOrWhiteSpace(_txtDsBase.Text) ? "https://api.deepseek.com" : _txtDsBase.Text.Trim();
            d["zcode_base"] = _txtZcBase.Text.Trim();
            d["refresh_minutes"] = ComboValue(_cmbRefresh, " 分钟");
            d["warn_low"] = ComboValue(_cmbLow, null);
            d["warn_mid"] = ComboValue(_cmbMid, null);
            d["hover_details"] = _chkHover.IsChecked == true ? "true" : "false";
            d["show_tray"] = _chkTray.IsChecked == true ? "true" : "false";
            d["show_ds"] = _chkShowDs.IsChecked == true ? "true" : "false";
            d["show_zc"] = _chkShowZc.IsChecked == true ? "true" : "false";
            d["zc_mood_source"] = (string)_cmbMoodSrc.SelectedItem ?? "five";
            SettingsData.Save(d);
            SettingsData.SetAutostart(_chkAutostart.IsChecked == true);
            _host.ApplyConfigChanged();
            Close();
        }
    }

    // ---------- 侧边栏（v1.4：原位抽屉展开 + HDR 双击确认） ----------'''

# 阈值行辅助：AddThresholds（两个下拉并排）需要先在类里存在——上面 NEW 里调用了它，这里补进 NEW 文本
NEW = NEW.replace('''        private string ComboValue(ComboBox cb, string suffix)''',
'''        private void AddThresholds(StackPanel sp, string low, string mid)
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

        private string ComboValue(ComboBox cb, string suffix)''')

s2, n = pat.subn(lambda m: NEW + "\n", s, count=1)
assert n == 1, "SettingsWindow block not found"

io.open(p, "w", encoding="utf-8").write(s2)
print("settings rebuilt")
