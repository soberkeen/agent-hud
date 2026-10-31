# -*- coding: utf-8 -*-
# MP4 -> 透明底 GIF（v2.0 FramePlayer 直接读 assets\anim-XX-<状态>.gif）
#   内容原封不动：不改分辨率、不丢帧、不裁剪、不找循环点。
#   只做两件事：
#     1) 洪泛抠底：从画面边缘 flood-fill 与背景色相近的连通区 -> 透明
#        （角色内部的白色/绿色不受影响），边缘 1px 过渡带向背景色混入防硬边。
#     2) 全片统一 256 色调色板（采样帧生成 + 逐帧误差扩散映射），避免逐帧调色板闪烁。
#   帧延时按 GIF 上限 1/100s 存档：30fps 存 30ms/帧（约快 10%），FramePlayer 按 Delay 逐帧读。
# 用法：python tools/mp4-to-gif.py 输入.mp4 [-o 输出.gif] [--tol 55] [--bg white|green|magenta|R,G,B]
import argparse, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

import imageio


def detect_bg(img, override):
    if override:
        if override == "white":
            return np.array([255, 255, 255], np.float32)
        if override == "green":
            return np.array([0, 177, 64], np.float32)
        if override == "magenta":
            return np.array([255, 0, 255], np.float32)
        return np.array([float(x) for x in override.split(",")], np.float32)
    b = np.concatenate([img[0, :, :3], img[-1, :, :3], img[:, 0, :3], img[:, -1, :3]]).astype(np.float32)
    return np.median(b, axis=0)


def key_bg(img, bg, tol, extra=None):
    """img: HxWx3 uint8 -> HxWx4 uint8（RGB 向背景色做过边缘过渡，alpha 硬切）；
    extra: 额外并入的背景掩码（如 --shadow 指定的地面阴影区）"""
    d = np.sqrt(((img[:, :, :3].astype(np.float32) - bg) ** 2).sum(axis=2))
    lbl, _ = ndimage.label(d < tol)
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    mask = np.isin(lbl, list(border))
    if extra is not None:
        mask = mask | extra
    if mask.mean() > 0.98:
        raise RuntimeError("整帧几乎被抠空——背景色识别失败，用 --bg 指定（white/green/R,G,B）")
    alpha = np.where(mask, 0, 255).astype(np.uint8)
    rim = ndimage.binary_dilation(mask) & ~mask
    w = np.clip(1.0 - d[rim] / tol, 0, 1)[..., None]
    out = img[:, :, :3].astype(np.float32)
    out[rim] = out[rim] * (1 - w) + bg * w
    out[mask] = bg
    return np.dstack([out.astype(np.uint8), alpha]), float(mask.mean())


