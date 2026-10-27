# -*- coding: utf-8 -*-
# 找到 HDR Toggle 窗口并截其区域（物理像素），供视觉验证；不抢焦点
import ctypes, ctypes.wintypes as wt, sys
from PIL import ImageGrab

user32 = ctypes.windll.user32
user32.SetProcessDPIAware()   # GetWindowRect 与 ImageGrab 统一物理像素
found = []
@ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
def cb(h, lp):
    if user32.IsWindowVisible(h):
        buf = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(h, buf, 256)
        if buf.value == "HDR Toggle":
            r = wt.RECT()
            user32.GetWindowRect(h, ctypes.byref(r))
            found.append((h, r.left, r.top, r.right, r.bottom))
    return True
user32.EnumWindows(cb, 0)
if not found:
    print("WINDOW NOT FOUND"); sys.exit(1)
h, l, t, r, b = found[0]
print("rect", l, t, r, b)
img = ImageGrab.grab(bbox=(l-10, t-10, r+10, b+10))
img.save("tools/widget-shot.png")
print("saved", img.size)
