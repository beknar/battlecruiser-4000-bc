using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>What a body's surface is like (and so what the landing site generates).</summary>
    public enum SurfaceKind { None, Volcanic, Desert, Ocean, GasGiant, IceGiant, Moon, Asteroid }

    /// <summary>The materials and models the surfaces are built from (set up by the level builder).</summary>
    [System.Serializable]
    public class SurfaceKit
    {
        [Tooltip("Universal Render Pipeline/Terrain/Lit.")]
        public Material terrain;
        [Tooltip("Skybox/Procedural: the sky of a world with an atmosphere.")]
        public Material sky;
        [Tooltip("Lit, smooth: the sea.")]
        public Material water;
        [Tooltip("Unlit: lava (its texture is made at run time).")]
        public Material lava;
        [Tooltip("Lit: the ground beyond the landing area, out to the horizon.")]
        public Material ground;
        [Tooltip("Rock for boulders (tinted to suit the world).")]
        public Material rock;
        [Tooltip("Alpha-blended particles: dust, ash, snow.")]
        public Material dust;
        [Tooltip("Additive particles: embers.")]
        public Material embers;
        [Tooltip("A landing pad with a few parked craft (optional: left out if the art isn't there).")]
        public GameObject outpost;
    }

    /// <summary>A generated landing site.</summary>
    public class SurfaceSite
    {
        public GameObject root;
        public Terrain terrain;
        public SurfaceKind kind;
        public Vector3 center;          // middle of the area, at the terrain's base height
        public Vector3 pad;             // top of the landing pad (or the flattened landing zone)
        public bool hasOutpost;
        public float seaLevel = float.NegativeInfinity;   // world height of the sea / lava (or -inf)
        public bool solidGround = true; // false for a gas giant's cloud deck
        public float floor;             // world height the ship can't go below (cloud deck)
        public float maxHeight;         // world height of the highest ground
        public string description;
        public Look look;
        readonly List<Object> owned = new List<Object>();

        public void Own(Object o) { if (o != null) owned.Add(o); }

        /// <summary>The ground's height under a point (terrain only).</summary>
        public float GroundAt(Vector3 p) => terrain != null ? terrain.transform.position.y + terrain.SampleHeight(p) : center.y;

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
        }
    }

    /// <summary>The sky, fog and light of a landing site.</summary>
    public class Look
    {
        public bool space;                       // airless: the starfield, no fog
        public Color skyTop = Color.gray, skyBottom = Color.gray;   // the horizon is the fog colour
        public float sunSize = 0.025f, sunGlow = 0.35f;
        public Color fog = Color.gray;
        public float fogStart = 1500f, fogEnd = 9000f;
        public Color sunColor = Color.white;
        public float sunIntensity = 2f, sunElevation = 40f, sunAzimuth = 30f;
        public Color ambientSky = Color.gray, ambientEquator = Color.gray, ambientGround = Color.black;
    }

    /// <summary>
    /// Builds a 5 km x 5 km landing site for a body when the frigate lands: a terrain shaped by the
    /// kind of world (lava fields and cinder cones, dunes and mesas, islands in a sea, a gas
    /// giant's cloud deck, an ice sheet with pressure ridges and crevasses, a cratered moon, a
    /// lumpy asteroid), textured to match, with its own sky, fog and light, boulders, and a
    /// landing pad where there is someone to land with. The seed comes from the body and where
    /// over it the ship was, so the same spot always gives the same site and other spots differ.
    /// The parts of the planet's own texture under the landing point carry through (a landing
    /// over Thalassa's ocean finds a few islands; over a continent, mostly land).
    /// Everything is made at run time and thrown away on take-off.
    /// </summary>
    public static class SurfaceGenerator
    {
        public const float Size = 5000f;
        const int Res = 513;          // height samples a side (about 10 m apart)
        const int SplatRes = 512;
        const float Half = Size * 0.5f;
        const float RingExtent = 25000f;
        const float PadRadius = 230f;
        const float PadLift = 1.5f;     // the pad's top above the flattened ground

        // ------------------------------------------------------------------ 2D noise

        static float S(float t) => t * t * (3f - 2f * t);

        static float V2(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = S(x - xi), fy = S(y - yi);
            float a = ProcNoise.Hash(xi, yi, 0, seed), b = ProcNoise.Hash(xi + 1, yi, 0, seed);
            float c = ProcNoise.Hash(xi, yi + 1, 0, seed), d = ProcNoise.Hash(xi + 1, yi + 1, 0, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal noise, roughly -1..1.</summary>
        static float Fbm(float x, float y, int seed, int oct, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                sum += (V2(x, y, seed + i * 31) * 2f - 1f) * amp;
                norm += amp; amp *= gain; x *= 2.03f; y *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Ridged noise, 0..1 (sharp crests).</summary>
        static float Ridged(float x, float y, int seed, int oct)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                float n = 1f - Mathf.Abs(V2(x, y, seed + i * 57) * 2f - 1f);
                sum += n * n * amp;
                norm += amp; amp *= 0.5f; x *= 2.1f; y *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>Billowy noise, 0..1 (rounded puffs: clouds).</summary>
        static float Billow(float x, float y, int seed, int oct)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                sum += Mathf.Abs(V2(x, y, seed + i * 43) * 2f - 1f) * amp;
                norm += amp; amp *= 0.5f; x *= 2.07f; y *= 2.07f;
            }
            return sum / norm;
        }

        static float Smooth(float a, float b, float x) => S(Mathf.Clamp01((x - a) / (b - a)));

        // ------------------------------------------------------------------ the site

        /// <summary>Generate the landing site for `kind`, centred on `center` (its base height).
        /// `localDir` is the landing point's direction from the body's centre in the body's own
        /// space (for reading the planet's map there); `latitude` in degrees.</summary>
        public static SurfaceSite Build(SurfaceKind kind, int seed, Vector3 localDir, string bodyName, Vector3 center, SurfaceKit kit, Material skyPlanet = null)
        {
            var site = new SurfaceSite { kind = kind, center = center };
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            localDir = localDir.sqrMagnitude > 1e-6f ? localDir.normalized : Vector3.up;
            float latitude = Mathf.Asin(Mathf.Clamp(localDir.y, -1f, 1f)) * Mathf.Rad2Deg;

            var p = Profile(kind, localDir, latitude, rng);
            site.look = p.look;
            site.description = p.description;
            var heights = new float[Res, Res];     // metres above the terrain's base, [z, x]
            var mask = new float[Res, Res];        // extra splat hint (crater ejecta, lava glow ...)
            float step = Size / (Res - 1);
            int s1 = seed * 7 + 11;

            // the landing zone: a flat spot at the middle (raised onto an island, a plateau ...)
            float padH = p.padHeight;

            for (int zi = 0; zi < Res; zi++)
            {
                float z = -Half + zi * step;
                for (int xi = 0; xi < Res; xi++)
                {
                    float x = -Half + xi * step;
                    float h = p.height(x, z, s1, ref mask[zi, xi]);
                    // flatten the landing zone, blending out over the next 200 m
                    float r = Mathf.Sqrt(x * x + z * z);
                    float flat = 1f - Smooth(PadRadius, PadRadius + 220f, r);
                    h = Mathf.Lerp(h, padH, flat);
                    // fade to the horizon level at the edges, so the ground beyond carries on
                    float edge = Mathf.Min(Half - Mathf.Abs(x), Half - Mathf.Abs(z));
                    h = Mathf.Lerp(p.edgeLevel, h, Smooth(0f, 260f, edge));
                    heights[zi, xi] = h;
                }
            }
            if (p.craters > 0) Craters(heights, mask, p, rng, step);
            // the world curves away (small bodies), applied last so the horizon ring matches
            if (p.curveRadius > 0f)
                for (int zi = 0; zi < Res; zi++)
                    for (int xi = 0; xi < Res; xi++)
                    {
                        float x = -Half + xi * step, z = -Half + zi * step;
                        heights[zi, xi] -= (x * x + z * z) / (2f * p.curveRadius);
                    }

            // the terrain's own height range: everything is shifted so the lowest point is 0
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float h in heights) { if (h < lo) lo = h; if (h > hi) hi = h; }
            lo = Mathf.Min(lo, 0f);
            float range = Mathf.Max(50f, hi - lo + 1f);
            var norm = new float[Res, Res];
            for (int zi = 0; zi < Res; zi++)
                for (int xi = 0; xi < Res; xi++)
                    norm[zi, xi] = (heights[zi, xi] - lo) / range;
            float baseY = center.y + lo;          // world height of terrain height 0

            var root = new GameObject($"Surface of {bodyName}");
            site.root = root;
            root.transform.position = center;
            int layer = LayerMask.NameToLayer(Cover.LayerName);
            if (layer < 0) layer = 0;

            // ---------------------------------------------------------------- terrain
            var td = new TerrainData();
            td.heightmapResolution = Res;
            td.size = new Vector3(Size, range, Size);
            td.SetHeights(0, 0, norm);
            td.alphamapResolution = SplatRes;
            td.baseMapResolution = 512;
            var layers = Layers(kind, p);
            td.terrainLayers = layers;
            td.SetAlphamaps(0, 0, Splat(heights, mask, p, layers.Length, step, seed));
            site.Own(td);
            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";
            tgo.layer = layer;
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.position = new Vector3(center.x - Half, baseY, center.z - Half);
            var terrain = tgo.GetComponent<Terrain>();
            if (kit.terrain != null) terrain.materialTemplate = kit.terrain;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 3000f;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            terrain.drawTreesAndFoliage = false;
            site.terrain = terrain;
            var tc = tgo.GetComponent<TerrainCollider>();
            site.solidGround = p.solid;
            if (tc != null) tc.enabled = p.solid;
            if (p.solid)
            {
                // so the hull collides with it (Cover.Active); not cover, and no core sphere for pilots
                var ob = tgo.AddComponent<Obstacle>();
                ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f;
            }
            site.floor = baseY + (p.solid ? 0f : p.floorLevel - lo);
            site.maxHeight = baseY + range;

            // ---------------------------------------------------------------- horizon ring
            var ringMat = kit.ground != null ? new Material(kit.ground) : null;
            if (ringMat != null)
            {
                ringMat.SetTexture("_BaseMap", layers[0].diffuseTexture);
                ringMat.SetColor("_BaseColor", Color.white);
                site.Own(ringMat);
                var ring = Ring(p, center, layers[0].tileSize.x);
                site.Own(ring);
                var rgo = new GameObject("Horizon", typeof(MeshFilter), typeof(MeshRenderer));
                rgo.transform.SetParent(root.transform, false);
                rgo.transform.position = center;
                rgo.GetComponent<MeshFilter>().sharedMesh = ring;
                var rr = rgo.GetComponent<MeshRenderer>();
                rr.sharedMaterial = ringMat;
                rr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // ---------------------------------------------------------------- sea / lava
            if (p.seaLevel > float.NegativeInfinity)
            {
                site.seaLevel = center.y + p.seaLevel;
                bool lava = kind == SurfaceKind.Volcanic;
                var m = lava ? kit.lava : kit.water;
                if (m != null)
                {
                    var mat = new Material(m);
                    site.Own(mat);
                    float extent = lava ? Half : RingExtent;
                    var plane = Quad(extent, lava ? 40f : 400f);
                    site.Own(plane);
                    var sgo = new GameObject(lava ? "Lava" : "Sea", typeof(MeshFilter), typeof(MeshRenderer));
                    sgo.transform.SetParent(root.transform, false);
                    sgo.transform.position = new Vector3(center.x, site.seaLevel, center.z);
                    sgo.GetComponent<MeshFilter>().sharedMesh = plane;
                    var sr = sgo.GetComponent<MeshRenderer>();
                    sr.sharedMaterial = mat;
                    sr.shadowCastingMode = ShadowCastingMode.Off;
                    if (lava)
                    {
                        var lt = LavaTexture();
                        mat.SetTexture("_BaseMap", lt);
                        mat.SetColor("_BaseColor", new Color(3.2f, 1.25f, 0.35f));
                        sgo.AddComponent<SurfaceScroll>().speed = new Vector2(0.004f, 0.0025f);
                        // the lava lights the smoke from below
                        var glow = new GameObject("Lava glow").AddComponent<Light>();
                        glow.transform.SetParent(root.transform, false);
                        glow.type = LightType.Directional;
                        glow.transform.rotation = Quaternion.LookRotation(Vector3.up);
                        glow.color = new Color(1f, 0.38f, 0.1f);
                        glow.intensity = 0.5f;
                        glow.shadows = LightShadows.None;
                    }
                    else
                    {
                        mat.SetColor("_BaseColor", p.waterColor);
                    }
                }
            }

            // ---------------------------------------------------------------- landing pad
            Vector3 padTop = new Vector3(center.x, center.y + padH, center.z);
            if (p.outpost && kit.outpost != null)
            {
                float lift = kind == SurfaceKind.GasGiant ? p.platformLift : 0f;
                var o = Object.Instantiate(kit.outpost, root.transform);
                o.name = "Outpost";
                o.transform.position = padTop + Vector3.up * (lift + PadLift);
                o.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                foreach (var c in o.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
                if (o.GetComponent<Obstacle>() == null) { var ob = o.AddComponent<Obstacle>(); ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f; }
                padTop += Vector3.up * (lift + PadLift);
                site.hasOutpost = true;
            }
            site.pad = padTop;

            // ---------------------------------------------------------------- boulders
            if (p.boulders > 0 && kit.rock != null)
            {
                var rockMat = new Material(kit.rock);
                rockMat.SetColor("_BaseColor", p.rockTint);
                site.Own(rockMat);
                var rocks = new GameObject("Boulders").transform;
                rocks.SetParent(root.transform, false);
                for (int i = 0, tries = 0; i < p.boulders && tries < p.boulders * 6; tries++)
                {
                    float x = R(-Half + 150f, Half - 150f), z = R(-Half + 150f, Half - 150f);
                    if (x * x + z * z < (PadRadius + 120f) * (PadRadius + 120f)) continue;
                    float h = Sample(heights, x, z, step);
                    if (p.seaLevel > float.NegativeInfinity && h < p.seaLevel + 2f) continue;
                    if (Slope(heights, x, z, step) > p.boulderSlope) continue;
                    float size = Mathf.Lerp(p.boulderSize.x, p.boulderSize.y, Mathf.Pow(R(0f, 1f), 2.2f));
                    var pos = new Vector3(center.x + x, center.y + h - size * 0.3f, center.z + z);
                    Boulder(rocks, pos, size, 600 + (i % 6), rockMat, layer, R(0f, 360f), R(0.55f, 0.9f));
                    i++;
                }
            }

            // ---------------------------------------------------------------- the sky above
            if (p.floatingRocks > 0 && kit.rock != null)
            {
                var rockMat = new Material(kit.rock);
                site.Own(rockMat);
                var sky = new GameObject("Asteroids overhead").transform;
                sky.SetParent(root.transform, false);
                for (int i = 0; i < p.floatingRocks; i++)
                {
                    float a = R(0f, 360f) * Mathf.Deg2Rad, d = R(1200f, 9000f);
                    var pos = center + new Vector3(Mathf.Cos(a) * d, R(1500f, 5500f), Mathf.Sin(a) * d);
                    Boulder(sky, pos, R(60f, 380f), 620 + (i % 5), rockMat, layer, R(0f, 360f), R(0.6f, 0.95f)).AddComponent<Drift>().spin =
                        new Vector3(R(-2f, 2f), R(-2f, 2f), R(-2f, 2f));
                }
            }
            if (skyPlanet != null)
            {
                // the planet this moon circles, hanging in the sky
                var pl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(pl.GetComponent<Collider>());
                pl.name = "Parent planet";
                pl.transform.SetParent(root.transform, false);
                float az = R(0f, 360f) * Mathf.Deg2Rad;
                pl.transform.position = center + new Vector3(Mathf.Cos(az), 0.55f, Mathf.Sin(az)).normalized * 30000f;
                pl.transform.localScale = Vector3.one * 16000f;
                pl.transform.rotation = Quaternion.Euler(R(-20f, 20f), R(0f, 360f), 0f);
                var pr = pl.GetComponent<MeshRenderer>();
                pr.sharedMaterial = skyPlanet;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
            }

            // ---------------------------------------------------------------- weather
            if (p.particles != null) Weather(root.transform, p, kit, site);
            return site;
        }

        /// <summary>A boulder from the asteroid generator (built inactive so it is shaped once).</summary>
        static GameObject Boulder(Transform parent, Vector3 pos, float size, int seed, Material mat, int layer, float yaw, float flat)
        {
            var go = new GameObject("Boulder");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = new Vector3(size, size * flat, size);
            go.layer = layer;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (size > 14f) go.AddComponent<MeshCollider>();
            var pa = go.AddComponent<ProceduralAsteroid>();
            pa.seed = seed;
            pa.detail = 3;
            pa.colliderDetail = 1;
            pa.semiAxes = Vector3.one * 0.5f;
            pa.roughness = 0.3f;
            pa.craters = 3;
            pa.uvTile = 0.25f;
            go.SetActive(true);
            return go;
        }

        // ------------------------------------------------------------------ per world

        delegate float HeightFn(float x, float z, int seed, ref float mask);

        class P
        {
            public SurfaceKind kind;
            public string description;
            public Look look = new Look();
            public HeightFn height;
            public float padHeight, edgeLevel, curveRadius;
            public float seaLevel = float.NegativeInfinity;
            public Color waterColor = new Color(0.03f, 0.12f, 0.2f);
            public bool solid = true, outpost;
            public float floorLevel, platformLift;
            public int craters, boulders, floatingRocks;
            public Vector2 craterSize = new Vector2(30f, 600f);
            public Vector2 boulderSize = new Vector2(6f, 30f);
            public float boulderSlope = 0.5f;
            public Color rockTint = Color.white;
            public float snowLine = float.PositiveInfinity;
            public float[] splatParams = new float[4];
            public ParticleLook particles;
        }

        class ParticleLook
        {
            public Color color = Color.white;
            public float size = 1.5f, rate = 600f, fall = 4f, wind = 2f;
            public bool embers;
        }

        static P Profile(SurfaceKind kind, Vector3 d, float latitude, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var p = new P { kind = kind };
            var L = p.look;
            L.sunAzimuth = R(0f, 360f);
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                {
                    // lava where the planet's own map shows cracks
                    float crack = Mathf.Pow(ProcNoise.Ridged(d * 4.5f + Vector3.one * 3f, 503, 5), 5f);
                    float lavaLevel = Mathf.Lerp(42f, 70f, Smooth(0.05f, 0.4f, crack));
                    p.seaLevel = lavaLevel;
                    var cones = new List<Vector4>();   // x, z, radius, height
                    int n = 2 + rng.Next(2);
                    for (int i = 0; i < n; i++)
                    {
                        // wholly inside the area (clear of the faded edge), clear of the pad
                        float a = R(0f, Mathf.PI * 2f) + i * 2.4f, dist = R(1150f, 1400f);
                        cones.Add(new Vector4(Mathf.Cos(a) * dist, Mathf.Sin(a) * dist, R(650f, 820f), R(380f, 620f)));
                    }
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float h = 85f + 45f * Fbm(x / 1300f, z / 1300f, s, 5) + 14f * Fbm(x / 160f, z / 160f, s + 5, 3);
                        // lava channels cut through the plain
                        float ch = Mathf.Pow(Ridged(x / 1100f + 7.3f, z / 1100f, s + 9, 3), 7f);
                        h -= ch * 70f;
                        foreach (var c in cones)
                        {
                            float dx = x - c.x, dz = z - c.y, r = Mathf.Sqrt(dx * dx + dz * dz) / c.z;
                            if (r >= 1.2f) continue;
                            float cone = Mathf.Pow(Mathf.Max(0f, 1f - r), 1.5f) * c.w;
                            cone *= 1f + 0.18f * (Ridged(x / 140f, z / 140f, s + 21, 3) - 0.5f);
                            float crater = r < 0.16f ? (1f - r / 0.16f) : 0f;
                            h += cone - crater * crater * c.w * 0.45f;
                        }
                        m = Mathf.Clamp01(1f - (h - lavaLevel) / 22f);   // scorched near the lava
                        return h;
                    };
                    p.padHeight = lavaLevel + 40f;
                    p.edgeLevel = 90f;
                    p.boulders = 70; p.boulderSize = new Vector2(5f, 26f); p.rockTint = new Color(0.32f, 0.27f, 0.25f);
                    p.description = "basalt plains, cinder cones and rivers of lava";
                    L.skyTop = new Color(0.16f, 0.08f, 0.06f); L.skyBottom = new Color(0.22f, 0.09f, 0.05f); L.sunSize = 0.035f; L.sunGlow = 0.6f;
                    L.fog = new Color(0.34f, 0.17f, 0.11f); L.fogStart = 600f; L.fogEnd = 7000f;
                    L.sunColor = new Color(1f, 0.66f, 0.45f); L.sunIntensity = 1.9f; L.sunElevation = R(25f, 55f);
                    L.ambientSky = new Color(0.42f, 0.25f, 0.18f); L.ambientEquator = new Color(0.4f, 0.19f, 0.1f); L.ambientGround = new Color(0.5f, 0.16f, 0.05f);
                    p.particles = new ParticleLook { color = new Color(0.2f, 0.18f, 0.17f, 0.75f), size = 1.4f, rate = 500f, fall = 3f, wind = 5f, embers = true };
                    break;
                }
                case SurfaceKind.Desert:
                {
                    float basin = Smooth(0.62f, 0.8f, ProcNoise.Fbm(d * 1.3f + Vector3.one * 7f, 611, 3) * 0.5f + 0.5f);
                    float duneAngle = R(0f, Mathf.PI);
                    float ca = Mathf.Cos(duneAngle), sa = Mathf.Sin(duneAngle);
                    float mesaThreshold = R(0.18f, 0.32f);
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float u = x * ca + z * sa, v = -x * sa + z * ca;
                        float warp = 70f * Fbm(x / 900f, z / 900f, s + 3, 3);
                        float crest = 1f - Mathf.Abs(Mathf.Sin((u + warp) / 260f * Mathf.PI));
                        float dunes = Mathf.Pow(crest, 1.6f) * (26f + 18f * Fbm(v / 1400f, u / 1400f, s + 4, 2));
                        float plain = 60f + 25f * Fbm(x / 2000f, z / 2000f, s, 4) + dunes * (1f - basin * 0.8f);
                        float mesa = Fbm(x / 1500f + 3.1f, z / 1500f, s + 7, 4);
                        float top = Smooth(mesaThreshold, mesaThreshold + 0.035f, mesa);
                        float tier = Smooth(mesaThreshold + 0.12f, mesaThreshold + 0.15f, mesa);
                        float rock = top * 140f + tier * 70f;
                        rock *= 1f + 0.06f * Fbm(x / 60f, z / 60f, s + 11, 2);
                        m = Mathf.Clamp01(basin * (1f - Smooth(-0.3f, -0.1f, Fbm(x / 1200f, z / 1200f, s + 13, 3))));   // salt pans
                        return plain + rock;
                    };
                    p.padHeight = 66f; p.edgeLevel = 60f;
                    p.outpost = true;
                    p.boulders = 30; p.boulderSize = new Vector2(5f, 22f); p.rockTint = new Color(0.95f, 0.62f, 0.45f);
                    p.description = "dune seas between flat-topped mesas";
                    L.skyTop = new Color(0.42f, 0.52f, 0.68f); L.skyBottom = new Color(0.62f, 0.47f, 0.34f); L.sunSize = 0.022f; L.sunGlow = 0.5f;
                    L.fog = new Color(0.78f, 0.62f, 0.48f); L.fogStart = 1800f; L.fogEnd = 14000f;
                    L.sunColor = new Color(1f, 0.9f, 0.76f); L.sunIntensity = 2.3f; L.sunElevation = R(30f, 65f);
                    L.ambientSky = new Color(0.62f, 0.52f, 0.42f); L.ambientEquator = new Color(0.5f, 0.38f, 0.28f); L.ambientGround = new Color(0.4f, 0.28f, 0.18f);
                    p.particles = new ParticleLook { color = new Color(0.85f, 0.68f, 0.5f, 0.35f), size = 2.2f, rate = 260f, fall = 0.5f, wind = 14f };
                    break;
                }
                case SurfaceKind.Ocean:
                {
                    // how much land: the planet's own height map at the landing point
                    float hp = ProcNoise.Fbm(d * 2.1f, 701, 6) + 0.25f * ProcNoise.Fbm(d * 7f, 709, 4);
                    float land = Mathf.Clamp(hp * 2.2f, -0.4f, 0.6f);
                    float polar = Smooth(48f, 70f, Mathf.Abs(latitude));
                    const float sea = 120f;
                    p.seaLevel = sea;
                    p.snowLine = Mathf.Lerp(560f, 210f, polar);
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float c = Fbm(x / 2300f, z / 2300f, s, 5) + land;
                        // an island for the landing pad
                        c += 0.75f * Mathf.Exp(-(x * x + z * z) / (2f * 950f * 950f));
                        float h = sea + c * 220f;
                        float hills = Smooth(0.05f, 0.45f, c);
                        h += hills * (Mathf.Pow(Ridged(x / 1300f, z / 1300f, s + 5, 5), 1.6f) * 520f + 40f * Fbm(x / 200f, z / 200f, s + 8, 3));
                        m = 0f;
                        return h;
                    };
                    p.padHeight = sea + 28f; p.edgeLevel = sea - 60f;
                    p.waterColor = new Color(0.02f, 0.1f, 0.2f);
                    p.outpost = true;
                    p.boulders = 25; p.boulderSize = new Vector2(4f, 18f); p.rockTint = new Color(0.62f, 0.6f, 0.56f); p.boulderSlope = 0.35f;
                    p.description = polar > 0.5f ? "cold islands in a grey polar sea" : "green islands in a deep blue sea";
                    L.skyTop = new Color(0.2f, 0.4f, 0.8f); L.skyBottom = new Color(0.28f, 0.36f, 0.44f); L.sunSize = 0.022f; L.sunGlow = 0.4f;
                    L.fog = new Color(0.62f, 0.72f, 0.86f); L.fogStart = 2500f; L.fogEnd = 20000f;
                    L.sunColor = new Color(1f, 0.95f, 0.86f); L.sunIntensity = 2.1f; L.sunElevation = R(25f, 60f);
                    L.ambientSky = new Color(0.45f, 0.55f, 0.72f); L.ambientEquator = new Color(0.36f, 0.42f, 0.48f); L.ambientGround = new Color(0.16f, 0.2f, 0.16f);
                    if (polar > 0.5f) p.particles = new ParticleLook { color = new Color(1f, 1f, 1f, 0.6f), size = 1.1f, rate = 500f, fall = 6f, wind = 4f };
                    break;
                }
                case SurfaceKind.GasGiant:
                {
                    p.solid = false;
                    p.floorLevel = 40f;
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float big = Billow(x / 900f, z / 900f, s, 4);
                        float small = Billow(x / 230f, z / 230f, s + 6, 3);
                        float band = Mathf.Sin(z / 700f + 2f * Fbm(x / 2500f, z / 2500f, s + 9, 3));
                        m = band * 0.5f + 0.5f;
                        return 90f + big * 230f + small * 60f;
                    };
                    p.padHeight = 280f; p.edgeLevel = 250f;
                    p.outpost = true; p.platformLift = 260f;
                    p.description = "the cloud tops, with a gas-mining platform riding above them";
                    L.skyTop = new Color(0.5f, 0.38f, 0.3f); L.skyBottom = new Color(0.62f, 0.44f, 0.3f); L.sunSize = 0.03f; L.sunGlow = 0.7f;
                    L.fog = new Color(0.78f, 0.6f, 0.44f); L.fogStart = 300f; L.fogEnd = 5200f;
                    L.sunColor = new Color(1f, 0.86f, 0.68f); L.sunIntensity = 1.8f; L.sunElevation = R(20f, 45f);
                    L.ambientSky = new Color(0.62f, 0.5f, 0.38f); L.ambientEquator = new Color(0.55f, 0.42f, 0.3f); L.ambientGround = new Color(0.45f, 0.32f, 0.2f);
                    break;
                }
                case SurfaceKind.IceGiant:
                {
                    float ridgeAngle = R(0f, Mathf.PI);
                    float ca = Mathf.Cos(ridgeAngle), sa = Mathf.Sin(ridgeAngle);
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float u = x * ca + z * sa, v = -x * sa + z * ca;
                        float h = 110f + 55f * Fbm(x / 1800f, z / 1800f, s, 5);
                        // long pressure ridges, crossed by crevasses
                        h += Mathf.Pow(Ridged(u / 2600f, v / 700f, s + 3, 4), 3f) * 330f;
                        float crev = Mathf.Pow(Ridged(v / 900f + 4.4f, u / 260f, s + 5, 2), 14f);
                        h -= crev * 45f;
                        // clusters of ice spires
                        float field = Smooth(0.15f, 0.4f, Fbm(x / 1100f + 9f, z / 1100f, s + 7, 3));
                        float spire = Mathf.Max(0f, V2(x / 70f, z / 70f, s + 8) - 0.72f) / 0.28f;
                        h += field * spire * spire * 110f;
                        m = crev;
                        return h;
                    };
                    p.padHeight = 120f; p.edgeLevel = 110f;
                    p.outpost = true;
                    p.boulders = 40; p.boulderSize = new Vector2(5f, 24f); p.rockTint = new Color(0.75f, 0.9f, 1f); p.boulderSlope = 0.6f;
                    p.description = "an ice sheet of pressure ridges, crevasses and ice spires under methane haze";
                    L.skyTop = new Color(0.26f, 0.46f, 0.56f); L.skyBottom = new Color(0.5f, 0.6f, 0.64f); L.sunSize = 0.014f; L.sunGlow = 0.45f;
                    L.fog = new Color(0.55f, 0.72f, 0.76f); L.fogStart = 900f; L.fogEnd = 9000f;
                    L.sunColor = new Color(0.85f, 0.95f, 1f); L.sunIntensity = 1.4f; L.sunElevation = R(12f, 35f);
                    L.ambientSky = new Color(0.45f, 0.6f, 0.68f); L.ambientEquator = new Color(0.38f, 0.5f, 0.55f); L.ambientGround = new Color(0.32f, 0.4f, 0.45f);
                    p.particles = new ParticleLook { color = new Color(0.9f, 0.97f, 1f, 0.55f), size = 1.0f, rate = 700f, fall = 3f, wind = 9f };
                    break;
                }
                case SurfaceKind.Moon:
                {
                    float mareBias = R(-0.25f, 0.1f);
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float h = 160f + 70f * Fbm(x / 1600f, z / 1600f, s, 5) + 12f * Fbm(x / 150f, z / 150f, s + 4, 3);
                        float mare = Smooth(0.05f, 0.25f, -Fbm(x / 3000f, z / 3000f, s + 6, 3) + mareBias);
                        h = Mathf.Lerp(h, 120f + 6f * Fbm(x / 400f, z / 400f, s + 8, 2), mare * 0.85f);
                        m = -mare;   // negative: dark maria (positive values are bright ejecta, from the craters)
                        return h;
                    };
                    p.padHeight = 150f; p.edgeLevel = 150f; p.curveRadius = 9000f;
                    p.craters = 160; p.craterSize = new Vector2(25f, 800f);
                    p.outpost = true;
                    p.boulders = 60; p.boulderSize = new Vector2(3f, 20f); p.rockTint = new Color(0.62f, 0.62f, 0.64f); p.boulderSlope = 0.7f;
                    p.description = "grey, airless and cratered";
                    L.space = true;
                    L.sunColor = new Color(1f, 0.97f, 0.92f); L.sunIntensity = 2.6f; L.sunElevation = R(10f, 40f);
                    L.ambientSky = new Color(0.07f, 0.07f, 0.09f); L.ambientEquator = new Color(0.05f, 0.05f, 0.06f); L.ambientGround = new Color(0.03f, 0.03f, 0.03f);
                    break;
                }
                case SurfaceKind.Asteroid:
                {
                    p.height = (float x, float z, int s, ref float m) =>
                    {
                        float lumps = Fbm(x / 1400f, z / 1400f, s, 4) * 180f;
                        float ridges = Mathf.Pow(Ridged(x / 700f, z / 700f, s + 3, 5), 2f) * 160f;
                        float grit = 10f * Fbm(x / 90f, z / 90f, s + 5, 3);
                        m = 0f;
                        return 220f + lumps + ridges + grit;
                    };
                    p.padHeight = 260f; p.edgeLevel = 220f; p.curveRadius = 6000f;
                    p.craters = 70; p.craterSize = new Vector2(40f, 650f);
                    p.boulders = 110; p.boulderSize = new Vector2(4f, 38f); p.rockTint = new Color(0.85f, 0.78f, 0.7f); p.boulderSlope = 0.9f;
                    p.floatingRocks = 26;
                    p.description = "a lump of dark rock, with the rest of the field drifting overhead";
                    L.space = true;
                    L.sunColor = new Color(1f, 0.96f, 0.9f); L.sunIntensity = 2.4f; L.sunElevation = R(15f, 60f);
                    L.ambientSky = new Color(0.08f, 0.08f, 0.1f); L.ambientEquator = new Color(0.05f, 0.05f, 0.06f); L.ambientGround = new Color(0.03f, 0.03f, 0.03f);
                    break;
                }
            }
            return p;
        }

        /// <summary>Stamp bowl craters (raised rims, bright ejecta) into the heights.</summary>
        static void Craters(float[,] h, float[,] mask, P p, System.Random rng, float step)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < p.craters; i++)
            {
                float rad = Mathf.Lerp(p.craterSize.x, p.craterSize.y, Mathf.Pow(R(0f, 1f), 3.2f));
                float cx = R(-Half, Half), cz = R(-Half, Half);
                float keep = PadRadius + 220f + rad * 2.2f;   // not on the pad zone or its blend, ejecta included
                if (cx * cx + cz * cz < keep * keep) continue;
                float depth = rad * R(0.16f, 0.24f), rim = rad * 0.06f;
                float reach = rad * 2.2f;
                int x0 = Mathf.Max(0, (int)((cx - reach + Half) / step)), x1 = Mathf.Min(Res - 1, (int)((cx + reach + Half) / step) + 1);
                int z0 = Mathf.Max(0, (int)((cz - reach + Half) / step)), z1 = Mathf.Min(Res - 1, (int)((cz + reach + Half) / step) + 1);
                for (int zi = z0; zi <= z1; zi++)
                    for (int xi = x0; xi <= x1; xi++)
                    {
                        float x = -Half + xi * step, z = -Half + zi * step;
                        float r = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / rad;
                        if (r > 2.2f) continue;
                        float d;
                        if (r < 1f) d = -depth * (1f - r * r) + rim * r * r * r;           // the bowl, up to the rim
                        else d = rim * Mathf.Exp(-(r - 1f) * 3.2f);                     // ejecta falling away outside
                        // keep the edges of the area level
                        float edge = Mathf.Min(Half - Mathf.Abs(x), Half - Mathf.Abs(z));
                        h[zi, xi] += d * Smooth(0f, 260f, edge);
                        if (r > 0.85f && r < 1.9f) mask[zi, xi] = Mathf.Max(mask[zi, xi], (1f - Mathf.Abs(r - 1.2f) / 0.7f) * Mathf.Clamp01(rad / 200f));
                    }
            }
        }

        static float DropAt(P p, float r) => p.curveRadius > 0f ? r * r / (2f * p.curveRadius) : 0f;

        static float Sample(float[,] h, float x, float z, float step)
        {
            float fx = Mathf.Clamp((x + Half) / step, 0f, Res - 1.001f), fz = Mathf.Clamp((z + Half) / step, 0f, Res - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            return Mathf.Lerp(Mathf.Lerp(h[iz, ix], h[iz, ix + 1], tx), Mathf.Lerp(h[iz + 1, ix], h[iz + 1, ix + 1], tx), tz);
        }

        static float Slope(float[,] h, float x, float z, float step)
        {
            float dx = Sample(h, x + step, z, step) - Sample(h, x - step, z, step);
            float dz = Sample(h, x, z + step, step) - Sample(h, x, z - step, step);
            return Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);   // rise over run
        }

        // ------------------------------------------------------------------ textures

        static readonly Dictionary<SurfaceKind, TerrainLayer[]> LayerCache = new Dictionary<SurfaceKind, TerrainLayer[]>();

        /// <summary>A tileable ground texture: a colour ramp over fractal noise, with grit and
        /// optional streaks (sand ripples) or cracks.</summary>
        static Texture2D GroundTex(string name, int seed, Color a, Color b, Color c, int period, float grit, float streaks = 0f, float cracks = 0f, float smooth = 0f)
        {
            const int N = 256;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float n = ProcNoise.TileFbm(u, v, period, 5, seed) * 0.5f + 0.5f;
                    var col = n < 0.5f ? Color.Lerp(a, b, n * 2f) : Color.Lerp(b, c, (n - 0.5f) * 2f);
                    float g = (ProcNoise.Hash(x, y, 7, seed) - 0.5f) * grit;
                    if (streaks > 0f)
                    {
                        float w = Mathf.Sin((v * 24f + ProcNoise.TileFbm(u, v, 4, 3, seed + 5) * 1.5f) * Mathf.PI * 2f);
                        g += w * streaks;
                    }
                    if (cracks > 0f)
                    {
                        float cr = 1f - Mathf.Abs(ProcNoise.TileFbm(u, v, period * 2, 3, seed + 9));
                        g -= Mathf.Pow(cr, 12f) * cracks;
                    }
                    col *= 1f + g;
                    col.a = smooth;     // the terrain shader reads smoothness from here
                    px[y * N + x] = col;
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        static TerrainLayer Layer(Texture2D t, float tile, float smooth = 0f)
        {
            return new TerrainLayer { diffuseTexture = t, tileSize = new Vector2(tile, tile), smoothness = smooth, metallic = 0f, name = t.name };
        }

        static TerrainLayer[] Layers(SurfaceKind kind, P p)
        {
            if (LayerCache.TryGetValue(kind, out var l) && l != null && l[0] != null && l[0].diffuseTexture != null) return l;
            l = kind switch
            {
                SurfaceKind.Volcanic => new[]
                {
                    Layer(GroundTex("basalt", 11, new Color(0.11f, 0.1f, 0.09f), new Color(0.19f, 0.16f, 0.14f), new Color(0.27f, 0.23f, 0.2f), 6, 0.25f, 0f, 0.6f), 40f),
                    Layer(GroundTex("ash", 12, new Color(0.2f, 0.19f, 0.18f), new Color(0.27f, 0.25f, 0.24f), new Color(0.34f, 0.32f, 0.3f), 8, 0.2f), 30f),
                    Layer(GroundTex("scoria", 13, new Color(0.18f, 0.08f, 0.05f), new Color(0.3f, 0.13f, 0.07f), new Color(0.42f, 0.2f, 0.11f), 5, 0.35f, 0f, 0.4f), 45f),
                    Layer(GroundTex("scorched", 14, new Color(0.25f, 0.06f, 0.02f), new Color(0.45f, 0.12f, 0.03f), new Color(0.7f, 0.25f, 0.05f), 6, 0.3f, 0f, 0.8f), 30f),
                },
                SurfaceKind.Desert => new[]
                {
                    Layer(GroundTex("sand", 21, new Color(0.72f, 0.5f, 0.32f), new Color(0.82f, 0.6f, 0.4f), new Color(0.9f, 0.72f, 0.52f), 5, 0.12f, 0.06f), 35f),
                    Layer(GroundTex("dunes", 22, new Color(0.62f, 0.4f, 0.24f), new Color(0.74f, 0.5f, 0.3f), new Color(0.84f, 0.6f, 0.38f), 4, 0.1f, 0.1f), 50f),
                    Layer(GroundTex("redrock", 23, new Color(0.36f, 0.18f, 0.11f), new Color(0.55f, 0.29f, 0.17f), new Color(0.7f, 0.42f, 0.27f), 6, 0.3f, 0f, 0.5f), 45f),
                    Layer(GroundTex("salt", 24, new Color(0.78f, 0.72f, 0.64f), new Color(0.88f, 0.84f, 0.78f), new Color(0.95f, 0.93f, 0.88f), 6, 0.08f, 0f, 0.7f), 40f),
                },
                SurfaceKind.Ocean => new[]
                {
                    Layer(GroundTex("grass", 31, new Color(0.1f, 0.2f, 0.06f), new Color(0.16f, 0.28f, 0.08f), new Color(0.26f, 0.34f, 0.12f), 6, 0.3f), 30f),
                    Layer(GroundTex("beach", 32, new Color(0.62f, 0.56f, 0.42f), new Color(0.74f, 0.68f, 0.52f), new Color(0.82f, 0.77f, 0.62f), 5, 0.12f, 0.03f), 25f),
                    Layer(GroundTex("cliff", 33, new Color(0.25f, 0.24f, 0.22f), new Color(0.38f, 0.36f, 0.33f), new Color(0.5f, 0.48f, 0.45f), 6, 0.3f, 0f, 0.5f), 45f),
                    Layer(GroundTex("snow", 34, new Color(0.82f, 0.85f, 0.9f), new Color(0.9f, 0.93f, 0.97f), new Color(0.97f, 0.98f, 1f), 5, 0.06f, 0f, 0f, 0.3f), 40f, 1f),
                },
                SurfaceKind.GasGiant => new[]
                {
                    Layer(GroundTex("cream", 41, new Color(0.82f, 0.7f, 0.55f), new Color(0.9f, 0.8f, 0.66f), new Color(0.96f, 0.9f, 0.8f), 3, 0.03f, 0.04f), 160f),
                    Layer(GroundTex("band", 42, new Color(0.66f, 0.42f, 0.24f), new Color(0.78f, 0.54f, 0.32f), new Color(0.86f, 0.66f, 0.44f), 3, 0.03f, 0.05f), 180f),
                    Layer(GroundTex("deep", 43, new Color(0.38f, 0.24f, 0.15f), new Color(0.48f, 0.32f, 0.2f), new Color(0.58f, 0.42f, 0.28f), 3, 0.03f), 160f),
                    Layer(GroundTex("tops", 44, new Color(0.95f, 0.92f, 0.86f), new Color(0.98f, 0.96f, 0.92f), new Color(1f, 1f, 0.98f), 3, 0.02f), 140f),
                },
                SurfaceKind.IceGiant => new[]
                {
                    Layer(GroundTex("snowfield", 51, new Color(0.78f, 0.86f, 0.9f), new Color(0.86f, 0.92f, 0.95f), new Color(0.94f, 0.97f, 0.99f), 5, 0.06f, 0.02f, 0f, 0.25f), 40f, 1f),
                    Layer(GroundTex("blueice", 52, new Color(0.3f, 0.55f, 0.68f), new Color(0.45f, 0.7f, 0.8f), new Color(0.62f, 0.82f, 0.9f), 4, 0.08f, 0f, 0.5f, 0.6f), 50f, 1f),
                    Layer(GroundTex("darkice", 53, new Color(0.14f, 0.22f, 0.28f), new Color(0.22f, 0.32f, 0.38f), new Color(0.32f, 0.44f, 0.5f), 6, 0.2f, 0f, 0.6f, 0.35f), 40f, 1f),
                    Layer(GroundTex("frost", 54, new Color(0.6f, 0.7f, 0.76f), new Color(0.7f, 0.8f, 0.84f), new Color(0.8f, 0.88f, 0.9f), 6, 0.15f, 0f, 0f, 0.2f), 35f, 1f),
                },
                SurfaceKind.Moon => new[]
                {
                    Layer(GroundTex("regolith", 61, new Color(0.3f, 0.3f, 0.3f), new Color(0.38f, 0.38f, 0.37f), new Color(0.46f, 0.46f, 0.45f), 6, 0.25f), 35f),
                    Layer(GroundTex("mare", 62, new Color(0.13f, 0.13f, 0.14f), new Color(0.18f, 0.18f, 0.19f), new Color(0.24f, 0.24f, 0.25f), 6, 0.2f), 40f),
                    Layer(GroundTex("ejecta", 63, new Color(0.5f, 0.5f, 0.49f), new Color(0.6f, 0.6f, 0.59f), new Color(0.7f, 0.7f, 0.69f), 6, 0.25f), 30f),
                    Layer(GroundTex("moonrock", 64, new Color(0.2f, 0.2f, 0.2f), new Color(0.28f, 0.28f, 0.28f), new Color(0.36f, 0.36f, 0.35f), 5, 0.35f, 0f, 0.6f), 45f),
                },
                _ => new[]
                {
                    Layer(GroundTex("dust", 71, new Color(0.18f, 0.16f, 0.14f), new Color(0.24f, 0.22f, 0.19f), new Color(0.3f, 0.27f, 0.23f), 6, 0.25f), 35f),
                    Layer(GroundTex("stone", 72, new Color(0.14f, 0.12f, 0.11f), new Color(0.22f, 0.19f, 0.17f), new Color(0.32f, 0.28f, 0.24f), 5, 0.35f, 0f, 0.7f), 45f),
                    Layer(GroundTex("fresh", 73, new Color(0.34f, 0.3f, 0.27f), new Color(0.42f, 0.38f, 0.34f), new Color(0.52f, 0.47f, 0.42f), 6, 0.3f), 30f),
                    Layer(GroundTex("gravel", 74, new Color(0.12f, 0.11f, 0.1f), new Color(0.2f, 0.18f, 0.16f), new Color(0.28f, 0.25f, 0.22f), 8, 0.5f), 25f),
                },
            };
            LayerCache[kind] = l;
            return l;
        }

        /// <summary>How much of each texture where: by height, slope and the extra mask.</summary>
        static float[,,] Splat(float[,] h, float[,] mask, P p, int n, float step, int seed)
        {
            var a = new float[SplatRes, SplatRes, n];
            float cell = Size / SplatRes;
            var w = new float[n];
            for (int zi = 0; zi < SplatRes; zi++)
                for (int xi = 0; xi < SplatRes; xi++)
                {
                    float x = -Half + (xi + 0.5f) * cell, z = -Half + (zi + 0.5f) * cell;
                    float ht = Sample(h, x, z, step);
                    float slope = Slope(h, x, z, step);
                    int mi = Mathf.Clamp(Mathf.RoundToInt((z + Half) / step), 0, Res - 1), mx = Mathf.Clamp(Mathf.RoundToInt((x + Half) / step), 0, Res - 1);
                    float m = mask[mi, mx];
                    float nz = Fbm(x / 300f, z / 300f, seed + 77, 3) * 0.5f + 0.5f;
                    for (int i = 0; i < n; i++) w[i] = 0f;
                    float steep = Smooth(0.35f, 0.7f, slope);
                    switch (p.kind)
                    {
                        case SurfaceKind.Volcanic:
                            w[0] = (1f - steep) * (0.4f + nz);
                            w[1] = (1f - steep) * Smooth(120f, 220f, ht) * 1.6f;     // ash on the high slopes
                            w[2] = steep * 1.5f;
                            w[3] = m * 2.5f;
                            break;
                        case SurfaceKind.Desert:
                            w[0] = (1f - steep) * (0.5f + nz);
                            w[1] = (1f - steep) * (1.2f - nz) * 0.8f;
                            w[2] = steep * 2f + Smooth(150f, 190f, ht) * 0.6f;
                            w[3] = m * 2.5f * (1f - steep);
                            break;
                        case SurfaceKind.Ocean:
                        {
                            float above = ht - p.seaLevel;
                            float beach = 1f - Smooth(4f, 14f, above);
                            float snow = Smooth(p.snowLine - 40f, p.snowLine + 40f, ht + nz * 60f);
                            w[1] = beach * 3f;
                            w[0] = (1f - beach) * (1f - steep) * (1f - snow);
                            w[2] = steep * 1.8f * (1f - beach) + Smooth(380f, 520f, ht) * 0.4f;
                            w[3] = snow * (1f - steep * 0.7f) * 2f;
                            break;
                        }
                        case SurfaceKind.GasGiant:
                            w[0] = 0.6f + nz * 0.4f;
                            w[1] = m * 1.4f;
                            w[2] = (1f - Smooth(110f, 190f, ht)) * 0.8f;
                            w[3] = Smooth(280f, 360f, ht) * 1.5f;
                            break;
                        case SurfaceKind.IceGiant:
                            w[0] = (1f - steep) * (0.6f + nz);
                            w[1] = steep * 1.6f + Smooth(260f, 380f, ht) * 0.5f;
                            w[2] = Mathf.Clamp01(m * 3f) * 2.5f;
                            w[3] = (1f - steep) * (1f - nz) * 0.6f;
                            break;
                        case SurfaceKind.Moon:
                            w[0] = (1f - steep) * (0.6f + nz * 0.5f);
                            w[1] = Mathf.Clamp01(-m) * 2.2f;
                            w[2] = Mathf.Clamp01(m) * 2.4f;
                            w[3] = steep * 1.6f;
                            break;
                        default:
                            w[0] = (1f - steep) * (0.6f + nz);
                            w[1] = steep * 1.8f;
                            w[2] = Mathf.Clamp01(m) * 2f;
                            w[3] = (1f - steep) * (1f - nz) * 0.7f;
                            break;
                    }
                    float sum = 0f;
                    for (int i = 0; i < n; i++) sum += w[i];
                    if (sum < 1e-4f) { w[0] = 1f; sum = 1f; }
                    for (int i = 0; i < n; i++) a[zi, xi, i] = w[i] / sum;
                }
            return a;
        }

        static Texture2D lavaTex;
        static Texture2D LavaTexture()
        {
            if (lavaTex != null) return lavaTex;
            const int N = 256;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float n = ProcNoise.TileFbm(u, v, 4, 5, 901) * 0.5f + 0.5f;
                    float crust = Mathf.Pow(1f - Mathf.Abs(ProcNoise.TileFbm(u, v, 6, 4, 907)), 6f);
                    float heat = Mathf.Clamp01(0.25f + n * 0.9f - crust * 0.8f);
                    px[y * N + x] = new Color(heat, heat * heat * 0.8f, heat * heat * heat * 0.4f);
                }
            lavaTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "lava", wrapMode = TextureWrapMode.Repeat };
            lavaTex.SetPixels(px);
            lavaTex.Apply(true, true);
            return lavaTex;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>The ground beyond the landing area, out to the horizon: a grid around the
        /// terrain's square at its edge height, curving away like the terrain on small bodies.</summary>
        static Mesh Ring(P p, Vector3 center, float tile)
        {
            const float cellSize = 500f;
            int n = Mathf.RoundToInt(RingExtent * 2f / cellSize);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int zi = 0; zi <= n; zi++)
                for (int xi = 0; xi <= n; xi++)
                {
                    float x = -RingExtent + xi * cellSize, z = -RingExtent + zi * cellSize;
                    float r = Mathf.Sqrt(x * x + z * z);
                    // just under the terrain's edge, so the seam never shows a gap
                    verts.Add(new Vector3(x, p.edgeLevel - DropAt(p, r) - 0.5f, z));
                    uvs.Add(new Vector2((x + center.x) / tile, (z + center.z) / tile));
                }
            for (int zi = 0; zi < n; zi++)
                for (int xi = 0; xi < n; xi++)
                {
                    float x0 = -RingExtent + xi * cellSize, z0 = -RingExtent + zi * cellSize;
                    if (x0 >= -Half && x0 + cellSize <= Half && z0 >= -Half && z0 + cellSize <= Half) continue;   // under the terrain
                    int i = zi * (n + 1) + xi;
                    tris.Add(i); tris.Add(i + n + 1); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + n + 1); tris.Add(i + n + 2);
                }
            var m = new Mesh { name = "Horizon", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad(float extent, float tile)
        {
            var m = new Mesh { name = "Plane" };
            m.vertices = new[] { new Vector3(-extent, 0, -extent), new Vector3(-extent, 0, extent), new Vector3(extent, 0, extent), new Vector3(extent, 0, -extent) };
            float t = extent * 2f / tile;
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, t), new Vector2(t, t), new Vector2(t, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ weather

        static void Weather(Transform root, P p, SurfaceKit kit, SurfaceSite site)
        {
            var w = p.particles;
            if (kit.dust != null) MakeParticles(root, "Weather", kit.dust, w.color, w.size, w.rate, w.fall, w.wind, 700f);
            if (w.embers && kit.embers != null) MakeParticles(root, "Embers", kit.embers, new Color(1f, 0.45f, 0.12f, 1f), 0.9f, 140f, -5f, 3f, 600f);
        }

        static void MakeParticles(Transform root, string name, Material mat, Color color, float size, float rate, float fall, float wind, float box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 14f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.5f);
            main.startColor = color;
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(box, box * 0.6f, box);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind * 0.6f, wind * 1.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-fall * 1.3f, -fall * 0.7f);
            vel.z = new ParticleSystem.MinMaxCurve(wind * 0.2f, wind * 0.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<FollowCamera>();
            ps.Play();
        }
    }

    /// <summary>Keeps a weather emitter round the camera.</summary>
    public class FollowCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.position = cam.transform.position + cam.transform.forward * 150f;
        }
    }

    /// <summary>Slides a material's texture (lava creeping).</summary>
    public class SurfaceScroll : MonoBehaviour
    {
        public Vector2 speed;
        Material m;
        void Start() => m = GetComponent<Renderer>().sharedMaterial;
        void Update() { if (m != null) m.SetTextureOffset("_BaseMap", speed * Time.time); }
    }
}
