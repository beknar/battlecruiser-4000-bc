using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using CarrierGame.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The third level, "Star System": the Kestrel system, which the frigate crosses by mini-warp.
    /// A star; five planets of different kinds and sizes (volcanic, desert, ocean, gas giant, ice
    /// giant), the ocean world with two moons and the gas giant with four; three asteroid fields,
    /// two of them with hostiles hiding in them. Every place has a WarpPoint for the system map.
    /// The planets are textured here (seamless noise on the sphere) and lit by the star's real
    /// position (Carrier/Planet shader); the moons and asteroids are generated from seeds when the
    /// level loads. Distances are compressed to keep every coordinate within ±300 km.
    /// </summary>
    public static partial class CarrierBuilder
    {
        public const string StarSystemScenePath = Root + "/Scenes/StarSystem.unity";
        const string ArtDir = Root + "/Art/Kenney";

        static Vector3 Polar(float angleDeg, float distKm, float yKm)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * distKm, yKm, Mathf.Sin(a) * distKm) * 1000f;
        }

        // ------------------------------------------------------------------ planet textures

        /// <summary>Unit direction for an equirectangular texel (matching ProceduralSphere's UVs).</summary>
        static Vector3 SphereDir(float u, float v)
        {
            float lon = (1f - u) * Mathf.PI * 2f, lat = v * Mathf.PI - Mathf.PI / 2f;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
        }

        delegate (Color albedo, Color glow) Texel(Vector3 d, float lat);

        static (Texture2D albedo, Texture2D glow) PlanetMaps(string name, int w, int h, Texel texel, bool withGlow)
        {
            var a = new Color[w * h];
            var g = withGlow ? new Color[w * h] : null;
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float lat = v * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    var (c, e) = texel(SphereDir((x + 0.5f) / w, v), lat);
                    a[y * w + x] = c;
                    if (g != null) g[y * w + x] = e;
                }
            }
            var at = SaveTexture($"T_{name}", a, w, h, false, true);
            var gt = withGlow ? SaveTexture($"T_{name}_Glow", g, w, h, false, true) : null;
            return (at, gt);
        }

        static Color Ramp(float t, params Color[] cs)
        {
            t = Mathf.Clamp01(t) * (cs.Length - 1);
            int i = Mathf.Min(cs.Length - 2, (int)t);
            return Color.Lerp(cs[i], cs[i + 1], t - i);
        }

        static (Texture2D, Texture2D) VolcanicMaps() => PlanetMaps("Planet_Volcanic", 1024, 512, (d, lat) =>
        {
            float rock = ProcNoise.Fbm(d * 3f, 501, 5) * 0.5f + 0.5f;
            float crack = Mathf.Pow(ProcNoise.Ridged(d * 4.5f + Vector3.one * 3f, 503, 5), 5f);
            float lava = Edge(0.12f, 0.4f, crack);
            var basalt = Color.Lerp(new Color(0.06f, 0.05f, 0.05f), new Color(0.2f, 0.16f, 0.14f), rock);
            var albedo = Color.Lerp(basalt, new Color(0.45f, 0.12f, 0.03f), lava);
            var glow = Color.Lerp(Color.black, new Color(1f, 0.42f, 0.08f), lava) * (0.6f + 0.4f * rock);
            return (albedo, glow);
        }, true);

        static Texture2D DesertMap() => PlanetMaps("Planet_Desert", 1024, 512, (d, lat) =>
        {
            float n = ProcNoise.Fbm(d * 2.4f, 601, 6) * 0.5f + 0.5f;
            float dunes = Mathf.Sin(lat * 60f + ProcNoise.Fbm(d * 6f, 607, 3) * 8f) * 0.5f + 0.5f;
            var c = Ramp(n, new Color(0.38f, 0.25f, 0.17f), new Color(0.72f, 0.42f, 0.22f), new Color(0.86f, 0.66f, 0.42f), new Color(0.92f, 0.8f, 0.6f));
            c *= 0.92f + 0.1f * dunes;
            float basin = Edge(0.62f, 0.8f, ProcNoise.Fbm(d * 1.3f + Vector3.one * 7f, 611, 3) * 0.5f + 0.5f);
            c = Color.Lerp(c, new Color(0.5f, 0.3f, 0.2f), basin * 0.5f);
            return (c, Color.black);
        }, false).albedo;

        static Texture2D OceanMap() => PlanetMaps("Planet_Ocean", 2048, 1024, (d, lat) =>
        {
            float h = ProcNoise.Fbm(d * 2.1f, 701, 6) + 0.25f * ProcNoise.Fbm(d * 7f, 709, 4);
            Color c;
            if (h < 0.06f)
                c = Color.Lerp(new Color(0.01f, 0.035f, 0.12f), new Color(0.03f, 0.14f, 0.28f), Mathf.InverseLerp(-0.35f, 0.06f, h));
            else
                c = Ramp(Mathf.InverseLerp(0.06f, 0.5f, h), new Color(0.55f, 0.5f, 0.34f), new Color(0.11f, 0.25f, 0.08f),
                         new Color(0.2f, 0.26f, 0.1f), new Color(0.33f, 0.26f, 0.17f), new Color(0.5f, 0.5f, 0.5f));
            float ice = Edge(0.72f, 0.82f, Mathf.Abs(lat) + 0.06f * ProcNoise.Fbm(d * 5f, 713, 3));
            c = Color.Lerp(c, new Color(0.92f, 0.95f, 0.98f), ice);
            float cloud = Edge(0.18f, 0.6f, ProcNoise.Fbm(d * 3.2f + Vector3.one * 11f, 719, 5)) * 0.65f;
            c = Color.Lerp(c, new Color(0.95f, 0.96f, 0.98f), cloud);
            return (c, Color.black);
        }, false).albedo;

        static Texture2D IceGiantMap() => PlanetMaps("Planet_IceGiant", 1024, 512, (d, lat) =>
        {
            float warp = ProcNoise.Fbm(d * 2.5f, 801, 4);
            float band = Mathf.Sin(lat * 9f + warp * 1.6f) * 0.35f + Mathf.Sin(lat * 23f + warp * 3f) * 0.15f + 0.5f;
            var c = Ramp(band, new Color(0.18f, 0.42f, 0.55f), new Color(0.36f, 0.66f, 0.74f), new Color(0.58f, 0.82f, 0.86f), new Color(0.28f, 0.52f, 0.66f));
            c = Color.Lerp(c, new Color(0.85f, 0.95f, 0.97f), Edge(0.8f, 1f, Mathf.Abs(lat)) * 0.5f);
            return (c, Color.black);
        }, false).albedo;

        /// <summary>A smooth radial glow for the star's corona (black outside, so it adds nothing there).</summary>
        static Texture2D StarGlowTexture()
        {
            const int N = 512;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ray = 0.85f + 0.15f * Mathf.Sin(Mathf.Atan2(dy, dx) * 9f) * Mathf.Sin(Mathf.Atan2(dy, dx) * 4f + 1f);
                    float k = Mathf.Clamp01(1f - r) ;
                    float v = Mathf.Pow(k, 2.6f) * ray + Mathf.Pow(k, 9f) * 0.6f;
                    px[y * N + x] = new Color(v, v, v);
                }
            var t = SaveTexture("T_StarGlow", px, N, N, false, true);
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter ti && ti.wrapModeU != TextureWrapMode.Clamp)
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return t;
        }

        static Material PlanetMat(string name, Texture2D albedo, Texture2D glow, Color glowColor, float ambient)
        {
            var m = new Material(Shader.Find("Carrier/Planet"));
            m.SetTexture("_BaseMap", albedo);
            if (glow) { m.SetTexture("_EmissionMap", glow); m.SetColor("_EmissionColor", glowColor); }
            m.SetFloat("_Ambient", ambient);
            return SaveMat(name, m);
        }

        static Material AtmosphereMat(string name, Color c, float rim, float strength)
        {
            var m = SaveMat(name, new Material(Shader.Find("Carrier/PlanetAtmosphere")));
            m.SetColor("_Color", c);
            m.SetFloat("_RimPower", rim);
            m.SetFloat("_Strength", strength);
            m.SetFloat("_NightLevel", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D KenneyTex(string name)
        {
            string path = $"{ArtDir}/{name}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (!ti.alphaIsTransparency || ti.mipmapEnabled == false))
            {
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return Load<Texture2D>(path);
        }

        // ------------------------------------------------------------------ bodies

        static GameObject MakePlanet(string name, Transform parent, Vector3 pos, float radius, Material mat, Material atmo, float atmoScale, int layer, float spin)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(UnityEngine.Random.Range(-15f, 15f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-10f, 10f)));
            go.transform.localScale = Vector3.one * radius * 2f;
            go.layer = layer;
            go.AddComponent<ProceduralSphere>().Build();
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<SphereCollider>().radius = 0.5f;
            var ob = go.AddComponent<Obstacle>();
            ob.radius = radius * 1.02f;
            ob.coreRadius = radius;
            go.AddComponent<Drift>().spin = new Vector3(0f, spin, 0f);
            if (atmo != null)
            {
                var a = new GameObject("Atmosphere", typeof(MeshFilter), typeof(MeshRenderer));
                a.transform.SetParent(go.transform, false);
                a.transform.localScale = Vector3.one * atmoScale;
                a.AddComponent<ProceduralSphere>().Build();
                var ar = a.GetComponent<MeshRenderer>();
                ar.sharedMaterial = atmo;
                ar.shadowCastingMode = ShadowCastingMode.Off;
                ar.receiveShadows = false;
            }
            return go;
        }

        static WarpPoint MakeWarpPoint(GameObject go, string display, WarpPoint.Kind kind, string desc, float bodyRadius, float arrival,
                                       Color mapColor, WarpPoint parent = null, bool hostile = false)
        {
            var wp = go.AddComponent<WarpPoint>();
            wp.displayName = display;
            wp.kind = kind;
            wp.description = desc;
            wp.bodyRadius = bodyRadius;
            wp.arrivalDistance = arrival;
            wp.mapColor = mapColor;
            wp.orbitParent = parent;
            wp.hostile = hostile;
            return wp;
        }

        /// <summary>A field of rocks round a point, flattened into a lens; returns the big rocks.</summary>
        static List<Vector3> MakeField(string name, Transform parent, Vector3 center, float radius, int count, int seed, Material mat, int layer)
        {
            var field = new GameObject(name).transform;
            field.SetParent(parent, false);
            field.position = center;
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var placed = new List<(Vector3 p, float r)>();
            var big = new List<Vector3>();
            var axes = new List<Vector3>();
            for (int v = 0; v < 8; v++) axes.Add(new Vector3(1f, R(0.55f, 0.95f), R(0.7f, 1f)));
            for (int attempt = 0; attempt < count * 40 && placed.Count < count; attempt++)
            {
                Vector3 d;
                do d = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)); while (d.sqrMagnitude > 1f);
                Vector3 pos = center + Vector3.Scale(d, new Vector3(radius, radius * 0.35f, radius));
                float rr = 20f + 200f * Mathf.Pow(R(0f, 1f), 2.4f);
                if (placed.Count < 8) rr = R(380f, 700f);            // a few big ones to hide behind
                if (placed.Any(q => Vector3.Distance(q.p, pos) < (q.r + rr) * 1.25f)) continue;
                placed.Add((pos, rr));
                if (rr > 350f) big.Add(pos);
                int variant = rr > 150f ? rng.Next(3) : 3 + rng.Next(5);
                MakeAsteroid("Rock", field, pos, Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), rr,
                    100 + variant, rr > 150f ? 4 : 3, 2, axes[variant], 0.28f, 6, 0.45f, mat, layer, 1.3f, 0.5f);
            }
            Log($"{name}: {placed.Count} rocks within {radius / 1000f:F1} km");
            return big;
        }

        // ------------------------------------------------------------------ the level

        static void BuildStarSystemScene(Prefabs p, GameObject frigatePrefab)
        {
            int layer = ObstacleLayer();
            UnityEngine.Random.InitState(4711);
            ProceduralAsteroid.ClearCache();
            var rockMat = Mats.TryGetValue("Asteroid_Rock", out var rm) ? rm : Load<Material>(MatDir + "/Asteroid_Rock.mat");
            var gasTex = Load<Texture2D>(EnvDir + "/T_GasGiant.png");
            var (volcAlbedo, volcGlow) = VolcanicMaps();
            var desertTex = DesertMap();
            var oceanTex = OceanMap();
            var iceTex = IceGiantMap();

            var matVolc = PlanetMat("Planet_Volcanic", volcAlbedo, volcGlow, new Color(2.2f, 1.1f, 0.4f), 0.02f);
            var matDesert = PlanetMat("Planet_Desert", desertTex, null, Color.black, 0.025f);
            var matOcean = PlanetMat("Planet_Ocean", oceanTex, null, Color.black, 0.025f);
            var matGas = PlanetMat("Planet_GasGiantLit", gasTex, null, Color.black, 0.03f);
            var matIce = PlanetMat("Planet_IceGiant", iceTex, null, Color.black, 0.03f);
            var atmoOcean = AtmosphereMat("Atmo_Ocean", new Color(0.35f, 0.6f, 1.6f), 4f, 1.7f);
            var atmoDesert = AtmosphereMat("Atmo_Desert", new Color(1.2f, 0.75f, 0.45f), 6f, 0.9f);
            var atmoGas = AtmosphereMat("Atmo_GasGiant", new Color(1.1f, 0.85f, 0.6f), 5f, 1.4f);
            var atmoIce = AtmosphereMat("Atmo_IceGiant", new Color(0.5f, 0.95f, 1.2f), 4.5f, 1.5f);
            var atmoVolc = AtmosphereMat("Atmo_Volcanic", new Color(1.4f, 0.45f, 0.2f), 6f, 0.8f);

            var starMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            starMat.SetColor("_BaseColor", new Color(14f, 10.5f, 6.5f));
            starMat = SaveMat("Star_Surface", starMat);
            var coronaMat = ParticleAdditive("Star_Corona", StarGlowTexture(), new Color(1.5f, 1.05f, 0.6f, 1f));
            var streakMat = ParticleAdditive("FX_WarpStreak", KenneyTex("trace_01_rotated"), new Color(1.6f, 1.8f, 2.2f, 1f));
            var hazeMat = ParticleAdditive("FX_WarpHaze", KenneyTex("light_03"), new Color(0.6f, 0.75f, 1.4f, 0.6f));
            var flashMat = ParticleAdditive("FX_WarpFlash", KenneyTex("star_06"), new Color(2.5f, 2.7f, 3f, 1f));
            var ringMat = ParticleAdditive("FX_WarpRing", KenneyTex("circle_05"), new Color(1.4f, 1.8f, 2.6f, 1f));
            var sparkMat = ParticleAdditive("FX_WarpSpark", KenneyTex("flare_01"), new Color(1.5f, 1.8f, 2.4f, 1f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SpaceLighting(Quaternion.identity, 2.4f, new Color(1f, 0.93f, 0.82f), new Color(0.05f, 0.05f, 0.07f));
            var sun = RenderSettings.sun;
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.65f, 0.9f);
            fill.intensity = 0.18f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.2f, 1f, -0.3f));
            var sys = new GameObject("Kestrel System").transform;

            // the star
            var star = new GameObject("Kestrel", typeof(MeshFilter), typeof(MeshRenderer));
            star.transform.SetParent(sys, false);
            star.transform.localScale = Vector3.one * 36000f;
            star.layer = layer;
            star.AddComponent<ProceduralSphere>().Build();
            star.GetComponent<MeshRenderer>().sharedMaterial = starMat;
            star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            star.AddComponent<SphereCollider>().radius = 0.5f;
            var sob = star.AddComponent<Obstacle>();
            sob.radius = 18500f; sob.coreRadius = 18000f;
            var corona = GameObject.CreatePrimitive(PrimitiveType.Quad);
            StripColliders(corona);
            corona.name = "Corona";
            corona.transform.SetParent(sys, false);
            corona.transform.localScale = Vector3.one * 120000f;
            var cr = corona.GetComponent<MeshRenderer>();
            cr.sharedMaterial = coronaMat;
            cr.shadowCastingMode = ShadowCastingMode.Off;
            var sl = star.AddComponent<StarLight>();
            sl.sun = sun;
            sl.corona = corona.transform;
            var wStar = MakeWarpPoint(star, "Kestrel", WarpPoint.Kind.Star,
                "A yellow-white main-sequence star. A close pass gives a spectacular view, and nowhere to hide.",
                18000f, 62000f, new Color(1f, 0.85f, 0.45f));

            // the planets
            var cinder = MakePlanet("Cinder", sys, Polar(20f, 55f, 0.4f), 3200f, matVolc, atmoVolc, 1.03f, layer, 0.4f);
            var wCinder = MakeWarpPoint(cinder, "Cinder", WarpPoint.Kind.Planet,
                "A small volcanic world close to the star, its crust split by rivers of lava. A corsair patrol (a heavy cruiser and two destroyers) holds station here.",
                3200f, 9000f, new Color(1f, 0.45f, 0.2f), null, true);
            wCinder.surface = SurfaceKind.Volcanic;
            var ochre = MakePlanet("Ochre", sys, Polar(135f, 92f, -1f), 5000f, matDesert, atmoDesert, 1.025f, layer, 0.3f);
            var wOchre = MakeWarpPoint(ochre, "Ochre", WarpPoint.Kind.Planet,
                "A dry desert planet of dune seas and dust basins under a thin, hazy atmosphere.", 5000f, 13000f, new Color(0.9f, 0.65f, 0.4f));
            wOchre.surface = SurfaceKind.Desert;
            var thalassa = MakePlanet("Thalassa", sys, Polar(250f, 128f, 0.8f), 6500f, matOcean, atmoOcean, 1.04f, layer, 0.25f);
            var wThalassa = MakeWarpPoint(thalassa, "Thalassa", WarpPoint.Kind.Planet,
                "An ocean world with scattered continents, polar ice and a thick blue atmosphere. Two moons.", 6500f, 17000f, new Color(0.35f, 0.6f, 1f));
            wThalassa.surface = SurfaceKind.Ocean;
            var jovara = MakePlanet("Jovara", sys, Polar(330f, 178f, 2f), 16000f, matGas, atmoGas, 1.03f, layer, 0.15f);
            var wJovara = MakeWarpPoint(jovara, "Jovara", WarpPoint.Kind.Planet,
                "A banded gas giant with a great storm, circled by four moons.", 16000f, 42000f, new Color(0.95f, 0.75f, 0.55f));
            wJovara.surface = SurfaceKind.GasGiant;
            var glacis = MakePlanet("Glacis", sys, Polar(72f, 232f, -2f), 11000f, matIce, atmoIce, 1.035f, layer, 0.2f);
            var wGlacis = MakeWarpPoint(glacis, "Glacis", WarpPoint.Kind.Planet,
                "A pale ice giant at the cold edge of the system, wrapped in methane haze. A pirate dreadnought and its destroyer escort guard the approach.",
                11000f, 30000f, new Color(0.6f, 0.9f, 0.95f), null, true);
            wGlacis.surface = SurfaceKind.IceGiant;

            // the moons: two round Thalassa, four round Jovara
            // every moon is a warp destination
            void Moon(string name, GameObject planet, WarpPoint parentWp, float angle, float distKm, float radius, int seed, string desc)
            {
                Vector3 pos = planet.transform.position + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0.03f, Mathf.Sin(angle * Mathf.Deg2Rad)) * distKm * 1000f;
                var m = MakeAsteroid(name, sys, pos, Quaternion.Euler(0f, angle, 0f), 1f, seed, 5, 3, new Vector3(radius, radius * 0.97f, radius),
                                     0.05f, 40, radius / 6f, rockMat, layer, radius * 1.15f, radius * 0.9f);
                m.GetComponent<Obstacle>().cover = true;
                MakeWarpPoint(m, name, WarpPoint.Kind.Moon, desc, radius, radius * 3.2f + 1500f, new Color(0.75f, 0.75f, 0.78f), parentWp).surface = SurfaceKind.Moon;
            }
            Moon("Selene", thalassa, wThalassa, 35f, 16f, 1400f, 901, "Thalassa's larger moon: grey, airless and heavily cratered.");
            Moon("Mnemos", thalassa, wThalassa, 205f, 24f, 900f, 902, "A small moon of Thalassa.");
            Moon("Brontes", jovara, wJovara, 10f, 34f, 2400f, 903, "Jovara's inner moon, scarred by ancient impacts.");
            Moon("Arges", jovara, wJovara, 125f, 46f, 1600f, 904, "A small moon of Jovara.");
            Moon("Steropes", jovara, wJovara, 235f, 60f, 2800f, 905, "Jovara's largest moon, with deep craters along its terminator.");
            Moon("Pyrois", jovara, wJovara, 300f, 75f, 1100f, 906, "A small moon of Jovara.");

            // the asteroid fields
            var innerC = Polar(80f, 76f, 0f);
            MakeField("Inner Belt", sys, innerC, 4500f, 110, 3101, rockMat, layer);
            var fInner = new GameObject("Inner Belt (warp point)");
            fInner.transform.SetParent(sys, false);
            fInner.transform.position = innerC;
            MakeWarpPoint(fInner, "Inner Belt", WarpPoint.Kind.AsteroidField,
                "A quiet cluster of rock between the inner planets. Good cover, no contacts.", 4500f, 7000f, new Color(0.7f, 0.62f, 0.52f)).surface = SurfaceKind.Asteroid;

            var trojanC = Polar(30f, 178f, 1f);
            var trojanBig = MakeField("Jovara Trojans", sys, trojanC, 5000f, 120, 3202, rockMat, layer);
            var fTrojan = new GameObject("Jovara Trojans (warp point)");
            fTrojan.transform.SetParent(sys, false);
            fTrojan.transform.position = trojanC;
            MakeWarpPoint(fTrojan, "Jovara Trojans", WarpPoint.Kind.AsteroidField,
                "Rocks gathered ahead of Jovara in its orbit. Raider fighters have been seen hiding here.", 5000f, 8000f, new Color(0.85f, 0.55f, 0.45f), null, true).surface = SurfaceKind.Asteroid;

            var outerC = Polar(165f, 262f, 3f);
            var outerBig = MakeField("Outer Drift", sys, outerC, 6000f, 120, 3303, rockMat, layer);
            var fOuter = new GameObject("Outer Drift (warp point)");
            fOuter.transform.SetParent(sys, false);
            fOuter.transform.position = outerC;
            MakeWarpPoint(fOuter, "Outer Drift", WarpPoint.Kind.AsteroidField,
                "A thin belt of rock at the system's edge, where a pirate cruiser is said to lie in wait.", 6000f, 9000f, new Color(0.8f, 0.5f, 0.5f), null, true).surface = SurfaceKind.Asteroid;
            Physics.SyncTransforms();

            // the frigate, in high orbit over Thalassa's day side
            var frigate = (GameObject)PrefabUtility.InstantiatePrefab(frigatePrefab);
            Vector3 start = wThalassa.ArrivalPoint(Vector3.zero);
            Vector3 look = thalassa.transform.position - start;
            look.y = 0f;
            frigate.transform.SetPositionAndRotation(start, Quaternion.LookRotation(look));
            var cc = frigate.GetComponent<CarrierController>();
            cc.target = null;
            var drive = frigate.AddComponent<WarpDrive>();
            drive.streakMaterial = streakMat;
            drive.hazeMaterial = hazeMat;
            drive.flashMaterial = flashMat;
            drive.ringMaterial = ringMat;
            drive.sparkMaterial = sparkMat;

            // hostiles hiding in two of the fields
            var all = new List<GameObject>();
            GameObject Enemy(GameObject prefab, string name, string display, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float fireArc)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = name;
                go.GetComponent<TargetInfo>().displayName = display;
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face - pos));
                var om = go.GetComponent<OrbitMover>();
                if (om) om.enabled = false;
                var w = go.GetComponent<EnemyWeapons>();
                if (w) { w.holdFire = 0f; w.fireArc = fireArc; }
                go.GetComponent<Health>().respawnDelay = 40f;
                var pilot = go.AddComponent<CombatPilot>();
                pilot.leashDistance = 26000f;   // the field, plus the arrival distance, plus room to fight
                pilot.relurkOnRespawn = true;
                setup(pilot);
                all.Add(go);
                return go;
            }
            void FighterPilot(CombatPilot pl)
            {
                pl.style = CombatPilot.Style.Fighter;
                pl.cruiseSpeed = 230f; pl.attackSpeed = 320f; pl.turnRate = 90f; pl.acceleration = 150f;
                pl.breakOffDistance = 420f; pl.extendDistance = 1400f;
                pl.hideBelow = 0.3f; pl.hideTime = 5f; pl.hideSearch = 2500f;
            }
            void DronePilot(CombatPilot pl)
            {
                pl.style = CombatPilot.Style.Fighter;
                pl.cruiseSpeed = 190f; pl.attackSpeed = 250f; pl.turnRate = 110f; pl.acceleration = 130f;
                pl.breakOffDistance = 350f; pl.extendDistance = 1000f;
                pl.hideBelow = 0.35f; pl.hideTime = 5f; pl.hideSearch = 2500f;
            }
            Vector3 Hide(List<Vector3> rocks, int i, Vector3 c) => rocks.Count > 0 ? rocks[i % rocks.Count] + (rocks[i % rocks.Count] - c).normalized * 900f + Vector3.up * (i % 2 == 0 ? 150f : -150f) : c;
            var raiders = new List<CombatPilot>();
            for (int i = 0; i < 4; i++)
                raiders.Add(Enemy(p.enemyFighter, $"Raider {(char)('A' + i)}", $"Raider Fighter {(char)('A' + i)}", Hide(trojanBig, i, trojanC), trojanC, FighterPilot, 18f).GetComponent<CombatPilot>());
            for (int i = 0; i < 2; i++)
                raiders.Add(Enemy(p.enemyDrone, $"Raider Drone {(char)('A' + i)}", $"Raider Drone {(char)('A' + i)}", Hide(trojanBig, i + 4, trojanC), trojanC, DronePilot, 35f).GetComponent<CombatPilot>());
            var pirates = new List<CombatPilot>();
            var cruiser = Enemy(p.enemyCruiser, "Pirate Cruiser", "Pirate Cruiser", Hide(outerBig, 0, outerC), outerC, pl =>
            {
                pl.style = CombatPilot.Style.Capital;
                pl.cruiseSpeed = 75f; pl.attackSpeed = 90f; pl.turnRate = 12f; pl.acceleration = 18f;
                pl.standoff = 1800f; pl.circleSpeed = 3f;
                pl.hideBelow = 0.5f; pl.hideTime = 8f; pl.hideCooldown = 30f; pl.hideSearch = 3000f;
            }, 180f);
            pirates.Add(cruiser.GetComponent<CombatPilot>());
            for (int i = 0; i < 2; i++)
                pirates.Add(Enemy(p.enemyDrone, $"Pirate Drone {(char)('A' + i)}", $"Pirate Drone {(char)('A' + i)}", Hide(outerBig, i + 1, outerC), outerC, DronePilot, 35f).GetComponent<CombatPilot>());
            var g1 = new GameObject("Raiders").AddComponent<EncounterGroup>();
            g1.groupName = "Raiders";
            g1.pilots = raiders.ToArray();
            g1.spotRange = 4500f;
            var g2 = new GameObject("Pirates").AddComponent<EncounterGroup>();
            g2.groupName = "Pirate cruiser";
            g2.pilots = pirates.ToArray();
            g2.spotRange = 5000f;

            // capital ship battle groups, on station in the open between the usual arrival point
            // (coming in from Thalassa) and the planet: the frigate drops out of warp with them
            // ahead of it, a few kilometres off, and they come to meet it
            void Capital(CombatPilot pl, float cruise, float attack, float turn, float accel, float standoff, float circle)
            {
                pl.style = CombatPilot.Style.Capital;
                pl.concealed = false;   // on patrol in plain sight, not hiding in rock
                pl.cruiseSpeed = cruise; pl.attackSpeed = attack; pl.turnRate = turn; pl.acceleration = accel;
                pl.standoff = standoff; pl.circleSpeed = circle;
                pl.hideBelow = 0f;
                pl.leashDistance = 32000f;
            }
            EncounterGroup BattleGroup(string groupName, WarpPoint at, float towardBody, (GameObject prefab, string name, Action<CombatPilot> setup, float respawn)[] ships)
            {
                Vector3 arrive = at.ArrivalPoint(wThalassa.Position);
                Vector3 toBody = (at.Position - arrive).normalized;
                Vector3 station = arrive + toBody * towardBody;
                Vector3 side = Vector3.Cross(Vector3.up, toBody).normalized;
                var pilots = new List<CombatPilot>();
                for (int i = 0; i < ships.Length; i++)
                {
                    // the flagship ahead, escorts either side and a little behind it
                    Vector3 pos = i == 0 ? station : station + side * (i % 2 == 1 ? 1100f : -1100f) + toBody * 500f + Vector3.up * (i % 2 == 1 ? 120f : -120f);
                    var s = ships[i];
                    var go = Enemy(s.prefab, s.name, s.name, pos, pos - toBody * 1000f, s.setup, 180f);
                    go.GetComponent<Health>().respawnDelay = s.respawn;
                    pilots.Add(go.GetComponent<CombatPilot>());
                }
                var g = new GameObject(groupName).AddComponent<EncounterGroup>();
                g.groupName = groupName;
                g.pilots = pilots.ToArray();
                g.spotRange = 9000f;
                Log($"{groupName}: {ships.Length} ships on station {Vector3.Distance(station, at.Position) / 1000f:F1} km from {at.displayName}, " +
                    $"{towardBody / 1000f:F1} km ahead of the arrival point");
                return g;
            }
            BattleGroup("Corsair patrol", wCinder, 3000f, new (GameObject, string, Action<CombatPilot>, float)[]
            {
                (p.enemyCruiser, "Corsair Cruiser", pl => Capital(pl, 75f, 90f, 12f, 18f, 1900f, 3f), 75f),
                (p.enemyDestroyer, "Corsair Destroyer A", pl => Capital(pl, 110f, 140f, 18f, 25f, 2300f, 4f), 60f),
                (p.enemyDestroyer, "Corsair Destroyer B", pl => Capital(pl, 110f, 140f, 18f, 25f, 2300f, 4f), 60f),
            });
            BattleGroup("Dreadnought battle group", wGlacis, 6000f, new (GameObject, string, Action<CombatPilot>, float)[]
            {
                (p.enemyDreadnought, "Pirate Dreadnought", pl => Capital(pl, 45f, 55f, 6f, 8f, 3300f, 1.5f), 120f),
                (p.enemyDestroyer, "Escort Destroyer A", pl => Capital(pl, 110f, 140f, 18f, 25f, 2300f, 4f), 60f),
                (p.enemyDestroyer, "Escort Destroyer B", pl => Capital(pl, 110f, 140f, 18f, 25f, 2300f, 4f), 60f),
            });

            // camera, controls and the system map
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 1200000f;
            cam.nearClipPlane = 1f;
            cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.target = frigate.transform;
            orbit.maxDistance = 6000f;
            camGo.transform.position = start + new Vector3(-600f, 200f, -300f);
            camGo.transform.LookAt(start);
            camGo.AddComponent<AudioListener>();

            var demoGo = new GameObject("Demo");
            var demo = demoGo.AddComponent<CarrierDemoInput>();
            demo.carrier = cc;
            demo.orbitCamera = orbit;
            demo.targetOnStart = false;
            demo.targets = new[] { cruiser.transform }.Concat(all.Where(g => g != cruiser).Select(g => g.transform)).ToArray();
            var map = demoGo.AddComponent<SystemMap>();
            map.carrier = cc;
            map.drive = drive;
            map.systemName = "Kestrel System";
            var landing = demoGo.AddComponent<PlanetLanding>();
            landing.carrier = cc;
            landing.drive = drive;
            landing.fill = fill;
            landing.kit = SurfaceAssets(rockMat);

            foreach (var w in sys.GetComponentsInChildren<WarpPoint>())
                Log($"warp point {w.displayName,-16} {w.kind,-13} {w.transform.position.magnitude / 1000f,6:F0} km from the star");

            StarSystemShots(sys, star.transform, thalassa.transform, jovara.transform, wThalassa, start);

            foreach (var pa in sys.GetComponentsInChildren<ProceduralAsteroid>(true)) pa.Clear();
            foreach (var ps in sys.GetComponentsInChildren<ProceduralSphere>(true)) ps.Clear();
            ProceduralAsteroid.ClearCache();
            ProceduralSphere.ClearCache();
            EditorSceneManager.SaveScene(scene, StarSystemScenePath);
            Log("scene saved " + StarSystemScenePath);
        }

        /// <summary>Review renders (Temp/carrier_shots/sys_*.png).</summary>
        static void StarSystemShots(Transform sys, Transform star, Transform thalassa, Transform jovara, WarpPoint wThalassa, Vector3 start)
        {
            Directory.CreateDirectory(ShotDir);
            var sl = star.GetComponent<StarLight>();
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 1200000f;
            cam.nearClipPlane = 1f;
            cam.fieldOfView = 50f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Shader.SetGlobalVector("_CarrierStarPos", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalColor("_CarrierStarColor", new Color(1f, 0.93f, 0.82f) * 1.05f);
            void Shot(string name, Vector3 pos, Vector3 lookAt)
            {
                go.transform.position = pos;
                go.transform.LookAt(lookAt);
                if (sl.sun) sl.sun.transform.rotation = Quaternion.LookRotation(pos - star.position);
                if (sl.corona) sl.corona.rotation = Quaternion.LookRotation(sl.corona.position - pos, Vector3.up);
                Render(cam, name);
            }
            Shot("sys_start", start + new Vector3(-400f, 150f, -500f), thalassa.position);
            Shot("sys_thalassa_moons", thalassa.position + new Vector3(-30000f, 9000f, -38000f), thalassa.position + new Vector3(5000f, 0f, 5000f));
            Shot("sys_jovara", jovara.position + new Vector3(-90000f, 25000f, -70000f), jovara.position);
            Shot("sys_star", star.position + new Vector3(55000f, 8000f, 30000f), star.position);
            foreach (var n in new[] { "Cinder", "Ochre", "Glacis" })
            {
                var t = sys.Find(n);
                var wp = t.GetComponent<WarpPoint>();
                Vector3 from = wp.ArrivalPoint(Vector3.zero);
                Shot($"sys_{n.ToLower()}", from + Vector3.up * 400f, t.position);
            }
            var tro = sys.Find("Jovara Trojans (warp point)");
            Shot("sys_trojans", tro.position + new Vector3(-7000f, 1500f, -5000f), tro.position);
            Object.DestroyImmediate(go);
            // the other scenes' atmospheres light from the main light unless this is set: clear it
            Shader.SetGlobalVector("_CarrierStarPos", Vector4.zero);
            Log("star system screenshots in " + ShotDir);
        }
    }
}
