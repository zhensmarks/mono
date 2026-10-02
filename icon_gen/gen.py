#!/usr/bin/env python3
"""Tahap 3F: generator ikon tool rail PixaCompact gaya Photoshop.
Semua path digambar ORIGINAL dari nol (koordinat ditulis tangan / dihitung
di sini), viewBox 24x24, fill solid. Output: icons.json {name: Data} +
preview PNG 3 state (normal/hover/active).
"""
import json, math, os
from PIL import Image, ImageDraw

OUT = os.path.dirname(os.path.abspath(__file__))

def P(pts):
    """polygon -> subpath list of (x,y)"""
    return [tuple(p) for p in pts]

def rect(x, y, w, h):
    return P([(x, y), (x + w, y), (x + w, y + h), (x, y + h)])

def circle(cx, cy, r, n=24, a0=0.0, a1=360.0):
    pts = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return P(pts)

def ellipse(cx, cy, rx, ry, n=32, a0=0.0, a1=360.0):
    pts = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        pts.append((cx + rx * math.cos(a), cy + ry * math.sin(a)))
    return P(pts)

def bar(ax, ay, bx, by, w):
    dx, dy = bx - ax, by - ay
    L = math.hypot(dx, dy)
    px, py = -dy / L * w / 2, dx / L * w / 2
    return P([(ax + px, ay + py), (bx + px, by + py),
              (bx - px, by - py), (ax - px, ay - py)])

def sparkle(cx, cy, r):
    k = 0.20
    pts = []
    dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)]
    for i, (sx, sy) in enumerate(dirs):
        pts.append((cx + sx * r, cy + sy * r))
        nx, ny = dirs[(i + 1) % 4]
        pts.append((cx + (sx + nx) * k * r, cy + (sy + ny) * k * r))
    return P(pts)

def zigzag_strip(x0, y0, x1, y1, teeth, amp, width):
    pts = []
    for i in range(teeth * 2 + 1):
        t = i / (teeth * 2)
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t + (amp if i % 2 == 1 else 0)
        pts.append((x, y))
    # offset strip
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    px, py = -dy / L * width / 2, dx / L * width / 2
    top = [(x + px, y + py) for x, y in pts]
    bot = [(x - px, y - py) for x, y in reversed(pts)]
    return P(top + bot)

ICONS = {}

# ---------- 1. Move: panah 4-arah ----------
ICONS["BtnToolMove"] = [
    P([(10.5, 5.4), (13.5, 5.4), (13.5, 18.6), (10.5, 18.6)]),   # shaft vert
    P([(5.4, 10.5), (18.6, 10.5), (18.6, 13.5), (5.4, 13.5)]),   # shaft hor
    P([(12, 1.8), (9.0, 6.6), (15.0, 6.6)]),                      # head atas
    P([(12, 22.2), (9.0, 17.4), (15.0, 17.4)]),                   # head bawah
    P([(1.8, 12), (6.6, 9.0), (6.6, 15.0)]),                      # head kiri
    P([(22.2, 12), (17.4, 9.0), (17.4, 15.0)]),                   # head kanan
]

# ---------- 2. RectMarquee: persegi garis putus-putus ----------
def _dashes():
    subs = []
    t = 2.0
    segs = [(4.0, 3.2), (8.9, 3.2), (13.8, 3.2), (18.0, 2.0)]
    for s, dl in segs:
        subs.append(rect(s, 4.0, dl, t))          # atas
        subs.append(rect(s, 20.0 - t, dl, t))     # bawah
        subs.append(rect(4.0, s, t, dl))          # kiri
        subs.append(rect(20.0 - t, s, t, dl))     # kanan
    return subs
ICONS["BtnToolRectMarquee"] = _dashes()

# ---------- 3. EllipseMarquee: elips garis putus-putus ----------
def _ellipse_dashes():
    subs = []
    cx, cy, rx, ry = 12, 12, 8.0, 6.4
    th = 1.05
    n, sweep = 14, 17.0
    for i in range(n):
        a0 = i * 360.0 / n
        a1 = a0 + sweep
        outer, inner = [], []
        for j in range(5):
            a = math.radians(a0 + (a1 - a0) * j / 4)
            c, s = math.cos(a), math.sin(a)
            outer.append((cx + (rx + th) * c, cy + (ry + th) * s))
            inner.append((cx + (rx - th) * c, cy + (ry - th) * s))
        subs.append(P(outer + inner[::-1]))
    return subs
