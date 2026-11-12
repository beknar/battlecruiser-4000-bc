#!/usr/bin/env python3
"""
Procedural sci-fi industrial textures for Battlecruiser 4000 BC (industry: refineries, factories).

Everything here is generated from code (seeded numpy noise + simple shapes); no external
images or fonts are used, so the output is wholly project-owned.

Usage:
    python make_industry_textures.py [out_dir]

Default out_dir: <project>/Assets/Carrier/Art/Industry  (project = two folders above this script).

Per material <Name> it writes:
    T_Ind_<Name>_D.png   albedo, sRGB, RGB
    T_Ind_<Name>_N.png   tangent-space normal map, OpenGL / Unity convention (+Y = green = up / +V)
    T_Ind_<Name>_M.png   URP metallic-smoothness: R = metallic, G = B = 0, A = smoothness
    T_Ind_<Name>_E.png   emission (Windows and Pad only), sRGB

Materials / metres per repeat (the modellers UV to these):
    Panel 16 m, Dark 16 m, Paint 12 m, Hazard 4 m, Grate 6 m, Tank 12 m, Pipe 4 m, Windows 8 m,
    Pad: not tiled, 0..1 UV spans the octagonal pad top (2048x2048).

All tiled textures wrap seamlessly: every drawing op uses modulo indexing, blurs are FFT
(periodic) and noise is periodic value noise.
Image row 0 is the top of the texture (V = 1); "down" on a wall is +row, which is where drips run.
"""
import math
import os
import sys
import zlib

import numpy as np
from PIL import Image, ImageDraw

N = 1024

# ---------------------------------------------------------------------------------------------
# RNG / noise / filters
# ---------------------------------------------------------------------------------------------

def make_rng(name):
    return np.random.default_rng(zlib.crc32(name.encode("utf-8")))


def _axis(n, cells):
    u = np.arange(n) * (cells / n)
    fl = np.floor(u)
    i0 = fl.astype(np.int64) % cells
    i1 = (i0 + 1) % cells
    f = u - fl
    f = f * f * (3.0 - 2.0 * f)
    return i0, i1, f


def value_noise(rng, h, w, cy, cx):
    """Periodic value noise: cy x cx random lattice smoothly interpolated over h x w pixels."""
    cy = int(max(1, min(cy, h)))
    cx = int(max(1, min(cx, w)))
    g = rng.random((cy, cx))
    x0, x1, fx = _axis(w, cx)
    y0, y1, fy = _axis(h, cy)
    a = g[:, x0] * (1 - fx) + g[:, x1] * fx
    return a[y0] * (1 - fy)[:, None] + a[y1] * fy[:, None]


def fbm(rng, h, w, cy, cx, octaves=4, gain=0.5):
    """Periodic fractal noise normalised to 0..1."""
    tot = np.zeros((h, w))
    amp, s = 1.0, 0.0
    for o in range(octaves):
        tot += amp * value_noise(rng, h, w, cy * 2 ** o, cx * 2 ** o)
        s += amp
        amp *= gain
    lo, hi = tot.min(), tot.max()
    return (tot - lo) / (hi - lo + 1e-9)


def blur(a, sx, sy=None):
    """Periodic Gaussian blur via FFT (wraps, so it keeps tiling)."""
    if a.ndim == 3:
        return np.stack([blur(a[..., c], sx, sy) for c in range(a.shape[2])], -1)
    if sy is None:
        sy = sx
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    k = np.exp(-2.0 * np.pi ** 2 * ((sx * fx) ** 2 + (sy * fy) ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * k, s=a.shape)


def conv(a, k):
    """Periodic convolution, kernel origin at [0, 0]."""
    return np.fft.irfft2(np.fft.rfft2(a) * np.fft.rfft2(k), s=a.shape)


def drip_kernel(h, w, length, width):
    """Kernel that smears a point downward (+row) into a fading streak."""
    yy = np.arange(h)[:, None].astype(float)
    xx = np.arange(w)[None, :].astype(float)
    xx = np.minimum(xx, w - xx)
    k = np.where(yy < h * 0.6, np.exp(-yy / length), 0.0) * np.exp(-(xx ** 2) / (2 * width ** 2))
    return k / k.max()


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def norm01(a, pct=99.5):
    hi = np.percentile(a, pct)
    return np.clip(a / (hi + 1e-9), 0, 1)


def cavity(hf, sigma=3.0):
    return norm01(np.clip(blur(hf, sigma) - hf, 0, None))


def convex(hf, sigma=2.0):
    return norm01(np.clip(hf - blur(hf, sigma), 0, None))


# ---------------------------------------------------------------------------------------------
# Wrap-safe drawing helpers
# ---------------------------------------------------------------------------------------------

def _ix(arr, x0, y0, x1, y1):
    ys = np.arange(int(round(y0)), int(round(y1))) % arr.shape[0]
    xs = np.arange(int(round(x0)), int(round(x1))) % arr.shape[1]
    return ys, xs


def fill_rect(arr, x0, y0, x1, y1, val, op="set"):
    ys, xs = _ix(arr, x0, y0, x1, y1)
    if len(ys) == 0 or len(xs) == 0:
        return
    ix = np.ix_(ys, xs)
    if op == "set":
        arr[ix] = val
    elif op == "add":
        arr[ix] += val
    elif op == "max":
        arr[ix] = np.maximum(arr[ix], val)
    elif op == "min":
        arr[ix] = np.minimum(arr[ix], val)


def ring_rect(arr, x0, y0, x1, y1, t, val, op="set"):
    fill_rect(arr, x0, y0, x1, y0 + t, val, op)
    fill_rect(arr, x0, y1 - t, x1, y1, val, op)
    fill_rect(arr, x0, y0 + t, x0 + t, y1 - t, val, op)
    fill_rect(arr, x1 - t, y0 + t, x1, y1 - t, val, op)


def stamp(arr, src, x, y, op="max"):
    """Stamp src with its top-left at (x, y), wrapping."""
    h, w = src.shape[:2]
    x, y = int(round(x)), int(round(y))
    ys = np.arange(y, y + h) % arr.shape[0]
    xs = np.arange(x, x + w) % arr.shape[1]
    ix = np.ix_(ys, xs)
    if op == "max":
        arr[ix] = np.maximum(arr[ix], src)
    elif op == "add":
        arr[ix] += src
    elif op == "min":
        arr[ix] = np.minimum(arr[ix], src)
    elif op == "set":
        arr[ix] = src


def stamp_c(arr, src, cx, cy, op="max"):
    h, w = src.shape[:2]
    stamp(arr, src, cx - w / 2.0, cy - h / 2.0, op)


def dome(r):
    """(height, mask) arrays for a round bolt head / rivet of radius r px, antialiased."""
    R = int(math.ceil(r)) + 2
    yy, xx = np.mgrid[-R:R + 1, -R:R + 1].astype(float)
    d = np.sqrt(xx ** 2 + yy ** 2)
    hgt = np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))
    m = np.clip(r - d + 0.5, 0, 1)
    return hgt, m


def disk(r, soft=0.5):
    R = int(math.ceil(r)) + 2
    yy, xx = np.mgrid[-R:R + 1, -R:R + 1].astype(float)
    d = np.sqrt(xx ** 2 + yy ** 2)
    return np.clip((r - d) / (2 * soft) + 0.5, 0, 1)


def poly_mask(points, w, h, ss=4):
    img = Image.new("L", (int(w * ss), int(h * ss)), 0)
    ImageDraw.Draw(img).polygon([(x * ss, y * ss) for x, y in points], fill=255)
    img = img.resize((int(w), int(h)), Image.Resampling.BOX)
    return np.asarray(img, np.float64) / 255.0


def lines_mask(n, lines, width, ss=2):
    """Antialiased wrapped line segments [(x0,y0,x1,y1), ...] on an n x n mask."""
    img = Image.new("L", (n * ss, n * ss), 0)
    dr = ImageDraw.Draw(img)
    for (x0, y0, x1, y1) in lines:
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                dr.line([((x0 + ox) * ss, (y0 + oy) * ss), ((x1 + ox) * ss, (y1 + oy) * ss)],
                        fill=255, width=max(1, int(round(width * ss))))
    img = img.resize((n, n), Image.Resampling.BOX)
    return np.asarray(img, np.float64) / 255.0


def lerp_col(alb, col, m):
    col = np.asarray(col, np.float64)
    alb += (col - alb) * m[..., None]


# ---------------------------------------------------------------------------------------------
# Stencil font (segment style, drawn from code)
# ---------------------------------------------------------------------------------------------
#   a b c
#   d e f
#   g h i
PTS = {"a": (0, 0), "b": (.5, 0), "c": (1, 0), "d": (0, .5), "e": (.5, .5), "f": (1, .5),
       "g": (0, 1), "h": (.5, 1), "i": (1, 1)}
