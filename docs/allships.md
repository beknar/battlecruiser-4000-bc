# All ships: statistics, capabilities and roles

Every ship in Star System, the player's and the enemies', as the game builds them today. The
numbers come from the game's data and its scene builder:

- player ships: `Assets/Carrier/Data/frigates.json` and the hardpoint files beside it
  (`frigate_*_hardpoints.json`, `destroyer_*_hardpoints.json`, `cruiser_*_hardpoints.json`);
- shipyard costs: `Assets/Carrier/Resources/Data/industry.json`;
- the Vrask's ships: `Assets/Carrier/Data/vrask.json` (and `CarrierBuilder.Vrask.cs`);
- enemy ships: `Assets/Carrier/Editor/CarrierBuilder.cs`, `CarrierBuilder.CapitalShips.cs`,
  `CarrierBuilder.FrigateSystems.cs`, `CarrierBuilder.StarSystem.cs` and `Scripts/PilotPresets.cs`;
- planetary forces: `Assets/Carrier/Resources/Data/ground_units.json`.

Change those and rebuild (**Tools > Carrier > Build Everything**), and this page goes out of date.

Units: speeds in m/s, turns in degrees a second, ranges in metres, damage in hit points (a gun's
per shot, a beam's per second), times in seconds. The **Roles** sections are projections: how each
ship is meant to be used, judged from its numbers, not rules the game enforces.

## Player ships

There are twelve: five frigates to choose from on the start menu, and five destroyers and two
cruisers built at a shipyard. You fly one at a time; ships you've built can sail with you, crewed (or
towed, which halves your speed), as your fleet (up to ten in all, ordered in fleet mode, key 5).

### Every player ship has

| System | What it does |
| --- | --- |
| Shield and hull | The shield takes hits first and recharges; the hull doesn't. |
| Turret and ball-mounted guns | Fire on the target on their own. Each ship's guns are in its row below. |
| CIWS mounts | Close-in guns that pick their own targets: the nearest drone or fighter, hostile missile or Vrask boarding pod they can bring to bear (a missile or pod first, unless a craft is less than half as far; never a capital ship), 8 damage a shot. Two bolts shoot down a missile, five a boarding pod. |
| Warp drive | Jumps between the system's places, from the map or navigation mode. |
| Mining laser | 8 t of ore a second, out to 1.5 km; the ore goes in the hold. |
| Kinetic strike (Y, combat mode) | A tungsten rod dropped from orbit onto a planet or moon, 3 km up to twice the body's radius: everything within 450 m destroyed, a crater left, one every 20 s. It aims at the nearest ground forces within 30 km, or the ground below. |
| Cargo hold | Ore, refined materials and parts all count against it. |
| Part slots | Fittings bought or made at industry sites (`industry.json` parts). |
| Crew | Short of 90 % of its complement, a ship works less well. |
| Docking and landing | Docks at stations and surface bases; flies down to the planets, moons and asteroid fields that can be landed on. |

### Frigates (chosen on the start menu)

| | Sentinel | Lancer | Warden | Talon | Vanguard |
| --- | ---: | ---: | ---: | ---: | ---: |
| Class | Gun frigate | Fast attack frigate | Heavy escort frigate | Strike frigate | Line frigate |
| Length (m) | 180 | 170 | 200 | 175 | 190 |
| Hull | 6,000 | 4,500 | 8,500 | 5,500 | 9,000 |
| Shield | 6,000 | 5,000 | 8,000 | 5,500 | 7,000 |
| Shield recharge (/s) | 250 | 300 | 220 | 260 | 200 |
| Hull + shield | 12,000 | 9,500 | 16,500 | 11,000 | 16,000 |
| Max speed | 150 | **190** | 120 | 165 | 115 |
| Acceleration | 30 | **42** | 22 | 34 | 18 |
| Turn rate | 20 | **28** | 15 | 22 | 13 |
| Guns | 2 ball mounts | 2 twin turrets | 3 twin turrets | 2 heavy twin turrets | 6 turrets, all bearing forward |
| Gun damage / shot | 90 | 55 | 70 | **130** | 75 |
| Gun interval | 0.5 | 0.55 | 0.8 | 1.0 | 0.7 |
| Gun range | 4,500 | 4,200 | 5,000 | 5,200 | 5,200 |
| Bow beam (dmg/s) | 1,400 | 1,200 | 1,400 | 1,800 | **2,400** |
| Beam charge | 10 | 9 | 12 | **7** | 14 |
| Missiles | none | none | none | none | none |
| CIWS mounts | 4 | 2 | 4 | 1 | 4 |
| Cargo (t) | 600 | 450 | **800** | 550 | 500 |
| Part slots | 4 | 3 | **5** | 4 | 3 |
| Crew | 140 | 90 | 220 | 110 | 300 |

**Roles**

- **Sentinel, all-rounder.** The starting ship and the balanced choice. Its two ball-mounted guns
  reach almost any bearing, and it has the fastest-firing guns of the frigates. Four CIWS mounts
  make it comfortable against fighters and drones. Good for learning, mining runs and mixed patrols.
- **Lancer, fast attack and scouting.** The fastest and most agile ship in the game, but the
  lightest-built. It picks fights it can win and runs from the rest: hunting fighters and drones,
  scouting ahead, and getting to a distress call first. Its 4,500 hull is the thinnest of all, so it
  can't trade blows with capital ships for long.
- **Warden, escort and hauler.** The thickest frigate shield, with four CIWS mounts and the biggest
  frigate hold and slot count. It suits guarding stations, escorting the fleet, absorbing a
  battle group's fire while others do the killing, and long mining-and-hauling runs.
- **Talon, strike.** Heavy single shots (130) and the quickest bow beam to charge (7 s). It kills
  capital ships fast in hit-and-run attacks. Its single CIWS mount leaves it exposed to fighter
  swarms, so it wants an escort or a quick exit.
- **Vanguard, line ship.** The toughest frigate hull and the heaviest frigate beam, with six turrets
  firing forward. It suits head-on fights against cruisers and destroyers, and bombarding
  ground defences from orbit. It is slow to arrive and slow to turn, so fighters can get behind it.

### Destroyers and cruisers (built at a shipyard)

These are built at a shipyard: Ochre Foundry, Mnemos Works, Kestrel Deep Yard, Belt Smelter,
Steropes Heavy Industries or Ochre Orbital Works. Their missiles fire from launch cells and are
refilled when docked or landed at a site. The destroyers have no bow beam; the cruisers do.

