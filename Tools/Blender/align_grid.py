"""Gridded views of an AI-made ship in the frame convert_lizards.py works in, to measure a spec from,
and (with --marks) the spec's turret boxes, engines, guns and points drawn on it, to check one.

Run:  blender -b --python align_grid.py -- <spec.json> <ai_models_dir> <out_dir> <id>
                [--align '<json>'] [--sym] [--marks] [--islands N] [--views top,side,...]
                [--zoom x0,x1,y0,y1,z0,z1]

  The ship is aligned with its spec entry's "align" (or --align, a JSON object of the same keys,
  e.g. '{"turn": "0"}' for the raw model only centred and scaled), exactly as convert_lizards.py
  does: bow at -X, +Z up, keel on z = 0, 1 unit long in X, centred in X and Y.
  "drop" boxes in the align are deleted as convert_lizards.py does.
  --sym      search the yaw/roll (degrees, applied after the align steps) that make the model most
             mirror-symmetric about the XZ plane, and print them (sym_yaw / sym_roll for the spec)
  --marks    draw the spec: turret housing boxes (orange), exclude boxes (grey), engines (red discs
             facing their exhaust), fixed guns (cyan: mount, muzzles), points by kind (coloured balls
             with a short bar along their direction), ciws (yellow), missile cells (purple)
  --islands  list the loose parts (seams welded) with N+ vertices, with their bounds in this frame
  --views    any of top, bottom, side (from port, -Y), starboard, stern (from +X), bow (from -X),
             persp, persp_aft, persp_below (default: all)
  --probe    rays at the hull, ';'-separated: t:x,y from above, b:x,y from below, s:x,z from
             starboard, p:x,z from port, a:y,z from astern, f:y,z from ahead; prints the first hit
  --zoom     limit the views to this box (a close-up), grid lines then every 0.01 / 0.05

Views (the WROTE line prints each camera's screen right and up): top has screen right = +X (the
bow at the left), screen up = +Y (starboard); side (from port) has screen right = +X, up = +Z; stern
looks forward, screen right = +Y; bottom has screen right = +X, up = -Y. Grid lines every 0.05 units (thick every
0.1); the red line is x = 0 or y = 0, the green one the other axis. Writes <out_dir>/<id>_<view>.png.
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Matrix, Vector, kdtree

argv = sys.argv[sys.argv.index("--") + 1:]


def opt(name, default=None, flag=False):
    if name in argv:
        i = argv.index(name)
        if flag:
            del argv[i]
            return True
        v = argv[i + 1]
        del argv[i:i + 2]
        return v
    return False if flag else default


ALIGN = opt("--align")
SYM = opt("--sym", flag=True)
MARKS = opt("--marks", flag=True)
ISL = opt("--islands")
VIEWS = opt("--views")
ZOOM = opt("--zoom")
PROBE = opt("--probe")
SPEC, AI, OUT, ID = argv[:4]
os.makedirs(OUT, exist_ok=True)
spec = json.load(open(SPEC))
ship = next((s for s in spec.get("ships", []) + spec.get("frigates", []) if s["id"] == ID), None)
if ship is None:
    ship = {"id": ID, "source": ID if ID.endswith(".glb") else ID + ".glb", "align": {"turn": "0"}}
a = json.loads(ALIGN) if ALIGN else ship["align"]
AXES = {"+X": (1, 0, 0), "-X": (-1, 0, 0), "+Y": (0, 1, 0), "-Y": (0, -1, 0), "+Z": (0, 0, 1), "-Z": (0, 0, -1)}


def long_axis(co):
    co = co - co.mean(axis=0)
    w, vecs = np.linalg.eigh(np.cov(co.T))
    return vecs[:, np.argmax(w)]


def pts(me):
    return np.array([tuple(v.co) for v in me.vertices])


def align_steps(me, a, sym=True):
    """convert_lizards.py's align(), step for step (without the final normalise when sym is False)."""
    if a.get("up"):
        u = Vector(a["up"]).normalized()
        me.transform(u.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4())
    turn = str(a.get("turn", "0"))
    if turn.startswith("pca"):
        ax = long_axis(pts(me)[:, :2])
        ang = -math.atan2(ax[1], ax[0]) + math.radians(float(turn[3:] or 0))
    else:
        ang = math.radians(float(turn))
    me.transform(Matrix.Rotation(ang, 4, "Z"))
    if a.get("long3d"):
        v = Vector(long_axis(pts(me)))
        if v.x > 0:
            v = -v
        me.transform(v.rotation_difference(Vector((-1, 0, 0))).to_matrix().to_4x4())
    if a.get("pitch"):
        me.transform(Matrix.Rotation(math.radians(a["pitch"]), 4, "Y"))
    if a.get("roll"):
        me.transform(Matrix.Rotation(math.radians(a["roll"]), 4, "X"))
    if sym and (a.get("sym_yaw") or a.get("sym_roll")):
        me.transform(Matrix.Rotation(math.radians(a.get("sym_roll", 0)), 4, "X")
                     @ Matrix.Rotation(math.radians(a.get("sym_yaw", 0)), 4, "Z"))


