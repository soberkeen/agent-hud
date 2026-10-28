# -*- coding: utf-8 -*-
# 精灵图流水线 v5：v4 基础上加时域降噪——压掉视频压缩噪声在低运动片段里的闪烁
import os, sys
import imageio
import numpy as np
from PIL import Image
from collections import deque

D = r"C:\Users\sober\Downloads"
OUT = os.path.dirname(os.path.abspath(__file__)).replace("\\tools", "")
SIZE = 96
MAX_FRAMES = 60
JOBS = [
    ("大肥鱼-标准.mp4", "anim-df-normal.png", "calm"),
    ("大肥鱼-紧张.mp4", "anim-df-worry.png", "action"),
    ("大肥鱼-呆滞.mp4", "anim-df-daze.png", "action"),
    ("GLM-标准.mp4", "anim-zc-normal.png", "calm"),
    ("GLM-新慌张.mp4", "anim-zc-worry.png", "action"),
    ("GLM-呆滞.mp4", "anim-zc-daze.png", "action"),
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

def temporal_smooth(frames):
    """相邻三帧加权平均：压压缩噪声闪烁；59fps 源帧距极短，动作几乎无损"""
    out = []
    n = len(frames)
    for i in range(n):
        a = frames[max(0, i - 1)].astype(np.float32)
        b = frames[i].astype(np.float32)
        c = frames[min(n - 1, i + 1)].astype(np.float32)
        out.append(np.clip(0.25 * a + 0.5 * b + 0.25 * c, 0, 255).astype(np.uint8))
    return out

def bbox_of(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    if len(xs) == 0: return None
    return xs.min(), ys.min(), xs.max(), ys.max()

def find_loop(mp4, mode):
    r = imageio.get_reader(os.path.join(D, mp4))
    meta = r.get_meta_data()
    dur = float(meta.get("duration") or 4.0)
    grays = []
    for f in r:
        g = Image.fromarray(f).convert("L").resize((160, 160), Image.BILINEAR)
        grays.append(np.asarray(g, dtype=np.float32))
    r.close()
    n = len(grays)
    fps_real = max(1.0, n / dur)
    G = np.stack(grays)
    klo = max(8, int(fps_real * 0.6))
    khi = min(n - 2, int(fps_real * 1.0), MAX_FRAMES)
    if khi <= klo: klo = khi - 1
    motion = np.abs(G[1:n] - G[0:n - 1]).mean(axis=(1, 2))
    best = None
    for K in range(klo, khi + 1):
        d = np.abs(G[0:n - K] - G[K:n]).mean(axis=(1, 2))
        mov = np.convolve(motion, np.ones(K) / K, mode="valid")
        if mode == "calm":
            s = int(d.argmin())
            cand = (float(d[s]), float(mov[s]), s, K)
            if best is None or cand[0] < best[0]:
                best = cand + ("calm",)
        else:
            ok = d < 25
            if ok.any():
                idx = np.where(ok)[0]
                s = int(idx[mov[idx].argmax()])
                cand = (float(d[s]), float(mov[s]), s, K)
                if best is None or cand[1] > best[1]:
                    best = cand + ("action",)
    if best is None:
        s, K = int((n - 1) * 0.4), klo
        best = (0.0, 0.0, s, K, "fallback")
    seam, mov, s, K, used = best
    idxs = list(range(s, s + K + 1))
    seamf = float(np.abs(G[idxs[0]] - G[idxs[-1]]).mean())
    print("  loop[%s]: start=%d K=%d(%.2fs) fps=%.1f 接缝=%.2f 运动=%.2f (n=%d)" %
          (used, s, K, K / fps_real, fps_real, seamf, mov, n))
    return idxs, fps_real

def process(mp4, out_name, mode):
    print(out_name)
    idxs, fps_real = find_loop(mp4, mode)
    want = set(idxs)
    frames = []
    r = imageio.get_reader(os.path.join(D, mp4))
    for i, f in enumerate(r):
        if i in want:
            frames.append(key_white(np.asarray(f)))
            if len(frames) == len(idxs): break
    r.close()
    assert len(frames) == len(idxs), "抽帧数量不足"
    frames = temporal_smooth(frames)
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
    print("  -> %s (%dx%d, %d 帧, 17ms/帧)" % (out_name, cw, ch, len(tiles)))

for mp4, out, mode in JOBS:
    process(mp4, out, mode)
print("done")
