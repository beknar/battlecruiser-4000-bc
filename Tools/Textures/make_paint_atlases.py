#!/usr/bin/env python3
"""
The ship paint's decal atlases for Battlecruiser 4000 BC (ShipPaint, the Carrier/ShipPaint shader).

Everything is drawn here from basic shapes (polygons, circles, thick strokes), with a tiny
rasteriser of its own and a PNG writer from the standard library: no third-party art, fonts or
packages. Run it again to remake the atlases.

Usage:
    python make_paint_atlases.py [out_dir]

Default out_dir: <project>/Assets/Carrier/Resources/Paint (project = two folders above this script).

It writes two greyscale PNGs (white shape on black; the shader reads the red channel):
    paint_logos.png  1024 x 768: 12 emblems, 4 across and 3 down, 256 px each, row 0 at the top:
                     Star, Wings, Skull, Shield, Eagle, Lightning, Anchor, Planet, Claw, Crescent,
                     Comet, Gear (ShipPaint.LogoNames, in that order).
    paint_logos_ui.png  512 x 384: the same, white with the shapes in the alpha (RGBA), for the
                     paint screen (the GUI would draw a one-channel texture red).
    paint_font.png   768 x 640: bold block stencil letters, 8 across and 5 down, 96 x 128 px each:
                     cell 0 blank (space), 1-26 A-Z, 27-36 0-9, 37 the dash.
"""
import math
import os
import struct
import sys
import zlib

SS = 4  # supersampling: every shape is drawn 4 x 4 finer and averaged down


class Canvas:
    """A one-channel canvas, drawn at SS times the output size. Shapes are given in cell units
    (0..1 across the cell, y down) and drawn into the cell at (ox, oy) of size (cw, ch) pixels."""

    def __init__(self, w, h):
        self.w, self.h = w * SS, h * SS
        self.px = bytearray(self.w * self.h)
        self.cell(0, 0, w, h)

    def cell(self, ox, oy, cw, ch):
        self.ox, self.oy, self.cw, self.ch = ox * SS, oy * SS, cw * SS, ch * SS

    def _to_px(self, p):
        return (self.ox + p[0] * self.cw, self.oy + p[1] * self.ch)

    def _span(self, y, x0, x1, v):
        x0 = max(int(math.ceil(x0 - 0.5)), 0)
        x1 = min(int(math.floor(x1 - 0.5)), self.w - 1)
        if x1 < x0:
            return
        row = y * self.w
        self.px[row + x0:row + x1 + 1] = (b'\xff' if v else b'\x00') * (x1 - x0 + 1)

    def poly(self, pts, v=1):
        """Fill a polygon (even-odd)."""
        pp = [self._to_px(p) for p in pts]
        ys = [p[1] for p in pp]
        y0 = max(int(math.floor(min(ys))), 0)
        y1 = min(int(math.ceil(max(ys))), self.h - 1)
        n = len(pp)
        for y in range(y0, y1 + 1):
            yc = y + 0.5
            xs = []
            for i in range(n):
                ax, ay = pp[i]
                bx, by = pp[(i + 1) % n]
                if (ay <= yc < by) or (by <= yc < ay):
                    xs.append(ax + (yc - ay) * (bx - ax) / (by - ay))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                self._span(y, xs[i], xs[i + 1], v)

    def ellipse(self, cx, cy, rx, ry, v=1, rot=0.0, n=96):
        c, s = math.cos(rot), math.sin(rot)
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            x, y = rx * math.cos(a), ry * math.sin(a)
            pts.append((cx + x * c - y * s, cy + x * s + y * c))
        self.poly(pts, v)

    def circle(self, cx, cy, r, v=1):
        # square pixels: in a non-square cell the radius is taken across the cell's width
        self.ellipse(cx, cy, r, r * self.cw / self.ch, v)

    def ring(self, cx, cy, r0, r1):
        self.circle(cx, cy, r1, 1)
        self.circle(cx, cy, r0, 0)

    def rect(self, x0, y0, x1, y1, v=1):
        self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], v)

    def stroke(self, pts, w, v=1, aspect=1.0):
        """A thick polyline of half-width w (in cell heights), round-jointed."""
        for i in range(len(pts) - 1):
            (ax, ay), (bx, by) = pts[i], pts[i + 1]
            dx, dy = (bx - ax) * aspect, by - ay
            L = math.hypot(dx, dy) or 1.0
            nx, ny = -dy / L * w / aspect, dx / L * w
            self.poly([(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)], v)
        for (x, y) in pts:
            self.ellipse(x, y, w / aspect, w, v, n=24)

    def save(self, path):
        W, H = self.w // SS, self.h // SS
        out = bytearray(W * H)
        area = SS * SS
        px, w = self.px, self.w
        for y in range(H):
            rows = [px[(y * SS + k) * w:(y * SS + k + 1) * w] for k in range(SS)]
            for x in range(W):
                t = 0
                xs = x * SS
                for r in rows:
                    t += sum(r[xs:xs + SS])
                out[y * W + x] = t // area
        write_png(path, W, H, out)
        return W, H, out


