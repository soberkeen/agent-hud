# -*- coding: utf-8 -*-
# PrintWindow 抓窗口自身表面（不受遮挡影响），检测动画是否真的在渲染
import ctypes, ctypes.wintypes as wt, time
from PIL import Image, ImageChops

u, g = ctypes.windll.user32, ctypes.windll.gdi32

class BMIH(ctypes.Structure):
    _fields_ = [('biSize', ctypes.c_uint32), ('biWidth', ctypes.c_int32), ('biHeight', ctypes.c_int32),
                ('biPlanes', ctypes.c_uint16), ('biBitCount', ctypes.c_uint16), ('biCompression', ctypes.c_uint32),
                ('biSizeImage', ctypes.c_uint32), ('biXPels', ctypes.c_int32), ('biYPels', ctypes.c_int32),
                ('biClrUsed', ctypes.c_uint32), ('biClrImportant', ctypes.c_uint32)]

def grab_hwnd(h, w, hgt):
    hdc = u.GetWindowDC(h)
    mem = g.CreateCompatibleDC(hdc)
    bmp = g.CreateCompatibleBitmap(hdc, w, hgt)
    g.SelectObject(mem, bmp)
    PW_RENDERFULLCONTENT = 2
    u.PrintWindow(h, mem, PW_RENDERFULLCONTENT)
    bmi = BMIH()
    bmi.biSize = ctypes.sizeof(BMIH); bmi.biWidth = w; bmi.biHeight = -hgt
    bmi.biPlanes = 1; bmi.biBitCount = 32; bmi.biCompression = 0
    buf = ctypes.create_string_buffer(w * hgt * 4)
    g.GetDIBits(mem, bmp, 0, hgt, buf, ctypes.byref(bmi), 0)
    img = Image.frombuffer('RGBA', (w, hgt), buf, 'raw', 'BGRA', 0, 1).convert('RGB')
    g.DeleteObject(bmp); g.DeleteDC(mem); u.ReleaseDC(h, hdc)
    return img

found = []
@ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
def cb(h, lp):
    if u.IsWindowVisible(h):
        b = ctypes.create_unicode_buffer(64)
        u.GetWindowTextW(h, b, 64)
        if b.value == 'HDR Toggle':
            r = wt.RECT(); u.GetWindowRect(h, ctypes.byref(r))
            found.append((h, r.right - r.left, r.bottom - r.top))
    return True
u.EnumWindows(cb, 0)
h, w, hgt = found[0]
print('window %dx%d' % (w, hgt))
a = grab_hwnd(h, w, hgt)
time.sleep(1.0)
c = grab_hwnd(h, w, hgt)
for name, y0, y1 in [('HDR', 5, 100), ('df', 110, 210), ('zc', 215, 315)]:
    d = ImageChops.difference(a.crop((8, y0, w - 8, y1)), c.crop((8, y0, w - 8, y1))).getbbox()
    print(name, 'diff:', d)
a.save('tools/pw-a.png'); c.save('tools/pw-b.png')
print('saved pw-a/pw-b')
