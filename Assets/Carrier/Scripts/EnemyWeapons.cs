using System.Collections;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Lets a demo enemy shoot back at the player's ship once it is in range: bursts from its guns
    /// (small-arms bolts or cannon shells), missile salvos, and a charged beam. Shots are aimed
    /// at where the player will be, with a little scatter. Holds fire while the enemy is destroyed.
    /// </summary>
    public class EnemyWeapons : MonoBehaviour
    {
        [Header("Guns")]
        public Projectile gunPrefab;
        public float gunRange = 3000f;
        public float burstInterval = 3f;
        public int burstShots = 3;
        public float shotSpacing = 0.15f;
        [Tooltip("Scatter in degrees.")]
        public float spread = 1.5f;

        [Header("Missiles")]
        public Missile missilePrefab;
        public float missileRange = 4500f;
        public float salvoInterval = 14f;
        public int salvoSize = 4;

        [Header("Turrets")]
        [Tooltip("Turrets that track and fire on the target by themselves while this ship is in the fight.")]
        public CarrierTurret[] turrets;

        [Header("Beam")]
        public MainBeam beam;
        public float beamRange = 3500f;
        public float beamInterval = 22f;

        [Header("Targeting")]
        public Transform target;
        [Tooltip("Wait this long after the game starts before opening fire.")]
        public float holdFire = 4f;
        [Tooltip("Guns only fire when the target is within this many degrees of the nose (180 = turreted, any direction).")]
        public float fireArc = 180f;
        [Tooltip("Cleared while lurking in an ambush: no shooting until the ambush is sprung.")]
        public bool weaponsFree = true;

        /// <summary>Shots held back because an asteroid or wreck was in the way (for the reel log).</summary>
        public int HeldForCover { get; private set; }
        /// <summary>The target is in plain sight (no obstacle in between).</summary>
        public bool HasLineOfSight { get; private set; } = true;

        Health health;
        IVelocity targetMover;
        float nextBurst, nextSalvo, nextBeam, turretsFrom;

        void Start()
        {
            health = GetComponent<Health>();
            if (target == null) target = PlayerShips.Primary;
            if (target != null) targetMover = Motion.Of(target);
            float t0 = Time.time + holdFire;
            nextBurst = t0 + Random.Range(0f, burstInterval);
            nextSalvo = t0 + Random.Range(2f, salvoInterval);
            nextBeam = t0 + Random.Range(6f, beamInterval);
            turretsFrom = t0 + Random.Range(0f, 1f);
            if (beam != null) beam.target = target;
        }

        Health targetHealth;
        float nextRetarget;
        CombatPilot pilot;
        bool pilotLooked;

        /// <summary>Aim everything at a new target.</summary>
        public void SetTarget(Transform t)
        {
            target = t;
            targetHealth = null;
            targetMover = t != null ? Motion.Of(t) : null;
            if (beam != null) beam.target = t;
        }

        void Update()
        {
            // with more than one player ship about (co-op), a gun with no pilot to choose for it
            // goes for the nearest now and then
            if (!pilotLooked) { pilot = GetComponent<CombatPilot>(); pilotLooked = true; }
            if (pilot == null)
            {
                var nearest = PlayerShips.Retarget(target, transform.position, ref nextRetarget);
                if (nearest != target) SetTarget(nearest);
            }
            if (target != null && targetHealth == null) targetHealth = target.GetComponentInParent<Health>();
            // turrets aim and fire for themselves (they hold fire behind cover); they only get the
            // target while this ship is alive, in the fight, and the target is alive
            if (turrets != null)
            {
                bool armed = target != null && weaponsFree && Time.time >= turretsFrom &&
                             (health == null || !health.Dead) && (targetHealth == null || !targetHealth.Dead);
                foreach (var t in turrets) if (t != null) t.target = armed ? target : null;
            }
            // hold fire while we are destroyed, or while the target is (no shooting at wreckage)
            if (target == null || (health != null && health.Dead) || (targetHealth != null && targetHealth.Dead))
            {
                if (beam != null) beam.Abort();
                return;
            }
            if (!weaponsFree)
            {
                if (beam != null) beam.Abort();
                return;
            }
            float dist = Vector3.Distance(transform.position, target.position);
            float now = Time.time;
            // nothing to shoot at behind a rock: hold every weapon until it comes back into view
            HasLineOfSight = !Cover.Blocked(transform.position, target.position);
            if (!HasLineOfSight)
            {
                if (now >= nextBurst && dist < gunRange) { HeldForCover++; nextBurst = now + 0.5f; }
                return;
            }
            bool inArc = fireArc >= 180f || Vector3.Angle(transform.forward, target.position - transform.position) <= fireArc;
            if (gunPrefab != null && now >= nextBurst && dist < gunRange && inArc)
            {
                nextBurst = now + burstInterval;
                StartCoroutine(Burst());
            }
            if (missilePrefab != null && now >= nextSalvo && dist < missileRange)
            {
                nextSalvo = now + salvoInterval;
                StartCoroutine(Salvo());
            }
            if (beam != null && now >= nextBeam && dist < beamRange && beam.Current == MainBeam.State.Idle)
            {
                nextBeam = now + beamInterval;
                beam.StartCharge();
            }
        }

        // ------------------------------------------------------------ on cue (the demo reel)

        /// <summary>Open fire within `seconds` (an ambush being sprung), staggered a little.</summary>
        public void OpenFireSoon(float seconds)
        {
            float t = Time.time + seconds;
            nextBurst = t + Random.Range(0f, 1f);
            turretsFrom = t + Random.Range(0f, 1f);
            nextSalvo = Mathf.Max(Time.time + seconds + 3f, Mathf.Min(nextSalvo, t + Random.Range(3f, 8f)));
            nextBeam = Mathf.Max(Time.time + seconds + 6f, Mathf.Min(nextBeam, t + Random.Range(6f, 12f)));
        }

        public void FireBurstNow() { if (gunPrefab != null && target != null) { nextBurst = Time.time + burstInterval; StartCoroutine(Burst()); } }
        public void FireSalvoNow() { if (missilePrefab != null && target != null) { nextSalvo = Time.time + salvoInterval; StartCoroutine(Salvo()); } }
        public bool ChargeBeamNow()
        {
            if (beam == null || target == null) return false;
            nextBeam = Time.time + beamInterval;
            beam.target = target;
            return beam.StartCharge();
        }

        /// <summary>A point just outside this ship's own hull, toward the target.</summary>
        Vector3 Muzzle(Vector3 dir)
        {
            var col = GetComponentInChildren<Collider>();
            float r = col != null ? col.bounds.extents.magnitude * 0.6f : 10f;
            return transform.position + dir * r;
        }

        IEnumerator Burst()
        {
            for (int i = 0; i < burstShots; i++)
            {
                if (target == null || (health != null && health.Dead) || !weaponsFree) yield break;
                Vector3 aim = Lead(gunPrefab.speed);
                Vector3 dir = (aim - transform.position).normalized;
                // a fighter's guns point along its nose: a shot well off the nose is not taken
                if (fireArc < 180f && Vector3.Angle(transform.forward, dir) > fireArc * 1.5f) yield break;
                dir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * dir;
                var p = Pool.Take(gunPrefab, Muzzle(dir), Quaternion.LookRotation(dir));
                p.owner = transform;
                SoundFX.Play(gunPrefab.kind == HitKind.Cannon ? Sfx.Cannon : Sfx.Blaster, p.transform.position, 0.7f,
                             gunPrefab.kind == HitKind.Cannon ? 0.85f : 1.3f);
                yield return new WaitForSeconds(shotSpacing);
            }
        }

        IEnumerator Salvo()
        {
            for (int i = 0; i < salvoSize; i++)
            {
                if (target == null || (health != null && health.Dead) || !weaponsFree) yield break;
                Vector3 up = (transform.up + Random.insideUnitSphere * 0.4f).normalized;
                var m = Instantiate(missilePrefab, Muzzle(up), Quaternion.LookRotation(up));
                m.owner = transform;
                m.target = target;
                m.damage = Mathf.Min(m.damage, 150f);
                yield return new WaitForSeconds(0.25f);
            }
        }

        Vector3 Lead(float shotSpeed)
        {
            Vector3 p = target.position;
            if (targetMover == null) return p;
            return Ballistics.Lead(transform.position, p, targetMover.Velocity, shotSpeed);
        }
    }
}
