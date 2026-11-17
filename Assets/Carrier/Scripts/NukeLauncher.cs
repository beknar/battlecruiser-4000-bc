using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Maw's nuclear silos (its nuke_cell points): a few nuclear missiles (NukeMissile), no
    /// more, launched one at a time; the stock is filled again only when the ship is (destroyed
    /// and) back. Not while the ship is destroyed, lying in wait, or its weapons are knocked out.
    /// The Maw's tactics decide when (MawTactics: rarely, at a fortified target).
    /// </summary>
    public class NukeLauncher : MonoBehaviour
    {
        public NukeMissile nukePrefab;
        [Tooltip("The silos, facing the way the missiles leave.")]
        public Transform[] cells;
        [Tooltip("Missiles aboard when full.")]
        public int capacity = 2;
        [Tooltip("Furthest (m) it launches at a ship.")]
        public float range = 9000f;
        [Tooltip("Seconds between launches.")]
        public float interval = 90f;

        public int Stock { get; private set; }
        public int Launched { get; private set; }
        public NukeMissile Last { get; private set; }

        float nextLaunch;
        int next;
        Health health;
        ShipSystems systems;
        EnemyWeapons weapons;
        CombatPilot pilot;

        void Awake()
        {
            health = GetComponentInParent<Health>();
            weapons = GetComponentInParent<EnemyWeapons>();
            pilot = GetComponentInParent<CombatPilot>();
            Stock = capacity;
        }

        void Start()
        {
            if (health == null) return;
            health.onRespawned ??= new UnityEngine.Events.UnityEvent();
            health.onRespawned.AddListener(Restock);
        }

        /// <summary>The silos filled again (the ship back from being destroyed).</summary>
        public void Restock()
        {
            Stock = capacity;
            nextLaunch = Time.time + interval * 0.5f;
        }

        /// <summary>One can go now.</summary>
        public bool Ready => nukePrefab != null && Stock > 0 && Time.time >= nextLaunch && (health == null || !health.Dead) &&
                             (weapons == null || weapons.weaponsFree) && (pilot == null || pilot.Current != CombatPilot.State.Lurk) &&
                             !ShipSystems.WeaponsDownOn(this, ref systems);

        /// <summary>Launch one at a ship in range, if one can go.</summary>
        public NukeMissile Launch(Transform target)
        {
            if (!Ready || target == null || (target.position - transform.position).sqrMagnitude > range * range) return null;
            return LaunchNow(target);
        }

        /// <summary>Launch one at a ship whatever the range and the wait (the self-test), if any are left.</summary>
        public NukeMissile LaunchNow(Transform target)
        {
            if (nukePrefab == null || Stock <= 0 || target == null) return null;
            Stock--;
            Launched++;
            nextLaunch = Time.time + interval;
            var at = Cell();
            Vector3 v = pilot != null ? pilot.Velocity : Vector3.zero;
            var ship = health != null ? health.transform : transform.root;
            Last = Fire(nukePrefab, at.position, at.rotation, ship, target, v);
            var info = ship.GetComponent<TargetInfo>();
            Notices.Post($"Nuclear missile launched by {(info != null ? info.Name : ship.name)}!");
            return Last;
        }

        Transform Cell()
        {
            if (cells == null || cells.Length == 0) return transform;
            for (int tries = 0; tries < cells.Length; tries++)
            {
                var c = cells[next];
                next = (next + 1) % cells.Length;
                if (c != null) return c;
            }
            return transform;
        }

        /// <summary>One nuclear missile, from a point at a ship (also the tests').</summary>
        public static NukeMissile Fire(NukeMissile prefab, Vector3 at, Quaternion rotation, Transform owner, Transform target, Vector3 inherited = default)
        {
            var m = Instantiate(prefab, at, rotation);
            m.owner = owner;
            m.target = target;
            m.inheritedVelocity = inherited;
            if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);     // (from a prefab kept switched off)
            return m;
        }
    }
}
