using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The battle simulator (-battlesim &lt;dir&gt;): fleets of the five frigate classes, as built
    /// (default fittings, full crews), against each of the star system's hostile encounters, with
    /// their bow beams (the main gun) and without. Every fleet of 1 to 6 ships drawn from the five
    /// classes (the order doesn't matter, a class may repeat: 461 fleets) meets every encounter
    /// twice. Each battle is fought where the encounter waits, then the encounter is put back as
    /// it was for the next. Meant to run headless (-batchmode -nographics): it runs as fast as the
    /// machine can, a fixed 1/30 s of game time a frame.
    ///
    /// Results go to &lt;dir&gt;/battles.csv, a line a battle, written as each ends, so a run can be
    /// stopped and started again (-simfrom N -simcount M: a batch of cases; ones already in the
    /// file are skipped). -simlimit S: a battle's longest, in game seconds (default 600).
    /// -simlist: write the cases and encounters and quit.
    ///
    /// Or -simduo: pairs of frigates (two different classes, with their main guns) against one
    /// band (-simenc, the Sheol picket by default), made over to a mix (-simmix 2,3,2,3: Maws,
    /// Snares, Fangs, Skinks; for the Greys' or the Empire's band their own roles' counts: VraskMix), each pair flown both ways (the simulator's own pilot, and
    /// as a section: SimPilot.section), -simreps times each (default 2), to duo.csv.
    /// The player's own ship is kept out of it: hidden near the fight, no target for anyone.
    /// </summary>
    public class BattleSim : MonoBehaviour
    {
        public static bool Running { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchArgs.Has("-battlesim")) return;
            // (told another star system: started once that one is open)
            if (StarSystems.Leaving) { StarSystems.WhenThere(Boot); return; }
            var s = new GameObject("BattleSim").AddComponent<BattleSim>();
            s.dir = LaunchArgs.FolderAfter("-battlesim", Application.persistentDataPath);
        }

        string dir, csv, log;
        CarrierDemoInput demo;
        CarrierController ghost;
        float limit = 600f;

        /// <summary>One of the hostile encounters: the ships or guns it is made of.</summary>
        public class Encounter
        {
            public string name, kind;
            public readonly List<Health> enemies = new List<Health>();
            public EncounterGroup group;
            public DefenseGrid grid;
            public Vector3 Centre
            {
                get
                {
                    var homes = enemies.Where(h => h != null).Select(Home).ToList();
                    return homes.Count == 0 ? Vector3.zero : homes.Aggregate(Vector3.zero, (a, b) => a + b) / homes.Count;
                }
            }
            static Vector3 Home(Health h) => h.TryGetComponent(out CombatPilot p) ? p.Home : h.transform.position;

            /// <summary>A band's ships as an encounter (null if it has none).</summary>
            public static Encounter FromGroup(EncounterGroup g)
            {
                if (g == null || g.pilots == null) return null;
                var e = new Encounter { name = g.groupName, kind = "band", group = g };
                foreach (var p in g.pilots) if (p != null && p.TryGetComponent(out Health h)) e.enemies.Add(h);
                return e.enemies.Count > 0 ? e : null;
            }
        }

        public readonly List<Encounter> Encounters = new List<Encounter>();
        public readonly List<string> Classes = new List<string>();
        readonly Dictionary<string, FrigateRoster.Entry> entries = new Dictionary<string, FrigateRoster.Entry>();

        void Log(string s)
        {
            Debug.Log("[BattleSim] " + s);
            log += s + "\n";
            try { File.AppendAllText(Path.Combine(dir, "battlesim.log"), s + "\n"); } catch { }
        }

        IEnumerator Start()
        {
            Running = true;
            Directory.CreateDirectory(dir);
            csv = Path.Combine(dir, "battles.csv");
            limit = LaunchArgs.FloatAfter("-simlimit", 600f);
            // as fast as the machine can: a fixed step of game time a frame, however long it takes
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Time.captureDeltaTime = 1f / 30f;
            yield return null;
            yield return null;
            demo = FindAnyObjectByType<CarrierDemoInput>();
            ghost = demo != null ? demo.carrier : null;
            if (ghost == null) { Log("no player ship: nothing to fly"); Quit(); yield break; }
            // nothing else comes back or happens on its own while the battles run
            if (SpawnDirector.Instance != null) SpawnDirector.Instance.enabled = false;
            foreach (var b in FindObjectsByType<VraskBombardment>(FindObjectsSortMode.None)) b.enabled = false;
            Ghost(true);

            var roster = FrigateRoster.Get();
            foreach (var e in roster != null ? roster.frigates : new FrigateRoster.Entry[0])
                if (e != null && e.prefab != null && e.category == FrigateRoster.Frigate) { Classes.Add(e.id); entries[e.id] = e; }
            FindEncounters();
            bool duo = LaunchArgs.Has("-simduo");
            string mix = "";
            if (duo)
            {
                string want = LaunchArgs.ValueOf("-simenc") ?? "Sheol picket";
                Encounters.RemoveAll(e => !string.Equals(e.name, want, System.StringComparison.OrdinalIgnoreCase));
                var counts = VraskMix.Parse(LaunchArgs.ValueOf("-simmix"));
                if (Encounters.Count > 0 && counts != null && Encounters[0].group != null)
                {
                    mix = VraskMix.Apply(Encounters[0].group, counts);
                    yield return null;
                    var remade = Encounter.FromGroup(Encounters[0].group);
                    if (remade != null) Encounters[0] = remade; else Encounters.Clear();
                }
                csv = Path.Combine(dir, "duo.csv");
            }
            var cases = duo ? DuoCases() : Cases();
            Log($"{Classes.Count} classes ({string.Join(", ", Classes.Select(c => entries[c].displayName))}), " +
                $"{Encounters.Count} encounters, {Fleets().Count} fleets: {cases.Count} battles");
            foreach (var e in Encounters) Log($"  encounter {e.name} ({e.kind}): {e.enemies.Count} - {string.Join(", ", e.enemies.Select(h => h.name))}");
            if (duo) Log($"  mix: {mix}");
            if (LaunchArgs.Has("-simlist")) { WriteCases(cases); Quit(); yield break; }

            var done = Done(duo ? (System.Func<int, string>)(i => DuoKey(cases[i])) : null);
            int from = (int)LaunchArgs.FloatAfter("-simfrom", 0f), count = (int)LaunchArgs.FloatAfter("-simcount", cases.Count);
            if (!File.Exists(csv))
                File.WriteAllText(csv, "case,encounter,kind,fleet,size,main_gun,outcome,seconds,enemies_killed,enemies_total,frigates_lost,frigates_captured,hull_left,shield_left,withdrew,hits_taken" + (duo ? ",tactic,mix" : "") + "\n");
            float real0 = Time.realtimeSinceStartup;
            int fought = 0;
            for (int i = from; i < Mathf.Min(cases.Count, from + count); i++)
            {
                if (done.Contains(i)) continue;
                var c = cases[i];
                yield return Fight(i, c.enc, c.fleet, c.gun, c.section, duo ? "," + (c.section ? "section" : "default") + "," + Csv(MixOf(c.enc)) : "");
                fought++;
                if (fought % 10 == 0)
                    Log($"{fought} fought in {(Time.realtimeSinceStartup - real0) / 60f:F1} min ({(Time.realtimeSinceStartup - real0) / fought:F1} s each)");
            }
            Log($"done: {fought} battles in {(Time.realtimeSinceStartup - real0) / 60f:F1} min");
            Quit();
        }

        void Quit()
        {
            Running = false;
            Application.Quit();
        }

        // ---------------------------------------------------------------- the cases

        public struct Case { public Encounter enc; public string[] fleet; public bool gun, section; }

        /// <summary>Every fleet: 1 to 6 ships of the five classes, the order not counting (a class may repeat).</summary>
        public List<string[]> Fleets()
        {
            var all = new List<string[]>();
            void Grow(List<string> f, int start, int size)
            {
                if (f.Count == size) { all.Add(f.ToArray()); return; }
                for (int k = start; k < Classes.Count; k++) { f.Add(Classes[k]); Grow(f, k, size); f.RemoveAt(f.Count - 1); }
            }
            for (int size = 1; size <= 6; size++) Grow(new List<string>(), 0, size);
            return all;
        }

        /// <summary>The battles, in order: each fleet against each encounter, with its main guns and without.</summary>
        public List<Case> Cases()
        {
            var list = new List<Case>();
            foreach (var f in Fleets())
                foreach (var e in Encounters)
                    foreach (bool gun in new[] { true, false })
                        list.Add(new Case { enc = e, fleet = f, gun = gun });
            return list;
        }

        /// <summary>The -simduo battles: every pair of two different classes, flown both ways, -simreps times.</summary>
        public List<Case> DuoCases()
        {
            var list = new List<Case>();
            int reps = Mathf.Max(1, (int)LaunchArgs.FloatAfter("-simreps", 2f));
            string only = LaunchArgs.ValueOf("-simpairs");
            for (int r = 0; r < reps; r++)
                for (int a = 0; a < Classes.Count; a++)
                    for (int b = a + 1; b < Classes.Count; b++)
                    {
                        var f = new[] { Classes[a], Classes[b] };
                        if (!string.IsNullOrEmpty(only) && !only.Split(';').Any(x => PairNamed(x, f))) continue;
                        foreach (var e in Encounters)
                            foreach (bool sec in new[] { false, true })
                                list.Add(new Case { enc = e, fleet = f, gun = true, section = sec });
                    }
            return list;
        }

        static string MixOf(Encounter e) => VraskMix.Describe(e.enemies.Where(h => h != null).Select(h => h.GetComponent<FactionShip>()).Where(v => v != null)).Replace(", ", " ");

        /// <summary>A -simpairs name ("Talon+Vanguard" or "Vanguard+Talon") for this pair.</summary>
        bool PairNamed(string name, string[] f)
        {
            var want = name.Split('+').Select(x => x.Trim().ToLowerInvariant()).OrderBy(x => x);
            var have = f.Select(id => entries[id].displayName.ToLowerInvariant()).OrderBy(x => x);
            return want.SequenceEqual(have);
        }

        string FleetName(string[] f) => string.Join("+", f.Select(id => entries[id].displayName));

        void WriteCases(List<Case> cases)
        {
            var sb = new StringBuilder("case,encounter,fleet,main_gun\n");
            for (int i = 0; i < cases.Count; i++) sb.Append($"{i},{cases[i].enc.name},{FleetName(cases[i].fleet)},{cases[i].gun}\n");
            File.WriteAllText(Path.Combine(dir, "cases.csv"), sb.ToString());
        }

        /// <summary>The cases already in the file. `key`: a -simduo case's pair, tactic and mix, which
        /// its line must match too (its number alone changes with -simpairs, -simmix, -simenc).</summary>
        HashSet<int> Done(System.Func<int, string> key = null)
        {
            var done = new HashSet<int>();
            if (!File.Exists(csv)) return done;
            foreach (var line in File.ReadAllLines(csv).Skip(1))
            {
                if (!int.TryParse(line.Split(',')[0], out int n)) continue;
                if (key != null)
                {
                    // (the line's fleet, after the encounter and kind, and its last two: tactic, mix)
                    var f = line.Split(',');
                    if (f.Length < 4 || !string.Equals(key(n), $"{f[3]},{f[f.Length - 2]},{f[f.Length - 1]}")) continue;
                }
                done.Add(n);
            }
            return done;
        }

        string DuoKey(Case c) => $"{FleetName(c.fleet)},{(c.section ? "section" : "default")},{MixOf(c.enc)}";

        /// <summary>The hostile encounters out in space: the bands lying in wait, the defended
        /// bases and batteries (not one on a planet's surface), the strike satellites.</summary>
        void FindEncounters()
        {
            foreach (var g in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None).OrderBy(g => g.groupName))
            {
                var e = Encounter.FromGroup(g);
                if (e != null) Encounters.Add(e);
            }
            foreach (var d in DefenseGrid.All.Where(d => d != null).OrderBy(d => d.gridName))
            {
                if (d.weapons == null || Encounters.Any(x => x.name == d.gridName)) continue;
                var e = new Encounter { name = d.gridName, kind = "defences", grid = d };
                foreach (var w in d.weapons) if (w != null && w.TryGetComponent(out Health h)) e.enemies.Add(h);
                // (on a planet's surface: out of a frigate fleet's reach from space)
                if (e.enemies.Count == 0 || OnSurface(e.Centre)) continue;
                Encounters.Add(e);
            }
            foreach (var s in FindObjectsByType<StrikeSatellite>(FindObjectsSortMode.None).OrderBy(s => s.name))
            {
                if (!s.TryGetComponent(out Health h)) continue;
                var e = new Encounter { name = s.GetComponent<TargetInfo>()?.displayName ?? s.name, kind = "satellite" };
                e.enemies.Add(h);
                Encounters.Add(e);
            }
            // (each enemy in one encounter only)
            var seen = new HashSet<Health>();
            foreach (var e in Encounters) e.enemies.RemoveAll(h => !seen.Add(h));
            Encounters.RemoveAll(e => e.enemies.Count == 0);
        }

        static bool OnSurface(Vector3 p)
        {
            var body = PlanetFlight.NearestBody(p, out float alt);
            return body != null && alt < 3000f;
        }

        // ---------------------------------------------------------------- a battle

        readonly List<SimPilot> fleet = new List<SimPilot>();

        IEnumerator Fight(int index, Encounter enc, string[] ships, bool gun, bool section = false, string extra = "")
        {
            yield return ResetEncounter(enc);
            Vector3 c = enc.Centre;
            // the fleet 9 km off, on the side towards the star, line abreast, bows on the encounter
            var star = WarpPoint.All.FirstOrDefault(w => w != null && w.kind == WarpPoint.Kind.Star);
            Vector3 dir = star != null && (star.Position - c).sqrMagnitude > 1f ? (star.Position - c).normalized : Vector3.forward;
            Vector3 start = Clear(c + dir * 9000f, dir);
            Vector3 side = Vector3.Cross(Vector3.up, -dir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            // the hidden player ship well back, so the fields and the origin are where the fight is
            Vector3 ghostAt = Clear(start + dir * 6000f, dir);
            PlaceGhost(ghostAt, c);
            if (LaunchArgs.Has("-simverbose")) Log($"  ghost put {Vector3.Distance(ghost.transform.position, c) / 1000f:F1} km from the centre ({c}); the player at {ghost.transform.position}");
            yield return null;
            if (LaunchArgs.Has("-simverbose")) Log($"  a frame on: ghost {Vector3.Distance(ghost.transform.position, enc.Centre) / 1000f:F1} km from the centre ({enc.Centre}); the player at {ghost.transform.position}; landing {PlanetLanding.Instance?.Current}");
            c = enc.Centre;
            start = Clear(c + dir * 9000f, dir);
            fleet.Clear();
            for (int k = 0; k < ships.Length; k++)
            {
                float off = (k - (ships.Length - 1) * 0.5f) * 500f;
                var p = Spawn(ships[k], start + side * off, Quaternion.LookRotation(c - start), gun, k, enc);
                if (p != null) fleet.Add(p);
            }
            if (fleet.Count == 0) { Log($"case {index}: couldn't make the fleet"); yield break; }
            foreach (var p in fleet) p.fleet = fleet;
            foreach (var p in fleet) p.encounter = enc;
            foreach (var p in fleet) p.section = section;
            // (-simduo: a band's lost ship stays lost till the next battle)
            if (LaunchArgs.Has("-simduo")) foreach (var h in enc.enemies) if (h != null) h.respawnHeld = true;

            float t0 = Time.time;
            string outcome = "timeout";
            bool withdrew = false;
            float nextNote = Time.time + 20f;
            while (Time.time - t0 < limit)
            {
                yield return null;
                int enemiesLeft = enc.enemies.Count(h => h != null && !h.Dead && !h.Captured);
                int shipsLeft = fleet.Count(p => p != null && p.Alive);
                if (enemiesLeft == 0) { outcome = "win"; break; }
                if (shipsLeft == 0) { outcome = "loss"; break; }
                // (a Vrask band that has given up the fight and gone back into hiding: won)
                if (enc.group != null && enc.group.holdOff) { withdrew = true; outcome = "win"; break; }
                if (LaunchArgs.Has("-simverbose") && Time.time >= nextNote && fleet.Count > 0 && fleet[0] != null)
                {
                    nextNote = Time.time + 20f;
                    var f0 = fleet[0];
                    Log($"  {Time.time - t0:F0} s: {f0.name} {Vector3.Distance(f0.transform.position, enc.Centre) / 1000f:F1} km from the centre, speed {f0.ship.Speed:F0}, target {(f0.Target != null ? f0.Target.name : "none")}, ghost {Vector3.Distance(ghost.transform.position, enc.Centre) / 1000f:F1} km");
                    if (enc.group != null)
                        Log($"    band: active {enc.group.Active}, hold off {enc.group.holdOff}, sprung {enc.group.TimesSprung}" +
                            $"{(enc.group.TryGetComponent(out GreysFleet gfl) ? $", Greys {gfl.Current} ({gfl.Why})" : "")}{(enc.group.TryGetComponent(out EmpireFleet efl) ? $", Empire {efl.Current} ({efl.Why})" : "")}");
                    foreach (var h in enc.enemies)
                    {
                        if (h == null) continue;
                        var cp = h.GetComponent<CombatPilot>();
                        var st = h.GetComponent<SensorTactic>();
                        Log($"    {h.name}: {(h.Dead ? "dead" : $"hull {h.Current / Mathf.Max(1f, h.maxHealth):P0}")}, {cp?.Current}{(cp != null && !cp.AtHome ? " (away)" : "")}, target {(cp != null && cp.target != null ? cp.target.name : "none")}, " +
                            $"{(st != null ? st.Doing : "")}, {Vector3.Distance(h.transform.position, f0.transform.position) / 1000f:F1} km from {f0.name}, unseen {Sensors.Unseen(h.transform)}, " +
                            $"shots {h.GetComponentsInChildren<CarrierTurret>(true).Sum(t => t.ShotsFired)} turret / {(h.TryGetComponent(out EnemyWeapons ew) ? ew.ShotsFired : 0)} guns");
                    }
                }
            }
            float secs = Time.time - t0;
            int killed = enc.enemies.Count(h => h != null && (h.Dead || h.Captured));
            int lost = fleet.Count(p => p == null || !p.Alive);
            int captured = fleet.Count(p => p != null && p.Taken);
            var alive = fleet.Where(p => p != null && p.Alive).ToList();
            float hull = alive.Count > 0 ? alive.Average(p => p.HullFraction) : 0f;
            float shield = alive.Count > 0 ? alive.Average(p => p.ShieldFraction) : 0f;
            int hits = fleet.Sum(p => p != null ? p.HitsTaken : 0);
            string line = string.Join(",", index, Csv(enc.name), enc.kind, Csv(FleetName(ships)), ships.Length, gun ? 1 : 0, outcome,
                                      secs.ToString("F0", CultureInfo.InvariantCulture), killed, enc.enemies.Count, lost, captured,
                                      hull.ToString("F2", CultureInfo.InvariantCulture), shield.ToString("F2", CultureInfo.InvariantCulture), withdrew ? 1 : 0, hits) + extra;
            File.AppendAllText(csv, line + "\n");
            Log($"case {index}: {FleetName(ships)} {(gun ? "with" : "without")} main guns{(section ? ", as a section," : "")} vs {enc.name}: {outcome} in {secs:F0} s, " +
                $"{killed}/{enc.enemies.Count} destroyed, {lost} lost ({captured} captured), hull {hull:P0} shield {shield:P0}, {hits} hits taken");
            foreach (var p in fleet) if (p != null) Destroy(p.gameObject);
            fleet.Clear();
            yield return null;
        }

        static string Csv(string s) => s.Contains(",") ? $"\"{s}\"" : s;

        /// <summary>A point clear of rock and wreck, out along `dir` from `p` if it has to be.</summary>
        public static Vector3 Clear(Vector3 p, Vector3 dir)
        {
            for (int i = 0; i < 60 && Cover.Mask != 0 && Physics.CheckSphere(p, 600f, Cover.Mask, QueryTriggerInteraction.Ignore); i++)
                p += dir * 400f;
            return p;
        }

        SimPilot Spawn(string id, Vector3 at, Quaternion facing, bool gun, int slot, Encounter enc) =>
            entries.TryGetValue(id, out var entry) ? SpawnFrigate(entry, at, facing, gun, slot, enc, $"{entry.displayName} (sim {slot + 1})") : null;

        /// <summary>A frigate of the roster flown by a SimPilot against an encounter: carried along
        /// with the encounter's place, no respawn, a full crew, and its bow beam only if `gun`
        /// (the battle simulator's, and the CIC reel's).</summary>
        public static SimPilot SpawnFrigate(FrigateRoster.Entry entry, Vector3 at, Quaternion facing, bool gun, int slot, Encounter enc, string name)
        {
            if (entry == null || entry.prefab == null) return null;
            var go = Instantiate(entry.prefab, at, facing);
            go.name = name;
            string id = entry.id;
            var cc = go.GetComponent<CarrierController>();
            if (cc == null) { Destroy(go); return null; }
            cc.shipName = entry.displayName;
            // carried along with the encounter's own place (a moving station, comet or moon), as
            // its ships and guns are: else the fight drifts apart as the place moves on
            var src = enc.enemies.Where(e => e != null).Select(e => e.GetComponentInParent<OrbitHome>()).FirstOrDefault(o => o != null && o.home != null);
            var first = enc.enemies.FirstOrDefault(e => e != null);
            Transform home = src != null ? src.home : first != null ? first.transform.root : null;
            if (home != null) go.AddComponent<OrbitHome>().home = home;
            var h = go.GetComponent<Health>();
            if (h != null) h.respawnDelay = 0f;
            var crew = go.GetComponent<Crew>();
            if (crew != null) crew.SetAboard(FleetDirector.Complement(id));
            // without a main gun: the bow beam taken out of her
            if (!gun) foreach (var b in go.GetComponentsInChildren<MainBeam>(true)) Destroy(b);
            var p = go.AddComponent<SimPilot>();
            p.ship = cc;
            p.slot = slot;
            p.useBeam = gun;
            return p;
        }

        /// <summary>The encounter as it was: every ship and gun whole and back at home, cold; nothing
        /// left in flight, no wrecks.</summary>
        public static IEnumerator ResetEncounter(Encounter enc)
        {
            foreach (var t in new[] { typeof(Projectile), typeof(Missile), typeof(ArtilleryShell), typeof(NukeMissile), typeof(BoardingPod) })
                foreach (var o in FindObjectsByType(t, FindObjectsSortMode.None))
                    if (o is Component comp && comp != null) Destroy(comp.gameObject);
            foreach (var w in Wreck.All.ToList()) if (w != null) Destroy(w.gameObject);
            foreach (var h in enc.enemies)
            {
                if (h == null) continue;
                if (h.Dead) h.RespawnNow(); else h.Restore();
                foreach (var s in h.GetComponentsInChildren<Shield>(true)) s.NetSet(1f, true);
                if (h.TryGetComponent(out CombatPilot p)) p.ResetHome();
            }
            if (enc.group != null)
            {
                enc.group.ResetBand();
                // (its fleet's own state too: not still vanishing or falling back from the last battle)
                if (enc.group.TryGetComponent(out GreysFleet gf)) gf.ResetBand();
                if (enc.group.TryGetComponent(out EmpireFleet ef)) ef.ResetBand();
            }
            // a few moments for them to settle (the band's own state goes back to lying in wait)
            for (int i = 0; i < 30; i++) yield return null;
        }

        void Ghost(bool hide) => Hide(ghost, hide);

        /// <summary>The player's own ship out of the fight (or back in it): not seen, not solid,
        /// its guns and beam quiet, unkillable, and no target for any hostile.</summary>
        public static void Hide(CarrierController ghost, bool hide)
        {
            if (ghost == null) return;
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) col.enabled = !hide;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true)) r.enabled = !hide;
            foreach (var b in ghost.GetComponentsInChildren<MainBeam>(true)) b.enabled = !hide;
            foreach (var t in ghost.mainTurrets) if (t != null) t.autoFire = false;
            foreach (var pd in ghost.GetComponentsInChildren<CiwsDirector>(true)) pd.enabled = !hide;
            if (ghost.TryGetComponent(out Health h)) h.cannotDie = hide;
            // no hostile goes for it
            var me = ghost.transform;
            PlayerShips.Available = hide ? (t => t != me) : null;
        }

        void PlaceGhost(Vector3 at, Vector3 facing)
        {
            ghost.transform.position = at;
            ghost.transform.rotation = Quaternion.LookRotation(facing - at);
            ghost.SetMotion(0f);
            ghost.throttle = 0f;
            ghost.SetSpawn(at, ghost.transform.rotation);
        }
    }

    /// <summary>
    /// A frigate of the battle simulator, fought as a player would: the fleet's guns together on one
    /// enemy (the nearest the fleet can see), broadside on at gun range for the turrets, and with a
    /// main gun, bow on to charge and fire the beam whenever it is in its reach.
    ///
    /// Or, flown as a section (`section`: a pair, the first the lead), against the Vrask way of
    /// fighting (disable, hold, board):
    /// - Cover each other first: a section-mate in a tractor's beam or with a Fang clamped to her
    ///   and the other goes for the Snare holding her, or the Fang.
    /// - Then the lead's choice, both on it: the Snares first (no Snares, no capture; with their
    ///   Maws gone too the Vrask break off), then a Fang on its run, then the Maws, a Skink last;
    ///   nearer and more damaged ones first. The beam isn't spent on a Skink.
    /// - Stay together: both wheel round the target the same way, and the wing more than 2 km
    ///   from the lead closes up (their CIWS cover each other, and a lone ship is a Snare pack's).
    /// - Press the attack: out of gun range, straight in at full speed (a Maw falls back as a ship
    ///   comes near; a slow ship half-throttle broadside-on never catches it).
    /// - Keep the shield up: a Fang can't ram, nor a Snare drag, a ship with its shield up. Under
    ///   a fifth, she turns away towards her mate till it is back over half, for 20 s at most
    ///   (not with her beam charging or firing, nor alone, nor with her mate in no better state).
    /// </summary>
    public class SimPilot : MonoBehaviour
    {
        public CarrierController ship;
        public int slot;
        public bool useBeam = true;
        /// <summary>Flown as a section (see above).</summary>
        public bool section;
        public List<SimPilot> fleet;
        public BattleSim.Encounter encounter;

        Health health;
        Shield shield;
        MainBeam beam;
        Transform target;
        float nextLook;
        bool recharging;
        float rechargeUntil, rechargeAgain;
        /// <summary>Why she has her target (a section's: "COVER: SNARE", "SNARE FIRST", ...).</summary>
        public string Why { get; private set; } = "";

        public Transform Target => target;
        /// <summary>What she is doing, for a display: CLOSING, BOW-ON BEAM, BEAM CHARGE, BEAM FIRE,
        /// BROADSIDE, SEARCH (none in sight), or DOWN.</summary>
        public string Doing { get; private set; } = "SEARCH";
        /// <summary>Her bow beam (null without one).</summary>
        public MainBeam Beam => beam;
        public bool Alive => health != null && !health.Dead && !health.Captured && gameObject.activeInHierarchy;
        public bool Taken => health != null && health.Captured;
        public float HullFraction => health != null && health.maxHealth > 0f ? health.Current / health.maxHealth : 0f;
        public float ShieldFraction => shield != null ? shield.Fraction : 0f;
        /// <summary>Hits she has taken, on her shield and her hull.</summary>
        public int HitsTaken => (health != null ? health.Hits : 0) + (shield != null ? shield.HitsByKind.Sum() : 0);

        void Start()
        {
            health = GetComponent<Health>();
            shield = GetComponentInChildren<Shield>();
            beam = useBeam ? GetComponentInChildren<MainBeam>() : null;
        }

        void Update()
        {
            if (ship == null || !Alive || ship.Destroyed) { Doing = "DOWN"; return; }
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 1f;
                target = Choose();
                ship.SetTarget(target);
                foreach (var t in ship.mainTurrets) if (t != null) t.autoFire = target != null;
            }
            if (section && SectionMoves()) return;
            if (target == null)
            {
                // none in sight: on towards where the encounter is
                Vector3 goal = encounter != null ? encounter.Centre : transform.position + transform.forward * 1000f;
                Steer(goal, 0.6f);
                Doing = "SEARCH";
                return;
            }
            Vector3 tp = target.position;
            float d = Vector3.Distance(transform.position, tp);
            bool beamBusy = beam != null && beam.Current != MainBeam.State.Idle;
            bool beamWanted = beam != null && beam.enabled && d < beam.range * 0.9f
                              && !(section && target.TryGetComponent(out VraskShip vt) && vt.role == VraskShip.Role.Skink);
            if (beamBusy || beamWanted)
            {
                // bow on: charge the beam once lined up, and hold it on the target while it fires
                Steer(tp, d > 2500f ? 0.7f : d < 1200f ? 0.15f : 0.4f);
                if (!beamBusy && Vector3.Angle(transform.forward, tp - transform.position) < 15f) beam.StartCharge();
                Doing = beam.Current == MainBeam.State.Firing ? "BEAM FIRE" : beam.Current == MainBeam.State.Charging ? "BEAM CHARGE" : "BOW-ON BEAM";
                return;
            }
            // a section out of gun range: straight in at full speed
            if (section && d > 4500f)
            {
                Steer(tp, 1f);
                Doing = "PRESSING";
                return;
            }
            // broadside on at gun range, the turrets firing (a section both the same way round)
            ship.rudder = ShipAutopilot.Broadside(ship, tp, section || slot % 2 == 0 ? 1 : -1, 1500f);
            ship.pitchInput = Mathf.Clamp(-ship.Pitch / 8f, -1f, 1f);
            ship.throttle = 0.7f;
            Doing = d > 3000f ? "CLOSING" : "BROADSIDE";
        }

        /// <summary>The section's own moves, before the attack: turning away to get her shield back,
        /// or the wing closing up on the lead (true: she has done one).</summary>
        bool SectionMoves()
        {
            var mate = Mate();
            bool firing = beam != null && beam.Current == MainBeam.State.Firing;
            bool charging = beam != null && beam.Current == MainBeam.State.Charging;
            float sh = ShieldFraction;
            if (recharging && (sh > 0.5f || firing || charging || mate == null || Time.time > rechargeUntil))
            {
                recharging = false;
                rechargeAgain = Time.time + 15f;
            }
            else if (!recharging && !firing && !charging && shield != null && sh < 0.2f && mate != null && Time.time > rechargeAgain
                     && (mate.ShieldFraction > sh + 0.15f || mate.HullFraction > HullFraction + 0.15f))
            {
                recharging = true;
                rechargeUntil = Time.time + 20f;
            }
            if (recharging && target != null)
            {
                // away from the target, towards her mate (and her guns)
                Vector3 away = (transform.position - target.position).normalized;
                Vector3 to = mate != null ? Vector3.Lerp(transform.position + away * 3000f, mate.transform.position, 0.5f) : transform.position + away * 3000f;
                Steer(to, 1f);
                Doing = "SHIELD RECHARGE";
                return true;
            }
            if (mate != null && target != null && slot != 0 && !firing && !charging
                && Vector3.Distance(transform.position, mate.transform.position) > 2000f)
            {
                Steer(Vector3.Lerp(mate.transform.position, target.position, 0.2f), 1f);
                Doing = "CLOSING UP";
                return true;
            }
            return false;
        }

        SimPilot Mate()
        {
            if (fleet == null) return null;
            foreach (var p in fleet) if (p != null && p != this && p.Alive) return p;
            return null;
        }

        /// <summary>How much a section wants a faction's ship dead, by its role (`d`: how far it is).</summary>
        static float RoleWeight(Transform t, float d)
        {
            if (!t.TryGetComponent(out FactionShip f)) return 1.5f;
            if (!(f is VraskShip v)) return f.SectionWeight(d);
            switch (v.role)
            {
                case VraskShip.Role.Snare: return 3f;
                case VraskShip.Role.Fang: return d < 4000f ? 3.2f : 2.4f;
                case VraskShip.Role.Maw: return 2.2f;
                default: return 1f;
            }
        }

        /// <summary>The section's target: her mate's captor first, else the lead's, else the best
        /// by the Vrask's order of danger, nearer and more damaged ones first.</summary>
        Transform ChooseSection()
        {
            // cover: the Fang clamped to her mate, or the Snare holding her
            foreach (var p in fleet)
            {
                if (p == null || p == this || !p.Alive || p.ship == null) continue;
                if (p.ship.Clamped && p.ship.ClampedBy != null)
                {
                    var fang = p.ship.ClampedBy.GetComponentInParent<Health>();
                    if (fang != null && Fit(fang.transform)) { Why = "COVER: FANG"; return fang.transform; }
                }
                Transform holder = null;
                float hd = float.MaxValue;
                foreach (var b in TractorBeam.BeamsHolding(p.transform))
                {
                    var h = b != null ? b.GetComponentInParent<Health>() : null;
                    if (h == null || !Fit(h.transform)) continue;
                    float dd = (h.transform.position - transform.position).sqrMagnitude;
                    if (dd < hd) { hd = dd; holder = h.transform; }
                }
                if (holder != null) { Why = "COVER: SNARE"; return holder; }
            }
            // the lead's
            if (slot != 0)
                foreach (var p in fleet)
                    if (p != null && p != this && p.Alive && p.slot == 0 && p.target != null && Fit(p.target)
                        && (p.target.position - transform.position).sqrMagnitude < 12000f * 12000f) { Why = "LEAD'S TARGET"; return p.target; }
            Transform best = null, any = null;
            float bestS = 0f, anyD = float.MaxValue;
            foreach (var h in encounter.enemies)
            {
                if (h == null || !Fit(h.transform) || Sensors.Unseen(h.transform)) continue;
                float d = Vector3.Distance(h.transform.position, transform.position);
                if (d < anyD) { anyD = d; any = h.transform; }
                if (d > 12000f || !Cover.Visible(transform.position, h.transform.position)) continue;
                float hull = h.maxHealth > 0f ? h.Current / h.maxHealth : 1f;
                float sc = RoleWeight(h.transform, d) * (1.6f - 0.6f * hull) / (1f + d / 2500f);
                // (she keeps on the one she has, unless another is much better)
                if (h.transform == target) sc *= 1.3f;
                if (sc > bestS) { bestS = sc; best = h.transform; }
            }
            if (best != null && best.TryGetComponent(out VraskShip vs)) Why = vs.role == VraskShip.Role.Snare ? "SNARE FIRST" : vs.role.ToString().ToUpperInvariant();
            else Why = best != null ? "NEAREST" : "HUNT";
            return best != null ? best : any;
        }

        void Steer(Vector3 to, float throttle)
        {
            var v = ShipAutopilot.Travel(ship, to, null, out float clear);
            ship.rudder = v.x;
            ship.pitchInput = v.y;
            ship.throttle = throttle * clear;
        }

        /// <summary>The fleet's target: the one the others are on if it is still in the fight and in
        /// her own sight, else the nearest enemy of the encounter she can see (any of it, if none is
        /// in sight). (Only in sight: a fleet all following one another onto a defence hidden behind
        /// its rock never chose again, and without the beam never hit anything.)</summary>
        Transform Choose()
        {
            if (encounter == null) return null;
            if (section) return ChooseSection();
            foreach (var p in fleet)
                if (p != null && p != this && p.Alive && p.target != null && Fit(p.target)
                    && (p.target.position - transform.position).sqrMagnitude < 12000f * 12000f
                    && Cover.Visible(transform.position, p.target.position)) return p.target;
            Transform best = null, any = null;
            float bestD = float.MaxValue, anyD = float.MaxValue;
            foreach (var h in encounter.enemies)
            {
                // (only what its sensors show: a cloaked or jammed-out ship isn't there to shoot)
                if (h == null || !Fit(h.transform) || Sensors.Unseen(h.transform)) continue;
                float dd = (h.transform.position - transform.position).sqrMagnitude;
                if (dd < anyD) { anyD = dd; any = h.transform; }
                if (dd < 12000f * 12000f && Cover.Visible(transform.position, h.transform.position) && dd < bestD) { bestD = dd; best = h.transform; }
            }
            // (none in sight: hunt the nearest, however far it has gone or wherever it hides; a fight
            // isn't over till the last of them is)
            return best != null ? best : any;
        }

        static bool Fit(Transform t) => t != null && t.gameObject.activeInHierarchy && t.TryGetComponent(out Health h) && !h.Dead && !h.Captured;
    }
}