GLYPHS = {
    "0": "ac ci ig ga", "1": "bh ab", "2": "ac cf fd dg gi", "3": "ac ci ig ef",
    "4": "ad df ci", "5": "ca ad df fi ig", "6": "ca ag gi if fd", "7": "ac ch",
    "8": "ac ci ig ga df", "9": "fd da ac ci ig",
    "A": "ga ac ci df", "B": "ga ab bf fi ig df", "C": "ca ag gi", "D": "ga ab bf fh hg",
    "E": "ca ag gi de", "F": "ca ag de", "G": "ca ag gi if fe", "H": "ag ci df", "I": "ac bh gi",
    "K": "ag dc di", "L": "ag gi", "M": "ga ae ec ci", "N": "ga ai ic", "O": "ac ci ig ga",
    "P": "ga ac cf fd", "R": "ga ac cf fd di", "S": "ca ad df fi ig", "T": "ac bh",
    "U": "ag gi ic", "V": "ah hc", "W": "ag ge ei ic", "X": "ai cg", "Y": "ae ce eh",
    "Z": "ac cg gi", "-": "df", "/": "cg", "!": "bh",
}


def text_mask(text, height, stroke=0.16, aspect=0.6, spacing=0.3, stencil=True, gap=0.03, ss=4):
    height = int(round(height))
    gw = height * aspect
    adv = gw + spacing * height
    W = int(math.ceil(len(text) * adv - spacing * height)) + 4
    H = height + 4
    img = Image.new("L", (W * ss, H * ss), 0)
    dr = ImageDraw.Draw(img)
    t = stroke * height
    ext = -(t / 2 + gap * height) if stencil else t / 2
    for ci, ch in enumerate(text.upper()):
        segs = GLYPHS.get(ch)
        if not segs:
            continue
        ox = 2 + ci * adv
        for seg in segs.split():
            p, q = PTS[seg[0]], PTS[seg[1]]
            if ch == "!" and seg == "bh":
                q = (.5, .68)
            ax, ay = ox + t / 2 + p[0] * (gw - t), 2 + t / 2 + p[1] * (height - t)
            bx, by = ox + t / 2 + q[0] * (gw - t), 2 + t / 2 + q[1] * (height - t)
            dx, dy = bx - ax, by - ay
            L = math.hypot(dx, dy)
            ux, uy = dx / L, dy / L
            nx, ny = -uy * t / 2, ux * t / 2
            # free ends (not touching another stroke) keep a square cap
            e0 = ext if _joint(segs, seg[0]) else t / 2 * 0.0
            e1 = ext if _joint(segs, seg[1]) else t / 2 * 0.0
            pa = (ax - ux * e0, ay - uy * e0)
            pb = (bx + ux * e1, by + uy * e1)
            poly = [(pa[0] + nx, pa[1] + ny), (pb[0] + nx, pb[1] + ny),
                    (pb[0] - nx, pb[1] - ny), (pa[0] - nx, pa[1] - ny)]
            dr.polygon([(x * ss, y * ss) for x, y in poly], fill=255)
        if ch == "!":
            cx, cy = ox + gw / 2, 2 + height - t / 2
            dr.rectangle([(cx - t / 2) * ss, (cy - t / 2) * ss, (cx + t / 2) * ss, (cy + t / 2) * ss], fill=255)
    img = img.resize((W, H), Image.Resampling.BOX)
    return np.asarray(img, np.float64) / 255.0


def _joint(segs, p):
    return sum(s.count(p) for s in segs.split()) > 1


def arrow_mask(length, width, direction="right"):
    sh = width * 0.36
    hl = length * 0.42
    pts = [(0, width / 2 - sh / 2), (length - hl, width / 2 - sh / 2), (length - hl, 0),
           (length, width / 2), (length - hl, width), (length - hl, width / 2 + sh / 2),
           (0, width / 2 + sh / 2)]
    m = poly_mask(pts, length, width)
    return {"right": m, "left": m[:, ::-1], "down": np.rot90(m, -1), "up": np.rot90(m, 1)}[direction]


def stripes_mask(w, h, period, phase=0.0, flip=False):
    yy, xx = np.mgrid[0:h, 0:w].astype(float) + 0.5
    s = (xx - yy if flip else xx + yy) + phase
    t = np.mod(s, period)
    half = period / 2
    d = np.where(t < half, np.minimum(t, half - t), -np.minimum(t - half, period - t))
    return np.clip(d / math.sqrt(2) + 0.5, 0, 1)


# ---------------------------------------------------------------------------------------------
# Map encoding / saving
# ---------------------------------------------------------------------------------------------

def height_to_normal(hf, strength):
    xp, xm = np.roll(hf, -1, 1), np.roll(hf, 1, 1)
    yp, ym = np.roll(hf, -1, 0), np.roll(hf, 1, 0)
    gx = (np.roll(xp, 1, 0) + 2 * xp + np.roll(xp, -1, 0) - np.roll(xm, 1, 0) - 2 * xm - np.roll(xm, -1, 0)) / 8
    gy = (np.roll(yp, 1, 1) + 2 * yp + np.roll(yp, -1, 1) - np.roll(ym, 1, 1) - 2 * ym - np.roll(ym, -1, 1)) / 8
    # u = column (right), v = up = -row  -> n = (-dh/du, -dh/dv, 1) = (-gx, +gy, 1)
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(hf)
    L = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    return np.stack([nx / L, ny / L, nz / L], -1) * 0.5 + 0.5


def to8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)


def save_set(out, name, alb, hf, nstr, met, smo, emi=None):
    paths = []
    p = os.path.join(out, "T_Ind_%s_D.png" % name)
    Image.fromarray(to8(alb), "RGB").save(p, optimize=True); paths.append(p)
    p = os.path.join(out, "T_Ind_%s_N.png" % name)
    Image.fromarray(to8(height_to_normal(hf, nstr)), "RGB").save(p, optimize=True); paths.append(p)
    z = np.zeros_like(met)
    p = os.path.join(out, "T_Ind_%s_M.png" % name)
    Image.fromarray(to8(np.stack([met, z, z, smo], -1)), "RGBA").save(p, optimize=True); paths.append(p)
    if emi is not None:
        p = os.path.join(out, "T_Ind_%s_E.png" % name)
        Image.fromarray(to8(emi), "RGB").save(p, optimize=True); paths.append(p)
    return paths


# ---------------------------------------------------------------------------------------------
# Plating (panel layouts + mechanical details), shared by Panel / Dark / Paint
# ---------------------------------------------------------------------------------------------

def guillotine(rng, rect, snap, min_sz, stop_sz, max_aspect=4.0):
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    can_x, can_y = w >= 2 * min_sz, h >= 2 * min_sz
    if not (can_x or can_y):
        return [rect]
    forced = w > max_aspect * h or h > max_aspect * w or max(w, h) > 2.2 * stop_sz
    p_stop = float(np.clip(1.05 - (w * h) / float(stop_sz * stop_sz), 0.0, 0.9))
    if not forced and rng.random() < p_stop:
        return [rect]
    if can_x and can_y:
        axis = "x" if rng.random() < (w * w) / float(w * w + h * h) else "y"
    else:
        axis = "x" if can_x else "y"
    if axis == "x":
        c = x0 + int(rng.choice(np.arange(min_sz, w - min_sz + 1, snap)))
        return (guillotine(rng, (x0, y0, c, y1), snap, min_sz, stop_sz, max_aspect) +
                guillotine(rng, (c, y0, x1, y1), snap, min_sz, stop_sz, max_aspect))
    c = y0 + int(rng.choice(np.arange(min_sz, h - min_sz + 1, snap)))
    return (guillotine(rng, (x0, y0, x1, c), snap, min_sz, stop_sz, max_aspect) +
            guillotine(rng, (x0, c, x1, y1), snap, min_sz, stop_sz, max_aspect))


def fill_lengths(rng, total, choices, min_last):
    out = []
    while sum(out) < total:
        out.append(int(rng.choice(choices)))
    over = sum(out) - total
    out[-1] -= over
    if out[-1] < min_last and len(out) > 1:
        last = out.pop()
        out[-1] += last
    return out


