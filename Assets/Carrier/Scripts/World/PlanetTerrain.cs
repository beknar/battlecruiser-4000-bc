using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A planet's or moon's ground seen from near it, out of space: the sphere is a cube's six faces
    /// pushed out round (a cube sphere), each face a quadtree of 33 x 33 patches, split finer the
    /// nearer the camera is (down to about 50 m between points). The height at each point is the
    /// planet's own surface (SurfaceWorld.Height, the same ground the ship lands on), the colour
    /// is the planet's map at that point (PlanetMaps, the texture it wears from afar, at any
    /// resolution) shaded by the slopes. The sea and the lava lie flat at their level.
    ///
    /// It takes over from the plain sphere within `activeRange` radii of the body, and lets it go
    /// beyond. The patches are worked out on worker threads (a few a frame on the main one in a
    /// web page), and a patch stays drawn until all four of its children are ready, so there are
    /// never holes. Each patch has a skirt hanging down round its edge to hide the cracks between
    /// patches of different sizes.
    /// </summary>
    public class PlanetTerrain : MonoBehaviour
    {
        public SurfaceWorld world;
        public SurfaceKind kind;
        [Tooltip("The map's seed (a moon's own).")]
        public int mapSeed = 901;

        /// <summary>Each body's surface (PlanetLanding hands them out: the same ground it lands on).</summary>
        public static System.Func<WarpPoint, SurfaceWorld> Worlds;
        [Tooltip("The material the patches are drawn with (the planet's own, with vertex colours).")]
        public Material material;
        public Renderer sphere;                 // the plain sphere, hidden while the patches show
        public float activeRange = 3f;          // radii from the centre
        public int maxLevel = 10;
        public float splitDistance = 2.4f;      // a patch splits within this many of its sizes
        public float minPatch = 1600f;          // m: no patch smaller

        const int N = 33;                       // points along a patch's side
        static bool Threads => Application.platform != RuntimePlatform.WebGLPlayer;

        /// <summary>The patches drawn now (for the self-test and the HUD).</summary>
        public int Drawn { get; private set; }
        public int Pending => pending;
        public bool Active { get; private set; }
        public static readonly List<PlanetTerrain> All = new List<PlanetTerrain>();

        float R;                                // the body's radius (m)
        Transform holder;                       // metres, round the body's centre (turning with it)
        Node[] roots;
        int pending;
        readonly Queue<Node> built = new Queue<Node>();
        readonly object gate = new object();
        readonly List<Node> mainThreadWork = new List<Node>();
        readonly Stack<Mesh> meshPool = new Stack<Mesh>();

        // the cube's faces: out, and the two ways across
        static readonly Vector3[] Out = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] AcrossA = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
        static readonly Vector3[] AcrossB = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };

        class Node
        {
            public int face, level;
            public double s0, t0, size;         // the patch on its face: [-1, 1]
            public Node[] kids;
            public GameObject go;
            public MeshRenderer mr;
            public Mesh mesh;
            public Vector3 centre;              // m, from the body's centre (body space, unturned)
            public float extent;                // m, about how big it is
            public volatile int state;          // 0 nothing, 1 working, 2 worked out, 3 ready
            public volatile bool cancelled;
            // worked out (worker thread) -> made into a mesh (main thread)
            public Vector3[] v;
            public Vector3[] n;
            public Color32[] c;
            public Vector2[] uv;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); SetActive(false); }
        void OnDestroy() { Release(); while (meshPool.Count > 0) Destroy(meshPool.Pop()); }

        void Start()
        {
            if (sphere == null) sphere = GetComponent<Renderer>();
        }

        /// <summary>The body's surface, found the first time it is wanted.</summary>
        bool Know()
        {
            if (world == null) world = Worlds?.Invoke(GetComponent<WarpPoint>());
            if (world == null) return false;
            R = (float)world.R;
            return true;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !Know()) return;
            // the system moved out of the way while the ship is down on a surface: the patches
            // kept as they are (hidden), ready for when it comes back up
            if (!ScaledSpace.Active)
            {
                if (holder != null && holder.gameObject.activeSelf) holder.gameObject.SetActive(false);
                if (Active && sphere != null) sphere.enabled = false;
                return;
            }
            if (Active && holder != null && !holder.gameObject.activeSelf) holder.gameObject.SetActive(true);
            float d = Vector3.Distance(cam.transform.position, transform.position);
            bool want = ScaledSpace.Active && d < R * activeRange;
            if (want != Active) SetActive(want);
            if (!Active) return;

            // (the body is a sphere of diameter 1 scaled to its size: its own space is in diameters)
            Vector3 eyeM = transform.InverseTransformPoint(cam.transform.position) * (2f * R);
            ApplyBuilt();
            Drawn = 0;
            foreach (var r in roots) Visit(r, eyeM);
            if (!Threads) RunSome(4f);
        }

        void SetActive(bool on)
        {
            Active = on;
            if (on)
            {
                if (holder == null)
                {
                    holder = new GameObject("Terrain").transform;
                    holder.SetParent(transform, false);
                    // (patches in metres: the body is a unit sphere of diameter 1 scaled up)
                    holder.localScale = Vector3.one / (2f * R);
                    holder.gameObject.layer = gameObject.layer;
                }
                holder.gameObject.SetActive(true);
                roots = new Node[6];
                for (int f = 0; f < 6; f++) roots[f] = NewNode(f, 0, -1.0, -1.0, 2.0);
                foreach (var r in roots) Request(r);
                if (sphere != null) sphere.enabled = true;      // until the first patches are ready
            }
            else
            {
                Release();
                if (holder != null) holder.gameObject.SetActive(false);
                if (sphere != null) sphere.enabled = true;
            }
        }

        Node NewNode(int face, int level, double s0, double t0, double size)
        {
            var nd = new Node { face = face, level = level, s0 = s0, t0 = t0, size = size };
            nd.centre = Point(face, s0 + size * 0.5, t0 + size * 0.5) * R;
            nd.extent = (float)(size * 0.5 * Mathf.PI * 0.5 * R);
            return nd;
        }

        /// <summary>A point of the cube's face (s, t in [-1, 1]) pushed out onto the unit sphere
        /// (spread evenly, so the patches are near enough square everywhere).</summary>
        static Vector3 Point(int face, double s, double t)
        {
            Vector3 c = Out[face] + AcrossA[face] * (float)s + AcrossB[face] * (float)t;
            float x2 = c.x * c.x, y2 = c.y * c.y, z2 = c.z * c.z;
            return new Vector3(c.x * Mathf.Sqrt(1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f),
                               c.y * Mathf.Sqrt(1f - z2 / 2f - x2 / 2f + z2 * x2 / 3f),
                               c.z * Mathf.Sqrt(1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f));
        }

        /// <summary>Draw this patch, or its children if the camera is near enough and they are
        /// ready (asking for them if not).</summary>
        void Visit(Node nd, Vector3 eye)
        {
            float dist = Vector3.Distance(eye, nd.centre);
            bool split = nd.level < maxLevel && nd.extent > minPatch && dist < nd.extent * splitDistance;
            if (split)
            {
                if (nd.kids == null)
                {
                    double h = nd.size * 0.5;
                    nd.kids = new[]
                    {
                        NewNode(nd.face, nd.level + 1, nd.s0, nd.t0, h), NewNode(nd.face, nd.level + 1, nd.s0 + h, nd.t0, h),
                        NewNode(nd.face, nd.level + 1, nd.s0, nd.t0 + h, h), NewNode(nd.face, nd.level + 1, nd.s0 + h, nd.t0 + h, h),
                    };
                    foreach (var k in nd.kids) Request(k);
                }
                bool ready = true;
                foreach (var k in nd.kids) if (k.state != 3) { ready = false; break; }
                if (ready)
                {
                    Show(nd, false);
                    foreach (var k in nd.kids) Visit(k, eye);
                    return;
                }
            }
            else if (nd.kids != null)
            {
                foreach (var k in nd.kids) Free(k);
                nd.kids = null;
            }
            if (nd.state == 3) { Show(nd, true); Drawn++; if (sphere != null && sphere.enabled) sphere.enabled = false; }
            else if (nd.state == 0) Request(nd);
        }

        void Show(Node nd, bool on)
        {
            if (nd.mr != null && nd.mr.enabled != on) nd.mr.enabled = on;
        }

        void Request(Node nd)
        {
            if (nd.state != 0) return;
            nd.state = 1;
            Interlocked.Increment(ref pending);
            if (Threads) ThreadPool.QueueUserWorkItem(_ => Work(nd));
            else mainThreadWork.Add(nd);
        }

        void RunSome(float ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // nearest (smallest) first: what the camera is over
            mainThreadWork.Sort((a, b) => b.level.CompareTo(a.level));
            while (mainThreadWork.Count > 0 && sw.Elapsed.TotalMilliseconds < ms)
            {
                var nd = mainThreadWork[0];
                mainThreadWork.RemoveAt(0);
                Work(nd);
            }
        }

        /// <summary>Work out a patch's points (any thread): positions round its centre, normals,
        /// colours, and the skirt.</summary>
        void Work(Node nd)
        {
            try
            {
                if (nd.cancelled) { Interlocked.Decrement(ref pending); return; }
                const int G = N + 2;                         // with a border, for the normals
                var pos = new Vector3[G * G];
                var hint = new float[G * G];
                double step = nd.size / (N - 1);
                var ctxCache = new Dictionary<long, TileFeatures[]>();
                bool fine = nd.extent < 30000f;
                for (int j = 0; j < G; j++)
                    for (int i = 0; i < G; i++)
                    {
                        Vector3 dir = Point(nd.face, nd.s0 + (i - 1) * step, nd.t0 + (j - 1) * step);
                        float h = HeightAt(dir, fine, ctxCache, out float m);
                        hint[j * G + i] = m;
                        pos[j * G + i] = dir * (R + h);
                    }
                int skirt = 4 * (N - 1);
                var v = new Vector3[N * N + skirt];
                var n = new Vector3[v.Length];
                var c = new Color32[v.Length];
                var uv = new Vector2[v.Length];
                float centreU = UV(nd.centre.normalized).x;
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        int g = (j + 1) * G + (i + 1);
                        Vector3 p = pos[g];
                        Vector3 nn = Vector3.Cross(pos[g + G] - pos[g - G], pos[g + 1] - pos[g - 1]).normalized;
                        Vector3 up = p.normalized;
                        if (Vector3.Dot(nn, up) < 0f) nn = -nn;
                        int k = j * N + i;
                        v[k] = p - nd.centre;
                        n[k] = nn;
                        Vector2 t = UV(up);
                        if (t.x - centreU > 0.5f) t.x -= 1f; else if (centreU - t.x > 0.5f) t.x += 1f;   // (no seam across a patch)
                        uv[k] = t;
                        float slope = 1f - Vector3.Dot(nn, up);
                        Color col = PlanetMaps.Ground(kind, up, mapSeed) * (1f - Mathf.Clamp01(slope * 3f) * 0.35f);
                        col = PlanetMaps.Hint(kind, col, hint[g]);
                        c[k] = col;
                    }
                // the skirt: round the edge, hanging down
                float drop = Mathf.Max(60f, nd.extent * 0.02f);
                int s = N * N;
                for (int e = 0; e < skirt; e++)
                {
                    int k = Edge(e);
                    v[s + e] = v[k] - (v[k] + nd.centre).normalized * drop;
                    n[s + e] = n[k];
                    c[s + e] = c[k];
                    uv[s + e] = uv[k];
                }
                nd.v = v; nd.n = n; nd.c = c; nd.uv = uv;
                nd.state = 2;
                lock (gate) built.Enqueue(nd);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlanetTerrain] {name}: {ex.Message}");
                nd.state = 0;
                Interlocked.Decrement(ref pending);
            }
        }

        /// <summary>The height of a body's ground (above its mean radius, m) under a world point:
        /// what is built on the surface stands on it (0 for a body with no terrain).</summary>
        public static float GroundAt(Transform body, Vector3 worldPoint)
        {
            if (body == null || !body.TryGetComponent(out PlanetTerrain t) || !t.Know()) return 0f;
            Vector3 dir = body.InverseTransformDirection(worldPoint - body.position);
            if (dir.sqrMagnitude < 1f) return 0f;
            return t.HeightAt(dir.normalized, true, new Dictionary<long, TileFeatures[]>(), out _);
        }

        /// <summary>The ground's height above the body's mean radius at a direction (its own space).</summary>
        float HeightAt(Vector3 dir, bool fine, Dictionary<long, TileFeatures[]> ctxCache, out float m)
        {
            world.FromDirection(dir, out double u, out double v);
            TileFeatures[] ctx = System.Array.Empty<TileFeatures>();
            if (fine)
            {
                int ci = (int)System.Math.Floor(u / SurfaceWorld.T), cj = (int)System.Math.Floor((v + world.V) / SurfaceWorld.T);
                world.CanonicalTile(ci, cj, out ci, out cj);
                long key = ((long)ci << 32) | (uint)cj;
                if (!ctxCache.TryGetValue(key, out ctx))
                {
                    ctxCache[key] = ctx = world.Neighbourhood(ci, cj) ?? System.Array.Empty<TileFeatures>();
                    // (a landing pad's ground levelled, as the surface has it)
                    foreach (var f in ctx) world.EnsureSiteHeight(f);
                }
            }
            m = 0f;
            float h = world.Height(u, v, world.Region(u, v), ctx, fine, ref m);
            if (!float.IsNegativeInfinity(world.seaLevel)) h = Mathf.Max(h, world.seaLevel);
            return h;
        }

        /// <summary>The planet map's coordinates at a direction (as ProceduralSphere's).</summary>
        static Vector2 UV(Vector3 d)
        {
            float lon = Mathf.Atan2(d.z, d.x);
            if (lon < 0f) lon += Mathf.PI * 2f;
            float lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
            return new Vector2(1f - lon / (Mathf.PI * 2f), (lat + Mathf.PI / 2f) / Mathf.PI);
        }

        /// <summary>The e-th point round a patch's edge.</summary>
        static int Edge(int e)
        {
            int side = e / (N - 1), k = e % (N - 1);
            switch (side)
            {
                case 0: return k;                                   // bottom, left to right
                case 1: return k * N + (N - 1);                     // right, up
                case 2: return (N - 1) * N + (N - 1 - k);           // top, right to left
                default: return (N - 1 - k) * N;                    // left, down
            }
        }

        static int[] triangles;
        static int[] Triangles()
        {
            if (triangles != null) return triangles;
            var t = new List<int>();
            for (int j = 0; j < N - 1; j++)
                for (int i = 0; i < N - 1; i++)
                {
                    int a = j * N + i, b = a + N;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            int s = N * N, skirt = 4 * (N - 1);
            for (int e = 0; e < skirt; e++)
            {
                int a = Edge(e), b = Edge((e + 1) % skirt), sa = s + e, sb = s + (e + 1) % skirt;
                t.Add(a); t.Add(b); t.Add(sa);
                t.Add(b); t.Add(sb); t.Add(sa);
            }
            return triangles = t.ToArray();
        }

        /// <summary>Make meshes of what the workers have finished (a few a frame).</summary>
        void ApplyBuilt()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 3.0)
            {
                Node nd;
                lock (gate) { if (built.Count == 0) break; nd = built.Dequeue(); }
                Interlocked.Decrement(ref pending);
                if (nd.cancelled) continue;
                var mesh = meshPool.Count > 0 ? meshPool.Pop() : new Mesh { name = "TerrainPatch", hideFlags = HideFlags.DontSave };
                mesh.Clear();
                mesh.vertices = nd.v;
                mesh.normals = nd.n;
                mesh.colors32 = nd.c;
                mesh.uv = nd.uv;
                mesh.triangles = Triangles();
                mesh.RecalculateBounds();
                nd.v = null; nd.n = null; nd.c = null; nd.uv = null;
                nd.mesh = mesh;
                if (nd.go == null)
                {
                    nd.go = new GameObject($"P{nd.face}_{nd.level}");
                    nd.go.layer = gameObject.layer;
                    nd.go.transform.SetParent(holder, false);
                    nd.go.AddComponent<MeshFilter>();
                    nd.mr = nd.go.AddComponent<MeshRenderer>();
                    nd.mr.sharedMaterial = material;
                    nd.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    nd.mr.receiveShadows = false;
                }
                nd.go.transform.localPosition = nd.centre;
                nd.go.GetComponent<MeshFilter>().sharedMesh = mesh;
                nd.mr.enabled = false;
                nd.state = 3;
            }
        }

        void Free(Node nd)
        {
            if (nd == null) return;
            nd.cancelled = true;
            if (nd.kids != null) foreach (var k in nd.kids) Free(k);
            nd.kids = null;
            if (mainThreadWork.Remove(nd)) Interlocked.Decrement(ref pending);
            if (nd.mesh != null) { meshPool.Push(nd.mesh); nd.mesh = null; }
            if (nd.go != null) Destroy(nd.go);
            nd.go = null;
        }

        void Release()
        {
            if (roots != null) foreach (var r in roots) Free(r);
            roots = null;
            mainThreadWork.Clear();
            lock (gate) built.Clear();
            pending = 0;
            Drawn = 0;
        }
    }
}
