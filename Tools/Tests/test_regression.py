"""Unit tests for regression.py (the run's first stage): python -m unittest discover -s Tools/Tests"""
import unittest

import regression as R

AUTOTEST = """ship Frigate (Gun frigate): max speed 150 m/s, acceleration 30 m/s/s, turn 20 deg/s, guns 2, hangar False, missiles False
   3.0s start: fps 180, hull 2820, hits 2
  handling: after 4.0s at full throttle and rudder: speed 120 m/s, turned 80 deg (20.0 deg/s)
  attitude: level off -> pitch 0.0 deg
  elevators: released 3s -> lift 0 m/s, flares ventral 0.00 / dorsal 0.00
  breakup: EnemyCruiser: 12 parts, 12 broke off before the explosion, explosion 1.01 s after the first part
  main gun: fired after 10.01 s of charging (Firing)
  frigate destroyed: dead True, breakup Breaking, 22 parts, respawn in 8.0 s
  respawned 5.6 s later: hull 6000/6000, shield 100 % up True, 22/22 parts back in place, turrets running 6, at (0, 0, 0), enemy hits on the shield while it was down: 0
 106.2s pad Start (pause menu open True, time scale 0): gamepad AutoTestPad
 106.8s pad removed: gamepad none, pad help shown False
shot check: 134 shots, 0 fired away from the target, 0 through the frigate's own hull
sound sources: library: Cannon (3), PointDefense (6)
music: playing True, combat mix 1.00
CIWS: 4 mounts, 682 shots, fired at: EnemyDrone B (Drone)
done""".splitlines()


def edit(lines, old, new):
    return [l.replace(old, new) for l in lines]


class AutotestTests(unittest.TestCase):
    def test_a_report_matches_itself(self):
        self.assertEqual(R.compare_autotest(AUTOTEST, AUTOTEST), [])

    def test_combat_numbers_and_line_order_dont_matter(self):
        run = edit(AUTOTEST, "hits 2", "hits 3")
        run = edit(run, "682 shots", "640 shots")
        run = run[1:-1] + run[:1] + run[-1:]          # the order changes; 'done' stays last
        run = edit(run, "10.01 s of charging", "10.02 s of charging")
        self.assertEqual(R.compare_autotest(AUTOTEST, run), [])

    def test_a_real_gamepad_doesnt_matter(self):
        run = edit(AUTOTEST, "gamepad none, pad help shown False", "gamepad XInputControllerWindows, pad help shown True")
        self.assertEqual(R.compare_autotest(AUTOTEST, run), [])

    def test_a_missing_or_new_line_is_a_regression(self):
        problems = R.compare_autotest(AUTOTEST, [l for l in AUTOTEST if "attitude" not in l] + ["  something new: 3"])
        self.assertTrue(any(p.startswith("missing: attitude: level off") for p in problems), problems)
        self.assertTrue(any(p.startswith("new: something new") for p in problems), problems)
        self.assertTrue(any(p.startswith("levelling off brings the nose level: no line") for p in problems), problems)

    def test_a_shot_through_the_hull_is_a_regression(self):
        problems = R.compare_autotest(AUTOTEST, edit(AUTOTEST, "0 through the frigate's own hull", "2 through the frigate's own hull"))
        self.assertEqual(problems, ["no shot misses its aim or hits the frigate: 'shot check: 134 shots, 0 fired away from the target, 2 through the frigate's own hull'"])

    def test_a_ship_that_comes_back_damaged_is_a_regression(self):
        problems = R.compare_autotest(AUTOTEST, edit(AUTOTEST, "22/22 parts", "21/22 parts"))
        self.assertTrue(any(p.startswith("the frigate comes back whole") for p in problems), problems)

    def test_a_slow_main_gun_is_a_regression(self):
        problems = R.compare_autotest(AUTOTEST, edit(AUTOTEST, "10.01 s of charging", "12.50 s of charging"))
        self.assertTrue(any(p.startswith("the main gun fires after its 10 s charge") for p in problems), problems)

    def test_a_lost_sound_or_ciws_mount_is_a_regression(self):
        run = edit(AUTOTEST, "PointDefense (6)", "PointDefense (5)")
        run = edit(run, "CIWS: 4 mounts", "CIWS: 3 mounts")
        problems = R.compare_autotest(AUTOTEST, run)
        self.assertTrue(any(p.startswith("every sound is there") for p in problems), problems)
        self.assertTrue(any(p.startswith("the CIWS mounts are all there") for p in problems), problems)

    def test_a_report_cut_short_is_a_regression(self):
        problems = R.compare_autotest(AUTOTEST, AUTOTEST[:-1])
        self.assertIn("the test ran to the end: no line like /^done$/", problems)
        self.assertEqual(R.compare_autotest(AUTOTEST, []), ["no report"])

    def test_a_report_cut_short_fails_even_against_itself(self):
        # what --bless and a first run (no baseline yet) check: a partial report mustn't pass
        short = AUTOTEST[:6]
        self.assertIn("the report stops before 'done' (the test didn't finish)", R.compare_autotest(short, short))

    def test_rules_for_lines_neither_report_has_dont_apply(self):
        short = [l for l in AUTOTEST if "breakup: EnemyCruiser" not in l]
        self.assertEqual(R.check_facts(R.AUTOTEST_FACTS, short, short), [])