class Plating:
    def __init__(self, n, rects, rng, seam=6, bevel=1.3, level_jitter=0.0):
        self.n, self.rng, self.rects = n, rng, rects
        self.base = np.zeros((n, n))
        self.pid = -np.ones((n, n), np.int32)
        sa, sb = seam // 2, seam - seam // 2
        self.sa, self.sb = sa, sb
        self.levels = []
        for i, (x0, y0, x1, y1) in enumerate(rects):
            lvl = 1.0 + level_jitter * rng.uniform(-1, 1)
            self.levels.append(lvl)
            fill_rect(self.base, x0 + sa, y0 + sa, x1 - sb, y1 - sb, lvl)
            fill_rect(self.pid, x0 + sa, y0 + sa, x1 - sb, y1 - sb, i)
        self.base = blur(self.base, bevel)
        self.seam = (self.pid < 0).astype(float)
        self.detail = np.zeros((n, n))     # hard-edged relief (lightly blurred later)
        self.bolt_h = np.zeros((n, n))     # bolt domes
        self.bolt_m = np.zeros((n, n))     # bolt masks
        self.dark = np.zeros((n, n))       # deep openings (vent slots)
        self.hatch = np.zeros((n, n))      # hatch lids (for tint)
        self.marks = []                    # (mask, colour, alpha) painted later
        self.drip_src = np.zeros((n, n))   # where grime streaks start
        self.busy = set()                  # rect indices holding a hatch / vent
        self.marked = set()                # rect indices holding a stencil

    # --- details -------------------------------------------------------------------------
    def bolt(self, x, y, r):
        hh, mm = dome(r)
        stamp_c(self.bolt_h, hh, x, y)
        stamp_c(self.bolt_m, mm, x, y)

    def bolt_line(self, x0, y0, x1, y1, spacing, r):
        L = math.hypot(x1 - x0, y1 - y0)
        k = max(1, int(round(L / spacing)))
        for i in range(k + 1):
            t = i / k
            self.bolt(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r)

    def bolt_edges(self, rect, edges, inset, spacing, r):
        x0, y0, x1, y1 = rect
        x0 += self.sa + inset; y0 += self.sa + inset; x1 -= self.sb + inset; y1 -= self.sb + inset
        if "t" in edges: self.bolt_line(x0, y0, x1, y0, spacing, r)
        if "b" in edges: self.bolt_line(x0, y1, x1, y1, spacing, r)
        if "l" in edges: self.bolt_line(x0, y0, x0, y1, spacing, r)
        if "r" in edges: self.bolt_line(x1, y0, x1, y1, spacing, r)

    def corner_bolts(self, rect, inset, r):
        x0, y0, x1, y1 = rect
        for x in (x0 + self.sa + inset, x1 - self.sb - inset):
            for y in (y0 + self.sa + inset, y1 - self.sb - inset):
                self.bolt(x, y, r)

    def hatch_at(self, x0, y0, x1, y1, groove=3, rivet_r=2.5, handle=True):
        ring_rect(self.detail, x0, y0, x1, y1, groove, -0.55, "min")
        fill_rect(self.hatch, x0, y0, x1, y1, 1.0, "max")
        fill_rect(self.detail, x0 + groove, y0 + groove, x1 - groove, y1 - groove, 0.06, "add")
        ins = groove + 7
        for x in (x0 + ins, x1 - ins):
            for y in (y0 + ins, y1 - ins):
                self.bolt(x, y, rivet_r)
        w, h = x1 - x0, y1 - y0
        if handle:
            if w >= h:
                cx, cy = (x0 + x1) / 2, y1 - groove - 12
                fill_rect(self.detail, cx - 16, cy - 4, cx + 16, cy + 4, -0.4, "min")
                fill_rect(self.dark, cx - 14, cy - 2, cx + 14, cy + 2, 0.6, "max")
            else:
                cx, cy = x1 - groove - 12, (y0 + y1) / 2
                fill_rect(self.detail, cx - 4, cy - 16, cx + 4, cy + 16, -0.4, "min")
                fill_rect(self.dark, cx - 2, cy - 14, cx + 2, cy + 14, 0.6, "max")
            # hinges on the opposite side
            for t in (0.25, 0.75):
                if w >= h:
                    hx = x0 + w * t
                    fill_rect(self.detail, hx - 9, y0 - 2, hx + 9, y0 + 7, 0.3, "max")
                else:
                    hy = y0 + h * t
                    fill_rect(self.detail, x0 - 2, hy - 9, x0 + 7, hy + 9, 0.3, "max")
        self.drip_src[int(y1) % self.n, int(x0) % self.n] += 1
        self.drip_src[int(y1) % self.n, int(x1 - 1) % self.n] += 1

    def vent_at(self, x0, y0, x1, y1, horizontal=True, slot=5, pitch=11, frame=5):
        fill_rect(self.detail, x0 - frame, y0 - frame, x1 + frame, y1 + frame, 0.18, "max")
        fill_rect(self.detail, x0, y0, x1, y1, -0.1, "min")
        if horizontal:
            y = y0 + 3
            while y + slot <= y1 - 2:
                fill_rect(self.detail, x0 + 3, y, x1 - 3, y + slot, -0.9, "min")
                fill_rect(self.dark, x0 + 3, y, x1 - 3, y + slot, 1.0, "max")
                y += pitch
        else:
            x = x0 + 3
            while x + slot <= x1 - 2:
                fill_rect(self.detail, x, y0 + 3, x + slot, y1 - 3, -0.9, "min")
                fill_rect(self.dark, x, y0 + 3, x + slot, y1 - 3, 1.0, "max")
                x += pitch
        for xx in np.linspace(x0 + 4, x1 - 4, 5):
            self.drip_src[int(y1 + frame) % self.n, int(xx) % self.n] += 0.6

    def groove(self, x0, y0, x1, y1, depth=-0.45):
        fill_rect(self.detail, x0, y0, x1, y1, depth, "min")

    def mark(self, mask, x, y, colour, alpha):
        m = np.zeros((self.n, self.n))
        stamp(m, mask, x, y)
        self.marks.append((m, colour, alpha))

    def height(self, bolt_height=0.35, detail_blur=0.8):
        return self.base + blur(self.detail, detail_blur) + self.bolt_h * bolt_height

    def inner(self, rect):
        x0, y0, x1, y1 = rect
        return x0 + self.sa, y0 + self.sa, x1 - self.sb, y1 - self.sb


def decorate(pl, rng, p_hatch=0.25, p_vent=0.2, p_bolt=0.55, bolt_r=3.0, bolt_sp=26,
             bolt_inset=9, p_groove=0.15, big=200):
    for ri, rect in enumerate(pl.rects):
        x0, y0, x1, y1 = pl.inner(rect)
        w, h = x1 - x0, y1 - y0
        mn = min(w, h)
        r = rng.random()
        used_centre = False
        if mn >= big and r < p_hatch:
            hw = int(rng.uniform(0.35, 0.6) * w) // 8 * 8
            hh = int(rng.uniform(0.35, 0.6) * h) // 8 * 8
            hx = x0 + int(rng.uniform(0.2, 0.8) * (w - hw))
            hy = y0 + int(rng.uniform(0.2, 0.8) * (h - hh))
            pl.hatch_at(hx, hy, hx + hw, hy + hh)
            used_centre = True
            pl.busy.add(ri)
        elif mn >= 96 and r < p_hatch + p_vent:
            horiz = rng.random() < 0.6
            vw = int(min(w * 0.6, rng.uniform(60, 160)))
            vh = int(min(h * 0.6, rng.uniform(40, 110)))
            vx = x0 + int(rng.uniform(0.15, 0.85) * (w - vw))
            vy = y0 + int(rng.uniform(0.15, 0.85) * (h - vh))
            pl.vent_at(vx, vy, vx + vw, vy + vh, horiz)
            used_centre = True
            pl.busy.add(ri)
        if not used_centre and mn >= 128 and rng.random() < p_groove:
            if w > h:
                gx = x0 + int(rng.uniform(0.3, 0.7) * w)
                pl.groove(gx - 1, y0, gx + 1, y1)
            else:
                gy = y0 + int(rng.uniform(0.3, 0.7) * h)
                pl.groove(x0, gy - 1, x1, gy + 1)
        if rng.random() < p_bolt:
            edges = "".join(e for e in "tblr" if rng.random() < 0.5) or "tb"
            if w > 2.5 * h:
                edges = "tb"
            elif h > 2.5 * w:
                edges = "lr"
            pl.bolt_edges(rect, edges, bolt_inset, bolt_sp, bolt_r)
        elif mn >= 64:
            pl.corner_bolts(rect, bolt_inset, bolt_r)


def place_marks(pl, rng, items, colour, alpha, min_side=150):
    """Place each mask in a random big-enough panel, near a corner."""
    cands = [r for i, r in enumerate(pl.rects)
             if min(r[2] - r[0], r[3] - r[1]) >= min_side and i not in pl.busy and i not in pl.marked]
    rng.shuffle(cands)
    for i, m in enumerate(items):
        if not cands:
            break
        x0, y0, x1, y1 = pl.inner(cands[i % len(cands)])
        mh, mw = m.shape
        if mw > (x1 - x0) - 40 or mh > (y1 - y0) - 40:
            continue
        cx = x0 + 22 if rng.random() < 0.5 else x1 - 22 - mw
        cy = y0 + 22 if rng.random() < 0.5 else y1 - 22 - mh
        pl.mark(m, cx, cy, colour, alpha)
        pl.marked.add(pl.rects.index(cands[i % len(cands)]))


