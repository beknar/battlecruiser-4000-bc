using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>One of the ground forces: a target for the player, remembered when destroyed.</summary>
    public class GroundUnit : MonoBehaviour
    {
        public long key;
        public GroundGarrison garrison;
        public PlanetSurface surface;

        Health health;
        EnemyWeapons weapons;
        CombatPilot pilot;
        PadLaunch launch;
        GroundVehicle vehicle;
        int hitsSeen;

        public bool Alive => health == null || !health.Dead;
        public bool WasHit => health != null && health.Hits > hitsSeen;
        /// <summary>Where it looks out from (above its own hull, so the ground it stands on doesn't block the view).</summary>
        public Vector3 Eye => transform.position + Vector3.up * 12f;

        void Start()
        {
            health = GetComponent<Health>();
            weapons = GetComponent<EnemyWeapons>();
            pilot = GetComponent<CombatPilot>();
            launch = GetComponent<PadLaunch>();
            vehicle = GetComponent<GroundVehicle>();
            if (health != null)
            {
                hitsSeen = health.Hits;
                health.onDestroyed.AddListener(() => { if (surface != null) surface.Destroyed.Add(key); });
            }
            Demo.CarrierDemoInput.AddTarget(transform);
        }

        void OnDestroy() => Demo.CarrierDemoInput.RemoveTarget(transform);

        public void Engage()
        {
            if (!Alive) return;
            if (weapons != null) { weapons.weaponsFree = true; weapons.OpenFireSoon(1.2f); }
            if (launch != null && !launch.Launched) launch.Launch();
            else if (pilot != null && pilot.enabled) pilot.Engage();
            if (vehicle != null) vehicle.engaged = true;
        }

        public void StandDown()
        {
            if (health != null) hitsSeen = health.Hits;
            if (weapons != null) weapons.weaponsFree = false;
            if (pilot != null && pilot.enabled) pilot.Disengage();
            if (vehicle != null) vehicle.engaged = false;
        }
    }
}
