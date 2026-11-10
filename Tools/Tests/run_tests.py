#!/usr/bin/env python3
"""Battlecruiser 4000 BC's tests, in one run: unit, integration and regression.

    python Tools/Tests/run_tests.py [--quick] [--build] [--only s,s] [--skip s,s] [--bless]

Stages, in order (--quick runs the ones marked *):

  tools *      the regression checker's own unit tests (Tools/Tests/test_regression.py)
  server *     the co-op server's unit and integration tests (dotnet test Server/CoopServer.Tests)
  unity *      the game's Edit mode and Play mode tests (Unity in batch mode, no graphics;
               the Unity editor must be closed)
  build        build the four game versions (only with --build; the editor must be closed)
  serverproc * the co-op server program, end to end (Tools/CoopTest/server_test.py)
  autotest *   CarrierDemo and AsteroidAmbush -autotest, compared with their baselines
  hud *        Star System -systemtest -hudtest (the ship's console and the captain)
  coop *       Star System -systemtest -cooptest against the scripted bot (Tools/CoopTest/bot.py)
  web          the web build's co-op test in headless Chrome (Tools/WebTest/cooptest.sh)
  system       the full Star System self-test (warps, landing, mining, a battle)
  land         -landtest: a landing on every kind of world
  orbit        -orbittest: ten minutes of orbits against Kepler's laws
  station      -stationtest: every station, base and installation

Every self-test must pass; each report is also compared with its baseline in Tools/Tests/baselines
(see regression.py): a check that disappears, an autotest line that changes shape or a fact that
no longer holds is a regression. --bless saves this run's reports as the new baselines (after a
deliberate change). The number of unit tests must not drop either (baselines/counts.json).

The games are run one at a time, in a 1280x720 window. The builds are in ../Builds (next to the
project; --builds to change it). Needs Python 3.8+ with the 'websockets' package (for the co-op
stages), the .NET 8 SDK, Unity 6000.6 (--unity or UNITY_EXE) and, for the web stage, Chrome and
Git Bash. Writes everything to <builds>/test_run (--out) and exits 1 if anything failed.
"""
import argparse, json, os, re, shutil, socket, subprocess, sys, time, xml.etree.ElementTree as ET
from pathlib import Path

sys.dont_write_bytecode = True
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(errors="replace")
    except AttributeError:
        pass
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import regression as R  # noqa: E402

PROJECT = HERE.parent.parent
BASELINES = HERE / "baselines"
COUNTS = BASELINES / "counts.json"
WINDOW = ["-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720"]
WINDOWS = os.name == "nt"
EXE = ".exe" if WINDOWS else ""

STAGES = ["tools", "server", "unity", "build", "serverproc", "autotest", "hud", "coop",
          "web", "system", "land", "orbit", "station"]
QUICK = ["tools", "server", "unity", "serverproc", "autotest", "hud", "coop"]
# Star System self-tests: stage -> (extra arguments, time limit in seconds)
SELFTESTS = {
    "hud": (["-hudtest"], 900),
    "system": ([], 1500),
    "land": (["-landtest"], 900),
    "orbit": (["-orbittest"], 1800),
    "station": (["-stationtest"], 1800),
}
BUILD_METHODS = {"CarrierDemo": "Build", "AsteroidAmbush": "BuildAsteroids", "StarSystem": "BuildStarSystem",
                 "StarSystemWeb": "BuildStarSystemWeb"}
# files a Unity build or batch run rewrites without changing them (reverted if they were clean before)
BUILD_NOISE = re.compile(r"(\.mat$|^Assets/Carrier/Settings/CarrierVolume\.asset$|^ProjectSettings/ProjectSettings\.asset$)")


def say(s=""):
    print(s, flush=True)


class Stage:
    def __init__(self, name):
        self.name, self.ok, self.notes, self.failures = name, True, [], []
        self.started, self.seconds = time.time(), 0.0

    def fail(self, why):
        self.ok = False
        self.failures.append(why)
        self.notes.append("FAIL " + why)
        say(f"    FAIL {why}")

    def note(self, s):
        self.notes.append(s)
        say(f"    {s}")


