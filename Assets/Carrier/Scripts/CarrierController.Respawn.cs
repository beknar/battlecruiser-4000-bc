using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>The ship destroyed, and back at its respawn point after a while.</summary>
    public partial class CarrierController
    {
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
            if (spawnFrame != null) spawnPosition = spawnFrame.position + spawnFrame.rotation * spawnOffset;
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

        /// <summary>Something else is flying the ship (the warp drive): no throttle, rudder or
        /// collisions from this controller until it is cleared.</summary>
        [System.NonSerialized] public bool externalMotion;

        /// <summary>Fastest the elevator thrusters may take the ship down (m/s): the landing
        /// assist slows the descent near the ground.</summary>
        [System.NonSerialized] public float maxDescent = float.PositiveInfinity;

        /// <summary>Hits slower than this (m/s, into the surface) do no damage: higher on a
        /// planet, so a gentle touchdown isn't a crash.</summary>
        [System.NonSerialized] public float safeImpact = 3f;

        /// <summary>Set the heading and nose angle directly (the warp drive lining up a jump).</summary>
        public void SetAttitude(float newHeading, float newPitch)
        {
            heading = Mathf.Repeat(newHeading, 360f);
            pitchAngle = Mathf.Clamp(newPitch, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(-pitchAngle, heading, 0f);
        }

        /// <summary>Set the forward speed directly (m/s); elevator movement stops.</summary>
        public void SetMotion(float newSpeed)
        {
            speed = newSpeed;
            liftSpeed = 0f;
        }

        /// <summary>Where the ship comes back after being destroyed (the last place it warped to),
        /// kept relative to the reach it is in (a planet's, moon's or field's orbit, which doesn't
        /// spin, or a station turning with its planet), or else to `frame`.</summary>
        public void SetSpawn(Vector3 position, Quaternion rotation, Transform frame = null)
        {
            if (OrbitalSystem.Instance != null)
            {
                var reach = OrbitalSystem.Instance.ReachAt(position);
                if (reach != null) frame = reach;
            }
            spawnPosition = position;
            spawnRotation = rotation;
            spawnFrame = frame;
            spawnOffset = frame != null ? Quaternion.Inverse(frame.rotation) * (position - frame.position) : Vector3.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Destroyed)
            {
                // the wreckage flies on its own (ShipBreakup); the ship itself waits to respawn
                if (hum != null) hum.volume = 0f;
                return;
            }
            if (externalMotion)
            {
                UpdateLiftFlares(0f);
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
            liftSpeed = Mathf.Max(liftSpeed, -maxDescent);
            transform.position += Collide((transform.forward * speed + transform.up * liftSpeed) * dt);
            if (Cover.Active) transform.position = Depenetrate(transform.position);
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
    }
}
