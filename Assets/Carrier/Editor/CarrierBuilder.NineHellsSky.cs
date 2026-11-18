using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Nine Hells' sky: Gilgal's ring, the comets Patmos and Ophir on their long ellipses,
    /// and the meteoroid streams that follow them, Havilah behind Patmos and Sheba behind Ophir
    /// (a long thin swarm of small rocks on the comet's own orbit, a fair way behind it, to mine).
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>A comet's orbit (CarrierBuilder.NineHells sets it up): near the star now or far out.</summary>
        class NhComet { public Transform t; public bool near; public double e; }
        /// <summary>A stream on a comet's orbit, `behind` (radians of mean anomaly) after it.</summary>
        class NhStream { public Transform t; public string comet; public double behind; }

        static void NineHellsSky(NineHellsRefs R)
        {
            GilgalRing(R);
            var rockTex = R.rockMat.GetTexture("_BaseMap") as Texture2D;
            var rockNormal = R.rockMat.GetTexture("_BumpMap") as Texture2D;
            var nucleusMat = Lit("Comet_Nucleus", new Color(0.3f, 0.3f, 0.32f), rockTex, rockNormal, 0f, 0.2f);
            var iceMat = Lit("Comet_Ice", new Color(0.78f, 0.86f, 0.96f), rockTex, rockNormal, 0f, 0.62f);
            Vector3 Along(float angleDeg, float climb) => (Polar(angleDeg + 90f, 1f, 0f) / 1000f + Vector3.up * climb).normalized;
            var patmos = MakeComet("Comet Patmos", R.sys, Polar(140f, 60f, 5f), Along(140f, 0.2f), 1700f, 70000f, 1f, 9101, R.star, nucleusMat, iceMat, R.layer,
                "A large bright comet on an elliptical orbit, close to the star now: a long blue ion tail and a curved dust tail. " +
                "Water, carbon dioxide, methane and ammonia to mine; the meteoroid stream Havilah follows it.");
            var ophir = MakeComet("Comet Ophir", R.sys, Polar(250f, 330f, -6f), Along(250f, -0.12f), 1400f, 30000f, 0.5f, 9202, R.star, nucleusMat, iceMat, R.layer,
                "Another large comet, far out in the cold on its long ellipse, its tails only beginning to grow. The meteoroid stream " +
                "Sheba trails it, a fair way behind.");
            R.comets.Add(new NhComet { t = patmos.transform, near = true, e = 0.62 });
            R.comets.Add(new NhComet { t = ophir.transform, near = false, e = 0.5 });
            R.streams.Add(new NhStream { t = Stream(R, "Havilah", patmos.transform, 1, 9301), comet = patmos.transform.name, behind = 0.035 });
            R.streams.Add(new NhStream { t = Stream(R, "Sheba", ophir.transform, -1, 9402), comet = ophir.transform.name, behind = 0.02 });
        }

        /// <summary>A meteoroid stream: small rocks strung along the comet's way for some 50 km
        /// (where it is laid out now: behind the comet; its orbit then keeps it there), with its own
        /// warp point.</summary>
        static Transform Stream(NineHellsRefs R, string name, Transform comet, int sense, int seed)
        {
            var comp = comet.GetComponent<Comet>();
            Vector3 way = comp != null ? comp.orbitDirection.normalized : Vector3.forward;
            // (behind the comet on its way: laid out a little behind; its orbit's lag puts it far behind)
            Vector3 c = comet.position - way * 90000f * sense;
            var root = new GameObject(name).transform;
            root.SetParent(R.sys, false);
            root.SetPositionAndRotation(c, Quaternion.LookRotation(way, Vector3.up));
            var rng = new System.Random(seed);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int made = 0;
            for (int i = 0; i < 110; i++)
            {
                // strung along the stream's length, thicker in the middle
                float z = (Rn(-1f, 1f) + Rn(-1f, 1f)) * 0.5f * 25000f;
                float spread = 900f + 900f * (1f - Mathf.Abs(z) / 25000f);
                Vector3 local = new Vector3(Rn(-1f, 1f) * spread, Rn(-0.4f, 0.4f) * spread, z);
                float rr = 15f + 110f * Mathf.Pow(Rn(0f, 1f), 2.6f);
                int variant = 3 + rng.Next(5);
                MakeAsteroid("Meteoroid", root, root.TransformPoint(local), Quaternion.Euler(Rn(0, 360), Rn(0, 360), Rn(0, 360)), rr,
                    100 + variant, 3, 2, new Vector3(1f, Rn(0.55f, 0.95f), Rn(0.7f, 1f)), 0.3f, 4, 0.45f, R.rockMat, R.layer, 1.3f, 0.5f);
                made++;
            }
            var wp = MakeWarpPoint(root.gameObject, name, WarpPoint.Kind.AsteroidField,
                $"A meteoroid stream: grit, ice and small rocks shed by {comet.name}, strung out along its orbit behind it, to mine.",
                25000f, 32000f, new Color(0.7f, 0.72f, 0.8f));
            wp.surface = SurfaceKind.Asteroid;
            Log($"nine hells: {name}: {made} meteoroids along 50 km of {comet.name}'s way");
            return root;
        }

        /// <summary>Gilgal's ring: a flat band in its equator, from 1.35 to 2.3 times its radius, of
        /// fine dusty bands with gaps between them, lit by the star and seen through.</summary>
        static void GilgalRing(NineHellsRefs R)
        {
            if (!R.bodies.TryGetValue("Gilgal", out var gilgal)) return;
            const int Seg = 256;
            float inner = 0.675f, outer = 1.15f;     // (in the planet's own units: its radius is 0.5)
            var mesh = new Mesh { name = "Ring_Gilgal" };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i <= Seg; i++)
            {
                float a = i * Mathf.PI * 2f / Seg;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * inner); uv.Add(new Vector2(0f, i / (float)Seg)); n.Add(Vector3.up);
                v.Add(d * outer); uv.Add(new Vector2(1f, i / (float)Seg)); n.Add(Vector3.up);
                if (i < Seg)
                {
                    int b = i * 2;
                    tri.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
                }
            }
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetNormals(n);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            System.IO.Directory.CreateDirectory(StructDir);
            mesh = SaveMesh(mesh, StructDir + "/Ring_Gilgal.asset");
            var mat = Transparent("Ring_Gilgal", "Universal Render Pipeline/Lit", 0f, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, BlendMode.OneMinusSrcAlpha,
                                  new Color(0.95f, 0.88f, 0.75f, 1f), RingTexture());
            var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(gilgal.transform, false);
            ring.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = ring.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Log("nine hells: Gilgal's ring, 1.35 to 2.3 radii");
        }

        /// <summary>The ring's bands across it (u: inner edge to outer), with gaps; alpha the dust.</summary>
        static Texture2D RingTexture()
        {
            const int W = 1024, H = 4;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "T_Ring_Gilgal", wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(4401);
            var px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                float dense = 0.55f + 0.3f * Mathf.PerlinNoise(u * 22f, 0.3f) + 0.25f * Mathf.PerlinNoise(u * 90f, 1.7f);
                // the gaps: a wide one two-thirds out, narrower ones, and soft edges
                dense *= 1f - Gap(u, 0.66f, 0.025f) - 0.8f * Gap(u, 0.32f, 0.008f) - 0.7f * Gap(u, 0.86f, 0.006f);
                dense *= Mathf.SmoothStep(0f, 1f, u / 0.06f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.1f);
                float shade = 0.75f + 0.25f * Mathf.PerlinNoise(u * 40f, 5.1f);
                var c = new Color(shade, shade * 0.93f, shade * 0.82f, Mathf.Clamp01(dense) * 0.85f);
                for (int y = 0; y < H; y++) px[y * W + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            System.IO.Directory.CreateDirectory(EnvDir);
            string path = EnvDir + "/T_Ring_Gilgal.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Gap(float u, float at, float half) => Mathf.Clamp01(1f - Mathf.Abs(u - at) / half);
    }
}
