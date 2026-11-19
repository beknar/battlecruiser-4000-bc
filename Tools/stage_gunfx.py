"""Copy the guns' effects from the Asset Store pack "Realistic Gun VFX" (Hivemind) into the ignored
Assets/Carrier/Kit/GunFX: the URP version's muzzle flashes and impacts the game uses, with
everything they need (materials, shader graphs and their sub-graphs, textures, meshes), keeping
their GUIDs. The pack is licensed: it is never in the repo. Download it first (Package Manager >
My Assets > Realistic Gun VFX > Download, not Import), then, with the editor closed or before
refreshing it:

    python Tools/stage_gunfx.py [path to the downloaded .unitypackage]

and build (Tools > Carrier > Build Everything, or Rebuild Gun FX): CarrierBuilder.GunFx makes the
game's own versions of them. Its textures are imported at most 1024 across."""
import glob
import os
import re
import shutil
import sys
import tarfile
import tempfile

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEST = os.path.join(PROJECT, "Assets", "Carrier", "Kit", "GunFX")
CACHE = os.path.join(os.environ.get("ASSETSTORE_CACHE_PATH") or os.path.join(os.environ.get("APPDATA", ""), "Unity", "Asset Store-5.x"),
                     "Hivemind")
URP = "Assets/HIVEMIND/GunFX/URP/"
# the effects CarrierBuilder.GunFx uses (under the URP version's Art/Prefabs)
EFFECTS = [
    "MuzzleFlashes/Sniper/NS_Muzzle_Sniper_01.prefab",
    "MuzzleFlashes/SMG/NS_Muzzle_SMG_01.prefab",
    "MuzzleFlashes/AR/NS_Muzzle_AR_01.prefab",
    "ImpactFX/Metal/NS_Impact_Metal_Med.prefab",
    "ImpactFX/Metal/NS_Impact_Metal_Low.prefab",
    "ImpactFX/Rock/NS_Impact_Rock_Med.prefab",
    "ImpactFX/Rock/NS_Impact_Rock_Low.prefab",
]
# a reference to another asset, in YAML or in a shader graph's JSON (where a sub-graph's is escaped)
GUID = re.compile(r"""guid[\\"']*\s*[:=]\s*[\\"']*([0-9a-f]{32})""")


def unpack(package, into):
    """A .unitypackage is a gzipped tar of <guid>/{pathname, asset, asset.meta}."""
    with tarfile.open(package, "r:gz") as t:
        for m in t.getmembers():
            # (only plain files, kept inside `into`)
            if not m.isfile() or m.name.startswith(("/", "..")) or ".." in m.name.split("/"):
                continue
            t.extract(m, into)
    index = {}
    for g in os.listdir(into):
        p = os.path.join(into, g, "pathname")
        if os.path.exists(p):
            index[open(p, encoding="utf-8").readline().strip()] = g
    return index


def needed(root, index, start):
    """Every asset of the package the starting ones use, following their references."""
    by_guid = {g: p for p, g in index.items()}
    todo, seen = [index[s] for s in start], set()
    while todo:
        g = todo.pop()
        if g in seen:
            continue
        seen.add(g)
        a = os.path.join(root, g, "asset")
        if not os.path.exists(a) or not by_guid[g].endswith((".prefab", ".mat", ".shadergraph", ".shadersubgraph", ".asset", ".vfx", ".vfxoperator", ".vfxblock")):
            continue
        for x in GUID.findall(open(a, encoding="utf-8", errors="ignore").read()):
            if x in by_guid:
                todo.append(x)
    return sorted(by_guid[g] for g in seen)


def main():
    package = sys.argv[1] if len(sys.argv) > 1 else next(iter(glob.glob(os.path.join(CACHE, "*", "Realistic Gun VFX*.unitypackage"))), None)
    if not package or not os.path.exists(package):
        sys.exit(f"no Realistic Gun VFX package (looked in {CACHE}): download it in Package Manager > My Assets first")
    with tempfile.TemporaryDirectory() as tmp:
        outer = os.path.join(tmp, "outer")
        index = unpack(package, outer)
        # (the pack holds its URP version as a package of its own)
        urp = index.get("Assets/HIVEMIND/GunFX/URP/GunFX_URP.unitypackage") or index.get("Assets/Hivemind/GunFX/URP/GunFX_URP.unitypackage")
        if urp is None:
            sys.exit("no URP version in the package")
        inner = os.path.join(tmp, "urp")
        index = unpack(os.path.join(outer, urp, "asset"), inner)
        files = needed(inner, index, [URP + "Art/Prefabs/" + e for e in EFFECTS])
        for path in files:
            g = index[path]
            out = os.path.join(DEST, path[len(URP):])
            os.makedirs(os.path.dirname(out), exist_ok=True)
            shutil.copyfile(os.path.join(inner, g, "asset"), out)
            meta = open(os.path.join(inner, g, "asset.meta"), encoding="utf-8").read()
            if out.lower().endswith(".png"):
                meta = re.sub(r"(maxTextureSize:\s*)(\d+)", lambda m: m.group(1) + str(min(int(m.group(2)), 1024)), meta)
            open(out + ".meta", "w", encoding="utf-8", newline="\n").write(meta)
        print(f"{len(files)} files into {DEST}")


if __name__ == "__main__":
    main()
