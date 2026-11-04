"""Orthographic views of a GLB with a coordinate grid (lines every 0.05 units, thicker every 0.1).
blender -b --python grid_glb.py -- <model.glb> <out_prefix>"""
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
scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
scene.world = bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

def line(a, b, w):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a + b) / 2)
    o = bpy.context.object
    d = b - a
    o.scale = (max(abs(d.x), w), max(abs(d.y), w), max(abs(d.z), w))

def grid(plane, offset):
    # plane "xz" at y = offset (side view) or "xy" at z = offset (top view)
    for i in range(-10, 11):
        v = i * 0.05
        w = 0.0025 if i % 2 == 0 else 0.0008
        if plane == "xz":
            line((v, offset, -0.05), (v, offset, 0.4), w)
            if 0 <= i <= 8: line((-0.55, offset, v), (0.55, offset, v), w)
        else:
            line((v, -0.2, offset), (v, 0.2, offset), w)
            if -4 <= i <= 4: line((-0.55, v, offset), (0.55, v, offset), w)

grid("xz", -0.25)
grid("xy", 0.45)
views = [
    ("side", (0, -2, 0.17), 1.15),
    ("top", (0, 0, 2), 1.15),
]
for name, pos, scale in views:
    cam.location = Vector(pos)
    d = -Vector(pos).normalized() if name == "top" else Vector((0, 1, 0))
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam_data.ortho_scale = scale
    scene.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath, "cam right", (cam.matrix_world.to_3x3() @ Vector((1, 0, 0))).to_tuple(2),
          "up", (cam.matrix_world.to_3x3() @ Vector((0, 1, 0))).to_tuple(2))
