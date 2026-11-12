using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Something built in the star system: a space station, an installation, an orbital weapons
    /// battery, an asteroid base, a base on a planet's surface, or an installation at a Lagrange
    /// point. A friendly one with a berth can be docked at (the frigate is repaired there, and
    /// where there is a market its ore is sold). A hostile one is defended by a DefenseGrid.
    /// </summary>
    public class Installation : MonoBehaviour
    {
        public string displayName = "Installation";
        public WarpPoint.Kind kind = WarpPoint.Kind.Installation;
        public bool hostile;
        [Tooltip("Ships can dock here (friendly only).")]
        public bool canDock;
        [Tooltip("Buys ore (when docked, the hold is sold).")]
        public bool market;
        [Tooltip("Where a docked ship sits; its forward is the way out.")]
        public Transform berth;
        // kept short: from farther out, K lands on the body nearby
        [Tooltip("Docking is possible within this distance of the berth (m).")]
        public float dockRange = 1600f;

        public static readonly List<Installation> All = new List<Installation>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }

        // its crew (CrewTable): none on an automated one
        void Awake()
        {
            if (GetComponent<Crew>() != null) return;
            var table = CrewTable.Get();
            int n = table.CrewOf(displayName);
            if (n <= 0) return;
            var c = gameObject.AddComponent<Crew>();
            c.complement = n;
            var place = table.Place(displayName);
            c.reinforcePerMinute = hostile && place != null ? place.refillPerMinute : 0f;
        }
        void OnDisable() => All.Remove(this);

        public Vector3 BerthPosition => berth != null ? berth.position : transform.position;

        /// <summary>The nearest friendly place to dock within its docking range of a point, or null.</summary>
        public static Installation DockableNear(Vector3 p)
        {
            Installation best = null;
            float bestD = float.MaxValue;
            foreach (var s in All)
            {
                if (s == null || !s.canDock || s.hostile) continue;
                float d = Vector3.Distance(p, s.BerthPosition);
                if (d < s.dockRange && d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
