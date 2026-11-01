# -*- coding: utf-8 -*-
"""Agent HUD 宣传片生成器
逐帧 PIL 合成 1920x1080@30fps 动效 + numpy 合成音乐床/音效 + voice/*.mp3 人声
用法:
  python gen_promo.py audio          # 只合成音轨 build/audio.wav
  python gen_promo.py video          # 只渲染视频 build/video.mp4
  python gen_promo.py all            # 音频+视频+混流 -> promo/AgentHUD-promo.mp4
  python gen_promo.py frame T OUT    # 渲染单帧 t=T 秒到 PNG（排版调试）
"""
import math, os, sys, subprocess, wave, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE   = os.path.dirname(os.path.abspath(__file__))
ROOT   = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, 'assets')
VOICE  = os.path.join(ROOT, 'voice')
BUILD  = os.path.join(HERE, 'build')
FINAL  = os.path.join(ROOT, 'promo', 'AgentHUD-promo.mp4')

W, H, FPS = 1920, 1080, 30
DUR = 38.6
N_FRAMES = int(round(DUR * FPS))

# ---------------------------------------------------------------- timeline
S1 = (0.0, 5.2)     # 标题
S2 = (5.2, 11.2)    # HUD 滑入 + 标注
S3 = (11.2, 16.8)   # HDR 双击
S4 = (16.8, 23.2)   # 抽屉/额度圈
S5 = (23.2, 29.2)   # 气泡/权限
S6 = (29.2, 33.8)   # 三态
S7 = (33.8, DUR)    # 结尾

INK   = (242, 245, 250, 255)
DIM   = (154, 167, 189, 255)
GREEN = (52, 211, 153, 255)
GREEN2= (14, 165, 130, 255)
AMBER = (245, 158, 11, 255)
RED   = (239, 68, 68, 255)
BLUE  = (109, 138, 255, 255)   # DeepSeek
VIOLET= (167, 139, 250, 255)   # GLM/噶漏姆

def F(p): return os.path.join(r'C:\Windows\Fonts', p)
def f_reg(s):  return ImageFont.truetype(F('msyh.ttc'), s)
def f_bold(s): return ImageFont.truetype(F('msyhbd.ttc'), s)
def f_mono(s): return ImageFont.truetype(F('consola.ttf'), s)

# ---------------------------------------------------------------- easing
def cl01(x): return 0.0 if x < 0 else (1.0 if x > 1 else x)
def eoc(x):  x = cl01(x); return 1 - (1 - x) ** 3
def eio(x):  x = cl01(x); return x * x * (3 - 2 * x)
def eob(x, s=1.70158):
    x = cl01(x) - 1
    return 1 + x * x * ((s + 1) * x + s)
def lerp(a, b, k): return a + (b - a) * k
def fade_io(t, t0, t1, fi=0.35, fo=0.35):
    return cl01((t - t0) / fi) * cl01((t1 - t) / fo)

# ---------------------------------------------------------------- assets
GIFS = {}
def load_gifs():
    for n in ['df-normal', 'df-worry', 'df-daze', 'zc-normal', 'zc-worry', 'zc-daze']:
        im = Image.open(os.path.join(ASSETS, 'anim-%s.gif' % n))
        fr = []
        i = 0
        while True:
            try: im.seek(i)
            except EOFError: break
            fr.append(im.convert('RGBA'))
            i += 1
        GIFS[n] = fr

CHACHE = {}
def char(name, t, size):
    fr = GIFS[name]
    fi = int(t * 25) % len(fr)
    key = (name, fi, size)
    r = CHACHE.get(key)
    if r is None:
        r = fr[fi].resize((size, size), Image.LANCZOS)
        CHACHE[key] = r
    return r

def paste(base, spr, xy, alpha=1.0):
    xy = (int(xy[0]), int(xy[1]))
    if alpha >= 0.999:
        base.alpha_composite(spr, xy)
    else:
        if alpha <= 0.003: return
        a = spr.getchannel('A').point(lambda v: int(v * alpha))
        tmp = spr.copy(); tmp.putalpha(a)
        base.alpha_composite(tmp, xy)

def paste_c(base, spr, center, alpha=1.0):
    paste(base, spr, (center[0] - spr.width / 2, center[1] - spr.height / 2), alpha)

def new_layer(): return Image.new('RGBA', (W, H), (0, 0, 0, 0))

def scale_fade(lay, k):
    """整体乘 alpha"""
    if k >= 0.999: return lay
    a = lay.getchannel('A').point(lambda v: int(v * k))
    lay.putalpha(a)
    return lay

def rrect(d, box, r, **kw): d.rounded_rectangle(box, radius=r, **kw)

# ---------------------------------------------------------------- 预生成素材
SPR = {}

