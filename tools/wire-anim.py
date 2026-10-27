# -*- coding: utf-8 -*-
# 接入动画：FramePlayer 类 + 图标行替换 + 额度状态联动 + build.ps1 嵌入
import io, os

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "hdr-toggle.cs")
s = io.open(p, encoding="utf-8").read()

# ---------- 1) FramePlayer 类（放在 Ui 类之后） ----------
anchor = '''    public static class Program
    {'''
player = '''    // ---------- 帧动画播放器（精灵图 + 乒乓循环，按额度状态切表） ----------
    internal class FramePlayer
    {
        private readonly Image _img = new Image();
        private readonly string _prefix;      // 资源名前缀，如 "anim.df."
        private readonly string _fallbackRes; // 精灵图缺失时的静态兜底
        private readonly Dictionary<string, BitmapSource> _cache = new Dictionary<string, BitmapSource>();
        private string _sheet;
        private const int Frames = 12;
        private const int FrameW = 96, FrameH = 96;

        public FramePlayer(string prefix, double size, string fallbackRes)
        {
            _prefix = prefix;
            _fallbackRes = fallbackRes;
            _img.Width = size;
            _img.Height = size;
            _img.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            SetSheet("normal");
        }

        public Image Visual { get { return _img; } }

        public void SetSheet(string key)
        {
            if (_sheet == key) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(key, out bmp) || bmp == null)
            {
                try { bmp = Ui.ResImage(_prefix + key + ".png") as BitmapSource; }
                catch { bmp = null; }
                _cache[key] = bmp;
            }
            if (bmp == null)
            {
                _sheet = null;
                if (_fallbackRes != null) _img.Source = Ui.ResImage(_fallbackRes);   // 静态兜底
                return;
            }
            _sheet = key;
            SetFrame(0);
        }

        public void SetFrame(int i)
        {
            if (_sheet == null) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(_sheet, out bmp) || bmp == null) return;
            i = Math.Max(0, Math.Min(Frames - 1, i));
            _img.Source = new CroppedBitmap(bmp, new Int32Rect(i * FrameW, 0, FrameW, FrameH));
        }
    }

    public static class Program
    {'''
assert anchor in s, "Program anchor not found"
s = s.replace(anchor, player, 1)

# ---------- 2) Widget 字段 ----------
old = '''        private Ellipse _dsDot, _zcDot;
        private Grid _dsRow, _zcRow;'''
new = '''        private Ellipse _dsDot, _zcDot;
        private Grid _dsRow, _zcRow;
        private FramePlayer _dfPlayer, _zcPlayer;
        private DispatcherTimer _animTimer;
        private int _animIdx;
        private bool _dfFrozen, _zcFrozen;   // 数据异常时定格，不瞎动
        private static readonly int[] Seq = BuildSeq();

        private static int[] BuildSeq()
        {
            // 乒乓序列 0..11,10..1：AI 视频首尾接不上也能无缝循环
            List<int> l = new List<int>();
            for (int i = 0; i < 12; i++) l.Add(i);
            for (int i = 10; i >= 1; i--) l.Add(i);
            return l.ToArray();
        }'''
assert old in s, "widget fields not found"
s = s.replace(old, new)

# ---------- 3) 图标行改用 FramePlayer ----------
old = '''            _dsRow = IconRow("ds");
            Image dsIcon = Ui.IconImage("Icon.Dafeiyu.png", 74);
            dsIcon.VerticalAlignment = VerticalAlignment.Center;
            dsIcon.HorizontalAlignment = HorizontalAlignment.Center;
            dsIcon.IsHitTestVisible = false;
            _dsDot = StatusDot();
            _dsRow.Children.Add(dsIcon);
            _dsRow.Children.Add(_dsDot);'''
new = '''            _dsRow = IconRow("ds");
            _dfPlayer = new FramePlayer("anim.df.", 74, "Icon.Dafeiyu.png");
            _dsDot = StatusDot();
            _dsRow.Children.Add(_dfPlayer.Visual);
            _dsRow.Children.Add(_dsDot);'''
assert old in s, "ds row not found"
s = s.replace(old, new)

old = '''            _zcRow = IconRow("zc");
            Image zcIcon = Ui.IconImage("Icon.Zcode.png", 74);
            zcIcon.VerticalAlignment = VerticalAlignment.Center;
            zcIcon.HorizontalAlignment = HorizontalAlignment.Center;
            zcIcon.IsHitTestVisible = false;
            _zcDot = StatusDot();
            _zcRow.Children.Add(zcIcon);
            _zcRow.Children.Add(_zcDot);'''