ICONS["BtnToolEllipseMarquee"] = _ellipse_dashes()

# ---------- 4. Lasso: loop tali + ekor ----------
ICONS["BtnToolLasso"] = [
    ellipse(11.2, 11.0, 6.6, 5.8, n=30),   # outer (lubang via evenodd)
    ellipse(11.2, 11.0, 4.7, 3.9, n=26),   # inner hole
    bar(7.0, 15.0, 3.9, 20.3, 2.0),        # ekor tali
    circle(7.3, 14.7, 1.7, n=12),          # simpul
]

# ---------- 5. PolyLasso: polyline bersudut + anchor ----------
def _poly_lasso():
    pts = [(5.0, 17.5), (8.5, 11.5), (13.0, 13.5), (17.5, 6.5)]
    subs = []
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        subs.append(bar(ax, ay, bx, by, 2.0))
    for x, y in pts:
        subs.append(rect(x - 1.5, y - 1.5, 3.0, 3.0))
    return subs
ICONS["BtnToolPolyLasso"] = _poly_lasso()

# ---------- 6. MagicWand: tongkat miring + percikan ----------
ICONS["BtnToolWand"] = [
    bar(5.5, 18.5, 13.0, 11.0, 3.2),
    circle(5.5, 18.5, 1.6, n=12),
    circle(13.0, 11.0, 1.6, n=12),
    sparkle(16.6, 7.2, 3.1),
    sparkle(20.0, 11.2, 2.2),
    sparkle(17.2, 3.6, 1.8),
]

# ---------- 7. Brush: kuas miring ----------
def _brush():
    subs = []
    # bristle: segitiga melengkung, ujung di (19,5)
    T = (19.0, 5.0)
    F1, F2 = (13.8, 10.5), (15.6, 12.3)
    left = []
    for j in range(5):
        t = j / 4
        cx, cy = 16.5, 6.2
        x = (1 - t) ** 2 * T[0] + 2 * (1 - t) * t * cx + t ** 2 * F1[0]
        y = (1 - t) ** 2 * T[1] + 2 * (1 - t) * t * cy + t ** 2 * F1[1]
        left.append((x, y))
    right = []
    for j in range(5):
        t = j / 4
        cx, cy = 18.2, 8.8
        x = (1 - t) ** 2 * F2[0] + 2 * (1 - t) * t * cx + t ** 2 * T[0]
        y = (1 - t) ** 2 * F2[1] + 2 * (1 - t) * t * cy + t ** 2 * T[1]
        right.append((x, y))
    subs.append(P(left + right))
    # ferrule
    subs.append(P([(13.8, 10.5), (15.6, 12.3), (12.8, 15.1), (11.0, 13.3)]))
    # handle
    subs.append(bar(11.9, 14.2, 5.2, 20.9, 4.0))
    subs.append(circle(5.2, 20.9, 2.0, n=14))
    return subs
ICONS["BtnToolBrush"] = _brush()

# ---------- 8. Eraser: penghapus miring ----------
ICONS["BtnToolEraser"] = [
    P([(5.5, 17.5), (11.5, 6.5), (20.5, 6.5), (14.5, 17.5)]),  # body
    P([(10.1, 9.0), (19.1, 9.0), (18.3, 11.4), (9.3, 11.4)]),  # band (lubang)
]

# ---------- 9. Pen: nib pena ----------
def _pen():
    base = [(12, 2.8), (13.2, 5.0), (14.0, 7.5), (14.2, 10.0),
            (13.4, 12.8), (12, 13.8), (10.6, 12.8),
            (9.8, 10.0), (10.0, 7.5), (10.8, 5.0)]
    pts = [(12 + (x - 12) * 1.18, 9 + (y - 9) * 1.18) for x, y in base]
    return [
        P(pts),
        circle(12, 8.6, 1.6, n=14),                       # lubang napas
        P([(11.5, 10.4), (12.5, 10.4), (12.5, 13.4), (11.5, 13.4)]),  # slit
    ]
