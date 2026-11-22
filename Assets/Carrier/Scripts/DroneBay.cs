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
        [Tooltip("The drone, by its key in CoopSpawnables.")]
        public string key = "EnemyDrone";
        public int stock = 6;
        public int maxOut = 3;
        public int perLaunch = 2;
        public float launchInterval = 12f;
        [Tooltip("It launches when a player's ship is within this (m) and its ship is fighting.")]
        public float launchRange = 9000f;
        [Tooltip("It calls them home when no player's ship is within this (m).")]
        public float recallRange = 18000f;
        [Tooltip("Its drones' pilot: cruise and attack speed (m/s), turn (deg/s), hull.")]
        public float droneSpeed = 260f, droneTurn = 120f, droneHull = 300f;
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
            // (the bay off, its ship gone: its drones go with it)
            foreach (var d in Out.ToArray()) Retire(d, false);
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
                if (d != null) CoopSpawn.Gone(d.gameObject);
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
            // (a drone of the bay's: no circling on its own, no respawn, a fighter's pilot)
            foreach (var m in go.GetComponentsInChildren<Demo.OrbitMover>()) m.enabled = false;
            var h = go.GetComponent<Health>();
            if (h != null) { h.respawnDelay = 0f; h.maxHealth = droneHull; h.Repair(); }
            var p = go.GetComponent<CombatPilot>() ?? go.AddComponent<CombatPilot>();
            p.style = CombatPilot.Style.Fighter;
            p.concealed = false;
            p.startLurking = false;
            p.cruiseSpeed = droneSpeed * 0.8f;
            p.attackSpeed = droneSpeed;
            p.turnRate = droneTurn;
            p.acceleration = droneSpeed;
            // (out of hiding at once: a pilot added here chose to lie in wait as it woke, before
            // startLurking could be turned off)
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
                Vector3 to = transform.position - d.transform.position;
                float dist = to.magnitude;
                // (its ship destroyed: no home to go to; it goes down with it)
                if (health != null && health.Dead) { Retire(d, false); continue; }
                if (dist < 150f) { Retire(d, true); continue; }
                float step = Mathf.Min(dist, droneSpeed * Time.deltaTime);
                d.transform.rotation = Quaternion.RotateTowards(d.transform.rotation, Quaternion.LookRotation(to), droneTurn * Time.deltaTime);
                d.transform.position += to / dist * step;
            }
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
