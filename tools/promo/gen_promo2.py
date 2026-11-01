# -*- coding: utf-8 -*-
"""Agent HUD 宣传片 v2 —— 4K@60 忠实复刻版
规格全部对齐 agent-hud.cs 实测:窗口 104 DIP / 面板 #101216@0x80 r24 / HDR 58x58 r16 无光晕
ON=#1F9D5C OFF=#353535 ARMED=#8A5000@B0 / 行高 76 GIF 74 / 点 10+9x9 / 气泡 288 r12 #2A2A2E@E8
时间轴为固定常量(LINES 表),文案文档与画面共用同轴;口播配音不进片,由用户按 docs/宣传文案.md 自行录制
音频 = 合成伴奏 + 音效 + Fish/Fox 角色人声(与 app VoiceCue 语义一致)
用法:
  python gen_promo2.py all | audio | video | doc | frame T OUT | mux
"""
import math, os, sys, subprocess, wave, json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE   = os.path.dirname(os.path.abspath(__file__))
ROOT   = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, 'assets')
VOICE  = os.path.join(ROOT, 'voice')
BUILD  = os.path.join(HERE, 'build')
NAR    = os.path.join(BUILD, 'nar')
FINAL  = os.path.join(ROOT, 'promo', 'AgentHUD-promo-4k.mp4')

W, H, FPS = 3840, 2160, 60
RS = 2.0                 # 渲染比例:设计空间 1920x1080 * RS
UIK = 2.4                # HUD DIP -> 设计像素
def S(v):  return int(round(v * RS))
def D(v):  return S(v * UIK)      # DIP 尺寸
def DT(v): return v * UIK         # DIP -> 设计浮点

# ================= 时间轴(固定常量,文案同轴;口播由用户自行录制) =================
# (key, 文案, 场景号, 入点, 出点)  —— 全片 77.1s,与画面节拍共用此表
LINES = [
    ('nar01', '你的屏幕边缘，是全桌面最容易被浪费的黄金位置。', 1, 0.50, 5.00),
    ('nar02', '今天，把它交给 Agent HUD。',                    1, 5.50, 7.80),
    ('nar03', 'HDR 开关、DeepSeek 余额、GLM 额度，最常瞄的三样，全在这一条边上。', 2, 8.60, 15.00),
    ('nar04', 'HDR 单击只是待确认，双击才真的切，防误触刻在骨子里。', 3, 16.00, 21.00),
    ('nar05', '开的一瞬间，整个画面直接上了个档次。',           3, 21.20, 24.40),
    ('nar06', '鼠标扫过大肥鱼，余额自己弹出来；扫过噶漏姆，两个额度圈一起展开。', 4, 25.60, 31.80),
    ('nar07', '单击是固定，双击直接拉起 ZCode 或 DSH。',        4, 32.20, 35.60),
    ('nar08', '我最喜欢的部分：ZCode 干完活，角色头顶直接冒气泡告诉你。', 5, 36.60, 42.20),
    ('nar09', '权限确认，气泡上点个允许，原路写回 hook，人不用挪窝。', 5, 42.60, 47.60),
    ('nar10', '额度还剩多少？看脸就知道：绿色淡定，黄色慌张，红色——它先替你痴呆。', 6, 48.60, 54.50),
    ('nar11', 'Setup 双击装完，hooks 自动接好；托盘、自启、实时预览设置，全都有。', 7, 55.60, 61.80),
    ('nar12', '命令行也留了门：一条 -notify，任何程序都能喂它消息。', 7, 62.20, 66.60),
    ('nar13', '开源、免费、单文件零依赖。GitHub 搜 agent-hud——让你的 Agent，也住进你的桌面。', 8, 67.60, 75.40),
]
SUBS = False     # 口播字幕默认不烧录(文案单独交付);需要时改 True
TL = None

def build_timeline():
    lines = {k: [t0, t1 - t0, sub, sc] for (k, sub, sc, t0, t1) in LINES}
    raw = {}
    for (k, sub, sc, t0, t1) in LINES:
        if sc not in raw: raw[sc] = [t0, t1]
        else: raw[sc][1] = max(raw[sc][1], t1)
    for sc in raw:
        raw[sc][0] -= 0.72; raw[sc][1] += 0.62
    raw[1][0] = 0.0; raw[8][1] += 1.7
    scenes = {}
    prev = 0.0
    for sc in sorted(raw):
        if sc < 8:
            mid = (raw[sc][1] + raw[sc + 1][0]) / 2
            scenes[sc] = (prev, mid); prev = mid
        else:
            scenes[sc] = (prev, raw[sc][1])
    return {'lines': {k: tuple(v) for k, v in lines.items()},
            'scenes': scenes, 'dur': scenes[8][1]}

def TLd():  return TL['dur']
def nl(key): return TL['lines'][key][0]            # 句起点
def nd(key): return TL['lines'][key][1]            # 句长
def nend(key): return TL['lines'][key][0] + TL['lines'][key][1]
def sc0(n): return TL['scenes'][n][0]
def sc1(n): return TL['scenes'][n][1]

# ================= easing =================
def cl01(x): return 0.0 if x < 0 else (1.0 if x > 1 else x)
def eoc(x):  x = cl01(x); return 1 - (1 - x) ** 3
def eoq(x):  x = cl01(x); return 1 - (1 - x) ** 5          # ease-out-quint
def eios(x): x = cl01(x); return 0.5 - 0.5 * math.cos(x * math.pi)
def eob(x, s=1.55):
    x = cl01(x) - 1
    return 1 + x * x * ((s + 1) * x + s)
def eoe(x):  # ease-out-expo
    x = cl01(x); return 1 if x >= 1 else 1 - 2 ** (-10 * x)
def lerp(a, b, k): return a + (b - a) * k
def fi(t, t0, d): return cl01((t - t0) / d)
def fo(t, t1, d): return cl01((t1 - t) / d)
def seg(t, a, b): return cl01((t - a) / (b - a)) if b > a else 1.0

# ================= 字体/绘图基础 =================
FCACHE = {}
def F(path, sz):
    key = (path, int(sz))
    f = FCACHE.get(key)
    if f is None:
        f = ImageFont.truetype(os.path.join(r'C:\Windows\Fonts', path), int(sz))
        FCACHE[key] = f
    return f
def f_reg(sz):  return F('msyh.ttc', sz * RS)
def f_bold(sz): return F('msyhbd.ttc', sz * RS)
def f_mono(sz): return F('consola.ttf', sz * RS)
def f_sym(sz):  return F('seguisym.ttf', sz * RS)

INK  = (242, 245, 250, 255)
DIM  = (154, 167, 189, 255)
GREEN= (52, 211, 153, 255)
AMBER= (245, 158, 11, 255)
BLUE = (109, 138, 255, 255)
VIOLET=(167, 139, 250, 255)
# HUD 实测色
HUD_PANEL = (16, 18, 22, 176)     # #101216 @0x80,视频可读性 +0x28
HUD_BORDER= (255, 255, 255, 0x44)
HUD_SEP   = (255, 255, 255, 0x26)
PILL      = (255, 255, 255, 0x66)
H_FILL_ON = (31, 157, 92, 255)    # #1F9D5C
H_FILL_OFF= (53, 53, 53, 255)     # #353535
H_FILL_ARM= (138, 80, 0, 176)     # #8A5000 @B0
H_BORDER  = (255, 255, 255, 0x55)
H_TXT_ON  = (255, 255, 255, 255)
H_TXT_OFF = (204, 204, 204, 255)
DOT_G = (124, 252, 158, 255)      # #7CFC9E
DOT_Y = (255, 210, 74, 255)       # #FFD24A
DOT_R = (255, 107, 107, 255)      # #FF6B6B
BUB_BG= (42, 42, 46, 232)         # #2A2A2E @E8
BUB_BD= (255, 255, 255, 0x4D)
BTN_BG0, BTN_BG1 = (243, 243, 243, 255), (235, 235, 235, 255)  # WPF 默认钮

SPR = {}

def paste(base, spr, xy, alpha=1.0):
    xy = (int(xy[0]), int(xy[1]))
    if alpha >= 0.999: base.alpha_composite(spr, xy)
    else:
        if alpha <= 0.004: return
        a = spr.getchannel('A').point(lambda v: min(255, int(v * alpha)))
        tmp = spr.copy(); tmp.putalpha(a)
        base.alpha_composite(tmp, xy)

def paste_c(base, spr, center, alpha=1.0):
    paste(base, spr, (center[0] - spr.width / 2, center[1] - spr.height / 2), alpha)

def new_layer(): return Image.new('RGBA', (W, H), (0, 0, 0, 0))

