using UnityEngine;

namespace CarrierGame
{
    /// <summary>How the ship moves: throttle and rudder, the nose up or down, roll, and the
    /// elevator thrusters; and when something else is flying it (the warp drive, the landing).</summary>
    public partial class CarrierController
    {
        /// <summary>Start bringing the nose and the wings back to level (stops as soon as the pilot
        /// pitches or rolls).</summary>
        public void LevelOff() => autoLevel = true;

        /// <summary>The nose up (+) or down, and how far the ship is rolled (degrees, + to the right).</summary>
        public float Roll => rollAngle;
        float rollAngle;

        Quaternion Attitude => Quaternion.Euler(-pitchAngle, heading, 0f) * Quaternion.AngleAxis(-rollAngle, Vector3.forward);

        /// <summary>Something else is flying the ship (the warp drive): no throttle, rudder or
        /// collisions from this controller until it is cleared.</summary>
        [System.NonSerialized] public bool externalMotion;

        /// <summary>Fastest the elevator thrusters may take the ship down (m/s): the landing
        /// assist slows the descent near the ground.</summary>
        [System.NonSerialized] public float maxDescent = float.PositiveInfinity;

        /// <summary>Hits slower than this (m/s, into the surface) do no damage: higher on a
        /// planet, so a gentle touchdown isn't a crash.</summary>
        [System.NonSerialized] public float safeImpact = 3f;

        /// <summary>The water holds the ship back (Seafaring): its top speed, and its elevator
        /// thrusters' speed, are this much of what they are in the air.</summary>
        [System.NonSerialized] public float speedScale = 1f, liftScale = 1f;

        /// <summary>Set the heading and nose angle directly (the warp drive lining up a jump, the
        /// landing assist): the wings come back to level as it goes on.</summary>
        public void SetAttitude(float newHeading, float newPitch)
        {
            heading = Mathf.Repeat(newHeading, 360f);
            pitchAngle = Mathf.Clamp(newPitch, -maxPitch, maxPitch);
            rollAngle = Mathf.MoveTowardsAngle(rollAngle, 0f, rollRate * 1.5f * Time.deltaTime);
            transform.rotation = Attitude;
        }

        /// <summary>Set the forward speed directly (m/s); elevator movement stops.</summary>
        public void SetMotion(float newSpeed)
        {
            speed = newSpeed;
            liftSpeed = 0f;
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
            speed = Mathf.MoveTowards(speed, throttle * maxSpeed * speedScale, acceleration * dt);

            // attitude: heading turns about the world vertical, the nose pitches up or down, and
            // the ship rolls about its own length (all the way round, if the pilot keeps on)
            heading = Mathf.Repeat(heading + rudder * turnRate * dt, 360f);
            bool pitching = Mathf.Abs(pitchInput) > 0.01f, rolling = Mathf.Abs(rollInput) > 0.01f;
            if (pitching || rolling) autoLevel = false;
            if (pitching) pitchAngle = Mathf.Clamp(pitchAngle + pitchInput * pitchRate * dt, -maxPitch, maxPitch);
            if (rolling) rollAngle = Mathf.Repeat(rollAngle + rollInput * rollRate * dt + 180f, 360f) - 180f;
            if (autoLevel)
            {
                pitchAngle = Mathf.MoveTowards(pitchAngle, 0f, pitchRate * dt);
                rollAngle = Mathf.MoveTowardsAngle(rollAngle, 0f, rollRate * dt);
                if (Mathf.Abs(pitchAngle) < 0.01f && Mathf.Abs(rollAngle) < 0.01f) autoLevel = false;
            }
            transform.rotation = Attitude;

            // elevator thrusters push straight up or down relative to the deck
            float wantLift = elevator * elevatorSpeed * liftScale;
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
