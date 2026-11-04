"""Model the frigate's side-gun ball turret and export it as FBX for Unity.

Run:  blender -b --python build_ball_turret.py -- <models_dir>

Three meshes, all with their transforms applied and the ball's centre at the origin (the gun's
pivot), barrel along Blender +Y. With the export settings below (the same as
convert_frigate.py) a Blender point (x, y, z) lands in Unity at (x, z, y), so the barrel points
along Unity +Z, which is the gun's rest direction (straight out from the hull side).

  BT_Base    fixed collar that sits against the gun pod (does not move)
  BT_Ball    the ball that swings in the collar
  BT_Barrel  the barrel and muzzle (moves with the ball)

Sizes in metres; the frigate is 180 m long and its gun pods are about 6 m across.
"""
import bpy, math, os, sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(OUT, exist_ok=True)

BALL_R = 2.4
MUZZLE_Y = 10.6

bpy.ops.wm.read_factory_settings(use_empty=True)


def cylinder_y(radius, y0, y1, verts=40):
    """A cylinder along +Y from y0 to y1."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=y1 - y0,
                                        location=(0, (y0 + y1) / 2, 0), rotation=(math.radians(90), 0, 0))
    return bpy.context.object


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.name = o.data.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return o


# fixed collar: a thick ring against the pod with a raised lip the ball sits in
base = join([
    cylinder_y(3.4, -2.7, -1.5, 48),
    cylinder_y(2.95, -1.6, -0.9, 48),
], "BT_Base")

# the ball, with a short mantlet where the barrel leaves it
bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=BALL_R, location=(0, 0, 0))
ball_sphere = bpy.context.object
ball = join([ball_sphere, cylinder_y(1.15, 1.6, 2.9, 32)], "BT_Ball")

# barrel: heat sleeve, tube and muzzle brake
barrel = join([
    cylinder_y(0.62, 2.7, 4.8, 28),
    cylinder_y(0.42, 4.6, 9.8, 24),
    cylinder_y(0.6, 9.6, MUZZLE_Y, 28),
], "BT_Barrel")

bpy.ops.object.select_all(action="DESELECT")
for o in (base, ball, barrel):
    o.select_set(True)
path = os.path.join(OUT, "BallTurret.fbx")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False)
print("EXPORTED", path, "muzzle at Unity z =", MUZZLE_Y)
