"""Model the settlement structures procedurally and export them as FBX for Unity.

Run:  blender -b --python build_settlements.py -- <models_dir> <data_dir> [preview_dir] [texture_dir]
          [--only Hab_Ring,Mil_Gate] [--samples 48]

  Hab_Ring         space-colony habitat ring, 1200 m across: a 36-sided ring of habitat sections
                   (tube ~92 x 90 m), 6 truss spokes with elevator pods, a hub collar of inner
                   radius 125 m (it clamps a separate 240 m drum) and 3 solar wings on the collar.
                   Origin at the ring's centre; the ring's axis is Blender Y (Unity Z).
  Mil_RadarDome    geodesic radome (45 m) on a 12 m octagonal base, lattice mast with a dish beside it
  Mil_WallSegment  straight fortress wall, 120 m long (X), 28 m tall, 14 m thick at the base
  Mil_WallTower    corner / joint tower, 40 x 40 m, 55 m tall, turret socket disc on top
  Mil_Gate         gatehouse 150 m long: two 50 m towers flanking a 50 x 30 m armoured gate, bridge over it

Axes (same as build_industry.py): Blender +Y = forward, +Z = up; with the export flags below a
Blender point (x, y, z) lands in Unity at (x, z, y). The military pieces face their OUTER
(armoured) side along Blender -Y = Unity -Z and their inner side (doors, stairs, walkway access)
along +Y = Unity +Z, the way the fort layouts place them. Their origin is at ground centre, with a
6 m foundation skirt below z = 0. Wall ends are flat (x = +-60) so segments butt end to end; the
gate's ends (x = +-75) are flat too and cover the wall's section.

Every mesh is built in world space with its object at the origin (no transforms to apply).
Materials are named slots only (Ind_*, Station_Solar, Station_Glass); Unity matches them by name.
UVs are box projections at TILE metres per repeat (in world axes, or in a part's own axes for the
parts of the ring and other rotated parts: the same projection, just not world-aligned); lathed
parts get cylindrical UVs (u around, v along; Ind_Pipe has U along the axis).

Outputs: <models_dir>/<name>.fbx, <data_dir>/settlement_hardpoints.json (Unity coordinates),
<preview_dir>/<name>_<view>.png (Cycles, CPU only), textured from <texture_dir> when present.
"""
import bpy, bmesh, math, json, os, sys, random
from contextlib import contextmanager
from mathutils import Vector as V, Matrix

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
D, P, PT, H, G, T, PI, W, L, GL = ("Ind_Dark", "Ind_Panel", "Ind_Paint", "Ind_Hazard", "Ind_Grate", "Ind_Tank",
                                   "Ind_Pipe", "Ind_Windows", "Ind_Light", "Ind_Glow")
SOL, GLS = "Station_Solar", "Station_Glass"
TILE = {P: 16.0, D: 16.0, PT: 12.0, H: 4.0, G: 6.0, T: 12.0, PI: 4.0, W: 8.0, L: 4.0, GL: 4.0, SOL: 9.0, GLS: 16.0}
FALLBACK = {P: (0.52, 0.53, 0.55), D: (0.10, 0.11, 0.12), PT: (0.62, 0.36, 0.10), H: (0.80, 0.62, 0.05),
            G: (0.28, 0.28, 0.27), T: (0.72, 0.71, 0.66), PI: (0.45, 0.42, 0.38), W: (0.06, 0.07, 0.08),
            L: (1.0, 0.95, 0.85), GL: (1.0, 0.42, 0.08), SOL: (0.03, 0.06, 0.16), GLS: (0.05, 0.11, 0.13)}
MATS = {}
for _n, _c in FALLBACK.items():
    _m = bpy.data.materials.new(_n)
    _m.diffuse_color = (*_c, 1.0)
    MATS[_n] = _m

SHARP = math.radians(35)
GZ = 0.5      # ground (top of the foundation skirt) for the surface pieces
SKIRT = -6.0  # bottom of the foundation skirt


def to_unity(p):
    return [round(p[0], 3), round(p[2], 3), round(p[1], 3)]


# ------------------------------------------------------------------ mesh builder
class MB:
    """Collects closed primitives into one bmesh. Faces get box-projected UVs (TILE metres per
    repeat for their material) in world axes, or in the axes of `frame` while one is set, unless
    explicit UVs are given."""

    def __init__(self, name):
        self.name, self.bm, self.mats = name, bmesh.new(), []
        self.uv = self.bm.loops.layers.uv.verify()
        self.frame = None   # (ex, ey, ez): projection axes

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
        t = TILE.get(mat, 8.0)
        n = f.normal
        if self.frame:
            ex, ey, ez = self.frame
            nl = (n.dot(ex), n.dot(ey), n.dot(ez))
            ax = max(range(3), key=lambda i: abs(nl[i]))
            a, b = [(1, 2), (0, 2), (0, 1)][ax]
            for lp in f.loops:
                co = lp.vert.co
                cl = (co.dot(ex), co.dot(ey), co.dot(ez))
                lp[self.uv].uv = (cl[a] / t, cl[b] / t)
            return
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


@contextmanager
def proj(mb, ex, ey, ez):
    """Box-project the faces made inside the block in the axes (ex, ey, ez) instead of world axes."""
    old = mb.frame
    mb.frame = (V(ex).normalized(), V(ey).normalized(), V(ez).normalized())
    try:
        yield
    finally:
        mb.frame = old


# ------------------------------------------------------------------ primitives (as build_industry.py)
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


# ------------------------------------------------------------------ new helpers
class Frame:
    """Local axes: p(x, y, z) = o + ex*x + ey*y + ez*z."""

    def __init__(self, o, ex, ey, ez):
        self.o, self.ex, self.ey, self.ez = V(o), V(ex), V(ey), V(ez)

    def p(self, x, y, z):
        return self.o + self.ex * x + self.ey * y + self.ez * z


