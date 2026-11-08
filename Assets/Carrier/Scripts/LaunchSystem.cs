using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A launcher on a planet or moon (a mass driver, a StarTram, a launch loop, a slingatron, a
    /// spin launcher): every so often a payload runs along its track, speeding up all the way
    /// (constant acceleration), leaves the end of it at speed with a glowing trail, and flies on
    /// out into space. The track is a line of points in the launcher's own space, so it turns
    /// with the body it stands on.
    /// </summary>
    public class LaunchSystem : MonoBehaviour
    {
        [Tooltip("The track, start to end, in this transform's space (m).")]
        public Vector3[] track;
        public float interval = 18f;
        [Tooltip("Seconds from the start of the track to the end.")]
        public float trackTime = 6f;
        [Tooltip("Speed leaving the track (m/s).")]
        public float exitSpeed = 900f;
        public float flightTime = 25f;
        public float firstLaunch = 3f;
        public Mesh payloadMesh;
        public Material payloadMaterial;
        public Vector3 payloadSize = new Vector3(6f, 6f, 16f);
        public Material trailMaterial;
        public float trailWidth = 8f;
        [Tooltip("A part that spins up before each launch (a spin launcher's arm), turning about its own up axis.")]
        public Transform spinner;
        public float spinnerRate = 720f;

        /// <summary>Payloads launched (left the end of the track).</summary>
        public int Launched { get; private set; }
        /// <summary>Payloads on the track or in flight now.</summary>
        public int Active => shots.Count;
        public float TrackLength { get; private set; }
        /// <summary>Seconds till the next payload starts along the track.</summary>
        public float NextLaunchIn => next - Time.time;

        class Shot { public Transform t; public float age; public bool free; public Vector3 vel; public TrailRenderer trail; }
        readonly List<Shot> shots = new List<Shot>();
        float[] cum;
        float next;

        void Start()
        {
            next = Time.time + firstLaunch;
            if (track == null || track.Length < 2) { enabled = false; return; }
            cum = new float[track.Length];
            for (int i = 1; i < track.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(track[i - 1], track[i]);
            TrackLength = cum[track.Length - 1];
        }

        void OnDestroy()
        {
            foreach (var s in shots) if (s.t != null) Destroy(s.t.gameObject);
        }

        /// <summary>The point `d` metres along the track (local), and the way it runs there.</summary>
        Vector3 At(float d, out Vector3 dir)
        {
            int i = 1;
            while (i < track.Length - 1 && cum[i] < d) i++;
            float seg = Mathf.Max(1e-3f, cum[i] - cum[i - 1]);
            dir = (track[i] - track[i - 1]) / seg;
            return Vector3.Lerp(track[i - 1], track[i], Mathf.Clamp01((d - cum[i - 1]) / seg));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (spinner != null) spinner.Rotate(0f, spinnerRate * dt, 0f, Space.Self);
            if (Time.time >= next)
            {
                next = Time.time + interval;
                Spawn();
            }
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                if (s.t == null) { shots.RemoveAt(i); continue; }
                s.age += dt;
                if (!s.free)
                {
                    // constant acceleration along the track: d = L (age / T)^2
                    float k = s.age / Mathf.Max(0.1f, trackTime);
                    if (k < 1f)
                    {
                        Vector3 p = At(TrackLength * k * k, out Vector3 dir);
                        s.t.position = transform.TransformPoint(p);
                        s.t.rotation = Quaternion.LookRotation(transform.TransformDirection(dir));
                        continue;
                    }
                    // off the end, out into space
                    At(TrackLength, out Vector3 end);
                    s.free = true;
                    s.t.SetParent(null, true);
                    s.t.position = transform.TransformPoint(track[track.Length - 1]);
                    s.vel = transform.TransformDirection(end).normalized * exitSpeed;
                    s.t.rotation = Quaternion.LookRotation(s.vel);
                    s.trail.emitting = true;
                    s.age = 0f;
                    Launched++;
                }
                s.t.position += s.vel * dt;
                if (s.age > flightTime) { Destroy(s.t.gameObject); shots.RemoveAt(i); }
            }
        }

        void Spawn()
        {
            var go = new GameObject("Payload", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localScale = payloadSize;
            go.GetComponent<MeshFilter>().sharedMesh = payloadMesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = payloadMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMaterial;
            tr.time = 2.5f;
            tr.startWidth = trailWidth;
            tr.endWidth = 0f;
            tr.minVertexDistance = 20f;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.emitting = false;
            var s = new Shot { t = go.transform, trail = tr };
            At(0f, out Vector3 dir);
            go.transform.position = transform.TransformPoint(track[0]);
            go.transform.rotation = Quaternion.LookRotation(transform.TransformDirection(dir));
            shots.Add(s);
        }
    }
}