def make_gradient_v(w, h, c0, c1):
    g = Image.new('RGBA', (w, h))
    px = []
    for y in range(h):
        k = y / max(1, h - 1)
        px.append(tuple(int(lerp(c0[i], c1[i], k)) for i in range(4)))
    g.putdata(px * w if False else [px[y // 1][0] if False else px[min(h - 1, y)] for y in range(h)] )
    # 上面写复杂了，直接逐行
    g = Image.new('RGBA', (w, h))
    d = ImageDraw.Draw(g)
    for y in range(h):
        k = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(lerp(c0[i], c1[i], k)) for i in range(4)))
    return g

def glow_under(w, h, r, color, blur, alpha=110):
    m = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(m)
    rrect(d, (blur * 2, blur * 2, w - blur * 2, h - blur * 2), r, fill=color + (alpha,))
    return m.filter(ImageFilter.GaussianBlur(blur))

def build_bg():
    base = make_gradient_v(W, H, (12, 15, 27, 255), (23, 29, 51, 255)).convert('RGBA')
    # 中央柔光
    glow = Image.new('L', (W, H), 0)
    dg = ImageDraw.Draw(glow)
    dg.ellipse((W/2 - 720, H/2 - 500, W/2 + 720, H/2 + 500), fill=80)
    glow = glow.filter(ImageFilter.GaussianBlur(220))
    tint = Image.new('RGBA', (W, H), (52, 70, 118, 255))
    base.paste(tint, (0, 0), glow)
    # 点阵
    dots = new_layer()
    dd = ImageDraw.Draw(dots)
    for gy in range(60, H, 76):
        for gx in range(60, W, 76):
            dd.ellipse((gx - 1, gy - 1, gx + 1, gy + 1), fill=(255, 255, 255, 14))
    base.alpha_composite(dots)
    # 晕影
    vig = Image.new('L', (W, H), 0)
    dv = ImageDraw.Draw(vig)
    dv.rectangle((0, 0, W, H), fill=88)
    dv.ellipse((-260, -200, W + 260, H + 200), fill=0)
    vig = vig.filter(ImageFilter.GaussianBlur(150))
    vlay = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    vlay.putalpha(vig)
    base.alpha_composite(vlay)
    SPR['bg'] = base
    # bokeh 精灵
    bs = 360
    bok = Image.new('RGBA', (bs, bs), (0, 0, 0, 0))
    db = ImageDraw.Draw(bok)
    for rr, aa in [(170, 26), (120, 40), (70, 60)]:
        db.ellipse((bs/2 - rr, bs/2 - rr, bs/2 + rr, bs/2 + rr), fill=aa)
    bok = bok.filter(ImageFilter.GaussianBlur(40))
    SPR['bokeh'] = bok
    # HDR 洗染
    wash = make_gradient_v(W, H, (24, 190, 178, 255), (110, 80, 255, 255))
    mask = Image.new('L', (W, H), 0)
    dm = ImageDraw.Draw(mask)
    dm.ellipse((W/2 - 900, H/2 - 620, W/2 + 900, H/2 + 620), fill=26)
    mask = mask.filter(ImageFilter.GaussianBlur(200))
    wash.putalpha(mask)
    SPR['wash'] = wash

BOKEH = [  # (基础x, y, 半径, 颜色, 速度, 相位)
    (300, 240, 0.9, (70, 110, 255), 0.13, 0.0),
    (1560, 300, 0.7, (150, 110, 255), 0.10, 1.3),
    (480, 860, 0.8, (40, 190, 200), 0.08, 2.2),
    (1380, 820, 1.0, (80, 100, 255), 0.11, 3.9),
    (980, 140, 0.6, (150, 110, 255), 0.09, 5.1),
]
def draw_bg(img, t, hdr_on=0.0):
    img.alpha_composite(SPR['bg'])
    for (bx, by, sc, col, sp, ph) in BOKEH:
        x = bx + math.sin(t * sp * 2 * math.pi + ph) * 60
        y = by + math.cos(t * sp * 2 * math.pi * 0.8 + ph) * 40
        spr = SPR['bokeh']
        a = 1.0 + 0.55 * hdr_on
        tint = Image.new('RGBA', spr.size, col + (255,))
        tint.putalpha(spr.getchannel('A').point(lambda v: min(255, int(v * a))))
        s = spr.resize((int(spr.width * sc), int(spr.height * sc)))
        tt = tint.resize(s.size)
        paste(img, tt, (x - s.width / 2, y - s.height / 2))
    if hdr_on > 0:
        paste(img, SPR['wash'], (0, 0), alpha=hdr_on)

# ---------------------------------------------------------------- HUD
HUD_X0, HUD_W, HUD_Y0 = 1632, 240, 267
PAD, BTN, GAP, ROW = 24, 168, 14, 150
BTN_Y = HUD_Y0 + PAD
SEP1_Y = BTN_Y + BTN + GAP
ROW1_C = SEP1_Y + 1 + GAP + ROW // 2
SEP2_Y = SEP1_Y + 1 + GAP + ROW
ROW2_C = SEP2_Y + 1 + GAP + ROW // 2
HUD_H  = (SEP2_Y + 1 + GAP + ROW + PAD) - HUD_Y0
GM = 44  # glow margin for sprite

def build_hud_sprites():
    pw, ph = HUD_W + GM * 2, HUD_H + GM * 2
    spr = Image.new('RGBA', (pw, ph), (0, 0, 0, 0))
    # 阴影+外发光
    gl = Image.new('RGBA', (pw, ph), (0, 0, 0, 0))
    dg = ImageDraw.Draw(gl)
    rrect(dg, (GM, GM, GM + HUD_W, GM + HUD_H), 26, fill=(90, 130, 255, 46))
    gl = gl.filter(ImageFilter.GaussianBlur(26))
    spr.alpha_composite(gl)
    d = ImageDraw.Draw(spr)
    # 玻璃面板
    rrect(d, (GM, GM, GM + HUD_W, GM + HUD_H), 26,
          fill=(17, 22, 38, 224), outline=(255, 255, 255, 34), width=2)
    d.line([(GM + 26, GM + 3), (GM + HUD_W - 26, GM + 3)], fill=(255, 255, 255, 60), width=2)
    # HDR 绿钮
    bx0, by0, bx1, by1 = GM + 36, GM + PAD, GM + HUD_W - 36, GM + PAD + BTN
    bgl = Image.new('RGBA', (pw, ph), (0, 0, 0, 0))
    dbg = ImageDraw.Draw(bgl)
    rrect(dbg, (bx0, by0, bx1, by1), 48, fill=(52, 230, 160, 120))
    bgl = bgl.filter(ImageFilter.GaussianBlur(22))
    spr.alpha_composite(bgl)
    grad = make_gradient_v(bx1 - bx0, by1 - by0, (72, 226, 168, 255), (16, 168, 122, 255))
    mask = Image.new('L', (bx1 - bx0, by1 - by0), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, bx1 - bx0 - 1, by1 - by0 - 1), 48, fill=255)
    spr.paste(grad, (bx0, by0), mask)
    d = ImageDraw.Draw(spr)
    d.line([(bx0 + 40, by0 + 14), (bx1 - 40, by0 + 14)], fill=(255, 255, 255, 130), width=4)
    tf = f_bold(46)
    d.text(((bx0 + bx1) / 2, (by0 + by1) / 2), 'HDR', font=tf, fill=(255, 255, 255, 255), anchor='mm')
    # 分隔线
    for sy in (SEP1_Y, SEP2_Y):
        d.line([(GM + 30, sy + GM - HUD_Y0), (GM + HUD_W - 30, sy + GM - HUD_Y0)],
               fill=(255, 255, 255, 40), width=1)
    SPR['hud'] = spr
    SPR['hud_xy'] = (HUD_X0 - GM, HUD_Y0 - GM)
    # amber 确认态覆盖层
    arm = Image.new('RGBA', (pw, ph), (0, 0, 0, 0))
    agl = Image.new('RGBA', (pw, ph), (0, 0, 0, 0))
    dag = ImageDraw.Draw(agl)
    rrect(dag, (bx0, by0, bx1, by1), 48, fill=(255, 190, 80, 150))
    arm.alpha_composite(agl.filter(ImageFilter.GaussianBlur(24)))
    grad2 = make_gradient_v(bx1 - bx0, by1 - by0, (255, 196, 87, 255), (198, 116, 10, 255))
    arm.paste(grad2, (bx0, by0), mask)
    da = ImageDraw.Draw(arm)
    da.text(((bx0 + bx1) / 2, (by0 + by1) / 2), '确认?', font=f_bold(44), fill=(40, 24, 4, 255), anchor='mm')
    SPR['hud_arm'] = arm

def draw_hud(lay, t, fade=1.0, slide=0.0, dots=('g', 'g'), armed=False, flash=0.0,
             ch_df='df-normal', ch_zc='zc-normal'):
    xy = (SPR['hud_xy'][0] + slide, SPR['hud_xy'][1])
    paste(lay, SPR['hud'], xy, alpha=fade)
    if armed:
        paste(lay, SPR['hud_arm'], xy, alpha=fade)
    if flash > 0:
        d = ImageDraw.Draw(lay)
        x0 = HUD_X0 + slide + 36; y0 = BTN_Y; x1 = x0 + HUD_W - 72; y1 = y0 + BTN
        a = int(220 * flash)
        rrect(d, (x0, y0, x1, y1), 48, outline=(255, 255, 255, a), width=6)
    bob = math.sin(t * 2 * math.pi * 0.55) * 3
    paste_c(lay, char(ch_df, t, 130), (HUD_X0 + slide + HUD_W / 2, ROW1_C + bob), alpha=fade)
    paste_c(lay, char(ch_zc, t, 130), (HUD_X0 + slide + HUD_W / 2, ROW2_C - bob), alpha=fade)
    dcol = {'g': (70, 220, 150), 'y': (245, 197, 66), 'r': (245, 90, 80), 'x': (130, 140, 155)}
    d = ImageDraw.Draw(lay)
    for (cy, st) in ((ROW1_C, dots[0]), (ROW2_C, dots[1])):
        cx = HUD_X0 + slide + HUD_W / 2 + 48
        cy = cy + 44
        c = dcol[st]
        d.ellipse((cx - 11, cy - 11, cx + 11, cy + 11), fill=c + (int(255 * fade),))
        d.ellipse((cx - 16, cy - 16, cx + 16, cy + 16), outline=c + (int(90 * fade),), width=2)

