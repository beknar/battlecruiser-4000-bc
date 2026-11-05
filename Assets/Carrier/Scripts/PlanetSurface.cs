using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>Something that keeps world positions of its own and must hear when the world
    /// down on a planet is shifted back toward the middle (PlanetSurface's floating origin).</summary>
    public interface IOriginShift
    {
        void OriginShifted(Vector3 delta);
    }

    /// <summary>
    /// The ground of a planet, built around the ship as it flies (one SurfaceWorld area at a
    /// time). Areas within `nearRadius` are built in full (10 m heights, colliders, boulders,
    /// landing pads and, on a hostile world, its ground forces); areas out to `farRadius` in less
    /// detail, for the view. An area is thrown away once the ship is `unloadRadius` (20 km) from
    /// it, and built again, the same as before, if the ship comes back. The areas are worked out
    /// on worker threads (or a few rows a frame in a web page) and put in place one a frame.
    ///
    /// The world down here never drifts far from `center`: when the ship gets more than
    /// `shiftDistance` from it, everything nearby (the ground, the ship, shots, wreckage) is moved
    /// back by a whole number of areas, and the planet position of `center` moves the other way.
    /// So the ship can fly all the way round a planet without the numbers getting large.
    /// </summary>
    [DefaultExecutionOrder(310)]
    public class PlanetSurface : MonoBehaviour
    {
        public float nearRadius = 5000f;
        public float farRadius = 15000f;
        public float unloadRadius = 20000f;
        public float shiftDistance = 7500f;

        /// <summary>Off until the ship is down here (it is still in orbit while the first areas are built).</summary>
        public bool Running { get; set; }

        public SurfaceWorld World { get; private set; }
        public SurfaceKit Kit { get; private set; }
        public Transform Ship { get; private set; }
        /// <summary>The middle of the world down here (fixed).</summary>
        public Vector3 Center { get; private set; }
        /// <summary>The planet position (unwrapped) at Center.</summary>
        public double OriginU { get; private set; }
        public double OriginV { get; private set; }

        /// <summary>Everything nearby has just been moved by this much (floating origin).</summary>
        public event System.Action<Vector3> Shifted;
        public int Shifts { get; private set; }
        public Vector3 TotalShift { get; private set; }

        /// <summary>Areas of the planet built at full detail at least once (by index), out of World.AreaCount.</summary>
        public HashSet<int> Explored { get; private set; }
        /// <summary>Ground forces destroyed on this planet (they stay destroyed).</summary>
        public HashSet<long> Destroyed { get; private set; }

        public int TilesLoaded => tiles.Count;
        public int TilesBuilt { get; private set; }
        public int TilesUnloaded { get; private set; }
        public float LastJobSeconds { get; private set; }
        public float LastApplyMs { get; private set; }

        float T => SurfaceWorld.T;
        int layer;

        class Tile
        {
            public int I, J, lod, ci, cj;
            public bool flipped;
            public GameObject go;
            public Terrain terrain;
            public TerrainData data;
            public TileFeatures features;
            public float maxHeight;
            public Transform outpost;
            public GroundGarrison garrison;
        }

        readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        // which built copy of each area holds its ground forces: near a pole the same area can be
        // built twice (once each side of it), and its forces must only be there once
        readonly Dictionary<int, Tile> garrisonOf = new Dictionary<int, Tile>();
        readonly Dictionary<(long, int), TileJob> jobs = new Dictionary<(long, int), TileJob>();
        readonly List<TileJob> ready = new List<TileJob>();
        TileJob webJob;
        IEnumerator<int> webSteps;
        int maxJobs;
        float nextPlan;
        bool ringDirty = true;
        Vector3 ringAt = new Vector3(float.NaN, 0f, 0f);

        Transform sea, ring, sky;
        Mesh ringMesh;
        Material ringMat;
        readonly List<Object> owned = new List<Object>();

        static bool Threads =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            true;
#endif

        static long Key(int I, int J) => ((long)I << 32) ^ (uint)J;

        // ------------------------------------------------------------------ making one

        public static PlanetSurface Create(SurfaceWorld world, SurfaceKit kit, Transform ship, Vector3 center, double u, double v,
                                           HashSet<int> explored, HashSet<long> destroyed, Material skyPlanet)
        {
            var go = new GameObject($"Surface of {world.name}");
            go.transform.position = center;
            var s = go.AddComponent<PlanetSurface>();
            s.World = world;
            s.Kit = kit;
            s.Ship = ship;
            s.Center = center;
            s.OriginU = u;
            s.OriginV = v;
            s.Explored = explored ?? new HashSet<int>();
            s.Destroyed = destroyed ?? new HashSet<long>();
            s.layer = LayerMask.NameToLayer(Cover.LayerName);
            if (s.layer < 0) s.layer = 0;
            s.maxJobs = Threads ? Mathf.Clamp(SystemInfo.processorCount - 2, 1, 4) : 1;
            s.BuildScenery(skyPlanet);
            return s;
        }

        void OnDestroy()
        {
            foreach (var j in jobs.Values) j.cancelled = true;
            jobs.Clear();
            foreach (var j in ready) j.Release();
            ready.Clear();
            foreach (var t in tiles.Values) DestroyTile(t);
            tiles.Clear();
            garrisonOf.Clear();
            TileJob.TrimPool();
            foreach (var o in owned) if (o != null) Destroy(o);
            owned.Clear();
        }

        // ------------------------------------------------------------------ where

        /// <summary>The unwrapped planet position of a world point.</summary>
        public void PlanetOf(Vector3 p, out double u, out double v)
        {
            u = OriginU + (p.x - Center.x);
            v = OriginV + (p.z - Center.z);
        }

        public Vector3 WorldOf(double u, double v, float height) =>
            new Vector3(Center.x + (float)(u - OriginU), Center.y + height, Center.z + (float)(v - OriginV));

        public void TileOf(Vector3 p, out int I, out int J)
        {
            PlanetOf(p, out double u, out double v);
            I = (int)System.Math.Floor(u / T);
            J = (int)System.Math.Floor((v + World.V) / T);
        }

        Vector3 Corner(int I, int J) => WorldOf(I * (double)T, J * (double)T - World.V, SurfaceWorld.BaseLevel);

        /// <summary>Latitude and longitude (degrees) under a world point.</summary>
        public void LatLon(Vector3 p, out float lat, out float lon)
        {
            PlanetOf(p, out double u, out double v);
            World.LatLon(u, v, out lat, out lon);
        }

        /// <summary>The index of the planet's area under a world point (the same all the way round).</summary>
        public int AreaIndex(Vector3 p)
        {
            TileOf(p, out int I, out int J);
            World.CanonicalTile(I, J, out int ci, out int cj);
            return ci * World.M + cj;
        }

        Tile TileUnder(Vector3 p)
        {
            TileOf(p, out int I, out int J);
            return tiles.TryGetValue(Key(I, J), out var t) ? t : null;
        }

        /// <summary>The ground's world height under a point (an average where nothing is built yet).</summary>
        public float GroundAt(Vector3 p)
        {
            var t = TileUnder(p);
            if (t != null && t.terrain != null) return t.terrain.transform.position.y + t.terrain.SampleHeight(p);
            return Center.y + World.edgeLevel;
        }

        /// <summary>The terrain of the area under a point (null if not built).</summary>
        public Terrain TerrainAt(Vector3 p) => TileUnder(p)?.terrain;

        /// <summary>Is the area under a point built (at full detail with `full`)?</summary>
        public bool Ready(Vector3 p, bool full = true)
        {
            var t = TileUnder(p);
            return t != null && (!full || t.lod == 0);
        }

        /// <summary>The world height nothing can go below: ground, sea, lava or cloud deck.</summary>
        public float FloorAt(Vector3 p)
        {
            float f = Mathf.Max(SeaLevel, CloudFloor);
            if (World.solid) f = Mathf.Max(f, GroundAt(p));
            return f;
        }

        public float SeaLevel => float.IsNegativeInfinity(World.seaLevel) ? float.NegativeInfinity : Center.y + World.seaLevel;
        public float CloudFloor => World.solid ? float.NegativeInfinity : Center.y + World.floorLevel;

        /// <summary>The highest ground in the areas built so far.</summary>
        public float HighestGround
        {
            get
            {
                float h = Center.y + World.edgeLevel;
                foreach (var t in tiles.Values) h = Mathf.Max(h, Center.y + t.maxHeight);
                return h;
            }
        }

        /// <summary>The landing pad nearest a point, among the areas built (null if none).</summary>
        public Transform NearestOutpost(Vector3 p, out float distance)
        {
            Transform best = null;
            distance = float.MaxValue;
            foreach (var t in tiles.Values)
            {
                if (t.outpost == null) continue;
                float d = Vector3.Distance(t.outpost.position, p);
                if (d < distance) { distance = d; best = t.outpost; }
            }
            return best;
        }

        /// <summary>Areas built twice in full detail (one copy each side of a pole), and how many
        /// of them have their ground forces in both copies (there should be none).</summary>
        public int TwinAreasBuilt => CountTwins(false);
        public int GarrisonCopies => CountTwins(true);

        int CountTwins(bool forces)
        {
            var seen = new HashSet<int>();
            int n = 0;
            foreach (var t in tiles.Values)
            {
                if (t.lod != 0 || (forces && t.garrison == null)) continue;
                if (!seen.Add(t.ci * World.M + t.cj)) n++;
            }
            return n;
        }

        public IEnumerable<GroundGarrison> Garrisons
        {
            get { foreach (var t in tiles.Values) if (t.garrison != null) yield return t.garrison; }
        }

        // ------------------------------------------------------------------ streaming

        /// <summary>Build the areas within `radius` of a point straight away (behind the screen
        /// fade on landing), at full detail. Takes at most `maxSeconds`.</summary>
        public IEnumerator Preload(Vector3 p, float radius, float maxSeconds)
        {
            float until = Time.realtimeSinceStartup + maxSeconds;
            var want = new List<(int, int)>();
            TileOf(p, out int I0, out int J0);
            for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                    if (DistanceToTile(p, I0 + di, J0 + dj) < radius) want.Add((I0 + di, J0 + dj));
            want.Sort((a, b) => DistanceToTile(p, a.Item1, a.Item2).CompareTo(DistanceToTile(p, b.Item1, b.Item2)));
            var list = new List<TileJob>();
            foreach (var (i, j) in want)
            {
                var job = new TileJob(World, i, j, 0);
                list.Add(job);
                if (Threads) System.Threading.ThreadPool.QueueUserWorkItem(_ => job.RunAll());
            }
            foreach (var job in list)
            {
                if (!Threads)
                {
                    // no threads in a web page: work it out now (the screen is faded out)
                    var it = job.Steps();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (true)
                    {
                        try { if (!it.MoveNext()) break; }
                        catch (System.Exception e) { job.error = e; break; }
                        if (sw.ElapsedMilliseconds > 200) { yield return null; sw.Restart(); }
                    }
                    job.done = true;
                }
                while (!job.done && Time.realtimeSinceStartup < until) yield return null;
                if (!job.done) { job.cancelled = true; continue; }   // it gives its buffers back when it stops
                if (job.error != null) { Debug.LogError($"[PlanetSurface] {World.name}: working out area {job.I},{job.J} ({job.ci},{job.cj}) failed: {job.error}"); job.Release(); continue; }
                Apply(job);
            }
            nextPlan = 0f;
        }

        void Update() => Tick();

        void Tick()
        {
            if (Ship == null || World == null || !Running) return;
            ShiftIfFar();
            CollectJobs();
            // put a finished area in place: one in full detail a frame, or a few distant ones
            if (ready.Count > 0)
            {
                Vector3 sp = Ship.position;
                ready.Sort((a, b) => DistanceToTile(sp, a.I, a.J).CompareTo(DistanceToTile(sp, b.I, b.J)));
                int budget = 3;
                while (ready.Count > 0 && budget > 0)
                {
                    var job = ready[0];
                    ready.RemoveAt(0);
                    if (job.error != null) { Debug.LogError($"[PlanetSurface] {World.name}: working out area {job.I},{job.J} ({job.ci},{job.cj}) failed: {job.error}"); job.Release(); continue; }
                    if (DistanceToTile(sp, job.I, job.J) > unloadRadius) { job.Release(); continue; }
                    if (tiles.TryGetValue(Key(job.I, job.J), out var have) && have.lod <= job.lod) { job.Release(); continue; }
                    Apply(job);
                    budget -= job.lod == 0 ? 3 : 1;
                }
            }
            if (Time.time >= nextPlan)
            {
                nextPlan = Time.time + 0.25f;
                Plan();
            }
            Follow();
        }

        /// <summary>How far a point is from the nearest edge of an area (0 inside it), across the ground.</summary>
        float DistanceToTile(Vector3 p, int I, int J)
        {
            Vector3 c = Corner(I, J);
            float dx = Mathf.Max(c.x - p.x, 0f, p.x - (c.x + T));
            float dz = Mathf.Max(c.z - p.z, 0f, p.z - (c.z + T));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Throw away the areas the ship has left behind, and start building the ones it
        /// is coming up to, nearest first.</summary>
        void Plan()
        {
            Vector3 p = Ship.position;
            var drop = new List<long>();
            foreach (var kv in tiles)
                if (DistanceToTile(p, kv.Value.I, kv.Value.J) > unloadRadius) drop.Add(kv.Key);
            foreach (var k in drop)
            {
                var gone = tiles[k];
                DestroyTile(gone);
                tiles.Remove(k);
                HandOverGarrison(gone);
                TilesUnloaded++;
                ringDirty = true;
            }
            var stale = new List<(long, int)>();
            foreach (var kv in jobs)
                if (DistanceToTile(p, kv.Value.I, kv.Value.J) > unloadRadius) { kv.Value.cancelled = true; stale.Add(kv.Key); }
            foreach (var k in stale)
            {
                var sj = jobs[k];
                jobs.Remove(k);
                // a web job only runs when stepped, so a cancelled one has stopped; a thread's job
                // gives its buffers back itself when it stops (or now, if it already has)
                if (!Threads) sj.done = true;
                sj.Release();
            }
            if (webJob != null && webJob.cancelled) { webJob = null; webSteps = null; }

            TileOf(p, out int I0, out int J0);
            int reach = Mathf.CeilToInt(farRadius / T) + 1;
            var wanted = new List<(float, int, int, int)>();
            for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    int I = I0 + di, J = J0 + dj;
                    float d = DistanceToTile(p, I, J);
                    int lod = d < nearRadius ? 0 : d < farRadius ? 1 : -1;
                    if (lod < 0) continue;
                    if (tiles.TryGetValue(Key(I, J), out var have) && have.lod <= lod) continue;
                    if (jobs.ContainsKey((Key(I, J), lod))) continue;
                    if (lod == 1 && jobs.ContainsKey((Key(I, J), 0))) continue;
                    if (ready.Exists(r => r.I == I && r.J == J && r.lod <= lod)) continue;
                    wanted.Add((d + (lod == 0 ? 0f : 2500f), I, J, lod));
                }
            wanted.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            foreach (var (_, I, J, lod) in wanted)
            {
                if (jobs.Count >= maxJobs) break;
                var job = new TileJob(World, I, J, lod);
                jobs[(Key(I, J), lod)] = job;
                if (Threads) System.Threading.ThreadPool.QueueUserWorkItem(_ => job.RunAll());
            }
        }

        void CollectJobs()
        {
            if (!Threads)
            {
                // no threads in a web page: a few milliseconds of the current job each frame
                if (webJob == null)
                    foreach (var j in jobs.Values) { if (!j.done && !j.cancelled) { webJob = j; webSteps = j.Steps(); break; } }
                if (webJob != null)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        while (sw.ElapsedMilliseconds < 6)
                            if (!webSteps.MoveNext()) { webJob.done = true; break; }
                    }
                    catch (System.Exception e) { webJob.error = e; webJob.done = true; }
                    if (webJob.done) { webJob = null; webSteps = null; }
                }
            }
            List<(long, int)> finished = null;
            foreach (var kv in jobs)
                if (kv.Value.done) (finished ??= new List<(long, int)>()).Add(kv.Key);
            if (finished == null) return;
            foreach (var k in finished)
            {
                var j = jobs[k];
                jobs.Remove(k);
                if (!j.cancelled) ready.Add(j);
                else j.Release();
            }
        }

        // ------------------------------------------------------------------ one area

        /// <summary>What Apply is doing (named in the log if it fails).</summary>
        string stage = "";

        void Apply(TileJob job)
        {
            try { ApplyNow(job); }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlanetSurface] {World.name}: area {job.I},{job.J} ({job.ci},{job.cj}, detail {job.lod}) failed at '{stage}': {e}");
            }
        }

        void ApplyNow(TileJob job)
        {
            stage = "terrain";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long key = Key(job.I, job.J);
            if (tiles.TryGetValue(key, out var old))
            {
                DestroyTile(old);
                tiles.Remove(key);
                HandOverGarrison(old);
            }
            var w = World;
            var td = new TerrainData();
            td.heightmapResolution = job.res;
            td.size = new Vector3(T, SurfaceWorld.Range, T);
            td.SetHeights(0, 0, job.norm);
            td.alphamapResolution = job.splatRes;
            td.baseMapResolution = job.lod == 0 ? 512 : 128;
            var layers = SurfaceGenerator.Layers(w.kind);
            td.terrainLayers = layers;
            td.SetAlphamaps(0, 0, job.splat);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Area {job.ci},{job.cj}{(job.flipped ? " (over the pole)" : "")}{(job.lod == 0 ? "" : " (far)")}";
            go.layer = layer;
            go.transform.SetParent(transform, false);
            go.transform.position = Corner(job.I, job.J);
            var terrain = go.GetComponent<Terrain>();
            if (Kit.terrain != null) terrain.materialTemplate = Kit.terrain;
            terrain.heightmapPixelError = job.lod == 0 ? 4f : 10f;
            terrain.basemapDistance = 3000f;
            terrain.shadowCastingMode = job.lod == 0 ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            terrain.drawTreesAndFoliage = false;
            terrain.groupingID = 100 + job.lod;    // full-detail areas join up with each other, distant ones likewise
            terrain.allowAutoConnect = true;
            var tc = go.GetComponent<TerrainCollider>();
            bool solid = w.solid && job.lod == 0;
            if (tc != null) tc.enabled = solid;
            if (solid)
            {
                // so the hull collides with it (Cover.Active); not cover, and no core sphere for pilots
                var ob = go.AddComponent<Obstacle>();
                ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f;
            }
            float hi = job.maxHeight;
            var tile = new Tile
            {
                I = job.I, J = job.J, lod = job.lod, ci = job.ci, cj = job.cj, flipped = job.flipped,
                go = go, terrain = terrain, data = td, features = job.ctx[4], maxHeight = hi,
            };
            tiles[key] = tile;
            if (job.lod == 0)
            {
                Explored.Add(job.ci * w.M + job.cj);
                stage = "furnish";
                Furnish(tile);
            }
            TilesBuilt++;
            LastJobSeconds = job.seconds;
            job.Release();
            ringDirty = true;
            LastApplyMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void DestroyTile(Tile t)
        {
            if (t.go != null) Destroy(t.go);
            if (t.data != null) Destroy(t.data);
        }

        /// <summary>Where a planet position in this area's own (canonical) coordinates is, in the
        /// world: over a pole the area is turned round.</summary>
        Vector3 Place(Tile t, double cu, double cv, out float yawTurn)
        {
            float lx = (float)SurfaceWorld.WrapDelta(cu - t.ci * (double)T, World.C);
            float lz = (float)(cv - (t.cj * (double)T - World.V));
            if (lx < -T * 0.5f) lx += (float)World.C;
            yawTurn = 0f;
            if (t.flipped) { lx = T - lx; lz = T - lz; yawTurn = 180f; }
            Vector3 c = t.go.transform.position;
            var p = new Vector3(c.x + lx, 0f, c.z + lz);
            p.y = t.terrain.transform.position.y + t.terrain.SampleHeight(p);
            return p;
        }

        /// <summary>A full-detail area's boulders, landing pad and ground forces.</summary>
        void Furnish(Tile t)
        {
            var w = World;
            var f = t.features;
            var root = t.go.transform;
            stage = "pad";
            // the landing pad (a base's launch pad on a hostile world)
            if (f != null && f.site != SiteKind.None && Kit.outpost != null)
            {
                Vector3 p = Place(t, f.su, f.sv, out float turn);
                p.y = Center.y + f.siteHeight;
                float lift = w.kind == SurfaceKind.GasGiant ? w.platformLift : 0f;
                var o = Instantiate(Kit.outpost, root);
                o.name = f.site == SiteKind.Base ? "Outpost (hostile base)" : "Outpost";
                o.transform.SetPositionAndRotation(p + Vector3.up * (lift + SurfaceWorld.PadLift), Quaternion.Euler(0f, f.siteYaw + turn, 0f));
                foreach (var c in o.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
                if (o.GetComponent<Obstacle>() == null) { var ob = o.AddComponent<Obstacle>(); ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f; }
                if (f.site == SiteKind.Base)
                    foreach (Transform c in o.transform)
                        if (c.name == "Parked craft") Destroy(c.gameObject);    // room for its own ships
                t.outpost = o.transform;
            }

            stage = "boulders";
            // boulders
            var rng = new System.Random(f != null ? (f.i * 7919 + f.j * 104729 + w.seed) : 1);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            if (w.boulders > 0 && Kit.rock != null)
            {
                var rockMat = RockMaterial();
                var rocks = new GameObject("Boulders").transform;
                rocks.SetParent(root, false);
                float slopeLimit = Mathf.Atan(w.boulderSlope) * Mathf.Rad2Deg;
                for (int i = 0, tries = 0; i < w.boulders && tries < w.boulders * 4; tries++)
                {
                    double cu = t.ci * (double)T + Rn(60f, T - 60f), cv = t.cj * (double)T - w.V + Rn(60f, T - 60f);
                    if (Mathf.Abs((float)cv) > w.V - w.polarBand) continue;
                    if (f != null && f.site != SiteKind.None && Dist(cu, cv, f.su, f.sv) < SurfaceWorld.PadRadius + 140f) continue;
                    bool nearUnit = false;
                    if (f != null) foreach (var us in f.units) if (Dist(cu, cv, us.u, us.v) < 60f) { nearUnit = true; break; }
                    float size = Mathf.Lerp(w.boulderSize.x, w.boulderSize.y, Mathf.Pow(Rn(0f, 1f), 2.2f));
                    float yaw = Rn(0f, 360f), flat = Rn(0.55f, 0.9f);
                    if (nearUnit) continue;
                    Vector3 p = Place(t, cu, cv, out _);
                    if (p.y < SeaLevel + 2f) continue;
                    var n = (p - t.go.transform.position) / T;
                    if (t.data.GetSteepness(Mathf.Clamp01(n.x), Mathf.Clamp01(n.z)) > slopeLimit) continue;
                    SurfaceGenerator.Boulder(rocks, p + Vector3.down * size * 0.3f, size, 600 + (i % 6), rockMat, layer, yaw, flat);
                    i++;
                }
            }
            stage = "floating rocks";
            // the rest of an asteroid field, drifting overhead
            if (w.floatingRocks > 0 && Kit.rock != null)
            {
                var rockMat = RockMaterial();
                for (int i = 0; i < w.floatingRocks; i++)
                {
                    var pos = t.go.transform.position + new Vector3(Rn(0f, T), 0f, Rn(0f, T));
                    pos.y = Center.y + Rn(1500f, 5500f);
                    SurfaceGenerator.Boulder(root, pos, Rn(60f, 380f), 620 + (i % 5), rockMat, layer, Rn(0f, 360f), Rn(0.6f, 0.95f))
                        .AddComponent<Drift>().spin = new Vector3(Rn(-2f, 2f), Rn(-2f, 2f), Rn(-2f, 2f));
                }
            }
            stage = "ground forces";
            // ground forces
            Garrison(t);
        }

        /// <summary>Put an area's ground forces in it, unless another built copy of the same area
        /// (over a pole) already has them.</summary>
        void Garrison(Tile t)
        {
            var f = t.features;
            if (f == null || f.units.Count == 0 || t.go == null) return;
            int area = t.ci * World.M + t.cj;
            if (garrisonOf.TryGetValue(area, out var holder) && holder != t && holder.go != null) return;
            garrisonOf[area] = t;
            t.garrison = GroundForces.Deploy(this, t.go.transform, t.outpost, f, (cu, cv) => { var p = Place(t, cu, cv, out float turn); return (p, turn); },
                                             (long)area << 8);
        }

        /// <summary>The copy of an area that held its forces has gone: if another copy of it is
        /// still built in full, the forces (those not destroyed) move there.</summary>
        void HandOverGarrison(Tile gone)
        {
            int area = gone.ci * World.M + gone.cj;
            if (!garrisonOf.TryGetValue(area, out var holder) || holder != gone) return;
            garrisonOf.Remove(area);
            foreach (var t in tiles.Values)
                if (t != gone && t.lod == 0 && t.ci == gone.ci && t.cj == gone.cj) { Garrison(t); break; }
        }

        static float Dist(double a, double b, double c, double d) => (float)System.Math.Sqrt((a - c) * (a - c) + (b - d) * (b - d));

        Material rockMatCache;
        Material RockMaterial()
        {
            if (rockMatCache != null) return rockMatCache;
            rockMatCache = new Material(Kit.rock);
            rockMatCache.SetColor("_BaseColor", World.rockTint);
            owned.Add(rockMatCache);
            return rockMatCache;
        }

        // ------------------------------------------------------------------ sea, far ground, sky

        void BuildScenery(Material skyPlanet)
        {
            var w = World;
            // the sea, or the lava: one wide sheet that follows the ship
            if (!float.IsNegativeInfinity(w.seaLevel))
            {
                bool lava = w.kind == SurfaceKind.Volcanic;
                var m = lava ? Kit.lava : Kit.water;
                if (m != null)
                {
                    var mat = new Material(m);
                    owned.Add(mat);
                    var plane = SurfaceGenerator.Quad(32000f, lava ? 40f : 400f);
                    owned.Add(plane);
                    var sgo = new GameObject(lava ? "Lava" : "Sea", typeof(MeshFilter), typeof(MeshRenderer));
                    sgo.transform.SetParent(transform, false);
                    sgo.GetComponent<MeshFilter>().sharedMesh = plane;
                    var sr = sgo.GetComponent<MeshRenderer>();
                    sr.sharedMaterial = mat;
                    sr.shadowCastingMode = ShadowCastingMode.Off;
                    sea = sgo.transform;
                    if (lava)
                    {
                        mat.SetTexture("_BaseMap", SurfaceGenerator.LavaTexture());
                        mat.SetColor("_BaseColor", new Color(3.2f, 1.25f, 0.35f));
                        sgo.AddComponent<SurfaceScroll>().speed = new Vector2(0.004f, 0.0025f);
                        // the lava lights the smoke from below
                        var glow = new GameObject("Lava glow").AddComponent<Light>();
                        glow.transform.SetParent(transform, false);
                        glow.type = LightType.Directional;
                        glow.transform.rotation = Quaternion.LookRotation(Vector3.up);
                        glow.color = new Color(1f, 0.38f, 0.1f);
                        glow.intensity = 0.5f;
                        glow.shadows = LightShadows.None;
                    }
                    else mat.SetColor("_BaseColor", w.waterColor);
                }
            }
            // the ground beyond the built areas, out to the horizon
            if (Kit.ground != null)
            {
                ringMat = new Material(Kit.ground);
                var layers = SurfaceGenerator.Layers(w.kind);
                ringMat.SetTexture("_BaseMap", layers[0].diffuseTexture);
                ringMat.SetColor("_BaseColor", Color.white);
                owned.Add(ringMat);
                ringMesh = new Mesh { name = "Horizon", indexFormat = IndexFormat.UInt32 };
                owned.Add(ringMesh);
                var rgo = new GameObject("Horizon", typeof(MeshFilter), typeof(MeshRenderer));
                rgo.transform.SetParent(transform, false);
                rgo.GetComponent<MeshFilter>().sharedMesh = ringMesh;
                var rr = rgo.GetComponent<MeshRenderer>();
                rr.sharedMaterial = ringMat;
                rr.shadowCastingMode = ShadowCastingMode.Off;
                ring = rgo.transform;
            }
            if (skyPlanet != null)
            {
                // the planet this moon circles, hanging in the sky
                var pl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(pl.GetComponent<Collider>());
                pl.name = "Parent planet";
                pl.transform.SetParent(transform, false);
                pl.transform.localScale = Vector3.one * 16000f;
                var rng = new System.Random(w.seed);
                pl.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 360f, 0f);
                float az = (float)rng.NextDouble() * Mathf.PI * 2f;
                skyOffset = new Vector3(Mathf.Cos(az), 0.55f, Mathf.Sin(az)).normalized * 30000f;
                var pr = pl.GetComponent<MeshRenderer>();
                pr.sharedMaterial = skyPlanet;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                sky = pl.transform;
            }
            if (w.particles != null) SurfaceGenerator.Weather(transform, w.particles, Kit);
        }

        Vector3 skyOffset;

        /// <summary>Keep the sea, the far ground and the sky round the ship.</summary>
        void Follow()
        {
            Vector3 p = Ship.position;
            if (sea != null)
            {
                float snap = World.kind == SurfaceKind.Volcanic ? 40f : 400f;
                sea.position = new Vector3(Snap(p.x, snap, Center.x), SeaLevel, Snap(p.z, snap, Center.z));
            }
            if (sky != null) sky.position = new Vector3(p.x, Center.y, p.z) + skyOffset;
            if (ring != null)
            {
                TileOf(p, out int I, out int J);
                Vector3 c = Corner(I, J);
                if (ringDirty || c.x != ringAt.x || c.z != ringAt.z) BuildRing(I, J);
            }
        }

        static float Snap(float x, float step, float origin) => origin + Mathf.Round((x - origin) / step) * step;

        /// <summary>The far ground: a grid round the ship's area, at the world's typical ground
        /// height, with holes where areas are built.</summary>
        void BuildRing(int I0, int J0)
        {
            ringDirty = false;
            Vector3 c0 = Corner(I0, J0);
            ringAt = c0;
            const int Sub = 4, Reach = 6;            // cells per area, areas out each way
            float cell = T / Sub;
            int n = (2 * Reach + 1) * Sub;
            var verts = new List<Vector3>((n + 1) * (n + 1));
            var uvs = new List<Vector2>((n + 1) * (n + 1));
            var tris = new List<int>();
            float tileUv = ringMat != null && ringMat.mainTexture != null ? SurfaceGenerator.Layers(World.kind)[0].tileSize.x : 40f;
            PlanetOf(c0, out double u0, out double v0);
            double period = tileUv * 1000.0;
            double ub = System.Math.Floor(u0 / period) * period, vb = System.Math.Floor(v0 / period) * period;
            float y = Center.y + World.edgeLevel - 0.5f;
            for (int zi = 0; zi <= n; zi++)
                for (int xi = 0; xi <= n; xi++)
                {
                    float x = (xi - Reach * Sub) * cell, z = (zi - Reach * Sub) * cell;
                    verts.Add(new Vector3(c0.x + x, y, c0.z + z) - transform.position);
                    uvs.Add(new Vector2((float)((u0 - ub + x) / tileUv), (float)((v0 - vb + z) / tileUv)));
                }
            for (int zi = 0; zi < n; zi++)
                for (int xi = 0; xi < n; xi++)
                {
                    int I = I0 + Mathf.FloorToInt((float)(xi - Reach * Sub) / Sub), J = J0 + Mathf.FloorToInt((float)(zi - Reach * Sub) / Sub);
                    if (tiles.ContainsKey(Key(I, J))) continue;
                    int i = zi * (n + 1) + xi;
                    tris.Add(i); tris.Add(i + n + 1); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + n + 1); tris.Add(i + n + 2);
                }
            ringMesh.Clear();
            ringMesh.SetVertices(verts);
            ringMesh.SetUVs(0, uvs);
            ringMesh.SetTriangles(tris, 0);
            ringMesh.RecalculateNormals();
            ringMesh.RecalculateBounds();
            ring.localPosition = Vector3.zero;
        }

        // ------------------------------------------------------------------ floating origin

        void ShiftIfFar()
        {
            Vector3 d = Ship.position - Center;
            if (Mathf.Abs(d.x) < shiftDistance && Mathf.Abs(d.z) < shiftDistance) return;
            var delta = new Vector3(-Mathf.Round(d.x / T) * T, 0f, -Mathf.Round(d.z / T) * T);
            ShiftBy(delta);
        }

        /// <summary>Move everything down here by `delta` (whole areas), keeping the planet where it was.</summary>
        public void ShiftBy(Vector3 delta)
        {
            if (delta == Vector3.zero) return;
            var moved = new List<Transform>();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            float reach2 = 60000f * 60000f;
            foreach (var r in roots)
            {
                if (r == gameObject) continue;
                if ((r.transform.position - Center).sqrMagnitude > reach2) continue;
                r.transform.position += delta;
                moved.Add(r.transform);
            }
            foreach (Transform c in transform)
            {
                c.position += delta;
                moved.Add(c);
            }
            OriginU -= delta.x;
            OriginV -= delta.z;
            Shifts++;
            TotalShift += delta;
            foreach (var t in moved)
            {
                if (t == null) continue;
                foreach (var s in t.GetComponentsInChildren<IOriginShift>(true)) s.OriginShifted(delta);
                foreach (var tr in t.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();
            }
            Physics.SyncTransforms();
            ringDirty = true;
            Shifted?.Invoke(delta);
        }
    }
}