def lbox(mb, F, x0, x1, y0, y1, z0, z1, mat):
    """Axis-aligned box in frame F's coordinates."""
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    obox(mb, F.p((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), F.ex, F.ey, F.ez,
         (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2, mat)


def fbox(mb, n, wall, u0, u1, z0, z1, d0, d1, mat, o=(0, 0)):
    """Box on a vertical face of any heading: outward normal n (2D), the face `wall` metres from
    o along n, u along the face (t = n turned +90 degrees), d0..d1 out from it. Projected in the
    face's axes so window rows stay level and square."""
    n3 = V((n[0], n[1], 0)).normalized()
    t3 = V((-n3.y, n3.x, 0))
    z3 = V((0, 0, 1))
    c = V((o[0], o[1], 0)) + n3 * (wall + (d0 + d1) / 2) + t3 * ((u0 + u1) / 2) + z3 * ((z0 + z1) / 2)
    with proj(mb, t3, n3, z3):
        obox(mb, c, t3, n3, z3, (u1 - u0) / 2, (d1 - d0) / 2, (z1 - z0) / 2, mat)


def chamfer_rect(cx, cy, hx, hy, c):
    """Rectangle with 45-degree chamfered corners, counter-clockwise."""
    pts = [(hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c),
           (-hx + c, -hy), (hx - c, -hy)]
    return [(cx + x, cy + y) for (x, y) in pts]


def octagon(apothem, cx=0.0, cy=0.0):
    """Octagon with flat sides facing +-X and +-Y (vertices at 22.5 + 45k degrees)."""
    rc = apothem / math.cos(math.pi / 8)
    return [(cx + rc * math.cos(math.pi / 8 + k * math.pi / 4), cy + rc * math.sin(math.pi / 8 + k * math.pi / 4))
            for k in range(8)]


def loft(mb, secs, caps=(D, D)):
    """Stacked horizontal polygons (same vertex count, counter-clockwise) joined into a closed
    shell. secs: [(poly, z, mat)]; the material applies to the band above that section."""
    rings = [[mb.vert((x, y, z)) for (x, y) in poly] for (poly, z, _) in secs]
    n = len(secs[0][0])
    for i in range(len(secs) - 1):
        m = secs[i][2]
        for k in range(n):
            j = (k + 1) % n
            mb.face([rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k]], m)
    if caps[0]:
        mb.face(list(reversed(rings[0])), caps[0], want=(0, 0, -1))
    if caps[1]:
        mb.face(rings[-1], caps[1], want=(0, 0, 1))


def poly_faces(poly):
    """(centre, outward normal, length) of each edge of a counter-clockwise polygon."""
    out = []
    for i in range(len(poly)):
        a, b = V((*poly[i], 0)), V((*poly[(i + 1) % len(poly)], 0))
        d = b - a
        out.append(((a + b) / 2, V((d.y, -d.x, 0)).normalized(), d.length))
    return out


def merlons(mb, poly, z0, z1, depth, width, pitch, mat=D, inset=0.0):
    """Battlements along every edge of a counter-clockwise polygon."""
    for (c, n, ln) in poly_faces(poly):
        k = max(1, int((ln - 1.0) // pitch))
        for i in range(k):
            u = -ln / 2 + ln * (i + 0.5) / k
            hw = min(width, ln / k - 1.0) / 2
            fbox(mb, n, 0, u - hw, u + hw, z0, z1, -depth - inset, -inset, mat, o=(c.x, c.y))


# ------------------------------------------------------------------ hardpoint helpers
def new_hp():
    return {"navLights": [], "floods": [], "glows": [], "sockets": []}


def nav(mb, hp, p, s=1.4, colour=None):
    lamp(mb, p, s)
    hp["navLights"].append((V(p), colour))


def flood(mb, hp, p, target):
    p = V(p)
    d = (V(target) - p).normalized()
    beam(mb, p - d * 2.2, p - d * 0.25, 2.6, D)
    beam(mb, p - d * 0.3, p, 2.1, L)
    hp["floods"].append((p, d))


def glow(hp, p):
    hp["glows"].append(V(p))


def lattice_mast(mb, hp, x, y, h, base, top, z0=GZ):
    n = int(h // 8)

    def half(z):
        return base + (top - base) * (z - z0) / (h - z0)
    for i in range(n):
        za, zb = z0 + (h - z0) * i / n, z0 + (h - z0) * (i + 1) / n
        ha, hb = half(za), half(zb)
        cs_a = [(x + sx * ha, y + sy * ha, za) for (sx, sy) in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
        cs_b = [(x + sx * hb, y + sy * hb, zb) for (sx, sy) in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
        for k in range(4):
            beam(mb, cs_a[k], cs_b[k], 0.6, PT)
            beam(mb, cs_b[k], cs_b[(k + 1) % 4], 0.35, D)
            if (i + k) % 2:
                beam(mb, cs_a[k], cs_b[(k + 1) % 4], 0.3, D)
            else:
                beam(mb, cs_a[(k + 1) % 4], cs_b[k], 0.3, D)
    beam(mb, (x, y, h), (x, y, h + 8), 0.5, D)
    nav(mb, hp, (x, y, h + 8.8), 1.0, "white")
    return half


# ================================================================== 1. habitat ring
HN = 36                      # sections round the ring
HA = math.pi / HN            # half the angle of a section
CS, TN = math.cos(HA), math.tan(HA)
R_IN, R_OUT, HW, CH = 505.0, 595.0, 46.0, 10.0     # tube: radial 505..595, axial +-46, 10 m chamfers
COLLAR_IN, COLLAR_OUT, COLLAR_HL = 125.0, 150.0, 20.0
SPOKES = (3, 9, 15, 21, 27, 33)                    # sections at 30, 90, 150 ... degrees
WINGS = (0, 12, 24)                                # 0, 120, 240 degrees (between spokes)
Y = V((0, 1, 0))


def rframe(a):
    """Ring frame at angle a in the XZ plane: x = tangent, y = the ring's axis, z = radial out."""
    return Frame((0, 0, 0), (-math.sin(a), 0, math.cos(a)), (0, 1, 0), (math.cos(a), 0, math.sin(a)))


def hab_prof(rin, rout, hw, c):
    """Chamfered octagonal tube section (r, y), counter-clockwise. Edges: 0 inner chamfer -y,
    1 side -y, 2 outer chamfer -y, 3 outer face, 4 outer chamfer +y, 5 side +y, 6 inner chamfer +y,
    7 inner face."""
    return [(rin, -hw + c), (rin + c, -hw), (rout - c, -hw), (rout, -hw + c), (rout, hw - c), (rout - c, hw),
            (rin + c, hw), (rin, hw - c)]


def ring_prism(mb, F, prof, mats, hl=None, caps=(D, D)):
    """Prism of a (r, y) profile along F.ex: a wedge between the frame's +-HA planes (hl None:
    a ring section, flat faces at r cos HA) or a straight slab +-hl long."""
    A, B = [], []
    for (r, y) in prof:
        if hl is None:
            A.append(mb.vert(F.p(-r * math.sin(HA), y, r * CS)))
            B.append(mb.vert(F.p(r * math.sin(HA), y, r * CS)))
        else:
            A.append(mb.vert(F.p(-hl, y, r)))
            B.append(mb.vert(F.p(hl, y, r)))
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        (r0, y0), (r1, y1) = prof[i], prof[j]
        out = F.ez * (y1 - y0) - F.ey * (r1 - r0)     # counter-clockwise (r, y): outward = (dy, -dr)
        mb.face([A[i], A[j], B[j], B[i]], mats[i], want=out)
    mb.face(list(reversed(A)), caps[0], want=-F.ex)
    mb.face(B, caps[1], want=F.ex)


def hab_section(mb, hp, F, i, kind, rng):
    """One habitat section (built in its own frame F; x tangent, y axial, z = distance out)."""
    ro, ri = R_OUT * CS, R_IN * CS                      # outer / inner flat faces
    rs0, rs1 = (R_IN + CH) * CS, (R_OUT - CH) * CS      # flat part of the sides
    ring_prism(mb, F, hab_prof(R_IN, R_OUT, HW, CH), [D, P, D, P, D, P, D, P])

    def hl(r, m):
        return r * TN - m

    def band_or_blank(x0, x1, y0, y1, z0, z1):
        lbox(mb, F, x0, x1, y0, y1, z0, z1, W if rng.random() > 0.12 else D)

    # outer face: two window bands, a service spine with conduits
    if kind != "S":
        for (y0, y1) in ((-34, -18), (18, 34)):
            if kind == "C" and rng.random() < 0.5:
                band_or_blank(-hl(ro, 6), -2, y0, y1, ro, ro + 0.4)
                lbox(mb, F, 2, hl(ro, 6), y0, y1, ro, ro + 0.6, D)
            else:
                band_or_blank(-hl(ro, 6), hl(ro, 6), y0, y1, ro, ro + 0.4)
    lbox(mb, F, -hl(ro, 4), hl(ro, 4), -5, 5, ro, ro + 0.9, D)
    for sy in (1, -1):
        cyl(mb, F.p(-ro * TN - 1, sy * 10, ro + 1.4), F.p(ro * TN + 1, sy * 10, ro + 1.4), 1.3, PI, 10)
    # side faces: decks of windows (v runs inward = up in spin gravity)
    side_bands = {"A": [(516, 530), (536, 550), (556, 570), (574, 580)], "B": [(536, 550), (556, 570)],
                  "C": [(556, 570)], "S": [(574, 580)]}[kind]
    for sy in (1, -1):
        for (z0, z1) in side_bands:
            if rng.random() < 0.15 and kind != "S":
                h0 = hl(z0, 5)
                cut = rng.uniform(-0.5, 0.5) * h0
                band_or_blank(-h0, cut - 2, sy * HW, sy * (HW + 0.4), z0, z1)
                lbox(mb, F, cut + 2, h0, sy * HW, sy * (HW + 0.6), z0, z1, D)
            else:
                band_or_blank(-hl(z0, 5), hl(z0, 5), sy * HW, sy * (HW + 0.4), z0, z1)
        if kind in ("B", "C"):          # plant / storage decks: dark plating
            lbox(mb, F, -hl(518, 6), hl(518, 6), sy * HW, sy * (HW + 0.6), 518, 530, D)
        if kind == "C":                 # furnace vents
            for xc in (-24.0, 24.0):
                lbox(mb, F, xc - 12, xc + 12, sy * HW, sy * (HW + 0.4), 538.8, 548.2, D)
                lbox(mb, F, xc - 10.5, xc + 10.5, sy * HW, sy * (HW + 0.7), 540, 547, GL)
                lbox(mb, F, xc - 12.5, xc + 12.5, sy * HW, sy * (HW + 4.0), 536.6, 538.6, D)
                glow(hp, F.p(xc, sy * (HW + 6), 543.5))
        if kind == "S":                 # docking bay doors
            lbox(mb, F, -18, 18, sy * HW, sy * (HW + 0.4), 522, 566, H)
            lbox(mb, F, -16, 16, sy * HW, sy * (HW + 0.9), 524, 564, D)
            for k in range(4):
                lbox(mb, F, -15 + 7.5 * k + 0.4, -15 + 7.5 * (k + 1) - 0.4, sy * (HW + 0.9), sy * (HW + 1.3), 540, 548, P)
            for x in (-18, -6, 6, 18):
                lamp(mb, F.p(x, sy * (HW + 0.8), 568.5), 1.6)
    # inner face (towards the hub)
    if kind == "A":
        for (y0, y1) in ((-34, -20), (20, 34)):
            band_or_blank(-hl(ri, 6), hl(ri, 6), y0, y1, ri - 0.4, ri)
        lbox(mb, F, -hl(ri, 5), hl(ri, 5), -14, 14, ri - 2.4, ri, GLS)
        n = 7
        for k in range(n + 1):
            x = -hl(ri, 5) + 2 * hl(ri, 5) * k / n
            lbox(mb, F, x - 0.6, x + 0.6, -15, 15, ri - 2.9, ri, D)
        lbox(mb, F, -hl(ri, 5), hl(ri, 5), -15, -14, ri - 2.9, ri, D)
        lbox(mb, F, -hl(ri, 5), hl(ri, 5), 14, 15, ri - 2.9, ri, D)
    elif kind == "B":                   # park / farm vault under glass
        arch = [(-31, 0.0), (-27, -6.5), (-17, -10.5), (17, -10.5), (27, -6.5), (31, 0.0)]
        L0 = hl(ri, 4)
        prism(mb, [(y, ri + z) for (y, z) in arch], F.o, F.ey, F.ez, F.ex, -L0, L0, GLS, caps=(D, D))
        n = 8
        rib = [(y * 1.03, ri + 0.2 if z == 0 else ri + z * 1.05 - 0.4) for (y, z) in arch]
        for k in range(n + 1):
            x = -L0 + 2 * L0 * k / n
            prism(mb, rib, F.o, F.ey, F.ez, F.ex, x - 0.7, x + 0.7, D)
        for sy in (1, -1):
            lbox(mb, F, -hl(ri, 6), hl(ri, 6), sy * 32, sy * 35.5, ri - 0.4, ri, W)
    elif kind == "C":                   # tanks and radiator fins
        for sy in (1, -1):
            capsule(mb, F.p(-hl(ri, 12), sy * 18, ri - 7.5), F.ex, 6.0, 2 * hl(ri, 12) - 6, T, 16)
            for x in (-hl(ri, 18), 0.0, hl(ri, 18)):
                lbox(mb, F, x - 1.5, x + 1.5, sy * 12, sy * 24, ri - 3.0, ri, D)
        lbox(mb, F, -hl(ri, 6), hl(ri, 6), -6, 6, ri - 1.2, ri, D)
        xs = [-hl(ro, 10) + k * 6.0 for k in range(int(2 * hl(ro, 10) // 6) + 1)]
        for x in xs:
            lbox(mb, F, x - 0.35, x + 0.35, -15, 15, ro + 0.9, ro + 9.5, G)
        cyl(mb, F.p(xs[0] - 1, 0, ro + 9.8), F.p(xs[-1] + 1, 0, ro + 9.8), 1.1, PI, 10)
    else:                               # spoke junction: hub block on the inner face
        lbox(mb, F, -24, 24, -30, 30, ri - 13, ri, D)
        lbox(mb, F, -24.4, 24.4, -30.4, 30.4, ri - 13, ri - 11.6, H)
        for sy in (1, -1):
            lbox(mb, F, -20, 20, sy * 30, sy * 30.4, ri - 9, ri - 3, W)
        for sx in (1, -1):
            lbox(mb, F, sx * 24, sx * 24.4, -26, 26, ri - 9, ri - 3, W)
        lathe(mb, F.p(0, 0, ri - 13), -F.ez, [(26, 0, D), (26, 3, H), (21, 6, D), (14, 18, D), (14, 22, D), (0, 22, D)], D, 20)
        # nav mast on the outer face, floods down the spoke
        beam(mb, F.p(0, 0, ro), F.p(0, 0, ro + 16), 1.2, D)
        nav(mb, hp, F.p(0, 0, ro + 17), 2.4)
        for sx in (1, -1):
            for sy in (1, -1):
                flood(mb, hp, F.p(sx * 21, sy * 27, ri - 14), F.p(0, 0, 330))
    # chamfer greebles (outer chamfers), random
    for sy in (1, -1):
        cz, cy = (R_OUT - CH / 2) * CS, sy * (HW - CH / 2)
        nrm = (F.ey * sy + F.ez).normalized()
        up = nrm.cross(F.ex).normalized()
        for k in range(rng.randint(1, 3)):
            x = rng.uniform(-hl(ro, 14), hl(ro, 14))
            ln, wd, ht = rng.uniform(4, 12), rng.uniform(2, 4.5), rng.uniform(1.0, 2.6)
            with proj(mb, F.ex, up, nrm):
                obox(mb, F.p(x, cy, cz) + nrm * (ht / 2), F.ex, up, nrm, ln / 2, wd / 2, ht / 2, rng.choice((D, D, P, PT)))


def build_hab_ring(coll):
    rng = random.Random(11)
    mb = MB("Hab_Ring")
    hp = new_hp()
    pattern = ("S", "A", "B", "C", "B", "A")
    for i in range(HN):
        am = 2 * HA * i
        F = rframe(am)
        kind = pattern[(i - 3) % 6]
        with proj(mb, F.ex, F.ey, -F.ez):
            hab_section(mb, hp, F, i, kind, rng)
        # joint collar between this section and the next
        Fj = rframe(am + HA)
        with proj(mb, Fj.ex, Fj.ey, -Fj.ez):
            jp = hab_prof(R_IN - 2.5, R_OUT + 2.5, HW + 2.5, CH + 1.0)
            ring_prism(mb, Fj, jp, [D, D, D, H if i % 3 == 0 else D, D, D, D, D], hl=3.2)
            for sy in (1, -1):
                lamp(mb, Fj.p(0, sy * 24, R_OUT + 3.0), 1.6)
                lamp(mb, Fj.p(0, sy * (HW + 2.9), R_OUT - 16), 1.6)
                lamp(mb, Fj.p(0, sy * (HW + 2.9), R_IN + 16), 1.6)
    # nav lights on the ring's sides at the four cardinal points
    for i in (0, 9, 18, 27):
        F = rframe(2 * HA * i)
        for sy in (1, -1):
            nav(mb, hp, F.p(8, sy * (HW + 1.0), (R_OUT - 14) * CS), 2.0)
    # ---- the hub collar (inner radius 125 m: the 240 m drum sits inside it)
    lathe(mb, (0, -COLLAR_HL, 0), Y, [(COLLAR_IN, 0, D), (COLLAR_OUT - 4, 0, D), (COLLAR_OUT, 4, P),
                                       (COLLAR_OUT, 2 * COLLAR_HL - 4, D), (COLLAR_OUT - 4, 2 * COLLAR_HL, D),
                                       (COLLAR_IN, 2 * COLLAR_HL, D)], P, segs=48, closed=True)
    for yb, m in ((-11.0, D), (11.0, D), (0.0, H)):
        ring(mb, (0, yb, 0), Y, COLLAR_OUT - 1, COLLAR_OUT + 2.5, 1.4 if m == H else 4.0, m, 48)
    for k in range(12):                 # hydraulic clamp jaws on both faces
        F = rframe(math.radians(k * 30))
        with proj(mb, F.ex, F.ey, -F.ez):
            for sy in (1, -1):
                lbox(mb, F, -5, 5, sy * COLLAR_HL, sy * (COLLAR_HL + 5), COLLAR_IN + 1, COLLAR_IN + 15, D)
                lbox(mb, F, -5.4, 5.4, sy * (COLLAR_HL + 4.6), sy * (COLLAR_HL + 5.4), COLLAR_IN + 1, COLLAR_IN + 4, H)
                cyl(mb, F.p(-2.5, sy * (COLLAR_HL + 2.5), COLLAR_IN + 15), F.p(-2.5, sy * (COLLAR_HL + 1.0), COLLAR_OUT - 1), 1.0, PI, 8)
                cyl(mb, F.p(2.5, sy * (COLLAR_HL + 2.5), COLLAR_IN + 15), F.p(2.5, sy * (COLLAR_HL + 1.0), COLLAR_OUT - 1), 1.0, PI, 8)
                lamp(mb, F.p(0, sy * (COLLAR_HL + 5.4), COLLAR_IN + 12), 1.2)
    # ---- spokes: elevator tube inside a square truss, pods, fairings at both ends
    ri = R_IN * CS
    for i in SPOKES:
        F = rframe(2 * HA * i)
        z0, z1 = COLLAR_OUT + 10, ri - 13
        with proj(mb, F.ex, F.ey, -F.ez):
            lbox(mb, F, -17, 17, -COLLAR_HL - 1, COLLAR_HL + 1, COLLAR_OUT - 4, COLLAR_OUT + 10, D)
            lbox(mb, F, -17.4, 17.4, -COLLAR_HL - 1.4, COLLAR_HL + 1.4, COLLAR_OUT + 6, COLLAR_OUT + 7.4, H)
            lathe(mb, F.p(0, 0, COLLAR_OUT + 10), F.ez, [(16, 0, D), (16, 2, D), (9, 14, D), (9, 16, D), (0, 16, D)], D, 16)
            cyl(mb, F.p(0, 0, z0), F.p(0, 0, z1), 6.0, P, 16)
            for z in range(int(z0) + 30, int(z1) - 10, 45):
                lathe(mb, F.p(0, 0, z), F.ez, [(6.8, 0), (6.8, 2.5)], D, 16)
            truss(mb, F.p(0, 0, z0 + 18), F.p(0, 0, z1 - 23), 24, 24, 26, 1.7, D, PT, up=F.ey)
            for sx in (1, -1):
                lbox(mb, F, sx * 6.2, sx * 7.2, -1.0, 1.0, z0, z1, D)            # elevator rails
            for zc in (z0 + 70 + rng.uniform(0, 70), z1 - 60 - rng.uniform(0, 80)):
                lbox(mb, F, -6.5, 6.5, -7.5, 7.5, zc - 8, zc + 8, PT)
                lbox(mb, F, -7.2, 7.2, -8.2, 8.2, zc - 9.5, zc - 8, D)
                lbox(mb, F, -7.2, 7.2, -8.2, 8.2, zc + 8, zc + 9.5, D)
                for sy in (1, -1):
                    lbox(mb, F, -5, 5, sy * 7.5, sy * 7.9, zc - 4, zc + 4, W)
                lamp(mb, F.p(0, 8.6, zc + 9), 1.0)
            for z in range(int(z0) + 40, int(z1) - 20, 52):
                for (sx, sy) in ((1, 1), (-1, -1)):
                    lamp(mb, F.p(sx * 13.2, sy * 13.2, z), 1.2)
        for sx in (1, -1):
            flood(mb, hp, F.p(sx * 15, COLLAR_HL + 2, COLLAR_OUT + 11), F.p(0, 0, 330))
    # ---- solar wings on the collar
    tilt = math.radians(22)
    for i in WINGS:
        F = rframe(2 * HA * i)
        vdir = (F.ex * math.cos(tilt) + F.ey * math.sin(tilt)).normalized()
        wdir = F.ez.cross(vdir).normalized()
        with proj(mb, F.ex, F.ey, -F.ez):
            lbox(mb, F, -9, 9, -12, 12, COLLAR_OUT - 3, COLLAR_OUT + 8, D)
            lbox(mb, F, -9.4, 9.4, -12.4, 12.4, COLLAR_OUT + 4, COLLAR_OUT + 5.2, H)
            truss(mb, F.p(0, 0, COLLAR_OUT + 8), F.p(0, 0, 432), 8, 8, 16, 1.1, D, PT, up=F.ey)
            lbox(mb, F, -4, 4, -4, 4, 432, 440, D)
            for sy in (1, -1):                         # radiator fins near the root
                with proj(mb, F.ez, F.ey, F.ex):
                    obox(mb, F.p(0, sy * 19, 186), F.ez, F.ey, F.ex, 18, 13, 0.3, G)
                cyl(mb, F.p(0, sy * 32.4, 166), F.p(0, sy * 32.4, 206), 0.9, PI, 8)
                beam(mb, F.p(0, sy * 4, 168), F.p(0, sy * 32, 168), 0.8, D)
                beam(mb, F.p(0, sy * 4, 204), F.p(0, sy * 32, 204), 0.8, D)
        for (z0, z1) in ((214.0, 284.0), (288.0, 358.0), (362.0, 428.0)):
            zc, hz = (z0 + z1) / 2, (z1 - z0) / 2
            for sv in (1, -1):
                c = F.p(0, 0, zc) + vdir * (sv * 21.5)
                with proj(mb, F.ez, vdir, wdir):
                    obox(mb, c, F.ez, vdir, wdir, hz, 16.5, 0.25, SOL)
                e0 = F.p(0, 0, z0) + vdir * (sv * 38.4)
                e1 = F.p(0, 0, z1) + vdir * (sv * 38.4)
                beam(mb, e0, e1, 0.9, D, up=wdir)
                beam(mb, F.p(0, 0, z0) + vdir * (sv * 4), e0, 0.7, D, up=wdir)
                beam(mb, F.p(0, 0, z1) + vdir * (sv * 4), e1, 0.7, D, up=wdir)
        nav(mb, hp, F.p(0, 0, 441), 2.0)
        for sv in (1, -1):
            nav(mb, hp, F.p(0, 0, 428) + vdir * (sv * 39.4), 1.4)
    obj = mb.build(coll)
    return [obj], hp, {"kind": "space"}


# ================================================================== military pieces
OUT = -1.0     # the outer (armoured) side is Blender -Y (Unity -Z)


def build_radar_dome(coll):
    rng = random.Random(31)
    mb = MB("Mil_RadarDome")
    hp = new_hp()
    up = ((0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1))
    APO, TOP = 22.5, 12.0
    prism(mb, octagon(26.0), *up, SKIRT, GZ, D)                             # foundation skirt
    box(mb, -8, 8, 24, 38, SKIRT, GZ, D)                                    # vehicle apron (+Y front)
    for x in (-8.4, 8.0):
        box(mb, x, x + 0.4, 25, 38, GZ, GZ + 0.08, H)
    box(mb, -8, 8, 37.6, 38.0, GZ, GZ + 0.08, H)
    prism(mb, octagon(APO + 0.35), *up, GZ, GZ + 1.3, H)                   # hazard plinth
    prism(mb, octagon(APO), *up, GZ, TOP - 1.4, P)                         # walls
    prism(mb, octagon(APO + 0.8), *up, TOP - 1.4, TOP + 0.6, D)            # cornice; its top is the roof
    oct_poly = octagon(APO)
    for k, (c, n, ln) in enumerate(poly_faces(oct_poly)):
        o = (c.x - n.x * APO, c.y - n.y * APO)
        ang = round(math.degrees(math.atan2(n.y, n.x))) % 360
        if ang == 90:                       # vehicle door on the front
            fbox(mb, n, APO, -6.6, 6.6, GZ, GZ + 8.6, 0, 0.5, H)
            fbox(mb, n, APO, -5.5, 5.5, GZ, GZ + 7.5, 0, 0.7, D)
            for k2 in range(4):
                u = -5.5 + 2.75 * k2
                fbox(mb, n, APO, u + 0.3, u + 2.45, GZ + 2.5, GZ + 3.2, 0.7, 0.9, P)
                fbox(mb, n, APO, u + 0.3, u + 2.45, GZ + 5.0, GZ + 5.7, 0.7, 0.9, P)
            fbox(mb, n, APO, -7.5, 7.5, GZ + 8.6, GZ + 9.2, 0, 2.6, D)      # canopy
            for u in (-5.0, 0.0, 5.0):
                lamp(mb, V((0, APO + 2.0, GZ + 8.3)) + V((u, 0, 0)), 0.7)
        elif ang == 180:                    # personnel door
            fbox(mb, n, APO, -2.2, 2.2, GZ, GZ + 4.6, 0, 0.4, H)
            fbox(mb, n, APO, -1.6, 1.6, GZ, GZ + 4.0, 0, 0.55, D)
            fbox(mb, n, APO, -2.8, 2.8, GZ + 4.6, GZ + 5.0, 0, 1.8, D)
            lamp(mb, V((-APO - 1.2, 0, GZ + 4.4)), 0.6)
            fbox(mb, n, APO, 4, 7.5, 6, 8, 0, 0.3, W)
        else:
            fbox(mb, n, APO, -7.0, 7.0, 6.0, 8.0, 0, 0.3, W if ang not in (270, 315) else D)
            fbox(mb, n, APO, -4.0, 4.0, 2.0, 4.0, 0, 0.35, G)
        # corner pilasters at the octagon's vertices
        vtx = V((*oct_poly[(k + 1) % 8], 0))
        rad = vtx.normalized()
        fbox(mb, (rad.x, rad.y), vtx.length - 0.6, -0.9, 0.9, GZ, TOP - 1.4, 0, 1.0, D)
    # ring beam round the radome's foot
    ring(mb, (0, 0, TOP + 1.5), (0, 0, 1), 18.0, 20.8, 1.8, D, 40)
    ring(mb, (0, 0, TOP + 2.55), (0, 0, 1), 18.0, 20.9, 0.3, H, 40)
    # geodesic radome: icosphere, panels inset (seams as raised frames)
    RC, RR = V((0, 0, 24.0)), 22.5
    tb = bmesh.new()
    bmesh.ops.create_icosphere(tb, subdivisions=3, radius=RR)
    dead = [f for f in tb.faces if all(v.co.z + RC.z < TOP for v in f.verts)]
    bmesh.ops.delete(tb, geom=dead, context="FACES")
    loose = [v for v in tb.verts if not v.link_faces]
    bmesh.ops.delete(tb, geom=loose, context="VERTS")
    panels = list(tb.faces)
    res = bmesh.ops.inset_individual(tb, faces=panels, thickness=0.32, depth=-0.18, use_even_offset=True)
    seams = set(res["faces"])
    vm = {v: mb.vert(RC + v.co) for v in tb.verts}
    for f in tb.faces:
        fc = sum((v.co for v in f.verts), V()) / len(f.verts)
        if f in seams:
            m = P
        else:
            m = P if rng.random() < 0.07 else T
        mb.face([vm[v] for v in f.verts], m, want=fc if f not in seams else None)
    tb.free()
    beam(mb, RC + V((0, 0, RR - 0.4)), RC + V((0, 0, RR + 4.5)), 0.4, D)
    nav(mb, hp, RC + V((0, 0, RR + 5.2)), 1.0, "white")
    # roof units and floods on the cornice corners
    for g in (45, 135, 225, 315):
        a = math.radians(g)
        x, y = math.cos(a) * 21.6, math.sin(a) * 21.6
        box(mb, x - 1.4, x + 1.4, y - 1.4, y + 1.4, TOP + 0.6, TOP + 2.2, D if g != 315 else P)
        flood(mb, hp, (math.cos(a) * 23.6, math.sin(a) * 23.6, TOP + 1.4),
              (math.cos(a) * 55, math.sin(a) * 55, 0))
    # lattice mast with a dish, its equipment shelter and a cable tray to the building
    MX, MY = 36.0, -14.0
    box(mb, MX - 7, MX + 7, MY - 7, MY + 7, SKIRT, GZ, D)
    box(mb, MX - 4.6, MX + 4.6, MY - 4.6, MY + 4.6, GZ, GZ + 0.9, D)
    half = lattice_mast(mb, hp, MX, MY, 44.0, 3.6, 1.2)
    dc = V((MX - 3.2, MY + 3.0, 36.0))
    dish(mb, dc, (-0.55, 0.65, 0.5), 5.0)
    beam(mb, V((MX, MY, 36.0)), dc - V((-0.55, 0.65, 0.5)).normalized() * 1.4, 0.5, D)
    for z in (24.0, 30.0):
        box(mb, MX + half(z) - 0.2, MX + half(z) + 0.8, MY - 0.8, MY + 0.8, z - 1.6, z + 1.6, P)
    nav(mb, hp, (MX + half(28) + 0.6, MY, 28), 0.8)
    nav(mb, hp, (MX - half(28) - 0.6, MY, 28), 0.8)
    box(mb, MX - 4, MX + 4, MY + 7, MY + 13, SKIRT, GZ, D)
    box(mb, MX - 3.5, MX + 3.5, MY + 7.5, MY + 12.5, GZ, GZ + 3.6, PT)
    box(mb, MX - 3.8, MX + 3.8, MY + 7.2, MY + 12.8, GZ + 3.6, GZ + 4.0, D)
    wall_box(mb, "+y", MY + 12.5, MX - 1, MX + 1, GZ, GZ + 2.6, 0, 0.2, D)
    beam(mb, (APO - 0.2, -10, 8.5), (MX - 3.6, MY, 8.5), 0.9, PT, h=0.5)
    for x in (27.5, 32.0):
        beam(mb, (x, -10 - (x - APO) * 4 / (MX - APO), GZ), (x, -10 - (x - APO) * 4 / (MX - APO), 8.2), 0.5, D)
    # generator and fuel tank on the -X side, blast barriers by the door
    box(mb, -40, -26, -10, 6, SKIRT, GZ, D)
    box(mb, -38, -30, -8, -1, GZ, GZ + 4.2, P)
    box(mb, -38.3, -29.7, -8.3, -0.7, GZ + 4.2, GZ + 4.6, D)
    wall_box(mb, "+y", -1, -37, -31, GZ + 1, GZ + 3, 0, 0.2, G)
    lathe_z(mb, -32, -3, [(0, GZ + 4.6, D), (0.8, GZ + 4.6, D), (0.8, GZ + 8, D), (0, GZ + 8, D)], D, 8)
    capsule(mb, (-37, 3.0, GZ + 2.4), (1, 0, 0), 1.9, 8.0, T, 14)
    for x in (-35.0, -30.0):
        box(mb, x - 0.5, x + 0.5, 1.4, 4.6, GZ, GZ + 1.0, D)
    for x in (-14.0, -11.0, 11.0, 14.0):
        box(mb, x - 1.2, x + 1.2, 25.6, 26.4, GZ, GZ + 1.1, H if x < 0 else P)
    obj = mb.build(coll)
    return [obj], hp, {"kind": "surface"}


# ---- the wall's section, outer side first written as +o, then o -> y = OUT * o
WALL_HALF = 60.0
WALL_PROF = [(-7.0, GZ, D), (7.0, GZ, P), (5.2, 13.0, D), (4.2, 13.0, P), (3.6, 23.6, D), (4.4, 23.6, D),
             (4.4, 26.0, D), (2.6, 26.0, D), (2.6, 25.5, G), (-5.6, 25.5, H), (-5.6, 25.9, H), (-6.4, 25.9, P),
             (-6.4, 21.0, D), (-7.0, 20.0, P)]   # (o, z, material of the edge to the next point)
WALKWAY = 25.5
WALK_O = (-5.6, 2.6)                             # walkway between these (o: + outward)


def oy(o):
    return OUT * o


def wall_body(mb, x0, x1):
    poly = [(oy(o), z) for (o, z, _) in WALL_PROF]
    mats = [m for (_, _, m) in WALL_PROF]
    prism(mb, poly, (0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0), x0, x1, P, caps=(D, D), edge_mats=mats)


def face_o(z):
    """Outer face's o at height z (on the battered faces)."""
    if z <= 13.0:
        return 7.0 + (5.2 - 7.0) * (z - GZ) / (13.0 - GZ)
    return 4.2 + (3.6 - 4.2) * (z - 13.0) / (23.6 - 13.0)


def slope_plate(mb, x0, x1, z0, z1, mat=D, thick=0.35):
    """Armour plate lying on the outer battered face between heights z0..z1."""
    a0, a1 = face_o(z0) + 0.02, face_o(z1) + 0.02
    pts = [V((x0, oy(a0), z0)), V((x1, oy(a0), z0)), V((x1, oy(a1), z1)), V((x0, oy(a1), z1))]
    sl = (pts[3] - pts[0]).normalized()
    nrm = V((1, 0, 0)).cross(sl).normalized()
    if nrm.y * OUT < 0:
        nrm = -nrm
    c = (pts[0] + pts[2]) / 2 + nrm * (thick / 2)
    with proj(mb, (1, 0, 0), sl, nrm):
        obox(mb, c, (1, 0, 0), sl, nrm, (x1 - x0) / 2, (pts[3] - pts[0]).length / 2, thick / 2, mat)


def railing(mb, x0, x1, y, z, step=3.0, h=1.1, mat=PT, gaps=()):
    n = max(1, round((x1 - x0) / step))
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    for x in xs:
        if any(g0 - 0.1 <= x <= g1 + 0.1 for (g0, g1) in gaps):
            continue
        box(mb, x - 0.09, x + 0.09, y - 0.09, y + 0.09, z, z + h, mat)
    runs = [(x0, x1)]
    for (g0, g1) in gaps:
        runs = [r for (a, b) in runs for r in ((a, min(b, g0)), (max(a, g1), b)) if r[1] - r[0] > 0.5]
    for (a, b) in runs:
        box(mb, a - 0.1, b + 0.1, y - 0.11, y + 0.11, z + h - 0.12, z + h + 0.06, mat)
        box(mb, a, b, y - 0.07, y + 0.07, z + h * 0.5 - 0.06, z + h * 0.5 + 0.06, mat)


def build_wall_segment(coll):
    rng = random.Random(41)
    mb = MB("Mil_WallSegment")
    hp = new_hp()
    X0, X1 = -WALL_HALF, WALL_HALF
    box(mb, X0, X1, oy(9.5), oy(-8.0), SKIRT, GZ, D)                           # footing / skirt
    wall_box(mb, "-y", oy(9.5), X0, X1, -1.4, GZ - 0.05, 0, 0.3, H)           # outer kerb stripe
    wall_body(mb, X0, X1)
    # battlements along the parapet (spaced so they continue across butted segments)
    for k in range(12):
        xc = X0 + 5 + 10 * k
        box(mb, xc - 2.6, xc + 2.6, oy(4.4), oy(2.4), 26.0, 28.0, D)
        box(mb, xc - 2.6, xc + 2.6, oy(4.6), oy(4.4), 26.9, 27.5, H)
        box(mb, xc - 0.9, xc + 0.9, oy(4.65), oy(4.4), 24.4, 25.6, D)          # gun port shutter
    # buttresses on the outer face
    BUT = (-45.0, -15.0, 15.0, 45.0)
    bprof = [(3.5, GZ), (11.0, GZ), (11.0, 3.0), (5.6, 19.5), (3.5, 19.5)]
    for xb in BUT:
        prism(mb, [(oy(o), z) for (o, z) in bprof], (0, 0, 0), (0, 1, 0), (0, 0, 1), (1, 0, 0), xb - 2.3, xb + 2.3, P,
              caps=(P, P), edge_mats=[D, P, P, D, D])
        box(mb, xb - 2.5, xb + 2.5, oy(11.25), oy(10.95), GZ, GZ + 2.6, H)
        box(mb, xb - 2.6, xb + 2.6, oy(6.2), oy(3.4), 19.5, 20.3, D)
        flood(mb, hp, (xb, oy(6.3), 21.3), (xb, oy(60.0), 0.0))
        lamp(mb, (xb, oy(8.6), 11.0), 0.8)
    # armour plates on the battered faces between the buttresses
    edges = [X0] + [b for xb in BUT for b in (xb - 2.3, xb + 2.3)] + [X1]
    for (a, b) in zip(edges[0::2], edges[1::2]):
        ln = b - a
        n = max(1, round(ln / 12))
        for k in range(n):
            pa, pb = a + ln * k / n + 1.0, a + ln * (k + 1) / n - 1.0
            slope_plate(mb, pa, pb, 2.5, 11.5, D if rng.random() > 0.2 else P)
            if rng.random() < 0.7:
                slope_plate(mb, pa + 1.0, pb - 1.0, 14.5, 21.5, D)
    # lamps under the parapet lip, outer side
    for k in range(8):
        lamp(mb, (X0 + 7.5 + 15 * k, oy(4.6), 23.1), 0.7)
    # walkway: grating deck, inner railing (gap at the stair head), lamps on the parapet
    box(mb, X0, X1, oy(2.6), oy(-5.6), WALKWAY, WALKWAY + 0.12, G)
    STAIR_X = (-14.0, -10.0)
    railing(mb, X0 + 0.5, X1 - 0.5, oy(-6.0), 25.9, gaps=[STAIR_X])
    for k in range(6):
        lamp(mb, (X0 + 5 + 20 * k, oy(2.25), 26.9), 0.5)
    # inner face: pilasters, gallery slit windows, conduits, doors
    IN = oy(-7.0)                                  # inner face y
    iside = "+y" if OUT < 0 else "-y"
    pil = [X0 + 7.5 + 15 * k for k in range(8)]
    for x in pil:
        wall_box(mb, iside, IN, x - 1.2, x + 1.2, GZ, 21.0, 0, 1.2, D)
    for (a, b) in zip(pil, pil[1:]):
        wall_box(mb, iside, IN, a + 2.2, b - 2.2, 16.0, 18.0, 0, 0.3, W)
    wall_box(mb, iside, IN, X0 + 1, pil[0] - 2.2, 16.0, 18.0, 0, 0.3, W)
    wall_box(mb, iside, IN, pil[-1] + 2.2, X1 - 1, 16.0, 18.0, 0, 0.3, W)
    for (z, r) in ((12.0, 0.65), (13.5, 0.45)):
        cyl(mb, (X0, oy(-7.9), z), (X1, oy(-7.9), z), r, PI, 10)
    for x in pil[1::3]:
        wall_box(mb, iside, IN, x - 1.4, x + 1.4, 11.0, 14.4, 1.2, 1.6, D)    # conduit clamps
    for xd in (-30.0, 30.0):
        wall_box(mb, iside, IN, xd - 2.2, xd + 2.2, GZ, GZ + 5.0, 0, 0.35, H)
        wall_box(mb, iside, IN, xd - 1.6, xd + 1.6, GZ, GZ + 4.4, 0, 0.5, D)
        wall_box(mb, iside, IN, xd - 2.8, xd + 2.8, GZ + 5.0, GZ + 5.4, 0, 2.0, PT)
        lamp(mb, wall_pt(iside, IN, xd, GZ + 4.8, 1.4), 0.6)
        wall_box(mb, iside, IN, xd + 3.0, xd + 4.0, GZ + 1.6, GZ + 3.0, 0, 0.4, P)   # door panel
    # switchback stair on the inner face up to the walkway
    lane1 = sorted((oy(-8.4), oy(-11.0)))
    lane2 = sorted((oy(-11.2), oy(-13.8)))
    ys = (min(lane1[0], lane2[0]), max(lane1[1], lane2[1]))
    xa, xb = -10.0, 8.0
    zm = 13.0
    for (p0, p1, ln) in (((xa, GZ), (xb, zm), lane1), ((xb, zm), (xa, WALKWAY - 0.1), lane2)):
        yc = (ln[0] + ln[1]) / 2
        beam(mb, (p0[0], yc, p0[1]), (p1[0], yc, p1[1]), ln[1] - ln[0], G, h=0.35, up=(0, 0, 1))
        for yy in ln:
            beam(mb, (p0[0], yy, p0[1] + 1.1), (p1[0], yy, p1[1] + 1.1), 0.22, PT)
            beam(mb, (p0[0], yy, p0[1] - 0.2), (p1[0], yy, p1[1] - 0.2), 0.5, PT, h=0.7)
    box(mb, xb, xb + 4, ys[0], ys[1], zm - 0.35, zm, G)                       # mid landing
    box(mb, STAIR_X[0], xa, lane2[0], lane2[1], WALKWAY - 0.35, WALKWAY, G)  # top landing
    box(mb, STAIR_X[0], STAIR_X[1], oy(-5.6), lane2[0], WALKWAY - 0.35, WALKWAY + 0.1, G)
    for (x, z) in ((xb + 3.6, zm), (STAIR_X[0] + 0.4, WALKWAY), (xa - 0.6, WALKWAY), (xb + 0.4, zm)):
        for yy in ys:
            box(mb, x - 0.2, x + 0.2, yy - 0.2, yy + 0.2, GZ, z - 0.35, D)
    lamp(mb, (xb + 2, (ys[0] + ys[1]) / 2, zm + 3.5), 0.6)
    obj = mb.build(coll)
    return [obj], hp, {"kind": "surface"}


def walkway_door(mb, side, wall, u0=-1.8, u1=1.8):
    """Door from a wall's walkway into a tower face (u is the face's horizontal coordinate)."""
    wall_box(mb, side, wall, u0 - 0.6, u1 + 0.6, WALKWAY, WALKWAY + 4.2, 0, 0.35, H)
    wall_box(mb, side, wall, u0, u1, WALKWAY, WALKWAY + 3.6, 0, 0.5, D)
    lamp(mb, wall_pt(side, wall, (u0 + u1) / 2, WALKWAY + 4.6, 0.6), 0.6)


def tower_core(mb, hp, cx, cy, hx, hy, z1, top, socket_r, rng, ends=("+x", "-x", "+y", "-y"), skirt=True,
               plates=True):
    """Fortress tower block centred (cx, cy), footprint 2hx x 2hy: vertical lower stage to z1,
    window stage, overhanging gallery, battlements and a turret socket disc whose top is `top`.
    Returns the gallery top height."""
    S1 = chamfer_rect(cx, cy, hx, hy, 3.0)
    loft(mb, [(chamfer_rect(cx, cy, hx + 0.35, hy + 0.35, 3.1), GZ, H), (chamfer_rect(cx, cy, hx + 0.35, hy + 0.35, 3.1), GZ + 1.3, H)])
    if skirt:
        box(mb, cx - hx - 2, cx + hx + 2, cy - hy - 2, cy + hy + 2, SKIRT, GZ, D)
    loft(mb, [(S1, GZ, P), (S1, z1, P)])
    loft(mb, [(chamfer_rect(cx, cy, hx + 0.9, hy + 0.9, 3.4), z1 - 0.4, D), (chamfer_rect(cx, cy, hx + 0.9, hy + 0.9, 3.4), z1 + 1.4, D)])
    hx2, hy2 = hx - 2.5, hy - 2.5
    zg = top - 10.0                      # gallery bottom
    S2 = chamfer_rect(cx, cy, hx2, hy2, 6.0)
    loft(mb, [(S2, z1 + 1.4, P), (S2, zg, P)])
    SG0, SG1 = chamfer_rect(cx, cy, hx2 + 0.5, hy2 + 0.5, 6.2), chamfer_rect(cx, cy, hx + 0.5, hy + 0.5, 7.0)
    zt = top - 4.5                       # gallery top / roof
    loft(mb, [(SG0, zg, D), (SG1, zg + 1.2, D), (SG1, zt, D)])
    # corner pilasters on the lower stage
    for (c, n, ln) in poly_faces(S1)[1::2]:
        fbox(mb, n, 0, -1.2, 1.2, GZ, z1 - 0.4, 0, 0.8, D, o=(c.x, c.y))
    # armour plates on the lower stage, clear of the walls that run into the tower
    if plates:
        for (c, n, ln) in poly_faces(S1)[0::2]:
            for sg in (1, -1):
                a, b = sorted((sg * 10.8, sg * (ln / 2 - 1.6)))
                fbox(mb, n, 0, a, b, 4.0, 15.0, 0, 0.4, D, o=(c.x, c.y))
                fbox(mb, n, 0, a, b, 16.0, 27.0, 0, 0.4, D if rng.random() > 0.3 else P, o=(c.x, c.y))
                fbox(mb, n, 0, a + 1.0, b - 1.0, 20.0, 22.0, 0.4, 0.7, W, o=(c.x, c.y))
    # window slits on the window stage, gallery band all round
    for (c, n, ln) in poly_faces(S2):
        if ln > 10:
            span = ln / 2 - 2.5
            for (z0, zz1) in ((z1 + 4.0, z1 + 6.0), (z1 + 10.0, z1 + 12.0)):
                if zz1 > zg - 1:
                    continue
                m = 3 if ln > 22 else 2
                for k in range(m):
                    a = -span + 2 * span * k / m + 0.8
                    b = -span + 2 * span * (k + 1) / m - 0.8
                    fbox(mb, n, 0, a, b, z0, zz1, 0, 0.3, W if rng.random() > 0.1 else D, o=(c.x, c.y))
            lamp(mb, V((c.x, c.y, zg - 0.8)) + n * 0.6, 0.7)
    for (c, n, ln) in poly_faces(SG1):
        span = ln / 2 - 1.2
        fbox(mb, n, 0, -span, span, zg + 2.5, zg + 4.5, 0, 0.3, W, o=(c.x, c.y))
    # walkway doors on the end faces
    for s in ends:
        if s in ("+x", "-x"):
            walkway_door(mb, s, cx + (hx if s == "+x" else -hx), cy - 1.8, cy + 1.8)
        else:
            walkway_door(mb, s, cy + (hy if s == "+y" else -hy), cx - 1.8, cx + 1.8)
    # battlements and the turret socket
    merlons(mb, SG1, zt, zt + 2.8, 1.4, 3.2, 6.0, D)
    lathe_z(mb, cx, cy, [(0, zt, D), (socket_r + 0.8, zt, D), (socket_r + 0.8, top - 1.2, D),
                         (socket_r + 0.8, top - 0.5, H), (socket_r + 0.8, top, D), (0, top, D)], D, 32)
    ring(mb, (cx, cy, top + 0.04), (0, 0, 1), socket_r - 0.6, socket_r, 0.08, H, 32)
    hp["sockets"].append((V((cx, cy, top)), socket_r))
    # floods at the gallery corners, aimed out and down
    for (c, n, ln) in poly_faces(SG1)[1::2]:
        flood(mb, hp, V((c.x, c.y, zg + 0.6)) + n * 1.0, V((c.x, c.y, 0)) + n * 45)
    return zt


def build_wall_tower(coll):
    rng = random.Random(51)
    mb = MB("Mil_WallTower")
    hp = new_hp()
    TOP = 55.0
    zt = tower_core(mb, hp, 0, 0, 20.0, 20.0, 31.0, TOP, 12.0, rng)
    # roof: hatch, a vent stack, the beacon mast in one corner
    box(mb, -16, -12, 10, 14, zt, zt + 2.2, D)
    box(mb, 11, 15, -15, -11, zt, zt + 1.6, P)
    beam(mb, (15.2, 15.2, zt), (15.2, 15.2, TOP + 7), 0.7, D)
    nav(mb, hp, (15.2, 15.2, TOP + 7.8), 1.2, "white")
    for (x, y) in ((-16.3, -16.3), (16.3, -16.3), (-16.3, 16.3)):
        beam(mb, (x, y, zt), (x, y, zt + 4.2), 0.4, D)
        nav(mb, hp, (x, y, zt + 4.7), 0.8, "red")
    obj = mb.build(coll)
    hp["padCentre"] = V((0, 0, TOP))
    hp["padRadius"] = 12.0
    hp["deckHeight"] = TOP
    return [obj], hp, {"kind": "surface"}


def build_gate(coll):
    rng = random.Random(61)
    mb = MB("Mil_Gate")
    hp = new_hp()
    GW, GH, TW = 25.0, 30.0, 50.0              # gate half width, height; tower width
    XE = GW + TW                               # 75: the flat ends
    TY0, TY1 = oy(14.0), oy(-12.0)             # tower depth: 14 m out, 12 m in
    TYC, THY = (TY0 + TY1) / 2, abs(TY1 - TY0) / 2
    box(mb, -XE, XE, min(TY0, TY1) - 2, max(TY0, TY1) + 2, SKIRT, GZ, D)
    for s in (1, -1):
        tower_core(mb, hp, s * (GW + TW / 2), TYC, TW / 2, THY, 31.0, 50.0, 9.0, rng,
                   ends=("+x" if s > 0 else "-x",), skirt=False, plates=False)
        for fy_, fsd in ((min(TY0, TY1), "-y"), (max(TY0, TY1), "+y")):
            for (a, b) in ((35.0, 46.0), (54.0, 65.0)):
                xa, xb = sorted((s * a, s * b))
                wall_box(mb, fsd, fy_, xa, xb, 9.0, 18.0, 0, 0.4, D)
                wall_box(mb, fsd, fy_, xa, xb, 19.0, 28.0, 0, 0.4, D if (a > 50) == (s > 0) else P)
                wall_box(mb, fsd, fy_, xa + 1.0, xb - 1.0, 22.0, 24.0, 0.4, 0.7, W)
            wall_box(mb, fsd, fy_, *sorted((s * 32.0, s * 72.0)), 7.6, 8.4, 0, 0.3, PT)
        beam(mb, (s * (GW + TW / 2 + 19), TYC, 45.5), (s * (GW + TW / 2 + 19), TYC, 49.5), 0.4, D)
        nav(mb, hp, (s * (GW + TW / 2 + 19), TYC, 50.0), 1.0)
        beam(mb, (s * (GW + TW / 2 - 16), TYC + 7, 45.5), (s * (GW + TW / 2 - 16), TYC + 7, 56), 0.6, D)
        nav(mb, hp, (s * (GW + TW / 2 - 16), TYC + 7, 56.8), 1.0)
        # tower front: vertical hazard stripes either side of the gate, a lamp column
        fy = min(TY0, TY1) if OUT < 0 else max(TY0, TY1)
        fs = "-y" if OUT < 0 else "+y"
        wall_box(mb, fs, fy, s * (GW + 1.0), s * (GW + 3.5), GZ, 30.5, 0, 0.35, H)
        for z in (8.0, 16.0, 24.0):
            lamp(mb, wall_pt(fs, fy, s * (GW + 6.0), z, 0.5), 0.8)
        flood(mb, hp, (s * (GW + 8.0), fy - 1.0 * (1 if OUT < 0 else -1), 30.0), (s * 6.0, oy(60.0), 0.0))
        iy = max(TY0, TY1) if OUT < 0 else min(TY0, TY1)
        isd = "+y" if OUT < 0 else "-y"
        wall_box(mb, isd, iy, s * (GW + 3), s * (GW + 11), GZ, GZ + 6, 0, 0.4, H)     # inner vehicle door
        wall_box(mb, isd, iy, s * (GW + 3.6), s * (GW + 10.4), GZ, GZ + 5.4, 0, 0.6, D)
        lamp(mb, wall_pt(isd, iy, s * (GW + 7), GZ + 6.6, 0.6), 0.7)
        flood(mb, hp, (s * (GW + 8.0), iy + 1.0 * (1 if OUT < 0 else -1), 30.0), (s * 10.0, oy(-55.0), 0.0))
    # the gate: passage block, armoured leaves on both faces, frame, lintel
    DO, DI = 8.0, -8.0                          # door faces (o)
    box(mb, -GW, GW, oy(DO - 0.5), oy(DI + 0.5), GZ, GH, D)
    for face_o_, sgn in ((DO, 1), (DI, -1)):
        y0 = oy(face_o_ - 0.5 * sgn)
        y1 = oy(face_o_ + 1.8 * sgn)
        yf = oy(face_o_ + 1.8 * sgn)
        out_dir = OUT * sgn                     # +-1 in y: away from the passage
        for s in (1, -1):
            xa, xb = sorted((s * 0.25, s * (GW - 0.5)))
            box(mb, xa, xb, y0, y1, GZ, GH - 0.5, D)
            for row in range(4):
                for col in range(2):
                    za, zb = GZ + 4.0 + row * 6.2, GZ + 9.2 + row * 6.2
                    ua, ub = xa + 1.2 + col * (xb - xa - 1.2) / 2, xa + (col + 1) * (xb - xa - 1.2) / 2
                    box(mb, ua, ub, yf, yf + out_dir * 0.5, za, zb, P if (row + col + (s > 0)) % 3 else D)
            box(mb, xa, xb, yf, yf + out_dir * 0.6, GZ, GZ + 2.8, H)
        for z in (6.0, 12.0, 18.0, 24.0):
            lamp(mb, (0, yf + out_dir * 0.3, z), 0.6)
        # jambs and lintel front
        for s in (1, -1):
            box(mb, s * GW, s * (GW - 1.2), oy(face_o_), oy(face_o_ + 2.6 * sgn), GZ, GH + 4, H)
        box(mb, -GW, GW, oy(face_o_), oy(face_o_ + 2.6 * sgn), GH, GH + 1.2, H)
        for x in (-16.0, 0.0, 16.0):
            yb = oy(face_o_ + 2.6 * sgn) + out_dir * 0.6
            box(mb, x - 0.8, x + 0.8, yb - 0.6, yb + 0.6, GH + 1.6, GH + 3.0, GL)
            glow(hp, (x, yb + out_dir * 1.0, GH + 2.3))
    box(mb, -GW, GW, oy(DO + 2.6), oy(DI - 2.6), GH, GH + 4.0, D)            # lintel
    # bridge over the gate between the towers
    BZ0, BZ1 = GH + 4.0, GH + 13.0
    box(mb, -GW, GW, oy(9.0), oy(-9.0), BZ0, BZ1, P)
    for sd in ("+y", "-y"):
        wy = oy(9.0) if (sd == "-y") == (OUT < 0) else oy(-9.0)
        wall_box(mb, sd, wy, -GW + 2, GW - 2, BZ0 + 4, BZ0 + 6, 0, 0.3, W)
        wall_box(mb, sd, wy, -GW + 2, GW - 2, BZ0 + 2, BZ0 + 4, 0, 0.3, W if sd == ("+y" if OUT < 0 else "-y") else D)
        wall_box(mb, sd, wy, -GW, GW, BZ1 - 1.4, BZ1, 0, 0.5, D)
    box(mb, -GW, GW, oy(9.4), oy(-9.4), BZ1, BZ1 + 0.6, D)
    for k in range(5):
        xc = -20.0 + 10 * k
        box(mb, xc - 2.6, xc + 2.6, oy(9.4), oy(7.8), BZ1 + 0.6, BZ1 + 2.4, D)
    railing(mb, -GW + 0.5, GW - 0.5, oy(-9.0), BZ1 + 0.6)
    beam(mb, (0, oy(-4.0), BZ1 + 0.6), (0, oy(-4.0), BZ1 + 8.0), 0.5, D)
    nav(mb, hp, (0, oy(-4.0), BZ1 + 8.7), 1.0, "white")
    for x in (-15.0, 0.0, 15.0):
        lamp(mb, (x, oy(10.0), BZ0 + 0.4), 0.8)
        lamp(mb, (x, oy(-10.0), BZ0 + 0.4), 0.8)
    # road markings through the gate (on the skirt top)
    for x in (-12.0, 12.0):
        box(mb, x - 0.3, x + 0.3, min(TY0, TY1) - 2, oy(DO + 1.0), GZ, GZ + 0.06, H)
        box(mb, x - 0.3, x + 0.3, oy(DI - 1.0), max(TY0, TY1) + 2, GZ, GZ + 0.06, H)
    obj = mb.build(coll)
    return [obj], hp, {"kind": "surface"}


# ------------------------------------------------------------------ analysis helpers
def tri_count(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


def bounds(objs):
    pts = [v.co for o in objs for v in o.data.vertices]
    lo = V((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = V((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi, max(p.length for p in pts)


def nav_colour(p, colour=None):
    """Port (-X) red, starboard (+X) green, centre-line / top white (unless given)."""
    if colour:
        return colour
    if abs(p.x) < 6:
        return "white"
    return "green" if p.x > 0 else "red"


# ------------------------------------------------------------------ build + export
BUILDERS = [("Hab_Ring", build_hab_ring), ("Mil_RadarDome", build_radar_dome),
            ("Mil_WallSegment", build_wall_segment), ("Mil_WallTower", build_wall_tower), ("Mil_Gate", build_gate)]
NOTE = ("Unity coordinates in metres: Blender (x, y, z) -> Unity (x, z, y); Unity +Y up. Hab_Ring: origin at "
        "the ring's centre, the ring's axis along Unity Z (the wheel stands in the Unity XY plane), the hub collar's "
        "inner radius is 125 m. Military pieces: origin at ground centre, 6 m foundation skirt below y = 0, the outer "
        "(armoured) side faces Unity -Z and the inner side +Z; Mil_WallSegment is 120 m along X with flat ends at "
        "x = +-60, Mil_Gate is 150 m with flat ends at x = +-75. Mil_WallTower: padCentre / padRadius / deckHeight "
        "are its turret socket disc (top). sockets = every turret socket disc (p = top centre, radius). size = "
        "bounding box, radius = bounding sphere about the origin. Nav light colour: red port (-X), green starboard "
        "(+X), white centre/top (or as modelled).")

want = [n for n, _ in BUILDERS if not ONLY or n in ONLY]
need_line = bool(PREVIEW_DIR) and any(n in ("Mil_WallSegment", "Mil_WallTower", "Mil_Gate") for n in want)
BUILT = {}
for name, fn in BUILDERS:
    export_it = name in want
    if not export_it and not (need_line and name in ("Mil_WallSegment", "Mil_WallTower", "Mil_Gate")):
        continue
    coll = bpy.data.collections.new(name)
    scene.collection.children.link(coll)
    objs, hp, info = fn(coll)
    lo, hi, rad = bounds(objs)
    info.update(lo=lo, hi=hi, radius=rad, tris=tri_count(objs))
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
    print(f"MODEL {name}: tris={info['tris']} size(unity)={to_unity(sz)} radius={rad:.1f} "
          f"lo(blender)=({lo.x:.1f}, {lo.y:.1f}, {lo.z:.1f}) hi=({hi.x:.1f}, {hi.y:.1f}, {hi.z:.1f}) "
          f"mats={[m.name for o in objs for m in o.data.materials]}")
    print(f"  navLights={len(hp['navLights'])} floods={len(hp['floods'])} glows={len(hp['glows'])} "
          f"sockets={[(to_unity(p), r) for (p, r) in hp['sockets']]}")

# ------------------------------------------------------------------ hardpoints JSON
jpath = os.path.join(DATA_DIR, "settlement_hardpoints.json")
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
        "berthClearance": 0.0,
        "padCentre": {"p": to_unity(hp["padCentre"]) if "padCentre" in hp else [0, 0, 0]},
        "padRadius": hp.get("padRadius", 0.0),
        "deckHeight": hp.get("deckHeight", 0.0),
        "navLights": [{"p": to_unity(p), "colour": nav_colour(p, c)} for (p, c) in hp["navLights"]],
        "floods": [{"p": to_unity(p), "forward": [round(c, 4) for c in to_unity(d)]} for (p, d) in hp["floods"]],
        "glows": [{"p": to_unity(p)} for p in hp["glows"]],
        "sockets": [{"p": to_unity(p), "radius": r} for (p, r) in hp["sockets"]],
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
        bsdf.inputs["Metallic"].default_value = 0.0 if name in (H, PT) else 0.5
        if name in emis:
            c, s = emis[name]
            bsdf.inputs["Emission Color"].default_value = (*c, 1)
            bsdf.inputs["Emission Strength"].default_value = s
            continue
        if name == SOL:          # dark blue cells in a light grid, 6 cells per 9 m repeat
            uvn = nt.nodes.new("ShaderNodeUVMap")
            br = nt.nodes.new("ShaderNodeTexBrick")
            nt.links.new(uvn.outputs["UV"], br.inputs["Vector"])
            br.inputs["Scale"].default_value = 6.0
            br.inputs["Mortar Size"].default_value = 0.03
            br.offset = 0.0
            br.inputs["Color1"].default_value = (0.02, 0.05, 0.14, 1)
            br.inputs["Color2"].default_value = (0.03, 0.06, 0.17, 1)
            br.inputs["Mortar"].default_value = (0.6, 0.62, 0.66, 1)
            br.inputs["Brick Width"].default_value = 1.0
            br.inputs["Row Height"].default_value = 1.0
            nt.links.new(br.outputs["Color"], bsdf.inputs["Base Color"])
            bsdf.inputs["Roughness"].default_value = 0.25
            bsdf.inputs["Metallic"].default_value = 0.6
            continue
        if name == GLS:
            bsdf.inputs["Roughness"].default_value = 0.06
            bsdf.inputs["Metallic"].default_value = 0.3
            bsdf.inputs["Emission Color"].default_value = (0.2, 0.5, 0.45, 1)
            bsdf.inputs["Emission Strength"].default_value = 0.5
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
        sun("key", (-0.55, 0.6, -0.5), 6.0, (1.0, 0.96, 0.9))
        sun("fill", (0.6, -0.4, 0.4), 0.7, (0.6, 0.7, 1.0))
    else:
        bg.inputs["Color"].default_value = (0.42, 0.5, 0.62, 1)
        bg.inputs["Strength"].default_value = 0.7
        sun("key", (-0.6, 0.25, -0.75), 4.0, (1.0, 0.93, 0.82), 0.03)


def look(cam, tgt, d, dist):
    tgt, d = V(tgt), V(d).normalized()
    cam.location = tgt + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def instance(coll_src, extra, loc, rot_z=0.0):
    inst = bpy.data.objects.new("Inst_" + coll_src.name, None)
    inst.instance_type = "COLLECTION"
    inst.instance_collection = coll_src
    inst.location = loc
    inst.rotation_euler = (0, 0, rot_z)
    extra.objects.link(inst)
    return inst


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
    only_views = set(OPTS["views"].split(",")) if OPTS.get("views") else None

    def shoot(name, vname, tgt, d, dist, lens=35):
        if only_views and f"{name}_{vname}" not in only_views and vname not in only_views:
            return
        cam.data.lens = lens
        look(cam, tgt, d, dist)
        scene.render.filepath = os.path.join(PREVIEW_DIR, f"{name}_{vname}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDERED", scene.render.filepath)

    ground = None
    for name, (coll, objs, hp, info, export_it) in BUILT.items():
        if not export_it:
            continue
        scene.collection.children.link(coll)
        kind = info["kind"]
        set_env(kind)
        lo, hi = info["lo"], info["hi"]
        c = (lo + hi) / 2
        R = (hi - lo).length / 2
        tmp = []
        if kind == "space":
            hub = MB("HubPlaceholder")
            lathe(hub, (0, -180, 0), Y, [(0, 0), (120, 0), (120, 360), (0, 360)], P, 64)
            ho = hub.build(extra)
            ho.data.materials.clear()
            ho.data.materials.append(HUB_MAT)
            tmp.append(ho)
            shoot(name, "34", V((0, 0, 0)), (0.62, -1.0, 0.5), 2300)
            shoot(name, "face", V((0, 0, 0)), (0.04, -1.0, 0.07), 2500)
            shoot(name, "edge", V((0, 0, 0)), (1.0, 0.3, 0.25), 2400)
            shoot(name, "close", V((-60, 0, 520)), (0.55, -0.9, 0.45), 420)
            shoot(name, "hub", V((0, 0, 150)), (0.5, -0.8, 0.5), 520)
        else:
            bpy.ops.mesh.primitive_plane_add(size=8000, location=(0, 0, 0.02))
            ground = bpy.context.object
            for cl in ground.users_collection:
                cl.objects.unlink(ground)
            extra.objects.link(ground)
            ground.data.materials.append(GROUND_MAT)
            tmp.append(ground)
            # military pieces: the outer side is -Y; views from outside (-Y) and inside (+Y)
            shoot(name, "outer", c + V((0, 0, -4)), (0.8, -1.25, 0.55), R * 2.5)
            shoot(name, "inner", c + V((0, 0, -4)), (-0.75, 1.2, 0.6), R * 2.5)
        for o in tmp:
            bpy.data.objects.remove(o, do_unlink=True)
        scene.collection.children.unlink(coll)
    # a stretch of fort: towers at the corners, walls running into their centres, the gate between
    if need_line and all(n in BUILT for n in ("Mil_WallSegment", "Mil_WallTower", "Mil_Gate")):
        set_env("ground")
        bpy.ops.mesh.primitive_plane_add(size=8000, location=(0, 0, 0.02))
        ground = bpy.context.object
        for cl in ground.users_collection:
            cl.objects.unlink(ground)
        extra.objects.link(ground)
        ground.data.materials.append(GROUND_MAT)
        wc, tc, gc = BUILT["Mil_WallSegment"][0], BUILT["Mil_WallTower"][0], BUILT["Mil_Gate"][0]
        tmp = [ground, instance(gc, extra, (0, 0, 0))]
        for s in (1, -1):
            tmp.append(instance(wc, extra, (s * 135, 0, 0)))
            tmp.append(instance(tc, extra, (s * 195, 0, 0)))
            for k in range(2):
                tmp.append(instance(wc, extra, (s * 195, 60 + 120 * k, 0), s * math.pi / 2))
        shoot("Mil_Line", "outer", V((0, 60, 10)), (0.6, -1.2, 0.55), 560)
        shoot("Mil_Line", "inner", V((-60, 40, 10)), (-0.5, 1.1, 0.7), 420)
        for o in tmp:
            bpy.data.objects.remove(o, do_unlink=True)
    print("TEXTURES USED:", TEX_USED if TEX_USED else "none (fallback colours)")


if PREVIEW_DIR:
    GROUND_MAT = plain_mat("Preview_Ground", (0.30, 0.25, 0.20), 0.95)
    HUB_MAT = plain_mat("Preview_Hub", (0.35, 0.36, 0.38), 0.6)
    render_previews()
print("SETTLEMENTS_DONE")
