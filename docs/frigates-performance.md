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
