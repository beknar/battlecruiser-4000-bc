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
    /// Builds the three levels from the Blender-made parts (Assets/Carrier/Models) and the kit parts
    /// copied from the Unreal export (Assets/Carrier/Kit): materials, prefabs (the frigate, the
    /// enemies, the capital ships on the carrier's hull), the scenes, and review screenshots in
    /// Temp/carrier_shots.
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
                var frigate = BuildFrigates(p);     // every frigate a player can fly; the starting one
                BuildScene(p, frigate);
                TakeScreenshots();
                BuildAsteroidScene(p, frigate);     // the second level: the asteroid-field ambush
                BuildStarSystemScene(p, frigate);   // the third level: the star system and its mini-warp
                RemoveStaleChunks();
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

        /// <summary>BuildAll from the command line (-batchmode -executeMethod): Unity empties Temp
        /// when it quits, so the report and the review pictures are copied to Logs/carrier_build.</summary>
        public static void BuildAllBatch()
        {
            BuildAll();
            const string Out = "Logs/carrier_build";
            if (Directory.Exists(Out)) Directory.Delete(Out, true);
            Directory.CreateDirectory(Out);
            File.Copy("Temp/carrier_build.log", Out + "/carrier_build.log", true);
            if (Directory.Exists(ShotDir))
                foreach (var f in Directory.GetFiles(ShotDir)) File.Copy(f, Path.Combine(Out, Path.GetFileName(f)), true);
        }

        static void LoadData()
        {
            hp = JsonUtility.FromJson<HP>(File.ReadAllText(Root + "/Data/carrier_hardpoints.json"));
            kit = JsonUtility.FromJson<KitData>(File.ReadAllText(Root + "/Data/kit_materials.json"));
            fhp = JsonUtility.FromJson<FHP>(File.ReadAllText(Root + "/Data/frigate_hardpoints.json"));
            LoadFrigateSpecs();
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
            foreach (var path in FrigateModelPaths()) yield return path;
            foreach (var path in IndustryModelPaths()) yield return path;
            foreach (var path in SettlementModelPaths()) yield return path;
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
            // as the editor would leave it (URP fills in its render type, passes and colours)
            Validate(m);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && SameMaterial(existing, m))
            {
                Object.DestroyImmediate(m);
                Mats[name] = existing;
                return existing;
            }
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.renderQueue = m.renderQueue;
                existing.SetOverrideTag("RenderType", m.GetTag("RenderType", false));
                Validate(existing);
                EditorUtility.SetDirty(existing);
                Mats[name] = existing;
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            Mats[name] = m;
            return m;
        }

        /// <summary>What the editor does to a material when it is opened or edited: its shader's
        /// own editor (URP's Lit, Unlit...) sets the render type, the passes and the keywords that
        /// follow from its properties. A material made in code doesn't get this until someone
        /// looks at it, so without it a rebuild would undo what the editor did, and the editor
        /// would do it again: a change to every material each time.</summary>
        static void Validate(Material m)
        {
            var editor = Editor.CreateEditor(m) as MaterialEditor;
            if (editor == null) return;
            try { editor.customShaderGUI?.ValidateMaterial(m); }
            finally { Object.DestroyImmediate(editor); }
        }

        /// <summary>A level's camera: it orbits `target`, with URP post-processing and SMAA, and
        /// carries the audio listener.</summary>
        static OrbitCamera MainCamera(Transform target, float near, float far, Vector3 position, Vector3 lookAt)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = far;
            cam.nearClipPlane = near;
            cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.target = target;
            camGo.transform.position = position;
            camGo.transform.LookAt(lookAt);
            camGo.AddComponent<AudioListener>();
            return orbit;
        }

        /// <summary>An engine's exhaust on its node `eg` (which faces out of the nozzle): the plume
        /// model stretched to the engine's size on a "Plume" node, a glow light, and the
        /// EngineThrust that drives them.</summary>
        static EngineThrust Exhaust(GameObject eg, GameObject plumeModel, float radius, Color glow)
        {
            var wrap = Node("Plume", eg.transform).transform;
            if (plumeModel != null)
            {
                var pm = Place(plumeModel, wrap, "PlumeMesh");
                var (ax, len) = AlignLongAxis(pm.transform, Vector3.forward);
                var pb = ParentBounds(pm.transform);
                float width = Mathf.Max(0.1f, Mathf.Max(pb.size.x, pb.size.y));
                Stretch(pm.transform, ax, len, radius * 6f, radius * 1.5f / width);
                pb = ParentBounds(pm.transform);
                pm.transform.localPosition += new Vector3(-pb.center.x, -pb.center.y, -1f - pb.min.z);
                foreach (var r in pm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var l = Node("Glow", eg.transform, new Vector3(0, 0, radius * 3f)).AddComponent<Light>();
            l.type = LightType.Point;
            l.color = glow;
            l.range = radius * 14f;
            var et = eg.AddComponent<EngineThrust>();
            et.plume = wrap;
            et.glow = l;
            et.maxIntensity = 600f;
            return et;
        }

        /// <summary>Every renderer under a model in one material (each of its slots).</summary>
        static void Paint(GameObject go, Material mat)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray();
        }

        static void Paint(Component c, Material mat) => Paint(c.gameObject, mat);

        /// <summary>Whether a saved material already has everything we set on a new one: shader,
        /// queue, render type, keywords, GI flags and every property the shader has.</summary>
        static bool SameMaterial(Material a, Material b)
        {
            if (a.shader != b.shader || a.renderQueue != b.renderQueue || a.GetTag("RenderType", false) != b.GetTag("RenderType", false)) return false;
            if (a.globalIlluminationFlags != b.globalIlluminationFlags || a.doubleSidedGI != b.doubleSidedGI) return false;
            if (!new HashSet<string>(a.shaderKeywords).SetEquals(b.shaderKeywords)) return false;
            var s = b.shader;
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                string n = s.GetPropertyName(i);
                switch (s.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        if (!Mathf.Approximately(a.GetFloat(n), b.GetFloat(n))) return false;
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                        if (a.GetInteger(n) != b.GetInteger(n)) return false;
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        if (a.GetColor(n) != b.GetColor(n)) return false;
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        if (a.GetVector(n) != b.GetVector(n)) return false;
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        if (a.GetTexture(n) != b.GetTexture(n) || a.GetTextureScale(n) != b.GetTextureScale(n) || a.GetTextureOffset(n) != b.GetTextureOffset(n)) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>Save a mesh made in code as an asset. One already there is overwritten in place,
        /// so it keeps its GUID and everything that uses it stays as it was.</summary>
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
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

        /// <summary>A see-through, two-sided URP material that doesn't write depth: `blend` is
        /// URP's blending mode (0 alpha, 2 additive) with the colour blend factors to match.</summary>
        static Material Transparent(string name, string shader, float blend, BlendMode src, BlendMode dst, BlendMode dstAlpha, Color color, Texture2D tex)
        {
            var m = new Material(Shader.Find(shader));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", blend);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)dstAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", color);
            if (tex) m.SetTexture("_BaseMap", tex);
            return SaveMat(name, m);
        }

        /// <summary>Unlit and additive: glows, bolts and plumes.</summary>
        static Material Additive(string name, Color hdr, Texture2D tex = null) =>
            Transparent(name, "Universal Render Pipeline/Unlit", 2f, BlendMode.One, BlendMode.One, BlendMode.One, hdr, tex);

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
            IndustryMaterials();
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
            public Projectile beam, frigateBolt, ciwsBolt, enemyCannon, enemyBlaster;
            public Explosion explosion, explosionSmall;
            public Missile missile;
            public CarrierTurret mainTurret;
            public GameObject enemyCruiser, enemyDrone, enemyFighter, enemyDestroyer, enemyDreadnought;
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
            // (under the water it trails bubbles instead of glowing)
            p.bubbles = ParticleAlpha("FX_SurfaceDust", KenneyTex("light_01"), Color.white);
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
            // (under the water it bubbles instead: the same soft particle as the torpedoes' trail)
            e.bubbles = ParticleAlpha("FX_SurfaceDust", KenneyTex("light_01"), Color.white);
            return SavePrefab<Explosion>(go, name);
        }

        static Prefabs BuildPrefabs()
        {
            var p = new Prefabs();
            p.explosion = MakeExplosion("Explosion", 6f, 90f, 1.6f, 40000f);
            p.explosionSmall = MakeExplosion("ExplosionSmall", 2f, 22f, 0.7f, 3000f);
            p.beam = MakeBolt("BeamBolt", "FX_BeamBolt", new Vector3(1.4f, 1.4f, 24f), 2400f, 60f, p.explosionSmall);
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
            MakeCapitalShips(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        static GameObject MakeEnemy(string name, GameObject model, Vector3 rot, float scale, float health, Explosion death, float deathScale,
            string displayName, string shipClass, string threat, TargetInfo.Kind kind, string weapons, Action<GameObject> arm)
        {
            var go = new GameObject(name);
            var m = Place(model, go.transform, "Model");
            m.transform.localRotation = Quaternion.Euler(rot);
            m.transform.localScale = Vector3.one * scale;
            Paint(m, Mats["Enemy_Hull"]);
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

        // ------------------------------------------------------------------ the frigate
        /// <summary>The player's frigate: the AI-made hull with a gun on each flank (no hangar,
        /// no missiles). The guns are part of the hull mesh, so each one is an invisible aiming
        /// joint at its pod that can only cover its own side: the ship has to turn to bring a
        /// broadside to bear. Quicker than the carrier, slower than fighters and drones.</summary>
        /// <summary>The frigate `fs` (and, for one with turrets modelled on it, its hardpoints `mhp`).</summary>
        static GameObject BuildFrigatePrefab(Prefabs p)
        {
            string texPath = $"{Root}/Models/{fs.texture}";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && ti.maxTextureSize != 4096)
            {
                ti.maxTextureSize = 4096;
                ti.SaveAndReimport();
            }
            // two-sided, so the pieces show their inside where the hull breaks apart
            var hullMat = Lit(mhp == null ? "Frigate_Hull" : fs.prefab + "_Hull", Color.white, Load<Texture2D>(texPath), null, 0.25f, 0.35f, twoSided: true);

            var root = new GameObject(fs.prefab);
            var cc = root.AddComponent<CarrierController>();
            cc.shipName = fs.shipName;
            cc.shipClass = fs.shipClass;
            cc.maxSpeed = fs.maxSpeed;               // the Sentinel: 150 (drones cruise 170, fighters 200)
            cc.acceleration = fs.acceleration;       // m/s per second
            cc.turnRate = fs.turnRate;               // degrees per second
            cc.pitchRate = fs.pitchRate;             // nose up/down, degrees per second
            cc.maxPitch = fs.maxPitch;
            cc.elevatorSpeed = fs.elevatorSpeed;     // elevator thrusters, m/s straight up or down
            cc.elevatorAcceleration = fs.elevatorAcceleration;
            cc.fighterStock = cc.droneStock = cc.dropshipStock = 0;
            cc.flankBays = new LaunchBay[0];
            cc.pointDefense = new CarrierTurret[0];

            var hull = Place(OwnModel(fs.model), root.transform, "Hull");
            var turretParts = TakeTurretParts(hull);
            Paint(hull, hullMat);
            // the hull's shape, so the guns can tell when their line of fire would cross it
            foreach (var mf in hull.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            var hb = LocalBounds(hull.transform);
            Log($"{fs.displayName} hull bounds center {hb.center:F1} size {hb.size:F1}" + (mhp == null ? " (expect ~53 x 63 x 180, bow at +Z)" : ""));

            // the guns: the Sentinel's ball mounts on its pods, or the turrets modelled on the others
            if (mhp != null)
            {
                var bolt = MakeBolt("FrigateBolt_" + fs.id, "FX_BeamBolt", V3(fs.boltSize), fs.boltSpeed, fs.gunDamage, p.explosionSmall);
                cc.mainTurrets = ModelledTurrets(root, turretParts, hullMat, bolt)
                    .Concat(ExtraMounts(root, hull.GetComponentInChildren<MeshCollider>(), bolt)).ToArray();
                cc.missiles = AddMissileCells(root, hull.GetComponentInChildren<MeshCollider>(), p);
            }
            else
            {
            var gunsNode = Node("SideGuns", root.transform).transform;
            // a ball mount just outboard of each pod's outer face, resting straight out from the side
            cc.mainTurrets = fhp.guns.Select(g => BallMount(gunsNode, "SideGun_" + g.name, g.name,
                V3(g.muzzle) + V3(g.outward).normalized * 1.5f, V3(g.outward).normalized, p.frigateBolt, 1f)).ToArray();
            }

            // engines: the kit exhaust plume in each of the stern nozzles
            {
                var engines = Node("Engines", root.transform).transform;
                var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
                cc.engines = (mhp != null ? mhp.engines : fhp.engines).Select((e, i) =>
                {
                    var eg = Node("Engine" + (i + 1), engines, V3(e.exit), Quaternion.LookRotation(V3(e.dir), Vector3.up));
                    return Exhaust(eg, plumeModel, e.radius, new Color(0.55f, 0.72f, 1f));
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
                var spots = LiftSpots(hb);
                // a spot worked out from the bounds (not given in the data) that misses the hull,
                // above or below (a slender bow, a waisted hull), moves in toward the centreline
                if (fs.liftSpots == null || fs.liftSpots.Length == 0)
                {
                    bool Hits(float x, float z, bool top)
                    {
                        var from = root.transform.TransformPoint(new Vector3(x, top ? 200f : -200f, z));
                        return hullCol != null && hullCol.Raycast(new Ray(from, root.transform.TransformDirection(top ? Vector3.down : Vector3.up)), out _, 400f);
                    }
                    for (int i = 0; i < spots.Length; i++)
                        for (float k = 1f; k > 0.05f; k -= 0.15f)
                            if (Hits(spots[i].x * k, spots[i].y, true) && Hits(spots[i].x * k, spots[i].y, false)) { spots[i].x *= k; break; }
                }
                cc.liftThrustersUp = spots.Select((s, i) =>
                    Make("Ventral" + (i + 1), new Vector3(s.x, Surface(s.x, s.y, false) - 0.3f, s.y), Vector3.down)).ToArray();
                cc.liftThrustersDown = spots.Select((s, i) =>
                    Make("Dorsal" + (i + 1), new Vector3(s.x, Surface(s.x, s.y, true) + 0.3f, s.y), Vector3.up)).ToArray();
                Log("elevator thrusters: ventral at y " + string.Join(", ", cc.liftThrustersUp.Select(f => f.transform.localPosition.y.ToString("F1"))) +
                    "; dorsal at y " + string.Join(", ", cc.liftThrustersDown.Select(f => f.transform.localPosition.y.ToString("F1"))));
            }

            AddFrigateSystems(root, cc, hull, p);
            root.AddComponent<FrigateClass>().id = fs.id;

            var saved = SavePrefab(root, fs.prefab);
            Log($"{fs.displayName} prefab saved: " + FrigateSummary(saved));
            return saved;
        }

        /// <summary>A visible ball turret (Tools/Blender/build_ball_turret.py) at `pivot`, resting
        /// facing `outward`: the collar stays on the hull, the ball and barrel swing with the mount;
        /// the ball's centre is the pivot. It reaches 30 degrees past square to its side in any
        /// direction (so two on opposite sides can share a target ahead, astern, above or below),
        /// wherever the line of fire clears the hull. `scale` sizes it to the ship.</summary>
        static CarrierTurret BallMount(Transform under, string node, string label, Vector3 pivot, Vector3 outward, Projectile bolt, float scale)
        {
            var go = Node(node, under, pivot, Quaternion.LookRotation(outward, Mathf.Abs(outward.y) > 0.9f ? Vector3.forward : Vector3.up));
            go.transform.localScale = Vector3.one * scale;
            var yaw = Node("Mount", go.transform).transform;
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
            Log($"ball turret {label}: barrel bounds {bb.center:F1} size {bb.size:F1} (expect along +Z to ~10.6)");
            var muzzle = Node("Muzzle", yaw, new Vector3(0f, 0f, bb.max.z + 0.5f)).transform;
            var tu = go.AddComponent<CarrierTurret>();
            tu.label = label;
            tu.yaw = yaw;
            tu.muzzles = new[] { muzzle };
            tu.projectilePrefab = bolt;
            tu.ballMount = true;
            tu.yawSpeed = fs.gunYawSpeed;
            tu.coneLimit = 120f;
            tu.checkHullClearance = true;
            tu.fireInterval = fs.gunInterval;
            tu.range = fs.gunRange;
            tu.fireCone = 3f;
            tu.leadTarget = true;
            return tu;
        }

        /// <summary>A destroyer's or cruiser's ball mounts, at the hardpoints' mounts its data picks
        /// (each set on the hull surface, found from the mesh, a little out from it).</summary>
        static CarrierTurret[] ExtraMounts(GameObject root, MeshCollider hullCol, Projectile bolt)
        {
            if (mhp?.mounts == null || fs.mounts == null || fs.mounts.Length == 0) return new CarrierTurret[0];
            var node = Node("Mounts", root.transform).transform;
            Physics.SyncTransforms();
            float scale = Mathf.Clamp(mhp.length / 200f, 1f, 1.8f);
            int p = 0, s = 0, t = 0, k = 0;
            var made = new List<CarrierTurret>();
            foreach (int i in fs.mounts)
            {
                if (i < 0 || i >= mhp.mounts.Length) { Log($"{fs.displayName}: no mount {i} in the hardpoints"); continue; }
                var m = mhp.mounts[i];
                Vector3 at = V3(m.pos), up = V3(m.up).normalized;
                // the hull's surface along the mount's side (cast in from outside)
                var from = root.transform.TransformPoint(at + up * 60f);
                if (hullCol != null && hullCol.Raycast(new Ray(from, root.transform.TransformDirection(-up)), out var hit, 120f))
                    at = root.transform.InverseTransformPoint(hit.point);
                else Log($"{fs.displayName} mount {i}: no hull surface found, set where the hardpoints say");
                string label = m.side == "port" ? "Mount P" + ++p : m.side == "starboard" ? "Mount S" + ++s : m.side == "keel" ? "Mount K" + ++k : "Mount T" + ++t;
                made.Add(BallMount(node, label.Replace(" ", "_"), label, at + up * 1.2f * scale, up, bolt, scale));
            }
            return made.ToArray();
        }

        /// <summary>A destroyer's or cruiser's missile launch cells, flush with the deck at the
        /// hardpoints' cells, and the launcher that fires from them with its magazine.</summary>
        static MissileLauncher AddMissileCells(GameObject root, MeshCollider hullCol, Prefabs pf)
        {
            if (fs.missiles <= 0 || mhp?.cells == null || mhp.cells.Length == 0) return null;
            if (pf.missile == null) pf.missile = Load<Missile>(PrefabDir + "/Missile.prefab");
            var node = Node("MissileCells", root.transform).transform;
            Physics.SyncTransforms();
            var cells = new List<Transform>();
            foreach (var c in mhp.cells)
            {
                Vector3 at = V3(c.pos);
                var from = root.transform.TransformPoint(new Vector3(at.x, at.y + 80f, at.z));
                if (hullCol != null && hullCol.Raycast(new Ray(from, -root.transform.up), out var hit, 160f))
                    at = root.transform.InverseTransformPoint(hit.point);
                var cell = Node("Cell" + (cells.Count + 1), node, at + Vector3.up * 0.6f, Quaternion.LookRotation(Vector3.up, Vector3.forward)).transform;
                // the hatch: a dark plate on the deck
                var hatch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(hatch.GetComponent<Collider>());
                hatch.name = "Hatch" + (cells.Count + 1);
                hatch.transform.SetParent(node, false);
                hatch.transform.localPosition = at + Vector3.up * 0.15f;
                hatch.transform.localScale = new Vector3(3.2f, 0.3f, 3.2f);
                hatch.GetComponent<Renderer>().sharedMaterial = Mats["Hull_Dark"];
                cells.Add(cell);
            }
            var ml = node.gameObject.AddComponent<MissileLauncher>();
            ml.missilePrefab = pf.missile;
            ml.cells = cells.ToArray();
            ml.cellReload = 10f;
            ml.salvoInterval = 0.2f;
            ml.magazine = fs.missiles;
            Log($"{fs.displayName}: {cells.Count} missile cells, {fs.missiles} missiles");
            return ml;
        }

        static string FrigateSummary(GameObject prefab)
        {
            var s = FrigateStats.Of(prefab);
            return $"{s.turrets} turrets ({s.barrels} barrels, {s.gunDps:F0} damage/s), {s.ciws} CIWS, {s.mainGuns} main gun, {s.missiles} missiles, crew {s.crew}, " +
                   $"hull {s.hull:F0}, shield {s.shield:F0}, {s.speed:F0} m/s, {s.turnRate:F0} deg/s, hold {s.cargo:F0} t";
        }

        /// <summary>The object that puts the player's chosen frigate in as the level starts.</summary>
        static void AddPlayerFrigate(CarrierController cc) => new GameObject("PlayerFrigate").AddComponent<PlayerFrigate>().ship = cc;

        // ------------------------------------------------------------------ scene
        static void BuildScene(Prefabs p, GameObject frigatePrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", KitTexture("TC_Space.exr"));
            sky.SetFloat("_Mapping", 1f);
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Exposure", 1.1f);
            sky = SaveMat("Space_Skybox", sky);

            // post-processing: bloom for the emissive lights, ACES tonemapping
            string profilePath = Root + "/Settings/CarrierVolume.asset";
            Directory.CreateDirectory(Root + "/Settings");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            T Effect<T>() where T : VolumeComponent
            {
                if (profile.TryGet<T>(out var c)) return c;
                c = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var bloom = Effect<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            Effect<Tonemapping>().mode.Override(TonemappingMode.ACES);
            Effect<Vignette>().intensity.Override(0.22f);
            Effect<ColorAdjustments>().postExposure.Override(0.3f);
            EditorUtility.SetDirty(profile);
            foreach (var comp in profile.components) EditorUtility.SetDirty(comp);
            AssetDatabase.SaveAssets();
            SpaceLighting(Quaternion.Euler(28f, -35f, 0f), 2.2f, new Color(1f, 0.96f, 0.9f), new Color(0.04f, 0.04f, 0.05f));

            var carrier = (GameObject)PrefabUtility.InstantiatePrefab(frigatePrefab);
            var cc = carrier.GetComponent<CarrierController>();
            AddPlayerFrigate(cc);

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

            var orbit = MainCamera(carrier.transform, 0.5f, 30000f, new Vector3(700f, 300f, 800f), carrier.transform.position);

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
            if (ship == null) { Log("no ship in " + ScenePath + ": no screenshots"); return; }
            FrigateShots(ship);
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
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;     // -nographics: no pictures
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
    }
}
