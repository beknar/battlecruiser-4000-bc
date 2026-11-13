using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The player's ship: moves under throttle and rudder, points its guns at the current target,
    /// and (when it has them) launches and commands fighters, drones and dropships and fires
    /// missile salvos. Used for both the carrier and the frigate; a ship without bays or a
    /// missile launcher simply leaves those empty. Game code drives it through the public methods.
    /// Being destroyed and coming back, and running into things, each have a file
    /// (CarrierController.Respawn.cs, .Collisions.cs).
    /// </summary>
    public partial class CarrierController : MonoBehaviour, IVelocity
    {
        [Header("Identity")]
        public string shipName = "Carrier";
        public string shipClass = "Fleet carrier";

        [Header("Weapons")]
        public CarrierTurret[] mainTurrets;
        public CarrierTurret[] pointDefense;
        public MissileLauncher missiles;

        [Header("Launch bays")]
        public LaunchBay[] flankBays;
        public LaunchBay ventralHangar;

        [Header("Air group")]
        public Craft fighterPrefab;
        public Craft dronePrefab;
        public Craft dropshipPrefab;
        public int fighterStock = 24;
        public int droneStock = 24;
        public int dropshipStock = 4;

        [Header("Movement")]
        public float maxSpeed = 80f;          // m/s
        public float acceleration = 6f;
        public float turnRate = 3f;           // degrees per second
        [Range(0f, 1f)] public float throttle = 0f;
        [Range(-1f, 1f)] public float rudder = 0f;
        public EngineThrust[] engines;

        [Header("Attitude and elevator thrusters")]
        [Tooltip("How fast the nose pitches up or down (degrees per second).")]
        public float pitchRate = 2f;
        [Tooltip("Steepest nose-up or nose-down angle.")]
        public float maxPitch = 45f;
        [Tooltip("Top speed of the elevator thrusters, straight up or down relative to the deck (m/s).")]
        public float elevatorSpeed = 8f;
        public float elevatorAcceleration = 2f;
        [Tooltip("-1 nose down .. +1 nose up.")]
        [Range(-1f, 1f)] public float pitchInput = 0f;
        [Tooltip("How fast the ship rolls about its length (degrees per second).")]
        public float rollRate = 45f;
        [Tooltip("-1 roll to the left .. +1 to the right.")]
        [Range(-1f, 1f)] public float rollInput = 0f;
        [Tooltip("-1 thrust down .. +1 thrust up.")]
        [Range(-1f, 1f)] public float elevator = 0f;
        [Tooltip("Bring the nose back to level while there's no pitch input.")]
        public bool autoLevel = false;
        [Tooltip("Elevator thrusters under the hull: they fire to push the ship up.")]
        public ThrusterFlare[] liftThrustersUp;
        [Tooltip("Elevator thrusters on top of the hull: they fire to push the ship down.")]
        public ThrusterFlare[] liftThrustersDown;

        [Header("Targeting")]
        public Transform target;

        readonly List<Craft> wing = new List<Craft>();
        float speed, heading, pitchAngle, liftSpeed;
        AudioSource hum;

        public Vector3 Velocity => transform.forward * speed + transform.up * liftSpeed;
        /// <summary>Compass heading in degrees (0 = +Z of the up frame: the world's in space).</summary>
        public float Heading => heading;
        /// <summary>Nose angle above the horizon in degrees (negative = nose down).</summary>
        public float Pitch => pitchAngle;
        /// <summary>Elevator thruster speed along the ship's up axis (m/s).</summary>
        public float LiftSpeed => liftSpeed;
        /// <summary>Rate of climb (m/s, along the up frame's up), from the engines and the elevator thrusters.</summary>
        public float ClimbRate => Vector3.Dot(Velocity, upFrame * Vector3.up);
        public IReadOnlyList<Craft> Wing => wing;
        public float Speed => speed;

        /// <summary>The ship can launch craft (it has bays and something to launch).</summary>
        public bool HasHangar =>
            ((flankBays != null && flankBays.Length > 0) && (fighterPrefab != null || dronePrefab != null)) ||
            (ventralHangar != null && dropshipPrefab != null);
        public bool HasMissiles => missiles != null;

        /// <summary>Names of the guns that can be brought to bear on the target right now,
        /// e.g. "Port" (empty when none can, or there is no target).</summary>
        public string GunsBearing()
        {
            if (target == null || mainTurrets == null) return "";
            var names = new List<string>();
            foreach (var tu in mainTurrets) if (tu && tu.CanBear(target.position) && !names.Contains(tu.Label)) names.Add(tu.Label);
            return string.Join(" + ", names);
        }

        /// <summary>HUD text for which guns bear: names for a few guns, a count for many.</summary>
        public string GunsBearingText()
        {
            if (Destroyed || target == null || mainTurrets == null || mainTurrets.Length == 0) return "";
            if (mainTurrets.Length <= 4)
            {
                string g = GunsBearing();
                if (g.Length == 0)
                    return Cover.Blocked(transform.position, target.position)
                        ? "none: the target is behind cover"
                        : "none: turn to bring a broadside round";
                return $"{g}  ({g.Split('+').Length} of {mainTurrets.Length} guns)";
            }
            int n = 0;
            foreach (var tu in mainTurrets) if (tu && tu.CanBear(target.position)) n++;
            return $"{n} of {mainTurrets.Length} turrets";
        }

        Health health;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        // the place the spawn point is kept relative to (it moves along its orbit), if any
        Transform spawnFrame;
        Vector3 spawnOffset;

        /// <summary>The ship has been destroyed and is waiting to respawn.</summary>
        public bool Destroyed => health != null && health.Dead;
        /// <summary>Seconds until a destroyed ship comes back (0 while alive).</summary>
        public float RespawnIn => Destroyed ? Mathf.Max(0f, health.RespawnAt - Time.time) : 0f;

        void OnEnable() { PlayerShips.Add(transform); OrbitalSystem.Recentred += OriginShifted; }
        void OnDisable() { PlayerShips.Remove(transform); OrbitalSystem.Recentred -= OriginShifted; }

        void Start()
        {
            SetTarget(target);
            hum = SoundFX.EngineHum(transform, 1500f);
            heading = transform.eulerAngles.y;
            pitchAngle = Mathf.Clamp(-Mathf.DeltaAngle(0f, transform.eulerAngles.x), -maxPitch, maxPitch);
            SetSpawn(transform.position, transform.rotation);
            health = GetComponent<Health>();
            FindSystems();
            if (health != null)
            {
                health.onDestroyed.AddListener(OnShipDestroyed);
                health.onRespawned.AddListener(OnShipRespawned);
            }
        }

        // (the last of the settings, so the inspector and the saved prefab keep their order: the
        // collision code that uses it is in CarrierController.Collisions.cs)
        [Header("Collisions")]
        [Tooltip("Damage per m/s of speed into an asteroid or wreck (the shield takes it first).")]
        public float collisionDamage = 6f;
    }
}
