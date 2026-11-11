"""An orthographic view of a GLB's stern, end on, with a coordinate grid (lines every 0.025 units,
thicker every 0.05), to read off where the engine nozzles are.

Run:  blender -b --python grid_end.py -- <model.glb> <out.png>
Tripo frigates after the glTF import: bow at -X, +Z up. The camera sits astern (+X) looking at the
bow, so screen right is -Y and screen up is +Z.
"""
import bpy, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "FLAT"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
scene.world = bpy.data.worlds.new("w")

def line(a, b, w):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a + b) / 2)
    o = bpy.context.object
    d = b - a
    o.scale = (max(abs(d.x), w), max(abs(d.y), w), max(abs(d.z), w))

X = 0.56      # just astern of the model
for i in range(-10, 11):
    v = i * 0.025
    w = 0.0015 if i % 2 == 0 else 0.0005
    line((X, v, -0.02), (X, v, 0.36), w)          # vertical lines at y = v
for i in range(0, 15):
    v = i * 0.025
    w = 0.0015 if i % 2 == 0 else 0.0005
    line((X, -0.26, v), (X, 0.26, v), w)          # horizontal lines at z = v

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 0.56
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = Vector((2.0, 0.0, 0.17))
cam.rotation_euler = Vector((-1, 0, 0)).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
r = cam.matrix_world.to_3x3()
print("WROTE", out, "cam right", (r @ Vector((1, 0, 0))).to_tuple(2), "up", (r @ Vector((0, 1, 0))).to_tuple(2))
