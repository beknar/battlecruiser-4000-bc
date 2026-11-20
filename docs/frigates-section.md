# Two frigates against the Vrask picket

This report is from the battle simulator's `-simduo` mode: **240 battles**. Every pair of two
different frigate classes fought the Sheol picket in the Nine Hells, 10 pairs in all, each with
its main gun. The picket was remade into four 10-ship mixes. Each pair was flown two ways:
- **default**: by the simulator's own pilot (guns on the nearest enemy the fleet sees);
- **section**: with the section tactics in `SimPilot` (see the README's CIC reel section).

Every pair, mix and tactic was fought 3 times.

To run it:

```
StarSystem.exe -batchmode -nographics -system ninehells -battlesim <dir> -simduo -simmix 2,3,2,3 -simreps 3
```

Run it once for each mix.

A **win** means every Vrask ship is destroyed, or the band gives up and withdraws. It withdraws
once it has lost its Maws and every Snare.

## By mix and tactic

| Vrask mix | Tactic | Battles | Won | Frigates lost | Captured | Average time (s) | Vrask destroyed | Vrask withdrew | Hits on the frigates |
|---|---|---|---|---|---|---|---|---|---|
| 2 Maws, 3 Snares, 2 Fangs, 3 Skinks | default | 30 | 29 | 2 | 1 | 98 | 9.7 | 0 | 48 |
| 2 Maws, 3 Snares, 2 Fangs, 3 Skinks | section | 30 | 30 | 0 | 0 | 79 | 7.0 | 30 | 110 |
| 2 Maws, 4 Snares, 2 Fangs, 2 Skinks | default | 30 | 29 | 2 | 1 | 101 | 9.7 | 0 | 56 |
| 2 Maws, 4 Snares, 2 Fangs, 2 Skinks | section | 30 | 30 | 0 | 0 | 90 | 8.0 | 30 | 97 |
| 2 Maws, 4 Snares, 3 Fangs, 1 Skink | default | 30 | 29 | 2 | 1 | 103 | 9.7 | 0 | 37 |
| 2 Maws, 4 Snares, 3 Fangs, 1 Skink | section | 30 | 30 | 1 | 0 | 90 | 9.0 | 30 | 54 |
| 3 Maws, 3 Snares, 2 Fangs, 2 Skinks | default | 30 | 29 | 2 | 1 | 109 | 9.6 | 3 | 68 |
| 3 Maws, 3 Snares, 2 Fangs, 2 Skinks | section | 30 | 30 | 0 | 0 | 83 | 8.0 | 30 | 65 |

## By pair and tactic (all four mixes)

| Pair | Tactic | Battles | Won | Frigates lost | Average time (s) | Hull left |
|---|---|---|---|---|---|---|
| Talon+Vanguard | section | 12 | 12 | 1 | 72 | 100% |
| Sentinel+Talon | section | 12 | 12 | 0 | 77 | 100% |
| Lancer+Talon | section | 12 | 12 | 0 | 78 | 87% |
| Lancer+Vanguard | section | 12 | 12 | 0 | 78 | 100% |
| Lancer+Warden | section | 12 | 12 | 0 | 86 | 100% |
| Lancer+Talon | default | 12 | 12 | 0 | 87 | 98% |
| Warden+Vanguard | section | 12 | 12 | 0 | 89 | 89% |
| Warden+Talon | section | 12 | 12 | 0 | 89 | 100% |
| Sentinel+Vanguard | section | 12 | 12 | 0 | 91 | 95% |
| Sentinel+Lancer | section | 12 | 12 | 0 | 91 | 97% |
| Talon+Vanguard | default | 12 | 12 | 0 | 96 | 100% |
| Lancer+Vanguard | default | 12 | 12 | 0 | 97 | 100% |
| Sentinel+Talon | default | 12 | 12 | 0 | 97 | 100% |
| Sentinel+Vanguard | default | 12 | 12 | 0 | 100 | 100% |
| Warden+Talon | default | 12 | 12 | 0 | 102 | 100% |
| Sentinel+Warden | section | 12 | 12 | 0 | 103 | 100% |
| Sentinel+Lancer | default | 12 | 8 | 8 | 106 | 67% |
| Lancer+Warden | default | 12 | 12 | 0 | 112 | 99% |
| Warden+Vanguard | default | 12 | 12 | 0 | 112 | 100% |
| Sentinel+Warden | default | 12 | 12 | 0 | 119 | 100% |

## What it shows

- **Two beam frigates beat ten Vrask, whatever the mix.** No frigate has missiles or fighters,
  and the Vrask never get a ship disabled long enough to board it.
- **The section lost 1 frigate in 120 battles; the default way lost 8.**
  - All 8 default losses were in the Sentinel+Lancer pair: the two lightest hulls, split up,
    each with no one covering.
  - The section's loss was a Talon+Vanguard battle against the Fang-heavy mix. The pair took
    no recorded hits in that battle, so it was probably a collision; the log doesn't say.
- **The section wins quicker,** in 72 to 103 s on average against 87 to 119 s. It ends the
  fight by breaking the Vrask (Snares, then Maws) rather than hunting down every ship.
  - Every section battle ended with the Vrask withdrawing, against 3 of the 120 default ones.
- **Talon+Vanguard is the quickest section.** It averaged 72 s. The Talon's beam charges in
  7 s and kills; the Vanguard's hull and heavy beam take the artillery.
- **The picket's own mix hits a section hardest.** 2 Maws, 3 Snares, 2 Fangs and 3 Skinks
  landed 110 hits on average, against 54 to 97 for the others. Its three Skinks spot for the
  Maws' artillery, and the Snare-heavy mixes lose their Snares to a section quickly.
- **Against the default pilot the mixes rank differently.** The Skink-light, Fang-heavy mix lands
  the fewest hits on the default pilot, but takes it longest to beat.

The two-frigate CIC reel (`-cic -cicfleet Vanguard,Talon -cicmix 2,3,2,3 -cicsection`) is
one of these battles, played in real time.
