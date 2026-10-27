# -*- coding: utf-8 -*-
# 从 6 条 MP4 生成动画精灵图：抽 12 帧 → 洪泛抠白底 → 联合包围盒对齐 → 96px → 横向拼接
# 输出: anim-df-{normal,worry,daze}.png / anim-zc-{normal,worry,daze}.png（每张 12 帧）
import os
import imageio
import numpy as np
from PIL import Image
from collections import deque

D = r"C:\Users\sober\Downloads"
OUT = os.path.dirname(os.path.abspath(__file__)).replace("\\tools", "")
JOBS = [
    ("大肥鱼-标准.mp4", "anim-df-normal.png"),
    ("大肥鱼-紧张.mp4", "anim-df-worry.png"),
    ("大肥鱼-呆滞.mp4", "anim-df-daze.png"),
    ("GLM-标准.mp4", "anim-zc-normal.png"),
    ("GLM-紧张.mp4", "anim-zc-worry.png"),
    ("GLM-呆滞.mp4", "anim-zc-daze.png"),
]
SIZE = 96
FRAMES = 12

def key_white(img):
    """洪泛抠白底：从四边出发，把连通的近白像素置透明（角色深色描边会挡住填充）"""
    h, w = img.shape[:2]
    alpha = np.full((h, w), 255, dtype=np.uint8)
    mn = img.min(axis=2)
    mx = img.max(axis=2)
    bg = (mn > 224) & ((mx - mn) < 30)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if bg[y, x] and alpha[y, x]:
                alpha[y, x] = 0; q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if bg[y, x] and alpha[y, x]:
                alpha[y, x] = 0; q.append((y, x))
    while q:
        y, x = q.popleft()
        for ny, nx in ((y+1,x),(y-1,x),(y,x+1),(y,x-1)):
            if 0 <= ny < h and 0 <= nx < w and alpha[ny, nx] and bg[ny, nx]:
                alpha[ny, nx] = 0; q.append((ny, nx))
    out = np.dstack([img, alpha])
    return out

def bbox_of(rgba):
    a = rgba[:, :, 3]
    ys, xs = np.where(a > 0)
    if len(xs) == 0:
        return None
    return xs.min(), ys.min(), xs.max(), ys.max()

def process(mp4, out_name):
    r = imageio.get_reader(os.path.join(D, mp4))
    n = r.count_frames()
    idxs = [round(i * (n - 1) / (FRAMES - 1)) for i in range(FRAMES)]
    frames = []
    for i in idxs:
        f = r.get_data(i)
        rgba = key_white(f)
        frames.append(rgba)
    r.close()
    # 联合包围盒：所有帧取并集，保证裁剪框稳定不抖
    u = [10**9, 10**9, -1, -1]
    for f in frames:
        b = bbox_of(f)
        if b is None: continue
        u[0] = min(u[0], b[0]); u[1] = min(u[1], b[1])
        u[2] = max(u[2], b[2]); u[3] = max(u[3], b[3])
    pad = 4
    u = [max(0, u[0]-pad), max(0, u[1]-pad), min(f.shape[1]-1, u[2]+pad), min(f.shape[0]-1, u[3]+pad)]
    cw, ch = u[2]-u[0]+1, u[3]-u[1]+1
    side = max(cw, ch)
    tiles = []
    for f in frames:
        crop = f[u[1]:u[3]+1, u[0]:u[2]+1]
        canvas = np.zeros((side, side, 4), dtype=np.uint8)
        oy, ox = (side-ch)//2, (side-cw)//2
        canvas[oy:oy+ch, ox:ox+cw] = crop
        pil = Image.fromarray(canvas).resize((SIZE, SIZE), Image.LANCZOS)
        tiles.append(pil)
    sheet = Image.new("RGBA", (SIZE*len(tiles), SIZE), (0, 0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, (i*SIZE, 0))
    sheet.save(os.path.join(OUT, out_name))
    print(out_name, "frames=%d union=%dx%d sheet=%s" % (len(tiles), cw, ch, sheet.size))

for mp4, out in JOBS:
    process(mp4, out)
print("all done")
