# Battlecruiser 4000 BC

A space combat demo made in Unity 6 (6000.6, URP). You fly a 180 m frigate against cruisers,
drones, fighters and capital ships. The frigate carries side guns, close-in point defence, a bow
beam that charges for 10 seconds and a hex shield. Every ship comes apart before it explodes.
The game is Star System, a whole star system at its real size. The frigate can also land on its
planets, moons and asteroid fields and fly all the way round them, and mine asteroids, comets and
planets for 27 different minerals. Ore is refined, built into ship parts and traded; destroyers
and two cruisers are built at shipyards, flown, or sent to fly with you as a fleet; wrecks are
salvaged. Five modes of play (combat, mining, navigation, services, fleet) each have their own
keys. Up to seven players fly together through a co-op server, which takes players with its API
keys. It runs on Windows and in a web browser, with keyboard and mouse or an Xbox 360 / XInput
gamepad; a Linux build runs an AI wingman on a server.

The two earlier games, Carrier Demo (open space) and Asteroid Ambush, are
[deprecated](#deprecated-games-carrier-demo-and-asteroid-ambush): Star System has everything
they had.

## Downloads and demo reels

The Windows build is 64-bit and uses DirectX 11. Unzip it and run `StarSystem/StarSystem.exe`.
The builds and the co-op server are on the [14 Nov release](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/star-system-2026-11-14), and
the demo reels on the [3 Nov release](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/demo-reel-2026-11-03) (recorded before the
real-size system).

| Game | Download | Demo reel |
|---|---|---|
| Star System | [battlecruiser-4000-bc-star-system-windows.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/star-system-2026-11-14/battlecruiser-4000-bc-star-system-windows.zip) (357 MB) | [214 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-reel.mp4) (290 MB) |
| Star System: mining, combat, landing and mini-warps | The Star System downloads above | [5 min 10 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-features-reel.mp4) (382 MB) |
| Star System: orbits, stations and launch systems | The Star System downloads above | [3 min 29 s, 1080p](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-orbits-reel.mp4) (157 MB) |
| Star System, web version | [battlecruiser-4000-bc-star-system-web.zip](https://github.com/beknar/battlecruiser-4000-bc/releases/download/star-system-2026-11-14/battlecruiser-4000-bc-star-system-web.zip) (315 MB) | See [The web version](#the-web-version) |

Carrier Demo and Asteroid Ambush are deprecated. Their last downloads stay on the release page,
but they are no longer built or updated:
[Carrier Demo](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-windows.zip)
and [Asteroid Ambush](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-asteroid-ambush-windows.zip).

### Carrier Demo (deprecated)

[![The frigate's bow beam hitting the cruiser (click to download the demo reel)](docs/demo-reel-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-demo-reel.mp4)

The reel shows the side guns, CIWS against drones, the cruiser's cannons, missiles and beam hitting
the shield, the 10-second bow beam with its deep charge and fire sound, and ships breaking apart.

### Asteroid Ambush (deprecated)

[![The frigate skimming the big asteroid toward the gas giant (click to download the demo reel)](docs/asteroid-ambush-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-asteroid-ambush-reel.mp4)

The reel opens with the frigate crossing a large asteroid to quiet travelling music and diving
round its far end. There it is ambushed by fighters hidden under the rock, and the music switches
to a battle track. Then the heavy cruiser and its drones come out from behind a boulder. The
frigate fights back with its CIWS and side guns, and turns its bow beam on the cruiser.

### Star System

[![The frigate flying low across Thalassa, the islands going on to the horizon (click to download the demo reel)](docs/star-system-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-reel.mp4)

The reel starts in orbit over the ocean world Thalassa.
- **Thalassa.** The frigate lands, flies in over an island and sets down on the landing pad. Then
  it lifts off and flies on across the planet, low and at full speed, while the camera cranes up
  to show the mountains, coast and sea going on to the horizon, built round the ship as it flies.
- **The Jovara Trojans.** Back in orbit, it opens the system map and warps to this hostile
  asteroid field, then lands among its ground forces. A gun battery opens fire on it, and its
  point-defence guns shoot down the fighters that come at it. It climbs until the battery is in
  sight, then its bow beam destroys a battery, and it climbs back to orbit.
- **The Outer Drift.** It warps across the system to the Outer Drift asteroid field, where a
  pirate cruiser and its drones ambush it from the rocks. The frigate turns its bow beam on the
  cruiser until it breaks apart.

The capital ships, the comets and mining aren't in the reel. The feature reel below shows them.

### Star System: the feature reel

[![The frigate's mining laser cutting ice from the nucleus of Comet Iskra (click to download the feature reel)](docs/star-system-features-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-features-reel.mp4)

A 5-minute reel of what the frigate can do in the Kestrel system, in four parts. Each scene has a
caption, and the scenes cut through black so there are no long flights in between.
- **Mining.** A mineral scan of the Inner Belt, then the laser cutting a small metallic asteroid
  until it breaks up. Comet Iskra from above with its ion and dust tails, then the laser cutting
  ice from its nucleus. A landing on Ochre, a scan for veins and the laser cutting one in the
  desert floor, while the cargo list fills.
- **Combat.** A pirate cruiser and its drones ambush the frigate in the Outer Drift asteroid
  field, and the bow beam destroys the cruiser. Down on the Jovara Trojans, the frigate fights the
  ground forces and the bow beam destroys a gun battery. At Glacis, the pirate dreadnought and its
  escorts open fire, and the bow beam hits the dreadnought.
- **Landing.** Down through the clouds to Thalassa's island landing pad. Then Cinder's lava
  fields, Glacis's ice ridges, Jovara's cloud tops with a floating mining platform, and the moon
  Selene with its planet in the sky.
- **Mini-warps.** The system map, then warps to Jovara, Comet Mirren and the star Kestrel, and
  home to Thalassa.

### Star System: the orbits and stations reel

[![The frigate docked at the Thalassa Highport, its ring turning and the space elevator's tether running out past it (click to download the reel)](docs/star-system-orbits-thumb.jpg)](https://github.com/beknar/battlecruiser-4000-bc/releases/download/demo-reel-2026-11-03/battlecruiser-4000-bc-star-system-orbits-reel.mp4)

A 3½-minute reel of the orbits, stations and launch systems. Each scene has a caption, and the
scenes cut through black.
- **Orbits.** The system map with the orbits sped up 200 times: the planets, moons and comets go
  round, and the Lagrange points go with Thalassa and Jovara. Then Selene circling Thalassa, 120
  times faster. Only the orbits are sped up; the frigate and everything else run at normal speed.
- **Lagrange points.** The L1 observatory drifts off its unstable point and fires its thrusters
  to get back. The propellant depot at Jovara's L5, and the Trojan asteroids at L4.
- **The Highport and the space elevator.** The geostationary station with its turning ring, and a
  climber riding the tether.
- **Docking.** At the Highport: the hull is repaired, and the ore sold on its trade page.
- **Launch systems.** The launch loop on Thalassa, the StarTram and spin launcher on Ochre, the
  mass driver on Selene and the slingatron on Steropes, each launching a payload.
- **Stations.** Deepcut Station, Ochre Relay and Brontes Shipyard.
- **Armed places.** The guns of Corsair Haven and the Cinder Orbital Battery open fire, and the
  bow beam destroys a gun and a platform. Then the colony on Selene, and Cinder Fortress with its
  garrison firing.

### Star System: the Vrask reel

A reel of the fourth round of improvements, about 4 minutes, with a caption on each scene and cuts
through black between them. It opens with a docking at Hollow Rock flown in by the docking
computer (auto-travel, then K, with no fade) and the fleet's Halberd steering round its leader in
a hard turn. Then the Vrask at Comet Mirren:
- each of their four ships close up;
- J aiming the guns at a Fang's engines until they are knocked out;
- the Snares' ion fire and tractor lock;
- the Fang's boarding pods, a breach and its clamp;
- the Maw's artillery and a nuclear missile in space;
- the Skinks shooting missiles down;
- the whole group fighting as a fleet, phase by phase.

It ends with a nuclear raid on Port Ochre from orbit and the ruins it leaves. Each scene has a time
limit, so a scene that stalls is cut short and the reel still ends. To record it:

```
StarSystem.exe -reel <dir> -vrask -screen-fullscreen 0 -screen-width 1920 -screen-height 1080
```

The frames and `reel.wav` go into `<dir>`, with a log in `reel.txt`. Join them with ffmpeg as
for the other reels.

## Playing the game

### A new game

The first game, with nothing saved yet:

- **The ship.** A frigate, chosen on the start menu (the Sentinel, Lancer, Warden, Talon or
  Vanguard), with the captain chosen there too. The destroyers and the cruisers are built later,
  at a shipyard.
- **Where.** In orbit of Thalassa, the ocean world. The orbits start where the system's clock
  has them.
- **What it has.** A full crew, a full shield and hull, an empty hold and no credits. Nothing
  fitted beyond the ship's own.
- **The mode.** Combat mode (1). The controls box in the top-left corner shows the mode's keys.

### Carrying on

- **Kept between games.** The industry (each site's storage and jobs, the parts fitted to each
  ship), the hold and the credits, the crews and the people waiting to sign on, the ships built
  (berthed, commanded, and the fleet), and the world (craters, rubble, what has been destroyed)
  are saved as the game goes, and the next game carries on from them: the hold, the credits and
  the fittings as they were, each ship's crew as it was (not full), and a destroyer or a
  cruiser among the ships offered on the start menu if one was being commanded.
- **Starting afresh.** `StarSystem.exe -newgame` starts a game as if nothing were saved, and
  saves nothing either: the next game without it carries on from the old save. For a fresh
  start that is kept, delete the save,
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Star System\industry_save.json`. The web
  version has no `-newgame`; clearing the site's data in the browser starts it afresh.

### How a game goes

There is no script: what to do next is the player's. A usual way up:

1. **Mine** (mining mode, 2). Scan (V) in an asteroid field, on a comet or over a planet,
   choose a deposit in the menu (Tab), let the ship fly there (C) and cut it (Z) until the hold
   is full.
2. **Sell or refine** (services mode, 4). Dock or land at a trading station and sell the ore on
   the trade page, or take it to a refinery, store it and refine it into materials.
3. **Build and fit.** At a factory, build ship parts from the materials and fit them to the
   ship (more armour, a stronger shield, faster engines, a bigger hold, more guns...), or sell
   them.
4. **Fight and salvage** (combat mode, 1). Pirates lie in wait in the asteroid fields and
   round their bases; capital ship groups wait on the way in to Cinder and Glacis. A destroyed
   hostile leaves a wreck, with steel, aluminium, copper and sometimes a part to salvage (Z).
5. **Build a bigger ship.** With the parts in a shipyard's storage, lay down a destroyer or a
   cruiser. Once built, take command of her, with crew recruited to fill her complement, or have
   her **join your fleet** and fly the frigate still (fleet mode, 5).
6. **Go anywhere.** The system map (M) warps to the planets, moons, fields, comets and Lagrange
   points; at each, navigation mode's menu (Tab) flies to its bases and stations, lands and
   docks.

### Modes of play

| Mode | For | Its menu (Tab) |
|---|---|---|
| 1 Combat | weapons, targets, lining up, salvage, strikes from orbit | the targets in range, and the wrecks near |
| 2 Mining | the scan, the laser, flying to a deposit, the hold | the deposits found, and the ore aboard |
| 3 Navigation | the system map, cruise, auto-travel, landing and docking | the points of interest at the place the ship is |
| 4 Services | what a station or base offers, docked or on its pad | its services screen: storage, refining, building, fitting, trade, crew, the shipyard |
| 5 Fleet | orders to the other ships | the fleet, and the ships berthed at the yards |

Flying is the same in every mode. See [Controls](#controls) for every key.

## Features

### The frigates and the start menu

There are five frigates to choose from. As each game starts, the start menu offers them, with a
turning picture of each, bars comparing them, and the five captains. The game waits while the
menu is up. Launching with a different frigate starts the level again with it. The menu shows once
a run, and the frigate is kept for the game: its crew, parts and hold go with it, so the pause menu
doesn't change it. The choice is remembered for next time.

| Frigate | Kind | Guns | CIWS | Hull / shield | Speed, turn |
|---|---|---|---|---|---|
| Sentinel (the starting frigate) | Gun frigate: all-rounder | 2 ball-mounted guns on its pods | 4 | 6000 / 6000 | 150 m/s, 20°/s |
| Lancer | Fast attack frigate | 2 twin turrets, one on each flank | 2 | 4500 / 5000 | 190 m/s, 28°/s |
| Warden | Heavy escort frigate | 3 twin turrets: forward, under the keel, aft | 4 | 8500 / 8000 | 120 m/s, 15°/s |
| Talon | Strike frigate | 2 heavy twin turrets on its flanks | 1 | 5500 / 5500 | 165 m/s, 22°/s |
| Vanguard | Line frigate | 6 turrets bearing forward: two twins on the foredeck, four ball domes on the flanks; the strongest bow beam | 4 | 9000 / 7000 | 115 m/s, 13°/s |

Every frigate has a bow main gun, at least one CIWS mount, the shield and the mining gear. The
Lancer, Warden, Talon and Vanguard fight with the turrets modelled on them: each traverses on its own
mount and elevates its barrels (the Warden's barrels are part of their housings, so its turrets
traverse as a whole), firing from the barrels' muzzles. Their numbers are in
`Assets/Carrier/Data/frigates.json`.

The four newer frigates started as AI-made models in another paint scheme (`ai models/frigate1.glb`,
`frigate 2.glb`, `frigate3.glb`, `frigate5.glb`). They were repainted in the starting frigate's naval grey, slate
and red (`Tools/Blender/recolour_frigate.py`, kept in `ai models/retextured`), and converted for
the game with their turrets cut out of the hull (`Tools/Blender/convert_frigates.py`, from
`Tools/Blender/frigates_spec.json`).

### Destroyers and the cruiser

Every game starts in a frigate. Five destroyers and two cruisers can be flown once they have
been built at a shipyard and taken command of (see
[Shipyards](#shipyards-destroyers-and-the-cruiser)). Until then the start menu doesn't offer
them, and nor does `-frigate`; afterwards it offers the one the player commands. That holds for
AI players and the reels too: only the self-tests may start in any ship.

- **Destroyers** are tougher than any frigate but slower. They have no bow main gun. They carry
  more turrets (their own, plus ball mounts on the hull), fewer CIWS mounts (one or two), and a
  limited stock of missiles fired from launch cells on the deck.
- **The cruisers** are tougher than any destroyer and have more turrets. They have the bow main
  gun, four or six CIWS mounts and missiles. The Sovereign is the all-rounder; the Dominion trades
  half the missiles, two CIWS mounts and some armour for the heaviest, longest-ranged guns and beam.
- **Crews.** Destroyers need a bigger crew than any frigate (350 to 460), and the cruisers need
  the most (800 and 900). A new ship comes off the slips empty. Taking command of it brings the crew
  across from the ship you were flying, as many as it needs, and the rest are recruited.
- **Missiles.** Each missile fired is one fewer in the magazine. The magazine is refilled when
  the ship docks at a station or lands at a site. Fire them with 4 (M in a level without a warp
  drive) or LB with a target. The ship's console shows the ready cells and the missiles left.

| Ship | Kind | Turrets | CIWS | Main gun | Missiles (cells) | Hull / shield | Speed, turn | Crew |
|---|---|---|---|---|---|---|---|---|
| Halberd | Missile destroyer | 8: a quad mount, four twins, 3 ball mounts | 2 | none | 24 (6) | 12000 / 10000 | 108 m/s, 12°/s | 380 |
| Rapier | Fast destroyer | 7: a single, a triple and two twins, 3 ball mounts | 1 | none | 12 (4) | 11000 / 9500 | 112 m/s, 13°/s | 360 |
| Claymore | Gun destroyer | 8: three heavy singles, 5 ball mounts | 2 | none | 12 (6) | 13000 / 10500 | 105 m/s, 11°/s | 420 |
| Bulwark | Heavy destroyer | 8: four twins, 4 flank mounts | 2 | none | 16 (4) | 15000 / 12000 | 100 m/s, 10°/s | 460 |
| Stiletto | Strike destroyer | 7: one big dorsal turret, 6 ball mounts | 1 | none | 20 (8) | 11500 / 9000 | 110 m/s, 12°/s | 350 |
| Sovereign | Heavy cruiser | 12: eight twins, 4 ball mounts | 6 | 3200/s, 16 s charge | 32 (8) | 26000 / 20000 | 90 m/s, 8°/s | 800 |
| Dominion | Gun cruiser | 12: three triples, five twins, 4 ball mounts | 4 | 3600/s, 18 s charge | 16 (4) | 24000 / 18000 | 95 m/s, 7°/s | 900 |

The seven were AI-made models (`ai models/destroyer1.glb` to `destroyer5.glb`, `cruiser1.glb`,
`cruiser2.glb`), repainted like the frigates and turned to lie bow first along the axis with
`Tools/Blender/align_glb.py`. The Dominion (`cruiser2.glb`) was rebuilt as well by
`Tools/Blender/build_cruiser2.py`: its crooked AI turrets replaced with new ones built square to
the ship, more detail modelled on (a mast, sensor domes, vents, conduits, antennas), and a new
texture baked with strong panel lines, with a normal map of them (the `normalMap` in
`frigates.json`). They were then converted by `convert_frigates.py` (the `ships` in
`frigates_spec.json`), which also gives where their ball mounts and missile cells go.

### The Sentinel

- **Handling.** Faster than a carrier and slower than fighters and drones: top speed 150 m/s,
  acceleration 30 m/s², turn rate 20°/s.
- **Attitude.** The nose pitches up or down by up to 60°, and a level-off command brings it back.
- **Elevator thrusters.** These move the ship straight up or down. Dorsal and ventral thruster
  flares show which way it is pushing.
- **Ships keep clear of each other.** Under an autopilot (auto-travel, auto-flight, the AI wingman, the fleet) a ship
  steers round the other ships in its way rather than through them: it looks a few seconds ahead for any that would
  come closer than the two hulls and a margin, and turns off to one side, easing the throttle for one ahead. The two
  ships always turn opposite ways, in one game or in two (co-op). Ships alongside on the same course, and docked or
  towed ones, are left alone. If two hulls ever do meet, each ship is pushed back out of the other.
- **Engines.** Engine plumes and a hum both grow with the throttle. They are out while the ship is docked or on a tow, and touching down throttles back.

### Weapons

- **Side guns.** There is one ball-mount turret on each side. Each can swing up to 120° from
  straight out, so the two arcs overlap ahead, behind, above and below, and both guns can bear on
  a target there. With a target selected, a gun fires
  only once it is lined up, and never through its own hull (it checks for clearance before each
  shot). Auto-fire can be turned on or off.
- **CIWS.** Four rotary point-defence mounts sit on the top deck by the conning tower. They fire only
  at enemy drones and fighters and at hostile missiles and boarding pods in flight, and each
  targets whatever is nearest and in reach, a missile or pod over a craft unless the craft is less than half as far. A missile takes two
  of their bolts to shoot down, a Vrask boarding pod five. (The Vrask Skink's point-defence guns
  do the same to your missiles: see The Vrask.)
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
- **Kinds of weapon, and armour.** Each kind of weapon is hard or light on each layer of a ship:
  ion is hard on shields and on components and light on the hull, a penetrator goes straight
  through armour, a nullifier strips shields but barely scratches the hull, and artillery and
  nuclear warheads are heavy on everything. Armour, between the shield and the hull, stops a share
  of each hit that gets through the shield and wears away as it does; a repair (docked) or a
  respawn makes it whole.
- **Components.** A ship's weapons, engines and shield generator each have a condition. They take
  damage from what gets through the shield, on top of the hull's (and ion reaches the engines and
  the shield generator even through a shield that holds). Aim at one (J in combat mode, Back + L3
  on the pad; the target panel shows each one's condition and which is aimed at) and most of that
  damage goes to it; otherwise a little goes to one at random. Below 25% a component is knocked
  out, and stays out until repaired above 50%: the weapons don't fire, the engines give 15% of top
  speed and 30% of acceleration, or the shield drops and doesn't recharge. Components repair
  slowly after 20 s without a hit, and all at once docked or respawned. Their condition isn't
  saved: a ship comes into a game whole.
- **Tractor beams.** The Vrask Snare's tractor beam locks onto a ship within 2.5 km and pulls it
  in at 30 m/s, holding down its speed and turning it slowly stern on to the Snare, until it is
  held 1.5 km off. One beam can be out-flown: at full thrust straight away the ship is faster
  than the pull, and the lock breaks at 3.25 km. Two beams on one ship, or one on a ship whose
  engines are knocked out, hold it fast. The lock also breaks when the Snare is destroyed or its
  weapons are knocked out. While held, the warp drive, docking and landing are refused ("held by
  a tractor beam"). The HUD shows "Tractor lock: N beams" in red at the top of the screen, with
  whether full thrust can break it; a notice says when the lock comes on and when it is gone.
- **Boarding.** Vrask boarding pods fly at a ship like missiles (and the CIWS shoot them down
  like missiles). One that reaches a ship whose shield is down, or under a quarter, breaches the
  hull and puts 8 to 12 breachers aboard; against a stronger shield it bursts for a little
  damage. Boarders fight the crew: each second every boarder overcomes a quarter of a crewman,
  and every crewman who can get at them (four to a boarder) kills a fiftieth of one, so a big
  crew wears a small party down and a small crew is overrun. Half the crew they overcome are
  taken prisoner, half killed. Kill them all and they are repelled, and those they had taken are
  freed. The HUD shows "BOARDERS ABOARD: N" in red with the crew fighting them, and the warp
  drive, docking and landing are refused ("boarders aboard"). A Vrask Fang can clamp its prow to
  your hull and send its own party through: clamped, you crawl at 4 m/s and can't warp.
- **Capture.** Boarders who bring the crew down to its survivors (15% of the complement, the
  fewest a crew is ever left with) take the ship. It isn't destroyed: nothing breaks up and no
  wreck is left. The cargo is seized (the credits are kept), the crew the boarders took are
  prisoners of the Vrask, and after 8 s the survivors bring the ship back to the last place it
  warped to, whole; a notice says "Captured by the Vrask: N of your crew taken". A fleet ship
  captured is lost from the fleet, her crew taken. The Vrask's prisoners are counted and saved,
  and the target display shows how many they hold when a Vrask ship is targeted.
- **Artillery.** The Vrask Maw's spinal gun reaches 13 km (no turret reaches half as far) but
  fires only every 16 s, and its shells are slow: 550 m/s, so 15 to 25 s in flight. Each is
  aimed at where its target will be when it gets there, from the target's course and speed, and
  flies straight to that point: turn or change speed while it is on its way and it bursts where
  you would have been. It bursts 500 m round (1 km across), 700 at the centre and less further out (a fifth of
  it at the edge), on every ship in the blast but the Vrask's own. Its round suits the target: a
  shield nullifier while your shield is up (three times as hard on a shield, light on the
  hull), an armour penetrator once it is down or at armour (straight through the plating), high
  explosive at anything else. In co-op its shells show in every game, and its blasts hit the
  other players' ships through their own games.
- **Nuclear missiles.** The Maw carries two in its silos, no more till it is destroyed and back.
  It launches one only rarely: at a big ship whose shield holds under its shells, or at ships
  bunched together. The missile is slow (320 m/s) and the CIWS can shoot it down (four bolts).
  In space it goes off in a blast 1.2 km round, 3,000 at the centre: with no air to carry a shock
  wave it breaks a frigate's shield but doesn't destroy a whole one. On a world with air it is
  another thing (see the Vrask siege, under Strikes from orbit).
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

- **The ship's console.** A console in the bottom-left corner (drawn at 92% size, so it takes
  about 15% less of the screen) shows the ship's state:
  - **The captain.** An animated portrait of the ship's captain (see below).
  - **Alerts.** A strip that turns red and flashes when the ship is under fire, a missile is
    inbound, the shield is down or the hull is critical. It turns amber when enemies are near,
    with how many and how far, and shows ALL CLEAR otherwise.
  - **Bars.** Shield, hull, thrust (with the speed), the bow gun's charge (and whether it is on
    target), the warp drive (charging, in warp, or locked and why) and the cargo hold (with the
    credits).
  - **Weapon lights.** Whether the guns bear on the target, the CIWS engaging, the missile cells,
    the wing in flight and the mining laser.
  - **What is around.** The target and its range, the nearest place, the landing and docking
    prompts, and the mining laser and sensor.

  The top-left panel now shows only the controls, and H hides it. A target display at the top
  right gives the target's name, range, speed, weapons, threat level and damage taken.
- **The captain.** Her portrait on the console reacts to everything that happens to the ship:
  - she winces when the hull is hit, and is alarmed when the shield is hit, when it goes down, at a
    missile inbound and at first contact with enemies;
  - she is set-jawed as the guns, missiles, wing or bow gun fire, and intent while the bow gun
    charges, in a fight, mining, scanning, charging the warp drive, docking and landing;
  - she smiles at a kill, an arrival, docking, touchdown, a rock mined out and a full hold;
  - she is awed in warp, grieves when the ship is lost, and is determined when it is back.

  The portrait is animated:
  - **Expressions in steps.** Each expression has three steps: a hint of it, clearly it, and all
    of it. A new expression grows through its steps, each blended into the next. On the way to
    another, she first relaxes back down through the steps of the one she is leaving. A shock,
    like a hit, comes on faster.
  - **Life.** She breathes, her head sways a little, and she blinks: eyelids half shut, closed,
    then open again, now and then twice. She mouths each message as it appears, her lips
    parting and opening through two steps.
  - **Hits.** The picture jolts and flashes red when the ship is hit.
  - **Speech.** She says a short line about what has just happened, typed out on the console's
    comms line while her mouth moves. For example: "Shields are holding!", "Missile inbound!",
    "Scratch one.", "Arrived at Jovara.", "Rocks don't shoot back. I like rocks."
    - There are 268 lines for 33 events, in `Assets/Carrier/Resources/Captains/lines.txt`, which
      is easy to edit.
    - A line is chosen at random, and none repeats until all of its event's lines have been
      used.
    - Urgent news (a hit, the shield down, a missile, contact, a kill) can cut in. Anything else
      waits its turn, and she doesn't say the same kind of thing again too soon.
    - She takes command as each level starts, and remarks on the quiet now and then.

  There are five captains to choose from on the start menu: Elena Vasquez, Amara Okafor, Yuki
  Tanaka, Freya Lindqvist and Priya Raman. All are young officers in their late twenties. The
  choice is remembered between sessions. Once the game has started she stays in command for it:
  the pause menu shows her but can't change her.

  The portraits were made with a local [InvokeAI](https://github.com/invoke-ai/InvokeAI) server:
  - Each captain's base portrait came from Juggernaut XL v9.
  - Every step of every expression is an edit of that portrait with FLUX.1 Kontext dev, so the
    face stays the same throughout.
  - Each picture was then lined up with the base portrait (OpenCV, matched on the background and
    uniform), so only the expression moves.

  Each captain has 26 pictures: calm, three steps each of smiling, determined, alarmed, wincing,
  intent, awed and sorrowful, and two steps each of blinking and talking.
- **Camera.** The orbit camera can be dragged with the mouse or turned with the right stick, and it
  zooms.
- **Gamepad.** A connection toast appears when a pad is plugged in or unplugged, and the pad rumbles
  when weapons fire and when the frigate is destroyed.
- **Help.** An on-screen help list shows only the controls that work on the current ship, and the
  text size can be changed.
- **Pause menu.** Esc or P (Start on the pad) pauses the game and opens a menu with Resume,
  Restart, Sound on/off, Co-op, the HUD's settings and Quit. Restart and Quit ask you to press
  them a second time to confirm. The captain in command is shown below the menu (chosen on the
  start menu; she can't be changed once the game has started).
- **Setting up the HUD.** In the pause menu, kept for next time:
  - **The captain** shown or hidden: without her portrait the console is narrower, the ship's
    state and the comms line still on it.
  - **HUD size** (left/right on its row, or click its < side for smaller and its > side for
    bigger; + and − in the game, Back + D-pad ↑↓ on the pad):
    everything drawn on screen, smaller to see more of the game, or bigger, in steps of 15%
    from 55% to 295% (100% always one of them). Enter on the row goes a size up, round to the
    smallest after the biggest. The pause menu itself never grows past the screen's height.
  - **The fleet's panels** shown or hidden: down the right side, below the target's, a small
    panel for each ship of the fleet with her name, her state (under fire, engaged, under tow
    or all clear, the panel's edge coloured to match), her hull and shield, her guns (how many,
    how many firing) and what she is doing.
  Tests, reels and AI players use the defaults and keep nothing.
- **Sound.** Sound effects come from a sound library, with several recordings of each so repeats
  don't sound identical. Any sound with no recording is synthesised in code instead, and the
  frigate's bow beam is always synthesised. A master limiter stops the mix from clipping.
- **Music.** A playlist of 26 calm tracks and 24 combat tracks, from the Space and Science
  Fiction Music Packs (Vol 1 and 2), Sci Fi Fantasy Music and the Space Journey Music Pack, all
  brought to the same loudness (`Tools/Audio/stage_music.sh`).
  - It plays the calm tracks while it is quiet, and crossfades to the combat tracks while
    fighting is going on nearby, then back. A piece with both a calm and a combat arrangement
    carries on in step in the other one.
  - Whichever is playing, it changes track every one to three minutes, the next fading in as
    the last fades out. It goes through all of a list, shuffled, before it plays one again.
  - The pause menu turns the music on or off (the rest of the sound goes on; kept for next
    time), and its Song row skips to the next song or back to the one before (left/right, or
    click its < or > side). Shift+N is the next song anywhere in the game.
  - Only the tracks playing are loaded; on Windows they stream from the disk.

### The original carrier

The game began with a carrier. Its prefab, and its fighters, drones and dropships, are no longer
built; its hull lives on as the enemy dreadnought. The code for a ship with a hangar (launching
craft and giving wing orders) is still there but no ship uses it now.

### Second game: Asteroid Ambush (deprecated)

A separate build (`AsteroidAmbush.exe`) set in an asteroid field near a gas giant. Deprecated:
see [Deprecated games](#deprecated-games-carrier-demo-and-asteroid-ambush).

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
  - It contains the star Kestrel and six planets: volcanic Cinder, desert Ochre, ocean
    Thalassa, Pelagia (the pirates' water world), the gas giant Jovara and icy Glacis. The
    planets and moons are in `Assets/Carrier/Data/star_system.json`.
  - Six moons circle Thalassa and Jovara.
  - There are three asteroid fields: the Inner Belt, the Jovara Trojans and the Outer Drift.
  - Two comets are on long orbits: bright Comet Iskra close to the star, and faint Comet
    Mirren far out.
  - Ten stations, bases and installations have been built (see below), five industrial sites
    (refineries and factories: see Industry), ten settlements (colony stations, cities,
    military and industrial bases: see Crew and the settlements), and six launch structures on
    the planets and moons.
  - The frigate starts near Thalassa.
- **Real sizes.** The planets are hundreds of kilometres across and millions of kilometres
  apart, and the mini-warp crosses the gaps (16 seconds at most).

  | Body | Across | From the star |
  |---|---|---|
  | Cinder | 320 km | 1.1 million km |
  | Ochre | 480 km | 1.8 million km |
  | Thalassa | 640 km | 2.6 million km |
  | Pelagia | 440 km | 3.0 million km |
  | Jovara | 1,000 km | 3.6 million km |
  | Glacis | 840 km | 4.6 million km |
  | The moons | 90 to 280 km | 1,400 to 3,100 km from their planets |
  | Kestrel (the star) | 100,000 km | |

  - **Precision.** A float holds a position to the centimetre only within a few hundred
    kilometres of the origin. So where everything is in the system is worked out in doubles
    (`Double3`, `OrbitalSystem`), and the world is drawn round the frigate: the origin is moved
    back to it whenever it strays 20 km from it, and all through a warp. Ships and other free
    things far from the frigate are kept in doubles in their orbit's frame until it comes back.
    Co-op games send positions in the system's doubles.
  - **Seeing far.** The camera sees out to 1,200 km. A planet, moon or the star further than
    500 km off is drawn nearer and smaller in the same proportion, which looks the same from the
    camera (`ScaledSpace`), and is lit from where it really is.
  - The level is laid out small, about 6,500 km across, with every orbit 800 times its laid-out
    size (`StarSystemData.LayoutScale`, `CarrierBuilder.DistanceScale`).
- **Orbits.** Everything moves on its orbit, at real speeds: the planets take days to weeks to
  go round the star, the moons hours.
  - **Kepler orbits.** The planets go round the star, and the moons and the orbiting stations
    round their planets. Each period follows from Kepler's third law (T² ∝ a³), with the star's
    mass set by Thalassa's year of 30 days.

    | Body | Period |
    |---|---|
    | Cinder | 8.4 days |
    | Ochre | 18 days |
    | Thalassa | 30 days |
    | Jovara | 49 days |
    | Glacis | 73 days |
    | Selene round Thalassa | 18 h |
    | The orbital battery round Cinder (a polar orbit, 120 km up) | 2.6 h |

  - **Comets.** They follow long ellipses. Iskra starts at its closest to the star and swings
    out fast, its tails turning to stay pointed away from the star. Mirren starts at its
    farthest and moves slowly.
  - **What goes with each body.** Everything on or round a body goes with it: bases, launchers,
    moons, stations, the asteroid fields' rocks and bases. So do the enemy patrols posted there.
  - **The frigate's frame of reference.** Wherever the frigate is (in a planet's, moon's,
    station's or field's reach), that place stays still and the rest of the system moves round
    it. Fights, shots and landings near it behave as before. A warp to a moving place arrives
    where the place has got to, and a take-off returns to the same spot over the planet.
  - **The system map** shows it all moving: the orbits, the comets' ellipses, and the five
    Lagrange points of Thalassa and Jovara. The info panel gives each place's period.
  - **Lagrange points.** They move with their planets, at the real positions.
    - L1 and L2 are at the edge of the planet's Hill sphere, r(μ/3)^⅓ from it. Thalassa's L1 is
      12,000 km sunward.
    - L4 and L5 are 60° ahead of and behind the planet.
    - L4 and L5 are stable, because Jovara is under 1/26 of the star's mass. The Trojan rocks at
      L4 and the depot at L5 swing very slowly round their points: one swing takes the planet's
      period divided by √(27μ/4), about thirty years at Jovara's real mass.
    - L1 is unstable. The observatory drifts off it, faster and faster, by a factor of e about
      every two days, and fires its thrusters to get back.
- **Launch structures.** Each is a warp destination, arrived at from above, with a landing pad
  by it on the surface. Each turns with its world, launching payloads that leave a glowing
  trail into space.

  | Structure | Where, and why there |
  |---|---|
  | Space elevator | Thalassa turns once in 6 hours, so its geostationary orbit is 400 km up (720 km from its centre). A tether runs from a sea platform on the equator up to the Highport there, and on to a counterweight, with climbers riding it |
  | Launch loop | Thalassa: a cable loop held up 350 m over the sea between two stations 4.5 km apart; payloads ride the top and are let go |
  | StarTram | Ochre: a maglev tube along the desert, then rising 900 m out of the thin air, tethered down |
  | Spin launcher | Ochre: a vacuum centrifuge whose arm whirls a payload up and lets it go into a launch tube |
  | Mass driver | Selene: a 1.7 km electromagnetic catapult across the airless moon, flinging ore buckets off it |
  | Slingatron | Steropes: a spiral track 700 m across; payloads spiral outward, faster each turn, and leave along a ramp |
- **Comets.** Each comet is a dark, lumpy nucleus of dirty ice inside a glowing coma, with the
  two tails real comets have, both pointing straight away from the star:
  - a thin blue ion tail, blown straight out by the solar wind, with rippling streamers;
  - a broader yellow-white dust tail, which curves back along the comet's path because the
    dust is pushed more gently and falls behind.

  A trail of debris, lumps of ice and grit shed by the nucleus, lies along the start of the
  dust tail. Iskra, close to the star, has tails 60 km long; Mirren's are shorter and fainter.
- **Mining.** The frigate carries a mining laser, a mineral sensor and a cargo hold.
  - **Mineral sensor** (V in mining mode, Y on the pad). It always picks up traces of the deposits within range: 12 km in
    space, 5 km on a planet's surface. A scan sends out a pulse that sweeps out to full range in
    2 seconds. Each deposit it finds is marked on screen for a minute, with its main minerals,
    how many tonnes are left and how far it is.
  - **Mining laser** (Z in mining mode, A on the pad). A turret under the bow cuts ore out of the nearest deposit within
    1.5 km and draws it back aboard in a stream of glowing grit, 8 tonnes a second. When a
    deposit is mined out it moves on to the next one in reach; a small rock (under 80 m
    across) breaks up. It passes over a deposit it can't get a clear line to, and stops when
    the hold is full.
  - **Choosing a deposit.** In mining mode, the menu (Tab) lists the deposits the scan found,
    nearest first: what each is, its two most valuable minerals, the tonnes left and how far it
    is. Choosing one makes it the laser's: it cuts that one while it is in reach, rather than
    the nearest. C (X on the pad) then flies the ship to it: to an asteroid or a comet right up
    to the rock, easing off over the last few hundred metres and cutting every engine within
    100 m of it (or as close as the hull lets her); over a planet's surface to within the laser's
    reach, climbing clear of the ground first. The chosen deposit is marked on
    screen.
  - **Cargo hold.** It holds 600 tonnes. I lists what is aboard and what it is worth; J
    jettisons the cheapest mineral to make room for better. The menu lists the ore aboard too,
    and ejects whichever is chosen (Enter, or J). The cargo is lost if the frigate is
    destroyed.
  - **What there is to mine.** Every rock, moon and comet has minerals in it, and so does the
    ground of every world the frigate can land on: one to three veins in each 5 km area.

    | Where | What it is made of |
    |---|---|
    | C-type asteroids (carbonaceous) | Silicates, carbon, water ice, iron, nickel, sulfur, a little cobalt |
    | S-type asteroids (stony) | Silicates, iron, nickel, magnesium, aluminium, titanium, copper, traces of silver and gold |
    | M-type asteroids (metallic) | Iron and nickel, with cobalt, copper, platinum, palladium, iridium and gold |
    | Comets and their debris | Water ice, carbon dioxide, methane and ammonia ices, silicates, carbon, deuterium |
    | Moons | Silicates, aluminium, iron, titanium, magnesium, rare earths, uranium, helium-3 |
    | Cinder (volcanic) | Sulfur, iron, copper, gold, uranium, platinum and diamonds |
    | Ochre (desert) | Iron, aluminium, copper, titanium, lithium, rare earths, uranium, silver, gold |
    | Thalassa (ocean) | On the islands: ores, silver and gold. Under the sea: heavy-water brine (deuterium, lithium) |
    | Jovara (gas giant) | Gas pockets in the cloud tops: hydrogen, helium, ammonia, methane, deuterium, helium-3 |
    | Glacis (ice giant) | Water, methane and ammonia ices, deuterium, helium-3 and diamonds |

    How much a deposit holds depends on its size: from about 80 tonnes in a small rock to
    1,500 in the biggest, and far more in a moon or a comet's nucleus. Restarting the level
    fills every deposit again.

  The frigate has the same gear in the two deprecated games. In Asteroid Ambush the rocks can
  be mined too.
- **Stations, bases and installations.** Each one is a warp destination of its own.

  | Place | What it is |
  |---|---|
  | Thalassa Highport | A space station: a habitat ring 1.4 km across, turning slowly round its docking hub, at the top of Thalassa's space elevator |
  | Deepcut Station | A mining base dug into a big rock at the edge of the Inner Belt: refinery towers, ore tanks and a mass driver |
  | Corsair Haven | A pirate base in a rock at the edge of the Outer Drift, with six gun emplacements round it (hostile) |
  | Cinder Orbital Battery | Orbital weapons over Cinder's north pole: three gun platforms (three turrets and missiles each) and a beam lance (hostile) |
  | Brontes Shipyard | A drydock in orbit of Brontes, with a destroyer half built inside its gantries |
  | Ochre Relay | A communications relay over Ochre: three big dishes on a 480 m mast |
  | Thalassa L1 Observatory | At the L1 Lagrange point between Kestrel and Thalassa: a 600 m sunshield, and a telescope in its shadow |
  | Jovara L5 Depot | A propellant depot at Jovara's trailing Lagrange point, L5, 60 degrees behind the planet (the Trojans are at L4) |
  | Selene Base | A colony on the surface of Selene: domes, habitats, a control tower and tanks round a landing pad |
  | Cinder Fortress | The corsairs' stronghold on the surface of Cinder (hostile) |

  - **Docking** (K, Back + B on the pad). (The ore is sold on the services screen's trade page,
    at a market.) Within 1.6 km of the berth of a station or base, and out of combat, K brings
    the frigate in to it. Farther out, but where a warp or an auto-flight there leaves the ship
    (a few kilometres off: Deepcut at the edge of the Inner Belt, the Lantern Arcology, Kestrel
    Deep Yard), K flies her in to the berth first, round to its open side, then docks: it
    doesn't land on the field or the body nearby. The last of the way in is flown by the docking
    computer, with no fade: it lines her up at a point 300 m out along the berth's way in, then
    takes her in, down onto a pad from over it on the lift thrusters or nose first into a pier,
    and draws her in the last few metres (about half a minute to a minute; the HUD says which
    step it is on). Steering, moving the throttle or a fight calls it off; if it gets nowhere
    (blocked for 15 s, or over two and a half minutes) it finishes the old way, with a fade. The
    fleet waits outside while she docks. There it is repaired, hull and shield, and where there
    is a market (all but the shipyard) the whole hold is sold. The HUD keeps the credits. K
    again undocks, and the ship pulls away under its own power, up off a pad first.
  - **The hostile ones fight.** Their guns are cold until the frigate comes within range in
    plain sight: 5.2 km of Corsair Haven's guns, 6.5 km of the battery's platforms. Then
    everything that can see it opens fire, and the warp drive is locked. Each gun and platform
    can be targeted and destroyed; destroyed platforms come apart, and all of them are rebuilt
    after two minutes or so. Out of range, they go cold again.
  - **Bases on the surfaces.** Selene Base and Cinder Fortress can be seen from orbit.
    Warping to one arrives straight above it, and landing from there (K) comes down beside it:
    by Selene Base's landing pad, or 2.6 km from the fortress, facing it. On the ground they are
    bigger, with two rings of buildings. The fortress has a ring of gun batteries and missile
    sites, tanks, and a gunship and fighters on its pad.
- **Places and points of interest.** The system map warps to **places**: the star, the planets,
  the moons, the asteroid fields, the comets, the Lagrange points, and what is built out in deep
  space on its own (the Kestrel Deep Yard). Everything else is a **point of interest** at a
  place: a base on a planet or moon, a station or battery in orbit close by, a base in an
  asteroid field. The map lists the points of interest at the place chosen.
  - **Landmarks.** Every planet, moon and asteroid field has more points of interest than its
    bases. A planet or moon with a surface has a site in its northern and in its southern lands
    (fly there, or K to fly there and land) and a high orbit over its pole; a gas giant, a high
    orbit over its pole and one over its equator; an asteroid field, its dense cluster and its
    outer rocks.
  - **At a place**, in navigation mode, the menu (Tab) lists its points of interest, nearest
    first (a place that is itself somewhere to dock, out on its own like the Lantern Arcology
    or Kestrel Deep Yard, is listed too, so K flies in and docks there), with how far each is,
    roughly how long the trip takes, and whether it has a pad or a
    berth.
  - **Auto-travel.** Enter flies the ship there by itself, in real time: no warp and no fade.
    Round a planet it cruises along the great circle on a glide slope, as high above the
    destination as there is still way to go (cruise is faster higher up), so it comes in fast
    and slows smoothly, the throttle easing off at the end. Across a field it flies straight.
    K flies there and then lands on that base's pad (the auto-lander takes her down) or docks
    at the station. Enter to a station ends in docking range, on the open side of its berth
    ("Arrived at Hollow Rock: K docks"); the distance shown for a station is to its berth. On the
    way in, the ship keeps clear of the rock or hull the berth is built into, going round it by
    the open side, and if it gets stuck it backs off and tries another way (three times, then it
    says so). K pressed while flying to a station docks there, even where the ship is nearer the
    middle of the asteroid field than the station. Landing or docking any other way ends the
    trip. The trip takes time: a few minutes round a planet. Auto-travel goes only to the points
    of interest at the place the ship is at; the map warps to the others.
  - **By hand.** C sets the course without flying it. The point of interest is marked on screen
    with its distance and the time to it at the speed the ship is making, or with a pointer at
    the screen's edge when it is behind. The menu opens on the course, so Tab, then Enter, flies
    it (A on the pad). X stops an auto-travel (and calls off the auto-lander if it is taking her
    down), and so do taking the helm, moving the throttle and changing mode; the course stays
    marked. A course is forgotten once the ship has warped somewhere else.
  - An AI player's "warp to" order, and the self-tests, still warp straight to a point of
    interest with the drive.
- **System map.** M (R3 on the pad), in any mode, opens a top-down chart of every place the frigate can go,
  with their orbits and the frigate's own position and heading. Stations and installations are
  small squares, bases and Lagrange points diamonds. For the chosen destination it shows what
  it is, the distance and how long the trip will take. Pick a place and warp from the map.
  - **Your side's ships.** The other players' ships (cyan), the AI wingmen (orange) and the
    fleet's ships (green) are on it too, each with its heading and name. Click another player or
    a wingman (its arrow or its name: in formation the arrow is under the frigate's own), then
    WARP (or double-click it), for a mini-warp to them: the ship drops out close by, astern of
    them and to one side. (Not to one down on a planet, nor to one already within 3 km; the
    fleet flies with you.) Navigation mode's menu (Tab) lists them too, under ALLIES: Enter
    mini-warps to the one chosen.
  - **AI wingmen** do the same: one left more than 15 km behind its leader mini-warps to them.
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
  | Cinder Orbital Battery | Three gun platforms and a beam lance |
  | Cinder Fortress (on the ground) | Gun batteries, missile sites, tanks, a gunship and fighters |
  | Corsair Haven | Six gun emplacements |
  | Glacis | Dreadnought battle group: a dreadnought and two escort destroyers |
  | Comet Mirren | Vrask raiders: two Snares, a Fang, a Maw and two Skinks, hidden round the comet |
  | Vrask Siege Group (over Port Ochre) | A Maw and two Skinks on station, shelling the city every two hours |

  **The Vrask.** An alien faction, lizard-like, who take prisoners, drain their life and capture
  ships. Their raiders lie in wait round Comet Mirren, far out from the corsairs' haunts, for the
  ships that come to mine its ice, and spring at 4 km. They are the first enemies with shields,
  and their hulls are armoured. Each of their four ships has its own job: the **Snare** strips
  shields with ion fire from medium range, bow on; the **Fang** makes fast runs with heavy guns
  and missiles; the **Maw** holds well back and shells what the others spot with its spinal
  artillery, and carries two nuclear missiles; the **Skink** screens the others with eight
  rapid-fire guns that shoot your missiles down, and runs ahead as the group's eyes. They fight
  as a group, in a set order (see The Vrask, under Features). They are hostile like the pirates,
  shown as "Hostile: Vrask" on the target display. Their numbers are in
  `Assets/Carrier/Data/vrask.json` (see `docs/allships.md`).

  **The Snares' capture.** The Snares hold about 1.8 to 2.2 km off (1.45 to 1.8 km while one holds
  you) and don't close. Their ion fire
  goes for your shield generator, then, with the shield down, your engines. Once the engines are
  knocked out, or you are crawling with your shield down, a Snare locks its tractor beam on you
  and pulls you in (see Tractor beams). Two Snares work as a pack: one keeps up the ion fire and
  the other holds you in its beam and turns its guns on any other ship that comes near. Close to
  within 900 m and a Snare breaks away, to come back in at its range. A Snare that holds a ship
  whose shield is down launches two boarding pods at it every 30 s (see Boarding).

  **The Fang's assault.** The Fang goes first for a ship the Snares hold or have disabled. It
  comes in fast from an odd angle: from well above or below you, or out from behind another
  Vrask ship. Its heavy guns aim at your weapons, and as it closes it launches a wave of three
  boarding pods every 18 s once your shield is under half. With your shield down it slows to
  your speed and clamps its prow to your hull, sending a party of 12 through, and holds you
  there while they fight. It casts off when they are repelled, your ship is taken, or its own
  hull is under 30%, flies clear and comes round again.

  **The Maw's bombardment.** The Maw keeps 8 to 12 km from its target, on the far side of the
  rest of the Vrask, bow on, and once there hardly moves. It has no firing solution of its own:
  it fires only at a ship one of the others has within 6 km (a Snare, the Fang), or a Skink
  within 9 km, so kill the ships near you and its shells stop. Two Maws fire their salvos together, on one beat.
  Its shells go for your shield generator while your shield is up, then your weapons. Come within 3 km and it falls
  back toward its escorts, slowly: it is the slowest ship in the system bar the dreadnought.

  **The Skinks' screen.** Each Skink flies a loose patrol 1.5 to 2.5 km round the Maw or a Snare
  that holds a ship. Your missile on its way to one of the group, your fighters and drones, or
  your ship near the Maw (within 3 km, or closing on it within 6 km), and a Skink goes for it at twice its acceleration,
  across a missile's path; its guns take your missiles and craft before anything else, and two
  of its needles bring a missile down. While the group closes in, the Skinks run ahead as its
  eyes (they see 9 km). A light or damaged ship that runs, they chase, in fast hit-and-run passes.

  **They come back, location by location.** Each band is a location. Every 5 minutes, the
  destroyed enemies of a location spawn there again, back in their hiding places. But once all
  of a location's enemies have been destroyed, none spawn there for 10 minutes: the place stays
  clear (a notice says so), then they all come back and the 5-minute spawns go on. In co-op the
  host's game decides it, as it does the enemies.

  The map marks the planets with capital ships on station as hostile. Enemy markers show for
  ships within 30 km. The capital ship groups at Cinder and Glacis wait on the way in, a few
  kilometres past where a warp drops the ship, straight ahead and a little below it. The planets go round
  over days and weeks, so the way in from anywhere turns with them. Whenever the warp drive
  charges for the planet, or for a base on it or a place in orbit close by, a group lying in
  wait out of sight moves onto that warp's way in.
- **Salvage.** Whatever hostile is destroyed (a ship, a fighter or drone, a gun emplacement, a
  battery platform) leaves a wreck: a few of its scorched pieces drifting where it died. A wreck
  holds steel plate, aluminium sheet and copper wire by its size, and a capital ship's wreck holds
  a ship part half the time (armour plating, a turret mount, fire control...). In combat mode the
  wrecks near are marked on screen, and the combat menu (Tab) lists them under WRECKS, with what
  they hold and how far they are. Choose one, fly within 1.5 km of it and press Z (Back + X on the
  pad): the salvage beam strips it into the hold over a few seconds, as much as there is room for.
  What it brings aboard is the industry's own goods, stored, sold or built with like any other.
  A wreck lasts 15 minutes. Wrecks aren't shared in co-op: each player salvages their own.
- **Capital ships.**

  | Ship | Length | Hull | Weapons |
  |---|---|---|---|
  | Destroyer | 153 m | 2,200 | 2 triple cannon turrets |
  | Dreadnought | 702 m | 18,000 | 8 triple cannon turrets and a beam |

  Each turret tracks and fires on its own and breaks off when the ship comes apart. Capital
  ships hold a stand-off range and circle, and their shots don't hit each other.
- **The sea.** On the ocean worlds (Thalassa and Pelagia) the frigate can go into the water.
  - **Splashdown.** Coming down on the sea, the descent eases near the water and the ship
    splashes down: spray is thrown up along the hull and a ring of foam spreads out round it.
  - **Afloat.** It sails on the surface like a surface ship, at 60% of its top speed, riding
    the swell with its nose level. It leaves a wake: churned water astern and bow waves rolling
    out to either side, with spray off the bow when it goes fast. The elevator thrusters
    pushing up (Space, RT) lift it off the water.
  - **Under water.** Held pushing down (Ctrl, LT) for a moment afloat, it dives, with a splash
    and the water boiling round the hull as it goes under. Under water it is slower (45%),
    trails bubbles, and flies as in the air down to the sea bed. The view goes blue and close,
    with the light dimmed. The surface hides it from anything above: only torpedoes reach it.
  - **Up again.** Coming up gently it surfaces and floats. With the elevator thrusters
    pushing up it bursts straight out of the water and flies on.
  - **The sea forces.** Pelagia's pirates have torpedo bases on the sea floor, which find a
    ship by sonar, and attack submarines, which patrol until their garrison is alerted, then
    hunt the nearest player ship, in the water or afloat. Their torpedoes keep under the
    surface, so they can't reach a ship in the air. A torpedo has no rocket flame: it trails
    bubbles, and draws a line of foam on the surface when it runs shallow. Their numbers are in
    `Assets/Carrier/Resources/Data/ground_units.json` (`seaBase`, `submarine`).
- **Flying down.** The frigate can be flown all the way down to a planet or moon and back up,
  with no break:
  - **Up** is out from the body near it. The ship flies level over the ground wherever it is
    on the planet, and the camera's horizon turns with it.
  - **Cruise** (O in navigation mode, X on the pad): out of combat and more than 6 km from a station, the top speed grows with
    the height above the ground (up to 15 km/s high over a planet). It slows near the ground
    and in thick air.
  - **The air** thickens toward the ground. Too fast in thick air, the hull heats up, shakes
    and at worst is damaged. The sky closes over the stars, the fog comes in, and the light
    takes the world's colours, until at the hand-off height it is the world's own sky.
  - **The ground** seen from space is the planet's own surface (`PlanetTerrain`): a cube
    sphere of patches split finer the nearer the camera is, from the same heights the ship
    lands on, coloured from the same maps the planet wears from afar.
  - **The hand-off.** Below 30 km the ground under the ship starts being built. At 3.2 km
    (2.2 km on an airless moon) the ship goes over to it: the same place, height, heading and
    speed, under the same sun, through a wisp of cloud. Climbing back above it returns the
    ship to the air over the planet. The rest of the system is moved out of sight meanwhile.
  - **The auto-lander.** In orbit, out of combat, K (Back + B on the pad) flies the frigate down
    through the air to the nearest landing pad (or a few kilometres from a hostile fortress)
    and sets it down. On the ground, K flies it back up to orbit. Any steering takes the
    controls back.
  - Asteroid fields are landed on as before, with a fade.
- **Strikes from orbit.** What is on a planet's ground is where its surface says it is, so a
  strike from orbit works out what it hits whether or not anyone is down there (`OrbitalStrikes`):
  - ground forces in the blast are destroyed, each rebuilt in its own time;
  - a settlement's buildings in it are ruined (rubble, till they are rebuilt an hour later), and
    its people killed (fewer recruits there);
  - a crater is left in the ground, scorched, on the surface landed on and on the ground seen
    from space alike;
  - it is all kept and saved with the world (`WorldState`).

  **The frigate's kinetic strike** (Y in combat mode): over a planet or moon, from space, it drops a tungsten rod
  on the nearest ground forces within 30 km of the point below (a fortress, a battery, tanks), or
  on the ground straight down. The rod falls for a few seconds, a white streak glowing in the air,
  and lands in a flash and a shock ring that spread for kilometres: everything within 450 m is
  destroyed. One every 20 seconds. The HUD shows what it would hit.

  **Corsair strike satellites** hang over two settlements, turning with their bodies, and fire
  on them on the game's clock, whether anyone is near or not (`StrikeSatellite`):

  | Satellite | Over | Weapon | Every |
  |---|---|---|---|
  | Corsair Ion Satellite | Port Ochre (250 km up) | Ion cannon: a beam to the city | 30 min |
  | Corsair Mass Driver | Pyrois Domes (120 km up) | A salvo of three slugs | 20 min |

  Near one, its strikes are seen. Each is a hostile destination that fights back (the beam lance,
  a gun platform); destroyed, it strikes no more until the corsairs put it back half an hour
  later.

  **The Vrask siege.** A Vrask Maw and two Skinks hold station 160 km over Port Ochre, the only
  city on a world with air (Ochre), turning with the planet, and raid it every two hours of the
  game's clock, whether anyone is near or not (`VraskBombardment`, saved with the world). A raid
  is four shells, 500 m round each: first at the fixed defences round the city (gun batteries,
  missile sites, a base's pads: none at Port Ochre, since Ochre isn't hostile, but a siege
  elsewhere would break them first), then into the city, building by building (rubble for an
  hour, as a kinetic strike's). Rarely (one raid in ten, on a fortified city, and no more often
  than every six hours) the Maw sends a nuclear missile down instead: its blast covers the whole
  city, every building in it is ruined for eight hours, and half of its people left are
  killed (fewer recruits there). The group is a hostile destination ("Vrask Siege Group"): its
  guns open up on a ship within 12 km, and its artillery on one its escorts can see. Destroy the
  Maw and the raids stop until the Vrask send another, half an hour later. In co-op the host's
  game runs the siege and every strike is sent to the other games, so the city is ruined in
  all of them.

  | World | Surface |
  |---|---|
  | Cinder (volcanic) | Basalt plains, cinder cones and rivers of lava under a red haze; the lava burns the hull |
  | Ochre (desert) | Dune seas between flat-topped mesas and salt pans, with blowing dust |
  | Thalassa (ocean) | Islands with beaches, grass and rocky hills in a deep sea; land and sea follow the planet's own map, and near the poles there is snow |
  | Pelagia (ocean, hostile) | An archipelago in a deep sea, held by pirates: island batteries and missile sites, torpedo bases on the sea floor and attack submarines |
  | Jovara (gas giant) | The cloud tops, with gas-mining platforms riding above them |
  | Glacis (ice giant) | An ice sheet of pressure ridges, crevasses and ice spires under methane haze |
  | Moons | Grey, cratered and airless, with the parent planet in the sky |
  | Asteroid fields | A lump of dark rock with boulders, and the rest of the field drifting overhead |

  **The whole planet can be flown over.**
  - The surface is a grid of 5 km x 5 km areas covering the body at its real size. The areas
    add up to its surface area, from 12 areas on the small asteroids to about 300,000 on Jovara.
  - Areas are built round the ship as it flies: in full detail out to 5 km, and in less
    detail out to 15 km for the view. They are built on worker threads, or a few rows a frame
    in the web version.
  - An area is thrown away once the ship is 20 km from it, to save memory.
  - The garbage is collected before a surface is built and after it is thrown away on take-off,
    and when the level is restarted (once before it is opened again, and once the old one is
    closed).
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
  targeted. What is destroyed stays destroyed until it is rebuilt, whether or not anyone is
  there: a tank or a fighter in about 10 minutes of game time, a battery or missile site in 20,
  a submarine in 15, a sea base in 30 (`rebuildMinutes` in `ground_units.json`). Landing again
  after that finds it back.

  Each world has its own sky, fog and light. Where people live (Ochre, Thalassa, Jovara and
  the moons) there are landing pads with parked craft and beacons, and a colony round each pad
  on solid ground: domes, habitats, a control tower, tanks, a solar field, a comms mast and a
  hangar. On hostile worlds the bases have fortress buildings round their pads: bunkers, a
  command post, radar, walls, fuel tanks, barracks and a watchtower. On the surface the frigate
  flies as it does in space:
  - it can't sink into the ground, the sea or the deep clouds;
  - a landing assist slows the descent near the ground, so it can set down gently.

  K again, or climbing high above the ground, takes it back to orbit where it left.

### Industry: refineries, factories and ship parts

Mined ore can be refined into materials, the materials built into ship parts, and the parts
fitted to the frigate to make it better. All of it is in
`Assets/Carrier/Resources/Data/industry.json`.

- **Five industrial sites**, one in each kind of place, each a destination on the system map:
  - **Ochre Foundry**, on the desert world Ochre, and **Mnemos Works**, on Thalassa's moon
    Mnemos: a refinery and a factory either side of a landing pad. Land from orbit over one (K),
    fly onto the pad and set down on it.
  - **Jovara Gas Works**, a gas refinery in orbit round Jovara, and **Kestrel Deep Yard**, a
    factory alone in deep space between the planets: dock at them (K).
  - **Belt Smelter**, a metal smelter on a rock at the edge of the Inner Belt (across the belt
    from Deepcut Station), with a landing pad on the rock: docking there sets the frigate down
    on the pad.
- **The site's screen** opens by itself on arriving (docked, or landed on the pad), which puts
  the game in services mode, and Tab or U (A on the pad) in services mode opens it again.
  What it offers depends on what the place is, page by page. It has up to seven pages:
  - **Storage**: move ore, refined materials and parts between the hold and the site's
    storage. Each site keeps its own storage, with no limit; the frigate's hold is what is
    limited, so materials have to be carried from a refinery to a factory.
  - **Refine**: queue refining jobs. A job takes its minerals from the site's storage and puts
    the refined material back into it.
  - **Build**: queue factory jobs, refined materials into parts.
  - **Fit**: fit parts from storage (or the hold) to the frigate, or take them off again (they
    go back into storage). Each part can be fitted once, and each frigate has so many slots.
  - **Trade**, at a trading station: a market (the Thalassa Highport, Deepcut Station, the
    Jovara L5 Depot) or any place where people live. Sell what is aboard, at its worth: the ore
    all at once, or item by item (Enter one lot, Shift+Enter all of it). Buy raw minerals,
    refined materials and ship parts at 1.3 times their worth (Enter one lot, Shift+Enter
    ten). A refinery's trade page buys minerals only. Docking no longer sells the ore by
    itself.
  - **Crew**: recruit, where the place recruits, or drop crew off (one or ten at a time): they
    go ashore and join those waiting there to sign on (never below the few a ship keeps through
    anything). A screen that opened because the crew is short opens on this page.
  - **Shipyard**, where there is one: build destroyers and the cruisers, and take command of
    the ships berthed there (see below).
- **Jobs take time** (20 to 45 s a batch to refine, 80 to 180 s a part), one at a time on each
  line, the refinery's and the factory's. They go on while the frigate is away.
- **The frigates differ in what they carry and fit:**

  | Frigate | Hold | Part slots |
  |---|---|---|
  | Sentinel | 600 t | 4 |
  | Lancer | 450 t | 3 |
  | Warden | 800 t | 5 |
  | Talon | 550 t | 4 |
  | Vanguard | 500 t | 3 |

  Ore, refined materials and parts all count against the hold (a part weighs what it says
  below). Markets still buy only the ore.
- **Saved.** The sites' storage and queues, the hold and credits, and each frigate's fitted
  parts are saved to `industry_save.json` in the player's data folder (a few seconds after
  anything changes, every minute and on quitting), and read again when the game starts. The
  save also holds the world's clock and what it remembers of each planet: which areas have
  been explored, and which ground forces were destroyed and when (`WorldState`).

**The world goes on when it isn't drawn.** The sites keep refining and building, the recruits
come back and the destroyed ground forces are rebuilt on the game's clock, with nothing of
them loaded. The heavy places (the ten settlements and the industrial sites, built from big kit
meshes and their 4K textures) are loaded only when the frigate comes near: each keeps its warp
point, berth, crew and industry in the level, and its looks are a prefab in
`Resources/Streamed` (`StreamedVisual`), loaded within about 25-40 km and let go, with their
textures, beyond about a third further. Arriving suddenly (a warp) loads it at once; flying in
loads it in the background. The settlements' surface layouts are loaded only to build their
surface, and let go with it.

The models are the project's own: built in Blender by `Tools/Blender/build_industry.py`
(`Assets/Carrier/Models/Ind_*.fbx`, their lights and berths in
`Assets/Carrier/Data/industry_hardpoints.json`), textured with panel, paint, hazard, grating,
tank, pipe, window and pad textures made by `Tools/Textures/make_industry_textures.py`
(`Assets/Carrier/Art/Industry`). The pictures of the materials and parts are rendered by
`Tools/Blender/build_goods.py`.

**Refined materials** (refined from mined minerals; a batch makes the tonnes shown):

| | Material | Form | Refined from | Makes | Time | Value | Refined at |
|---|---|---|---|---|---|---|---|
| <img src="Assets/Carrier/Resources/Industry/Icons/SteelPlate.png" width="64"> | Steel plate | Rolled plates, stacked and strapped | 5 t iron, 0.5 t carbon | 5 t | 20 s | 40 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/TitaniumBillet.png" width="64"> | Titanium billets | Square-section billets | 4 t titanium, 1 t magnesium | 4 t | 30 s | 140 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/AluminiumSheet.png" width="64"> | Aluminium sheet | Coils of sheet | 5 t aluminium | 4.5 t | 20 s | 45 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/CopperWire.png" width="64"> | Copper wire | Spools of drawn wire | 4 t copper | 4 t | 25 s | 85 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/Superalloy.png" width="64"> | Superalloy ingots | Nickel-cobalt ingots | 3 t nickel, 1 t cobalt, 1 t iron | 4 t | 35 s | 160 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/Magnets.png" width="64"> | Rare-earth magnets | Sintered magnet blocks | 1 t rare earths, 1 t iron, 0.2 t cobalt | 1.5 t | 40 s | 700 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/Conductors.png" width="64"> | Precious conductors | Gold, silver and platinum foil and bar | 0.1 t gold, 0.3 t silver, 0.05 t platinum | 0.4 t | 40 s | 4,500 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/FuelRods.png" width="64"> | Reactor fuel rods | Bundles of clad fuel rods | 1 t uranium, 0.2 t lithium | 0.9 t | 45 s | 700 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/SiliconWafer.png" width="64"> | Silicon wafers | Polished wafers in cassettes | 10 t silicates, 1 t hydrogen | 1 t | 35 s | 300 cr/t | Ochre Foundry, Mnemos Works, Jovara Gas Works |
| <img src="Assets/Carrier/Resources/Industry/Icons/ArmourGlass.png" width="64"> | Armour glass | Thick laminated panes | 6 t silicates, 1 t aluminium | 4 t | 25 s | 25 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/DiamondOptics.png" width="64"> | Diamond optics | Cut lenses and prisms | 0.1 t diamonds | 0.08 t | 45 s | 30,000 cr/t | Ochre Foundry, Mnemos Works, Belt Smelter |
| <img src="Assets/Carrier/Resources/Industry/Icons/CarbonFibre.png" width="64"> | Carbon-fibre rolls | Rolls of woven carbon fibre | 4 t carbon, 2 t methane | 3 t | 30 s | 90 cr/t | Ochre Foundry, Mnemos Works, Jovara Gas Works |
| <img src="Assets/Carrier/Resources/Industry/Icons/Coolant.png" width="64"> | Coolant | Pressurised coolant drums | 2 t ammonia, 2 t water | 3.5 t | 20 s | 30 cr/t | Ochre Foundry, Mnemos Works, Jovara Gas Works |
| <img src="Assets/Carrier/Resources/Industry/Icons/FusionPellets.png" width="64"> | Fusion fuel pellets | Cryogenic pellet canisters | 1 t deuterium, 0.1 t helium-3 | 1 t | 40 s | 1,500 cr/t | Ochre Foundry, Mnemos Works, Jovara Gas Works |

**Ship parts** (built at a factory from refined materials; each can be fitted once):

| | Part | Looks like | Built from | Weighs | Time | Fitted | Value |
|---|---|---|---|---|---|---|---|
| <img src="Assets/Carrier/Resources/Industry/Icons/ArmourPlating.png" width="64"> | Armour plating | A curved section of thick steel-and-titanium hull armour, bolted ribs behind | 30 t steel plate, 8 t titanium billets | 40 t | 90 s | hull +15% | 2,800 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/CompositeArmour.png" width="64"> | Composite armour | Layered titanium, carbon-fibre and glass armour tiles in a frame | 12 t titanium billets, 10 t carbon-fibre rolls, 6 t armour glass | 25 t | 150 s | hull +20% | 4,200 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/ShieldEmitter.png" width="64"> | Shield emitter | A ringed emitter dish on a coil-wound base | 6 t superalloy ingots, 3 t rare-earth magnets, 8 t copper wire, 1 t silicon wafers | 15 t | 120 s | shield +20% | 6,500 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/CapacitorBank.png" width="64"> | Capacitor bank | A rack of cylindrical capacitors with heavy bus bars | 10 t copper wire, 0.5 t precious conductors, 6 t coolant | 18 t | 100 s | shield recharge +35% | 4,300 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/FusionThruster.png" width="64"> | Fusion thruster | A bell-nozzled fusion drive with a magnetic throat and fuel lines | 12 t superalloy ingots, 3 t fusion fuel pellets, 5 t coolant | 30 t | 150 s | top speed +12%, acceleration +15% | 7,000 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/VectoringJets.png" width="64"> | Vectoring jets | A cluster of four gimballed manoeuvring nozzles on a mount | 6 t titanium billets, 4 t superalloy ingots, 3 t copper wire | 12 t | 90 s | turn rate +20% | 2,200 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/CargoPod.png" width="64"> | Cargo pod | A ribbed aluminium cargo container with clamp points and a hatch | 25 t aluminium sheet, 10 t steel plate | 20 t | 80 s | hold +30% | 1,700 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/RailBarrels.png" width="64"> | Rail barrels | A pair of long magnetic rail barrels with cooling jackets | 8 t superalloy ingots, 4 t rare-earth magnets, 3 t coolant | 14 t | 120 s | gun damage +15% | 4,300 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/FireControl.png" width="64"> | Fire-control computer | An armoured computer case with a sensor eye and cable ports | 2 t silicon wafers, 0.3 t precious conductors, 0.05 t diamond optics, 2 t copper wire | 3 t | 140 s | gun range +15% | 5,000 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/AutoLoader.png" width="64"> | Autoloader | A belt-fed loading mechanism with a shell carousel | 8 t steel plate, 4 t superalloy ingots, 2 t copper wire | 10 t | 100 s | rate of fire +15% | 1,400 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/BeamLens.png" width="64"> | Beam focusing lens | A diamond lens stack in a cooled, finned housing | 0.08 t diamond optics, 2 t armour glass, 4 t coolant | 5 t | 160 s | beam damage +20% | 3,000 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/ReactorCore.png" width="64"> | Reactor core | A shielded reactor vessel with a glowing core window and coolant pipes | 2 t reactor fuel rods, 8 t superalloy ingots, 8 t coolant | 25 t | 180 s | beam charge time −20% | 3,200 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/MiningHead.png" width="64"> | Mining laser head | A laser emitter head with a diamond aperture and heat fins | 0.04 t diamond optics, 3 t copper wire, 2 t coolant, 4 t steel plate | 6 t | 90 s | mining rate +50% | 2,000 cr |

**Ship sections** (built at a factory for a shipyard; never fitted):

| | Section | Looks like | Built from | Weighs | Time | Value |
|---|---|---|---|---|---|---|
| <img src="Assets/Carrier/Resources/Industry/Icons/HullFrame.png" width="64"> | Hull frame section | Two deep ring frames joined by stringers over a keel beam | 80 t steel plate, 30 t titanium billets, 10 t superalloy ingots | 120 t | 240 s | 9,000 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/TurretMount.png" width="64"> | Turret mount | A training ring and bearing race on a deck plate, with its drive | 10 t steel plate, 6 t superalloy ingots, 4 t copper wire, 1 t rare-earth magnets | 20 t | 150 s | 5,200 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/MissileCells.png" width="64"> | Missile cell block | Four vertical launch cells in a frame, their hatches shut | 12 t steel plate, 5 t superalloy ingots, 4 t copper wire, 1 t fusion fuel pellets | 25 t | 160 s | 6,000 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/BridgeModule.png" width="64"> | Bridge module | A command block with armoured windows and a sensor mast | 15 t aluminium sheet, 6 t armour glass, 3 t silicon wafers, 0.4 t precious conductors, 6 t copper wire | 30 t | 200 s | 9,500 cr |
| <img src="Assets/Carrier/Resources/Industry/Icons/CrewQuarters.png" width="64"> | Crew quarters | A habitation block of berths, galleys and an airlock, for forty | 25 t aluminium sheet, 10 t steel plate, 4 t coolant, 4 t carbon-fibre rolls | 35 t | 120 s | 2,500 cr |

**The sites:**

| Site | Where | Refines | Builds and fits | Shipyard |
|---|---|---|---|---|
| Ochre Foundry | on a planet (pad): Ochre | everything | everything | yes |
| Mnemos Works | on a moon (pad): Mnemos | everything | everything | yes |
| Jovara Gas Works | in orbit (docked): Jovara | carbon-fibre rolls, coolant, fusion fuel pellets, silicon wafers | - | - |
| Kestrel Deep Yard | in deep space (docked) | - | everything | yes |
| Belt Smelter | on an asteroid (pad): Inner Belt | steel plate, titanium billets, aluminium sheet, copper wire, superalloy ingots, rare-earth magnets, precious conductors, reactor fuel rods, armour glass, diamond optics | hull frame sections, turret mounts, crew quarters | yes |
| Steropes Heavy Industries | on a moon (pad): Steropes | everything | everything | yes |
| Ochre Orbital Works | in orbit (docked): Ochre | steel plate, titanium billets, aluminium sheet, copper wire, superalloy ingots, rare-earth magnets, armour glass | everything | yes |

### Shipyards: destroyers and the cruiser

Every site with a shipyard builds the destroyers and the cruisers: in deep space (Kestrel Deep
Yard), in orbit (Ochre Orbital Works), on a planet (Ochre Foundry), on moons (Mnemos Works,
Steropes Heavy Industries) and on an asteroid (Belt Smelter). The site's **Shipyard** page lists
the ships. A ship is built from parts and sections (built at a factory) and steel, all in that
site's storage. Laying one down takes them out of storage (stopping it puts them back). It
then takes 9 to 21 minutes on the slips, two ships at a time, while the player is away.

- A finished ship is berthed at its yard, with nobody aboard. The player can have one of each
  class.
- **Take command** of a berthed ship on the Shipyard page. It is swapped in where the frigate
  lies, and the crew come across. The ship flown until then stays behind: a built one is
  berthed at that yard, and a frigate can be chosen again from the start menu.
- The start menu offers the frigates and the built ship you command. A berthed ship is taken
  command of at its yard, where the crew can come across. Choosing a frigate sends the ship
  commanded until then back to its yard.
- The ships built, where each is berthed and the one commanded are saved with the rest.

| Ship | Built from | Time |
|---|---|---|
| Halberd | 4 hull frame section, 8 turret mount, 2 missile cell block, 1 bridge module, 10 crew quarters, 1 reactor core, 2 fusion thruster, 1 shield emitter, 2 armour plating, 1 fire-control computer, 150 t steel plate | 10 min |
| Rapier | 4 hull frame section, 7 turret mount, 1 missile cell block, 1 bridge module, 9 crew quarters, 1 reactor core, 2 fusion thruster, 1 shield emitter, 2 armour plating, 1 vectoring jets, 120 t steel plate | 9 min |
| Claymore | 4 hull frame section, 8 turret mount, 1 missile cell block, 1 bridge module, 11 crew quarters, 1 reactor core, 2 fusion thruster, 1 shield emitter, 2 armour plating, 1 rail barrels, 160 t steel plate | 10 min |
| Bulwark | 4 hull frame section, 8 turret mount, 2 missile cell block, 1 bridge module, 12 crew quarters, 1 reactor core, 2 fusion thruster, 1 shield emitter, 2 armour plating, 2 armour plating, 200 t steel plate | 11 min |
| Stiletto | 4 hull frame section, 7 turret mount, 2 missile cell block, 1 bridge module, 9 crew quarters, 1 reactor core, 2 fusion thruster, 1 shield emitter, 2 armour plating, 1 fire-control computer, 120 t steel plate | 9.33333 min |
| Sovereign | 8 hull frame section, 12 turret mount, 2 missile cell block, 1 bridge module, 20 crew quarters, 2 reactor core, 3 fusion thruster, 2 shield emitter, 4 armour plating, 2 capacitor bank, 1 beam focusing lens, 1 fire-control computer, 400 t steel plate, 100 t titanium billets | 20 min |
| Dominion | 8 hull frame section, 12 turret mount, 1 missile cell block, 1 bridge module, 23 crew quarters, 2 reactor core, 3 fusion thruster, 2 shield emitter, 4 armour plating, 2 capacitor bank, 1 beam focusing lens, 2 fire-control computer, 2 rail barrels, 450 t steel plate, 80 t titanium billets | 21 min |

### Fleets

A player can fly a fleet: the ship they command and the others they have built, up to **ten
ships** in all. In fleet mode the others take orders while the player flies their own.

- **Joining.** A ship built at a shipyard is berthed there. On the yard's shipyard page
  (services) a berthed ship can:
  - **Join the fleet** (J, X on the pad), crewed. Recruits sign on at the yard first, paid
    from the hold (250 cr each), up to her complement. If they are too few, crew come across from
    the ship commanded, as many as it can spare while keeping half its own. She needs half her
    complement to sail. If even then she can't, she doesn't join, and nothing is paid or moved.
  - **Be towed** (T, Y on the pad), with nobody aboard. While anything is under tow the
    ship commanded makes half its speed, and its warps take twice as long, the charge and the
    trip.
  - Or be **left in the yard** for later. A fleet ship can be left at a yard too ("Leave her
    here"), and joins again from there.
- **Orders** (fleet mode), to the ship chosen in the menu or the whole fleet:
  - follow me: loose formation, fighting back if attacked;
  - guard me: in formation, going for any enemy that comes within 6 km of the ship commanded;
  - form up: close formation;
  - attack my target: the ship commanded's target, then back into formation;
  - hold position.
  A towed ship takes no orders.
- **Flying together.** The fleet keeps station off the ship commanded's quarters, matching its
  heading and speed. It warps with it, arriving on its stations. Left far behind, as when the
  ship commanded cruises, it catches up out of sight. It can't be sent off on its own, to mine
  or to fight elsewhere in the system: that is what an AI player is for. It waits in orbit
  while the ship commanded is down on a planet or docked (a ship on the tow is cast off there
  and taken up again on the way out).
- **Fighting.** Each ship's guns fire by themselves at what it is fighting (the target given,
  or a hostile that comes close), broadside on at gun range. Enemies go for the fleet's ships as they do for the player's.
- **Losses.** A fleet ship destroyed is lost, and her crew with her.
- **The menu** (Tab) lists each ship: what she is doing, how far off, her hull and her crew (or
  "towed"), and the ships berthed at the yards. B views a ship of the fleet, and back.
- **Saved.** The fleet, and its ships' crews, are saved with the game.
- **One of each class.** A shipyard builds one ship of each class (the five destroyers and the
  two cruisers), so a fleet today is at most eight ships: a frigate and the seven.
- **Not shared in co-op.** The other players don't see a player's fleet.

### Crew and the settlements

Every frigate has a crew, and so has every installation except the automated ones (the Cinder
Orbital Battery's defence platforms, and the two long-range sensor arrays: Ochre Relay and the
Thalassa L1 Observatory). The rules and every place's people are in
`Assets/Carrier/Resources/Data/crew.json`.

| Frigate | Crew |
|---|---|
| Sentinel | 140 |
| Lancer | 90 |
| Warden | 220 |
| Talon | 110 |
| Vanguard | 300 |

- **Losses.** Hull damage kills crew: a full hull's worth of damage kills half of them. Some
  always survive (15%), so a frigate is never left with nobody. Shield hits cost nobody.
  Repairs at a station mend the hull, not the crew, and a frigate destroyed and back comes back
  with the crew it had.
- **Short-handed.** Down to 90% of its complement a frigate works at full strength. Beyond 10%
  lost it works less well, more so the more are lost (down to 30% at worst). Its top speed,
  acceleration, turning, shield and recharge, rate of fire, main-gun charge and mining laser
  all suffer; the hull, the hold and what each shot does don't. The console's CREW bar shows the
  crew and the efficiency.
- **Installations too.** A hostile base loses crew as its guns are hit, and its guns fire less
  often for it. It gets reinforcements slowly. The target panel shows a target's crew.
- **Recruiting.** At the bigger friendly places, the site's screen (services mode: Tab or U, A on the pad) has a CREW
  page: recruit one, ten, or as many as the frigate is short, at 250 cr each. Each place has a
  pool of people willing to sign on, which refills slowly in game time. The pools and the crew
  aboard are saved with the game.

**The settlements.** Ten new places, where people live and work in numbers, and where crew can
be recruited. They are built from the licensed packs in `Assets/Carrier/Kit` (Mega_City,
SpaceColony2086, Volstok_District, Monolith_megastructure, OrbitalBase, Tropical_Scifi_City,
Sci-Fi_Flying_Cargo_Ship, SciFi_Cliff, SilentZonePack, RMG_Crane, Industrial_Pipe_Rack,
Desert_Outpost, Starship) and the project's own models: a habitat ring, radar domes, and a
fortress's walls, towers and gate (`Tools/Blender/build_settlements.py`).

| Place | Kind | Where | Get there |
|---|---|---|---|
| Concord Colony | Colony station: a habitat ring 1.2 km across round its hub | In orbit of Thalassa | Dock |
| Arges Station | Colony station: drum habitats on a spine | In orbit of Jovara's moon Arges | Dock |
| Port Ochre | City: towers up to half a kilometre tall round a spaceport | On Ochre | Land on its pad |
| Pyrois Domes | Domed town | On Jovara's moon Pyrois | Land on its pad |
| Hollow Rock | Asteroid city: towers and domes on a big rock | The Inner Belt | Dock (sets down on its pad) |
| Lantern Arcology | Deep-space habitat: a slab of towers 2.6 km across | Deep space, between Cinder and the Jovara Trojans | Dock |
| Aegis Fleet Base | Military base: hangars, armoured blocks, gun turrets | Thalassa's L4 point | Dock |
| Fort Brontes | Military base: walls and gun towers round a spaceport, hangars, bunkers, radar | On Jovara's moon Brontes | Land on its pad |
| Steropes Heavy Industries | Industrial base: a power plant, refinery, factory, crane yards (also an industrial site) | On Jovara's moon Steropes | Land on its pad |
| Ochre Orbital Works | Industrial base: a factory yard and a refinery (also an industrial site) | In orbit of Ochre | Dock |

Crew can also be recruited at Thalassa Highport, Deepcut Station, Brontes Shipyard, Selene Base,
Ochre Foundry, Mnemos Works and the Kestrel Deep Yard.

### The Vrask

The Vrask are the game's alien enemy: lizard-like raiders who take prisoners, drain their life and
capture ships rather than destroy them. They have four ships, each with its own job, and they fight
as a group in a set order. Their numbers are in `Assets/Carrier/Data/vrask.json`, and every
ship's statistics in `docs/allships.md`.

| Ship | Its job | What it brings |
|---|---|---|
| Snare (capture frigate) | Disable and hold | Ion fire on your shield generator, then your engines; a tractor beam; boarding pods at the ship it holds |
| Fang (assault frigate) | Board | Heavy guns on your weapons, missiles, waves of boarding pods, a prow that clamps to your hull |
| Maw (siege frigate) | Bombard | Spinal artillery to 13 km (shield nullifier, armour penetrator), two nuclear missiles; fires only at what the others see |
| Skink (escort frigate) | Screen, picket, chase | Eight rapid point-defence guns that shoot your missiles down; sees 9 km; the fastest of them |

Shields, armour, ion, tractor beams, boarding and capture, artillery and nuclear missiles are
under Defence and damage; each ship's way of fighting is under Star System's encounters.

**How they fight together.** A group runs its fight in phases (`VraskFleet`), each moving on when
something is so on the ship it is working:

1. **Approach and picket.** The Skinks run up to 2.5 km ahead of the rest as the group's eyes;
   the Snares and the Fang come no nearer than 5.5 km. Contact is made when one of them has you
   within its sensors (a Skink's reach 9 km, the others' 6 km).
2. **Bombard.** The Maw's artillery opens at long range, on what the others see, while the rest
   hold back: for up to 45 s, or until your shield is under half, or you close within 4 km of a
   Snare or the Fang.
3. **Screen and disable.** The Snares go in: ion on your shield generator and engines, then the
   tractor. The Skinks patrol round the Maw and any Snare holding a ship, running down your
   missiles, fighters and drones, and any ship closing on them. The Fang still waits.
4. **Board.** Once you are disabled (engines out, held fast by two beams, held with your shield
   down, or boarders already aboard), the Fang surges in with its pods and its clamp. Get free for
   15 s and it is back to the Snares.
5. **Pursue.** With the ship taken or destroyed, or a light or damaged one running, the Skinks
   chase the cripples in hit-and-run passes; 20 s later the group takes up its next target.
6. **Withdraw.** A group that loses its Maw and both Snares breaks off: the rest go back into
   hiding and don't spring again until the spawns bring the others back.

Their target is a player's ship first (the nearest, in co-op). Without one, it is the ship most
worth taking that is easiest to disable: its hull and shield, against how worn its shield and
engines are, the nearer the better.

**Where they are.** The raiders (two Snares, a Fang, a Maw and two Skinks) lie in wait round Comet
Mirren and spring at 4 km; a warp drops you 8.5 km out, so they don't set on you on arrival.
The siege group (a Maw and two Skinks) holds station 160 km over Port Ochre and shells the city
every two hours (see Strikes from orbit). Its Skinks spot for the Maw and shoot your missiles
down, and with the Maw destroyed they go quiet until it is back.

**Fighting them.**

- Kill the Skinks, or get past them, and the Maw is blind: it fires only at what another Vrask
  ship sees.
- Keep your shield up and your engines whole. One tractor beam can be out-flown at full thrust;
  two, or one with your engines out, can't.
- Knock out a Snare's weapons and its beam lets go. J (Back + L3 on the pad) aims your fire at a
  target's weapons, engines or shield generator.
- Fire missiles from outside the Skinks' patrol (they take any aimed at a Vrask ship): two of
  their needles bring a missile down, as two CIWS bolts do.
- A big crew repels boarders. Boarders who bring the crew down to its survivors take the ship.
- When a shell is on its way, change course: it flies to where you would have been.

**In co-op** the host's game runs the Vrask, their fleet and their tactics, and the others' games
show them. With more players they are tougher and hit a little harder, as every enemy does
(`CoopScaling`): hull and shields ×1.6 with two players and ×2.2 with three, damage ×1.12 and
×1.24. A Skink that shoots down a player's missile in the host's game takes it down in that
player's game too.

There are no new keys for the Vrask. The HUD warns of a tractor lock and of boarders aboard.
Tests: `-vrasktest` (the `vrask` stage), and `VraskDataTests`, `TractorBeamTests`,
`BoardingTests`, `ArtilleryTests` and `VraskFleetTests`.

## Controls

There are too many things to do for a key each, so the game has **modes of play**: combat,
mining, navigation, services and fleet. Flying the ship, the camera and the menus are the same in
every mode; the other keys are the mode's own, so the same key can do different things in
different modes. Every mode is played in real time: the game never stops for one, nor for its
menu (only the pause menu stops it).

- **Changing mode.** 1 to 5 on the keyboard (Back + LB / RB on the pad). The controls box in the
  top-left corner shows the mode, its keys, and the keys every mode shares (H, or Back, hides it).
- **The mode's menu.** Tab (Back + Y) opens a list over the game: the combat mode's targets,
  and in later modes their own lists. Choose with ↑/↓ (D-pad), act with Enter (A) or a
  click, close with Tab or Esc (B). The ship can still be flown while it is open.
- **Modes taken up by themselves.** The system map is navigation while it is open, and closing
  it (or warping from it) goes back to the mode before. Docking at a station or landing on a
  site's pad is services, and leaving it goes back to the mode before.

**In every mode**

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| Throttle / turn | W/S, A/D | Left stick |
| Nose up / down | ↑ / ↓ | D-pad ↑ / ↓ |
| Roll left / right | Q / E | D-pad ← / → |
| Elevator thrusters up / down | Space / Ctrl | RT / LT |
| Level off (nose and roll) | L | Back + A |
| Mode: combat, mining, navigation, services, fleet | 1, 2, 3, 4, 5 | Back + LB / RB (previous / next) |
| The mode's menu | Tab | Back + Y |
| System map (opens in navigation mode) | M | R3 |
| Land / take off, dock / undock | K | Back + B |
| Orbit camera / zoom | Right-drag / mouse wheel | Right stick / Back + D-pad ← → |
| Help / text size | H / + − | Back / Back + D-pad ↑↓ |
| Sound on/off | N | (pause menu) |
| Next song (the music on or off, and the song before: the pause menu) | Shift+N | (pause menu) |
| Pause menu: resume, restart, sound, co-op, the HUD, quit | Esc or P | Start |
| Window / full screen | F11 | |

**Combat mode (1)**

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| Fire side guns | F | RB |
| Missile salvo at the target (destroyers, the cruiser) | R | LB |
| Auto-fire on/off | G | L3 |
| Charge main gun (again to cancel) | B | X |
| Next / previous target, or click a ship (the hostiles, then the friendly stations and installations within 30 km: those to fly to, never fired on) | T / Shift+T | Y |
| Stop targeting | X | B |
| Aim at the target's weapons, engines or shield generator, in turn, and back to its hull (a new target starts at the hull) | J | Back + L3 |
| Line up on the target, until the guns bear (again, or steer, to stop) | C | A |
| Fly to the target, to its guns' reach, and line up on it there (again, steer or the throttle to stop) | U | |
| Kinetic strike from orbit | Y | |
| Salvage the chosen wreck, or the nearest in reach; one out of reach is flown to, and salvaged there (again to stop) | Z | Back + X |
| Choose the next wreck, nearest first | Shift+Z | |
| Menu: the targets in range and the wrecks near, nearest first (Enter targets one or chooses a wreck, C lines up or flies to the wreck, U flies to it, Z salvages) | Tab | Back + Y |

**Mining mode (2)**

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| Mining laser on/off | Z | A |
| Mineral scan | V | Y |
| Choose the next / previous deposit found, nearest first | T / Shift+T | |
| Fly to the chosen deposit (or the nearest found), until the laser reaches it (again, steer or move the throttle to stop) | C | X |
| Cargo hold list / jettison the cheapest ore | I / J | RB / B |
| Menu: the 20 nearest deposits found, then the ore aboard (Enter chooses a deposit for the laser or ejects that ore; C flies to the deposit; Z chooses it and cuts it, or, out of reach, chooses it to fly to; J ejects the ore) | Tab | Back + Y |

**Navigation mode (3)**

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| System map: warp to a place | M | R3 |
| Menu: the points of interest here (Enter flies there; K flies there and lands or docks; C sets the course, marked on screen) | Tab | Back + Y (A flies there, X and lands or docks) |
| Fly to the course set / and land or dock there | Tab, then Enter / Tab, then K (the menu opens on it) | A / LB |
| Stop the auto-travel (or just steer) | X | B |
| Cruise on/off | O | X |
| On the map: choose a place / warp / close | Click, arrows or Tab / WARP, double-click or Enter / M or Esc | Left stick, D-pad or LB/RB / A / B |
| On the map: a mini-warp to another player or a wingman | Click their ship, then WARP (or double-click it) | |

**Services mode (4)**

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| The services screen (docked, or on its pad; it opens by itself on arriving): storage, refining, building, fitting, trade, crew, the shipyard, as the place offers | Tab or U | A |
| In the site's screen: pages / choose / load, store / act / cancel a job / close | Tab or Q E / ↑↓ / ← → (Shift for all) / Enter / Backspace / U or Esc | LB RB / D-pad ↑↓ / D-pad ← →, X, Y / A / Y / B |

**Fleet mode (5)**: the player's other ships (see [Fleets](#fleets)). Orders go to the ship chosen in the menu, or all of them.

| Action | Keyboard / mouse | Xbox 360 pad |
|---|---|---|
| Follow me / guard me (protect) / form up on me | F / G / R | A / X / Y |
| Attack my target / hold position | V / Z | RB / B |
| View the next ship of the fleet (and back to mine) | B | LB |
| Menu: the fleet, and the ships berthed at the yards (Enter: orders go to that ship, or all) | Tab | Back + Y |

In combat mode, V sends the fleet after the target and Shift+V forms it up.

**The captain** is chosen on the start menu, and stays in command for the game.

In the web version, use the page's Full screen button rather than F11.

## Project layout

- `Assets/Carrier/Scripts`: game code: ship control, turrets, CIWS, main beam, shield, health,
  breakup, enemy weapons and sound synthesis. Shots and explosions are reused rather than made
  new each time (`Pool`); the warp drive's rules and its look are separate (`WarpDrive`,
  `WarpEffects`); how each kind of hostile flies is in one table (`PilotPresets`)
- `Assets/Carrier/Resources/Data/ground_units.json`: the planets' ground forces' numbers (hull,
  weapons, names, and how their fighters and gunships fly), read as the game runs
- `Assets/Carrier/Data/star_system.json`: the star system's planets, moons and asteroid fields
  (where they are, their size, look, mass, map colour and description), read by the scene
  builder; rebuild the scenes after changing it
- `Assets/Carrier/Scripts/Demo`: input and gamepad support (`CarrierDemoInput`), the HUD
  (`ShipHud`, the ship's console and the captain), the pause menu and system map, camera,
  autopilot, landing and docking. Gameplay code doesn't call the HUD: it posts messages to
  `Notices` and adds what can be targeted to `Targets`, and the HUD reads those
- `Assets/Carrier/Harness`: the self-tests, the autotest and the demo reel recorder, in their own
  assembly (`Carrier.Harness`) so the game can't come to depend on them
- `Assets/Carrier/Resources/Captains`: the five captains' portraits, 26 each
  (`<captain>_neutral.jpg` and `<captain>_<expression>_<step>.jpg`), generated as described
  under The captain
- `Assets/Carrier/Shaders`: the shield bubble and planet atmosphere shaders (URP, additive)
- `Assets/Carrier/Editor`: `CarrierBuilder` generates the materials, prefabs, breakup chunks and
  scene; `CarrierPlayerBuild` builds the Windows, web and Linux players; `CarrierCommandWatcher` runs builds from
  a trigger file
- `Assets/Carrier/Models`: custom models (frigate hull, ball turret, CIWS, beam emitter, carrier
  parts, drone, missile)
- `Assets/WebGLTemplates/StarSystem`: the web page (full-window canvas, loader, Full screen
  button) and `xbox360.js`; `Assets/Carrier/Plugins/WebGL` holds the bridge between the game and
  the page
- `Assets/Carrier/Scripts/Net`: co-op: the connection (`CoopClient`, over a WebSocket in both
  the Windows and web versions), the shared world (`CoopWorld`), other players' ships
  (`RemotePlayer`) and the co-op panel (`CoopMenu`)
- `Server/CoopServer`: the co-op server (.NET 8, Windows and Linux), the program around
  `Server/CoopServer.Core`, which holds the rooms and the settings; `Server/CoopServer.Tests` its
  unit and integration tests, which use Core directly
- `Assets/Carrier/Tests`: the game's unit and integration tests (Unity Test Framework). The game's
  code is its own assembly, `Carrier.Game` (`Assets/Carrier/Scripts`), and the editor tools are
  `Carrier.Editor`, so the tests can reference them
- `Tools/Tests`: `run_tests.py`, which runs every test, and the regression baselines
- `Tools/CoopTest`: the co-op server test, the scripted second player and the reel's wingmen
- `Tools/WebTest`: the headless Chrome gamepad test and its stand-in controller
- `Tools/Blender`: the scripts that make those models (frigate conversion from GLB, ball turret,
  CIWS, beam emitter, carrier parts, the refineries, factories and landing pad) and preview
  renders, and the pictures of the refined materials and ship parts (`build_goods.py`)
- `Tools/Textures`: the script that makes the industrial sites' textures
- `Assets/Carrier/Scripts/Industry`: the industry: its data, the sites' storage and production
  lines (`IndustryLedger`), the parts fitted to a frigate (`ShipFittings`) and the save
- `Assets/Carrier/Scripts/World`: the world's clock and memory (`WorldState`), the places
  loaded only when near (`StreamedVisual`), positions in doubles (`Double3`), far bodies drawn
  nearer (`ScaledSpace`), and the planets' ground seen from space (`PlanetTerrain`,
  `PlanetMaps`)

## Building

1. Open the project in Unity 6000.6.
2. Run **Tools > Carrier > Build Everything** to generate the materials, prefabs and scene.
3. Run one of these:
   - **Tools > Carrier > Build Star System** for `../Builds/StarSystem/StarSystem.exe`;
   - **Tools > Carrier > Build Star System (Web)** for `../Builds/StarSystemWeb/`;
   - **Tools > Carrier > Build Star System (Linux)** for `../Builds/StarSystemLinux/StarSystem.x86_64`
     (it needs Unity's Linux Build Support (Mono) module): chiefly to run an AI wingman on a
     Linux server, beside the co-op server.

   The deprecated games are under **Tools > Carrier > Deprecated**, for
   `../Builds/CarrierDemo/CarrierDemo.exe` and `../Builds/AsteroidAmbush/AsteroidAmbush.exe`.

The Windows players use DirectX 11, which is the stable choice on hybrid AMD + NVIDIA laptops.

## The web version

The web build of Star System uses WebGPU where the browser supports it and falls back to
WebGL 2 elsewhere. The badge in the corner of the page shows which one is in use.

- **Hosting.** Upload `index.html`, `xbox360.js` and the `Build` folder, keeping the folder
  layout. Serve them over https, because browsers allow WebGPU only on secure pages (or on
  localhost). No special server settings are needed, as the game can unpack its own compressed
  files. Loading is faster if the server sends the `.unityweb` files with the header
  `Content-Encoding: br`.
- **Setting it up on a web server**, step by step:
  1. Unzip `battlecruiser-4000-bc-star-system-web.zip` (the release's) or build it
     (**Tools > Carrier > Build Star System (Web)**, into `../Builds/StarSystemWeb/`). Co-op
     with API keys (step 5) needs a build from after 14 Nov 2026: the next release, or one built
     from source.
  2. Copy what is in its folder (`index.html`, `xbox360.js`, `Build/`) to the web server's
     folder for the game, keeping the layout.
  3. Serve it over **https** (WebGPU needs a secure page; plain http falls back to WebGL 2, and
     localhost counts as secure). Any static web server will do: nginx, Apache, IIS, GitHub
     Pages, a CDN.
  4. Optional, for faster loading: the `Content-Encoding: br` header above (in nginx:
     `location ~ \.unityweb$ { add_header Content-Encoding br; default_type application/octet-stream; }`).
  5. For co-op from the page: run a co-op server (see [Running the server](#running-the-server))
     with a certificate, so the page can reach it over `wss://`, and give each player an API
     key. A link like `https://<site>/?server=<host>:<port>&name=<name>&apikey=<key>&connect`
     opens the game straight into it. Without `apikey` in the link, the first visit is turned
     away for having no key: the player types it into the co-op panel (the pause menu, Co-op)
     once, and it is kept, for that name, in that browser.
- **Trying it locally.** From the build folder, run `python -m http.server 8080` and open
  `http://localhost:8080`.
- **Gamepads.** Chrome and Edge on Windows report an Xbox 360 pad in the standard layout.
  Firefox on Linux and the macOS 360Controller driver report the raw layout instead, and
  `xbox360.js` translates it so the game still sees a normal gamepad. Rumble works through the
  browser.
- **Quitting.** Quit in the pause menu closes the game, and the page offers Play again.

## Co-op

Up to seven players can fly together in Star System, on Windows or in the web version (and in
the two deprecated games), through a small server that any of them (or a machine elsewhere) runs.

### Joining a game

When the game starts it asks whether to play alone or join a co-op game, with your name, the
server's address and port, and your **API key**. The same panel is under **Co-op** in the pause
menu. While you're connected, a panel at the top of the screen lists the players, who is
hosting, and the round trip to the server.

- **The API key.** A server lets in only players with one of its keys, unless its owner has
  opened it to everyone. Ask the owner for a key (see [Running the server](#running-the-server))
  and paste it into the panel. It shows only its first characters there.
- **Kept for next time.** The Windows version keeps one key with the rest of the panel. The web
  version keeps a key for each player name, in that browser, so people sharing a browser each
  keep their own: typing another name brings up that name's key.
- **Turned away.** Without a key, or with one the server doesn't know (or has revoked), the panel
  says so.

To skip the question, start the game with `-connect <address>:<port> [-name <name>] [-apikey
<key>]`, or open the web version with `?server=<address>:<port>&name=<name>&connect`. A key given
with `-apikey` (or `&apikey=<key>` in the page address, for automated tests) is used but not
kept.

### What is shared

- **Ships.** Each game shows the other players' ships, smoothed, with their names, each as the
  frigate its player flies. A ship disappears while its player is in warp, on a planet or
  destroyed.
- **Enemies.** Each level has a host: the first player there. The host's game runs the enemies,
  and every other game shows copies that follow them. A hit on a copy goes to the host's game,
  which applies it, so a kill counts for everyone. Enemies go for whichever player is nearest,
  and their hits land on that player's ship.
- **Fire.** Shots, missiles and main-gun beams appear in every game.
- **Tractor beams.** The host's game tells a player's game when its enemies' beams hold that
  player's ship, and that game pulls its own ship; a hold not renewed for 1.5 s lets go. Every
  game shows the beams.
- **Boarding.** A player's crew is in their own game. When the host's pods (or a Fang's ram)
  get aboard another player's ship, the host tells that player's game, which fights the boarders
  and decides whether the ship is taken. Every ship's updates say how many boarders are aboard
  and whether it was captured, so the other games show no explosion. A missile or pod shot down
  by a game's own ships goes down in the others too.
- **Hand-over.** When the host leaves, the next player takes the enemies over where they are.
- **More players, harder enemies.** Every encounter in space is scaled for the number of
  players flying in the level. This covers raiders, ambushes, capital ships and station guns.
  Players who have landed on a planet don't count until they take off again. Each
  player after the first adds 60% to every enemy's hull and shields (shields also recharge
  faster), and 12% to the damage of its guns, missiles and beams. With seven players, enemies
  are 4.6 times as tough and hit 1.72 times as hard. When players join or leave, enemies keep
  how damaged they are. The players panel shows the current scaling, and a message announces
  each change. Fights on a planet's surface are each player's own, so they aren't scaled.
- **The Star System.** Every game runs the orbits on the same clock, so the planets, stations
  and everything round them are in the same places for everyone.
- **Not shared.** Fights down on a planet's surface stay in each player's own game. The pause
  menu doesn't pause the game while you're connected.

### Running the server

Ready-built servers for Windows and Linux are on the
[14 Nov release](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/star-system-2026-11-14): `battlecruiser-4000-bc-coop-server-windows.zip`
and `battlecruiser-4000-bc-coop-server-linux.tar.gz`. The 3 Nov release's servers speak co-op
protocol 1 and can't host the current game.

The server is in `Server/CoopServer` (.NET 8). It runs on Windows and Linux and needs nothing
else installed when built as below.

```
CoopServer [--port 7777] [--bind 0.0.0.0] [--max-players 7]
           [--allow 192.168.1.0/24,10.0.0.5] [--config coopserver.json]
           [--cert server.pfx --cert-password secret] [--keys coopkeys.json] [--open]
CoopServer --new-key <name> [--ai]      make an API key, then exit
CoopServer --list-keys                  list the keys made, then exit
CoopServer --revoke-key <name|key>      revoke a key, then exit
```

- **Settings.** They are in `coopserver.json` next to the program, and anything given on the
  command line wins over the file. `CoopServer --help` lists them all.
- **Allow-list.** `allow` (or `--allow`) limits who may connect, by single IPs or CIDR ranges,
  IPv4 or IPv6. Anyone else is refused with HTTP 403. An empty list lets anyone in.
- **Players.** `maxPlayers` (or `--max-players`) is how many can be on at once: seven by
  default. A `coopserver.json` kept from an older copy keeps its own number (4 in those).
- **API keys.** Every player, and every AI player, needs one of the server's keys to join.
  Without one, the server turns them away.
  - Make a key with `CoopServer --new-key Alice` (add `--ai` for an AI player), or type
    `newkey Alice` (`newaikey Wingman`) in the console of a running server. The key is printed
    once: give it to the player, who pastes it into the game's co-op panel. An AI player is
    started with `-apikey <key>`.
  - `--list-keys` (`keys` in the console) lists them, and `--revoke-key Alice` (`revoke Alice`)
    revokes one, by its name, the whole key, or its first characters (as many as the list shows,
    or more; a start that more than one key shares revokes none). A key made or revoked counts at
    once, even while the server runs: within a few seconds the players using a revoked key are
    told and disconnected.
  - The keys are kept in `coopkeys.json` next to the settings (`keysFile`; `--keys <file>` on the
    command line is from the folder it is run in). Only each key's hash is stored, never the key
    itself. The file is changed under a lock, so a key made or revoked beside a running server
    can't be undone by it. If the file can't be read (it was hand-edited and broken), no one gets
    in and no key can be made or revoked until it is mended, so the keys in it aren't lost.
  - The status page shows who is on, but not whose key each came with (anyone the allow-list
    lets in can read it); the console's `status` does.
  - A new server has no keys, so no one can join until one is made: its console says so.
  - `"requireKey": false` (or `--open`) lets in anyone the allow-list does, without a key.
- **Live changes.** Saved changes to the file's `allow`, `maxPlayers` and `requireKey` take
  effect at once, without a restart.
- **Console.** While the server runs, type `status` for who is on (and whose key each player
  joined with), `newkey <name>`, `newaikey <name>`, `keys`, `revoke <name|key>`, `reload` to
  re-read the settings, or `quit`.
- **Status page.** `http://<server>:<port>/` shows the same status to allowed addresses.
- **Web players.** A web page served over https can only connect to `wss://`, so give the
  server a certificate (`--cert`), or put it behind a proxy that has one. A page served over
  plain http, or from localhost, can use `ws://`. When the page comes from the co-op server's
  own host and the port it was given doesn't answer over `wss://` (the plain 7777, say), the
  game tries the page's own address next, `wss://<host>/coop`, so a proxy there (nginx's
  `location /coop { proxy_pass http://127.0.0.1:7777/coop; ... }`, with the WebSocket upgrade
  headers) works with the default port. Behind such a proxy every player connects from the
  proxy's own address (127.0.0.1), so an `allow` list can't tell them apart: leave loopback
  out of it, or rely on the API keys.
- **Firewall.** Open the port (TCP) on the server's firewall for players on other machines.

#### Setting up a standalone server on Windows

The API keys these steps use came after the 14 Nov 2026 release: use the next release's server,
or one built from source (see [Building the server](#building-the-server)).

1. Unzip `battlecruiser-4000-bc-coop-server-windows.zip` somewhere it can stay, say
   `C:\CoopServer`. Nothing else needs installing.
2. Edit `coopserver.json` next to `CoopServer.exe` if needed: the port, the player limit, an
   allow-list, a certificate for `wss://` (give its full path, e.g. `C:\CoopServer\server.pfx`).
3. Make a key for each player and AI player, in a command prompt there:
   `CoopServer.exe --new-key Alice` (`--ai` for an AI player). Send each their key.
4. Let players in through the firewall (as administrator):
   `netsh advfirewall firewall add rule name="Battlecruiser co-op" dir=in action=allow protocol=TCP localport=7777`.
5. Start it: `CoopServer.exe`. Its console shows who joins and takes the commands under
   **Console** above.
6. To start it with Windows, without anyone logged in, in PowerShell as administrator (in its
   own folder, with no time limit, started again if it stops):

   ```
   $a = New-ScheduledTaskAction -Execute "C:\CoopServer\CoopServer.exe" -WorkingDirectory "C:\CoopServer"
   $t = New-ScheduledTaskTrigger -AtStartup
   $s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
   Register-ScheduledTask -TaskName "Battlecruiser co-op" -Action $a -Trigger $t -Settings $s -User SYSTEM -RunLevel Highest
   ```

   Run that way it has no console: its status page (`http://localhost:7777/`) shows who is on.
   Keys can still be made beside it while it runs (`CoopServer.exe --new-key Bob`), and count
   at once.

#### Setting up a standalone server on Linux

As on Windows, the API keys need the next release's server, or one built from source.

1. Unpack it: `sudo tar xzf battlecruiser-4000-bc-coop-server-linux.tar.gz -C /opt` (it makes
   `/opt/CoopServer`), and `sudo chmod +x /opt/CoopServer/CoopServer` if needed. It is a single
   self-contained program: no .NET to install.
2. A user for it to run as, owning its folder:
   `sudo useradd -r coopserver && sudo chown -R coopserver /opt/CoopServer`.
3. Edit `/opt/CoopServer/coopserver.json` if needed (`sudo -u coopserver nano ...`).
4. Make the keys, as that user (so the keys file stays theirs):
   `sudo -u coopserver /opt/CoopServer/CoopServer --new-key Alice` (`--ai` for an AI player),
   and `--list-keys` / `--revoke-key` later.
5. Open the port: `sudo ufw allow 7777/tcp` (or your firewall's way).
6. Run it as a service, `/etc/systemd/system/coopserver.service`:

   ```
   [Unit]
   Description=Battlecruiser 4000 BC co-op server
   After=network-online.target

   [Service]
   ExecStart=/opt/CoopServer/CoopServer
   WorkingDirectory=/opt/CoopServer
   User=coopserver
   Restart=on-failure

   [Install]
   WantedBy=multi-user.target
   ```

   then `sudo systemctl enable --now coopserver`. `journalctl -u coopserver -f` follows its
   log. Keys made while it runs (step 4) count at once.
7. For web players on an https page, either:
   - a certificate for the server itself (`certificate` in the settings, its full path); or
   - a reverse proxy with one in front (nginx with `proxy_pass` and the WebSocket upgrade
     headers) forwarding to the server. Then have the server listen on this machine only
     (`"bind": "127.0.0.1"`), don't open its port (skip step 5; open the proxy's 443 instead),
     and leave the allow-list empty: behind a proxy every player seems to come from the proxy's
     address, so it can't tell them apart. The API keys still keep out anyone without one.

#### Building the server

To build it, install the .NET 8 SDK and run this from `Server/CoopServer`:

```
dotnet publish -c Release -r win-x64   -o ../../../Builds/CoopServer/win-x64
dotnet publish -c Release -r linux-x64 -o ../../../Builds/CoopServer/linux-x64
```

Each build is a single self-contained file (about 47 MB) plus `coopserver.json`. On Linux,
`chmod +x CoopServer` first.

### Co-op comms and AI wingman

**Comms.** In a co-op game, press **Enter** to open a line at the foot of the screen, beside the ship's console. Type a message, then
press **Enter** to send it to everyone in your level, or **Esc** to drop it. The last few
messages show under it and fade after about 20 seconds. Each message from someone else also
goes to the captain's notices. The server passes chat on like any other game message (type
`chat`, field `text`, 240 characters at most), so it needs no change.

**The AI wingman.** Any copy of the Star System game can join as an AI player. Start it with
`-coopbot`:

```
StarSystem.exe -coopbot -connect <address>:<port> -name Wingman [-frigate Lancer] [-botport 7781] [-batchmode -nographics]
```

- **On a Linux server**, beside the co-op server: unpack the Linux build (say to
  `/opt/StarSystemBot`), `chmod +x StarSystem.x86_64` (a build made on Windows loses the flag),
  and run `StarSystem.x86_64 -batchmode -nographics -coopbot -connect 127.0.0.1:7777 -apikey
  <an AI key> -name Wingman` as a service (its own user, `HOME` set to a folder it can write,
  `Restart=always`, after the co-op server). It uses about half a CPU core and 340 MB. The sound
  card errors in its log, on a server without one, do no harm.
- It needs no window. It flies the starting frigate unless given another, and never touches
  your save.
- It greets the players, then forms up on the first one to join. If that player warps far
  away, it warps after them.
- It fights any enemy that comes within 6 km, then goes back to its order.
- Its pilot flies through the same controls a player uses: the autopilot inputs, the throttle,
  the target, the guns, the bow beam, missiles, the warp drive and docking.

To give it orders, start a message with its name (`Wingman, ...` or `@Wingman ...`). `all
ships`, `everyone` or `wingmen` reaches every bot. It answers each order on the channel.

| Order | What it does |
| --- | --- |
| `follow me` or `follow <name>` | Forms up on that player. `escort` and `form up on` do the same. |
| `regroup` | Forms up on whoever said it. |
| `hold` | Stops where it is. |
| `attack [target]` | Fights that target, or the nearest enemy. |
| `warp to <place>` | Warps there, then holds. |
| `dock` | Docks at the station it is at. |
| `mine` or `mine at <field>` | Mines asteroids: warps to the field (the one it is at, or the nearest), scans, flies to the nearest deposit and cuts it until the hold is full, then warps to the nearest market (a station or base that buys ore), docks, sells the ore and goes back for more, over and over. `start mining` and `mine asteroids` do the same. |
| `sell` | Takes the ore aboard to the nearest market now, sells it, then goes back to mining. |
| `cargo` | Reports what is in its hold (ore by mineral, goods, parts), how full the hold is, and its credits. `what's in your hold?`, `how full is your hold?`, `inventory` and `manifest` do the same. |
| `give [N] credits [to <name>]` | Transfers credits from its hold to that player (whoever asked, if no name is given): N (`give me 500 credits`, `transfer 2k credits to Ann`, `give Ann 500 credits`, `pay me 300`), half (`give me half your credits`) or all (`give me all your credits`). Without an amount it asks how many. The player's game adds them to the ship's hold and says so. Credits go only to human players in the same level, and only from a player the server let in with an AI key (on an open server, one that says it is a bot). They come back to the wingman if the player's game doesn't confirm them within 10 s, and a game that only sees them after 6 s refuses them. |
| `status` | Reports its hull, shield, missiles, hold, credits, what it is doing and where it is. |
| `help` | Lists these orders. |

It also says when it starts a fight, when the target is destroyed, when it arrives, when it sells a
load (how much, where, and for how much), and when it can't do what it was told.

A message to it that isn't an order (`Wingman, how's it going?`) is talk, and the bot leaves talk
for whoever is listening on the channel through its API (see **Talking to the wingman** below).
When nothing has listened for 90 s, it answers talk with its list of orders, at most once every
30 s.

**An AI at the controls.** The bot game answers a small HTTP API on this machine only
(`127.0.0.1`, port `-botport`, 7781 by default):

- `GET /status`: its ship, its order and what it is doing, what is near it, and the players.
- `GET /messages?after=N[&wait=S][&listener=1]`: the comms channel after message N, marked by who each
  message was to. With `wait`, if nothing new has been said, the answer waits up to S seconds (60
  at most) for the next message. `listener=1` tells the bot a listener is answering talk.
- `GET /cargo`: what is in the hold, item by item, how full it is, and the credits.
- `GET /destinations`: the places to warp to, nearest first.
- `POST /say {"text"}`: sends a message on the channel.
- `POST /order {"order","target"}`: gives an order (`give` takes the target `<N|all|half>[ to <name>]`).
- `POST /quit`: closes the game.

`Tools/CoopAI/coop_ai_mcp.py` turns that API into an MCP server over stdio. It needs no
packages. Its tools are `status`, `read_messages`, `wait_messages` (waits for the next message),
`cargo`, `give_credits`, `say`, `order`, `destinations`, `launch_bot` (starts the bot game headless
and connects it) and `stop_bot`. Claude or any MCP client can then fly the wingman and talk to
the players:

```
claude mcp add coop-ai -- python "<repo>/Tools/CoopAI/coop_ai_mcp.py"
```

The environment variables `COOP_BOT_API` and `COOP_GAME_EXE` point it at another port or
build. See `Tools/CoopAI/README.md`.

**Talking to the wingman.** Players can chat with the wingman, and Claude answers in its voice
through Claude Code:

1. The bot game's API listens on the server's `127.0.0.1` only. Tunnel it to this machine:
   `ssh -N -L 7781:127.0.0.1:7781 root@<server>` (leave it running).
2. Add the MCP server, which then reaches the bot through the tunnel:
   `claude mcp add coop-ai -- python "<repo>/Tools/CoopAI/coop_ai_mcp.py"`.
3. In a Claude Code session, run `python Tools/CoopAI/listen.py` as a background monitor. It
   prints a line for each message addressed to the wingman (`--all` for every player's message), and
   each line wakes the session. Claude reads it, checks `status` or `cargo` if the answer needs
   facts, and answers with `say`. It can also act on what it's asked with `order` or
   `give_credits`. While a listener is reading the channel, the bot leaves talk to it.
4. `Tools/CoopAI/WINGMAN.md` is the voice brief: short radio lines, facts only from its
   instruments, quiet in a fight.

This works only while the session is open. A reply takes a few seconds, and every message costs
tokens.

**Tests.**
- `Tools/Tests/test_coop_ai_mcp.py` (in the tools stage) tests the MCP server without the game:
  its JSON-RPC, and its tools against a stand-in for the API.
- The test runner's `aibot` stage starts a server and the bot game headless. A test player
  (`Tools/CoopAI/aibot_test.py`) then checks:
  - that the bot greets it and follows it;
  - its orders by name, the API, and the MCP server against the live bot;
  - that told to mine it mines a field, and told to sell it docks at a market, sells the ore
    and goes back to mining;
  - that it reports its cargo, and that `give` moves its credits to the test player (with the
    receipt) and refuses more than it has;
  - that it quits when told.

### Testing co-op

The protocol's version and message names are in one file,
`Assets/Carrier/Scripts/Net/CoopWire.cs`. The game builds it, the server builds the same file
(in `Server/CoopServer.Core`), and the test bots read the version from it. `Server/protocol` holds
a sample of each message the server sends. The server's tests check that it sends nothing that
isn't in the samples, and the game's tests check that it reads every field in them. A field added
on one side but not the other fails a test. Adding fields or messages keeps the protocol version; renaming one never happens,
because released games and servers must keep understanding each other.

Protocol 2 came with the real-size system: a position (a ship, an enemy, a shot, a hit) is sent
from the reach it is in (`rf`: a planet's, moon's, station's or field's, which go round at
several km/s millions of kilometres out, each game drawing them where its own clock has them),
or from the star in open space, in doubles. Games and servers of protocol 1 can't play with it,
so servers need rebuilding (above).

- The Python bots and test players join with the API key in `COOP_KEY` (an environment
  variable). The test suite makes a key for each one with the server's `--new-key`, so every
  co-op stage goes through the key check.
- `Tools/CoopTest/server_test.py <host> <port> [--key <key>] [--allowtest <settings file>]
  [--keytest <server program> <keys file>]` tests a running server:
  - as many players as the server takes (seven by default; it needs to take at least four)
    get in, and one more is turned away;
  - messages reach the right players, and the server's own messages can't be forged;
  - the host hands over when it leaves, and the clock and rooms work;
  - a game speaking another protocol is refused, and the status page answers;
  - with `--allowtest`, addresses off the allow-list are refused, and a saved change lets
    them in;
  - with `--keytest`, a player with no key or an unknown one is turned away; a key made with
    `--new-key` while the server runs gets in at once, and once revoked doesn't; and the keys
    file keeps no key itself.
- `StarSystem.exe -systemtest <dir> -cooptest -connect <address>:<port> -apikey <key>` plays co-op against
  `Tools/CoopTest/bot.py`, a scripted second player. Start the bot first, so it hosts.
  - First the game is a guest. It checks that the bot's ship shows where it is, that an enemy
    goes where the bot puts it, and that the bot's hits and shots arrive. It also checks that
    its own hits on the enemy reach the bot.
  - Then the bot leaves and the game takes the enemies over. The bot comes back as a guest,
    receives the enemies, and its hits land on the real ones.
  - At Corsair Haven the station's guns go for the bot's ship, and the hits reach it.
  - With the bot in the level, it checks that the enemies are scaled for two players: their
    hull is tougher, and an enemy's shot fires at the stronger damage.
  - Last, it leaves, checking the game carries on alone and the enemies are back to normal.
  - `-coopquick` skips the Corsair Haven part. On the web, use
    `?systemtest&cooptest&server=<address>:<port>&connect`.
- `Tools/WebTest/cooptest.sh [build dir]` runs the quick version against the web build in
  headless Chrome, with its own server and bot. It renders in software (WebGL 2), so the GPU
  isn't used; the web version connects the same way under WebGPU.
- These tools need Python's `websockets` package.

## Tests

`python Tools/Tests/run_tests.py` runs all of them, one after another, and exits 1 if anything
fails. Add `--build` to build the games first, or `--quick` for the quicker half (about 15
minutes; the whole run takes about an hour). The results go to `../Builds/test_run`. The run
needs:

- the .NET 8 SDK;
- Unity 6000.6, with the editor closed;
- Python 3.8 or later, with the `websockets` package;
- Chrome and Git Bash, for the web stage.

The games run one at a time, in a 1280x720 window.

- **Unit tests.**
  - The game (`Assets/Carrier/Tests/EditMode`): the co-op enemy scaling, the co-op messages
    and the server address, minerals and the cargo hold, Kepler orbits and the Lagrange points,
    comet tails, noise, the captain's portraits and lines, the data files, and the frigates
    (every one with its modelled turrets, a CIWS mount or more and one main gun).
  - The server (`Server/CoopServer.Tests`): the allow-list, and reading and reloading the
    settings.
  - The run's own regression checker (`Tools/Tests/test_regression.py`).
- **Integration tests.**
  - The game's Play mode tests (`Assets/Carrier/Tests/PlayMode`):
    - hull and shield together: damage, death and respawn;
    - co-op scaling and the host's control of the hull;
    - shots flying through the physics world to land on a shield or a hull, missing, sparing
      their own ship and the enemies' own side.
  - The server's hub, driven over real WebSocket connections: joining, the seven-player
    limit, rooms and hosts, passing messages on and what it refuses.
  - The server program end to end (`Tools/CoopTest/server_test.py`).
  - The games' own self-tests (see Command-line modes): the HUD and captain, co-op against
    the scripted bot, the web build's co-op, and the full, landing, orbit and station tests.
- **Regression tests.** Each report is compared with its baseline in `Tools/Tests/baselines`:
  - **Self-test reports:** every check must pass, and no check or verdict in the baseline may
    disappear.
  - **Autotest reports (the deprecated Carrier Demo and Asteroid Ambush):** these have no verdict of their
    own. Every line must keep its shape (with the numbers masked, since combat varies), and a
    set of facts must hold, for example:
    - no shot misses its aim or hits the frigate;
    - the frigate comes back whole after it is destroyed;
    - the main gun fires after its 10 s charge;
    - every sound is there.
  - **Unit tests:** their number may not drop.

  After a deliberate change, `--bless` saves the run's reports as the new baselines. It lists
  how they differ from the old ones first.

`--only` and `--skip` pick the stages:

| Stage | What it runs |
|---|---|
| `tools` | the regression checker's tests |
| `server` | the server's tests |
| `unity` | the game's Edit mode and Play mode tests |
| `build` | the builds (with `--build`) |
| `serverproc` | the server program, end to end |
| `autotest` | the deprecated Carrier Demo and Asteroid Ambush autotests: run only when named (`--only autotest --build`) |
| `hud`, `modes`, `nav`, `services`, `fleet`, `ships`, `water`, `industry`, `crew`, `strike`, `vrask`, `coop`, `system`, `land`, `orbit`, `station` | the Star System self-tests |
| `web` | the web build's co-op test |

`--quick` runs `tools`, `server`, `unity`, `serverproc`, `autotest`, `hud` and `coop`.

Each part can also be run alone:

- `dotnet test Server/CoopServer.Tests` runs the server's tests.
- Unity runs the game's tests in its Test Runner window (Window > General > Test Runner), or
  in batch mode:

  ```
  Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml
  ```

## Command-line modes

- `-frigate <id>` (or `?frigate=<id>` on the web), with any game: fly that frigate (Sentinel,
  Lancer, Warden, Talon or Vanguard) without the start menu. A destroyer or a cruiser is flown
  only if the save has it built and in command; otherwise the game starts in the Sentinel. The
  self-tests fly the Sentinel unless told otherwise, and may be given any ship.
- `-industrysave <file>`: save the game there instead of the player's data folder. Automated
  runs (tests, reels) save only where they are told to, so they never touch the player's save.
  `-newgame` plays without the save: it neither reads it nor writes over it.
- `CarrierDemo.exe -autotest <dir>` (deprecated game): runs the scripted self-test and writes a report and screenshots
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
  - mining an asteroid, a comet and a vein on a planet (see `-minetest` below);
  - a capital ship battle, checking that both sides land hits.

  Add `-shiptest -frigate <id>` (`?shiptest&frigate=<id>` on the web) to test only the frigate:
  that the level put it in and pointed everything at it, that it has its turrets, a CIWS mount
  or more and one main gun, that its guns fire on a target ahead, to each side, above and astern
  (never off target or through the hull), that its main gun hits, and the start menu; with
  pictures of the ship and the menu.

  Add `-watertest` (or `?watertest` on the web) to test only the sea, at Pelagia: rolling and
  levelling off, splashing down (with spray and foam), sailing (with a wake), diving (the water
  churning), the underwater view and speed, the surface hiding the ship, surfacing, lifting off
  the water and launching from under it, the sea bases and submarines, their torpedoes (no
  flame, bubbles) and the ship's guns hitting a base under the water.

  Add `-crewtest` (or `?crewtest` on the web) to test only the crew and the settlements: that
  the pause menu offers no other frigate, that hull damage costs crew and more than 10% lost
  slows the frigate (top speed, turning, shield, guns) while repairs don't bring them back, that
  every installation has its crew but the automated ones and a hostile base's guns slow as it
  loses them, and at each of the ten settlements: arriving, docking or landing on its pad, and
  recruiting ten crew for credits, with pictures of each.

  Add `-streamtest` (or `?streamtest` on the web) to test only the streaming and the world's
  memory: that the heavy places start unloaded, that the Lantern Arcology's looks are loaded on
  arriving and let go (and the texture memory with them) after warping far away, that a ground
  unit destroyed on Cinder is still gone on landing again, that the save holds the world, and
  that once its rebuild time has passed, with nobody there, landing again finds it rebuilt.

  Add `-industrytest` (or `?industrytest` on the web) to test only the industry, with the jobs
  sped up: ore stored and refined at the Belt Smelter (docked on its pad), the steel and
  aluminium carried to the Kestrel Deep Yard, a cargo pod built there and fitted (the hold
  grows) and taken off, the gas works refining only, landing on the pads of Ochre Foundry and
  Mnemos Works (their refinery and factory beside the pad), and the save written and read back.

  Add `-landtest` (or `?landtest` on the web) to test only the landings, with screenshots, on
  every kind of world. On the hostile ones it also checks that the ground forces are there,
  that a battery fires on the frigate, and that near the north pole (where an area can be built
  on both sides of the pole) each area's forces appear only once.
  Add `-minetest` (or `?minetest` on the web) to test only the mining. It warps to the Inner
  Belt in mining mode and scans. It chooses the nearest rock in the mining menu and lets the
  ship fly itself there (C), checks the laser cuts the rock chosen, and mines until the hold is
  full, moving from rock to rock. Then it jettisons the cheapest ore, opens the cargo list, and
  ejects the biggest load from the menu. Next it warps to Comet
  Iskra, checks the tails point away from the star and mines ice. Last, it lands on Ochre and
  mines a vein in the ground.
  Add `-hudtest` (or `?hudtest` on the web) to test the ship's console and the captain:
  - the console sits in the bottom-left corner, and all five captains have all 26 pictures;
  - an expression grows through its steps;
  - there are at least 250 lines, at least 5 for every event, and she speaks about what happens
    during the test, each line in its turn;
  - the captain reacts as she should to:
    - hits on the shield and hull;
    - the bow gun charging and firing, and a scan;
    - a warp, docking and a landing;
    - a fight at Corsair Haven, with contact, hits, firing back and a kill;
    - the ship lost and respawned;
  - she talks with each message and blinks;
  - the captain can't be changed in the pause menu (neither by clicking her portrait nor by
    key), and a click on the HUD size row's < side makes the HUD smaller, on its > side bigger.

  The test pictures each captain, then puts the player's own choice back.
  Add `-orbittest` (or `?orbittest` on the web) to test the orbits. It runs a week of them in
  a minute and a quarter and checks every motion, in the system's doubles, against Kepler's laws:
  - the Highport stays geostationary;
  - Thalassa's L1 stays on the line from the star, kept there by station-keeping burns;
  - Jovara's L4 and L5 stay 60° from it, with the Trojans librating;
  - the comet follows its ellipse;
  - the frigate and the posted enemies stay with their places;
  - the map changes, and every launcher launches.

  Then it warps to fast-moving Cinder and to each launcher for pictures, and restarts.
  Add `-striketest` (or `?striketest` on the web) to test the strikes from orbit: the mass driver
  striking Pyrois Domes on its own time with nobody near, the ion satellite watched burning into
  Port Ochre, the frigate's rod on Cinder Fortress (Y) destroying its ground forces, landing there
  to find them gone and the crater in the ground, the rubble at Port Ochre, the ion satellite
  destroyed and striking no more, and the save holding the craters and the satellites' clocks.
  Add `-vrasktest` (or `?vrasktest` on the web) to test the Vrask: their raiders round Comet
  Mirren, all four of their ships there, hostile, armoured, shielded and each with its own
  weapons, the Maw the slowest; the frigate warps there and closes in until they spring; the
  warp drive locked while they fight; each of the four firing on the frigate, ion bolts among
  the hits; the Snares' capture (their ion fire knocking out the
  frigate's shield generator and engines, a tractor lock pulling it in, the warp drive refused
  while held, the beam letting go when its Snare is destroyed); boarding (the Snares' pods at the
  frigate they held, the CIWS shooting pods down, none getting aboard through the shield, a pod
  breaching with the shield down and a full crew repelling its breachers, a crew near its
  survivors overrun and the frigate captured, its cargo seized and its credits kept, the
  prisoners counted, the frigate back at its respawn point; the Fang coming in, launching its
  pods, clamping onto the hull and holding the frigate, then casting off); the Maw's artillery
  (holding its fire with no other Vrask ship near the frigate, then opening fire from beyond
  8 km once a Skink is near it, a nullifier at its shield, the shell landing on it and its blast
  hurting a hulk 300 m off too) and its nuclear missiles (one breaking the whole frigate's shield
  without destroying it, the silos holding two); the Vrask siege over Port Ochre (on station over
  a city on a world with air, its raids on the game's clock and saved, a scripted nuclear strike
  ruining every building in the city for longer than a kinetic strike and killing its people);
  the fleet engagement (the whole group against the frigate, whose crew is too big to be taken:
  the Skinks on picket making contact, the Maw's artillery first while the Snares and the Fang
  hold back, a Skink shooting down a missile the frigate fires at the Maw, the Snares disabling
  the frigate and a tractor holding it, then the Fang boarding; the Maw and both Snares
  destroyed and the rest withdrawing into hiding); each destroyed; and all of them back with the
  spawns, no longer holding off.
  Add `-modestest` (or `?modestest` on the web) to test the modes of play: a game starts in
  combat mode; 1 to 5 change mode, each with its keys in the controls box; a mode's keys work
  in it and not in another (V scans in mining mode, not in combat); services away from a site
  says to dock or land; the combat menu opens over a game that goes on, lists the targets in
  range and targets the one chosen; lining up (C) turns the ship until its guns bear, and
  steering takes the helm back; a hostile destroyed leaves a wreck, listed in the menu, chosen
  there, and Z strips it into the hold.
  Add `-fleettest` (or `?fleettest` on the web) to test the fleet: a Halberd and a Rapier berthed
  at the Kestrel Deep Yard; there the Halberd joins the fleet crewed and the Rapier is towed (the
  leader at half speed, its warps twice as long); the fleet mode's menu lists them; the Halberd
  follows on a run, and attacks the leader's target (V, combat mode); the fleet warps with the
  leader to Thalassa; the Halberd destroyed is lost; the Rapier left at the yard is berthed
  there, and the leader's speed and warps are back to normal.
  Add `-servicestest` (or `?servicestest` on the web) to test the services mode: docking at the
  Highport puts the game in services mode; its trade page sells the ore at its worth, buys a
  part at the markup and sells it back; ten crew are dropped off and recruited back; undocking
  goes back to the mode before; and the Belt Smelter's trade page buys minerals and nothing else.
  Add `-navtest` (or `?navtest` on the web) to test the navigation mode: which places are
  places and which are points of interest at them; the map stepping through places only; at
  Thalassa, the menu listing its points of interest; auto-travel to the Highport to dock, flown
  with no warp; auto-travel to the launch loop to land, flown and then taken down by the
  auto-lander; a course set (C) marked but not flown; and X stopping a trip.
  Add `-approachtest` (or `?approachtest` on the web) to test the real-size system and flying
  down to it: the planets' sizes and distances, the world drawn round the frigate after a warp
  of millions of kilometres, the far bodies drawn nearer, Ochre's ground seen from orbit, the
  auto-lander from orbit down through the air onto Port Ochre's pad, the climb back to orbit,
  and a descent by hand onto airless Selene and back up.
  Add `-stationtest` (or `?stationtest` on the web) to test only the stations, bases and
  installations. It warps to each one. It docks at every berth, checking the repair, the sale
  and that the warp drive and landing are locked while docked. It flies in on Corsair Haven and
  the orbital battery until their guns open up, fights, destroys a gun and a platform, and
  leaves until they stand down. It lands at both surface bases, checking their buildings and
  ground forces. It checks the garbage is collected on every landing, take-off and restart.
- `CarrierDemo.exe -reel <dir>` (or `AsteroidAmbush.exe` / `StarSystem.exe`): records a demo reel
  (60 s, 76 s for the ambush or about 215 s for the star system) at 30 fps as JPG frames plus a WAV
  soundtrack, then quits. Add `-features` to `StarSystem.exe -reel <dir>` for the feature reel
  (about 310 s: mining, combat, landing and mini-warps), `-orbits` for the orbits and stations
  reel (about 210 s), `-vrask` for the Vrask reel (about 4 minutes: flown docking, ships keeping
  clear, components, the Vrask and the siege of Port Ochre), `-frigates` for the start menu and the five frigates, `-sea` for the sea
  reel (about 200 s: each frigate comes down from orbit onto Pelagia's sea, sails in on an island
  base, dives on a torpedo base and launches out of the water), or `-all` for everything in one reel. That includes:
  - the ship's console and the captain
  - every ship on the start menu, each frigate close up, and the cruiser and destroyers firing
    missile salvoes
  - the real-size worlds and the auto-lander flying down from orbit to a pad
  - refining, building and fitting a part on the sites' screen, and a cruiser laid down and built
    at the Kestrel Deep Yard's shipyard, then taken command of
  - the crew (losses, the frigate slowed, recruiting) and the ten settlements
  - the orbital strikes (the ion satellite over Port Ochre, a kinetic rod on Cinder Fortress, and
    Port Ochre's ruins)
  - a run on Pelagia's sea

  Add `-reelcoop <address>:<port>` to `-all` for its co-op scene: two scripted wingmen
  (`Tools/CoopTest/wingman.py <server url> <name> <right,up,forward>`, started first, named
  `Kestrel 2` and `Kestrel 3`) wait on that server and fly in formation with the frigate. With
  `-reelbot` as well, the scene goes on to the AI wingman: start a bot game (`StarSystem.exe
  -batchmode -nographics -coopbot -connect <address>:<port> -name Wingman`) once the reel's game
  has joined, so the reel's game hosts. The reel holds the bot where it is, brings the frigate up
  behind it, and orders it on the comms channel. `-reelscenes Intro,ShipsMenu,...` records only
  the scenes named (the names in `DemoReel.All.cs`), to try them out.
  Join the frames and sound with ffmpeg:

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

## Deprecated games: Carrier Demo and Asteroid Ambush

The first two games, Carrier Demo (the frigate against a cruiser and drones in open space) and
Asteroid Ambush (an ambush in an asteroid field), are deprecated. Star System has everything
they had: the same frigates and weapons, fights in open space, and ambushes in its asteroid
fields, along with everything else. Only Star System is built and shipped now, for Windows
(`StarSystem.zip`) and the web (`StarSystemWeb.zip`).

- Their last downloads stay on the
  [release page](https://github.com/beknar/battlecruiser-4000-bc/releases/tag/demo-reel-2026-11-03),
  but are no longer updated.
- Their scenes and code are still in the project, and **Tools > Carrier > Deprecated** builds
  them.
- The test suite no longer builds or tests them unless asked: `run_tests.py --only autotest
  --build` builds them and runs their autotests.

## Missing third-party assets

`Assets/Carrier/Kit` is not in this repo. It holds marketplace packs from an Unreal asset export:
Spaceship_Kit, CargoShip, Mega_City, MC_Environment, Megastructure_Fallen_City,
Sci-Fi_Flying_Cargo_Ship and StylizedShieldsVol1, and for the settlements SpaceColony2086, Starship,
Volstok_District, Monolith_megastructure, OrbitalBase, Tropical_Scifi_City, SciFi_Cliff,
SilentZonePack, RMG_Crane, Industrial_Pipe_Rack and Desert_Outpost (staged by
`Tools/stage_carrier_assets.py`). Those packs are licensed and can't be redistributed. Without them
the settlements are built with only the project's own pieces.

Two sets of files are made from those packs, so they are left out too: the enemy cruiser's breakup
meshes and `Models/ShieldHex.fbx`.

Without the packs, these are missing: the cruiser model, the engine plumes, the skybox, some hull
textures and the shield mesh. To restore them, put your own copies of the packs in
`Assets/Carrier/Kit` and run **Tools > Carrier > Build Everything**.

`Assets/Carrier/Resources/CarrierAudio` (the sound effect library and the music) is licensed
too and not in the repo. Without it, the sound effects are synthesised in code and there is no
music.
