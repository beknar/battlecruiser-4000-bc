"""Model the carrier's custom parts in Blender and export them as FBX for Unity.

Run:  blender -b --python build_carrier.py -- <out_dir> [preview.png]

Original design in the style of a 1980s anime space carrier (inspired by the Ikazuchi class):
608 m main hull (702 m with engine shields), 154 m wide, 177 m tall, hourglass cross-section,
six main engines, command superstructure, 8 triple beam turrets on the upper deck, 6 flank
launch bays, a ventral hangar amidships and a vertical missile launch block.

Blender axes: bow points to -Y, +Z is up. Exported with axis_forward="-Z", axis_up="Y" this
imports into Unity with the bow pointing +Z and up +Y.  Unity position = (-x, z, -y).
Every exported mesh has its transform applied (vertices in the model's own space, object at
the origin), because nested object offsets don't survive the FBX axis conversion reliably;
Unity rebuilds pivots (e.g. the turret's yaw and pitch joints) from carrier_hardpoints.json.

Outputs: Carrier_Hull.fbx, TripleTurret.fbx, Missile.fbx, carrier_hardpoints.json
"""
import bpy, bmesh, math, json, os, sys, random

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)
TILE = 16.0  # metres per texture repeat on the hull

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

PALETTE = {
    "Hull_Main": (0.62, 0.64, 0.66), "Hull_Dark": (0.22, 0.25, 0.30), "Hull_Accent": (0.55, 0.12, 0.10),
    "Deck": (0.40, 0.42, 0.44), "Window_Emissive": (0.4, 0.9, 1.0), "Engine_Glow": (1.0, 0.6, 0.2),
    "Bay_Interior": (0.18, 0.19, 0.21), "Hatch": (0.33, 0.35, 0.37), "Barrel": (0.15, 0.15, 0.17),
    "Light_Strip": (0.95, 0.9, 0.6),
}
MATS = {}
for name, col in PALETTE.items():
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*col, 1.0)
    MATS[name] = m


def to_unity(p):
    return [round(-p[0], 3), round(p[2], 3), round(-p[1], 3)]


class MB:
    """Collects faces into one bmesh; each face gets flat UVs projected from world space."""

    def __init__(self, name):
        self.name, self.bm, self.mats = name, bmesh.new(), []
        self.uv = self.bm.loops.layers.uv.verify()

    def _mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def face(self, pts, mat, want=None):
        """Add a polygon. `want` is a vector the face normal should point along (flips if not)."""
        if len(pts) < 3:
            return None
        vs = [self.bm.verts.new(p) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f.normal_update()
        if want is not None and f.normal.dot(want) < 0:
            f.normal_flip()
        f.material_index = self._mi(mat)
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(0, 1), (1, 2), (0, 2)][[2, 0, 1].index(ax)]
        for loop in f.loops:
            co = loop.vert.co
            loop[self.uv].uv = (co[a] / TILE, co[b] / TILE)
        return f

    def build(self, parent=None, location=(0, 0, 0)):
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in self.mats:
            mesh.materials.append(MATS[m])
        obj = bpy.data.objects.new(self.name, mesh)
        scene.collection.objects.link(obj)
        obj.location = location
        if parent:
            obj.parent = parent
        return obj


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def center(pts):
    n = len(pts)
    return (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n, sum(p[2] for p in pts) / n)


def ring_from_half(half):
    """Half profile (x>=0) from top centre to bottom centre -> closed ring of (x, z)."""
    return list(half) + [(-x, z) for (x, z) in reversed(half[1:-1])]


