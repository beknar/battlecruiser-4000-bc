"""Write docs/frigates-performance.md from the battle simulator's results (battles.csv).

    python Tools/BattleSim/report.py <battles.csv> [docs/frigates-performance.md]

The simulator (StarSystem.exe -batchmode -nographics -battlesim <dir>) fights every fleet of 1 to 6
of the five frigate classes (461 fleets) against every hostile encounter in space, with the bow beam
(the main gun) and without; a line a battle. This turns the lines into the report: how each
encounter went for each size of fleet, with the main gun and without; which fleets did best and
worst; what each class brings; the smallest fleet that reliably wins each fight.
"""
import csv
import sys
from collections import defaultdict
from statistics import mean

CLASSES = ["Sentinel", "Lancer", "Warden", "Talon", "Vanguard"]


def load(path):
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            r["size"] = int(r["size"])
            r["main_gun"] = r["main_gun"] == "1"
            for k in ("seconds", "enemies_killed", "enemies_total", "frigates_lost", "frigates_captured", "hits_taken"):
                r[k] = int(float(r.get(k) or 0))
            for k in ("hull_left", "shield_left"):
                r[k] = float(r.get(k) or 0)
            r["fleet_list"] = r["fleet"].split("+")
            rows.append(r)
    return rows


def pct(n, d):
    return f"{100.0 * n / d:.0f}%" if d else "-"


def score(r):
    """How well a battle went, 0 to 100: a win with nothing lost, quickly and whole, is best; a loss
    is scored by what was destroyed before it."""
    killed = r["enemies_killed"] / max(1, r["enemies_total"])
    if r["outcome"] == "win":
        s = 60 + 25 * (1 - r["frigates_lost"] / max(1, r["size"])) + 10 * r["hull_left"] + 5 * max(0.0, 1 - r["seconds"] / 600.0)
    elif r["outcome"] == "timeout":
        s = 45 * killed - 15 * r["frigates_lost"] / max(1, r["size"])
    else:
        s = 35 * killed
    return max(0.0, min(100.0, s))


