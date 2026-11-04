using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The player's ship: moves under throttle and rudder, points its guns at the current target,
    /// and (when it has them) launches and commands fighters, drones and dropships and fires
    /// missile salvos. Used for both the carrier and the frigate; a ship without bays or a
    /// missile launcher simply leaves those empty. Game code drives it through the public methods.
    /// </summary>
    public class CarrierController : MonoBehaviour, IVelocity
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
        /// <summary>Compass heading in degrees (0 = world +Z).</summary>
        public float Heading => heading;
        /// <summary>Nose angle above the horizon in degrees (negative = nose down).</summary>
        public float Pitch => pitchAngle;
        /// <summary>Elevator thruster speed along the ship's up axis (m/s).</summary>
        public float LiftSpeed => liftSpeed;
        /// <summary>Rate of climb in world terms (m/s), from the engines and the elevator thrusters.</summary>
        public float ClimbRate => Velocity.y;
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

        /// <summary>The ship has been destroyed and is waiting to respawn.</summary>
        public bool Destroyed => health != null && health.Dead;
        /// <summary>Seconds until a destroyed ship comes back (0 while alive).</summary>
        public float RespawnIn => Destroyed ? Mathf.Max(0f, health.RespawnAt - Time.time) : 0f;

        void Start()
        {
            SetTarget(target);
            hum = SoundFX.EngineHum(transform, 1500f);
            heading = transform.eulerAngles.y;
            pitchAngle = Mathf.Clamp(-Mathf.DeltaAngle(0f, transform.eulerAngles.x), -maxPitch, maxPitch);
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            health = GetComponent<Health>();
            FindSystems();
            if (health != null)
            {
                health.onDestroyed.AddListener(OnShipDestroyed);
                health.onRespawned.AddListener(OnShipRespawned);
            }
        }

        // ------------------------------------------------------------ destruction and respawn

        /// <summary>Everything aboard shuts down: guns, CIWS, the bow gun, the shield and the
        /// engines; the hull stops being solid so the wreckage isn't shot at or hit.</summary>
        void OnShipDestroyed()
        {
            SetSystems(false);
            throttle = 0f;
            speed = liftSpeed = 0f;   // the wreckage has already taken the ship's motion with it
            rudder = pitchInput = elevator = 0f;
        }

        /// <summary>Back at the starting point, whole, at rest, with everything working.</summary>
        void OnShipRespawned()
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            heading = spawnRotation.eulerAngles.y;
            pitchAngle = 0f;
            speed = liftSpeed = 0f;
            throttle = 0f;
            autoLevel = false;
            SetSystems(true);
        }

        // found once at the start: when the ship comes apart its gun mounts fly off as wreckage
        // and are no longer under it
        CarrierTurret[] allTurrets;
        MainBeam[] allBeams;
        Collider[] hullColliders;
        Shield shieldComp;
        ThrusterFlare[] allFlares;

        void FindSystems()
        {
            if (allTurrets != null) return;
            allTurrets = GetComponentsInChildren<CarrierTurret>(true);
            allBeams = GetComponentsInChildren<MainBeam>(true);
            hullColliders = System.Array.FindAll(GetComponentsInChildren<Collider>(true), c => c.GetComponent<Shield>() == null);
            shieldComp = GetComponentInChildren<Shield>(true);
            allFlares = GetComponentsInChildren<ThrusterFlare>(true);
        }

        void SetSystems(bool on)
        {
            FindSystems();
            foreach (var tu in allTurrets) if (tu) { if (!on) tu.target = null; tu.enabled = on; }
            if (on) SetTarget(target);
            var ciws = GetComponent<CiwsDirector>();
            if (ciws != null) ciws.active = on;
            foreach (var beam in allBeams)
            {
                if (!beam) continue;
                if (!on) beam.Abort();
                beam.enabled = on;
            }
            if (shieldComp != null) shieldComp.SetOffline(!on);
            foreach (var c in hullColliders) if (c) c.enabled = on;
            if (engines != null)
                foreach (var e in engines)
                {
                    if (!e) continue;
                    e.enabled = on;
                    foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
                    if (e.glow) e.glow.enabled = on;
                }
            foreach (var f in allFlares) if (f) { f.thrust = 0f; f.Apply(0f); f.enabled = on; }
            if (hum != null) hum.volume = on ? hum.volume : 0f;
        }

        /// <summary>Start bringing the nose back to level (stops as soon as the pilot pitches).</summary>
        public void LevelOff() => autoLevel = true;

        void Update()
        {
            float dt = Time.deltaTime;
            if (Destroyed)
            {
                // the wreckage flies on its own (ShipBreakup); the ship itself waits to respawn
                if (hum != null) hum.volume = 0f;
                return;
            }
            speed = Mathf.MoveTowards(speed, throttle * maxSpeed, acceleration * dt);

            // attitude: heading turns about the world vertical, the nose pitches up or down; no roll
            heading = Mathf.Repeat(heading + rudder * turnRate * dt, 360f);
            if (Mathf.Abs(pitchInput) > 0.01f)
            {
                autoLevel = false;
                pitchAngle = Mathf.Clamp(pitchAngle + pitchInput * pitchRate * dt, -maxPitch, maxPitch);
            }
            else if (autoLevel)
            {
                pitchAngle = Mathf.MoveTowards(pitchAngle, 0f, pitchRate * dt);
                if (Mathf.Abs(pitchAngle) < 0.01f) autoLevel = false;
            }
            transform.rotation = Quaternion.Euler(-pitchAngle, heading, 0f);

            // elevator thrusters push straight up or down relative to the deck
            float wantLift = elevator * elevatorSpeed;
            float liftChange = wantLift - liftSpeed;
            liftSpeed = Mathf.MoveTowards(liftSpeed, wantLift, elevatorAcceleration * dt);
            transform.position += Collide((transform.forward * speed + transform.up * liftSpeed) * dt);
            UpdateLiftFlares(liftChange);
            if (engines != null) foreach (var e in engines) if (e) e.throttle = Mathf.Max(0.15f, throttle);
            wing.RemoveAll(c => c == null);
            if (hum != null)
            {
                float f = Mathf.InverseLerp(0f, maxSpeed, speed);
                hum.volume = 0.2f + 0.4f * Mathf.Max(throttle, f);
                hum.pitch = 0.8f + 0.45f * f;
            }
        }

        // ------------------------------------------------------------ collisions with asteroids and wrecks

        [Header("Collisions")]
        [Tooltip("Damage per m/s of speed into an asteroid or wreck (the shield takes it first).")]
        public float collisionDamage = 6f;

        /// <summary>Times the ship has run into an asteroid or wreck.</summary>
        public int Collisions { get; private set; }
        public float LastCollisionTime { get; private set; } = -100f;

        const float Skin = 2f;
        Vector3 capCenter;
        float capHalf = -1f, capRadius;

        /// <summary>A capsule along the hull, from the hull collider's size.</summary>
        void EnsureCapsule()
        {
            if (capHalf >= 0f) return;
            FindSystems();
            var b = new Bounds(Vector3.zero, Vector3.one * 20f);
            bool any = false;
            foreach (var c in hullColliders)
            {
                if (!(c is MeshCollider mc) || mc.sharedMesh == null) continue;
                var mb = mc.sharedMesh.bounds;
                var s = mc.transform.lossyScale;
                var center = transform.InverseTransformPoint(mc.transform.TransformPoint(mb.center));
                var size = Vector3.Scale(mb.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                var lb = new Bounds(center, size);
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            capCenter = b.center;
            capRadius = Mathf.Max(5f, Mathf.Max(b.extents.x, b.extents.y) * 0.8f);
            capHalf = Mathf.Max(0f, b.extents.z - capRadius);
        }

        /// <summary>Limit a move so the hull doesn't pass into an obstacle: stop at the surface and
        /// slide along it, losing speed (and taking a knock) in proportion to how hard it hit.</summary>
        Vector3 Collide(Vector3 move)
        {
            if (!Cover.Active || move.sqrMagnitude < 1e-8f) return move;
            EnsureCapsule();
            Vector3 c = transform.TransformPoint(capCenter), f = transform.forward * capHalf;
            float dist = move.magnitude;
            Vector3 dir = move / dist;
            if (!Physics.CapsuleCast(c + f, c - f, capRadius, dir, out var hit, dist + Skin, Cover.Mask, QueryTriggerInteraction.Ignore))
                return move;
            float allowed = Mathf.Max(0f, hit.distance - Skin);
            Vector3 slide = Vector3.ProjectOnPlane(move - dir * allowed, hit.normal);
            float impact = Vector3.Dot(Velocity, -hit.normal);
            if (impact > 3f)
            {
                speed *= Mathf.Clamp01(1f - 0.6f * impact / Mathf.Max(1f, Velocity.magnitude));
                if (Vector3.Dot(transform.up * liftSpeed, hit.normal) < 0f) liftSpeed = 0f;
                if (Time.time - LastCollisionTime > 0.5f)
                {
                    Collisions++;
                    if (health != null) health.Damage(impact * collisionDamage, hit.point, HitKind.Cannon);
                    SoundFX.Play(Sfx.Impact, hit.point, 1f, 0.6f);
                }
                LastCollisionTime = Time.time;
            }
            Vector3 moved = dir * allowed;
            if (slide.sqrMagnitude > 1e-8f &&
                Physics.CapsuleCast(c + f + moved, c - f + moved, capRadius, slide.normalized, out var h2, slide.magnitude + Skin, Cover.Mask, QueryTriggerInteraction.Ignore))
                slide = slide.normalized * Mathf.Max(0f, h2.distance - Skin);
            return moved + slide;
        }

        /// <summary>Clearance from the hull to the nearest obstacle surface below `range` (for the reel log).</summary>
        public float ObstacleClearance(float range)
        {
            if (!Cover.Active) return range;
            EnsureCapsule();
            Vector3 c = transform.TransformPoint(capCenter), f = transform.forward * capHalf;
            float best = range;
            foreach (var d in new[] { Vector3.down, Vector3.up, transform.right, -transform.right, transform.forward })
                if (Physics.CapsuleCast(c + f, c - f, capRadius, d, out var hit, range, Cover.Mask, QueryTriggerInteraction.Ignore))
                    best = Mathf.Min(best, hit.distance);
            return best;
        }

        /// <summary>Light the elevator thrusters that are pushing: the ventral ones to go up (or to
        /// stop a descent), the dorsal ones to go down (or to stop a climb). Full flare while
        /// changing speed, a steady smaller one while holding the thrusters on.</summary>
        void UpdateLiftFlares(float liftChange)
        {
            float push = Mathf.Clamp(liftChange / Mathf.Max(0.1f, elevatorSpeed * 0.25f), -1f, 1f);
            float up = Mathf.Max(push, 0f), down = Mathf.Max(-push, 0f);
            if (elevator > 0f) up = Mathf.Max(up, 0.35f * elevator);
            if (elevator < 0f) down = Mathf.Max(down, -0.35f * elevator);
            if (Mathf.Abs(liftChange) < 0.05f && Mathf.Abs(elevator) < 0.01f) up = down = 0f;
            if (liftThrustersUp != null) foreach (var f in liftThrustersUp) if (f) f.thrust = up;
            if (liftThrustersDown != null) foreach (var f in liftThrustersDown) if (f) f.thrust = down;
        }

        /// <summary>Point every gun (and later launches) at this target. Null stops targeting:
        /// the guns stow and craft attacking the old target fall back to escort.</summary>
        public void SetTarget(Transform t)
        {
            var old = target;
            target = t;
            if (mainTurrets != null) foreach (var tu in mainTurrets) if (tu) tu.target = t;
            if (pointDefense != null) foreach (var tu in pointDefense) if (tu) tu.target = t;
            if (t == null && old != null)
                foreach (var c in wing)
                    if (c != null && c.order == Craft.Order.Attack && c.target == old) c.Command(Craft.Order.Escort);
        }

        public void LaunchFighters(int perBay = 2) => LaunchFromFlanks(fighterPrefab, ref fighterStock, perBay);
        public void LaunchDrones(int perBay = 2) => LaunchFromFlanks(dronePrefab, ref droneStock, perBay);

        void LaunchFromFlanks(Craft prefab, ref int stock, int perBay)
        {
            if (prefab == null || flankBays == null) return;
            foreach (var bay in flankBays)
            {
                int n = Mathf.Min(perBay, stock);
                if (n <= 0 || bay == null || bay.Busy) continue;
                stock -= n;
                bay.Launch(prefab, n, this, target);
            }
        }

        public void LaunchDropships(int count = 2)
        {
            int n = Mathf.Min(count, dropshipStock);
            if (n <= 0 || ventralHangar == null || dropshipPrefab == null || ventralHangar.Busy) return;
            dropshipStock -= n;
            ventralHangar.Launch(dropshipPrefab, n, this, null);
        }

        public void FireMissiles(int count = 8)
        {
            if (missiles != null && target != null) missiles.FireSalvo(target, count, Velocity);
        }

        /// <summary>Order every craft in the air: attack the current target, escort, or return.</summary>
        public void OrderWing(Craft.Order order)
        {
            if (order == Craft.Order.Attack && target == null) return;
            foreach (var c in wing)
                if (c != null && c.kind != Craft.Kind.Dropship) c.Command(order, target);
        }

        public void Register(Craft c)
        {
            if (!wing.Contains(c)) wing.Add(c);
        }

        /// <summary>Called when a craft lands back aboard.</summary>
        public void Recover(Craft c)
        {
            wing.Remove(c);
            switch (c.kind)
            {
                case Craft.Kind.Fighter: fighterStock++; break;
                case Craft.Kind.Drone: droneStock++; break;
                case Craft.Kind.Dropship: dropshipStock++; break;
            }
        }
    }
}
