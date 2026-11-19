"""Copy the fires from the Asset Store pack "Realistic Fire VFX" (Hivemind) into the ignored
Assets/Carrier/Kit/FireFX: the URP version's VFX Graph effects the game uses (a large fire, a
ground fire and the smoke of what has burnt out), with everything they need (their graphs, shader
graphs, textures and meshes), keeping their GUIDs. Not the pack's scripts or its light prefab:
CarrierBuilder.FireFx does their work. The pack is licensed: it is never in the repo. Download it
first (Package Manager > My Assets > Realistic Fire VFX > Download, not Import), then, with the
editor closed or before refreshing it:

    python Tools/stage_firefx.py [path to the downloaded .unitypackage]

and build (Tools > Carrier > Build Everything, or Rebuild Fire FX). The effects need the Visual
Effect Graph package (in the manifest) and compute shaders: a web game on WebGL 2 shows none. Its
textures are imported at most 1024 across."""
import glob
import os
import re
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stage_gunfx import CACHE as GUN_CACHE, unpack, needed  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEST = os.path.join(PROJECT, "Assets", "Carrier", "Kit", "FireFX")
CACHE = os.path.dirname(GUN_CACHE)
URP = "Assets/Hivemind/FireVFX/URP/Commons/"
PREFABS = URP + "VFX_PREFABS (Use This)/"
# the fires CarrierBuilder.FireFx uses
EFFECTS = [
    "LargeScaleFire/VFX_LargeScaleFire01.prefab",
    "GroundFire/VFX_GroundFire_Med.prefab",
    "Aftermath/VFX_AftermathMed.prefab",
]


def main():
    package = sys.argv[1] if len(sys.argv) > 1 else next(iter(glob.glob(os.path.join(CACHE, "*", "*", "Realistic Fire VFX*.unitypackage"))), None)
    if not package or not os.path.exists(package):
        sys.exit(f"no Realistic Fire VFX package (looked in {CACHE}): download it in Package Manager > My Assets first")
    with tempfile.TemporaryDirectory() as tmp:
        outer = os.path.join(tmp, "outer")
        index = unpack(package, outer)
        # (the pack holds its URP version as a package of its own)
        urp = next((g for p, g in index.items() if p.startswith("Assets/Hivemind/FireVFX/URP/") and p.endswith(".unitypackage")), None)
        if urp is None:
            sys.exit("no URP version in the package")
        inner = os.path.join(tmp, "urp")
        index = unpack(os.path.join(outer, urp, "asset"), inner)
        files = [f for f in needed(inner, index, [PREFABS + e for e in EFFECTS])
                 if f.startswith(URP) and not f.endswith(".cs") and "/Lights/" not in f]
        for path in files:
            g = index[path]
            if not os.path.exists(os.path.join(inner, g, "asset")):
                continue    # (a folder)
            out = os.path.join(DEST, path[len(URP):].replace("VFX_PREFABS (Use This)", "Prefabs"))
            os.makedirs(os.path.dirname(out), exist_ok=True)
            shutil.copyfile(os.path.join(inner, g, "asset"), out)
            meta = open(os.path.join(inner, g, "asset.meta"), encoding="utf-8").read()
            if out.lower().endswith(".png"):
                meta = re.sub(r"(maxTextureSize:\s*)(\d+)", lambda m: m.group(1) + str(min(int(m.group(2)), 1024)), meta)
            open(out + ".meta", "w", encoding="utf-8", newline="\n").write(meta)
        print(f"{len(files)} files into {DEST}")


if __name__ == "__main__":
    main()
