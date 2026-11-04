using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Close-in weapon systems: picks targets for the ship's CIWS mounts on its own, separately
    /// from the ship's main target. A mount only ever engages small craft (drones and fighters),
    /// never a capital ship: each one takes the nearest live drone or fighter it can bring to
    /// bear, re-checked a few times a second, and stows when there is none.
    /// </summary>
    public class CiwsDirector : MonoBehaviour
    {
        public CarrierTurret[] mounts;
        public float retargetInterval = 0.2f;
        public bool active = true;

        float next;

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
                m.target = active ? Pick(m) : null;
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
            return best;
        }
    }
}
