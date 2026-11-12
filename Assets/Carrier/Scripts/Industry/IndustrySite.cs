using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A port in the level: an industrial site (IndustryTable sites, by name: a refinery, a
    /// factory or both, with storage), a place that recruits crew (CrewTable), or both. As an
    /// industrial site it is a refinery, a factory or both, with storage. In space it is an Installation the frigate docks at; on a planet's or
    /// moon's surface (and on the asteroid) it is round a landing pad (`pad`, `padRadius`), and
    /// the frigate sets down on that. Its storage and queues are in IndustryLedger.
    /// </summary>
    public class IndustrySite : MonoBehaviour
    {
        [Tooltip("The site's name in industry.json.")]
        public string siteName;
        [Tooltip("The landing pad's centre (on the ground), if it has one.")]
        public Transform pad;
        public float padRadius = 140f;

        public static readonly List<IndustrySite> All = new List<IndustrySite>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public SiteSpec Spec => IndustryTable.Get().Site(siteName);
        /// <summary>It recruits crew (CrewTable).</summary>
        public bool Recruits => CrewTable.Get().Place(siteName)?.Recruits ?? false;
        public SiteState State => IndustryLedger.Site(siteName);

        /// <summary>What it does, for the HUD and the map: "refinery and factory".</summary>
        public static string Services(SiteSpec s)
        {
            if (s == null) return "";
            return s.CanRefine && s.CanBuild ? "refinery and factory" : s.CanRefine ? "refinery" : s.CanBuild ? "factory" : "storage";
        }

        /// <summary>The site at an installation (docked there), or null.</summary>
        public static IndustrySite At(Installation inst)
        {
            if (inst == null) return null;
            return inst.GetComponentInParent<IndustrySite>() ?? inst.GetComponentInChildren<IndustrySite>();
        }

        /// <summary>The site whose pad a ship at `p` is over (within its radius across, and not
        /// far above it), or null.</summary>
        public static IndustrySite PadUnder(Vector3 p, float above = 60f)
        {
            foreach (var s in All)
            {
                if (s == null || s.pad == null) continue;
                Vector3 d = p - s.pad.position;
                Vector3 up = s.pad.up;
                float h = Vector3.Dot(d, up);
                Vector3 across = d - up * h;
                if (across.magnitude <= s.padRadius && h > -20f && h < above) return s;
            }
            return null;
        }
    }
}
