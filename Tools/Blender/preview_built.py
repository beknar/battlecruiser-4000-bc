"""Render built ships (the FBX and hardpoints JSON that convert_lizards.py wrote) with their hardpoints
drawn on, to check the whole conversion: that the bow is forward (Unity +Z), the ship upright, the
turrets back on their pivots and every point on the hull where it was meant to be.

Run:  blender -b --python preview_built.py -- <models_dir> <data_dir> <out_dir> <Prefix_Id> [...]
      e.g. ... Assets/Carrier/Models Assets/Carrier/Data /tmp/prev Greys_Scout Empire_Assault

Writes <out_dir>/<Prefix_Id>_<view>.png for the views persp (from ahead, to port, above), aft
(from astern, to starboard, above), below and top (orthographic, bow to the left, starboard up). Marks: turret pivots orange with their muzzles
cyan, engines red discs facing their exhaust, fixed-gun muzzles cyan, CIWS yellow (at the top of
the hull), missile cells purple, role points by kind (designator red, sensor green, drone_bay blue,
ecm magenta, emp pale blue, tractor cyan, abduct white, ciws yellow, missile_cell purple,
boarding_pod orange) with a bar along their direction. A small green cone at the bow marks +Z.
Hardpoint positions are Unity's (x, y, z) = Blender's (x, z, y) of the exported model.
"""
import bpy, json, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
MODELS, DATA, OUT = argv[:3]
os.makedirs(OUT, exist_ok=True)
KIND = {"designator": (1, 0.1, 0.1), "sensor": (0.1, 1, 0.3), "drone_bay": (0.2, 0.5, 1), "ecm": (1, 0.2, 1),
        "emp": (0.6, 0.9, 1), "tractor": (0.1, 1, 1), "abduct": (1, 1, 1), "ciws": (1, 1, 0.1),
        "missile_cell": (0.7, 0.2, 1), "boarding_pod": (1, 0.6, 0.1)}


def B(u):
    """Unity position/direction to Blender."""
    return Vector((u[0], u[2], u[1]))


def mat(rgb):
    m = bpy.data.materials.new("m")
    m.diffuse_color = (*rgb, 1)
    return m


def ball(p, r, rgb):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=p, segments=16, ring_count=8)
    bpy.context.object.data.materials.append(mat(rgb))


def bar(a, b, w, rgb):
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=w, depth=max(d.length, 1e-6), location=(a + b) / 2)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    o.data.materials.append(mat(rgb))


def disc(p, d, r, rgb):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=r * 0.15, location=p)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    o.data.materials.append(mat(rgb))


for name in argv[3:]:
    pre, sid = name.split("_", 1)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, name + ".fbx"))
    hp = json.load(open(os.path.join(DATA, f"{pre.lower()}_{sid.lower()}_hardpoints.json")))
    objs = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
    for o in objs.values():
        o.parent = None
    # the importer's scale: the hull's length along Y against the length the JSON gives
    hull = objs.get("Hull") or max(objs.values(), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    ys = [(hull.matrix_world @ v.co).y for v in hull.data.vertices]
    s = (max(ys) - min(ys)) / (hp["bounds_size"][2] or hp["length"])
    print("PREVIEW", name, "import scale", round(s, 4), "objects", sorted(objs))

    def P(u):
        return B(u) * s

    L = hp["length"] * s
    for t in hp.get("turrets", []):
        for mesh, piv in ((t["yaw_mesh"], t["yaw_pivot"]), (t["pitch_mesh"], t["pitch_pivot"])):
            o = objs.get(mesh)
            if o is not None:
                o.location = P(piv)
                o.color = (1, 0.55, 0.2, 1)
        ball(P(t["yaw_pivot"]), L * 0.008, (1, 0.45, 0.05))
        f = B(t["facing"]).normalized()
        for m in t["muzzles"]:
            ball(P(m["pos"]), L * 0.005, (0.3, 1, 1))
            bar(P(m["pos"]), P(m["pos"]) + f * L * 0.04, L * 0.0015, (0.3, 1, 1))
    for e in hp.get("engines", []):
        d = B(e["dir"]).normalized()
        disc(P(e["exit"]), d, e["radius"] * s, (1, 0.1, 0.05))
        bar(P(e["exit"]), P(e["exit"]) + d * L * 0.06, L * 0.002, (1, 0.1, 0.05))
    for g in hp.get("guns", []):
        f = B(g["facing"]).normalized()
        for m in g["muzzles"]:
            ball(P(m["pos"]), L * 0.005, (0.3, 1, 1))
            bar(P(m["pos"]), P(m["pos"]) + f * L * 0.05, L * 0.0015, (0.3, 1, 1))
    top = hp["model_bounds_max"][1]
    for c in hp.get("ciws", []):
        ball(P((c["x"], top, c["z"])), L * 0.008, (1, 1, 0.1))
    for c in hp.get("cells", []):
        ball(P(c["pos"]), L * 0.006, (0.7, 0.2, 1))
    for p in hp.get("points", []):
        rgb = KIND.get(p["kind"], (0.9, 0.9, 0.9))
        ball(P(p["pos"]), max(p.get("size", 0) * s, L * 0.006), rgb)
        bar(P(p["pos"]), P(p["pos"]) + B(p["dir"]).normalized() * L * 0.08, L * 0.002, rgb)
    # +Z (Unity forward) marker ahead of the bow
    bpy.ops.mesh.primitive_cone_add(radius1=L * 0.02, depth=L * 0.05, location=(0, hp["model_bounds_max"][2] * s + L * 0.06, 0),
                                    rotation=(-math.pi / 2, 0, 0))
    bpy.context.object.data.materials.append(mat((0.2, 1, 0.2)))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.world = bpy.data.worlds.new("w")
    scene.world.color = (0.03, 0.03, 0.05)
    cd = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cd.clip_end = L * 20
    mid = (B(hp["model_bounds_min"]) + B(hp["model_bounds_max"])) * s / 2
    for view, d in (("persp", (-0.8, 1.0, 0.7)), ("aft", (0.8, -1.0, 0.6)), ("below", (-0.7, 0.8, -0.7)), ("top", (0, 0, 1))):
        if view == "top":
            cd.type = "ORTHO"
            cd.ortho_scale = L * 1.25
            cam.location = mid + Vector((0, 0, L * 3))
            cam.rotation_euler = (0, 0, -math.pi / 2)     # bow to the left, starboard up
        else:
            cd.type = "PERSP"
            cd.lens = 40
            cam.location = mid + Vector(d).normalized() * L * 1.45
            cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print("WROTE", scene.render.filepath)
