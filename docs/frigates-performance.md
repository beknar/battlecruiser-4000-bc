# Frigate performance against the hostile encounters

From the battle simulator: **9220 battles**: every fleet of 1 to 6 ships drawn from the five frigate classes (Sentinel, Lancer, Warden, Talon, Vanguard; the order not counting, a class may repeat: 461 fleets), as built (default fittings, full crews), against each of the Testing System's 10 hostile encounters in space, with the bow beam (the main gun) and without.

Each battle was fought where the encounter waits, the fleet coming in line abreast from 9 km, its frigates flown as a player would (`SimPilot`: every gun on one enemy, broadside for the turrets, bow on to charge and fire the beam in its reach). A battle ends in a **win** (every enemy destroyed, or a Vrask band that gives up and withdraws), a **loss** (every frigate destroyed or captured) or a **timeout** (still undecided after 10 minutes). Recorded with `StarSystem.exe -batchmode -nographics -battlesim <dir>`; this report is `python Tools/BattleSim/report.py <dir>/battles.csv`.

A battle's **score** (0 to 100) rates how well it went: a win counts 60, plus up to 25 for the frigates still in the fight, 10 for the hull left and 5 for being quick; a timeout or a loss counts what it destroyed before it ended.

## Overall

| | Battles | Won | Lost | Timed out |
|---|---|---|---|---|
| With main gun | 4610 | 4605 (100%) | 0 (0%) | 5 (0%) |
| Without main gun | 4610 | 3767 (82%) | 9 (0%) | 834 (18%) |
| **All** | 9220 | 8372 (91%) | 9 (0%) | 839 (9%) |

## Each encounter, by fleet size

Wins out of the fleets of that size (with the main gun / without). Fleets of each size: 1: 5, 2: 15, 3: 35, 4: 70, 5: 126, 6: 210.

| Encounter | 1 | 2 | 3 | 4 | 5 | 6 | Smallest fleet that always wins (gun / no gun) |
|---|---|---|---|---|---|---|---|
| Cinder Orbital Battery | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 1 / 1 |
| Corsair Haven | 80% / 0% | 100% / 0% | 100% / 0% | 100% / 0% | 100% / 0% | 100% / 0% | 2 / - |
| Corsair Ion Satellite | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 1 / 1 |
| Corsair Mass Driver | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 1 / 1 |
| Corsair patrol | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 100% / 100% | 1 / 1 |
| Dreadnought battle group | 100% / 40% | 100% / 60% | 100% / 71% | 100% / 79% | 100% / 84% | 100% / 88% | 1 / - |
| Pirate cruiser | 80% / 40% | 100% / 60% | 100% / 71% | 100% / 79% | 100% / 83% | 100% / 87% | 2 / - |
| Raiders | 40% / 40% | 100% / 33% | 100% / 66% | 100% / 73% | 100% / 78% | 100% / 82% | 2 / - |
| Vrask Siege Group | 100% / 40% | 100% / 60% | 100% / 69% | 100% / 79% | 100% / 83% | 100% / 88% | 1 / - |
| Vrask raiders | 100% / 0% | 100% / 67% | 100% / 89% | 100% / 90% | 100% / 97% | 100% / 98% | 1 / - |

## Each encounter in detail

### Cinder Orbital Battery (defences, 4 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 72 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 93 s on average, with 0.14 frigates lost.
- **Best:** Sentinel+Lancer+Warden+Talon+Talon+Talon (gun): win in 58 s, score 100; Lancer+Lancer+Warden+Talon (gun): win in 59 s, score 100; Sentinel+Lancer+Warden+Talon+Vanguard+Vanguard (gun): win in 59 s, score 100.
- **Worst:** Sentinel+Lancer (no gun): win after 131 s, 4/4 destroyed, score 86; Sentinel+Lancer+Warden (no gun): win after 112 s, 4/4 destroyed, score 91; Sentinel+Lancer+Vanguard (no gun): win after 110 s, 4/4 destroyed, score 91.
- **Alone:** Sentinel win/win; Lancer win/win; Warden win/win; Talon win/win; Vanguard win/win (with the gun / without).

### Corsair Haven (defences, 6 enemies)

