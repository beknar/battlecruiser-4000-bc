"""Turn an AI-made ship model so it lies the way the conversion (convert_frigates.py) expects: along
X with the bow at -X, up +Z, 1 unit long, centred, its keel on z = 0. Writes a new GLB.

Run:  blender -b --python align_glb.py -- <in.glb> <out.glb> <turn> [--up ux,uy,uz] [--long3d]
                                          [--pitch deg] [--roll deg]

The steps, in this order (each optional but the turn):
  --up       level a tilted model: turn the direction given (its up, in the file's frame) onto +Z
  turn       about Z: "pca" (the long axis, found from the vertices in the horizontal plane, onto X),
             "pca+180" (the same, bow and stern swapped), "pca+<deg>", or a number of degrees
  --long3d   then turn its long axis in 3D (found the same way) onto X, the end at -X staying there
  --pitch    then turn by so many degrees about Y (a bow that droops or rises)
  --roll     then roll by so many degrees about X (a list to one side)

The ships in frigates_spec.json were aligned (from the recoloured GLBs, recolour_frigate.py) with:
  Sovereign (cruiser1)   0 --up (the deck's normal, measured)    Halberd (destroyer1)  pca+180 --long3d --roll -14.5
  Rapier (destroyer2)    0                                        Claymore (destroyer3) 0
  Bulwark (destroyer4)   pca                                      Stiletto (destroyer5) pca+180 --pitch 15.09 --roll -8.8
Check each one with grid_glb.py / closeup_glb.py: in a top view of the aligned model the image's
right is -X (the bow); in a side view it is +X.
"""
import bpy, math, sys
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]


def option(name, default=None):
    if name in argv:
        i = argv.index(name)
        value = argv[i + 1] if i + 1 < len(argv) and not argv[i + 1].startswith("--") else True
        del argv[i:i + (1 if value is True else 2)]
        return value
    return default


up = option("--up")
long3d = option("--long3d", False)
pitch = float(option("--pitch", 0))
roll = float(option("--roll", 0))
src, dst = argv[0], argv[1]
turn = argv[2] if len(argv) > 2 else "0"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert len(meshes) == 1, f"expected one mesh, found {len(meshes)}"
hull = meshes[0]
hull.data.transform(hull.matrix_world)
hull.parent = None
hull.matrix_basis.identity()
for o in list(bpy.context.scene.objects):
    if o is not hull:
        bpy.data.objects.remove(o, do_unlink=True)


def points():
    return np.array([tuple(v.co) for v in hull.data.vertices])


def long_axis(co):
    co = co - co.mean(axis=0)
    w, vecs = np.linalg.eigh(np.cov(co.T))
    return vecs[:, np.argmax(w)]


if up:
    u = Vector([float(v) for v in up.split(",")]).normalized()
    hull.data.transform(u.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4())

if turn.startswith("pca"):
    ax = long_axis(points()[:, :2])
    ang = -math.atan2(ax[1], ax[0]) + math.radians(float(turn[3:] or 0))
else:
    ang = math.radians(float(turn))
hull.data.transform(Matrix.Rotation(ang, 4, "Z"))

if long3d:
    a = Vector(long_axis(points()))
    if a.x > 0:
        a = -a          # the axis pointing to the -X end
    hull.data.transform(a.rotation_difference(Vector((-1, 0, 0))).to_matrix().to_4x4())
if pitch:
    hull.data.transform(Matrix.Rotation(math.radians(pitch), 4, "Y"))
if roll:
    hull.data.transform(Matrix.Rotation(math.radians(roll), 4, "X"))

co = points()
lo, hi = co.min(axis=0), co.max(axis=0)
s = 1.0 / (hi[0] - lo[0])
c = (lo + hi) / 2
hull.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-c[0], -c[1], -lo[2]))))
co = points()
print("ALIGNED turned", round(math.degrees(ang), 2), "min", co.min(axis=0).round(3), "max", co.max(axis=0).round(3))
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=False)
