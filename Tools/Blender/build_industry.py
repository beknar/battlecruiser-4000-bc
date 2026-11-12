"""Model five industrial structures procedurally and export them as FBX for Unity.

Run:  blender -b --python build_industry.py -- <models_dir> <data_dir> [preview_dir] [texture_dir]
          [--only Ind_RefinerySpace,Ind_LandingPad] [--samples 48]

  Ind_RefinerySpace    orbital ore refinery (~700 m), origin at its centre of mass, berth on a pier
  Ind_FactorySpace     orbital shipyard / factory (~800 m long), origin at its centre of mass, berth on a pier
  Ind_RefinerySurface  ground refinery (~265 x 245 m, 140 m stacks), origin at ground centre
  Ind_FactorySurface   ground fabrication plant (~305 x 175 m), origin at ground centre
  Ind_LandingPad       octagonal pad, 300 m across flats, deck 6 m up; meshes "Pad Deck" + "Pad Frame"

Axes (same as build_ball_turret.py): Blender +Y = forward, +Z = up; with the export flags below a
Blender point (x, y, z) lands in Unity at (x, z, y). Surface structures face their "front" (the
side meant to look at the landing pad) along Blender +Y = Unity +Z. Surface models carry a 12 m
foundation skirt below z = 0.

Every mesh is built in world space with its object at the origin (no transforms to apply).
Materials are named slots only (Ind_*); Unity matches them by name. UVs are world-space box
projections at TILE metres per repeat; lathed parts (pipes, tanks, stacks) get cylindrical UVs
(u around, v along); the pad deck top (Ind_Pad) maps its bounding square to 0..1.

Outputs: <models_dir>/Ind_*.fbx, <data_dir>/industry_hardpoints.json (Unity coordinates),
<preview_dir>/Ind_*_<view>.png (Cycles, CPU only), textured from <texture_dir> when present.
"""
import bpy, bmesh, math, json, os, sys, random
from mathutils import Vector as V, Matrix
from mathutils.bvhtree import BVHTree

# ------------------------------------------------------------------ arguments
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS, OPTS = [], {}
_i = 0
while _i < len(argv):
    if argv[_i].startswith("--"):
        OPTS[argv[_i][2:]] = argv[_i + 1] if _i + 1 < len(argv) else ""
        _i += 2
    else:
        POS.append(argv[_i])
        _i += 1
HERE = os.path.dirname(os.path.abspath(__file__))
MODELS_DIR = POS[0] if POS else os.path.join(HERE, "out")
DATA_DIR = POS[1] if len(POS) > 1 else MODELS_DIR
PREVIEW_DIR = POS[2] if len(POS) > 2 else None
TEX_DIR = POS[3] if len(POS) > 3 else None
ONLY = set(OPTS["only"].split(",")) if OPTS.get("only") else None
SAMPLES = int(OPTS.get("samples", 48))
for d in (MODELS_DIR, DATA_DIR, PREVIEW_DIR):
    if d:
        os.makedirs(d, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ------------------------------------------------------------------ materials (named slots)
D, P, PT, H, G, T, PI, W, L, GL, PAD = ("Ind_Dark", "Ind_Panel", "Ind_Paint", "Ind_Hazard", "Ind_Grate", "Ind_Tank",
                                        "Ind_Pipe", "Ind_Windows", "Ind_Light", "Ind_Glow", "Ind_Pad")
TILE = {P: 16.0, D: 16.0, PT: 12.0, H: 4.0, G: 6.0, T: 12.0, PI: 4.0, W: 8.0, L: 4.0, GL: 4.0, PAD: 300.0}
FALLBACK = {P: (0.52, 0.53, 0.55), D: (0.10, 0.11, 0.12), PT: (0.62, 0.36, 0.10), H: (0.80, 0.62, 0.05),
            G: (0.28, 0.28, 0.27), T: (0.72, 0.71, 0.66), PI: (0.45, 0.42, 0.38), W: (0.06, 0.07, 0.08),
            L: (1.0, 0.95, 0.85), GL: (1.0, 0.42, 0.08), PAD: (0.22, 0.23, 0.24)}
MATS = {}
for _n, _c in FALLBACK.items():
    _m = bpy.data.materials.new(_n)
    _m.diffuse_color = (*_c, 1.0)
    MATS[_n] = _m

SHARP = math.radians(35)


def to_unity(p):
    return [round(p[0], 3), round(p[2], 3), round(p[1], 3)]


# ------------------------------------------------------------------ mesh builder
class MB:
    """Collects closed primitives into one bmesh. Faces get box-projected world UVs (TILE metres
    per repeat for their material) unless explicit UVs are given."""

    def __init__(self, name, pad_square=None):
        self.name, self.bm, self.mats = name, bmesh.new(), []
        self.uv = self.bm.loops.layers.uv.verify()
        self.pad_square = pad_square  # (x0, y0, size) for Ind_Pad planar mapping

    def _mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def vert(self, p):
        return self.bm.verts.new(p)

    def face(self, vs, mat, want=None, uvs=None):
        if len(vs) < 3 or len(set(vs)) < len(vs):
            return None
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        if f.calc_area() < 1e-5:
            self.bm.faces.remove(f)
            return None
        f.normal_update()
        if want is not None and f.normal.dot(V(want)) < 0:
            f.normal_flip()
        f.material_index = self._mi(mat)
        if uvs is not None:
            m = dict(zip(vs, uvs))
            for lp in f.loops:
                lp[self.uv].uv = m[lp.vert]
        else:
            self._proj(f, mat)
        return f

    def _proj(self, f, mat):
        if mat == PAD and self.pad_square:
            x0, y0, s = self.pad_square
            for lp in f.loops:
                co = lp.vert.co
                lp[self.uv].uv = ((co.x - x0) / s, (co.y - y0) / s)
            return
        t = TILE.get(mat, 8.0)
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]   # vertical faces: v = world z
        for lp in f.loops:
            co = lp.vert.co
            lp[self.uv].uv = (co[a] / t, co[b] / t)

    def build(self, coll):
        bm = self.bm
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
        for f in bm.faces:
            f.smooth = True
        for e in bm.edges:
            if len(e.link_faces) != 2 or e.calc_face_angle(math.pi) > SHARP:
                e.smooth = False
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for m in self.mats:
            mesh.materials.append(MATS[m])
        obj = bpy.data.objects.new(self.name, mesh)
        coll.objects.link(obj)
        return obj


# ------------------------------------------------------------------ primitives
def basis(a, up=None):
    a = V(a).normalized()
    ref = V(up).normalized() if up is not None else V((0, 0, 1))
    if abs(a.dot(ref)) > 0.98:
        ref = V((1, 0, 0)) if abs(a.x) < 0.9 else V((0, 1, 0))
    e1 = ref.cross(a).normalized()
    e2 = a.cross(e1).normalized()
    return a, e1, e2


