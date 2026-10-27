# -*- coding: utf-8 -*-
# 制作侧边栏图标：
#   icon-dafeiyu.png — 大肥鱼立绘表左起第一个正面像，白底转透明
#   icon-zcode.png   — zcode 狼女图，去烤死的棋盘格背景与鼠标指针
from PIL import Image
import os

OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def flood_bg_to_alpha(im, seed_ok, border=True):
    """从四边(或给定点)泛洪：seed_ok(px)->bool 判定属于背景则置透明"""
    w, h = im.size
    px = im.load()
    from collections import deque
    q = deque()
    seen = [[False]*h for _ in range(w)]
    def push(x, y):
        if 0 <= x < w and 0 <= y < h and not seen[x][y]:
            seen[x][y] = True
            if seed_ok(px[x, y]):
                px[x, y] = (0, 0, 0, 0)
                q.append((x, y))
    if border:
        for x in range(w):
            push(x, 0); push(x, h-1)
        for y in range(h):
            push(0, y); push(w-1, y)
    while q:
        x, y = q.popleft()
        push(x+1, y); push(x-1, y); push(x, y+1); push(x, y-1)
    return im

def opaque_component_clear(im, sx, sy):
    """把 (sx,sy) 所在的不透明连通块整体置透明（用于去掉鼠标指针）"""
    w, h = im.size
    px = im.load()
    if px[sx, sy][3] == 0: return
    from collections import deque
    q = deque([(sx, sy)])
    seen = [[False]*h for _ in range(w)]
    seen[sx][sy] = True
    while q:
        x, y = q.popleft()
        px[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[nx][ny]:
                seen[nx][ny] = True
                if px[nx, ny][3] != 0:
                    q.append((nx, ny))

def crop_bbox(im):
    return im.crop(im.getbbox())

def square_pad(im, size):
    w, h = im.size
    s = max(w, h)
    canvas = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    canvas.paste(im, ((s-w)//2, (s-h)//2))
    return canvas.resize((size, size), Image.LANCZOS)

# ---------- 大肥鱼 ----------
dfy = Image.open(r"C:\Users\sober\Downloads\dafeiyu-chibi-character-sheet.png").convert("RGBA")
fig = dfy.crop((0, 0, 512, 640))   # 左起第一个正面全身
def dfy_bg(p):
    r, g, b, a = p
    return r > 235 and g > 235 and b > 235   # 白底
flood_bg_to_alpha(fig, dfy_bg)
fig = crop_bbox(fig)
square_pad(fig, 160).save(os.path.join(OUT, "icon-dafeiyu.png"))

# ---------- zcode 狼女 ----------
zc = Image.open(r"C:\Users\sober\Downloads\0fb4c5d1-eb12-4976-97c1-252aa8bab1f8.png").convert("RGBA")
def zc_bg(p):
    r, g, b, a = p
    if a == 0: return True
    mx, mn = max(r, g, b), min(r, g, b)
    return mx - mn < 26 and mx > 165        # 棋盘格: 白255/灰199 无饱和
flood_bg_to_alpha(zc, zc_bg)
# 鼠标指针：左上角箭头，其不透明连通块整体清除（指针尖端附近取种子）
opaque_component_clear(zc, 14, 30)
zc = crop_bbox(zc)
square_pad(zc, 160).save(os.path.join(OUT, "icon-zcode.png"))
print("done", fig.size, zc.size)