def inbox(p, b):
    if isinstance(b[0], list):
        return any(inbox(p, bb) for bb in b)
    return b[0] <= p.x <= b[1] and b[2] <= p.y <= b[3] and b[4] <= p.z <= b[5]


def normalise(me):
    co = pts(me)
    lo, hi = co.min(axis=0), co.max(axis=0)
    s = 1.0 / (hi[0] - lo[0])
    c = (lo + hi) / 2
    me.transform(Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-c[0], -c[1], -lo[2]))))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(AI, ship["source"]))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print("MESHES", len(meshes), "verts", [len(o.data.vertices) for o in meshes])
hull = meshes[0]
hull.data.transform(hull.matrix_world)
hull.parent = None
hull.matrix_basis.identity()
for o in list(bpy.context.scene.objects):
    if o is not hull:
        bpy.data.objects.remove(o, do_unlink=True)

if SYM:
    align_steps(hull.data, a, sym=False)
    normalise(hull.data)
    co = pts(hull.data)
    rng = np.random.default_rng(1)
    sample = co[rng.choice(len(co), min(4000, len(co)), replace=False)]
    kd = kdtree.KDTree(len(co))
    for i, p in enumerate(co):
        kd.insert(p, i)
    kd.balance()

    def cost(yaw, roll):
        R = np.array(Matrix.Rotation(math.radians(roll), 3, "X") @ Matrix.Rotation(math.radians(yaw), 3, "Z"))
        q = sample @ R.T
        # mirrored about y = c, the middle of the rotated model's width
        R_all = co @ R.T
        c = (R_all[:, 1].max() + R_all[:, 1].min()) / 2
        m = q.copy()
        m[:, 1] = 2 * c - m[:, 1]
        # distances from each mirrored sample point to the (unrotated) tree: rotate back
        back = m @ R
        return float(np.mean([kd.find(p)[2] for p in back]))

    best = (cost(0, 0), 0.0, 0.0)
    for step, span in ((2.0, 16), (0.5, 2.0), (0.1, 0.5)):
        y0, r0 = best[1], best[2]
        for yaw in np.arange(y0 - span, y0 + span + 1e-6, step):
            for roll in np.arange(r0 - span, r0 + span + 1e-6, step):
                c = cost(yaw, roll)
                if c < best[0]:
                    best = (c, round(float(yaw), 2), round(float(roll), 2))
        print("SYM step", step, "best yaw", best[1], "roll", best[2], "cost", round(best[0], 5))
    print("SYM", json.dumps({"sym_yaw": best[1], "sym_roll": best[2]}), "unrotated cost", round(cost(0, 0), 5))
    hull.data.transform(Matrix.Rotation(math.radians(best[2]), 4, "X") @ Matrix.Rotation(math.radians(best[1]), 4, "Z"))
else:
    align_steps(hull.data, a)
