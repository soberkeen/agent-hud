# -*- coding: utf-8 -*-
# 用户成品循环 MP4 -> GIF（直接可用）：满帧率抽取+抠白底+缩 256px+透明背景
# 不做任何循环点搜索/交叉溶合——循环完全由用户的剪辑决定
import os
import imageio
import numpy as np
from PIL import Image
from scipy import ndimage

D = r"C:\Users\sober\Videos"
OUT = r"I:\ZCOD 工作区\HDR 开关"          # 仓库 assets 源
JOBS = [
    ("大肥鱼-标准 转gif.mp4", "anim-df-normal.gif"),
    ("大肥鱼-紧张 转gif.mp4", "anim-df-worry.gif"),
    ("大肥鱼-呆滞 转gif.mp4", "anim-df-daze.gif"),
    ("GLM-标准 转gif.mp4", "anim-zc-normal.gif"),
    ("GLM-紧张转gif 新.mp4", "anim-zc-worry.gif"),
    ("GLM-痴呆 转gif.mp4", "anim-zc-daze.gif"),
]
OUT_PX = 256

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

def frame_to_gif_tile(rgba):
    im = Image.fromarray(rgba, 'RGBA').resize((OUT_PX, OUT_PX), Image.LANCZOS)
    alpha = im.getchannel('A')
    p = im.convert('RGB').convert('P', palette=Image.ADAPTIVE, colors=255)
    mask = alpha.point(lambda a: 255 if a <= 100 else 0)
    p.paste(255, mask)                       # 索引 255 = 透明
    p.info['transparency'] = 255
    return p

for mp4, gif_name in JOBS:
    src = os.path.join(D, mp4)
    r = imageio.get_reader(src)
    fps = float(r.get_meta_data().get("fps") or 24)
    frames = []
    for f in r:
        frames.append(key_white(np.asarray(f)))
    r.close()
    delay = max(20, round(1000 / fps))
    tiles = [frame_to_gif_tile(f) for f in frames]
    tiles[0].save(os.path.join(OUT, gif_name), save_all=True, append_images=tiles[1:],
                  duration=delay, loop=0, transparency=255, disposal=2, optimize=False)
    print("%s <- %s (%d 帧, %dms/帧, %.1fs)" % (gif_name, mp4, len(tiles), delay, len(tiles) * delay / 1000))
print("done")
