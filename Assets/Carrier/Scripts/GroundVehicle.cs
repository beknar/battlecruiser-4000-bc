using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>
    /// A hover tank: it circles its post until the alarm, then drives toward the player's ship
    /// and circles it at gun range, riding a few metres over the ground (and over water and lava
    /// alike), leaning with the slope, its turret on the target.
    /// </summary>
    public class GroundVehicle : MonoBehaviour, IOriginShift, IVelocity
    {
        public PlanetSurface surface;
        public Transform body, turret;
        public float hover = 3.5f;
        public float patrolSpeed = 10f, chaseSpeed = 24f;
        public float patrolRadius = 350f, standoff = 1400f;
        public float turnRate = 40f;
        public bool engaged;

        public Vector3 Velocity { get; private set; }

        Vector3 home;
        float angle, speed, heading, circleSign = 1f;
        Transform target;
        Health health;
        float nextRetarget;

        void Start()
        {
            home = transform.position;
            heading = transform.eulerAngles.y;
            angle = Random.Range(0f, Mathf.PI * 2f);
            circleSign = Random.value < 0.5f ? -1f : 1f;
            health = GetComponent<Health>();
            var cc = FindAnyObjectByType<CarrierController>();
            if (cc != null) target = cc.transform;
        }

        public void OriginShifted(Vector3 d) => home += d;

        /// <summary>With more than one player ship about (co-op), go for the nearest now and then.</summary>
        void Retarget()
        {
            if (PlayerShips.Count < 2 || Time.time < nextRetarget) return;
            nextRetarget = Time.time + 2f;
            var nearest = PlayerShips.Nearest(transform.position);
            if (nearest != null) target = nearest;
        }

        void Update()
        {
            if (surface == null || (health != null && health.Dead)) return;
            Retarget();
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 goal;
            float want;
            if (engaged && target != null)
            {
                Vector3 to = target.position - pos;
                to.y = 0f;
                float d = to.magnitude;
                if (d > standoff) { goal = target.position; want = chaseSpeed; }
                else
                {
                    // circle at gun range
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * circleSign;
                    goal = pos + side * 200f - to.normalized * (standoff - d) * 0.5f;
                    want = chaseSpeed * 0.7f;
                }
            }
            else
            {
                angle += patrolSpeed / patrolRadius * dt * circleSign;
                goal = home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolRadius;
                want = patrolSpeed;
            }
            Vector3 dir = goal - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1f)
            {
                float wantHeading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                heading = Mathf.MoveTowardsAngle(heading, wantHeading, turnRate * dt);
            }
            speed = Mathf.MoveTowards(speed, want, 8f * dt);
            Vector3 fwd = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 next = pos + fwd * speed * dt;
            float floor = surface.FloorAt(next);
            next.y = Mathf.Lerp(pos.y, floor + hover, 1f - Mathf.Exp(-6f * dt));
            next.y = Mathf.Max(next.y, floor + 1f);
            Velocity = (next - pos) / Mathf.Max(dt, 1e-4f);
            transform.position = next;
            // lean with the ground
            float f = surface.FloorAt(next + fwd * 8f), b = surface.FloorAt(next - fwd * 8f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float r = surface.FloorAt(next + right * 6f), l = surface.FloorAt(next - right * 6f);
            Vector3 n = new Vector3(-(r - l) / 12f, 1f, -(f - b) / 16f);
            Vector3 normal = (right * n.x + Vector3.up + fwd * n.z).normalized;
            var flat = Quaternion.Euler(0f, heading, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.FromToRotation(Vector3.up, normal) * flat, 1f - Mathf.Exp(-5f * dt));
            if (turret != null && target != null && engaged)
            {
                Vector3 t = turret.parent.InverseTransformDirection(target.position - turret.position);
                float yaw = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
                turret.localRotation = Quaternion.RotateTowards(turret.localRotation, Quaternion.Euler(0f, yaw, 0f), 60f * dt);
            }
        }
    }
}
