# Improvements 7: the plan

This plan covers `docs/improvements7.txt`: two new factions, the Greys and the Empire, each four
frigates made from the AI models in `ai models/`, placed in both star systems, with every
capability their roles call for. It splits the work into eleven PRs, done in order, with the model
work running alongside the code. After the last one is merged, the shipping build is made.

The plan was reviewed against the code before any work started. "Changed after review" marks what
that review changed.

**Assumptions:**
- "The test and the pit star systems" are the Testing System (scene `StarSystem`) and the Nine
  Hells (scene `NineHells`), the only two systems in the game.
- Both factions are hostile to the player, as the Vrask are. Every hostile is on one side today:
  `Projectile.Friendly` makes hostile shots harmless to other hostiles, and enemies only ever
  target players. So the Greys and the Empire don't fight the pirates, the Vrask or each other.
  Giving factions their own wars needs a relationship table, a new target picker and changes to
  every tactic; that is left for a later list.
- The last line of the list ("If there are opportunities to do parallel actions.") is read as:
  run work in parallel where it can be. See "Parallel work" below.

## What exists, and what's missing

The research before the plan found:

| Capability the roles call for | Today |
|---|---|
| Sensors, detection range, stealth | **Missing.** Enemies always know where every player is (`PlayerShips.Retarget`). The player sees every enemy inside a flat 30 km (`TargetDisplay.MarkerRange`). The only "hidden" state is an ambush lying in wait (`CombatPilot.IsHidden`). |
| ECM, ECCM, jamming, comms | **Missing.** Aim is perfect apart from a fixed spread. Missiles home on `target.position` with nothing to fool them. Orders to wingmen and fleets are instant, with no link to cut. |
| Ghost contacts, masking | **Missing.** |
| Target designation | **Nearly.** The Vrask Maw holds its artillery until another Vrask ship "spots" the target (`MawTactics.IsSpotted`). Nothing else uses it. |
| EMP | **Partly.** `ShipSystems` knocks out weapons, engines and shields below 25% condition, and everything obeys it. Ion damage hurts components. There is no timed knockout and no EMP weapon. |
| Point defence against missiles and drones | **Yes, for the Vrask only** (`PointDefence`, the Skink). It shoots; nothing disrupts electronically. Player drones (`Craft`) have no `Health`, so they can't be hurt. |
| Tractor, boarding, capture | **Yes** (`TractorBeam`, `BoardingPod`, `Boarders`, `Health.Capture`). |
| Ablative armour | **Yes** (`Armor`, `AblativePlates`, from PR #110). |
| CIWS | **Yes, for the player** (`CiwsDirector`). Enemies have none, apart from the Skink's guns. |
| Missiles | **Yes** (`EnemyWeapons` salvos). |
| Enemy drones | **Placed only.** `EnemyDrone` ships are put in the level by the builder. Nothing launches drones in a fight, and co-op registers enemies once at the start. |

A faction today is wired through about forty places that say "Vrask" by name: `VraskShip`,
`VraskStats` (a hard-coded `Data/vrask.json`), `CarrierBuilder.Vrask`, the shared boarding,
artillery and escort builders, `PointDefence.AtVrask`, the co-op host-only list, the battle sim's
roles, the CIC's role letters and the "captured by the Vrask" texts.

## The two factions

Names are the role words from the list. Each faction gets its own colours, so they read apart from
the green Vrask on screen.

### The Greys: violet and pale blue

| Ship | Model | Hull | Role, in short |
|---|---|---|---|
| **Grey Scout** | `greys-scout-frigate.glb`, a plain saucer | light | Stealth. Sees furthest. Paints targets for the others. Calls the rest in. Hit-and-run with precision beams and two drones, then cloaks. |
| **Grey Shroud** (ECM) | `greys-ecm-frigate.glb`, a saucer with a dish and a fin | medium | Jams sensors, targeting and comms over 6 km. Puts ghost contacts on the player's screen. Its emitters scramble missiles and drones close in. Short-range EMP. |
| **Grey Lance** (strike) | `greys-strike-frigate.glb`, a manta | medium | Fast. Opens from the flank or rear with a heavy energy salvo, breaks off. Runs down cripples. |
| **Grey Reaper** (boarding) | `greys-boarding-frigate.glb`, a saucer on grappling legs | heavy | Locks on with tractor emitters, then abducts the crew, samples and cargo of a disabled ship. Also raids stations and settlements for people. Light guns. |

How they fight together (`GreysFleet`):
1. **Watch:** the Scout shadows a player, cloaked, beyond their sensors.
2. **Paint and call:** it paints the target and calls the group in.
3. **Blind:** the Shroud moves to 4 to 6 km and jams. Ghost contacts appear.
4. **Strike:** the Lances hit from the flanks, aiming for engines and weapons, with an EMP from the
   Shroud.
5. **Take:** the Reaper locks on to a disabled ship and abducts.
6. **Vanish:** everyone cloaks and withdraws once the Reaper has its prize, or once the Shroud
   or the Reaper is lost.

### The Empire: imperial white hulls with red

| Ship | Model | Hull | Role, in short |
|---|---|---|---|
| **Imperial Assault Frigate** | `empire-assault-frigate.glb` | heavy, ablative armour | The line. Turrets, a missile battery, a CIWS, and a bay of combat drones. Built to soak energy and kinetic fire. |
| **Imperial Patrol Frigate** | `empire-patrol-frigate.glb` | medium | Escort and screen: a CIWS against fighters, missiles and drones. Fast patrols. A sensor platform with ECCM. Carries marines in boarding pods. |
| **Imperial Raider** | `empire-raider-frigate.glb` | light | Fast attack. Goes for the lightest targets (drones, fighters, wingmen, haulers) and gets out before the heavies arrive. Intercepts missiles. |
| **Imperial Recon Frigate** | `empire-recon-frigate.glb` | light | Scouts and paints targets for the fleet's missiles. Strong ECCM, so it breaks through Greys-style jamming and stealth for the whole fleet. Light fire support. |

How they fight together (`EmpireFleet`): a disciplined battle line, unlike the Greys' ambushes.
1. **Patrol:** a wide sweep, the Recon and Raiders ahead.
2. **Contact:** the Recon paints. The fleet's missiles fire from further out at a painted target.
3. **Line of battle:** the Assault frigates close head-on with drones out. The Patrol frigates
   screen them with CIWS.
4. **Raid:** Raiders cut in at the player's drones, fighters and wingmen.
5. **Board:** a disabled player ship gets the marines.
6. **Retreat in order:** at half strength the fleet falls back, the Patrol frigates covering.

### Where they are

**Testing System:**
- The Greys watch the Outer Drift, at the system's cold edge. One Scout shadows the Jovara Trojans.
- An Empire task force is stationed among Jovara's moons, Brontes and Arges. A patrol sweeps between
  Jovara and Thalassa.

**Nine Hells:**
- **The Greys:**
  - A Greys cell picks over the dead race's ruins on Goliath.
  - They raid the allied research station on Nineveh for people.
  - A lone Scout watches Ptolomea.
- **The Empire:**
  - A forward base holds Emim (Rephaim's moon), with a picket.
  - A battle group patrols between Rephaim and Paradise.

Places can still move in their PR if the level shows a reason. For example, a band must not sit
on a friendly world, which the PR 6a fix of improvements 6 now checks.

## The PRs

Each PR goes: branch off main, code, compile, test in the game (windowed, one run at a time),
review the diff, fix what the review finds, merge.

There are eleven PRs. The review split the old PRs 3 and 4 into four, and added the co-op layer
for enemies made during play as a PR of its own (changed after review).

### PR 1: The eight models (runs alongside PRs 2 to 8)

This is Blender work only, with no Unity, so it runs in its own worktree while the code PRs go
ahead.

- **Specs:** `Tools/Blender/greys_spec.json` and `empire_spec.json`, in the layout of
  `lizard_spec.json`. Each ship has:
  - Alignment: the long axis for the Empire hulls. For the saucers, the front is chosen: the
    Scout's hatch, the Shroud's dish, and the Reaper's widest pair of legs. The Lance's nose is its
    bow.
  - Length. The Greys are 110 to 160 m and the Empire 180 to 260 m, near the Vrask (170 m).
  - Turret boxes cut from the hull. Most of these models are one mesh, so faces in a box are cut,
    as for the Vrask pods.
  - Engines and fixed guns.
  - Role points: `designator`, `sensor`, `ecm`, `emp`, `tractor`, `abduct`, `drone_bay`, `ciws`,
    `missile_cell`, `boarding_pod`.
- **Conversion:** `convert_lizards.py` already loops over any spec. Shell scripts
  `build_greys.sh` and `build_empire.sh` call it.
  - Output: `Models/Greys_<Id>.fbx` and `Models/Empire_<Id>.fbx`, their `_BaseColor.png`, and
    `Data/greys_<id>_hardpoints.json` / `empire_<id>_hardpoints.json`.
  - These are AI models of the user's own, not from the licensed Kit, so they are committed, as
    the Lizard models are (through LFS).
  - A Vrask rebuild must still give the same files.
- **Check:** preview renders of each ship with its points marked, looked at before the PR goes up.

### PR 2: Factions as data, not as the Vrask

No change in how the game plays. Every baseline must come out the same.

- **Data:**
  - `FactionStats` loads any faction file. `VraskStats` becomes a thin wrapper, so its callers
    don't change.
  - The JSON classes are shared. The Greys and the Empire add their new fields there, unused by
    the Vrask.
- **Ships:** `FactionShip`, a base class with:
  - the faction, a role string, the points, `sensorRange`, the plate count and the shield going
    down on death.
  - `VraskShip` derives from it and keeps its `Role` enum.
  - `FactionShip.All` lists every faction ship. `VraskShip.All` stays for the Vrask code.
- **Builder:**
  - `CarrierBuilder.Vrask`'s ship-making becomes `CarrierBuilder.Faction`. It covers the hull
    material, cut turrets, fixed guns, points, engines, the shield bubble (tinted by the faction's
    colour), breakup chunks, health, armour, plates and target info.
  - Builder names that say Vrask (boarding pod, pod trail, artillery shell, nuke) take the
    faction's prefix.
  - `AddFleet` takes the fleet type.
- **Point defence:** `PointDefence` protects ships of its own faction, not only Vrask ones.
- **Co-op:** the host-only list and `Keep` become a list each faction adds to.
- **Who captured you** (changed after review):
  - `Boarders`, the co-op Board message and the `cpt` field carry the capturing faction.
  - "Captured by the Vrask" in `CarrierController.Boarding`, `FleetDirector`, `RemotePlayer` and
    `TargetDisplay` names whichever faction it was.
  - Prisoners are saved as a list of {faction, count}, since JsonUtility has no dictionaries. An
    old save's single number loads as Vrask prisoners.
- **The CIC display, the battle sim's section targeting and `VraskMix`** (changed after review):
  `TacticalDisplay.Begin`, `BattleSim`'s `RoleWeight` and `VraskMix.Describe` take
  `FactionShip`. Role letters come from the faction.
- **Tests:**
  - All existing tests and baselines pass unchanged.
  - A new edit-mode test loads `vrask.json` through `FactionStats` and checks it matches.
  - A new edit-mode test round-trips an old save's prisoners.

### PR 3: Co-op enemies made during play (new after review)

Ghost contacts are not enemies after review, so only drones need this. The Scout's drones and
the Empire's drone bays do.

- **Messages:**
  - A spawn message carries the enemy's id, a prefab key, position and owner.
  - A despawn message removes it.
  - The host keeps a counter for runtime ids, apart from the scene-path ids.
- **Prefab registry:** a list of what can be spawned in play, by key (`Resources`, built by the
  builder). A guest makes the puppet from it.
- **Hookup:**
  - The new enemy goes into `ents` and `entByHealth`, with the `Health.Intercept` hookup.
  - `CoopScaling` is applied as it's added.
  - It becomes a puppet with the `Keep` rules.
- **Old guests:** they ignore the spawn message and the ids they don't know. They would still take
  `HitPlayer` damage from a source they don't have, so hits from unknown sources are dropped.
  - The join carries a content tag. A guest without the new factions is told so and plays without
    them.
- **Bandwidth:** `SendEntities` sends every enemy at 10 Hz with string ids, and the hub
  disconnects a player whose queue overflows.
  - Entities more than 60 km from every player go at 1 Hz.
  - Before and after are measured in the co-op test.
- **Protocol:** the new fields and messages go in `Server/protocol` samples and `ProtocolTests`.
  The README says a field added on one side only fails a test.
- **Tests:**
  - The co-op self-test spawns and despawns an enemy on the host, and the guest shows and loses
    it.
  - A protocol round trip.
  - A guest's hit from an unknown source is dropped.

### PR 4: Sensors and detection

This is the base that the cloak, jamming, spoofing and painting work through.

- **`Sensors`:**
  - Every ship has a signature, 1 by default. It is lower when cloaked, higher for a while after
    firing or at full thrust.
  - Every ship has a sensor range: 30 km for the player, today's figure, from the frigate class.
  - A contact is seen when it is within range × signature × (1 − jamming), or for a few seconds
    after it fires or hits.
  - A contact is **identified** inside half that, and is only an **unknown contact** beyond it:
    a bracket with no name or class.
  - **One cache:** what each player ship sees is worked out 5 to 10 times a second, not again for
    each caller (changed after review).
  - **Contacts:** the cache keeps a contact list. Ghosts in PR 6 live there too.
- **Every player-side filter uses it in place of `CombatPilot.IsHidden`:**
  - the HUD markers, the target list and T cycling, the mouse pick, the console hostiles count,
    `FleetPilot`, and the CIC display.
  - `Wingman.NearestEnemy`, `CiwsDirector.Pick` and the co-op bot also get it. They see
    everything today.
  - **The battle sim's `SimPilot`** targets through it as well (changed after review).
- **Tells** (changed after review): an engaged enemy blocks warp ("hostiles nearby") and starts the
  combat music (`CombatPilot.AnyEngagedNear`). Both now count only enemies the player can detect.
  A cloaked Scout must not give itself away.
- **Enemies detecting players is opt-in** (changed after review). The old enemies keep today's
  path exactly:
  - `PlayerShips.Retarget` is unchanged. The pirates, grids, ground forces and the Vrask still
    know where every player is.
  - The new factions' tactics use `Sensors.CanSee(observer, target)` and lose a player they
    can't see, searching where it was last seen.
- **Test fixtures:** PRs 4 to 8 need ships with a cloak, a jammer or an EMP before the Greys exist.
  `SystemTest.Sensors` spawns plain test hostiles next to the player and adds the component under
  test. They live in `Harness/` only.
- **Tests:**
  - Edit-mode tests for detection and identify ranges, and signature after firing.
  - `SystemTest.Sensors` (`-sensortest`): a target past range is off the list and back on it inside;
    an unknown contact turns into a named one closer in; warp is not blocked by an enemy the player
    can't see.
  - Every old baseline unchanged.

### PR 5: Stealth, designation, ECCM and interdiction

- **Cloak:**
  - It brings the signature down to 0.1. While cloaked the ship shimmers when seen.
  - Firing, or a hit taken, drops it for some seconds. Recloaking takes time.
  - It works on guest puppets too (`Keep`), and its state syncs as a new `CoopEntity` field
    (0 means absent).
- **Designation:**
  - A `Designation` registry: who paints whom, from how far, and for how long.
  - The painted ship takes tighter spread from that faction's guns, and their missiles may launch
    from 1.5 times their range.
  - A painted player sees "Targeting laser: <ship>" on the HUD, with a warning tone, and can see
    where the painter is.
  - `MawTactics.IsSpotted` is left as it is. The Vrask keep their own spotting.
- **ECCM:**
  - Each frigate class has a rating (`frigates.json`): the Sentinel and Warden highest, the
    Talon lowest.
  - It cuts jamming (PR 6) and EMP time (PR 7), and sees cloaked ships from further.
  - The ship info panel shows it.
- **Interdiction** (new after review), for the role lines "interdict" and "prevent escapes":
  - An `Interdictor` field stops warp inside its radius ("Warp inhibited by <ship>"), through the
    warp block check in `WarpDrive`.
  - The Imperial Patrol and Recon carry one. The Grey Reaper's tractor lock does the same to the
    ship it holds.
- **Tests:**
  - Edit-mode tests for cloak break and recloak, paint expiry, and ECCM scaling.
  - `SystemTest.Sensors` adds: a cloaked fixture is unseen until it fires, a painter shows on the
    HUD, and warp is refused inside an interdictor.

### PR 6: Jamming and spoofing

- **`EcmEmitter` (the jammer):** a field of a set radius and strength, shown as a faint distortion
  sphere. Inside it, for a player ship (less its ECCM):
  - **Sensors:** detection range is cut, contacts drop to unknown, and markers jitter.
  - **Targeting:** turret aim takes an error (`CarrierTurret.AimPoint`), and a lock takes longer.
  - **Missiles:** each missile has a chance a second to lose lock and fly straight on. A lost
    missile reacquires if the jamming ends.
  - **Comms:** orders to wingmen and the fleet are delayed and can fail ("Comms jammed"). The AI
    wingman's replies come through broken up. Co-op chat between people is left alone.
- **Spoofing** (changed after review): ghosts are sensor contacts only, not enemies.
  - **Ghost contacts:** entries in the `Sensors` contact list that move like ships. They have no
    `TargetInfo`, `Health` or pilot, so the CIWS, the wingman, the warp block, the music and salvage
    never see them.
  - The HUD, the target list and the pick show them as unknown contacts until identified. Then
    they vanish ("Ghost contact").
  - A shot at a ghost passes through.
  - **Masking:** the jammer's escorts are shown at a false position near the real one until
    identified.
  - Each machine makes its own ghosts from the emitter's synced state and a shared seed, so nothing
    more is sent.
- **Electronic point defence:**
  - Inside 1.5 km of an active emitter, a player missile is scrambled and detonates early.
  - A player drone is knocked out for a while.
- **HUD:** a "JAMMED" band shows the effect strength. Ghosts and unknown contacts get their own
  bracket.
- **Co-op:** an emitter's on/off goes in `CoopEntity`. The emitter works on guest puppets, so a
  guest works out its own jamming.
- **Tests:**
  - Edit-mode tests: jamming falls off with distance and ECCM; a lock is lost and retaken.
  - `SystemTest.Sensors` adds: a jammer fixture cuts the player's range; ghosts appear and vanish
    when identified; the CIWS never aims at a ghost; a missile into the 1.5 km detonates early.

### PR 7: EMP weapons

- **`HitKind.Emp`**, appended at the end so old saves and co-op messages keep their numbers
  (changed after review):
  - A row in `DamageTable.rows` and a `Label`.
  - `Legacy()` sends it to an older game as Ion, the nearest kind, not as a full-damage Cannon hit.
  - Little hull damage. It goes through shields at 30%, by a path like `IonThroughShield`.
- **Timed knockouts** (changed after review):
  - A separate `knockedUntil` time per system, ORed into `IsDown`, `WeaponsDown` and
    `EnginesDown`. The repair tick's `Set` would clear the `down` flags.
  - `Apply` runs when a knockout starts and when it ends.
  - The knockout is ORed into `Pack()`, so the existing down bits carry it in co-op with no new
    field.
  - A ship's ECCM shortens the time.
- **Weapons:**
  - `EmpProjector`: a short-to-medium range bolt that picks a system.
  - `EmpBurst`: a sphere around the ship (the Shroud), with a charge-up the player can see and
    escape.
- **HUD:** each downed system shows its seconds back. "EMP" appears on the damage notice.
- **Tests:**
  - Edit-mode tests:
    - an EMP hit downs the chosen system for the time, less with ECCM;
    - the repair tick doesn't end it early;
    - the legacy mapping;
    - a burst misses a ship out of range.
  - `SystemTest.Sensors` adds: an EMP fixture downs the player's weapons, and they come back on
    time.

### PR 8: Enemy drones and CIWS

- **Drone bays:** `DroneBay` launches combat drones from a ship's `drone_bay` points during a fight
  and recovers them after.
  - Drones are small `CombatPilot` fighters, `TargetInfo.Kind.Drone`, so the player's CIWS
    engages them.
  - Each bay has a stock, so lost drones are replaced only up to it.
  - They go over co-op through PR 3.
- **Player drones and fighters get `Health`** (only here, changed after review), so enemy CIWS and
  PR 6's electronic defence can kill them. Today Skink guns aim at them but can't hurt them.
- **Enemy CIWS:** `PointDefence` from PR 2, set up as a CIWS.
  - Faster guns, shorter range.
  - It engages the player's missiles, drones and fighters.
- **Kit models:** the drone hull and plumes (changed after review).
  - Enemy drones reuse the `EnemyDrone` prefab's look. Its chunks come from Kit models and are
    already gitignored, as `EnemyCruiser` and `EnemyFighter` are.
  - Any new chunk or mesh baked from a Kit model gets its `.gitignore` line in this PR.
- **Tests:**
  - Play-mode tests: a bay launches up to its stock and recovers on withdraw; the CIWS downs a
    missile; a player drone can be destroyed.
  - The co-op self-test sees a launched drone on the guest.

### PR 9: The Greys

- **Data and prefabs:** `Data/greys.json` holds the bolts (violet precision beams, a heavy strike
  salvo, EMP bolts) and the four ships' numbers. `CarrierBuilder.Greys` builds the four prefabs.
  - **Scout:** cloak, designator, long sensors, two hit-and-run drones.
  - **Shroud:** ECM emitter, ghosts, masking, EMP burst, electronic point defence.
  - **Lance:** heavy salvo, high speed, EMP projector.
  - **Reaper:** tractor emitters, abduction beam, light guns.
- **Tactics:** `ScoutTactics`, `ShroudTactics`, `LanceTactics` and `ReaperTactics`, and
  `GreysFleet` running the six phases above. They see through `Sensors` (PR 4).
  - **Scout** (changed after review):
    - It shadows the player at the edge of their sensors.
    - When a heavier threat closes it evades, cloaked.
    - It calls in the nearest other Greys band within 40 km, which comes to the place it's painting.
  - **Shroud:** it escorts the Reaper, its mothership, and keeps its ECM bubble on it.
  - **Lance** (changed after review):
    - It flanks and hits engines and weapons, and runs down cripples.
    - It also hits facilities (installations, defence satellites) in its way.
    - It counter-attacks the player's fighters and drones that go for the Shroud or the Reaper.
  - **Reaper** (changed after review): it locks on to a disabled ship, which holds it in place and
    stops its warp. It abducts, and can tow the ship away with it as the group withdraws. The tow
    breaks if the Reaper is hit hard or the target gets its engines back.
- **Abduction** (changed after review):
  - `Crew.Take(n)` counts the taken as prisoners of the Greys, not as killed.
  - At the crew floor, abduction stops: the Greys take people, not ships.
  - It takes cargo from the hold, and one "sample" at the end: a random ship system's condition
    halved until repaired.
  - **Co-op:** an Abduct message `{to, n, faction}`, modelled on the Board message.
  - **From places:** a raid on a station or settlement takes part of its population. The raid is
    kept in `WorldState`'s strike record, the one the Vrask siege uses, so it saves and syncs. The
    place's description shows it while it lasts.
- **Placement** in both systems (above), with `dangerNear` set on the warp points so the map
  warns. Nine Hells' and the Testing System's `about` texts name them.
- **Co-op:** the fleet's host-only behaviours are listed. The abduction beam shows like a tractor.
- **Wingman:** `wingman_dialogue.json` gets a Greys topic, `aibot_test.py` checks it, and the
  CoopAI README lists the topic.
- **Tests:**
  - `SystemTest.Greys` (`-greystest`), in the Vrask test's style:
    - the four prefabs, their points and numbers;
    - the Scout unseen at range, then seen after firing;
    - the Scout calling in a band;
    - the Shroud jams and puts up ghosts;
    - an EMP downs a system;
    - the Reaper abducts crew from a disabled ship and tows it;
    - the group withdraws when the Reaper is lost.
  - `GreysDataTests` in edit mode.
  - **Run-tests plumbing** (changed after review): a `greys` stage in `run_tests.py` (`STAGES`,
    `SELFTESTS` with a timeout) with its baseline, `counts.json` blessed, and the stage tables in
    `Harness/README.md` and README.
  - **Baselines blessed:** `ninehells`, `coop` (its enemy count), and `system`, `modes` and `nav`
    if the new bands show in them. Each change is checked by hand.

### PR 10: The Empire

- **Data and prefabs:** `Data/empire.json` and `CarrierBuilder.Empire`.
  - **Assault Frigate:**
    - Ablative armour: Armor with plates, a high rating. It shrugs off beams and kinetics alike.
    - Turrets, a missile battery, a CIWS and a drone bay.
  - **Patrol Frigate:**
    - CIWS, high speed, sensors with ECCM, and an interdictor.
    - Boarding pods with marines.
  - **Raider:** speed, guns to pick off light targets, and its guns double as point defence
    against missiles.
  - **Recon Frigate:** a designator, the longest sensors, fleet ECCM and an interdictor.
- **Tactics:** `AssaultTactics`, `PatrolTactics`, `RaiderTactics` and `ReconTactics`, and
  `EmpireFleet` running the six phases above.
  - **Raiders pick light targets first:** drones and fighters, the wingman, then the lightest
    frigate.
  - **Boarding:** reuses the Vrask boarding (`Boarders`): marines take a disabled ship. "Captured
    by the Empire".
  - **Light logistics** (changed after review): a Patrol frigate near an Assault frigate restocks
    its drones and missiles. "Light troop/vehicle delivery" is its marines. Landing troops on
    worlds is left out, because the ground war isn't tied to fleets.
- **Placement** in both systems (above). The forward base at Emim is a station with defence
  satellites, like the Sheol outpost.
- **Co-op and wingman:** as for the Greys.
- **Tests:**
  - `SystemTest.Empire` (`-empiretest`):
    - the four prefabs, their numbers and points;
    - armour and plates on the Assault frigate;
    - drones launched in a fight;
    - the CIWS downs a player missile;
    - the Recon paints and the fleet fires from further out;
    - the Raider goes for a drone before a frigate;
    - the Patrol restocks drones;
    - warp is refused near an interdictor;
    - the fleet retreats at half strength.
  - `EmpireDataTests`, an `empire` stage with its plumbing and baseline, and the baselines blessed
    as for the Greys.

### PR 11: Balance, docs and the battle sims

- **Battle sims:** the battle simulator (`-battlesim`, `-simduo`) learns the new factions' roles,
  for its section targeting and a mix option per faction.
  - The sim pilots target through `Sensors` (PR 4), so stealth and jamming count.
  - The sims run on the remote hosts, in parallel with the docs work.
  - The fights:
    - two player frigates against a Greys cell, against an Imperial patrol, and against an
      Imperial battle group;
    - every new band, through `-battlesim`.
  - Each new fight should be winnable by two frigates most of the time, as the Vrask picket is,
    and should not last much longer than the Vrask fights. Numbers are tuned in the JSON until it
    is.
  - Results go into `docs/frigates-performance.md`.
- **Docs:**
  - README: the factions, their ships and where they are; the new mechanics (sensors and
    stealth, designation, ECM and ECCM, interdiction, EMP, enemy drones and CIWS); the HUD; the
    new test switches.
  - `docs/allships.md`: a section for each faction.
- **Shipping build:** after this PR is merged, the Windows, web and Linux release is built, as
  last time (`BuildAll` with `-nographics`, then the release zips). No release is published and
  nothing is deployed unless asked: the server stays off.

## Parallel work

- **PR 1** (Blender, no Unity) runs in a worktree from the start, alongside PRs 2 to 8 (Unity
  code). Its FBX files are needed only from PR 9.
- **Battle sims** run on the three remote hosts (.35, .61, .58) while the game is tested on the
  laptop.
- **Reviews:** each PR's diff is reviewed by a separate agent while the next PR's code starts.
- **Kept serial:** only one Unity editor or game process runs on the laptop at a time (the GPU
  rule), so building and in-game tests stay serial.

## Risks

- **The saucers have no front.** Their front is a choice, and a saucer flown like a hull can look
  odd. The Greys get a `holdFacing` style that glides sideways, like the Snare.
- **Detection changes everyone.** PR 4 keeps the old enemies on today's
  path, and every baseline must come out the same before PR 5 builds on it.
- **Co-op enemies made during play** (ghosts, drones) are new to the protocol. A guest on an
  older version ignores what it doesn't know, which is the protocol's rule.
- **Balance:** new fights are tuned only through sims. The sims can't judge whether the jamming
  feels fair to a human, so that is a thing to try in play.