- **With the main gun:** won 460 of 461 (100%), lost 0, timed out 1; wins took 158 s on average, with 0.11 frigates lost; the 1 not won destroyed 0.0 of 6 enemies on average.
- **Without the main gun:** won 0 of 461, lost 0, timed out 461; the 461 not won destroyed 3.3 of 6 enemies on average.
- **Best:** Sentinel+Sentinel+Sentinel+Sentinel+Lancer+Lancer (gun): win in 114 s, score 99; Sentinel+Sentinel+Lancer+Lancer+Lancer+Lancer (gun): win in 114 s, score 99; Sentinel+Lancer+Lancer+Lancer+Lancer+Lancer (gun): win in 115 s, score 99.
- **Worst:** Lancer+Lancer+Lancer+Lancer+Lancer+Vanguard (no gun): timeout after 600 s, 0/6 destroyed, score 0; Warden+Vanguard+Vanguard+Vanguard+Vanguard+Vanguard (no gun): timeout after 600 s, 0/6 destroyed, score 0; Lancer+Talon+Talon+Vanguard (no gun): timeout after 600 s, 0/6 destroyed, score 0.
- **Alone:** Sentinel win/timeout; Lancer win/timeout; Warden timeout/timeout; Talon win/timeout; Vanguard win/timeout (with the gun / without).

### Corsair Ion Satellite (defences, 1 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 118 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 138 s on average, with 0.00 frigates lost.
- **Best:** Lancer+Lancer+Lancer+Lancer+Lancer (gun): win in 90 s, score 99; Lancer+Lancer+Lancer+Lancer+Vanguard (gun): win in 90 s, score 99; Lancer+Lancer+Lancer+Warden+Warden (gun): win in 90 s, score 99.
- **Worst:** Vanguard (no gun): win after 236 s, 1/1 destroyed, score 98; Vanguard+Vanguard (no gun): win after 232 s, 1/1 destroyed, score 98; Vanguard+Vanguard+Vanguard (no gun): win after 230 s, 1/1 destroyed, score 98.
- **Alone:** Sentinel win/win; Lancer win/win; Warden win/win; Talon win/win; Vanguard win/win (with the gun / without).

### Corsair Mass Driver (defences, 1 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 64 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 73 s on average, with 0.00 frigates lost.
- **Best:** Lancer+Lancer (gun): win in 55 s, score 100; Lancer+Lancer+Vanguard (gun): win in 55 s, score 100; Lancer (gun): win in 56 s, score 100.
- **Worst:** Sentinel+Sentinel+Sentinel+Sentinel+Warden+Vanguard (no gun): win after 87 s, 1/1 destroyed, score 99; Sentinel+Sentinel+Sentinel+Sentinel+Sentinel (no gun): win after 87 s, 1/1 destroyed, score 99; Sentinel+Sentinel+Sentinel+Sentinel+Lancer (no gun): win after 87 s, 1/1 destroyed, score 99.
- **Alone:** Sentinel win/win; Lancer win/win; Warden win/win; Talon win/win; Vanguard win/win (with the gun / without).

### Corsair patrol (band, 3 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 38 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 85 s on average, with 0.00 frigates lost.
- **Best:** Talon+Talon+Talon (gun): win in 32 s, score 100; Talon+Talon+Talon+Talon (gun): win in 32 s, score 100; Talon+Talon+Talon+Vanguard (gun): win in 32 s, score 100.
- **Worst:** Lancer (no gun): win after 134 s, 3/3 destroyed, score 99; Lancer+Warden+Talon (no gun): win after 127 s, 3/3 destroyed, score 99; Talon (no gun): win after 127 s, 3/3 destroyed, score 99.
- **Alone:** Sentinel win/win; Lancer win/win; Warden win/win; Talon win/win; Vanguard win/win (with the gun / without).

### Dreadnought battle group (band, 3 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 85 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 382 of 461 (83%), lost 3, timed out 76; wins took 137 s on average, with 0.00 frigates lost; the 79 not won destroyed 0.6 of 3 enemies on average.
- **Best:** Lancer+Lancer+Lancer+Lancer+Warden+Talon (gun): win in 69 s, score 99; Lancer+Lancer+Warden+Talon (gun): win in 70 s, score 99; Lancer+Lancer+Warden+Talon+Vanguard (gun): win in 70 s, score 99.
- **Worst:** Lancer+Lancer+Lancer+Lancer+Lancer+Lancer (no gun): timeout after 600 s, 0/3 destroyed, score 0; Lancer+Lancer+Lancer+Lancer+Lancer+Talon (no gun): timeout after 600 s, 0/3 destroyed, score 0; Lancer+Lancer+Lancer+Lancer+Lancer+Vanguard (no gun): timeout after 600 s, 0/3 destroyed, score 0.
- **Alone:** Sentinel win/win; Lancer win/loss; Warden win/win; Talon win/loss; Vanguard win/loss (with the gun / without).