def scale_fade(lay, k):
    if k >= 0.999: return lay
    a = lay.getchannel('A').point(lambda v: int(v * k))
    lay.putalpha(a)
    return lay

def rrect(d, box, r, **kw): d.rounded_rectangle(box, radius=max(0, r), **kw)

def glass(w, h, r=22, fill=(18, 23, 40, 216), border_a=40):
    m = 26
    spr = Image.new('RGBA', (S(w) + m * 2, S(h) + m * 2), (0, 0, 0, 0))
    sh = Image.new('RGBA', spr.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((m, m, m + S(w), m + S(h)), S(r), fill=(0, 0, 0, 110))
    spr.alpha_composite(sh.filter(ImageFilter.GaussianBlur(S(9))))
    d = ImageDraw.Draw(spr)
    rrect(d, (m, m, m + S(w), m + S(h)), S(r), fill=fill, outline=(255, 255, 255, border_a), width=S(1.2))
    d.line([(m + S(r), m + S(2)), (m + S(w) - S(r), m + S(2))], fill=(255, 255, 255, 46), width=S(1.2))
    return spr, m

# ================= 背景 =================
def build_bg():
    mw = S(60)  # 平移余量
    grad = Image.new('RGBA', (W + mw * 2, H + mw * 2))
    d = ImageDraw.Draw(grad)
    c0, c1 = (11, 14, 26), (23, 29, 52)
    for y in range(H + mw * 2):
        k = y / (H + mw * 2 - 1)
        d.line([(0, y), (W + mw * 2, y)], fill=tuple(int(lerp(c0[i], c1[i], k)) for i in range(3)) + (255,))
    glow = Image.new('L', grad.size, 0)
    dg = ImageDraw.Draw(glow)
    dg.ellipse((W/2 - 1440, H/2 - 1000, W/2 + 1440, H/2 + 1000), fill=78)
    glow = glow.filter(ImageFilter.GaussianBlur(S(110)))
    tint = Image.new('RGBA', grad.size, (52, 70, 118, 255))
    grad.paste(tint, (0, 0), glow)
    dots = Image.new('RGBA', grad.size, (0, 0, 0, 0))
    dd = ImageDraw.Draw(dots)
    for gy in range(S(30), H + mw * 2, S(38)):
        for gx in range(S(30), W + mw * 2, S(38)):
            dd.ellipse((gx - 1, gy - 1, gx + 1, gy + 1), fill=(255, 255, 255, 13))
    grad.alpha_composite(dots)
    SPR['bg'] = grad
    bs = S(180)
    bok = Image.new('RGBA', (bs, bs), (0, 0, 0, 0))
    db = ImageDraw.Draw(bok)
    for rr, aa in [(85, 30), (60, 46), (35, 66)]:
        db.ellipse((bs/2 - rr, bs/2 - rr, bs/2 + rr, bs/2 + rr), fill=aa)
    SPR['bokeh'] = bok.filter(ImageFilter.GaussianBlur(S(20)))
    wash = Image.new('RGBA', (W, H))
    dd2 = ImageDraw.Draw(wash)
    a0, a1 = (24, 190, 178), (110, 80, 255)
    for y in range(H):
        k = y / (H - 1)
        dd2.line([(0, y), (W, y)], fill=tuple(int(lerp(a0[i], a1[i], k)) for i in range(3)) + (255,))
    mask = Image.new('L', (W, H), 0)
    dm = ImageDraw.Draw(mask)
    dm.ellipse((W/2 - 1800, H/2 - 1240, W/2 + 1800, H/2 + 1240), fill=30)
    wash.putalpha(mask.filter(ImageFilter.GaussianBlur(S(100))))
    SPR['wash'] = wash

BOKEH = [(300, 240, 0.9, (70, 110, 255), 0.13, 0.0),
         (1560, 300, 0.7, (150, 110, 255), 0.10, 1.3),
         (480, 860, 0.8, (40, 190, 200), 0.08, 2.2),
         (1380, 820, 1.0, (80, 100, 255), 0.11, 3.9),
         (980, 140, 0.6, (150, 110, 255), 0.09, 5.1)]

def draw_bg(img, t, hdr=0.0):
    off = (math.sin(t * 0.05) * S(14), math.cos(t * 0.04) * S(10))
    img.alpha_composite(SPR['bg'], (-S(60) - int(off[0]) // 2, -S(60) - int(off[1]) // 2))
    a = 1.0 + 0.55 * hdr
    for (bx, by, sc, col, sp, ph) in BOKEH:
        x = S(bx + math.sin(t * sp * 2 * math.pi + ph) * 60) - S(60)
        y = S(by + math.cos(t * sp * 1.6 * math.pi + ph) * 40) - S(60)
        spr = SPR['bokeh']
        tint = Image.new('RGBA', spr.size, col + (255,))
        tint.putalpha(spr.getchannel('A').point(lambda v: min(255, int(v * a))))
        if sc != 1.0:
            spr = spr.resize((int(spr.width * sc), int(spr.height * sc)))
            tint = tint.resize(spr.size)
        img.alpha_composite(tint, (int(x - spr.width / 2), int(y - spr.height / 2)))
    if hdr > 0:
        paste(img, SPR['wash'], (0, 0), alpha=hdr * 0.85)

# ================= 角色 =================
GIFS = {}
def load_gifs():
    for n in ['df-normal', 'df-worry', 'df-daze', 'zc-normal', 'zc-worry', 'zc-daze']:
        im = Image.open(os.path.join(ASSETS, 'anim-%s.gif' % n))
        fr = []; i = 0
        while True:
            try: im.seek(i)
            except EOFError: break
            fr.append(im.convert('RGBA')); i += 1
        GIFS[n] = fr
CH = {}
def char(name, t, size):
    fr = GIFS[name]
    fi = int(t * 25) % len(fr)
    key = (name, fi, size)
    r = CH.get(key)
    if r is None:
        r = fr[fi].resize((size, size), Image.LANCZOS); CH[key] = r
    return r

# ================= HUD(忠实复刻) =================
# 全部 DIP 尺寸 × UIK;面板内容从上到下: handle16 hdr76 sep row76 sep row76 pad6
def hud_geom(open_ds=0.0, open_zc=0.0):
    g = {}
    g['W'] = DT(104)
    g['h_handle'] = DT(16); g['h_hdr'] = DT(76); g['h_row'] = DT(76)
    g['h_pad'] = DT(6); g['h_sep'] = DT(7)      # 1px + 上下 margin 3
    g['dsH'] = DT(40) * open_ds; g['zcH'] = DT(54) * open_zc
    g['H'] = g['h_handle'] + g['h_hdr'] + g['h_sep'] + g['h_row'] + g['dsH'] + g['h_sep'] + g['h_row'] + g['zcH'] + g['h_pad']
    y = 0.0
    g['y_handle'] = y; y += g['h_handle']
    g['y_hdr'] = y; y += g['h_hdr']
    g['y_sep1'] = y; y += g['h_sep']
    g['y_row1'] = y; y += g['h_row']
    g['y_ds'] = y; y += g['dsH']
    g['y_sep2'] = y; y += g['h_sep']
    g['y_row2'] = y; y += g['h_row']
    g['y_zc'] = y
    g['r'] = DT(24); g['btn'] = DT(58); g['gif'] = DT(74)
    return g

def draw_hud(img, t, x0, y0, hdr='on', armed=False, open_ds=0.0, open_zc=0.0,
             ds_text='¥ 42.13', five=0.66, week=0.57, dot_ds=DOT_G,
             dot_f=DOT_G, dot_w=DOT_G, alpha=1.0, ch_df='df-normal', ch_zc='zc-normal'):
    """按 agent-hud.cs 1:1 复刻。x0,y0 = 面板左上角(设计坐标)"""
    g = hud_geom(open_ds, open_zc)
    lay = new_layer() if alpha < 0.999 else img
    d = ImageDraw.Draw(lay)
    px0, py0 = S(x0), S(y0)
    pw, ph = S(g['W']), S(g['H'])
    # 面板本体:半透明暗纱 + 1px 亮边 + r24,无投影无高光(与实现一致)
    rrect(d, (px0, py0, px0 + pw, py0 + ph), S(g['r']), fill=HUD_PANEL, outline=HUD_BORDER, width=S(1))
    # 把手 pill 46x5 r2.5
    pwid, phh = S(DT(46)), S(DT(5))
    cx = px0 + pw // 2
    rrect(d, (cx - pwid // 2, py0 + S(g['y_handle'] + g['h_handle']/2) - phh//2, cx + pwid//2,
              py0 + S(g['y_handle'] + g['h_handle']/2) + phh//2), S(DT(2.5)), fill=PILL)
    # HDR 钮 58x58 r16,平涂 + 1px 边,无光晕
    b = S(g['btn'])
    bx0 = px0 + pw//2 - b//2
    by0 = py0 + S(g['y_hdr'] + g['h_hdr']/2) - b//2
    if armed: fill, txt, tsize = H_FILL_ARM, '确认?', 13.0
    elif hdr == 'on': fill, txt, tsize = H_FILL_ON, 'HDR', 14.5
    elif hdr == 'err': fill, txt, tsize = (138, 100, 0, 255), 'HDR', 14.5
    else: fill, txt, tsize = H_FILL_OFF, 'HDR', 14.5
    rrect(d, (bx0, by0, bx0 + b, by0 + b), S(DT(16)), fill=fill, outline=H_BORDER, width=S(1))
    d.text((bx0 + b/2, by0 + b/2), txt, font=f_bold(tsize * UIK), anchor='mm',
           fill=(255, 255, 255, 255) if txt == '确认?' else (H_TXT_ON if hdr=='on' else H_TXT_OFF))
    # 分隔线
    for yy in (g['y_sep1'] + DT(3.5), g['y_sep2'] + DT(3.5)):
        d.line([(px0 + S(DT(10)), py0 + S(yy)), (px0 + pw - S(DT(10)), py0 + S(yy))], fill=HUD_SEP, width=S(1))
    # 角色行: GIF 74 居中 + 状态点
    gs = S(g['gif'])
    bob = math.sin(t * 2 * math.pi * 0.5) * S(2)
    paste_c(lay, char(ch_df, t, gs), (px0 + pw/2, py0 + S(g['y_row1'] + g['h_row']/2) + bob), alpha)
    paste_c(lay, char(ch_zc, t, gs), (px0 + pw/2, py0 + S(g['y_row2'] + g['h_row']/2) - bob), alpha)
    def dot(cx, cy, dia, col):
        d.ellipse((S(cx) - S(dia)/2, S(cy) - S(dia)/2, S(cx) + S(dia)/2, S(cy) + S(dia)/2), fill=col)
    dot(x0 + g['W'] - DT(8) - DT(5),  y0 + g['y_row1'] + g['h_row'] - DT(4) - DT(5), DT(10), dot_ds)
    zx = x0 + g['W'] - DT(7) - DT(4.5)
    dot(zx, y0 + g['y_row2'] + g['h_row'] - DT(3) - DT(4.5) - DT(5.5), DT(9), dot_f)
    dot(zx, y0 + g['y_row2'] + g['h_row'] - DT(3) - DT(4.5), DT(9), dot_w)
    # DS 抽屉(面板内向下展开): 余额 17px bold #F4F6FA
    if open_ds > 0.01:
        clip_h = S(g['dsH'])
        yy = py0 + S(g['y_ds'])
        d2 = ImageDraw.Draw(lay)
        k = cl01(open_ds * 1.4)
        ty = yy + S(DT(6)) + S(DT(24)) * (1 - eoc(k))
        d2.text((px0 + pw/2, ty + S(DT(11))), ds_text, font=f_bold(17 * UIK),
                fill=(244, 246, 250, int(255 * k)), anchor='mm')
    # ZC 抽屉: 双圈 34 stroke3.5 + 标签 9.5px #99FFF
    if open_zc > 0.01:
        k = cl01(open_zc * 1.4)
        yy = py0 + S(g['y_zc'])
        for i, (lab, pct, ang) in enumerate((('5时', five, t * 1.0), ('本周', week, 0.0))):
            cx = x0 + g['W']/2 + (DT(-21) if i == 0 else DT(21))
            cy = y0 + g['y_zc'] + DT(21)
            rr_ = DT(17)
            bbox = (S(cx - rr_), S(cy - rr_), S(cx + rr_), S(cy + rr_))
            d.arc(bbox, 0, 360, fill=(255, 255, 255, 40), width=S(DT(3.5)))
            kk = eoc(seg(k, 0.15 + i * 0.18, 1.0))
            if kk > 0.01:
                col = DOT_G if pct > 0.5 else DOT_Y
                d.arc(bbox, -90, -90 + 360 * pct * kk, fill=col, width=S(DT(3.5)))
            d.text((S(cx), S(cy)), '%d' % round(pct * 100 * kk), font=f_bold(9.5 * UIK), fill=INK, anchor='mm')
            d.text((S(cx), S(cy) + S(DT(24))), lab, font=f_reg(9.5 * UIK),
                   fill=(255, 255, 255, int(153 * max(k, 0.35))), anchor='mm')
    if lay is not img:
        scale_fade(lay, alpha)
        img.alpha_composite(lay)
    return g

def draw_tucked(img, t, alpha=1.0):
    """半隐藏:面板贴右缘只露 12 DIP(真实 tuck 状态)"""
    g = hud_geom()
    lay = new_layer()
    d = ImageDraw.Draw(lay)
    pw = S(g['W'])
    x0 = W - S(DT(12))
    rrect(d, (x0, 0, x0 + pw, S(g['H'])), S(g['r']), fill=(30, 34, 44, int(242 * alpha)),
          outline=(255, 255, 255, 80), width=S(1))
    d.line([(x0 + S(4), S(g['H'] / 2)), (x0 + S(9), S(g['H'] / 2))],
           fill=(255, 255, 255, 150), width=S(2))
    img.alpha_composite(lay)

def hud_slide(img, t, y0, k, **kw):
    """HUD 从右缘滑入就位: k 0(全藏) -> 1(贴右缘 x=HUD_X0);带速度残影"""
    travel = (1920 - HUD_X0) + 60
    x = HUD_X0 + (1 - k) * travel
    v = abs(1 - k) * travel / 240        # 归一速度(设计空间)
    if v > 0.3:
        ghost = new_layer()
        draw_hud(ghost, t, x + v * S(24), y0, **kw)
        paste(img, ghost, (0, 0), 0.14)
        ghost2 = new_layer()
        draw_hud(ghost2, t, x + v * S(12), y0, **kw)
        paste(img, ghost2, (0, 0), 0.26)
    draw_hud(img, t, x, y0, **kw)

# ================= 气泡(忠实 NotifyBubble) =================
BUB_CACHE = {}
def bubble_sprite(title, body, buttons=None, clicked=None):
    """按 NotifyBubble 规格:288 DIP 宽自适应,r12,pad12,标题13bold,正文12,按钮12.5 WPF 默认灰"""
    key = (title, body, buttons, clicked)
    if key in BUB_CACHE: return BUB_CACHE[key]
    wd = DT(288)
    title_f, body_f, btn_f = f_bold(13 * UIK), f_reg(12 * UIK), f_reg(12.5 * UIK)
    tmp = Image.new('RGBA', (S(wd), S(400)), (0, 0, 0, 0))
    td = ImageDraw.Draw(tmp)
    # 行高估算(换行)
    def wrap_lines(text, font, maxw):
        rows, cur = [], ''
        for chx in text:
            if td.textlength(cur + chx, font=font) > S(maxw) or chx == '\n':
                rows.append(cur); cur = chx
            else: cur += chx
        rows.append(cur)
        return rows
    trows = wrap_lines(title, title_f, wd - DT(24))
    brows = wrap_lines(body, body_f, wd - DT(24))
    th = len(trows) * S(17 * UIK); bh = len(brows) * S(16 * UIK)
    btn_h = S(30 * UIK) if buttons else 0
    hd = S(12 * UIK) * 2 + th + S(5 * UIK) + bh + (S(10 * UIK) + btn_h if buttons else 0)
    spr = Image.new('RGBA', (S(wd) + S(20), hd + S(20)), (0, 0, 0, 0))
    d = ImageDraw.Draw(spr)
    fx0, fy0 = S(10), S(10)
    rrect(d, (fx0, fy0, fx0 + S(wd), fy0 + hd), S(DT(12)), fill=BUB_BG, outline=BUB_BD, width=S(1))
    y = fy0 + S(12 * UIK)
    for rrow in trows:
        d.text((fx0 + S(12 * UIK), y), rrow, font=title_f, fill=(244, 246, 250, 255), anchor='lt')
        y += S(17 * UIK)
    y += S(5 * UIK)
    for rrow in brows:
        d.text((fx0 + S(12 * UIK), y), rrow, font=body_f, fill=(204, 204, 204, 255), anchor='lt')
        y += S(16 * UIK)
    if buttons:
        y += S(10 * UIK)
        bx = fx0 + S(wd) - S(12 * UIK)
        for lab in reversed(buttons):
            bw = td.textlength(lab, font=btn_f) + S(24 * UIK)
            pressed = (clicked == lab)
            grad = make_v(int(bw), S(30 * UIK), (212, 236, 210, 255), (160, 205, 158, 255)) if pressed \
                else make_v(int(bw), S(30 * UIK), BTN_BG0, BTN_BG1)
            mk = Image.new('L', grad.size, 0)
            ImageDraw.Draw(mk).rounded_rectangle((0, 0, grad.width - 1, grad.height - 1), S(3), fill=255)
            spr.paste(grad, (int(bx - bw), int(y)), mk)
            d.rounded_rectangle((bx - bw, y, bx, y + S(30 * UIK)), S(3),
                                outline=(60, 160, 90, 255) if pressed else (112, 112, 112, 255),
                                width=S(1.4))
            d.text((bx - bw / 2, y + S(15 * UIK)), lab, font=btn_f,
                   fill=(20, 60, 35, 255) if pressed else (26, 26, 26, 255), anchor='mm')
            bx -= bw + S(6 * UIK)
    # 尾巴 11x11 r2 菱形(材质同气泡),贴右缘
    tx, ty = fx0 + S(wd) - S(DT(3)), S(0)
    tail = Image.new('RGBA', (S(DT(16)), S(DT(16))), (0, 0, 0, 0))
    ImageDraw.Draw(tail).rounded_rectangle((S(2), S(2), S(DT(13)), S(DT(13))), S(DT(2)), fill=BUB_BG)
    tail = tail.rotate(45, expand=True, resample=Image.BICUBIC)
    spr.alpha_composite(tail, (int(tx - tail.width / 2), int(S(60) - tail.height / 2)))
    BUB_CACHE[key] = (spr, hd)
    return spr, hd

def make_v(w, h, c0, c1):
    g = Image.new('RGBA', (int(w), int(h)))
    d = ImageDraw.Draw(g)
    for y in range(int(h)):
        k = y / max(1, int(h) - 1)
        d.line([(0, y), (int(w), y)], fill=tuple(int(lerp(c0[i], c1[i], k)) for i in range(4)))
    return g

def draw_bubble(img, t, t0, row_cy, title, body, buttons=None, clicked_at=None, clicked_lab=None):
    """气泡在 HUD 左侧,右缘尾巴指向角色头部;顶边≈行上方分割线(真实锚定)"""
    k = eob(fi(t, t0, 0.34), 1.25)
    if k <= 0.02: return
    clicked = clicked_lab if (clicked_at and t >= clicked_at) else None
    spr, hd = bubble_sprite(title, body, buttons, clicked)
    x1 = HUD_X0 - S(DT(10)) - spr.width      # 右缘贴 HUD-10DIP
    scale = lerp(0.86, 1.0, k)
    if scale < 0.999:
        spr = spr.resize((int(spr.width * scale), int(spr.height * scale)), Image.LANCZOS)
    y0 = row_cy - S(DT(30))
    jig = math.sin(t * 2 * math.pi * 15) * S(3) * max(0.0, 1 - fi(t, t0 + 0.05, 0.5)) if t - t0 < 0.6 else 0
    paste(img, spr, (x1, y0 + jig), alpha=cl01(k * 2.5))

# ================= 全局 HUD 位 =================
HUD_X0 = 1920 - 60 - DT(104)     # 面板右缘贴 1920-60
HUD_Y0 = 168.0
ROW1_C = HUD_Y0 + DT(16 + 76 + 7 + 38)
ROW2_C = HUD_Y0 + DT(16 + 76 + 7 + 76 + 7 + 38)
SEP1_Y = HUD_Y0 + DT(16 + 76 + 3.5)
SEP2_Y = HUD_Y0 + DT(16 + 76 + 7 + 76 + 3.5)

# ================= 光标 =================
def build_cursor():
    s = 2.6 * RS
    pts = [(0, 0), (0, 21), (4.6, 16.6), (8.2, 25.4), (12.2, 23.6), (8.6, 15.2), (15.6, 15.2)]
    spr = Image.new('RGBA', (int(44 * RS), int(58 * RS)), (0, 0, 0, 0))
    d = ImageDraw.Draw(spr)
    poly = [(x * s * 0.9 + 4 * RS, y * s * 0.9 + 4 * RS) for x, y in pts]
    sh = Image.new('RGBA', spr.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon([(p[0] + S(2), p[1] + S(2)) for p in poly], fill=(0, 0, 0, 90))
    spr.alpha_composite(sh.filter(ImageFilter.GaussianBlur(S(2))))
    d.polygon(poly, fill=(245, 248, 252, 255), outline=(10, 12, 18, 255))
    d.line(poly + [poly[0]], fill=(10, 12, 18, 255), width=S(1.4))
    SPR['cursor'] = spr

def draw_cursor(lay, pos, alpha=1.0):
    paste(lay, SPR['cursor'], pos, alpha)

def ripple(lay, pos, k):
    r = S(lerp(10, 52, k)); a = int(210 * (1 - k))
    d = ImageDraw.Draw(lay)
    d.ellipse((pos[0] - r, pos[1] - r, pos[0] + r, pos[1] + r), outline=(255, 255, 255, a), width=S(3))

# ================= 小件 =================
def chip(lay, center, text, alpha, color=GREEN, size=17):
    if alpha <= 0.01: return
    f = f_bold(size)
    d = ImageDraw.Draw(lay)
    tw = d.textlength(text, font=f)
    w = tw + S(56); h = S(size * 2.1)
    x0 = center[0] - w / 2; y0 = center[1] - h / 2
    rrect(d, (x0, y0, x0 + w, y0 + h), h / 2, fill=(10, 26, 22, int(205 * alpha)),
          outline=color[:3] + (int(200 * alpha),), width=S(1.5))
    d.ellipse((x0 + S(16), center[1] - S(4.5), x0 + S(25), center[1] + S(4.5)),
              fill=color[:3] + (int(255 * alpha),))
    d.text((x0 + S(35), center[1]), text, font=f, fill=color[:3] + (int(255 * alpha),), anchor='lm')

def ring(lay, center, radius, pct, color, alpha=1.0, width=9, track=46):
    d = ImageDraw.Draw(lay)
    a = int(255 * alpha)
    R = S(radius)
    bbox = (center[0] - R, center[1] - R, center[0] + R, center[1] + R)
    d.arc(bbox, 0, 360, fill=(255, 255, 255, int(track * alpha)), width=S(width))
    if pct > 0.005:
        d.arc(bbox, -90, -90 + 360 * pct, fill=color[:3] + (a,), width=S(width))

def check_row(lay, xy, text, k, size=21, col=DOT_G):
    """绿色对勾清单行,k=入场 0..1"""
    d = ImageDraw.Draw(lay)
    x, y = xy
    a = cl01(k * 2)
    r = S(11)
    d.ellipse((S(x) - r, S(y) - r, S(x) + r, S(y) + r), outline=col[:3] + (int(255 * a),), width=S(2))
    if k > 0.35:
        d.line([(S(x) - S(4.5), S(y) + S(0.5)), (S(x) - S(1), S(y) + S(4)), (S(x) + S(5), S(y) - S(4))],
               fill=col[:3] + (255,), width=S(2), joint='curve')
    d.text((S(x) + S(20), S(y)), text, font=f_reg(size),
           fill=INK[:3] + (int(235 * cl01((k - 0.25) * 2)),), anchor='lm')

def wrap2(text, limit=19):
    if len(text) <= limit: return [text]
    best, bd = None, 1e9
    for i, ch in enumerate(text):
        if ch in '，、；：——？.,; ':
            score = abs(i - len(text) / 2)
            if score < bd: bd, best = score, i
    if best is None: best = len(text) // 2
    return [text[:best].rstrip('，、；：——？.,; '), text[best:].lstrip('，、；：——？.,; ')]

def subtitle(img, key, t):
    st, dur, text, sc = TL['lines'][key]
    a = fi(t, st - 0.12, 0.28) * fo(t, st + dur + 0.42, 0.30)
    if a <= 0.01: return
    rows = wrap2(text)
    f = f_bold(21.5)
    d = ImageDraw.Draw(img)
    y0 = S(1002) - (len(rows) - 1) * S(15)
    for i, row in enumerate(rows):
        tw = d.textlength(row, font=f)
        x = W / 2 - tw / 2
        y = y0 + i * S(30) + (1 - eoc(a)) * S(6)
        d.text((x, y), row, font=f, fill=(255, 255, 255, int(246 * a)),
               stroke_width=S(1.6), stroke_fill=(8, 10, 16, int(220 * a)))

# ================= 场景 =================
def title_sprite():
    if 'title2' in SPR: return
    f = f_bold(74)
    w, h = S(700), S(120)
    spr = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(spr)
    d.text((w / 2, h / 2), 'Agent HUD', font=f, fill=INK, anchor='mm')
    tw_a = d.textlength('Agent ', font=f); tw_h = d.textlength('HUD', font=f)
    x0 = w / 2 - (tw_a + tw_h) / 2 + tw_a
    grad = make_v(int(tw_h) + 8, S(84), (86, 220, 255, 255), (167, 120, 255, 255))
    mask = Image.new('L', (int(tw_h) + 8, S(84)), 0)
    ImageDraw.Draw(mask).text((S(2), S(42)), 'HUD', font=f, fill=255, anchor='lm')
    spr.paste(grad, (int(x0) - S(2), int(h / 2 - S(42))), mask)
    SPR['title2'] = spr

def s1(lay, t):
    peek_t = nend('nar01') - 0.85
    b1 = nl('nar02') - 0.28
    if peek_t <= t < b1:
        draw_tucked(lay, t)
    elif t >= b1:
        k = eob(seg(t, b1, b1 + 0.92), 1.12)
        hud_slide(lay, t, HUD_Y0, k, hdr='on')
    tt = nl('nar02') + 0.55
    a = fi(t, tt, 0.4)
    if a > 0:
        title_sprite()
        k2 = eob(fi(t, tt, 0.5), 1.3)
        tsp = SPR['title2']
        sc = lerp(1.10, 1.0, k2)
        if sc < 0.999:
            tsp = tsp.resize((int(tsp.width * sc), int(tsp.height * sc)), Image.LANCZOS)
        paste_c(lay, tsp, (S(1120), S(400)), alpha=a)
        d = ImageDraw.Draw(lay)
        a2 = fi(t, tt + 0.4, 0.4)
        d.text((S(1120), S(498)), '把最常看的信息，钉在屏幕边上', font=f_reg(24),
               fill=INK[:3] + (int(225 * a2),), anchor='mm')

def s2(lay, t):
    draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='on')
    d = ImageDraw.Draw(lay)
    t3 = nl('nar03')
    for (t0, cy, txt, col) in ((t3 + 0.25, HUD_Y0 + DT(54), 'HDR 开关 · 双击确认', GREEN),
                               (t3 + 1.45, ROW1_C, '大肥鱼 · DeepSeek 余额', BLUE),
                               (t3 + 2.65, ROW2_C, '噶漏姆 · GLM 双额度圈', VIOLET)):
        k = fi(t, t0, 0.45)
        if k <= 0.01: continue
        ln = eoc(k) * DT(30)
        tx = HUD_X0 - DT(14) - ln
        d.line([(S(tx), S(cy)), (S(HUD_X0 - DT(14)), S(cy))], fill=(255, 255, 255, int(120 * k)), width=S(1.4))
        d.ellipse((S(HUD_X0 - DT(16)), S(cy) - S(2.4), S(HUD_X0 - DT(11)), S(cy) + S(2.4)),
                  fill=(255, 255, 255, int(210 * k)))
        d.text((S(tx - 8), S(cy)), txt, font=f_bold(21), fill=col[:3] + (int(255 * k),), anchor='rm')

def big_button(lay, t, c, size, armed, on, k_in, squash=1.0):
    bs = int(S(size) * squash)
    rad = S(size / 58 * 16)
    if armed: fill, txt, tf = H_FILL_ARM, '确认?', 13.0
    elif on: fill, txt, tf = H_FILL_ON, 'HDR', 14.5
    else: fill, txt, tf = H_FILL_OFF, 'HDR', 14.5
    bsp = Image.new('RGBA', (bs, bs), (0, 0, 0, 0))
    d = ImageDraw.Draw(bsp)
    rrect(d, (0, 0, bs, bs), rad, fill=fill, outline=H_BORDER, width=S(1.2))
    d.text((bs / 2, bs / 2), txt, font=f_bold(tf * size / 58),
           fill=(255, 255, 255, 255) if (armed or on) else H_TXT_OFF, anchor='mm')
    paste_c(lay, bsp, c, alpha=k_in)

def s3(lay, t):
    draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='off')
    arm_t = nl('nar04') + 1.9; on_t = nl('nar04') + 3.3
    k_in = eob(fi(t, sc0(3) + 0.15, 0.5), 1.1)
    if k_in > 0.02:
        card, m = glass(520, 520, r=30)
        paste_c(lay, card, (S(560), S(470)), alpha=cl01(k_in * 2))
    squash = 1.0
    for ct in (arm_t, on_t):
        if ct <= t < ct + 0.32:
            squash = 1 - 0.075 * math.sin(math.pi * seg(t, ct, ct + 0.32))
    big_button(lay, t, (S(560), S(470)), 380, arm_t <= t < on_t, t >= on_t, k_in, squash)
    d = ImageDraw.Draw(lay)
    a1 = eoc(fi(t, sc0(3) + 0.35, 0.5))
    d.text((S(900), S(392)), 'HDR 开关', font=f_bold(44), fill=INK[:3] + (int(255 * a1),), anchor='lm')
    d.text((S(900), S(470)), '单击待确认 · 双击生效', font=f_reg(26), fill=INK[:3] + (int(232 * a1),), anchor='lm')
    d.text((S(900), S(530)), '3 秒轮询同步系统状态 · SDR 白电平仲裁驱动 quirk', font=f_reg(17),
           fill=DIM[:3] + (int(215 * a1),), anchor='lm')
    if t >= on_t:
        wa = 0.55 * eoc(seg(t, nl('nar05'), nl('nar05') + 0.8)) * fo(t, sc1(3), 0.5)
        paste(lay, SPR['wash'], (0, 0), alpha=wa)
    if t >= nl('nar05') + 0.35:
        chip(lay, (S(560), S(700)), 'HDR ON · 已同步', fi(t, nl('nar05') + 0.35, 0.4))
    # 光标
    tgt = (S(560) + S(50), S(470) + S(60))
    if t < arm_t:
        k = eios(seg(t, sc0(3) + 0.6, arm_t - 0.15))
        cur = (lerp(S(1060), tgt[0], k), lerp(S(960), tgt[1], k))
    elif t < on_t:
        k = eios(seg(t, arm_t + 0.35, on_t - 0.15))
        cur = (lerp(tgt[0], S(560) - S(56), k), lerp(tgt[1], S(470) - S(36), k))
    else:
        cur = (S(560) - S(56), S(470) - S(36))
    if t > sc0(3) + 0.55:
        draw_cursor(lay, cur)
    for ct, pos in ((arm_t, tgt), (on_t, (S(560) - S(56), S(470) - S(36)))):
        if ct <= t < ct + 0.3:
            ripple(lay, pos, seg(t, ct, ct + 0.3))

def s4(lay, t):
    t6 = nl('nar06')
    ods = eoc(seg(t, t6 + 1.0, t6 + 1.38)) * (1 - eoc(seg(t, sc1(4) - 0.5, sc1(4) - 0.2)))
    ozc = eoc(seg(t, t6 + 3.5, t6 + 3.88)) * (1 - eoc(seg(t, sc1(4) - 0.5, sc1(4) - 0.2)))
    fill_k = seg(t, t6 + 3.7, t6 + 4.9)
    ds_open_t = t6 + 1.0
    txt_full = '¥ 42.13'
    nk = int(seg(t, ds_open_t + 0.12, ds_open_t + 0.75) * len(txt_full) + 0.999)
    draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='on', open_ds=ods, open_zc=ozc,
             ds_text=txt_full[:max(0, nk)], five=0.66 * eoc(fill_k), week=0.57 * eoc(fill_k))
    # 光标走位
    if t < t6 + 3.0:
        k = eios(seg(t, t6 + 0.45, t6 + 0.95))
        cur = (S(HUD_X0 + DT(52)), lerp(S(980), S(ROW1_C), k))
    elif t < sc1(4) - 0.6:
        k = eios(seg(t, t6 + 3.0, t6 + 3.42))
        cur = (S(HUD_X0 + DT(52)), lerp(S(ROW1_C), S(ROW2_C), k))
    else:
        cur = (S(HUD_X0 + DT(52)), S(ROW2_C))
    draw_cursor(lay, cur)
    if abs(t - (t6 + 1.0)) < 0.15: ripple(lay, cur, (t - t6 - 0.85) / 0.3)
    if abs(t - (t6 + 3.5)) < 0.15: ripple(lay, cur, (t - t6 - 3.35) / 0.3)
    t7 = nl('nar07')
    chip(lay, (S(1330), S(505)), '单击 = 固定常驻', fi(t, t7 + 0.4, 0.4), color=BLUE)
    chip(lay, (S(1330), S(576)), '双击 = 启动 ZCode / DSH', fi(t, t7 + 1.25, 0.4), color=VIOLET)

def s5(lay, t):
    draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='on')
    b1 = nl('nar08') + 1.15
    draw_bubble(lay, t, b1, ROW2_C, '任务完成', 'ZCode 会话已结束，快回来看一眼吧')
    b2 = nl('nar09') + 0.5
    click_t = nl('nar09') + 2.75
    draw_bubble(lay, t, b2, ROW1_C, '权限请求', 'agent-hud.exe 请求执行构建',
                buttons=('拒绝', '允许'), clicked_at=click_t, clicked_lab='允许')
    if t >= click_t + 0.12:
        chip(lay, (S(1430), S(ROW1_C + DT(96))), '已放行 · hook 退出码 0', fi(t, click_t + 0.12, 0.35))
    # 光标点允许
    if t >= nl('nar09') + 1.6:
        k = eios(seg(t, nl('nar09') + 1.6, click_t - 0.1))
        cur = (lerp(S(1500), S(1430), k), lerp(S(950), S(ROW1_C + DT(52)), k))
        draw_cursor(lay, cur)
        if abs(t - click_t) < 0.15:
            ripple(lay, (S(1430), S(ROW1_C + DT(52))), (t - click_t + 0.15) / 0.3)

def s6(lay, t):
    t0 = nl('nar10') - 0.6
    if t < t0 + 0.42:
        k = eoc(seg(t, t0, t0 + 0.42))
        hud_slide(lay, t, HUD_Y0, 1 - k, hdr='on')   # 滑回隐藏
    else:
        draw_tucked(lay, t)
    cols = [(S(480), 1.0, 0.80, '标准', '>50%', 'df-normal', 'zc-normal', DOT_G),
            (S(960), 2.0, 0.40, '紧张', '20~50%', 'df-worry', 'zc-worry', DOT_Y),
            (S(1440), 3.0, 0.10, '痴呆', '<20%', 'df-daze', 'zc-daze', DOT_R)]
    for (cx, dt_, pct, mood, rng, cn, cz, col) in cols:
        k = eob(fi(t, nl('nar10') + dt_, 0.5), 1.2)
        if k <= 0.02: continue
        ring(lay, (cx, S(330)), S(96), pct * eoc(seg(t, nl('nar10') + dt_ + 0.15, nl('nar10') + dt_ + 0.8)),
             col, width=13, alpha=cl01(k * 2))
        d = ImageDraw.Draw(lay)
        d.text((cx, S(330)), '%d%%' % int(pct * 100), font=f_bold(40), fill=INK, anchor='mm')
        bob = math.sin(t * 2 * math.pi * 0.55 + cx) * S(3)
        paste_c(lay, char(cn, t, S(180)), (cx - S(80), S(560) + bob), alpha=cl01(k * 2))
        paste_c(lay, char(cz, t, S(180)), (cx + S(80), S(560) - bob), alpha=cl01(k * 2))
        d.text((cx, S(712)), mood, font=f_bold(30), fill=col[:3] + (255,), anchor='mm')
        d.text((cx, S(762)), rng, font=f_reg(19), fill=DIM, anchor='mm')

def s7(lay, t):
    draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='on')
    t11, t12 = nl('nar11'), nl('nar12')
    fadeAB = 1 - eoc(seg(t, t12 + 0.1, t12 + 0.45))
    # Card A: 安装
    kA = eob(fi(t, t11 + 0.35, 0.5), 1.15)
    if kA > 0.02 and fadeAB > 0.02:
        card, m = glass(860, 420, r=26)
        paste_c(lay, card, (S(660), S(430)), alpha=cl01(kA * 2) * fadeAB)
        d = ImageDraw.Draw(lay)
        y = S(430) - S(150)
        d.text((S(660) - S(390), y), 'AgentHUD-Setup.exe', font=f_mono(22), fill=INK, anchor='lm')
        tw = d.textlength('AgentHUD-Setup.exe', font=f_mono(22))
        rrect(d, (S(660) - S(390) + tw + S(14), y - S(14), S(660) - S(390) + tw + S(96), y + S(14)),
              S(14), outline=GREEN, width=S(1.6))
        d.text((S(660) - S(390) + tw + S(55), y), '向导', font=f_reg(13.5), fill=GREEN, anchor='mm')
        for i, row in enumerate(('双击安装 · per-user 免管理员', '自动接入 ZCode hooks', '双击启动路径自动探测')):
            check_row(lay, (S(660) - S(370), S(430) - S(60) + i * S(74)), row,
                      fi(t, t11 + 0.7 + i * 0.22, 0.45))
    # Card B: 常驻体验
    kB = eob(fi(t, t11 + 4.8, 0.5), 1.15)
    if kB > 0.02 and fadeAB > 0.02:
        card, m = glass(760, 420, r=26)
        paste_c(lay, card, (S(1445), S(430)), alpha=cl01(kB * 2) * fadeAB)
        d = ImageDraw.Draw(lay)
        d.text((S(1445) - S(340), S(430) - S(150)), '常驻体验', font=f_bold(24), fill=INK, anchor='lm')
        for i, row in enumerate(('托盘 + 开机自启', '全屏让位 · 不挡游戏', '贴边半隐藏 · Home Indicator', '设置实时预览 · 皮肤 ×3')):
            check_row(lay, (S(1445) - S(340), S(430) - S(60) + i * S(74)), row,
                      fi(t, t11 + 5.15 + i * 0.22, 0.45))
    # 终端卡
    kT = eob(fi(t, t12 + 0.5, 0.5), 1.15)
    if kT > 0.02:
        TCX = 1060
        card, m = glass(980, 280, r=18, fill=(12, 14, 20, 235))
        paste_c(lay, card, (S(TCX), S(470)), alpha=cl01(kT * 2))
        d = ImageDraw.Draw(lay)
        for i in range(3):
            d.ellipse((S(TCX) - S(460) + i * S(26), S(470) - S(112), S(TCX) - S(460) + i * S(26) + S(12),
                       S(470) - S(112) + S(12)), fill=(90, 96, 108, 200))
        d.text((S(TCX) - S(400), S(470) - S(106)), 'agent-hud.exe — 命令行', font=f_reg(13.5), fill=DIM, anchor='lm')
        typed = '-notify "构建完成" "473 项测试全部通过" zcode'
        nk = int(seg(t, t12 + 0.95, t12 + 2.75) * len(typed) + 0.999)
        shown = typed[:max(0, min(len(typed), nk))]
        d.text((S(TCX) - S(460), S(470) - S(30)), '> ', font=f_mono(19), fill=DOT_G, anchor='lm')
        d.text((S(TCX) - S(424), S(470) - S(30)), shown, font=f_mono(19), fill=INK, anchor='lm')
        if nk <= len(typed) and (t * 2.7) % 1 < 0.5:
            tw2 = d.textlength(shown, font=f_mono(19))
            d.rectangle((S(TCX) - S(424) + tw2 + S(3), S(470) - S(48), S(TCX) - S(424) + tw2 + S(12), S(470) - S(12)),
                        fill=(240, 244, 250, 220))
        if t >= t12 + 2.95:
            ka = fi(t, t12 + 2.95, 0.35)
            d.text((S(TCX) - S(460), S(470) + S(52)), '✓ 事件已投递 · 气泡 8s 后自动收摊', font=f_reg(16),
                   fill=DOT_G[:3] + (int(255 * ka),), anchor='lm')
    # 回声气泡
    draw_bubble(lay, t, t12 + 3.3, ROW1_C, '构建完成', '473 项测试全部通过')
    chip(lay, (S(1430), S(ROW1_C + DT(96))), '任何程序 · 一行接入', fi(t, t12 + 3.75, 0.35), color=BLUE)

def s8(lay, t):
    t0 = sc0(8)
    # HUD 淡出
    fa = 1 - eoc(seg(t, t0 + 0.1, t0 + 0.6))
    if fa > 0.02:
        draw_hud(lay, t, HUD_X0, HUD_Y0, hdr='on', alpha=fa)
    k_in = eoc(fi(t, t0 + 0.15, 0.6))
    d = ImageDraw.Draw(lay)
    glow_text2(lay, (S(960), S(320)), 'Agent HUD', f_bold(92), INK, (110, 160, 255), alpha=k_in)
    d.text((S(960), S(452)), '开源 · 免费 · 单文件零依赖 · 双角色动画', font=f_reg(27),
           fill=INK[:3] + (int(235 * fi(t, t0 + 0.5, 0.45)),), anchor='mm')
    a3 = fi(t, t0 + 0.95, 0.45)
    d.text((S(960), S(540)), 'github.com/soberkeen/agent-hud', font=f_mono(27),
           fill=(120, 190, 255, int(255 * a3)), anchor='mm')
    if a3 > 0.5:
        uw = d.textlength('github.com/soberkeen/agent-hud', font=f_mono(27))
        d.line([(S(960) - uw / 2, S(568)), (S(960) + uw / 2, S(568))],
               fill=(120, 190, 255, int(130 * a3)), width=S(1.6))
    chip(lay, (S(960), S(650)), 'AgentHUD-Setup.exe · 双击即装', fi(t, t0 + 1.4, 0.45))
    for (cx, name, dl) in ((S(700), 'df-normal', 0.3), (S(1220), 'zc-normal', 0.45)):
        k = eob(fi(t, t0 + dl, 0.7))
        y = lerp(S(1140), S(880), k) + math.sin(t * 2 * math.pi * 0.5 + dl) * S(5)
        paste_c(lay, char(name, t, S(300)), (cx, y), alpha=k)

def glow_text2(lay, center, text, font, fill, glow, alpha=1.0):
    tmp = Image.new('RGBA', lay.size, (0, 0, 0, 0))
    ImageDraw.Draw(tmp).text(center, text, font=font, fill=glow + (int(105 * alpha),), anchor='mm')
    lay.alpha_composite(tmp.filter(ImageFilter.GaussianBlur(S(13))))
    ImageDraw.Draw(lay).text(center, text, font=font, fill=fill[:3] + (int(255 * alpha),), anchor='mm')

# ================= 渲染 =================
SCENE_FN = {1: s1, 2: s2, 3: s3, 4: s4, 5: s5, 6: s6, 7: s7, 8: s8}

def render_frame(idx):
    t = idx / FPS
    img = Image.new('RGBA', (W, H), (5, 6, 12, 255))
    hdr_amt = 0.55 * eoc(seg(t, nl('nar05'), nl('nar05') + 0.8)) * fo(t, sc1(3), 0.5)
    draw_bg(img, t, hdr_amt)
    lay = new_layer()
    for scn, (a, b) in TL['scenes'].items():
        if a - 0.4 <= t <= b + 0.05:
            SCENE_FN[scn](lay, min(t, b))
    if SUBS:
        for (key, sub, sc, t0, t1) in LINES:
            st = TL['lines'][key][0]
            if st - 0.6 <= t <= st + TL['lines'][key][1] + 0.9:
                subtitle(lay, key, t)
    img.alpha_composite(lay)
    ga = cl01(t / 0.7) * cl01((TLd() - t) / 0.9)
    if ga < 0.999:
        scale_fade(img, ga)
        img = Image.blend(Image.new('RGBA', (W, H), (5, 6, 12, 255)), img, ga)
    return img.convert('RGB')

# ================= 音频 =================
SR = 44100
def midi2f(m): return 440.0 * 2 ** ((m - 69) / 12.0)

def note(buf, t0, m, dur, gain, pan=0.0):
    n = int(dur * SR)
    if n <= 0 or t0 * SR > len(buf[0]): return
    tt = np.arange(n) / SR
    f = midi2f(m)
    env = np.minimum(tt / 0.006, 1) * np.exp(-tt / (dur * 0.55))
    wv = np.sin(2 * np.pi * f * tt) + 0.35 * np.sin(4 * np.pi * f * tt) + 0.14 * np.sin(6 * np.pi * f * tt)
    sig = wv * env * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    seg_ = sig[:i1 - i0]
    gl = math.sqrt(0.5 * (1 - pan)); gr = math.sqrt(0.5 * (1 + pan))
    buf[0][i0:i1] += seg_ * gl * 2 ** 0.5
    buf[1][i0:i1] += seg_ * gr * 2 ** 0.5

def kick(buf, t0, gain=0.42):
    n = int(0.16 * SR); tt = np.arange(n) / SR
    f = 110 * np.exp(-tt / 0.03) + 42
    ph = 2 * np.pi * np.cumsum(f) / SR
    sig = np.sin(ph) * np.exp(-tt / 0.09) * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def hat(buf, t0, gain=0.08):
    n = int(0.05 * SR)
    rng = np.random.default_rng(int(t0 * 1000) % 99991)
    x = np.diff(rng.standard_normal(n), prepend=0)
    env = np.exp(-np.arange(n) / SR / 0.012)
    sig = x * env * gain * 0.3
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def bass(buf, t0, m, dur, gain=0.19):
    n = int(dur * SR); tt = np.arange(n) / SR
    env = np.minimum(tt / 0.01, 1) * np.exp(-tt / (dur * 0.6))
    sig = np.sin(2 * np.pi * midi2f(m) * tt) * env * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def pad_chord(buf, t0, ms, dur, gain=0.05):
    n = int(dur * SR); tt = np.arange(n) / SR
    env = np.minimum(tt / 1.1, 1) * np.clip(np.minimum((dur - tt) / 1.2, 1), 0, 1)
    sig = np.zeros(n)
    for m in ms:
        f = midi2f(m - 12)
        sig += np.sin(2 * np.pi * f * tt + 0.3) + 0.5 * np.sin(2 * np.pi * f * 1.003 * tt)
    sig *= env * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

CHORDS = [([57, 60, 64, 69], 45), ([53, 57, 60, 65], 41),
          ([48, 52, 55, 60], 36), ([55, 59, 62, 67], 43)]

def build_music(dur):
    N = int(dur * SR)
    buf = [np.zeros(N), np.zeros(N)]
    bpm = 96; beat = 60 / bpm; bar = beat * 4
    sec_len = bar * 2
    t = 0.0; ci = 0
    full_from = nl('nar02') - 0.3
    brk = (sc0(6), sc1(6) - 0.3)
    while t < dur:
        tones, rootm = CHORDS[ci % 4]
        pad_chord(buf, t, tones, min(sec_len + 0.6, dur - t))
        pat = [0, 1, 2, 3, 2, 3, 1, 2]
        for e8 in range(16):
            et = t + e8 * beat / 2
            if et >= dur: break
            if e8 % 8 in (3, 6) and t > sec_len * 1.2: continue
            m = tones[pat[e8 % 8]] + 12
            g = 0.15 * (1.0 if e8 % 4 == 0 else 0.72)
            note(buf, et, m, 0.55, g, -0.4 if e8 % 2 == 0 else 0.4)
        full = full_from <= t < brk[0] or t >= brk[1]
        for b in range(8):
            bt = t + b * beat
            if bt >= dur: break
            if full and b % 2 == 0: kick(buf, bt)
            if full and b % 2 == 1: hat(buf, bt)
            if b in (0, 4):
                bass(buf, bt, rootm - 12, beat * 1.8, 0.19 if full else 0.11)
        t += sec_len; ci += 1
    return np.stack(buf)

def add_sfx(buf, t0, kind, gain=1.0):
    N = len(buf[0]); i0 = int(t0 * SR)
    if i0 >= N: return
    n_max = N - i0
    def put(sig):
        n = min(len(sig), n_max)
        buf[0][i0:i0 + n] += sig[:n]; buf[1][i0:i0 + n] += sig[:n]
    if kind == 'whoosh':
        n = int(0.55 * SR)
        x = np.random.default_rng(7).standard_normal(n)
        x = np.convolve(x, np.ones(90) / 90, mode='same')
        put(x * np.sin(np.linspace(0, math.pi, n)) ** 1.5 * 0.5 * gain)
    elif kind == 'click':
        n = int(0.03 * SR); tt = np.arange(n) / SR
        sig = np.sin(2 * np.pi * 1900 * tt) * np.exp(-tt / 0.006) * 0.5
        sig += np.random.default_rng(3).standard_normal(n) * np.exp(-tt / 0.002) * 0.35
        put(sig * gain)
    elif kind == 'tick':
        n = int(0.09 * SR); tt = np.arange(n) / SR
        put(np.sign(np.sin(2 * np.pi * 1150 * tt)) * np.exp(-tt / 0.035) * 0.16 * gain)
    elif kind == 'thunk':
        n = int(0.16 * SR); tt = np.arange(n) / SR
        put(np.sin(2 * np.pi * (95 * np.exp(-tt / 0.04) + 48) * tt) * np.exp(-tt / 0.06) * 0.6 * gain)
    elif kind == 'shimmer':
        n = int(1.2 * SR); tt = np.arange(n) / SR
        sig = sum(np.sin(2 * np.pi * midi2f(m) * tt) * np.exp(-tt / (0.45 + i * 0.1))
                  for i, m in enumerate([81, 85, 88, 93])) * 0.05
        put(sig * gain)
    elif kind == 'pop':
        n = int(0.09 * SR); tt = np.arange(n) / SR
        f = 320 + 2600 * tt / 0.09
        put(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.035) * 0.4 * gain)
    elif kind == 'chime':
        for i, m in enumerate([76, 81]):
            n = int(0.6 * SR); tt = np.arange(n) / SR
            sig = np.sin(2 * np.pi * midi2f(m) * tt) * np.exp(-tt / 0.22) * 0.16
            i2 = i0 + int(i * 0.09 * SR)
            if i2 < N:
                nn = min(len(sig), N - i2)
                buf[0][i2:i2 + nn] += sig[:nn]; buf[1][i2:i2 + nn] += sig[:nn]
    elif kind == 'blip':
        n = int(0.07 * SR); tt = np.arange(n) / SR
        put(np.sin(2 * np.pi * 740 * tt) * np.exp(-tt / 0.028) * 0.13 * gain)
    elif kind == 'key':
        n = int(0.025 * SR); tt = np.arange(n) / SR
        put(np.sin(2 * np.pi * 2400 * tt) * np.exp(-tt / 0.004) * 0.05 * gain)

