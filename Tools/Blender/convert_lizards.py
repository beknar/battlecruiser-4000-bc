"""Convert the AI-made lizardman frigates (lizard_spec.json) into FBX models for Unity, each with its
colour texture and a hardpoints JSON. A copy of Tools/Blender/convert_frigates.py (same frame, same
turret cutting, same JSON layout) with these additions:

  * the alignment is done here, from the raw GLB, with the steps of align_glb.py (turn about Z, the
    3D long axis onto X) plus the small yaw/roll that make the model mirror-symmetric (found by
    align_grid.py --sym and recorded in the spec), then scaled to 1 unit long, centred, keel on z = 0,
    bow at -X. Spec coordinates are in that frame (the frame of work/lfN.glb).
  * "housing" may be a list of boxes (their union), and with housing_mode "faces" an "exclude" box
    (or list) keeps faces in it out (small deck spikes inside a turret's box); housing_mode "box"
    is "faces" with the hull's own part included too (models made as one piece, where a turret is
    part of the hull: every face whose middle is in the box is cut);
  * align "drop": boxes of faces deleted (stray slivers off the hull), see drop();
  * engines may give their own "dir" (default: astern);
  * "guns": fixed guns left in the hull (a mount point, a facing, muzzles);
  * "points": role-specific points (tractor emitter, ion muzzles, breach point, boarding-pod
    launchers, spinal muzzle, point-defence mounts, ...), each with a kind, position and direction;
  * the texture is written as a real PNG (convert_frigates.py's img.save() writes the packed JPEG
    bytes under a .png name), optionally downscaled (--tex-size, default 2048), and with
    "tex_levels" [lift, gain] brought up from near black;

Run:  blender -b --python convert_lizards.py -- <spec.json> <ai_models_dir> <models_dir> <data_dir>
                                               [id ...] [--preview <dir>] [--tex-size N]

Unity frame as in convert_frigates.py: bow +Z, up +Y; Unity position = (x, z, y) of the exported
(Blender) coordinates, Blender = spec frame scaled and turned -90 deg about Z (spec -X -> Blender +Y).
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
PREVIEW = None
TEX = 2048
if "--preview" in argv:
    i = argv.index("--preview"); PREVIEW = argv[i + 1]; del argv[i:i + 2]
if "--tex-size" in argv:
    i = argv.index("--tex-size"); TEX = int(argv[i + 1]); del argv[i:i + 2]
SPEC, AI, MODELS, DATA = argv[:4]
ONLY = set(argv[4:])
AXES = {"+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)),
        "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}


def vec(v):
    return AXES[v] if isinstance(v, str) else Vector(v).normalized()


def components(me, weld):
    """Each vertex's component: vertices joined by edges, and (with weld) at the same position."""
    parent = list(range(len(me.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(a, b):
        a, b = find(a), find(b)
        if a != b:
            parent[a] = b

    if weld:
        first = {}
        for v in me.vertices:
            key = (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5))
            if key in first:
                union(v.index, first[key])
            else:
                first[key] = v.index
    for e in me.edges:
        union(e.vertices[0], e.vertices[1])
    return [find(i) for i in range(len(me.vertices))]


def islands(obj):
    comp = components(obj.data, True)
    return [comp[p.vertices[0]] for p in obj.data.polygons]


def in_box(p, b):
    if isinstance(b[0], list):
        return any(in_box(p, bb) for bb in b)
    return b[0] <= p.x <= b[1] and b[2] <= p.y <= b[3] and b[4] <= p.z <= b[5]


def select_faces(obj, box, mode, part=None, exclude=None):
    """As convert_frigates.py: whole loose parts whose middle is in the box ("islands"), or every
    face whose middle is in it, leaving out the hull's own (biggest) part ("faces"). The box may be
    a list of boxes."""
    me = obj.data
    if part is None:
        part = islands(obj)
    sizes = {}
    for p in part:
        sizes[p] = sizes.get(p, 0) + 1
    hull = max(sizes, key=sizes.get)
    if mode in ("faces", "box"):
        return [i for i, poly in enumerate(me.polygons) if (mode == "box" or part[i] != hull) and in_box(poly.center, box)
                and not (exclude and in_box(poly.center, exclude))]
    lo, hi = {}, {}
    for i, poly in enumerate(me.polygons):
        for vi in poly.vertices:
            c = me.vertices[vi].co
            k = part[i]
            if k not in lo:
                lo[k], hi[k] = c.copy(), c.copy()
            else:
                lo[k] = Vector(map(min, lo[k], c))
                hi[k] = Vector(map(max, hi[k], c))
    chosen = {k for k in lo if in_box((lo[k] + hi[k]) / 2, box)}
    chosen.discard(hull)
    return [i for i in range(len(me.polygons)) if part[i] in chosen]


def cut(hull, faces, name):
    if not faces:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = hull
    hull.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="DESELECT")
    bm = bmesh.from_edit_mesh(hull.data)
    bm.faces.ensure_lookup_table()
    for i in faces:
        bm.faces[i].select_set(True)
    bm.select_flush_mode()
    bmesh.update_edit_mesh(hull.data)
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = [o for o in bpy.context.selected_objects if o is not hull][0]
    new.name = new.data.name = name
    return new


def bounds(obj):
    vs = [v.co for v in obj.data.vertices]
    return Vector(map(min, *vs)), Vector(map(max, *vs))


def bounds_of(points):
    return (Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
            Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))))


