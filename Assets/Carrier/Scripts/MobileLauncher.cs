using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A mobile missile launcher: a truck (GroundVehicle) with a box of missile tubes on a
    /// turntable. On patrol it drives with its launcher down. Once its garrison is alerted it
    /// drives toward the player's ship; with the ship in missile range it stops, turns its
    /// turntable to the ship and raises the launcher, fires its salvoes (EnemyWeapons, on cue),
    /// lowers it and drives on a while before stopping again. Built by GroundForces; its numbers
    /// are in Resources/Data/ground_units.json.
    /// </summary>
    public class MobileLauncher : MonoBehaviour
    {
        public enum Phase { Driving, Stopping, Raising, Firing, Lowering }

        public GroundVehicle vehicle;
        public EnemyWeapons weapons;
        [Tooltip("The hinge the box of tubes tilts up on (its forward is level when down).")]
        public Transform launcher;
        public float raisedAngle = 55f;
        [Tooltip("Seconds to raise the launcher (and to lower it again).")]
        public float raiseTime = 3f;
        [Tooltip("Seconds it drives between stops.")]
        public float driveTime = 10f;
        [Tooltip("Seconds between salvoes from one stop.")]
        public float salvoInterval = 6f;
        public int salvosPerStop = 2;

        public Phase Current { get; private set; } = Phase.Driving;
        /// <summary>Salvoes fired, and stops made to fire them (for the self-test).</summary>
        public int Salvos { get; private set; }
        public int Stops { get; private set; }
        /// <summary>How far up the launcher is: 0 down, 1 raised.</summary>
        public float Raised => up;

        float up, driveUntil, nextSalvo, raisedAt, lastSalvo;
        int salvosHere;
        Health health;

        void Start()
        {
            health = GetComponent<Health>();
            if (vehicle == null) vehicle = GetComponent<GroundVehicle>();
            if (weapons == null) weapons = GetComponent<EnemyWeapons>();
            driveUntil = Time.time + Random.Range(2f, 5f);
        }

        void Update()
        {
            if (vehicle == null || weapons == null || (health != null && health.Dead)) return;
            float now = Time.time, dt = Time.deltaTime;
            bool engaged = vehicle.engaged && weapons.weaponsFree;
            var t = weapons.target;
            bool inRange = t != null && (t.position - transform.position).sqrMagnitude < weapons.missileRange * weapons.missileRange;

            if (!engaged && Current != Phase.Driving && Current != Phase.Lowering) Current = Phase.Lowering;
            switch (Current)
            {
                case Phase.Driving:
                    vehicle.halted = false;
                    vehicle.turretTracks = false;
                    if (engaged && inRange && now >= driveUntil) { Current = Phase.Stopping; Stops++; }
                    break;
                case Phase.Stopping:
                    vehicle.halted = true;
                    if (vehicle.Speed < 0.5f) Current = Phase.Raising;
                    break;
                case Phase.Raising:
                    vehicle.halted = true;
                    vehicle.turretTracks = true;
                    up = Mathf.MoveTowards(up, 1f, dt / Mathf.Max(0.1f, raiseTime));
                    if (up >= 1f)
                    {
                        Current = Phase.Firing;
                        raisedAt = now;
                        nextSalvo = now + 0.5f;
                        salvosHere = 0;
                    }
                    break;
                case Phase.Firing:
                    vehicle.halted = true;
                    vehicle.turretTracks = true;
                    if (salvosHere < salvosPerStop && now >= nextSalvo)
                    {
                        if (inRange && weapons.HasLineOfSight)
                        {
                            weapons.FireSalvoNow();
                            Salvos++;
                            salvosHere++;
                            lastSalvo = now;
                            nextSalvo = now + salvoInterval;
                        }
                        else if (now > raisedAt + salvoInterval * Mathf.Max(1, salvosPerStop))
                        {
                            // nothing to shoot at from here: move on
                            salvosHere = salvosPerStop;
                            lastSalvo = now - 10f;
                        }
                    }
                    // (down again once the last missile is out of its tube)
                    if (salvosHere >= salvosPerStop && now >= lastSalvo + 0.3f * weapons.salvoSize + 0.5f) Current = Phase.Lowering;
                    break;
                case Phase.Lowering:
                    vehicle.halted = true;
                    vehicle.turretTracks = false;
                    up = Mathf.MoveTowards(up, 0f, dt / Mathf.Max(0.1f, raiseTime));
                    if (up <= 0f)
                    {
                        Current = Phase.Driving;
                        vehicle.halted = false;
                        driveUntil = now + driveTime;
                    }
                    break;
            }
            if (launcher != null) launcher.localRotation = Quaternion.Euler(-raisedAngle * Mathf.SmoothStep(0f, 1f, up), 0f, 0f);
        }
    }
}