HUD = """   0.0s  screen 1280x720, 33 warp points
 294.7s    ok   the console is in the bottom-left corner
 294.7s    ok   at least 250 lines, at least 5 for every event
 294.7s  PASS: HUD and captain (2 of 2)
 294.7s  done""".splitlines()

ORBIT = """ 730.1s    ok: Kepler's third law
 730.1s  PASS: orbits (1 of 1)
 730.1s  done""".splitlines()

WEB = """[SystemTest]    5.1s    ok   with two players the enemies are tougher
[SystemTest]   12.3s  PASS: co-op (1 of 1)""".splitlines()


class SelfTestTests(unittest.TestCase):
    def test_a_report_matches_itself(self):
        for report in (HUD, ORBIT):
            self.assertEqual(R.compare_selftest(report, report), [])
        self.assertEqual(R.compare_selftest(WEB, WEB, require_done=False), [])

    def test_the_checks_are_read(self):
        t = R.SelfTest(HUD)
        self.assertEqual(t.checks, [("ok", "the console is in the bottom-left corner"), ("ok", "at least 250 lines, at least 5 for every event")])
        self.assertEqual(t.verdicts, [("PASS", "HUD and captain (2 of 2)")])
        self.assertTrue(t.done)
        self.assertEqual(R.SelfTest(ORBIT).checks, [("ok", "Kepler's third law")])
        self.assertEqual(R.SelfTest(WEB).verdicts, [("PASS", "co-op (1 of 1)")])

    def test_a_failing_check_is_a_failure(self):
        run = [l.replace("ok   the console", "FAIL the console") for l in HUD]
        run = [l.replace("PASS: HUD and captain (2 of 2)", "FAIL: HUD and captain (1 of 2)") for l in run]
        problems = R.compare_selftest(HUD, run)
        self.assertIn("FAIL: the console is in the bottom-left corner", problems)
        self.assertIn("FAIL: HUD and captain (1 of 2)", problems)
        self.assertIn("check missing or failing: the console is in the bottom-left corner", problems)

    def test_a_check_that_disappears_is_a_regression(self):
        run = [l for l in HUD if "250 lines" not in l]
        run = [l.replace("(2 of 2)", "(1 of 1)") for l in run]
        problems = R.compare_selftest(HUD, run)
        self.assertIn("check missing or failing: at least 250 lines, at least 5 for every event", problems)
        self.assertTrue(any(p.startswith("fewer checks than the baseline") for p in problems), problems)

    def test_more_checks_are_fine_and_reported(self):
        run = HUD[:3] + [" 294.7s    ok   a new check"] + [HUD[3].replace("(2 of 2)", "(3 of 3)")] + HUD[4:]
        self.assertEqual(R.compare_selftest(HUD, run), [])
        self.assertEqual(R.new_checks(HUD, run), ["a new check"])

    def test_failed_also_counts_as_a_failure(self):
        run = ORBIT[:1] + [" 730.1s    FAILED: the comet follows its ellipse"] + ORBIT[1:]
        self.assertIn("FAIL: the comet follows its ellipse", R.compare_selftest(ORBIT, run))

    def test_a_test_that_stops_early_is_a_failure(self):
        problems = R.compare_selftest(HUD, HUD[:-1])
        self.assertIn("the report stops before 'done' (the test didn't finish)", problems)
        self.assertIn("no report", R.compare_selftest(HUD, []))
        self.assertIn("no verdict (PASS or FAIL) in the report", R.compare_selftest(HUD, HUD[:1]))


class NormaliseTests(unittest.TestCase):
    def test_numbers_are_masked(self):
        self.assertEqual(R.normalise("  3.0s hits -2, 1280x720"), "  Ns hits N, NxN")

    def test_values(self):
        self.assertEqual([R.value("12"), R.value("-3"), R.value("1.5"), R.value("True")], [12, -3, 1.5, "True"])


if __name__ == "__main__":
    unittest.main()
