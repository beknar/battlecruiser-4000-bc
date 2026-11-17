# All ships: statistics, capabilities and roles

Every ship in Star System, the player's and the enemies', as the game builds them today. The
numbers come from the game's data and its scene builder:

- player ships: `Assets/Carrier/Data/frigates.json` and the hardpoint files beside it
  (`frigate_*_hardpoints.json`, `destroyer_*_hardpoints.json`, `cruiser_sovereign_hardpoints.json`);
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

There are eleven: five frigates to choose from on the start menu, and five destroyers and a
cruiser built at a shipyard. You fly one at a time; ships you've built can sail with you, crewed (or
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

### Destroyers and cruiser (built at a shipyard)

These are built at a shipyard: Ochre Foundry, Mnemos Works, Kestrel Deep Yard, Belt Smelter,
Steropes Heavy Industries or Ochre Orbital Works. Their missiles fire from launch cells and are
refilled when docked or landed at a site. The destroyers have no bow beam; the cruiser does.

| | Halberd | Rapier | Claymore | Bulwark | Stiletto | Sovereign |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Class | Missile destroyer | Fast destroyer | Gun destroyer | Heavy destroyer | Strike destroyer | Heavy cruiser |
| Length (m) | 300 | 280 | 300 | 290 | 270 | 420 |
| Hull | 12,000 | 11,000 | 13,000 | 15,000 | 11,500 | **26,000** |
| Shield | 10,000 | 9,500 | 10,500 | 12,000 | 9,000 | **20,000** |
| Shield recharge (/s) | 320 | 340 | 330 | 300 | 350 | **450** |
| Hull + shield | 22,000 | 20,500 | 23,500 | 27,000 | 20,500 | **46,000** |
| Max speed | 108 | **112** | 105 | 100 | 110 | 90 |
| Acceleration | 17 | **19** | 16 | 15 | 18 | 12 |
| Turn rate | 12 | **13** | 11 | 10 | 12 | 8 |
| Guns | 5 turrets (quad forward) + 3 ball mounts | 4 turrets + 3 ball mounts | 3 heavy single turrets + 5 ball mounts | 4 twin turrets + 4 flank mounts | 1 big dorsal turret + 6 ball mounts | 8 turrets + 4 ball mounts |
| Gun damage / shot | 70 | 65 | 120 | 95 | 80 | **125** |
| Gun interval | 0.75 | 0.6 | 0.9 | 0.85 | 0.7 | 0.8 |
| Gun range | 5,000 | 4,800 | 5,400 | 5,200 | 5,000 | **5,600** |
| Bow beam (dmg/s) | none | none | none | none | none | **3,200** (16 s charge) |
| Missiles (cells) | 24 (6) | 12 (4) | 12 (6) | 16 (4) | 20 (8) | **32** (8) |
| CIWS mounts | 2 | 1 | 2 | 2 | 1 | **6** |
| Cargo (t) | 900 | 700 | 850 | 1,100 | 750 | **1,600** |
| Part slots | 5 | 5 | 6 | 6 | 5 | **8** |
| Crew | 380 | 360 | 420 | 460 | 350 | 800 |
| Shipyard time | 10 min | 9 min | 10 min | 11 min | 9 min 20 s | 20 min |
| Steel plate (t) | 150 | 120 | 160 | 200 | 120 | 400, plus 100 t titanium billet |

Every one also needs parts: hull frames, turret mounts, missile cells, a bridge module, crew
quarters, a reactor core, fusion thrusters, a shield emitter and armour plating. On top of those,
the Halberd and Stiletto need fire control, the Rapier vectoring jets and the Claymore rail barrels.
The Sovereign needs a capacitor bank, a beam lens and fire control. `industry.json` has the tonnes.

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

### The player fleet at a glance

| Want | Take |
| --- | --- |
| Speed, scouting, catching fighters | Lancer, then Rapier |
| Killing capital ships quickly | Talon (beam), Stiletto or Halberd (missiles), Sovereign |
| Taking punishment | Warden, Vanguard, then Bulwark, then Sovereign |
| Fighter and drone defence (CIWS) | Sentinel, Warden or Vanguard (4 CIWS each), Sovereign (6) |
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
| Missiles (250 each) | none | none | 3 every 20 s, 4.5 km | 2 every 18 s, 5 km | 4 every 16 s, 6 km |
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
| Tactics | stops 1.8 to 2.2 km off (1.45 to 1.8 km while holding), bow on; ion on your shield generator, then your engines; its tractor once they are out; breaks away inside 900 m | goes for a held or disabled ship; in fast from above, below or behind a Vrask ship; guns on your weapons; clamps on with your shield down, casts off when its boarders are done or its hull is under 30%; attack runs (breaks off at 450 m, extends 1.6 km) while your shield holds | holds 8 to 12 km from the target behind the others, bow on, hardly moving; fires only at what another Vrask ship sees within 6 km; salvos on a beat shared with any other Maw; shells on your shield generator, then your weapons (at an installation, its generators and batteries); falls back to its escorts inside 3 km; a nuke at a big ship whose shield holds, or ships bunched | screens the Maw: stands off 1.5 km from the target, never more than 2.8 km from the Maw |
| Respawn | with the spawns (below) | | | |

### Where they are

| Group | Ships | Where |
| --- | --- | --- |
| Vrask raiders | 2 Snares, 1 Fang, 1 Maw, 2 Skinks | Hidden round Comet Mirren, 2 to 3 km from it, spotting you at 4 km (a warp drops you 8.5 km out, so they spring as you close in to mine it) |
| Vrask Siege Group | 1 Maw, 2 Skinks | On station 160 km over Port Ochre, turning with Ochre; a hostile destination. Not piloted: the guns open up within 12 km, the Maw turns in place to bring its artillery to bear on a ship its Skinks see. It raids the city every two hours (see Not ships, but they fight). The Maw is back 30 minutes after it is destroyed |

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
  within 6 km, so it sits 8 to 12 km back behind the others and shells the ships they find, its
  salvos together with any other Maw's. The shells are slow (15 to 25 s in flight) and burst
  500 m round where you were going to be: change course when one is on its way. A nullifier
  round strips shields, a penetrator goes through armour. It saves its two nuclear missiles for
  a big ship whose shield holds, or a bunch of ships; a nuke breaks a frigate's shield but in
  space doesn't destroy a whole one, and the CIWS can shoot it down. It is the slowest ship in
  the system bar the dreadnought, and turns slowly to aim; close within 3 km and it falls back
  to its escorts. Kill its spotters, or get round it, past the Skinks.
- **Skink, escort.** The fastest and most agile of them, with many light guns: a nuisance alone, a
  screen for the Maw. It fights on its own once the Maw is destroyed.

Later work gives them point defence against missiles and fighters and fleet tactics (the Skink
and the group). The places on their hulls for those are in the models' hardpoint files already.

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
  planets (`ground_units.json`).