def draw_sliver(lay, t, fade=1.0):
    d = ImageDraw.Draw(lay)
    a = int(235 * fade)
    rrect(d, (W - 34, HUD_Y0, W + 30, HUD_Y0 + HUD_H), 18, fill=(20, 26, 44, a),
          outline=(255, 255, 255, int(40 * fade)), width=2)
    d.line([(W - 20, HUD_Y0 + HUD_H / 2), (W - 8, HUD_Y0 + HUD_H / 2)],
           fill=(255, 255, 255, int(90 * fade)), width=3)

# ---------------------------------------------------------------- 光标
def build_cursor():
    s = 2.6
    pts = [(0, 0), (0, 21), (4.6, 16.6), (8.2, 25.4), (12.2, 23.6), (8.6, 15.2), (15.6, 15.2)]
    spr = Image.new('RGBA', (60, 76), (0, 0, 0, 0))
    d = ImageDraw.Draw(spr)
    poly = [(x * s + 4, y * s + 4) for x, y in pts]
    d.polygon(poly, fill=(245, 248, 252, 255), outline=(10, 12, 18, 255))
    d.line(poly + [poly[0]], fill=(10, 12, 18, 255), width=3)
    SPR['cursor'] = spr

def draw_cursor(lay, pos, alpha=1.0):
    paste(lay, SPR['cursor'], pos, alpha)

def draw_ripple(lay, pos, k):
    r = lerp(10, 52, k); a = int(220 * (1 - k))
    d = ImageDraw.Draw(lay)
    d.ellipse((pos[0] - r, pos[1] - r, pos[0] + r, pos[1] + r),
              outline=(255, 255, 255, a), width=max(2, int(6 * (1 - k))))

# ---------------------------------------------------------------- 通用小件
def glass_panel(w, h, r=22, fill=(18, 23, 40, 226), border_a=40):
    m = 30
    spr = Image.new('RGBA', (w + m * 2, h + m * 2), (0, 0, 0, 0))
    sh = Image.new('RGBA', spr.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((m, m, m + w, m + h), r, fill=(0, 0, 0, 120))
    spr.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18)))
    d = ImageDraw.Draw(spr)
    rrect(d, (m, m, m + w, m + h), r, fill=fill, outline=(255, 255, 255, border_a), width=2)
    d.line([(m + r, m + 3), (m + w - r, m + 3)], fill=(255, 255, 255, 55), width=2)
    return spr, m

def panel_center(spr):
    return (spr.width / 2, spr.height / 2)

def caption(lay, text, alpha, y=992, size=46, color=INK):
    if alpha <= 0.01: return
    f = f_bold(size)
    d = ImageDraw.Draw(lay)
    tw = d.textlength(text, font=f)
    x0 = W / 2 - tw / 2 - 34
    rrect(d, (x0, y - size * 0.82, x0 + tw + 68, y + size * 0.82), size * 0.82,
          fill=(12, 16, 30, int(150 * alpha)), outline=(255, 255, 255, int(30 * alpha)), width=2)
    d.text((W / 2, y), text, font=f, fill=color[:3] + (int(255 * alpha),), anchor='mm')

def chip(lay, center, text, alpha, color=GREEN, size=36):
    if alpha <= 0.01: return
    f = f_bold(size)
    d = ImageDraw.Draw(lay)
    tw = d.textlength(text, font=f)
    w = tw + 108; h = size * 1.9
    x0 = center[0] - w / 2; y0 = center[1] - h / 2
    rrect(d, (x0, y0, x0 + w, y0 + h), h / 2, fill=(10, 26, 22, int(200 * alpha)),
          outline=color[:3] + (int(200 * alpha),), width=3)
    d.ellipse((x0 + 32, center[1] - 9, x0 + 50, center[1] + 9), fill=color[:3] + (int(255 * alpha),))
    d.text((x0 + 70, center[1]), text, font=f, fill=(color[0], color[1], color[2], int(255 * alpha)), anchor='lm')

def draw_note(dd, xy, label, color, size=26, align='l'):
    """♪ + 文字(双字体,msyh 无 U+266A)"""
    fnote = ImageFont.truetype(F('seguisym.ttf'), size)
    fm = f_reg(size)
    nw = dd.textlength('♪', font=fnote)
    if align == 'l':
        dd.text(xy, '♪', font=fnote, fill=color, anchor='lm')
        dd.text((xy[0] + nw + 7, xy[1]), label, font=fm, fill=color, anchor='lm')
    else:
        lw = dd.textlength(label, font=fm)
        dd.text(xy, label, font=fm, fill=color, anchor='rm')
        dd.text((xy[0] - lw - 7, xy[1]), '♪', font=fnote, fill=color, anchor='rm')

def ring(lay, center, radius, pct, color, alpha=1.0, width=18, track=46):
    d = ImageDraw.Draw(lay)
    a = int(255 * alpha)
    bbox = (center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius)
    d.arc(bbox, 0, 360, fill=(255, 255, 255, int(track * alpha)), width=width)
    if pct > 0.005:
        d.arc(bbox, -90, -90 + 360 * pct, fill=color[:3] + (a,), width=width)

def glow_text(lay, center, text, font, fill, glow=(120, 170, 255), blur=22, galpha=140, alpha=1.0):
    tmp = Image.new('RGBA', lay.size, (0, 0, 0, 0))
    td = ImageDraw.Draw(tmp)
    td.text(center, text, font=font, fill=glow + (int(galpha * alpha),), anchor='mm')
    tmp = tmp.filter(ImageFilter.GaussianBlur(blur))
    lay.alpha_composite(tmp)
    d = ImageDraw.Draw(lay)
    d.text(center, text, font=font, fill=fill[:3] + (int(255 * alpha),), anchor='mm')

# ---------------------------------------------------------------- 场景
def title_sprite():
    if 'title' in SPR: return
    f = f_bold(150)
    w, h = 1100, 260
    spr = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(spr)
    d.text((w / 2, h / 2), 'Agent HUD', font=f, fill=INK, anchor='mm')
    # HUD 渐变字:把 “HUD” 用渐变重画
    d2 = ImageDraw.Draw(spr)
    tw_full = d2.textlength('Agent ', font=f)
    tw_hud = d2.textlength('HUD', font=f)
    x0 = w / 2 - (tw_full + tw_hud) / 2 + tw_full
    grad = make_gradient_v(int(tw_hud) + 8, 170, (86, 220, 255, 255), (167, 120, 255, 255))
    mask = Image.new('L', (int(tw_hud) + 8, 170), 0)
    ImageDraw.Draw(mask).text((4, 85), 'HUD', font=f, fill=255, anchor='lm')
    spr.paste(grad, (int(x0) - 4, int(h / 2) - 85), mask)
    SPR['title'] = spr

