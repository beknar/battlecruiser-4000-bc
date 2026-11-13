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
    /// The system is millions of kilometres across, far more than a float can hold to the
    /// centimetre, so where everything is in the system is worked out in doubles (Double3, the
    /// star at zero) and the world is drawn round the frigate: `Origin` is the system position of
    /// the world's (0, 0, 0). Each frame every orbit's frame is put at its system position less the
    /// origin. The frames are kept side by side at the top of the scene, none inside another, so
    /// what is near the frigate is always near zero and exact.
    ///
    /// The frigate's neighbourhood stays still: whatever "reach" the frigate is in (a planet, a
    /// moon, a station, a field) is held where it is by moving the origin with it, and the rest of
    /// the system moves round it. The frigate keeps the reach it is in until it leaves it or warps
    /// (a moon going past the planet it is by doesn't take it along); arriving somewhere, it takes
    /// the smallest reach it is in. So shots, wreckage, particles and fights near the frigate never
    /// notice the orbits. Anywhere else, ships and shots go along with the body whose reach they
    /// are in (or their home's, for enemies posted somewhere: OrbitHome), and are told how far they
    /// moved (IOriginShift), so an ambush stays in its asteroid field and a patrol on station by
    /// its planet.
    ///
    /// When the frigate has gone more than `recentreDistance` from the world's origin (and all
    /// through a warp), the origin is brought back to it: the frigate, the camera and everything
    /// free in space move back by the same amount (and are told), the orbits' frames are put again.
    /// Down on a planet's surface the surface keeps its own origin, and this waits.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // before the other LateUpdates (the camera follows after)
    public class OrbitalSystem : MonoBehaviour
    {
        /// <summary>The star's gravitational parameter, G times its mass (m^3/s^2).</summary>
        public static double StarGM = 2.839e9;

        public double starGM = 2.839e9;
        [Tooltip("The star system's root: the star is at its origin. The orbits' frames are lifted out of it when the level starts.")]
        public Transform system;
        public Transform player;
        [Tooltip("More reaches: things that move but aren't on an Orbit (a station on a space elevator, turning with its planet).")]
        public Transform[] extraReach;
        public float[] extraReachRadius;
        [Tooltip("How far the frigate may go from the world's origin before the origin is brought back to it (m).")]
        public float recentreDistance = 20000f;

        public static OrbitalSystem Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Orbital time (s): game time since the level started.</summary>
        public double Time { get; private set; }

        /// <summary>The orbits run this many times faster than the game (a time-lapse, for the
        /// reel); 1 normally. Only the orbits: the game's own clock is untouched.</summary>
        [System.NonSerialized] public float timeLapse = 1f;
        /// <summary>Co-op: the room's shared clock (every game in it runs its orbits on the same
        /// time). Null in single-player.</summary>
        public static System.Func<double> ClockOverride;
        double ahead;                        // seconds the orbits have run ahead of the game

        /// <summary>Carry on from this orbit time on our own clock (after the co-op clock stops).</summary>
        public void ContinueFrom(double t) => ahead = t - UnityEngine.Time.timeSinceLevelLoad;
        /// <summary>The reach the frigate is in (held still), or null in open space.</summary>
        public Transform Anchor { get; private set; }
        public int AnchorChanges { get; private set; }
        /// <summary>Things carried along this frame (for the self-test).</summary>
        public int CarriedLastFrame { get; private set; }
        /// <summary>How many times the origin has been brought back to the frigate.</summary>
        public int Recentres { get; private set; }
        /// <summary>The star, in the world.</summary>
        public Vector3 StarPosition => system != null ? system.position : Vector3.zero;

        /// <summary>The system position (star at zero) of the world's origin.</summary>
        public Double3 Origin => origin;
        /// <summary>A world point's position in the system (double: exact anywhere).</summary>
        public Double3 ToSystem(Vector3 world) => origin + world;
        /// <summary>A system position in the world (good near the frigate; far off, as near as a float can be).</summary>
        public Vector3 ToWorld(Double3 sys) => (sys - origin).ToVector3();
        /// <summary>The reach a world point is in (its name), and the point's offset from it; ""
        /// in open space. Co-op games send positions this way: each game draws a place where the
        /// orbits have it on its own clock, and the place carries what is in it.</summary>
        public string FrameOf(Vector3 world, out Vector3 offset)
        {
            var r = ReachAt(world);
            offset = r != null ? world - r.position : Vector3.zero;
            return r != null ? r.name : "";
        }

        /// <summary>A point given by a reach's name and an offset from it, in the world (false if
        /// there is no such reach).</summary>
        public bool WorldOf(string frameName, Vector3 offset, out Vector3 world)
        {
            world = Vector3.zero;
            if (string.IsNullOrEmpty(frameName)) return false;
            foreach (var (t, _) in reaches)
                if (t != null && t.name == frameName) { world = t.position + offset; return true; }
            return false;
        }

        /// <summary>An orbit's system position now.</summary>
        public Double3 SystemPosition(Orbit o) => o != null && index.TryGetValue(o, out int i) ? pos[i] : Double3.zero;

        /// <summary>Something else holds the origin (a planet's surface, with its own): no
        /// bringing it back to the frigate meanwhile.</summary>
        public static bool HoldOrigin;

        /// <summary>How far the system is moved out of the way while the frigate is down on a
        /// planet's surface (built far below it), so none of it is in view from there.</summary>
        public static readonly Vector3 ParkOffset = new Vector3(0f, 4.0e6f, 0f);
        public bool Parked { get; private set; }
        int parkAnchor = -1;

        /// <summary>Move the system out of the way (or back): the orbits' frames and everything
        /// free in space; the frigate with it, unless it is down on the surface (far below).</summary>
        public void Park(bool on)
        {
            if (on == Parked || orbits == null) return;
            Vector3 d = on ? ParkOffset : -ParkOffset;
            parkAnchor = on && Anchor != null ? IndexOf(Anchor) : -1;
            Parked = on;
            origin -= (Double3)d;
            PutFrames();
            for (int i = 0; i < reaches.Count; i++) if (reaches[i].t != null) last[i] = reaches[i].t.position;
            var scene = gameObject.scene;
            if (!scene.IsValid()) return;
            scene.GetRootGameObjects(roots);
            var me = player != null ? player.root : null;
            foreach (var go in roots)
            {
                if (go == null) continue;
                var t = go.transform;
                if (t == system || t == transform || frames.Contains(t) || far.ContainsKey(t)) continue;
                if (AtSurface(t.position)) continue;          // a planet's surface (and the frigate on it)
                if (Camera.main != null && t == Camera.main.transform.root && me != null && AtSurface(me.position)) continue;
                t.position += d;
                Tell(go, d, true);
            }
            Physics.SyncTransforms();
        }

        /// <summary>Told when the origin is brought back to the frigate: everything moved by this.</summary>
        public static event System.Action<Vector3> Recentred;

        /// <summary>Where a planet's surface is being built (PlanetLanding), or null: what is
        /// within 200 km of it is the surface's (it has its own origin), not the system's.</summary>
        public static Vector3? SurfaceAt;
        static bool AtSurface(Vector3 p) => SurfaceAt.HasValue && (p - SurfaceAt.Value).sqrMagnitude < 200000f * 200000f;

        Orbit[] orbits;
        Double3[] pos = new Double3[0], posBefore = new Double3[0];
        int[] parentOf = new int[0];
        readonly Dictionary<Orbit, int> index = new Dictionary<Orbit, int>();
        readonly HashSet<Transform> frames = new HashSet<Transform>();
        Double3 origin;
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
        readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[256];

        /// <summary>Further than this from the world's origin (m), a float can't hold a position to
        /// within a few metres: what is free out there is kept in doubles, in its frame.</summary>
        public const float FarLimit = 1000000f;
        // what is free and far: kept where it is in its frame (`frame` an index into the orbits),
        // or in the system (frame -1: `off` is its system position)
        struct Far { public int frame; public Double3 off; }
        readonly Dictionary<Transform, Far> far = new Dictionary<Transform, Far>();
        readonly List<Transform> nearAgain = new List<Transform>();
        /// <summary>Free things kept far off, in doubles (for the self-test).</summary>
        public int FarCount => far.Count;

        void Awake()
        {
            Instance = this;
            HoldOrigin = false;
            ScaledSpace.Active = true;
            SurfaceAt = null;
            StarGM = starGM;
            if (system == null) system = transform;
            orbits = system.GetComponentsInChildren<Orbit>(true).ToArray();
            // parents before children, so a moon is placed after its planet
            orbits = orbits.OrderBy(Depth).ToArray();
            index.Clear();
            for (int i = 0; i < orbits.Length; i++) index[orbits[i]] = i;
            parentOf = new int[orbits.Length];
            for (int i = 0; i < orbits.Length; i++) parentOf[i] = orbits[i].parent != null && index.TryGetValue(orbits[i].parent, out int p) ? p : -1;
            pos = new Double3[orbits.Length];
            posBefore = new Double3[orbits.Length];
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
            // The level is laid out small (everything within a few hundred kilometres of the star,
            // where a float is exact); the orbits' sizes are the real ones. Each frame goes to its
            // real place, and what is free (ships) goes along with the frame it was laid out in
            // (its home's, or the smallest reach it was in), kept there exactly in doubles.
            var authored = new Vector3[orbits.Length];
            for (int i = 0; i < orbits.Length; i++) authored[i] = orbits[i].transform.position;
            Vector3 star = system.position;
            var laidOut = new List<(Transform t, int frame, Double3 off)>();
            var scene = gameObject.scene;
            if (scene.IsValid())
            {
                scene.GetRootGameObjects(roots);
                foreach (var go in roots)
                {
                    var t = go.transform;
                    if (t == system || t == transform) continue;
                    int fi = HomeFrame(go, authored);
                    laidOut.Add((t, fi, fi >= 0 ? (Double3)(t.position - authored[fi]) : (Double3)(t.position - star)));
                }
            }
            // lift every frame out to the top of the scene, side by side (a frame inside another,
            // far off, would be drawn only as well as a float holds that far)
            frames.Clear();
            foreach (var o in orbits)
            {
                o.transform.SetParent(null, true);
                frames.Add(o.transform);
            }
            // the world drawn round the frigate: its frame where it was laid out
            origin = -(Double3)star;
            for (int i = 0; i < orbits.Length; i++) pos[i] = Double3.zero;
            Place(0.0);
            int mine = -1;
            if (player != null) foreach (var l in laidOut) if (l.t == player.root) mine = l.frame;
            if (mine >= 0) origin = pos[mine] - (Double3)authored[mine];
            PutFrames();
            Debug.Log($"[OrbitalSystem] laid out {laidOut.Count} free things; the frigate ({(player != null ? player.root.name : "none")}) in " +
                      $"{(mine >= 0 ? orbits[mine].name : "no frame")}, authored at {(player != null ? player.root.position : Vector3.zero)}");
            foreach (var (t, fi, off) in laidOut)
            {
                if (t == null) continue;
                Double3 sys = fi >= 0 ? pos[fi] + off : off;
                Vector3 now = (sys - origin).ToVector3();
                Vector3 moved = now - t.position;
                t.position = now;
                if (moved.sqrMagnitude > 1e-6f) Tell(t.gameObject, moved, true);
                // (the frigate and the camera are never far: the world is drawn round them)
                bool mineOrCam = (player != null && t == player.root) || (Camera.main != null && t == Camera.main.transform.root);
                if (!mineOrCam && now.sqrMagnitude > FarLimit * FarLimit) far[t] = new Far { frame = fi, off = off };
            }
            // the planets and moons turn by the orbit clock (so a geostationary station, and the
            // ground, are where they are in every game sharing the clock)
            foreach (var o in orbits)
                foreach (var d in o.GetComponentsInChildren<Drift>(true))
                    if (d.GetComponent<WarpPoint>() != null) d.LockToOrbitClock();
            for (int i = 0; i < reaches.Count; i++) last[i] = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The frame something free was laid out in: its home's, or the smallest reach it
        /// was in, or else the nearest frame (-1: none).</summary>
        int HomeFrame(GameObject go, Vector3[] authored)
        {
            if (go.TryGetComponent(out OrbitHome home) && home.home != null)
            {
                var o = OrbitOf(home.home);
                if (o != null && index.TryGetValue(o, out int hi)) return hi;
            }
            Vector3 p = go.transform.position;
            int best = -1;
            float bestR = float.MaxValue;
            for (int i = 0; i < orbits.Length; i++)
            {
                float r = orbits[i].reach;
                if (r > 0f && r < bestR && (p - authored[i]).sqrMagnitude < r * r) { best = i; bestR = r; }
            }
            if (best >= 0) return best;
            float bestD = float.MaxValue;
            for (int i = 0; i < orbits.Length; i++)
            {
                float d = (p - authored[i]).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        static int Depth(Orbit o)
        {
            int d = 0;
            for (var p = o.parent; p != null; p = p.parent) d++;
            if (o.kind == Orbit.Kind.Lagrange && o.secondary != null) d = Mathf.Max(d, Depth(o.secondary) + 1);
            return d;
        }

        public IReadOnlyList<Orbit> Orbits => orbits;

        /// <summary>Every orbit's system position at time t, and its frame put there in the world.</summary>
        void Place(double t)
        {
            for (int i = 0; i < orbits.Length; i++)
            {
                var o = orbits[i];
                if (o == null) continue;
                Double3 off = o.OffsetD(t);
                pos[i] = parentOf[i] >= 0 ? pos[parentOf[i]] + off : off;
            }
            PutFrames();
        }

        void PutFrames()
        {
            for (int i = 0; i < orbits.Length; i++)
            {
                var o = orbits[i];
                if (o == null) continue;
                o.Put((pos[i] - origin).ToVector3(), parentOf[i] >= 0 ? pos[i] - pos[parentOf[i]] : pos[i]);
            }
            if (system != null) system.position = (-origin).ToVector3();
        }

        void LateUpdate()
        {
            if (orbits == null) return;
            if (timeLapse != 1f) ahead += UnityEngine.Time.deltaTime * (timeLapse - 1f);
            Time = ClockOverride != null ? ClockOverride() : UnityEngine.Time.timeSinceLevelLoad + ahead;

            for (int i = 0; i < reaches.Count; i++) before[i] = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
            Double3 originBefore = origin;
            for (int i = 0; i < orbits.Length; i++) posBefore[i] = pos[i];
            bool warping = drive != null && drive.Current == WarpDrive.State.Warping;
            int anchor = -1;
            if (Parked) anchor = parkAnchor;       // (held where it was while the frigate is down)
            else if (player != null && !warping)
            {
                // Millions of kilometres out every place goes round at up to several km/s, and the
                // frigate shares the motion of the place it is at: the smallest place within three
                // times its reach (a station orbiting a planet, rather than the planet: a fight round
                // the station stays round it), kept while within five times it unless a smaller one
                // comes that near; and with no place that near, the nearest (relative to its reach),
                // till the next warp
                int now = Anchor != null ? IndexOf(Anchor) : -1;
                Vector3 p = player.position;
                int near = -1;
                float nearR = float.MaxValue;
                for (int i = 0; i < reaches.Count; i++)
                {
                    if (reaches[i].t == null) continue;
                    float r = reaches[i].r;
                    if (r < nearR && (p - before[i]).sqrMagnitude < 9f * r * r) { near = i; nearR = r; }
                }
                bool keep = now >= 0 && reaches[now].t != null && (p - before[now]).sqrMagnitude < 25f * reaches[now].r * reaches[now].r;
                if (near >= 0 && (!keep || nearR < reaches[now].r)) anchor = near;
                else if (keep) anchor = now;
                else
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < reaches.Count; i++)
                    {
                        if (reaches[i].t == null) continue;
                        float k = (p - before[i]).magnitude / reaches[i].r;
                        if (k < best) { best = k; anchor = i; }
                    }
                }
            }

            // everything where it is now, round the origin as it was
            Place(Time);

            // hold the frigate's neighbourhood still: move the origin with it (its orbit, and the
            // turning of a planet it is on: a geostationary station)
            Vector3 shift = anchor >= 0 && reaches[anchor].t != null ? last[anchor] - reaches[anchor].t.position : Vector3.zero;
            // and bring the origin back to the frigate if it has strayed (or is in warp)
            Vector3 back = Vector3.zero;
            if (player != null && !HoldOrigin)
            {
                float limit = warping ? 1000f : recentreDistance;
                if (player.position.sqrMagnitude > limit * limit) back = -player.position;
            }
            if (shift != Vector3.zero || back != Vector3.zero)
            {
                origin -= (Double3)(shift + back);
                PutFrames();
            }
            var a = anchor >= 0 ? reaches[anchor].t : null;
            if (a != Anchor) { Anchor = a; AnchorChanges++; }
            for (int i = 0; i < reaches.Count; i++)
            {
                Vector3 now = reaches[i].t != null ? reaches[i].t.position : Vector3.zero;
                delta[i] = now - last[i];       // (with the origin coming back in it, if it did)
                last[i] = now;
            }

            Carry(shift + back, back, originBefore);
            if (back != Vector3.zero) Recentre(back);

            Physics.SyncTransforms();
        }

        /// <summary>The frigate and the camera brought back with the origin, and told.</summary>
        void Recentre(Vector3 d)
        {
            Recentres++;
            var cam = Camera.main != null ? Camera.main.transform.root : null;
            var me = player != null ? player.root : null;
            foreach (var t in cam != me ? new[] { me, cam } : new[] { me })   // (a camera on the ship goes with it)
            {
                if (t == null) continue;
                t.position += d;
                Tell(t.gameObject, d, true);
            }
            Recentred?.Invoke(d);
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

        /// <summary>Move what is free in space (ships, shots, wreckage) along with whatever it is in
        /// (`openSpace` where it is in nothing). `back`: the origin came back by this, so what
        /// moved is told even if its reach held it still.</summary>
        void Carry(Vector3 openSpace, Vector3 back, Double3 originBefore)
        {
            // what is kept far off: where it is in its frame (or the system), exactly
            nearAgain.Clear();
            foreach (var kv in far)
            {
                var t = kv.Key;
                if (t == null) { nearAgain.Add(t); continue; }
                var e = kv.Value;
                Double3 sys = e.frame >= 0 ? pos[e.frame] + e.off : e.off;
                Vector3 now = (sys - origin).ToVector3();
                Vector3 moved = now - t.position;
                t.position = now;
                if (moved.sqrMagnitude > 1e-6f) Tell(t.gameObject, moved, moved.sqrMagnitude > 1e6f);
                if (now.sqrMagnitude < FarLimit * FarLimit * 0.81f) nearAgain.Add(t);
            }
            foreach (var t in nearAgain) far.Remove(t);

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
                if (t == system || t == me || t == cam || t == transform || frames.Contains(t) || far.ContainsKey(t)) continue;
                Vector3 p = t.position;
                if (AtSurface(p)) continue;          // a planet's surface (it has its own origin)
                int i = -1;
                if (go.TryGetComponent(out OrbitHome home) && home.home != null) i = IndexOf(home.home);
                if (i < 0) i = ReachOf(p, before);
                Vector3 d = i >= 0 ? delta[i] : openSpace;
                if (d.sqrMagnitude < 1e-8f) continue;
                if ((p + d).sqrMagnitude > FarLimit * FarLimit)
                {
                    // going far off (the frigate has warped away): kept from here on in doubles, in
                    // its reach (or the system), from where it is now, while that is still exact
                    int fi = i >= 0 && i < firstExtra ? index[reaches[i].t.GetComponent<Orbit>()] : -1;
                    var e = fi >= 0 ? new Far { frame = fi, off = (Double3)(p - (posBefore[fi] - originBefore).ToVector3()) }
                                    : new Far { frame = -1, off = originBefore + p };
                    far[t] = e;
                    Double3 sys = e.frame >= 0 ? pos[e.frame] + e.off : e.off;
                    Vector3 now = (sys - origin).ToVector3();
                    d = now - p;
                    t.position = now;
                    CarriedLastFrame++;
                    Tell(go, d, true);
                    continue;
                }
                t.position = p + d;
                CarriedLastFrame++;
                Tell(go, d, back != Vector3.zero);
            }
        }

        /// <summary>Tell what is under a root that it moved by `d` (and, after a big move, take its
        /// world-space particles and trails along).</summary>
        void Tell(GameObject go, Vector3 d, bool big)
        {
            // (looked up again when something has been added under it or taken away)
            int count = go.transform.hierarchyCount;
            if (!shifters.TryGetValue(go, out var entry) || entry.count != count)
            {
                buffer.Clear();
                go.GetComponentsInChildren(true, buffer);
                shifters[go] = entry = (buffer.ToArray(), count);
            }
            foreach (var s in entry.list) if (s != null) s.OriginShifted(d);
            if (!big) return;
            particles.Clear();
            go.GetComponentsInChildren(false, particles);
            foreach (var ps in particles) ShiftParticles(ps, d);
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(false)) tr.Clear();
            foreach (var lr in go.GetComponentsInChildren<LineRenderer>(false))
                if (lr.useWorldSpace)
                    for (int k = 0; k < lr.positionCount; k++) lr.SetPosition(k, lr.GetPosition(k) + d);
        }

        void ShiftParticles(ParticleSystem ps, Vector3 d)
        {
            if (ps == null || ps.main.simulationSpace != ParticleSystemSimulationSpace.World) return;
            int n = ps.particleCount;
            if (n == 0) return;
            if (particleBuffer.Length < n) particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];
            n = ps.GetParticles(particleBuffer, n);
            for (int k = 0; k < n; k++) particleBuffer[k].position += d;
            ps.SetParticles(particleBuffer, n);
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
            var s = planet.OffsetD(Time);
            return ToWorld(Orbit.LagrangePointD(s, planet.periapsis, planet.ahead, planet.massRatio, n));
        }
    }
}
