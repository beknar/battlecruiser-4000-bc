using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Close-in weapon systems: picks targets for the ship's CIWS mounts on its own, separately
    /// from the ship's main target. A mount only ever engages small craft (drones and fighters)
    /// and hostile missiles and boarding pods in flight, never a capital ship: each one takes the
    /// nearest it can bring to bear (a missile or pod first: counted at half its distance),
    /// re-checked a few times a second, and stows when there is none. Its bolts shoot a missile
    /// or pod down (Missile.toughness).
    /// </summary>
    public class CiwsDirector : MonoBehaviour
    {
        public CarrierTurret[] mounts;
        public float retargetInterval = 0.2f;
        public bool active = true;

        float next;
        ShipSystems systems;

        /// <summary>The craft each mount is shooting at (null = standing by), for the HUD.</summary>
        public Transform EngagedTarget
        {
            get
            {
                if (mounts == null) return null;
                foreach (var m in mounts) if (m && m.target) return m.target;
                return null;
            }
        }

        void Update()
        {
            if (mounts == null || Time.time < next) return;
            next = Time.time + retargetInterval;
            foreach (var m in mounts)
            {
                if (!m) continue;
                m.target = active && !ShipSystems.WeaponsDownOn(this, ref systems) ? Pick(m) : null;   // (none with the weapons knocked out)
            }
        }

        static Transform Pick(CarrierTurret mount)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var info in TargetInfo.All)
            {
                if (info == null || !info.IsSmallCraft || !info.Hostile) continue;
                var h = info.GetComponent<Health>();
                if (h != null && h.Dead) continue;
                Vector3 p = info.transform.position;
                float d = (p - mount.transform.position).sqrMagnitude;
                if (d >= bestDist || !mount.CanBear(p)) continue;
                bestDist = d;
                best = info.transform;
            }
            // hostile missiles and boarding pods in flight (on their way in: first)
            foreach (var m in Missile.Active)
            {
                if (m == null || !m.Hostile) continue;
                Vector3 p = m.transform.position;
                float d = (p - mount.transform.position).sqrMagnitude * 0.25f;
                if (d >= bestDist || !mount.CanBear(p)) continue;
                bestDist = d;
                best = m.transform;
            }
            return best;
        }
    }
}