def barrel_muzzles(obj, facing):
    me = obj.data
    part = islands(obj)
    groups = {}
    for i, poly in enumerate(me.polygons):
        groups.setdefault(part[i], set()).update(poly.vertices)
    out = []
    for vs in groups.values():
        pts = [me.vertices[v].co.copy() for v in vs]
        if len(pts) < 20:
            continue
        lo, hi = bounds_of(pts)
        mid = (lo + hi) / 2
        far = max(p.dot(facing) for p in pts)
        out.append(mid + facing * (far - mid.dot(facing)))
    return out


def front_muzzles(obj, facing, up, single=False):
    """The front of the housing, either side of its middle (or one muzzle in the middle)."""
    pts = [v.co.copy() for v in obj.data.vertices]
    far = max(p.dot(facing) for p in pts)
    near = min(p.dot(facing) for p in pts)
    front = [p for p in pts if p.dot(facing) > far - 0.15 * (far - near)]
    side = facing.cross(up).normalized()
    lo, hi = bounds_of(front)
    mid = (lo + hi) / 2
    if single:
        return [mid + facing * (far - mid.dot(facing))]
    out = []
    for s in (-1, 1):
        half = [p for p in front if (p - mid).dot(side) * s > 0]
        if not half:
            continue
        hlo, hhi = bounds_of(half)
        c = (hlo + hhi) / 2
        out.append(c + facing * (far - c.dot(facing)))
    return out


def long_axis(co):
    co = co - co.mean(axis=0)
    w, vecs = np.linalg.eigh(np.cov(co.T))
    return vecs[:, np.argmax(w)]


