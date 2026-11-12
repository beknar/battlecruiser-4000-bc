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
        [Tooltip("Colony buildings put up round a friendly landing pad (domes, habitats, a tower...).")]
        public GameObject[] colony;
        [Tooltip("Fortress buildings round a hostile base's pad (bunkers, a command post, radar...).")]
        public GameObject[] fort;

        [Header("Industry (IndustryTable sites on the ground)")]
        [Tooltip("An industrial site's landing pad (its deck is a 'Pad ' collider under an 'Outpost' root).")]
        public GameObject industryPad;
        [Tooltip("A refinery and a factory, built beside the pad.")]
        public GameObject industryRefinery, industryFactory;
        [Tooltip("The pad's landable radius and deck height (m).")]
        public float industryPadRadius = 140f, industryDeck = 6f;

        [Header("Ground forces (hostile worlds)")]
        [Tooltip("A heavy turret for the gun batteries.")]
        public CarrierTurret battery;
        public Projectile cannon, blaster;
        public Missile missile;
        [Tooltip("What the sea bases and submarines fire (a Missile that keeps under the water).")]
        public Missile torpedo;
        [Tooltip("Ships that patrol the sky and launch from the bases' pads.")]
        public GameObject fighter, drone, gunship;
        public Explosion explosion, explosionSmall;
        [Tooltip("Lit: bunkers and launchers; vehicle armour; red warning lights (emissive).")]
        public Material bunker, armor, warning;
    }

    /// <summary>Where the ship came down on a planet (the streamed surface round it is a
    /// PlanetSurface): the area it arrived in, and the landing pad it was brought in to.</summary>
    public class SurfaceSite
    {
        public PlanetSurface surface;
        public GameObject root;         // the surface (everything down there is under it)
        public Terrain terrain;         // the area the ship arrived in
        public SurfaceKind kind;
        public Vector3 center;          // the middle of the world down there (PlanetLanding.SiteCenter)
        public Vector3 pad;             // top of the landing pad it was brought in to (kept up to date as the world shifts)
        public bool hasOutpost;
        public Transform outpost;       // that pad (null if it came down somewhere without one)
        public float seaLevel = float.NegativeInfinity;   // world height of the sea / lava (or -inf)
        public bool water;              // the sea is water (not lava): ships can sail it and go under
        public bool solidGround = true; // false for a gas giant's cloud deck
        public float floor;             // world height the ship can't go below (cloud deck)
        public string description;
        public Look look;

        /// <summary>The ground's height under a point.</summary>
        public float GroundAt(Vector3 p) => surface != null ? surface.GroundAt(p) : center.y;

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
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

    public class ParticleLook
    {
        public Color color = Color.white;
        public float size = 1.5f, rate = 600f, fall = 4f, wind = 2f;
        public bool embers;
    }

    /// <summary>
    /// The shared pieces of the planet surfaces (PlanetSurface streams the ground itself, from a
    /// SurfaceWorld): the ground textures for each kind of world, the lava texture, flat planes,
    /// boulders and the weather.
    /// </summary>
    public static class SurfaceGenerator
    {
        /// <summary>The size of one area of a planet's surface (m).</summary>
        public const float Size = 5000f;

        /// <summary>A boulder from the asteroid generator (built inactive so it is shaped once).</summary>
        public static GameObject Boulder(Transform parent, Vector3 pos, float size, int seed, Material mat, int layer, float yaw, float flat)
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

        public static TerrainLayer[] Layers(SurfaceKind kind)
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

        static Texture2D lavaTex;
        public static Texture2D LavaTexture()
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

        public static Mesh Quad(float extent, float tile)
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

        public static void Weather(Transform root, ParticleLook w, SurfaceKit kit)
        {
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
