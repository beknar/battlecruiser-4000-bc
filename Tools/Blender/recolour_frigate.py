"""Recolour an AI-made frigate's texture into the starting frigate's paint scheme, and write a copy
of the GLB with the new texture.

Run:  python recolour_frigate.py <reference.glb> <model.glb> <out.glb> [--method luma|lab] [--png out.png]
(needs Pillow and numpy)

The Tripo frigates are one mesh with one 4096 x 4096 colour atlas. The new frigates came out
saturated blue and green; the starting frigate is naval grey and white over dark slate, with red
and yellow trim. The panel detail and shading of each model are kept; only the colours change:

- luma: each texel's brightness is remapped so the model's brightness spread matches the starting
  frigate's (histogram matching), then given the starting frigate's colour for that brightness
  (the average of its untrimmed texels that bright): light panels go grey-white, dark ones slate.
- lab:  the model's colours are shifted and scaled to the starting frigate's average and spread in
  Lab colour space (Reinhard's transfer).

- scheme (and scheme-dark): the starting frigate's paint applied part by part (see
  recolour_scheme); --split a,b sets where the hull goes from slate (darker than a times the hull's
  middle brightness) to light grey (brighter than b times it).

With luma and lab, strongly coloured trim (the green lights and stripes) becomes the starting frigate's
red trim, at the brightness the remap gave it. The empty parts of the atlas are left alone.
"""
import io, json, struct, sys
import numpy as np
from PIL import Image


# ------------------------------------------------------------------ GLB in and out

def read_glb(path):
    b = open(path, "rb").read()
    assert b[:4] == b"glTF"
    off, js, binc = 12, None, None
    while off < len(b):
        clen, ctype = struct.unpack_from("<I4s", b, off)
        data = b[off + 8: off + 8 + clen]
        if ctype == b"JSON":
            js = json.loads(data)
        elif ctype == b"BIN\x00":
            binc = data
        off += 8 + clen
    return js, binc


def image_of(js, binc, i=0):
    bv = js["bufferViews"][js["images"][i]["bufferView"]]
    o = bv.get("byteOffset", 0)
    return Image.open(io.BytesIO(binc[o:o + bv["byteLength"]])).convert("RGB")


def write_glb(path, js, binc, new_image_bytes, mime, i=0):
    """The GLB with image i replaced: every buffer view packed again in order (4-byte aligned)."""
    img_view = js["images"][i]["bufferView"]
    out = bytearray()
    for vi, bv in enumerate(js["bufferViews"]):
        o = bv.get("byteOffset", 0)
        data = new_image_bytes if vi == img_view else binc[o:o + bv["byteLength"]]
        while len(out) % 4:
            out.append(0)
        bv["byteOffset"] = len(out)
        bv["byteLength"] = len(data)
        out += data
    while len(out) % 4:
        out.append(0)
    js["buffers"][0]["byteLength"] = len(out)
    js["images"][i]["mimeType"] = mime
    j = json.dumps(js, separators=(",", ":")).encode()
    while len(j) % 4:
        j += b" "
    total = 12 + 8 + len(j) + 8 + len(out)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(out), b"BIN\x00") + bytes(out))


# ------------------------------------------------------------------ colour

def luma(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def hsv(a):
    """Hue (degrees) and saturation of an (..., 3) array of 0..1 floats."""
    mx, mn = a.max(-1), a.min(-1)
    d = mx - mn
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx)
    dd = np.maximum(d, 1e-6)
    h = np.where(mx == r, ((g - b) / dd) % 6, h)
    h = np.where(mx == g, (b - r) / dd + 2, h)
    h = np.where(mx == b, (r - g) / dd + 4, h)
    return h * 60, s


def used(a):
    """Texels the mesh uses: the atlas's empty background is near black."""
    return a.max(-1) > 0.04


def is_trim(h, s, l):
    """Strongly coloured texels: the green lights and stripes on the new models."""
    return (s > 0.45) & (h > 70) & (h < 175) & (l > 0.12)


def ref_palette(ref):
    """The starting frigate's colour for each of 256 brightness levels (untrimmed texels), its red
    trim colour, and its brightness values (for histogram matching)."""
    m = used(ref)
    l = luma(ref)
    h, s = hsv(ref)
    plain = m & (s < 0.35)
    lv = np.clip((l[plain] * 255).astype(int), 0, 255)
    cols = ref[plain]
    lut = np.zeros((256, 3))
    cnt = np.bincount(lv, minlength=256)
    for c in range(3):
        lut[:, c] = np.bincount(lv, weights=cols[:, c], minlength=256)
    have = cnt > 20
    idx = np.arange(256)
    for c in range(3):
        lut[:, c] = np.interp(idx, idx[have], lut[have, c] / cnt[have])
    red = m & (s > 0.5) & ((h < 15) | (h > 345)) & (l > 0.15)
    trim = ref[red].mean(0) if red.sum() > 100 else np.array([0.7, 0.12, 0.1])
    return lut, trim, np.sort(l[m])


def match_luma(l, ref_sorted, m):
    """Each used texel's brightness moved to the same rank in the starting frigate's spread."""
    src = l[m]
    order = np.argsort(src)
    ranks = np.empty_like(order)
    ranks[order] = np.arange(len(src))
    q = ranks / max(1, len(src) - 1)
    out = l.copy()
    out[m] = np.interp(q, np.linspace(0, 1, len(ref_sorted)), ref_sorted)
    return out


