using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The three-way CIC reel: StarSystem.exe -reel &lt;folder&gt; -cic -cic3. Five frigates of the
    /// Mandate of Heaven (Blue; -cicfleet, by default the Vanguard, Talon, Sentinel, Lancer and
    /// Warden, line abreast, with their standard guns and bow beams), ten ships of the Empire (Red)
    /// and ten of the Greys (Magenta), all at war with each other (Rivalry): every ship sees every
    /// other (Rivalry.RevealAll) and each faction hunts the other's ships as it does the players'.
    /// They meet over Goliath in the Nine Hells, at the corners of a triangle 3 km a side
    /// (-cic3side), both bands sprung at once. The camera is with the Mandate's ships (-cic3overview
    /// for the whole battle's hottest exchange instead): behind one of them, on her target as her
    /// fire goes in, or on her as she is hit; the wrecks don't burn (FireFx.Off).
    ///
    /// The bands are made over for combat: -cic3empire 4,3,2,1 (Assault, Patrol, Raider, Recon:
    /// the line of battle, its CIWS screen, the raiders and a painter for the guns) and -cic3greys
    /// 1,2,7,0 (Scout, Shroud, Lance, Reaper: a painter, two jamming shields and seven strike mantas;
    /// the Reaper's capture is for players' ships only, so none). Neither breaks off: the Greys
    /// don't vanish, the Empire doesn't fall back, the Raiders fight on hurt, and no ship goes home
    /// on its leash. It plays till one side is left, or the time limit.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the three-way battle (-cic -cic3).</summary>
        static bool Cic3 => LaunchArgs.Has("-cic3");

        EncounterGroup cicEmpireGroup, cicGreysGroup;
        EmpireFleet cicEmpireFleet;
        GreysFleet cicGreysFleet;
        readonly List<FactionShip> cicEmpire = new List<FactionShip>();
        readonly List<FactionShip> cicGreys = new List<FactionShip>();
        static readonly string[] Cic3Fleet = { "Vanguard", "Talon", "Sentinel", "Lancer", "Warden" };
        // the triangle's side (m): -cic3side
        static float Cic3Side => float.TryParse(LaunchArgs.ValueOf("-cic3side"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 500f ? v : 3000f;
        /// <summary>The camera with the Mandate of Heaven's ships (not -cic3overview).</summary>
        static bool Cic3Pov => !LaunchArgs.Has("-cic3overview");
        // the factions' weapons refitted for the battle, if asked: their damage times this
        // (-cic3refit for the Empire, -cic3refitgreys for the Greys; 1, as in the game, by default)
        static float Cic3Arg(string name, float dflt) => float.TryParse(LaunchArgs.ValueOf(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f ? v : dflt;
        static float Cic3RefitEmpire => Cic3Arg("-cic3refit", 1f);
        static float Cic3RefitGreys => Cic3Arg("-cic3refitgreys", 1f);
        const float Cic3Lull = 240f;             // no ship lost this long (after 5 min): the battle is called (game s)
        const float Cic3Limit = 900f;            // the three-way battle's longest (game seconds)
        static float Cic3Arena => Mathf.Max(2500f, Cic3Side * 0.6f);   // how far from the middle their ships fight (m)

        static EncounterGroup Cic3Band<T>(string name) where T : Component
        {
            var groups = FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None);
            return groups.FirstOrDefault(g => g != null && g.groupName == name && g.GetComponent<T>() != null)
                   ?? groups.Where(g => g != null && g.pilots != null && g.GetComponent<T>() != null).OrderByDescending(g => g.pilots.Length).FirstOrDefault();
        }

        /// <summary>A band moved to `to` in the place `frame` (carried along with it, as a Greys
        /// Scout's call-in moves one: ScoutTactics.CallIn), its ships facing `centre`.</summary>
        static void Cic3Move(EncounterGroup g, Vector3 to, Vector3 centre, Vector3 up, Transform frame)
        {
            var pilots = g.pilots.Where(p => p != null).ToList();
            if (pilots.Count == 0) return;
            Vector3 c0 = pilots.Aggregate(Vector3.zero, (a, p) => a + p.transform.position) / pilots.Count;
            foreach (var p in pilots)
            {
                Vector3 off = Vector3.ProjectOnPlane(p.transform.position - c0, up);
                // (packed close: the bands start almost in contact)
                off = Vector3.ClampMagnitude(off * 0.4f, Cic3Side * 0.22f);
                var oh = p.GetComponent<OrbitHome>();
                if (frame != null) { if (oh == null) oh = p.gameObject.AddComponent<OrbitHome>(); oh.home = frame; }
                else if (oh != null) oh.home = null;
                p.transform.position = to + off;
                p.transform.rotation = Quaternion.LookRotation(centre - p.transform.position, up);
                p.Repost();
                if (OrbitalSystem.Instance != null) OrbitalSystem.Instance.Forget(p.transform);
            }
            g.transform.position = to;
        }

        /// <summary>Made over for the fight to the end: no breaking off, no leash.</summary>
        static void Cic3ForCombat(EncounterGroup g)
        {
            foreach (var p in g.pilots.Where(p => p != null))
            {
                p.leashDistance = 0f;
                // (no escort's leash: a Lance strikes wherever its target is, not round its Shroud:
                // the arena's leash takes its place)
                p.guard = null;
                if (p.TryGetComponent(out RaiderTactics rt)) rt.breakBelow = 0f;
                if (p.TryGetComponent(out ReaperTactics rp)) rp.breakBelow = 0f;
            }
            if (g.TryGetComponent(out EmpireFleet ef)) ef.retreatBelow = 0f;
            if (g.TryGetComponent(out GreysFleet gf)) gf.fightToTheEnd = true;
            foreach (var r in g.GetComponents<GreysRaid>()) r.enabled = false;
            foreach (var w in g.GetComponents<EncounterPatrol>()) w.enabled = false;
        }

        IEnumerator Cic3Setup(Action<bool> done)
        {
            done(false);
            cicGreysGroup = Cic3Band<GreysFleet>("Greys cell");
            cicEmpireGroup = Cic3Band<EmpireFleet>("Imperial battle group");
            if (cicGreysGroup == null || cicEmpireGroup == null) { Note("cic3: no Greys band or no Imperial band"); yield break; }
            // nothing else comes back, calls anyone in or happens on its own
            if (SpawnDirector.Instance != null) SpawnDirector.Instance.enabled = false;
            foreach (var b in FindObjectsByType<VraskBombardment>(FindObjectsSortMode.None)) b.enabled = false;
            ScoutTactics.CallInOff = true;
            // (no wrecks burning: the fire pack's smoke draws as dark squares against space)
            FireFx.Off = true;
            foreach (var w in FindObjectsByType<EncounterPatrol>(FindObjectsSortMode.None)) if (w.group == cicEmpireGroup || w.group == cicGreysGroup) w.enabled = false;

            // the bands made over for combat
            var em = VraskMix.Parse(LaunchArgs.ValueOf("-cic3empire") ?? "4,3,2,1");
            var gm = VraskMix.Parse(LaunchArgs.ValueOf("-cic3greys") ?? "1,2,7,0");
            Note($"cic3: the Empire made over: {VraskMix.Apply(cicEmpireGroup, em)}");
            Note($"cic3: the Greys made over: {VraskMix.Apply(cicGreysGroup, gm)}");
            yield return null;
            Cic3ForCombat(cicEmpireGroup);
            Cic3ForCombat(cicGreysGroup);
            cicEmpireFleet = cicEmpireGroup.GetComponent<EmpireFleet>();
            cicGreysFleet = cicGreysGroup.GetComponent<GreysFleet>();

            // the player's own ship out of it, well back
            BattleSim.Hide(cc, true);
            var greysEnc = BattleSim.Encounter.FromGroup(cicGreysGroup);
            Vector3 c = greysEnc.Centre;
            Vector3 up = CicUp(c);
            Vector3 blueDir = CicStarDir(c);
            Place(BattleSim.Clear(c + blueDir * 22000f, blueDir), c);
            yield return Wait(1.5f);
            BattleSim.Hide(cc, true);
            // (moving the player's ship brings the world's origin to it: the battle's middle,
            // and which way is which, again where they are now, or the whole fight is laid out
            // where it was, millions of kilometres off, and drawn only as well as a float holds there)
            greysEnc = BattleSim.Encounter.FromGroup(cicGreysGroup);
            c = greysEnc.Centre;
            up = CicUp(c);
            blueDir = CicStarDir(c);
            Note($"cic3: the battle's middle {c.magnitude / 1000f:F0} km from the world's origin");

            // the three corners: blue on the star's side, the Empire and the Greys a third of the way round each way
            float R = Cic3Side / Mathf.Sqrt(3f);
            Vector3 empireDir = Quaternion.AngleAxis(120f, up) * blueDir, greysDir = Quaternion.AngleAxis(-120f, up) * blueDir;
            Transform frame = cicGreysGroup.pilots.Where(p => p != null).Select(p => p.GetComponentInParent<OrbitHome>()).FirstOrDefault(o => o != null && o.home != null)?.home;
            Cic3Move(cicGreysGroup, BattleSim.Clear(c + greysDir * R, greysDir), c, up, frame);
            Cic3Move(cicEmpireGroup, BattleSim.Clear(c + empireDir * R, empireDir), c, up, frame);
            // the arena: every ship of theirs fights within Cic3Arena of its middle (the escorts'
            // leash, on a mark there), so the fight stays one fight the frigates can reach
            var arena = new GameObject("CIC3 arena").transform;
            arena.position = c;
            if (frame != null) arena.gameObject.AddComponent<OrbitHome>().home = frame;
            foreach (var g in new[] { cicEmpireGroup, cicGreysGroup })
                foreach (var p in g.pilots.Where(p => p != null)) { p.guard = arena; p.guardLeash = Cic3Arena; }
            yield return null;

            // one encounter of both bands (the frigates fight them all), each ship whole, none back once lost
            cicEnc = BattleSim.Encounter.FromGroup(cicEmpireGroup);
            var gEnc = BattleSim.Encounter.FromGroup(cicGreysGroup);
            yield return BattleSim.ResetEncounter(cicEnc);
            yield return BattleSim.ResetEncounter(gEnc);
            cicEnc.name = "Empire and Greys";
            cicEnc.enemies.AddRange(gEnc.enemies.Where(h => h != null && !cicEnc.enemies.Contains(h)));
            foreach (var h in cicEnc.enemies) if (h != null) h.respawnHeld = true;
            cicEmpire.Clear();
            cicEmpire.AddRange(cicEmpireGroup.pilots.Where(p => p != null).Select(p => p.GetComponent<FactionShip>()).Where(f => f != null).OrderBy(f => f.RoleName).ThenBy(f => f.name));
            cicGreys.Clear();
            cicGreys.AddRange(cicGreysGroup.pilots.Where(p => p != null).Select(p => p.GetComponent<FactionShip>()).Where(f => f != null).OrderBy(f => f.RoleName).ThenBy(f => f.name));
            Note($"cic3: Empire {VraskMix.Describe(cicEmpire)}; Greys {VraskMix.Describe(cicGreys)}");
            // (each ship's armour plates laid out again round its hull, whole now: some went on
            // while the hull was out of place, and stood off it)
            foreach (var f in cicEmpire.Concat(cicGreys)) Cic3PlateCheck(f);
            foreach (var f in cicEmpire.Concat(cicGreys)) if (f != null && f.TryGetComponent(out AblativePlates ap)) ap.Relay();
            yield return null;
            foreach (var f in cicEmpire.Concat(cicGreys)) Cic3PlateCheck(f);

            // at war, and every ship seen by every other
            Rivalry.On = true;
            Rivalry.RevealAll = true;
            // both fleets refitted for this battle: their weapons hit harder (said on screen)
            Rivalry.Refit.Clear();
            if (cicEmpire.Count > 0) Rivalry.Refit[cicEmpire[0].FactionName] = Cic3RefitEmpire;
            if (cicGreys.Count > 0) Rivalry.Refit[cicGreys[0].FactionName] = Cic3RefitGreys;

            // the frigates of the Mandate of Heaven, a pair on the star's side, bows on the centre
            Vector3 start = BattleSim.Clear(c + blueDir * R, blueDir);
            Vector3 side = Vector3.Cross(up, -blueDir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            var roster = FrigateRoster.Get();
            var order = (LaunchArgs.ValueOf("-cicfleet") ?? "").Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            if (order.Length == 0) order = Cic3Fleet;
            var all = (roster != null ? roster.frigates : new FrigateRoster.Entry[0]).Where(e => e != null && e.prefab != null && e.category == FrigateRoster.Frigate).ToList();
            var entries = order.Select(n => all.FirstOrDefault(e => string.Equals(n, e.displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(n, e.id, StringComparison.OrdinalIgnoreCase)))
                               .Where(e => e != null).Take(5).ToList();
            cicBlue.Clear();
            for (int k = 0; k < entries.Count; k++)
            {
                float off = (k - (entries.Count - 1) * 0.5f) * (entries.Count > 2 ? 420f : 650f);
                Vector3 at = start + side * off;
                SimPilot p = null;
                try { p = BattleSim.SpawnFrigate(entries[k], at, Quaternion.LookRotation(c - at, up), true, k, cicEnc, entries[k].displayName); }
                catch (Exception e) { Note($"cic3: {entries[k].displayName}: {e.GetType().Name}: {e.Message}"); }
                if (p != null) cicBlue.Add(p);
            }
            if (cicBlue.Count == 0) { Note("cic3: couldn't make the frigates"); yield break; }
            // (a pair flies as a section, as in the two-frigate reel)
            bool section = cicBlue.Count == 2 || LaunchArgs.Has("-cicsection");
            foreach (var p in cicBlue) { p.fleet = cicBlue; p.encounter = cicEnc; p.section = section; }
            cam.maxDistance = Mathf.Max(cam.maxDistance, 12000f);
            if (cicMark == null) cicMark = new GameObject("CIC camera mark").transform;
            yield return null;

            // they have found each other: both bands out of hiding at once
            cicEmpireGroup.Spring();
            cicGreysGroup.Spring();

            Transform frameOf = cicBlue[0] != null && cicBlue[0].TryGetComponent(out OrbitHome oh) ? oh.home : frame;
            // the HUD at its smallest (the pause menu's last step down): the captions, and the CIC
            // inset over the action, take as little of the picture as they can
            Hud.UserScale = HudSettings.Smallest;
            tac = gameObject.AddComponent<TacticalDisplay>();
            tac.insetScale = HudSettings.Smallest;
            tac.Logged += line => Note("cic  " + line);
            tac.Begin(frameOf, up, -blueDir, cicBlue, cicEmpire, null, cicEmpireGroup, "GOLIATH", cicGreys, "EMPIRE", "GREYS");
            tac.PhaseText = () => $"EMPIRE {(cicEmpireFleet != null ? cicEmpireFleet.Current.ToString() : "-")}   GREYS {(cicGreysFleet != null ? cicGreysFleet.Current.ToString() : "-")}";
            cicLine = string.Join(", ", cicBlue.Select(p => p.ship != null ? p.ship.shipName : p.name));
            Note($"cic3: blue {cicLine}{(section ? ", flown as a section" : "")}; the triangle {Cic3Side / 1000f:F1} km a side over Goliath; the Empire refitted x{Cic3RefitEmpire:0.#}, the Greys x{Cic3RefitGreys:0.#}; positions on {(frameOf != null ? frameOf.name : "the world")}");
            done(true);
        }

        float cic3NextDump;

        /// <summary>Where a ship's armour plates sit against its hull (the reel's log: a check).</summary>
        void Cic3PlateCheck(FactionShip f)
        {
            var plates = f != null ? f.GetComponent<AblativePlates>() : null;
            if (plates == null) return;
            var toLocal = f.transform.worldToLocalMatrix;
            float far = 0f, hull = 0f;
            int n = 0;
            foreach (var mf in f.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.TryGetComponent(out MeshRenderer mr) || !mr.enabled || mf.GetComponentInParent<Shield>(true) != null) continue;
                Vector3 c = toLocal.MultiplyPoint3x4(mf.transform.TransformPoint(mf.sharedMesh.bounds.center));
                if (mf.name.StartsWith(AblativePlates.PlateName)) { n++; far = Mathf.Max(far, new Vector2(c.x, c.y).magnitude); continue; }
                // (in the ship's own units, as the plates are)
                Vector3 ls = f.transform.lossyScale;
                Vector3 e = Vector3.Scale(mf.sharedMesh.bounds.extents, new Vector3(mf.transform.lossyScale.x / ls.x, mf.transform.lossyScale.y / ls.y, mf.transform.lossyScale.z / ls.z));
                hull = Mathf.Max(hull, new Vector2(Mathf.Abs(c.x) + e.x, Mathf.Abs(c.y) + e.y).magnitude);
            }
            Note($"cic3 plates {f.name}: {n} on of {plates.Count}, the farthest {far:F0} m off the keel, the hull {hull:F0} m; laid out round {plates.HullExtents.x:F0}x{plates.HullExtents.y:F0}x{plates.HullExtents.z:F0}, the widest {plates.Widest}");
        }

        int cic3PovTurn;
        TacticalDisplay.Track cic3PovFoe, cic3PovShip;

        /// <summary>The camera with one of the Mandate of Heaven's ships: behind her, looking where
        /// she fires; on her target as her fire goes in, seen from her side; or on her as she is
        /// hit, from her attacker's side. Each in turn, the one under fire first every other cut.
        /// False with none of them left (then the whole battle's hottest exchange).</summary>
        bool Cic3PovFrame(int variant)
        {
            var blue = tac.Tracks.Where(k => k.side == 0 && !k.dead && k.t != null).ToList();
            if (blue.Count == 0) return false;
            bool Live(TacticalDisplay.Track k) => k != null && !k.dead && k.t != null;
            var hit = blue.Where(k => Live(k.lastBy) && k.lastBy.side != 0 && Time.time - k.lastByAt < 4f).OrderByDescending(k => k.lastByAt).FirstOrDefault();
            var m = hit != null && variant % 2 == 1 ? hit : blue[cic3PovTurn++ % blue.Count];
            var tgt = m.pilot != null && m.pilot.Target != null ? tac.Of(m.pilot.Target) : null;
            if (!Live(tgt)) tgt = null;
            var by = Live(m.lastBy) && m.lastBy.side != 0 && Time.time - m.lastByAt < 6f ? m.lastBy : null;
            var foe = tgt ?? by ?? tac.Tracks.Where(k => Live(k) && k.side != 0).OrderBy(k => (k.t.position - m.t.position).sqrMagnitude).FirstOrDefault();
            if (foe == null) return false;
            // (under fire: on her, from where it comes; else behind her, or on what she fires at)
            // ("her target" only when she has one)
            int kind = m == hit && by != null && variant % 2 == 1 ? 2 : (variant / 2) % 2 == 1 && tgt != null ? 1 : 0;
            var from = kind == 2 ? by : foe;
            float sign = variant % 2 == 0 ? 1f : -1f;
            Transform on;
            float yawOff, dist, pitch;
            switch (kind)
            {
                case 0: on = m.t; yawOff = 10f * sign; dist = SizeOf(m.t) * 2.3f; pitch = 6f; cicSubject = m; break;
                case 1: on = foe.t; yawOff = 22f * sign; dist = Mathf.Max(220f, SizeOf(foe.t) * 3f); pitch = 8f; cicSubject = foe; break;
                default: on = m.t; yawOff = 35f * sign; dist = SizeOf(m.t) * 2.6f; pitch = 9f; cicSubject = m; break;
            }
            cic3PovFoe = from;
            cic3PovShip = m;
            Transform mt = m.t, ft = from.t;
            Vector3 P1() => kind == 2 ? ft.position : mt.position;
            Vector3 P2() => kind == 2 ? mt.position : ft.position;
            Shot(1250 + variant, on, Vector3.zero, dist, Bearing(P1(), P2()) + yawOff, pitch, 0f);
            Note($"cic camera ({(kind == 0 ? "behind" : kind == 1 ? "her target" : "she is hit")}): {m.Label} and {from.Label}{(kind == 2 ? " firing on her" : "")}, {Vector3.Distance(m.t.position, from.t.position) / 1000f:F1} km");
            perFrame = () =>
            {
                if (mt == null || ft == null || cam == null) return;
                cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(P1(), P2()) + yawOff, Time.deltaTime * 1.5f);
            };
            return true;
        }

        /// <summary>Every 20 s of the battle: each ship's side, what it is doing, on what, how far
        /// off, its hull and shield (the reel's log: a voice-over's facts, and a check on the fight).</summary>
        void Cic3Dump()
        {
            if (!Cic3 || tac == null || !tac.Running || Time.time < cic3NextDump) return;
            cic3NextDump = Time.time + 20f;
            Vector3 cen = Vector3.zero;
            var alive = tac.Tracks.Where(k => !k.dead && k.t != null).ToList();
            if (alive.Count > 0) cen = alive.Aggregate(Vector3.zero, (a, k) => a + k.t.position) / alive.Count;
            Note($"cic3 dump T+{tac.ClockText}: {string.Join(", ", Enumerable.Range(0, 3).Select(x => $"{tac.SideNames[x]} {tac.SideLeft(x)}/{tac.SideCount(x)} dealt {tac.SideDealt(x):F0}"))}; " +
                 $"empire {(cicEmpireFleet != null ? cicEmpireFleet.Current + " (" + cicEmpireFleet.Why + ")" : "-")}, greys {(cicGreysFleet != null ? cicGreysFleet.Current + " (" + cicGreysFleet.Why + ")" : "-")}, " +
                 $"band active {(cicEmpireGroup != null && cicEmpireGroup.Active)}/{(cicGreysGroup != null && cicGreysGroup.Active)}");
            foreach (var k in alive)
            {
                var st = k.t.GetComponent<SensorTactic>();
                var cp = k.t.GetComponent<CombatPilot>();
                Transform tgt = k.pilot != null ? k.pilot.Target : cp != null ? cp.target : null;
                string doing = k.pilot != null ? $"{k.pilot.Doing} [{k.pilot.Why}]" : $"{cp?.Current} {st?.Doing}";
                int shots = k.t.GetComponentsInChildren<CarrierTurret>(true).Sum(x => x.ShotsFired);
                Note($"cic3   {k.id} {k.name}: {doing} on {(tgt != null ? tgt.name : "-")}{(tgt != null ? $" {Vector3.Distance(k.t.position, tgt.position) / 1000f:F1} km" : "")}, " +
                     $"{Vector3.Distance(k.t.position, cen) / 1000f:F1} km from the centre, hull {k.hull:P0} shield {k.shieldF:P0}, {shots} shots, dealt {k.dealt:F0}");
            }
        }

        /// <summary>The three-way battle over? One side left, or the time limit.</summary>
        bool Cic3Over()
        {
            if (cicDone) return true;
            if (tac == null) { cicOutcome = "NO BATTLE"; return cicDone = true; }
            var standing = Enumerable.Range(0, 3).Where(x => tac.SideLeft(x) > 0).ToList();
            if (standing.Count <= 1)
            {
                if (standing.Count == 0) { cicWinner = ""; cicOutcome = "ALL THREE SIDES DESTROYED"; }
                else
                {
                    int w = standing[0];
                    cicWinner = tac.SideNames[w];
                    string who = w == 0 ? "THE MANDATE OF HEAVEN" : w == 1 ? "THE EMPIRE" : "THE GREYS";
                    cicOutcome = $"{who} HOLD{(w == 0 ? "S" : "")} THE FIELD: {tac.SideLeft(w)} OF {tac.SideCount(w)} LEFT";
                }
                return cicDone = true;
            }
            // (a lull: no ship lost for four minutes, after the first five: called, the strongest side ahead)
            int dead = tac.Tracks.Count(k => k.dead);
            if (dead != cic3Dead) { cic3Dead = dead; cic3LastLoss = tac.Clock; }
            if (tac.Clock > 300f && tac.Clock - cic3LastLoss > Cic3Lull) { Cic3TimeUp("THE FIGHTING DIES DOWN"); return cicDone = true; }
            if (tac.Clock > Cic3Limit) { Cic3TimeUp(); return cicDone = true; }
            return false;
        }

        int cic3Dead;
        float cic3LastLoss;

        void Cic3TimeUp(string why = "TIME LIMIT")
        {
            float StrengthOf(int side)
            {
                var all = tac.Tracks.Where(k => k.side == side).ToList();
                return all.Count == 0 ? 0f : all.Sum(k => k.dead ? 0f : k.hull) / all.Count;
            }
            var st = Enumerable.Range(0, 3).Select(x => (x, s: StrengthOf(x))).OrderByDescending(x => x.s).ToList();
            cicWinner = st[0].s > st[1].s + 0.05f ? tac.SideNames[st[0].x] : "";
            cicOutcome = $"{why} T+{Mmss(tac.Clock)}: " + string.Join(", ", Enumerable.Range(0, 3).Select(x => $"{tac.SideNames[x]} {tac.SideLeft(x)}/{tac.SideCount(x)} AT {StrengthOf(x):P0}"));
        }

        /// <summary>The opening captions' words.</summary>
        string Cic3Title => $"The Mandate of Heaven, the Empire and the Greys over Goliath, in the Nine Hells: {CicWord(cicBlue.Count).ToLowerInvariant()} frigates, ten and ten, all at war";
        string Cic3Roster => $"Blue: {cicLine}, {(cicBlue.Count > 0 && cicBlue[0].section ? "a section" : cicBlue.Count > 2 ? "line abreast" : cicBlue.Count == 2 ? "a pair" : "alone")}. Red, the Empire: {VraskMix.Describe(cicEmpire)}. Magenta, the Greys: {VraskMix.Describe(cicGreys)}{Cic3RefitText}";
        static string Cic3RefitText => Cic3RefitEmpire == 1f && Cic3RefitGreys == 1f ? "" : $". Refitted for battle: the Empire's weapons x{Cic3RefitEmpire:0.#}, the Greys' x{Cic3RefitGreys:0.#}";
    }
}
