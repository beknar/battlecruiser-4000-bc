using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A targeting laser (a Grey scout's, an Imperial recon frigate's) from its "designator"
    /// point: while it has a target within `range` and in sight, it paints it for its faction
    /// (Designation), drawn as a thin line. Its tactics give it the target; alone it paints its
    /// pilot's. In co-op only the host's paints; the others are told (CoopEntity.pt) and mark the
    /// paint themselves (CoopWorld.ShowPaint).
    /// </summary>
    public class Designator : MonoBehaviour
    {
        public float range = 15000f;
        [Tooltip("Where the laser comes from (its designator point; the ship if none).")]
        public Transform emitter;
        public LineRenderer line;
        [Tooltip("What it paints (its tactics set it; none: its pilot's target).")]
        public Transform target;

        /// <summary>What it is painting now (null: nothing).</summary>
        public Transform Painting { get; private set; }

        string faction;
        CombatPilot pilot;
        Health health;

        void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            health = GetComponent<Health>();
            if (emitter == null) emitter = transform;
        }

        void Start() => faction = FactionShip.Of(transform)?.FactionName ?? "";

        void OnDisable() { Painting = null; if (line != null) line.enabled = false; }

        void Update()
        {
            Transform t = target != null ? target : pilot != null ? pilot.target : null;
            bool alive = health == null || !health.Dead;
            bool on = alive && t != null && t.gameObject.activeInHierarchy && (t.position - emitter.position).sqrMagnitude <= range * range
                      && !Cover.Blocked(emitter.position, t.position) && !ShipSystems.WeaponsDownOn(this, ref systems);
            Painting = on ? t : null;
            if (on) Designation.Mark(transform, t, faction);
            if (line != null)
            {
                line.enabled = on;
                if (on) { line.SetPosition(0, emitter.position); line.SetPosition(1, t.position); }
            }
        }

        ShipSystems systems;
    }
}