ICONS["BtnToolPen"] = _pen()

# ---------- 10. RefineEdge: kuas mini + tepi bergerigi ----------
ICONS["BtnToolRefineEdge"] = [
    bar(5.0, 18.0, 10.0, 13.0, 2.8),                       # gagang mini
    circle(5.0, 18.0, 1.4, n=12),
    P([(14.6, 8.2), (11.0, 11.6), (12.6, 13.2)]),           # bristle
    zigzag_strip(3.5, 19.6, 20.5, 18.2, teeth=7, amp=1.7, width=1.5),
]

# ---------- 11. Pan: tangan ----------
def _hand():
    subs = []
    # jari: 4 jari rounded
    for i, (cx, top) in enumerate([(8.7, 7.0), (11.2, 6.2), (13.7, 6.4), (16.2, 7.4)]):
        w = 2.05
        pts = [(cx - w / 2, 13.5), (cx - w / 2, top)]
        for j in range(7):
            a = math.pi - j * math.pi / 6
            pts.append((cx + (w / 2) * math.cos(a), top - (w / 2) * math.sin(a)))
        pts.append((cx + w / 2, 13.5))
        subs.append(P(pts))
    # telapak
    subs.append(P([(7.6, 12.5), (17.4, 12.5), (17.4, 19.0),
                   (16.4, 20.8), (8.6, 20.8), (7.6, 19.0)]))
    # ibu jari
    subs.append(bar(7.8, 16.0, 4.6, 13.4, 2.4))
    subs.append(circle(4.6, 13.4, 1.35, n=12))
    return subs
ICONS["BtnToolPan"] = _hand()

# ---------- 12. QuickMask: lingkaran + setengah arsir ----------
def _quickmask():
    subs = [circle(12, 12, 8.6, n=36), circle(12, 12, 6.9, n=32)]
    # arsir diagonal di setengah kanan
    c = 15.0
    while c < 33.0:
        xs = []
        x = 12.0
        while x <= 18.9:
            y = -x + c
            if (x - 12) ** 2 + (y - 12) ** 2 <= 6.9 ** 2:
                xs.append(x)
            x += 0.1
        if xs and max(xs) - min(xs) > 0.6:
            x0, x1 = min(xs), max(xs)
            y0, y1 = -x0 + c, -x1 + c
            dx, dy = x1 - x0, y1 - y0
            L = math.hypot(dx, dy)
            px, py = -dy / L * 0.55, dx / L * 0.55
            subs.append(P([(x0 + px, y0 + py), (x1 + px, y1 + py),
                           (x1 - px, y1 - py), (x0 - px, y0 - py)]))
        c += 2.4
    return subs
ICONS["BtnQuickMask"] = _quickmask()

# ---------- 13. MaskView: lingkaran setengah ----------
ICONS["BtnMaskView"] = [
    circle(12, 12, 8.6, n=36),
    circle(12, 12, 6.9, n=32),
    P([(12, 5.1)] + [(12 + 6.9 * math.cos(math.radians(a)),
                      12 + 6.9 * math.sin(math.radians(a)))
                     for a in range(90, 271, 6)] + [(12, 18.9)]),
]

# ---------- 14. RefineHair: kepala + bahu + rambut ----------
def _hair():
    subs = [circle(12, 9.4, 4.3, n=26)]
    # spike rambut di atas kepala
    for deg in range(200, 341, 20):
        a = math.radians(deg)
        a1 = math.radians(deg - 9)
        a2 = math.radians(deg + 9)
        subs.append(P([
            (12 + 4.1 * math.cos(a1), 9.4 + 4.1 * math.sin(a1)),
            (12 + 6.4 * math.cos(a), 9.4 + 6.4 * math.sin(a)),
            (12 + 4.1 * math.cos(a2), 9.4 + 4.1 * math.sin(a2)),
        ]))
    # bahu
    subs.append(P([(4.5, 21.5), (6.0, 17.6), (9.0, 16.0), (9.2, 14.6),
                   (14.8, 14.6), (15.0, 16.0), (18.0, 17.6), (19.5, 21.5)]))
    return subs
ICONS["BtnRefineHair"] = _hair()