class Runner:
    def __init__(self, args):
        self.args = args
        self.builds = Path(args.builds).resolve()
        self.out = Path(args.out).resolve() if args.out else self.builds / "test_run"
        self.unity = args.unity or os.environ.get("UNITY_EXE") or r"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
        self.dotnet = shutil.which("dotnet") or (r"C:\Program Files\dotnet\dotnet.exe" if WINDOWS else "dotnet")
        self.server_exe = None
        self.counts = json.loads(COUNTS.read_text()) if COUNTS.exists() else {}
        self.new_counts = {}

    # ------------------------------------------------------------ helpers

    def run(self, cmd, log, timeout, cwd=None, env=None):
        """Run a program to the end (or its time limit), its output to `log`; returns its exit code."""
        log.parent.mkdir(parents=True, exist_ok=True)
        with open(log, "w", encoding="utf-8", errors="replace") as f:
            p = subprocess.Popen([str(c) for c in cmd], stdout=f, stderr=subprocess.STDOUT, cwd=cwd, env=env,
                                 stdin=subprocess.DEVNULL)
            try:
                return p.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                stop(p)
                f.write(f"\n[run_tests] stopped after {timeout} s\n")
                return None
            except BaseException:            # Ctrl+C and the like: don't leave a game or the editor running
                stop(p)
                raise

    def play(self, st, what, cmd, log, limit, cwd=None):
        """Run a game (or a test script) to the end; a time-out is a failure."""
        code = self.run(cmd, log, limit, cwd=cwd)
        if code is None:
            st.fail(f"{what}: still running after {limit} s, so stopped (see {log.parent})")
        return code

    def start(self, cmd, log, cwd=None, env=None):
        log.parent.mkdir(parents=True, exist_ok=True)
        f = open(log, "w", encoding="utf-8", errors="replace")
        p = subprocess.Popen([str(c) for c in cmd], stdout=f, stderr=subprocess.STDOUT, cwd=cwd, env=env,
                             stdin=subprocess.DEVNULL)
        p.log_file = f
        return p

    def fresh(self, name):
        d = self.out / name
        if d.exists():
            shutil.rmtree(d)
        d.mkdir(parents=True)
        return d

    def git_dirty(self):
        try:
            out = subprocess.run(["git", "status", "--porcelain"], cwd=PROJECT, capture_output=True, text=True).stdout
        except OSError:
            return None
        return {line[3:].strip().strip('"') for line in out.splitlines() if line[:2].strip()}

    def tidy(self, before, st):
        """Put back the files a Unity run rewrote that were unchanged before it."""
        if before is None:
            return
        after = self.git_dirty() or set()
        noise = sorted(f for f in after - before if BUILD_NOISE.search(f))
        if noise:
            subprocess.run(["git", "checkout", "--"] + noise, cwd=PROJECT)
            st.note(f"put back {len(noise)} file(s) Unity rewrote: {', '.join(noise[:4])}{' ...' if len(noise) > 4 else ''}")
        other = sorted(after - before - set(noise))
        if other:
            st.note(f"changed by the run (left as they are): {', '.join(other[:6])}")

    def baseline(self, name):
        p = BASELINES / name
        return p.read_text(encoding="utf-8").splitlines() if p.exists() else None

    def bless(self, name, lines, st):
        if self.args.bless:
            BASELINES.mkdir(exist_ok=True)
            (BASELINES / name).write_text("\n".join(lines) + "\n", encoding="utf-8")
            st.note(f"saved as the baseline {name}")

    def count(self, key, n, st):
        """The number of tests mustn't drop below the baseline's."""
        self.new_counts[key] = n
        want = self.counts.get(key)
        if want is not None and n < want and not self.args.bless:
            st.fail(f"{key}: {n} tests, fewer than the baseline's {want} (were tests lost?)")

    def game(self, name, st):
        """A game's build, if there is one and it is newer than the game's code."""
        exe = self.builds / name / f"{name}{EXE}"
        if not exe.exists():
            st.fail(f"no {name} build in {exe.parent} (build it, or add --build)")
            return None
        return exe if self.fresh_build(name, exe.parent / f"{name}_Data", st) else None

    # what goes into a build: a change to any of these makes an older build stale (not Materials or
    # Settings, which a build itself rewrites and the tidy-up then puts back)
    BUILD_INPUTS = ("Scripts", "Resources", "Scenes", "Prefabs", "Shaders", "Plugins")

    def fresh_build(self, name, data, st):
        """A build older than the newest game script or asset would test old code or old assets."""
        carrier = PROJECT / "Assets" / "Carrier"
        code = max((f.stat().st_mtime for d in self.BUILD_INPUTS for f in (carrier / d).rglob("*")
                    if f.is_file() and f.suffix != ".meta"), default=0)
        built = max((f.stat().st_mtime for f in data.iterdir() if f.is_file()), default=0) if data.is_dir() else 0
        if built < code:
            st.fail(f"the {name} build ({time.strftime('%Y-%m-%d %H:%M', time.localtime(built))}) is older than the game's "
                    f"code or assets ({time.strftime('%Y-%m-%d %H:%M', time.localtime(code))}): rebuild it, or add --build")
            return False
        return True

    def need_websockets(self, st):
        r = subprocess.run([sys.executable, "-c", "import websockets"], capture_output=True)
        if r.returncode != 0:
            st.fail(f"this Python ({sys.executable}) has no 'websockets' package: pip install websockets")
            return False
        return True

    def server(self, st):
        """The co-op server, built from the source for this run."""
        if self.server_exe:
            return self.server_exe
        rid = "win-x64" if WINDOWS else "linux-x64"
        d = self.out / "server"
        code = self.run([self.dotnet, "publish", PROJECT / "Server" / "CoopServer", "-c", "Release", "-r", rid, "-o", d, "--nologo"],
                        self.out / "server_build.log", 600)
        exe = d / f"CoopServer{EXE}"
        if code != 0 or not exe.exists():
            st.fail(f"the co-op server didn't build (see {self.out / 'server_build.log'})")
            return None
        self.server_exe = exe
        return exe

    # ------------------------------------------------------------ stages

    def tools(self, st):
        log = self.out / "tools.log"
        code = self.run([sys.executable, "-B", "-m", "unittest", "discover", "-s", HERE, "-p", "test_*.py"], log, 120)
        text = log.read_text(errors="replace")
        m = re.search(r"Ran (\d+) test", text)
        if m:
            self.count("tools", int(m.group(1)), st)
            st.note(f"{m.group(1)} tests")
        if code != 0:
            st.fail(f"the checker's own tests failed (see {log})")

    def server_tests(self, st):
        log = self.out / "server_tests.log"
        env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        code = self.run([self.dotnet, "test", PROJECT / "Server" / "CoopServer.Tests", "--nologo"], log, 900, env=env)
        text = log.read_text(errors="replace")
        m = re.search(r"(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", text)
        if not m:
            st.fail(f"no test results (see {log})")
            return
        failed, passed, total = int(m.group(2)), int(m.group(3)), int(m.group(5))
        st.note(f"{passed} of {total} passed")
        for line in re.findall(r"^\s+Failed (\S+)", text, re.M):
            st.fail(line)
        if failed or code != 0:
            st.fail(f"{failed} failed (see {log})")
        self.count("server", total, st)

    def unity_tests(self, st):
        before = self.git_dirty()
        for platform in ("EditMode", "PlayMode"):
            xml, log = self.out / f"unity_{platform}.xml", self.out / f"unity_{platform}.log"
            if xml.exists():
                xml.unlink()
            code = self.run([self.unity, "-batchmode", "-nographics", "-projectPath", PROJECT, "-runTests",
                             "-testPlatform", platform, "-testResults", xml, "-logFile", log], self.out / f"unity_{platform}.out", 1800)
            if not xml.exists():
                text = log.read_text(errors="replace") if log.exists() else ""
                why = "another Unity is running with this project open (close the editor)" if "another Unity instance" in text \
                    else "; ".join(sorted(set(re.findall(r"error CS\d+: [^\n]+", text)))[:5]) or f"exit code {code}"
                st.fail(f"{platform}: no results: {why} (see {log})")
                continue
            root = ET.parse(xml).getroot()
            total, passed, failed = int(root.get("total", 0)), int(root.get("passed", 0)), int(root.get("failed", 0))
            st.note(f"{platform}: {passed} of {total} passed")
            for tc in root.iter("test-case"):
                if tc.get("result") == "Failed":
                    msg = (tc.findtext("failure/message") or "").strip().replace("\n", " ")
                    st.fail(f"{tc.get('fullname')}: {msg[:300]}")
            if total == 0:
                st.fail(f"{platform}: no tests found")
            elif failed or root.get("result", "").startswith("Failed"):
                st.fail(f"{platform}: {failed} failed, result {root.get('result')} (see {xml})")
            self.count(f"unity {platform}", total, st)
        self.tidy(before, st)

    def build(self, st):
        wanted = ["CarrierDemo", "AsteroidAmbush", "StarSystem"] + (["StarSystemWeb"] if "web" in self.stages else [])
        before = self.git_dirty()
        for name in wanted:
            log = self.out / f"build_{name}.log"
            t0 = time.time()
            self.run([self.unity, "-batchmode", "-quit", "-projectPath", PROJECT, "-executeMethod",
                      f"Carrier.EditorTools.CarrierPlayerBuild.{BUILD_METHODS[name]}", "-logFile", log], self.out / f"build_{name}.out", 3600)
            text = log.read_text(errors="replace") if log.exists() else ""
            if "Build Finished, Result: Success" not in text:
                errors = "; ".join(sorted(set(re.findall(r"error CS\d+: [^\n]+", text)))[:5])
                st.fail(f"{name} didn't build{': ' + errors if errors else ''} (see {log})")
            else:
                st.note(f"{name} built in {time.time() - t0:.0f} s")
        self.tidy(before, st)

    def serverproc(self, st):
        if not self.need_websockets(st):
            return
        exe = self.server(st)
        if not exe:
            return
        d = self.fresh("serverproc")
        cfg = d / "coopserver.json"
        shutil.copy(PROJECT / "Server" / "CoopServer" / "coopserver.json", cfg)
        port = free_port(7795)
        srv = self.start([exe, "--config", cfg, "--bind", "127.0.0.1", "--port", port], d / "server.log")
        try:
            wait_port(port, 15)
            code = self.play(st, "server_test.py", [sys.executable, "-B", PROJECT / "Tools" / "CoopTest" / "server_test.py",
                                                    "127.0.0.1", port, "--allowtest", cfg], d / "server_test.log", 300)
        finally:
            stop(srv)
        lines = (d / "server_test.log").read_text(errors="replace").splitlines()
        self.selftest_result(st, "serverproc.txt", lines, require_done=False)
        if code != 0:
            st.fail(f"server_test.py exit code {code}")

    def autotest(self, st):
        for name in ("CarrierDemo", "AsteroidAmbush"):
            exe = self.game(name, st)
            if not exe:
                continue
            d = self.fresh(f"autotest_{name}")
            self.play(st, name, [exe] + WINDOW + ["-autotest", d, "-logFile", d / "player.log"], d / "run.out", 600, cwd=exe.parent)
            report = d / "autotest.txt"
            lines = report.read_text(encoding="utf-8", errors="replace").splitlines() if report.exists() else []
            base = self.baseline(f"autotest_{name}.txt")
            if not lines:
                st.fail(f"{name}: no report (see {d / 'player.log'})")
                continue
            # what must hold whatever the baseline says; then the comparison with the baseline
            hard = R.compare_autotest(lines, lines)
            problems = hard if self.args.bless or base is None else R.compare_autotest(base, lines)
            if self.args.bless and base is not None:
                for p in R.compare_autotest(base, lines):
                    st.note(f"{name}: differs from the old baseline: {p}")
            for p in problems[:25]:
                st.fail(f"{name}: {p}")
            if len(problems) > 25:
                st.fail(f"{name}: ... {len(problems) - 25} more")
            if base is None:
                st.note(f"{name}: no baseline yet ({len(lines)} lines); --bless to save one")
            elif not problems:
                st.note(f"{name}: matches its baseline ({len(lines)} lines, {len(R.AUTOTEST_FACTS)} facts)")
            if not hard:
                self.bless(f"autotest_{name}.txt", lines, st)

    def selftest_result(self, st, baseline_name, lines, require_done=True):
        base = self.baseline(baseline_name)
        got = R.SelfTest(lines)
        for kind, text in got.verdicts:
            st.note(f"{kind}: {text}")
        # what must hold whatever the baseline says (every check passed, the test finished); then
        # the comparison with the baseline (no check lost), unless this run is the new baseline
        hard = R.compare_selftest(lines, lines, require_done)
        problems = hard if self.args.bless or base is None else R.compare_selftest(base, lines, require_done)
        if self.args.bless and base is not None:
            for p in R.compare_selftest(base, lines, require_done):
                st.note(f"differs from the old baseline: {p}")
        for p in problems:
            st.fail(p)
        if base is None:
            st.note("no baseline yet; --bless to save one")
        else:
            for t in R.new_checks(base, lines):
                st.note(f"new (not in the baseline): {t}")
        if not hard:
            self.bless(baseline_name, lines, st)

    def selftest(self, st, stage, extra, limit):
        exe = self.game("StarSystem", st)
        if not exe:
            return
        d = self.fresh(stage)
        self.play(st, "StarSystem", [exe] + WINDOW + ["-systemtest", d] + extra + ["-logFile", d / "player.log"], d / "run.out", limit,
                  cwd=exe.parent)
        report = d / "systemtest.txt"
        lines = report.read_text(encoding="utf-8", errors="replace").splitlines() if report.exists() else []
        self.selftest_result(st, f"selftest_{stage}.txt", lines)

    def coop(self, st):
        if not self.need_websockets(st):
            return
        exe = self.game("StarSystem", st)
        if not exe:
            return
        server = self.server(st)
        if not server:
            return
        d = self.fresh("coop")
        port = free_port(7790)
        srv = self.start([server, "--bind", "127.0.0.1", "--port", port], d / "server.log")
        bot = None
        try:
            wait_port(port, 15)
            # the bot joins first, so it hosts
            bot = self.start([sys.executable, "-B", "-u", PROJECT / "Tools" / "CoopTest" / "bot.py", f"ws://127.0.0.1:{port}/coop", "StarSystem"], d / "bot.log")
            # the server logs each player who gets in: wait for the bot before the game joins
            joined = lambda: "joined from" in (d / "server.log").read_text(errors="replace")
            end = time.time() + 20
            while time.time() < end and not joined() and bot.poll() is None:
                time.sleep(0.25)
            if not joined():
                st.fail(f"the bot didn't get in (see {d / 'bot.log'})")
                return
            self.play(st, "StarSystem", [exe] + WINDOW + ["-systemtest", d, "-cooptest", "-connect", f"127.0.0.1:{port}", "-name", "Tester",
                                         "-logFile", d / "player.log"], d / "run.out", 600, cwd=exe.parent)
        finally:
            stop(bot)
            stop(srv)
        report = d / "systemtest.txt"
        lines = report.read_text(encoding="utf-8", errors="replace").splitlines() if report.exists() else []
        self.selftest_result(st, "selftest_coop.txt", lines)

    def web(self, st):
        if not self.need_websockets(st):
            return
        build = self.builds / "StarSystemWeb"
        if not (build / "index.html").exists():
            st.fail(f"no web build in {build} (build it, or add --build)")
            return
        if not self.fresh_build("StarSystemWeb", build / "Build", st):
            return
        server = self.server(st)
        if not server:
            return
        bash = self.args.bash or (r"C:\Program Files\Git\bin\bash.exe" if WINDOWS and Path(r"C:\Program Files\Git\bin\bash.exe").exists() else shutil.which("bash"))
        if not bash:
            st.fail("no bash to run Tools/WebTest/cooptest.sh (Git Bash on Windows)")
            return
        d = self.fresh("web")
        env = dict(os.environ, PYTHON=posix(sys.executable), COOPSERVER=posix(server), PYTHONDONTWRITEBYTECODE="1")
        code = self.run([bash, posix(PROJECT / "Tools" / "WebTest" / "cooptest.sh"), posix(build), "300"], d / "cooptest.log", 600, env=env)
        if code is None:
            st.fail("cooptest.sh: still running after 600 s, so stopped")
        lines = (d / "cooptest.log").read_text(errors="replace").splitlines()
        self.selftest_result(st, "selftest_web.txt", [l for l in lines if l.startswith("[SystemTest]")], require_done=False)
        if code != 0:
            st.fail(f"cooptest.sh exit code {code}")

    # ------------------------------------------------------------ the run

    def main(self):
        a = self.args
        stages = [s.strip() for s in a.only.split(",")] if a.only else QUICK if a.quick else [s for s in STAGES if s != "build"]
        if a.build:
            stages = stages + ["build"]
        skip = [s.strip() for s in a.skip.split(",")] if a.skip else []
        stages = [s for s in stages if s not in skip]
        unknown = [s for s in stages + skip if s not in STAGES]
        if unknown:
            say(f"unknown stage(s): {', '.join(unknown)}; the stages are {', '.join(STAGES)}")
            return 2
        self.stages = [s for s in STAGES if s in stages]
        self.out.mkdir(parents=True, exist_ok=True)
        say(f"Battlecruiser 4000 BC tests: {', '.join(self.stages)}")
        say(f"  project {PROJECT}\n  builds  {self.builds}\n  output  {self.out}")
        results = []
        for name in self.stages:
            st = Stage(name)
            say(f"\n== {name} ({time.strftime('%H:%M:%S')})")
            try:
                if name in SELFTESTS:
                    self.selftest(st, name, *SELFTESTS[name])
                else:
                    {"tools": self.tools, "server": self.server_tests, "unity": self.unity_tests, "build": self.build,
                     "serverproc": self.serverproc, "autotest": self.autotest, "coop": self.coop, "web": self.web}[name](st)
            except Exception as e:  # a broken stage mustn't stop the others
                st.fail(f"{type(e).__name__}: {e}")
            st.seconds = time.time() - st.started
            say(f"   {'pass' if st.ok else 'FAIL'} in {st.seconds:.0f} s")
            results.append(st)
        if a.bless and self.new_counts:
            counts = dict(self.counts, **self.new_counts)
            COUNTS.write_text(json.dumps(counts, indent=2, sort_keys=True) + "\n")
            say(f"\nsaved the test counts as the baseline: {counts}")
        failed = [r for r in results if not r.ok]
        lines = ["", "== summary"] + [f"  {'pass' if r.ok else 'FAIL'}  {r.name:<11} {r.seconds:6.0f} s"
                                       + ("" if r.ok else f"  {r.failures[0]}") for r in results]
        lines.append(f"{'PASS' if not failed else 'FAIL'}: {len(results) - len(failed)} of {len(results)} stages passed")
        for line in lines:
            say(line)
        (self.out / "summary.txt").write_text("\n".join(
            [f"{r.name}: {'pass' if r.ok else 'FAIL'} ({r.seconds:.0f} s)\n" + "".join(f"    {n}\n" for n in r.notes) for r in results]
            + lines[1:]) + "\n", encoding="utf-8")
        return 1 if failed else 0


