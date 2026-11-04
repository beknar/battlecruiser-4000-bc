"""Render a few labelled views of a GLB to inspect it.

Run:  blender -b --python preview_glb.py -- <model.glb> <out_prefix>
Writes <out_prefix>_<view>.png. Blender axes after the glTF import: +Z up, the glTF +Z axis
becomes Blender -Y.
"""
import bpy, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
centre, size = (lo + hi) / 2, hi - lo
print("BOUNDS min", tuple(round(v, 4) for v in lo), "max", tuple(round(v, 4) for v in hi))

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1200, 700
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.05, 0.05, 0.07)

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
r = max(size) * 1.15

views = {
    "top":    Vector((0, 0, 1)),
    "bottom": Vector((0, 0, -1)),
    "left":   Vector((0, -1, 0)),     # looking from -Y
    "right":  Vector((0, 1, 0)),
    "endXpos": Vector((1, 0, 0)),
    "endXneg": Vector((-1, 0, 0)),
    "persp":  Vector((1, -1, 0.7)).normalized(),
}
for name, d in views.items():
    cam.location = centre + d * r * 3
    up = Vector((0, 1, 0)) if abs(d.z) > 0.9 else Vector((0, 0, 1))
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y" if abs(d.z) > 0.9 else "Y").to_euler()
    if abs(d.z) <= 0.9:
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam_data.ortho_scale = r * (1.0 if name != "persp" else 1.1)
    scene.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath)
