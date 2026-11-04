using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>Flies a target ship in a slow circle around a point (a stand-in enemy for the demo).</summary>
    public class OrbitMover : MonoBehaviour, IVelocity
    {
        public Vector3 center;
        public float radius = 2500f;
        public float height = 300f;
        public float speed = 60f;
        public float startAngle = 40f;

        float angle;
        Vector3 last, velocity;

        public Vector3 Velocity => velocity;

        void Start()
        {
            angle = startAngle;
            transform.position = PointAt(angle);
            last = transform.position;
        }

        Vector3 PointAt(float a) => center + Quaternion.Euler(0f, a, 0f) * Vector3.forward * radius + Vector3.up * height;

        void Update()
        {
            angle += speed / radius * Mathf.Rad2Deg * Time.deltaTime;
            Vector3 p = PointAt(angle);
            Vector3 ahead = PointAt(angle + 1f);
            transform.position = p;
            transform.rotation = Quaternion.LookRotation(ahead - p, Vector3.up);
            velocity = (p - last) / Mathf.Max(Time.deltaTime, 1e-4f);
            last = p;
        }
    }
}
