using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A charged beam weapon: StartCharge(), it charges for `chargeTime` seconds (particles drawn
    /// into the emitter, a growing glow and a rising whine), then fires for `fireDuration`:
    /// a stream of particles pouring out of the emitter along the beam, burning whatever it
    /// touches first (a shield, or a hull) for `damagePerSecond`.
    ///
    /// The frigate's bow gun fires straight ahead, nudged onto the target when it is within
    /// `aimAssist` degrees of the bow; the enemy cruiser's version aims straight at its target.
    /// </summary>
    public class MainBeam : MonoBehaviour
    {
        public enum State { Idle, Charging, Firing }

        [Header("Weapon")]
        public float chargeTime = 10f;
        public float fireDuration = 2.5f;
        public float damagePerSecond = 1400f;
        public float range = 6000f;
        [Tooltip("How fat the beam is when it touches things (m).")]
        public float radius = 4f;
        [Tooltip("Bend toward the target when it is within this many degrees of the emitter's forward (180 = always).")]
        public float aimAssist = 6f;
        public Transform muzzle;
        public Transform target;

        [Header("Effects")]
        public ParticleSystem chargeParticles;
        public ParticleSystem streamParticles;
        public ParticleSystem sparkleParticles;
        public ParticleSystem impactParticles;
        public Light glow;
        public float glowIntensity = 4000f;
        public Renderer lens;
        [ColorUsage(false, true)] public Color lensColor = new Color(3f, 3.4f, 4f);
        public float streamSpeed = 3200f;

        [Header("Sound")]
        public Sfx chargeSound = Sfx.BeamCharge;
        public Sfx fireSound = Sfx.BeamFire;

        public State Current { get; private set; }
        public float ChargeProgress => Current == State.Charging ? Mathf.Clamp01((Time.time - stateStart) / chargeTime) : Current == State.Firing ? 1f : 0f;
        public float ChargeTimeLeft => Current == State.Charging ? Mathf.Max(0f, chargeTime - (Time.time - stateStart)) : 0f;
        /// <summary>Where the beam ends this frame while firing (its impact point, or full range).</summary>
        public Vector3 BeamEnd { get; private set; }
        public Transform LastHit { get; private set; }
        public int Shots { get; private set; }
        public float DamageDealt { get; private set; }
        /// <summary>Seconds from StartCharge to the first frame of firing, for the last shot.</summary>
        public float LastChargeSeconds { get; private set; }

        float stateStart;
        Transform owner;
        Vector3 beamDir;
        MaterialPropertyBlock block;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly RaycastHit[] Hits = new RaycastHit[32];

        void Awake()
        {
            owner = transform.root;
            block = new MaterialPropertyBlock();
            SetEmitting(chargeParticles, false);
            SetEmitting(streamParticles, false);
            SetEmitting(sparkleParticles, false);
            SetEmitting(impactParticles, false);
            if (glow) glow.intensity = 0f;
            SetLens(0f);
        }

        public bool StartCharge()
        {
            if (Current != State.Idle || !isActiveAndEnabled) return false;   // switched off (its ship destroyed)
            Current = State.Charging;
            stateStart = Time.time;
            SetEmitting(chargeParticles, true);
            SoundFX.PlayFitted(chargeSound, Origin, chargeTime, true);    // ends as the beam fires
            return true;
        }

        /// <summary>Stop at once, charging or firing (the ship carrying it was destroyed).</summary>
        public void Abort()
        {
            if (Current == State.Charging) Cancel();
            else if (Current == State.Firing) EndFiring();
        }

        public void Cancel()
        {
            if (Current != State.Charging) return;
            Current = State.Idle;
            SetEmitting(chargeParticles, false);
            if (glow) glow.intensity = 0f;
            SetLens(0f);
        }

        Vector3 Origin => muzzle ? muzzle.position : transform.position;
        Vector3 Forward => muzzle ? muzzle.forward : transform.forward;

        void Update()
        {
            switch (Current)
            {
                case State.Charging:
                {
                    float k = ChargeProgress;
                    if (chargeParticles)
                    {
                        var em = chargeParticles.emission;
                        em.rateOverTimeMultiplier = Mathf.Lerp(40f, 600f, k * k);
                    }
                    if (glow) glow.intensity = glowIntensity * 0.4f * k * k;
                    SetLens(k * k);
                    if (k >= 1f) BeginFiring();
                    break;
                }
                case State.Firing:
                    Fire();
                    if (Time.time - stateStart >= fireDuration) EndFiring();
                    break;
            }
        }

        void BeginFiring()
        {
            LastChargeSeconds = Time.time - stateStart;
            Current = State.Firing;
            stateStart = Time.time;
            Shots++;
            beamDir = AimDirection();
            SetEmitting(chargeParticles, false);
            SetEmitting(streamParticles, true);
            SetEmitting(sparkleParticles, true);
            SoundFX.PlayFitted(fireSound, Origin, fireDuration, false);
        }

        void EndFiring()
        {
            Current = State.Idle;
            SetEmitting(streamParticles, false);
            SetEmitting(sparkleParticles, false);
            SetEmitting(impactParticles, false);
            if (glow) glow.intensity = 0f;
            SetLens(0f);
            LastHit = null;
        }

        /// <summary>Straight out of the emitter, or onto the target when it is close enough to the line.</summary>
        Vector3 AimDirection()
        {
            Vector3 fwd = Forward;
            if (target == null) return fwd;
            var h = target.GetComponentInParent<Health>();
            if (h != null && h.Dead) return fwd;
            Vector3 to = target.position - Origin;
            return Vector3.Angle(fwd, to) <= aimAssist ? to.normalized : fwd;
        }

        void Fire()
        {
            // the beam swings with the emitter (and follows the target inside the assist cone)
            beamDir = Vector3.RotateTowards(beamDir, AimDirection(), 20f * Mathf.Deg2Rad * Time.deltaTime, 0f);
            Vector3 o = Origin;
            float length = range;
            LastHit = null;
            int n = Physics.SphereCastNonAlloc(o, radius, beamDir, Hits, range, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var hit = Hits[i];
                if (hit.transform.IsChildOf(owner) || hit.distance <= 0f) continue;
                if (hit.distance < bestDist) { bestDist = hit.distance; best = hit; }
            }
            if (bestDist < float.MaxValue)
            {
                length = bestDist;
                LastHit = best.transform;
                var h = best.collider.GetComponentInParent<Health>();
                if (h != null && !h.Dead)
                {
                    float dmg = damagePerSecond * Time.deltaTime;
                    h.Damage(dmg, best.point, HitKind.Beam);
                    DamageDealt += dmg;
                }
            }
            BeamEnd = o + beamDir * length;

            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 30f, 0.5f);
            if (glow) glow.intensity = glowIntensity * flicker;
            SetLens(1.4f * flicker);
            if (streamParticles)
            {
                streamParticles.transform.rotation = Quaternion.LookRotation(beamDir);
                var main = streamParticles.main;
                main.startLifetimeMultiplier = Mathf.Max(0.02f, length / streamSpeed);
            }
            if (sparkleParticles)
            {
                // a sleeve of slower sparks spread along the whole beam
                var shape = sparkleParticles.shape;
                sparkleParticles.transform.SetPositionAndRotation(o + beamDir * length * 0.5f, Quaternion.LookRotation(beamDir));
                shape.scale = new Vector3(radius * 1.2f, radius * 1.2f, length);
            }
            if (impactParticles)
            {
                SetEmitting(impactParticles, LastHit != null);
                impactParticles.transform.SetPositionAndRotation(BeamEnd, Quaternion.LookRotation(-beamDir));
            }
        }

        void SetLens(float k)
        {
            if (lens == null) return;
            lens.GetPropertyBlock(block);
            block.SetColor(EmissionId, lensColor * k);
            lens.SetPropertyBlock(block);
        }

        static void SetEmitting(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on && !ps.isEmitting) ps.Play(true);
            else if (!on && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
