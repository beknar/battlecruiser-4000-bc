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
| 2 Maws, 3 Snares, 2 Fangs, 3 Skinks | default | 30 | 29 | 2 | 1 | 99 | 9.7 | 0 | 63 |
| 2 Maws, 3 Snares, 2 Fangs, 3 Skinks | section | 30 | 30 | 0 | 0 | 79 | 7.1 | 30 | 99 |
| 2 Maws, 4 Snares, 2 Fangs, 2 Skinks | default | 30 | 29 | 2 | 1 | 101 | 9.7 | 0 | 49 |
| 2 Maws, 4 Snares, 2 Fangs, 2 Skinks | section | 30 | 30 | 0 | 0 | 87 | 8.0 | 30 | 99 |
| 2 Maws, 4 Snares, 3 Fangs, 1 Skink | default | 30 | 29 | 2 | 1 | 103 | 9.7 | 0 | 32 |
| 2 Maws, 4 Snares, 3 Fangs, 1 Skink | section | 30 | 30 | 0 | 0 | 91 | 9.0 | 30 | 57 |
| 3 Maws, 3 Snares, 2 Fangs, 2 Skinks | default | 30 | 29 | 2 | 1 | 112 | 9.6 | 3 | 60 |
| 3 Maws, 3 Snares, 2 Fangs, 2 Skinks | section | 30 | 30 | 0 | 0 | 86 | 8.0 | 30 | 65 |

## By pair and tactic (all four mixes)

| Pair | Tactic | Battles | Won | Frigates lost | Average time (s) | Hull left |
|---|---|---|---|---|---|---|
| Talon+Vanguard | section | 12 | 12 | 0 | 72 | 96% |
| Lancer+Vanguard | section | 12 | 12 | 0 | 77 | 100% |
| Sentinel+Talon | section | 12 | 12 | 0 | 80 | 100% |
| Lancer+Talon | section | 12 | 12 | 0 | 82 | 91% |
| Sentinel+Vanguard | section | 12 | 12 | 0 | 85 | 97% |
| Warden+Vanguard | section | 12 | 12 | 0 | 87 | 88% |
| Warden+Talon | section | 12 | 12 | 0 | 90 | 100% |
| Lancer+Talon | default | 12 | 12 | 0 | 90 | 97% |
| Sentinel+Lancer | section | 12 | 12 | 0 | 91 | 98% |
| Lancer+Warden | section | 12 | 12 | 0 | 92 | 100% |
| Talon+Vanguard | default | 12 | 12 | 0 | 95 | 100% |
| Lancer+Vanguard | default | 12 | 12 | 0 | 98 | 100% |
| Sentinel+Talon | default | 12 | 12 | 0 | 98 | 100% |
| Sentinel+Vanguard | default | 12 | 12 | 0 | 102 | 100% |
| Warden+Talon | default | 12 | 12 | 0 | 104 | 100% |
| Sentinel+Warden | section | 12 | 12 | 0 | 105 | 100% |
| Sentinel+Lancer | default | 12 | 8 | 8 | 106 | 67% |
| Lancer+Warden | default | 12 | 12 | 0 | 110 | 100% |
| Warden+Vanguard | default | 12 | 12 | 0 | 115 | 100% |
| Sentinel+Warden | default | 12 | 12 | 0 | 122 | 100% |

## What it shows

- **Two beam frigates beat ten Vrask, whatever the mix.** No frigate has missiles or fighters,
  and the Vrask never get a ship disabled long enough to board it.
- **The section never lost a frigate in 120 battles.** Flown the default way, it lost 8, all
  from the Sentinel+Lancer pair: the two lightest hulls, split up, each with no one covering.
- **The section wins quicker,** in 72 to 105 s on average against 90 to 122 s. It ends the
  fight by breaking the Vrask (Snares, then Maws) rather than hunting down every ship.
  - Every section battle ended with the Vrask withdrawing, against 3 of the 120 default ones.
- **Talon+Vanguard is the quickest section.** It averaged 72 s, with 96% of its hull left. The
  Talon's beam charges in 7 s and kills; the Vanguard's hull and heavy beam take the artillery.
- **The picket's own mix hits a section hardest.** 2 Maws, 3 Snares, 2 Fangs and 3 Skinks
  landed 99 hits on average, against 57 to 99 for the others. Its three Skinks spot for the
  Maws' artillery, and the Snare-heavy mixes lose their Snares to a section quickly.
- **Against the default pilot the mixes rank differently.** The Skink-light, Fang-heavy mix lands
  the fewest hits on the default pilot, but takes it longest to beat.

The two-frigate CIC reel (`-cic -cicfleet Vanguard,Talon -cicmix 2,3,2,3 -cicsection`) is
one of these battles, played in real time.
