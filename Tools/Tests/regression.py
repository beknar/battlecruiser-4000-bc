"""Comparing a test run's reports with the baselines in Tools/Tests/baselines.

Two kinds of report:

- An autotest report (CarrierDemo.exe / AsteroidAmbush.exe -autotest) is a log of what the
  scripted test saw, with no verdict of its own. It is compared with its baseline two ways:
  - its shape: every line, with the numbers masked (combat numbers differ from run to run), must
    be in the baseline and the other way round, in any order;
  - its facts: rules about the numbers that must hold (no shot misses its target, the frigate
    comes back whole, the main gun fires after its 10 s charge...), some of them checked against
    the baseline's own line.
- A self-test report (StarSystem.exe -systemtest, the co-op server test) has its own checks and
  verdicts. Every one must pass, the report must have run to the end, and every check and verdict
  in the baseline must still be there (a check that disappears is a regression too); a count like
  "(19 of 19)" may grow but not shrink.

Only the standard library is used.
"""
import re
from collections import Counter

NUM = re.compile(r"-?\d+(?:\.\d+)?")


def normalise(line):
    """A line with its numbers masked: '  3.0s hits 2' -> '  Ns hits N'."""
    return NUM.sub("N", line.rstrip())


def value(s):
    """A captured group as a number where it is one."""
    if s is None:
        return None
    if re.fullmatch(r"-?\d+", s):
        return int(s)
    if re.fullmatch(r"-?\d+\.\d+", s):
        return float(s)
    return s


# ---------------------------------------------------------------- autotest reports

# Lines whose shape depends on the machine rather than the game.
VOLATILE = [re.compile(p) for p in (
    r"pad removed: gamepad",        # a real gamepad, if one is plugged in, shows here
)]

SAME = "same"                # the line must be exactly the baseline's
SAME_GROUPS = "same-groups"  # the captured values must be the baseline's


class Fact:
    """A rule about a report: the first line matching `pattern` must pass `test` (a function of
    the captured values, or SAME / SAME_GROUPS). A rule whose line is in neither the report nor
    the baseline doesn't apply to that game."""

    def __init__(self, name, pattern, test):
        self.name, self.pattern, self.test = name, re.compile(pattern), test

    def find(self, lines):
        for line in lines:
            m = self.pattern.search(line)
            if m:
                return line, m
        return None, None


AUTOTEST_FACTS = [
    Fact("the ship is as built", r"^ship ", SAME),
    Fact("it turns at its rated 20 deg/s", r"handling: .*\(([\d.]+) deg/s\)", lambda r: abs(r - 20) <= 0.5),
    Fact("levelling off brings the nose level", r"attitude: level off -> pitch (-?[\d.]+) deg", lambda p: abs(p) < 1),
    Fact("the elevators stop when let go", r"elevators: released 3s -> lift (-?\d+) m/s", lambda l: l == 0),
    Fact("the main gun fires after its 10 s charge", r"main gun: fired after ([\d.]+) s of charging", lambda t: 9.9 <= t <= 10.25),
    Fact("the enemy cruiser comes apart", r"breakup: EnemyCruiser: (\d+) parts, (\d+) broke off", lambda n, b: n > 0 and n == b),
    Fact("the frigate breaks up when destroyed", r"frigate destroyed: dead (\w+), breakup (\w+), (\d+) parts",
         lambda d, b, n: d == "True" and b == "Breaking" and n > 0),
    Fact("the frigate comes back whole", r"respawned .*hull (\d+)/(\d+), shield (\d+) % up (\w+), (\d+)/(\d+) parts back in place",
         lambda h, hmax, s, up, p, pmax: h == hmax and s == 100 and up == "True" and p == pmax),
    Fact("no hits on the shield while it was down", r"enemy hits on the shield while it was down: (\d+)", lambda n: n == 0),
    Fact("the pause menu stops the game", r"pad Start \(pause menu open (\w+), time scale (\d+)\)", lambda o, t: o == "True" and t == 0),
    Fact("no shot misses its aim or hits the frigate", r"^shot check: (\d+) shots, (\d+) fired away from the target, (\d+) through",
         lambda s, away, hull: s > 0 and away == 0 and hull == 0),
    Fact("every sound is there", r"^sound sources:", SAME),
    Fact("the music plays", r"^music: playing (\w+)", lambda p: p == "True"),
    Fact("the CIWS mounts are all there", r"^CIWS: (\d+) mounts", SAME_GROUPS),
    Fact("the test ran to the end", r"^done$", SAME),
]


