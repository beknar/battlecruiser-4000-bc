using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// What the planets' surfaces are built from (PlanetSurface makes the terrain itself when the
    /// ship lands): the terrain, sky, sea, lava and horizon materials, the weather particles, an
    /// outpost (a landing pad of Mega_City pad tiles with a couple of parked cargo craft and
    /// beacon lights) for the worlds where someone lives, the buildings put up round the pads (a
    /// colony's, or a hostile fortress's), and what hostile worlds' ground forces are made of
    /// (the heavy turret, enemy ships and their weapons, bunker and armour materials).
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string PadMesh = Root + "/Kit/Mega_City/Meshes/Landing_pad/SM_landing_pad_module.fbx";
        const string CraftMesh = Root + "/Kit/Mega_City/Meshes/Ship/SM_ship_flying.fbx";

        static SurfaceKit SurfaceAssets(Material rockMat, Prefabs p)
        {
            var kit = new SurfaceKit();
            kit.terrain = SaveMat("Surface_Terrain", new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")));
            kit.sky = SaveMat("Surface_Sky", new Material(Shader.Find("Carrier/SkyGradient")));
            kit.water = Lit("Surface_Water", new Color(0.02f, 0.1f, 0.2f), metallic: 0.05f, smooth: 0.93f);
            var lava = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lava.SetColor("_BaseColor", new Color(3f, 1.2f, 0.3f));
            kit.lava = SaveMat("Surface_Lava", lava);
            kit.ground = Lit("Surface_Ground", Color.white, metallic: 0f, smooth: 0.08f);
            kit.rock = rockMat;
            kit.dust = ParticleAlpha("FX_SurfaceDust", KenneyTex("light_01"), Color.white);
            kit.embers = ParticleAdditive("FX_Embers", KenneyTex("flare_01"), new Color(1.6f, 0.7f, 0.25f, 1f));
            kit.outpost = OutpostPrefab();
            // the ground forces of hostile worlds
            kit.battery = p.mainTurret;
            kit.cannon = p.enemyCannon;
            kit.blaster = p.enemyBlaster;
            kit.missile = p.missile;
            kit.fighter = p.enemyFighter;
            kit.drone = p.enemyDrone;
            kit.gunship = p.enemyDestroyer;
            kit.explosion = p.explosion;
            kit.explosionSmall = p.explosionSmall;
            kit.bunker = Lit("Surface_Bunker", new Color(0.3f, 0.3f, 0.31f), metallic: 0.2f, smooth: 0.25f);
            kit.armor = Lit("Surface_Armor", new Color(0.36f, 0.2f, 0.17f), metallic: 0.6f, smooth: 0.45f);
            kit.warning = Lit("Surface_Warning", new Color(1f, 0.25f, 0.15f), metallic: 0f, smooth: 0.3f, emission: new Color(1f, 0.2f, 0.1f) * 8f);
            // the planetary bases' buildings
            kit.colony = ColonyBuildings();
            kit.fort = FortBuildings(kit);
            Log($"base buildings: {kit.colony.Length} for colonies, {kit.fort.Length} for fortresses");
            return kit;
        }

        static Material ParticleAlpha(string name, Texture2D tex, Color tint)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", tint);
            if (tex) m.SetTexture("_BaseMap", tex);
            return SaveMat(name, m);
        }

        static Mesh FirstMesh(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Mesh m) return m;
            return null;
        }

        /// <summary>A 3x3 landing pad of Mega_City pad tiles (about 250 m across, for a 180 m
        /// frigate), two parked cargo craft and four beacons; its pivot is the pad's top surface.
        /// Null when the Mega_City pack isn't there (the sites then have a flat landing zone).</summary>
        static GameObject OutpostPrefab()
        {
            var tile = FirstMesh(PadMesh);
            var padMat = Load<Material>(MatDir + "/Kit_Mega_City_mi_landing_pad.mat");
            if (tile == null || padMat == null)
            {
                Log("outpost: no Mega_City landing pad (Kit not present): sites get a flat landing zone");
                AssetDatabase.DeleteAsset($"{PrefabDir}/Outpost.prefab");
                return null;
            }
            int layer = ObstacleLayer();
            var root = new GameObject("Outpost");
            root.layer = layer;
            var s = new Vector3(9f, 3f, 9f);
            var b = tile.bounds;
            Vector3 c = Vector3.Scale(b.center, s), size = Vector3.Scale(b.size, s);
            float top = c.y + size.y * 0.5f;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var t = new GameObject($"Pad {i + 1}{j + 1}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                    t.layer = layer;
                    t.transform.SetParent(root.transform, false);
                    t.transform.localScale = s;
                    t.transform.localPosition = new Vector3(i * size.x - c.x, -top, j * size.z - c.z);
                    t.GetComponent<MeshFilter>().sharedMesh = tile;
                    t.GetComponent<MeshRenderer>().sharedMaterial = padMat;
                    t.GetComponent<MeshCollider>().sharedMesh = tile;
                }
            float half = size.x * 1.5f;

            var craft = FirstMesh(CraftMesh);
            var craftMat = Load<Material>(MatDir + "/Kit_Mega_City_mi_ship.mat");
            if (craft != null && craftMat != null)
            {
                const float k = 1.6f;
                var cb = craft.bounds;
                foreach (var (x, z, yaw) in new[] { (-half * 0.72f, half * 0.7f, 35f), (half * 0.7f, half * 0.74f, -20f) })
                {
                    var g = new GameObject("Parked craft", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                    g.layer = layer;
                    g.transform.SetParent(root.transform, false);
                    g.transform.localScale = Vector3.one * k;
                    g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    g.transform.localPosition = new Vector3(x, -(cb.center.y - cb.extents.y) * k, z) - g.transform.localRotation * new Vector3(cb.center.x, 0f, cb.center.z) * k;
                    g.GetComponent<MeshFilter>().sharedMesh = craft;
                    g.GetComponent<MeshRenderer>().sharedMaterial = craftMat;
                    var bc = g.GetComponent<BoxCollider>();
                    bc.center = cb.center;
                    bc.size = cb.size;
                }
            }

            // beacons at the corners, so the pad can be found in haze and at night
            var beaconMat = Lit("Surface_Beacon", new Color(1f, 0.3f, 0.2f), metallic: 0f, smooth: 0.3f, emission: new Color(1f, 0.25f, 0.15f) * 12f);
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half : half, 0f, (i & 2) == 0 ? -half : half) * 0.97f;
                var bgo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripColliders(bgo);
                bgo.name = "Beacon";
                bgo.transform.SetParent(root.transform, false);
                bgo.transform.localPosition = corner + Vector3.up * 3f;
                bgo.transform.localScale = Vector3.one * 5f;
                bgo.GetComponent<MeshRenderer>().sharedMaterial = beaconMat;
                bgo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(bgo.transform, false);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.35f, 0.25f);
                l.range = 140f;
                l.intensity = 4f;
                l.shadows = LightShadows.None;
            }
            Log($"outpost: pad {size.x * 3f:F0} m x {size.z * 3f:F0} m");
            return SavePrefab(root, "Outpost");
        }
    }
}
