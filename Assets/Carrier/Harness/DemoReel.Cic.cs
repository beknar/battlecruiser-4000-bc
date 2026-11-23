using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The CIC reel: StarSystem.exe -reel &lt;folder&gt; -cic. Five frigates by default (the Sentinel, Lancer,
    /// Warden, Talon and Vanguard, each flown by the battle simulator's SimPilot) come in line
    /// abreast against the Vrask picket over Sheol in the Nine Hells (two Maws, three Snares, two
    /// Fangs, three Skinks, fighting as a fleet), and fight it out to the end. The reel cuts
    /// between combat shots on the hottest exchange (a tractor lock, a Fang's run or clamp, a bow
    /// beam, the fleet's target) and the tactical display (TacticalDisplay) full screen, now and
    /// then keeping it as an inset over the picture. It plays till one side is destroyed or the
    /// Vrask withdraw (losing their Maws and Snares), or 12 minutes of battle; a lull with nothing
    /// hit is cut. Then the results card, the final analysis (the battle replayed on the plot at
    /// 10-20x with callouts at its key moments) and an end card. The reel opens the Nine Hells by
    /// itself. Each part runs under a time limit (Safe), and the reel always ends.
    ///
    /// Its options: -cicfleet Talon,Vanguard (the frigates, in their order: the first leads),
    /// -cicmix 2,3,2,3 (the picket made over to that many Maws, Snares, Fangs and Skinks:
    /// VraskMix) and -cicsection (the frigates flown as a section: SimPilot.section). Each
    /// frigate's move and the reason for her target go in the reel's log as they change ("cic
    /// blue"), for a voice-over's radio calls.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>The tactical display's mode in the reel's log (for a voice-over's timing).</summary>
        void CicMode() { if (tac != null) Note($"cic mode {tac.mode}"); }

        /// <summary>Recording the CIC reel (-cic).</summary>
        public static bool CicReel;
        static bool cicOpening;

        TacticalDisplay tac;
        BattleSim.Encounter cicEnc;
        EncounterGroup cicGroup;
        VraskFleet cicFleet;
        readonly List<SimPilot> cicBlue = new List<SimPilot>();
        readonly List<VraskShip> cicRed = new List<VraskShip>();
        TacticalDisplay.Track cicSubject;
        Transform cicMark;                      // the camera's mark between two ships (side-on shots)
        string cicOutcome = "", cicWinner = "", cicLine = "";
        float cicWithdrawAt = -1f, cicCapAt = -100f, cicReelStart = -1f;
        int cicMoments;
        bool cicDone;
        const float CicLimit = 720f;            // the battle's longest (game seconds)
        static readonly string[] CicOrder = { "Sentinel", "Lancer", "Warden", "Talon", "Vanguard" };
        static readonly string[] CicWords = { "No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve" };
        static string CicWord(int n) => n >= 0 && n < CicWords.Length ? CicWords[n] : n.ToString();
        readonly Dictionary<SimPilot, (string doing, string why, Transform target)> cicDoing = new Dictionary<SimPilot, (string, string, Transform)>();

        /// <summary>Not in the Nine Hells: open it, and `then` once it is (false: it is here
        /// already, or can't be opened).</summary>
        static bool CicOpenNineHells(Action then)
        {
            var nh = StarSystems.Find("ninehells");
            if (cicOpening || nh == null || !StarSystems.Available(nh) || SceneManager.GetActiveScene().name == nh.scene) return false;
            cicOpening = true;
            void Loaded(Scene sc, LoadSceneMode m)
            {
                if (sc.name != nh.scene) return;
                SceneManager.sceneLoaded -= Loaded;
                then();
            }
            SceneManager.sceneLoaded += Loaded;
            if (StarSystems.Open(nh)) return true;
            SceneManager.sceneLoaded -= Loaded;
            return false;
        }

        /// <summary>Every frame (from Update): a last cap on the reel's length, and the camera never
        /// left on a ship that has gone.</summary>
        void CicUpdate()
        {
            if (cicReelStart < 0f) cicReelStart = Time.time;
            Cic3Dump();
            if (!finished && Time.time - cicReelStart > 1700f) { Note("cic: the reel's time cap"); finished = true; }
            // each frigate's move, as it changes (for the voice-over's radio calls)
            foreach (var p in cicBlue)
            {
                if (p == null || tac == null || tac.mode == TacticalDisplay.Mode.Results) continue;
                var now = (p.Doing, p.Why, p.Target);
                if (cicDoing.TryGetValue(p, out var was) && was == now) continue;
                cicDoing[p] = now;
                Note($"cic blue T+{tac.ClockText} {(p.ship != null ? p.ship.shipName : p.name)}: {p.Doing}{(p.section && !string.IsNullOrEmpty(p.Why) ? " [" + p.Why + "]" : "")} " +
                     $"on {(p.Target != null ? p.Target.name : "-")}, shield {p.ShieldFraction:P0}, hull {p.HullFraction:P0}");
            }
            if (cam != null && cam.target == null && tac != null)
            {
                var k = tac.Tracks.FirstOrDefault(x => !x.dead && x.t != null);
                if (k != null) cam.target = k.t;
                else if (cc != null) cam.target = cc.transform;
            }
        }

        static int RoleOrder(VraskShip.Role r) => r == VraskShip.Role.Maw ? 0 : r == VraskShip.Role.Snare ? 1 : r == VraskShip.Role.Fang ? 2 : 3;

        static string Mmss(float s) => $"{Mathf.FloorToInt(Mathf.Max(0f, s) / 60f):00}:{Mathf.FloorToInt(Mathf.Max(0f, s) % 60f):00}";

        IEnumerator CicShowReel()
        {
            try
            {
                recording = false;
                while (frame < Warmup) yield return null;
                hullF = cc.GetComponent<Health>();
                if (hullF != null) hullF.cannotDie = true;
                SideGuns(false);
                Note($"the CIC reel, in {StarSystems.Current.name} (scene {SceneManager.GetActiveScene().name})");
                bool ok = false;
                yield return Safe("cic: setting up", Cic3 ? Cic3Setup(r => ok = r) : CicSetup(r => ok = r), 120f);
                if (ok && tac != null)
                {
                    yield return Safe("cic: the battle", CicBattle(), (Cic3 ? Cic3Limit : CicLimit) + 300f);
                    CicFinish();
                    yield return Safe("cic: results", CicResults(), 40f);
                    yield return Safe("cic: analysis", CicAnalysis(), 160f);
                }
                else Note(Cic3 ? "cic3: no bands or no frigates: the battle is left out" : "cic: no Vrask picket or no frigates: the battle is left out");
                yield return Safe("cic: the end", CicEndCard(), 30f);
                Note($"the end: {cicOutcome}");
            }
            finally { finished = true; if (Cic3) FireFx.Off = false; }
        }

        // ------------------------------------------------------------------ setting up

        /// <summary>"Up" at a point: out from the world it is over (ships fly level over it), else the world's.</summary>
        static Vector3 CicUp(Vector3 c)
        {
            var body = PlanetFlight.NearestBody(c, out float alt);
            if (body == null || alt > Mathf.Max(200000f, body.bodyRadius * 3f)) return Vector3.up;
            Vector3 u = c - body.Position;
            return u.sqrMagnitude > 1f ? u.normalized : Vector3.up;
        }

        /// <summary>Which way from the picket the frigates come in: level with it, from the star's
        /// side (the star behind them) if the star isn't straight overhead or underfoot.</summary>
        static Vector3 CicStarDir(Vector3 c)
        {
            Vector3 up = CicUp(c);
            var star = WarpPoint.All.FirstOrDefault(w => w != null && w.kind == WarpPoint.Kind.Star);
            Vector3 d = star != null ? Vector3.ProjectOnPlane(star.Position - c, up) : Vector3.zero;
            if (star != null && d.magnitude > 0.3f * (star.Position - c).magnitude) return d.normalized;
            d = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward);
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }

        IEnumerator CicSetup(Action<bool> done)
        {
            done(false);
            var groups = FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None);
            cicGroup = groups.FirstOrDefault(g => g != null && g.groupName == "Sheol picket");
            if (cicGroup == null)
                cicGroup = groups.Where(g => g != null && g.pilots != null && g.GetComponent<VraskFleet>() != null).OrderByDescending(g => g.pilots.Length).FirstOrDefault();
            var mix = VraskMix.Parse(LaunchArgs.ValueOf("-cicmix"));
            if (mix != null && cicGroup != null)
            {
                Note($"cic: the picket made over: {VraskMix.Apply(cicGroup, mix)}");
                yield return null;
            }
            cicEnc = BattleSim.Encounter.FromGroup(cicGroup);
            if (cicEnc == null) { Note("cic: no Vrask band to fight"); yield break; }
            cicFleet = cicGroup.GetComponent<VraskFleet>();
            cicRed.Clear();
            cicRed.AddRange(cicEnc.enemies.Where(h => h != null).Select(h => h.GetComponent<VraskShip>()).Where(v => v != null)
                                  .OrderBy(v => RoleOrder(v.role)).ThenBy(v => v.name));
            Note($"cic: {cicGroup.groupName}: {string.Join(", ", cicRed.Select(v => $"{v.name} ({v.role})"))}; fleet coordination {(cicFleet != null ? "on" : "none")}");

            // nothing else comes back or happens on its own (as in the battle simulator)
            if (SpawnDirector.Instance != null) SpawnDirector.Instance.enabled = false;
            foreach (var b in FindObjectsByType<VraskBombardment>(FindObjectsSortMode.None)) b.enabled = false;
            // the player's own ship out of it: hidden, no target, well back on the star's side
            // (where it is, the world's origin is: the fight stays near it)
            BattleSim.Hide(cc, true);
            Vector3 c = cicEnc.Centre;
            Vector3 dir = CicStarDir(c);
            Place(BattleSim.Clear(c + dir * 17000f, dir), c);
            yield return Wait(1.5f);
            BattleSim.Hide(cc, true);
            yield return BattleSim.ResetEncounter(cicEnc);
            // (a Vrask ship lost stays lost: none comes back mid-battle)
            foreach (var h in cicEnc.enemies) if (h != null) h.respawnHeld = true;

            // the frigates, line abreast 11 km off, bows on the picket
            c = cicEnc.Centre;
            dir = CicStarDir(c);
            Vector3 start = BattleSim.Clear(c + dir * 11000f, dir);
            Vector3 side = Vector3.Cross(CicUp(c), -dir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            var roster = FrigateRoster.Get();
            var order = (LaunchArgs.ValueOf("-cicfleet") ?? "").Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            if (order.Length == 0) order = CicOrder;
            var all = (roster != null ? roster.frigates : new FrigateRoster.Entry[0]).Where(e => e != null && e.prefab != null && e.category == FrigateRoster.Frigate).ToList();
            var entries = order.Select(n => all.FirstOrDefault(e => string.Equals(n, e.displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(n, e.id, StringComparison.OrdinalIgnoreCase)))
                               .Where(e => e != null).Take(5).ToList();
            cicBlue.Clear();
            for (int k = 0; k < entries.Count; k++)
            {
                float off = (k - (entries.Count - 1) * 0.5f) * 650f;
                Vector3 at = start + side * off;
                SimPilot p = null;
                try { p = BattleSim.SpawnFrigate(entries[k], at, Quaternion.LookRotation(c - at, CicUp(c)), true, k, cicEnc, entries[k].displayName); }
                catch (Exception e) { Note($"cic: {entries[k].displayName}: {e.GetType().Name}: {e.Message}"); }
                if (p != null) cicBlue.Add(p);
            }
            if (cicBlue.Count == 0) { Note("cic: couldn't make the frigates"); yield break; }
            bool section = LaunchArgs.Has("-cicsection");
            foreach (var p in cicBlue) { p.fleet = cicBlue; p.encounter = cicEnc; p.section = section; }
            cam.maxDistance = Mathf.Max(cam.maxDistance, 9000f);
            if (cicMark == null) cicMark = new GameObject("CIC camera mark").transform;
            yield return null;

            Transform frameOf = cicBlue[0] != null && cicBlue[0].TryGetComponent(out OrbitHome oh) ? oh.home : null;
            tac = gameObject.AddComponent<TacticalDisplay>();
            tac.Logged += line => Note("cic  " + line);
            // (drawn level over Sheol, the picket at the top)
            tac.Begin(frameOf, CicUp(c), -dir, cicBlue, cicRed.Cast<FactionShip>().ToList(), cicFleet, cicGroup, "SHEOL");
            cicLine = string.Join(", ", cicBlue.Select(p => p.ship != null ? p.ship.shipName : p.name));
            Note($"cic: blue {cicLine}{(section ? ", flown as a section" : "")}, {Vector3.Distance(start, c) / 1000f:F1} km from the picket; positions on {(frameOf != null ? frameOf.name : "the world")}");
            done(true);
        }

        // ------------------------------------------------------------------ the battle

        IEnumerator CicBattle()
        {
            // the opening: behind the blue line, looking out at the picket; then the CIC
            var mid = cicBlue[cicBlue.Count / 2];
            Vector3 c = cicEnc.Centre;
            if (mid != null)
            {
                float size = SizeOf(mid.transform);
                Shot(1100, mid.transform, Vector3.zero, size * 3.4f, Bearing(mid.transform.position, c) + 24f, 9f, -1.5f);
            }
            yield return Wait(0.5f);
            yield return FadeIn(1f);
            Caption("BATTLECRUISER 4000 BC", Cic3 ? Cic3Title : $"{CicWord(cicBlue.Count)} frigate{(cicBlue.Count == 1 ? "" : "s")} against {CicWord(cicRed.Count).ToLowerInvariant()} Vrask over Sheol, in the Nine Hells", 6f);
            cicCapAt = Time.time;
            yield return CicHold(6.5f, false);
            tac.mode = TacticalDisplay.Mode.Full; CicMode();
            string roles = VraskMix.Describe(cicRed);
            if (Cic3) Caption("COMBAT INFORMATION CENTRE", Cic3Roster, 7f);
            else Caption("COMBAT INFORMATION CENTRE", $"Blue: {cicLine}, {(cicBlue.Count == 2 ? (cicBlue[0].section ? "a section, " + cicLine.Split(',')[0] + " leading" : "a pair") : "line abreast")}. Red: {roles}", 7f);
            cicCapAt = Time.time;
            yield return CicHold(8f, true);
            tac.mode = TacticalDisplay.Mode.Off; CicMode();

            int n = 0;
            while (!CicOver())
            {
                // combat, 12 to 18 s (every third with the plot as an inset)
                tac.mode = n % 3 == 1 ? TacticalDisplay.Mode.Inset : TacticalDisplay.Mode.Off; CicMode();
                yield return CicCombat(n, 12f + (n * 5) % 7);
                if (CicOver()) break;
                // the CIC full screen, 6 to 8 s
                perFrame = null;
                tac.mode = TacticalDisplay.Mode.Full; CicMode();
                yield return CicHold(n % 2 == 0 ? 8f : 6.5f, true);
                tac.mode = TacticalDisplay.Mode.Off; CicMode();
                // a lull: nothing hit for a while, cut ahead
                if (!CicOver() && tac.SinceLastHit > 35f) yield return CicSkipLull();
                n++;
            }
            yield return CicAftermath();
        }

        /// <summary>The battle over? (one side gone, the Vrask withdrawn 20 s since, or the time
        /// limit): the outcome and the winner set once.</summary>
        bool CicOver()
        {
            if (Cic3) return Cic3Over();
            if (cicDone) return true;
            if (tac == null) { cicOutcome = "NO BATTLE"; return cicDone = true; }
            int blue = tac.BlueLeft, red = tac.RedLeft;
            if (red == 0) { cicWinner = "BLUE"; cicOutcome = $"THE VRASK PICKET DESTROYED: ALL {tac.RedCount} SHIPS"; return cicDone = true; }
            if (blue == 0) { cicWinner = "RED"; cicOutcome = $"ALL {tac.BlueCount} FRIGATES LOST"; return cicDone = true; }
            bool withdraw = (cicGroup != null && cicGroup.holdOff) || (cicFleet != null && cicFleet.Current == VraskFleet.Phase.Withdraw);
            if (withdraw && cicWithdrawAt < 0f)
            {
                cicWithdrawAt = Time.time;
                Note($"cic: the Vrask withdraw at T+{tac.ClockText} ({(cicFleet != null ? cicFleet.Why : "the band holds off")})");
            }
            if (cicWithdrawAt >= 0f && Time.time - cicWithdrawAt > 20f)
            {
                cicWinner = "BLUE";
                cicOutcome = $"THE VRASK WITHDRAW: {tac.RedCount - red} OF {tac.RedCount} LOST, {red} BREAK OFF";
                return cicDone = true;
            }
            if (tac.Clock > CicLimit) { CicTimeUp(); return cicDone = true; }
            return false;
        }

        void CicTimeUp()
        {
            if (Cic3) { Cic3TimeUp(); return; }
            float StrengthOf(bool blue)
            {
                var all = tac.Tracks.Where(k => k.friendly == blue).ToList();
                return all.Count == 0 ? 0f : all.Sum(k => k.dead ? 0f : k.hull) / all.Count;
            }
            float b = StrengthOf(true), r = StrengthOf(false);
            cicWinner = b > r + 0.05f ? "BLUE" : r > b + 0.05f ? "RED" : "";
            cicOutcome = $"TIME LIMIT T+{Mmss(tac.Clock)}: BLUE {tac.BlueLeft}/{tac.BlueCount} AT {b:P0}, RED {tac.RedLeft}/{tac.RedCount} AT {r:P0}";
        }

        IEnumerator CicHold(float seconds, bool stopWhenOver)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                CicCaptions();
                if (stopWhenOver && CicOver()) yield break;
                yield return null;
            }
        }

        IEnumerator CicCombat(int n, float len)
        {
            float until = Time.time + len, recut = Time.time + len * 0.55f;
            CicFrame(n);
            while (Time.time < until && !CicOver())
            {
                CicCaptions();
                bool gone = cicSubject == null || cicSubject.t == null || (cicSubject.dead && tac.Clock - cicSubject.diedAt > 3.5f)
                            || (cic3PovFoe != null && cic3PovFoe.dead && tac.Clock - cic3PovFoe.diedAt > 3.5f)
                            || (cic3PovShip != null && cic3PovShip.dead && tac.Clock - cic3PovShip.diedAt > 3.5f);
                if (Time.time > recut) { recut = float.MaxValue; CicFrame(n + 5); }
                else if (gone && Time.time < until - 3f) CicFrame(n + 3);
                yield return null;
            }
        }

        /// <summary>The camera on the hottest exchange (TacticalDisplay.Hottest): over the first
        /// ship's shoulder at the second, on the second under fire from the first, or side-on to
        /// both; it eases round to keep them so.</summary>
        void CicFrame(int variant)
        {
            cic3PovFoe = cic3PovShip = null;
            if (Cic3 && Cic3Pov && Cic3PovFrame(variant)) return;
            var a = tac.Hottest(variant, out var b, out string why);
            cicSubject = a;
            if (a == null || a.t == null) { perFrame = null; return; }
            Transform at = a.t, bt = b != null ? b.t : null;
            Vector3 ap = at.position, bp = bt != null ? bt.position : ap + at.forward * 1500f;
            float range = Vector3.Distance(ap, bp);
            int kind = bt == null ? 0 : (variant + (why == "tractor" ? 2 : 0)) % 3;
            float sign = variant % 2 == 0 ? 1f : -1f;
            Transform on;
            float yawOff, dist, pitch;
            switch (kind)
            {
                case 0: on = at; yawOff = 16f * sign; dist = SizeOf(at) * 3.2f; pitch = 7f; break;
                case 1: on = bt; yawOff = 32f * sign; dist = SizeOf(bt) * 3.6f; pitch = 10f; cicSubject = b; break;
                default:
                    on = cicMark;
                    cicMark.position = (ap + bp) * 0.5f;
                    yawOff = 90f * sign; dist = Mathf.Clamp(range * 1.05f + 300f, 900f, 8500f); pitch = 16f; break;
            }
            Shot(1200 + variant, on, Vector3.zero, dist, Bearing(ap, bp) + yawOff, pitch, 0f);
            Note($"cic camera ({(kind == 0 ? "shoulder" : kind == 1 ? "under fire" : "side-on")}): {a.Label} [{why}] and {(b != null ? b.Label : "-")}, {range / 1000f:F1} km");
            perFrame = () =>
            {
                if (at == null || cam == null) return;
                Vector3 p1 = at.position, p2 = bt != null ? bt.position : p1 + at.forward * 1500f;
                if (kind == 2 && cicMark != null) cicMark.position = (p1 + p2) * 0.5f;
                cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(p1, p2) + yawOff, Time.deltaTime * 1.2f);
            };
        }

        /// <summary>A caption for the latest moment (a phase, a kill, a lock), one at a time.</summary>
        void CicCaptions()
        {
            if (tac == null) return;
            while (cicMoments < tac.Moments.Count)
            {
                var m = tac.Moments[cicMoments];
                if (Time.time - m.t > 6f) { cicMoments++; continue; }
                if (Time.time - cicCapAt < 6.5f) return;
                cicMoments++;
                cicCapAt = Time.time;
                Caption(m.title, m.text, 6f);
                return;
            }
        }

        IEnumerator CicSkipLull()
        {
            Note($"cic: {tac.SinceLastHit:F0} s without a hit: cutting ahead");
            perFrame = null;
            yield return FadeOut(0.4f);
            float c0 = tac.Clock, until = Time.time + 150f;
            while (Time.time < until && tac.SinceLastHit > 2f && !CicOver()) yield return null;
            tac.mode = TacticalDisplay.Mode.Full; CicMode();
            yield return FadeIn(0.4f);
            Caption("LATER", $"{Mmss(tac.Clock - c0)} on: battle clock T+{tac.ClockText}", 4f);
            cicCapAt = Time.time;
            yield return CicHold(5f, true);
            tac.mode = TacticalDisplay.Mode.Off; CicMode();
        }

        /// <summary>The battle's last moments: a survivor of the winning side, then the CIC.</summary>
        IEnumerator CicAftermath()
        {
            Note($"cic: the battle is over at T+{tac.ClockText}: {cicOutcome}");
            perFrame = null;
            int winSide = Array.IndexOf(tac.SideNames, cicWinner);
            var last = Cic3 ? tac.Tracks.Where(k => !k.dead && k.t != null).OrderByDescending(k => k.side == winSide).FirstOrDefault()
                            : tac.Tracks.Where(k => !k.dead && k.t != null).OrderByDescending(k => k.friendly == (cicWinner != "RED")).FirstOrDefault();
            tac.mode = TacticalDisplay.Mode.Off; CicMode();
            string title = cicWinner == "BLUE" ? (Cic3 ? "THE MANDATE OF HEAVEN PREVAILS" : "BLUE FORCE VICTORY")
                         : Cic3 && winSide == 1 ? "THE EMPIRE PREVAILS" : Cic3 && winSide == 2 ? "THE GREYS PREVAIL"
                         : cicWinner == "RED" ? "THE VRASK PREVAIL" : "NO DECISION";
            if (last != null)
            {
                Shot(1300, last.t, Vector3.zero, SizeOf(last.t) * 4f, last.t.eulerAngles.y + 150f, 12f, 3f);
                Caption(title, cicOutcome, 6f);
                cicCapAt = Time.time;
                yield return Wait(6f);
            }
            tac.mode = TacticalDisplay.Mode.Full; CicMode();
            if (last == null) Caption(title, cicOutcome, 4f);
            yield return Wait(4f);
        }

        void CicFinish()
        {
            try
            {
                if (tac == null) return;
                // (cut short, or at the limit: decided on what is left)
                if (!CicOver() || string.IsNullOrEmpty(cicOutcome)) CicTimeUp();
                tac.End(cicOutcome, cicWinner);
                Note($"cic: {cicOutcome}; blue dealt {tac.BlueDealt:F0}, red dealt {tac.RedDealt:F0}, {tac.Keys.Count} key events");
                foreach (var a in tac.Analysis) Note("cic analysis: " + a);
                foreach (var k in tac.Tracks) Note($"cic  {k.Label}: {(k.dead ? (k.captured ? "captured" : "destroyed") : "survived")}, kills {k.kills}, dealt {k.dealt:F0}, taken {k.taken:F0}");
            }
            catch (Exception e) { Note($"cic: finishing: {e.GetType().Name}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ the ending

        IEnumerator CicResults()
        {
            perFrame = null;
            tac.mode = TacticalDisplay.Mode.Results; CicMode();
            yield return FadeIn(0.6f);
            yield return Wait(13f);
        }

        IEnumerator CicAnalysis()
        {
            float dur = Mathf.Max(1f, tac.Duration);
            tac.replaySpeed = Mathf.Clamp(dur / 40f, 10f, 20f);
            tac.replayT = 0f;
            tac.mode = TacticalDisplay.Mode.Replay; CicMode();
            yield return FadeIn(0.6f);
            Caption("FINAL ANALYSIS", $"The whole battle replayed at {tac.replaySpeed:F0}x: every ship's track, who fired on whom, and the key moments", 6f);
            perFrame = () => { if (tac != null) tac.replayT = Mathf.Min(dur, tac.replayT + Time.deltaTime * tac.replaySpeed); };
            float until = Time.time + dur / tac.replaySpeed + 10f;
            while (tac.replayT < dur && Time.time < until) yield return null;
            yield return Wait(6f);
            perFrame = null;
        }

        IEnumerator CicEndCard()
        {
            perFrame = null;
            if (tac != null) { tac.mode = TacticalDisplay.Mode.EndCard; CicMode(); }
            else
            {
                Shot(1400, cc.transform, demo.ShipViewOffset, D * 3f, cc.Heading + 150f, 12f, 3f);
                Caption("BATTLECRUISER 4000 BC", "The CIC reel", 5f);
            }
            yield return FadeIn(0.8f);
            yield return Wait(6f);
            yield return FadeOut(1.5f);
            if (tac != null) { tac.mode = TacticalDisplay.Mode.Off; CicMode(); }
        }
    }
}
