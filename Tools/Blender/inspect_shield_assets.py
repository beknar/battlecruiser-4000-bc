"""Print channel statistics of the shield textures and the layout of the shield sphere meshes.
blender -b --python inspect_shield_assets.py -- <StylizedShieldsVol1 folder> <preview.png>"""
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
root, preview = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)

for rel in ["Textures/Sprites/T_ShieldHexagonUV_01.png", "Textures/Sprites/T_HexaPenta_01.png",
            "Textures/Sprites/T_Circle18.png", "Textures/Gradients/T_Glow_02.png", "Textures/Noises/T_Voronoi01.png"]:
    img = bpy.data.images.load(os.path.join(root, rel))
    px = list(img.pixels)
    n = len(px) // 4
    step = max(1, n // 20000)
    sums, mins, maxs = [0.0] * 4, [9.0] * 4, [-1.0] * 4
    cnt = 0
    for i in range(0, n, step):
        for c in range(4):
            v = px[i * 4 + c]
            sums[c] += v; mins[c] = min(mins[c], v); maxs[c] = max(maxs[c], v)
        cnt += 1
    print("TEX", rel, img.size[:], "channels", img.channels,
          "mean", [round(s / cnt, 3) for s in sums], "min", [round(m, 2) for m in mins], "max", [round(m, 2) for m in maxs])

for rel in ["Models/SM_Sphere_Hexagons_01.fbx", "Models/SM_Sphere_01.fbx"]:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(root, rel))
    for o in set(bpy.data.objects) - before:
        if o.type != "MESH":
            continue
        me = o.data
        pts = [o.matrix_world @ v.co for v in me.vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        uvs = me.uv_layers.active.data if me.uv_layers.active else []
        us = [d.uv.x for d in uvs]; vs = [d.uv.y for d in uvs]
        print("MESH", rel, o.name, "verts", len(me.vertices), "faces", len(me.polygons),
              "sizes", sorted({len(p.vertices) for p in me.polygons}), "bounds", lo.to_tuple(2), hi.to_tuple(2),
              "uv layers", len(me.uv_layers), "uv range", (round(min(us), 2), round(max(us), 2), round(min(vs), 2), round(max(vs), 2)) if us else None,
              "materials", [m.name for m in me.materials if m])
