"""Model the frigate's CIWS mount and bow beam emitter, and prepare the shield bubble mesh.

Run:  blender -b --python build_frigate_parts.py -- <models_dir> <SM_Sphere_Hexagons_01.fbx>

Same axis convention as build_ball_turret.py: Blender (x, y, z) lands in Unity at (x, z, y), so
Blender +Y is Unity forward (+Z) and Blender +Z is Unity up (+Y). Every mesh has its transform
applied; Unity rebuilds the joints (pivots are listed below).

CIWS.fbx (metres)
  CIWS_Base     fixed drum on the deck, 0..1.2 m up
  CIWS_Yaw      turning housing; yaw pivot at the origin
  CIWS_Pitch    cradle and ammunition drum; pitch pivot at (0, 0, PITCH_Z) Blender = Unity y 2.9
  CIWS_Barrels  six-barrel rotary cluster round the line (0, y, PITCH_Z); muzzle at y = MUZZLE_Y

BeamEmitter.fbx
  BE_Housing    armoured ring and four prongs reaching forward
  BE_Lens       the emitter face (its emission is driven while charging and firing)
  The emitter face points along +Y (Unity +Z); its back sits at y = 0.

ShieldHex.fbx
  Shield_Hex    the StylizedShieldsVol1 hexagon sphere, subdivided twice and pushed back onto a
                unit sphere so it stays round when scaled up to ship size; each tile keeps its UVs
"""
import bpy, math, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, HEX = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)

PITCH_Z = 2.9
MUZZLE_Y = 5.2


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def cyl(radius, depth, loc, axis="Y", verts=32):
    rot = {"Y": (math.radians(90), 0, 0), "Z": (0, 0, 0), "X": (0, math.radians(90), 0)}[axis]
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot)
    return bpy.context.object


def box(size, loc):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.scale = size
    return o


def join(objs, name, smooth=35):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.name = o.data.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
    return o


def export(objs, filename, smooth="FACE"):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    path = os.path.join(OUT, filename)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type=smooth, use_mesh_modifiers=True, add_leaf_bones=False)
    print("EXPORTED", path)


# ------------------------------------------------------------------ CIWS mount
reset()
base = join([cyl(1.9, 1.2, (0, 0, 0.6), "Z", 40), cyl(1.6, 0.25, (0, 0, 1.3), "Z", 40)], "CIWS_Base")
yaw = join([
    box((2.6, 2.4, 1.6), (0, -0.3, 1.9)),                 # housing body
    box((0.5, 1.6, 1.4), (1.35, 0.0, 2.7)),               # trunnion cheeks either side of the cradle
    box((0.5, 1.6, 1.4), (-1.35, 0.0, 2.7)),
    box((1.6, 1.0, 1.0), (0, -1.2, 3.0)),                 # radar/sensor box behind
    cyl(0.55, 0.4, (0, -1.2, 3.7), "Z", 24),              # sensor dome base
], "CIWS_Yaw", 20)
bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.6, location=(0, -1.2, 3.95))
dome = bpy.context.object
yaw_dome = join([dome], "CIWS_Dome")
pitch = join([
    box((2.0, 2.0, 1.2), (0, 0.2, PITCH_Z)),               # cradle
    cyl(0.75, 1.2, (0.0, 0.4, PITCH_Z - 0.9), "X", 24),    # ammunition drum under the cradle
    cyl(0.55, 0.8, (0, 1.4, PITCH_Z), "Y", 24),            # barrel bearing
], "CIWS_Pitch", 25)
barrel_parts = []
for k in range(6):
    a = k * math.tau / 6
    barrel_parts.append(cyl(0.11, 3.8, (0.3 * math.cos(a), 3.3, PITCH_Z + 0.3 * math.sin(a)), "Y", 12))
barrel_parts.append(cyl(0.5, 0.25, (0, 2.4, PITCH_Z), "Y", 24))     # rear clamp
barrel_parts.append(cyl(0.48, 0.25, (0, 4.3, PITCH_Z), "Y", 24))    # mid clamp
barrel_parts.append(cyl(0.46, 0.3, (0, MUZZLE_Y - 0.15, PITCH_Z), "Y", 24))  # muzzle clamp
barrels = join(barrel_parts, "CIWS_Barrels", 30)
# the dome turns with the housing
bpy.ops.object.select_all(action="DESELECT")
yaw.select_set(True); yaw_dome.select_set(True)
bpy.context.view_layer.objects.active = yaw
bpy.ops.object.join()
yaw = bpy.context.object
yaw.name = yaw.data.name = "CIWS_Yaw"
export([base, yaw, pitch, barrels], "CIWS.fbx")

# ------------------------------------------------------------------ bow beam emitter
reset()
ring = cyl(4.2, 3.0, (0, 1.5, 0), "Y", 48)
hole = cyl(3.0, 3.4, (0, 1.5, 0), "Y", 48)
mod = ring.modifiers.new("bore", "BOOLEAN")
mod.operation = "DIFFERENCE"
mod.object = hole
bpy.context.view_layer.objects.active = ring
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.data.objects.remove(hole, do_unlink=True)
parts = [ring, cyl(3.1, 0.6, (0, 0.3, 0), "Y", 48)]          # back plate inside the ring
for k in range(4):
    a = math.radians(45 + 90 * k)
    x, z = 4.4 * math.cos(a), 4.4 * math.sin(a)
    p = box((1.0, 7.0, 1.0), (x, 4.0, z))
    p.rotation_euler = (0, -a, 0)
    parts.append(p)
    parts.append(box((1.4, 1.2, 1.4), (x * 0.98, 7.3, z * 0.98)))  # prong tips
housing = join(parts, "BE_Housing", 30)
lens = join([cyl(2.95, 0.4, (0, 0.85, 0), "Y", 48)], "BE_Lens")
export([housing, lens], "BeamEmitter.fbx")

# ------------------------------------------------------------------ shield bubble
reset()
bpy.ops.import_scene.fbx(filepath=HEX)
sphere = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
for o in list(bpy.context.scene.objects):
    if o is not sphere:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.objects.active = sphere
sphere.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.subdivide(number_cuts=2)
bpy.ops.object.mode_set(mode="OBJECT")
for v in sphere.data.vertices:
    v.co = v.co.normalized()          # back onto a unit sphere
sphere.data.update()
bpy.ops.object.shade_smooth()
sphere.name = sphere.data.name = "Shield_Hex"
print("SHIELD verts", len(sphere.data.vertices), "faces", len(sphere.data.polygons))
export([sphere], "ShieldHex.fbx", smooth="FACE")