### Pirate cruiser (band, 3 enemies)

- **With the main gun:** won 460 of 461 (100%), lost 0, timed out 1; wins took 139 s on average, with 0.00 frigates lost; the 1 not won destroyed 2.0 of 3 enemies on average.
- **Without the main gun:** won 378 of 461 (82%), lost 0, timed out 83; wins took 127 s on average, with 0.00 frigates lost; the 83 not won destroyed 0.0 of 3 enemies on average.
- **Best:** Lancer+Lancer+Warden+Warden+Warden+Talon (no gun): win in 90 s, score 99; Lancer+Lancer+Warden+Warden+Vanguard (no gun): win in 91 s, score 99; Warden+Talon+Talon+Vanguard (gun): win in 92 s, score 99.
- **Worst:** Lancer+Lancer+Lancer+Lancer+Lancer+Lancer (no gun): timeout after 600 s, 0/3 destroyed, score 0; Lancer+Lancer+Lancer+Lancer+Lancer+Talon (no gun): timeout after 600 s, 0/3 destroyed, score 0; Lancer+Lancer+Lancer+Lancer+Lancer+Vanguard (no gun): timeout after 600 s, 0/3 destroyed, score 0.
- **Alone:** Sentinel win/win; Lancer timeout/timeout; Warden win/win; Talon win/timeout; Vanguard win/timeout (with the gun / without).

### Raiders (band, 6 enemies)

- **With the main gun:** won 458 of 461 (99%), lost 0, timed out 3; wins took 128 s on average, with 0.00 frigates lost; the 3 not won destroyed 4.3 of 6 enemies on average.
- **Without the main gun:** won 352 of 461 (76%), lost 0, timed out 109; wins took 176 s on average, with 0.00 frigates lost; the 109 not won destroyed 4.3 of 6 enemies on average.
- **Best:** Sentinel+Lancer+Lancer+Warden+Warden+Talon (gun): win in 60 s, score 100; Lancer+Lancer+Talon+Talon+Talon+Talon (gun): win in 60 s, score 100; Lancer+Lancer+Talon+Talon+Talon+Vanguard (gun): win in 60 s, score 100.
- **Worst:** Lancer (no gun): timeout after 600 s, 3/6 destroyed, score 22; Talon (gun): timeout after 600 s, 3/6 destroyed, score 22; Sentinel+Lancer+Lancer+Lancer+Lancer+Lancer (no gun): timeout after 600 s, 4/6 destroyed, score 30.
- **Alone:** Sentinel win/win; Lancer timeout/timeout; Warden timeout/win; Talon timeout/timeout; Vanguard win/timeout (with the gun / without).

### Vrask Siege Group (defences, 3 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 121 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 379 of 461 (82%), lost 0, timed out 82; wins took 196 s on average, with 0.01 frigates lost; the 82 not won destroyed 2.0 of 3 enemies on average.
- **Best:** Lancer+Lancer+Lancer+Lancer+Lancer+Lancer (gun): win in 90 s, score 99; Lancer+Lancer+Lancer+Lancer (gun): win in 92 s, score 99; Lancer+Lancer+Warden+Talon (gun): win in 92 s, score 99.
- **Worst:** Sentinel+Vanguard+Vanguard (no gun): timeout after 600 s, 2/3 destroyed, score 25; Lancer+Lancer+Lancer+Lancer+Lancer+Lancer (no gun): timeout after 600 s, 2/3 destroyed, score 30; Lancer+Lancer+Lancer+Lancer+Lancer+Talon (no gun): timeout after 600 s, 2/3 destroyed, score 30.
- **Alone:** Sentinel win/win; Lancer win/timeout; Warden win/win; Talon win/timeout; Vanguard win/timeout (with the gun / without).

### Vrask raiders (band, 6 enemies)