| | Halberd | Rapier | Claymore | Bulwark | Stiletto | Sovereign | Dominion |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Class | Missile destroyer | Fast destroyer | Gun destroyer | Heavy destroyer | Strike destroyer | Heavy cruiser | Gun cruiser |
| Length (m) | 300 | 280 | 300 | 290 | 270 | 420 | 430 |
| Hull | 12,000 | 11,000 | 13,000 | 15,000 | 11,500 | **26,000** | 24,000 |
| Shield | 10,000 | 9,500 | 10,500 | 12,000 | 9,000 | **20,000** | 18,000 |
| Shield recharge (/s) | 320 | 340 | 330 | 300 | 350 | **450** | 420 |
| Hull + shield | 22,000 | 20,500 | 23,500 | 27,000 | 20,500 | **46,000** | 42,000 |
| Max speed | 108 | **112** | 105 | 100 | 110 | 90 | 95 |
| Acceleration | 17 | **19** | 16 | 15 | 18 | 12 | 13 |
| Turn rate | 12 | **13** | 11 | 10 | 12 | 8 | 7 |
| Guns | 5 turrets (quad forward) + 3 ball mounts | 4 turrets + 3 ball mounts | 3 heavy single turrets + 5 ball mounts | 4 twin turrets + 4 flank mounts | 1 big dorsal turret + 6 ball mounts | 8 turrets + 4 ball mounts | 3 triple and 5 twin turrets + 4 ball mounts |
| Gun damage / shot | 70 | 65 | 120 | 95 | 80 | 125 | **165** |
| Gun interval | 0.75 | 0.6 | 0.9 | 0.85 | 0.7 | 0.8 | 1.0 |
| Gun range | 5,000 | 4,800 | 5,400 | 5,200 | 5,000 | 5,600 | **6,200** |
| Bow beam (dmg/s) | none | none | none | none | none | 3,200 (16 s charge) | **3,600** (18 s charge) |
| Missiles (cells) | 24 (6) | 12 (4) | 12 (6) | 16 (4) | 20 (8) | **32** (8) | 16 (4) |
| CIWS mounts | 2 | 1 | 2 | 2 | 1 | **6** | 4 |
| Cargo (t) | 900 | 700 | 850 | 1,100 | 750 | **1,600** | 1,400 |
| Part slots | 5 | 5 | 6 | 6 | 5 | **8** | **8** |
| Crew | 380 | 360 | 420 | 460 | 350 | 800 | 900 |
| Shipyard time | 10 min | 9 min | 10 min | 11 min | 9 min 20 s | 20 min | 21 min |
| Steel plate (t) | 150 | 120 | 160 | 200 | 120 | 400, plus 100 t titanium billet | 450, plus 80 t titanium billet |

Every one also needs parts: hull frames, turret mounts, missile cells, a bridge module, crew
quarters, a reactor core, fusion thrusters, a shield emitter and armour plating. On top of those,
the Halberd and Stiletto need fire control, the Rapier vectoring jets and the Claymore rail barrels.
The Sovereign needs a capacitor bank, a beam lens and fire control; the Dominion the same, with
rail barrels besides. `industry.json` has the tonnes.

**Roles**

- **Halberd, missile boat.** Twenty-four missiles fired from six cells, about 6,000 damage when
  they all hit. It opens a fight from range against a capital ship, then finishes with its guns.
  It is best kept behind a tougher ship.
- **Rapier, fast escort.** The quickest destroyer, with the fastest-firing guns of the class. It
  screens the fleet against fighters and drones and chases down what runs. Its single CIWS mount
  is its weakness.
- **Claymore, gunship.** The hardest-hitting destroyer guns (120 a shot, 5.4 km). It suits
  sustained gun duels with cruisers and destroyers, and knocking out stations' turrets.
- **Bulwark, tank and freighter.** The toughest destroyer and the biggest destroyer hold. It
  draws fire in fleet fights, guards a mining operation, and carries a whole shipyard's worth of
  materials.
- **Stiletto, strike.** Eight cells, the most of any destroyer, launch its twenty missiles in big
  salvos fast. It suits alpha strikes on a single target: a dreadnought's escorts, or a
  weapons platform.
- **Sovereign, flagship.** Nearly twice the hull and shield of the toughest destroyer, with the heaviest beam,
  guns and missile load, six CIWS mounts and the biggest hold. It is the only player ship meant to
  fight the dreadnought battle group head-on, and the best fleet leader. Its cost (400 t steel,
  100 t titanium, a crew of 800, 20 minutes) and its slowness are the price.
- **Dominion, gunline.** A battleship among cruisers: nineteen barrels in eight turrets (three
  triples forward, each firing over the one ahead) and four ball mounts, the heaviest guns
  (165 a shot, 6.2 km) and the heaviest beam. It wins gun duels at long range with capital
  ships, stations and batteries. It has half the Sovereign's missiles, four CIWS mounts and less
  armour, and is the slowest ship of all to turn, so fighters that get close are its problem.

### The player fleet at a glance

| Want | Take |
| --- | --- |
| Speed, scouting, catching fighters | Lancer, then Rapier |
| Killing capital ships quickly | Talon (beam), Stiletto or Halberd (missiles), Sovereign, Dominion (guns and beam) |
| Taking punishment | Warden, Vanguard, then Bulwark, then Dominion and Sovereign |
| Fighter and drone defence (CIWS) | Sentinel, Warden or Vanguard (4 CIWS each), Sovereign (6), Dominion (4) |
| Mining and hauling | Warden (800 t) among frigates; Bulwark (1,100 t) and Sovereign (1,600 t) among the rest |
| Bombarding a planet | Any (all have the kinetic strike). Vanguard or Warden survive ground fire best |

## Enemy ships

The enemies in this table belong to the pirates and corsairs. Their hull is all there is: they carry
no shields, so the numbers below are the whole of their toughness. (The Vrask, below, are shielded
and armoured.) In co-op they get tougher and hit harder
with each player in the level:

- **Toughness:** hull grows 60 % for each player after the first.
- **Firepower:** damage grows 12 % for each player after the first.

Destroyed ships come back after their respawn time, at their post: the field ships in the rocks, the battle groups on station.

