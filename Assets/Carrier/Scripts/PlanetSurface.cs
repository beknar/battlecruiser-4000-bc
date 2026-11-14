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
    ///
    /// Here: making one and where things are on it; the streaming, one area, the backdrop and the
    /// floating origin each have a file (PlanetSurface.*.cs).
    /// </summary>
    [DefaultExecutionOrder(310)]
    public partial class PlanetSurface : MonoBehaviour
    {
        public float nearRadius = 5000f;
        public float farRadius = 15000f;
        public float unloadRadius = 20000f;
        public float shiftDistance = 7500f;

        /// <summary>Off until the ship is down here (it is still in orbit while the first areas are built).</summary>
        public bool Running { get; set; }

        public SurfaceWorld World { get; private set; }
        public SurfaceKit Kit { get; private set; }

        // the settlements' layouts, loaded once and held while this surface is here (so a
        // clear-out of unused assets doesn't take one away between its areas being built)
        readonly Dictionary<string, GameObject> layouts = new Dictionary<string, GameObject>();

        /// <summary>A named base's layout (Kit.Settlement), loaded the first time it is wanted.</summary>
        GameObject Layout(string baseName)
        {
            if (!layouts.TryGetValue(baseName, out var l)) layouts[baseName] = l = Kit.Settlement(baseName);
            return l;
        }
        public Transform Ship { get; private set; }

        /// <summary>Build round this from now on (the ship, once it is down: a stand-in for it
        /// before, while the ground is built ahead of it).</summary>
        public void Follow(Transform ship) => Ship = ship;
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
        /// <summary>Ground forces destroyed on this planet, each till it is rebuilt (WorldState).</summary>
        public DestroyedSet Destroyed { get; private set; }

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
                                           HashSet<int> explored, DestroyedSet destroyed, Material skyPlanet)
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
            s.Destroyed = destroyed ?? new DestroyedSet();
            s.layer = LayerMask.NameToLayer(Cover.LayerName);
            if (s.layer < 0) s.layer = 0;
            s.maxJobs = Threads ? Mathf.Clamp(SystemInfo.processorCount - 2, 1, 4) : 1;
            s.BuildScenery(skyPlanet);
            return s;
        }

        void OnDestroy()
        {
            ShowAll();      // (what it hid near its centre, back on: it may outlive the surface)
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
    }
}
