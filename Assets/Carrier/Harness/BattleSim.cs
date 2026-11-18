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
            var cases = Cases();
            Log($"{Classes.Count} classes ({string.Join(", ", Classes.Select(c => entries[c].displayName))}), " +
                $"{Encounters.Count} encounters, {Fleets().Count} fleets: {cases.Count} battles");
            foreach (var e in Encounters) Log($"  encounter {e.name} ({e.kind}): {e.enemies.Count} - {string.Join(", ", e.enemies.Select(h => h.name))}");
            if (LaunchArgs.Has("-simlist")) { WriteCases(cases); Quit(); yield break; }

            var done = Done();
            int from = (int)LaunchArgs.FloatAfter("-simfrom", 0f), count = (int)LaunchArgs.FloatAfter("-simcount", cases.Count);
            if (!File.Exists(csv))
                File.WriteAllText(csv, "case,encounter,kind,fleet,size,main_gun,outcome,seconds,enemies_killed,enemies_total,frigates_lost,frigates_captured,hull_left,shield_left,withdrew,hits_taken\n");
            float real0 = Time.realtimeSinceStartup;
            int fought = 0;
            for (int i = from; i < Mathf.Min(cases.Count, from + count); i++)
            {
                if (done.Contains(i)) continue;
                var c = cases[i];
                yield return Fight(i, c.enc, c.fleet, c.gun);
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

        public struct Case { public Encounter enc; public string[] fleet; public bool gun; }

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

        string FleetName(string[] f) => string.Join("+", f.Select(id => entries[id].displayName));

        void WriteCases(List<Case> cases)
        {
            var sb = new StringBuilder("case,encounter,fleet,main_gun\n");
            for (int i = 0; i < cases.Count; i++) sb.Append($"{i},{cases[i].enc.name},{FleetName(cases[i].fleet)},{cases[i].gun}\n");
            File.WriteAllText(Path.Combine(dir, "cases.csv"), sb.ToString());
        }

        HashSet<int> Done()
        {
            var done = new HashSet<int>();
            if (!File.Exists(csv)) return done;
            foreach (var line in File.ReadAllLines(csv).Skip(1))
                if (int.TryParse(line.Split(',')[0], out int n)) done.Add(n);
            return done;
        }

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

        IEnumerator Fight(int index, Encounter enc, string[] ships, bool gun)
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
                                      hull.ToString("F2", CultureInfo.InvariantCulture), shield.ToString("F2", CultureInfo.InvariantCulture), withdrew ? 1 : 0, hits);
            File.AppendAllText(csv, line + "\n");
            Log($"case {index}: {FleetName(ships)} {(gun ? "with" : "without")} main guns vs {enc.name}: {outcome} in {secs:F0} s, " +
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
            if (enc.group != null) enc.group.holdOff = false;
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
    /// </summary>
    public class SimPilot : MonoBehaviour
    {
        public CarrierController ship;
        public int slot;
        public bool useBeam = true;
        public List<SimPilot> fleet;
        public BattleSim.Encounter encounter;

        Health health;
        Shield shield;
        MainBeam beam;
        Transform target;
        float nextLook;

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
            bool beamWanted = beam != null && beam.enabled && d < beam.range * 0.9f;
            if (beamBusy || beamWanted)
            {
                // bow on: charge the beam once lined up, and hold it on the target while it fires
                Steer(tp, d > 2500f ? 0.7f : d < 1200f ? 0.15f : 0.4f);
                if (!beamBusy && Vector3.Angle(transform.forward, tp - transform.position) < 15f) beam.StartCharge();
                Doing = beam.Current == MainBeam.State.Firing ? "BEAM FIRE" : beam.Current == MainBeam.State.Charging ? "BEAM CHARGE" : "BOW-ON BEAM";
                return;
            }
            // broadside on at gun range, the turrets firing
            ship.rudder = ShipAutopilot.Broadside(ship, tp, slot % 2 == 0 ? 1 : -1, 1500f);
            ship.pitchInput = Mathf.Clamp(-ship.Pitch / 8f, -1f, 1f);
            ship.throttle = 0.7f;
            Doing = d > 3000f ? "CLOSING" : "BROADSIDE";
        }

        void Steer(Vector3 to, float throttle)
        {
            var v = ShipAutopilot.Travel(ship, to, null, out float clear);
            ship.rudder = v.x;
            ship.pitchInput = v.y;
            ship.throttle = throttle * clear;
        }

        /// <summary>The fleet's target: the one the others are on if it is still in the fight, else
        /// the nearest enemy of the encounter it can see (any of it, if none is in sight).</summary>
        Transform Choose()
        {
            if (encounter == null) return null;
            foreach (var p in fleet)
                if (p != null && p != this && p.Alive && p.target != null && Fit(p.target)) return p.target;
            Transform best = null, any = null;
            float bestD = float.MaxValue, anyD = float.MaxValue;
            foreach (var h in encounter.enemies)
            {
                if (h == null || !Fit(h.transform)) continue;
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