| | Fighter | Drone | Cruiser | Destroyer | Dreadnought |
| --- | ---: | ---: | ---: | ---: | ---: |
| Shown as | Raider Fighter, Hostile Fighter | Raider / Pirate Drone, Hostile Drone | Pirate / Corsair / Hostile Cruiser | Corsair / Escort Destroyer | Pirate Dreadnought |
| Class | Strike fighter | Recon drone | Heavy cruiser | Destroyer | Dreadnought |
| Threat (as targeted) | Medium | Low | Medium | High | Extreme |
| Size | small | small | large freighter hull | the Sentinel's hull at 85 % (≈150 m) | 700 m (the carrier hull) |
| Hull | 260 | 300 | 3,000 | 2,200 | **18,000** |
| Cruise / attack speed | 230 / **320** | 190 / 250 | 75 / 90 | 110 / 140 | 45 / 55 |
| Turn rate | 90 | **110** | 12 | 18 | 6 |
| Guns | blaster cannons: 4-shot bursts every 0.9 s, 6 a shot, 1.5 km, only within 18° of the nose | blasters: 3-shot bursts every 1.2 s, 6 a shot, 1.8 km, within 35° of the nose | cannons: 3-shell bursts every 4 s, 30 a shell, 3.5 km | 2 triple turrets: every 3.2 s, 30 a shell, 4.2 km | 8 triple turrets: every 6.5 s, 30 a shell, 5.5 km |
| Missiles (150 each) | none | none | 3 every 20 s, 4.5 km | 2 every 18 s, 5 km | 4 every 16 s, 6 km |
| Beam | none | none | 260/s for 1.6 s after a 4 s charge, every 16 s, 4.5 km | none | 320/s for 2 s after a 5 s charge, every 18 s, 6 km |
| Tactics | attack runs: in fast, break off at 420 m, extend 1.4 km, come round again; hides in rock when hurt | shorter, tighter passes; hides in rock when hurt | stands off about 1.8 km and circles; hides when below half hull (the Corsair cruiser: 1.9 km, never hides) | stands off 2.3 km and circles | stands off 3.3 km and circles slowly |
| Respawn | 40 s | 40 s | 40 s in the Outer Drift, 75 s with the Corsair patrol | 60 s | 120 s |

### Where they are

| Group | Ships | Where |
| --- | --- | --- |
| Raiders | 4 fighters, 2 drones | Hidden among the big rocks of the Jovara Trojans, spotting you at 4.5 km |
| Pirate cruiser | 1 cruiser, 2 drones | Hidden in the Outer Drift, spotting you at 5 km |
| Corsair patrol | 1 cruiser, 2 destroyers | On station in the open on the way in to Cinder, spotting you at 9 km |
| Dreadnought battle group | 1 dreadnought, 2 escort destroyers | On station on the way in to Glacis, spotting you at 9 km |
| Planetary defence | patrol fighters, plus pad fighters and drones and sometimes a gunship (the destroyer hull) | Over the planets' surfaces. Same guns and hull as in space, but slower (fighters 200/290 m/s, turn 85; gunships 100/130 m/s, 1.8 km standoff), and they don't hide. Rebuilt 8 min (fighters, drones) and 15 min (gunships) after they're destroyed |

The two older levels (Carrier Demo and Asteroid Ambush, both deprecated) use the same drone and
cruiser (Asteroid Ambush the fighter too).

### Roles

- **Fighter, harasser.** It does little damage on its own (about 27 a second while on target),
  but it is the fastest thing in the system and comes in groups. It chips at shields and keeps
  a slow ship turning. Ships with ball mounts or many turrets handle fighters best (the Sentinel
  and the Vanguard). The Lancer can chase them.
- **Drone, picket.** It is weaker still, turns tightest, and is there to find you and add to the
  noise of a fight. It runs into the rocks when hurt.
- **Cruiser, ambusher.** It waits in the rocks and opens with cannons, a missile salvo and a
  charged beam. It is the main threat in the asteroid fields, and it retreats into cover when below
  half its hull. A frigate's bow beam is the quickest answer to it.
- **Destroyer, line escort.** Its missile pairs are the main danger, and only the shield stops
  them. It protects the cruiser or the dreadnought and comes back a minute after it is destroyed, so
  killing the flagship first is usually the better plan.
- **Dreadnought, fortress.** Eight triple turrets, four-missile salvos and a heavy beam, all
  reaching 5.5 to 6 km, and 18,000 hull. It is too slow to chase anything, so a fast ship can disengage at will. A
  fleet, or the Sovereign, is the intended answer.

## The Vrask

An alien faction, lizard-like, who take prisoners, drain their life and capture ships. They are
hostile like the pirates (the target display says "Hostile: Vrask"). They are the first enemies
with **shields**, and their hulls carry **armour** plating, which stops a share of each hit that
gets through the shield until it is worn through. Their ion bolts are twice as hard on a shield
as their damage says and a quarter as hard on a hull; the Maw's artillery shells strip a shield
three times as hard (the nullifier) or go straight through armour (the penetrator). Numbers:
`Assets/Carrier/Data/vrask.json`.

