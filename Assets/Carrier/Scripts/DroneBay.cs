using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A drone bay (the Imperial assault frigate's, the Grey scout's): in a fight it launches
    /// combat drones from its "drone_bay" points, `perLaunch` at a time and at most `maxOut` in
    /// the air, from a `stock` that drones lost in the fight come out of; out of the fight (no
    /// player's ship within `recallRange`, its ship destroyed, or `recall` set by its tactics)
    /// it calls them home, and each that gets back is stock again.
    ///
    /// A drone is the prefab under `key` in CoopSpawnables (an EnemyDrone), with a fighter's
    /// pilot (CombatPilot.Style.Fighter): hostile small craft, so the players' CIWS goes for it.
    /// It is made during play, so it is told to co-op (CoopSpawn) and made as a puppet in the
    /// other players' games. Only the game running the enemies launches (the host's: off on a
    /// guest's puppet). Its stock fills again when its ship respawns.
    /// </summary>
    public class DroneBay : MonoBehaviour
    {
        /// <summary>The bay drone's key in CoopSpawnables (made by the builder).</summary>
        public const string DroneKey = "BayDrone";

        [Tooltip("The drone, by its key in CoopSpawnables.")]
        public string key = DroneKey;
        public int stock = 6;
        public int maxOut = 3;
        public int perLaunch = 2;
        public float launchInterval = 12f;
        [Tooltip("It launches when a player's ship is within this (m) and its ship is fighting.")]
        public float launchRange = 9000f;
        [Tooltip("It calls them home when no player's ship is within this (m).")]
        public float recallRange = 18000f;
        [Tooltip("How fast (m/s) and how sharply (deg/s) its drones fly home when called.")]
        public float droneSpeed = 260f, droneTurn = 120f;
        [Tooltip("Set by its tactics: call the drones home now.")]
        public bool recall;
        [Tooltip("Where they launch from (its drone_bay points; the ship itself if none).")]
        public Transform[] bays = new Transform[0];

        public readonly List<CombatPilot> Out = new List<CombatPilot>();

        /// <summary>Drones launched, lost, and recovered (the tests).</summary>
        public int Launched { get; private set; }
        public int Lost { get; private set; }
        public int Recovered { get; private set; }
        public int Stock { get; private set; }

        Health health;
        CombatPilot pilot;
        float next;
        int bayIndex;
        GameObject prefab;
        readonly List<CombatPilot> homing = new List<CombatPilot>();

        void Awake()
        {
            health = GetComponent<Health>();
            pilot = GetComponent<CombatPilot>();
        }

        void Start()
        {
            // (its stock as set up: in Start, after whoever added it has set it)
            Stock = stock;
            if (health != null)
            {
                health.onRespawned ??= new UnityEngine.Events.UnityEvent();
                health.onRespawned.AddListener(() => Stock = stock);
            }
        }

        void OnDisable()
        {
            // (the bay off: its drones go; back in stock unless its ship is gone)
            bool alive = health == null || !health.Dead;
            foreach (var d in Out.ToArray()) Retire(d, alive);
        }

        /// <summary>Its ship is in a fight: alive, its pilot (if any) out of hiding.</summary>
        bool Fighting => (health == null || !health.Dead) && (pilot == null || (pilot.enabled && pilot.Current != CombatPilot.State.Lurk));

        float NearestPlayer()
        {
            float best = float.MaxValue;
            foreach (var p in PlayerShips.All)
                if (p != null && p.gameObject.activeInHierarchy && PlayerShips.Present(p))
                    best = Mathf.Min(best, (p.position - transform.position).sqrMagnitude);
            return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
        }

        void Update()
        {
            // the lost: destroyed (no respawn: out of the stock for good)
            for (int i = Out.Count - 1; i >= 0; i--)
            {
                var d = Out[i];
                if (d != null && !(d.TryGetComponent(out Health dh) && dh.Dead)) continue;
                Out.RemoveAt(i);
                homing.Remove(d);
                Lost++;
                // (not told gone: its death and breakup reach co-op as any enemy's, and once its
                // Health has destroyed it the co-op world says it's gone)
            }
            float near = NearestPlayer();
            bool home = recall || !Fighting || near > recallRange;
            if (home) { CallHome(); return; }
            // (back to the fight: those on their way home turn round)
            foreach (var d in homing) if (d != null) d.externalMotion = false;
            homing.Clear();
            if (Time.time < next || near > launchRange || Out.Count >= maxOut || Stock <= 0 || ShipSystems.WeaponsDownOn(this, ref systems)) return;
            next = Time.time + launchInterval;
            for (int n = 0; n < perLaunch && Out.Count < maxOut && Stock > 0; n++) Launch();
        }

        ShipSystems systems;

        void Launch()
        {
            if (prefab == null) prefab = CoopSpawnables.Find(key);
            if (prefab == null) return;
            var at = bays != null && bays.Length > 0 ? bays[bayIndex++ % bays.Length] : transform;
            if (at == null) at = transform;
            var go = Instantiate(prefab, at.position + at.forward * 30f, at.rotation);
            go.name = $"{name} Drone {Launched + 1}";
            // (the bay drone is set up in its prefab, the same in every co-op game: a fighter's
            // pilot out of hiding from the start, no circling of its own, no respawn; the EnemyDrone,
            // for a bay given that key, is made one here)
            foreach (var m in go.GetComponentsInChildren<Demo.OrbitMover>()) m.enabled = false;
            if (go.TryGetComponent(out Health h)) h.respawnDelay = 0f;
            if (!go.TryGetComponent(out CombatPilot p))
            {
                p = go.AddComponent<CombatPilot>();
                p.style = CombatPilot.Style.Fighter;
                p.cruiseSpeed = droneSpeed * 0.8f;
                p.attackSpeed = droneSpeed;
                p.turnRate = droneTurn;
                p.acceleration = droneSpeed;
            }
            p.concealed = false;
            p.startLurking = false;
            p.NetLurk(false);
            var info = go.GetComponent<TargetInfo>();
            if (info != null)
            {
                info.displayName = $"{(TryGetComponent(out TargetInfo mine) ? mine.Name : name)} drone";
                info.group = mine != null ? mine.group : info.group;
            }
            Out.Add(p);
            Stock--;
            Launched++;
            CoopSpawn.Spawned(go, key);
        }

        /// <summary>Every drone out flies home; those back are stock again.</summary>
        void CallHome()
        {
            foreach (var d in Out.ToArray())
            {
                if (d == null) continue;
                if (!homing.Contains(d)) { homing.Add(d); d.externalMotion = true; }
                Vector3 to = HomeFor(d.transform.position) - d.transform.position;
                float dist = to.magnitude;
                // (its ship destroyed: no home to go to; it goes down with it)
                if (health != null && health.Dead) { Down(d); continue; }
                if (dist < 150f) { Retire(d, true); continue; }
                float step = Mathf.Min(dist, droneSpeed * Time.deltaTime);
                d.transform.rotation = Quaternion.RotateTowards(d.transform.rotation, Quaternion.LookRotation(to), droneTurn * Time.deltaTime);
                d.transform.position += to / dist * step;
            }
        }

        /// <summary>Drones put back in its stock (a Patrol frigate's logistics), up to its full
        /// stock counting those out: true if any went in.</summary>
        public bool Restock(int n)
        {
            int before = Stock;
            Stock = Mathf.Max(Stock, Mathf.Min(stock - Out.Count, Stock + Mathf.Max(0, n)));
            return Stock > before;
        }

        /// <summary>The nearest of its bays to a point (its ship if it has none).</summary>
        Vector3 HomeFor(Vector3 from)
        {
            Vector3 best = transform.position;
            float bd = float.MaxValue;
            if (bays != null)
                foreach (var b in bays)
                {
                    if (b == null) continue;
                    float d = (b.position - from).sqrMagnitude;
                    if (d < bd) { bd = d; best = b.position; }
                }
            return best;
        }

        /// <summary>A drone whose ship is gone: destroyed (its own explosion), lost.</summary>
        void Down(CombatPilot d)
        {
            Out.Remove(d);
            homing.Remove(d);
            Lost++;
            if (d != null && d.TryGetComponent(out Health dh) && !dh.Dead) dh.Damage(1e9f, d.transform.position, HitKind.Cannon);
            else if (d != null) { CoopSpawn.Gone(d.gameObject); Destroy(d.gameObject); }
        }

        /// <summary>A drone off the field: back in the bay (stock again), or gone with its ship.</summary>
        void Retire(CombatPilot d, bool aboard)
        {
            Out.Remove(d);
            homing.Remove(d);
            if (d == null) return;
            if (aboard) { Stock = Mathf.Min(stock, Stock + 1); Recovered++; }
            CoopSpawn.Gone(d.gameObject);
            Destroy(d.gameObject);
        }
    }
}