def s1(lay, t):
    t0, t1 = S1
    k_out = 1 - cl01((t - (t1 - 0.55)) / 0.55)
    title_sprite()
    k_in = eob(cl01((t - 0.35) / 0.85))
    sc = lerp(0.90, 1.0, k_in)
    tsp = SPR['title']
    if sc < 0.999:
        tsp = tsp.resize((int(tsp.width * sc), int(tsp.height * sc)), Image.LANCZOS)
    pulse = 0.75 + 0.25 * math.sin(t * 2.4)
    gl = Image.new('RGBA', lay.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(gl)
    gd.text((W / 2, 400), 'Agent HUD', font=f_bold(150), fill=(110, 160, 255, int(90 * pulse)), anchor='mm')
    lay.alpha_composite(gl.filter(ImageFilter.GaussianBlur(30)))
    paste_c(lay, tsp, (W / 2, 400), alpha=k_in * k_out)
    d = ImageDraw.Draw(lay)
    d.text((W / 2, 560), '把 HDR、余额、订阅额度，钉在屏幕边缘',
           font=f_reg(46), fill=INK[:3] + (int(235 * eoc(cl01((t - 0.9) / 0.7)) * k_out),), anchor='mm')
    d.text((W / 2, 642), 'Win11 桌面悬浮侧边栏 · 原生 WPF · 单文件零依赖 · 开源',
           font=f_reg(30), fill=DIM[:3] + (int(220 * eoc(cl01((t - 1.25) / 0.7)) * k_out),), anchor='mm')
    # 双角色升起
    for (cx, name, dl) in ((330, 'df-normal', 0.55), (1590, 'zc-normal', 0.7)):
        k = eob(cl01((t - dl) / 0.8))
        y = lerp(1080 + 160, 930, k) + math.sin(t * 2 * math.pi * 0.5 + dl) * 6
        paste_c(lay, char(name, t, 300), (cx, y), alpha=k_out)
    caption(lay, 'soberkeen / agent-hud', cl01((t - 1.8) / 0.6) * k_out, y=60, size=28, color=DIM)

def s2(lay, t):
    tau = t - S2[0]
    slide = 0.0
    if tau < 0.2:
        draw_sliver(lay, t, fade=cl01(tau / 0.15))
    elif tau < 1.0:
        k = eob(cl01((tau - 0.2) / 0.8), 1.1)
        slide = (1 - k) * 300
        draw_hud(lay, t, slide=slide)
    else:
        draw_hud(lay, t)
    # 标注
    items = [(1.25, BTN_Y + BTN / 2, 'HDR 开关 · 双击确认', GREEN),
             (2.0, ROW1_C, '大肥鱼 · DeepSeek 余额', BLUE),
             (2.75, ROW2_C, '噶漏姆 · GLM 双额度圈', VIOLET)]
    d = ImageDraw.Draw(lay)
    for (ts, cy, txt, col) in items:
        a = eoc(cl01((tau - ts) / 0.4))
        if a <= 0.01: continue
        tx = HUD_X0 - 88
        d.text((tx, cy), txt, font=f_bold(40), fill=(col[0], col[1], col[2], int(255 * a)), anchor='rm')
        d.line([(tx + 14, cy), (HUD_X0 - 8, cy)], fill=(255, 255, 255, int(120 * a)), width=3)
        d.ellipse((HUD_X0 - 14, cy - 5, HUD_X0 - 4, cy + 5), fill=(255, 255, 255, int(200 * a)))
    caption(lay, '常驻屏幕边缘 · 半隐藏不挡视线', fade_io(t, S2[0] + 3.3, S2[1], 0.4, 0.4))

def s3(lay, t):
    tau = t - S2[1] + S2[1] - S2[1]  # 0-based
    tau = t - S3[0]
    # HUD 退场(滑到半隐藏)
    if tau < 0.5:
        k = eoc(tau / 0.5)
        draw_hud(lay, t, slide=k * 300, dots=('g', 'g'))
    else:
        draw_sliver(lay, t)
    btn_c = (560, 470); BTN2 = 380
    k_in = eob(cl01(tau / 0.5))
    # 文案
    d = ImageDraw.Draw(lay)
    a1 = eoc(cl01((tau - 0.25) / 0.5))
    d.text((880, 380), 'HDR 开关', font=f_bold(78), fill=INK[:3] + (int(255 * a1),), anchor='lm')
    d.text((880, 478), '双击确认 · 防误触', font=f_reg(48), fill=INK[:3] + (int(235 * a1),), anchor='lm')
    d.text((880, 552), '3 秒轮询同步系统状态 · SDR 白电平仲裁驱动 quirk',
           font=f_reg(30), fill=DIM[:3] + (int(220 * a1),), anchor='lm')
    # 大按钮
    armed = tau >= 1.3
    clicked = tau >= 2.9
    bs = int(BTN2 * lerp(0.8, 1.0, k_in))
    # 光晕
    gcol = (255, 190, 80) if armed else (52, 230, 160)
    gl = Image.new('RGBA', lay.size, (0, 0, 0, 0))
    dg = ImageDraw.Draw(gl)
    rrect(dg, (btn_c[0] - BTN2/2, btn_c[1] - BTN2/2, btn_c[0] + BTN2/2, btn_c[1] + BTN2/2), 100,
          fill=gcol + (int((70 if not clicked else 120) * k_in),))
    lay.alpha_composite(gl.filter(ImageFilter.GaussianBlur(40)))
    if bs > 4:
        bsp = Image.new('RGBA', (bs, bs), (0, 0, 0, 0))
        db = ImageDraw.Draw(bsp)
        if armed:
            grad = make_gradient_v(bs, bs, (255, 200, 92, 255), (204, 122, 12, 255))
        else:
            grad = make_gradient_v(bs, bs, (72, 226, 168, 255), (16, 168, 122, 255))
        mk = Image.new('L', (bs, bs), 0)
        ImageDraw.Draw(mk).rounded_rectangle((0, 0, bs - 1, bs - 1), 100, fill=255)
        bsp.paste(grad, (0, 0), mk)
        db = ImageDraw.Draw(bsp)
        db.line([(bs * 0.2, bs * 0.08), (bs * 0.8, bs * 0.08)], fill=(255, 255, 255, 120), width=8)
        if armed:
            db.text((bs / 2, bs / 2), '确认?', font=f_bold(96), fill=(42, 26, 4, 255), anchor='mm')
        else:
            db.text((bs / 2, bs / 2), 'HDR', font=f_bold(100), fill=(255, 255, 255, 255), anchor='mm')
        paste_c(lay, bsp, btn_c, alpha=k_in)
    if clicked:
        k = cl01((tau - 2.9) / 0.4)
        r = BTN2 / 2 + 20 + 40 * k
        dd = ImageDraw.Draw(lay)
        dd.ellipse((btn_c[0] - r, btn_c[1] - r, btn_c[0] + r, btn_c[1] + r),
                   outline=(255, 255, 255, int(200 * (1 - k))), width=6)
    # HDR ON 洗染
    wash_k = eoc(cl01((tau - 3.1) / 0.7)) * (1 - eoc(cl01((tau - 5.0) / 0.5)))
    if wash_k > 0.01:
        paste(lay, SPR['wash'], (0, 0), alpha=wash_k * 0.8)
    if tau >= 3.25:
        chip(lay, (560, 760), 'HDR ON · 已同步', eoc(cl01((tau - 3.25) / 0.4)))
    # 光标
    cpath = None
    if tau < 1.3:
        k = eio(cl01((tau - 0.55) / 0.6))
        cpath = (lerp(1020, btn_c[0] + 40, k), lerp(950, btn_c[1] + 40, k))
    elif tau < 2.9:
        k = eio(cl01((tau - 1.8) / 0.7))
        cpath = (lerp(btn_c[0] + 40, btn_c[0] - 60, k), lerp(btn_c[1] + 40, btn_c[1] - 30, k))
    else:
        cpath = (btn_c[0] - 60, btn_c[1] - 30)
    if tau > 0.5:
        draw_cursor(lay, cpath)
    if abs(tau - 1.3) < 0.13:
        draw_ripple(lay, (btn_c[0] + 40, btn_c[1] + 40), (tau - 1.17) / 0.26)
    if abs(tau - 2.9) < 0.13:
        draw_ripple(lay, (btn_c[0] - 60, btn_c[1] - 30), (tau - 2.77) / 0.26)
    caption(lay, '状态即时生效 · 长稳零回弹', fade_io(t, S3[0] + 4.2, S3[1], 0.35, 0.4))

def s4(lay, t):
    tau = t - S4[0]
    if tau < 0.7:
        if tau < 0.12:
            draw_sliver(lay, t)
        else:
            k = eob(cl01((tau - 0.1) / 0.6), 1.1)
            draw_hud(lay, t, slide=(1 - k) * 300, ch_df='df-normal', ch_zc='zc-normal')
    else:
        draw_hud(lay, t, ch_df='df-normal', ch_zc='zc-normal')
    d = ImageDraw.Draw(lay)
    # 光标走位
    cur = (1500, 1000)
    hover = None
    if tau < 0.6:
        k = eio(cl01(tau / 0.6)); cur = (lerp(1500, HUD_X0 + 120, k), lerp(1000, ROW1_C, k))
    elif tau < 2.6:
        cur = (HUD_X0 + 120, ROW1_C); hover = 'ds'
    elif tau < 3.0:
        k = eio(cl01((tau - 2.6) / 0.4)); cur = (HUD_X0 + 120, lerp(ROW1_C, ROW2_C, k))
    elif tau < 5.9:
        cur = (HUD_X0 + 120, ROW2_C); hover = 'zc'
    else:
        k = eio(cl01((tau - 5.9) / 0.4)); cur = (HUD_X0 + 120, lerp(ROW2_C, ROW2_C - 130, k))
    # DS 抽屉
    if hover == 'ds' or (2.6 <= tau < 2.85):
        k = eob(cl01((tau - 0.9) / 0.3 if tau < 2.6 else (2.85 - tau) / 0.25), 1.2)
        if k > 0.02: draw_ds_drawer(lay, k, t)
    # ZC 抽屉
    if tau >= 3.3:
        k = eob(cl01((tau - 3.3) / 0.35), 1.2)
        if k > 0.02 and tau < 5.8:
            draw_zc_drawer(lay, k, tau - 3.5)
        elif tau >= 5.8:
            k2 = 1 - eoc(cl01((tau - 5.8) / 0.3))
            if k2 > 0.02: draw_zc_drawer(lay, k2, 2.3)
    if tau > 0.45: draw_cursor(lay, cur)
    if abs(tau - 0.9) < 0.13: draw_ripple(lay, (HUD_X0 + 120, ROW1_C), (tau - 0.77) / 0.26)
    if abs(tau - 3.3) < 0.13: draw_ripple(lay, (HUD_X0 + 120, ROW2_C), (tau - 3.17) / 0.26)
    caption(lay, '悬停弹出 · 单击固定 · 双击启动对应程序', fade_io(t, S4[0] + 0.9, S4[1], 0.4, 0.4))

def draw_ds_drawer(lay, k, t):
    w, h = 560, 190
    spr, m = glass_panel(w, h)
    sc = lerp(0.86, 1.0, k)
    spr = spr.resize((int(spr.width * sc), int(spr.height * sc)), Image.LANCZOS)
    x1 = HUD_X0 - 22; y1 = ROW1_C
    paste_c(lay, spr, (x1 - spr.width / 2 + m * sc, y1), alpha=cl01(k * 3))
    # 内容
    tmp = new_layer()
    dd = ImageDraw.Draw(tmp)
    cx = x1 - w - 22 + w / 2 + m  # 面板内容中心
    ox = x1 - w - 22 + m
    oy = ROW1_C - h / 2
    dd.text((ox + 40, oy + 52), 'DeepSeek 余额', font=f_bold(40), fill=BLUE, anchor='lm')
    dd.text((ox + 40, oy + 118), '¥ 42.13', font=f_bold(64), fill=INK, anchor='lm')
    dd.text((ox + 40, oy + 166), 'key 存 Windows 凭据管理器 · 配置文件不落密钥',
            font=f_reg(26), fill=DIM, anchor='lm')
    kk = cl01(k * 3)
    scale_fade(tmp, kk)
    lay.alpha_composite(tmp)

def draw_zc_drawer(lay, k, fill_t):
    w, h = 560, 290
    spr, m = glass_panel(w, h)
    sc = lerp(0.86, 1.0, k)
    spr = spr.resize((int(spr.width * sc), int(spr.height * sc)), Image.LANCZOS)
    x1 = HUD_X0 - 22; y1 = ROW2_C
    paste_c(lay, spr, (x1 - spr.width / 2 + m * sc, y1), alpha=cl01(k * 3))
    tmp = new_layer()
    dd = ImageDraw.Draw(tmp)
    ox = x1 - w - 22 + m
    oy = ROW2_C - h / 2
    dd.text((ox + 40, oy + 46), 'GLM Coding Plan', font=f_bold(40), fill=VIOLET, anchor='lm')
    datas = [('五小时', 0.66), ('本周', 0.57)]
    for i, (lab, pct) in enumerate(datas):
        cx = ox + 150 + i * 260
        cy = oy + 130
        kk = eoc(cl01(fill_t / 1.2 - i * 0.15))
        ring(tmp, (cx, cy), 52, pct * kk, GREEN if pct * kk > 0.5 else AMBER, width=15)
        dd.text((cx, cy), '%d%%' % round(pct * 100 * kk), font=f_bold(40), fill=INK, anchor='mm')
        dd.text((cx, oy + 206), lab, font=f_reg(30), fill=DIM, anchor='mm')
    dd.text((ox + 40, oy + 256), '自动读取 ZCode 登录凭证 · 零配置', font=f_reg(26), fill=DIM, anchor='lm')
    kk = cl01(k * 3)
    scale_fade(tmp, kk)
    lay.alpha_composite(tmp)

def s5(lay, t):
    tau = t - S5[0]
    draw_hud(lay, t)
    # 气泡 1(噶漏姆/任务完成)
    if tau >= 0.4:
        k = eob(cl01((tau - 0.4) / 0.35), 1.3)
        if k > 0.02: draw_bubble(lay, ROW2_C, k, t,
            ('任务完成', 'ZCode 会话已结束', GREEN), voice_jig=tau - 0.45)
    # 气泡 2(大肥鱼/权限确认)
    if tau >= 2.8:
        k = eob(cl01((tau - 2.8) / 0.35), 1.3)
        allow_click = tau >= 4.4
        if k > 0.02: draw_confirm_bubble(lay, ROW1_C, k, t, allow_click)
    # 光标
    if tau >= 3.4:
        k = eio(cl01((tau - 3.4) / 0.9))
        cur = (lerp(1500, 1218, k), lerp(950, ROW1_C + 26, k))
        draw_cursor(lay, cur)
        if abs(tau - 4.4) < 0.13: draw_ripple(lay, (1218, ROW1_C + 26), (tau - 4.27) / 0.26)
    caption(lay, 'ZCode hooks 接入 · 通知气泡 + 权限确认远程回传', fade_io(t, S5[0] + 0.9, S5[1], 0.4, 0.4))

def draw_bubble(lay, row_c, k, t, content, voice_jig=None):
    w, h = 470, 172
    spr, m = glass_panel(w, h, r=26)
    sc = lerp(0.8, 1.0, k)
    spr = spr.resize((int(spr.width * sc), int(spr.height * sc)), Image.LANCZOS)
    box_c = row_c + 52
    y = box_c
    if voice_jig is not None and 0 < voice_jig < 0.7:
        y += math.sin(voice_jig * 2 * math.pi * 16) * 4 * (1 - voice_jig / 0.7)
    x1 = HUD_X0 - 26
    paste_c(lay, spr, (x1 - spr.width / 2 + m * sc, y))
    tmp = new_layer()
    dd = ImageDraw.Draw(tmp)
    ox = x1 - w - 26 + m; oy = box_c - h / 2
    # 尾巴
    dd.polygon([(ox + w - 12, row_c - 16), (ox + w + 16, row_c), (ox + w - 12, row_c + 16)],
               fill=(18, 23, 40, 235))
    cc = content[2]
    dd.ellipse((ox + 40, oy + 40, ox + 92, oy + 92), outline=cc, width=5)
    dd.line([(ox + 54, oy + 68), (ox + 66, oy + 82), (ox + 88, oy + 48)], fill=cc, width=6)
    dd.text((ox + 120, oy + 48), content[0], font=f_bold(44), fill=INK, anchor='lm')
    dd.text((ox + 120, oy + 100), content[1], font=f_reg(32), fill=DIM, anchor='lm')
    draw_note(dd, (ox + 40, oy + h - 26), ' 噶漏姆~', VIOLET[:3] + (235,))
    kk = cl01(k * 3)
    scale_fade(tmp, kk)
    lay.alpha_composite(tmp)

def draw_confirm_bubble(lay, row_c, k, t, clicked):
    w, h = 560, 244
    spr, m = glass_panel(w, h, r=26)
    sc = lerp(0.8, 1.0, k)
    spr = spr.resize((int(spr.width * sc), int(spr.height * sc)), Image.LANCZOS)
    box_c = row_c - 22
    x1 = HUD_X0 - 26
    paste_c(lay, spr, (x1 - spr.width / 2 + m * sc, box_c))
    tmp = new_layer()
    dd = ImageDraw.Draw(tmp)
    ox = x1 - w - 26 + m; oy = box_c - h / 2
    dd.polygon([(ox + w - 12, row_c - 16), (ox + w + 16, row_c), (ox + w - 12, row_c + 16)],
               fill=(18, 23, 40, 235))
    dd.text((ox + 40, oy + 50), '权限请求', font=f_bold(44), fill=AMBER, anchor='lm')
    draw_note(dd, (ox + w - 40, oy + 50), '大肥鱼deepdeep~', BLUE[:3] + (235,), align='r')
    dd.text((ox + 40, oy + 102), 'agent-hud.exe 请求执行构建', font=f_reg(32), fill=INK, anchor='lm')
    # 按钮
    bx1, by = ox + w - 44, oy + 162
    bw, bh = 128, 52
    k1 = eoc(cl01((t - S5[0] - 4.4) / 0.25))
    rrect(dd, (bx1 - bw, by - bh / 2, bx1, by + bh / 2), 14,
          fill=(30, 120, 90, 255) if clicked else (0, 0, 0, 0),
          outline=GREEN, width=3)
    dd.text((bx1 - bw / 2, by), '允许', font=f_bold(34),
            fill=(255, 255, 255) if clicked else GREEN, anchor='mm')
    rrect(dd, (bx1 - bw * 2 - 18, by - bh / 2, bx1 - bw - 18, by + bh / 2), 14,
          outline=(150, 160, 175, 255), width=3)
    dd.text((bx1 - bw * 1.5 - 18, by), '拒绝', font=f_bold(34), fill=DIM, anchor='mm')
    if clicked and k1 > 0.01:
        dd.text((ox + 40, oy + 218), '已放行 · hook 退出码 0', font=f_reg(28),
                fill=GREEN[:3] + (int(255 * k1),), anchor='lm')
    kk = cl01(k * 3)
    scale_fade(tmp, kk)
    lay.alpha_composite(tmp)

def s6(lay, t):
    tau = t - S6[0]
    if tau < 0.45:
        draw_hud(lay, t, slide=eoc(tau / 0.45) * 300)
    else:
        draw_sliver(lay, t)
    cols = [(480, 0.4, 0.80, '标准', ('df-normal', 'zc-normal'), GREEN),
            (960, 1.4, 0.40, '紧张', ('df-worry', 'zc-worry'), AMBER),
            (1440, 2.4, 0.10, '痴呆', ('df-daze', 'zc-daze'), RED)]
    for (cx, ts, pct, mood, (cn, cz), col) in cols:
        k = eob(cl01((tau - ts) / 0.45), 1.2)
        if k <= 0.02: continue
        sc = lerp(0.85, 1.0, k)
        ring(lay, (cx, 300), int(96 * sc), pct * cl01(k * 2), col, width=20)
        d = ImageDraw.Draw(lay)
        d.text((cx, 300), '%d%%' % int(pct * 100), font=f_bold(52),
               fill=INK[:3] + (int(255 * cl01(k * 2)),), anchor='mm')
        bob = math.sin(t * 2 * math.pi * 0.55 + cx) * 4
        paste_c(lay, char(cn, t, 180), (cx - 80, 560 + bob), alpha=cl01(k * 2))
        paste_c(lay, char(cz, t, 180), (cx + 80, 560 - bob), alpha=cl01(k * 2))
        d.text((cx, 700), mood, font=f_bold(38), fill=col[:3] + (int(255 * cl01(k * 2)),), anchor='mm')
        d.text((cx, 752), ('>50%' if pct > 0.5 else ('20~50%' if pct > 0.2 else '<20%')),
               font=f_reg(28), fill=DIM[:3] + (int(220 * cl01(k * 2)),), anchor='mm')
    caption(lay, '额度状态实时驱动角色三态', fade_io(t, S6[0] + 0.8, S6[1], 0.4, 0.4))

def s7(lay, t):
    tau = t - S7[0]
    k_in = eoc(cl01(tau / 0.7))
    d = ImageDraw.Draw(lay)
    glow_text(lay, (W / 2, 300), 'Agent HUD', f_bold(128), INK,
              glow=(110, 160, 255), blur=26, galpha=110, alpha=k_in)
    d.text((W / 2, 415), '免费 · 开源 · 单文件零依赖 · 双角色动画',
           font=f_reg(42), fill=INK[:3] + (int(235 * eoc(cl01((tau - 0.3) / 0.5))),), anchor='mm')
    d.text((W / 2, 500), 'github.com/soberkeen/agent-hud',
           font=f_mono(44), fill=(120, 190, 255, int(255 * eoc(cl01((tau - 0.5) / 0.5)))), anchor='mm')
    chip(lay, (W / 2, 592), 'AgentHUD-Setup.exe · 下载即装', eoc(cl01((tau - 0.7) / 0.5)))
    for (cx, name, dl) in ((700, 'df-normal', 0.35), (1220, 'zc-normal', 0.5)):
        k = eob(cl01((tau - dl) / 0.7))
        y = lerp(1140, 880, k) + math.sin(t * 2 * math.pi * 0.5 + dl) * 6
        paste_c(lay, char(name, t, 320), (cx, y), alpha=k)
    caption(lay, '让 Agent 住进你的桌面', eoc(cl01((tau - 1.1) / 0.6)), y=700, size=40, color=DIM)

SCENES = [(S1, s1), (S2, s2), (S3, s3), (S4, s4), (S5, s5), (S6, s6), (S7, s7)]

def render_frame(idx):
    t = idx / FPS
    img = Image.new('RGBA', (W, H), (5, 6, 12, 255))  # 不透明底,杜绝 convert 丢 alpha 露底
    hdr_on = 0.0
    if S3[0] + 3.4 <= t < S3[1] - 0.3: hdr_on = 1.0
    elif S3[1] - 0.3 <= t < S3[1] + 0.4: hdr_on = 1 - (t - (S3[1] - 0.3)) / 0.7
    draw_bg(img, t, hdr_on=cl01(hdr_on))
    lay = new_layer()
    for (rng, fn) in SCENES:
        if rng[0] - 0.6 <= t <= rng[1] + 0.05:
            fn(lay, min(t, rng[1]))
    img.alpha_composite(lay)
    # 全局淡入淡出
    ga = cl01(t / 0.7) * cl01((DUR - t) / 0.7)
    if ga < 0.999:
        scale_fade(img, ga)
        black = Image.new('RGBA', (W, H), (5, 6, 12, 255))
        img = Image.blend(black, img, ga)
    return img.convert('RGB')

# ---------------------------------------------------------------- 音频
SR = 44100
def midi2f(m): return 440.0 * 2 ** ((m - 69) / 12.0)

def note(buf, t0, m, dur, gain, pan=0.0, bright=0.35):
    n = int(dur * SR)
    if n <= 0 or t0 * SR > len(buf[0]): return
    tt = np.arange(n) / SR
    f = midi2f(m)
    env = np.minimum(tt / 0.006, 1) * np.exp(-tt / (dur * 0.55))
    wv = np.sin(2 * np.pi * f * tt) + bright * np.sin(4 * np.pi * f * tt) + 0.14 * np.sin(6 * np.pi * f * tt)
    sig = wv * env * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    seg = sig[:i1 - i0]
    gl = np.sqrt(0.5 * (1 - pan)); gr = np.sqrt(0.5 * (1 + pan))
    buf[0][i0:i1] += seg * gl * 2 ** 0.5
    buf[1][i0:i1] += seg * gr * 2 ** 0.5

def kick(buf, t0, gain=0.5):
    n = int(0.16 * SR); tt = np.arange(n) / SR
    f = 110 * np.exp(-tt / 0.03) + 42
    ph = 2 * np.pi * np.cumsum(f) / SR
    sig = np.sin(ph) * np.exp(-tt / 0.09) * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def hat(buf, t0, gain=0.07):
    n = int(0.05 * SR)
    rng = np.random.default_rng(int(t0 * 1000) % 99991)
    x = rng.standard_normal(n)
    x = np.diff(x, prepend=0)  # 高通
    env = np.exp(-np.arange(n) / SR / 0.012)
    sig = x * env * gain * 0.3
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def bass(buf, t0, m, dur, gain=0.20):
    n = int(dur * SR); tt = np.arange(n) / SR
    f = midi2f(m)
    env = np.minimum(tt / 0.01, 1) * np.exp(-tt / (dur * 0.6))
    sig = np.sin(2 * np.pi * f * tt) * env * gain
    i0 = int(t0 * SR); i1 = min(i0 + n, len(buf[0]))
    buf[0][i0:i1] += sig[:i1 - i0]; buf[1][i0:i1] += sig[:i1 - i0]

def pad_chord(buf, t0, ms, dur, gain=0.055):
    n = int(dur * SR); tt = np.arange(n) / SR
    att = np.minimum(tt / 1.1, 1)
    rel = np.minimum((dur - tt) / 1.2, 1)
    env = att * np.clip(rel, 0, 1)
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
    sec_len = bar * 2  # 每和弦 2 小节
    t = 0.0; ci = 0
    while t < dur:
        tones, rootm = CHORDS[ci % 4]
        pad_chord(buf, t, tones, min(sec_len + 0.6, dur - t), gain=0.05)
        # 琶音(八分音符)
        pat = [0, 1, 2, 3, 2, 3, 1, 2]
        for e8 in range(16):
            et = t + e8 * beat / 2
            if et >= dur: break
            if e8 % 8 in (3, 6) and (t / sec_len) > 1.2: continue
            m = tones[pat[e8 % 8]] + 12
            g = 0.16 * (1.0 if e8 % 4 == 0 else 0.72)
            pan = -0.4 if e8 % 2 == 0 else 0.4
            note(buf, et, m, 0.55, g, pan)
            # 主歌加低八度点缀
            if e8 % 8 == 0 and (t / sec_len) >= 0.5:
                note(buf, et, m - 12, 0.7, 0.09, pan * -0.5)
        # 贝斯 & 鼓(5.0s 后,29.2~33.8 breakdown)
        full = 5.0 <= t < 29.2 or t >= 33.8
        for b in range(8):
            bt = t + b * beat
            if bt >= dur: break
            if full and b % 2 == 0:
                kick(buf, bt, 0.42)
            if full and b % 2 == 1:
                hat(buf, bt, 0.09)
            if full and b in (0, 4):
                bass(buf, bt, rootm - 12, beat * 1.8, 0.20)
            elif t >= 29.2 and t < 33.8 and b in (0, 4):
                bass(buf, bt, rootm - 12, beat * 1.8, 0.12)
        t += sec_len; ci += 1
    return buf

def add_sfx(buf, t0, kind, gain=1.0):
    N = len(buf[0]); i0 = int(t0 * SR)
    if i0 >= N: return
    n_max = N - i0
    def put(sig):
        n = min(len(sig), n_max)
        buf[0][i0:i0 + n] += sig[:n]; buf[1][i0:i0 + n] += sig[:n]
    if kind == 'whoosh':
        n = int(0.55 * SR)
        rng = np.random.default_rng(7)
        x = rng.standard_normal(n)
        k = np.ones(90) / 90
        x = np.convolve(x, k, mode='same')
        env = np.sin(np.linspace(0, math.pi, n)) ** 1.5
        put(x * env * 0.5 * gain)
    elif kind == 'click':
        n = int(0.03 * SR); tt = np.arange(n) / SR
        sig = np.sin(2 * np.pi * 1900 * tt) * np.exp(-tt / 0.006) * 0.5
        rng = np.random.default_rng(3)
        sig += rng.standard_normal(n) * np.exp(-tt / 0.002) * 0.35
        put(sig * gain)
    elif kind == 'tick':
        n = int(0.09 * SR); tt = np.arange(n) / SR
        sig = np.sign(np.sin(2 * np.pi * 1150 * tt)) * np.exp(-tt / 0.035) * 0.16
        put(sig * gain)
    elif kind == 'thunk':
        n = int(0.16 * SR); tt = np.arange(n) / SR
        sig = np.sin(2 * np.pi * (95 * np.exp(-tt / 0.04) + 48) * tt) * np.exp(-tt / 0.06) * 0.6
        put(sig * gain)
    elif kind == 'shimmer':
        n = int(1.2 * SR); tt = np.arange(n) / SR
        sig = sum(np.sin(2 * np.pi * midi2f(m) * tt) * np.exp(-tt / (0.45 + i * 0.1))
                  for i, m in enumerate([81, 85, 88, 93])) * 0.05
        put(sig * gain)
    elif kind == 'pop':
        n = int(0.09 * SR); tt = np.arange(n) / SR
        f = 320 + 2600 * tt / 0.09
        sig = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.035) * 0.4
        put(sig * gain)
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
        sig = np.sin(2 * np.pi * 740 * tt) * np.exp(-tt / 0.028) * 0.13
        put(sig * gain)

