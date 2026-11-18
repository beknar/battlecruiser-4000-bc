using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A band of enemies that wanders on a regular circuit round a body (an EncounterGroup on
    /// patrol): its place goes round the body once every `period` seconds, `radius` out, in the
    /// plane square to `normal`, and its ships lying in wait go with it in their formation. It is
    /// a hostile place of its own (its WarpPoint, Kind.Patrol), so it is seen moving on the system
    /// map and can be warped to. It holds where it is while it is fighting or a player is near
    /// (within `quietRange`): it is met where it was seen.
    /// </summary>
    public class EncounterPatrol : MonoBehaviour
    {
        public WarpPoint body;
        public float radius = 1500000f;
        public float period = 7200f;
        [Tooltip("Where on its circuit it starts (degrees).")]
        public float phase;
        public Vector3 normal = Vector3.up;
        public float quietRange = 60000f;
        public EncounterGroup group;

        public static readonly List<EncounterPatrol> All = new List<EncounterPatrol>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>How far round its circuit it has gone (degrees), and whether it is moving now.</summary>
        public float Angle { get; private set; }
        public bool Moving { get; private set; }

        Vector3[] offsets;
        Vector3 axis0;

        void Start()
        {
            if (group == null) group = GetComponent<EncounterGroup>();
            Angle = phase;
            Vector3 n = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            axis0 = Vector3.Cross(n, Vector3.right).sqrMagnitude > 0.01f ? Vector3.Cross(n, Vector3.right).normalized : Vector3.forward;
            // each ship's place in the formation, from the band's middle
            if (group != null && group.pilots != null)
            {
                offsets = new Vector3[group.pilots.Length];
                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = group.pilots[i] != null ? group.pilots[i].Home - transform.position : Vector3.zero;
            }
            Place();
        }

        /// <summary>Where on its circuit it is at an angle (degrees round).</summary>
        public Vector3 PointAt(float degrees)
        {
            Vector3 n = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            return (body != null ? body.Position : Vector3.zero) + Quaternion.AngleAxis(degrees, n) * axis0 * radius;
        }

        void Update()
        {
            if (body == null) return;
            Moving = !Quiet();
            if (Moving) Angle = (Angle + 360f * Time.deltaTime / Mathf.Max(60f, period)) % 360f;
            Place();
        }

        bool Quiet()
        {
            if (group != null && group.Active) return true;
            foreach (var t in PlayerShips.All)
                if (t != null && (t.position - transform.position).sqrMagnitude < quietRange * quietRange) return true;
            return false;
        }

        /// <summary>The band where its circuit has it (only those still lying in wait at home are
        /// moved with it: one out fighting, or flying back, is left to its own way).</summary>
        void Place()
        {
            Vector3 c = PointAt(Angle);
            transform.position = c;
            if (!Moving || group == null || group.pilots == null || offsets == null) return;
            for (int i = 0; i < group.pilots.Length && i < offsets.Length; i++)
            {
                var p = group.pilots[i];
                if (p == null || p.Current != CombatPilot.State.Lurk || !p.AtHome) continue;
                Vector3 home = c + offsets[i];
                p.SetHome(home, p.transform.rotation);
                p.transform.position = home;
            }
        }
    }
}