def weathering(rng, n, hf, grime=0.12, cav=0.35, streak=0.06, speckle=0.03):
    """Returns a multiplicative albedo factor plus the masks used (for roughness)."""
    g = smoothstep(0.35, 0.9, fbm(rng, n, n, 3, 3, 5))
    c = cavity(hf, 3.0)
    s = fbm(rng, n, n, 2, 40, 3) * smoothstep(0.3, 0.8, fbm(rng, n, n, 4, 4, 3))
    sp = value_noise(rng, n, n, n // 2, n // 2) - 0.5
    fac = (1 - grime * g) * (1 - cav * c) * (1 - streak * s) * (1 + speckle * sp)
    return fac, g, c, s


def drips(pl_src, rng, n, length=60, width=1.6, gain=1.0):
    k = drip_kernel(n, n, length, width)
    d = conv(pl_src, k)
    br = 0.4 + 0.6 * fbm(rng, n, n, 2, 64, 3)   # vertical-streaky breakup
    return np.clip(d * gain, 0, 1) * br


# ---------------------------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------------------------

def mat_panel(out):
    rng = make_rng("Ind_Panel")
    rects = guillotine(rng, (0, 0, N, N), 32, 64, 300)
    pl = Plating(N, rects, rng, seam=6, bevel=1.3, level_jitter=0.06)
    decorate(pl, rng, p_hatch=0.22, p_vent=0.16, p_bolt=0.5)
    marks = [text_mask("A-12", 26), text_mask("07", 34), text_mask("C4", 26),
             arrow_mask(70, 30, "right"), text_mask("NO STEP", 18)]
    place_marks(pl, rng, marks, (0.18, 0.18, 0.19), 0.35)
    hf = pl.height()

    base = np.array([0.60, 0.585, 0.555])
    alb = np.empty((N, N, 3))
    pv = np.zeros((len(rects), 3))
    for i in range(len(rects)):
        r = rng.random()
        c = base * (1 + rng.normal(0, 0.03))
        if r < 0.10:
            c = np.array([0.50, 0.50, 0.49]) * (1 + rng.normal(0, 0.02))
        elif r < 0.18:
            c = np.array([0.66, 0.645, 0.60])
        pv[i] = c
    pid = pl.pid
    alb[:] = np.where((pid >= 0)[..., None], pv[np.maximum(pid, 0)], base * 0.42)
    lerp_col(alb, base * 0.85, pl.hatch * 0.25)
    lerp_col(alb, (0.06, 0.06, 0.065), pl.dark)
    for m, col, a in pl.marks:
        wear = smoothstep(0.15, 0.45, fbm(rng, N, N, 32, 32, 3))
        lerp_col(alb, col, m * a * wear)
    fac, g, c, s = weathering(rng, N, hf, grime=0.10, cav=0.45, streak=0.05, speckle=0.035)
    d = drips(pl.drip_src, rng, N, 70, 1.8)
    alb *= fac[..., None] * (1 - 0.25 * d)[..., None]
    edge = convex(hf, 2.0) * smoothstep(0.55, 0.8, fbm(rng, N, N, 64, 64, 3))
    alb += 0.08 * edge[..., None]
    alb += 0.05 * pl.bolt_m[..., None] * convex(hf, 1.5)[..., None]

    met = np.full((N, N), 0.6) + 0.04 * (fbm(rng, N, N, 4, 4, 2) - 0.5)
    met = np.where(pid >= 0, met, 0.5)
    smo = 0.45 + 0.05 * (fbm(rng, N, N, 6, 6, 3) - 0.5) - 0.12 * g - 0.2 * c - 0.08 * d - 0.05 * s
    smo = np.where(pid >= 0, smo, 0.25)
    smo = smo - 0.15 * pl.dark
    return save_set(out, "Panel", alb, hf, 3.0, met, np.clip(smo, 0.05, 1))


def mat_dark(out):
    rng = make_rng("Ind_Dark")
    rects = []
    y = 0
    for rh in fill_lengths(rng, N, [128, 160, 192, 256, 320], 96):
        shift = int(rng.integers(0, N // 32)) * 32
        x = 0
        for pw in fill_lengths(rng, N, [192, 256, 320, 384, 512], 128):
            r = (x + shift, y, x + pw + shift, y + rh)
            if rng.random() < 0.35:
                rects += guillotine(rng, r, 32, 64, 140)
            else:
                rects.append(r)
            x += pw
        y += rh
    pl = Plating(N, rects, rng, seam=8, bevel=1.6, level_jitter=0.08)
    decorate(pl, rng, p_hatch=0.25, p_vent=0.22, p_bolt=0.75, bolt_r=3.6, bolt_sp=22, bolt_inset=10)
    yellow = (0.78, 0.60, 0.10)
    # warning stencils: hazard stripe bars, triangles, a couple of codes
    stripe = stripes_mask(160, 28, 28)
    tri = poly_mask([(0, 56), (32, 0), (64, 56)], 64, 56) - np.pad(
        poly_mask([(0, 42), (24, 0), (48, 42)], 48, 42), ((10, 4), (8, 8)))
    tri = np.clip(tri, 0, 1)
    excl = np.zeros_like(tri)
    em = text_mask("!", 28, stroke=0.2)
    excl[56 - em.shape[0] - 6:56 - 6, 32 - em.shape[1] // 2:32 - em.shape[1] // 2 + em.shape[1]] = em
    tri = np.clip(tri + excl, 0, 1)
    marks = [text_mask("HV 440", 26), tri, text_mask("DANGER", 22), text_mask("E-07", 30)]
    place_marks(pl, rng, marks, yellow, 0.8)
    place_marks(pl, rng, [arrow_mask(90, 36, "up"), text_mask("X9", 30)], (0.62, 0.64, 0.66), 0.45)
    # hazard bars along a few panel edges
    stripe_masks = []
    cands = [r for r in rects if (r[2] - r[0]) >= 240]
    rng.shuffle(cands)
    for r in cands[:3]:
        x0, y0, x1, y1 = pl.inner(r)
        m = np.zeros((N, N))
        frame = np.zeros((36, 168)); frame[:] = 0.0
        frame[4:32, 4:164] = stripe
        stamp(m, frame, x0 + 30, y1 - 52)
        bg = np.zeros((N, N)); fill_rect(bg, x0 + 30, y1 - 52, x0 + 198, y1 - 16, 1.0)
        stripe_masks.append((m, bg))
    hf = pl.height(bolt_height=0.45)

    base = np.array([0.245, 0.26, 0.285])
    pv = np.array([base * (1 + rng.normal(0, 0.06)) if rng.random() > 0.15 else
                   np.array([0.19, 0.20, 0.215]) for _ in rects])
    pid = pl.pid
    alb = np.where((pid >= 0)[..., None], pv[np.maximum(pid, 0)], base * 0.4)
    lerp_col(alb, base * 1.12, pl.hatch * 0.3)
    lerp_col(alb, (0.03, 0.03, 0.035), pl.dark)
    wearn = smoothstep(0.12, 0.45, fbm(rng, N, N, 32, 32, 3))
    for m, bg in stripe_masks:
        lerp_col(alb, (0.05, 0.05, 0.05), bg * 0.85 * wearn)
        lerp_col(alb, yellow, m * 0.85 * wearn)
    for m, col, a in pl.marks:
        lerp_col(alb, col, m * a * wearn)
    fac, g, c, s = weathering(rng, N, hf, grime=0.12, cav=0.5, streak=0.06, speckle=0.04)
    d = drips(pl.drip_src, rng, N, 80, 1.8)
    alb *= fac[..., None] * (1 - 0.3 * d)[..., None]
    edge = convex(hf, 2.0) * smoothstep(0.5, 0.8, fbm(rng, N, N, 64, 64, 3))
    alb += 0.10 * edge[..., None]

    paint = np.zeros((N, N))
    for m, bg in stripe_masks:
        paint = np.maximum(paint, bg * wearn)
    for m, _, a in pl.marks:
        paint = np.maximum(paint, m * wearn)
    met = np.where(pid >= 0, 0.7, 0.55) * (1 - 0.7 * paint)
    smo = 0.40 + 0.05 * (fbm(rng, N, N, 6, 6, 3) - 0.5) - 0.12 * g - 0.2 * c - 0.08 * d
    smo = np.where(pid >= 0, smo, 0.22) - 0.15 * pl.dark + 0.03 * edge
    return save_set(out, "Dark", alb, hf, 3.2, met, np.clip(smo, 0.05, 1))


def mat_paint(out):
    rng = make_rng("Ind_Paint")
    rects = guillotine(rng, (0, 0, N, N), 64, 128, 380)
    pl = Plating(N, rects, rng, seam=8, bevel=1.8, level_jitter=0.05)
    decorate(pl, rng, p_hatch=0.25, p_vent=0.25, p_bolt=0.7, bolt_r=4.0, bolt_sp=30, bolt_inset=12, big=220)
    place_marks(pl, rng, [text_mask("MAX 40T", 24), arrow_mask(80, 34, "down"), text_mask("07", 40)],
                (0.06, 0.06, 0.06), 0.85, min_side=160)
    hf0 = pl.height(bolt_height=0.4)

    # wear: strongest along seams, hatch grooves, bolts and convex edges
    edges = np.clip(pl.seam + (pl.bolt_m > 0.5) + np.abs(blur(pl.detail, 0.8)) * 1.5, 0, 1)
    prox = norm01(blur(edges, 3.5), 99.0)
    nz = fbm(rng, N, N, 48, 48, 4)
    big = fbm(rng, N, N, 6, 6, 3)
    chip_n = smoothstep(0.42, 0.78, fbm(rng, N, N, 80, 80, 4))
    wear = prox * (0.25 + 1.1 * chip_n) * (0.55 + 0.7 * big) + 0.15 * convex(hf0, 2) * chip_n
    primer = smoothstep(0.56, 0.60, wear)
    steel = smoothstep(0.66, 0.70, wear)
    # isolated scratches/chips on panel faces
    flecks = smoothstep(0.86, 0.9, fbm(rng, N, N, 96, 96, 3)) * smoothstep(0.4, 0.7, big)
    steel = np.maximum(steel, flecks)
    primer = np.maximum(primer, steel)
    hf = hf0 - 0.10 * primer - 0.05 * steel

    base = np.array([0.84, 0.40, 0.09])
    pid = pl.pid
    pv = np.array([base * (1 + rng.normal(0, 0.035)) for _ in rects])
    alb = np.where((pid >= 0)[..., None], pv[np.maximum(pid, 0)], base * 0.45)
    lerp_col(alb, base * 0.93, pl.hatch * 0.5)
    lerp_col(alb, (0.05, 0.04, 0.035), pl.dark)
    for m, col, a in pl.marks:
        lerp_col(alb, col, m * a * smoothstep(0.2, 0.5, nz + 0.2))
    lerp_col(alb, (0.30, 0.29, 0.27), primer)
    rust = smoothstep(0.55, 0.8, fbm(rng, N, N, 24, 24, 3))
    steel_col = np.array([0.47, 0.47, 0.46])
    alb = alb * (1 - steel[..., None]) + steel[..., None] * (
        steel_col * (1 - 0.5 * rust[..., None]) + np.array([0.36, 0.20, 0.10]) * 0.5 * rust[..., None])
    fac, g, c, s = weathering(rng, N, hf, grime=0.12, cav=0.45, streak=0.05, speckle=0.03)
    soot_src = pl.drip_src.copy()
    pts = rng.integers(0, N, (40, 2))
    soot_src[pts[:, 0], pts[:, 1]] += rng.uniform(0.3, 0.8, 40)
    soot = blur(drips(soot_src, rng, N, 110, 3.2, 1.3), 1.5)
    soot = np.clip(soot + 0.35 * blur(soot, 6), 0, 1)
    alb *= fac[..., None]
    lerp_col(alb, (0.07, 0.06, 0.055), soot * 0.6)

    met = 0.05 + 0.15 * primer * 0 + 0.85 * steel * (1 - 0.6 * rust)
    smo = 0.36 + 0.04 * (fbm(rng, N, N, 6, 6, 3) - 0.5) - 0.12 * g - 0.18 * c - 0.2 * soot
    smo = smo * (1 - steel) + steel * (0.5 - 0.3 * rust)
    smo = np.where(pid >= 0, smo, 0.22) - 0.15 * pl.dark
    return save_set(out, "Paint", alb, hf, 3.0, met, np.clip(smo, 0.05, 1))


def mat_hazard(out):
    rng = make_rng("Ind_Hazard")
    period = N // 4          # 4 stripe pairs per 4 m repeat (measured along U and along V)
    st = stripes_mask(N, N, period)
    yellow = np.array([0.86, 0.66, 0.08])
    black = np.array([0.045, 0.045, 0.045])
    alb = black + (yellow - black) * st[..., None]
    alb *= (1 + 0.04 * (fbm(rng, N, N, 8, 8, 3) - 0.5))[..., None]

    # scuffs: paint worn through to steel in patches, more on the black/yellow boundary
    big = fbm(rng, N, N, 5, 5, 4)
    fine = fbm(rng, N, N, 64, 64, 3)
    bnd = 1 - np.abs(st * 2 - 1)
    w = 0.65 * big + 0.45 * fine + 0.12 * blur(bnd, 3)
    scuff = smoothstep(0.80, 0.84, w)
    # scratches
    lines = []
    for _ in range(140):
        x, y = rng.uniform(0, N, 2)
        a = rng.normal(0.0, 0.5) + (math.pi / 2 if rng.random() < 0.3 else 0)
        L = rng.uniform(20, 140)
        lines.append((x, y, x + L * math.cos(a), y + L * math.sin(a)))
    scr = lines_mask(N, lines, 1.3) * (0.5 + 0.5 * fine)
    scuff = np.clip(scuff + 0.8 * scr, 0, 1)
    steel = np.array([0.42, 0.42, 0.41])
    lerp_col(alb, steel, scuff)
    # dirt
    dirt = smoothstep(0.45, 0.95, fbm(rng, N, N, 3, 3, 5))
    smudge = smoothstep(0.55, 0.85, fbm(rng, N, N, 16, 16, 4))
    lerp_col(alb, (0.12, 0.10, 0.08), 0.35 * dirt + 0.2 * smudge * dirt)
    hf = (1 - scuff) * 0.35 + 0.15 * fbm(rng, N, N, 4, 4, 3) + 0.05 * fine
    hf = blur(hf, 0.8)
    met = 0.02 + 0.8 * scuff
    smo = 0.42 - 0.15 * dirt - 0.05 * smudge + 0.08 * scuff + 0.04 * (fine - 0.5)
    return save_set(out, "Hazard", alb, hf, 2.0, met, np.clip(smo, 0.05, 1))


def mat_grate(out):
    rng = make_rng("Ind_Grate")
    P = N // 2
    yy, xx = np.mgrid[0:N, 0:N]
    lx, ly = xx % P, yy % P
    plate_i = (xx // P) + 2 * (yy // P)
    is_grate = (plate_i == 0) | (plate_i == 3)
    seam = 3
    frame = 16
    in_plate = (lx >= seam) & (lx < P - seam) & (ly >= seam) & (ly < P - seam)
    in_inner = (lx >= frame) & (lx < P - frame) & (ly >= frame) & (ly < P - frame)
    frame_m = in_plate & ~in_inner

    hf = np.zeros((N, N))
    gaps = np.zeros((N, N))
    lug = np.zeros((N, N))
    # bar grating: bearing bars pitch 16 px (9 cm), cross bars every 64 px
    vert = plate_i == 0
    along = np.where(vert, lx, ly)
    across = np.where(vert, ly, lx)
    bar = ((along - frame) % 16) < 6
    cross = ((across - frame) % 64) < 4
    g_top = np.where(bar, 1.0, np.where(cross, 0.75, -1.2))
    gaps = (is_grate & in_inner & ~bar & ~cross).astype(float)
    # diamond tread plate: lugs on a 32 px lattice, alternating +/-45 deg
    c = 32
    cxp = (lx - frame) % c - c / 2 + 0.5
    cyp = (ly - frame) % c - c / 2 + 0.5
    par = (((lx - frame) // c) + ((ly - frame) // c)) % 2
    u = (cxp + cyp) / math.sqrt(2)
    v = (cxp - cyp) / math.sqrt(2)
    u, v = np.where(par == 0, u, v), np.where(par == 0, v, u)
    dd = (u / 11.0) ** 2 + (v / 3.2) ** 2
    lug = np.sqrt(np.clip(1 - dd, 0, 1)) * (in_inner & ~is_grate)
    hf = np.where(is_grate, g_top, 0.7 + 0.35 * lug)
    hf = np.where(in_inner, hf, np.where(frame_m, 1.15, -1.5))
    hf = blur(hf, 0.9)
    # frame bolts
    bolt_m = np.zeros((N, N))
    bh = np.zeros((N, N))
    dh, dm = dome(4.0)
    for py in range(2):
        for px in range(2):
            for t in (0.0, 0.5, 1.0):
                for (bx, by) in [(t, 0), (t, 1), (0, t), (1, t)]:
                    x = px * P + frame / 2 + 1 + bx * (P - frame - 2)
                    y = py * P + frame / 2 + 1 + by * (P - frame - 2)
                    stamp_c(bh, dh, x, y)
                    stamp_c(bolt_m, dm, x, y)
    hf = hf + 0.3 * bh
    hf += 0.03 * fbm(rng, N, N, 64, 64, 2)

    steel = np.array([0.50, 0.51, 0.50])
    zinc = fbm(rng, N, N, 128, 128, 2)
    alb = np.empty((N, N, 3))
    alb[:] = steel
    alb *= (0.94 + 0.12 * zinc)[..., None]
    # tread plate slightly darker, polished lug tops, dirt between lugs
    tread = (in_inner & ~is_grate).astype(float)
    alb *= (1 - 0.08 * tread)[..., None]
    alb += (0.10 * lug * tread)[..., None]
    dirt_low = (1 - lug) * tread * smoothstep(0.3, 0.8, fbm(rng, N, N, 8, 8, 4))
    lerp_col(alb, (0.20, 0.18, 0.15), 0.22 * dirt_low)
    # yellow safety nosing on one frame edge of each tread plate
    nose = (~is_grate) & frame_m & (ly >= P - frame)
    nose_wear = smoothstep(0.35, 0.6, fbm(rng, N, N, 48, 48, 3))
    lerp_col(alb, (0.80, 0.62, 0.08), nose * nose_wear * 0.9)
    lerp_col(alb, (0.025, 0.025, 0.03), np.clip(gaps + (hf < -0.6), 0, 1))
    fac, gr, cv, s = weathering(rng, N, hf, grime=0.08, cav=0.3, streak=0.0, speckle=0.03)
    alb *= fac[..., None]
    deep = np.clip(gaps + (blur(hf, 0.5) < -0.6), 0, 1)
    met = 0.85 * (1 - deep) * (1 - 0.9 * nose * nose_wear) * (1 - 0.15 * dirt_low)
    smo = 0.40 + 0.2 * lug * tread - 0.15 * gr - 0.15 * dirt_low - 0.1 * cv
    smo = smo * (1 - deep) + 0.08 * deep
    return save_set(out, "Grate", alb, hf, 2.5, met, np.clip(smo, 0.05, 1))


def mat_tank(out):
    rng = make_rng("Ind_Tank")
    row_h = N // 4                        # 3 m plates (12 m repeat)
    hf = 0.6 * fbm(rng, N, N, 4, 4, 3)    # slight oil-canning of plates
    bead = np.zeros((N, N))
    weld_pts = []
    yy, xx = np.mgrid[0:N, 0:N].astype(float)
    # horizontal welds
    for r in range(4):
        y = r * row_h
        dy = (yy - y + N / 2) % N - N / 2
        prof = np.exp(-(dy ** 2) / (2 * 2.8 ** 2))
        bead = np.maximum(bead, prof * (1 + 0.12 * np.sin(xx * 2 * math.pi / 5.0)))
    # vertical welds, staggered per row
    offs = [0, 192, 64, 320]
    for r in range(4):
        y0 = r * row_h
        for k in range(2):
            x = (offs[r] + k * 512 + int(rng.integers(-2, 3)) * 32) % N
            dx = (xx - x + N / 2) % N - N / 2
            rowm = ((yy - y0) % N) < row_h
            prof = np.exp(-(dx ** 2) / (2 * 2.8 ** 2)) * rowm
            bead = np.maximum(bead, prof * (1 + 0.12 * np.sin(yy * 2 * math.pi / 5.0)))
            weld_pts.append((x, y0 + row_h))
    hf = hf * 0.15 + 0.8 * bead
    # a few nozzles / reinforcing pads
    pads = np.zeros((N, N))
    for (px, py, pw, ph) in [(700, 420, 120, 64), (140, 860, 64, 64)]:
        fill_rect(pads, px, py, px + pw, py + ph, 1.0)
    pads = blur(pads, 1.5)
    hf += 0.35 * pads
    bm = np.zeros((N, N)); bhh = np.zeros((N, N))
    dh, dm = dome(3.0)
    for (px, py, pw, ph) in [(700, 420, 120, 64), (140, 860, 64, 64)]:
        for x in np.linspace(px + 8, px + pw - 8, max(2, pw // 28)):
            for y in (py + 8, py + ph - 8):
                stamp_c(bhh, dh, x, y); stamp_c(bm, dm, x, y)
    hf += 0.3 * bhh

    cream = np.array([0.86, 0.83, 0.74])
    alb = np.empty((N, N, 3)); alb[:] = cream
    alb *= (1 + 0.03 * (fbm(rng, N, N, 6, 6, 4) - 0.5))[..., None]
    lerp_col(alb, (0.70, 0.66, 0.56), smoothstep(0.2, 0.7, bead) * 0.7)
    lerp_col(alb, cream * 0.9, pads * 0.5)
    # stencilled identifiers
    ink = np.zeros((N, N))
    for txt, hpx, x, y in [("T-104", 56, 330, 150), ("LOX", 40, 760, 640), ("07", 64, 90, 520),
                           ("BC-4000", 22, 330, 214)]:
        stamp(ink, text_mask(txt, hpx), x, y)
    stamp(ink, arrow_mask(90, 34, "down"), 820, 700)
    ink *= smoothstep(0.2, 0.5, fbm(rng, N, N, 48, 48, 3))
    lerp_col(alb, (0.18, 0.19, 0.21), ink * 0.85)
    # rust drips from weld junctions and a few random points
    src = np.zeros((N, N))
    for (x, y) in weld_pts:
        src[int(y) % N, int(x) % N] += rng.uniform(0.5, 1.0)
    for _ in range(18):
        src[int(rng.integers(0, N)), int(rng.integers(0, N))] += rng.uniform(0.2, 0.6)
    for r in range(4):
        for x in rng.integers(0, N, 6):
            src[(r * row_h + 3) % N, x] += rng.uniform(0.2, 0.5)
    rust = drips(src, rng, N, 120, 1.6, 1.0)
    rust = np.clip(rust + 0.4 * blur(rust, 4), 0, 1)
    lerp_col(alb, (0.52, 0.30, 0.15), rust * 0.45)
    fac, g, c, s = weathering(rng, N, hf, grime=0.08, cav=0.25, streak=0.05, speckle=0.02)
    alb *= fac[..., None]
    alb += 0.05 * (bm * convex(hf, 1.5))[..., None]
    met = 0.08 + 0.1 * smoothstep(0.3, 0.8, bead)
    smo = 0.50 - 0.12 * g - 0.15 * rust - 0.1 * bead - 0.05 * s + 0.05 * ink
    return save_set(out, "Tank", alb, hf, 2.2, met, np.clip(smo, 0.05, 1))


def mat_pipe(out):
    rng = make_rng("Ind_Pipe")
    # brushing along U: long in x, fine in y
    br = 0.5 * value_noise(rng, N, N, 512, 3) + 0.3 * value_noise(rng, N, N, 256, 6) + \
        0.2 * value_noise(rng, N, N, 128, 12)
    br2 = value_noise(rng, N, N, 1024, 8)
    lines = []
    for _ in range(60):
        x, y = rng.uniform(0, N, 2)
        L = rng.uniform(80, 400)
        a = rng.normal(0, 0.02)
        lines.append((x, y, x + L * math.cos(a), y + L * math.sin(a)))
    scr = lines_mask(N, lines, 1.0)
    tint = fbm(rng, N, N, 2, 2, 3)
    steel = np.array([0.56, 0.59, 0.64])
    alb = np.empty((N, N, 3)); alb[:] = steel
    alb *= (0.90 + 0.14 * br + 0.05 * br2 + 0.06 * scr)[..., None]
    lerp_col(alb, (0.48, 0.55, 0.68), smoothstep(0.5, 0.9, tint) * 0.45)   # blued patches
    lerp_col(alb, (0.62, 0.57, 0.48), smoothstep(0.75, 1.0, 1 - tint) * 0.25)  # faint straw tint
    hf = 0.04 * br + 0.03 * br2 - 0.05 * scr

    xx = np.arange(N)
    xd = lambda x0: (xx - x0 + N / 2) % N - N / 2
    # clamp band (raised strap with a bolted lug) and a thin weld ring
    band_c, band_w = 640, 52
    band = (np.abs(xd(band_c)) < band_w / 2).astype(float)[None, :].repeat(N, 0)
    band = blur(band, 1.4)
    weld = np.exp(-(xd(150) ** 2) / (2 * 3.0 ** 2))[None, :].repeat(N, 0)
    weld = weld * (1 + 0.12 * np.sin(np.arange(N)[:, None] * 2 * math.pi / 6))
    hf = hf + 1.0 * band + 0.6 * weld
    groove = np.zeros((N, N))
    for gx in (band_c - band_w / 2 + 8, band_c + band_w / 2 - 8):
        fill_rect(groove, gx - 1, 0, gx + 1, N, 1.0)
    hf -= 0.25 * blur(groove, 0.7)
    lug_m = np.zeros((N, N))
    for ly in (220, 732):
        fill_rect(lug_m, band_c - 34, ly - 30, band_c + 34, ly + 30, 1.0)
    lug_m = blur(lug_m, 1.4)
    hf += 0.5 * lug_m
    bh = np.zeros((N, N)); bm = np.zeros((N, N))
    hh, mm = dome(7.0)
    for ly in (220, 732):
        for bx in (band_c - 18, band_c + 18):
            for by in (ly - 14, ly + 14):
                stamp_c(bh, hh, bx, by); stamp_c(bm, mm, bx, by)
    hf += 0.6 * bh
    band_all = np.clip(band + lug_m, 0, 1)
    band_col = np.array([0.36, 0.38, 0.41])
    lerp_col(alb, band_col, band_all * 0.85)
    lerp_col(alb, (0.40, 0.38, 0.36), smoothstep(0.2, 0.8, weld) * 0.7)
    fac, g, c, s = weathering(rng, N, hf, grime=0.10, cav=0.4, streak=0.0, speckle=0.02)
    alb *= fac[..., None]
    alb += 0.06 * (bm * convex(hf, 2))[..., None]
    met = 0.95 - 0.15 * band_all - 0.1 * g
    smo = 0.70 + 0.08 * (br - 0.5) - 0.2 * band_all - 0.15 * g - 0.15 * c - 0.15 * weld
    return save_set(out, "Pipe", alb, hf, 2.5, met, np.clip(smo, 0.05, 1))


def mat_windows(out):
    rng = make_rng("Ind_Windows")
    rows, cols = 4, 8                      # 2 m floors, 1 m window pitch (8 m repeat)
    fh, cw = N // rows, N // cols
    ww, wh = 92, 112                       # glass opening incl. frame
    fr = 7
    wall = np.array([0.20, 0.21, 0.23])
    alb = np.empty((N, N, 3)); alb[:] = wall
    emi = np.zeros((N, N, 3))
    hf = np.zeros((N, N))
    frame_m = np.zeros((N, N)); glass_m = np.zeros((N, N))
    # wall: horizontal floor seams, spandrel panels, vertical seams every 2 windows
    seam = np.zeros((N, N))
    for r in range(rows):
        y0 = r * fh
        fill_rect(seam, 0, y0 - 2, N, y0 + 2, 1.0)
        fill_rect(seam, 0, y0 + 196, N, y0 + 199, 1.0)
        fill_rect(hf, 0, y0 + 199, N, y0 + fh - 2, 0.12)   # raised spandrel band
        for c in range(0, cols, 2):
            x = c * cw + (64 if r % 2 else 0)
            fill_rect(seam, x - 1, y0, x + 2, y0 + fh, 1.0)
    hf -= 0.6 * seam
    lit_col = []
    for r in range(rows):
        for c in range(cols):
            x0 = c * cw + (cw - ww) // 2
            y0 = r * fh + 48
            x1, y1 = x0 + ww, y0 + wh
            fill_rect(hf, x0 - 4, y0 - 4, x1 + 4, y1 + 4, 0.25, "max")     # frame outer lip
            fill_rect(frame_m, x0 - 4, y0 - 4, x1 + 4, y1 + 4, 1.0)
            fill_rect(hf, x0 + fr, y0 + fr, x1 - fr, y1 - fr, -0.5)        # recessed glass
            fill_rect(frame_m, x0 + fr, y0 + fr, x1 - fr, y1 - fr, 0.0)
            fill_rect(glass_m, x0 + fr, y0 + fr, x1 - fr, y1 - fr, 1.0)
            fill_rect(hf, x0 - 6, y1 + 4, x1 + 6, y1 + 12, 0.4, "max")    # sill
            fill_rect(frame_m, x0 - 6, y1 + 4, x1 + 6, y1 + 12, 1.0)
            # mullion
            mx = (x0 + x1) // 2
            fill_rect(hf, mx - 2, y0 + fr, mx + 2, y1 - fr, 0.1)
            fill_rect(frame_m, mx - 2, y0 + fr, mx + 2, y1 - fr, 1.0)
            fill_rect(glass_m, mx - 2, y0 + fr, mx + 2, y1 - fr, 0.0)
            r_ = rng.random()
            if r_ < 0.56:
                col = np.array([1.0, 0.80, 0.55]) * rng.uniform(0.6, 1.0)
            elif r_ < 0.72:
                col = np.array([0.62, 0.80, 1.0]) * rng.uniform(0.55, 0.95)
            else:
                col = None
            gx0, gy0, gx1, gy1 = x0 + fr, y0 + fr, x1 - fr, y1 - fr
            gh, gw = gy1 - gy0, gx1 - gx0
            if col is not None:
                gy = np.linspace(1.0, 0.7, gh)[:, None]
                img = np.ones((gh, gw)) * gy
                # interior clutter silhouettes / partial blinds
                if rng.random() < 0.35:
                    bl = int(rng.uniform(0.2, 0.6) * gh)
                    img[:bl] *= 0.25
                for _ in range(int(rng.integers(0, 3))):
                    bx = int(rng.integers(0, gw - 20)); bwid = int(rng.integers(12, 34))
                    bhgt = int(rng.integers(15, 45))
                    img[gh - bhgt:, bx:bx + bwid] *= 0.55
                e = img[..., None] * col
                ys = np.arange(gy0, gy1) % N; xs = np.arange(gx0, gx1) % N
                emi[np.ix_(ys, xs)] = e
    emi *= glass_m[..., None]
    hf = blur(hf, 1.0)
    glass_col = np.array([0.06, 0.07, 0.09])
    lerp_col(alb, (0.33, 0.34, 0.36), frame_m)
    alb = alb * (1 - glass_m[..., None]) + glass_m[..., None] * (glass_col + 0.25 * emi)
    # small vents in spandrels
    vent = np.zeros((N, N))
    for r in range(rows):
        for c in range(cols):
            if rng.random() < 0.2:
                x0 = c * cw + 30; y0 = r * fh + 214
                for k in range(4):
                    fill_rect(vent, x0, y0 + k * 8, x0 + 68, y0 + k * 8 + 4, 1.0)
    hf -= 0.4 * blur(vent, 0.7)
    lerp_col(alb, (0.04, 0.04, 0.045), vent * 0.9)
    fac, g, c, s = weathering(rng, N, hf, grime=0.12, cav=0.4, streak=0.10, speckle=0.03)
    alb *= (fac * (1 - 0.6 * glass_m) + glass_m * 0.6 + 0.4 * glass_m * fac)[..., None]
    # grime streaks under the sills
    src = np.zeros((N, N))
    for r in range(rows):
        for c in range(cols):
            x0 = c * cw + (cw - ww) // 2
            for xx_ in (x0 - 4, x0 + ww + 3, x0 + ww // 2):
                src[(r * fh + 48 + wh + 13) % N, xx_ % N] += rng.uniform(0.2, 0.7)
    d = drips(src, rng, N, 40, 2.0)
    alb *= (1 - 0.35 * d)[..., None]
    met = 0.6 * (1 - glass_m) + 0.2 * frame_m
    smo = (0.40 - 0.12 * g - 0.15 * c - 0.1 * d) * (1 - glass_m) + 0.92 * glass_m + 0.05 * frame_m
    met = met * (1 - glass_m)
    return save_set(out, "Windows", alb, hf, 3.0, met, np.clip(smo, 0.05, 1), emi=emi)


def mat_pad(out):
    rng = make_rng("Ind_Pad")
    S = 2048
    A = S / 2.0                            # apothem in px (octagon flat sides touch the square edges)
    yy, xx = np.mgrid[0:S, 0:S].astype(float)
    u = (xx + 0.5 - A) / A
    v = (yy + 0.5 - A) / A                 # +v = down in the image
    r = np.sqrt(u * u + v * v)
    # regular octagon, flat sides on the square edges: d = 1 on the outline
    dirs = [(math.cos(k * math.pi / 4), math.sin(k * math.pi / 4)) for k in range(8)]
    proj = np.stack([u * c + v * s for c, s in dirs], 0)
    d_oct = proj.max(0)
    edge_k = proj.argmax(0)
    tan_c = np.array([-s for c, s in dirs]); tan_s = np.array([c for c, s in dirs])
    t_edge = u * tan_c[edge_k] + v * tan_s[edge_k]

    # --- deck tiles --------------------------------------------------------------------------
    tile = 128
    seam = 4
    lx, ly = xx % tile, yy % tile
    face = ((lx >= seam / 2) & (lx < tile - seam / 2) & (ly >= seam / 2) & (ly < tile - seam / 2)).astype(float)
    tid = (xx // tile).astype(int) + 16 * (yy // tile).astype(int)
    tv = rng.normal(0, 0.035, 256)
    tv_s = rng.random(256)
    hf = blur(face, 1.5) * 0.8
    deck = np.array([0.215, 0.22, 0.225])
    alb = np.empty((S, S, 3)); alb[:] = deck
    alb *= (1 + tv[tid])[..., None]
    speck = value_noise(rng, S, S, S // 2, S // 2)
    mott = fbm(rng, S, S, 8, 8, 5)
    alb *= (0.92 + 0.10 * speck + 0.10 * (mott - 0.5))[..., None]
    lerp_col(alb, deck * 0.6, 1 - face)
    # a few metal service plates replacing deck tiles
    svc = np.zeros((S, S))
    plates = []
    for k in np.where(tv_s < 0.06)[0]:
        tx, ty = (k % 16) * tile, (k // 16) * tile
        cxp, cyp = tx + tile / 2 - A, ty + tile / 2 - A
        if 0.35 * A < math.hypot(cxp, cyp) and max(abs(cxp), abs(cyp)) < 0.82 * A:
            fill_rect(svc, tx + 14, ty + 14, tx + tile - 14, ty + tile - 14, 1.0)
            plates.append((tx, ty))
    svcb = blur(svc, 1.2)
    hf += 0.15 * svcb
    lerp_col(alb, (0.36, 0.37, 0.38), svcb)
    dh, dm = dome(4.0)
    bh = np.zeros((S, S)); bm = np.zeros((S, S))
    for tx, ty in plates:
        for bx in (tx + 24, tx + tile - 24):
            for by in (ty + 24, ty + tile - 24):
                stamp_c(bh, dh, bx, by); stamp_c(bm, dm, bx, by)

    # --- painted markings --------------------------------------------------------------------
    def ring(r0, w):
        return np.clip((w / 2 - np.abs(r - r0)) * A + 0.5, 0, 1)

    R1, W1 = 0.56, 0.05
    R2, W2 = 0.36, 0.016
    white = np.clip(ring(R1, W1) + ring(R2, W2), 0, 1)
    num = text_mask("07", 0.36 * A, stroke=0.15, aspect=0.58, spacing=0.25, stencil=False)
    nm = np.zeros((S, S)); stamp_c(nm, num, A, A)
    white = np.clip(white + nm, 0, 1)
    # approach chevrons (pointing toward the centre from the bottom = approach side)
    yel = np.zeros((S, S))
    for k in range(3):
        cy = A + (0.66 + 0.09 * k) * A
        hw, th, hgt = 0.17 * A, 0.035 * A, 0.07 * A
        pts = [(0, hgt + th), (hw, th), (2 * hw, hgt + th), (2 * hw, hgt), (hw, 0), (0, hgt)]
        pm = poly_mask(pts, 2 * hw + 1, hgt + th + 1)
        stamp_c(yel, pm, A, cy)
    # approach line from chevrons to the circle
    fill_rect(yel, A - 0.012 * A, A + (R1 + W1 / 2 + 0.02) * A, A + 0.012 * A, A + 0.62 * A, 1.0, "max")
    traffic = np.exp(-(r / 0.45) ** 2)
    paint_wear = smoothstep(0.66, 0.82, fbm(rng, S, S, 64, 64, 3) * 0.7 + 0.18 * traffic +
                            0.2 * fbm(rng, S, S, 6, 6, 3))
    keep = 1 - paint_wear
    lerp_col(alb, (0.80, 0.80, 0.76), white * keep * 0.95)
    lerp_col(alb, (0.85, 0.62, 0.08), yel * keep * 0.95)
    hf += 0.05 * (white + yel) * keep

    # --- outer hazard band -------------------------------------------------------------------
    band_in = 0.94
    band = np.clip((d_oct - band_in) * A + 0.5, 0, 1)
    coord = (t_edge + (d_oct - 1.0)) * A
    per = 128
    tt = np.mod(coord, per)
    sd = np.where(tt < per / 2, np.minimum(tt, per / 2 - tt), -np.minimum(tt - per / 2, per - tt))
    st = np.clip(sd / math.sqrt(2) + 0.5, 0, 1)
    band_col = np.array([0.05, 0.05, 0.05]) + (np.array([0.86, 0.66, 0.08]) - 0.05) * st[..., None]
    scuff = smoothstep(0.78, 0.84, 0.6 * fbm(rng, S, S, 8, 8, 4) + 0.5 * fbm(rng, S, S, 96, 96, 3))
    band_col = band_col * (1 - scuff[..., None]) + scuff[..., None] * np.array([0.40, 0.40, 0.39])
    alb = alb * (1 - band[..., None]) + band_col * band[..., None]
    # raised steel edge strip just inside the band
    lip = np.clip((0.012 - np.abs(d_oct - (band_in - 0.006))) * A + 0.5, 0, 1)
    lerp_col(alb, (0.38, 0.39, 0.40), lip)
    hf = hf * (1 - band) + band * (0.95 - 0.1 * scuff) + 0.3 * lip
    outside = np.clip((d_oct - 1.0) * A + 0.5, 0, 1)

    # --- scorch & tyre marks -----------------------------------------------------------------
    ang = np.arctan2(v, u)
    rv = rng.random(96)
    ua = np.mod(ang, 2 * math.pi) / (2 * math.pi) * 96
    i0 = np.floor(ua).astype(int) % 96
    fa = ua - np.floor(ua)
    ray_n = rv[i0] * (1 - fa) + rv[(i0 + 1) % 96] * fa
    scorch = np.exp(-(r / 0.27) ** 2.2) * (0.7 + 0.3 * ray_n) * (0.6 + 0.4 * mott)
    scorch += 0.25 * np.exp(-(r / 0.10) ** 2)
    scorch = np.clip(scorch, 0, 1)
    tyre_lines = []
    for _ in range(9):
        a0 = rng.uniform(0, 2 * math.pi)
        rad = rng.uniform(0.15, 0.42) * A
        span = rng.uniform(0.25, 0.7)
        cx0 = A + rng.normal(0, 0.06) * A; cy0 = A + rng.normal(0, 0.06) * A
        for gap in (0.0, 0.035 * A):
            pts = [(cx0 + (rad + gap) * math.cos(a0 + span * i / 20),
                    cy0 + (rad + gap) * math.sin(a0 + span * i / 20)) for i in range(21)]
            tyre_lines.append(pts)
    img = Image.new("L", (S, S), 0)
    drw = ImageDraw.Draw(img)
    for pts in tyre_lines:
        drw.line(pts, fill=255, width=12)
    tyre = blur(np.asarray(img, np.float64) / 255.0, 2.0) * smoothstep(0.3, 0.7, fbm(rng, S, S, 32, 32, 3))
    lerp_col(alb, (0.03, 0.028, 0.026), scorch * 0.55 * (1 - band))
    lerp_col(alb, (0.05, 0.045, 0.04), tyre * 0.4 * (1 - band))

    # --- light fixtures ----------------------------------------------------------------------
    emi = np.zeros((S, S, 3))
    house_h = np.zeros((S, S)); house_m = np.zeros((S, S)); lens = np.zeros((S, S))
    amber = np.array([1.0, 0.72, 0.32])
    cyan = np.array([0.35, 0.80, 1.0])
    ring_pts = []
    for k in range(24):
        a = 2 * math.pi * (k + 0.5) / 24
        rr = (R1 + W1 / 2 + 0.035) * A
        ring_pts.append((A + rr * math.cos(a), A + rr * math.sin(a)))
    edge_pts = []
    t_half = math.tan(math.pi / 8)
    for k, (c, s) in enumerate(dirs):
        for t in (-0.5, 0.0, 0.5):
            a = 0.97 * A
            edge_pts.append((A + a * c - t * a * t_half * s, A + a * s + t * a * t_half * c))
        vang = (k + 0.5) * math.pi / 4
        vr = 0.97 * A / math.cos(math.pi / 8)
        edge_pts.append((A + vr * math.cos(vang), A + vr * math.sin(vang)))
    hsq = np.zeros((40, 40)); hsq[2:38, 2:38] = 1
    hsq = blur(np.pad(hsq, 4), 1.0)
    hci = disk(19, 1.0)
    ld = disk(10, 0.8)
    for pts, hmask, col in ((ring_pts, hsq, amber), (edge_pts, hci, cyan)):
        for (x, y) in pts:
            stamp_c(house_h, hmask, x, y)
            stamp_c(house_m, np.clip(hmask * 1.5, 0, 1), x, y)
            lm = np.zeros((S, S)); stamp_c(lm, ld, x, y)
            lens = np.maximum(lens, lm)
            emi += lm[..., None] * col
    emi = np.clip(emi + 0.25 * blur(emi, 5.0), 0, 1)   # lens + faint halo for bloom
    hf += 0.5 * house_h + 0.25 * blur(lens, 1.0)
    lerp_col(alb, (0.22, 0.23, 0.25), house_m)
    alb = alb * (1 - lens[..., None]) + lens[..., None] * (0.35 + 0.55 * emi)
    alb *= (1 - 0.35 * cavity(hf, 3.0))[..., None]
    alb += 0.05 * (bm * convex(hf, 1.5))[..., None]
    hf += 0.3 * bh
    hf *= (1 - outside)
    alb = alb * (1 - 0.5 * outside[..., None])

    white_k = (white + yel) * keep * (1 - band)
    met = 0.08 + 0.7 * svcb + 0.7 * house_m + 0.5 * band * scuff + 0.4 * lip
    met = np.clip(met * (1 - 0.8 * white_k) * (1 - lens), 0, 1)
    smo = 0.26 + 0.04 * (mott - 0.5) + 0.12 * white_k + 0.15 * svcb - 0.12 * scorch - 0.08 * tyre
    smo = smo * (1 - band) + band * (0.40 - 0.1 * scuff) + 0.2 * house_m
    smo = smo * (1 - lens) + 0.92 * lens
    return save_set(out, "Pad", alb, hf, 3.0, np.clip(met, 0, 1), np.clip(smo, 0.05, 1), emi=emi)


MATERIALS = [mat_panel, mat_dark, mat_paint, mat_hazard, mat_grate, mat_tank, mat_pipe, mat_windows, mat_pad]


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    project = os.path.normpath(os.path.join(here, "..", ".."))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(project, "Assets", "Carrier", "Art", "Industry")
    only = set(sys.argv[2:])
    os.makedirs(out, exist_ok=True)
    total = 0
    for fn in MATERIALS:
        if only and fn.__name__[4:] not in only:
            continue
        for p in fn(out):
            sz = os.path.getsize(p)
            total += sz
            print("%-28s %7.2f MB" % (os.path.basename(p), sz / 1e6))
    print("total %.2f MB -> %s" % (total / 1e6, out))


if __name__ == "__main__":
    main()
