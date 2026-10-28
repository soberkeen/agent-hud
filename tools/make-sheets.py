# -*- coding: utf-8 -*-
# 精灵图流水线 v7（最终版）：
#   循环窗口搜索(同v6) -> scipy 洪泛抠底(毫秒级) -> 时域降噪 ->
#   窗口重采样为固定 90 帧(带小数线性插值, 步长均匀) -> 交叉溶合兜底 -> 96px 横向拼接
#   播放固定 30fps（60Hz 屏幕恰好每 2 刷新一帧，节拍数学均匀）—— 3 秒循环
import os, sys
import imageio
import numpy as np
from PIL import Image
from scipy import ndimage

D = r"C:\Users\sober\Downloads"
OUT = os.path.dirname(os.path.abspath(__file__)).replace("\\tools", "")
SIZE, N_OUT = 96, 90
MAX_FRAMES = 160
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
    mn = img.min(axis=2); mx = img.max(axis=2)
    bg = (mn > 224) & ((mx - mn) < 30)
    lbl, _ = ndimage.label(bg)
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    mask = np.isin(lbl, list(border))
    out = img.copy()
    out[mask] = 0
    alpha = np.where(mask, 0, 255).astype(np.uint8)
    return np.dstack([out, alpha])

def temporal_smooth(frames):
    out = []
    n = len(frames)
    for i in range(n):
        a = frames[max(0, i - 1)].astype(np.float32)
        b = frames[i].astype(np.float32)
        c = frames[min(n - 1, i + 1)].astype(np.float32)
        out.append(np.clip(0.25 * a + 0.5 * b + 0.25 * c, 0, 255).astype(np.uint8))
    return out

def resample(frames, n_out):
    """带小数位置的线性插值重采样：源帧距不均(VFR)也被抹平"""
    n = len(frames)
    arr = [f.astype(np.float32) for f in frames]
    out = []
    for i in range(n_out):
        pos = i * (n - 1) / (n_out - 1) if n_out > 1 else 0
        j = int(pos); w = pos - j
        k = min(j + 1, n - 1)
        out.append(np.clip(arr[j] * (1 - w) + arr[k] * w, 0, 255).astype(np.uint8))
    return out

def bbox_of(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    if len(xs) == 0: return None
    return xs.min(), ys.min(), xs.max(), ys.max()

def to96(rgb):
    return np.asarray(Image.fromarray(rgb).resize((SIZE, SIZE), Image.BILINEAR), dtype=np.float32)

def find_loop(F, mode, fps_real):
    n = len(F)
    klo = max(8, int(fps_real * 1.0))
    khi = min(n - 2, max(klo + 1, int(fps_real * 2.5)), MAX_FRAMES)
    if khi <= klo: klo = khi - 1
    diff = np.abs(F[1:n] - F[0:n - 1]).mean(axis=(1, 2, 3))
    best = None
    for K in range(klo, khi + 1):
        seam = np.abs(F[0:n - K] - F[K:n]).mean(axis=(1, 2, 3))
        mov = np.convolve(diff, np.ones(K) / K, mode="valid")
        if mode == "calm":
            ratio = seam / np.maximum(mov, 1.5)
            ratio[mov < 2.0] = 1e9
            s = int(ratio.argmin())
            cand = (float(ratio[s]), s, K)
            if best is None or cand[0] < best[0]:
                best = cand + (float(seam[s]), float(mov[s]))
        else:
            ok = seam < 35                      # 烘焙溶合能吃掉中等接缝，阈值放宽
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
    print("  loop: start=%d K=%d(%.2fs) fps=%.1f 接缝=%.1f 动作=%.1f" % (s, K, K / fps_real, fps_real, seam, mov))
    idxs = list(range(s, s + K + 1))
    frames = []
    for i in idxs:
        frames.append(key_white(full[i]))
    frames = temporal_smooth(frames)
    frames = resample(frames, N_OUT)
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

    # 接缝残余大于动作时，尾部两帧向首帧交叉溶合（约 66ms 快速过渡）
    f0 = tiles[0]
    seam_out = float(np.abs(np.asarray(tiles[0], np.float32) - np.asarray(tiles[-1], np.float32)).mean())
    mov_out = float(np.abs(np.asarray(tiles[0], np.float32) - np.asarray(tiles[N_OUT // 2], np.float32)).mean())
    if seam_out > mov_out:
        for k, w in ((-2, 0.35), (-1, 0.7)):
            a = np.asarray(tiles[k], dtype=np.float32)
            tiles[k] = Image.fromarray(np.clip(a * (1 - w) + np.asarray(f0, np.float32) * w, 0, 255).astype(np.uint8))
        print("  (接缝烘焙溶合)")

    sheet = Image.new("RGBA", (SIZE * N_OUT, SIZE), (0, 0, 0, 0))
    for i, t in enumerate(tiles): sheet.paste(t, (i * SIZE, 0))
    sheet.save(os.path.join(OUT, out_name))
    print("  -> %s (%dx%d, %d 帧, 33ms/帧) 成品首尾差=%.1f 动作=%.1f" % (out_name, cw, ch, N_OUT, seam_out, mov_out))

for mp4, out, mode in JOBS:
    process(mp4, out, mode)
print("done")
