"""Convert the AI-made frigate (ai models/starting frigate.glb, a single textured Tripo mesh) into an
FBX for Unity, plus its colour texture and the hardpoints Unity needs (side guns, engine exits).

Run:  blender -b --python convert_frigate.py -- <frigate.glb> <models_dir> <data_dir>

The GLB is 1 unit long. After Blender's glTF import its bow points to -X, +Z is up and the hull
sits on z = 0. The mesh is turned so the bow points +Y, centred, and scaled to LENGTH metres, then
exported with the same FBX axis settings as the carrier parts (build_carrier.py), so in Unity the
bow points +Z and up is +Y. Unity position = (x, z, y) of the exported Blender coordinates.

Hardpoints are given in the GLB's own (imported) coordinates, read off gridded renders
(grid_glb.py), and refined against the mesh where it matters (the gun pods' outer faces).
"""
import bpy, json, math, os, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, MODELS, DATA = argv[0], argv[1], argv[2]
LENGTH = 180.0

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert len(meshes) == 1, f"expected one mesh, found {len(meshes)}"
obj = meshes[0]

# bake the imported object transform into the vertices, then drop the empties above it
mw = obj.matrix_world.copy()
obj.data.transform(mw)
obj.parent = None
obj.matrix_basis.identity()
for o in list(bpy.context.scene.objects):
    if o is not obj:
        bpy.data.objects.remove(o, do_unlink=True)

verts = [v.co.copy() for v in obj.data.vertices]
lo = Vector((min(v.x for v in verts), min(v.y for v in verts), min(v.z for v in verts)))
hi = Vector((max(v.x for v in verts), max(v.y for v in verts), max(v.z for v in verts)))
centre = (lo + hi) / 2
scale = LENGTH / (hi.x - lo.x)
print("SOURCE bounds", lo.to_tuple(4), hi.to_tuple(4), "scale", round(scale, 3))

# source (bow -X) -> export space (bow +Y): -90 degrees about Z, centred, scaled.
# With these FBX settings an exported point (x, y, z) lands in Unity at (x, z, y), so the bow
# ends up at Unity +Z. (build_carrier.py turns its meshes 180 degrees before export, which is
# why its notes give the mapping as (-x, z, -y) for a bow at Blender -Y; checked on the frigate
# with the review screenshots.)
M = Matrix.Scale(scale, 4) @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation(-centre)
obj.data.transform(M)
obj.name = obj.data.name = "Frigate_Hull"

def unity(p):
    q = M @ Vector(p)
    return [round(q.x, 3), round(q.z, 3), round(q.y, 3)]

def unity_dir(d):
    q = M.to_3x3() @ Vector(d)
    q.normalize()
    return [round(q.x, 4), round(q.z, 4), round(q.y, 4)]

# ---- side guns: the cylindrical pods in a recessed mount on each flank, aft of midships
guns = []
for side in (-1, 1):
    # outer face of the pod: the furthest vertex out along +-Y in the pod's box
    pod = [v for v in verts if 0.26 < v.x < 0.35 and 0.115 < v.z < 0.15 and v.y * side > 0.10]
    outer = max(v.y * side for v in pod) if pod else 0.162
    zs = sorted(v.z for v in pod if v.y * side > outer - 0.02) or [0.133]
    z = zs[len(zs) // 2]
    centre_pt = (0.306, side * (outer - 0.012), z)
    guns.append({
        "name": "Starboard" if side > 0 else "Port",
        "pos": unity(centre_pt),
        "outward": unity_dir((0, side, 0)),
        "muzzle": unity((0.306, side * (outer + 0.004), z)),
    })
    print("GUN", side, "pod verts", len(pod), "outer y", round(outer, 4), "z", round(z, 4))

# ---- engines: two large lower nozzles and two smaller upper ones at the stern (+X)
engines = []
for (y, z, r) in ((0.077, 0.089, 0.029), (0.10, 0.137, 0.02)):
    for side in (-1, 1):
        engines.append({"exit": unity((0.495, side * y, z)), "dir": unity_dir((1, 0, 0)), "radius": round(r * scale, 2)})

# Unity's left/right: name the guns by the side they end up on
for g in guns:
    g["name"] = "Starboard" if g["pos"][0] > 0 else "Port"

os.makedirs(MODELS, exist_ok=True)
os.makedirs(DATA, exist_ok=True)

# colour texture, saved next to the FBX
mat = obj.data.materials[0] if obj.data.materials else None
img = None
if mat and mat.use_nodes:
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image:
            img = n.image
            break
tex_path = os.path.join(MODELS, "Frigate_BaseColor.png")
if img:
    img.filepath_raw = tex_path
    img.file_format = "PNG"
    img.save()
    print("TEXTURE", tex_path, img.size[0], img.size[1])
else:
    print("TEXTURE missing")

# smooth shading from the GLB's own normals is kept; export
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
fbx = os.path.join(MODELS, "Frigate_Hull.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
                         path_mode="STRIP")
print("EXPORTED", fbx)

hp = {"length": LENGTH, "guns": guns, "engines": engines,
      "bounds_size": [round((hi.y - lo.y) * scale, 2), round((hi.z - lo.z) * scale, 2), LENGTH]}
with open(os.path.join(DATA, "frigate_hardpoints.json"), "w") as f:
    json.dump(hp, f, indent=1)
print("HARDPOINTS", json.dumps(hp))