if a.get("drop"):
    normalise(hull.data)
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    faces = [f for f in bm.faces if inbox(f.calc_center_median(), a["drop"])]
    bmesh.ops.delete(bm, geom=faces, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(hull.data)
    bm.free()
    print("DROPPED", len(faces), "faces")
normalise(hull.data)
co = pts(hull.data)
lo, hi = co.min(axis=0), co.max(axis=0)
print("ALIGNED bounds", np.round(lo, 4).tolist(), np.round(hi, 4).tolist(), "size", np.round(hi - lo, 4).tolist())

if PROBE:
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    bvh = BVHTree.FromBMesh(bm)
    rays = {"t": lambda a, b: ((a, b, 2), (0, 0, -1)), "b": lambda a, b: ((a, b, -2), (0, 0, 1)),
            "s": lambda a, b: ((a, 2, b), (0, -1, 0)), "p": lambda a, b: ((a, -2, b), (0, 1, 0)),
            "a": lambda a, b: ((2, a, b), (-1, 0, 0)), "f": lambda a, b: ((-2, a, b), (1, 0, 0))}
    for ray in PROBE.split(";"):
        k, xy = ray.split(":")
        a_, b_ = (float(t) for t in xy.split(","))
        o, d = rays[k](a_, b_)
        hit, nrm, _, _ = bvh.ray_cast(Vector(o), Vector(d))
        print("PROBE", ray, "hit", None if hit is None else tuple(round(c, 4) for c in hit),
              "normal", None if nrm is None else tuple(round(c, 3) for c in nrm))
    bm.free()

if ISL:
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.verts.ensure_lookup_table()
    seen, isl = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, part = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            part.append(x.co.copy())
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        isl.append(part)
    isl.sort(key=lambda p: -len(p))
    print(f"ISLANDS {len(isl)} total, {sum(1 for p in isl if len(p) >= int(ISL))} with {ISL}+ verts")
    for p in isl:
        if len(p) < int(ISL):
            continue
        l = [min(q[i] for q in p) for i in range(3)]
        h = [max(q[i] for q in p) for i in range(3)]
        print(f"ISLAND {len(p):6d}  x {l[0]:+.3f}..{h[0]:+.3f}  y {l[1]:+.3f}..{h[1]:+.3f}  z {l[2]:+.3f}..{h[2]:+.3f}")
    bm.free()

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.03, 0.03, 0.05)
mats = {}


def mat(rgb):
    key = tuple(rgb)
    if key not in mats:
        m = bpy.data.materials.new("m%d" % len(mats))
        m.diffuse_color = (*rgb, 1)
        mats[key] = m
    return mats[key]


def bar(a, b, w, rgb):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=w, depth=max(d.length, 1e-6), location=(a + b) / 2)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler() if d.length > 1e-9 else (0, 0, 0)
    o.data.materials.append(mat(rgb))
    return o


def ball(p, r, rgb):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=Vector(p), segments=16, ring_count=8)
    bpy.context.object.data.materials.append(mat(rgb))


def box(b, rgb, w=0.0015):
    if isinstance(b[0], list):
        for bb in b:
            box(bb, rgb, w)
        return
    x0, x1, y0, y1, z0, z1 = b
    c = [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
    for i in range(8):
        for j in range(i + 1, 8):
            if sum(1 for k in range(3) if c[i][k] != c[j][k]) == 1:
                bar(c[i], c[j], w, rgb)


def dirv(d):
    return Vector(AXES[d]) if isinstance(d, str) else Vector(d).normalized()


KIND = {"designator": (1, 0.1, 0.1), "sensor": (0.1, 1, 0.3), "drone_bay": (0.2, 0.5, 1), "ecm": (1, 0.2, 1),
        "emp": (0.6, 0.9, 1), "tractor": (0.1, 1, 1), "abduct": (1, 1, 1), "ciws": (1, 1, 0.1),
        "missile_cell": (0.7, 0.2, 1), "boarding_pod": (1, 0.6, 0.1)}
if MARKS:
    for t in ship.get("turrets", []):
        box(t["housing"], (1, 0.45, 0.05))
        if t.get("exclude"):
            box(t["exclude"], (0.5, 0.5, 0.5))
        if t.get("pivot"):
            ball(t["pivot"], 0.006, (1, 0.45, 0.05))
    for e in ship.get("engines", []):
        d = dirv(e.get("dir", (1, 0, 0)))
        p = Vector(e["pos"])
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=e["r"], depth=0.004, location=p)
        o = bpy.context.object
        o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        o.data.materials.append(mat((1, 0.1, 0.05)))
        bar(p, p + d * 0.05, 0.002, (1, 0.1, 0.05))
    for g in ship.get("guns", []):
        ball(g["pos"], 0.006, (0, 0.9, 0.9))
        for m in g["muzzles"]:
            ball(m, 0.005, (0.4, 1, 1))
            bar(m, Vector(m) + dirv(g["facing"]) * 0.04, 0.0015, (0.4, 1, 1))
    for p in ship.get("points", []):
        rgb = KIND.get(p["kind"], (0.9, 0.9, 0.9))
        ball(p["pos"], max(p.get("size", 0.012), 0.006), rgb)
        bar(p["pos"], Vector(p["pos"]) + dirv(p["dir"]) * 0.06, 0.002, rgb)
    for x, y in ship.get("ciws", []):
        ball((x, y, hi[2]), 0.01, (1, 1, 0.1))
    for c in ship.get("missile_cells", []):
        ball(c, 0.008, (0.7, 0.2, 1))