VOICE_EV = [  # (time, file, gain)
    (23.65, 'Fox.mp3', 0.95),
    (27.70, 'Fish.mp3', 0.95),
    (34.20, 'Fox.mp3', 0.95),
    (35.40, 'Fish.mp3', 0.95),
]

def convert_voice():
    os.makedirs(BUILD, exist_ok=True)
    for f in ('Fish.mp3', 'Fox.mp3'):
        src = os.path.join(VOICE, f)
        dst = os.path.join(BUILD, f.replace('.mp3', '.wav'))
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', src,
                        '-ar', str(SR), '-ac', '2', '-sample_fmt', 's16', dst], check=True)

def load_voice(f):
    wf = wave.open(os.path.join(BUILD, f.replace('.mp3', '.wav')), 'rb')
    data = np.frombuffer(wf.readframes(wf.getnframes()), dtype=np.int16)
    wf.close()
    a = data.reshape(-1, 2).astype(np.float64) / 32768.0
    a /= max(1e-9, np.abs(a).max())
    return a

def build_audio():
    convert_voice()
    music = build_music(DUR)
    music = np.stack(music)
    # 人声闪避
    duck = np.ones(int(DUR * SR) + SR)
    for (t0, f, g) in VOICE_EV:
        d = load_voice(f); nd = len(d) / SR
        i0 = int((t0 - 0.05) * SR); i1 = int((t0 + nd + 0.25) * SR)
        ramp_dn = np.linspace(1, 0.38, int(0.07 * SR))
        ramp_up = np.linspace(0.38, 1, int(0.3 * SR))
        duck[i0:i0 + len(ramp_dn)] = np.minimum(duck[i0:i0 + len(ramp_dn)], ramp_dn)
        duck[i0 + len(ramp_dn):i1] = np.minimum(duck[i0 + len(ramp_dn):i1], 0.38)
        duck[i1:i1 + len(ramp_up)] = np.minimum(duck[i1:i1 + len(ramp_up)], ramp_up)
    music *= duck[:music.shape[1]][None, :]
    mix = music * 0.34
    sfx = [np.zeros(int(DUR * SR)), np.zeros(int(DUR * SR))]
    EV = [
        (5.42, 'whoosh', 1.0),
        (6.45, 'blip', 0.8), (7.2, 'blip', 0.8), (7.95, 'blip', 0.8),
        (12.5, 'click', 1.0), (12.56, 'tick', 1.0),
        (14.1, 'click', 1.0), (14.16, 'thunk', 1.0), (14.3, 'shimmer', 1.0),
        (17.7, 'pop', 0.9), (20.1, 'pop', 0.9),
        (23.6, 'pop', 1.0), (26.0, 'pop', 0.9),
        (27.6, 'click', 1.0), (27.66, 'chime', 1.0),
        (29.35, 'whoosh', 0.7),
        (29.6, 'pop', 0.8), (30.6, 'pop', 0.8), (31.6, 'pop', 0.8),
        (33.9, 'shimmer', 0.8),
    ]
    for (t0, kind, g) in EV:
        add_sfx(sfx, t0, kind, g)
    mix += np.stack(sfx) * 0.9
    for (t0, f, g) in VOICE_EV:
        d = load_voice(f)  # (n, 2)
        i0 = int(t0 * SR); i1 = min(i0 + len(d), int(DUR * SR))
        mix[:, i0:i1] += d[:i1 - i0].T * g
    # 结尾淡出
    fo = int(0.8 * SR)
    mix[:, -fo:] *= np.linspace(1, 0, fo)[None, :]
    peak = np.abs(mix).max()
    if peak > 0.98: mix *= 0.98 / peak
    pcm = (mix * 32767).astype('<i2')
    wf = wave.open(os.path.join(BUILD, 'audio.wav'), 'wb')
    wf.setnchannels(2); wf.setsampwidth(2); wf.setframerate(SR)
    wf.writeframes(pcm.tobytes())
    wf.close()
    print('audio ok, peak=%.3f' % peak)