| | Snare | Fang | Maw | Skink |
| --- | ---: | ---: | ---: | ---: |
| Shown as | Vrask Snare A, B | Vrask Fang | Vrask Maw | Vrask Skink A, B |
| Class | Capture frigate | Assault frigate | Siege frigate | Escort frigate |
| Threat (as targeted) | High | High | High | Medium |
| Length (m) | 170 | 150 | 190 | 130 |
| Hull | 2,600 | 2,800 | **3,000** | 2,200 |
| Shield (recharge /s) | 1,500 (80) | 1,200 (70) | **1,800** (90) | 1,200 (90) |
| Armour (share stopped, plating) | 32 %, 900 | 36 %, 1,100 | **40 %**, 1,400 | 30 %, 700 |
| Cruise / attack speed | 120 / 150 | 160 / 240 | 70 / 85 | 170 / **260** |
| Turn rate | 20 | 40 | 10 | **45** |
| Acceleration | 30 | 60 | 12 | **70** |
| Guns | 16 ion pods (turrets): 12 ion a bolt, every 4.5 s, 2.8 km; an ion battery of 6 tubes firing together, 12 each, every 3.5 s, 3 km, within 15° of the bow | 4 cheek guns (6 barrels): 40 a shell, every 2.4 s, 1.6 km, within 15° of the bow | spinal artillery: a shell every 16 s, 1.5 to **13 km**, within 20° of the bow, 550 m/s, bursting 500 m wide (700 at the centre, a fifth at the edge): a nullifier at a shield that is up, a penetrator at armour or a shield down; 3 twin dorsal turrets: 35 a shell, every 5.5 s, 5 km; 7 jaw and flank guns: 35, every 10 s, 4.5 km, within 6° | 8 point-defence guns: 3 a shot every 0.45 s, 2.4 km, swinging 130° |
| Missiles | none | 2 every 14 s, 3.5 km | 2 nuclear missiles (refilled on respawn), 9 km, at most one every 90 s: 320 m/s, shot down by 4 CIWS bolts; a 1.2 km blast, 3,000 at the centre, in space | none |
| Boarding pods | 2 every 30 s, at the ship its beam holds once its shield is down | 3 every 18 s from its 3 hatches, 3.5 km, as it closes; then its prow clamped on, a party of 12 | none | none |
| Tractor beam | 2.5 km: pulls at 30 m/s to 1.5 km off; out-flown alone, but not two of them, or with your engines out; lets go at 3.25 km | none | none | none |
| Sees (spots for the Maw) | 6 km | 6 km | 6 km (not for itself) | **9 km** |
| Tactics | stops 1.8 to 2.2 km off (1.45 to 1.8 km while holding), bow on; ion on your shield generator, then your engines; its tractor once they are out; breaks away inside 900 m | goes for a held or disabled ship; in fast from above, below or behind a Vrask ship; guns on your weapons; clamps on with your shield down, casts off when its boarders are done or its hull is under 30%; attack runs (breaks off at 450 m, extends 1.6 km) while your shield holds | holds 8 to 12 km from the target behind the others, bow on, hardly moving; fires only at what another Vrask ship sees (6 km, a Skink 9 km); salvos on a beat shared with any other Maw; shells on your shield generator, then your weapons (at an installation, its generators and batteries); falls back to its escorts inside 3 km; a nuke at a big ship whose shield holds, or ships bunched | patrols 1.5 to 2.5 km round the Maw or a Snare holding a ship; runs down your missiles, fighters and drones, and ships closing on them, within 6 km, at twice its acceleration; its guns take your missiles first (a needle does 8 to one: two bring it down); runs ahead as the picket; chases light or damaged ships that run, in to 500 m and out 1.5 km; with nothing to screen, stands off 1.5 km from you within 2.8 km of the Maw |
| Respawn | with the spawns (below) | | | |

### Where they are

| Group | Ships | Where |
| --- | --- | --- |
| Vrask raiders | 2 Snares, 1 Fang, 1 Maw, 2 Skinks | Hidden round Comet Mirren, 2 to 3 km from it, spotting you at 4 km (a warp drops you 8.5 km out, so they spring as you close in to mine it) |
| Vrask Siege Group | 1 Maw, 2 Skinks | On station 160 km over Port Ochre, turning with Ochre; a hostile destination. Not piloted: the guns open up within 12 km, the Maw turns in place to bring its artillery to bear on a ship its Skinks see (9 km); the Skinks' point defence shoots your missiles down, and they go quiet while the Maw is destroyed. It raids the city every two hours (see Not ships, but they fight). The Maw is back 30 minutes after it is destroyed |

They come back with the spawns like the pirates' bands: every 5 minutes, or 10 minutes after the
place is cleared.

### Roles