def align(hull, a):
    """align_glb.py's steps (--up, turn, --long3d, --pitch, --roll), then the symmetry yaw/roll,
    then 1 unit long, centred in X and Y, keel on z = 0."""
    def pts():
        return np.array([tuple(v.co) for v in hull.data.vertices])
    if a.get("up"):
        u = Vector(a["up"]).normalized()
        hull.data.transform(u.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4())
    turn = str(a.get("turn", "0"))
    if turn.startswith("pca"):
        ax = long_axis(pts()[:, :2])
        ang = -math.atan2(ax[1], ax[0]) + math.radians(float(turn[3:] or 0))
    else:
        ang = math.radians(float(turn))
    hull.data.transform(Matrix.Rotation(ang, 4, "Z"))
    if a.get("long3d"):
        v = Vector(long_axis(pts()))
        if v.x > 0:
            v = -v
        hull.data.transform(v.rotation_difference(Vector((-1, 0, 0))).to_matrix().to_4x4())
    if a.get("pitch"):
        hull.data.transform(Matrix.Rotation(math.radians(a["pitch"]), 4, "Y"))
    if a.get("roll"):
        hull.data.transform(Matrix.Rotation(math.radians(a["roll"]), 4, "X"))
    if a.get("sym_yaw") or a.get("sym_roll"):
        hull.data.transform(Matrix.Rotation(math.radians(a.get("sym_roll", 0)), 4, "X")
                            @ Matrix.Rotation(math.radians(a.get("sym_yaw", 0)), 4, "Z"))
    if a.get("drop"):
        normalise(hull)
        drop(hull, a["drop"])
    normalise(hull)
    print("ALIGNED", round(math.degrees(ang), 2), "deg about Z")


def drop(hull, box):
    """Delete the faces whose middle is in the box (or boxes), in the frame normalised once before
    the drop (stray slivers beyond the hull, e.g. a loose needle off the bow), and loose vertices."""
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    faces = [f for f in bm.faces if in_box(f.calc_center_median(), box)]
    bmesh.ops.delete(bm, geom=faces, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(hull.data)
    bm.free()
    print("DROPPED", len(faces), "faces")


def normalise(hull):
    """1 unit long, centred in X and Y, keel on z = 0."""
    co = np.array([tuple(v.co) for v in hull.data.vertices])
    lo, hi = co.min(axis=0), co.max(axis=0)
    s = 1.0 / (hi[0] - lo[0])
    c = (lo + hi) / 2
    hull.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-c[0], -c[1], -lo[2]))))


