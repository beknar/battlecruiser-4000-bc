using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{

    /// <summary>
    /// An asteroid shaped from a seed: a subdivided sphere stretched to its size, roughened with
    /// fractal noise and pitted with craters. The mesh (and its collider) are built when the level
    /// loads, so the same seed always gives the same rock and nothing large is stored in the
    /// project. Rocks that share a seed and detail share one mesh.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ProceduralAsteroid : MonoBehaviour
    {
        public int seed = 1;
        [Range(1, 6)] public int detail = 3;
        [Tooltip("Collider detail (lower is cheaper).")]
        [Range(1, 6)] public int colliderDetail = 2;
        [Tooltip("Half-size along each axis before roughening (m). Leave at 1 for a rock scaled by its transform.")]
        public Vector3 semiAxes = Vector3.one;
        [Range(0f, 0.6f)] public float roughness = 0.25f;
        public int craters = 12;
        [Tooltip("Texture repeat size, in the mesh's own units.")]
        public float uvTile = 0.5f;

        static readonly Dictionary<string, (Mesh render, Mesh collide)> Cache = new Dictionary<string, (Mesh, Mesh)>();

        void Awake() => Build();

        string Key => $"{seed}_{detail}_{colliderDetail}_{semiAxes}_{roughness}_{craters}_{uvTile}";

        /// <summary>Build (or reuse) the mesh and collider. Safe to call in the editor.</summary>
        public void Build()
        {
            if (!Cache.TryGetValue(Key, out var m) || m.render == null)
            {
                m = (Generate(detail, true), Generate(colliderDetail, false));
                Cache[Key] = m;
            }
            GetComponent<MeshFilter>().sharedMesh = m.render;
            var mc = GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = m.collide;
        }

        /// <summary>Drop the generated meshes (before saving a scene, so they aren't stored in it).</summary>
        public void Clear()
        {
            GetComponent<MeshFilter>().sharedMesh = null;
            var mc = GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = null;
        }

        /// <summary>Forget (and free) every generated mesh.</summary>
        public static void ClearCache()
        {
            foreach (var m in Cache.Values)
                foreach (var mesh in new[] { m.render, m.collide })
                    if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            Cache.Clear();
        }

        /// <summary>Surface radius factor along a unit direction (1 = the plain ellipsoid).</summary>
        public float Height(Vector3 d, List<(Vector3 c, float a, float depth)> crater)
        {
            Vector3 off = new Vector3(seed * 7.13f, seed * 3.71f, seed * 5.37f);
            float h = 1f + roughness * ProcNoise.Fbm(d * 1.7f + off, seed, 5, 0.52f)
                         + roughness * 0.45f * (ProcNoise.Ridged(d * 3.1f + off, seed + 99, 4) - 0.5f);
            foreach (var (c, a, depth) in crater)
            {
                float x = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, c), -1f, 1f)) / a;
                if (x < 1f) h -= depth * (1f - x * x);
                else if (x < 1.45f) h += depth * 0.3f * (1f - (x - 1f) / 0.45f);
            }
            return h;
        }

        Mesh Generate(int level, bool forRendering)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere(level, verts, tris);
            var rng = new System.Random(seed * 7919 + 17);
            var crater = new List<(Vector3, float, float)>();
            for (int i = 0; i < craters; i++)
            {
                var c = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                if (c.sqrMagnitude < 1e-4f) c = Vector3.up;
                float a = Mathf.Lerp(0.07f, 0.38f, Mathf.Pow((float)rng.NextDouble(), 1.8f));
                crater.Add((c.normalized, a, a * 0.32f));
            }
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 d = verts[i];
                verts[i] = Vector3.Scale(d * Height(d, crater), semiAxes);
            }

            var shared = new Mesh { name = $"Asteroid_{seed}_{level}" };
            shared.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            shared.SetVertices(verts);
            shared.SetTriangles(tris, 0);
            shared.RecalculateNormals();
            shared.RecalculateBounds();
            shared.hideFlags = HideFlags.DontSave;
            if (!forRendering) return shared;

            // texture coordinates by box projection: each triangle takes the two axes it faces
            // least along, so the rock texture never smears; normals stay smooth across seams
            var sn = shared.normals;
            int n = tris.Count;
            var pv = new Vector3[n];
            var pn = new Vector3[n];
            var uv = new Vector2[n];
            var idx = new int[n];
            float s = 1f / Mathf.Max(1e-3f, uvTile);
            for (int t = 0; t < n; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                fn = new Vector3(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z));
                int axis = fn.x > fn.y ? (fn.x > fn.z ? 0 : 2) : (fn.y > fn.z ? 1 : 2);
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t + k];
                    Vector3 p = verts[vi];
                    pv[t + k] = p;
                    pn[t + k] = sn[vi];
                    uv[t + k] = axis == 0 ? new Vector2(p.z, p.y) * s : axis == 1 ? new Vector2(p.x, p.z) * s : new Vector2(p.x, p.y) * s;
                    idx[t + k] = t + k;
                }
            }
            var mesh = new Mesh { name = shared.name + "_render" };
            mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = pv;
            mesh.normals = pn;
            mesh.uv = uv;
            mesh.triangles = idx;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.DontSave;
            if (Application.isPlaying) Destroy(shared); else DestroyImmediate(shared);
            return mesh;
        }

        static void Icosphere(int level, List<Vector3> verts, List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            foreach (var v in new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            }) verts.Add(v.normalized);
            tris.AddRange(new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            });
            // make every face point outward (Unity's front faces are the ones whose
            // cross(b - a, c - a) points at the viewer)
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }
            for (int l = 0; l < level; l++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>(tris.Count * 4);
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int i)) return i;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    mid[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris.Clear();
                tris.AddRange(next);
            }
        }
    }
}