def halve(w, h, grey):
    """Half the size (a 2 x 2 average)."""
    W, H = w // 2, h // 2
    out = bytearray(W * H)
    for y in range(H):
        a, b = (2 * y) * w, (2 * y + 1) * w
        for x in range(W):
            out[y * W + x] = (grey[a + 2 * x] + grey[a + 2 * x + 1] + grey[b + 2 * x] + grey[b + 2 * x + 1]) // 4
    return W, H, out


def write_png(path, w, h, grey, rgba=False):
    """Greyscale, or (rgba) white with the grey as its alpha."""
    if rgba:
        raw = b''.join(b'\x00' + b''.join(b'\xff\xff\xff' + bytes((grey[y * w + x],)) for x in range(w)) for y in range(h))
    else:
        raw = b''.join(b'\x00' + bytes(grey[y * w:(y + 1) * w]) for y in range(h))

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)

    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6 if rgba else 0, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(png)


def star_pts(cx, cy, r0, r1, n=5, rot=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r0
        a = rot + math.pi * i / n
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def arc_pts(cx, cy, r, a0, a1, n=40):
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


# ------------------------------------------------------------------------------------------ logos

def logo_star(c):
    c.ring(0.5, 0.5, 0.40, 0.47)
    c.poly(star_pts(0.5, 0.52, 0.15, 0.37))


def logo_wings(c):
    for side in (-1, 1):
        def X(x):
            return 0.5 + side * x
        # three feathers, each longer than the one below it
        c.poly([(X(0.08), 0.40), (X(0.47), 0.26), (X(0.44), 0.36), (X(0.08), 0.50)])
        c.poly([(X(0.08), 0.50), (X(0.40), 0.42), (X(0.36), 0.52), (X(0.08), 0.60)])
        c.poly([(X(0.08), 0.60), (X(0.31), 0.57), (X(0.26), 0.66), (X(0.08), 0.69)])
    c.circle(0.5, 0.50, 0.11)
    c.circle(0.5, 0.50, 0.055, 0)
    c.poly(star_pts(0.5, 0.50, 0.022, 0.05))


def logo_skull(c):
    c.circle(0.5, 0.42, 0.30)
    c.rect(0.33, 0.55, 0.67, 0.80)
    c.circle(0.38, 0.45, 0.085, 0)
    c.circle(0.62, 0.45, 0.085, 0)
    c.poly([(0.5, 0.55), (0.45, 0.65), (0.55, 0.65)], 0)
    for x in (0.415, 0.5, 0.585):
        c.rect(x - 0.012, 0.70, x + 0.012, 0.80, 0)
    # crossed bones behind
    for s in (-1, 1):
        c.stroke([(0.5 - 0.40 * s, 0.70), (0.5 + 0.40 * s, 0.95)], 0.035)
        for (x, y) in ((0.5 - 0.40 * s, 0.70), (0.5 + 0.40 * s, 0.95)):
            c.circle(x, y, 0.045)
    c.rect(0.33, 0.80, 0.67, 0.86, 0)


def shield_outline(scale):
    """A heater shield: straight sides for the top half, curving in to a point (scaled about its middle)."""
    right = [(0.82, 0.10), (0.82, 0.45)]
    for i in range(1, 21):
        t = i / 20
        # a quadratic curve from (0.82, 0.45) through the control (0.82, 0.78) to the point (0.5, 0.92)
        x = (1 - t) ** 2 * 0.82 + 2 * (1 - t) * t * 0.82 + t * t * 0.5
        y = (1 - t) ** 2 * 0.45 + 2 * (1 - t) * t * 0.78 + t * t * 0.92
        right.append((x, y))
    left = [(1.0 - x, y) for (x, y) in reversed(right[:-1])]
    return [(0.5 + (x - 0.5) * scale, 0.48 + (y - 0.48) * scale) for (x, y) in right + left]


def logo_shield(c):
    c.poly(shield_outline(1.0))
    c.poly(shield_outline(0.86), 0)
    c.poly(shield_outline(0.74))
    # a chevron cut across it
    c.poly([(0.20, 0.36), (0.5, 0.56), (0.80, 0.36), (0.80, 0.25), (0.5, 0.45), (0.20, 0.25)], 0)


def logo_eagle(c):
    # spread wings, stepped feathers, a head turned to the right and a fan tail
    body = [(0.5, 0.25), (0.56, 0.32), (0.58, 0.62), (0.5, 0.70), (0.42, 0.62), (0.44, 0.32)]
    c.poly(body)
    for side in (-1, 1):
        def X(x):
            return 0.5 + side * x
        c.poly([(X(0.05), 0.36), (X(0.46), 0.18), (X(0.40), 0.30), (X(0.47), 0.32), (X(0.38), 0.42),
                (X(0.44), 0.45), (X(0.32), 0.53), (X(0.36), 0.57), (X(0.05), 0.55)])
    c.poly([(0.42, 0.66), (0.58, 0.66), (0.66, 0.86), (0.5, 0.80), (0.34, 0.86)])
    c.circle(0.52, 0.22, 0.07)
    c.poly([(0.56, 0.18), (0.66, 0.22), (0.57, 0.27)])
    c.circle(0.535, 0.205, 0.015, 0)


def logo_lightning(c):
    c.ring(0.5, 0.5, 0.40, 0.47)
    c.poly([(0.56, 0.12), (0.30, 0.54), (0.47, 0.54), (0.40, 0.88), (0.70, 0.42), (0.53, 0.42), (0.63, 0.12)])


def logo_anchor(c):
    c.ring(0.5, 0.17, 0.045, 0.09)
    c.rect(0.465, 0.25, 0.535, 0.82)
    c.rect(0.30, 0.32, 0.70, 0.38)
    c.circle(0.30, 0.35, 0.04)
    c.circle(0.70, 0.35, 0.04)
    # the arms: the bottom of a thick ring, with a fluke on each end
    outer = arc_pts(0.5, 0.52, 0.34, 0.15 * math.pi, 0.85 * math.pi)
    inner = arc_pts(0.5, 0.52, 0.27, 0.85 * math.pi, 0.15 * math.pi)
    c.poly(outer + inner)
    for s in (-1, 1):
        a = math.pi * (0.5 - 0.35 * s)
        x, y = 0.5 + 0.305 * math.cos(a), 0.52 + 0.305 * math.sin(a)
        c.poly([(x - 0.08 * s, y + 0.02), (x + 0.03 * s, y - 0.12), (x + 0.06 * s, y + 0.03)])


def ellipse_arc(cx, cy, rx, ry, rot, a0, a1, n=48):
    c, s = math.cos(rot), math.sin(rot)
    pts = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        x, y = rx * math.cos(a), ry * math.sin(a)
        pts.append((cx + x * c - y * s, cy + x * s + y * c))
    return pts


def logo_planet(c):
    rot = -0.35
    # the ring all round, a gap cut round the planet's top half (where the ring is behind it),
    # the planet, then the front of the ring again over it with a thin gap above
    c.ellipse(0.5, 0.5, 0.47, 0.15, 1, rot)
    c.ellipse(0.5, 0.5, 0.37, 0.09, 0, rot)
    c.poly(arc_pts(0.5, 0.5, 0.29, math.pi + rot, 2 * math.pi + rot), 0)
    c.circle(0.5, 0.5, 0.25)
    c.poly(ellipse_arc(0.5, 0.5, 0.40, 0.11, rot, 0, math.pi) + ellipse_arc(0.5, 0.5, 0.33, 0.065, rot, math.pi, 0), 0)
    c.poly(ellipse_arc(0.5, 0.5, 0.47, 0.15, rot, 0, math.pi) + ellipse_arc(0.5, 0.5, 0.37, 0.09, rot, math.pi, 0), 1)
    for (x, y, r) in ((0.15, 0.20, 0.025), (0.82, 0.80, 0.02), (0.85, 0.18, 0.015)):
        c.poly(star_pts(x, y, r * 0.4, r * 1.6, 4, 0))


def logo_claw(c):
    # three tapering slashes, curved
    for k in (-1, 0, 1):
        pts_l, pts_r = [], []
        for i in range(21):
            t = i / 20
            x = 0.32 + 0.16 * k + 0.30 * t + 0.06 * math.sin(t * math.pi)
            y = 0.12 + 0.76 * t
            w = 0.055 * math.sin(math.pi * min(1.0, 0.15 + t * 0.95))
            pts_l.append((x - w, y + w * 0.3))
            pts_r.append((x + w, y - w * 0.3))
        c.poly(pts_l + list(reversed(pts_r)))


def logo_crescent(c):
    c.circle(0.46, 0.5, 0.38)
    c.circle(0.60, 0.44, 0.33, 0)
    c.poly(star_pts(0.70, 0.52, 0.06, 0.15))


def logo_comet(c):
    c.circle(0.68, 0.32, 0.15)
    for (a, w, L) in ((0.0, 0.11, 0.62), (-0.30, 0.05, 0.42), (0.30, 0.05, 0.42)):
        ang = math.atan2(0.68, -0.68) + a
        dx, dy = math.cos(ang), math.sin(ang)
        nx, ny = -dy, dx
        hx, hy = 0.68, 0.32
        c.poly([(hx + nx * w, hy + ny * w), (hx + dx * L, hy + dy * L), (hx - nx * w, hy - ny * w)])
    c.circle(0.68, 0.32, 0.07, 0)
    c.circle(0.68, 0.32, 0.035)


def logo_gear(c):
    n = 10
    for i in range(n):
        a = 2 * math.pi * i / n
        ca, sa = math.cos(a), math.sin(a)
        pts = []
        for (r, t) in ((0.30, -0.10), (0.44, -0.075), (0.44, 0.075), (0.30, 0.10)):
            pts.append((0.5 + r * ca - t * sa, 0.5 + r * sa + t * ca))
        c.poly(pts)
    c.circle(0.5, 0.5, 0.34)
    c.circle(0.5, 0.5, 0.20, 0)
    c.circle(0.5, 0.5, 0.11)
    c.circle(0.5, 0.5, 0.05, 0)


LOGOS = [logo_star, logo_wings, logo_skull, logo_shield, logo_eagle, logo_lightning,
         logo_anchor, logo_planet, logo_claw, logo_crescent, logo_comet, logo_gear]

# ------------------------------------------------------------------------------------------- font

# Each glyph: strokes on a 4 x 6 grid (x right, y down), and stencil cuts (grid-unit rectangles
# cut out after the strokes: the gaps that hold a stencil's middles in).
O = [[(1, 0), (3, 0), (4, 1), (4, 5), (3, 6), (1, 6), (0, 5), (0, 1), (1, 0)]]
P = [[(0, 6), (0, 0), (3, 0), (4, 1), (4, 2), (3, 3), (0, 3)]]
GAP_TB = [(1.8, -1, 2.2, 0.6), (1.8, 5.4, 2.2, 7)]
GLYPHS = {
    'A': ([[(0, 6), (0, 1), (1, 0), (3, 0), (4, 1), (4, 6)], [(0, 3.5), (4, 3.5)]], [(1.8, -1, 2.2, 0.6)]),
    'B': ([[(0, 0), (0, 6), (3, 6), (4, 5), (4, 4), (3, 3), (0, 3)], [(0, 0), (3, 0), (4, 1), (4, 2), (3, 3)]], [(0.5, -1, 0.85, 0.6), (0.5, 5.4, 0.85, 7)]),
    'C': ([[(4, 0), (1, 0), (0, 1), (0, 5), (1, 6), (4, 6)]], []),
    'D': ([[(0, 0), (0, 6), (3, 6), (4, 5), (4, 1), (3, 0), (0, 0)]], [(0.5, -1, 0.85, 0.6), (0.5, 5.4, 0.85, 7)]),
    'E': ([[(4, 0), (0, 0), (0, 6), (4, 6)], [(0, 3), (3, 3)]], []),
    'F': ([[(4, 0), (0, 0), (0, 6)], [(0, 3), (3, 3)]], []),
    'G': ([[(4, 1), (3, 0), (1, 0), (0, 1), (0, 5), (1, 6), (3, 6), (4, 5), (4, 3.2), (2.2, 3.2)]], []),
    'H': ([[(0, 0), (0, 6)], [(4, 0), (4, 6)], [(0, 3), (4, 3)]], []),
    'I': ([[(2, 0), (2, 6)], [(0.8, 0), (3.2, 0)], [(0.8, 6), (3.2, 6)]], []),
    'J': ([[(4, 0), (4, 5), (3, 6), (1, 6), (0, 5), (0, 4)]], []),
    'K': ([[(0, 0), (0, 6)], [(4, 0), (1.2, 3), (4, 6)], [(0, 3), (1.2, 3)]], []),
    'L': ([[(0, 0), (0, 6), (4, 6)]], []),
    'M': ([[(0, 6), (0, 0), (2, 3.2), (4, 0), (4, 6)]], []),
    'N': ([[(0, 6), (0, 0), (4, 6), (4, 0)]], []),
    'O': (O, GAP_TB),
    'P': (P, [(0.5, -1, 0.85, 0.6)]),
    'Q': ([O[0], [(2.4, 4.2), (4.2, 6.2)]], [(1.8, -1, 2.2, 0.6)]),
    'R': ([P[0], [(2, 3), (4, 6)]], [(0.5, -1, 0.85, 0.6)]),
    'S': ([[(4, 1), (3, 0), (1, 0), (0, 1), (0, 2), (1, 3), (3, 3), (4, 4), (4, 5), (3, 6), (1, 6), (0, 5)]], []),
    'T': ([[(0, 0), (4, 0)], [(2, 0), (2, 6)]], []),
    'U': ([[(0, 0), (0, 5), (1, 6), (3, 6), (4, 5), (4, 0)]], [(1.8, 5.4, 2.2, 7)]),
    'V': ([[(0, 0), (2, 6), (4, 0)]], []),
    'W': ([[(0, 0), (1, 6), (2, 2.8), (3, 6), (4, 0)]], []),
    'X': ([[(0, 0), (4, 6)], [(4, 0), (0, 6)]], []),
    'Y': ([[(0, 0), (2, 3), (4, 0)], [(2, 3), (2, 6)]], []),
    'Z': ([[(0, 0), (4, 0), (0, 6), (4, 6)]], []),
    '0': (O + [[(3, 1.6), (1, 4.4)]], GAP_TB),
    '1': ([[(0.8, 1.2), (2, 0), (2, 6)], [(0.8, 6), (3.2, 6)]], []),
    '2': ([[(0, 1), (1, 0), (3, 0), (4, 1), (4, 2.2), (0, 6), (4, 6)]], []),
    '3': ([[(0, 0), (4, 0), (2, 2.6), (3, 2.6), (4, 3.6), (4, 5), (3, 6), (1, 6), (0, 5)]], []),
    '4': ([[(3, 6), (3, 0), (0, 4), (4, 4)]], [(2.6, 3.2, 3.4, 3.55)]),
    '5': ([[(4, 0), (0, 0), (0, 3), (3, 3), (4, 4), (4, 5), (3, 6), (0, 6)]], []),
    '6': ([[(3.5, 0), (1, 0), (0, 1), (0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (3, 3), (0, 3)]], [(1.8, 5.4, 2.2, 7)]),
    '7': ([[(0, 0), (4, 0), (1.5, 6)]], []),
    '8': ([[(1, 0), (3, 0), (4, 1), (4, 2), (3, 3), (1, 3), (0, 4), (0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (3, 3)],
           [(1, 3), (0, 2), (0, 1), (1, 0)]], GAP_TB),
    '9': ([[(4, 3), (1, 3), (0, 2), (0, 1), (1, 0), (3, 0), (4, 1), (4, 5), (3, 6), (0.5, 6)]], [(1.8, -1, 2.2, 0.6)]),
    '-': ([[(0.6, 3), (3.4, 3)]], []),
}
CHARS = ' ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-'


def draw_glyph(c, ch, aspect):
    strokes, cuts = GLYPHS.get(ch, ([], []))
    # the 4 x 6 grid in the cell: margins round it, strokes inside
    x0, x1, y0, y1 = 0.2, 0.8, 0.12, 0.88
    w = 0.07  # half the stroke width, in cell heights

    def G(p):
        return (x0 + (x1 - x0) * p[0] / 4.0, y0 + (y1 - y0) * p[1] / 6.0)

    for s in strokes:
        c.stroke([G(p) for p in s], w, 1, aspect)
    for (a, b, cc, d) in cuts:
        (ax, ay), (bx, by) = G((a, b)), G((cc, d))
        c.rect(ax, ay, bx, by, 0)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', '..', 'Assets', 'Carrier', 'Resources', 'Paint')
    os.makedirs(out, exist_ok=True)

    cell = 256
    logos = Canvas(cell * 4, cell * 3)
    for i, f in enumerate(LOGOS):
        logos.cell((i % 4) * cell, (i // 4) * cell, cell, cell)
        f(logos)
    w, h, grey = logos.save(os.path.join(out, 'paint_logos.png'))
    # (and half the size, white with the shapes in the alpha, for the paint screen's buttons)
    write_png(os.path.join(out, 'paint_logos_ui.png'), *halve(w, h, grey), rgba=True)

    cw, ch = 96, 128
    font = Canvas(cw * 8, ch * 5)
    for code, c in enumerate(CHARS):
        font.cell((code % 8) * cw, (code // 8) * ch, cw, ch)
        draw_glyph(font, c, cw / ch)
    font.save(os.path.join(out, 'paint_font.png'))
    print('wrote', os.path.abspath(out))


if __name__ == '__main__':
    main()
