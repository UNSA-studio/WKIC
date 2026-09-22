#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
为 Windows Keyboard Integrity Check 生成应用图标。

风格：Microsoft Fluent / Windows 11 应用图标 —— 圆角方形渐变底 + 白色键盘图形。
完全用代码绘制，不使用任何微软的商标资产。

输出：assets/app.ico （多尺寸，每个尺寸独立渲染 + 4x 超采样抗锯齿）
      assets/app_256.png （预览图）

用法：python3 tools/make_icon.py
"""

import os
import struct
import zlib
from PIL import Image, ImageDraw

# ---- 尺寸列表：覆盖 Windows 资源管理器 / 任务栏 / Alt+Tab 会用到的档位 ----
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

SUPERSAMPLE = 4

# ---- Fluent 主色调：上浅下深的微软蓝 ----
TOP_RGB = (0x2E, 0x93, 0xE8)
BOTTOM_RGB = (0x0A, 0x5A, 0xA0)

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def render(size):
    """渲染单个尺寸的图标（RGBA）。"""
    w = size * SUPERSAMPLE
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))

    # ---------- 1. 圆角方形 + 垂直渐变底 ----------
    gradient = Image.new("RGB", (w, w))
    gd = ImageDraw.Draw(gradient)
    for y in range(w):
        t = y / float(w - 1)
        t = min(1.0, t * 1.12)          # 顶部稍亮，更接近 Fluent 的高光走向
        gd.line([(0, y), (w, y)], fill=lerp(TOP_RGB, BOTTOM_RGB, t))

    mask = Image.new("L", (w, w), 0)
    margin = w * 0.045
    ImageDraw.Draw(mask).rounded_rectangle(
        [margin, margin, w - margin, w - margin],
        radius=w * 0.215, fill=255)
    img.paste(gradient, (0, 0), mask)

    # ---------- 2. 白色键盘本体 ----------
    d = ImageDraw.Draw(img)
    kx0, kx1 = w * 0.165, w * 0.835
    ky0, ky1 = w * 0.295, w * 0.715
    d.rounded_rectangle([kx0, ky0, kx1, ky1], radius=w * 0.055,
                        fill=(255, 255, 255, 255))

    # ---------- 3. 键帽 ----------
    small = size < 32
    rows = 2 if small else 3
    cols = 3 if small else 5

    pad = w * 0.038
    bx0, bx1 = kx0 + pad, kx1 - pad
    by0, by1 = ky0 + pad, ky1 - pad

    cell_w = (bx1 - bx0) / float(cols)
    cell_h = (by1 - by0) / float(rows)
    key_w = cell_w * 0.80
    key_h = cell_h * 0.66
    radius = max(1.0, key_w * 0.22)

    key_color = lerp(TOP_RGB, BOTTOM_RGB, 0.35) + (255,)

    for r in range(rows):
        cy = by0 + cell_h * r + (cell_h - key_h) / 2.0
        for c in range(cols):
            # 最后一行中间留给空格键
            if r == rows - 1 and c == cols // 2:
                continue
            cx = bx0 + cell_w * c + (cell_w - key_w) / 2.0
            d.rounded_rectangle([cx, cy, cx + key_w, cy + key_h],
                                radius=radius, fill=key_color)

    # ---------- 4. 空格键 ----------
    sp_w = (bx1 - bx0) * (0.48 if small else 0.52)
    sp_cx = (bx0 + bx1) / 2.0
    sp_y = by0 + cell_h * (rows - 1) + (cell_h - key_h) / 2.0
    d.rounded_rectangle([sp_cx - sp_w / 2.0, sp_y, sp_cx + sp_w / 2.0, sp_y + key_h],
                        radius=radius, fill=key_color)

    return img.resize((size, size), Image.LANCZOS)


def png_bytes(img):
    """把 PIL 图像编码成 PNG 字节流（用于塞进 ICO）。"""
    from io import BytesIO
    buf = BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path, images):
    """
    手写 ICO 容器：Vista 之后 ICO 允许直接内嵌 PNG，质量与体积都优于 BMP。
    结构：ICONDIR(6) + N * ICONDIRENTRY(16) + N * PNG data
    """
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)     # reserved, type=icon, count

    entries = b""
    blobs = b""
    offset = 6 + 16 * count

    for img in images:
        data = png_bytes(img)
        w, h = img.size
        entries += struct.pack(
            "<BBBBHHII",
            w if w < 256 else 0,      # 256 用 0 表示
            h if h < 256 else 0,
            0,                        # 调色板数（真彩为 0）
            0,                        # reserved
            1,                        # color planes
            32,                       # bits per pixel
            len(data),
            offset)
        blobs += data
        offset += len(data)

    with open(path, "wb") as f:
        f.write(header + entries + blobs)


def main():
    out_dir = os.path.normpath(OUT_DIR)
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    images = [render(s) for s in SIZES]

    ico_path = os.path.join(out_dir, "app.ico")
    write_ico(ico_path, images)

    preview_path = os.path.join(out_dir, "app_256.png")
    images[-1].save(preview_path)

    print("icon written : " + ico_path + "  (" + str(os.path.getsize(ico_path)) + " bytes)")
    print("preview      : " + preview_path)
    print("sizes        : " + ", ".join(str(s) for s in SIZES))


if __name__ == "__main__":
    main()