new = '''            _zcRow = IconRow("zc");
            _zcPlayer = new FramePlayer("anim.zc.", 74, "Icon.Zcode.png");
            _zcDot = StatusDot();
            _zcRow.Children.Add(_zcPlayer.Visual);
            _zcRow.Children.Add(_zcDot);'''
assert old in s, "zc row not found"
s = s.replace(old, new)

# ---------- 4) 动画时钟（Start 里启动；Closed 里停止） ----------
old = '''        public void Start()
        {
            UpdateVisual(false);
            _pollTimer.Start();'''
new = '''        public void Start()
        {
            UpdateVisual(false);
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _animTimer.Tick += delegate
            {
                _animIdx = (_animIdx + 1) % Seq.Length;
                if (!_dfFrozen) _dfPlayer.SetFrame(Seq[_animIdx]);
                if (!_zcFrozen) _zcPlayer.SetFrame(Seq[_animIdx]);
            };
            _animTimer.Start();
            _pollTimer.Start();'''
assert old in s, "Start not found"
s = s.replace(old, new)

old = '''                _pollTimer.Stop();
                _dataTimer.Stop();
                _armTimer.Stop();'''
new = '''                _pollTimer.Stop();
                _dataTimer.Stop();
                _armTimer.Stop();
                if (_animTimer != null) _animTimer.Stop();'''
assert old in s, "Closed cleanup not found"
s = s.replace(old, new)

# ---------- 5) ApplyData：按状态切表 + 冻结逻辑 ----------
old = '''            // 角标状态点
            if (_ds != null && _ds.Ok) SetDot(_dsDot, Ui.Col(0x7C, 0xFC, 0x9E));
            else if (_ds != null && _ds.Error != null && _ds.Error.Contains("未配置")) SetDot(_dsDot, Ui.Col(0x88, 0x99, 0x99, 0x99));
            else SetDot(_dsDot, Ui.Col(0xFF, 0x6B, 0x6B));'''
new = '''            // 动画表切换（DS：余额映射心情；异常时定格不播）
            if (_ds != null && _ds.Ok)
            {
                double bal; bool num = double.TryParse(_ds.Balance, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out bal);
                int mood = !num ? 0 : (bal <= 0 ? 2 : (bal < 20 ? 1 : 0));
                _dfPlayer.SetSheet(mood == 0 ? "normal" : (mood == 1 ? "worry" : "daze"));
                _dfFrozen = false;
            }
            else _dfFrozen = true;
            if (_zc != null && _zc.Ok)
            {
                double worst = Math.Min(_zc.Five.RemainFrac < 0 ? 1 : _zc.Five.RemainFrac, _zc.Week.RemainFrac < 0 ? 1 : _zc.Week.RemainFrac);
                int mood = DataFeed.MoodFromRemain(worst, low, mid);
                _zcPlayer.SetSheet(mood == 0 ? "normal" : (mood == 1 ? "worry" : "daze"));
                _zcFrozen = false;
            }
            else _zcFrozen = true;

            // 角标状态点
            if (_ds != null && _ds.Ok) SetDot(_dsDot, Ui.Col(0x7C, 0xFC, 0x9E));
            else if (_ds != null && _ds.Error != null && _ds.Error.Contains("未配置")) SetDot(_dsDot, Ui.Col(0x88, 0x99, 0x99, 0x99));
            else SetDot(_dsDot, Ui.Col(0xFF, 0x6B, 0x6B));'''
assert old in s, "ApplyData dots anchor not found"
s = s.replace(old, new)

io.open(p, "w", encoding="utf-8").write(s)

# ---------- 6) build.ps1 嵌入精灵图 ----------
p2 = "build.ps1"
s2 = io.open(p2, encoding="utf-8").read()
old = '''$resDfy  = '-res:{0},Icon.Dafeiyu.png' -f $iconDfy
$resZc   = '-res:{0},Icon.Zcode.png'   -f $iconZc'''
new = '''$resDfy  = '-res:{0},Icon.Dafeiyu.png' -f $iconDfy
$resZc   = '-res:{0},Icon.Zcode.png'   -f $iconZc
# 动画精灵图 anim-df-normal.png -> anim.df.normal.png
$resAnim = @()
Get-ChildItem $PSScriptRoot -Filter 'anim-*.png' | ForEach-Object {
    $resAnim += ('-res:{0},anim.{1}.png' -f $_.FullName, (($_.BaseName -replace '^anim-','') -replace '-','.'))
}'''
assert old in s2, "build res anchor not found"
s2 = s2.replace(old, new)
old = "$resDfy $resZc $src"
new = "$resDfy $resZc $resAnim $src"
assert old in s2, "build csc args not found"
s2 = s2.replace(old, new)
io.open(p2, "w", encoding="utf-8").write(s2)
print("patched ok")
