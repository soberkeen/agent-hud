# -*- coding: utf-8 -*-
# 精灵图流水线 v2：自动寻找无缝循环窗口（正向播放，不用倒放）
#   1) 全帧降采样算帧差，搜索首尾差异最小的 (起点, K=12帧) 窗口
#   2) 只在窗口内抽 12 帧 -> 洪泛抠白底 -> 联合包围盒 -> 96px 横向拼接
import os, sys
import imageio
import numpy as np
from PIL import Image
from collections import deque

D = r"C:\Users\sober\Downloads"
OUT = os.path.dirname(os.path.abspath(__file__)).replace("\\tools", "")
SIZE, FRAMES = 96, 12
JOBS = [
    ("大肥鱼-标准.mp4", "anim-df-normal.png"),
    ("大肥鱼-紧张.mp4", "anim-df-worry.png"),
    ("大肥鱼-呆滞.mp4", "anim-df-daze.png"),
    ("GLM-标准.mp4", "anim-zc-normal.png"),
    ("GLM-新慌张.mp4", "anim-zc-worry.png"),
    ("GLM-呆滞.mp4", "anim-zc-daze.png"),
]

def key_white(img):
    h, w = img.shape[:2]
    alpha = np.full((h, w), 255, dtype=np.uint8)
    mn = img.min(axis=2); mx = img.max(axis=2)
    bg = (mn > 224) & ((mx - mn) < 30)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if bg[y, x] and alpha[y, x]: alpha[y, x] = 0; q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if bg[y, x] and alpha[y, x]: alpha[y, x] = 0; q.append((y, x))
    while q:
        y, x = q.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and alpha[ny, nx] and bg[ny, nx]:
                alpha[ny, nx] = 0; q.append((ny, nx))
    return np.dstack([img, alpha])

def bbox_of(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    if len(xs) == 0: return None
    return xs.min(), ys.min(), xs.max(), ys.max()

def find_loop(mp4):
    """全帧降采样灰度化，找首尾差异最小的 K 帧窗口"""
    r = imageio.get_reader(os.path.join(D, mp4))
    grays = []
    for f in r:
        g = Image.fromarray(f).convert("L").resize((160, 160), Image.BILINEAR)
        grays.append(np.asarray(g, dtype=np.float32))
    r.close()
    n = len(grays)
    G = np.stack(grays)
    best = (1e9, 0, 12)
    for K in range(12, 31):                      # 0.5~1.25 秒 @24fps
        d = np.abs(G[0:n - K] - G[K:n]).mean(axis=(1, 2))
        s = int(d.argmin())
        if d[s] < best[0]:
            best = (float(d[s]), s, K)
    _, s, K = best
    idxs = [round(s + i * (K - 1) / (FRAMES - 1)) for i in range(FRAMES)]
    seam = float(np.abs(G[idxs[0]] - G[idxs[-1]]).mean())
    print("  loop: start=%d K=%d 帧间差=%.2f 接缝差=%.2f (n=%d)" % (s, K, best[0], seam, n))
    return idxs, seam

def process(mp4, out_name):
    print(out_name)
    idxs, seam = find_loop(mp4)
    want = set(idxs)
    frames = []
    r = imageio.get_reader(os.path.join(D, mp4))
    for i, f in enumerate(r):
        if i in want:
            frames.append(key_white(np.asarray(f)))
            if len(frames) == len(idxs): break
    r.close()
    assert len(frames) == len(idxs), "抽帧数量不足"
    u = [10**9, 10**9, -1, -1]
    for f in frames:
        b = bbox_of(f)
        if b is None: continue
        u[0] = min(u[0], b[0]); u[1] = min(u[1], b[1])
        u[2] = max(u[2], b[2]); u[3] = max(u[3], b[3])
    pad = 4
    u = [max(0, u[0] - pad), max(0, u[1] - pad), min(f.shape[1] - 1, u[2] + pad), min(f.shape[0] - 1, u[3] + pad)]
    cw, ch = u[2] - u[0] + 1, u[3] - u[1] + 1
    side = max(cw, ch)
    tiles = []
    for f in frames:
        crop = f[u[1]:u[3] + 1, u[0]:u[2] + 1]
        canvas = np.zeros((side, side, 4), dtype=np.uint8)
        oy, ox = (side - ch) // 2, (side - cw) // 2
        canvas[oy:oy + ch, ox:ox + cw] = crop
        tiles.append(Image.fromarray(canvas).resize((SIZE, SIZE), Image.LANCZOS))
    sheet = Image.new("RGBA", (SIZE * len(tiles), SIZE), (0, 0, 0, 0))
    for i, t in enumerate(tiles): sheet.paste(t, (i * SIZE, 0))
    sheet.save(os.path.join(OUT, out_name))
    print("  -> %s (%dx%d)" % (out_name, cw, ch))
    return seam

seams = {}
for mp4, out in JOBS:
    seams[out] = process(mp4, out)
print("接缝差汇总（越小越无缝）:")
for k, v in seams.items():
    print("  %-24s %.2f" % (k, v))
