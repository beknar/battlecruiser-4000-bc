using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// What the star system's stations, installations and bases are built from: plain shapes
    /// (boxes, cylinders, spheres, rings, dishes, trusses, solar wings) in station materials,
    /// put together in code and then baked into one mesh per part (one draw call for each of
    /// its materials, with a collider that blocks ships and shots). The texture is laid on by
    /// position, so panels are the same size on a small hut and on a 1.4 km ring.
    /// Also the buildings of the planets' bases (colonies and fortresses), saved as prefabs for
    /// the surfaces to put up round their landing pads.
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string StructDir = Root + "/Generated/Structures";
        const string BaseDir = PrefabDir + "/Bases";

        /// <summary>The station materials (made by StructureMaterials).</summary>
        class StructMats
        {
            public Material hull, dark, white, solar, window, red, green, work, gold, mirror, glass, pirate, hazard;
        }
        static StructMats sm;
        /// <summary>Texture size on a baked part, per material (m per repeat).</summary>
        static readonly Dictionary<Material, float> UvTile = new Dictionary<Material, float>();

        static void StructureMaterials()
        {
            var panelD = KitTexture("T_Metal_pannel_D.png");
            var panelN = KitTexture("T_Metal_pannel_N.png");
            sm = new StructMats
            {
                hull = Lit("Station_Hull", new Color(0.74f, 0.76f, 0.79f), panelD, panelN, 0.5f, 0.42f),
                dark = Lit("Station_Dark", new Color(0.24f, 0.26f, 0.29f), panelD, panelN, 0.55f, 0.4f),
                white = Lit("Station_Radiator", new Color(0.88f, 0.89f, 0.9f), metallic: 0.1f, smooth: 0.3f),
                solar = Lit("Station_Solar", Color.white, SolarTexture(), null, 0.7f, 0.85f),
                window = Lit("Station_Windows", Color.white, WindowTexture(), null, 0.2f, 0.6f, new Color(1f, 0.82f, 0.55f) * 2.2f, WindowTexture()),
                red = Lit("Station_RedLight", new Color(1f, 0.2f, 0.15f), metallic: 0f, smooth: 0.3f, emission: new Color(1f, 0.15f, 0.1f) * 14f),
                green = Lit("Station_GreenLight", new Color(0.2f, 1f, 0.3f), metallic: 0f, smooth: 0.3f, emission: new Color(0.15f, 1f, 0.25f) * 12f),
                work = Lit("Station_WorkLight", new Color(1f, 0.9f, 0.7f), metallic: 0f, smooth: 0.3f, emission: new Color(1f, 0.85f, 0.6f) * 9f),
                gold = Lit("Station_Gold", new Color(0.86f, 0.62f, 0.24f), metallic: 0.95f, smooth: 0.72f),
                mirror = Lit("Station_Mirror", new Color(0.86f, 0.88f, 0.92f), metallic: 1f, smooth: 0.93f),
                glass = Lit("Station_Glass", new Color(0.05f, 0.11f, 0.13f), metallic: 0.3f, smooth: 0.95f, emission: new Color(0.2f, 0.5f, 0.45f) * 0.5f),
                pirate = Lit("Station_Pirate", new Color(0.32f, 0.13f, 0.12f), panelD, panelN, 0.5f, 0.42f),
                hazard = Lit("Station_Hazard", new Color(0.86f, 0.62f, 0.1f), metallic: 0.2f, smooth: 0.4f),
            };
            UvTile.Clear();
            UvTile[sm.hull] = 28f; UvTile[sm.dark] = 28f; UvTile[sm.pirate] = 28f;
            UvTile[sm.solar] = 9f; UvTile[sm.window] = 7f; UvTile[sm.white] = 20f;
            primCache.Clear();
            torusCache.Clear();
            dishCache.Clear();
            // (no Refresh here: it would reload the prefabs already made, and the builder holds them)
            if (!AssetDatabase.IsValidFolder(StructDir)) AssetDatabase.CreateFolder(Root + "/Generated", "Structures");
        }

        /// <summary>Solar cells: dark blue squares in a light grid.</summary>
        static Texture2D SolarTexture()
        {
            const int N = 64;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool line = x % 16 < 1 || y % 16 < 1;
                    float n = ProcNoise.Hash(x / 16, y / 16, 0, 77) * 0.06f;
                    px[y * N + x] = line ? new Color(0.62f, 0.64f, 0.68f) : new Color(0.07f + n, 0.1f + n, 0.24f + n * 2f);
                }
            return SaveTexture("T_SolarCells", px, N, N, false, false);
        }

        /// <summary>A lit window in a dark panel (its own emission map too).</summary>
        static Texture2D WindowTexture()
        {
            const int N = 32;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool pane = x >= 5 && x < 27 && y >= 11 && y < 22;
                    px[y * N + x] = pane ? new Color(0.95f, 0.85f, 0.65f) : new Color(0.05f, 0.05f, 0.06f);
                }
            var path = $"{EnvDir}/T_StationWindow.png";
            var t = Load<Texture2D>(path);
            return t != null && t.width == N ? t : SaveTexture("T_StationWindow", px, N, N, false, false);
        }

        // ------------------------------------------------------------------ shapes

        static readonly Dictionary<PrimitiveType, Mesh> primCache = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<string, Mesh> torusCache = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Mesh> dishCache = new Dictionary<string, Mesh>();

        static Mesh Prim(PrimitiveType t)
        {
            if (primCache.TryGetValue(t, out var m) && m != null) return m;
            var g = GameObject.CreatePrimitive(t);
            m = g.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(g);
            primCache[t] = m;
            return m;
        }

        /// <summary>A ring round the Y axis: radius R to the middle of the tube, tube radius r.</summary>
        static Mesh TorusMesh(float R, float r)
        {
            string key = $"Torus_{R:F1}_{r:F1}";
            if (torusCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            int seg = Mathf.Clamp(Mathf.RoundToInt(R / 6f), 24, 160), side = r > 10f ? 16 : 8;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j * Mathf.PI * 2f / side;
                    Vector3 d = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c * R + d * r);
                    n.Add(d);
                    uv.Add(new Vector2(i / (float)seg, j / (float)side));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + side + 1;
                    tri.Add(a0); tri.Add(a0 + 1); tri.Add(a1);
                    tri.Add(a1); tri.Add(a0 + 1); tri.Add(a1 + 1);
                }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            torusCache[key] = mesh;
            return mesh;
        }

        /// <summary>A shallow dish opening toward +Z (both faces), radius and depth in metres.</summary>
        static Mesh DishMesh(float radius, float depth)
        {
            string key = $"Dish_{radius:F1}_{depth:F1}";
            if (dishCache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            const int rings = 8, seg = 32;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int face = 0; face < 2; face++)
            {
                int start = v.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float rho = radius * i / rings;
                    float z = depth * (rho / radius) * (rho / radius);
                    float slope = 2f * depth * rho / (radius * radius);
                    for (int j = 0; j <= seg; j++)
                    {
                        float a = j * Mathf.PI * 2f / seg;
                        Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                        v.Add(dir * rho + Vector3.forward * (z + (face == 1 ? -0.4f : 0f)));
                        Vector3 nn = (Vector3.forward - dir * slope).normalized;
                        n.Add(face == 0 ? nn : -nn);
                        uv.Add(new Vector2(j / (float)seg, i / (float)rings));
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < seg; j++)
                    {
                        int a0 = start + i * (seg + 1) + j, a1 = a0 + seg + 1;
                        if (face == 0) { tri.Add(a0); tri.Add(a1); tri.Add(a0 + 1); tri.Add(a0 + 1); tri.Add(a1); tri.Add(a1 + 1); }
                        else { tri.Add(a0); tri.Add(a0 + 1); tri.Add(a1); tri.Add(a0 + 1); tri.Add(a1 + 1); tri.Add(a1); }
                    }
            }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            dishCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// The pieces of one part, in its own space, ready to be baked into a single mesh.
        /// </summary>
        sealed class Geo
        {
            public readonly Transform t;
            public Geo(Transform parent, string name) { t = Node(name, parent).transform; }

            public Transform Piece(Mesh mesh, Material m, Vector3 pos, Quaternion rot, Vector3 scale)
            {
                var go = new GameObject("piece", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(t, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
                go.transform.localScale = scale;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                return go.transform;
            }

            public void Box(Material m, Vector3 c, Vector3 size, Quaternion? rot = null) =>
                Piece(Prim(PrimitiveType.Cube), m, c, rot ?? Quaternion.identity, size);

            /// <summary>A bar of square section from a to b.</summary>
            public void Rod(Material m, Vector3 a, Vector3 b, float th)
            {
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-4f) return;
                Piece(Prim(PrimitiveType.Cube), m, (a + b) * 0.5f, Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up),
                      new Vector3(th, th, d.magnitude));
            }

            /// <summary>A round tube from a to b.</summary>
            public void Cyl(Material m, Vector3 a, Vector3 b, float radius)
            {
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-4f) return;
                Piece(Prim(PrimitiveType.Cylinder), m, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d),
                      new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f));
            }

            public void Disc(Material m, Vector3 c, Vector3 normal, float radius, float thick) =>
                Cyl(m, c - normal.normalized * thick * 0.5f, c + normal.normalized * thick * 0.5f, radius);

            public void Ball(Material m, Vector3 c, float r) => Piece(Prim(PrimitiveType.Sphere), m, c, Quaternion.identity, Vector3.one * r * 2f);
            public void Ball(Material m, Vector3 c, Vector3 radii, Quaternion rot) => Piece(Prim(PrimitiveType.Sphere), m, c, rot, radii * 2f);

            public void Torus(Material m, Vector3 c, Vector3 axis, float R, float r) =>
                Piece(TorusMesh(R, r), m, c, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);

            public void Dish(Material m, Vector3 c, Vector3 facing, float radius, float depth) =>
                Piece(DishMesh(radius, depth), m, c, Quaternion.LookRotation(facing), Vector3.one);

            /// <summary>A square lattice girder from a to b, `w` wide, of `th` bars.</summary>
            public void Truss(Material m, Vector3 a, Vector3 b, float w, float th)
            {
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1f) return;
                d /= len;
                Vector3 u = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized * (w * 0.5f);
                Vector3 v = Vector3.Cross(d, u).normalized * (w * 0.5f);
                var corners = new[] { u + v, u - v, -u - v, -u + v };
                foreach (var c in corners) Rod(m, a + c, b + c, th);
                // square bays, but no more than 40 to a girder (a long one gets longer bays)
                int bays = Mathf.Clamp(Mathf.RoundToInt(len / w), 1, 40);
                for (int i = 0; i <= bays; i++)
                {
                    Vector3 p = a + d * (len * i / bays);
                    for (int k = 0; k < 4; k++) Rod(m, p + corners[k], p + corners[(k + 1) % 4], th * 0.8f);
                    if (i == bays) continue;
                    Vector3 q = a + d * (len * (i + 1) / bays);
                    for (int k = 0; k < 4; k++)
                        Rod(m, p + corners[k], q + corners[(k + ((i & 1) == 0 ? 1 : 3)) % 4], th * 0.6f);
                }
            }

            /// <summary>A solar wing: a boom from `root` along `along`, with panels either side of
            /// it facing `normal`, `width` across in all.</summary>
            public void Solar(Vector3 root, Vector3 along, Vector3 normal, float length, float width)
            {
                along.Normalize();
                normal = Vector3.ProjectOnPlane(normal, along).normalized;
                Vector3 side = Vector3.Cross(normal, along).normalized;
                Rod(sm.dark, root, root + along * length, 3f);
                int n = Mathf.Max(1, Mathf.RoundToInt(length / 45f));
                float seg = length / n;
                var rot = Quaternion.LookRotation(along, normal);
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = root + along * (seg * (i + 0.5f));
                    foreach (float s in new[] { -1f, 1f })
                        Box(sm.solar, c + side * s * (width * 0.25f + 2f), new Vector3(width * 0.5f - 4f, 0.8f, seg - 3f), rot);
                    Rod(sm.dark, c - side * width * 0.5f, c + side * width * 0.5f, 1.5f);
                }
            }

            /// <summary>A radiator: a thin white fin from `root` along `along`.</summary>
            public void Radiator(Vector3 root, Vector3 along, Vector3 normal, float length, float width)
            {
                along.Normalize();
                Box(sm.white, root + along * length * 0.5f, new Vector3(width, 1.2f, length), Quaternion.LookRotation(along, normal));
                Rod(sm.dark, root, root + along * length, 2f);
            }

            /// <summary>A band of lit windows round a tube from a to b (just proud of its surface).</summary>
            public void WindowBand(Vector3 a, Vector3 b, float radius) => Cyl(sm.window, a, b, radius + 0.4f);
        }

        /// <summary>
        /// Bake a part's pieces into one mesh (a sub-mesh per material), with the texture laid on
        /// by position (`tile` m per repeat, per material), saved as an asset. The pieces are
        /// removed and the part gets the mesh, its renderer and (if `collide`) a collider on the
        /// obstacle layer.
        /// </summary>
        static GameObject Bake(Geo g, string asset, bool collide, int layer, bool shadows = true)
        {
            var root = g.t;
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            var toLocal = root.worldToLocalMatrix;
            var pieces = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.transform != root).ToList();
            foreach (var mf in pieces)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var m = mats[Mathf.Min(s, mats.Length - 1)];
                    if (!byMat.TryGetValue(m, out var list)) byMat[m] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = toLocal * mf.transform.localToWorldMatrix });
                }
            }
            var parts = new List<CombineInstance>();
            var order = new List<Material>();
            foreach (var kv in byMat)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(kv.Value.ToArray(), true, true);
                // texture by position: each vertex takes the two axes across its normal
                float tile = UvTile.TryGetValue(kv.Key, out var tl) ? tl : 16f;
                var vs = part.vertices; var ns = part.normals; var uv = new Vector2[vs.Length];
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 n = ns.Length == vs.Length ? ns[i] : Vector3.up, p = vs[i];
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    uv[i] = (ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y)) / tile;
                }
                part.uv = uv;
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                order.Add(kv.Key);
            }
            var mesh = new Mesh { name = asset, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), false, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            foreach (var p in parts) Object.DestroyImmediate(p.mesh);
            string path = $"{StructDir}/{asset}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            foreach (var mf in pieces) if (mf != null) Object.DestroyImmediate(mf.gameObject);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = root.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = order.ToArray();
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collide)
            {
                root.gameObject.layer = layer;
                root.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            return root.gameObject;
        }

        /// <summary>A blinking navigation light (and, with `range`, a real light).</summary>
        static GameObject NavLight(Transform parent, Vector3 pos, Material m, float size, float period, float phase, float range = 0f, Color? colour = null)
        {
            var go = new GameObject("Nav light", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshFilter>().sharedMesh = Prim(PrimitiveType.Sphere);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var b = go.AddComponent<Beacon>();
            b.period = period;
            b.phase = phase;
            if (range > 0f)
            {
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(go.transform, false);
                l.type = LightType.Point;
                l.color = colour ?? Color.white;
                l.range = range;
                l.intensity = 6f;
                l.shadows = LightShadows.None;
            }
            return go;
        }

        /// <summary>A steady floodlight (a real light, no shadows).</summary>
        static Light Flood(Transform parent, Vector3 pos, Color c, float range, float intensity)
        {
            var l = new GameObject("Floodlight").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        // ------------------------------------------------------------------ planetary bases

        /// <summary>A base building: its pieces baked, a collider, standing on its pivot (the
        /// ground), its front (+Z) toward the landing pad.</summary>
        static GameObject Building(string name, System.Action<Geo> build, System.Action<Transform> extras = null)
        {
            int layer = ObstacleLayer();
            var root = new GameObject(name);
            root.layer = layer;
            var g = new Geo(root.transform, "Mesh");
            build(g);
            Bake(g, name, true, layer);
            extras?.Invoke(root.transform);
            Directory.CreateDirectory(BaseDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{BaseDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>The buildings of a colony: a dome, habitats, a control tower, tanks, a solar
        /// field, a comms mast and a hangar (about 30 to 90 m each).</summary>
        static GameObject[] ColonyBuildings()
        {
            var list = new List<GameObject>
            {
                Building("Colony_Dome", g =>
                {
                    g.Cyl(sm.hull, Vector3.zero, Vector3.up * 5f, 36f);
                    g.Ball(sm.glass, Vector3.up * 3f, new Vector3(34f, 27f, 34f), Quaternion.identity);
                    g.Torus(sm.window, Vector3.up * 5.2f, Vector3.up, 35.2f, 1.1f);
                    g.Box(sm.hull, new Vector3(0f, 5f, 36f), new Vector3(10f, 10f, 14f));
                    g.Box(sm.window, new Vector3(0f, 6f, 43.1f), new Vector3(6f, 4f, 0.4f));
                    g.Box(sm.hull, new Vector3(-30f, 4f, -18f), new Vector3(14f, 8f, 12f), Quaternion.Euler(0f, 30f, 0f));
                }, t => NavLight(t, new Vector3(0f, 31f, 0f), sm.red, 2.2f, 2f, 0f)),
                Building("Colony_Habitat", g =>
                {
                    g.Box(sm.hull, new Vector3(0f, 9f, 0f), new Vector3(64f, 18f, 22f));
                    foreach (float z in new[] { -11.3f, 11.3f })
                    foreach (float y in new[] { 5f, 13f })
                        g.Box(sm.window, new Vector3(0f, y, z), new Vector3(58f, 3.2f, 0.5f));
                    g.Box(sm.dark, new Vector3(-18f, 19.5f, 0f), new Vector3(14f, 3f, 10f));
                    g.Box(sm.dark, new Vector3(16f, 19f, 3f), new Vector3(8f, 2f, 8f));
                    g.Cyl(sm.white, new Vector3(4f, 18f, -6f), new Vector3(4f, 23f, -6f), 1.6f);
                    g.Box(sm.hull, new Vector3(26f, 7f, -24f), new Vector3(16f, 14f, 30f));
                    g.Box(sm.window, new Vector3(34.3f, 8f, -24f), new Vector3(0.5f, 3f, 26f));
                    g.Box(sm.hazard, new Vector3(0f, 0.6f, 11.6f), new Vector3(64f, 1.2f, 0.4f));
                }),
                Building("Colony_Tower", g =>
                {
                    g.Cyl(sm.hull, Vector3.zero, Vector3.up * 56f, 6f);
                    g.Box(sm.hull, new Vector3(0f, 2.5f, 0f), new Vector3(18f, 5f, 18f));
                    g.Box(sm.dark, new Vector3(0f, 60f, 0f), new Vector3(20f, 9f, 20f));
                    g.Box(sm.window, new Vector3(0f, 60.5f, 0f), new Vector3(20.6f, 4f, 20.6f));
                    g.Box(sm.hull, new Vector3(0f, 65f, 0f), new Vector3(23f, 1.6f, 23f));
                    g.Rod(sm.dark, new Vector3(0f, 65f, 0f), new Vector3(0f, 88f, 0f), 0.9f);
                    g.Dish(sm.white, new Vector3(7f, 68f, 6f), new Vector3(0.3f, 0.8f, 0.5f), 4f, 1.2f);
                }, t => NavLight(t, new Vector3(0f, 89f, 0f), sm.red, 2f, 1.4f, 0.3f)),
                Building("Colony_Tanks", g =>
                {
                    g.Box(sm.dark, new Vector3(0f, 0.6f, 0f), new Vector3(52f, 1.2f, 46f));
                    foreach (var (x, z) in new[] { (-14f, -10f), (14f, -10f), (0f, 13f) })
                    {
                        g.Cyl(sm.white, new Vector3(x, 0f, z), new Vector3(x, 24f, z), 9f);
                        g.Ball(sm.white, new Vector3(x, 24f, z), new Vector3(9f, 4f, 9f), Quaternion.identity);
                        g.Torus(sm.hazard, new Vector3(x, 12f, z), Vector3.up, 9.1f, 0.6f);
                    }
                    g.Cyl(sm.hull, new Vector3(-14f, 4f, -10f), new Vector3(14f, 4f, -10f), 1.2f);
                    g.Cyl(sm.hull, new Vector3(14f, 4f, -10f), new Vector3(0f, 4f, 13f), 1.2f);
                    g.Cyl(sm.hull, new Vector3(0f, 4f, 13f), new Vector3(0f, 4f, 26f), 1.2f);
                }),
                Building("Colony_Solar", g =>
                {
                    for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++)
                        {
                            Vector3 c = new Vector3(i * 17f, 0f, j * 13f);
                            g.Rod(sm.dark, c, c + Vector3.up * 3.6f, 0.7f);
                            g.Box(sm.solar, c + Vector3.up * 4f, new Vector3(15f, 0.4f, 10f), Quaternion.Euler(-24f, 0f, 0f));
                        }
                }),
                Building("Colony_Mast", g =>
                {
                    g.Truss(sm.dark, Vector3.zero, Vector3.up * 82f, 5f, 0.6f);
                    g.Box(sm.hull, new Vector3(0f, 1.5f, 0f), new Vector3(12f, 3f, 12f));
                    g.Dish(sm.white, new Vector3(0f, 74f, 4f), new Vector3(0f, 0.55f, 1f), 13f, 3.5f);
                    g.Rod(sm.dark, new Vector3(0f, 74f, 0f), new Vector3(0f, 74f, 4f), 1.4f);
                }, t => NavLight(t, new Vector3(0f, 83f, 0f), sm.red, 2f, 1.8f, 0.6f)),
                Building("Colony_Hangar", g =>
                {
                    g.Cyl(sm.hull, new Vector3(0f, 0f, -32f), new Vector3(0f, 0f, 30f), 17f);
                    g.Box(sm.dark, new Vector3(0f, 6.5f, 30.3f), new Vector3(22f, 13f, 0.6f));
                    g.Box(sm.hazard, new Vector3(0f, 13.6f, 30.4f), new Vector3(24f, 1.2f, 0.6f));
                    g.Box(sm.window, new Vector3(-17.2f, 6f, 0f), new Vector3(0.5f, 3f, 50f));
                    g.Box(sm.window, new Vector3(17.2f, 6f, 0f), new Vector3(0.5f, 3f, 50f));
                }),
            };
            return list.ToArray();
        }

        /// <summary>The buildings of a hostile fortress: bunkers, a command post, a radar tower,
        /// wall sections, fuel tanks, barracks and a watchtower (armoured, with red lights).</summary>
        static GameObject[] FortBuildings(SurfaceKit kit)
        {
            var bunker = kit.bunker;
            var armor = kit.armor;
            var warn = kit.warning;
            UvTile[bunker] = 14f;
            UvTile[armor] = 14f;
            var list = new List<GameObject>
            {
                Building("Fort_Bunker", g =>
                {
                    g.Box(bunker, new Vector3(0f, 4f, -2f), new Vector3(40f, 8f, 26f));
                    g.Box(bunker, new Vector3(0f, 3.2f, 11.5f), new Vector3(40f, 6.5f, 8f), Quaternion.Euler(-28f, 0f, 0f));
                    g.Box(warn, new Vector3(0f, 6f, 10.6f), new Vector3(30f, 0.9f, 0.4f));
                    g.Cyl(armor, new Vector3(0f, 8f, -4f), new Vector3(0f, 10.5f, -4f), 6f);
                    g.Rod(armor, new Vector3(0f, 11f, -4f), new Vector3(0f, 11.5f, 8f), 1.2f);
                }),
                Building("Fort_Command", g =>
                {
                    g.Box(bunker, new Vector3(0f, 8f, 0f), new Vector3(56f, 16f, 44f));
                    g.Box(bunker, new Vector3(0f, 20f, -4f), new Vector3(36f, 8f, 28f));
                    g.Box(armor, new Vector3(0f, 27f, -6f), new Vector3(20f, 6f, 16f));
                    g.Box(warn, new Vector3(0f, 21f, 10.2f), new Vector3(30f, 1.2f, 0.4f));
                    g.Box(warn, new Vector3(0f, 12f, 22.2f), new Vector3(44f, 1f, 0.4f));
                    g.Rod(armor, new Vector3(-6f, 30f, -6f), new Vector3(-6f, 52f, -6f), 0.8f);
                    g.Rod(armor, new Vector3(6f, 30f, -8f), new Vector3(6f, 44f, -8f), 0.8f);
                    g.Box(armor, new Vector3(0f, 1.5f, 24f), new Vector3(16f, 3f, 6f));
                }, t => { NavLight(t, new Vector3(-6f, 53f, -6f), sm.red, 2f, 1.2f, 0f); NavLight(t, new Vector3(6f, 45f, -8f), sm.red, 1.6f, 1.2f, 0.5f); }),
                Building("Fort_Radar", g =>
                {
                    g.Cyl(armor, Vector3.zero, Vector3.up * 32f, 4f);
                    g.Box(bunker, new Vector3(0f, 2f, 0f), new Vector3(14f, 4f, 14f));
                    g.Box(bunker, new Vector3(0f, 33f, 0f), new Vector3(14f, 2f, 14f));
                }, t =>
                {
                    // the dish turns
                    var spin = Node("Dish", t, new Vector3(0f, 35f, 0f));
                    var dg = new Geo(spin.transform, "Mesh");
                    dg.Dish(armor, new Vector3(0f, 6f, 1f), new Vector3(0f, 0.25f, 1f), 11f, 2.5f);
                    dg.Rod(armor, Vector3.zero, new Vector3(0f, 6f, 0f), 1.4f);
                    Bake(dg, "Fort_Radar_Dish", false, 0);
                    spin.AddComponent<Drift>().spin = new Vector3(0f, 40f, 0f);
                }),
                Building("Fort_Wall", g =>
                {
                    g.Box(bunker, new Vector3(0f, 6f, 0f), new Vector3(90f, 12f, 6f));
                    foreach (float x in new[] { -38f, -13f, 13f, 38f })
                    {
                        g.Box(bunker, new Vector3(x, 4f, -5f), new Vector3(6f, 8f, 6f));
                        g.Box(bunker, new Vector3(x, 13f, 0f), new Vector3(8f, 2.5f, 7f));
                    }
                    g.Box(warn, new Vector3(0f, 9f, 3.1f), new Vector3(80f, 0.6f, 0.3f));
                }),
                Building("Fort_Tanks", g =>
                {
                    g.Box(bunker, new Vector3(0f, 0.8f, 0f), new Vector3(46f, 1.6f, 26f));
                    foreach (float x in new[] { -11f, 11f })
                    {
                        g.Ball(armor, new Vector3(x, 12f, 0f), 10f);
                        for (int k = 0; k < 4; k++)
                        {
                            float a = k * Mathf.PI * 0.5f + 0.785f;
                            g.Rod(bunker, new Vector3(x + Mathf.Cos(a) * 7f, 0f, Mathf.Sin(a) * 7f), new Vector3(x + Mathf.Cos(a) * 6f, 8f, Mathf.Sin(a) * 6f), 1.2f);
                        }
                    }
                    g.Cyl(armor, new Vector3(-11f, 3f, 0f), new Vector3(11f, 3f, 0f), 1.4f);
                }),
                Building("Fort_Barracks", g =>
                {
                    g.Box(bunker, new Vector3(0f, 5f, 0f), new Vector3(62f, 10f, 18f));
                    g.Box(armor, new Vector3(0f, 10.6f, 0f), new Vector3(64f, 1.2f, 20f));
                    g.Box(warn, new Vector3(0f, 6f, 9.1f), new Vector3(54f, 0.8f, 0.3f));
                    g.Box(bunker, new Vector3(-24f, 12.5f, 0f), new Vector3(8f, 3f, 8f));
                }),
                Building("Fort_Watchtower", g =>
                {
                    g.Cyl(bunker, Vector3.zero, Vector3.up * 28f, 5f);
                    g.Box(bunker, new Vector3(0f, 2f, 0f), new Vector3(14f, 4f, 14f));
                    g.Box(armor, new Vector3(0f, 31f, 0f), new Vector3(13f, 6f, 13f));
                    g.Box(warn, new Vector3(0f, 31.5f, 0f), new Vector3(13.4f, 0.9f, 13.4f));
                    g.Box(armor, new Vector3(0f, 34.6f, 0f), new Vector3(15f, 1.2f, 15f));
                }, t => NavLight(t, new Vector3(0f, 36.5f, 0f), sm.red, 1.8f, 1.1f, 0.2f)),
            };
            return list.ToArray();
        }
    }
}