def preview(name, hull, turret_parts):
    """Renders (--preview <dir>) of the cut, as convert_frigates.py: each turret tinted and turned 25
    degrees about its pivot, so what was cut out (and only that) shows."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x, scene.render.resolution_y = 1000, 600
    scene.world = bpy.data.worlds.new("w")
    hull.color = (0.6, 0.6, 0.62, 1)
    for yaw, pitch, pivot, up in turret_parts:
        turn = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(25), 4, up) @ Matrix.Translation(-pivot)
        yaw.color = (1.0, 0.45, 0.1, 1)
        yaw.matrix_world = turn
        if pitch is not None:
            pitch.color = (0.2, 0.8, 1.0, 1)
            pitch.matrix_world = turn
    lo, hi = bounds(hull)
    mid = (lo + hi) / 2
    cd = bpy.data.cameras.new("pc")
    cam = bpy.data.objects.new("pc", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for view, pos in (("above", (-0.55, -0.65, 0.75)), ("below", (-0.55, 0.65, -0.45)), ("aboveaft", (0.6, -0.6, 0.75))):
        cam.location = Vector(pos) + Vector((0, 0, mid.z))
        cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
        cd.lens = 32
        scene.render.filepath = os.path.join(PREVIEW, f"cut_{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    for yaw, pitch, _, _ in turret_parts:
        yaw.matrix_world = Matrix.Identity(4)
        if pitch is not None:
            pitch.matrix_world = Matrix.Identity(4)


def save_png(img, path, size, levels=None):
    """Write the texture as a real PNG (downscaled to size if bigger). levels [lift, gain]: each
    colour becomes lift + gain * it (0..1), to bring a near-black paint up to a dark grey that
    shows against space (the Greys' Shroud and Reaper)."""
    w, h = img.size
    copy = img.copy()
    if size and max(w, h) > size:
        copy.scale(size, size * h // w)
    if levels:
        lift, gain = levels
        px = np.empty(len(copy.pixels), dtype=np.float32)
        copy.pixels.foreach_get(px)
        px = px.reshape(-1, 4)
        px[:, :3] = np.clip(lift + gain * px[:, :3], 0.0, 1.0)
        copy.pixels.foreach_set(px.ravel())
        print("TEXTURE LEVELS", lift, gain)
    scene = bpy.context.scene
    s = scene.render.image_settings
    s.file_format = "PNG"
    s.color_mode = "RGB"
    s.color_depth = "8"
    s.compression = 90
    copy.save_render(path, scene=scene)
    print("TEXTURE", path, copy.size[0], copy.size[1])


def convert(f):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(AI, f["source"]))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    assert len(meshes) == 1, f"expected one mesh, found {len(meshes)}"
    hull = meshes[0]
    hull.data.transform(hull.matrix_world)
    hull.parent = None
    hull.matrix_basis.identity()
    for o in list(bpy.context.scene.objects):
        if o is not hull:
            bpy.data.objects.remove(o, do_unlink=True)
    hull.name = hull.data.name = "Hull"
    align(hull, f["align"])
    tris = sum(len(p.vertices) - 2 for p in hull.data.polygons)

    lo, hi = bounds(hull)
    centre = (lo + hi) / 2
    scale = f["length"] / (hi.x - lo.x)
    M = Matrix.Scale(scale, 4) @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation(-centre)
    R = M.to_3x3().normalized()

    def unity(p):
        q = M @ Vector(p)
        return [round(q.x, 3), round(q.z, 3), round(q.y, 3)]

    def unity_dir(d):
        q = (R @ vec(d)).normalized()
        return [round(q.x, 4), round(q.z, 4), round(q.y, 4)]

    turrets, parts, turret_parts = [], [], []
    for t in f.get("turrets", []):
        facing, up = vec(t["facing"]), vec(t["up"])
        pitch = None
        if "barrels" in t:
            pitch = cut(hull, select_faces(hull, t["barrels"], "islands"), t["name"] + "_Pitch")
        yaw = cut(hull, select_faces(hull, t["housing"], t.get("housing_mode", "islands"), exclude=t.get("exclude")), t["name"] + "_Yaw")
        assert yaw is not None, f"{f['id']} {t['name']}: nothing in the housing box"
        ylo, yhi = bounds(yaw)
        if "pivot" in t:
            yaw_pivot = Vector(t["pivot"])
        else:
            yaw_pivot = (ylo + yhi) / 2
            foot = min(v.co.dot(up) for v in yaw.data.vertices)
            yaw_pivot += up * (foot - yaw_pivot.dot(up))
        if pitch is not None:
            plo, phi = bounds(pitch)
            pitch_pivot = (plo + phi) / 2
            breech = min(v.co.dot(facing) for v in pitch.data.vertices)
            pitch_pivot += facing * (breech - pitch_pivot.dot(facing))
            muzzles = barrel_muzzles(pitch, facing)
        else:
            pitch_pivot = (ylo + yhi) / 2
            muzzles = front_muzzles(yaw, facing, up, t.get("single_muzzle", False))
        print(f"TURRET {f['id']} {t['name']}: housing {len(yaw.data.vertices)} verts, "
              f"barrels {len(pitch.data.vertices) if pitch else 0} verts, {len(muzzles)} muzzles")
        turrets.append({
            "name": t["name"],
            "yaw_mesh": yaw.name, "pitch_mesh": pitch.name if pitch else "",
            "yaw_pivot": unity(yaw_pivot), "pitch_pivot": unity(pitch_pivot),
            "facing": unity_dir(facing), "up": unity_dir(up),
            "muzzles": [{"pos": unity(m)} for m in muzzles],
            **({"arc": t["arc"]} if "arc" in t else {}),
        })
        parts.append((yaw, yaw_pivot))
        if pitch is not None:
            parts.append((pitch, pitch_pivot))
        turret_parts.append((yaw, pitch, yaw_pivot, up))

    if PREVIEW and turret_parts:
        preview(f["id"], hull, turret_parts)

    hull.data.transform(M)
    for o, pivot in parts:
        o.data.transform(Matrix.Translation(-(M @ pivot)) @ M)

    engines = [{"exit": unity(e["pos"]), "dir": unity_dir(e.get("dir", (1, 0, 0))), "radius": round(e["r"] * scale, 2)}
               for e in f["engines"]]
    ciws = [{"x": unity((x, y, 0))[0], "z": unity((x, y, 0))[2]} for x, y in f.get("ciws", [])]

    os.makedirs(MODELS, exist_ok=True)
    os.makedirs(DATA, exist_ok=True)
    mat = hull.data.materials[0] if hull.data.materials else None
    img = next((n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None) if mat and mat.use_nodes else None
    pre = f.get("prefix", "Frigate")
    if img:
        save_png(img, os.path.join(MODELS, f"{pre}_{f['id']}_BaseColor.png"), TEX, f.get("tex_levels"))
        img.filepath_raw = os.path.join(MODELS, f"{pre}_{f['id']}_BaseColor.png")   # what the FBX's material names

    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            o.select_set(True)
    bpy.context.view_layer.objects.active = hull
    fbx = os.path.join(MODELS, f"{pre}_{f['id']}.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
                             path_mode="STRIP")
    print("EXPORTED", fbx)

    hlo, hhi = bounds(hull)
    hp = {"id": f["id"], "faction": f.get("faction", ""), "role": f.get("role", ""), "length": f["length"],
          "turrets": turrets, "engines": engines, "ciws": ciws,
          "bounds_size": [round(hhi.x - hlo.x, 2), round(hhi.z - hlo.z, 2), round(hhi.y - hlo.y, 2)],
          "bounds_min": [round(hlo.x, 2), round(hlo.z, 2), round(hlo.y, 2)],
          "bounds_max": [round(hhi.x, 2), round(hhi.z, 2), round(hhi.y, 2)],
          "tris": tris}
    # the whole model's box (hull and turrets), in Unity's frame
    corners = [unity((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    hp["model_bounds_min"] = [round(min(c[i] for c in corners), 2) for i in range(3)]
    hp["model_bounds_max"] = [round(max(c[i] for c in corners), 2) for i in range(3)]
    sides = {"port": (0, -1, 0), "starboard": (0, 1, 0), "top": (0, 0, 1), "keel": (0, 0, -1)}
    if f.get("extra_mounts"):
        hp["mounts"] = [{"pos": unity(m["pos"]), "up": unity_dir(sides[m["side"]]), "side": m["side"]} for m in f["extra_mounts"]]
    if f.get("missile_cells"):
        hp["cells"] = [{"pos": unity(c)} for c in f["missile_cells"]]
    if f.get("guns"):
        hp["guns"] = [{"name": g["name"], "pos": unity(g["pos"]), "facing": unity_dir(g["facing"]),
                       "muzzles": [{"pos": unity(m)} for m in g["muzzles"]]} for g in f["guns"]]
    if f.get("points"):
        hp["points"] = [{"name": p["name"], "kind": p["kind"], "pos": unity(p["pos"]), "dir": unity_dir(p["dir"]),
                         **({"size": round(p["size"] * scale, 2)} if "size" in p else {})} for p in f["points"]]
    with open(os.path.join(DATA, f"{pre.lower()}_{f['id'].lower()}_hardpoints.json"), "w") as fh:
        json.dump(hp, fh, indent=1)
    print("HARDPOINTS", f["id"], "tris", tris, "hull bounds", hp["bounds_size"], "min", hp["bounds_min"], "max", hp["bounds_max"],
          "model min", hp["model_bounds_min"], "max", hp["model_bounds_max"])


spec = json.load(open(SPEC))
for f in spec.get("frigates", []) + spec.get("ships", []):
    if not ONLY or f["id"] in ONLY:
        convert(f)