def obox(mb, c, ax, ay, az, hx, hy, hz, mat, side=None):
    """Oriented closed box; `side` overrides materials per face key ('-x', '+z', ...)."""
    if min(hx, hy, hz) < 5e-4:
        return
    c, ax, ay, az = V(c), V(ax), V(ay), V(az)
    Pv = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                Pv[(sx, sy, sz)] = mb.vert(c + ax * (sx * hx) + ay * (sy * hy) + az * (sz * hz))
    quads = {
        "-x": ([(-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)], -ax),
        "+x": ([(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)], ax),
        "-y": ([(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], -ay),
        "+y": ([(-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)], ay),
        "-z": ([(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)], -az),
        "+z": ([(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)], az),
    }
    for k, (q, wv) in quads.items():
        m = side.get(k, mat) if side else mat
        mb.face([Pv[t] for t in q], m, want=wv)


def box(mb, x0, x1, y0, y1, z0, z1, mat, side=None):
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    obox(mb, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1),
         (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2, mat, side)


def beam(mb, p0, p1, w, mat, h=None, up=(0, 0, 1)):
    """Box beam from p0 to p1, w wide and h (default w) deep; h lies towards `up`."""
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    if d.length < 1e-3:
        return
    ax, ay, az = basis(d, up)
    obox(mb, (p0 + p1) / 2, ax, ay, az, d.length / 2, w / 2, (h or w) / 2, mat)


def lathe(mb, o, a, prof, mat, segs=16, cap=(True, True), cap_mat=None, closed=False, phase=0.0):
    """Surface of revolution about axis `a` through `o`. prof: [(r, t[, mat])], the material of an
    entry applies to the segment that starts there. r == 0 makes an apex. closed=True joins the
    last profile point back to the first (a ring/washer). Cylindrical UVs."""
    o = V(o)
    a, e1, e2 = basis(a)
    pts = [(p[0], p[1], p[2] if len(p) > 2 else mat) for p in prof]
    if closed:
        pts = pts + [pts[0]]
    rmax = max(p[0] for p in pts)
    s = [0.0]
    for i in range(1, len(pts)):
        s.append(s[-1] + math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]))
    angs = [phase + 2 * math.pi * k / segs for k in range(segs)]
    rings = []
    for idx, (r, t, _) in enumerate(pts):
        if closed and idx == len(pts) - 1:
            rings.append(rings[0])
        elif r < 1e-4:
            rings.append([mb.vert(o + a * t)])
        else:
            rings.append([mb.vert(o + a * t + (e1 * math.cos(g) + e2 * math.sin(g)) * r) for g in angs])
    for i in range(len(pts) - 1):
        m = pts[i][2]
        tl = TILE.get(m, 8.0)
        nrep = max(1, round(2 * math.pi * rmax / tl))
        v0, v1 = s[i] / tl, s[i + 1] / tl
        R0, R1 = rings[i], rings[i + 1]
        if len(R0) == 1 and len(R1) == 1:
            continue
        cen = o + a * ((pts[i][1] + pts[i + 1][1]) / 2)
        for k in range(segs):
            j = (k + 1) % segs
            u0, u1 = k / segs * nrep, (k + 1) / segs * nrep
            if len(R0) == 1:
                vs, uv = [R0[0], R1[j], R1[k]], [((u0 + u1) / 2, v0), (u1, v1), (u0, v1)]
            elif len(R1) == 1:
                vs, uv = [R0[k], R0[j], R1[0]], [(u0, v0), (u1, v0), ((u0 + u1) / 2, v1)]
            else:
                vs, uv = [R0[k], R0[j], R1[j], R1[k]], [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            if m == PI:   # pipe texture brushing runs along U: U along the axis, V round
                uv = [(q[1], q[0]) for q in uv]
            fc = sum((v.co for v in vs), V()) / len(vs)
            mb.face(vs, m, want=fc - cen, uvs=uv)
    if not closed:
        if cap[0] and len(rings[0]) > 1:
            mb.face(list(reversed(rings[0])), cap_mat or pts[0][2], want=-a)
        if cap[1] and len(rings[-1]) > 1:
            mb.face(rings[-1], cap_mat or pts[-1][2], want=a)


def lathe_z(mb, x, y, prof, mat, segs=16, **kw):
    lathe(mb, (x, y, 0), (0, 0, 1), prof, mat, segs, **kw)


def cyl(mb, p0, p1, r, mat, segs=12):
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    if d.length < 1e-3:
        return
    lathe(mb, p0, d, [(r, 0), (r, d.length)], mat, segs)


def ring(mb, c, axis, r_in, r_out, h, mat, segs=24):
    """Washer / band of height h centred on c."""
    lathe(mb, V(c) - V(axis).normalized() * (h / 2), axis,
          [(r_in, 0), (r_out, 0), (r_out, h), (r_in, h)], mat, segs, closed=True)


def sphere(mb, c, r, mat, segs=24, rings=12):
    prof = [(r * math.sin(math.pi * i / rings), -r * math.cos(math.pi * i / rings)) for i in range(rings + 1)]
    lathe(mb, c, (0, 0, 1), prof, mat, segs)


def capsule(mb, p0, a, r, length, mat, segs=20, dome=0.5, n=4):
    """Tank along a from p0: cylinder of `length` with elliptical domes on both ends."""
    prof = [(r * math.sin(math.pi / 2 * i / n), -r * dome * math.cos(math.pi / 2 * i / n)) for i in range(n + 1)]
    prof += [(r * math.cos(math.pi / 2 * i / n), length + r * dome * math.sin(math.pi / 2 * i / n)) for i in range(n + 1)]
    lathe(mb, p0, a, prof, mat, segs)


def prism(mb, poly, o, u, v, w, d0, d1, mat, caps=(None, None), edge_mats=None):
    """Polygon (in the u-v plane at o) extruded along w from d0 to d1."""
    o, u, v, w = V(o), V(u), V(v), V(w)
    A = [mb.vert(o + u * p[0] + v * p[1] + w * d0) for p in poly]
    B = [mb.vert(o + u * p[0] + v * p[1] + w * d1) for p in poly]
    n = len(poly)
    cx, cy = sum(p[0] for p in poly) / n, sum(p[1] for p in poly) / n
    axis_pt = o + u * cx + v * cy
    for i in range(n):
        j = (i + 1) % n
        vs = [A[i], A[j], B[j], B[i]]
        fc = sum((q.co for q in vs), V()) / 4
        want = fc - axis_pt
        want -= w * want.dot(w)
        mb.face(vs, edge_mats[i] if edge_mats else mat, want=want)
    sg = 1 if d1 > d0 else -1
    mb.face(list(reversed(A)), caps[0] or mat, want=-w * sg)
    mb.face(B, caps[1] or mat, want=w * sg)


def plate(mb, quad, thick, mat, out_hint):
    """Thick quad: quad points plus an inner copy offset against out_hint."""
    q = [V(p) for p in quad]
    n = (q[1] - q[0]).cross(q[2] - q[0]).normalized()
    if n.dot(V(out_hint)) < 0:
        n = -n
    A = [mb.vert(p) for p in q]
    B = [mb.vert(p - n * thick) for p in q]
    c = sum(q, V()) / 4 - n * thick / 2
    mb.face(A, mat, want=n)
    mb.face(list(reversed(B)), mat, want=-n)
    for i in range(4):
        j = (i + 1) % 4
        vs = [A[i], A[j], B[j], B[i]]
        mb.face(vs, mat, want=sum((x.co for x in vs), V()) / 4 - c)


def truss(mb, p0, p1, w, h, bay, chord, mat, brace_mat=None, up=(0, 0, 1), brace=None, diag=True):
    """Rectangular lattice truss: 4 chords, frames at every bay, alternating face diagonals."""
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    Lg = d.length
    ax, ay, az = basis(d, up)
    cs = [ay * (w / 2) + az * (h / 2), ay * (-w / 2) + az * (h / 2), ay * (-w / 2) + az * (-h / 2), ay * (w / 2) + az * (-h / 2)]
    for c in cs:
        beam(mb, p0 + c, p1 + c, chord, mat, up=up)
    n = max(1, round(Lg / bay))
    bm2, bs = brace_mat or mat, brace or chord * 0.6
    for i in range(n + 1):
        t = ax * (Lg * i / n)
        for k in range(4):
            beam(mb, p0 + t + cs[k], p0 + t + cs[(k + 1) % 4], bs, bm2, up=ax)
    if diag:
        for i in range(n):
            t0, t1 = ax * (Lg * i / n), ax * (Lg * (i + 1) / n)
            for k in range(4):
                a, b = cs[k], cs[(k + 1) % 4]
                if (i + k) % 2:
                    a, b = b, a
                beam(mb, p0 + t0 + a, p0 + t1 + b, bs, bm2, up=(a + b))


def pipe_run(mb, pts, r, mat=PI, segs=12, joint=True, closed=False):
    pts = [V(p) for p in pts]
    if closed:
        pts = pts + [pts[0]]
    for a, b in zip(pts, pts[1:]):
        cyl(mb, a, b, r, mat, segs)
    if joint:
        for p in (pts if closed else pts[1:-1]):
            sphere(mb, p, r * 1.2, mat, segs=segs, rings=6)


def wall_box(mb, side, wall, u0, u1, z0, z1, d0, d1, mat):
    """Box on an axis-aligned wall: side '+x'/'-x'/'+y'/'-y', wall = that coordinate, u = the
    other horizontal axis, d0..d1 = distance out from the wall."""
    if side == "+x":
        box(mb, wall + d0, wall + d1, u0, u1, z0, z1, mat)
    elif side == "-x":
        box(mb, wall - d1, wall - d0, u0, u1, z0, z1, mat)
    elif side == "+y":
        box(mb, u0, u1, wall + d0, wall + d1, z0, z1, mat)
    else:
        box(mb, u0, u1, wall - d1, wall - d0, z0, z1, mat)


def wall_pt(side, wall, u, z, d):
    s = 1 if side[0] == "+" else -1
    return V((wall + s * d, u, z)) if side[1] == "x" else V((u, wall + s * d, z))


def bands(mb, x0, x1, y0, y1, sides, zlist, margin=2.0, d=0.3, mat=W):
    """Window bands (or any strip) on the vertical faces of an axis-aligned block."""
    for s in sides:
        for (z0, z1) in zlist:
            if s in ("+x", "-x"):
                wall_box(mb, s, x1 if s == "+x" else x0, y0 + margin, y1 - margin, z0, z1, 0, d, mat)
            else:
                wall_box(mb, s, y1 if s == "+y" else y0, x0 + margin, x1 - margin, z0, z1, 0, d, mat)


def trim(mb, x0, x1, y0, y1, z0, z1, mat=H, d=0.35):
    """Strip all round an axis-aligned block (hazard edge / paint band)."""
    box(mb, x0 - d, x1 + d, y0 - d, y0, z0, z1, mat)
    box(mb, x0 - d, x1 + d, y1, y1 + d, z0, z1, mat)
    box(mb, x0 - d, x0, y0, y1, z0, z1, mat)
    box(mb, x1, x1 + d, y0, y1, z0, z1, mat)


def vent(mb, side, wall, uc, zc, w, h):
    """Glowing furnace slot: dark surround, orange glow slot, dark hood above."""
    wall_box(mb, side, wall, uc - w / 2 - 1.2, uc + w / 2 + 1.2, zc - h / 2 - 1.2, zc + h / 2 + 1.0, 0, 0.4, D)
    wall_box(mb, side, wall, uc - w / 2, uc + w / 2, zc - h / 2, zc + h / 2, 0, 0.6, GL)
    wall_box(mb, side, wall, uc - w / 2 - 1.4, uc + w / 2 + 1.4, zc + h / 2 + 0.2, zc + h / 2 + 1.2, 0, 3.0, D)


def hangar_door(mb, side, wall, u0, u1, z0, z1, leaf=12.0):
    wall_box(mb, side, wall, u0, u1, z0, z1, 0, 0.2, D)
    wall_box(mb, side, wall, u0 - 2.5, u0, z0, z1 + 2.5, 0, 0.9, H)
    wall_box(mb, side, wall, u1, u1 + 2.5, z0, z1 + 2.5, 0, 0.9, H)
    wall_box(mb, side, wall, u0, u1, z1, z1 + 2.5, 0, 0.9, H)
    n = max(2, round((u1 - u0) / leaf))
    for i in range(n):
        a, b = u0 + (u1 - u0) * i / n + 0.35, u0 + (u1 - u0) * (i + 1) / n - 0.35
        wall_box(mb, side, wall, a, b, z0, z1, 0, 0.6, D)
        for f in (0.3, 0.62):
            zr = z0 + (z1 - z0) * f
            wall_box(mb, side, wall, a + 0.6, b - 0.6, zr, zr + 0.8, 0.6, 0.85, P)
    for i in range(n + 1):
        u = u0 + (u1 - u0) * i / n
        lamp(mb, wall_pt(side, wall, u, z1 + 3.6, 0.8), 1.0)


def lamp(mb, p, s=1.0):
    p = V(p)
    box(mb, p.x - s / 2, p.x + s / 2, p.y - s / 2, p.y + s / 2, p.z - s / 2, p.z + s / 2, L)


def dish(mb, c, axis, r, mat=P):
    """Shallow closed dish facing along axis, with a feed horn."""
    c = V(c)
    a = V(axis).normalized()
    lathe(mb, c, a, [(0, 0.0), (0.5 * r, 0.08 * r), (r, 0.32 * r), (1.04 * r, 0.29 * r), (0.5 * r, 0.0),
                     (0.14 * r, -0.08 * r), (0, -0.08 * r)], mat, 20)
    beam(mb, c, c + a * 0.6 * r, 0.12 * r, D)
    beam(mb, c - a * 0.08 * r, c - a * 0.5 * r, 0.25 * r, D)


# ------------------------------------------------------------------ hardpoint helpers
def new_hp():
    return {"navLights": [], "floods": [], "glows": []}


def nav(mb, hp, p, s=1.4):
    lamp(mb, p, s)
    hp["navLights"].append(V(p))


def flood(mb, hp, p, target):
    p = V(p)
    d = (V(target) - p).normalized()
    beam(mb, p - d * 2.2, p - d * 0.25, 2.6, D)
    beam(mb, p - d * 0.3, p, 2.1, L)
    hp["floods"].append((p, d))


def glow(hp, p):
    hp["glows"].append(V(p))


# ------------------------------------------------------------------ shared: docking pier
SHIP = (180.0, 40.0, 30.0)   # placeholder frigate box: length, beam, height
CLEAR = 40.0                 # required clearance round the parked ship


def pier(mb, hp, x_root, x_end, py, pz, side):
    """Docking pier along X from x_root to x_end at (y=py, z=pz); clamp arms reach towards
    `side` (+1/-1 in Y). Sets hp['berth'] (ship centre, pier's outward direction)."""
    sx = 1 if x_end > x_root else -1
    truss(mb, (x_root, py, pz), (x_end, py, pz), 18, 12, 16, 1.4, D, PT)
    g0, g1 = sorted((x_root, x_end - sx * 30))
    box(mb, g0, g1, py - 4.5, py + 4.5, pz - 4.5, pz + 3, P)               # pressurised gangway
    for s in ("+y", "-y"):
        wall_box(mb, s, py + (4.5 if s == "+y" else -4.5), g0 + 4, g1 - 4, pz - 2, pz + 1, 0, 0.3, W)
    xa, xb = sorted((x_root, x_end))
    box(mb, xa, xb, py - 9.6, py + 9.6, pz + 6.8, pz + 7.4, G)             # top grating deck
    for s in (1, -1):
        box(mb, xa, xb, py + s * 9.6, py + s * 10.3, pz + 6.3, pz + 7.9, H)
    for k, dy in enumerate((-5.0, 0.0, 5.0)):                               # umbilicals underneath
        cyl(mb, (x_root, py + dy, pz - 7.8), (x_end - sx * 20, py + dy, pz - 7.8), 1.4 if k == 1 else 1.0, PI, 12)
    n = int(abs(x_end - x_root) // 20)
    for i in range(1, n):
        x = x_root + sx * 20 * i
        for s in (1, -1):
            lamp(mb, (x, py + s * 10.8, pz + 7.1), 0.9)
    # pier head with control cab
    h0, h1 = sorted((x_end - sx * 32, x_end))
    box(mb, h0, h1, py - 20, py + 20, pz - 10, pz + 10, P)
    trim(mb, h0, h1, py - 20, py + 20, pz + 5.5, pz + 8.5, PT, 0.3)
    bands(mb, h0, h1, py - 20, py + 20, ("+y", "-y"), [(pz - 4, pz + 0)], margin=3)
    end_side = "+x" if sx > 0 else "-x"
    wall_box(mb, end_side, x_end, py - 17, py + 17, pz - 4, pz + 0, 0, 0.3, W)
    wall_box(mb, end_side, x_end, py - 20, py + 20, pz - 10, pz - 7, 0, 0.4, H)
    box(mb, h0 + 6, h1 - 6, py - 12, py + 12, pz + 10, pz + 18, PT)
    bands(mb, h0 + 6, h1 - 6, py - 12, py + 12, ("+x", "-x", "+y", "-y"), [(pz + 12, pz + 16)], margin=1)
    box(mb, h0 + 5, h1 - 5, py - 13, py + 13, pz + 18, pz + 19, D)
    beam(mb, ((h0 + h1) / 2, py, pz + 19), ((h0 + h1) / 2, py, pz + 34), 0.9, D)
    nav(mb, hp, ((h0 + h1) / 2, py, pz + 35))
    for s in (1, -1):
        nav(mb, hp, (x_end + sx * 0.9, py + s * 20.9, pz + 10.9))
    # clamp arms towards the berth (they stop CLEAR short of the ship's side)
    tip = 30.0
    for k in (16, 58, 100):
        x = x_end - sx * k
        root = py + side * (20 if k < 32 else 9.6)
        box(mb, x - 4, x + 4, *sorted((root, root + side * 4)), pz - 4, pz + 4, D)
        p1 = V((x, root + side * 3, pz + 2))
        p2 = V((x, py + side * (tip - 8), pz + 8))
        p3 = V((x, py + side * (tip - 1.5), pz + 1))
        beam(mb, p1, p2, 2.6, PT)
        beam(mb, p2, p3, 2.2, PT)
        sphere(mb, p2, 2.0, D, 12, 6)
        box(mb, x - 3.5, x + 3.5, *sorted((py + side * (tip - 2), py + side * tip)), pz - 3.5, pz + 3.5, H)
    by = py + side * (tip + CLEAR + SHIP[1] / 2 + 3)
    bx = x_end - sx * 30
    for k in (8, 26):
        x = x_end - sx * k
        flood(mb, hp, (x, py + side * 20.5, pz + 10.5), (bx, by, pz))
    hp["berth"] = {"position": V((bx, by, pz)), "forward": V((sx, 0, 0))}


def stack(mb, x, y, z0, h, r):
    """Smelter / furnace chimney with a glowing throat (Ind_Glow floor below the lip)."""
    lathe_z(mb, x, y, [(0, z0, D), (r + 1.5, z0, D), (r + 1.5, z0 + 3, D), (r, z0 + 6, P), (r, z0 + h - 5, H),
                       (r, z0 + h - 2, D), (r + 0.7, z0 + h - 2, D), (r + 0.7, z0 + h, D), (r - 0.9, z0 + h, D),
                       (r - 0.9, z0 + h - 4, GL), (0, z0 + h - 4, GL)], P, segs=20)


# ------------------------------------------------------------------ 1. orbital refinery
def build_refinery_space(coll):
    rng = random.Random(3)
    mb = MB("Ind_RefinerySpace")
    hp = new_hp()
    S, Y0, Y1 = 18.0, -330.0, 250.0
    # spine: lattice truss round a solid utility duct, pipe bundles down both sides
    truss(mb, (0, Y0, 0), (0, Y1, 0), 2 * S, 2 * S, 20, 2.6, D, PT, brace=1.6)
    box(mb, -7, 7, Y0, Y1, -7, 7, P)
    for y in range(int(Y0) + 10, int(Y1), 40):
        box(mb, -8.2, 8.2, y, y + 3, -8.2, 8.2, D)
    for s in (1, -1):
        for z in (-6.0, 0.0, 6.0):
            cyl(mb, (s * (S + 3), Y0 + 8, z), (s * (S + 3), Y1 - 4, z), 1.6, PI, 12)
        for y in range(int(Y0) + 20, int(Y1), 60):
            box(mb, s * (S + 1), s * (S + 5.5), y, y + 2, -9, 9, D)
    # power module at the stern, with an antenna boom
    lathe(mb, (0, Y0 - 34, 0), (0, 1, 0), [(0, 0, D), (16, 0, D), (24, 6, P), (24, 22, H), (24, 27, P),
                                           (24, 34, D), (0, 34, D)], P, segs=32)
    beam(mb, (0, Y0 - 34, 0), (0, Y0 - 66, 0), 1.6, D)
    nav(mb, hp, (0, Y0 - 67.5, 0))
    dish(mb, (0, Y0 - 20, 30), (0.2, -0.6, 1), 9)
    beam(mb, (0, Y0 - 20, 24), (0, Y0 - 20, 29), 1.5, D)
    # radiator wings: arms out to +-232 m carrying vertical radiator panels above and below
    for s in (1, -1):
        for ya in (-305.0, -262.0):
            truss(mb, (s * S, ya, 0), (s * 232, ya, 0), 8, 8, 14, 1.4, D, P)
            for k in range(5):
                xa, xb = s * (32 + k * 40), s * (32 + k * 40 + 36)
                for sz in (1, -1):
                    box(mb, xa, xb, ya - 0.6, ya + 0.6, sz * 6, sz * 46, G)
                    cyl(mb, (min(xa, xb) - 0.4, ya, sz * 46.8), (max(xa, xb) + 0.4, ya, sz * 46.8), 1.1, PI, 12)
                    box(mb, xa - s * 0.4, xa + s * 1.4, ya - 1.0, ya + 1.0, sz * 4.5, sz * 47.5, D)
            box(mb, s * 229, s * 236, ya - 5, ya + 5, -5, 5, H)
            for sz in (1, -1):
                nav(mb, hp, (s * 232.0, ya, sz * 49.0))
    # tank cluster: four spheres, four capsule tanks, struts and feed pipes to the smelters
    for sx in (1, -1):
        for sz in (1, -1):
            c = V((sx * 46, -110, sz * 46))
            sphere(mb, c, 22, T, 24, 12)
            lathe(mb, c - V((0, 0, 1.6)), (0, 0, 1), [(22.6, 0), (22.6, 3.2)], D, 24)
            for dy in (-9, 9):
                beam(mb, (sx * S, -110 + dy, sz * S), (sx * 33, -110 + dy * 0.6, sz * 33), 2.2, D)
            pipe_run(mb, [(sx * 46, -89, sz * 46), (sx * 46, -62, sz * 46), (sx * 50, -62, sz * 20),
                          (sx * 50, -27, sz * 20)], 2.2)
            lamp(mb, (sx * 46, -110, sz * 68.6), 1.2)
    for (cx, cz) in ((50, 0), (-50, 0), (0, 50), (0, -50)):
        capsule(mb, (cx, -228, cz), (0, 1, 0), 13, 70, T, 20)
        for yb in (-215, -190, -165):
            lathe(mb, (cx, yb, cz), (0, 1, 0), [(13.5, 0), (13.5, 2.5)], D, 20)
        d = V((cx, 0, cz)).normalized()
        for yb in (-210, -170):
            beam(mb, d * S + V((0, yb, 0)), d * 37.5 + V((0, yb, 0)), 2.4, D)
        if cx:
            pipe_run(mb, [(cx, -234, 0), (cx, -262, 0)], 1.8, joint=False)
    # smelter modules (4) with glowing vent slots and stacks
    for s in (1, -1):
        for yc in (5.0, 95.0):
            x0, x1 = sorted((s * S, s * 80))
            box(mb, x0, x1, yc - 34, yc + 34, -28, 28, P)
            trim(mb, x0, x1, yc - 34, yc + 34, 18, 23, PT, 0.3)
            trim(mb, x0, x1, yc - 34, yc + 34, -28, -25, H, 0.3)
            side, wall = ("+x", 80.0) if s > 0 else ("-x", -80.0)
            for zc in (-13.0, 1.0):
                for uc in (yc - 16, yc + 16):
                    vent(mb, side, wall, uc, zc, 20, 3)
            end = ("-y", yc - 34) if yc < 50 else ("+y", yc + 34)
            for uc in (s * 36, s * 62):
                vent(mb, end[0], end[1], uc, -6, 16, 3)
            box(mb, x0 + 4, x1 - 4, yc - 30, yc + 30, 28, 30, D)          # roof plating
            for (xo, yo, hh) in ((s * 40, yc - 16, 26), (s * 63, yc + 14, 32)):
                stack(mb, xo, yo, 30, hh, 6)
                glow(hp, (xo, yo, 30 + hh + 8))
            box(mb, s * 66, s * 76, yc - 28, yc - 14, 30, 36, D)             # roof plant
            lamp(mb, (s * 80.6, yc - 33, 27), 1.0)
            lamp(mb, (s * 80.6, yc + 33, 27), 1.0)
    # crusher, transfer house, conveyor tube to the smelters
    box(mb, -28, 28, 196, 252, -28, 28, PT)
    trim(mb, -28, 28, 196, 252, 22, 25, H, 0.3)
    for s in (1, -1):
        box(mb, s * 28, s * 36, 206, 226, -10, 10, D)                        # crusher drives
        cyl(mb, (s * 36, 216, 0), (s * 40, 216, 0), 8, D, 20)
        wall_box(mb, "+x" if s > 0 else "-x", s * 28, 230, 248, -20, -8, 0, 0.5, H)
    box(mb, -9, 9, 200, 224, 28, 46, P)
    lathe(mb, (0, -36, 36), (0, 1, 0), [(6, 0), (6, 248)], P, 16)
    for y in range(-30, 212, 22):
        lathe(mb, (0, y, 36), (0, 1, 0), [(7, 0), (7, 2)], D, 16)
    for y in range(-20, 200, 44):
        beam(mb, (0, y, 30), (0, y, 19), 3, D)
    box(mb, -11, 11, -52, -28, 18, 46, PT)                                   # distributor house
    for yc in (5.0, 95.0):
        for s in (1, -1):
            cyl(mb, (0, yc, 38), (s * 30, yc, 38), 3.5, P, 12)
            box(mb, s * 27, s * 33, yc - 3, yc + 3, 29, 41, D)
    dish(mb, (14, 238, 36), (0.4, 0.5, 1), 7)
    beam(mb, (14, 238, 28), (14, 238, 35), 1.4, D)
    # ore intake hopper (double-walled funnel), ribs, lip lights
    lathe(mb, (0, 0, 0), (0, 1, 0), [(0, 244, D), (26, 244, P), (26, 256, P), (62, 318, P), (100, 358, H),
                                     (102, 366, D), (97, 368, D), (93, 362, D), (58, 322, D), (22, 266, D),
                                     (0, 266, D)], P, segs=32)
    for k in range(8):
        g = math.pi / 8 + k * math.pi / 4
        e = V((math.cos(g), 0, math.sin(g)))
        beam(mb, e * 27.5 + V((0, 256, 0)), e * 63.5 + V((0, 318, 0)), 2.6, D, up=e)
        beam(mb, e * 63.5 + V((0, 318, 0)), e * 101.5 + V((0, 358, 0)), 2.6, D, up=e)
    for k in range(8):                    # inner guide vanes and a crusher cone in the throat
        g = k * math.pi / 4
        e = V((math.cos(g), 0, math.sin(g)))
        beam(mb, e * 19.5 + V((0, 270, 0)), e * 54.5 + V((0, 324, 0)), 1.6, PT, h=6, up=e)
        beam(mb, e * 54.5 + V((0, 324, 0)), e * 89 + V((0, 362, 0)), 1.6, PT, h=6, up=e)
    lathe(mb, (0, 0, 0), (0, 1, 0), [(0, 264, D), (14, 264, D), (14, 270, H), (6, 290, D), (0, 294, D)], D, 24)
    for k in range(16):
        g = k * math.pi / 8
        lamp(mb, (103.2 * math.cos(g), 362, 103.2 * math.sin(g)), 1.4)
        lamp(mb, (91 * math.cos(g + 0.2), 360, 91 * math.sin(g + 0.2)), 1.0)
    for p in ((0, 364, 104.5), (0, 364, -104.5), (104.5, 364, 0), (-104.5, 364, 0)):
        nav(mb, hp, p, 1.8)
    # crew block hanging under the spine, observation dome, antenna
    box(mb, -26, 26, 135, 205, -80, -18, P)
    bands(mb, -26, 26, 135, 205, ("+x", "-x", "+y", "-y"),
          [(-74, -70), (-66, -62), (-58, -54), (-42, -38), (-34, -30)], margin=3)
    trim(mb, -26, 26, 135, 205, -50, -46, PT, 0.35)
    lathe_z(mb, 0, 170, [(0, -92, P), (14, -90, P), (22, -85, W), (24, -80, P), (0, -80, P)], P, 24)
    beam(mb, (0, 170, -92), (0, 170, -122), 1.2, D)
    nav(mb, hp, (0, 170, -123.5))
    for s in (1, -1):
        box(mb, s * 10, s * 22, 140, 150, -18, -14, D)
    # docking pier out of the crew block towards +X, berth on the +Y side of its head
    pier(mb, hp, 26, 290, 170, -50, +1)
    # work floods aimed at the modules
    for (p, t) in (((0, -40, 47.5), (50, 5, 30)), ((0, -40, 47.5), (-50, 5, 30)),
                   ((22, 252, 30), (0, 340, 60)), ((-22, 252, 30), (0, 340, -60)),
                   ((0, -150, 20.5), (46, -110, 46)), ((0, -150, -20.5), (-46, -110, -46))):
        flood(mb, hp, p, t)
    # greebles on the spine duct
    for y in range(int(Y0) + 30, int(Y1) - 20, 26):
        if -60 < y < 140:
            continue
        w, l, h = rng.uniform(3, 6), rng.uniform(6, 14), rng.uniform(2, 4)
        x = rng.uniform(-3, 3)
        box(mb, x - w / 2, x + w / 2, y, y + l, 7, 7 + h, rng.choice((D, PT, P)))
    obj = mb.build(coll)
    return [obj], hp, {"kind": "space", "ship_side": +1}


# ------------------------------------------------------------------ 2. orbital factory / shipyard
HULL_HALF = [(0, 1), (0.55, 1), (0.9, 0.78), (1, 0.42), (1, -0.12), (0.82, -0.62), (0.45, -0.95), (0, -1)]
HULL_W, HULL_ZT, HULL_ZB = 48.0, 36.0, -44.0
HULL_ST = [(118, 1.0, 36, -44), (205, 1.0, 36, -44), (245, 0.82, 32, -40), (272, 0.55, 24, -32),
           (290, 0.24, 12, -16), (298, 0.06, 3, -5)]


def hull_ring(y):
    """Points (x, z) round the hull cross-section at station y (full ring, from the top)."""
    st = HULL_ST
    if y <= st[0][0]:
        w, zt, zb = 1.0, HULL_ZT, HULL_ZB
    else:
        for a, b in zip(st, st[1:]):
            if a[0] <= y <= b[0]:
                f = (y - a[0]) / (b[0] - a[0])
                w, zt, zb = (a[1] + f * (b[1] - a[1]), a[2] + f * (b[2] - a[2]), a[3] + f * (b[3] - a[3]))
                break
        else:
            w, zt, zb = st[-1][1:]
    half = HULL_HALF
    ring = half + [(-x, z) for (x, z) in reversed(half[1:-1])]
    return [(x * HULL_W * w, zb + (z + 1) / 2 * (zt - zb)) for (x, z) in ring]


def robot_arm(mb, base, elbow, tip):
    base, elbow, tip = V(base), V(elbow), V(tip)
    box(mb, base.x - 4, base.x + 4, base.y - 4, base.y + 4, base.z - 4, base.z + 4, D)
    sphere(mb, base + (elbow - base).normalized() * 4, 3.4, PT, 16, 8)
    beam(mb, base, elbow, 3.0, PT)
    cyl(mb, base + V((0, 0, 2.2)), elbow + V((0, 0, 2.2)), 0.6, PI, 8 + 4)
    sphere(mb, elbow, 2.4, D, 16, 8)
    wrist = elbow + (tip - elbow) * 0.82
    beam(mb, elbow, wrist, 2.3, PT)
    box(mb, wrist.x - 1.6, wrist.x + 1.6, wrist.y - 1.6, wrist.y + 1.6, wrist.z - 1.6, wrist.z + 1.6, D)
    beam(mb, wrist, tip, 0.8, D)
    sphere(mb, tip, 0.9, GL, 10, 5)           # welding spark


def build_factory_space(coll):
    rng = random.Random(8)
    mb = MB("Ind_FactorySpace")
    hp = new_hp()
    HY0, HY1 = -410.0, -130.0
    # ---- twin fabrication halls with a connector spine
    box(mb, -15, 15, HY0 + 10, HY1, -14, 14, D)
    for s in (1, -1):
        for z in (-15.5, 15.5):
            cyl(mb, (s * 7, HY0 + 12, z), (s * 7, HY1, z), 1.6, PI, 12)
    DOOR = (-300.0, -240.0)
    for s in (1, -1):
        x0, x1 = sorted((s * 15, s * 85))
        box(mb, x0, x1, HY0, HY1, -30, 30, P)
        trim(mb, x0, x1, HY0, HY1 + 0.4, 24, 27, PT, 0.3)
        box(mb, x0 + 6, x1 - 6, HY0 + 4, HY1, 30, 32.5, D)                    # roof ridge
        out, ow = ("+x", 85.0) if s > 0 else ("-x", -85.0)
        # window bands above the racks, broken by the side hangar door
        for (u0, u1) in ((HY0 + 3, DOOR[0] - 4), (DOOR[1] + 4, HY1 - 3)):
            for (z0, z1) in ((12, 15), (18, 21)):
                wall_box(mb, out, ow, u0, u1, z0, z1, 0, 0.3, W)
        hangar_door(mb, out, ow, DOOR[0], DOOR[1], -28, 16)
        hangar_door(mb, "-y", HY0, x0 + 8, x1 - 8, -26, 20)                    # rear end door
        bands(mb, x0, x1, HY0, HY1, ("-y",), [(26.5, 29.5)], margin=4)
        # vertical radiator fins on the roof
        for yf in range(int(HY0) + 22, int(HY1) - 10, 30):
            box(mb, x0 + 4, x1 - 4, yf - 0.7, yf + 0.7, 32.5, 62.8, G)
            box(mb, x0 + 3.6, x1 - 3.6, yf - 1.1, yf + 1.1, 62.8, 64.6, D)
        cyl(mb, (s * 50, HY0 + 18, 64.8), (s * 50, HY1 - 16, 64.8), 1.3, PI, 12)
        nav(mb, hp, (s * 85.8, HY0 - 0.8, 30.8))
        nav(mb, hp, (s * 81.0, HY0 + 22, 65.6))
        # part racks with containers along the outer wall (z -30..8)
        for (r0, r1) in ((HY0 + 6, DOOR[0] - 8), (DOOR[1] + 8, HY1 - 4)):
            nb = max(1, round((r1 - r0) / 13))
            step = (r1 - r0) / nb
            xin, xout = s * 85, s * 104
            for lv in (-30.0, -17.0, -4.0, 8.0):
                box(mb, xin, xout, r0, r1, lv - 0.6, lv, G)
            for i in range(nb + 1):
                y = r0 + i * step
                for xp in (s * 86, s * 103.4):
                    box(mb, xp - 0.6, xp + 0.6, y - 0.6, y + 0.6, -30.6, 8, D)
            for i in range(nb):
                ya = r0 + i * step
                for lv in (-30.0, -17.0, -4.0):
                    for (a, b) in ((86.6, 94.4), (95.2, 103)):
                        if rng.random() < 0.72:
                            box(mb, s * a, s * b, ya + 0.8, ya + step - 0.8, lv, lv + rng.choice((6.0, 6.0, 9.0)) - (0 if lv < -5 else 0),
                                rng.choice((PT, PT, T, P, H, D)))
            box(mb, xin, xout, r0 - 0.4, r1 + 0.4, 8, 8.6, D)
    # ---- bulkhead block that anchors the frame, command tower on top
    box(mb, -110, 110, -130, -108, -50, 50, P)
    trim(mb, -110, 110, -130, -108, 44, 50, PT, 0.3)
    trim(mb, -110, 110, -130, -108, -50, -46, H, 0.3)
    wall_box(mb, "+y", -108, -100, 100, 36, 40, 0, 0.3, W)
    box(mb, -22, 22, -128, -110, 50, 78, P)
    bands(mb, -22, 22, -128, -110, ("+x", "-x", "+y", "-y"), [(54, 57), (61, 64), (68, 72)], margin=2)
    box(mb, -23, 23, -129, -109, 78, 80, D)
    beam(mb, (0, -119, 80), (0, -119, 124), 1.6, D)
    nav(mb, hp, (0, -119, 125.5))
    dish(mb, (12, -119, 86), (0.5, 0.6, 1), 6)
    beam(mb, (12, -119, 80), (12, -119, 85), 1.2, D)
    # ---- open assembly frame (lattice gantry), y -108 .. 330
    FY0, FY1, X, Z = -108.0, 330.0, 90.0, 70.0
    RINGS = (-20.0, 70.0, 160.0, 250.0, 330.0)
    for (cx, cz) in ((X, Z), (-X, Z), (X, -Z), (-X, -Z), (0, Z)):
        truss(mb, (cx, FY0, cz), (cx, FY1, cz), 8, 8, 20, 1.8, D, PT)
    for yr in RINGS:
        truss(mb, (-X, yr, Z), (X, yr, Z), 8, 8, 20, 1.8, D, PT, up=(0, 1, 0))
        truss(mb, (-X, yr, -Z), (X, yr, -Z), 8, 8, 20, 1.8, D, PT, up=(0, 1, 0))
        for sx in (1, -1):
            truss(mb, (sx * X, yr, -Z), (sx * X, yr, Z), 8, 8, 20, 1.8, D, PT, up=(0, 1, 0))
            for sz in (1, -1):
                box(mb, sx * X - 6, sx * X + 6, yr - 6, yr + 6, sz * Z - 6, sz * Z + 6, PT)
                wall_box(mb, "+x" if sx > 0 else "-x", sx * (X + 6), yr - 6, yr + 6, sz * Z - 6, sz * Z - 4, 0, 0.3, H)
    for sx in (1, -1):
        for sz in (1, -1):
            nav(mb, hp, (sx * (X + 6.8), FY1 + 6.8, sz * (Z + 6.8)))
    # gantry bridges riding the top chords, with trolleys lowering plates
    for (yb, tx) in ((40.0, 12.0), (200.0, -18.0)):
        truss(mb, (-X - 4, yb, Z + 10), (X + 4, yb, Z + 10), 6, 6, 12, 1.4, PT, D, up=(0, 1, 0))
        for sx in (1, -1):
            box(mb, sx * X - 7, sx * X + 7, yb - 5, yb + 5, Z + 4, Z + 13, D)
            wall_box(mb, "+y", yb + 5, sx * X - 7, sx * X + 7, Z + 4, Z + 6, 0, 0.3, H)
        box(mb, tx - 6, tx + 6, yb - 6, yb + 6, Z + 13, Z + 18, PT)
        for dx in (-2.0, 2.0):
            beam(mb, (tx + dx, yb, Z + 13), (tx + dx, yb, HULL_ZT + 12), 0.35, D)
        box(mb, tx - 3, tx + 3, yb - 2, yb + 2, HULL_ZT + 9, HULL_ZT + 12, H)
        box(mb, tx - 10, tx + 10, yb - 6, yb + 6, HULL_ZT + 7, HULL_ZT + 8, PT)
    # half-built hull: plated bow, skeleton aft
    def mat_fn(a, i):
        return P if rng.random() < 0.22 else PT
    rings = []
    for (y, w, zt, zb) in HULL_ST:
        rings.append([mb.vert((x, y, z)) for (x, z) in hull_ring(y)])
    n = len(rings[0])
    for a in range(len(rings) - 1):
        ym = (HULL_ST[a][0] + HULL_ST[a + 1][0]) / 2
        for i in range(n):
            j = (i + 1) % n
            vs = [rings[a][i], rings[a][j], rings[a + 1][j], rings[a + 1][i]]
            fc = sum((v.co for v in vs), V()) / 4
            mb.face(vs, mat_fn(a, i), want=fc - V((0, ym, -4)))
    mb.face(list(reversed(rings[0])), D, want=(0, -1, 0))
    mb.face(rings[-1], PT, want=(0, 1, 0))
    for y in (140, 170, 200, 230, 255):          # weld seams / frames on the plating
        pts = hull_ring(y)
        for i in range(n):
            j = (i + 1) % n
            beam(mb, (pts[i][0] * 1.02, y, pts[i][1] * 1.02 + (-4 * 0.02)), (pts[j][0] * 1.02, y, pts[j][1] * 1.02 + (-4 * 0.02)), 1.0, D)
    rib_ys = [-50 + 14 * k for k in range(13)]
    for y in rib_ys:
        pts = hull_ring(y)
        for i in range(n):
            j = (i + 1) % n
            beam(mb, (pts[i][0], y, pts[i][1]), (pts[j][0], y, pts[j][1]), 1.8, D if y % 28 else PT, up=(0, 1, 0))
    for i in (0, 2, 3, 5, 7, 9, 11, 12):
        pa, pb = hull_ring(-50)[i], hull_ring(118)[i]
        beam(mb, (pa[0], -50, pa[1]), (pb[0], 118, pb[1]), 1.4, D)
    box(mb, -2.5, 2.5, -60, 118, HULL_ZB - 2.5, HULL_ZB + 1.5, PT)        # keel
    ring0 = hull_ring(0)
    for k in range(3, 9):                         # some lower plates already fitted
        for i in (4, 5, 6, 7, 8, 9):
            if rng.random() < 0.55:
                continue
            y0, y1 = 20 + (k - 3) * 14, 20 + (k - 2) * 14
            if y1 > 118:
                continue
            a, b = ring0[i], ring0[(i + 1) % n]
            q = [(a[0], y0 + 0.9, a[1]), (b[0], y0 + 0.9, b[1]), (b[0], y1 - 0.9, b[1]), (a[0], y1 - 0.9, a[1])]
            mid = V(((a[0] + b[0]) / 2, 0, (a[1] + b[1]) / 2 + 4))
            plate(mb, q, 0.8, PT if rng.random() < 0.6 else P, mid)
    for xe in (-24.0, 0.0, 24.0):                 # engine bells waiting at the stern
        lathe(mb, (xe, -50, -6), (0, -1, 0), [(5, 0, D), (9, 12, D), (8.2, 12, D), (4.2, 0.6, D)], D, 20, closed=True)
    # cradle beams from the frame to the hull
    for yr in RINGS[:4]:
        pts = hull_ring(yr)
        for sx in (1, -1):
            lo = pts[5] if sx > 0 else pts[9]
            hi = pts[2] if sx > 0 else pts[12]
            beam(mb, (sx * (X - 4), yr, -Z + 4), (lo[0], yr, lo[1]), 2.2, PT)
            beam(mb, (sx * (X - 4), yr, Z - 4), (hi[0], yr, hi[1]), 2.2, PT)
    # robotic welding arms on the frame's side members
    for yr in RINGS[:4]:
        for sx in (1, -1):
            pts = hull_ring(yr + 22)
            hwx = max(abs(p[0]) for p in pts)
            for zb, zt in ((22.0, 6.0), (-30.0, -16.0)):
                robot_arm(mb, (sx * (X - 6), yr, zb), (sx * (X - 30), yr + 10, zb + (16 if zb > 0 else 10)),
                          (sx * (hwx + 2.5), yr + 22, zt))
    # ---- docking pier towards -X from the bulkhead, berth on its -Y side
    pier(mb, hp, -110, -330, -119, 0, -1)
    # floods on the frame aimed at the hull
    for yr in (-20.0, 160.0, 330.0):
        for sx in (1, -1):
            flood(mb, hp, (sx * (X - 2), yr, Z + 7), (0, yr + 50 if yr < 300 else 260, 0))
    for sx in (1, -1):
        flood(mb, hp, (sx * 60, -107, 51), (sx * 30, 60, 0))
    obj = mb.build(coll)
    return [obj], hp, {"kind": "space", "ship_side": -1}


# ------------------------------------------------------------------ shared: ground helpers
GZ = 0.5   # yard surface height on the foundation slab


def foundation(mb, x0, x1, y0, y1):
    box(mb, x0, x1, y0, y1, -12, GZ, D)
    wall_box(mb, "+y", y1, x0, x1, -1.5, GZ - 0.1, 0, 0.3, H)            # front kerb stripe


def flood_mast(mb, hp, x, y, h, targets):
    box(mb, x - 2, x + 2, y - 2, y + 2, GZ, GZ + 1.5, D)
    beam(mb, (x, y, GZ), (x, y, h), 1.3, PT)
    box(mb, x - 3, x + 3, y - 0.6, y + 0.6, h - 0.5, h + 0.5, D)
    for t in targets:
        d = (V(t) - V((x, y, h))).normalized()
        flood(mb, hp, V((x, y, h + 1.6)) + V((d.x, d.y, 0)) * 1.5, t)
    nav(mb, hp, (x, y, h + 4.0), 1.0)


def rack_frame(mb, p0, p1, width, h, step, pipes):
    """Pipe rack from p0 to p1 (horizontal, axis-aligned): portal frames every `step`, pipes
    given as (offset across, z, r)."""
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    Lg = d.length
    ax = d / Lg
    ac = V((-ax.y, ax.x, 0))
    n = max(1, round(Lg / step))
    for i in range(n + 1):
        c = p0 + ax * (Lg * i / n)
        for s in (1, -1):
            q = c + ac * (s * width / 2)
            box(mb, q.x - 0.6, q.x + 0.6, q.y - 0.6, q.y + 0.6, GZ, h, D)
        for z in (h - 5.5, h):
            beam(mb, c - ac * (width / 2 + 0.6) + V((0, 0, z)), c + ac * (width / 2 + 0.6) + V((0, 0, z)), 1.0, PT)
    for (off, z, r) in pipes:
        cyl(mb, p0 + ac * off + V((0, 0, z + r + 0.5)), p1 + ac * off + V((0, 0, z + r + 0.5)), r, PI, 12)


# ------------------------------------------------------------------ 3. ground refinery
def build_refinery_surface(coll):
    rng = random.Random(5)
    mb = MB("Ind_RefinerySurface")
    hp = new_hp()
    foundation(mb, -132, 132, -122, 122)
    for (x0, x1, y0, y1) in ((-128, 128, 116, 117.2), (-128, -126.8, -118, 117), (126.8, 128, -118, 117)):
        box(mb, x0, x1, y0, y1, GZ, GZ + 0.12, H)
    # ---- blast furnaces with casthouses (glowing tap openings face the front / pad)
    FURN = [(-78.0, -28.0), (-28.0, -28.0)]
    for (fx, fy) in FURN:
        lathe_z(mb, fx, fy, [(0, GZ, D), (14, GZ, D), (14, 14, D), (16, 22, H), (16, 27, PT), (10.5, 72, D),
                             (10.5, 74, D), (9, 80, D), (4.5, 88, D), (4.5, 92, D), (0, 92, D)], D, segs=32)
        for z in (40.0, 56.0):
            r = 16 + (10.5 - 16) * (z - 27) / 45
            lathe_z(mb, fx, fy, [(r + 0.5, z), (r + 0.5, z + 1.6)], D, 32)
        ring(mb, (fx, fy, 74.3), (0, 0, 1), 10.6, 16, 0.6, G, 32)
        for k in range(12):
            g = k * math.pi / 6
            beam(mb, (fx + 15.6 * math.cos(g), fy + 15.6 * math.sin(g), 74.6),
                 (fx + 15.6 * math.cos(g), fy + 15.6 * math.sin(g), 75.8), 0.3, PT)
        pipe_run(mb, [(fx + 21 * math.cos(k * math.pi / 8), fy + 21 * math.sin(k * math.pi / 8), 24) for k in range(16)],
                 2.0, PI, 12, joint=False, closed=True)
        for k in range(12):
            g = k * math.pi / 6 + 0.1
            cyl(mb, (fx + 20.5 * math.cos(g), fy + 20.5 * math.sin(g), 23.5),
                (fx + 14.6 * math.cos(g), fy + 14.6 * math.sin(g), 18), 0.7, PI, 12)
            wall = V((fx + 14.1 * math.cos(g), fy + 14.1 * math.sin(g), 17.8))
            box(mb, wall.x - 0.8, wall.x + 0.8, wall.y - 0.8, wall.y + 0.8, 17.0, 18.6, GL)
        for k in range(4):                         # uptakes converging over the top
            g = math.pi / 4 + k * math.pi / 2
            ca, sa = math.cos(g), math.sin(g)
            pipe_run(mb, [(fx + 4 * ca, fy + 4 * sa, 89), (fx + 7 * ca, fy + 7 * sa, 102), (fx + 1.5 * ca, fy + 1.5 * sa, 110)], 2.2)
        sphere(mb, (fx, fy, 110), 3.6, D, 16, 8)
        pipe_run(mb, [(fx, fy, 110), (fx, fy - 34, 72), (fx, fy - 34, 50)], 3.2, PI, 16)
        lathe_z(mb, fx, fy - 34, [(0, 10, D), (3, 10, D), (8.5, 18, PT), (8.5, 44, PT), (4, 50, D), (0, 50, D)], D, 24)
        for (dx, dy) in ((6, 6), (6, -6), (-6, 6), (-6, -6)):
            beam(mb, (fx + dx, fy - 34 + dy, GZ), (fx + dx * 0.95, fy - 34 + dy * 0.95, 16), 1.2, D)
        nav(mb, hp, (fx + 7.5, fy, 102), 1.0)
        nav(mb, hp, (fx, fy, 114.2), 1.2)
        glow(hp, (fx, fy, 120))
        # casthouse in front, with a glowing tap opening, slag runner and pot
        box(mb, fx - 14, fx + 14, fy + 10, fy + 34, GZ, 16, D)
        box(mb, fx - 15, fx + 15, fy + 9, fy + 35, 16, 18, P)
        trim(mb, fx - 14, fx + 14, fy + 10, fy + 34, 12.5, 15, PT, 0.3)
        wall_box(mb, "+y", fy + 34, fx - 9, fx + 9, GZ, GZ + 8, 0, 0.5, GL)
        wall_box(mb, "+y", fy + 34, fx - 10.5, fx + 10.5, GZ + 8.2, GZ + 9.4, 0, 3.5, D)
        for s in (1, -1):
            wall_box(mb, "+x" if s > 0 else "-x", fx + s * 14, fy + 14, fy + 30, 9, 11.5, 0, 0.5, GL)
            wall_box(mb, "+x" if s > 0 else "-x", fx + s * 14, fy + 13, fy + 31, 11.7, 12.5, 0, 2.5, D)
            box(mb, fx + s * 9.5, fx + s * 10.5, fy + 34, fy + 54, GZ, GZ + 1.2, H)
        box(mb, fx - 1.4, fx + 1.4, fy + 34, fy + 52, GZ, GZ + 0.35, GL)
        lathe_z(mb, fx, fy + 56, [(0, GZ, D), (4.5, GZ, D), (4.5, 5, D), (3.8, 5, GL), (0, 5, GL)], D, 16)
        flood(mb, hp, (fx + 12, fy + 35.5, 17.5), (fx, fy + 52, GZ))
        for k in range(5):
            lamp(mb, (fx - 12 + 6 * k, fy + 34.6, 15.6), 0.8)
    # hot-blast stoves along the back with the hot-blast main to the bustle rings
    STOVES = [(-104.0, -98.0), (-82.0, -98.0), (-60.0, -98.0), (-38.0, -98.0)]
    for (sx_, sy_) in STOVES:
        lathe_z(mb, sx_, sy_, [(0, GZ, D), (7.6, GZ, D), (7.6, 3, D), (7, 4, T), (7, 50, T), (6, 55, T), (3.5, 58, T), (0, 59, T)], T, 24)
        for z in (14.0, 30.0, 46.0):
            lathe_z(mb, sx_, sy_, [(7.4, z), (7.4, z + 1.4)], D, 24)
        cyl(mb, (sx_, sy_ + 6, 30), (sx_, -84, 30), 1.8, PI, 12)
    pipe_run(mb, [(-104, -84, 30), (-10, -84, 30)], 2.6, PI, 16)
    for (fx, fy) in FURN:
        pipe_run(mb, [(fx + 18, -84, 30), (fx + 18, -40, 30), (fx + 21, fy, 24)], 2.2, PI, 12)
    # chimney stacks (tallest points), glowing throats
    for (cx, cy, h) in ((-118.0, -108.0, 140.0), (-10.0, -108.0, 128.0)):
        lathe_z(mb, cx, cy, [(0, GZ, D), (8, GZ, D), (8, 5, D), (6.2, 10, P), (4.3, h - 14, H), (4.3, h - 10, P),
                             (4.3, h - 7, H), (4.3, h - 5, D), (4.3, h - 2, D), (5, h - 2, D), (5, h, D),
                             (3.6, h, D), (3.6, h - 4, GL), (0, h - 4, GL)], P, segs=24)
        ring(mb, (cx, cy, h * 0.55), (0, 0, 1), 4.3 + 1.9 * 0.42, 4.3 + 1.9 * 0.42 + 2.2, 0.6, G, 24)
        glow(hp, (cx, cy, h + 8))
        for s in (1, -1):
            nav(mb, hp, (cx + s * 5.6, cy, h - 1), 1.0)
        cyl(mb, (cx + 8, cy, 3), (cx + 22, cy + 6, 3), 1.6, PI, 12)
    # ---- ore hoppers, collector belt and inclined conveyor gantry to the furnace tops
    for hx in (44.0, 78.0, 112.0):
        hy = -100.0
        lathe_z(mb, hx, hy, [(0, 12, D), (4, 12, PT), (14.1, 26, PT), (14.1, 30, D), (14.8, 30, H), (14.8, 33, G),
                             (0, 33, G)], PT, segs=4, phase=math.pi / 4)
        for (dx, dy) in ((8.5, 8.5), (8.5, -8.5), (-8.5, 8.5), (-8.5, -8.5)):
            box(mb, hx + dx - 0.8, hx + dx + 0.8, hy + dy - 0.8, hy + dy + 0.8, GZ, 22, D)
        box(mb, hx - 2.5, hx + 2.5, hy - 2.5, hy + 2.5, 9, 12, D)
    truss(mb, (28, -100, 8), (126, -100, 8), 6, 4, 10, 0.8, D, PT)
    box(mb, 28, 126, -101.6, -98.4, 7, 9.2, P)
    for x in range(30, 127, 16):
        beam(mb, (x, -103, GZ), (x, -103, 6), 0.8, D)
        beam(mb, (x, -97, GZ), (x, -97, 6), 0.8, D)
    pa, pb = V((28, -100, 8)), V((-3, -50, 98))
    truss(mb, pa, pb, 6, 5, 8, 0.9, D, PT)
    beam(mb, pa + V((0, 0, 1)), pb + V((0, 0, 1)), 3.4, P, h=2.6)
    for f in (0.33, 0.66):
        q = pa + (pb - pa) * f
        truss(mb, (q.x, q.y, GZ), (q.x, q.y, q.z - 3), 6, 6, 8, 0.8, D, PT, up=(0, 1, 0))
    truss(mb, (-3, -50, GZ), (-3, -50, 98), 8, 8, 9, 1.0, D, PT, up=(0, 1, 0))
    box(mb, -9, 3, -56, -44, 96, 106, PT)
    bands(mb, -9, 3, -56, -44, ("+y", "-x"), [(100, 103)], margin=1.5)
    truss(mb, (-9, -48, 101), (-82, -46, 101), 5, 5, 9, 0.8, D, PT)
    box(mb, -82, -9, -48.8, -45.2, 100, 102, P)
    for (fx, fy) in FURN:
        beam(mb, (fx + 3, -47, 99), (fx + 5, fy - 9, 90), 2.4, D)
    nav(mb, hp, (-3, -50, 107.5), 1.2)
    # ---- distillation columns with platforms, ladders and vapour lines
    COLS = [(28.0, -8.0, 4.5, 82.0), (48.0, 14.0, 6.0, 66.0), (66.0, -22.0, 4.0, 94.0),
            (88.0, 4.0, 5.5, 74.0), (108.0, -20.0, 3.5, 58.0), (112.0, 22.0, 3.0, 48.0)]
    for (cx, cy, r, h) in COLS:
        lathe_z(mb, cx, cy, [(0, GZ, D), (r + 1.5, GZ, D), (r + 1.5, 5, D), (r, 7, T), (r, h, T),
                             (r * 0.7, h + r * 0.5, T), (0, h + r * 0.75, T)], T, 20)
        for z in range(20, int(h), 20):
            lathe_z(mb, cx, cy, [(r + 0.3, z), (r + 0.3, z + 1.5)], PT, 20)
        for z in range(16, int(h) - 4, 16):
            ring(mb, (cx, cy, z), (0, 0, 1), r + 0.2, r + 3, 0.6, G, 20)
        box(mb, cx + r + 0.4, cx + r + 1.6, cy - 0.6, cy + 0.6, GZ, h, D)          # ladder cage
        ox = cx + r + 4
        pipe_run(mb, [(cx, cy, h + r * 0.6), (cx, cy, h + r * 0.75 + 3), (ox, cy, h + r * 0.75 + 3), (ox, cy, 7)], 1.1)
        box(mb, ox - 2.5, ox + 2.5, cy - 2.5, cy + 2.5, GZ, 7, D)
        if h > 70:
            nav(mb, hp, (cx, cy, h + r * 0.75 + 0.8), 1.0)
    # ---- tank farm at the front right inside a bund wall
    TANKS = [(46.0, 66.0, 14.0), (82.0, 66.0, 14.0), (46.0, 100.0, 13.0), (82.0, 100.0, 13.0), (114.0, 84.0, 9.0)]
    for (tx, ty, r) in TANKS:
        hgt = 18 if r > 10 else 14
        lathe_z(mb, tx, ty, [(0, GZ, D), (r + 0.6, GZ, D), (r + 0.6, 2, T), (r + 0.6, hgt, H), (r + 0.6, hgt + 1.5, D),
                             (r * 0.85, hgt + 3.5, T), (r * 0.45, hgt + 5.2, T), (0, hgt + 5.6, T)], T, 28)
        lathe_z(mb, tx, ty, [(r + 0.9, hgt * 0.5), (r + 0.9, hgt * 0.5 + 1.2)], D, 28)
        for k in range(4):                         # stair stringer up the side
            g0, g1 = 0.3 + k * 0.4, 0.7 + k * 0.4
            beam(mb, (tx + (r + 1.6) * math.cos(g0), ty + (r + 1.6) * math.sin(g0), GZ + hgt * k / 4),
                 (tx + (r + 1.6) * math.cos(g1), ty + (r + 1.6) * math.sin(g1), GZ + hgt * (k + 1) / 4), 1.2, PT)
    for (x0, x1, y0, y1) in ((28, 128, 47, 48.2), (28, 128, 117, 118.2), (28, 29.2, 47, 118.2), (126.8, 128, 47, 118.2)):
        box(mb, x0, x1, y0, y1, GZ, 3, D)
    # ---- control building at the front left with a glazed control room on top
    box(mb, -122, -62, 68, 108, GZ, 19, P)
    bands(mb, -122, -62, 68, 108, ("+x", "-x", "+y", "-y"), [(3, 6), (8.5, 11.5), (14, 17)], margin=2.5)
    trim(mb, -122, -62, 68, 108, 17.6, 19, PT, 0.3)
    box(mb, -114, -82, 76, 100, 19, 26.5, D)
    bands(mb, -114, -82, 76, 100, ("+x", "-x", "+y", "-y"), [(20.2, 25.6)], margin=0.8, d=0.35)
    box(mb, -115.5, -80.5, 74.5, 101.5, 26.5, 27.5, D)
    box(mb, -100, -84, 108, 114, 5, 6, PT)
    for x in (-99.0, -85.0):
        box(mb, x - 0.4, x + 0.4, 113, 113.8, GZ, 5, D)
    for (x, y) in ((-74.0, 74.0), (-70.0, 84.0), (-74.0, 92.0)):
        box(mb, x - 3, x + 3, y - 2.5, y + 2.5, 19, 22, D)
    beam(mb, (-68, 102, 19), (-68, 102, 48), 0.9, D)
    nav(mb, hp, (-68, 102, 49.2), 1.0)
    dish(mb, (-100, 70, 31), (0.2, 1, 0.8), 3.5)
    beam(mb, (-100, 70, 27.5), (-100, 70, 30.5), 0.8, D)
    flood(mb, hp, (-82, 100, 28.5), (-50, 10, 5))
    # ---- pipe racks: E-W along y = 40 and N-S along x = 12
    rack_frame(mb, (-124, 40, 0), (124, 40, 0), 8, 16, 16,
               [(-3, 10.5, 0.9), (-1, 10.5, 1.4), (1.5, 10.5, 0.9), (3.2, 10.5, 1.1), (-2, 16, 1.6), (2.2, 16, 1.0)])
    rack_frame(mb, (12, -40, 0), (12, 36, 0), 8, 16, 16, [(-3, 10.5, 1.2), (0, 10.5, 1.5), (3, 10.5, 0.9), (0, 16, 1.2)])
    for (x, y) in ((46.0, 52.0), (82.0, 52.0)):
        pipe_run(mb, [(x, 44, 17.6), (x, y, 17.6), (x, y, 19.5)], 0.9)
    pipe_run(mb, [(-62, 76, 12), (-58, 76, 12), (-58, 44, 12)], 0.6)
    for (fx, fy) in FURN:
        pipe_run(mb, [(fx + 16, fy + 20, 12), (fx + 16, 40, 12)], 1.0, joint=False)
    # ---- flood masts at the corners, some yard clutter
    flood_mast(mb, hp, -127, 118, 34, [(-60, 40, 0), (-100, 60, 0)])
    flood_mast(mb, hp, 127, 118, 34, [(70, 60, 0), (60, 0, 20)])
    flood_mast(mb, hp, 127, -118, 34, [(80, -100, 10)])
    flood_mast(mb, hp, -129, -82, 34, [(-80, -90, 20)])
    for k in range(6):                             # ore haulers parked by the hoppers
        x = 36 + k * 15 + rng.uniform(-2, 2)
        box(mb, x - 3, x + 3, -84, -76, GZ, 4.5, rng.choice((PT, H)))
        box(mb, x - 2.6, x + 2.6, -76, -73.5, GZ, 5.5, D)
    obj = mb.build(coll)
    return [obj], hp, {"kind": "surface", "pad_gap": 250.0, "front": 122.0}


# ------------------------------------------------------------------ 4. ground fabrication plant
def lattice_mast(mb, hp, x, y, h, base, top):
    n = int(h // 8)
    def half(z):
        return base + (top - base) * z / h
    for i in range(n):
        za, zb = GZ + (h - GZ) * i / n, GZ + (h - GZ) * (i + 1) / n
        ha, hb = half(za), half(zb)
        cs_a = [(x + sx * ha, y + sy * ha, za) for (sx, sy) in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
        cs_b = [(x + sx * hb, y + sy * hb, zb) for (sx, sy) in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
        for k in range(4):
            beam(mb, cs_a[k], cs_b[k], 0.9, PT)
            beam(mb, cs_b[k], cs_b[(k + 1) % 4], 0.5, D)
            if (i + k) % 2:
                beam(mb, cs_a[k], cs_b[(k + 1) % 4], 0.4, D)
            else:
                beam(mb, cs_a[(k + 1) % 4], cs_b[k], 0.4, D)
    beam(mb, (x, y, h), (x, y, h + 12), 0.7, D)
    nav(mb, hp, (x, y, h + 13), 1.2)
    for s in (1, -1):
        nav(mb, hp, (x + s * (half(h * 0.55) + 0.8), y, GZ + h * 0.55), 0.9)


def build_factory_surface(coll):
    rng = random.Random(21)
    mb = MB("Ind_FactorySurface")
    hp = new_hp()
    foundation(mb, -152, 152, -87, 87)
    # ---- fabrication hall with sawtooth roof (glazed north lights face the front / pad)
    HX0, HX1, HY0, HY1, HZ = -148.0, 30.0, -80.0, 38.0, 42.0
    box(mb, HX0, HX1, HY0, HY1, GZ, HZ, P)
    trim(mb, HX0, HX1, HY0, HY1, HZ - 5, HZ - 1, PT, 0.3)
    box(mb, HX0 - 1, HX1 + 1, HY0 - 1, HY1 + 1, HZ, HZ + 1.2, D)
    pitch = (HY1 - HY0) / 5
    for i in range(5):
        ya, yb = HY0 + i * pitch, HY0 + (i + 1) * pitch
        prism(mb, [(ya, HZ + 1.2), (yb, HZ + 1.2), (yb, HZ + 15)], (0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0),
              HX0, HX1, D, caps=(P, P))
        box(mb, HX0 + 3, HX1 - 3, yb, yb + 0.3, HZ + 2.4, HZ + 14, W)
        for k in range(6):                                                    # roof extract fans
            x = HX0 + 18 + k * 28 + rng.uniform(-3, 3)
            yv = ya + pitch * 0.45
            zv = HZ + 1.2 + 13.8 * 0.45
            lathe_z(mb, x, yv, [(0, zv - 1, D), (2.2, zv - 1, D), (2.2, zv + 3, D), (2.6, zv + 3, D), (2.6, zv + 3.6, D),
                                (0, zv + 3.6, D)], D, 12)
    DOORS = (-122.0, -76.0, -30.0)
    for xc in DOORS:
        hangar_door(mb, "+y", HY1, xc - 16, xc + 16, GZ, GZ + 30, leaf=8)
        box(mb, xc - 18.5, xc + 18.5, HY1, HY1 + 14, GZ, GZ + 0.12, H)          # apron box
    for x in (-145.0, -99.0, -53.0, -7.0, 22.0):
        wall_box(mb, "+y", HY1, x - 0.9, x + 0.9, GZ, HZ, 0, 1.4, D)
    for (u0, u1) in ((-142, -101), (-96, -55), (-50, -9), (-4, 27)):
        wall_box(mb, "+y", HY1, u0 + 1.5, u1 - 1.5, 34, 37, 0, 0.3, W)
    for x in range(int(HX0) + 8, int(HX1), 16):
        wall_box(mb, "-y", HY0, x - 0.9, x + 0.9, GZ, HZ, 0, 1.4, D)
    bands(mb, HX0, HX1, HY0, HY1, ("-y",), [(20, 23), (32, 35)], margin=3)
    bands(mb, HX0, HX1, HY0, HY1, ("-x",), [(8, 11), (20, 23), (32, 35)], margin=3)
    hangar_door(mb, "+x", HX1, -62, 14, GZ, GZ + 34, leaf=12)
    for k in range(5):
        lamp(mb, (HX1 + 0.6, -70 + 4 * k, 8), 0.8)
    # ---- office block at the front left, skybridge to the hall
    OX0, OX1, OY0, OY1, OZ = -142.0, -88.0, 48.0, 82.0, 26.5
    box(mb, OX0, OX1, OY0, OY1, GZ, OZ, P)
    bands(mb, OX0, OX1, OY0, OY1, ("+x", "-x", "+y", "-y"),
          [(GZ + 2.0 + 4 * k, GZ + 5.0 + 4 * k) for k in range(6)], margin=2)
    for x in (OX0 + 1, -115.0, OX1 - 1):
        wall_box(mb, "+y", OY1, x - 0.8, x + 0.8, GZ, OZ + 2, 0, 1.0, PT)
    trim(mb, OX0, OX1, OY0, OY1, OZ, OZ + 1.6, D, 0.3)
    box(mb, OX0 + 6, OX0 + 18, OY0 + 6, OY0 + 16, OZ, OZ + 4, D)
    box(mb, OX1 - 16, OX1 - 6, OY1 - 12, OY1 - 4, OZ, OZ + 3, D)
    box(mb, -122, -106, OY1, OY1 + 7, 5, 6, PT)
    wall_box(mb, "+y", OY1, -121, -107, GZ, 4.6, 0, 0.3, W)
    box(mb, -128, -120, HY1, OY0, 12, 17, P)
    bands(mb, -128, -120, HY1, OY0, ("+x", "-x"), [(13, 16)], margin=0.6)
    beam(mb, (-96, 54, OZ), (-96, 54, OZ + 18), 0.6, D)
    nav(mb, hp, (-96, 54, OZ + 19), 0.9)
    dish(mb, (-134, 76, OZ + 5), (0.3, 1, 0.9), 3)
    beam(mb, (-134, 76, OZ), (-134, 76, OZ + 4.5), 0.7, D)
    # ---- comms mast at the front, between the office and the yard
    MX, MY = -78.0, 68.0
    lattice_mast(mb, hp, MX, MY, 88.0, 6.0, 1.8)
    box(mb, MX - 9, MX + 9, MY - 9, MY + 9, GZ, GZ + 1.2, D)
    box(mb, MX + 9.5, MX + 17, MY - 4, MY + 4, GZ, 4.5, P)
    for (z, ax, r) in ((56.0, (0.4, 1, 0.1), 4.0), (70.0, (-1, 0.6, 0.15), 3.2), (44.0, (1, 0.2, 0.3), 2.8)):
        a = V(ax).normalized()
        hz = 6.0 + (1.8 - 6.0) * z / 88
        c = V((MX, MY, z)) + V((a.x, a.y, 0)).normalized() * (hz + r * 0.6)
        dish(mb, c, a, r)
        beam(mb, V((MX, MY, z)), c - a * 0.3 * r, 0.5, D)
    # ---- gantry-crane yard on the right
    for sy in (1, -1):
        for x in range(44, 147, 17):
            box(mb, x - 1.25, x + 1.25, sy * 76 - 1.25, sy * 76 + 1.25, GZ, 31, PT)
            beam(mb, (x, sy * 76, 25), (x + 5 if x < 140 else x - 5, sy * 76, 31), 0.8, D)
        box(mb, 42, 148, sy * 76 - 1.4, sy * 76 + 1.4, 31, 34, PT)
        box(mb, 42, 148, sy * 76 - 0.4, sy * 76 + 0.4, 34, 34.6, D)
        for xe in (42.0, 148.0):
            wall_box(mb, "-x" if xe < 100 else "+x", xe, sy * 76 - 1.4, sy * 76 + 1.4, 31, 34, 0, 0.3, H)
        nav(mb, hp, (148.6, sy * 76, 35.4), 1.0)
    for (xc, ty, hook, load) in ((72.0, -22.0, 14.0, True), (124.0, 28.0, 22.0, False)):
        for dx in (-3.5, 3.5):
            box(mb, xc + dx - 1.2, xc + dx + 1.2, -77, 77, 35.5, 39.5, PT)
        for sy in (1, -1):
            box(mb, xc - 6, xc + 6, sy * 76 - 2, sy * 76 + 2, 34.6, 37.5, D)
            wall_box(mb, "+x", xc + 6, sy * 76 - 2, sy * 76 + 2, 34.6, 36.0, 0, 0.3, H)
            wall_box(mb, "-x", xc - 6, sy * 76 - 2, sy * 76 + 2, 34.6, 36.0, 0, 0.3, H)
        box(mb, xc - 5.5, xc + 5.5, ty - 4, ty + 4, 39.5, 43, D)
        for dx in (-1.5, 1.5):
            beam(mb, (xc + dx, ty, 39.5), (xc + dx, ty, hook + 1.5), 0.3, D)
        box(mb, xc - 2, xc + 2, ty - 1.2, ty + 1.2, hook - 1.5, hook + 1.5, H)
        if load:
            box(mb, xc - 10, xc + 10, ty - 5, ty + 5, hook - 3, hook - 2.2, PT)
        box(mb, xc + 3, xc + 7, 66, 72, 31, 35.5, P)
        wall_box(mb, "-y", 66, xc + 3.4, xc + 6.6, 32, 34.8, 0, 0.3, W)
    # part stacks: plates, beam bundles, containers, ring sections, a module frame
    for (px, py) in ((56.0, -52.0), (56.0, -26.0), (80.0, -60.0)):
        zc = GZ
        for k in range(rng.randint(4, 7)):
            ox, oy = rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6)
            box(mb, px - 7 + ox, px + 7 + ox, py - 4.5 + oy, py + 4.5 + oy, zc, zc + 0.8, rng.choice((P, P, PT, D)))
            zc += 0.8
    for (px, py) in ((100.0, -56.0), (100.0, -36.0)):
        for row in range(3):
            for col in range(4):
                y = py - 4.5 + col * 3
                z = GZ + row * 2.2
                box(mb, px - 11, px + 11, y - 1.1, y + 1.1, z, z + 1.9, D if (row + col) % 2 else PT)
    for (px, py) in ((136.0, -56.0), (136.0, -40.0), (136.0, -24.0), (136.0, 44.0), (136.0, 58.0)):
        for k in range(rng.randint(1, 3)):
            box(mb, px - 6, px + 6, py - 2.6, py + 2.6, GZ + k * 5.2, GZ + (k + 1) * 5.2 - 0.1, rng.choice((PT, T, P, H, PT)))
    for (px, py, m) in ((104.0, 20.0, T), (104.0, 48.0, P), (76.0, 52.0, T)):
        lathe(mb, (px, py - 9, GZ + 7.6), (0, 1, 0), [(7, 0, m), (7, 18, D), (5.8, 18, D), (5.8, 0.0, D)], m, 24, closed=True)
        for yy in (py - 6, py + 6):
            box(mb, px - 5, px + 5, yy - 1, yy + 1, GZ, GZ + 2.2, D)
    for (bx0, by0) in ((60.0, 20.0),):
        pts = [(bx0, by0), (bx0 + 18, by0), (bx0 + 18, by0 + 16), (bx0, by0 + 16)]
        for (x, y) in pts:
            box(mb, x - 0.7, x + 0.7, y - 0.7, y + 0.7, GZ, GZ + 14, PT)
        for z in (GZ + 0.7, GZ + 14):
            for a, b in zip(pts, pts[1:] + pts[:1]):
                beam(mb, (a[0], a[1], z), (b[0], b[1], z), 1.0, PT)
        box(mb, bx0, bx0 + 18, by0, by0 + 16, GZ + 13.6, GZ + 14.4, P)
        wall_box(mb, "-y", by0, bx0, bx0 + 18, GZ + 1.4, GZ + 9, 0, 0.5, P)
    for y in (-68.0, 66.0):
        box(mb, 44, 146, y - 0.5, y + 0.5, GZ, GZ + 0.12, H)
    # ---- front apron: vehicle lane, haulers, fuel tanks, flood masts
    box(mb, -150, 150, 84.0, 85.0, GZ, GZ + 0.12, H)
    for k, x in enumerate((-40.0, -26.0, 128.0)):
        box(mb, x - 3, x + 3, 58, 72, GZ, GZ + 4.5, PT if k % 2 else H)
        box(mb, x - 2.8, x + 2.8, 72, 76, GZ, GZ + 6, D)
        wall_box(mb, "+y", 76, x - 2, x + 2, GZ + 3.5, GZ + 5.5, 0, 0.3, W)
    for (tx, ty) in ((2.0, 72.0), (14.0, 78.0)):
        capsule(mb, (tx - 6, ty, GZ + 3.4), (1, 0, 0), 2.6, 12, T, 16)
        for xx in (tx - 4, tx + 4):
            box(mb, xx - 0.6, xx + 0.6, ty - 2, ty + 2, GZ, GZ + 1.6, D)
    flood_mast(mb, hp, -149, 84, 30, [(-110, 30, 0), (-70, 60, 0)])
    flood_mast(mb, hp, 149, 84, 30, [(100, 0, 0)])
    for sy in (1, -1):
        for x in (61.0, 112.0):
            flood(mb, hp, (x, sy * 74.2, 30), (x + 10, 0, GZ))
    obj = mb.build(coll)
    return [obj], hp, {"kind": "surface", "pad_gap": 220.0, "front": 87.0}


# ------------------------------------------------------------------ 5. landing pad
PAD_HALF = 150.0          # apothem: 300 m across flats
DECK_Z = 6.0


def octagon(apothem):
    """Octagon with flat sides facing +-X and +-Y (vertices at 22.5 + 45k degrees)."""
    rc = apothem / math.cos(math.pi / 8)
    return [(rc * math.cos(math.pi / 8 + k * math.pi / 4), rc * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]


def build_landing_pad(coll):
    hp = new_hp()
    deck = MB("Pad Deck", pad_square=(-PAD_HALF, -PAD_HALF, 2 * PAD_HALF))
    up = ((0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    prism(deck, octagon(PAD_HALF), *up, 3.0, DECK_Z, P, caps=(D, PAD))
    prism(deck, octagon(PAD_HALF - 4), *up, 0.0, 3.0, D)
    prism(deck, octagon(PAD_HALF + 4), *up, -12.0, 0.0, D)
    fr = MB("Pad Frame")
    rc = PAD_HALF / math.cos(math.pi / 8)
    side_len = 2 * PAD_HALF * math.tan(math.pi / 8)
    RAMP_W, STAIR_W = 16.0, 6.0
    for k in range(8):
        g = k * math.pi / 4
        n = V((math.cos(g), math.sin(g), 0))
        t = V((-math.sin(g), math.cos(g), 0))
        gap = RAMP_W + 1 if k in (2, 6) else (STAIR_W + 1 if k in (0, 4) else 0)
        segs = [(-side_len / 2 - 0.2, -gap), (gap, side_len / 2 + 0.2)] if gap else [(-side_len / 2 - 0.2, side_len / 2 + 0.2)]
        for (s0, s1) in segs:
            c = n * (PAD_HALF + 0.2) + t * ((s0 + s1) / 2) + V((0, 0, 5.05))
            obox(fr, c, t, n, (0, 0, 1), (s1 - s0) / 2, 0.2, 0.75, H)
        for i in range(8):
            s = -side_len / 2 + side_len * (i + 0.5) / 8
            if gap and abs(s) < gap + 1:
                continue
            lamp(fr, n * (PAD_HALF + 0.55) + t * s + V((0, 0, 3.6)), 0.8)
        cg = g + math.pi / 8
        nav(fr, hp, (math.cos(cg) * (rc + 0.7), math.sin(cg) * (rc + 0.7), 5.2), 1.0)
    # vehicle ramps on the +-Y sides (approach side -Y = low V on the pad texture)
    for sy in (1, -1):
        y0, y1 = sy * PAD_HALF, sy * (PAD_HALF + 52)
        prism(fr, [(y0, -2.0), (y1, -2.0), (y1, 0.0), (y0, DECK_Z)], (0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0),
              -RAMP_W, RAMP_W, P, edge_mats=[D, D, G, D])
        for sx in (1, -1):
            x = sx * (RAMP_W - 0.8)
            a, b = V((x, y0, DECK_Z + 0.08)), V((x, y1, 0.08))
            beam(fr, a, b, 1.2, H, h=0.16)
            for f in [i / 8 for i in range(9)]:
                p = a + (b - a) * f
                beam(fr, p, p + V((0, 0, 1.2)), 0.3, PT)
            beam(fr, a + V((0, 0, 1.2)), b + V((0, 0, 1.2)), 0.35, PT)
            box(fr, sx * RAMP_W - (0.8 if sx > 0 else -0.8) * 0 - 0.8, sx * RAMP_W + 0.8, *sorted((y1 - sy * 0.5, y1 + sy * 1.5)), -0.5, 1.2, H)
    # personnel stairs on the +-X sides
    for sx in (1, -1):
        for i in range(12):
            xa, xb = sx * (PAD_HALF + 0.9 * i), sx * (PAD_HALF + 0.9 * (i + 1))
            box(fr, xa, xb, -STAIR_W, STAIR_W, -1.0, DECK_Z - 0.5 * (i + 1), G if i % 2 else P)
        for sy in (1, -1):
            y = sy * (STAIR_W + 0.4)
            beam(fr, (sx * PAD_HALF, y, DECK_Z + 1.1), (sx * (PAD_HALF + 10.8), y, 1.1), 0.25, PT)
            beam(fr, (sx * (PAD_HALF + 10.8), y, -0.5), (sx * (PAD_HALF + 10.8), y, 1.1), 0.25, PT)
            beam(fr, (sx * (PAD_HALF + 0.3), y, 0), (sx * (PAD_HALF + 10.8), y, -0.6), 0.6, D)
    # flood masts beyond four corners, aimed at the deck centre
    for k in (0, 2, 4, 6):
        g = math.pi / 8 + k * math.pi / 4
        x, y = math.cos(g) * (rc + 20), math.sin(g) * (rc + 20)
        box(fr, x - 2.5, x + 2.5, y - 2.5, y + 2.5, -2, 1.0, D)
        beam(fr, (x, y, 1.0), (x, y, 32), 1.4, PT)
        flood(fr, hp, (x - math.cos(g) * 1.5, y - math.sin(g) * 1.5, 33), (0, 0, DECK_Z))
        nav(fr, hp, (x, y, 35.5), 1.0)
    # control kiosk by one corner
    g = math.pi / 8 + 3 * math.pi / 4
    kx, ky = math.cos(g) * (rc + 26), math.sin(g) * (rc + 26)
    box(fr, kx - 6, kx + 6, ky - 6, ky + 6, -1.0, 14, P)
    bands(fr, kx - 6, kx + 6, ky - 6, ky + 6, ("+x", "-x", "+y", "-y"), [(9.5, 13.0)], margin=0.6)
    trim(fr, kx - 6, kx + 6, ky - 6, ky + 6, 3.0, 4.2, H, 0.3)
    box(fr, kx - 7, kx + 7, ky - 7, ky + 7, 14, 15, D)
    beam(fr, (kx, ky, 15), (kx, ky, 26), 0.6, D)
    nav(fr, hp, (kx, ky, 27), 0.9)
    objs = [deck.build(coll), fr.build(coll)]
    hp["padCentre"] = V((0, 0, DECK_Z))
    hp["padRadius"] = PAD_HALF
    hp["deckHeight"] = DECK_Z
    return objs, hp, {"kind": "pad"}


# ------------------------------------------------------------------ analysis helpers
def tri_count(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


def centre_of_mass(objs):
    """Volume-weighted centroid of the closed parts (divergence theorem over triangles)."""
    vol, acc = 0.0, V()
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        vs = me.vertices
        for t in me.loop_triangles:
            a, b, c = (vs[i].co for i in t.vertices)
            v6 = a.dot(b.cross(c))
            vol += v6
            acc += (a + b + c) * v6
    return acc / (4 * vol) if abs(vol) > 1e-6 else V()


def shift_hp(hp, d):
    for k in ("navLights", "glows"):
        hp[k] = [p + d for p in hp[k]]
    hp["floods"] = [(p + d, f) for (p, f) in hp["floods"]]
    if "berth" in hp:
        hp["berth"]["position"] = hp["berth"]["position"] + d
    if "padCentre" in hp:
        hp["padCentre"] = hp["padCentre"] + d


def bounds(objs):
    pts = [v.co for o in objs for v in o.data.vertices]
    lo = V((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = V((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi, max(p.length for p in pts)


def ship_frame(berth):
    f = V(berth["forward"]).normalized()
    s = V((0, 0, 1)).cross(f).normalized()
    return V(berth["position"]), f, s, V((0, 0, 1))


def berth_clearance(objs, berth, step=4.0):
    """Minimum distance from the parked ship box's surface to the geometry, and the number of
    structure vertices inside the box."""
    c, f, s, u = ship_frame(berth)
    hl, hb, hh = SHIP[0] / 2, SHIP[1] / 2, SHIP[2] / 2
    best, inside = 1e9, 0
    for o in objs:
        me = o.data
        bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [p.vertices[:] for p in me.polygons])
        for v in me.vertices:
            q = v.co - c
            if abs(q.dot(f)) < hl and abs(q.dot(s)) < hb and abs(q.dot(u)) < hh:
                inside += 1
        nl, nb, nh = int(SHIP[0] / step) + 1, int(SHIP[1] / step) + 1, int(SHIP[2] / step) + 1
        samples = []
        for i in range(nl):
            a = -hl + SHIP[0] * i / (nl - 1)
            for j in range(nb):
                b = -hb + SHIP[1] * j / (nb - 1)
                samples += [(a, b, -hh), (a, b, hh)]
            for k in range(nh):
                h = -hh + SHIP[2] * k / (nh - 1)
                samples += [(a, -hb, h), (a, hb, h)]
        for j in range(nb):
            for k in range(nh):
                b, h = -hb + SHIP[1] * j / (nb - 1), -hh + SHIP[2] * k / (nh - 1)
                samples += [(-hl, b, h), (hl, b, h)]
        for (a, b, h) in samples:
            r = bvh.find_nearest(c + f * a + s * b + u * h)
            if r[0] is not None:
                best = min(best, r[3])
    return best, inside


def nav_colour(p, kind):
    """Port (-X) red, starboard (+X) green, centre-line / top white."""
    if abs(p.x) < 6:
        return "white"
    return "green" if p.x > 0 else "red"


# ------------------------------------------------------------------ build + export
BUILDERS = [("Ind_RefinerySpace", build_refinery_space), ("Ind_FactorySpace", build_factory_space),
            ("Ind_RefinerySurface", build_refinery_surface), ("Ind_FactorySurface", build_factory_surface),
            ("Ind_LandingPad", build_landing_pad)]
NOTE = ("Unity coordinates in metres: Blender (x, y, z) -> Unity (x, z, y); Unity +Y up, +Z = the model's "
        "forward (surface plants face their pad side along +Z). Space models: origin at the centre of mass; the "
        "berth is the parked ship's centre (ship level, facing berth.forward, horizontal). Surface models and the "
        "pad: origin at ground centre, 12 m foundation below y = 0. size = bounding box, radius = bounding "
        "sphere about the origin. Nav light colour: red port (-X), green starboard (+X), white centre/top.")

want = [n for n, _ in BUILDERS if not ONLY or n in ONLY]
need_pad = PREVIEW_DIR and any(n in ("Ind_RefinerySurface", "Ind_FactorySurface") for n in want)
BUILT = {}
for name, fn in BUILDERS:
    export_it = name in want
    if not export_it and not (name == "Ind_LandingPad" and need_pad):
        continue
    coll = bpy.data.collections.new(name)
    scene.collection.children.link(coll)
    objs, hp, info = fn(coll)
    if info["kind"] == "space":
        com = centre_of_mass(objs)
        for o in objs:
            o.data.transform(Matrix.Translation(-com))
        shift_hp(hp, -com)
        info["com_shift"] = com
    lo, hi, rad = bounds(objs)
    info.update(lo=lo, hi=hi, radius=rad, tris=tri_count(objs))
    if "berth" in hp:
        info["clearance"], info["inside"] = berth_clearance(objs, hp["berth"])
    if export_it:
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        path = os.path.join(MODELS_DIR, name + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False)
        print("EXPORTED", path)
    scene.collection.children.unlink(coll)
    BUILT[name] = (coll, objs, hp, info, export_it)
    sz = hi - lo
    print(f"MODEL {name}: tris={info['tris']} size(blender)=({sz.x:.1f}, {sz.y:.1f}, {sz.z:.1f}) "
          f"radius={rad:.1f} mats={[m.name for o in objs for m in o.data.materials]}")
    if "berth" in hp:
        print(f"  berth {to_unity(hp['berth']['position'])} fwd {to_unity(hp['berth']['forward'])} "
              f"clearance={info['clearance']:.1f} m, vertices inside ship box={info['inside']}")
    if "com_shift" in info:
        print(f"  centre of mass shift (blender) = {tuple(round(c, 2) for c in info['com_shift'])}")

# ------------------------------------------------------------------ hardpoints JSON
jpath = os.path.join(DATA_DIR, "industry_hardpoints.json")
models = {}
if ONLY and os.path.exists(jpath):
    try:
        with open(jpath) as fh:
            models = {m["name"]: m for m in json.load(fh).get("models", [])}
    except Exception:
        models = {}
for name, (coll, objs, hp, info, export_it) in BUILT.items():
    if not export_it:
        continue
    lo, hi = info["lo"], info["hi"]
    b = hp.get("berth")
    models[name] = {
        "name": name,
        "size": to_unity(hi - lo),
        "boundsCentre": to_unity((lo + hi) / 2),
        "radius": round(info["radius"], 3),
        "triangles": info["tris"],
        "berth": {"p": to_unity(b["position"]) if b else [0, 0, 0], "forward": to_unity(b["forward"]) if b else [0, 0, 0]},
        "berthClearance": round(info.get("clearance", 0.0), 2),
        "padCentre": {"p": to_unity(hp["padCentre"]) if "padCentre" in hp else [0, 0, 0]},
        "padRadius": hp.get("padRadius", 0.0),
        "deckHeight": hp.get("deckHeight", 0.0),
        "navLights": [{"p": to_unity(p), "colour": nav_colour(p, info["kind"])} for p in hp["navLights"]],
        "floods": [{"p": to_unity(p), "forward": [round(c, 4) for c in to_unity(d)]} for (p, d) in hp["floods"]],
        "glows": [{"p": to_unity(p)} for p in hp["glows"]],
    }
order = [n for n, _ in BUILDERS]
with open(jpath, "w") as fh:
    json.dump({"note": NOTE, "models": [models[n] for n in order if n in models]}, fh, indent=1)
print("WROTE", jpath)


# ------------------------------------------------------------------ preview renders (Cycles, CPU only)
TEX_USED = {}


def _img(short, suffix, noncolor):
    if not TEX_DIR:
        return None
    p = os.path.join(TEX_DIR, f"T_Ind_{short}_{suffix}.png")
    if not os.path.exists(p):
        return None
    im = bpy.data.images.load(p, check_existing=True)
    if noncolor:
        im.colorspace_settings.name = "Non-Color"
    im.alpha_mode = "CHANNEL_PACKED"
    TEX_USED.setdefault(short, []).append(suffix)
    return im


def setup_preview_materials():
    """Only after every FBX is written: give the named slots Cycles node trees for the previews."""
    emis = {L: ((1.0, 0.95, 0.85), 5.0), GL: ((1.0, 0.32, 0.04), 8.0)}
    for name, m in MATS.items():
        try:
            m.use_nodes = True
        except Exception:
            pass
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        col = FALLBACK[name]
        bsdf.inputs["Base Color"].default_value = (*col, 1)
        bsdf.inputs["Roughness"].default_value = 0.55
        bsdf.inputs["Metallic"].default_value = 0.0 if name in (H, PT, PAD) else 0.5
        if name in emis:
            c, s = emis[name]
            bsdf.inputs["Emission Color"].default_value = (*c, 1)
            bsdf.inputs["Emission Strength"].default_value = s
            continue
        short = name[4:]
        uvn = nt.nodes.new("ShaderNodeUVMap")
        dimg, nimg, mimg, eimg = (_img(short, "D", False), _img(short, "N", True), _img(short, "M", True),
                                  _img(short, "E", False))

        def tex(im):
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = im
            nt.links.new(uvn.outputs["UV"], t.inputs["Vector"])
            return t
        if dimg:
            nt.links.new(tex(dimg).outputs["Color"], bsdf.inputs["Base Color"])
        if nimg:
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nt.links.new(tex(nimg).outputs["Color"], nm.inputs["Color"])
            nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        if mimg:
            t = tex(mimg)
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(t.outputs["Color"], sep.inputs["Color"])
            nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
            inv = nt.nodes.new("ShaderNodeMath")
            inv.operation = "SUBTRACT"
            inv.inputs[0].default_value = 1.0
            nt.links.new(t.outputs["Alpha"], inv.inputs[1])
            nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
        if eimg:
            nt.links.new(tex(eimg).outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 3.0 if name == W else 2.0
        elif name == W:
            bsdf.inputs["Emission Color"].default_value = (1.0, 0.82, 0.55, 1)
            bsdf.inputs["Emission Strength"].default_value = 1.5


def plain_mat(name, col, rough=0.8, emit=0.0):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    b = m.node_tree.nodes.get("Principled BSDF")
    if b is None:
        m.node_tree.nodes.clear()
        out = m.node_tree.nodes.new("ShaderNodeOutputMaterial")
        b = m.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
        m.node_tree.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*col, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


def ship_box(coll, berth):
    """180 x 40 x 30 m placeholder frigate (preview only) with a nose marker."""
    c, f, s, u = ship_frame(berth)
    mb = MB("ShipPlaceholder")
    obox(mb, c, f, s, u, SHIP[0] / 2, SHIP[1] / 2, SHIP[2] / 2, P)
    obox(mb, c + f * (SHIP[0] / 2 + 6), f, s, u, 6, 8, 6, H)
    o = mb.build(coll)
    o.data.materials.clear()
    o.data.materials.append(SHIP_MAT)
    return o


def setup_render():
    scene.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "NONE"
        for dev in getattr(prefs, "devices", []):
            dev.use = dev.type == "CPU"
    except Exception as e:
        print("cycles prefs:", e)
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.cycles.denoiser = "OPENIMAGEDENOISE"
    if hasattr(scene.cycles, "denoising_use_gpu"):
        scene.cycles.denoising_use_gpu = False
    scene.cycles.max_bounces = 4
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"


def set_env(kind):
    w = bpy.data.worlds.get("PrevWorld") or bpy.data.worlds.new("PrevWorld")
    scene.world = w
    try:
        w.use_nodes = True
    except Exception:
        pass
    bg = w.node_tree.nodes.get("Background")
    if bg is None:
        w.node_tree.nodes.clear()
        out = w.node_tree.nodes.new("ShaderNodeOutputWorld")
        bg = w.node_tree.nodes.new("ShaderNodeBackground")
        w.node_tree.links.new(bg.outputs["Background"], out.inputs["Surface"])
    for o in [o for o in scene.collection.objects if o.type == "LIGHT"]:
        bpy.data.objects.remove(o, do_unlink=True)

    def sun(name, d, strength, col, angle=0.02):
        ld = bpy.data.lights.new(name, "SUN")
        ld.energy, ld.color, ld.angle = strength, col, angle
        ob = bpy.data.objects.new(name, ld)
        scene.collection.objects.link(ob)
        ob.rotation_euler = V(d).to_track_quat("-Z", "Y").to_euler()
    if kind == "space":
        bg.inputs["Color"].default_value = (0.012, 0.014, 0.02, 1)
        bg.inputs["Strength"].default_value = 1.0
        sun("key", (-0.55, -0.35, -0.6), 6.0, (1.0, 0.96, 0.9))
        sun("fill", (0.6, 0.5, 0.4), 0.7, (0.6, 0.7, 1.0))
    else:
        bg.inputs["Color"].default_value = (0.42, 0.5, 0.62, 1)
        bg.inputs["Strength"].default_value = 0.7
        sun("key", (-0.5, -0.45, -0.55), 4.0, (1.0, 0.93, 0.82), 0.03)


def look(cam, tgt, d, dist):
    tgt, d = V(tgt), V(d).normalized()
    cam.location = tgt + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def render_previews():
    setup_preview_materials()
    setup_render()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 35
    cam.data.clip_start, cam.data.clip_end = 1.0, 30000
    extra = bpy.data.collections.new("PreviewExtras")
    scene.collection.children.link(extra)
    ground = None
    written = []
    for name, (coll, objs, hp, info, export_it) in BUILT.items():
        if not export_it:
            continue
        scene.collection.children.link(coll)
        kind = info["kind"]
        set_env("space" if kind == "space" else "ground")
        lo, hi = info["lo"], info["hi"]
        c = (lo + hi) / 2
        R = (hi - lo).length / 2
        tmp = []
        if kind != "space":
            bpy.ops.mesh.primitive_plane_add(size=8000, location=(0, 0, 0.02))
            ground = bpy.context.object
            for cl in ground.users_collection:
                cl.objects.unlink(ground)
            extra.objects.link(ground)
            ground.data.materials.append(GROUND_MAT)
            tmp.append(ground)
        views = []
        if kind == "space":
            side = info["ship_side"]
            views = [("34", c, (1.0, 1.15, 0.55), R * 2.4), ("side", c, (-1.0, 0.12, 0.2), R * 2.5)]
            ship = ship_box(extra, hp["berth"])
            tmp.append(ship)
            bp = hp["berth"]["position"]
            tgt = bp * 0.6 + V(hp["berth"]["forward"]) * -60
            views.append(("berth", tgt, (0.45 * hp["berth"]["forward"].x, 1.0 * side, 0.55), 620))
        elif kind == "surface":
            views = [("34", c + V((0, 0, -10)), (0.85, 1.25, 0.6), R * 2.6), ("side", c, (-1.0, 0.25, 0.22), R * 2.6)]
            pad_y = info["front"] + info["pad_gap"] + PAD_HALF
            inst = bpy.data.objects.new("PadInst", None)
            inst.instance_type = "COLLECTION"
            inst.instance_collection = BUILT["Ind_LandingPad"][0]
            inst.location = (0, pad_y, 0)
            extra.objects.link(inst)
            ship = ship_box(extra, {"position": V((0, pad_y, DECK_Z + SHIP[2] / 2)), "forward": V((0, 1, 0))})
            tmp += [inst, ship]
            views.append(("site", V((0, pad_y * 0.45, 20)), (1.0, 0.55, 0.55), (pad_y + 300) * 1.25))
        else:
            ship = ship_box(extra, {"position": V((0, 0, DECK_Z + SHIP[2] / 2)), "forward": V((0, 1, 0))})
            tmp.append(ship)
            ship.hide_render = True
            views = [("34", V((0, 0, 0)), (0.9, -1.1, 0.75), 560), ("side", V((0, 0, 4)), (1.0, -0.25, 0.1), 430),
                     ("ship", V((0, 0, 10)), (0.75, -0.9, 0.6), 520)]
        for (vname, tgt, d, dist) in views:
            if kind == "pad":
                ship.hide_render = vname != "ship"
            look(cam, tgt, d, dist)
            scene.render.filepath = os.path.join(PREVIEW_DIR, f"{name}_{vname}.png")
            bpy.ops.render.render(write_still=True)
            written.append(scene.render.filepath)
            print("RENDERED", scene.render.filepath)
        for o in tmp:
            bpy.data.objects.remove(o, do_unlink=True)
        scene.collection.children.unlink(coll)
    print("TEXTURES USED:", TEX_USED if TEX_USED else "none (fallback colours)")
    return written


if PREVIEW_DIR:
    SHIP_MAT = plain_mat("Preview_Ship", (0.25, 0.55, 0.85), 0.5)
    GROUND_MAT = plain_mat("Preview_Ground", (0.30, 0.25, 0.20), 0.95)
    render_previews()
print("INDUSTRY_DONE")
