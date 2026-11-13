"""Convert the new AI-made frigates (frigates_spec.json) into FBX models for Unity, each with its
colour texture and the hardpoints the scene builder needs: the turrets cut out of the hull so they
can turn, the engine exits, and where the CIWS mounts go.

Run:  blender -b --python convert_frigates.py -- <spec.json> <ai_models_dir> <models_dir> <data_dir> [id ...] [--preview <dir>]

Each model is one textured Tripo mesh, 1 unit long. As in convert_frigate.py (the starting frigate),
it is turned so the bow points +Y, centred, scaled to its length in metres, and exported so that in
Unity the bow points +Z and up is +Y: Unity position = (x, z, y) of the exported coordinates.

The turrets are modelled into the hull. Each is cut out by the boxes in the spec: its barrels (the
loose parts whose middle is in the barrels box) and its housing (the loose parts whose middle is in
the housing box, or with housing_mode "faces", every face whose middle is in it apart from the
hull's own). A barrel_faces box (or list of boxes) adds barrels modelled joined to their housing:
the pieces (between UV seams) of the housing's parts that lie wholly in it go with the barrels.
The mesh is split along its UV seams, so loose parts are found by joining vertices that share a
position. Every part is exported at the origin with its vertices relative to its own
pivot (the housing turns about its foot, the barrels about their breech), and the hardpoints give
where each pivot is, which way the turret faces at rest and which way is up for it, and the barrels'
muzzles. A turret without separate barrels gets muzzles at the front of the housing.

The spec's "frigates" and "ships" lists are both converted. A ship may give a "prefix" for its
files (default "Frigate"): writes <models_dir>/<Prefix>_<id>.fbx (objects Hull, <turret>_Yaw,
<turret>_Pitch), <models_dir>/<Prefix>_<id>_BaseColor.png and <data_dir>/<prefix>_<id>_hardpoints.json.

A ship may also list "extra_mounts" (where the scene builder adds ball-mounted guns: a point on
the hull and the side it faces, port, starboard, top or keel) and "missile_cells" (points on the
deck where missile launch cells go); both are passed on in the hardpoints, in Unity's frame.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
PREVIEW = None
if "--preview" in argv:
    i = argv.index("--preview"); PREVIEW = argv[i + 1]; del argv[i:i + 2]
SPEC, AI, MODELS, DATA = argv[:4]
ONLY = set(argv[4:])
AXES = {"+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)),
        "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}


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
    """Loose part id for each face (vertices at the same position count as joined)."""
    comp = components(obj.data, True)
    return [comp[p.vertices[0]] for p in obj.data.polygons]


def in_box(p, b):
    return b[0] <= p.x <= b[1] and b[2] <= p.y <= b[3] and b[4] <= p.z <= b[5]


def select_faces(obj, box, mode, part=None):
    """Faces to cut: whole loose parts whose middle is in the box ("islands"), or every face whose
    middle is in it, leaving out the hull's own (biggest) part ("faces"). `part`: the mesh's loose
    parts if already found (islands)."""
    me = obj.data
    if part is None:
        part = islands(obj)
    if mode == "faces":
        sizes = {}
        for p in part:
            sizes[p] = sizes.get(p, 0) + 1
        hull = max(sizes, key=sizes.get)
        return [i for i, poly in enumerate(me.polygons) if part[i] != hull and in_box(poly.center, box)]
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
    sizes = {}
    for p in part:
        sizes[p] = sizes.get(p, 0) + 1
    chosen.discard(max(sizes, key=sizes.get))
    return [i for i in range(len(me.polygons)) if part[i] in chosen]


def joined_barrel_faces(obj, housing_box, boxes):
    """For barrels modelled joined to their housing: of the housing's loose parts (those whose
    middle is in the housing box, not the hull's own), the pieces between UV seams that lie wholly
    in this box (or one of a list of boxes). A barrel is many small pieces; the housing's own
    pieces are bigger than a box drawn tight round the barrel."""
    me = obj.data
    part = islands(obj)
    housing = {part[i] for i in select_faces(obj, housing_box, "islands", part)}
    if not isinstance(boxes[0], list):
        boxes = [boxes]
    # the pieces between UV seams: joined by edges only, not welded across the seams
    piece = components(me, False)
    pieces = {}
    for i, poly in enumerate(me.polygons):
        if part[i] in housing:
            pieces.setdefault(piece[poly.vertices[0]], []).append(i)
    out = []
    for faces in pieces.values():
        pts = [me.vertices[v].co for i in faces for v in me.polygons[i].vertices]
        if any(all(in_box(p, b) for p in pts) for b in boxes):
            out += faces
    return out


def cut(hull, faces, name):
    """Separate those faces of the hull into an object of their own."""
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
    return Vector(map(min, *vs)) if len(vs) > 1 else vs[0], Vector(map(max, *vs)) if len(vs) > 1 else vs[0]


def bounds_of(points):
    return (Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
            Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))))


def barrel_muzzles(obj, facing):
    """The tip of each barrel (each loose part), on its axis."""
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


def front_muzzles(obj, facing, up):
    """For a turret whose barrels are part of the housing: the front of the housing, either side
    of its middle."""
    pts = [v.co.copy() for v in obj.data.vertices]
    far = max(p.dot(facing) for p in pts)
    near = min(p.dot(facing) for p in pts)
    front = [p for p in pts if p.dot(facing) > far - 0.15 * (far - near)]
    side = facing.cross(up).normalized()
    lo, hi = bounds_of(front)
    mid = (lo + hi) / 2
    out = []
    for s in (-1, 1):
        half = [p for p in front if (p - mid).dot(side) * s > 0]
        if not half:
            continue
        hlo, hhi = bounds_of(half)
        c = (hlo + hhi) / 2
        out.append(c + facing * (far - c.dot(facing)))
    return out


def preview(name, hull, turret_parts):
    """Renders (--preview <dir>) of the cut: each turret tinted and turned 25 degrees about its
    pivot, so what was cut out (and only that) shows."""
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
    cd = bpy.data.cameras.new("pc")
    cam = bpy.data.objects.new("pc", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for view, pos in (("above", (-0.45, -0.55, 0.6)), ("below", (-0.45, 0.55, -0.35))):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector((0.0, 0.0, 0.13)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        cd.lens = 35
        scene.render.filepath = os.path.join(PREVIEW, f"cut_{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    for yaw, pitch, _, _ in turret_parts:
        yaw.matrix_world = Matrix.Identity(4)
        if pitch is not None:
            pitch.matrix_world = Matrix.Identity(4)


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

    lo, hi = bounds(hull)
    centre = (lo + hi) / 2
    scale = f["length"] / (hi.x - lo.x)
    M = Matrix.Scale(scale, 4) @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation(-centre)
    R = M.to_3x3().normalized()

    def unity(p):
        q = M @ Vector(p)
        return [round(q.x, 3), round(q.z, 3), round(q.y, 3)]

    def unity_dir(d):
        q = (R @ Vector(d)).normalized()
        return [round(q.x, 4), round(q.z, 4), round(q.y, 4)]

    # ---- the turrets, cut out of the hull (in the GLB's coordinates), then each moved so its
    # pivot is at the origin
    turrets, parts, turret_parts = [], [], []
    for t in f["turrets"]:
        facing, up = AXES[t["facing"]], AXES[t["up"]]
        pitch = None
        if "barrels" in t:
            faces = select_faces(hull, t["barrels"], "islands")
            if "barrel_faces" in t:
                faces = sorted(set(faces) | set(joined_barrel_faces(hull, t["housing"], t["barrel_faces"])))
            pitch = cut(hull, faces, t["name"] + "_Pitch")
        yaw = cut(hull, select_faces(hull, t["housing"], t.get("housing_mode", "islands")), t["name"] + "_Yaw")
        assert yaw is not None, f"{f['id']} {t['name']}: nothing in the housing box"
        ylo, yhi = bounds(yaw)
        if "pivot" in t:
            yaw_pivot = Vector(t["pivot"])
        else:
            yaw_pivot = (ylo + yhi) / 2
            # the foot: the housing's far side from where its up points
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
            muzzles = front_muzzles(yaw, facing, up)
        print(f"TURRET {f['id']} {t['name']}: housing {len(yaw.data.vertices)} verts, "
              f"barrels {len(pitch.data.vertices) if pitch else 0} verts, {len(muzzles)} muzzles")
        turrets.append({
            "name": t["name"],
            "yaw_mesh": yaw.name, "pitch_mesh": pitch.name if pitch else "",
            "yaw_pivot": unity(yaw_pivot), "pitch_pivot": unity(pitch_pivot),
            "facing": unity_dir(facing), "up": unity_dir(up),
            "muzzles": [{"pos": unity(m)} for m in muzzles],
        })
        parts.append((yaw, yaw_pivot))
        if pitch is not None:
            parts.append((pitch, pitch_pivot))
        turret_parts.append((yaw, pitch, yaw_pivot, up))

    if PREVIEW:
        preview(f["id"], hull, turret_parts)

    # everything into the exported frame; each turret part about its pivot
    hull.data.transform(M)
    for o, pivot in parts:
        o.data.transform(Matrix.Translation(-(M @ pivot)) @ M)

    engines = [{"exit": unity(e["pos"]), "dir": unity_dir((1, 0, 0)), "radius": round(e["r"] * scale, 2)} for e in f["engines"]]
    # (as objects: Unity's JSON reader takes no arrays of arrays)
    ciws = [{"x": unity((x, y, 0))[0], "z": unity((x, y, 0))[2]} for x, y in f["ciws"]]

    os.makedirs(MODELS, exist_ok=True)
    os.makedirs(DATA, exist_ok=True)
    mat = hull.data.materials[0] if hull.data.materials else None
    img = next((n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None) if mat and mat.use_nodes else None
    pre = f.get("prefix", "Frigate")
    tex = os.path.join(MODELS, f"{pre}_{f['id']}_BaseColor.png")
    if img:
        img.filepath_raw = tex
        img.file_format = "PNG"
        img.save()
        print("TEXTURE", tex, img.size[0], img.size[1])

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
    hp = {"id": f["id"], "length": f["length"], "turrets": turrets, "engines": engines, "ciws": ciws,
          "bounds_size": [round(hhi.x - hlo.x, 2), round(hhi.z - hlo.z, 2), round(hhi.y - hlo.y, 2)]}
    sides = {"port": (0, -1, 0), "starboard": (0, 1, 0), "top": (0, 0, 1), "keel": (0, 0, -1)}
    if f.get("extra_mounts"):
        hp["mounts"] = [{"pos": unity(m["pos"]), "up": unity_dir(sides[m["side"]]), "side": m["side"]} for m in f["extra_mounts"]]
    if f.get("missile_cells"):
        hp["cells"] = [{"pos": unity(c)} for c in f["missile_cells"]]
    with open(os.path.join(DATA, f"{pre.lower()}_{f['id'].lower()}_hardpoints.json"), "w") as fh:
        json.dump(hp, fh, indent=1)
    print("HARDPOINTS", f["id"], json.dumps(hp)[:600])


spec = json.load(open(SPEC))
for f in spec.get("frigates", []) + spec.get("ships", []):
    if not ONLY or f["id"] in ONLY:
        convert(f)
