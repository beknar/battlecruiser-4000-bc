using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The frigate's combat systems and the enemies' weapons:
    /// - CIWS: two clusters of two rotary-cannon mounts on the top deck beside the conning
    ///   tower, which engage only drones and fighters (CiwsDirector picks their targets);
    /// - the main gun: a bow beam emitter that charges for 10 s and fires a stream of white
    ///   particles (MainBeam);
    /// - the shield: the StylizedShieldsVol1 hexagon sphere round the whole ship, which ripples
    ///   where projectiles, cannon shells, missiles and beams hit it (Shield + ShieldBubble shader);
    /// - enemy weapons, so there is something to hit the shield: drone blasters, and the cruiser's
    ///   cannons, missiles and a red charged beam (EnemyWeapons).
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string ShieldKit = Root + "/Kit/StylizedShieldsVol1";

        static void FrigateSystemMaterials()
        {
            var hex = Load<Texture2D>(ShieldKit + "/Textures/T_ShieldHexagonUV_01.png");
            var noise = Load<Texture2D>(ShieldKit + "/Textures/T_Voronoi01.png");
            var glow = Load<Texture2D>(ShieldKit + "/Textures/T_Glow_02.png");
            if (hex == null || glow == null) Log("shield kit textures missing under " + ShieldKit);

            var shader = Shader.Find("Carrier/ShieldBubble");
            if (shader == null) Log("ShieldBubble shader not found");
            var shield = new Material(shader);
            shield.SetTexture("_MainTex", hex);
            shield.SetTexture("_NoiseTex", noise);
            shield.SetColor("_Color", new Color(0.35f, 0.75f, 1.8f));
            shield.SetFloat("_HitRadius", 22f);
            shield.renderQueue = (int)RenderQueue.Transparent + 10;
            SaveMat("Shield_Bubble", shield);

            ParticleAdditive("FX_BeamParticle", glow, new Color(2.4f, 2.5f, 2.8f));
            Lit("Beam_Lens", new Color(0.18f, 0.2f, 0.24f), metallic: 0.3f, smooth: 0.9f, emission: Color.black);
            Additive("FX_EnemyCannon", new Color(1f, 0.38f, 0.1f) * 7f);
            Additive("FX_EnemyBlaster", new Color(1f, 0.16f, 0.16f) * 6f);
            Additive("FX_CiwsBolt", new Color(1f, 0.85f, 0.45f) * 7f);
        }

        /// <summary>URP particle material, additive, using the texture's alpha and the particle colour.</summary>
        static Material ParticleAdditive(string name, Texture2D tex, Color tint) =>
            Transparent(name, "Universal Render Pipeline/Particles/Unlit", 2f, BlendMode.SrcAlpha, BlendMode.One, BlendMode.One, tint, tex);

        // ------------------------------------------------------------------ ships that come apart
        static Explosion breakupBlast;
        const string ChunkDir = Root + "/Generated/Chunks";

        static readonly HashSet<string> chunksMade = new HashSet<string>();

        /// <summary>Start a build's chunks. The folder is kept (chunks are overwritten in place, so
        /// the prefabs that use them don't change); RemoveStaleChunks clears out the rest.</summary>
        static void ResetChunkFolder()
        {
            chunksMade.Clear();
            if (!AssetDatabase.IsValidFolder(Root + "/Generated")) AssetDatabase.CreateFolder(Root, "Generated");
            if (!AssetDatabase.IsValidFolder(ChunkDir)) AssetDatabase.CreateFolder(Root + "/Generated", "Chunks");
        }

        /// <summary>Delete chunks this build didn't make (from a ship that has gone, or has fewer).</summary>
        static void RemoveStaleChunks()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { ChunkDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!chunksMade.Contains(path) && AssetDatabase.DeleteAsset(path)) Log("removed old chunk " + path);
            }
        }

        /// <summary>
        /// Rebuild a ship's model as chunks it can break into (ShipBreakup). The mesh's own
        /// separate parts (engines, plates, fins: connected pieces of the mesh) are kept whole and
        /// grouped by position; any part too big to be one chunk (the main hull) is cut by region.
        /// The chunks replace the model, so the intact ship looks exactly the same.
        /// </summary>
        static void MakeBreakable(GameObject root, GameObject model, string name, int chunkCount, bool capital, Transform[] extraParts = null)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var triMat = new List<int>();
            var mats = new List<Material>();
            var toRoot = root.transform.worldToLocalMatrix;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (mesh == null || r == null) continue;
                var M = toRoot * mf.transform.localToWorldMatrix;
                int baseV = verts.Count;
                var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(M.MultiplyPoint3x4(mv[i]));
                    norms.Add(mn.Length == mv.Length ? M.MultiplyVector(mn[i]).normalized : Vector3.up);
                    uvs.Add(mu.Length == mv.Length ? mu[i] : Vector2.zero);
                }
                var rm = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = rm.Length > 0 ? rm[Mathf.Min(s, rm.Length - 1)] : null;
                    int mi = mats.IndexOf(mat);
                    if (mi < 0) { mats.Add(mat); mi = mats.Count - 1; }
                    var t = mesh.GetTriangles(s);
                    for (int k = 0; k < t.Length; k += 3)
                    {
                        tris.Add(baseV + t[k]); tris.Add(baseV + t[k + 1]); tris.Add(baseV + t[k + 2]);
                        triMat.Add(mi);
                    }
                }
            }
            int triCount = triMat.Count;
            if (triCount == 0) { Log(name + ": no mesh to break up"); return; }

            // connected parts: union-find over triangles sharing (welded) vertex positions
            var bounds = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) bounds.Encapsulate(v);
            float q = Mathf.Max(1e-4f, bounds.size.magnitude * 2e-5f);
            var parent = new int[verts.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Count; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] / q);
                if (weld.TryGetValue(key, out int other)) Union(i, other); else weld[key] = i;
            }
            for (int t = 0; t < triCount; t++) { Union(tris[t * 3], tris[t * 3 + 1]); Union(tris[t * 3 + 1], tris[t * 3 + 2]); }

            var triCentre = new Vector3[triCount];
            var triArea = new float[triCount];
            var islands = new Dictionary<int, List<int>>();
            float totalArea = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
                triCentre[t] = (a + b + c) / 3f;
                triArea[t] = Mathf.Max(1e-6f, Vector3.Cross(b - a, c - a).magnitude * 0.5f);
                totalArea += triArea[t];
                int id = Find(tris[t * 3]);
                if (!islands.TryGetValue(id, out var list)) islands[id] = list = new List<int>();
                list.Add(t);
            }

            // units to group: whole small parts, or single triangles of the big ones
            var units = new List<(List<int> tris, Vector3 centre, float weight)>();
            float bigPart = totalArea / chunkCount * 0.8f;
            foreach (var isl in islands.Values)
            {
                float area = isl.Sum(t => triArea[t]);
                if (area > bigPart)
                    foreach (var t in isl) units.Add((new List<int> { t }, triCentre[t], triArea[t]));
                else
                {
                    Vector3 c = Vector3.zero;
                    foreach (var t in isl) c += triCentre[t] * triArea[t];
                    units.Add((isl, c / area, area));
                }
            }

            // weighted k-means on the units, seeded by farthest-point sampling
            int clusters = Mathf.Min(chunkCount, units.Count);
            var centres = new List<Vector3> { units.OrderByDescending(u => (u.centre - bounds.center).sqrMagnitude).First().centre };
            while (centres.Count < clusters)
                centres.Add(units.OrderByDescending(u => centres.Min(c => (u.centre - c).sqrMagnitude) * Mathf.Sqrt(u.weight)).First().centre);
            var assign = new int[units.Count];
            for (int iter = 0; iter < 15; iter++)
            {
                for (int u = 0; u < units.Count; u++)
                {
                    float best = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float d = (units[u].centre - centres[c]).sqrMagnitude;
                        if (d < best) { best = d; assign[u] = c; }
                    }
                }
                var sum = new Vector3[centres.Count];
                var w = new float[centres.Count];
                for (int u = 0; u < units.Count; u++) { sum[assign[u]] += units[u].centre * units[u].weight; w[assign[u]] += units[u].weight; }
                for (int c = 0; c < centres.Count; c++) if (w[c] > 0f) centres[c] = sum[c] / w[c];
            }

            // one mesh per chunk, pivoted on its own centre
            var chunkNode = Node("Chunks", root.transform).transform;
            var chunkTransforms = new List<Transform>();
            for (int c = 0; c < centres.Count; c++)
            {
                var myTris = new List<int>();
                for (int u = 0; u < units.Count; u++) if (assign[u] == c) myTris.AddRange(units[u].tris);
                if (myTris.Count == 0) continue;
                Vector3 pivot = Vector3.zero;
                float pw = 0f;
                foreach (var t in myTris) { pivot += triCentre[t] * triArea[t]; pw += triArea[t]; }
                pivot /= pw;

                var remap = new Dictionary<int, int>();
                var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>();
                var perMat = new SortedDictionary<int, List<int>>();
                foreach (var t in myTris)
                {
                    if (!perMat.TryGetValue(triMat[t], out var idx)) perMat[triMat[t]] = idx = new List<int>();
                    for (int j = 0; j < 3; j++)
                    {
                        int ov = tris[t * 3 + j];
                        if (!remap.TryGetValue(ov, out int nv))
                        {
                            nv = cv.Count;
                            remap[ov] = nv;
                            cv.Add(verts[ov] - pivot); cn.Add(norms[ov]); cu.Add(uvs[ov]);
                        }
                        idx.Add(nv);
                    }
                }
                var mesh = new Mesh { name = $"{name}_chunk{c}" };
                if (cv.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(cv);
                mesh.SetNormals(cn);
                mesh.SetUVs(0, cu);
                mesh.subMeshCount = perMat.Count;
                int sm = 0;
                var chunkMats = new List<Material>();
                foreach (var kv in perMat) { mesh.SetTriangles(kv.Value, sm++); chunkMats.Add(mats[kv.Key]); }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                string chunkPath = $"{ChunkDir}/{name}_chunk{c}.asset";
                mesh = SaveMesh(mesh, chunkPath);
                chunksMade.Add(chunkPath);

                var go = Node($"Chunk{c}", chunkNode, pivot);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = chunkMats.ToArray();
                chunkTransforms.Add(go.transform);
            }
            Object.DestroyImmediate(model);
            // fittings (gun mounts and the like) break off as parts of their own
            if (extraParts != null) chunkTransforms.AddRange(extraParts.Where(t => t != null));

            var br = root.AddComponent<ShipBreakup>();
            br.chunks = chunkTransforms.ToArray();
            br.smallBlast = breakupBlast;
            if (capital)
            {
                br.breakTime = 1.0f; br.duration = 5f; br.crackSpeed = 8f; br.blastSpeed = 55f; br.spin = 35f; br.smallBlastScale = 2.2f;
            }
            else
            {
                br.breakTime = 0.35f; br.duration = 4f; br.crackSpeed = 6f; br.blastSpeed = 40f; br.spin = 140f; br.smallBlastScale = 0.5f;
            }
            Log($"{name}: {verts.Count} vertices, {triCount} triangles, {islands.Count} separate parts -> {chunkTransforms.Count} chunks");
        }

        /// <summary>Review shot: an enemy with its chunks pushed apart, to see how it breaks up.</summary>
        static void BreakupShots(Camera cam)
        {
            foreach (var (name, dist) in new[] { ("EnemyCruiser", 520f), ("EnemyDrone", 90f), ("Frigate", 420f) })
            {
                var prefab = Load<GameObject>($"{PrefabDir}/{name}.prefab");
                var br = prefab ? prefab.GetComponent<ShipBreakup>() : null;
                if (br == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = new Vector3(0f, 0f, 20000f);
                var inst = go.GetComponent<ShipBreakup>();
                Vector3 centre = go.transform.position;
                foreach (var c in inst.chunks)
                    c.position += (c.position - centre) * 0.35f + (c.position - centre).normalized * dist * 0.03f;
                cam.transform.position = centre + new Vector3(dist * 0.7f, dist * 0.35f, dist * 0.6f);
                cam.transform.LookAt(centre);
                Render(cam, "breakup_" + name);
                Object.DestroyImmediate(go);
            }
        }

        static void MakeSystemBolts(Prefabs p)
        {
            breakupBlast = p.explosionSmall;
            ResetChunkFolder();
            p.ciwsBolt = MakeBolt("CiwsBolt", "FX_CiwsBolt", new Vector3(0.7f, 0.7f, 18f), 2300f, 8f, null);
            p.enemyCannon = MakeBolt("EnemyCannonShell", "FX_EnemyCannon", new Vector3(1.4f, 1.4f, 12f), 1500f, 30f, p.explosionSmall);
            p.enemyCannon.kind = HitKind.Cannon;
            EditorUtility.SetDirty(p.enemyCannon);
            p.enemyBlaster = MakeBolt("EnemyBlasterBolt", "FX_EnemyBlaster", new Vector3(0.45f, 0.45f, 6f), 1400f, 6f, null);
            p.enemyBlaster.kind = HitKind.Projectile;
            EditorUtility.SetDirty(p.enemyBlaster);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ enemy weapons
        static void ArmDrone(GameObject go, Prefabs p)
        {
            var w = go.AddComponent<EnemyWeapons>();
            w.gunPrefab = p.enemyBlaster;
            w.gunRange = 1800f;
            w.burstInterval = 1.2f;
            w.burstShots = 3;
            w.shotSpacing = 0.12f;
            w.spread = 2.5f;
            w.holdFire = 4f;
        }

        /// <summary>Fighter guns: short, fast bursts along the nose during an attack run.</summary>
        static void ArmFighter(GameObject go, Prefabs p)
        {
            var w = go.AddComponent<EnemyWeapons>();
            w.gunPrefab = p.enemyBlaster;
            w.gunRange = 1500f;
            w.burstInterval = 0.9f;
            w.burstShots = 4;
            w.shotSpacing = 0.08f;
            w.spread = 1.2f;
            w.holdFire = 4f;
            w.fireArc = 18f;
        }

        static void ArmCruiser(GameObject go, Prefabs p)
        {
            var w = go.AddComponent<EnemyWeapons>();
            w.gunPrefab = p.enemyCannon;
            w.gunRange = 3500f;
            w.burstInterval = 4f;
            w.burstShots = 3;
            w.shotSpacing = 0.25f;
            w.spread = 1f;
            w.missilePrefab = p.missile;
            w.missileRange = 4500f;
            w.salvoInterval = 20f;
            w.salvoSize = 3;
            w.holdFire = 4f;

            // a red charged beam from the top of the hull, aimed straight at its target
            var b = LocalBounds(go.transform);
            var node = Node("Beam", go.transform, new Vector3(b.center.x, b.max.y + 3f, b.center.z));
            var muzzle = Node("Muzzle", node.transform).transform;
            var beam = node.AddComponent<MainBeam>();
            beam.muzzle = muzzle;
            beam.chargeTime = 4f;
            beam.fireDuration = 1.6f;
            beam.damagePerSecond = 260f;
            beam.range = 5000f;
            beam.radius = 3f;
            beam.aimAssist = 180f;
            beam.streamSpeed = 2600f;
            BeamEffects(beam, node.transform, muzzle, new Color(1f, 0.25f, 0.2f), 0.7f);
            w.beam = beam;
            w.beamRange = 4500f;
            w.beamInterval = 16f;
        }

        // ------------------------------------------------------------------ frigate systems
        static void AddFrigateSystems(GameObject root, CarrierController cc, GameObject hull, Prefabs p)
        {
            var hullCol = hull.GetComponentInChildren<MeshCollider>();
            Physics.SyncTransforms();

            // hull points: when they run out the frigate comes apart and explodes, then respawns
            var health = root.AddComponent<Health>();
            health.maxHealth = fs.hull;
            health.cannotDie = false;
            health.respawnDelay = 8f;
            health.deathEffect = p.explosion;
            health.deathEffectScale = 2f;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            LogDeckMap(root, hullCol);
            AddCiws(root, hullCol, p);
            AddMainGun(root, hullCol);
            AddShield(root, LocalBounds(hull.transform));
            var miningLaser = AddMiningGear(root, hullCol);

            // the hull's solid shape moves to its own object (the guns check their line of fire
            // against it, and enemy fire hits it when the shield is down), then the hull model is
            // rebuilt as chunks; the gun pods, CIWS mounts and bow gun break off as parts too
            var shape = Node("HullCollider", root.transform);
            shape.AddComponent<MeshCollider>().sharedMesh = hullCol.sharedMesh;
            var fittings = new List<Transform>();
            foreach (var tu in root.GetComponentsInChildren<CarrierTurret>()) fittings.Add(tu.transform);
            var gun = root.GetComponentInChildren<MainBeam>();
            if (gun != null) fittings.Add(gun.transform);
            fittings.Add(miningLaser);
            MakeBreakable(root, hull, fs.prefab, 14, true, fittings.ToArray());
            var br = root.GetComponent<ShipBreakup>();
            if (br != null)
            {
                br.breakTime = 1.1f;
                br.duration = 6f;
                br.crackSpeed = 7f;
                br.blastSpeed = 45f;
                br.spin = 40f;
                br.smallBlastScale = 1.6f;
            }
        }

        static float DeckHeight(Transform root, MeshCollider hullCol, float x, float z)
        {
            var from = root.TransformPoint(new Vector3(x, 200f, z));
            return hullCol != null && hullCol.Raycast(new Ray(from, -root.up), out var hit, 400f)
                ? root.InverseTransformPoint(hit.point).y : float.NaN;
        }

        /// <summary>A coarse height map of the top deck, to see where the conning tower is.</summary>
        static void LogDeckMap(GameObject root, MeshCollider hullCol)
        {
            var sb = new StringBuilder("frigate top-deck heights (m), rows z (bow up), columns x -24..24 step 3\n");
            for (float z = 40f; z >= -70f; z -= 5f)
            {
                sb.Append($"z{z,5:F0}:");
                for (float x = -24f; x <= 24.1f; x += 3f)
                {
                    float y = DeckHeight(root.transform, hullCol, x, z);
                    sb.Append(float.IsNaN(y) ? "    ." : $"{y,5:F0}");
                }
                sb.AppendLine();
            }
            File.WriteAllText("Temp/frigate_deck.txt", sb.ToString());
        }

        /// <summary>Find a flat bit of deck near (x, z): the four corners of the mount's footprint
        /// within `flat` metres of each other, trying a little further out and along the deck.</summary>
        static Vector3 FlatDeckSpot(Transform root, MeshCollider hullCol, float x, float z, float foot, float flat, out float rough)
        {
            Vector3 best = new Vector3(x, 0f, z);
            rough = float.MaxValue;
            // (further out last: the nearest flat spot wins, and only a hull without one near looks far)
            foreach (float dx in new[] { 0f, 1f, 2f, 3f, 4f, -1f, 5f, 6f, -2f, -3f })
            foreach (float dz in new[] { 0f, 2f, -2f, 4f, -4f, 6f, -6f, 8f, -8f, 10f, -10f })
            {
                float px = x + Mathf.Sign(x) * dx, pz = z + dz;
                var hs = new[]
                {
                    DeckHeight(root, hullCol, px, pz),
                    DeckHeight(root, hullCol, px + foot, pz + foot), DeckHeight(root, hullCol, px - foot, pz + foot),
                    DeckHeight(root, hullCol, px + foot, pz - foot), DeckHeight(root, hullCol, px - foot, pz - foot),
                };
                if (hs.Any(float.IsNaN)) continue;
                float r = hs.Max() - hs.Min();
                if (r < rough - 0.05f)
                {
                    rough = r;
                    best = new Vector3(px, hs.Max(), pz);
                }
                if (rough <= flat) return best;
            }
            // no flat spot near: seat it part way down the slope, not on the highest corner, so
            // its base doesn't stand clear of the hull on the low side
            best.y -= rough * 0.6f;
            return best;
        }

        static void AddCiws(GameObject root, MeshCollider hullCol, Prefabs p)
        {
            var node = Node("CIWS", root.transform).transform;
            // the Sentinel: two clusters, port and starboard of the conning tower, two mounts each
            var spots = CiwsSpots();
            var mounts = new List<CarrierTurret>();
            foreach (var (name, s) in spots)
            {
                Vector3 pos = FlatDeckSpot(root.transform, hullCol, s.x, s.y, 2f, 0.6f, out float rough);
                var mount = Node("CIWS_" + name, node, pos);
                var model = Place(OwnModel("CIWS"), mount.transform, "Model");
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Transform Part(string n) => model.GetComponentsInChildren<Transform>().First(t => t.name == n);
                var yaw = Node("Yaw", mount.transform).transform;
                var pitch = Node("Pitch", yaw, new Vector3(0f, 2.9f, 0f)).transform;
                var spin = Node("Spin", pitch).transform;
                var housing = Part("CIWS_Yaw");
                var cradle = Part("CIWS_Pitch");
                var barrels = Part("CIWS_Barrels");
                housing.SetParent(yaw, true);
                cradle.SetParent(pitch, true);
                barrels.SetParent(spin, true);
                Part("CIWS_Base").GetComponent<Renderer>().sharedMaterial = Mats["Hull_Dark"];
                housing.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Main"];
                cradle.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Dark"];
                barrels.GetComponent<Renderer>().sharedMaterial = Mats["Barrel"];
                var muzzle = Node("Muzzle", pitch, new Vector3(0f, 0f, 5.5f)).transform;

                var tu = mount.AddComponent<CarrierTurret>();
                tu.label = "CIWS " + name;
                tu.yaw = yaw;
                tu.pitch = pitch;
                tu.muzzles = new[] { muzzle };
                tu.projectilePrefab = p.ciwsBolt;
                tu.fireSound = Sfx.PointDefense;
                tu.yawSpeed = 160f;
                tu.pitchSpeed = 120f;
                tu.yawLimit = 180f;
                tu.minPitch = -12f;
                tu.maxPitch = 88f;
                tu.fireInterval = 0.06f;
                tu.range = 2200f;
                tu.fireCone = 4f;
                tu.leadTarget = true;
                tu.checkHullClearance = true;
                var bs = spin.gameObject.AddComponent<BarrelSpin>();
                bs.turret = tu;
                // muzzle flash: a flickering glow and a small light at the barrel tips
                var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(flash.GetComponent<Collider>());
                flash.name = "MuzzleFlash";
                flash.transform.SetParent(pitch, false);
                flash.transform.localPosition = new Vector3(0f, 0f, 6.3f);
                flash.transform.localScale = new Vector3(1.1f, 1.1f, 2.4f);
                var fr = flash.GetComponent<Renderer>();
                fr.sharedMaterial = Mats["FX_CiwsBolt"];
                fr.shadowCastingMode = ShadowCastingMode.Off;
                fr.enabled = false;
                var fl = Node("FlashLight", pitch, new Vector3(0f, 0f, 6.5f)).AddComponent<Light>();
                fl.type = LightType.Point;
                fl.color = new Color(1f, 0.8f, 0.45f);
                fl.range = 18f;
                fl.enabled = false;
                bs.flash = fr;
                bs.flashLight = fl;
                mounts.Add(tu);
                Log($"CIWS {name} at {pos:F1} (deck unevenness {rough:F2} m)");
            }
            var dir = root.AddComponent<CiwsDirector>();
            dir.mounts = mounts.ToArray();
        }

        static void AddMainGun(GameObject root, MeshCollider hullCol)
        {
            // the bow: the frontmost point of the hull along the centreline
            float bestZ = float.MinValue, bestY = 0f;
            for (float y = -16f; y <= 6f; y += 1f)
            {
                var from = root.transform.TransformPoint(new Vector3(0f, y, 300f));
                if (hullCol != null && hullCol.Raycast(new Ray(from, -root.transform.forward), out var hit, 600f))
                {
                    float z = root.transform.InverseTransformPoint(hit.point).z;
                    if (z > bestZ) { bestZ = z; bestY = y; }
                }
            }
            if (bestZ == float.MinValue) { bestZ = 88f; bestY = -6f; Log("main gun: bow not found, using a default spot"); }

            var node = Node("MainGun", root.transform, new Vector3(0f, bestY, bestZ - 1.0f));
            var model = Place(OwnModel("BeamEmitter"), node.transform, "Model");
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var housing = model.GetComponentsInChildren<Renderer>().First(r => r.name == "BE_Housing");
            var lens = model.GetComponentsInChildren<Renderer>().First(r => r.name == "BE_Lens");
            housing.sharedMaterial = Mats["Hull_Dark"];
            lens.sharedMaterial = Mats["Beam_Lens"];
            var muzzle = Node("Muzzle", node.transform, new Vector3(0f, 0f, 2.2f)).transform;

            var beam = node.AddComponent<MainBeam>();
            beam.muzzle = muzzle;
            beam.lens = lens;
            beam.chargeTime = fs.beamCharge;
            beam.fireDuration = 2.5f;
            beam.damagePerSecond = fs.beamDamage;
            beam.range = 6000f;
            beam.radius = 4f;
            beam.aimAssist = 6f;
            beam.streamSpeed = 3200f;
            beam.chargeSound = Sfx.HeavyBeamCharge;   // deeper than the cruiser's beam
            beam.fireSound = Sfx.HeavyBeamFire;
            BeamEffects(beam, node.transform, muzzle, Color.white, 1f);
            Log($"main gun at the bow: {node.transform.localPosition:F1}");
        }

        /// <summary>The beam's particle effects: charging sparks drawn into the emitter, the stream
        /// of white particles pouring out along the beam, a sleeve of sparks round it, and a
        /// splash where it lands; plus the emitter's light.</summary>
        static void BeamEffects(MainBeam beam, Transform node, Transform muzzle, Color tint, float scale)
        {
            var mat = Mats["FX_BeamParticle"];
            Color soft = Color.Lerp(tint, Color.white, 0.35f);

            // charge: sparks drift in from a 25 m shell round the emitter (they follow the ship)
            var charge = Ps("ChargeParticles", muzzle, mat, 2500, false);
            {
                var m = charge.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
                m.startSpeed = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.6f * scale, 1.8f * scale);
                m.startColor = new ParticleSystem.MinMaxGradient(tint, soft);
                var sh = charge.shape;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 25f * scale;
                sh.radiusThickness = 0.1f;
                var v = charge.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.Local;
                v.radial = new ParticleSystem.MinMaxCurve(-22f * scale);
                FadeInOut(charge, 0.25f);
                var sz = charge.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
                var em = charge.emission;
                em.rateOverTime = 40f;
            }

            // stream: fast white particles pouring out of the emitter, drawn as short streaks
            var stream = Ps("StreamParticles", muzzle, mat, 12000, true);
            {
                var m = stream.main;
                m.startLifetime = 1f;   // set every frame from the beam's length
                m.startSpeed = beam.streamSpeed;
                m.startSize = new ParticleSystem.MinMaxCurve(1.8f * scale, 5.5f * scale);
                m.startColor = new ParticleSystem.MinMaxGradient(tint, soft);
                var sh = stream.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 0.35f;
                sh.radius = 2.4f * scale;
                var col = stream.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 1f, 0.85f, 0.5f }, new[] { 0f, 0.03f, 0.7f, 1f });
                var em = stream.emission;
                em.rateOverTime = 2400f * scale;
                var r = stream.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.006f;
                r.lengthScale = 1.5f;
            }

            // sparkle: a sleeve of slower sparks all along the beam (box shape stretched by MainBeam)
            var sparkle = Ps("SparkleParticles", node, mat, 4000, true);
            {
                var m = sparkle.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 18f * scale);
                m.startSize = new ParticleSystem.MinMaxCurve(0.4f * scale, 1.4f * scale);
                m.startColor = new ParticleSystem.MinMaxGradient(tint, soft);
                var sh = sparkle.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(4f, 4f, 100f);
                sh.randomDirectionAmount = 1f;
                FadeInOut(sparkle, 0.2f);
                var em = sparkle.emission;
                em.rateOverTime = 1100f * scale;
            }

            // impact: a splash of particles where the beam lands
            var impact = Ps("ImpactParticles", node, mat, 2000, true);
            {
                var m = impact.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(40f * scale, 160f * scale);
                m.startSize = new ParticleSystem.MinMaxCurve(1.5f * scale, 4f * scale);
                m.startColor = new ParticleSystem.MinMaxGradient(tint, soft);
                var sh = impact.shape;
                sh.shapeType = ParticleSystemShapeType.Hemisphere;
                sh.radius = 3f * scale;
                FadeInOut(impact, 0.05f);
                var em = impact.emission;
                em.rateOverTime = 700f * scale;
            }

            var light = Node("Glow", muzzle, new Vector3(0f, 0f, 6f)).AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(tint, new Color(0.8f, 0.9f, 1f), 0.3f);
            light.range = 160f * scale;
            light.intensity = 0f;

            beam.chargeParticles = charge;
            beam.streamParticles = stream;
            beam.sparkleParticles = sparkle;
            beam.impactParticles = impact;
            beam.glow = light;
            beam.glowIntensity = 7000f * scale;
        }

        static ParticleSystem Ps(string name, Transform parent, Material mat, int max, bool world)
        {
            var go = Node(name, parent);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.playOnAwake = false;
            m.loop = true;
            m.duration = 5f;
            m.maxParticles = max;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 2f;
            return ps;
        }

        static void FadeInOut(ParticleSystem ps, float inFrac)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(new[] { 0f, 1f, 1f, 0f }, new[] { 0f, inFrac, 0.6f, 1f });
        }

        static ParticleSystem.MinMaxGradient Fade(float[] alphas, float[] times)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      alphas.Select((a, i) => new GradientAlphaKey(a, times[i])).ToArray());
            return new ParticleSystem.MinMaxGradient(g);
        }

        static void AddShield(GameObject root, Bounds hullBounds)
        {
            var src = OwnModel("ShieldHex");
            var mesh = src != null ? src.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) { Log("shield mesh missing (Models/ShieldHex.fbx)"); return; }
            var go = Node("Shield", root.transform);
            go.transform.localScale = ShieldScale(hullBounds);   // round the hull, turrets, flares and bow gun
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mats["Shield_Bubble"];
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var shield = go.AddComponent<Shield>();
            shield.bubble = mr;
            shield.capacity = fs.shield;
            shield.rechargeRate = fs.shieldRecharge;
            Log($"shield bubble: {mesh.vertexCount} vertices, scale {go.transform.localScale}");
        }

        // ------------------------------------------------------------------ review shots
        static void FrigateSystemShots(CarrierController ship, Camera cam)
        {
            BreakupShots(cam);
            var c = ship.transform;
            var go = cam.gameObject;

            // CIWS clusters beside the conning tower
            go.transform.position = c.TransformPoint(new Vector3(-45f, 30f, 10f));
            go.transform.LookAt(c.TransformPoint(new Vector3(-6f, 4f, -18f)));
            Render(cam, "f_ciws");
            go.transform.position = c.TransformPoint(new Vector3(0f, 70f, 40f));
            go.transform.LookAt(c.TransformPoint(new Vector3(0f, 0f, -18f)));
            Render(cam, "f_ciws_top");

            // the bow gun, charging and firing (particles simulated in the editor)
            var beam = ship.GetComponentInChildren<MainBeam>();
            if (beam != null)
            {
                go.transform.position = beam.transform.position + c.TransformDirection(new Vector3(30f, 12f, 30f));
                go.transform.LookAt(beam.transform.position);
                Render(cam, "f_main_gun");

                beam.chargeParticles.Simulate(1.2f, true, true);
                var em = beam.chargeParticles.emission; em.rateOverTime = 500f;
                beam.chargeParticles.Simulate(1.2f, true, true);
                Render(cam, "f_main_gun_charging");
                em.rateOverTime = 40f;
                beam.chargeParticles.Clear(true);

                var main = beam.streamParticles.main;
                main.startLifetime = 0.6f;
                beam.streamParticles.transform.rotation = Quaternion.LookRotation(c.forward);
                beam.streamParticles.Simulate(0.6f, true, true);
                var sh = beam.sparkleParticles.shape;
                beam.sparkleParticles.transform.SetPositionAndRotation(beam.muzzle.position + c.forward * 960f, Quaternion.LookRotation(c.forward));
                sh.scale = new Vector3(5f, 5f, 1920f);
                beam.sparkleParticles.Simulate(0.8f, true, true);
                go.transform.position = c.TransformPoint(new Vector3(90f, 35f, 60f));
                go.transform.LookAt(c.TransformPoint(new Vector3(0f, 0f, 400f)));
                Render(cam, "f_beam_firing");
                go.transform.position = c.TransformPoint(new Vector3(30f, 20f, -80f));
                go.transform.LookAt(c.TransformPoint(new Vector3(0f, -6f, 300f)));
                Render(cam, "f_beam_firing_behind");
                beam.streamParticles.Clear(true);
                beam.sparkleParticles.Clear(true);
            }

            // the shield with a few hits on it
            var shield = ship.GetComponentInChildren<Shield>();
            if (shield != null)
            {
                var r = shield.GetComponent<Renderer>();
                var block = new MaterialPropertyBlock();
                var hits = new Vector4[8];
                var strength = new Vector4[8];
                for (int i = 0; i < 8; i++) hits[i] = new Vector4(0, 0, 0, -1);
                Vector3 Surface(Vector3 dir) => shield.transform.TransformPoint(dir.normalized);
                hits[0] = Surface(new Vector3(1f, 0.3f, 0.2f)); hits[0].w = 0.08f; strength[0] = new Vector4(1.0f, 0.8f);
                hits[1] = Surface(new Vector3(0.9f, -0.1f, -0.5f)); hits[1].w = 0.45f; strength[1] = new Vector4(1.6f, 1.6f);
                hits[2] = Surface(new Vector3(0.7f, 0.6f, 0.6f)); hits[2].w = 0.2f; strength[2] = new Vector4(0.6f, 0.45f);
                block.SetVectorArray("_Hits", hits);
                block.SetVectorArray("_HitStrength", strength);
                block.SetFloat("_Flash", 0.25f);
                block.SetFloat("_Idle", 0.06f);
                r.SetPropertyBlock(block);
                go.transform.position = c.TransformPoint(new Vector3(260f, 60f, 30f));
                go.transform.LookAt(c.position);
                Render(cam, "f_shield_hits");
                for (int i = 0; i < 8; i++) hits[i].w = -1f;
                block.SetVectorArray("_Hits", hits);
                block.SetFloat("_Flash", 0f);
                r.SetPropertyBlock(block);
                Render(cam, "f_shield_idle");
                r.SetPropertyBlock(null);
            }
        }
    }
}
