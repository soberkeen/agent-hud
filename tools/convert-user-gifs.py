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
    """颜色距离<tol 且与边框连通的像素置透明（scipy C 级连通域）"""
    d = np.sqrt(((img[:, :, :3].astype(np.float32) - np.array(bg_color, np.float32)) ** 2).sum(axis=2))
    mask = d < tol
    lbl, _ = ndimage.label(mask)
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    kill = np.isin(lbl, list(border))
    out = img.copy()
    out[kill] = 0
    alpha = np.where(kill, 0, 255).astype(np.uint8)
    return np.dstack([out[:, :, :3], alpha])

def bg_estimate(frames):
    """背景色 = 多帧四边像素的中位数（抗角色入镜/噪声）"""
    samples = []
    for f in frames[:: max(1, len(frames) // 7)]:
        h, w = f.shape[:2]
        samples.append(f[0, :, :3]); samples.append(f[h - 1, :, :3])
        samples.append(f[:, 0, :3]); samples.append(f[:, w - 1, :3])
    S = np.concatenate([s.reshape(-1, 3) for s in samples])
    return np.median(S, axis=0)

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
    bg = bg_estimate(raw)
    keyed = [key_adaptive(f, bg) for f in raw]
    delay = max(20, round(1000 / fps))
    tiles = [frame_to_gif_tile(f) for f in keyed]
    tiles[0].save(os.path.join(OUT, gif_name), save_all=True, append_images=tiles[1:],
                  duration=delay, loop=0, transparency=255, disposal=2, optimize=False)
    print("%s  bg=%s  %d 帧 %dms/帧" % (gif_name, bg.astype(int), len(tiles), delay))
print("done")