cd = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cd)
scene.collection.objects.link(cam)
scene.camera = cam
if ZOOM:
    z = [float(v) for v in ZOOM.split(",")]
    vlo, vhi, fine = np.array([z[0], z[2], z[4]]), np.array([z[1], z[3], z[5]]), True
else:
    vlo, vhi, fine = lo, hi, False
mid = (vlo + vhi) / 2
ext = vhi - vlo
grid_objs = []


def grid(view):
    """Grid lines on a plane just behind the model (as seen from the camera)."""
    for o in grid_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    grid_objs.clear()
    step, thick = (0.01, 5) if fine else (0.05, 2)
    pad = 0.06
    if view in ("top", "bottom"):
        axes, depth_axis, depth = (0, 1), 2, (vlo[2] - 0.02 if view == "top" else vhi[2] + 0.02)
    elif view in ("side", "starboard"):
        axes, depth_axis, depth = (0, 2), 1, (vhi[1] + 0.02 if view == "side" else vlo[1] - 0.02)
    else:
        axes, depth_axis, depth = (1, 2), 0, (vlo[0] - 0.02 if view == "stern" else vhi[0] + 0.02)
    for k, other in ((axes[0], axes[1]), (axes[1], axes[0])):
        n0 = int(math.floor((vlo[k] - pad) / step))
        n1 = int(math.ceil((vhi[k] + pad) / step))
        for n in range(n0, n1 + 1):
            v = n * step
            a = [0, 0, 0]
            b = [0, 0, 0]
            a[k] = b[k] = v
            a[other], b[other] = vlo[other] - pad, vhi[other] + pad
            a[depth_axis] = b[depth_axis] = depth
            if n == 0:
                rgb, w = ((0.9, 0.1, 0.1) if k == axes[0] else (0.1, 0.8, 0.1)), 0.0016 * (0.3 if fine else 1)
            else:
                rgb = (0.75, 0.75, 0.8)
                w = (0.0012 if n % thick == 0 else 0.0004) * (0.3 if fine else 1)
            grid_objs.append(bar(a, b, w, rgb))


ortho = {
    "top": ((0, 0, 1), (0, 1, 0)), "bottom": ((0, 0, -1), (0, -1, 0)),
    "side": ((0, -1, 0), (0, 0, 1)), "starboard": ((0, 1, 0), (0, 0, 1)),
    "stern": ((1, 0, 0), (0, 0, 1)), "bow": ((-1, 0, 0), (0, 0, 1)),
}
persp = {"persp": (-0.9, -1.0, 0.8), "persp_aft": (1.0, -0.9, 0.7), "persp_below": (-0.8, 0.9, -0.6),
         "persp_stbd": (-0.6, 1.0, 0.7)}
want = [] if VIEWS == "none" else VIEWS.split(",") if VIEWS else list(ortho) + list(persp)
R = float(max(ext)) * 1.2
for name in want:
    if name in ortho:
        d, up = Vector(ortho[name][0]), Vector(ortho[name][1])
        grid(name)
        cd.type = "ORTHO"
        cam.location = Vector(mid) + d * 5
        right = up.cross(d)
        cam.rotation_euler = Matrix((right, up, d)).transposed().to_euler()
        # the screen extent of the view: the two axes in the image plane
        w = abs(right.dot(Vector(ext))) + 0.12
        h = abs(up.dot(Vector(ext))) + 0.12
        cd.ortho_scale = max(w, h * 1.6)
        cd.clip_end = 20
    else:
        for o in grid_objs:
            bpy.data.objects.remove(o, do_unlink=True)
        grid_objs.clear()
        cd.type = "PERSP"
        cd.lens = 40
        cam.location = Vector(mid) + Vector(persp[name]).normalized() * R * 1.35
        cam.rotation_euler = (Vector(mid) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, f"{ID}_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath, "screen right", (cam.matrix_world.to_3x3() @ Vector((1, 0, 0))).to_tuple(2),
          "up", (cam.matrix_world.to_3x3() @ Vector((0, 1, 0))).to_tuple(2))
