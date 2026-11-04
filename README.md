# Battlecruiser 4000 BC

A Unity 6 (6000.6, URP) space combat demo: a frigate with ball-mount side guns, CIWS point
defence, a 10-second charged bow beam and a hex shield, fighting drones and a cruiser. Ships
break apart before they explode. Keyboard and Xbox 360 / XInput gamepad are supported.

## Layout

- `Assets/Carrier/Scripts`: game code (ship control, turrets, beam, shield, breakup, sound synthesis)
- `Assets/Carrier/Scripts/Demo`: input, HUD, camera, the `-autotest` self-test and the `-reel` demo recorder
- `Assets/Carrier/Editor`: `CarrierBuilder` generates the materials, prefabs and scene; `CarrierPlayerBuild` builds the Windows player
- `Tools/Blender`: scripts that make the custom models (frigate conversion, ball turret, CIWS, beam emitter)

## Missing third-party assets

`Assets/Carrier/Kit` (marketplace packs from an Unreal asset export: Spaceship_Kit, CargoShip,
Mega_City, MC_Environment, Megastructure_Fallen_City, Sci-Fi_Flying_Cargo_Ship and
StylizedShieldsVol1) is not included because those packs are licensed and can't be
redistributed. The enemy ships' breakup meshes and `Models/ShieldHex.fbx` are derived from them and
are left out too. Without the packs the enemy ships, engine plumes, skybox and shield mesh are
missing. Put your own copies of the packs in `Assets/Carrier/Kit`, then run
**Tools > Carrier > Build Everything** in the editor to rebuild the generated assets.

## Running

- `CarrierDemo.exe -autotest <dir>`: runs the scripted self-test and writes a report and screenshots
- `CarrierDemo.exe -reel <dir>`: records a 60 s demo reel as JPG frames and a WAV soundtrack
