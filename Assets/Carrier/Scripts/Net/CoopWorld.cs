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
    ///
    /// Here: joining and leaving, the frame's work and the messages. The enemies, the ships, the
    /// fire and the hits each have a file of their own (CoopWorld.*.cs).
    /// </summary>
    [DefaultExecutionOrder(500)]
    public partial class CoopWorld : MonoBehaviour
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
            PlayerShips.Available = t => !remoteByRoot.TryGetValue(t.root, out var rp) || !rp.Hidden;
        }

        void OnDestroy()
        {
            CoopClient.Message -= OnMessage;
            Projectile.Spawned -= OnShot;
            Missile.Spawned -= OnMissile;
            MainBeam.Began -= OnBeam;
            if (Health.Intercept == (Func<Health, float, Vector3, HitKind, bool>)Intercept) Health.Intercept = null;
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

        // ------------------------------------------------------------------ messages

        void OnMessage(CoopMsg m)
        {
            if (!On || m.from == Client.MyId) return;
            switch (m.t)
            {
                case CoopWire.Ship:
                    Remote(m.from)?.Push(m);
                    break;
                case CoopWire.Enemies:
                    if (puppeting) ApplyEntities(m);
                    break;
                case CoopWire.Fire:
                    ShowFire(m);
                    break;
                case CoopWire.HitEnemy:
                    // a hit from another player's game on one of my enemies (I'm the host)
                    if (IsHost && ents.TryGetValue(m.tg ?? "", out var e) && e.h != null && !e.h.Dead)
                    {
                        Health.Source = remotes.TryGetValue(m.from, out var who) && who != null ? who.transform : null;
                        e.h.Damage(m.a, FromNet(new Vector3(m.x, m.y, m.z)), (HitKind)m.kd);
                        Health.Source = null;
                        HitsAppliedForOthers++;
                    }
                    break;
                case CoopWire.HitPlayer:
                    // the host's enemies hit me
                    if (myHealth != null && !myHealth.Dead)
                    {
                        Health.Source = null;
                        myHealth.Damage(m.a, FromNet(new Vector3(m.x, m.y, m.z)), (HitKind)m.kd);
                        HitsTaken++;
                    }
                    break;
                case CoopWire.Leave:
                    RemoveRemote(m.id);
                    break;
                case CoopWire.Players:
                    foreach (var p in Client.Players) if (remotes.TryGetValue(p.id, out var r) && r != null) r.Rename(p.name);
                    break;
            }
        }

    }

    static class TransformKey
    {
        /// <summary>A key for a transform that doesn't use the (now error-raising) GetInstanceID.</summary>
        public static string GetInstanceIDSafe(this Transform t) => t.GetHashCode().ToString();
    }
}
