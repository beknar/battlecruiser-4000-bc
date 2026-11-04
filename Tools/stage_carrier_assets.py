"""Copy the kit parts the carrier uses from the Unreal export (I:\\ue-assets) into the Unity
project, and write Assets/Carrier/Data/kit_materials.json (material slot -> texture paths)."""
import json, os, re, shutil, sys

RAW = r"I:\ue-assets\_raw_unreal"
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KIT = os.path.join(PROJECT, "Assets", "Carrier", "Kit")
DATA = os.path.join(PROJECT, "Assets", "Carrier", "Data")

PARTS = {  # pack -> meshes
    "Spaceship_Kit": ["SM_turret", "SM_reactor_left", "SM_reactor_right", "SM_reactor_FX"],
    "Sci-Fi_Flying_Cargo_Ship": ["SM_antenna_01", "SM_antenna_02"],
    "MC_Environment": ["SM_Hangar_floor_8x8", "SM_Hangar_arch_a", "SM_Hangar_beam_b"],
    "Mega_City": ["SM_landing_pad_module", "SM_ship_flying"],
    "CargoShip": ["SM_SpaceShip_Outer_Body2"],
}
EXTRA_TEXTURES = [  # used directly by the carrier's own materials and the scene
    "Game/Spaceship_Kit/Textures/T_space_ship/T_metal_pannel/T_Metal_pannel_D.png",
    "Game/Spaceship_Kit/Textures/T_space_ship/T_metal_pannel/T_Metal_pannel_N.png",
    "Game/Spaceship_Kit/Textures/T_space_ship/T_space_ship_Trim/T_spaceship_trim_D.png",
    "Game/Spaceship_Kit/Textures/T_space_ship/T_space_ship_Trim/T_spaceship_trim_N.png",
    "Game/Spaceship_Kit/Textures/T_space_ship/T_space_ship_Trim/T_spaceship_trim_E.png",
    "Game/CargoShip/Textures/TC_Space.exr",
]


def find(rel_name):
    """Locate a texture by file name anywhere under the pack (the manifest gives exact paths
    for diffuse/normal; emissive maps are found by naming convention)."""
    return rel_name if os.path.isfile(os.path.join(RAW, rel_name)) else None


def copy(rel):
    src = os.path.join(RAW, rel)
    dst = os.path.join(KIT, rel.split("/", 1)[1] if rel.startswith("Game/") else rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if not os.path.exists(dst) or os.path.getsize(dst) != os.path.getsize(src):
        shutil.copy2(src, dst)
    return "Assets/Carrier/Kit/" + (rel.split("/", 1)[1] if rel.startswith("Game/") else rel)


def main():
    man = json.load(open(os.path.join(RAW, "ripper_manifest.json"), encoding="utf-8"))
    modes = man.get("material_modes", {})
    out = {"models": [], "textures": []}
    for x in man["meshes"]:
        parts = x["package"].split("/")
        pack, name = parts[2], parts[-1]
        if name not in PARTS.get(pack, []):
            continue
        model = copy(x["fbx"])
        mats = {}
        for slot, tex in (x.get("materials") or {}).items():
            entry = {}
            for kind in ("diffuse", "normal"):
                if tex.get(kind) and not tex[kind].startswith("Engine/") and find(tex[kind]):
                    entry[kind] = copy(tex[kind])
            if tex.get("diffuse") and tex["diffuse"].endswith("_D.png"):
                e = tex["diffuse"][:-6] + "_E.png"
                if find(e):
                    entry["emissive"] = copy(e)
            mode = (x.get("material_modes") or {}).get(slot, {})
            entry["blend"] = mode.get("blend", "opaque")
            mats[slot] = entry
        for slot, mode in (x.get("material_modes") or {}).items():
            mats.setdefault(slot, {"blend": mode.get("blend", "opaque")})
        out["models"].append({"key": f"{pack}/{name}", "path": model,
                              "materials": [dict(slot=k, **v) for k, v in mats.items()]})
        print("model", pack, name, list(mats))
    for rel in EXTRA_TEXTURES:
        out["textures"].append({"name": os.path.basename(rel), "path": copy(rel)})
    # reactor FX textures for the engine plume material
    for rel in ("Game/Spaceship_Kit/Textures/T_Reactor_FX.png", "Game/Spaceship_Kit/Textures/T_Reactor_FX_02.png"):
        hits = [t["file"] for t in man["textures"] if t["file"].endswith(os.path.basename(rel))]
        if hits:
            out["textures"].append({"name": os.path.basename(rel), "path": copy(hits[0])})
    os.makedirs(DATA, exist_ok=True)
    json.dump(out, open(os.path.join(DATA, "kit_materials.json"), "w"), indent=1)
    print("STAGED", len(out["models"]), "models,", len(out["textures"]), "extra textures")


if __name__ == "__main__":
    main()
