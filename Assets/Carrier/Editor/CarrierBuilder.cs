using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Builds the carrier from the Blender-made parts (Assets/Carrier/Models) and the kit parts
    /// copied from the Unreal export (Assets/Carrier/Kit): materials, prefabs, the assembled
    /// Carrier prefab, a showcase scene, and review screenshots in Temp/carrier_shots.
    /// Tools > Carrier > Build Everything (or -executeMethod Carrier.EditorTools.CarrierBuilder.BuildAll).
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string Root = "Assets/Carrier";
        const string MatDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string ScenePath = Root + "/Scenes/CarrierShowcase.unity";
        const string ShotDir = "Temp/carrier_shots";

        static readonly StringBuilder Report = new StringBuilder();
        static void Log(string s) { Report.AppendLine(s); Debug.Log("[CarrierBuilder] " + s); }

        // ------------------------------------------------------------------ data files
        [Serializable] class V { public float[] pos; }
        [Serializable] class Bay { public string name; public float[] mouth, launch_dir, floor_center, floor_size; public float height; public string side; }
        [Serializable] class Hangar { public float[] opening_center, ceiling_center, size, launch_dir; public float height; public V[] launch_points; }
        [Serializable] class Engine { public float[] exit, dir; public float radius; }
        [Serializable] class Aux { public float[] pos, dir; public bool mirror; }
        [Serializable] class Antenna { public float[] pos; public string kind; }
        [Serializable] class Cell { public float[] pos, dir; }
        [Serializable] class MainT { public float[] pos, facing; }
        [Serializable] class PdT { public float[] pos, up, facing; }
        [Serializable] class TurretModel { public float yaw_pivot_height; public float[] pitch_pivot; public float barrel_length; public V[] muzzles; }
        [Serializable] class HP
        {
            public Bay[] flank_bays; public Hangar ventral_hangar; public Engine[] engines; public Aux[] aux_thrusters;
            public Antenna[] antennas; public Cell[] vls_cells; public MainT[] main_turrets; public PdT[] pd_turrets;
            public TurretModel turret_model;
        }
        // frigate (converted from ai models/starting frigate.glb by Tools/Blender/convert_frigate.py)
        [Serializable] class FGun { public string name; public float[] pos, outward, muzzle; }
        [Serializable] class FHP { public float length; public FGun[] guns; public Engine[] engines; public float[] bounds_size; }
        [Serializable] class KitMat { public string slot, diffuse, normal, emissive, blend; }
        [Serializable] class KitModel { public string key, path; public KitMat[] materials; }
        [Serializable] class KitTex { public string name, path; }
        [Serializable] class KitData { public KitModel[] models; public KitTex[] textures; }

        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);

        static KitData kit;
        static HP hp;
        static FHP fhp;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        // Orientation fixes for kit models (Unreal-exported FBX), applied to the model inside its prefab.
        // Filled in from the review screenshots; identity until a part is known to be turned.
        static readonly Dictionary<string, Vector3> KitRotation = new Dictionary<string, Vector3>
        {
            { "Mega_City/SM_ship_flying", new Vector3(0, -90, 0) },
            { "CargoShip/SM_SpaceShip_Outer_Body2", new Vector3(0, 0, 0) },
            { "Spaceship_Kit/SM_turret", new Vector3(0, 0, 0) },
        };

        [MenuItem("Tools/Carrier/Build Everything")]
        public static void BuildAll()
        {
            Report.Clear();
            Log("BUILD_START " + DateTime.Now);
            try
            {
                LoadData();
                ConfigureTextures();
                ConfigureModels();
                BuildMaterials();
                RemapModelMaterials();
                var p = BuildPrefabs();
                BuildCarrierPrefab(p);                 // kept as a prefab; the scene now uses the frigate
                var frigate = BuildFrigatePrefab(p);
                BuildScene(p, frigate);
                TakeScreenshots();
                BuildAsteroidScene(p, frigate);     // the second level: the asteroid-field ambush
                Log("BUILD_OK");
            }
            catch (Exception e)
            {
                Log("BUILD_FAILED " + e);
            }
            finally
            {
                Directory.CreateDirectory("Temp");
                File.WriteAllText("Temp/carrier_build.log", Report.ToString());
            }
        }

        static void LoadData()
        {
            hp = JsonUtility.FromJson<HP>(File.ReadAllText(Root + "/Data/carrier_hardpoints.json"));
            kit = JsonUtility.FromJson<KitData>(File.ReadAllText(Root + "/Data/kit_materials.json"));
            fhp = JsonUtility.FromJson<FHP>(File.ReadAllText(Root + "/Data/frigate_hardpoints.json"));
            Log($"frigate: {fhp.length} m, {fhp.guns.Length} side guns, {fhp.engines.Length} engines");
            Log($"data: {hp.main_turrets.Length} main turrets, {hp.pd_turrets.Length} PD, {hp.flank_bays.Length} bays, " +
                $"{hp.vls_cells.Length} VLS cells, {kit.models.Length} kit models");
        }

        // ------------------------------------------------------------------ importers
        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Kit" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool dirty = false;
                if (name.EndsWith("_N") && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (name == "TC_Space")
                {
                    if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
                    if (ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; dirty = true; }
                    if (ti.maxTextureSize != 4096) { ti.maxTextureSize = 4096; dirty = true; }
                }
                else if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        static IEnumerable<string> AllModelPaths()
        {
            foreach (var m in kit.models) yield return m.path;
            foreach (var n in new[] { "Carrier_Hull", "TripleTurret", "Missile", "Drone", "Frigate_Hull", "BallTurret", "CIWS", "BeamEmitter", "ShieldHex" })
                yield return $"{Root}/Models/{n}.fbx";
        }

        static void ConfigureModels()
        {
            foreach (var path in AllModelPaths())
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { Log("missing model " + path); continue; }
                bool dirty = false;
                if (!mi.bakeAxisConversion) { mi.bakeAxisConversion = true; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
                { mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ materials
        static T Load<T>(string path) where T : Object => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        static Texture2D KitTexture(string name) => Load<Texture2D>(kit.textures.FirstOrDefault(t => t.name == name)?.path);

        static Material SaveMat(string name, Material m)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.renderQueue = m.renderQueue;
                existing.SetOverrideTag("RenderType", m.GetTag("RenderType", false));
                EditorUtility.SetDirty(existing);
                Mats[name] = existing;
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            Mats[name] = m;
            return m;
        }

        static Material Lit(string name, Color color, Texture2D baseMap = null, Texture2D normal = null, float metallic = 0.4f,
            float smooth = 0.45f, Color? emission = null, Texture2D emissionMap = null, float tiling = 1f, bool twoSided = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            if (baseMap) m.SetTexture("_BaseMap", baseMap);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionMap) m.SetTexture("_EmissionMap", emissionMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (twoSided) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
            return SaveMat(name, m);
        }

        static Material Additive(string name, Color hdr, Texture2D tex = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", hdr);
            if (tex) m.SetTexture("_BaseMap", tex);
            return SaveMat(name, m);
        }

        static void BuildMaterials()
        {
            var panelD = KitTexture("T_Metal_pannel_D.png");
            var panelN = KitTexture("T_Metal_pannel_N.png");
            var trimD = KitTexture("T_spaceship_trim_D.png");
            var trimN = KitTexture("T_spaceship_trim_N.png");
            var fx = KitTexture("T_Reactor_FX.png");
            var floorD = Load<Texture2D>(Root + "/Kit/Spaceship_Kit/Textures/T_space_ship/T_floor_spaceship/T_space_ship_floor_D.png");
            var floorN = Load<Texture2D>(Root + "/Kit/Spaceship_Kit/Textures/T_space_ship/T_floor_spaceship/T_space_ship_floor_N.png");
            var bayD = Load<Texture2D>(Root + "/Kit/MC_Environment/Textures/T_Metal_03_BC.png");
            var bayN = Load<Texture2D>(Root + "/Kit/MC_Environment/Textures/T_Metal_03_N.png");

            Lit("Hull_Main", new Color(0.80f, 0.82f, 0.85f), panelD, panelN, 0.55f, 0.5f);
            Lit("Hull_Dark", new Color(0.32f, 0.34f, 0.38f), panelD, panelN, 0.55f, 0.45f);
            Lit("Hull_Accent", new Color(0.62f, 0.10f, 0.08f), null, panelN, 0.3f, 0.55f);
            Lit("Deck", new Color(0.55f, 0.57f, 0.60f), floorD, floorN, 0.5f, 0.4f, tiling: 3f);
            Lit("Window_Emissive", new Color(0.05f, 0.08f, 0.1f), metallic: 0.2f, smooth: 0.9f, emission: new Color(0.3f, 0.75f, 1f) * 1.6f);
            Lit("Engine_Glow", new Color(0.3f, 0.5f, 0.8f), metallic: 0f, smooth: 0.2f, emission: new Color(0.55f, 0.75f, 1f) * 14f);
            Lit("Bay_Interior", new Color(0.58f, 0.60f, 0.64f), panelD, panelN, 0.35f, 0.35f, tiling: 2f);
            Lit("Hatch", new Color(0.48f, 0.50f, 0.53f), trimD, trimN, 0.5f, 0.45f, tiling: 4f);
            Lit("Barrel", new Color(0.13f, 0.13f, 0.15f), metallic: 0.85f, smooth: 0.6f);
            Lit("Light_Strip", new Color(0.9f, 0.9f, 0.8f), metallic: 0f, smooth: 0.5f, emission: new Color(1f, 0.92f, 0.7f) * 6f);
            Lit("Kit_Fallback", new Color(0.70f, 0.72f, 0.75f), panelD, panelN, 0.5f, 0.45f, tiling: 4f, twoSided: true);
            Lit("Enemy_Hull", new Color(0.35f, 0.12f, 0.12f), panelD, panelN, 0.5f, 0.45f, tiling: 4f, twoSided: true);

            Additive("FX_EnginePlume", new Color(0.45f, 0.65f, 1f) * 2.5f, fx);
            Additive("FX_BeamBolt", new Color(0.4f, 0.9f, 1f) * 8f);
            Additive("FX_PDBolt", new Color(1f, 0.55f, 0.2f) * 6f);
            Additive("FX_FighterBolt", new Color(1f, 0.3f, 0.25f) * 6f);
            Additive("FX_Explosion", new Color(1f, 0.55f, 0.2f) * 6f);
            Additive("FX_MissileTrail", new Color(1f, 0.8f, 0.5f) * 3f);
            FrigateSystemMaterials();

            // kit materials: one per model slot, from the textures the Unreal export recorded
            foreach (var m in kit.models)
            foreach (var s in m.materials)
            {
                string key = KitMatName(m.key, s.slot);
                if (s.blend == "translucent" || s.blend == "additive")
                {
                    Additive(key, new Color(0.45f, 0.65f, 1f) * 2f, fx);
                    continue;
                }
                if (s.slot.Contains("emission"))
                {
                    Mats[key] = Mats["Engine_Glow"];
                    continue;
                }
                var d = Load<Texture2D>(s.diffuse);
                var n = Load<Texture2D>(s.normal);
                var e = Load<Texture2D>(s.emissive);
                if (d == null && n == null)
                {
                    Mats[key] = Mats["Kit_Fallback"];
                    continue;
                }
                Lit(key, Color.white, d, n, 0.4f, 0.45f, e != null ? Color.white * 2f : (Color?)null, e);
            }
            AssetDatabase.SaveAssets();
        }

        static string KitMatName(string modelKey, string slot) =>
            "Kit_" + modelKey.Split('/')[0] + "_" + slot.Replace(" ", "_");

        static void RemapModelMaterials()
        {
            foreach (var path in AllModelPaths())
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                var km = kit.models.FirstOrDefault(k => k.path == path);
                var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).Distinct().ToList();
                // also include names already remapped (they no longer appear as sub-assets)
                foreach (var kv in mi.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
                var used = new List<string>();
                foreach (var name in names)
                {
                    Material target = null;
                    if (km == null) Mats.TryGetValue(name, out target);
                    else
                    {
                        string lower = name.ToLowerInvariant();
                        var slot = km.materials.FirstOrDefault(s => s.slot == lower)
                                   ?? km.materials.FirstOrDefault(s => s.slot == "mi_" + lower || "mi_" + s.slot == lower)
                                   ?? km.materials.FirstOrDefault(s => lower.Contains(s.slot) || s.slot.Contains(lower));
                        if (slot != null) Mats.TryGetValue(KitMatName(km.key, slot.slot), out target);
                        if (target == null && lower.Contains("emiss")) target = Mats["Engine_Glow"];
                    }
                    if (target == null) target = Mats["Kit_Fallback"];
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), target);
                    used.Add($"{name}->{target.name}");
                }
                mi.SaveAndReimport();
                Log($"materials {Path.GetFileName(path)}: {string.Join(", ", used)}");
            }
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Model(string key) => Load<GameObject>(kit.models.First(m => m.key == key).path);
        static GameObject OwnModel(string name) => Load<GameObject>($"{Root}/Models/{name}.fbx");

        static GameObject Place(Object prefabOrModel, Transform parent, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabOrModel);
            if (parent) go.transform.SetParent(parent, false);
            if (name != null) go.name = name;
            return go;
        }

        static GameObject Node(string name, Transform parent, Vector3 pos = default, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            return go;
        }

        /// <summary>Bounds of the meshes under `subject`, measured in `space`'s local frame.</summary>
        static Bounds BoundsIn(Transform space, Transform subject)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in subject.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Bounds of all meshes under `t`, in `t`'s own local space.</summary>
        static Bounds LocalBounds(Transform t) => BoundsIn(t, t);

        /// <summary>Bounds of a child measured in its parent's space (after its own rotation/scale).</summary>
        static Bounds ParentBounds(Transform child) => BoundsIn(child.parent, child);

        /// <summary>Move a child so its bounds centre (in the parent's space) lands on `target`.</summary>
        static void CenterAt(Transform child, Vector3 target)
        {
            var b = ParentBounds(child);
            child.localPosition += target - b.center;
        }

        /// <summary>Rotate `model` so its longest mesh axis points along `dir` in its parent's space
        /// (the end farther from the pivot points that way). Returns the axis index and length.</summary>
        static (int axis, float length) AlignLongAxis(Transform model, Vector3 dir)
        {
            model.localRotation = Quaternion.identity;
            var b = BoundsIn(model, model);
            var s = Vector3.Scale(b.size, Vector3.one);
            int a = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            Vector3 axis = Vector3.zero;
            axis[a] = Mathf.Abs(b.center[a]) < 0.01f * b.size[a] ? 1f : Mathf.Sign(b.center[a]);
            model.localRotation = Quaternion.FromToRotation(axis, dir.normalized);
            return (a, b.size[a]);
        }

        /// <summary>Scale a model to `length` along its long axis (index from AlignLongAxis) and by
        /// `girth` across it.</summary>
        static void Stretch(Transform model, int axis, float currentLength, float length, float girth)
        {
            var sc = Vector3.one * girth;
            sc[axis] = length / Mathf.Max(0.01f, currentLength);
            model.localScale = sc;
        }

        static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static T SavePrefab<T>(GameObject go, string name) where T : Component
        {
            Directory.CreateDirectory(PrefabDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return saved.GetComponent<T>();
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            Directory.CreateDirectory(PrefabDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return saved;
        }

        // ------------------------------------------------------------------ prefabs
        class Prefabs
        {
            public Projectile beam, pdBolt, fighterBolt, frigateBolt, ciwsBolt, enemyCannon, enemyBlaster;
            public Explosion explosion, explosionSmall;
            public Missile missile;
            public CarrierTurret mainTurret, pdTurret;
            public Craft fighter, drone, dropship;
            public GameObject enemyCruiser, enemyDrone, enemyFighter;
        }

        static Projectile MakeBolt(string name, string mat, Vector3 size, float speed, float damage, Explosion hit)
        {
            var go = new GameObject(name);
            var p = go.AddComponent<Projectile>();
            p.speed = speed;
            p.damage = damage;
            p.hitEffect = hit;
            var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            StripColliders(vis);
            vis.name = "Bolt";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localRotation = Quaternion.Euler(90, 0, 0);
            vis.transform.localScale = new Vector3(size.x, size.z * 0.5f, size.y);
            vis.transform.localPosition = new Vector3(0, 0, -size.z * 0.5f);
            var r = vis.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mats[mat];
            r.shadowCastingMode = ShadowCastingMode.Off;
            return SavePrefab<Projectile>(go, name);
        }

        static Explosion MakeExplosion(string name, float start, float end, float duration, float lightIntensity)
        {
            var go = new GameObject(name);
            var e = go.AddComponent<Explosion>();
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripColliders(s);
            s.name = "Flash";
            s.transform.SetParent(go.transform, false);
            var r = s.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mats["FX_Explosion"];
            r.shadowCastingMode = ShadowCastingMode.Off;
            var l = Node("Light", go.transform).AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.3f);
            l.intensity = lightIntensity;
            l.range = end * 3f;
            e.flash = r;
            e.glow = l;
            e.startSize = start;
            e.endSize = end;
            e.duration = duration;
            return SavePrefab<Explosion>(go, name);
        }

        static Prefabs BuildPrefabs()
        {
            var p = new Prefabs();
            p.explosion = MakeExplosion("Explosion", 6f, 90f, 1.6f, 40000f);
            p.explosionSmall = MakeExplosion("ExplosionSmall", 2f, 22f, 0.7f, 3000f);
            p.beam = MakeBolt("BeamBolt", "FX_BeamBolt", new Vector3(1.4f, 1.4f, 24f), 2400f, 60f, p.explosionSmall);
            p.pdBolt = MakeBolt("PDBolt", "FX_PDBolt", new Vector3(0.5f, 0.5f, 9f), 1800f, 10f, null);
            p.fighterBolt = MakeBolt("FighterBolt", "FX_FighterBolt", new Vector3(0.35f, 0.35f, 6f), 1600f, 6f, null);
            p.frigateBolt = MakeBolt("FrigateBolt", "FX_BeamBolt", new Vector3(1.0f, 1.0f, 16f), 2600f, 90f, p.explosionSmall);

            // missile
            {
                var go = new GameObject("Missile");
                Place(OwnModel("Missile"), go.transform, "Model");
                var m = go.AddComponent<Missile>();
                m.explosion = p.explosion;
                var trail = Node("Trail", go.transform, new Vector3(0, 0, -4.6f)).AddComponent<TrailRenderer>();
                trail.time = 1.4f;
                trail.startWidth = 1.6f;
                trail.endWidth = 0f;
                trail.sharedMaterial = Mats["FX_MissileTrail"];
                trail.shadowCastingMode = ShadowCastingMode.Off;
                p.missile = SavePrefab<Missile>(go, "Missile");
            }

            // main triple turret: model hierarchy Turret_Yaw > Turret_Pitch > barrels
            {
                // the FBX holds flat meshes (base, house, barrels) in turret space; build the joints here
                var go = new GameObject("TripleTurret");
                var model = Place(OwnModel("TripleTurret"), go.transform, "Model");
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var house = model.GetComponentsInChildren<Transform>().First(t => t.name == "Turret_House");
                var barrels = model.GetComponentsInChildren<Transform>().First(t => t.name == "Turret_Barrels");
                var pivot = V3(hp.turret_model.pitch_pivot);
                var yaw = Node("Yaw", go.transform, new Vector3(0, hp.turret_model.yaw_pivot_height, 0)).transform;
                var pitch = Node("Pitch", yaw, pivot - yaw.localPosition).transform;
                house.SetParent(yaw, true);
                barrels.SetParent(pitch, true);
                var tb = BoundsIn(go.transform, barrels);
                Log($"turret barrels bounds {tb.center:F1} size {tb.size:F1} (expect forward along +Z, ~35 long, at y~8)");
                var muzzles = hp.turret_model.muzzles.Select((m, i) =>
                {
                    var t = Node("Muzzle" + i, pitch).transform;
                    t.position = go.transform.TransformPoint(V3(m.pos));
                    t.rotation = pitch.rotation;
                    return t;
                }).ToArray();
                var tu = go.AddComponent<CarrierTurret>();
                tu.yaw = yaw;
                tu.pitch = pitch;
                tu.muzzles = muzzles;
                tu.projectilePrefab = p.beam;
                tu.yawSpeed = 18f;
                tu.pitchSpeed = 12f;
                tu.fireInterval = 3.5f;
                tu.range = 7000f;
                tu.fireCone = 2.5f;
                tu.yawLimit = 160f;
                Log($"turret pivot check: pitch at {pitch.position:F1} expected {pivot:F1}; yaw at {yaw.position:F1}");
                p.mainTurret = SavePrefab<CarrierTurret>(go, "TripleTurret");
            }

            // point-defence turret: kit twin turret, scaled up, turning as a whole
            {
                var go = new GameObject("PDTurret");
                var yaw = Node("Yaw", go.transform).transform;
                var model = Place(Model("Spaceship_Kit/SM_turret"), yaw, "Model");
                model.transform.localScale = Vector3.one * 2.2f;
                model.transform.localRotation = Quaternion.Euler(KitRotation["Spaceship_Kit/SM_turret"]);
                var b = LocalBounds(yaw);
                model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                b = LocalBounds(yaw);
                var muzzle = Node("Muzzle", yaw, new Vector3(0, b.center.y, b.max.z)).transform;
                var tu = go.AddComponent<CarrierTurret>();
                tu.yaw = yaw;
                tu.muzzles = new[] { muzzle };
                tu.projectilePrefab = p.pdBolt;
                tu.fireSound = Sfx.PointDefense;
                tu.yawSpeed = 90f;
                tu.fireInterval = 0.35f;
                tu.range = 3000f;
                tu.fireCone = 5f;
                tu.yawLimit = 175f;
                Log($"PD turret size {b.size:F1}");
                p.pdTurret = SavePrefab<CarrierTurret>(go, "PDTurret");
            }

            p.fighter = MakeCraft("Fighter", Model("Mega_City/SM_ship_flying"), KitRotation["Mega_City/SM_ship_flying"], 1f,
                Craft.Kind.Fighter, 200f, 290f, 85f, 1000f, p.fighterBolt, 0.25f, 60f, p.explosionSmall);
            p.drone = MakeCraft("Drone", OwnModel("Drone"), Vector3.zero, 1.6f,
                Craft.Kind.Drone, 170f, 240f, 120f, 700f, p.fighterBolt, 0.5f, 30f, p.explosionSmall);
            p.dropship = MakeCraft("Dropship", Model("CargoShip/SM_SpaceShip_Outer_Body2"), KitRotation["CargoShip/SM_SpaceShip_Outer_Body2"], 1f,
                Craft.Kind.Dropship, 90f, 90f, 30f, 1400f, null, 1f, 400f, p.explosion);
            p.dropship.launchTime = 7f;
            EditorUtility.SetDirty(p.dropship);

            // demo enemies, armed so the frigate's shield has something to stop
            MakeSystemBolts(p);
            p.enemyCruiser = MakeEnemy("EnemyCruiser", Model("CargoShip/SM_SpaceShip_Outer_Body2"),
                KitRotation["CargoShip/SM_SpaceShip_Outer_Body2"], 4f, 3000f, p.explosion, 2.5f,
                "Hostile Cruiser", "Heavy cruiser", "Medium", TargetInfo.Kind.Capital, "Cannons, missiles, beam",
                go => ArmCruiser(go, p));
            p.enemyDrone = MakeEnemy("EnemyDrone", OwnModel("Drone"), Vector3.zero, 4f, 300f, p.explosion, 1f,
                "Hostile Drone", "Recon drone", "Low", TargetInfo.Kind.Drone, "Blasters",
                go => ArmDrone(go, p));
            p.enemyFighter = MakeEnemy("EnemyFighter", Model("Mega_City/SM_ship_flying"), KitRotation["Mega_City/SM_ship_flying"], 1.5f, 260f,
                p.explosion, 0.55f, "Hostile Fighter", "Strike fighter", "Medium", TargetInfo.Kind.Fighter, "Blaster cannons",
                go => ArmFighter(go, p));
            AssetDatabase.SaveAssets();
            return p;
        }

        static Craft MakeCraft(string name, GameObject model, Vector3 rot, float scale, Craft.Kind kind, float cruise, float attack,
            float turn, float escort, Projectile gun, float fireInterval, float health, Explosion death)
        {
            var go = new GameObject(name);
            var m = Place(model, go.transform, "Model");
            m.transform.localRotation = Quaternion.Euler(rot);
            m.transform.localScale = Vector3.one * scale;
            var b = LocalBounds(go.transform);
            m.transform.localPosition = -b.center;
            b = LocalBounds(go.transform);
            var col = go.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = b.size;
            var c = go.AddComponent<Craft>();
            c.kind = kind;
            c.cruiseSpeed = cruise;
            c.attackSpeed = attack;
            c.turnRate = turn;
            c.escortRadius = escort;
            c.gunPrefab = gun;
            c.fireInterval = fireInterval;
            c.guns = new[] { Node("Gun", go.transform, new Vector3(0, 0, b.max.z + 1f)).transform };
            var h = go.AddComponent<Health>();
            h.maxHealth = health;
            h.deathEffect = death;
            Log($"{name}: size {b.size:F1}");
            return SavePrefab<Craft>(go, name);
        }

        static GameObject MakeEnemy(string name, GameObject model, Vector3 rot, float scale, float health, Explosion death, float deathScale,
            string displayName, string shipClass, string threat, TargetInfo.Kind kind, string weapons, Action<GameObject> arm)
        {
            var go = new GameObject(name);
            var m = Place(model, go.transform, "Model");
            m.transform.localRotation = Quaternion.Euler(rot);
            m.transform.localScale = Vector3.one * scale;
            foreach (var r in m.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(_ => Mats["Enemy_Hull"]).ToArray();
            var b = LocalBounds(go.transform);
            m.transform.localPosition = -b.center;
            b = LocalBounds(go.transform);
            var col = go.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = b.size;
            // rebuilt as chunks so it can come apart when destroyed
            MakeBreakable(go, m, name, kind == TargetInfo.Kind.Capital ? 12 : 6, kind == TargetInfo.Kind.Capital);
            var h = go.AddComponent<Health>();
            h.maxHealth = health;
            h.deathEffect = death;
            h.deathEffectScale = deathScale;
            h.respawnDelay = 6f;
            var info = go.AddComponent<TargetInfo>();
            info.displayName = displayName;
            info.shipClass = shipClass;
            info.threat = threat;
            info.weapons = weapons;
            info.kind = kind;
            go.AddComponent<OrbitMover>();
            arm?.Invoke(go);
            return SavePrefab(go, name);
        }

        // ------------------------------------------------------------------ the carrier
        static GameObject BuildCarrierPrefab(Prefabs p)
        {
            var root = new GameObject("Carrier");
            var cc = root.AddComponent<CarrierController>();
            var hull = Place(OwnModel("Carrier_Hull"), root.transform, "Hull");
            var hb = LocalBounds(hull.transform);
            Log($"hull bounds center {hb.center:F1} size {hb.size:F1} (expect ~154 x 177 x 702, bow at +Z)");

            // main turrets
            var mains = Node("MainTurrets", root.transform).transform;
            cc.mainTurrets = hp.main_turrets.Select((t, i) =>
            {
                var go = Place(p.mainTurret.gameObject, mains, $"MainTurret_{i + 1}");
                go.transform.localPosition = V3(t.pos);
                go.transform.localRotation = Quaternion.LookRotation(V3(t.facing), Vector3.up);
                return go.GetComponent<CarrierTurret>();
            }).ToArray();

            // point defence
            var pds = Node("PointDefense", root.transform).transform;
            cc.pointDefense = hp.pd_turrets.Select((t, i) =>
            {
                var go = Place(p.pdTurret.gameObject, pds, $"PD_{i + 1}");
                Vector3 up = V3(t.up).normalized;
                Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                go.transform.localPosition = V3(t.pos);
                go.transform.localRotation = Quaternion.LookRotation(fwd, up);
                return go.GetComponent<CarrierTurret>();
            }).ToArray();

            // flank launch bays with hangar-kit interiors
            var bays = Node("FlankBays", root.transform).transform;
            var floorTile = Model("MC_Environment/SM_Hangar_floor_8x8");
            var pad = Model("Mega_City/SM_landing_pad_module");
            var rib = Model("MC_Environment/SM_Hangar_arch_a");
            cc.flankBays = hp.flank_bays.Select(b =>
            {
                Vector3 outDir = V3(b.launch_dir).normalized;
                var bayGo = Node(b.name, bays, V3(b.mouth), Quaternion.LookRotation(outDir, Vector3.up));
                var bay = bayGo.AddComponent<LaunchBay>();
                bay.mouth = bayGo.transform;
                bay.launchSpeed = 70f;
                bay.interval = 1.8f;
                Vector3 floor = V3(b.floor_center);
                var interior = Node("Interior", bays);
                float depth = b.floor_size[0], length = b.floor_size[1];
                // the bay's depth runs along carrier X (inward from the mouth), its length along Z
                float inward = -Mathf.Sign(outDir.x);
                for (int ix = 0; ix < 3; ix++)
                for (int iz = 0; iz < 4; iz++)
                {
                    var tile = Place(floorTile, interior.transform, "Floor");
                    var tb0 = ParentBounds(tile.transform);
                    tile.transform.localScale = new Vector3(depth / 3f / Mathf.Max(0.1f, tb0.size.x), 1f, length / 4f / Mathf.Max(0.1f, tb0.size.z));
                    var tb = ParentBounds(tile.transform);
                    CenterAt(tile.transform, floor + new Vector3((ix - 1) * depth / 3f, 0.05f - tb.extents.y, (iz - 1.5f) * length / 4f));
                }
                var padGo = Place(pad, interior.transform, "LaunchPad");
                var pb = ParentBounds(padGo.transform);
                CenterAt(padGo.transform, floor + new Vector3(inward * 4f, 0.3f - pb.extents.y, 0));
                for (int k = 0; k < 3; k++)
                {
                    var r = Place(rib, interior.transform, "CeilingRib");
                    AlignLongAxis(r.transform, Vector3.forward);
                    var rb = ParentBounds(r.transform);
                    CenterAt(r.transform, floor + new Vector3((k - 1) * 9f, b.height - 0.6f - rb.extents.y, 0));
                }
                var light = Node("BayLight", interior.transform, floor + Vector3.up * (b.height - 4f)).AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.93f, 0.8f);
                light.range = 70f;
                light.intensity = 900f;
                bay.launchPoints = new[] { -7f, 7f }.Select((dz, i) =>
                    Node("LaunchPoint" + i, bayGo.transform.parent,
                        floor + Vector3.up * 8f + Vector3.forward * dz - outDir * 4f,
                        Quaternion.LookRotation(outDir, Vector3.up)).transform).ToArray();
                return bay;
            }).ToArray();

            // ventral hangar
            {
                var h = hp.ventral_hangar;
                var hg = Node("VentralHangar", root.transform, V3(h.opening_center), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var bay = hg.AddComponent<LaunchBay>();
                bay.mouth = hg.transform;
                bay.launchSpeed = 25f;
                bay.interval = 4f;
                bay.launchPoints = h.launch_points.Select((lp, i) =>
                    Node("LaunchPoint" + i, root.transform.Find("VentralHangar"), Vector3.zero).transform).ToArray();
                for (int i = 0; i < bay.launchPoints.Length; i++)
                {
                    bay.launchPoints[i].position = root.transform.TransformPoint(V3(h.launch_points[i].pos));
                    bay.launchPoints[i].rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                }
                var beam = Model("MC_Environment/SM_Hangar_beam_b");
                Vector3 ceil = V3(h.ceiling_center);
                var beams = Node("HangarBeams", root.transform).transform;
                for (float z = -h.size[1] / 2f + 10f; z < h.size[1] / 2f; z += 20f)
                {
                    var bm = Place(beam, beams, "HangarBeam");
                    var (ax, len) = AlignLongAxis(bm.transform, Vector3.right);
                    Stretch(bm.transform, ax, len, h.size[0] - 2f, 4f);
                    var bb = ParentBounds(bm.transform);
                    CenterAt(bm.transform, ceil + new Vector3(0, -0.5f - bb.extents.y, z));
                }
                foreach (var z in new[] { -60f, -20f, 20f, 60f })
                {
                    var l = Node("HangarLight", hg.transform).AddComponent<Light>();
                    l.transform.position = root.transform.TransformPoint(ceil + new Vector3(0, -8f, z));
                    l.type = LightType.Point;
                    l.range = 130f;
                    l.intensity = 2500f;
                    l.color = new Color(1f, 0.95f, 0.85f);
                }
                cc.ventralHangar = bay;
            }

            // missile launch block
            {
                var ml = Node("MissileLauncher", root.transform).AddComponent<MissileLauncher>();
                ml.missilePrefab = p.missile;
                ml.cells = hp.vls_cells.Select((c, i) =>
                    Node("Cell" + i, ml.transform, V3(c.pos), Quaternion.LookRotation(Vector3.up, Vector3.forward)).transform).ToArray();
                cc.missiles = ml;
            }

            // engines: kit exhaust plume + light
            {
                var engines = Node("Engines", root.transform).transform;
                var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
                cc.engines = hp.engines.Select((e, i) =>
                {
                    var eg = Node("Engine" + (i + 1), engines, V3(e.exit), Quaternion.LookRotation(V3(e.dir), Vector3.up));
                    var wrap = Node("Plume", eg.transform).transform;
                    var pm = Place(plumeModel, wrap, "PlumeMesh");
                    var (ax, len) = AlignLongAxis(pm.transform, Vector3.forward);
                    var pb = ParentBounds(pm.transform);
                    float width = Mathf.Max(0.1f, Mathf.Max(pb.size.x, pb.size.y));
                    Stretch(pm.transform, ax, len, 90f, e.radius * 1.7f / width);
                    pb = ParentBounds(pm.transform);
                    pm.transform.localPosition += new Vector3(-pb.center.x, -pb.center.y, -4f - pb.min.z);
                    foreach (var r in pm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                    var l = Node("Glow", eg.transform, new Vector3(0, 0, 25f)).AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(0.55f, 0.72f, 1f);
                    l.range = 260f;
                    var et = eg.AddComponent<EngineThrust>();
                    et.plume = wrap;
                    et.glow = l;
                    et.maxIntensity = 2500f;
                    return et;
                }).ToArray();
            }

            // auxiliary thrusters (kit reactor nacelles) and antennas
            {
                var aux = Node("AuxThrusters", root.transform).transform;
                foreach (var a in hp.aux_thrusters)
                {
                    var m = Place(Model(a.mirror ? "Spaceship_Kit/SM_reactor_right" : "Spaceship_Kit/SM_reactor_left"), aux, "AuxThruster");
                    AlignLongAxis(m.transform, Vector3.forward);
                    m.transform.localScale = Vector3.one * 1.8f;
                    var b = ParentBounds(m.transform);
                    // sit on top of the upper engine shield / under the lower one
                    CenterAt(m.transform, V3(a.pos) + new Vector3(0, Mathf.Sign(a.pos[1]) * b.extents.y, 0));
                }
                var ants = Node("Antennas", root.transform).transform;
                foreach (var a in hp.antennas)
                {
                    var m = Place(Model("Sci-Fi_Flying_Cargo_Ship/" + a.kind), ants, a.kind);
                    AlignLongAxis(m.transform, Vector3.up);
                    m.transform.localScale = Vector3.one * 1.3f;
                    var b = ParentBounds(m.transform);
                    CenterAt(m.transform, V3(a.pos) + new Vector3(0, b.extents.y, 0));
                }
            }

            var saved = SavePrefab(root, "Carrier");
            Log("carrier prefab saved");
            return saved;
        }

        // ------------------------------------------------------------------ the frigate
        /// <summary>The player's frigate: the AI-made hull with a gun on each flank (no hangar,
        /// no missiles). The guns are part of the hull mesh, so each one is an invisible aiming
        /// joint at its pod that can only cover its own side: the ship has to turn to bring a
        /// broadside to bear. Quicker than the carrier, slower than fighters and drones.</summary>
        static GameObject BuildFrigatePrefab(Prefabs p)
        {
            string texPath = $"{Root}/Models/Frigate_BaseColor.png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && ti.maxTextureSize != 4096)
            {
                ti.maxTextureSize = 4096;
                ti.SaveAndReimport();
            }
            // two-sided, so the pieces show their inside where the hull breaks apart
            var hullMat = Lit("Frigate_Hull", Color.white, Load<Texture2D>(texPath), null, 0.25f, 0.35f, twoSided: true);

            var root = new GameObject("Frigate");
            var cc = root.AddComponent<CarrierController>();
            cc.shipName = "Frigate";
            cc.shipClass = "Gun frigate";
            cc.maxSpeed = 150f;        // carrier 80, drones cruise 170, fighters 200
            cc.acceleration = 30f;     // carrier 6, craft 120 (m/s per second)
            cc.turnRate = 20f;         // carrier 3, fighters 85, drones 120 (degrees per second)
            cc.pitchRate = 12f;        // nose up/down, degrees per second
            cc.maxPitch = 60f;
            cc.elevatorSpeed = 40f;    // elevator thrusters, m/s straight up or down
            cc.elevatorAcceleration = 15f;
            cc.fighterStock = cc.droneStock = cc.dropshipStock = 0;
            cc.flankBays = new LaunchBay[0];
            cc.pointDefense = new CarrierTurret[0];

            var hull = Place(OwnModel("Frigate_Hull"), root.transform, "Hull");
            foreach (var r in hull.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(_ => hullMat).ToArray();
            // the hull's shape, so the guns can tell when their line of fire would cross it
            foreach (var mf in hull.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            var hb = LocalBounds(hull.transform);
            Log($"frigate hull bounds center {hb.center:F1} size {hb.size:F1} (expect ~53 x 63 x 180, bow at +Z)");

            // side guns
            var gunsNode = Node("SideGuns", root.transform).transform;
            cc.mainTurrets = fhp.guns.Select(g =>
            {
                // a ball mount just outboard of the pod's outer face, resting straight out from the side
                Vector3 outward = V3(g.outward).normalized;
                Vector3 pivot = V3(g.muzzle) + outward * 1.5f;
                var go = Node("SideGun_" + g.name, gunsNode, pivot, Quaternion.LookRotation(outward, Vector3.up));
                var yaw = Node("Mount", go.transform).transform;
                // visible ball turret (Tools/Blender/build_ball_turret.py): the collar stays on the
                // pod, the ball and barrel swing with the mount; the ball's centre is the pivot
                var model = Place(OwnModel("BallTurret"), go.transform, "Model");
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Transform Part(string n) => model.GetComponentsInChildren<Transform>().First(t => t.name == n);
                var collar = Part("BT_Base");
                var ball = Part("BT_Ball");
                var barrel = Part("BT_Barrel");
                ball.SetParent(yaw, true);
                barrel.SetParent(yaw, true);
                collar.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Dark"];
                ball.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Main"];
                barrel.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Dark"];
                var bb = BoundsIn(go.transform, barrel);
                Log($"ball turret {g.name}: barrel bounds {bb.center:F1} size {bb.size:F1} (expect along +Z to ~10.6)");
                var muzzle = Node("Muzzle", yaw, new Vector3(0f, 0f, bb.max.z + 0.5f)).transform;
                var tu = go.AddComponent<CarrierTurret>();
                tu.label = g.name;
                tu.yaw = yaw;
                tu.muzzles = new[] { muzzle };
                tu.projectilePrefab = p.frigateBolt;
                tu.ballMount = true;
                tu.yawSpeed = 70f;
                // reaches 30 degrees past the centreline in any direction, so both guns can share a
                // target ahead, astern, above or below, wherever the line of fire clears the hull
                tu.coneLimit = 120f;
                tu.checkHullClearance = true;
                tu.fireInterval = 0.5f;
                tu.range = 4500f;
                tu.fireCone = 3f;
                tu.leadTarget = true;
                return tu;
            }).ToArray();

            // engines: the kit exhaust plume in each of the four stern nozzles
            {
                var engines = Node("Engines", root.transform).transform;
                var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
                cc.engines = fhp.engines.Select((e, i) =>
                {
                    var eg = Node("Engine" + (i + 1), engines, V3(e.exit), Quaternion.LookRotation(V3(e.dir), Vector3.up));
                    var wrap = Node("Plume", eg.transform).transform;
                    var pm = Place(plumeModel, wrap, "PlumeMesh");
                    var (ax, len) = AlignLongAxis(pm.transform, Vector3.forward);
                    var pb = ParentBounds(pm.transform);
                    float width = Mathf.Max(0.1f, Mathf.Max(pb.size.x, pb.size.y));
                    Stretch(pm.transform, ax, len, e.radius * 6f, e.radius * 1.5f / width);
                    pb = ParentBounds(pm.transform);
                    pm.transform.localPosition += new Vector3(-pb.center.x, -pb.center.y, -1f - pb.min.z);
                    foreach (var r in pm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                    var l = Node("Glow", eg.transform, new Vector3(0, 0, e.radius * 3f)).AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(0.55f, 0.72f, 1f);
                    l.range = e.radius * 14f;
                    var et = eg.AddComponent<EngineThrust>();
                    et.plume = wrap;
                    et.glow = l;
                    et.maxIntensity = 600f;
                    return et;
                }).ToArray();
            }

            // elevator thrusters: four under the hull (they fire downward to lift the ship) and four
            // on top (they fire upward to push it down), fore and aft on each side. Each sits on
            // the hull surface, found from the mesh itself.
            {
                // cast down onto the deck / up onto the keel against the hull's collider
                // (the AI mesh has big flat faces, so its vertices are too sparse to search)
                var hullCol = hull.GetComponentInChildren<MeshCollider>();
                Physics.SyncTransforms();
                float Surface(float x, float z, bool top)
                {
                    var from = root.transform.TransformPoint(new Vector3(x, top ? 200f : -200f, z));
                    var ray = new Ray(from, root.transform.TransformDirection(top ? Vector3.down : Vector3.up));
                    if (hullCol != null && hullCol.Raycast(ray, out var hit, 400f)) return root.transform.InverseTransformPoint(hit.point).y;
                    Log($"elevator thruster: no hull surface at x {x} z {z} ({(top ? "top" : "bottom")})");
                    return top ? hb.max.y : hb.min.y;
                }
                var node = Node("ElevatorThrusters", root.transform).transform;
                var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
                ThrusterFlare Make(string name, Vector3 pos, Vector3 dir)
                {
                    var t = Node(name, node, pos, Quaternion.LookRotation(dir, Vector3.forward));
                    var wrap = Node("Plume", t.transform).transform;
                    var pm = Place(plumeModel, wrap, "PlumeMesh");
                    var (ax, len) = AlignLongAxis(pm.transform, Vector3.forward);
                    var pb = ParentBounds(pm.transform);
                    float width = Mathf.Max(0.1f, Mathf.Max(pb.size.x, pb.size.y));
                    Stretch(pm.transform, ax, len, 10f, 3.6f / width);
                    pb = ParentBounds(pm.transform);
                    pm.transform.localPosition += new Vector3(-pb.center.x, -pb.center.y, -pb.min.z);
                    foreach (var r in pm.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.enabled = false; }
                    var l = Node("Glow", t.transform, new Vector3(0, 0, 4f)).AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(0.6f, 0.78f, 1f);
                    l.range = 45f;
                    l.intensity = 0f;
                    l.enabled = false;
                    var f = t.AddComponent<ThrusterFlare>();
                    f.plume = wrap;
                    f.glow = l;
                    f.maxIntensity = 250f;
                    return f;
                }
                var spots = new[] { new Vector2(-15f, 58f), new Vector2(15f, 58f), new Vector2(-15f, -32f), new Vector2(15f, -32f) };
                cc.liftThrustersUp = spots.Select((s, i) =>
                    Make("Ventral" + (i + 1), new Vector3(s.x, Surface(s.x, s.y, false) - 0.3f, s.y), Vector3.down)).ToArray();
                cc.liftThrustersDown = spots.Select((s, i) =>
                    Make("Dorsal" + (i + 1), new Vector3(s.x, Surface(s.x, s.y, true) + 0.3f, s.y), Vector3.up)).ToArray();
                Log("elevator thrusters: ventral at y " + string.Join(", ", cc.liftThrustersUp.Select(f => f.transform.localPosition.y.ToString("F1"))) +
                    "; dorsal at y " + string.Join(", ", cc.liftThrustersDown.Select(f => f.transform.localPosition.y.ToString("F1"))));
            }

            AddFrigateSystems(root, cc, hull, p);

            var saved = SavePrefab(root, "Frigate");
            Log("frigate prefab saved");
            return saved;
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(Prefabs p, GameObject carrierPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", KitTexture("TC_Space.exr"));
            sky.SetFloat("_Mapping", 1f);
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Exposure", 1.1f);
            sky = SaveMat("Space_Skybox", sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.22f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.11f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
            RenderSettings.sun = sun;

            // post-processing: bloom for the emissive lights, ACES tonemapping
            string profilePath = Root + "/Settings/CarrierVolume.asset";
            Directory.CreateDirectory(Root + "/Settings");
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.3f);
            foreach (var comp in profile.components) AssetDatabase.AddObjectToAsset(comp, profile);
            AssetDatabase.SaveAssets();
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var carrier = (GameObject)PrefabUtility.InstantiatePrefab(carrierPrefab);
            var cc = carrier.GetComponent<CarrierController>();
            if (cc.flankBays != null && cc.flankBays.Length > 0)
            {
                cc.fighterPrefab = p.fighter;
                cc.dronePrefab = p.drone;
                cc.dropshipPrefab = p.dropship;
            }

            var cruiser = (GameObject)PrefabUtility.InstantiatePrefab(p.enemyCruiser);
            var om = cruiser.GetComponent<OrbitMover>();
            om.radius = 2600f; om.height = 250f; om.speed = 45f; om.startAngle = 35f;
            var d1 = (GameObject)PrefabUtility.InstantiatePrefab(p.enemyDrone);
            d1.name = "EnemyDrone A";
            d1.GetComponent<TargetInfo>().displayName = "Hostile Drone A";
            om = d1.GetComponent<OrbitMover>(); om.radius = 1500f; om.height = -250f; om.speed = 140f; om.startAngle = 160f;
            var d2 = (GameObject)PrefabUtility.InstantiatePrefab(p.enemyDrone);
            d2.name = "EnemyDrone B";
            d2.GetComponent<TargetInfo>().displayName = "Hostile Drone B";
            om = d2.GetComponent<OrbitMover>(); om.radius = 1900f; om.height = 420f; om.speed = -170f; om.startAngle = 260f;
            // place them where they start, for the editor view and screenshots
            foreach (var e in new[] { cruiser, d1, d2 })
            {
                var o = e.GetComponent<OrbitMover>();
                e.transform.position = o.center + Quaternion.Euler(0, o.startAngle, 0) * Vector3.forward * o.radius + Vector3.up * o.height;
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 30000f;
            cam.nearClipPlane = 0.5f;
            cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.target = carrier.transform;
            camGo.transform.position = new Vector3(700f, 300f, 800f);
            camGo.transform.LookAt(carrier.transform.position);
            camGo.AddComponent<AudioListener>();

            var demo = new GameObject("Demo").AddComponent<CarrierDemoInput>();
            demo.carrier = cc;
            demo.orbitCamera = orbit;
            demo.targets = new[] { cruiser.transform, d1.transform, d2.transform };
            cc.target = cruiser.transform;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Log("scene saved " + ScenePath);
        }

        // ------------------------------------------------------------------ screenshots
        [MenuItem("Tools/Carrier/Take Screenshots")]
        public static void TakeScreenshots()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(ShotDir);
            var ship = Object.FindAnyObjectByType<CarrierController>();
            if (ship != null && ship.name.StartsWith("Frigate")) { FrigateShots(ship); return; }
            var carrier = GameObject.Find("Carrier");
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 30000f;
            cam.nearClipPlane = 0.3f;
            cam.fieldOfView = 40f;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var c = carrier.transform;
            Transform Find(string n) => c.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == n);
            var shots = new List<(string, Vector3, Vector3)>
            {
                ("front34", new Vector3(560, 300, 700), new Vector3(0, 0, 30)),
                ("rear34", new Vector3(-520, 240, -760), new Vector3(0, 0, -60)),
                ("side", new Vector3(980, 40, -30), new Vector3(0, 0, -30)),
                ("below", new Vector3(420, -460, 320), new Vector3(0, -40, 0)),
                ("top", new Vector3(0, 1100, 1), new Vector3(0, 0, 0)),
            };
            var bay = Find("Bay_S2");
            if (bay) shots.Add(("bay", bay.position + bay.forward * 70f + Vector3.up * 8f + Vector3.forward * 30f, bay.position - bay.forward * 15f));
            var t1 = Find("MainTurret_3");
            if (t1) shots.Add(("turret", t1.position + new Vector3(-45, 22, 55), t1.position + Vector3.up * 5f));
            var bridge = Find("Antennas");
            shots.Add(("bridge", new Vector3(70, 175, -60), new Vector3(0, 125, -160)));
            shots.Add(("engines", new Vector3(-150, 60, -560), new Vector3(0, 0, -340)));
            var hangar = Find("VentralHangar");
            if (hangar) shots.Add(("hangar", hangar.position + new Vector3(60, -140, 120), hangar.position + Vector3.up * 15f));
            var pd = Find("PD_1");
            if (pd) shots.Add(("pd", pd.position + new Vector3(-30, 20, 35) + pd.up * 10f, pd.position));

            foreach (var (name, pos, look) in shots)
            {
                go.transform.position = c.TransformPoint(pos);
                go.transform.LookAt(c.TransformPoint(look));
                Render(cam, name);
            }
            Lineup(cam);
            Object.DestroyImmediate(go);
            Log("screenshots in " + ShotDir);
        }

        static void FrigateShots(CarrierController ship)
        {
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 30000f;
            cam.nearClipPlane = 0.3f;
            cam.fieldOfView = 40f;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var c = ship.transform;
            var shots = new List<(string, Vector3, Vector3)>
            {
                ("f_front34", new Vector3(170, 90, 230), new Vector3(0, 0, 10)),
                ("f_rear34", new Vector3(-160, 70, -240), new Vector3(0, 0, -30)),
                ("f_port", new Vector3(-300, 15, -10), new Vector3(0, 0, -10)),
                ("f_starboard", new Vector3(300, 15, -10), new Vector3(0, 0, -10)),
                ("f_top", new Vector3(0, 380, 1), Vector3.zero),
                ("f_engines", new Vector3(-50, 15, -190), new Vector3(0, -8, -88)),
            };
            foreach (var tu in ship.mainTurrets)
                if (tu) shots.Add(("f_gun_" + tu.Label, c.InverseTransformPoint(tu.transform.position + tu.transform.forward * 45f + Vector3.up * 12f + c.forward * 25f),
                                   c.InverseTransformPoint(tu.transform.position)));
            foreach (var (name, pos, look) in shots)
            {
                go.transform.position = c.TransformPoint(pos);
                go.transform.LookAt(c.TransformPoint(look));
                Render(cam, name);
            }
            // guns swung up and toward the bow, to check the ball and barrel move and the collar stays
            var mounts = ship.mainTurrets.Where(tu => tu).Select(tu => tu.yaw).ToArray();
            foreach (var m in mounts) m.localRotation = Quaternion.Euler(-30f, 0f, 0f) * Quaternion.AngleAxis(m.parent.InverseTransformDirection(c.forward).x > 0 ? 45f : -45f, Vector3.up);
            foreach (var tu in ship.mainTurrets)
                if (tu)
                {
                    go.transform.position = tu.transform.position + tu.transform.forward * 30f + c.forward * 22f + Vector3.up * 8f;
                    go.transform.LookAt(tu.transform.position);
                    Render(cam, "f_gun_aimed_" + tu.Label);
                }
            foreach (var m in mounts) m.localRotation = Quaternion.identity;

            // elevator thruster flares: the ventral set (going up), then the dorsal set (going down)
            void Flares(ThrusterFlare[] set, float k) { if (set != null) foreach (var f in set) if (f) f.Apply(k); }
            Flares(ship.liftThrustersUp, 0f);
            Flares(ship.liftThrustersDown, 0f);
            Flares(ship.liftThrustersUp, 1f);
            go.transform.position = c.TransformPoint(new Vector3(230f, -70f, 40f));
            go.transform.LookAt(c.TransformPoint(new Vector3(0f, -25f, 10f)));
            Render(cam, "f_elevators_up");
            Flares(ship.liftThrustersUp, 0f);
            Flares(ship.liftThrustersDown, 1f);
            go.transform.position = c.TransformPoint(new Vector3(230f, 80f, 40f));
            go.transform.LookAt(c.TransformPoint(new Vector3(0f, 25f, 10f)));
            Render(cam, "f_elevators_down");
            Flares(ship.liftThrustersDown, 0f);

            FrigateSystemShots(ship, cam);

            // gun arcs and muzzles drawn as markers, seen from above
            var marks = new List<GameObject>();
            foreach (var tu in ship.mainTurrets)
            {
                if (!tu) continue;
                foreach (var m in tu.muzzles)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    s.transform.position = m.position + m.forward * 6f;
                    s.transform.localScale = Vector3.one * 5f;
                    if (Mats.TryGetValue("Hull_Accent", out var red)) s.GetComponent<Renderer>().sharedMaterial = red;
                    marks.Add(s);
                }
            }
            go.transform.position = c.TransformPoint(new Vector3(0, 380, 1));
            go.transform.LookAt(c.position);
            Render(cam, "f_top_muzzles");
            foreach (var m in marks) Object.DestroyImmediate(m);
            Object.DestroyImmediate(go);
            Log("frigate screenshots in " + ShotDir);
        }

        static void Render(Camera cam, string name, int w = 1600, int h = 900)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{ShotDir}/{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        /// <summary>Craft and parts in a row, each with a red marker at its forward (+Z) end and a
        /// green marker on top (+Y), to check orientation.</summary>
        static void Lineup(Camera cam)
        {
            var row = new GameObject("Lineup");
            row.transform.position = new Vector3(0, 0, 9000);
            var items = new[] { "Fighter", "Drone", "Dropship", "Missile", "PDTurret", "TripleTurret" };
            float x = 0;
            var red = Mats["Hull_Accent"];
            var green = Mats["Light_Strip"];
            foreach (var n in items)
            {
                var prefab = Load<GameObject>($"{PrefabDir}/{n}.prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetParent(row.transform, false);
                var b = LocalBounds(go.transform);
                go.transform.localPosition = new Vector3(x - b.min.x, 0, 0);
                float m = Mathf.Max(1f, b.size.magnitude * 0.06f);
                var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f.transform.SetParent(go.transform, false);
                f.transform.localPosition = new Vector3(b.center.x, b.center.y, b.max.z + m);
                f.transform.localScale = Vector3.one * m;
                f.GetComponent<Renderer>().sharedMaterial = red;
                var u = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                u.transform.SetParent(go.transform, false);
                u.transform.localPosition = new Vector3(b.center.x, b.max.y + m, b.center.z);
                u.transform.localScale = Vector3.one * m;
                u.GetComponent<Renderer>().sharedMaterial = green;
                x += b.size.x + 25f;
            }
            cam.transform.position = row.transform.position + new Vector3(x * 0.5f, 60f, -x * 0.9f);
            cam.transform.LookAt(row.transform.position + new Vector3(x * 0.5f, 0, 0));
            Render(cam, "lineup");
            cam.transform.position = row.transform.position + new Vector3(-80f, 40f, 30f);
            cam.transform.LookAt(row.transform.position + new Vector3(x * 0.25f, 0, 0));
            Render(cam, "lineup_side");
            Object.DestroyImmediate(row);
        }
    }
}
