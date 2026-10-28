# -*- coding: utf-8 -*-
# v2.1：FramePlayer 支持直接播放 GIF（assets\anim-XX-<状态>.gif），无需任何流水线
#   加载顺序：assets\*.gif（用户自制，含逐帧延时） -> assets\*.png（精灵图） -> 内嵌静态图标
import io, os

p = 'hdr-toggle.cs'
s = io.open(p, encoding='utf-8').read()

# ---------- 1) FramePlayer 字段：支持帧数组模式（GIF） ----------
old = '''        private readonly Image _img = new Image();
        private readonly string _prefix;      // assets 文件名前缀，如 "anim-df-"
        private readonly string _fallbackRes; // 全部缺失时的静态兜底（内嵌资源）
        private readonly Dictionary<string, BitmapSource> _cache = new Dictionary<string, BitmapSource>();
        private string _sheet;
        private int _frames = 1;
        private const int FrameW = 96, FrameH = 96;'''
new = '''        private readonly Image _img = new Image();
        private readonly string _prefix;      // assets 文件名前缀，如 "anim-df-"
        private readonly string _fallbackRes; // 全部缺失时的静态兜底（内嵌资源）
        private readonly Dictionary<string, BitmapSource> _cache = new Dictionary<string, BitmapSource>();
        private string _sheet;
        private int _frames = 1;
        private BitmapSource[] _frameArr;     // GIF 模式：独立帧
        private int[] _delays;                // GIF 模式：每帧延时 ms
        private bool _sheetMode = true;       // true=PNG 精灵图裁切 / false=独立帧数组
        private const int FrameW = 96, FrameH = 96;'''
assert old in s, "fields not found"
s = s.replace(old, new)

# ---------- 2) SetSheet：GIF 优先 ----------
old = '''        public void SetSheet(string key)
        {
            if (_sheet == key) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(key, out bmp) || bmp == null)
            {
                try { bmp = Assets.Load(_prefix + key + ".png", _fallbackRes); }
                catch { bmp = null; }
                _cache[key] = bmp;
            }
            if (bmp == null)
            {
                _sheet = null;
                return;
            }
            _sheet = key;
            _frames = Math.Max(1, bmp.PixelWidth / FrameW);
            SetFrame(0);
        }'''
new = '''        public void SetSheet(string key)
        {
            if (_sheet == key) return;
            // 1) 用户自制 GIF（assets\\anim-XX-<状态>.gif）
            string gif = Path.Combine(Assets.Dir(), _prefix + key + ".gif");
            if (LoadGif(gif)) { _sheet = key; SetFrame(0); return; }
            // 2) PNG 精灵图（流水线产物）
            BitmapSource bmp = Assets.Load(_prefix + key + ".png", _fallbackRes);
            if (bmp == null) { _sheet = null; return; }
            _frameArr = null; _delays = null; _sheetMode = true;
            _frames = Math.Max(1, bmp.PixelWidth / FrameW);
            _loopMs = _frames * 33;
            _sheet = key;
            SetFrame(0);
        }

        private bool LoadGif(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                GifBitmapDecoder dec = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (dec.Frames == null || dec.Frames.Count == 0) return false;
                List<BitmapSource> fl = new List<BitmapSource>();
                List<int> dl = new List<int>();
                foreach (BitmapFrame f in dec.Frames)
                {
                    double sc = 96.0 / Math.Max(1, f.PixelWidth);
                    TransformedBitmap tb = new TransformedBitmap(f.Clone(), new ScaleTransform(sc, sc));
                    tb.Freeze();
                    fl.Add(tb);
                    int ms = 60;
                    try
                    {
                        BitmapMetadata md = f.Metadata as BitmapMetadata;
                        object d;
                        if (md != null && md.TryGetQuery("/grctlext/Delay", out d))
                            ms = Math.Max(20, Math.Min(500, Convert.ToInt32(d) * 10));   // 单位 1/100 秒
                    }
                    catch { }
                    dl.Add(ms);
                }
                _frameArr = fl.ToArray();
                _delays = dl.ToArray();
                _frames = _frameArr.Length;
                _sheetMode = false;
                _loopMs = 0;
                foreach (int x in _delays) _loopMs += x;
                return _frames > 0;
            }
            catch { return false; }
        }

        // 按绝对时间推进到对应帧（时基由 Widget 的秒表提供）
        public void Advance(int elapsedMs)
        {
            if (_sheetMode) { SetFrame(elapsedMs / 33); return; }          // 精灵图 30fps
            if (_loopMs <= 0) return;
            int t = ((elapsedMs % _loopMs) + _loopMs) % _loopMs;
            int acc = 0;
            for (int i = 0; i < _delays.Length; i++)
            {
                acc += _delays[i];
                if (t < acc) { ShowFrameArr(i); return; }
            }
            ShowFrameArr(_delays.Length - 1);
        }

        private void ShowFrameArr(int i)
        {
            if (_frameArr == null || _frameArr.Length == 0) return;
            i = Math.Max(0, Math.Min(_frameArr.Length - 1, i));
            if (i != _lastShownArr) { _lastShownArr = i; _img.Source = _frameArr[i]; }
        }
        private int _lastShownArr = -1;'''
assert old in s, "SetSheet not found"
s = s.replace(old, new)

# ---------- 3) ShowFrame/SetFrame 拆分（精灵图模式） ----------
old = '''        public void SetFrame(int i)
        {
            if (_sheet == null || _frames <= 0) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(_sheet, out bmp) || bmp == null) return;
            i = ((i % _frames) + _frames) % _frames;   // 循环取帧
            _img.Source = new CroppedBitmap(bmp, new Int32Rect(i * FrameW, 0, FrameW, FrameH));
        }'''
new = '''        public void SetFrame(int i)
        {
            if (!_sheetMode || _sheet == null || _frames <= 0) return;
            BitmapSource bmp;
            if (!_cache.TryGetValue(_sheet, out bmp) || bmp == null) return;
            i = ((i % _frames) + _frames) % _frames;   // 循环取帧
            _img.Source = new CroppedBitmap(bmp, new Int32Rect(i * FrameW, 0, FrameW, FrameH));
        }'''
assert old in s, "SetFrame not found"
s = s.replace(old, new)

# ---------- 4) 字段补充 ----------
old = '''        private bool _sheetMode = true;       // true=PNG 精灵图裁切 / false=独立帧数组'''
new = '''        private bool _sheetMode = true;       // true=PNG 精灵图裁切 / false=独立帧数组
        private int _loopMs;                  // GIF 模式：整圈毫秒'''
assert old in s, "loopMs field not found"
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8').write(s)
print('frameplayer gif support patched')

# ---------- Widget：动画推进改走 Advance（秒表绝对时间时基） ----------
old = '''                int idx = (int)(_animClock.Elapsed.TotalMilliseconds / 33.3);   // 30fps：60Hz 屏每 2 刷新一帧，节拍均匀
                if (idx == _animIdx) return;
                _animIdx = idx;
                if (!_dfFrozen) _dfPlayer.SetFrame(idx);   // SetFrame 内部按各表帧数取模
                if (!_zcFrozen) _zcPlayer.SetFrame(idx);'''
new = '''                int t = (int)(_animClock.Elapsed.TotalMilliseconds);
                if (!_dfFrozen) _dfPlayer.Advance(t);   // Advance 内部按各自动画的循环时长取帧
                if (!_zcFrozen) _zcPlayer.Advance(t);'''
assert old in s, "rendering tick not found"
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8').write(s)
print('widget advance patched')