# ---------- emit Data ----------
def fmt(v):
    s = f"{v:.1f}".rstrip("0").rstrip(".")
    return "0" if s == "-0" else s

def to_data(subs):
    parts = []
    for poly in subs:
        d = "M" + "L".join(f"{fmt(x)},{fmt(y)}" for x, y in poly) + "Z"
        parts.append(d)
    return "".join(parts)

DATA = {k: to_data(v) for k, v in ICONS.items()}

# ---------- preview ----------
BG = {"normal": "#333333", "hover": "#404040", "active": "#1F3A52"}
BORDER = {"normal": "#555555", "hover": "#31A8FF", "active": "#31A8FF"}

def render_icon(draw, data_subs, cx, cy, scale, ox, oy, fill):
    # rasterize polygon subpaths with evenodd via mask
    S = 96
    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    for poly in data_subs:
        pts = [(ox + x * scale, oy + y * scale) for x, y in poly]
        md.polygon(pts, fill=255)
    # evenodd: use PIL's approach -> draw each poly, then composite with XOR not available;
    # approximate: nested holes drawn with fill 0 after (works for our ring/hole cases)
    return mask

def rasterize_evenodd(subs, W, H, ox, oy, sc):
    """Scanline even-odd fill — sama seperti Avalonia StreamGeometry."""
    mask = Image.new("L", (W, H), 0)
    px = mask.load()
    edges = []
    for poly in subs:
        pts = [(ox + x * sc, oy + y * sc) for x, y in poly]
        n = len(pts)
        for i in range(n):
            x1, y1 = pts[i]
            x2, y2 = pts[(i + 1) % n]
            if y1 == y2:
                continue
            edges.append((x1, y1, x2, y2))
    for y in range(H):
        ys = y + 0.5
        xs = []
        for x1, y1, x2, y2 in edges:
            if (y1 <= ys < y2) or (y2 <= ys < y1):
                xs.append(x1 + (ys - y1) / (y2 - y1) * (x2 - x1))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            xa = int(math.ceil(xs[i]))
            xb = int(math.floor(xs[i + 1]))
            for x in range(xa, xb + 1):
                if 0 <= x < W:
                    px[x, y] = 255
    return mask

def draw_state(base_draw, subs_list, names, bg, border, y0):
    x = 8
    for subs, name in zip(subs_list, names):
        base_draw.rounded_rectangle([x, y0, x + 36 * SS, y0 + 36 * SS],
                                   radius=3 * SS, fill=bg, outline=border, width=SS)
        # icon 16px centered in 36px button
        sc = 16 / 24 * SS
        ox = x + (36 * SS - 24 * sc) / 2
        oy = y0 + (36 * SS - 24 * sc) / 2
        m = rasterize_evenodd(subs, 36 * SS, 36 * SS, ox - x, oy - y0, sc)
        white = Image.new("RGB", (36 * SS, 36 * SS), (255, 255, 255))
        base.paste(white, (x, y0), m)
        x += 36 * SS + 6 * SS

SS = 4  # supersample
names = list(ICONS.keys())
W = 8 + (36 * SS + 6 * SS) * len(names)
H = 3 * (36 * SS + 14 * SS) + 16
base = Image.new("RGB", (W, H), (24, 24, 24))
bd = ImageDraw.Draw(base)
subs_list = [ICONS[n] for n in names]
for i, (state, bg) in enumerate(BG.items()):
    draw_state(bd, subs_list, names, bg, BORDER[state], 8 + i * (36 * SS + 14 * SS))
# labels
from PIL import ImageFont
try:
    fnt = ImageFont.load_default()
    x = 8
    for n in names:
        bd.text((x, 8 + 2 * (36 * SS + 14 * SS) + 36 * SS + 4), n.replace("BtnTool", "").replace("Btn", ""), fill=(180, 180, 180), font=fnt)
        x += 36 * SS + 6 * SS
except Exception:
    pass
base = base.resize((W // 2, H // 2), Image.LANCZOS)
base.save(os.path.join(OUT, "preview.png"))

with open(os.path.join(OUT, "icons.json"), "w") as f:
    json.dump(DATA, f, indent=1)
print("wrote preview.png + icons.json,", len(DATA), "icons")
for k, v in DATA.items():
    print(k, len(v))