def load_voice(path, peak=0.85):
    wf = wave.open(path, 'rb')
    data = np.frombuffer(wf.readframes(wf.getnframes()), dtype=np.int16)
    wf.close()
    a = data.reshape(-1, 2).astype(np.float64) / 32768.0
    a /= max(1e-9, np.abs(a).max())
    return a * peak

def build_audio():
    dur = TLd()
    music = build_music(dur)
    # 闪避包络:角色人声期间音乐压低
    duck = np.ones(int(dur * SR) + SR)
    def duck_seg(t0, t1, to=0.32):
        i0 = int(max(0, (t0 - 0.06) * SR)); i1 = int((t1 + 0.22) * SR)
        dn = np.linspace(1, to, int(0.08 * SR)); up = np.linspace(to, 1, int(0.3 * SR))
        duck[i0:i0 + len(dn)] = np.minimum(duck[i0:i0 + len(dn)], dn)
        duck[i0 + len(dn):max(i0 + len(dn), i1)] = np.minimum(duck[i0 + len(dn):max(i0 + len(dn), i1)], to)
        duck[i1:i1 + len(up)] = np.minimum(duck[i1:i1 + len(up)], up)
    # 音效事件
    sfx = [np.zeros(int(dur * SR)), np.zeros(int(dur * SR))]
    EV = [(nl('nar02') - 0.28, 'whoosh', 1.0),
          (nl('nar03') + 0.25, 'blip', 0.8), (nl('nar03') + 1.45, 'blip', 0.8), (nl('nar03') + 2.65, 'blip', 0.8),
          (nl('nar04') + 1.9, 'click', 1.0), (nl('nar04') + 1.96, 'tick', 1.0),
          (nl('nar04') + 3.3, 'click', 1.0), (nl('nar04') + 3.36, 'thunk', 1.0),
          (nl('nar05') + 0.35, 'shimmer', 0.9),
          (nl('nar06') + 1.0, 'pop', 0.85), (nl('nar06') + 3.5, 'pop', 0.85),
          (nl('nar08') + 1.15, 'pop', 1.0), (nl('nar09') + 0.5, 'pop', 0.9),
          (nl('nar09') + 2.75, 'click', 1.0), (nl('nar09') + 2.81, 'chime', 1.0),
          (nl('nar10') - 0.6, 'whoosh', 0.6),
          (nl('nar10') + 1.0, 'pop', 0.75), (nl('nar10') + 2.0, 'pop', 0.75), (nl('nar10') + 3.0, 'pop', 0.75),
          (nl('nar11') + 0.35, 'pop', 0.8), (nl('nar11') + 4.8, 'pop', 0.8),
          (nl('nar12') + 0.5, 'pop', 0.8), (nl('nar12') + 3.3, 'pop', 0.85),
          (nl('nar13') + 0.4, 'shimmer', 0.8)]
    tt_ = nl('nar12') + 0.95
    while tt_ < nl('nar12') + 2.7:
        EV.append((tt_, 'key', 1.0)); tt_ += 0.075
    for (t0, kind, g) in EV:
        add_sfx(sfx, t0, kind, g)
    mix = music * 0.34 * duck[:music.shape[1]][None, :]
    mix += np.stack(sfx) * 0.9
    # 角色人声点缀(dsh→Fish,其余→Fox,与 VoiceCue 语义一致):跟气泡/点击走
    stings = [(nl('nar08') + 1.3, 'Fox.mp3'), (nl('nar09') + 2.9, 'Fish.mp3'),
              (nl('nar13') + 0.5, 'Fox.mp3'), (nl('nar13') + 1.7, 'Fish.mp3')]
    for (t0, f) in stings:
        src = os.path.join(BUILD, 'v_' + f.replace('.mp3', '.wav'))
        if not os.path.exists(src):
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', os.path.join(VOICE, f),
                            '-ar', str(SR), '-ac', '2', '-sample_fmt', 's16', src], check=True)
        d = load_voice(src, 0.9)
        i0 = int(t0 * SR); i1 = min(i0 + len(d), int(dur * SR))
        mix[:, i0:i1] += d[:i1 - i0].T
        duck_seg(t0, t0 + len(d) / SR, 0.30)
    fo = int(0.9 * SR)
    mix[:, -fo:] *= np.linspace(1, 0, fo)[None, :]
    peak = np.abs(mix).max()
    if peak > 0.97: mix *= 0.97 / peak
    pcm = (mix * 32767).astype('<i2')
    wf = wave.open(os.path.join(BUILD, 'audio2.wav'), 'wb')
    wf.setnchannels(2); wf.setsampwidth(2); wf.setframerate(SR)
    wf.writeframes(pcm.tobytes()); wf.close()
    print('audio2 ok, dur=%.2f peak=%.3f' % (dur, peak))

