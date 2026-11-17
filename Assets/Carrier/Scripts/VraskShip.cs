using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// One of the Vrask's ships: which of their four it is, and the places on its hull that the
    /// rest of their way of fighting works from (from the model's hardpoints, Data/lizard_frigateN_
    /// hardpoints.json): the Snare's tractor projector and ion tubes, the Fang's breaching prow
    /// and boarding-pod hatches, the Maw's spinal gun, silos and missile cells, the Skink's
    /// point-defence guns. Its shield goes down with it when it is destroyed and comes back when
    /// it does.
    ///
    /// The Vrask are hostile like the pirates (TargetInfo.faction "Hostile": the co-op world, the
    /// CIWS and the wingmen treat them as enemies); TargetInfo.group says "Vrask".
    /// </summary>
    public class VraskShip : MonoBehaviour
    {
        /// <summary>What TargetInfo.group says for them.</summary>
        public const string Faction = "Vrask";

        /// <summary>Snare: capture and disable (ion guns, a tractor). Fang: assault and boarding
        /// (heavy short-range guns, missiles, a ram). Maw: long-range fire support and siege (a
        /// spinal gun; nuclear silos). Skink: interceptor and escort (rapid point-defence guns).</summary>
        public enum Role { Snare, Fang, Maw, Skink }

        public Role role;

        /// <summary>A place on the hull: its name and kind as the hardpoints have them ("tractor",
        /// "ion_muzzle", "breach", "boarding_pod", "spinal_muzzle", "nuke_cell", "missile_cell",
        /// "pd"), facing the way it works, and its size (radius, m).</summary>
        [System.Serializable]
        public struct Point
        {
            public string name, kind;
            public Transform at;
            public float size;
        }

        public Point[] points = new Point[0];

        /// <summary>Every Vrask ship in the scene (kept while a co-op puppet, too).</summary>
        public static readonly List<VraskShip> All = new List<VraskShip>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll() => All.Clear();

        Health health;
        Shield shield;

        void Awake()
        {
            if (!All.Contains(this)) All.Add(this);
            health = GetComponent<Health>();
            shield = GetComponentInChildren<Shield>(true);
        }

        void OnDestroy() => All.Remove(this);

        void Start()
        {
            if (health == null) return;
            health.onDestroyed ??= new UnityEngine.Events.UnityEvent();
            health.onRespawned ??= new UnityEngine.Events.UnityEvent();
            health.onDestroyed.AddListener(ShieldDown);
            health.onRespawned.AddListener(ShieldUp);
        }

        void ShieldDown() { if (shield != null) shield.SetOffline(true); }
        void ShieldUp() { if (shield != null) shield.SetOffline(false); }

        /// <summary>The ship's points of a kind (none: an empty array).</summary>
        public Transform[] PointsOf(string kind)
        {
            var found = new List<Transform>();
            foreach (var p in points) if (p.kind == kind && p.at != null) found.Add(p.at);
            return found.ToArray();
        }

        /// <summary>Its first point of a kind (null if it has none): the Snare's "tractor", the
        /// Fang's "breach", the Maw's "spinal_muzzle".</summary>
        public Transform PointOf(string kind)
        {
            foreach (var p in points) if (p.kind == kind && p.at != null) return p.at;
            return null;
        }

        /// <summary>Its pilot is alive and out of hiding.</summary>
        public bool Fighting
        {
            get
            {
                var pilot = GetComponent<CombatPilot>();
                return pilot != null && pilot.Alive && pilot.Current != CombatPilot.State.Lurk;
            }
        }
    }
}