def loft(mb, half, stations, mat, hole=None, caps=(True, True), cap_mat=None):
    """Loft a convex cross-section along Y.
    stations: (y, width_scale, z_top, z_bottom); the profile's own z range is remapped to
    [z_bottom, z_top]. `hole(center)` returns True for faces to leave out (bay openings)."""
    ring = ring_from_half(half)
    zt0, zb0 = max(z for _, z in ring), min(z for _, z in ring)
    rings = []
    for (y, w, zt, zb) in stations:
        pts = []
        for (x, z) in ring:
            t = (z - zb0) / (zt0 - zb0)
            pts.append((x * w, y, zb + t * (zt - zb)))
        rings.append((y, (zt + zb) / 2, pts))
    for (y0, zm0, r0), (y1, zm1, r1) in zip(rings, rings[1:]):
        n = len(r0)
        for i in range(n):
            j = (i + 1) % n
            quad = [r0[i], r0[j], r1[j], r1[i]]
            c = center(quad)
            if hole and hole(c):
                continue
            mb.face(quad, mat, want=sub(c, (0, c[1], (zm0 + zm1) / 2)))
    if caps[0]:
        mb.face(rings[0][2], cap_mat or mat, want=(0, -1, 0))
    if caps[1]:
        mb.face(rings[-1][2], cap_mat or mat, want=(0, 1, 0))


def box(mb, x0, x1, y0, y1, z0, z1, mat, skip=()):
    """Closed box with outward normals; `skip` names faces to leave out (-x,+x,-y,+y,-z,+z)."""
    c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    F = {
        "-x": [(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)],
        "+x": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
        "-y": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
        "+y": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        "-z": [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)],
        "+z": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
    }
    for k, pts in F.items():
        if k not in skip:
            mb.face(pts, mat, want=sub(center(pts), c))


def cavity(mb, x0, x1, y0, y1, z0, z1, open_side, mat, floor_mat=None):
    """Open box seen from inside (normals inward), missing the `open_side` face."""
    c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    F = {
        "-x": [(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)],
        "+x": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
        "-y": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
        "+y": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        "-z": [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)],
        "+z": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
    }
    for k, pts in F.items():
        if k != open_side:
            m = floor_mat if (k == "-z" and floor_mat) else mat
            mb.face(pts, m, want=sub(c, center(pts)))


