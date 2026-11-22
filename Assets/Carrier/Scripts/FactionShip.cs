using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A ship of one of the alien and foreign factions (the Vrask, the Greys, the Empire), built
    /// from the faction's file (FactionTable): which faction it is, which of its ships (its role),
    /// and the places on its hull that the rest of its way of fighting works from (from the
    /// model's hardpoints: a tractor projector, boarding-pod hatches, a spinal gun, silos, point
    /// defence, a designator, a jammer...). Its shield goes down with it when it is destroyed and
    /// comes back when it does; its armour, if it has some, wears as plates that blow off
    /// (AblativePlates).
    ///
    /// Every faction is hostile, as the pirates are (TargetInfo.faction "Hostile"); TargetInfo.group
    /// says which faction. Each faction derives its own (VraskShip), with its roles as an enum.
    /// </summary>
    public abstract class FactionShip : MonoBehaviour
    {
        /// <summary>A place on the hull: its name and kind as the hardpoints have them ("tractor",
        /// "ion_muzzle", "breach", "boarding_pod", "spinal_muzzle", "nuke_cell", "missile_cell",
        /// "pd", ...), facing the way it works, and its size (radius, m).</summary>
        [System.Serializable]
        public struct Point
        {
            public string name, kind;
            public Transform at;
            public float size;
        }

        public Point[] points = new Point[0];

        [Tooltip("How far (m) its own sensors reach, beyond what any of them sees (a Skink's: the group's eyes); 0: no further.")]
        public float sensorRange;

        /// <summary>Its faction's name, as TargetInfo.group has it ("Vrask").</summary>
        public abstract string FactionName { get; }
        /// <summary>Its role's name ("Snare", "Fang"...).</summary>
        public abstract string RoleName { get; }
        /// <summary>One letter for its role, on the CIC's tactical display.</summary>
        public virtual char RoleLetter => string.IsNullOrEmpty(RoleName) ? '?' : RoleName[0];
        /// <summary>How many ablative plates its hull wears (its armour, seen); 0: none.</summary>
        public virtual int PlateCount => 0;
        /// <summary>How much a section of the players' frigates wants it dead, at `distance` m (the
        /// battle simulator's targeting: the Vrask weigh theirs in BattleSim).</summary>
        public virtual float SectionWeight(float distance) => 1.5f;

        /// <summary>Every faction ship in the scene (kept while a co-op puppet, too).</summary>
        public static readonly List<FactionShip> AllShips = new List<FactionShip>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAllShips() => AllShips.Clear();

        protected Health health;
        protected Shield shield;

        protected virtual void Awake()
        {
            if (!AllShips.Contains(this)) AllShips.Add(this);
            health = GetComponent<Health>();
            shield = GetComponentInChildren<Shield>(true);
        }

        protected virtual void OnDestroy() => AllShips.Remove(this);

        protected virtual void Start()
        {
            // its armour, seen: plates that blow off as it wears
            int plates = PlateCount;
            if (plates > 0 && GetComponent<Armor>() != null && GetComponent<AblativePlates>() == null)
                gameObject.AddComponent<AblativePlates>().count = plates;
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

        /// <summary>The faction ship a transform is part of (null: none).</summary>
        public static FactionShip Of(Transform t) => t != null ? t.GetComponentInParent<FactionShip>() : null;

        /// <summary>Two transforms are ships of the same faction.</summary>
        public static bool SameFaction(Transform a, Transform b)
        {
            var fa = Of(a);
            var fb = Of(b);
            return fa != null && fb != null && fa.FactionName == fb.FactionName;
        }
    }
}
