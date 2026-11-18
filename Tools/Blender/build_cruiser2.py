"""Rebuild the AI-made cruiser2 model into the Dominion gun cruiser: its crooked turrets replaced
with clean ones, more detail modelled on, and a new colour texture with strong panel lines baked
from the hull's own shape (and a normal map of them). Writes a GLB that convert_frigates.py cuts
up like the others.

Run:  blender -b --python build_cruiser2.py -- <recoloured.glb> <out.glb> [--no-bake] [--res 4096]

<recoloured.glb> is cruiser2.glb after recolour_frigate.py --method scheme-mono --split 0.75,0.95
(see frigates_spec.json). Writes <out.glb> and, beside it, <out>_normal.png. --no-bake skips the
texture bake (quick geometry checks: the new parts get flat colours instead). Prints the spec's
turret boxes, missile cells and ball mounts as JSON (SPEC ...), measured off the rebuilt model.

The model comes from Tripo already lying the way the conversion wants it (bow at -X, +Z up, 1 unit
long, keel at z = 0), so it is only re-normalised. Then:

1. The AI turrets come off: every loose part lying wholly inside one of the REMOVE boxes (the two
   deck turrets with their bent, segmented barrels, the twin guns splayed out of the engine pods'
   noses and the two crooked keel turrets).
2. New turrets go on, built square to the ship: housings level, barrels parallel to the deck and
   pointing straight fore or aft along X. Three triple turrets forward (A, B superfiring over A,
   C superfiring over B), a twin aft firing astern (X), a twin on each engine pod and two twins
   under the keel (hung from armour slabs that cover the AI turrets' shadows, painted into the
   atlas). Each sits on a barbette (a separate part, so it stays on the hull) reaching down into
   the deck, so the turret is level even where the deck slopes; a superfiring one stands high
   enough for its barrels to clear the roof ahead. Each barrel, mantlet collar and all, is one
   lathed part (its own loose part, so the conversion finds one muzzle on it).
3. Detail goes on, placed by casting rays at the hull: an aft mast with yards, sensor domes on the
   bow and the belly, whip antennas, louvred vents on the fore deck and the flanks, conduits along
   the deck, caps in the engine pods' empty gun rings.
4. The texture: a new UV layout keeps the AI atlas in the lower left 7/8 (its UVs scaled down) and
   gives each group of new parts a square of the strip left over. Cycles bakes the colour into it:
   the recoloured atlas brightened to the cruisers' light grey, a slate lower hull (where the
   surface faces down), one plate in ten slate, a slate deck walkway edged in red, and two grids
   of recessed panel lines (plates staggered like brickwork, and heavy frames three plates by four
   rows) laid out from the hull's own surface (across the deck, along the flanks, across the ends)
   so they follow it, each line dark with a light edge on one side; the new parts in their own
   greys with a finer square plating and red roof bands. The same lines are baked again as a
   tangent-space normal map.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
NO_BAKE = "--no-bake" in argv
if NO_BAKE:
    argv.remove("--no-bake")
RES = 4096
if "--res" in argv:
    i = argv.index("--res"); RES = int(argv[i + 1]); del argv[i:i + 2]
SRC, OUT = argv[0], argv[1]

# the AI turrets: loose parts wholly inside one of these boxes [x0, x1, y0, y1, z0, z1] go
REMOVE = [
    [0.040, 0.190, -0.030, 0.035, 0.135, 0.170],    # A: housing, mantlet, barrel
    [0.095, 0.250, -0.020, 0.025, 0.163, 0.190],    # B: housing, breech, long barrel
    [0.215, 0.320, -0.130, -0.090, 0.095, 0.146],   # port pod guns and their mount
    [0.215, 0.320, 0.090, 0.130, 0.095, 0.146],     # starboard pod guns
    [-0.180, -0.090, -0.030, 0.030, 0.000, 0.045],  # forward keel turret
    [0.160, 0.275, -0.035, 0.035, -0.001, 0.067],   # aft keel turret
]


def srgb(c):
    """An sRGB 0-255 colour as linear 0-1."""
    return tuple(((v / 255 + 0.055) / 1.055) ** 2.4 if v / 255 > 0.04045 else v / 255 / 12.92 for v in c)


# the cruisers' paint (the starting frigate's tones, as recolour_frigate.py measures them)
LIGHT = srgb((172, 180, 183))
SLATE = srgb((60, 68, 78))
GUNMETAL = srgb((84, 90, 98))
DARK = srgb((38, 42, 48))
TRIM = srgb((150, 44, 52))
YELLOW = srgb((196, 158, 52))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in scene.objects if o.type == "MESH"]
assert len(meshes) == 1, f"expected one mesh, found {len(meshes)}"
hull = meshes[0]
hull.data.transform(hull.matrix_world)
hull.parent = None
hull.matrix_basis.identity()
for o in list(scene.objects):
    if o is not hull:
        bpy.data.objects.remove(o, do_unlink=True)
hull.name = hull.data.name = "Hull"
co = [v.co for v in hull.data.vertices]
lo = Vector(map(min, *co)); hi = Vector(map(max, *co))
s = 1.0 / (hi.x - lo.x)
hull.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))


# ------------------------------------------------------------------ 1. the AI turrets off

def loose_parts(me):
    parent = list(range(len(me.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    first = {}
    for v in me.vertices:
        k = (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5))
        if k in first:
            a, b = find(v.index), find(first[k])
            if a != b:
                parent[a] = b
        else:
            first[k] = v.index
    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    return [find(i) for i in range(len(me.vertices))]


def inside(p, b, eps=0.0):
    return b[0] - eps <= p.x <= b[1] + eps and b[2] - eps <= p.y <= b[3] + eps and b[4] - eps <= p.z <= b[5] + eps


me = hull.data
comp = loose_parts(me)
part_verts = {}
for v in me.vertices:
    part_verts.setdefault(comp[v.index], []).append(v.co)
gone = {k for k, vs in part_verts.items() if any(all(inside(p, b) for p in vs) for b in REMOVE)}
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
dead = [f for f in bm.faces if comp[f.verts[0].index] in gone]
bmesh.ops.delete(bm, geom=dead, context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(me)
bm.free()
print(f"REMOVED {len(gone)} parts ({len(dead)} faces); hull now {len(me.polygons)} faces")

depsgraph = bpy.context.evaluated_depsgraph_get()
bvh = BVHTree.FromObject(hull, depsgraph)


def ray(p, d):
    h = bvh.ray_cast(Vector(p), Vector(d))
    return (h[0], h[1]) if h[0] is not None else (None, None)


def deck(x, y):
    p, n = ray((x, y, 1.0), (0, 0, -1))
    return (p, n) if p is not None else (Vector((x, y, 0.0)), Vector((0, 0, 1)))


def keel(x, y):
    p, n = ray((x, y, -1.0), (0, 0, 1))
    return (p, n) if p is not None else (Vector((x, y, 1.0)), Vector((0, 0, -1)))


def footprint(x, y, r):
    """Points over a disc of radius r round (x, y)."""
    pts = [(x, y)]
    for ring, k in ((r * 0.5, 8), (r, 16)):
        pts += [(x + ring * math.cos(a * math.tau / k), y + ring * math.sin(a * math.tau / k)) for a in range(k)]
    return pts


# ------------------------------------------------------------------ mesh building

parts = []   # (object, group)


def make(name, bm, tone, group, panel=1.0, smooth_angle=None):
    """An object of the bmesh, coloured `tone` (linear RGB) with `panel` strength of plating lines,
    in UV group `group`."""
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = False
    if smooth_angle is not None:
        # smooth the round parts (barrels, domes), keep the hard edges sharp
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    attr = me.color_attributes.new("tone", "FLOAT_COLOR", "CORNER")
    for d in attr.data:
        d.color = (*tone, panel)
    parts.append((ob, group))
    return ob


def add_box(bm, centre, size, M=Matrix()):
    bmesh.ops.create_cube(bm, size=1.0, matrix=M @ Matrix.Translation(Vector(centre)) @ Matrix.Diagonal((*size, 1.0)))


def add_cyl(bm, p0, p1, r0, r1, segs, M=Matrix()):
    p0, p1 = Vector(p0), Vector(p1)
    axis = p1 - p0
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    mat = M @ Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0, radius2=r1,
                          depth=axis.length, matrix=mat)


def add_lathe(bm, origin, axis, profile, segs, M=Matrix()):
    """A solid of revolution about `axis` from `origin`: profile is [(distance along, radius)];
    radius 0 closes an end. One connected piece."""
    origin, axis = Vector(origin), Vector(axis).normalized()
    side = axis.orthogonal().normalized()
    up = axis.cross(side)
    rings = []
    for t, r in profile:
        c = M @ (origin + axis * t)
        if r <= 0:
            rings.append([bm.verts.new(c)])
        else:
            rings.append([bm.verts.new(M @ (origin + axis * t + (side * math.cos(a * math.tau / segs) + up * math.sin(a * math.tau / segs)) * r))
                          for a in range(segs)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(segs):
            j = (i + 1) % segs
            if len(a) == 1:
                bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))


def add_hull(bm, pts, M=Matrix()):
    """The convex hull of the points, its coplanar triangles merged."""
    vs = [bm.verts.new(M @ Vector(p)) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.delete(bm, geom=[g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)], context="VERTS")
    faces = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1), verts=list({v for f in faces for v in f.verts}),
                             edges=list({e for f in faces for e in f.edges}))


def add_dome(bm, centre, r, M=Matrix(), segs=16, rings=8):
    """A half sphere on a short collar, up local +Z."""
    tmp = bmesh.new()
    bmesh.ops.create_uvsphere(tmp, u_segments=segs, v_segments=rings, radius=r)
    bmesh.ops.delete(tmp, geom=[v for v in tmp.verts if v.co.z < -1e-6], context="VERTS")
    bmesh.ops.holes_fill(tmp, edges=tmp.edges, sides=0)
    tmp.transform(M @ Matrix.Translation(Vector(centre)))
    me = bpy.data.meshes.new("tmp")
    tmp.to_mesh(me)
    tmp.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


def frame_on(p, n, fwd=Vector((1, 0, 0))):
    """A frame at p with +Z along n and +X as near fwd as it can."""
    z = Vector(n).normalized()
    x = Vector(fwd) - z * Vector(fwd).dot(z)
    x = x.normalized() if x.length > 1e-6 else z.orthogonal().normalized()
    y = z.cross(x)
    M = Matrix((x, y, z)).transposed().to_4x4()
    M.translation = Vector(p)
    return M


# ------------------------------------------------------------------ 2. turrets

turret_spec = []
group_counter = [0]


def turret(name, x, y, kind, facing, up, min_base=None, stripe=True, slab=None):
    """A turret at (x, y): kind "triple" (main) or "twin"; facing "-X" (fore) or "+X" (aft); up
    "+Z" (on the deck) or "-Z" (under the keel). Built square: its traverse axis vertical, its
    barrels level along X. min_base: no lower than this (a deck turret superfiring over another).
    slab (x0, x1, half width): a keel turret hangs from an armoured slab that covers where the AI's
    turret was (its shadow is painted into the hull's texture)."""
    sc = 1.0 if kind == "triple" else 0.62
    n = 3 if kind == "triple" else 2
    L, W, H = 0.050 * sc, 0.044 * sc, 0.015 * sc
    ring_r = 0.0215 * sc
    # level: on the highest deck under it (or the lowest keel over it), plus a little
    pts = footprint(x, y, max(L, W) * 0.55)
    if up == "+Z":
        zs = [h.z for h in (ray((px, py, 1.0), (0, 0, -1))[0] for px, py in pts) if h is not None]
        base = max(zs) + 0.0012
        if min_base is not None and base < min_base:
            print(f"({name} raised {min_base - base:.4f} to clear the turret ahead)")
            base = min_base
        depth = base - (min(zs) - 0.004)
    elif slab:
        x0, x1, hw = slab
        grid = [(x0 + (x1 - x0) * i / 6, hw * (j / 3 - 1)) for i in range(7) for j in range(7)]
        zs = [h.z for h in (ray((px, py, -1.0), (0, 0, 1))[0] for px, py in grid) if h is not None]
        top, bottom = max(zs) + 0.004, min(zs) - 0.0045
        ch = 0.003
        sb = bmesh.new()
        add_hull(sb, [(x0, -hw, top), (x0, hw, top), (x1, -hw, top), (x1, hw, top),
                      (x0, -hw, bottom + ch), (x0, hw, bottom + ch), (x1, -hw, bottom + ch), (x1, hw, bottom + ch),
                      (x0 + ch, -hw + ch, bottom), (x0 + ch, hw - ch, bottom), (x1 - ch, -hw + ch, bottom), (x1 - ch, hw - ch, bottom)])
        make(f"{name}_slab", sb, SLATE, group_counter[0])
        base = bottom - 0.0011
        depth = 0.004
    else:
        zs = [h.z for h in (ray((px, py, -1.0), (0, 0, 1))[0] for px, py in pts) if h is not None]
        base = min(zs) - 0.0012
        depth = (max(zs) + 0.004) - base
    R = Matrix.Identity(4)
    if facing == "+X":
        R = Matrix.Rotation(math.pi, 4, "Z")
    if up == "-Z":
        R = Matrix.Rotation(math.pi, 4, "X") @ R
    M = Matrix.Translation((x, y, base)) @ R
    g = group_counter[0]
    group_counter[0] += 1

    # the housing: a turret ring, a sloped-front gunhouse, a rangefinder across its back with its
    # lenses out at the sides, roof hatches and periscopes
    hb = bmesh.new()
    add_cyl(hb, (0, 0, 0), (0, 0, 0.0018 * sc), ring_r, ring_r, 32, M)
    z0 = 0.0016 * sc
    xf_top = -L / 2 + 0.013 * sc
    add_hull(hb, [(-L / 2, -W / 2, z0), (-L / 2, W / 2, z0), (L / 2, -W / 2, z0), (L / 2, W / 2, z0),
                  (xf_top, -(W / 2 - 0.004 * sc), H), (xf_top, W / 2 - 0.004 * sc, H),
                  (L / 2 - 0.003 * sc, -(W / 2 - 0.004 * sc), H), (L / 2 - 0.003 * sc, W / 2 - 0.004 * sc, H)], M)
    rf_x = L / 2 - 0.011 * sc
    add_box(hb, (rf_x, 0, H * 0.78), (0.006 * sc, W + 0.012 * sc, 0.0042 * sc), M)
    for sy in (-1, 1):
        add_cyl(hb, (rf_x, sy * (W / 2 + 0.006 * sc), H * 0.78), (rf_x, sy * (W / 2 + 0.0085 * sc), H * 0.78),
                0.0026 * sc, 0.0026 * sc, 12, M)
        add_box(hb, (0.004 * sc, sy * 0.009 * sc, H + 0.0004 * sc), (0.007 * sc, 0.0055 * sc, 0.0012 * sc), M)
        add_cyl(hb, (-0.003 * sc, sy * 0.0155 * sc, H - 0.0005 * sc), (-0.003 * sc, sy * 0.0155 * sc, H + 0.0032 * sc),
                0.0012 * sc, 0.0012 * sc, 8, M)
    # vents on the back wall
    add_box(hb, (L / 2 + 0.0004 * sc, 0, H * 0.4), (0.0016 * sc, W * 0.55, 0.0035 * sc), M)
    make(f"{name}_housing", hb, LIGHT, g)
    if stripe:
        sb = bmesh.new()
        add_box(sb, (-0.0055 * sc, 0, H + 0.0002 * sc), (0.0042 * sc, W - 0.0095 * sc, 0.0005 * sc), M)
        make(f"{name}_stripe", sb, TRIM, g, panel=0.0)

    # the barbette, fixed to the hull, down into the deck
    bb = bmesh.new()
    add_cyl(bb, (0, 0, -depth), (0, 0, -0.0002), 0.0200 * sc, 0.0200 * sc, 28, M)
    add_cyl(bb, (0, 0, -0.0016 * sc), (0, 0, -0.0004), 0.0222 * sc, 0.0222 * sc, 28, M)
    make(f"{name}_barbette", bb, LIGHT, g)

    # the barrels: each one lathed piece (a breech sleeve, a mantlet collar where it leaves the
    # gunhouse, a tapering tube, a band, a muzzle brake): one loose part, one muzzle
    zb = 0.0068 * sc
    xf = -L / 2 + 0.013 * sc * (zb - z0) / (H - z0)
    lb = 0.062 * sc if kind == "triple" else 0.066 * sc
    gap = 0.0125 * sc if n == 3 else 0.0135 * sc
    ys = [(i - (n - 1) / 2) * gap for i in range(n)]
    muzzles = []
    for i, by in enumerate(ys):
        pb = bmesh.new()
        r0, r1, rs = 0.0021 * sc, 0.0016 * sc, 0.0025 * sc
        rm, m0, m1 = 0.0041 * sc, 0.0028 * sc, 0.0096 * sc
        prof = [(0, 0), (0, rs), (m0, rs), (m0, rm), (m1 - 0.0008 * sc, rm), (m1, rm * 0.8), (m1, rs), (0.0095 * sc + 0.0012 * sc, r0),
                (0.0095 * sc + lb * 0.38, r0 * 0.93 + r1 * 0.07), (0.0095 * sc + lb * 0.38, r0 * 1.12),
                (0.0095 * sc + lb * 0.38 + 0.0016 * sc, r0 * 1.12), (0.0095 * sc + lb * 0.38 + 0.0016 * sc, r0 * 0.9),
                (0.0095 * sc + lb - 0.0032 * sc, r1), (0.0095 * sc + lb - 0.0032 * sc, r1 * 1.4),
                (0.0095 * sc + lb, r1 * 1.4), (0.0095 * sc + lb, 0)]
        add_lathe(pb, (xf + 0.006 * sc, by, zb), (-1, 0, 0), prof, 12, M)
        make(f"{name}_barrel{i + 1}", pb, GUNMETAL, g, panel=0.0, smooth_angle=40)
        muzzles.append(M @ Vector((xf + 0.006 * sc - 0.0095 * sc - lb, by, zb)))

    # the conversion's boxes (in this model's coordinates): the housing above the barbette, the
    # barrels in front of the gunhouse (their middles). The housing's box stops just over its
    # periscopes, under the barrels of the turret superfiring over it
    def box_of(local_pts):
        w = [M @ Vector(p) for p in local_pts]
        return [round(min(p.x for p in w), 4), round(max(p.x for p in w), 4), round(min(p.y for p in w), 4),
                round(max(p.y for p in w), 4), round(min(p.z for p in w), 4), round(max(p.z for p in w), 4)]
    housing_box = box_of([(-L / 2 - 0.004 * sc, -W / 2 - 0.012 * sc, 0.0001), (L / 2 + 0.004 * sc, W / 2 + 0.012 * sc, H + 0.0036 * sc)])
    barrel_box = box_of([(xf - 0.006 * sc - lb, ys[0] - 0.003 * sc, zb - 0.0025 * sc),
                         (xf + 0.0035 * sc, ys[-1] + 0.003 * sc, zb + 0.0025 * sc)])
    turret_spec.append({"name": name, "facing": facing, "up": up, "barrels": barrel_box, "housing": housing_box})
    print(f"TURRET {name}: base z {base:.4f}, barbette {depth:.4f} deep, muzzles "
          + ", ".join(f"({m.x:.3f}, {m.y:.3f}, {m.z:.4f})" for m in muzzles))
    return base


def clear_of(base, sc=1.0):
    """The least base for a main turret whose barrels pass over one based at `base`: their
    underside over its roof, with a little to spare."""
    roof_top = base + 0.015 * sc + 0.002
    return roof_top + 0.002 - 0.0068 + 0.0021 * 1.12


# three triples forward, each superfiring over the one ahead of it
a_base = turret("A", 0.085, 0.0, "triple", "-X", "+Z")
b_base = turret("B", 0.152, 0.0, "triple", "-X", "+Z", min_base=clear_of(a_base))
turret("C", 0.214, 0.0, "triple", "-X", "+Z", min_base=clear_of(b_base))
# (cut highest first, so no barrels overhanging a lower turret are still on when it is cut)
turret_spec[:3] = turret_spec[2::-1]
turret("X", 0.394, 0.0, "twin", "+X", "+Z")
turret("PodPort", 0.352, -0.094, "twin", "-X", "+Z", stripe=False)
turret("PodStarboard", 0.352, 0.094, "twin", "-X", "+Z", stripe=False)
turret("KeelFwd", -0.135, 0.0, "twin", "-X", "-Z", stripe=False, slab=(-0.180, -0.090, 0.028))
turret("KeelAft", 0.245, 0.0, "twin", "-X", "-Z", stripe=False, slab=(0.214, 0.276, 0.032))


# ------------------------------------------------------------------ 3. detail

def greeble_group():
    g = group_counter[0]
    group_counter[0] += 1
    return g


G_MAST = greeble_group()
G_DOME = greeble_group()
G_VENT = greeble_group()
G_PIPE = greeble_group()

# the aft mast: a tapering pole on two struts, two yards, a radar bar and a lamp dome at the top
mx, top = 0.362, 0.245
d, _ = deck(mx, 0.0)
mb = bmesh.new()
add_lathe(mb, (mx, 0, d.z - 0.002), (0, 0, 1), [(0, 0), (0, 0.0016), (0.04, 0.0012), (top - d.z + 0.002, 0.0008),
                                                 (top - d.z + 0.002, 0)], 8)
for sx in (-1, 1):
    f, _ = deck(mx + sx * 0.011, 0.0)
    add_cyl(mb, (mx + sx * 0.011, 0, f.z - 0.001), (mx, 0, top - 0.035), 0.0007, 0.0006, 6)
add_cyl(mb, (mx, -0.018, top - 0.022), (mx, 0.018, top - 0.022), 0.0006, 0.0006, 6)
add_cyl(mb, (mx, -0.012, top - 0.010), (mx, 0.012, top - 0.010), 0.0005, 0.0005, 6)
add_box(mb, (mx - 0.0015, 0, top - 0.016), (0.0025, 0.014, 0.0022))
for sy in (-1, 1):
    add_cyl(mb, (mx, sy * 0.017, top - 0.022), (mx, sy * 0.017, top - 0.013), 0.00035, 0.00035, 6)
make("Mast", mb, GUNMETAL, G_MAST, panel=0.0)
lb_ = bmesh.new()
add_dome(lb_, (mx, 0, top - 0.0005), 0.0018, segs=12, rings=6)
make("MastLamp", lb_, LIGHT, G_MAST, panel=0.0, smooth_angle=60)

# whip antennas at the bow and on the engine pods' tails
wb = bmesh.new()
for wx, wy, h in ((-0.455, -0.012, 0.022), (-0.455, 0.012, 0.022), (0.43, -0.094, 0.026), (0.43, 0.094, 0.026)):
    p, _ = deck(wx, wy)
    add_cyl(wb, (wx, wy, p.z - 0.001), (wx, wy, p.z + h), 0.0005, 0.0003, 6)
    add_cyl(wb, (wx, wy, p.z - 0.0005), (wx, wy, p.z + 0.0015), 0.0012, 0.0012, 8)
make("Whips", wb, GUNMETAL, G_MAST, panel=0.0)

# sensor domes: one on the bow, one big one under the forward hull and two small aft
for nm, (dx, dy), r, below in (("BowDome", (-0.418, 0.0), 0.0058, False), ("KeelDome", (-0.30, 0.0), 0.0075, True),
                               ("KeelDomePort", (0.150, -0.022), 0.0045, True), ("KeelDomeStbd", (0.150, 0.022), 0.0045, True)):
    p, n = keel(dx, dy) if below else deck(dx, dy)
    Mf = frame_on(p, Vector((0, 0, -1 if below else 1)))
    db = bmesh.new()
    add_cyl(db, (0, 0, -0.003), (0, 0, 0.0016), r * 0.82, r * 0.82, 16, Mf)
    add_dome(db, (0, 0, 0.0014), r, Mf)
    make(nm, db, LIGHT, G_DOME, panel=0.0, smooth_angle=50)


def vent(bm, M, lx, ly, t, slats):
    """A louvred vent on a surface frame M (+Z out of the surface): a dark frame and slats."""
    add_box(bm, (0, 0, t * 0.2), (lx, ly, t), M)
    for k in range(slats):
        sx = (k - (slats - 1) / 2) * lx / (slats + 0.5)
        add_box(bm, (sx, 0, t * 0.85), (lx / (slats * 2.2), ly * 0.86, t * 0.5), M)


# louvred vents on the fore deck, in pairs either side of the walkway
vb = bmesh.new()
for vx in (-0.34, -0.29, -0.24, -0.19, -0.14):
    for vy in (-0.021, 0.021):
        p, n = deck(vx, vy)
        vent(vb, frame_on(p, n), 0.014, 0.008, 0.0012, 5)
# and along both flanks, high on the side
for vx in (-0.33, -0.27, -0.12, -0.06, 0.06, 0.12):
    for sy in (-1, 1):
        p, n = ray((vx, sy * 1.0, 0.094), (0, -sy, 0))
        if p is not None:
            vent(vb, frame_on(p, n), 0.016, 0.0055, 0.0011, 6)
make("Vents", vb, DARK, G_VENT, panel=0.0)

# conduits along the deck either side, clamped every so often
pb_ = bmesh.new()
for sy in (-1, 1):
    pts = []
    for k in range(9):
        cx = -0.30 + k * 0.042
        p, n = deck(cx, sy * 0.044)
        pts.append(p + n * 0.0011)
    for a, b in zip(pts, pts[1:]):
        add_cyl(pb_, a, b, 0.00075, 0.00075, 8)
    for p in pts:
        add_cyl(pb_, p - Vector((0.0012, 0, 0)), p + Vector((0.0012, 0, 0)), 0.0012, 0.0012, 8)
make("Conduits", pb_, GUNMETAL, G_PIPE, panel=0.0)

# caps in the engine pods' gun rings, where the AI's splayed guns were
for nm, sy in (("PodCapPort", -1), ("PodCapStbd", 1)):
    c = Vector((0.308, sy * 0.108, 0.128))
    cb = bmesh.new()
    Mf = frame_on(c, Vector((-1, 0, 0)), fwd=Vector((0, 0, 1)))
    add_cyl(cb, (0, 0, -0.006), (0, 0, 0.001), 0.0185, 0.0185, 24, Mf)
    add_dome(cb, (0, 0, 0.0008), 0.010, Mf)
    make(nm, cb, SLATE, G_DOME, panel=0.0, smooth_angle=50)


# ------------------------------------------------------------------ spec numbers off the model

def cell(x, y):
    p, _ = deck(x, y)
    return [round(x, 3), round(y, 3), round(p.z, 4)]


def mount(x, z, side):
    sy = -1 if side == "port" else 1
    p, _ = ray((x, sy * 1.0, z), (0, -sy, 0))
    return {"pos": [round(p.x, 3), round(p.y, 4), round(p.z, 3)], "side": side}


spec = {
    "turrets": turret_spec,
    "missile_cells": [cell(-0.10, -0.018), cell(-0.10, 0.018), cell(-0.065, -0.018), cell(-0.065, 0.018)],
    "extra_mounts": [mount(-0.22, 0.072, "port"), mount(-0.22, 0.072, "starboard"),
                     mount(0.02, 0.072, "port"), mount(0.02, 0.072, "starboard")],
}
print("SPEC " + json.dumps(spec))

# ------------------------------------------------------------------ 4. UVs and the bake

# the hull: a second UV layer, the AI atlas scaled into the lower left 7/8
K = 0.875
old_uv = hull.data.uv_layers[0]
old_uv.name = "AtlasUV"
new_uv = hull.data.uv_layers.new(name="UVMap")
for a, b in zip(old_uv.data, new_uv.data):
    b.uv = a.uv * K

# each group of new parts: one object, unwrapped into its own square of the strip that's left
squares = [(K, k * (1 - K)) for k in range(8)] + [(k * (1 - K), K) for k in range(7)]
groups = {}
for ob, g in parts:
    groups.setdefault(g, []).append(ob)
assert len(groups) <= len(squares), f"{len(groups)} groups, {len(squares)} squares"
joined = []
for gi, (g, obs) in enumerate(sorted(groups.items())):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    me = ob.data
    me.uv_layers.new(name="AtlasUV")
    me.uv_layers.new(name="UVMap")
    me.uv_layers.active = me.uv_layers["UVMap"]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    u0, v0 = squares[gi]
    for d in me.uv_layers["UVMap"].data:
        d.uv = Vector((u0 + d.uv.x * (1 - K), v0 + d.uv.y * (1 - K)))
    joined.append(ob)

# the hull's own material for the bake, and one for the new parts
hull.data.color_attributes.new("tone", "FLOAT_COLOR", "CORNER")
for d in hull.data.color_attributes["tone"].data:
    d.color = (*LIGHT, 1.0)
atlas_img = next(n.image for n in hull.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
hull.data.materials.clear()
for ob in joined:
    ob.data.materials.clear()

bpy.ops.object.select_all(action="DESELECT")
for ob in joined + [hull]:
    ob.select_set(True)
bpy.context.view_layer.objects.active = hull
bpy.ops.object.join()
ship = hull
ship.data.uv_layers.active = ship.data.uv_layers["UVMap"]
print(f"MODEL {len(ship.data.polygons)} faces, {sum(len(p.vertices) - 2 for p in ship.data.polygons)} triangles")


def node(nt, kind, loc=(0, 0), **inputs):
    n = nt.nodes.new(kind)
    n.location = loc
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n


def math_node(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    for i, x in enumerate((a, b)):
        if x is None:
            continue
        if isinstance(x, (int, float)):
            n.inputs[i].default_value = x
        else:
            nt.links.new(x, n.inputs[i])
    return n.outputs[0]


def brick(nt, uv, width, height, mortar, offset, color1=(0, 0, 0, 1), color2=(1, 1, 1, 1), shift=(0, 0)):
    """A brick texture over the planar coordinates `uv` (a vector socket): its mortar mask (1 in
    the lines) and its random per-brick colour."""
    src = uv
    if shift != (0, 0):
        add = nt.nodes.new("ShaderNodeVectorMath")
        add.operation = "ADD"
        nt.links.new(uv, add.inputs[0])
        add.inputs[1].default_value = (shift[0], shift[1], 0)
        src = add.outputs[0]
    b = nt.nodes.new("ShaderNodeTexBrick")
    b.offset = offset
    b.offset_frequency = 2
    b.squash = 1.0
    b.inputs["Scale"].default_value = 1.0
    b.inputs["Mortar Size"].default_value = mortar
    b.inputs["Mortar Smooth"].default_value = 0.15
    b.inputs["Bias"].default_value = 0.0
    b.inputs["Brick Width"].default_value = width
    b.inputs["Row Height"].default_value = height
    b.inputs["Color1"].default_value = color1
    b.inputs["Color2"].default_value = color2
    b.inputs["Mortar"].default_value = (1, 1, 1, 1)
    nt.links.new(src, b.inputs["Vector"])
    return b.outputs["Fac"], b.outputs["Color"]


def planar_uv(nt):
    """Coordinates laid out on the surface: across the top and bottom (x, y), down the flanks
    (x, z), across the ends (y, z), chosen by which way the true normal points most."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sp.inputs[0])
    sn = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["True Normal"], sn.inputs[0])
    ax, ay, az = (math_node(nt, "ABSOLUTE", sn.outputs[i]) for i in range(3))
    wz = math_node(nt, "MULTIPLY", math_node(nt, "GREATER_THAN", az, ay), math_node(nt, "GREATER_THAN", az, ax))
    wy = math_node(nt, "MULTIPLY", math_node(nt, "GREATER_THAN", ay, ax), math_node(nt, "SUBTRACT", 1.0, wz))
    wx = math_node(nt, "SUBTRACT", math_node(nt, "SUBTRACT", 1.0, wz), wy)
    x, y, z = sp.outputs[0], sp.outputs[1], sp.outputs[2]
    u = math_node(nt, "ADD", math_node(nt, "MULTIPLY", x, math_node(nt, "ADD", wz, wy)), math_node(nt, "MULTIPLY", y, wx))
    v = math_node(nt, "ADD", math_node(nt, "MULTIPLY", y, wz), math_node(nt, "MULTIPLY", z, math_node(nt, "ADD", wy, wx)))
    cm = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(u, cm.inputs[0])
    nt.links.new(v, cm.inputs[1])
    return cm.outputs[0], sn.outputs[2], y, x


def mix_rgb(nt, blend, fac, a, b):
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = blend
    m.clamp_result = True
    for sock, val in ((m.inputs["Factor"], fac), (m.inputs[6], a), (m.inputs[7], b)):
        if isinstance(val, (int, float)):
            sock.default_value = val
        elif isinstance(val, tuple):
            sock.default_value = val
        else:
            nt.links.new(val, sock)
    return m.outputs[2]


def bake_material(name, is_hull, base_img, normal_img):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial")
    uv, nz, py, px = planar_uv(nt)
    tone = nt.nodes.new("ShaderNodeVertexColor")
    tone.layer_name = "tone"
    panel = tone.outputs["Alpha"]
    if is_hull:
        uvm = nt.nodes.new("ShaderNodeUVMap")
        uvm.uv_map = "AtlasUV"
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = atlas_img
        tex.interpolation = "Cubic"
        nt.links.new(uvm.outputs[0], tex.inputs[0])
        base = mix_rgb(nt, "MULTIPLY", 1.0, tex.outputs[0], (1.10, 1.10, 1.10, 1))
        # plates: staggered; frames: three plates by four rows, square; a few plates slate
        PW, PH = 0.034, 0.0135
        plate_f, plate_c = brick(nt, uv, PW, PH, 0.0005, 0.5, (0.90, 0.90, 0.90, 1), (1.06, 1.06, 1.06, 1))
        frame_f, _ = brick(nt, uv, PW * 3, PH * 4, 0.0009, 0.0)
        frame_sh, _ = brick(nt, uv, PW * 3, PH * 4, 0.0009, 0.0, shift=(0.0007, -0.0007))
        plate_sh, _ = brick(nt, uv, PW, PH, 0.0005, 0.5, shift=(0.0005, -0.0005))
        _, slate_c = brick(nt, uv, PW, PH, 0.0, 0.5, (0, 0, 0, 1), (1, 1, 1, 1))
        sc_ = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(slate_c, sc_.inputs[0])
        slate_m = math_node(nt, "GREATER_THAN", sc_.outputs[0], 0.90)
        # the lower hull slate: where the surface faces down
        belly = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "MULTIPLY", math_node(nt, "ADD", nz, 0.45), 6.0, clamp=False))
        belly = math_node(nt, "MINIMUM", math_node(nt, "MAXIMUM", belly, 0.0), 1.0)
        # a slate walkway down the fore deck, edged in red
        on_top = math_node(nt, "GREATER_THAN", nz, 0.85)
        fore = math_node(nt, "LESS_THAN", px, 0.050)
        ay = math_node(nt, "ABSOLUTE", py)
        walk = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", on_top, fore), math_node(nt, "LESS_THAN", ay, 0.0042))
        edge = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", on_top, fore),
                         math_node(nt, "MULTIPLY", math_node(nt, "GREATER_THAN", ay, 0.0042), math_node(nt, "LESS_THAN", ay, 0.0054)))
        slate_any = math_node(nt, "MINIMUM", math_node(nt, "ADD", math_node(nt, "ADD", slate_m, belly), walk), 1.0)
        ratio = tuple(SLATE[i] / LIGHT[i] for i in range(3)) + (1,)
        col = mix_rgb(nt, "MULTIPLY", slate_any, base, ratio)
        col = mix_rgb(nt, "MULTIPLY", 1.0, col, plate_c)
        col = mix_rgb(nt, "MIX", edge, col, (*TRIM, 1))
        groove = math_node(nt, "MAXIMUM", math_node(nt, "MULTIPLY", plate_f, 0.5), math_node(nt, "MULTIPLY", frame_f, 0.88))
        lit = math_node(nt, "ADD", math_node(nt, "MULTIPLY", math_node(nt, "MAXIMUM", math_node(nt, "SUBTRACT", frame_sh, frame_f), 0.0), 0.10),
                        math_node(nt, "MULTIPLY", math_node(nt, "MAXIMUM", math_node(nt, "SUBTRACT", plate_sh, plate_f), 0.0), 0.05))
        height = math_node(nt, "ADD", math_node(nt, "MULTIPLY", plate_f, -0.5), math_node(nt, "MULTIPLY", frame_f, -1.0))
    else:
        col = tone.outputs["Color"]
        # a square grid of plates, finer than the hull's
        plate_f, plate_c = brick(nt, uv, 0.0145, 0.0072, 0.00036, 0.0, (0.93, 0.93, 0.93, 1), (1.05, 1.05, 1.05, 1))
        plate_sh, _ = brick(nt, uv, 0.0145, 0.0072, 0.00036, 0.0, shift=(0.0004, -0.0004))
        col = mix_rgb(nt, "MULTIPLY", panel, col, plate_c)
        groove = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", plate_f, 0.45), panel)
        lit = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", math_node(nt, "MAXIMUM", math_node(nt, "SUBTRACT", plate_sh, plate_f), 0.0), 0.06), panel)
        height = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", plate_f, -0.6), panel)
    col = mix_rgb(nt, "MULTIPLY", groove, col, (0.08, 0.085, 0.09, 1))
    col = mix_rgb(nt, "ADD", lit, col, (1, 1, 1, 1))
    emit = node(nt, "ShaderNodeEmission")
    nt.links.new(col, emit.inputs["Color"])
    bump = node(nt, "ShaderNodeBump")
    bump.inputs["Strength"].default_value = 1.0
    bump.inputs["Distance"].default_value = 0.0006
    nt.links.new(height, bump.inputs["Height"])
    bsdf = node(nt, "ShaderNodeBsdfPrincipled")
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    targets = []
    for img in (base_img, normal_img):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = img
        targets.append(t)
    return mat, out, emit, bsdf, targets


def flat_material(name, colour):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1)
    return mat


stem = os.path.splitext(OUT)[0]
if NO_BAKE:
    # quick look: the hull keeps its atlas (on the old UVs), the new parts a flat grey
    m_hull = bpy.data.materials.new("Hull")
    m_hull.use_nodes = True
    nt = m_hull.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = atlas_img
    uvm = nt.nodes.new("ShaderNodeUVMap")
    uvm.uv_map = "AtlasUV"
    nt.links.new(uvm.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"])
    ship.data.materials.append(m_hull)
    ship.data.materials.append(flat_material("Parts", LIGHT))
    # the new parts are the faces in the strip
    uvl = ship.data.uv_layers["UVMap"].data
    for p in ship.data.polygons:
        u = uvl[p.loop_start].uv
        p.material_index = 1 if (u.x > K or u.y > K) else 0
    ship.data.uv_layers.active = ship.data.uv_layers["AtlasUV"]
    ship.data.uv_layers["AtlasUV"].active_render = True
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=False, export_attributes=False)
    print("WROTE (no bake)", OUT)
    sys.exit(0)

base_img = bpy.data.images.new("Cruiser2_BaseColor", RES, RES, alpha=False)
base_img.generated_color = (*LIGHT, 1)
normal_img = bpy.data.images.new("Cruiser2_Normal", RES, RES, alpha=False)
normal_img.generated_color = (0.5, 0.5, 1.0, 1)
normal_img.colorspace_settings.name = "Non-Color"
m_hull, out_h, emit_h, bsdf_h, tg_h = bake_material("BakeHull", True, base_img, normal_img)
m_part, out_p, emit_p, bsdf_p, tg_p = bake_material("BakeParts", False, base_img, normal_img)
ship.data.materials.append(m_hull)
ship.data.materials.append(m_part)
uvl = ship.data.uv_layers["UVMap"].data
for p in ship.data.polygons:
    u = uvl[p.loop_start].uv
    p.material_index = 1 if (u.x > K + 1e-4 or u.y > K + 1e-4) else 0

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 4
scene.render.bake.margin = 12
scene.render.bake.use_clear = False     # (the space between islands keeps the plain grey / flat normal)
bpy.ops.object.select_all(action="DESELECT")
ship.select_set(True)
bpy.context.view_layer.objects.active = ship

for (nt_mat, tg) in ((m_hull, tg_h), (m_part, tg_p)):
    nt_mat.node_tree.nodes.active = tg[0]
bpy.ops.object.bake(type="EMIT", margin=12, use_clear=False)
print("BAKED colour")

for (nt_mat, out, bsdf, tg) in ((m_hull, out_h, bsdf_h, tg_h), (m_part, out_p, bsdf_p, tg_p)):
    nt_mat.node_tree.links.new(bsdf.outputs[0], out.inputs["Surface"])
    nt_mat.node_tree.nodes.active = tg[1]
bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", margin=12, use_clear=False)
print("BAKED normals")

# where the AI mesh's shading normals fight its faces (slivers, folds) the bake comes out steeply
# tilted: flatten those few texels rather than light them oddly
import numpy as np
px = np.empty(RES * RES * 4, dtype=np.float32)
normal_img.pixels.foreach_get(px)
px = px.reshape(-1, 4)
steep = px[:, 2] * 2 - 1 < 0.6
px[steep, :3] = (0.5, 0.5, 1.0)
# and the bake's faint noise on the flat plates flattened too (it only bloats the PNG)
faint = np.abs(px[:, :2] - 0.5).max(axis=1) < 0.012
px[faint, :3] = (0.5, 0.5, 1.0)
normal_img.pixels.foreach_set(px.ravel())
print(f"flattened {steep.mean() * 100:.2f}% of the normal map")
normal_path = stem + "_normal.png"
normal_img.filepath_raw = normal_path
normal_img.file_format = "PNG"
normal_img.save()
print("WROTE", normal_path)

# the exported model: one material with the baked colour on the new UVs, the old UVs gone
ship.data.materials.clear()
final = bpy.data.materials.new("Cruiser2")
final.use_nodes = True
nt = final.node_tree
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = base_img
nt.links.new(tex.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"])
ship.data.materials.append(final)
for p in ship.data.polygons:
    p.material_index = 0
ship.data.uv_layers.remove(ship.data.uv_layers["AtlasUV"])
ship.data.color_attributes.remove(ship.data.color_attributes["tone"])
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=False, export_attributes=False,
                          export_image_format="JPEG", export_image_quality=95)   # (JPEG, like the other retextured GLBs)
print("WROTE", OUT)
