"""Line icons drawn with Pillow (24-unit grid, 2-unit stroke, like Lucide).

Emoji render tiny and monochrome in Tk on Windows, so the UI uses these.
`icon(name, colour)` returns a CTkImage that follows light/dark mode.
"""
import math
from functools import lru_cache
from PIL import Image, ImageDraw
import customtkinter as ctk

S = 96            # source canvas, scaled down by CTkImage
U = S / 24        # one grid unit
W = round(2 * U)  # stroke width


def _p(x, y):
    return (x * U, y * U)


class _Pen:
    def __init__(self, img, color):
        self.d = ImageDraw.Draw(img)
        self.c = color

    def line(self, *pts, width=W):
        pts = [_p(*p) for p in pts]
        self.d.line(pts, fill=self.c, width=width, joint="curve")
        r = width / 2
        for x, y in (pts[0], pts[-1]):
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=self.c)

    def poly(self, *pts):
        self.line(*pts, pts[0])

    def rrect(self, x0, y0, x1, y1, r, fill=False):
        box = [*_p(x0, y0), *_p(x1, y1)]
        if fill:
            self.d.rounded_rectangle(box, radius=r * U, fill=self.c)
        else:
            self.d.rounded_rectangle(box, radius=r * U, outline=self.c, width=W)

    def circle(self, cx, cy, r, fill=False):
        box = [*_p(cx - r, cy - r), *_p(cx + r, cy + r)]
        if fill:
            self.d.ellipse(box, fill=self.c)
        else:
            self.d.ellipse(box, outline=self.c, width=W)

    def arc(self, cx, cy, r, start, end):
        box = [*_p(cx - r, cy - r), *_p(cx + r, cy + r)]
        self.d.arc(box, start, end, fill=self.c, width=W)


def _draw(name: str, color: str) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    p = _Pen(img, color)

    if name == "chat":
        p.rrect(3, 4, 21, 17, 4)
        p.line((8, 17), (5.5, 20.5), (12, 17))
    elif name == "models":
        p.poly((12, 2.5), (20.5, 7), (20.5, 17), (12, 21.5), (3.5, 17), (3.5, 7))
        p.line((3.5, 7), (12, 11.5), (20.5, 7))
        p.line((12, 11.5), (12, 21.5))
    elif name == "compare":
        p.rrect(3, 4, 11, 20, 2)
        p.rrect(13, 4, 21, 20, 2)
    elif name == "agent":
        p.poly((10, 4), (12, 10.5), (18, 12.5), (12, 14.5), (10, 21), (8, 14.5), (2, 12.5), (8, 10.5))
        p.line((19, 2.5), (19, 7.5))
        p.line((16.5, 5), (21.5, 5))
    elif name == "settings":
        pts = []
        for k in range(8):
            t = k * 45
            for a, r in ((t - 16, 7.4), (t - 8, 9.6), (t + 8, 9.6), (t + 16, 7.4)):
                pts.append((12 + r * math.cos(math.radians(a)), 12 + r * math.sin(math.radians(a))))
        p.poly(*pts)
        p.circle(12, 12, 3)
    elif name == "send":
        p.line((12, 19), (12, 5))
        p.line((6, 11), (12, 5), (18, 11))
    elif name == "stop":
        p.rrect(6.5, 6.5, 17.5, 17.5, 2.5, fill=True)
    elif name == "plus":
        p.line((12, 5), (12, 19))
        p.line((5, 12), (19, 12))
    elif name == "trash":
        p.line((4, 6.5), (20, 6.5))
        p.line((9, 6.5), (9, 4), (15, 4), (15, 6.5))
        p.line((6, 6.5), (7, 20), (17, 20), (18, 6.5))
    elif name == "download":
        p.line((12, 4), (12, 15))
        p.line((7, 10), (12, 15), (17, 10))
        p.line((5, 20), (19, 20))
    elif name == "folder":
        p.poly((3, 6), (9, 6), (11, 8.5), (21, 8.5), (21, 19), (3, 19))
    elif name == "check":
        p.line((5, 12.5), (10, 17.5), (19, 7))
    elif name == "x":
        p.line((6, 6), (18, 18))
        p.line((18, 6), (6, 18))
    elif name == "copy":
        p.line((4, 15), (4, 4), (15, 4))
        p.rrect(8, 8, 20, 20, 2)
    elif name == "sliders":
        p.line((4, 7), (7, 7))
        p.line((11, 7), (20, 7))
        p.circle(9, 7, 2)
        p.line((4, 17), (13, 17))
        p.line((17, 17), (20, 17))
        p.circle(15, 17, 2)
    elif name == "refresh":
        p.arc(12, 12, 8, 40, 320)
        p.line((14.5, 6.6), (18.2, 6.9), (18.6, 3.2))
    elif name == "search":
        p.circle(10.5, 10.5, 6.5)
        p.line((15.5, 15.5), (20, 20))
    elif name == "external":
        p.line((14, 4), (20, 4), (20, 10))
        p.line((20, 4), (11, 13))
        p.line((18, 14), (18, 20), (4, 20), (4, 6), (10, 6))
    elif name == "sidebar":
        p.rrect(3, 4, 21, 20, 3)
        p.line((15, 4.5), (15, 19.5))
    elif name == "dot":
        p.circle(12, 12, 5, fill=True)
    else:
        p.circle(12, 12, 8)
    return img


@lru_cache(maxsize=256)
def icon(name: str, color=("#52525B", "#A1A1AA"), size: int = 18) -> ctk.CTkImage:
    if isinstance(color, str):
        color = (color, color)
    return ctk.CTkImage(light_image=_draw(name, color[0]), dark_image=_draw(name, color[1]),
                        size=(size, size))


# ── app logo ──────────────────────────────────────────────────────────────────

def logo_image(px: int = 256) -> Image.Image:
    """Rounded square with a blue→violet gradient and a white spark."""
    n = px * 4
    grad = Image.new("RGBA", (n, n))
    top, bottom = (10, 132, 255), (124, 58, 237)
    gp = grad.load()
    for y in range(n):
        for x in range(n):
            t = (x + y) / (2 * n)
            gp[x, y] = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=n * 0.23, fill=255)
    out = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(out)
    cx, cy, r = n * 0.47, n * 0.53, n * 0.30
    k = 0.22  # waist of the four-point star
    star = []
    for i in range(8):
        a = math.radians(i * 45 - 90)
        rr = r if i % 2 == 0 else r * k * 1.35
        star.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(star, fill=(255, 255, 255, 255))
    sx, sy, sr = n * 0.75, n * 0.26, n * 0.09
    small = []
    for i in range(8):
        a = math.radians(i * 45 - 90)
        rr = sr if i % 2 == 0 else sr * 0.3
        small.append((sx + rr * math.cos(a), sy + rr * math.sin(a)))
    d.polygon(small, fill=(255, 255, 255, 230))
    return out.resize((px, px), Image.LANCZOS)


@lru_cache(maxsize=8)
def logo(size: int = 28) -> ctk.CTkImage:
    img = logo_image(size * 4)
    return ctk.CTkImage(light_image=img, dark_image=img, size=(size, size))
