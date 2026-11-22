using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A warp inhibitor (the Imperial patrol and recon frigates'): within `radius` of it a ship
    /// can't warp ("Warp inhibited by ..."), as the warp drive checks (WarpDrive.CanWarp: docking,
    /// landing and cruise aren't stopped). Off
    /// while its ship is destroyed. Kept on in a co-op guest's game, so each player's drive sees
    /// the host's ships' inhibitors.
    /// </summary>
    public class Interdictor : MonoBehaviour
    {
        public float radius = 12000f;

        static readonly List<Interdictor> all = new List<Interdictor>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => all.Clear();

        Health health;
        TargetInfo info;

        void Awake() { health = GetComponent<Health>(); info = GetComponent<TargetInfo>(); }
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>Its name as the warp drive says it (the players' sensors may not have named it).</summary>
        public string Name => Sensors.NameOf(transform, info != null ? info.Name : name);

        /// <summary>The inhibitor holding a point in its field, if any.</summary>
        public static Interdictor At(Vector3 point)
        {
            foreach (var i in all)
            {
                if (i == null || (i.health != null && i.health.Dead)) continue;
                if ((i.transform.position - point).sqrMagnitude <= i.radius * i.radius) return i;
            }
            return null;
        }
    }
}
