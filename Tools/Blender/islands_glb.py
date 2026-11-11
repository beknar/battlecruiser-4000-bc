"""List a GLB mesh's loose parts (islands) with their bounds, biggest first, to find pieces such as
turrets that can be cut out whole.

Run:  blender -b --python islands_glb.py -- <model.glb> [min_verts]
Bounds are in Blender's coordinates after the glTF import (for the Tripo frigates: bow at -X, +Z up,
the hull sitting on z = 0, 1 unit long).
"""
import bpy, bmesh, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src = argv[0]
min_verts = int(argv[1]) if len(argv) > 1 else 200

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
mw = obj.matrix_world
bm = bmesh.new()
bm.from_mesh(obj.data)
# the mesh is split along its UV seams: weld those back first, so a part is one piece
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.verts.ensure_lookup_table()
seen = set()
islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack, part = [v], []
    seen.add(v.index)
    while stack:
        x = stack.pop()
        part.append(x)
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    pts = [mw @ p.co for p in part]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    islands.append((len(part), lo, hi))
islands.sort(key=lambda t: -t[0])
print(f"ISLANDS {len(islands)} total, {sum(1 for i in islands if i[0] >= min_verts)} with {min_verts}+ verts")
for n, lo, hi in islands:
    if n < min_verts:
        continue
    print(f"ISLAND verts {n:6d}  x {lo[0]:+.3f}..{hi[0]:+.3f}  y {lo[1]:+.3f}..{hi[1]:+.3f}  z {lo[2]:+.3f}..{hi[2]:+.3f}")
