using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A gun that turns toward a target and fires projectiles from its muzzles.
    ///
    /// Two kinds of mount:
    /// - yaw + pitch joints (the carrier's turrets): `yaw` turns around its own up axis relative
    ///   to its rest pose, so the turret works on any mounting angle; pitch is optional.
    /// - ball mount (the frigate's side guns): `yaw` swings freely within a cone around its rest
    ///   direction (straight out from the hull side), like a gun in a ball socket.
    ///
    /// A gun only fires when it points at the target (within `fireCone`), never on the way
    /// there. With `checkHullClearance` it also won't fire, or report that it bears, when the line
    /// of fire would pass through its own ship.
    /// </summary>
    public class CarrierTurret : MonoBehaviour
    {
        [Header("Moving parts")]
        public Transform yaw;
        public Transform pitch;
        public Transform[] muzzles;

        [Header("Limits")]
        public float yawSpeed = 30f;      // degrees per second (a ball mount's traverse speed)
        public float pitchSpeed = 20f;
        public float minPitch = -5f;
        public float maxPitch = 85f;
        [Tooltip("How far the turret may turn either side of its rest direction (180 = all round).")]
        public float yawLimit = 150f;

        [Header("Ball mount")]
        [Tooltip("Aim freely within a cone around the rest direction instead of separate yaw and pitch joints.")]
        public bool ballMount = false;
        [Tooltip("Ball mount: the widest angle from the rest direction (90 = a hemisphere; more lets it reach across the ship).")]
        public float coneLimit = 120f;
        [Tooltip("Don't fire, or count as bearing, when the shot would pass through this ship's own hull.")]
        public bool checkHullClearance = false;

        [Header("Weapon")]
        public Projectile projectilePrefab;
        public float fireInterval = 2f;
        public float range = 5000f;
        [Tooltip("Fire when the guns point within this many degrees of the target.")]
        public float fireCone = 3f;
        public bool autoFire = true;
        public Sfx fireSound = Sfx.Cannon;
        [Tooltip("Aim ahead of a moving target so the shots meet it.")]
        public bool leadTarget = false;
        [Tooltip("Shown in the HUD (e.g. \"Port\"); the object name if empty.")]
        public string label = "";
        [Tooltip("Only engage ships on this side of the hull: +1 starboard, -1 port, 0 either side.")]
        public int side = 0;

        [Header("Targeting")]
        public Transform target;

        /// <summary>Shots fired since the game started (for the HUD and the self-test).</summary>
        public int ShotsFired { get; private set; }
        /// <summary>Shots fired with a target set while pointing well away from it (should stay 0).</summary>
        public int OffTargetShots { get; private set; }
        /// <summary>Shots whose path crossed this ship's own hull (should stay 0 with hull clearance).</summary>
        public int HullShots { get; private set; }
        /// <summary>When it last fired (for spinning barrels and the self-test).</summary>
        public float LastFiredTime { get; private set; } = -100f;
        /// <summary>Targets it has fired at, by name, for the self-test (CIWS: drones only).</summary>
        public readonly System.Collections.Generic.HashSet<string> FiredAt = new System.Collections.Generic.HashSet<string>();

        float yawAngle, pitchAngle, cooldown, fireRequestUntil;
        Transform healthOf, moverOf;
        Health targetHealth;
        IVelocity mover;
        Quaternion yawRest, pitchRest;
        Collider[] hullColliders;

        public string Label => string.IsNullOrEmpty(label) ? name : label;

        void Awake()
        {
            if (yaw == null) yaw = transform;
            yawRest = yaw.localRotation;
            if (pitch != null) pitchRest = pitch.localRotation;
            cooldown = Random.Range(0f, fireInterval);
        }

        void Update()
        {
            cooldown -= Time.deltaTime;
            if (target == null) { ReturnToRest(); return; }
            if (target != healthOf) { healthOf = target; targetHealth = target.GetComponentInParent<Health>(); }
            Vector3 aimAt = AimPoint();
            bool bears = CanBear(aimAt);
            // a gun that can't reach the target (wrong side, or its own hull in the way) stays stowed
            if ((ballMount || side != 0) && !bears) { ReturnToRest(); return; }
            Aim(aimAt);
            // hold fire while the target is destroyed (waiting to respawn)
            bool alive = targetHealth == null || !targetHealth.Dead;
            bool wantFire = autoFire || Time.time < fireRequestUntil;
            if (wantFire && alive && bears && cooldown <= 0f && OnTarget(aimAt))
            {
                Fire();
                fireRequestUntil = 0f;
            }
        }

        /// <summary>Kept for older callers: the same as RequestFire.</summary>
        public bool FireIfBearing() => RequestFire();

        /// <summary>Manual fire: shoot as soon as the gun is lined up on the target (within
        /// `window` seconds), rather than wherever it happens to point now. Returns false if the
        /// gun can't bear on the target at all.</summary>
        public bool RequestFire(float window = 1.5f)
        {
            if (target == null || !CanBear(AimPoint())) return false;
            fireRequestUntil = Time.time + window;
            return true;
        }

        /// <summary>Where to point: the target, or (with leadTarget) where it will be when a shot arrives.</summary>
        Vector3 AimPoint()
        {
            Vector3 p = target.position;
            if (!leadTarget || projectilePrefab == null || projectilePrefab.speed <= 0f) return p;
            if (moverOf != target) { moverOf = target; mover = Motion.Of(target); }
            if (mover == null) return p;
            Vector3 origin = AimOrigin(), v = mover.Velocity;
            float t = Vector3.Distance(origin, p) / projectilePrefab.speed;
            for (int i = 0; i < 2; i++) t = Vector3.Distance(origin, p + v * t) / projectilePrefab.speed;
            return p + v * t;
        }

        /// <summary>Whether this gun can be brought to bear on a point: inside its traverse,
        /// elevation and range limits, and (with hull clearance) with a clear line of fire.</summary>
        public bool CanBear(Vector3 worldPos)
        {
            if (yaw == null) return false;
            Vector3 d = worldPos - yaw.position;
            if (d.sqrMagnitude > range * range) return false;
            if (side != 0 && Hull.InverseTransformPoint(worldPos).x * side <= 0f) return false;
            if (ballMount)
            {
                if (Vector3.Angle(RestForwardWorld, d) > coneLimit) return false;
            }
            else
            {
                Transform frame = yaw.parent;
                Vector3 inParent = frame ? frame.InverseTransformPoint(worldPos) : worldPos;
                Vector3 local = Quaternion.Inverse(yawRest) * (inParent - yaw.localPosition);
                float yawA = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(yawA) > yawLimit) return false;
                if (pitch != null)
                {
                    float pitchA = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                    if (pitchA < minPitch - 1f || pitchA > maxPitch + 1f) return false;
                }
            }
            // line of fire from the gun's own pivot (a deck mount's base sits on the deck itself)
            Vector3 from = Gun.position;
            if (checkHullClearance && HullInTheWay(from, worldPos - from)) return false;
            // no shooting through an asteroid or a wreck at a ship hiding behind it
            return !Cover.Blocked(from, worldPos);
        }

        /// <summary>Does a shot from `origin` along `dir` cross this ship's own hull?</summary>
        bool HullInTheWay(Vector3 origin, Vector3 dir)
        {
            if (hullColliders == null)
            {
                var ship = GetComponentInParent<CarrierController>();
                // the hull itself, not the shield bubble round it (shots pass out through that)
                hullColliders = ship != null
                    ? System.Array.FindAll(ship.GetComponentsInChildren<Collider>(true), c => c.GetComponent<Shield>() == null)
                    : new Collider[0];
            }
            if (hullColliders.Length == 0) return false;
            // the ship is a few hundred metres long at most; no need to test further out
            var ray = new Ray(origin, dir.normalized);
            float reach = Mathf.Min(dir.magnitude, 600f);
            foreach (var c in hullColliders)
                if (c != null && c.enabled && c.Raycast(ray, out _, reach)) return true;
            return false;
        }

        /// <summary>With no target, swing back to the stowed position.</summary>
        void ReturnToRest()
        {
            if (ballMount)
            {
                yaw.localRotation = Quaternion.RotateTowards(yaw.localRotation, yawRest, yawSpeed * Time.deltaTime);
                return;
            }
            yawAngle = Mathf.MoveTowardsAngle(yawAngle, 0f, yawSpeed * Time.deltaTime);
            yaw.localRotation = yawRest * Quaternion.AngleAxis(yawAngle, Vector3.up);
            if (pitch == null) return;
            pitchAngle = Mathf.MoveTowards(pitchAngle, 0f, pitchSpeed * Time.deltaTime);
            pitch.localRotation = pitchRest * Quaternion.AngleAxis(-pitchAngle, Vector3.right);
        }

        Vector3 RestForwardWorld => (yaw.parent ? yaw.parent.rotation : Quaternion.identity) * (yawRest * Vector3.forward);

        /// <summary>Turn toward a world position at the turret's traverse speed.</summary>
        public void Aim(Vector3 worldPos)
        {
            Transform frame = yaw.parent;
            if (ballMount)
            {
                Vector3 dir = frame ? frame.InverseTransformDirection(worldPos - yaw.position) : worldPos - yaw.position;
                Vector3 rest = yawRest * Vector3.forward;
                if (Vector3.Angle(rest, dir) > coneLimit) dir = Vector3.RotateTowards(rest, dir, coneLimit * Mathf.Deg2Rad, 0f);
                Vector3 up = yawRest * Vector3.up;
                if (Vector3.Cross(dir, up).sqrMagnitude < 1e-6f) up = yawRest * Vector3.forward;
                var want = Quaternion.LookRotation(dir, up);
                yaw.localRotation = Quaternion.RotateTowards(yaw.localRotation, want, yawSpeed * Time.deltaTime);
                return;
            }

            Vector3 inParent = frame ? frame.InverseTransformPoint(worldPos) : worldPos;
            Vector3 local = Quaternion.Inverse(yawRest) * (inParent - yaw.localPosition);
            float wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
            yawAngle = Mathf.MoveTowardsAngle(yawAngle, wantYaw, yawSpeed * Time.deltaTime);
            yaw.localRotation = yawRest * Quaternion.AngleAxis(yawAngle, Vector3.up);

            if (pitch != null)
            {
                Vector3 p = Quaternion.Inverse(pitchRest) * (yaw.InverseTransformPoint(worldPos) - pitch.localPosition);
                float wantPitch = Mathf.Atan2(p.y, new Vector2(p.x, p.z).magnitude) * Mathf.Rad2Deg;
                wantPitch = Mathf.Clamp(wantPitch, minPitch, maxPitch);
                pitchAngle = Mathf.MoveTowards(pitchAngle, wantPitch, pitchSpeed * Time.deltaTime);
                pitch.localRotation = pitchRest * Quaternion.AngleAxis(-pitchAngle, Vector3.right);
            }
        }

        public bool OnTarget(Vector3 worldPos)
        {
            Vector3 dir = worldPos - AimOrigin();
            if (dir.sqrMagnitude > range * range) return false;
            return Vector3.Angle(AimForward(), dir) <= fireCone;
        }

        Transform hull;
        /// <summary>The ship this gun is mounted on (its centreline divides port from starboard).</summary>
        Transform Hull
        {
            get
            {
                if (hull == null)
                {
                    var ship = GetComponentInParent<CarrierController>();
                    hull = ship != null ? ship.transform : transform.root;
                }
                return hull;
            }
        }

        Transform Gun => pitch != null ? pitch : yaw;
        Vector3 AimOrigin() => muzzles != null && muzzles.Length > 0 && muzzles[0] ? muzzles[0].position : Gun.position;
        Vector3 AimForward() => muzzles != null && muzzles.Length > 0 && muzzles[0] ? muzzles[0].forward : Gun.forward;

        /// <summary>Fire one shot from every muzzle, wherever the gun points now.</summary>
        public void Fire()
        {
            cooldown = fireInterval;
            if (projectilePrefab == null || muzzles == null) return;
            SoundFX.Play(fireSound, AimOrigin());
            ShotsFired++;
            LastFiredTime = Time.time;
            if (target != null) FiredAt.Add(target.name);
            if (target != null && Vector3.Angle(AimForward(), AimPoint() - AimOrigin()) > fireCone * 2f) OffTargetShots++;
            if (checkHullClearance && HullInTheWay(AimOrigin(), AimForward() * range)) HullShots++;
            var owner = GetComponentInParent<CarrierController>();
            foreach (var m in muzzles)
            {
                if (m == null) continue;
                var p = Instantiate(projectilePrefab, m.position, m.rotation);
                p.owner = owner ? owner.transform : transform.root;
            }
        }
    }
}