- **With the main gun:** won 461 of 461 (100%), lost 0, timed out 0; wins took 87 s on average, with 0.00 frigates lost.
- **Without the main gun:** won 432 of 461 (94%), lost 6, timed out 23; wins took 142 s on average, with 0.01 frigates lost; 6 frigates captured in all; the 29 not won destroyed 2.7 of 6 enemies on average.
- **Best:** Lancer+Lancer+Lancer+Lancer+Talon+Vanguard (gun): win in 55 s, score 100; Lancer+Lancer+Warden (gun): win in 58 s, score 100; Lancer+Lancer+Vanguard (gun): win in 58 s, score 100.
- **Worst:** Lancer (no gun): loss after 282 s, 0/6 destroyed, score 0; Talon (no gun): timeout after 600 s, 0/6 destroyed, score 0; Lancer+Talon+Talon+Talon+Talon+Talon (no gun): timeout after 600 s, 1/6 destroyed, score 8.
- **Alone:** Sentinel win/timeout; Lancer win/loss; Warden win/timeout; Talon win/timeout; Vanguard win/timeout (with the gun / without).

## What each class brings

The average score of every battle fought by a fleet with at least one of the class in it, against the same with none (the difference is what having the class in the fleet is worth), with the main gun and without; and the class alone.

| Class | With it (gun) | Without it (gun) | Worth (gun) | Worth (no gun) | Alone: wins (gun / no gun) |
|---|---|---|---|---|---|
| Sentinel | 99.1 | 98.8 | +0.2 | +13.8 | 10/10 / 8/10 |
| Lancer | 99.1 | 98.8 | +0.2 | -3.2 | 8/10 / 4/10 |
| Warden | 99.0 | 99.0 | +0.0 | +14.3 | 8/10 / 8/10 |
| Talon | 99.0 | 98.9 | +0.1 | -3.4 | 9/10 / 4/10 |
| Vanguard | 99.0 | 98.9 | +0.2 | -2.7 | 10/10 / 4/10 |

## What the main gun is worth

The same fleet against the same encounter, with the bow beam and without: how often having it turned a loss or timeout into a win, and the other way.

Over 4610 pairs: the gun won a fight that was otherwise not won **839** times; without it a fleet won where with it it didn't 1 times; the rest (3770) came out the same.

| Encounter | Won only with the gun | Won only without it | Same either way |
|---|---|---|---|
| Cinder Orbital Battery | 0 | 0 | 461 |
| Corsair Haven | 460 | 0 | 1 |
| Corsair Ion Satellite | 0 | 0 | 461 |
| Corsair Mass Driver | 0 | 0 | 461 |
| Corsair patrol | 0 | 0 | 461 |
| Dreadnought battle group | 79 | 0 | 382 |
| Pirate cruiser | 82 | 0 | 379 |
| Raiders | 107 | 1 | 353 |
| Vrask Siege Group | 82 | 0 | 379 |
| Vrask raiders | 29 | 0 | 432 |

## Notes

- The frigates are flown by the simulator's pilot, not a player: they don't use cover, missiles (none are fitted by default), fleet orders or retreat. A player could do better (or worse) in each fight.
- An encounter is fought from where its ships wait, fresh each time; a timeout means neither side won in 10 minutes, usually a fleet that couldn't hit fast fighters or get through shields without its beam.
- The Vrask encounters end in a win when the band gives up and withdraws (losing its Maw and Snares), as they do in the game.

## Two frigates against the Greys and the Empire