def recolour_luma(img, ref):
    lut, trim, ref_l = ref_palette(ref)
    m = used(img)
    l = luma(img)
    h, s = hsv(img)
    trim_mask = is_trim(h, s, l) & m
    l2 = match_luma(l, ref_l, m)
    out = img.copy()
    li = np.clip((l2 * 255).astype(int), 0, 255)
    out[m] = lut[li[m]]
    # the trim: the starting frigate's red, as bright as the remap made the texel
    k = (l2[trim_mask] / max(luma(trim[None])[0], 1e-3))[:, None]
    out[trim_mask] = np.clip(trim[None] * np.clip(k, 0.6, 1.4), 0, 1)
    return out


def to_lab(a):
    a = np.where(a > 0.04045, ((a + 0.055) / 1.055) ** 2.4, a / 12.92)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = a @ M.T / np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def from_lab(lab):
    fy = (lab[..., 0] + 16) / 116
    fx, fz = fy + lab[..., 1] / 500, fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f ** 3 > 0.008856, f ** 3, (f - 16 / 116) / 7.787) * np.array([0.9505, 1.0, 1.089])
    M = np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]])
    a = np.clip(xyz @ M.T, 0, 1)
    return np.where(a > 0.0031308, 1.055 * a ** (1 / 2.4) - 0.055, 12.92 * a)


def recolour_lab(img, ref):
    _, trim, _ = ref_palette(ref)
    m, mr = used(img), used(ref)
    h, s = hsv(img)
    l = luma(img)
    trim_mask = is_trim(h, s, l) & m
    a, b = to_lab(img[m & ~trim_mask]), to_lab(ref[mr])
    a2 = (a - a.mean(0)) / np.maximum(a.std(0), 1e-3) * b.std(0) + b.mean(0)
    out = img.copy()
    out[m & ~trim_mask] = from_lab(a2)
    out[trim_mask] = np.clip(trim[None] * np.clip((l[trim_mask] / 0.5)[:, None], 0.6, 1.4), 0, 1)
    return out


def ref_tones(ref):
    """The starting frigate's light hull grey, its dark slate panels and its red trim."""
    m = used(ref)
    l = luma(ref)
    h, s = hsv(ref)
    plain = m & (s < 0.3)
    light = ref[plain & (l > 0.55) & (l < 0.9)].mean(0)
    dark = ref[plain & (l > 0.12) & (l < 0.32)].mean(0)
    _, trim, _ = ref_palette(ref)
    return light, dark, trim


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def recolour_scheme(img, ref, accent="red", split=(0.55, 1.0)):
    """The starting frigate's paint, part by part: the model's main colour (its blue hull) becomes
    the light hull grey, darkening to the slate panels where the model is darker; its accent colour
    (green) becomes the red trim, or with accent="dark" the slate panels; greys stay grey, brought
    to the starting frigate's cooler tone. The model's own shading and panel lines are kept as
    brightness relative to each colour's middle."""
    light, dark, trim = ref_tones(ref)
    m = used(img)
    l = luma(img)
    h, s = hsv(img)
    body = m & (s > 0.25) & (h >= 175) & (h <= 270)
    acc = m & is_trim(h, s, l)
    grey = m & ~body & ~acc
    out = img.copy()
    # the hull: brightness relative to the hull's middle sets light grey or slate, and shades it
    rel = l / max(np.median(l[body]), 1e-3)
    k = smoothstep(split[0], split[1], rel[body])[:, None]
    tone = dark[None] * (1 - k) + light[None] * k
    out[body] = np.clip(tone * np.clip(rel[body], 0.35, 1.35)[:, None], 0, 1)
    # the accents
    if acc.any():
        ra = l[acc] / max(np.median(l[acc]), 1e-3)
        col = trim if accent == "red" else dark
        out[acc] = np.clip(col[None] * np.clip(ra, 0.5, 1.4)[:, None], 0, 1)
    # greys: kept, slightly cooled toward the starting frigate's light grey
    tint = light / max(luma(light[None])[0], 1e-3)
    out[grey] = np.clip(l[grey][:, None] * (0.75 + 0.25 * tint[None]), 0, 1)
    return out


def main():
    args = sys.argv[1:]
    method = "luma"
    png = None
    split = (0.55, 1.0)
    if "--split" in args:
        i = args.index("--split"); split = tuple(float(x) for x in args[i + 1].split(",")); del args[i:i + 2]
    if "--method" in args:
        i = args.index("--method"); method = args[i + 1]; del args[i:i + 2]
    if "--png" in args:
        i = args.index("--png"); png = args[i + 1]; del args[i:i + 2]
    ref_path, src, out = args
    rjs, rbin = read_glb(ref_path)
    js, binc = read_glb(src)
    ref = np.asarray(image_of(rjs, rbin).resize((1024, 1024), Image.BILINEAR), dtype=np.float32) / 255
    img = np.asarray(image_of(js, binc), dtype=np.float32) / 255
    if method.startswith("scheme"):
        new = recolour_scheme(img, ref, "dark" if method == "scheme-dark" else "red", split)
    else:
        new = (recolour_luma if method == "luma" else recolour_lab)(img, ref)
    im = Image.fromarray((np.clip(new, 0, 1) * 255 + 0.5).astype(np.uint8))
    if png:
        im.save(png)
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=92)
    write_glb(out, js, binc, buf.getvalue(), "image/jpeg")
    print("WROTE", out, method)


if __name__ == "__main__":
    main()