def lathe(mb, profile, mat, axis="y", c=(0, 0), segs=32, outward=True):
    """Surface of revolution. profile: [(radius, t)] along the axis; c = centre in the
    other two axes ((x, z) for axis y, (x, y) for axis z)."""
    rings = []
    for (r, t) in profile:
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            ca, sa = math.cos(a), math.sin(a)
            if axis == "y":
                ring.append(((c[0] + r * ca, t, c[1] + r * sa), (ca, 0, sa)))
            else:
                ring.append(((c[0] + r * ca, c[1] + r * sa, t), (ca, sa, 0)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            quad = [r0[k][0], r0[j][0], r1[j][0], r1[k][0]]
            d = r0[k][1]
            if not outward:
                d = (-d[0], -d[1], -d[2])
            mb.face(quad, mat, want=d)
    return rings


def disc(mb, r, t, mat, axis="y", c=(0, 0), segs=32, facing=1):
    pts = []
    for k in range(segs):
        a = 2 * math.pi * k / segs
        if axis == "y":
            pts.append((c[0] + r * math.cos(a), t, c[1] + r * math.sin(a)))
        else:
            pts.append((c[0] + r * math.cos(a), c[1] + r * math.sin(a), t))
    want = (0, facing, 0) if axis == "y" else (0, 0, facing)
    mb.face(pts, mat, want=want)


hard = {"meta": {"units": "metres", "space": "Unity (bow +Z, up +Y)", "length_main_hull": 608,
                 "length_overall": 702, "beam": 154, "height": 177}}

# ------------------------------------------------------------------ hull
root = bpy.data.objects.new("Carrier_Hull", None)
scene.collection.objects.link(root)

UPPER = [(0, 89), (50, 89), (62, 84), (77, 66), (77, 48), (64, 30), (40, 20), (0, 20)]
LOWER = [(0, -15), (42, -15), (64, -28), (77, -46), (77, -66), (64, -82), (46, -89), (40, -89), (0, -89)]
WAIST = [(0, 30), (32, 30), (38, 24), (38, 16), (38, -10), (38, -19), (32, -25), (0, -25)]

BAYS_Y = [-120.0, -20.0, 80.0]      # flank launch bay centres (each side)
BAY_HALF = 18.0
BAY_Z = (-10.0, 16.0)
HANGAR_Y = (-60.0, 100.0)           # ventral hangar opening
HANGAR_X = 40.0

mb = MB("Hull_Upper")
loft(mb, UPPER, [(-304, 0.10, 72, 48), (-270, 0.38, 84, 36), (-220, 0.70, 89, 24), (-150, 0.92, 89, 20),
                 (-60, 1.0, 89, 20), (250, 1.0, 89, 20), (275, 0.98, 87, 22), (300, 0.95, 84, 24)], "Hull_Main",
     cap_mat="Hull_Dark")
mb.build(root)

mb = MB("Hull_Lower")
loft(mb, LOWER, [(-268, 0.18, -42, -58), (-245, 0.55, -24, -76), (-215, 0.88, -16, -86), (-180, 1.0, -15, -89),
                 (HANGAR_Y[0], 1.0, -15, -89),
                 (HANGAR_Y[1], 1.0, -15, -89), (250, 1.0, -15, -89), (290, 0.95, -18, -86)], "Hull_Main",
     hole=lambda c: c[2] < -88 and abs(c[0]) < HANGAR_X + 0.5 and HANGAR_Y[0] < c[1] < HANGAR_Y[1],
     cap_mat="Hull_Dark")
mb.build(root)

waist_st = [(-200, 0.6, 30, -25), (-180, 1.0, 30, -25)]
for yc in BAYS_Y:
    waist_st += [(yc - BAY_HALF, 1.0, 30, -25), (yc + BAY_HALF, 1.0, 30, -25)]
waist_st += [(296, 1.0, 30, -25)]
mb = MB("Hull_Waist")
loft(mb, WAIST, waist_st, "Hull_Dark",
     hole=lambda c: abs(c[0]) > 37 and BAY_Z[0] < c[2] < BAY_Z[1] and any(abs(c[1] - y) < BAY_HALF for y in BAYS_Y))
mb.build(root)

# flank bays: recess, frame, launch data
mb = MB("Launch_Bays")
hard["flank_bays"] = []
for s in (1, -1):
    for i, yc in enumerate(BAYS_Y):
        xo, xi = 38 * s, 8 * s
        cavity(mb, min(xo, xi), max(xo, xi), yc - BAY_HALF, yc + BAY_HALF, BAY_Z[0], BAY_Z[1],
               "+x" if s > 0 else "-x", "Bay_Interior", floor_mat="Deck")
        x0, x1 = (38, 42) if s > 0 else (-42, -38)
        box(mb, x0, x1, yc - BAY_HALF - 2, yc + BAY_HALF + 2, BAY_Z[1], BAY_Z[1] + 2, "Hull_Accent")
        box(mb, x0, x1, yc - BAY_HALF - 2, yc + BAY_HALF + 2, BAY_Z[0] - 2, BAY_Z[0], "Hull_Accent")
        box(mb, x0, x1, yc - BAY_HALF - 2, yc - BAY_HALF, BAY_Z[0], BAY_Z[1], "Hull_Accent")
        box(mb, x0, x1, yc + BAY_HALF, yc + BAY_HALF + 2, BAY_Z[0], BAY_Z[1], "Hull_Accent")
        # bay lights strip on the ceiling
        box(mb, min(xo, xi) + 2 * (1 if s > 0 else 0), max(xo, xi) - 2 * (0 if s > 0 else 1),
            yc - 0.6, yc + 0.6, BAY_Z[1] - 0.4, BAY_Z[1] - 0.05, "Light_Strip")
        side = "starboard" if s > 0 else "port"   # Unity -x is port after the axis flip
        hard["flank_bays"].append({
            "name": f"Bay_{'S' if s > 0 else 'P'}{i + 1}",
            "mouth": to_unity((40 * s, yc, (BAY_Z[0] + BAY_Z[1]) / 2)),
            "launch_dir": to_unity((s, 0, 0)),
            "floor_center": to_unity(((xo + xi) / 2, yc, BAY_Z[0])),
            "floor_size": [30, 36], "height": BAY_Z[1] - BAY_Z[0], "side": side})
# ventral hangar
cavity(mb, -HANGAR_X, HANGAR_X, HANGAR_Y[0], HANGAR_Y[1], -89, -60, "-z", "Bay_Interior")
for x in (-HANGAR_X - 3, HANGAR_X):
    box(mb, x, x + 3, HANGAR_Y[0] - 3, HANGAR_Y[1] + 3, -91, -89, "Hull_Accent")
for y in (HANGAR_Y[0] - 3, HANGAR_Y[1]):
    box(mb, -HANGAR_X, HANGAR_X, y, y + 3, -91, -89, "Hull_Accent")
for y in range(int(HANGAR_Y[0]) + 10, int(HANGAR_Y[1]), 20):
    box(mb, -HANGAR_X + 1, HANGAR_X - 1, y - 0.6, y + 0.6, -60.4, -60.05, "Light_Strip")
mb.build(root)
hard["ventral_hangar"] = {"opening_center": to_unity((0, sum(HANGAR_Y) / 2, -89)),
                          "ceiling_center": to_unity((0, sum(HANGAR_Y) / 2, -60)),
                          "size": [HANGAR_X * 2, HANGAR_Y[1] - HANGAR_Y[0]], "height": 29,
                          "launch_dir": to_unity((0, 0, -1)),
                          "launch_points": [{"pos": to_unity((x, y, -75))} for y in (-30, 20, 70) for x in (-20, 20)]}

# ------------------------------------------------------------------ engines
mb = MB("Engine_Block")
box(mb, -70, 70, 290, 318, 30, 80, "Hull_Dark")
box(mb, -70, 70, 284, 318, -80, -25, "Hull_Dark")
box(mb, -36, 36, 290, 316, -25, 30, "Hull_Dark")
nozzles = [(-45, 55), (0, 55), (45, 55), (-45, -52), (0, -52), (45, -52)]
glow = MB("Engine_Glow")
hard["engines"] = []
for (cx, cz) in nozzles:
    outer = [(16, 316), (17, 326), (19.5, 340), (20, 346)]
    inner = [(13, 318), (14, 328), (17.5, 340), (18.2, 345.5)]
    lathe(mb, outer, "Hull_Dark", c=(cx, cz))
    lathe(mb, inner, "Barrel", c=(cx, cz), outward=False)
    lathe(mb, [(18.2, 345.5), (20, 346)], "Hull_Accent", c=(cx, cz))  # lip
    disc(glow, 13.2, 319, "Engine_Glow", c=(cx, cz), facing=1)
    hard["engines"].append({"exit": to_unity((cx, 346, cz)), "dir": to_unity((0, 1, 0)), "radius": 18})
mb.build(root)
glow.build(root)

def prism(mb, poly_yz, x0, x1, mat):
    """Convex polygon in the Y-Z plane extruded along X from x0 to x1."""
    cyz = (sum(p[0] for p in poly_yz) / len(poly_yz), sum(p[1] for p in poly_yz) / len(poly_yz))
    c = ((x0 + x1) / 2, cyz[0], cyz[1])
    for x in (x0, x1):
        pts = [(x, y, z) for (y, z) in poly_yz]
        mb.face(pts, mat, want=sub(center(pts), c))
    n = len(poly_yz)
    for i in range(n):
        (ya, za), (yb, zb) = poly_yz[i], poly_yz[(i + 1) % n]
        pts = [(x0, ya, za), (x0, yb, zb), (x1, yb, zb), (x1, ya, za)]
        mb.face(pts, mat, want=sub(center(pts), c))


mb = MB("Engine_Shields")
for (z0, z1) in ((82, 86), (-86, -82)):
    loft(mb, [(0, 2), (80, 2), (80, -2), (0, -2)],
         [(300, 1.0, z1, z0), (360, 1.0, z1, z0), (398, 0.82, z1, z0)], "Hull_Main")
for s in (1, -1):
    x0, x1 = (78, 83) if s > 0 else (-83, -78)
    prism(mb, [(300, 26), (300, 86), (398, 82), (398, 44)], x0, x1, "Hull_Main")
    prism(mb, [(300, -86), (300, -20), (398, -38), (398, -82)], x0, x1, "Hull_Main")
    xa, xb = (x1, x1 + 0.8) if s > 0 else (x0 - 0.8, x0)
    box(mb, xa, xb, 325, 385, 60, 64, "Hull_Accent")
    box(mb, xa, xb, 325, 385, -64, -60, "Hull_Accent")
    box(mb, xa, xb, 310, 392, 72, 72.8, "Light_Strip")
mb.build(root)

# auxiliary thrusters (kit reactors) on the stern corners
hard["aux_thrusters"] = [{"pos": to_unity((s * 68, 300, z)), "dir": to_unity((0, 1, 0)), "mirror": s < 0}
                         for s in (1, -1) for z in (86, -90)]

# ------------------------------------------------------------------ bridge superstructure
mb = MB("Bridge")
CHAMF = [(0, 1), (0.85, 1), (1, 0.7), (1, 0), (0, 0)]
loft(mb, CHAMF, [(92, 28, 98, 89), (102, 38, 102, 89), (205, 38, 102, 89)], "Hull_Main")
loft(mb, CHAMF, [(110, 0.7 * 28, 112, 102), (122, 28, 116, 102), (200, 28, 116, 102)], "Hull_Main")
loft(mb, CHAMF, [(136, 14, 134, 116), (144, 18, 140, 116), (182, 18, 140, 116), (188, 15, 137, 116)], "Hull_Dark")
loft(mb, [(0, 1), (1, 1), (1, 0), (0, 0)], [(142, 32, 139, 134), (158, 32, 139, 134)], "Hull_Main")  # bridge wings
box(mb, -12, 12, 150, 180, 140, 145, "Hull_Main")
box(mb, -18.6, 18.6, 186, 196, 102, 104, "Hull_Accent")
mb.build(root)
win = MB("Bridge_Windows")
win.face([(-12.5, 135.7, 124), (12.5, 135.7, 124), (12.5, 135.7, 130), (-12.5, 135.7, 130)],
         "Window_Emissive", want=(0, -1, 0))
win.face([(-31.5, 141.7, 135), (31.5, 141.7, 135), (31.5, 141.7, 137.5), (-31.5, 141.7, 137.5)], "Window_Emissive",
         want=(0, -1, 0))
for y in range(128, 196, 8):
    for s in (1, -1):
        win.face([(28.1 * s, y, 107), (28.1 * s, y + 4, 107), (28.1 * s, y + 4, 109.5), (28.1 * s, y, 109.5)],
                 "Window_Emissive", want=(s, 0, 0))
for y in range(150, 180, 6):
    for s in (1, -1):
        win.face([(18.1 * s, y, 128), (18.1 * s, y + 3, 128), (18.1 * s, y + 3, 131), (18.1 * s, y, 131)],
                 "Window_Emissive", want=(s, 0, 0))
win.build(root)
hard["antennas"] = [{"pos": to_unity((0, 172, 145)), "kind": "SM_antenna_01"},
                    {"pos": to_unity((8, 158, 145)), "kind": "SM_antenna_02"},
                    {"pos": to_unity((-8, 158, 145)), "kind": "SM_antenna_02"},
                    {"pos": to_unity((27, 150, 139)), "kind": "SM_antenna_02"},
                    {"pos": to_unity((-27, 150, 139)), "kind": "SM_antenna_02"}]
hard["bridge"] = {"center": to_unity((0, 165, 135))}

# ------------------------------------------------------------------ missile launch block (VLS)
mb = MB("VLS_Block")
box(mb, -12, 12, -216, -133, 88.8, 90.4, "Deck")
hard["vls_cells"] = []
cols = [-6.9, -2.3, 2.3, 6.9]
for r in range(16):
    y = -211.5 + r * 4.9
    for x in cols:
        mb.face([(x - 2, y - 2, 90.6), (x + 2, y - 2, 90.6), (x + 2, y + 2, 90.6), (x - 2, y + 2, 90.6)], "Hatch",
                want=(0, 0, 1))
        hard["vls_cells"].append({"pos": to_unity((x, y, 90.7)), "dir": [0, 1, 0]})
mb.build(root)

# ------------------------------------------------------------------ armour bands, stripes, lights, greebles
mb = MB("Hull_Details")
for s in (1, -1):
    xa, xb = (77, 78.2) if s > 0 else (-78.2, -77)
    box(mb, xa, xb, -150, 255, 50, 64, "Hull_Main")          # upper armour belt
    box(mb, xa, xb, -170, 245, -64, -50, "Hull_Main")        # lower armour belt
    box(mb, xa + (1.2 if s > 0 else -0.6), xb + (0.6 if s > 0 else -1.2), -150, -90, 54, 58, "Hull_Accent")
xs = {}
for s in (1, -1):
    xs[s] = (78.2, 78.6) if s > 0 else (-78.6, -78.2)
    box(mb, *xs[s], -80, 250, 60.6, 61.4, "Light_Strip")
    box(mb, *xs[s], -160, 240, -57.4, -56.6, "Light_Strip")
rng = random.Random(7)
turret_spots = []
for y in range(-150, 245, 9):                          # deck-edge greebles
    for s in (1, -1):
        if 84 < y < 210:
            continue
        w, l, h = rng.uniform(2, 5), rng.uniform(3, 7), rng.uniform(0.8, 2.5)
        x = s * rng.uniform(43, 48)
        box(mb, x - w / 2, x + w / 2, y, y + l, 89, 89 + h, "Hull_Dark")
for y in range(-170, 260, 14):                         # waist pipes / blocks (between bays)
    if any(abs(y - b) < BAY_HALF + 6 for b in BAYS_Y):
        continue
    for s in (1, -1):
        x0, x1 = (38, 40.5) if s > 0 else (-40.5, -38)
        box(mb, x0, x1, y, y + 9, -6, 12, "Hull_Main")
mb.build(root)

# ------------------------------------------------------------------ hardpoints for turrets
main = [(-24, -205, 1), (24, -205, 1), (-30, -150, 1), (30, -150, 1), (-30, -95, 1), (30, -95, 1),
        (-30, 232, -1), (30, 232, -1)]
hard["main_turrets"] = [{"pos": to_unity((x, y, 89)), "facing": to_unity((0, -f, 0))} for (x, y, f) in main]
pd = [(77, -120, 66, 1), (-77, -120, 66, -1), (77, 60, 66, 1), (-77, 60, 66, -1),
      (77, -60, -66, 1), (-77, -60, -66, -1), (77, 150, -66, 1), (-77, 150, -66, -1)]
hard["pd_turrets"] = [{"pos": to_unity((x, y, z)), "up": to_unity((s, 0, 1 if z > 0 else -1)),
                       "facing": to_unity((s, 0, 0))} for (x, y, z, s) in pd]


# ------------------------------------------------------------------ triple beam turret (separate FBX)
def build_turret():
    base_root = bpy.data.objects.new("TripleTurret", None)
    scene.collection.objects.link(base_root)
    b = MB("Turret_Base")
    lathe(b, [(11.5, 0), (11.5, 1.5), (11, 3)], "Hull_Dark", axis="z")
    disc(b, 11, 3, "Deck", axis="z", facing=1)
    b.build(base_root)
    yaw = bpy.data.objects.new("Turret_Yaw", None)
    scene.collection.objects.link(yaw)
    yaw.parent = base_root
    yaw.location = (0, 0, 3)
    h = MB("Turret_House")
    loft(h, [(0, 9), (7, 9), (10, 6), (10, 0), (0, 0)],
         [(-11, 0.7, 6, 0), (-6, 1.0, 9, 0), (10, 1.0, 9, 0), (12, 0.9, 8, 0)], "Hull_Main")
    box(h, -10.4, -10, -2, 8, 3, 5, "Hull_Dark")
    box(h, 10, 10.4, -2, 8, 3, 5, "Hull_Dark")
    box(h, -3, 3, 4, 9, 9, 10.5, "Hull_Dark")      # rangefinder / sensor housing
    h.build(yaw)
    pitch = bpy.data.objects.new("Turret_Pitch", None)
    scene.collection.objects.link(pitch)
    pitch.parent = yaw
    pitch.location = (0, -9, 5)
    g = MB("Turret_Barrels")
    for x in (-4.5, 0, 4.5):
        lathe(g, [(1.9, 1.5), (1.9, -3), (1.1, -4), (1.0, -32), (1.45, -32.5), (1.45, -35)], "Barrel",
              c=(x, 0), segs=16)
        disc(g, 1.45, -35, "Engine_Glow", c=(x, 0), segs=16, facing=-1)
    box(g, -7, 7, -2.5, 1.5, -2.2, 2.2, "Hull_Dark")  # mantlet
    g.build(pitch)
    return base_root


turret = build_turret()
hard["turret_model"] = {"yaw_pivot_height": 3, "pitch_pivot": to_unity((0, -9, 8)), "barrel_length": 35,
                        "muzzles": [{"pos": to_unity((x, -44, 8))} for x in (-4.5, 0, 4.5)]}


def build_missile():
    m_root = bpy.data.objects.new("Missile", None)
    scene.collection.objects.link(m_root)
    m = MB("Missile_Body")
    lathe(m, [(0.05, -4.6), (0.35, -4.0), (0.6, -3.0), (0.6, 4.2), (0.5, 4.5)], "Hull_Main", c=(0, 0), segs=16)
    disc(m, 0.5, 4.5, "Engine_Glow", c=(0, 0), segs=16, facing=1)
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        ca, sa = math.cos(ang), math.sin(ang)
        p = [(0.55 * ca, 2.5, 0.55 * sa), (1.6 * ca, 3.6, 1.6 * sa), (1.6 * ca, 4.4, 1.6 * sa), (0.55 * ca, 4.4, 0.55 * sa)]
        m.face(p, "Hull_Accent", want=(-sa, 0, ca))
        m.face(list(reversed(p)), "Hull_Accent", want=(sa, 0, -ca))
    lathe(m, [(0.62, -1.0), (0.62, -0.6)], "Hull_Accent", c=(0, 0), segs=16)
    m.build(m_root)
    return m_root


missile = build_missile()


def build_drone():
    """Small unmanned combat drone (~7 m): wedge body, twin engine pods, sensor eye."""
    d_root = bpy.data.objects.new("Drone", None)
    scene.collection.objects.link(d_root)
    d = MB("Drone_Body")
    loft(d, [(0, 1), (0.6, 1), (1, 0.5), (0.7, 0), (0, 0)],
         [(-3.6, 0.5, 0.6, -0.2), (-2.0, 1.3, 1.0, -0.6), (2.4, 1.6, 1.0, -0.7), (3.2, 1.2, 0.8, -0.5)], "Hull_Dark")
    for s in (1, -1):
        box(d, 1.6 * s - 0.55, 1.6 * s + 0.55, -0.8, 3.6, -0.55, 0.45, "Hull_Main")
        lathe(d, [(0.45, 3.6), (0.4, 3.9)], "Barrel", c=(1.6 * s, -0.05), segs=12)
        disc(d, 0.38, 3.85, "Engine_Glow", c=(1.6 * s, -0.05), segs=12, facing=1)
        box(d, 2.15 * s - 0.05 if s > 0 else -2.15 - 1.5, 2.15 + 1.5 if s > 0 else -2.15 + 0.05, 0.5, 2.6,
            -0.06, 0.06, "Hull_Accent")  # stub wings
    d.face([(-0.35, -3.0, 0.62), (0.35, -3.0, 0.62), (0.35, -2.4, 0.8), (-0.35, -2.4, 0.8)], "Window_Emissive",
           want=(0, -0.5, 1))
    d.build(d_root)
    return d_root


drone = build_drone()


# ------------------------------------------------------------------ export
def select_tree(root_obj):
    bpy.ops.object.select_all(action="DESELECT")
    stack = [root_obj]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)


def flatten(root_obj):
    """Bake every mesh's world transform into its vertices, parent it straight to the root at
    the origin, and delete the intermediate empties."""
    bpy.context.view_layer.update()
    stack, meshes, empties = list(root_obj.children), [], []
    while stack:
        o = stack.pop()
        stack.extend(o.children)
        (meshes if o.type == "MESH" else empties).append(o)
    from mathutils import Matrix
    turn = Matrix.Rotation(math.pi, 4, "Z")
    for o in meshes:
        mw = turn @ o.matrix_world
        o.data.transform(mw)
        o.parent = root_obj
        o.matrix_parent_inverse.identity()
        o.matrix_basis.identity()
    for e in empties:
        bpy.data.objects.remove(e, do_unlink=True)
    root_obj.matrix_basis.identity()


def export(root_obj, name):
    flatten(root_obj)
    select_tree(root_obj)
    path = os.path.join(OUT, name)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False)
    print("EXPORTED", path)


