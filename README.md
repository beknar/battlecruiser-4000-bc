# Battlecruiser 4000 BC

A space combat demo made in Unity 6 (6000.6, URP). You fly a 180 m frigate against cruisers,
drones, fighters and capital ships. The frigate carries side guns, close-in point defence, a bow
beam that charges for 10 seconds and a hex shield. Every ship comes apart before it explodes.
There are three games: Carrier Demo (open space), Asteroid Ambush and Star System. In Star
System the frigate can also land on the planets, moons and asteroid fields and fly all the way
round them. Star System also runs in a web browser. All of them play with keyboard and mouse or an Xbox 360 / XInput
gamepad.

## Downloads and demo reels

The Windows builds are 64-bit and use DirectX 11. Unzip one and run the `.exe` inside. Every
file is also on the [release page](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/demo-reel-2026-11-03).

| Game | Download | Demo reel |
|---|---|---|
| Carrier Demo (open space) | [battlecruiser-4000-bc-windows.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-windows.zip) (79 MB) | [60 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-demo-reel.mp4) (72 MB) |
| Asteroid Ambush | [battlecruiser-4000-bc-asteroid-ambush-windows.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-asteroid-ambush-windows.zip) (82 MB) | [76 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-asteroid-ambush-reel.mp4) (93 MB) |
| Star System | [battlecruiser-4000-bc-star-system-windows.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-windows.zip) (96 MB) | [100 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-reel.mp4) (133 MB) |
| Star System, web version | [battlecruiser-4000-bc-star-system-web.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-web.zip) (58 MB) | See [The web version](#the-web-version) |

### Carrier Demo

[![The frigate's bow beam hitting the cruiser (click to download the demo reel)](docs/demo-reel-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-demo-reel.mp4)

The reel shows the side guns, CIWS against drones, the cruiser's cannons, missiles and beam hitting
the shield, the 10-second bow beam with its deep charge and fire sound, and ships breaking apart.

### Asteroid Ambush

[![The frigate skimming the big asteroid toward the gas giant (click to download the demo reel)](docs/asteroid-ambush-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-asteroid-ambush-reel.mp4)

The reel opens with the frigate crossing a large asteroid to quiet travelling music and diving
round its far end. There it is ambushed by fighters hidden under the rock, and the music switches
to a battle track. Then the heavy cruiser and its drones come out from behind a boulder. The
frigate fights back with its CIWS and side guns, and turns its bow beam on the cruiser.

### Star System

[![The frigate charging its bow beam at the Outer Drift (click to download the demo reel)](docs/star-system-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-reel.mp4)

The reel starts at the ocean world Thalassa. The frigate opens the system map, picks the gas
giant Jovara and mini-warps there. It then warps across the system to the Outer Drift asteroid
field, where a pirate cruiser and its drones ambush it from the rocks. The frigate turns its
bow beam on the cruiser until it breaks apart. The reel was recorded before landing, flying
round the planets, the ground forces and the capital ships were added, so it doesn't show them.

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
- **Pause menu.** Esc or P (Start on the pad) pauses the game and opens a menu with Resume,
  Restart, Sound on/off and Quit. Restart and Quit ask you to press them a second time to confirm.
- **Sound.** Sound effects come from a sound library, with several recordings of each so repeats
  don't sound identical. Any sound with no recording is synthesised in code instead, and the
  frigate's bow beam is always synthesised. The music has a calm track and a combat track, and
  crossfades to the combat one while fighting is going on nearby. A master limiter stops the mix
  from clipping.

### Carrier mode

The code still supports the original carrier, which has a hangar of fighters, drones and dropships,
missile salvos and wing orders (attack, escort, return). The HUD and controls adapt to whichever
ship is in the scene.

### Second game: Asteroid Ambush

A separate build (`AsteroidAmbush.exe`) set in an asteroid field near a gas giant.

- **The field.** The frigate starts at one end of a 3 km asteroid. Around it are about 230
  smaller rocks, a boulder, and the drifting wreck of a bigger ship of the frigate's class with
  a cloud of tumbling debris. Every rock is generated from a seed when the level loads.
- **Cover.** Asteroids and wreck sections block shots, beams, missiles and line of sight. Guns
  won't fire at, and enemies won't shoot at, a ship hidden behind one. The HUD shows how many
  enemies can see the frigate. The frigate can't fly through rock: it scrapes along the
  surface, losing speed and taking a knock on the shield.
- **The ambush.**
  - Four fighters wait out of sight under the far end of the big asteroid. They attack as soon
    as one of them can see the frigate.
  - Ten seconds later the heavy cruiser and two drones come out from behind the boulder.
  - The music switches from a quiet travelling track to a battle track the moment the ambush
    is sprung.
- **Enemy pilots.** Fighters and drones make attack runs and break away. The cruiser holds a
  stand-off range and circles. Any of them retreats behind the nearest rock or wreck when its
  hull runs low, then comes back. Destroyed enemies respawn where they were hiding and rejoin
  the fight.

### Third game: Star System

A separate build (`StarSystem.exe`, and a web version) set in the Kestrel system, a whole star
system to travel around.

- **The system.**
  - It contains the star Kestrel and five planets: volcanic Cinder, desert Ochre, ocean
    Thalassa, the gas giant Jovara and icy Glacis.
  - Six moons circle Thalassa and Jovara.
  - There are three asteroid fields: the Inner Belt, the Jovara Trojans and the Outer Drift.
  - The frigate starts near Thalassa.
- **System map.** M (R3 on the pad) opens a top-down chart of every place the frigate can go,
  with their orbits and the frigate's own position and heading. For the chosen destination it
  shows the distance and how long the trip will take. Pick a place and warp from the map.
- **Mini-warp.**
  - The ship turns onto its destination and charges, with sparks streaming into the hull.
  - It jumps with a flash and a shockwave ring, then crosses the distance with streaks of
    starlight rushing past. The route bends round any planet in the way.
  - Longer trips take longer.
  - The drive is locked in combat: while an enemy that has left cover is close by, or for a few
    seconds after the ship is hit. Taking fire while charging cancels the jump.
- **Encounters.**

  | Where | Who |
  |---|---|
  | Jovara Trojans | Raiders: four fighters and two drones hidden in the rocks |
  | Outer Drift | A pirate cruiser and two drones hidden in the rocks |
  | Cinder | Corsair patrol: a cruiser and two destroyers |
  | Glacis | Dreadnought battle group: a dreadnought and two escort destroyers |

  The map marks the planets with capital ships on station as hostile. Enemy markers show for
  ships within 30 km.
- **Capital ships.**

  | Ship | Length | Hull | Weapons |
  |---|---|---|---|
  | Destroyer | 153 m | 2,200 | 2 triple cannon turrets |
  | Dreadnought | 702 m | 18,000 | 8 triple cannon turrets and a beam |

  Each turret tracks and fires on its own and breaks off when the ship comes apart. Capital
  ships hold a stand-off range and circle, and their shots don't hit each other.
- **Landing.** In orbit of a planet, moon or asteroid field, and out of combat, K (LB on the
  pad) takes the frigate down to the surface under where it was. Where people live it comes in
  near the nearest landing pad.

  | World | Surface |
  |---|---|
  | Cinder (volcanic) | Basalt plains, cinder cones and rivers of lava under a red haze; the lava burns the hull |
  | Ochre (desert) | Dune seas between flat-topped mesas and salt pans, with blowing dust |
  | Thalassa (ocean) | Islands with beaches, grass and rocky hills in a deep sea; land and sea follow the planet's own map, and near the poles there is snow |
  | Jovara (gas giant) | The cloud tops, with gas-mining platforms riding above them |
  | Glacis (ice giant) | An ice sheet of pressure ridges, crevasses and ice spires under methane haze |
  | Moons | Grey, cratered and airless, with the parent planet in the sky |
  | Asteroid fields | A lump of dark rock with boulders, and the rest of the field drifting overhead |

  **The whole planet can be flown over.**
  - The surface is a grid of 5 km x 5 km areas, sized from the body: four times its size in
    space. The areas add up to its surface area, from 12 areas on the small asteroids to
    2,000 on Jovara.
  - Areas are built round the ship as it flies: in full detail out to 5 km, and in less
    detail out to 15 km for the view. They are built on worker threads, or a few rows a frame
    in the web version.
  - An area is thrown away once the ship is 20 km from it, to save memory.
  - Every area is generated from where it is on the planet, so it comes back the same when the
    ship returns. Flying all the way round, east or west, brings the ship back to where it
    started. Over a pole it comes down the far side, and carrying on over the other pole
    brings it back again.
  - The HUD shows the latitude and longitude, and how many of the planet's areas have been
    explored.

  **Hostile worlds have ground forces** (Cinder, Glacis, the Jovara Trojans and the Outer
  Drift). Areas there hold:
  - gun batteries (a heavy triple turret on a bunker);
  - missile sites;
  - hover tanks that patrol and then chase the frigate;
  - fighters on patrol overhead;
  - bases, whose launch pads hold fighters, and sometimes a gunship, that lift off when the
    alarm goes up.

  An area's forces all go to action stations when one of them sees the frigate within
  5.5 km (hills hide it) or is hit. They stand down when it has gone. Each one can be
  targeted, and what is destroyed stays destroyed.

  Each world has its own sky, fog and light. Where people live (Ochre, Thalassa, Jovara and
  the moons) there are landing pads with parked craft and beacons. On the surface the frigate
  flies as it does in space:
  - it can't sink into the ground, the sea or the deep clouds;
  - a landing assist slows the descent near the ground, so it can set down gently.

  K again, or climbing high above the ground, takes it back to orbit where it left.

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
| Sound on/off | N | (pause menu) |
| Pause menu: resume, restart, sound, quit | Esc or P | Start |
| Window / full screen | F11 | |
| System map (Star System) | M | R3 |
| Land / take off (Star System, in orbit) | K | LB |
| On the map: choose a place / warp / close | Click, arrows or Tab / WARP, double-click or Enter / M or Esc | Left stick, D-pad or LB/RB / A / B |

In the web version, use the page's Full screen button rather than F11.

## Project layout

- `Assets/Carrier/Scripts`: game code: ship control, turrets, CIWS, main beam, shield, health,
  breakup, enemy weapons and sound synthesis
- `Assets/Carrier/Scripts/Demo`: input and gamepad support, HUD, camera, autopilot, the self-test and
  the demo reel recorder
- `Assets/Carrier/Shaders`: the shield bubble and planet atmosphere shaders (URP, additive)
- `Assets/Carrier/Editor`: `CarrierBuilder` generates the materials, prefabs, breakup chunks and
  scene; `CarrierPlayerBuild` builds the Windows player; `CarrierCommandWatcher` runs builds from
  a trigger file
- `Assets/Carrier/Models`: custom models (frigate hull, ball turret, CIWS, beam emitter, carrier
  parts, drone, missile)
- `Assets/WebGLTemplates/StarSystem`: the web page (full-window canvas, loader, Full screen
  button) and `xbox360.js`; `Assets/Carrier/Plugins/WebGL` holds the bridge between the game and
  the page
- `Tools/WebTest`: the headless Chrome gamepad test and its stand-in controller
- `Tools/Blender`: the scripts that make those models (frigate conversion from GLB, ball turret,
  CIWS, beam emitter, carrier parts) and preview renders

## Building

1. Open the project in Unity 6000.6.
2. Run **Tools > Carrier > Build Everything** to generate the materials, prefabs and scene.
3. Run one of these:
   - **Tools > Carrier > Build Windows Demo** for `../Builds/CarrierDemo/CarrierDemo.exe`;
   - **Tools > Carrier > Build Asteroid Ambush** for `../Builds/AsteroidAmbush/AsteroidAmbush.exe`;
   - **Tools > Carrier > Build Star System** for `../Builds/StarSystem/StarSystem.exe`;
   - **Tools > Carrier > Build Star System (Web)** for `../Builds/StarSystemWeb/`.

The Windows players use DirectX 11, which is the stable choice on hybrid AMD + NVIDIA laptops.

## The web version

The web build of Star System uses WebGPU where the browser supports it and falls back to
WebGL 2 elsewhere. The badge in the corner of the page shows which one is in use.

- **Hosting.** Upload `index.html`, `xbox360.js` and the `Build` folder, keeping the folder
  layout. Serve them over https, because browsers allow WebGPU only on secure pages (or on
  localhost). No special server settings are needed, as the game can unpack its own compressed
  files. Loading is faster if the server sends the `.unityweb` files with the header
  `Content-Encoding: br`.
- **Trying it locally.** From the build folder, run `python -m http.server 8080` and open
  `http://localhost:8080`.
- **Gamepads.** Chrome and Edge on Windows report an Xbox 360 pad in the standard layout.
  Firefox on Linux and the macOS 360Controller driver report the raw layout instead, and
  `xbox360.js` translates it so the game still sees a normal gamepad. Rumble works through the
  browser.
- **Quitting.** Quit in the pause menu closes the game, and the page offers Play again.

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
- `StarSystem.exe -systemtest <dir>`: runs the Star System self-test and writes a report and
  screenshots to `<dir>`. The test covers:
  - the system map with a virtual keyboard, mouse and gamepad;
  - warps of different lengths, and that the drive refuses to warp in combat;
  - the pause menu and restart;
  - landing at Thalassa and setting down on the pad;
  - flying on across the planet (areas built ahead and thrown away behind), all the way round it
    east and over both poles, checking the ground comes back the same, then taking off;
  - a capital ship battle, checking that both sides land hits.

  Add `-landtest` (or `?landtest` on the web) to test only the landings, with screenshots, on
  every kind of world. On the hostile ones it also checks that the ground forces are there,
  that a battery fires on the frigate, and that near the north pole (where an area can be built
  on both sides of the pole) each area's forces appear only once.
- `CarrierDemo.exe -reel <dir>` (or `AsteroidAmbush.exe` / `StarSystem.exe`): records a demo reel
  (60 s, 76 s for the ambush or about 170 s for the star system) at 30 fps as JPG frames plus a WAV
  soundtrack, then quits. Join them with ffmpeg:

  ```
  ffmpeg -framerate 30 -i <dir>/f%05d.jpg -i <dir>/reel.wav -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac reel.mp4
  ```

Add `-screen-fullscreen 0 -screen-width 1920 -screen-height 1080` to run any of these modes in a
window.

The web version has two test modes, added to the page address:

- `index.html?systemtest` runs the Star System self-test and logs to the browser console.
- `index.html?padtest` logs every gamepad control as the game reads it.
  `Tools/WebTest/padtest.sh` runs this in headless Chrome with a stand-in Xbox 360 controller,
  in five layouts (standard, Linux wired, Linux wireless, a third-party pad and macOS). It
  renders in software, so the GPU isn't used.

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

`Assets/Carrier/Resources/CarrierAudio` (the sound effect library and the music) is licensed
too and not in the repo. Without it, the sound effects are synthesised in code and there is no
music.
