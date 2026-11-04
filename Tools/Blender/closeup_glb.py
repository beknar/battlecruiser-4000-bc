"""Close-up perspective renders of a GLB: blender -b --python closeup_glb.py -- <model.glb> <out_prefix>
Each view is (name, camera position, look-at point, lens mm) in the model's units (Blender axes)."""
import bpy, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1400, 800
scene.world = bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

# a grid of small markers every 0.1 units along X on both sides, to read positions off the renders
mat = bpy.data.materials.new("mark")
for x10 in range(-5, 6):
    for y in (-0.16, 0.16):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.006, location=(x10 / 10, y, 0.0))

views = [
    ("side_aft_neg",  (0.25, -0.75, 0.25), (0.25, -0.1, 0.17), 50),
    ("side_fwd_neg",  (-0.25, -0.75, 0.25), (-0.25, -0.1, 0.15), 50),
    ("side_aft_pos",  (0.25, 0.75, 0.25), (0.25, 0.1, 0.17), 50),
    ("above_aft",     (0.9, -0.5, 0.7), (0.3, 0.0, 0.2), 50),
]
for name, pos, at, lens in views:
    cam.location = Vector(pos)
    cam.rotation_euler = (Vector(at) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = lens
    scene.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath)