export(root, "Carrier_Hull.fbx")
export(turret, "TripleTurret.fbx")
export(missile, "Missile.fbx")
export(drone, "Drone.fbx")
with open(os.path.join(OUT, "carrier_hardpoints.json"), "w") as f:
    json.dump(hard, f, indent=1)

# ------------------------------------------------------------------ preview render (Blender only)
if PREVIEW:
    missile.location = (0, 0, -2000)
    drone.location = (0, 0, -2000)
    turret.location = (0, 0, -1000)
    for (x, y, f) in main:
        inst = bpy.data.objects.new("T", None)
        inst.instance_type = "COLLECTION"
        coll = bpy.data.collections.get("TurretColl")
        if coll is None:
            coll = bpy.data.collections.new("TurretColl")
            stack = [turret]
            while stack:
                o = stack.pop()
                coll.objects.link(o)
                stack.extend(o.children)
            coll.instance_offset = (0, 0, -1000)
        inst.instance_collection = coll
        inst.location = (x, y, 89)
        inst.rotation_euler = (0, 0, 0 if f > 0 else math.pi)
        scene.collection.objects.link(inst)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    w = bpy.data.worlds.new("w")
    scene.world = w
    w.color = (0.02, 0.025, 0.04)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 35
    cam.data.clip_end = 5000
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    views = {"front34": ((-520, -620, 320), (0, -40, 0)), "side": ((-900, 40, 20), (0, 40, 0)),
             "front": ((0, -900, 0), (0, 0, 0)), "rear34": ((480, 720, 260), (0, 60, 0)),
             "below": ((-380, -300, -420), (0, 0, -40))}
    from mathutils import Vector
    for name, (loc, tgt) in views.items():
        cam.location = loc
        d = Vector(tgt) - Vector(loc)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = PREVIEW.replace(".png", f"_{name}.png")
        bpy.ops.render.render(write_still=True)
print("CARRIER_DONE")
