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
    /// The second level, "Asteroid Ambush": a large asteroid the frigate crosses and rounds, a
    /// field of smaller rocks, the drifting wreck of an earlier frigate with a cloud of debris, a
    /// gas giant in the distance, and an ambush: fighters hidden under the big asteroid, and the
    /// heavy cruiser and its drones behind a boulder further on. Every rock is generated from a
    /// seed when the level loads (ProceduralAsteroid), so only the textures are stored.
    /// Asteroids and large wreckage sit on the Obstacle layer: they block shots and line of sight,
    /// so ships can hide behind them.
    /// </summary>
    public static partial class CarrierBuilder
    {
        public const string AsteroidScenePath = Root + "/Scenes/AsteroidAmbush.unity";
        const string EnvDir = Root + "/Generated/Environment";

        /// <summary>Half-size of the big asteroid; it lies along X.</summary>
        static readonly Vector3 BigAxes = new Vector3(1500f, 560f, 950f);
        const float RouteClearance = 240f;

        /// <summary>0 below e0, 1 above e1, smooth in between (GLSL's smoothstep; Unity's
        /// Mathf.SmoothStep is an interpolation between two values instead).</summary>
        static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static int ObstacleLayer()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == Cover.LayerName) return i;
            for (int i = 8; i < layers.arraySize; i++)
            {
                var l = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(l.stringValue)) continue;
                l.stringValue = Cover.LayerName;
                tm.ApplyModifiedProperties();
                Log($"added layer {Cover.LayerName} = {i}");
                return i;
            }
            throw new Exception("no free layer for obstacles");
        }

        // ------------------------------------------------------------------ textures

        static Texture2D SaveTexture(string name, Color[] px, int w, int h, bool normal, bool clampV)
        {
            Directory.CreateDirectory(EnvDir);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, normal);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{EnvDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = clampV ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A tileable rock surface: mottled grey-brown stone, fine grit, dark cracks.</summary>
        static (Texture2D albedo, Texture2D normal) RockTextures()
        {
            const int N = 1024;
            var h = new float[N * N];
            var col = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float body = ProcNoise.TileFbm(u, v, 6, 6, 11);
                    float grit = ProcNoise.TileFbm(u, v, 64, 3, 37);
                    float crack = Mathf.Pow(1f - Mathf.Abs(ProcNoise.TileFbm(u, v, 5, 4, 23)), 8f);
                    float hh = 0.55f * body + 0.2f * grit - 0.5f * crack;
                    h[y * N + x] = hh;
                    float k = Mathf.Clamp01(body * 0.5f + 0.5f);
                    var c = Color.Lerp(new Color(0.20f, 0.18f, 0.16f), new Color(0.47f, 0.44f, 0.40f), k);
                    c = Color.Lerp(c, new Color(0.36f, 0.30f, 0.24f), Mathf.Clamp01(ProcNoise.TileFbm(u, v, 3, 3, 51) * 0.7f + 0.2f));
                    c *= 0.85f + 0.25f * grit;
                    c *= 1f - 0.65f * crack;
                    col[y * N + x] = new Color(c.r, c.g, c.b);
                }
            var nrm = new Color[N * N];
            const float strength = 6f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                    float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    nrm[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
                }
            return (SaveTexture("T_Rock_BaseColor", col, N, N, false, false), SaveTexture("T_Rock_Normal", nrm, N, N, true, false));
        }

        /// <summary>A banded gas giant (equirectangular), with a great storm.</summary>
        static Texture2D PlanetTexture()
        {
            const int W = 2048, H = 1024;
            var px = new Color[W * H];
            var palette = new[]
            {
                new Color(0.93f, 0.87f, 0.74f), new Color(0.82f, 0.66f, 0.47f), new Color(0.70f, 0.45f, 0.29f),
                new Color(0.48f, 0.32f, 0.22f), new Color(0.76f, 0.73f, 0.70f), new Color(0.88f, 0.78f, 0.60f),
            };
            for (int y = 0; y < H; y++)
            {
                float lat = (y + 0.5f) / H;
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W;
                    float warp = ProcNoise.TileFbm(u, lat, 8, 5, 77);
                    float band = Mathf.Sin(lat * Mathf.PI * 15f + warp * 2.6f) * 0.6f + Mathf.Sin(lat * Mathf.PI * 37f + warp * 4f) * 0.4f;
                    float t = Mathf.Clamp01(band * 0.5f + 0.5f) * (palette.Length - 1);
                    int i = Mathf.Min(palette.Length - 2, (int)t);
                    var c = Color.Lerp(palette[i], palette[i + 1], t - i);
                    // a great storm, an oval swirl south of the equator
                    float sx = Mathf.DeltaAngle(u * 360f, 110f) / 360f / 0.045f, sy = (lat - 0.62f) / 0.022f;
                    float sd = sx * sx + sy * sy;
                    if (sd < 1.6f)
                    {
                        float swirl = Mathf.Sin(Mathf.Sqrt(sd) * 9f + Mathf.Atan2(sy, sx) * 2f) * 0.5f + 0.5f;
                        c = Color.Lerp(c, Color.Lerp(new Color(0.78f, 0.36f, 0.22f), new Color(0.95f, 0.70f, 0.52f), swirl), Mathf.Clamp01(1.6f - sd));
                    }
                    float pole = Mathf.Abs(lat - 0.5f) * 2f;
                    c = Color.Lerp(c, new Color(0.55f, 0.53f, 0.52f), Edge(0.75f, 1f, pole) * 0.6f);
                    px[y * W + x] = c;
                }
            }
            return SaveTexture("T_GasGiant", px, W, H, false, true);
        }

        // ------------------------------------------------------------------ pieces

        static GameObject MakeAsteroid(string name, Transform parent, Vector3 pos, Quaternion rot, float scale,
            int seed, int detail, int colliderDetail, Vector3 axes, float rough, int craters, float uvTile,
            Material mat, int layer, float radius, float core)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            go.layer = layer;
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            go.AddComponent<MeshCollider>();
            var pa = go.AddComponent<ProceduralAsteroid>();
            pa.seed = seed;
            pa.detail = detail;
            pa.colliderDetail = colliderDetail;
            pa.semiAxes = axes;
            pa.roughness = rough;
            pa.craters = craters;
            pa.uvTile = uvTile;
            pa.Build();
            var ob = go.AddComponent<Obstacle>();
            ob.radius = radius * scale;
            ob.coreRadius = core * scale;
            ob.cover = radius * scale > 60f;
            // every rock has minerals in it (an asteroid unless the caller says otherwise)
            go.AddComponent<MineralDeposit>().seed = MineralDeposit.PositionSeed(pos);
            return go;
        }

        static float DistanceToRoute(Vector3 p, IList<Vector3> route)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector3 a = route[i], ab = route[i + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-3f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector3.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>The way across the top of the big asteroid, round its east end and back along
        /// its underside: low over the surface, always clear of it.</summary>
        static List<Vector3> Route(int mask, out int rounding)
        {
            var r = new List<Vector3>();
            foreach (float x in new[] { -2050f, -1450f, -850f, -250f, 350f, 950f, 1400f })
            {
                float top = float.MinValue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (Physics.Raycast(new Vector3(x + dx * 170f, 5000f, dz * 170f), Vector3.down, out var hit, 10000f, mask))
                            top = Mathf.Max(top, hit.point.y);
                if (top == float.MinValue) top = r.Count > 0 ? r[r.Count - 1].y - RouteClearance : 260f;
                r.Add(new Vector3(x, top + RouteClearance, 0f));
            }
            var A = BigAxes;
            r.Add(new Vector3(A.x + 450f, r[r.Count - 1].y - 120f, 60f));
            rounding = r.Count;
            r.Add(new Vector3(A.x + 680f, -260f, 420f));       // round the end, dropping below it
            r.Add(new Vector3(A.x + 450f, -820f, 1080f));
            r.Add(new Vector3(A.x - 150f, -1020f, 1650f));
            r.Add(new Vector3(A.x - 900f, -1060f, 2150f));
            r.Add(new Vector3(A.x - 1800f, -1000f, 2550f));
            r.Add(new Vector3(A.x - 2900f, -950f, 2800f));
            r.Add(new Vector3(A.x - 4300f, -900f, 2950f));
            // keep every point clear of the rock: over the top push up, elsewhere push outward
            for (int i = 0; i < r.Count; i++)
            {
                Vector3 push = i < rounding ? Vector3.up : r[i].normalized;
                int guard = 0;
                while (Physics.CheckSphere(r[i], RouteClearance, mask) && guard++ < 60) r[i] += push * 30f;
                if (guard > 0) Log($"route point {i} pushed {guard * 30f} m clear of the rock");
            }
            for (int i = 0; i + 1 < r.Count; i++)
            {
                Vector3 d = r[i + 1] - r[i];
                if (Physics.SphereCast(r[i], 120f, d.normalized, out var hit, d.magnitude, mask))
                    Log($"WARNING route leg {i}->{i + 1} passes within 120 m of rock at {hit.point:F0}");
            }
            Log("route: " + string.Join(" ", r.Select(p => p.ToString("F0"))));
            return r;
        }

        // ------------------------------------------------------------------ the level

        static void BuildAsteroidScene(Prefabs p, GameObject frigatePrefab)
        {
            int layer = ObstacleLayer();
            int mask = 1 << layer;
            var (rockAlbedo, rockNormal) = RockTextures();
            var planetTex = PlanetTexture();
            var rockMat = Lit("Asteroid_Rock", new Color(0.9f, 0.86f, 0.82f), rockAlbedo, rockNormal, 0f, 0.12f);
            rockMat.enableInstancing = true;
            var frigateTex = Load<Texture2D>(Root + "/Models/Frigate_BaseColor.png");
            var wreckMat = Lit("Wreck_Burnt", new Color(0.34f, 0.30f, 0.28f), frigateTex, null, 0.3f, 0.2f, twoSided: true);
            var debrisMat = Lit("Debris_Metal", new Color(0.42f, 0.42f, 0.45f), null, null, 0.75f, 0.35f);
            var planetMat = Lit("Planet_GasGiant", Color.white, planetTex, null, 0f, 0.2f);
            var atmoMat = SaveMat("Planet_Atmosphere", new Material(Shader.Find("Carrier/PlanetAtmosphere")));
            atmoMat.SetColor("_Color", new Color(0.95f, 0.75f, 0.55f) * 1.2f);
            atmoMat.SetFloat("_RimPower", 5f);
            atmoMat.SetFloat("_Strength", 1.6f);
            atmoMat.SetFloat("_NightLevel", 0f);
            EditorUtility.SetDirty(atmoMat);
            ProceduralAsteroid.ClearCache();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunTravel = new Vector3(0.30f, -0.45f, 0.84f).normalized;
            SpaceLighting(Quaternion.LookRotation(sunTravel), 2.3f, new Color(1f, 0.95f, 0.88f), new Color(0.06f, 0.06f, 0.08f));
            // light reflected off the gas giant, and a faint fill so the undersides aren't black
            var shine = new GameObject("Planetshine").AddComponent<Light>();
            shine.type = LightType.Directional;
            shine.color = new Color(1f, 0.72f, 0.5f);
            shine.intensity = 0.45f;
            shine.shadows = LightShadows.None;
            shine.transform.rotation = Quaternion.LookRotation(-new Vector3(62000f, 9000f, -30000f).normalized);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.65f, 0.9f);
            fill.intensity = 0.28f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.2f, 1f, -0.3f));
            var env = new GameObject("Environment").transform;

            // the big asteroid the frigate crosses
            MakeAsteroid("Big Asteroid", env, Vector3.zero, Quaternion.identity, 1f, 4242, 6, 5, BigAxes, 0.17f, 28, 70f,
                rockMat, layer, BigAxes.x * 1.2f, BigAxes.y * 0.75f);
            Physics.SyncTransforms();
            var route = Route(mask, out int rounding);

            // fighters wait in the shadow under its east end, out of sight of the way across the top
            var fighterLurks = new List<Vector3>();
            foreach (var (x, z) in new[] { (700f, 380f), (980f, 130f), (1180f, 470f), (860f, 680f) })
            {
                Vector3 lp = Physics.Raycast(new Vector3(x, -5000f, z), Vector3.up, out var hit, 10000f, mask)
                    ? hit.point + Vector3.down * 150f : new Vector3(x, -750f, z);
                fighterLurks.Add(lp);
                int seen = 0;
                for (int i = 0; i < rounding; i++) if (!Physics.Linecast(route[i], lp, mask)) seen++;
                bool spotted = false;
                for (int i = rounding; i < rounding + 2; i++) if (!Physics.Linecast(route[i], lp, mask)) spotted = true;
                Log($"fighter lurk {lp:F0}: in view of {seen} of {rounding} points on the way across (want 0), visible from the rounding: {spotted}");
            }

            // a boulder further on, with the cruiser and its drones behind it
            var A = BigAxes;
            var boulderPos = new Vector3(A.x + 1800f, -1500f, 2700f);
            MakeAsteroid("Boulder", env, boulderPos, Quaternion.Euler(20f, 35f, 10f), 1f, 777, 5, 4, new Vector3(560f, 430f, 500f), 0.22f, 12, 45f,
                rockMat, layer, 560f * 1.25f, 430f * 0.75f);
            Physics.SyncTransforms();
            Vector3 hideDir = (boulderPos - route[rounding + 2]).normalized;
            var cruiserLurk = boulderPos + hideDir * (560f + 480f);
            Vector3 lateral = Vector3.Cross(hideDir, Vector3.up).normalized;
            var droneLurks = new[] { cruiserLurk + hideDir * 140f + lateral * 170f + Vector3.up * 60f,
                                     cruiserLurk + hideDir * 140f - lateral * 170f - Vector3.up * 40f };
            foreach (var lp in new[] { cruiserLurk }.Concat(droneLurks))
            {
                int seen = 0;
                for (int i = rounding; i < route.Count; i++) if (!Physics.Linecast(route[i], lp, mask)) seen++;
                Log($"second-wave lurk {lp:F0}: in view of {seen} of {route.Count - rounding} points after the rounding");
            }

            // the wreck of an earlier, bigger ship of the frigate's class: the hull sections put back
            // where they belong, broken into bow, midships and stern, which have drifted apart
            var wreckCenter = new Vector3(A.x + 700f, -1450f, 2850f);
            var wreck = new GameObject("Hulk Wreck").transform;
            wreck.SetParent(env, false);
            wreck.SetPositionAndRotation(wreckCenter, Quaternion.Euler(18f, -60f, 28f));
            var rng = new System.Random(1999);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 RandDir() { Vector3 v; do v = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)); while (v.sqrMagnitude > 1f || v.sqrMagnitude < 0.01f); return v.normalized; }
            const float hulkScale = 2.2f;
            var groups = new[]
            {
                (name: "Bow", minZ: 30f, maxZ: 999f, offset: new Vector3(10f, 35f, 120f), rot: new Vector3(10f, 18f, 30f)),
                (name: "Midships", minZ: -30f, maxZ: 30f, offset: Vector3.zero, rot: Vector3.zero),
                (name: "Stern", minZ: -999f, maxZ: -30f, offset: new Vector3(-20f, -40f, -130f), rot: new Vector3(-14f, -22f, -40f)),
            };
            var groupTf = groups.Select(g =>
            {
                var t = new GameObject(g.name).transform;
                t.SetParent(wreck, false);
                t.localPosition = g.offset * hulkScale;
                t.localRotation = Quaternion.Euler(g.rot);
                t.localScale = Vector3.one * hulkScale;
                t.gameObject.layer = layer;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                t.gameObject.AddComponent<Drift>().spin = RandDir() * R(0.15f, 0.5f);
                return t;
            }).ToArray();
            var breakup = frigatePrefab.GetComponent<ShipBreakup>();
            int wreckPieces = 0;
            foreach (var chunk in breakup != null ? breakup.chunks : new Transform[0])
            {
                var mf = chunk != null ? chunk.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || !chunk.name.StartsWith("Chunk")) continue;
                Vector3 pivot = frigatePrefab.transform.InverseTransformPoint(chunk.position);
                int gi = groups.Select((g, i) => (g, i)).First(x => pivot.z >= x.g.minZ && pivot.z < x.g.maxZ).i;
                bool loose = rng.NextDouble() < 0.25;   // a few sections have broken right away
                var piece = new GameObject($"Wreck {chunk.name}");
                piece.transform.SetParent(groupTf[gi], false);
                piece.transform.localPosition = pivot + (loose ? RandDir() * R(25f, 70f) : Vector3.zero);
                piece.transform.localRotation = loose ? Quaternion.Euler(R(-40, 40), R(-40, 40), R(-40, 40)) : Quaternion.identity;
                piece.layer = layer;
                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = piece.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Enumerable.Repeat(wreckMat, Mathf.Max(1, mf.sharedMesh.subMeshCount)).ToArray();
                piece.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                wreckPieces++;
            }
            Physics.SyncTransforms();
            foreach (var t in groupTf)
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var ob = t.gameObject.AddComponent<Obstacle>();
                ob.centerOffset = t.InverseTransformPoint(b.center);
                ob.radius = b.extents.magnitude;
                ob.coreRadius = Mathf.Min(b.extents.x, Mathf.Min(b.extents.y, b.extents.z)) * 0.4f;
                ob.cover = true;
            }

            // small debris: plates, girders and containers, tumbling (they stop shots but are no cover)
            var debris = new GameObject("Debris").transform;
            debris.SetParent(env, false);
            int debrisCount = 0;
            for (int i = 0; i < 170; i++)
            {
                bool nearWreck = i < 120;
                Vector3 pos = nearWreck ? wreckCenter + RandDir() * R(120f, 750f)
                                        : route[rounding + (i % 3)] + RandDir() * R(320f, 900f);
                float size = R(3f, 26f);
                if (DistanceToRoute(pos, route) < size + 140f) continue;
                if (Physics.CheckSphere(pos, size, mask)) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Debris";
                go.transform.SetParent(debris, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)));
                int shape = i % 4;
                go.transform.localScale = shape == 0 ? new Vector3(size, size * 0.06f, size * 0.7f)       // hull plate
                                        : shape == 1 ? new Vector3(size * 0.08f, size * 0.08f, size)         // girder
                                        : shape == 2 ? new Vector3(size * 0.4f, size * 0.4f, size * 0.9f)    // container
                                        : new Vector3(size * 0.5f, size * 0.12f, size * 0.35f);              // panel
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = shape == 2 && i % 3 == 0 ? wreckMat : debrisMat;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var dr = go.AddComponent<Drift>();
                dr.spin = RandDir() * R(2f, 16f);
                dr.velocity = RandDir() * R(0.2f, 1.5f);
                debrisCount++;
            }

            // the asteroid field round it all
            var field = new GameObject("Asteroid Field").transform;
            field.SetParent(env, false);
            var placed = new List<(Vector3 p, float r)> { (boulderPos, 600f), (wreckCenter, 600f) };
            var avoid = fighterLurks.Concat(droneLurks).Append(cruiserLurk).ToList();
            var variantAxes = new List<Vector3>();
            for (int v = 0; v < 8; v++) variantAxes.Add(new Vector3(1f, R(0.55f, 0.95f), R(0.7f, 1f)));
            int rocks = 0;
            for (int attempt = 0; attempt < 8000 && rocks < 230; attempt++)
            {
                bool nearRoute = rng.NextDouble() < 0.55;
                Vector3 pos;
                if (nearRoute)
                {
                    int leg = rng.Next(route.Count - 1);
                    pos = Vector3.Lerp(route[leg], route[leg + 1], R(0f, 1f)) + RandDir() * R(450f, 1700f);
                }
                else pos = new Vector3(R(-5200f, 6500f), R(-3300f, 2300f), R(-3500f, 7200f));
                float rr = 22f + 160f * Mathf.Pow(R(0f, 1f), 2.4f);
                if (rng.NextDouble() < 0.07) rr = R(220f, 420f);
                // keep out of the big asteroid, the route, the lurking places and each other
                var e = new Vector3(pos.x / (A.x + rr + 260f), pos.y / (A.y + rr + 260f), pos.z / (A.z + rr + 260f));
                if (e.sqrMagnitude < 1f) continue;
                if (DistanceToRoute(pos, route) < rr * 1.3f + 260f) continue;
                if (avoid.Any(a => Vector3.Distance(a, pos) < rr * 1.3f + 220f)) continue;
                if (placed.Any(q => Vector3.Distance(q.p, pos) < (q.r + rr) * 1.15f)) continue;
                placed.Add((pos, rr));
                int variant = rr > 150f ? rng.Next(3) : 3 + rng.Next(5);
                MakeAsteroid("Rock", field, pos, Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), rr,
                    100 + variant, rr > 150f ? 4 : 3, 2, variantAxes[variant], 0.28f, 6, 0.45f,
                    rockMat, layer, 1.3f, 0.5f);
                rocks++;
            }
            Log($"asteroid field: {rocks} rocks, {wreckPieces} wreck sections, {debrisCount} pieces of debris");

            // a gas giant far off, ahead and to the right of the way across
            var planet = new GameObject("Gas Giant", typeof(MeshFilter), typeof(MeshRenderer));
            planet.AddComponent<ProceduralSphere>().Build();
            planet.transform.SetParent(env, false);
            planet.transform.SetPositionAndRotation(new Vector3(62000f, 9000f, -30000f), Quaternion.Euler(14f, 40f, 9f));
            planet.transform.localScale = Vector3.one * 34000f;
            var pr = planet.GetComponent<MeshRenderer>();
            pr.sharedMaterial = planetMat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            var atmo = new GameObject("Atmosphere", typeof(MeshFilter), typeof(MeshRenderer));
            atmo.AddComponent<ProceduralSphere>().Build();
            atmo.transform.SetParent(planet.transform, false);
            atmo.transform.localScale = Vector3.one * 1.035f;
            var ar = atmo.GetComponent<MeshRenderer>();
            ar.sharedMaterial = atmoMat;
            ar.shadowCastingMode = ShadowCastingMode.Off;
            ar.receiveShadows = false;

            // the frigate at the start of the way across
            var frigate = (GameObject)PrefabUtility.InstantiatePrefab(frigatePrefab);
            Vector3 fwd = route[1] - route[0];
            fwd.y = 0f;
            frigate.transform.SetPositionAndRotation(route[0], Quaternion.LookRotation(fwd));
            var cc = frigate.GetComponent<CarrierController>();
            cc.target = null;

            // the ambushers
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
                var pilot = go.AddComponent<CombatPilot>();
                setup(pilot);
                return go;
            }
            var faceAmbush = route[rounding + 1];
            var fighters = fighterLurks.Select((lp, i) => Enemy(p.enemyFighter, $"EnemyFighter {(char)('A' + i)}", $"Hostile Fighter {(char)('A' + i)}", lp, faceAmbush, pl =>
            {
                pl.style = CombatPilot.Style.Fighter;
                pl.cruiseSpeed = 230f; pl.attackSpeed = 320f; pl.turnRate = 90f; pl.acceleration = 150f;
                pl.breakOffDistance = 420f; pl.extendDistance = 1400f;
                pl.hideBelow = 0.3f; pl.hideTime = 5f; pl.hideSearch = 2000f;
            }, 18f)).ToArray();
            var cruiser = Enemy(p.enemyCruiser, "EnemyCruiser", "Hostile Cruiser", cruiserLurk, route[rounding + 3], pl =>
            {
                pl.style = CombatPilot.Style.Capital;
                pl.cruiseSpeed = 75f; pl.attackSpeed = 90f; pl.turnRate = 12f; pl.acceleration = 18f;
                pl.standoff = 1800f; pl.circleSpeed = 3f;
                pl.hideBelow = 0.5f; pl.hideTime = 8f; pl.hideCooldown = 30f; pl.hideSearch = 3000f;
            }, 180f);
            var drones = droneLurks.Select((lp, i) => Enemy(p.enemyDrone, $"EnemyDrone {(char)('A' + i)}", $"Hostile Drone {(char)('A' + i)}", lp, route[rounding + 3], pl =>
            {
                pl.style = CombatPilot.Style.Fighter;
                pl.cruiseSpeed = 190f; pl.attackSpeed = 250f; pl.turnRate = 110f; pl.acceleration = 130f;
                pl.breakOffDistance = 350f; pl.extendDistance = 1000f;
                pl.hideBelow = 0.35f; pl.hideTime = 5f; pl.hideSearch = 2000f;
            }, 35f)).ToArray();

            // camera, controls and the ambush itself
            var orbit = MainCamera(frigate.transform, 1f, 140000f, route[0] + new Vector3(-600f, 200f, -300f), route[0]);

            var demo = new GameObject("Demo").AddComponent<CarrierDemoInput>();
            demo.carrier = cc;
            demo.orbitCamera = orbit;
            demo.targetOnStart = false;
            demo.targets = new[] { cruiser.transform }.Concat(fighters.Select(f => f.transform)).Concat(drones.Select(d => d.transform)).ToArray();

            var director = new GameObject("Ambush").AddComponent<AmbushDirector>();
            director.player = cc;
            director.firstWave = fighters.Select(f => f.GetComponent<CombatPilot>()).ToArray();
            director.secondWave = new[] { cruiser }.Concat(drones).Select(g => g.GetComponent<CombatPilot>()).ToArray();
            director.triggerPoint = route[rounding + 1];
            director.triggerRadius = 450f;
            director.route = route.ToArray();
            director.roundingIndex = rounding;

            AsteroidShots(route, rounding, fighterLurks, cruiserLurk, wreckCenter, planet.transform.position);

            // the rocks are rebuilt from their seeds when the level loads: don't store the meshes
            foreach (var pa in env.GetComponentsInChildren<ProceduralAsteroid>(true))
                pa.Clear();
            foreach (var ps in env.GetComponentsInChildren<ProceduralSphere>(true))
                ps.Clear();
            ProceduralAsteroid.ClearCache();
            ProceduralSphere.ClearCache();

            EditorSceneManager.SaveScene(scene, AsteroidScenePath);
            Log("scene saved " + AsteroidScenePath);
        }

        /// <summary>The lighting both levels share: the space sky, trilight ambient, a sun and the
        /// post-processing volume (the sky material and volume profile are made by BuildScene).</summary>
        static void SpaceLighting(Quaternion sunRotation, float sunIntensity, Color sunColor, Color ambientGround)
        {
            RenderSettings.skybox = Load<Material>(MatDir + "/Space_Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.22f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.11f, 0.14f);
            RenderSettings.ambientGroundColor = ambientGround;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = sunRotation;
            RenderSettings.sun = sun;
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = Load<VolumeProfile>(Root + "/Settings/CarrierVolume.asset");
        }

        /// <summary>Review renders of the level (Temp/carrier_shots/ast_*.png).</summary>
        static void AsteroidShots(List<Vector3> route, int rounding, List<Vector3> fighterLurks, Vector3 cruiserLurk, Vector3 wreck, Vector3 planet)
        {
            Directory.CreateDirectory(ShotDir);
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 140000f;
            cam.nearClipPlane = 1f;
            cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            Vector3 lurk = fighterLurks.Aggregate(Vector3.zero, (a, b) => a + b) / fighterLurks.Count;
            var shots = new List<(string, Vector3, Vector3)>
            {
                ("ast_overview", new Vector3(-4200f, 2600f, -5600f), new Vector3(600f, -400f, 900f)),
                ("ast_start", route[0] + new Vector3(-520f, 110f, -260f), route[2]),
                ("ast_surface", route[3] + new Vector3(-320f, 30f, 160f), route[5]),
                ("ast_rounding", route[rounding] + new Vector3(600f, 250f, -700f), route[rounding + 1]),
                ("ast_ambush", route[rounding + 1] + new Vector3(250f, 120f, -150f), lurk),
                ("ast_cruiser", route[rounding + 3] + Vector3.up * 150f, cruiserLurk),
                ("ast_wreck", wreck + new Vector3(-900f, 300f, -700f), wreck),
                ("ast_planet", route[2] + Vector3.up * 50f, planet),
            };
            foreach (var (name, pos, look) in shots)
            {
                go.transform.position = pos;
                go.transform.LookAt(look);
                Render(cam, name);
            }
            Object.DestroyImmediate(go);
            Log("asteroid screenshots in " + ShotDir);
        }
    }
}
