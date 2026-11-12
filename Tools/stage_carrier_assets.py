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
# The colony stations, cities, military and industrial bases (CarrierBuilder.Settlements), by
# their exact path under the export (some packs have two meshes of one name). Keyed in
# kit_materials.json by that path, without "Game/" and ".fbx". Packs exported without material
# maps (SpaceColony2086, Industrial_Pipe_Rack, SilentZonePack) get the project's own industrial
# materials in the builder.
SETTLEMENT_MESHES = [
    "Game/Mega_City/Meshes/City_buildings/SM_large_building_module_01.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_large_building_module_02.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_building_01.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_building_02.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_building_02_b.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_building_08.fbx",
    "Game/Mega_City/Meshes/City_buildings/SM_main_building.fbx",
    "Game/Mega_City/Meshes/Background_building/SM_building_03.fbx",
    "Game/Mega_City/Meshes/Background_building/SM_building_04.fbx",
    "Game/Mega_City/Meshes/Background_building/SM_building_06.fbx",
    "Game/Mega_City/Meshes/Background_building/SM_building_08.fbx",
    "Game/Mega_City/Meshes/Landing_pad/SM_landing_pad.fbx",
    "Game/Mega_City/Meshes/Antennas/SM_antenna_03.fbx",
    "Game/SpaceColony2086/Meshes/SM_MainModule01.fbx",
    "Game/SpaceColony2086/Meshes/SM_MainModule02_Prefab02.fbx",
    "Game/SpaceColony2086/Meshes/SM_Module03_Base09.fbx",
    "Game/SpaceColony2086/Meshes/SM_AntennaLarge02.fbx",
    "Game/Starship/Meshes/SM_SS_Gate.fbx",
    "Game/Volstok_District/Meshes/LP/SM_Vista_Building_02a.fbx",
    "Game/Volstok_District/Meshes/LP/SM_Vista_Building_04a.fbx",
    "Game/Volstok_District/Meshes/LP/SM_Vista_Building_05b.fbx",
    "Game/Volstok_District/Meshes/LP/SM_Distant_Building_01a.fbx",
    "Game/Monolith_megastructure/Meshes/Modules/SM_tall_module_04.fbx",
    "Game/Monolith_megastructure/Meshes/Modules/SM_round_wall_big.fbx",
    "Game/Monolith_megastructure/Meshes/Red_modules/SM_entrance.fbx",
    "Game/Monolith_megastructure/Meshes/Generator/SM_generator.fbx",
    "Game/Monolith_megastructure/Meshes/Generator/SM_generator_support.fbx",
    "Game/OrbitalBase/Meshes/Domes/SM_Dome01.fbx",
    "Game/OrbitalBase/Meshes/Domes/SM_Dome01OuterGlass.fbx",
    "Game/Tropical_Scifi_City/Meshes/Buildings/SM_building_02.fbx",
    "Game/Tropical_Scifi_City/Meshes/Buildings/SM_big_building.fbx",
    "Game/Tropical_Scifi_City/Meshes/Buildings/SM_tower_01.fbx",
    "Game/Sci-Fi_Flying_Cargo_Ship/Meshes/Reactor/SM_reactor.fbx",
    "Game/Sci-Fi_Flying_Cargo_Ship/Meshes/Hangar/SM_hangar.fbx",
    "Game/Sci-Fi_Flying_Cargo_Ship/Meshes/Cargo_modules/SM_cargo_body_01.fbx",
    "Game/Sci-Fi_Flying_Cargo_Ship/Meshes/Cargo_modules/SM_cargo_front.fbx",
    "Game/SciFi_Cliff/Meshes/Large_module/SM_large_module_01.fbx",
    "Game/SilentZonePack/Assets/SM/SM_Station.fbx",
    "Game/RMG_Crane/Meshes/SM_RMG_Crane_Merged.fbx",
    "Game/Industrial_Pipe_Rack/Meshes/SM_Pipe_Rack_Straight_20m.fbx",
    "Game/Industrial_Pipe_Rack/Meshes/SM_Pipe_Rack_Junction.fbx",
    "Game/Desert_Outpost/Meshs/SM_scope.fbx",
    "Game/Desert_Outpost/Meshs/SM_bck_con_1.fbx",
]

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
        by_path = x["fbx"] in SETTLEMENT_MESHES
        if name not in PARTS.get(pack, []) and not by_path:
            continue
        if by_path:
            name = x["fbx"][len("Game/" + pack + "/"):-len(".fbx")]
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
