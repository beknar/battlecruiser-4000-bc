using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op in one level. One game in each level's room is the host: its enemies are the real
    /// ones, and it sends where they are, how damaged, and when they die, ten times a second. In
    /// every other game the enemies are puppets: their own flying and shooting are switched off
    /// and they follow the host's updates, their shots shown but harmless. A hit a player lands on
    /// a puppet goes to the host, whose game applies it to the real enemy; a hit the host's
    /// enemies land on another player's ship goes to that player's game. When the host leaves,
    /// the next player's game takes the enemies over where they are.
    ///
    /// Every game shows the other players' ships (RemotePlayer), sends its own ship fifteen
    /// times a second, and mirrors shots, missiles and beams so everyone sees the same fight.
    /// In the Star System, positions are relative to the star and every game runs its orbits on
    /// the room's clock, so the planets, and everything round them, are in the same places.
    /// Fights down on a planet's surface stay in each player's own game.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class CoopWorld : MonoBehaviour
    {
        public CarrierDemoInput demo;
        public static CoopWorld Instance { get; private set; }

        class Ent
        {
            public string id;
            public Transform t;
            public Health h;
            public CombatPilot pilot;
            public NetPuppet puppet;
            public readonly List<Behaviour> off = new List<Behaviour>();
            public double ts0 = -1, ts1 = -1;
            public Vector3 p0, p1, v1;
            public Quaternion q0 = Quaternion.identity, q1 = Quaternion.identity;
            public float baseHull;                 // its hull as built, for one player
            public Shield[] shields;
        }

        readonly Dictionary<string, Ent> ents = new Dictionary<string, Ent>();
        readonly Dictionary<Health, Ent> entByHealth = new Dictionary<Health, Ent>();
        readonly Dictionary<Transform, Ent> entByTransform = new Dictionary<Transform, Ent>();
        readonly Dictionary<int, RemotePlayer> remotes = new Dictionary<int, RemotePlayer>();
        readonly Dictionary<Transform, RemotePlayer> remoteByRoot = new Dictionary<Transform, RemotePlayer>();
        readonly List<Behaviour> directorsOff = new List<Behaviour>();
        readonly Dictionary<string, UnityEngine.Object> prefabCache = new Dictionary<string, UnityEngine.Object>();
        readonly Dictionary<string, float> fxBudget = new Dictionary<string, float>();

        CarrierController me;
        Health myHealth;
        Transform sysRoot;
        string level;
        bool registered, puppeting, clockOn;
        float nextShip, nextEnts;
        int scaledFor = 1;

        // ---- for the self-test
        public bool Puppeting => puppeting;
        public int Entities => ents.Count;
        public IEnumerable<string> EntityIds => ents.Keys;
        public int Remotes => remotes.Count;
        public RemotePlayer RemoteOf(int id) => remotes.TryGetValue(id, out var r) ? r : null;
        public int HitsSentToHost { get; private set; }
        public int HitsAppliedForOthers { get; private set; }
        public int HitsSentToPlayers { get; private set; }
        public int HitsTaken { get; private set; }
        public int ShotsShown { get; private set; }
        public int ShotsSent { get; private set; }
        public Transform EntityTransform(string id) => ents.TryGetValue(id, out var e) ? e.t : null;
        /// <summary>The number of players the enemies are scaled for (1 alone).</summary>
        public int ScaledFor => scaledFor;
        /// <summary>An enemy's hull as built, for one player.</summary>
        public float BaseHullOf(Health h) => h != null && entByHealth.TryGetValue(h, out var e) ? e.baseHull : h != null ? h.maxHealth : 0f;
        /// <summary>The last hit sent to another player's game (after scaling).</summary>
        public float LastHitToPlayer { get; private set; }

        static readonly HashSet<Type> Keep = new HashSet<Type>
        {
            typeof(Health), typeof(Shield), typeof(TargetInfo), typeof(ShipBreakup), typeof(MainBeam), typeof(Explosion),
            typeof(ThrusterFlare), typeof(EngineThrust), typeof(BarrelSpin), typeof(Drift), typeof(Obstacle), typeof(NetPuppet),
            typeof(OrbitHome), typeof(Beacon),
        };

        void Awake() => Instance = this;

        void Start()
        {
            me = demo != null ? demo.carrier : null;
            myHealth = me != null ? me.GetComponent<Health>() : null;
            sysRoot = OrbitalSystem.Instance != null ? OrbitalSystem.Instance.system : null;
            level = SceneManager.GetActiveScene().name;
            CoopClient.Message += OnMessage;
            Projectile.Spawned += OnShot;
            Missile.Spawned += OnMissile;
            MainBeam.Began += OnBeam;
            Health.Intercept = Intercept;
            Health.Adjust = AdjustHit;
            PlayerShips.Available = t => !remoteByRoot.TryGetValue(t.root, out var rp) || !rp.Hidden;
        }

        void OnDestroy()
        {
            CoopClient.Message -= OnMessage;
            Projectile.Spawned -= OnShot;
            Missile.Spawned -= OnMissile;
            MainBeam.Began -= OnBeam;
            if (Health.Intercept == (Func<Health, float, Vector3, HitKind, bool>)Intercept) Health.Intercept = null;
            if (Health.Adjust == (Func<Health, float, float>)AdjustHit) Health.Adjust = null;
            PlayerShips.Available = null;
            OrbitalSystem.ClockOverride = null;
            if (Instance == this) Instance = null;
        }

        CoopClient Client => CoopClient.Instance;
        /// <summary>Playing together in this level now.</summary>
        public bool On => CoopClient.Active && Client.RoomLevel == level;
        bool IsHost => On && Client.IsHost;

        /// <summary>From this game's world to the shared frame (relative to the star system) and back.</summary>
        public Vector3 ToNet(Vector3 w) => sysRoot != null ? w - sysRoot.position : w;
        public Vector3 FromNet(Vector3 n) => sysRoot != null ? n + sysRoot.position : n;

        // ------------------------------------------------------------------ the enemies

        /// <summary>Every enemy out in space, by its place in the level (the same in every game).</summary>
        void Register()
        {
            registered = true;
            foreach (var info in TargetInfo.All.ToArray())
            {
                if (info == null || !info.Hostile) continue;
                var t = info.transform;
                if (t.position.y < -50000f || t.GetComponentInParent<PlanetSurface>() != null) continue;   // on a planet: each game's own
                string id = PathOf(t);
                string unique = id;
                for (int n = 2; ents.ContainsKey(unique); n++) unique = $"{id}#{n}";
                var e = new Ent { id = unique, t = t, h = t.GetComponent<Health>(), pilot = t.GetComponent<CombatPilot>(), shields = t.GetComponentsInChildren<Shield>(true) };
                e.baseHull = e.h != null ? e.h.maxHealth : 0f;
                ents[unique] = e;
                entByTransform[t] = e;
                if (e.h != null) entByHealth[e.h] = e;
            }
            Debug.Log($"[Co-op] {ents.Count} enemies in '{level}'");
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>The enemy a transform belongs to (it or a parent), if any.</summary>
        Ent EntOf(Transform t)
        {
            for (var x = t; x != null; x = x.parent) if (entByTransform.TryGetValue(x, out var e)) return e;
            return null;
        }

        /// <summary>Hand the enemies over to the host (puppets here), or take them back.</summary>
        void SetPuppeting(bool on)
        {
            if (on == puppeting) return;
            puppeting = on;
            if (on)
            {
                foreach (var e in ents.Values)
                {
                    if (e.t == null) continue;
                    foreach (var b in e.t.GetComponentsInChildren<MonoBehaviour>(true))
                        if (b != null && b.enabled && !Keep.Contains(b.GetType())) { b.enabled = false; e.off.Add(b); }
                    e.puppet = e.t.gameObject.AddComponent<NetPuppet>();
                    e.ts0 = e.ts1 = -1;
                }
                // the things that start fights (ambushes, defence grids) are the host's too
                foreach (var b in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None)) Off(b);
                foreach (var b in FindObjectsByType<DefenseGrid>(FindObjectsSortMode.None)) Off(b);
                foreach (var b in FindObjectsByType<AmbushDirector>(FindObjectsSortMode.None)) Off(b);
                Debug.Log("[Co-op] the enemies are the host's: puppets here");
            }
            else
            {
                foreach (var e in ents.Values)
                {
                    foreach (var b in e.off) if (b != null) b.enabled = true;
                    e.off.Clear();
                    // a main gun that was only showing the host's shots does damage again
                    if (e.t != null) foreach (var beam in e.t.GetComponentsInChildren<MainBeam>(true)) beam.cosmetic = false;
                    if (e.puppet != null) Destroy(e.puppet);
                    e.puppet = null;
                    // one the host had destroyed comes back in its own time, as it would have
                    if (e.h != null && e.h.Dead && e.h.respawnDelay > 0f) StartCoroutine(RespawnLater(e.h, e.h.respawnDelay));
                }
                foreach (var b in directorsOff) if (b != null) b.enabled = true;
                directorsOff.Clear();
                Debug.Log("[Co-op] the enemies run here");
            }
        }

        void Off(Behaviour b) { if (b != null && b.enabled) { b.enabled = false; directorsOff.Add(b); } }

        // ------------------------------------------------------------------ more players, harder enemies

        /// <summary>Players in this level's room, me included (every game counts the same list).</summary>
        int PlayersHere()
        {
            int n = 0;
            foreach (var p in Client.Players) if (p.level == level) n++;
            return Mathf.Max(1, n);
        }

        /// <summary>Every enemy's hull and shields made as strong as `players` call for (see
        /// CoopScaling), each keeping how damaged it is.</summary>
        void ScaleEnemies(int players)
        {
            int was = scaledFor;
            scaledFor = players;
            float k = CoopScaling.Toughness(players);
            foreach (var e in ents.Values)
            {
                if (e.t == null) continue;
                if (e.h != null && e.baseHull > 0f) e.h.SetMaxHealth(e.baseHull * k);
                if (e.shields != null) foreach (var s in e.shields) if (s != null) s.ScaleTo(k);
            }
            Debug.Log($"[Co-op] {players} players here: the enemies' hull and shields x{k:0.##}, their hits x{CoopScaling.Firepower(players):0.##}");
            if (players > 1) CarrierDemoInput.Toast($"{players} players here: the enemies are {k:0.#}x as tough and hit {CoopScaling.Firepower(players):0.##}x as hard");
            else if (was > 1) CarrierDemoInput.Toast("Flying alone: the enemies are back to normal");
        }

        /// <summary>Health.Adjust: an enemy's hit on a player's ship (mine, or another player's in
        /// my game) is made as hard as the players here call for. Only the game running the
        /// enemies deals their hits (the host's, or one alone), and the hits it sends on to other
        /// players' games are already scaled, so nothing is scaled twice.</summary>
        float AdjustHit(Health h, float amount)
        {
            if (scaledFor <= 1 || !On || me == null || h == null) return amount;
            var src = Health.Source;
            if (src == null) return amount;
            bool onAPlayer = h == myHealth || remoteByRoot.ContainsKey(h.transform.root);
            if (!onAPlayer || EntOf(src) == null) return amount;
            return amount * CoopScaling.Firepower(scaledFor);
        }

        static System.Collections.IEnumerator RespawnLater(Health h, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (h != null) h.NetRespawn();
        }

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            if (!registered) Register();
            bool on = On;
            // the room's clock for the orbits (and back to our own, carrying on from it, after)
            if (sysRoot != null && on != clockOn)
            {
                clockOn = on;
                if (on) OrbitalSystem.ClockOverride = () => Client.OrbitClock;
                else
                {
                    OrbitalSystem.ClockOverride = null;
                    if (OrbitalSystem.Instance != null) OrbitalSystem.Instance.ContinueFrom(OrbitalSystem.Instance.Time);
                }
            }
            SetPuppeting(on && !Client.IsHost);
            // the enemies, as hard as the number of players here calls for
            int players = on ? PlayersHere() : 1;
            if (players != scaledFor) ScaleEnemies(players);
            if (!on)
            {
                if (remotes.Count > 0) ClearRemotes();
                return;
            }
            if (Time.unscaledTime >= nextShip)
            {
                nextShip = Time.unscaledTime + 1f / 15f;
                SendShip();
            }
            if (IsHost && Time.unscaledTime >= nextEnts)
            {
                nextEnts = Time.unscaledTime + 0.1f;
                SendEntities();
            }
            // players who have gone
            foreach (var id in remotes.Keys.ToArray())
                if (!Client.Players.Any(p => p.id == id && p.level == level)) RemoveRemote(id);
        }

        void LateUpdate()
        {
            if (!On) return;
            double now = Client.ServerNow;
            foreach (var r in remotes.Values) if (r != null) r.Place(this, now);
            if (puppeting) DrivePuppets(now - RemotePlayer.Delay);
        }

        // ------------------------------------------------------------------ my ship, and theirs

        void SendShip()
        {
            if (me == null) return;
            var shield = demo.ShieldGen;
            var warp = demo.Warp;
            var landing = PlanetLanding.Instance;
            var t = me.transform;
            Vector3 p = ToNet(t.position), v = me.Velocity;
            var q = t.rotation;
            Client.Send(new ShipMsg
            {
                ts = Client.ServerNow,
                x = p.x, y = p.y, z = p.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w, vx = v.x, vy = v.y, vz = v.z,
                th = me.throttle,
                sh = shield != null ? shield.Fraction : 0f, su = shield != null && shield.Up,
                hu = myHealth != null ? myHealth.Current / Mathf.Max(1f, myHealth.maxHealth) : 1f,
                dead = me.Destroyed,
                warp = warp != null ? (int)warp.Current : 0,
                dock = Docking.Docked,
                surf = landing != null && landing.Current != PlanetLanding.State.InSpace && landing.Body != null ? landing.Body.displayName : "",
                cap = Captain.Index,
            });
        }

        RemotePlayer Remote(int id)
        {
            if (remotes.TryGetValue(id, out var r) && r != null) return r;
            if (me == null) return null;
            // a copy of my own ship, with its controls and weapons taken out
            var holder = new GameObject("co-op ship (being made)");
            holder.SetActive(false);
            var go = Instantiate(me.gameObject, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null) continue;
                    var type = b.GetType();
                    if (Keep.Contains(type) && type != typeof(NetPuppet)) continue;
                    if (b is CarrierTurret turret) { turret.enabled = false; turret.autoFire = false; continue; }   // kept for its muzzles
                    DestroyImmediate(b);
                }
            foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(l);
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c.gameObject);
            string name = Client.NameOf(id);
            go.name = $"{name}'s {me.shipName.ToLower()}";
            go.transform.SetParent(null, false);
            Destroy(holder);
            r = go.AddComponent<RemotePlayer>();
            r.Init(id, name);
            remotes[id] = r;
            remoteByRoot[go.transform] = r;
            PlayerShips.Add(go.transform);
            Debug.Log($"[Co-op] {go.name} is here");
            return r;
        }

        void RemoveRemote(int id)
        {
            if (!remotes.TryGetValue(id, out var r)) return;
            remotes.Remove(id);
            if (r != null)
            {
                remoteByRoot.Remove(r.transform);
                PlayerShips.Remove(r.transform);
                Destroy(r.gameObject);
            }
        }

        void ClearRemotes()
        {
            foreach (var id in remotes.Keys.ToArray()) RemoveRemote(id);
        }

        // ------------------------------------------------------------------ the host's enemies

        void SendEntities()
        {
            var list = new List<CoopEntity>(ents.Count);
            foreach (var e in ents.Values)
            {
                if (e.t == null) { list.Add(new CoopEntity { id = e.id, gone = true, dead = true }); continue; }
                Vector3 p = ToNet(e.t.position);
                var q = e.t.rotation;
                var mover = Motion.Of(e.t);
                Vector3 v = mover != null ? mover.Velocity : Vector3.zero;
                list.Add(new CoopEntity
                {
                    id = e.id, x = p.x, y = p.y, z = p.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w, vx = v.x, vy = v.y, vz = v.z,
                    hu = e.h != null ? e.h.Current / Mathf.Max(1f, e.h.maxHealth) : 1f,
                    dead = e.h != null && e.h.Dead,
                    lurk = e.pilot != null && e.pilot.Current == CombatPilot.State.Lurk,
                });
            }
            Client.Send(new EntitiesMsg { ts = Client.ServerNow, e = list.ToArray() });
        }

        void ApplyEntities(CoopMsg m)
        {
            if (m.e == null) return;
            foreach (var s in m.e)
            {
                if (s == null || s.id == null || !ents.TryGetValue(s.id, out var e) || e.t == null) continue;
                if (e.h != null)
                {
                    if (s.dead) e.h.NetKill();
                    else
                    {
                        if (e.h.Dead) e.h.NetRespawn();
                        e.h.NetSetHull(s.hu * e.h.maxHealth);
                    }
                }
                if (s.gone || s.dead) continue;
                if (e.pilot != null) e.pilot.NetLurk(s.lurk);
                // the last two updates, to go between
                e.ts0 = e.ts1; e.p0 = e.p1; e.q0 = e.q1;
                e.ts1 = m.ts; e.p1 = new Vector3(s.x, s.y, s.z); e.q1 = new Quaternion(s.qx, s.qy, s.qz, s.qw); e.v1 = new Vector3(s.vx, s.vy, s.vz);
                if (e.ts0 < 0) { e.ts0 = e.ts1; e.p0 = e.p1; e.q0 = e.q1; }
            }
        }

        void DrivePuppets(double t)
        {
            foreach (var e in ents.Values)
            {
                if (e.t == null || e.ts1 < 0 || (e.h != null && e.h.Dead)) continue;
                Vector3 p;
                Quaternion q;
                if (t >= e.ts1)
                {
                    float ahead = (float)Math.Min(0.4, t - e.ts1);
                    p = e.p1 + e.v1 * ahead;
                    q = e.q1;
                }
                else
                {
                    float k = e.ts1 > e.ts0 ? Mathf.Clamp01((float)((t - e.ts0) / (e.ts1 - e.ts0))) : 1f;
                    p = Vector3.Lerp(e.p0, e.p1, k);
                    q = Quaternion.Slerp(e.q0, e.q1, k);
                }
                e.t.SetPositionAndRotation(FromNet(p), q);
                if (e.puppet != null) e.puppet.velocity = e.v1;
            }
        }

        // ------------------------------------------------------------------ hits

        /// <summary>Every hit comes here first (Health.Intercept): it is taken (true) when it
        /// belongs to another player's game.</summary>
        bool Intercept(Health h, float amount, Vector3 point, HitKind kind)
        {
            if (!On || me == null) return false;
            var src = Health.Source;
            var srcRoot = src != null ? src.root : null;
            bool fromMe = srcRoot != null && srcRoot == me.transform.root;
            bool fromPlayers = fromMe || (srcRoot != null && remoteByRoot.ContainsKey(srcRoot));
            // another player's ship: the host's enemies' hits go to that player (no friendly fire)
            if (remoteByRoot.TryGetValue(h.transform.root, out var rp))
            {
                if (!fromPlayers && IsHost)
                {
                    Vector3 p = ToNet(point);
                    Client.Send(new HitPlayerMsg { to = rp.Id, a = amount, kd = (int)kind, x = p.x, y = p.y, z = p.z });
                    HitsSentToPlayers++;
                    LastHitToPlayer = amount;
                }
                return true;
            }
            // my ship: nothing from another player's (shown) shots
            if (h == myHealth) return fromPlayers && !fromMe;
            // an enemy whose real self is in the host's game: my hits go there
            if (puppeting && entByHealth.TryGetValue(h, out var e))
            {
                if (fromMe)
                {
                    Vector3 p = ToNet(point);
                    Client.Send(new HitEnemyMsg { tg = e.id, a = amount, kd = (int)kind, x = p.x, y = p.y, z = p.z });
                    HitsSentToHost++;
                }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ shots, missiles, beams

        /// <summary>Who fired: "p&lt;id&gt;" for my ship, or an enemy's id if I'm the host.</summary>
        string SourceOf(Transform owner)
        {
            if (owner == null || me == null) return null;
            if (owner.root == me.transform.root) return "p" + Client.MyId;
            if (!IsHost) return null;
            var e = EntOf(owner);
            return e != null ? e.id : null;
        }

        /// <summary>No more than so many shots a second from one ship (a fast gun or the CIWS).</summary>
        bool Budget(string src)
        {
            fxBudget.TryGetValue(src, out float next);
            if (Time.unscaledTime < next) return false;
            fxBudget[src] = Time.unscaledTime + 1f / 25f;
            return true;
        }

        static string BaseName(string n) => n.EndsWith("(Clone)") ? n.Substring(0, n.Length - 7).Trim() : n;

        void OnShot(Projectile p)
        {
            if (!On || p == null || p.damage <= 0f) return;          // a shown shot (no damage) isn't news
            string src = SourceOf(p.owner);
            if (src == null || !Budget(src)) return;
            Vector3 pos = ToNet(p.transform.position), d = p.transform.forward;
            Client.Send(new FireMsg { src = src, k = "shot", pf = BaseName(p.name), x = pos.x, y = pos.y, z = pos.z, dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        void OnMissile(Missile m)
        {
            if (!On || m == null || m.damage <= 0f) return;
            string src = SourceOf(m.owner);
            if (src == null) return;
            Vector3 pos = ToNet(m.transform.position), d = m.transform.forward;
            Client.Send(new FireMsg { src = src, k = "msl", pf = BaseName(m.name), tg = IdOf(m.target), x = pos.x, y = pos.y, z = pos.z, dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        void OnBeam(MainBeam b)
        {
            if (!On || b == null) return;
            string src = SourceOf(b.transform);
            if (src == null) return;
            Vector3 d = b.BeamDirection;
            Client.Send(new FireMsg { src = src, k = "beam", dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        /// <summary>A target as the others know it: a player ("p&lt;id&gt;") or an enemy's id.</summary>
        string IdOf(Transform t)
        {
            if (t == null) return "";
            if (me != null && t.root == me.transform.root) return "p" + Client.MyId;
            if (remoteByRoot.TryGetValue(t.root, out var rp)) return "p" + rp.Id;
            var e = EntOf(t);
            return e != null ? e.id : "";
        }

        /// <summary>A ship from its id: mine, another player's, or an enemy.</summary>
        Transform Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("p") && int.TryParse(id.Substring(1), out int pid))
            {
                if (pid == Client.MyId) return me != null ? me.transform : null;
                return remotes.TryGetValue(pid, out var r) && r != null && !r.Hidden ? r.transform : null;
            }
            return ents.TryGetValue(id, out var e) ? e.t : null;
        }

        /// <summary>The shot or missile a ship fires, by name, from its own guns and launchers.</summary>
        T PrefabOf<T>(Transform ship, string name) where T : UnityEngine.Object
        {
            string key = $"{ship.GetInstanceIDSafe()}|{typeof(T).Name}|{name}";
            if (prefabCache.TryGetValue(key, out var cached)) return cached as T;
            T found = null;
            foreach (var tu in ship.GetComponentsInChildren<CarrierTurret>(true))
                if (tu.projectilePrefab is T a && (found == null || tu.projectilePrefab.name == name)) { found = a; if (tu.projectilePrefab.name == name) break; }
            foreach (var w in ship.GetComponentsInChildren<EnemyWeapons>(true))
            {
                if (w.gunPrefab is T g && (found == null || w.gunPrefab.name == name)) found = g;
                if (w.missilePrefab is T mm && (found == null || w.missilePrefab.name == name)) found = mm;
            }
            foreach (var l in ship.GetComponentsInChildren<MissileLauncher>(true))
                if (l.missilePrefab is T lm && (found == null || l.missilePrefab.name == name)) found = lm;
            prefabCache[key] = found;
            return found;
        }

        void ShowFire(CoopMsg m)
        {
            var ship = Resolve(m.src);
            if (ship == null || (me != null && ship == me.transform)) return;
            Vector3 pos = FromNet(new Vector3(m.x, m.y, m.z)), dir = new Vector3(m.dx, m.dy, m.dz);
            if (dir.sqrMagnitude < 1e-6f) dir = ship.forward;
            switch (m.k)
            {
                case "shot":
                {
                    var prefab = PrefabOf<Projectile>(ship, m.pf);
                    if (prefab == null) return;
                    var p = Instantiate(prefab, pos, Quaternion.LookRotation(dir));
                    p.damage = 0f;                                      // for show: the real one is in their game
                    p.owner = ship.root;
                    ShotsShown++;
                    break;
                }
                case "msl":
                {
                    var prefab = PrefabOf<Missile>(ship, m.pf);
                    if (prefab == null) return;
                    var mi = Instantiate(prefab, pos, Quaternion.LookRotation(dir));
                    mi.damage = 0f;
                    mi.owner = ship.root;
                    mi.target = Resolve(m.tg);
                    ShotsShown++;
                    break;
                }
                case "beam":
                {
                    var beam = ship.GetComponentInChildren<MainBeam>(true);
                    if (beam == null) return;
                    if (!beam.enabled) beam.enabled = true;
                    beam.NetFire(dir);
                    ShotsShown++;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ messages

        void OnMessage(CoopMsg m)
        {
            if (!On || m.from == Client.MyId) return;
            switch (m.t)
            {
                case "ps":
                    Remote(m.from)?.Push(m);
                    break;
                case "es":
                    if (puppeting) ApplyEntities(m);
                    break;
                case "fx":
                    ShowFire(m);
                    break;
                case "de":
                    // a hit from another player's game on one of my enemies (I'm the host)
                    if (IsHost && ents.TryGetValue(m.tg ?? "", out var e) && e.h != null && !e.h.Dead)
                    {
                        Health.Source = remotes.TryGetValue(m.from, out var who) && who != null ? who.transform : null;
                        e.h.Damage(m.a, FromNet(new Vector3(m.x, m.y, m.z)), (HitKind)m.kd);
                        Health.Source = null;
                        HitsAppliedForOthers++;
                    }
                    break;
                case "dp":
                    // the host's enemies hit me
                    if (myHealth != null && !myHealth.Dead)
                    {
                        Health.Source = null;
                        myHealth.Damage(m.a, FromNet(new Vector3(m.x, m.y, m.z)), (HitKind)m.kd);
                        HitsTaken++;
                    }
                    break;
                case "leave":
                    RemoveRemote(m.id);
                    break;
                case "players":
                    foreach (var p in Client.Players) if (remotes.TryGetValue(p.id, out var r) && r != null) r.Rename(p.name);
                    break;
            }
        }

        // ------------------------------------------------------------------ names over the other ships

        void OnGUI()
        {
            if (!On || Camera.main == null) return;
            var cam = Camera.main;
            float s = Hud.S;
            foreach (var r in remotes.Values)
            {
                if (r == null || r.Hidden) continue;
                Vector3 sp = cam.WorldToScreenPoint(r.transform.position + Vector3.up * 40f);
                if (sp.z <= 0f) continue;
                var st = Hud.Text(13, true, TextAnchor.LowerCenter);
                string text = $"<color=#7ee08a>{r.Name}</color>  <color=#9aa4b0>{Vector3.Distance(cam.transform.position, r.transform.position) / 1000f:F1} km</color>";
                var size = st.CalcSize(new GUIContent(text));
                var rect = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y - 8f * s, size.x, size.y);
                GUI.Label(rect, text, st);
                // shield and hull
                float w = 60f * s, h = 3f * s;
                var bar = new Rect(sp.x - w * 0.5f, rect.yMax + 2f * s, w, h);
                Hud.Fill(bar, new Color(1f, 1f, 1f, 0.15f));
                Hud.Fill(new Rect(bar.x, bar.y, w * Mathf.Clamp01(r.Shield), h), new Color(0.35f, 0.78f, 1f));
                bar.y += h + 2f * s;
                Hud.Fill(bar, new Color(1f, 1f, 1f, 0.15f));
                Hud.Fill(new Rect(bar.x, bar.y, w * Mathf.Clamp01(r.Hull), h), new Color(0.45f, 0.9f, 0.55f));
            }
        }
    }

    static class TransformKey
    {
        /// <summary>A key for a transform that doesn't use the (now error-raising) GetInstanceID.</summary>
        public static string GetInstanceIDSafe(this Transform t) => t.GetHashCode().ToString();
    }
}
