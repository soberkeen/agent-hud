# -*- coding: utf-8 -*-
# 用户成品循环 MP4 -> GIF 转换器 v2（自适应抠底版）
#   背景色从画面四边自动采样（任何纯色/浅渐变背景都适用，不限白色）
#   抠底 = 颜色距离 < 容差 且 与边框连通（角色内部同色区域不受影响）
#   满帧率抽取 + 抠底 + 缩 256px + 透明 GIF（逐帧延时取自源帧率）
#   循环完全由用户剪辑决定：不做窗口搜索/倒放/溶合
import os
import imageio
import numpy as np
from PIL import Image
from scipy import ndimage

D = r"C:\Users\sober\Videos"
OUT = r"I:\ZCOD 工作区\HDR 开关"
OUT_PX = 256
TOL = 42                                  # 与背景色的 RGB 距离容差
JOBS = [
    ("大肥鱼-标准 转gif.mp4", "anim-df-normal.gif"),
    ("大肥鱼-紧张 转gif.mp4", "anim-df-worry.gif"),
    ("大肥鱼-呆滞 转gif.mp4", "anim-df-daze.gif"),
    ("GLM-标准 转gif.mp4", "anim-zc-normal.gif"),
    ("GLM-紧张转gif 新.mp4", "anim-zc-worry.gif"),
    ("GLM-痴呆 转gif.mp4", "anim-zc-daze.gif"),
]

def key_adaptive(img, bg_color, tol=TOL):
    """颜色距离<tol 的像素：与边框连通的置透明；大块背景色孤岛（>3% 画面，如卡片式底板）也置透明"""
    d = np.sqrt(((img[:, :, :3].astype(np.float32) - np.array(bg_color, np.float32)) ** 2).sum(axis=2))
    mask = d < tol
    lbl, num = ndimage.label(mask)
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    kill = np.isin(lbl, list(border))
    # 背景色大孤岛：用户剪辑可能带卡片式底板（不与边框相连）
    if num:
        sizes = ndimage.sum(mask, lbl, range(1, num + 1))
        big = [i + 1 for i, sz in enumerate(sizes) if sz > 0.03 * mask.size and (i + 1) not in border]
        if big:
            kill |= np.isin(lbl, big)
    out = img.copy()
    out[kill] = 0
    alpha = np.where(kill, 0, 255).astype(np.uint8)
    return np.dstack([out[:, :, :3], alpha])

def bg_estimate(frame):
    """背景色 = 单帧四边像素的中位数"""
    h, w = frame.shape[:2]
    samples = np.concatenate([frame[0, :, :3], frame[h - 1, :, :3], frame[:, 0, :3], frame[:, w - 1, :3]])
    return np.median(samples, axis=0)

def frame_to_gif_tile(rgba):
    im = Image.fromarray(rgba, 'RGBA').resize((OUT_PX, OUT_PX), Image.LANCZOS)
    alpha = im.getchannel('A')
    p = im.convert('RGB').convert('P', palette=Image.ADAPTIVE, colors=255)
    mask = alpha.point(lambda a: 255 if a <= 100 else 0)
    p.paste(255, mask)
    p.info['transparency'] = 255
    return p

for mp4, gif_name in JOBS:
    src = os.path.join(D, mp4)
    r = imageio.get_reader(src)
    fps = float(r.get_meta_data().get("fps") or 24)
    raw = [np.asarray(f) for f in r]
    r.close()
    keyed = [key_adaptive(f, bg_estimate(f)) for f in raw]
    delay = max(20, round(1000 / fps))
    tiles = [frame_to_gif_tile(f) for f in keyed]
    tiles[0].save(os.path.join(OUT, gif_name), save_all=True, append_images=tiles[1:],
                  duration=delay, loop=0, transparency=255, disposal=2, optimize=False)
    print("%s  %d 帧 %dms/帧" % (gif_name, len(tiles), delay))
print("done")