def posix(p):
    """A path as Git Bash takes it (forward slashes)."""
    return str(p).replace("\\", "/")


def free_port(preferred):
    for port in [preferred] + list(range(preferred + 10, preferred + 60)):
        with socket.socket() as s:
            if s.connect_ex(("127.0.0.1", port)) != 0:
                return port
    raise RuntimeError("no free port for the co-op server")


def wait_port(port, seconds):
    end = time.time() + seconds
    while time.time() < end:
        with socket.socket() as s:
            if s.connect_ex(("127.0.0.1", port)) == 0:
                return
        time.sleep(0.25)
    raise RuntimeError(f"the co-op server didn't start listening on port {port}")


def stop(p):
    if p is None:
        return
    if p.poll() is None:
        if WINDOWS:
            # the whole tree: the editor's import workers, or the web test's Chrome, server and bot
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(p.pid)], capture_output=True)
        else:
            p.terminate()
        try:
            p.wait(timeout=10)
        except subprocess.TimeoutExpired:
            p.kill()
            p.wait(timeout=10)
    f = getattr(p, "log_file", None)
    if f:
        f.close()


def parse(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter,
                                 epilog=__doc__.split("\n\n", 1)[1])
    ap.add_argument("--quick", action="store_true", help="the quicker stages only (about 15 minutes)")
    ap.add_argument("--build", action="store_true", help="build the games first (the Unity editor must be closed)")
    ap.add_argument("--only", help="just these stages, comma-separated")
    ap.add_argument("--skip", help="leave out these stages, comma-separated")
    ap.add_argument("--bless", action="store_true", help="save this run's reports as the new baselines")
    ap.add_argument("--builds", default=str(PROJECT.parent / "Builds"), help="where the builds are (default ../Builds)")
    ap.add_argument("--out", help="where to write the results (default <builds>/test_run)")
    ap.add_argument("--unity", help="the Unity editor program (default UNITY_EXE, or Unity 6000.6.7f1's usual place)")
    ap.add_argument("--bash", help="bash, for the web stage (default Git Bash)")
    return ap.parse_args(argv)


if __name__ == "__main__":
    sys.exit(Runner(parse()).main())
