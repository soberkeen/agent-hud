# -*- coding: utf-8 -*-
# 精灵图流水线 v6：以「接缝/动作 比」为核心的循环窗口搜索
#   直接在 96px 成品尺度上评估每个候选窗口的接缝差与动作差，
#   calm 选 比值最小 且 有动作；action 在接缝可接受里选动作最大。
#   窗口上限放宽到 88 帧（~1.5 秒@59fps）以覆盖真实周期。
import os, sys
import imageio
import numpy as np
from PIL import Image
from collections import deque

D = r"C:\Users\sober\Downloads"
OUT = os.path.dirname(os.path.abspath(__file__)).replace("\\tools", "")
SIZE = 96
MAX_FRAMES = 88
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
    out = []
    n = len(frames)
    for i in range(n):
        a = frames[max(0, i - 1)].astype(np.float32)
        b = frames[i].astype(np.float32)
        c = frames[min(n - 1, i + 1)].astype(np.float32)
        out.append(np.clip(0.25 * a + 0.5 * b + 0.25 * c, 0, 255).astype(np.uint8))
    return out

def to96(rgb):
    return np.asarray(Image.fromarray(rgb).resize((SIZE, SIZE), Image.BILINEAR), dtype=np.float32)

def bbox_of(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    if len(xs) == 0: return None
    return xs.min(), ys.min(), xs.max(), ys.max()

def find_loop(F, mode, fps_real):
    """F: (n,96,96,3) float32 96px 帧。返回 (start,K)"""
    n = len(F)
    klo = max(8, int(fps_real * 0.5))
    khi = min(n - 2, max(klo + 1, int(fps_real * 1.5)), MAX_FRAMES)
    if khi <= klo: klo = khi - 1
    diff = np.abs(F[1:n] - F[0:n - 1]).mean(axis=(1, 2, 3))     # 每帧运动量(96px)
    best = None
    for K in range(klo, khi + 1):
        seam = np.abs(F[0:n - K] - F[K:n]).mean(axis=(1, 2, 3))  # 每个起点的接缝差
        mov = np.convolve(diff, np.ones(K) / K, mode="valid")
        if mode == "calm":
            # 比值最小 = 接缝相对动作最不显眼；动作太小的不要
            ratio = seam / np.maximum(mov, 1.5)
            ratio[mov < 2.0] = 1e9
            s = int(ratio.argmin())
            cand = (float(ratio[s]), s, K)
            if best is None or cand[0] < best[0]:
                best = cand + (float(seam[s]), float(mov[s]))
        else:
            ok = seam < 25
            if ok.any():
                idx = np.where(ok)[0]
                s = int(idx[mov[idx].argmax()])
                cand = (float(mov[s]), s, K)
                if best is None or cand[0] > best[0]:
                    best = cand + (float(seam[s]), float(mov[s]))
    if best is None:
        s, K = int((n - 1) * 0.4), klo
        best = (0.0, s, K, 0.0, 0.0)
    if mode == "calm":
        ratio, s, K, seam, mov = best
    else:
        mov, s, K, seam = best[0], best[1], best[2], best[3]
    return s, K, float(seam), float(mov)

def process(mp4, out_name, mode):
    print(out_name)
    # 读全帧，96px 评分用 + 全帧留源
    r = imageio.get_reader(os.path.join(D, mp4))
    meta = r.get_meta_data()
    dur = float(meta.get("duration") or 4.0)
    small, full = [], []
    for f in r:
        small.append(to96(f))
        full.append(np.asarray(f))
    r.close()
    n = len(small)
    fps_real = max(1.0, n / dur)
    F = np.stack(small)
    s, K, seam, mov = find_loop(F, mode, fps_real)
    print("  loop: start=%d K=%d(%.2fs) fps=%.1f 接缝=%.1f 动作=%.1f 比=%.2f" %
          (s, K, K / fps_real, fps_real, seam, mov, seam / max(mov, 0.01)))
    idxs = list(range(s, s + K + 1))
    frames = []
    for i in idxs:
        frames.append(key_white(full[i]))
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

    # 接缝残余过大（>动作）时，尾帧向首帧交叉溶合，把跳变化成 60ms 快速过渡
    f0 = tiles[0]
    if len(tiles) >= 3 and seam > mov:
        for k, w in ((-2, 0.35), (-1, 0.7)):
            a = np.asarray(tiles[k], dtype=np.float32)
            b = np.asarray(f0, dtype=np.float32)
            tiles[k] = Image.fromarray(np.clip(a * (1 - w) + b * w, 0, 255).astype(np.uint8))
        print("  (接缝>动作，已烘焙尾部 2 帧交叉溶合)")

    sheet = Image.new("RGBA", (SIZE * len(tiles), SIZE), (0, 0, 0, 0))
    for i, t in enumerate(tiles): sheet.paste(t, (i * SIZE, 0))
    sheet.save(os.path.join(OUT, out_name))
    print("  -> %s (%dx%d, %d 帧)" % (out_name, cw, ch, len(tiles)))

    # 成品实测：首尾差 vs 中段动作差
    fa = np.asarray(tiles[0], dtype=np.float64)
    fl = np.asarray(tiles[-1], dtype=np.float64)
    fm = np.asarray(tiles[len(tiles) // 2], dtype=np.float64)
    mask = np.minimum(fa[:, :, 3], fl[:, :, 3]) > 128
    print("  实测: 首尾差=%.1f 中段动作=%.1f" % (np.abs(fa - fl)[mask].mean(), np.abs(fa - fm)[mask].mean()))

for mp4, out, mode in JOBS:
    process(mp4, out, mode)
print("done")