def check_facts(facts, baseline, lines):
    """Problems with a report's facts (an empty list if there are none)."""
    problems = []
    for f in facts:
        b_line, b_match = f.find(baseline)
        g_line, g_match = f.find(lines)
        if b_line is None and g_line is None:
            continue
        if g_line is None:
            problems.append(f"{f.name}: no line like /{f.pattern.pattern}/")
            continue
        if f.test == SAME:
            if b_line is not None and g_line.strip() != b_line.strip():
                problems.append(f"{f.name}: '{g_line.strip()}' (baseline '{b_line.strip()}')")
        elif f.test == SAME_GROUPS:
            if b_match is not None and g_match.groups() != b_match.groups():
                problems.append(f"{f.name}: {g_match.groups()} (baseline {b_match.groups()})")
        elif not f.test(*[value(g) for g in g_match.groups()]):
            problems.append(f"{f.name}: '{g_line.strip()}'")
    return problems


def compare_shape(baseline, lines, volatile=VOLATILE):
    """Lines (numbers masked) in one report and not the other, in any order."""
    def shape(ls):
        return Counter(normalise(l) for l in ls if l.strip() and not any(v.search(l) for v in volatile))
    want, got = shape(baseline), shape(lines)
    missing, extra = want - got, got - want
    return [f"missing: {l.strip()}" for l in sorted(missing.elements())] + \
           [f"new: {l.strip()}" for l in sorted(extra.elements())]


def compare_autotest(baseline, lines):
    """Problems with an autotest report against its baseline."""
    if not lines:
        return ["no report"]
    # the game rewrites its report after every step: one cut short (a crash, a hang) has no 'done'
    ended = [] if lines[-1].strip() == "done" else ["the report stops before 'done' (the test didn't finish)"]
    return ended + compare_shape(baseline, lines) + check_facts(AUTOTEST_FACTS, baseline, lines)


# ---------------------------------------------------------------- self-test reports

PREFIX = re.compile(r"^\[SystemTest\]\s?")                       # the web version's console lines
CHECK = re.compile(r"^\s*(?:[\d.]+s\s+)?(ok|FAIL(?:ED)?)(?::\s*|\s+)(.+?)\s*$")
VERDICT = re.compile(r"^\s*(?:[\d.]+s\s+)?(PASS|FAIL(?:ED)?): (.+?)\s*$")
DONE = re.compile(r"^\s*(?:[\d.]+s\s+)?done\s*$")
COUNT = re.compile(r"\((\d+) of (\d+)")


class SelfTest:
    """A self-test report read: its checks and verdicts, and whether it ran to the end."""

    def __init__(self, lines):
        self.checks, self.verdicts, self.done = [], [], False
        for raw in lines:
            line = PREFIX.sub("", raw.rstrip())
            v = VERDICT.match(line)
            if v:
                self.verdicts.append((v.group(1), v.group(2)))
                continue
            c = CHECK.match(line)
            if c:
                self.checks.append((c.group(1), c.group(2)))
            elif DONE.match(line):
                self.done = True

    @property
    def failures(self):
        return [f"FAIL: {t}" for k, t in self.verdicts + self.checks if k not in ("ok", "PASS")]


def compare_selftest(baseline, lines, require_done=True):
    """Problems with a self-test report against its baseline."""
    if not lines:
        return ["no report"]
    base, got = SelfTest(baseline), SelfTest(lines)
    problems = list(got.failures)
    if not got.verdicts:
        problems.append("no verdict (PASS or FAIL) in the report")
    if require_done and not got.done:
        problems.append("the report stops before 'done' (the test didn't finish)")
    for _, text in got.verdicts:
        m = COUNT.search(text)
        if m and m.group(1) != m.group(2):
            problems.append(f"not every check passed: {text}")
    # every check and verdict in the baseline must still be there and pass
    got_checks = {normalise(t) for k, t in got.checks if k == "ok"}
    got_verdicts = {normalise(t): t for k, t in got.verdicts if k == "PASS"}
    for _, text in base.checks:
        if normalise(text) not in got_checks:
            problems.append(f"check missing or failing: {text}")
    for _, text in base.verdicts:
        key = normalise(text)
        if key not in got_verdicts:
            problems.append(f"verdict missing or failing: {text}")
            continue
        b, g = COUNT.search(text), COUNT.search(got_verdicts[key])
        if b and g and int(g.group(2)) < int(b.group(2)):
            problems.append(f"fewer checks than the baseline: '{got_verdicts[key]}' (baseline '{text}')")
    return problems


def new_checks(baseline, lines):
    """Checks and verdicts in the report that the baseline doesn't have (not a failure)."""
    base, got = SelfTest(baseline), SelfTest(lines)
    known = {normalise(t) for _, t in base.checks + base.verdicts}
    return [t for _, t in got.checks + got.verdicts if normalise(t) not in known]
