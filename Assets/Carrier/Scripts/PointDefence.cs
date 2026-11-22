using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A faction ship's point defence (the Vrask Skink's rapid guns): the mirror of the players' CIWS
    /// (CiwsDirector). Every few tenths of a second each of its mounts takes the nearest thing of
    /// the players' it can bring to bear that threatens the Vrask: a missile of theirs on its way
    /// to a Vrask ship (first: counted at a quarter of its distance), then a fighter or drone of
    /// a players' carrier. While a mount has one, the ship's EnemyWeapons leave it alone
    /// (EnemyWeapons.claimed); with none, the mount goes back to the ship's own target. Its shots
    /// meet the players' missiles in flight (Projectile, Missile.InPath), `missileScale` times as
    /// hard as on a hull, so a needle does to a missile about what a CIWS bolt does. Nothing while
    /// its weapons are cold or knocked out, or it is destroyed.
    /// </summary>
    public class PointDefence : MonoBehaviour
    {
        public CarrierTurret[] mounts = new CarrierTurret[0];
        public float retargetInterval = 0.2f;
        [Tooltip("Its shots are this many times as hard on a missile as their damage.")]
        public float missileScale = 2.7f;

        /// <summary>A mount turning to a missile or a craft (counted each time), what the last
        /// one took, and the missiles its shots have brought down.</summary>
        public int Engagements { get; private set; }
        public Transform EngagedTarget { get; private set; }
        public int ShotDown { get; private set; }

        readonly Dictionary<CarrierTurret, Transform> on = new Dictionary<CarrierTurret, Transform>();
        EnemyWeapons weapons;
        Health health;
        ShipSystems systems;
        float next;

        void Awake()
        {
            weapons = GetComponent<EnemyWeapons>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (weapons != null) weapons.claimed = Claimed;
            Missile.Downed += OnDowned;
        }

        void OnDisable()
        {
            if (weapons != null && weapons.claimed == (System.Func<CarrierTurret, bool>)Claimed) weapons.claimed = null;
            Missile.Downed -= OnDowned;
            foreach (var kv in on) if (kv.Key != null && kv.Value != null && kv.Key.target == kv.Value) kv.Key.target = null;
            on.Clear();
            EngagedTarget = null;
        }

        void OnDowned(Missile m, Transform by)
        {
            if (by != null && by.root == transform.root) ShotDown++;
        }

        /// <summary>A mount it is aiming now (EnemyWeapons leaves it alone).</summary>
        bool Claimed(CarrierTurret t) => t != null && on.TryGetValue(t, out var x) && x != null;

        void Update()
        {
            if (mounts == null || Time.time < next) return;
            next = Time.time + retargetInterval;
            bool armed = (health == null || !health.Dead) && (weapons == null || weapons.weaponsFree) && !ShipSystems.WeaponsDownOn(this, ref systems);
            foreach (var m in mounts)
            {
                if (!m) continue;
                var pick = armed ? Pick(m.transform.position, m.CanBear, Faction) : null;
                on.TryGetValue(m, out var was);
                if (pick != null)
                {
                    if (pick != was) { Engagements++; EngagedTarget = pick; }
                    on[m] = pick;
                    m.target = pick;
                }
                else if (was != null || on.ContainsKey(m))
                {
                    on[m] = null;
                    if (m.target == was) m.target = null;   // (its ship's guns hand it back their target)
                }
            }
        }

        /// <summary>For a mount at `from` (`bears`: can it be brought to bear on a point): the
        /// players' missile on its way to a Vrask ship, or their fighter or drone, it should take
        /// (a missile first: at a quarter of its distance), or null.</summary>
        public static Transform Pick(Vector3 from, System.Func<Vector3, bool> bears, string faction = null)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var m in Missile.Active)
            {
                if (m == null || m.Hostile || m.owner == null || !AtFaction(m, faction)) continue;
                Vector3 p = m.transform.position;
                float d = (p - from).sqrMagnitude * 0.25f;
                if (d >= bestD || !bears(p)) continue;
                bestD = d;
                best = m.transform;
            }
            foreach (var s in PlayerShips.All)
            {
                var cc = s != null ? s.GetComponent<CarrierController>() : null;
                if (cc == null) continue;
                foreach (var c in cc.Wing)
                {
                    if (c == null || !c.isActiveAndEnabled || c.kind == Craft.Kind.Dropship) continue;
                    Vector3 p = c.transform.position;
                    float d = (p - from).sqrMagnitude;
                    if (d >= bestD || !bears(p)) continue;
                    bestD = d;
                    best = c.transform;
                }
            }
            return best;
        }

        /// <summary>A missile on its way to a Vrask ship.</summary>
        public static bool AtVrask(Missile m) => AtFaction(m, VraskShip.Faction);

        /// <summary>A missile on its way to a ship of `faction` (null: the Vrask's).</summary>
        public static bool AtFaction(Missile m, string faction)
        {
            var f = m != null ? FactionShip.Of(m.target) : null;
            return f != null && f.FactionName == (string.IsNullOrEmpty(faction) ? VraskShip.Faction : faction);
        }

        string faction;
        /// <summary>The faction whose ships it protects: its own ship's (the Vrask's if it has none).</summary>
        string Faction => faction ??= FactionShip.Of(transform)?.FactionName ?? VraskShip.Faction;

        /// <summary>What a hostile ship's shot does to the players' missiles, as a share of its
        /// damage: its point defence's `missileScale`, or nothing (a hostile gun that isn't point
        /// defence doesn't shoot missiles down).</summary>
        public static float MissileScale(Transform owner)
        {
            var pd = owner != null ? owner.GetComponentInParent<PointDefence>() : null;
            return pd != null && pd.isActiveAndEnabled ? pd.missileScale : 0f;
        }
    }
}
