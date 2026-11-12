using System;
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
    /// The industry (Resources/Data/industry.json): refineries and factories, from the project's
    /// own models (Tools/Blender/build_industry.py, Models/Ind_*.fbx, with their lights and berths
    /// in Data/industry_hardpoints.json) and textures (Tools/Textures/make_industry_textures.py,
    /// Art/Industry). Five sites:
    ///
    /// - Jovara Gas Works, a gas refinery in orbit round Jovara (docked at);
    /// - Kestrel Deep Yard, a factory on its own orbit in deep space (docked at);
    /// - Belt Smelter, a metal smelter on a rock at the edge of the Inner Belt (across the belt
    ///   from Deepcut Station; the Outer Drift is the corsairs'), with a
    ///   landing pad on the rock (docked at: the frigate sets down on the pad);
    /// - Ochre Foundry, on the desert world Ochre, and Mnemos Works, on Thalassa's moon Mnemos:
    ///   a refinery and a factory round a landing pad, seen from orbit, and built on the surface
    ///   when the frigate lands (PlanetSurface.BuildIndustry).
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string IndArtDir = Root + "/Art/Industry";
        const string IndPrefabDir = PrefabDir + "/Industry";
        static readonly string[] IndModels = { "Ind_RefinerySpace", "Ind_FactorySpace", "Ind_RefinerySurface", "Ind_FactorySurface", "Ind_LandingPad" };

        [Serializable] class IndPoint { public float[] p, forward; public string colour; }
        [Serializable]
        class IndModelPoints
        {
            public string name;
            public float[] size;
            public float radius, padRadius, deckHeight;
            public IndPoint berth, padCentre;
            public IndPoint[] navLights, floods, glows;
        }
        [Serializable] class IndPoints { public IndModelPoints[] models; }

        class IndPrefabs
        {
            public GameObject refinerySpace, factorySpace, refinerySurface, factorySurface, pad;
            public IndModelPoints padPoints;
        }
        static IndPrefabs ind;

        static Vector3 IV(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        /// <summary>A model's lights, berth and pad (Data/industry_hardpoints.json, or
        /// settlement_hardpoints.json for the settlements' own models), or null.</summary>
        static IndModelPoints IndPointsFor(string model)
        {
            foreach (var file in new[] { "industry_hardpoints.json", "settlement_hardpoints.json" })
            {
                var path = Root + "/Data/" + file;
                if (!File.Exists(path)) continue;
                var found = JsonUtility.FromJson<IndPoints>(File.ReadAllText(path))?.models?.FirstOrDefault(m => m.name == model);
                if (found != null) return found;
            }
            return null;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>A texture of the industry set, imported as it should be (colour, normal, or a
        /// linear mask), or null if it hasn't been made.</summary>
        static Texture2D IndTexture(string name, bool normal = false, bool linear = false, bool clamp = false)
        {
            string path = $"{IndArtDir}/{name}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
            bool dirty = false;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type) { ti.textureType = type; dirty = true; }
            bool srgb = !normal && !linear;
            if (ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
            var wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (ti.wrapMode != wrap) { ti.wrapMode = wrap; dirty = true; }
            if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
            int max = name.StartsWith("T_Ind_Pad") ? 2048 : 1024;
            if (ti.maxTextureSize != max) { ti.maxTextureSize = max; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return Load<Texture2D>(path);
        }

        /// <summary>An industry material: URP Lit with its albedo, normal and metallic-smoothness
        /// maps (and emission), named as the models' material slots are.</summary>
        static Material IndMat(string slot, Color fallback, float metallic, float smooth, Color? emission = null, bool clamp = false)
        {
            string n = slot.Substring("Ind_".Length);
            var d = IndTexture($"T_Ind_{n}_D", clamp: clamp);
            var nm = IndTexture($"T_Ind_{n}_N", normal: true, clamp: clamp);
            var ms = IndTexture($"T_Ind_{n}_M", linear: true, clamp: clamp);
            var em = IndTexture($"T_Ind_{n}_E", clamp: clamp);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", d != null ? Color.white : fallback);
            if (d != null) m.SetTexture("_BaseMap", d);
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f);         // (the map's alpha is the smoothness)
                m.SetFloat("_Metallic", 1f);
            }
            else
            {
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smooth);
            }
            if (emission != null)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", emission.Value);
                if (em != null) m.SetTexture("_EmissionMap", em);
            }
            return SaveMat(slot, m);
        }

        /// <summary>The materials the industry models' slots are matched to (by name). Run with the
        /// rest of the materials, before the models' materials are remapped.</summary>
        static void IndustryMaterials()
        {
            ind = null;
            IndMat("Ind_Panel", new Color(0.7f, 0.7f, 0.68f), 0.6f, 0.45f);
            IndMat("Ind_Dark", new Color(0.22f, 0.23f, 0.25f), 0.6f, 0.4f);
            IndMat("Ind_Paint", new Color(0.85f, 0.42f, 0.12f), 0.15f, 0.35f);
            IndMat("Ind_Hazard", new Color(0.9f, 0.72f, 0.1f), 0.2f, 0.4f);
            IndMat("Ind_Grate", new Color(0.35f, 0.36f, 0.37f), 0.7f, 0.35f);
            IndMat("Ind_Tank", new Color(0.86f, 0.84f, 0.78f), 0.2f, 0.4f);
            IndMat("Ind_Pipe", new Color(0.6f, 0.62f, 0.66f), 0.9f, 0.55f);
            IndMat("Ind_Windows", new Color(0.2f, 0.22f, 0.25f), 0.3f, 0.6f, new Color(1f, 0.85f, 0.62f) * 2.2f);
            IndMat("Ind_Pad", new Color(0.3f, 0.31f, 0.32f), 0.2f, 0.3f, new Color(1f, 0.9f, 0.7f) * 3f, clamp: true);
            var light = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            light.SetColor("_BaseColor", new Color(1f, 0.92f, 0.78f));
            light.EnableKeyword("_EMISSION");
            light.SetColor("_EmissionColor", new Color(1f, 0.88f, 0.66f) * 6f);
            SaveMat("Ind_Light", light);
            var glow = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            glow.SetColor("_BaseColor", new Color(1f, 0.5f, 0.15f));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.1f) * 9f);
            SaveMat("Ind_Glow", glow);
        }

        /// <summary>The industry models, for the import settings and the material remap.</summary>
        static System.Collections.Generic.IEnumerable<string> IndustryModelPaths()
        {
            foreach (var n in IndModels)
            {
                string path = $"{Root}/Models/{n}.fbx";
                if (File.Exists(path)) yield return path;
            }
        }

        // ------------------------------------------------------------------ prefabs

        /// <summary>The industry prefabs (made once a build): each model with colliders on the
        /// obstacle layer, its nav lights and floodlights, and in space its berth.</summary>
        static IndPrefabs IndustryPrefabs()
        {
            if (ind != null) return ind;
            layouts = null;   // (the settlements' layouts are made from these)
            missingPieces.Clear();
            ind = new IndPrefabs
            {
                refinerySpace = IndPrefab("Ind_RefinerySpace", true),
                factorySpace = IndPrefab("Ind_FactorySpace", true),
                refinerySurface = IndPrefab("Ind_RefinerySurface", false),
                factorySurface = IndPrefab("Ind_FactorySurface", false),
                pad = IndPad(),
                padPoints = IndPointsFor("Ind_LandingPad"),
            };
            return ind;
        }

        static Material IndLightMat(string colour)
        {
            if (sm == null) StructureMaterials();
            return colour == "red" ? sm.red : colour == "green" ? sm.green : sm.work;
        }

        /// <summary>The model's meshes under `root`, unpacked, each with a mesh collider.</summary>
        static GameObject IndModel(string model, Transform root, int layer)
        {
            var m = OwnModel(model);
            if (m == null) return null;
            var inst = Place(m, root, "Model");
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                mf.gameObject.layer = layer;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
            }
            return inst;
        }

        static void IndLights(Transform root, IndModelPoints hp)
        {
            if (hp == null) return;
            int i = 0;
            if (hp.navLights != null)
                foreach (var l in hp.navLights)
                    NavLight(root, IV(l.p), IndLightMat(l.colour), 5f, 1.6f, (i++ * 0.37f) % 1f);
            if (hp.floods != null)
                foreach (var f in hp.floods)
                    Flood(root, IV(f.p), new Color(1f, 0.88f, 0.7f), Mathf.Max(220f, hp.radius * 0.8f), 6f);
        }

        static GameObject IndPrefab(string model, bool space)
        {
            int layer = ObstacleLayer();
            var root = new GameObject(model);
            root.layer = layer;
            if (IndModel(model, root.transform, layer) == null) { Object.DestroyImmediate(root); Log("missing industry model " + model); return null; }
            var hp = IndPointsFor(model);
            IndLights(root.transform, hp);
            if (space && hp?.berth != null && IV(hp.berth.forward).sqrMagnitude > 0.01f)
                Berth(root.transform, IV(hp.berth.p), IV(hp.berth.forward).normalized);
            Directory.CreateDirectory(IndPrefabDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{IndPrefabDir}/{model}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>The landing pad: its deck ("Pad Deck") right under the root, named "Outpost" as a
        /// pad's root is, so landing on it counts as landing on a pad.</summary>
        static GameObject IndPad()
        {
            int layer = ObstacleLayer();
            var root = new GameObject("Outpost");
            root.layer = layer;
            var model = IndModel("Ind_LandingPad", root.transform, layer);
            if (model == null) { Object.DestroyImmediate(root); Log("missing industry model Ind_LandingPad"); return null; }
            // the deck and frame straight under the root (the pad test looks at the deck's parent)
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true).ToArray())
                mf.transform.SetParent(root.transform, true);
            Object.DestroyImmediate(model);
            IndLights(root.transform, IndPointsFor("Ind_LandingPad"));
            Directory.CreateDirectory(IndPrefabDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{IndPrefabDir}/Ind_LandingPad.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>The surface kit's industry parts (for PlanetSurface.BuildIndustry).</summary>
        static void IndustryKit(SurfaceKit kit)
        {
            var ip = IndustryPrefabs();
            kit.industryPad = ip.pad;
            kit.industryRefinery = ip.refinerySurface;
            kit.industryFactory = ip.factorySurface;
            if (ip.padPoints != null)
            {
                if (ip.padPoints.padRadius > 1f) kit.industryPadRadius = ip.padPoints.padRadius;
                if (ip.padPoints.deckHeight > 0f) kit.industryDeck = ip.padPoints.deckHeight;
            }
        }

        // ------------------------------------------------------------------ the sites

        static string IndDescription(string name)
        {
            var site = IndustryTable.Get().Site(name);
            return site != null ? site.description : "";
        }

        static string IndLabel(string name)
        {
            // (as the HUD and the site's screen put it, with a capital)
            string s = IndustrySite.Services(IndustryTable.Get().Site(name));
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }

        static void IndustrySites(SystemRefs s, Transform parent, Built b)
        {
            var ip = IndustryPrefabs();
            if (ip.refinerySpace == null || ip.factorySpace == null || ip.pad == null || ip.refinerySurface == null || ip.factorySurface == null)
            {
                Log("industry: models missing, no industrial sites built");
                return;
            }
            GasWorks(s, parent, b, ip);
            DeepYard(s, parent, b, ip);
            BeltSmelter(s, parent, b, ip);
            var wOchre = s.ochre.GetComponent<WarpPoint>();
            var wMnemos = s.sys.GetComponentsInChildren<WarpPoint>(true).First(w => w.displayName == "Mnemos");
            SurfaceIndustry(s, "Ochre Foundry", wOchre, 18f, -70f, 0.5f, b, ip);
            SurfaceIndustry(s, "Mnemos Works", wMnemos, -12f, 30f, 0.3f, b, ip);
        }

        /// <summary>A site in space, docked at: its model, berth, the IndustrySite and its warp point.</summary>
        static GameObject SpaceSite(string name, GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, Built b, WarpPoint parentPoint = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.transform.SetParent(go.transform, false);
            // (its berth in the level: the model is loaded only when the frigate is near)
            var berth = OwnBerth(go.transform, model.transform.Find("Berth"));
            Facility(go, name, WarpPoint.Kind.Installation, false, berth, false);
            go.AddComponent<IndustrySite>().siteName = name;
            var hp = IndPointsFor(prefab.name);
            float r = hp != null && hp.radius > 1f ? hp.radius : 450f;
            Obstruct(go, r, r * 0.35f, false);
            Destination(go, name, WarpPoint.Kind.Installation, IndDescription(name) + " Dock here (K), then U for the site's screen.",
                        r, Mathf.Max(3000f, r * 4f), FriendlyWorks, parentPoint, false, IndLabel(name));
            Vector3 view = berth != null ? berth.position + (berth.position - pos).normalized * r * 2.2f + Vector3.up * r * 0.9f
                                         : pos + rot * new Vector3(r * 2f, r, r * 2f);
            b.shots.Add(($"ind_{name.ToLower().Replace(' ', '_')}", view, pos));
            StreamOut(go.transform, name, r, null, "Berth");
            return go;
        }

        static void GasWorks(SystemRefs s, Transform parent, Built b, IndPrefabs ip)
        {
            // 25 km out from Jovara, away from where its moons are at the start
            Vector3 jv = s.jovara.transform.position;
            Vector3 toStar = -jv; toStar.y = 0f; toStar.Normalize();
            Vector3 dir = Quaternion.Euler(0f, 150f, 0f) * toStar;
            Vector3 pos = jv + dir * 25000f + Vector3.up * 800f;
            SpaceSite("Jovara Gas Works", ip.refinerySpace, parent, pos, Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up), b,
                      s.jovara.GetComponent<WarpPoint>());
        }

        static void DeepYard(SystemRefs s, Transform parent, Built b, IndPrefabs ip)
        {
            // alone between the planets, on its own orbit 150 km from the star
            Vector3 pos = Polar(290f, 150f, 4f);
            Vector3 toStar = -pos; toStar.y = 0f;
            SpaceSite("Kestrel Deep Yard", ip.factorySpace, parent, pos, Quaternion.LookRotation(toStar.normalized, Vector3.up), b);
        }

        static void BeltSmelter(SystemRefs s, Transform parent, Built b, IndPrefabs ip)
        {
            // on a rock at the Inner Belt's edge, round the belt from Deepcut Station (which is on
            // its outer edge): the Outer Drift is the corsairs', and no place to dock
            Vector3 radial = new Vector3(s.innerC.x, 0f, s.innerC.z).normalized;
            Vector3 outward = Vector3.Cross(Vector3.up, radial).normalized;
            Vector3 pos = s.innerC + outward * 7500f + Vector3.up * 400f;
            const string name = "Belt Smelter";
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var rock = BaseRock(s, go.transform, "Belt Smelter", pos, 650f, 5501);
            Physics.SyncTransforms();
            var body = rock.transform;
            // the pad on top, the smelter beside it
            var padSpot = OnSurface(go.transform, body, Vector3.up, 640f, "Pad site", Vector3.forward, 2f);
            var pad = (GameObject)PrefabUtility.InstantiatePrefab(ip.pad);
            pad.transform.SetParent(padSpot, false);
            var smelterSpot = OnSurface(go.transform, body, (Vector3.up + Vector3.right * 0.55f).normalized, 640f, "Smelter site", Vector3.forward, 6f);
            var smelter = (GameObject)PrefabUtility.InstantiatePrefab(ip.refinerySurface);
            smelter.transform.SetParent(smelterSpot, false);
            smelter.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            // the frigate sets down on the pad: its berth is on the deck, level, its keel on it
            float deck = ip.padPoints != null && ip.padPoints.deckHeight > 0f ? ip.padPoints.deckHeight : 6f;
            var berth = Berth(padSpot, new Vector3(0f, deck + FrigateKeel(), 0f), Vector3.forward);
            Facility(go, name, WarpPoint.Kind.AsteroidBase, false, berth, false);
            var site = go.AddComponent<IndustrySite>();
            site.siteName = name;
            Destination(go, name, WarpPoint.Kind.AsteroidBase, IndDescription(name) + " Dock (K) to set down on its pad, then U for the site's screen.",
                        700f, 3200f, FriendlyWorks, null, false, IndLabel(name));
            b.shots.Add(("ind_belt_smelter", padSpot.position + Vector3.up * 900f + outward * 1400f, padSpot.position));
            StreamOut(go.transform, name, 700f, body, rock.name, "Pad site");
        }

        /// <summary>How far the frigate's origin is above the bottom of its hull (for a berth on a deck).</summary>
        static float FrigateKeel()
        {
            var prefab = Load<GameObject>(PrefabDir + "/Frigate.prefab");
            if (prefab == null) return 20f;
            var bnd = LocalBounds(prefab.transform);
            return Mathf.Max(5f, -bnd.min.y + 2f);
        }

        /// <summary>A site on a planet's or moon's surface, as seen from orbit (its pad, refinery and
        /// factory at `k` of their size), and a destination over it: landing there comes down beside
        /// it, where the surface builds it in full.</summary>
        static void SurfaceIndustry(SystemRefs s, string name, WarpPoint body, float latitude, float sunAngle, float k, Built b, IndPrefabs ip)
        {
            var bt = body.transform;
            float radius = body.bodyRadius;
            Vector3 sun = (s.star.position - bt.position).normalized;
            Vector3 face = Quaternion.AngleAxis(sunAngle, bt.up) * sun;
            Vector3 local = bt.InverseTransformDirection(face);
            float lon = Mathf.Atan2(local.z, local.x);
            float lat = latitude * Mathf.Deg2Rad;
            Vector3 dir = bt.TransformDirection(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon))).normalized;

            var go = new GameObject(name);
            go.transform.SetParent(bt, true);
            float sc = bt.lossyScale.x;
            go.transform.localScale = Vector3.one / Mathf.Max(1e-6f, sc);
            go.transform.SetPositionAndRotation(bt.position + dir * radius, Quaternion.FromToRotation(Vector3.up, dir));
            var anchor = go.AddComponent<SurfaceAnchor>();
            anchor.body = bt;
            anchor.sink = 0f;
            Physics.SyncTransforms();
            anchor.Settle();
            Object.DestroyImmediate(anchor);
            var spec = IndustryTable.Get().Site(name);
            void Put(GameObject prefab, Vector3 at, float yaw)
            {
                if (prefab == null) return;
                var m = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                m.transform.SetParent(go.transform, false);
                m.transform.localScale = Vector3.one * k;
                m.transform.localPosition = at * k;
                m.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var an = m.AddComponent<SurfaceAnchor>(); an.body = bt; an.sink = 1f; an.Settle();
            }
            float off = SurfaceWorld.PadRadius + 230f;
            Put(ip.pad, Vector3.zero, 0f);
            if (spec == null || spec.CanRefine) Put(ip.refinerySurface, new Vector3(0f, 0f, off), 180f);
            if (spec == null || spec.CanBuild) Put(ip.factorySurface, new Vector3(0f, 0f, -off), 0f);
            Flood(go.transform, new Vector3(0f, 120f, 0f), new Color(1f, 0.88f, 0.7f), 500f, 6f);
            var wp = Destination(go, name, WarpPoint.Kind.SurfaceBase, IndDescription(name) + " From orbit over it, land (K), set down on the pad, then U for the site's screen.",
                                 300f, 2600f, FriendlyWorks, body, false, IndLabel(name));
            wp.landsOn = body;
            wp.overhead = true;
            wp.surface = body.surface;
            Facility(go, name, WarpPoint.Kind.SurfaceBase, false);
            Vector3 tangent = Vector3.Cross(dir, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.5f) tangent = Vector3.right;
            b.shots.Add(($"ind_{name.ToLower().Replace(' ', '_')}", go.transform.position + dir * 900f + tangent * 700f, go.transform.position));
            StreamOut(go.transform, name, 600f, bt);
        }
    }
}