From the battle simulator's duo runs (`-simduo -simenc <band> -simreps 4`): every pair of the five frigate classes (two of a kind left out: 10 pairs), each fought flown both ways, by the simulator's own pilot and as a section, 4 times each: **80 battles a band**, against the bands as built (default mixes), the frigates with their main guns. The frigates fight by what their sensors show (a cloaked or jammed-out ship isn't there to shoot until it is seen). A band that breaks off and withdraws (the Greys vanishing, the Empire retreating) counts as a win, as a Vrask band's does.

| Band | System | Battles | Won | Lost | Timed out | Withdrew | Wins took (median) | Frigates lost a battle | Hull / shield left |
|---|---|---|---|---|---|---|---|---|---|
| Sheol picket | Nine Hells | 80 | 80 (100%) | 0 | 0 | 41 | 87 s | 0.03 | 98% / 96% |
| Greys cell | Testing System | 80 | 76 (95%) | 0 | 4 | 75 | 61 s | 0.00 | 99% / 78% |
| Greys cell | Nine Hells | 80 | 76 (95%) | 0 | 4 | 74 | 72 s | 0.00 | 100% / 99% |
| Imperial patrol | Testing System | 80 | 80 (100%) | 0 | 0 | 1 | 68 s | 0.00 | 100% / 100% |
| Imperial task force | Testing System | 80 | 79 (99%) | 1 | 0 | 0 | 142 s | 0.07 | 99% / 100% |
| Imperial battle group | Nine Hells | 80 | 80 (100%) | 0 | 0 | 1 | 116 s | 0.00 | 100% / 99% |
| Imperial forward base | Nine Hells | 80 | 79 (99%) | 1 | 0 | 0 | 106 s | 0.03 | 100% / 100% |

- **Sheol picket, Nine Hells** (the Vrask picket at Sheol, for comparison; 10 ships: 2 Maws 3 Snares 2 Fangs 3 Skinks): won 40 of 40 in 92 s flown by the simulator's pilot, 40 of 40 in 81 s as a section; 8.5 of its ships destroyed a battle on average. The hardest pair: Sentinel and Warden, won 8 of 8, in 112 s on average.
- **Greys cell, Testing System** (a Greys cell; 5 ships: 1 Scout 1 Shroud 2 Lances 1 Reaper): won 36 of 40 in 66 s flown by the simulator's pilot, 40 of 40 in 61 s as a section; 3.4 of its ships destroyed a battle on average. The hardest pair: Warden and Vanguard, won 4 of 8, in 336 s on average.
- **Greys cell, Nine Hells** (a Greys cell; 5 ships: 1 Scout 1 Shroud 2 Lances 1 Reaper): won 36 of 40 in 72 s flown by the simulator's pilot, 40 of 40 in 72 s as a section; 3.5 of its ships destroyed a battle on average. The hardest pair: Warden and Vanguard, won 4 of 8, in 337 s on average.
- **Imperial patrol, Testing System** (an Imperial patrol; 3 ships: 1 Patrol 1 Raider 1 Recon): won 40 of 40 in 74 s flown by the simulator's pilot, 40 of 40 in 56 s as a section; 3.0 of its ships destroyed a battle on average. The hardest pair: Warden and Talon, won 8 of 8, in 87 s on average.
- **Imperial task force, Testing System** (an Imperial task force; 7 ships: 2 Assaults 2 Patrols 2 Raiders 1 Recon): won 39 of 40 in 146 s flown by the simulator's pilot, 40 of 40 in 142 s as a section; 6.9 of its ships destroyed a battle on average. The hardest pair: Sentinel and Lancer, won 7 of 8, in 125 s on average.
- **Imperial battle group, Nine Hells** (the Imperial battle group; 6 ships: 1 Assault 2 Patrols 2 Raiders 1 Recon): won 40 of 40 in 130 s flown by the simulator's pilot, 40 of 40 in 102 s as a section; 6.0 of its ships destroyed a battle on average. The hardest pair: Warden and Vanguard, won 8 of 8, in 155 s on average.
- **Imperial forward base, Nine Hells** (the Imperial forward base; 6 ships: 2 Assaults 2 Patrols 1 Raider 1 Recon): won 39 of 40 in 117 s flown by the simulator's pilot, 40 of 40 in 99 s as a section; 6.0 of its ships destroyed a battle on average. The hardest pair: Sentinel and Lancer, won 7 of 8, in 95 s on average.

### What the numbers mean

- **Every new band is winnable by two frigates nearly every time**, as the Vrask picket is, and the fights are about as long as the Vrask one: the Greys' a little shorter, the Empire's from a little shorter (a patrol of three) to a little over half as long again (the task force of seven).
- **The Greys** vanish once their Shroud and their Reaper are both lost, so a win is usually three or four of the cell's five destroyed and the rest gone. Their timeouts are all one pair, the Warden and the Vanguard flown by the simulator's own pilot (as a section, they always win): they take little damage, and can't finish the band in 10 minutes.
- **The Empire** brings more ships: drones, point defence that takes missiles, and armour that soaks the first hits. A band falls back fighting at half strength and then retreats.
- The simulator's frigates fight as a pair from the start; a player alone, or caught by a band's ambush, has a harder time.
- Tuned for this: the Grey Reaper (hull 4,600, shield 3,000, cloaked to 0.35 while it holds back), the Shroud (hull 3,400, shield 4,200 recharging 180/s) and the Lance (hull 2,400, a flank of 6 s at most); the Imperial cannon 55 to 75 a shell; a painted target takes a quarter more from its painters' turrets.
