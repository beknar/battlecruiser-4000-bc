using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>
    /// Runs the star system's orbits. Every frame each Orbit (planets, moons, stations, fields,
    /// comets, Lagrange points) is put where it is at the current time, so the planets go round
    /// the star, the moons and stations round their planets, and the system map shows it.
    ///
    /// The frigate's neighbourhood stays still: whatever "reach" the frigate is in (a planet, a
    /// moon, a station, a field) is held where it is, and the rest of the system moves round it.
    /// The frigate keeps the reach it is in until it leaves it or warps (a moon going past the
    /// planet it is by doesn't take it along); arriving somewhere, it takes the smallest reach
    /// it is in
    /// (the whole system is shifted to cancel that body's motion). So shots, wreckage, particles
    /// and fights near the frigate never notice the orbits. Anywhere else, ships and shots go
    /// along with the body whose reach they are in (or their home's, for enemies posted
    /// somewhere: OrbitHome), and are told how far they moved (IOriginShift), so an ambush stays
    /// in its asteroid field and a patrol on station by its planet.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // before the other LateUpdates (the camera follows after)
    public class OrbitalSystem : MonoBehaviour
    {
        /// <summary>The star's gravitational parameter, G times its mass (m^3/s^2).</summary>
        public static double StarGM = 2.839e9;

        public double starGM = 2.839e9;
        [Tooltip("The star system's root: the star is at its origin, and every orbit is in it.")]
        public Transform system;
        public Transform player;
        [Tooltip("More reaches: things that move but aren't on an Orbit (a station on a space elevator, turning with its planet).")]
        public Transform[] extraReach;
        public float[] extraReachRadius;

        public static OrbitalSystem Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Orbital time (s): game time since the level started.</summary>
        public double Time { get; private set; }
        /// <summary>The reach the frigate is in (held still), or null in open space.</summary>
        public Transform Anchor { get; private set; }
        public int AnchorChanges { get; private set; }
        /// <summary>Things carried along this frame (for the self-test).</summary>
        public int CarriedLastFrame { get; private set; }
        /// <summary>The star, in the world.</summary>
        public Vector3 StarPosition => system != null ? system.position : Vector3.zero;

        Orbit[] orbits;
        WarpDrive drive;
        readonly List<(Transform t, float r)> reaches = new List<(Transform, float)>();
        Vector3[] before = new Vector3[0];
        Vector3[] last = new Vector3[0];     // where each reach was at the end of the last frame
        int firstExtra;                      // reaches from here on are stations' (extraReach)
        int frame;
        Vector3[] delta = new Vector3[0];
        readonly List<GameObject> roots = new List<GameObject>();
        readonly Dictionary<GameObject, (IOriginShift[] list, int count)> shifters = new Dictionary<GameObject, (IOriginShift[] list, int count)>();
        readonly List<GameObject> dead = new List<GameObject>();
        readonly List<IOriginShift> buffer = new List<IOriginShift>();

        void Awake()
        {
            Instance = this;
            StarGM = starGM;
            if (system == null) system = transform;
            // parents before children, so a moon is placed after its planet
            orbits = system.GetComponentsInChildren<Orbit>(true).OrderBy(Depth).ToArray();
            reaches.Clear();
            foreach (var o in orbits) if (o.reach > 0f) reaches.Add((o.transform, o.reach));
            firstExtra = reaches.Count;
            if (extraReach != null)
                for (int i = 0; i < extraReach.Length; i++)
                    if (extraReach[i] != null) reaches.Add((extraReach[i], extraReachRadius != null && i < extraReachRadius.Length ? extraReachRadius[i] : 5000f));
            before = new Vector3[reaches.Count];
            last = new Vector3[reaches.Count];
            delta = new Vector3[reaches.Count];
            if (player != null) drive = player.GetComponent<WarpDrive>();
            Time = 0.0;
            foreach (var o in orbits) o.Apply(0.0);
            for (int i = 0; i < reaches.Count; i++) last[i] = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static int Depth(Orbit o)
        {
            int d = 0;
            for (var p = o.transform.parent; p != null; p = p.parent) d++;
            return d;
        }

        public IReadOnlyList<Orbit> Orbits => orbits;

        void LateUpdate()
        {
            if (orbits == null) return;
            Time = UnityEngine.Time.timeSinceLevelLoad;

            for (int i = 0; i < reaches.Count; i++) before[i] = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
            int anchor = -1;
            if (player != null && !(drive != null && drive.Current == WarpDrive.State.Warping))
            {
                // the reach it is in already, while it is still in it; otherwise the smallest it is in
                int now = Anchor != null ? IndexOf(Anchor) : -1;
                anchor = now >= 0 && (player.position - before[now]).sqrMagnitude < reaches[now].r * reaches[now].r ? now : ReachOf(player.position, before);
                // flying (not warping) up to a station in the reach it is in: the station's own
                // reach takes over, so a station turning with its planet doesn't sweep past it
                for (int i = firstExtra; i < reaches.Count; i++)
                    if (i != anchor && reaches[i].t != null && (anchor < 0 || reaches[i].r < reaches[anchor].r) &&
                        (player.position - before[i]).sqrMagnitude < reaches[i].r * reaches[i].r) { anchor = i; break; }
            }

            foreach (var o in orbits) if (o != null) o.Apply(Time);

            // hold the frigate's neighbourhood still: shift the whole system against its motion since
            // the last frame (its orbit, and the turning of a planet it is on: a geostationary station)
            Vector3 shift = anchor >= 0 && reaches[anchor].t != null ? last[anchor] - reaches[anchor].t.position : Vector3.zero;
            if (shift != Vector3.zero) system.position += shift;
            var a = anchor >= 0 ? reaches[anchor].t : null;
            if (a != Anchor) { Anchor = a; AnchorChanges++; }
            for (int i = 0; i < reaches.Count; i++)
            {
                Vector3 now = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
                delta[i] = now - last[i];
                last[i] = now;
            }

            Carry(shift);
            Physics.SyncTransforms();
        }

        /// <summary>The smallest reach a point is in now (a planet, moon, station or field), or null.</summary>
        public Transform ReachAt(Vector3 p)
        {
            for (int i = 0; i < reaches.Count; i++) before[i] = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
            int i0 = ReachOf(p, before);
            return i0 >= 0 ? reaches[i0].t : null;
        }

        /// <summary>The smallest reach a point is in (by where the reaches were), or -1.</summary>
        int ReachOf(Vector3 p, Vector3[] at)
        {
            int best = -1;
            float bestR = float.MaxValue;
            for (int i = 0; i < reaches.Count; i++)
            {
                float r = reaches[i].r;
                if (r < bestR && (p - at[i]).sqrMagnitude < r * r) { best = i; bestR = r; }
            }
            return best;
        }

        int IndexOf(Transform t)
        {
            for (int i = 0; i < reaches.Count; i++) if (reaches[i].t == t) return i;
            return -1;
        }

        /// <summary>Move what is free in space (ships, shots, wreckage) along with whatever it is in.</summary>
        void Carry(Vector3 openSpace)
        {
            CarriedLastFrame = 0;
            // now and then, forget the roots that have gone (shots, wrecks)
            if (++frame % 300 == 0)
            {
                dead.Clear();
                foreach (var k in shifters.Keys) if (k == null) dead.Add(k);
                foreach (var k in dead) shifters.Remove(k);
            }
            var scene = gameObject.scene;
            if (!scene.IsValid()) return;
            scene.GetRootGameObjects(roots);
            var cam = Camera.main != null ? Camera.main.transform.root : null;
            var me = player != null ? player.root : null;
            foreach (var go in roots)
            {
                if (go == null) continue;
                var t = go.transform;
                if (t == system || t == me || t == cam || t == transform) continue;
                Vector3 p = t.position;
                if (p.y < -100000f) continue;          // a planet's surface, far below everything
                int i = -1;
                if (go.TryGetComponent(out OrbitHome home) && home.home != null) i = IndexOf(home.home);
                if (i < 0) i = ReachOf(p, before);
                Vector3 d = i >= 0 ? delta[i] : openSpace;
                if (d.sqrMagnitude < 1e-8f) continue;
                t.position = p + d;
                CarriedLastFrame++;
                // (looked up again when something has been added under it or taken away)
                int count = t.hierarchyCount;
                if (!shifters.TryGetValue(go, out var entry) || entry.count != count)
                {
                    buffer.Clear();
                    go.GetComponentsInChildren(true, buffer);
                    shifters[go] = entry = (buffer.ToArray(), count);
                }
                foreach (var s in entry.list) if (s != null) s.OriginShifted(d);
            }
        }

        // ------------------------------------------------------------------ for the map and the tests

        /// <summary>The orbit a transform (or one of its parents) is on, if any.</summary>
        public static Orbit OrbitOf(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.TryGetComponent(out Orbit o)) return o;
            return null;
        }

        /// <summary>A planet's Lagrange point n (1..5) in the world, now.</summary>
        public Vector3 LagrangePoint(Orbit planet, int n)
        {
            var s = planet.Offset(Time);
            return StarPosition + Orbit.LagrangePoint(s, planet.periapsis, planet.ahead, planet.massRatio, n);
        }
    }
}