# ================= 主流程 =================
def low_priority():
    if sys.platform != 'win32': return
    import ctypes
    k = ctypes.windll.kernel32
    k.GetCurrentProcess.restype = ctypes.c_void_p
    h = k.GetCurrentProcess()
    if not k.SetPriorityClass(ctypes.c_void_p(h), 0x00000040):
        print('warn: SetPriorityClass failed')
    else:
        print('priority: idle (0x40)')

def probe_encoder(args):
    try:
        r = subprocess.run(['ffmpeg', '-v', 'error', '-f', 'lavfi', '-i',
                            'color=black:s=256x256:d=0.2'] + args + ['-f', 'null', '-'],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        return r.returncode == 0
    except Exception:
        return False

def pick_video_codec():
    if probe_encoder(['-c:v', 'h264_nvenc']):
        return ['-c:v', 'h264_nvenc', '-preset', 'p5', '-tune', 'hq',
                '-rc', 'vbr', '-cq', '21', '-b:v', '0', '-maxrate', '80M',
                '-spatial-aq', '1', '-profile:v', 'high']
    print('nvenc unavailable, fallback libx264')
    return ['-c:v', 'libx264', '-preset', 'medium', '-crf', '19',
            '-threads', str(max(2, (os.cpu_count() or 8) // 2))]

def render_video(out):
    cmd = ['ffmpeg', '-v', 'error', '-y',
           '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H),
           '-r', str(FPS), '-i', '-',
           '-pix_fmt', 'yuv420p', '-movflags', '+faststart'] + pick_video_codec() + [out]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    n = int(round(TLd() * FPS))
    for i in range(n):
        p.stdin.write(render_frame(i).tobytes())
        if i % 200 == 0:
            print('frame %d / %d (%.0f%%)' % (i, n, 100.0 * i / n), flush=True)
    p.stdin.close(); p.wait()
    if p.returncode != 0:
        raise SystemExit('ffmpeg encode failed')

def mux():
    subprocess.run(['ffmpeg', '-v', 'error', '-y',
                    '-i', os.path.join(BUILD, 'video2.mp4'),
                    '-i', os.path.join(BUILD, 'audio2.wav'),
                    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k',
                    '-movflags', '+faststart', FINAL], check=True)
    print('final ->', FINAL)

def main():
    global TL
    os.makedirs(BUILD, exist_ok=True)
    low_priority()
    build_bg(); build_cursor(); load_gifs()
    TL = build_timeline()
    print('timeline: dur=%.2fs' % TLd(), flush=True)
    for (key, sub, sc, t0, t1) in LINES:
        print('  %s  %.2f -> %.2f  SC%d' % (key, t0, t1, sc), flush=True)
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if what == 'frame':
        render_frame(int(float(sys.argv[2]) * FPS)).save(sys.argv[3])
        print('frame saved', sys.argv[3]); return
    if what == 'doc':
        with open(os.path.join(BUILD, 'timeline.json'), 'w', encoding='utf-8') as fp:
            json.dump({'lines': TL['lines'], 'scenes': TL['scenes'], 'dur': TLd()}, fp,
                      ensure_ascii=False, indent=1)
        print('timeline.json written'); return
    if what in ('audio', 'all'): build_audio()
    if what in ('video', 'all'): render_video(os.path.join(BUILD, 'video2.mp4'))
    if what == 'all': mux()

if __name__ == '__main__':
    main()
