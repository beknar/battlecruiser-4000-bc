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

        [Header("Beam")]
        public MainBeam beam;
        public float beamRange = 3500f;
        public float beamInterval = 22f;

        [Header("Targeting")]
        public Transform target;
        [Tooltip("Wait this long after the game starts before opening fire.")]
        public float holdFire = 4f;

        Health health;
        IVelocity targetMover;
        float nextBurst, nextSalvo, nextBeam;

        void Start()
        {
            health = GetComponent<Health>();
            if (target == null)
            {
                var player = FindAnyObjectByType<CarrierController>();
                if (player != null) target = player.transform;
            }
            if (target != null) targetMover = target.GetComponentInParent<IVelocity>();
            float t0 = Time.time + holdFire;
            nextBurst = t0 + Random.Range(0f, burstInterval);
            nextSalvo = t0 + Random.Range(2f, salvoInterval);
            nextBeam = t0 + Random.Range(6f, beamInterval);
            if (beam != null) beam.target = target;
        }

        Health targetHealth;

        void Update()
        {
            if (target != null && targetHealth == null) targetHealth = target.GetComponentInParent<Health>();
            // hold fire while we are destroyed, or while the target is (no shooting at wreckage)
            if (target == null || (health != null && health.Dead) || (targetHealth != null && targetHealth.Dead))
            {
                if (beam != null) beam.Abort();
                return;
            }
            float dist = Vector3.Distance(transform.position, target.position);
            float now = Time.time;
            if (gunPrefab != null && now >= nextBurst && dist < gunRange)
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
                if (target == null || (health != null && health.Dead)) yield break;
                Vector3 aim = Lead(gunPrefab.speed);
                Vector3 dir = (aim - transform.position).normalized;
                dir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * dir;
                var p = Instantiate(gunPrefab, Muzzle(dir), Quaternion.LookRotation(dir));
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
                if (target == null || (health != null && health.Dead)) yield break;
                Vector3 up = (transform.up + Random.insideUnitSphere * 0.4f).normalized;
                var m = Instantiate(missilePrefab, Muzzle(up), Quaternion.LookRotation(up));
                m.target = target;
                m.damage = Mathf.Min(m.damage, 150f);
                yield return new WaitForSeconds(0.25f);
            }
        }

        Vector3 Lead(float shotSpeed)
        {
            Vector3 p = target.position;
            if (targetMover == null || shotSpeed <= 0f) return p;
            float t = Vector3.Distance(transform.position, p) / shotSpeed;
            return p + targetMover.Velocity * t;
        }
    }
}
