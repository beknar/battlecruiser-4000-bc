# Battlecruiser 4000 BC

A space combat demo made in Unity 6 (6000.6, URP) for Windows. You fly a 180 m frigate against a
heavy cruiser and two recon drones. The frigate carries side guns, close-in point defence, a bow
beam that charges for 10 seconds and a hex shield. Every ship comes apart before it explodes.
It plays with keyboard and mouse or an Xbox 360 / XInput gamepad.

## Demo reel

[![The frigate's bow beam hitting the cruiser (click to download the demo reel)](docs/demo-reel-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-demo-reel.mp4)

**[Download the 60-second demo reel](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-demo-reel.mp4)**
(MP4, 1080p, 72 MB; [release page](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/demo-reel-2026-11-03)).
It shows the side guns, CIWS against drones, the cruiser's cannons, missiles and beam hitting the
shield, the 10-second bow beam with its deep charge and fire sound, and ships breaking apart.

## Features

### The frigate

- **Handling.** Faster than a carrier and slower than fighters and drones: top speed 150 m/s,
  acceleration 30 m/s², turn rate 20°/s.
- **Attitude.** The nose pitches up or down by up to 60°, and a level-off command brings it back.
- **Elevator thrusters.** These move the ship straight up or down. Dorsal and ventral thruster
  flares show which way it is pushing.
- **Engines.** Engine plumes and a hum both grow with the throttle.

### Weapons

- **Side guns.** There is one ball-mount turret on each side. Each can swing up to 120° from
  straight out, so the two arcs overlap ahead, behind, above and below, and both guns can bear on
  a target there. With a target selected, a gun fires
  only once it is lined up, and never through its own hull (it checks for clearance before each
  shot). Auto-fire can be turned on or off.
- **CIWS.** Four rotary point-defence mounts sit on the top deck by the conning tower. They fire only
  at enemy drones and fighters, and each targets whatever hostile small craft is nearest and in reach.
- **Main gun.** The bow beam charges for 10 s: particles are drawn into the emitter and the glow
  builds over a deep, rising growl. It then fires for 2.5 s as a stream of white particles pouring
  from the bow, with a low roar. It fires straight ahead and locks onto the target if the target is
  within 6° of the bow. The HUD shows a charge bar and how far off the bow is.
  Pressing the button again cancels the charge.

### Defence and damage

- **Shield.** A hex bubble absorbs 6,000 damage and recharges at 250 per second after 3 s without
  a hit. When it collapses it stays down for 6 s and comes back at 30%. Each hit leaves a ripple
  sized to its weapon type (projectile, cannon, missile or beam), with a flash and a sound.
- **Hull.** Damage reaches the hull's 6,000 points only after it gets through the shield.
- **Breakup.** When a ship dies, its hull splits into chunks and its turrets break off one after
  another, outer parts first. Then the main blast throws the wreckage outward, where it drifts and
  shrinks away.
- **Frigate destruction and respawn.**
  - While the frigate is destroyed:
    - All its systems shut down: guns, CIWS, main gun, shield, engines and flares.
    - The controls lock.
    - The HUD shows a respawn countdown.
    - Enemies hold their fire.
  - After 8 s the frigate returns to its starting point fully repaired.

### Enemies

| Enemy | Hull | Weapons | Respawn |
|---|---|---|---|
| Heavy cruiser | 3,000 | Cannon bursts, missile salvos and a red beam that charges for 4 s | 6 s |
| Recon drones (2) | 300 each | Blasters | 6 s |

Enemy guns lead their target and have some spread. The enemies fly orbits around the frigate.

### Interface and presentation

- **HUD.** The HUD shows speed, throttle, pitch and climb rate, shield and hull %, main gun charge,
  what the CIWS is engaging, and which guns bear on the current target. A target display gives
  the target's name, range, speed, weapons, threat level and damage taken.
- **Camera.** The orbit camera can be dragged with the mouse or turned with the right stick, and it
  zooms.
- **Gamepad.** A connection toast appears when a pad is plugged in or unplugged, and the pad rumbles
  when weapons fire and when the frigate is destroyed.
- **Help.** An on-screen help list shows only the controls that work on the current ship, and the
  text size can be changed.
- **Sound.** All sounds are synthesised in code at startup, so the game ships no audio files. The
  sounds cover guns, CIWS, missiles, explosions, shield hits, both beams (the frigate's is deeper
  than the cruiser's), engine hum and an ambient drone. A master limiter stops the mix from clipping.

### Carrier mode

The code still supports the original carrier, which has a hangar of fighters, drones and dropships,
missile salvos and wing orders (attack, escort, return). The HUD and controls adapt to whichever
ship is in the scene.

## Controls

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| Throttle / turn | W/S, A/D | Left stick |
| Nose up / down | ↑ / ↓ | D-pad ↑ / ↓ |
| Level off | L | A |
| Elevator thrusters up / down | Space / Ctrl | RT / LT |
| Fire side guns | F | RB |
| Auto-fire on/off | G | L3 |
| Charge main gun (again to cancel) | B | X |
| Next target / stop targeting | T / X (or click a ship) | Y / B |
| Orbit camera / zoom | Right-drag / mouse wheel | Right stick / D-pad ← → |
| Help / text size | H / + − | Back / Back + D-pad ↑↓ |
| Sound on/off | N | Start |
| Window / quit | F11 / Esc | Hold Back + Start |

## Project layout

- `Assets/Carrier/Scripts`: game code: ship control, turrets, CIWS, main beam, shield, health,
  breakup, enemy weapons and sound synthesis
- `Assets/Carrier/Scripts/Demo`: input and gamepad support, HUD, camera, autopilot, the self-test and
  the demo reel recorder
- `Assets/Carrier/Shaders`: the shield bubble shader (URP, additive)
- `Assets/Carrier/Editor`: `CarrierBuilder` generates the materials, prefabs, breakup chunks and
  scene; `CarrierPlayerBuild` builds the Windows player; `CarrierCommandWatcher` runs builds from
  a trigger file
- `Assets/Carrier/Models`: custom models (frigate hull, ball turret, CIWS, beam emitter, carrier
  parts, drone, missile)
- `Tools/Blender`: the scripts that make those models (frigate conversion from GLB, ball turret,
  CIWS, beam emitter, carrier parts) and preview renders

## Building

1. Open the project in Unity 6000.6.
2. Run **Tools > Carrier > Build Everything** to generate the materials, prefabs and scene.
3. Run **Tools > Carrier > Build Windows Demo** to build `../Builds/CarrierDemo/CarrierDemo.exe`.

The player uses DirectX 11, which is the stable choice on hybrid AMD + NVIDIA laptops.

## Command-line modes

- `CarrierDemo.exe -autotest <dir>`: runs the scripted self-test and writes a report and screenshots
  to `<dir>`. The test covers:
  - handling, attitude and elevators;
  - gun arcs, plus checks that no shot misses its aim or hits the ship's own hull;
  - CIWS targeting;
  - main gun timing and damage;
  - shield hits from every weapon type;
  - enemy breakup;
  - frigate destruction and respawn;
  - every gamepad control, using a virtual pad.
- `CarrierDemo.exe -reel <dir>`: records a 60 s demo reel at 30 fps as JPG frames plus a WAV
  soundtrack, then quits. Join them with ffmpeg:

  ```
  ffmpeg -framerate 30 -i <dir>/f%05d.jpg -i <dir>/reel.wav -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac reel.mp4
  ```

Add `-screen-fullscreen 0 -screen-width 1920 -screen-height 1080` to run either mode in a window.

## Missing third-party assets

`Assets/Carrier/Kit` is not in this repo. It holds marketplace packs from an Unreal asset export:
Spaceship_Kit, CargoShip, Mega_City, MC_Environment, Megastructure_Fallen_City,
Sci-Fi_Flying_Cargo_Ship and StylizedShieldsVol1. Those packs are licensed and can't be
redistributed.

Two sets of files are made from those packs, so they are left out too: the enemy cruiser's breakup
meshes and `Models/ShieldHex.fbx`.

Without the packs, these are missing: the cruiser model, the engine plumes, the skybox, some hull
textures and the shield mesh. To restore them, put your own copies of the packs in
`Assets/Carrier/Kit` and run **Tools > Carrier > Build Everything**.