def build_palette(rgba_iter, samples=12, max_pixels=200000):
    """从均匀抽帧的非透明像素建全局 255 色调色板（第 256 索引留给透明）"""
    pix = []
    step = max(1, len(rgba_iter) // samples)
    for im in rgba_iter[::step]:
        a = np.asarray(im)
        m = a[:, :, 3] > 128
        if m.any():
            pix.append(a[:, :, :3][m][::4])
    arr = np.concatenate(pix, axis=0)
    if len(arr) > max_pixels:
        arr = arr[:: len(arr) // max_pixels + 1]
    im = Image.fromarray(arr.reshape(-1, 1, 3), "RGB")
    return im.quantize(colors=255, method=Image.MEDIANCUT)


def to_p(rgba, pal_img):
    """RGBA 帧 -> 透明索引 255 的 P 模式帧（全局调色板 + 误差扩散）"""
    a = np.asarray(rgba)
    q = Image.fromarray(a[:, :, :3], "RGB").quantize(palette=pal_img, dither=Image.FLOYDSTEINBERG)
    p = np.array(q)
    p[a[:, :, 3] < 128] = 255
    out = Image.fromarray(p, "P")
    out.putpalette(pal_img.getpalette()[:255 * 3] + [0, 0, 0])
    return out


def checker(w, h, cell=16):
    yy, xx = np.mgrid[0:h, 0:w]
    c = (((yy // cell) + (xx // cell)) % 2 * 40 + 60).astype(np.uint8)
    return np.dstack([c, c, c + 20])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("-o", "--output")
    ap.add_argument("--tol", type=float, default=55.0, help="背景色容差（欧氏距离）")
    ap.add_argument("--bg", help="背景色 white/green/magenta/R,G,B（默认从边缘自动检测）")
    ap.add_argument("--shadow", help="地面阴影矩形 x0,y0,x1,y1，分号分隔多个；矩形内低饱和中亮度像素判为阴影抠掉")
    ap.add_argument("--shband", default="125,241", help="阴影亮度带 mn 下限,上限（配合 --shadow）")
    args = ap.parse_args()

    if args.shadow:
        lo, hi = (float(x) for x in args.shband.split(","))
        rects = [tuple(int(v) for v in r.split(",")) for r in args.shadow.split(";")]

        def sh_mask(frame):
            m = np.zeros(frame.shape[:2], bool)
            for x0, y0, x1, y1 in rects:
                sub = frame[y0:y1, x0:x1]
                mn = sub.min(axis=2).astype(np.float32)
                mx = sub.max(axis=2).astype(np.float32)
                m[y0:y1, x0:x1] = ((mx - mn) < 33) & (mn > lo) & (mn < hi)
            return m

    out = args.output or os.path.splitext(args.input)[0] + ".gif"
    r = imageio.get_reader(args.input)
    meta = r.get_meta_data()
    fps = meta.get("fps")
    dur = meta.get("duration")
    bg = None
    keyed, ratios = [], []
    for f in r:
        arr = np.asarray(f)
        if bg is None:
            bg = detect_bg(arr, args.bg)
        k, ratio = key_bg(arr, bg, args.tol, sh_mask(arr) if args.shadow else None)
        keyed.append(k)
        ratios.append(ratio)
    r.close()
    n = len(keyed)
    if n == 0:
        sys.exit("没读到帧")
    if not fps:
        fps = n / (dur or n / 30.0)
    fps = float(fps)
    h, w = keyed[0].shape[:2]

    pal = build_palette([Image.fromarray(k) for k in keyed])
    p_frames = [to_p(Image.fromarray(k), pal) for k in keyed]
    base = 1000.0 / fps
    units = max(2, int(round(base / 10)))
    ms = units * 10
    p_frames[0].save(
        out, save_all=True, append_images=p_frames[1:], loop=0,
        disposal=2, transparency=255, duration=[ms] * n, optimize=False,
    )

    # 预览图：抽 4 帧铺在棋盘格上，直接看抠底效果
    pv = Image.new("RGB", (w * 2, h * 2))
    cb = checker(w, h)
    for i, idx in enumerate(sorted(set([0, n // 3, 2 * n // 3, n - 1]))[:4]):
        a = np.asarray(keyed[idx], np.float32)
        al = a[:, :, 3:] / 255.0
        comp = a[:, :, :3] * al + cb * (1 - al)
        pv.paste(Image.fromarray(comp.astype(np.uint8)), ((i % 2) * w, (i // 2) * h))
    pv.save(os.path.splitext(out)[0] + "-preview.png")

    eff = 1000.0 / ms
    print("背景色=%s 容差=%.0f" % (bg.astype(int), args.tol))
    print("%s: %dx%d, %d 帧, 源 %.2ffps -> 存 %dms/帧(实际播 %.2ffps), 整圈 %.2fs, 背景抠除占比均值 %.1f%%, 文件 %.1fMB"
          % (out, w, h, n, fps, ms, eff, n * ms / 1000.0, 100 * sum(ratios) / len(ratios), os.path.getsize(out) / 1048576.0))


if __name__ == "__main__":
    main()