# ---------------------------------------------------------------- 主流程
def low_priority():
    """渲染全程 IDLE 优先级,不抢前台(抢购脚本/游戏)的 CPU"""
    if sys.platform != 'win32':
        return
    import ctypes
    k = ctypes.windll.kernel32
    k.GetCurrentProcess.restype = ctypes.c_void_p  # 伪句柄必须按 64 位指针传
    h = k.GetCurrentProcess()
    if not k.SetPriorityClass(ctypes.c_void_p(h), 0x00000040):  # IDLE_PRIORITY_CLASS
        print('warn: SetPriorityClass failed, running at normal priority', flush=True)
    else:
        print('priority: idle (0x40)', flush=True)

def probe_encoder(args):
    """0.2s 试编码一次,验证硬件编码器真实可用(列表里有不代表能用)"""
    try:
        r = subprocess.run(
            ['ffmpeg', '-v', 'error', '-f', 'lavfi', '-i', 'color=black:s=256x256:d=0.2']
            + args + ['-f', 'null', '-'],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        return r.returncode == 0
    except Exception:
        return False

def pick_video_codec():
    if probe_encoder(['-c:v', 'h264_nvenc']):
        # NVENC: quality 对齐 x264 crf18 档
        return ['-c:v', 'h264_nvenc', '-preset', 'p5', '-tune', 'hq',
                '-rc', 'vbr', '-cq', '19', '-b:v', '0',
                '-spatial-aq', '1', '-profile:v', 'high']
    print('nvenc unavailable, fallback libx264 (limited threads)', flush=True)
    n = max(2, (os.cpu_count() or 8) // 2)
    return ['-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-threads', str(n)]

def render_video(out):
    os.makedirs(os.path.dirname(out), exist_ok=True)
    cmd = ['ffmpeg', '-v', 'error', '-y',
           '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H),
           '-r', str(FPS), '-i', '-',
           '-pix_fmt', 'yuv420p', '-movflags', '+faststart'] \
          + pick_video_codec() + [out]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(N_FRAMES):
        p.stdin.write(render_frame(i).tobytes())
        if i % 150 == 0:
            print('frame %d / %d' % (i, N_FRAMES), flush=True)
    p.stdin.close()
    p.wait()
    if p.returncode != 0:
        raise SystemExit('ffmpeg encode failed')

def mux():
    os.makedirs(os.path.dirname(FINAL), exist_ok=True)
    subprocess.run(['ffmpeg', '-v', 'error', '-y',
                    '-i', os.path.join(BUILD, 'video.mp4'),
                    '-i', os.path.join(BUILD, 'audio.wav'),
                    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k',
                    '-movflags', '+faststart', FINAL], check=True)
    print('final ->', FINAL)

def main():
    os.makedirs(BUILD, exist_ok=True)
    low_priority()
    build_bg(); build_hud_sprites(); build_cursor(); load_gifs()
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if what == 'frame':
        t = float(sys.argv[2]); out = sys.argv[3]
        render_frame(int(t * FPS)).save(out)
        print('frame saved', out)
        return
    if what in ('audio', 'all'):
        build_audio()
    if what in ('video', 'all'):
        render_video(os.path.join(BUILD, 'video.mp4'))
    if what == 'all':
        mux()

if __name__ == '__main__':
    main()