def verdict(rows):
    w = sum(r["outcome"] == "win" for r in rows)
    l = sum(r["outcome"] == "loss" for r in rows)
    t = sum(r["outcome"] == "timeout" for r in rows)
    return w, l, t


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "battles.csv"
    out = sys.argv[2] if len(sys.argv) > 2 else "docs/frigates-performance.md"
    rows = load(src)
    encounters = sorted({r["encounter"] for r in rows}, key=lambda e: e)
    lines = []
    add = lines.append
    total = len(rows)
    add("# Frigate performance against the hostile encounters\n")
    add(f"From the battle simulator: **{total} battles**: every fleet of 1 to 6 ships drawn from the five frigate classes "
        f"({', '.join(CLASSES)}; the order not counting, a class may repeat: 461 fleets), as built (default fittings, full crews), "
        f"against each of the Testing System's {len(encounters)} hostile encounters in space, with the bow beam (the main gun) and without.\n")
    add("Each battle was fought where the encounter waits, the fleet coming in line abreast from 9 km, its frigates flown as a player "
        "would (`SimPilot`: every gun on one enemy, broadside for the turrets, bow on to charge and fire the beam in its reach). "
        "A battle ends in a **win** (every enemy destroyed, or a Vrask band that gives up and withdraws), a **loss** (every frigate "
        "destroyed or captured) or a **timeout** (still undecided after 10 minutes). Recorded with "
        "`StarSystem.exe -batchmode -nographics -battlesim <dir>`; this report is `python Tools/BattleSim/report.py <dir>/battles.csv`.\n")
    add("A battle's **score** (0 to 100) rates how well it went: a win counts 60, plus up to 25 for the frigates still in the fight, "
        "10 for the hull left and 5 for being quick; a timeout or a loss counts what it destroyed before it ended.\n")

    # ---- overall
    w, l, t = verdict(rows)
    add("## Overall\n")
    add(f"| | Battles | Won | Lost | Timed out |\n|---|---|---|---|---|")
    for gun in (True, False):
        rs = [r for r in rows if r["main_gun"] == gun]
        a, b, c = verdict(rs)
        add(f"| {'With main gun' if gun else 'Without main gun'} | {len(rs)} | {a} ({pct(a, len(rs))}) | {b} ({pct(b, len(rs))}) | {c} ({pct(c, len(rs))}) |")
    add(f"| **All** | {total} | {w} ({pct(w, total)}) | {l} ({pct(l, total)}) | {t} ({pct(t, total)}) |\n")

    # ---- per encounter: wins by fleet size, gun / no gun
    add("## Each encounter, by fleet size\n")
    add("Wins out of the fleets of that size (with the main gun / without). Fleets of each size: 1: 5, 2: 15, 3: 35, 4: 70, 5: 126, 6: 210.\n")
    add("| Encounter | 1 | 2 | 3 | 4 | 5 | 6 | Smallest fleet that always wins (gun / no gun) |")
    add("|---|---|---|---|---|---|---|---|")
    for e in encounters:
        cells = []
        smallest = []
        for gun in (True, False):
            sm = "-"
            for size in range(1, 7):
                rs = [r for r in rows if r["encounter"] == e and r["size"] == size and r["main_gun"] == gun]
                if rs and all(r["outcome"] == "win" for r in rs):
                    sm = str(size)
                    break
            smallest.append(sm)
        for size in range(1, 7):
            g = [r for r in rows if r["encounter"] == e and r["size"] == size and r["main_gun"]]
            n = [r for r in rows if r["encounter"] == e and r["size"] == size and not r["main_gun"]]
            cells.append(f"{pct(sum(r['outcome'] == 'win' for r in g), len(g))} / {pct(sum(r['outcome'] == 'win' for r in n), len(n))}")
        add(f"| {e} | " + " | ".join(cells) + f" | {smallest[0]} / {smallest[1]} |")
    add("")

    # ---- per encounter detail
    add("## Each encounter in detail\n")
    for e in encounters:
        rs = [r for r in rows if r["encounter"] == e]
        if not rs:
            continue
        kind = rs[0]["kind"]
        add(f"### {e} ({kind}, {rs[0]['enemies_total']} enemies)\n")
        for gun in (True, False):
            sub = [r for r in rs if r["main_gun"] == gun]
            a, b, c = verdict(sub)
            wins = [r for r in sub if r["outcome"] == "win"]
            add(f"- **{'With' if gun else 'Without'} the main gun:** won {a} of {len(sub)} ({pct(a, len(sub))}), lost {b}, timed out {c}; "
                f"wins took {mean(r['seconds'] for r in wins):.0f} s on average" + (f", with {mean(r['frigates_lost'] for r in wins):.2f} frigates lost" if wins else "") +
                (f"; {sum(r['frigates_captured'] for r in sub)} frigates captured in all" if any(r['frigates_captured'] for r in sub) else "") + "." if wins else
                f"- **{'With' if gun else 'Without'} the main gun:** won {a} of {len(sub)}, lost {b}, timed out {c}.")
            rest = [r for r in sub if r["outcome"] != "win"]
            if rest:
                lines[-1] = lines[-1][:-1] + (f"; the {len(rest)} not won destroyed {mean(r['enemies_killed'] for r in rest):.1f} "
                                              f"of {rest[0]['enemies_total']} enemies on average.")
        best = sorted(rs, key=lambda r: (-score(r), r["size"], r["seconds"]))[:3]
        worst = sorted(rs, key=lambda r: (score(r), -r["size"]))[:3]
        add("- **Best:** " + "; ".join(f"{r['fleet']} ({'gun' if r['main_gun'] else 'no gun'}): {r['outcome']} in {r['seconds']} s, score {score(r):.0f}" for r in best) + ".")
        add("- **Worst:** " + "; ".join(f"{r['fleet']} ({'gun' if r['main_gun'] else 'no gun'}): {r['outcome']} after {r['seconds']} s, "
                                       f"{r['enemies_killed']}/{r['enemies_total']} destroyed, score {score(r):.0f}" for r in worst) + ".")
        # the lone frigates
        solo = {c: [r for r in rs if r["size"] == 1 and r["fleet"] == c] for c in CLASSES}
        add("- **Alone:** " + "; ".join(f"{c} " + "/".join(f"{x['outcome']}" for x in sorted(v, key=lambda x: not x['main_gun'])) for c, v in solo.items() if v) +
            " (with the gun / without).\n")

    # ---- what each class brings
    add("## What each class brings\n")
    add("The average score of every battle fought by a fleet with at least one of the class in it, against the same with none "
        "(the difference is what having the class in the fleet is worth), with the main gun and without; and the class alone.\n")
    add("| Class | With it (gun) | Without it (gun) | Worth (gun) | Worth (no gun) | Alone: wins (gun / no gun) |")
    add("|---|---|---|---|---|---|")
    for c in CLASSES:
        cells = []
        worth = []
        for gun in (True, False):
            inn = [score(r) for r in rows if r["main_gun"] == gun and c in r["fleet_list"]]
            rest = [score(r) for r in rows if r["main_gun"] == gun and c not in r["fleet_list"]]
            if gun:
                cells += [f"{mean(inn):.1f}" if inn else "-", f"{mean(rest):.1f}" if rest else "-"]
            worth.append(f"{(mean(inn) - mean(rest)):+.1f}" if inn and rest else "-")
        solo_g = [r for r in rows if r["fleet"] == c and r["main_gun"]]
        solo_n = [r for r in rows if r["fleet"] == c and not r["main_gun"]]
        add(f"| {c} | {cells[0]} | {cells[1]} | {worth[0]} | {worth[1]} | "
            f"{sum(r['outcome'] == 'win' for r in solo_g)}/{len(solo_g)} / {sum(r['outcome'] == 'win' for r in solo_n)}/{len(solo_n)} |")
    add("")

    # ---- the main gun
    add("## What the main gun is worth\n")
    add("The same fleet against the same encounter, with the bow beam and without: how often having it turned a loss or timeout "
        "into a win, and the other way.\n")
    pairs = defaultdict(dict)
    for r in rows:
        pairs[(r["encounter"], r["fleet"])][r["main_gun"]] = r
    turned, lost_it, same = 0, 0, 0
    per_enc = defaultdict(lambda: [0, 0, 0])
    for (e, f), d in pairs.items():
        if True not in d or False not in d:
            continue
        a, b = d[True]["outcome"] == "win", d[False]["outcome"] == "win"
        if a and not b:
            turned += 1; per_enc[e][0] += 1
        elif b and not a:
            lost_it += 1; per_enc[e][1] += 1
        else:
            same += 1; per_enc[e][2] += 1
    add(f"Over {turned + lost_it + same} pairs: the gun won a fight that was otherwise not won **{turned}** times; "
        f"without it a fleet won where with it it didn't {lost_it} times; the rest ({same}) came out the same.\n")
    add("| Encounter | Won only with the gun | Won only without it | Same either way |\n|---|---|---|---|")
    for e in encounters:
        a, b, c = per_enc[e]
        add(f"| {e} | {a} | {b} | {c} |")
    add("")

    # ---- notes
    add("## Notes\n")
    add("- The frigates are flown by the simulator's pilot, not a player: they don't use cover, missiles (none are fitted by default), "
        "fleet orders or retreat. A player could do better (or worse) in each fight.")
    add("- An encounter is fought from where its ships wait, fresh each time; a timeout means neither side won in 10 minutes, usually a "
        "fleet that couldn't hit fast fighters or get through shields without its beam.")
    add("- The Vrask encounters end in a win when the band gives up and withdraws (losing its Maw and Snares), as they do in the game.")
    open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    print(f"{out}: {total} battles, {len(encounters)} encounters")


if __name__ == "__main__":
    main()
