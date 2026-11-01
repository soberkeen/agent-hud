# -*- coding: utf-8 -*-
"""抓运行中的 Agent HUD:
  cap_hud.py panel     -> PrintWindow 全内容 + 屏幕区域图
  cap_hud.py bubble    -> 发一条真 notify,1.5s 后连 HUD 带气泡截屏
不抢焦点,不改窗口状态。"""
import ctypes, ctypes.wintypes as wt, sys, subprocess, time, os
from PIL import ImageGrab, Image

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
user32.SetProcessDPIAware()
HERE = os.path.dirname(os.path.abspath(__file__))

def find_hud():
    found = []
    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        if user32.IsWindowVisible(h):
            buf = ctypes.create_unicode_buffer(256)
            user32.GetWindowTextW(h, buf, 256)
            if buf.value == "Agent HUD":
                r = wt.RECT()
                user32.GetWindowRect(h, ctypes.byref(r))
                found.append((h, r.left, r.top, r.right, r.bottom))
        return True
    user32.EnumWindows(cb, 0)
    return found[0] if found else None

def print_window(h, w, hgt, out):
    # PrintWindow 全内容(含分层窗口),落到 BMP 再转 PNG
    hdc = user32.GetWindowDC(h)
    mem = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, hgt)
    gdi32.SelectObject(mem, bmp)
    PW_RENDERFULLCONTENT = 2
    ok = user32.PrintWindow(h, mem, PW_RENDERFULLCONTENT)
    class BMPINFOHEADER(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                    ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                    ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                    ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                    ("biClrImportant", ctypes.c_uint32)]
    bi = BMPINFOHEADER(ctypes.sizeof(BMPINFOHEADER), w, -hgt, 1, 32, 0, 0, 0, 0, 0, 0)
    buf = ctypes.create_string_buffer(w * hgt * 4)
    gdi32.GetDIBits(mem, bmp, 0, hgt, buf, ctypes.byref(bi), 0)
    img = Image.frombuffer("RGBA", (w, hgt), buf.raw, "raw", "BGRA", 0, 1)
    img.save(out)
    gdi32.DeleteObject(bmp); gdi32.DeleteDC(mem); user32.ReleaseDC(h, hdc)
    return ok, img

mode = sys.argv[1] if len(sys.argv) > 1 else "panel"
hit = find_hud()
if not hit:
    print("HUD WINDOW NOT FOUND"); sys.exit(1)
h, l, t, r, b = hit
w, hgt = r - l, b - t
print("rect", l, t, r, b, "size", w, hgt)

if mode == "panel":
    ok, img = print_window(h, w, hgt, os.path.join(HERE, "hud_pw.png"))
    print("printwindow ok:", ok)
    img2 = ImageGrab.grab(bbox=(l-14, t-14, r+14, hgt+t+14))
    img2.save(os.path.join(HERE, "hud_screen.png"))
    print("screen grab saved", img2.size)
elif mode == "bubble":
    exe = os.path.join(os.path.dirname(os.path.dirname(HERE)), "agent-hud.exe")
    subprocess.Popen([exe, "-notify", "任务完成", "ZCode 会话已结束，快回来看一眼吧", "zcode"])
    time.sleep(1.6)
    # 气泡 288 DIP*1.25=360px,在 HUD 左侧;多截一大片
    img2 = ImageGrab.grab(bbox=(max(0, l-560), max(0, t-40), min(3840, r+60), min(2160, b+60)))
    img2.save(os.path.join(HERE, "hud_bubble.png"))
    print("bubble grab saved", img2.size)
