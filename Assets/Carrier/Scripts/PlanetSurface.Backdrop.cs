using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>The sea, the far ground beyond the streamed areas, and the sky.</summary>
    public partial class PlanetSurface
    {
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
    }
}
