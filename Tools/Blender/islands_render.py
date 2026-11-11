"""Render a GLB with each sizeable loose part (seams welded) in its own colour, and print the key, to
see which parts are turrets, barrels and so on.

Run:  blender -b --python islands_render.py -- <model.glb> <out_prefix> [min_verts]
Coordinates are Blender's after the glTF import (Tripo frigates: bow at -X, +Z up, 1 unit long).
"""
import bpy, bmesh, sys, colorsys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
min_verts = int(argv[2]) if len(argv) > 2 else 100

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=1e-5)
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")

parts = sorted([o for o in bpy.context.scene.objects if o.type == "MESH"], key=lambda o: -len(o.data.vertices))
k = 0
for o in parts:
    n = len(o.data.vertices)
    if n < min_verts or o is parts[0]:
        o.color = (0.55, 0.55, 0.58, 1)
        continue
    h = (k * 0.618) % 1.0
    r, g, b = colorsys.hsv_to_rgb(h, 0.85, 1.0)
    o.color = (r, g, b, 1)
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    print(f"PART {k:2d} rgb({int(r*255):3d},{int(g*255):3d},{int(b*255):3d}) verts {n:5d}  "
          f"x {lo[0]:+.3f}..{hi[0]:+.3f}  y {lo[1]:+.3f}..{hi[1]:+.3f}  z {lo[2]:+.3f}..{hi[2]:+.3f}")
    k += 1

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.render.resolution_x, scene.render.resolution_y = 1400, 800
scene.world = bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
views = [
    ("above_bow", (-0.75, -0.55, 0.65), (0.0, 0.0, 0.12), 45),
    ("above_stern", (0.75, 0.55, 0.65), (0.0, 0.0, 0.12), 45),
    ("below", (-0.3, -0.7, -0.45), (0.0, 0.0, 0.08), 45),
]
for name, pos, at, lens in views:
    cam.location = Vector(pos)
    cam.rotation_euler = (Vector(at) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = lens
    scene.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath)