- **Snare, disabler and captor.** Its ion fire strips a shield fast (a pair of them, with the pods
  bearing, can take down a frigate's in well under a minute) but does little to a hull. It holds
  1.8 to 2.2 km off (1.45 to 1.8 km while it holds a ship), bow on, and breaks away from any ship that closes within 900 m. Its ion goes
  for your shield generator, then your engines; once they are knocked out (or you are crawling
  with your shield down) it locks its tractor beam on you and pulls you in, to be held 1.5 km off.
  One beam can be broken at full thrust straight away; two Snares work as a pack,
  one firing and one holding, and two beams, or one with your engines out, can't be broken. While
  held you can't warp, dock or land. Destroy it, or knock out its weapons, and its beam lets go.
  Holding a ship whose shield is down, it boards it: two pods every 30 s.
- **Fang, assault and boarding.** The heaviest guns at close range, in fast passes, with
  missiles, and boarding pods. It goes for a ship the Snares hold or have disabled, coming in
  fast from above or below, or from behind another Vrask ship, its guns aimed at your weapons
  (knock out your weapons and your CIWS go quiet too). As it closes it launches three pods every
  18 s once your shield is under half; each that gets through a shield down or under a quarter
  puts 8 to 12 breachers aboard. With your shield down it clamps its prow to your hull, sends 12
  more through, and holds you to a crawl until they are repelled or have taken the ship, or its
  hull is under 30%. Keep your shield up, keep your CIWS working, and a big crew aboard: boarders
  who bring the crew down to its survivors (15%) capture the ship, its cargo seized and its crew
  taken prisoner; it comes back at the last warp point with the survivors.
- **Maw, artillery and siege.** Its spinal artillery reaches 13 km, much further than anything
  else in the system, but it is blind on its own: it fires only at what another Vrask ship sees
  within 6 km (a Skink within 9 km), so it sits 8 to 12 km back behind the others and shells the
  ships they find, its salvos together with any other Maw's. The shells are slow (15 to 25 s in flight) and burst
  500 m round where you were going to be: change course when one is on its way. A nullifier
  round strips shields, a penetrator goes through armour. It saves its two nuclear missiles for
  a big ship whose shield holds, or a bunch of ships; a nuke breaks a frigate's shield but in
  space doesn't destroy a whole one, and the CIWS can shoot it down. It is the slowest ship in
  the system bar the dreadnought, and turns slowly to aim; close within 3 km and it falls back
  to its escorts. Kill its spotters, or get round it, past the Skinks.
- **Skink, escort, picket and hunter.** The fastest and most agile of them, with eight rapid
  light guns. It flies a loose patrol 1.5 to 2.5 km round one of the group's high-value ships
  (the Maw, or a Snare holding a ship), each Skink its own. A threat within 6 km of the group and
  it goes for it at twice its acceleration (140 m/s2): your missile on its way to one of them (it
  crosses the missile's path), your fighters and drones, your ship closing on the Maw or a holding
  Snare (it fights it 1.2 km out). Its guns take your missiles, fighters and drones before
  anything else: a needle does 8 to a missile (its 3, times 2.7), so two bring one down, as two
  CIWS bolts do. Its sensors reach 9 km against the others' 6, so it runs ahead of the group as
  its picket and is usually what the Maw fires by. A light (hull 3,000 or less) or damaged (under
  half) ship of yours that runs, it chases in hit-and-run passes, in to 500 m and out 1.5 km;
  after the main fight it runs down any cripple. With nothing to screen it stands off 1.5 km from
  you, within 2.8 km of the Maw.

### How they fight together

A group runs its fight in phases (`VraskFleet`; the numbers are `vrask.json`'s "fleet"), each
moving on when something is so on the ship it is working. The siege group has no Snares or Fang,
so it stays in the bombardment.

| Phase | Skinks | Snares | Fang | Maw | Moves on when |
| --- | --- | --- | --- | --- | --- |
| Approach and picket | up to 2.5 km ahead of the rest, no nearer you than 4 km | no nearer than 5.5 km | no nearer than 5.5 km | to its station, 8 to 12 km back | one of them has you in its sensors (contact) |
| Bombard | patrol and intercept | hold at 5.5 km | hold at 5.5 km | shells you | 45 s, your shield under half, or you within 4 km of a Snare or the Fang |
| Screen and disable | patrol and intercept | ion, then the tractor | holds at 5.5 km | shells you | you are disabled: engines out, held fast, held with your shield down, or boarders aboard |
| Board | patrol and intercept | hold you | pods, then its clamp | shells you | free of it for 15 s: back to screen and disable |
| Pursue | chase cripples | their own way | its own way | its own way | 20 s, then the next target |
| Withdraw | back into hiding | lost | back into hiding | lost | the Maw or a Snare back with the spawns |

- **With ships lost.** Without a Maw the bombardment is skipped; a target already disabled at
  contact goes straight to the boarding. A ship holding the line fights if you come within
  3.3 km of it.
- **Their target.** A player's ship first (the nearest, in co-op); without one, the ship most
  worth taking that is easiest to disable: its hull and shield, weighted up the more worn its
  shield and engines are, and down the further off it is (within 20 km).
- **Withdrawing.** With its Maw and both Snares destroyed the band breaks off and goes back into
  hiding, and doesn't spring again until they are back (one shot at fights back). The siege
  group's Skinks can't leave their station: they go quiet till the Maw is back.
- **Co-op.** The host's game runs the fleet and the Vrask's tactics; the other games show them.
  With more players every Vrask ship's hull and shield are tougher (×1.6 with two players, ×2.2
  with three) and its guns, shells and missiles hit harder (×1.12, ×1.24), as every enemy's are.
  A Skink that shoots down a player's missile in the host's game takes it down in that player's.

## The Greys

A mystery faction of saucers and a manta that watch, blind, strike and take. They are hostile
like the pirates and the Vrask (the target display says "Hostile: Greys"). They are the first
enemies that are **hard to see**: each has a signature under 1, so the players' sensors find it
only closer in than an ordinary ship, and name it closer still (see Sensors, stealth and
electronic warfare in the README). They are shielded but carry **no armour** plating. Their beams
are plain energy, as hard on a shield as on a hull; the Shroud's EMP bolts do a tenth of their
damage to a hull and knock a component out for a time instead. They hunt by their sensors: unlike
the pirates and the Vrask, they go only for a ship they can see. Numbers:
`Assets/Carrier/Data/greys.json`.

| | Scout | Shroud | Lance | Reaper |
| --- | ---: | ---: | ---: | ---: |
| Shown as | Grey Watcher | Grey Shroud | Grey Lance A, B | Grey Reaper |
| Class | Scout saucer | ECM saucer | Strike manta | Abduction saucer |
| Threat (as targeted) | Low | High | High | High |
| Length (m) | 110 | 140 | 125 | 160 |
| Hull | 1,400 | 3,400 | 2,400 | **4,600** |
| Shield (recharge /s) | 1,000 (100) | **4,200** (**180**) | 1,200 (90) | 3,000 (100) |
| Armour | none | none | none | none |
| Cruise / attack speed | 200 / 320 | 110 / 150 | 220 / **380** | 90 / 120 |
| Turn rate | 45 | 20 | **60** | 15 |
| Acceleration | 90 | 30 | **120** | 15 |
| Signature (seen at, named within) | 0.6 (18 km, 9 km); cloaked **0.06** (1.8 km, 0.9 km) | 0.8 (24 km, 12 km) | 0.8 (24 km, 12 km) | 0.8 (24 km, 12 km); cloaked while it holds back **0.28** (8.4 km, 4.2 km) |
| Sees (your ships) | **25 km** | 20 km | 18 km | 20 km |
| Guns | 2 hatch beams: 28 a bolt, every 2.2 s, 3.5 km, within 25° of the bow | EMP projector: an EMP bolt of 140 every 9 s, 4 km, within 50° | strike salvo, 5 slots firing together: 62 a bolt, every 2.6 s, 2.2 km, within 12° of the nose | 2 rim guns: 28 a bolt, every 4 s, 2.5 km, within 30° |
| Electronic warfare | targeting laser, 15 km; a cloak | jammer: a 6 km field, 60 % at its heart, 3 ghost contacts, missiles scrambled and drones stunned within 1.5 km; EMP burst: a 4 s charge, then 400 to every one of your ships within 2.5 km, ready again 30 s after | none | none |
| Drones | 2: both out at once in one launch, every 30 s, within 6 km | none | none | none |
| Tractor beam | none | none | none | 2.5 km: locks on within 1.8 km of a disabled ship and holds it 1.5 km off; no warp while held; lets go at 3.25 km |
| Abduction | none | none | none | while it holds a ship, every 3 s: 4 of its crew (never below the survivors) and 6 t of its cargo; after 8 takes, a sample (a component's condition halved) |
| Tactics | shadows a ship it sees 7 km back and 2.1 km to one side, cloaked, guns cold; paints it; calls in another band; slips away at full speed from any of your ships within 3 km; with your shield down, a hit-and-run (at most every 45 s): in at full speed, uncloaked, drones out, 8 s of fire in range (25 s at most), then cloaked again | keeps 1.2 km from the Reaper, between it and you (4 km off you with no Reaper within 20 km); jams once you are within 9 km of it; its burst once it sees you within 2.1 km | goes for your craft near the Shroud or Reaper, then the ship of yours it sees with the least hull left; swings out to your flank (1.8 km to the side, 1.1 km behind) and runs in from there, its salvo on your engines (your weapons once they are out); breaks off at 500 m, extends 2.2 km, round again from the other flank | holds 6 km off while the others work; closes on a ship once it is disabled and locks on; lets go if the ship is no longer disabled or its own hull is under 35 %; tows the ship off as the band vanishes |
| Respawn | 90 s | 90 s | 75 s | 120 s |

### Where they are

| Group | Ships | Where |
| --- | --- | --- |
| Greys cell | 1 Reaper, 1 Shroud, 2 Lances, 1 Scout | The Testing System: hidden in the Outer Drift round its middle (the Reaper 3 km back from it, the Scout 4.5 km out on the side away from the star), spotting you at 9 km. On the map: danger near, "Harder" |
| Greys watcher | 1 Scout (Grey Watcher Trojans) | The Testing System: 6 km from the Jovara Trojans, spotting you at 12 km. "Easy" |
| Greys cell | as the Outer Drift's | The Nine Hells: picking over the dead race's ruins on Goliath, half-way out from the planet to where a warp drops you, spotting you at 9 km. It raids Nineveh Observatory for people (below). "Harder" |
| Greys watcher | 1 Scout (Grey Watcher Ptolomea) | The Nine Hells: by Ptolomea, spotting you at 12 km. "Easy" |

A band springs when one of its ships, still in hiding, has a ship of yours within its spotting
range and in sight, or when one of them is shot. Each ship comes back at its hiding place its
respawn time after it is destroyed (straight into the fight if its band is fighting); the Scout's
drone stock fills again when it does.

### Roles

- **Scout, watcher and spotter.** The ship you are least likely to see. Cloaked, its signature is
  0.06: your sensors find it within 1.8 km and name it within 0.9 km (further with good ECCM: a
  Sentinel's or a Warden's, 0.7, see it from 3.1 km). It shadows the ship it sees 7 km back and
  off to one side, guns cold so the cloak holds, with its targeting laser on it (the HUD warns
  you, and says from where). The first time it sees you it calls in the nearest other Greys band
  lying in wait, wherever it is in the system: a band more than 15 km off drops out of warp
  together 7 km beyond you, on the far side from the Scout ("Unidentified ships drop out of warp
  nearby"). Close within 3 km and it runs, at 320 m/s. With your shield down it makes a
  hit-and-run, at most once in 45 s: straight in, its cloak off and its two drones out, 8 s of
  fire once its beams reach (25 s at most), then away and cloaked again, its drones out 20 s more.
  Firing or a hit lights it up: seen at 30 km and named for 8 s after, and its cloak takes 4 s
  more to come back.
- **Shroud, electronic warfare.** The band's jammer (see the README for what jamming does). It
  keeps by the Reaper, 1.2 km out from it towards you, with its field over both, and jams once you
  are within 9 km of it, quiet otherwise. In its field your sensors see less far and name less,
  your turrets' aim is thrown off, your missiles lose their lock, your orders may not get through,
  ghost contacts move on your sensors and the ships near it are shown at a false place till named.
  Within 1.5 km of it your missiles are scrambled (they go off short) and your drones stunned. It
  has no guns, but an EMP projector (140 every 9 s, out to 4 km) and an EMP burst: once it sees
  you within 2.1 km it charges for 4 s, the HUD counting it down, then knocks a component of every
  one of your ships within 2.5 km out for a time. Get out of reach while it charges, or knock its
  weapons out (the pulse fizzles).
- **Lance, precision strike.** The fastest of them, and the hardest hitter: five 62-point bolts
  at once, though only within 12° of its nose and 2.2 km. It doesn't come head on: it swings out to
  your flank or rear (for 6 s at most: a ship that turns with it is run at from wherever it is) and runs in from there, its salvo aimed at your engines (once they are out,
  your weapons), breaks off at 500 m, extends 2.2 km and comes round from the other side. Of the
  ships of yours it sees, it goes for the one with the least hull left. It screens the Shroud and
  the Reaper first: a fighter or drone of yours within 3 km of either is its target before any
  ship. With no ship of yours in sight it strikes a friendly facility (yours or an ally's) within
  4 km of it.
- **Reaper, capture by abduction.** The toughest of them, slow, with two light rim guns. It holds
  6 km back while the others work. Once a ship of yours it sees is disabled (its engines out, or
  its shield down and its hull under half), it closes to 1.8 km and locks its tractor on (one beam:
  the Vrask's rules, so with your engines working full thrust can break it, and with them out you
  are held fast). Held, you can't warp, dock or land. Its abduction beam then takes 4 of your crew
  alive and 6 t of your cargo (the biggest load first) every 3 s, never bringing the crew below its
  survivors; after 8 takes (or with the crew at its survivors) it takes a sample, halving one of
  your components at random till it is repaired, and its work is done. The crew it takes are
  prisoners of the Greys. It lets go if you are no longer disabled (your engines back, or your
  shield), or its own hull falls under 35 %, or its weapons are knocked out.

### How they fight together

A band's phase (`GreysFleet`) is the furthest along of what its ships are doing. Unlike the
Vrask's, it holds no ship back: from the moment the band springs each fights by its own tactics,
and the fleet decides only when the band leaves.

| Phase | When | What it means |
| --- | --- | --- |
| Watch | not sprung, or none of the others below | lying in wait, or hunting where you were last seen |
| Paint | the Scout's laser is on one of your ships | it has called the nearest other band in |
| Blind | the Shroud is jamming | your sensors, aim, missiles and comms are degraded |
| Strike | a Lance has a target | the Lances are on their runs |
| Take | the Reaper's tractor holds a ship | it abducts from it |
| Vanish | the Reaper has its prize, or the Shroud and the Reaper are both destroyed | every ship breaks off back into hiding (the Scout cloaked), the Reaper towing what it holds |

- **Hunting by sensors.** Each ship goes for a ship of yours within its sensors (18 to 25 km;
  your ships have no signature to hide them). Lost from sight, it searches where it saw you last,
  and after 30 s gives you up.
- **Their targets.** The Scout and the Shroud take the nearest ship of yours they see; the Lances
  and the Reaper choose as above.
- **Vanishing.** The band says so ("Greys cell vanishing") and goes. The Reaper tows the ship it
  holds away from it at 24 m/s (no faster than its beam can pull) for 60 s, then lets go and goes
  home. The band doesn't spring again till the Shroud and the Reaper are back and every ship is in
  hiding at its place.
- **Raids.** The Nine Hells cell raids Nineveh Observatory every three hours of the game's clock
  (saved, whether anyone is near or not), while it isn't fighting and its Reaper is alive: 3 % of
  the people there (at least one) are taken, and a notice says so.
- **Co-op.** The host's game runs the bands and their tactics, and the others show them. Each game
  works out the jamming on its own player, and makes the same ghosts; the host says when the
  Scout's cloak is up and what it paints, and abduction from another player's ship is done in that
  player's game. With more players every Greys ship's hull and shield are tougher (×1.6 with two
  players, ×2.2 with three) and its hits harder (×1.12, ×1.24), as every enemy's are.

## The Empire

An aggressive military power of long grey warships that fights as a disciplined line where the
Greys ambush. They are hostile like the pirates and the Vrask (the target display says "Hostile:
Empire"). They are shielded, and armoured as the Vrask are: ablative plating that stops a share of
each hit that gets through the shield until it is worn through, plates blowing off the hull as it
wears (16 on an Assault frigate, 10 on a Patrol frigate, 6 on a Raider or a Recon frigate). They
are the first enemies with **CIWS** of their own, **combat drones** launched in the fight,
**warp inhibitors** and **marines**. They carry no signature, so the players' sensors see them as
any ship; but like the Greys they hunt by their sensors, going only for a ship they can see.
Numbers: `Assets/Carrier/Data/empire.json`.

| | Assault | Patrol | Raider | Recon |
| --- | ---: | ---: | ---: | ---: |
| Shown as | Imperial Assault Frigate A, B | Imperial Patrol Frigate A, B | Imperial Raider A, B | Imperial Recon Frigate |
| Class | Line frigate | Escort frigate | Fast attack frigate | Recon frigate |
| Threat (as targeted) | High | Medium | Medium | Medium |
| Length (m) | **260** | 220 | 180 | 190 |
| Hull | **4,200** | 2,600 | 1,800 | 1,600 |
| Shield (recharge /s) | **1,500** (60) | 1,300 (70) | 900 (**80**) | 1,000 (**80**) |
| Armour (share stopped, plating) | **45 %**, 2,200 | 25 %, 800 | 20 %, 500 | 15 %, 300 |
| Cruise / attack speed | 80 / 110 | 200 / 280 | 260 / **420** | 230 / 320 |
| Turn rate | 12 | 30 | **70** | 40 |
| Acceleration | 14 | 45 | **150** | 60 |
| Sees (your ships) | 25 km | 30 km | 20 km | **40 km** |
| Guns | 6 turrets: 75 a shell, every 3.2 s, 5 km; 4 CIWS: 5 a round, every 0.12 s, 1.8 km, swinging 150° | 2 turrets: 75 a shell, every 3 s, 4.5 km; 2 CIWS, as the Assault's | 2 autocannon turrets: 14 a round, every 0.6 s, 2.5 km; chin guns (2 barrels): 14 a round, every 0.45 s, 2.2 km, within 35° of the bow | 2 light turrets: 14 a round, every 1 s, 3.5 km |
| Point defence | its CIWS, 2.7 times as hard on a missile: two rounds bring one down | its CIWS, likewise | its chin guns, 2.5 times as hard on a missile: one round brings one down | none |
| Missiles | 4 every 26 s, 8 km (12 km at a painted target), 150 each | none | none | none |
| Drones | a bay of 6: 2 a launch every 15 s, at most 3 out, within 9 km | none (it restocks the Assault's) | none | none |
| Marines | none | 3 boarding pods every 25 s, 3 km, at a disabled ship; 8 to 12 aboard each pod that gets through | none | none |
| Warp inhibitor | none | 12 km | none | **15 km** |
| Targeting laser | none | none | none | **20 km** |
| Tactics | closes bow on to 3.2 km and holds there (full speed beyond 3.5 km, stopped inside 2.6 km), its armour taking what comes; drones out on their own; missiles at a painted target from further out | keeps 1.5 km from the nearest Assault frigate, between it and you (3 km off you with none within 20 km), its CIWS over both; marines at a disabled ship within 3 km; a drone back in the bay of an Assault frigate within 2.5 km every 20 s | goes for the lightest it sees: your fighters and drones, then a wingman or fleet ship, then your lightest ship; attack runs (breaks off at 400 m, extends 2 km); under 30 % hull, runs from its prey at full speed | holds 9 km off you and 2.25 km to one side, its laser on you while it sees you; runs at full speed from you inside 4 km |
| Respawn | 120 s | 90 s | 75 s | 90 s |

### Where they are

| Group | Ships | Where |
| --- | --- | --- |
| Imperial task force | 2 Assault, 2 Patrol, 2 Raiders, 1 Recon | The Testing System: holding Brontes, Jovara's moon, 60 % of the way out from its surface to where a warp drops you, on the side away from the star; the Assault and Patrol frigates 3.2 to 4.6 km from the middle, the Raiders 6 km, the Recon 11 km; spotting you at 14 km. On the map: danger near, "Boss" |
| Imperial patrol | 1 Patrol, 1 Raider, 1 Recon (each "Arges") | The Testing System: by Arges, Jovara's other moon, likewise; spotting you at 12 km. "Harder" |
| Imperial forward base | 2 Assault, 2 Patrol, 1 Raider, 1 Recon (each "Emim", "Emim Two") | The Nine Hells: holding Emim, Rephaim's moon, half-way out from it to where a warp drops you, the Recon 9 km out from the line; spotting you at 14 km. "Boss" |
| Imperial battle group | 1 Assault, 2 Patrol, 2 Raiders, 1 Recon (each "Battle", "Battle Two") | The Nine Hells: on patrol round Rephaim, 1.7 times its arrival distance out, once round in 5 hours, seen moving on the system map as a hostile patrol; spotting you at 12 km. "Harder" |

A band springs when one of its ships, still in hiding, has a ship of yours within its spotting
range and in sight, or when one of them is shot. Each ship comes back at its place its respawn
time after it is destroyed (straight into the fight if its band is fighting); the Assault
frigate's drone stock fills again when it does. Their warp inhibitors work whether the band is
fighting or still in hiding.

### Roles

- **Assault frigate, the line.** The heaviest ship of either new faction: 4,200 hull, a 1,500
  shield and armour that stops 45 % of what gets through it, worn away slowly (2,200 of plating).
  It closes bow on to 3.2 km and holds there, six turrets on you. Its four CIWS take your missiles
  aimed at any Imperial ship within 1.8 km, and your fighters and drones, before they go back to
  its target. Its missiles go four at a time from 8 km, or from 12 km at a ship the Recon paints.
  In a fight its bay launches combat drones (the pirates' drone, flown as a fighter: 300 hull, its
  blasters), two at a time, three out at most, while a ship of yours is within 9 km; those that get
  back are stock again, and those destroyed are gone till a Patrol frigate restocks the bay or the
  Assault frigate respawns. It is slow and turns slowly, so a fast ship can choose its fight.
- **Patrol frigate, escort and screen.** It keeps 1.5 km out from the nearest Assault frigate
  towards you, its two CIWS over both. Its warp inhibitor stops your warp within 12 km of it
  ("Warp inhibited by ..."); docking, landing and cruise still work. Once your ship is disabled
  (its engines out, or its shield down and its hull under half) and within 3 km, it launches its
  marines: three boarding pods every 25 s. A pod that reaches you with your shield down or under
  a quarter puts 8 to 12 marines aboard, and boarders who bring your crew down to its survivors
  take the ship, its crew prisoners of the Empire. Every 20 s, fighting or not, it puts a drone
  back in the bay of an Assault frigate within 2.5 km of it.
- **Raider, fast attack.** The fastest ship of either new faction (420 m/s, turning 70). It goes
  for the lightest thing of yours it can see: your fighters and drones first, then a wingman or a
  ship of your fleet (the one with the least hull, as built), then the player's ship with the least
  hull. It makes fighter's attack runs on it, breaking off at 400 m and extending 2 km, its
  autocannon turrets firing as it goes. Its chin guns are point defence too: a missile of yours on
  its way to any Imperial ship, within 2.2 km and 35° of its bow, is brought down by a single
  round. Under 30 % hull it runs from what it was raiding at full speed.
- **Recon frigate, scout and designator.** It sees furthest of any enemy (40 km), and holds 9 km off
  you and a little to one side, its targeting laser on you while it sees you (the HUD warns you,
  and says from where). Painted, you are in reach of the Assault frigates' missiles from 12 km
  rather than 8. Its warp inhibitor reaches 15 km, so from where it holds you can't warp away. It
  has only light guns and no CIWS, and runs at full speed from you inside 4 km. Kill it first, or
  knock out its weapons (its laser goes off with them).

### How they fight together

A band's phase (`EmpireFleet`) is the furthest along of what its ships are doing. Like the Greys'
it holds no ship back: from the moment the band springs each fights by its own tactics, and the
fleet decides only when the band falls back.

| Phase | When | What it means |
| --- | --- | --- |
| Patrol | not sprung, or none of the others below | lying in wait, or hunting where you were last seen |
| Contact | the Recon's laser is on one of your ships | the Assault frigates' missiles reach half as far again |
| Line | an Assault frigate has a target | the line of battle: the Assault frigates bow on at 3.2 km, drones out, the Patrol frigates screening |
| Raid | a Raider has prey | the Raiders on their runs at your light craft and ships |
| Board | a Patrol frigate's target is disabled | the marines' pods on their way |
| Retreat | the band fighting with under half its ships left | it falls back in order (below) |

- **Hunting by sensors.** Each ship goes for a ship of yours within its sensors (20 to 40 km).
  Lost from sight, it searches where it saw you last, and after 30 s gives you up. The Assault,
  Patrol and Recon frigates take the nearest ship of yours they see; the Raiders choose as above.
- **Falling back in order.** Under half its ships left (3 of the task force's 7, 2 of the forward
  base's or the battle group's 6, 1 of the patrol's 3), the band says so ("Imperial task force
  falling back in order"). The Raiders and the Recon break off at once, back into hiding. The
  Assault frigates fly away from you at cruising speed, guns still on you, and the Patrol frigates
  keep screening them. The band doesn't spring again till every one of its ships is back.
- **Co-op.** The host's game runs the bands and their tactics, and the others show them; each
  game's warp drive sees the host's ships' inhibitors, and the drones are made in every game. With
  more players every Imperial ship's hull and shield are tougher (×1.6 with two players, ×2.2 with
  three) and its hits harder (×1.12, ×1.24), as every enemy's are.

## Not ships, but they fight

Stations and installations aren't covered here, but some of them fight. Their profiles are in the
game's target read-outs.

- **Cinder Orbital Battery:** three gun platforms and a beam lance over Cinder's pole.
- **Corsair strike satellites:** an ion cannon over Port Ochre and a mass driver over Pyrois
  Domes. They bombard those settlements every 30 and 20 minutes until destroyed (put back after half an hour).
- **The Vrask siege of Port Ochre:** the Vrask Siege Group (a Maw and two Skinks, above) raids
  the city from orbit every two hours, on the game's clock whether anyone is near or not:
  four shells 500 m round on its fixed defences first (it has none: Ochre isn't hostile), then on
  its buildings (rubble for an hour). One raid in ten, at most every six hours, is a nuclear
  strike: Ochre has air, so the blast covers the whole city, every building is ruined for eight
  hours and half its people left are killed. Destroy the Maw to stop it for half an hour. In
  co-op every game in the star system applies the host's strikes (one in another level when the host strikes doesn't).
- **Ground defences:** batteries, missile sites, hover tanks, sea bases and submarines on the
  planets (`ground_units.json`), and:

  | Unit | What it is | Hull | Speed (patrol / chase) | Weapons |
  |---|---|---|---|---|
  | Pirate LAV | Six-wheeled light armoured vehicle with a small turret; keeps to dry land. Four with a fortress, troops of 2-3 on patrol | 200 | 16 / 42 m/s | Auto-cannon, bursts of 5 (12 each), 2.6 km |
  | Mobile Launcher | Truck with a tilting box of six missile tubes on a turntable: drives, stops, raises it, fires, lowers it, moves on. Two with a fortress | 260 | 9 / 20 m/s | 2 salvoes of 4 guided missiles (120 each) a stop, 6 km |
  | Pirate Gunboat | Surface patrol ship on a hostile water world's sea, in 25 m of water or more: bridge, mast, funnel, a gun fore and aft, anti-air missile box. 2-3 off a coastal base, more on patrol | 1100 | 10 / 18 m/s | Twin deck guns (30 each), 3.8 km; 2 anti-air missiles (110 each) every 12 s, 5.5 km |
